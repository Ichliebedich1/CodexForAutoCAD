using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Codex.AutoCAD.AgentLauncher;

if (args.Length > 0
    && string.Equals(args[0], "--job-owner-helper", StringComparison.Ordinal))
{
    return RunJobOwnerHelper(args);
}

var arguments = ParseArguments(args);
var fixture = new FakeAgentHostFixture(arguments.FakeAgentHostPath);
try
{
    var specs = new[]
    {
        new SpecCase("REAL_AGENTHOST_SUCCESS", "真实AgentHost完成认证bootstrap doctor且无残留", () => RealAgentHostSucceeds(arguments.AgentHostPath)),
        new SpecCase("REAL_AGENTHOST_REPEAT_5", "连续五次真实引导均成功且无残留", () => RepeatedRealAgentHostSucceeds(arguments.AgentHostPath)),
        new SpecCase("JOB_RESOURCE_LIMITS_APPLIED", "Job Object应用进程数、内存、CPU与累计用户时间硬限制", ProcessTreeResourceLimitsAreApplied),
        new SpecCase("JOB_RESOURCE_LIMITS_INVALID", "无效进程树与会话运行限制在启动前失败关闭", ProcessTreeResourceLimitsFailClosed),
        new SpecCase("JOB_USER_TIME_TERMINATES_TREE", "累计Job用户时间耗尽会终止忙碌进程树", () => JobUserTimeTerminatesBusyTree(fixture)),
        new SpecCase("SESSION_RUNTIME_TERMINATES_TREE", "会话墙钟截止会终止AgentHost进程树", () => SessionRuntimeTerminatesTree(fixture)),
        new SpecCase("SESSION_RUNTIME_RETRIES_CLEANUP", "会话墙钟截止首次清理失败后自动重试", SessionRuntimeRetriesCleanup),
        new SpecCase("SESSION_STOP_PREVENTS_RUNTIME_EXPIRY", "显式停止完成后会话墙钟状态不得反转", SessionStopPreventsRuntimeExpiry),
        new SpecCase("SERVICE_STOP_ALLOWS_GRACEFUL_EXIT", "service停止在强制终止前允许一次自然退出", ServiceStopAllowsGracefulExit),
        new SpecCase("SERVICE_STOP_KILLS_PROCESS_TREE", "停止服务会回收AgentHost及其受监管后代进程", () => ServiceStopKillsProcessTree(fixture)),
        new SpecCase("AGENTHOST_UNEXPECTED_EXIT_KILLS_PROCESS_TREE", "AgentHost异常退出时启动器仍存活也会回收受监管后代进程", () => AgentHostUnexpectedExitKillsProcessTree(fixture)),
        new SpecCase("OWNER_EXIT_KILLS_PROCESS_TREE", "拥有Job的启动器退出会回收AgentHost及其受监管后代进程", () => JobOwnerExitKillsProcessTree(fixture)),
        new SpecCase("INVALID_EXECUTABLE_PATHS", "相对路径、真实非EXE与缺失文件均失败关闭", () => InvalidExecutablePathsFailClosed(fixture)),
        new SpecCase("EXECUTABLE_SHA256_MISMATCH", "批准SHA-256不匹配时拒绝启动", () => ExecutableSha256MismatchFails(fixture.CreateMode("success"))),
        new SpecCase("TIMEOUT_TERMINATES_UNCONFIRMED", "启动截止触发失败关闭，随后在有界清理窗口内终止未确认子进程", () => TimeoutTerminatesChild(fixture.CreateMode("hang"))),
        new SpecCase("CONFIRMATION_THEN_HANG_TIMEOUT", "有效确认后仍挂起时由启动截止触发失败关闭并执行有界终止清理", () => ValidConfirmationThenHangTerminatesChild(fixture.CreateMode("confirmhang"))),
        new SpecCase("CALLER_THREAD_NONBLOCKING", "启动核心不在调用线程同步阻塞", () => CallerThreadIsNotBlocked(fixture.CreateMode("hang"))),
        new SpecCase("CANCELLATION_TERMINATES_UNCONFIRMED", "取消未确认引导时子进程被终止", () => CancellationTerminatesChild(fixture.CreateMode("hang"))),
        new SpecCase("EARLY_EXIT_REJECTED", "子进程提前异常退出被识别", () => EarlyExitIsReported(fixture.CreateMode("exit42"))),
        new SpecCase("MALFORMED_CONFIRMATION_REJECTED", "畸形确认帧被拒绝", () => MalformedConfirmationFails(fixture.CreateMode("garbage"))),
        new SpecCase("IDENTITY_MISMATCH_REJECTED", "确认身份不匹配被拒绝", () => IdentityMismatchFails(fixture.CreateMode("identity"))),
        new SpecCase("TRAILING_DUPLICATE_REJECTED", "确认尾随字节与第二帧均被拒绝", () => TrailingAndDuplicateConfirmationFail(fixture)),
        new SpecCase("CHILD_CLEARS_INHERITANCE", "子端领取句柄后清除继承位", () => ChildClearsInheritance(fixture.CreateMode("inherit"))),
        new SpecCase("HANDLE_ALLOWLIST_CANARY", "启动句柄白名单排除父进程可继承canary", () => HandleAllowListExcludesCanary(fixture.CreateMode("canary"))),
        new SpecCase("STDERR_BOUNDED", "stderr持续排空、严格受限且失败时不公开原文", () => StandardErrorIsBounded(fixture)),
        new SpecCase("SERVICE_STOP_RETRIES_TERMINATION", "service首次终止失败后第二次STOP重新尝试并成功", ServiceStopRetriesTermination),
        new SpecCase("SERVICE_STOP_RETRIES_THROWN_TERMINATION", "service终止委托抛错后第二次STOP重新尝试并成功", ServiceStopRetriesThrownTermination),
        new SpecCase("SERVICE_STOP_PROCESS_DISPOSE_CAN_RETRY", "service进程包装释放失败后只重试未完成清理", ServiceStopProcessDisposeCanRetry),
        new SpecCase("SERVICE_STOP_ABORT_IO_CAN_RETRY", "service I/O中止失败后重试且不提前释放进程包装", ServiceStopAbortIoCanRetry),
        new SpecCase("SERVICE_STOP_THROWN_ABORT_IO_CAN_RETRY", "service I/O中止委托抛错后结构化失败并可重试", ServiceStopThrownAbortIoCanRetry),
        new SpecCase("SERVICE_STOP_STDERR_CAN_RETRY", "service stderr排空超时后保留任务并在下一次STOP收口", ServiceStopStandardErrorCanRetry),
        new SpecCase("SERVICE_STOP_FAULTED_STDERR_IS_SETTLED", "service I/O中止后的faulted stderr按已终止收口", ServiceStopFaultedStandardErrorIsSettled),
        new SpecCase("SERVICE_STOP_RETRY_DOES_NOT_POISON_START", "显式STOP失败重试成功后不会永久阻断下一次启动", ServiceStopRetryDoesNotPoisonStart),
        new SpecCase("SERVICE_DISPOSE_FAILURE_CAN_RETRY", "service Dispose失败后再次Dispose继续剩余清理", ServiceDisposeFailureCanRetry),
        new SpecCase("SERVICE_STOP_CONCURRENT_CALLERS", "并发service STOP共享同一个有界终止尝试", ServiceStopConcurrentCallers),
        new SpecCase("SERVICE_STOP_CONCURRENT_FAILURE_SHARED", "并发service STOP共享同一失败尝试并在其后重试", ServiceStopConcurrentFailureShared),
        // This process-wide poison assertion must remain last. A failed cleanup intentionally
        // prevents every later launch in the same process.
        new SpecCase("SESSION_RUNTIME_FAILURE_POISONS_START", "会话墙钟清理连续失败后阻断后续启动", () => SessionRuntimeCleanupFailurePoisonsStart(fixture))
    };

    var failed = 0;
    foreach (var spec in specs)
    {
        try
        {
            spec.Run();
            Console.WriteLine("PASS " + spec.Id + " " + spec.Name);
        }
        catch (Exception exception)
        {
            failed++;
            Console.Error.WriteLine(
                "FAIL " + spec.Id + " " + spec.Name + ": " + exception.Message);
        }
    }

    Console.WriteLine((specs.Length - failed) + "/" + specs.Length + " specs passed");
    return failed == 0 ? 0 : 1;
}
finally
{
    fixture.Dispose();
}

static void RealAgentHostSucceeds(string agentHostPath)
{
    var options = CreateOptions(agentHostPath);
    var result = AgentHostBootstrapDoctor.RunAsync(
            options,
            CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    True(result.ProcessId > 0, "Process id was not captured.");
    Equal(32, result.BootstrapId.Length);
    Equal(32, result.SessionId.Length);
    True(result.PipeName.StartsWith("codex-autocad-", StringComparison.Ordinal), "Pipe name is invalid.");
    Equal(options.ExpectedExecutableSha256, result.ExecutableSha256);
    ProcessMustBeGone(result.ProcessId);
}

static void RepeatedRealAgentHostSucceeds(string agentHostPath)
{
    for (var index = 0; index < 5; index++)
    {
        RealAgentHostSucceeds(agentHostPath);
    }
}

static void ProcessTreeResourceLimitsAreApplied()
{
    const int processLimit = 7;
    const long memoryLimit = 768L * 1024 * 1024;
    const int cpuRatePercent = 37;
    var jobUserTime = TimeSpan.FromMinutes(2);
    var defaults = new AgentHostBootstrapOptions("relative.exe", new string('0', 64))
        .GetValidatedProcessTreeLimits();
    Equal(
        AgentHostBootstrapOptions.DefaultMaximumActiveProcesses,
        defaults.MaximumActiveProcesses);
    Equal(
        AgentHostBootstrapOptions.DefaultMaximumJobMemoryBytes,
        defaults.MaximumJobMemoryBytes);
    Equal(
        AgentHostBootstrapOptions.DefaultMaximumCpuRatePercent,
        defaults.MaximumCpuRatePercent);
    Equal(
        AgentHostBootstrapOptions.DefaultMaximumJobUserTime,
        defaults.MaximumJobUserTime);
    Equal(
        AgentHostBootstrapOptions.DefaultMaximumSessionRuntime,
        new AgentHostBootstrapOptions("relative.exe", new string('0', 64))
            .GetValidatedSessionRuntime());

    using (var job = WindowsProcessTreeJob.CreateKillOnClose(
               new AgentHostProcessTreeLimits(
                   processLimit,
                   memoryLimit,
                   cpuRatePercent,
                   jobUserTime)))
    {
        var applied = job.QueryLimits();
        Equal(processLimit, applied.MaximumActiveProcesses);
        Equal(memoryLimit, applied.MaximumJobMemoryBytes);
        Equal(jobUserTime, applied.MaximumJobUserTime);
        Equal(cpuRatePercent * 100, applied.CpuRateBasisPoints);
        True(
            (applied.LimitFlags & WindowsNative.JobObjectLimitKillOnJobClose) != 0,
            "KILL_ON_JOB_CLOSE was not applied.");
        True(
            (applied.LimitFlags & WindowsNative.JobObjectLimitActiveProcess) != 0,
            "ACTIVE_PROCESS was not applied.");
        True(
            (applied.LimitFlags & WindowsNative.JobObjectLimitJobMemory) != 0,
            "JOB_MEMORY was not applied.");
        True(
            (applied.LimitFlags & WindowsNative.JobObjectLimitJobTime) != 0,
            "JOB_TIME was not applied.");
        True(
            (applied.CpuControlFlags & WindowsNative.JobObjectCpuRateControlEnable) != 0,
            "CPU rate control was not enabled.");
        True(
            (applied.CpuControlFlags & WindowsNative.JobObjectCpuRateControlHardCap) != 0,
            "CPU hard cap was not applied.");
    }
}

static void ProcessTreeResourceLimitsFailClosed()
{
    var options = new AgentHostBootstrapOptions("relative.exe", new string('0', 64));

    options.MaximumActiveProcesses =
        AgentHostBootstrapOptions.MinimumMaximumActiveProcesses - 1;
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedProcessTreeLimits());

    options.MaximumActiveProcesses =
        AgentHostBootstrapOptions.MaximumMaximumActiveProcesses + 1;
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedProcessTreeLimits());

    options.MaximumActiveProcesses = AgentHostBootstrapOptions.DefaultMaximumActiveProcesses;
    options.MaximumJobMemoryBytes =
        AgentHostBootstrapOptions.MinimumMaximumJobMemoryBytes - 1;
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedProcessTreeLimits());

    options.MaximumJobMemoryBytes =
        AgentHostBootstrapOptions.MaximumMaximumJobMemoryBytes + 1;
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedProcessTreeLimits());

    options.MaximumJobMemoryBytes = AgentHostBootstrapOptions.DefaultMaximumJobMemoryBytes;
    options.MaximumCpuRatePercent =
        AgentHostBootstrapOptions.MinimumMaximumCpuRatePercent - 1;
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedProcessTreeLimits());

    options.MaximumCpuRatePercent =
        AgentHostBootstrapOptions.MaximumMaximumCpuRatePercent + 1;
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedProcessTreeLimits());

    options.MaximumCpuRatePercent = AgentHostBootstrapOptions.DefaultMaximumCpuRatePercent;
    options.MaximumJobUserTime =
        AgentHostBootstrapOptions.MinimumMaximumJobUserTime - TimeSpan.FromTicks(1);
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedProcessTreeLimits());

    options.MaximumJobUserTime =
        AgentHostBootstrapOptions.MaximumMaximumJobUserTime + TimeSpan.FromTicks(1);
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedProcessTreeLimits());

    options.MaximumJobUserTime = AgentHostBootstrapOptions.DefaultMaximumJobUserTime;
    options.MaximumSessionRuntime =
        AgentHostBootstrapOptions.MinimumMaximumSessionRuntime - TimeSpan.FromTicks(1);
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedSessionRuntime());

    options.MaximumSessionRuntime =
        AgentHostBootstrapOptions.MaximumMaximumSessionRuntime + TimeSpan.FromTicks(1);
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => options.GetValidatedSessionRuntime());
}

static void JobUserTimeTerminatesBusyTree(FakeAgentHostFixture fixture)
{
    AgentHostServiceSession? session = null;
    try
    {
        var options = CreateOptions(fixture.CreateMode("serveburn"));
        options.MaximumCpuRatePercent = 100;
        options.MaximumJobUserTime = TimeSpan.FromSeconds(1);
        options.MaximumSessionRuntime = TimeSpan.FromSeconds(15);
        session = AgentHostBootstrapService.StartAsync(options, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        WaitForProcessToExit(session.ProcessId, TimeSpan.FromSeconds(10));
        True(!session.RuntimeExpired, "The wall-clock deadline fired before the Job user-time limit.");
        session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        session.Dispose();
        session = null;
    }
    finally
    {
        session?.Dispose();
    }
}

static void SessionRuntimeTerminatesTree(FakeAgentHostFixture fixture)
{
    AgentHostServiceSession? session = null;
    try
    {
        var options = CreateOptions(fixture.CreateMode("servewall"));
        options.MaximumJobUserTime = TimeSpan.FromHours(1);
        options.MaximumSessionRuntime = TimeSpan.FromSeconds(1);
        session = AgentHostBootstrapService.StartAsync(options, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        WaitForProcessToExit(session.ProcessId, TimeSpan.FromSeconds(5));
        WaitForCondition(
            () => session.RuntimeExpired,
            TimeSpan.FromSeconds(1),
            "The service process exited without publishing the wall-clock deadline state.");
        session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        session.Dispose();
        session = null;
    }
    finally
    {
        session?.Dispose();
    }
}

static void SessionRuntimeRetriesCleanup()
{
    var terminateCount = 0;
    var abortIoCount = 0;
    var disposeCount = 0;
    var session = new AgentHostServiceSession(
        _ => Interlocked.Increment(ref terminateCount) > 1,
        () =>
        {
            Interlocked.Increment(ref abortIoCount);
            return null;
        },
        () => Interlocked.Increment(ref disposeCount),
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult(),
        TimeSpan.FromSeconds(1));
    try
    {
        WaitForCondition(
            () => Volatile.Read(ref terminateCount) >= 2,
            TimeSpan.FromSeconds(5),
            "The runtime deadline did not retry failed termination.");
        session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        True(session.RuntimeExpired, "The session did not retain its runtime-expired state.");
        Equal(2, Volatile.Read(ref terminateCount));
        Equal(1, Volatile.Read(ref abortIoCount));
        Equal(1, Volatile.Read(ref disposeCount));
    }
    finally
    {
        session.Dispose();
    }
}

static void SessionStopPreventsRuntimeExpiry()
{
    var terminateCount = 0;
    var abortIoCount = 0;
    var disposeCount = 0;
    var session = new AgentHostServiceSession(
        _ =>
        {
            Interlocked.Increment(ref terminateCount);
            return true;
        },
        () =>
        {
            Interlocked.Increment(ref abortIoCount);
            return null;
        },
        () => Interlocked.Increment(ref disposeCount),
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult(),
        TimeSpan.FromMilliseconds(250));
    try
    {
        session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        Thread.Sleep(500);
        True(!session.RuntimeExpired, "A cancelled runtime deadline reversed the stopped state.");
        Equal(1, Volatile.Read(ref terminateCount));
        Equal(1, Volatile.Read(ref abortIoCount));
        Equal(1, Volatile.Read(ref disposeCount));
    }
    finally
    {
        session.Dispose();
    }
}

static void SessionRuntimeCleanupFailurePoisonsStart(FakeAgentHostFixture fixture)
{
    var terminateCount = 0;
    var session = new AgentHostServiceSession(
        _ =>
        {
            Interlocked.Increment(ref terminateCount);
            return false;
        },
        () => null,
        () => { },
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult(),
        TimeSpan.FromSeconds(1));

    WaitForCondition(
        () =>
        {
            try
            {
                AgentBootstrapLateFailureRegistry.ThrowIfPoisoned();
                return false;
            }
            catch (AgentBootstrapLaunchException exception)
            {
                return exception.Failure == AgentBootstrapLaunchFailure.ChildTerminationFailed;
            }
        },
        TimeSpan.FromSeconds(5),
        "The runtime deadline did not poison later launches after two cleanup failures.");

    True(session.RuntimeExpired, "The failed cleanup did not retain the runtime-expired state.");
    Equal(2, Volatile.Read(ref terminateCount));
    var fakePath = fixture.CreateMode("success");
    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        () => AgentHostBootstrapService.StartAsync(
                CreateOptions(fakePath),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    ProcessNameMustBeGone(fakePath);
}

static void ServiceStopAllowsGracefulExit()
{
    var waitCount = 0;
    var terminateCount = 0;
    var abortIoCount = 0;
    var disposeCount = 0;
    var session = new AgentHostServiceSession(
        _ =>
        {
            Interlocked.Increment(ref waitCount);
            return true;
        },
        _ =>
        {
            Interlocked.Increment(ref terminateCount);
            return false;
        },
        () =>
        {
            Interlocked.Increment(ref abortIoCount);
            return null;
        },
        () => Interlocked.Increment(ref disposeCount),
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult());
    session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    session.Dispose();

    Equal(1, Volatile.Read(ref waitCount));
    Equal(0, Volatile.Read(ref terminateCount));
    Equal(1, Volatile.Read(ref abortIoCount));
    Equal(1, Volatile.Read(ref disposeCount));
}

static void ServiceStopKillsProcessTree(FakeAgentHostFixture fixture)
{
    const string descendantExecutableVariable = "CODEX_AUTOCAD_TEST_DESCENDANT_EXECUTABLE";
    const string descendantProcessIdPathVariable = "CODEX_AUTOCAD_TEST_DESCENDANT_PROCESS_ID_PATH";
    var descendantExecutable = fixture.CreateMode("hang");
    var processIdPath = Path.Combine(
        Path.GetTempPath(),
        "CodexAgentLauncherDescendant-" + Guid.NewGuid().ToString("N") + ".txt");
    var previousDescendantExecutable = Environment.GetEnvironmentVariable(descendantExecutableVariable);
    var previousProcessIdPath = Environment.GetEnvironmentVariable(descendantProcessIdPathVariable);
    AgentHostServiceSession? session = null;
    var descendantProcessId = 0;
    try
    {
        Environment.SetEnvironmentVariable(descendantExecutableVariable, descendantExecutable);
        Environment.SetEnvironmentVariable(descendantProcessIdPathVariable, processIdPath);
        session = AgentHostBootstrapService.StartAsync(
                CreateOptions(fixture.CreateMode("servechild")),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        descendantProcessId = ReadProcessIdFile(processIdPath);
        True(descendantProcessId > 0, "The process-tree test descendant id is invalid.");

        session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        ProcessMustBeGone(session.ProcessId);
        ProcessMustBeGone(descendantProcessId);
        session.Dispose();
        session = null;
    }
    finally
    {
        try
        {
            session?.Dispose();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                descendantExecutableVariable,
                previousDescendantExecutable);
            Environment.SetEnvironmentVariable(
                descendantProcessIdPathVariable,
                previousProcessIdPath);
            if (descendantProcessId > 0)
            {
                KillFixtureProcessIfStillRunning(descendantProcessId, descendantExecutable);
            }

            try
            {
                File.Delete(processIdPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

static void AgentHostUnexpectedExitKillsProcessTree(FakeAgentHostFixture fixture)
{
    const string descendantExecutableVariable = "CODEX_AUTOCAD_TEST_DESCENDANT_EXECUTABLE";
    const string descendantProcessIdPathVariable = "CODEX_AUTOCAD_TEST_DESCENDANT_PROCESS_ID_PATH";
    const string exitEventNameVariable = "CODEX_AUTOCAD_TEST_AGENTHOST_EXIT_EVENT";
    var descendantExecutable = fixture.CreateMode("hang");
    var processIdPath = Path.Combine(
        Path.GetTempPath(),
        "CodexAgentLauncherUnexpectedExitDescendant-" + Guid.NewGuid().ToString("N") + ".txt");
    var eventName = "CodexAgentLauncherUnexpectedExit-" + Guid.NewGuid().ToString("N");
    var previousDescendantExecutable = Environment.GetEnvironmentVariable(descendantExecutableVariable);
    var previousProcessIdPath = Environment.GetEnvironmentVariable(descendantProcessIdPathVariable);
    var previousExitEventName = Environment.GetEnvironmentVariable(exitEventNameVariable);
    AgentHostServiceSession? session = null;
    var descendantProcessId = 0;
    try
    {
        // The AgentLauncher integration suite is Windows-only; use a named event to make the
        // FakeAgentHost exit only after the service session and its Job are established.
#pragma warning disable CA1416
        using (var exitSignal = new EventWaitHandle(
                   false,
                   EventResetMode.ManualReset,
                   eventName))
#pragma warning restore CA1416
        {
            Environment.SetEnvironmentVariable(descendantExecutableVariable, descendantExecutable);
            Environment.SetEnvironmentVariable(descendantProcessIdPathVariable, processIdPath);
            Environment.SetEnvironmentVariable(exitEventNameVariable, eventName);
            session = AgentHostBootstrapService.StartAsync(
                    CreateOptions(fixture.CreateMode("servechildexit")),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            descendantProcessId = ReadProcessIdFile(processIdPath);
            True(descendantProcessId > 0, "The unexpected-exit descendant id is invalid.");

            exitSignal.Set();
            WaitForProcessToExit(session.ProcessId, TimeSpan.FromSeconds(3));
            // Do not call STOP before this assertion: the watcher itself must close the retained
            // Job handle and let KILL_ON_JOB_CLOSE collect the still-running descendant.
            WaitForProcessToExit(descendantProcessId, TimeSpan.FromSeconds(3));

            session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            session.Dispose();
            session = null;
        }
    }
    finally
    {
        try
        {
            session?.Dispose();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                descendantExecutableVariable,
                previousDescendantExecutable);
            Environment.SetEnvironmentVariable(
                descendantProcessIdPathVariable,
                previousProcessIdPath);
            Environment.SetEnvironmentVariable(exitEventNameVariable, previousExitEventName);
            if (descendantProcessId > 0)
            {
                KillFixtureProcessIfStillRunning(descendantProcessId, descendantExecutable);
            }

            DeleteFileIfPresent(processIdPath);
        }
    }
}

static void JobOwnerExitKillsProcessTree(FakeAgentHostFixture fixture)
{
    var descendantExecutable = fixture.CreateMode("hang");
    var agentHostExecutable = fixture.CreateMode("servechild");
    var unique = Guid.NewGuid().ToString("N");
    var descendantProcessIdPath = Path.Combine(
        Path.GetTempPath(),
        "CodexAgentLauncherDescendant-" + unique + ".txt");
    var agentHostProcessIdPath = Path.Combine(
        Path.GetTempPath(),
        "CodexAgentLauncherAgentHost-" + unique + ".txt");
    var descendantProcessId = 0;
    var agentHostProcessId = 0;
    var descendantExitVerified = false;
    var agentHostExitVerified = false;
    try
    {
        using (var owner = StartJobOwnerHelper(
                   agentHostExecutable,
                   descendantExecutable,
                   descendantProcessIdPath,
                   agentHostProcessIdPath))
        {
            if (!owner.WaitForExit(5000))
            {
                owner.Kill();
                throw new InvalidOperationException(
                    "The process-tree Job owner did not exit inside the test deadline.");
            }

            Equal(0, owner.ExitCode);
        }

        descendantProcessId = ReadProcessIdFile(descendantProcessIdPath);
        agentHostProcessId = ReadProcessIdFile(agentHostProcessIdPath);
        WaitForProcessToExit(agentHostProcessId, TimeSpan.FromSeconds(3));
        agentHostExitVerified = true;
        WaitForProcessToExit(descendantProcessId, TimeSpan.FromSeconds(3));
        descendantExitVerified = true;
    }
    finally
    {
        if (!agentHostExitVerified)
        {
            KillFixtureProcessIfStillRunning(agentHostProcessId, agentHostExecutable);
        }
        if (!descendantExitVerified)
        {
            KillFixtureProcessIfStillRunning(descendantProcessId, descendantExecutable);
        }
        DeleteFileIfPresent(descendantProcessIdPath);
        DeleteFileIfPresent(agentHostProcessIdPath);
    }
}

static int RunJobOwnerHelper(string[] values)
{
    var fakeAgentHost = GetRequiredOption(values, "--fake-agent-host");
    var descendantProcessIdPath = GetRequiredOption(values, "--descendant-process-id-path");
    var agentHostProcessIdPath = GetRequiredOption(values, "--agenthost-process-id-path");
    var session = AgentHostBootstrapService.StartAsync(
            CreateOptions(fakeAgentHost),
            CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    File.WriteAllText(
        agentHostProcessIdPath,
        session.ProcessId.ToString(CultureInfo.InvariantCulture),
        new System.Text.UTF8Encoding(false));
    ReadProcessIdFile(descendantProcessIdPath);

    // Deliberately bypass StopAsync and Dispose. The OS closing this process's Job handle is the
    // behavior under test; the caller must observe the AgentHost subtree disappear afterwards.
    Environment.Exit(0);
    return 1;
}

static Process StartJobOwnerHelper(
    string fakeAgentHost,
    string descendantExecutable,
    string descendantProcessIdPath,
    string agentHostProcessIdPath)
{
    var entryAssembly = Assembly.GetEntryAssembly();
    if (entryAssembly == null || string.IsNullOrWhiteSpace(entryAssembly.Location))
    {
        throw new InvalidOperationException("The AgentLauncher Specs entry assembly is unavailable.");
    }

    var entryAssemblyPath = entryAssembly.Location;
    string hostExecutable;
    using (var current = Process.GetCurrentProcess())
    {
        hostExecutable = current.MainModule == null
            ? string.Empty
            : current.MainModule.FileName;
    }
    if (string.IsNullOrWhiteSpace(hostExecutable))
    {
        throw new InvalidOperationException("The AgentLauncher Specs process host is unavailable.");
    }

    var arguments = string.Join(" ", new[]
    {
        "--job-owner-helper",
        "--fake-agent-host", QuoteCommandLineArgument(fakeAgentHost),
        "--descendant-process-id-path", QuoteCommandLineArgument(descendantProcessIdPath),
        "--agenthost-process-id-path", QuoteCommandLineArgument(agentHostProcessIdPath),
    });
    var usesDotNetHost = string.Equals(
        Path.GetFileNameWithoutExtension(hostExecutable),
        "dotnet",
        StringComparison.OrdinalIgnoreCase);
    var startInfo = new ProcessStartInfo
    {
        FileName = hostExecutable,
        Arguments = usesDotNetHost
            ? QuoteCommandLineArgument(entryAssemblyPath) + " " + arguments
            : arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    startInfo.EnvironmentVariables["CODEX_AUTOCAD_TEST_DESCENDANT_EXECUTABLE"] = descendantExecutable;
    startInfo.EnvironmentVariables["CODEX_AUTOCAD_TEST_DESCENDANT_PROCESS_ID_PATH"] =
        descendantProcessIdPath;
    var process = Process.Start(startInfo);
    if (process == null)
    {
        throw new InvalidOperationException("Starting the process-tree Job owner helper failed.");
    }

    return process;
}

static void ServiceStopRetriesTermination()
{
    var terminateCount = 0;
    var abortIoCount = 0;
    var disposeCount = 0;
    var session = new AgentHostServiceSession(
        _ =>
        {
            terminateCount++;
            return terminateCount > 1;
        },
        () =>
        {
            abortIoCount++;
            return null;
        },
        () => disposeCount++,
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult());

    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        () => session.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
    Equal(1, terminateCount);
    Equal(0, abortIoCount);
    Equal(0, disposeCount);

    session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(2, terminateCount);
    Equal(1, abortIoCount);
    Equal(1, disposeCount);
    session.Dispose();
}

static void ServiceStopRetriesThrownTermination()
{
    var terminateCount = 0;
    var session = new AgentHostServiceSession(
        _ =>
        {
            terminateCount++;
            if (terminateCount == 1)
            {
                throw new InvalidOperationException("first-termination-throw");
            }

            return true;
        },
        () => null,
        () => { },
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult());

    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        () => session.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
    Equal(1, terminateCount);

    session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(2, terminateCount);
    session.Dispose();
}

static void ServiceStopConcurrentCallers()
{
    var terminateCount = 0;
    var disposeCount = 0;
    using (var entered = new ManualResetEventSlim(false))
    using (var release = new ManualResetEventSlim(false))
    {
        var session = new AgentHostServiceSession(
            _ =>
            {
                Interlocked.Increment(ref terminateCount);
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return true;
            },
            () => null,
            () => Interlocked.Increment(ref disposeCount),
            Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
            CreateServiceResult());

        var first = session.StopAsync(CancellationToken.None);
        True(entered.Wait(TimeSpan.FromSeconds(2)), "Service stop did not start.");
        var second = session.StopAsync(CancellationToken.None);
        True(!second.IsCompleted, "Concurrent service STOP completed before termination.");
        Equal(1, Volatile.Read(ref terminateCount));

        release.Set();
        Task.WhenAll(first, second).GetAwaiter().GetResult();
        Equal(1, Volatile.Read(ref terminateCount));
        Equal(1, Volatile.Read(ref disposeCount));
        session.Dispose();
    }
}

static void ServiceStopConcurrentFailureShared()
{
    var terminateCount = 0;
    var abortIoCount = 0;
    var disposeCount = 0;
    using (var entered = new ManualResetEventSlim(false))
    using (var release = new ManualResetEventSlim(false))
    {
        var session = new AgentHostServiceSession(
            _ =>
            {
                var attempt = Interlocked.Increment(ref terminateCount);
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return attempt > 1;
            },
            () =>
            {
                Interlocked.Increment(ref abortIoCount);
                return null;
            },
            () => Interlocked.Increment(ref disposeCount),
            Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
            CreateServiceResult());

        var first = session.StopAsync(CancellationToken.None);
        True(entered.Wait(TimeSpan.FromSeconds(2)), "Failing service stop did not start.");
        var second = session.StopAsync(CancellationToken.None);
        Equal(1, Volatile.Read(ref terminateCount));

        release.Set();
        ExpectFailure(
            AgentBootstrapLaunchFailure.ChildTerminationFailed,
            () => first.GetAwaiter().GetResult());
        ExpectFailure(
            AgentBootstrapLaunchFailure.ChildTerminationFailed,
            () => second.GetAwaiter().GetResult());
        Equal(1, Volatile.Read(ref terminateCount));
        Equal(0, Volatile.Read(ref abortIoCount));
        Equal(0, Volatile.Read(ref disposeCount));

        session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        Equal(2, Volatile.Read(ref terminateCount));
        Equal(1, Volatile.Read(ref abortIoCount));
        Equal(1, Volatile.Read(ref disposeCount));
        session.Dispose();
    }
}

static void ServiceStopProcessDisposeCanRetry()
{
    var terminateCount = 0;
    var abortIoCount = 0;
    var disposeCount = 0;
    var session = new AgentHostServiceSession(
        _ =>
        {
            terminateCount++;
            return true;
        },
        () =>
        {
            abortIoCount++;
            return null;
        },
        () =>
        {
            disposeCount++;
            if (disposeCount == 1)
            {
                throw new InvalidOperationException("first-process-dispose-failure");
            }
        },
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult());

    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        () => session.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
    Equal(1, terminateCount);
    Equal(1, abortIoCount);
    Equal(1, disposeCount);

    session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(1, terminateCount);
    Equal(1, abortIoCount);
    Equal(2, disposeCount);
    session.Dispose();
}

static void ServiceStopAbortIoCanRetry()
{
    var terminateCount = 0;
    var abortIoCount = 0;
    var disposeCount = 0;
    var session = new AgentHostServiceSession(
        _ =>
        {
            terminateCount++;
            return true;
        },
        () =>
        {
            abortIoCount++;
            return abortIoCount == 1
                ? new InvalidOperationException("first-abort-io-failure")
                : null;
        },
        () => disposeCount++,
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult());

    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        () => session.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
    Equal(1, terminateCount);
    Equal(1, abortIoCount);
    Equal(0, disposeCount);

    session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(1, terminateCount);
    Equal(2, abortIoCount);
    Equal(1, disposeCount);
    session.Dispose();
}

static void ServiceStopThrownAbortIoCanRetry()
{
    var abortIoCount = 0;
    var disposeCount = 0;
    var session = new AgentHostServiceSession(
        _ => true,
        () =>
        {
            abortIoCount++;
            if (abortIoCount == 1)
            {
                throw new InvalidOperationException("first-abort-io-throw");
            }

            return null;
        },
        () => disposeCount++,
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult());

    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        () => session.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
    Equal(1, abortIoCount);
    Equal(0, disposeCount);

    session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(2, abortIoCount);
    Equal(1, disposeCount);
    session.Dispose();
}

static void ServiceStopStandardErrorCanRetry()
{
    var terminateCount = 0;
    var abortIoCount = 0;
    var disposeCount = 0;
    var standardError = new TaskCompletionSource<AgentHostStandardErrorCapture>();
    var session = new AgentHostServiceSession(
        _ =>
        {
            terminateCount++;
            return true;
        },
        () =>
        {
            abortIoCount++;
            return null;
        },
        () => disposeCount++,
        standardError.Task,
        CreateServiceResult());

    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        () => session.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
    Equal(1, terminateCount);
    Equal(1, abortIoCount);
    Equal(1, disposeCount);

    standardError.TrySetResult(new AgentHostStandardErrorCapture(7, true));
    session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(1, terminateCount);
    Equal(1, abortIoCount);
    Equal(1, disposeCount);
    Equal(7, session.StandardErrorBytes);
    Equal(true, session.StandardErrorTruncated);
    session.Dispose();
}

static void ServiceStopFaultedStandardErrorIsSettled()
{
    var terminateCount = 0;
    var abortIoCount = 0;
    var disposeCount = 0;
    var standardError = new TaskCompletionSource<AgentHostStandardErrorCapture>();
    var session = new AgentHostServiceSession(
        _ =>
        {
            terminateCount++;
            return true;
        },
        () =>
        {
            abortIoCount++;
            standardError.TrySetException(
                new IOException("simulated stderr abort completion"));
            return null;
        },
        () => disposeCount++,
        standardError.Task,
        CreateServiceResult());

    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        () => session.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
    Equal(1, terminateCount);
    Equal(1, abortIoCount);
    Equal(1, disposeCount);

    session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(1, terminateCount);
    Equal(1, abortIoCount);
    Equal(1, disposeCount);
    Equal(0, session.StandardErrorBytes);
    Equal(false, session.StandardErrorTruncated);
    session.Dispose();
}

static void ServiceStopRetryDoesNotPoisonStart()
{
    var terminateCount = 0;
    var session = new AgentHostServiceSession(
        _ =>
        {
            terminateCount++;
            return terminateCount > 1;
        },
        () => null,
        () => { },
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult());

    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        () => session.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
    session.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

    const string placeholderSha256 =
        "0000000000000000000000000000000000000000000000000000000000000000";
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => AgentHostBootstrapService.StartAsync(
                new AgentHostBootstrapOptions("relative.exe", placeholderSha256),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    session.Dispose();
}

static void ServiceDisposeFailureCanRetry()
{
    var disposeCount = 0;
    var session = new AgentHostServiceSession(
        _ => true,
        () => null,
        () =>
        {
            disposeCount++;
            if (disposeCount == 1)
            {
                throw new InvalidOperationException("first-service-dispose-failure");
            }
        },
        Task.FromResult(new AgentHostStandardErrorCapture(0, false)),
        CreateServiceResult());

    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildTerminationFailed,
        session.Dispose);
    Equal(1, disposeCount);

    session.Dispose();
    Equal(2, disposeCount);
    session.Dispose();
    Equal(2, disposeCount);
}

static AgentBootstrapDoctorResult CreateServiceResult()
{
    return new AgentBootstrapDoctorResult(
        1234,
        5678,
        "0123456789abcdef0123456789abcdef",
        "fedcba9876543210fedcba9876543210",
        "codex-autocad-test",
        new string('A', 64),
        0,
        false);
}

static void InvalidExecutablePathsFailClosed(FakeAgentHostFixture fixture)
{
    const string validPlaceholderSha256 = "0000000000000000000000000000000000000000000000000000000000000000";
    var nonExecutablePath = fixture.CreateNonExecutable();
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => Run(new AgentHostBootstrapOptions("relative.exe", validPlaceholderSha256)));
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => Run(new AgentHostBootstrapOptions("\\\\server\\share\\AgentHost.exe", validPlaceholderSha256)));
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => Run(new AgentHostBootstrapOptions(
            nonExecutablePath,
            ComputeFileSha256(nonExecutablePath))));
    ExpectFailure(
        AgentBootstrapLaunchFailure.InvalidConfiguration,
        () => Run(new AgentHostBootstrapOptions(
            Path.Combine(Path.GetTempPath(), "missing.exe"),
            validPlaceholderSha256)));
}

static void TimeoutTerminatesChild(string fakePath)
{
    var options = CreateOptions(fakePath);
    options.StartupTimeout = TimeSpan.FromMilliseconds(350);
    var stopwatch = Stopwatch.StartNew();
    ExpectFailure(AgentBootstrapLaunchFailure.Timeout, () => Run(options));
    stopwatch.Stop();
    True(
        stopwatch.Elapsed < TimeSpan.FromSeconds(8),
        "Timeout cleanup exceeded its bounded deadline: " + stopwatch.Elapsed + ".");
    ProcessNameMustBeGone(fakePath);
}

static void ExecutableSha256MismatchFails(string fakePath)
{
    const string mismatchedSha256 = "0000000000000000000000000000000000000000000000000000000000000000";
    var actualSha256 = ComputeFileSha256(fakePath);
    True(
        !string.Equals(actualSha256, mismatchedSha256, StringComparison.Ordinal),
        "The fake AgentHost unexpectedly matched the mismatch sentinel.");
    ExpectFailure(
        AgentBootstrapLaunchFailure.IdentityMismatch,
        () => Run(new AgentHostBootstrapOptions(fakePath, mismatchedSha256)));
    ProcessNameMustBeGone(fakePath);
}

static void ValidConfirmationThenHangTerminatesChild(string fakePath)
{
    var options = CreateOptions(fakePath);
    options.StartupTimeout = TimeSpan.FromMilliseconds(500);
    var stopwatch = Stopwatch.StartNew();
    ExpectFailure(AgentBootstrapLaunchFailure.Timeout, () => Run(options));
    stopwatch.Stop();
    True(
        stopwatch.Elapsed < TimeSpan.FromSeconds(8),
        "Confirmation-then-hang cleanup exceeded its bounded deadline: "
            + stopwatch.Elapsed + ".");
    ProcessNameMustBeGone(fakePath);
}

static void CancellationTerminatesChild(string fakePath)
{
    var options = CreateOptions(fakePath);
    options.StartupTimeout = TimeSpan.FromSeconds(10);
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.CancelAfter(250);
        var stopwatch = Stopwatch.StartNew();
        ExpectFailure(
            AgentBootstrapLaunchFailure.Cancellation,
            () => AgentHostBootstrapDoctor.RunAsync(options, cancellation.Token)
                .GetAwaiter()
                .GetResult());
        stopwatch.Stop();
        True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(8),
            "Cancellation cleanup exceeded its bounded deadline: " + stopwatch.Elapsed + ".");
    }

    ProcessNameMustBeGone(fakePath);
}

static void CallerThreadIsNotBlocked(string fakePath)
{
    var options = CreateOptions(fakePath);
    options.StartupTimeout = TimeSpan.FromSeconds(10);
    using (var cancellation = new CancellationTokenSource())
    {
        var stopwatch = Stopwatch.StartNew();
        var launchTask = AgentHostBootstrapDoctor.RunAsync(options, cancellation.Token);
        stopwatch.Stop();
        True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(250),
            "RunAsync synchronously blocked the caller for " + stopwatch.Elapsed + ".");
        cancellation.Cancel();
        ExpectFailure(
            AgentBootstrapLaunchFailure.Cancellation,
            () => launchTask.GetAwaiter().GetResult());
    }

    ProcessNameMustBeGone(fakePath);
}

static void EarlyExitIsReported(string fakePath)
{
    ExpectFailure(
        AgentBootstrapLaunchFailure.ChildExitedWithError,
        () => Run(CreateOptions(fakePath)));
    ProcessNameMustBeGone(fakePath);
}

static void MalformedConfirmationFails(string fakePath)
{
    ExpectFailure(
        AgentBootstrapLaunchFailure.ConfirmationInvalid,
        () => Run(CreateOptions(fakePath)));
    ProcessNameMustBeGone(fakePath);
}

static void IdentityMismatchFails(string fakePath)
{
    ExpectFailure(
        AgentBootstrapLaunchFailure.IdentityMismatch,
        () => Run(CreateOptions(fakePath)));
    ProcessNameMustBeGone(fakePath);
}

static void TrailingAndDuplicateConfirmationFail(FakeAgentHostFixture fixture)
{
    foreach (var mode in new[] { "trailing", "double" })
    {
        var fakePath = fixture.CreateMode(mode);
        ExpectFailure(
            AgentBootstrapLaunchFailure.ConfirmationInvalid,
            () => Run(CreateOptions(fakePath)));
        ProcessNameMustBeGone(fakePath);
    }
}

static void ChildClearsInheritance(string fakePath)
{
    var result = Run(CreateOptions(fakePath));
    ProcessMustBeGone(result.ProcessId);
}

static void HandleAllowListExcludesCanary(string fakePath)
{
    const string handleVariable = "CODEX_AUTOCAD_TEST_CANARY_HANDLE";
    const string pathVariable = "CODEX_AUTOCAD_TEST_CANARY_PATH";
    var previousHandle = Environment.GetEnvironmentVariable(handleVariable);
    var previousPath = Environment.GetEnvironmentVariable(pathVariable);
    using (var canary = new InheritableCanaryFile())
    {
        try
        {
            Environment.SetEnvironmentVariable(
                handleVariable,
                canary.HandleValue.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable(pathVariable, canary.Path);
            var result = Run(CreateOptions(fakePath));
            ProcessMustBeGone(result.ProcessId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(handleVariable, previousHandle);
            Environment.SetEnvironmentVariable(pathVariable, previousPath);
        }
    }
}

static void StandardErrorIsBounded(FakeAgentHostFixture fixture)
{
    var fakePath = fixture.CreateMode("stderr");
    var options = CreateOptions(fakePath);
    options.MaximumStandardErrorBytes = 1024;
    var result = Run(options);
    Equal(1024, result.StandardErrorBytes);
    True(result.StandardErrorTruncated, "stderr truncation was not reported.");
    ProcessMustBeGone(result.ProcessId);

    const string marker = "CODEX_RAW_STDERR_MUST_NOT_ESCAPE";
    var failingPath = fixture.CreateMode("stderrfail");
    var failingOptions = CreateOptions(failingPath);
    failingOptions.MaximumStandardErrorBytes = 64;
    var failure = ExpectFailure(
        AgentBootstrapLaunchFailure.ChildExitedWithError,
        () => Run(failingOptions));
    True(
        failure.Message.IndexOf("stderrBytes=64", StringComparison.Ordinal) >= 0,
        "The bounded stderr byte count was not reported.");
    True(
        failure.Message.IndexOf("stderrTruncated=true", StringComparison.Ordinal) >= 0,
        "The stderr truncation flag was not reported.");
    True(
        failure.ToString().IndexOf(marker, StringComparison.Ordinal) < 0,
        "Raw stderr escaped through the public failure.");
    ProcessNameMustBeGone(failingPath);
}

static AgentHostBootstrapOptions CreateOptions(string executablePath)
{
    return new AgentHostBootstrapOptions(
        executablePath,
        ComputeFileSha256(executablePath));
}

static string ComputeFileSha256(string path)
{
    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
    using (var sha256 = SHA256.Create())
    {
        var hash = sha256.ComputeHash(input);
        try
        {
            var characters = new char[hash.Length * 2];
            const string hex = "0123456789ABCDEF";
            for (var index = 0; index < hash.Length; index++)
            {
                characters[index * 2] = hex[hash[index] >> 4];
                characters[index * 2 + 1] = hex[hash[index] & 0x0f];
            }

            return new string(characters);
        }
        finally
        {
            Array.Clear(hash, 0, hash.Length);
        }
    }
}

static AgentBootstrapDoctorResult Run(AgentHostBootstrapOptions options)
{
    return AgentHostBootstrapDoctor.RunAsync(options, CancellationToken.None)
        .GetAwaiter()
        .GetResult();
}

static AgentBootstrapLaunchException ExpectFailure(
    AgentBootstrapLaunchFailure expected,
    Action action)
{
    try
    {
        action();
        throw new InvalidOperationException("Expected launch failure " + expected + ".");
    }
    catch (AgentBootstrapLaunchException exception)
    {
        Equal(expected, exception.Failure);
        return exception;
    }
}

static void ProcessMustBeGone(int processId)
{
    try
    {
        using (var process = Process.GetProcessById(processId))
        {
            if (!process.HasExited)
            {
                throw new InvalidOperationException("AgentHost process is still running: " + processId + ".");
            }
        }
    }
    catch (ArgumentException)
    {
    }
}

static void ProcessNameMustBeGone(string executablePath)
{
    var processName = Path.GetFileNameWithoutExtension(executablePath);
    var deadline = DateTime.UtcNow.AddSeconds(2);
    do
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            if (processes.Length == 0)
            {
                return;
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        Thread.Sleep(25);
    } while (DateTime.UtcNow < deadline);

    throw new InvalidOperationException("Fake AgentHost process remains: " + processName + ".");
}

static int ReadProcessIdFile(string path)
{
    var deadline = DateTime.UtcNow.AddSeconds(3);
    do
    {
        try
        {
            var text = File.ReadAllText(path).Trim();
            int processId;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out processId)
                && processId > 0)
            {
                return processId;
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (IOException)
        {
        }

        Thread.Sleep(25);
    } while (DateTime.UtcNow < deadline);

    throw new InvalidOperationException("The process-tree test descendant did not publish its process id.");
}

static void WaitForProcessToExit(int processId, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow.Add(timeout);
    do
    {
        try
        {
            using (var process = Process.GetProcessById(processId))
            {
                if (process.HasExited)
                {
                    return;
                }
            }
        }
        catch (ArgumentException)
        {
            return;
        }
        Thread.Sleep(25);
    } while (DateTime.UtcNow < deadline);

    throw new InvalidOperationException(
        "Expected process did not exit inside the process-tree Job cleanup deadline: " + processId + ".");
}

static void WaitForCondition(Func<bool> condition, TimeSpan timeout, string failureMessage)
{
    var deadline = DateTime.UtcNow.Add(timeout);
    do
    {
        if (condition())
        {
            return;
        }

        Thread.Sleep(25);
    } while (DateTime.UtcNow < deadline);

    if (!condition())
    {
        throw new InvalidOperationException(failureMessage);
    }
}

static void KillFixtureProcessIfStillRunning(int processId, string executablePath)
{
    if (processId <= 0)
    {
        return;
    }

    try
    {
        using (var process = Process.GetProcessById(processId))
        {
            if (process.HasExited
                || !string.Equals(
                    process.ProcessName,
                    Path.GetFileNameWithoutExtension(executablePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            process.Kill();
            process.WaitForExit(2000);
        }
    }
    catch (ArgumentException)
    {
    }
    catch (InvalidOperationException)
    {
    }
}

static void DeleteFileIfPresent(string path)
{
    try
    {
        File.Delete(path);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

static Arguments ParseArguments(string[] values)
{
    string? agentHost = null;
    string? fakeAgentHost = null;
    for (var index = 0; index < values.Length - 1; index += 2)
    {
        if (string.Equals(values[index], "--agent-host", StringComparison.Ordinal))
        {
            agentHost = values[index + 1];
        }
        else if (string.Equals(values[index], "--fake-agent-host", StringComparison.Ordinal))
        {
            fakeAgentHost = values[index + 1];
        }
    }

    if (string.IsNullOrWhiteSpace(agentHost) || string.IsNullOrWhiteSpace(fakeAgentHost))
    {
        throw new ArgumentException("--agent-host and --fake-agent-host are required.");
    }

    return new Arguments(Path.GetFullPath(agentHost), Path.GetFullPath(fakeAgentHost));
}

static string GetRequiredOption(string[] values, string option)
{
    for (var index = 0; index < values.Length - 1; index++)
    {
        if (string.Equals(values[index], option, StringComparison.Ordinal))
        {
            var value = values[index + 1];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Path.GetFullPath(value);
            }
        }
    }

    throw new ArgumentException("Missing required helper option: " + option);
}

static string QuoteCommandLineArgument(string value)
{
    return "\"" + value.Replace("\"", "\\\"") + "\"";
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException("Expected " + expected + ", actual " + actual + ".");
    }
}

sealed class FakeAgentHostFixture : IDisposable
{
    private readonly string sourceExecutable;
    private readonly string root;
    private readonly Dictionary<string, string> modes = new Dictionary<string, string>(StringComparer.Ordinal);
    private string? nonExecutablePath;

    internal FakeAgentHostFixture(string sourceExecutable)
    {
        this.sourceExecutable = Path.GetFullPath(sourceExecutable);
        root = Path.Combine(Path.GetTempPath(), "CodexAgentLauncherSpecs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var source in Directory.GetFiles(Path.GetDirectoryName(this.sourceExecutable)!))
        {
            File.Copy(source, Path.Combine(root, Path.GetFileName(source)), true);
        }
    }

    internal string CreateMode(string mode)
    {
        string? existing;
        if (modes.TryGetValue(mode, out existing))
        {
            return existing!;
        }

        var target = Path.Combine(root, "CodexLauncherFake-" + mode + ".exe");
        File.Copy(sourceExecutable, target, true);
        modes.Add(mode, target);
        return target;
    }

    internal string CreateNonExecutable()
    {
        if (nonExecutablePath != null)
        {
            return nonExecutablePath;
        }

        nonExecutablePath = Path.Combine(root, "CodexLauncherNotExecutable.dll");
        File.WriteAllBytes(nonExecutablePath, new byte[] { 0x43, 0x44, 0x58, 0x00 });
        return nonExecutablePath;
    }

    public void Dispose()
    {
        foreach (var path in modes.Values)
        {
            EnsureProcessNameIsGone(path);
        }

        if (Directory.Exists(root))
        {
            DeleteDirectoryWithRetry(root);
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastFailure = null;
        do
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException exception)
            {
                lastFailure = exception;
            }
            catch (UnauthorizedAccessException exception)
            {
                lastFailure = exception;
            }

            Thread.Sleep(50);
        } while (stopwatch.Elapsed < TimeSpan.FromSeconds(5));

        throw new IOException(
            "Fake AgentHost fixture directory cleanup exceeded its bounded retry window.",
            lastFailure);
    }

    private static void EnsureProcessNameIsGone(string executablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        do
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length == 0)
                {
                    return;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }

            Thread.Sleep(25);
        } while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException("Fake AgentHost process remains: " + processName + ".");
    }
}

sealed class InheritableCanaryFile : IDisposable
{
    private const uint HandleFlagInherit = 1;
    private readonly FileStream stream;

    internal InheritableCanaryFile()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CodexAgentLauncherCanary-" + Guid.NewGuid().ToString("N") + ".tmp");
        stream = new FileStream(
            Path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        if (!SetHandleInformation(
                stream.SafeFileHandle,
                HandleFlagInherit,
                HandleFlagInherit))
        {
            throw new InvalidOperationException(
                "Making the parent canary handle inheritable failed.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    internal string Path { get; }

    internal long HandleValue
    {
        get { return stream.SafeFileHandle.DangerousGetHandle().ToInt64(); }
    }

    public void Dispose()
    {
        stream.Dispose();
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(
        SafeFileHandle handle,
        uint mask,
        uint flags);
}

sealed class Arguments
{
    internal Arguments(string agentHostPath, string fakeAgentHostPath)
    {
        AgentHostPath = agentHostPath;
        FakeAgentHostPath = fakeAgentHostPath;
    }

    internal string AgentHostPath { get; }

    internal string FakeAgentHostPath { get; }
}

sealed class SpecCase
{
    internal SpecCase(string id, string name, Action run)
    {
        Id = id;
        Name = name;
        Run = run;
    }

    internal string Id { get; }

    internal string Name { get; }

    internal Action Run { get; }
}
