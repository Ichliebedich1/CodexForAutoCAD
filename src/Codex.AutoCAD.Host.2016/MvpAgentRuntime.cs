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
                UnifiedPaletteRuntime.UpdateAgentStatus(
                    "AgentHost 退出清理失败：" + exception.GetType().Name);
            }
        }

        internal static Task StartAsync()
        {
            MvpAgentClient current;
            CancellationToken token;
            lock (sync)
            {
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

        internal static async Task StopAsync()
        {
            MvpAgentClient current;
            CancellationTokenSource currentLifetime;
            lock (sync)
            {
                current = client;
                currentLifetime = lifetime;
                client = null;
                lifetime = null;
            }

            if (currentLifetime != null)
            {
                currentLifetime.Cancel();
                currentLifetime.Dispose();
            }

            if (current != null)
            {
                current.StatusChanged -= OnStatusChanged;
                current.TextChanged -= OnTextChanged;
                current.ErrorChanged -= OnErrorChanged;
                await current.StopAsync(CancellationToken.None).ConfigureAwait(false);
                current.Dispose();
            }
        }

        private static void OnStatusChanged(string value)
        {
            UnifiedPaletteRuntime.UpdateAgentStatus(value);
        }

        private static void OnTextChanged(string value)
        {
            UnifiedPaletteRuntime.UpdateAgentText(value);
        }

        private static void OnErrorChanged(string value)
        {
            UnifiedPaletteRuntime.UpdateAgentStatus(value);
        }
    }
}
