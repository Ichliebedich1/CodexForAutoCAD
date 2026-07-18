namespace Codex.AutoCAD.Contracts;

/// <summary>固定的 AgentHost ↔ AutoCAD 消息白名单；不允许任意命令名穿透到 CAD API。</summary>
public static class AgentBridgeMethods
{
    public const string StartThread = "agent.thread.start";
    public const string StartTurn = "agent.turn.start";
    public const string InterruptTurn = "agent.turn.interrupt";
    public const string ResolveApproval = "agent.approval.resolve";
    public const string ProposeLine = "cad.line.propose";
    public const string EventNotification = "agent.event";
}

public static class AgentBridgeEventKinds
{
    public const string ThreadStarted = "thread.started";
    public const string TurnStarted = "turn.started";
    public const string UserMessage = "message.user";
    public const string AssistantMessageStarted = "message.assistant.started";
    public const string AssistantMessageDelta = "message.assistant.delta";
    public const string AssistantMessageCompleted = "message.assistant.completed";
    public const string ToolStarted = "tool.started";
    public const string ToolProgress = "tool.progress";
    public const string ToolCompleted = "tool.completed";
    public const string ToolFailed = "tool.failed";
    public const string ApprovalRequested = "approval.requested";
    public const string ApprovalResolved = "approval.resolved";
    public const string TurnCompleted = "turn.completed";
    public const string TurnFailed = "turn.failed";
    public const string TurnCancelled = "turn.cancelled";
}

public sealed class AgentThreadStartRequest
{
    public string ConversationId { get; set; } = string.Empty;
}

public sealed class AgentThreadStartResponse
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class AgentTurnStartRequest
{
    public string ThreadId { get; set; } = string.Empty;

    public string ClientTurnId { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public CadContextEnvelope? Context { get; set; }
}

public sealed class AgentTurnStartResponse
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;
}

public sealed class AgentTurnInterruptRequest
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;
}

public sealed class AgentApprovalResolveRequest
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ApprovalId { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;
}

/// <summary>
/// 进程间的展示事件 DTO。它只承载 UI 数据；不能包含可执行委托、命令字符串或任意 CAD API 名称。
/// </summary>
public sealed class AgentBridgeEvent
{
    public string Kind { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public long Sequence { get; set; }

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string MessageId { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Delta { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;

    public string ApprovalId { get; set; } = string.Empty;

    public string ApprovalKind { get; set; } = string.Empty;

    public string Risk { get; set; } = string.Empty;

    public string[] AllowedDecisions { get; set; } = new string[0];

    public string Decision { get; set; } = string.Empty;

    public string OccurredAtUtc { get; set; } = string.Empty;

    public string ExpiresAtUtc { get; set; } = string.Empty;
}

/// <summary>
/// 模型动态工具能提出的最小 CAD 写请求。文档指纹、修订号、选择快照、计划哈希和审批令牌
/// 均由 AutoCAD 受信端补齐，模型不能提供或覆盖。
/// </summary>
public sealed class CadLineProposalRequest
{
    public string ProposalId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ToolCallId { get; set; } = string.Empty;

    public CadPoint3 Start { get; set; } = new();

    public CadPoint3 End { get; set; } = new();

    public string Layer { get; set; } = "current";
}

public sealed class CadLineProposalResponse
{
    public bool Success { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string CreatedHandle { get; set; } = string.Empty;
}

public static class AgentBridgeContractValidator
{
    private const int MaximumIdentifierLength = 256;
    private const int MaximumPromptLength = 128 * 1024;
    private const double MaximumCoordinateMagnitude = 1_000_000_000d;

    public static CadValidationFailure[] Validate(AgentTurnStartRequest? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure("turn_request_required", "$", "回合请求不能为空。")];
        }

        RequireIdentifier(request.ThreadId, "thread_id", "$.threadId", failures);
        RequireIdentifier(request.ClientTurnId, "client_turn_id", "$.clientTurnId", failures);
        Require(!string.IsNullOrWhiteSpace(request.Prompt) && request.Prompt.Length <= MaximumPromptLength,
            failures, "prompt_length", "$.prompt", "提示词不能为空且不能超过安全长度。");
        if (request.Context is not null)
        {
            Require(request.Context.ProtocolVersion == ProtocolConstants.CurrentVersion,
                failures, "context_protocol", "$.context.protocolVersion", "上下文协议版本不受支持。");
            var contextEntities = request.Context.Selection?.Entities ?? new CadEntityRef[0];
            Require(contextEntities.Length <= ProtocolConstants.MaximumContextEntities,
                failures, "context_entity_limit", "$.context.selection.entities", "上下文图元数量超过上限。");
        }

        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(CadLineProposalRequest? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure("proposal_required", "$", "直线提案不能为空。")];
        }

        RequireIdentifier(request.ProposalId, "proposal_id", "$.proposalId", failures);
        RequireIdentifier(request.ThreadId, "thread_id", "$.threadId", failures);
        RequireIdentifier(request.TurnId, "turn_id", "$.turnId", failures);
        RequireIdentifier(request.ToolCallId, "tool_call_id", "$.toolCallId", failures);
        Require(IsBoundedPoint(request.Start), failures,
            "start_coordinate", "$.start", "起点必须是安全范围内的有限坐标。");
        Require(IsBoundedPoint(request.End), failures,
            "end_coordinate", "$.end", "终点必须是安全范围内的有限坐标。");
        if (request.Start is not null
            && request.End is not null
            && request.Start.IsFinite
            && request.End.IsFinite)
        {
            var dx = request.Start.X - request.End.X;
            var dy = request.Start.Y - request.End.Y;
            var dz = request.Start.Z - request.End.Z;
            Require((dx * dx) + (dy * dy) + (dz * dz) > 1e-20d, failures,
                "line_zero_length", "$", "不能提出零长度直线。");
        }

        Require(!string.IsNullOrWhiteSpace(request.Layer)
                && request.Layer.Length <= 255
                && request.Layer.All(static character => !char.IsControl(character)),
            failures, "layer_invalid", "$.layer", "图层提示无效。");
        return failures.ToArray();
    }

    private static bool IsBoundedPoint(CadPoint3 point)
    {
        return point is not null
            && point.IsFinite
            && Math.Abs(point.X) <= MaximumCoordinateMagnitude
            && Math.Abs(point.Y) <= MaximumCoordinateMagnitude
            && Math.Abs(point.Z) <= MaximumCoordinateMagnitude;
    }

    private static void RequireIdentifier(
        string? value,
        string code,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(value is not null
                && !string.IsNullOrWhiteSpace(value)
                && value.Length <= MaximumIdentifierLength,
            failures, code, path, "标识不能为空且不能超过安全长度。");
    }

    private static void Require(
        bool condition,
        ICollection<CadValidationFailure> failures,
        string code,
        string path,
        string message)
    {
        if (!condition)
        {
            failures.Add(new CadValidationFailure(code, path, message));
        }
    }
}
