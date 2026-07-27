# CodexForAutoCAD 构建安全门禁自检。
#
# 必须同时在 PowerShell 7 与 Windows PowerShell 5.1 下通过，且不得调用 dotnet。
# 本文件必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。

[CmdletBinding()]
param(
    [switch] $SelfTestOnly,
    [string] $EvidencePath,
    [string] $ArtifactBase,
    [ValidateRange(0, 40)]
    [double] $MinimumFreeGiB = 40
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$helperPath = Join-Path $PSScriptRoot "build-safety.ps1"
. $helperPath

function Assert-True {
    param([Parameter(Mandatory = $true)][bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory = $true)][scriptblock] $Action,
        [Parameter(Mandatory = $true)][string] $Message
    )
    $rejected = $false
    try {
        & $Action | Out-Null
    }
    catch {
        $rejected = $true
    }
    Assert-True $rejected $Message
}

$previousArtifactBase = $env:CODEX_AUTOCAD_ARTIFACT_BASE
$previousAddGlobalTools = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH

if (-not [string]::IsNullOrWhiteSpace($ArtifactBase)) {
    $env:CODEX_AUTOCAD_ARTIFACT_BASE = $ArtifactBase
}

$effectiveArtifactBase = $env:CODEX_AUTOCAD_ARTIFACT_BASE
if ([string]::IsNullOrWhiteSpace($effectiveArtifactBase)) {
    $effectiveArtifactBase = [Environment]::GetEnvironmentVariable(
        "CODEX_AUTOCAD_ARTIFACT_BASE", "User")
}
if ([string]::IsNullOrWhiteSpace($effectiveArtifactBase)) {
    throw "自检失败：未配置 CODEX_AUTOCAD_ARTIFACT_BASE，无法保证测试产物离开系统盘。"
}

$pathBefore = Get-CodexUserPathState
Assert-True ($pathBefore.PollutingEntryCount -eq 0) `
    "自检失败：当前用户 PATH 已存在项目临时工具污染。"

# 本地默认仍为 40 GiB。标准 GitHub Runner 由工作流显式传入受控的 5 GiB 下限；
# 不允许通过环境变量静默降低门槛。
$productionArtifactRoot = Resolve-CodexArtifactRoot -RepoRoot $repoRoot `
    -MinimumFreeGiB $MinimumFreeGiB
$artifactVolume = [IO.Path]::GetPathRoot($productionArtifactRoot)
$repoVolume = [IO.Path]::GetPathRoot($repoRoot)
$systemVolume = [IO.Path]::GetPathRoot([Environment]::GetFolderPath("Windows"))

# 自检自身产生的所有文件都放在产物卷上，绝不落到系统盘临时目录。
$selfTestBase = Join-Path (Join-Path $productionArtifactRoot "build-safety-selftest") `
    ([Guid]::NewGuid().ToString("N"))

$createdArtifactPaths = @()
try {
    $env:CODEX_AUTOCAD_ARTIFACT_BASE = $selfTestBase
    # 自检目录包含 GUID，路径天然超过生产 60 字符上限；自检不执行 net45 隔离构建，
    # 因此在此显式放宽，生产默认值仍由下方负向用例守住。
    $state = Initialize-CodexBuildSafety -RepoRoot $repoRoot -MinimumFreeGiB 0 `
        -MaximumArtifactRootLength 512
    $createdArtifactPaths += $state.ArtifactRoot

    Assert-True ($state.ArtifactRoot.StartsWith(
            [IO.Path]::GetFullPath($selfTestBase),
            [StringComparison]::OrdinalIgnoreCase)) `
        "自检失败：产物目录没有隔离到配置的基目录。"
    # 按 Worktree 名称隔离，避免并行任务互相覆盖。
    Assert-True ((Split-Path -Leaf $state.ArtifactRoot) -ceq (Split-Path -Leaf $repoRoot)) `
        "自检失败：产物目录没有按 Worktree 名称隔离。"
    Assert-True ($env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH -ceq "0") `
        "自检失败：未禁用 .NET 全局工具 PATH 自动写入。"
    Complete-CodexBuildSafety -State $state -Stage "self-test" | Out-Null

    # fail-closed：空间不足必须在写入前拒绝。
    Assert-Rejected {
        Resolve-CodexArtifactRoot -RepoRoot $repoRoot `
            -MinimumFreeGiB ([double]::MaxValue) -NoCreate
    } "自检失败：空间不足没有被 fail-closed 拒绝。"

    # 非法产物基目录必须拒绝。
    $env:CODEX_AUTOCAD_ARTIFACT_BASE = "relative\path"
    Assert-Rejected { Resolve-CodexArtifactRoot -RepoRoot $repoRoot -MinimumFreeGiB 0 -NoCreate } `
        "自检失败：相对路径产物基目录没有被拒绝。"
    $env:CODEX_AUTOCAD_ARTIFACT_BASE = "\\server\share\artifacts"
    Assert-Rejected { Resolve-CodexArtifactRoot -RepoRoot $repoRoot -MinimumFreeGiB 0 -NoCreate } `
        "自检失败：UNC 产物基目录没有被拒绝。"

    # MAX_PATH fail-closed：过长产物根必须在构建前被拒绝。
    # 2026-07-26 产物根迁到 E 盘后长度增加 23 字符，使 net45 隔离构建路径达到 267 字符并以
    # MSB3030 失败；该用例保证同类回归以后在构建开始前就被拦下。
    $env:CODEX_AUTOCAD_ARTIFACT_BASE = "E:\" + ("d" * 200)
    Assert-Rejected { Resolve-CodexArtifactRoot -RepoRoot $repoRoot -MinimumFreeGiB 0 -NoCreate } `
        "自检失败：超长产物根没有被 fail-closed 拒绝。"
    # 生产默认上限必须真实生效，而不是只在显式传参时生效。
    $env:CODEX_AUTOCAD_ARTIFACT_BASE = "E:\" + ("d" * 64)
    Assert-Rejected { Resolve-CodexArtifactRoot -RepoRoot $repoRoot -MinimumFreeGiB 0 -NoCreate } `
        "自检失败：默认产物根长度上限没有生效。"

    $env:CODEX_AUTOCAD_ARTIFACT_BASE = $selfTestBase

    # 门禁运行关联标识：未设置时必须返回 $null（调用方据此退回时间窗），
    # 设置但格式非法时必须失败关闭——静默忽略会让汇总器悄悄失去同一次运行的证明。
    $previousGateRunId = $env:CODEX_GATE_RUN_ID
    try {
        $env:CODEX_GATE_RUN_ID = $null
        if ($null -ne (Get-CodexGateRunCorrelationId)) {
            throw "自检失败：未设置运行关联标识时没有返回 null。"
        }
        $env:CODEX_GATE_RUN_ID = "   "
        if ($null -ne (Get-CodexGateRunCorrelationId)) {
            throw "自检失败：空白运行关联标识没有被视为未设置。"
        }
        $env:CODEX_GATE_RUN_ID = "  run-2026a.7_x-1  "
        if ((Get-CodexGateRunCorrelationId) -cne "run-2026a.7_x-1") {
            throw "自检失败：合法运行关联标识没有被原样接受。"
        }
        $env:CODEX_GATE_RUN_ID = "run id"
        Assert-Rejected { Get-CodexGateRunCorrelationId } `
            "自检失败：含空格的运行关联标识没有被拒绝。"
        $env:CODEX_GATE_RUN_ID = "run/../id"
        Assert-Rejected { Get-CodexGateRunCorrelationId } `
            "自检失败：含路径分隔符的运行关联标识没有被拒绝。"
        $env:CODEX_GATE_RUN_ID = "a" * 65
        Assert-Rejected { Get-CodexGateRunCorrelationId } `
            "自检失败：超长运行关联标识没有被拒绝。"
    }
    finally {
        $env:CODEX_GATE_RUN_ID = $previousGateRunId
    }

    # PATH 守卫必须在指纹变化时拒绝，而不是静默接受。
    $tamperedState = [pscustomobject]@{
        Length = $pathBefore.Length
        EntryCount = $pathBefore.EntryCount
        Sha256 = ("0" * 64)
    }
    Assert-Rejected { Assert-CodexUserPathSafe -ExpectedState $tamperedState -Stage "tamper" } `
        "自检失败：PATH 指纹不一致时没有被拒绝。"

    # 静态门禁：阻止后续新增违规脚本或代码。
    $staticGate = Invoke-CodexBuildSafetyStaticGate -RepoRoot $repoRoot
    if ($staticGate.ViolationCount -ne 0) {
        $summary = ($staticGate.Violations | ForEach-Object {
            if ($_.Line -gt 0) { "$($_.Rule) $($_.File):$($_.Line)" } else { "$($_.Rule) $($_.File)" }
        }) -join "; "
        throw "自检失败：构建安全静态门禁 $($staticGate.ViolationCount) 项未通过：$summary"
    }

    # 自检期间新建的产物必须全部落在产物卷上。
    $selfTestFullPath = [IO.Path]::GetFullPath($selfTestBase)
    Assert-True ([IO.Path]::GetPathRoot($selfTestFullPath) -ieq $artifactVolume) `
        "自检失败：自检产物没有落在配置的产物卷上。"
    $offVolumeArtifacts = @()
    if (Test-Path -LiteralPath $selfTestFullPath -PathType Container) {
        $offVolumeArtifacts = @(
            Get-ChildItem -LiteralPath $selfTestFullPath -Recurse -Force -ErrorAction SilentlyContinue |
                Where-Object { [IO.Path]::GetPathRoot($_.FullName) -ine $artifactVolume }
        )
    }
    Assert-True ($offVolumeArtifacts.Count -eq 0) `
        "自检失败：存在落在产物卷之外的自检产物。"

    $pathAfter = Get-CodexUserPathState
    Assert-True ($pathAfter.Sha256 -ceq $pathBefore.Sha256) `
        "自检失败：用户 PATH 指纹发生变化。"
    Assert-True ($pathAfter.Length -eq $pathBefore.Length) `
        "自检失败：用户 PATH 长度发生变化。"
    Assert-True ($pathAfter.EntryCount -eq $pathBefore.EntryCount) `
        "自检失败：用户 PATH 项目数发生变化。"
    Assert-True ($pathAfter.PollutingEntryCount -eq 0) `
        "自检失败：自检后出现临时工具污染项。"

    if (-not $SelfTestOnly -and -not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $resolvedEvidencePath = if ([IO.Path]::IsPathRooted($EvidencePath)) {
            [IO.Path]::GetFullPath($EvidencePath)
        }
        else {
            [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidencePath))
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidencePath) `
            -Force | Out-Null
        # 只记录指纹与计数，不记录 PATH 明文、绝对路径或环境变量内容。
        $evidence = [ordered]@{
            SchemaVersion = 2
            RunCorrelationId = Get-CodexGateRunCorrelationId
            Scope = "codex-autocad-build-safety-gate"
            Status = "passed"
            PowerShellEdition = [string] $PSVersionTable.PSEdition
            PowerShellVersion = $PSVersionTable.PSVersion.ToString()
            UserPathLength = $pathAfter.Length
            UserPathEntryCount = $pathAfter.EntryCount
            UserPathSha256 = $pathAfter.Sha256
            UserPathChanged = $false
            PollutingPathEntryCount = 0
            ArtifactVolumeIsSystemVolume = ($artifactVolume -ieq $systemVolume)
            ArtifactVolumeIsRepositoryVolume = ($artifactVolume -ieq $repoVolume)
            ArtifactRootIsolatedByWorktreeName = $true
            StaticGatePowerShellFileCount = $staticGate.ScannedPowerShellFileCount
            StaticGateSourceFileCount = $staticGate.ScannedSourceFileCount
            StaticGateCliHomeAssignmentSiteCount = $staticGate.DotnetCliHomeAssignmentSiteCount
            StaticGateViolationCount = 0
            RawPathPersisted = $false
            RawEnvironmentPersisted = $false
            ArtifactPathPersisted = $false
        }
        $encoding = New-Object Text.UTF8Encoding($false)
        [IO.File]::WriteAllText(
            $resolvedEvidencePath,
            ($evidence | ConvertTo-Json -Depth 6),
            $encoding)
    }

    Write-Host "BUILD_SAFETY_SELF_TEST=passed"
    Write-Host ("BUILD_SAFETY_SHELL=" + $PSVersionTable.PSEdition + " " + $PSVersionTable.PSVersion.ToString())
    Write-Host ("BUILD_SAFETY_ARTIFACT_VOLUME=" + $artifactVolume)
    Write-Host ("BUILD_SAFETY_ARTIFACT_VOLUME_IS_SYSTEM=" + ($artifactVolume -ieq $systemVolume))
    Write-Host ("BUILD_SAFETY_STATIC_GATE_PS_FILES=" + $staticGate.ScannedPowerShellFileCount)
    Write-Host ("BUILD_SAFETY_STATIC_GATE_CS_FILES=" + $staticGate.ScannedSourceFileCount)
    Write-Host ("BUILD_SAFETY_CLI_HOME_SITES=" + $staticGate.DotnetCliHomeAssignmentSiteCount)
    Write-Host ("BUILD_SAFETY_STATIC_GATE_VIOLATIONS=" + $staticGate.ViolationCount)
    Write-Host ("BUILD_SAFETY_USER_PATH_LENGTH=" + $pathAfter.Length)
    Write-Host ("BUILD_SAFETY_USER_PATH_ENTRIES=" + $pathAfter.EntryCount)
    Write-Host ("BUILD_SAFETY_USER_PATH_SHA256=" + $pathAfter.Sha256)
}
finally {
    $env:CODEX_AUTOCAD_ARTIFACT_BASE = $previousArtifactBase
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $previousAddGlobalTools
    if (Test-Path -LiteralPath $selfTestBase -PathType Container) {
        Remove-Item -LiteralPath $selfTestBase -Recurse -Force -ErrorAction SilentlyContinue
    }
    $selfTestParent = Split-Path -Parent $selfTestBase
    if ((Test-Path -LiteralPath $selfTestParent -PathType Container) -and
        (@(Get-ChildItem -LiteralPath $selfTestParent -Force -ErrorAction SilentlyContinue).Count -eq 0)) {
        Remove-Item -LiteralPath $selfTestParent -Force -ErrorAction SilentlyContinue
    }
}
