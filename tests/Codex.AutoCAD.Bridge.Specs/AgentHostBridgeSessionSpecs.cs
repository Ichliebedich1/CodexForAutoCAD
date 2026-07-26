using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Codex.AutoCAD.AgentHost;
using Codex.AutoCAD.AgentLauncher;
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
                SystemConversationId = "conversation-1",
                SystemTurnId = "turn-1",
                SystemRequestId = "request-1",
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
            "systemSessionId",
            "segmentId",
            "previousRecordHash",
            "recordHash",
            "eventType",
            "systemConversationId",
            "systemTurnId",
            "systemRequestId",
            "bridgeRequestId",
            "providerThreadId",
            "providerTurnId",
            "method",
            "approvalKind",
            "resolution",
            "outcomeCode",
            "errorCode",
        };
        var expectedPreviousHash = AgentHostAuditIntegrity.GenesisHash;
        for (var index = 0; index < lines.Length; index++)
        {
            using var document = JsonDocument.Parse(lines[index]);
            var root = document.RootElement;
            Equal("codex.autocad.agenthost.audit/2", root.GetProperty("schema").GetString());
            Equal(index + 1L, root.GetProperty("sequence").GetInt64());
            Equal(sessionId, root.GetProperty("systemSessionId").GetString());
            Equal("segment-000001", root.GetProperty("segmentId").GetString());
            Equal(expectedPreviousHash, root.GetProperty("previousRecordHash").GetString());
            var envelope = JsonSerializer.Deserialize<AgentHostAuditLog.AgentHostAuditEnvelope>(
                    lines[index],
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("审计链记录反序列化失败。");
            Equal(
                AgentHostAuditIntegrity.ComputeRecordHash(envelope),
                root.GetProperty("recordHash").GetString());
            expectedPreviousHash = root.GetProperty("recordHash").GetString()
                ?? throw new InvalidOperationException("审计记录哈希缺失。");
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
        using (var requestDocument = JsonDocument.Parse(lines[1]))
        {
            Equal("conversation-1",
                requestDocument.RootElement.GetProperty("systemConversationId").GetString());
            Equal("turn-1",
                requestDocument.RootElement.GetProperty("systemTurnId").GetString());
            Equal("request-1",
                requestDocument.RootElement.GetProperty("systemRequestId").GetString());
        }

        Equal(AgentHostAuditEventTypes.SessionStopped,
            JsonDocument.Parse(lines[3]).RootElement.GetProperty("eventType").GetString());

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

    public static async Task AuditConcurrentWritesAreSequentialAndComplete()
    {
        const string sessionId = "0123456789abcdef0123456789abcdef";
        const int workers = 8;
        const int recordsPerWorker = 25;
        using var stream = new MemoryStream();
        using var audit = new AgentHostAuditLog(
            stream,
            sessionId,
            leaveOpen: true,
            maximumRecords: (workers * recordsPerWorker) + 2,
            maximumBytes: 256 * 1024);

        var tasks = Enumerable.Range(0, workers)
            .Select(worker => Task.Run(() =>
            {
                for (var index = 0; index < recordsPerWorker; index++)
                {
                    audit.Record(new AgentHostAuditEvent
                    {
                        EventType = AgentHostAuditEventTypes.RequestReceived,
                        SystemRequestId = $"request-{worker}-{index}",
                        Method = "agent.turn.start",
                    });
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);
        audit.Complete();

        var records = System.Text.Encoding.UTF8.GetString(stream.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        Equal((workers * recordsPerWorker) + 2, records.Length);
        Equal(AgentHostAuditEventTypes.SessionStarted,
            records[0].GetProperty("eventType").GetString());
        Equal(AgentHostAuditEventTypes.SessionStopped,
            records[^1].GetProperty("eventType").GetString());

        var requestIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < records.Length; index++)
        {
            Equal(index + 1L, records[index].GetProperty("sequence").GetInt64());
            if (index is > 0 && index < records.Length - 1)
            {
                Equal(AgentHostAuditEventTypes.RequestReceived,
                    records[index].GetProperty("eventType").GetString());
                var requestId = records[index].GetProperty("systemRequestId").GetString()
                    ?? throw new InvalidOperationException("并发审计请求标识缺失。");
                if (!requestIds.Add(requestId))
                {
                    throw new InvalidOperationException("并发审计出现重复请求标识。");
                }
            }
        }

        Equal(workers * recordsPerWorker, requestIds.Count);
    }

    public static Task AuditPartialWriteFailsClosedAndCannotResume()
    {
        const string sessionId = "0123456789abcdef0123456789abcdef";
        using var stream = new PartialWriteFailureStream();
        using var audit = new AgentHostAuditLog(
            stream,
            sessionId,
            leaveOpen: true,
            maximumRecords: 8,
            maximumBytes: 16 * 1024);
        stream.FailNextWrite();

        try
        {
            audit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.RequestReceived,
                SystemRequestId = "request-partial-1",
                Method = "agent.turn.start",
            });
            throw new InvalidOperationException("部分审计写入未触发失败关闭。");
        }
        catch (AgentHostAuditException)
        {
        }

        var writeCountAfterFailure = stream.WriteCount;
        try
        {
            audit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.RequestFailed,
                SystemRequestId = "request-partial-2",
                OutcomeCode = AgentHostAuditOutcomeCodes.Failed,
                ErrorCode = AgentHostAuditErrorCodes.AuditUnavailable,
            });
            throw new InvalidOperationException("审计故障后错误地恢复写入。");
        }
        catch (AgentHostAuditException)
        {
        }

        Equal(writeCountAfterFailure, stream.WriteCount);
        var chunks = System.Text.Encoding.UTF8.GetString(stream.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Equal(2, chunks.Length);
        using (var first = JsonDocument.Parse(chunks[0]))
        {
            Equal(AgentHostAuditEventTypes.SessionStarted,
                first.RootElement.GetProperty("eventType").GetString());
        }

        try
        {
            using var _ = JsonDocument.Parse(chunks[1]);
            throw new InvalidOperationException("部分审计记录未保持可检测的截断状态。");
        }
        catch (JsonException)
        {
        }

        return Task.CompletedTask;
    }

    public static Task AuditAnchorPersistenceFailureFailsClosedAndIsDetectable()
    {
        const string sessionId = "abcdef0123456789abcdef0123456789";
        using var stream = new MemoryStream();
        using var anchorSink = new SyntheticAuditAnchorFailureSink();
        using var audit = new AgentHostAuditLog(
            stream,
            sessionId,
            "segment-000001",
            AgentHostAuditIntegrity.GenesisHash,
            anchorSink,
            leaveOpen: true,
            maximumRecords: 8,
            maximumBytes: 16 * 1024);
        anchorSink.FailNextWrite();

        try
        {
            audit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.RequestReceived,
                SystemRequestId = "request-anchor-failure-1",
                Method = "agent.turn.start",
            });
            throw new InvalidOperationException("审计锚点持久化故障未触发失败关闭。");
        }
        catch (AgentHostAuditException exception)
        {
            Equal(true, exception.InnerException is IOException);
        }

        var bytesAfterFailure = stream.ToArray();
        var writeCountAfterFailure = anchorSink.WriteCount;
        try
        {
            audit.Fail(AgentHostAuditErrorCodes.AuditUnavailable);
            throw new InvalidOperationException("锚点故障后错误地产生第二个审计终态。");
        }
        catch (AgentHostAuditException)
        {
        }

        Equal(writeCountAfterFailure, anchorSink.WriteCount);
        Equal(bytesAfterFailure.Length, stream.Length);
        var records = System.Text.Encoding.UTF8.GetString(bytesAfterFailure)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Equal(2, records.Length);
        using (var second = JsonDocument.Parse(records[1]))
        {
            Equal(
                AgentHostAuditEventTypes.RequestReceived,
                second.RootElement.GetProperty("eventType").GetString());
        }

        var durableAnchor = anchorSink.Current
            ?? throw new InvalidOperationException("审计锚点故障夹具缺少最后耐久锚点。");
        Equal(1L, durableAnchor.Sequence);
        ExpectAuditIntegrityFailure(() => AgentHostAuditIntegrity.Verify(
            new ReadOnlyMemory<byte>[] { bytesAfterFailure },
            durableAnchor));
        return Task.CompletedTask;
    }

    public static Task AuditHashChainDetectsTamperingAcrossSegments()
    {
        const string sessionId = "0123456789abcdef0123456789abcdef";
        using var firstStream = new MemoryStream();
        using var firstAnchor = new CapturingAuditAnchorSink();
        using (var firstAudit = new AgentHostAuditLog(
                   firstStream,
                   sessionId,
                   "segment-000001",
                   AgentHostAuditIntegrity.GenesisHash,
                   firstAnchor,
                   leaveOpen: true,
                   maximumRecords: 8,
                   maximumBytes: 16 * 1024))
        {
            firstAudit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.RequestReceived,
                SystemRequestId = "request-chain-1",
                Method = "agent.turn.start",
            });
            firstAudit.Complete();
        }

        var firstFinal = firstAnchor.Current
            ?? throw new InvalidOperationException("第一段审计锚点缺失。");
        using var secondStream = new MemoryStream();
        using var secondAnchor = new CapturingAuditAnchorSink();
        using (var secondAudit = new AgentHostAuditLog(
                   secondStream,
                   sessionId,
                   "segment-000002",
                   firstFinal.RecordHash,
                   secondAnchor,
                   leaveOpen: true,
                   maximumRecords: 8,
                   maximumBytes: 16 * 1024))
        {
            secondAudit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.RequestCompleted,
                SystemRequestId = "request-chain-2",
                OutcomeCode = AgentHostAuditOutcomeCodes.Completed,
            });
            secondAudit.Complete();
        }

        var finalAnchor = secondAnchor.Current
            ?? throw new InvalidOperationException("第二段审计锚点缺失。");
        var firstBytes = firstStream.ToArray();
        var secondBytes = secondStream.ToArray();
        var verified = AgentHostAuditIntegrity.Verify(
            new ReadOnlyMemory<byte>[] { firstBytes, secondBytes },
            finalAnchor);
        Equal(2, verified.SegmentCount);
        Equal(6L, verified.RecordCount);
        Equal(finalAnchor.RecordHash, verified.FinalRecordHash);

        var firstLines = System.Text.Encoding.UTF8.GetString(firstBytes)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var deleted = System.Text.Encoding.UTF8.GetBytes(
            string.Join('\n', firstLines.Where((_, index) => index != 1)) + "\n");
        ExpectAuditIntegrityFailure(() => AgentHostAuditIntegrity.Verify(
            new ReadOnlyMemory<byte>[] { deleted, secondBytes },
            finalAnchor));

        var insertedLines = firstLines.Take(2)
            .Concat(new[] { firstLines[1] })
            .Concat(firstLines.Skip(2));
        var inserted = System.Text.Encoding.UTF8.GetBytes(
            string.Join('\n', insertedLines) + "\n");
        ExpectAuditIntegrityFailure(() => AgentHostAuditIntegrity.Verify(
            new ReadOnlyMemory<byte>[] { inserted, secondBytes },
            finalAnchor));

        var modifiedText = System.Text.Encoding.UTF8.GetString(firstBytes).Replace(
            "\"eventType\":\"request_received\"",
            "\"eventType\":\"request_failed\"",
            StringComparison.Ordinal);
        if (string.Equals(
                modifiedText,
                System.Text.Encoding.UTF8.GetString(firstBytes),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("审计修改样本未命中目标字段。");
        }
        ExpectAuditIntegrityFailure(() => AgentHostAuditIntegrity.Verify(
            new ReadOnlyMemory<byte>[]
            {
                System.Text.Encoding.UTF8.GetBytes(modifiedText),
                secondBytes,
            },
            finalAnchor));

        ExpectAuditIntegrityFailure(() => AgentHostAuditIntegrity.Verify(
            new ReadOnlyMemory<byte>[] { firstBytes.AsMemory(0, firstBytes.Length - 1), secondBytes },
            finalAnchor));
        ExpectAuditIntegrityFailure(() => AgentHostAuditIntegrity.Verify(
            new ReadOnlyMemory<byte>[] { secondBytes, firstBytes },
            finalAnchor));
        ExpectAuditIntegrityFailure(() => AgentHostAuditIntegrity.Verify(
            new ReadOnlyMemory<byte>[] { firstBytes, secondBytes },
            new AgentHostAuditAnchor
            {
                SystemSessionId = finalAnchor.SystemSessionId,
                SegmentId = finalAnchor.SegmentId,
                Sequence = finalAnchor.Sequence,
                RecordHash = AgentHostAuditIntegrity.GenesisHash,
            }));

        return Task.CompletedTask;
    }

    public static Task AuditFileAnchorTracksDurableChainHead()
    {
        const string sessionId = "fedcba9876543210fedcba9876543210";
        var auditDirectory = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-anchor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(auditDirectory);
        try
        {
            using (var audit = AgentHostAuditLog.CreateInSessionDirectory(
                       sessionId,
                       auditDirectory))
            {
                audit.Record(new AgentHostAuditEvent
                {
                    EventType = AgentHostAuditEventTypes.BridgeConnected,
                    OutcomeCode = AgentHostAuditOutcomeCodes.Connected,
                });
                audit.Complete();
            }

            var auditPath = Path.Combine(auditDirectory, sessionId + ".jsonl");
            var anchorPath = Path.Combine(auditDirectory, sessionId + ".anchor.json");
            Equal(true, File.Exists(auditPath));
            Equal(true, File.Exists(anchorPath));
            var anchor = JsonSerializer.Deserialize<AgentHostAuditAnchor>(
                    File.ReadAllText(anchorPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("审计锚点文件反序列化失败。");
            Equal(AgentHostAuditAnchor.SchemaValue, anchor.Schema);
            Equal(sessionId, anchor.SystemSessionId);
            Equal("segment-000001", anchor.SegmentId);
            Equal(3L, anchor.Sequence);
            var result = AgentHostAuditIntegrity.Verify(
                new ReadOnlyMemory<byte>[] { File.ReadAllBytes(auditPath) },
                anchor);
            Equal(1, result.SegmentCount);
            Equal(3L, result.RecordCount);
            Equal(anchor.RecordHash, result.FinalRecordHash);
        }
        finally
        {
            Directory.Delete(auditDirectory, recursive: true);
        }

        var blockedDirectory = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-anchor-blocked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(blockedDirectory);
        var blockedAnchorPath = Path.Combine(
            blockedDirectory,
            sessionId + ".anchor.json");
        const string sentinel = "existing-protected-anchor";
        File.WriteAllText(blockedAnchorPath, sentinel);
        try
        {
            try
            {
                using var _ = AgentHostAuditLog.CreateInSessionDirectory(
                    sessionId,
                    blockedDirectory);
                throw new InvalidOperationException("旧审计锚点被错误覆盖。");
            }
            catch (AgentHostAuditException)
            {
            }

            Equal(sentinel, File.ReadAllText(blockedAnchorPath));
        }
        finally
        {
            Directory.Delete(blockedDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public static Task AuditRedactedExportVerifiesChainAndOmitsProviderIdentity()
    {
        const string sessionId = "1234567890abcdef1234567890abcdef";
        using var auditStream = new MemoryStream();
        using var anchorSink = new CapturingAuditAnchorSink();
        using (var audit = new AgentHostAuditLog(
                   auditStream,
                   sessionId,
                   "segment-000001",
                   AgentHostAuditIntegrity.GenesisHash,
                   anchorSink,
                   leaveOpen: true,
                   maximumRecords: 8,
                   maximumBytes: 16 * 1024))
        {
            audit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.RequestCompleted,
                SystemConversationId = "conversation-export-1",
                SystemTurnId = "turn-export-1",
                SystemRequestId = "request-export-1",
                BridgeRequestId = "bridge-export-1",
                ProviderThreadId = "provider-thread-sensitive-value",
                ProviderTurnId = "provider-turn-sensitive-value",
                Method = "agent.turn.start",
                OutcomeCode = AgentHostAuditOutcomeCodes.Completed,
            });
            audit.Complete();
        }

        var anchor = anchorSink.Current
            ?? throw new InvalidOperationException("脱敏导出缺少审计锚点。");
        using var exportStream = new MemoryStream();
        var verification = AgentHostAuditRedactedExport.WriteVerified(
            exportStream,
            new ReadOnlyMemory<byte>[] { auditStream.ToArray() },
            anchor);
        Equal(3L, verification.RecordCount);
        var exportText = System.Text.Encoding.UTF8.GetString(exportStream.ToArray());
        Equal(false, exportText.Contains("provider-thread-sensitive-value", StringComparison.Ordinal));
        Equal(false, exportText.Contains("provider-turn-sensitive-value", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(exportText);
        var root = document.RootElement;
        Equal(AgentHostAuditRedactedExport.Schema, root.GetProperty("schema").GetString());
        Equal(sessionId, root.GetProperty("systemSessionId").GetString());
        Equal(3L, root.GetProperty("recordCount").GetInt64());
        Equal(anchor.RecordHash, root.GetProperty("finalRecordHash").GetString());
        var records = root.GetProperty("records");
        Equal(3, records.GetArrayLength());
        var request = records[1];
        Equal("request-export-1", request.GetProperty("systemRequestId").GetString());
        Equal(false, request.TryGetProperty("providerThreadId", out _));
        Equal(false, request.TryGetProperty("providerTurnId", out _));
        Equal(false, request.TryGetProperty("payload", out _));
        return Task.CompletedTask;
    }

    public static Task AuditExportServiceBuffersVerifiedOutput()
    {
        const string completeSession = "fedcba9876543210fedcba9876543210";
        const string missingAnchorSession = "fedcba9876543210fedcba9876543211";
        const string corruptSession = "fedcba9876543210fedcba9876543212";
        const string mismatchSession = "fedcba9876543210fedcba9876543213";
        const string nonTerminalSession = "fedcba9876543210fedcba9876543214";
        var auditRoot = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-export-service-" + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(auditRoot);
            CreateAudit(completeSession, includeSensitiveProviderIds: true);
            CreateAudit(missingAnchorSession, includeSensitiveProviderIds: false);
            CreateAudit(corruptSession, includeSensitiveProviderIds: false);
            CreateAudit(mismatchSession, includeSensitiveProviderIds: false);
            CreateClosedNonTerminalAudit(
                nonTerminalSession,
                store.SegmentDirectory,
                store.AnchorDirectory);

            File.Delete(Path.Combine(
                store.AnchorDirectory,
                missingAnchorSession + ".anchor.json"));
            File.AppendAllText(
                Path.Combine(
                    store.SegmentDirectory,
                    corruptSession + ".segment-000001.jsonl"),
                "broken\n",
                new System.Text.UTF8Encoding(false));

            var mismatchAnchorPath = Path.Combine(
                store.AnchorDirectory,
                mismatchSession + ".anchor.json");
            var mismatchAnchor = JsonSerializer.Deserialize<AgentHostAuditAnchor>(
                    File.ReadAllText(mismatchAnchorPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("导出测试锚点缺失。");
            File.WriteAllText(
                mismatchAnchorPath,
                JsonSerializer.Serialize(
                    new AgentHostAuditAnchor
                    {
                        Schema = mismatchAnchor.Schema,
                        SystemSessionId = mismatchAnchor.SystemSessionId,
                        SegmentId = mismatchAnchor.SegmentId,
                        Sequence = mismatchAnchor.Sequence,
                        RecordHash = AgentHostAuditIntegrity.GenesisHash,
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                new System.Text.UTF8Encoding(false));

            using var exported = new MemoryStream();
            AgentHostAuditExportService.ExportSessionToStream(
                completeSession,
                store.SegmentDirectory,
                store.AnchorDirectory,
                exported);
            var exportText = System.Text.Encoding.UTF8.GetString(exported.ToArray());
            Equal(false, exportText.Contains(
                "provider-thread-must-not-export",
                StringComparison.Ordinal));
            Equal(false, exportText.Contains(
                "provider-turn-must-not-export",
                StringComparison.Ordinal));
            using (var document = JsonDocument.Parse(exportText))
            {
                Equal(AgentHostAuditRedactedExport.Schema,
                    document.RootElement.GetProperty("schema").GetString());
                Equal(completeSession,
                    document.RootElement.GetProperty("systemSessionId").GetString());
            }

            ExpectRejectedWithoutOutput(missingAnchorSession);
            ExpectRejectedWithoutOutput(corruptSession);
            ExpectRejectedWithoutOutput(mismatchSession);
            ExpectRejectedWithoutOutput(nonTerminalSession);
            ExpectRejectedWithoutOutput("invalid-session-id");

            using var closedDestination = new MemoryStream();
            closedDestination.Dispose();
            try
            {
                AgentHostAuditExportService.ExportSessionToStream(
                    completeSession,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    closedDestination);
                throw new InvalidOperationException("不可写导出目标被错误接受。");
            }
            catch (AgentHostAuditCatalogException)
            {
            }
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(auditRoot))
            {
                Directory.Delete(auditRoot, recursive: true);
            }
        }

        return Task.CompletedTask;

        void CreateAudit(string sessionId, bool includeSensitiveProviderIds)
        {
            using var audit = AgentHostAuditLog.CreateRotatingInProtectedDirectories(
                sessionId,
                store!.SegmentDirectory,
                store.AnchorDirectory,
                maximumRecords: 8,
                maximumBytes: 16 * 1024,
                maximumSegments: 2);
            audit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.RequestCompleted,
                SystemRequestId = "request-export-service-1",
                ProviderThreadId = includeSensitiveProviderIds
                    ? "provider-thread-must-not-export"
                    : null,
                ProviderTurnId = includeSensitiveProviderIds
                    ? "provider-turn-must-not-export"
                    : null,
                Method = "agent.turn.start",
                OutcomeCode = AgentHostAuditOutcomeCodes.Completed,
            });
            audit.Complete();
        }

        void ExpectRejectedWithoutOutput(string sessionId)
        {
            using var destination = new MemoryStream();
            var sentinel = System.Text.Encoding.UTF8.GetBytes("sentinel");
            destination.Write(sentinel, 0, sentinel.Length);
            try
            {
                AgentHostAuditExportService.ExportSessionToStream(
                    sessionId,
                    store!.SegmentDirectory,
                    store.AnchorDirectory,
                    destination);
                throw new InvalidOperationException("非完整审计会话被错误导出。");
            }
            catch (AgentHostAuditCatalogException)
            {
            }

            Equal("sentinel", System.Text.Encoding.UTF8.GetString(destination.ToArray()));
        }
    }

    public static Task AuditAutomaticallyRotatesWithContinuousChain()
    {
        const string sessionId = "abcdefabcdefabcdefabcdefabcdefab";
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-rotation-" + Guid.NewGuid().ToString("N"));
        var segments = Path.Combine(root, "segments");
        var anchors = Path.Combine(root, "anchors");
        Directory.CreateDirectory(segments);
        Directory.CreateDirectory(anchors);
        try
        {
            using (var audit = AgentHostAuditLog.CreateRotatingInProtectedDirectories(
                       sessionId,
                       segments,
                       anchors,
                       maximumRecords: 2,
                       maximumBytes: 16 * 1024,
                       maximumSegments: 3))
            {
                audit.Record(new AgentHostAuditEvent
                {
                    EventType = AgentHostAuditEventTypes.BridgeConnected,
                    OutcomeCode = AgentHostAuditOutcomeCodes.Connected,
                });
                audit.Record(new AgentHostAuditEvent
                {
                    EventType = AgentHostAuditEventTypes.RequestReceived,
                    SystemRequestId = "request-rotated-1",
                    Method = "agent.turn.start",
                });
                audit.Complete();
            }

            var firstPath = Path.Combine(
                segments,
                sessionId + ".segment-000001.jsonl");
            var secondPath = Path.Combine(
                segments,
                sessionId + ".segment-000002.jsonl");
            var anchorPath = Path.Combine(anchors, sessionId + ".anchor.json");
            Equal(true, File.Exists(firstPath));
            Equal(true, File.Exists(secondPath));
            Equal(2, Directory.GetFiles(segments, sessionId + ".segment-*.jsonl").Length);

            var anchor = JsonSerializer.Deserialize<AgentHostAuditAnchor>(
                    File.ReadAllText(anchorPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("轮转审计锚点反序列化失败。");
            Equal("segment-000002", anchor.SegmentId);
            Equal(2L, anchor.Sequence);
            var verified = AgentHostAuditIntegrity.Verify(
                new ReadOnlyMemory<byte>[]
                {
                    File.ReadAllBytes(firstPath),
                    File.ReadAllBytes(secondPath),
                },
                anchor);
            Equal(2, verified.SegmentCount);
            Equal(4L, verified.RecordCount);
            Equal(anchor.RecordHash, verified.FinalRecordHash);

            const string blockedSessionId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            var blockedPath = Path.Combine(
                segments,
                blockedSessionId + ".segment-000001.jsonl");
            const string sentinel = "existing-segment-must-not-be-overwritten";
            File.WriteAllText(blockedPath, sentinel);
            try
            {
                using var _ = AgentHostAuditLog.CreateRotatingInProtectedDirectories(
                    blockedSessionId,
                    segments,
                    anchors,
                    maximumRecords: 2,
                    maximumBytes: 16 * 1024,
                    maximumSegments: 3);
                throw new InvalidOperationException("旧审计分段被错误覆盖。");
            }
            catch (AgentHostAuditException)
            {
            }

            Equal(sentinel, File.ReadAllText(blockedPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    public static Task AuditPersistsAfterSessionWorkspaceCleanup()
    {
        const string sessionId = "00112233445566778899aabbccddeeff";
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-persistent-audit-" + Guid.NewGuid().ToString("N"));
        var sessionsRoot = Path.Combine(root, "sessions");
        var persistentRoot = Path.Combine(root, "persistent-audit");
        Directory.CreateDirectory(sessionsRoot);
        AgentSessionWorkspaceLease? workspace = null;
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            workspace = AgentSessionWorkspaceLease.Create(sessionsRoot, sessionId);
            store = AgentPersistentAuditStoreLease.Create(persistentRoot);
            using (var audit = AgentHostAuditLog.CreateInProtectedDirectories(
                       sessionId,
                       store.SegmentDirectory,
                       store.AnchorDirectory))
            {
                audit.Record(new AgentHostAuditEvent
                {
                    EventType = AgentHostAuditEventTypes.BridgeConnected,
                    OutcomeCode = AgentHostAuditOutcomeCodes.Connected,
                });
                audit.Complete();
            }

            var sessionPath = workspace.SessionPath;
            var auditPath = Path.Combine(store.SegmentDirectory, sessionId + ".jsonl");
            var anchorPath = Path.Combine(store.AnchorDirectory, sessionId + ".anchor.json");
            Equal(true, File.Exists(auditPath));
            Equal(true, File.Exists(anchorPath));
            Equal(false, string.Equals(
                Path.GetDirectoryName(auditPath),
                Path.GetDirectoryName(anchorPath),
                StringComparison.OrdinalIgnoreCase));

            workspace.Dispose();
            workspace = null;
            Equal(false, Directory.Exists(sessionPath));
            Equal(true, File.Exists(auditPath));
            Equal(true, File.Exists(anchorPath));

            var anchor = JsonSerializer.Deserialize<AgentHostAuditAnchor>(
                    File.ReadAllText(anchorPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("持久审计锚点反序列化失败。");
            var verified = AgentHostAuditIntegrity.Verify(
                new ReadOnlyMemory<byte>[] { File.ReadAllBytes(auditPath) },
                anchor);
            Equal(3L, verified.RecordCount);
            Equal(anchor.RecordHash, verified.FinalRecordHash);
        }
        finally
        {
            workspace?.Dispose();
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static Task AuditCatalogClassifiesPersistentArtifacts()
    {
        const string completeSession = "00112233445566778899aabbccddee01";
        const string missingAnchorSession = "00112233445566778899aabbccddee02";
        const string corruptSession = "00112233445566778899aabbccddee03";
        const string mismatchSession = "00112233445566778899aabbccddee04";
        const string nonTerminalSession = "00112233445566778899aabbccddee05";
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-catalog-" + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            var segments = store.SegmentDirectory;
            var anchors = store.AnchorDirectory;
            CreateCatalogAudit(completeSession, segments, anchors);
            CreateCatalogAudit(missingAnchorSession, segments, anchors);
            CreateCatalogAudit(corruptSession, segments, anchors);
            CreateCatalogAudit(mismatchSession, segments, anchors);
            CreateClosedNonTerminalAudit(
                nonTerminalSession,
                segments,
                anchors);

            File.Delete(Path.Combine(anchors, missingAnchorSession + ".anchor.json"));
            File.AppendAllText(
                Path.Combine(segments, corruptSession + ".segment-000001.jsonl"),
                "broken\n",
                new System.Text.UTF8Encoding(false));

            var mismatchAnchorPath = Path.Combine(anchors, mismatchSession + ".anchor.json");
            var mismatchAnchor = JsonSerializer.Deserialize<AgentHostAuditAnchor>(
                    File.ReadAllText(mismatchAnchorPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("目录分类测试锚点缺失。");
            var mismatchJson = JsonSerializer.Serialize(
                new AgentHostAuditAnchor
                {
                    Schema = mismatchAnchor.Schema,
                    SystemSessionId = mismatchAnchor.SystemSessionId,
                    SegmentId = mismatchAnchor.SegmentId,
                    Sequence = mismatchAnchor.Sequence,
                    RecordHash = AgentHostAuditIntegrity.GenesisHash,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllText(mismatchAnchorPath, mismatchJson, new System.Text.UTF8Encoding(false));

            var temporaryAnchor = Path.Combine(
                anchors,
                completeSession + ".anchor.json.tmp-interrupted");
            File.WriteAllText(temporaryAnchor, "{}", new System.Text.UTF8Encoding(false));

            var snapshot = AgentHostAuditCatalog.Read(segments, anchors);
            Equal(5, snapshot.Entries.Count);
            Equal(true, snapshot.EnumerationComplete);

            var complete = FindCatalogEntry(snapshot, completeSession);
            Equal(AgentHostAuditCatalogStatus.Incomplete, complete.Status);
            Equal(AgentHostAuditCatalogReasonCodes.TemporaryAnchor, complete.ReasonCode);

            var missingAnchor = FindCatalogEntry(snapshot, missingAnchorSession);
            Equal(AgentHostAuditCatalogStatus.Incomplete, missingAnchor.Status);
            Equal(AgentHostAuditCatalogReasonCodes.MissingAnchor, missingAnchor.ReasonCode);

            var corrupt = FindCatalogEntry(snapshot, corruptSession);
            Equal(AgentHostAuditCatalogStatus.Corrupt, corrupt.Status);
            Equal(AgentHostAuditCatalogReasonCodes.ChainInvalid, corrupt.ReasonCode);

            var mismatch = FindCatalogEntry(snapshot, mismatchSession);
            Equal(AgentHostAuditCatalogStatus.AnchorMismatch, mismatch.Status);
            Equal(AgentHostAuditCatalogReasonCodes.AnchorMismatch, mismatch.ReasonCode);

            var nonTerminal = FindCatalogEntry(snapshot, nonTerminalSession);
            if (nonTerminal.Status != AgentHostAuditCatalogStatus.Incomplete)
            {
                throw new InvalidOperationException(
                    "Expected non-terminal audit to be incomplete, actual "
                    + nonTerminal.Status
                    + "/"
                    + nonTerminal.ReasonCode
                    + ".");
            }
            Equal(AgentHostAuditCatalogReasonCodes.SessionNotTerminal, nonTerminal.ReasonCode);
            Equal(2L, nonTerminal.RecordCount);

            File.Delete(temporaryAnchor);
            var completeSnapshot = AgentHostAuditCatalog.Read(segments, anchors);
            var completeEntry = FindCatalogEntry(completeSnapshot, completeSession);
            Equal(AgentHostAuditCatalogStatus.Complete, completeEntry.Status);
            Equal(3L, completeEntry.RecordCount);
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;

        static void CreateCatalogAudit(string sessionId, string segmentDirectory, string anchorDirectory)
        {
            using var audit = AgentHostAuditLog.CreateRotatingInProtectedDirectories(
                sessionId,
                segmentDirectory,
                anchorDirectory,
                maximumRecords: 8,
                maximumBytes: 16 * 1024,
                maximumSegments: 2);
            audit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.BridgeConnected,
                OutcomeCode = AgentHostAuditOutcomeCodes.Connected,
            });
            audit.Complete();
        }

        static AgentHostAuditCatalogEntry FindCatalogEntry(
            AgentHostAuditCatalogSnapshot snapshot,
            string sessionId)
            => snapshot.Entries.Single(entry =>
                string.Equals(entry.SystemSessionId, sessionId, StringComparison.Ordinal));
    }
    public static Task AuditRetentionPlannerIsReadOnlyAndConservative()
    {
        const string ageSession = "11111111111111111111111111111111";
        const string capacitySession = "22222222222222222222222222222222";
        const string minimumSession = "33333333333333333333333333333333";
        const string nonTerminalSession = "44444444444444444444444444444444";
        const string corruptSession = "55555555555555555555555555555555";
        const string mismatchSession = "66666666666666666666666666666666";
        var utcNow = new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-" + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            CreateCompleteAudit(ageSession);
            CreateCompleteAudit(capacitySession);
            CreateCompleteAudit(minimumSession);
            CreateClosedNonTerminalAudit(
                nonTerminalSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            CreateCompleteAudit(corruptSession);
            CreateCompleteAudit(mismatchSession);

            File.AppendAllText(
                SegmentPath(corruptSession),
                "broken\n",
                new System.Text.UTF8Encoding(false));
            var mismatchAnchorPath = AnchorPath(mismatchSession);
            var mismatchAnchor = JsonSerializer.Deserialize<AgentHostAuditAnchor>(
                    File.ReadAllText(mismatchAnchorPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("保留规划测试锚点缺失。");
            File.WriteAllText(
                mismatchAnchorPath,
                JsonSerializer.Serialize(
                    new AgentHostAuditAnchor
                    {
                        Schema = mismatchAnchor.Schema,
                        SystemSessionId = mismatchAnchor.SystemSessionId,
                        SegmentId = mismatchAnchor.SegmentId,
                        Sequence = mismatchAnchor.Sequence,
                        RecordHash = AgentHostAuditIntegrity.GenesisHash,
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                new System.Text.UTF8Encoding(false));

            SetSessionWriteTime(ageSession, utcNow.AddDays(-60));
            SetSessionWriteTime(capacitySession, utcNow.AddDays(-10));
            SetSessionWriteTime(minimumSession, utcNow.AddDays(-1));
            SetSessionWriteTime(nonTerminalSession, utcNow.AddDays(-20));
            SetSessionWriteTime(corruptSession, utcNow.AddDays(-40));
            SetSessionWriteTime(mismatchSession, utcNow.AddDays(-50));

            var ignoredPath = Path.Combine(store.SegmentDirectory, "operator-note.bin");
            File.WriteAllBytes(ignoredPath, new byte[257]);
            File.SetLastWriteTimeUtc(ignoredPath, utcNow.UtcDateTime.AddDays(-90));
            var before = CaptureArtifacts();

            var plan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                new AgentHostAuditRetentionPolicy
                {
                    OlderThanDays = 30,
                    MaximumStoreBytes = 1,
                    MinimumCompleteSessionsToRetain = 1,
                },
                utcNow);

            Equal(AgentHostAuditRetentionPlan.SchemaValue, plan.Schema);
            Equal(false, plan.CapacitySatisfied);
            Equal(before.Values.Sum(static item => item.Length), plan.CurrentStoreBytes);
            Equal(true, plan.IgnoredFileCount >= 1);
            Equal(plan.CurrentStoreBytes - plan.CandidateBytes, plan.ProjectedStoreBytes);
            Equal(AgentHostAuditRetentionActionCodes.EligibleAge, Action(plan, ageSession));
            Equal(AgentHostAuditRetentionActionCodes.EligibleCapacity, Action(plan, capacitySession));
            Equal(AgentHostAuditRetentionActionCodes.RetainMinimum, Action(plan, minimumSession));
            Equal(AgentHostAuditRetentionActionCodes.RetainManualReview, Action(plan, nonTerminalSession));
            Equal(AgentHostAuditRetentionActionCodes.RetainManualReview, Action(plan, corruptSession));
            Equal(AgentHostAuditRetentionActionCodes.RetainManualReview, Action(plan, mismatchSession));

            var json = JsonSerializer.Serialize(
                plan,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Equal(false, json.Contains(root, StringComparison.OrdinalIgnoreCase));
            Equal(false, json.Contains("segmentDirectory", StringComparison.OrdinalIgnoreCase));
            Equal(false, json.Contains("anchorDirectory", StringComparison.OrdinalIgnoreCase));
            AssertArtifactsEqual(before, CaptureArtifacts());

            ExpectRejected(() => AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                new AgentHostAuditRetentionPolicy
                {
                    OlderThanDays = 0,
                    MaximumStoreBytes = 1,
                    MinimumCompleteSessionsToRetain = 0,
                },
                utcNow));
            ExpectRejected(() => AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                new AgentHostAuditRetentionPolicy
                {
                    OlderThanDays = 1,
                    MaximumStoreBytes = 1,
                    MinimumCompleteSessionsToRetain = 0,
                },
                utcNow.ToOffset(TimeSpan.FromHours(8))));

            var oversizedPath = Path.Combine(store.SegmentDirectory, "oversized.bin");
            using (var oversized = new FileStream(
                       oversizedPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                oversized.SetLength(64L * 1024 * 1024 + 1);
            }

            try
            {
                AgentHostAuditRetentionPlanner.Create(
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    new AgentHostAuditRetentionPolicy
                    {
                        OlderThanDays = 1,
                        MaximumStoreBytes = 1,
                        MinimumCompleteSessionsToRetain = 0,
                    },
                    utcNow);
                throw new InvalidOperationException("超大审计 artifact 被错误接受。");
            }
            catch (AgentHostAuditCatalogException)
            {
            }
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;

        void CreateCompleteAudit(string sessionId)
        {
            using var audit = AgentHostAuditLog.CreateRotatingInProtectedDirectories(
                sessionId,
                store!.SegmentDirectory,
                store.AnchorDirectory,
                maximumRecords: 8,
                maximumBytes: 16 * 1024,
                maximumSegments: 2);
            audit.Record(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.BridgeConnected,
                OutcomeCode = AgentHostAuditOutcomeCodes.Connected,
            });
            audit.Complete();
        }

        string SegmentPath(string sessionId)
            => Path.Combine(store!.SegmentDirectory, sessionId + ".segment-000001.jsonl");

        string AnchorPath(string sessionId)
            => Path.Combine(store!.AnchorDirectory, sessionId + ".anchor.json");

        void SetSessionWriteTime(string sessionId, DateTimeOffset timestamp)
        {
            File.SetLastWriteTimeUtc(SegmentPath(sessionId), timestamp.UtcDateTime);
            File.SetLastWriteTimeUtc(AnchorPath(sessionId), timestamp.UtcDateTime);
        }

        static string Action(AgentHostAuditRetentionPlan plan, string sessionId)
            => plan.Entries.Single(entry => string.Equals(
                entry.SystemSessionId,
                sessionId,
                StringComparison.Ordinal)).Action;

        Dictionary<string, (long Length, long LastWriteTicks, string Hash)> CaptureArtifacts()
            => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    path => Path.GetRelativePath(root, path),
                    path =>
                    {
                        var info = new FileInfo(path);
                        return (
                            info.Length,
                            info.LastWriteTimeUtc.Ticks,
                            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                    },
                    StringComparer.OrdinalIgnoreCase);

        static void AssertArtifactsEqual(
            IReadOnlyDictionary<string, (long Length, long LastWriteTicks, string Hash)> expected,
            IReadOnlyDictionary<string, (long Length, long LastWriteTicks, string Hash)> actual)
        {
            Equal(expected.Count, actual.Count);
            foreach (var pair in expected)
            {
                if (!actual.TryGetValue(pair.Key, out var actualValue))
                {
                    throw new InvalidOperationException(
                        "审计保留规划改变了 artifact 集合：" + pair.Key);
                }

                Equal(pair.Value, actualValue);
            }
        }

        static void ExpectRejected(Action action)
        {
            try
            {
                action();
                throw new InvalidOperationException("非法保留规划参数被错误接受。");
            }
            catch (ArgumentException)
            {
            }
        }
    }

    public static Task AuditRetentionControlStatusFailsClosedForUnknownArtifacts()
    {
        const string oldSession = "10101010101010101010101010101010";
        const string contentMarker = "retention-control-private-marker";
        var utcNow = new DateTimeOffset(2026, 7, 26, 2, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-control-" + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            CreateCompletePersistentAudit(
                oldSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            SetPersistentAuditWriteTime(store, oldSession, utcNow.AddDays(-60));
            var policy = new AgentHostAuditRetentionPolicy
            {
                OlderThanDays = 30,
                MaximumStoreBytes = 1024L * 1024 * 1024,
                MinimumCompleteSessionsToRetain = 0,
            };
            var plan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                utcNow);

            var clean = AgentHostAuditRetentionExecutor.InspectControlDirectory(
                store.ControlDirectory);
            Equal(AgentHostAuditRetentionControlStatuses.Ready, clean.Status);
            Equal(true, clean.InspectionComplete);
            Equal(false, clean.RecoveryRequired);
            Equal(false, clean.ManualReviewRequired);

            var unknownFile = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-operator-note.bin");
            var unknownDirectory = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-shadow");
            File.WriteAllText(
                unknownFile,
                "Bear" + "er " + contentMarker + @" C:\Users\operator\private\note.txt",
                new System.Text.UTF8Encoding(false));
            Directory.CreateDirectory(unknownDirectory);

            var manual = AgentHostAuditRetentionExecutor.InspectControlDirectory(
                store.ControlDirectory);
            Equal(
                AgentHostAuditRetentionControlStatuses.ManualReviewRequired,
                manual.Status);
            Equal(true, manual.InspectionComplete);
            Equal(false, manual.RecoveryRequired);
            Equal(true, manual.ManualReviewRequired);
            Equal(2, manual.ArtifactCount);
            Equal(2, manual.ManualReviewArtifactCount);
            Equal(1, manual.UnsafeArtifactCount);
            Contains(
                manual.ReasonCodes,
                AgentHostAuditRetentionControlReasonCodes.UnknownArtifact);
            Contains(
                manual.ReasonCodes,
                AgentHostAuditRetentionControlReasonCodes.UnsafeArtifact);

            var planWithControlStatus = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                utcNow,
                manual);
            Equal(plan.PlanId, planWithControlStatus.PlanId);
            Equal(
                AgentHostAuditRetentionControlStatuses.ManualReviewRequired,
                planWithControlStatus.ControlStatus.Status);
            var statusJson = JsonSerializer.Serialize(
                planWithControlStatus,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Equal(false, statusJson.Contains(root, StringComparison.OrdinalIgnoreCase));
            Equal(false, statusJson.Contains(contentMarker, StringComparison.Ordinal));
            Equal(false, statusJson.Contains("operator-note", StringComparison.Ordinal));
            Equal(false, statusJson.Contains("retention-shadow", StringComparison.Ordinal));

            ExpectRetentionRejected(
                AgentHostAuditRetentionExecutionReasonCodes.ManualReviewRequired,
                () => AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    plan.PlanId,
                    utcNow));
            Equal(true, File.Exists(unknownFile));
            Equal(true, Directory.Exists(unknownDirectory));
            Equal(true, File.Exists(RetentionSegmentPath(store, oldSession)));
            Equal(true, File.Exists(RetentionAnchorPath(store, oldSession)));
            Equal(
                0,
                Directory.EnumerateFiles(
                    store.ControlDirectory,
                    "*.journal.json",
                    SearchOption.TopDirectoryOnly).Count());
            Equal(
                0,
                Directory.EnumerateFiles(
                    store.ControlDirectory,
                    "*.receipt.json",
                    SearchOption.TopDirectoryOnly).Count());

            File.Delete(unknownFile);
            Directory.Delete(unknownDirectory);
            var recoveryTemporary = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-" + plan.PlanId + ".journal.json.tmp");
            File.WriteAllText(
                recoveryTemporary,
                "{}",
                new System.Text.UTF8Encoding(false));
            var recovery = AgentHostAuditRetentionExecutor.InspectControlDirectory(
                store.ControlDirectory);
            Equal(
                AgentHostAuditRetentionControlStatuses.RecoveryRequired,
                recovery.Status);
            Equal(true, recovery.RecoveryRequired);
            Equal(false, recovery.ManualReviewRequired);
            Equal(1, recovery.RecoveryArtifactCount);
            Equal(plan.PlanId, recovery.RecoveryPlanIds.Single());
            Contains(
                recovery.ReasonCodes,
                AgentHostAuditRetentionControlReasonCodes.PendingRecovery);

            File.Delete(recoveryTemporary);
            var invalidReceipt = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-" + plan.PlanId + ".receipt.json");
            File.WriteAllText(
                invalidReceipt,
                "{}",
                new System.Text.UTF8Encoding(false));
            var invalid = AgentHostAuditRetentionExecutor.InspectControlDirectory(
                store.ControlDirectory);
            Equal(
                AgentHostAuditRetentionControlStatuses.ManualReviewRequired,
                invalid.Status);
            Equal(1, invalid.InvalidArtifactCount);
            Contains(
                invalid.ReasonCodes,
                AgentHostAuditRetentionControlReasonCodes.InvalidArtifact);
            Equal(true, File.Exists(invalidReceipt));
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static async Task AuditRetentionPlanCliRejectsInvalidArguments()
    {
        Equal(true, AgentHostProgram.TryParseAuditRetentionPolicy(
            [
                "audit-retention-plan",
                "--retain-complete", "7",
                "--older-than-days", "90",
                "--max-store-mib", "512",
            ],
            out var parsedPolicy));
        Equal(90, parsedPolicy.OlderThanDays);
        Equal(512L * 1024 * 1024, parsedPolicy.MaximumStoreBytes);
        Equal(7, parsedPolicy.MinimumCompleteSessionsToRetain);
        Equal(false, AgentHostProgram.TryParseAuditRetentionPolicy(
            [
                "audit-retention-plan",
                "--older-than-days", "90",
                "--older-than-days", "91",
                "--retain-complete", "0",
            ],
            out _));
        Equal(false, AgentHostProgram.TryParseAuditRetentionPolicy(
            [
                "audit-retention-plan",
                "--Older-Than-Days", "90",
                "--max-store-mib", "512",
                "--retain-complete", "0",
            ],
            out _));

        var planId = new string('a', 64);
        Equal(true, AgentHostProgram.TryParseAuditRetentionApplyArguments(
            [
                "audit-retention-apply",
                "--retain-complete", "7",
                "--plan", planId,
                "--older-than-days", "90",
                "--max-store-mib", "512",
            ],
            out var applyPolicy,
            out var parsedPlanId));
        Equal(planId, parsedPlanId);
        Equal(90, applyPolicy.OlderThanDays);
        Equal(false, AgentHostProgram.TryParseAuditRetentionApplyArguments(
            [
                "audit-retention-apply",
                "--plan", planId.ToUpperInvariant(),
                "--older-than-days", "90",
                "--max-store-mib", "512",
                "--retain-complete", "7",
            ],
            out _,
            out _));

        var originalError = Console.Error;
        using var capturedError = new StringWriter();
        try
        {
            Console.SetError(capturedError);
            var exitCode = await AgentHostProgram.RunAsync(
                [
                    "audit-retention-plan",
                    "--older-than-days", "0",
                    "--max-store-mib", "1",
                    "--retain-complete", "0",
                ]);
            Equal(2, exitCode);
            Equal(
                "audit-retention-plan: invalid_arguments",
                capturedError.ToString().Trim());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    public static Task AuditRetentionApplyDeletesOnlyApprovedAndIsIdempotent()
    {
        const string oldSession = "77777777777777777777777777777777";
        const string minimumSession = "88888888888888888888888888888888";
        var utcNow = new DateTimeOffset(2026, 7, 25, 1, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-apply-" + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            CreateCompletePersistentAudit(
                oldSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            CreateCompletePersistentAudit(
                minimumSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            SetPersistentAuditWriteTime(store, oldSession, utcNow.AddDays(-60));
            SetPersistentAuditWriteTime(store, minimumSession, utcNow.AddDays(-1));
            var policy = new AgentHostAuditRetentionPolicy
            {
                OlderThanDays = 30,
                MaximumStoreBytes = 1024L * 1024 * 1024,
                MinimumCompleteSessionsToRetain = 1,
            };
            var plan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                utcNow);
            Equal(true, IsLowerHex(plan.PlanId, 64));
            Equal(
                plan.PlanId,
                AgentHostAuditRetentionPlanner.Create(
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    utcNow.AddMinutes(1)).PlanId);

            var result = AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                plan.PlanId,
                utcNow);
            Equal(AgentHostAuditRetentionApplyStatuses.Applied, result.Status);
            Equal(1, result.DeletedSessionCount);
            Equal(2, result.DeletedArtifactCount);
            Equal(false, File.Exists(RetentionSegmentPath(store, oldSession)));
            Equal(false, File.Exists(RetentionAnchorPath(store, oldSession)));
            Equal(true, File.Exists(RetentionSegmentPath(store, minimumSession)));
            Equal(true, File.Exists(RetentionAnchorPath(store, minimumSession)));

            var repeated = AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                plan.PlanId,
                utcNow.AddMinutes(2));
            Equal(AgentHostAuditRetentionApplyStatuses.AlreadyApplied, repeated.Status);
            Equal(result.DeletedBytes, repeated.DeletedBytes);
            var receiptPath = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-" + plan.PlanId + ".receipt.json");
            Equal(true, File.Exists(receiptPath));
            var receiptText = File.ReadAllText(receiptPath);
            Equal(false, receiptText.Contains(root, StringComparison.OrdinalIgnoreCase));
            Equal(false, receiptText.Contains("segments", StringComparison.OrdinalIgnoreCase));
            Equal(false, receiptText.Contains("anchors", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static async Task UnknownCommandDiagnosticIsSanitized()
    {
        var tokenMarker = "agenthost-cli-secret-marker";
        var unsafeCommand = string.Join(
            " ",
            "Bear" + "er " + tokenMarker,
            @"C:\Users\cli-user\private\command.json",
            "https://cli-user@example.invalid/?api_"
                + "key=agenthost-cli-query-marker",
            @"CONTOSO\cli-user");
        var originalOutput = Console.Out;
        using var capturedOutput = new StringWriter();
        try
        {
            Console.SetOut(capturedOutput);
            var exitCode = await AgentHostProgram.RunAsync([unsafeCommand]);
            Equal(2, exitCode);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        var output = capturedOutput.ToString().Trim();
        foreach (var marker in new[]
                 {
                     tokenMarker,
                     "agenthost-cli-query-marker",
                     "cli-user",
                     "example.invalid",
                 })
        {
            Equal(
                true,
                output.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0);
        }

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Equal("unknown_command", root.GetProperty("error").GetString());
        Equal(
            DiagnosticDataClassification.Configuration.ToString(),
            root.GetProperty("diagnosticClassification").GetString());
        var redactions = (DiagnosticRedactionKinds)root
            .GetProperty("diagnosticRedactions")
            .GetInt32();
        Equal(true, (redactions & DiagnosticRedactionKinds.Token) != 0);
        Equal(true, (redactions & DiagnosticRedactionKinds.Path) != 0);
        Equal(true, (redactions & DiagnosticRedactionKinds.Uri) != 0);
        Equal(true, (redactions & DiagnosticRedactionKinds.Identity) != 0);
        var safeCommand = root.GetProperty("command").GetString() ?? string.Empty;
        Equal(true, safeCommand.Length <= DiagnosticSanitizer.MaximumOutputCharacters);
    }

    public static Task AuditCliUnexpectedFailureIsStructuredAndSanitized()
    {
        const string messageMarker = "audit-cli-message-secret-marker";
        const string innerMarker = "audit-cli-inner-secret-marker";
        const string dataMarker = "audit-cli-data-secret-marker";
        var failure = new InvalidOperationException(
            "Bear" + "er " + messageMarker
                + @" C:\Users\audit-cli-user\private\audit.json"
                + " https://audit-cli-user@example.invalid/?api_key=audit-query-secret",
            new IOException(innerMarker + @" C:\Users\audit-inner-user\private\inner.log"));
        failure.Data["credential"] = dataMarker;
        var originalError = Console.Error;
        using var capturedError = new StringWriter();
        try
        {
            Console.SetError(capturedError);
            var exitCode = AgentHostProgram.RunAuditCliCommand(
                "audit-export",
                () => throw failure);
            Equal(1, exitCode);
        }
        finally
        {
            Console.SetError(originalError);
        }

        var output = capturedError.ToString().Trim();
        foreach (var marker in new[]
                 {
                     messageMarker,
                     innerMarker,
                     dataMarker,
                     "audit-query-secret",
                     "audit-cli-user",
                     "audit-inner-user",
                     "example.invalid",
                     nameof(InvalidOperationException),
                     nameof(IOException),
                 })
        {
            Equal(
                true,
                output.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0);
        }

        Equal(
            true,
            output.StartsWith(
                "audit-export: agenthost_audit_failure;",
                StringComparison.Ordinal));
        Equal(
            true,
            output.Contains(
                "errorCode=audit_export_failed",
                StringComparison.Ordinal));
        Equal(
            true,
            output.Contains(
                "errorStage=agenthost_audit",
                StringComparison.Ordinal));
        Equal(
            true,
            output.Contains(
                "diagnosticClassification=Exception",
                StringComparison.Ordinal));
        Equal(
            true,
            output.Contains(
                "diagnosticRedactions=",
                StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public static async Task AgentHostCliFailureIsStructuredAndSanitized()
    {
        var originalOutput = Console.Out;
        using var capturedOutput = new StringWriter();
        try
        {
            Console.SetOut(capturedOutput);
            var exitCode = await AgentHostProgram.RunAsync(
                ["doctor", "--workspace", "relative-workspace"]);
            Equal(1, exitCode);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        var output = capturedOutput.ToString().Trim();
        Equal(
            true,
            output.IndexOf(
                nameof(ArgumentException),
                StringComparison.OrdinalIgnoreCase) < 0);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Equal(false, root.GetProperty("ok").GetBoolean());
        Equal("doctor", root.GetProperty("command").GetString());
        Equal("agenthost_cli_failure", root.GetProperty("error").GetString());
        Equal("agenthost_internal_error", root.GetProperty("errorCode").GetString());
        Equal("agenthost_cli", root.GetProperty("errorStage").GetString());
        Equal(
            DiagnosticDataClassification.Configuration.ToString(),
            root.GetProperty("diagnosticClassification").GetString());
        Equal(
            JsonValueKind.Number,
            root.GetProperty("diagnosticRedactions").ValueKind);
    }

    public static Task ProtocolFaultStandardErrorIsStructuredAndSanitized()
    {
        const string marker = "agenthost-protocol-fault-secret-marker";
        var fault = new AppServerProtocolFaultEventArgs(
            new InvalidOperationException(
                "Bear" + "er " + marker + @" C:\Users\protocol-user\fault.log"));

        var output = AgentHostProgram.FormatProtocolFaultForStandardError(fault);

        Equal(
            true,
            output.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0);
        Equal(
            true,
            output.IndexOf(
                nameof(InvalidOperationException),
                StringComparison.OrdinalIgnoreCase) < 0);
        Equal(
            true,
            output.StartsWith(
                "protocol: appserver_protocol_fault;",
                StringComparison.Ordinal));
        Equal(
            true,
            output.Contains(
                "diagnosticClassification="
                    + fault.DiagnosticClassification,
                StringComparison.Ordinal));
        Equal(
            true,
            output.Contains(
                "diagnosticRedactions="
                    + (int)fault.DiagnosticRedactions,
                StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public static async Task BootstrapCliFailureIsStructuredAndSanitized()
    {
        foreach (var command in new[] { "bootstrap-doctor", "bootstrap-serve" })
        {
            var originalError = Console.Error;
            using var capturedError = new StringWriter();
            try
            {
                Console.SetError(capturedError);
                var exitCode = await AgentHostProgram.RunAsync([command, "unexpected"]);
                Equal(1, exitCode);
            }
            finally
            {
                Console.SetError(originalError);
            }

            var output = capturedError.ToString().Trim();
            Equal(
                true,
                output.IndexOf(
                    nameof(ArgumentException),
                    StringComparison.OrdinalIgnoreCase) < 0);
            Equal(
                true,
                output.StartsWith(
                    command + ": agenthost_bootstrap_failure;",
                    StringComparison.Ordinal));
            Equal(
                true,
                output.Contains(
                    "errorCode=invalid_arguments",
                    StringComparison.Ordinal));
            Equal(
                true,
                output.Contains(
                    "errorStage=agenthost_bootstrap",
                    StringComparison.Ordinal));
            Equal(
                true,
                output.Contains(
                    "diagnosticClassification=Configuration",
                    StringComparison.Ordinal));
            Equal(
                true,
                output.Contains(
                    "diagnosticRedactions=0",
                    StringComparison.Ordinal));
        }
    }

    public static Task DoctorStatusOmitsRawEnvironmentFingerprint()
    {
        const string marker = "doctor-environment-secret-marker";
        var status = AgentHostPublicStatus.CreateDoctor(
            AppServerClientState.Running,
            CodexExecutableSource.CommandLine,
            new CodexVersionPreflightResult(
                new CodexSemanticVersion(0, 144, 4),
                CodexVersionCompatibility.Default),
            new AppServerInitializeResponse(
                @"C:\Users\" + marker + @"\.codex",
                "windows",
                "windows",
                "Bearer " + marker + " codex/0.144.4 (Windows 10.0.19045; x86_64)"));
        var json = JsonSerializer.Serialize(
            status,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

        Equal(
            true,
            json.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Equal(true, root.GetProperty("ok").GetBoolean());
        Equal("Running", root.GetProperty("state").GetString());
        Equal(true, root.GetProperty("workspaceReady").GetBoolean());
        Equal("CommandLine", root.GetProperty("codexExecutableSource").GetString());
        Equal("0.144.4", root.GetProperty("codexVersion").GetString());
        Equal(true, root.GetProperty("codexHomeConfigured").GetBoolean());
        Equal(true, root.GetProperty("platformCompatible").GetBoolean());
        Equal(false, root.TryGetProperty("codexHome", out _));
        Equal(false, root.TryGetProperty("platformFamily", out _));
        Equal(false, root.TryGetProperty("platformOs", out _));
        Equal(false, root.TryGetProperty("userAgent", out _));
        return Task.CompletedTask;
    }

    public static Task AuditRetentionReceiptsConvergeToBoundedCheckpoint()
    {
        const int maximumRetainedReceipts = 256;
        var utcNow = new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-receipt-convergence-"
            + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            AgentHostAuditRetentionPolicy? firstPolicy = null;
            AgentHostAuditRetentionPlan? firstPlan = null;
            AgentHostAuditRetentionPolicy? latestPolicy = null;
            AgentHostAuditRetentionPlan? latestPlan = null;
            for (var index = 0; index <= maximumRetainedReceipts; index++)
            {
                var policy = new AgentHostAuditRetentionPolicy
                {
                    OlderThanDays = index + 1,
                    MaximumStoreBytes = 1024L * 1024,
                    MinimumCompleteSessionsToRetain = 0,
                };
                var operationTime = utcNow.AddMinutes(index);
                var plan = AgentHostAuditRetentionPlanner.Create(
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    operationTime);
                var result = AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    plan.PlanId,
                    operationTime);
                Equal(AgentHostAuditRetentionApplyStatuses.NoCandidates, result.Status);
                firstPolicy ??= policy;
                firstPlan ??= plan;
                latestPolicy = policy;
                latestPlan = plan;
            }

            Equal(
                maximumRetainedReceipts,
                Directory.EnumerateFiles(
                    store.ControlDirectory,
                    "*.receipt.json",
                    SearchOption.TopDirectoryOnly).Count());
            Equal(
                true,
                File.Exists(Path.Combine(
                    store.ControlDirectory,
                    ".audit-retention-receipts.checkpoint.json")));
            Equal(
                false,
                File.Exists(Path.Combine(
                    store.ControlDirectory,
                    ".audit-retention-" + firstPlan!.PlanId + ".receipt.json")));
            Equal(
                true,
                File.Exists(Path.Combine(
                    store.ControlDirectory,
                    ".audit-retention-" + latestPlan!.PlanId + ".receipt.json")));

            var repeatedLatest = AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                latestPolicy!,
                latestPlan.PlanId,
                utcNow.AddHours(5));
            Equal(
                AgentHostAuditRetentionApplyStatuses.AlreadyApplied,
                repeatedLatest.Status);
            var repeatedCompactedPlan = AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                firstPolicy!,
                firstPlan.PlanId,
                utcNow.AddHours(6));
            Equal(
                AgentHostAuditRetentionApplyStatuses.NoCandidates,
                repeatedCompactedPlan.Status);
            Equal(
                maximumRetainedReceipts,
                Directory.EnumerateFiles(
                    store.ControlDirectory,
                    "*.receipt.json",
                    SearchOption.TopDirectoryOnly).Count());
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static Task AuditRetentionReceiptCheckpointRecoveryDoesNotDoubleCount()
    {
        const int maximumRetainedReceipts = 256;
        var utcNow = new DateTimeOffset(2026, 7, 25, 14, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-receipt-checkpoint-recovery-"
            + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        byte[]? oldestReceiptBytes = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            string? oldestReceiptPath = null;
            for (var index = 0; index < maximumRetainedReceipts; index++)
            {
                var policy = new AgentHostAuditRetentionPolicy
                {
                    OlderThanDays = index + 1,
                    MaximumStoreBytes = 2L * 1024 * 1024,
                    MinimumCompleteSessionsToRetain = 0,
                };
                var operationTime = utcNow.AddMinutes(index);
                var plan = AgentHostAuditRetentionPlanner.Create(
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    operationTime);
                AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    plan.PlanId,
                    operationTime);
                if (index == 0)
                {
                    oldestReceiptPath = Path.Combine(
                        store.ControlDirectory,
                        ".audit-retention-" + plan.PlanId + ".receipt.json");
                }
            }

            oldestReceiptBytes = File.ReadAllBytes(oldestReceiptPath!);
            var latestPolicy = new AgentHostAuditRetentionPolicy
            {
                OlderThanDays = maximumRetainedReceipts + 1,
                MaximumStoreBytes = 2L * 1024 * 1024,
                MinimumCompleteSessionsToRetain = 0,
            };
            var latestTime = utcNow.AddMinutes(maximumRetainedReceipts);
            var latestPlan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                latestPolicy,
                latestTime);
            AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                latestPolicy,
                latestPlan.PlanId,
                latestTime);

            var checkpointPath = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-receipts.checkpoint.json");
            Equal(1L, ReadCompactedReceiptCount(checkpointPath));
            Equal(false, File.Exists(oldestReceiptPath));

            File.WriteAllBytes(oldestReceiptPath!, oldestReceiptBytes);
            var recovered = AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                latestPolicy,
                latestPlan.PlanId,
                latestTime.AddMinutes(1));
            Equal(AgentHostAuditRetentionApplyStatuses.AlreadyApplied, recovered.Status);
            Equal(1L, ReadCompactedReceiptCount(checkpointPath));
            Equal(false, File.Exists(oldestReceiptPath));
            Equal(
                maximumRetainedReceipts,
                Directory.EnumerateFiles(
                    store.ControlDirectory,
                    "*.receipt.json",
                    SearchOption.TopDirectoryOnly).Count());
        }
        finally
        {
            if (oldestReceiptBytes != null)
            {
                Array.Clear(oldestReceiptBytes, 0, oldestReceiptBytes.Length);
            }

            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static Task AuditRetentionReceiptCheckpointCommitsBeforeDeletion()
    {
        const int maximumRetainedReceipts = 256;
        var utcNow = new DateTimeOffset(2026, 7, 25, 22, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-receipt-checkpoint-order-"
            + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            string? oldestReceiptPath = null;
            for (var index = 0; index < maximumRetainedReceipts; index++)
            {
                var policy = new AgentHostAuditRetentionPolicy
                {
                    OlderThanDays = index + 1,
                    MaximumStoreBytes = 4L * 1024 * 1024,
                    MinimumCompleteSessionsToRetain = 0,
                };
                var operationTime = utcNow.AddMinutes(index);
                var plan = AgentHostAuditRetentionPlanner.Create(
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    operationTime);
                AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    plan.PlanId,
                    operationTime);
                if (index == 0)
                {
                    oldestReceiptPath = Path.Combine(
                        store.ControlDirectory,
                        ".audit-retention-" + plan.PlanId + ".receipt.json");
                }
            }

            var latestPolicy = new AgentHostAuditRetentionPolicy
            {
                OlderThanDays = maximumRetainedReceipts + 1,
                MaximumStoreBytes = 4L * 1024 * 1024,
                MinimumCompleteSessionsToRetain = 0,
            };
            var latestTime = utcNow.AddMinutes(maximumRetainedReceipts);
            var latestPlan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                latestPolicy,
                latestTime);
            var interrupted = false;
            try
            {
                AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    latestPolicy,
                    latestPlan.PlanId,
                    latestTime,
                    new AuditRetentionFailAfterReceiptCheckpointInjector());
            }
            catch (AgentHostAuditRetentionExecutionException exception)
            {
                Equal(
                    AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                    exception.ReasonCode);
                interrupted = true;
            }

            Equal(true, interrupted);
            var checkpointPath = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-receipts.checkpoint.json");
            Equal(true, File.Exists(checkpointPath));
            Equal(1L, ReadCompactedReceiptCount(checkpointPath));
            Equal(true, File.Exists(oldestReceiptPath));
            Equal(
                maximumRetainedReceipts + 1,
                Directory.EnumerateFiles(
                    store.ControlDirectory,
                    "*.receipt.json",
                    SearchOption.TopDirectoryOnly).Count());

            var recovered = AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                latestPolicy,
                latestPlan.PlanId,
                latestTime.AddMinutes(1));
            Equal(AgentHostAuditRetentionApplyStatuses.AlreadyApplied, recovered.Status);
            Equal(1L, ReadCompactedReceiptCount(checkpointPath));
            Equal(false, File.Exists(oldestReceiptPath));
            Equal(
                maximumRetainedReceipts,
                Directory.EnumerateFiles(
                    store.ControlDirectory,
                    "*.receipt.json",
                    SearchOption.TopDirectoryOnly).Count());
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static Task AuditRetentionRemovesRedundantForeignReceiptTemporaryFile()
    {
        var utcNow = new DateTimeOffset(2026, 7, 25, 20, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-receipt-temp-convergence-"
            + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            var firstPolicy = new AgentHostAuditRetentionPolicy
            {
                OlderThanDays = 30,
                MaximumStoreBytes = 3L * 1024 * 1024,
                MinimumCompleteSessionsToRetain = 0,
            };
            var firstPlan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                firstPolicy,
                utcNow);
            AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                firstPolicy,
                firstPlan.PlanId,
                utcNow);
            var redundantTemporaryPath = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-"
                + firstPlan.PlanId
                + ".receipt.json.tmp");
            File.WriteAllText(redundantTemporaryPath, "interrupted-temp");

            var secondPolicy = new AgentHostAuditRetentionPolicy
            {
                OlderThanDays = 31,
                MaximumStoreBytes = 3L * 1024 * 1024,
                MinimumCompleteSessionsToRetain = 0,
            };
            var secondPlan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                secondPolicy,
                utcNow.AddMinutes(1));
            var result = AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                secondPolicy,
                secondPlan.PlanId,
                utcNow.AddMinutes(1));
            Equal(AgentHostAuditRetentionApplyStatuses.NoCandidates, result.Status);
            Equal(false, File.Exists(redundantTemporaryPath));
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static Task AuditRetentionApplyRecoversInterruptedJournal()
    {
        const string oldSession = "99999999999999999999999999999999";
        const string minimumSession = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var utcNow = new DateTimeOffset(2026, 7, 25, 2, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-recover-" + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            CreateCompletePersistentAudit(
                oldSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            CreateCompletePersistentAudit(
                minimumSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            SetPersistentAuditWriteTime(store, oldSession, utcNow.AddDays(-60));
            SetPersistentAuditWriteTime(store, minimumSession, utcNow.AddDays(-1));
            var policy = CreateRetentionPolicy();
            var plan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                utcNow);
            try
            {
                AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    plan.PlanId,
                    utcNow,
                    new AuditRetentionFailAfterDeleteInjector(1));
                throw new InvalidOperationException("审计清理中断注入未生效。");
            }
            catch (AgentHostAuditRetentionExecutionException exception)
            {
                Equal(
                    AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                    exception.ReasonCode);
            }

            var journalPath = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-" + plan.PlanId + ".journal.json");
            Equal(true, File.Exists(journalPath));
            Equal(false, File.Exists(RetentionAnchorPath(store, oldSession)));
            Equal(true, File.Exists(RetentionSegmentPath(store, oldSession)));

            var recovered = AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                plan.PlanId,
                utcNow.AddMinutes(1));
            Equal(AgentHostAuditRetentionApplyStatuses.Recovered, recovered.Status);
            Equal(false, File.Exists(journalPath));
            Equal(false, File.Exists(RetentionSegmentPath(store, oldSession)));
            Equal(true, File.Exists(RetentionSegmentPath(store, minimumSession)));

            var repeated = AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                plan.PlanId,
                utcNow.AddMinutes(2));
            Equal(AgentHostAuditRetentionApplyStatuses.AlreadyApplied, repeated.Status);
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;

        static AgentHostAuditRetentionPolicy CreateRetentionPolicy()
            => new()
            {
                OlderThanDays = 30,
                MaximumStoreBytes = 1024L * 1024 * 1024,
                MinimumCompleteSessionsToRetain = 1,
            };
    }

    public static Task AuditRetentionPersistenceIoFailuresConvergeOnce()
    {
        AssertConverges(
            SyntheticAuditRetentionIoFailurePoint.JournalPrepared,
            expectOldArtifactsAfterFailure: true,
            expectJournalAfterFailure: false,
            AgentHostAuditRetentionApplyStatuses.Applied);
        AssertConverges(
            SyntheticAuditRetentionIoFailurePoint.JournalCommitted,
            expectOldArtifactsAfterFailure: true,
            expectJournalAfterFailure: true,
            AgentHostAuditRetentionApplyStatuses.Recovered);
        AssertConverges(
            SyntheticAuditRetentionIoFailurePoint.ReceiptPrepared,
            expectOldArtifactsAfterFailure: false,
            expectJournalAfterFailure: true,
            AgentHostAuditRetentionApplyStatuses.Recovered);
        return Task.CompletedTask;

        static void AssertConverges(
            SyntheticAuditRetentionIoFailurePoint failurePoint,
            bool expectOldArtifactsAfterFailure,
            bool expectJournalAfterFailure,
            string expectedRecoveryStatus)
        {
            const string oldSession = "9182736455aa44bb9182736455aa44bb";
            const string minimumSession = "a182736455aa44bba182736455aa44bb";
            const string privateMarker = "synthetic-retention-io-private-marker";
            var utcNow = new DateTimeOffset(2026, 7, 26, 2, 0, 0, TimeSpan.Zero);
            var root = Path.Combine(
                Path.GetTempPath(),
                "codex-autocad-audit-retention-io-"
                + failurePoint.ToString().ToLowerInvariant()
                + "-"
                + Guid.NewGuid().ToString("N"));
            AgentPersistentAuditStoreLease? store = null;
            try
            {
                store = AgentPersistentAuditStoreLease.Create(root);
                CreateCompletePersistentAudit(
                    oldSession,
                    store.SegmentDirectory,
                    store.AnchorDirectory);
                CreateCompletePersistentAudit(
                    minimumSession,
                    store.SegmentDirectory,
                    store.AnchorDirectory);
                SetPersistentAuditWriteTime(store, oldSession, utcNow.AddDays(-60));
                SetPersistentAuditWriteTime(store, minimumSession, utcNow.AddDays(-1));
                var policy = new AgentHostAuditRetentionPolicy
                {
                    OlderThanDays = 30,
                    MaximumStoreBytes = 1024L * 1024 * 1024,
                    MinimumCompleteSessionsToRetain = 1,
                };
                var plan = AgentHostAuditRetentionPlanner.Create(
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    utcNow);
                AgentHostAuditRetentionExecutionException? failure = null;
                try
                {
                    AgentHostAuditRetentionExecutor.Apply(
                        store.Root,
                        store.ControlDirectory,
                        store.SegmentDirectory,
                        store.AnchorDirectory,
                        policy,
                        plan.PlanId,
                        utcNow,
                        new SyntheticAuditRetentionIoFailureInjector(
                            failurePoint,
                            privateMarker));
                }
                catch (AgentHostAuditRetentionExecutionException exception)
                {
                    failure = exception;
                }

                if (failure is null)
                {
                    throw new InvalidOperationException("受控审计保留I/O故障未触发。");
                }

                Equal(
                    AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                    failure.ReasonCode);
                Equal(true, failure.InnerException is IOException);
                var publicFailure = AgentHostProgram.FormatAuditFailureForStandardError(
                    "audit-retention-apply",
                    failure);
                Contains(publicFailure, "errorCode=audit_retention_failed");
                Contains(publicFailure, "errorStage=agenthost_audit");
                Contains(publicFailure, "diagnosticClassification=Environment");
                Equal(
                    true,
                    publicFailure.IndexOf(privateMarker, StringComparison.OrdinalIgnoreCase) < 0);
                Equal(
                    true,
                    publicFailure.IndexOf(root, StringComparison.OrdinalIgnoreCase) < 0);

                Equal(
                    expectOldArtifactsAfterFailure,
                    File.Exists(RetentionSegmentPath(store, oldSession)));
                Equal(
                    expectOldArtifactsAfterFailure,
                    File.Exists(RetentionAnchorPath(store, oldSession)));
                Equal(
                    expectJournalAfterFailure,
                    Directory.EnumerateFiles(
                        store.ControlDirectory,
                        "*.journal.json",
                        SearchOption.TopDirectoryOnly).Any());
                var controlStatus = AgentHostAuditRetentionExecutor.InspectControlDirectory(
                    store.ControlDirectory);
                Equal(
                    AgentHostAuditRetentionControlStatuses.RecoveryRequired,
                    controlStatus.Status);
                Contains(
                    controlStatus.ReasonCodes,
                    AgentHostAuditRetentionControlReasonCodes.PendingRecovery);

                var recovered = AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    plan.PlanId,
                    utcNow.AddMinutes(1));
                Equal(expectedRecoveryStatus, recovered.Status);
                Equal(1, recovered.DeletedSessionCount);
                Equal(2, recovered.DeletedArtifactCount);
                Equal(false, File.Exists(RetentionSegmentPath(store, oldSession)));
                Equal(false, File.Exists(RetentionAnchorPath(store, oldSession)));
                Equal(true, File.Exists(RetentionSegmentPath(store, minimumSession)));
                Equal(true, File.Exists(RetentionAnchorPath(store, minimumSession)));

                var repeated = AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    plan.PlanId,
                    utcNow.AddMinutes(2));
                Equal(AgentHostAuditRetentionApplyStatuses.AlreadyApplied, repeated.Status);
                Equal(recovered.DeletedSessionCount, repeated.DeletedSessionCount);
                Equal(recovered.DeletedArtifactCount, repeated.DeletedArtifactCount);
                Equal(recovered.DeletedBytes, repeated.DeletedBytes);
                Equal(
                    0,
                    Directory.EnumerateFiles(
                        store.ControlDirectory,
                        "*.journal.json",
                        SearchOption.TopDirectoryOnly).Count());
                Equal(
                    0,
                    Directory.EnumerateFiles(
                        store.ControlDirectory,
                        "*.tmp",
                        SearchOption.TopDirectoryOnly).Count());
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }

    public static Task AuditRetentionApplyRejectsChangedPlanAndTamperedRecovery()
    {
        const string oldSession = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string minimumSession = "cccccccccccccccccccccccccccccccc";
        var utcNow = new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-tamper-" + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            CreateCompletePersistentAudit(
                oldSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            CreateCompletePersistentAudit(
                minimumSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            SetPersistentAuditWriteTime(store, oldSession, utcNow.AddDays(-60));
            SetPersistentAuditWriteTime(store, minimumSession, utcNow.AddDays(-1));
            var policy = new AgentHostAuditRetentionPolicy
            {
                OlderThanDays = 30,
                MaximumStoreBytes = 1024L * 1024 * 1024,
                MinimumCompleteSessionsToRetain = 1,
            };
            var stalePlan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                utcNow);
            var foreignTemporaryJournal = Path.Combine(
                store.ControlDirectory,
                ".audit-retention-"
                + new string('f', 64)
                + ".journal.json.tmp");
            File.WriteAllText(
                foreignTemporaryJournal,
                "{}",
                new System.Text.UTF8Encoding(false));
            ExpectRetentionRejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalConflict,
                () => AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    stalePlan.PlanId,
                    utcNow));
            File.Delete(foreignTemporaryJournal);

            var ignored = Path.Combine(store.SegmentDirectory, "changed-after-plan.bin");
            File.WriteAllBytes(ignored, new byte[11]);
            ExpectRetentionRejected(
                AgentHostAuditRetentionExecutionReasonCodes.PlanChanged,
                () => AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    stalePlan.PlanId,
                    utcNow));
            Equal(true, File.Exists(RetentionSegmentPath(store, oldSession)));
            Equal(0, Directory.EnumerateFiles(
                store.ControlDirectory,
                "*.journal.json",
                SearchOption.TopDirectoryOnly).Count());

            File.Delete(ignored);
            var currentPlan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                utcNow);
            try
            {
                AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    currentPlan.PlanId,
                    utcNow,
                    new AuditRetentionFailAfterDeleteInjector(1));
                throw new InvalidOperationException("审计清理篡改夹具未进入恢复状态。");
            }
            catch (AgentHostAuditRetentionExecutionException exception)
            {
                Equal(
                    AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                    exception.ReasonCode);
            }

            var recoveryJournalPath = Directory.EnumerateFiles(
                    store.ControlDirectory,
                    "*.journal.json",
                    SearchOption.TopDirectoryOnly)
                .Single();
            var originalJournal = File.ReadAllText(recoveryJournalPath);
            var invalidJournal = originalJournal.Replace(
                "\"segmentCount\":1",
                "\"segmentCount\":2",
                StringComparison.Ordinal);
            if (string.Equals(originalJournal, invalidJournal, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("清理日志段数夹具未命中。");
            }

            File.WriteAllText(
                recoveryJournalPath,
                invalidJournal,
                new System.Text.UTF8Encoding(false));
            ExpectRetentionRejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                () => AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    currentPlan.PlanId,
                    utcNow.AddSeconds(30)));
            File.WriteAllText(
                recoveryJournalPath,
                originalJournal,
                new System.Text.UTF8Encoding(false));

            File.AppendAllText(
                RetentionSegmentPath(store, oldSession),
                "tampered\n",
                new System.Text.UTF8Encoding(false));
            ExpectRetentionRejected(
                AgentHostAuditRetentionExecutionReasonCodes.ArtifactChanged,
                () => AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    currentPlan.PlanId,
                    utcNow.AddMinutes(1)));
            Equal(true, File.Exists(RetentionSegmentPath(store, oldSession)));
            Equal(true, Directory.EnumerateFiles(
                store.ControlDirectory,
                "*.journal.json",
                SearchOption.TopDirectoryOnly).Any());
            Equal(true, File.Exists(RetentionSegmentPath(store, minimumSession)));
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static async Task AuditRetentionApplySerializesConcurrentExecutors()
    {
        const string oldSession = "dddddddddddddddddddddddddddddddd";
        const string minimumSession = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var utcNow = new DateTimeOffset(2026, 7, 25, 4, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-concurrent-" + Guid.NewGuid().ToString("N"));
        AgentPersistentAuditStoreLease? store = null;
        var injector = new AuditRetentionBlockingInjector();
        try
        {
            store = AgentPersistentAuditStoreLease.Create(root);
            CreateCompletePersistentAudit(
                oldSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            CreateCompletePersistentAudit(
                minimumSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            SetPersistentAuditWriteTime(store, oldSession, utcNow.AddDays(-60));
            SetPersistentAuditWriteTime(store, minimumSession, utcNow.AddDays(-1));
            var policy = new AgentHostAuditRetentionPolicy
            {
                OlderThanDays = 30,
                MaximumStoreBytes = 1024L * 1024 * 1024,
                MinimumCompleteSessionsToRetain = 1,
            };
            var plan = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                utcNow);
            var first = Task.Run(() => AgentHostAuditRetentionExecutor.Apply(
                store.Root,
                store.ControlDirectory,
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                plan.PlanId,
                utcNow,
                injector));
            if (!injector.JournalCommitted.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("并发清理夹具未进入已提交日志状态。");
            }

            ExpectRetentionRejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupBusy,
                () => AgentHostAuditRetentionExecutor.Apply(
                    store.Root,
                    store.ControlDirectory,
                    store.SegmentDirectory,
                    store.AnchorDirectory,
                    policy,
                    plan.PlanId,
                    utcNow));
            injector.Release.Set();
            var completed = await first.WaitAsync(TimeSpan.FromSeconds(5));
            Equal(AgentHostAuditRetentionApplyStatuses.Applied, completed.Status);
        }
        finally
        {
            injector.Release.Set();
            injector.Dispose();
            store?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    public static int RunAuditRetentionCrashWorker(string[] args)
    {
        if (args.Length != 7
            || !int.TryParse(
                args[3],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var olderThanDays)
            || !long.TryParse(
                args[4],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var maximumStoreBytes)
            || !int.TryParse(
                args[5],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var retainComplete))
        {
            return 2;
        }

        using var store = AgentPersistentAuditStoreLease.Create(args[1]);
        var result = AgentHostAuditRetentionExecutor.Apply(
            store.Root,
            store.ControlDirectory,
            store.SegmentDirectory,
            store.AnchorDirectory,
            new AgentHostAuditRetentionPolicy
            {
                OlderThanDays = olderThanDays,
                MaximumStoreBytes = maximumStoreBytes,
                MinimumCompleteSessionsToRetain = retainComplete,
            },
            args[2],
            new DateTimeOffset(2026, 7, 25, 5, 0, 0, TimeSpan.Zero),
            new AuditRetentionProcessCrashInjector(args[6]));
        return string.Equals(
            result.Status,
            AgentHostAuditRetentionApplyStatuses.Applied,
            StringComparison.Ordinal)
            ? 0
            : 3;
    }

    public static async Task AuditRetentionApplyRecoversAfterProcessKill()
    {
        const string oldSession = "f0000000000000000000000000000001";
        const string minimumSession = "f0000000000000000000000000000002";
        var utcNow = new DateTimeOffset(2026, 7, 25, 5, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-process-kill-"
            + Guid.NewGuid().ToString("N"));
        var markerPath = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-audit-retention-process-kill-marker-"
            + Guid.NewGuid().ToString("N"));
        var policy = new AgentHostAuditRetentionPolicy
        {
            OlderThanDays = 30,
            MaximumStoreBytes = 1024L * 1024 * 1024,
            MinimumCompleteSessionsToRetain = 1,
        };
        string planId;
        using (var store = AgentPersistentAuditStoreLease.Create(root))
        {
            CreateCompletePersistentAudit(
                oldSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            CreateCompletePersistentAudit(
                minimumSession,
                store.SegmentDirectory,
                store.AnchorDirectory);
            SetPersistentAuditWriteTime(store, oldSession, utcNow.AddDays(-60));
            SetPersistentAuditWriteTime(store, minimumSession, utcNow.AddDays(-1));
            planId = AgentHostAuditRetentionPlanner.Create(
                store.SegmentDirectory,
                store.AnchorDirectory,
                policy,
                utcNow).PlanId;
        }

        Process? worker = null;
        try
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("当前规格进程路径不可用。");
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(processPath),
                    "dotnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add(
                    typeof(AgentHostBridgeSessionSpecs).Assembly.Location);
            }

            startInfo.ArgumentList.Add("audit-retention-crash-worker");
            startInfo.ArgumentList.Add(root);
            startInfo.ArgumentList.Add(planId);
            startInfo.ArgumentList.Add(policy.OlderThanDays.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(policy.MaximumStoreBytes.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(policy.MinimumCompleteSessionsToRetain.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(markerPath);
            worker = Process.Start(startInfo)
                ?? throw new InvalidOperationException("清理崩溃工作器无法启动。");

            var markerDeadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(markerPath)
                   && !worker.HasExited
                   && DateTime.UtcNow < markerDeadline)
            {
                await Task.Delay(25);
            }

            if (!File.Exists(markerPath))
            {
                var stderr = worker.HasExited
                    ? await worker.StandardError.ReadToEndAsync()
                    : string.Empty;
                throw new InvalidOperationException(
                    "清理崩溃工作器未进入首删状态：" + stderr);
            }

            worker.Kill(entireProcessTree: true);
            if (!worker.WaitForExit(5000))
            {
                throw new InvalidOperationException("清理崩溃工作器未在强杀后退出。");
            }

            using var recoveredStore = AgentPersistentAuditStoreLease.Create(root);
            Equal(true, Directory.EnumerateFiles(
                recoveredStore.ControlDirectory,
                "*.journal.json",
                SearchOption.TopDirectoryOnly).Any());
            Equal(false, File.Exists(RetentionAnchorPath(recoveredStore, oldSession)));
            Equal(true, File.Exists(RetentionSegmentPath(recoveredStore, oldSession)));
            var recovered = AgentHostAuditRetentionExecutor.Apply(
                recoveredStore.Root,
                recoveredStore.ControlDirectory,
                recoveredStore.SegmentDirectory,
                recoveredStore.AnchorDirectory,
                policy,
                planId,
                utcNow.AddMinutes(1));
            Equal(AgentHostAuditRetentionApplyStatuses.Recovered, recovered.Status);
            Equal(false, File.Exists(RetentionSegmentPath(recoveredStore, oldSession)));
            Equal(true, File.Exists(RetentionSegmentPath(recoveredStore, minimumSession)));
            Equal(0, Directory.EnumerateFiles(
                recoveredStore.ControlDirectory,
                "*.journal.json",
                SearchOption.TopDirectoryOnly).Count());
        }
        finally
        {
            if (worker is { HasExited: false })
            {
                worker.Kill(entireProcessTree: true);
                worker.WaitForExit(5000);
            }

            worker?.Dispose();
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
            using var auditStream = new SyntheticAuditWriteFailureStream();
            using var audit = new AgentHostAuditLog(
                auditStream,
                keyPair.AgentKeys.SessionId,
                leaveOpen: true,
                maximumRecords: 16,
                maximumBytes: 16 * 1024);
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
            auditStream.FailBeforeNextWrite();

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

            Equal(1, auditStream.FailedWriteCount);
            var writeCountAfterFailure = auditStream.WriteCount;
            try
            {
                audit.Fail(AgentHostAuditErrorCodes.AuditUnavailable);
            }
            catch (AgentHostAuditException)
            {
            }

            Equal(writeCountAfterFailure, auditStream.WriteCount);
            var eventTypes = System.Text.Encoding.UTF8.GetString(auditStream.ToArray())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line).RootElement
                    .GetProperty("eventType").GetString())
                .ToArray();
            Equal(2, eventTypes.Length);
            Equal(AgentHostAuditEventTypes.SessionStarted, eventTypes[0]);
            Equal(AgentHostAuditEventTypes.BridgeConnected, eventTypes[1]);
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
            audit.Complete();
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

    public static Task CodexHealthFailuresUseStableAuditCodes()
    {
        Equal(
            AgentHostAuditErrorCodes.CodexVersionUnsupported,
            AgentHostAuditErrorCodes.FromException(new CodexVersionPreflightException(
                CodexVersionPreflightFailure.UnsupportedVersion,
                "sanitized")));
        Equal(
            AgentHostAuditErrorCodes.CodexVersionTerminationFailed,
            AgentHostAuditErrorCodes.FromException(new CodexVersionPreflightException(
                CodexVersionPreflightFailure.TerminationFailed,
                "sanitized")));
        Equal(
            AgentHostAuditErrorCodes.CodexExecutableIdentityFailed,
            AgentHostAuditErrorCodes.FromException(new CodexVersionPreflightException(
                CodexVersionPreflightFailure.ExecutableIdentityChanged,
                "sanitized")));
        Equal(
            AgentHostAuditErrorCodes.CodexAppServerHandshakeTimedOut,
            AgentHostAuditErrorCodes.FromException(new AgentHostCodexHealthException(
                AgentHostCodexHealthFailure.AppServerHandshakeTimedOut,
                "sanitized")));
        Equal(
            AgentHostAuditErrorCodes.CodexAppServerHandshakeFailed,
            AgentHostAuditErrorCodes.FromException(new AgentHostCodexHealthException(
                AgentHostCodexHealthFailure.AppServerHandshakeFailed,
                "sanitized")));
        return Task.CompletedTask;
    }

    public static async Task CodexHealthTimeoutCancelsUnderlyingStart()
    {
        using var cleanup = new CancellationTokenSource();
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        AgentHostCodexHealthException? failure = null;
        try
        {
            await AgentHostCodexHealthCheck.StartAsync(
                async cancellationToken =>
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                        throw;
                    }
                },
                TimeSpan.FromMilliseconds(50),
                cleanup.Token);
        }
        catch (AgentHostCodexHealthException exception)
        {
            failure = exception;
        }

        var cancelledBeforeCallerCleanup = cancellationObserved.Task.IsCompletedSuccessfully;
        cleanup.Cancel();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        Equal(AgentHostCodexHealthFailure.AppServerHandshakeTimedOut, failure?.Failure);
        Equal(true, cancelledBeforeCallerCleanup);

        using var callerCancellation = new CancellationTokenSource();
        var callerCancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        callerCancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
        OperationCanceledException? cancellationFailure = null;
        try
        {
            await AgentHostCodexHealthCheck.StartAsync(
                async cancellationToken =>
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        callerCancellationObserved.TrySetResult();
                        throw;
                    }
                },
                TimeSpan.FromSeconds(1),
                callerCancellation.Token);
        }
        catch (OperationCanceledException exception)
        {
            cancellationFailure = exception;
        }

        await callerCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1))
            .ConfigureAwait(false);
        Equal(true, cancellationFailure is not null);
        Equal(true, callerCancellation.IsCancellationRequested);
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
            audit.Complete();

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
            audit.Complete();
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
            var beforeRuntimeOwnerCompletion =
                System.Text.Encoding.UTF8.GetString(auditStream.ToArray());
            False(beforeRuntimeOwnerCompletion.Contains(
                "\"eventType\":\"session_stopped\"",
                StringComparison.Ordinal));
            audit.Complete();
            var auditEvents = System.Text.Encoding.UTF8.GetString(auditStream.ToArray())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
                .ToArray();
            var requestedIndex = Array.FindIndex(auditEvents, item => string.Equals(
                item.GetProperty("eventType").GetString(),
                AgentHostAuditEventTypes.CancelRequested,
                StringComparison.Ordinal));
            var dispatchedIndex = Array.FindIndex(auditEvents, item => string.Equals(
                item.GetProperty("eventType").GetString(),
                AgentHostAuditEventTypes.CancelDispatched,
                StringComparison.Ordinal));
            var terminalIndex = Array.FindIndex(auditEvents, item => string.Equals(
                item.GetProperty("eventType").GetString(),
                AgentHostAuditEventTypes.TurnCancelled,
                StringComparison.Ordinal));
            Equal(true, requestedIndex >= 0
                && dispatchedIndex > requestedIndex
                && terminalIndex > dispatchedIndex);
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
            Equal("conversation-cancel-1",
                requested.GetProperty("systemConversationId").GetString());
            Equal("client-turn-cancel-1", requested.GetProperty("systemRequestId").GetString());
            Equal("thread-cancel-1", requested.GetProperty("providerThreadId").GetString());
            Equal("turn-cancel-1", requested.GetProperty("providerTurnId").GetString());
            Equal(requested.GetProperty("bridgeRequestId").GetString(),
                dispatched.GetProperty("bridgeRequestId").GetString());
            Equal("client-turn-cancel-1", terminal.GetProperty("systemRequestId").GetString());
            Equal("conversation-cancel-1",
                terminal.GetProperty("systemConversationId").GetString());
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
                                    ObjectId = "obj-00000042",
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
            Equal("obj-00000042", toolResult.GetProperty("entities")[0]
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
            audit.Complete();
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
            audit.Complete();

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

    private static void CreateCompletePersistentAudit(
        string sessionId,
        string segmentDirectory,
        string anchorDirectory)
    {
        using var audit = AgentHostAuditLog.CreateRotatingInProtectedDirectories(
            sessionId,
            segmentDirectory,
            anchorDirectory,
            maximumRecords: 8,
            maximumBytes: 16 * 1024,
            maximumSegments: 2);
        audit.Record(new AgentHostAuditEvent
        {
            EventType = AgentHostAuditEventTypes.BridgeConnected,
            OutcomeCode = AgentHostAuditOutcomeCodes.Connected,
        });
        audit.Complete();
    }

    private static void SetPersistentAuditWriteTime(
        AgentPersistentAuditStoreLease store,
        string sessionId,
        DateTimeOffset timestamp)
    {
        File.SetLastWriteTimeUtc(
            RetentionSegmentPath(store, sessionId),
            timestamp.UtcDateTime);
        File.SetLastWriteTimeUtc(
            RetentionAnchorPath(store, sessionId),
            timestamp.UtcDateTime);
    }

    private static string RetentionSegmentPath(
        AgentPersistentAuditStoreLease store,
        string sessionId)
        => Path.Combine(
            store.SegmentDirectory,
            sessionId + ".segment-000001.jsonl");

    private static string RetentionAnchorPath(
        AgentPersistentAuditStoreLease store,
        string sessionId)
        => Path.Combine(store.AnchorDirectory, sessionId + ".anchor.json");

    private static long ReadCompactedReceiptCount(string checkpointPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(checkpointPath));
        return document.RootElement
            .GetProperty("compactedReceiptCount")
            .GetInt64();
    }

    private static void ExpectRetentionRejected(string reasonCode, Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException(
                "不安全的审计清理操作被错误接受：" + reasonCode);
        }
        catch (AgentHostAuditRetentionExecutionException exception)
        {
            Equal(reasonCode, exception.ReasonCode);
        }
    }

    private static bool IsLowerHex(string value, int length)
        => value.Length == length
            && value.All(static character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static void CreateClosedNonTerminalAudit(
        string sessionId,
        string segmentDirectory,
        string anchorDirectory)
    {
        using var stream = new MemoryStream();
        using var anchorSink = new CapturingAuditAnchorSink();
        using var audit = new AgentHostAuditLog(
            stream,
            sessionId,
            "segment-000001",
            AgentHostAuditIntegrity.GenesisHash,
            anchorSink,
            leaveOpen: true,
            maximumRecords: 8,
            maximumBytes: 16 * 1024);
        audit.Record(new AgentHostAuditEvent
        {
            EventType = AgentHostAuditEventTypes.BridgeConnected,
            OutcomeCode = AgentHostAuditOutcomeCodes.Connected,
        });

        var bytes = stream.ToArray();
        var anchor = anchorSink.Current
            ?? throw new InvalidOperationException("非终态审计缺少锚点。");
        File.WriteAllBytes(
            Path.Combine(segmentDirectory, sessionId + ".segment-000001.jsonl"),
            bytes);
        File.WriteAllText(
            Path.Combine(anchorDirectory, sessionId + ".anchor.json"),
            JsonSerializer.Serialize(
                anchor,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new System.Text.UTF8Encoding(false));
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

    private static void ExpectAuditIntegrityFailure(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("篡改后的审计链错误地通过验证。");
        }
        catch (AgentHostAuditIntegrityException)
        {
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

internal sealed class AuditRetentionProcessCrashInjector
    : IAgentHostAuditRetentionFaultInjector
{
    private readonly string _markerPath;

    internal AuditRetentionProcessCrashInjector(string markerPath)
    {
        _markerPath = markerPath;
    }

    public void OnJournalCommitted()
    {
    }

    public void OnArtifactDeleted(int deletedArtifactCount)
    {
        if (deletedArtifactCount != 1)
        {
            return;
        }

        using (var marker = new FileStream(
                   _markerPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.Read,
                   4096,
                   FileOptions.WriteThrough))
        {
            marker.WriteByte(1);
            marker.Flush(flushToDisk: true);
        }

        Thread.Sleep(Timeout.Infinite);
    }
}

internal sealed class AuditRetentionFailAfterDeleteInjector
    : IAgentHostAuditRetentionFaultInjector
{
    private readonly int _failureCount;

    internal AuditRetentionFailAfterDeleteInjector(int failureCount)
    {
        _failureCount = failureCount;
    }

    public void OnJournalCommitted()
    {
    }

    public void OnArtifactDeleted(int deletedArtifactCount)
    {
        if (deletedArtifactCount == _failureCount)
        {
            throw new IOException("Injected audit retention interruption.");
        }
    }
}

internal sealed class AuditRetentionFailAfterReceiptCheckpointInjector
    : IAgentHostAuditRetentionFaultInjector
{
    public void OnJournalCommitted()
    {
    }

    public void OnArtifactDeleted(int deletedArtifactCount)
    {
    }

    public void OnReceiptCheckpointCommitted()
    {
        throw new IOException("Injected audit receipt checkpoint interruption.");
    }
}

internal enum SyntheticAuditRetentionIoFailurePoint
{
    JournalPrepared,
    JournalCommitted,
    ReceiptPrepared,
}

internal sealed class SyntheticAuditRetentionIoFailureInjector
    : IAgentHostAuditRetentionFaultInjector
{
    private readonly SyntheticAuditRetentionIoFailurePoint _failurePoint;
    private readonly string _privateMarker;
    private int _failed;

    internal SyntheticAuditRetentionIoFailureInjector(
        SyntheticAuditRetentionIoFailurePoint failurePoint,
        string privateMarker)
    {
        _failurePoint = failurePoint;
        _privateMarker = privateMarker;
    }

    public void OnControlFilePrepared(AgentHostAuditRetentionPersistenceStage stage)
    {
        if ((_failurePoint is SyntheticAuditRetentionIoFailurePoint.JournalPrepared
                && stage is AgentHostAuditRetentionPersistenceStage.JournalPrepared)
            || (_failurePoint is SyntheticAuditRetentionIoFailurePoint.ReceiptPrepared
                && stage is AgentHostAuditRetentionPersistenceStage.ReceiptPrepared))
        {
            FailOnce();
        }
    }

    public void OnJournalCommitted()
    {
        if (_failurePoint is SyntheticAuditRetentionIoFailurePoint.JournalCommitted)
        {
            FailOnce();
        }
    }

    public void OnArtifactDeleted(int deletedArtifactCount)
    {
    }

    private void FailOnce()
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0)
        {
            return;
        }

        throw new IOException(
            "Synthetic audit retention I/O failure fixture: "
            + _privateMarker
            + @" C:\Users\synthetic-audit-user\private\retention.json");
    }
}

internal sealed class AuditRetentionBlockingInjector
    : IAgentHostAuditRetentionFaultInjector, IDisposable
{
    internal ManualResetEventSlim JournalCommitted { get; } = new(false);

    internal ManualResetEventSlim Release { get; } = new(false);

    public void OnJournalCommitted()
    {
        JournalCommitted.Set();
        if (!Release.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("Audit retention concurrency fixture timed out.");
        }
    }

    public void OnArtifactDeleted(int deletedArtifactCount)
    {
    }

    public void Dispose()
    {
        JournalCommitted.Dispose();
        Release.Dispose();
    }
}

internal sealed class CapturingAuditAnchorSink : IAgentHostAuditAnchorSink
{
    public AgentHostAuditAnchor? Current { get; private set; }

    public void Write(AgentHostAuditAnchor anchor)
    {
        Current = new AgentHostAuditAnchor
        {
            SystemSessionId = anchor.SystemSessionId,
            SegmentId = anchor.SegmentId,
            Sequence = anchor.Sequence,
            RecordHash = anchor.RecordHash,
        };
    }

    public void Dispose()
    {
    }
}

internal sealed class SyntheticAuditAnchorFailureSink : IAgentHostAuditAnchorSink
{
    private bool _failNextWrite;

    public AgentHostAuditAnchor? Current { get; private set; }

    public int WriteCount { get; private set; }

    public void FailNextWrite() => _failNextWrite = true;

    public void Write(AgentHostAuditAnchor anchor)
    {
        WriteCount++;
        if (_failNextWrite)
        {
            _failNextWrite = false;
            throw new IOException("Synthetic audit anchor persistence failure.");
        }

        Current = new AgentHostAuditAnchor
        {
            SystemSessionId = anchor.SystemSessionId,
            SegmentId = anchor.SegmentId,
            Sequence = anchor.Sequence,
            RecordHash = anchor.RecordHash,
        };
    }

    public void Dispose()
    {
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

internal sealed class PartialWriteFailureStream : MemoryStream
{
    private bool _failNextWrite;

    public int WriteCount { get; private set; }

    public void FailNextWrite() => _failNextWrite = true;

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteCount++;
        if (_failNextWrite)
        {
            _failNextWrite = false;
            base.Write(buffer, offset, Math.Max(1, count / 2));
            throw new IOException("Synthetic partial audit write failure.");
        }

        base.Write(buffer, offset, count);
    }
}

internal sealed class SyntheticAuditWriteFailureStream : MemoryStream
{
    private bool _failBeforeNextWrite;

    public int WriteCount { get; private set; }

    public int FailedWriteCount { get; private set; }

    public void FailBeforeNextWrite() => _failBeforeNextWrite = true;

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteCount++;
        if (_failBeforeNextWrite)
        {
            _failBeforeNextWrite = false;
            FailedWriteCount++;
            throw new IOException("Synthetic persistent audit I/O failure fixture.");
        }

        base.Write(buffer, offset, count);
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
