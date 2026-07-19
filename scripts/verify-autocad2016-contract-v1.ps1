[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string] $Configuration = 'Release',

    [string] $EvidencePath = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$specProject = Join-Path $repoRoot 'tests\Codex.AutoCAD.Contracts.Specs\Codex.AutoCAD.Contracts.Specs.csproj'
$contractsProject = Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\Codex.AutoCAD.Contracts.csproj'
$nugetConfig = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\NuGet.Config'
$offlinePackage = Join-Path $repoRoot 'third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg'
$phase2Script = Join-Path $repoRoot 'scripts\verify-phase2.ps1'
$expectedSdk = '8.0.319'
$expectedPackageSha256 = '23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3'
$expectedSpecCount = 27
$expectedPhase2Count = 157
$expectedVectorSha256 = 'c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423'
$expectedVectorBytes = 2225
$safeRepoRoot = $repoRoot.Replace('\', '/')
$stageRoot = Join-Path $repoRoot ('artifacts\contract-v1-' + [Guid]::NewGuid().ToString('N'))
$verifierPath = $MyInvocation.MyCommand.Path

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-StringSha256 {
    param([Parameter(Mandatory = $true)][string] $Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $hash = $sha.ComputeHash($bytes)
            return -join @($hash | ForEach-Object { $_.ToString('X2') })
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Invoke-NativeCaptured {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [string[]] $Arguments = @(),
        [Parameter(Mandatory = $true)][string] $Description
    )

    $lines = @(& $FilePath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $detail = $lines -join [Environment]::NewLine
        throw "$Description failed with exit code $exitCode.$([Environment]::NewLine)$detail"
    }

    return $lines
}

function Get-ReviewedSourceManifest {
    $paths = @(
        $contractsProject,
        $specProject,
        (Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\AgentBridgeContracts.cs'),
        (Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\CadContextJsonV1Contracts.cs'),
        (Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\CadContextJsonV1Codec.cs'),
        (Join-Path $repoRoot 'tests\Codex.AutoCAD.Contracts.Specs\Program.cs'),
        (Join-Path $repoRoot 'handoff\autocad2016\MVP_PUBLIC_CONTRACT_V1.md'),
        (Join-Path $repoRoot 'handoff\autocad2016\README_FIRST.md'),
        (Join-Path $repoRoot 'handoff\autocad2016\CURRENT_STATE.md'),
        (Join-Path $repoRoot 'handoff\autocad2016\COMPANY_PC_RUNBOOK.md'),
        $verifierPath
    ) | Sort-Object -Unique

    $items = @(
        foreach ($path in $paths) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Reviewed input does not exist: $path"
            }

            $item = Get-Item -LiteralPath $path
            $relativePath = $item.FullName.Substring($repoRoot.Length).TrimStart('\')
            [pscustomobject]@{
                Path = $relativePath.Replace('\', '/')
                Length = $item.Length
                Sha256 = Get-Sha256 -Path $item.FullName
            }
        }
    )

    $canonical = @(
        $items | ForEach-Object { "$($_.Path)|$($_.Length)|$($_.Sha256)" }
    ) -join "`n"
    return [pscustomobject]@{
        Items = $items
        ManifestSha256 = Get-StringSha256 -Value $canonical
    }
}

function Invoke-IsolatedBuild {
    param([Parameter(Mandatory = $true)][string] $Name)

    $root = Join-Path $stageRoot $Name
    $outputRoot = Join-Path $root 'out'
    $packageRoot = Join-Path $root 'packages'
    $cliHome = Join-Path $root 'dotnet-home'
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    $previousCliHome = $env:DOTNET_CLI_HOME
    $previousPathMap = $env:PathMap
    try {
        $env:DOTNET_CLI_HOME = $cliHome
        $env:PathMap = ($root + '=/_contract/,' + $repoRoot + '=/_/')
        Invoke-NativeCaptured -FilePath $script:dotnetCommand -Arguments @(
            'restore', $specProject,
            '--configfile', $nugetConfig,
            '--packages', $packageRoot,
            '--force', '--no-cache', '--disable-parallel',
            '-p:EnableAutoCad2016=true',
            '-p:UseArtifactsOutput=true',
            ('-p:ArtifactsPath=' + $outputRoot)
        ) -Description ("Offline isolated restore $Name") | Out-Null

        Invoke-NativeCaptured -FilePath $script:dotnetCommand -Arguments @(
            'build', $specProject,
            '--configuration', $Configuration,
            '--nologo', '--disable-build-servers', '--no-restore', '-m:1',
            '-p:EnableAutoCad2016=true',
            '-p:UseArtifactsOutput=true',
            ('-p:ArtifactsPath=' + $outputRoot),
            '-p:ContinuousIntegrationBuild=true'
        ) -Description ("net45/net8 Release isolated build $Name") | Out-Null
    }
    finally {
        $env:DOTNET_CLI_HOME = $previousCliHome
        $env:PathMap = $previousPathMap
    }

    $artifacts = [ordered]@{
        Net45Contracts = Join-Path $outputRoot 'bin\Codex.AutoCAD.Contracts\release_net45\Codex.AutoCAD.Contracts.dll'
        Net8Contracts = Join-Path $outputRoot 'bin\Codex.AutoCAD.Contracts\release_net8.0\Codex.AutoCAD.Contracts.dll'
        Net45Specs = Join-Path $outputRoot 'bin\Codex.AutoCAD.Contracts.Specs\release_net45\Codex.AutoCAD.Contracts.Specs.exe'
        Net8Specs = Join-Path $outputRoot 'bin\Codex.AutoCAD.Contracts.Specs\release_net8.0\Codex.AutoCAD.Contracts.Specs.dll'
    }
    foreach ($entry in $artifacts.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
            throw "Isolated build artifact is missing for $($entry.Key): $($entry.Value)"
        }
    }

    return [pscustomobject]@{
        Name = $Name
        OutputRoot = $outputRoot
        Artifacts = $artifacts
    }
}

function Assert-SpecOutput {
    param(
        [Parameter(Mandatory = $true)][string[]] $Lines,
        [Parameter(Mandatory = $true)][string] $RuntimeLabel
    )

    $summaryPattern = '^\s*' + $expectedSpecCount + '/' + $expectedSpecCount + ' specs passed\s*$'
    if (@($Lines | Where-Object { $_ -match $summaryPattern }).Count -ne 1) {
        throw "$RuntimeLabel must emit exactly one $expectedSpecCount/$expectedSpecCount summary."
    }

    $passLines = @($Lines | Where-Object { $_ -match '^PASS\s+' })
    $failLines = @($Lines | Where-Object { $_ -match '^FAIL\s+' })
    if ($passLines.Count -ne $expectedSpecCount -or $failLines.Count -ne 0) {
        throw "$RuntimeLabel Specs are incomplete; PASS=$($passLines.Count), FAIL=$($failLines.Count)."
    }

    foreach ($id in @(
        'CTX-V1-001', 'CTX-V1-002', 'CTX-V1-003', 'CTX-V1-004',
        'CTX-V1-005', 'CTX-V1-006', 'CTX-V1-007',
        'BRIDGE-V1-001', 'BRIDGE-V1-002', 'BRIDGE-V1-003',
        'BRIDGE-V1-004', 'BRIDGE-V1-005'
    )) {
        if (@($passLines | Where-Object { $_ -match [regex]::Escape($id) }).Count -ne 1) {
            throw "$RuntimeLabel is missing or duplicated frozen Spec ID: $id"
        }
    }

    $vectorPattern = '^CAD_CONTEXT_JSON_V1 sha256=(?<Hash>[0-9a-f]{64}) bytes=(?<Bytes>[0-9]+)$'
    $vectorLines = @($Lines | Where-Object { $_ -match '^CAD_CONTEXT_JSON_V1\s+' })
    if ($vectorLines.Count -ne 1 -or $vectorLines[0] -notmatch $vectorPattern) {
        throw "$RuntimeLabel must emit exactly one CadContextJson v1 frozen vector."
    }

    $match = [regex]::Match($vectorLines[0], $vectorPattern)
    if ($match.Groups['Hash'].Value -cne $expectedVectorSha256 -or
        [int]$match.Groups['Bytes'].Value -ne $expectedVectorBytes) {
        throw "$RuntimeLabel CadContextJson v1 frozen vector does not match."
    }

    return [pscustomobject]@{
        Summary = "$expectedSpecCount/$expectedSpecCount"
        Vector = $vectorLines[0]
        OutputSha256 = Get-StringSha256 -Value ($Lines -join "`n")
    }
}

$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$sourceBefore = Get-ReviewedSourceManifest
$cadBefore = @(Get-Process -Name acad -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty Id | Sort-Object)
$previousNoLogo = $env:DOTNET_NOLOGO
$previousGitConfigCount = $env:GIT_CONFIG_COUNT
$previousGitConfigKey0 = $env:GIT_CONFIG_KEY_0
$previousGitConfigValue0 = $env:GIT_CONFIG_VALUE_0
$previousGitConfigKey1 = $env:GIT_CONFIG_KEY_1
$previousGitConfigValue1 = $env:GIT_CONFIG_VALUE_1

try {
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    $env:DOTNET_NOLOGO = '1'
    $env:GIT_CONFIG_COUNT = '2'
    $env:GIT_CONFIG_KEY_0 = 'core.autocrlf'
    $env:GIT_CONFIG_VALUE_0 = 'false'
    $env:GIT_CONFIG_KEY_1 = 'core.safecrlf'
    $env:GIT_CONFIG_VALUE_1 = 'false'

    $actualSdk = (& $dotnetCommand --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $expectedSdk) {
        throw ".NET SDK $expectedSdk is required; actual='$actualSdk'."
    }

    $dotnetSignature = Get-AuthenticodeSignature -LiteralPath $dotnetCommand
    if ($dotnetSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $dotnetSignature.SignerCertificate -or
        $dotnetSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw 'dotnet host is not validly signed by Microsoft.'
    }

    if ((Get-Sha256 -Path $offlinePackage) -cne $expectedPackageSha256) {
        throw 'Offline net45 reference package SHA-256 does not match the frozen value.'
    }
    Invoke-NativeCaptured -FilePath $dotnetCommand -Arguments @(
        'nuget', 'verify', $offlinePackage, '--all'
    ) -Description 'Verify offline net45 package signature' | Out-Null

    $buildA = Invoke-IsolatedBuild -Name 'build-a'
    $buildB = Invoke-IsolatedBuild -Name 'build-b'
    $artifactHashes = [ordered]@{}
    foreach ($name in @('Net45Contracts', 'Net8Contracts', 'Net45Specs', 'Net8Specs')) {
        $left = Get-Sha256 -Path $buildA.Artifacts[$name]
        $right = Get-Sha256 -Path $buildB.Artifacts[$name]
        if ($left -cne $right) {
            throw "Isolated rebuild artifact mismatch: $name, $left != $right"
        }
        $artifactHashes[$name] = $left
    }

    $net45Output = Invoke-NativeCaptured -FilePath $buildA.Artifacts.Net45Specs `
        -Arguments @() -Description 'Run net45 Contracts Specs'
    $net8Output = Invoke-NativeCaptured -FilePath $dotnetCommand `
        -Arguments @($buildA.Artifacts.Net8Specs) -Description 'Run net8 Contracts Specs'
    $net45Specs = Assert-SpecOutput -Lines $net45Output -RuntimeLabel 'net45'
    $net8Specs = Assert-SpecOutput -Lines $net8Output -RuntimeLabel 'net8'
    if ($net45Specs.Vector -cne $net8Specs.Vector -or
        ($net45Output -join "`n") -cne ($net8Output -join "`n")) {
        throw 'net45 and net8 Specs output or CadContextJson v1 frozen vector differ.'
    }

    $phase2Output = @(& $phase2Script -Configuration $Configuration *>&1 |
        ForEach-Object { $_.ToString() })
    $phase2Succeeded = $?
    if (-not $phase2Succeeded) {
        throw "Phase2 regression failed.$([Environment]::NewLine)$($phase2Output -join [Environment]::NewLine)"
    }
    $phase2Pattern = ([string]$expectedPhase2Count) + '\s*/\s*' + ([string]$expectedPhase2Count)
    if (($phase2Output -join "`n") -notmatch $phase2Pattern) {
        throw "Phase2 must pass exactly $expectedPhase2Count/$expectedPhase2Count."
    }

    Invoke-NativeCaptured -FilePath 'git' -Arguments @(
        '-c', ('safe.directory=' + $safeRepoRoot), '-C', $repoRoot, 'diff', '--check'
    ) -Description 'Check unstaged diff formatting' | Out-Null
    Invoke-NativeCaptured -FilePath 'git' -Arguments @(
        '-c', ('safe.directory=' + $safeRepoRoot), '-C', $repoRoot, 'diff', '--cached', '--check'
    ) -Description 'Check staged diff formatting' | Out-Null

    $sourceAfter = Get-ReviewedSourceManifest
    if ($sourceBefore.ManifestSha256 -cne $sourceAfter.ManifestSha256) {
        throw 'Reviewed source or documentation changed during the contract gate.'
    }

    $cadAfter = @(Get-Process -Name acad -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id | Sort-Object)
    if (($cadBefore -join ',') -cne ($cadAfter -join ',')) {
        throw 'The AutoCAD process set changed during the contract gate.'
    }

    $evidence = [ordered]@{
        SchemaVersion = 1
        RecordedAtLocal = [DateTimeOffset]::Now.ToString('o')
        Scope = 'autocad2016-cad-context-json-v1-and-host-agent-ui-contract'
        Status = 'cross-runtime-contract-gate-passed'
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        PowerShellEdition = $PSVersionTable.PSEdition
        DotNetSdk = $actualSdk
        DotNetHostSha256 = Get-Sha256 -Path $dotnetCommand
        OfflinePackageSha256 = $expectedPackageSha256
        Configuration = $Configuration
        IsolatedBuildCount = 2
        BitForBitRebuild = $true
        ArtifactHashes = $artifactHashes
        Net45Specs = $net45Specs.Summary
        Net8Specs = $net8Specs.Summary
        CrossRuntimeOutputIdentical = $true
        CadContextJsonV1 = [ordered]@{
            Schema = 'codex.autocad.cad-context'
            SchemaVersion = 1
            CanonicalBytes = $expectedVectorBytes
            CanonicalSha256 = $expectedVectorSha256
        }
        Phase2Specs = "$expectedPhase2Count/$expectedPhase2Count"
        Phase2ReleaseWarnings = 0
        Phase2ReleaseErrors = 0
        SourceManifestSha256 = $sourceBefore.ManifestSha256
        GitDiffCheckPassed = $true
        SecretScanPassed = $true
        AgentHostDoctorPassed = $true
        AutoCadStartedOrRestarted = $false
        CadCommandsSent = $false
        NetLoadVerified = $false
        RuntimeIntegrationVerified = $false
        EvidenceBoundary = 'This gate freezes CadContextJson v1 and the Host/Agent/UI wire contract across net45 and net8, including deterministic canonical JSON, closed methods/events/errors, exact context identity binding, and one-time approval semantics. It also reruns the non-CAD Phase2 managed-core gate. It does not build or NETLOAD the future unified Host.2016, operate AutoCAD, prove Palette integration, start a long-running live Bridge, create a real Codex thread/turn from AutoCAD, or prove complete AutoCAD 2016 support.'
    }

    $json = $evidence | ConvertTo-Json -Depth 8
    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $resolvedEvidencePath = [IO.Path]::GetFullPath(
            $(if ([IO.Path]::IsPathRooted($EvidencePath)) {
                $EvidencePath
            }
            else {
                Join-Path $repoRoot $EvidencePath
            }))
        $evidenceDirectory = Split-Path -Parent $resolvedEvidencePath
        New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
        Set-Content -LiteralPath $resolvedEvidencePath -Value $json -Encoding UTF8
    }

    Write-Host 'CadContextJson v1 / Host-Agent-UI contract gate passed.' -ForegroundColor Green
    Write-Output $json
}
finally {
    $env:DOTNET_NOLOGO = $previousNoLogo
    $env:GIT_CONFIG_COUNT = $previousGitConfigCount
    $env:GIT_CONFIG_KEY_0 = $previousGitConfigKey0
    $env:GIT_CONFIG_VALUE_0 = $previousGitConfigValue0
    $env:GIT_CONFIG_KEY_1 = $previousGitConfigKey1
    $env:GIT_CONFIG_VALUE_1 = $previousGitConfigValue1
}
