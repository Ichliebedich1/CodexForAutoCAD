[CmdletBinding()]
param(
    # M4.15.6 汇总器写出的 evidence。冻结必须建立在一次可验证的门禁运行上，
    # 而不是「最近好像都绿过」。
    [string] $ReadinessEvidencePath,

    # 回滚点：必须是已经存在、且解析到 readiness 所绑定候选提交的 Git ref。本脚本只读取和校验，
    # 绝不创建、移动或删除任何 ref——回滚点由谁建立，属于人的决定。
    [string] $RollbackRef,

    # M4.15 实机矩阵的机器可读处置。八项允许 deferred，但必须写明理由并同时约定在
    # M9/M10 重新评估；RealAbnormalExitMatrixVerified 必须为 verified。
    [string] $LiveMatrixResultsPath,

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
# 在 `live-matrix-results.json` 尚未形成合法九项处置、真实异常退出尚未 verified 或回滚点
# 尚未建立时，运行它必定失败，且应当失败。它的价值在于把「为什么还不能冻结」变成
# 可执行、可测试的判定，而不是一段需要每次重新论证的说明。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$safeRepoRoot = $repoRoot.Replace("\", "/")

$script:LiveMatrixIds = @(
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

function Test-ObjectProperty {
    param(
        $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    return ($null -ne $Object -and $Object.PSObject.Properties.Name -contains $Name)
}

function Test-ExactObjectProperties {
    param(
        $Object,
        [Parameter(Mandatory = $true)][string[]] $Allowed
    )
    if ($null -eq $Object) {
        return $false
    }
    $unknown = @($Object.PSObject.Properties.Name | Where-Object {
            $Allowed -cnotcontains [string] $_
        })
    return ($unknown.Count -eq 0)
}

function Test-SafeLiveMatrixReason {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 256) {
        return $false
    }
    $forbidden = @(
        '[\r\n\p{Cc}\u202A-\u202E\u2066-\u2069]',
        '[A-Za-z]:[\\/]',
        '\\\\',
        '(?i)\b[A-Za-z][A-Za-z0-9+.-]*://',
        '(?i)\b[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}\b',
        '(?i)(?:\$env:|%[A-Z_][A-Z0-9_]*%)',
        '(?i)\b(?:token|secret|password|api[_-]?key|authorization|bearer)\b\s*[:=]'
    )
    foreach ($pattern in $forbidden) {
        if ([regex]::IsMatch($Value, $pattern)) {
            return $false
        }
    }
    return $true
}

function Test-JsonBooleanValue {
    param(
        $Value,
        [Parameter(Mandatory = $true)][bool] $Expected
    )
    return ($Value -is [bool] -and [bool] $Value -eq $Expected)
}

function Test-JsonZeroInteger {
    param($Value)
    return (($Value -is [int] -or $Value -is [long]) -and [long] $Value -eq 0)
}

function Convert-M4JsonEvidenceBytes {
    param(
        [Parameter(Mandatory = $true)][byte[]] $Bytes,
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][int] $MaximumBytes
    )

    if ($null -eq $Bytes -or $Bytes.Length -lt 2 -or
        $Bytes.Length -gt $MaximumBytes) {
        throw "$Label 字节长度必须为 2–$MaximumBytes。"
    }

    $utf8 = New-Object Text.UTF8Encoding($false, $true)
    try {
        $jsonText = $utf8.GetString($Bytes)
    }
    catch {
        throw "$Label 不是严格 UTF-8。"
    }
    if ($jsonText.Length -gt 0 -and $jsonText[0] -eq [char] 0xFEFF) {
        $jsonText = $jsonText.Substring(1)
    }

    try {
        $json = $jsonText | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "$Label 不是有效 JSON。"
    }

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace("-", "")
    }
    finally {
        $sha.Dispose()
    }
    return [pscustomobject]@{
        Json = $json
        Sha256 = $hash
    }
}

function Convert-M4LiveMatrixBytes {
    param([Parameter(Mandatory = $true)][byte[]] $Bytes)

    return Convert-M4JsonEvidenceBytes -Bytes $Bytes `
        -Label "live matrix" -MaximumBytes 65536
}

function Read-M4JsonEvidence {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][int] $MaximumBytes
    )
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label 不能是 reparse point。"
    }

    $stream = New-Object IO.FileStream(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    $bytes = $null
    try {
        if ($stream.Length -lt 2 -or $stream.Length -gt $MaximumBytes) {
            throw "$Label 字节长度必须为 2–$MaximumBytes。"
        }
        $bytes = New-Object byte[] ([int] $stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -le 0) {
                throw "$Label 读取未完整结束。"
            }
            $offset += $read
        }
        return Convert-M4JsonEvidenceBytes -Bytes $bytes `
            -Label $Label -MaximumBytes $MaximumBytes
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
        $stream.Dispose()
    }
}

function Read-M4LiveMatrixEvidence {
    param([Parameter(Mandatory = $true)][string] $Path)

    return Read-M4JsonEvidence -Path $Path `
        -Label "live matrix" -MaximumBytes 65536
}

function Read-M4ReadinessEvidence {
    param([Parameter(Mandatory = $true)][string] $Path)

    return Read-M4JsonEvidence -Path $Path `
        -Label "readiness evidence" -MaximumBytes 4194304
}

function Resolve-M4FreezeEvidenceOutputPath {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $RepoRoot,
        [Parameter(Mandatory = $true)][string] $ArtifactRoot
    )

    $resolved = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $RepoRoot $Path))
    }
    $resolvedArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $artifactPrefix = $resolvedArtifactRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith(
            $artifactPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "冻结 evidence 只能写入 build-safety 产物根。"
    }
    if ([IO.Path]::GetExtension($resolved) -cne ".json") {
        throw "冻结 evidence 必须使用 .json 文件。"
    }
    return $resolved
}

function Test-LiveMatrixEvidenceCommitDelta {
    param([string[]] $NameStatusLines)

    $lines = @($NameStatusLines | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string] $_)
        })
    if ($lines.Count -eq 0) {
        # 候选提交与当前提交相同是合法的前置状态；由于 live matrix 尚未提交，它仍会在
        # 后续文件/处置检查处 fail-closed。
        return $true
    }
    if ($lines.Count -ne 1) {
        return $false
    }
    return ([string] $lines[0] -cmatch
        "^[AM]`thandoff/autocad2016/live-matrix-results\.json$")
}

function Resolve-M4LiveMatrixDisposition {
    <#
    .SYNOPSIS
        验证 M4.15 的九项真实机器处置，并返回 verified/deferred 两组。
    .DESCRIPTION
        这是 M4.15 与 M4.16 之间的窄契约。它不推断未填写项，不把自动化结果升级成实机
        结果，也不读取任意路径或系统事件原文。
    #>
    param(
        [Parameter(Mandatory = $true)] $Evidence,
        [Parameter(Mandatory = $true)][string] $ExpectedHeadCommit,
        [Parameter(Mandatory = $true)][string] $ExpectedHostSha256,
        [Parameter(Mandatory = $true)][string] $ExpectedAgentHostSha256
    )

    $errors = New-Object System.Collections.ArrayList
    $verified = New-Object System.Collections.ArrayList
    $deferred = New-Object System.Collections.ArrayList

    if (-not (Test-ExactObjectProperties $Evidence @(
                "SchemaVersion", "Scope", "Candidate", "Items"
            ))) {
        $null = $errors.Add("live matrix 顶层包含未知字段。")
    }
    if (-not (Test-ObjectProperty $Evidence "SchemaVersion") -or
        -not ($Evidence.SchemaVersion -is [int] -or $Evidence.SchemaVersion -is [long]) -or
        [long] $Evidence.SchemaVersion -ne 1) {
        $null = $errors.Add("live matrix SchemaVersion 必须为 1。")
    }
    if (-not (Test-ObjectProperty $Evidence "Scope") -or
        [string] $Evidence.Scope -cne "m4-live-matrix-results") {
        $null = $errors.Add("live matrix Scope 必须为 m4-live-matrix-results。")
    }

    if (-not (Test-ObjectProperty $Evidence "Candidate")) {
        $null = $errors.Add("live matrix 缺少 Candidate。")
    }
    else {
        if (-not (Test-ExactObjectProperties $Evidence.Candidate @(
                    "HeadCommit", "R201HostDllSha256", "AgentHostDllSha256"
                ))) {
            $null = $errors.Add("live matrix Candidate 包含未知字段。")
        }
        foreach ($binding in @(
                [pscustomobject]@{
                    Name = "HeadCommit"
                    Expected = $ExpectedHeadCommit
                    Pattern = "^[0-9a-f]{40}$"
                },
                [pscustomobject]@{
                    Name = "R201HostDllSha256"
                    Expected = $ExpectedHostSha256
                    Pattern = "^[0-9A-F]{64}$"
                },
                [pscustomobject]@{
                    Name = "AgentHostDllSha256"
                    Expected = $ExpectedAgentHostSha256
                    Pattern = "^[0-9A-F]{64}$"
                })) {
            if (-not (Test-ObjectProperty $Evidence.Candidate $binding.Name)) {
                $null = $errors.Add("live matrix Candidate 缺少 $($binding.Name)。")
                continue
            }
            $actual = [string] $Evidence.Candidate.($binding.Name)
            if ($actual -cnotmatch $binding.Pattern -or $actual -cne $binding.Expected) {
                $null = $errors.Add("live matrix Candidate.$($binding.Name) 与 readiness 不一致。")
            }
        }
    }

    $items = @()
    if (-not (Test-ObjectProperty $Evidence "Items")) {
        $null = $errors.Add("live matrix 缺少 Items。")
    }
    else {
        $items = @($Evidence.Items)
    }

    $knownIds = @{}
    foreach ($item in $items) {
        if (-not (Test-ObjectProperty $item "Id") -or
            [string]::IsNullOrWhiteSpace([string] $item.Id)) {
            $null = $errors.Add("live matrix item 缺少 Id。")
            continue
        }
        $id = [string] $item.Id
        if ($script:LiveMatrixIds -cnotcontains $id) {
            $null = $errors.Add("live matrix 包含未知 item ID。")
            continue
        }
        if ($knownIds.ContainsKey($id)) {
            $null = $errors.Add("live matrix item 重复：$id。")
            continue
        }
        $knownIds[$id] = $item
    }

    foreach ($id in $script:LiveMatrixIds) {
        if (-not $knownIds.ContainsKey($id)) {
            $null = $errors.Add("live matrix 缺少 item：$id。")
            continue
        }
        $item = $knownIds[$id]
        if (-not (Test-ObjectProperty $item "Disposition")) {
            $null = $errors.Add("live matrix item $id 缺少 Disposition。")
            continue
        }
        $disposition = [string] $item.Disposition
        if ($disposition -ceq "verified") {
            $allowedVerifiedFields = if ($id -ceq "RealAbnormalExitMatrixVerified") {
                @("Id", "Disposition", "EvidenceSha256", "Outcome")
            }
            else {
                @("Id", "Disposition", "EvidenceSha256")
            }
            if (-not (Test-ExactObjectProperties $item $allowedVerifiedFields)) {
                $null = $errors.Add("verified live matrix item 包含未知字段。")
            }
            $evidenceSha256 = if (Test-ObjectProperty $item "EvidenceSha256") {
                [string] $item.EvidenceSha256
            }
            else {
                ""
            }
            if ($evidenceSha256 -cnotmatch "^[0-9A-F]{64}$") {
                $null = $errors.Add("verified item $id 缺少有效 EvidenceSha256。")
                continue
            }
            if ($evidenceSha256 -cmatch "^([0-9A-F])\1{63}$") {
                $null = $errors.Add("verified item $id 使用了占位 EvidenceSha256。")
                continue
            }

            if ($id -ceq "RealAbnormalExitMatrixVerified") {
                if (-not (Test-ObjectProperty $item "Outcome")) {
                    $null = $errors.Add("RealAbnormalExitMatrixVerified 缺少 Outcome。")
                    continue
                }
                if (-not (Test-ExactObjectProperties $item.Outcome @(
                            "AutoCadForcedTerminationVerified",
                            "AgentHostForcedTerminationVerified",
                            "CodexForcedTerminationVerified",
                            "UniqueTerminal",
                            "ResidualProcessCount",
                            "SubsequentRequestsFailClosed",
                            "SensitiveDataExposed"
                        ))) {
                    $null = $errors.Add(
                        "RealAbnormalExitMatrixVerified.Outcome 包含未知字段。")
                }
                foreach ($requiredTrue in @(
                        "AutoCadForcedTerminationVerified",
                        "AgentHostForcedTerminationVerified",
                        "CodexForcedTerminationVerified",
                        "UniqueTerminal",
                        "SubsequentRequestsFailClosed")) {
                    if (-not (Test-ObjectProperty $item.Outcome $requiredTrue) -or
                        -not (Test-JsonBooleanValue `
                            -Value $item.Outcome.($requiredTrue) -Expected $true)) {
                        $null = $errors.Add(
                            "RealAbnormalExitMatrixVerified.Outcome.$requiredTrue " +
                            "必须是 JSON boolean true。")
                    }
                }
                if (-not (Test-ObjectProperty $item.Outcome "ResidualProcessCount") -or
                    -not (Test-JsonZeroInteger $item.Outcome.ResidualProcessCount)) {
                    $null = $errors.Add(
                        "RealAbnormalExitMatrixVerified.Outcome.ResidualProcessCount " +
                        "必须是 JSON integer 0。")
                }
                if (-not (Test-ObjectProperty $item.Outcome "SensitiveDataExposed") -or
                    -not (Test-JsonBooleanValue `
                        -Value $item.Outcome.SensitiveDataExposed -Expected $false)) {
                    $null = $errors.Add(
                        "RealAbnormalExitMatrixVerified.Outcome.SensitiveDataExposed " +
                        "必须是 JSON boolean false。")
                }
            }
            $null = $verified.Add($id)
            continue
        }

        if ($disposition -ceq "deferred") {
            if (-not (Test-ExactObjectProperties $item @(
                        "Id", "Disposition", "Reason", "ReassessAt"
                    ))) {
                $null = $errors.Add("deferred live matrix item 包含未知字段。")
            }
            if ($id -ceq "RealAbnormalExitMatrixVerified") {
                $null = $errors.Add("RealAbnormalExitMatrixVerified 不允许 deferred。")
                continue
            }
            if (-not (Test-ObjectProperty $item "Reason") -or
                -not ($item.Reason -is [string]) -or
                -not (Test-SafeLiveMatrixReason -Value ([string] $item.Reason))) {
                $null = $errors.Add(
                    "deferred item $id 的 Reason 包含不允许的敏感形态，或不是单行 1–256 字符。")
            }
            $reassessAt = @()
            if (Test-ObjectProperty $item "ReassessAt") {
                $rawReassessAt = @($item.ReassessAt)
                if (@($rawReassessAt | Where-Object { -not ($_ -is [string]) }).Count -gt 0) {
                    $null = $errors.Add(
                        "deferred item $id 的 ReassessAt 必须是 JSON string 数组。")
                }
                $reassessAt = @($rawReassessAt | ForEach-Object { [string] $_ } |
                    Sort-Object -Unique)
            }
            if ($reassessAt.Count -ne 2 -or
                $reassessAt -cnotcontains "M9" -or
                $reassessAt -cnotcontains "M10") {
                $null = $errors.Add(
                    "deferred item $id 的 ReassessAt 必须精确包含 M9 和 M10。")
            }
            $null = $deferred.Add($id)
            continue
        }

        $null = $errors.Add("live matrix item $id 的 Disposition 只能是 verified 或 deferred。")
    }

    return [pscustomobject]@{
        Errors = @($errors)
        Verified = @($verified)
        Deferred = @($deferred)
    }
}

function New-LiveMatrixSelfTestEvidence {
    param(
        [Parameter(Mandatory = $true)][string] $HeadCommit,
        [Parameter(Mandatory = $true)][string] $HostSha256,
        [Parameter(Mandatory = $true)][string] $AgentHostSha256
    )

    $items = New-Object System.Collections.ArrayList
    foreach ($id in $script:LiveMatrixIds) {
        if ($id -ceq "RealAbnormalExitMatrixVerified") {
            $null = $items.Add([pscustomobject]@{
                Id = $id
                Disposition = "verified"
                EvidenceSha256 = "195621A76FA759E4527C5E5BCB82C1905CF70509ECA10B51811A014D8B659100"
                Outcome = [pscustomobject]@{
                    AutoCadForcedTerminationVerified = $true
                    AgentHostForcedTerminationVerified = $true
                    CodexForcedTerminationVerified = $true
                    UniqueTerminal = $true
                    ResidualProcessCount = 0
                    SubsequentRequestsFailClosed = $true
                    SensitiveDataExposed = $false
                }
            })
        }
        else {
            $null = $items.Add([pscustomobject]@{
                Id = $id
                Disposition = "deferred"
                Reason = "Self-test controlled deferral."
                ReassessAt = @("M9", "M10")
            })
        }
    }

    return [pscustomobject]@{
        SchemaVersion = 1
        Scope = "m4-live-matrix-results"
        Candidate = [pscustomobject]@{
            HeadCommit = $HeadCommit
            R201HostDllSha256 = $HostSha256
            AgentHostDllSha256 = $AgentHostSha256
        }
        Items = @($items)
    }
}

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
        [Parameter(Mandatory = $true)][bool] $RollbackRefResolvesToHead,
        [Parameter(Mandatory = $true)][bool] $CandidateCommitChainValid,
        [Parameter(Mandatory = $true)] $LiveMatrixDisposition
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
        $null = $blockers.Add("回滚点缺失或未指向 readiness 所绑定的候选提交。")
    }
    if (-not $CandidateCommitChainValid) {
        $null = $blockers.Add(
            "当前 HEAD 不是候选提交，或候选之后夹带了 live matrix 以外的修改。")
    }

    if (-not (Test-Property "Scope") -or
        [string] $Evidence.Scope -cne "m4-automated-readiness-binding") {
        $null = $blockers.Add("readiness evidence 的 Scope 不是 m4-automated-readiness-binding。")
    }

    if (-not (Test-Property "Source") -or
        -not (Test-ObjectProperty $Evidence.Source "HeadCommit") -or
        [string] $Evidence.Source.HeadCommit -cne $ExpectedHeadCommit) {
        $null = $blockers.Add("readiness evidence 绑定的 HeadCommit 与当前 HEAD 不一致。")
    }
    if (-not (Test-Property "Source") -or
        -not (Test-ObjectProperty $Evidence.Source "WorkingTreeDirty") -or
        -not (Test-JsonBooleanValue `
            -Value $Evidence.Source.WorkingTreeDirty -Expected $false)) {
        $null = $blockers.Add(
            "readiness evidence 的 Source.WorkingTreeDirty 必须是 JSON boolean false。")
    }

    # 冻结不能建立在弱保证上：FreshnessOnly 只证明五份 evidence 时间接近，
    # 证明不了它们来自同一次运行，而失败的门禁恰恰不写 evidence。
    if (-not (Test-Property "RunCorrelation") -or
        -not (Test-ObjectProperty $Evidence.RunCorrelation "Mode") -or
        [string] $Evidence.RunCorrelation.Mode -cne "Correlated" -or
        -not (Test-ObjectProperty $Evidence.RunCorrelation "Id") -or
        [string] $Evidence.RunCorrelation.Id -cnotmatch "^run-[0-9a-f]{32}$") {
        $null = $blockers.Add(
            "readiness evidence 必须包含 Correlated 模式和有效 RunCorrelation.Id。")
    }

    foreach ($mustBeTrue in @("AutomatedGatesPassed")) {
        if (-not (Test-Property $mustBeTrue) -or
            -not (Test-JsonBooleanValue `
                -Value $Evidence.$mustBeTrue -Expected $true)) {
            $null = $blockers.Add(
                "readiness evidence 的 $mustBeTrue 必须是 JSON boolean true。")
        }
    }

    # 冻结前这些必须仍然为 false：写入开关一旦在冻结候选里是开的，冻结本身就失去意义。
    foreach ($mustBeFalse in @(
            "CadWriteEnabled",
            "PluginInitiatedSaveEnabled",
            "AutoCadStartedOrCommanded",
            "M4Complete",
            "M416Frozen")) {
        if (-not (Test-Property $mustBeFalse) -or
            -not (Test-JsonBooleanValue `
                -Value $Evidence.$mustBeFalse -Expected $false)) {
            $null = $blockers.Add(
                "readiness evidence 的 $mustBeFalse 必须是 JSON boolean false。")
        }
    }

    # 权威目标把自动化 readiness 与实机处置分开：八项可以 deferred，只有真实异常退出
    # 必须 verified。不得再从 readiness 中固定为 false 的历史布尔值推断处置状态。
    if ($null -eq $LiveMatrixDisposition -or
        -not (Test-ObjectProperty $LiveMatrixDisposition "Errors") -or
        -not (Test-ObjectProperty $LiveMatrixDisposition "Verified") -or
        -not (Test-ObjectProperty $LiveMatrixDisposition "Deferred")) {
        $null = $blockers.Add("缺少有效的 live matrix disposition。")
    }
    else {
        foreach ($matrixError in @($LiveMatrixDisposition.Errors)) {
            $null = $blockers.Add("live matrix 无效：" + [string] $matrixError)
        }
        $recordedCount = @($LiveMatrixDisposition.Verified).Count +
            @($LiveMatrixDisposition.Deferred).Count
        if ($recordedCount -ne $script:LiveMatrixIds.Count) {
            $null = $blockers.Add(
                "live matrix 未精确处置全部 $($script:LiveMatrixIds.Count) 项。")
        }
        if (@($LiveMatrixDisposition.Verified) -cnotcontains
            "RealAbnormalExitMatrixVerified") {
            $null = $blockers.Add("RealAbnormalExitMatrixVerified 必须为 verified。")
        }
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
        RunCorrelation = [pscustomobject]@{
            Mode = "Correlated"
            Id = "run-" + ("1" * 32)
        }
        AutomatedGatesPassed = $true
        M4Complete = $false
        M416Frozen = $false
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
    $hostHash = "B" * 64
    $agentHostHash = "C" * 64

    if (-not (Test-LiveMatrixEvidenceCommitDelta -NameStatusLines @(
                "A`thandoff/autocad2016/live-matrix-results.json"
            ))) {
        throw "自检失败：唯一 live matrix evidence commit 未被接受。"
    }
    if (Test-LiveMatrixEvidenceCommitDelta -NameStatusLines @(
            "A`thandoff/autocad2016/live-matrix-results.json",
            "M`tsrc/Unexpected.cs"
        )) {
        throw "自检失败：夹带源码修改的 evidence commit 被接受。"
    }
    $minimalMatrixBytes = [Text.Encoding]::UTF8.GetBytes('{"SchemaVersion":1}')
    $minimalMatrix = Convert-M4LiveMatrixBytes -Bytes $minimalMatrixBytes
    if (-not (Test-ObjectProperty $minimalMatrix "Json") -or
        -not (Test-ObjectProperty $minimalMatrix "Sha256")) {
        throw "自检失败：有界 live matrix 字节解析器未返回 JSON 与 SHA-256。"
    }
    $minimalReadiness = Convert-M4JsonEvidenceBytes `
        -Bytes ([Text.Encoding]::UTF8.GetBytes('{"AutomatedGatesPassed":true}')) `
        -Label "readiness evidence" -MaximumBytes 4194304
    if (-not (Test-ObjectProperty $minimalReadiness "Json") -or
        -not (Test-ObjectProperty $minimalReadiness "Sha256") -or
        $minimalReadiness.Json.AutomatedGatesPassed -isnot [bool]) {
        throw "自检失败：readiness 单次字节解析器未返回强类型 JSON 与 SHA-256。"
    }
    $oversizedMatrixRejected = $false
    try {
        $null = Convert-M4LiveMatrixBytes -Bytes (New-Object byte[] 65537)
    }
    catch {
        $oversizedMatrixRejected = $true
    }
    if (-not $oversizedMatrixRejected) {
        throw "自检失败：超过 64 KiB 的 live matrix 未被拒绝。"
    }
    $selfTestDriveRoot = [IO.Path]::GetPathRoot($repoRoot)
    $selfTestArtifactRoot = [IO.Path]::GetFullPath(
        (Join-Path $selfTestDriveRoot "codex-freeze-self-test"))
    $expectedSelfTestEvidencePath = [IO.Path]::GetFullPath(
        (Join-Path $selfTestArtifactRoot "gate-evidence\freeze.json"))
    $selfTestEvidencePath = Resolve-M4FreezeEvidenceOutputPath `
        -Path $expectedSelfTestEvidencePath `
        -RepoRoot $repoRoot -ArtifactRoot $selfTestArtifactRoot
    if ($selfTestEvidencePath -cne $expectedSelfTestEvidencePath) {
        throw "自检失败：产物根内的冻结 evidence 路径未被接受。"
    }
    $escapedEvidenceRejected = $false
    try {
        $outsideEvidencePath = Join-Path $selfTestDriveRoot "outside-freeze-evidence.json"
        $null = Resolve-M4FreezeEvidenceOutputPath `
            -Path $outsideEvidencePath `
            -RepoRoot $repoRoot -ArtifactRoot $selfTestArtifactRoot
    }
    catch {
        $escapedEvidenceRejected = $true
    }
    if (-not $escapedEvidenceRejected) {
        throw "自检失败：产物根外的冻结 evidence 路径未被拒绝。"
    }

    # 权威目标允许除真实异常退出以外的八项明确延期；该处置必须成为可执行契约，
    # 不能继续沿用“九项全为 true”这一已经过期的冻结条件。
    $goalMatrix = New-LiveMatrixSelfTestEvidence `
        -HeadCommit $head -HostSha256 $hostHash -AgentHostSha256 $agentHostHash
    $goalDisposition = Resolve-M4LiveMatrixDisposition `
        -Evidence $goalMatrix -ExpectedHeadCommit $head `
        -ExpectedHostSha256 $hostHash -ExpectedAgentHostSha256 $agentHostHash
    if ($goalDisposition.Errors.Count -ne 0 -or
        $goalDisposition.Verified.Count -ne 1 -or
        $goalDisposition.Deferred.Count -ne 8) {
        throw ("自检失败：8 项 deferred + RealAbnormalExit verified 未被接受；" +
            "verified=$($goalDisposition.Verified.Count)，" +
            "deferred=$($goalDisposition.Deferred.Count)，" +
            "errors=" + ($goalDisposition.Errors -join " / "))
    }
    $stringReady = New-FreezeSelfTestEvidence
    $stringReady.AutomatedGatesPassed = "false"
    $stringReadyResult = Test-FreezePrecondition -Evidence $stringReady `
        -ExpectedHeadCommit $head -WorkingTreeClean $true `
        -RollbackRefResolvesToHead $true -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
    if (@($stringReadyResult | Where-Object {
                $_ -match "AutomatedGatesPassed 必须是 JSON boolean true"
            }).Count -ne 1) {
        throw "自检失败：字符串 AutomatedGatesPassed 被当作 JSON boolean true。"
    }
    $missingDirty = New-FreezeSelfTestEvidence
    $missingDirty.Source.PSObject.Properties.Remove("WorkingTreeDirty")
    $missingDirtyResult = Test-FreezePrecondition -Evidence $missingDirty `
        -ExpectedHeadCommit $head -WorkingTreeClean $true `
        -RollbackRefResolvesToHead $true -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
    if (@($missingDirtyResult | Where-Object {
                $_ -match "Source.WorkingTreeDirty 必须是 JSON boolean false"
            }).Count -ne 1) {
        throw "自检失败：缺少 Source.WorkingTreeDirty 时未失败关闭。"
    }
    $missingRunId = New-FreezeSelfTestEvidence
    $missingRunId.RunCorrelation.PSObject.Properties.Remove("Id")
    $missingRunIdResult = Test-FreezePrecondition -Evidence $missingRunId `
        -ExpectedHeadCommit $head -WorkingTreeClean $true `
        -RollbackRefResolvesToHead $true -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
    if (@($missingRunIdResult | Where-Object {
                $_ -match "有效 RunCorrelation.Id"
            }).Count -ne 1) {
        throw "自检失败：缺少 RunCorrelation.Id 时未失败关闭。"
    }
    $deferredAbnormal = $goalMatrix | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $deferredAbnormalItem = @($deferredAbnormal.Items | Where-Object {
            $_.Id -ceq "RealAbnormalExitMatrixVerified"
        })[0]
    $deferredAbnormalItem.Disposition = "deferred"
    $deferredAbnormalResult = Resolve-M4LiveMatrixDisposition `
        -Evidence $deferredAbnormal -ExpectedHeadCommit $head `
        -ExpectedHostSha256 $hostHash -ExpectedAgentHostSha256 $agentHostHash
    if (@($deferredAbnormalResult.Errors | Where-Object {
                $_ -match "RealAbnormalExitMatrixVerified 不允许 deferred"
            }).Count -ne 1) {
        throw "自检失败：RealAbnormalExitMatrixVerified 被允许 deferred。"
    }
    $missingDeferralReason = $goalMatrix | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $firstDeferredItem = @($missingDeferralReason.Items | Where-Object {
            $_.Disposition -ceq "deferred"
        })[0]
    $firstDeferredItem.PSObject.Properties.Remove("Reason")
    $firstDeferredItem.ReassessAt = @("M9")
    $missingDeferralResult = Resolve-M4LiveMatrixDisposition `
        -Evidence $missingDeferralReason -ExpectedHeadCommit $head `
        -ExpectedHostSha256 $hostHash -ExpectedAgentHostSha256 $agentHostHash
    if (@($missingDeferralResult.Errors | Where-Object {
                $_ -match "Reason"
            }).Count -eq 0 -or
        @($missingDeferralResult.Errors | Where-Object {
                $_ -match "ReassessAt"
            }).Count -eq 0) {
        throw "自检失败：deferred item 缺少理由或 M9/M10 重评点时未被拒绝。"
    }
    $wrongCandidate = $goalMatrix | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $wrongCandidate.Candidate.HeadCommit = "e" * 40
    $wrongCandidateResult = Resolve-M4LiveMatrixDisposition `
        -Evidence $wrongCandidate -ExpectedHeadCommit $head `
        -ExpectedHostSha256 $hostHash -ExpectedAgentHostSha256 $agentHostHash
    if (@($wrongCandidateResult.Errors | Where-Object {
                $_ -match "Candidate.HeadCommit"
            }).Count -ne 1) {
        throw "自检失败：live matrix 绑定到其他提交时未被拒绝。"
    }
    $unknownFieldMatrix = $goalMatrix | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $unknownFieldMatrix.Items[0] | Add-Member -NotePropertyName "RawEventLog" `
        -NotePropertyValue "should-not-be-committed"
    $unknownFieldResult = Resolve-M4LiveMatrixDisposition `
        -Evidence $unknownFieldMatrix -ExpectedHeadCommit $head `
        -ExpectedHostSha256 $hostHash -ExpectedAgentHostSha256 $agentHostHash
    if (@($unknownFieldResult.Errors | Where-Object {
                $_ -match "未知字段"
            }).Count -eq 0) {
        throw "自检失败：live matrix item 的未知字段未被拒绝。"
    }
    $sensitiveReasonMatrix = $goalMatrix | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $sensitiveReasonItem = @($sensitiveReasonMatrix.Items | Where-Object {
            $_.Disposition -ceq "deferred"
        })[0]
    $sensitiveReasonItem.Reason = "See C:\Users\Example and token=secret."
    $sensitiveReasonResult = Resolve-M4LiveMatrixDisposition `
        -Evidence $sensitiveReasonMatrix -ExpectedHeadCommit $head `
        -ExpectedHostSha256 $hostHash -ExpectedAgentHostSha256 $agentHostHash
    if (@($sensitiveReasonResult.Errors | Where-Object {
                $_ -match "Reason 包含不允许的敏感形态"
            }).Count -ne 1) {
        throw "自检失败：deferred Reason 中的敏感路径/secret 形态未被拒绝。"
    }
    $placeholderHashMatrix = $goalMatrix | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $placeholderHashItem = @($placeholderHashMatrix.Items | Where-Object {
            $_.Disposition -ceq "verified"
        })[0]
    $placeholderHashItem.EvidenceSha256 = "0" * 64
    $placeholderHashResult = Resolve-M4LiveMatrixDisposition `
        -Evidence $placeholderHashMatrix -ExpectedHeadCommit $head `
        -ExpectedHostSha256 $hostHash -ExpectedAgentHostSha256 $agentHostHash
    if (@($placeholderHashResult.Errors | Where-Object {
                $_ -match "占位 EvidenceSha256"
            }).Count -ne 1) {
        throw "自检失败：verified item 的占位 EvidenceSha256 未被拒绝。"
    }
    $stringBooleanMatrix = $goalMatrix | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $stringBooleanItem = @($stringBooleanMatrix.Items | Where-Object {
            $_.Id -ceq "RealAbnormalExitMatrixVerified"
        })[0]
    $stringBooleanItem.Outcome.UniqueTerminal = "false"
    $stringBooleanResult = Resolve-M4LiveMatrixDisposition `
        -Evidence $stringBooleanMatrix -ExpectedHeadCommit $head `
        -ExpectedHostSha256 $hostHash -ExpectedAgentHostSha256 $agentHostHash
    if (@($stringBooleanResult.Errors | Where-Object {
                $_ -match "UniqueTerminal 必须是 JSON boolean true"
            }).Count -ne 1) {
        throw "自检失败：字符串 false 被误当作 JSON boolean true。"
    }
    $stringIntegerMatrix = $goalMatrix | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $stringIntegerItem = @($stringIntegerMatrix.Items | Where-Object {
            $_.Id -ceq "RealAbnormalExitMatrixVerified"
        })[0]
    $stringIntegerItem.Outcome.ResidualProcessCount = "0"
    $stringIntegerResult = Resolve-M4LiveMatrixDisposition `
        -Evidence $stringIntegerMatrix -ExpectedHeadCommit $head `
        -ExpectedHostSha256 $hostHash -ExpectedAgentHostSha256 $agentHostHash
    if (@($stringIntegerResult.Errors | Where-Object {
                $_ -match "ResidualProcessCount 必须是 JSON integer 0"
            }).Count -ne 1) {
        throw "自检失败：字符串 0 被误当作 JSON integer 0。"
    }
    $goalReady = Test-FreezePrecondition `
        -Evidence (New-FreezeSelfTestEvidence) `
        -ExpectedHeadCommit $head -WorkingTreeClean $true `
        -RollbackRefResolvesToHead $true `
        -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
    if ($goalReady.Count -ne 0) {
        throw ("自检失败：符合权威处置规则的独立 live matrix 仍被拒绝：" +
            ($goalReady -join " / "))
    }

    # 全部满足时必须放行，否则这个门禁永远无法通过，等于没有判定能力。
    $ready = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence -Complete) `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $true `
        -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
    if ($ready.Count -ne 0) {
        throw ("自检失败：满足全部前置条件时仍被拒绝：" + ($ready -join " / "))
    }

    # 今天的真实形态：实机矩阵尚未形成合法处置，必须被拒绝。
    $missingMatrixDisposition = [pscustomobject]@{
        Errors = @("实机矩阵尚未记录。")
        Verified = @()
        Deferred = @()
    }
    $today = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence) `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $true `
        -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $missingMatrixDisposition
    if ($today.Count -eq 0) {
        throw "自检失败：实机矩阵未形成合法处置时没有被拒绝。"
    }

    $dirty = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence -Complete) `
        -ExpectedHeadCommit $head -WorkingTreeClean $false -RollbackRefResolvesToHead $true `
        -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
    if ($dirty.Count -eq 0) {
        throw "自检失败：工作树不干净时没有被拒绝。"
    }

    $noRollback = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence -Complete) `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $false `
        -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
    if ($noRollback.Count -eq 0) {
        throw "自检失败：缺少回滚点时没有被拒绝。"
    }

    $badCommitChain = Test-FreezePrecondition `
        -Evidence (New-FreezeSelfTestEvidence -Complete) `
        -ExpectedHeadCommit $head -WorkingTreeClean $true `
        -RollbackRefResolvesToHead $true -CandidateCommitChainValid $false `
        -LiveMatrixDisposition $goalDisposition
    if ($badCommitChain.Count -eq 0) {
        throw "自检失败：候选之后夹带其他修改时没有被拒绝。"
    }

    $otherHead = Test-FreezePrecondition -Evidence (New-FreezeSelfTestEvidence -Complete) `
        -ExpectedHeadCommit ("b" * 40) -WorkingTreeClean $true -RollbackRefResolvesToHead $true `
        -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
    if ($otherHead.Count -eq 0) {
        throw "自检失败：evidence 绑定到其他提交时没有被拒绝。"
    }

    $weak = New-FreezeSelfTestEvidence -Complete
    $weak.RunCorrelation.Mode = "FreshnessOnly"
    $weakResult = Test-FreezePrecondition -Evidence $weak `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $true `
        -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
    if ($weakResult.Count -eq 0) {
        throw "自检失败：FreshnessOnly 的弱关联证据被接受为冻结依据。"
    }

    $writeOn = New-FreezeSelfTestEvidence -Complete
    $writeOn.CadWriteEnabled = $true
    $writeOnResult = Test-FreezePrecondition -Evidence $writeOn `
        -ExpectedHeadCommit $head -WorkingTreeClean $true -RollbackRefResolvesToHead $true `
        -CandidateCommitChainValid $true `
        -LiveMatrixDisposition $goalDisposition
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
$readinessRead = Read-M4ReadinessEvidence -Path $resolvedReadiness
$readinessJson = $readinessRead.Json
$readinessSha256 = $readinessRead.Sha256

$currentHeadCommit = (& git -c "safe.directory=$safeRepoRoot" -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $currentHeadCommit -cnotmatch "^[0-9a-f]{40}$") {
    throw "无法读取当前 Git HEAD。"
}
$workingTreeClean = @(& git -c "safe.directory=$safeRepoRoot" -C $repoRoot status --porcelain).Count -eq 0
$candidateHeadCommit = if (
    (Test-ObjectProperty $readinessJson "Source") -and
    (Test-ObjectProperty $readinessJson.Source "HeadCommit")) {
    [string] $readinessJson.Source.HeadCommit
}
else {
    ""
}

# 实机结果必须在候选运行后才能产生，故不能把 Candidate.HeadCommit 写成保存该结果的提交本身。
# 允许当前 HEAD 等于候选，或仅比候选多一个 live-matrix-results.json evidence commit；
# 任何源码、脚本或其他文档夹带都会失败关闭。
$candidateCommitChainValid = $false
if ($candidateHeadCommit -cmatch "^[0-9a-f]{40}$") {
    if ($currentHeadCommit -ceq $candidateHeadCommit) {
        $candidateCommitChainValid = $true
    }
    else {
        try {
            & git -c "safe.directory=$safeRepoRoot" -C $repoRoot merge-base --is-ancestor `
                $candidateHeadCommit $currentHeadCommit 2>$null
            $isAncestorExit = $LASTEXITCODE
            if ($isAncestorExit -eq 0) {
                $commitRange = $candidateHeadCommit + ".." + $currentHeadCommit
                $nameStatusLines = @(& git -c "safe.directory=$safeRepoRoot" -C $repoRoot `
                        diff --name-status --no-renames $commitRange --)
                if ($LASTEXITCODE -eq 0) {
                    $candidateCommitChainValid =
                        Test-LiveMatrixEvidenceCommitDelta -NameStatusLines $nameStatusLines
                }
            }
        }
        catch {
            $candidateCommitChainValid = $false
        }
    }
}

$liveMatrixDisposition = [pscustomobject]@{
    Errors = @("缺少 handoff/autocad2016/live-matrix-results.json。")
    Verified = @()
    Deferred = @()
}
$liveMatrixSha256 = $null
$expectedLiveMatrixPath = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "handoff\autocad2016\live-matrix-results.json"))
if (-not [string]::IsNullOrWhiteSpace($LiveMatrixResultsPath)) {
    $resolvedLiveMatrixPath = if ([IO.Path]::IsPathRooted($LiveMatrixResultsPath)) {
        [IO.Path]::GetFullPath($LiveMatrixResultsPath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $LiveMatrixResultsPath))
    }
    if ($resolvedLiveMatrixPath -cne $expectedLiveMatrixPath) {
        $liveMatrixDisposition = [pscustomobject]@{
            Errors = @("live matrix 必须使用 handoff/autocad2016/live-matrix-results.json。")
            Verified = @()
            Deferred = @()
        }
    }
    elseif (-not (Test-Path -LiteralPath $resolvedLiveMatrixPath -PathType Leaf)) {
        $liveMatrixDisposition = [pscustomobject]@{
            Errors = @("live matrix 文件不存在。")
            Verified = @()
            Deferred = @()
        }
    }
    else {
        try {
            $liveMatrixRead = Read-M4LiveMatrixEvidence -Path $resolvedLiveMatrixPath
            $liveMatrixJson = $liveMatrixRead.Json
            $expectedHostSha256 = if (
                (Test-ObjectProperty $readinessJson "CandidateHashes") -and
                (Test-ObjectProperty $readinessJson.CandidateHashes "R201HostDllSha256")) {
                [string] $readinessJson.CandidateHashes.R201HostDllSha256
            }
            else {
                ""
            }
            $expectedAgentHostSha256 = if (
                (Test-ObjectProperty $readinessJson "CandidateHashes") -and
                (Test-ObjectProperty $readinessJson.CandidateHashes "AgentHostDllSha256")) {
                [string] $readinessJson.CandidateHashes.AgentHostDllSha256
            }
            else {
                ""
            }
            $liveMatrixDisposition = Resolve-M4LiveMatrixDisposition `
                -Evidence $liveMatrixJson -ExpectedHeadCommit $candidateHeadCommit `
                -ExpectedHostSha256 $expectedHostSha256 `
                -ExpectedAgentHostSha256 $expectedAgentHostSha256
            $liveMatrixSha256 = $liveMatrixRead.Sha256
        }
        catch {
            $liveMatrixDisposition = [pscustomobject]@{
                Errors = @("live matrix 无法按固定 schema 解析。")
                Verified = @()
                Deferred = @()
            }
            $liveMatrixSha256 = $null
        }
    }
}

$rollbackResolves = $false
if (-not [string]::IsNullOrWhiteSpace($RollbackRef)) {
    # 只读取。本脚本不创建、不移动、不删除任何 ref。
    # ref 不存在是预期分支而不是异常；PowerShell 7.4 起原生命令的非零退出码在
    # $ErrorActionPreference='Stop' 下会抛出，所以这里必须显式接住。
    try {
        $resolved = (& git -c "safe.directory=$safeRepoRoot" -C $repoRoot rev-parse --verify `
            --quiet ($RollbackRef + "^{commit}") 2>$null)
        if ($LASTEXITCODE -eq 0 -and $null -ne $resolved) {
            $rollbackResolves = (([string] $resolved).Trim() -ceq $candidateHeadCommit)
        }
    }
    catch {
        $rollbackResolves = $false
    }
}

$blockers = Test-FreezePrecondition -Evidence $readinessJson `
    -ExpectedHeadCommit $candidateHeadCommit -WorkingTreeClean $workingTreeClean `
    -RollbackRefResolvesToHead $rollbackResolves `
    -CandidateCommitChainValid $candidateCommitChainValid `
    -LiveMatrixDisposition $liveMatrixDisposition

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $resolvedEvidencePath = Resolve-M4FreezeEvidenceOutputPath `
        -Path $EvidencePath -RepoRoot $repoRoot -ArtifactRoot $buildSafety.ArtifactRoot
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidencePath) -Force | Out-Null
    $report = [ordered]@{
        SchemaVersion = 2
        RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
        RunCorrelationId = Get-CodexGateRunCorrelationId
        Scope = "m4-16-freeze-precondition-check"
        Status = if ($blockers.Count -eq 0) { "preconditions_met" } else { "freeze_refused" }
        CandidateHeadCommit = $candidateHeadCommit
        EvaluationHeadCommit = $currentHeadCommit
        CandidateCommitChainValid = $candidateCommitChainValid
        WorkingTreeClean = $workingTreeClean
        RollbackRefResolvesToHead = $rollbackResolves
        ReadinessSha256 = $readinessSha256
        LiveMatrixSha256 = $liveMatrixSha256
        Verified = @($liveMatrixDisposition.Verified)
        Deferred = @($liveMatrixDisposition.Deferred)
        BlockerCount = $blockers.Count
        Blockers = @($blockers)
        M4RequiredItemsComplete = ($blockers.Count -eq 0)
        M4Complete = ($blockers.Count -eq 0)
        M416Frozen = $false
        EvidenceBoundary = "This evidence records only whether the M4.16 freeze preconditions currently hold. Verified and deferred live-matrix dispositions are preserved as separate groups; RealAbnormalExitMatrixVerified cannot be deferred. It does not build a candidate, produce candidate hashes, create or move any Git ref, start or command AutoCAD, enable CAD writes or saves, or freeze M4.16."
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
