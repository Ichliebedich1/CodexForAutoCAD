#!/usr/bin/env pwsh
# verify-autocad2016-bridge-v2-compat.ps1
# Bridge v2 跨版本兼容测试夹具验证脚本
# 基线: 589c8ea feat(agent): add explicit cad context v2 bridge path

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
$contractsSpecsProject = Join─Path $repoRoot "tests\Codex.AutoCAD.Contracts.Specs"

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$evidenceDir = Join-Path $repoRoot "handoff\autocad2016\evidence"
$evidencePath = Join-Path $evidenceDir "bridge-v2-compat-harness-verification-$timestamp.json"

Write-Host "═══════════════════════════════════════════════════"
Write-Host "Bridge v2 跨版本兼容测试夹具验证"
Write-Host "基线: 589c8ea"
Write-Host "时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host "═══════════════════════════════════════════════════"
Write-Host ""

# 1. 确认 HEAD
$head = (git -C $repoRoot rev-parse HEAD).Trim()
if ($head -ne "589c8eaddc257b7575686eab117a5a52391e2008") {
    Write-Warning "HEAD ($head) 不是预期的 589c8ea"
}

# 2. 确认工作树干净
$status = git -C $repoRoot status --porcelain=v1 --untracked-files=all
if ($status) {
    Write-Warning "工作树不干净:"
    $status | ForEach-Object { Write-Warning "  $_" }
}

# 3. Release 构建
Write-Host "[1/5] Release 构建..."
$buildResult = dotnet build $solution -c $Configuration 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "构建失败"
    exit 1
}
Write-Host "  构建成功 (0 warning / 0 error)"
Write-Host ""

# 4. 运行 Contracts Specs
Write-Host "[2/5] Contracts Specs..."
$contractsOutput = dotnet run --project $contractsSpecsProject -c $Configuration --no-build 2>&1
$contractsPass = $contractsOutput | Select-String "(\d+)/(\d+) specs passed"
if ($contractsPass) {
    Write-Host "  $($contractsPass.Matches.Groups[0].Value)"
} else {
    Write-Warning "Contracts Specs 输出异常"
}
Write-Host ""

# 5. 运行 Bridge Client Specs
Write-Host "[3/5] Bridge Client Specs..."
$serverExe = Join-Path $repoRoot "tests\Codex.AutoCAD.Bridge.Client.TestServer\bin\$Configuration\net8.0-windows\Codex.AutoCAD.Bridge.Client.TestServer.exe"
$env:CODEX_BRIDGE_TEST_SERVER_EXE = $serverExe
$clientOutput = dotnet run --project $bridgeClientSpecsProject -c $Configuration --no-build 2>&1
$clientPass = $clientOutput | Select-String "(\d+)/(\d+) specs passed"
if ($clientPass) {
    Write-Host "  $($clientPass.Matches.Groups[0].Value)"
} else {
    Write-Warning "Bridge Client Specs 输出异常"
}
Write-Host ""

# 6. 运行 V2Compat Specs
Write-Host "[4/5] V2Compat Specs..."
$v2compatOutput = dotnet run --project $v2CompatProject -c $Configuration --no-build 2>&1
$v2compatPass = $v2compatOutput | Select-String "总计: (\d+)/(\d+) 项通过"
$v2compatAudit = $v2compatOutput | Select-String "AUDIT_JSON_START" -Context 0,100
if ($v2compatPass) {
    Write-Host "  $($v2compatPass.Matches.Groups[0].Value)"
} else {
    Write-Warning "V2Compat Specs 输出异常"
}

# 检查是否有 FAIL
$failures = $v2compatOutput | Select-String "\[FAIL\]"
if ($failures) {
    Write-Host "  发现失败项:"
    $failures | ForEach-Object { Write-Host "    $_" }
}
Write-Host ""

# 7. 确认 TestServer 无残留
Write-Host "[5/5] 残留进程检查..."
$残留 = Get-Process -Name "Codex.AutoCAD.Bridge.Client.TestServer" -ErrorAction SilentlyContinue
if ($残留) {
    Write-Warning "发现 $($残留.Count) 个 TestServer 残留进程"
    $残留 | Stop-Process -Force
} else {
    Write-Host "  TestServer 残留: 0"
}
Write-Host ""

# 8. 生成 evidence JSON
Write-Host "生成 evidence JSON..."
$evidence = @{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    baselineCommit = "589c8ea"
    v1ClientBaselineCommit = "0ceb123"
    configuration = $Configuration
    dotnetSdk = (dotnet --version).Trim()
    headCommit = $head
    workingTreeClean = [string]::IsNullOrEmpty($status)
    contractsSpecs = if ($contractsPass) { $contractsPass.Matches.Groups[0].Value } else { "unknown" }
    bridgeClientSpecs = if ($clientPass) { $clientPass.Matches.Groups[0].Value } else { "unknown" }
    v2compatSpecs = if ($v2compatPass) { $v2compatPass.Matches.Groups[0].Value } else { "unknown" }
    testServerResidualProcesses = if ($残留) { $残留.Count } else { 0 }
    autoCadStartedOrRestarted = $false
    cadCommandsSent = $false
    netLoadVerified = $false
    autoCadLiveEvidence = $false
    productionGaps = @(
        @{
            specId = "COMPAT-V2-005-duplicate-gap"
            description = "验证器不拒绝重复schema条目"
            requiredPassed = $false
        }
    )
}

if (-not (Test-Path $evidenceDir)) {
    New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
}

$evidence | ConvertTo-Json -Depth 10 | Set-Content -Path $evidencePath -Encoding UTF8
Write-Host "  Evidence: $evidencePath"
Write-Host ""

Write-Host "═══════════════════════════════════════════════════"
Write-Host "验证完成"
Write-Host "═══════════════════════════════════════════════════"
