namespace Codex.AutoCAD.AgentRuntime;

/// <summary>
/// Terminal result reported by the single trusted AutoCAD proposal broker. A proposal is successful
/// only after the broker reports <see cref="Applied"/>; merely publishing or enqueueing it is not a
/// successful dynamic-tool call.
/// </summary>
public enum AgentCadProposalOutcome
{
    Applied,
    Rejected,
    Failed,
}

public sealed record AgentCadProposalResult(
    AgentCadProposalOutcome Outcome,
    string Message,
    string ProposalId,
    string ThreadId,
    string TurnId,
    string CallId)
{
    public override string ToString()
        => nameof(AgentCadProposalResult)
            + " { Outcome = "
            + Outcome
            + ", MessageConfigured = "
            + AgentDiagnosticFormatting.Configured(Message)
            + ", ProposalIdConfigured = "
            + AgentDiagnosticFormatting.Configured(ProposalId)
            + ", ThreadIdConfigured = "
            + AgentDiagnosticFormatting.Configured(ThreadId)
            + ", TurnIdConfigured = "
            + AgentDiagnosticFormatting.Configured(TurnId)
            + ", CallIdConfigured = "
            + AgentDiagnosticFormatting.Configured(CallId)
            + " }";

    public static AgentCadProposalResult Applied(
        AgentCadOperationBatchProposal proposal,
        string message = "The CAD proposal was applied.")
        => ForProposal(proposal, AgentCadProposalOutcome.Applied, message);

    public static AgentCadProposalResult Rejected(
        AgentCadOperationBatchProposal proposal,
        string message = "The CAD proposal was rejected.")
        => ForProposal(proposal, AgentCadProposalOutcome.Rejected, message);

    public static AgentCadProposalResult Failed(
        AgentCadOperationBatchProposal proposal,
        string message = "The CAD proposal failed.")
        => ForProposal(proposal, AgentCadProposalOutcome.Failed, message);

    private static AgentCadProposalResult ForProposal(
        AgentCadOperationBatchProposal proposal,
        AgentCadProposalOutcome outcome,
        string message)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return new AgentCadProposalResult(
            outcome,
            message,
            proposal.ProposalId,
            proposal.ThreadId,
            proposal.TurnId,
            proposal.CallId);
    }
}

/// <summary>
/// Single trusted bridge from the agent runtime to AutoCAD. Implementations must bind the proposal
/// to the active document, preview it, obtain one-time approval, execute it transactionally and
/// return the terminal result.
/// </summary>
public interface IAgentCadProposalBroker
{
    ValueTask<AgentCadProposalResult> ExecuteAsync(
        AgentCadOperationBatchProposal proposal,
        CancellationToken cancellationToken);
}
