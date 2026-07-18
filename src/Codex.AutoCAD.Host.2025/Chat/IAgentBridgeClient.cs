namespace Codex.AutoCAD.Host.Chat;

public sealed class AgentTurnRequest
{
    public AgentTurnRequest(
        string threadId,
        string turnId,
        string prompt,
        IReadOnlyList<ContextChip>? contextChips = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        ThreadId = threadId;
        TurnId = turnId;
        Prompt = prompt;
        ContextChips = ChatSessionSnapshot.ReadOnlyCopy(contextChips ?? Array.Empty<ContextChip>());
    }

    public string ThreadId { get; }

    public string TurnId { get; }

    public string Prompt { get; }

    public IReadOnlyList<ContextChip> ContextChips { get; }
}

public sealed record AgentTurnReference(string ThreadId, string TurnId);

public sealed class AgentApprovalDecisionRequest
{
    public AgentApprovalDecisionRequest(
        string threadId,
        string turnId,
        string approvalId,
        ApprovalDecisionKind decision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);

        ThreadId = threadId;
        TurnId = turnId;
        ApprovalId = approvalId;
        Decision = decision;
    }

    public string ThreadId { get; }

    public string TurnId { get; }

    public string ApprovalId { get; }

    public ApprovalDecisionKind Decision { get; }
}

/// <summary>
/// Narrow transport boundary between the panel and AgentHost. Implementations may submit turns,
/// interrupt turns and answer trusted approval requests. Deliberately absent are arbitrary command,
/// file-write and CAD-write methods; those operations remain behind AgentHost and native approval
/// gates.
/// </summary>
public interface IAgentBridgeClient : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task SubmitTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default);

    Task CancelTurnAsync(AgentTurnReference turn, CancellationToken cancellationToken = default);

    Task ResolveApprovalAsync(
        AgentApprovalDecisionRequest decision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one ordered stream of normalized events. Implementations must assign monotonically
    /// increasing sequence numbers and stable event ids before yielding an event.
    /// </summary>
    IAsyncEnumerable<AgentEvent> ReadEventsAsync(CancellationToken cancellationToken = default);
}
