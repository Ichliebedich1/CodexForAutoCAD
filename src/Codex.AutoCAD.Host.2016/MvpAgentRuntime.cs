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
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                UpdateAgentStatusSafely(
                    "AgentHost 退出清理失败：" + exception.GetType().Name);
            }
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
                    UnifiedReadOnlyContextRuntime.GetCurrentState(),
                    token)
                .ConfigureAwait(false);
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
