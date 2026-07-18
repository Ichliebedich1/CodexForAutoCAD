using System.Text.Json;
using Codex.AutoCAD.AppServer.Protocol;

namespace Codex.AutoCAD.AgentRuntime;

public abstract record AgentEvent(string ThreadId, string TurnId);

public enum AgentItemLifecycle
{
    Started,
    Completed,
}

public enum AgentItemKind
{
    Unknown,
    UserMessage,
    AgentMessage,
    Plan,
    Reasoning,
    CommandExecution,
    FileChange,
    McpToolCall,
    DynamicToolCall,
    CollaborationToolCall,
    WebSearch,
    ImageView,
    ImageGeneration,
    SubAgentActivity,
    HookPrompt,
    Sleep,
    ContextCompaction,
    EnteredReviewMode,
    ExitedReviewMode,
}

public enum AgentToolKind
{
    Unknown,
    CommandExecution,
    FileChange,
    McpToolCall,
    DynamicToolCall,
    Collaboration,
    WebSearch,
    ImageGeneration,
}

public enum AgentToolStatus
{
    Unknown,
    InProgress,
    Completed,
    Failed,
    Declined,
}

public sealed record AgentItemSnapshot(
    string ItemId,
    AgentItemKind Kind,
    string WireType,
    string? Status,
    string? DisplayName,
    JsonElement Payload);

public sealed record AgentMessageDeltaEvent(
    string ThreadId,
    string TurnId,
    string ItemId,
    string Delta) : AgentEvent(ThreadId, TurnId);

public sealed record AgentItemStateChangedEvent(
    string ThreadId,
    string TurnId,
    AgentItemLifecycle Lifecycle,
    long OccurredAtMs,
    AgentItemSnapshot Item) : AgentEvent(ThreadId, TurnId);

public sealed record AgentToolStateChangedEvent(
    string ThreadId,
    string TurnId,
    AgentItemLifecycle Lifecycle,
    long OccurredAtMs,
    AgentToolKind ToolKind,
    AgentToolStatus Status,
    AgentItemSnapshot Item) : AgentEvent(ThreadId, TurnId);

public sealed record AgentToolProgressEvent(
    string ThreadId,
    string TurnId,
    string ItemId,
    AgentToolKind ToolKind,
    string Message,
    JsonElement? Data = null) : AgentEvent(ThreadId, TurnId);

public sealed record AgentCadProposalCreatedEvent(
    string ThreadId,
    string TurnId,
    string CallId,
    AgentCadOperationBatchProposal Proposal) : AgentEvent(ThreadId, TurnId);

public sealed record AgentDynamicToolRejectedEvent(
    string ThreadId,
    string TurnId,
    string CallId,
    string? Namespace,
    string Tool,
    string Reason) : AgentEvent(ThreadId, TurnId);

public sealed record AgentTurnStateChangedEvent(
    string ThreadId,
    string TurnId,
    AgentTurnStatus Status,
    string? ErrorMessage,
    JsonElement Turn) : AgentEvent(ThreadId, TurnId);

public enum AgentApprovalReviewLifecycle
{
    Started,
    Completed,
}

public sealed record AgentApprovalReviewStateChangedEvent(
    string ThreadId,
    string TurnId,
    string ReviewId,
    string? TargetItemId,
    AgentApprovalReviewLifecycle Lifecycle,
    long OccurredAtMs,
    JsonElement Review) : AgentEvent(ThreadId, TurnId);

public enum AgentApprovalKind
{
    Command,
    FileChange,
    Permissions,
    Cad,
}

public abstract record AgentApprovalRequestedEvent(
    string ThreadId,
    string TurnId,
    string ItemId,
    long StartedAtMs) : AgentEvent(ThreadId, TurnId)
{
    public abstract AgentApprovalKind Kind { get; }
}

public sealed record AgentCommandApprovalRequestedEvent(CommandApprovalRequest Request)
    : AgentApprovalRequestedEvent(Request.ThreadId, Request.TurnId, Request.ItemId, Request.StartedAtMs)
{
    public override AgentApprovalKind Kind => AgentApprovalKind.Command;
}

public sealed record AgentFileChangeApprovalRequestedEvent(FileChangeApprovalRequest Request)
    : AgentApprovalRequestedEvent(Request.ThreadId, Request.TurnId, Request.ItemId, Request.StartedAtMs)
{
    public override AgentApprovalKind Kind => AgentApprovalKind.FileChange;
}

public sealed record AgentPermissionsApprovalRequestedEvent(PermissionsApprovalRequest Request)
    : AgentApprovalRequestedEvent(Request.ThreadId, Request.TurnId, Request.ItemId, Request.StartedAtMs)
{
    public override AgentApprovalKind Kind => AgentApprovalKind.Permissions;
}

public sealed record AgentCadApprovalRequestedEvent(CadApprovalRequest Request)
    : AgentApprovalRequestedEvent(Request.ThreadId, Request.TurnId, Request.ApprovalId, 0)
{
    public override AgentApprovalKind Kind => AgentApprovalKind.Cad;
}

public sealed class AgentEventProjectionFailedEventArgs(string method, Exception exception) : EventArgs
{
    public string Method { get; } = method;

    public Exception Exception { get; } = exception;
}

public sealed class AgentEventObserverFailedEventArgs(AgentEvent agentEvent, Exception exception) : EventArgs
{
    public AgentEvent AgentEvent { get; } = agentEvent;

    public Exception Exception { get; } = exception;
}

public sealed class AgentEventProjectionException : Exception
{
    public AgentEventProjectionException(string method, string message)
        : base($"Cannot project App Server notification '{method}': {message}")
    {
        Method = method;
    }

    public AgentEventProjectionException(string method, string message, Exception innerException)
        : base($"Cannot project App Server notification '{method}': {message}", innerException)
    {
        Method = method;
    }

    public string Method { get; }
}
