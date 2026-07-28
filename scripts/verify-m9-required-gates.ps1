[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string] $Configuration = "Release",

    [string] $CodexExecutable,

    [string] $AutoCad2016Dir,

    [string] $ArtifactBase,

    [ValidateRange(0, 40)]
    [double] $MinimumFreeGiB = 40,

    [switch] $SelfTestOnly
)

# This file is intentionally ASCII so Windows PowerShell 5.1 can parse it
# without depending on the machine legacy code page.
#
# M9.3 is a final local gate aggregator. The existing verify-all-gates.ps1
# remains the correlated M4 prerequisite suite because the M4 candidate
# packager consumes its readiness and suite evidence. This script adds the
# M9.1/M9.2 inputs and then packages and verifies the exact M4 read-only
# candidate. It never starts or commands AutoCAD.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")

$requiredPhase2Projects = @(
    "Codex.AutoCAD.Contracts.Specs",
    "Codex.AutoCAD.Ipc.Specs",
    "Codex.AutoCAD.Security.Specs",
    "Codex.AutoCAD.AppServer.Specs",
    "Codex.AutoCAD.Bridge.Specs",
    "Codex.AutoCAD.Bridge.Client.Specs",
    "Codex.AutoCAD.AgentRuntime.Specs",
    "Codex.AutoCAD.Chat.Specs",
    "Codex.AutoCAD.Host.2016.Mvp.Specs",
    "Codex.AutoCAD.Host.2016.ReadOnlyContext.Specs",
    "Codex.AutoCAD.Host.2016.V2.Specs"
)

$requiredNestedGates = @(
    "build-safety-powershell7",
    "build-safety-windowspowershell51",
    "phase2-powershell7",
    "phase2-windowspowershell51",
    "agent-bootstrap",
    "auth-compat",
    "r201-host-build",
    "m9-sbom-licenses",
    "m4-readiness"
)

$requiredCoverage = @(
    "Contracts",
    "Ipc",
    "Bridge",
    "Launcher",
    "AppServer",
    "Runtime",
    "HostMvp",
    "Security",
    "ForbiddenApi",
    "SecretScan",
    "CandidateManifest",
    "CandidateDoctor"
)

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required evidence file is missing."
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-BytesSha256 {
    param([Parameter(Mandatory = $true)][byte[]] $Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace("-", "")
    }
    finally {
        $sha.Dispose()
    }
}

function Read-StrictJson {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Label,
        [int] $MaximumBytes = 8388608
    )

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label cannot be a reparse file."
    }
    if ($item.Length -lt 2 -or $item.Length -gt $MaximumBytes) {
        throw "$Label size is outside the accepted range."
    }

    $stream = New-Object IO.FileStream(
        $item.FullName,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    $bytes = $null
    try {
        $bytes = New-Object byte[] ([int] $stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -le 0) {
                throw "$Label did not read to EOF."
            }
            $offset += $read
        }
        $utf8 = New-Object Text.UTF8Encoding($false, $true)
        try {
            $text = $utf8.GetString($bytes)
        }
        catch {
            throw "$Label is not strict UTF-8."
        }
        if ($text.Length -gt 0 -and $text[0] -eq [char] 0xFEFF) {
            $text = $text.Substring(1)
        }
        try {
            $json = $text | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "$Label is not valid JSON."
        }
        return [pscustomobject]@{
            Json = $json
            Sha256 = Get-BytesSha256 -Bytes $bytes
        }
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
        $stream.Dispose()
    }
}

function Assert-JsonBoolean {
    param(
        $Value,
        [bool] $Expected,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if ($Value -isnot [bool] -or [bool] $Value -ne $Expected) {
        throw "$Label must be the expected JSON boolean."
    }
}

function Test-JsonInteger {
    param($Value)

    return (
        $Value -is [byte] -or $Value -is [sbyte] -or
        $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64])
}

function Convert-SpecSummary {
    param(
        [Parameter(Mandatory = $true)][string] $Value,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $match = [regex]::Match($Value, "^(?<Passed>[1-9][0-9]*)/(?<Total>[1-9][0-9]*)$")
    if (-not $match.Success) {
        throw "$Label is not a valid spec summary."
    }
    $passed = [int] $match.Groups["Passed"].Value
    $total = [int] $match.Groups["Total"].Value
    if ($passed -ne $total) {
        throw "$Label contains failed specs."
    }
    return $total
}

function Assert-ExactNameSet {
    param(
        [Parameter(Mandatory = $true)][string[]] $Expected,
        [Parameter(Mandatory = $true)][string[]] $Actual,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $duplicates = @(
        $Actual | Group-Object -CaseSensitive | Where-Object { $_.Count -ne 1 }
    )
    $difference = @(
        Compare-Object -CaseSensitive `
            -ReferenceObject @($Expected | Sort-Object) `
            -DifferenceObject @($Actual | Sort-Object)
    )
    if ($duplicates.Count -ne 0 -or $difference.Count -ne 0) {
        throw "$Label does not match the required set."
    }
}

function Assert-Phase2Evidence {
    param(
        [Parameter(Mandatory = $true)] $Evidence,
        [Parameter(Mandatory = $true)][string] $ExpectedEdition
    )

    if ([string] $Evidence.Scope -cne "phase2-managed-core-gate" -or
        [string] $Evidence.Status -cne "automated-gate-passed" -or
        [string] $Evidence.PowerShellEdition -cne $ExpectedEdition -or
        [string] $Evidence.Configuration -cne "Release") {
        throw "Phase2 evidence identity is invalid."
    }
    foreach ($property in @(
        "SolutionBuildPassed",
        "HostForbiddenApiScanPassed",
        "AgentHostDoctorPassed",
        "GitDiffCheckPassed",
        "BasicSecretScanPassed",
        "ConditionalLockFileRestored"
    )) {
        Assert-JsonBoolean $Evidence.$property $true "Phase2.$property"
    }

    $projects = @($Evidence.SpecProjects)
    $names = @($projects | ForEach-Object { [string] $_.Name })
    Assert-ExactNameSet -Expected $requiredPhase2Projects -Actual $names `
        -Label "Phase2 projects"

    $total = 0
    foreach ($project in $projects) {
        if (-not (Test-JsonInteger $project.Passed) -or
            -not (Test-JsonInteger $project.Total) -or
            -not (Test-JsonInteger $project.Failed) -or
            [int] $project.Total -le 0 -or
            [int] $project.Passed -ne [int] $project.Total -or
            [int] $project.Failed -ne 0) {
            throw "Phase2 contains an invalid project result."
        }
        $total += [int] $project.Total
    }
    if (-not (Test-JsonInteger $Evidence.TotalSpecs) -or
        $total -ne [int] $Evidence.TotalSpecs) {
        throw "Phase2 dynamic total is inconsistent."
    }

    return [pscustomobject]@{
        Total = $total
        Projects = @(
            $projects | Sort-Object Name | ForEach-Object {
                [ordered]@{
                    Name = [string] $_.Name
                    Total = [int] $_.Total
                }
            }
        )
    }
}

function Get-SourceState {
    $head = @(& git -C $repoRoot rev-parse HEAD 2>&1 | ForEach-Object { [string] $_ }) |
        Select-Object -Last 1
    if ($LASTEXITCODE -ne 0 -or $head -cnotmatch "^[0-9a-f]{40}$") {
        throw "Cannot resolve the source commit."
    }
    $status = @(& git -C $repoRoot status --porcelain=v1 -uall 2>&1 |
        ForEach-Object { [string] $_ })
    if ($LASTEXITCODE -ne 0) {
        throw "Cannot inspect the source worktree."
    }
    return [pscustomobject]@{
        Head = $head.Trim()
        Dirty = ($status.Count -ne 0)
    }
}

function Get-RelevantProcessKeys {
    return @(
        Get-Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.ProcessName -ieq "acad" -or
                $_.ProcessName -ieq "Codex.AutoCAD.AgentHost" -or
                $_.ProcessName -ieq "Codex.AutoCAD.AgentLauncher.FakeAgentHost" -or
                $_.ProcessName -like "CodexLauncherFake-*" -or
                $_.ProcessName -like "CodexAgentServiceFake-*"
            } |
            ForEach-Object { $_.ProcessName.ToLowerInvariant() + ":" + $_.Id } |
            Sort-Object -Unique
    )
}

function Write-SafeStageOutput {
    param($Value)

    if ($null -eq $Value) {
        return
    }
    $line = [string] $Value
    if ($line -match "^[A-Z][A-Z0-9_.-]*=(?:passed|failed|true|false|[0-9]+)$" -or
        $line -match "^\s*[0-9]+\s*/\s*[0-9]+\s+specs passed\s*$") {
        Write-Output $line
    }
}

function Invoke-Stage {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Script,
        [string[]] $Arguments = @()
    )

    $shell = (Get-Process -Id $PID).Path
    Write-Host ("M9_REQUIRED_GATE_STAGE_START=" + $Name)
    $raw = @(& $shell -NoProfile -NonInteractive -File $Script @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    foreach ($line in $raw) {
        Write-SafeStageOutput $line
    }
    if ($exitCode -ne 0) {
        throw "M9 required stage failed: $Name (exit $exitCode)."
    }
    Write-Host ("M9_REQUIRED_GATE_STAGE_PASS=" + $Name)
}

function Resolve-Base {
    param([string] $Requested)

    $value = $Requested
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = [Environment]::GetEnvironmentVariable(
            "CODEX_AUTOCAD_ARTIFACT_BASE",
            "Process")
    }
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = [Environment]::GetEnvironmentVariable(
            "CODEX_AUTOCAD_ARTIFACT_BASE",
            "User")
    }
    if ([string]::IsNullOrWhiteSpace($value) -or
        $value -match "^\\\\" -or
        -not [IO.Path]::IsPathRooted($value)) {
        throw "ArtifactBase must be a local absolute path."
    }
    $resolved = [IO.Path]::GetFullPath($value).TrimEnd("\", "/")
    $systemRoot = [IO.Path]::GetPathRoot([Environment]::GetFolderPath("Windows"))
    if ([IO.Path]::GetPathRoot($resolved) -ieq $systemRoot) {
        throw "ArtifactBase cannot be on the Windows system drive."
    }
    return $resolved
}

function Get-CandidateIdFromLogicalRoot {
    param([Parameter(Mandatory = $true)][string] $LogicalRoot)

    $match = [regex]::Match(
        $LogicalRoot,
        "^artifacts/(?<Id>[a-z0-9][a-z0-9._-]{0,159})$",
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) {
        throw "Candidate logical root is invalid."
    }
    return $match.Groups["Id"].Value
}

function Invoke-SelfTests {
    foreach ($relative in @(
        "scripts\verify-m9-windows-ci.ps1",
        "scripts\verify-m9-toolchain-lock.ps1",
        "scripts\verify-m9-net45-x64.ps1",
        "scripts\verify-all-gates.ps1",
        "scripts\verify-autocad2016-context-v2-candidate.ps1"
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $relative) -PathType Leaf)) {
            throw "M9.3 self-test is missing a required script."
        }
    }

    $projects = @(
        foreach ($name in $requiredPhase2Projects) {
            [pscustomobject]@{
                Name = $name
                Passed = 1
                Total = 1
                Failed = 0
            }
        }
    )
    $synthetic = [pscustomobject]@{
        Scope = "phase2-managed-core-gate"
        Status = "automated-gate-passed"
        PowerShellEdition = "Core"
        Configuration = "Release"
        SolutionBuildPassed = $true
        HostForbiddenApiScanPassed = $true
        AgentHostDoctorPassed = $true
        GitDiffCheckPassed = $true
        BasicSecretScanPassed = $true
        ConditionalLockFileRestored = $true
        SpecProjects = $projects
        TotalSpecs = $projects.Count
    }
    $result = Assert-Phase2Evidence -Evidence $synthetic -ExpectedEdition "Core"
    if ($result.Total -ne $requiredPhase2Projects.Count) {
        throw "M9.3 self-test did not dynamically sum Phase2."
    }

    $duplicateRejected = $false
    try {
        $synthetic.SpecProjects = @($projects + $projects[0])
        $null = Assert-Phase2Evidence -Evidence $synthetic -ExpectedEdition "Core"
    }
    catch {
        $duplicateRejected = $true
    }
    if (-not $duplicateRejected) {
        throw "M9.3 self-test accepted a duplicate Phase2 project."
    }

    if ((Convert-SpecSummary "7/7" "self-test") -ne 7) {
        throw "M9.3 self-test did not parse a dynamic spec summary."
    }
    $stringBooleanRejected = $false
    try {
        Assert-JsonBoolean "false" $false "self-test"
    }
    catch {
        $stringBooleanRejected = $true
    }
    if (-not $stringBooleanRejected) {
        throw "M9.3 self-test accepted a string as a JSON boolean."
    }

    $candidateId = Get-CandidateIdFromLogicalRoot `
        "artifacts/autocad2016-m4-live-v042-a1-b2-c3"
    if ($candidateId -cne "autocad2016-m4-live-v042-a1-b2-c3") {
        throw "M9.3 self-test did not parse a candidate logical root."
    }
    foreach ($invalidRoot in @(
        "artifact/candidate",
        "artifacts/../candidate",
        "artifacts/candidate/subdirectory",
        "C:/candidate"
    )) {
        $invalidRootRejected = $false
        try {
            $null = Get-CandidateIdFromLogicalRoot $invalidRoot
        }
        catch {
            $invalidRootRejected = $true
        }
        if (-not $invalidRootRejected) {
            throw "M9.3 self-test accepted an invalid candidate logical root."
        }
    }

    Write-Host "M9_REQUIRED_GATES_SELF_TEST=passed"
    Write-Host ("M9_REQUIRED_GATES_PHASE2_PROJECTS=" + $requiredPhase2Projects.Count)
    Write-Host ("M9_REQUIRED_GATES_COVERAGE_ITEMS=" + $requiredCoverage.Count)
}

if ($SelfTestOnly) {
    if (-not [string]::IsNullOrWhiteSpace($CodexExecutable) -or
        -not [string]::IsNullOrWhiteSpace($AutoCad2016Dir) -or
        -not [string]::IsNullOrWhiteSpace($ArtifactBase)) {
        throw "SelfTestOnly cannot be combined with runtime inputs."
    }
    Invoke-SelfTests
    return
}

$sourceBefore = Get-SourceState
if ($sourceBefore.Dirty) {
    throw "M9.3 final aggregation requires a clean committed worktree."
}
if (@(Get-Process -Name acad -ErrorAction SilentlyContinue).Count -ne 0) {
    throw "M9.3 final aggregation refuses to run while AutoCAD is open."
}
if ([string]::IsNullOrWhiteSpace($AutoCad2016Dir)) {
    throw "M9.3 requires the reviewed AutoCAD 2016 R20.1 input directory."
}

$base = Resolve-Base -Requested $ArtifactBase
$runId = "m9r-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$isolatedBase = Join-Path $base $runId
$previousArtifactBase = $env:CODEX_AUTOCAD_ARTIFACT_BASE
$previousRunId = $env:CODEX_GATE_RUN_ID
$buildSafety = $null
$completed = $false

try {
    $env:CODEX_AUTOCAD_ARTIFACT_BASE = $isolatedBase
    $env:CODEX_GATE_RUN_ID = "run-" + [Guid]::NewGuid().ToString("N")
    $buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot `
        -MinimumFreeGiB $MinimumFreeGiB
    $artifactRoot = $buildSafety.ArtifactRoot
    $stageRoot = Join-Path $artifactRoot "m9-required-gates"
    $allGateEvidence = Join-Path $stageRoot "all-gates"
    $candidateEvidenceDirectory = Join-Path $stageRoot "candidate-evidence"
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $allGateEvidence -Force | Out-Null
    New-Item -ItemType Directory -Path $candidateEvidenceDirectory -Force | Out-Null

    $processBefore = @(Get-RelevantProcessKeys)
    $windowsEvidencePath = Join-Path $stageRoot "windows-ci.json"
    $toolchainEvidencePath = Join-Path $stageRoot "toolchain.json"
    $net45EvidencePath = Join-Path $stageRoot "net45-x64.json"

    Invoke-Stage -Name "windows-ci-definition" `
        -Script (Join-Path $PSScriptRoot "verify-m9-windows-ci.ps1") `
        -Arguments @(
            "-EvidencePath", $windowsEvidencePath,
            "-MinimumFreeGiB", ([string] $MinimumFreeGiB)
        )
    Invoke-Stage -Name "toolchain-lock" `
        -Script (Join-Path $PSScriptRoot "verify-m9-toolchain-lock.ps1") `
        -Arguments @(
            "-AutoCad2016Dir", $AutoCad2016Dir,
            "-EvidencePath", $toolchainEvidencePath,
            "-MinimumFreeGiB", ([string] $MinimumFreeGiB)
        )
    Invoke-Stage -Name "net45-x64" `
        -Script (Join-Path $PSScriptRoot "verify-m9-net45-x64.ps1") `
        -Arguments @(
            "-Configuration", $Configuration,
            "-EvidencePath", $net45EvidencePath,
            "-MinimumFreeGiB", ([string] $MinimumFreeGiB)
        )

    $allGateArguments = @(
        "-Configuration", $Configuration,
        "-AutoCad2016Dir", $AutoCad2016Dir,
        "-ArtifactBase", $isolatedBase,
        "-EvidenceDirectory", $allGateEvidence
    )
    if (-not [string]::IsNullOrWhiteSpace($CodexExecutable)) {
        $allGateArguments += @("-CodexExecutable", $CodexExecutable)
    }
    Invoke-Stage -Name "correlated-required-suite" `
        -Script (Join-Path $PSScriptRoot "verify-all-gates.ps1") `
        -Arguments $allGateArguments

    $candidateArguments = @(
        "-Configuration", $Configuration,
        "-CandidateProfile", "m4-live",
        "-AutoCad2016Dir", $AutoCad2016Dir,
        "-ReadinessEvidencePath", (Join-Path $allGateEvidence "m4-readiness.json"),
        "-SuiteEvidencePath", (Join-Path $allGateEvidence "all-gates.json"),
        "-EvidenceDirectory", $candidateEvidenceDirectory
    )
    if (-not [string]::IsNullOrWhiteSpace($CodexExecutable)) {
        $candidateArguments += @("-CodexExecutable", $CodexExecutable)
    }
    Invoke-Stage -Name "candidate-manifest-doctor" `
        -Script (Join-Path $PSScriptRoot "verify-autocad2016-context-v2-candidate.ps1") `
        -Arguments $candidateArguments

    $candidateEvidenceFiles = @(
        Get-ChildItem -LiteralPath $candidateEvidenceDirectory -File -Filter "*.json"
    )
    if ($candidateEvidenceFiles.Count -ne 1) {
        throw "Candidate stage did not produce exactly one evidence file."
    }

    $windows = Read-StrictJson $windowsEvidencePath "Windows CI evidence"
    $toolchain = Read-StrictJson $toolchainEvidencePath "Toolchain evidence"
    $net45 = Read-StrictJson $net45EvidencePath "net45 evidence"
    $suitePath = Join-Path $allGateEvidence "all-gates.json"
    $readinessPath = Join-Path $allGateEvidence "m4-readiness.json"
    $phase2Ps7Path = Join-Path $allGateEvidence "phase2-ps7.json"
    $phase2Ps51Path = Join-Path $allGateEvidence "phase2-ps51.json"
    $suite = Read-StrictJson $suitePath "Nested suite evidence"
    $readiness = Read-StrictJson $readinessPath "M4 readiness evidence"
    $phase2Ps7 = Read-StrictJson $phase2Ps7Path "Phase2 PowerShell 7 evidence"
    $phase2Ps51 = Read-StrictJson $phase2Ps51Path "Phase2 Windows PowerShell evidence"
    $candidate = Read-StrictJson $candidateEvidenceFiles[0].FullName "Candidate evidence"

    if ([string] $windows.Json.Schema -cne
            "codex.autocad.m9-windows-ci-definition/3" -or
        [string] $windows.Json.Status -cne "definition_verified") {
        throw "Windows CI definition evidence is invalid."
    }
    Assert-JsonBoolean $windows.Json.RemoteWorkflowRunVerified $false `
        "WindowsCI.RemoteWorkflowRunVerified"

    if ([string] $toolchain.Json.Schema -cne
            "codex.autocad.m9-toolchain-verification/1" -or
        [string] $toolchain.Json.Status -cne "toolchain_lock_verified") {
        throw "Toolchain evidence is invalid."
    }
    Assert-JsonBoolean $toolchain.Json.R201BinaryInputsVerified $true `
        "Toolchain.R201BinaryInputsVerified"
    Assert-JsonBoolean $toolchain.Json.CleanCacheReproducible $true `
        "Toolchain.CleanCacheReproducible"
    if (-not (Test-JsonInteger $toolchain.Json.CleanCachePassCount) -or
        [int] $toolchain.Json.CleanCachePassCount -ne 2) {
        throw "Toolchain evidence lacks two clean-cache passes."
    }

    if ([string] $net45.Json.Schema -cne "codex.autocad.m9-net45-x64/1" -or
        [string] $net45.Json.Status -cne "net45_x64_build_verified" -or
        [string] $net45.Json.TargetFramework -cne "net45" -or
        [string] $net45.Json.Architecture -cne "x64") {
        throw "net45/x64 evidence is invalid."
    }

    $nestedGateNames = @($suite.Json.Gates | ForEach-Object { [string] $_.Name })
    Assert-ExactNameSet -Expected $requiredNestedGates -Actual $nestedGateNames `
        -Label "Nested gate names"
    if (-not (Test-JsonInteger $suite.Json.GateTotal) -or
        [int] $suite.Json.GateTotal -ne $requiredNestedGates.Count -or
        [int] $suite.Json.GatePassed -ne $requiredNestedGates.Count -or
        [int] $suite.Json.GateFailed -ne 0 -or
        [int] $suite.Json.IntroducedResidualProcessCount -ne 0) {
        throw "Nested required suite did not pass every gate."
    }

    if ([string] $readiness.Json.Source.HeadCommit -cne $sourceBefore.Head) {
        throw "M4 readiness is not bound to the source commit."
    }
    Assert-JsonBoolean $readiness.Json.Source.WorkingTreeDirty $false `
        "Readiness.Source.WorkingTreeDirty"
    Assert-JsonBoolean $readiness.Json.AutomatedGatesPassed $true `
        "Readiness.AutomatedGatesPassed"
    Assert-JsonBoolean $readiness.Json.M4Complete $false "Readiness.M4Complete"
    Assert-JsonBoolean $readiness.Json.M416Frozen $false "Readiness.M416Frozen"
    Assert-JsonBoolean $readiness.Json.CadWriteEnabled $false `
        "Readiness.CadWriteEnabled"

    $phase2Core = Assert-Phase2Evidence $phase2Ps7.Json "Core"
    $phase2Desktop = Assert-Phase2Evidence $phase2Ps51.Json "Desktop"
    if (($phase2Core | ConvertTo-Json -Depth 8 -Compress) -cne
        ($phase2Desktop | ConvertTo-Json -Depth 8 -Compress)) {
        throw "Dual-shell Phase2 dynamic projects are not identical."
    }

    if ([string] $candidate.Json.scope -cne
            "autocad2016-m4-live-candidate-build" -or
        [string] $candidate.Json.source.headCommit -cne $sourceBefore.Head -or
        [string] $candidate.Json.source.suiteEvidenceSha256 -cne $suite.Sha256 -or
        [string] $candidate.Json.source.readinessEvidenceSha256 -cne
            $readiness.Sha256) {
        throw "Candidate evidence is not bound to this source and suite."
    }
    Assert-JsonBoolean $candidate.Json.gates.candidateAgentHostDoctor $true `
        "Candidate.AgentHostDoctor"
    Assert-JsonBoolean $candidate.Json.gates.sensitiveScan $true `
        "Candidate.SensitiveScan"
    Assert-JsonBoolean $candidate.Json.gates.AutoCADStartedOrRestarted $false `
        "Candidate.AutoCadStarted"

    $candidateId = Get-CandidateIdFromLogicalRoot `
        ([string] $candidate.Json.candidate.root)
    $candidateRoot = [IO.Path]::GetFullPath(
        (Join-Path $artifactRoot $candidateId))
    $candidateItem = Get-Item -LiteralPath $candidateRoot -Force -ErrorAction Stop
    if (-not $candidateItem.PSIsContainer -or
        ($candidateItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Candidate physical root is invalid."
    }
    $manifestPath = Join-Path $candidateRoot "manifest.json"
    if ((Get-Sha256 $manifestPath) -cne
        [string] $candidate.Json.candidate.manifestSha256) {
        throw "Candidate manifest hash is not bound to candidate evidence."
    }
    $manifest = Read-StrictJson $manifestPath "Candidate manifest"
    if ([string] $manifest.Json.m4Binding.sourceHeadCommit -cne $sourceBefore.Head -or
        [string] $manifest.Json.m4Binding.suiteEvidenceSha256 -cne $suite.Sha256 -or
        [string] $manifest.Json.m4Binding.readinessEvidenceSha256 -cne
            $readiness.Sha256) {
        throw "Candidate manifest is not bound to the verified source and suite."
    }
    Assert-JsonBoolean $manifest.Json.m4Binding.cadWriteEnabled $false `
        "Manifest.CadWriteEnabled"

    $bootstrapCandidates = @(
        Get-ChildItem -LiteralPath $artifactRoot -Directory `
            -Filter "autocad2016-agent-bootstrap-*" |
            ForEach-Object {
                $path = Join-Path $_.FullName "verification.json"
                if (Test-Path -LiteralPath $path -PathType Leaf) {
                    $read = Read-StrictJson $path "Agent bootstrap evidence"
                    if ([string] $read.Json.RunCorrelationId -ceq
                        [string] $suite.Json.RunCorrelationId) {
                        [pscustomobject]@{ Path = $path; Read = $read }
                    }
                }
            }
    )
    if ($bootstrapCandidates.Count -ne 1) {
        throw "Cannot bind exactly one Agent bootstrap evidence file."
    }
    $bootstrap = $bootstrapCandidates[0].Read
    if ([int] $bootstrap.Json.SchemaVersion -ne 17) {
        throw "Agent bootstrap evidence schema is not M9.3."
    }
    $bootstrapSpecs = Convert-SpecSummary `
        ([string] $bootstrap.Json.Net8Specs) "Agent bootstrap Specs"
    $bootstrapNet45Specs = Convert-SpecSummary `
        ([string] $bootstrap.Json.Net45Specs) "Agent bootstrap net45 Specs"
    $serviceSpecs = Convert-SpecSummary `
        ([string] $bootstrap.Json.AgentServiceSpecs) "Agent service Specs"
    if ($bootstrapSpecs -ne $bootstrapNet45Specs -or
        $bootstrapSpecs -ne @($bootstrap.Json.RequiredRuntimeSpecIds).Count -or
        $serviceSpecs -ne @($bootstrap.Json.RequiredAgentServiceSpecIds).Count) {
        throw "Agent bootstrap dynamic spec counts are inconsistent."
    }
    Assert-JsonBoolean $bootstrap.Json.BootstrapServeLifecycleVerified $true `
        "AgentBootstrap.BootstrapServeLifecycleVerified"

    $coverage = [ordered]@{
        Contracts = $true
        Ipc = $true
        Bridge = $true
        Launcher = $true
        AppServer = $true
        Runtime = $true
        HostMvp = $true
        Security = $true
        ForbiddenApi = $true
        SecretScan = $true
        CandidateManifest = $true
        CandidateDoctor = $true
    }
    Assert-ExactNameSet -Expected $requiredCoverage `
        -Actual @($coverage.Keys) -Label "M9.3 coverage"

    $sourceAfter = Get-SourceState
    if ($sourceAfter.Dirty -or $sourceAfter.Head -cne $sourceBefore.Head) {
        throw "Source changed during M9.3 aggregation."
    }
    $processAfter = @(Get-RelevantProcessKeys)
    $introducedProcesses = @(
        Compare-Object -ReferenceObject $processBefore `
            -DifferenceObject $processAfter |
            Where-Object { $_.SideIndicator -ceq "=>" }
    )
    if ($introducedProcesses.Count -ne 0) {
        throw "M9.3 left relevant residual processes."
    }

    $finalEvidence = [ordered]@{
        Schema = "codex.autocad.m9-required-gates/1"
        Status = "required_gates_verified"
        RunCorrelationId = $env:CODEX_GATE_RUN_ID
        Source = [ordered]@{
            HeadCommit = $sourceBefore.Head
            WorkingTreeDirty = $false
        }
        Coverage = $coverage
        DynamicSpecs = [ordered]@{
            Phase2ProjectCount = $phase2Core.Projects.Count
            Phase2LogicalSpecCount = $phase2Core.Total
            AgentBootstrapLogicalSpecCount = $bootstrapSpecs
            AgentServiceLogicalSpecCount = $serviceSpecs
            UniqueLogicalSpecCount =
                $phase2Core.Total + $bootstrapSpecs + $serviceSpecs
            CrossRuntimeBootstrapExecutionCount =
                $bootstrapSpecs + $bootstrapNet45Specs
            DuplicateGateRerunsExcludedFromUniqueTotal = $true
        }
        EvidenceHashes = [ordered]@{
            WindowsCi = $windows.Sha256
            Toolchain = $toolchain.Sha256
            Net45X64 = $net45.Sha256
            NestedSuite = $suite.Sha256
            Readiness = $readiness.Sha256
            AgentBootstrap = $bootstrap.Sha256
            Candidate = $candidate.Sha256
            CandidateManifest = $manifest.Sha256
        }
        NestedRunCorrelationId = [string] $suite.Json.RunCorrelationId
        IntroducedResidualProcessCount = 0
        AutoCadStartedOrCommanded = $false
        NetLoadVerified = $false
        RemoteWorkflowRunVerified = $false
        M4Complete = $false
        M416Frozen = $false
        CadWriteEnabled = $false
        EvidenceBoundary = "This evidence proves the local M9.3 required automated gate aggregation from one clean committed source: M9.1 workflow definition, M9.2 pinned toolchain and clean-cache R20.1 probe, net45/x64 managed outputs, the correlated M4 suite, dynamic managed and launcher service specs, candidate manifest, and candidate AgentHost doctor. It does not prove a remote GitHub Actions run, AutoCAD NETLOAD, the M4 real abnormal-exit matrix, M4.16 freeze, CAD writes, enterprise policy matrices, or release readiness."
    }
    $finalEvidencePath = Join-Path $stageRoot "m9-required-gates.json"
    [IO.File]::WriteAllText(
        $finalEvidencePath,
        ($finalEvidence | ConvertTo-Json -Depth 12),
        (New-Object Text.UTF8Encoding($false)))

    Complete-CodexBuildSafety -State $buildSafety -Stage "m9-required-gates" |
        Out-Null
    $completed = $true
    Write-Host "M9_REQUIRED_GATES=passed"
    Write-Host ("M9_REQUIRED_GATES_UNIQUE_LOGICAL_SPECS=" +
        $finalEvidence.DynamicSpecs.UniqueLogicalSpecCount)
    Write-Host "M9_REQUIRED_GATES_EVIDENCE=m9-required-gates/m9-required-gates.json"
}
finally {
    try {
        if ($null -ne $buildSafety -and -not $completed) {
            Complete-CodexBuildSafety -State $buildSafety `
                -Stage "m9-required-gates-aborted" | Out-Null
        }
    }
    finally {
        $env:CODEX_GATE_RUN_ID = $previousRunId
        $env:CODEX_AUTOCAD_ARTIFACT_BASE = $previousArtifactBase
    }
}
