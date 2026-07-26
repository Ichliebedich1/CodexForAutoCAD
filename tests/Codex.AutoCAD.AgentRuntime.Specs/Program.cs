using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.AutoCAD.AgentRuntime;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AppServer.Protocol;
using CadContextEntityTypesV2 = Codex.AutoCAD.Contracts.CadContextEntityTypesV2;
using CadQueryBlockAttribute = Codex.AutoCAD.Contracts.CadQueryBlockAttribute;
using CadQueryBlockDetailStatuses = Codex.AutoCAD.Contracts.CadQueryBlockDetailStatuses;
using CadQueryBlockDetails = Codex.AutoCAD.Contracts.CadQueryBlockDetails;
using CadQueryDynamicBlockProperty = Codex.AutoCAD.Contracts.CadQueryDynamicBlockProperty;
using CadQueryDynamicValueKinds = Codex.AutoCAD.Contracts.CadQueryDynamicValueKinds;
using CadQueryEntity = Codex.AutoCAD.Contracts.CadQueryEntity;
using CadQueryReadStatuses = Codex.AutoCAD.Contracts.CadQueryReadStatuses;
using CadQueryResponse = Codex.AutoCAD.Contracts.CadQueryResponse;
using CadQueryStatuses = Codex.AutoCAD.Contracts.CadQueryStatuses;
using AgentPolicyErrorCodes = Codex.AutoCAD.Contracts.AgentPolicyErrorCodes;
using AgentPolicyLayerDocument = Codex.AutoCAD.Contracts.AgentPolicyLayerDocument;
using AgentPolicyLayers = Codex.AutoCAD.Contracts.AgentPolicyLayers;
using AgentPolicyResolver = Codex.AutoCAD.Contracts.AgentPolicyResolver;
using AgentReasoningEfforts = Codex.AutoCAD.Contracts.AgentReasoningEfforts;
using DiagnosticDataClassification = Codex.AutoCAD.Contracts.DiagnosticDataClassification;
using DiagnosticRedactionKinds = Codex.AutoCAD.Contracts.DiagnosticRedactionKinds;
using ResolvedAgentPolicy = Codex.AutoCAD.Contracts.ResolvedAgentPolicy;

var specs = new (string Name, Func<Task> Run)[]
{
    ("POLICY-M41-020 未配置策略时危险模型字符串不出站", PolicyBlocksUnsafeModelStringWithoutPolicy),
    ("POLICY-M41-021 白名单外模型在出站前被拒绝", PolicyBlocksModelOutsideAllowList),
    ("POLICY-M41-022 管理员锁定后偏离默认值的模型被拒绝", PolicyBlocksDivergentModelWhenLocked),
    ("POLICY-M41-023 实际下发到wire的是策略接受值", PolicyAcceptedModelIsWhatReachesTheWire),
    ("新建与恢复线程使用安全默认值", ThreadRequestsUseSafeDefaults),
    ("启动轮次生成0.144.5输入结构", TurnStartUsesExpectedWireShape),
    ("中断轮次生成精确请求", InterruptUsesExpectedWireShape),
    ("新线程注册cad声明式提案工具", ThreadRegistersCadProposalTool),
    ("只读图纸查询工具可独立于CAD写入提案注册", ThreadRegistersOnlyReadOnlyDrawingQueryTool),
    ("只读图纸查询通过Broker执行且隐藏Host绑定身份", CadDrawingQueryUsesBrokerAndHidesBindingIdentity),
    ("只读图纸查询拒绝原始Handle形状对象令牌", CadDrawingQueryRejectsRawHandleToken),
    ("cad动态工具仅在Broker确认落盘后成功", CadDynamicToolRequiresAppliedTerminalResult),
    ("cad提案事件是与Broker隔离的深不可变快照", CadProposalEventIsDeeplyIsolatedFromBroker),
    ("cad动态工具未连接Broker时默认失败", CadDynamicToolWithoutBrokerFailsClosed),
    ("cad动态工具拒绝未绑定活动轮次的调用", CadDynamicToolRejectsInactiveTurn),
    ("cad动态工具拒绝伪造turn started通知授权", CadDynamicToolRejectsForgedTurnStartedNotification),
    ("cad动态工具终态撤销授权且started通知不可恢复", CadDynamicToolTerminalNotificationRevokesAuthorization),
    ("cad动态工具拒绝与失败终态均返回失败", CadDynamicToolRejectsNonAppliedOutcomes),
    ("cad动态工具拒绝Broker所有终态的身份错配", CadDynamicToolRejectsMismatchedBrokerIdentity),
    ("cad动态工具Broker超时返回失败", CadDynamicToolTimeoutFailsClosed),
    ("cad动态工具Broker忽略取消时仍硬超时且晚到结果无效", CadDynamicToolHardTimeoutIgnoresLateBrokerResult),
    ("cad动态工具回合终态取消在途Broker且晚到Applied无效", CadDynamicToolTerminalCancelsInFlightBroker),
    ("cad动态工具永不结束Broker不阻塞超时与释放", CadDynamicToolNeverEndingBrokerDoesNotBlockDisposal),
    ("cad动态工具晚到Broker fault被观察且不改变终态", CadDynamicToolLateBrokerFaultIsObserved),
    ("cad动态工具按thread-turn-call幂等", CadDynamicToolIsIdempotent),
    ("cad动态工具注册表满时保留旧call tombstone并fail closed", CadCallRegistryFullPreservesReplayTombstone),
    ("cad动态工具终态清理注册表并拒绝旧callId重放", CadCallRegistryClearsOnlyAtTurnTerminal),
    ("cad动态工具拒绝文档绑定字段", CadDynamicToolRejectsDocumentBinding),
    ("cad动态工具畸形参数被隔离", MalformedCadDynamicToolIsIsolated),
    ("cad动态工具校验诊断出站前脱敏", CadDynamicToolValidationDiagnosticsAreSanitized),
    ("工作区写权限必须绑定受信根", WorkspaceWriteRequiresManagedRoot),
    ("受信工作区拒绝越界和ADS路径", ManagedWorkspaceRejectsEscapeAndAds),
    ("本地文件输入默认关闭", LocalFileInputsAreDisabledByDefault),
    ("消息与item通知投影为强类型事件", MessageAndItemNotificationsAreProjected),
    ("工具进度通知投影为强类型事件", ToolProgressNotificationsAreProjected),
    ("轮次状态通知投影完整终态", TurnStateNotificationsAreProjected),
    ("失败轮次公共事件不泄露Provider诊断", FailedTurnPublicEventDoesNotLeakProviderDiagnostics),
    ("审批请求投影并转发决定", ApprovalRequestsAreProjectedAndForwarded),
    ("畸形通知被隔离", MalformedNotificationIsIsolated),
    ("事件观察者故障被隔离", EventObserverFailureIsIsolated),
    ("运行时诊断事件不保留原始异常图", RuntimeDiagnosticEventsDoNotRetainRawExceptions),
    ("观察者故障诊断不保留原始Agent事件", ObserverFailureDiagnosticsDoNotRetainRawAgentEvent),
    ("运行时公开记录字符串不泄露路径提示词或Provider标识", RuntimePublicRecordStringsAreSafe),
};

var failures = 0;
foreach (var spec in specs)
{
    try
    {
        await spec.Run();
        Console.WriteLine("PASS " + spec.Name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + spec.Name + ": " + exception);
    }
}

Console.WriteLine($"{specs.Length - failures}/{specs.Length} specs passed");
return failures == 0 ? 0 : 1;

static async Task ThreadRequestsUseSafeDefaults()
{
    await using var server = new FakeAgentAppServer();
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-new"},"cwd":"C:\\work","model":"gpt-test","modelProvider":"openai"}
        """);
    server.QueueResponse("thread/resume", """
        {"thread":{"id":"thread-existing"},"cwd":"C:\\work","model":"gpt-test","modelProvider":"openai"}
        """);

    await using var runtime = new CodexAgentRuntime(
        server,
        new AgentRuntimeOptions
        {
            Model = "gpt-test",
            ModelProvider = "openai",
        });

    var created = await runtime.CreateThreadAsync();
    var resumed = await runtime.ResumeThreadAsync("thread-existing");

    Equal("thread-new", created.ThreadId);
    Equal("thread-existing", resumed.ThreadId);
    Equal(1, server.StartCalls);
    Equal(2, server.Requests.Count);

    AssertSafeThreadRequest(server.Requests[0], "thread/start", expectedThreadId: null);
    AssertSafeThreadRequest(server.Requests[1], "thread/resume", "thread-existing");
}

static async Task TurnStartUsesExpectedWireShape()
{
    await using var server = new FakeAgentAppServer();
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-1"}}
        """);
    server.QueueResponse("turn/start", """
        {"turn":{"id":"turn-1","status":"inProgress","items":[]}}
        """);
    await using var runtime = new CodexAgentRuntime(server);

    _ = await runtime.CreateThreadAsync();
    var turn = await runtime.StartTurnAsync("thread-1", "绘制一条直线");

    Equal("thread-1", turn.ThreadId);
    Equal("turn-1", turn.TurnId);
    Equal(AgentTurnStatus.InProgress, turn.Status);
    Equal(2, server.Requests.Count);
    var request = server.Requests[1];
    Equal("turn/start", request.Method);
    Equal("thread-1", String(request.Params, "threadId"));
    Equal("on-request", String(request.Params, "approvalPolicy"));
    Equal("user", String(request.Params, "approvalsReviewer"));

    var sandbox = request.Params.GetProperty("sandboxPolicy");
    Equal("readOnly", String(sandbox, "type"));
    Equal(false, sandbox.GetProperty("networkAccess").GetBoolean());

    var input = request.Params.GetProperty("input");
    Equal(JsonValueKind.Array, input.ValueKind);
    Equal(1, input.GetArrayLength());
    Equal("text", String(input[0], "type"));
    Equal("绘制一条直线", String(input[0], "text"));
}

static async Task ThreadRegistersCadProposalTool()
{
    await using var server = new FakeAgentAppServer();
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-1"}}
        """);
    await using var runtime = new CodexAgentRuntime(server);

    _ = await runtime.CreateThreadAsync();

    var request = Single(server.Requests);
    var namespaces = request.Params.GetProperty("dynamicTools");
    Equal(1, namespaces.GetArrayLength());
    var cad = namespaces[0];
    Equal("namespace", String(cad, "type"));
    Equal("cad", String(cad, "name"));
    var tools = cad.GetProperty("tools");
    Equal(1, tools.GetArrayLength());
    Equal("propose_operations", String(tools[0], "name"));
    var schema = tools[0].GetProperty("inputSchema");
    Equal(false, schema.GetProperty("additionalProperties").GetBoolean());
    var operation = schema.GetProperty("properties").GetProperty("operations").GetProperty("items");
    Equal(false, operation.GetProperty("additionalProperties").GetBoolean());
    Equal("create_line", operation.GetProperty("properties").GetProperty("type")
        .GetProperty("enum")[0].GetString());
    Equal(false, schema.GetProperty("properties").TryGetProperty("documentFingerprint", out _));
}

static async Task ThreadRegistersOnlyReadOnlyDrawingQueryTool()
{
    await using var server = new FakeAgentAppServer();
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-query-only"}}
        """);
    await using var runtime = new CodexAgentRuntime(server);

    _ = await runtime.CreateThreadAsync(
        new AgentThreadOptions
        {
            EnableCadDynamicTools = false,
            EnableCadDrawingQueryTool = true,
        });

    var request = Single(server.Requests);
    var namespaces = request.Params.GetProperty("dynamicTools");
    Equal(1, namespaces.GetArrayLength());
    var tools = namespaces[0].GetProperty("tools");
    Equal(1, tools.GetArrayLength());
    Equal("query_drawing", String(tools[0], "name"));
    var schema = tools[0].GetProperty("inputSchema");
    Equal(false, schema.GetProperty("additionalProperties").GetBoolean());
    Equal(false, schema.GetProperty("properties").TryGetProperty("indexId", out _));
    Equal(false, schema.GetProperty("properties").TryGetProperty("documentId", out _));
    Equal(false, schema.GetProperty("properties").TryGetProperty("documentRevision", out _));
    Equal(false, schema.GetProperty("properties").TryGetProperty("queryId", out _));
    var objectToken = schema.GetProperty("properties").GetProperty("objectIds")
        .GetProperty("items");
    Equal(12, objectToken.GetProperty("minLength").GetInt32());
    Equal(12, objectToken.GetProperty("maxLength").GetInt32());
    Equal("^obj-[0-9]{8}$", String(objectToken, "pattern"));
    Equal(512, schema.GetProperty("properties").GetProperty("cursor")
        .GetProperty("maxLength").GetInt32());
    Equal(false, tools.EnumerateArray().Any(tool =>
        string.Equals(String(tool, "name"), "propose_operations", StringComparison.Ordinal)));
}

static async Task CadDrawingQueryUsesBrokerAndHidesBindingIdentity()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadDrawingQueryBroker(query =>
        AgentCadDrawingQueryResult.ForQuery(
            query,
            new CadQueryResponse
            {
                IndexId = "idx-host-bound",
                DocumentId = "doc-host-bound",
                DocumentRevision = 7,
                QueryId = query.QueryId,
                Status = CadQueryStatuses.Ok,
                Complete = false,
                TotalMatches = 2,
                ReturnedCount = 1,
                Entities =
                [
                    new CadQueryEntity
                    {
                        ObjectId = "obj-00000026",
                        EntityType = CadContextEntityTypesV2.BlockReference,
                        ActualType = "AcDbBlockReference",
                        Layer = "A-BLOCK",
                        Space = "model",
                        BlockName = "Door",
                        BlockDetails = new CadQueryBlockDetails
                        {
                            DetailStatus = CadQueryBlockDetailStatuses.Complete,
                            IsDynamic = true,
                            HasAttributeDefinitions = true,
                            AttributeCount = 1,
                            Attributes =
                            [
                                new CadQueryBlockAttribute
                                {
                                    Tag = "DOOR_ID",
                                    Value = "D-01",
                                },
                            ],
                            DynamicPropertyCount = 1,
                            DynamicProperties =
                            [
                                new CadQueryDynamicBlockProperty
                                {
                                    Name = "Width",
                                    ValueKind = CadQueryDynamicValueKinds.Number,
                                    Value = "900",
                                    IsVisible = true,
                                },
                            ],
                            NestedBlockReferenceCount = 2,
                            MaximumNestedBlockDepth = 1,
                        },
                        ReadStatus = CadQueryReadStatuses.Parsed,
                    },
                ],
                NextCursor = "dq1-safe-cursor",
            }));
    await using var runtime = new CodexAgentRuntime(server, cadDrawingQueryBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    var resolution = await server.RequestServerAsync("item/tool/call", """
        {
          "threadId":"thread-1","turnId":"turn-1","callId":"call-query-1",
          "namespace":"cad","tool":"query_drawing",
          "arguments":{"layers":["AI"],"pageSize":1,"includeUnsupported":false}
        }
        """);

    NotNull(resolution);
    var response = ResolutionResult(resolution!);
    Equal(true, response.GetProperty("success").GetBoolean());
    var content = String(response.GetProperty("contentItems")[0], "text");
    using var contentJson = JsonDocument.Parse(content);
    var result = contentJson.RootElement;
    Equal(CadQueryStatuses.Ok, String(result, "status"));
    Equal(2, result.GetProperty("totalMatches").GetInt32());
    Equal("dq1-safe-cursor", String(result, "nextCursor"));
    Equal("obj-00000026", String(result.GetProperty("entities")[0], "objectId"));
    var blockDetails = result.GetProperty("entities")[0].GetProperty("blockDetails");
    Equal(CadQueryBlockDetailStatuses.Complete, String(blockDetails, "detailStatus"));
    Equal(true, blockDetails.GetProperty("isDynamic").GetBoolean());
    Equal("DOOR_ID", String(blockDetails.GetProperty("attributes")[0], "tag"));
    Equal("900", String(blockDetails.GetProperty("dynamicProperties")[0], "value"));
    Equal(false, blockDetails.TryGetProperty("xrefPath", out _));
    Equal(false, blockDetails.TryGetProperty("sourcePath", out _));
    Equal(false, result.TryGetProperty("indexId", out _));
    Equal(false, result.TryGetProperty("documentId", out _));
    Equal(false, result.TryGetProperty("documentRevision", out _));
    Equal(false, result.TryGetProperty("queryId", out _));

    Equal(1, broker.CallCount);
    NotNull(broker.LastQuery);
    Equal("runtime-request-1", broker.LastQuery!.RequestId);
    Equal("call-query-1", broker.LastQuery.CallId);
    True(!string.Equals(broker.LastQuery.QueryId, broker.LastQuery.CallId, StringComparison.Ordinal),
        "Host查询ID必须与模型callId分离。");
    Equal("AI", Single(broker.LastQuery.Filter.Layers));
    Equal(1, broker.LastQuery.PageSize);
    Equal(false, broker.LastQuery.Filter.IncludeUnsupported);
}

static async Task CadDrawingQueryRejectsRawHandleToken()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadDrawingQueryBroker(
        query => throw new InvalidOperationException("Broker must not receive invalid tokens."));
    await using var runtime = new CodexAgentRuntime(server, cadDrawingQueryBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    var resolution = await server.RequestServerAsync("item/tool/call", """
        {
          "threadId":"thread-1","turnId":"turn-1","callId":"call-query-raw-handle",
          "namespace":"cad","tool":"query_drawing",
          "arguments":{"objectIds":["1A"],"pageSize":1}
        }
        """);

    NotNull(resolution);
    var response = ResolutionResult(resolution!);
    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(0, broker.CallCount);
}

static async Task CadDynamicToolRequiresAppliedTerminalResult()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadProposalBroker(
        proposal => AgentCadProposalResult.Applied(proposal, "line committed"));
    await using var runtime = new CodexAgentRuntime(server, cadProposalBroker: broker);
    AgentCadProposalCreatedEvent? proposalEvent = null;
    runtime.EventReceived += (_, agentEvent) => proposalEvent = agentEvent as AgentCadProposalCreatedEvent ?? proposalEvent;
    await PrepareActiveTurnAsync(server, runtime);

    var resolution = await server.RequestServerAsync("item/tool/call", """
        {
          "threadId":"thread-1","turnId":"turn-1","callId":"call-1",
          "namespace":"cad","tool":"propose_operations",
          "arguments":{"operations":[
            {"type":"create_line","start":{"x":1,"y":2},"end":{"x":4,"y":5,"z":6},"layer":"AI"}
          ]}
        }
        """);

    NotNull(resolution);
    var response = ResolutionResult(resolution!);
    Equal(true, response.GetProperty("success").GetBoolean());
    Equal("inputText", String(response.GetProperty("contentItems")[0], "type"));
    NotNull(proposalEvent);
    Equal("call-1", proposalEvent!.CallId);
    Equal("call-1", proposalEvent!.Proposal.CallId);
    True(!string.Equals(
            proposalEvent.Proposal.ProposalId,
            proposalEvent.Proposal.CallId,
            StringComparison.Ordinal),
        "Runtime签发的proposalId必须与模型提供的callId独立。");
    Equal(1, proposalEvent.Proposal.Operations.Count);
    var line = IsType<AgentCadCreateLineProposal>(proposalEvent.Proposal.Operations[0]);
    Equal(new AgentCadPoint3d(1, 2, 0), line.Start);
    Equal(new AgentCadPoint3d(4, 5, 6), line.End);
    Equal("AI", line.Layer);
    Equal(1, broker.CallCount);
}

static async Task CadProposalEventIsDeeplyIsolatedFromBroker()
{
    await using var server = new FakeAgentAppServer();
    AgentCadOperationBatchProposal? brokerProposal = null;
    var broker = new FunctionalCadProposalBroker(proposal =>
    {
        brokerProposal = proposal;
        var line = IsType<AgentCadCreateLineProposal>(Single(proposal.Operations.ToArray()));
        return line.End == new AgentCadPoint3d(1, 1, 0)
            ? AgentCadProposalResult.Applied(proposal)
            : AgentCadProposalResult.Failed(proposal, "observer changed broker proposal");
    });
    await using var runtime = new CodexAgentRuntime(server, cadProposalBroker: broker);
    AgentCadOperationBatchProposal? eventProposal = null;
    runtime.EventReceived += (_, agentEvent) =>
    {
        if (agentEvent is not AgentCadProposalCreatedEvent created)
        {
            return;
        }

        eventProposal = created.Proposal;
        if (created.Proposal.Operations is IList<AgentCadOperationProposal> mutableOperations)
        {
            try
            {
                mutableOperations[0] = new AgentCadCreateLineProposal(
                    new AgentCadPoint3d(900, 900, 0),
                    new AgentCadPoint3d(999, 999, 0),
                    "observer-poison");
            }
            catch (NotSupportedException)
            {
                // A deeply immutable public snapshot is the expected implementation.
            }
        }

        var mutableLine = IsType<AgentCadCreateLineProposal>(
            Single(created.Proposal.Operations.ToArray()));
        var endProperty = typeof(AgentCadCreateLineProposal).GetProperty(
            nameof(AgentCadCreateLineProposal.End))
            ?? throw new InvalidOperationException("Expected the line End property.");
        var layerProperty = typeof(AgentCadCreateLineProposal).GetProperty(
            nameof(AgentCadCreateLineProposal.Layer))
            ?? throw new InvalidOperationException("Expected the line Layer property.");
        True(endProperty.CanWrite, "Test setup requires an actually mutable init property.");
        True(layerProperty.CanWrite, "Test setup requires an actually mutable init property.");
        endProperty.SetValue(mutableLine, new AgentCadPoint3d(999, 999, 0));
        layerProperty.SetValue(mutableLine, "observer-property-poison");
    };
    await PrepareActiveTurnAsync(server, runtime);

    var response = ResolutionResult((await server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-event-isolation")))!);

    Equal(true, response.GetProperty("success").GetBoolean());
    var eventSnapshot = eventProposal
        ?? throw new InvalidOperationException("Expected a CAD proposal-created event.");
    var brokerSnapshot = brokerProposal
        ?? throw new InvalidOperationException("Expected the broker to receive a CAD proposal.");
    var eventLine = IsType<AgentCadCreateLineProposal>(Single(eventSnapshot.Operations.ToArray()));
    var brokerLine = IsType<AgentCadCreateLineProposal>(Single(brokerSnapshot.Operations.ToArray()));
    Equal(new AgentCadPoint3d(999, 999, 0), eventLine.End);
    Equal("observer-property-poison", eventLine.Layer);
    Equal(new AgentCadPoint3d(1, 1, 0), brokerLine.End);
    Equal<string?>(null, brokerLine.Layer);
    Equal(false, ReferenceEquals(eventSnapshot, brokerSnapshot));
    Equal(false, ReferenceEquals(eventSnapshot.Operations, brokerSnapshot.Operations));
}

static async Task CadDynamicToolWithoutBrokerFailsClosed()
{
    await using var server = new FakeAgentAppServer();
    await using var runtime = new CodexAgentRuntime(server);
    var events = new List<AgentEvent>();
    runtime.EventReceived += (_, agentEvent) => events.Add(agentEvent);
    await PrepareActiveTurnAsync(server, runtime);

    var resolution = await server.RequestServerAsync("item/tool/call", """
        {
          "threadId":"thread-1","turnId":"turn-1","callId":"call-no-broker",
          "namespace":"cad","tool":"propose_operations",
          "arguments":{"operations":[
            {"type":"create_line","start":{"x":0,"y":0},"end":{"x":1,"y":1}}
          ]}
        }
        """);

    var response = ResolutionResult(resolution!);
    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(0, events.OfType<AgentCadProposalCreatedEvent>().Count());
    Equal(1, events.OfType<AgentDynamicToolRejectedEvent>().Count());
}

static async Task CadDynamicToolRejectsInactiveTurn()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadProposalBroker(
        proposal => AgentCadProposalResult.Applied(proposal));
    await using var runtime = new CodexAgentRuntime(server, cadProposalBroker: broker);

    var resolution = await server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-inactive"));
    var response = ResolutionResult(resolution!);

    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(0, broker.CallCount);
}

static async Task CadDynamicToolRejectsForgedTurnStartedNotification()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadProposalBroker(
        proposal => AgentCadProposalResult.Applied(proposal));
    await using var runtime = new CodexAgentRuntime(server, cadProposalBroker: broker);
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-1"}}
        """);
    _ = await runtime.CreateThreadAsync();

    server.EmitNotification("turn/started", """
        {"threadId":"thread-1","turn":{"id":"turn-1","status":"inProgress","items":[]}}
        """);
    var resolution = await server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-forged-turn"));
    var response = ResolutionResult(resolution!);

    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(0, broker.CallCount);
}

static async Task CadDynamicToolTerminalNotificationRevokesAuthorization()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadProposalBroker(
        proposal => AgentCadProposalResult.Applied(proposal));
    await using var runtime = new CodexAgentRuntime(server, cadProposalBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    server.EmitNotification("turn/completed", """
        {"threadId":"thread-1","turn":{"id":"turn-1","status":"completed","items":[]}}
        """);
    server.EmitNotification("turn/started", """
        {"threadId":"thread-1","turn":{"id":"turn-1","status":"inProgress","items":[]}}
        """);
    var resolution = await server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-resurrected-turn"));
    var response = ResolutionResult(resolution!);

    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(0, broker.CallCount);
}

static async Task CadDynamicToolRejectsNonAppliedOutcomes()
{
    foreach (var outcome in new[]
             {
                 AgentCadProposalOutcome.Rejected,
                 AgentCadProposalOutcome.Failed,
             })
    {
        await using var server = new FakeAgentAppServer();
        var broker = new FunctionalCadProposalBroker(proposal => outcome switch
        {
            AgentCadProposalOutcome.Rejected =>
                AgentCadProposalResult.Rejected(proposal, "user declined"),
            AgentCadProposalOutcome.Failed =>
                AgentCadProposalResult.Failed(proposal, "transaction rolled back"),
            _ => throw new InvalidOperationException("Unexpected test outcome."),
        });
        await using var runtime = new CodexAgentRuntime(server, cadProposalBroker: broker);
        await PrepareActiveTurnAsync(server, runtime);

        var resolution = await server.RequestServerAsync("item/tool/call", ValidCadToolRequest("call-terminal"));
        var response = ResolutionResult(resolution!);
        Equal(false, response.GetProperty("success").GetBoolean());
        Equal(1, broker.CallCount);
    }
}

static async Task CadDynamicToolRejectsMismatchedBrokerIdentity()
{
    var mismatches = new Func<AgentCadOperationBatchProposal, AgentCadProposalResult>[]
    {
        proposal => new AgentCadProposalResult(
            AgentCadProposalOutcome.Applied,
            "applied with wrong proposal",
            proposal.ProposalId + "-wrong",
            proposal.ThreadId,
            proposal.TurnId,
            proposal.CallId),
        proposal => new AgentCadProposalResult(
            AgentCadProposalOutcome.Applied,
            "applied with wrong thread",
            proposal.ProposalId,
            proposal.ThreadId + "-wrong",
            proposal.TurnId,
            proposal.CallId),
        proposal => new AgentCadProposalResult(
            AgentCadProposalOutcome.Applied,
            "applied with wrong turn",
            proposal.ProposalId,
            proposal.ThreadId,
            proposal.TurnId + "-wrong",
            proposal.CallId),
        proposal => new AgentCadProposalResult(
            AgentCadProposalOutcome.Applied,
            "applied with wrong call",
            proposal.ProposalId,
            proposal.ThreadId,
            proposal.TurnId,
            proposal.CallId + "-wrong"),
        proposal => new AgentCadProposalResult(
            AgentCadProposalOutcome.Rejected,
            "rejected with wrong thread",
            proposal.ProposalId,
            proposal.ThreadId + "-wrong",
            proposal.TurnId,
            proposal.CallId),
        proposal => new AgentCadProposalResult(
            AgentCadProposalOutcome.Failed,
            "failed with wrong thread",
            proposal.ProposalId,
            proposal.ThreadId + "-wrong",
            proposal.TurnId,
            proposal.CallId),
    };

    for (var index = 0; index < mismatches.Length; index++)
    {
        await using var server = new FakeAgentAppServer();
        var broker = new FunctionalCadProposalBroker(mismatches[index]);
        await using var runtime = new CodexAgentRuntime(server, cadProposalBroker: broker);
        var events = new List<AgentEvent>();
        runtime.EventReceived += (_, agentEvent) => events.Add(agentEvent);
        await PrepareActiveTurnAsync(server, runtime);

        var resolution = await server.RequestServerAsync(
            "item/tool/call",
            ValidCadToolRequest("call-identity-" + index));
        var response = ResolutionResult(resolution!);
        Equal(false, response.GetProperty("success").GetBoolean());
        Equal(1, broker.CallCount);
        var rejected = Single(events.OfType<AgentDynamicToolRejectedEvent>().ToArray());
        Equal("call-identity-" + index, rejected.CallId);
        True(rejected.Reason.Contains("identity", StringComparison.OrdinalIgnoreCase),
            "Broker身份错配必须以明确身份错误fail closed。");
    }
}

static async Task CadDynamicToolTimeoutFailsClosed()
{
    await using var server = new FakeAgentAppServer();
    var broker = new BlockingCadProposalBroker();
    await using var runtime = new CodexAgentRuntime(
        server,
        new AgentRuntimeOptions { CadProposalTimeout = TimeSpan.FromMilliseconds(25) },
        cadProposalBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    var resolution = await server.RequestServerAsync("item/tool/call", ValidCadToolRequest("call-timeout"));
    var response = ResolutionResult(resolution!);
    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(1, broker.CallCount);
}

static async Task CadDynamicToolHardTimeoutIgnoresLateBrokerResult()
{
    await using var server = new FakeAgentAppServer();
    var broker = new IgnoringCancellationCadProposalBroker();
    await using var runtime = new CodexAgentRuntime(
        server,
        new AgentRuntimeOptions { CadProposalTimeout = TimeSpan.FromMilliseconds(25) },
        cadProposalBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    var pending = server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-hard-timeout")).AsTask();
    ServerRequestResolution? resolution;
    try
    {
        resolution = await pending.WaitAsync(TimeSpan.FromMilliseconds(500));
    }
    catch
    {
        broker.CompleteApplied("late applied result");
        _ = await pending;
        throw;
    }

    var response = ResolutionResult(resolution!);
    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(1, broker.CallCount);

    broker.CompleteApplied("late applied result");
    await Task.Delay(25);

    var replay = ResolutionResult((await server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-hard-timeout")))!);
    Equal(false, replay.GetProperty("success").GetBoolean());
    Equal(1, broker.CallCount);
}

static async Task CadDynamicToolTerminalCancelsInFlightBroker()
{
    foreach (var terminalStatus in new[] { "completed", "interrupted", "cancelled", "failed" })
    {
        await using var server = new FakeAgentAppServer();
        var broker = new IgnoringCancellationCadProposalBroker();
        await using var runtime = new CodexAgentRuntime(
            server,
            new AgentRuntimeOptions { CadProposalTimeout = TimeSpan.FromSeconds(30) },
            cadProposalBroker: broker);
        await PrepareActiveTurnAsync(server, runtime);

        var callId = "call-terminal-in-flight-" + terminalStatus;
        var request = ValidCadToolRequest(callId);
        var pending = server.RequestServerAsync("item/tool/call", request).AsTask();
        await broker.Started.WaitAsync(TimeSpan.FromMilliseconds(500));

        server.EmitNotification("turn/completed", JsonSerializer.Serialize(new
        {
            threadId = "thread-1",
            turn = new
            {
                id = "turn-1",
                status = terminalStatus,
                items = Array.Empty<object>(),
            },
        }));

        try
        {
            await broker.CancellationObserved.WaitAsync(TimeSpan.FromMilliseconds(500));
            var terminal = ResolutionResult(
                (await pending.WaitAsync(TimeSpan.FromMilliseconds(500)))!);
            Equal(false, terminal.GetProperty("success").GetBoolean());

            broker.CompleteApplied("late applied after " + terminalStatus);
            await Task.Delay(25);

            var replay = ResolutionResult(
                (await server.RequestServerAsync("item/tool/call", request))!);
            Equal(false, replay.GetProperty("success").GetBoolean());
            Equal(1, broker.CallCount);
        }
        finally
        {
            broker.CompleteApplied("test cleanup");
        }
    }
}

static async Task CadDynamicToolNeverEndingBrokerDoesNotBlockDisposal()
{
    await using var server = new FakeAgentAppServer();
    var broker = new IgnoringCancellationCadProposalBroker();
    await using var runtime = new CodexAgentRuntime(
        server,
        new AgentRuntimeOptions { CadProposalTimeout = TimeSpan.FromMilliseconds(25) },
        cadProposalBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    var resolution = await server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-never-ending")).AsTask().WaitAsync(TimeSpan.FromMilliseconds(500));
    var response = ResolutionResult(resolution!);

    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(1, broker.CallCount);
    await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(500));
}

static async Task CadDynamicToolLateBrokerFaultIsObserved()
{
    const string marker = "late-broker-fault-must-be-observed";
    var unobserved = 0;
    EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
    {
        if (args.Exception.Flatten().InnerExceptions.Any(
                exception => string.Equals(exception.Message, marker, StringComparison.Ordinal)))
        {
            Interlocked.Exchange(ref unobserved, 1);
            args.SetObserved();
        }
    };
    TaskScheduler.UnobservedTaskException += handler;
    try
    {
        var lateTask = await RunLateFaultScenarioAsync(marker);
        for (var attempt = 0; attempt < 10 && lateTask.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(10);
        }

        Equal(false, lateTask.IsAlive);
        Equal(0, Volatile.Read(ref unobserved));
    }
    finally
    {
        TaskScheduler.UnobservedTaskException -= handler;
    }
}

[System.Runtime.CompilerServices.MethodImpl(
    System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static async Task<WeakReference> RunLateFaultScenarioAsync(string marker)
{
    await using var server = new FakeAgentAppServer();
    var broker = new IgnoringCancellationCadProposalBroker();
    await using var runtime = new CodexAgentRuntime(
        server,
        new AgentRuntimeOptions { CadProposalTimeout = TimeSpan.FromMilliseconds(25) },
        cadProposalBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    var request = ValidCadToolRequest("call-late-fault");
    var resolution = await server.RequestServerAsync(
        "item/tool/call",
        request).AsTask().WaitAsync(TimeSpan.FromMilliseconds(500));
    Equal(false, ResolutionResult(resolution!).GetProperty("success").GetBoolean());
    Equal(1, broker.CallCount);

    var lateTask = broker.ExecutionReference;
    broker.Fail(new InvalidOperationException(marker));
    await Task.Delay(25);

    var replay = ResolutionResult((await server.RequestServerAsync("item/tool/call", request))!);
    var next = ResolutionResult((await server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-after-late-fault")))!);
    Equal(false, replay.GetProperty("success").GetBoolean());
    Equal(false, next.GetProperty("success").GetBoolean());
    Equal(2, broker.CallCount);
    return lateTask;
}

static async Task CadDynamicToolIsIdempotent()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadProposalBroker(
        proposal => AgentCadProposalResult.Applied(proposal));
    await using var runtime = new CodexAgentRuntime(server, cadProposalBroker: broker);
    var proposals = new List<AgentCadOperationBatchProposal>();
    runtime.EventReceived += (_, agentEvent) =>
    {
        if (agentEvent is AgentCadProposalCreatedEvent created)
        {
            proposals.Add(created.Proposal);
        }
    };
    await PrepareActiveTurnAsync(server, runtime);

    const string request = """
        {
          "threadId":"thread-1","turnId":"turn-1","callId":"call-idempotent",
          "namespace":"cad","tool":"propose_operations",
          "arguments":{"operations":[
            {"type":"create_line","start":{"x":0,"y":0},"end":{"x":1,"y":1}}
          ]}
        }
        """;
    var first = ResolutionResult((await server.RequestServerAsync("item/tool/call", request))!);
    var replay = ResolutionResult((await server.RequestServerAsync("item/tool/call", request))!);
    var tampered = ResolutionResult((await server.RequestServerAsync("item/tool/call", """
        {
          "threadId":"thread-1","turnId":"turn-1","callId":"call-idempotent",
          "namespace":"cad","tool":"propose_operations",
          "arguments":{"operations":[
            {"type":"create_line","start":{"x":0,"y":0},"end":{"x":2,"y":2}}
          ]}
        }
        """))!);

    Equal(true, first.GetProperty("success").GetBoolean());
    Equal(true, replay.GetProperty("success").GetBoolean());
    Equal(false, tampered.GetProperty("success").GetBoolean());
    Equal(1, broker.CallCount);
    var proposal = Single(proposals);
    Equal("call-idempotent", proposal.CallId);
    True(!string.Equals(proposal.ProposalId, proposal.CallId, StringComparison.Ordinal),
        "幂等执行的proposalId必须独立于callId。");
    Equal(
        String(first.GetProperty("contentItems")[0], "text"),
        String(replay.GetProperty("contentItems")[0], "text"));
    using var resultPayload = JsonDocument.Parse(String(first.GetProperty("contentItems")[0], "text"));
    Equal(proposal.ProposalId, String(resultPayload.RootElement, "proposalId"));
    Equal(proposal.CallId, String(resultPayload.RootElement, "callId"));
}

static async Task CadCallRegistryFullPreservesReplayTombstone()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadProposalBroker(
        proposal => AgentCadProposalResult.Applied(proposal));
    await using var runtime = new CodexAgentRuntime(
        server,
        new AgentRuntimeOptions { MaximumTrackedCadCalls = 1 },
        cadProposalBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    var firstRequest = ValidCadToolRequest("call-kept");
    var first = ResolutionResult((await server.RequestServerAsync(
        "item/tool/call",
        firstRequest))!);
    var overCapacity = ResolutionResult((await server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-over-capacity")))!);
    var replay = ResolutionResult((await server.RequestServerAsync(
        "item/tool/call",
        firstRequest))!);

    Equal(true, first.GetProperty("success").GetBoolean());
    Equal(false, overCapacity.GetProperty("success").GetBoolean());
    Equal(true, replay.GetProperty("success").GetBoolean());
    Equal(1, broker.CallCount);
}

static async Task CadCallRegistryClearsOnlyAtTurnTerminal()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadProposalBroker(
        proposal => AgentCadProposalResult.Applied(proposal));
    await using var runtime = new CodexAgentRuntime(
        server,
        new AgentRuntimeOptions { MaximumTrackedCadCalls = 1 },
        cadProposalBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    var oldRequest = ValidCadToolRequest("call-old-turn");
    var first = ResolutionResult((await server.RequestServerAsync(
        "item/tool/call",
        oldRequest))!);
    Equal(true, first.GetProperty("success").GetBoolean());
    Equal(1, broker.CallCount);

    server.EmitNotification("turn/completed", """
        {"threadId":"thread-1","turn":{"id":"turn-1","status":"completed","items":[]}}
        """);
    server.QueueResponse("turn/start", """
        {"turn":{"id":"turn-2","status":"inProgress","items":[]}}
        """);
    _ = await runtime.StartTurnAsync("thread-1", "next turn");

    var oldReplay = ResolutionResult((await server.RequestServerAsync(
        "item/tool/call",
        oldRequest))!);
    var nextTurn = ResolutionResult((await server.RequestServerAsync(
        "item/tool/call",
        ValidCadToolRequest("call-next-turn", "turn-2")))!);

    Equal(false, oldReplay.GetProperty("success").GetBoolean());
    Equal(true, nextTurn.GetProperty("success").GetBoolean());
    Equal(2, broker.CallCount);
}

static async Task CadDynamicToolRejectsDocumentBinding()
{
    await using var server = new FakeAgentAppServer();
    await using var runtime = new CodexAgentRuntime(server);
    var events = new List<AgentEvent>();
    runtime.EventReceived += (_, agentEvent) => events.Add(agentEvent);
    await PrepareActiveTurnAsync(server, runtime);

    var resolution = await server.RequestServerAsync("item/tool/call", """
        {
          "threadId":"thread-1","turnId":"turn-1","callId":"call-2",
          "namespace":"cad","tool":"propose_operations",
          "arguments":{
            "documentFingerprint":"model-supplied",
            "operations":[{"type":"create_line","start":{"x":0,"y":0},"end":{"x":1,"y":1}}]
          }
        }
        """);

    var response = ResolutionResult(resolution!);
    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(0, events.OfType<AgentCadProposalCreatedEvent>().Count());
    var rejected = Single(events.OfType<AgentDynamicToolRejectedEvent>().ToArray());
    True(rejected.Reason.Contains("documentFingerprint", StringComparison.Ordinal),
        "拒绝原因必须指出模型提交的文档绑定字段。");
}

static async Task MalformedCadDynamicToolIsIsolated()
{
    await using var server = new FakeAgentAppServer();
    var broker = new FunctionalCadProposalBroker(
        proposal => AgentCadProposalResult.Applied(proposal));
    await using var runtime = new CodexAgentRuntime(server, cadProposalBroker: broker);
    await PrepareActiveTurnAsync(server, runtime);

    var resolution = await server.RequestServerAsync("item/tool/call", """
        {
          "threadId":"thread-1","turnId":"turn-1","callId":"call-malformed",
          "namespace":"cad","tool":"propose_operations",
          "arguments":{"operations":[
            {"type":"create_line","start":null,"end":{"x":"not-a-number","y":1}}
          ]}
        }
        """);

    var response = ResolutionResult(resolution!);
    Equal(false, response.GetProperty("success").GetBoolean());
    Equal(0, broker.CallCount);
}

static Task RuntimePublicRecordStringsAreSafe()
{
    var pathMarker = "runtime-path-user-marker";
    var promptMarker = "runtime-prompt-secret-marker";
    var providerMarker = "runtime-provider-thread-marker";
    var coordinateMarker = "123456.789";
    using var outputSchema = JsonDocument.Parse(
        $$"""{"credential":"{{promptMarker}}","path":"C:\\Users\\{{pathMarker}}\\schema.json"}""");
    using var eventPayload = JsonDocument.Parse(
        $$"""{"credential":"{{promptMarker}}","path":"C:\\Users\\{{pathMarker}}\\event.json"}""");
    var item = new AgentItemSnapshot(
        providerMarker,
        AgentItemKind.AgentMessage,
        promptMarker,
        promptMarker,
        promptMarker,
        eventPayload.RootElement.Clone());
    var lineProposal = new AgentCadCreateLineProposal(
        new AgentCadPoint3d(123456.789, 0, 0),
        new AgentCadPoint3d(1, 1, 0),
        promptMarker);
    var proposal = new AgentCadOperationBatchProposal(
        providerMarker,
        providerMarker,
        providerMarker,
        providerMarker,
        new AgentCadOperationProposal[]
        {
            lineProposal,
        });
    var commandApproval = new CommandApprovalRequest(
        providerMarker,
        1,
        providerMarker,
        providerMarker,
        Command: "cmd /c " + promptMarker,
        WorkingDirectory: $@"C:\Users\{pathMarker}\approval",
        Reason: promptMarker);
    var fileApproval = new FileChangeApprovalRequest(
        providerMarker,
        2,
        providerMarker,
        providerMarker,
        $@"C:\Users\{pathMarker}\grant",
        promptMarker);
    var permissionsApproval = new PermissionsApprovalRequest(
        $@"C:\Users\{pathMarker}\permissions",
        providerMarker,
        new PermissionProfile(),
        3,
        providerMarker,
        providerMarker,
        Reason: promptMarker);
    var cadApproval = new CadApprovalRequest(
        providerMarker,
        providerMarker,
        providerMarker,
        new CadDocumentIdentity(providerMarker, providerMarker, 4, providerMarker),
        providerMarker,
        promptMarker,
        5,
        new CadChangeSummary(1, 0, 0, promptMarker),
        eventPayload.RootElement.Clone());
    object[] values =
    {
        new AgentRuntimeOptions
        {
            WorkingDirectory = $@"C:\Users\{pathMarker}\workspace",
            ManagedWorkspaceRoot = $@"C:\Users\{pathMarker}\managed",
            Model = promptMarker,
            ModelProvider = promptMarker,
        },
        new AgentThreadOptions
        {
            WorkingDirectory = $@"C:\Users\{pathMarker}\thread",
            Model = promptMarker,
            ModelProvider = promptMarker,
            DeveloperInstructions = "Bear" + "er " + promptMarker,
            ServiceTier = promptMarker,
        },
        new AgentTurnOptions
        {
            WorkingDirectory = $@"C:\Users\{pathMarker}\turn",
            Model = promptMarker,
            ClientUserMessageId = promptMarker,
            ServiceTier = promptMarker,
            OutputSchema = outputSchema.RootElement.Clone(),
        },
        new AgentThreadHandle(
            providerMarker,
            $@"C:\Users\{pathMarker}\handle",
            promptMarker,
            promptMarker),
        new AgentTurnHandle(providerMarker, providerMarker, AgentTurnStatus.InProgress),
        new AgentTextInput("Bear" + "er " + promptMarker),
        new AgentLocalImageInput($@"C:\Users\{pathMarker}\private.png"),
        new AgentMentionInput(promptMarker, $@"C:\Users\{pathMarker}\private.txt"),
        lineProposal.Start,
        lineProposal,
        proposal,
        new AgentCadProposalResult(
            AgentCadProposalOutcome.Failed,
            promptMarker,
            providerMarker,
            providerMarker,
            providerMarker,
            providerMarker),
        new AgentEventDiagnosticSnapshot(promptMarker),
        item,
        new AgentMessageDeltaEvent(providerMarker, providerMarker, providerMarker, promptMarker),
        new AgentItemStateChangedEvent(
            providerMarker,
            providerMarker,
            AgentItemLifecycle.Started,
            6,
            item),
        new AgentToolStateChangedEvent(
            providerMarker,
            providerMarker,
            AgentItemLifecycle.Started,
            7,
            AgentToolKind.CommandExecution,
            AgentToolStatus.InProgress,
            item),
        new AgentToolProgressEvent(
            providerMarker,
            providerMarker,
            providerMarker,
            AgentToolKind.CommandExecution,
            promptMarker,
            eventPayload.RootElement.Clone()),
        new AgentCadProposalCreatedEvent(
            providerMarker,
            providerMarker,
            providerMarker,
            proposal),
        new AgentDynamicToolRejectedEvent(
            providerMarker,
            providerMarker,
            providerMarker,
            promptMarker,
            promptMarker,
            promptMarker),
        new AgentTurnStateChangedEvent(
            providerMarker,
            providerMarker,
            AgentTurnStatus.Failed,
            promptMarker,
            eventPayload.RootElement.Clone()),
        new AgentApprovalReviewStateChangedEvent(
            providerMarker,
            providerMarker,
            providerMarker,
            providerMarker,
            AgentApprovalReviewLifecycle.Started,
            8,
            eventPayload.RootElement.Clone()),
        new AgentCommandApprovalRequestedEvent(commandApproval),
        new AgentFileChangeApprovalRequestedEvent(fileApproval),
        new AgentPermissionsApprovalRequestedEvent(permissionsApproval),
        new AgentCadApprovalRequestedEvent(cadApproval),
    };

    foreach (var value in values)
    {
        var diagnostic = value.ToString() ?? string.Empty;
        True(
            diagnostic.StartsWith(value.GetType().Name, StringComparison.Ordinal),
            "Runtime record string projection omitted its stable type name.");
        foreach (var marker in new[] { pathMarker, promptMarker, providerMarker, coordinateMarker })
        {
            True(
                diagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
                "Runtime record string projection leaked a protected marker.");
        }

        True(
            diagnostic.IndexOf(@"C:\Users\", StringComparison.OrdinalIgnoreCase) < 0,
            "Runtime record string projection leaked an absolute path.");
    }

    return Task.CompletedTask;
}

static async Task CadDynamicToolValidationDiagnosticsAreSanitized()
{
    await using var server = new FakeAgentAppServer();
    await using var runtime = new CodexAgentRuntime(server);
    var events = new List<AgentEvent>();
    runtime.EventReceived += (_, agentEvent) => events.Add(agentEvent);
    await PrepareActiveTurnAsync(server, runtime);

    var marker = "dynamic-tool-secret-marker";
    var unsafeProperty = "Authorization=Bearer " + marker + " " + @"C:\Users\tool-user\secret.txt";
    var request = JsonSerializer.Serialize(new
    {
        threadId = "thread-1",
        turnId = "turn-1",
        callId = "call-sanitized-validation",
        @namespace = "cad",
        tool = "query_drawing",
        arguments = new Dictionary<string, object?>
        {
            [unsafeProperty] = true,
        },
    });

    var resolution = await server.RequestServerAsync("item/tool/call", request);
    var response = ResolutionResult(resolution!);
    Equal(false, response.GetProperty("success").GetBoolean());
    var rejected = Single(events.OfType<AgentDynamicToolRejectedEvent>().ToArray());
    var publicText = response.GetRawText() + " " + rejected.Reason;
    foreach (var protectedValue in new[] { marker, "tool-user", "secret.txt" })
    {
        True(
            publicText.IndexOf(protectedValue, StringComparison.OrdinalIgnoreCase) < 0,
            "Dynamic tool validation diagnostics leaked a protected value.");
    }

    True(
        publicText.Contains("[redacted-token]", StringComparison.Ordinal)
        && publicText.Contains("[redacted-path]", StringComparison.Ordinal),
        "Dynamic tool validation diagnostics did not preserve bounded redaction placeholders.");
}

static async Task WorkspaceWriteRequiresManagedRoot()
{
    await using var server = new FakeAgentAppServer();
    Throws<ArgumentException>(() => new CodexAgentRuntime(
        server,
        new AgentRuntimeOptions { Sandbox = AgentSandboxMode.WorkspaceWrite }));
}

static async Task ManagedWorkspaceRejectsEscapeAndAds()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-autocad-runtime-" + Guid.NewGuid().ToString("N"));
    var outside = Path.Combine(Path.GetTempPath(), "codex-autocad-outside-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(outside);
    try
    {
        await using var server = new FakeAgentAppServer();
        await using var runtime = new CodexAgentRuntime(
            server,
            new AgentRuntimeOptions
            {
                Sandbox = AgentSandboxMode.WorkspaceWrite,
                ManagedWorkspaceRoot = root,
            });
        server.QueueResponse("thread/start", """
            {"thread":{"id":"thread-1"}}
            """);
        _ = await runtime.CreateThreadAsync();

        var request = Single(server.Requests);
        Equal("workspace-write", String(request.Params, "sandbox"));
        Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), String(request.Params, "cwd"));
        Equal(1, request.Params.GetProperty("runtimeWorkspaceRoots").GetArrayLength());

        await ThrowsAsync<ArgumentException>(() => runtime.StartTurnAsync(
            "thread-1",
            "escape",
            new AgentTurnOptions { WorkingDirectory = outside }));

        if (OperatingSystem.IsWindows())
        {
            await ThrowsAsync<ArgumentException>(() => runtime.StartTurnAsync(
                "thread-1",
                "ads",
                new AgentTurnOptions { WorkingDirectory = root + ":hidden" }));
        }
    }
    finally
    {
        Directory.Delete(root, recursive: true);
        Directory.Delete(outside, recursive: true);
    }
}

static async Task LocalFileInputsAreDisabledByDefault()
{
    await using var server = new FakeAgentAppServer();
    await using var runtime = new CodexAgentRuntime(server);
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-1"}}
        """);
    _ = await runtime.CreateThreadAsync();

    await ThrowsAsync<InvalidOperationException>(() => runtime.StartTurnAsync(
        "thread-1",
        new AgentInput[] { new AgentLocalImageInput("C:\\outside.png") }));
}

static async Task PrepareActiveTurnAsync(FakeAgentAppServer server, CodexAgentRuntime runtime)
{
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-1"}}
        """);
    server.QueueResponse("turn/start", """
        {"turn":{"id":"turn-1","status":"inProgress","items":[]}}
        """);
    _ = await runtime.CreateThreadAsync();
    _ = await runtime.StartTurnAsync(
        "thread-1",
        "prepare",
        new AgentTurnOptions { ClientUserMessageId = "runtime-request-1" });
}

static string ValidCadToolRequest(string callId, string turnId = "turn-1")
    => JsonSerializer.Serialize(new
    {
        threadId = "thread-1",
        turnId,
        callId,
        @namespace = "cad",
        tool = "propose_operations",
        arguments = new
        {
            operations = new[]
            {
                new
                {
                    type = "create_line",
                    start = new { x = 0, y = 0 },
                    end = new { x = 1, y = 1 },
                },
            },
        },
    });

static async Task InterruptUsesExpectedWireShape()
{
    await using var server = new FakeAgentAppServer();
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-1"}}
        """);
    server.QueueResponse("turn/start", """
        {"turn":{"id":"turn-1","status":"inProgress","items":[]}}
        """);
    server.QueueResponse("turn/interrupt", "{}");
    await using var runtime = new CodexAgentRuntime(server);

    _ = await runtime.CreateThreadAsync();
    _ = await runtime.StartTurnAsync("thread-1", "test");
    await runtime.InterruptTurnAsync("thread-1", "turn-1");

    Equal(3, server.Requests.Count);
    var request = server.Requests[2];
    Equal("turn/interrupt", request.Method);
    Equal("thread-1", String(request.Params, "threadId"));
    Equal("turn-1", String(request.Params, "turnId"));
}

static Task MessageAndItemNotificationsAreProjected()
{
    var server = new FakeAgentAppServer();
    var runtime = new CodexAgentRuntime(server);
    var events = new List<AgentEvent>();
    runtime.EventReceived += (_, agentEvent) => events.Add(agentEvent);

    server.EmitNotification("item/agentMessage/delta", """
        {"threadId":"thread-1","turnId":"turn-1","itemId":"message-1","delta":"你好"}
        """);
    server.EmitNotification("item/started", """
        {
          "threadId":"thread-1","turnId":"turn-1","startedAtMs":10,
          "item":{"id":"command-1","type":"commandExecution","command":"git status","status":"inProgress"}
        }
        """);
    server.EmitNotification("item/completed", """
        {
          "threadId":"thread-1","turnId":"turn-1","completedAtMs":20,
          "item":{"id":"message-1","type":"agentMessage","text":"你好"}
        }
        """);

    var delta = IsType<AgentMessageDeltaEvent>(events[0]);
    Equal("你好", delta.Delta);
    var tool = IsType<AgentToolStateChangedEvent>(events[1]);
    Equal(AgentToolKind.CommandExecution, tool.ToolKind);
    Equal(AgentToolStatus.InProgress, tool.Status);
    Equal("git status", tool.Item.DisplayName);
    var item = IsType<AgentItemStateChangedEvent>(events[2]);
    Equal(AgentItemKind.AgentMessage, item.Item.Kind);
    Equal(AgentItemLifecycle.Completed, item.Lifecycle);

    return DisposeAsync(runtime, server);
}

static Task ToolProgressNotificationsAreProjected()
{
    var server = new FakeAgentAppServer();
    var runtime = new CodexAgentRuntime(server);
    var events = new List<AgentEvent>();
    runtime.EventReceived += (_, agentEvent) => events.Add(agentEvent);

    server.EmitNotification("item/commandExecution/outputDelta", """
        {"threadId":"thread-1","turnId":"turn-1","itemId":"command-1","delta":"output"}
        """);
    server.EmitNotification("item/mcpToolCall/progress", """
        {"threadId":"thread-1","turnId":"turn-1","itemId":"mcp-1","message":"working"}
        """);
    server.EmitNotification("item/fileChange/patchUpdated", """
        {"threadId":"thread-1","turnId":"turn-1","itemId":"file-1","changes":[{"path":"a.cs"}]}
        """);

    var command = IsType<AgentToolProgressEvent>(events[0]);
    Equal(AgentToolKind.CommandExecution, command.ToolKind);
    Equal("output", command.Message);
    var mcp = IsType<AgentToolProgressEvent>(events[1]);
    Equal(AgentToolKind.McpToolCall, mcp.ToolKind);
    Equal("working", mcp.Message);
    var file = IsType<AgentToolProgressEvent>(events[2]);
    Equal(AgentToolKind.FileChange, file.ToolKind);
    Equal(JsonValueKind.Array, file.Data!.Value.ValueKind);

    return DisposeAsync(runtime, server);
}

static Task TurnStateNotificationsAreProjected()
{
    var server = new FakeAgentAppServer();
    var runtime = new CodexAgentRuntime(server);
    var events = new List<AgentEvent>();
    runtime.EventReceived += (_, agentEvent) => events.Add(agentEvent);

    server.EmitNotification("turn/started", """
        {"threadId":"thread-1","turn":{"id":"turn-1","status":"inProgress","items":[]}}
        """);
    server.EmitNotification("turn/completed", """
        {"threadId":"thread-1","turn":{"id":"turn-1","status":"failed","items":[],"error":{"message":"boom"}}}
        """);

    var started = IsType<AgentTurnStateChangedEvent>(events[0]);
    Equal(AgentTurnStatus.InProgress, started.Status);
    var completed = IsType<AgentTurnStateChangedEvent>(events[1]);
    Equal(AgentTurnStatus.Failed, completed.Status);
    Equal("boom", completed.ErrorMessage);

    return DisposeAsync(runtime, server);
}

static Task FailedTurnPublicEventDoesNotLeakProviderDiagnostics()
{
    var server = new FakeAgentAppServer();
    var runtime = new CodexAgentRuntime(server);
    var events = new List<AgentEvent>();
    runtime.EventReceived += (_, agentEvent) => events.Add(agentEvent);
    var tokenMarker = "failed-turn-token-marker";
    var payloadMarker = "failed-turn-payload-marker";

    server.EmitNotification(
        "turn/completed",
        JsonSerializer.Serialize(new
        {
            threadId = "thread-1",
            turn = new
            {
                id = "turn-1",
                status = "failed",
                items = Array.Empty<object>(),
                error = new
                {
                    message = "Authorization=Bearer " + tokenMarker
                        + " "
                        + @"C:\Users\turn-user\failure.log",
                },
                providerDebugPayload = payloadMarker,
            },
        }));

    var failed = IsType<AgentTurnStateChangedEvent>(Single(events.ToArray()));
    Equal(AgentTurnStatus.Failed, failed.Status);
    var publicText = failed.ErrorMessage + " " + failed.Turn.GetRawText();
    foreach (var protectedValue in new[]
             {
                 tokenMarker,
                 payloadMarker,
                 "turn-user",
                 "failure.log",
             })
    {
        True(
            publicText.IndexOf(protectedValue, StringComparison.OrdinalIgnoreCase) < 0,
            "Failed turn public event leaked Provider diagnostics.");
    }

    True(
        publicText.Contains("[redacted-token]", StringComparison.Ordinal)
        && publicText.Contains("[redacted-path]", StringComparison.Ordinal),
        "Failed turn public event did not preserve bounded redaction placeholders.");
    Equal(
        DiagnosticDataClassification.RemoteError,
        failed.ErrorDiagnosticClassification);
    True(
        (failed.ErrorDiagnosticRedactions & DiagnosticRedactionKinds.Token) != 0
        && (failed.ErrorDiagnosticRedactions & DiagnosticRedactionKinds.Path) != 0,
        "Failed turn public event did not expose numeric redaction evidence.");
    return DisposeAsync(runtime, server);
}

static async Task ApprovalRequestsAreProjectedAndForwarded()
{
    await using var server = new FakeAgentAppServer();
    await using var runtime = new CodexAgentRuntime(server);
    var events = new List<AgentEvent>();
    runtime.EventReceived += (_, agentEvent) => events.Add(agentEvent);
    runtime.CommandApprovalRequested += (_, _) => ValueTask.FromResult<CommandApprovalResponse?>(
        CommandApprovalResponse.AcceptOnce);
    runtime.FileChangeApprovalRequested += (_, _) => ValueTask.FromResult<FileChangeApprovalResponse?>(
        new FileChangeApprovalResponse(FileChangeApprovalDecision.Accept));
    runtime.PermissionsApprovalRequested += (_, _) => ValueTask.FromResult<PermissionsApprovalResponse?>(
        new PermissionsApprovalResponse(new PermissionProfile(), PermissionGrantScope.Turn));
    runtime.CadApprovalRequested += (approval, _) => ValueTask.FromResult<CadApprovalResponse?>(
        new CadApprovalResponse(
            CadApprovalDecision.Accept,
            approval.Request.ApprovalId,
            approval.Request.NormalizedPlanHash));

    var command = new CommandApprovalRequest(
        "command-1",
        10,
        "thread-1",
        "turn-1",
        Command: "git status",
        WorkingDirectory: "C:\\work");
    var file = new FileChangeApprovalRequest("file-1", 11, "thread-1", "turn-1");
    var permissions = new PermissionsApprovalRequest(
        "C:\\work",
        "permissions-1",
        new PermissionProfile(),
        12,
        "thread-1",
        "turn-1");
    var cad = new CadApprovalRequest(
        "cad-1",
        "thread-1",
        "turn-1",
        new CadDocumentIdentity("doc-1", "fingerprint", 4),
        "plan-hash",
        "R3",
        1000,
        new CadChangeSummary(1, 0, 0, "add line"));

    Equal(CommandApprovalDecisionKind.Accept, (await server.RequestCommandApprovalAsync(command))!.Kind);
    Equal(FileChangeApprovalDecision.Accept, (await server.RequestFileApprovalAsync(file))!.Decision);
    Equal(PermissionGrantScope.Turn, (await server.RequestPermissionsApprovalAsync(permissions))!.Scope);
    Equal(CadApprovalDecision.Accept, (await server.RequestCadApprovalAsync(cad))!.Decision);
    IsType<AgentCommandApprovalRequestedEvent>(events[0]);
    IsType<AgentFileChangeApprovalRequestedEvent>(events[1]);
    IsType<AgentPermissionsApprovalRequestedEvent>(events[2]);
    IsType<AgentCadApprovalRequestedEvent>(events[3]);
}

static Task MalformedNotificationIsIsolated()
{
    var server = new FakeAgentAppServer();
    var runtime = new CodexAgentRuntime(server);
    AgentEventProjectionFailedEventArgs? failure = null;
    runtime.ProjectionFailed += (_, args) => failure = args;

    server.EmitNotification("item/agentMessage/delta", """
        {"threadId":"thread-1","turnId":"turn-1","itemId":"message-1"}
        """);

    NotNull(failure);
    Equal("item/agentMessage/delta", failure!.Method);
    return DisposeAsync(runtime, server);
}

static Task EventObserverFailureIsIsolated()
{
    var server = new FakeAgentAppServer();
    var runtime = new CodexAgentRuntime(server);
    var delivered = 0;
    var observerFailures = 0;
    runtime.EventReceived += (_, _) => throw new InvalidOperationException("observer failed");
    runtime.EventReceived += (_, _) => delivered++;
    runtime.EventObserverFailed += (_, _) => observerFailures++;

    server.EmitNotification("item/agentMessage/delta", """
        {"threadId":"thread-1","turnId":"turn-1","itemId":"message-1","delta":"x"}
        """);

    Equal(1, delivered);
    Equal(1, observerFailures);
    return DisposeAsync(runtime, server);
}

static Task RuntimeDiagnosticEventsDoNotRetainRawExceptions()
{
    var server = new FakeAgentAppServer();
    var runtime = new CodexAgentRuntime(server);
    AgentEventProjectionFailedEventArgs? projectionFailure = null;
    AgentEventObserverFailedEventArgs? observerFailure = null;
    var messageMarker = "runtime-observer-message-marker";
    var innerMarker = "runtime-observer-inner-marker";
    var dataMarker = "runtime-observer-data-marker";
    var sourceFailure = new InvalidOperationException(
        "Bear" + "er " + messageMarker + " " + @"C:\Users\runtime-user\fault.log",
        new InvalidDataException(innerMarker));
    sourceFailure.Data["credential"] = dataMarker;

    runtime.ProjectionFailed += (_, args) => projectionFailure = args;
    runtime.EventReceived += (_, _) => throw sourceFailure;
    runtime.EventObserverFailed += (_, args) => observerFailure = args;

    server.EmitNotification("item/agentMessage/delta", """
        {"threadId":"thread-1","turnId":"turn-1","itemId":"message-1"}
        """);
    NotNull(projectionFailure);
    AssertSafeRuntimeDiagnostic(
        projectionFailure!.Exception,
        projectionFailure.DiagnosticClassification,
        projectionFailure.DiagnosticRedactions,
        Array.Empty<string>());

    server.EmitNotification("item/agentMessage/delta", """
        {"threadId":"thread-1","turnId":"turn-1","itemId":"message-1","delta":"x"}
        """);
    NotNull(observerFailure);
    AssertSafeRuntimeDiagnostic(
        observerFailure!.Exception,
        observerFailure.DiagnosticClassification,
        observerFailure.DiagnosticRedactions,
        new[] { messageMarker, innerMarker, dataMarker, "runtime-user" });
    True(
        !ReferenceEquals(sourceFailure, observerFailure.Exception),
        "Runtime observer diagnostics retained the original exception.");
    True(
        (observerFailure.DiagnosticRedactions & DiagnosticRedactionKinds.Token) != 0
        && (observerFailure.DiagnosticRedactions & DiagnosticRedactionKinds.Path) != 0,
        "Runtime observer diagnostics did not retain numeric redaction evidence.");

    return DisposeAsync(runtime, server);
}

static Task ObserverFailureDiagnosticsDoNotRetainRawAgentEvent()
{
    var server = new FakeAgentAppServer();
    var runtime = new CodexAgentRuntime(server);
    AgentEvent? sourceEvent = null;
    AgentEventObserverFailedEventArgs? observerFailure = null;
    var payloadMarker = "observer-event-payload-marker";
    runtime.EventReceived += (_, agentEvent) =>
    {
        sourceEvent = agentEvent;
        throw new InvalidOperationException("observer failed");
    };
    runtime.EventObserverFailed += (_, args) => observerFailure = args;

    server.EmitNotification(
        "item/agentMessage/delta",
        JsonSerializer.Serialize(new
        {
            threadId = "thread-" + payloadMarker,
            turnId = "turn-" + payloadMarker,
            itemId = "item-" + payloadMarker,
            delta = "Authorization=Bearer " + payloadMarker,
        }));

    NotNull(sourceEvent);
    NotNull(observerFailure);
    True(
        !ReferenceEquals(sourceEvent, observerFailure!.AgentEvent),
        "Observer failure diagnostics retained the original Agent event object.");
    True(
        observerFailure.AgentEvent
            .ToString()
            .IndexOf(payloadMarker, StringComparison.OrdinalIgnoreCase) < 0,
        "Observer failure diagnostics retained the original Agent event payload.");
    return DisposeAsync(runtime, server);
}

static void AssertSafeRuntimeDiagnostic(
    Exception exception,
    DiagnosticDataClassification classification,
    DiagnosticRedactionKinds redactions,
    IReadOnlyList<string> protectedMarkers)
{
    True(
        exception.InnerException is null
        && exception.Data.Count == 0
        && string.IsNullOrEmpty(exception.StackTrace),
        "Runtime diagnostics retained a raw exception graph.");
    var publicDiagnostic = exception.Message + " " + exception;
    foreach (var marker in protectedMarkers)
    {
        True(
            publicDiagnostic.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0,
            "Runtime diagnostics leaked a protected marker.");
    }

    True(
        Enum.IsDefined(classification)
        && redactions >= DiagnosticRedactionKinds.None,
        "Runtime diagnostics did not expose stable structured metadata.");
}

// ---------------- M4.1 策略接入（真实调用链） ----------------

static ResolvedAgentPolicy BuildTestPolicy(bool lockModel = false)
{
    var machine = new AgentPolicyLayerDocument
    {
        Layer = AgentPolicyLayers.MachinePolicy,
        AllowedModels = new[] { "gpt-test", "gpt-test-mini" },
        DefaultModel = "gpt-test",
        AllowedReasoningEfforts = new[] { AgentReasoningEfforts.Medium, AgentReasoningEfforts.High },
        DefaultReasoningEffort = AgentReasoningEfforts.Medium,
        LockModel = lockModel,
    };
    var resolution = AgentPolicyResolver.Resolve(machine, null, null);
    True(resolution.Accepted && resolution.Policy is not null, "test policy must resolve");
    return resolution.Policy!;
}

static async Task<string> CaptureThreadStartFailure(AgentRuntimeOptions options)
{
    await using var server = new FakeAgentAppServer();
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-1"}}
        """);
    await using var runtime = new CodexAgentRuntime(server, options);

    var code = string.Empty;
    try
    {
        _ = await runtime.CreateThreadAsync();
    }
    catch (AgentPolicyViolationException exception)
    {
        code = exception.ErrorCode;
    }

    // 最重要的断言：被拒绝的值绝不会产生任何出站请求。
    Equal(0, server.Requests.Count);
    return code;
}

static async Task PolicyBlocksUnsafeModelStringWithoutPolicy()
{
    // 未配置策略时也必须拒绝危险形态，否则任意字符串会进入 Codex 进程参数。
    foreach (var hostile in new[] { "model with space", "model\"quote", "..\\..\\escape", "a;rm -rf" })
    {
        var code = await CaptureThreadStartFailure(new AgentRuntimeOptions { Model = hostile });
        Equal(AgentPolicyErrorCodes.ModelInvalid, code);
    }
}

static async Task PolicyBlocksModelOutsideAllowList()
{
    var code = await CaptureThreadStartFailure(new AgentRuntimeOptions
    {
        Model = "gpt-not-allowed",
        AgentPolicy = BuildTestPolicy(),
    });
    Equal(AgentPolicyErrorCodes.ModelNotAllowed, code);
}

static async Task PolicyBlocksDivergentModelWhenLocked()
{
    // 白名单内但偏离受信默认值，在管理员锁定下同样拒绝。
    var code = await CaptureThreadStartFailure(new AgentRuntimeOptions
    {
        Model = "gpt-test-mini",
        AgentPolicy = BuildTestPolicy(lockModel: true),
    });
    Equal(AgentPolicyErrorCodes.LockedByHigherLayer, code);
}

static async Task PolicyAcceptedModelIsWhatReachesTheWire()
{
    await using var server = new FakeAgentAppServer();
    server.QueueResponse("thread/start", """
        {"thread":{"id":"thread-1"}}
        """);
    server.QueueResponse("turn/start", """
        {"turn":{"id":"turn-1","status":"inProgress","items":[]}}
        """);
    // 未显式指定模型：策略默认值必须成为真正下发的值。
    await using var runtime = new CodexAgentRuntime(
        server,
        new AgentRuntimeOptions { AgentPolicy = BuildTestPolicy() });

    _ = await runtime.CreateThreadAsync();
    _ = await runtime.StartTurnAsync("thread-1", "绘制一条直线");

    Equal(2, server.Requests.Count);
    Equal("gpt-test", String(server.Requests[0].Params, "model"));
    Equal("gpt-test", String(server.Requests[1].Params, "model"));
}

static void AssertSafeThreadRequest(SentRequest request, string method, string? expectedThreadId)
{
    Equal(method, request.Method);
    Equal("read-only", String(request.Params, "sandbox"));
    Equal("on-request", String(request.Params, "approvalPolicy"));
    Equal("user", String(request.Params, "approvalsReviewer"));
    Equal(false, request.Params.TryGetProperty("cwd", out _));
    Equal(false, request.Params.TryGetProperty("runtimeWorkspaceRoots", out _));
    Equal("gpt-test", String(request.Params, "model"));
    Equal("openai", String(request.Params, "modelProvider"));
    if (expectedThreadId is not null)
    {
        Equal(expectedThreadId, String(request.Params, "threadId"));
    }
}

static async Task DisposeAsync(CodexAgentRuntime runtime, FakeAgentAppServer server)
{
    await runtime.DisposeAsync();
    await server.DisposeAsync();
}

static string String(JsonElement element, string property)
    => element.GetProperty(property).GetString()
        ?? throw new InvalidOperationException($"Property '{property}' was null.");

static JsonElement ResolutionResult(ServerRequestResolution resolution)
{
    NotNull(resolution.Result);
    return JsonSerializer.SerializeToElement(
        resolution.Result,
        resolution.Result!.GetType(),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

static T Single<T>(IReadOnlyList<T> values)
{
    Equal(1, values.Count);
    return values[0];
}

static T IsType<T>(object value)
{
    if (value is T typed)
    {
        return typed;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}, actual {value.GetType().Name}.");
}

static void NotNull(object? value)
{
    if (value is null)
    {
        throw new InvalidOperationException("Expected non-null value.");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Throws<TException>(Action action)
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

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task ThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }
}

internal sealed record SentRequest(string Method, JsonElement Params);

internal sealed class FunctionalCadProposalBroker(
    Func<AgentCadOperationBatchProposal, AgentCadProposalResult> resultFactory) : IAgentCadProposalBroker
{
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public ValueTask<AgentCadProposalResult> ExecuteAsync(
        AgentCadOperationBatchProposal proposal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _callCount);
        return ValueTask.FromResult(resultFactory(proposal));
    }
}

internal sealed class FunctionalCadDrawingQueryBroker(
    Func<AgentCadDrawingQuery, AgentCadDrawingQueryResult> resultFactory)
    : IAgentCadDrawingQueryBroker
{
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public AgentCadDrawingQuery? LastQuery { get; private set; }

    public ValueTask<AgentCadDrawingQueryResult> ExecuteAsync(
        AgentCadDrawingQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _callCount);
        LastQuery = query;
        return ValueTask.FromResult(resultFactory(query));
    }
}

internal sealed class BlockingCadProposalBroker : IAgentCadProposalBroker
{
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public async ValueTask<AgentCadProposalResult> ExecuteAsync(
        AgentCadOperationBatchProposal proposal,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return AgentCadProposalResult.Applied(proposal);
    }
}

internal sealed class IgnoringCancellationCadProposalBroker : IAgentCadProposalBroker
{
    private readonly TaskCompletionSource<AgentCadProposalResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cancellationObserved =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AgentCadOperationBatchProposal? _proposal;
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public Task Started => _started.Task;

    public Task CancellationObserved => _cancellationObserved.Task;

    public WeakReference ExecutionReference => new(_completion.Task);

    public ValueTask<AgentCadProposalResult> ExecuteAsync(
        AgentCadOperationBatchProposal proposal,
        CancellationToken cancellationToken)
    {
        Volatile.Write(ref _proposal, proposal);
        Interlocked.Increment(ref _callCount);
        _ = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            _cancellationObserved);
        _started.TrySetResult();
        return new ValueTask<AgentCadProposalResult>(_completion.Task);
    }

    public void CompleteApplied(string message)
    {
        var proposal = Volatile.Read(ref _proposal)
            ?? throw new InvalidOperationException("The broker has not received a proposal.");
        _completion.TrySetResult(AgentCadProposalResult.Applied(proposal, message));
    }

    public void Fail(Exception exception)
        => _completion.TrySetException(exception);
}

internal sealed class FakeAgentAppServer : IAgentAppServer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<string, Queue<JsonElement>> _responses = new(StringComparer.Ordinal);
    private long _approvalRequestId;
    private int _disposed;

    public event EventHandler<AppServerNotification>? NotificationReceived;

    public event CommandApprovalRequestedHandler? CommandApprovalRequested;

    public event FileChangeApprovalRequestedHandler? FileChangeApprovalRequested;

    public event PermissionsApprovalRequestedHandler? PermissionsApprovalRequested;

    public event CadApprovalRequestedHandler? CadApprovalRequested;

    public event ServerRequestReceivedHandler? ServerRequestReceived;

    public int StartCalls { get; private set; }

    public List<SentRequest> Requests { get; } = new();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        StartCalls++;
        return Task.CompletedTask;
    }

    public Task<TResult> SendRequestAsync<TResult>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var serializedParams = parameters is null
            ? JsonSerializer.SerializeToElement(new { }, SerializerOptions)
            : JsonSerializer.SerializeToElement(parameters, parameters.GetType(), SerializerOptions);
        Requests.Add(new SentRequest(method, serializedParams));

        if (!_responses.TryGetValue(method, out var responses) || responses.Count == 0)
        {
            throw new InvalidOperationException("No fake response queued for " + method + ".");
        }

        var response = responses.Dequeue().Clone();
        if (typeof(TResult) == typeof(JsonElement))
        {
            return Task.FromResult((TResult)(object)response);
        }

        var result = JsonSerializer.Deserialize<TResult>(response.GetRawText(), SerializerOptions)
            ?? throw new InvalidOperationException("Fake response deserialized to null.");
        return Task.FromResult(result);
    }

    public void QueueResponse(string method, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!_responses.TryGetValue(method, out var responses))
        {
            responses = new Queue<JsonElement>();
            _responses.Add(method, responses);
        }

        responses.Enqueue(document.RootElement.Clone());
    }

    public void EmitNotification(string method, string paramsJson)
    {
        using var document = JsonDocument.Parse(paramsJson);
        NotificationReceived?.Invoke(this, new AppServerNotification(method, document.RootElement.Clone()));
    }

    public ValueTask<CommandApprovalResponse?> RequestCommandApprovalAsync(CommandApprovalRequest request)
        => InvokeAsync(CommandApprovalRequested, request);

    public ValueTask<FileChangeApprovalResponse?> RequestFileApprovalAsync(FileChangeApprovalRequest request)
        => InvokeAsync(FileChangeApprovalRequested, request);

    public ValueTask<PermissionsApprovalResponse?> RequestPermissionsApprovalAsync(PermissionsApprovalRequest request)
        => InvokeAsync(PermissionsApprovalRequested, request);

    public ValueTask<CadApprovalResponse?> RequestCadApprovalAsync(CadApprovalRequest request)
        => InvokeAsync(CadApprovalRequested, request);

    public async ValueTask<ServerRequestResolution?> RequestServerAsync(string method, string paramsJson)
    {
        if (ServerRequestReceived is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(paramsJson);
        var request = new AppServerServerRequest(NextRequestId(), method, document.RootElement.Clone());
        foreach (ServerRequestReceivedHandler handler in ServerRequestReceived.GetInvocationList())
        {
            var response = await handler(request, CancellationToken.None);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    private async ValueTask<CommandApprovalResponse?> InvokeAsync(
        CommandApprovalRequestedHandler? handlers,
        CommandApprovalRequest request)
    {
        if (handlers is null)
        {
            return null;
        }

        var approval = new RpcApprovalEvent<CommandApprovalRequest>(NextRequestId(), request);
        foreach (CommandApprovalRequestedHandler handler in handlers.GetInvocationList())
        {
            var response = await handler(approval, CancellationToken.None);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    private async ValueTask<FileChangeApprovalResponse?> InvokeAsync(
        FileChangeApprovalRequestedHandler? handlers,
        FileChangeApprovalRequest request)
    {
        if (handlers is null)
        {
            return null;
        }

        var approval = new RpcApprovalEvent<FileChangeApprovalRequest>(NextRequestId(), request);
        foreach (FileChangeApprovalRequestedHandler handler in handlers.GetInvocationList())
        {
            var response = await handler(approval, CancellationToken.None);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    private async ValueTask<PermissionsApprovalResponse?> InvokeAsync(
        PermissionsApprovalRequestedHandler? handlers,
        PermissionsApprovalRequest request)
    {
        if (handlers is null)
        {
            return null;
        }

        var approval = new RpcApprovalEvent<PermissionsApprovalRequest>(NextRequestId(), request);
        foreach (PermissionsApprovalRequestedHandler handler in handlers.GetInvocationList())
        {
            var response = await handler(approval, CancellationToken.None);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    private async ValueTask<CadApprovalResponse?> InvokeAsync(
        CadApprovalRequestedHandler? handlers,
        CadApprovalRequest request)
    {
        if (handlers is null)
        {
            return null;
        }

        var approval = new RpcApprovalEvent<CadApprovalRequest>(NextRequestId(), request);
        foreach (CadApprovalRequestedHandler handler in handlers.GetInvocationList())
        {
            var response = await handler(approval, CancellationToken.None);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    private JsonRpcId NextRequestId() => new(Interlocked.Increment(ref _approvalRequestId));
}
