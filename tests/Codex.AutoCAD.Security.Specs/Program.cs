using System.Globalization;
using Codex.AutoCAD.Contracts;
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
            ("执行前无效计划会销毁一次性令牌", InvalidCurrentPlanInvalidatesToken),
            ("CAD审批禁止会话级授权", SessionApprovalIsForbidden),
            ("未知动作默认拒绝", UnknownActionIsDeniedByDefault),
            ("CAD计划不能被独立动作描述符降级", CadPlanCannotBeDowngradedByDescriptor),
            ("超限计划不能进入审批门", OversizedPlanCannotEnterApprovalGate),
            ("R4无检查点不得进入用户决策", HighRiskRequestRequiresCheckpoint),
            ("R4检查点完成审批执行闭环", HighRiskCheckpointCompletesStateMachine),
            ("检查点篡改立即使批准失效", TamperedCheckpointInvalidatesApproval),
            ("危险路径全部拒绝", DangerousPathsAreDenied),
            ("资源配额采取摘要升级或拒绝", ResourceQuotasFailClosed),
            ("计划哈希跨区域设置保持稳定", PlanHashIsCultureInvariant),
            ("畸形Unicode计划不能产生替换回退哈希碰撞", MalformedUnicodePlanIsRejectedBeforeHashing),
            ("计划任一执行字段变化都会改变哈希", PlanHashBindsExecutionFields),
            ("无效计划不能生成审批哈希", InvalidPlanCannotBeHashed),
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

        // Machine-consumed verification output must remain invariant across redirected
        // PowerShell hosts and console code pages.
        Console.WriteLine($"{specifications.Length - failed}/{specifications.Length} specs passed");
        return failed == 0 ? 0 : 1;
    }

    private static void ApprovalCompletesStateMachine()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero));
        using var gate = new CadApprovalGate(clock);
        var prepared = PrepareApprovedRequest(gate);
        using var token = prepared.Token;

        AssertSuccess(gate.MarkDocumentLocked(prepared.RequestId));
        var consumption = gate.ValidateAndConsume(token, prepared.Batch);
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
        Assert.True(gate.ValidateAndConsume(token, prepared.Batch).Success, "第一次消费应成功。");

        var replay = gate.ValidateAndConsume(token, prepared.Batch);
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
        prepared.Batch.Document.Revision++;
        var result = gate.ValidateAndConsume(token, prepared.Batch);

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
        ((CreateLineOperation)prepared.Batch.Operations[0]).End.X++;
        var result = gate.ValidateAndConsume(token, prepared.Batch);
        Assert.False(result.Success, "计划变化后旧批准必须失效。");
        Assert.Equal(ApprovalFailureReason.BindingMismatch, result.Failure);
    }

    private static void InvalidCurrentPlanInvalidatesToken()
    {
        using var gate = new CadApprovalGate();
        var prepared = PrepareApprovedRequest(gate);
        using var token = prepared.Token;
        AssertSuccess(gate.MarkDocumentLocked(prepared.RequestId));

        var line = (CreateLineOperation)prepared.Batch.Operations[0];
        line.End = new CadPoint3(line.Start.X, line.Start.Y, line.Start.Z);
        var result = gate.ValidateAndConsume(token, prepared.Batch);

        Assert.False(result.Success, "执行前计划失效时不得保留批准。");
        Assert.Equal(ApprovalFailureReason.PolicyDenied, result.Failure);
        Assert.True(token.IsDisposed, "无效当前计划必须立即清零一次性令牌。");
        AssertSnapshotState(gate, prepared.RequestId, CadApprovalState.Expired);
    }

    private static void SessionApprovalIsForbidden()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero));
        using var gate = new CadApprovalGate(clock);
        var batch = CreateLineBatch();
        var action = new CadActionDescriptor(CadActionKind.CreateEntity);
        var requestId = PrepareAwaitingRequest(gate, batch);

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

    private static void CadPlanCannotBeDowngradedByDescriptor()
    {
        using var gate = new CadApprovalGate();
        var destructivePlan = CreateEraseBatch();

        // The legacy overload cannot be used for any CAD write, even when the caller lies about
        // the action kind/counts and supplies a hand-built plan hash.
        Assert.Throws<InvalidOperationException>(
            () => gate.Propose(
                CreateBinding(drawingRevision: 7),
                new CadActionDescriptor(CadActionKind.CreateEntity)));

        var requestId = gate.Propose(destructivePlan);
        var snapshot = GetSnapshot(gate, requestId);
        Assert.Equal(CadActionKind.DeleteEntity, snapshot.Risk.Action);
        Assert.Equal(CadRiskLevel.R4DestructiveOrBulk, snapshot.Risk.Level);
        Assert.True(snapshot.Risk.RequiresCheckpoint, "删除计划必须由门内派生为R4。");
        var plan = snapshot.Plan
            ?? throw new InvalidOperationException("CAD审批快照必须包含冻结计划事实。");
        Assert.Equal(1, plan.OperationCount);
        Assert.Equal(2, plan.DeletedEntityCount);
        Assert.Equal(2, plan.TargetEntityCount);
        Assert.Equal(snapshot.Binding.NormalizedPlanHash, plan.NormalizedPlanHash);

        var lineBatch = CreateLineBatch();
        var lineRequestId = PrepareAwaitingRequest(gate, lineBatch);
        var issue = gate.Approve(lineRequestId, ApprovalScope.Once);
        Assert.True(issue.Success && issue.Token is not null, issue.ReasonCode);
        using var token = issue.Token!;
        AssertSuccess(gate.MarkDocumentLocked(lineRequestId));
        var bindingOnlyAttempt = gate.ValidateAndConsume(
            token,
            GetSnapshot(gate, lineRequestId).Binding);
        Assert.False(
            bindingOnlyAttempt.Success,
            "CAD计划不能使用调用者可独立构造的Binding消费令牌。");
        Assert.Equal(ApprovalFailureReason.PolicyDenied, bindingOnlyAttempt.Failure);
        Assert.True(token.IsDisposed, "拒绝Binding降级路径后必须清零令牌。");
        AssertSnapshotState(gate, lineRequestId, CadApprovalState.Expired);
    }

    private static void OversizedPlanCannotEnterApprovalGate()
    {
        using var gate = new CadApprovalGate();
        Assert.Throws<InvalidOperationException>(() => gate.Propose(CreateLineBatch(5_001)));

        var requestId = gate.Propose(CreateLineBatch(501));
        var snapshot = GetSnapshot(gate, requestId);
        Assert.Equal(CadRiskLevel.R4DestructiveOrBulk, snapshot.Risk.Level);
        Assert.True(snapshot.Risk.RequiresCheckpoint, "超过批量阈值后必须创建恢复检查点。");
        Assert.Equal(501, snapshot.Plan!.OperationCount);
    }

    private static void HighRiskRequestRequiresCheckpoint()
    {
        using var gate = new CadApprovalGate();
        var requestId = PreparePreviewReadyRequest(gate, CreateEraseBatch());

        var awaitResult = gate.AwaitUserDecision(requestId);
        Assert.False(awaitResult.Success, "R4没有检查点不得进入用户审批。");
        Assert.Equal(ApprovalFailureReason.CheckpointRequired, awaitResult.Failure);
        AssertSnapshotState(gate, requestId, CadApprovalState.PreviewReady);

        var approveResult = gate.Approve(requestId, ApprovalScope.Once);
        Assert.False(approveResult.Success, "R4没有检查点不得签发令牌。");
        Assert.True(approveResult.Token is null, "缺失检查点时不得产生令牌。");

        var executeResult = gate.BeginExecution(requestId);
        Assert.False(executeResult.Success, "R4没有检查点不得开始执行。");
    }

    private static void HighRiskCheckpointCompletesStateMachine()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero));
        using var gate = new CadApprovalGate(clock);
        var batch = CreateEraseBatch();
        var checkpoint = CreateCheckpoint("checkpoint-r4-001");
        var requestId = PrepareAwaitingRequest(gate, batch, checkpoint);
        var snapshot = GetSnapshot(gate, requestId);

        var checkpointAudit = snapshot.Checkpoint
            ?? throw new InvalidOperationException("R4快照必须保留检查点审计证据。");
        Assert.Equal(checkpoint, checkpointAudit.Evidence);
        Assert.Equal(64, checkpointAudit.Attestation.Length);
        Assert.False(
            string.Equals(checkpoint.CheckpointDigest, checkpointAudit.Attestation, StringComparison.Ordinal),
            "检查点证明必须是门内HMAC，而不是原样复用调用者摘要。");

        var issue = gate.Approve(requestId, ApprovalScope.Once);
        Assert.True(issue.Success && issue.Token is not null, issue.ReasonCode);
        using var token = issue.Token!;
        AssertSuccess(gate.MarkDocumentLocked(requestId));
        var consumption = gate.ValidateAndConsume(token, batch, checkpoint);
        Assert.True(consumption.Success, consumption.ReasonCode);
        AssertSuccess(gate.BeginExecution(requestId));
        AssertSuccess(gate.Commit(requestId));
        AssertSnapshotState(gate, requestId, CadApprovalState.Committed);
    }

    private static void TamperedCheckpointInvalidatesApproval()
    {
        using var gate = new CadApprovalGate();
        var batch = CreateEraseBatch();
        var checkpoint = CreateCheckpoint("checkpoint-r4-002");
        var requestId = PrepareAwaitingRequest(gate, batch, checkpoint);
        var issue = gate.Approve(requestId, ApprovalScope.Once);
        Assert.True(issue.Success && issue.Token is not null, issue.ReasonCode);
        using var token = issue.Token!;
        AssertSuccess(gate.MarkDocumentLocked(requestId));

        var tampered = new CadCheckpointEvidence(
            checkpoint.CheckpointId,
            SecurityHash.ComputeSha256Hex("attacker-replaced-checkpoint"));
        var consumption = gate.ValidateAndConsume(token, batch, tampered);
        Assert.False(consumption.Success, "替换检查点摘要后旧批准必须失效。");
        Assert.Equal(ApprovalFailureReason.CheckpointMismatch, consumption.Failure);
        Assert.True(token.IsDisposed, "检测到检查点篡改后必须清零令牌。");
        AssertSnapshotState(gate, requestId, CadApprovalState.Expired);
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

    private static void PlanHashIsCultureInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var batch = CreateHashableBatch();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var frenchHash = CadPlanHash.Compute(batch);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            var chineseHash = CadPlanHash.Compute(batch);

            Assert.Equal(frenchHash, chineseHash);
            Assert.Equal(64, frenchHash.Length);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static void MalformedUnicodePlanIsRejectedBeforeHashing()
    {
        var highSurrogateBatch = CreateHashableBatch();
        ((CreateLineOperation)highSurrogateBatch.Operations[0]).Layer = "\uD800";
        var lowSurrogateBatch = CreateHashableBatch();
        ((CreateLineOperation)lowSurrogateBatch.Operations[0]).Layer = "\uDFFF";

        var highOutcome = TryComputePlanHash(highSurrogateBatch);
        var lowOutcome = TryComputePlanHash(lowSurrogateBatch);

        Assert.True(
            highOutcome.Rejected && lowOutcome.Rejected,
            "未配对代理项必须在计划哈希前被拒绝。" +
            $" observedHighHash={highOutcome.Hash}; observedLowHash={lowOutcome.Hash}; " +
            $"collision={string.Equals(highOutcome.Hash, lowOutcome.Hash, StringComparison.Ordinal)}");
    }

    private static (bool Rejected, string Hash) TryComputePlanHash(CadOperationBatch batch)
    {
        try
        {
            return (false, CadPlanHash.Compute(batch));
        }
        catch (InvalidOperationException)
        {
            return (true, string.Empty);
        }
    }

    private static void PlanHashBindsExecutionFields()
    {
        var batch = CreateHashableBatch();
        var original = CadPlanHash.Compute(batch);

        var transform = (TransformEntitiesOperation)batch.Operations[2];
        transform.UniformScale = 1.5000000000000002d;
        var changedScale = CadPlanHash.Compute(batch);
        Assert.False(string.Equals(original, changedScale, StringComparison.Ordinal),
            "缩放参数变化必须改变计划哈希。");

        transform.UniformScale = 1.5d;
        batch.Document.DocumentId = "document-002";
        var changedDocument = CadPlanHash.Compute(batch);
        Assert.False(string.Equals(original, changedDocument, StringComparison.Ordinal),
            "文档标识变化必须改变计划哈希。");

        batch.Document.DocumentId = "document-001";
        ((EraseEntitiesOperation)batch.Operations[1]).Handles[0] = "FF";
        var changedTarget = CadPlanHash.Compute(batch);
        Assert.False(string.Equals(original, changedTarget, StringComparison.Ordinal),
            "目标Handle变化必须改变计划哈希。");

        batch = CreateLineBatch();
        original = CadPlanHash.Compute(batch);
        batch.RequiresSelectionRevalidation = true;
        var changedSelectionPolicy = CadPlanHash.Compute(batch);
        Assert.False(string.Equals(original, changedSelectionPolicy, StringComparison.Ordinal),
            "选择重验证要求变化必须改变计划哈希。");

        batch = CreateHashableBatch();
        original = CadPlanHash.Compute(batch);
        var line = (CreateLineOperation)batch.Operations[0];
        line.OwnerSpaceHandle = "2F";
        var changedOwner = CadPlanHash.Compute(batch);
        Assert.False(string.Equals(original, changedOwner, StringComparison.Ordinal),
            "目标空间Handle变化必须改变计划哈希。");

        batch = CreateHashableBatch();
        original = CadPlanHash.Compute(batch);
        ((CreateLineOperation)batch.Operations[0]).LayerHandle = "11";
        var changedLayer = CadPlanHash.Compute(batch);
        Assert.False(string.Equals(original, changedLayer, StringComparison.Ordinal),
            "目标图层Handle变化必须改变计划哈希。");
    }

    private static void InvalidPlanCannotBeHashed()
    {
        var batch = CreateHashableBatch();
        var line = (CreateLineOperation)batch.Operations[0];
        line.End = new CadPoint3(line.Start.X, line.Start.Y, line.Start.Z);

        Assert.Throws<InvalidOperationException>(() => CadPlanHash.Compute(batch));
    }

    private static CadOperationBatch CreateHashableBatch()
    {
        return new CadOperationBatch
        {
            BatchId = "batch-001",
            ThreadId = "thread-001",
            TurnId = "turn-001",
            Document = new CadDocumentRef
            {
                DocumentId = "document-001",
                DrawingFingerprint = SecurityHash.ComputeSha256Hex("drawing-001"),
                Revision = 12,
            },
            SelectionSnapshotHash = SecurityHash.ComputeSha256Hex("selection-001"),
            RequiresSelectionRevalidation = true,
            DeclaredRisk = Codex.AutoCAD.Contracts.CadRiskLevel.DestructiveWrite,
            Operations =
            [
                new CreateLineOperation
                {
                    OperationId = "line-001",
                    Start = new CadPoint3(1.25d, -2.5d, 0d),
                    End = new CadPoint3(10.75d, 4.125d, 0d),
                    Layer = "AI-PREVIEW",
                    LayerHandle = "10",
                    OwnerSpaceHandle = "1F",
                },
                new EraseEntitiesOperation
                {
                    OperationId = "erase-001",
                    Handles = ["A1", "B2"],
                },
                new TransformEntitiesOperation
                {
                    OperationId = "transform-001",
                    Handles = ["C3"],
                    Translation = new CadPoint3(3d, 4d, 0d),
                    RotationRadians = Math.PI / 4d,
                    UniformScale = 1.5d,
                },
            ],
        };
    }

    private static PreparedApproval PrepareApprovedRequest(CadApprovalGate gate)
    {
        var batch = CreateLineBatch();
        var requestId = PrepareAwaitingRequest(gate, batch);
        var issue = gate.Approve(requestId, ApprovalScope.Once);

        Assert.True(issue.Success, issue.ReasonCode);
        Assert.True(issue.Token is not null, "成功批准必须签发令牌。");
        Assert.Equal(TimeSpan.FromSeconds(60), issue.Token!.ExpiresAt - GetSnapshot(gate, requestId).ApprovedAt!.Value);
        return new PreparedApproval(
            requestId,
            batch,
            GetSnapshot(gate, requestId).Binding,
            issue.Token!);
    }

    private static Guid PrepareAwaitingRequest(
        CadApprovalGate gate,
        CadOperationBatch batch,
        CadCheckpointEvidence? checkpoint = null)
    {
        var requestId = PreparePreviewReadyRequest(gate, batch);
        if (checkpoint is not null)
        {
            AssertSuccess(gate.RecordCheckpoint(requestId, checkpoint));
            AssertSnapshotState(gate, requestId, CadApprovalState.CheckpointRecorded);
        }

        AssertSuccess(gate.AwaitUserDecision(requestId));
        return requestId;
    }

    private static Guid PreparePreviewReadyRequest(
        CadApprovalGate gate,
        CadOperationBatch batch)
    {
        var requestId = gate.Propose(batch);
        AssertSuccess(gate.RecordSchemaValidated(requestId));
        AssertSuccess(gate.RecordPolicyValidated(requestId));
        AssertSuccess(gate.RecordSideDatabaseSimulated(requestId));
        AssertSuccess(gate.RecordPreviewReady(requestId));
        return requestId;
    }

    private static CadOperationBatch CreateLineBatch(int operationCount = 1)
    {
        var operations = new CadOperation[operationCount];
        for (var index = 0; index < operationCount; index++)
        {
            operations[index] = new CreateLineOperation
            {
                OperationId = "line-" + index.ToString("D5", CultureInfo.InvariantCulture),
                Start = new CadPoint3(index, 0d, 0d),
                End = new CadPoint3(index + 1d, 1d, 0d),
                Layer = "0",
                LayerHandle = "10",
                OwnerSpaceHandle = "1F",
            };
        }

        return new CadOperationBatch
        {
            BatchId = "batch-lines-" + operationCount.ToString(CultureInfo.InvariantCulture),
            ThreadId = "thread-001",
            TurnId = "turn-001",
            Document = new CadDocumentRef
            {
                DocumentId = "document-001",
                DrawingFingerprint = SecurityHash.ComputeSha256Hex("drawing fingerprint"),
                Revision = 7,
            },
            SelectionSnapshotHash = SecurityHash.ComputeSha256Hex("empty selection snapshot"),
            RequiresSelectionRevalidation = false,
            DeclaredRisk = Codex.AutoCAD.Contracts.CadRiskLevel.ReversibleWrite,
            Operations = operations,
        };
    }

    private static CadOperationBatch CreateEraseBatch()
    {
        return new CadOperationBatch
        {
            BatchId = "batch-erase-001",
            ThreadId = "thread-001",
            TurnId = "turn-001",
            Document = new CadDocumentRef
            {
                DocumentId = "document-001",
                DrawingFingerprint = SecurityHash.ComputeSha256Hex("drawing fingerprint"),
                Revision = 7,
            },
            SelectionSnapshotHash = SecurityHash.ComputeSha256Hex("selected A1 B2"),
            RequiresSelectionRevalidation = true,
            DeclaredRisk = Codex.AutoCAD.Contracts.CadRiskLevel.DestructiveWrite,
            Operations =
            [
                new EraseEntitiesOperation
                {
                    OperationId = "erase-001",
                    Handles = ["A1", "B2"],
                },
            ],
        };
    }

    private static CadCheckpointEvidence CreateCheckpoint(string checkpointId) =>
        new(checkpointId, SecurityHash.ComputeSha256Hex("checkpoint artifact " + checkpointId));

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
        CadOperationBatch Batch,
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
