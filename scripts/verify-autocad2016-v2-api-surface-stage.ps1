[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCad2016Dir,

    [ValidateSet("Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$baseVerifier = Join-Path $PSScriptRoot "verify-autocad2016-v2-api-surface.ps1"
$evidenceDir = Join-Path $repoRoot "handoff\autocad2016\evidence"

$expectedPassed = 19
$expectedFailed = 8
$expectedPassedMembers = @(
    "Spline.GetControlPointAt [method]",
    "Spline.GetFitPointAt [method]",
    "Polyline.GetBulgeAt [method]",
    "Leader.VertexAt [method]",
    "MLeader.GetLeaderIndexes [method]",
    "MLeader.GetLeaderLineIndexes [method]",
    "MLeader.VerticesCount [method]",
    "MLeader.GetVertex [method]",
    "Hatch.GetLoopAt [method]",
    "MLeader.MText [property]",
    "MLeader.ContentType [property]",
    "Hatch.NumberOfLoops [property]",
    "Table.GetTextString [method]",
    "Table.Cells [property]",
    "Table.Rows [property]",
    "Table.Columns [property]",
    "Leader.NumVertices [property]",
    "DBPoint.EcsRotation [property]",
    "Spline.NurbsData [property]"
)
$expectedFailedMembers = @(
    "MLeader.TextString [any]",
    "Table.GetTextStyle [method]",
    "Table.GetCellType [method]",
    "Polyline2d.VertexObjectIdList [property]",
    "Polyline3d.Vertices [property]",
    "Polyline3d.VertexObjectIdList [property]",
    "BlockReference.XrefStatus [property]",
    "Dimension.DimensionType [property]"
)

# --- Helper functions ---
function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string]$Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "")
        }
        finally { $sha.Dispose() }
    }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

function Get-ProcessIds {
    param([Parameter(Mandatory = $true)][string]$Name)
    return @(Get-Process -Name $Name -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id | Sort-Object)
}

function Assert-SameProcessSet {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][int[]]$Before,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][int[]]$After,
        [Parameter(Mandatory = $true)][string]$Label
    )
    $difference = @(Compare-Object -ReferenceObject $Before -DifferenceObject $After)
    if ($difference.Count -ne 0) {
        throw "$Label process set changed during verification."
    }
}

function Assert-MemberSetEqual {
    param(
        [Parameter(Mandatory = $true)][string[]]$Actual,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )
    $sortedActual = @($Actual | Sort-Object)
    $sortedExpected = @($Expected | Sort-Object)
    if ($sortedActual.Count -ne $sortedExpected.Count) {
        throw "$Label count mismatch: expected $($sortedExpected.Count), got $($sortedActual.Count)"
    }
    for ($i = 0; $i -lt $sortedActual.Count; $i++) {
        if ($sortedActual[$i] -cne $sortedExpected[$i]) {
            throw "$Label mismatch at index $i`: expected '$($sortedExpected[$i])', got '$($sortedActual[$i])'"
        }
    }
}

function Invoke-ChildVerifier {
    param(
        [Parameter(Mandatory = $true)][string]$PowerShellPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$ChildEvidencePath,
        [Parameter(Mandatory = $true)][string]$ChildArtifactRoot,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $baseVerifier,
        "-AutoCad2016Dir", $AutoCad2016Dir,
        "-Configuration", $Configuration,
        "-EvidencePath", $ChildEvidencePath,
        "-ArtifactRoot", $ChildArtifactRoot
    )

    Write-Host "`n==> Running $Label verifier" -ForegroundColor Cyan
    $output = @(& $PowerShellPath @arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE

    New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null
    [IO.File]::WriteAllLines($LogPath, $output, [Text.UTF8Encoding]::new($false))
    foreach ($line in $output) {
        Write-Host "[$Label] $line"
    }

    if ($exitCode -ne 0) {
        throw "$Label verifier failed with exit code $exitCode"
    }
    if (-not (Test-Path -LiteralPath $ChildEvidencePath -PathType Leaf)) {
        throw "$Label verifier did not produce evidence at: $ChildEvidencePath"
    }
}

function Get-NormalizedEvidence {
    param([Parameter(Mandatory = $true)]$Evidence)

    return [ordered]@{
        probeVersion = $Evidence.probeVersion
        targetAssembly = $Evidence.targetAssembly
        framework = $Evidence.framework
        platform = $Evidence.platform
        buildWarnings = $Evidence.buildWarnings
        buildErrors = $Evidence.buildErrors
        compileTimeTypeChecks = $Evidence.compileTimeTypeChecks
        compileTimePropertyChecks = $Evidence.compileTimePropertyChecks
        runtimeChecksPassed = $Evidence.runtimeChecksPassed
        runtimeChecksFailed = $Evidence.runtimeChecksFailed
        runtimePassedMembers = @($Evidence.runtimePassedMembers | Sort-Object)
        runtimeFailedMembers = @($Evidence.runtimeFailedMembers | Sort-Object)
        dllSha256 = $Evidence.dllSha256
        autodeskDllsInOutput = $Evidence.autodeskDllsInOutput
        autoCadStartedOrRestarted = $Evidence.autoCadStartedOrRestarted
        cadCommandsSent = $Evidence.cadCommandsSent
        netLoadVerified = $Evidence.netLoadVerified
        autoCadLiveEvidence = $Evidence.autoCadLiveEvidence
    }
}

# --- Preconditions ---
Write-Host "=== V2 API Surface Probe Dual-Shell Stage Verification ==="
Write-Host "AutoCAD 2016 dir: $AutoCad2016Dir"
Write-Host "Configuration: $Configuration"
Write-Host ""

if (-not (Test-Path $baseVerifier -PathType Leaf)) {
    throw "Base verifier not found: $baseVerifier"
}
if (-not (Test-Path (Join-Path $AutoCad2016Dir 'acad.exe'))) {
    throw "acad.exe not found in $AutoCad2016Dir"
}

$pwshCommand = (Get-Command pwsh -ErrorAction Stop).Source
$windowsPowerShellCommand = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
if (-not (Test-Path -LiteralPath $windowsPowerShellCommand -PathType Leaf)) {
    throw "Windows PowerShell 5.1 not found at: $windowsPowerShellCommand"
}

Write-Host "PowerShell 7: $pwshCommand"
Write-Host "Windows PowerShell 5.1: $windowsPowerShellCommand"
Write-Host ""

# --- Record AutoCAD process baseline ---
$cadBefore = @(Get-ProcessIds -Name "acad")

# --- Prepare stage root ---
$stageRoot = Join-Path $repoRoot ("artifacts\v2api-probe-stage-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

$ps7EvidencePath = Join-Path $stageRoot "ps7\evidence.json"
$ps51EvidencePath = Join-Path $stageRoot "ps51\evidence.json"
$ps7ArtifactRoot = Join-Path $stageRoot "ps7\build"
$ps51ArtifactRoot = Join-Path $stageRoot "ps51\build"
$ps7LogPath = Join-Path $stageRoot "powershell7.log"
$ps51LogPath = Join-Path $stageRoot "windowspowershell51.log"

# --- Run both shells ---
Invoke-ChildVerifier -PowerShellPath $pwshCommand -Label "PowerShell7" `
    -ChildEvidencePath $ps7EvidencePath -ChildArtifactRoot $ps7ArtifactRoot `
    -LogPath $ps7LogPath

Invoke-ChildVerifier -PowerShellPath $windowsPowerShellCommand -Label "WindowsPowerShell51" `
    -ChildEvidencePath $ps51EvidencePath -ChildArtifactRoot $ps51ArtifactRoot `
    -LogPath $ps51LogPath

# --- Read and validate evidence ---
$ps7Evidence = Get-Content -LiteralPath $ps7EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
$ps51Evidence = Get-Content -LiteralPath $ps51EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json

# Validate build results
foreach ($ev in @($ps7Evidence, $ps51Evidence)) {
    $shellLabel = if ($ev -eq $ps7Evidence) { "PowerShell7" } else { "WindowsPowerShell51" }
    if ($ev.framework -ne "net45") {
        throw "$shellLabel framework is '$($ev.framework)', expected 'net45'"
    }
    if ($ev.platform -ne "x64") {
        throw "$shellLabel platform is '$($ev.platform)', expected 'x64'"
    }
    if ($ev.buildWarnings -ne 0) {
        throw "$shellLabel build produced $($ev.buildWarnings) warning(s)"
    }
    if ($ev.buildErrors -ne 0) {
        throw "$shellLabel build produced $($ev.buildErrors) error(s)"
    }
    if ($ev.autodeskDllsInOutput -ne 0) {
        throw "$shellLabel found $($ev.autodeskDllsInOutput) Autodesk DLL(s) in output"
    }
}

# Validate runtime check counts
if ($ps7Evidence.runtimeChecksPassed -ne $expectedPassed) {
    throw "PowerShell7 passed=$($ps7Evidence.runtimeChecksPassed), expected $expectedPassed"
}
if ($ps7Evidence.runtimeChecksFailed -ne $expectedFailed) {
    throw "PowerShell7 failed=$($ps7Evidence.runtimeChecksFailed), expected $expectedFailed"
}
if ($ps51Evidence.runtimeChecksPassed -ne $expectedPassed) {
    throw "WindowsPowerShell51 passed=$($ps51Evidence.runtimeChecksPassed), expected $expectedPassed"
}
if ($ps51Evidence.runtimeChecksFailed -ne $expectedFailed) {
    throw "WindowsPowerShell51 failed=$($ps51Evidence.runtimeChecksFailed), expected $expectedFailed"
}

# Validate exact member sets
Assert-MemberSetEqual -Actual @($ps7Evidence.runtimePassedMembers) `
    -Expected $expectedPassedMembers -Label "PowerShell7 passed members"
Assert-MemberSetEqual -Actual @($ps7Evidence.runtimeFailedMembers) `
    -Expected $expectedFailedMembers -Label "PowerShell7 failed members"
Assert-MemberSetEqual -Actual @($ps51Evidence.runtimePassedMembers) `
    -Expected $expectedPassedMembers -Label "WindowsPowerShell51 passed members"
Assert-MemberSetEqual -Actual @($ps51Evidence.runtimeFailedMembers) `
    -Expected $expectedFailedMembers -Label "WindowsPowerShell51 failed members"

# Validate evidence safety flags
foreach ($ev in @($ps7Evidence, $ps51Evidence)) {
    $shellLabel = if ($ev -eq $ps7Evidence) { "PowerShell7" } else { "WindowsPowerShell51" }
    if ($ev.autoCadStartedOrRestarted -ne $false) {
        throw "$shellLabel autoCadStartedOrRestarted must be false"
    }
    if ($ev.cadCommandsSent -ne $false) {
        throw "$shellLabel cadCommandsSent must be false"
    }
    if ($ev.netLoadVerified -ne $false) {
        throw "$shellLabel netLoadVerified must be false"
    }
    if ($ev.autoCadLiveEvidence -ne $false) {
        throw "$shellLabel autoCadLiveEvidence must be false"
    }
}

# --- Cross-shell comparison ---
$ps7Normalized = Get-NormalizedEvidence -Evidence $ps7Evidence
$ps51Normalized = Get-NormalizedEvidence -Evidence $ps51Evidence
$ps7Comparable = $ps7Normalized | ConvertTo-Json -Depth 20 -Compress
$ps51Comparable = $ps51Normalized | ConvertTo-Json -Depth 20 -Compress

if ($ps7Comparable -cne $ps51Comparable) {
    throw "Normalized evidence differs between PowerShell 7 and Windows PowerShell 5.1."
}

# Validate DLL SHA-256 match
if ($ps7Evidence.dllSha256 -cne $ps51Evidence.dllSha256) {
    throw "DLL SHA-256 mismatch: PS7=$($ps7Evidence.dllSha256), PS51=$($ps51Evidence.dllSha256)"
}

# --- Record AutoCAD process final state ---
$cadAfter = @(Get-ProcessIds -Name "acad")
Assert-SameProcessSet -Before $cadBefore -After $cadAfter -Label "AutoCAD"

# --- Copy per-shell evidence to final locations ---
$ps7FinalPath = Join-Path $evidenceDir "v2-api-surface-probe-pwsh7-20260721.json"
$ps51FinalPath = Join-Path $evidenceDir "v2-api-surface-probe-powershell51-20260721.json"

if (Test-Path -LiteralPath $ps7FinalPath) {
    throw "Evidence file already exists (would overwrite): $ps7FinalPath"
}
if (Test-Path -LiteralPath $ps51FinalPath) {
    throw "Evidence file already exists (would overwrite): $ps51FinalPath"
}

New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
Copy-Item -LiteralPath $ps7EvidencePath -Destination $ps7FinalPath -Force
Copy-Item -LiteralPath $ps51EvidencePath -Destination $ps51FinalPath -Force

# --- Build cross-shell aggregate evidence ---
$crossShellEvidence = [ordered]@{
    schemaVersion = 1
    recordedAtLocal = [DateTimeOffset]::Now.ToString("o")
    scope = "autocad2016-v2-api-surface-probe-cross-shell"
    status = "dual-shell-gate-passed"
    configuration = $Configuration
    autoCad2016Dir = $AutoCad2016Dir
    powerShell7 = [ordered]@{
        version = $ps7Evidence.powerShellVersion
        evidenceFile = "v2-api-surface-probe-pwsh7-20260721.json"
        evidenceFileSha256 = Get-Sha256 -Path $ps7FinalPath
        logSha256 = Get-Sha256 -Path $ps7LogPath
    }
    windowsPowerShell51 = [ordered]@{
        version = $ps51Evidence.powerShellVersion
        evidenceFile = "v2-api-surface-probe-powershell51-20260721.json"
        evidenceFileSha256 = Get-Sha256 -Path $ps51FinalPath
        logSha256 = Get-Sha256 -Path $ps51LogPath
    }
    normalizedComparisonSha256 = Get-TextSha256 -Value $ps7Comparable
    dllSha256 = $ps7Evidence.dllSha256
    runtimeChecksPassed = $ps7Evidence.runtimeChecksPassed
    runtimeChecksFailed = $ps7Evidence.runtimeChecksFailed
    runtimePassedMembers = $ps7Evidence.runtimePassedMembers
    runtimeFailedMembers = $ps7Evidence.runtimeFailedMembers
    passedMembersMatchExpected = $true
    failedMembersMatchExpected = $true
    crossShellNormalizedIdentical = $true
    crossShellDllSha256Identical = $true
    buildWarnings = $ps7Evidence.buildWarnings
    buildErrors = $ps7Evidence.buildErrors
    compileTimeTypeChecks = $ps7Evidence.compileTimeTypeChecks
    compileTimePropertyChecks = $ps7Evidence.compileTimePropertyChecks
    autodeskDllsInOutput = $ps7Evidence.autodeskDllsInOutput
    autoCadProcessSetChanged = $false
    autoCadStartedOrRestarted = $false
    cadCommandsSent = $false
    netLoadVerified = $false
    autoCadLiveEvidence = $false
    historicalEvidenceUnchanged = $true
    historicalEvidencePath = "v2-api-surface-probe-verification.json"
    historicalEvidenceNote = "Historical single-shell evidence file preserved byte-for-byte; not overwritten by this stage run."
    evidenceBoundary = "Both PowerShell 7 and Windows PowerShell 5.1 independently built the V2 API Surface Probe in Release net45/x64 with 0 warnings, 0 errors, and 0 Autodesk DLLs in output. Runtime reflection checks produced identical results: 19 passed, 8 failed, with identical member sets. Both shells produced identical DLL SHA-256. This probe verifies API surface existence only; it does NOT start or operate AutoCAD and is NOT equivalent to AutoCAD runtime verification."
}

$crossShellEvidencePath = Join-Path $evidenceDir "v2-api-surface-probe-cross-shell-20260721.json"
if (Test-Path -LiteralPath $crossShellEvidencePath) {
    throw "Cross-shell evidence file already exists (would overwrite): $crossShellEvidencePath"
}

[IO.File]::WriteAllText(
    $crossShellEvidencePath,
    ($crossShellEvidence | ConvertTo-Json -Depth 30),
    [Text.UTF8Encoding]::new($false)
)

Write-Host ""
Write-Host "=== V2 API Surface Probe Dual-Shell Stage Passed ===" -ForegroundColor Green
Write-Host "PS7 evidence: $ps7FinalPath"
Write-Host "PS51 evidence: $ps51FinalPath"
Write-Host "Cross-shell evidence: $crossShellEvidencePath"
Write-Host "DLL SHA-256: $($ps7Evidence.dllSha256)"
Write-Host "Runtime: passed=$($ps7Evidence.runtimeChecksPassed), failed=$($ps7Evidence.runtimeChecksFailed)"
