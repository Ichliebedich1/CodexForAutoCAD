using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016;
using Codex.AutoCAD.Host2016.ReadOnlyContext;

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
    new SpecCase("CTX-V2-001 十九类强类型与未知占位产生冻结规范向量", CadContextJsonV2Specs.CanonicalVectorIsFrozen),
    new SpecCase("CTX-V2-002 图元和表格输入顺序不改变规范JSON", CadContextJsonV2Specs.EntityOrderingIsCanonical),
    new SpecCase("CTX-V2-003 混合选区显式报告完整性和计数", CadContextJsonV2Specs.MixedSelectionIsExplicit),
    new SpecCase("CTX-V2-004 每个图元必须且只能匹配一个payload", CadContextJsonV2Specs.PayloadMustBeUniqueAndMatching),
    new SpecCase("CTX-V2-005 复杂实体限额与未知原因fail-closed", CadContextJsonV2Specs.LimitsFailClosed),
    new SpecCase("CTX-V2-006 规范JSON保持隐私边界", CadContextJsonV2Specs.PrivacyBoundaryIsPreserved),
    new SpecCase("CTX-V2-007 v1与v2 schema版本明确分离", CadContextJsonV2Specs.SchemaVersionIsIndependent),
    new SpecCase("CTX-V2-101 19个强类型payload分别独立验证通过", CadContextJsonV2Specs.EachTypedPayloadValidatesIndividually),
    new SpecCase("CTX-V2-102 三种unsupported reason分别通过", CadContextJsonV2Specs.ThreeUnsupportedReasonsAreAccepted),
    new SpecCase("CTX-V2-103 计数不一致组合全部拒绝", CadContextJsonV2Specs.CountInconsistenciesAreRejected),
    new SpecCase("CTX-V2-104 实体上限和上限+1", CadContextJsonV2Specs.EntityLimitAndLimitPlusOne),
    new SpecCase("CTX-V2-105 多段线顶点上限和上限+1", CadContextJsonV2Specs.PolylineVertexLimitAndPlusOne),
    new SpecCase("CTX-V2-106 文本字符上限和上限+1", CadContextJsonV2Specs.TextLimitAndPlusOne),
    new SpecCase("CTX-V2-107 名称字符上限和上限+1", CadContextJsonV2Specs.NameLimitAndPlusOne),
    new SpecCase("CTX-V2-108 表格单元格上限和上限+1", CadContextJsonV2Specs.TableCellLimitAndPlusOne),
    new SpecCase("CTX-V2-109 填充环上限和上限+1", CadContextJsonV2Specs.HatchLoopLimitAndPlusOne),
    new SpecCase("CTX-V2-110 多重引线数量上限和上限+1", CadContextJsonV2Specs.MLeaderLineLimitAndPlusOne),
    new SpecCase("CTX-V2-111 Handle数值排序边界1A F 10 100", CadContextJsonV2Specs.HandleNumericSortBoundary),
    new SpecCase("CTX-V2-112 图元输入顺序不改变规范JSON", CadContextJsonV2Specs.EntityInputOrderDoesNotChangeCanonical),
    new SpecCase("CTX-V2-113 几何数组保持原始顺序不被排序", CadContextJsonV2Specs.GeometryArraysPreserveOriginalOrder),
    new SpecCase("CTX-V2-114 NaN Infinity超大坐标被拒绝", CadContextJsonV2Specs.RejectsUnsafeValuesNanInfinityMagnitude),
    new SpecCase("CTX-V2-115 控制字符和双向格式字符被拒绝", CadContextJsonV2Specs.RejectsControlCharactersAndBidiFormats),
    new SpecCase("CTX-V2-116 非法代理项被拒绝", CadContextJsonV2Specs.RejectsIllegalSurrogates),
    new SpecCase("CTX-V2-117 名称字段null被拒绝", CadContextJsonV2Specs.RejectsNullInNameField),
    new SpecCase("CTX-V2-118 文档字段null被拒绝", CadContextJsonV2Specs.RejectsNullInDocumentFields),
    new SpecCase("CTX-V2-119 空选区被拒绝", CadContextJsonV2Specs.SelectionMustNotBeEmpty),
    new SpecCase("CTX-V2-120 规范JSON不含敏感信息", CadContextJsonV2Specs.PrivacyBoundaryIsComprehensive),
    new SpecCase("CTX-V2-121 v1冻结向量2225字节SHA不变", CadContextJsonV2Specs.V1FrozenVectorIsUnchanged),
    new SpecCase("CTX-V2-122 v2规范向量多次运行确定", CadContextJsonV2Specs.V2CanonicalVectorIsDeterministic),
    new SpecCase("CTX-V2-123 浮点格式跨运行时确定一致", CadContextJsonV2Specs.NumberFormatIsDeterministicAcrossRuntimes),
    new SpecCase("CTX-V2-124 样条曲线总点数上限256通过257精确失败", CadContextJsonV2Specs.SplineTotalPointLimitAndPlusOne),
    new SpecCase("CTX-V2-125 引线顶点上限256通过257精确失败", CadContextJsonV2Specs.LeaderVertexLimitAndPlusOne),
    new SpecCase("CTX-V2-126 多重引线顶点总数上限256通过257精确失败", CadContextJsonV2Specs.MLeaderTotalVertexLimitAndPlusOne),
    new SpecCase("CTX-V2-127 冻结合法边界fixture确定性与纯ASCII输出", CadContextJsonV2Specs.FrozenLegalBoundaryFixtureIsDeterministic),
    new SpecCase("BRIDGE-V1-001 回合请求和接受响应绑定精确上下文", BridgeTurnBindsExactContextIdentity),
    new SpecCase("BRIDGE-V1-002 能力协商只允许冻结方法事件和审批", BridgeCapabilitiesAreClosed),
    new SpecCase("BRIDGE-V1-003 审批只允许拒绝或一次允许", BridgeApprovalIsOneTimeOnly),
    new SpecCase("BRIDGE-V1-004 事件序列错误和结果身份均fail-closed", BridgeEventsFailClosed),
    new SpecCase("BRIDGE-V1-005 离线断线超时使用闭集错误语义", BridgeFailuresUseClosedErrorCodes),
    new SpecCase("BRIDGE-V2-001 v2回合请求和接受响应绑定精确上下文v2", BridgeV2TurnBindsExactContextIdentity),
    new SpecCase("BRIDGE-V2-002 v2回合请求拒绝schema版本或哈希不匹配", BridgeV2TurnRejectsMismatch),
    new SpecCase("BRIDGE-V2-003 能力响应列出支持的CadContext schema版本", BridgeCapabilitiesListSupportedSchemas),
    new SpecCase("BRIDGE-V2-004 v1客户端在v2-capable AgentHost仍可协商", BridgeV1ClientNegotiatesWithV2CapableHost),
    new SpecCase("BRIDGE-V2-005 重复CadContext schema版本被拒绝", BridgeCapabilitiesRejectDuplicateSchemas),
    new SpecCase("BRIDGE-QUERY-001 反向整图查询绑定请求和回合且模型不能提供图纸身份", BridgeDrawingQueryBindsTrustedIdentity),
    new SpecCase("HOST16-V1-001 六类只读快照映射为精确公共契约字段", UnifiedHostMapsSixEntityTypes),
    new SpecCase("HOST16-V1-002 binary-v1选择和实体状态哈希保持绑定", UnifiedHostPreservesSelectionIdentity),
    new SpecCase("HOST16-V1-003 映射后canonical JSON确定且不含图名路径", UnifiedHostCanonicalJsonIsPrivateAndDeterministic),
    new SpecCase("HOST16-V1-004 可读摘要展示坐标图层文字半径顶点块名", UnifiedHostSummaryShowsRequiredFields),
    new SpecCase("HOST16-V1-005 不透明文档元数据不合规则fail-closed", UnifiedHostRejectsUnsafeDocumentMetadata),
    new SpecCase("INDEX-V1-001 五万对象DrawingIndex描述通过冻结契约", DrawingIndexContractsSpecs.FiftyThousandEntityDescriptorPasses),
    new SpecCase("INDEX-V1-002 DrawingIndex计数与终态不一致被拒绝", DrawingIndexContractsSpecs.DescriptorInvariantsFailClosed),
    new SpecCase("QUERY-V1-001 类型图层块文字范围和对象ID过滤正确", DrawingIndexContractsSpecs.FiltersAreCombined),
    new SpecCase("QUERY-V1-002 游标分页稳定且不重复不遗漏", DrawingIndexContractsSpecs.CursorPaginationIsStable),
    new SpecCase("QUERY-V1-003 游标绑定索引与查询形状但允许跨请求分页", DrawingIndexContractsSpecs.CursorIsBoundToQueryShapeAcrossRequestIdentities),
    new SpecCase("QUERY-V1-010 游标不能跨索引或revision使用", DrawingIndexContractsSpecs.CursorCannotCrossIndexOrRevision),
    new SpecCase("QUERY-V1-008 篡改游标偏移量被拒绝", DrawingIndexContractsSpecs.ForgedCursorOffsetIsRejected),
    new SpecCase("QUERY-V1-009 过期游标被拒绝", DrawingIndexContractsSpecs.ExpiredCursorIsRejected),
    new SpecCase("QUERY-V1-004 图纸revision变化返回stale而非旧结果", DrawingIndexContractsSpecs.RevisionMismatchReturnsStale),
    new SpecCase("QUERY-V1-005 partial与limited完整性不被伪装", DrawingIndexContractsSpecs.PartialAndLimitedStayExplicit),
    new SpecCase("QUERY-V1-006 内存预算在加入实体前fail-closed", DrawingIndexContractsSpecs.AccumulatorHonorsMemoryBudget),
    new SpecCase("QUERY-V1-007 五万对象索引可过滤并分页", DrawingIndexContractsSpecs.FiftyThousandEntitiesCanBeQueried),
    new SpecCase("INDEX-V1-003 完成状态不会把占位或预算超限伪装为完整", DrawingIndexContractsSpecs.CompletionPolicyIsFailClosed),
    new SpecCase("INDEX-V1-004 图纸身份或revision变化使索引失效", DrawingIndexContractsSpecs.IdentityPolicyRejectsStaleIndex),
    new SpecCase("INDEX-V1-005 重复实体令牌在累积与响应层均fail-closed", DrawingIndexContractsSpecs.DuplicateObjectTokensFailClosed),
    new SpecCase("INDEX-V1-006 原始Handle形状不能作为查询实体令牌", DrawingIndexContractsSpecs.RawHandleShapedObjectTokensFailClosed),
    new SpecCase("INDEX-M3-001 占位原因和实际类型统计有界且不混入实体数据", DrawingIndexContractsSpecs.ReadIssueStatisticsStayStructuredAndBounded),
    new SpecCase("INDEX-M3-002 块详情有界深拷贝且不定义Xref路径字段", DrawingIndexContractsSpecs.BlockDetailsAreBoundedDeepCopiedAndPathFree),
    new SpecCase("INDEX-M3-003 重复块定义按实例路径计数且循环受限", DrawingIndexContractsSpecs.RepeatedBlockDefinitionsPreserveInstancePaths),
    new SpecCase("INDEX-M3-004 块定义摘要缓存仅保存托管快照并隔离实例结果", DrawingIndexContractsSpecs.BlockDefinitionSummaryCacheIsCloneSafe),
    new SpecCase("INDEX-M3-005 动态属性保留前八项但统计真实总数", DrawingIndexContractsSpecs.DynamicPropertyCountContinuesPastRetainedLimit),
    new SpecCase("INDEX-M3-006 单实体读取遵守Idle切片预算", DrawingIndexContractsSpecs.BlockReadBudgetExpiresAtSliceBoundary),
    new SpecCase("INDEX-M3-007 预算过期的块定义摘要不会污染会话缓存", DrawingIndexContractsSpecs.BudgetExpiredBlockDefinitionSummaryIsNotCached),
    new SpecCase("INDEX-M3-008 高价值对象保持可查询且明确受限", DrawingIndexContractsSpecs.HighValueLimitedTypesStayQueryableAndExplicit),
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

static void BridgeV2TurnBindsExactContextIdentity()
{
    var context = CreateCadContextV2();
    var contextHash = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
    var request = new AgentTurnStartV2Request
    {
        ThreadId = "thread-1",
        ClientTurnId = "client-turn-v2-1",
        Prompt = "解释当前v2选区。",
        ContextV2 = context,
        ContextV2Sha256 = contextHash,
    };
    Equal(0, AgentBridgeContractValidator.Validate(request).Length,
        "合法v2回合请求应通过。");

    var response = new AgentTurnStartV2Response
    {
        ThreadId = "thread-1",
        TurnId = "turn-v2-1",
        AcceptedContextV2Sha256 = contextHash,
    };
    Equal(0, AgentBridgeContractValidator.ValidateTurnV2Acceptance(request, response).Length,
        "v2响应必须回显精确上下文v2身份。");

    response.AcceptedContextV2Sha256 = new string('0', 64);
    Contains(AgentBridgeContractValidator.ValidateTurnV2Acceptance(request, response),
        "response_context_v2_mismatch");
}

static void BridgeV2TurnRejectsMismatch()
{
    var context = CreateCadContextV2();
    var request = new AgentTurnStartV2Request
    {
        ThreadId = "thread-1",
        ClientTurnId = "client-turn-v2-2",
        Prompt = "测试v2拒绝。",
        ContextV2 = context,
        ContextV2Sha256 = new string('0', 64),
    };
    Contains(AgentBridgeContractValidator.Validate(request), "context_v2_hash_mismatch");

    request.ContextV2Sha256 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
    request.ContextV2!.SchemaVersion = 1;
    Contains(AgentBridgeContractValidator.Validate(request), "context_v2_schema_version");

    request.ContextV2 = null;
    request.ContextV2Sha256 = new string('a', 64);
    Contains(AgentBridgeContractValidator.Validate(request), "context_v2_hash_without_context");
}

static void BridgeCapabilitiesListSupportedSchemas()
{
    var response = new AgentCapabilitiesResponse
    {
        AgentInstanceId = "agent-v2",
        Methods =
        [
            AgentBridgeMethods.GetCapabilities,
            AgentBridgeMethods.StartThread,
            AgentBridgeMethods.StartTurn,
            AgentBridgeMethods.StartTurnV2,
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
        SupportedCadContextSchemas =
        [
            new Codex.AutoCAD.Contracts.CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV1Constants.Schema,
                SchemaVersion = 1,
            },
            new Codex.AutoCAD.Contracts.CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV2Constants.Schema,
                SchemaVersion = 2,
            },
        ],
        CadWriteAvailable = false,
    };
    Equal(0, AgentBridgeContractValidator.Validate(response).Length,
        "包含v1和v2的schema列表应通过。");

    response.SupportedCadContextSchemas =
    [
        new Codex.AutoCAD.Contracts.CadContextSchemaVersionEntry
        {
            Schema = CadContextJsonV2Constants.Schema,
            SchemaVersion = 2,
        },
    ];
    Contains(AgentBridgeContractValidator.Validate(response), "capabilities_schemas_v1_required");

    response.SupportedCadContextSchemas = [];
    Contains(AgentBridgeContractValidator.Validate(response), "capabilities_schemas_required");
}

static void BridgeV1ClientNegotiatesWithV2CapableHost()
{
    var response = new AgentCapabilitiesResponse
    {
        AgentInstanceId = "agent-v2-compat",
        CadContextSchema = CadContextJsonV1Constants.Schema,
        CadContextSchemaVersion = 1,
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
        SupportedCadContextSchemas =
        [
            new Codex.AutoCAD.Contracts.CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV1Constants.Schema,
                SchemaVersion = 1,
            },
            new Codex.AutoCAD.Contracts.CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV2Constants.Schema,
                SchemaVersion = 2,
            },
        ],
        CadWriteAvailable = false,
    };
    Equal(0, AgentBridgeContractValidator.Validate(response).Length,
        "v1客户端看到v2-capable能力响应应通过。");
    Equal(CadContextJsonV1Constants.Schema, response.CadContextSchema,
        "v1客户端应能读取v1 schema字段。");
    Equal(2, response.SupportedCadContextSchemas.Length,
        "v2-capable host应列出两个schema版本。");
}

static void BridgeCapabilitiesRejectDuplicateSchemas()
{
    var response = new AgentCapabilitiesResponse
    {
        AgentInstanceId = "agent-v2-duplicate",
        Methods =
        [
            AgentBridgeMethods.GetCapabilities,
            AgentBridgeMethods.StartThread,
            AgentBridgeMethods.StartTurn,
            AgentBridgeMethods.StartTurnV2,
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
        SupportedCadContextSchemas =
        [
            new CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV1Constants.Schema,
                SchemaVersion = CadContextJsonV1Constants.SchemaVersion,
            },
            new CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV1Constants.Schema,
                SchemaVersion = CadContextJsonV1Constants.SchemaVersion,
            },
        ],
        CadWriteAvailable = false,
    };

    Contains(
        AgentBridgeContractValidator.Validate(response),
        "capabilities_schema_duplicate");
}

static void BridgeDrawingQueryBindsTrustedIdentity()
{
    var request = new AgentDrawingQueryRequest
    {
        RequestId = "request-query-1",
        ThreadId = "thread-query-1",
        TurnId = "turn-query-1",
        ToolCallId = "call-query-1",
        QueryId = "query-host-1",
        Filter = new CadQueryFilter
        {
            Layers = new[] { "AI" },
            IncludeUnsupported = false,
        },
        PageSize = 25,
    };
    Equal(0, AgentBridgeContractValidator.Validate(request).Length,
        "合法反向整图查询应通过Bridge契约。");
    Equal(null, typeof(AgentDrawingQueryRequest).GetProperty("IndexId"),
        "模型侧Bridge请求不得暴露indexId。");
    Equal(null, typeof(AgentDrawingQueryRequest).GetProperty("DocumentId"),
        "模型侧Bridge请求不得暴露documentId。");
    Equal(null, typeof(AgentDrawingQueryRequest).GetProperty("DocumentRevision"),
        "模型侧Bridge请求不得暴露documentRevision。");

    var response = new AgentDrawingQueryResponse
    {
        RequestId = request.RequestId,
        ThreadId = request.ThreadId,
        TurnId = request.TurnId,
        ToolCallId = request.ToolCallId,
        QueryId = request.QueryId,
        Query = new CadQueryResponse
        {
            IndexId = "idx-host-1",
            DocumentId = "doc-host-1",
            DocumentRevision = 9,
            QueryId = request.QueryId,
            Status = CadQueryStatuses.Ok,
            Complete = true,
            TotalMatches = 0,
            ReturnedCount = 0,
        },
    };
    Equal(0, AgentBridgeContractValidator.ValidateDrawingQueryResponse(request, response).Length,
        "合法反向查询响应应绑定全部请求身份。");

    response.RequestId = "request-other";
    Contains(
        AgentBridgeContractValidator.ValidateDrawingQueryResponse(request, response),
        "drawing_query_response_request_mismatch");

    response.RequestId = request.RequestId;
    response.ThreadId = "thread-other";
    Contains(
        AgentBridgeContractValidator.ValidateDrawingQueryResponse(request, response),
        "drawing_query_response_thread_mismatch");

    response.ThreadId = request.ThreadId;
    response.TurnId = "turn-other";
    Contains(
        AgentBridgeContractValidator.ValidateDrawingQueryResponse(request, response),
        "drawing_query_response_turn_mismatch");

    response.TurnId = request.TurnId;
    response.ToolCallId = "call-other";
    Contains(
        AgentBridgeContractValidator.ValidateDrawingQueryResponse(request, response),
        "drawing_query_response_tool_call_mismatch");

    response.ToolCallId = request.ToolCallId;
    response.QueryId = "query-other";
    Contains(
        AgentBridgeContractValidator.ValidateDrawingQueryResponse(request, response),
        "drawing_query_response_query_mismatch");

    response.QueryId = request.QueryId;
    response.Query.QueryId = "query-payload-other";
    Contains(
        AgentBridgeContractValidator.ValidateDrawingQueryResponse(request, response),
        "drawing_query_response_payload_mismatch");
}

static void UnifiedHostMapsSixEntityTypes()
{
    var context = CreateUnifiedHostContext();
    Equal(6, context.Selection.EntityCount, "统一Host必须映射完整六类选择。" );
    Equal("A", context.Selection.Entities[0].Handle, "Handle必须按数值排序。" );
    Equal(CadContextEntityTypes.Circle, context.Selection.Entities[0].EntityType,
        "首个图元应为Circle。" );
    Equal(12.5d, context.Selection.Entities[0].Circle!.Radius, "圆半径必须保真。" );
    Equal("圆层", context.Selection.Entities[0].Layer, "图层必须保真。" );

    var line = context.Selection.Entities.Single(entity =>
        entity.EntityType == CadContextEntityTypes.Line);
    Equal(100.25d, line.Line!.End.X, "直线坐标必须保真。" );

    var polyline = context.Selection.Entities.Single(entity =>
        entity.EntityType == CadContextEntityTypes.Polyline);
    Equal(3, polyline.Polyline!.Vertices.Length, "多段线顶点必须完整映射。" );
    Equal(0.25d, polyline.Polyline.Vertices[1].Bulge, "多段线bulge必须保真。" );

    var dbText = context.Selection.Entities.Single(entity =>
        entity.EntityType == CadContextEntityTypes.DbText);
    Equal("阀门 A-01", dbText.DbText!.Text, "单行文字必须保真。" );

    var mText = context.Selection.Entities.Single(entity =>
        entity.EntityType == CadContextEntityTypes.MText);
    Equal("第一行\n第二行", mText.MText!.Text, "多行文字必须保留换行。" );

    var block = context.Selection.Entities.Single(entity =>
        entity.EntityType == CadContextEntityTypes.BlockReference);
    Equal("PUMP_01", block.BlockReference!.EffectiveName, "有效块名必须保真。" );
    Equal(true, block.BlockReference.IsDynamic, "动态块标记必须保真。" );
}

static void UnifiedHostPreservesSelectionIdentity()
{
    var selection = CreateUnifiedHostSelection();
    var context = CadContextJsonMapper.Build(
        CreateUnifiedHostDocumentMetadata(),
        selection,
        DateTimeOffset.Parse("2026-07-19T12:34:56.789Z",
            System.Globalization.CultureInfo.InvariantCulture));

    Equal(selection.SnapshotHash, context.Selection.SnapshotHash,
        "统一Host不得重新发明选择哈希。" );
    Equal(selection.Entities.Count, context.Selection.Entities.Length,
        "映射不得丢失实体。" );
    for (var index = 0; index < selection.Entities.Count; index++)
    {
        Equal(selection.Entities[index].StateHash, context.Selection.Entities[index].StateHash,
            "实体状态哈希必须逐项保持。" );
    }
}

static void UnifiedHostCanonicalJsonIsPrivateAndDeterministic()
{
    var first = CreateUnifiedHostContext();
    var second = CreateUnifiedHostContext();
    var firstJson = CadContextJsonV1Codec.SerializeCanonical(first);
    var secondJson = CadContextJsonV1Codec.SerializeCanonical(second);
    Equal(firstJson, secondJson, "同一只读快照必须生成相同canonical JSON。" );
    Equal(
        CadContextJsonV1Codec.ComputeCanonicalSha256(first),
        CadContextJsonV1Codec.ComputeCanonicalSha256(second),
        "同一只读快照必须生成相同上下文哈希。" );
    Equal(0, CadContextJsonV1Validator.Validate(first).Length,
        "统一Host映射结果必须通过冻结公共契约。" );

    DoesNotContainText(firstJson, "Drawing1");
    DoesNotContainText(firstJson, "C:\\\\");
    DoesNotContainText(firstJson, "documentName");
    DoesNotContainText(firstJson, "documentPath");
    DoesNotContainText(firstJson, "pathHash");

    Equal(2198, System.Text.Encoding.UTF8.GetByteCount(firstJson),
        "统一Host映射固定向量字节数漂移。" );
    Equal("e57ebb86e98216a501e8de0c702fe64e65a3db9e391be4a7cc7a6cfdcac71e18",
        CadContextJsonV1Codec.ComputeCanonicalSha256(first),
        "统一Host映射固定向量SHA-256漂移。" );
    Console.WriteLine("HOST16_CONTEXT_BYTES="
        + System.Text.Encoding.UTF8.GetByteCount(firstJson));
    Console.WriteLine("HOST16_CONTEXT_SHA256="
        + CadContextJsonV1Codec.ComputeCanonicalSha256(first));
}

static void UnifiedHostSummaryShowsRequiredFields()
{
    var context = CreateUnifiedHostContext();
    var json = CadContextJsonV1Codec.SerializeCanonical(context);
    var summary = CadContextJsonMapper.BuildReadableSummary(
        context,
        CadContextJsonV1Codec.ComputeCanonicalSha256(context),
        System.Text.Encoding.UTF8.GetByteCount(json));

    ContainsText(summary, "图层：圆层");
    ContainsText(summary, "半径：12.5");
    ContainsText(summary, "起点：(0, 0, 0)");
    ContainsText(summary, "顶点：3");
    ContainsText(summary, "文字：阀门 A-01");
    ContainsText(summary, "多行文字：第一行 ↵ 第二行");
    ContainsText(summary, "块名：PUMP_01");
}

static void UnifiedHostRejectsUnsafeDocumentMetadata()
{
    var context = CadContextJsonMapper.Build(
        new CadContextDocumentMetadata(
            "C:\\secret\\Drawing1.dwg",
            new string('a', 64),
            42,
            CadContextJsonV1Constants.ModelSpace,
            "AC1027",
            "millimeters"),
        CreateUnifiedHostSelection(),
        DateTimeOffset.Parse("2026-07-19T12:34:56.789Z",
            System.Globalization.CultureInfo.InvariantCulture));
    Contains(CadContextJsonV1Validator.Validate(context), "context_document_id");
}

static CadContextJsonV1 CreateUnifiedHostContext()
{
    return CadContextJsonMapper.Build(
        CreateUnifiedHostDocumentMetadata(),
        CreateUnifiedHostSelection(),
        DateTimeOffset.Parse("2026-07-19T12:34:56.789Z",
            System.Globalization.CultureInfo.InvariantCulture));
}

static CadContextDocumentMetadata CreateUnifiedHostDocumentMetadata()
{
    return new CadContextDocumentMetadata(
        "doc-unified-001",
        new string('a', 64),
        42,
        CadContextJsonV1Constants.ModelSpace,
        "AC1027",
        "millimeters");
}

static ContextSelectionSnapshot CreateUnifiedHostSelection()
{
    var vertices = new List<ContextPolylineVertex>
    {
        new ContextPolylineVertex(new ContextPoint2(0, 0), 0),
        new ContextPolylineVertex(new ContextPoint2(10.5, 0), 0.25),
        new ContextPolylineVertex(new ContextPoint2(10.5, 20), -0.125),
    };

    return CanonicalSelectionHash.Build(new List<ContextEntityDraft>
    {
        UnifiedHostDraft(
            ContextEntityKind.BlockReference,
            0x60,
            "设备层",
            block: new ContextBlockData(
                new ContextPoint3(50, 60, 0),
                0.5,
                new ContextVector3(1, 2, 1),
                "PUMP_01",
                true,
                false)),
        UnifiedHostDraft(
            ContextEntityKind.Line,
            0x20,
            "线层",
            line: new ContextLineData(
                new ContextPoint3(0, 0, 0),
                new ContextPoint3(100.25, 5, 0))),
        UnifiedHostDraft(
            ContextEntityKind.Circle,
            0x0A,
            "圆层",
            circle: new ContextCircleData(
                new ContextPoint3(1, 2, 3),
                12.5,
                new ContextVector3(0, 0, 1))),
        UnifiedHostDraft(
            ContextEntityKind.Polyline,
            0x30,
            "轮廓层",
            polyline: new ContextPolylineData(
                true,
                5,
                new ContextVector3(0, 0, 1),
                vertices)),
        UnifiedHostDraft(
            ContextEntityKind.DbText,
            0x40,
            "文字层",
            dbText: new ContextDbTextData(
                "阀门 A-01",
                new ContextPoint3(7, 8, 0),
                2.5,
                0.25)),
        UnifiedHostDraft(
            ContextEntityKind.MText,
            0x50,
            "说明层",
            mText: new ContextMTextData(
                "第一行\n第二行",
                new ContextPoint3(9, 10, 0),
                3.5,
                0.75)),
    });
}

static ContextEntityDraft UnifiedHostDraft(
    ContextEntityKind kind,
    ulong handle,
    string layer,
    ContextLineData? line = null,
    ContextCircleData? circle = null,
    ContextPolylineData? polyline = null,
    ContextDbTextData? dbText = null,
    ContextMTextData? mText = null,
    ContextBlockData? block = null)
{
    return new ContextEntityDraft(
        kind,
        handle,
        0x1F,
        layer,
        line!,
        circle!,
        polyline!,
        dbText!,
        mText!,
        block!);
}

static CadContextJsonV2 CreateCadContextV2()
{
    return new CadContextJsonV2
    {
        CapturedAtUtc = "2026-07-21T04:00:00.000Z",
        Document = new CadContextDocumentV2
        {
            DocumentId = "doc-v2-spec",
            DrawingFingerprint = new string('a', 64),
            Revision = 1,
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
                    Handle = "10",
                    OwnerSpaceHandle = "1F",
                    EntityType = CadContextEntityTypesV2.Line,
                    StateHash = new string('c', 64),
                    Layer = "结构层",
                    Line = new CadContextLineV2
                    {
                        Start = new CadPoint3(0, 0, 0),
                        End = new CadPoint3(100.25, 20.5, 0),
                    },
                },
            ],
        },
    };
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
