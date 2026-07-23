using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Codex.AutoCAD.AppServer;

var specs = new (string Name, Func<Task> Run)[]
{
    ("分片JSONL帧可重组", FragmentedFrameIsReassembled),
    ("超限帧被拒绝", OversizedFrameFails),
    ("未终止帧被拒绝", UnterminatedFrameFails),
    ("initialize握手完成", InitializeHandshakeCompletes),
    ("乱序响应仍按请求关联", OutOfOrderResponsesAreCorrelated),
    ("无处理器的命令审批默认拒绝", CommandApprovalDefaultsToDecline),
    ("通知被分发", NotificationIsDispatched),
    ("stderr只保留有界无内容摘要", StandardErrorIsDrainedWithoutText),
    ("进程退出等待完整stderr摘要", ProcessExitPublishesCompletedStandardErrorSummary),
    ("隔离子进程看不到父环境哨兵", IsolatedChildDoesNotInheritParentEnvironment),
    ("环境null覆写删除继承变量", NullEnvironmentOverrideRemovesInheritedVariable),
    ("非法环境键值启动前被拒绝", InvalidEnvironmentEntriesAreRejected),
    ("本地Codex配置只接受固定盘绝对exe", LocalCodexConfigurationAcceptsConfiguredExecutable),
    ("本地Codex配置强制使用环境白名单", LocalCodexConfigurationUsesEnvironmentAllowlist),
    ("本地Codex配置错误不泄露路径", LocalCodexConfigurationFailsClosedWithoutPath),
    ("无效环境Codex路径不会回退", LocalCodexConfigurationDoesNotFallbackFromInvalidEnvironment),
    ("缺失本地Codex配置返回稳定错误", LocalCodexConfigurationReportsMissingExecutable),
    ("本地Codex可从绝对PATH发现", LocalCodexConfigurationDiscoversAbsolutePath),
    ("无效临时目录返回稳定错误", LocalCodexConfigurationRejectsInvalidTemporaryDirectory),
    ("stderr限额无效时被拒绝", StandardErrorLimitIsValidated),
    ("Codex版本格式与兼容范围固定", CodexVersionFormatAndCompatibilityAreFrozen),
    ("Codex版本预检使用隔离环境", CodexVersionPreflightUsesIsolatedEnvironment),
    ("不支持Codex版本fail-closed", UnsupportedCodexVersionFailsClosed),
    ("Codex版本子进程退出错误fail-closed", CodexVersionProcessExitFailsClosed),
    ("超限Codex版本输出fail-closed", OversizedCodexVersionOutputFailsClosed),
    ("非UTF8 Codex版本输出fail-closed", NonUtf8CodexVersionOutputFailsClosed),
    ("Codex版本预检超时fail-closed", CodexVersionPreflightTimeoutFailsClosed)
};

var failed = 0;
foreach (var spec in specs)
{
    try
    {
        await spec.Run();
        Console.WriteLine("PASS " + spec.Name);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + spec.Name + ": " + exception);
    }
}

Console.WriteLine($"{specs.Length - failed}/{specs.Length} specs passed");
return failed == 0 ? 0 : 1;

static async Task FragmentedFrameIsReassembled()
{
    await using var stream = new FragmentedReadStream("{\"id\":1", ",\"result\":{}}\r\n");
    var reader = new JsonLineFrameReader(stream, 1024, readBufferBytes: 4);
    Equal("{\"id\":1,\"result\":{}}", await reader.ReadFrameAsync(CancellationToken.None));
    Equal<string?>(null, await reader.ReadFrameAsync(CancellationToken.None));
}

static async Task OversizedFrameFails()
{
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 33) + "\n"));
    var reader = new JsonLineFrameReader(stream, maximumFrameBytes: 32);
    await ThrowsAsync<AppServerProtocolException>(() => reader.ReadFrameAsync(CancellationToken.None).AsTask());
}

static async Task UnterminatedFrameFails()
{
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":1}"));
    var reader = new JsonLineFrameReader(stream, maximumFrameBytes: 128);
    await ThrowsAsync<AppServerProtocolException>(() => reader.ReadFrameAsync(CancellationToken.None).AsTask());
}

static async Task InitializeHandshakeCompletes()
{
    await using var fixture = await ClientFixture.StartAsync();
    Equal(AppServerClientState.Running, fixture.Client.State);
    Equal("windows", fixture.Client.InitializeResponse?.PlatformFamily);
    True(fixture.Frames.Any(frame => Method(frame) == "initialized"), "客户端必须发送initialized通知。");
}

static async Task OutOfOrderResponsesAreCorrelated()
{
    await using var fixture = await ClientFixture.StartAsync();
    var requests = new List<(long Id, string Method)>();
    var sync = new object();

    fixture.Transport.FrameWritten += frame =>
    {
        var method = Method(frame);
        if (method is not ("test/first" or "test/second"))
        {
            return;
        }

        lock (sync)
        {
            requests.Add((Id(frame), method));
            if (requests.Count == 2)
            {
                var first = requests.Single(item => item.Method == "test/first");
                var second = requests.Single(item => item.Method == "test/second");
                fixture.Transport.Inject($"{{\"id\":{second.Id},\"result\":{{\"value\":2}}}}");
                fixture.Transport.Inject($"{{\"id\":{first.Id},\"result\":{{\"value\":1}}}}");
            }
        }
    };

    var firstTask = fixture.Client.SendRequestAsync<TestResult>("test/first");
    var secondTask = fixture.Client.SendRequestAsync<TestResult>("test/second");
    Equal(1, (await firstTask).Value);
    Equal(2, (await secondTask).Value);
}

static async Task CommandApprovalDefaultsToDecline()
{
    await using var fixture = await ClientFixture.StartAsync();
    var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Transport.FrameWritten += frame =>
    {
        if (frame.RootElement.TryGetProperty("id", out var id) && id.TryGetInt64(out var value) && value == 700
            && frame.RootElement.TryGetProperty("result", out var result))
        {
            response.TrySetResult(result.GetProperty("decision").GetString() ?? string.Empty);
        }
    };

    fixture.Transport.Inject("""
        {"id":700,"method":"item/commandExecution/requestApproval","params":{"itemId":"item-1","startedAtMs":1,"threadId":"thread-1","turnId":"turn-1","command":"whoami","cwd":"C:\\work"}}
        """);

    Equal("decline", await response.Task.WaitAsync(TimeSpan.FromSeconds(5)));
}

static async Task NotificationIsDispatched()
{
    await using var fixture = await ClientFixture.StartAsync();
    var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Client.NotificationReceived += (_, notification) => received.TrySetResult(notification.Method);
    fixture.Transport.Inject("{\"method\":\"turn/started\",\"params\":{\"turnId\":\"turn-1\"}}");
    Equal("turn/started", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
}

static async Task StandardErrorIsDrainedWithoutText()
{
    var raw = Encoding.UTF8.GetBytes("secret-line-" + new string('x', 2_048));
    await using var input = new MemoryStream(raw, writable: false);
    var summary = await AppServerStandardErrorCapture.DrainAsync(input, maximumBytes: 1_024);

    Equal(1_024, summary.Bytes);
    True(summary.Truncated, "stderr summary did not report truncation.");
    True(
        typeof(AppServerStandardErrorSummary).GetProperties()
            .All(property => property.PropertyType != typeof(string)),
        "stderr summary unexpectedly exposes text.");
}

static async Task ProcessExitPublishesCompletedStandardErrorSummary()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "codex-autocad-appserver-spec-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var payloadPath = Path.Combine(directory, "stderr-payload.txt");
        var scriptPath = Path.Combine(directory, "stderr-child.cmd");
        File.WriteAllText(payloadPath, new string('x', 32 * 1024), Encoding.ASCII);
        File.WriteAllText(
            scriptPath,
            "@echo off\r\ntype \"%~dp0stderr-payload.txt\" 1>&2\r\nexit /b 37\r\n",
            Encoding.ASCII);

        await using (var transport = new CodexProcessTransport(new AppServerClientOptions
        {
            CodexExecutablePath = scriptPath,
            WorkingDirectory = directory,
            MaximumStandardErrorBytes = 1_024,
        }))
        {
            var exited = new TaskCompletionSource<AppServerTransportExitedEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            transport.Exited += (_, eventArgs) => exited.TrySetResult(eventArgs);

            await transport.StartAsync();
            var actual = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Equal<int?>(37, actual.ExitCode);
            Equal(1, actual.StandardErrorTail.Count);
            Equal(1_024, actual.StandardErrorTail[0].Bytes);
            True(actual.StandardErrorTail[0].Truncated, "Exit event did not retain the bounded stderr summary.");
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task IsolatedChildDoesNotInheritParentEnvironment()
{
    const string parentVariable = "CODEX_AUTOCAD_TEST_PARENT_SENTINEL";
    const string allowedVariable = "CODEX_AUTOCAD_TEST_EXPLICIT_ALLOWED";
    var previousParentValue = Environment.GetEnvironmentVariable(parentVariable);
    var directory = CreateTemporaryDirectory("environment-isolation");
    try
    {
        Environment.SetEnvironmentVariable(parentVariable, "parent-marker");
        var environment = new Dictionary<string, string?>(
            CodexChildEnvironmentPolicy.CreateForCurrentProcess(directory),
            StringComparer.OrdinalIgnoreCase)
        {
            [allowedVariable] = "allow-marker",
        };
        var scriptPath = WriteEnvironmentProbe(
            directory,
            "if defined " + parentVariable + " (echo parent=present) else (echo parent=absent)",
            "if \"%" + allowedVariable + "%\"==\"allow-marker\" (echo allowed=present) else (echo allowed=absent)");

        var output = await RunProcessProbeAsync(new AppServerClientOptions
        {
            CodexExecutablePath = scriptPath,
            WorkingDirectory = directory,
            Environment = environment,
            InheritParentEnvironment = false,
        });

        ContainsLine(output, "parent=absent");
        ContainsLine(output, "allowed=present");
    }
    finally
    {
        Environment.SetEnvironmentVariable(parentVariable, previousParentValue);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task NullEnvironmentOverrideRemovesInheritedVariable()
{
    const string variable = "CODEX_AUTOCAD_TEST_NULL_REMOVAL";
    var previousValue = Environment.GetEnvironmentVariable(variable);
    var directory = CreateTemporaryDirectory("environment-null-removal");
    try
    {
        Environment.SetEnvironmentVariable(variable, "parent-marker");
        var scriptPath = WriteEnvironmentProbe(
            directory,
            "if defined " + variable + " (echo inherited=present) else (echo inherited=absent)");

        var output = await RunProcessProbeAsync(new AppServerClientOptions
        {
            CodexExecutablePath = scriptPath,
            WorkingDirectory = directory,
            Environment = new Dictionary<string, string?>
            {
                [variable] = null,
            },
        });

        ContainsLine(output, "inherited=absent");
    }
    finally
    {
        Environment.SetEnvironmentVariable(variable, previousValue);
        Directory.Delete(directory, recursive: true);
    }
}

static Task InvalidEnvironmentEntriesAreRejected()
{
    Throws<ArgumentException>(() => new AppServerClientOptions
    {
        Environment = new Dictionary<string, string?> { [" "] = "value" },
    }.Validate());
    Throws<ArgumentException>(() => new AppServerClientOptions
    {
        Environment = new Dictionary<string, string?> { ["INVALID=NAME"] = "value" },
    }.Validate());
    Throws<ArgumentException>(() => new AppServerClientOptions
    {
        Environment = new Dictionary<string, string?> { ["INVALID\0NAME"] = "value" },
    }.Validate());
    Throws<ArgumentException>(() => new AppServerClientOptions
    {
        Environment = new Dictionary<string, string?> { ["VALID_NAME"] = "invalid\0value" },
    }.Validate());
    return Task.CompletedTask;
}

static Task LocalCodexConfigurationAcceptsConfiguredExecutable()
{
    using var fixture = new LocalCodexConfigurationFixture();
    var configuration = CodexLocalAppServerConfigurationResolver.Resolve(
        fixture.CreateRequest(commandLineExecutablePath: fixture.ExecutablePath));

    Equal(CodexExecutableSource.CommandLine, configuration.ExecutableSource);
    Equal(fixture.ExecutablePath, configuration.CodexExecutablePath);
    Equal(fixture.DirectoryPath, configuration.WorkingDirectory);
    Equal(TimeSpan.FromSeconds(9), configuration.StartupTimeout);
    Equal(TimeSpan.FromSeconds(4), configuration.ShutdownTimeout);
    Equal(fixture.ExecutablePath, configuration.CreateClientOptions().CodexExecutablePath);
    Equal(">=0.144.4 <0.145.0", configuration.VersionCompatibility.ToString());
    return Task.CompletedTask;
}

static Task LocalCodexConfigurationUsesEnvironmentAllowlist()
{
    using var fixture = new LocalCodexConfigurationFixture();
    var configuration = CodexLocalAppServerConfigurationResolver.Resolve(
        fixture.CreateRequest(commandLineExecutablePath: fixture.ExecutablePath));
    var options = configuration.CreateClientOptions();
    var expectedNames = new[]
    {
        "APPDATA",
        "ComSpec",
        "GCM_INTERACTIVE",
        "GIT_CONFIG_GLOBAL",
        "GIT_CONFIG_NOSYSTEM",
        "GIT_TERMINAL_PROMPT",
        "HOME",
        "LOCALAPPDATA",
        "PATH",
        "PATHEXT",
        "RUST_LOG",
        "SystemRoot",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "WINDIR",
    };

    True(!options.InheritParentEnvironment, "Production Codex options still inherit the parent environment.");
    Equal(
        string.Join("|", expectedNames.Order(StringComparer.OrdinalIgnoreCase)),
        string.Join("|", options.Environment.Keys.Order(StringComparer.OrdinalIgnoreCase)));
    Equal(fixture.TempDirectory, options.Environment["TEMP"]);
    Equal(fixture.TempDirectory, options.Environment["TMP"]);
    True(!options.Environment.ContainsKey("CODEX_HOME"), "Policy unexpectedly sets CODEX_HOME.");
    True(!options.Environment.ContainsKey("CODEX_ACCESS_TOKEN"), "Policy inherited a Codex access token.");
    True(!options.Environment.ContainsKey("OPENAI_API_KEY"), "Policy inherited an API key.");
    True(!options.Environment.ContainsKey("HTTPS_PROXY"), "Policy inherited a proxy setting.");
    True(!options.Environment.ContainsKey("PSModulePath"), "Policy inherited PowerShell module state.");
    return Task.CompletedTask;
}

static Task LocalCodexConfigurationFailsClosedWithoutPath()
{
    using var fixture = new LocalCodexConfigurationFixture();
    var exception = Capture<CodexLocalConfigurationException>(() =>
        CodexLocalAppServerConfigurationResolver.Resolve(
            fixture.CreateRequest(commandLineExecutablePath: "relative-codex.exe")));

    Equal(
        CodexLocalConfigurationFailure.InvalidConfiguredExecutable,
        exception.Failure);
    True(
        !exception.Message.Contains(fixture.DirectoryPath, StringComparison.OrdinalIgnoreCase),
        "Configuration error exposed the configured local path.");
    return Task.CompletedTask;
}

static Task LocalCodexConfigurationDoesNotFallbackFromInvalidEnvironment()
{
    using var fixture = new LocalCodexConfigurationFixture();
    var exception = Capture<CodexLocalConfigurationException>(() =>
        CodexLocalAppServerConfigurationResolver.Resolve(
            fixture.CreateRequest(
                environmentExecutablePath: "relative-codex.exe",
                pathValue: fixture.DirectoryPath)));

    Equal(
        CodexLocalConfigurationFailure.InvalidConfiguredExecutable,
        exception.Failure);
    return Task.CompletedTask;
}

static Task LocalCodexConfigurationReportsMissingExecutable()
{
    using var fixture = new LocalCodexConfigurationFixture();
    File.Delete(fixture.ExecutablePath);
    var exception = Capture<CodexLocalConfigurationException>(() =>
        CodexLocalAppServerConfigurationResolver.Resolve(fixture.CreateRequest()));

    Equal(CodexLocalConfigurationFailure.CodexExecutableNotFound, exception.Failure);
    return Task.CompletedTask;
}

static Task LocalCodexConfigurationDiscoversAbsolutePath()
{
    using var fixture = new LocalCodexConfigurationFixture();
    var configuration = CodexLocalAppServerConfigurationResolver.Resolve(
        fixture.CreateRequest(pathValue: fixture.DirectoryPath));

    Equal(CodexExecutableSource.Path, configuration.ExecutableSource);
    Equal(fixture.ExecutablePath, configuration.CodexExecutablePath);
    return Task.CompletedTask;
}

static Task LocalCodexConfigurationRejectsInvalidTemporaryDirectory()
{
    using var fixture = new LocalCodexConfigurationFixture();
    var exception = Capture<CodexLocalConfigurationException>(() =>
        CodexLocalAppServerConfigurationResolver.Resolve(
            fixture.CreateRequest(temporaryDirectory: "relative-temp")));

    Equal(CodexLocalConfigurationFailure.InvalidTemporaryDirectory, exception.Failure);
    True(
        !exception.Message.Contains(fixture.DirectoryPath, StringComparison.OrdinalIgnoreCase),
        "Temporary-directory error exposed a local path.");
    return Task.CompletedTask;
}

static Task StandardErrorLimitIsValidated()
{
    Throws<ArgumentOutOfRangeException>(() => new AppServerClientOptions
    {
        MaximumStandardErrorBytes = 1_023,
    }.Validate());

    return Task.CompletedTask;
}

static Task CodexVersionFormatAndCompatibilityAreFrozen()
{
    True(
        CodexVersionPreflight.TryParseVersion("codex-cli 0.144.4\r\n", out var observed),
        "The documented local Codex version format was not parsed.");
    Equal(new CodexSemanticVersion(0, 144, 4), observed);
    True(
        CodexVersionCompatibility.Default.IsSupported(observed),
        "The locally verified Codex version is outside the frozen compatibility range.");
    True(
        CodexVersionCompatibility.Default.IsSupported(new CodexSemanticVersion(0, 144, 99)),
        "A compatible patch release was rejected.");
    True(
        !CodexVersionCompatibility.Default.IsSupported(new CodexSemanticVersion(0, 145, 0)),
        "An unreviewed Codex minor release was accepted.");
    True(
        !CodexVersionPreflight.TryParseVersion("codex-cli 0.144.4-preview", out _),
        "An unreviewed prerelease version was accepted.");
    True(
        !CodexVersionPreflight.TryParseVersion("codex-cli 0.144.4\nother", out _),
        "Ambiguous multi-line version output was accepted.");
    return Task.CompletedTask;
}

static async Task CodexVersionPreflightUsesIsolatedEnvironment()
{
    const string parentVariable = "CODEX_AUTOCAD_VERSION_PREFLIGHT_PARENT";
    var previousParentValue = Environment.GetEnvironmentVariable(parentVariable);
    var directory = CreateTemporaryDirectory("version-preflight-isolation");
    try
    {
        Environment.SetEnvironmentVariable(parentVariable, "parent-marker");
        var scriptPath = Path.Combine(directory, "codex-version.cmd");
        File.WriteAllLines(
            scriptPath,
            new[]
            {
                "@echo off",
                "if defined " + parentVariable + " (exit /b 17)",
                "echo codex-cli 0.144.4",
                "exit /b 0",
            },
            Encoding.ASCII);
        var options = new AppServerClientOptions
        {
            CodexExecutablePath = scriptPath,
            WorkingDirectory = directory,
            Environment = CodexChildEnvironmentPolicy.CreateForCurrentProcess(directory),
            InheritParentEnvironment = false,
        };

        var result = await CodexVersionPreflight.VerifyAsync(
            options,
            CodexVersionCompatibility.Default,
            TimeSpan.FromSeconds(5));

        Equal(new CodexSemanticVersion(0, 144, 4), result.Version);
        Equal(">=0.144.4 <0.145.0", result.Compatibility.ToString());
    }
    finally
    {
        Environment.SetEnvironmentVariable(parentVariable, previousParentValue);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task UnsupportedCodexVersionFailsClosed()
{
    var directory = CreateTemporaryDirectory("version-preflight-unsupported");
    try
    {
        var scriptPath = WriteVersionProbe(directory, "echo codex-cli 0.145.0", "exit /b 0");
        var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
            CodexVersionPreflight.VerifyAsync(
                CreateVersionPreflightOptions(scriptPath, directory),
                CodexVersionCompatibility.Default,
                TimeSpan.FromSeconds(5)));

        Equal(CodexVersionPreflightFailure.UnsupportedVersion, exception.Failure);
        True(
            !exception.Message.Contains(directory, StringComparison.OrdinalIgnoreCase),
            "Version preflight error exposed a local path.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task CodexVersionProcessExitFailsClosed()
{
    var directory = CreateTemporaryDirectory("version-preflight-process-exit");
    try
    {
        const string stderrMarker = "version-preflight-private-stderr-marker";
        var scriptPath = WriteVersionProbe(
            directory,
            "echo " + stderrMarker + " 1>&2",
            "exit /b 23");
        var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
            CodexVersionPreflight.VerifyAsync(
                CreateVersionPreflightOptions(scriptPath, directory),
                CodexVersionCompatibility.Default,
                TimeSpan.FromSeconds(5)));

        Equal(CodexVersionPreflightFailure.ProcessExitedWithError, exception.Failure);
        True(
            !exception.Message.Contains(directory, StringComparison.OrdinalIgnoreCase)
            && !exception.Message.Contains(stderrMarker, StringComparison.Ordinal),
            "Version preflight process-exit error exposed a path or stderr text.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task OversizedCodexVersionOutputFailsClosed()
{
    var directory = CreateTemporaryDirectory("version-preflight-output-limit");
    try
    {
        File.WriteAllText(
            Path.Combine(directory, "payload.txt"),
            new string('x', CodexVersionPreflight.MaximumVersionOutputBytes + 1),
            Encoding.ASCII);
        var scriptPath = WriteVersionProbe(
            directory,
            "type \"%~dp0payload.txt\"",
            "exit /b 0");
        var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
            CodexVersionPreflight.VerifyAsync(
                CreateVersionPreflightOptions(scriptPath, directory),
                CodexVersionCompatibility.Default,
                TimeSpan.FromSeconds(5)));

        Equal(CodexVersionPreflightFailure.VersionOutputTooLarge, exception.Failure);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task NonUtf8CodexVersionOutputFailsClosed()
{
    var directory = CreateTemporaryDirectory("version-preflight-non-utf8");
    try
    {
        File.WriteAllBytes(Path.Combine(directory, "payload.bin"), new byte[] { 0xff, 0xfe, 0xfd });
        var scriptPath = WriteVersionProbe(
            directory,
            "type \"%~dp0payload.bin\"",
            "exit /b 0");
        var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
            CodexVersionPreflight.VerifyAsync(
                CreateVersionPreflightOptions(scriptPath, directory),
                CodexVersionCompatibility.Default,
                TimeSpan.FromSeconds(5)));

        Equal(CodexVersionPreflightFailure.InvalidVersionOutput, exception.Failure);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task CodexVersionPreflightTimeoutFailsClosed()
{
    var directory = CreateTemporaryDirectory("version-preflight-timeout");
    try
    {
        var scriptPath = WriteVersionProbe(
            directory,
            "ping 127.0.0.1 -n 8 > nul",
            "echo codex-cli 0.144.4",
            "exit /b 0");
        var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
            CodexVersionPreflight.VerifyAsync(
                CreateVersionPreflightOptions(scriptPath, directory),
                CodexVersionCompatibility.Default,
                TimeSpan.FromMilliseconds(200)));

        Equal(CodexVersionPreflightFailure.TimedOut, exception.Failure);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static string WriteVersionProbe(string directory, params string[] commands)
{
    var scriptPath = Path.Combine(directory, "codex-version.cmd");
    File.WriteAllLines(
        scriptPath,
        new[] { "@echo off" }.Concat(commands),
        Encoding.ASCII);
    return scriptPath;
}

static AppServerClientOptions CreateVersionPreflightOptions(string scriptPath, string directory)
{
    return new AppServerClientOptions
    {
        CodexExecutablePath = scriptPath,
        WorkingDirectory = directory,
        Environment = CodexChildEnvironmentPolicy.CreateForCurrentProcess(directory),
        InheritParentEnvironment = false,
    };
}

static string CreateTemporaryDirectory(string purpose)
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "codex-autocad-appserver-" + purpose + "-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static string WriteEnvironmentProbe(string directory, params string[] commands)
{
    var scriptPath = Path.Combine(directory, "environment-probe.cmd");
    File.WriteAllLines(
        scriptPath,
        new[] { "@echo off" }.Concat(commands).Concat(new[] { "exit /b 0" }),
        Encoding.ASCII);
    return scriptPath;
}

static async Task<string> RunProcessProbeAsync(AppServerClientOptions options)
{
    await using var transport = new CodexProcessTransport(options);
    var exited = new TaskCompletionSource<AppServerTransportExitedEventArgs>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    transport.Exited += (_, eventArgs) => exited.TrySetResult(eventArgs);

    await transport.StartAsync();
    using var reader = new StreamReader(transport.ReadStream, Encoding.ASCII);
    var outputTask = reader.ReadToEndAsync();
    var exit = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
    var output = await outputTask.WaitAsync(TimeSpan.FromSeconds(10));
    Equal<int?>(0, exit.ExitCode);
    return output;
}

static void ContainsLine(string output, string expected)
{
    var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    True(
        lines.Contains(expected, StringComparer.Ordinal),
        "Process probe did not emit expected marker '" + expected + "'.");
}

static string? Method(JsonDocument frame)
{
    return frame.RootElement.TryGetProperty("method", out var method) ? method.GetString() : null;
}

static long Id(JsonDocument frame)
{
    return frame.RootElement.GetProperty("id").GetInt64();
}

static async Task ThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException("Expected " + typeof(TException).Name);
}

static async Task<TException> CaptureAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException("Expected " + typeof(TException).Name);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
}

static TException Capture<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
}

internal sealed record TestResult(int Value);

internal sealed class LocalCodexConfigurationFixture : IDisposable
{
    public LocalCodexConfigurationFixture()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-local-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        TempDirectory = Path.Combine(DirectoryPath, "temp");
        Directory.CreateDirectory(TempDirectory);
        ExecutablePath = Path.Combine(DirectoryPath, "codex.exe");
        File.WriteAllBytes(ExecutablePath, Array.Empty<byte>());
    }

    public string DirectoryPath { get; }

    public string ExecutablePath { get; }

    public string TempDirectory { get; }

    public CodexLocalAppServerConfigurationRequest CreateRequest(
        string? commandLineExecutablePath = null,
        string? environmentExecutablePath = null,
        string? pathValue = null,
        string? temporaryDirectory = null)
    {
        return new CodexLocalAppServerConfigurationRequest
        {
            CommandLineExecutablePath = commandLineExecutablePath,
            EnvironmentExecutablePath = environmentExecutablePath,
            ApplicationDataDirectory = null,
            PathValue = pathValue,
            WorkingDirectory = DirectoryPath,
            TemporaryDirectory = temporaryDirectory ?? TempDirectory,
            StartupTimeout = TimeSpan.FromSeconds(9),
            ShutdownTimeout = TimeSpan.FromSeconds(4),
        };
    }

    public void Dispose()
    {
        Directory.Delete(DirectoryPath, recursive: true);
    }
}

internal sealed class ClientFixture : IAsyncDisposable
{
    private ClientFixture(ScriptedTransport transport, CodexAppServerClient client, List<JsonDocument> frames)
    {
        Transport = transport;
        Client = client;
        Frames = frames;
    }

    public ScriptedTransport Transport { get; }

    public CodexAppServerClient Client { get; }

    public List<JsonDocument> Frames { get; }

    public static async Task<ClientFixture> StartAsync()
    {
        var frames = new List<JsonDocument>();
        var transport = new ScriptedTransport();
        transport.FrameWritten += frame =>
        {
            lock (frames)
            {
                frames.Add(JsonDocument.Parse(frame.RootElement.GetRawText()));
            }

            var method = frame.RootElement.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString()
                : null;
            if (method == "initialize")
            {
                var id = frame.RootElement.GetProperty("id").GetInt64();
                transport.Inject($"{{\"id\":{id},\"result\":{{\"codexHome\":\"C:\\\\Users\\\\tester\\\\.codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\",\"userAgent\":\"codex-test\"}}}}");
            }
        };

        var client = new CodexAppServerClient(transport);
        await client.StartAsync();
        return new ClientFixture(transport, client, frames);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        foreach (var frame in Frames)
        {
            frame.Dispose();
        }

        await Transport.DisposeAsync();
    }
}

internal sealed class ScriptedTransport : IAppServerTransport
{
    private readonly ChannelReadStream _read = new();
    private readonly FrameCaptureWriteStream _write;

    public ScriptedTransport()
    {
        _write = new FrameCaptureWriteStream(frame =>
        {
            using var document = JsonDocument.Parse(frame);
            FrameWritten?.Invoke(document);
        });
    }

    public Stream ReadStream => _read;

    public Stream WriteStream => _write;

    public bool IsRunning { get; private set; }

    public event Action<JsonDocument>? FrameWritten;

    public event EventHandler<AppServerTransportExitedEventArgs>? Exited;

    public event EventHandler<AppServerStandardErrorEventArgs>? StandardErrorReceived
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRunning)
        {
            IsRunning = false;
            _read.Complete();
            Exited?.Invoke(this, new AppServerTransportExitedEventArgs(0, expected: true));
        }

        return Task.CompletedTask;
    }

    public void Inject(string json)
    {
        _read.Inject(Encoding.UTF8.GetBytes(json + "\n"));
    }

    public ValueTask DisposeAsync()
    {
        _read.Dispose();
        _write.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class ChannelReadStream : Stream
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
    private byte[]? _current;
    private int _offset;

    public void Inject(byte[] bytes) => _channel.Writer.TryWrite(bytes);

    public void Complete() => _channel.Writer.TryComplete();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_current is null || _offset >= _current.Length)
        {
            if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                return 0;
            }

            if (!_channel.Reader.TryRead(out _current))
            {
                continue;
            }

            _offset = 0;
        }

        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class FrameCaptureWriteStream(Action<string> onFrame) : Stream
{
    private readonly MemoryStream _buffer = new();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var value in buffer.Span)
        {
            if (value == (byte)'\n')
            {
                onFrame(Encoding.UTF8.GetString(_buffer.ToArray()));
                _buffer.SetLength(0);
            }
            else
            {
                _buffer.WriteByte(value);
            }
        }

        return ValueTask.CompletedTask;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _buffer.Length;
    public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => _buffer.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();
}

internal sealed class FragmentedReadStream(params string[] fragments) : Stream
{
    private readonly Queue<byte[]> _fragments = new(fragments.Select(Encoding.UTF8.GetBytes));
    private byte[]? _current;
    private int _offset;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_current is null || _offset >= _current.Length)
        {
            if (_fragments.Count == 0)
            {
                return ValueTask.FromResult(0);
            }

            _current = _fragments.Dequeue();
            _offset = 0;
        }

        var count = Math.Min(_current.Length - _offset, buffer.Length);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return ValueTask.FromResult(count);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
