using System.Diagnostics;
using System.Text;

namespace Codex.AutoCAD.Host.Chat;

public enum AgentEventApplyStatus
{
    Applied,
    Duplicate,
    Stale,
}

public sealed record AgentEventApplyResult(
    AgentEventApplyStatus Status,
    long Version,
    long LastAppliedSequence);

public sealed class ChatSessionChangedEventArgs(
    AgentEvent appliedEvent,
    ChatSessionSnapshot snapshot) : EventArgs
{
    public AgentEvent AppliedEvent { get; } = appliedEvent;

    public ChatSessionSnapshot Snapshot { get; } = snapshot;
}

public sealed class ChatSessionObserverFailedEventArgs(
    Delegate observer,
    Exception exception) : EventArgs
{
    public Delegate Observer { get; } = observer;

    public Exception Exception { get; } = exception;
}

/// <summary>
/// Thread-safe reducer for the normalized Agent event stream. All mutations happen under one lock
/// and observers receive an immutable point-in-time snapshot after the lock is released.
/// </summary>
public sealed class ChatSessionState
{
    private const int MaximumAppliedEvents = 100_000;
    private const int MaximumMessages = 10_000;
    private const int MaximumToolItems = 10_000;
    private const int MaximumApprovalCards = 10_000;
    private const int MaximumAssistantCharacters = 2_000_000;
    private static readonly long DeltaNotificationIntervalTicks = Math.Max(1, Stopwatch.Frequency / 20);

    private readonly object _gate = new();
    private readonly HashSet<string> _appliedEventIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChatMessage> _messages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StringBuilder> _assistantBuffers = new(StringComparer.Ordinal);
    private readonly List<string> _messageOrder = new();
    private readonly Dictionary<string, ToolTimelineItem> _tools = new(StringComparer.Ordinal);
    private readonly List<string> _toolOrder = new();
    private readonly Dictionary<string, ApprovalCardModel> _approvals = new(StringComparer.Ordinal);
    private readonly List<string> _approvalOrder = new();
    private readonly List<ContextChip> _contextChips = new();

    private string? _currentTurnId;
    private ChatSessionStatus _status = ChatSessionStatus.Idle;
    private long _version;
    private long _lastAppliedSequence = -1;
    private string? _error;
    private long _lastDeltaNotificationTimestamp;

    public ChatSessionState(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ThreadId = threadId;
    }

    public event EventHandler<ChatSessionChangedEventArgs>? Changed;

    public event EventHandler<ChatSessionObserverFailedEventArgs>? ObserverFailed;

    public string ThreadId { get; }

    public ChatSessionSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return CreateSnapshotUnsafe();
        }
    }

    /// <summary>
    /// Atomically applies one event. Stable event ids make retries idempotent; an event whose
    /// sequence is not newer than the last applied event is ignored as stale.
    /// </summary>
    public AgentEventApplyResult Apply(AgentEvent agentEvent)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);

        ChatSessionChangedEventArgs? changed = null;
        AgentEventApplyResult result;
        lock (_gate)
        {
            if (!StringComparer.Ordinal.Equals(agentEvent.ThreadId, ThreadId))
            {
                throw new InvalidOperationException(
                    $"Agent event belongs to thread '{agentEvent.ThreadId}', not '{ThreadId}'.");
            }

            if (_appliedEventIds.Contains(agentEvent.EventId))
            {
                return new AgentEventApplyResult(
                    AgentEventApplyStatus.Duplicate,
                    _version,
                    _lastAppliedSequence);
            }

            if (agentEvent.Sequence <= _lastAppliedSequence)
            {
                return new AgentEventApplyResult(
                    AgentEventApplyStatus.Stale,
                    _version,
                    _lastAppliedSequence);
            }

            if (_appliedEventIds.Count >= MaximumAppliedEvents)
            {
                throw new InvalidOperationException("Chat event quota exceeded; start a new conversation.");
            }

            ApplyCoreUnsafe(agentEvent);
            _appliedEventIds.Add(agentEvent.EventId);
            _lastAppliedSequence = agentEvent.Sequence;
            _version++;

            if (ShouldPublishSnapshotUnsafe(agentEvent))
            {
                var snapshot = CreateSnapshotUnsafe();
                changed = new ChatSessionChangedEventArgs(agentEvent, snapshot);
            }
            result = new AgentEventApplyResult(
                AgentEventApplyStatus.Applied,
                _version,
                _lastAppliedSequence);
        }

        if (changed is not null)
        {
            PublishChanged(changed);
        }

        return result;
    }

    private void ApplyCoreUnsafe(AgentEvent agentEvent)
    {
        switch (agentEvent)
        {
            case AgentContextChangedEvent contextChanged:
                ApplyContextChangedUnsafe(contextChanged);
                break;
            case AgentTurnStartedEvent turnStarted:
                EnsureTurnOpenUnsafe(RequireTurnId(turnStarted));
                break;
            case AgentUserMessageAddedEvent userMessage:
                ApplyUserMessageUnsafe(userMessage);
                break;
            case AgentAssistantMessageStartedEvent messageStarted:
                ApplyAssistantStartedUnsafe(messageStarted);
                break;
            case AgentAssistantMessageDeltaEvent messageDelta:
                ApplyAssistantDeltaUnsafe(messageDelta);
                break;
            case AgentAssistantMessageCompletedEvent messageCompleted:
                ApplyAssistantCompletedUnsafe(messageCompleted);
                break;
            case AgentAssistantMessageFailedEvent messageFailed:
                ApplyAssistantFailedUnsafe(messageFailed);
                break;
            case AgentAssistantMessageCancelledEvent messageCancelled:
                ApplyAssistantCancelledUnsafe(messageCancelled);
                break;
            case AgentToolStartedEvent toolStarted:
                ApplyToolStartedUnsafe(toolStarted);
                break;
            case AgentToolProgressEvent toolProgress:
                ApplyToolProgressUnsafe(toolProgress);
                break;
            case AgentToolCompletedEvent toolCompleted:
                ApplyToolCompletedUnsafe(toolCompleted);
                break;
            case AgentToolFailedEvent toolFailed:
                ApplyToolFailedUnsafe(toolFailed);
                break;
            case AgentToolCancelledEvent toolCancelled:
                ApplyToolCancelledUnsafe(toolCancelled);
                break;
            case AgentApprovalRequestedEvent approvalRequested:
                ApplyApprovalRequestedUnsafe(approvalRequested);
                break;
            case AgentApprovalResolvedEvent approvalResolved:
                ApplyApprovalResolvedUnsafe(approvalResolved);
                break;
            case AgentApprovalExpiredEvent approvalExpired:
                ApplyApprovalExpiredUnsafe(approvalExpired);
                break;
            case AgentApprovalFailedEvent approvalFailed:
                ApplyApprovalFailedUnsafe(approvalFailed);
                break;
            case AgentTurnCompletedEvent turnCompleted:
                ApplyTurnCompletedUnsafe(turnCompleted);
                break;
            case AgentTurnFailedEvent turnFailed:
                ApplyTurnFailedUnsafe(turnFailed);
                break;
            case AgentTurnCancelledEvent turnCancelled:
                ApplyTurnCancelledUnsafe(turnCancelled);
                break;
            default:
                throw new NotSupportedException(
                    $"Unsupported normalized Agent event type '{agentEvent.GetType().FullName}'.");
        }
    }

    private void ApplyContextChangedUnsafe(AgentContextChangedEvent agentEvent)
    {
        var replacement = new List<ContextChip>(agentEvent.ContextChips.Count);
        var chipIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chip in agentEvent.ContextChips)
        {
            if (chip is null)
            {
                throw new InvalidOperationException("Context chip collection contains null.");
            }

            if (string.IsNullOrWhiteSpace(chip.ChipId) || string.IsNullOrWhiteSpace(chip.Label))
            {
                throw new InvalidOperationException("Context chip id and label are required.");
            }

            if (!chipIds.Add(chip.ChipId))
            {
                throw new InvalidOperationException($"Duplicate context chip id '{chip.ChipId}'.");
            }

            replacement.Add(chip);
        }

        _contextChips.Clear();
        _contextChips.AddRange(replacement);
    }

    private void ApplyUserMessageUnsafe(AgentUserMessageAddedEvent agentEvent)
    {
        EnsureCollectionQuota(_messages.Count, MaximumMessages, "message");
        EnsureMessageIdAvailable(agentEvent.MessageId);
        var turnId = RequireTurnId(agentEvent);
        EnsureTurnOpenUnsafe(turnId);

        _messages.Add(
            agentEvent.MessageId,
            new ChatMessage(
                agentEvent.MessageId,
                turnId,
                ChatMessageRole.User,
                agentEvent.Content,
                ChatMessageStatus.Completed,
                agentEvent.OccurredAt,
                agentEvent.OccurredAt));
        _messageOrder.Add(agentEvent.MessageId);
    }

    private void ApplyAssistantStartedUnsafe(AgentAssistantMessageStartedEvent agentEvent)
    {
        EnsureCollectionQuota(_messages.Count, MaximumMessages, "message");
        EnsureAssistantLength(agentEvent.InitialContent.Length);
        EnsureMessageIdAvailable(agentEvent.MessageId);
        var turnId = RequireTurnId(agentEvent);
        EnsureTurnOpenUnsafe(turnId);

        _messages.Add(
            agentEvent.MessageId,
            new ChatMessage(
                agentEvent.MessageId,
                turnId,
                ChatMessageRole.Assistant,
                agentEvent.InitialContent,
                ChatMessageStatus.Streaming,
                agentEvent.OccurredAt,
                agentEvent.OccurredAt));
        _messageOrder.Add(agentEvent.MessageId);
        _assistantBuffers.Add(agentEvent.MessageId, new StringBuilder(agentEvent.InitialContent));
    }

    private void ApplyAssistantDeltaUnsafe(AgentAssistantMessageDeltaEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        if (!_messages.TryGetValue(agentEvent.MessageId, out var message))
        {
            EnsureCollectionQuota(_messages.Count, MaximumMessages, "message");
            EnsureTurnOpenUnsafe(turnId);
            message = new ChatMessage(
                agentEvent.MessageId,
                turnId,
                ChatMessageRole.Assistant,
                string.Empty,
                ChatMessageStatus.Streaming,
                agentEvent.OccurredAt,
                agentEvent.OccurredAt);
            _messages.Add(agentEvent.MessageId, message);
            _messageOrder.Add(agentEvent.MessageId);
            _assistantBuffers.Add(agentEvent.MessageId, new StringBuilder());
        }
        else
        {
            ValidateStreamingAssistant(message, turnId);
            EnsureTurnOpenUnsafe(turnId);
        }

        var buffer = _assistantBuffers.TryGetValue(agentEvent.MessageId, out var existingBuffer)
            ? existingBuffer
            : (_assistantBuffers[agentEvent.MessageId] = new StringBuilder(message.Content));
        EnsureAssistantLength(checked(buffer.Length + agentEvent.Delta.Length));
        buffer.Append(agentEvent.Delta);
        _messages[agentEvent.MessageId] = message with
        {
            UpdatedAt = agentEvent.OccurredAt,
        };
    }

    private void ApplyAssistantCompletedUnsafe(AgentAssistantMessageCompletedEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        if (!_messages.TryGetValue(agentEvent.MessageId, out var message))
        {
            EnsureCollectionQuota(_messages.Count, MaximumMessages, "message");
            EnsureAssistantLength((agentEvent.FinalContent ?? string.Empty).Length);
            EnsureTurnOpenUnsafe(turnId);
            message = new ChatMessage(
                agentEvent.MessageId,
                turnId,
                ChatMessageRole.Assistant,
                agentEvent.FinalContent ?? string.Empty,
                ChatMessageStatus.Completed,
                agentEvent.OccurredAt,
                agentEvent.OccurredAt);
            _messages.Add(agentEvent.MessageId, message);
            _messageOrder.Add(agentEvent.MessageId);
            return;
        }

        ValidateStreamingAssistant(message, turnId);
        EnsureTurnOpenUnsafe(turnId);
        var completedContent = agentEvent.FinalContent
            ?? (_assistantBuffers.TryGetValue(agentEvent.MessageId, out var buffer)
                ? buffer.ToString()
                : message.Content);
        EnsureAssistantLength(completedContent.Length);
        _messages[agentEvent.MessageId] = message with
        {
            Content = completedContent,
            Status = ChatMessageStatus.Completed,
            UpdatedAt = agentEvent.OccurredAt,
            Error = null,
        };
        _assistantBuffers.Remove(agentEvent.MessageId);
    }

    private void ApplyAssistantFailedUnsafe(AgentAssistantMessageFailedEvent agentEvent)
    {
        ApplyAssistantTerminalUnsafe(
            agentEvent,
            agentEvent.MessageId,
            ChatMessageStatus.Failed,
            agentEvent.Error);
    }

    private void ApplyAssistantCancelledUnsafe(AgentAssistantMessageCancelledEvent agentEvent)
    {
        ApplyAssistantTerminalUnsafe(
            agentEvent,
            agentEvent.MessageId,
            ChatMessageStatus.Cancelled,
            agentEvent.Reason);
    }

    private void ApplyAssistantTerminalUnsafe(
        AgentEvent agentEvent,
        string messageId,
        ChatMessageStatus status,
        string? error)
    {
        var turnId = RequireTurnId(agentEvent);
        if (!_messages.TryGetValue(messageId, out var message))
        {
            EnsureCollectionQuota(_messages.Count, MaximumMessages, "message");
            EnsureTurnOpenUnsafe(turnId);
            _messages.Add(
                messageId,
                new ChatMessage(
                    messageId,
                    turnId,
                    ChatMessageRole.Assistant,
                    string.Empty,
                    status,
                    agentEvent.OccurredAt,
                    agentEvent.OccurredAt,
                    error));
            _messageOrder.Add(messageId);
            return;
        }

        ValidateStreamingAssistant(message, turnId);
        EnsureTurnOpenUnsafe(turnId);
        var terminalContent = _assistantBuffers.TryGetValue(messageId, out var terminalBuffer)
            ? terminalBuffer.ToString()
            : message.Content;
        _messages[messageId] = message with
        {
            Content = terminalContent,
            Status = status,
            UpdatedAt = agentEvent.OccurredAt,
            Error = error,
        };
        _assistantBuffers.Remove(messageId);
    }

    private void ApplyToolStartedUnsafe(AgentToolStartedEvent agentEvent)
    {
        EnsureCollectionQuota(_tools.Count, MaximumToolItems, "tool timeline item");
        if (_tools.ContainsKey(agentEvent.ItemId))
        {
            throw new InvalidOperationException($"Tool item '{agentEvent.ItemId}' already exists.");
        }

        var turnId = RequireTurnId(agentEvent);
        EnsureTurnOpenUnsafe(turnId);
        _tools.Add(
            agentEvent.ItemId,
            new ToolTimelineItem(
                agentEvent.ItemId,
                turnId,
                agentEvent.ToolName,
                agentEvent.Category,
                agentEvent.Summary,
                ToolTimelineStatus.Running,
                agentEvent.OccurredAt,
                agentEvent.OccurredAt));
        _toolOrder.Add(agentEvent.ItemId);
    }

    private void ApplyToolProgressUnsafe(AgentToolProgressEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        var item = GetActiveTool(agentEvent.ItemId, turnId, allowWaitingForApproval: true);
        EnsureTurnOpenUnsafe(turnId);
        _tools[agentEvent.ItemId] = item with
        {
            Summary = agentEvent.Summary ?? item.Summary,
            Details = agentEvent.Details ?? item.Details,
            UpdatedAt = agentEvent.OccurredAt,
        };
    }

    private void ApplyToolCompletedUnsafe(AgentToolCompletedEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        var item = GetActiveTool(agentEvent.ItemId, turnId, allowWaitingForApproval: false);
        EnsureTurnOpenUnsafe(turnId);
        _tools[agentEvent.ItemId] = item with
        {
            Summary = agentEvent.ResultSummary ?? item.Summary,
            Status = ToolTimelineStatus.Succeeded,
            UpdatedAt = agentEvent.OccurredAt,
            Error = null,
        };
    }

    private void ApplyToolFailedUnsafe(AgentToolFailedEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        var item = GetActiveTool(agentEvent.ItemId, turnId, allowWaitingForApproval: true);
        EnsureTurnOpenUnsafe(turnId);
        _tools[agentEvent.ItemId] = item with
        {
            Status = ToolTimelineStatus.Failed,
            UpdatedAt = agentEvent.OccurredAt,
            Error = agentEvent.Error,
        };
    }

    private void ApplyToolCancelledUnsafe(AgentToolCancelledEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        var item = GetActiveTool(agentEvent.ItemId, turnId, allowWaitingForApproval: true);
        EnsureTurnOpenUnsafe(turnId);
        _tools[agentEvent.ItemId] = item with
        {
            Status = ToolTimelineStatus.Cancelled,
            UpdatedAt = agentEvent.OccurredAt,
            Error = agentEvent.Reason,
        };
    }

    private void ApplyApprovalRequestedUnsafe(AgentApprovalRequestedEvent agentEvent)
    {
        EnsureCollectionQuota(_approvals.Count, MaximumApprovalCards, "approval card");
        if (_approvals.ContainsKey(agentEvent.ApprovalId))
        {
            throw new InvalidOperationException($"Approval '{agentEvent.ApprovalId}' already exists.");
        }

        if (agentEvent.Kind == ApprovalCardKind.Cad
            && agentEvent.AllowedDecisions.Contains(ApprovalDecisionKind.AcceptForSession))
        {
            throw new InvalidOperationException("CAD approvals cannot grant session-wide acceptance.");
        }

        ToolTimelineItem? tool = null;
        var turnId = RequireTurnId(agentEvent);
        if (agentEvent.ToolItemId is not null)
        {
            tool = GetActiveTool(agentEvent.ToolItemId, turnId, allowWaitingForApproval: false);
        }

        EnsureTurnOpenUnsafe(turnId);
        var card = new ApprovalCardModel(
            agentEvent.ApprovalId,
            turnId,
            agentEvent.Kind,
            agentEvent.Title,
            agentEvent.Summary,
            agentEvent.Risk,
            ApprovalCardStatus.Pending,
            ChatSessionSnapshot.ReadOnlyCopy(agentEvent.AllowedDecisions),
            agentEvent.OccurredAt,
            agentEvent.ExpiresAt,
            ToolItemId: agentEvent.ToolItemId);
        _approvals.Add(agentEvent.ApprovalId, card);
        _approvalOrder.Add(agentEvent.ApprovalId);

        if (tool is not null)
        {
            _tools[tool.ItemId] = tool with
            {
                Status = ToolTimelineStatus.WaitingForApproval,
                UpdatedAt = agentEvent.OccurredAt,
                ApprovalId = agentEvent.ApprovalId,
            };
        }

        _status = ChatSessionStatus.WaitingForApproval;
    }

    private void ApplyApprovalResolvedUnsafe(AgentApprovalResolvedEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        var card = GetPendingApproval(agentEvent.ApprovalId, turnId);
        if (!card.AllowedDecisions.Contains(agentEvent.Decision))
        {
            throw new InvalidOperationException(
                $"Decision '{agentEvent.Decision}' is not allowed for approval '{card.ApprovalId}'.");
        }

        EnsureTurnOpenUnsafe(turnId);
        var cardStatus = agentEvent.Decision switch
        {
            ApprovalDecisionKind.AcceptOnce or ApprovalDecisionKind.AcceptForSession
                => ApprovalCardStatus.Accepted,
            ApprovalDecisionKind.DeclineAndContinue or ApprovalDecisionKind.DeclineAndStop
                => ApprovalCardStatus.Declined,
            ApprovalDecisionKind.Cancel => ApprovalCardStatus.Cancelled,
            _ => throw new InvalidOperationException($"Unknown approval decision '{agentEvent.Decision}'."),
        };

        _approvals[agentEvent.ApprovalId] = card with
        {
            Status = cardStatus,
            Decision = agentEvent.Decision,
            ResolvedAt = agentEvent.OccurredAt,
            ResolutionDetail = agentEvent.Detail,
        };

        ResolveApprovalToolUnsafe(card, cardStatus, agentEvent.OccurredAt, agentEvent.Detail);
        RefreshWaitingStatusUnsafe();
    }

    private void ApplyApprovalExpiredUnsafe(AgentApprovalExpiredEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        var card = GetPendingApproval(agentEvent.ApprovalId, turnId);
        EnsureTurnOpenUnsafe(turnId);
        _approvals[agentEvent.ApprovalId] = card with
        {
            Status = ApprovalCardStatus.Expired,
            ResolvedAt = agentEvent.OccurredAt,
            ResolutionDetail = "Approval expired.",
        };
        ResolveApprovalToolUnsafe(
            card,
            ApprovalCardStatus.Expired,
            agentEvent.OccurredAt,
            "Approval expired.");
        RefreshWaitingStatusUnsafe();
    }

    private void ApplyApprovalFailedUnsafe(AgentApprovalFailedEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        var card = GetPendingApproval(agentEvent.ApprovalId, turnId);
        EnsureTurnOpenUnsafe(turnId);
        _approvals[agentEvent.ApprovalId] = card with
        {
            Status = ApprovalCardStatus.Failed,
            ResolvedAt = agentEvent.OccurredAt,
            ResolutionDetail = agentEvent.Error,
        };
        ResolveApprovalToolUnsafe(
            card,
            ApprovalCardStatus.Failed,
            agentEvent.OccurredAt,
            agentEvent.Error);
        RefreshWaitingStatusUnsafe();
    }

    private void ApplyTurnCompletedUnsafe(AgentTurnCompletedEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        EnsureTurnOpenUnsafe(turnId);
        FinalizeMessagesUnsafe(turnId, ChatMessageStatus.Completed, agentEvent.OccurredAt, null);
        FinalizeToolsUnsafe(
            turnId,
            ToolTimelineStatus.Cancelled,
            agentEvent.OccurredAt,
            "Turn completed before the tool reported a terminal state.");
        FinalizeApprovalsUnsafe(
            turnId,
            ApprovalCardStatus.Cancelled,
            agentEvent.OccurredAt,
            "Turn completed before the approval was resolved.");
        _status = ChatSessionStatus.Completed;
        _error = null;
    }

    private void ApplyTurnFailedUnsafe(AgentTurnFailedEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        EnsureTurnOpenUnsafe(turnId);
        FinalizeMessagesUnsafe(turnId, ChatMessageStatus.Failed, agentEvent.OccurredAt, agentEvent.Error);
        FinalizeToolsUnsafe(turnId, ToolTimelineStatus.Failed, agentEvent.OccurredAt, agentEvent.Error);
        FinalizeApprovalsUnsafe(turnId, ApprovalCardStatus.Failed, agentEvent.OccurredAt, agentEvent.Error);
        _status = ChatSessionStatus.Failed;
        _error = agentEvent.Error;
    }

    private void ApplyTurnCancelledUnsafe(AgentTurnCancelledEvent agentEvent)
    {
        var turnId = RequireTurnId(agentEvent);
        EnsureTurnOpenUnsafe(turnId);
        FinalizeMessagesUnsafe(
            turnId,
            ChatMessageStatus.Cancelled,
            agentEvent.OccurredAt,
            agentEvent.Reason);
        FinalizeToolsUnsafe(
            turnId,
            ToolTimelineStatus.Cancelled,
            agentEvent.OccurredAt,
            agentEvent.Reason);
        FinalizeApprovalsUnsafe(
            turnId,
            ApprovalCardStatus.Cancelled,
            agentEvent.OccurredAt,
            agentEvent.Reason);
        _status = ChatSessionStatus.Cancelled;
        _error = agentEvent.Reason;
    }

    private void FinalizeMessagesUnsafe(
        string turnId,
        ChatMessageStatus status,
        DateTimeOffset occurredAt,
        string? error)
    {
        foreach (var messageId in _messageOrder)
        {
            var message = _messages[messageId];
            if (message.TurnId == turnId
                && message.Role == ChatMessageRole.Assistant
                && message.Status == ChatMessageStatus.Streaming)
            {
                _messages[messageId] = message with
                {
                    Content = _assistantBuffers.TryGetValue(messageId, out var buffer)
                        ? buffer.ToString()
                        : message.Content,
                    Status = status,
                    UpdatedAt = occurredAt,
                    Error = error,
                };
                _assistantBuffers.Remove(messageId);
            }
        }
    }

    private void FinalizeToolsUnsafe(
        string turnId,
        ToolTimelineStatus status,
        DateTimeOffset occurredAt,
        string? error)
    {
        foreach (var itemId in _toolOrder)
        {
            var item = _tools[itemId];
            if (item.TurnId == turnId && !item.IsTerminal)
            {
                _tools[itemId] = item with
                {
                    Status = status,
                    UpdatedAt = occurredAt,
                    Error = error,
                };
            }
        }
    }

    private void FinalizeApprovalsUnsafe(
        string turnId,
        ApprovalCardStatus status,
        DateTimeOffset occurredAt,
        string? detail)
    {
        foreach (var approvalId in _approvalOrder)
        {
            var card = _approvals[approvalId];
            if (card.TurnId == turnId && card.IsPending)
            {
                _approvals[approvalId] = card with
                {
                    Status = status,
                    ResolvedAt = occurredAt,
                    ResolutionDetail = detail,
                };
            }
        }
    }

    private void ResolveApprovalToolUnsafe(
        ApprovalCardModel card,
        ApprovalCardStatus approvalStatus,
        DateTimeOffset occurredAt,
        string? detail)
    {
        if (card.ToolItemId is null || !_tools.TryGetValue(card.ToolItemId, out var tool))
        {
            return;
        }

        if (tool.Status != ToolTimelineStatus.WaitingForApproval)
        {
            // A tool failure/cancellation may race with the user's decision. The approval still
            // reaches a terminal state, but a terminal tool result must never be overwritten.
            return;
        }

        var toolStatus = approvalStatus switch
        {
            ApprovalCardStatus.Accepted => ToolTimelineStatus.Running,
            ApprovalCardStatus.Failed => ToolTimelineStatus.Failed,
            _ => ToolTimelineStatus.Cancelled,
        };
        _tools[tool.ItemId] = tool with
        {
            Status = toolStatus,
            UpdatedAt = occurredAt,
            Error = toolStatus == ToolTimelineStatus.Running ? null : detail,
        };
    }

    private ToolTimelineItem GetActiveTool(
        string itemId,
        string turnId,
        bool allowWaitingForApproval)
    {
        if (!_tools.TryGetValue(itemId, out var item))
        {
            throw new InvalidOperationException($"Unknown tool item '{itemId}'.");
        }

        if (!StringComparer.Ordinal.Equals(item.TurnId, turnId))
        {
            throw new InvalidOperationException(
                $"Tool item '{itemId}' belongs to turn '{item.TurnId}', not '{turnId}'.");
        }

        var active = item.Status == ToolTimelineStatus.Running
            || (allowWaitingForApproval && item.Status == ToolTimelineStatus.WaitingForApproval);
        if (!active)
        {
            throw new InvalidOperationException(
                $"Tool item '{itemId}' cannot transition from status '{item.Status}'.");
        }

        return item;
    }

    private ApprovalCardModel GetPendingApproval(string approvalId, string turnId)
    {
        if (!_approvals.TryGetValue(approvalId, out var card))
        {
            throw new InvalidOperationException($"Unknown approval '{approvalId}'.");
        }

        if (!StringComparer.Ordinal.Equals(card.TurnId, turnId))
        {
            throw new InvalidOperationException(
                $"Approval '{approvalId}' belongs to turn '{card.TurnId}', not '{turnId}'.");
        }

        if (!card.IsPending)
        {
            throw new InvalidOperationException(
                $"Approval '{approvalId}' is already in terminal status '{card.Status}'.");
        }

        return card;
    }

    private void RefreshWaitingStatusUnsafe()
    {
        _status = _approvals.Values.Any(static card => card.IsPending)
            ? ChatSessionStatus.WaitingForApproval
            : ChatSessionStatus.Running;
    }

    private void EnsureMessageIdAvailable(string messageId)
    {
        if (_messages.ContainsKey(messageId))
        {
            throw new InvalidOperationException($"Message '{messageId}' already exists.");
        }
    }

    private static void ValidateStreamingAssistant(ChatMessage message, string turnId)
    {
        if (!StringComparer.Ordinal.Equals(message.TurnId, turnId))
        {
            throw new InvalidOperationException(
                $"Message '{message.MessageId}' belongs to turn '{message.TurnId}', not '{turnId}'.");
        }

        if (message.Role != ChatMessageRole.Assistant
            || message.Status != ChatMessageStatus.Streaming)
        {
            throw new InvalidOperationException(
                $"Message '{message.MessageId}' cannot receive an assistant streaming transition from '{message.Status}'.");
        }
    }

    private void EnsureTurnOpenUnsafe(string turnId)
    {
        if (_currentTurnId is null)
        {
            _currentTurnId = turnId;
            _status = ChatSessionStatus.Running;
            _error = null;
            return;
        }

        if (!StringComparer.Ordinal.Equals(_currentTurnId, turnId))
        {
            if (_status is ChatSessionStatus.Running or ChatSessionStatus.WaitingForApproval)
            {
                throw new InvalidOperationException(
                    $"Turn '{_currentTurnId}' is still active; turn '{turnId}' cannot start yet.");
            }

            _currentTurnId = turnId;
            _status = ChatSessionStatus.Running;
            _error = null;
            return;
        }

        if (_status is ChatSessionStatus.Completed
            or ChatSessionStatus.Failed
            or ChatSessionStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"Turn '{turnId}' is already in terminal session status '{_status}'.");
        }

        if (_status == ChatSessionStatus.Idle)
        {
            _status = ChatSessionStatus.Running;
        }
    }

    private static string RequireTurnId(AgentEvent agentEvent)
        => agentEvent.TurnId
            ?? throw new InvalidOperationException(
                $"Agent event '{agentEvent.EventId}' requires a turn id.");

    private ChatSessionSnapshot CreateSnapshotUnsafe()
    {
        return new ChatSessionSnapshot(
            ThreadId,
            _currentTurnId,
            _status,
            _version,
            _lastAppliedSequence,
            ChatSessionSnapshot.ReadOnlyCopy(_messageOrder.Select(id =>
            {
                var message = _messages[id];
                return _assistantBuffers.TryGetValue(id, out var buffer)
                    ? message with { Content = buffer.ToString() }
                    : message;
            })),
            ChatSessionSnapshot.ReadOnlyCopy(_contextChips),
            ChatSessionSnapshot.ReadOnlyCopy(_toolOrder.Select(id => _tools[id])),
            ChatSessionSnapshot.ReadOnlyCopy(_approvalOrder.Select(id => _approvals[id])),
            _error);
    }

    private bool ShouldPublishSnapshotUnsafe(AgentEvent agentEvent)
    {
        if (agentEvent is not AgentAssistantMessageDeltaEvent)
        {
            return true;
        }

        var now = Stopwatch.GetTimestamp();
        if (now - _lastDeltaNotificationTimestamp < DeltaNotificationIntervalTicks)
        {
            return false;
        }

        _lastDeltaNotificationTimestamp = now;
        return true;
    }

    private void PublishChanged(ChatSessionChangedEventArgs changed)
    {
        if (Changed is null)
        {
            return;
        }

        foreach (EventHandler<ChatSessionChangedEventArgs> handler in Changed.GetInvocationList())
        {
            try
            {
                handler(this, changed);
            }
            catch (Exception exception)
            {
                PublishObserverFailure(handler, exception);
            }
        }
    }

    private void PublishObserverFailure(Delegate observer, Exception exception)
    {
        if (ObserverFailed is null)
        {
            return;
        }

        var args = new ChatSessionObserverFailedEventArgs(observer, exception);
        foreach (EventHandler<ChatSessionObserverFailedEventArgs> handler in ObserverFailed.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // 诊断观察者不能破坏已提交的 reducer 状态。
            }
        }
    }

    private static void EnsureCollectionQuota(int currentCount, int maximumCount, string label)
    {
        if (currentCount >= maximumCount)
        {
            throw new InvalidOperationException($"Chat {label} quota exceeded; start a new conversation.");
        }
    }

    private static void EnsureAssistantLength(int length)
    {
        if (length > MaximumAssistantCharacters)
        {
            throw new InvalidOperationException("Assistant message exceeds the bounded UI content limit.");
        }
    }
}
