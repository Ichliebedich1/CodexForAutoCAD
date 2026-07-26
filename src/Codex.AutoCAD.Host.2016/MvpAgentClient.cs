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
        private static readonly TimeSpan DefaultTurnTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan AgentHostResourceAttributionWindow =
            TimeSpan.FromSeconds(1);
        private readonly object sync = new object();
        private readonly TimeSpan turnTimeout;
        private readonly Func<CancellationToken, Task> startupCheckpoint;
        private AgentHostServiceSession serviceSession;
        private IAgentBridgeClient bridge;
        private string threadId = string.Empty;
        private string systemSessionId = string.Empty;
        private string conversationDocumentId = string.Empty;
        private MvpAgentTurnState activeTurn;
        private DrawingQueryTurnBinding activeDrawingQueryBinding;
        private string terminalBridgeErrorCode = string.Empty;
        private Task startTask;
        private CancellationTokenSource startCancellation;
        private Task stopTask;
        private MvpAgentStopCoordinator stopCoordinator;
        private bool online;
        private bool conversationTransition;
        private bool conversationResetRequired;
        private long conversationEpoch;
        private bool stopRequested;
        private bool stopCompleted;

        internal event Action<string> StatusChanged;

        internal event Action<string> TextChanged;

        internal event Action<string> ErrorChanged;

        internal MvpAgentClient()
        {
            turnTimeout = DefaultTurnTimeout;
        }

        internal MvpAgentClient(Func<CancellationToken, Task> startupCheckpoint)
        {
            if (startupCheckpoint == null)
            {
                throw new ArgumentNullException(nameof(startupCheckpoint));
            }

            this.startupCheckpoint = startupCheckpoint;
            turnTimeout = DefaultTurnTimeout;
        }

        internal MvpAgentClient(
            IAgentBridgeClient establishedBridge,
            string establishedThreadId,
            string establishedSystemSessionId,
            TimeSpan? configuredTurnTimeout = null,
            AgentHostServiceSession establishedServiceSession = null)
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
            serviceSession = establishedServiceSession;
            threadId = establishedThreadId;
            systemSessionId = establishedSystemSessionId;
            turnTimeout = configuredTurnTimeout ?? DefaultTurnTimeout;
            if (turnTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(configuredTurnTimeout));
            }

            startTask = Task.FromResult(0);
            online = true;
            establishedBridge.EventReceived += OnBridgeEvent;
            establishedBridge.ConnectionFaulted += OnBridgeFaulted;
            if (establishedServiceSession != null)
            {
                _ = MonitorAgentHostResourceLimitAsync(
                    establishedServiceSession,
                    establishedBridge);
                _ = MonitorAgentHostProcessExitAsync(
                    establishedServiceSession,
                    establishedBridge);
            }
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
                    var newStartCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    startCancellation = newStartCancellation;
                    startTask = Task.Run(
                        () => StartCoreAsync(newStartCancellation),
                        CancellationToken.None);
                }

                return startTask;
            }
        }

        internal Task AskAsync(
            string prompt,
            UnifiedContextState context,
            Func<bool> isCurrentContext,
            CancellationToken cancellationToken)
        {
            return AskAsync(
                prompt,
                context,
                isCurrentContext,
                null,
                cancellationToken);
        }

        internal async Task AskAsync(
            string prompt,
            UnifiedContextState context,
            Func<bool> isCurrentContext,
            DrawingIndexAgentSnapshot drawingIndexSnapshot,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("提示词不能为空。", nameof(prompt));
            }

            var hasSelectionContext = context != null && context.Published;
            if (hasSelectionContext && (isCurrentContext == null || !isCurrentContext()))
            {
                throw new InvalidOperationException("当前 CAD 上下文已失效，请重新执行 CODEX16CTX。");
            }
            if (drawingIndexSnapshot != null && !drawingIndexSnapshot.IsCurrent)
            {
                drawingIndexSnapshot = null;
            }
            if (!hasSelectionContext && drawingIndexSnapshot == null)
            {
                throw new InvalidOperationException(
                    "请先执行 CODEX16INDEX 建立整图索引，或预选图元并执行 CODEX16CTX。");
            }

            await StartAsync(cancellationToken).ConfigureAwait(false);
            if (hasSelectionContext && !isCurrentContext())
            {
                throw new InvalidOperationException("当前 CAD 上下文已失效，请重新执行 CODEX16CTX。");
            }
            if (drawingIndexSnapshot != null && !drawingIndexSnapshot.IsCurrent)
            {
                drawingIndexSnapshot = null;
            }
            if (!hasSelectionContext && drawingIndexSnapshot == null)
            {
                throw new InvalidOperationException(
                    "DrawingIndex 已失效，请重新执行 CODEX16INDEX。");
            }

            var selectionDocumentId = !hasSelectionContext
                                      || context.Context == null
                                      || context.Context.Document == null
                ? string.Empty
                : context.Context.Document.DocumentId;
            var indexDocumentId = drawingIndexSnapshot == null
                ? string.Empty
                : drawingIndexSnapshot.DocumentId;
            if (!string.IsNullOrWhiteSpace(selectionDocumentId)
                && !string.IsNullOrWhiteSpace(indexDocumentId)
                && !string.Equals(
                    selectionDocumentId,
                    indexDocumentId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "选择上下文与 DrawingIndex 不属于同一图纸；已拒绝混用。");
            }
            var documentId = !string.IsNullOrWhiteSpace(selectionDocumentId)
                ? selectionDocumentId
                : indexDocumentId;
            if (!string.IsNullOrWhiteSpace(documentId))
            {
                await EnsureConversationForDocumentAsync(documentId, cancellationToken)
                    .ConfigureAwait(false);
                if (hasSelectionContext && !isCurrentContext())
                {
                    throw new InvalidOperationException(
                        "当前 CAD 上下文已失效，请重新执行 CODEX16CTX。");
                }
                if (!hasSelectionContext
                    && (drawingIndexSnapshot == null || !drawingIndexSnapshot.IsCurrent))
                {
                    throw new InvalidOperationException(
                        "DrawingIndex 已失效，请重新执行 CODEX16INDEX。");
                }
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
                    DateTimeOffset.UtcNow,
                    turnTimeout);
                activeTurn = requestTurn;
                activeDrawingQueryBinding = drawingIndexSnapshot == null
                    ? null
                    : new DrawingQueryTurnBinding(
                        requestTurn.RequestId,
                        currentThread,
                        drawingIndexSnapshot);
            }

            BeginTurnTimeoutMonitor(requestTurn);
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
                ContextV2 = hasSelectionContext ? context.Context : null,
                ContextV2Sha256 = hasSelectionContext ? context.ContextSha256 : string.Empty,
            };
            try
            {
                if (hasSelectionContext && !isCurrentContext())
                {
                    throw new InvalidOperationException(
                        "当前 CAD 上下文已失效，请重新执行 CODEX16CTX。");
                }
                if (!hasSelectionContext
                    && (drawingIndexSnapshot == null || !drawingIndexSnapshot.IsCurrent))
                {
                    throw new InvalidOperationException(
                        "DrawingIndex 已失效，请重新执行 CODEX16INDEX。");
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
                    if (activeDrawingQueryBinding != null
                        && !activeDrawingQueryBinding.TryBindProviderTurn(turn.TurnId))
                    {
                        throw new InvalidOperationException(
                            "AgentHost 返回的整图查询回合身份不一致。");
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
                            : "Codex 正在分析当前图纸数据",
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
                    if (activeDrawingQueryBinding != null
                        && string.Equals(
                            activeDrawingQueryBinding.RequestId,
                            requestTurn.RequestId,
                            StringComparison.Ordinal))
                    {
                        activeDrawingQueryBinding = null;
                    }
                }

                requestTurn.CancelTimeout();

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

        internal async Task NewConversationAsync(
            string documentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IAgentBridgeClient currentBridge;
            string newSystemSessionId;
            long transitionEpoch;
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

                if (conversationTransition)
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.Busy,
                        "Codex 对话正在切换。");
                }

                currentBridge = bridge;
                newSystemSessionId = Guid.NewGuid().ToString("N");
                conversationTransition = true;
                transitionEpoch = conversationEpoch;
            }

            try
            {
                var thread = await currentBridge.StartThreadAsync(
                        new AgentThreadStartRequest
                        {
                            ConversationId = newSystemSessionId,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId))
                {
                    throw new InvalidOperationException(
                        "AgentHost 未返回有效 Codex thread。");
                }

                lock (sync)
                {
                    if (!ReferenceEquals(bridge, currentBridge) || !online)
                    {
                        throw CreateUnavailableExceptionLocked();
                    }

                    if (conversationEpoch != transitionEpoch)
                    {
                        throw new AgentBridgeClientException(
                            AgentBridgeErrorCodes.ContextInvalid,
                            "新对话建立期间当前图纸已变化。");
                    }

                    systemSessionId = newSystemSessionId;
                    threadId = thread.ThreadId;
                    conversationDocumentId = documentId ?? string.Empty;
                    conversationResetRequired = false;
                    conversationEpoch++;
                    activeTurn = null;
                    activeDrawingQueryBinding = null;
                    conversationTransition = false;
                }

                PublishSafely(TextChanged, string.Empty);
                PublishSafely(
                    StatusChanged,
                    "新的只读 Codex 对话已建立；CAD 上下文保持不变。");
            }
            catch
            {
                lock (sync)
                {
                    conversationTransition = false;
                }

                throw;
            }
        }

        private async Task EnsureConversationForDocumentAsync(
            string documentId,
            CancellationToken cancellationToken)
        {
            bool createFreshConversation;
            lock (sync)
            {
                EnsureOnlineForAskLocked();
                if (conversationTransition)
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.Busy,
                        "Codex 对话正在切换。");
                }

                if (!conversationResetRequired
                    && string.IsNullOrEmpty(conversationDocumentId))
                {
                    conversationDocumentId = documentId;
                    return;
                }

                createFreshConversation = conversationResetRequired
                    || !string.Equals(
                        conversationDocumentId,
                        documentId,
                        StringComparison.Ordinal);
            }

            if (createFreshConversation)
            {
                await NewConversationAsync(documentId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        internal void InvalidateConversationForDocumentChange()
        {
            MvpAgentTurnState requestTurn;
            IAgentBridgeClient currentBridge;
            string currentThread;
            string providerTurnId;
            bool dispatchInterrupt;
            TaskCompletionSource<bool> cancellationCompletion;
            lock (sync)
            {
                conversationEpoch++;
                conversationDocumentId = string.Empty;
                conversationResetRequired = true;
                requestTurn = activeTurn != null && !activeTurn.IsTerminal
                    ? activeTurn
                    : null;
                currentBridge = bridge;
                currentThread = threadId;
                providerTurnId = requestTurn == null
                    ? string.Empty
                    : requestTurn.ProviderTurnId;
                dispatchInterrupt = requestTurn != null
                    && requestTurn.TryBeginForcedInterrupt();
                cancellationCompletion = requestTurn == null
                    ? null
                    : requestTurn.MarkTerminal(MvpAgentTurnStates.Failed);
                activeDrawingQueryBinding = null;
            }

            PublishSafely(TextChanged, string.Empty);

            if (requestTurn == null)
            {
                PublishSafely(
                    StatusChanged,
                    "图纸已切换；旧 Codex 对话已隔离，下一次提问将建立新对话。");
                return;
            }

            requestTurn.CancelTimeout();
            var failure = new MvpAgentTurnException(
                requestTurn.RequestId,
                requestTurn.State,
                new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ContextInvalid,
                    "当前图纸已切换。"));
            if (cancellationCompletion != null)
            {
                cancellationCompletion.TrySetException(failure);
            }

            PublishSafely(
                ErrorChanged,
                MvpAgentFailureFormatter
                    .FromErrorCode(
                        AgentBridgeErrorCodes.ContextInvalid,
                        MvpAgentFailureStages.RunningTurn)
                    .WithRequest(requestTurn.RequestId, requestTurn.State)
                    .FormatForUser("图纸切换"));
            if (dispatchInterrupt && currentBridge != null)
            {
                _ = InterruptTurnBestEffortAsync(
                    currentBridge,
                    currentThread,
                    providerTurnId);
            }
        }

        internal void ClearConversation()
        {
            lock (sync)
            {
                if (activeTurn != null && !activeTurn.IsTerminal)
                {
                    throw new MvpAgentTurnException(
                        activeTurn.RequestId,
                        activeTurn.State,
                        new AgentBridgeClientException(
                            AgentBridgeErrorCodes.Busy,
                            "已有只读 Codex 回合正在运行。"));
                }

                if (conversationTransition)
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.Busy,
                        "Codex 对话正在切换。");
                }

                conversationEpoch++;
                conversationDocumentId = string.Empty;
                conversationResetRequired = true;
                activeTurn = null;
                activeDrawingQueryBinding = null;
            }

            PublishSafely(TextChanged, string.Empty);
            PublishSafely(
                StatusChanged,
                "当前 Codex 对话已清除；下一次提问将建立新对话。");
        }

        private static async Task InterruptTurnBestEffortAsync(
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
            }
            catch
            {
                // The old drawing is already isolated locally. Provider interruption is best effort.
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

        private void BeginTurnTimeoutMonitor(MvpAgentTurnState requestTurn)
        {
            _ = MonitorTurnTimeoutAsync(requestTurn);
        }

        private async Task MonitorTurnTimeoutAsync(MvpAgentTurnState requestTurn)
        {
            try
            {
                await Task.Delay(turnTimeout, requestTurn.TimeoutToken).ConfigureAwait(false);
                await HandleTurnTimeoutAsync(requestTurn).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A normal terminal event or AgentHost shutdown ended the timeout monitor.
            }
            finally
            {
                requestTurn.DisposeTimeout();
            }
        }

        private async Task HandleTurnTimeoutAsync(MvpAgentTurnState requestTurn)
        {
            IAgentBridgeClient currentBridge;
            string currentThread;
            string providerTurnId;
            bool dispatchInterrupt;
            TaskCompletionSource<bool> cancellationCompletion;
            lock (sync)
            {
                if (!ReferenceEquals(activeTurn, requestTurn)
                    || requestTurn.IsTerminal
                    || !online)
                {
                    return;
                }

                currentBridge = bridge;
                currentThread = threadId;
                providerTurnId = requestTurn.ProviderTurnId;
                dispatchInterrupt = requestTurn.TryBeginForcedInterrupt();
                cancellationCompletion = requestTurn.MarkTerminal(
                    MvpAgentTurnStates.Failed);
                activeDrawingQueryBinding = null;
                terminalBridgeErrorCode = AgentBridgeErrorCodes.Timeout;
                online = false;
            }

            var timeoutException = new MvpAgentTurnException(
                requestTurn.RequestId,
                requestTurn.State,
                new AgentBridgeClientException(
                    AgentBridgeErrorCodes.Timeout,
                    "Host 只读回合已超时。"));
            if (cancellationCompletion != null)
            {
                cancellationCompletion.TrySetException(timeoutException);
            }

            PublishSafely(
                ErrorChanged,
                MvpAgentFailureFormatter
                    .FromErrorCode(
                        AgentBridgeErrorCodes.Timeout,
                        MvpAgentFailureStages.RunningTurn)
                    .WithRequest(requestTurn.RequestId, requestTurn.State)
                    .FormatForUser("Codex 回合"));

            if (!dispatchInterrupt || currentBridge == null)
            {
                return;
            }

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
            }
            catch
            {
                // Timeout is already terminal and fail-closed. Best-effort interrupt failure is
                // observed here and remaining process cleanup stays owned by CODEX16AGENTSTOP.
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
            CancellationTokenSource currentStartCancellation;
            lock (sync)
            {
                currentStart = startTask;
                currentStartCancellation = startCancellation;
            }

            RequestStartupCancellation(currentStartCancellation);
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

            MvpAgentTurnState stoppedTurn;
            TaskCompletionSource<bool> turnCancellationCompletion;
            CancellationTokenSource completedStartCancellation;
            lock (sync)
            {
                if (currentCoordinator != null && !currentCoordinator.IsComplete)
                {
                    throw new InvalidOperationException("AgentHost 清理尚未完成。");
                }

                bridge = null;
                serviceSession = null;
                completedStartCancellation = startCancellation;
                startCancellation = null;
                stopCoordinator = null;
                online = false;
                stoppedTurn = activeTurn;
                turnCancellationCompletion = stoppedTurn == null
                    ? null
                    : stoppedTurn.MarkTerminal(MvpAgentTurnStates.Cancelled);
                activeDrawingQueryBinding = null;
                terminalBridgeErrorCode = string.Empty;
                stopCompleted = true;
            }

            if (completedStartCancellation != null)
            {
                completedStartCancellation.Dispose();
            }

            if (stoppedTurn != null)
            {
                stoppedTurn.CancelTimeout();
            }

            if (turnCancellationCompletion != null)
            {
                turnCancellationCompletion.TrySetResult(true);
            }

            PublishSafely(StatusChanged, "AgentHost 已停止；CAD 写入仍禁用。");
        }

        private static void RequestStartupCancellation(
            CancellationTokenSource startupCancellation)
        {
            if (startupCancellation == null || startupCancellation.IsCancellationRequested)
            {
                return;
            }

            try
            {
                startupCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A concurrently completed startup may already have released the source. STOP still
                // owns the established Bridge and AgentHost resources through the normal coordinator.
            }
            catch (AggregateException)
            {
                // Cancellation callbacks cannot be allowed to skip process cleanup. The source is
                // already cancelled, so StopCore continues by awaiting startup and reclaiming every
                // resource that reached the Host boundary.
            }
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

        /// <summary>
        /// Dedicated reverse Bridge handler. This method is intentionally limited to the pure-managed
        /// snapshot bound to the active turn and never enters an Autodesk API.
        /// </summary>
        internal Task<AgentDrawingQueryResponse> HandleDrawingQueryAsync(
            AgentDrawingQueryRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var failures = AgentBridgeContractValidator.Validate(request);
            if (failures.Length != 0)
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.RequestInvalid,
                    "整图查询请求未通过冻结契约。");
            }

            MvpAgentTurnState requestTurn;
            DrawingQueryTurnBinding binding;
            DrawingIndexAgentSnapshot snapshot;
            lock (sync)
            {
                EnsureOnlineForAskLocked();
                requestTurn = activeTurn;
                if (requestTurn == null
                    || requestTurn.IsTerminal
                    || !string.Equals(
                        requestTurn.RequestId,
                        request.RequestId,
                        StringComparison.Ordinal)
                    || !string.Equals(threadId, request.ThreadId, StringComparison.Ordinal))
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.ResultIdentityMismatch,
                        "整图查询未绑定到当前活动回合。");
                }

                binding = activeDrawingQueryBinding;
                if (binding == null)
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.DrawingQueryUnavailable,
                        "当前回合没有可查询的 DrawingIndex 快照。");
                }
                if (!requestTurn.TryBindProviderTurn(request.TurnId)
                    || !binding.TryBindProviderTurn(request.TurnId)
                    || !binding.Matches(request))
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.ResultIdentityMismatch,
                        "整图查询身份与当前快照绑定不一致。");
                }

                snapshot = binding.Snapshot;
                if (!snapshot.IsCurrent)
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.DrawingQueryUnavailable,
                        "DrawingIndex 已失效，请重新建立索引。");
                }
            }

            CadQueryResponse queryResponse;
            try
            {
                queryResponse = snapshot.Query(request, cancellationToken);
            }
            catch (DrawingIndexQueryException exception)
            {
                var code = string.Equals(
                               exception.Code,
                               "drawing_index_stale",
                               StringComparison.Ordinal)
                           || string.Equals(
                               exception.Code,
                               "drawing_index_unavailable",
                               StringComparison.Ordinal)
                    ? AgentBridgeErrorCodes.DrawingQueryUnavailable
                    : AgentBridgeErrorCodes.RequestInvalid;
                throw new AgentBridgeClientException(
                    code,
                    code == AgentBridgeErrorCodes.DrawingQueryUnavailable
                        ? "DrawingIndex 已失效或不可查询。"
                        : "整图查询参数或游标无效。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                if (!ReferenceEquals(activeTurn, requestTurn)
                    || requestTurn.IsTerminal
                    || !ReferenceEquals(activeDrawingQueryBinding, binding)
                    || !binding.Matches(request)
                    || !snapshot.IsCurrent)
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.ResultIdentityMismatch,
                        "整图查询完成前回合或图纸身份已变化；结果已拒绝。");
                }
            }

            return Task.FromResult(new AgentDrawingQueryResponse
            {
                RequestId = request.RequestId,
                ThreadId = request.ThreadId,
                TurnId = request.TurnId,
                ToolCallId = request.ToolCallId,
                QueryId = request.QueryId,
                Query = queryResponse,
            });
        }

        private async Task StartCoreAsync(
            CancellationTokenSource startupCancellationSource)
        {
            var cancellationToken = startupCancellationSource.Token;
            AgentHostServiceSession newServiceSession = null;
            AgentBridgeClient newBridge = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (startupCheckpoint != null)
                {
                    await startupCheckpoint(cancellationToken).ConfigureAwait(false);
                }

                string executablePath;
                string executableSha256;
                ResolveAgentHostConfiguration(out executablePath, out executableSha256);

                PublishSafely(StatusChanged, "正在启动并验证 AgentHost……");
                var bootstrapOptions =
                    new AgentHostBootstrapOptions(executablePath, executableSha256);
                // 缺少配置文件时返回默认的禁用配置，因此这一行不改变现有生产行为；
                // 配置存在但非法时在这里就抛出，不会带着半个配置去启动 AgentHost。
                bootstrapOptions.Credential = MvpAgentCredentialConfig.Load();
                newServiceSession = await AgentHostBootstrapService.StartAsync(
                        bootstrapOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                var directionKeys = newServiceSession.ClaimDirectionKeys();
                using (directionKeys)
                {
                    newBridge = new AgentBridgeClient(
                        directionKeys,
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(30),
                        drawingQueryHandler: HandleDrawingQueryAsync);
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
                if (!MvpAgentCapabilityPolicy.SupportsDrawingQuery(capabilities))
                {
                    throw new InvalidOperationException(
                        "AgentHost 不支持 cad.drawing.query；已拒绝启动整图查询链路。");
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

                var monitoredServiceSession = newServiceSession;
                var monitoredBridge = newBridge;
                bool stopWasRequested;
                lock (sync)
                {
                    serviceSession = newServiceSession;
                    bridge = newBridge;
                    systemSessionId = newSessionId;
                    threadId = thread.ThreadId;
                    activeTurn = null;
                    activeDrawingQueryBinding = null;
                    terminalBridgeErrorCode = string.Empty;
                    stopWasRequested = stopRequested;
                    online = !stopWasRequested;
                    newServiceSession = null;
                    newBridge = null;
                }

                _ = MonitorAgentHostResourceLimitAsync(
                    monitoredServiceSession,
                    monitoredBridge);
                _ = MonitorAgentHostProcessExitAsync(
                    monitoredServiceSession,
                    monitoredBridge);

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

                CancellationTokenSource completedStartCancellation = null;
                bool suppressStartupFailure;
                lock (sync)
                {
                    online = false;
                    suppressStartupFailure =
                        stopRequested
                        && cancellationToken.IsCancellationRequested
                        && exception is OperationCanceledException;
                    if (!stopRequested)
                    {
                        startTask = null;
                        if (ReferenceEquals(
                            startCancellation,
                            startupCancellationSource))
                        {
                            completedStartCancellation = startCancellation;
                            startCancellation = null;
                        }
                    }
                }

                if (completedStartCancellation != null)
                {
                    completedStartCancellation.Dispose();
                }

                if (!suppressStartupFailure)
                {
                    PublishSafely(
                        ErrorChanged,
                        MvpAgentFailureFormatter
                            .FromException(
                                exception,
                                MvpAgentFailureStages.StartingAgentHost)
                            .FormatForUser("启动 AgentHost"));
                }
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

        private async Task MonitorAgentHostResourceLimitAsync(
            AgentHostServiceSession monitoredServiceSession,
            IAgentBridgeClient monitoredBridge)
        {
            try
            {
                var failure = await monitoredServiceSession.ResourceLimitFailureTask
                    .ConfigureAwait(false);
                if (failure == AgentHostResourceLimitFailure.None)
                {
                    return;
                }

                var exception = new AgentHostResourceLimitException(failure);
                TransitionOfflineForAgentHostFailure(
                    monitoredServiceSession,
                    monitoredBridge,
                    exception,
                    MvpAgentFailureFormatter.FromResourceLimitFailure(
                        failure,
                        MvpAgentFailureStages.AgentHostRuntime));
            }
            catch (Exception exception)
            {
                TransitionOfflineForAgentHostFailure(
                    monitoredServiceSession,
                    monitoredBridge,
                    exception,
                    MvpAgentFailureFormatter.FromException(
                        exception,
                        MvpAgentFailureStages.AgentHostRuntime));
            }
        }

        private async Task MonitorAgentHostProcessExitAsync(
            AgentHostServiceSession monitoredServiceSession,
            IAgentBridgeClient monitoredBridge)
        {
            try
            {
                var failure = await monitoredServiceSession.ProcessExitFailureTask
                    .ConfigureAwait(false);
                if (failure == AgentHostProcessExitFailure.None)
                {
                    return;
                }

                var exception = new AgentHostProcessExitException(failure);
                TransitionOfflineForAgentHostFailure(
                    monitoredServiceSession,
                    monitoredBridge,
                    exception,
                    MvpAgentFailureFormatter.FromProcessExitFailure(
                        failure,
                        MvpAgentFailureStages.AgentHostRuntime));
            }
            catch (Exception exception)
            {
                TransitionOfflineForAgentHostFailure(
                    monitoredServiceSession,
                    monitoredBridge,
                    exception,
                    MvpAgentFailureFormatter.FromException(
                        exception,
                        MvpAgentFailureStages.AgentHostRuntime));
            }
        }

        private void TransitionOfflineForAgentHostFailure(
            AgentHostServiceSession monitoredServiceSession,
            IAgentBridgeClient monitoredBridge,
            Exception exception,
            MvpAgentFailure failure)
        {
            MvpAgentTurnState requestTurn;
            TaskCompletionSource<bool> cancellationCompletion;
            string requestId;
            string currentState;
            lock (sync)
            {
                if (!ReferenceEquals(serviceSession, monitoredServiceSession)
                    || !ReferenceEquals(bridge, monitoredBridge)
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
                activeDrawingQueryBinding = null;
                terminalBridgeErrorCode = failure.ErrorCode;
                online = false;
            }

            if (requestTurn != null)
            {
                requestTurn.CancelTimeout();
            }

            if (cancellationCompletion != null)
            {
                cancellationCompletion.TrySetException(
                    new MvpAgentTurnException(
                        requestId,
                        currentState,
                        exception));
            }

            PublishSafely(
                ErrorChanged,
                failure
                    .WithRequest(requestId, currentState)
                    .FormatForUser("AgentHost 运行"));
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
                    QueueBridgeFaultAttribution(
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
            bool becameTerminal = false;
            string requestId;
            string currentState;
            lock (sync)
            {
                if (!ReferenceEquals(bridge, sender as IAgentBridgeClient)
                    || activeTurn == null
                    || activeTurn.IsTerminal
                    || (!string.IsNullOrEmpty(bridgeEvent.ThreadId)
                        && !string.Equals(
                            threadId,
                            bridgeEvent.ThreadId,
                            StringComparison.Ordinal)))
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
                    becameTerminal = true;
                }
                else if (string.Equals(
                        bridgeEvent.Kind,
                        AgentBridgeEventKinds.TurnFailed,
                        StringComparison.Ordinal))
                {
                    cancellationCompletion = requestTurn.MarkTerminal(
                        MvpAgentTurnStates.Failed);
                    becameTerminal = true;
                }
                else if (string.Equals(
                        bridgeEvent.Kind,
                        AgentBridgeEventKinds.TurnCancelled,
                        StringComparison.Ordinal))
                {
                    cancellationCompletion = requestTurn.MarkTerminal(
                        MvpAgentTurnStates.Cancelled);
                    becameTerminal = true;
                }

                if (becameTerminal)
                {
                    activeDrawingQueryBinding = null;
                }

                requestId = requestTurn.RequestId;
                currentState = requestTurn.State;
            }

            if (becameTerminal)
            {
                requestTurn.CancelTimeout();
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
            QueueBridgeFaultAttribution(
                sender as IAgentBridgeClient,
                args == null ? null : args.Exception);
        }

        private void QueueBridgeFaultAttribution(
            IAgentBridgeClient faultedBridge,
            AgentBridgeClientException exception)
        {
            AgentHostServiceSession currentServiceSession;
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

                currentServiceSession = serviceSession;
            }

            if (currentServiceSession == null)
            {
                TransitionOffline(faultedBridge, exception);
                return;
            }

            _ = AttributeBridgeFaultAsync(
                currentServiceSession,
                faultedBridge,
                exception);
        }

        private async Task AttributeBridgeFaultAsync(
            AgentHostServiceSession monitoredServiceSession,
            IAgentBridgeClient faultedBridge,
            AgentBridgeClientException exception)
        {
            var resourceFailureTask = monitoredServiceSession.ResourceLimitFailureTask;
            var processExitFailureTask = monitoredServiceSession.ProcessExitFailureTask;
            if (!resourceFailureTask.IsCompleted && !processExitFailureTask.IsCompleted)
            {
                await Task.WhenAny(
                        resourceFailureTask,
                        processExitFailureTask,
                        Task.Delay(AgentHostResourceAttributionWindow))
                    .ConfigureAwait(false);
            }

            try
            {
                if (resourceFailureTask.IsCompleted)
                {
                    var failure = await resourceFailureTask.ConfigureAwait(false);
                    if (failure != AgentHostResourceLimitFailure.None)
                    {
                        var resourceException = new AgentHostResourceLimitException(failure);
                        TransitionOfflineForAgentHostFailure(
                            monitoredServiceSession,
                            faultedBridge,
                            resourceException,
                            MvpAgentFailureFormatter.FromResourceLimitFailure(
                                failure,
                                MvpAgentFailureStages.AgentHostRuntime));
                        return;
                    }
                }
            }
            catch (Exception resourceMonitorException)
            {
                TransitionOfflineForAgentHostFailure(
                    monitoredServiceSession,
                    faultedBridge,
                    resourceMonitorException,
                    MvpAgentFailureFormatter.FromException(
                        resourceMonitorException,
                        MvpAgentFailureStages.AgentHostRuntime));
                return;
            }

            try
            {
                if (processExitFailureTask.IsCompleted)
                {
                    var failure = await processExitFailureTask.ConfigureAwait(false);
                    if (failure != AgentHostProcessExitFailure.None)
                    {
                        var processExitException =
                            new AgentHostProcessExitException(failure);
                        TransitionOfflineForAgentHostFailure(
                            monitoredServiceSession,
                            faultedBridge,
                            processExitException,
                            MvpAgentFailureFormatter.FromProcessExitFailure(
                                failure,
                                MvpAgentFailureStages.AgentHostRuntime));
                        return;
                    }
                }
            }
            catch (Exception processExitMonitorException)
            {
                TransitionOfflineForAgentHostFailure(
                    monitoredServiceSession,
                    faultedBridge,
                    processExitMonitorException,
                    MvpAgentFailureFormatter.FromException(
                        processExitMonitorException,
                        MvpAgentFailureStages.AgentHostRuntime));
                return;
            }

            TransitionOffline(faultedBridge, exception);
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
                activeDrawingQueryBinding = null;
                terminalBridgeErrorCode = errorCode;
                online = false;
            }

            if (requestTurn != null)
            {
                requestTurn.CancelTimeout();
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

            var disconnectedMessage =
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
                + "后续问题已拒绝。请先停止并重新启动 AgentHost。";
            PublishSafely(
                ErrorChanged,
                DiagnosticSanitizer
                    .SanitizeText(
                        DiagnosticDataClassification.Exception,
                        disconnectedMessage)
                    .SafeText);
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

        private sealed class DrawingQueryTurnBinding
        {
            private string providerTurnId = string.Empty;

            internal DrawingQueryTurnBinding(
                string requestId,
                string threadId,
                DrawingIndexAgentSnapshot snapshot)
            {
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    throw new ArgumentException("RequestId 不能为空。", nameof(requestId));
                }
                if (string.IsNullOrWhiteSpace(threadId))
                {
                    throw new ArgumentException("ThreadId 不能为空。", nameof(threadId));
                }
                if (snapshot == null)
                {
                    throw new ArgumentNullException(nameof(snapshot));
                }

                RequestId = requestId;
                ThreadId = threadId;
                Snapshot = snapshot;
                SnapshotGeneration = snapshot.Generation;
            }

            internal string RequestId { get; private set; }

            internal string ThreadId { get; private set; }

            internal int SnapshotGeneration { get; private set; }

            internal DrawingIndexAgentSnapshot Snapshot { get; private set; }

            internal bool TryBindProviderTurn(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(providerTurnId))
                {
                    providerTurnId = value;
                    return true;
                }
                return string.Equals(providerTurnId, value, StringComparison.Ordinal);
            }

            internal bool Matches(AgentDrawingQueryRequest request)
            {
                return request != null
                       && SnapshotGeneration == Snapshot.Generation
                       && string.Equals(RequestId, request.RequestId, StringComparison.Ordinal)
                       && string.Equals(ThreadId, request.ThreadId, StringComparison.Ordinal)
                       && string.Equals(providerTurnId, request.TurnId, StringComparison.Ordinal);
            }
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
