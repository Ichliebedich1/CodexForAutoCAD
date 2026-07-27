[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    # AgentHost 直接启动的 codex.exe 绝对路径；留空时由 Phase 2 自行探测。
    [string] $CodexExecutable,

    # R20.1 门禁需要的 AutoCAD 2016 安装目录（只读取托管 API 程序集，不启动 AutoCAD）。
    [string] $AutoCad2016Dir,

    # 必须显式指向非系统盘的短产物基目录；也可由进程/用户级
    # CODEX_AUTOCAD_ARTIFACT_BASE 提供。入口不会写入任何持久环境变量。
    [string] $ArtifactBase,

    [string] $EvidenceDirectory,

    # R20.1 门禁在构建前后比对 acad 进程集合，构建期间集合变化就失败——这是它证明
    # 「构建没碰过运行中的 AutoCAD」的方式。因此本套件默认在 AutoCAD 运行时直接拒绝
    # 启动，而不是先花十几分钟构建再必然失败。
    [switch] $IgnoreRunningAutoCad,

    [switch] $SelfTestOnly
)

# 本文件必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。
#
# M9.3：把必过门禁汇总成一个可复现的入口。
#
# 在此之前，跑全套门禁靠的是会话临时目录里的一次性驱动脚本——它不在仓库里，别人复现不了，
# 会话结束就消失，而且每次重写都可能漏掉某一项。本脚本把这件事变成仓库内容。
#
# 总数一律动态汇总，不写死任何规格数字：硬编码的数字在规格增加后只会变成一个需要有人
# 记得更新的谎言。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")

function Get-CodexGateDefinitions {
    <#
    .SYNOPSIS
        必过门禁的顺序定义。Readiness 必须排在最后——它汇总前面各项的 evidence。
    #>
    return @(
        [pscustomobject]@{
            Name = "build-safety-powershell7"
            Script = "scripts\verify-build-safety.ps1"
            Shell = "Core"
            EvidenceFile = "build-safety-ps7.json"
            AcceptsConfiguration = $false
            AcceptsCodexExecutable = $false
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "build-safety-windowspowershell51"
            Script = "scripts\verify-build-safety.ps1"
            Shell = "Desktop"
            EvidenceFile = "build-safety-ps51.json"
            AcceptsConfiguration = $false
            AcceptsCodexExecutable = $false
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "phase2-powershell7"
            Script = "scripts\verify-phase2.ps1"
            Shell = "Core"
            EvidenceFile = "phase2-ps7.json"
            AcceptsConfiguration = $true
            AcceptsCodexExecutable = $true
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "phase2-windowspowershell51"
            Script = "scripts\verify-phase2.ps1"
            Shell = "Desktop"
            EvidenceFile = "phase2-ps51.json"
            AcceptsConfiguration = $true
            AcceptsCodexExecutable = $true
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "agent-bootstrap"
            Script = "scripts\verify-autocad2016-agent-bootstrap.ps1"
            Shell = "Core"
            EvidenceFile = ""
            AcceptsConfiguration = $true
            AcceptsCodexExecutable = $false
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "auth-compat"
            Script = "scripts\verify-autocad2016-auth-compat.ps1"
            Shell = "Core"
            EvidenceFile = ""
            AcceptsConfiguration = $true
            AcceptsCodexExecutable = $false
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "r201-host-build"
            Script = "scripts\verify-m4-r201-host-build.ps1"
            Shell = "Core"
            EvidenceFile = "r201-host-build.json"
            AcceptsConfiguration = $true
            AcceptsCodexExecutable = $false
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "m9-sbom-licenses"
            Script = "scripts\verify-m9-sbom-and-licenses.ps1"
            Shell = "Core"
            EvidenceFile = "m9-sbom.json"
            AcceptsConfiguration = $false
            AcceptsCodexExecutable = $false
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "m4-readiness"
            Script = "scripts\verify-m4-automated-readiness.ps1"
            Shell = "Core"
            EvidenceFile = "m4-readiness.json"
            AcceptsConfiguration = $false
            AcceptsCodexExecutable = $false
            IsAggregator = $true
        }
    )
}

function Get-CodexRelevantProcessSnapshot {
    $keys = New-Object System.Collections.ArrayList
    foreach ($process in @(
            Get-Process -ErrorAction SilentlyContinue | Where-Object {
                $_.ProcessName -ieq "Codex.AutoCAD.AgentHost" -or
                $_.ProcessName -ieq "Codex.AutoCAD.AgentLauncher.FakeAgentHost" -or
                $_.ProcessName -like "CodexLauncherFake-*" -or
                $_.ProcessName -ieq "Codex.AutoCAD.Bridge.Client.TestServer"
            })) {
        $null = $keys.Add(("{0}:{1}" -f $process.ProcessName.ToLowerInvariant(), $process.Id))
    }

    # 当前 Codex 桌面/CLI 自己也可能持有 app-server；因此只记录匿名进程键并在套件结束时
    # 与启动前基线做差，绝不能把外部既有 Codex 会话误判为本次残留。
    foreach ($process in @(
            Get-CimInstance Win32_Process -Filter "Name='codex.exe'" -ErrorAction Stop |
                Where-Object {
                    [string] $_.CommandLine -match '(?i)(?:^|\s)app-server(?:\s|$)'
                })) {
        $null = $keys.Add(("codex-app-server:{0}" -f $process.ProcessId))
    }
    return @($keys | Sort-Object -Unique)
}

function Get-CodexIntroducedResidualProcessCount {
    param(
        [Parameter(Mandatory = $true)] $Before,
        [Parameter(Mandatory = $true)] $After
    )

    $beforeSet = @{}
    foreach ($key in @($Before)) {
        $beforeSet[[string] $key] = $true
    }
    return @(
        @($After) | Where-Object { -not $beforeSet.ContainsKey([string] $_) }
    ).Count
}

function Assert-CodexGateArtifactBaseSafe {
    param([AllowNull()][AllowEmptyString()][string] $ArtifactBase)

    if ([string]::IsNullOrWhiteSpace($ArtifactBase)) {
        throw ("未配置产物基目录。请用 -ArtifactBase 指定短的非系统盘绝对路径，" +
            "或仅在当前进程设置 CODEX_AUTOCAD_ARTIFACT_BASE。")
    }
    if ($ArtifactBase -match '^\\\\') {
        throw "产物基目录不接受 UNC 或设备命名空间路径。"
    }
    if (-not [IO.Path]::IsPathRooted($ArtifactBase)) {
        throw "产物基目录必须是绝对路径。"
    }

    $resolved = Get-CodexTrimmedPath ([IO.Path]::GetFullPath($ArtifactBase))
    $volumeRoot = Get-CodexTrimmedPath ([IO.Path]::GetPathRoot($resolved))
    $systemRoot = Get-CodexTrimmedPath (
        [IO.Path]::GetPathRoot([Environment]::GetFolderPath("Windows")))
    if ($resolved -ieq $volumeRoot) {
        throw "产物基目录不能是卷根目录。"
    }
    if ($volumeRoot -ieq $systemRoot) {
        throw "产物基目录不能位于 Windows 系统卷；全量门禁不得把构建产物写回 C 盘。"
    }
    return $resolved
}

function Assert-CodexPathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Root
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = Get-CodexTrimmedPath ([IO.Path]::GetFullPath($Root))
    $rootPrefix = $resolvedRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "EvidenceDirectory 必须位于本次 Worktree 的受控产物根内。"
    }
    return $resolvedPath
}

function Select-CodexCorrelatedEvidenceCandidate {
    param(
        [Parameter(Mandatory = $true)] $Candidates,
        [Parameter(Mandatory = $true)][string] $RunCorrelationId
    )

    $matches = @(
        @($Candidates) | Where-Object {
            [string] $_.RunCorrelationId -ceq $RunCorrelationId
        }
    )
    if ($matches.Count -eq 0) {
        throw "没有找到本次 gate run 的精确 evidence。"
    }
    if ($matches.Count -ne 1) {
        throw "本次 gate run 出现重复 evidence，无法确定唯一输入。"
    }
    return $matches[0]
}

function Find-CodexCorrelatedStageEvidence {
    <#
    .SYNOPSIS
        某些门禁只写到自己的 stage 目录，不接受 -EvidencePath。只接受本次运行的唯一证据。
    .DESCRIPTION
        不按 LastWriteTime 选择，也不回退旧成功文件。目录内每份 verification.json
        必须先解析出与本次 CODEX_GATE_RUN_ID 完全一致的标识，且匹配数必须恰好为一。
    #>
    param(
        [Parameter(Mandatory = $true)][string] $ArtifactRoot,
        [Parameter(Mandatory = $true)][string] $Pattern,
        [Parameter(Mandatory = $true)][string] $RunCorrelationId
    )
    if (-not (Test-Path -LiteralPath $ArtifactRoot -PathType Container)) {
        throw "产物根不存在，无法查找本次 gate run evidence。"
    }
    $directories = @(Get-ChildItem -LiteralPath $ArtifactRoot -Directory -Force -Filter $Pattern `
            -ErrorAction SilentlyContinue)
    $candidates = New-Object System.Collections.ArrayList
    foreach ($directory in $directories) {
        $candidate = Join-Path $directory.FullName "verification.json"
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }
        try {
            $json = Get-Content -LiteralPath $candidate -Raw | ConvertFrom-Json
        }
        catch {
            continue
        }
        if ($json.PSObject.Properties.Name -notcontains "RunCorrelationId") {
            continue
        }
        $null = $candidates.Add([pscustomobject]@{
            Path = $candidate
            RunCorrelationId = [string] $json.RunCorrelationId
        })
    }
    return (Select-CodexCorrelatedEvidenceCandidate `
        -Candidates $candidates `
        -RunCorrelationId $RunCorrelationId).Path
}

function Get-CodexGateEvidenceBinding {
    param(
        [Parameter(Mandatory = $true)][string] $EvidencePath,
        [Parameter(Mandatory = $true)][string] $RunCorrelationId
    )

    if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
        throw "门禁成功但没有生成预期 evidence。"
    }
    try {
        $json = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json
    }
    catch {
        throw "门禁 evidence 不是有效 JSON。"
    }

    $actualRunId = $null
    if ($json.PSObject.Properties.Name -contains "RunCorrelationId") {
        $actualRunId = [string] $json.RunCorrelationId
    }
    elseif (($json.PSObject.Properties.Name -contains "RunCorrelation") -and
        $null -ne $json.RunCorrelation -and
        ($json.RunCorrelation.PSObject.Properties.Name -contains "Id")) {
        $actualRunId = [string] $json.RunCorrelation.Id
    }
    if ($actualRunId -cne $RunCorrelationId) {
        throw "门禁 evidence 没有绑定本次 RunCorrelationId。"
    }

    return [pscustomobject]@{
        Sha256 = (Get-FileHash -LiteralPath $EvidencePath -Algorithm SHA256).Hash.ToUpperInvariant()
        RunCorrelationId = $actualRunId
    }
}

function Get-CodexGateSuiteSummary {
    param([Parameter(Mandatory = $true)] $Results)

    $all = @($Results)
    $failed = @($all | Where-Object { $_.ExitCode -ne 0 })
    $pathChanged = @($all | Where-Object { -not $_.UserPathUnchanged })
    return [pscustomobject]@{
        Total = $all.Count
        Failed = $failed.Count
        Passed = ($all.Count - $failed.Count)
        FailedNames = @($failed | ForEach-Object { $_.Name })
        UserPathChangedCount = $pathChanged.Count
        Success = ($failed.Count -eq 0 -and $pathChanged.Count -eq 0)
    }
}

function Write-CodexSafeGateOutput {
    param([AllowNull()] $Value)

    if ($null -eq $Value) {
        return
    }
    $line = [string] $Value
    if ($line -match '^[A-Z][A-Z0-9_.-]*=(?:passed|failed|true|false|\d+)$' -or
        $line -match '^\s*\d+\s*/\s*\d+\s+specs passed\s*$') {
        Write-Output $line
    }
}

if ($SelfTestOnly) {
    $definitions = @(Get-CodexGateDefinitions)
    if ($definitions.Count -lt 2) {
        throw "自检失败：门禁定义少于两项。"
    }
    foreach ($requiredGateName in @(
            "build-safety-powershell7",
            "build-safety-windowspowershell51")) {
        if (@($definitions | Where-Object { $_.Name -ceq $requiredGateName }).Count -ne 1) {
            throw "自检失败：缺少双 Shell 构建安全门禁 $requiredGateName。"
        }
    }
    if (@($definitions | Where-Object { $_.IsAggregator }).Count -ne 1) {
        throw "自检失败：汇总器门禁必须且只能有一个。"
    }
    if (-not $definitions[$definitions.Count - 1].IsAggregator) {
        throw "自检失败：汇总器必须是最后一项，否则它读不到本次上游 evidence。"
    }
    foreach ($definition in $definitions) {
        $scriptPath = Join-Path $repoRoot $definition.Script
        if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
            throw "自检失败：门禁脚本不存在：$($definition.Script)"
        }
    }
    $duplicateNames = @($definitions | Group-Object Name | Where-Object { $_.Count -gt 1 })
    if ($duplicateNames.Count -ne 0) {
        throw "自检失败：门禁名称重复。"
    }

    $allPassed = Get-CodexGateSuiteSummary @(
        [pscustomobject]@{ Name = "a"; ExitCode = 0; UserPathUnchanged = $true },
        [pscustomobject]@{ Name = "b"; ExitCode = 0; UserPathUnchanged = $true })
    if (-not $allPassed.Success -or $allPassed.Passed -ne 2) {
        throw "自检失败：全部通过时汇总为失败。"
    }
    $oneFailed = Get-CodexGateSuiteSummary @(
        [pscustomobject]@{ Name = "a"; ExitCode = 0; UserPathUnchanged = $true },
        [pscustomobject]@{ Name = "b"; ExitCode = 1; UserPathUnchanged = $true })
    if ($oneFailed.Success -or $oneFailed.Failed -ne 1 -or $oneFailed.FailedNames[0] -cne "b") {
        throw "自检失败：单项失败没有让整套失败。"
    }
    # PATH 被改动即使门禁全绿也必须整套失败——那正是本项目两次事故的形态。
    $pathMoved = Get-CodexGateSuiteSummary @(
        [pscustomobject]@{ Name = "a"; ExitCode = 0; UserPathUnchanged = $false })
    if ($pathMoved.Success) {
        throw "自检失败：用户 PATH 变化没有让整套失败。"
    }

    $systemRoot = [IO.Path]::GetPathRoot([Environment]::GetFolderPath("Windows"))
    $alternateRoot = if ($systemRoot -ieq "Z:\") { "Y:\" } else { "Z:\" }
    $safeBase = Assert-CodexGateArtifactBaseSafe -ArtifactBase ($alternateRoot + "cfa")
    if ($safeBase -cne ($alternateRoot + "cfa")) {
        throw "自检失败：短的非系统盘产物根没有被接受。"
    }
    $systemBaseRejected = $false
    try {
        Assert-CodexGateArtifactBaseSafe -ArtifactBase (
            [IO.Path]::Combine($systemRoot, "cfa")) | Out-Null
    }
    catch {
        $systemBaseRejected = $true
    }
    if (-not $systemBaseRejected) {
        throw "自检失败：系统盘产物根没有被拒绝。"
    }
    $missingBaseRejected = $false
    try {
        Assert-CodexGateArtifactBaseSafe -ArtifactBase $null | Out-Null
    }
    catch {
        $missingBaseRejected = $true
    }
    if (-not $missingBaseRejected) {
        throw "自检失败：缺失产物根没有被拒绝。"
    }

    $selectedEvidence = Select-CodexCorrelatedEvidenceCandidate -Candidates @(
        [pscustomobject]@{
            Path = "newer-but-stale.json"
            RunCorrelationId = "run-stale"
        },
        [pscustomobject]@{
            Path = "older-but-current.json"
            RunCorrelationId = "run-current"
        }) -RunCorrelationId "run-current"
    if ($selectedEvidence.Path -cne "older-but-current.json") {
        throw "自检失败：没有按本次 RunCorrelationId 选择精确 evidence。"
    }

    $phase2Definitions = @($definitions | Where-Object { $_.Script -ceq "scripts\verify-phase2.ps1" })
    if ($phase2Definitions.Count -ne 2 -or
        @($phase2Definitions | Where-Object { -not $_.AcceptsCodexExecutable }).Count -ne 0) {
        throw "自检失败：Phase 2 没有独占 CodexExecutable 参数。"
    }
    $invalidCodexTargets = @(
        $definitions | Where-Object {
            $_.Script -in @(
                "scripts\verify-autocad2016-agent-bootstrap.ps1",
                "scripts\verify-autocad2016-auth-compat.ps1") -and
            $_.AcceptsCodexExecutable
        })
    if ($invalidCodexTargets.Count -ne 0) {
        throw "自检失败：bootstrap/auth 不得接收 CodexExecutable 参数。"
    }

    Write-Host "ALL_GATES_SELF_TEST=passed"
    return
}

$runningAutoCad = @(Get-Process -Name acad -ErrorAction SilentlyContinue).Count
if ($runningAutoCad -ne 0 -and -not $IgnoreRunningAutoCad) {
    throw ("检测到 $runningAutoCad 个 AutoCAD 进程。R20.1 门禁会比对构建前后的 acad 进程" +
        "集合，构建期间启动或退出 AutoCAD 必然使该门禁失败。请先关闭 AutoCAD，" +
        "或显式传入 -IgnoreRunningAutoCad 接受这一后果。")
}

if ([string]::IsNullOrWhiteSpace($AutoCad2016Dir)) {
    throw "全量门禁要求显式传入 -AutoCad2016Dir，必须在长时间构建开始前完成 R20.1 预检。"
}
$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)
if (-not (Test-Path -LiteralPath $AutoCad2016Dir -PathType Container)) {
    throw "AutoCAD 2016 R20.1 托管程序集目录不存在。"
}
foreach ($apiAssemblyName in @("accoremgd.dll", "acdbmgd.dll", "acmgd.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $AutoCad2016Dir $apiAssemblyName) -PathType Leaf)) {
        throw "AutoCAD 2016 R20.1 托管程序集目录不完整。"
    }
}

$effectiveArtifactBase = $ArtifactBase
if ([string]::IsNullOrWhiteSpace($effectiveArtifactBase)) {
    $effectiveArtifactBase = [Environment]::GetEnvironmentVariable(
        "CODEX_AUTOCAD_ARTIFACT_BASE",
        "Process")
}
if ([string]::IsNullOrWhiteSpace($effectiveArtifactBase)) {
    $effectiveArtifactBase = [Environment]::GetEnvironmentVariable(
        "CODEX_AUTOCAD_ARTIFACT_BASE",
        "User")
}
$effectiveArtifactBase = Assert-CodexGateArtifactBaseSafe -ArtifactBase $effectiveArtifactBase
$previousArtifactBase = $env:CODEX_AUTOCAD_ARTIFACT_BASE
$previousRunId = $env:CODEX_GATE_RUN_ID
$previousAddGlobalToolsToPath = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
$buildSafety = $null
$buildSafetyCompleted = $false

try {
    $env:CODEX_AUTOCAD_ARTIFACT_BASE = $effectiveArtifactBase

    $buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
    $artifactRoot = $buildSafety.ArtifactRoot
    if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
        $EvidenceDirectory = Join-Path $artifactRoot "gate-evidence"
    }
    elseif (-not [IO.Path]::IsPathRooted($EvidenceDirectory)) {
        throw "EvidenceDirectory 必须是产物根内的绝对路径。"
    }
    $EvidenceDirectory = Assert-CodexPathWithinRoot `
        -Path $EvidenceDirectory `
        -Root $artifactRoot
    New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null

    # 一次性运行关联标识：各门禁写进自己的 evidence，汇总器要求全部输入携带同一个。
    # 没有它，失败的门禁不写 evidence，汇总器可能读到上一次成功遗留的旧文件并报绿。
    $env:CODEX_GATE_RUN_ID = "run-" + [Guid]::NewGuid().ToString("N")
    $runCorrelationId = $env:CODEX_GATE_RUN_ID

$pwshPath = (Get-Command pwsh -ErrorAction SilentlyContinue)
if ($null -eq $pwshPath) {
    throw "找不到 pwsh。不要假设 PowerShell 7 的安装路径，必须由 Get-Command 解析。"
}
$coreShell = $pwshPath.Source
$desktopShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"

$baselinePath = Get-CodexUserPathState
$baselineProcessSnapshot = @(Get-CodexRelevantProcessSnapshot)
$definitions = @(Get-CodexGateDefinitions)
$results = New-Object System.Collections.ArrayList
$evidencePaths = @{}

try {
    foreach ($definition in $definitions) {
        $gateArguments = New-Object System.Collections.ArrayList

        if ($definition.IsAggregator) {
            $failedSoFar = @($results | Where-Object { $_.ExitCode -ne 0 })
            if ($failedSoFar.Count -ne 0) {
                Write-Host ("`n==> 跳过 $($definition.Name)：已有 $($failedSoFar.Count) 项上游门禁失败") `
                    -ForegroundColor Yellow
                $null = $results.Add([pscustomobject]@{
                    Name = $definition.Name
                    ExitCode = 1
                    UserPathUnchanged = $true
                    Skipped = $true
                    EvidenceBoundToRun = $false
                    EvidenceSha256 = $null
                })
                continue
            }
            $null = $gateArguments.AddRange(@(
                "-Phase2PowerShell7EvidencePath", $evidencePaths["phase2-powershell7"],
                "-Phase2WindowsPowerShell51EvidencePath",
                $evidencePaths["phase2-windowspowershell51"],
                "-AgentBootstrapEvidencePath", $evidencePaths["agent-bootstrap"],
                "-AuthCompatEvidencePath", $evidencePaths["auth-compat"],
                "-R201HostEvidencePath", $evidencePaths["r201-host-build"],
                "-RequireRunCorrelation"))
        }
        else {
            if (-not [string]::IsNullOrWhiteSpace($AutoCad2016Dir)) {
                if ($definition.Name -eq "r201-host-build") {
                    $null = $gateArguments.AddRange(@("-AutoCad2016Dir", $AutoCad2016Dir))
                }
            }
            if ($definition.AcceptsConfiguration) {
                $null = $gateArguments.AddRange(@("-Configuration", $Configuration))
            }
            if ($definition.AcceptsCodexExecutable -and
                -not [string]::IsNullOrWhiteSpace($CodexExecutable)) {
                $null = $gateArguments.AddRange(@("-CodexExecutable", $CodexExecutable))
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($definition.EvidenceFile)) {
            $null = $gateArguments.AddRange(@(
                "-EvidencePath", (Join-Path $EvidenceDirectory $definition.EvidenceFile)))
        }

        $shell = if ($definition.Shell -eq "Desktop") { $desktopShell } else { $coreShell }
        $shellArguments = if ($definition.Shell -eq "Desktop") {
            @("-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File")
        }
        else {
            @("-NoProfile", "-NonInteractive", "-File")
        }

        Write-Host "`n================ GATE: $($definition.Name) ================" -ForegroundColor Cyan
        & $shell @shellArguments (Join-Path $repoRoot $definition.Script) @gateArguments 2>&1 |
            ForEach-Object { Write-CodexSafeGateOutput $_ }
        $exitCode = $LASTEXITCODE

        $currentPath = Get-CodexUserPathState
        $pathUnchanged = ($currentPath.Sha256 -ceq $baselinePath.Sha256)
        $evidenceBoundToRun = $false
        $evidenceSha256 = $null
        if ($exitCode -eq 0) {
            try {
                $gateEvidencePath = if (-not [string]::IsNullOrWhiteSpace($definition.EvidenceFile)) {
                    Join-Path $EvidenceDirectory $definition.EvidenceFile
                }
                elseif ($definition.Name -eq "agent-bootstrap") {
                    Find-CodexCorrelatedStageEvidence -ArtifactRoot $artifactRoot `
                        -Pattern "autocad2016-agent-bootstrap-*" `
                        -RunCorrelationId $runCorrelationId
                }
                elseif ($definition.Name -eq "auth-compat") {
                    Find-CodexCorrelatedStageEvidence -ArtifactRoot $artifactRoot `
                        -Pattern "autocad2016-auth-compat-*" `
                        -RunCorrelationId $runCorrelationId
                }
                else {
                    throw "门禁定义没有声明 evidence 输出。"
                }
                $binding = Get-CodexGateEvidenceBinding `
                    -EvidencePath $gateEvidencePath `
                    -RunCorrelationId $runCorrelationId
                $evidencePaths[$definition.Name] = $gateEvidencePath
                $evidenceBoundToRun = $true
                $evidenceSha256 = $binding.Sha256
            }
            catch {
                $exitCode = 1
                Write-Host "---- evidence binding failed" -ForegroundColor Yellow
            }
        }
        Write-Host ("---- $($definition.Name): exit=$exitCode pathUnchanged=$pathUnchanged")
        $null = $results.Add([pscustomobject]@{
            Name = $definition.Name
            ExitCode = $exitCode
            UserPathUnchanged = $pathUnchanged
            Skipped = $false
            EvidenceBoundToRun = $evidenceBoundToRun
            EvidenceSha256 = $evidenceSha256
        })

        if ($exitCode -ne 0 -or -not $pathUnchanged) {
            Write-Host "---- fail-fast：停止后续长门禁" -ForegroundColor Yellow
            break
        }
    }

    $completedNames = @($results | ForEach-Object { $_.Name })
    foreach ($definition in $definitions) {
        if ($completedNames -contains $definition.Name) {
            continue
        }
        $null = $results.Add([pscustomobject]@{
            Name = $definition.Name
            ExitCode = 1
            UserPathUnchanged = $true
            Skipped = $true
            EvidenceBoundToRun = $false
            EvidenceSha256 = $null
        })
    }
}
finally {
    $env:CODEX_GATE_RUN_ID = $previousRunId
}

$summary = Get-CodexGateSuiteSummary $results
$finalProcessSnapshot = @(Get-CodexRelevantProcessSnapshot)
$residual = Get-CodexIntroducedResidualProcessCount `
    -Before $baselineProcessSnapshot `
    -After $finalProcessSnapshot
$finalPath = Get-CodexUserPathState

$suiteEvidence = [ordered]@{
    SchemaVersion = 2
    RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
    RunCorrelationId = $runCorrelationId
    Scope = "codex-autocad-implemented-automated-gate-suite"
    Configuration = $Configuration
    GateDefinitionTotal = $definitions.Count
    GateTotal = $summary.Total
    GatePassed = $summary.Passed
    GateFailed = $summary.Failed
    FailedGates = @($summary.FailedNames)
    Gates = @($results | ForEach-Object {
        [ordered]@{
            Name = $_.Name
            ExitCode = $_.ExitCode
            UserPathUnchanged = $_.UserPathUnchanged
            Skipped = $_.Skipped
            EvidenceBoundToRun = $_.EvidenceBoundToRun
            EvidenceSha256 = $_.EvidenceSha256
        }
    })
    UserPathLength = $finalPath.Length
    UserPathSha256 = $finalPath.Sha256
    UserPathUnchanged = ($finalPath.Sha256 -ceq $baselinePath.Sha256)
    BaselineRelevantProcessCount = $baselineProcessSnapshot.Count
    FinalRelevantProcessCount = $finalProcessSnapshot.Count
    IntroducedResidualProcessCount = $residual
    AutoCadStartedOrCommanded = $false
    EvidenceBoundary = "This evidence records the currently implemented automated gates that ran in one correlated suite invocation, their exact evidence hashes, exit codes, a hashed user-PATH fingerprint and only processes introduced by this run. Gate totals are counted at run time and never hardcoded. M9.8 vulnerability-database and manual/IL review, candidate manifest/doctor, CI/clean-cache proof, every real-machine and enterprise matrix, candidate freeze, CAD writes and saves remain outside this suite."
}

$suiteEvidencePath = Join-Path $EvidenceDirectory "all-gates.json"
$encoding = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($suiteEvidencePath, ($suiteEvidence | ConvertTo-Json -Depth 8), $encoding)

Complete-CodexBuildSafety -State $buildSafety -Stage "all-gates" | Out-Null
$buildSafetyCompleted = $true

Write-Host "`n================ SUMMARY ================"
foreach ($result in $results) {
    $state = if ($result.Skipped) { "skipped" } elseif ($result.ExitCode -eq 0) { "pass" } else { "FAIL" }
    Write-Host ("{0,-30} {1,-8} exit={2,-4} pathUnchanged={3}" -f `
        $result.Name, $state, $result.ExitCode, $result.UserPathUnchanged)
}
Write-Host ("ALL_GATES_TOTAL=" + $summary.Total)
Write-Host ("ALL_GATES_PASSED=" + $summary.Passed)
Write-Host ("ALL_GATES_FAILED=" + $summary.Failed)
Write-Host ("ALL_GATES_RESIDUAL_PROCESSES=" + $residual)
Write-Host ("ALL_GATES_USER_PATH_UNCHANGED=" + $suiteEvidence.UserPathUnchanged)
Write-Host "ALL_GATES_EVIDENCE=gate-evidence/all-gates.json"

if (-not $summary.Success -or $residual -ne 0) {
    Write-Host "`n必过门禁套件未通过。" -ForegroundColor Yellow
    Write-Host "ALL_GATES=failed"
    exit 1
}

Write-Host "`n当前已实现的自动化门禁全部通过；M9.8 剩余项、候选及真实机器/企业矩阵仍未验证。" `
    -ForegroundColor Green
Write-Host "ALL_GATES=passed"
}
finally {
    try {
        if ($null -ne $buildSafety -and -not $buildSafetyCompleted) {
            Complete-CodexBuildSafety -State $buildSafety -Stage "all-gates-aborted" | Out-Null
        }
    }
    finally {
        $env:CODEX_GATE_RUN_ID = $previousRunId
        $env:CODEX_AUTOCAD_ARTIFACT_BASE = $previousArtifactBase
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $previousAddGlobalToolsToPath
    }
}
