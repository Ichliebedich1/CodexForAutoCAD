using System.Diagnostics;
using System.Security.Cryptography;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.Ipc;

var fakeAgentHostPath = ParseFakeAgentHostPath(args);
var fixture = new FakeAgentHostFixture(fakeAgentHostPath);
try
{
    var specs = new[]
    {
        new SpecCase(
            "SERVICE_STAYS_ALIVE_AFTER_CONFIRMATION",
            "bootstrap-serve确认后保持运行直到Host显式停止",
            () => ServiceStaysAliveAfterConfirmation(fixture.CreateMode("success"))),
        new SpecCase(
            "BRIDGE_KEYS_REMAIN_CLAIMABLE_ONCE",
            "确认专用密钥不消费Bridge方向密钥且Host只能领取一次",
            () => BridgeKeysRemainClaimableOnce(fixture.CreateMode("success"))),
        new SpecCase(
            "CONCURRENT_STOP_IS_BOUNDED",
            "并发Stop幂等且在有界窗口内证明子进程退出",
            () => ConcurrentStopIsBounded(fixture.CreateMode("success"))),
        new SpecCase(
            "DISPOSE_TERMINATES_SERVICE",
            "Dispose终止未显式停止的长运行AgentHost且无残留",
            () => DisposeTerminatesService(fixture.CreateMode("success"))),
        new SpecCase(
            "STARTUP_TIMEOUT_TERMINATES_SERVICE",
            "确认前超时按fail-closed终止子进程",
            () => StartupTimeoutTerminatesService(fixture.CreateMode("hang"))),
        new SpecCase(
            "STARTUP_CANCELLATION_TERMINATES_SERVICE",
            "确认前取消按fail-closed终止子进程",
            () => StartupCancellationTerminatesService(fixture.CreateMode("hang"))),
        new SpecCase(
            "SERVICE_STDERR_IS_BOUNDED",
            "长运行服务stderr持续排空且仅保留限界计数",
            () => ServiceStandardErrorIsBounded(fixture.CreateMode("stderr"))),
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

static void ServiceStaysAliveAfterConfirmation(string fakePath)
{
    using (var session = Start(CreateOptions(fakePath)))
    {
        True(session.ProcessId > 0, "Process id was not captured.");
        Equal(32, session.BootstrapId.Length);
        Equal(32, session.SessionId.Length);
        True(
            session.PipeName.StartsWith("codex-autocad-", StringComparison.Ordinal),
            "Pipe name is invalid.");
        ProcessMustBeAlive(session.ProcessId);
        session.StopAsync().GetAwaiter().GetResult();
        ProcessMustBeGone(session.ProcessId);
    }
}

static void BridgeKeysRemainClaimableOnce(string fakePath)
{
    using (var session = Start(CreateOptions(fakePath)))
    {
        using (var keys = session.ClaimDirectionKeys())
        {
            Equal(session.SessionId, keys.SessionId);
            Equal(session.PipeName, keys.PipeName);
            using (var outbound = keys.CreateOutboundAuthenticator())
            using (var inbound = keys.CreateInboundGuard())
            {
                True(outbound != null, "Host outbound authenticator was not created.");
                True(inbound != null, "Host inbound guard was not created.");
            }
        }

        try
        {
            session.ClaimDirectionKeys();
            throw new InvalidOperationException("Direction keys were claimable twice.");
        }
        catch (AgentBootstrapException exception)
        {
            Equal(AgentBootstrapValidationCode.AlreadyConsumed, exception.ValidationCode);
        }

        session.StopAsync().GetAwaiter().GetResult();
        ProcessMustBeGone(session.ProcessId);
    }
}

static void ConcurrentStopIsBounded(string fakePath)
{
    using (var session = Start(CreateOptions(fakePath)))
    {
        ProcessMustBeAlive(session.ProcessId);
        var stopwatch = Stopwatch.StartNew();
        Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => session.StopAsync())
                    .ToArray())
            .GetAwaiter()
            .GetResult();
        stopwatch.Stop();
        True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(8),
            "Concurrent stop exceeded its bounded window: " + stopwatch.Elapsed + ".");
        session.StopAsync().GetAwaiter().GetResult();
        ProcessMustBeGone(session.ProcessId);
    }
}

static void DisposeTerminatesService(string fakePath)
{
    var session = Start(CreateOptions(fakePath));
    var processId = session.ProcessId;
    ProcessMustBeAlive(processId);
    session.Dispose();
    session.Dispose();
    ProcessMustBeGone(processId);
}

static void StartupTimeoutTerminatesService(string fakePath)
{
    var options = CreateOptions(fakePath);
    options.StartupTimeout = TimeSpan.FromMilliseconds(350);
    var stopwatch = Stopwatch.StartNew();
    ExpectFailure(
        AgentBootstrapLaunchFailure.Timeout,
        () => Start(options));
    stopwatch.Stop();
    True(
        stopwatch.Elapsed < TimeSpan.FromSeconds(8),
        "Service timeout cleanup exceeded its bounded window: " + stopwatch.Elapsed + ".");
    ProcessNameMustBeGone(fakePath);
}

static void StartupCancellationTerminatesService(string fakePath)
{
    var options = CreateOptions(fakePath);
    options.StartupTimeout = TimeSpan.FromSeconds(10);
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.CancelAfter(250);
        var stopwatch = Stopwatch.StartNew();
        ExpectFailure(
            AgentBootstrapLaunchFailure.Cancellation,
            () => AgentHostBootstrapService.StartAsync(options, cancellation.Token)
                .GetAwaiter()
                .GetResult());
        stopwatch.Stop();
        True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(8),
            "Service cancellation cleanup exceeded its bounded window: "
                + stopwatch.Elapsed
                + ".");
    }

    ProcessNameMustBeGone(fakePath);
}

static void ServiceStandardErrorIsBounded(string fakePath)
{
    var options = CreateOptions(fakePath);
    options.MaximumStandardErrorBytes = 1024;
    using (var session = Start(options))
    {
        session.StopAsync().GetAwaiter().GetResult();
        Equal(1024, session.StandardErrorBytes);
        True(session.StandardErrorTruncated, "stderr truncation was not reported.");
        ProcessMustBeGone(session.ProcessId);
    }
}

static AgentHostServiceSession Start(AgentHostBootstrapOptions options)
{
    return AgentHostBootstrapService.StartAsync(options, CancellationToken.None)
        .GetAwaiter()
        .GetResult();
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
            const string hex = "0123456789ABCDEF";
            var characters = new char[hash.Length * 2];
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

static void ProcessMustBeAlive(int processId)
{
    using (var process = Process.GetProcessById(processId))
    {
        True(!process.HasExited, "AgentHost service exited before explicit stop.");
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
                throw new InvalidOperationException(
                    "AgentHost service process is still running: " + processId + ".");
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

    throw new InvalidOperationException("Fake AgentHost service remains: " + processName + ".");
}

static string ParseFakeAgentHostPath(string[] values)
{
    for (var index = 0; index < values.Length - 1; index += 2)
    {
        if (string.Equals(values[index], "--fake-agent-host", StringComparison.Ordinal))
        {
            return Path.GetFullPath(values[index + 1]);
        }
    }

    throw new ArgumentException("--fake-agent-host is required.");
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
        throw new InvalidOperationException(
            "Expected " + expected + ", actual " + actual + ".");
    }
}

sealed class FakeAgentHostFixture : IDisposable
{
    private readonly string sourceExecutable;
    private readonly string root;
    private readonly Dictionary<string, string> modes =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal FakeAgentHostFixture(string sourceExecutable)
    {
        this.sourceExecutable = Path.GetFullPath(sourceExecutable);
        root = Path.Combine(
            Path.GetTempPath(),
            "CodexAgentServiceSpecs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var source in Directory.GetFiles(Path.GetDirectoryName(this.sourceExecutable)!))
        {
            File.Copy(source, Path.Combine(root, Path.GetFileName(source)), true);
        }
    }

    internal string CreateMode(string mode)
    {
        if (modes.TryGetValue(mode, out var existing))
        {
            return existing;
        }

        var target = Path.Combine(root, "CodexAgentServiceFake-" + mode + ".exe");
        File.Copy(sourceExecutable, target, true);
        modes.Add(mode, target);
        return target;
    }

    public void Dispose()
    {
        foreach (var path in modes.Values)
        {
            EnsureProcessNameIsGone(path);
        }

        if (Directory.Exists(root))
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            Exception? lastFailure = null;
            do
            {
                try
                {
                    Directory.Delete(root, true);
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
            } while (DateTime.UtcNow < deadline);

            throw new IOException(
                "Fake AgentHost service fixture cleanup exceeded its bounded retry window.",
                lastFailure);
        }
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

        throw new InvalidOperationException(
            "Fake AgentHost service remains: " + processName + ".");
    }
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
