using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Codex.AutoCAD.Bridge;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

if (args.Length < 3 || args.Length > 4)
{
    Console.Error.WriteLine("usage: <pipeName> <sessionId> <syntheticSecretHex> [mode]");
    return 2;
}

var pipeName = args[0];
var sessionId = args[1];
var mode = args.Length == 4 ? args[3] : "happy";
if (mode != "happy"
    && mode != "disconnect"
    && mode != "timeout"
    && mode != "badmac"
    && mode != "sequence-gap"
    && mode != "nonce-replay"
    && mode != "unknown-field"
    && mode != "duplicate-field"
    && mode != "wrong-case"
    && mode != "trailing-json"
    && mode != "invalid-utf8"
    && mode != "oversized-frame"
    && mode != "terminal-late-event"
    && mode != "v2-happy")
{
    Console.Error.WriteLine("invalid test mode");
    return 2;
}
byte[] secret;
try
{
    secret = Convert.FromHexString(args[2]);
}
catch (FormatException)
{
    Console.Error.WriteLine("invalid synthetic secret");
    return 2;
}

if (secret.Length != 32)
{
    Console.Error.WriteLine("invalid synthetic secret length");
    return 2;
}

var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    MaxDepth = 32,
};

try
{
    if (mode != "happy" && mode != "disconnect" && mode != "timeout"
        && mode != "terminal-late-event" && mode != "v2-happy")
    {
        return await RunRawFaultServerAsync(
            pipeName,
            sessionId,
            secret,
            mode,
            serializerOptions).ConfigureAwait(false);
    }

    var acceptTask = NamedPipeBridge.AcceptOneAsync(pipeName, sessionId, secret);
    Console.Out.WriteLine("READY");
    Console.Out.Flush();

    await using var connection = await acceptTask.ConfigureAwait(false);
    if (mode == "disconnect")
    {
        return 0;
    }

    var emitAssistantEvents = false;
    var activeThreadId = string.Empty;
    var activeTurnId = string.Empty;
    var activeContextSha256 = string.Empty;
    var emitTerminalLateEvents = false;
    connection.Start(async (request, cancellationToken) =>
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (mode == "timeout"
            && string.Equals(request.Method, AgentBridgeMethods.GetCapabilities, StringComparison.Ordinal))
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return "null";
        }

        if (string.Equals(request.Method, AgentBridgeMethods.GetCapabilities, StringComparison.Ordinal))
        {
            var capabilitiesRequest = JsonSerializer.Deserialize<AgentCapabilitiesRequest>(
                request.BodyJson,
                serializerOptions);
            if (AgentBridgeContractValidator.Validate(capabilitiesRequest).Length != 0)
            {
                throw new InvalidOperationException("invalid capabilities request");
            }

            if (emitAssistantEvents)
            {
                emitAssistantEvents = false;
                var deltaEvent = new AgentBridgeEvent
                {
                    Kind = AgentBridgeEventKinds.AssistantMessageDelta,
                    EventId = "event-assistant-delta-1",
                    Sequence = 1,
                    ThreadId = activeThreadId,
                    TurnId = activeTurnId,
                    ItemId = "item-assistant-1",
                    MessageId = "message-assistant-1",
                    Delta = "正在分析选中的直线。",
                    ContextSha256 = activeContextSha256,
                    OccurredAtUtc = "2026-07-20T09:00:01.000Z",
                };
                var completedEvent = new AgentBridgeEvent
                {
                    Kind = AgentBridgeEventKinds.AssistantMessageCompleted,
                    EventId = "event-assistant-completed-1",
                    Sequence = 2,
                    ThreadId = activeThreadId,
                    TurnId = activeTurnId,
                    ItemId = "item-assistant-1",
                    MessageId = "message-assistant-1",
                    Content = "该直线从原点附近延伸至正X、正Y方向。",
                    ContextSha256 = activeContextSha256,
                    OccurredAtUtc = "2026-07-20T09:00:02.000Z",
                };
                await connection.NotifyAsync(
                    AgentBridgeMethods.EventNotification,
                    JsonSerializer.Serialize(deltaEvent, serializerOptions),
                    cancellationToken);
                await connection.NotifyAsync(
                    AgentBridgeMethods.EventNotification,
                    JsonSerializer.Serialize(completedEvent, serializerOptions),
                    cancellationToken);
            }

            if (emitTerminalLateEvents)
            {
                emitTerminalLateEvents = false;
                var terminalEvent = new AgentBridgeEvent
                {
                    Kind = AgentBridgeEventKinds.TurnCompleted,
                    EventId = "event-turn-completed-1",
                    Sequence = 1,
                    ThreadId = activeThreadId,
                    TurnId = activeTurnId,
                    ContextSha256 = activeContextSha256,
                    OccurredAtUtc = "2026-07-20T09:00:01.000Z",
                };
                var lateEvent = new AgentBridgeEvent
                {
                    Kind = AgentBridgeEventKinds.AssistantMessageDelta,
                    EventId = "event-late-assistant-delta-1",
                    Sequence = 2,
                    ThreadId = activeThreadId,
                    TurnId = activeTurnId,
                    ItemId = "item-late-assistant-1",
                    MessageId = "message-late-assistant-1",
                    Delta = "该事件位于turn终态之后，必须被拒绝。",
                    ContextSha256 = activeContextSha256,
                    OccurredAtUtc = "2026-07-20T09:00:02.000Z",
                };
                await connection.NotifyAsync(
                    AgentBridgeMethods.EventNotification,
                    JsonSerializer.Serialize(terminalEvent, serializerOptions),
                    cancellationToken);
                await connection.NotifyAsync(
                    AgentBridgeMethods.EventNotification,
                    JsonSerializer.Serialize(lateEvent, serializerOptions),
                    cancellationToken);
            }

            var response = new AgentCapabilitiesResponse
            {
                AgentInstanceId = "test-agent-instance",
                Methods = mode == "v2-happy"
                    ? new[]
                    {
                        AgentBridgeMethods.GetCapabilities,
                        AgentBridgeMethods.StartThread,
                        AgentBridgeMethods.StartTurn,
                        AgentBridgeMethods.StartTurnV2,
                        AgentBridgeMethods.InterruptTurn,
                        AgentBridgeMethods.ResolveApproval,
                        AgentBridgeMethods.EventNotification,
                    }
                    : new[]
                    {
                        AgentBridgeMethods.GetCapabilities,
                        AgentBridgeMethods.StartThread,
                        AgentBridgeMethods.StartTurn,
                        AgentBridgeMethods.InterruptTurn,
                        AgentBridgeMethods.ResolveApproval,
                        AgentBridgeMethods.EventNotification,
                    },
                EventKinds = new[]
                {
                    AgentBridgeEventKinds.ConnectionStateChanged,
                    AgentBridgeEventKinds.ThreadStarted,
                    AgentBridgeEventKinds.TurnStarted,
                    AgentBridgeEventKinds.AssistantMessageDelta,
                    AgentBridgeEventKinds.AssistantMessageCompleted,
                    AgentBridgeEventKinds.TurnCompleted,
                    AgentBridgeEventKinds.TurnFailed,
                    AgentBridgeEventKinds.TurnCancelled,
                },
                ApprovalDecisions = new[]
                {
                    AgentBridgeApprovalDecisions.AllowOnce,
                    AgentBridgeApprovalDecisions.DeclineAndContinue,
                    AgentBridgeApprovalDecisions.DeclineAndCancelTurn,
                },
                SupportedCadContextSchemas = mode == "v2-happy"
                    ? new[]
                    {
                        new CadContextSchemaVersionEntry
                        {
                            Schema = CadContextJsonV1Constants.Schema,
                            SchemaVersion = CadContextJsonV1Constants.SchemaVersion,
                        },
                        new CadContextSchemaVersionEntry
                        {
                            Schema = CadContextJsonV2Constants.Schema,
                            SchemaVersion = CadContextJsonV2Constants.SchemaVersion,
                        },
                    }
                    : new[]
                    {
                        new CadContextSchemaVersionEntry
                        {
                            Schema = CadContextJsonV1Constants.Schema,
                            SchemaVersion = CadContextJsonV1Constants.SchemaVersion,
                        },
                    },
                CadWriteAvailable = false,
            };

            return JsonSerializer.Serialize(response, serializerOptions);
        }

        if (string.Equals(request.Method, AgentBridgeMethods.StartThread, StringComparison.Ordinal))
        {
            var threadRequest = JsonSerializer.Deserialize<AgentThreadStartRequest>(
                request.BodyJson,
                serializerOptions);
            if (AgentBridgeContractValidator.Validate(threadRequest).Length != 0)
            {
                throw new InvalidOperationException("invalid thread start request");
            }

            return JsonSerializer.Serialize(
                new AgentThreadStartResponse
                {
                    ThreadId = "thread-test-1",
                },
                serializerOptions);
        }

        if (string.Equals(request.Method, AgentBridgeMethods.StartTurn, StringComparison.Ordinal))
        {
            var turnRequest = JsonSerializer.Deserialize<AgentTurnStartRequest>(
                request.BodyJson,
                serializerOptions);
            if (AgentBridgeContractValidator.Validate(turnRequest).Length != 0)
            {
                throw new InvalidOperationException("invalid turn start request");
            }

            activeThreadId = turnRequest!.ThreadId;
            activeTurnId = "turn-test-1";
            activeContextSha256 = turnRequest.ContextSha256;
            emitAssistantEvents = mode == "happy" || mode == "v2-happy";
            emitTerminalLateEvents = mode == "terminal-late-event";

            return JsonSerializer.Serialize(
                new AgentTurnStartResponse
                {
                    ThreadId = activeThreadId,
                    TurnId = activeTurnId,
                    AcceptedContextSha256 = turnRequest.ContextSha256,
                },
                serializerOptions);
        }

        if (string.Equals(request.Method, AgentBridgeMethods.StartTurnV2, StringComparison.Ordinal))
        {
            var turnV2Request = JsonSerializer.Deserialize<AgentTurnStartV2Request>(
                request.BodyJson,
                serializerOptions);
            if (AgentBridgeContractValidator.Validate(turnV2Request).Length != 0)
            {
                throw new InvalidOperationException("invalid turn v2 start request");
            }

            activeThreadId = turnV2Request!.ThreadId;
            activeTurnId = "turn-v2-test-1";
            activeContextSha256 = turnV2Request.ContextV2Sha256;
            emitAssistantEvents = mode == "v2-happy";

            return JsonSerializer.Serialize(
                new AgentTurnStartV2Response
                {
                    ThreadId = activeThreadId,
                    TurnId = activeTurnId,
                    AcceptedContextV2Sha256 = turnV2Request.ContextV2Sha256,
                },
                serializerOptions);
        }

        if (string.Equals(request.Method, AgentBridgeMethods.InterruptTurn, StringComparison.Ordinal))
        {
            var interruptRequest = JsonSerializer.Deserialize<AgentTurnInterruptRequest>(
                request.BodyJson,
                serializerOptions);
            if (AgentBridgeContractValidator.Validate(interruptRequest).Length != 0
                || !string.Equals(interruptRequest!.ThreadId, activeThreadId, StringComparison.Ordinal)
                || !string.Equals(interruptRequest.TurnId, activeTurnId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("invalid turn interrupt request");
            }

            return "null";
        }

        if (string.Equals(request.Method, AgentBridgeMethods.ResolveApproval, StringComparison.Ordinal))
        {
            var approvalRequest = JsonSerializer.Deserialize<AgentApprovalResolveRequest>(
                request.BodyJson,
                serializerOptions);
            if (AgentBridgeContractValidator.Validate(approvalRequest).Length != 0
                || !string.Equals(approvalRequest!.ThreadId, activeThreadId, StringComparison.Ordinal)
                || !string.Equals(approvalRequest.TurnId, activeTurnId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("invalid approval resolve request");
            }

            return "null";
        }

        throw new InvalidOperationException("unsupported test method");
    });

    await connection.Completion.ConfigureAwait(false);
    return 0;
}
finally
{
    Array.Clear(secret, 0, secret.Length);
}

static async Task<int> RunRawFaultServerAsync(
    string pipeName,
    string sessionId,
    byte[] secret,
    string mode,
    JsonSerializerOptions serializerOptions)
{
    await using var pipe = new NamedPipeServerStream(
        pipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    Console.Out.WriteLine("READY");
    Console.Out.Flush();
    await pipe.WaitForConnectionAsync().ConfigureAwait(false);

    using var incomingGuard = new IpcSessionGuard(sessionId, secret);
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    var responseCount = mode == "nonce-replay" ? 2 : 1;
    const string replayNonce = "00112233445566778899AABBCCDDEEFF";

    for (var index = 0; index < responseCount; index++)
    {
        var request = await LengthPrefixedFrameCodec.ReadAsync(pipe).ConfigureAwait(false);
        if (request is null
            || incomingGuard.ValidateAndAccept(request) != IpcValidationCode.Accepted
            || !string.Equals(request.MessageType, "bridge.request", StringComparison.Ordinal))
        {
            return 3;
        }

        var bodyJson = JsonSerializer.Serialize(
            new AgentCapabilitiesResponse
            {
                AgentInstanceId = "raw-test-agent-instance",
                Methods = new[] { AgentBridgeMethods.GetCapabilities },
                EventKinds = new[] { AgentBridgeEventKinds.ConnectionStateChanged },
                ApprovalDecisions = Array.Empty<string>(),
                CadWriteAvailable = false,
            },
            serializerOptions);
        var responsePayload = JsonSerializer.Serialize(
            new
            {
                bodyJson,
                errorCode = string.Empty,
                errorMessage = string.Empty,
            },
            serializerOptions);

        var responseSequence = mode == "sequence-gap" ? 2 : index + 1;
        var envelope = new IpcEnvelope
        {
            MessageId = "raw-response-" + (index + 1),
            CorrelationId = request.MessageId,
            SessionId = sessionId,
            Sequence = responseSequence,
            MessageType = "bridge.response",
            PayloadJson = responsePayload,
            Nonce = mode == "nonce-replay"
                ? replayNonce
                : "102132435465768798A9BACBDCEDFE0" + index,
        };
        envelope.Mac = authenticator.Sign(envelope);
        if (mode == "badmac")
        {
            envelope.Mac = (envelope.Mac[0] == 'A' ? "B" : "A") + envelope.Mac.Substring(1);
        }

        if (mode == "oversized-frame")
        {
            await WriteLengthPrefixAsync(
                    pipe,
                    checked(ProtocolConstants.MaximumMessageBytes + 1))
                .ConfigureAwait(false);
            continue;
        }

        if (mode == "unknown-field"
            || mode == "duplicate-field"
            || mode == "wrong-case"
            || mode == "trailing-json"
            || mode == "invalid-utf8")
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, serializerOptions);
            await WriteRawFrameAsync(pipe, MutateEnvelopePayload(payload, mode))
                .ConfigureAwait(false);
            continue;
        }

        await LengthPrefixedFrameCodec.WriteAsync(pipe, envelope).ConfigureAwait(false);
    }

    return 0;
}

static byte[] MutateEnvelopePayload(byte[] payload, string mode)
{
    if (mode == "invalid-utf8")
    {
        var invalid = new byte[payload.Length + 1];
        Buffer.BlockCopy(payload, 0, invalid, 0, payload.Length);
        invalid[invalid.Length - 1] = 0xFF;
        return invalid;
    }

    var json = Encoding.UTF8.GetString(payload);
    switch (mode)
    {
        case "unknown-field":
            json = json.Insert(json.Length - 1, ",\"unexpected\":true");
            break;
        case "duplicate-field":
            json = json.Insert(1, "\"messageId\":\"duplicate\",");
            break;
        case "wrong-case":
            json = json.Replace("\"messageId\"", "\"MessageId\"", StringComparison.Ordinal);
            break;
        case "trailing-json":
            json += "{}";
            break;
        default:
            throw new InvalidOperationException("unsupported malformed JSON mode");
    }

    return Encoding.UTF8.GetBytes(json);
}

static async Task WriteRawFrameAsync(Stream stream, byte[] payload)
{
    await WriteLengthPrefixAsync(stream, payload.Length).ConfigureAwait(false);
    await stream.WriteAsync(payload).ConfigureAwait(false);
    await stream.FlushAsync().ConfigureAwait(false);
}

static async Task WriteLengthPrefixAsync(Stream stream, int length)
{
    var prefix = new byte[4];
    prefix[0] = (byte)length;
    prefix[1] = (byte)(length >> 8);
    prefix[2] = (byte)(length >> 16);
    prefix[3] = (byte)(length >> 24);
    await stream.WriteAsync(prefix).ConfigureAwait(false);
    await stream.FlushAsync().ConfigureAwait(false);
}
