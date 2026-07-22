using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Codex.AutoCAD.Host2016
{
    internal sealed class MvpAgentStopException : Exception
    {
        internal MvpAgentStopException(IList<Exception> failures)
            : base(
                "Stopping the read-only Agent MVP did not complete every cleanup step.",
                BuildInnerException(failures))
        {
            FailureCount = failures == null ? 0 : failures.Count;
        }

        internal int FailureCount { get; private set; }

        private static Exception BuildInnerException(IList<Exception> failures)
        {
            if (failures == null || failures.Count == 0)
            {
                return new InvalidOperationException("No stop failure was supplied.");
            }

            return failures.Count == 1
                ? failures[0]
                : new AggregateException(failures);
        }
    }

    internal sealed class MvpAgentStopCoordinator
    {
        private readonly object sync = new object();
        private readonly Func<Task>? stopBridge;
        private readonly Action? disposeBridge;
        private readonly Func<Task>? stopAgentHost;
        private Task? currentAttempt;
        private bool bridgeStopped;
        private bool bridgeDisposed;
        private bool agentHostReleased;

        internal MvpAgentStopCoordinator(
            Func<Task>? stopBridge,
            Action? disposeBridge,
            Func<Task>? stopAgentHost)
        {
            this.stopBridge = stopBridge;
            this.disposeBridge = disposeBridge;
            this.stopAgentHost = stopAgentHost;
            bridgeStopped = stopBridge == null;
            bridgeDisposed = disposeBridge == null;
            agentHostReleased = stopAgentHost == null;
        }

        internal bool IsComplete
        {
            get
            {
                lock (sync)
                {
                    return bridgeStopped && bridgeDisposed && agentHostReleased;
                }
            }
        }

        internal Task StopAsync()
        {
            lock (sync)
            {
                if (bridgeStopped && bridgeDisposed && agentHostReleased)
                {
                    return Task.FromResult(0);
                }

                if (currentAttempt != null)
                {
                    return currentAttempt;
                }

                var completion = new TaskCompletionSource<bool>();
                var attempt = completion.Task;
                currentAttempt = attempt;
                _ = CompleteAttemptAsync(completion, attempt);
                return attempt;
            }
        }

        private async Task CompleteAttemptAsync(
            TaskCompletionSource<bool> completion,
            Task attempt)
        {
            Exception failure = null;
            try
            {
                await Task.Run(RunAttemptAsync).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            lock (sync)
            {
                if (ReferenceEquals(currentAttempt, attempt))
                {
                    currentAttempt = null;
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

        private async Task RunAttemptAsync()
        {
            bool runBridgeStop;
            bool runBridgeDispose;
            bool runAgentHost;
            lock (sync)
            {
                runBridgeStop = !bridgeStopped;
                runBridgeDispose = !bridgeDisposed;
                runAgentHost = !agentHostReleased;
            }

            var failures = new List<Exception>();

            // Begin both operations before awaiting either one. Terminating AgentHost releases a
            // pipe read that cannot always be interrupted by closing it from another net45 thread.
            var bridgeTask = runBridgeStop
                ? Start(stopBridge)
                : Task.FromResult(0);
            var agentHostTask = runAgentHost
                ? Start(stopAgentHost)
                : Task.FromResult(0);

            var bridgeStopSucceeded = await ObserveAsync(bridgeTask, failures).ConfigureAwait(false);
            var agentHostStopped = await ObserveAsync(agentHostTask, failures).ConfigureAwait(false);

            var bridgeDisposeSucceeded = !runBridgeDispose;
            if (runBridgeDispose && bridgeStopSucceeded && disposeBridge != null)
            {
                try
                {
                    disposeBridge();
                    bridgeDisposeSucceeded = true;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            lock (sync)
            {
                if (runBridgeStop && bridgeStopSucceeded)
                {
                    bridgeStopped = true;
                }

                if (runBridgeDispose && bridgeDisposeSucceeded)
                {
                    bridgeDisposed = true;
                }

                if (runAgentHost && agentHostStopped)
                {
                    agentHostReleased = true;
                }

            }

            if (failures.Count != 0)
            {
                throw new MvpAgentStopException(failures);
            }
        }

        private static Task Start(Func<Task>? operation)
        {
            if (operation == null)
            {
                return Task.FromResult(0);
            }

            try
            {
                var task = operation();
                if (task == null)
                {
                    throw new InvalidOperationException("A stop operation returned a null task.");
                }

                return task;
            }
            catch (Exception exception)
            {
                var failure = new TaskCompletionSource<bool>();
                failure.SetException(exception);
                return failure.Task;
            }
        }

        private static async Task<bool> ObserveAsync(Task task, IList<Exception> failures)
        {
            try
            {
                await task.ConfigureAwait(false);
                return true;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                return false;
            }
        }
    }
}
