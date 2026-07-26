# CodexForAutoCAD DOTNET CLI PATH 防复发门禁（统一版）。
#
# 本文件取代早期主仓库版本。早期版本用整文件级 Contains 判断防护变量是否存在：
# 只要文件任意位置出现过一次 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH 就判定整个文件通过，
# 因此会放过"顶部防护一次、后续多处赋值无防护"的写法——而这正是 2026-07-24 与
# 2026-07-25 两次事故的实际形态。早期版本的污染项正则以 tools$ 锚定，也会漏掉
# 带尾部分隔符的 PATH 项。统一版改用 build-safety.ps1 的行级窗口规则 R1-R5。
#
# 输出契约与早期版本保持兼容：单个压缩 JSON 对象，包含 Status、UserPathLength、
# UserPathEntryCount、UserPathSha256、TemporaryToolEntryCount 和 GuardedScriptCount，
# 因此 verify-phase2.ps1 的前置/后置调用无需修改即可升级到强检测。
#
# 本文件不调用 dotnet，不写入任何持久环境变量，不记录 PATH 明文。
# 必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。

[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..'),

    # 由 verify-phase2.ps1 后置调用传入前置快照，确保门禁期间用户 PATH 未被改动。
    [string] $ExpectedUserPathSha256 = '',

    # 事故中 PATH 达到约 32K 字符时登录环境开始失败，这里保留充足余量。
    [int] $MaxUserPathLength = 8191,

    # PathGuardOnly 只强制 R1/R2，即直接导致用户 PATH 污染的两类规则；
    # R3 安全入口、R4 产物根目录、R5 双 Shell 编码属于尚未完成的治理项，
    # 会被报告为 Suppressed* 计数但不阻塞，避免本门禁在尚未改造完的仓库中无法运行。
    # Strict 强制 R1-R5 全部，供已完成改造的 Worktree 使用。
    [ValidateSet('PathGuardOnly', 'Strict')]
    [string] $StaticGateMode = 'PathGuardOnly'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'build-safety.ps1')

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

$state = Get-CodexUserPathState

if ($state.PollutingEntryCount -ne 0) {
    throw "用户 PATH 包含 $($state.PollutingEntryCount) 个 CodexForAutoCAD 临时 .dotnet\tools 目录。"
}
if ($state.Length -gt $MaxUserPathLength) {
    throw "用户 PATH 长度 $($state.Length) 超过 $MaxUserPathLength 字符保护阈值。"
}
if ((-not [string]::IsNullOrWhiteSpace($ExpectedUserPathSha256)) -and
    ($state.Sha256 -cne $ExpectedUserPathSha256)) {
    throw "验证期间用户 PATH 已改变。"
}

# 行级窗口静态门禁：R1 防护变量、R2 持久 PATH 写入、R3 安全入口、R4 产物根、R5 双 Shell 编码。
$gate = Invoke-CodexBuildSafetyStaticGate -RepoRoot $RepositoryRoot

if ($StaticGateMode -ceq 'Strict') {
    $enforced = @($gate.Violations)
    $suppressed = @()
}
else {
    # 只强制与 PATH 污染直接相关的规则；其余仍然统计并回报，不静默丢弃。
    $enforced = @($gate.Violations | Where-Object { $_.Rule -match '^R[12]-' })
    $suppressed = @($gate.Violations | Where-Object { $_.Rule -notmatch '^R[12]-' })
}

if (@($enforced).Count -ne 0) {
    $summary = ($enforced | ForEach-Object {
        if ($_.Line -gt 0) { "$($_.Rule) $($_.File):$($_.Line)" } else { "$($_.Rule) $($_.File)" }
    }) -join '; '
    throw "DOTNET CLI PATH 防复发门禁失败（$(@($enforced).Count) 项）：$summary"
}

# 被抑制的违规必须可见，否则治理欠账会被门禁的 passed 掩盖。
$suppressedSummary = ''
if (@($suppressed).Count -ne 0) {
    $suppressedSummary = (($suppressed | Group-Object { ($_.Rule -split '-')[0] } |
        Sort-Object Name | ForEach-Object { "$($_.Name):$($_.Count)" }) -join ',')
}

# 只输出指纹与计数，不输出 PATH 明文、绝对路径或环境变量内容。
[pscustomobject]@{
    Status = 'passed'
    SchemaVersion = 2
    UserPathLength = $state.Length
    UserPathEntryCount = $state.EntryCount
    UserPathSha256 = $state.Sha256
    UserPathSha256Encoding = 'utf-8'
    TemporaryToolEntryCount = $state.PollutingEntryCount
    MissingDirectoryEntryCount = $state.MissingDirectoryEntryCount
    GuardedScriptCount = $gate.ScannedPowerShellFileCount
    ScannedSourceFileCount = $gate.ScannedSourceFileCount
    CliHomeAssignmentSiteCount = $gate.DotnetCliHomeAssignmentSiteCount
    StaticGateMode = $StaticGateMode
    StaticGateViolationCount = @($enforced).Count
    SuppressedViolationCount = @($suppressed).Count
    SuppressedRuleSummary = $suppressedSummary
    TotalStaticGateFindingCount = $gate.ViolationCount
    MaxUserPathLength = $MaxUserPathLength
} | ConvertTo-Json -Compress
