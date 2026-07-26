[CmdletBinding()]
param(
    [string] $Phase2PowerShell7EvidencePath,
    [string] $Phase2WindowsPowerShell51EvidencePath,
    [string] $AgentBootstrapEvidencePath,
    [string] $AuthCompatEvidencePath,
    [string] $R201HostEvidencePath,
    [string] $EvidencePath,
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
