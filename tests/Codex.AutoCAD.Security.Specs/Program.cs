using Codex.AutoCAD.Security;

namespace Codex.AutoCAD.Security.Specs;

internal static class Program
{
    private static int Main()
    {
        var specifications = new (string Name, Action Run)[]
        {
            ("合法审批按完整状态机提交", ApprovalCompletesStateMachine),
            ("一次性令牌在60秒后过期", ApprovalExpiresAfterSixtySeconds),
            ("已消费令牌不能重放", ConsumedTokenCannotBeReplayed),
            ("图纸修订变化使批准失效", DrawingRevisionChangeInvalidatesApproval),
            ("计划哈希变化使批准失效", PlanHashChangeInvalidatesApproval),
            ("CAD审批禁止会话级授权", SessionApprovalIsForbidden),
            ("未知动作默认拒绝", UnknownActionIsDeniedByDefault),
            ("超限计划不能进入审批门", OversizedPlanCannotEnterApprovalGate),
            ("危险路径全部拒绝", DangerousPathsAreDenied),
            ("资源配额采取摘要升级或拒绝", ResourceQuotasFailClosed),
        };

        var failed = 0;
        foreach (var specification in specifications)
        {
            try
            {
                specification.Run();
                Console.WriteLine($"PASS  {specification.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL  {specification.Name}");
                Console.Error.WriteLine(exception);
            }
        }

        Console.WriteLine($"\n规格总数: {specifications.Length}, 通过: {specifications.Length - failed}, 失败: {failed}");
        return failed == 0 ? 0 : 1;
    }

    private static void ApprovalCompletesStateMachine()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero));
        using var gate = new CadApprovalGate(clock);
        var prepared = PrepareApprovedRequest(gate);
        using var token = prepared.Token;

        AssertSuccess(gate.MarkDocumentLocked(prepared.RequestId));
        var consumption = gate.ValidateAndConsume(token, prepared.Binding);
        Assert.True(consumption.Success, consumption.ReasonCode);
        Assert.Equal(CadApprovalState.RevisionRevalidated, consumption.State!.Value);
        Assert.True(token.IsDisposed, "令牌成功消费后必须清零并处于已释放状态。");

        AssertSuccess(gate.BeginExecution(prepared.RequestId));
        AssertSuccess(gate.Commit(prepared.RequestId));
        AssertSnapshotState(gate, prepared.RequestId, CadApprovalState.Committed);
    }

    private static void ApprovalExpiresAfterSixtySeconds()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero));
        using var gate = new CadApprovalGate(clock);
        var prepared = PrepareApprovedRequest(gate);
        using var token = prepared.Token;

        clock.Advance(TimeSpan.FromSeconds(60));
        var lockResult = gate.MarkDocumentLocked(prepared.RequestId);

        Assert.False(lockResult.Success, "到达60秒边界后不应继续获取文档锁授权。");
        Assert.Equal(ApprovalFailureReason.TokenExpired, lockResult.Failure);
        AssertSnapshotState(gate, prepared.RequestId, CadApprovalState.Expired);
    }

    private static void ConsumedTokenCannotBeReplayed()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero));
        using var gate = new CadApprovalGate(clock);
        var prepared = PrepareApprovedRequest(gate);
        using var token = prepared.Token;

        AssertSuccess(gate.MarkDocumentLocked(prepared.RequestId));
        Assert.True(gate.ValidateAndConsume(token, prepared.Binding).Success, "第一次消费应成功。");

        var replay = gate.ValidateAndConsume(token, prepared.Binding);
        Assert.False(replay.Success, "同一令牌不得消费第二次。");
        Assert.Equal(ApprovalFailureReason.ReplayDetected, replay.Failure);
    }

    private static void DrawingRevisionChangeInvalidatesApproval()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero));
        using var gate = new CadApprovalGate(clock);
        var prepared = PrepareApprovedRequest(gate);
        using var token = prepared.Token;

        AssertSuccess(gate.MarkDocumentLocked(prepared.RequestId));
        var staleBinding = prepared.Binding.WithDrawingRevision(prepared.Binding.DrawingRevision + 1);
        var result = gate.ValidateAndConsume(token, staleBinding);

        Assert.False(result.Success, "图纸修订变化后旧批准必须失效。");
        Assert.Equal(ApprovalFailureReason.BindingMismatch, result.Failure);
        AssertSnapshotState(gate, prepared.RequestId, CadApprovalState.Expired);
    }

    private static void PlanHashChangeInvalidatesApproval()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero));
        using var gate = new CadApprovalGate(clock);
        var prepared = PrepareApprovedRequest(gate);
        using var token = prepared.Token;

        AssertSuccess(gate.MarkDocumentLocked(prepared.RequestId));
        var changedPlan = new CadApprovalBinding(
            prepared.Binding.ThreadId,
            prepared.Binding.TurnId,
            SecurityHash.ComputeSha256Hex("different normalized operation batch"),
            prepared.Binding.DrawingFingerprint,
            prepared.Binding.DrawingRevision,
            prepared.Binding.SelectionSnapshotHash);

        var result = gate.ValidateAndConsume(token, changedPlan);
        Assert.False(result.Success, "计划变化后旧批准必须失效。");
        Assert.Equal(ApprovalFailureReason.BindingMismatch, result.Failure);
    }

    private static void SessionApprovalIsForbidden()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero));
        using var gate = new CadApprovalGate(clock);
        var binding = CreateBinding(drawingRevision: 7);
        var action = new CadActionDescriptor(CadActionKind.CreateEntity);
        var requestId = PrepareAwaitingRequest(gate, binding, action);

        var result = gate.Approve(requestId, ApprovalScope.Session);
        Assert.False(result.Success, "CAD写操作不能获得会话授权。");
        Assert.Equal(ApprovalFailureReason.SessionScopeForbidden, result.Failure);
        Assert.True(result.Token is null, "拒绝会话授权时不得签发令牌。");
        AssertSnapshotState(gate, requestId, CadApprovalState.AwaitingUser);
        Assert.False(SessionAuthorizationPolicy.IsAllowed(action), "R3 CAD写操作必须禁用会话授权。");
    }

    private static void UnknownActionIsDeniedByDefault()
    {
        var unknown = CadRiskClassifier.Assess(new CadActionDescriptor((CadActionKind)9_999));
        Assert.Equal(PolicyDecision.Deny, unknown.Decision);
        Assert.Equal(CadRiskLevel.HardDeny, unknown.Level);
        Assert.False(unknown.SessionGrantAllowed, "未知动作不得获得会话授权。");

        using var gate = new CadApprovalGate();
        Assert.Throws<InvalidOperationException>(
            () => gate.Propose(CreateBinding(1), new CadActionDescriptor((CadActionKind)9_999)));
    }

    private static void OversizedPlanCannotEnterApprovalGate()
    {
        using var gate = new CadApprovalGate();
        var oversized = new CadActionDescriptor(CadActionKind.CreateEntity)
        {
            OperationCount = 5_001,
            AffectedEntityCount = 5_001,
        };

        Assert.Throws<InvalidOperationException>(() => gate.Propose(CreateBinding(1), oversized));

        var bulk = new CadActionDescriptor(CadActionKind.ModifyEntity)
        {
            OperationCount = 501,
            AffectedEntityCount = 501,
            TargetEntityCount = 1_000,
        };
        var requestId = gate.Propose(CreateBinding(1), bulk);
        var snapshot = GetSnapshot(gate, requestId);
        Assert.Equal(CadRiskLevel.R4DestructiveOrBulk, snapshot.Risk.Level);
        Assert.True(snapshot.Risk.RequiresCheckpoint, "超过批量阈值后必须创建恢复检查点。");
    }

    private static void DangerousPathsAreDenied()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codex-autocad-security-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policy = new PathRootPolicy(
                new[] { new PathRootRule(root, PathAccessKind.Read | PathAccessKind.Write) });

            var safePath = Path.Combine(root, "outputs", "report.txt");
            var safe = policy.Evaluate(safePath, PathAccessKind.Write);
            Assert.True(safe.Allowed, safe.ReasonCode);
            Assert.True(safe.MustRevalidateImmediatelyBeforeUse, "允许结果必须要求使用前再次检查以缩小TOCTOU窗口。");

            AssertDenied(
                policy.Evaluate(Path.Combine(root, "..", "escape.txt"), PathAccessKind.Write),
                PathPolicyFailureReason.TraversalSegment);
            AssertDenied(
                policy.Evaluate(@"\\server\share\payload.txt", PathAccessKind.Read),
                PathPolicyFailureReason.UncPath);
            AssertDenied(
                policy.Evaluate(@"\\?\C:\temp\payload.txt", PathAccessKind.Read),
                PathPolicyFailureReason.DevicePath);
            AssertDenied(
                policy.Evaluate(Path.Combine(root, "report.txt:secret"), PathAccessKind.Write),
                PathPolicyFailureReason.AlternateDataStream);
            AssertDenied(
                policy.Evaluate(Path.Combine(root, "CON.txt"), PathAccessKind.Write),
                PathPolicyFailureReason.ReservedDeviceName);
            AssertDenied(
                policy.Evaluate(Path.Combine(root, "MODEL~1", "report.txt"), PathAccessKind.Write),
                PathPolicyFailureReason.AmbiguousShortName);
            AssertDenied(
                policy.Evaluate(Path.Combine(root, "drawing.dwg"), PathAccessKind.Write),
                PathPolicyFailureReason.ProtectedCadFile);

            var cadRead = policy.Evaluate(Path.Combine(root, "drawing.dwg"), PathAccessKind.Read);
            Assert.True(cadRead.Allowed, "允许根内的DWG读取不应被普通写入保护规则误伤。");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ResourceQuotasFailClosed()
    {
        var policy = new ResourceQuotaPolicy();

        Assert.Equal(
            QuotaDisposition.Reject,
            policy.AssessIpcMessage(8 * 1024 * 1024 + 1, jsonDepth: 1).Disposition);
        Assert.Equal(
            QuotaDisposition.SummarizeOnly,
            policy.AssessContext(entityCount: 10_001, payloadBytes: 1).Disposition);
        Assert.Equal(
            QuotaDisposition.EscalateToHighRisk,
            policy.AssessPlan(operationCount: 501, deletedEntityCount: 0, targetEntityCount: 1_000).Disposition);
        Assert.Equal(
            QuotaDisposition.Reject,
            policy.AssessPlan(operationCount: 5_001, deletedEntityCount: 0, targetEntityCount: 1_000).Disposition);
        Assert.Equal(
            QuotaDisposition.BoundingBoxesOnly,
            policy.AssessPreview(objectCount: 2_001).Disposition);
    }

    private static PreparedApproval PrepareApprovedRequest(CadApprovalGate gate)
    {
        var binding = CreateBinding(drawingRevision: 7);
        var action = new CadActionDescriptor(CadActionKind.CreateEntity);
        var requestId = PrepareAwaitingRequest(gate, binding, action);
        var issue = gate.Approve(requestId, ApprovalScope.Once);

        Assert.True(issue.Success, issue.ReasonCode);
        Assert.True(issue.Token is not null, "成功批准必须签发令牌。");
        Assert.Equal(TimeSpan.FromSeconds(60), issue.Token!.ExpiresAt - GetSnapshot(gate, requestId).ApprovedAt!.Value);
        return new PreparedApproval(requestId, binding, issue.Token);
    }

    private static Guid PrepareAwaitingRequest(
        CadApprovalGate gate,
        CadApprovalBinding binding,
        CadActionDescriptor action)
    {
        var requestId = gate.Propose(binding, action);
        AssertSuccess(gate.RecordSchemaValidated(requestId));
        AssertSuccess(gate.RecordPolicyValidated(requestId));
        AssertSuccess(gate.RecordSideDatabaseSimulated(requestId));
        AssertSuccess(gate.RecordPreviewReady(requestId));
        AssertSuccess(gate.AwaitUserDecision(requestId));
        return requestId;
    }

    private static CadApprovalBinding CreateBinding(long drawingRevision) =>
        new(
            threadId: "thread-001",
            turnId: "turn-001",
            normalizedPlanHash: SecurityHash.ComputeSha256Hex("normalized operation batch"),
            drawingFingerprint: SecurityHash.ComputeSha256Hex("drawing fingerprint"),
            drawingRevision,
            selectionSnapshotHash: SecurityHash.ComputeSha256Hex("selection snapshot"));

    private static CadApprovalRequestSnapshot GetSnapshot(CadApprovalGate gate, Guid requestId)
    {
        Assert.True(gate.TryGetSnapshot(requestId, out var snapshot), "请求快照应存在。");
        return snapshot!;
    }

    private static void AssertSnapshotState(CadApprovalGate gate, Guid requestId, CadApprovalState expected) =>
        Assert.Equal(expected, GetSnapshot(gate, requestId).State);

    private static void AssertSuccess(ApprovalOperationResult result) =>
        Assert.True(result.Success, $"操作失败: {result.ReasonCode}, state={result.State}");

    private static void AssertDenied(PathPolicyDecision decision, PathPolicyFailureReason expected)
    {
        Assert.False(decision.Allowed, $"危险路径被错误允许: {decision.CanonicalPath}");
        Assert.Equal(expected, decision.Failure);
    }

    private sealed record PreparedApproval(
        Guid RequestId,
        CadApprovalBinding Binding,
        CadApprovalToken Token);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private long _timestamp;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _utcNow += value;
            _timestamp = checked(_timestamp + value.Ticks);
        }
    }

    private static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void False(bool condition, string message) => True(!condition, message);

        public static void Equal<T>(T expected, T actual)
            where T : notnull
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"预期: {expected}; 实际: {actual}");
            }
        }

        public static void Throws<TException>(Action action)
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

            throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}。");
        }
    }
}
