using System.Collections.ObjectModel;

namespace Codex.AutoCAD.Host.Chat;

public enum ChatMessageRole
{
    User,
    Assistant,
    System,
    Tool,
}

public enum ChatMessageStatus
{
    Streaming,
    Completed,
    Failed,
    Cancelled,
}

public enum ContextChipKind
{
    Selection,
    CurrentView,
    DrawingSummary,
    Attachment,
    Custom,
}

public enum ToolTimelineStatus
{
    Queued,
    Running,
    WaitingForApproval,
    Succeeded,
    Failed,
    Cancelled,
}

public enum ApprovalCardKind
{
    Command,
    FileChange,
    Permissions,
    Network,
    Cad,
    Other,
}

public enum ApprovalRiskLevel
{
    Informational,
    Low,
    Medium,
    High,
    Critical,
}

public enum ApprovalCardStatus
{
    Pending,
    Accepted,
    Declined,
    Cancelled,
    Expired,
    Failed,
}

public enum ApprovalDecisionKind
{
    AcceptOnce,
    AcceptForSession,
    DeclineAndContinue,
    DeclineAndStop,
    Cancel,
}

public enum ChatSessionStatus
{
    Idle,
    Running,
    WaitingForApproval,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>
/// Immutable message view consumed by the panel. Streaming changes replace the record instead of
/// exposing a mutable buffer to the UI thread.
/// </summary>
public sealed record ChatMessage(
    string MessageId,
    string TurnId,
    ChatMessageRole Role,
    string Content,
    ChatMessageStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Error = null)
{
    public bool IsTerminal => Status is ChatMessageStatus.Completed
        or ChatMessageStatus.Failed
        or ChatMessageStatus.Cancelled;
}

/// <summary>Immutable description of context explicitly attached to the next turn.</summary>
public sealed record ContextChip(
    string ChipId,
    ContextChipKind Kind,
    string Label,
    string Summary,
    bool IsSensitive = false);

/// <summary>Immutable tool invocation view; it contains display data only and cannot execute work.</summary>
public sealed record ToolTimelineItem(
    string ItemId,
    string TurnId,
    string ToolName,
    string Category,
    string Summary,
    ToolTimelineStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    string? Details = null,
    string? Error = null,
    string? ApprovalId = null)
{
    public bool IsTerminal => Status is ToolTimelineStatus.Succeeded
        or ToolTimelineStatus.Failed
        or ToolTimelineStatus.Cancelled;
}

/// <summary>
/// Immutable approval card. Allowed decisions originate in the trusted bridge; the UI must not
/// synthesize a broader decision set.
/// </summary>
public sealed record ApprovalCardModel(
    string ApprovalId,
    string TurnId,
    ApprovalCardKind Kind,
    string Title,
    string Summary,
    ApprovalRiskLevel Risk,
    ApprovalCardStatus Status,
    IReadOnlyList<ApprovalDecisionKind> AllowedDecisions,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? ResolvedAt = null,
    ApprovalDecisionKind? Decision = null,
    string? ResolutionDetail = null,
    string? ToolItemId = null)
{
    public bool IsPending => Status == ApprovalCardStatus.Pending;
}

/// <summary>A point-in-time, collection-safe view of the entire chat session.</summary>
public sealed record ChatSessionSnapshot(
    string ThreadId,
    string? CurrentTurnId,
    ChatSessionStatus Status,
    long Version,
    long LastAppliedSequence,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ContextChip> ContextChips,
    IReadOnlyList<ToolTimelineItem> ToolTimeline,
    IReadOnlyList<ApprovalCardModel> ApprovalCards,
    string? Error)
{
    internal static IReadOnlyList<T> ReadOnlyCopy<T>(IEnumerable<T> source)
        => new ReadOnlyCollection<T>(source.ToArray());
}
