namespace Codex.AutoCAD.Host.Chat;

/// <summary>
/// Base type for normalized events emitted by <see cref="IAgentBridgeClient"/>. Sequence numbers
/// are strictly increasing within one bridge event stream and event ids are stable for retries.
/// </summary>
public abstract class AgentEvent
{
    protected AgentEvent(
        string eventId,
        long sequence,
        string threadId,
        string? turnId,
        DateTimeOffset occurredAt)
    {
        EventId = RequireText(eventId, nameof(eventId));
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Agent event sequence cannot be negative.");
        }

        Sequence = sequence;
        ThreadId = RequireText(threadId, nameof(threadId));
        TurnId = turnId is null ? null : RequireText(turnId, nameof(turnId));
        OccurredAt = occurredAt;
    }

    public string EventId { get; }

    public long Sequence { get; }

    public string ThreadId { get; }

    public string? TurnId { get; }

    public DateTimeOffset OccurredAt { get; }

    protected static string RequireText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public sealed class AgentContextChangedEvent : AgentEvent
{
    public AgentContextChangedEvent(
        string eventId,
        long sequence,
        string threadId,
        IReadOnlyList<ContextChip> contextChips,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, null, occurredAt)
    {
        ArgumentNullException.ThrowIfNull(contextChips);
        ContextChips = ChatSessionSnapshot.ReadOnlyCopy(contextChips);
    }

    public IReadOnlyList<ContextChip> ContextChips { get; }
}

public sealed class AgentTurnStartedEvent : AgentEvent
{
    public AgentTurnStartedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
    }
}

public sealed class AgentUserMessageAddedEvent : AgentEvent
{
    public AgentUserMessageAddedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string messageId,
        string content,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        MessageId = RequireText(messageId, nameof(messageId));
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public string MessageId { get; }

    public string Content { get; }
}

public sealed class AgentAssistantMessageStartedEvent : AgentEvent
{
    public AgentAssistantMessageStartedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string messageId,
        DateTimeOffset occurredAt,
        string initialContent = "")
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        MessageId = RequireText(messageId, nameof(messageId));
        InitialContent = initialContent ?? throw new ArgumentNullException(nameof(initialContent));
    }

    public string MessageId { get; }

    public string InitialContent { get; }
}

public sealed class AgentAssistantMessageDeltaEvent : AgentEvent
{
    public AgentAssistantMessageDeltaEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string messageId,
        string delta,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        MessageId = RequireText(messageId, nameof(messageId));
        Delta = delta ?? throw new ArgumentNullException(nameof(delta));
    }

    public string MessageId { get; }

    public string Delta { get; }
}

public sealed class AgentAssistantMessageCompletedEvent : AgentEvent
{
    public AgentAssistantMessageCompletedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string messageId,
        DateTimeOffset occurredAt,
        string? finalContent = null)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        MessageId = RequireText(messageId, nameof(messageId));
        FinalContent = finalContent;
    }

    public string MessageId { get; }

    /// <summary>When null, the accumulated streaming content remains authoritative.</summary>
    public string? FinalContent { get; }
}

public sealed class AgentAssistantMessageFailedEvent : AgentEvent
{
    public AgentAssistantMessageFailedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string messageId,
        string error,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        MessageId = RequireText(messageId, nameof(messageId));
        Error = RequireText(error, nameof(error));
    }

    public string MessageId { get; }

    public string Error { get; }
}

public sealed class AgentAssistantMessageCancelledEvent : AgentEvent
{
    public AgentAssistantMessageCancelledEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string messageId,
        DateTimeOffset occurredAt,
        string? reason = null)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        MessageId = RequireText(messageId, nameof(messageId));
        Reason = reason;
    }

    public string MessageId { get; }

    public string? Reason { get; }
}

public sealed class AgentToolStartedEvent : AgentEvent
{
    public AgentToolStartedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        string category,
        string summary,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        ItemId = RequireText(itemId, nameof(itemId));
        ToolName = RequireText(toolName, nameof(toolName));
        Category = RequireText(category, nameof(category));
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    public string ItemId { get; }

    public string ToolName { get; }

    public string Category { get; }

    public string Summary { get; }
}

public sealed class AgentToolProgressEvent : AgentEvent
{
    public AgentToolProgressEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string itemId,
        DateTimeOffset occurredAt,
        string? summary = null,
        string? details = null)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        ItemId = RequireText(itemId, nameof(itemId));
        Summary = summary;
        Details = details;
    }

    public string ItemId { get; }

    public string? Summary { get; }

    public string? Details { get; }
}

public sealed class AgentToolCompletedEvent : AgentEvent
{
    public AgentToolCompletedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string itemId,
        DateTimeOffset occurredAt,
        string? resultSummary = null)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        ItemId = RequireText(itemId, nameof(itemId));
        ResultSummary = resultSummary;
    }

    public string ItemId { get; }

    public string? ResultSummary { get; }
}

public sealed class AgentToolFailedEvent : AgentEvent
{
    public AgentToolFailedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string itemId,
        string error,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        ItemId = RequireText(itemId, nameof(itemId));
        Error = RequireText(error, nameof(error));
    }

    public string ItemId { get; }

    public string Error { get; }
}

public sealed class AgentToolCancelledEvent : AgentEvent
{
    public AgentToolCancelledEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string itemId,
        DateTimeOffset occurredAt,
        string? reason = null)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        ItemId = RequireText(itemId, nameof(itemId));
        Reason = reason;
    }

    public string ItemId { get; }

    public string? Reason { get; }
}

public sealed class AgentApprovalRequestedEvent : AgentEvent
{
    public AgentApprovalRequestedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string approvalId,
        ApprovalCardKind kind,
        string title,
        string summary,
        ApprovalRiskLevel risk,
        IReadOnlyList<ApprovalDecisionKind> allowedDecisions,
        DateTimeOffset occurredAt,
        DateTimeOffset? expiresAt = null,
        string? toolItemId = null)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        ApprovalId = RequireText(approvalId, nameof(approvalId));
        Title = RequireText(title, nameof(title));
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        ArgumentNullException.ThrowIfNull(allowedDecisions);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Unknown approval kind is denied by default.");
        }

        if (!Enum.IsDefined(risk))
        {
            throw new ArgumentOutOfRangeException(nameof(risk), "Unknown approval risk is denied by default.");
        }

        if (allowedDecisions.Count == 0)
        {
            throw new ArgumentException("An approval must expose at least one trusted decision.", nameof(allowedDecisions));
        }

        if (allowedDecisions.Any(static decision => !Enum.IsDefined(decision)))
        {
            throw new ArgumentOutOfRangeException(nameof(allowedDecisions),
                "Unknown approval decision is denied by default.");
        }

        if (expiresAt is not null && expiresAt <= occurredAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Approval expiry must be later than request time.");
        }

        Kind = kind;
        Risk = risk;
        AllowedDecisions = ChatSessionSnapshot.ReadOnlyCopy(allowedDecisions.Distinct());
        ExpiresAt = expiresAt;
        ToolItemId = toolItemId;
    }

    public string ApprovalId { get; }

    public ApprovalCardKind Kind { get; }

    public string Title { get; }

    public string Summary { get; }

    public ApprovalRiskLevel Risk { get; }

    public IReadOnlyList<ApprovalDecisionKind> AllowedDecisions { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public string? ToolItemId { get; }
}

public sealed class AgentApprovalResolvedEvent : AgentEvent
{
    public AgentApprovalResolvedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string approvalId,
        ApprovalDecisionKind decision,
        DateTimeOffset occurredAt,
        string? detail = null)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        ApprovalId = RequireText(approvalId, nameof(approvalId));
        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), "Unknown approval decision is denied by default.");
        }

        Decision = decision;
        Detail = detail;
    }

    public string ApprovalId { get; }

    public ApprovalDecisionKind Decision { get; }

    public string? Detail { get; }
}

public sealed class AgentApprovalExpiredEvent : AgentEvent
{
    public AgentApprovalExpiredEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string approvalId,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        ApprovalId = RequireText(approvalId, nameof(approvalId));
    }

    public string ApprovalId { get; }
}

public sealed class AgentApprovalFailedEvent : AgentEvent
{
    public AgentApprovalFailedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string approvalId,
        string error,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        ApprovalId = RequireText(approvalId, nameof(approvalId));
        Error = RequireText(error, nameof(error));
    }

    public string ApprovalId { get; }

    public string Error { get; }
}

public sealed class AgentTurnCompletedEvent : AgentEvent
{
    public AgentTurnCompletedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
    }
}

public sealed class AgentTurnFailedEvent : AgentEvent
{
    public AgentTurnFailedEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        string error,
        DateTimeOffset occurredAt)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        Error = RequireText(error, nameof(error));
    }

    public string Error { get; }
}

public sealed class AgentTurnCancelledEvent : AgentEvent
{
    public AgentTurnCancelledEvent(
        string eventId,
        long sequence,
        string threadId,
        string turnId,
        DateTimeOffset occurredAt,
        string? reason = null)
        : base(eventId, sequence, threadId, turnId, occurredAt)
    {
        Reason = reason;
    }

    public string? Reason { get; }
}
