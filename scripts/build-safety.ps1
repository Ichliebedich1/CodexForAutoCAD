# CodexForAutoCAD 统一构建安全入口。
#
# 背景：2026-07-24 与 2026-07-25 两次事故中，验证脚本为隔离构建创建了大量临时
# DOTNET_CLI_HOME，却没有在同一作用域设置 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0。
# .NET CLI 因此把每个临时 .dotnet\tools 目录永久写入用户 PATH，PATH 膨胀到约
# 32K 字符后破坏了 Windows 登录环境。详见
# handoff/autocad2016/DOTNET_CLI_PATH_INCIDENT_20260725.md。
#
# 本文件必须同时兼容 PowerShell 7 与 Windows PowerShell 5.1，且必须保存为
# UTF-8 with BOM：Windows PowerShell 5.1 会用系统 ANSI 代码页（本机为 936）
# 解码无 BOM 的 .ps1，含中文时会破坏字符串边界并产生解析错误。

$ErrorActionPreference = "Stop"

function Get-CodexTrimmedPath {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Path)

    # 注意：不要写成 TrimEnd("\\", "/")。PowerShell 的双引号字符串不做反斜杠转义，
    # "\\" 是两个字符，无法转换为 System.Char，会在两个 Shell 下都抛 MethodException。
    return $Path.TrimEnd([char[]]@('\', '/'))
}

function Get-CodexTextSha256 {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Value)

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

function Get-CodexUserPathState {
    # 只读取用户级 PATH 并返回指纹；调用方不得记录 PATH 明文。
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($null -eq $userPath) {
        $userPath = ""
    }
    $entries = @(
        $userPath -split ";" |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    $pollutingEntries = @(
        $entries | Where-Object {
            $_ -match "(?i)CodexForAutoCAD.*[\\/](?:dotnet-home|dotnet-cli-home|\.dotnet)[\\/](?:\.dotnet[\\/])?tools(?:[\\/]|$)" -or
            $_ -match "(?i)CodexForAutoCAD.*[\\/]\.dotnet[\\/]tools(?:[\\/]|$)"
        }
    )
    # 事故中残留项的共同特征是目录已被清理但 PATH 项仍在。该计数只作遥测，
    # 不作为失败条件：正常开发环境也可能存在少量暂不存在的路径。
    $missingEntries = @(
        $entries | Where-Object {
            $exists = $false
            try {
                $expanded = [Environment]::ExpandEnvironmentVariables($_)
                $exists = [IO.Directory]::Exists($expanded)
            }
            catch {
                $exists = $false
            }
            -not $exists
        }
    )

    return [pscustomobject]@{
        Length = $userPath.Length
        EntryCount = $entries.Count
        Sha256 = Get-CodexTextSha256 -Value $userPath
        PollutingEntryCount = $pollutingEntries.Count
        MissingDirectoryEntryCount = $missingEntries.Count
    }
}

function Assert-CodexUserPathSafe {
    param(
        [Parameter(Mandatory = $true)] $ExpectedState,
        [string] $Stage = "build"
    )

    $actualState = Get-CodexUserPathState
    if ($actualState.PollutingEntryCount -ne 0) {
        throw "构建安全门禁拒绝继续：用户 PATH 出现 CodexForAutoCAD 临时 .dotnet\tools 污染项。"
    }
    if ($actualState.Length -ne [int] $ExpectedState.Length) {
        throw "构建安全门禁拒绝继续：$Stage 前后用户 PATH 长度发生变化。"
    }
    if ($actualState.EntryCount -ne [int] $ExpectedState.EntryCount) {
        throw "构建安全门禁拒绝继续：$Stage 前后用户 PATH 项目数发生变化。"
    }
    if ($actualState.Sha256 -cne [string] $ExpectedState.Sha256) {
        throw "构建安全门禁拒绝继续：$Stage 前后用户 PATH 指纹发生变化。"
    }
    return $actualState
}

function Get-CodexDotnetIsolationGuard {
    # 供各验证脚本在构造隔离环境字典时展开，确保防护变量与 DOTNET_CLI_HOME 同作用域。
    return [ordered]@{
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
    }
}

function Resolve-CodexArtifactRoot {
    param(
        [Parameter(Mandatory = $true)][string] $RepoRoot,
        [double] $MinimumFreeGiB = 40,
        # 隔离构建会在产物根下生成很深的路径，实测最长约 192 字符，例如
        # <root>\autocad2016-agent-bootstrap-<32hex>\build-a\out\obj\<长项目名>\release_net45\
        # <长项目名>.exe.withSupportedRuntime.config。Windows MAX_PATH 为 260 且本机
        # LongPathsEnabled=0，因此产物根本身必须留出足够预算，否则 net45 隔离构建会以
        # MSB3030 失败。2026-07-26 把产物根从 C:\tmp 迁到 E 盘时长度增加 23 字符，
        # 正是这样打断了 agent-bootstrap 与 auth-compat 门禁。
        [int] $MaximumArtifactRootLength = 60,
        [switch] $NoCreate
    )

    $resolvedRepoRoot = Get-CodexTrimmedPath ([IO.Path]::GetFullPath($RepoRoot))
    $artifactBase = [Environment]::GetEnvironmentVariable(
        "CODEX_AUTOCAD_ARTIFACT_BASE",
        "Process")
    if ([string]::IsNullOrWhiteSpace($artifactBase)) {
        $artifactBase = [Environment]::GetEnvironmentVariable(
            "CODEX_AUTOCAD_ARTIFACT_BASE",
            "User")
    }

    if ([string]::IsNullOrWhiteSpace($artifactBase)) {
        $artifactRoot = Join-Path $resolvedRepoRoot "artifacts"
    }
    else {
        if ($artifactBase -match '^\\\\') {
            throw "CODEX_AUTOCAD_ARTIFACT_BASE 不接受 UNC 或设备命名空间路径。"
        }
        if (-not [IO.Path]::IsPathRooted($artifactBase)) {
            throw "CODEX_AUTOCAD_ARTIFACT_BASE 必须是绝对路径。"
        }
        $resolvedBase = Get-CodexTrimmedPath ([IO.Path]::GetFullPath($artifactBase))
        # 按 Worktree 目录名隔离，避免并行任务互相覆盖产物。
        $repoName = Split-Path -Leaf $resolvedRepoRoot
        if ([string]::IsNullOrWhiteSpace($repoName)) {
            throw "无法从仓库路径生成隔离的产物目录名称。"
        }
        $artifactRoot = Join-Path $resolvedBase $repoName
    }

    $artifactRoot = Get-CodexTrimmedPath ([IO.Path]::GetFullPath($artifactRoot))
    $driveRoot = Get-CodexTrimmedPath ([IO.Path]::GetPathRoot($artifactRoot))
    if ($artifactRoot -ieq $driveRoot -or $artifactRoot -ieq $resolvedRepoRoot) {
        throw "产物根目录不能是卷根目录或仓库根目录。"
    }
    # fail-closed：在任何构建开始前拒绝过长的产物根，而不是等 MSBuild 报 MSB3030。
    if ($artifactRoot.Length -gt $MaximumArtifactRootLength) {
        throw ("产物根目录路径过长：$($artifactRoot.Length) 字符，门禁上限 " +
            "$MaximumArtifactRootLength。隔离 net45 构建会在其下生成约 192 字符的深层路径，" +
            "超过 Windows MAX_PATH 260 会导致 MSB3030。请改用更短的 " +
            "CODEX_AUTOCAD_ARTIFACT_BASE。")
    }

    $drive = New-Object IO.DriveInfo([IO.Path]::GetPathRoot($artifactRoot))
    if (-not $drive.IsReady) {
        throw "产物卷当前不可用。"
    }
    # fail-closed：空间不足时在写入前拒绝，而不是等磁盘写满才失败。
    $freeGiB = [math]::Round($drive.AvailableFreeSpace / 1GB, 2)
    if ($freeGiB -lt $MinimumFreeGiB) {
        throw "产物卷剩余空间不足：当前 $freeGiB GiB，门禁要求至少 $MinimumFreeGiB GiB。"
    }

    if (-not $NoCreate) {
        New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    }
    return $artifactRoot
}

function Initialize-CodexBuildSafety {
    param(
        [Parameter(Mandatory = $true)][string] $RepoRoot,
        [double] $MinimumFreeGiB = 40,
        # 见 Resolve-CodexArtifactRoot：生产默认 60。自检使用 GUID 隔离目录，路径天然更长，
        # 会显式放宽该上限，因为它不执行 net45 隔离构建。
        [int] $MaximumArtifactRootLength = 60,
        [switch] $NoCreateArtifactRoot
    )

    $pathBefore = Get-CodexUserPathState
    if ($pathBefore.PollutingEntryCount -ne 0) {
        throw "构建安全门禁拒绝启动：用户 PATH 已含 CodexForAutoCAD 临时 .dotnet\tools 污染项。"
    }

    $previousAddGlobalToolsToPath = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
    # 进程级兜底：本进程及其全部子进程都禁止 .NET CLI 自动写入 PATH。
    # 各脚本在设置 DOTNET_CLI_HOME 时仍必须在同一作用域重复设置该变量。
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
    $artifactRoot = Resolve-CodexArtifactRoot -RepoRoot $RepoRoot `
        -MinimumFreeGiB $MinimumFreeGiB `
        -MaximumArtifactRootLength $MaximumArtifactRootLength `
        -NoCreate:$NoCreateArtifactRoot

    return [pscustomobject]@{
        ArtifactRoot = $artifactRoot
        UserPathBefore = $pathBefore
        PreviousAddGlobalToolsToPath = $previousAddGlobalToolsToPath
        MinimumFreeGiB = $MinimumFreeGiB
    }
}

function Complete-CodexBuildSafety {
    param(
        [Parameter(Mandatory = $true)] $State,
        [string] $Stage = "build"
    )

    if ($env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH -cne "0") {
        throw "构建安全门禁拒绝完成：DOTNET_ADD_GLOBAL_TOOLS_TO_PATH 不再为 0。"
    }
    return Assert-CodexUserPathSafe -ExpectedState $State.UserPathBefore -Stage $Stage
}

function Invoke-CodexBuildSafetyStaticGate {
    <#
    .SYNOPSIS
        纯静态门禁，阻止本次事故类问题以新脚本或新代码的形式复发。不调用 dotnet。
    .DESCRIPTION
        R1 设置 DOTNET_CLI_HOME 的位置附近必须出现 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH。
        R2 禁止 setx 或 User/Machine 目标写入环境变量。
        R3 调用 dotnet/MSBuild 的脚本必须经过统一安全入口。
        R4 禁止硬编码 <Worktree>\artifacts，必须使用统一产物根目录。
        R5 scripts 下的 .ps1 必须是纯 ASCII 或带 UTF-8 BOM，保证双 Shell 可解析。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $RepoRoot,
        [int] $GuardWindowLines = 25
    )

    $resolvedRepoRoot = Get-CodexTrimmedPath ([IO.Path]::GetFullPath($RepoRoot))
    $scriptDirectory = Join-Path $resolvedRepoRoot "scripts"

    # 门禁定义文件自身包含规则文本，内容型规则 R1-R4 不扫描它们；
    # 作为替代，下方对它们施加更强的不变量：不得设置 DOTNET_CLI_HOME。
    $gateDefinitionNames = @(
        "build-safety.ps1",
        "verify-build-safety.ps1",
        "verify-dotnet-cli-path-guard.ps1")

    $powerShellFiles = @()
    if (Test-Path -LiteralPath $scriptDirectory -PathType Container) {
        $powerShellFiles = @(
            Get-ChildItem -LiteralPath $scriptDirectory -Recurse -File -Filter "*.ps1" |
                Sort-Object FullName
        )
    }

    $sourceFiles = @()
    foreach ($sourceDirectoryName in @("src", "tests")) {
        $sourceDirectory = Join-Path $resolvedRepoRoot $sourceDirectoryName
        if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
            continue
        }
        $sourceFiles += @(
            Get-ChildItem -LiteralPath $sourceDirectory -Recurse -File -Filter "*.cs" |
                Where-Object { $_.FullName -notmatch "[\\/](?:bin|obj|packages|artifacts)[\\/]" }
        )
    }
    $sourceFiles = @($sourceFiles | Sort-Object FullName)

    $cliHomeAssignmentPattern = '\bDOTNET_CLI_HOME\b["'']?\s*\]?\s*[=,]'
    $guardPattern = '\bDOTNET_ADD_GLOBAL_TOOLS_TO_PATH\b'
    $dotnetUsagePattern = '(?i)(?:Get-Command\s+dotnet|dotnet\.exe|\bmsbuild\.exe|\bMSBuildPath\b|Invoke-DotNet)'
    $hardcodedArtifactPattern = '(?i)(?:Join-Path\s+\$repoRoot\s+[''"]artifacts[''"]|\$repoRoot\\artifacts)'

    $violations = New-Object System.Collections.ArrayList
    $cliHomeSiteCount = 0

    function Add-CodexGateViolation {
        param(
            [Parameter(Mandatory = $true)] $Sink,
            [Parameter(Mandatory = $true)][string] $Rule,
            [Parameter(Mandatory = $true)][string] $RelativePath,
            [int] $Line = 0,
            [string] $Detail = ""
        )
        # 只记录仓库相对路径，不记录绝对路径或用户名。
        $null = $Sink.Add([pscustomobject]@{
            Rule = $Rule
            File = $RelativePath
            Line = $Line
            Detail = $Detail
        })
    }

    $allScannedFiles = @($powerShellFiles) + @($sourceFiles)
    foreach ($file in $allScannedFiles) {
        $relativePath = $file.FullName.Substring($resolvedRepoRoot.Length).TrimStart([char[]]@('\', '/')).Replace("\", "/")
        $isGateDefinition = ($file.Extension -ieq ".ps1") -and ($gateDefinitionNames -contains $file.Name)
        $isPowerShell = ($file.Extension -ieq ".ps1")

        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        $hasNonAscii = $false
        foreach ($b in $bytes) {
            if ($b -gt 0x7F) { $hasNonAscii = $true; break }
        }

        # R5：只对 scripts 下的 PowerShell 脚本生效，含门禁定义文件自身。
        if ($isPowerShell -and $hasNonAscii -and -not $hasBom) {
            Add-CodexGateViolation -Sink $violations -Rule "R5-DualShellEncoding" `
                -RelativePath $relativePath `
                -Detail "含非 ASCII 字符但缺少 UTF-8 BOM，Windows PowerShell 5.1 将按 ANSI 代码页误解码。"
        }

        $lines = [IO.File]::ReadAllLines($file.FullName)
        $fileText = ($lines -join "`n")

        for ($i = 0; $i -lt $lines.Length; $i++) {
            $line = $lines[$i]

            # 对门禁定义文件施加更强不变量：完全不得设置 DOTNET_CLI_HOME。
            if ($isGateDefinition) {
                if ($line -match ('\$env:' + 'DOTNET_CLI_HOME\s*=')) {
                    Add-CodexGateViolation -Sink $violations -Rule "R1-GateDefinitionMustNotSetCliHome" `
                        -RelativePath $relativePath -Line ($i + 1)
                }
            }

            # R1：DOTNET_CLI_HOME 赋值点附近必须有防护变量。
            if (-not $isGateDefinition -and $line -match $cliHomeAssignmentPattern) {
                $cliHomeSiteCount++
                $windowStart = [Math]::Max(0, $i - $GuardWindowLines)
                $windowEnd = [Math]::Min($lines.Length - 1, $i + $GuardWindowLines)
                $window = ($lines[$windowStart..$windowEnd] -join "`n")
                if ($window -notmatch $guardPattern) {
                    Add-CodexGateViolation -Sink $violations -Rule "R1-MissingPathGuard" `
                        -RelativePath $relativePath -Line ($i + 1) `
                        -Detail "设置 DOTNET_CLI_HOME 但同作用域 $GuardWindowLines 行内没有 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH。"
                }
            }

            # R2：禁止 setx 与 User/Machine 目标写入。
            # 只匹配命令位置的 setx，避免把规则文本或注释误判为调用。
            if (-not $isGateDefinition -and $line -match '(?i)(?:^|[;&|(]|\s)setx(?:\.exe)?\s+\S') {
                Add-CodexGateViolation -Sink $violations -Rule "R2-ForbiddenSetx" `
                    -RelativePath $relativePath -Line ($i + 1)
            }
            if (-not $isGateDefinition -and $line -match 'SetEnvironmentVariable') {
                $windowEnd = [Math]::Min($lines.Length - 1, $i + 2)
                $callWindow = ($lines[$i..$windowEnd] -join " ")
                # 只匹配显式的持久化目标实参，避免把 ::Process 调用附近的注释误判。
                if ($callWindow -match 'EnvironmentVariableTarget\s*(?:\]::|\.)\s*(?:User|Machine)\b' -or
                    $callWindow -match ',\s*["''](?:User|Machine)["'']\s*\)') {
                    Add-CodexGateViolation -Sink $violations -Rule "R2-ForbiddenPersistentEnvironmentWrite" `
                        -RelativePath $relativePath -Line ($i + 1) `
                        -Detail "禁止以 User/Machine 目标持久化写入环境变量。"
                }
            }

            # R4：禁止硬编码 Worktree 下的 artifacts 目录。
            if (-not $isGateDefinition -and $isPowerShell -and $line -match $hardcodedArtifactPattern) {
                Add-CodexGateViolation -Sink $violations -Rule "R4-HardcodedArtifactRoot" `
                    -RelativePath $relativePath -Line ($i + 1) `
                    -Detail "必须使用 Resolve-CodexArtifactRoot 或 `$buildSafety.ArtifactRoot。"
            }
        }

        # R3：调用 dotnet/MSBuild 的脚本必须经过统一安全入口。
        if (-not $isGateDefinition -and $isPowerShell -and $fileText -match $dotnetUsagePattern) {
            if ($fileText -notmatch '\bInitialize-CodexBuildSafety\b') {
                Add-CodexGateViolation -Sink $violations -Rule "R3-DotnetOutsideSafeEntry" `
                    -RelativePath $relativePath `
                    -Detail "调用 dotnet/MSBuild 但没有经过 Initialize-CodexBuildSafety。"
            }
        }
    }

    $violationArray = @($violations.ToArray())
    return [pscustomobject]@{
        ScannedPowerShellFileCount = @($powerShellFiles).Count
        ScannedSourceFileCount = @($sourceFiles).Count
        DotnetCliHomeAssignmentSiteCount = $cliHomeSiteCount
        ViolationCount = $violationArray.Count
        Violations = $violationArray
    }
}

function Get-CodexUnguardedCliHomeSites {
    <#
    .SYNOPSIS
        对任意 Worktree 只读统计缺少防护变量的 DOTNET_CLI_HOME 赋值点。
    .DESCRIPTION
        只实现 R1，用于历史 Worktree 的风险面体检。历史 Worktree 未接入统一安全入口，
        对它们跑完整门禁会产生大量 R3/R4/R5 噪音，因此这里只回答一个问题：
        "如果用户级防护变量消失，这个 Worktree 里有多少处会立刻污染用户 PATH。"
        本函数不修改、不重写、不删除任何文件。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [int] $GuardWindowLines = 25
    )

    # 必须与 Invoke-CodexBuildSafetyStaticGate 的 R1 定义保持一致。
    $cliHomeAssignmentPattern = '\bDOTNET_CLI_HOME\b["'']?\s*\]?\s*[=,]'
    $guardPattern = '\bDOTNET_ADD_GLOBAL_TOOLS_TO_PATH\b'

    $resolvedRoot = Get-CodexTrimmedPath ([IO.Path]::GetFullPath($Root))
    $scriptDirectory = Join-Path $resolvedRoot "scripts"

    $files = @()
    if (Test-Path -LiteralPath $scriptDirectory -PathType Container) {
        $files = @(
            Get-ChildItem -LiteralPath $scriptDirectory -Recurse -File -Filter "*.ps1" `
                -ErrorAction SilentlyContinue | Sort-Object FullName
        )
    }

    $siteCount = 0
    $unguardedCount = 0
    $unguardedFiles = New-Object System.Collections.ArrayList

    foreach ($file in $files) {
        try {
            $lines = [IO.File]::ReadAllLines($file.FullName)
        }
        catch {
            continue
        }

        $fileUnguarded = 0
        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -notmatch $cliHomeAssignmentPattern) {
                continue
            }
            $siteCount++
            $windowStart = [Math]::Max(0, $i - $GuardWindowLines)
            $windowEnd = [Math]::Min($lines.Length - 1, $i + $GuardWindowLines)
            if (($lines[$windowStart..$windowEnd] -join "`n") -notmatch $guardPattern) {
                $unguardedCount++
                $fileUnguarded++
            }
        }

        if ($fileUnguarded -gt 0) {
            # 只记录文件名，不记录绝对路径或用户名。
            $null = $unguardedFiles.Add([pscustomobject]@{
                File = $file.Name
                UnguardedSiteCount = $fileUnguarded
            })
        }
    }

    return [pscustomobject]@{
        WorktreeName = Split-Path -Leaf $resolvedRoot
        PowerShellFileCount = @($files).Count
        CliHomeAssignmentSiteCount = $siteCount
        UnguardedSiteCount = $unguardedCount
        UnguardedFileCount = @($unguardedFiles).Count
        UnguardedFiles = @($unguardedFiles.ToArray())
    }
}

function Assert-CodexBuildSafetyStaticGate {
    param(
        [Parameter(Mandatory = $true)][string] $RepoRoot,
        [int] $GuardWindowLines = 25
    )

    $result = Invoke-CodexBuildSafetyStaticGate -RepoRoot $RepoRoot -GuardWindowLines $GuardWindowLines
    if ($result.ViolationCount -ne 0) {
        $summary = ($result.Violations | ForEach-Object {
            if ($_.Line -gt 0) { "$($_.Rule) $($_.File):$($_.Line)" } else { "$($_.Rule) $($_.File)" }
        }) -join "; "
        throw "构建安全静态门禁失败（$($result.ViolationCount) 项）：$summary"
    }
    return $result
}
