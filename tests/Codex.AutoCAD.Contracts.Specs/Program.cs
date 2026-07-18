using Codex.AutoCAD.Contracts;

var specs = new (string Name, Action Run)[]
{
    ("有效直线计划通过", ValidLineBatchPasses),
    ("零长度直线被拒绝", ZeroLengthLineFails),
    ("NaN坐标被拒绝", NonFiniteCoordinateFails),
    ("低报风险被拒绝", UnderstatedRiskFails),
    ("重复Handle被拒绝", DuplicateHandleFails),
    ("目标现有图元必须重验选择快照", ExistingTargetsRequireSelectionRevalidation),
    ("协议版本不匹配被拒绝", ProtocolMismatchFails),
    ("缺失文档引用以验证失败返回", MissingDocumentFailsClosed),
    ("图纸和选择摘要必须是64位十六进制", HashDigestsRequireSha256HexShape),
    ("所有目标Handle必须是1到16位ASCII十六进制", TargetHandlesRequireBoundedAsciiHex),
    ("目标Handle总数受批次级配额约束", TotalTargetHandlesAreBoundedPerBatch),
    ("计划规范化UTF8字节数受硬配额约束", CanonicalPlanUtf8BytesAreBounded),
    ("计划字符串拒绝控制字符危险格式和超长值", PlanStringsRejectUnsafeCharactersAndLength),
    ("桥接直线提案拒绝受信边界外输入", BridgeLineProposalFailsClosed),
    ("桥接回合限制提示词和上下文数量", BridgeTurnRequestIsBounded),
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
    var request = new AgentTurnStartRequest
    {
        ThreadId = "thread-1",
        ClientTurnId = "client-turn-1",
        Prompt = new string('x', (128 * 1024) + 1),
        Context = new CadContextEnvelope
        {
            Selection = new CadSelectionSnapshot
            {
                Entities = Enumerable.Range(0, ProtocolConstants.MaximumContextEntities + 1)
                    .Select(static index => new CadEntityRef { Handle = index.ToString("X") })
                    .ToArray(),
            },
        },
    };

    var failures = AgentBridgeContractValidator.Validate(request);
    Contains(failures, "prompt_length");
    Contains(failures, "context_entity_limit");
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

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}. {message}");
    }
}
