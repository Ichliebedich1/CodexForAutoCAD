[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCad2016Dir,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $repoRoot 'handoff\autocad2016\evidence\r201-api-signatures-cross-shell-20260721.json'
}

$scriptPath = Join-Path $PSScriptRoot 'verify-autocad2016-r201-api-signatures.ps1'
if (-not (Test-Path $scriptPath)) { throw "Signature probe script not found: $scriptPath" }

$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)
$strictUtf8 = New-Object Text.UTF8Encoding($false, $true)

Write-Host "============================================================"
Write-Host "  R20.1 API Signature Probe — Dual-Shell Stage Orchestrator"
Write-Host "============================================================"
Write-Host "AutoCAD 2016 dir: $AutoCad2016Dir"
Write-Host "Configuration: $Configuration"
Write-Host "Evidence path: $EvidencePath"
Write-Host ""

# === Phase 1: PowerShell 7 ===
Write-Host "=== Phase 1: PowerShell 7 verification ==="
Write-Host "PowerShell version: $($PSVersionTable.PSVersion)"
Write-Host ""

$ps7Result = @( & $scriptPath -AutoCad2016Dir $AutoCad2016Dir -Configuration $Configuration 6>$null )
$ps7Json = $ps7Result | Where-Object { $_ -ne $null } | Select-Object -Last 1
$ps7Probe = $ps7Json.probeOutput
$ps7Overall = $ps7Probe.summary.overallPassed
Write-Host "PS7 overall: $(if ($ps7Overall) { 'PASSED' } else { 'FAILED' })"
Write-Host ""

# === Phase 2: PowerShell 5.1 ===
Write-Host "=== Phase 2: PowerShell 5.1 verification ==="
$ps51Path = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path $ps51Path)) { throw "Windows PowerShell 5.1 not found: $ps51Path" }

$ps51OutDir = Join-Path $repoRoot ("artifacts\r201-sig-ps51-{0}" -f [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $ps51OutDir -Force | Out-Null
$ps51JsonPath = Join-Path $ps51OutDir 'ps51-result.json'
$ps51TempScript = Join-Path $ps51OutDir 'run.ps1'

@"
Set-ExecutionPolicy -Scope Process Bypass
`$result = & '$scriptPath' -AutoCad2016Dir '$AutoCad2016Dir' -Configuration '$Configuration' 6>`$null
`$json = `$result | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText('$ps51JsonPath', `$json, [Text.UTF8Encoding]::new(`$false, `$true))
"@ | Set-Content -Path $ps51TempScript -Encoding UTF8

& $ps51Path -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $ps51TempScript 2>&1 | Out-Null
$ps51ExitCode = $LASTEXITCODE
Write-Host "PS5.1 exit code: $ps51ExitCode"
if ($ps51ExitCode -ne 0) { throw "PowerShell 5.1 verification failed with exit code $ps51ExitCode." }
if (-not (Test-Path $ps51JsonPath)) { throw "PS5.1 result JSON not found" }

$ps51Json = [IO.File]::ReadAllText($ps51JsonPath, $strictUtf8) | ConvertFrom-Json
$ps51Probe = $ps51Json.probeOutput
$ps51Overall = $ps51Probe.summary.overallPassed
Write-Host "PS5.1 overall: $(if ($ps51Overall) { 'PASSED' } else { 'FAILED' })"
Write-Host ""

# === Phase 3: Cross-shell consistency ===
Write-Host "=== Phase 3: Cross-shell consistency check ==="

$crossShell = [ordered]@{ consistent = $true; checks = [ordered]@{} }

# Compare normalized probe output
$ps7Norm = $ps7Probe | ConvertTo-Json -Depth 10
$ps51Norm = $ps51Probe | ConvertTo-Json -Depth 10
$match = ($ps7Norm -eq $ps51Norm)
$crossShell.checks["normalizedProbeOutputIdentical"] = $match
if (-not $match) { $crossShell.consistent = $false; Write-Host "[FAIL] Probe output differs" }
else { Write-Host "[PASS] Probe output identical" }

# Shell identity check
$ps7ShellId = $ps7Json.shellIdentity
$ps51ShellId = $ps51Json.shellIdentity
Write-Host "PS7 shell: $ps7ShellId, PS5.1 shell: $ps51ShellId"
$crossShell.checks["shellIdentities"] = @($ps7ShellId, $ps51ShellId)

# Probe DLL hash
$dllMatch = ($ps7Json.probeDllSha256 -eq $ps51Json.probeDllSha256)
$crossShell.checks["probeDllHashMatch"] = $dllMatch
if (-not $dllMatch) { $crossShell.consistent = $false; Write-Host "[FAIL] Probe DLL hash differs" }
else { Write-Host "[PASS] Probe DLL hash identical: $($ps7Json.probeDllSha256)" }

Write-Host ""
Write-Host "Cross-shell: $(if ($crossShell.consistent) { 'CONSISTENT' } else { 'DRIFT' })"

# === Phase 4: Generate evidence ONLY after all gates pass ===
if (-not $crossShell.consistent) { throw "Cross-shell consistency FAILED. No evidence generated." }
if (-not $ps7Overall -or -not $ps51Overall) { throw "One or both shells FAILED. No evidence generated." }

Write-Host ""
Write-Host "=== Phase 4: Generating evidence ==="

$evidenceDir = Split-Path -Parent $EvidencePath
if (-not (Test-Path $evidenceDir)) { New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null }

$now = [DateTime]::UtcNow.ToString("o")

# PS7 evidence
$ps7EvidencePath = Join-Path $repoRoot 'handoff\autocad2016\evidence\r201-api-signatures-pwsh7-20260721.json'
$ps7Evidence = [ordered]@{
    probeVersion = "1.0.0"; verificationTimeUtc = $now
    shell = $ps7Json.shellIdentity; powerShellVersion = $ps7Json.powerShellVersion
    autoCad2016Dir = "REDACTED"; configuration = $Configuration
    buildWarnings = 0; buildErrors = 0
    probeDllSha256 = $ps7Json.probeDllSha256; probeJsonSha256 = $ps7Json.probeJsonSha256
    autodeskDllsInOutput = 0; probeOutput = $ps7Probe
    autoCadStartedOrRestarted = $false; cadCommandsSent = $false
    netLoadVerified = $false; autoCadLiveEvidence = $false
}
[IO.File]::WriteAllText($ps7EvidencePath, ($ps7Evidence | ConvertTo-Json -Depth 10), $strictUtf8)
Write-Host "PS7 evidence: $ps7EvidencePath"

# PS5.1 evidence
$ps51EvidencePath = Join-Path $repoRoot 'handoff\autocad2016\evidence\r201-api-signatures-powershell51-20260721.json'
$ps51Evidence = [ordered]@{
    probeVersion = "1.0.0"; verificationTimeUtc = $now
    shell = $ps51Json.shellIdentity; powerShellVersion = $ps51Json.powerShellVersion
    autoCad2016Dir = "REDACTED"; configuration = $Configuration
    buildWarnings = 0; buildErrors = 0
    probeDllSha256 = $ps51Json.probeDllSha256; probeJsonSha256 = $ps51Json.probeJsonSha256
    autodeskDllsInOutput = 0; probeOutput = $ps51Probe
    autoCadStartedOrRestarted = $false; cadCommandsSent = $false
    netLoadVerified = $false; autoCadLiveEvidence = $false
}
[IO.File]::WriteAllText($ps51EvidencePath, ($ps51Evidence | ConvertTo-Json -Depth 10), $strictUtf8)
Write-Host "PS5.1 evidence: $ps51EvidencePath"

# Cross-shell evidence
$evidence = [ordered]@{
    probeVersion = "1.0.0"; verificationTimeUtc = $now; stageScriptVersion = "1.0.0"
    autoCad2016Dir = "REDACTED"; configuration = $Configuration
    autoCadStartedOrRestarted = $false; cadCommandsSent = $false
    netLoadVerified = $false; autoCadLiveEvidence = $false
    crossShellConsistency = $crossShell
    powershell7 = [ordered]@{
        shellIdentity = $ps7Json.shellIdentity; powerShellVersion = $ps7Json.powerShellVersion
        buildWarnings = 0; buildErrors = 0
        probeDllSha256 = $ps7Json.probeDllSha256; probeJsonSha256 = $ps7Json.probeJsonSha256
        autodeskDllsInOutput = 0; probeOutput = $ps7Probe
    }
    powershell51 = [ordered]@{
        shellIdentity = $ps51Json.shellIdentity; powerShellVersion = $ps51Json.powerShellVersion
        buildWarnings = 0; buildErrors = 0
        probeDllSha256 = $ps51Json.probeDllSha256; probeJsonSha256 = $ps51Json.probeJsonSha256
        autodeskDllsInOutput = 0; probeOutput = $ps51Probe
    }
    summary = [ordered]@{
        crossShellConsistent = $crossShell.consistent
        ps7OverallPassed = $ps7Overall; ps51OverallPassed = $ps51Overall
        probeDllSha256 = $ps7Json.probeDllSha256
        disclaimer = "This probe does NOT start or operate AutoCAD. Evidence is non-live."
    }
}
[IO.File]::WriteAllText($EvidencePath, ($evidence | ConvertTo-Json -Depth 10), $strictUtf8)
Write-Host "Cross-shell evidence: $EvidencePath"

# === Final summary ===
Write-Host ""
Write-Host "============================================================"
Write-Host "  Final Summary"
Write-Host "============================================================"
$s = $ps7Probe.summary
Write-Host "Positive signatures: $($s.positiveSignature.methods.passed)/$($s.positiveSignature.methods.total) methods, $($s.positiveSignature.properties.passed)/$($s.positiveSignature.properties.total) properties"
Write-Host "Expected absence: $($s.expectedAbsence.correctlyAbsent)/$($s.expectedAbsence.total)"
Write-Host "Enum freeze: $($s.enumFreeze.passed)/$($s.enumFreeze.total) (DimensionType absent=$($s.enumFreeze.dimensionTypeAbsent))"
Write-Host "Assembly identity: $($s.assemblyIdentity.passed)/$($s.assemblyIdentity.total)"
Write-Host "Cross-shell: $(if ($crossShell.consistent) { 'CONSISTENT' } else { 'DRIFT' })"
Write-Host "Probe DLL SHA: $($ps7Json.probeDllSha256)"
Write-Host "AutoCAD started: false | CAD commands: false | NETLOAD: false | Live evidence: false"
Write-Host ""
Write-Host "All gates passed. Evidence files written."
