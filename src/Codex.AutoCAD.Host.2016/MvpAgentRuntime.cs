using System;
using System.Threading;
using System.Threading.Tasks;

namespace Codex.AutoCAD.Host2016
{
    internal static class MvpAgentRuntime
    {
        private static readonly object sync = new object();
        private static MvpAgentClient client;
        private static CancellationTokenSource lifetime;
        private static Task stopTask;

        internal static void Initialize()
        {
            lock (sync)
            {
                if (lifetime == null)
                {
                    lifetime = new CancellationTokenSource();
                }
            }
        }

        internal static void Terminate()
        {
            MvpAgentTerminationCoordinator.Terminate(
                () => StopAsync(),
                UpdateAgentStatusSafely);
        }

        internal static Task StartAsync()
        {
            MvpAgentClient current;
            CancellationToken token;
            lock (sync)
            {
                if (stopTask != null)
                {
                    throw new InvalidOperationException(
                        "AgentHost 正在停止；完成或重试剩余清理后才能再次启动。");
                }

                if (lifetime == null)
                {
                    lifetime = new CancellationTokenSource();
                }

                if (client == null)
                {
                    client = new MvpAgentClient();
                    client.StatusChanged += OnStatusChanged;
                    client.TextChanged += OnTextChanged;
                    client.ErrorChanged += OnErrorChanged;
                }

                current = client;
                token = lifetime.Token;
            }

            return current.StartAsync(token);
        }

        internal static async Task AskAsync(string prompt)
        {
            var context = UnifiedReadOnlyContextRuntime.GetCurrentState();
            var hasSelectionContext = UnifiedReadOnlyContextRuntime.IsCurrentPublishedState(context);
            DrawingIndexAgentSnapshot drawingIndexSnapshot;
            var hasDrawingIndex = DrawingIndexRuntime.TryFreezeAgentSnapshot(
                out drawingIndexSnapshot);
            if (!hasSelectionContext && !hasDrawingIndex)
            {
                throw new InvalidOperationException(
                    "请先执行 CODEX16INDEX 建立整图索引，或预选图元并执行 CODEX16CTX。");
            }

            MvpAgentClient current;
            CancellationToken token;
            lock (sync)
            {
                current = client;
                token = lifetime == null ? CancellationToken.None : lifetime.Token;
            }

            if (current == null)
            {
                await StartAsync().ConfigureAwait(false);
                lock (sync)
                {
                    current = client;
                }
            }

            await current.AskAsync(
                    prompt,
                    hasSelectionContext ? context : null,
                    hasSelectionContext
                        ? (Func<bool>)(() =>
                            UnifiedReadOnlyContextRuntime.IsCurrentPublishedState(context))
                        : null,
                    hasDrawingIndex ? drawingIndexSnapshot : null,
                    token)
                .ConfigureAwait(false);
        }

        internal static Task CancelAsync()
        {
            MvpAgentClient current;
            lock (sync)
            {
                current = client;
            }

            if (current == null)
            {
                UpdateAgentStatusSafely("当前没有运行中的 Codex 回合可取消。");
                return Task.FromResult(0);
            }

            return current.CancelActiveTurnAsync(CancellationToken.None);
        }

        internal static async Task NewConversationAsync()
        {
            MvpAgentClient current;
            CancellationToken token;
            lock (sync)
            {
                current = client;
                token = lifetime == null ? CancellationToken.None : lifetime.Token;
            }

            if (current == null)
            {
                throw new InvalidOperationException(
                    "请先执行 CODEX16AGENTSTART，确认 AgentHost 在线后再新建对话。");
            }

            var context = UnifiedReadOnlyContextRuntime.GetCurrentState();
            var documentId = context != null
                && context.Published
                && context.Context != null
                && context.Context.Document != null
                    ? context.Context.Document.DocumentId
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(documentId))
            {
                DrawingIndexAgentSnapshot drawingIndexSnapshot;
                if (DrawingIndexRuntime.TryFreezeAgentSnapshot(out drawingIndexSnapshot))
                {
                    documentId = drawingIndexSnapshot.DocumentId;
                }
            }
            await current.NewConversationAsync(documentId, token)
                .ConfigureAwait(false);
        }

        internal static void ClearAll()
        {
            MvpAgentClient current;
            lock (sync)
            {
                current = client;
            }

            if (current != null)
            {
                current.ClearConversation();
            }

            UnifiedReadOnlyContextRuntime.Clear("all-user-command");
            UnifiedPaletteRuntime.UpdateAgentText(string.Empty);
            UpdateAgentStatusSafely(
                "CAD 上下文、回答文本和当前 Codex 对话已清除；下一次提问将建立新对话。");
        }

        internal static void HandleDocumentChanged()
        {
            MvpAgentClient current;
            lock (sync)
            {
                current = client;
            }

            if (current != null)
            {
                current.InvalidateConversationForDocumentChange();
            }
        }

        internal static Task StopAsync()
        {
            MvpAgentClient current;
            CancellationTokenSource currentLifetime;
            lock (sync)
            {
                if (stopTask != null)
                {
                    return stopTask;
                }

                current = client;
                currentLifetime = lifetime;
                var completion = new TaskCompletionSource<bool>();
                var attempt = completion.Task;
                stopTask = attempt;
                _ = CompleteStopAttemptAsync(
                    completion,
                    attempt,
                    current,
                    currentLifetime);
                return attempt;
            }
        }

        private static async Task CompleteStopAttemptAsync(
            TaskCompletionSource<bool> completion,
            Task attempt,
            MvpAgentClient current,
            CancellationTokenSource currentLifetime)
        {
            Exception failure = null;
            try
            {
                await Task.Run(
                        () => StopCoreAsync(current, currentLifetime),
                        CancellationToken.None)
                    .ConfigureAwait(false);
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

        private static async Task StopCoreAsync(
            MvpAgentClient current,
            CancellationTokenSource currentLifetime)
        {
            if (currentLifetime != null)
            {
                try
                {
                    currentLifetime.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // A concurrent successful STOP already released this lifetime.
                }
            }

            if (current == null)
            {
                lock (sync)
                {
                    if (client == null && ReferenceEquals(lifetime, currentLifetime))
                    {
                        lifetime = null;
                    }
                }

                if (currentLifetime != null)
                {
                    currentLifetime.Dispose();
                }

                UpdateAgentStatusSafely(
                    "AgentHost 已停止；CAD 写入仍禁用。");
                return;
            }

            await current.StopAsync(CancellationToken.None).ConfigureAwait(false);

            var releaseOwnership = false;
            lock (sync)
            {
                if (ReferenceEquals(client, current))
                {
                    client = null;
                    if (ReferenceEquals(lifetime, currentLifetime))
                    {
                        lifetime = null;
                    }

                    releaseOwnership = true;
                }
            }

            if (releaseOwnership)
            {
                current.StatusChanged -= OnStatusChanged;
                current.TextChanged -= OnTextChanged;
                current.ErrorChanged -= OnErrorChanged;
                current.Dispose();
                if (currentLifetime != null)
                {
                    currentLifetime.Dispose();
                }
            }
        }

        private static void OnStatusChanged(string value)
        {
            UpdateAgentStatusSafely(value);
        }

        private static void OnTextChanged(string value)
        {
            UnifiedPaletteRuntime.UpdateAgentText(value);
        }

        private static void OnErrorChanged(string value)
        {
            UpdateAgentStatusSafely(value);
        }

        private static void UpdateAgentStatusSafely(string value)
        {
            try
            {
                UnifiedPaletteRuntime.UpdateAgentStatus(value);
            }
            catch
            {
                // Palette state is observational and must never retain AgentHost resources.
            }
        }
    }
}
