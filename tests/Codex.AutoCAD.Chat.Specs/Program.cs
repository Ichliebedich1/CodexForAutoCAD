using Codex.AutoCAD.Host.Chat;

var specifications = new (string Name, Action Run)[]
{
    ("流式增量按序拼接并完成", StreamingMessageCompletes),
    ("审批等待与解析同步工具状态", ApprovalWaitAndResolution),
    ("失败和取消收敛活动状态", FailureAndCancellationConverge),
    ("重复与迟到事件不会二次应用", DuplicateAndStaleEventsAreIgnored),
    ("CAD 审批拒绝会话级授权", CadApprovalRejectsSessionGrant),
    ("审批响应竞态不复活终止工具", ApprovalRaceDoesNotReviveTerminalTool),
    ("并发重放同一事件只应用一次", ConcurrentReplayIsIdempotent),
    ("未知审批枚举默认拒绝", UnknownApprovalEnumsAreRejected),
    ("观察者异常不回滚已提交状态", ObserverFailureIsIsolated),
};

var failed = 0;
foreach (var specification in specifications)
{
    try
    {
        specification.Run();
        Console.WriteLine($"PASS {specification.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {specification.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{specifications.Length - failed}/{specifications.Length} specs passed");
return failed == 0 ? 0 : 1;

static void StreamingMessageCompletes()
{
    var state = new ChatSessionState("thread-stream");
    state.Apply(new AgentTurnStartedEvent("e-1", 1, state.ThreadId, "turn-1", At(1)));
    state.Apply(new AgentAssistantMessageStartedEvent(
        "e-2", 2, state.ThreadId, "turn-1", "message-1", At(2)));
    state.Apply(new AgentAssistantMessageDeltaEvent(
        "e-3", 3, state.ThreadId, "turn-1", "message-1", "Hello ", At(3)));
    state.Apply(new AgentAssistantMessageDeltaEvent(
        "e-4", 4, state.ThreadId, "turn-1", "message-1", "AutoCAD", At(4)));
    state.Apply(new AgentAssistantMessageCompletedEvent(
        "e-5", 5, state.ThreadId, "turn-1", "message-1", At(5)));
    state.Apply(new AgentTurnCompletedEvent("e-6", 6, state.ThreadId, "turn-1", At(6)));

    var snapshot = state.GetSnapshot();
    Equal(ChatSessionStatus.Completed, snapshot.Status);
    Equal(1, snapshot.Messages.Count);
    Equal("Hello AutoCAD", snapshot.Messages[0].Content);
    Equal(ChatMessageStatus.Completed, snapshot.Messages[0].Status);
    Equal(6L, snapshot.Version);
}

static void ApprovalWaitAndResolution()
{
    var state = new ChatSessionState("thread-approval");
    state.Apply(new AgentTurnStartedEvent("e-1", 1, state.ThreadId, "turn-1", At(1)));
    state.Apply(new AgentToolStartedEvent(
        "e-2", 2, state.ThreadId, "turn-1", "tool-1", "shell", "command", "Inspect command", At(2)));
    state.Apply(new AgentApprovalRequestedEvent(
        "e-3",
        3,
        state.ThreadId,
        "turn-1",
        "approval-1",
        ApprovalCardKind.Command,
        "Confirm command",
        "Exact command and working directory",
        ApprovalRiskLevel.High,
        new[] { ApprovalDecisionKind.AcceptOnce, ApprovalDecisionKind.DeclineAndContinue },
        At(3),
        At(30),
        "tool-1"));

    var waiting = state.GetSnapshot();
    Equal(ChatSessionStatus.WaitingForApproval, waiting.Status);
    Equal(ToolTimelineStatus.WaitingForApproval, waiting.ToolTimeline[0].Status);
    Equal(ApprovalCardStatus.Pending, waiting.ApprovalCards[0].Status);

    state.Apply(new AgentApprovalResolvedEvent(
        "e-4",
        4,
        state.ThreadId,
        "turn-1",
        "approval-1",
        ApprovalDecisionKind.AcceptOnce,
        At(4)));

    var resolved = state.GetSnapshot();
    Equal(ChatSessionStatus.Running, resolved.Status);
    Equal(ToolTimelineStatus.Running, resolved.ToolTimeline[0].Status);
    Equal(ApprovalCardStatus.Accepted, resolved.ApprovalCards[0].Status);
    Equal(ApprovalDecisionKind.AcceptOnce, resolved.ApprovalCards[0].Decision);
}

static void FailureAndCancellationConverge()
{
    var state = new ChatSessionState("thread-terminal");
    state.Apply(new AgentAssistantMessageDeltaEvent(
        "e-1", 1, state.ThreadId, "turn-failed", "message-1", "partial", At(1)));
    state.Apply(new AgentToolStartedEvent(
        "e-2", 2, state.ThreadId, "turn-failed", "tool-1", "query", "read", "Read context", At(2)));
    state.Apply(new AgentTurnFailedEvent(
        "e-3", 3, state.ThreadId, "turn-failed", "Bridge disconnected", At(3)));

    var failed = state.GetSnapshot();
    Equal(ChatSessionStatus.Failed, failed.Status);
    Equal(ChatMessageStatus.Failed, failed.Messages[0].Status);
    Equal(ToolTimelineStatus.Failed, failed.ToolTimeline[0].Status);

    state.Apply(new AgentTurnStartedEvent("e-4", 4, state.ThreadId, "turn-cancelled", At(4)));
    state.Apply(new AgentAssistantMessageStartedEvent(
        "e-5", 5, state.ThreadId, "turn-cancelled", "message-2", At(5)));
    state.Apply(new AgentTurnCancelledEvent(
        "e-6", 6, state.ThreadId, "turn-cancelled", At(6), "User cancelled"));

    var cancelled = state.GetSnapshot();
    Equal(ChatSessionStatus.Cancelled, cancelled.Status);
    Equal(ChatMessageStatus.Cancelled, cancelled.Messages[1].Status);
    Equal("User cancelled", cancelled.Error);
}

static void DuplicateAndStaleEventsAreIgnored()
{
    var state = new ChatSessionState("thread-ordering");
    var started = new AgentTurnStartedEvent("e-2", 2, state.ThreadId, "turn-1", At(2));
    Equal(AgentEventApplyStatus.Applied, state.Apply(started).Status);
    Equal(AgentEventApplyStatus.Duplicate, state.Apply(started).Status);
    Equal(
        AgentEventApplyStatus.Stale,
        state.Apply(new AgentTurnStartedEvent("e-1", 1, state.ThreadId, "turn-1", At(1))).Status);
    Equal(1L, state.GetSnapshot().Version);

    Throws<InvalidOperationException>(() =>
        state.Apply(new AgentTurnStartedEvent("wrong-thread", 3, "another-thread", "turn-1", At(3))));
    Equal(1L, state.GetSnapshot().Version);
}

static void CadApprovalRejectsSessionGrant()
{
    var state = new ChatSessionState("thread-cad");
    state.Apply(new AgentTurnStartedEvent("e-1", 1, state.ThreadId, "turn-1", At(1)));
    var version = state.GetSnapshot().Version;

    Throws<InvalidOperationException>(() => state.Apply(new AgentApprovalRequestedEvent(
        "e-2",
        2,
        state.ThreadId,
        "turn-1",
        "approval-cad",
        ApprovalCardKind.Cad,
        "Commit drawing change",
        "Create one line",
        ApprovalRiskLevel.High,
        new[] { ApprovalDecisionKind.AcceptOnce, ApprovalDecisionKind.AcceptForSession },
        At(2))));

    Equal(version, state.GetSnapshot().Version);
    Equal(1L, state.GetSnapshot().LastAppliedSequence);
    state.Apply(new AgentTurnCancelledEvent("e-3", 2, state.ThreadId, "turn-1", At(3)));
}

static void ApprovalRaceDoesNotReviveTerminalTool()
{
    var state = new ChatSessionState("thread-approval-race");
    state.Apply(new AgentTurnStartedEvent("e-1", 1, state.ThreadId, "turn-1", At(1)));
    state.Apply(new AgentToolStartedEvent(
        "e-2", 2, state.ThreadId, "turn-1", "tool-1", "shell", "command", "Run command", At(2)));
    state.Apply(new AgentApprovalRequestedEvent(
        "e-3",
        3,
        state.ThreadId,
        "turn-1",
        "approval-1",
        ApprovalCardKind.Command,
        "Confirm command",
        "Exact command",
        ApprovalRiskLevel.High,
        new[] { ApprovalDecisionKind.AcceptOnce, ApprovalDecisionKind.DeclineAndContinue },
        At(3),
        toolItemId: "tool-1"));

    state.Apply(new AgentToolFailedEvent(
        "e-4", 4, state.ThreadId, "turn-1", "tool-1", "Transport closed", At(4)));
    state.Apply(new AgentApprovalResolvedEvent(
        "e-5",
        5,
        state.ThreadId,
        "turn-1",
        "approval-1",
        ApprovalDecisionKind.AcceptOnce,
        At(5)));

    var snapshot = state.GetSnapshot();
    Equal(ApprovalCardStatus.Accepted, snapshot.ApprovalCards[0].Status);
    Equal(ToolTimelineStatus.Failed, snapshot.ToolTimeline[0].Status);
    Equal("Transport closed", snapshot.ToolTimeline[0].Error);
}

static void ConcurrentReplayIsIdempotent()
{
    var state = new ChatSessionState("thread-concurrent");
    var started = new AgentTurnStartedEvent("same-event", 1, state.ThreadId, "turn-1", At(1));
    var statuses = new AgentEventApplyStatus[1024];

    Parallel.For(0, statuses.Length, index =>
    {
        statuses[index] = state.Apply(started).Status;
        _ = state.GetSnapshot();
    });

    Equal(1, statuses.Count(status => status == AgentEventApplyStatus.Applied));
    Equal(1023, statuses.Count(status => status == AgentEventApplyStatus.Duplicate));
    Equal(1L, state.GetSnapshot().Version);
}

static void UnknownApprovalEnumsAreRejected()
{
    Throws<ArgumentOutOfRangeException>(() => new AgentApprovalRequestedEvent(
        "unknown-kind",
        1,
        "thread-1",
        "turn-1",
        "approval-1",
        (ApprovalCardKind)999,
        "Unknown",
        "Unknown approval kind",
        ApprovalRiskLevel.High,
        new[] { ApprovalDecisionKind.AcceptOnce },
        At(1)));

    Throws<ArgumentOutOfRangeException>(() => new AgentApprovalRequestedEvent(
        "unknown-decision",
        1,
        "thread-1",
        "turn-1",
        "approval-1",
        ApprovalCardKind.Cad,
        "Unknown",
        "Unknown decision",
        ApprovalRiskLevel.High,
        new[] { (ApprovalDecisionKind)999 },
        At(1)));
}

static void ObserverFailureIsIsolated()
{
    var state = new ChatSessionState("thread-observer");
    var diagnosticCount = 0;
    state.Changed += (_, _) => throw new InvalidOperationException("UI renderer failed");
    state.ObserverFailed += (_, args) =>
    {
        Equal("UI renderer failed", args.Exception.Message);
        diagnosticCount++;
    };

    var result = state.Apply(new AgentTurnStartedEvent(
        "observer-event", 1, state.ThreadId, "turn-1", At(1)));

    Equal(AgentEventApplyStatus.Applied, result.Status);
    Equal(1L, state.GetSnapshot().Version);
    Equal(1, diagnosticCount);
}

static DateTimeOffset At(int second)
    => new(2026, 7, 18, 0, 0, second, TimeSpan.Zero);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected exception '{typeof(TException).Name}'.");
}
