# 独立的用户 PATH 体检脚本，不构建、不调用 dotnet、不修改任何环境变量。
#
# 用途：2026-07-24 / 2026-07-25 两次 Explorer 白屏事故的根因是用户 PATH 被
# .NET CLI 追加了数百个临时 .dotnet\tools 目录，膨胀到约 32K 字符后破坏登录环境。
# 本脚本给出可随时手动复核的健康指标，不输出 PATH 明文。
#
# 用法：
#   pwsh -NoProfile -File scripts\check-user-path-health.ps1
#   powershell -NoProfile -File scripts\check-user-path-health.ps1
#
# 退出码：0 健康；1 需要处理。

[CmdletBinding()]
param(
    # 事故中 PATH 达到约 32K 字符时登录环境开始失败，这里留出充足余量。
    [int] $WarnLength = 4096,
    [int] $FailLength = 8192
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'build-safety.ps1')

$state = Get-CodexUserPathState
$guardUser = [Environment]::GetEnvironmentVariable('DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', 'User')

$problems = New-Object System.Collections.ArrayList

if ($state.PollutingEntryCount -ne 0) {
    $null = $problems.Add("用户 PATH 存在 $($state.PollutingEntryCount) 个 CodexForAutoCAD 临时 .dotnet\tools 污染项。")
}
if ($state.Length -ge $FailLength) {
    $null = $problems.Add("用户 PATH 长度 $($state.Length) 已达失败阈值 $FailLength。")
}
elseif ($state.Length -ge $WarnLength) {
    $null = $problems.Add("用户 PATH 长度 $($state.Length) 已超警告阈值 $WarnLength。")
}
if ($guardUser -cne '0') {
    $null = $problems.Add('用户级 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH 不是 0，防复发开关缺失。')
}

Write-Output ("USER_PATH_LENGTH=" + $state.Length)
Write-Output ("USER_PATH_ENTRIES=" + $state.EntryCount)
Write-Output ("USER_PATH_SHA256=" + $state.Sha256)
Write-Output ("USER_PATH_POLLUTING_ENTRIES=" + $state.PollutingEntryCount)
Write-Output ("USER_PATH_MISSING_DIRECTORY_ENTRIES=" + $state.MissingDirectoryEntryCount)
Write-Output ("USER_GUARD_DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=" +
    $(if ($null -eq $guardUser) { '<unset>' } else { $guardUser }))
Write-Output ("WARN_LENGTH=" + $WarnLength + " FAIL_LENGTH=" + $FailLength)

if ($problems.Count -eq 0) {
    Write-Output 'USER_PATH_HEALTH=ok'
    exit 0
}

Write-Output 'USER_PATH_HEALTH=action-required'
foreach ($problem in $problems) {
    Write-Output ("  - " + $problem)
}
Write-Output '处理指引：只移除精确匹配 CodexForAutoCAD 临时 .dotnet\tools 的项，保留正常开发路径；'
Write-Output '不要导入 2026-07-25 的注册表备份（会把污染项带回来），也不要删除用户配置或 AppData。'
exit 1
