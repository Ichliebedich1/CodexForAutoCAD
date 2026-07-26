[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string] $Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$artifactsRoot = $buildSafety.ArtifactRoot
$verificationScriptPath = $MyInvocation.MyCommand.Path
$safeRepoRoot = $repoRoot.Replace("\", "/")
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$solutionPath = Join-Path $repoRoot "Codex.AutoCAD.sln"
$launcherProject = Join-Path $repoRoot "src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj"
$agentHostProject = Join-Path $repoRoot "src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj"
$specProject = Join-Path $repoRoot "tests\Codex.AutoCAD.AgentLauncher.Specs\Codex.AutoCAD.AgentLauncher.Specs.csproj"
$fakeProject = Join-Path $repoRoot "tests\Codex.AutoCAD.AgentLauncher.FakeAgentHost\Codex.AutoCAD.AgentLauncher.FakeAgentHost.csproj"
$agentHostSource = Join-Path $repoRoot "src\Codex.AutoCAD.AgentHost\Program.cs"
$ipcBootstrapSource = Join-Path $repoRoot "src\Codex.AutoCAD.Ipc\AgentBootstrap.cs"
$globalJsonPath = Join-Path $repoRoot "global.json"
$nugetConfig = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\NuGet.Config"
$offlinePackage = Join-Path $repoRoot "third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg"
$expectedSdk = "8.0.319"
$runId = [Guid]::NewGuid().ToString("N")
$stageRoot = Join-Path $artifactsRoot ("autocad2016-agent-bootstrap-" + $runId)
$evidencePath = Join-Path $stageRoot "verification.json"
$requiredSpecIds = @(
    "REAL_AGENTHOST_SUCCESS",
    "REAL_AGENTHOST_REPEAT_5",
    "RESTRICTED_TOKEN_PRIMITIVES_FAIL_CLOSED",
    "RESTRICTED_TOKEN_BOOTSTRAP_PROBE_PORTABLE",
    "PROCESS_POLICY_BLOCK_CLASSIFIED",
    "JOB_RESOURCE_LIMITS_APPLIED",
    "JOB_RESOURCE_LIMITS_INVALID",
    "RESOURCE_LIMIT_ERROR_CODES_STABLE",
    "CREDENTIAL_BROKER_CONFIGURATION_FAILS_CLOSED",
    "CREDENTIAL_MANAGER_READ_FAILS_CLOSED",
    "CREDENTIAL_SECRET_DISPOSE_ZEROES",
    "CREDENTIAL_DELIVERY_DISABLED",
    "CREDENTIAL_DELIVERY_AUTHENTICATED",
    "CREDENTIAL_DELIVERY_ATTACKS_FAIL_CLOSED",
    "NESTED_JOB_ASSIGNMENT_COMPATIBLE",
    "NESTED_JOB_ASSIGNMENT_FAILURE_CLASSIFIED",
    "EXPERIMENTAL_IDENTITY_NOT_PUBLIC",
    "JOB_USER_TIME_TERMINATES_TREE",
    "JOB_PROCESS_LIMIT_STRUCTURED",
    "JOB_MEMORY_LIMIT_STRUCTURED",
    "JOB_COMBINED_LIMIT_SINGLE_TERMINAL",
    "SESSION_RUNTIME_TERMINATES_TREE",
    "SESSION_RUNTIME_RETRIES_CLEANUP",
    "SESSION_STOP_PREVENTS_RUNTIME_EXPIRY",
    "SERVICE_STOP_ALLOWS_GRACEFUL_EXIT",
    "SERVICE_STOP_USES_CONFIGURED_GRACE",
    "SESSION_WORKSPACE_PROTECTED_LAYOUT",
    "SESSION_WORKSPACE_DUPLICATE_REJECTED",
    "SESSION_WORKSPACE_INVALID_ROOTS_REJECTED",
    "SESSION_WORKSPACE_REPARSE_ROOT_REJECTED",
    "SESSION_WORKSPACE_ACTIVE_LEASE_PRESERVED",
    "SESSION_WORKSPACE_CRASH_RECOVERY",
    "SERVICE_SESSION_WORKSPACE_REMOVED",
    "SERVICE_START_FAILURE_WORKSPACE_REMOVED",
    "SERVICE_WORKSPACE_CLEANUP_CAN_RETRY",
    "SERVICE_START_STOP_REPEAT_500",
    "SERVICE_STOP_KILLS_PROCESS_TREE",
    "AGENTHOST_UNEXPECTED_EXIT_KILLS_PROCESS_TREE",
    "OWNER_EXIT_KILLS_PROCESS_TREE",
    "INVALID_EXECUTABLE_PATHS",
    "EXECUTABLE_SHA256_MISMATCH",
    "TIMEOUT_TERMINATES_UNCONFIRMED",
    "CONFIRMATION_THEN_HANG_TIMEOUT",
    "CALLER_THREAD_NONBLOCKING",
    "CANCELLATION_TERMINATES_UNCONFIRMED",
    "EARLY_EXIT_REJECTED",
    "MALFORMED_CONFIRMATION_REJECTED",
    "IDENTITY_MISMATCH_REJECTED",
    "BOOTSTRAP_FAILURE_DIAGNOSTICS_SANITIZED",
    "TRAILING_DUPLICATE_REJECTED",
    "CHILD_CLEARS_INHERITANCE",
    "HANDLE_ALLOWLIST_CANARY",
    "STDERR_BOUNDED",
    "SERVICE_STOP_RETRIES_TERMINATION",
    "SERVICE_STOP_RETRIES_THROWN_TERMINATION",
    "SERVICE_STOP_PROCESS_DISPOSE_CAN_RETRY",
    "SERVICE_STOP_ABORT_IO_CAN_RETRY",
    "SERVICE_STOP_THROWN_ABORT_IO_CAN_RETRY",
    "SERVICE_STOP_STDERR_CAN_RETRY",
    "SERVICE_STOP_FAULTED_STDERR_IS_SETTLED",
    "SERVICE_STOP_RETRY_DOES_NOT_POISON_START",
    "SERVICE_DISPOSE_FAILURE_CAN_RETRY",
    "SERVICE_STOP_CONCURRENT_CALLERS",
    "SERVICE_STOP_CONCURRENT_FAILURE_SHARED",
    "SESSION_RUNTIME_FAILURE_POISONS_START"
)

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

function Invoke-Captured {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [string[]] $Arguments = @(),
        [Parameter(Mandatory = $true)][string] $Description
    )

    Write-Host ("`n==> " + $Description) -ForegroundColor Cyan
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $raw = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    $lines = @($raw | ForEach-Object { [string] $_ })
    foreach ($line in $lines) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        throw "$Description 失败，退出码：$exitCode"
    }

    return $lines
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "缺少预期文件：$Path"
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-SourceSnapshot {
    param([Parameter(Mandatory = $true)][string[]] $Paths)

    $snapshot = [ordered]@{}
    foreach ($path in $Paths) {
        $absolutePath = [IO.Path]::GetFullPath($path)
        $relativePath = $absolutePath.Substring($repoRoot.Length + 1).Replace("\", "/")
        $snapshot[$relativePath] = Get-Sha256 -Path $absolutePath
    }

    return $snapshot
}

function Get-CompiledInputPaths {
    $paths = @(
        foreach ($root in @(
            (Join-Path $repoRoot "src"),
            (Join-Path $repoRoot "tests")
        )) {
            foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File)) {
                if ($file.FullName -match '[\\/](bin|obj|artifacts)[\\/]') {
                    continue
                }

                $file.FullName
            }
        }

        Get-ChildItem -LiteralPath $repoRoot -File | ForEach-Object { $_.FullName }
        $globalJsonPath
        $solutionPath
        $nugetConfig
        $offlinePackage
        $verificationScriptPath
    )

    return @($paths | Sort-Object -Unique)
}

function Get-LocalBuildArtifactSnapshot {
    $snapshot = [ordered]@{}
    foreach ($root in @(
        (Join-Path $repoRoot "src"),
        (Join-Path $repoRoot "tests")
    )) {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File)) {
            if ($file.FullName -notmatch '[\\/](bin|obj)[\\/]') {
                continue
            }

            $relativePath = $file.FullName.Substring($repoRoot.Length + 1).Replace("\", "/")
            $snapshot[$relativePath] = [ordered]@{
                Length = $file.Length
                LastWriteUtcTicks = $file.LastWriteTimeUtc.Ticks
                Sha256 = Get-Sha256 -Path $file.FullName
            }
        }
    }

    return $snapshot
}

function Get-RunnableOutputSnapshot {
    param([Parameter(Mandatory = $true)][string] $Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "缺少完整可运行输出树：$Root"
    }

    $absoluteRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $snapshot = [ordered]@{}
    foreach ($file in @(Get-ChildItem -LiteralPath $absoluteRoot -Recurse -File | Sort-Object FullName)) {
        $relativePath = $file.FullName.Substring($absoluteRoot.Length + 1).Replace("\", "/")
        $snapshot[$relativePath] = [ordered]@{
            Length = $file.Length
            Sha256 = Get-Sha256 -Path $file.FullName
        }
    }

    if ($snapshot.Count -eq 0) {
        throw "完整可运行输出树为空：$Root"
    }

    return $snapshot
}

function Assert-RunnableOutputTreesEqual {
    param(
        [Parameter(Mandatory = $true)] $Left,
        [Parameter(Mandatory = $true)] $Right
    )

    $leftPaths = @($Left.Keys)
    $rightPaths = @($Right.Keys)
    $pathDifference = @(Compare-Object -ReferenceObject $leftPaths -DifferenceObject $rightPaths)
    if ($pathDifference.Count -ne 0) {
        $detail = @($pathDifference | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join "; "
        throw "隔离双构建的完整可运行输出树文件集合不一致：$detail"
    }

    foreach ($relativePath in $leftPaths) {
        $leftFile = $Left[$relativePath]
        $rightFile = $Right[$relativePath]
        if ($leftFile.Length -ne $rightFile.Length -or $leftFile.Sha256 -cne $rightFile.Sha256) {
            throw "隔离双构建的完整可运行输出树内容不一致：$relativePath；$($leftFile.Sha256) != $($rightFile.Sha256)"
        }
    }
}

function Assert-SnapshotsEqual {
    param(
        [Parameter(Mandatory = $true)] $Before,
        [Parameter(Mandatory = $true)] $After,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if (($Before | ConvertTo-Json -Depth 10 -Compress) -cne
        ($After | ConvertTo-Json -Depth 10 -Compress)) {
        throw "$Label 在隔离验证期间发生变化。"
    }
}

function Assert-SolutionMembership {
    $requiredProjects = @(
        "src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj",
        "src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj",
        "tests\Codex.AutoCAD.AgentLauncher.Specs\Codex.AutoCAD.AgentLauncher.Specs.csproj",
        "tests\Codex.AutoCAD.AgentLauncher.FakeAgentHost\Codex.AutoCAD.AgentLauncher.FakeAgentHost.csproj"
    )
    $solutionText = Get-Content -LiteralPath $solutionPath -Raw -Encoding UTF8
    foreach ($project in $requiredProjects) {
        $escaped = [regex]::Escape($project)
        if ($solutionText -notmatch $escaped) {
            throw "解决方案缺少 Agent bootstrap 项目：$project"
        }
    }
}

function Assert-SourceBoundary {
    $launcherSources = @(
        Get-ChildItem -LiteralPath (Split-Path -Parent $launcherProject) -Filter "*.cs" -File |
            Sort-Object FullName
    )
    $launcherText = @(
        $launcherSources |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }
    ) -join "`n"
    $bootstrapLauncherText = @(
        $launcherSources |
            Where-Object { $_.Name -cne "AgentCredentialNamedPipeChannel.cs" } |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }
    ) -join "`n"
    $credentialChannelPath = Join-Path `
        (Split-Path -Parent $launcherProject) `
        "AgentCredentialNamedPipeChannel.cs"
    $credentialChannelText = Get-Content `
        -LiteralPath $credentialChannelPath `
        -Raw `
        -Encoding UTF8
    $agentHostText = Get-Content -LiteralPath $agentHostSource -Raw -Encoding UTF8
    $combined = $launcherText + "`n" + $agentHostText

    $required = [ordered]@{
        "精确继承句柄 allowlist" = "ProcThreadAttributeHandleList"
        "扩展启动信息" = "ExtendedStartupInfoPresent"
        "stdin 承载 bootstrap" = "hStdInput = childBootstrapRead"
        "stdout 承载 confirmation" = "hStdOutput = childConfirmationWrite"
        "stderr 独立捕获" = "hStdError = childStandardErrorWrite"
        "子端清除继承位" = "ClearInheritFlag(safeHandle)"
        "子端清除stderr继承位" = "ClearStandardErrorInheritance"
        "认证键与frame同一机密句柄" = "WriteSingleBootstrapPacket"
        "子端读取固定认证键" = "ReadSingleBootstrapPacket"
        "确认帧 EOF" = "EnsureEndOfStream"
        "认证后不可变快照" = "SnapshotEnvelope"
        "PID创建时间绑定" = "ProcessCreationFileTime"
        "进程映像绑定" = "QueryFullProcessImageName"
        "卷和文件ID绑定" = "GetFileIdentity"
        "批准SHA-256绑定" = "ExpectedSha256"
        "挂起创建" = "CreateSuspended"
        "硬终止" = "TerminateProcess"
        "进程树Job Object" = "JobObjectLimitKillOnJobClose"
        "进程树数量上限" = "JobObjectLimitActiveProcess"
        "进程树内存上限" = "JobObjectLimitJobMemory"
        "进程树累计用户时间上限" = "JobObjectLimitJobTime"
        "进程树CPU硬上限" = "JobObjectCpuRateControlHardCap"
        "会话墙钟截止与自动清理重试" = "AutomaticCleanupAttempts"
        "AgentHost异常退出监视" = "WatchForUnexpectedProcessExit"
        "停止宽限公共配置" = "GracefulStopTimeout"
        "停止宽限边界校验" = "GetValidatedGracefulStopTimeout"
        "停止前自然退出宽限" = "_gracefulExitWaitMilliseconds"
        "进程树Job分配" = "AssignProcessToJobObject"
        "进程退出等待" = "WaitForSingleObject"
        "单调绝对截止" = "deadlineTimestamp"
        "专用截止监督线程" = "RunSupervisor"
        "启动worker离开调用线程" = "Task.Run(() => RunWorkerAsync"
        "启动门可中止等待" = "LaunchGate.Wait(controller.AbortToken)"
        "终止失败毒化后续启动" = "AgentBootstrapLateFailureRegistry"
        "仅本地驱动路径" = "absolute local-drive path"
        "仅固定本地磁盘" = "DriveType.Fixed"
        "受保护会话工作区DACL" = "ConvertStringSecurityDescriptorToSecurityDescriptor"
        "会话目录拒绝删除共享" = "DeleteAccess"
        "清理不跟随重解析点" = "DeleteTreeWithoutFollowingReparsePoints"
        "过期lease有界枚举" = "MaximumExpiredCleanupCandidates"
        "活动lease独占探测" = "FileShare.None"
        "AgentHost标准输入领取" = "OpenStandardInput()"
        "AgentHost标准输出确认" = "OpenStandardOutput()"
    }
    foreach ($entry in $required.GetEnumerator()) {
        if ($combined.IndexOf([string]$entry.Value, [StringComparison]::Ordinal) -lt 0) {
            throw "Agent bootstrap 源码缺少门禁：$($entry.Key)"
        }
    }

    $forbidden = [ordered]@{
        "命令行 bootstrap handle" = "--bootstrap-handle"
        "命令行 confirmation handle" = "--confirmation-handle"
        "原始字符串句柄入口" = "OpenReadHandle"
        "原始字符串写句柄入口" = "OpenWriteHandle"
        "环境变量交付秘密" = "SetEnvironmentVariable"
        "内存映射交付 bootstrap" = "MemoryMappedFile"
        "ShellExecute" = "ShellExecute"
        "AutoCAD 托管 API" = "Autodesk.AutoCAD"
        "CAD 保存 API" = "SaveAs("
    }
    foreach ($entry in $forbidden.GetEnumerator()) {
        if ($combined.IndexOf([string]$entry.Value, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Agent bootstrap 源码出现禁止边界：$($entry.Key)"
        }
    }

    if ($bootstrapLauncherText.IndexOf("NamedPipe", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "冻结 bootstrap 核心仍禁止命名管道；仅凭据通道文件可使用认证命名管道。"
    }
    foreach ($requiredCredentialBoundary in @(
        "PipeOptions.CurrentUserOnly",
        "WindowsIdentity.GetCurrent",
        "PipeSecurity",
        "PipeAccessRights.ReadWrite",
        "PipeDirection.Out",
        "PipeDirection.In",
        "MaximumCredentialBytes",
        "CreateConfirmationOutboundAuthenticator",
        "CreateConfirmationInboundGuard"
    )) {
        if ($combined.IndexOf($requiredCredentialBoundary, [StringComparison]::Ordinal) -lt 0) {
            throw "凭据认证通道缺少边界：$requiredCredentialBoundary"
        }
    }

    if ($launcherText -notmatch 'AgentHostBootstrapCommand\.Doctor\s*=>\s*" bootstrap-doctor"' -or
        $launcherText -notmatch 'AgentHostBootstrapCommand\.Serve\s*=>\s*" bootstrap-serve"' -or
        $launcherText -notmatch '_\s*=>\s*throw new AgentBootstrapLaunchException') {
        throw "CreateProcess 命令行必须只允许固定 bootstrap-doctor/bootstrap-serve 枚举值。"
    }
    $singleArgumentGuards = [regex]::Matches(
        $agentHostText,
        'args\.Length\s*!=\s*1').Count
    if ($singleArgumentGuards -lt 2 -or
        $agentHostText -notmatch 'bootstrap-doctor accepts no command-line bootstrap material' -or
        $agentHostText -notmatch 'bootstrap-serve accepts no command-line bootstrap material') {
        throw "AgentHost 两种 bootstrap 模式都必须拒绝任何附加命令行材料。"
    }

    return [ordered]@{
        AuthenticationKeyAndFrameUseInheritedStandardInput = $true
        ConfirmationUsesInheritedStandardOutput = $true
        StandardErrorUsesSeparateHandle = $true
        CommandLineBootstrapMaterialTokenFound = $false
        EnvironmentBootstrapDeliveryApiTokenFound = $false
        HandleAllowlistApiTokenFound = $true
        ChildClearInheritedFlagTokenFound = $true
        ConfirmationEofCheckTokenFound = $true
        ConfirmationSnapshotTokenFound = $true
        ProcessCreationTimeBindingTokenFound = $true
        ProcessImageQueryTokenFound = $true
        ExecutableVolumeAndFileIdTokenFound = $true
        ExecutableSha256TokenFound = $true
        CreateSuspendedTokenFound = $true
        ProcessTreeJobObjectTokenFound = $true
        ProcessTreeActiveProcessLimitTokenFound = $true
        ProcessTreeMemoryLimitTokenFound = $true
        ProcessTreeJobUserTimeLimitTokenFound = $true
        ProcessTreeCpuHardCapTokenFound = $true
        SessionWallClockDeadlineTokenFound = $true
        UnexpectedAgentHostExitWatcherTokenFound = $true
        GracefulExitWaitTokenFound = $true
        ProcessTreeJobAssignmentTokenFound = $true
        LocalFixedDriveChecksFound = $true
        MonotonicDeadlineTokenFound = $true
        DedicatedSupervisorTokenFound = $true
        CallerOffloadTokenFound = $true
        AbortableLaunchGateTokenFound = $true
        LateFailurePoisonTokenFound = $true
        ProtectedSessionWorkspaceDaclTokenFound = $true
        SessionWorkspaceNoDeleteShareTokenFound = $true
        ReparseSafeWorkspaceCleanupTokenFound = $true
        ExpiredWorkspaceCleanupBoundTokenFound = $true
        ActiveWorkspaceLeaseProbeTokenFound = $true
    }
}

function Invoke-IsolatedBuild {
    param([Parameter(Mandatory = $true)][string] $Name)

    $buildRoot = Join-Path $stageRoot $Name
    $outputRoot = Join-Path $buildRoot "out"
    $packageRoot = Join-Path $buildRoot "packages"
    $cliHome = Join-Path $buildRoot "dotnet-home"
    $net45ReferencePath = Join-Path `
        $packageRoot `
        "microsoft.netframework.referenceassemblies.net45\1.0.3\build\.NETFramework\v4.5"
    New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null

    $previousPathMap = $env:PathMap
    $previousCliHome = $env:DOTNET_CLI_HOME
    $previousAddGlobalToolsToPath = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
    try {
        $env:PathMap = ($buildRoot + "=/_build/," + $repoRoot + "=/_/")
        $env:DOTNET_CLI_HOME = $cliHome
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"

        foreach ($restore in @(
            [pscustomobject]@{ Project = $agentHostProject; Extra = @() },
            [pscustomobject]@{ Project = $fakeProject; Extra = @() },
            [pscustomobject]@{ Project = $specProject; Extra = @("-p:EnableAutoCad2016=true") }
        )) {
            $arguments = @(
                "restore", $restore.Project,
                "--configfile", $nugetConfig,
                "--packages", $packageRoot,
                "--force", "--no-cache",
                "-p:UseArtifactsOutput=true",
                ("-p:ArtifactsPath=" + $outputRoot)
            ) + @($restore.Extra)
            Invoke-Captured -FilePath $dotnetCommand -Arguments $arguments `
                -Description ("隔离恢复 " + $Name + " " + (Split-Path -Leaf $restore.Project)) | Out-Null
        }

        foreach ($build in @(
            [pscustomobject]@{ Project = $agentHostProject; Extra = @() },
            [pscustomobject]@{ Project = $fakeProject; Extra = @() },
            [pscustomobject]@{ Project = $specProject; Extra = @("-p:EnableAutoCad2016=true") }
        )) {
            $arguments = @(
                "build", $build.Project,
                "--configuration", $Configuration,
                "--nologo", "--disable-build-servers", "--no-restore",
                "-m:1", "-p:UseSharedCompilation=false",
                "-p:UseArtifactsOutput=true",
                ("-p:ArtifactsPath=" + $outputRoot),
                "-p:ContinuousIntegrationBuild=true",
                ("-p:FrameworkPathOverride=" + $net45ReferencePath)
            ) + @($build.Extra)
            Invoke-Captured -FilePath $dotnetCommand -Arguments $arguments `
                -Description ("隔离构建 " + $Name + " " + (Split-Path -Leaf $build.Project)) | Out-Null
        }
    }
    finally {
        $env:PathMap = $previousPathMap
        $env:DOTNET_CLI_HOME = $previousCliHome
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $previousAddGlobalToolsToPath
    }

    return [pscustomobject]@{
        Root = $buildRoot
        OutputRoot = $outputRoot
        RunnableRoot = Join-Path $outputRoot "bin"
        Net45Launcher = Join-Path $outputRoot "bin\Codex.AutoCAD.AgentLauncher\release_net45\Codex.AutoCAD.AgentLauncher.dll"
        Net8Launcher = Join-Path $outputRoot "bin\Codex.AutoCAD.AgentLauncher\release_net8.0\Codex.AutoCAD.AgentLauncher.dll"
        AgentHostDll = Join-Path $outputRoot "bin\Codex.AutoCAD.AgentHost\release_win-x64\Codex.AutoCAD.AgentHost.dll"
        AgentHostExe = Join-Path $outputRoot "bin\Codex.AutoCAD.AgentHost\release_win-x64\Codex.AutoCAD.AgentHost.exe"
        FakeHostDll = Join-Path $outputRoot "bin\Codex.AutoCAD.AgentLauncher.FakeAgentHost\release_win-x64\Codex.AutoCAD.AgentLauncher.FakeAgentHost.dll"
        FakeHostExe = Join-Path $outputRoot "bin\Codex.AutoCAD.AgentLauncher.FakeAgentHost\release_win-x64\Codex.AutoCAD.AgentLauncher.FakeAgentHost.exe"
        Net45Specs = Join-Path $outputRoot "bin\Codex.AutoCAD.AgentLauncher.Specs\release_net45\Codex.AutoCAD.AgentLauncher.Specs.exe"
        Net8Specs = Join-Path $outputRoot "bin\Codex.AutoCAD.AgentLauncher.Specs\release_net8.0\Codex.AutoCAD.AgentLauncher.Specs.dll"
    }
}

function Assert-SpecOutput {
    param(
        [Parameter(Mandatory = $true)][string[]] $Lines,
        [Parameter(Mandatory = $true)][string] $RuntimeLabel
    )

    $summary = @($Lines | Where-Object { $_ -match '^\s*\d+/\d+ specs passed\s*$' })
    $passes = @($Lines | Where-Object { $_ -match '^PASS\s+' })
    $failures = @($Lines | Where-Object { $_ -match '^FAIL\s+' })
    if ($summary.Count -ne 1) {
        throw "$RuntimeLabel 必须且只能输出一条 Specs 汇总；实际：$($summary.Count)。"
    }

    $summaryMatch = [regex]::Match($summary[0], '^\s*(\d+)/(\d+) specs passed\s*$')
    $passedCount = [int]$summaryMatch.Groups[1].Value
    $totalCount = [int]$summaryMatch.Groups[2].Value
    $parsedPasses = @(
        foreach ($line in $passes) {
            $match = [regex]::Match($line, '^PASS\s+([A-Z][A-Z0-9_]*)\s+(.+)$')
            if (-not $match.Success) {
                throw "$RuntimeLabel Specs PASS 行格式无效：$line"
            }

            [pscustomobject]@{
                Id = $match.Groups[1].Value
                Name = $match.Groups[2].Value.Trim()
            }
        }
    )
    $passIds = @($parsedPasses | ForEach-Object { $_.Id })
    $duplicateIds = @(
        $passIds | Group-Object | Where-Object { $_.Count -ne 1 } |
            ForEach-Object { $_.Name }
    )
    $missingIds = @(
        $requiredSpecIds | Where-Object { -not ($passIds -ccontains $_) }
    )
    $unknownIds = @(
        $passIds | Where-Object { -not ($requiredSpecIds -ccontains $_) } |
            Sort-Object -Unique
    )
    if ($totalCount -ne $requiredSpecIds.Count -or
        $passedCount -ne $totalCount -or
        $passes.Count -ne $totalCount -or
        $parsedPasses.Count -ne $passes.Count -or
        $duplicateIds.Count -ne 0 -or
        $missingIds.Count -ne 0 -or
        $unknownIds.Count -ne 0 -or
        $failures.Count -ne 0) {
        throw "$RuntimeLabel Specs 固定集合门禁失败；summary=$passedCount/$totalCount，required=$($requiredSpecIds.Count)，PASS=$($passes.Count)，duplicate=$($duplicateIds -join ','), missing=$($missingIds -join ','), unknown=$($unknownIds -join ','), FAIL=$($failures.Count)。"
    }

    return [pscustomobject]@{
        Passed = $passedCount
        Total = $totalCount
        Ids = @($passIds | Sort-Object)
    }
}

function Test-SpecPassed {
    param(
        [Parameter(Mandatory = $true)] $SpecResult,
        [Parameter(Mandatory = $true)][string] $Id
    )

    return @($SpecResult.Ids | Where-Object { $_ -ceq $Id }).Count -eq 1
}

function Get-ProbeOutcome {
    param(
        [Parameter(Mandatory = $true)][string[]] $Lines,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string[]] $AllowedValues,
        [Parameter(Mandatory = $true)][string] $RuntimeLabel
    )

    $prefix = $Name + "="
    $matches = @($Lines | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) {
        throw "$RuntimeLabel 必须且只能输出一个 $Name；实际：$($matches.Count)。"
    }

    $outcome = $matches[0].Substring($prefix.Length)
    if (-not ($AllowedValues -ccontains $outcome)) {
        throw "$RuntimeLabel 输出了未知 $Name：$outcome。"
    }

    return $outcome
}

function Get-ProcessSnapshot {
    return @(
        Get-Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.ProcessName -ieq "Codex.AutoCAD.AgentHost" -or
                $_.ProcessName -ieq "Codex.AutoCAD.AgentLauncher.FakeAgentHost" -or
                $_.ProcessName -like "CodexLauncherFake-*"
            } |
            Select-Object @{Name="Name";Expression={$_.ProcessName}}, Id
    ) | Sort-Object Name, Id
}

$sourcePaths = Get-CompiledInputPaths

$previousNoLogo = $env:DOTNET_NOLOGO
try {
    $env:DOTNET_NOLOGO = "1"
    $sourceBefore = Get-SourceSnapshot -Paths $sourcePaths
    $localBuildArtifactsBefore = Get-LocalBuildArtifactSnapshot
    $processBefore = @(Get-ProcessSnapshot)
    if ($processBefore.Count -ne 0) {
        $detail = @($processBefore | ForEach-Object { "$($_.Name):$($_.Id)" }) -join ", "
        throw "验证前必须没有相关 AgentHost/FakeAgentHost 进程；实际：$detail"
    }
    $cadBefore = @(Get-Process -Name acad -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id | Sort-Object)

    $actualSdk = (& $dotnetCommand --version).Trim()
    if ($actualSdk -cne $expectedSdk) {
        throw "需要 .NET SDK $expectedSdk，实际：$actualSdk"
    }
    if ((Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json).sdk.version -cne $expectedSdk) {
        throw "global.json 未固定到 $expectedSdk。"
    }

    Invoke-Captured -FilePath $dotnetCommand -Arguments @("nuget", "verify", $offlinePackage, "--all") `
        -Description "验证离线 net45 引用包签名" | Out-Null
    Assert-SolutionMembership
    $staticObservations = Assert-SourceBoundary

    Invoke-Captured -FilePath "git" -Arguments @(
        "-c", ("safe.directory=" + $safeRepoRoot), "-C", $repoRoot,
        "diff", "--exit-code", "HEAD", "--", "src/Codex.AutoCAD.Ipc/AgentBootstrap.cs"
    ) -Description "确认冻结 bootstrap 原语未被 launcher 阶段修改" | Out-Null

    $buildA = Invoke-IsolatedBuild -Name "build-a"
    $buildB = Invoke-IsolatedBuild -Name "build-b"
    $buildATree = Get-RunnableOutputSnapshot -Root $buildA.RunnableRoot
    $buildBTree = Get-RunnableOutputSnapshot -Root $buildB.RunnableRoot
    Assert-RunnableOutputTreesEqual -Left $buildATree -Right $buildBTree
    $artifacts = @(
        "Net45Launcher", "Net8Launcher", "AgentHostDll", "AgentHostExe",
        "FakeHostDll", "FakeHostExe", "Net45Specs", "Net8Specs"
    )
    $hashes = [ordered]@{}
    foreach ($artifact in $artifacts) {
        $left = [string]$buildA.$artifact
        $right = [string]$buildB.$artifact
        $leftHash = Get-Sha256 -Path $left
        $rightHash = Get-Sha256 -Path $right
        if ($leftHash -cne $rightHash) {
            throw "隔离双构建不一致：$artifact，$leftHash != $rightHash"
        }
        $hashes[$artifact] = $leftHash
    }

    $specArguments = @(
        "--agent-host", $buildA.AgentHostExe,
        "--fake-agent-host", $buildA.FakeHostExe
    )
    $net8Output = Invoke-Captured -FilePath $dotnetCommand `
        -Arguments (@($buildA.Net8Specs) + $specArguments) `
        -Description "运行 net8 AgentHost bootstrap Specs"
    $net8Specs = Assert-SpecOutput -Lines $net8Output -RuntimeLabel "net8"
    $net45Output = Invoke-Captured -FilePath $buildA.Net45Specs `
        -Arguments $specArguments `
        -Description "运行 net45 AgentHost bootstrap Specs"
    $net45Specs = Assert-SpecOutput -Lines $net45Output -RuntimeLabel "net45"
    $primitiveOutcomes = @("available", "process_isolation_failed")
    $bootstrapOutcomes = @(
        "authenticated_success",
        "process_isolation_failed",
        "child_exited"
    )
    $net8RestrictedPrimitiveOutcome = Get-ProbeOutcome `
        -Lines $net8Output `
        -Name "RESTRICTED_TOKEN_PRIMITIVES_OUTCOME" `
        -AllowedValues $primitiveOutcomes `
        -RuntimeLabel "net8"
    $net45RestrictedPrimitiveOutcome = Get-ProbeOutcome `
        -Lines $net45Output `
        -Name "RESTRICTED_TOKEN_PRIMITIVES_OUTCOME" `
        -AllowedValues $primitiveOutcomes `
        -RuntimeLabel "net45"
    $net8RestrictedBootstrapOutcome = Get-ProbeOutcome `
        -Lines $net8Output `
        -Name "RESTRICTED_TOKEN_BOOTSTRAP_OUTCOME" `
        -AllowedValues $bootstrapOutcomes `
        -RuntimeLabel "net8"
    $net45RestrictedBootstrapOutcome = Get-ProbeOutcome `
        -Lines $net45Output `
        -Name "RESTRICTED_TOKEN_BOOTSTRAP_OUTCOME" `
        -AllowedValues $bootstrapOutcomes `
        -RuntimeLabel "net45"
    if ($net45Specs.Total -ne $net8Specs.Total -or
        (($net45Specs.Ids | ConvertTo-Json -Compress) -cne
         ($net8Specs.Ids | ConvertTo-Json -Compress))) {
        throw "net45 与 net8 Specs 集合不一致；net45=$($net45Specs.Total)，net8=$($net8Specs.Total)。"
    }

    $buildATreeAfterSpecs = Get-RunnableOutputSnapshot -Root $buildA.RunnableRoot
    $buildBTreeAfterSpecs = Get-RunnableOutputSnapshot -Root $buildB.RunnableRoot
    Assert-SnapshotsEqual -Before $buildATree -After $buildATreeAfterSpecs `
        -Label "build-a 完整可运行输出树"
    Assert-SnapshotsEqual -Before $buildBTree -After $buildBTreeAfterSpecs `
        -Label "build-b 完整可运行输出树"
    Assert-RunnableOutputTreesEqual -Left $buildATreeAfterSpecs -Right $buildBTreeAfterSpecs
    foreach ($artifact in $artifacts) {
        $actualHash = Get-Sha256 -Path ([string]$buildA.$artifact)
        if ($actualHash -cne $hashes[$artifact]) {
            throw "Specs 执行后产物发生变化：$artifact；$actualHash != $($hashes[$artifact])"
        }
    }

    Invoke-Captured -FilePath "git" -Arguments @(
        "-c", ("safe.directory=" + $safeRepoRoot), "-C", $repoRoot, "diff", "--check"
    ) -Description "检查未暂存差异格式" | Out-Null
    Invoke-Captured -FilePath "git" -Arguments @(
        "-c", ("safe.directory=" + $safeRepoRoot), "-C", $repoRoot, "diff", "--cached", "--check"
    ) -Description "检查已暂存差异格式" | Out-Null

    Assert-SnapshotsEqual -Before $sourceBefore -After (Get-SourceSnapshot -Paths $sourcePaths) `
        -Label "Agent bootstrap 源码/项目输入"
    Assert-SnapshotsEqual -Before $localBuildArtifactsBefore -After (Get-LocalBuildArtifactSnapshot) `
        -Label "源码树本地 bin/obj"

    $processAfter = @(Get-ProcessSnapshot)
    if ($processAfter.Count -ne 0) {
        $detail = @($processAfter | ForEach-Object { "$($_.Name):$($_.Id)" }) -join ", "
        throw "验证后存在 AgentHost/FakeAgentHost 残留进程：$detail"
    }
    $cadAfter = @(Get-Process -Name acad -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id | Sort-Object)
    if (($cadBefore -join ",") -cne ($cadAfter -join ",")) {
        throw "验证期间 AutoCAD 进程集合发生变化。"
    }

    $runtimeEvidenceSpecMap = [ordered]@{
        RealAgentHostBootstrapDoctorCompleted = "REAL_AGENTHOST_SUCCESS"
        RepeatedRealAgentHostBootstrapCompleted = "REAL_AGENTHOST_REPEAT_5"
        RestrictedTokenPrimitivesFailClosed = "RESTRICTED_TOKEN_PRIMITIVES_FAIL_CLOSED"
        RestrictedTokenBootstrapProbePortable = "RESTRICTED_TOKEN_BOOTSTRAP_PROBE_PORTABLE"
        ExperimentalProcessIdentityNotPublic = "EXPERIMENTAL_IDENTITY_NOT_PUBLIC"
        ProcessTreeResourceLimitsApplied = "JOB_RESOURCE_LIMITS_APPLIED"
        InvalidProcessTreeResourceLimitsFailClosed = "JOB_RESOURCE_LIMITS_INVALID"
        ResourceLimitErrorCodesStable = "RESOURCE_LIMIT_ERROR_CODES_STABLE"
        NestedJobAssignmentCompatible = "NESTED_JOB_ASSIGNMENT_COMPATIBLE"
        JobUserTimeTerminatesProcessTree = "JOB_USER_TIME_TERMINATES_TREE"
        JobProcessLimitProducesStructuredTerminal = "JOB_PROCESS_LIMIT_STRUCTURED"
        JobMemoryLimitProducesStructuredTerminal = "JOB_MEMORY_LIMIT_STRUCTURED"
        CombinedJobLimitsProduceSingleTerminal = "JOB_COMBINED_LIMIT_SINGLE_TERMINAL"
        SessionWallClockTerminatesProcessTree = "SESSION_RUNTIME_TERMINATES_TREE"
        SessionWallClockRetriesCleanup = "SESSION_RUNTIME_RETRIES_CLEANUP"
        SessionStopPreventsRuntimeExpiry = "SESSION_STOP_PREVENTS_RUNTIME_EXPIRY"
        SessionWallClockFailedCleanupPoisonsStart = "SESSION_RUNTIME_FAILURE_POISONS_START"
        ServiceStopAllowsGracefulExitBeforeForcedTermination = "SERVICE_STOP_ALLOWS_GRACEFUL_EXIT"
        ServiceStopUsesConfiguredGrace = "SERVICE_STOP_USES_CONFIGURED_GRACE"
        SessionWorkspaceProtectedLayout = "SESSION_WORKSPACE_PROTECTED_LAYOUT"
        SessionWorkspaceDuplicateRejected = "SESSION_WORKSPACE_DUPLICATE_REJECTED"
        SessionWorkspaceInvalidRootsRejected = "SESSION_WORKSPACE_INVALID_ROOTS_REJECTED"
        SessionWorkspaceReparseRootRejected = "SESSION_WORKSPACE_REPARSE_ROOT_REJECTED"
        SessionWorkspaceActiveLeasePreserved = "SESSION_WORKSPACE_ACTIVE_LEASE_PRESERVED"
        SessionWorkspaceCrashRecovery = "SESSION_WORKSPACE_CRASH_RECOVERY"
        ServiceSessionWorkspaceRemoved = "SERVICE_SESSION_WORKSPACE_REMOVED"
        ServiceStartFailureWorkspaceRemoved = "SERVICE_START_FAILURE_WORKSPACE_REMOVED"
        ServiceWorkspaceCleanupCanRetry = "SERVICE_WORKSPACE_CLEANUP_CAN_RETRY"
        ServiceStartStopRepeat500 = "SERVICE_START_STOP_REPEAT_500"
        InvalidExecutablePathsFailClosed = "INVALID_EXECUTABLE_PATHS"
        ApprovedExecutableSha256MismatchRejected = "EXECUTABLE_SHA256_MISMATCH"
        StartupTimeoutTriggersFailClosedAbortAndBoundedCleanup = "TIMEOUT_TERMINATES_UNCONFIRMED"
        ConfirmationThenHangTriggersFailClosedAbortAndBoundedCleanup = "CONFIRMATION_THEN_HANG_TIMEOUT"
        CallerThreadNonBlockingVerified = "CALLER_THREAD_NONBLOCKING"
        CancellationTerminatesUnconfirmedChild = "CANCELLATION_TERMINATES_UNCONFIRMED"
        EarlyExitFailClosed = "EARLY_EXIT_REJECTED"
        MalformedConfirmationRejected = "MALFORMED_CONFIRMATION_REJECTED"
        ConfirmationIdentityMismatchRejected = "IDENTITY_MISMATCH_REJECTED"
        TrailingAndDuplicateConfirmationRejected = "TRAILING_DUPLICATE_REJECTED"
        ChildClearsInheritedFlags = "CHILD_CLEARS_INHERITANCE"
        HandleAllowlistCanaryVerified = "HANDLE_ALLOWLIST_CANARY"
        StandardErrorSeparateAndBounded = "STDERR_BOUNDED"
        ServiceStopKillsProcessTree = "SERVICE_STOP_KILLS_PROCESS_TREE"
        UnexpectedAgentHostExitKillsProcessTree = "AGENTHOST_UNEXPECTED_EXIT_KILLS_PROCESS_TREE"
        ProcessOwnerExitKillsProcessTree = "OWNER_EXIT_KILLS_PROCESS_TREE"
    }
    $runtimeEvidence = [ordered]@{}
    foreach ($entry in $runtimeEvidenceSpecMap.GetEnumerator()) {
        $runtimeEvidence[$entry.Key] = Test-SpecPassed `
            -SpecResult $net8Specs -Id ([string]$entry.Value)
    }

    $evidence = [ordered]@{
        SchemaVersion = 16
        RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
        Scope = "autocad2016-live-agenthost-inherited-handle-bootstrap-doctor"
        Status = "live-agenthost-bootstrap-doctor-gate-passed"
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        DotNetSdk = $actualSdk
        Configuration = $Configuration
        IsolatedBuildCount = 2
        BitForBitMatch = $true
        RunnableOutputTreeComparedByRelativePathAndSha256 = $true
        RunnableOutputTreesRecheckedAfterSpecs = $true
        RunnableOutputTreeFileCount = $buildATree.Count
        RunnableOutputTreeExclusions = @()
        CompiledInputFileCount = $sourceBefore.Count
        ArtifactHashes = $hashes
        Net45Specs = "$($net45Specs.Passed)/$($net45Specs.Total)"
        Net8Specs = "$($net8Specs.Passed)/$($net8Specs.Total)"
        RequiredRuntimeSpecIds = @($requiredSpecIds)
        RuntimeSpecIds = @($net8Specs.Ids)
        BootstrapPrimitiveSourceUnchanged = $true
        StaticSourceObservations = $staticObservations
        RuntimeEvidence = $runtimeEvidence
        RelevantProcessBaselineCount = $processBefore.Count
        RelevantProcessFinalCount = $processAfter.Count
        NoNewResidualAgentProcesses = $true
        ResidualAgentProcesses = $false
        ExternalAuthenticationKeyDeliveryLiveVerified = $runtimeEvidence.RealAgentHostBootstrapDoctorCompleted
        DedicatedInheritedHandleTransportLiveVerified = (
            $runtimeEvidence.RealAgentHostBootstrapDoctorCompleted -and
            $runtimeEvidence.HandleAllowlistCanaryVerified -and
            $runtimeEvidence.ChildClearsInheritedFlags)
        BootstrapTransportConfidentialityLiveVerified = $false
        BootstrapTransportConfidentialityAgainstExternalHandleDuplicationVerified = $false
        ChildProcessIdentityBindingLiveVerified = $false
        ChildConfirmationPidAndCreationTimeBindingLiveVerified = $runtimeEvidence.ConfirmationIdentityMismatchRejected
        ApprovedExecutableSha256EnforcementLiveVerified = $runtimeEvidence.ApprovedExecutableSha256MismatchRejected
        ExecutableFileIdentityToctouRaceDynamicallyVerified = $false
        SuspendedLaunchRaceDynamicallyVerified = $false
        StartupDeadlineAbortAndBoundedTerminationCleanupLiveVerified = (
            $runtimeEvidence.StartupTimeoutTriggersFailClosedAbortAndBoundedCleanup -and
            $runtimeEvidence.ConfirmationThenHangTriggersFailClosedAbortAndBoundedCleanup -and
            $runtimeEvidence.CancellationTerminatesUnconfirmedChild)
        ProcessTreeCleanupOnServiceStopLiveVerified = $runtimeEvidence.ServiceStopKillsProcessTree
        ProcessTreeStartStopRepeat500Verified = $runtimeEvidence.ServiceStartStopRepeat500
        ProcessTreeCleanupOnUnexpectedAgentHostExitLiveVerified =
            $runtimeEvidence.UnexpectedAgentHostExitKillsProcessTree
        ProcessTreeCleanupOnOwnerExitLiveVerified = $runtimeEvidence.ProcessOwnerExitKillsProcessTree
        ProcessTreeResourceLimitsRuntimeVerified = (
            $runtimeEvidence.ProcessTreeResourceLimitsApplied -and
            $runtimeEvidence.InvalidProcessTreeResourceLimitsFailClosed -and
            $runtimeEvidence.JobUserTimeTerminatesProcessTree)
        ResourceLimitTerminalAttributionRuntimeVerified = (
            $runtimeEvidence.ResourceLimitErrorCodesStable -and
            $runtimeEvidence.JobUserTimeTerminatesProcessTree -and
            $runtimeEvidence.JobProcessLimitProducesStructuredTerminal -and
            $runtimeEvidence.JobMemoryLimitProducesStructuredTerminal -and
            $runtimeEvidence.CombinedJobLimitsProduceSingleTerminal -and
            $runtimeEvidence.SessionWallClockTerminatesProcessTree)
        NestedJobAssignmentCurrentRuntimeVerified =
            $runtimeEvidence.NestedJobAssignmentCompatible
        EnterpriseNestedJobMatrixVerified = $false
        SessionWallClockDeadlineRuntimeVerified = (
            $runtimeEvidence.SessionWallClockTerminatesProcessTree -and
            $runtimeEvidence.SessionWallClockRetriesCleanup -and
            $runtimeEvidence.SessionStopPreventsRuntimeExpiry -and
            $runtimeEvidence.SessionWallClockFailedCleanupPoisonsStart)
        GracefulServiceExitBeforeForcedTerminationRuntimeVerified =
            $runtimeEvidence.ServiceStopAllowsGracefulExitBeforeForcedTermination
        ConfiguredGracefulStopTimeoutRuntimeVerified =
            $runtimeEvidence.ServiceStopUsesConfiguredGrace
        ProtectedSessionWorkspaceLifecycleRuntimeVerified = (
            $runtimeEvidence.SessionWorkspaceProtectedLayout -and
            $runtimeEvidence.SessionWorkspaceDuplicateRejected -and
            $runtimeEvidence.SessionWorkspaceInvalidRootsRejected -and
            $runtimeEvidence.SessionWorkspaceReparseRootRejected -and
            $runtimeEvidence.SessionWorkspaceActiveLeasePreserved -and
            $runtimeEvidence.SessionWorkspaceCrashRecovery -and
            $runtimeEvidence.ServiceSessionWorkspaceRemoved -and
            $runtimeEvidence.ServiceStartFailureWorkspaceRemoved -and
            $runtimeEvidence.ServiceWorkspaceCleanupCanRetry)
        RestrictedTokenPrimitiveRuntimeVerified =
            $runtimeEvidence.RestrictedTokenPrimitivesFailClosed
        RestrictedTokenPublicProductSurfaceClosed =
            $runtimeEvidence.ExperimentalProcessIdentityNotPublic
        RestrictedTokenCompatibilityProbePortable =
            $runtimeEvidence.RestrictedTokenBootstrapProbePortable
        RestrictedTokenProbeOutcomes = [ordered]@{
            Net8Primitives = $net8RestrictedPrimitiveOutcome
            Net45Primitives = $net45RestrictedPrimitiveOutcome
            Net8Bootstrap = $net8RestrictedBootstrapOutcome
            Net45Bootstrap = $net45RestrictedBootstrapOutcome
        }
        RestrictedTokenCurrentRuntimeFailsClosed =
            ($net8RestrictedBootstrapOutcome -ne "authenticated_success" -and
             $net45RestrictedBootstrapOutcome -ne "authenticated_success")
        RestrictedTokenSuccessfulAuthenticatedBootstrapVerified =
            ($net8RestrictedBootstrapOutcome -eq "authenticated_success" -and
             $net45RestrictedBootstrapOutcome -eq "authenticated_success")
        PendingBootstrapAtomicConsumptionLiveVerified = $false
        SourceTreeBinOrObjModified = $false
        AutoCadProcessSetChanged = $false
        AutoCadStartedOrRestarted = $false
        CadCommandsSent = $false
        NetLoadAttempted = $false
        NetLoadVerified = $false
        AgentHostLiveBridgeVerified = $false
        CadRuntimeIntegrated = $false
        EvidenceBoundary = "This gate proves the exact mandatory net45/net8 Spec ID set, real out-of-process bootstrap-doctor authentication through restricted inherited standard handles, approved SHA-256 mismatch rejection, PID/creation-time confirmation rejection, startup-deadline fail-closed abort followed by bounded termination cleanup, cancellation cleanup, bounded stderr, handle-allowlist canary exclusion, service and owner process-tree cleanup, Windows-reported Job resource limits, nested Job assignment on the current Windows runtime, and authenticated-service runtime cleanup. It does not prove the required enterprise nested-Job policy matrix. The public product configuration and result surfaces do not expose the experimental process-identity selector or its raw telemetry. The internal restricted-token probe accepts only a Windows-reported restricted success, a structured process-isolation failure, or child failure after a restricted launch; it never falls back to CurrentUser, and the exact sanitized net45/net8 outcomes are recorded separately. A successful primitive or probe result is not production sandbox evidence and does not prove runtime/workspace/pipe ACLs, credentials, real Codex, bootstrap-serve, AutoCAD integration, CAD work, or complete AutoCAD 2016 support. The gate also proves reproducible runnable outputs and an empty relevant-process baseline/final state."
    }
    $evidence | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $evidencePath -Encoding UTF8

    Write-Host "`nAutoCAD 2016 真实 AgentHost 安全引导门禁通过。" -ForegroundColor Green
    Write-Host ("AGENT_BOOTSTRAP_EVIDENCE=" + $evidencePath)
}
finally {
    Complete-CodexBuildSafety -State $buildSafety -Stage "agent-bootstrap" | Out-Null
    $env:DOTNET_NOLOGO = $previousNoLogo
}
