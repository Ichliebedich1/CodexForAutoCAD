using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Codex.AutoCAD.AgentHost;
using Codex.AutoCAD.AgentRuntime;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AppServer.Protocol;
using Codex.AutoCAD.Bridge.Client;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

internal static class AgentHostBridgeSessionSpecs
{
    public static async Task TwoContextTurnsReuseThreadAndMapAssistantEvents()
    {
        var keyPair = CreateBootstrapDirectionKeyPair();
        try
        {
            await using var appServer = new ScriptedAgentAppServer();
            appServer.QueueResponse("thread/start", """
                {"thread":{"id":"thread-live-1"}}
                """);
            appServer.QueueResponse("turn/start", """
                {"turn":{"id":"turn-live-1","status":"inProgress","items":[]}}
                """, () =>
                {
                    appServer.EmitNotification("item/started", """
                        {
                          "threadId":"thread-live-1","turnId":"turn-live-1","startedAtMs":10,
                          "item":{"id":"message-live-1","type":"agentMessage","text":""}
                        }
                        """);
                    appServer.EmitNotification("item/agentMessage/delta", """
                        {"threadId":"thread-live-1","turnId":"turn-live-1","itemId":"message-live-1","delta":"第一轮"}
                        """);
                    appServer.EmitNotification("item/completed", """
                        {
                          "threadId":"thread-live-1","turnId":"turn-live-1","completedAtMs":20,
                          "item":{"id":"message-live-1","type":"agentMessage","text":"第一轮完成"}
                        }
                        """);
                    appServer.EmitNotification("turn/completed", """
                        {"threadId":"thread-live-1","turn":{"id":"turn-live-1","status":"completed","items":[]}}
                        """);
                });
            appServer.QueueResponse("turn/start", """
                {"turn":{"id":"turn-live-2","status":"inProgress","items":[]}}
                """);

            await using var runtime = new CodexAgentRuntime(
                appServer,
                new AgentRuntimeOptions
                {
                    Sandbox = AgentSandboxMode.ReadOnly,
                    ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                    ApprovalsReviewer = AgentApprovalsReviewer.User,
                    MaximumPromptCharacters = 320 * 1024,
                });
            var service = new AgentHostBridgeSession(runtime, "agenthost-two-turn-spec");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serviceTask = service.RunAsync(keyPair.AgentKeys, timeout.Token);
            using var client = new AgentBridgeClient(
                keyPair.HostKeys,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));
            var events = Channel.CreateUnbounded<AgentBridgeEvent>();
            client.EventReceived += (_, args) => events.Writer.TryWrite(args.BridgeEvent);
            await client.StartAsync(timeout.Token);

            var capabilities = await client.GetCapabilitiesAsync(
                new AgentCapabilitiesRequest
                {
                    ClientName = "Codex.AutoCAD.Host.2016",
                    ClientVersion = "0.2.0.0",
                    HostTarget = "autocad-r20.1-net45-x64",
                },
                timeout.Token);
            Contains(capabilities.Methods, AgentBridgeMethods.StartThread);
            Contains(capabilities.Methods, AgentBridgeMethods.StartTurn);
            Contains(capabilities.EventKinds, AgentBridgeEventKinds.AssistantMessageDelta);
            Contains(capabilities.EventKinds, AgentBridgeEventKinds.AssistantMessageCompleted);

            var thread = await client.StartThreadAsync(
                new AgentThreadStartRequest { ConversationId = "conversation-live-1" },
                timeout.Token);
            Equal("thread-live-1", thread.ThreadId);
            var threadEvent = await ReadKindAsync(
                events.Reader,
                AgentBridgeEventKinds.ThreadStarted,
                timeout.Token);
            Equal(thread.ThreadId, threadEvent.ThreadId);

            var firstContext = CreateContext("doc-live-1", revision: 1, lineEndX: 10d);
            var firstHash = CadContextJsonV1Codec.ComputeCanonicalSha256(firstContext);
            var firstTurn = await client.StartTurnAsync(
                new AgentTurnStartRequest
                {
                    ThreadId = thread.ThreadId,
                    ClientTurnId = "client-turn-live-1",
                    Prompt = "分析所选直线。",
                    Context = firstContext,
                    ContextSha256 = firstHash,
                },
                timeout.Token);
            Equal(thread.ThreadId, firstTurn.ThreadId);
            Equal("turn-live-1", firstTurn.TurnId);
            Equal(firstHash, firstTurn.AcceptedContextSha256);

            await AssertAssistantTurnAsync(
                events.Reader,
                firstTurn,
                firstHash,
                "第一轮",
                "第一轮完成",
                timeout.Token);

            var secondContext = CreateContext("doc-live-1", revision: 2, lineEndX: 20d);
            var secondHash = CadContextJsonV1Codec.ComputeCanonicalSha256(secondContext);
            var secondTurn = await client.StartTurnAsync(
                new AgentTurnStartRequest
                {
                    ThreadId = thread.ThreadId,
                    ClientTurnId = "client-turn-live-2",
                    Prompt = "和上一轮相比有什么变化？",
                    Context = secondContext,
                    ContextSha256 = secondHash,
                },
                timeout.Token);
            Equal(thread.ThreadId, secondTurn.ThreadId);
            Equal("turn-live-2", secondTurn.TurnId);
            Equal(secondHash, secondTurn.AcceptedContextSha256);

            appServer.EmitNotification("item/started", """
                {
                  "threadId":"thread-live-1","turnId":"turn-live-2","startedAtMs":30,
                  "item":{"id":"message-live-2","type":"agentMessage","text":""}
                }
                """);
            appServer.EmitNotification("item/agentMessage/delta", """
                {"threadId":"thread-live-1","turnId":"turn-live-2","itemId":"message-live-2","delta":"第二轮"}
                """);
            appServer.EmitNotification("item/completed", """
                {
                  "threadId":"thread-live-1","turnId":"turn-live-2","completedAtMs":40,
                  "item":{"id":"message-live-2","type":"agentMessage","text":"第二轮完成"}
                }
                """);
            appServer.EmitNotification("turn/completed", """
                {"threadId":"thread-live-1","turn":{"id":"turn-live-2","status":"completed","items":[]}}
                """);

            await AssertAssistantTurnAsync(
                events.Reader,
                secondTurn,
                secondHash,
                "第二轮",
                "第二轮完成",
                timeout.Token);

            Equal(3, appServer.Requests.Count);
            Equal("thread/start", appServer.Requests[0].Method);
            Equal("turn/start", appServer.Requests[1].Method);
            Equal("turn/start", appServer.Requests[2].Method);
            Equal(0, appServer.Requests[0].Params.GetProperty("dynamicTools").GetArrayLength());
            Contains(
                appServer.Requests[0].Params.GetProperty("developerInstructions").GetString()
                    ?? string.Empty,
                "untrusted data");
            AssertUntrustedContextInput(appServer.Requests[1], firstContext, firstHash);
            AssertUntrustedContextInput(appServer.Requests[2], secondContext, secondHash);
            Equal(
                "thread-live-1",
                appServer.Requests[1].Params.GetProperty("threadId").GetString());
            Equal(
                "thread-live-1",
                appServer.Requests[2].Params.GetProperty("threadId").GetString());

            await client.StopAsync(CancellationToken.None);
            await serviceTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            keyPair.HostKeys.Dispose();
            keyPair.AgentKeys.Dispose();
        }
    }

    private static async Task AssertAssistantTurnAsync(
        ChannelReader<AgentBridgeEvent> reader,
        AgentTurnStartResponse turn,
        string contextSha256,
        string expectedDelta,
        string expectedCompleted,
        CancellationToken cancellationToken)
    {
        var started = await ReadKindAsync(
            reader,
            AgentBridgeEventKinds.TurnStarted,
            cancellationToken);
        AssertIdentity(started, turn, contextSha256);

        var assistantStarted = await ReadKindAsync(
            reader,
            AgentBridgeEventKinds.AssistantMessageStarted,
            cancellationToken);
        AssertIdentity(assistantStarted, turn, contextSha256);

        var delta = await ReadKindAsync(
            reader,
            AgentBridgeEventKinds.AssistantMessageDelta,
            cancellationToken);
        AssertIdentity(delta, turn, contextSha256);
        Equal(expectedDelta, delta.Delta);

        var completed = await ReadKindAsync(
            reader,
            AgentBridgeEventKinds.AssistantMessageCompleted,
            cancellationToken);
        AssertIdentity(completed, turn, contextSha256);
        Equal(expectedCompleted, completed.Content);

        var terminal = await ReadKindAsync(
            reader,
            AgentBridgeEventKinds.TurnCompleted,
            cancellationToken);
        AssertIdentity(terminal, turn, contextSha256);
    }

    private static void AssertIdentity(
        AgentBridgeEvent bridgeEvent,
        AgentTurnStartResponse turn,
        string contextSha256)
    {
        Equal(turn.ThreadId, bridgeEvent.ThreadId);
        Equal(turn.TurnId, bridgeEvent.TurnId);
        Equal(contextSha256, bridgeEvent.ContextSha256);
    }

    private static void AssertUntrustedContextInput(
        SentAppServerRequest request,
        CadContextJsonV1 context,
        string contextSha256)
    {
        var input = request.Params.GetProperty("input");
        Equal(2, input.GetArrayLength());
        Equal("text", input[0].GetProperty("type").GetString());
        Equal("text", input[1].GetProperty("type").GetString());
        var contextInput = input[1].GetProperty("text").GetString() ?? string.Empty;
        Contains(contextInput, "UNTRUSTED CAD CONTEXT");
        Contains(contextInput, contextSha256);
        Contains(contextInput, CadContextJsonV1Codec.SerializeCanonical(context));
    }

    private static async Task<AgentBridgeEvent> ReadKindAsync(
        ChannelReader<AgentBridgeEvent> reader,
        string kind,
        CancellationToken cancellationToken)
    {
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var bridgeEvent))
            {
                if (string.Equals(bridgeEvent.Kind, kind, StringComparison.Ordinal))
                {
                    return bridgeEvent;
                }
            }
        }

        throw new EndOfStreamException("Agent event channel ended before " + kind + ".");
    }

    private static CadContextJsonV1 CreateContext(
        string documentId,
        long revision,
        double lineEndX)
    {
        return new CadContextJsonV1
        {
            CapturedAtUtc = "2026-07-20T00:00:00.000Z",
            Document = new CadContextDocumentV1
            {
                DocumentId = documentId,
                DrawingFingerprint = new string('a', 64),
                Revision = revision,
                CurrentSpace = CadContextJsonV1Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "millimeters",
            },
            Selection = new CadContextSelectionV1
            {
                SnapshotHash = new string('b', 64),
                EntityCount = 1,
                Entities =
                [
                    new CadContextEntityV1
                    {
                        Handle = "1A",
                        OwnerSpaceHandle = "1",
                        EntityType = CadContextEntityTypes.Line,
                        StateHash = new string('c', 64),
                        Layer = "SPEC",
                        Line = new CadContextLineV1
                        {
                            Start = new CadPoint3(0d, 0d, 0d),
                            End = new CadPoint3(lineEndX, 0d, 0d),
                        },
                    },
                ],
            },
        };
    }

    private static (AgentBootstrapDirectionKeys HostKeys, AgentBootstrapDirectionKeys AgentKeys)
        CreateBootstrapDirectionKeyPair()
    {
        var sessionId = CreateLowerHexIdentifier();
        var pipeName = "codex-autocad-" + CreateLowerHexIdentifier();
        using var outboundPayload = AgentBootstrapPayload.CreateRandom(sessionId, pipeName);
        using var encoded = new MemoryStream();
        var writeKey = AgentBootstrapProtocol.CreateAuthenticationKey();
        var readKey = (byte[])writeKey.Clone();
        try
        {
            AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
                encoded,
                outboundPayload,
                writeKey);
            encoded.Position = 0;
            using var inboundPayload = AgentBootstrapProtocol.ReadSingleFrameAndClearKey(
                encoded,
                readKey);
            return (
                HostKeys: outboundPayload.DeriveDirectionKeys(),
                AgentKeys: inboundPayload.DeriveDirectionKeys());
        }
        finally
        {
            Array.Clear(writeKey, 0, writeKey.Length);
            Array.Clear(readKey, 0, readKey.Length);
        }
    }

    private static string CreateLowerHexIdentifier()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static void Contains(IEnumerable<string> values, string expected)
    {
        if (!values.Contains(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Expected collection to contain: " + expected);
        }
    }

    private static void Contains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected text to contain: " + expected);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', actual '{actual}'.");
        }
    }
}

internal sealed record SentAppServerRequest(string Method, JsonElement Params);

internal sealed class ScriptedAgentAppServer : IAgentAppServer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentQueue<(string Method, string Json, Action? BeforeReturn)> _responses = new();
    private readonly List<SentAppServerRequest> _requests = new();
    private readonly object _sync = new();

    public event EventHandler<AppServerNotification>? NotificationReceived;
    public event CommandApprovalRequestedHandler? CommandApprovalRequested
    {
        add { }
        remove { }
    }

    public event FileChangeApprovalRequestedHandler? FileChangeApprovalRequested
    {
        add { }
        remove { }
    }

    public event PermissionsApprovalRequestedHandler? PermissionsApprovalRequested
    {
        add { }
        remove { }
    }

    public event CadApprovalRequestedHandler? CadApprovalRequested
    {
        add { }
        remove { }
    }

    public event ServerRequestReceivedHandler? ServerRequestReceived
    {
        add { }
        remove { }
    }

    public IReadOnlyList<SentAppServerRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    public void QueueResponse(string method, string json, Action? beforeReturn = null)
        => _responses.Enqueue((method, json, beforeReturn));

    public Task StartAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<TResult> SendRequestAsync<TResult>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parametersJson = JsonSerializer.Serialize(parameters, SerializerOptions);
        using var document = JsonDocument.Parse(parametersJson);
        lock (_sync)
        {
            _requests.Add(new SentAppServerRequest(method, document.RootElement.Clone()));
        }

        if (!_responses.TryDequeue(out var response)
            || !string.Equals(response.Method, method, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected App Server request: " + method);
        }
        response.BeforeReturn?.Invoke();

        var value = JsonSerializer.Deserialize<TResult>(response.Json, SerializerOptions)
            ?? throw new InvalidDataException("Scripted App Server response was null.");
        return Task.FromResult(value);
    }

    public void EmitNotification(string method, string paramsJson)
    {
        using var document = JsonDocument.Parse(paramsJson);
        NotificationReceived?.Invoke(
            this,
            new AppServerNotification(method, document.RootElement.Clone()));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
