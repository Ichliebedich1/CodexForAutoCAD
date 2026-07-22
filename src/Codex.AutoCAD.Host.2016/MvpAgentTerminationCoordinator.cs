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
                    MvpAgentFailureFormatter
                        .FromException(
                            lastFailure,
                            MvpAgentFailureStages.TerminatingAgentHost)
                        .FormatForUser("AutoCAD 退出清理 AgentHost"));
            }
            catch
            {
                // Exit cleanup ownership must not depend on observational Palette callbacks.
            }
        }
    }
}
