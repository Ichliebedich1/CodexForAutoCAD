[CmdletBinding()]
param(
    [string] $Phase2PowerShell7EvidencePath,
    [string] $Phase2WindowsPowerShell51EvidencePath,
    [string] $AgentBootstrapEvidencePath,
    [string] $AuthCompatEvidencePath,
    [string] $R201HostEvidencePath,
    [string] $EvidencePath,

    # 上游门禁失败时通常不写 evidence，于是本汇总器会读到上一次成功遗留的旧文件，
    # 从而在门禁实际失败的情况下报绿。以下两个窗口用于证明五份输入 evidence 确实
    # 来自同一次、且是近期的门禁运行。时间窗只是第二层：它拦不住「同一小时内重跑、
    # 其中一项失败」的情况，那一层由 RunCorrelationId 负责，见 -RequireRunCorrelation。
    [int] $MaximumEvidenceAgeHours = 24,
    [int] $MaximumEvidenceSpreadHours = 6,

    # 要求五份输入 evidence 携带同一个 CODEX_GATE_RUN_ID 关联标识。默认关闭以兼容
    # 不设置该变量的历史调用方；关闭时汇总器只能给出较弱的时间窗保证，并在 evidence
    # 的 RunCorrelation.Mode 中把 FreshnessOnly 明确记录下来供审计。
    [switch] $RequireRunCorrelation,

    [switch] $SelfTestOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$artifactsRoot = $buildSafety.ArtifactRoot
$safeRepoRoot = $repoRoot.Replace("\", "/")
$bridgeLockPath = Join-Path $repoRoot "src\Codex.AutoCAD.Bridge.Client\packages.lock.json"
$stageRoot = Join-Path $artifactsRoot ("m4-automated-readiness-" + [Guid]::NewGuid().ToString("N"))

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string] $Value)
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

function Resolve-EvidenceRunCorrelation {
    # 证明五份 evidence 来自同一次门禁运行。缺失或不一致都必须失败关闭：任何一份来自
    # 别的运行，都意味着对应门禁这次没有真正通过。
    param(
        [Parameter(Mandatory = $true)][hashtable] $EvidenceByName,
        [bool] $Required
    )
    $present = New-Object System.Collections.ArrayList
    $missing = New-Object System.Collections.ArrayList
    foreach ($evidenceName in ($EvidenceByName.Keys | Sort-Object)) {
        $evidenceJson = $EvidenceByName[$evidenceName]
        $correlationId = $null
        if ($evidenceJson.PSObject.Properties.Name -contains "RunCorrelationId") {
            $rawCorrelationId = [string] $evidenceJson.RunCorrelationId
            if (-not [string]::IsNullOrWhiteSpace($rawCorrelationId)) {
                $correlationId = $rawCorrelationId.Trim()
            }
        }
        if ($null -eq $correlationId) {
            $null = $missing.Add($evidenceName)
        }
        else {
            $null = $present.Add($correlationId)
        }
    }

    if ($present.Count -eq 0) {
        if ($Required) {
            throw ("M4 就绪要求运行关联标识，但五份输入 evidence 均无 RunCorrelationId。" +
                "请在同一次套件运行中设置 CODEX_GATE_RUN_ID 后重跑全部上游门禁。")
        }
        return [pscustomobject]@{ Mode = "FreshnessOnly"; Id = $null }
    }
    if ($missing.Count -gt 0) {
        throw ("M4 就绪输入 evidence 的运行关联标识不完整，缺少：" +
            (@($missing) -join "、") + "。这些 evidence 不是同一次门禁运行产生的。")
    }
    $distinctIds = @($present | Sort-Object -Unique -CaseSensitive)
    if ($distinctIds.Count -ne 1) {
        throw ("M4 就绪输入 evidence 来自 " + $distinctIds.Count +
            " 次不同的门禁运行：RunCorrelationId 不一致。" +
            "上游门禁很可能失败并遗留了上一次成功的旧 evidence。")
    }
    return [pscustomobject]@{ Mode = "Correlated"; Id = $distinctIds[0] }
}

function Read-Evidence {
    param([Parameter(Mandatory = $true)][string] $Path)
    $resolved = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "缺少 M4 自动化就绪输入 evidence。"
    }
    try {
        $json = Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "M4 自动化就绪输入 evidence 不是有效 JSON。"
    }
    return [pscustomobject]@{
        Path = $resolved
        Sha256 = Get-Sha256 -Path $resolved
        Json = $json
    }
}

function Assert-PropertyEquals {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)] $Expected
    )
    if ($Object.PSObject.Properties.Name -notcontains $Name) {
        throw "M4 自动化就绪 evidence 缺少必需字段：$Name"
    }
    if ($Object.$Name -cne $Expected) {
        throw "M4 自动化就绪 evidence 字段不满足严格门禁：$Name"
    }
}

function Assert-FalseProperty {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    Assert-PropertyEquals -Object $Object -Name $Name -Expected $false
}

function Assert-TrueProperty {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )
    Assert-PropertyEquals -Object $Object -Name $Name -Expected $true
}

function Assert-Sha256Value {
    param([Parameter(Mandatory = $true)][string] $Value)
    if ($Value -cnotmatch "^[0-9A-F]{64}$") {
        throw "M4 自动化就绪 evidence 包含无效 SHA-256。"
    }
}

function Assert-SpecSummary {
    param([Parameter(Mandatory = $true)][string] $Value)
    $match = [regex]::Match($Value, "^(?<Passed>[1-9][0-9]*)/(?<Total>[1-9][0-9]*)$")
    if (-not $match.Success -or $match.Groups["Passed"].Value -cne $match.Groups["Total"].Value) {
        throw "M4 自动化就绪 evidence 包含未通过的规格摘要。"
    }
}

function Assert-Phase2Evidence {
    param(
        [Parameter(Mandatory = $true)] $Evidence,
        [Parameter(Mandatory = $true)][string] $ExpectedEdition
    )
    Assert-PropertyEquals $Evidence "SchemaVersion" 1
    Assert-PropertyEquals $Evidence "Scope" "phase2-managed-core-gate"
    Assert-PropertyEquals $Evidence "Status" "automated-gate-passed"
    Assert-PropertyEquals $Evidence "PowerShellEdition" $ExpectedEdition
    Assert-PropertyEquals $Evidence "Configuration" "Release"
    foreach ($name in @(
        "SolutionBuildPassed",
        "HostForbiddenApiScanPassed",
        "AgentHostDoctorPassed",
        "GitDiffCheckPassed",
        "BasicSecretScanPassed",
        "ConditionalLockFileRestored"
    )) {
        Assert-TrueProperty $Evidence $name
    }
    foreach ($name in @(
        "AutoCadStartedOrCommanded",
        "CadWriteEnabled",
        "PluginInitiatedSaveEnabled",
        "EnterpriseMatrixVerified",
        "RealMachinePolicyMatrixVerified"
    )) {
        Assert-FalseProperty $Evidence $name
    }
    $projects = @($Evidence.SpecProjects)
    if ($projects.Count -ne 9) {
        throw "Phase 2 evidence 必须包含精确 9 个规格项目。"
    }
    $total = 0
    foreach ($project in $projects) {
        if ([int] $project.Total -le 0 -or
            [int] $project.Passed -ne [int] $project.Total -or
            [int] $project.Failed -ne 0) {
            throw "Phase 2 evidence 存在未通过的规格项目。"
        }
        $total += [int] $project.Total
    }
    if ($total -ne [int] $Evidence.TotalSpecs) {
        throw "Phase 2 evidence 动态总数不一致。"
    }
    return @($projects | Sort-Object Name | ForEach-Object {
        [ordered]@{ Name = [string] $_.Name; Total = [int] $_.Total }
    })
}

function Assert-BootstrapEvidence {
    param([Parameter(Mandatory = $true)] $Evidence)
    Assert-PropertyEquals $Evidence "SchemaVersion" 16
    Assert-PropertyEquals $Evidence "Scope" "autocad2016-live-agenthost-inherited-handle-bootstrap-doctor"
    Assert-PropertyEquals $Evidence "Status" "live-agenthost-bootstrap-doctor-gate-passed"
    Assert-PropertyEquals $Evidence "Configuration" "Release"
    foreach ($name in @(
        "BitForBitMatch",
        "RunnableOutputTreesRecheckedAfterSpecs",
        "NoNewResidualAgentProcesses",
        "BootstrapPrimitiveSourceUnchanged"
    )) {
        Assert-TrueProperty $Evidence $name
    }
    foreach ($name in @(
        "ResidualAgentProcesses",
        "SourceTreeBinOrObjModified",
        "AutoCadProcessSetChanged",
        "AutoCadStartedOrRestarted",
        "CadCommandsSent",
        "NetLoadAttempted",
        "NetLoadVerified",
        "AgentHostLiveBridgeVerified",
        "CadRuntimeIntegrated",
        "EnterpriseNestedJobMatrixVerified"
    )) {
        Assert-FalseProperty $Evidence $name
    }
    Assert-SpecSummary ([string] $Evidence.Net45Specs)
    Assert-SpecSummary ([string] $Evidence.Net8Specs)
    if ([string] $Evidence.Net45Specs -cne [string] $Evidence.Net8Specs) {
        throw "Agent bootstrap net45/net8 规格摘要不一致。"
    }
    $candidateHash = [string] $Evidence.ArtifactHashes.AgentHostDll
    Assert-Sha256Value $candidateHash
    return $candidateHash
}

function Assert-AuthEvidence {
    param([Parameter(Mandatory = $true)] $Evidence)
    Assert-PropertyEquals $Evidence "SchemaVersion" 3
    Assert-PropertyEquals $Evidence "Scope" "autocad2016-net45-net8-auth-and-bootstrap-primitive"
    Assert-PropertyEquals $Evidence "Status" "static-and-cross-runtime-bootstrap-primitive-gate-passed"
    Assert-PropertyEquals $Evidence "Configuration" "Release"
    foreach ($name in @(
        "BitForBitMatch",
        "BootstrapSourceBoundaryVerified",
        "BootstrapCompiledMemberRefBoundaryVerifiedForNet45AndNet8",
        "BootstrapCompiledPublicApiBoundaryVerifiedForNet45AndNet8",
        "BootstrapCriticalStateMachineIlVerifiedForNet45AndNet8",
        "BootstrapCompleteImplementationIlFingerprintVerifiedForNet45AndNet8",
        "GitDiffCheckPassed"
    )) {
        if ($Evidence.PSObject.Properties.Name -contains $name) {
            Assert-TrueProperty $Evidence $name
        }
    }
    foreach ($name in @(
        "AutoCadProcessSetChanged",
        "AutoCadStartedOrRestarted",
        "CadCommandsSent",
        "NetLoadAttempted",
        "NetLoadVerified",
        "AgentHostLiveBridgeVerified",
        "RuntimeToCadCandidateBindingVerified"
    )) {
        Assert-FalseProperty $Evidence $name
    }
    Assert-SpecSummary ([string] $Evidence.Net45Specs)
    Assert-SpecSummary ([string] $Evidence.Net8Specs)
    Assert-SpecSummary ([string] $Evidence.BridgeRegressionSpecs)
    if ([string] $Evidence.Net45Specs -cne [string] $Evidence.Net8Specs) {
        throw "认证原语 net45/net8 规格摘要不一致。"
    }
}

function Assert-R201Evidence {
    param([Parameter(Mandatory = $true)] $Evidence)
    Assert-PropertyEquals $Evidence "SchemaVersion" 1
    Assert-PropertyEquals $Evidence "Scope" "m4-r201-host-build-gate"
    Assert-PropertyEquals $Evidence "Status" "automated-r201-build-passed"
    Assert-PropertyEquals $Evidence "Configuration" "Release"
    Assert-PropertyEquals $Evidence "TargetFramework" ".NETFramework,Version=v4.5"
    Assert-PropertyEquals $Evidence "Architecture" "x64"
    Assert-PropertyEquals $Evidence "AutoCadManagedApiVersion" "20.1.0.0"
    Assert-PropertyEquals $Evidence "IsolatedBuildCount" 2
    Assert-PropertyEquals $Evidence "AutodeskDllCopiedCount" 0
    Assert-PropertyEquals $Evidence "ReleaseWarnings" 0
    Assert-PropertyEquals $Evidence "ReleaseErrors" 0
    foreach ($name in @("BitForBitMatch", "LockFilesRestored")) {
        Assert-TrueProperty $Evidence $name
    }
    foreach ($name in @(
        "ResidualAgentProcesses",
        "AutoCadProcessSetChanged",
        "AutoCadStartedOrCommanded",
        "CadWriteEnabled",
        "PluginInitiatedSaveEnabled",
        "NetLoadVerified",
        "RuntimeVerified",
        "EnterpriseMatrixVerified"
    )) {
        Assert-FalseProperty $Evidence $name
    }
    Assert-Sha256Value ([string] $Evidence.HostCandidateSha256)
    Assert-Sha256Value ([string] $Evidence.BridgeClientLockSha256)
    Assert-Sha256Value ([string] $Evidence.HostLockSha256)
    foreach ($apiName in @("accoremgd.dll", "acdbmgd.dll", "acmgd.dll")) {
        $api = $Evidence.AutoCadManagedApis.$apiName
        if ($null -eq $api -or [string] $api.AssemblyVersion -cne "20.1.0.0") {
            throw "R20.1 evidence 缺少目标 AutoCAD API 身份。"
        }
        Assert-Sha256Value ([string] $api.Sha256)
    }
    return [string] $Evidence.HostCandidateSha256
}

function Get-SourceManifestFingerprint {
    [string[]] $files = @(
        & git -c "safe.directory=$safeRepoRoot" -C $repoRoot `
            ls-files --cached --others --exclude-standard |
            ForEach-Object { [string] $_ }
    )
    if ($LASTEXITCODE -ne 0) {
        throw "无法枚举当前源码以绑定 M4 自动化 evidence。"
    }
    [Array]::Sort($files, [StringComparer]::Ordinal)
    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $files) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }
        $normalized = $relativePath.Replace("\", "/")
        if ($normalized -match "(?:^|/)(?:artifacts|bin|obj)(?:/|$)") {
            continue
        }
        $absolutePath = Join-Path $repoRoot $relativePath
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
            continue
        }
        $item = Get-Item -LiteralPath $absolutePath
        $entries.Add($normalized + "`t" + [string] $item.Length + "`t" + (Get-Sha256 -Path $absolutePath))
    }
    return [pscustomobject]@{
        FileCount = $entries.Count
        Sha256 = Get-TextSha256 -Value ($entries -join "`n")
    }
}

function Get-RelevantProcessCount {
    return @(
        Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $_.ProcessName -ieq "Codex.AutoCAD.AgentHost" -or
            $_.ProcessName -ieq "Codex.AutoCAD.AgentLauncher.FakeAgentHost" -or
            $_.ProcessName -like "CodexLauncherFake-*" -or
            $_.ProcessName -ieq "Codex.AutoCAD.Bridge.Client.TestServer"
        }
    ).Count
}

if ($SelfTestOnly) {
    $rejectedTrue = $false
    try {
        Assert-FalseProperty ([pscustomobject]@{ EnterpriseMatrixVerified = $true }) `
            "EnterpriseMatrixVerified"
    }
    catch {
        $rejectedTrue = $true
    }
    if (-not $rejectedTrue) {
        throw "自检失败：未验证企业项不能接受 true。"
    }

    $rejectedHash = $false
    try {
        Assert-Sha256Value "00"
    }
    catch {
        $rejectedHash = $true
    }
    if (-not $rejectedHash) {
        throw "自检失败：无效候选哈希未被拒绝。"
    }

    Assert-SpecSummary "83/83"
    $rejectedPartialSummary = $false
    try {
        Assert-SpecSummary "82/83"
    }
    catch {
        $rejectedPartialSummary = $true
    }
    if (-not $rejectedPartialSummary) {
        throw "自检失败：部分通过的规格摘要未被拒绝。"
    }

    # "(absent)" 表示该份 evidence 完全没有 RunCorrelationId 属性；括号不在合法字符集内，
    # 因此不会与真实标识混淆。PowerShell 的强制参数绑定不接受全 $null 数组，故用哨兵值。
    $absentCorrelationId = "(absent)"

    function New-CorrelationSelfTestSet {
        param([string[]] $Ids)
        $names = @(
            "agent-bootstrap",
            "auth-compat",
            "phase2-powershell7",
            "phase2-windowspowershell51",
            "r201-host-build"
        )
        $set = @{}
        for ($i = 0; $i -lt $names.Count; $i++) {
            $candidateId = $Ids[$i]
            if ($candidateId -ceq $absentCorrelationId) {
                $set[$names[$i]] = [pscustomobject]@{ RecordedAtLocal = "self-test" }
            }
            else {
                $set[$names[$i]] = [pscustomobject]@{
                    RecordedAtLocal = "self-test"
                    RunCorrelationId = $candidateId
                }
            }
        }
        return $set
    }

    function Assert-CorrelationRejected {
        param(
            [string[]] $Ids,
            [bool] $Required,
            [string] $Because
        )
        $rejected = $false
        try {
            $null = Resolve-EvidenceRunCorrelation `
                -EvidenceByName (New-CorrelationSelfTestSet -Ids $Ids) -Required $Required
        }
        catch {
            $rejected = $true
        }
        if (-not $rejected) {
            throw "自检失败：$Because"
        }
    }

    $sameRun = @("run-a", "run-a", "run-a", "run-a", "run-a")
    $correlated = Resolve-EvidenceRunCorrelation `
        -EvidenceByName (New-CorrelationSelfTestSet -Ids $sameRun) -Required $true
    if ($correlated.Mode -cne "Correlated" -or $correlated.Id -cne "run-a") {
        throw "自检失败：同一运行标识的五份 evidence 未被判定为 Correlated。"
    }

    # 上游门禁失败时不写 evidence，遗留的旧文件要么没有标识，要么带着上一次的标识。
    Assert-CorrelationRejected -Ids @("run-a", "run-a", "run-a", "run-a", $absentCorrelationId) `
        -Required $false -Because "缺少一份运行标识未被拒绝。"
    Assert-CorrelationRejected -Ids @("run-a", "run-a", "run-a", "run-a", "run-b") `
        -Required $false -Because "混合两次运行标识未被拒绝。"
    Assert-CorrelationRejected -Ids @("run-a", "run-a", "run-a", "run-a", "RUN-A") `
        -Required $false -Because "仅大小写不同的运行标识未被区分。"
    Assert-CorrelationRejected -Ids @("run-a", "run-a", "run-a", "run-a", "   ") `
        -Required $false -Because "空白运行标识未被视为缺失。"
    $allAbsent = @($absentCorrelationId) * 5
    Assert-CorrelationRejected -Ids $allAbsent `
        -Required $true -Because "要求关联时全部缺失未被拒绝。"

    $legacy = Resolve-EvidenceRunCorrelation `
        -EvidenceByName (New-CorrelationSelfTestSet -Ids $allAbsent) `
        -Required $false
    if ($legacy.Mode -cne "FreshnessOnly" -or $null -ne $legacy.Id) {
        throw "自检失败：无关联标识时未退回并标注 FreshnessOnly。"
    }

    Write-Host "M4_AUTOMATED_READINESS_SELF_TEST=passed"
    return
}

foreach ($requiredInput in @(
    $Phase2PowerShell7EvidencePath,
    $Phase2WindowsPowerShell51EvidencePath,
    $AgentBootstrapEvidencePath,
    $AuthCompatEvidencePath,
    $R201HostEvidencePath
)) {
    if ([string]::IsNullOrWhiteSpace($requiredInput)) {
        throw "缺少 M4 自动化就绪输入 evidence 参数。"
    }
}

$phase2Ps7 = Read-Evidence $Phase2PowerShell7EvidencePath
$phase2Ps51 = Read-Evidence $Phase2WindowsPowerShell51EvidencePath
$bootstrap = Read-Evidence $AgentBootstrapEvidencePath
$auth = Read-Evidence $AuthCompatEvidencePath
$r201 = Read-Evidence $R201HostEvidencePath

# 上游门禁失败时不写 evidence，本汇总器会退回读到上一次成功遗留的旧文件，
# 于是在门禁实际失败时报绿——这正是 2026-07-26 M4.7 尝试中观察到的现象：
# Phase 2 与 auth 均失败，但汇总器仍以旧 evidence 输出 469/469 且退出码为 0。
# 因此在此校验五份 evidence 确实来自同一次、且是近期的运行。
$evidenceRecordedAt = @{
    "phase2-powershell7" = $phase2Ps7.Json
    "phase2-windowspowershell51" = $phase2Ps51.Json
    "agent-bootstrap" = $bootstrap.Json
    "auth-compat" = $auth.Json
    "r201-host-build" = $r201.Json
}
$recordedTimes = New-Object System.Collections.ArrayList
foreach ($evidenceName in ($evidenceRecordedAt.Keys | Sort-Object)) {
    $evidenceJson = $evidenceRecordedAt[$evidenceName]
    if (-not ($evidenceJson.PSObject.Properties.Name -contains "RecordedAtLocal")) {
        throw "M4 就绪输入 evidence 缺少 RecordedAtLocal：$evidenceName。"
    }
    $parsedTime = [datetime]::MinValue
    if (-not [datetime]::TryParse(
            [string] $evidenceJson.RecordedAtLocal,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None,
            [ref] $parsedTime)) {
        if (-not [datetime]::TryParse([string] $evidenceJson.RecordedAtLocal, [ref] $parsedTime)) {
            throw "M4 就绪输入 evidence 的 RecordedAtLocal 无法解析：$evidenceName。"
        }
    }
    $null = $recordedTimes.Add($parsedTime)
}

$oldestEvidence = ($recordedTimes | Sort-Object | Select-Object -First 1)
$newestEvidence = ($recordedTimes | Sort-Object | Select-Object -Last 1)
$evidenceAgeHours = [math]::Round(((Get-Date) - $oldestEvidence).TotalHours, 2)
$evidenceSpreadHours = [math]::Round(($newestEvidence - $oldestEvidence).TotalHours, 2)

if ($evidenceAgeHours -gt $MaximumEvidenceAgeHours) {
    throw ("M4 就绪输入 evidence 过旧：最早一份为 $evidenceAgeHours 小时前，" +
        "上限 $MaximumEvidenceAgeHours 小时。上游门禁很可能失败并遗留了旧 evidence。")
}
if ($evidenceSpreadHours -gt $MaximumEvidenceSpreadHours) {
    throw ("M4 就绪输入 evidence 时间跨度过大：$evidenceSpreadHours 小时，" +
        "上限 $MaximumEvidenceSpreadHours 小时。这些 evidence 不像来自同一次门禁运行。")
}

# 时间窗只是第二层，拦不住同一小时内的重跑：2026-07-26 R20.1 门禁因构建期间 AutoCAD
# 进程集合变化而失败，它遗留的 evidence 只有 26 分钟，落在两个窗口内，汇总器再次报绿。
# 关联标识把「同一次运行」变成可判定的事实，而不是从时间上推测。
$runCorrelation = Resolve-EvidenceRunCorrelation `
    -EvidenceByName $evidenceRecordedAt `
    -Required ([bool] $RequireRunCorrelation)

$phase2Ps7Specs = Assert-Phase2Evidence $phase2Ps7.Json "Core"
$phase2Ps51Specs = Assert-Phase2Evidence $phase2Ps51.Json "Desktop"
if (($phase2Ps7Specs | ConvertTo-Json -Depth 5 -Compress) -cne
    ($phase2Ps51Specs | ConvertTo-Json -Depth 5 -Compress)) {
    throw "PowerShell 7 与 Windows PowerShell 5.1 Phase 2 动态规格集合不一致。"
}
if ([int] $phase2Ps7.Json.TotalSpecs -ne [int] $phase2Ps51.Json.TotalSpecs) {
    throw "PowerShell 7 与 Windows PowerShell 5.1 Phase 2 总数不一致。"
}
$agentHostCandidateSha256 = Assert-BootstrapEvidence $bootstrap.Json
Assert-AuthEvidence $auth.Json
$hostCandidateSha256 = Assert-R201Evidence $r201.Json

if (-not (Test-Path -LiteralPath $bridgeLockPath -PathType Leaf)) {
    throw "缺少当前 Bridge.Client packages.lock.json。"
}
$currentBridgeLockSha256 = Get-Sha256 -Path $bridgeLockPath
if ($currentBridgeLockSha256 -cne [string] $r201.Json.BridgeClientLockSha256) {
    throw "当前 Bridge.Client 锁文件与 R20.1 evidence 不一致。"
}

$relevantProcessCount = Get-RelevantProcessCount
if ($relevantProcessCount -ne 0) {
    throw "M4 自动化就绪汇总时存在相关 Agent 或测试服务器残留进程。"
}
$sourceManifest = Get-SourceManifestFingerprint
$sourceHead = (& git -c "safe.directory=$safeRepoRoot" -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceHead -cnotmatch "^[0-9a-f]{40}$") {
    throw "无法绑定当前 Git HEAD。"
}
$sourceBranch = (& git -c "safe.directory=$safeRepoRoot" -C $repoRoot branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceBranch)) {
    throw "无法绑定当前 Git 分支。"
}
$workingTreeDirty = @(& git -c "safe.directory=$safeRepoRoot" -C $repoRoot status --porcelain).Count -gt 0
if ($LASTEXITCODE -ne 0) {
    throw "无法读取当前 Git 工作树状态。"
}
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($null -eq $userPath) {
    $userPath = ""
}
$userPathSha256 = Get-TextSha256 -Value $userPath

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $stageRoot "verification.json"
}
$resolvedEvidencePath = if ([IO.Path]::IsPathRooted($EvidencePath)) {
    [IO.Path]::GetFullPath($EvidencePath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidencePath))
}
$evidenceParent = Split-Path -Parent $resolvedEvidencePath
New-Item -ItemType Directory -Path $evidenceParent -Force | Out-Null

$readiness = [ordered]@{
    SchemaVersion = 1
    RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
    Scope = "m4-automated-readiness-binding"
    Status = "automated_readiness_only"
    Source = [ordered]@{
        HeadCommit = $sourceHead
        Branch = $sourceBranch
        WorkingTreeDirty = $workingTreeDirty
        ManifestFileCount = $sourceManifest.FileCount
        ManifestSha256 = $sourceManifest.Sha256
        BridgeClientLockSha256 = $currentBridgeLockSha256
    }
    Phase2 = [ordered]@{
        PowerShell7 = [ordered]@{
            Specs = ([string] $phase2Ps7.Json.TotalSpecs + "/" + [string] $phase2Ps7.Json.TotalSpecs)
            EvidenceSha256 = $phase2Ps7.Sha256
        }
        WindowsPowerShell51 = [ordered]@{
            Specs = ([string] $phase2Ps51.Json.TotalSpecs + "/" + [string] $phase2Ps51.Json.TotalSpecs)
            EvidenceSha256 = $phase2Ps51.Sha256
        }
        DynamicSpecSetsIdentical = $true
        ReleaseBuildPassed = $true
        HostForbiddenApiScanPassed = $true
        AgentHostDoctorPassed = $true
        GitDiffCheckPassed = $true
        BasicSecretScanPassed = $true
    }
    CandidateHashes = [ordered]@{
        R201HostDllSha256 = $hostCandidateSha256
        AgentHostDllSha256 = $agentHostCandidateSha256
    }
    InputEvidence = [ordered]@{
        AgentBootstrapSha256 = $bootstrap.Sha256
        AuthCompatSha256 = $auth.Sha256
        R201HostBuildSha256 = $r201.Sha256
    }
    EnvironmentFingerprint = [ordered]@{
        UserPathLength = $userPath.Length
        UserPathSha256 = $userPathSha256
        RelevantResidualProcessCount = $relevantProcessCount
        RawPathPersisted = $false
        RawEnvironmentPersisted = $false
    }
    # 证明五份输入 evidence 来自同一次近期运行，而不是上游门禁失败后遗留的旧文件。
    # 只记录相对窗口，不记录具体时刻，避免持久化本机时间线。
    EvidenceFreshness = [ordered]@{
        OldestInputAgeHours = $evidenceAgeHours
        InputSpreadHours = $evidenceSpreadHours
        MaximumAgeHours = $MaximumEvidenceAgeHours
        MaximumSpreadHours = $MaximumEvidenceSpreadHours
    }
    # Correlated 表示五份 evidence 携带同一个一次性运行标识，这是「同一次运行」的强证据；
    # FreshnessOnly 表示调用方没有设置 CODEX_GATE_RUN_ID，本次只有上面的时间窗保证，
    # 拦不住同一时间窗内失败门禁遗留的旧 evidence。审计时必须区分这两种模式。
    RunCorrelation = [ordered]@{
        Mode = $runCorrelation.Mode
        Id = $runCorrelation.Id
        Required = [bool] $RequireRunCorrelation
    }
    AutomatedGatesPassed = $true
    AutoCadStartedOrCommanded = $false
    NetLoadVerified = $false
    CadWriteEnabled = $false
    PluginInitiatedSaveEnabled = $false
    RealCredentialManagerVerified = $false
    RealCodexLoginAndKeyringVerified = $false
    RealRestrictedTokenProductChainVerified = $false
    RealFixedCapacityVolumeVerified = $false
    RealDiskFullVerified = $false
    RealPowerLossVerified = $false
    RealAbnormalExitMatrixVerified = $false
    EnterpriseAppLockerWacEdRMatrixVerified = $false
    EnterpriseRetentionArchiveMatrixVerified = $false
    M4Complete = $false
    M416Frozen = $false
    EvidenceBoundary = "This is a machine-readable binding of automated M4 readiness only. It binds dual-shell Phase 2 results, current R20.1 Host and AgentHost candidate hashes, authentication/bootstrap evidence, the current source manifest, the tracked Bridge.Client lock file, a hashed user-PATH fingerprint, secret/API checks, and an empty relevant-process state. Every real-machine, credential, restricted-token, disk-full, power-loss, abnormal-exit, enterprise execution-control, and enterprise retention item remains explicitly false. It does not start or command AutoCAD, verify NETLOAD, enable CAD writes or saves, complete M4, or freeze M4.16."
}

$encoding = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($resolvedEvidencePath, ($readiness | ConvertTo-Json -Depth 12), $encoding)
Complete-CodexBuildSafety -State $buildSafety -Stage "m4-automated-readiness" | Out-Null
Write-Host "`nM4 自动化就绪证据绑定通过；真实机器与企业矩阵仍未验证。" -ForegroundColor Green
Write-Host ("M4_AUTOMATED_READINESS_EVIDENCE=" + $resolvedEvidencePath)
