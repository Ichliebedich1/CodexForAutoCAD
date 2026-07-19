using Codex.AutoCAD.Contracts;

var specs = new[]
{
    new SpecCase("有效直线计划通过", ValidLineBatchPasses),
    new SpecCase("零长度直线被拒绝", ZeroLengthLineFails),
    new SpecCase("NaN坐标被拒绝", NonFiniteCoordinateFails),
    new SpecCase("低报风险被拒绝", UnderstatedRiskFails),
    new SpecCase("重复Handle被拒绝", DuplicateHandleFails),
    new SpecCase("目标现有图元必须重验选择快照", ExistingTargetsRequireSelectionRevalidation),
    new SpecCase("协议版本不匹配被拒绝", ProtocolMismatchFails),
    new SpecCase("缺失文档引用以验证失败返回", MissingDocumentFailsClosed),
    new SpecCase("图纸和选择摘要必须是64位十六进制", HashDigestsRequireSha256HexShape),
    new SpecCase("所有目标Handle必须是1到16位ASCII十六进制", TargetHandlesRequireBoundedAsciiHex),
    new SpecCase("目标Handle总数受批次级配额约束", TotalTargetHandlesAreBoundedPerBatch),
    new SpecCase("计划规范化UTF8字节数受硬配额约束", CanonicalPlanUtf8BytesAreBounded),
    new SpecCase("计划字符串拒绝控制字符危险格式和超长值", PlanStringsRejectUnsafeCharactersAndLength),
    new SpecCase("桥接直线提案拒绝受信边界外输入", BridgeLineProposalFailsClosed),
    new SpecCase("桥接回合限制提示词和上下文数量", BridgeTurnRequestIsBounded),
    new SpecCase("CTX-V1-001 六类上下文通过并产生冻结规范向量", CadContextV1CanonicalVectorIsFrozen),
    new SpecCase("CTX-V1-002 图元输入顺序不改变规范JSON和哈希", CadContextV1SortsEntitiesByNumericHandle),
    new SpecCase("CTX-V1-003 schema版本独立于IPC版本并严格拒绝漂移", CadContextV1SchemaVersionIsIndependent),
    new SpecCase("CTX-V1-004 强类型payload必须唯一且匹配图元类型", CadContextV1RequiresMatchingShape),
    new SpecCase("CTX-V1-005 坐标文本顶点和Unicode受硬限制", CadContextV1RejectsUnsafeValues),
    new SpecCase("CTX-V1-006 规范JSON保留中文转义文本且不含图名路径", CadContextV1PreservesPrivacyBoundary),
    new SpecCase("CTX-V1-007 浮点数格式在net45和net8间确定一致", CadContextV1NumberFormattingIsDeterministic),
    new SpecCase("BRIDGE-V1-001 回合请求和接受响应绑定精确上下文", BridgeTurnBindsExactContextIdentity),
    new SpecCase("BRIDGE-V1-002 能力协商只允许冻结方法事件和审批", BridgeCapabilitiesAreClosed),
    new SpecCase("BRIDGE-V1-003 审批只允许拒绝或一次允许", BridgeApprovalIsOneTimeOnly),
    new SpecCase("BRIDGE-V1-004 事件序列错误和结果身份均fail-closed", BridgeEventsFailClosed),
    new SpecCase("BRIDGE-V1-005 离线断线超时使用闭集错误语义", BridgeFailuresUseClosedErrorCodes),
};

var failed = 0;
foreach (var spec in specs)
{
    try
    {
        spec.Run();
        Console.WriteLine("PASS " + spec.Name);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + spec.Name + ": " + exception.Message);
    }
}

Console.WriteLine($"{specs.Length - failed}/{specs.Length} specs passed");
return failed == 0 ? 0 : 1;

static void ValidLineBatchPasses()
{
    var failures = CadContractValidator.Validate(CreateLineBatch());
    Equal(0, failures.Length, string.Join("; ", failures.Select(static failure => failure.Code)));
}

static void ZeroLengthLineFails()
{
    var batch = CreateLineBatch();
    var line = (CreateLineOperation)batch.Operations[0];
    line.End = new CadPoint3(line.Start.X, line.Start.Y, line.Start.Z);
    Contains(CadContractValidator.Validate(batch), "line_zero_length");
}

static void NonFiniteCoordinateFails()
{
    var batch = CreateLineBatch();
    ((CreateLineOperation)batch.Operations[0]).End.X = double.NaN;
    Contains(CadContractValidator.Validate(batch), "end_finite");
}

static void UnderstatedRiskFails()
{
    var batch = CreateLineBatch();
    batch.DeclaredRisk = CadRiskLevel.Preview;
    Contains(CadContractValidator.Validate(batch), "risk_understated");
}

static void DuplicateHandleFails()
{
    var batch = CreateLineBatch();
    batch.DeclaredRisk = CadRiskLevel.DestructiveWrite;
    batch.Operations =
    [
        new EraseEntitiesOperation
        {
            OperationId = "erase-1",
            Handles = ["1A", "1a"]
        }
    ];
    Contains(CadContractValidator.Validate(batch), "handle_duplicate");
}

static void ExistingTargetsRequireSelectionRevalidation()
{
    var batch = CreateLineBatch();
    batch.DeclaredRisk = CadRiskLevel.ReversibleWrite;
    batch.Operations =
    [
        new TransformEntitiesOperation
        {
            OperationId = "move-1",
            Handles = ["1A"],
            Translation = new CadPoint3(10, 0, 0),
        }
    ];
    batch.RequiresSelectionRevalidation = false;
    Contains(CadContractValidator.Validate(batch), "selection_revalidation_required");

    batch.RequiresSelectionRevalidation = true;
    Equal(0, CadContractValidator.Validate(batch).Length,
        "启用锁内选择重验后，合法的变换计划应通过。");
}

static void ProtocolMismatchFails()
{
    var batch = CreateLineBatch();
    batch.ProtocolVersion = ProtocolConstants.CurrentVersion + 1;
    Contains(CadContractValidator.Validate(batch), "protocol_version");
}

static void MissingDocumentFailsClosed()
{
    var batch = CreateLineBatch();
    batch.Document = null!;
    Contains(CadContractValidator.Validate(batch), "document_required");
}

static void HashDigestsRequireSha256HexShape()
{
    var batch = CreateLineBatch();
    batch.Document.DrawingFingerprint = new string('a', 63);
    Contains(CadContractValidator.Validate(batch), "drawing_fingerprint_format");

    batch.Document.DrawingFingerprint = new string('A', 64);
    batch.SelectionSnapshotHash = new string('b', 63) + "g";
    Contains(CadContractValidator.Validate(batch), "selection_snapshot_hash_format");

    batch.SelectionSnapshotHash = new string('B', 64);
    Equal(0, CadContractValidator.Validate(batch).Length,
        "大小写ASCII十六进制SHA-256摘要都应通过形状验证。");
}

static void TargetHandlesRequireBoundedAsciiHex()
{
    var batch = CreateLineBatch();
    batch.DeclaredRisk = CadRiskLevel.DestructiveWrite;
    batch.RequiresSelectionRevalidation = true;
    batch.Operations =
    [
        new EraseEntitiesOperation
        {
            OperationId = "erase-1",
            Handles = ["ZZ"],
        },
    ];
    Contains(CadContractValidator.Validate(batch), "handle_format");

    ((EraseEntitiesOperation)batch.Operations[0]).Handles = ["1234567890ABCDEF0"];
    Contains(CadContractValidator.Validate(batch), "handle_format");

    ((EraseEntitiesOperation)batch.Operations[0]).Handles = ["abcdef0123456789"];
    Equal(0, CadContractValidator.Validate(batch).Length,
        "1到16位大小写ASCII十六进制Handle应通过。");

    batch = CreateLineBatch();
    ((CreateLineOperation)batch.Operations[0]).LayerHandle = "1234567890ABCDEF0";
    Contains(CadContractValidator.Validate(batch), "layer_handle");
}

static void TotalTargetHandlesAreBoundedPerBatch()
{
    var handles = Enumerable.Range(1, ProtocolConstants.MaximumEntityHandlesPerOperation)
        .Select(static value => value.ToString("X"))
        .ToArray();
    var batch = CreateLineBatch();
    batch.DeclaredRisk = CadRiskLevel.DestructiveWrite;
    batch.RequiresSelectionRevalidation = true;
    batch.Operations = Enumerable.Range(0, 5)
        .Select(index => (CadOperation)new EraseEntitiesOperation
        {
            OperationId = "erase-" + index,
            Handles = (string[])handles.Clone(),
        })
        .ToArray();
    Equal(0, CadContractValidator.Validate(batch).Length,
        "50,000个合法目标Handle应处于批次上限内。");

    batch.Operations =
    [
        .. batch.Operations,
        new EraseEntitiesOperation
        {
            OperationId = "erase-5",
            Handles = (string[])handles.Clone(),
        },
    ];
    Contains(CadContractValidator.Validate(batch), "handles_batch_limit");
}

static void CanonicalPlanUtf8BytesAreBounded()
{
    var batch = CreateLineBatch();
    batch.Operations = Enumerable.Range(0, ProtocolConstants.MaximumOperationsPerBatch)
        .Select(index => (CadOperation)new CreateLineOperation
        {
            OperationId = "line-" + index.ToString("D5"),
            Start = new CadPoint3(index, 0, 0),
            End = new CadPoint3(index + 1, 1, 0),
            Layer = new string('层', 255),
            LayerHandle = "10",
            OwnerSpaceHandle = "1F",
        })
        .ToArray();

    Contains(CadContractValidator.Validate(batch), "plan_canonical_bytes_limit");
}

static void PlanStringsRejectUnsafeCharactersAndLength()
{
    var batch = CreateLineBatch();
    var line = (CreateLineOperation)batch.Operations[0];
    line.Layer = "中文图层-\U0001F642";
    Equal(0, CadContractValidator.Validate(batch).Length,
        "合法中文和配对代理项应被允许。");

    line.Layer = "layer\0hidden";
    Contains(CadContractValidator.Validate(batch), "string_control");

    line.Layer = "layer\u202Ehidden";
    Contains(CadContractValidator.Validate(batch), "string_format");

    line.Layer = new string('L', 256);
    Contains(CadContractValidator.Validate(batch), "string_length");

    line.Layer = "0";
    batch.BatchId = new string('B', 257);
    Contains(CadContractValidator.Validate(batch), "string_length");
}

static void BridgeLineProposalFailsClosed()
{
    var proposal = new CadLineProposalRequest
    {
        ProposalId = "proposal-1",
        ThreadId = "thread-1",
        TurnId = "turn-1",
        ToolCallId = "call-1",
        Start = new CadPoint3(0, 0, 0),
        End = new CadPoint3(100, 0, 0),
        Layer = "current",
    };
    Equal(0, AgentBridgeContractValidator.Validate(proposal).Length,
        "合法的未绑定直线提案应通过桥接边界验证。");

    proposal.End.X = double.PositiveInfinity;
    Contains(AgentBridgeContractValidator.Validate(proposal), "end_coordinate");
    proposal.End = new CadPoint3(0, 0, 0);
    Contains(AgentBridgeContractValidator.Validate(proposal), "line_zero_length");
    proposal.Start = null!;
    Contains(AgentBridgeContractValidator.Validate(proposal), "start_coordinate");
}

static void BridgeTurnRequestIsBounded()
{
    var context = CreateCadContextV1();
    context.Selection.Entities = Enumerable.Range(1, CadContextJsonV1Constants.MaximumEntities + 1)
        .Select(index => CreateLineContextEntity(
            index.ToString("X"),
            index,
            new string((char)('a' + (index % 6)), 64)))
        .ToArray();
    context.Selection.EntityCount = context.Selection.Entities.Length;
    var request = new AgentTurnStartRequest
    {
        ThreadId = "thread-1",
        ClientTurnId = "client-turn-1",
        Prompt = new string('x', (128 * 1024) + 1),
        Context = context,
        ContextSha256 = new string('c', 64),
    };

    var failures = AgentBridgeContractValidator.Validate(request);
    Contains(failures, "prompt_length");
    Contains(failures, "context_entity_limit");
}

static void CadContextV1CanonicalVectorIsFrozen()
{
    var context = CreateCadContextV1();
    var failures = CadContextJsonV1Validator.Validate(context);
    Equal(0, failures.Length, string.Join("; ", failures.Select(static failure => failure.Code)));

    var json = CadContextJsonV1Codec.SerializeCanonical(context);
    var bytes = CadContextJsonV1Codec.SerializeCanonicalUtf8(context);
    var sha256 = CadContextJsonV1Codec.ComputeCanonicalSha256(context);
    Console.WriteLine(
        "CAD_CONTEXT_JSON_V1 sha256=" + sha256
        + " bytes=" + bytes.Length);

    Equal("c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423", sha256,
        "CadContextJson v1的规范字节发生变化时必须显式升级schema或更新审计向量。" );
}

static void CadContextV1SortsEntitiesByNumericHandle()
{
    var context = CreateCadContextV1();
    var canonical = CadContextJsonV1Codec.SerializeCanonical(context);
    var hash = CadContextJsonV1Codec.ComputeCanonicalSha256(context);

    context.Selection.Entities = context.Selection.Entities.Reverse().ToArray();
    Equal(canonical, CadContextJsonV1Codec.SerializeCanonical(context),
        "输入数组顺序不应改变规范JSON。" );
    Equal(hash, CadContextJsonV1Codec.ComputeCanonicalSha256(context),
        "输入数组顺序不应改变规范哈希。" );

    var a = canonical.IndexOf("\"handle\":\"A\"", StringComparison.Ordinal);
    var b = canonical.IndexOf("\"handle\":\"B\"", StringComparison.Ordinal);
    var c = canonical.IndexOf("\"handle\":\"C\"", StringComparison.Ordinal);
    var twenty = canonical.IndexOf("\"handle\":\"20\"", StringComparison.Ordinal);
    Equal(true, a >= 0 && a < b && b < c && c < twenty,
        "图元必须按Handle数值而不是输入顺序或字符串顺序排序。" );
}

static void CadContextV1SchemaVersionIsIndependent()
{
    Equal(1, CadContextJsonV1Constants.SchemaVersion, "CadContextJson v1版本应固定为1。" );
    Equal(1, ProtocolConstants.CurrentVersion, "当前IPC版本基线应保持为1。" );

    var context = CreateCadContextV1();
    context.SchemaVersion = 2;
    Contains(CadContextJsonV1Validator.Validate(context), "context_schema_version");

    context = CreateCadContextV1();
    var request = new AgentTurnStartRequest
    {
        ContractVersion = AgentBridgeContractConstants.CurrentVersion + 1,
        ThreadId = "thread-1",
        ClientTurnId = "client-turn-1",
        Prompt = "分析当前选择。",
        Context = context,
        ContextSha256 = CadContextJsonV1Codec.ComputeCanonicalSha256(context),
    };
    Contains(AgentBridgeContractValidator.Validate(request), "agent_contract_version");
}

static void CadContextV1RequiresMatchingShape()
{
    var context = CreateCadContextV1();
    var line = FindEntity(context, CadContextEntityTypes.Line);
    line.Circle = new CadContextCircleV1
    {
        Center = new CadPoint3(0, 0, 0),
        Radius = 1,
        Normal = new CadPoint3(0, 0, 1),
    };
    Contains(CadContextJsonV1Validator.Validate(context), "context_shape_count");

    line.Circle = null;
    line.EntityType = CadContextEntityTypes.Circle;
    Contains(CadContextJsonV1Validator.Validate(context), "context_shape_mismatch");
}

static void CadContextV1RejectsUnsafeValues()
{
    var context = CreateCadContextV1();
    FindEntity(context, CadContextEntityTypes.Line).Line!.Start.X = double.NaN;
    Contains(CadContextJsonV1Validator.Validate(context), "context_point3");

    context = CreateCadContextV1();
    FindEntity(context, CadContextEntityTypes.MText).MText!.Text = "安全文本\u202E隐藏";
    Contains(CadContextJsonV1Validator.Validate(context), "context_text_unicode");

    context = CreateCadContextV1();
    FindEntity(context, CadContextEntityTypes.Circle).Circle!.Radius = 0;
    Contains(CadContextJsonV1Validator.Validate(context), "context_radius");

    context = CreateCadContextV1();
    FindEntity(context, CadContextEntityTypes.Polyline).Polyline!.Vertices =
        Enumerable.Range(0, CadContextJsonV1Constants.MaximumPolylineVertices + 1)
            .Select(index => new CadContextPolylineVertexV1
            {
                Position = new CadPoint2(index, index),
                Bulge = 0,
            })
            .ToArray();
    Contains(CadContextJsonV1Validator.Validate(context), "context_polyline_vertex_limit");
}

static void CadContextV1PreservesPrivacyBoundary()
{
    var json = CadContextJsonV1Codec.SerializeCanonical(CreateCadContextV1());
    ContainsText(json, "\"schema\":\"codex.autocad.cad-context\"");
    ContainsText(json, "\"entityType\":\"line\"");
    ContainsText(json, "\"radius\":12.5");
    ContainsText(json, "\"effectiveName\":\"动态块_A\"");
    ContainsText(json, "第一行\\n第二行\\t🙂");
    DoesNotContainText(json, "\"displayName\"");
    DoesNotContainText(json, "\"pathHash\"");
    DoesNotContainText(json, "\"path\"");
}

static void CadContextV1NumberFormattingIsDeterministic()
{
    var context = CreateCadContextV1();
    var line = FindEntity(context, CadContextEntityTypes.Line).Line!;
    line.Start.X = 0.1d;
    line.Start.Y = -0d;
    line.Start.Z = 0.0000001d;
    var json = CadContextJsonV1Codec.SerializeCanonical(context);
    ContainsText(json, "\"start\":{\"x\":0.10000000000000001,\"y\":0,\"z\":9.9999999999999995e-8}");
}

static void BridgeTurnBindsExactContextIdentity()
{
    var context = CreateCadContextV1();
    var contextHash = CadContextJsonV1Codec.ComputeCanonicalSha256(context);
    var request = new AgentTurnStartRequest
    {
        ThreadId = "thread-1",
        ClientTurnId = "client-turn-1",
        Prompt = "解释当前六个图元。",
        Context = context,
        ContextSha256 = contextHash,
    };
    Equal(0, AgentBridgeContractValidator.Validate(request).Length,
        "合法回合请求应通过。" );

    var response = new AgentTurnStartResponse
    {
        ThreadId = "thread-1",
        TurnId = "turn-1",
        AcceptedContextSha256 = contextHash,
    };
    Equal(0, AgentBridgeContractValidator.ValidateTurnAcceptance(request, response).Length,
        "响应必须回显精确上下文身份。" );

    response.AcceptedContextSha256 = new string('0', 64);
    Contains(AgentBridgeContractValidator.ValidateTurnAcceptance(request, response),
        "response_context_mismatch");

    request.ContextSha256 = new string('0', 64);
    Contains(AgentBridgeContractValidator.Validate(request), "context_hash_mismatch");
}

static void BridgeCapabilitiesAreClosed()
{
    var request = new AgentCapabilitiesRequest
    {
        ClientName = "host2016",
        ClientVersion = "1.0.0",
        HostTarget = "autocad-r20.1-net45-x64",
    };
    Equal(0, AgentBridgeContractValidator.Validate(request).Length,
        "合法能力协商请求应通过。" );

    var response = new AgentCapabilitiesResponse
    {
        AgentInstanceId = "agent-instance-1",
        Methods =
        [
            AgentBridgeMethods.GetCapabilities,
            AgentBridgeMethods.StartThread,
            AgentBridgeMethods.StartTurn,
            AgentBridgeMethods.InterruptTurn,
        ],
        EventKinds =
        [
            AgentBridgeEventKinds.ThreadStarted,
            AgentBridgeEventKinds.TurnStarted,
            AgentBridgeEventKinds.AssistantMessageDelta,
            AgentBridgeEventKinds.AssistantMessageCompleted,
            AgentBridgeEventKinds.TurnCompleted,
            AgentBridgeEventKinds.TurnFailed,
        ],
        ApprovalDecisions =
        [
            AgentBridgeApprovalDecisions.AllowOnce,
            AgentBridgeApprovalDecisions.DeclineAndContinue,
            AgentBridgeApprovalDecisions.DeclineAndCancelTurn,
        ],
        CadWriteAvailable = false,
    };
    Equal(0, AgentBridgeContractValidator.Validate(response).Length,
        "冻结白名单内的能力响应应通过。" );

    response.Methods = ["cad.execute.arbitrary"];
    Contains(AgentBridgeContractValidator.Validate(response), "capabilities_method");
    response.Methods = [AgentBridgeMethods.GetCapabilities];
    response.ApprovalDecisions = ["allow_for_session"];
    Contains(AgentBridgeContractValidator.Validate(response), "capabilities_approval");
}

static void BridgeApprovalIsOneTimeOnly()
{
    var request = new AgentApprovalResolveRequest
    {
        ThreadId = "thread-1",
        TurnId = "turn-1",
        ApprovalId = "approval-1",
        Decision = AgentBridgeApprovalDecisions.AllowOnce,
    };
    Equal(0, AgentBridgeContractValidator.Validate(request).Length,
        "一次允许应通过公共契约。" );

    request.Decision = "allow_for_session";
    Contains(AgentBridgeContractValidator.Validate(request), "approval_decision");
}

static void BridgeEventsFailClosed()
{
    var contextHash = CadContextJsonV1Codec.ComputeCanonicalSha256(CreateCadContextV1());
    var bridgeEvent = new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageDelta,
        EventId = "event-1",
        Sequence = 1,
        ThreadId = "thread-1",
        TurnId = "turn-1",
        MessageId = "message-1",
        Delta = "正在分析",
        ContextSha256 = contextHash,
        OccurredAtUtc = "2026-07-19T08:31:00.000Z",
    };
    Equal(0, AgentBridgeContractValidator.ValidateEventIdentity(
            bridgeEvent, "thread-1", "turn-1", contextHash).Length,
        "合法助手文本事件应通过身份绑定。" );

    bridgeEvent.Sequence = 0;
    Contains(AgentBridgeContractValidator.Validate(bridgeEvent), "event_sequence");
    bridgeEvent.Sequence = 1;
    Contains(AgentBridgeContractValidator.ValidateEventIdentity(
        bridgeEvent, "thread-1", "turn-1", new string('0', 64)), "event_context_mismatch");

    bridgeEvent.Kind = AgentBridgeEventKinds.TurnFailed;
    bridgeEvent.Error = "连接中断";
    bridgeEvent.ErrorCode = "unknown_failure";
    Contains(AgentBridgeContractValidator.Validate(bridgeEvent), "event_error_code");

    bridgeEvent.Kind = AgentBridgeEventKinds.ApprovalRequested;
    bridgeEvent.ApprovalId = "approval-1";
    bridgeEvent.AllowedDecisions = ["allow_for_session"];
    Contains(AgentBridgeContractValidator.Validate(bridgeEvent), "event_approval_decision");
}

static void BridgeFailuresUseClosedErrorCodes()
{
    foreach (var code in new[]
             {
                 AgentBridgeErrorCodes.Offline,
                 AgentBridgeErrorCodes.ConnectionLost,
                 AgentBridgeErrorCodes.Timeout,
             })
    {
        var failure = new AgentBridgeFailure
        {
            Code = code,
            Message = "Agent当前不可用。",
            Retryable = true,
            ThreadId = "thread-1",
            TurnId = "turn-1",
            OccurredAtUtc = "2026-07-19T08:31:00.000Z",
        };
        Equal(0, AgentBridgeContractValidator.Validate(failure).Length,
            "离线、断线和超时必须使用可解释的闭集错误码。" );
    }

    var unknown = new AgentBridgeFailure
    {
        Code = "fallback_to_unauthenticated_pipe",
        Message = "禁止回退。",
        OccurredAtUtc = "2026-07-19T08:31:00.000Z",
    };
    Contains(AgentBridgeContractValidator.Validate(unknown), "bridge_error_code");
}

static CadContextJsonV1 CreateCadContextV1()
{
    return new CadContextJsonV1
    {
        CapturedAtUtc = "2026-07-19T08:30:45.123Z",
        Document = new CadContextDocumentV1
        {
            DocumentId = "doc-session-01",
            DrawingFingerprint = new string('a', 64),
            Revision = 42,
            CurrentSpace = CadContextJsonV1Constants.ModelSpace,
            DrawingVersion = "AC1027",
            Units = "millimeters",
        },
        Selection = new CadContextSelectionV1
        {
            SnapshotHash = new string('b', 64),
            EntityCount = 6,
            Entities =
            [
                CreateLineContextEntity("20", 100.25, new string('1', 64)),
                new CadContextEntityV1
                {
                    Handle = "A",
                    OwnerSpaceHandle = "1F",
                    EntityType = CadContextEntityTypes.Circle,
                    StateHash = new string('2', 64),
                    Layer = "圆层",
                    Circle = new CadContextCircleV1
                    {
                        Center = new CadPoint3(1, 2, 3),
                        Radius = 12.5,
                        Normal = new CadPoint3(0, 0, 1),
                    },
                },
                new CadContextEntityV1
                {
                    Handle = "30",
                    OwnerSpaceHandle = "1F",
                    EntityType = CadContextEntityTypes.Polyline,
                    StateHash = new string('3', 64),
                    Layer = "轮廓层",
                    Polyline = new CadContextPolylineV1
                    {
                        Closed = true,
                        Elevation = 5,
                        Normal = new CadPoint3(0, 0, 1),
                        Vertices =
                        [
                            new CadContextPolylineVertexV1
                            {
                                Position = new CadPoint2(0, 0),
                                Bulge = 0,
                            },
                            new CadContextPolylineVertexV1
                            {
                                Position = new CadPoint2(10.5, 0),
                                Bulge = 0.25,
                            },
                            new CadContextPolylineVertexV1
                            {
                                Position = new CadPoint2(10.5, 20),
                                Bulge = -0.125,
                            },
                        ],
                    },
                },
                new CadContextEntityV1
                {
                    Handle = "B",
                    OwnerSpaceHandle = "1F",
                    EntityType = CadContextEntityTypes.DbText,
                    StateHash = new string('4', 64),
                    Layer = "文字层",
                    DbText = new CadContextDbTextV1
                    {
                        Text = "设备A",
                        Position = new CadPoint3(8, 9, 0),
                        Height = 2.5,
                        Rotation = 0.5,
                    },
                },
                new CadContextEntityV1
                {
                    Handle = "40",
                    OwnerSpaceHandle = "1F",
                    EntityType = CadContextEntityTypes.MText,
                    StateHash = new string('5', 64),
                    Layer = "说明层",
                    MText = new CadContextMTextV1
                    {
                        Text = "第一行\n第二行\t🙂",
                        Location = new CadPoint3(-2, 4.25, 0),
                        TextHeight = 3,
                        Rotation = 0,
                    },
                },
                new CadContextEntityV1
                {
                    Handle = "C",
                    OwnerSpaceHandle = "1F",
                    EntityType = CadContextEntityTypes.BlockReference,
                    StateHash = new string('6', 64),
                    Layer = "设备层",
                    BlockReference = new CadContextBlockReferenceV1
                    {
                        Position = new CadPoint3(-1, 2.5, 0),
                        Rotation = 1.5707963267948966,
                        Scale = new CadPoint3(1, -1, 2),
                        EffectiveName = "动态块_A",
                        IsDynamic = true,
                        IsExternalReference = false,
                    },
                },
            ],
        },
    };
}

static CadContextEntityV1 CreateLineContextEntity(
    string handle,
    double endX,
    string stateHash)
{
    return new CadContextEntityV1
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypes.Line,
        StateHash = stateHash,
        Layer = "结构层",
        Line = new CadContextLineV1
        {
            Start = new CadPoint3(0, -3.5, 0),
            End = new CadPoint3(endX, 7.125, 0),
        },
    };
}

static CadContextEntityV1 FindEntity(CadContextJsonV1 context, string entityType)
{
    return context.Selection.Entities.Single(entity =>
        string.Equals(entity.EntityType, entityType, StringComparison.Ordinal));
}

static CadOperationBatch CreateLineBatch()
{
    return new CadOperationBatch
    {
        BatchId = "batch-1",
        ThreadId = "thread-1",
        TurnId = "turn-1",
        Document = new CadDocumentRef
        {
            DocumentId = "doc-1",
            DrawingFingerprint = new string('a', 64),
            Revision = 7
        },
        SelectionSnapshotHash = new string('b', 64),
        DeclaredRisk = CadRiskLevel.ReversibleWrite,
        Operations =
        [
            new CreateLineOperation
            {
                OperationId = "line-1",
                Start = new CadPoint3(0, 0, 0),
                End = new CadPoint3(100, 0, 0),
                Layer = "0",
                LayerHandle = "10",
                OwnerSpaceHandle = "1F",
            }
        ]
    };
}

static void Contains(IEnumerable<CadValidationFailure> failures, string expectedCode)
{
    if (!failures.Any(failure => string.Equals(failure.Code, expectedCode, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("Expected failure code: " + expectedCode);
    }
}

static void ContainsText(string value, string expected)
{
    if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
    {
        throw new InvalidOperationException("Expected text: " + expected);
    }
}

static void DoesNotContainText(string value, string unexpected)
{
    if (value.IndexOf(unexpected, StringComparison.Ordinal) >= 0)
    {
        throw new InvalidOperationException("Unexpected text: " + unexpected);
    }
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}. {message}");
    }
}

sealed class SpecCase
{
    public SpecCase(string name, Action run)
    {
        Name = name;
        Run = run;
    }

    public string Name { get; }

    public Action Run { get; }
}
