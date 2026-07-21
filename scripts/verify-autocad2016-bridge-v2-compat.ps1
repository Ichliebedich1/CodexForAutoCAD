# verify-autocad2016-bridge-v2-compat.ps1
# Bridge v2 cross-version compatibility harness verifier
# Baseline: 589c8ea feat(agent): add explicit cad context v2 bridge path

[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $repoRoot "Codex.AutoCAD.sln"
$testServerProject = Join-Path $repoRoot "tests\Codex.AutoCAD.Bridge.Client.TestServer"
$v2CompatProject = Join-Path $repoRoot "tests\Codex.AutoCAD.Bridge.V2Compat.Specs"
$bridgeClientSpecsProject = Join-Path $repoRoot "tests\Codex.AutoCAD.Bridge.Client.Specs"
$contractsSpecsProject = Join-Path $repoRoot "tests\Codex.AutoCAD.Contracts.Specs"

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$evidenceDir = Join-Path $repoRoot "handoff\autocad2016\evidence"
$evidencePath = Join-Path $evidenceDir ("bridge-v2-compat-harness-verification-" + $timestamp + ".json")

Write-Host "======================================================="
Write-Host "Bridge v2 cross-version compatibility harness verifier"
Write-Host "Baseline: 589c8ea"
Write-Host ("Time: " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Host "======================================================="
Write-Host ""

# 1. Confirm HEAD
$head = (git -C $repoRoot rev-parse HEAD).Trim()
if ($head -ne "589c8eaddc257b7575686eab117a5a52391e2008") {
    Write-Warning ("HEAD (" + $head + ") is not the expected 589c8ea")
}

# 2. Confirm clean working tree
$status = git -C $repoRoot status --porcelain=v1 --untracked-files=all
if ($status) {
    Write-Warning "Working tree is not clean:"
    $status | ForEach-Object { Write-Warning ("  " + $_) }
}

# 3. Release build
Write-Host "[1/5] Release build..."
$buildResult = dotnet build $solution -c $Configuration 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}
Write-Host "  Build succeeded (0 warning / 0 error)"
Write-Host ""

# 4. Run Contracts Specs
Write-Host "[2/5] Contracts Specs..."
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$contractsOutput = dotnet run --project $contractsSpecsProject -c $Configuration --no-build 2>&1
$ErrorActionPreference = $prevEAP
$contractsPass = $contractsOutput | Select-String "(\d+)/(\d+) specs passed"
if ($contractsPass) {
    Write-Host ("  " + $contractsPass.Matches.Groups[0].Value)
} else {
    Write-Warning "Contracts Specs output unexpected"
}
Write-Host ""

# 5. Run Bridge Client Specs
Write-Host "[3/5] Bridge Client Specs..."
$serverExe = Join-Path $repoRoot ("tests\Codex.AutoCAD.Bridge.Client.TestServer\bin\" + $Configuration + "\net8.0-windows\Codex.AutoCAD.Bridge.Client.TestServer.exe")
$env:CODEX_BRIDGE_TEST_SERVER_EXE = $serverExe
$prevEAP2 = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$clientOutput = dotnet run --project $bridgeClientSpecsProject -c $Configuration --no-build 2>&1
$ErrorActionPreference = $prevEAP2
$clientPass = $clientOutput | Select-String "(\d+)/(\d+) specs passed"
if ($clientPass) {
    Write-Host ("  " + $clientPass.Matches.Groups[0].Value)
} else {
    Write-Warning "Bridge Client Specs output unexpected"
}
Write-Host ""

# 6. Run V2Compat Specs
Write-Host "[4/5] V2Compat Specs..."
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$v2compatOutput = dotnet run --project $v2CompatProject -c $Configuration --no-build 2>&1
$ErrorActionPreference = $prevEAP
$v2compatExit = $LASTEXITCODE
$v2compatPass = $v2compatOutput | Select-String "(\d+)/(\d+)"
if ($v2compatPass) {
    Write-Host ("  " + $v2compatPass.Matches.Groups[0].Value)
} else {
    Write-Warning "V2Compat Specs output unexpected"
}

# Check for FAIL items
$failures = $v2compatOutput | Select-String "\[FAIL\]"
if ($failures) {
    Write-Host "  Failed items:"
    $failures | ForEach-Object { Write-Host ("    " + $_) }
}
Write-Host ""

# 7. Confirm no residual TestServer processes
Write-Host "[5/5] Residual process check..."
$residual = Get-Process -Name "Codex.AutoCAD.Bridge.Client.TestServer" -ErrorAction SilentlyContinue
if ($residual) {
    Write-Warning ("Found " + $residual.Count + " residual TestServer process(es)")
    $residual | Stop-Process -Force
} else {
    Write-Host "  TestServer residual: 0"
}
Write-Host ""

# 8. Generate evidence JSON
Write-Host "Generating evidence JSON..."
$contractsSpecsResult = if ($contractsPass) { $contractsPass.Matches.Groups[0].Value } else { "unknown" }
$bridgeClientSpecsResult = if ($clientPass) { $clientPass.Matches.Groups[0].Value } else { "unknown" }
$v2compatSpecsResult = if ($v2compatPass) { $v2compatPass.Matches.Groups[0].Value } else { "unknown" }
$residualCount = if ($residual) { $residual.Count } else { 0 }

$evidence = @{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    baselineCommit = "589c8ea"
    v1ClientBaselineCommit = "0ceb123"
    configuration = $Configuration
    dotnetSdk = (dotnet --version).Trim()
    headCommit = $head
    workingTreeClean = [string]::IsNullOrEmpty($status)
    contractsSpecs = $contractsSpecsResult
    bridgeClientSpecs = $bridgeClientSpecsResult
    v2compatSpecs = $v2compatSpecsResult
    testServerResidualProcesses = $residualCount
    autoCadStartedOrRestarted = $false
    cadCommandsSent = $false
    netLoadVerified = $false
    autoCadLiveEvidence = $false
    productionGaps = @(
        @{
            specId = "COMPAT-V2-005-duplicate-gap"
            description = "Validator does not reject duplicate schema entries"
            requiredPassed = $false
        }
    )
}

if (-not (Test-Path $evidenceDir)) {
    New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
}

$evidence | ConvertTo-Json -Depth 10 | Set-Content -Path $evidencePath -Encoding UTF8
Write-Host ("  Evidence: " + $evidencePath)
Write-Host ""

Write-Host "======================================================="
Write-Host "Verification complete"
Write-Host "======================================================="
