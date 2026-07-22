using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.Bridge.Client;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// Minimal read-only MVP client. It owns the authenticated AgentHost lifetime and exposes one
    /// system conversation to the Palette. CAD writes and approval resolution are intentionally not
    /// exposed here.
    /// </summary>
    internal sealed class MvpAgentClient : IDisposable
    {
        private readonly object sync = new object();
        private AgentHostServiceSession serviceSession;
        private IAgentBridgeClient bridge;
        private string threadId = string.Empty;
        private string systemSessionId = string.Empty;
        private MvpAgentTurnState activeTurn;
        private string terminalBridgeErrorCode = string.Empty;
        private Task startTask;
        private Task stopTask;
        private MvpAgentStopCoordinator stopCoordinator;
        private bool online;
        private bool stopRequested;
        private bool stopCompleted;

        internal event Action<string> StatusChanged;

        internal event Action<string> TextChanged;

        internal event Action<string> ErrorChanged;

        internal MvpAgentClient()
        {
        }

        internal MvpAgentClient(
            IAgentBridgeClient establishedBridge,
            string establishedThreadId,
            string establishedSystemSessionId)
        {
            if (establishedBridge == null)
            {
                throw new ArgumentNullException(nameof(establishedBridge));
            }

            if (string.IsNullOrWhiteSpace(establishedThreadId))
            {
                throw new ArgumentException("ThreadId 不能为空。", nameof(establishedThreadId));
            }

            if (string.IsNullOrWhiteSpace(establishedSystemSessionId))
            {
                throw new ArgumentException(
                    "SystemSessionId 不能为空。",
                    nameof(establishedSystemSessionId));
            }

            bridge = establishedBridge;
            threadId = establishedThreadId;
            systemSessionId = establishedSystemSessionId;
            startTask = Task.FromResult(0);
            online = true;
            establishedBridge.EventReceived += OnBridgeEvent;
            establishedBridge.ConnectionFaulted += OnBridgeFaulted;
        }

        internal bool IsStarted
        {
            get
            {
                lock (sync)
                {
                    return online && !stopRequested && !stopCompleted;
                }
            }
        }

        internal Task StartAsync(CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (stopRequested || stopCompleted)
                {
                    throw new InvalidOperationException(
                        "AgentHost 正在停止或等待重试清理，不能再次启动。");
                }

                if (!string.IsNullOrEmpty(terminalBridgeErrorCode))
                {
                    throw CreateUnavailableExceptionLocked();
                }

                if (startTask == null)
                {
                    startTask = Task.Run(
                        () => StartCoreAsync(cancellationToken),
                        CancellationToken.None);
                }

                return startTask;
            }
        }

        internal async Task AskAsync(
            string prompt,
            UnifiedContextState context,
            Func<bool> isCurrentContext,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("提示词不能为空。", nameof(prompt));
            }

            if (isCurrentContext == null || !isCurrentContext())
            {
                throw new InvalidOperationException("当前 CAD 上下文已失效，请重新执行 CODEX16CTX。");
            }

            await StartAsync(cancellationToken).ConfigureAwait(false);
            if (!isCurrentContext())
            {
                throw new InvalidOperationException("当前 CAD 上下文已失效，请重新执行 CODEX16CTX。");
            }

            if (context == null || !context.Published)
            {
                throw new InvalidOperationException("请先预选图元并执行 CODEX16CTX。");
            }

            if (!isCurrentContext())
            {
                throw new InvalidOperationException("当前 CAD 上下文已失效，请重新执行 CODEX16CTX。");
            }

            IAgentBridgeClient currentBridge;
            string currentThread;
            MvpAgentTurnState requestTurn;
            lock (sync)
            {
                EnsureOnlineForAskLocked();
                if (activeTurn != null && !activeTurn.IsTerminal)
                {
                    throw new MvpAgentTurnException(
                        activeTurn.RequestId,
                        activeTurn.State,
                        new AgentBridgeClientException(
                            AgentBridgeErrorCodes.Busy,
                            "已有只读 Codex 回合正在运行。"));
                }

                currentBridge = bridge;
                currentThread = threadId;
                requestTurn = new MvpAgentTurnState(
                    Guid.NewGuid().ToString("N"),
                    DateTimeOffset.UtcNow);
                activeTurn = requestTurn;
            }

            PublishSafely(TextChanged, string.Empty);
            PublishSafely(
                StatusChanged,
                FormatTurnStatus(
                    "正在向本机 Codex 发送只读问题",
                    requestTurn.RequestId,
                    requestTurn.State));
            var request = new AgentTurnStartV2Request
            {
                ThreadId = currentThread,
                ClientTurnId = requestTurn.ClientTurnId,
                Prompt = prompt,
                ContextV2 = context.Context,
                ContextV2Sha256 = context.ContextSha256,
            };
            try
            {
                if (!isCurrentContext())
                {
                    throw new InvalidOperationException(
                        "当前 CAD 上下文已失效，请重新执行 CODEX16CTX。");
                }

                var turn = await currentBridge.StartTurnV2Async(request, cancellationToken)
                    .ConfigureAwait(false);
                bool dispatchCancellation;
                string currentState;
                lock (sync)
                {
                    if (!ReferenceEquals(bridge, currentBridge) || !online)
                    {
                        throw CreateUnavailableExceptionLocked();
                    }

                    if (!ReferenceEquals(activeTurn, requestTurn))
                    {
                        throw new InvalidOperationException("当前 Agent 回合所有权已变化。");
                    }

                    if (requestTurn.IsTerminal)
                    {
                        return;
                    }

                    if (turn == null || !requestTurn.TryBindProviderTurn(turn.TurnId))
                    {
                        throw new InvalidOperationException("AgentHost 返回的回合标识无效或不一致。");
                    }

                    dispatchCancellation = requestTurn.TryBeginCancellationDispatch();
                    currentState = requestTurn.State;
                }

                PublishSafely(
                    StatusChanged,
                    FormatTurnStatus(
                        string.Equals(
                                currentState,
                                MvpAgentTurnStates.Cancelling,
                                StringComparison.Ordinal)
                            ? "取消请求已登记，正在通知 Codex"
                            : "Codex 正在分析当前图纸上下文",
                        requestTurn.RequestId,
                        currentState));
                if (dispatchCancellation)
                {
                    BeginCancellationDispatch(
                        requestTurn,
                        currentBridge,
                        currentThread,
                        requestTurn.ProviderTurnId);
                }
            }
            catch (Exception exception)
            {
                var terminalState = exception is OperationCanceledException
                    ? MvpAgentTurnStates.Cancelled
                    : MvpAgentTurnStates.Failed;
                TaskCompletionSource<bool> cancellationCompletion;
                string currentState;
                lock (sync)
                {
                    cancellationCompletion = requestTurn.MarkTerminal(terminalState);
                    currentState = requestTurn.State;
                }

                var turnException = exception as MvpAgentTurnException
                    ?? new MvpAgentTurnException(
                        requestTurn.RequestId,
                        currentState,
                        exception);
                if (cancellationCompletion != null)
                {
                    cancellationCompletion.TrySetException(turnException);
                }

                throw turnException;
            }
        }

        internal Task CancelActiveTurnAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MvpAgentTurnState requestTurn;
            IAgentBridgeClient currentBridge;
            string currentThread;
            string providerTurnId;
            Task cancellationTask;
            bool dispatchCancellation;
            bool noActiveTurn;
            lock (sync)
            {
                requestTurn = activeTurn;
                if (requestTurn == null || requestTurn.IsTerminal)
                {
                    noActiveTurn = true;
                    currentBridge = null;
                    currentThread = string.Empty;
                    providerTurnId = string.Empty;
                    cancellationTask = Task.FromResult(0);
                    dispatchCancellation = false;
                }
                else
                {
                    noActiveTurn = false;
                    currentBridge = bridge;
                    currentThread = threadId;
                    providerTurnId = requestTurn.ProviderTurnId;
                    cancellationTask = requestTurn.RequestCancellation();
                    dispatchCancellation = requestTurn.TryBeginCancellationDispatch();
                }
            }

            if (noActiveTurn)
            {
                PublishSafely(StatusChanged, "当前没有运行中的 Codex 回合可取消。");
                return cancellationTask;
            }

            PublishSafely(
                StatusChanged,
                FormatTurnStatus(
                    string.IsNullOrEmpty(providerTurnId)
                        ? "取消请求已登记，等待 AgentHost 接受回合"
                        : "正在取消 Codex 回合",
                    requestTurn.RequestId,
                    requestTurn.State));
            if (dispatchCancellation)
            {
                BeginCancellationDispatch(
                    requestTurn,
                    currentBridge,
                    currentThread,
                    providerTurnId);
            }

            return cancellationTask;
        }

        private void BeginCancellationDispatch(
            MvpAgentTurnState requestTurn,
            IAgentBridgeClient currentBridge,
            string currentThread,
            string providerTurnId)
        {
            _ = CompleteCancellationDispatchAsync(
                requestTurn,
                currentBridge,
                currentThread,
                providerTurnId);
        }

        private async Task CompleteCancellationDispatchAsync(
            MvpAgentTurnState requestTurn,
            IAgentBridgeClient currentBridge,
            string currentThread,
            string providerTurnId)
        {
            try
            {
                await currentBridge.InterruptTurnAsync(
                        new AgentTurnInterruptRequest
                        {
                            ThreadId = currentThread,
                            TurnId = providerTurnId,
                        },
                        CancellationToken.None)
                    .ConfigureAwait(false);

                TaskCompletionSource<bool> cancellationCompletion;
                lock (sync)
                {
                    cancellationCompletion = requestTurn.CancellationCompletion;
                }

                if (cancellationCompletion != null)
                {
                    cancellationCompletion.TrySetResult(true);
                }
            }
            catch (Exception exception)
            {
                TaskCompletionSource<bool> cancellationCompletion;
                string currentState;
                lock (sync)
                {
                    cancellationCompletion = ReferenceEquals(activeTurn, requestTurn)
                        ? requestTurn.ResetCancellationAfterDispatchFailure()
                        : null;
                    currentState = requestTurn.State;
                }

                var turnException = new MvpAgentTurnException(
                    requestTurn.RequestId,
                    currentState,
                    exception);
                if (cancellationCompletion != null)
                {
                    cancellationCompletion.TrySetException(turnException);
                }
            }
        }

        internal Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                stopRequested = true;
                if (stopCompleted)
                {
                    return Task.FromResult(0);
                }

                Task observedAttempt;
                if (stopTask == null)
                {
                    var completion = new TaskCompletionSource<bool>();
                    observedAttempt = completion.Task;
                    stopTask = observedAttempt;
                    _ = CompleteStopAttemptAsync(
                        completion,
                        observedAttempt);
                }
                else
                {
                    observedAttempt = stopTask;
                }

                return observedAttempt;
            }
        }

        private async Task CompleteStopAttemptAsync(
            TaskCompletionSource<bool> completion,
            Task attempt)
        {
            Exception failure = null;
            try
            {
                await Task.Run(StopCoreAsync).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            lock (sync)
            {
                if (ReferenceEquals(stopTask, attempt))
                {
                    stopTask = null;
                }
            }

            if (failure == null)
            {
                completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetException(failure);
            }
        }

        private async Task StopCoreAsync()
        {
            Task currentStart;
            lock (sync)
            {
                currentStart = startTask;
            }

            if (currentStart != null)
            {
                try
                {
                    await currentStart.ConfigureAwait(false);
                }
                catch
                {
                    // Startup reports its own failure. Stop still owns any retained partial
                    // resources and must continue cleanup.
                }
            }

            MvpAgentStopCoordinator currentCoordinator;
            lock (sync)
            {
                if (stopCoordinator == null)
                {
                    stopCoordinator = CreateStopCoordinator(bridge, serviceSession);
                }

                currentCoordinator = stopCoordinator;
            }

            PublishSafely(StatusChanged, "正在停止 AgentHost……");
            try
            {
                if (currentCoordinator != null)
                {
                    await currentCoordinator.StopAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                PublishSafely(
                    ErrorChanged,
                    MvpAgentFailureFormatter
                        .FromException(
                            exception,
                            MvpAgentFailureStages.StoppingAgentHost)
                        .FormatForUser("停止 AgentHost"));
                throw;
            }

            TaskCompletionSource<bool> turnCancellationCompletion;
            lock (sync)
            {
                if (currentCoordinator != null && !currentCoordinator.IsComplete)
                {
                    throw new InvalidOperationException("AgentHost 清理尚未完成。");
                }

                bridge = null;
                serviceSession = null;
                stopCoordinator = null;
                online = false;
                turnCancellationCompletion = activeTurn == null
                    ? null
                    : activeTurn.MarkTerminal(MvpAgentTurnStates.Cancelled);
                terminalBridgeErrorCode = string.Empty;
                stopCompleted = true;
            }

            if (turnCancellationCompletion != null)
            {
                turnCancellationCompletion.TrySetResult(true);
            }

            PublishSafely(StatusChanged, "AgentHost 已停止；CAD 写入仍禁用。");
        }

        private MvpAgentStopCoordinator CreateStopCoordinator(
            IAgentBridgeClient currentBridge,
            AgentHostServiceSession currentSession)
        {
            Func<Task> stopBridge = null;
            Action disposeBridge = null;
            if (currentBridge != null)
            {
                currentBridge.EventReceived -= OnBridgeEvent;
                currentBridge.ConnectionFaulted -= OnBridgeFaulted;
                stopBridge = () => currentBridge.StopAsync(CancellationToken.None);
                disposeBridge = () => currentBridge.Dispose();
            }

            Func<Task> stopAgentHost = null;
            if (currentSession != null)
            {
                stopAgentHost = () => currentSession.StopAsync(CancellationToken.None);
            }

            return stopBridge == null && disposeBridge == null && stopAgentHost == null
                ? null
                : new MvpAgentStopCoordinator(
                    stopBridge,
                    disposeBridge,
                    stopAgentHost);
        }

        public void Dispose()
        {
            try
            {
                StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                PublishSafely(
                    ErrorChanged,
                    MvpAgentFailureFormatter
                        .FromException(
                            exception,
                            MvpAgentFailureStages.StoppingAgentHost)
                        .FormatForUser("停止 AgentHost"));
            }
        }

        private async Task StartCoreAsync(CancellationToken cancellationToken)
        {
            AgentHostServiceSession newServiceSession = null;
            AgentBridgeClient newBridge = null;
            try
            {
                string executablePath;
                string executableSha256;
                ResolveAgentHostConfiguration(out executablePath, out executableSha256);

                PublishSafely(StatusChanged, "正在启动并验证 AgentHost……");
                newServiceSession = await AgentHostBootstrapService.StartAsync(
                        new AgentHostBootstrapOptions(executablePath, executableSha256),
                        cancellationToken)
                    .ConfigureAwait(false);
                var directionKeys = newServiceSession.ClaimDirectionKeys();
                using (directionKeys)
                {
                    newBridge = new AgentBridgeClient(
                        directionKeys,
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(30));
                }

                newBridge.EventReceived += OnBridgeEvent;
                newBridge.ConnectionFaulted += OnBridgeFaulted;
                await newBridge.StartAsync(cancellationToken).ConfigureAwait(false);
                var capabilities = await newBridge.GetCapabilitiesAsync(
                        MvpAgentProtocolIdentity.CreateCapabilitiesRequest(),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (capabilities == null || capabilities.ContractVersion != AgentBridgeContractConstants.CurrentVersion)
                {
                    throw new InvalidOperationException("AgentHost Bridge 契约版本不匹配。");
                }

                if (!MvpAgentCapabilityPolicy.SupportsCadContextV2(capabilities))
                {
                    throw new InvalidOperationException(
                        "AgentHost 不支持 CadContextJson v2 或 agent.turn.start.v2；已拒绝回退到 v1。");
                }

                var newSessionId = Guid.NewGuid().ToString("N");
                var thread = await newBridge.StartThreadAsync(
                        new AgentThreadStartRequest { ConversationId = newSessionId },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId))
                {
                    throw new InvalidOperationException("AgentHost 未返回有效 Codex thread。");
                }

                bool stopWasRequested;
                lock (sync)
                {
                    serviceSession = newServiceSession;
                    bridge = newBridge;
                    systemSessionId = newSessionId;
                    threadId = thread.ThreadId;
                    activeTurn = null;
                    terminalBridgeErrorCode = string.Empty;
                    stopWasRequested = stopRequested;
                    online = !stopWasRequested;
                    newServiceSession = null;
                    newBridge = null;
                }

                lock (sync)
                {
                    stopWasRequested = stopRequested || stopCompleted || !online;
                    if (!stopWasRequested)
                    {
                        PublishSafely(StatusChanged, "AgentHost 在线；只读 Codex 会话已建立。");
                    }
                }

                if (stopWasRequested)
                {
                    PublishSafely(StatusChanged, "启动期间已收到停止请求，正在清理 AgentHost……");
                }
            }
            catch (Exception exception)
            {
                var cleanupCoordinator = CreateStopCoordinator(
                    newBridge,
                    newServiceSession);
                Exception cleanupFailure = null;
                if (cleanupCoordinator != null)
                {
                    try
                    {
                        await cleanupCoordinator.StopAsync().ConfigureAwait(false);
                    }
                    catch (Exception observedCleanupFailure)
                    {
                        cleanupFailure = observedCleanupFailure;
                    }
                }

                if (cleanupFailure != null)
                {
                    lock (sync)
                    {
                        bridge = newBridge;
                        serviceSession = newServiceSession;
                        stopCoordinator = cleanupCoordinator;
                        stopRequested = true;
                        online = false;
                        newBridge = null;
                        newServiceSession = null;
                    }

                    PublishSafely(
                        ErrorChanged,
                        MvpAgentFailureFormatter
                            .FromException(
                                cleanupFailure,
                                MvpAgentFailureStages.StoppingAgentHost)
                            .FormatForUser("回收 AgentHost"));
                    throw new AggregateException(exception, cleanupFailure);
                }

                lock (sync)
                {
                    online = false;
                    if (!stopRequested)
                    {
                        startTask = null;
                    }
                }

                PublishSafely(
                    ErrorChanged,
                    MvpAgentFailureFormatter
                        .FromException(
                            exception,
                            MvpAgentFailureStages.StartingAgentHost)
                        .FormatForUser("启动 AgentHost"));
                throw;
            }
        }

        private static void ResolveAgentHostConfiguration(
            out string executablePath,
            out string executableSha256)
        {
            executablePath = Environment.GetEnvironmentVariable("CODEX_AGENTHOST_PATH");
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                var hostDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                executablePath = Path.Combine(
                    hostDirectory ?? string.Empty,
                    "AgentHost",
                    "Codex.AutoCAD.AgentHost.exe");
            }

            executableSha256 = Environment.GetEnvironmentVariable("CODEX_AGENTHOST_SHA256");
            if (string.IsNullOrWhiteSpace(executableSha256))
            {
                var sidecar = executablePath + ".sha256";
                if (File.Exists(sidecar))
                {
                    var content = File.ReadAllText(sidecar).Trim();
                    var separator = content.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                    executableSha256 = separator < 0 ? content : content.Substring(0, separator);
                }
            }

            if (!File.Exists(executablePath) || string.IsNullOrWhiteSpace(executableSha256))
            {
                throw new InvalidOperationException(
                    "MVP AgentHost 包不完整。需要 AgentHost/Codex.AutoCAD.AgentHost.exe 及其 .sha256 文件。");
            }
        }

        private void OnBridgeEvent(object sender, AgentBridgeEventReceivedEventArgs args)
        {
            var bridgeEvent = args == null ? null : args.BridgeEvent;
            if (bridgeEvent == null)
            {
                return;
            }

            if (string.Equals(
                    bridgeEvent.Kind,
                    AgentBridgeEventKinds.ConnectionStateChanged,
                    StringComparison.Ordinal))
            {
                if (string.Equals(
                        bridgeEvent.ConnectionState,
                        AgentBridgeConnectionStates.Offline,
                        StringComparison.Ordinal)
                    || string.Equals(
                        bridgeEvent.ConnectionState,
                        AgentBridgeConnectionStates.Closed,
                        StringComparison.Ordinal))
                {
                    TransitionOffline(
                        sender as IAgentBridgeClient,
                        new AgentBridgeClientException(
                            AgentBridgeErrorCodes.Offline,
                            "Agent Bridge 已报告离线状态。"));
                    return;
                }

                lock (sync)
                {
                    if (!ReferenceEquals(bridge, sender as IAgentBridgeClient) || !online)
                    {
                        return;
                    }
                }

                PublishSafely(StatusChanged, "Agent Bridge 状态：" + bridgeEvent.ConnectionState);
                return;
            }

            MvpAgentTurnState requestTurn;
            TaskCompletionSource<bool> cancellationCompletion = null;
            string requestId;
            string currentState;
            lock (sync)
            {
                if (!ReferenceEquals(bridge, sender as IAgentBridgeClient)
                    || activeTurn == null
                    || activeTurn.IsTerminal)
                {
                    return;
                }

                requestTurn = activeTurn;
                if (string.Equals(
                        bridgeEvent.Kind,
                        AgentBridgeEventKinds.TurnStarted,
                        StringComparison.Ordinal)
                    && string.IsNullOrEmpty(requestTurn.ProviderTurnId))
                {
                    requestTurn.TryBindProviderTurn(bridgeEvent.TurnId);
                }

                if (!requestTurn.MatchesProviderTurn(bridgeEvent.TurnId))
                {
                    return;
                }

                if (string.Equals(
                        bridgeEvent.Kind,
                        AgentBridgeEventKinds.TurnStarted,
                        StringComparison.Ordinal))
                {
                    requestTurn.MarkRunning();
                }
                else if (string.Equals(
                        bridgeEvent.Kind,
                        AgentBridgeEventKinds.TurnCompleted,
                        StringComparison.Ordinal))
                {
                    cancellationCompletion = requestTurn.MarkTerminal(
                        MvpAgentTurnStates.Completed);
                }
                else if (string.Equals(
                        bridgeEvent.Kind,
                        AgentBridgeEventKinds.TurnFailed,
                        StringComparison.Ordinal))
                {
                    cancellationCompletion = requestTurn.MarkTerminal(
                        MvpAgentTurnStates.Failed);
                }
                else if (string.Equals(
                        bridgeEvent.Kind,
                        AgentBridgeEventKinds.TurnCancelled,
                        StringComparison.Ordinal))
                {
                    cancellationCompletion = requestTurn.MarkTerminal(
                        MvpAgentTurnStates.Cancelled);
                }

                requestId = requestTurn.RequestId;
                currentState = requestTurn.State;
            }

            if (cancellationCompletion != null)
            {
                cancellationCompletion.TrySetResult(true);
            }

            if (string.Equals(
                    bridgeEvent.Kind,
                    AgentBridgeEventKinds.AssistantMessageDelta,
                    StringComparison.Ordinal))
            {
                PublishSafely(TextChanged, bridgeEvent.Delta ?? string.Empty);
            }
            else if (string.Equals(
                    bridgeEvent.Kind,
                    AgentBridgeEventKinds.AssistantMessageCompleted,
                    StringComparison.Ordinal))
            {
                PublishSafely(
                    StatusChanged,
                    FormatTurnStatus(
                        "Codex 回答文本已接收，等待回合终态",
                        requestId,
                        currentState));
            }
            else if (string.Equals(
                    bridgeEvent.Kind,
                    AgentBridgeEventKinds.TurnStarted,
                    StringComparison.Ordinal))
            {
                PublishSafely(
                    StatusChanged,
                    FormatTurnStatus(
                        string.Equals(
                                currentState,
                                MvpAgentTurnStates.Cancelling,
                                StringComparison.Ordinal)
                            ? "Codex 回合已开始，取消请求仍在处理"
                            : "Codex 正在分析当前图纸上下文",
                        requestId,
                        currentState));
            }
            else if (string.Equals(
                    bridgeEvent.Kind,
                    AgentBridgeEventKinds.TurnCompleted,
                    StringComparison.Ordinal))
            {
                PublishSafely(
                    StatusChanged,
                    FormatTurnStatus(
                        "Codex 回答完成",
                        requestId,
                        currentState));
            }
            else if (string.Equals(
                    bridgeEvent.Kind,
                    AgentBridgeEventKinds.TurnFailed,
                    StringComparison.Ordinal))
            {
                PublishSafely(
                    ErrorChanged,
                    MvpAgentFailureFormatter
                        .FromErrorCode(
                            bridgeEvent.ErrorCode,
                            MvpAgentFailureStages.RunningTurn)
                        .WithRequest(requestId, currentState)
                        .FormatForUser("Codex 回合"));
            }
            else if (string.Equals(
                    bridgeEvent.Kind,
                    AgentBridgeEventKinds.TurnCancelled,
                    StringComparison.Ordinal))
            {
                PublishSafely(
                    StatusChanged,
                    FormatTurnStatus(
                        "Codex 回合已取消",
                        requestId,
                        currentState));
            }
        }

        private void OnBridgeFaulted(object sender, AgentBridgeConnectionFaultedEventArgs args)
        {
            TransitionOffline(
                sender as IAgentBridgeClient,
                args == null ? null : args.Exception);
        }

        private void TransitionOffline(
            IAgentBridgeClient faultedBridge,
            AgentBridgeClientException exception)
        {
            var errorCode = MvpAgentFailureFormatter.NormalizeBridgeErrorCode(exception);
            MvpAgentTurnState requestTurn;
            TaskCompletionSource<bool> cancellationCompletion;
            string requestId;
            string currentState;
            lock (sync)
            {
                if (faultedBridge == null
                    || !ReferenceEquals(bridge, faultedBridge)
                    || stopRequested
                    || stopCompleted
                    || (!online && !string.IsNullOrEmpty(terminalBridgeErrorCode)))
                {
                    return;
                }

                requestTurn = activeTurn != null && !activeTurn.IsTerminal
                    ? activeTurn
                    : null;
                cancellationCompletion = requestTurn == null
                    ? null
                    : requestTurn.MarkTerminal(MvpAgentTurnStates.Failed);
                requestId = requestTurn == null ? string.Empty : requestTurn.RequestId;
                currentState = requestTurn == null ? string.Empty : requestTurn.State;
                terminalBridgeErrorCode = errorCode;
                online = false;
            }

            if (cancellationCompletion != null)
            {
                cancellationCompletion.TrySetException(
                    new MvpAgentTurnException(
                        requestId,
                        currentState,
                        exception ?? new AgentBridgeClientException(
                            errorCode,
                            "Agent Bridge 已断开。")));
            }

            PublishSafely(
                ErrorChanged,
                "Agent Bridge 已断开（error_code="
                + errorCode
                + "）；"
                + (requestTurn == null
                    ? string.Empty
                    : "当前回合已终止（request_id="
                        + requestId
                        + ", state="
                        + currentState
                        + "）；")
                + "后续问题已拒绝。请先停止并重新启动 AgentHost。");
        }

        private void EnsureOnlineForAskLocked()
        {
            if (!online || bridge == null || string.IsNullOrWhiteSpace(threadId))
            {
                throw CreateUnavailableExceptionLocked();
            }
        }

        private AgentBridgeClientException CreateUnavailableExceptionLocked()
        {
            var errorCode = string.IsNullOrEmpty(terminalBridgeErrorCode)
                ? AgentBridgeErrorCodes.Offline
                : terminalBridgeErrorCode;
            return new AgentBridgeClientException(
                errorCode,
                "Agent Bridge 当前离线（error_code="
                + errorCode
                + "）；请先停止并重新启动 AgentHost。");
        }

        private static string FormatTurnStatus(
            string message,
            string requestId,
            string turnState)
        {
            return (message ?? "Codex 回合状态已更新")
                + "（request_id="
                + (requestId ?? string.Empty)
                + ", state="
                + (turnState ?? string.Empty)
                + "）。";
        }

        private static void PublishSafely(Action<string> subscribers, string value)
        {
            if (subscribers == null)
            {
                return;
            }

            foreach (Action<string> subscriber in subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(value);
                }
                catch
                {
                    // Palette/dispatcher observers must never acquire resource-lifecycle
                    // ownership or prevent AgentHost cleanup.
                }
            }
        }
    }
}
