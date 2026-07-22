using System;
using System.Threading.Tasks;

namespace Codex.AutoCAD.Host2016
{
    internal static class MvpAgentTerminationCoordinator
    {
        private const int MaximumAttempts = 2;

        internal static void Terminate(
            Func<Task> stopAsync,
            Action<string> reportFailure)
        {
            if (stopAsync == null)
            {
                throw new ArgumentNullException(nameof(stopAsync));
            }

            if (reportFailure == null)
            {
                throw new ArgumentNullException(nameof(reportFailure));
            }

            Exception lastFailure = new InvalidOperationException(
                "AgentHost exit cleanup did not start.");
            for (var attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                try
                {
                    var stopTask = stopAsync();
                    if (stopTask == null)
                    {
                        throw new InvalidOperationException(
                            "AgentHost stop operation returned no task.");
                    }

                    stopTask.GetAwaiter().GetResult();
                    return;
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                }
            }

            try
            {
                reportFailure(
                    "AgentHost 退出清理失败：" + lastFailure.GetType().Name);
            }
            catch
            {
                // Exit cleanup ownership must not depend on observational Palette callbacks.
            }
        }
    }
}
