using System.Text.Json;
using Codex.AutoCAD.AppServer.Protocol;
using DiagnosticDataClassification = Codex.AutoCAD.Contracts.DiagnosticDataClassification;
using DiagnosticRedactionKinds = Codex.AutoCAD.Contracts.DiagnosticRedactionKinds;
using DiagnosticSanitizer = Codex.AutoCAD.Contracts.DiagnosticSanitizer;

namespace Codex.AutoCAD.AgentRuntime;

public abstract record AgentEvent(string ThreadId, string TurnId);

/// <summary>
/// Metadata-only compatibility snapshot used by diagnostic events. Provider identifiers and the
/// original event payload are intentionally omitted.
/// </summary>
public sealed record AgentEventDiagnosticSnapshot(string EventType)
    : AgentEvent(string.Empty, string.Empty)
{
    public override string ToString()
        => nameof(AgentEventDiagnosticSnapshot)
            + " { EventTypeConfigured = "
            + AgentDiagnosticFormatting.Configured(EventType)
            + " }";
}

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
    JsonElement Payload)
{
    public override string ToString()
        => nameof(AgentItemSnapshot)
            + " { ItemIdConfigured = "
            + AgentDiagnosticFormatting.Configured(ItemId)
            + ", Kind = "
            + Kind
            + ", WireTypeConfigured = "
            + AgentDiagnosticFormatting.Configured(WireType)
            + ", StatusConfigured = "
            + AgentDiagnosticFormatting.Configured(Status)
            + ", DisplayNameConfigured = "
            + AgentDiagnosticFormatting.Configured(DisplayName)
            + ", PayloadPresent = "
            + (Payload.ValueKind != JsonValueKind.Undefined)
            + " }";
}

public sealed record AgentMessageDeltaEvent(
    string ThreadId,
    string TurnId,
    string ItemId,
    string Delta) : AgentEvent(ThreadId, TurnId)
{
    public override string ToString()
        => nameof(AgentMessageDeltaEvent)
            + AgentEventDiagnosticFormatting.EventIdentity(ThreadId, TurnId)
            + ", ItemIdConfigured = "
            + AgentDiagnosticFormatting.Configured(ItemId)
            + ", DeltaConfigured = "
            + AgentDiagnosticFormatting.Configured(Delta)
            + " }";
}

public sealed record AgentItemStateChangedEvent(
    string ThreadId,
    string TurnId,
    AgentItemLifecycle Lifecycle,
    long OccurredAtMs,
    AgentItemSnapshot Item) : AgentEvent(ThreadId, TurnId)
{
    public override string ToString()
        => nameof(AgentItemStateChangedEvent)
            + AgentEventDiagnosticFormatting.EventIdentity(ThreadId, TurnId)
            + ", Lifecycle = "
            + Lifecycle
            + ", ItemPresent = "
            + (Item is not null)
            + " }";
}

public sealed record AgentToolStateChangedEvent(
    string ThreadId,
    string TurnId,
    AgentItemLifecycle Lifecycle,
    long OccurredAtMs,
    AgentToolKind ToolKind,
    AgentToolStatus Status,
    AgentItemSnapshot Item) : AgentEvent(ThreadId, TurnId)
{
    public override string ToString()
        => nameof(AgentToolStateChangedEvent)
            + AgentEventDiagnosticFormatting.EventIdentity(ThreadId, TurnId)
            + ", Lifecycle = "
            + Lifecycle
            + ", ToolKind = "
            + ToolKind
            + ", Status = "
            + Status
            + ", ItemPresent = "
            + (Item is not null)
            + " }";
}

public sealed record AgentToolProgressEvent(
    string ThreadId,
    string TurnId,
    string ItemId,
    AgentToolKind ToolKind,
    string Message,
    JsonElement? Data = null) : AgentEvent(ThreadId, TurnId)
{
    public override string ToString()
        => nameof(AgentToolProgressEvent)
            + AgentEventDiagnosticFormatting.EventIdentity(ThreadId, TurnId)
            + ", ItemIdConfigured = "
            + AgentDiagnosticFormatting.Configured(ItemId)
            + ", ToolKind = "
            + ToolKind
            + ", MessageConfigured = "
            + AgentDiagnosticFormatting.Configured(Message)
            + ", DataPresent = "
            + Data.HasValue
            + " }";
}

public sealed record AgentCadProposalCreatedEvent(
    string ThreadId,
    string TurnId,
    string CallId,
    AgentCadOperationBatchProposal Proposal) : AgentEvent(ThreadId, TurnId)
{
    public override string ToString()
        => nameof(AgentCadProposalCreatedEvent)
            + AgentEventDiagnosticFormatting.EventIdentity(ThreadId, TurnId)
            + ", CallIdConfigured = "
            + AgentDiagnosticFormatting.Configured(CallId)
            + ", ProposalPresent = "
            + (Proposal is not null)
            + " }";
}

public sealed record AgentDynamicToolRejectedEvent(
    string ThreadId,
    string TurnId,
    string CallId,
    string? Namespace,
    string Tool,
    string Reason) : AgentEvent(ThreadId, TurnId)
{
    public override string ToString()
        => nameof(AgentDynamicToolRejectedEvent)
            + AgentEventDiagnosticFormatting.EventIdentity(ThreadId, TurnId)
            + ", CallIdConfigured = "
            + AgentDiagnosticFormatting.Configured(CallId)
            + ", NamespaceConfigured = "
            + AgentDiagnosticFormatting.Configured(Namespace)
            + ", ToolConfigured = "
            + AgentDiagnosticFormatting.Configured(Tool)
            + ", ReasonConfigured = "
            + AgentDiagnosticFormatting.Configured(Reason)
            + " }";
}

public sealed record AgentTurnStateChangedEvent(
    string ThreadId,
    string TurnId,
    AgentTurnStatus Status,
    string? ErrorMessage,
    JsonElement Turn) : AgentEvent(ThreadId, TurnId)
{
    public DiagnosticDataClassification? ErrorDiagnosticClassification { get; init; }

    public DiagnosticRedactionKinds ErrorDiagnosticRedactions { get; init; }

    public override string ToString()
        => nameof(AgentTurnStateChangedEvent)
            + AgentEventDiagnosticFormatting.EventIdentity(ThreadId, TurnId)
            + ", Status = "
            + Status
            + ", ErrorMessageConfigured = "
            + AgentDiagnosticFormatting.Configured(ErrorMessage)
            + ", TurnPresent = "
            + (Turn.ValueKind != JsonValueKind.Undefined)
            + ", ErrorDiagnosticClassificationPresent = "
            + ErrorDiagnosticClassification.HasValue
            + ", ErrorDiagnosticRedactions = "
            + (int)ErrorDiagnosticRedactions
            + " }";
}

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
    JsonElement Review) : AgentEvent(ThreadId, TurnId)
{
    public override string ToString()
        => nameof(AgentApprovalReviewStateChangedEvent)
            + AgentEventDiagnosticFormatting.EventIdentity(ThreadId, TurnId)
            + ", ReviewIdConfigured = "
            + AgentDiagnosticFormatting.Configured(ReviewId)
            + ", TargetItemIdConfigured = "
            + AgentDiagnosticFormatting.Configured(TargetItemId)
            + ", Lifecycle = "
            + Lifecycle
            + ", ReviewPresent = "
            + (Review.ValueKind != JsonValueKind.Undefined)
            + " }";
}

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

    public override string ToString()
        => nameof(AgentCommandApprovalRequestedEvent)
            + AgentEventDiagnosticFormatting.Approval(Kind, Request is not null);
}

public sealed record AgentFileChangeApprovalRequestedEvent(FileChangeApprovalRequest Request)
    : AgentApprovalRequestedEvent(Request.ThreadId, Request.TurnId, Request.ItemId, Request.StartedAtMs)
{
    public override AgentApprovalKind Kind => AgentApprovalKind.FileChange;

    public override string ToString()
        => nameof(AgentFileChangeApprovalRequestedEvent)
            + AgentEventDiagnosticFormatting.Approval(Kind, Request is not null);
}

public sealed record AgentPermissionsApprovalRequestedEvent(PermissionsApprovalRequest Request)
    : AgentApprovalRequestedEvent(Request.ThreadId, Request.TurnId, Request.ItemId, Request.StartedAtMs)
{
    public override AgentApprovalKind Kind => AgentApprovalKind.Permissions;

    public override string ToString()
        => nameof(AgentPermissionsApprovalRequestedEvent)
            + AgentEventDiagnosticFormatting.Approval(Kind, Request is not null);
}

public sealed record AgentCadApprovalRequestedEvent(CadApprovalRequest Request)
    : AgentApprovalRequestedEvent(Request.ThreadId, Request.TurnId, Request.ApprovalId, 0)
{
    public override AgentApprovalKind Kind => AgentApprovalKind.Cad;

    public override string ToString()
        => nameof(AgentCadApprovalRequestedEvent)
            + AgentEventDiagnosticFormatting.Approval(Kind, Request is not null);
}

internal static class AgentEventDiagnosticFormatting
{
    internal static string EventIdentity(string? threadId, string? turnId)
        => " { ThreadIdConfigured = "
            + AgentDiagnosticFormatting.Configured(threadId)
            + ", TurnIdConfigured = "
            + AgentDiagnosticFormatting.Configured(turnId);

    internal static string Approval(AgentApprovalKind kind, bool requestPresent)
        => " { Kind = "
            + kind
            + ", RequestPresent = "
            + requestPresent
            + " }";
}

public sealed class AgentEventProjectionFailedEventArgs : EventArgs
{
    public AgentEventProjectionFailedEventArgs(string method, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(exception);

        DiagnosticClassification = DiagnosticDataClassification.RemoteError;
        var methodDiagnostic = DiagnosticSanitizer.SanitizeText(
            DiagnosticClassification,
            method);
        var exceptionDiagnostic = DiagnosticSanitizer.SanitizeException(
            DiagnosticClassification,
            exception);
        Method = methodDiagnostic.SafeText;
        DiagnosticRedactions = methodDiagnostic.Redactions | exceptionDiagnostic.Redactions;
        Exception = new AgentEventProjectionException(Method, "Projection failed.");
    }

    public string Method { get; }

    /// <summary>
    /// Compatibility projection containing a new fixed-message exception without the source
    /// exception, stack trace, data dictionary, or inner exception graph.
    /// </summary>
    public Exception Exception { get; }

    public DiagnosticDataClassification DiagnosticClassification { get; }

    public DiagnosticRedactionKinds DiagnosticRedactions { get; }
}

public sealed class AgentEventObserverFailedEventArgs : EventArgs
{
    public AgentEventObserverFailedEventArgs(AgentEvent agentEvent, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        ArgumentNullException.ThrowIfNull(exception);

        AgentEvent = new AgentEventDiagnosticSnapshot(agentEvent.GetType().Name);
        DiagnosticClassification = DiagnosticDataClassification.Exception;
        var diagnostic = DiagnosticSanitizer.SanitizeException(
            DiagnosticClassification,
            exception);
        DiagnosticRedactions = diagnostic.Redactions;
        Exception = new InvalidOperationException("Agent event observer failed.");
    }

    public AgentEvent AgentEvent { get; }

    /// <summary>
    /// Compatibility projection containing a new fixed-message exception without the source
    /// exception, stack trace, data dictionary, or inner exception graph.
    /// </summary>
    public Exception Exception { get; }

    public DiagnosticDataClassification DiagnosticClassification { get; }

    public DiagnosticRedactionKinds DiagnosticRedactions { get; }
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

/// <summary>
/// M4.1 策略拒绝。只公开稳定错误码，绝不携带被拒绝的原始模型字符串、思考强度、
/// 配置路径或任何请求正文，避免越界输入经诊断通道回流。
/// </summary>
public sealed class AgentPolicyViolationException : Exception
{
    public AgentPolicyViolationException(string errorCode)
        : base("Agent policy rejected the requested configuration.")
    {
        ErrorCode = errorCode;
    }

    /// <summary>来自 <see cref="Codex.AutoCAD.Contracts.AgentPolicyErrorCodes"/> 的稳定闭集值。</summary>
    public string ErrorCode { get; }

    public override string ToString()
        => nameof(AgentPolicyViolationException) + " { ErrorCode = " + ErrorCode + " }";
}
