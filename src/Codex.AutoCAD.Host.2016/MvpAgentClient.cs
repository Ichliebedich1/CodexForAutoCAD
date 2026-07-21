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
        private AgentBridgeClient bridge;
        private string threadId = string.Empty;
        private string systemSessionId = string.Empty;
        private int started;
        private int stopped;

        internal event Action<string> StatusChanged;

        internal event Action<string> TextChanged;

        internal event Action<string> ErrorChanged;

        internal bool IsStarted
        {
            get { return Volatile.Read(ref started) != 0 && Volatile.Read(ref stopped) == 0; }
        }

        internal Task StartAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            {
                return Task.FromResult(0);
            }

            return Task.Run(() => StartCoreAsync(cancellationToken), cancellationToken);
        }

        internal async Task AskAsync(
            string prompt,
            UnifiedContextState context,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("提示词不能为空。", nameof(prompt));
            }

            await StartAsync(cancellationToken).ConfigureAwait(false);
            AgentBridgeClient currentBridge;
            string currentThread;
            lock (sync)
            {
                currentBridge = bridge ?? throw new InvalidOperationException("Agent Bridge 尚未连接。");
                currentThread = threadId;
            }

            if (context == null || !context.Published)
            {
                throw new InvalidOperationException("请先预选图元并执行 CODEX16CTX。");
            }

            TextChanged?.Invoke(string.Empty);
            StatusChanged?.Invoke("正在向本机 Codex 发送只读问题……");
            var request = new AgentTurnStartRequest
            {
                ThreadId = currentThread,
                ClientTurnId = Guid.NewGuid().ToString("N"),
                Prompt = prompt,
                Context = context.Context,
                ContextSha256 = context.ContextSha256,
            };
            await currentBridge.StartTurnAsync(request, cancellationToken).ConfigureAwait(false);
        }

        internal async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0)
            {
                return;
            }

            AgentBridgeClient currentBridge;
            AgentHostServiceSession currentSession;
            lock (sync)
            {
                currentBridge = bridge;
                currentSession = serviceSession;
                bridge = null;
                serviceSession = null;
            }

            StatusChanged?.Invoke("正在停止 AgentHost……");
            if (currentBridge != null)
            {
                currentBridge.EventReceived -= OnBridgeEvent;
                currentBridge.ConnectionFaulted -= OnBridgeFaulted;
                try
                {
                    await currentBridge.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    currentBridge.Dispose();
                }
            }

            if (currentSession != null)
            {
                await currentSession.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            StatusChanged?.Invoke("AgentHost 已停止；CAD 写入仍禁用。");
        }

        public void Dispose()
        {
            try
            {
                StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                ErrorChanged?.Invoke("停止 AgentHost 失败：" + exception.GetType().Name);
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

                StatusChanged?.Invoke("正在启动并验证 AgentHost……");
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

                var newSessionId = Guid.NewGuid().ToString("N");
                var thread = await newBridge.StartThreadAsync(
                        new AgentThreadStartRequest { ConversationId = newSessionId },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId))
                {
                    throw new InvalidOperationException("AgentHost 未返回有效 Codex thread。");
                }

                lock (sync)
                {
                    serviceSession = newServiceSession;
                    bridge = newBridge;
                    systemSessionId = newSessionId;
                    threadId = thread.ThreadId;
                    newServiceSession = null;
                    newBridge = null;
                }

                StatusChanged?.Invoke("AgentHost 在线；只读 Codex 会话已建立。");
            }
            catch (Exception exception)
            {
                if (newBridge != null)
                {
                    newBridge.EventReceived -= OnBridgeEvent;
                    newBridge.ConnectionFaulted -= OnBridgeFaulted;
                    try
                    {
                        await newBridge.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    newBridge.Dispose();
                }

                if (newServiceSession != null)
                {
                    try
                    {
                        await newServiceSession.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                Interlocked.Exchange(ref started, 0);
                Interlocked.Exchange(ref stopped, 0);
                ErrorChanged?.Invoke("AgentHost 启动失败：" + exception.GetType().Name + "。" + exception.Message);
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

            if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.AssistantMessageDelta, StringComparison.Ordinal))
            {
                TextChanged?.Invoke(bridgeEvent.Delta ?? string.Empty);
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.AssistantMessageCompleted, StringComparison.Ordinal))
            {
                StatusChanged?.Invoke("Codex 回答完成。");
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.TurnStarted, StringComparison.Ordinal))
            {
                StatusChanged?.Invoke("Codex 正在分析当前图纸上下文……");
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.TurnFailed, StringComparison.Ordinal))
            {
                ErrorChanged?.Invoke("Codex 回合失败：" + bridgeEvent.ErrorCode + "。" + bridgeEvent.Error);
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.TurnCancelled, StringComparison.Ordinal))
            {
                StatusChanged?.Invoke("Codex 回合已取消。");
            }
            else if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.ConnectionStateChanged, StringComparison.Ordinal))
            {
                StatusChanged?.Invoke("Agent Bridge 状态：" + bridgeEvent.ConnectionState);
            }
        }

        private void OnBridgeFaulted(object sender, AgentBridgeConnectionFaultedEventArgs args)
        {
            var exception = args == null ? null : args.Exception;
            ErrorChanged?.Invoke(
                exception == null
                    ? "Agent Bridge 已断开；不会自动重试。"
                    : "Agent Bridge 已断开：" + exception.Code + "。不会自动重试。");
        }
    }
}
