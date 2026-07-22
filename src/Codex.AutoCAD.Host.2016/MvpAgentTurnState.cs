using System;
using System.Threading.Tasks;

namespace Codex.AutoCAD.Host2016
{
    internal static class MvpAgentTurnStates
    {
        internal const string StartingProvider = "starting_provider";
        internal const string Running = "running";
        internal const string Cancelling = "cancelling";
        internal const string Completed = "completed";
        internal const string Failed = "failed";
        internal const string Cancelled = "cancelled";
    }

    /// <summary>
    /// Host-owned request identity and state for one read-only Codex turn. All mutation is performed
    /// by MvpAgentClient while holding its sync lock. The Provider turn id never replaces RequestId.
    /// </summary>
    internal sealed class MvpAgentTurnState
    {
        private TaskCompletionSource<bool> cancellationCompletion;

        internal MvpAgentTurnState(string requestId, DateTimeOffset createdAtUtc)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException("RequestId 不能为空。", nameof(requestId));
            }

            RequestId = requestId;
            CreatedAtUtc = createdAtUtc;
            State = MvpAgentTurnStates.StartingProvider;
        }

        internal string RequestId { get; private set; }

        internal string ClientTurnId
        {
            get { return RequestId; }
        }

        internal string ProviderTurnId { get; private set; } = string.Empty;

        internal DateTimeOffset CreatedAtUtc { get; private set; }

        internal string State { get; private set; }

        internal bool CancellationRequested { get; private set; }

        internal bool CancellationDispatchStarted { get; private set; }

        internal TaskCompletionSource<bool> CancellationCompletion
        {
            get { return cancellationCompletion; }
        }

        internal bool IsTerminal
        {
            get
            {
                return string.Equals(State, MvpAgentTurnStates.Completed, StringComparison.Ordinal)
                    || string.Equals(State, MvpAgentTurnStates.Failed, StringComparison.Ordinal)
                    || string.Equals(State, MvpAgentTurnStates.Cancelled, StringComparison.Ordinal);
            }
        }

        internal bool TryBindProviderTurn(string providerTurnId)
        {
            if (IsTerminal || string.IsNullOrWhiteSpace(providerTurnId))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(ProviderTurnId))
            {
                return string.Equals(ProviderTurnId, providerTurnId, StringComparison.Ordinal);
            }

            ProviderTurnId = providerTurnId;
            if (!CancellationRequested)
            {
                State = MvpAgentTurnStates.Running;
            }

            return true;
        }

        internal bool MatchesProviderTurn(string providerTurnId)
        {
            return !string.IsNullOrEmpty(ProviderTurnId)
                && !string.IsNullOrEmpty(providerTurnId)
                && string.Equals(ProviderTurnId, providerTurnId, StringComparison.Ordinal);
        }

        internal void MarkRunning()
        {
            if (!IsTerminal && !CancellationRequested)
            {
                State = MvpAgentTurnStates.Running;
            }
        }

        internal Task RequestCancellation()
        {
            if (IsTerminal)
            {
                return Task.FromResult(0);
            }

            CancellationRequested = true;
            State = MvpAgentTurnStates.Cancelling;
            if (cancellationCompletion == null)
            {
                cancellationCompletion = new TaskCompletionSource<bool>();
            }

            return cancellationCompletion.Task;
        }

        internal bool TryBeginCancellationDispatch()
        {
            if (IsTerminal
                || !CancellationRequested
                || CancellationDispatchStarted
                || string.IsNullOrEmpty(ProviderTurnId))
            {
                return false;
            }

            CancellationDispatchStarted = true;
            return true;
        }

        internal TaskCompletionSource<bool> MarkTerminal(string terminalState)
        {
            if (IsTerminal || !IsTerminalState(terminalState))
            {
                return null;
            }

            State = terminalState;
            return cancellationCompletion;
        }

        internal TaskCompletionSource<bool> ResetCancellationAfterDispatchFailure()
        {
            if (IsTerminal)
            {
                return null;
            }

            var completion = cancellationCompletion;
            cancellationCompletion = null;
            CancellationRequested = false;
            CancellationDispatchStarted = false;
            State = string.IsNullOrEmpty(ProviderTurnId)
                ? MvpAgentTurnStates.StartingProvider
                : MvpAgentTurnStates.Running;
            return completion;
        }

        private static bool IsTerminalState(string state)
        {
            return string.Equals(state, MvpAgentTurnStates.Completed, StringComparison.Ordinal)
                || string.Equals(state, MvpAgentTurnStates.Failed, StringComparison.Ordinal)
                || string.Equals(state, MvpAgentTurnStates.Cancelled, StringComparison.Ordinal);
        }
    }
}
