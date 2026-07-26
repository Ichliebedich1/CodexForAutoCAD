using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Codex.AutoCAD.AgentHost;
using Codex.AutoCAD.AgentRuntime;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AppServer.Protocol;
using Codex.AutoCAD.Bridge;
using Codex.AutoCAD.Bridge.Client;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;
using Codex.AutoCAD.AgentLauncher;

if (args.Length > 0
    && string.Equals(
        args[0],
        "audit-retention-crash-worker",
        StringComparison.Ordinal))
{
    return AgentHostBridgeSessionSpecs.RunAuditRetentionCrashWorker(args);
}

if (args.Length >= 2
    && string.Equals(args[0], "login", StringComparison.Ordinal)
    && string.Equals(args[1], "--with-access-token", StringComparison.Ordinal))
{
    return RunFakeCodexLogin();
}

var specs = new (string Name, Func<Task> Run)[]
{
    ("POLICY-M41-030 缺失全部策略层fail-closed",
        AgentHostPolicyStoreSpecs.MissingEveryLayerFailsClosed),
    ("POLICY-M41-031 仅机器策略即可解析并如实报告层存在性",
        AgentHostPolicyStoreSpecs.MachinePolicyAloneResolves),
    ("POLICY-M41-032 用户层可收窄但不能扩大白名单",
        AgentHostPolicyStoreSpecs.UserLayerNarrowsButCannotWiden),
    ("POLICY-M41-033 管理员锁定阻止用户层覆盖",
        AgentHostPolicyStoreSpecs.AdministratorLockBlocksUserLayer),
    ("POLICY-M41-034 损坏JSON未知字段和伪造层声明fail-closed",
        AgentHostPolicyStoreSpecs.MalformedAndUnknownFieldsFailClosed),
    ("POLICY-M41-035 旧schema版本fail-closed",
        AgentHostPolicyStoreSpecs.OutdatedSchemaVersionFailsClosed),
    ("POLICY-M41-036 超限策略文件fail-closed",
        AgentHostPolicyStoreSpecs.OversizedPolicyFileFailsClosed),
    ("POLICY-M41-037 相对路径UNC和设备路径被拒绝",
        AgentHostPolicyStoreSpecs.UnsafePolicyPathsAreRejected),
    ("POLICY-M41-038 产品入口使用固定且互异的策略位置",
        AgentHostPolicyStoreSpecs.ProductEntryPointUsesFixedLocations),
    ("POLICY-M41-039 启动失败脱敏且区分未配置与配置损坏",
        AgentHostPolicyStoreSpecs.StartupFailureIsRedactedAndDistinguishesUnconfigured),
    ("AUDIT-M413-001 链MAC密钥生成后可稳定重载",
        AgentHostAuditChainKeySpecs.CreatesStableKeyAndReloadsIt),
    ("AUDIT-M413-002 密钥损坏截断或退化时拒绝重新生成",
        AgentHostAuditChainKeySpecs.CorruptedOrTruncatedKeyRefusesToRegenerate),
    ("AUDIT-M413-003 不同审计存储不共享链MAC密钥",
        AgentHostAuditChainKeySpecs.DistinctRootsProduceDistinctKeys),
    ("AUDIT-M413-004 释放后的密钥不可再签名",
        AgentHostAuditChainKeySpecs.DisposedKeyCannotSign),
    ("AUDIT-M413-005 同用户篡改边界如实声明为未解决",
        AgentHostAuditChainKeySpecs.ThreatModelBoundaryIsDeclaredHonestly),
    ("AUDIT-M413-006 锚点MAC往返且检出内容篡改",
        AgentHostAuditAnchorMacSpecs.AnchorMacRoundTripsAndDetectsTampering),
    ("AUDIT-M413-007 删除或破坏MAC不能降级校验",
        AgentHostAuditAnchorMacSpecs.MissingOrCorruptMacCannotDowngradeVerification),
    ("AUDIT-M413-008 无密钥存储保持向后兼容",
        AgentHostAuditAnchorMacSpecs.StoresWithoutKeyRemainBackwardCompatible),
    ("AUDIT-M413-009 外来密钥不能验证本存储锚点",
        AgentHostAuditAnchorMacSpecs.ForeignKeyCannotVerifyAnchor),
    ("AUDIT-M413-010 锚点重写时MAC同步更新",
        AgentHostAuditAnchorMacSpecs.RewritingAnchorRefreshesMac),
    ("AUDIT-M413-011 只读密钥入口绝不创建密钥",
        AgentHostAuditAnchorMacSpecs.ReadOnlyKeyLookupNeverCreatesAKey),
    ("AUDIT-M412-001 CAD schema扩展不改变既有事件哈希",
        AgentHostCadAuditSchemaSpecs.ExistingEventHashesAreUnchangedByTheCadExtension),
    ("AUDIT-M412-002 CAD字段全部纳入哈希链",
        AgentHostCadAuditSchemaSpecs.CadFieldsAreCoveredByTheHashChain),
    ("AUDIT-M412-003 CAD事件类型覆盖写入全链",
        AgentHostCadAuditSchemaSpecs.CadEventTypesCoverTheWholeWriteChain),
    ("AUDIT-M412-004 CAD字段白名单冻结且不含敏感字段",
        AgentHostCadAuditSchemaSpecs.CadFieldWhitelistIsFrozen),
    ("AgentHost审计日志为有界内容脱敏JSONL",
        AgentHostBridgeSessionSpecs.AuditLogIsBoundedContentFreeJsonl),
    ("AgentHost并发审计写入保持完整JSONL和单调序号",
        AgentHostBridgeSessionSpecs.AuditConcurrentWritesAreSequentialAndComplete),
    ("AgentHost部分写入失败后保持截断可检测且永久失败关闭",
        AgentHostBridgeSessionSpecs.AuditPartialWriteFailsClosedAndCannotResume),
    ("AgentHost锚点持久化失败后保持链不一致可检测且永久失败关闭",
        AgentHostBridgeSessionSpecs.AuditAnchorPersistenceFailureFailsClosedAndIsDetectable),
    ("AgentHost审计哈希链检测删除插入修改截断锚点和跨段重排",
        AgentHostBridgeSessionSpecs.AuditHashChainDetectsTamperingAcrossSegments),
    ("AgentHost生产审计目录持久化独立链锚点",
        AgentHostBridgeSessionSpecs.AuditFileAnchorTracksDurableChainHead),
    ("AgentHost脱敏导出先验链且省略Provider身份和payload",
        AgentHostBridgeSessionSpecs.AuditRedactedExportVerifiesChainAndOmitsProviderIdentity),
    ("AgentHost受控审计导出仅输出完整链且失败不泄漏半份JSON",
        AgentHostBridgeSessionSpecs.AuditExportServiceBuffersVerifiedOutput),
    ("AgentHost只读审计保留规划保护非完整证据且无文件副作用",
        AgentHostBridgeSessionSpecs.AuditRetentionPlannerIsReadOnlyAndConservative),
    ("AgentHost审计保留控制区未知或恶意artifact明确转人工复核并拒绝清理",
        AgentHostBridgeSessionSpecs.AuditRetentionControlStatusFailsClosedForUnknownArtifacts),
    ("AgentHost审计保留CLI拒绝非法参数并返回稳定错误",
        AgentHostBridgeSessionSpecs.AuditRetentionPlanCliRejectsInvalidArguments),
    ("AgentHost审计CLI未预期失败统一脱敏",
        AgentHostBridgeSessionSpecs.AuditCliUnexpectedFailureIsStructuredAndSanitized),
    ("AgentHost未知命令诊断先分类脱敏再输出",
        AgentHostBridgeSessionSpecs.UnknownCommandDiagnosticIsSanitized),
    ("AgentHost通用CLI失败返回稳定阶段和脱敏元数据",
        AgentHostBridgeSessionSpecs.AgentHostCliFailureIsStructuredAndSanitized),
    ("AgentHost协议故障stderr只输出稳定分类和数值元数据",
        AgentHostBridgeSessionSpecs.ProtocolFaultStandardErrorIsStructuredAndSanitized),
    ("AgentHost bootstrap CLI失败不输出CLR类型名",
        AgentHostBridgeSessionSpecs.BootstrapCliFailureIsStructuredAndSanitized),
    ("AgentHost doctor成功响应不公开原始环境指纹",
        AgentHostBridgeSessionSpecs.DoctorStatusOmitsRawEnvironmentFingerprint),
    ("AgentHost受控审计清理只删除已确认候选且重复执行幂等",
        AgentHostBridgeSessionSpecs.AuditRetentionApplyDeletesOnlyApprovedAndIsIdempotent),
    ("AgentHost审计清理receipt收敛到有界检查点",
        AgentHostBridgeSessionSpecs.AuditRetentionReceiptsConvergeToBoundedCheckpoint),
    ("AgentHost审计清理receipt检查点恢复不重复累计",
        AgentHostBridgeSessionSpecs.AuditRetentionReceiptCheckpointRecoveryDoesNotDoubleCount),
    ("AgentHost审计清理receipt检查点先于删除耐久提交",
        AgentHostBridgeSessionSpecs.AuditRetentionReceiptCheckpointCommitsBeforeDeletion),
    ("AgentHost审计清理收敛已完成计划的冗余receipt临时文件",
        AgentHostBridgeSessionSpecs.AuditRetentionRemovesRedundantForeignReceiptTemporaryFile),
    ("AgentHost受控审计清理可从耐久日志恢复中断",
        AgentHostBridgeSessionSpecs.AuditRetentionApplyRecoversInterruptedJournal),
    ("AgentHost审计保留持久化I/O故障保持可恢复且重试只收敛一次",
        AgentHostBridgeSessionSpecs.AuditRetentionPersistenceIoFailuresConvergeOnce),
    ("AgentHost受控审计清理拒绝变化计划和篡改恢复",
        AgentHostBridgeSessionSpecs.AuditRetentionApplyRejectsChangedPlanAndTamperedRecovery),
    ("AgentHost受控审计清理串行化并发执行器",
        AgentHostBridgeSessionSpecs.AuditRetentionApplySerializesConcurrentExecutors),
    ("AgentHost受控审计清理在子进程强杀后由耐久日志恢复",
        AgentHostBridgeSessionSpecs.AuditRetentionApplyRecoversAfterProcessKill),
    ("AgentHost生产审计达到单段上限后自动轮转且链连续",
        AgentHostBridgeSessionSpecs.AuditAutomaticallyRotatesWithContinuousChain),
    ("AgentHost工作区清理后持久审计和独立锚点仍可验证",
        AgentHostBridgeSessionSpecs.AuditPersistsAfterSessionWorkspaceCleanup),
    ("AgentHost只读审计目录分类完整、不完整、损坏和锚点不匹配",
        AgentHostBridgeSessionSpecs.AuditCatalogClassifiesPersistentArtifacts),
    ("AgentHost审计不可继续时会终止Bridge会话",
        AgentHostBridgeSessionSpecs.AuditFailureTerminatesBridgeSession),
    ("AgentHost失败请求只记录稳定错误码",
        AgentHostBridgeSessionSpecs.FailedRequestAuditUsesStableErrorCode),
    ("AgentHost Codex预检与握手失败映射稳定错误码",
        AgentHostBridgeSessionSpecs.CodexHealthFailuresUseStableAuditCodes),
    ("AgentHost Codex握手超时会取消底层启动",
        AgentHostBridgeSessionSpecs.CodexHealthTimeoutCancelsUnderlyingStart),
    ("AgentHost会话CodexHome生成最小配置并按租约清理",
        AgentHostCodexSessionHomeSpecs.CreatesMinimalHomeAndCleansOnDispose),
    ("AgentHost凭据登录只经stdin并使用隔离home",
        AgentHostCodexSessionHomeSpecs.CodexAccessTokenLoginUsesStdin),
    ("AgentHost凭据登录失败、超时和取消均失败关闭且不泄露秘密",
        AgentHostCodexSessionHomeSpecs.CodexAccessTokenLoginFailuresFailClosed),
    ("AgentHost会话CodexHome拒绝非法身份和重复占用",
        AgentHostCodexSessionHomeSpecs.RejectsInvalidIdentityAndConcurrentOwner),
    ("AgentHost会话CodexHome失败映射稳定审计码",
        AgentHostCodexSessionHomeSpecs.FailuresUseStableAuditCodes),
    ("AgentHost审批请求审计不记录命令或路径",
        AgentHostBridgeSessionSpecs.ApprovalRequestAuditOmitsCommandAndPath),
    ("AgentHost长运行服务返回冻结v1能力", AgentHostCapabilitiesRoundTrip),
    ("AgentHost同一thread完成两轮上下文对话并映射assistant事件",
        AgentHostBridgeSessionSpecs.TwoContextTurnsReuseThreadAndMapAssistantEvents),
    ("AgentHost实际接收v2上下文并回显v2哈希",
        AgentHostBridgeSessionSpecs.V2ContextTurnUsesV2MethodAndEchoesHash),
    ("AgentHost取消审计关联系统请求与Provider回合",
        AgentHostBridgeSessionSpecs.CancellationAuditCorrelatesSystemAndProviderIds),
    ("AgentHost只读查询经认证反向Bridge往返且不暴露Host身份",
        AgentHostBridgeSessionSpecs.DrawingQueryFlowsThroughAuthenticatedReverseBridge),
    ("当前用户命名管道可完成请求响应", RequestResponseWorks),
    ("bootstrap方向密钥可完成具体Client到服务端认证", BootstrapDirectionKeysAuthenticateConcreteClient),
    ("通知可单向投递", NotificationWorks),
    ("Bridge请求与通知字符串投影不泄露JSON载荷", BridgeMessageStringProjectionsAreSafe),
    ("取消消息会取消远端请求", CancellationPropagates),
    ("远端错误被结构化返回", RemoteErrorPropagates),
    ("远端错误公共异常会分类脱敏且不保留原始诊断", RemoteErrorExceptionIsSanitized),
    ("Bridge Client公共异常会归一错误码并保留数值脱敏证据",
        BridgeClientExceptionIsSanitized),
    ("坏MAC被拒绝", BadMacIsRejected),
    ("重复序号被拒绝", ReplayedSequenceIsRejected),
    ("重复nonce被拒绝", ReplayedNonceIsRejected),
    ("乱序消息被拒绝", OutOfOrderSequenceIsRejected),
    ("超大入站帧在分配前被拒绝", OversizedIncomingFrameIsRejected),
    ("信封未知字段被拒绝", UnknownEnvelopeFieldIsRejected),
    ("信封重复字段被拒绝", DuplicateEnvelopeFieldIsRejected),
    ("信封字段大小写错误被拒绝", WrongCaseEnvelopeFieldIsRejected),
    ("信封尾随JSON被拒绝", TrailingEnvelopeJsonIsRejected),
    ("信封非法UTF-8被拒绝", InvalidEnvelopeUtf8IsRejected),
    ("超大出站帧被拒绝且不破坏序号", OversizedOutgoingFrameIsRejectedWithoutSequenceGap),
    ("非32字节会话密钥被拒绝", InvalidSecretLengthIsRejected),
    ("超过32层JSON被拒绝且连接可复用", ExcessiveJsonDepthIsRejected),
    ("出站pending上限原子拒绝且释放后恢复", PendingRequestLimitRejectsAndRecovers),
    ("入站active请求洪泛会fail-closed断开", ActiveRequestFloodAbortsConnection),
    ("通知handler洪泛会fail-closed断开", HandlerFloodAbortsConnection),
    ("内存双工生命周期：同步阻塞handler不会阻塞接收循环", SynchronouslyBlockingHandlerDoesNotBlockReceiveLoop),
    ("内存双工生命周期：忽略取消handler不阻塞并发Dispose", NonCooperativeHandlerDoesNotBlockConcurrentDispose),
    ("内存双工生命周期：迟到handler fault被观察且可诊断", LateHandlerFaultIsObservedAndDiagnosable),
    ("内存双工生命周期：非协作stream和sendGate关闭有界且延迟清密钥", NonCooperativeStreamAndSendGateUseBoundedSafeCleanup),
    ("容量槽位完成后可反复复用", CapacitySlotsRecoverAfterCompletion),
    ("出站通知队列上限原子拒绝且释放后恢复", PendingNotificationLimitRejectsAndRecovers),
    ("自定义帧上限拒绝超限且不破坏连接", ConfiguredFrameLimitRejectsAndRecovers),
    ("自定义入站帧上限在分配前拒绝", ConfiguredIncomingFrameLimitRejectsBeforeAllocation),
    ("通知handler异常会终止连接", NotificationHandlerFailureAbortsConnection),
    ("非法容量或关闭超时配置被拒绝", InvalidCapacityOptionsAreRejected),
    ("正常EOF会终止连接", NormalEofTerminatesConnection),
    ("部分写入失败会中止连接", PartialWriteFailureAbortsConnection),
    ("认证后协议异常会中止连接", AuthenticatedProtocolErrorAbortsConnection),
    ("IPC密钥副本在释放时清零", IpcSecretsAreZeroedOnDispose)
};

if (args.Length > 1)
{
    throw new ArgumentException("Bridge Specs 最多接受一个名称筛选参数。");
}

var selectedSpecs = args.Length == 0
    ? specs
    : specs.Where(spec => spec.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)).ToArray();
if (selectedSpecs.Length == 0)
{
    throw new ArgumentException($"没有匹配的 Bridge Spec：{args[0]}。");
}

var failed = 0;
foreach (var spec in selectedSpecs)
{
    try
    {
        await spec.Run();
        Console.WriteLine("PASS " + spec.Name);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + spec.Name + ": " + exception);
    }
}

Console.WriteLine($"{selectedSpecs.Length - failed}/{selectedSpecs.Length} specs passed");
return failed == 0 ? 0 : 1;

static int RunFakeCodexLogin()
{
    var home = Environment.GetEnvironmentVariable("CODEX_HOME");
    if (string.IsNullOrWhiteSpace(home) || !Directory.Exists(home))
    {
        return 2;
    }

    var modePath = Path.Combine(home, ".fake-login-mode");
    var mode = File.Exists(modePath)
        ? File.ReadAllText(modePath, Encoding.UTF8).Trim()
        : "success";
    using var input = Console.OpenStandardInput();
    using var buffer = new MemoryStream();
    var chunk = new byte[1024];
    try
    {
        while (true)
        {
            var read = input.Read(chunk, 0, chunk.Length);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > 4 * 1024)
            {
                return 3;
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        try
        {
            if (bytes.Length > 0 && bytes[^1] == (byte)'\n')
            {
                Array.Resize(ref bytes, bytes.Length - 1);
            }

            var digest = SHA256.HashData(bytes);
            try
            {
                var token = Encoding.UTF8.GetString(bytes);
                try
                {
                    var argumentsContainToken = Environment.GetCommandLineArgs()
                        .Any(argument => argument.Contains(token, StringComparison.Ordinal));
                    var environmentContainsToken = Environment.GetEnvironmentVariables()
                        .Cast<System.Collections.DictionaryEntry>()
                        .Any(entry => (entry.Value as string)?.Contains(
                            token,
                            StringComparison.Ordinal) == true);
                    File.WriteAllText(
                        Path.Combine(home, ".fake-login-observation"),
                        "argv=" + argumentsContainToken + ";env=" + environmentContainsToken,
                        new UTF8Encoding(false));
                }
                finally
                {
                    token = string.Empty;
                }

                File.WriteAllText(
                    Path.Combine(home, ".fake-login-sha256"),
                    Convert.ToHexString(digest),
                    new UTF8Encoding(false));
            }
            finally
            {
                Array.Clear(digest, 0, digest.Length);
            }
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }
    finally
    {
        Array.Clear(chunk, 0, chunk.Length);
    }

    if (string.Equals(mode, "fail", StringComparison.Ordinal))
    {
        return 17;
    }

    if (string.Equals(mode, "auth", StringComparison.Ordinal))
    {
        File.WriteAllText(
            Path.Combine(home, "auth.json"),
            "{\"unexpected\":true}",
            new UTF8Encoding(false));
        return 0;
    }

    if (string.Equals(mode, "hang", StringComparison.Ordinal))
    {
        Thread.Sleep(Timeout.Infinite);
    }

    return 0;
}

static async Task RequestResponseWorks()
{
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start((request, _) =>
    {
        Equal("cad.selection.read", request.Method);
        Equal("{\"handles\":[\"1A\"]}", request.BodyJson);
        return ValueTask.FromResult<string?>("{\"count\":1}");
    });
    client.Start();

    var response = await client.RequestAsync("cad.selection.read", "{\"handles\":[\"1A\"]}")
        .WaitAsync(TimeSpan.FromSeconds(5));
    Equal("{\"count\":1}", response);
}
static async Task BootstrapDirectionKeysAuthenticateConcreteClient()
{
    var keyPair = CreateBootstrapDirectionKeyPair();
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var acceptTask = NamedPipeBridge.AcceptOneAsync(keyPair.AgentKeys, timeout.Token);
        using var client = new AgentBridgeClient(
            keyPair.HostKeys,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
        await client.StartAsync(timeout.Token);
        await using var server = await acceptTask;
        server.Start((request, _) =>
        {
            Equal(AgentBridgeMethods.GetCapabilities, request.Method);
            return ValueTask.FromResult<string?>(JsonSerializer.Serialize(
                new AgentCapabilitiesResponse
                {
                    AgentInstanceId = "bootstrap-direction-agent",
                    Methods = new[]
                    {
                        AgentBridgeMethods.GetCapabilities,
                        AgentBridgeMethods.StartThread,
                        AgentBridgeMethods.StartTurn,
                        AgentBridgeMethods.InterruptTurn,
                        AgentBridgeMethods.ResolveApproval,
                    },
                    EventKinds = new[]
                    {
                        AgentBridgeEventKinds.ConnectionStateChanged,
                        AgentBridgeEventKinds.ThreadStarted,
                        AgentBridgeEventKinds.TurnStarted,
                        AgentBridgeEventKinds.AssistantMessageDelta,
                        AgentBridgeEventKinds.AssistantMessageCompleted,
                        AgentBridgeEventKinds.TurnCompleted,
                        AgentBridgeEventKinds.TurnFailed,
                        AgentBridgeEventKinds.TurnCancelled,
                    },
                    ApprovalDecisions = new[]
                    {
                        AgentBridgeApprovalDecisions.AllowOnce,
                        AgentBridgeApprovalDecisions.DeclineAndContinue,
                        AgentBridgeApprovalDecisions.DeclineAndCancelTurn,
                    },
                    CadWriteAvailable = false,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        });

        var response = await client.GetCapabilitiesAsync(
            new AgentCapabilitiesRequest
            {
                ClientName = "Codex.AutoCAD.Host.2016",
                ClientVersion = "0.2.0.0",
                HostTarget = "autocad-r20.1-net45-x64",
            },
            timeout.Token);
        Equal("bootstrap-direction-agent", response.AgentInstanceId);
        await client.StopAsync(CancellationToken.None);
    }
    finally
    {
        keyPair.HostKeys.Dispose();
        keyPair.AgentKeys.Dispose();
    }
}
static async Task AgentHostCapabilitiesRoundTrip()
{
    var keyPair = CreateBootstrapDirectionKeyPair();
    try
    {
        await using var appServer = new NoOpAgentAppServer();
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
            "agenthost-capabilities-spec",
            audit);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serviceTask = service.RunAsync(keyPair.AgentKeys, timeout.Token);
        using var client = new AgentBridgeClient(
            keyPair.HostKeys,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
        await client.StartAsync(timeout.Token);
        var response = await client.GetCapabilitiesAsync(
            new AgentCapabilitiesRequest
            {
                ClientName = "Codex.AutoCAD.Host.2016",
                ClientVersion = "0.2.0.0",
                HostTarget = "autocad-r20.1-net45-x64",
            },
            timeout.Token);

        Equal(AgentBridgeContractConstants.CurrentVersion, response.ContractVersion);
        Equal(AgentBridgeContractConstants.MinimumCompatibleVersion,
            response.MinimumCompatibleVersion);
        Equal("agenthost-capabilities-spec", response.AgentInstanceId);
        Equal(CadContextJsonV1Constants.Schema, response.CadContextSchema);
        Equal(CadContextJsonV1Constants.SchemaVersion, response.CadContextSchemaVersion);
        Equal(false, response.CadWriteAvailable);
        Equal(true, response.Methods.Contains(AgentBridgeMethods.GetCapabilities, StringComparer.Ordinal));
        Equal(true, response.Methods.Contains(AgentBridgeMethods.StartTurnV2, StringComparer.Ordinal));
        Equal(true, response.SupportedCadContextSchemas.Any(schema =>
            string.Equals(schema.Schema, CadContextJsonV1Constants.Schema, StringComparison.Ordinal)
            && schema.SchemaVersion == CadContextJsonV1Constants.SchemaVersion));
        Equal(true, response.SupportedCadContextSchemas.Any(schema =>
            string.Equals(schema.Schema, CadContextJsonV2Constants.Schema, StringComparison.Ordinal)
            && schema.SchemaVersion == CadContextJsonV2Constants.SchemaVersion));
        Equal(true, response.EventKinds.Contains(
            AgentBridgeEventKinds.ConnectionStateChanged,
            StringComparer.Ordinal));
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


static string CreateLowerHexIdentifier()
{
    return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}

static (AgentBootstrapDirectionKeys HostKeys, AgentBootstrapDirectionKeys AgentKeys)
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



static async Task NotificationWorks()
{
    var received = new TaskCompletionSource<BridgeNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (notification, _) =>
    {
        received.TrySetResult(notification);
        return ValueTask.CompletedTask;
    });
    client.Start();

    await client.NotifyAsync("cad.selection.changed", "{\"count\":2}");
    var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Equal("cad.selection.changed", notification.Method);
    Equal("{\"count\":2}", notification.BodyJson);
}

static Task BridgeMessageStringProjectionsAreSafe()
{
    var requestIdMarker = "bridge-request-id-secret-marker";
    var notificationIdMarker = "bridge-notification-id-secret-marker";
    var methodMarker = "bridge-method-secret-marker";
    var bodyMarker = "bridge-body-secret-marker";
    object[] messages =
    {
        new BridgeRequest(
            requestIdMarker,
            methodMarker,
            $$"""{"credential":"{{bodyMarker}}","path":"C:\\Users\\bridge-user\\private.json"}"""),
        new BridgeNotification(
            notificationIdMarker,
            methodMarker,
            $$"""{"accessToken":"{{bodyMarker}}","path":"\\\\server\\private"}"""),
    };

    foreach (var message in messages)
    {
        var diagnostic = message.ToString() ?? string.Empty;
        True(diagnostic.StartsWith(message.GetType().Name, StringComparison.Ordinal));
        foreach (var marker in new[]
                 {
                     requestIdMarker,
                     notificationIdMarker,
                     methodMarker,
                     bodyMarker,
                     "bridge-user",
                 })
        {
            False(diagnostic.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }

    Equal(
        "BridgeRequest { RequestIdConfigured = True, MethodConfigured = True, BodyJsonConfigured = True }",
        messages[0].ToString());
    Equal(
        "BridgeNotification { NotificationIdConfigured = True, MethodConfigured = True, BodyJsonConfigured = True }",
        messages[1].ToString());
    return Task.CompletedTask;
}

static async Task CancellationPropagates()
{
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(async (_, cancellationToken) =>
    {
        started.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled.TrySetResult();
            throw;
        }

        return "null";
    });
    client.Start();

    using var requestCancellation = new CancellationTokenSource();
    var request = client.RequestAsync("cad.long_operation", "{}", requestCancellation.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    requestCancellation.Cancel();

    await ThrowsAsync<OperationCanceledException>(() => request);
    await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task RemoteErrorPropagates()
{
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start((_, _) => throw new InvalidOperationException("模拟处理失败"));
    client.Start();

    var exception = await ThrowsAsync<BridgeRemoteException>(
        () => client.RequestAsync("cad.fail", "{}"));
    Equal("handler_error", exception.Code);
    Equal("远端请求处理失败。", exception.Message);
    False(exception.Message.Contains("模拟处理失败", StringComparison.Ordinal));
}

static Task RemoteErrorExceptionIsSanitized()
{
    const string secret =
        "Bearer bridge-secret C:\\Users\\Sensitive\\agent.json user@example.com";
    var exception = new BridgeRemoteException(
        "handler_error:C:\\Users\\Sensitive\\code",
        secret);

    Equal("remote_error", exception.Code);
    False(exception.Code.Contains("Sensitive", StringComparison.Ordinal));
    False(exception.Message.Contains("bridge-secret", StringComparison.Ordinal));
    False(exception.Message.Contains("Sensitive", StringComparison.Ordinal));
    False(exception.Message.Contains("user@example.com", StringComparison.Ordinal));
    Equal(DiagnosticDataClassification.RemoteError, exception.DiagnosticClassification);
    True(exception.DiagnosticRedactions != DiagnosticRedactionKinds.None);
    True(exception.InnerException is null);
    return Task.CompletedTask;
}

static Task BridgeClientExceptionIsSanitized()
{
    const string secret =
        "Bearer client-secret C:\\Users\\Sensitive\\client.json user@example.com";
    var source = new InvalidOperationException("token=inner-secret");
    var exception = new AgentBridgeClientException(
        "bad code:C:\\Users\\Sensitive",
        secret,
        source);

    Equal(AgentBridgeErrorCodes.InternalError, exception.Code);
    False(exception.Message.Contains("client-secret", StringComparison.Ordinal));
    False(exception.Message.Contains("Sensitive", StringComparison.Ordinal));
    False(exception.Message.Contains("user@example.com", StringComparison.Ordinal));
    Equal(DiagnosticDataClassification.Exception, exception.DiagnosticClassification);
    True(exception.DiagnosticRedactions != DiagnosticRedactionKinds.None);
    True(exception.InnerException is null);
    return Task.CompletedTask;
}

static Task BadMacIsRejected()
{
    return AssertRawEnvelopeRejected(
        (secret, sessionId) =>
        {
            var envelope = CreateEnvelope(secret, sessionId, 1, "nonce-1");
            envelope.Mac = new string('0', 64);
            return [envelope];
        },
        IpcValidationCode.InvalidMac);
}

static Task ReplayedSequenceIsRejected()
{
    return AssertRawEnvelopeRejected(
        (secret, sessionId) =>
        [
            CreateEnvelope(secret, sessionId, 1, "nonce-1"),
            CreateEnvelope(secret, sessionId, 1, "nonce-2")
        ],
        IpcValidationCode.InvalidSequence);
}

static Task ReplayedNonceIsRejected()
{
    return AssertRawEnvelopeRejected(
        (secret, sessionId) =>
        [
            CreateEnvelope(secret, sessionId, 1, "nonce-1"),
            CreateEnvelope(secret, sessionId, 2, "nonce-1")
        ],
        IpcValidationCode.ReplayedNonce);
}

static Task OutOfOrderSequenceIsRejected()
{
    return AssertRawEnvelopeRejected(
        (secret, sessionId) => [CreateEnvelope(secret, sessionId, 2, "nonce-2")],
        IpcValidationCode.InvalidSequence);
}

static async Task OversizedIncomingFrameIsRejected()
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var accept = NamedPipeBridge.AcceptOneAsync(pipeName, sessionId, secret);
    await using var rawClient = CreateRawClient(pipeName);
    await rawClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await using var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    server.Start();

    var prefix = new byte[sizeof(int)];
    BinaryPrimitives.WriteInt32LittleEndian(prefix, ProtocolConstants.MaximumMessageBytes + 1);
    await rawClient.WriteAsync(prefix);
    await rawClient.FlushAsync();

    await ThrowsAsync<BridgeProtocolException>(() => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
}

static Task UnknownEnvelopeFieldIsRejected()
{
    return AssertMalformedEnvelopeRejected(payload =>
    {
        var json = Encoding.UTF8.GetString(payload);
        return Encoding.UTF8.GetBytes(
            json.Insert(json.Length - 1, ",\"unexpected\":true"));
    });
}

static Task DuplicateEnvelopeFieldIsRejected()
{
    return AssertMalformedEnvelopeRejected(payload =>
    {
        var json = Encoding.UTF8.GetString(payload);
        return Encoding.UTF8.GetBytes(
            json.Insert(1, "\"messageId\":\"duplicate\","));
    });
}

static Task WrongCaseEnvelopeFieldIsRejected()
{
    return AssertMalformedEnvelopeRejected(payload =>
    {
        var json = Encoding.UTF8.GetString(payload);
        return Encoding.UTF8.GetBytes(
            json.Replace("\"messageId\"", "\"MessageId\"", StringComparison.Ordinal));
    });
}

static Task TrailingEnvelopeJsonIsRejected()
{
    return AssertMalformedEnvelopeRejected(payload =>
    {
        var trailing = Encoding.UTF8.GetBytes("{}");
        var mutated = new byte[payload.Length + trailing.Length];
        Buffer.BlockCopy(payload, 0, mutated, 0, payload.Length);
        Buffer.BlockCopy(trailing, 0, mutated, payload.Length, trailing.Length);
        return mutated;
    });
}

static Task InvalidEnvelopeUtf8IsRejected()
{
    return AssertMalformedEnvelopeRejected(payload =>
    {
        var mutated = new byte[payload.Length + 1];
        Buffer.BlockCopy(payload, 0, mutated, 0, payload.Length);
        mutated[mutated.Length - 1] = 0xFF;
        return mutated;
    });
}

static async Task OversizedOutgoingFrameIsRejectedWithoutSequenceGap()
{
    var notification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (_, _) =>
    {
        notification.TrySetResult();
        return ValueTask.CompletedTask;
    });
    client.Start();

    var oversizedJson = "\"" + new string('x', ProtocolConstants.MaximumMessageBytes) + "\"";
    await ThrowsAsync<BridgeProtocolException>(() => client.NotifyAsync("cad.too_large", oversizedJson));

    await client.NotifyAsync("cad.valid", "{}");
    await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task InvalidSecretLengthIsRejected()
{
    await ThrowsAsync<ArgumentException>(
        () => NamedPipeBridge.AcceptOneAsync(
            NewPipeName(),
            Guid.NewGuid().ToString("N"),
            new byte[16]));
}

static async Task ExcessiveJsonDepthIsRejected()
{
    var notification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (_, _) =>
    {
        notification.TrySetResult();
        return ValueTask.CompletedTask;
    });
    client.Start();

    var tooDeep = new string('[', 33) + "0" + new string(']', 33);
    await ThrowsAsync<ArgumentException>(() => client.NotifyAsync("cad.too_deep", tooDeep));

    await client.NotifyAsync("cad.valid", "{}");
    await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task PendingRequestLimitRejectsAndRecovers()
{
    var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var oneCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startedCount = 0;
    var clientOptions = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 2,
        MaximumActiveRequests = 2,
        MaximumConcurrentHandlers = 2
    };
    var pair = await CreatePairAsync(clientOptions: clientOptions);
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(async (request, cancellationToken) =>
    {
        if (request.Method == "cad.recovered")
        {
            return "{\"ok\":true}";
        }

        if (Interlocked.Increment(ref startedCount) == 2)
        {
            allStarted.TrySetResult();
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            oneCancelled.TrySetResult();
            throw;
        }

        return "null";
    });
    client.Start();

    using var firstCancellation = new CancellationTokenSource();
    using var secondCancellation = new CancellationTokenSource();
    var first = client.RequestAsync("cad.block.1", "{}", firstCancellation.Token);
    var second = client.RequestAsync("cad.block.2", "{}", secondCancellation.Token);
    await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

    var capacityError = await ThrowsAsync<BridgeCapacityExceededException>(
        () => client.RequestAsync("cad.over_limit", "{}"));
    Equal(BridgeCapacityKind.PendingRequests, capacityError.CapacityKind);
    Equal(2, capacityError.Limit);

    firstCancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => first);
    await oneCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

    var recovered = await client.RequestAsync("cad.recovered", "{}")
        .WaitAsync(TimeSpan.FromSeconds(5));
    Equal("{\"ok\":true}", recovered);

    secondCancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => second);
}

static async Task ActiveRequestFloodAbortsConnection()
{
    var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startedCount = 0;
    var serverOptions = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 4,
        MaximumActiveRequests = 2,
        MaximumConcurrentHandlers = 2
    };
    var clientOptions = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 4,
        MaximumActiveRequests = 2,
        MaximumConcurrentHandlers = 2
    };
    var pair = await CreatePairAsync(serverOptions, clientOptions);
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(async (_, cancellationToken) =>
    {
        if (Interlocked.Increment(ref startedCount) == 2)
        {
            allStarted.TrySetResult();
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return "null";
    });
    client.Start();

    var first = client.RequestAsync("cad.flood.1", "{}");
    var second = client.RequestAsync("cad.flood.2", "{}");
    await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var third = client.RequestAsync("cad.flood.3", "{}");

    var capacityError = await ThrowsAsync<BridgeCapacityExceededException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    Equal(BridgeCapacityKind.ActiveRequests, capacityError.CapacityKind);
    Equal(2, capacityError.Limit);
    await AssertFailsWithoutTimeoutAsync(first);
    await AssertFailsWithoutTimeoutAsync(second);
    await AssertFailsWithoutTimeoutAsync(third);
}

static async Task HandlerFloodAbortsConnection()
{
    var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startedCount = 0;
    var serverOptions = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 2,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2
    };
    var pair = await CreatePairAsync(serverOptions: serverOptions);
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: async (_, cancellationToken) =>
    {
        if (Interlocked.Increment(ref startedCount) == 2)
        {
            allStarted.TrySetResult();
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    });
    client.Start();

    await client.NotifyAsync("cad.flood.1", "{}");
    await client.NotifyAsync("cad.flood.2", "{}");
    await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await client.NotifyAsync("cad.flood.3", "{}");

    var capacityError = await ThrowsAsync<BridgeCapacityExceededException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    Equal(BridgeCapacityKind.ConcurrentHandlers, capacityError.CapacityKind);
    Equal(2, capacityError.Limit);
}

static async Task SynchronouslyBlockingHandlerDoesNotBlockReceiveLoop()
{
    using var releaseFirstHandler = new ManualResetEventSlim(false);
    var firstHandlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondHandlerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = CreateInMemoryPair();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (notification, _) =>
    {
        if (notification.Method == "cad.blocking.first")
        {
            firstHandlerStarted.TrySetResult();
            releaseFirstHandler.Wait();
        }
        else if (notification.Method == "cad.blocking.second")
        {
            secondHandlerCompleted.TrySetResult();
        }

        return ValueTask.CompletedTask;
    });
    client.Start();

    try
    {
        await client.NotifyAsync("cad.blocking.first", "{}");
        await firstHandlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.NotifyAsync("cad.blocking.second", "{}");
        await secondHandlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    finally
    {
        releaseFirstHandler.Set();
    }
}

static async Task NonCooperativeHandlerDoesNotBlockConcurrentDispose()
{
    using var releaseHandler = new ManualResetEventSlim(false);
    var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var handlerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var serverOptions = new BridgeConnectionOptions
    {
        ShutdownTimeout = TimeSpan.FromMilliseconds(100)
    };
    var pair = CreateInMemoryPair(serverOptions: serverOptions);
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (_, _) =>
    {
        handlerStarted.TrySetResult();
        try
        {
            releaseHandler.Wait();
        }
        finally
        {
            handlerExited.TrySetResult();
        }

        return ValueTask.CompletedTask;
    });
    client.Start();

    try
    {
        await client.NotifyAsync("cad.blocking.dispose", "{}");
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var elapsed = Stopwatch.StartNew();
        var firstDispose = server.DisposeAsync().AsTask();
        var concurrentDispose = server.DisposeAsync().AsTask();
        await Task.WhenAll(firstDispose, concurrentDispose).WaitAsync(TimeSpan.FromSeconds(2));
        elapsed.Stop();

        True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
        False(handlerExited.Task.IsCompleted);
    }
    finally
    {
        releaseHandler.Set();
    }

    await handlerExited.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await server.DisposeAsync();
}

static async Task LateHandlerFaultIsObservedAndDiagnosable()
{
    var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var lateHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var marker = "late-handler-secret-marker";
    var lateException = new InvalidOperationException(
        "Authorization=Bearer "
        + marker
        + " "
        + @"C:\Users\late-handler-user\fault.log");
    var unobservedLateFault = false;
    void OnUnobservedTaskException(object? _, UnobservedTaskExceptionEventArgs eventArgs)
    {
        if (eventArgs.Exception.Flatten().InnerExceptions.Any(exception =>
                ReferenceEquals(exception, lateException)))
        {
            unobservedLateFault = true;
        }
    }

    TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    try
    {
        var serverOptions = new BridgeConnectionOptions
        {
            ShutdownTimeout = TimeSpan.FromMilliseconds(100)
        };
        var pair = CreateInMemoryPair(serverOptions: serverOptions);
        await using var server = pair.Server;
        await using var client = pair.Client;
        server.Start(notificationHandler: (_, _) =>
        {
            handlerStarted.TrySetResult();
            return new ValueTask(lateHandler.Task);
        });
        client.Start();

        await client.NotifyAsync("cad.late.fault", "{}");
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        lateHandler.TrySetException(lateException);

        await WaitUntilAsync(
            () => server.TerminalError is not null,
            TimeSpan.FromSeconds(2));
        var terminal = server.TerminalError as BridgeTerminalException
            ?? throw new InvalidOperationException("Expected a safe Bridge terminal snapshot.");
        True(!ReferenceEquals(terminal, lateException));
        True(terminal.InnerException is null);
        Equal(DiagnosticDataClassification.Exception, terminal.DiagnosticClassification);
        True(
            (terminal.DiagnosticRedactions & DiagnosticRedactionKinds.Token) != 0
            && (terminal.DiagnosticRedactions & DiagnosticRedactionKinds.Path) != 0);
        True(
            (terminal.Message + " " + terminal)
                .IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0);
        await server.DisposeAsync();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        False(unobservedLateFault);
    }
    finally
    {
        lateHandler.TrySetCanceled();
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }
}

static async Task NonCooperativeStreamAndSendGateUseBoundedSafeCleanup()
{
    var stream = new NonCooperativeShutdownStream();
    var options = new BridgeConnectionOptions
    {
        ShutdownTimeout = TimeSpan.FromMilliseconds(100)
    };
    await using var connection = new AuthenticatedPipeConnection(
        stream,
        Guid.NewGuid().ToString("N"),
        IpcSessionSecret.Generate(),
        options);
    var authenticatorSecret = GetConnectionAuthenticatorSecret(connection);
    connection.Start();
    Task? pendingNotification = null;

    try
    {
        pendingNotification = connection.NotifyAsync("cad.blocking.write", "{}");
        await stream.FirstWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

        var elapsed = Stopwatch.StartNew();
        await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        elapsed.Stop();
        True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
        False(authenticatorSecret.All(static value => value == 0));

        stream.ReleaseDispose();
        await connection.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        False(authenticatorSecret.All(static value => value == 0));

        stream.ReleaseWrites();
        await pendingNotification.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            () => authenticatorSecret.All(static value => value == 0),
            TimeSpan.FromSeconds(2));
    }
    finally
    {
        stream.ReleaseDispose();
        stream.ReleaseWrites();
        if (pendingNotification is not null)
        {
            try
            {
                await pendingNotification.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
        }
    }
}

static async Task CapacitySlotsRecoverAfterCompletion()
{
    var options = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 1,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2
    };
    var pair = await CreatePairAsync(options, options);
    await using var server = pair.Server;
    await using var client = pair.Client;
    server.Start((_, _) => ValueTask.FromResult<string?>("{\"ok\":true}"));
    client.Start();

    for (var attempt = 0; attempt < 100; attempt++)
    {
        var response = await client.RequestAsync("cad.reuse", "{}")
            .WaitAsync(TimeSpan.FromSeconds(5));
        Equal("{\"ok\":true}", response);
    }
}

static async Task PendingNotificationLimitRejectsAndRecovers()
{
    var stream = new BlockingWriteStream();
    var options = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 1,
        MaximumPendingNotifications = 2,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2
    };
    await using var connection = new AuthenticatedPipeConnection(
        stream,
        Guid.NewGuid().ToString("N"),
        IpcSessionSecret.Generate(),
        options);
    connection.Start();

    var first = connection.NotifyAsync("cad.queue.1", "{}");
    await stream.FirstWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));
    var second = connection.NotifyAsync("cad.queue.2", "{}");

    var capacityError = await ThrowsAsync<BridgeCapacityExceededException>(
        () => connection.NotifyAsync("cad.queue.3", "{}"));
    Equal(BridgeCapacityKind.PendingNotifications, capacityError.CapacityKind);
    Equal(2, capacityError.Limit);

    stream.ReleaseWrites();
    await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    await connection.NotifyAsync("cad.queue.recovered", "{}").WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task ConfiguredFrameLimitRejectsAndRecovers()
{
    const int maximumFrameBytes = 1024;
    var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var options = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 2,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2,
        MaximumFrameBytes = maximumFrameBytes
    };
    var pair = await CreatePairAsync(options, options);
    await using var server = pair.Server;
    await using var client = pair.Client;
    server.Start(notificationHandler: (_, _) =>
    {
        received.TrySetResult();
        return ValueTask.CompletedTask;
    });
    client.Start();

    var oversizedJson = "\"" + new string('x', maximumFrameBytes) + "\"";
    await ThrowsAsync<BridgeProtocolException>(
        () => client.NotifyAsync("cad.frame.too_large", oversizedJson));

    await client.NotifyAsync("cad.frame.valid", "{}");
    await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task ConfiguredIncomingFrameLimitRejectsBeforeAllocation()
{
    const int maximumFrameBytes = 1024;
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var options = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 2,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2,
        MaximumFrameBytes = maximumFrameBytes
    };
    var accept = NamedPipeBridge.AcceptOneAsync(
        pipeName,
        sessionId,
        secret,
        options: options);
    await using var rawClient = CreateRawClient(pipeName);
    await rawClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await using var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    server.Start();

    var prefix = new byte[sizeof(int)];
    BinaryPrimitives.WriteInt32LittleEndian(prefix, maximumFrameBytes + 1);
    await rawClient.WriteAsync(prefix);
    await rawClient.FlushAsync();

    await ThrowsAsync<BridgeProtocolException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    await AssertPipeClosedAsync(rawClient);
}

static async Task NotificationHandlerFailureAbortsConnection()
{
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;
    var marker = "bridge-handler-secret-marker";
    var sourceFailure = new InvalidOperationException(
        "Authorization=Bearer "
        + marker
        + " "
        + @"C:\Users\bridge-user\handler.log",
        new InvalidDataException("bridge-handler-inner-marker"));
    server.Start(notificationHandler: (_, _) =>
        throw sourceFailure);
    client.Start();

    await client.NotifyAsync("cad.notification.fail", "{}");
    var exception = await ThrowsAsync<Exception>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    True(!ReferenceEquals(sourceFailure, exception));
    True(exception.InnerException is null);
    var publicDiagnostic = exception.Message + " " + server.TerminalError;
    foreach (var protectedValue in new[]
             {
                 marker,
                 "bridge-user",
                 "handler.log",
                 "bridge-handler-inner-marker",
             })
    {
        True(
            publicDiagnostic.IndexOf(protectedValue, StringComparison.OrdinalIgnoreCase) < 0);
    }

    await client.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    await ThrowsAsync<EndOfStreamException>(
        () => client.NotifyAsync("cad.after_handler_failure", "{}"));
}

static Task InvalidCapacityOptionsAreRejected()
{
    using var stream = new MemoryStream();
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new AuthenticatedPipeConnection(
            stream,
            Guid.NewGuid().ToString("N"),
            IpcSessionSecret.Generate(),
            new BridgeConnectionOptions { MaximumPendingRequests = 0 }));
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new AuthenticatedPipeConnection(
            stream,
            Guid.NewGuid().ToString("N"),
            IpcSessionSecret.Generate(),
            new BridgeConnectionOptions { MaximumPendingNotifications = 0 }));
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new AuthenticatedPipeConnection(
            stream,
            Guid.NewGuid().ToString("N"),
            IpcSessionSecret.Generate(),
            new BridgeConnectionOptions { ShutdownTimeout = TimeSpan.Zero }));
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new AuthenticatedPipeConnection(
            stream,
            Guid.NewGuid().ToString("N"),
            IpcSessionSecret.Generate(),
            new BridgeConnectionOptions
            {
                ShutdownTimeout =
                    BridgeConnectionOptions.AbsoluteMaximumShutdownTimeout + TimeSpan.FromTicks(1)
            }));
    return Task.CompletedTask;
}

static async Task NormalEofTerminatesConnection()
{
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;
    server.Start();
    client.Start();

    await client.DisposeAsync();
    await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    await ThrowsAsync<EndOfStreamException>(() => server.NotifyAsync("cad.after_eof", "{}"));
}

static async Task PartialWriteFailureAbortsConnection()
{
    var stream = new PartialWriteFailingStream();
    await using var connection = new AuthenticatedPipeConnection(
        stream,
        Guid.NewGuid().ToString("N"),
        IpcSessionSecret.Generate());
    connection.Start();

    await ThrowsAsync<IOException>(() => connection.NotifyAsync("cad.partial_write", "{}"));
    await connection.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    True(stream.IsDisposed);
    await ThrowsAsync<EndOfStreamException>(() => connection.NotifyAsync("cad.after_failure", "{}"));
}

static async Task AuthenticatedProtocolErrorAbortsConnection()
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var accept = NamedPipeBridge.AcceptOneAsync(pipeName, sessionId, secret);
    await using var rawClient = CreateRawClient(pipeName);
    await rawClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await using var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    server.Start();

    var malformed = CreateEnvelope(secret, sessionId, 1, "nonce-malformed");
    malformed.PayloadJson = "{";
    malformed.Mac = new IpcEnvelopeAuthenticator(secret).Sign(malformed);
    await LengthPrefixedFrameCodec.WriteAsync(rawClient, malformed);

    await ThrowsAsync<BridgeProtocolException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    await AssertPipeClosedAsync(rawClient);
}

static Task IpcSecretsAreZeroedOnDispose()
{
    var secret = IpcSessionSecret.Generate();
    var authenticator = new IpcEnvelopeAuthenticator(secret);
    var authenticatorSecret = GetAuthenticatorSecret(authenticator);
    False(authenticatorSecret.All(static value => value == 0));
    authenticator.Dispose();
    True(authenticatorSecret.All(static value => value == 0));
    Throws<ObjectDisposedException>(() => authenticator.Sign(new IpcEnvelope()));

    var guard = new IpcSessionGuard("session-zero-test", secret);
    var guardAuthenticatorField = typeof(IpcSessionGuard).GetField(
        "_authenticator",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("IpcSessionGuard authenticator field not found.");
    var guardAuthenticator = (IpcEnvelopeAuthenticator?)guardAuthenticatorField.GetValue(guard)
        ?? throw new InvalidOperationException("IpcSessionGuard authenticator missing.");
    var guardSecret = GetAuthenticatorSecret(guardAuthenticator);
    guard.Dispose();
    True(guardSecret.All(static value => value == 0));
    Throws<ObjectDisposedException>(() => guard.ValidateAndAccept(new IpcEnvelope()));
    return Task.CompletedTask;
}

static async Task AssertRawEnvelopeRejected(
    Func<byte[], string, IReadOnlyList<IpcEnvelope>> createEnvelopes,
    IpcValidationCode expectedCode)
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var accept = NamedPipeBridge.AcceptOneAsync(pipeName, sessionId, secret);
    await using var rawClient = CreateRawClient(pipeName);
    await rawClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await using var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    server.Start();

    foreach (var envelope in createEnvelopes(secret, sessionId))
    {
        await LengthPrefixedFrameCodec.WriteAsync(rawClient, envelope);
    }

    var exception = await ThrowsAsync<BridgeAuthenticationException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    Equal(expectedCode, exception.ValidationCode);

    await AssertPipeClosedAsync(rawClient);
}

static async Task AssertMalformedEnvelopeRejected(Func<byte[], byte[]> mutate)
{
    var secret = IpcSessionSecret.Generate();
    try
    {
        var envelope = CreateEnvelope(
            secret,
            Guid.NewGuid().ToString("N"),
            1,
            "malformed-envelope-nonce");
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload = mutate(payload);

        await using var stream = new MemoryStream();
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix);
        await stream.WriteAsync(payload);
        stream.Position = 0;

        await ThrowsAsync<BridgeProtocolException>(
            () => LengthPrefixedFrameCodec.ReadAsync(stream).AsTask());
    }
    finally
    {
        Array.Clear(secret, 0, secret.Length);
    }
}

static async Task AssertPipeClosedAsync(Stream stream)
{
    var probe = new byte[1];
    try
    {
        var read = await stream.ReadAsync(probe).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Equal(0, read);
    }
    catch (IOException)
    {
        // Windows may report a broken pipe instead of EOF; both prove the rejected connection was aborted.
    }
}

static byte[] GetAuthenticatorSecret(IpcEnvelopeAuthenticator authenticator)
{
    var field = typeof(IpcEnvelopeAuthenticator).GetField(
        "_sessionSecret",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("IpcEnvelopeAuthenticator secret field not found.");
    return (byte[]?)field.GetValue(authenticator)
        ?? throw new InvalidOperationException("IpcEnvelopeAuthenticator secret missing.");
}

static byte[] GetConnectionAuthenticatorSecret(AuthenticatedPipeConnection connection)
{
    var field = typeof(AuthenticatedPipeConnection).GetField(
        "_authenticator",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AuthenticatedPipeConnection authenticator field not found.");
    var authenticator = (IpcEnvelopeAuthenticator?)field.GetValue(connection)
        ?? throw new InvalidOperationException("AuthenticatedPipeConnection authenticator missing.");
    return GetAuthenticatorSecret(authenticator);
}

static async Task<(AuthenticatedPipeConnection Server, AuthenticatedPipeConnection Client)> CreatePairAsync(
    BridgeConnectionOptions? serverOptions = null,
    BridgeConnectionOptions? clientOptions = null)
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var accept = NamedPipeBridge.AcceptOneAsync(
        pipeName,
        sessionId,
        secret,
        options: serverOptions);
    var client = await NamedPipeBridge.ConnectAsync(
        pipeName,
        sessionId,
        secret,
        TimeSpan.FromSeconds(5),
        options: clientOptions);
    var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    return (server, client);
}

static (AuthenticatedPipeConnection Server, AuthenticatedPipeConnection Client) CreateInMemoryPair(
    BridgeConnectionOptions? serverOptions = null,
    BridgeConnectionOptions? clientOptions = null)
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var streams = InMemoryDuplexStream.CreatePair();
    return (
        new AuthenticatedPipeConnection(streams.Left, sessionId, secret, serverOptions),
        new AuthenticatedPipeConnection(streams.Right, sessionId, secret, clientOptions));
}

static NamedPipeClientStream CreateRawClient(string pipeName)
{
    return new NamedPipeClientStream(
        ".",
        pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
}

static IpcEnvelope CreateEnvelope(byte[] secret, string sessionId, long sequence, string nonce)
{
    var envelope = new IpcEnvelope
    {
        MessageId = Guid.NewGuid().ToString("N"),
        SessionId = sessionId,
        Sequence = sequence,
        MessageType = BridgeMessageTypes.Notification,
        PayloadJson = "{\"method\":\"cad.test\",\"bodyJson\":\"{}\"}",
        Nonce = nonce
    };
    envelope.Mac = new IpcEnvelopeAuthenticator(secret).Sign(envelope);
    return envelope;
}

static string NewPipeName()
{
    return "codex-autocad-spec-" + Guid.NewGuid().ToString("N");
}

static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task AssertFailsWithoutTimeoutAsync(Task task)
{
    try
    {
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch (TimeoutException)
    {
        throw;
    }
    catch (Exception)
    {
        return;
    }

    throw new InvalidOperationException("Expected connection failure.");
}

static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!predicate())
    {
        if (DateTime.UtcNow >= deadline)
        {
            throw new TimeoutException("Expected condition was not reached before timeout.");
        }

        await Task.Delay(10);
    }
}

static TException Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void False(bool condition)
{
    if (condition)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }
}

sealed class PartialWriteFailingStream : Stream
{
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _writeCount;

    public bool IsDisposed => _disposed.Task.IsCompleted;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _disposed.Task.WaitAsync(cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _writeCount) == 1)
        {
            return ValueTask.CompletedTask;
        }

        return ValueTask.FromException(new IOException("模拟部分写入失败。"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed.TrySetResult();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        _disposed.TrySetResult();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

sealed class BlockingWriteStream : Stream
{
    private readonly TaskCompletionSource _allowWrites = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task FirstWriteStarted => _firstWriteStarted.Task;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void ReleaseWrites()
    {
        _allowWrites.TrySetResult();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _disposed.Task.WaitAsync(cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        _firstWriteStarted.TrySetResult();
        await _allowWrites.Task.WaitAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed.TrySetResult();
            _allowWrites.TrySetResult();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        _disposed.TrySetResult();
        _allowWrites.TrySetResult();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

sealed class InMemoryDuplexStream : Stream
{
    private readonly ChannelReader<byte[]> _incoming;
    private readonly ChannelWriter<byte[]> _incomingCompletion;
    private readonly ChannelWriter<byte[]> _outgoing;
    private readonly CancellationTokenSource _disposedCancellation = new();
    private byte[]? _currentRead;
    private int _currentReadOffset;
    private int _disposed;

    private InMemoryDuplexStream(
        ChannelReader<byte[]> incoming,
        ChannelWriter<byte[]> incomingCompletion,
        ChannelWriter<byte[]> outgoing)
    {
        _incoming = incoming;
        _incomingCompletion = incomingCompletion;
        _outgoing = outgoing;
    }

    public static (InMemoryDuplexStream Left, InMemoryDuplexStream Right) CreatePair()
    {
        var leftToRight = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        var rightToLeft = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        return (
            new InMemoryDuplexStream(
                rightToLeft.Reader,
                rightToLeft.Writer,
                leftToRight.Writer),
            new InMemoryDuplexStream(
                leftToRight.Reader,
                leftToRight.Writer,
                rightToLeft.Writer));
    }

    public override bool CanRead => Volatile.Read(ref _disposed) == 0;

    public override bool CanSeek => false;

    public override bool CanWrite => Volatile.Read(ref _disposed) == 0;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        while (_currentRead is null || _currentReadOffset >= _currentRead.Length)
        {
            _currentRead = null;
            _currentReadOffset = 0;
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposedCancellation.Token);
            if (!await _incoming.WaitToReadAsync(linkedCancellation.Token))
            {
                return 0;
            }

            if (!_incoming.TryRead(out _currentRead))
            {
                continue;
            }
        }

        var bytesToCopy = Math.Min(buffer.Length, _currentRead.Length - _currentReadOffset);
        _currentRead.AsMemory(_currentReadOffset, bytesToCopy).CopyTo(buffer);
        _currentReadOffset += bytesToCopy;
        return bytesToCopy;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_outgoing.TryWrite(buffer.ToArray()))
        {
            return ValueTask.FromException(new IOException("内存双工流的对端已关闭。"));
        }

        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCore();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposedCancellation.Cancel();
        _outgoing.TryComplete();
        _incomingCompletion.TryComplete();
    }
}

sealed class NonCooperativeShutdownStream : Stream
{
    private readonly TaskCompletionSource _disposeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _writeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task FirstWriteStarted => _firstWriteStarted.Task;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void ReleaseDispose()
    {
        _disposeRelease.TrySetResult();
    }

    public void ReleaseWrites()
    {
        _writeRelease.TrySetResult();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _disposeStarted.Task.WaitAsync(cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        _firstWriteStarted.TrySetResult();
        await _writeRelease.Task;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposeStarted.TrySetResult();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {

        _disposeStarted.TrySetResult();
        await _disposeRelease.Task;
        GC.SuppressFinalize(this);
    }
}

sealed class NoOpAgentAppServer : IAgentAppServer
{
    public event EventHandler<AppServerNotification>? NotificationReceived
    {
        add { }
        remove { }
    }

    public event CommandApprovalRequestedHandler? CommandApprovalRequested
    {
        add { }
        remove { }
    }

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

    public event ServerRequestReceivedHandler? ServerRequestReceived
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<TResult> SendRequestAsync<TResult>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No Codex request is expected in this spec.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
