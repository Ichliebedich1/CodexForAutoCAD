[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string] $Configuration = "Release",

    [string] $CodexExecutable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$safeRepoRoot = $repoRoot.Replace("\", "/")
$artifactsRoot = Join-Path $repoRoot "artifacts"
$runId = [Guid]::NewGuid().ToString("N")
$stageRoot = Join-Path $artifactsRoot ("autocad2016-agent-bootstrap-stage-" + $runId)
$finalEvidencePath = Join-Path $repoRoot `
    "handoff\autocad2016\evidence\agent-bootstrap-verification-20260719.json"
$bootstrapVerifier = Join-Path $PSScriptRoot "verify-autocad2016-agent-bootstrap.ps1"
$authVerifier = Join-Path $PSScriptRoot "verify-autocad2016-auth-compat.ps1"
$phase2Verifier = Join-Path $PSScriptRoot "verify-phase2.ps1"
$orchestratorPath = $MyInvocation.MyCommand.Path
$pwshCommand = (Get-Command pwsh -ErrorAction Stop).Source
$windowsPowerShellCommand = Join-Path `
    $env:SystemRoot `
    "System32\WindowsPowerShell\v1.0\powershell.exe"
$expectedPhase2ProjectResults = [ordered]@{
    "Codex.AutoCAD.Contracts.Specs" = 96
    "Codex.AutoCAD.Ipc.Specs" = 35
    "Codex.AutoCAD.Security.Specs" = 19
    "Codex.AutoCAD.AppServer.Specs" = 32
    "Codex.AutoCAD.Bridge.Specs" = 49
    "Codex.AutoCAD.Bridge.Client.Specs" = 30
    "Codex.AutoCAD.AgentRuntime.Specs" = 34
    "Codex.AutoCAD.Chat.Specs" = 9
    "Codex.AutoCAD.Host.2016.Mvp.Specs" = 56
}
$expectedPhase2Specs = [int] (
    ($expectedPhase2ProjectResults.Values | Measure-Object -Sum).Sum)
if ($expectedPhase2Specs -ne 360) {
    throw "冻结 Phase2 项目计数必须精确合计为 360。"
}

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

function Assert-FileExists {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "缺少预期文件：$Path"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    Assert-FileExists -Path $Path
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string] $Text)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        try {
            return ([BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace("-", "")
        }
        finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-CandidateManifestBinding {
    $evidenceAbsolutePath = [IO.Path]::GetFullPath($finalEvidencePath)
    $evidenceRelativePath = $evidenceAbsolutePath.Substring(
        $repoRoot.Length + 1).Replace("\", "/")
    $gitFiles = & git -c "safe.directory=$safeRepoRoot" -C $repoRoot `
        ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) {
        throw "无法枚举候选文件以生成工作树绑定。"
    }

    $manifestLines = [System.Collections.Generic.List[string]]::new()
    $relativePaths = @(
        $gitFiles |
            ForEach-Object { ([string] $_).Replace("\", "/") } |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                $_ -cne $evidenceRelativePath
            } |
            Sort-Object -Unique
    )
    foreach ($relativePath in $relativePaths) {
        $absolutePath = Join-Path $repoRoot $relativePath
        if (Test-Path -LiteralPath $absolutePath -PathType Leaf) {
            $item = Get-Item -LiteralPath $absolutePath
            $manifestLines.Add(
                $relativePath + "`t" + $item.Length + "`t" +
                (Get-Sha256 -Path $absolutePath))
        }
        else {
            $manifestLines.Add($relativePath + "`tMISSING")
        }
    }

    $manifestText = @($manifestLines) -join "`n"
    return [pscustomobject]@{
        FileCount = $manifestLines.Count
        ManifestSha256 = Get-TextSha256 -Text $manifestText
        EvidenceFileExcludedToAvoidSelfReference = $true
    }
}

function Assert-CandidateManifestEqual {
    param(
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if ($Expected.FileCount -ne $Actual.FileCount -or
        $Expected.ManifestSha256 -cne $Actual.ManifestSha256) {
        throw "$Label 期间候选工作树发生变化。"
    }
}

function Remove-AnsiEscape {
    param([AllowEmptyString()][string] $Text)

    return [regex]::Replace(
        $Text,
        ([char]27 + '\[[0-?]*[ -/]*[@-~]'),
        "",
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Get-AutoCadProcessSnapshot {
    $snapshot = [System.Collections.Generic.List[string]]::new()
    foreach ($process in @(Get-Process -Name acad -ErrorAction SilentlyContinue)) {
        try {
            $identity = "{0}|{1}" -f `
                $process.Id,
                $process.StartTime.ToUniversalTime().Ticks
        }
        catch {
            $identity = "{0}|unavailable" -f $process.Id
        }
        $snapshot.Add($identity)
    }

    return @($snapshot | Sort-Object)
}

function Assert-AutoCadProcessSetUnchanged {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]] $Before,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]] $After
    )

    if (($Before -join ",") -cne ($After -join ",")) {
        throw "最终阶段验证期间 AutoCAD 进程集合发生变化。"
    }
}

function Invoke-ChildPowerShellGate {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $ShellPath,
        [Parameter(Mandatory = $true)][string] $ScriptPath,
        [string[]] $ScriptArguments = @()
    )

    Assert-FileExists -Path $ShellPath
    Assert-FileExists -Path $ScriptPath

    $safeLabel = $Label.ToLowerInvariant() -replace '[^a-z0-9.-]', '-'
    $logPath = Join-Path $stageRoot ($safeLabel + ".log")
    Write-Host ("`n==> " + $Label) -ForegroundColor Cyan

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $raw = & $ShellPath @(
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy", "Bypass",
            "-File", $ScriptPath
        ) @ScriptArguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    $lines = @($raw | ForEach-Object { Remove-AnsiEscape -Text ([string] $_) })
    $lines | Set-Content -LiteralPath $logPath -Encoding UTF8

    if ($exitCode -ne 0) {
        $tail = @($lines | Select-Object -Last 20) -join [Environment]::NewLine
        throw "$Label 失败，退出码：$exitCode。原始日志位于 ignored artifacts。`n$tail"
    }

    Write-Host ("通过；日志 SHA-256：" + (Get-Sha256 -Path $logPath)) -ForegroundColor Green
    return [pscustomobject]@{
        Label = $Label
        Lines = $lines
        LogPath = $logPath
        LogSha256 = Get-Sha256 -Path $logPath
        ExitCode = $exitCode
    }
}

function Read-MarkedEvidence {
    param(
        [Parameter(Mandatory = $true)] $GateRun,
        [Parameter(Mandatory = $true)][string] $MarkerName
    )

    $pattern = "^" + [regex]::Escape($MarkerName) + "=(?<path>.+)$"
    $markerMatches = [System.Collections.Generic.List[object]]::new()
    foreach ($line in @($GateRun.Lines)) {
        $match = [regex]::Match(
            [string] $line,
            $pattern,
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if ($match.Success) {
            $markerMatches.Add($match)
        }
    }

    if ($markerMatches.Count -ne 1) {
        throw "$($GateRun.Label) 必须且只能输出一条 $MarkerName 标记；实际：$($markerMatches.Count)。"
    }

    $resolvedPath = (Resolve-Path -LiteralPath `
        $markerMatches[0].Groups["path"].Value.Trim()).Path
    $artifactPrefix = $artifactsRoot.TrimEnd('\') + "\"
    if (-not $resolvedPath.StartsWith(
            $artifactPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$MarkerName 必须指向仓库 ignored artifacts 内的原始证据。"
    }

    $json = Get-Content -LiteralPath $resolvedPath -Raw -Encoding UTF8 | ConvertFrom-Json
    return [pscustomobject]@{
        Path = $resolvedPath
        Sha256 = Get-Sha256 -Path $resolvedPath
        Json = $json
    }
}

function Assert-PropertyEquals {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)] $Expected
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "证据缺少字段：$Name"
    }
    if ($property.Value -cne $Expected) {
        throw "证据字段 $Name 不符合预期；actual=$($property.Value)，expected=$Expected。"
    }
}

function Assert-BooleanProperty {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][bool] $Expected
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool]) {
        throw "证据字段 $Name 必须是布尔值。"
    }
    if ([bool] $property.Value -ne $Expected) {
        throw "证据字段 $Name 不符合预期；actual=$($property.Value)，expected=$Expected。"
    }
}

function Assert-BootstrapEvidence {
    param([Parameter(Mandatory = $true)] $Evidence)

    Assert-PropertyEquals $Evidence "SchemaVersion" 16
    Assert-PropertyEquals $Evidence "Scope" `
        "autocad2016-live-agenthost-inherited-handle-bootstrap-doctor"
    Assert-PropertyEquals $Evidence "Status" `
        "live-agenthost-bootstrap-doctor-gate-passed"
    Assert-PropertyEquals $Evidence "Configuration" "Release"
    Assert-PropertyEquals $Evidence "Net45Specs" "57/57"
    Assert-PropertyEquals $Evidence "Net8Specs" "57/57"
    Assert-PropertyEquals $Evidence "RelevantProcessBaselineCount" 0
    Assert-PropertyEquals $Evidence "RelevantProcessFinalCount" 0

    foreach ($name in @(
        "BitForBitMatch",
        "RunnableOutputTreeComparedByRelativePathAndSha256",
        "RunnableOutputTreesRecheckedAfterSpecs",
        "BootstrapPrimitiveSourceUnchanged",
        "NoNewResidualAgentProcesses",
        "ExternalAuthenticationKeyDeliveryLiveVerified",
        "DedicatedInheritedHandleTransportLiveVerified",
        "ChildConfirmationPidAndCreationTimeBindingLiveVerified",
        "ApprovedExecutableSha256EnforcementLiveVerified",
        "StartupDeadlineAbortAndBoundedTerminationCleanupLiveVerified",
        "ProcessTreeCleanupOnServiceStopLiveVerified",
        "ProcessTreeStartStopRepeat500Verified",
        "ProcessTreeCleanupOnUnexpectedAgentHostExitLiveVerified",
        "ProcessTreeCleanupOnOwnerExitLiveVerified",
        "ProcessTreeResourceLimitsRuntimeVerified",
        "ResourceLimitTerminalAttributionRuntimeVerified",
        "NestedJobAssignmentCurrentRuntimeVerified",
        "SessionWallClockDeadlineRuntimeVerified",
        "GracefulServiceExitBeforeForcedTerminationRuntimeVerified",
        "ConfiguredGracefulStopTimeoutRuntimeVerified",
        "ProtectedSessionWorkspaceLifecycleRuntimeVerified"
    )) {
        Assert-BooleanProperty $Evidence $name $true
    }

    foreach ($name in @(
        "ResidualAgentProcesses",
        "BootstrapTransportConfidentialityLiveVerified",
        "BootstrapTransportConfidentialityAgainstExternalHandleDuplicationVerified",
        "ChildProcessIdentityBindingLiveVerified",
        "ExecutableFileIdentityToctouRaceDynamicallyVerified",
        "SuspendedLaunchRaceDynamicallyVerified",
        "PendingBootstrapAtomicConsumptionLiveVerified",
        "EnterpriseNestedJobMatrixVerified",
        "SourceTreeBinOrObjModified",
        "AutoCadProcessSetChanged",
        "AutoCadStartedOrRestarted",
        "CadCommandsSent",
        "NetLoadAttempted",
        "NetLoadVerified",
        "AgentHostLiveBridgeVerified",
        "CadRuntimeIntegrated"
    )) {
        Assert-BooleanProperty $Evidence $name $false
    }

    foreach ($name in @(
        "RealAgentHostBootstrapDoctorCompleted",
        "RepeatedRealAgentHostBootstrapCompleted",
        "InvalidExecutablePathsFailClosed",
        "ApprovedExecutableSha256MismatchRejected",
        "StartupTimeoutTriggersFailClosedAbortAndBoundedCleanup",
        "ConfirmationThenHangTriggersFailClosedAbortAndBoundedCleanup",
        "CallerThreadNonBlockingVerified",
        "CancellationTerminatesUnconfirmedChild",
        "EarlyExitFailClosed",
        "MalformedConfirmationRejected",
        "ConfirmationIdentityMismatchRejected",
        "TrailingAndDuplicateConfirmationRejected",
        "ChildClearsInheritedFlags",
        "HandleAllowlistCanaryVerified",
        "StandardErrorSeparateAndBounded",
        "ServiceStopKillsProcessTree",
        "UnexpectedAgentHostExitKillsProcessTree",
        "ProcessOwnerExitKillsProcessTree",
        "ProcessTreeResourceLimitsApplied",
        "InvalidProcessTreeResourceLimitsFailClosed",
        "ResourceLimitErrorCodesStable",
        "NestedJobAssignmentCompatible",
        "JobUserTimeTerminatesProcessTree",
        "JobProcessLimitProducesStructuredTerminal",
        "JobMemoryLimitProducesStructuredTerminal",
        "CombinedJobLimitsProduceSingleTerminal",
        "SessionWallClockTerminatesProcessTree",
        "SessionWallClockRetriesCleanup",
        "SessionStopPreventsRuntimeExpiry",
        "ServiceStopUsesConfiguredGrace",
        "SessionWorkspaceProtectedLayout",
        "SessionWorkspaceDuplicateRejected",
        "SessionWorkspaceInvalidRootsRejected",
        "SessionWorkspaceReparseRootRejected",
        "SessionWorkspaceActiveLeasePreserved",
        "SessionWorkspaceCrashRecovery",
        "ServiceSessionWorkspaceRemoved",
        "ServiceStartFailureWorkspaceRemoved",
        "ServiceWorkspaceCleanupCanRetry"
    )) {
        Assert-BooleanProperty $Evidence.RuntimeEvidence $name $true
    }
}

function Assert-AuthEvidence {
    param([Parameter(Mandatory = $true)] $Evidence)

    Assert-PropertyEquals $Evidence "SchemaVersion" 3
    Assert-PropertyEquals $Evidence "Scope" `
        "autocad2016-net45-net8-auth-and-bootstrap-primitive"
    Assert-PropertyEquals $Evidence "Status" `
        "static-and-cross-runtime-bootstrap-primitive-gate-passed"
    Assert-PropertyEquals $Evidence "Configuration" "Release"
    Assert-PropertyEquals $Evidence "Net45Specs" "35/35"
    Assert-PropertyEquals $Evidence "Net8Specs" "35/35"
    Assert-PropertyEquals $Evidence "BridgeRegressionSpecs" "49/49"

    foreach ($name in @(
        "AuthCompatIsolatedRestoreOffline",
        "ManagedCoreRegressionOutputsIsolated",
        "BitForBitMatch",
        "BridgeRuntimeCopyMatchesProjectOutput",
        "NullSignedFieldsRejected",
        "SequenceStrictlyIncrementsByOne",
        "NonceReplayRejected",
        "InvalidMacDoesNotAdvanceState",
        "SecretPrivateCopyZeroedOnDispose",
        "ExternalAuthenticationKeyRequired",
        "AuthenticationAndSessionKeyReuseRejected",
        "SingleFrameAndEofRequired",
        "SyncAndAsyncAll180TruncationOffsetsRejected",
        "DirectionReflectionRejectedWithoutStateAdvance",
        "PayloadSingleUse",
        "SingleFrameWriteAttempt",
        "FailedWriteConsumesPayload",
        "InboundPayloadForwardingRejected",
        "InboundForwardingAttemptConsumesPayload",
        "OutboundDerivationRequiresSuccessfulWrite",
        "EndpointRoleBoundByPayloadOrigin",
        "InboundAndOutboundClaimedOnce",
        "BootstrapSourceBoundaryVerified",
        "BootstrapCompiledMemberRefBoundaryVerifiedForNet45AndNet8",
        "BootstrapCompiledPublicApiBoundaryVerifiedForNet45AndNet8",
        "BootstrapCriticalStateMachineIlVerifiedForNet45AndNet8",
        "BootstrapCompleteImplementationIlFingerprintVerifiedForNet45AndNet8"
    )) {
        Assert-BooleanProperty $Evidence $name $true
    }

    foreach ($name in @(
        "ManagedCoreRegressionRestoreOffline",
        "AllRuntimeAndTargetStreamSecretCopiesEliminated",
        "ProjectLocalObjModified",
        "AutoCadProcessSetChanged",
        "AutoCadStartedOrRestarted",
        "CadCommandsSent",
        "NetLoadAttempted",
        "NetLoadVerified",
        "ExternalAuthenticationKeyDeliveryLiveVerified",
        "BootstrapTransportConfidentialityLiveVerified",
        "PendingBootstrapAtomicConsumptionLiveVerified",
        "ChildProcessIdentityBindingLiveVerified",
        "HardTimeoutAndProcessLifecycleLiveVerified",
        "AgentHostLiveBridgeVerified",
        "RuntimeToCadCandidateBindingVerified"
    )) {
        Assert-BooleanProperty $Evidence $name $false
    }
}

function Get-ComparableEvidenceJson {
    param([Parameter(Mandatory = $true)] $Evidence)

    $clone = ($Evidence | ConvertTo-Json -Depth 50) | ConvertFrom-Json
    $clone.PSObject.Properties.Remove("RecordedAtLocal")
    $clone.PSObject.Properties.Remove("PowerShellVersion")
    return ($clone | ConvertTo-Json -Depth 50 -Compress)
}

function Assert-Phase2Log {
    param([Parameter(Mandatory = $true)] $GateRun)

    $text = (@($GateRun.Lines) -join "`n").Replace("`r", "")
    $warningMatches = [regex]::Matches(
        $text,
        '(?im)^\s*(?<count>\d+)\s+(?:Warning\(s\)|warnings?|个警告)\s*$')
    $errorMatches = [regex]::Matches(
        $text,
        '(?im)^\s*(?<count>\d+)\s+(?:Error\(s\)|errors?|个错误)\s*$')
    if ($warningMatches.Count -lt 1 -or $errorMatches.Count -lt 1) {
        throw "$($GateRun.Label) 缺少可机读的 Release warning/error 汇总。"
    }
    foreach ($match in $warningMatches) {
        if ([int] $match.Groups["count"].Value -ne 0) {
            throw "$($GateRun.Label) Release 构建存在 warning。"
        }
    }
    foreach ($match in $errorMatches) {
        if ([int] $match.Groups["count"].Value -ne 0) {
            throw "$($GateRun.Label) Release 构建存在 error。"
        }
    }

    $aggregateMatches = [regex]::Matches(
        $text,
        '(?m)^==> 规格动态计数汇总：(?<passed>\d+)/(?<total>\d+)\s*$')
    if ($aggregateMatches.Count -ne 1) {
        throw "$($GateRun.Label) 必须且只能输出一条 Phase2 规格汇总。"
    }
    $passed = [int] $aggregateMatches[0].Groups["passed"].Value
    $total = [int] $aggregateMatches[0].Groups["total"].Value
    if ($passed -ne $expectedPhase2Specs -or $total -ne $expectedPhase2Specs) {
        throw "$($GateRun.Label) Phase2 规格门禁失败；actual=$passed/$total，expected=$expectedPhase2Specs/$expectedPhase2Specs。"
    }

    $projectPattern = '(?m)^(?<name>Codex\.AutoCAD\.[A-Za-z0-9.]+\.Specs):\s*(?<passed>\d+)/(?<total>\d+)\s*$'
    $projectMatches = [regex]::Matches($text, $projectPattern)
    if ($projectMatches.Count -ne $expectedPhase2ProjectResults.Count) {
        throw "$($GateRun.Label) 必须输出冻结的 $($expectedPhase2ProjectResults.Count) 个 Phase2 规格项目汇总；实际：$($projectMatches.Count)。"
    }
    $actualProjectResults = [ordered]@{}
    foreach ($match in $projectMatches) {
        $name = $match.Groups["name"].Value
        $projectPassed = [int] $match.Groups["passed"].Value
        $projectCount = [int] $match.Groups["total"].Value
        if (-not $expectedPhase2ProjectResults.Contains($name)) {
            throw "$($GateRun.Label) 出现未批准的 Phase2 规格项目：$name。"
        }
        if ($actualProjectResults.Contains($name)) {
            throw "$($GateRun.Label) Phase2 规格项目汇总重复：$name。"
        }
        $expectedCount = [int] $expectedPhase2ProjectResults[$name]
        if ($projectPassed -ne $expectedCount -or $projectCount -ne $expectedCount) {
            throw "$($GateRun.Label) Phase2 项目 $name 必须精确为 $expectedCount/$expectedCount；实际：$projectPassed/$projectCount。"
        }
        $actualProjectResults[$name] = $projectCount
    }

    $projectResults = [ordered]@{}
    foreach ($entry in $expectedPhase2ProjectResults.GetEnumerator()) {
        if (-not $actualProjectResults.Contains($entry.Key)) {
            throw "$($GateRun.Label) 缺少冻结 Phase2 规格项目：$($entry.Key)。"
        }
        $projectResults[$entry.Key] = "$($entry.Value)/$($entry.Value)"
    }

    $requiredPatterns = [ordered]@{
        HostForbiddenApiScan = 'AutoCAD Host 受审 Compile 闭包及词法禁用 API 扫描通过'
        DoctorStarted = '==> 执行 AgentHost doctor 活体握手'
        DoctorOk = '"ok"\s*:\s*true'
        DoctorRunning = '"state"\s*:\s*"Running"'
        UnstagedDiff = '==> 检查未暂存差异格式'
        StagedDiff = '==> 检查已暂存差异格式'
        SecretScan = '敏感信息基础扫描通过。'
        FinalGate = '阶段 2 托管核心门禁通过：Release 构建'
    }
    foreach ($entry in $requiredPatterns.GetEnumerator()) {
        if (-not [regex]::IsMatch(
                $text,
                [string] $entry.Value,
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            throw "$($GateRun.Label) 缺少成功标记：$($entry.Key)。"
        }
    }

    return [pscustomobject]@{
        Configuration = "Release"
        Warnings = 0
        Errors = 0
        SpecProjects = $expectedPhase2ProjectResults.Count
        SpecsPassed = $passed
        SpecsTotal = $total
        ProjectResults = [pscustomobject] $projectResults
        HostForbiddenApiScanPassed = $true
        AgentHostDoctorPassed = $true
        GitDiffCheckPassed = $true
        BasicSecretScanPassed = $true
    }
}

function Invoke-GitDiffChecks {
    foreach ($arguments in @(
        @("-c", "safe.directory=$safeRepoRoot", "-C", $repoRoot, "diff", "--check"),
        @("-c", "safe.directory=$safeRepoRoot", "-C", $repoRoot, "diff", "--cached", "--check")
    )) {
        $output = & git @arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "最终 Git diff --check 失败：$(@($output) -join [Environment]::NewLine)"
        }
    }
}

function Assert-NoLikelySecret {
    $textExtensions = @(
        ".cs", ".csproj", ".config", ".json", ".md", ".props", ".ps1",
        ".sln", ".targets", ".xml", ".yaml", ".yml"
    )
    $secretPatterns = [ordered]@{
        "private-key" = "-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"
        "openai-token" = "\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b"
        "github-token" = "\bgh[pousr]_[A-Za-z0-9]{20,}\b"
        "aws-access-key" = "\bAKIA[0-9A-Z]{16}\b"
    }

    $gitFiles = & git -c "safe.directory=$safeRepoRoot" -C $repoRoot `
        ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) {
        throw "无法枚举 Git 文件以执行最终敏感信息扫描。"
    }

    $findingCount = 0
    foreach ($relativePath in @($gitFiles)) {
        if ([string]::IsNullOrWhiteSpace([string] $relativePath)) {
            continue
        }
        $normalized = ([string] $relativePath).Replace("\", "/")
        if ($normalized -match '(?:^|/)(?:\.git|artifacts|bin|obj|packages)(?:/|$)') {
            continue
        }
        $extension = [IO.Path]::GetExtension($normalized).ToLowerInvariant()
        if ($textExtensions -notcontains $extension) {
            continue
        }
        $absolutePath = Join-Path $repoRoot ([string] $relativePath)
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
            continue
        }
        $content = Get-Content -LiteralPath $absolutePath -Raw -Encoding UTF8
        foreach ($pattern in $secretPatterns.Values) {
            if ([regex]::IsMatch($content, [string] $pattern)) {
                $findingCount++
            }
        }
    }

    if ($findingCount -ne 0) {
        throw "最终敏感信息扫描失败；疑似命中数：$findingCount。"
    }
}

function Get-GitValue {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $output = & git -c "safe.directory=$safeRepoRoot" -C $repoRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Git 查询失败。"
    }
    return (@($output) -join "`n").Trim()
}

foreach ($path in @(
    $bootstrapVerifier,
    $authVerifier,
    $phase2Verifier,
    $orchestratorPath,
    $windowsPowerShellCommand
)) {
    Assert-FileExists -Path $path
}
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

$cadBefore = @(Get-AutoCadProcessSnapshot)
$candidateManifestBefore = Get-CandidateManifestBinding
$originalEvidenceExists = Test-Path -LiteralPath $finalEvidencePath -PathType Leaf
$originalEvidenceBytes = if ($originalEvidenceExists) {
    [IO.File]::ReadAllBytes($finalEvidencePath)
}
else {
    $null
}
$finalEvidenceWritten = $false

try {
    $bootstrapArguments = @("-Configuration", $Configuration)
    $bootstrapPs7Run = Invoke-ChildPowerShellGate `
        -Label "bootstrap-powershell7" `
        -ShellPath $pwshCommand `
        -ScriptPath $bootstrapVerifier `
        -ScriptArguments $bootstrapArguments
    $bootstrapPs51Run = Invoke-ChildPowerShellGate `
        -Label "bootstrap-windowspowershell51" `
        -ShellPath $windowsPowerShellCommand `
        -ScriptPath $bootstrapVerifier `
        -ScriptArguments $bootstrapArguments
    $bootstrapPs7 = Read-MarkedEvidence $bootstrapPs7Run "AGENT_BOOTSTRAP_EVIDENCE"
    $bootstrapPs51 = Read-MarkedEvidence $bootstrapPs51Run "AGENT_BOOTSTRAP_EVIDENCE"
    Assert-BootstrapEvidence $bootstrapPs7.Json
    Assert-BootstrapEvidence $bootstrapPs51.Json
    $bootstrapPs7Comparable = Get-ComparableEvidenceJson $bootstrapPs7.Json
    $bootstrapPs51Comparable = Get-ComparableEvidenceJson $bootstrapPs51.Json
    if ($bootstrapPs7Comparable -cne $bootstrapPs51Comparable) {
        throw "PowerShell 7 与 Windows PowerShell 5.1 bootstrap 原始证据不一致。"
    }

    $authArguments = @("-Configuration", $Configuration)
    $authPs7Run = Invoke-ChildPowerShellGate `
        -Label "auth-powershell7" `
        -ShellPath $pwshCommand `
        -ScriptPath $authVerifier `
        -ScriptArguments $authArguments
    $authPs51Run = Invoke-ChildPowerShellGate `
        -Label "auth-windowspowershell51" `
        -ShellPath $windowsPowerShellCommand `
        -ScriptPath $authVerifier `
        -ScriptArguments $authArguments
    $authPs7 = Read-MarkedEvidence $authPs7Run "AUTH_COMPAT_EVIDENCE"
    $authPs51 = Read-MarkedEvidence $authPs51Run "AUTH_COMPAT_EVIDENCE"
    Assert-AuthEvidence $authPs7.Json
    Assert-AuthEvidence $authPs51.Json
    $authPs7Comparable = Get-ComparableEvidenceJson $authPs7.Json
    $authPs51Comparable = Get-ComparableEvidenceJson $authPs51.Json
    if ($authPs7Comparable -cne $authPs51Comparable) {
        throw "PowerShell 7 与 Windows PowerShell 5.1 认证兼容原始证据不一致。"
    }

    $phase2Arguments = @("-Configuration", $Configuration)
    if (-not [string]::IsNullOrWhiteSpace($CodexExecutable)) {
        $phase2Arguments += @("-CodexExecutable", $CodexExecutable)
    }
    $phase2Ps7Run = Invoke-ChildPowerShellGate `
        -Label "phase2-powershell7" `
        -ShellPath $pwshCommand `
        -ScriptPath $phase2Verifier `
        -ScriptArguments $phase2Arguments
    $phase2Ps51Run = Invoke-ChildPowerShellGate `
        -Label "phase2-windowspowershell51" `
        -ShellPath $windowsPowerShellCommand `
        -ScriptPath $phase2Verifier `
        -ScriptArguments $phase2Arguments
    $phase2Ps7 = Assert-Phase2Log $phase2Ps7Run
    $phase2Ps51 = Assert-Phase2Log $phase2Ps51Run
    $phase2Ps7Comparable = $phase2Ps7 | ConvertTo-Json -Depth 20 -Compress
    $phase2Ps51Comparable = $phase2Ps51 | ConvertTo-Json -Depth 20 -Compress
    if ($phase2Ps7Comparable -cne $phase2Ps51Comparable) {
        throw "PowerShell 7 与 Windows PowerShell 5.1 Phase2 规范化结果不一致。"
    }

    $cadAfterGates = @(Get-AutoCadProcessSnapshot)
    Assert-AutoCadProcessSetUnchanged $cadBefore $cadAfterGates
    $candidateManifestAfterGates = Get-CandidateManifestBinding
    Assert-CandidateManifestEqual `
        -Expected $candidateManifestBefore `
        -Actual $candidateManifestAfterGates `
        -Label "完整子门禁"

    Invoke-GitDiffChecks
    Assert-NoLikelySecret

    $gitHead = Get-GitValue @("rev-parse", "HEAD")
    $gitHeadShort = Get-GitValue @("rev-parse", "--short=8", "HEAD")
    $scriptHashes = [ordered]@{
        StageOrchestrator = Get-Sha256 -Path $orchestratorPath
        BootstrapVerifier = Get-Sha256 -Path $bootstrapVerifier
        AuthenticationCompatibilityVerifier = Get-Sha256 -Path $authVerifier
        Phase2Verifier = Get-Sha256 -Path $phase2Verifier
    }

    $finalEvidence = [ordered]@{
        schemaVersion = 3
        recordedDate = [DateTimeOffset]::Now.ToString("yyyy-MM-dd")
        recordedAtLocal = [DateTimeOffset]::Now.ToString("o")
        generatedBy = "scripts/verify-autocad2016-agent-bootstrap-stage.ps1"
        source = "machine-orchestrated PowerShell 7 and Windows PowerShell 5.1 stage gates"
        scope = "autocad2016-agenthost-secure-bootstrap-stage"
        validatedBaseHead = $gitHead
        validatedBaseHeadShort = $gitHeadShort
        stageCommitBinding = "the single Git commit containing this generated evidence, AgentLauncher, AgentHost changes, Specs, all four verifier scripts, solution update, and handoff updates"
        worktreeCommittedAtRecord = $false
        autoCadLiveEvidence = $false
        candidateTreeBinding = [ordered]@{
            algorithm = "SHA-256 of sorted relative-path, byte-length, and file-SHA-256 manifest"
            evidenceFileExcludedToAvoidSelfReference = $candidateManifestBefore.EvidenceFileExcludedToAvoidSelfReference
            fileCount = $candidateManifestBefore.FileCount
            manifestSha256 = $candidateManifestBefore.ManifestSha256
            stableAcrossAllGates = $true
        }
        toolchain = [ordered]@{
            configuration = $Configuration
            dotNetSdk = $bootstrapPs7.Json.DotNetSdk
            powerShell7 = $bootstrapPs7.Json.PowerShellVersion
            windowsPowerShell = $bootstrapPs51.Json.PowerShellVersion
            crossShellBootstrapEvidenceEqual = $true
            crossShellAuthenticationEvidenceEqual = $true
            crossShellPhase2SummaryEqual = $true
        }
        verifierHashes = $scriptHashes
        rawEvidenceBindings = [ordered]@{
            bootstrap = [ordered]@{
                schemaVersion = 16
                powerShell7EvidenceSha256 = $bootstrapPs7.Sha256
                windowsPowerShell51EvidenceSha256 = $bootstrapPs51.Sha256
                normalizedEvidenceSha256 = Get-TextSha256 $bootstrapPs7Comparable
                powerShell7LogSha256 = $bootstrapPs7Run.LogSha256
                windowsPowerShell51LogSha256 = $bootstrapPs51Run.LogSha256
                normalizedCrossShellEqual = $true
            }
            authenticationCompatibility = [ordered]@{
                schemaVersion = 3
                powerShell7EvidenceSha256 = $authPs7.Sha256
                windowsPowerShell51EvidenceSha256 = $authPs51.Sha256
                normalizedEvidenceSha256 = Get-TextSha256 $authPs7Comparable
                powerShell7LogSha256 = $authPs7Run.LogSha256
                windowsPowerShell51LogSha256 = $authPs51Run.LogSha256
                normalizedCrossShellEqual = $true
            }
            phase2 = [ordered]@{
                powerShell7LogSha256 = $phase2Ps7Run.LogSha256
                windowsPowerShell51LogSha256 = $phase2Ps51Run.LogSha256
                normalizedSummarySha256 = Get-TextSha256 $phase2Ps7Comparable
                normalizedCrossShellEqual = $true
            }
            rawFilesPersistedOnlyUnderIgnoredArtifacts = $true
            rawPathsPersistedInGitEvidence = $false
        }
        reproducibility = [ordered]@{
            isolatedBuildCountPerBootstrapShell = $bootstrapPs7.Json.IsolatedBuildCount
            bootstrapCompiledInputFileCount = $bootstrapPs7.Json.CompiledInputFileCount
            bootstrapRunnableOutputTreeFileCount = $bootstrapPs7.Json.RunnableOutputTreeFileCount
            bootstrapOutputTreesBitForBitEqual = $bootstrapPs7.Json.BitForBitMatch
            bootstrapOutputTreesRecheckedAfterSpecs = $bootstrapPs7.Json.RunnableOutputTreesRecheckedAfterSpecs
            authenticationIsolatedBuildCountPerShell = $authPs7.Json.IsolatedBuildCount
            authenticationOutputsBitForBitEqual = $authPs7.Json.BitForBitMatch
        }
        artifactHashes = $bootstrapPs7.Json.ArtifactHashes
        gates = [ordered]@{
            bootstrap = [ordered]@{
                powerShell7Passed = $true
                windowsPowerShell51Passed = $true
                net45Specs = $bootstrapPs7.Json.Net45Specs
                net8Specs = $bootstrapPs7.Json.Net8Specs
                exactRequiredSpecIdSetEnforced = $true
                noResidualRelevantAgentProcesses = $true
            }
            authenticationCompatibility = [ordered]@{
                powerShell7Passed = $true
                windowsPowerShell51Passed = $true
                bridgeSpecs = $authPs7.Json.BridgeRegressionSpecs
                net45Specs = $authPs7.Json.Net45Specs
                net8Specs = $authPs7.Json.Net8Specs
                fixedVectorsAndCompiledBoundariesMatch = $true
            }
            phase2 = [ordered]@{
                powerShell7Passed = $true
                windowsPowerShell51Passed = $true
                configuration = $phase2Ps7.Configuration
                releaseBuildWarnings = $phase2Ps7.Warnings
                releaseBuildErrors = $phase2Ps7.Errors
                specProjects = $phase2Ps7.SpecProjects
                specsPassed = $phase2Ps7.SpecsPassed
                specsTotal = $phase2Ps7.SpecsTotal
                projectResults = $phase2Ps7.ProjectResults
                hostForbiddenApiScanPassed = $phase2Ps7.HostForbiddenApiScanPassed
                agentHostDoctorPassed = $phase2Ps7.AgentHostDoctorPassed
                gitDiffCheckPassed = $phase2Ps7.GitDiffCheckPassed
                basicSecretScanPassed = $phase2Ps7.BasicSecretScanPassed
            }
            finalGitDiffCheckPassed = $true
            finalBasicSecretScanPassed = $true
            autoCadProcessSetChanged = $false
            autoCadProcessBaselineCount = $cadBefore.Count
            autoCadProcessFinalCount = $cadAfterGates.Count
            autoCadStartedOrRestarted = $false
            cadCommandsSent = $false
        }
        runtimeEvidence = [ordered]@{
            realAgentHostBootstrapDoctorCompleted = $true
            realAgentHostBootstrapRepeatedFiveTimes = $true
            approvedExecutableSha256MismatchRejected = $true
            confirmationPidAndCreationTimeMismatchRejected = $true
            startupDeadlineTriggersFailClosedAbort = $true
            maximumTerminationCleanupAfterStartupDeadlineSeconds = 5
            startupDeadlineAbortAndBoundedTerminationCleanupVerified = $true
            cancellationAbortAndBoundedTerminationCleanupVerified = $true
            handleAllowlistCanaryExcluded = $true
            childClearsInheritedHandleFlags = $true
            standardErrorSeparateBoundedAndRedacted = $true
        }
        liveVerification = [ordered]@{
            externalAuthenticationKeyDelivery = $true
            dedicatedInheritedHandleTransport = $true
            bootstrapTransportConfidentialityAgainstExternalHandleDuplication = $false
            childConfirmationPidAndCreationTimeBinding = $true
            approvedExecutableSha256Enforcement = $true
            executableFileIdentityToctouRaceDynamicAttack = $false
            suspendedLaunchRaceDynamicAttack = $false
            pendingBootstrapAtomicConsumption = $false
            longRunningAgentBridgeClient = $false
            agentHostLiveBridge = $false
            host2016LiveHandshake = $false
            netLoadAttempted = $false
            netLoadVerified = $false
            cadRuntimeIntegrated = $false
        }
        limitations = @(
            "The bootstrap frame contains the session secret in plaintext; HMAC provides integrity, not confidentiality.",
            "The startup deadline triggers a fail-closed abort; proving child termination may then use at most five seconds of bounded cleanup, so termination is not claimed to finish inside the configured startup deadline itself.",
            "Restricted inherited handles were exercised, but resistance to external eligible-handle duplication was not dynamically verified.",
            "Deliberate executable replacement during the CREATE_SUSPENDED validation window and the suspended-launch race were not dynamically attacked.",
            "The bootstrap-doctor exits after authenticated confirmation; long-running IAgentBridgeClient ownership, pending-bootstrap atomic consumption, authenticated Bridge traffic, disconnect handling, and result identity binding remain unverified.",
            "No AutoCAD NETLOAD, CAD command, selection read, approval, transaction, drawing write, or automatic-save behavior was executed by this stage.",
            "This evidence does not establish complete AutoCAD 2016 support."
        )
        redaction = [ordered]@{
            rawArtifactOrLogPathsIncluded = $false
            cadTrustedDirectorySettingIncluded = $false
            userNamesIncluded = $false
            processIdsIncluded = $false
            networkPathsIncluded = $false
            localFilePathsIncluded = $false
            drawingNamesOrPathsIncluded = $false
            licenseDataIncluded = $false
            credentialsIncluded = $false
            rawStandardErrorIncluded = $false
        }
    conclusion = "The machine-orchestrated AgentHost secure-bootstrap stage passed PowerShell 7 and Windows PowerShell 5.1 bootstrap gates (57/57 net45 and net8), authentication compatibility gates (Bridge 49/49 and net45/net8 35/35), and Phase2 Release gates (0 warnings, 0 errors, 360/360 Specs, Host scan, doctor, diff, and secret scan). The configured startup deadline triggers fail-closed abort followed by no more than five seconds of bounded termination cleanup. Process-tree cleanup, protected per-session workspace ACL/lifecycle, junction rejection, active-lease preservation, expired crash recovery, validated graceful-stop configuration, authoritative process-count, Job-memory, Job-user-time, wall-clock, and combined resource terminal attribution, 500 consecutive service start/stop cycles, and nested Job assignment were verified on the current Windows runtime; the required enterprise nested-Job policy matrix remains unverified. AutoCAD was not started, restarted, or commanded. Long-running Bridge, Host.2016 integration, and complete AutoCAD 2016 support remain unverified."
    }

    $finalJson = $finalEvidence | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText(
        $finalEvidencePath,
        $finalJson + [Environment]::NewLine,
        (New-Object Text.UTF8Encoding($false)))
    $finalEvidenceWritten = $true

    # These checks deliberately run after the exact Git evidence file is generated.
    Invoke-GitDiffChecks
    Assert-NoLikelySecret
    $cadAfterFinalChecks = @(Get-AutoCadProcessSnapshot)
    Assert-AutoCadProcessSetUnchanged $cadBefore $cadAfterFinalChecks
    $candidateManifestAfterFinalChecks = Get-CandidateManifestBinding
    Assert-CandidateManifestEqual `
        -Expected $candidateManifestBefore `
        -Actual $candidateManifestAfterFinalChecks `
        -Label "最终 evidence 生成后检查"

    Write-Host "`nAgentHost 安全引导阶段最终编排门禁通过。" -ForegroundColor Green
    Write-Host ("AGENT_BOOTSTRAP_STAGE_EVIDENCE=" + $finalEvidencePath)
    Write-Host ("AGENT_BOOTSTRAP_STAGE_EVIDENCE_SHA256=" + (Get-Sha256 $finalEvidencePath))
}
catch {
    if ($finalEvidenceWritten) {
        if ($originalEvidenceExists) {
            [IO.File]::WriteAllBytes($finalEvidencePath, $originalEvidenceBytes)
        }
        elseif (Test-Path -LiteralPath $finalEvidencePath) {
            Remove-Item -LiteralPath $finalEvidencePath -Force
        }
    }
    throw
}
