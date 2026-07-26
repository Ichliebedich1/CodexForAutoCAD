[CmdletBinding()]
param(
    # M4.15.6 汇总器写出的 evidence。冻结必须建立在一次可验证的门禁运行上，
    # 而不是「最近好像都绿过」。
    [string] $ReadinessEvidencePath,

    # 回滚点：必须是已经存在、且解析到当前 HEAD 的 Git ref。本脚本只读取和校验，
    # 绝不创建、移动或删除任何 ref——回滚点由谁建立，属于人的决定。
    [string] $RollbackRef,

    [string] $EvidencePath,
    [switch] $SelfTestOnly
)

# 本文件必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。
#
# 范围声明：本脚本**只校验 M4.16 的冻结前置条件**，不构建候选、不产生候选哈希、
# 不写回滚点。目标文件 M4.16 的交付还包括「从已提交源码构建的候选」与「资源/身份
# evidence」，那部分要等 M4 全部必选项真正通过后再接线；在那之前构建一个不能冻结的
# 候选只会制造一个看起来像冻结产物的目录。
#
# 今天运行它必定失败，且应当失败：M4 的真实机器与企业矩阵仍未验证，汇总器固定输出
# M4Complete=false。它的价值在于把「为什么还不能冻结」变成可执行、可测试的判定，
# 而不是一段需要每次重新论证的说明。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$safeRepoRoot = $repoRoot.Replace("\", "/")

function Test-FreezePrecondition {
    <#
    .SYNOPSIS
        判定一份 readiness evidence 是否满足 M4.16 冻结前置条件。
    .DESCRIPTION
        返回未通过项的列表，而不是抛出第一个错误：冻结被拒绝时，使用者需要看到
        全部缺口才能判断还差多远，逐条试错既慢又容易误以为「只差最后一项」。
    #>
    param(
        [Parameter(Mandatory = $true)] $Evidence,
        [Parameter(Mandatory = $true)][string] $ExpectedHeadCommit,
        [Parameter(Mandatory = $true)][bool] $WorkingTreeClean,
        [Parameter(Mandatory = $true)][bool] $RollbackRefResolvesToHead
    )

    $blockers = New-Object System.Collections.ArrayList

    function Test-Property {
        param([string] $Name)
        return ($Evidence.PSObject.Properties.Name -contains $Name)
    }

    if (-not $WorkingTreeClean) {
        $null = $blockers.Add("工作树不干净：候选必须从已提交源码构建。")
    }
    if (-not $RollbackRefResolvesToHead) {
        $null = $blockers.Add("回滚点缺失或未指向当前 HEAD。")
    }

    if (-not (Test-Property "Scope") -or
        [string] $Evidence.Scope -cne "m4-automated-readiness-binding") {
        $null = $blockers.Add("readiness evidence 的 Scope 不是 m4-automated-readiness-binding。")
    }

    if (-not (Test-Property "Source") -or
        -not ($Evidence.Source.PSObject.Properties.Name -contains "HeadCommit") -or
        [string] $Evidence.Source.HeadCommit -cne $ExpectedHeadCommit) {
        $null = $blockers.Add("readiness evidence 绑定的 HeadCommit 与当前 HEAD 不一致。")
    }
    elseif (($Evidence.Source.PSObject.Properties.Name -contains "WorkingTreeDirty") -and
        [bool] $Evidence.Source.WorkingTreeDirty) {
        $null = $blockers.Add("readiness evidence 记录当时工作树不干净。")
    }

    # 冻结不能建立在弱保证上：FreshnessOnly 只证明五份 evidence 时间接近，
    # 证明不了它们来自同一次运行，而失败的门禁恰恰不写 evidence。
    if (-not (Test-Property "RunCorrelation") -or
        -not ($Evidence.RunCorrelation.PSObject.Properties.Name -contains "Mode") -or
        [string] $Evidence.RunCorrelation.Mode -cne "Correlated") {
        $null = $blockers.Add("readiness evidence 的 RunCorrelation.Mode 不是 Correlated。")
    }

    foreach ($mustBeTrue in @("AutomatedGatesPassed", "M4Complete")) {
        if (-not (Test-Property $mustBeTrue) -or -not [bool] $Evidence.$mustBeTrue) {
            $null = $blockers.Add("readiness evidence 的 $mustBeTrue 不为 true。")
        }
    }

    # 冻结前这些必须仍然为 false：写入开关一旦在冻结候选里是开的，冻结本身就失去意义。
    foreach ($mustBeFalse in @(
            "CadWriteEnabled",
            "PluginInitiatedSaveEnabled",
            "AutoCadStartedOrCommanded")) {
        if (-not (Test-Property $mustBeFalse) -or [bool] $Evidence.$mustBeFalse) {
            $null = $blockers.Add("readiness evidence 的 $mustBeFalse 不为 false。")
        }
    }

    # M4 的真实机器与企业矩阵：任何一项未验证，M4 就没有全部通过，也就不能冻结。
    $realWorldFlags = @(
        "RealCredentialManagerVerified",
        "RealCodexLoginAndKeyringVerified",
        "RealRestrictedTokenProductChainVerified",
        "RealFixedCapacityVolumeVerified",
        "RealDiskFullVerified",
        "RealPowerLossVerified",
        "RealAbnormalExitMatrixVerified",
        "EnterpriseAppLockerWacEdRMatrixVerified",
        "EnterpriseRetentionArchiveMatrixVerified"
    )
    $unverified = New-Object System.Collections.ArrayList
    foreach ($flag in $realWorldFlags) {
        if (-not (Test-Property $flag) -or -not [bool] $Evidence.$flag) {
            $null = $unverified.Add($flag)
        }
    }
    if ($unverified.Count -gt 0) {
        $null = $blockers.Add(
            "真实机器/企业矩阵仍有 $($unverified.Count) 项未验证：" + (@($unverified) -join "、"))
    }

    # 一元逗号防止 PowerShell 把数组展开：空列表会变成 $null、单元素会变成标量，
    # 调用方的 .Count 在 StrictMode 下就会直接报错。
    return ,@($blockers)
}

function New-FreezeSelfTestEvidence {
    param([switch] $Complete)
    $evidence = [pscustomobject]@{
        Scope = "m4-automated-readiness-binding"
        Source = [pscustomobject]@{
            HeadCommit = ("a" * 40)
            WorkingTreeDirty = $false
        }
        RunCorrelation = [pscustomobject]@{ Mode = "Correlated" }
        AutomatedGatesPassed = $true
        M4Complete = [bool] $Complete
        CadWriteEnabled = $false
        PluginInitiatedSaveEnabled = $false
        AutoCadStartedOrCommanded = $false
    }
    foreach ($flag in @(
            "RealCredentialManagerVerified",
            "RealCodexLoginAndKeyringVerified",
            "RealRestrictedTokenProductChainVerified",
            "RealFixedCapacityVolumeVerified",
            "RealDiskFullVerified",
            "RealPowerLossVerified",
            "RealAbnormalExitMatrixVerified",
            "EnterpriseAppLockerWacEdRMatrixVerified",
            "EnterpriseRetentionArchiveMatrixVerified")) {
        $evidence | Add-Member -NotePropertyName $flag -NotePropertyValue ([bool] $Complete)
    }
    return $evidence
}

if ($SelfTestOnly) {
    $head = "a" * 40

    # 全部满足时必须放行，否则这个门禁永远无法通过，等于没有判定能力。
    $ready = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence -Complete) `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $true
    if ($ready.Count -ne 0) {
        throw ("自检失败：满足全部前置条件时仍被拒绝：" + ($ready -join " / "))
    }

    # 今天的真实形态：M4 未完成 + 真实矩阵全未验证，必须被拒绝。
    $today = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence) `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $true
    if ($today.Count -lt 2) {
        throw "自检失败：M4 未完成且真实矩阵未验证时没有被拒绝。"
    }

    $dirty = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence -Complete) `
        -ExpectedHeadCommit $head -WorkingTreeClean $false -RollbackRefResolvesToHead $true
    if ($dirty.Count -eq 0) {
        throw "自检失败：工作树不干净时没有被拒绝。"
    }

    $noRollback = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence -Complete) `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $false
    if ($noRollback.Count -eq 0) {
        throw "自检失败：缺少回滚点时没有被拒绝。"
    }

    $otherHead = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence -Complete) `
        -ExpectedHeadCommit ("b" * 40) -WorkingTreeClean $true -RollbackRefResolvesToHead $true
    if ($otherHead.Count -eq 0) {
        throw "自检失败：evidence 绑定到其他提交时没有被拒绝。"
    }

    $weak = New-FreezeSelfTestEvidence -Complete
    $weak.RunCorrelation.Mode = "FreshnessOnly"
    $weakResult = Test-FreezePrecondition -Evidence $weak `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $true
    if ($weakResult.Count -eq 0) {
        throw "自检失败：FreshnessOnly 的弱关联证据被接受为冻结依据。"
    }

    $writeOn = New-FreezeSelfTestEvidence -Complete
    $writeOn.CadWriteEnabled = $true
    $writeOnResult = Test-FreezePrecondition -Evidence $writeOn `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $true
    if ($writeOnResult.Count -eq 0) {
        throw "自检失败：CAD 写入已启用时仍允许冻结。"
    }

    Write-Host "M4_16_FREEZE_PRECONDITION_SELF_TEST=passed"
    return
}

if ([string]::IsNullOrWhiteSpace($ReadinessEvidencePath)) {
    throw "缺少 -ReadinessEvidencePath。"
}
$resolvedReadiness = if ([IO.Path]::IsPathRooted($ReadinessEvidencePath)) {
    [IO.Path]::GetFullPath($ReadinessEvidencePath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $ReadinessEvidencePath))
}
if (-not (Test-Path -LiteralPath $resolvedReadiness -PathType Leaf)) {
    throw "readiness evidence 不存在。"
}
$readinessJson = Get-Content -LiteralPath $resolvedReadiness -Raw -Encoding UTF8 | ConvertFrom-Json

$headCommit = (& git -c "safe.directory=$safeRepoRoot" -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $headCommit -cnotmatch "^[0-9a-f]{40}$") {
    throw "无法读取当前 Git HEAD。"
}
$workingTreeClean = @(& git -c "safe.directory=$safeRepoRoot" -C $repoRoot status --porcelain).Count -eq 0

$rollbackResolves = $false
if (-not [string]::IsNullOrWhiteSpace($RollbackRef)) {
    # 只读取。本脚本不创建、不移动、不删除任何 ref。
    # ref 不存在是预期分支而不是异常；PowerShell 7.4 起原生命令的非零退出码在
    # $ErrorActionPreference='Stop' 下会抛出，所以这里必须显式接住。
    try {
        $resolved = (& git -c "safe.directory=$safeRepoRoot" -C $repoRoot rev-parse --verify `
            --quiet ($RollbackRef + "^{commit}") 2>$null)
        if ($LASTEXITCODE -eq 0 -and $null -ne $resolved) {
            $rollbackResolves = (([string] $resolved).Trim() -ceq $headCommit)
        }
    }
    catch {
        $rollbackResolves = $false
    }
}

$blockers = Test-FreezePrecondition -Evidence $readinessJson `
    -ExpectedHeadCommit $headCommit -WorkingTreeClean $workingTreeClean `
    -RollbackRefResolvesToHead $rollbackResolves

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $resolvedEvidencePath = if ([IO.Path]::IsPathRooted($EvidencePath)) {
        [IO.Path]::GetFullPath($EvidencePath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidencePath))
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidencePath) -Force | Out-Null
    $report = [ordered]@{
        SchemaVersion = 1
        RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
        RunCorrelationId = Get-CodexGateRunCorrelationId
        Scope = "m4-16-freeze-precondition-check"
        Status = if ($blockers.Count -eq 0) { "preconditions_met" } else { "freeze_refused" }
        HeadCommit = $headCommit
        WorkingTreeClean = $workingTreeClean
        RollbackRefResolvesToHead = $rollbackResolves
        BlockerCount = $blockers.Count
        Blockers = @($blockers)
        M416Frozen = $false
        EvidenceBoundary = "This evidence records only whether the M4.16 freeze preconditions currently hold. It does not build a candidate, produce candidate hashes, create or move any Git ref, start or command AutoCAD, enable CAD writes or saves, or freeze M4.16."
    }
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($resolvedEvidencePath, ($report | ConvertTo-Json -Depth 8), $encoding)
    Write-Host ("M4_16_FREEZE_PRECONDITION_EVIDENCE=" + $resolvedEvidencePath)
}

Complete-CodexBuildSafety -State $buildSafety -Stage "m4-16-freeze-preconditions" | Out-Null

if ($blockers.Count -eq 0) {
    Write-Host "`nM4.16 冻结前置条件全部满足；候选构建与冻结仍需单独执行。" -ForegroundColor Green
    Write-Host "M4_16_FREEZE_PRECONDITIONS=met"
    exit 0
}

Write-Host "`nM4.16 冻结被拒绝，共 $($blockers.Count) 项未满足：" -ForegroundColor Yellow
foreach ($blocker in $blockers) {
    Write-Host ("  - " + $blocker)
}
Write-Host "M4_16_FREEZE_PRECONDITIONS=refused"
exit 1
