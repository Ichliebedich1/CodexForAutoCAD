using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AppServer.Protocol;
using Codex.AutoCAD.Contracts;

if (args is ["--version"])
{
    return await RunFakeCodexVersionProbeAsync();
}

var specs = new (string Name, Func<Task> Run)[]
{
    ("分片JSONL帧可重组", FragmentedFrameIsReassembled),
    ("超限帧被拒绝", OversizedFrameFails),
    ("未终止帧被拒绝", UnterminatedFrameFails),
    ("initialize握手完成", InitializeHandshakeCompletes),
    ("乱序响应仍按请求关联", OutOfOrderResponsesAreCorrelated),
    ("无处理器的命令审批默认拒绝", CommandApprovalDefaultsToDecline),
    ("服务端请求失败响应在出站边界脱敏", ServerRequestFailureResponseIsSanitizedBeforeWrite),
    ("通知被分发", NotificationIsDispatched),
    ("RPC异常公开诊断脱敏且不保留原始data", RpcExceptionPublicDiagnosticIsSanitized),
    ("协议异常公开诊断脱敏且不保留原始inner", ProtocolExceptionPublicDiagnosticIsSanitized),
    ("协议故障事件不保留观察者原始异常", ProtocolFaultEventDoesNotRetainObserverException),
    ("stderr观察者异常被隔离并转为安全协议故障", StandardErrorObserverFailureIsIsolated),
    ("配置和版本预检异常先分类再脱敏", ConfigurationDiagnosticsAreClassifiedBeforeSanitization),
    ("stderr只保留有界无内容摘要", StandardErrorIsDrainedWithoutText),
    ("进程退出等待完整stderr摘要", ProcessExitPublishesCompletedStandardErrorSummary),
    ("transport stderr观察者异常不阻断排空与后续观察者", TransportStandardErrorObserverFailureIsIsolated),
    ("隔离子进程看不到任意父环境变量", IsolatedChildDoesNotInheritArbitraryParentEnvironment),
    ("环境null覆写删除继承变量", NullEnvironmentOverrideRemovesInheritedVariable),
    ("非法环境键值启动前被拒绝", InvalidEnvironmentEntriesAreRejected),
    ("本地Codex配置请求字符串投影不泄露路径或环境", LocalCodexConfigurationRequestStringIsSafe),
    ("AppServer启动配置字符串投影不泄露参数或环境", AppServerClientOptionsStringIsSafe),
    ("AppServer初始化请求字符串投影不泄露客户端配置", AppServerInitializeRequestStringsAreSafe),
    ("AppServer协议包装器字符串投影不泄露transport payload", AppServerProtocolWrapperStringsAreSafe),
    ("AppServer控制包装器字符串投影不泄露Provider或审批payload", AppServerControlWrapperStringsAreSafe),
    ("AppServer审批payload字符串投影不泄露命令路径或Provider身份", AppServerApprovalPayloadStringsAreSafe),
    ("本地Codex配置只接受固定盘绝对exe", LocalCodexConfigurationAcceptsConfiguredExecutable),
    ("本地Codex配置使用兼容白名单", LocalCodexConfigurationUsesCompatibilityAllowlist),
    ("隔离CodexHome覆盖父配置并进入子进程", IsolatedCodexHomeOverridesParentAndReachesChild),
    ("本地Codex配置错误不泄露路径", LocalCodexConfigurationFailsClosedWithoutPath),
    ("无效环境Codex路径不会回退", LocalCodexConfigurationDoesNotFallbackFromInvalidEnvironment),
    ("缺失本地Codex配置返回稳定错误", LocalCodexConfigurationReportsMissingExecutable),
    ("本地Codex可从绝对PATH发现", LocalCodexConfigurationDiscoversAbsolutePath),
    ("无效临时目录返回稳定错误", LocalCodexConfigurationRejectsInvalidTemporaryDirectory),
    ("无效隔离CodexHome返回稳定错误", LocalCodexConfigurationRejectsInvalidCodexHomeDirectory),
    ("stderr限额无效时被拒绝", StandardErrorLimitIsValidated),
    ("Codex版本格式与兼容范围固定", CodexVersionFormatAndCompatibilityAreFrozen),
    ("Codex版本预检使用同一身份锁和隔离环境", CodexVersionPreflightUsesLockedIdentityAndIsolatedEnvironment),
    ("不支持Codex版本fail-closed", UnsupportedCodexVersionFailsClosed),
    ("Codex版本错误输出不泄露stderr", CodexVersionProcessExitFailsClosed),
    ("超限与非UTF8版本输出fail-closed", InvalidCodexVersionOutputFailsClosed),
    ("Codex版本预检超时清理后代", CodexVersionTimeoutCleansDescendant),
    ("Codex版本预检取消清理子进程", CodexVersionCancellationCleansProcess),
    ("Codex版本终止失败有界返回", CodexVersionTerminationFailureIsBounded),
    ("Codex身份租约阻止路径替换", CodexExecutableLeasePreventsReplacement),
    ("AppServer停止超时与取消均清理进程", AppServerStopTimeoutAndCancellationCleanProcess)
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

static async Task ServerRequestFailureResponseIsSanitizedBeforeWrite()
{
    await using var fixture = await ClientFixture.StartAsync();
    var messageMarker = "outbound-message-secret-marker";
    var dataMarker = "outbound-data-secret-marker";
    var unsafeMessage = string.Join(
        " ",
        "Bear" + "er " + messageMarker,
        @"C:\Users\outbound-user\private\failure.log",
        "https://outbound-user:outbound-pass@example.invalid/api?access_token=remote-secret",
        @"CONTOSO\outbound-user");
    using var sourceData = JsonDocument.Parse(
        $$"""{"credential":"{{dataMarker}}","path":"C:\\Users\\outbound-user\\private\\data.json"}""");
    var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

    fixture.Client.ServerRequestReceived += (_, _) =>
        ValueTask.FromResult<ServerRequestResolution?>(
            ServerRequestResolution.Failure(
                47001,
                unsafeMessage,
                sourceData.RootElement.Clone()));
    fixture.Transport.FrameWritten += frame =>
    {
        if (frame.RootElement.TryGetProperty("id", out var id)
            && id.TryGetInt64(out var value)
            && value == 701
            && frame.RootElement.TryGetProperty("error", out _))
        {
            response.TrySetResult(frame.RootElement.GetRawText());
        }
    };

    fixture.Transport.Inject("""{"id":701,"method":"test/failure","params":{}}""");

    var rawResponse = await response.Task.WaitAsync(TimeSpan.FromSeconds(5));
    using var responseDocument = JsonDocument.Parse(rawResponse);
    var error = responseDocument.RootElement.GetProperty("error");
    Equal(47001L, error.GetProperty("code").GetInt64());

    var expected = DiagnosticSanitizer.SanitizeText(
        DiagnosticDataClassification.RemoteError,
        unsafeMessage);
    Equal(expected.SafeText, error.GetProperty("message").GetString());
    True(
        expected.Redactions != DiagnosticRedactionKinds.None,
        "测试输入必须触发至少一种统一诊断脱敏规则。");

    var safeData = error.GetProperty("data");
    Equal(
        DiagnosticDataClassification.RemoteError.ToString(),
        safeData.GetProperty("diagnosticClassification").GetString());
    Equal((int)expected.Redactions, safeData.GetProperty("diagnosticRedactions").GetInt32());
    True(safeData.GetProperty("sourceDataWasPresent").GetBoolean(), "原始data存在标志必须保留。");
    True(!rawResponse.Contains(messageMarker, StringComparison.Ordinal), "出站frame泄露了message令牌。");
    True(!rawResponse.Contains(dataMarker, StringComparison.Ordinal), "出站frame泄露了原始data。");
    True(!rawResponse.Contains("outbound-user", StringComparison.Ordinal), "出站frame泄露了本机身份。");
    True(!rawResponse.Contains(@"C:\\Users", StringComparison.Ordinal), "出站frame泄露了本机路径。");
    True(!rawResponse.Contains("example.invalid", StringComparison.Ordinal), "出站frame泄露了URI。");
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

static Task RpcExceptionPublicDiagnosticIsSanitized()
{
    var bearerMarker = "rpc-bearer-secret-marker";
    var queryMarker = "rpc-query-secret-marker";
    var jsonMarker = "rpc-json-secret-marker";
    var unsafeMessage = string.Join(
        " ",
        "Bear" + "er " + bearerMarker,
        @"C:\Users\rpc-user\private\rpc.log",
        "https://rpc-user:rpc-pass@example.invalid/api?access_"
            + "token="
            + queryMarker,
        @"CONTOSO\rpc-user");
    using var data = JsonDocument.Parse(
        "{\"api_" + "key\":\"" + jsonMarker + "\","
        + "\"path\":\"C:\\\\Users\\\\rpc-user\\\\payload.json\"}");

    var failure = new AppServerRpcException(-32001, unsafeMessage, data.RootElement);

    Equal(-32001L, failure.Code);
    True(failure.DataWasPresent, "RPC exception lost the structured data-presence signal.");
    True(failure.DataElement is null, "RPC exception retained the original data payload.");
    True(
        (failure.DiagnosticRedactions & DiagnosticRedactionKinds.Token) != 0
        && (failure.DiagnosticRedactions & DiagnosticRedactionKinds.Path) != 0
        && (failure.DiagnosticRedactions & DiagnosticRedactionKinds.Uri) != 0
        && (failure.DiagnosticRedactions & DiagnosticRedactionKinds.Identity) != 0,
        "RPC exception did not preserve structured redaction flags.");
    var publicDiagnostic = failure.Message + " " + failure.RpcMessage + " " + failure;
    foreach (var marker in new[]
             {
                 bearerMarker,
                 queryMarker,
                 jsonMarker,
                 "rpc-user",
                 "example.invalid",
             })
    {
        True(
            publicDiagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
            "RPC exception leaked a protected marker.");
    }

    True(
        publicDiagnostic.Contains("[redacted-token]", StringComparison.Ordinal)
        && publicDiagnostic.Contains("[redacted-path]", StringComparison.Ordinal)
        && publicDiagnostic.Contains("[redacted-uri]", StringComparison.Ordinal)
        && publicDiagnostic.Contains("[redacted-identity]", StringComparison.Ordinal),
        "RPC exception did not preserve structured redaction evidence.");
    True(
        failure.RpcMessage.Length <= 512,
        "RPC exception message exceeded the public output bound.");

    return Task.CompletedTask;
}

static Task ProtocolExceptionPublicDiagnosticIsSanitized()
{
    var messageMarker = "protocol-message-secret-marker";
    var innerMarker = "protocol-inner-secret-marker";
    var failure = new AppServerProtocolException(
        "Bear" + "er " + messageMarker + " " + @"C:\Users\protocol-user\frame.json",
        new InvalidOperationException(
            innerMarker + " " + "https://protocol-user@example.invalid/?api_"
            + "key=inner-query-secret-marker"));

    True(failure.InnerException is null, "Protocol exception retained the raw inner exception.");
    var publicDiagnostic = failure.Message + " " + failure;
    foreach (var marker in new[]
             {
                 messageMarker,
                 innerMarker,
                 "inner-query-secret-marker",
                 "protocol-user",
                 "example.invalid",
             })
    {
        True(
            publicDiagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
            "Protocol exception leaked a protected marker.");
    }

    True(
        publicDiagnostic.Contains("[redacted-token]", StringComparison.Ordinal)
        && publicDiagnostic.Contains("[redacted-path]", StringComparison.Ordinal),
        "Protocol exception did not preserve structured redaction evidence.");

    return Task.CompletedTask;
}

static async Task ProtocolFaultEventDoesNotRetainObserverException()
{
    await using var fixture = await ClientFixture.StartAsync();
    var messageMarker = "protocol-fault-event-message-marker";
    var innerMarker = "protocol-fault-event-inner-marker";
    var dataMarker = "protocol-fault-event-data-marker";
    var sourceFailure = new InvalidOperationException(
        "Bear" + "er " + messageMarker + " " + @"C:\Users\event-user\fault.log",
        new InvalidDataException(
            innerMarker + " https://event-user@example.invalid/?api_"
            + "key=protocol-fault-query-marker"));
    sourceFailure.Data["credential"] = dataMarker;

    var received = new TaskCompletionSource<AppServerProtocolFaultEventArgs>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Client.ProtocolFaulted += (_, fault) => received.TrySetResult(fault);
    fixture.Client.NotificationReceived += (_, _) => throw sourceFailure;

    fixture.Transport.Inject("{\"method\":\"turn/started\",\"params\":{\"turnId\":\"turn-1\"}}");
    var fault = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

    True(
        !ReferenceEquals(sourceFailure, fault.Exception),
        "Protocol fault event retained the original observer exception.");
    True(
        fault.Exception.InnerException is null,
        "Protocol fault event retained the original inner exception graph.");
    True(
        fault.Exception.Data.Count == 0,
        "Protocol fault event retained the original exception data.");
    True(
        string.IsNullOrEmpty(fault.Exception.StackTrace),
        "Protocol fault event retained the original exception stack trace.");

    var publicDiagnostic = fault.Exception.Message + " " + fault.Exception;
    foreach (var marker in new[]
             {
                 messageMarker,
                 innerMarker,
                 dataMarker,
                 "protocol-fault-query-marker",
                 "event-user",
                 "example.invalid",
             })
    {
        True(
            publicDiagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
            "Protocol fault event leaked a protected marker.");
    }

    Equal(DiagnosticDataClassification.Exception, fault.DiagnosticClassification);
    True(
        (fault.DiagnosticRedactions & DiagnosticRedactionKinds.Token) != 0
        && (fault.DiagnosticRedactions & DiagnosticRedactionKinds.Path) != 0
        && (fault.DiagnosticRedactions & DiagnosticRedactionKinds.Uri) != 0,
        "Protocol fault event did not preserve structured redaction evidence.");
}

static async Task StandardErrorObserverFailureIsIsolated()
{
    await using var fixture = await ClientFixture.StartAsync();
    var messageMarker = "stderr-observer-message-marker";
    var innerMarker = "stderr-observer-inner-marker";
    var sourceFailure = new InvalidOperationException(
        "Bear" + "er " + messageMarker + " " + @"C:\Users\stderr-user\fault.log",
        new InvalidDataException(innerMarker));

    var laterObserver = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var protocolFault = new TaskCompletionSource<AppServerProtocolFaultEventArgs>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Client.StandardErrorReceived += (_, _) => throw sourceFailure;
    fixture.Client.StandardErrorReceived += (_, _) => laterObserver.TrySetResult(true);
    fixture.Client.ProtocolFaulted += (_, fault) => protocolFault.TrySetResult(fault);

    fixture.Transport.InjectStandardError(new AppServerStandardErrorSummary(17, truncated: true));

    True(
        await laterObserver.Task.WaitAsync(TimeSpan.FromSeconds(5)),
        "A failing stderr observer prevented a later observer from receiving the summary.");
    var fault = await protocolFault.Task.WaitAsync(TimeSpan.FromSeconds(5));
    True(
        !ReferenceEquals(sourceFailure, fault.Exception)
        && fault.Exception.InnerException is null
        && fault.Exception.Data.Count == 0
        && string.IsNullOrEmpty(fault.Exception.StackTrace),
        "The stderr observer failure escaped through the public protocol fault.");
    var publicDiagnostic = fault.Exception.Message + " " + fault.Exception;
    foreach (var marker in new[] { messageMarker, innerMarker, "stderr-user" })
    {
        True(
            publicDiagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
            "The stderr observer failure leaked a protected marker.");
    }

    Equal(DiagnosticDataClassification.Exception, fault.DiagnosticClassification);
    True(
        (fault.DiagnosticRedactions & DiagnosticRedactionKinds.Token) != 0
        && (fault.DiagnosticRedactions & DiagnosticRedactionKinds.Path) != 0,
        "The stderr observer failure did not preserve structured redaction evidence.");
}

static Task ConfigurationDiagnosticsAreClassifiedBeforeSanitization()
{
    var tokenMarker = "configuration-secret-marker";
    var unsafeMessage = string.Join(
        " ",
        "Bear" + "er " + tokenMarker,
        @"C:\Users\configuration-user\private\codex.exe",
        "https://configuration-user@example.invalid/?api_"
            + "key=configuration-query-marker");
    AppServerException[] failures =
    {
        new CodexLocalConfigurationException(
            CodexLocalConfigurationFailure.InvalidConfiguredExecutable,
            unsafeMessage),
        new CodexVersionPreflightException(
            CodexVersionPreflightFailure.InvalidVersionOutput,
            unsafeMessage),
    };

    foreach (var failure in failures)
    {
        Equal(DiagnosticDataClassification.Configuration, failure.DiagnosticClassification);
        True(
            (failure.DiagnosticRedactions & DiagnosticRedactionKinds.Token) != 0
            && (failure.DiagnosticRedactions & DiagnosticRedactionKinds.Path) != 0
            && (failure.DiagnosticRedactions & DiagnosticRedactionKinds.Uri) != 0,
            "Configuration diagnostic did not preserve structured redaction flags.");
        True(
            failure.Message.IndexOf(tokenMarker, StringComparison.Ordinal) < 0
            && failure.Message.IndexOf("configuration-user", StringComparison.Ordinal) < 0
            && failure.Message.IndexOf("example.invalid", StringComparison.Ordinal) < 0,
            "Configuration diagnostic leaked a protected marker.");
    }

    return Task.CompletedTask;
}

static Task LocalCodexConfigurationRequestStringIsSafe()
{
    var markers = new[]
    {
        "command-line-user-marker",
        "environment-user-marker",
        "application-data-user-marker",
        "path-value-user-marker",
        "working-directory-user-marker",
        "temporary-directory-user-marker",
        "codex-home-user-marker",
    };
    var request = new CodexLocalAppServerConfigurationRequest
    {
        CommandLineExecutablePath =
            @"C:\Users\command-line-user-marker\bin\codex.exe",
        EnvironmentExecutablePath =
            @"C:\Users\environment-user-marker\bin\codex.exe",
        ApplicationDataDirectory =
            @"C:\Users\application-data-user-marker\AppData\Roaming",
        PathValue = string.Join(
            Path.PathSeparator,
            @"C:\Users\path-value-user-marker\bin",
            @"\\server\private-share"),
        WorkingDirectory =
            @"C:\Users\working-directory-user-marker\workspace",
        TemporaryDirectory =
            @"C:\Users\temporary-directory-user-marker\temp",
        CodexHomeDirectory =
            @"C:\Users\codex-home-user-marker\.codex",
        StartupTimeout = TimeSpan.FromSeconds(9),
        ShutdownTimeout = TimeSpan.FromSeconds(4),
    };

    var diagnostic = request.ToString();
    Equal(
        "CodexLocalAppServerConfigurationRequest { "
            + "CommandLineExecutableConfigured = True, "
            + "EnvironmentExecutableConfigured = True, "
            + "ApplicationDataConfigured = True, "
            + "PathConfigured = True, "
            + "WorkingDirectoryConfigured = True, "
            + "TemporaryDirectoryConfigured = True, "
            + "CodexHomeConfigured = True, "
            + "StartupTimeoutSeconds = 9, "
            + "ShutdownTimeoutSeconds = 4, "
            + "VersionCompatibilityConfigured = False }",
        diagnostic);
    foreach (var marker in markers)
    {
        True(
            diagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
            "Configuration request string projection leaked a protected marker.");
    }

    True(
        diagnostic.IndexOf(@"\\server\private-share", StringComparison.OrdinalIgnoreCase) < 0,
        "Configuration request string projection leaked a UNC path.");
    return Task.CompletedTask;
}

static Task AppServerClientOptionsStringIsSafe()
{
    var pathMarker = "appserver-options-path-marker";
    var secretMarker = "appserver-options-secret-marker";
    var options = new AppServerClientOptions
    {
        CodexExecutablePath = $@"C:\Users\{pathMarker}\bin\codex.exe",
        WorkingDirectory = $@"C:\Users\{pathMarker}\workspace",
        AdditionalArguments = new[]
        {
            "--access-token",
            secretMarker,
        },
        Environment = new Dictionary<string, string?>
        {
            ["CODEX_ACCESS_TOKEN"] = secretMarker,
            ["PATH"] = $@"C:\Users\{pathMarker}\bin",
        },
        InheritParentEnvironment = false,
        MaximumStandardErrorBytes = 32 * 1024,
        MaximumFrameBytes = 4 * 1024 * 1024,
        MaximumJsonDepth = 32,
        ShutdownTimeout = TimeSpan.FromSeconds(7),
    };

    var diagnostic = options.ToString();
    Equal(
        "AppServerClientOptions { "
            + "CodexExecutableConfigured = True, "
            + "WorkingDirectoryConfigured = True, "
            + "AdditionalArgumentCount = 2, "
            + "EnvironmentEntryCount = 2, "
            + "InheritParentEnvironment = False, "
            + "MaximumStandardErrorBytes = 32768, "
            + "MaximumFrameBytes = 4194304, "
            + "MaximumJsonDepth = 32, "
            + "ShutdownTimeoutSeconds = 7, "
            + "ExecutableLeaseConfigured = False }",
        diagnostic);
    foreach (var marker in new[] { pathMarker, secretMarker, "CODEX_ACCESS_TOKEN" })
    {
        True(
            diagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
            "App Server client options string projection leaked a protected marker.");
    }

    return Task.CompletedTask;
}

static Task AppServerInitializeRequestStringsAreSafe()
{
    const string marker = "initialize-client-protected-marker";
    var clientInfo = new AppServerClientInfo(marker, marker, marker);
    var capabilities = new AppServerInitializeCapabilities(
        ExperimentalApi: true,
        McpServerOpenaiFormElicitation: true,
        OptOutNotificationMethods: new[] { marker },
        RequestAttestation: true);
    var initialize = new AppServerInitializeParams(clientInfo, capabilities);

    object[] values =
    {
        clientInfo,
        capabilities,
        initialize,
    };

    foreach (var value in values)
    {
        var diagnostic = value.ToString() ?? string.Empty;
        True(
            diagnostic.StartsWith(value.GetType().Name, StringComparison.Ordinal),
            value.GetType().Name + " string projection omitted its stable type name.");
        True(
            diagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
            value.GetType().Name + " string projection leaked client configuration.");
        True(
            diagnostic.Length <= 512,
            value.GetType().Name + " string projection exceeded the public diagnostic bound.");
    }

    var wireJson = JsonSerializer.Serialize(initialize);
    True(
        wireJson.Contains(marker, StringComparison.Ordinal),
        "Initialize wire fields changed while hardening only diagnostic string projections.");
    return Task.CompletedTask;
}

static Task AppServerProtocolWrapperStringsAreSafe()
{
    const string codexHomeMarker = "initialize-codex-home-marker";
    const string platformMarker = "initialize-platform-marker";
    const string platformOsMarker = "initialize-platform-os-marker";
    const string userAgentMarker = "initialize-user-agent-marker";
    const string notificationMethodMarker = "notification-method-marker";
    const string notificationPayloadMarker = "notification-payload-marker";
    const string requestIdMarker = "server-request-id-marker";
    const string requestMethodMarker = "server-request-method-marker";
    const string requestPayloadMarker = "server-request-payload-marker";
    const string errorMessageMarker = "rpc-error-message-marker";
    const string errorDataMarker = "rpc-error-data-marker";
    const string resolutionResultMarker = "resolution-result-marker";

    using var notificationParams = JsonDocument.Parse(
        $$"""{"prompt":"{{notificationPayloadMarker}}"}""");
    using var requestParams = JsonDocument.Parse(
        $$"""{"path":"C:\\Users\\{{requestPayloadMarker}}\\private.dwg"}""");
    using var errorData = JsonDocument.Parse(
        $$"""{"access_token":"{{errorDataMarker}}"}""");

    var initialize = new AppServerInitializeResponse(
        $@"C:\Users\{codexHomeMarker}\.codex",
        platformMarker,
        platformOsMarker,
        userAgentMarker);
    var notification = new AppServerNotification(
        notificationMethodMarker,
        notificationParams.RootElement.Clone());
    var request = new AppServerServerRequest(
        new JsonRpcId(requestIdMarker),
        requestMethodMarker,
        requestParams.RootElement.Clone());
    var error = new AppServerRpcError(
        -32001,
        "Bear" + "er " + errorMessageMarker,
        errorData.RootElement.Clone());
    var success = ServerRequestResolution.Success(
        new { Content = resolutionResultMarker });
    var failure = ServerRequestResolution.Failure(
        -32002,
        "Bear" + "er " + errorMessageMarker,
        errorData.RootElement.Clone());

    var diagnostics = new[]
    {
        initialize.ToString(),
        notification.ToString(),
        request.ToString(),
        error.ToString(),
        success.ToString(),
        failure.ToString(),
    };
    Equal(
        "AppServerInitializeResponse { "
            + "CodexHomeConfigured = True, "
            + "PlatformFamilyConfigured = True, "
            + "PlatformOsConfigured = True, "
            + "UserAgentConfigured = True }",
        diagnostics[0]);
    Equal(
        "AppServerNotification { MethodConfigured = True, ParamsPresent = True }",
        diagnostics[1]);
    Equal(
        "AppServerServerRequest { IdConfigured = True, MethodConfigured = True, ParamsPresent = True }",
        diagnostics[2]);
    Equal(
        "AppServerRpcError { Code = -32001, MessageConfigured = True, DataPresent = True }",
        diagnostics[3]);
    Equal(
        "ServerRequestResolution { Succeeded = True, ResultPresent = True, ErrorPresent = False }",
        diagnostics[4]);
    Equal(
        "ServerRequestResolution { Succeeded = False, ResultPresent = False, ErrorPresent = True }",
        diagnostics[5]);

    var markers = new[]
    {
        codexHomeMarker,
        platformMarker,
        platformOsMarker,
        userAgentMarker,
        notificationMethodMarker,
        notificationPayloadMarker,
        requestIdMarker,
        requestMethodMarker,
        requestPayloadMarker,
        errorMessageMarker,
        errorDataMarker,
        resolutionResultMarker,
    };
    foreach (var diagnostic in diagnostics)
    {
        foreach (var marker in markers)
        {
            True(
                diagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
                "App Server protocol wrapper string projection leaked a protected transport marker.");
        }
    }

    var wirePayloads = new[]
    {
        JsonSerializer.Serialize(initialize),
        JsonSerializer.Serialize(notification),
        JsonSerializer.Serialize(request),
        JsonSerializer.Serialize(error),
    };
    foreach (var marker in new[]
             {
                 codexHomeMarker,
                 notificationPayloadMarker,
                 requestIdMarker,
                 requestPayloadMarker,
                 errorMessageMarker,
                 errorDataMarker,
             })
    {
        True(
            wirePayloads.Any(payload => payload.Contains(marker, StringComparison.Ordinal)),
            "A transport field changed while hardening only the diagnostic string projection.");
    }

    return Task.CompletedTask;
}

static Task AppServerControlWrapperStringsAreSafe()
{
    const string threadMarker = "interrupt-thread-marker";
    const string turnMarker = "interrupt-turn-marker";
    const string requestIdMarker = "approval-request-id-marker";
    const string commandMarker = "approval-command-marker";
    const string workingDirectoryMarker = "approval-working-directory-marker";
    const string reasonMarker = "approval-reason-marker";

    var interrupt = new TurnInterruptParams(threadMarker, turnMarker);
    var approvalRequest = new CommandApprovalRequest(
        "approval-item-marker",
        123,
        threadMarker,
        turnMarker,
        Command: "cmd /c " + commandMarker,
        WorkingDirectory: $@"C:\Users\{workingDirectoryMarker}\workspace",
        Reason: reasonMarker);
    var approval = new RpcApprovalEvent<CommandApprovalRequest>(
        new JsonRpcId(requestIdMarker),
        approvalRequest);

    var interruptDiagnostic = interrupt.ToString();
    var approvalDiagnostic = approval.ToString();
    Equal(
        "TurnInterruptParams { ThreadIdConfigured = True, TurnIdConfigured = True }",
        interruptDiagnostic);
    Equal(
        "RpcApprovalEvent { RequestIdConfigured = True, RequestPresent = True }",
        approvalDiagnostic);

    foreach (var diagnostic in new[] { interruptDiagnostic, approvalDiagnostic })
    {
        foreach (var marker in new[]
                 {
                     threadMarker,
                     turnMarker,
                     requestIdMarker,
                     commandMarker,
                     workingDirectoryMarker,
                     reasonMarker,
                 })
        {
            True(
                diagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
                "App Server control wrapper string projection leaked a protected marker.");
        }
    }

    Equal(threadMarker, interrupt.ThreadId);
    Equal(turnMarker, interrupt.TurnId);
    Equal(requestIdMarker, approval.RequestId.Text);
    True(
        ReferenceEquals(approvalRequest, approval.Request),
        "The approval payload changed while hardening only the diagnostic string projection.");

    return Task.CompletedTask;
}

static Task AppServerApprovalPayloadStringsAreSafe()
{
    const string marker = "approval-payload-protected-marker";
    using var pathDocument = JsonDocument.Parse(
        $$"""{"path":"C:\\Users\\{{marker}}\\workspace"}""");
    using var previewDocument = JsonDocument.Parse(
        $$"""{"preview":"{{marker}}"}""");
    using var decisionDocument = JsonDocument.Parse(
        $$"""{"decision":"{{marker}}"}""");

    var commandAction = new CommandAction(
        marker,
        "cmd /c " + marker,
        marker,
        $@"C:\Users\{marker}\script.cmd",
        marker);
    var networkContext = new NetworkApprovalContext(marker, marker);
    var networkAmendment = new NetworkPolicyAmendment(marker, marker);
    var sandboxEntry = new FileSystemSandboxEntry(
        marker,
        pathDocument.RootElement.GetProperty("path").Clone());
    var fileSystemPermissions = new AdditionalFileSystemPermissions(
        new[] { sandboxEntry },
        3,
        new[] { $@"C:\Users\{marker}\read" },
        new[] { $@"C:\Users\{marker}\write" });
    var networkPermissions = new AdditionalNetworkPermissions(true);
    var permissionProfile = new PermissionProfile(fileSystemPermissions, networkPermissions);
    var commandRequest = new CommandApprovalRequest(
        marker,
        123,
        marker,
        marker,
        permissionProfile,
        marker,
        new[] { decisionDocument.RootElement.Clone() },
        "cmd /c " + marker,
        new[] { commandAction },
        $@"C:\Users\{marker}\cwd",
        marker,
        networkContext,
        new[] { marker },
        new[] { networkAmendment },
        marker);
    var commandResponse = CommandApprovalResponse.WithExecPolicyAmendment(new[] { marker });
    var networkResponse = CommandApprovalResponse.WithNetworkPolicyAmendment(networkAmendment);
    var fileRequest = new FileChangeApprovalRequest(
        marker,
        124,
        marker,
        marker,
        $@"C:\Users\{marker}\grant",
        marker);
    var permissionsRequest = new PermissionsApprovalRequest(
        $@"C:\Users\{marker}\permissions",
        marker,
        permissionProfile,
        125,
        marker,
        marker,
        marker,
        marker);
    var permissionsResponse = new PermissionsApprovalResponse(
        permissionProfile,
        PermissionGrantScope.Turn,
        true);
    var documentIdentity = new CadDocumentIdentity(marker, marker, 126, marker);
    var changeSummary = new CadChangeSummary(1, 2, 3, marker);
    var cadRequest = new Codex.AutoCAD.AppServer.Protocol.CadApprovalRequest(
        marker,
        marker,
        marker,
        documentIdentity,
        marker,
        marker,
        127,
        changeSummary,
        previewDocument.RootElement.Clone());
    var cadResponse = new Codex.AutoCAD.AppServer.Protocol.CadApprovalResponse(
        Codex.AutoCAD.AppServer.Protocol.CadApprovalDecision.Accept,
        marker,
        marker);

    object[] values =
    {
        commandAction,
        networkContext,
        networkAmendment,
        sandboxEntry,
        fileSystemPermissions,
        networkPermissions,
        permissionProfile,
        commandRequest,
        commandResponse,
        networkResponse,
        fileRequest,
        permissionsRequest,
        permissionsResponse,
        documentIdentity,
        changeSummary,
        cadRequest,
        cadResponse,
    };

    foreach (var value in values)
    {
        var diagnostic = value.ToString() ?? string.Empty;
        True(
            diagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
            value.GetType().Name + " string projection leaked approval payload.");
        True(
            diagnostic.Length <= 512,
            value.GetType().Name + " string projection exceeded the public diagnostic bound.");
    }

    var wireJson = string.Join(
        "\n",
        values.Select(value => JsonSerializer.Serialize(value, value.GetType())));
    True(
        wireJson.Contains(marker, StringComparison.Ordinal),
        "Approval wire fields changed while hardening only diagnostic string projections.");

    return Task.CompletedTask;
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

static async Task TransportStandardErrorObserverFailureIsIsolated()
{
    var directory = CreateTemporaryDirectory("stderr-observer-isolation");
    try
    {
        var scriptPath = Path.Combine(directory, "stderr-child.cmd");
        File.WriteAllText(
            scriptPath,
            "@echo off\r\necho observer-summary 1>&2\r\nexit /b 0\r\n",
            Encoding.ASCII);

        await using var transport = new CodexProcessTransport(new AppServerClientOptions
        {
            CodexExecutablePath = scriptPath,
            WorkingDirectory = directory,
            MaximumStandardErrorBytes = 1_024,
        });
        var laterObserver = new TaskCompletionSource<AppServerStandardErrorSummary>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<AppServerTransportExitedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        transport.StandardErrorReceived += (_, _) => throw new InvalidOperationException(
            "Transport stderr observer failed.");
        transport.StandardErrorReceived += (_, eventArgs) =>
            laterObserver.TrySetResult(eventArgs.Summary);
        transport.Exited += (_, eventArgs) => exited.TrySetResult(eventArgs);

        await transport.StartAsync();
        var exit = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var summary = await laterObserver.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Equal<int?>(0, exit.ExitCode);
        True(summary.Bytes > 0, "The later stderr observer did not receive the drained summary.");
        Equal(1, exit.StandardErrorTail.Count);
        Equal(summary.Bytes, exit.StandardErrorTail[0].Bytes);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task IsolatedChildDoesNotInheritArbitraryParentEnvironment()
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

static Task LocalCodexConfigurationUsesCompatibilityAllowlist()
{
    const string arbitraryVariable = "CODEX_AUTOCAD_TEST_NOT_ALLOWLISTED";
    const string proxyValue = "http://127.0.0.1:18765";
    var previousArbitrary = Environment.GetEnvironmentVariable(arbitraryVariable);
    var previousProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
    try
    {
        Environment.SetEnvironmentVariable(arbitraryVariable, "must-not-propagate");
        Environment.SetEnvironmentVariable("HTTPS_PROXY", proxyValue);

        using var fixture = new LocalCodexConfigurationFixture();
        var configuration = CodexLocalAppServerConfigurationResolver.Resolve(
            fixture.CreateRequest(commandLineExecutablePath: fixture.ExecutablePath));
        var options = configuration.CreateClientOptions();
        var requiredNames = new[]
        {
            "APPDATA",
            "ComSpec",
            "GCM_INTERACTIVE",
            "GIT_CONFIG_GLOBAL",
            "GIT_CONFIG_NOSYSTEM",
            "GIT_TERMINAL_PROMPT",
            "HOME",
            "HTTPS_PROXY",
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
        True(
            requiredNames.All(options.Environment.ContainsKey),
            "Compatibility allowlist omitted a required operating-system or connectivity variable.");
        Equal(fixture.TempDirectory, options.Environment["TEMP"]);
        Equal(fixture.TempDirectory, options.Environment["TMP"]);
        Equal(proxyValue, options.Environment["HTTPS_PROXY"]);
        True(
            !options.Environment.ContainsKey(arbitraryVariable),
            "Compatibility allowlist copied an arbitrary parent variable.");
        True(!options.Environment.ContainsKey("CODEX_HOME"), "Policy unexpectedly sets CODEX_HOME.");
        True(!options.Environment.ContainsKey("CODEX_ACCESS_TOKEN"), "Policy inherited a Codex access token.");
        True(!options.Environment.ContainsKey("OPENAI_API_KEY"), "Policy inherited an API key.");
        True(!options.Environment.ContainsKey("PSModulePath"), "Policy inherited PowerShell module state.");
        return Task.CompletedTask;
    }
    finally
    {
        Environment.SetEnvironmentVariable(arbitraryVariable, previousArbitrary);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", previousProxy);
    }
}

static async Task IsolatedCodexHomeOverridesParentAndReachesChild()
{
    var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
    using var fixture = new LocalCodexConfigurationFixture();
    var globalHome = Path.Combine(fixture.DirectoryPath, "global-home");
    var sessionHome = Path.Combine(fixture.DirectoryPath, "session-home");
    Directory.CreateDirectory(globalHome);
    Directory.CreateDirectory(sessionHome);
    try
    {
        Environment.SetEnvironmentVariable("CODEX_HOME", globalHome);
        var configuration = CodexLocalAppServerConfigurationResolver.Resolve(
            fixture.CreateRequest(
                commandLineExecutablePath: fixture.ExecutablePath,
                codexHomeDirectory: sessionHome));
        var options = configuration.CreateClientOptions();

        Equal(sessionHome, options.Environment["CODEX_HOME"]);
        True(!options.InheritParentEnvironment, "Isolated Codex home still inherited the parent environment.");

        const string expectedVariable = "CODEX_AUTOCAD_TEST_EXPECTED_HOME";
        var environment = new Dictionary<string, string?>(
            options.Environment,
            StringComparer.OrdinalIgnoreCase)
        {
            [expectedVariable] = sessionHome,
        };
        var scriptPath = WriteEnvironmentProbe(
            fixture.DirectoryPath,
            "if /I \"%CODEX_HOME%\"==\"%" + expectedVariable
                + "%\" (echo home=isolated) else (echo home=unexpected)");
        var output = await RunProcessProbeAsync(options with
        {
            CodexExecutablePath = scriptPath,
            Environment = environment,
        });

        ContainsLine(output, "home=isolated");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
    }
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

static Task LocalCodexConfigurationRejectsInvalidCodexHomeDirectory()
{
    using var fixture = new LocalCodexConfigurationFixture();
    var exception = Capture<CodexLocalConfigurationException>(() =>
        CodexLocalAppServerConfigurationResolver.Resolve(
            fixture.CreateRequest(
                commandLineExecutablePath: fixture.ExecutablePath,
                codexHomeDirectory: "relative-codex-home")));

    Equal(CodexLocalConfigurationFailure.InvalidCodexHomeDirectory, exception.Failure);
    True(
        !exception.Message.Contains(fixture.DirectoryPath, StringComparison.OrdinalIgnoreCase),
        "Codex-home error exposed a local path.");
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
    True(CodexVersionCompatibility.Default.IsSupported(observed), "Minimum version was rejected.");
    True(
        CodexVersionCompatibility.Default.IsSupported(new CodexSemanticVersion(0, 144, 99)),
        "Compatible patch version was rejected.");
    True(
        !CodexVersionCompatibility.Default.IsSupported(new CodexSemanticVersion(0, 145, 0)),
        "Unreviewed minor version was accepted.");
    True(
        !CodexVersionPreflight.TryParseVersion("codex-cli 0.144.4-preview", out _),
        "Unreviewed prerelease was accepted.");
    True(
        !CodexVersionPreflight.TryParseVersion("codex-cli 0.144.4\nother", out _),
        "Ambiguous multi-line output was accepted.");
    return Task.CompletedTask;
}

static async Task CodexVersionPreflightUsesLockedIdentityAndIsolatedEnvironment()
{
    const string parentVariable = "CODEX_AUTOCAD_VERSION_PREFLIGHT_PARENT";
    var previousValue = Environment.GetEnvironmentVariable(parentVariable);
    using var fixture = new VersionPreflightFixture("isolated");
    try
    {
        Environment.SetEnvironmentVariable(parentVariable, "must-not-propagate");
        var configuration = fixture.Resolve();
        using var launch = await CodexVersionPreflight.VerifyAsync(configuration);
        var options = launch.CreateClientOptions();

        Equal(new CodexSemanticVersion(0, 144, 4), launch.Version.Version);
        Equal(">=0.144.4 <0.145.0", launch.Version.Compatibility.ToString());
        Equal("--version", File.ReadAllText(fixture.ArgumentsPath, Encoding.UTF8));
        True(!options.InheritParentEnvironment, "Version preflight inherited the parent environment.");
        True(options.ExecutableLease is not null, "Verified launch omitted its executable identity lease.");
        options.ExecutableLease!.ValidateCurrentPath(options.CodexExecutablePath);
    }
    finally
    {
        Environment.SetEnvironmentVariable(parentVariable, previousValue);
    }
}

static async Task UnsupportedCodexVersionFailsClosed()
{
    using var fixture = new VersionPreflightFixture("unsupported");
    var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
        CodexVersionPreflight.VerifyAsync(fixture.Resolve()));

    Equal(CodexVersionPreflightFailure.UnsupportedVersion, exception.Failure);
    True(
        !exception.Message.Contains(fixture.DirectoryPath, StringComparison.OrdinalIgnoreCase),
        "Version preflight error exposed a local path.");
}

static async Task CodexVersionProcessExitFailsClosed()
{
    using var fixture = new VersionPreflightFixture("error");
    var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
        CodexVersionPreflight.VerifyAsync(fixture.Resolve()));

    Equal(CodexVersionPreflightFailure.ProcessExitedWithError, exception.Failure);
    True(
        !exception.Message.Contains(VersionPreflightFixture.PrivateStderrMarker, StringComparison.Ordinal),
        "Version preflight error exposed stderr text.");
}

static async Task InvalidCodexVersionOutputFailsClosed()
{
    using (var oversized = new VersionPreflightFixture("oversized"))
    {
        var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
            CodexVersionPreflight.VerifyAsync(oversized.Resolve()));
        Equal(CodexVersionPreflightFailure.VersionOutputTooLarge, exception.Failure);
    }

    using (var nonUtf8 = new VersionPreflightFixture("nonutf8"))
    {
        var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
            CodexVersionPreflight.VerifyAsync(nonUtf8.Resolve()));
        Equal(CodexVersionPreflightFailure.InvalidVersionOutput, exception.Failure);
    }
}

static async Task CodexVersionTimeoutCleansDescendant()
{
    using var fixture = new VersionPreflightFixture(
        "descendant",
        startupTimeout: TimeSpan.FromMilliseconds(250));
    var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
        CodexVersionPreflight.VerifyAsync(fixture.Resolve()));

    Equal(CodexVersionPreflightFailure.TimedOut, exception.Failure);
    var descendantId = await fixture.ReadDescendantProcessIdAsync();
    await RequireProcessExitAsync(descendantId, TimeSpan.FromSeconds(5));
}

static async Task CodexVersionCancellationCleansProcess()
{
    using var fixture = new VersionPreflightFixture(
        "timeout",
        startupTimeout: TimeSpan.FromSeconds(5));
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
    var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
        CodexVersionPreflight.VerifyAsync(fixture.Resolve(), cancellation.Token));

    Equal(CodexVersionPreflightFailure.Cancelled, exception.Failure);
    var childId = int.Parse(
        await WaitForFileTextAsync(fixture.ProcessIdPath, TimeSpan.FromSeconds(5)),
        System.Globalization.CultureInfo.InvariantCulture);
    await RequireProcessExitAsync(childId, TimeSpan.FromSeconds(5));
}

static async Task CodexVersionTerminationFailureIsBounded()
{
    var stopwatch = Stopwatch.StartNew();
    var exception = await CaptureAsync<CodexVersionPreflightException>(() =>
        CodexVersionPreflight.VerifyProcessAsync(
            new AppServerClientOptions
            {
                CodexExecutablePath = "unused.exe",
                WorkingDirectory = Environment.CurrentDirectory,
            },
            CodexVersionCompatibility.Default,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(40),
            _ => new UnterminableVersionProcess()));
    stopwatch.Stop();

    Equal(CodexVersionPreflightFailure.TerminationFailed, exception.Failure);
    True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "Termination failure exceeded its hard bound.");
}

static Task CodexExecutableLeasePreventsReplacement()
{
    using var fixture = new LocalCodexConfigurationFixture();
    var configuration = CodexLocalAppServerConfigurationResolver.Resolve(
        fixture.CreateRequest(commandLineExecutablePath: fixture.ExecutablePath));
    using var lease = CodexExecutableLease.Acquire(configuration.CodexExecutablePath);

    Throws<IOException>(() =>
    {
        using var write = new FileStream(
            fixture.ExecutablePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
    });
    lease.ValidateCurrentPath(configuration.CodexExecutablePath);
    var movedPath = fixture.DirectoryPath + "-moved";
    var directoryMoveBlocked = false;
    try
    {
        Directory.Move(fixture.DirectoryPath, movedPath);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        directoryMoveBlocked = true;
    }
    finally
    {
        if (Directory.Exists(movedPath) && !Directory.Exists(fixture.DirectoryPath))
        {
            Directory.Move(movedPath, fixture.DirectoryPath);
        }
    }

    True(directoryMoveBlocked, "Executable identity lease allowed its parent directory to move.");
    using var retainedReference = lease.AcquireReference();
    lease.Dispose();
    retainedReference.Lease.ValidateCurrentPath(configuration.CodexExecutablePath);
    Throws<IOException>(() =>
    {
        using var write = new FileStream(
            fixture.ExecutablePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
    });
    retainedReference.Dispose();
    using (var write = new FileStream(
        fixture.ExecutablePath,
        FileMode.Open,
        FileAccess.Write,
        FileShare.ReadWrite | FileShare.Delete))
    {
    }

    return Task.CompletedTask;
}

static async Task AppServerStopTimeoutAndCancellationCleanProcess()
{
    var directory = CreateTemporaryDirectory("transport-stop");
    try
    {
        var scriptPath = Path.Combine(directory, "slow-appserver.cmd");
        File.WriteAllLines(
            scriptPath,
            new[]
            {
                "@echo off",
                "ping 127.0.0.1 -n 30 > nul",
                "exit /b 0",
            },
            Encoding.ASCII);

        await using (var timeoutTransport = new CodexProcessTransport(new AppServerClientOptions
        {
            CodexExecutablePath = scriptPath,
            WorkingDirectory = directory,
        }))
        {
            await timeoutTransport.StartAsync();
            await timeoutTransport.StopAsync(TimeSpan.FromMilliseconds(50));
            True(!timeoutTransport.IsRunning, "Timeout stop left the App Server process running.");
        }

        await using (var cancelledTransport = new CodexProcessTransport(new AppServerClientOptions
        {
            CodexExecutablePath = scriptPath,
            WorkingDirectory = directory,
        }))
        {
            await cancelledTransport.StartAsync();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() =>
                cancelledTransport.StopAsync(TimeSpan.FromSeconds(5), cancellation.Token));
            True(!cancelledTransport.IsRunning, "Cancelled stop left the App Server process running.");
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task<int> RunFakeCodexVersionProbeAsync()
{
    var directory = Environment.CurrentDirectory;
    var mode = (await File.ReadAllTextAsync(
            Path.Combine(directory, VersionPreflightFixture.ModeFileName),
            Encoding.UTF8))
        .Trim();
    await File.WriteAllTextAsync(
        Path.Combine(directory, VersionPreflightFixture.ArgumentsFileName),
        string.Join(" ", Environment.GetCommandLineArgs().Skip(1)),
        new UTF8Encoding(false));
    await File.WriteAllTextAsync(
        Path.Combine(directory, VersionPreflightFixture.ProcessIdFileName),
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        new UTF8Encoding(false));

    switch (mode)
    {
        case "valid":
            Console.WriteLine("codex-cli 0.144.4");
            return 0;
        case "isolated":
            if (Environment.GetEnvironmentVariable("CODEX_AUTOCAD_VERSION_PREFLIGHT_PARENT") is not null)
            {
                return 17;
            }

            Console.WriteLine("codex-cli 0.144.4");
            return 0;
        case "unsupported":
            Console.WriteLine("codex-cli 0.145.0");
            return 0;
        case "error":
            Console.Error.WriteLine(VersionPreflightFixture.PrivateStderrMarker);
            return 23;
        case "oversized":
            await Console.OpenStandardOutput().WriteAsync(
                new byte[CodexVersionPreflight.MaximumVersionOutputBytes + 1]);
            return 0;
        case "nonutf8":
            await Console.OpenStandardOutput().WriteAsync(new byte[] { 0xff, 0xfe, 0xfd });
            return 0;
        case "descendant":
        {
            var pingPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "ping.exe");
            using var descendant = Process.Start(new ProcessStartInfo
            {
                FileName = pingPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "127.0.0.1", "-n", "30" },
            }) ?? throw new InvalidOperationException("Fake descendant did not start.");
            await File.WriteAllTextAsync(
                Path.Combine(directory, VersionPreflightFixture.DescendantIdFileName),
                descendant.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new UTF8Encoding(false));
            await Task.Delay(TimeSpan.FromSeconds(30));
            return 0;
        }
        case "timeout":
            await Task.Delay(TimeSpan.FromSeconds(30));
            return 0;
        default:
            return 29;
    }
}

static async Task RequireProcessExitAsync(int processId, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }
        }
        catch (ArgumentException)
        {
            return;
        }

        await Task.Delay(25);
    }

    throw new InvalidOperationException("Version preflight left a residual process.");
}

static async Task<string> WaitForFileTextAsync(string path, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            if (File.Exists(path))
            {
                var value = await File.ReadAllTextAsync(path, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }
        catch (IOException)
        {
        }

        await Task.Delay(25);
    }

    throw new InvalidOperationException("Version preflight probe did not publish expected evidence.");
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

    throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
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

internal sealed class VersionPreflightFixture : IDisposable
{
    internal const string ModeFileName = "codex-version-probe.mode";
    internal const string ArgumentsFileName = "codex-version-arguments.txt";
    internal const string ProcessIdFileName = "codex-version-process.pid";
    internal const string DescendantIdFileName = "codex-version-descendant.pid";
    internal const string PrivateStderrMarker = "version-preflight-private-stderr-marker";

    private readonly TimeSpan _startupTimeout;

    internal VersionPreflightFixture(
        string mode,
        TimeSpan? startupTimeout = null)
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-appserver-version-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        TempDirectory = Path.Combine(DirectoryPath, "temp");
        Directory.CreateDirectory(TempDirectory);
        ExecutablePath = Path.Combine(
            AppContext.BaseDirectory,
            "Codex.AutoCAD.AppServer.Specs.exe");
        if (!File.Exists(ExecutablePath))
        {
            throw new InvalidOperationException("Version preflight test apphost is unavailable.");
        }

        File.WriteAllText(
            Path.Combine(DirectoryPath, ModeFileName),
            mode,
            new UTF8Encoding(false));
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(5);
    }

    internal string DirectoryPath { get; }

    internal string TempDirectory { get; }

    internal string ExecutablePath { get; }

    internal string ArgumentsPath => Path.Combine(DirectoryPath, ArgumentsFileName);

    internal string ProcessIdPath => Path.Combine(DirectoryPath, ProcessIdFileName);

    internal CodexLocalAppServerConfiguration Resolve()
    {
        return CodexLocalAppServerConfigurationResolver.Resolve(
            new CodexLocalAppServerConfigurationRequest
            {
                CommandLineExecutablePath = ExecutablePath,
                ApplicationDataDirectory = null,
                PathValue = null,
                WorkingDirectory = DirectoryPath,
                TemporaryDirectory = TempDirectory,
                StartupTimeout = _startupTimeout,
                ShutdownTimeout = TimeSpan.FromSeconds(2),
            });
    }

    internal async Task<int> ReadDescendantProcessIdAsync()
    {
        var path = Path.Combine(DirectoryPath, DescendantIdFileName);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    var value = await File.ReadAllTextAsync(path, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return int.Parse(
                            value.Trim(),
                            System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
            catch (IOException)
            {
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException("Version preflight descendant evidence was not published.");
    }

    public void Dispose()
    {
        Directory.Delete(DirectoryPath, recursive: true);
    }
}

internal sealed class UnterminableVersionProcess : ICodexVersionProcess
{
    private readonly MemoryStream _standardOutput = new();
    private readonly MemoryStream _standardError = new();

    public Stream StandardOutput => _standardOutput;

    public Stream StandardError => _standardError;

    public int ExitCode => throw new InvalidOperationException("Process is still running.");

    public bool HasExited => false;

    public void CloseStandardInput()
    {
    }

    public void KillProcessTree()
    {
        throw new Win32Exception(5);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public void Dispose()
    {
        _standardOutput.Dispose();
        _standardError.Dispose();
    }
}

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
        string? temporaryDirectory = null,
        string? codexHomeDirectory = null)
    {
        return new CodexLocalAppServerConfigurationRequest
        {
            CommandLineExecutablePath = commandLineExecutablePath,
            EnvironmentExecutablePath = environmentExecutablePath,
            ApplicationDataDirectory = null,
            PathValue = pathValue,
            WorkingDirectory = DirectoryPath,
            TemporaryDirectory = temporaryDirectory ?? TempDirectory,
            CodexHomeDirectory = codexHomeDirectory,
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

    public event EventHandler<AppServerStandardErrorEventArgs>? StandardErrorReceived;

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

    public void InjectStandardError(AppServerStandardErrorSummary summary)
    {
        StandardErrorReceived?.Invoke(this, new AppServerStandardErrorEventArgs(summary));
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
