using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Codex.AutoCAD.AgentHost;

/// <summary>
/// M4.12 CAD 执行事件 schema 规格。本里程碑只冻结事件类型、字段白名单、脱敏边界和哈希链
/// 纳入方式；真实接线归 M5.13，因此这里不断言存在生产调用方。
/// </summary>
internal static class AgentHostCadAuditSchemaSpecs
{
    private const string SamplePlanHash =
        "9f2c4a1b8e7d6c5b4a39281706f5e4d3c2b1a09f8e7d6c5b4a39281706f5e4d3";

    private static AgentHostAuditLog.AgentHostAuditEnvelope CreateEnvelope(
        string eventType,
        string? cadOperationKind = null,
        int? cadOperationCount = null,
        string? cadRiskLevel = null,
        string? cadRuleVersion = null,
        string? cadPlanHash = null,
        long? cadDocumentRevision = null)
        => new()
        {
            Schema = "codex.autocad.agenthost.audit/2",
            Sequence = 7,
            TimestampUtc = "2026-07-26T12:00:00.0000000Z",
            SystemSessionId = "0123456789abcdef0123456789abcdef",
            SegmentId = "segment-000001",
            PreviousRecordHash = AgentHostAuditIntegrity.GenesisHash,
            EventType = eventType,
            SystemRequestId = "req-1",
            CadOperationKind = cadOperationKind,
            CadOperationCount = cadOperationCount,
            CadRiskLevel = cadRiskLevel,
            CadRuleVersion = cadRuleVersion,
            CadPlanHash = cadPlanHash,
            CadDocumentRevision = cadDocumentRevision,
        };

    /// <summary>
    /// 独立重算扩展之前的哈希算法：18 个字段、长度前缀编码、SHA-256 小写十六进制。
    /// 用它证明本次 schema 扩展对既有记录逐字节不变。
    /// </summary>
    private static string ComputeLegacyRecordHash(AgentHostAuditLog.AgentHostAuditEnvelope e)
    {
        var builder = new StringBuilder(1024);
        void Append(string? value)
        {
            if (value is null)
            {
                builder.Append("-1:");
                return;
            }
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
        }

        Append(e.Schema);
        Append(e.Sequence.ToString(CultureInfo.InvariantCulture));
        Append(e.TimestampUtc);
        Append(e.SystemSessionId);
        Append(e.SegmentId);
        Append(e.PreviousRecordHash);
        Append(e.EventType);
        Append(e.SystemConversationId);
        Append(e.SystemTurnId);
        Append(e.SystemRequestId);
        Append(e.BridgeRequestId);
        Append(e.ProviderThreadId);
        Append(e.ProviderTurnId);
        Append(e.Method);
        Append(e.ApprovalKind);
        Append(e.Resolution);
        Append(e.OutcomeCode);
        Append(e.ErrorCode);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    /// <summary>
    /// 本组最重要的一条：扩展 schema 不得改变既有八类事件的记录哈希，
    /// 否则所有已持久化的生产审计链会集体失效。
    /// </summary>
    public static Task ExistingEventHashesAreUnchangedByTheCadExtension()
    {
        foreach (var eventType in new[]
                 {
                     AgentHostAuditEventTypes.SessionStarted,
                     AgentHostAuditEventTypes.RequestCompleted,
                     AgentHostAuditEventTypes.ApprovalRequested,
                     AgentHostAuditEventTypes.TurnFailed,
                 })
        {
            var envelope = CreateEnvelope(eventType);
            if (envelope.HasCadExecutionFields)
            {
                throw new InvalidOperationException("Non-CAD envelope must not report CAD fields.");
            }
            if (!string.Equals(
                    ComputeLegacyRecordHash(envelope),
                    AgentHostAuditIntegrity.ComputeRecordHash(envelope),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "CAD extension changed the hash of an existing event type: " + eventType);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>CAD 字段必须真正参与哈希，否则可被篡改而不破坏链。</summary>
    public static Task CadFieldsAreCoveredByTheHashChain()
    {
        var baseline = CreateEnvelope(
            AgentHostAuditEventTypes.CadApprovalDecided,
            AgentHostAuditCadOperationKinds.CreateLine,
            1,
            AgentHostAuditCadRiskLevels.Low,
            "cad_rules_v1",
            SamplePlanHash,
            42);

        if (!baseline.HasCadExecutionFields)
        {
            throw new InvalidOperationException("CAD envelope must report CAD fields.");
        }

        var baseHash = AgentHostAuditIntegrity.ComputeRecordHash(baseline);

        // 带 CAD 字段的记录，其哈希必须与旧算法不同——否则说明 CAD 段根本没进哈希。
        if (string.Equals(
                baseHash, ComputeLegacyRecordHash(baseline), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CAD segment is not part of the hash input.");
        }

        var mutations = new (string Name, AgentHostAuditLog.AgentHostAuditEnvelope Envelope)[]
        {
            ("count", CreateEnvelope(
                AgentHostAuditEventTypes.CadApprovalDecided,
                AgentHostAuditCadOperationKinds.CreateLine, 2,
                AgentHostAuditCadRiskLevels.Low, "cad_rules_v1", SamplePlanHash, 42)),
            ("risk", CreateEnvelope(
                AgentHostAuditEventTypes.CadApprovalDecided,
                AgentHostAuditCadOperationKinds.CreateLine, 1,
                AgentHostAuditCadRiskLevels.High, "cad_rules_v1", SamplePlanHash, 42)),
            ("ruleVersion", CreateEnvelope(
                AgentHostAuditEventTypes.CadApprovalDecided,
                AgentHostAuditCadOperationKinds.CreateLine, 1,
                AgentHostAuditCadRiskLevels.Low, "cad_rules_v2", SamplePlanHash, 42)),
            ("planHash", CreateEnvelope(
                AgentHostAuditEventTypes.CadApprovalDecided,
                AgentHostAuditCadOperationKinds.CreateLine, 1,
                AgentHostAuditCadRiskLevels.Low, "cad_rules_v1",
                SamplePlanHash.Replace('9', '8'), 42)),
            ("revision", CreateEnvelope(
                AgentHostAuditEventTypes.CadApprovalDecided,
                AgentHostAuditCadOperationKinds.CreateLine, 1,
                AgentHostAuditCadRiskLevels.Low, "cad_rules_v1", SamplePlanHash, 43)),
            ("droppedPlanHash", CreateEnvelope(
                AgentHostAuditEventTypes.CadApprovalDecided,
                AgentHostAuditCadOperationKinds.CreateLine, 1,
                AgentHostAuditCadRiskLevels.Low, "cad_rules_v1", null, 42)),
        };

        foreach (var (name, mutated) in mutations)
        {
            if (string.Equals(
                    baseHash,
                    AgentHostAuditIntegrity.ComputeRecordHash(mutated),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "CAD field is not covered by the hash chain: " + name);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>全链事件类型必须齐备，覆盖 M5.13 交付要求的每个阶段。</summary>
    public static Task CadEventTypesCoverTheWholeWriteChain()
    {
        var required = new[]
        {
            AgentHostAuditEventTypes.CadProposalReceived,
            AgentHostAuditEventTypes.CadValidationFailed,
            AgentHostAuditEventTypes.CadPreviewGenerated,
            AgentHostAuditEventTypes.CadApprovalPresented,
            AgentHostAuditEventTypes.CadApprovalDecided,
            AgentHostAuditEventTypes.CadCapabilityConsumed,
            AgentHostAuditEventTypes.CadLockRevalidated,
            AgentHostAuditEventTypes.CadLockRevalidationFailed,
            AgentHostAuditEventTypes.CadTransactionCommitted,
            AgentHostAuditEventTypes.CadTransactionAborted,
            AgentHostAuditEventTypes.CadExecutionCompleted,
            AgentHostAuditEventTypes.CadExecutionFailed,
            AgentHostAuditEventTypes.CadExecutionUnknown,
        };

        foreach (var eventType in required)
        {
            if (!AgentHostAuditEventTypes.IsKnown(eventType))
            {
                throw new InvalidOperationException("CAD event type not registered: " + eventType);
            }
            if (!AgentHostAuditEventTypes.IsCadExecution(eventType))
            {
                throw new InvalidOperationException("CAD event type not classified: " + eventType);
            }
        }

        if (AgentHostAuditEventTypes.IsCadExecution(AgentHostAuditEventTypes.TurnCompleted))
        {
            throw new InvalidOperationException("Non-CAD event must not classify as CAD execution.");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 隐私边界：CAD 事件的公共字段集合必须保持最小。新增坐标、图层、Handle、路径或
    /// token 字段会破坏 M4.12"不记录完整 CAD JSON"与 M5.13"无法复现敏感图纸"的要求，
    /// 因此把字段集合本身冻结为断言。
    /// </summary>
    public static Task CadFieldWhitelistIsFrozen()
    {
        var cadProperties = typeof(AgentHostAuditEvent)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => name.StartsWith("Cad", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "CadDocumentRevision",
            "CadOperationCount",
            "CadOperationKind",
            "CadPlanHash",
            "CadRiskLevel",
            "CadRuleVersion",
        };

        if (cadProperties.Length != expected.Length)
        {
            throw new InvalidOperationException(
                "CAD audit field whitelist changed; review the privacy boundary before updating.");
        }
        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(cadProperties[i], expected[i], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "CAD audit field whitelist changed: " + cadProperties[i]);
            }
        }
        return Task.CompletedTask;
    }
}
