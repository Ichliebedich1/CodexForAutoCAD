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
        private string activeTurnId = string.Empty;
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
            IAgentBridgeClient currentBridge;
            string currentThread;
            lock (sync)
            {
                EnsureOnlineForAskLocked();
                currentBridge = bridge;
                currentThread = threadId;
            }

            if (context == null || !context.Published)
            {
                throw new InvalidOperationException("请先预选图元并执行 CODEX16CTX。");
            }

            if (!isCurrentContext())
            {
                throw new InvalidOperationException("当前 CAD 上下文已失效，请重新执行 CODEX16CTX。");
            }

            PublishSafely(TextChanged, string.Empty);
            PublishSafely(StatusChanged, "正在向本机 Codex 发送只读问题……");
            var request = new AgentTurnStartV2Request
            {
                ThreadId = currentThread,
                ClientTurnId = Guid.NewGuid().ToString("N"),
                Prompt = prompt,
                ContextV2 = context.Context,
                ContextV2Sha256 = context.ContextSha256,
            };
            var turn = await currentBridge.StartTurnV2Async(request, cancellationToken)
                .ConfigureAwait(false);
            lock (sync)
            {
                if (!ReferenceEquals(bridge, currentBridge) || !online)
                {
                    throw CreateUnavailableExceptionLocked();
                }

                activeTurnId = turn.TurnId;
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
                    "停止 AgentHost 失败："
                    + exception.GetType().Name
                    + "。可再次执行 CODEX16AGENTSTOP 重试剩余清理。");
                throw;
            }

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
                activeTurnId = string.Empty;
                terminalBridgeErrorCode = string.Empty;
                stopCompleted = true;
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
                PublishSafely(ErrorChanged, "停止 AgentHost 失败：" + exception.GetType().Name);
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
                    activeTurnId = string.Empty;
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
                        "AgentHost 启动失败且清理未完成："
                        + cleanupFailure.GetType().Name
                        + "。请执行 CODEX16AGENTSTOP 重试清理。");
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
                    "AgentHost 启动失败：" + exception.GetType().Name + "。" + exception.Message);
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
            var bridgeEvent = args.BridgeEvent;
            if (bridgeEvent == null)
            {
                return;
            }

            if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.TurnCompleted, StringComparison.Ordinal)
                || string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.TurnFailed, StringComparison.Ordinal)
                || string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.TurnCancelled, StringComparison.Ordinal))
            {
                ClearActiveTurn(bridgeEvent.TurnId);
            }

            if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.AssistantMessageDelta, StringComparison.Ordinal))
            {
                PublishSafely(TextChanged, bridgeEvent.Delta ?? string.Empty);
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.AssistantMessageCompleted, StringComparison.Ordinal))
            {
                PublishSafely(StatusChanged, "Codex 回答完成。");
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.TurnStarted, StringComparison.Ordinal))
            {
                PublishSafely(StatusChanged, "Codex 正在分析当前图纸上下文……");
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.TurnFailed, StringComparison.Ordinal))
            {
                PublishSafely(
                    ErrorChanged,
                    "Codex 回合失败：" + bridgeEvent.ErrorCode + "。" + bridgeEvent.Error);
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.TurnCancelled, StringComparison.Ordinal))
            {
                PublishSafely(StatusChanged, "Codex 回合已取消。");
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.ConnectionStateChanged, StringComparison.Ordinal))
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

                PublishSafely(StatusChanged, "Agent Bridge 状态：" + bridgeEvent.ConnectionState);
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
            var errorCode = NormalizeBridgeErrorCode(exception);
            bool hadActiveTurn;
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

                hadActiveTurn = !string.IsNullOrEmpty(activeTurnId);
                activeTurnId = string.Empty;
                terminalBridgeErrorCode = errorCode;
                online = false;
            }

            PublishSafely(
                ErrorChanged,
                "Agent Bridge 已断开（error_code="
                + errorCode
                + "）；"
                + (hadActiveTurn ? "当前回合已终止；" : string.Empty)
                + "后续问题已拒绝。请先停止并重新启动 AgentHost。");
        }

        private void ClearActiveTurn(string completedTurnId)
        {
            lock (sync)
            {
                if (string.IsNullOrEmpty(activeTurnId)
                    || string.IsNullOrEmpty(completedTurnId)
                    || string.Equals(activeTurnId, completedTurnId, StringComparison.Ordinal))
                {
                    activeTurnId = string.Empty;
                }
            }
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

        private static string NormalizeBridgeErrorCode(AgentBridgeClientException exception)
        {
            var code = exception == null ? null : exception.Code;
            return IsKnownBridgeErrorCode(code)
                ? code
                : AgentBridgeErrorCodes.ConnectionLost;
        }

        private static bool IsKnownBridgeErrorCode(string code)
        {
            return string.Equals(code, AgentBridgeErrorCodes.Offline, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ContractMismatch, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.AuthenticationFailed, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ReplayRejected, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.RequestInvalid, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ContextInvalid, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ContextHashMismatch, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.AgentUnavailable, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ConnectionLost, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.Timeout, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.Busy, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.TurnNotFound, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ApprovalInvalid, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ApprovalExpired, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ApprovalAlreadyConsumed, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ResultIdentityMismatch, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.InternalError, StringComparison.Ordinal);
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
