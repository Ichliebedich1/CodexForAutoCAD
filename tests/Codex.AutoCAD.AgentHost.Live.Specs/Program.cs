using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.Bridge.Client;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016;

var agentHostPath = ParseAgentHostPath(args);
var options = new AgentHostBootstrapOptions(
    agentHostPath,
    ComputeFileSha256(agentHostPath))
{
    StartupTimeout = TimeSpan.FromSeconds(15),
    MaximumStandardErrorBytes = 16 * 1024,
};

AgentHostServiceSession? session = null;
AgentBridgeClient? client = null;
var passed = 0;
var currentSpec = "REAL_AGENTHOST_CAPABILITY_HANDSHAKE";
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    Stage("bootstrap.start");
    session = await AgentHostBootstrapService.StartAsync(options, timeout.Token);
    Stage("bootstrap.completed");
    var processId = session.ProcessId;
    ProcessMustBeAlive(processId);
    using (var keys = session.ClaimDirectionKeys())
    {
        client = new AgentBridgeClient(
            keys,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(45));
    }

    var events = Channel.CreateUnbounded<AgentBridgeEvent>();
    client.EventReceived += (_, eventArgs) =>
        events.Writer.TryWrite(eventArgs.BridgeEvent);
    Stage("bridge.connect.start");
    await client.StartAsync(timeout.Token);
    Stage("bridge.connect.completed");
    Stage("capabilities.start");
    var capabilities = await client.GetCapabilitiesAsync(
        MvpAgentProtocolIdentity.CreateCapabilitiesRequest(),
        timeout.Token);
    Stage("capabilities.completed");

    Equal(AgentBridgeContractConstants.CurrentVersion, capabilities.ContractVersion);
    Equal(
        AgentBridgeContractConstants.MinimumCompatibleVersion,
        capabilities.MinimumCompatibleVersion);
    Equal(CadContextJsonV1Constants.Schema, capabilities.CadContextSchema);
    Equal(CadContextJsonV1Constants.SchemaVersion, capabilities.CadContextSchemaVersion);
    True(
        capabilities.Methods.Contains(AgentBridgeMethods.GetCapabilities, StringComparer.Ordinal),
        "Capability response omitted get-capabilities.");
    True(
        capabilities.Methods.Contains(AgentBridgeMethods.StartThread, StringComparer.Ordinal),
        "Capability response omitted thread start.");
    True(
        capabilities.Methods.Contains(AgentBridgeMethods.StartTurn, StringComparer.Ordinal),
        "Capability response omitted turn start.");
    True(
        capabilities.Methods.Contains(AgentBridgeMethods.StartTurnV2, StringComparer.Ordinal),
        "Capability response omitted v2 turn start.");
    True(
        capabilities.SupportedCadContextSchemas.Any(schema =>
            string.Equals(
                schema.Schema,
                CadContextJsonV2Constants.Schema,
                StringComparison.Ordinal)
            && schema.SchemaVersion == CadContextJsonV2Constants.SchemaVersion),
        "Capability response omitted CadContextJson v2.");
    True(!capabilities.CadWriteAvailable, "Read-only AgentHost advertised CAD write access.");
    Equal(0, capabilities.ApprovalDecisions.Length);
    passed++;
    Console.WriteLine(
        "PASS REAL_AGENTHOST_CAPABILITY_HANDSHAKE "
        + "真实bootstrap-serve完成认证能力协商");

    currentSpec = "REAL_CODEX_V2_TWO_CONTEXT_TURNS";
    Stage("thread.start");
    var thread = await client.StartThreadAsync(
        new AgentThreadStartRequest
        {
            ConversationId = "conversation-live-spec",
        },
        timeout.Token);
    Stage("thread.completed");
    var threadStarted = await ReadKindAsync(
        events.Reader,
        AgentBridgeEventKinds.ThreadStarted,
        timeout.Token);
    Equal(thread.ThreadId, threadStarted.ThreadId);

    var firstContext = CreateContextV2(revision: 1, lineEndX: 10d);
    var firstHash = CadContextJsonV2Codec.ComputeCanonicalSha256(firstContext);
    Stage("turn1.start");
    var firstTurn = await client.StartTurnV2Async(
        new AgentTurnStartV2Request
        {
            ThreadId = thread.ThreadId,
            ClientTurnId = "client-turn-live-1",
            Prompt = "请只根据本轮CAD上下文，用阿拉伯数字回答所选直线终点X坐标；不要调用工具。",
            ContextV2 = firstContext,
            ContextV2Sha256 = firstHash,
        },
        timeout.Token);
    Stage("turn1.accepted");
    Equal(thread.ThreadId, firstTurn.ThreadId);
    Equal(firstHash, firstTurn.AcceptedContextV2Sha256);
    var firstAnswer = await ReadTurnCompletionAsync(
        events.Reader,
        firstTurn.ThreadId,
        firstTurn.TurnId,
        firstHash,
        timeout.Token);
    Stage("turn1.completed");
    Contains(firstAnswer, "10");

    var secondContext = CreateContextV2(revision: 2, lineEndX: 20d);
    var secondHash = CadContextJsonV2Codec.ComputeCanonicalSha256(secondContext);
    True(!string.Equals(firstHash, secondHash, StringComparison.Ordinal),
        "Synthetic context hashes unexpectedly matched.");
    Stage("turn2.start");
    var secondTurn = await client.StartTurnV2Async(
        new AgentTurnStartV2Request
        {
            ThreadId = thread.ThreadId,
            ClientTurnId = "client-turn-live-2",
            Prompt = "请比较本轮CAD上下文与上一轮，使用阿拉伯数字回答终点X变化量；不要调用工具。",
            ContextV2 = secondContext,
            ContextV2Sha256 = secondHash,
        },
        timeout.Token);
    Stage("turn2.accepted");
    Equal(thread.ThreadId, secondTurn.ThreadId);
    Equal(secondHash, secondTurn.AcceptedContextV2Sha256);
    var secondAnswer = await ReadTurnCompletionAsync(
        events.Reader,
        secondTurn.ThreadId,
        secondTurn.TurnId,
        secondHash,
        timeout.Token);
    Stage("turn2.completed");
    Contains(secondAnswer, "10");

    passed++;
    Console.WriteLine(
        "PASS REAL_CODEX_V2_TWO_CONTEXT_TURNS "
        + "同一thread完成两轮真实Codex v2上下文分析、哈希绑定和assistant事件回传");

    await client.StopAsync(CancellationToken.None);
    client.Dispose();
    client = null;
    await session.StopAsync(CancellationToken.None);
    ProcessMustBeGone(processId);
    Console.WriteLine(passed + "/2 specs passed");
    return 0;
}
catch (Exception exception)
{
    Stage("failed." + currentSpec);
    Console.Error.WriteLine("FAIL " + currentSpec + ": " + exception);
    Console.WriteLine(passed + "/2 specs passed");
    return 1;
}
finally
{
    Stage("cleanup.start");
    if (client is not null)
    {
        try
        {
            await client.StopAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception cleanupException)
        {
            Console.Error.WriteLine("CLEANUP bridge: " + cleanupException.GetType().Name);
        }
    }

    if (session is not null)
    {
        try
        {
            await session.StopAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception cleanupException)
        {
            Console.Error.WriteLine("CLEANUP agenthost: " + cleanupException.GetType().Name);
        }
    }
    Stage("cleanup.completed");
}

static void Stage(string value)
{
    Console.Error.WriteLine(
        "STAGE " + DateTimeOffset.UtcNow.ToString("O") + " " + value);
    Console.Error.Flush();
}

static async Task<string> ReadTurnCompletionAsync(
    ChannelReader<AgentBridgeEvent> reader,
    string threadId,
    string turnId,
    string contextSha256,
    CancellationToken cancellationToken)
{
    var deltas = new StringBuilder();
    var completedText = string.Empty;
    while (await reader.WaitToReadAsync(cancellationToken))
    {
        while (reader.TryRead(out var bridgeEvent))
        {
            if (!string.Equals(bridgeEvent.TurnId, turnId, StringComparison.Ordinal))
            {
                continue;
            }

            Equal(threadId, bridgeEvent.ThreadId);
            Equal(contextSha256, bridgeEvent.ContextSha256);
            if (string.Equals(
                    bridgeEvent.Kind,
                    AgentBridgeEventKinds.AssistantMessageDelta,
                    StringComparison.Ordinal))
            {
                deltas.Append(bridgeEvent.Delta);
            }
            else if (string.Equals(
                         bridgeEvent.Kind,
                         AgentBridgeEventKinds.AssistantMessageCompleted,
                         StringComparison.Ordinal))
            {
                completedText = bridgeEvent.Content;
            }
            else if (string.Equals(
                         bridgeEvent.Kind,
                         AgentBridgeEventKinds.TurnFailed,
                         StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Codex turn failed: " + bridgeEvent.ErrorCode + ".");
            }
            else if (string.Equals(
                         bridgeEvent.Kind,
                         AgentBridgeEventKinds.TurnCancelled,
                         StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Codex turn was cancelled.");
            }
            else if (string.Equals(
                         bridgeEvent.Kind,
                         AgentBridgeEventKinds.TurnCompleted,
                         StringComparison.Ordinal))
            {
                var answer = string.IsNullOrWhiteSpace(completedText)
                    ? deltas.ToString()
                    : completedText;
                True(!string.IsNullOrWhiteSpace(answer),
                    "Codex turn completed without assistant text.");
                return answer;
            }
        }
    }

    throw new EndOfStreamException("Agent event stream ended before turn completion.");
}

static async Task<AgentBridgeEvent> ReadKindAsync(
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

    throw new EndOfStreamException("Agent event stream ended before " + kind + ".");
}

static CadContextJsonV2 CreateContextV2(long revision, double lineEndX)
{
    return new CadContextJsonV2
    {
        CapturedAtUtc = "2026-07-20T00:00:00.000Z",
        Document = new CadContextDocumentV2
        {
            DocumentId = "doc-live-spec",
            DrawingFingerprint = new string('a', 64),
            Revision = revision,
            CurrentSpace = CadContextJsonV2Constants.ModelSpace,
            DrawingVersion = "AC1027",
            Units = "millimeters",
        },
        Selection = new CadContextSelectionV2
        {
            SnapshotHash = new string('b', 64),
            EntityCount = 1,
            ParsedEntityCount = 1,
            UnsupportedEntityCount = 0,
            Complete = true,
            Entities =
            [
                new CadContextEntityV2
                {
                    Handle = "1A",
                    OwnerSpaceHandle = "1",
                    EntityType = CadContextEntityTypesV2.Line,
                    StateHash = new string('c', 64),
                    Layer = "SPEC",
                    Line = new CadContextLineV2
                    {
                        Start = new CadPoint3(0d, 0d, 0d),
                        End = new CadPoint3(lineEndX, 0d, 0d),
                    },
                },
            ],
        },
    };
}

static string ParseAgentHostPath(string[] values)
{
    for (var index = 0; index < values.Length - 1; index += 2)
    {
        if (string.Equals(values[index], "--agent-host", StringComparison.Ordinal))
        {
            return Path.GetFullPath(values[index + 1]);
        }
    }

    throw new ArgumentException("--agent-host is required.");
}

static string ComputeFileSha256(string path)
{
    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
    using (var sha256 = SHA256.Create())
    {
        var hash = sha256.ComputeHash(input);
        try
        {
            return Convert.ToHexString(hash);
        }
        finally
        {
            Array.Clear(hash, 0, hash.Length);
        }
    }
}

static void ProcessMustBeAlive(int processId)
{
    using (var process = Process.GetProcessById(processId))
    {
        True(!process.HasExited, "AgentHost exited before live validation.");
    }
}

static void ProcessMustBeGone(int processId)
{
    try
    {
        using (var process = Process.GetProcessById(processId))
        {
            True(process.HasExited, "AgentHost remains after bounded stop.");
        }
    }
    catch (ArgumentException)
    {
    }
}

static void Contains(string value, string expected)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Expected assistant text to contain '" + expected + "', actual: " + value);
    }
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
