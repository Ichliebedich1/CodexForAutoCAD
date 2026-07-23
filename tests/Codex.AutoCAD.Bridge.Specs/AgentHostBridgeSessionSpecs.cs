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
    public static Task AuditLogIsBoundedContentFreeJsonl()
    {
        const string sessionId = "0123456789abcdef0123456789abcdef";
        using var stream = new MemoryStream();
        using (var audit = new AgentHostAuditLog(
            stream,
            sessionId,
            leaveOpen: true,
            maximumRecords: 4,
            maximumBytes: 4096))
        {
            audit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.RequestReceived,
                BridgeRequestId = "bridge-request-1",
                Method = "agent.capabilities.get",
            });
            audit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.RequestCompleted,
                BridgeRequestId = "bridge-request-1",
                Method = "agent.capabilities.get",
                OutcomeCode = AgentHostAuditOutcomeCodes.Completed,
            });
            audit.Complete();
        }

        var jsonl = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        var lines = jsonl.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Equal(4, lines.Length);
        var allowedFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "schema",
            "sequence",
            "timestampUtc",
            "sessionId",
            "eventType",
            "systemConversationId",
            "systemRequestId",
            "bridgeRequestId",
            "providerThreadId",
            "providerTurnId",
            "method",
            "approvalKind",
            "resolution",
            "outcomeCode",
            "errorCode",
            "previousRecordHash",
            "recordHash",
        };
        for (var index = 0; index < lines.Length; index++)
        {
            using var document = JsonDocument.Parse(lines[index]);
            var root = document.RootElement;
            Equal(AgentHostAuditLog.Schema, root.GetProperty("schema").GetString());
            Equal(index + 1L, root.GetProperty("sequence").GetInt64());
            Equal(sessionId, root.GetProperty("sessionId").GetString());
            if (!DateTimeOffset.TryParse(
                    root.GetProperty("timestampUtc").GetString(),
                    out var timestamp)
                || timestamp.Offset != TimeSpan.Zero)
            {
                throw new InvalidOperationException("审计时间戳不是UTC。");
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!allowedFields.Contains(property.Name))
                {
                    throw new InvalidOperationException(
                        "审计记录包含非白名单字段：" + property.Name);
                }
            }
        }

        Equal(AgentHostAuditEventTypes.SessionStarted,
            JsonDocument.Parse(lines[0]).RootElement.GetProperty("eventType").GetString());
        Equal(AgentHostAuditEventTypes.SessionStopped,
            JsonDocument.Parse(lines[3]).RootElement.GetProperty("eventType").GetString());
        var records = lines.Select(static line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToArray();
        Equal(new string('0', 64),
            records[0].GetProperty("previousRecordHash").GetString());
        for (var index = 1; index < records.Length; index++)
        {
            Equal(
                records[index - 1].GetProperty("recordHash").GetString(),
                records[index].GetProperty("previousRecordHash").GetString());
        }

        using (var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonl)))
        {
            var integrity = AgentHostAuditLog.VerifyIntegrity(
                source,
                expectedSessionId: sessionId);
            Equal(true, integrity.IsValid);
            Equal(AgentHostAuditIntegrityFailure.None, integrity.Failure);
            Equal(4L, integrity.RecordCount);
            Equal(records[^1].GetProperty("recordHash").GetString(), integrity.TerminalRecordHash);
        }

        using var boundedStream = new FlushCountingStream();
        using (var boundedAudit = new AgentHostAuditLog(
                   boundedStream,
                   sessionId,
                   leaveOpen: true,
                   maximumRecords: 2,
                   maximumBytes: 4096))
        {
            boundedAudit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.BridgeConnected,
            });
            try
            {
                boundedAudit.Record(new AgentHostAuditEvent
                {
                    EventType = AgentHostAuditEventTypes.BridgeDisconnected,
                });
                throw new InvalidOperationException("审计记录上限未触发失败闭合。");
            }
            catch (AgentHostAuditException)
            {
            }
        }

        Equal(2, boundedStream.FlushCount);

        return Task.CompletedTask;
    }

    public static Task AuditHashChainDetectsTampering()
    {
        const string sessionId = "0123456789abcdef0123456789abcdef";
        string jsonl;
        using (var stream = new MemoryStream())
        {
            using (var audit = new AgentHostAuditLog(
                       stream,
                       sessionId,
                       leaveOpen: true,
                       maximumRecords: 4,
                       maximumBytes: 4096,
                       utcNow: static () => new DateTimeOffset(
                           2026,
                           7,
                           23,
                           12,
                           34,
                           56,
                           TimeSpan.Zero)))
            {
                audit.Record(new AgentHostAuditEvent
                {
                    EventType = AgentHostAuditEventTypes.RequestReceived,
                    BridgeRequestId = "bridge-request-1",
                    Method = "agent.capabilities.get",
                });
                audit.Record(new AgentHostAuditEvent
                {
                    EventType = AgentHostAuditEventTypes.RequestCompleted,
                    BridgeRequestId = "bridge-request-1",
                    Method = "agent.capabilities.get",
                    OutcomeCode = AgentHostAuditOutcomeCodes.Completed,
                });
                audit.Complete();
            }

            jsonl = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        var lines = jsonl.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Equal(4, lines.Length);

        Equal(
            AgentHostAuditIntegrityFailure.RecordHashMismatch,
            VerifyAuditIntegrity(
                jsonl.Replace(
                    "agent.capabilities.get",
                    "agent.capabilities.set",
                    StringComparison.Ordinal)).Failure);

        var sequenceTampered = (string[])lines.Clone();
        sequenceTampered[1] = sequenceTampered[1].Replace(
            "\"sequence\":2",
            "\"sequence\":8",
            StringComparison.Ordinal);
        Equal(
            AgentHostAuditIntegrityFailure.SequenceMismatch,
            VerifyAuditIntegrity(string.Join("\n", sequenceTampered) + "\n").Failure);

        var deletedMiddleRecord = new[] { lines[0], lines[2], lines[3] };
        Equal(
            AgentHostAuditIntegrityFailure.SequenceMismatch,
            VerifyAuditIntegrity(string.Join("\n", deletedMiddleRecord) + "\n").Failure);

        using (var document = JsonDocument.Parse(lines[2]))
        {
            var previousHash = document.RootElement
                .GetProperty("previousRecordHash")
                .GetString()
                ?? throw new InvalidOperationException("缺少审计前序哈希。");
            var previousHashTampered = (string[])lines.Clone();
            previousHashTampered[2] = previousHashTampered[2].Replace(
                "\"previousRecordHash\":\"" + previousHash + "\"",
                "\"previousRecordHash\":\"" + new string('a', 64) + "\"",
                StringComparison.Ordinal);
            Equal(
                AgentHostAuditIntegrityFailure.PreviousHashMismatch,
                VerifyAuditIntegrity(string.Join("\n", previousHashTampered) + "\n").Failure);
        }

        var noTerminalRecord = new[] { lines[0], lines[1], lines[2] };
        Equal(
            AgentHostAuditIntegrityFailure.TerminalRecordMissing,
            VerifyAuditIntegrity(string.Join("\n", noTerminalRecord) + "\n").Failure);

        return Task.CompletedTask;
    }

    public static async Task AuditFailureTerminatesBridgeSession()
    {
        var keyPair = CreateBootstrapDirectionKeyPair();
        try
        {
            await using var appServer = new ScriptedAgentAppServer();
            await using var runtime = new CodexAgentRuntime(
                appServer,
                new AgentRuntimeOptions
                {
                    Sandbox = AgentSandboxMode.ReadOnly,
                    ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                    ApprovalsReviewer = AgentApprovalsReviewer.User,
                });
            using var auditStream = new MemoryStream();
            using var audit = new AgentHostAuditLog(
                auditStream,
                keyPair.AgentKeys.SessionId,
                leaveOpen: true,
                maximumRecords: 3,
                maximumBytes: 4096);
            var service = new AgentHostBridgeSession(
                runtime,
                "agenthost-audit-failure-spec",
                audit);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serviceTask = service.RunAsync(keyPair.AgentKeys, timeout.Token);
            using var client = new AgentBridgeClient(
                keyPair.HostKeys,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));
            await client.StartAsync(timeout.Token);

            var requestFailed = false;
            try
            {
                _ = await client.GetCapabilitiesAsync(
                    new AgentCapabilitiesRequest
                    {
                        ClientName = "Codex.AutoCAD.Host.2016",
                        ClientVersion = "0.3.2.0",
                        HostTarget = "autocad-r20.1-net45-x64",
                    },
                    timeout.Token);
            }
            catch (Exception)
            {
                requestFailed = true;
            }

            Equal(true, requestFailed);
            var sessionFailed = false;
            try
            {
                await serviceTask.WaitAsync(TimeSpan.FromSeconds(7));
            }
            catch (Exception)
            {
                sessionFailed = true;
            }

            Equal(true, sessionFailed);
            try
            {
                await client.StopAsync(CancellationToken.None);
            }
            catch (AgentBridgeClientException)
            {
                await client.StopAsync(CancellationToken.None);
            }

            var eventTypes = System.Text.Encoding.UTF8.GetString(auditStream.ToArray())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line).RootElement
                    .GetProperty("eventType").GetString())
                .ToArray();
            Equal(3, eventTypes.Length);
            Equal(AgentHostAuditEventTypes.SessionStarted, eventTypes[0]);
            Equal(AgentHostAuditEventTypes.BridgeConnected, eventTypes[1]);
            Equal(AgentHostAuditEventTypes.RequestReceived, eventTypes[2]);
        }
        finally
        {
            keyPair.HostKeys.Dispose();
            keyPair.AgentKeys.Dispose();
        }
    }

    public static async Task FailedRequestAuditUsesStableErrorCode()
    {
        var keyPair = CreateBootstrapDirectionKeyPair();
        try
        {
            await using var appServer = new ScriptedAgentAppServer();
            await using var runtime = new CodexAgentRuntime(
                appServer,
                new AgentRuntimeOptions
                {
                    Sandbox = AgentSandboxMode.ReadOnly,
                    ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                    ApprovalsReviewer = AgentApprovalsReviewer.User,
                });
            using var auditStream = new MemoryStream();
            using var audit = new AgentHostAuditLog(
                auditStream,
                keyPair.AgentKeys.SessionId,
                leaveOpen: true,
                maximumRecords: 32,
                maximumBytes: 16 * 1024);
            var service = new AgentHostBridgeSession(
                runtime,
                "agenthost-failed-request-audit-spec",
                audit);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serviceTask = service.RunAsync(keyPair.AgentKeys, timeout.Token);
            using var client = new AgentBridgeClient(
                keyPair.HostKeys,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));
            await client.StartAsync(timeout.Token);

            var rejected = false;
            try
            {
                _ = await client.StartThreadAsync(
                    new AgentThreadStartRequest
                    {
                        ConversationId = "conversation-request-failure-1",
                    },
                    timeout.Token);
            }
            catch (AgentBridgeRemoteException)
            {
                rejected = true;
            }

            Equal(true, rejected);
            await client.StopAsync(CancellationToken.None);
            await serviceTask.WaitAsync(TimeSpan.FromSeconds(5));
            var auditJsonl = System.Text.Encoding.UTF8.GetString(auditStream.ToArray());
            False(auditJsonl.Contains("Unexpected App Server request", StringComparison.Ordinal));
            False(auditJsonl.Contains("conversation-request-failure-1", StringComparison.Ordinal));
            var failed = auditJsonl
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
                .Single(item => string.Equals(
                    item.GetProperty("eventType").GetString(),
                    AgentHostAuditEventTypes.RequestFailed,
                    StringComparison.Ordinal));
            Equal(AgentBridgeMethods.StartThread, failed.GetProperty("method").GetString());
            Equal(AgentHostAuditOutcomeCodes.Failed,
                failed.GetProperty("outcomeCode").GetString());
            Equal(AgentHostAuditErrorCodes.InvalidState,
                failed.GetProperty("errorCode").GetString());
            Equal(false, string.IsNullOrWhiteSpace(
                failed.GetProperty("bridgeRequestId").GetString()));
        }
        finally
        {
            keyPair.HostKeys.Dispose();
            keyPair.AgentKeys.Dispose();
        }
    }

    public static async Task ApprovalRequestAuditOmitsCommandAndPath()
    {
        var keyPair = CreateBootstrapDirectionKeyPair();
        try
        {
            await using var appServer = new ScriptedAgentAppServer();
            appServer.QueueResponse("thread/start", """
                {"thread":{"id":"thread-approval-audit-1"}}
                """);
            appServer.QueueResponse("turn/start", """
                {"turn":{"id":"turn-approval-audit-1","status":"inProgress","items":[]}}
                """);
            await using var runtime = new CodexAgentRuntime(
                appServer,
                new AgentRuntimeOptions
                {
                    Sandbox = AgentSandboxMode.ReadOnly,
                    ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                    ApprovalsReviewer = AgentApprovalsReviewer.User,
                });
            using var auditStream = new MemoryStream();
            using var audit = new AgentHostAuditLog(
                auditStream,
                keyPair.AgentKeys.SessionId,
                leaveOpen: true,
                maximumRecords: 32,
                maximumBytes: 16 * 1024);
            var service = new AgentHostBridgeSession(
                runtime,
                "agenthost-approval-audit-spec",
                audit);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serviceTask = service.RunAsync(keyPair.AgentKeys, timeout.Token);
            using var client = new AgentBridgeClient(
                keyPair.HostKeys,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));
            var events = Channel.CreateUnbounded<AgentBridgeEvent>();
            client.EventReceived += (_, args) => events.Writer.TryWrite(args.BridgeEvent);
            await client.StartAsync(timeout.Token);
            var thread = await client.StartThreadAsync(
                new AgentThreadStartRequest
                {
                    ConversationId = "conversation-approval-audit-1",
                },
                timeout.Token);
            var turn = await client.StartTurnAsync(
                new AgentTurnStartRequest
                {
                    ThreadId = thread.ThreadId,
                    ClientTurnId = "client-turn-approval-audit-1",
                    Prompt = "只读审批审计测试。",
                },
                timeout.Token);
            _ = await ReadKindAsync(
                events.Reader,
                AgentBridgeEventKinds.TurnStarted,
                timeout.Token);

            _ = await appServer.EmitCommandApprovalAsync(
                new CommandApprovalRequest(
                    "item-approval-audit-1",
                    100,
                    thread.ThreadId,
                    turn.TurnId,
                    Command: "AUDIT_SECRET_COMMAND_731",
                    WorkingDirectory: "C:\\AUDIT_SECRET_PATH_732"),
                timeout.Token);
            appServer.EmitNotification("turn/completed", """
                {
                  "threadId":"thread-approval-audit-1",
                  "turn":{"id":"turn-approval-audit-1","status":"completed","items":[]}
                }
                """);
            _ = await ReadKindAsync(
                events.Reader,
                AgentBridgeEventKinds.TurnCompleted,
                timeout.Token);
            await client.StopAsync(CancellationToken.None);
            await serviceTask.WaitAsync(TimeSpan.FromSeconds(5));

            var auditJsonl = System.Text.Encoding.UTF8.GetString(auditStream.ToArray());
            False(auditJsonl.Contains("AUDIT_SECRET_COMMAND_731", StringComparison.Ordinal));
            False(auditJsonl.Contains("AUDIT_SECRET_PATH_732", StringComparison.Ordinal));
            var approval = auditJsonl
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
                .Single(item => string.Equals(
                    item.GetProperty("eventType").GetString(),
                    AgentHostAuditEventTypes.ApprovalRequested,
                    StringComparison.Ordinal));
            Equal("command", approval.GetProperty("approvalKind").GetString());
            Equal("client-turn-approval-audit-1",
                approval.GetProperty("systemRequestId").GetString());
            Equal("thread-approval-audit-1",
                approval.GetProperty("providerThreadId").GetString());
            Equal("turn-approval-audit-1",
                approval.GetProperty("providerTurnId").GetString());
        }
        finally
        {
            keyPair.HostKeys.Dispose();
            keyPair.AgentKeys.Dispose();
        }
    }

    public static async Task V2ContextTurnUsesV2MethodAndEchoesHash()
    {
        var keyPair = CreateBootstrapDirectionKeyPair();
        try
        {
            await using var appServer = new ScriptedAgentAppServer();
            appServer.QueueResponse("thread/start", """
                {"thread":{"id":"thread-v2-1"}}
                """);
            appServer.QueueResponse("turn/start", """
                {"turn":{"id":"turn-v2-1","status":"inProgress","items":[]}}
                """, () =>
                {
                    appServer.EmitNotification("turn/completed", """
                        {"threadId":"thread-v2-1","turn":{"id":"turn-v2-1","status":"completed","items":[]}}
                        """);
                });

            await using var runtime = new CodexAgentRuntime(
                appServer,
                new AgentRuntimeOptions
                {
                    Sandbox = AgentSandboxMode.ReadOnly,
                    ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                    ApprovalsReviewer = AgentApprovalsReviewer.User,
                });
            using var audit = new AgentHostAuditLog(
                new MemoryStream(),
                keyPair.AgentKeys.SessionId);
            var service = new AgentHostBridgeSession(
                runtime,
                "agenthost-v2-turn-spec",
                audit);
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
                    ClientVersion = "0.3.2.0",
                    HostTarget = "autocad-r20.1-net45-x64",
                },
                timeout.Token);
            Contains(capabilities.Methods, AgentBridgeMethods.StartTurnV2);
            if (!capabilities.SupportedCadContextSchemas.Any(schema =>
                    string.Equals(schema.Schema, CadContextJsonV2Constants.Schema, StringComparison.Ordinal)
                    && schema.SchemaVersion == CadContextJsonV2Constants.SchemaVersion))
            {
                throw new InvalidOperationException("AgentHost未公布v2 CadContext schema。");
            }

            var thread = await client.StartThreadAsync(
                new AgentThreadStartRequest { ConversationId = "conversation-v2-1" },
                timeout.Token);
            var context = CreateContextV2();
            var hash = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
            var turn = await client.StartTurnV2Async(
                new AgentTurnStartV2Request
                {
                    ThreadId = thread.ThreadId,
                    ClientTurnId = "client-turn-v2-1",
                    Prompt = "只读分析当前v2上下文。",
                    ContextV2 = context,
                    ContextV2Sha256 = hash,
                },
                timeout.Token);

            Equal(thread.ThreadId, turn.ThreadId);
            Equal("turn-v2-1", turn.TurnId);
            Equal(hash, turn.AcceptedContextV2Sha256);
            var terminal = await ReadKindAsync(
                events.Reader,
                AgentBridgeEventKinds.TurnCompleted,
                timeout.Token);
            Equal(turn.ThreadId, terminal.ThreadId);
            Equal(turn.TurnId, terminal.TurnId);
            Equal(hash, terminal.ContextSha256);

            var requests = appServer.Requests;
            Equal(2, requests.Count);
            Equal("thread/start", requests[0].Method);
            Equal("turn/start", requests[1].Method);
            var input = requests[1].Params.GetProperty("input");
            Contains(input[1].GetProperty("text").GetString() ?? string.Empty, hash);
            Contains(input[1].GetProperty("text").GetString() ?? string.Empty,
                CadContextJsonV2Codec.SerializeCanonical(context));

            await client.StopAsync(CancellationToken.None);
            await serviceTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            keyPair.HostKeys.Dispose();
            keyPair.AgentKeys.Dispose();
        }
    }

    public static async Task CancellationAuditCorrelatesSystemAndProviderIds()
    {
        var keyPair = CreateBootstrapDirectionKeyPair();
        try
        {
            await using var appServer = new ScriptedAgentAppServer();
            appServer.QueueResponse("thread/start", """
                {"thread":{"id":"thread-cancel-1"}}
                """);
            appServer.QueueResponse("turn/start", """
                {"turn":{"id":"turn-cancel-1","status":"inProgress","items":[]}}
                """);
            appServer.QueueResponse("turn/interrupt", "{}", () =>
            {
                appServer.EmitNotification("turn/completed", """
                    {
                      "threadId":"thread-cancel-1",
                      "turn":{"id":"turn-cancel-1","status":"interrupted","items":[]}
                    }
                    """);
            });

            await using var runtime = new CodexAgentRuntime(
                appServer,
                new AgentRuntimeOptions
                {
                    Sandbox = AgentSandboxMode.ReadOnly,
                    ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                    ApprovalsReviewer = AgentApprovalsReviewer.User,
                });
            using var auditStream = new MemoryStream();
            using var audit = new AgentHostAuditLog(
                auditStream,
                keyPair.AgentKeys.SessionId,
                leaveOpen: true,
                maximumRecords: 64,
                maximumBytes: 32 * 1024);
            var service = new AgentHostBridgeSession(
                runtime,
                "agenthost-cancel-audit-spec",
                audit);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serviceTask = service.RunAsync(keyPair.AgentKeys, timeout.Token);
            using var client = new AgentBridgeClient(
                keyPair.HostKeys,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));
            var events = Channel.CreateUnbounded<AgentBridgeEvent>();
            client.EventReceived += (_, args) => events.Writer.TryWrite(args.BridgeEvent);
            await client.StartAsync(timeout.Token);
            _ = await client.GetCapabilitiesAsync(
                new AgentCapabilitiesRequest
                {
                    ClientName = "Codex.AutoCAD.Host.2016",
                    ClientVersion = "0.3.2.0",
                    HostTarget = "autocad-r20.1-net45-x64",
                },
                timeout.Token);
            var thread = await client.StartThreadAsync(
                new AgentThreadStartRequest { ConversationId = "conversation-cancel-1" },
                timeout.Token);
            var turn = await client.StartTurnAsync(
                new AgentTurnStartRequest
                {
                    ThreadId = thread.ThreadId,
                    ClientTurnId = "client-turn-cancel-1",
                    Prompt = "等待取消。",
                },
                timeout.Token);
            _ = await ReadKindAsync(
                events.Reader,
                AgentBridgeEventKinds.TurnStarted,
                timeout.Token);

            await client.InterruptTurnAsync(
                new AgentTurnInterruptRequest
                {
                    ThreadId = thread.ThreadId,
                    TurnId = turn.TurnId,
                },
                timeout.Token);
            var cancelled = await ReadKindAsync(
                events.Reader,
                AgentBridgeEventKinds.TurnCancelled,
                timeout.Token);
            Equal(turn.ThreadId, cancelled.ThreadId);
            Equal(turn.TurnId, cancelled.TurnId);

            await client.StopAsync(CancellationToken.None);
            await serviceTask.WaitAsync(TimeSpan.FromSeconds(5));
            var auditEvents = System.Text.Encoding.UTF8.GetString(auditStream.ToArray())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
                .ToArray();
            var requested = auditEvents.Single(item => string.Equals(
                item.GetProperty("eventType").GetString(),
                AgentHostAuditEventTypes.CancelRequested,
                StringComparison.Ordinal));
            var dispatched = auditEvents.Single(item => string.Equals(
                item.GetProperty("eventType").GetString(),
                AgentHostAuditEventTypes.CancelDispatched,
                StringComparison.Ordinal));
            var terminal = auditEvents.Single(item => string.Equals(
                item.GetProperty("eventType").GetString(),
                AgentHostAuditEventTypes.TurnCancelled,
                StringComparison.Ordinal));
            Equal("client-turn-cancel-1", requested.GetProperty("systemRequestId").GetString());
            Equal("thread-cancel-1", requested.GetProperty("providerThreadId").GetString());
            Equal("turn-cancel-1", requested.GetProperty("providerTurnId").GetString());
            Equal(requested.GetProperty("bridgeRequestId").GetString(),
                dispatched.GetProperty("bridgeRequestId").GetString());
            Equal("client-turn-cancel-1", terminal.GetProperty("systemRequestId").GetString());
            Equal(AgentHostAuditOutcomeCodes.Cancelled,
                terminal.GetProperty("outcomeCode").GetString());
        }
        finally
        {
            keyPair.HostKeys.Dispose();
            keyPair.AgentKeys.Dispose();
        }
    }

    public static async Task DrawingQueryFlowsThroughAuthenticatedReverseBridge()
    {
        var keyPair = CreateBootstrapDirectionKeyPair();
        try
        {
            await using var appServer = new ScriptedAgentAppServer();
            appServer.QueueResponse("thread/start", """
                {"thread":{"id":"thread-query-e2e"}}
                """);
            appServer.QueueResponse("turn/start", """
                {"turn":{"id":"turn-query-e2e","status":"inProgress","items":[]}}
                """);

            var queryBroker = new AgentHostCadQueryBroker();
            await using var runtime = new CodexAgentRuntime(
                appServer,
                new AgentRuntimeOptions
                {
                    Sandbox = AgentSandboxMode.ReadOnly,
                    ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                    ApprovalsReviewer = AgentApprovalsReviewer.User,
                },
                cadDrawingQueryBroker: queryBroker);
            using var audit = new AgentHostAuditLog(
                new MemoryStream(),
                keyPair.AgentKeys.SessionId);
            var service = new AgentHostBridgeSession(
                runtime,
                "agenthost-query-e2e",
                audit,
                queryBroker);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serviceTask = service.RunAsync(keyPair.AgentKeys, timeout.Token);
            AgentDrawingQueryRequest? hostRequest = null;
            using var client = new AgentBridgeClient(
                keyPair.HostKeys,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                drawingQueryHandler: (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hostRequest = request;
                    return Task.FromResult(new AgentDrawingQueryResponse
                    {
                        RequestId = request.RequestId,
                        ThreadId = request.ThreadId,
                        TurnId = request.TurnId,
                        ToolCallId = request.ToolCallId,
                        QueryId = request.QueryId,
                        Query = new CadQueryResponse
                        {
                            IndexId = "index-trusted-host",
                            DocumentId = "document-trusted-host",
                            DocumentRevision = 12,
                            QueryId = request.QueryId,
                            Status = CadQueryStatuses.Ok,
                            Complete = true,
                            TotalMatches = 1,
                            ReturnedCount = 1,
                            Entities =
                            [
                                new CadQueryEntity
                                {
                                    ObjectId = "2A",
                                    EntityType = "line",
                                    ActualType = "AcDbLine",
                                    Layer = "AI",
                                    Space = "model",
                                    ReadStatus = CadQueryReadStatuses.Parsed,
                                },
                            ],
                        },
                    });
                });
            await client.StartAsync(timeout.Token);

            var capabilities = await client.GetCapabilitiesAsync(
                new AgentCapabilitiesRequest
                {
                    ClientName = "Codex.AutoCAD.Host.2016",
                    ClientVersion = "0.3.2.0",
                    HostTarget = "autocad-r20.1-net45-x64",
                },
                timeout.Token);
            Contains(capabilities.Methods, AgentBridgeMethods.QueryDrawing);

            var thread = await client.StartThreadAsync(
                new AgentThreadStartRequest { ConversationId = "conversation-query-e2e" },
                timeout.Token);
            var context = CreateContext("doc-query-e2e", revision: 12, lineEndX: 5d);
            var contextHash = CadContextJsonV1Codec.ComputeCanonicalSha256(context);
            const string systemRequestId = "request-query-e2e";
            var turn = await client.StartTurnAsync(
                new AgentTurnStartRequest
                {
                    ThreadId = thread.ThreadId,
                    ClientTurnId = systemRequestId,
                    Prompt = "查询AI图层中的直线。",
                    Context = context,
                    ContextSha256 = contextHash,
                },
                timeout.Token);

            var resolution = await appServer.RequestServerAsync(
                "item/tool/call",
                """
                {
                  "threadId":"thread-query-e2e",
                  "turnId":"turn-query-e2e",
                  "callId":"call-query-e2e",
                  "namespace":"cad",
                  "tool":"query_drawing",
                  "arguments":{"layers":["AI"],"pageSize":25,"includeUnsupported":false}
                }
                """,
                timeout.Token);
            if (resolution?.Result is null || resolution.Error is not null)
            {
                throw new InvalidOperationException("Runtime drawing query did not return a result.");
            }

            var result = JsonSerializer.SerializeToElement(
                resolution.Result,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Equal(true, result.GetProperty("success").GetBoolean());
            var content = result.GetProperty("contentItems")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;
            using var contentDocument = JsonDocument.Parse(content);
            var toolResult = contentDocument.RootElement;
            Equal(CadQueryStatuses.Ok, toolResult.GetProperty("status").GetString());
            Equal("2A", toolResult.GetProperty("entities")[0]
                .GetProperty("objectId")
                .GetString());
            Equal(false, toolResult.TryGetProperty("indexId", out _));
            Equal(false, toolResult.TryGetProperty("documentId", out _));
            Equal(false, toolResult.TryGetProperty("documentRevision", out _));
            Equal(false, toolResult.TryGetProperty("queryId", out _));

            if (hostRequest is null)
            {
                throw new InvalidOperationException("AutoCAD Host did not receive the reverse query.");
            }

            Equal(systemRequestId, hostRequest.RequestId);
            Equal(thread.ThreadId, hostRequest.ThreadId);
            Equal(turn.TurnId, hostRequest.TurnId);
            Equal("call-query-e2e", hostRequest.ToolCallId);
            Equal(false, string.Equals(
                hostRequest.QueryId,
                hostRequest.ToolCallId,
                StringComparison.Ordinal));
            Equal("AI", hostRequest.Filter.Layers.Single());

            var threadRequest = appServer.Requests[0];
            var dynamicNamespaces = threadRequest.Params.GetProperty("dynamicTools");
            Equal(1, dynamicNamespaces.GetArrayLength());
            var tools = dynamicNamespaces[0].GetProperty("tools");
            Equal(1, tools.GetArrayLength());
            Equal("query_drawing", tools[0].GetProperty("name").GetString());
            Equal(false, tools.EnumerateArray().Any(value => string.Equals(
                value.GetProperty("name").GetString(),
                "propose_operations",
                StringComparison.Ordinal)));

            await client.StopAsync(CancellationToken.None);
            await serviceTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            keyPair.HostKeys.Dispose();
            keyPair.AgentKeys.Dispose();
        }
    }

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
            using var auditStream = new MemoryStream();
            using var audit = new AgentHostAuditLog(
                auditStream,
                keyPair.AgentKeys.SessionId,
                leaveOpen: true,
                maximumRecords: 128,
                maximumBytes: 64 * 1024);
            var service = new AgentHostBridgeSession(
                runtime,
                "agenthost-two-turn-spec",
                audit);
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
                    Prompt = "分析所选直线。AUDIT_PRIVATE_PROMPT_731",
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

            var auditJsonl = System.Text.Encoding.UTF8.GetString(auditStream.ToArray());
            False(auditJsonl.Contains("AUDIT_PRIVATE_PROMPT_731", StringComparison.Ordinal));
            False(auditJsonl.Contains("doc-live-1", StringComparison.Ordinal));
            False(auditJsonl.Contains("canonicalJson", StringComparison.Ordinal));
            False(auditJsonl.Contains("第一轮完成", StringComparison.Ordinal));
            var auditEvents = auditJsonl.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
                .ToArray();
            Equal(AgentHostAuditEventTypes.SessionStarted,
                auditEvents[0].GetProperty("eventType").GetString());
            Equal(AgentHostAuditEventTypes.SessionStopped,
                auditEvents[^1].GetProperty("eventType").GetString());
            Equal(true, auditEvents.Any(item =>
                string.Equals(
                    item.GetProperty("eventType").GetString(),
                    AgentHostAuditEventTypes.BridgeConnected,
                    StringComparison.Ordinal)));
            Equal(true, auditEvents.Any(item =>
                string.Equals(
                    item.GetProperty("eventType").GetString(),
                    AgentHostAuditEventTypes.BridgeDisconnected,
                    StringComparison.Ordinal)));
            var threadAudit = auditEvents.Single(item =>
                string.Equals(
                    item.GetProperty("eventType").GetString(),
                    AgentHostAuditEventTypes.ThreadStarted,
                    StringComparison.Ordinal));
            Equal("conversation-live-1",
                threadAudit.GetProperty("systemConversationId").GetString());
            Equal("thread-live-1", threadAudit.GetProperty("providerThreadId").GetString());
            var firstTurnAudit = auditEvents.Single(item =>
                string.Equals(
                    item.GetProperty("eventType").GetString(),
                    AgentHostAuditEventTypes.TurnStarted,
                    StringComparison.Ordinal)
                && string.Equals(
                    item.GetProperty("systemRequestId").GetString(),
                    "client-turn-live-1",
                    StringComparison.Ordinal));
            Equal("thread-live-1",
                firstTurnAudit.GetProperty("providerThreadId").GetString());
            Equal("turn-live-1",
                firstTurnAudit.GetProperty("providerTurnId").GetString());
            Equal(false, string.IsNullOrWhiteSpace(
                firstTurnAudit.GetProperty("bridgeRequestId").GetString()));
            Equal(2, auditEvents.Count(item =>
                string.Equals(
                    item.GetProperty("eventType").GetString(),
                    AgentHostAuditEventTypes.TurnCompleted,
                    StringComparison.Ordinal)));
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

    private static CadContextJsonV2 CreateContextV2()
    {
        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T00:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-v2-1",
                DrawingFingerprint = new string('a', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "R20.1",
                Units = "millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('b', 64),
                EntityCount = 1,
                ParsedEntityCount = 1,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities = new[]
                {
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
                            End = new CadPoint3(10d, 0d, 0d),
                        },
                    },
                },
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

    private static AgentHostAuditIntegrityResult VerifyAuditIntegrity(string jsonl)
    {
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonl));
        return AgentHostAuditLog.VerifyIntegrity(source, maximumRecords: 4, maximumBytes: 4096);
    }

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

    private static void False(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }
}

internal sealed class FlushCountingStream : MemoryStream
{
    public int FlushCount { get; private set; }

    public override void Flush()
    {
        FlushCount++;
        base.Flush();
    }
}

internal sealed record SentAppServerRequest(string Method, JsonElement Params);

internal sealed class ScriptedAgentAppServer : IAgentAppServer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentQueue<(string Method, string Json, Action? BeforeReturn)> _responses = new();
    private readonly List<SentAppServerRequest> _requests = new();
    private readonly object _sync = new();
    private long _serverRequestId;

    public event EventHandler<AppServerNotification>? NotificationReceived;
    public event CommandApprovalRequestedHandler? CommandApprovalRequested;

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

    public event ServerRequestReceivedHandler? ServerRequestReceived;

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

    public async ValueTask<CommandApprovalResponse?> EmitCommandApprovalAsync(
        CommandApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var handlers = CommandApprovalRequested;
        if (handlers is null)
        {
            return null;
        }

        var approval = new RpcApprovalEvent<CommandApprovalRequest>(
            new JsonRpcId(Interlocked.Increment(ref _serverRequestId)),
            request);
        foreach (CommandApprovalRequestedHandler handler in handlers.GetInvocationList())
        {
            var response = await handler(approval, cancellationToken).ConfigureAwait(false);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    public async ValueTask<ServerRequestResolution?> RequestServerAsync(
        string method,
        string paramsJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handlers = ServerRequestReceived;
        if (handlers is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(paramsJson);
        var request = new AppServerServerRequest(
            new JsonRpcId(Interlocked.Increment(ref _serverRequestId)),
            method,
            document.RootElement.Clone());
        foreach (ServerRequestReceivedHandler handler in handlers.GetInvocationList())
        {
            var response = await handler(request, cancellationToken).ConfigureAwait(false);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
