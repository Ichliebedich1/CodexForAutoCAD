[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string] $Configuration = "Release",

    [string] $EvidencePath = "",

    [switch] $Worker,

    [string] $WorkerStageRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$safeRepoRoot = $repoRoot.Replace("\", "/")
$scriptPath = $MyInvocation.MyCommand.Path
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$solutionPath = Join-Path $repoRoot "Codex.AutoCAD.sln"
$clientProject = Join-Path $repoRoot "src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj"
$clientSpecsProject = Join-Path $repoRoot "tests\Codex.AutoCAD.Bridge.Client.Specs\Codex.AutoCAD.Bridge.Client.Specs.csproj"
$testServerProject = Join-Path $repoRoot "tests\Codex.AutoCAD.Bridge.Client.TestServer\Codex.AutoCAD.Bridge.Client.TestServer.csproj"
$bridgeSpecsProject = Join-Path $repoRoot "tests\Codex.AutoCAD.Bridge.Specs\Codex.AutoCAD.Bridge.Specs.csproj"
$phase2Verifier = Join-Path $repoRoot "scripts\verify-phase2.ps1"
$nugetConfig = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\NuGet.Config"
$expectedSdk = "8.0.319"
$expectedClientSpecs = 30
$expectedBridgeSpecs = 39
$expectedPhase2Specs = 322

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string] $Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "")
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Get-PackageLockSnapshots {
    $snapshots = [ordered]@{}
    foreach ($root in @(
        (Join-Path $repoRoot "src"),
        (Join-Path $repoRoot "tests")
    )) {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File `
            -Filter "packages.lock.json" | Where-Object {
                $_.FullName -notmatch '\\(?:bin|obj)\\'
            })) {
            $snapshots[$file.FullName] = [IO.File]::ReadAllBytes($file.FullName)
        }
    }
    return $snapshots
}

function Restore-PackageLockSnapshots {
    param([Parameter(Mandatory = $true)] $Snapshots)

    foreach ($entry in $Snapshots.GetEnumerator()) {
        [IO.File]::WriteAllBytes(
            [string]$entry.Key,
            [byte[]]$entry.Value)
    }
}

function Get-RelativePathText {
    param([Parameter(Mandatory = $true)][string] $Path)

    $rootWithSlash = $repoRoot.TrimEnd('\') + '\'
    $rootUri = [Uri]::new($rootWithSlash)
    $pathUri = [Uri]::new([IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace("\", "/")
}

function Get-SourceManifest {
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($file in @(
        $solutionPath,
        $scriptPath,
        $phase2Verifier,
        (Join-Path $repoRoot "scripts\verify-autocad2016-auth-compat.ps1"),
        (Join-Path $repoRoot "scripts\verify-autocad2016-contract-v1.ps1"),
        (Join-Path $repoRoot "scripts\verify-autocad2016-agent-bootstrap-stage.ps1"),
        (Join-Path $repoRoot "src\Codex.AutoCAD.Bridge\LengthPrefixedFrameCodec.cs"),
        (Join-Path $repoRoot "tests\Codex.AutoCAD.Bridge.Specs\Program.cs")
    )) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Bridge Client阶段缺少受审文件：$file"
        }
        $paths.Add([IO.Path]::GetFullPath($file))
    }

    foreach ($directory in @(
        (Split-Path -Parent $clientProject),
        (Split-Path -Parent $clientSpecsProject),
        (Split-Path -Parent $testServerProject)
    )) {
        foreach ($file in @(Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object {
            $_.FullName -notmatch '\\(?:bin|obj)\\' -and
            $_.Extension -in @('.cs', '.csproj')
        })) {
            $paths.Add($file.FullName)
        }
    }

    $entries = [ordered]@{}
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($path in @($paths | Sort-Object -Unique)) {
        $relative = Get-RelativePathText -Path $path
        $hash = Get-Sha256 -Path $path
        $entries[$relative] = $hash
        $lines.Add($relative + "=" + $hash)
    }

    return [pscustomobject]@{
        FileCount = $entries.Count
        Files = $entries
        Sha256 = Get-TextSha256 -Value ($lines -join "`n")
    }
}

function Get-ProcessIds {
    param([Parameter(Mandatory = $true)][string] $Name)

    return @(Get-Process -Name $Name -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id |
        Sort-Object)
}

function Assert-SameProcessSet {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][int[]] $Before,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][int[]] $After,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $difference = @(Compare-Object -ReferenceObject $Before -DifferenceObject $After)
    if ($difference.Count -ne 0) {
        throw "$Label 进程集合发生变化。"
    }
}

function Invoke-NativeCaptured {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $Description
    )

    Write-Host "`n==> $Description" -ForegroundColor Cyan
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Host $line
    }
    if ($exitCode -ne 0) {
        throw "$Description 失败，退出码：$exitCode"
    }
    return $output
}

function Assert-FileExists {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "缺少隔离构建产物：$Path"
    }
}

function Assert-SpecSummary {
    param(
        [Parameter(Mandatory = $true)][string[]] $Lines,
        [Parameter(Mandatory = $true)][int] $Expected,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $pattern = '^\s*' + $Expected + '\s*/\s*' + $Expected + '\s+specs passed\s*$'
    if (@($Lines | Where-Object { $_ -match $pattern }).Count -ne 1) {
        throw "$Label 必须精确通过 $Expected/$Expected。"
    }
}

function Invoke-IsolatedBuild {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $Root
    )

    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    $common = @(
        "--configuration", $Configuration,
        "--nologo",
        "--disable-build-servers",
        "--artifacts-path", $Root,
        "--warnaserror",
        "-m:1",
        "-p:RestoreConfigFile=$nugetConfig",
        "-p:ContinuousIntegrationBuild=true",
        "-p:Deterministic=true",
        "-p:PathMap=$repoRoot=Z:\repo%2C$Root=Z:\artifacts"
    )

    Invoke-NativeCaptured -FilePath $dotnetCommand -Arguments (@(
        "build", $clientSpecsProject,
        "-p:EnableAutoCad2016=true"
    ) + $common) -Description "$Label 双目标 Bridge Client/Specs 构建" | Out-Null

    Invoke-NativeCaptured -FilePath $dotnetCommand -Arguments (@(
        "build", $testServerProject
    ) + $common) -Description "$Label .NET 8 TestServer 构建" | Out-Null

    Invoke-NativeCaptured -FilePath $dotnetCommand -Arguments (@(
        "build", $bridgeSpecsProject
    ) + $common) -Description "$Label .NET 8 Bridge回归构建" | Out-Null

    $artifacts = [ordered]@{
        Net45Client = Join-Path $Root "bin\Codex.AutoCAD.Bridge.Client\release_net45\Codex.AutoCAD.Bridge.Client.dll"
        Net8Client = Join-Path $Root "bin\Codex.AutoCAD.Bridge.Client\release_net8.0\Codex.AutoCAD.Bridge.Client.dll"
        Net45Specs = Join-Path $Root "bin\Codex.AutoCAD.Bridge.Client.Specs\release_net45\Codex.AutoCAD.Bridge.Client.Specs.exe"
        Net8Specs = Join-Path $Root "bin\Codex.AutoCAD.Bridge.Client.Specs\release_net8.0\Codex.AutoCAD.Bridge.Client.Specs.dll"
        TestServerDll = Join-Path $Root "bin\Codex.AutoCAD.Bridge.Client.TestServer\release\Codex.AutoCAD.Bridge.Client.TestServer.dll"
        TestServerExe = Join-Path $Root "bin\Codex.AutoCAD.Bridge.Client.TestServer\release\Codex.AutoCAD.Bridge.Client.TestServer.exe"
        BridgeDll = Join-Path $Root "bin\Codex.AutoCAD.Bridge\release\Codex.AutoCAD.Bridge.dll"
        BridgeSpecs = Join-Path $Root "bin\Codex.AutoCAD.Bridge.Specs\release\Codex.AutoCAD.Bridge.Specs.dll"
    }
    foreach ($path in $artifacts.Values) {
        Assert-FileExists -Path $path
    }

    $hashes = [ordered]@{}
    foreach ($name in @(
        "Net45Client", "Net8Client", "Net45Specs", "Net8Specs", "TestServerDll", "BridgeDll"
    )) {
        $hashes[$name] = Get-Sha256 -Path $artifacts[$name]
    }

    return [pscustomobject]@{
        Artifacts = $artifacts
        Hashes = $hashes
    }
}

function Invoke-Worker {
    if ([string]::IsNullOrWhiteSpace($WorkerStageRoot)) {
        throw "Worker必须提供独立阶段目录。"
    }

    $workerRoot = [IO.Path]::GetFullPath($WorkerStageRoot)
    New-Item -ItemType Directory -Path $workerRoot -Force | Out-Null
    $dotnetHome = Join-Path $workerRoot "dotnet-home"
    $nugetPackages = Join-Path $workerRoot "packages"
    New-Item -ItemType Directory -Path $dotnetHome -Force | Out-Null
    New-Item -ItemType Directory -Path $nugetPackages -Force | Out-Null

    $previousDotnetHome = $env:DOTNET_CLI_HOME
    $previousNugetPackages = $env:NUGET_PACKAGES
    $previousTestServer = $env:CODEX_BRIDGE_TEST_SERVER_EXE
    $cadBefore = @(Get-ProcessIds -Name "acad")
    $testServerBefore = @(Get-ProcessIds -Name "Codex.AutoCAD.Bridge.Client.TestServer")
    $packageLockSnapshots = Get-PackageLockSnapshots
    $sourceBefore = Get-SourceManifest

    try {
        $env:DOTNET_CLI_HOME = $dotnetHome
        $env:NUGET_PACKAGES = $nugetPackages
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

        $actualSdk = (& $dotnetCommand --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $expectedSdk) {
            throw "需要 .NET SDK $expectedSdk，实际：$actualSdk"
        }

        $buildA = Invoke-IsolatedBuild -Label "build-a" -Root (Join-Path $workerRoot "build-a")
        $buildB = Invoke-IsolatedBuild -Label "build-b" -Root (Join-Path $workerRoot "build-b")
        foreach ($name in $buildA.Hashes.Keys) {
            if ($buildA.Hashes[$name] -cne $buildB.Hashes[$name]) {
                throw "Bridge Client隔离双构建产物不一致：$name"
            }
        }

        $env:CODEX_BRIDGE_TEST_SERVER_EXE = $buildA.Artifacts.TestServerExe
        $net45Output = Invoke-NativeCaptured -FilePath $buildA.Artifacts.Net45Specs `
            -Arguments @() -Description "运行 net45 Bridge Client Specs"
        $net8Output = Invoke-NativeCaptured -FilePath $dotnetCommand `
            -Arguments @($buildA.Artifacts.Net8Specs) -Description "运行 net8 Bridge Client Specs"
        Assert-SpecSummary -Lines $net45Output -Expected $expectedClientSpecs -Label "net45 Bridge Client"
        Assert-SpecSummary -Lines $net8Output -Expected $expectedClientSpecs -Label "net8 Bridge Client"
        if (($net45Output -join "`n") -cne ($net8Output -join "`n")) {
            throw "net45与net8 Bridge Client Specs输出不一致。"
        }

        $bridgeOutput = Invoke-NativeCaptured -FilePath $dotnetCommand `
            -Arguments @($buildA.Artifacts.BridgeSpecs) -Description "运行 .NET 8 Bridge回归"
        Assert-SpecSummary -Lines $bridgeOutput -Expected $expectedBridgeSpecs -Label "Bridge回归"

        Write-Host "`n==> 运行扩展 Phase 2 门禁" -ForegroundColor Cyan
        $phase2Output = @(& $phase2Verifier -Configuration $Configuration *>&1 |
            ForEach-Object { $_.ToString() })
        $phase2Succeeded = $?
        foreach ($line in $phase2Output) {
            Write-Host $line
        }
        if (-not $phase2Succeeded) {
            throw "扩展 Phase 2 门禁失败。"
        }
        $phase2Pattern = ([string]$expectedPhase2Specs) + '\s*/\s*' + ([string]$expectedPhase2Specs)
        if (($phase2Output -join "`n") -notmatch $phase2Pattern) {
            throw "Phase 2必须精确通过 $expectedPhase2Specs/$expectedPhase2Specs。"
        }

        Invoke-NativeCaptured -FilePath "git" -Arguments @(
            "-c", "safe.directory=$safeRepoRoot", "-C", $repoRoot, "diff", "--check"
        ) -Description "检查未暂存差异格式" | Out-Null
        Invoke-NativeCaptured -FilePath "git" -Arguments @(
            "-c", "safe.directory=$safeRepoRoot", "-C", $repoRoot, "diff", "--cached", "--check"
        ) -Description "检查已暂存差异格式" | Out-Null

        $sourceAfter = Get-SourceManifest
        if ($sourceBefore.Sha256 -cne $sourceAfter.Sha256) {
            throw "Bridge Client阶段验证期间受审源码发生变化。"
        }

        $cadAfter = @(Get-ProcessIds -Name "acad")
        $testServerAfter = @(Get-ProcessIds -Name "Codex.AutoCAD.Bridge.Client.TestServer")
        Assert-SameProcessSet -Before $cadBefore -After $cadAfter -Label "AutoCAD"
        Assert-SameProcessSet -Before $testServerBefore -After $testServerAfter -Label "Bridge TestServer"

        $evidence = [ordered]@{
            schemaVersion = 1
            recordedAtLocal = [DateTimeOffset]::Now.ToString("o")
            scope = "autocad2016-net45-authenticated-bridge-client"
            status = "worker-gate-passed"
            powerShellVersion = $PSVersionTable.PSVersion.ToString()
            powerShellEdition = [string]$PSVersionTable.PSEdition
            dotNetSdk = $actualSdk
            configuration = $Configuration
            isolatedBuildCount = 2
            bitForBitRebuild = $true
            artifactHashes = $buildA.Hashes
            net45Specs = "$expectedClientSpecs/$expectedClientSpecs"
            net8Specs = "$expectedClientSpecs/$expectedClientSpecs"
            crossRuntimeOutputIdentical = $true
            bridgeRegressionSpecs = "$expectedBridgeSpecs/$expectedBridgeSpecs"
            phase2Specs = "$expectedPhase2Specs/$expectedPhase2Specs"
            phase2ReleaseWarnings = 0
            phase2ReleaseErrors = 0
            sourceManifestFileCount = $sourceAfter.FileCount
            sourceManifestSha256 = $sourceAfter.Sha256
            sourceFiles = $sourceAfter.Files
            gitDiffCheckPassed = $true
            secretScanPassed = $true
            agentHostDoctorPassed = $true
            noResidualTestServerProcesses = $true
            autoCadProcessSetChanged = $false
            autoCadStartedOrRestarted = $false
            cadCommandsSent = $false
            netLoadVerified = $false
            autoCadLiveEvidence = $false
            evidenceBoundary = "This worker gate proves deterministic net45/net8 Bridge Client builds, authenticated named-pipe requests, context identity binding, assistant event delivery, turn-terminal consumption and late-event rejection, strict JSON/frame rejection, replay protection, lifecycle fail-closed behavior, Bridge regression, and the non-CAD Phase2 managed-core gate. It does not load AutoCAD, connect the unified Host.2016 to a long-running AgentHost, or prove a real Codex CAD conversation."
        }

        $resolvedEvidencePath = if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
            Join-Path $workerRoot "verification.json"
        }
        else {
            [IO.Path]::GetFullPath($EvidencePath)
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidencePath) -Force | Out-Null
        [IO.File]::WriteAllText(
            $resolvedEvidencePath,
            ($evidence | ConvertTo-Json -Depth 20),
            [Text.UTF8Encoding]::new($false)
        )
        Write-Host "BRIDGE_CLIENT_WORKER_EVIDENCE=$resolvedEvidencePath"
    }
    finally {
        Restore-PackageLockSnapshots -Snapshots $packageLockSnapshots
        $env:DOTNET_CLI_HOME = $previousDotnetHome
        $env:NUGET_PACKAGES = $previousNugetPackages
        $env:CODEX_BRIDGE_TEST_SERVER_EXE = $previousTestServer
    }
}

function Invoke-ChildWorker {
    param(
        [Parameter(Mandatory = $true)][string] $PowerShellPath,
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $ChildEvidencePath,
        [Parameter(Mandatory = $true)][string] $ChildStageRoot,
        [Parameter(Mandatory = $true)][string] $LogPath
    )

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $scriptPath,
        "-Worker",
        "-Configuration", $Configuration,
        "-EvidencePath", $ChildEvidencePath,
        "-WorkerStageRoot", $ChildStageRoot
    )
    $output = @(& $PowerShellPath @arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null
    [IO.File]::WriteAllLines($LogPath, $output, [Text.UTF8Encoding]::new($false))
    foreach ($line in $output) {
        Write-Host "[$Label] $line"
    }
    if ($exitCode -ne 0) {
        throw "$Label Bridge Client worker失败，退出码：$exitCode"
    }
    if (-not (Test-Path -LiteralPath $ChildEvidencePath -PathType Leaf)) {
        throw "$Label worker未产生 evidence。"
    }
}

function Get-NormalizedWorkerEvidence {
    param([Parameter(Mandatory = $true)] $Evidence)

    return [ordered]@{
        schemaVersion = $Evidence.schemaVersion
        scope = $Evidence.scope
        status = $Evidence.status
        dotNetSdk = $Evidence.dotNetSdk
        configuration = $Evidence.configuration
        isolatedBuildCount = $Evidence.isolatedBuildCount
        bitForBitRebuild = $Evidence.bitForBitRebuild
        artifactHashes = $Evidence.artifactHashes
        net45Specs = $Evidence.net45Specs
        net8Specs = $Evidence.net8Specs
        crossRuntimeOutputIdentical = $Evidence.crossRuntimeOutputIdentical
        bridgeRegressionSpecs = $Evidence.bridgeRegressionSpecs
        phase2Specs = $Evidence.phase2Specs
        phase2ReleaseWarnings = $Evidence.phase2ReleaseWarnings
        phase2ReleaseErrors = $Evidence.phase2ReleaseErrors
        sourceManifestFileCount = $Evidence.sourceManifestFileCount
        sourceManifestSha256 = $Evidence.sourceManifestSha256
        sourceFiles = $Evidence.sourceFiles
        gitDiffCheckPassed = $Evidence.gitDiffCheckPassed
        secretScanPassed = $Evidence.secretScanPassed
        agentHostDoctorPassed = $Evidence.agentHostDoctorPassed
        noResidualTestServerProcesses = $Evidence.noResidualTestServerProcesses
        autoCadProcessSetChanged = $Evidence.autoCadProcessSetChanged
        autoCadStartedOrRestarted = $Evidence.autoCadStartedOrRestarted
        cadCommandsSent = $Evidence.cadCommandsSent
        netLoadVerified = $Evidence.netLoadVerified
        autoCadLiveEvidence = $Evidence.autoCadLiveEvidence
        evidenceBoundary = $Evidence.evidenceBoundary
    }
}

if ($Worker) {
    Invoke-Worker
    exit 0
}

$stageRoot = Join-Path $repoRoot (
    "artifacts\autocad2016-bridge-client-stage-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
$pwshCommand = (Get-Command pwsh -ErrorAction Stop).Source
$windowsPowerShellCommand = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
if (-not (Test-Path -LiteralPath $windowsPowerShellCommand -PathType Leaf)) {
    throw "未找到 Windows PowerShell 5.1。"
}

$cadBefore = @(Get-ProcessIds -Name "acad")
$ps7EvidencePath = Join-Path $stageRoot "powershell7-verification.json"
$ps51EvidencePath = Join-Path $stageRoot "windowspowershell51-verification.json"
$ps7LogPath = Join-Path $stageRoot "powershell7.log"
$ps51LogPath = Join-Path $stageRoot "windowspowershell51.log"

Invoke-ChildWorker -PowerShellPath $pwshCommand -Label "PowerShell7" `
    -ChildEvidencePath $ps7EvidencePath -ChildStageRoot (Join-Path $stageRoot "ps7") `
    -LogPath $ps7LogPath
Invoke-ChildWorker -PowerShellPath $windowsPowerShellCommand -Label "WindowsPowerShell51" `
    -ChildEvidencePath $ps51EvidencePath -ChildStageRoot (Join-Path $stageRoot "ps51") `
    -LogPath $ps51LogPath

$ps7Evidence = Get-Content -LiteralPath $ps7EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
$ps51Evidence = Get-Content -LiteralPath $ps51EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
$ps7Normalized = Get-NormalizedWorkerEvidence -Evidence $ps7Evidence
$ps51Normalized = Get-NormalizedWorkerEvidence -Evidence $ps51Evidence
$ps7Comparable = $ps7Normalized | ConvertTo-Json -Depth 20 -Compress
$ps51Comparable = $ps51Normalized | ConvertTo-Json -Depth 20 -Compress
if ($ps7Comparable -cne $ps51Comparable) {
    throw "PowerShell 7与Windows PowerShell 5.1 Bridge Client evidence不一致。"
}

$cadAfter = @(Get-ProcessIds -Name "acad")
Assert-SameProcessSet -Before $cadBefore -After $cadAfter -Label "AutoCAD"

$finalEvidence = [ordered]@{
    schemaVersion = 1
    recordedAtLocal = [DateTimeOffset]::Now.ToString("o")
    scope = "autocad2016-net45-authenticated-bridge-client-stage"
    status = "dual-shell-gate-passed"
    powerShell7 = [ordered]@{
        version = $ps7Evidence.powerShellVersion
        edition = $ps7Evidence.powerShellEdition
        logSha256 = Get-Sha256 -Path $ps7LogPath
    }
    windowsPowerShell51 = [ordered]@{
        version = $ps51Evidence.powerShellVersion
        edition = $ps51Evidence.powerShellEdition
        logSha256 = Get-Sha256 -Path $ps51LogPath
    }
    normalizedSummarySha256 = Get-TextSha256 -Value $ps7Comparable
    result = $ps7Normalized
    autoCadProcessSetChanged = $false
    autoCadStartedOrRestarted = $false
    cadCommandsSent = $false
    netLoadVerified = $false
    autoCadLiveEvidence = $false
    evidenceBoundary = "PowerShell 7 and Windows PowerShell 5.1 independently passed two isolated deterministic builds, net45/net8 Bridge Client $expectedClientSpecs/$expectedClientSpecs, Bridge $expectedBridgeSpecs/$expectedBridgeSpecs, and Phase2 $expectedPhase2Specs/$expectedPhase2Specs. Valid turn terminal events consume the active turn identity, and later events for that turn are rejected fail-closed. This is non-CAD evidence and does not prove unified Host.2016 NETLOAD, a live AgentHost connection from AutoCAD, or a real Codex CAD conversation."
}

$resolvedFinalEvidencePath = if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    Join-Path $stageRoot "verification.json"
}
else {
    [IO.Path]::GetFullPath($EvidencePath)
}
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedFinalEvidencePath) -Force | Out-Null
[IO.File]::WriteAllText(
    $resolvedFinalEvidencePath,
    ($finalEvidence | ConvertTo-Json -Depth 30),
    [Text.UTF8Encoding]::new($false)
)
Write-Host "Bridge Client双PowerShell阶段门禁通过。" -ForegroundColor Green
Write-Host "BRIDGE_CLIENT_STAGE_EVIDENCE=$resolvedFinalEvidencePath"
