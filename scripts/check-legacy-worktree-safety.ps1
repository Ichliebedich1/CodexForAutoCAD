# 历史 Worktree PATH 污染风险面体检。只读：不构建、不调用 dotnet、不改写任何脚本、
# 不修改任何环境变量。
#
# 背景：2026-07-24 与 2026-07-25 两次事故后，只有当前活动 Worktree 的脚本被修复为
# "设置 DOTNET_CLI_HOME 的同作用域必须设置 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0"。
# 历史 Worktree 按事故记录与用户要求一律不批量改写，因此它们仍保留大量未防护赋值点。
#
# 这些历史赋值点当前之所以无害，唯一原因是用户级 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0
# 覆盖了它们。该变量是当前唯一的真实防线：一旦它被删除、被改为非 0、被旧注册表备份
# 覆盖，或换到另一个没有该变量的 Windows 用户账户，运行任意历史 Worktree 脚本都会
# 立即恢复污染用户 PATH 的行为。
#
# 本脚本回答一个问题：防线是否还在，以及一旦失守暴露面有多大。
#
# 用法：
#   pwsh -NoProfile -File scripts\check-legacy-worktree-safety.ps1
#   powershell -NoProfile -File scripts\check-legacy-worktree-safety.ps1
#
# 退出码：0 防线完好；1 需要处理。
#
# 本文件必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。

[CmdletBinding()]
param(
    [string] $WorktreeRoot = 'C:\tmp',
    [string] $WorktreeFilter = 'CodexForAutoCAD*',
    [switch] $ListFiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'build-safety.ps1')

$guardUser = [Environment]::GetEnvironmentVariable('DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', 'User')
$guardProcess = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
$pathState = Get-CodexUserPathState

$reports = New-Object System.Collections.ArrayList
$skippedReparse = 0

if (Test-Path -LiteralPath $WorktreeRoot -PathType Container) {
    foreach ($dir in (Get-ChildItem -LiteralPath $WorktreeRoot -Directory -Force `
                -Filter $WorktreeFilter -ErrorAction SilentlyContinue)) {
        # 不进入 reparse point：它们可能指向主仓库或其他卷。
        if (($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $skippedReparse++
            continue
        }
        $report = Get-CodexUnguardedCliHomeSites -Root $dir.FullName
        if ($report.CliHomeAssignmentSiteCount -gt 0) {
            $null = $reports.Add($report)
        }
    }
}

$all = @($reports.ToArray())
$exposed = @($all | Where-Object { $_.UnguardedSiteCount -gt 0 })
$totalSites = 0
$totalUnguarded = 0
$totalUnguardedFiles = 0
foreach ($r in $all) {
    $totalSites += $r.CliHomeAssignmentSiteCount
    $totalUnguarded += $r.UnguardedSiteCount
    $totalUnguardedFiles += $r.UnguardedFileCount
}

Write-Output ("GUARD_USER_SCOPE=" + $(if ($null -eq $guardUser) { '<unset>' } else { $guardUser }))
Write-Output ("GUARD_PROCESS_SCOPE=" + $(if ($null -eq $guardProcess) { '<unset>' } else { $guardProcess }))
Write-Output ("USER_PATH_LENGTH=" + $pathState.Length)
Write-Output ("USER_PATH_ENTRIES=" + $pathState.EntryCount)
Write-Output ("USER_PATH_SHA256=" + $pathState.Sha256)
Write-Output ("USER_PATH_POLLUTING_ENTRIES=" + $pathState.PollutingEntryCount)
Write-Output ("WORKTREES_SCANNED=" + $all.Count)
Write-Output ("WORKTREES_SKIPPED_REPARSE=" + $skippedReparse)
Write-Output ("WORKTREES_WITH_UNGUARDED_SITES=" + $exposed.Count)
Write-Output ("TOTAL_CLI_HOME_SITES=" + $totalSites)
Write-Output ("TOTAL_UNGUARDED_SITES=" + $totalUnguarded)
Write-Output ("TOTAL_UNGUARDED_FILES=" + $totalUnguardedFiles)

if ($ListFiles) {
    foreach ($r in ($exposed | Sort-Object UnguardedSiteCount -Descending)) {
        Write-Output ("  " + $r.WorktreeName + " unguarded=" + $r.UnguardedSiteCount +
            " files=" + $r.UnguardedFileCount)
        foreach ($f in $r.UnguardedFiles) {
            Write-Output ("      " + $f.File + " (" + $f.UnguardedSiteCount + ")")
        }
    }
}

$problems = New-Object System.Collections.ArrayList

if ($pathState.PollutingEntryCount -ne 0) {
    $null = $problems.Add(
        "用户 PATH 已出现 $($pathState.PollutingEntryCount) 个临时 .dotnet\tools 污染项，污染正在发生。")
}
if ($guardUser -cne '0') {
    $null = $problems.Add(
        "用户级 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH 不是 0，$totalUnguarded 处历史未防护赋值点已全部失去保护。")
}

if ($problems.Count -eq 0) {
    Write-Output 'LEGACY_WORKTREE_SAFETY=ok'
    Write-Output ("防线完好：用户级防护变量为 0，覆盖 " + $exposed.Count + " 个历史 Worktree 的 " +
        $totalUnguarded + " 处未防护赋值点。")
    Write-Output '注意：该变量是用户级的，换 Windows 用户账户后不会自动继承。'
    exit 0
}

Write-Output 'LEGACY_WORKTREE_SAFETY=action-required'
foreach ($problem in $problems) {
    Write-Output ("  - " + $problem)
}
Write-Output '处理指引：'
Write-Output '  1) 恢复用户级 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0，再运行任何验证脚本。'
Write-Output '  2) 只移除精确匹配 CodexForAutoCAD 临时 .dotnet\tools 的 PATH 项，保留正常开发路径。'
Write-Output '  3) 不要导入 2026-07-25 的注册表备份：那是修复前快照，会把污染项带回来。'
Write-Output '  4) 不要批量改写历史 Worktree；需要时从已修复的主仓脚本重建。'
exit 1
