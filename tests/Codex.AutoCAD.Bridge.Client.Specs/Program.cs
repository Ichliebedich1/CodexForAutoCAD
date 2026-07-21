using System.Diagnostics;
using Codex.AutoCAD.Bridge.Client;
using Codex.AutoCAD.Contracts;

const string explicitEmptySchemasSpecId = "bridge-client-explicit-empty-schemas-fail-closed";
const string absentSchemasSpecId = "bridge-client-absent-schemas-v1-default";
const string v1v2SchemasSpecId = "bridge-client-v1v2-schemas-pass";
const string duplicateSchemasSpecId = "bridge-client-duplicate-schemas-rejected";
const string nullEntrySchemasSpecId = "bridge-client-null-entry-schemas-rejected";
const string explicitNullSchemasSpecId = "bridge-client-explicit-null-schemas-rejected";
const string versionDecimalSpecId = "bridge-client-schema-version-decimal-rejected";
const string versionScientificSpecId = "bridge-client-schema-version-scientific-rejected";
const string v1OnlySchemasSpecId = "bridge-client-v1-only-schemas-pass";
const string v2OnlySchemasSpecId = "bridge-client-v2-only-schemas-rejected";
const string mixedNullEntrySchemasSpecId = "bridge-client-mixed-null-entry-schemas-rejected";
const string missingVersionFieldSpecId = "bridge-client-missing-version-field-rejected";
const string capabilitiesSpecId = "bridge-client-capabilities-cross-runtime";
const string threadSpecId = "bridge-client-thread-start-cross-runtime";
const string turnSpecId = "bridge-client-turn-start-with-context-cross-runtime";
const string assistantEventsSpecId = "bridge-client-assistant-events-cross-runtime";
const string interruptSpecId = "bridge-client-turn-interrupt-cross-runtime";
const string terminalLateEventSpecId = "bridge-client-terminal-turn-rejects-late-event";
const string approvalSpecId = "bridge-client-approval-resolve-cross-runtime";
const string stopIdempotentSpecId = "bridge-client-concurrent-stop-idempotent";
const string offlineSpecId = "bridge-client-offline-fail-closed";
const string disconnectSpecId = "bridge-client-disconnect-fail-closed";
const string timeoutSpecId = "bridge-client-request-timeout-fail-closed";
const string cancellationSpecId = "bridge-client-request-cancellation-fail-closed";
const string disposeIdempotentSpecId = "bridge-client-dispose-idempotent";
const string badMacSpecId = "bridge-client-bad-mac-fail-closed";
const string sequenceGapSpecId = "bridge-client-sequence-gap-fail-closed";
const string nonceReplaySpecId = "bridge-client-nonce-replay-fail-closed";
const string unknownFieldSpecId = "bridge-client-unknown-field-fail-closed";
const string duplicateFieldSpecId = "bridge-client-duplicate-field-fail-closed";
const string wrongCaseSpecId = "bridge-client-wrong-case-fail-closed";
const string trailingJsonSpecId = "bridge-client-trailing-json-fail-closed";
const string invalidUtf8SpecId = "bridge-client-invalid-utf8-fail-closed";
const string oversizedFrameSpecId = "bridge-client-oversized-frame-fail-closed";
var currentSpecId = explicitEmptySchemasSpecId;

// Codec-level regression tests for supportedCadContextSchemas fail-closed behavior.
// These tests verify the JSON decoder correctly distinguishes between:
//   - absent field (backward compat → v1 default)
//   - explicit null (fail-closed → rejected, NOT same as absent)
//   - explicit empty array (fail-closed → rejected)
//   - valid v1+v2, v1-only, v2-only, duplicate, null entry, malformed versions

try
{
    // TC-1: Explicit empty supportedCadContextSchemas → codec rejects (fail-closed)
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[]");
        var failure = CaptureAgentBridgeFailure(
            () => BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json));
        Require(failure.Code == AgentBridgeErrorCodes.ContractMismatch,
            "explicit empty schemas must be rejected by codec");
    }
    Console.WriteLine("[PASS] " + explicitEmptySchemasSpecId);

    currentSpecId = absentSchemasSpecId;
    // TC-2: Absent supportedCadContextSchemas → v1 default preserved → validation passes
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: null);
        var response = BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json);
        Require(response.SupportedCadContextSchemas.Length == 1,
            "absent schemas must produce v1 default");
        Require(response.SupportedCadContextSchemas[0].Schema == CadContextJsonV1Constants.Schema,
            "absent schemas default schema must be v1");
        Require(response.SupportedCadContextSchemas[0].SchemaVersion == CadContextJsonV1Constants.SchemaVersion,
            "absent schemas default version must be v1");
        var failures = AgentBridgeContractValidator.Validate(response);
        Require(failures.Length == 0,
            "absent schemas with v1 default must pass validation");
    }
    Console.WriteLine("[PASS] " + absentSchemasSpecId);

    currentSpecId = v1v2SchemasSpecId;
    // TC-3: v1+v2 schemas → decoded correctly → validation passes
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[{\"schema\":\"codex.autocad.cad-context\",\"schemaVersion\":1},{\"schema\":\"codex.autocad.cad-context\",\"schemaVersion\":2}]");
        var response = BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json);
        Require(response.SupportedCadContextSchemas.Length == 2,
            "v1+v2 schemas must produce two entries");
        Require(response.SupportedCadContextSchemas[0].SchemaVersion == 1,
            "first schema version must be 1");
        Require(response.SupportedCadContextSchemas[1].SchemaVersion == 2,
            "second schema version must be 2");
        var failures = AgentBridgeContractValidator.Validate(response);
        Require(failures.Length == 0,
            "v1+v2 schemas must pass validation");
    }
    Console.WriteLine("[PASS] " + v1v2SchemasSpecId);

    currentSpecId = duplicateSchemasSpecId;
    // TC-4: Duplicate schemas → codec rejects (fail-closed)
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[{\"schema\":\"codex.autocad.cad-context\",\"schemaVersion\":1},{\"schema\":\"codex.autocad.cad-context\",\"schemaVersion\":1}]");
        var failure = CaptureAgentBridgeFailure(
            () => BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json));
        Require(failure.Code == AgentBridgeErrorCodes.ContractMismatch,
            "duplicate schemas must be rejected by codec");
    }
    Console.WriteLine("[PASS] " + duplicateSchemasSpecId);

    currentSpecId = nullEntrySchemasSpecId;
    // TC-5: Null entry inside schema array → codec rejects (fail-closed)
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[null]");
        var failure = CaptureAgentBridgeFailure(
            () => BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json));
        Require(failure.Code == AgentBridgeErrorCodes.ContractMismatch
                || failure.Code == "request_invalid",
            "null entry in schema array must be rejected by codec");
    }
    Console.WriteLine("[PASS] " + nullEntrySchemasSpecId);

    currentSpecId = explicitNullSchemasSpecId;
    // TC-6: Explicit null supportedCadContextSchemas → codec rejects (NOT same as absent)
    {
        var json = BuildCapabilitiesResponseJsonWithRawField(
            "\"supportedCadContextSchemas\":null");
        var failure = CaptureAgentBridgeFailure(
            () => BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json));
        Require(failure.Code == "request_invalid",
            "explicit null schemas must be rejected as invalid JSON type");
    }
    Console.WriteLine("[PASS] " + explicitNullSchemasSpecId);

    currentSpecId = versionDecimalSpecId;
    // TC-7: schemaVersion as 1.0 (decimal notation) → codec rejects
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[{\"schema\":\"codex.autocad.cad-context\",\"schemaVersion\":1.0}]");
        var failure = CaptureAgentBridgeFailure(
            () => BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json));
        Require(failure.Code == "request_invalid",
            "decimal schemaVersion 1.0 must be rejected as non-integer");
    }
    Console.WriteLine("[PASS] " + versionDecimalSpecId);

    currentSpecId = versionScientificSpecId;
    // TC-8: schemaVersion as 1e0 (scientific notation) → codec rejects
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[{\"schema\":\"codex.autocad.cad-context\",\"schemaVersion\":1e0}]");
        var failure = CaptureAgentBridgeFailure(
            () => BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json));
        Require(failure.Code == "request_invalid",
            "scientific schemaVersion 1e0 must be rejected as non-integer");
    }
    Console.WriteLine("[PASS] " + versionScientificSpecId);

    currentSpecId = v1OnlySchemasSpecId;
    // TC-9: v1-only schemas → decoded correctly → validation passes
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[{\"schema\":\"codex.autocad.cad-context\",\"schemaVersion\":1}]");
        var response = BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json);
        Require(response.SupportedCadContextSchemas.Length == 1,
            "v1-only schemas must produce one entry");
        Require(response.SupportedCadContextSchemas[0].SchemaVersion == 1,
            "v1-only schema version must be 1");
        var failures = AgentBridgeContractValidator.Validate(response);
        Require(failures.Length == 0,
            "v1-only schemas must pass validation");
    }
    Console.WriteLine("[PASS] " + v1OnlySchemasSpecId);

    currentSpecId = v2OnlySchemasSpecId;
    // TC-10: v2-only schemas → codec rejects (v1 always required)
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[{\"schema\":\"codex.autocad.cad-context\",\"schemaVersion\":2}]");
        var failure = CaptureAgentBridgeFailure(
            () => BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json));
        Require(failure.Code == AgentBridgeErrorCodes.ContractMismatch,
            "v2-only schemas must be rejected (v1 required)");
    }
    Console.WriteLine("[PASS] " + v2OnlySchemasSpecId);

    currentSpecId = mixedNullEntrySchemasSpecId;
    // TC-11: [valid v1, null] → codec rejects (mixed null entry in array)
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[{\"schema\":\"codex.autocad.cad-context\",\"schemaVersion\":1},null]");
        var failure = CaptureAgentBridgeFailure(
            () => BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json));
        Require(failure.Code == "request_invalid",
            "mixed null entry in schema array must be request_invalid");
    }
    Console.WriteLine("[PASS] " + mixedNullEntrySchemasSpecId);

    currentSpecId = missingVersionFieldSpecId;
    // TC-12: Raw JSON schema entry missing schemaVersion field → codec rejects (wire-level absence)
    {
        var json = BuildCapabilitiesResponseJson(
            supportedCadContextSchemasJson: "[{\"schema\":\"codex.autocad.cad-context\"}]");
        var failure = CaptureAgentBridgeFailure(
            () => BridgeClientJsonCodec.DeserializeCapabilitiesResponse(json));
        Require(failure.Code == "request_invalid",
            "wire-level missing schemaVersion must be request_invalid");
    }
    Console.WriteLine("[PASS] " + missingVersionFieldSpecId);
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "[FAIL] " + currentSpecId + ": " + exception.GetType().Name + ": " + exception.Message);
    return 1;
}

currentSpecId = capabilitiesSpecId;
var serverExe = Environment.GetEnvironmentVariable("CODEX_BRIDGE_TEST_SERVER_EXE");
if (string.IsNullOrWhiteSpace(serverExe) || !File.Exists(serverExe))
{
    Console.Error.WriteLine(
        "[FAIL] " + capabilitiesSpecId + ": CODEX_BRIDGE_TEST_SERVER_EXE is missing.");
    return 1;
}

var pipeName = "codex-bridge-client-spec-" + Guid.NewGuid().ToString("N");
var sessionId = "spec-session-" + Guid.NewGuid().ToString("N");
var secret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
var secretHex = BitConverter.ToString(secret).Replace("-", string.Empty);
Process? server = null;
Process? disconnectServer = null;
Process? timeoutServer = null;
Process? terminalLateServer = null;
Process? cancellationServer = null;
Process? badMacServer = null;
Process? sequenceGapServer = null;
Process? nonceReplayServer = null;

try
{
    server = StartTestServer(serverExe, pipeName, sessionId, secretHex, "happy");

    using (var client = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = pipeName,
        SessionId = sessionId,
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(5),
    }))
    {
        var receivedEvents = new List<AgentBridgeEvent>();
        using var eventGate = new ManualResetEventSlim(false);
        client.EventReceived += (_, eventArgs) =>
        {
            lock (receivedEvents)
            {
                receivedEvents.Add(eventArgs.BridgeEvent);
                if (receivedEvents.Count >= 2)
                {
                    eventGate.Set();
                }
            }
        };

        client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var response = client.GetCapabilitiesAsync(
                new AgentCapabilitiesRequest
                {
                    ClientName = "Codex.AutoCAD.Host.2016",
                    ClientVersion = "1.0.0.0",
                    HostTarget = "autocad-r20.1-net45-x64",
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Require(response.ContractVersion == 1, "contract version");
        Require(response.MinimumCompatibleVersion == 1, "minimum compatible version");
        Require(response.AgentInstanceId == "test-agent-instance", "agent instance identity");
        Require(response.CadContextSchema == CadContextJsonV1Constants.Schema, "context schema");
        Require(response.CadContextSchemaVersion == 1, "context schema version");
        Require(!response.CadWriteAvailable, "read-only capability");

        Console.WriteLine("[PASS] " + capabilitiesSpecId);
        currentSpecId = threadSpecId;
        var thread = client.StartThreadAsync(
                new AgentThreadStartRequest
                {
                    ConversationId = "conversation-test-1",
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Require(thread.ContractVersion == 1, "thread contract version");
        Require(thread.ThreadId == "thread-test-1", "thread identity");
        Console.WriteLine("[PASS] " + threadSpecId);

        currentSpecId = turnSpecId;
        var context = CreateCadContext();
        var contextSha256 = CadContextJsonV1Codec.ComputeCanonicalSha256(context);
        var turn = client.StartTurnAsync(
                new AgentTurnStartRequest
                {
                    ThreadId = thread.ThreadId,
                    ClientTurnId = "client-turn-test-1",
                    Prompt = "分析当前选中的直线。",
                    Context = context,
                    ContextSha256 = contextSha256,
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Require(turn.ContractVersion == 1, "turn contract version");
        Require(turn.ThreadId == thread.ThreadId, "turn thread identity");
        Require(turn.TurnId == "turn-test-1", "turn identity");
        Require(turn.AcceptedContextSha256 == contextSha256, "turn context identity");
        Console.WriteLine("[PASS] " + turnSpecId);

        currentSpecId = assistantEventsSpecId;
        _ = client.GetCapabilitiesAsync(
                new AgentCapabilitiesRequest
                {
                    ClientName = "Codex.AutoCAD.Host.2016",
                    ClientVersion = "1.0.0.0",
                    HostTarget = "autocad-r20.1-net45-x64",
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Require(eventGate.Wait(TimeSpan.FromSeconds(5)), "assistant event delivery timeout");
        AgentBridgeEvent[] eventSnapshot;
        lock (receivedEvents)
        {
            eventSnapshot = receivedEvents.ToArray();
        }

        Require(eventSnapshot.Length == 2, "assistant event count");
        Require(eventSnapshot[0].Kind == AgentBridgeEventKinds.AssistantMessageDelta,
            "assistant delta kind");
        Require(eventSnapshot[0].Sequence == 1, "assistant delta sequence");
        Require(eventSnapshot[0].Delta == "正在分析选中的直线。", "assistant delta text");
        Require(eventSnapshot[1].Kind == AgentBridgeEventKinds.AssistantMessageCompleted,
            "assistant completed kind");
        Require(eventSnapshot[1].Sequence == 2, "assistant completed sequence");
        Require(eventSnapshot[1].Content == "该直线从原点附近延伸至正X、正Y方向。",
            "assistant completed text");
        Require(eventSnapshot.All(item => item.ThreadId == turn.ThreadId),
            "assistant event thread identity");
        Require(eventSnapshot.All(item => item.TurnId == turn.TurnId),
            "assistant event turn identity");
        Require(eventSnapshot.All(item => item.ContextSha256 == contextSha256),
            "assistant event context identity");
        Console.WriteLine("[PASS] " + assistantEventsSpecId);

        currentSpecId = interruptSpecId;
        client.InterruptTurnAsync(
                new AgentTurnInterruptRequest
                {
                    ThreadId = turn.ThreadId,
                    TurnId = turn.TurnId,
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Console.WriteLine("[PASS] " + interruptSpecId);

        currentSpecId = approvalSpecId;
        client.ResolveApprovalAsync(
                new AgentApprovalResolveRequest
                {
                    ThreadId = turn.ThreadId,
                    TurnId = turn.TurnId,
                    ApprovalId = "approval-test-1",
                    Decision = AgentBridgeApprovalDecisions.DeclineAndContinue,
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Console.WriteLine("[PASS] " + approvalSpecId);

        currentSpecId = stopIdempotentSpecId;
        Task.WhenAll(
                client.StopAsync(CancellationToken.None),
                client.StopAsync(CancellationToken.None))
            .GetAwaiter()
            .GetResult();
        Console.WriteLine("[PASS] " + stopIdempotentSpecId);
    }

    if (!server.WaitForExit(5000))
    {
        throw new TimeoutException("Bridge test server did not exit after client stop.");
    }

    if (server.ExitCode != 0)
    {
        throw new InvalidOperationException(
            "Bridge test server failed with exit code " + server.ExitCode + ".");
    }

    currentSpecId = terminalLateEventSpecId;
    var terminalPipe = "codex-bridge-terminal-late-" + Guid.NewGuid().ToString("N");
    var terminalSession = "terminal-late-session-" + Guid.NewGuid().ToString("N");
    terminalLateServer = StartTestServer(
        serverExe,
        terminalPipe,
        terminalSession,
        secretHex,
        "terminal-late-event");
    using (var terminalClient = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = terminalPipe,
        SessionId = terminalSession,
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(2),
    }))
    {
        var terminalEvents = new List<AgentBridgeEvent>();
        AgentBridgeClientException? connectionFault = null;
        using var connectionFaultGate = new ManualResetEventSlim(false);
        terminalClient.EventReceived += (_, eventArgs) =>
        {
            lock (terminalEvents)
            {
                terminalEvents.Add(eventArgs.BridgeEvent);
            }
        };
        terminalClient.ConnectionFaulted += (_, eventArgs) =>
        {
            connectionFault = eventArgs.Exception;
            connectionFaultGate.Set();
        };

        terminalClient.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var terminalThread = terminalClient.StartThreadAsync(
                new AgentThreadStartRequest { ConversationId = "terminal-late-conversation" },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var terminalContext = CreateCadContext();
        var terminalContextSha256 = CadContextJsonV1Codec.ComputeCanonicalSha256(terminalContext);
        var terminalTurn = terminalClient.StartTurnAsync(
                new AgentTurnStartRequest
                {
                    ThreadId = terminalThread.ThreadId,
                    ClientTurnId = "client-turn-terminal-late-1",
                    Prompt = "验证终态后迟到事件拒绝。",
                    Context = terminalContext,
                    ContextSha256 = terminalContextSha256,
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var terminalFailure = CaptureAgentBridgeFailure(
            () => RequestCapabilities(terminalClient));
        Require(terminalFailure.Code == AgentBridgeErrorCodes.ResultIdentityMismatch,
            "terminal late event error code");
        Require(connectionFaultGate.Wait(TimeSpan.FromSeconds(5)),
            "terminal late event connection fault timeout");
        Require(connectionFault is not null
                && connectionFault.Code == AgentBridgeErrorCodes.ResultIdentityMismatch,
            "terminal late event connection fault code");
        AgentBridgeEvent[] terminalEventSnapshot;
        lock (terminalEvents)
        {
            terminalEventSnapshot = terminalEvents.ToArray();
        }

        Require(terminalEventSnapshot.Length == 1, "terminal event count before late event");
        Require(terminalEventSnapshot[0].Kind == AgentBridgeEventKinds.TurnCompleted,
            "terminal event kind");
        Require(terminalEventSnapshot[0].ThreadId == terminalTurn.ThreadId,
            "terminal event thread identity");
        Require(terminalEventSnapshot[0].TurnId == terminalTurn.TurnId,
            "terminal event turn identity");
        Require(terminalEventSnapshot[0].ContextSha256 == terminalContextSha256,
            "terminal event context identity");
    }

    Require(terminalLateServer.WaitForExit(5000), "terminal late event server exit");
    Require(terminalLateServer.ExitCode == 0, "terminal late event server exit code");
    Console.WriteLine("[PASS] " + terminalLateEventSpecId);

    currentSpecId = offlineSpecId;
    using (var offlineClient = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = "codex-bridge-offline-" + Guid.NewGuid().ToString("N"),
        SessionId = "offline-session-" + Guid.NewGuid().ToString("N"),
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromMilliseconds(250),
        RequestTimeout = TimeSpan.FromMilliseconds(250),
    }))
    {
        var firstFailure = CaptureAgentBridgeFailure(
            () => offlineClient.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Require(firstFailure.Code == AgentBridgeErrorCodes.Timeout, "offline timeout code");

        var terminalFailure = CaptureAgentBridgeFailure(
            () => offlineClient.GetCapabilitiesAsync(
                    new AgentCapabilitiesRequest
                    {
                        ClientName = "Codex.AutoCAD.Host.2016",
                        ClientVersion = "1.0.0.0",
                        HostTarget = "autocad-r20.1-net45-x64",
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult());
        Require(terminalFailure.Code == AgentBridgeErrorCodes.Timeout,
            "offline terminal fail-closed code");
    }

    Console.WriteLine("[PASS] " + offlineSpecId);

    currentSpecId = disconnectSpecId;
    var disconnectPipe = "codex-bridge-disconnect-" + Guid.NewGuid().ToString("N");
    var disconnectSession = "disconnect-session-" + Guid.NewGuid().ToString("N");
    disconnectServer = StartTestServer(
        serverExe,
        disconnectPipe,
        disconnectSession,
        secretHex,
        "disconnect");
    using (var disconnectClient = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = disconnectPipe,
        SessionId = disconnectSession,
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(2),
    }))
    {
        disconnectClient.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        Require(disconnectServer.WaitForExit(5000), "disconnect server exit");
        Require(disconnectServer.ExitCode == 0, "disconnect server exit code");
        var disconnectFailure = CaptureAgentBridgeFailure(
            () => disconnectClient.GetCapabilitiesAsync(
                    new AgentCapabilitiesRequest
                    {
                        ClientName = "Codex.AutoCAD.Host.2016",
                        ClientVersion = "1.0.0.0",
                        HostTarget = "autocad-r20.1-net45-x64",
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult());
        Require(disconnectFailure.Code == AgentBridgeErrorCodes.ConnectionLost,
            "disconnect error code");
        var disconnectTerminal = CaptureAgentBridgeFailure(
            () => disconnectClient.StartThreadAsync(
                    new AgentThreadStartRequest { ConversationId = "disconnect-conversation" },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult());
        Require(disconnectTerminal.Code == AgentBridgeErrorCodes.ConnectionLost,
            "disconnect terminal fail-closed code");
    }

    Console.WriteLine("[PASS] " + disconnectSpecId);

    currentSpecId = timeoutSpecId;
    var timeoutPipe = "codex-bridge-timeout-" + Guid.NewGuid().ToString("N");
    var timeoutSession = "timeout-session-" + Guid.NewGuid().ToString("N");
    timeoutServer = StartTestServer(
        serverExe,
        timeoutPipe,
        timeoutSession,
        secretHex,
        "timeout");
    using (var timeoutClient = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = timeoutPipe,
        SessionId = timeoutSession,
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromMilliseconds(250),
    }))
    {
        timeoutClient.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var timeoutFailure = CaptureAgentBridgeFailure(
            () => timeoutClient.GetCapabilitiesAsync(
                    new AgentCapabilitiesRequest
                    {
                        ClientName = "Codex.AutoCAD.Host.2016",
                        ClientVersion = "1.0.0.0",
                        HostTarget = "autocad-r20.1-net45-x64",
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult());
        Require(timeoutFailure.Code == AgentBridgeErrorCodes.Timeout, "request timeout code");
        var timeoutTerminal = CaptureAgentBridgeFailure(
            () => timeoutClient.GetCapabilitiesAsync(
                    new AgentCapabilitiesRequest
                    {
                        ClientName = "Codex.AutoCAD.Host.2016",
                        ClientVersion = "1.0.0.0",
                        HostTarget = "autocad-r20.1-net45-x64",
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult());
        Require(timeoutTerminal.Code == AgentBridgeErrorCodes.Timeout,
            "request timeout terminal fail-closed code");
    }

    Require(timeoutServer.WaitForExit(5000), "timeout server exit");
    Require(timeoutServer.ExitCode == 0, "timeout server exit code");
    Console.WriteLine("[PASS] " + timeoutSpecId);

    currentSpecId = cancellationSpecId;
    var cancellationPipe = "codex-bridge-cancellation-" + Guid.NewGuid().ToString("N");
    var cancellationSession = "cancellation-session-" + Guid.NewGuid().ToString("N");
    cancellationServer = StartTestServer(
        serverExe,
        cancellationPipe,
        cancellationSession,
        secretHex,
        "timeout");
    using (var cancellationClient = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = cancellationPipe,
        SessionId = cancellationSession,
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(5),
    }))
    {
        cancellationClient.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        CaptureCancellation(
            () => cancellationClient.GetCapabilitiesAsync(
                    new AgentCapabilitiesRequest
                    {
                        ClientName = "Codex.AutoCAD.Host.2016",
                        ClientVersion = "1.0.0.0",
                        HostTarget = "autocad-r20.1-net45-x64",
                    },
                    cancellation.Token)
                .GetAwaiter()
                .GetResult());
        var cancellationTerminal = CaptureAgentBridgeFailure(
            () => RequestCapabilities(cancellationClient));
        Require(cancellationTerminal.Code == AgentBridgeErrorCodes.ConnectionLost,
            "request cancellation terminal fail-closed code");
    }

    Require(cancellationServer.WaitForExit(5000), "cancellation server exit");
    Require(cancellationServer.ExitCode == 0, "cancellation server exit code");
    Console.WriteLine("[PASS] " + cancellationSpecId);

    currentSpecId = disposeIdempotentSpecId;
    var disposeClient = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = "codex-bridge-dispose-" + Guid.NewGuid().ToString("N"),
        SessionId = "dispose-session-" + Guid.NewGuid().ToString("N"),
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromMilliseconds(250),
        RequestTimeout = TimeSpan.FromMilliseconds(250),
    });
    disposeClient.Dispose();
    disposeClient.Dispose();
    Console.WriteLine("[PASS] " + disposeIdempotentSpecId);

    currentSpecId = badMacSpecId;
    var badMacPipe = "codex-bridge-badmac-" + Guid.NewGuid().ToString("N");
    var badMacSession = "badmac-session-" + Guid.NewGuid().ToString("N");
    badMacServer = StartTestServer(serverExe, badMacPipe, badMacSession, secretHex, "badmac");
    using (var badMacClient = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = badMacPipe,
        SessionId = badMacSession,
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(2),
    }))
    {
        var faultCount = 0;
        var faultCode = string.Empty;
        using var faultGate = new ManualResetEventSlim(false);
        badMacClient.ConnectionFaulted += (_, eventArgs) =>
        {
            faultCode = eventArgs.Exception.Code;
            Interlocked.Increment(ref faultCount);
            faultGate.Set();
        };
        badMacClient.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var badMacFailure = CaptureAgentBridgeFailure(
            () => RequestCapabilities(badMacClient));
        Require(badMacFailure.Code == AgentBridgeErrorCodes.AuthenticationFailed,
            "bad MAC error code");
        var badMacTerminal = CaptureAgentBridgeFailure(
            () => RequestCapabilities(badMacClient));
        Require(badMacTerminal.Code == AgentBridgeErrorCodes.AuthenticationFailed,
            "bad MAC terminal fail-closed code");
        Require(faultGate.Wait(TimeSpan.FromSeconds(5)), "bad MAC fault event timeout");
        Require(faultCode == AgentBridgeErrorCodes.AuthenticationFailed,
            "bad MAC fault event code");
        Require(Volatile.Read(ref faultCount) == 1, "bad MAC fault event count");
    }

    Require(badMacServer.WaitForExit(5000), "bad MAC server exit");
    Require(badMacServer.ExitCode == 0, "bad MAC server exit code");
    Console.WriteLine("[PASS] " + badMacSpecId);

    currentSpecId = sequenceGapSpecId;
    var sequencePipe = "codex-bridge-sequence-" + Guid.NewGuid().ToString("N");
    var sequenceSession = "sequence-session-" + Guid.NewGuid().ToString("N");
    sequenceGapServer = StartTestServer(
        serverExe,
        sequencePipe,
        sequenceSession,
        secretHex,
        "sequence-gap");
    using (var sequenceClient = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = sequencePipe,
        SessionId = sequenceSession,
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(2),
    }))
    {
        sequenceClient.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var sequenceFailure = CaptureAgentBridgeFailure(
            () => RequestCapabilities(sequenceClient));
        Require(sequenceFailure.Code == AgentBridgeErrorCodes.ReplayRejected,
            "sequence gap error code");
        var sequenceTerminal = CaptureAgentBridgeFailure(
            () => RequestCapabilities(sequenceClient));
        Require(sequenceTerminal.Code == AgentBridgeErrorCodes.ReplayRejected,
            "sequence gap terminal fail-closed code");
    }

    Require(sequenceGapServer.WaitForExit(5000), "sequence gap server exit");
    Require(sequenceGapServer.ExitCode == 0, "sequence gap server exit code");
    Console.WriteLine("[PASS] " + sequenceGapSpecId);

    currentSpecId = nonceReplaySpecId;
    var noncePipe = "codex-bridge-nonce-" + Guid.NewGuid().ToString("N");
    var nonceSession = "nonce-session-" + Guid.NewGuid().ToString("N");
    nonceReplayServer = StartTestServer(
        serverExe,
        noncePipe,
        nonceSession,
        secretHex,
        "nonce-replay");
    using (var nonceClient = new AgentBridgeClient(new AgentBridgeClientOptions
    {
        PipeName = noncePipe,
        SessionId = nonceSession,
        SessionSecret = secret,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(2),
    }))
    {
        nonceClient.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var firstNonceResponse = RequestCapabilities(nonceClient);
        Require(firstNonceResponse.AgentInstanceId == "raw-test-agent-instance",
            "first nonce response");
        var nonceFailure = CaptureAgentBridgeFailure(
            () => RequestCapabilities(nonceClient));
        Require(nonceFailure.Code == AgentBridgeErrorCodes.ReplayRejected,
            "nonce replay error code");
        var nonceTerminal = CaptureAgentBridgeFailure(
            () => RequestCapabilities(nonceClient));
        Require(nonceTerminal.Code == AgentBridgeErrorCodes.ReplayRejected,
            "nonce replay terminal fail-closed code");
    }

    Require(nonceReplayServer.WaitForExit(5000), "nonce replay server exit");
    Require(nonceReplayServer.ExitCode == 0, "nonce replay server exit code");
    Console.WriteLine("[PASS] " + nonceReplaySpecId);

    currentSpecId = unknownFieldSpecId;
    RunProtocolFaultSpec(
        serverExe,
        secret,
        secretHex,
        "unknown-field",
        unknownFieldSpecId);

    currentSpecId = duplicateFieldSpecId;
    RunProtocolFaultSpec(
        serverExe,
        secret,
        secretHex,
        "duplicate-field",
        duplicateFieldSpecId);

    currentSpecId = wrongCaseSpecId;
    RunProtocolFaultSpec(
        serverExe,
        secret,
        secretHex,
        "wrong-case",
        wrongCaseSpecId);

    currentSpecId = trailingJsonSpecId;
    RunProtocolFaultSpec(
        serverExe,
        secret,
        secretHex,
        "trailing-json",
        trailingJsonSpecId);

    currentSpecId = invalidUtf8SpecId;
    RunProtocolFaultSpec(
        serverExe,
        secret,
        secretHex,
        "invalid-utf8",
        invalidUtf8SpecId);

    currentSpecId = oversizedFrameSpecId;
    RunProtocolFaultSpec(
        serverExe,
        secret,
        secretHex,
        "oversized-frame",
        oversizedFrameSpecId);

    Console.WriteLine("33/33 specs passed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "[FAIL] " + currentSpecId + ": " + exception.GetType().Name + ": " + exception.Message);
    return 1;
}
finally
{
    Array.Clear(secret, 0, secret.Length);
    DisposeTestServer(terminalLateServer);
    DisposeTestServer(server);
    DisposeTestServer(disconnectServer);
    DisposeTestServer(timeoutServer);
    DisposeTestServer(cancellationServer);
    DisposeTestServer(badMacServer);
    DisposeTestServer(sequenceGapServer);
    DisposeTestServer(nonceReplayServer);
}

static Process StartTestServer(
    string serverExe,
    string pipeName,
    string sessionId,
    string secretHex,
    string mode)
{
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = serverExe,
        Arguments = pipeName + " " + sessionId + " " + secretHex + " " + mode,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    }) ?? throw new InvalidOperationException("Failed to start the bridge test server.");

    var readyTask = process.StandardOutput.ReadLineAsync();
    if (!readyTask.Wait(TimeSpan.FromSeconds(5))
        || !string.Equals(readyTask.Result, "READY", StringComparison.Ordinal))
    {
        DisposeTestServer(process);
        throw new TimeoutException("Bridge test server did not become ready.");
    }

    return process;
}

static void DisposeTestServer(Process? process)
{
    if (process is null)
    {
        return;
    }

    try
    {
        if (!process.HasExited)
        {
            process.Kill();
            process.WaitForExit(5000);
        }
    }
    catch
    {
    }

    process.Dispose();
}

static void Require(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + label + ".");
    }
}

static AgentBridgeClientException CaptureAgentBridgeFailure(Action action)
{
    try
    {
        action();
    }
    catch (AgentBridgeClientException exception)
    {
        return exception;
    }

    throw new InvalidOperationException("Expected AgentBridgeClientException was not thrown.");
}

static void CaptureCancellation(Action action)
{
    try
    {
        action();
    }
    catch (OperationCanceledException)
    {
        return;
    }

    throw new InvalidOperationException("Expected OperationCanceledException was not thrown.");
}

static AgentCapabilitiesResponse RequestCapabilities(IAgentBridgeClient client)
{
    return client.GetCapabilitiesAsync(
            new AgentCapabilitiesRequest
            {
                ClientName = "Codex.AutoCAD.Host.2016",
                ClientVersion = "1.0.0.0",
                HostTarget = "autocad-r20.1-net45-x64",
            },
            CancellationToken.None)
        .GetAwaiter()
        .GetResult();
}

static void RunProtocolFaultSpec(
    string serverExe,
    byte[] secret,
    string secretHex,
    string mode,
    string specId)
{
    var pipeName = "codex-bridge-protocol-fault-" + Guid.NewGuid().ToString("N");
    var sessionId = "protocol-fault-session-" + Guid.NewGuid().ToString("N");
    Process? process = null;
    try
    {
        process = StartTestServer(serverExe, pipeName, sessionId, secretHex, mode);
        using (var client = new AgentBridgeClient(new AgentBridgeClientOptions
        {
            PipeName = pipeName,
            SessionId = sessionId,
            SessionSecret = secret,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(2),
        }))
        {
            client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            var firstFailure = CaptureAgentBridgeFailure(() => RequestCapabilities(client));
            Require(firstFailure.Code == "request_invalid", specId + " first error code");
            var terminalFailure = CaptureAgentBridgeFailure(() => RequestCapabilities(client));
            Require(terminalFailure.Code == "request_invalid", specId + " terminal error code");
        }

        Require(process.WaitForExit(5000), specId + " server exit");
        Require(process.ExitCode == 0, specId + " server exit code");
        Console.WriteLine("[PASS] " + specId);
    }
    finally
    {
        DisposeTestServer(process);
    }
}

static CadContextJsonV1 CreateCadContext()
{
    return new CadContextJsonV1
    {
        CapturedAtUtc = "2026-07-20T09:00:00.000Z",
        Document = new CadContextDocumentV1
        {
            DocumentId = "doc-test-1",
            DrawingFingerprint = new string('a', 64),
            Revision = 1,
            CurrentSpace = CadContextJsonV1Constants.ModelSpace,
            DrawingVersion = "AC1027",
            Units = "millimeters",
        },
        Selection = new CadContextSelectionV1
        {
            SnapshotHash = new string('b', 64),
            EntityCount = 1,
            Entities = new[]
            {
                new CadContextEntityV1
                {
                    Handle = "10",
                    OwnerSpaceHandle = "1F",
                    EntityType = CadContextEntityTypes.Line,
                    StateHash = new string('c', 64),
                    Layer = "结构层",
                    Line = new CadContextLineV1
                    {
                        Start = new CadPoint3(0, 0, 0),
                        End = new CadPoint3(100.25, 20.5, 0),
                    },
                },
            },
        },
    };
}

static string BuildCapabilitiesResponseJson(string? supportedCadContextSchemasJson)
{
    var json = "{\"contractVersion\":1,"
        + "\"minimumCompatibleVersion\":1,"
        + "\"agentInstanceId\":\"codec-test-agent\","
        + "\"cadContextSchema\":\"codex.autocad.cad-context\","
        + "\"cadContextSchemaVersion\":1,"
        + (supportedCadContextSchemasJson is not null
            ? "\"supportedCadContextSchemas\":" + supportedCadContextSchemasJson + ","
            : string.Empty)
        + "\"methods\":[\"agent.capabilities.get\",\"agent.thread.start\",\"agent.turn.start\",\"agent.turn.interrupt\"],"
        + "\"eventKinds\":[\"thread.started\",\"turn.started\",\"message.assistant.delta\",\"message.assistant.completed\",\"turn.completed\",\"turn.failed\"],"
        + "\"approvalDecisions\":[],"
        + "\"cadWriteAvailable\":false}";
    return json;
}

static string BuildCapabilitiesResponseJsonWithRawField(string rawField)
{
    return "{\"contractVersion\":1,"
        + "\"minimumCompatibleVersion\":1,"
        + "\"agentInstanceId\":\"codec-test-agent\","
        + "\"cadContextSchema\":\"codex.autocad.cad-context\","
        + "\"cadContextSchemaVersion\":1,"
        + rawField + ","
        + "\"methods\":[\"agent.capabilities.get\",\"agent.thread.start\",\"agent.turn.start\",\"agent.turn.interrupt\"],"
        + "\"eventKinds\":[\"thread.started\",\"turn.started\",\"message.assistant.delta\",\"message.assistant.completed\",\"turn.completed\",\"turn.failed\"],"
        + "\"approvalDecisions\":[],"
        + "\"cadWriteAvailable\":false}";
}
