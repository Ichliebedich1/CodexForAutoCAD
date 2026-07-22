namespace Codex.AutoCAD.Contracts;

public static class AgentBridgeContractConstants
{
    public const int CurrentVersion = 1;
    public const int MinimumCompatibleVersion = 1;
}

/// <summary>固定的 AgentHost ↔ AutoCAD 消息白名单；不允许任意命令名穿透到 CAD API。</summary>
public static class AgentBridgeMethods
{
    public const string GetCapabilities = "agent.capabilities.get";
    public const string StartThread = "agent.thread.start";
    public const string StartTurn = "agent.turn.start";
    public const string StartTurnV2 = "agent.turn.start.v2";
    public const string InterruptTurn = "agent.turn.interrupt";
    public const string ResolveApproval = "agent.approval.resolve";
    public const string ProposeLine = "cad.line.propose";
    public const string QueryDrawing = "cad.drawing.query";
    public const string EventNotification = "agent.event";
}

public static class AgentBridgeEventKinds
{
    public const string ConnectionStateChanged = "connection.changed";
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

public static class AgentBridgeConnectionStates
{
    public const string Offline = "offline";
    public const string Connecting = "connecting";
    public const string Online = "online";
    public const string Degraded = "degraded";
    public const string Closed = "closed";
}

public static class AgentBridgeApprovalDecisions
{
    public const string AllowOnce = "allow_once";
    public const string DeclineAndContinue = "decline_and_continue";
    public const string DeclineAndCancelTurn = "decline_and_cancel_turn";
}

public static class AgentBridgeErrorCodes
{
    public const string Offline = "offline";
    public const string ContractMismatch = "contract_mismatch";
    public const string AuthenticationFailed = "authentication_failed";
    public const string ReplayRejected = "replay_rejected";
    public const string RequestInvalid = "request_invalid";
    public const string ContextInvalid = "context_invalid";
    public const string ContextHashMismatch = "context_hash_mismatch";
    public const string AgentUnavailable = "agent_unavailable";
    public const string ConnectionLost = "connection_lost";
    public const string Timeout = "timeout";
    public const string Busy = "busy";
    public const string RequestCancelled = "request_cancelled";
    public const string TurnNotFound = "turn_not_found";
    public const string ApprovalInvalid = "approval_invalid";
    public const string ApprovalExpired = "approval_expired";
    public const string ApprovalAlreadyConsumed = "approval_already_consumed";
    public const string DrawingQueryUnavailable = "drawing_query_unavailable";
    public const string ResultIdentityMismatch = "result_identity_mismatch";
    public const string InternalError = "internal_error";
}

public sealed class AgentCapabilitiesRequest
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string ClientName { get; set; } = string.Empty;

    public string ClientVersion { get; set; } = string.Empty;

    public string HostTarget { get; set; } = string.Empty;
}

public sealed class AgentCapabilitiesResponse
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public int MinimumCompatibleVersion { get; set; } = AgentBridgeContractConstants.MinimumCompatibleVersion;

    public string AgentInstanceId { get; set; } = string.Empty;

    public string CadContextSchema { get; set; } = CadContextJsonV1Constants.Schema;

    public int CadContextSchemaVersion { get; set; } = CadContextJsonV1Constants.SchemaVersion;

    public string[] Methods { get; set; } = new string[0];

    public string[] EventKinds { get; set; } = new string[0];

    public string[] ApprovalDecisions { get; set; } = new string[0];

    /// <summary>
    /// Explicit list of CadContextJson schema/version pairs supported by this AgentHost.
    /// v1-only hosts return [{schema, 1}]; v2-capable hosts return both v1 and v2 entries.
    /// </summary>
    public CadContextSchemaVersionEntry[] SupportedCadContextSchemas { get; set; } =
    [
        new CadContextSchemaVersionEntry
        {
            Schema = CadContextJsonV1Constants.Schema,
            SchemaVersion = CadContextJsonV1Constants.SchemaVersion,
        },
    ];

    /// <summary>
    /// Descriptive capability only. A true value never authorizes a CAD write; preview, one-time
    /// approval, lock-time revalidation and a single transaction remain mandatory.
    /// </summary>
    public bool CadWriteAvailable { get; set; }
}

public sealed class CadContextSchemaVersionEntry
{
    public string Schema { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }
}

public sealed class AgentThreadStartRequest
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string ConversationId { get; set; } = string.Empty;
}

public sealed class AgentThreadStartResponse
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string ThreadId { get; set; } = string.Empty;
}

public sealed class AgentTurnStartRequest
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string ThreadId { get; set; } = string.Empty;

    public string ClientTurnId { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public CadContextJsonV1? Context { get; set; }

    /// <summary>Lower-case SHA-256 of canonical CadContextJson v1 bytes.</summary>
    public string ContextSha256 { get; set; } = string.Empty;
}

public sealed class AgentTurnStartResponse
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    /// <summary>Echo of the exact accepted CadContextJson v1 identity, or empty when no context exists.</summary>
    public string AcceptedContextSha256 { get; set; } = string.Empty;
}

public sealed class AgentTurnStartV2Request
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string ThreadId { get; set; } = string.Empty;

    public string ClientTurnId { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public CadContextJsonV2? ContextV2 { get; set; }

    /// <summary>Lower-case SHA-256 of canonical CadContextJson v2 bytes.</summary>
    public string ContextV2Sha256 { get; set; } = string.Empty;
}

public sealed class AgentTurnStartV2Response
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    /// <summary>Echo of the exact accepted CadContextJson v2 identity.</summary>
    public string AcceptedContextV2Sha256 { get; set; } = string.Empty;
}

public sealed class AgentTurnInterruptRequest
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;
}

public sealed class AgentApprovalResolveRequest
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

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
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

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

    public string ErrorCode { get; set; } = string.Empty;

    public bool Retryable { get; set; }

    public string ConnectionState { get; set; } = string.Empty;

    /// <summary>
    /// Exact context identity accepted for this turn. Assistant/tool/terminal events must retain it
    /// so the Host can reject results from another document or selection.
    /// </summary>
    public string ContextSha256 { get; set; } = string.Empty;

    public string ApprovalId { get; set; } = string.Empty;

    public string ApprovalKind { get; set; } = string.Empty;

    public string Risk { get; set; } = string.Empty;

    public string[] AllowedDecisions { get; set; } = new string[0];

    public string Decision { get; set; } = string.Empty;

    public string OccurredAtUtc { get; set; } = string.Empty;

    public string ExpiresAtUtc { get; set; } = string.Empty;
}

public sealed class AgentBridgeFailure
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// UI hint only. Even when true, the Host must never automatically retry a CAD write or reuse an
    /// approval. Retrying a read-only turn requires a new explicit client request.
    /// </summary>
    public bool Retryable { get; set; }

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string OccurredAtUtc { get; set; } = string.Empty;
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

/// <summary>
/// AgentHost发往受信AutoCAD Host的只读整图查询。索引、文档和修订身份由Host绑定，
/// 因此不能出现在这个请求中。
/// </summary>
public sealed class AgentDrawingQueryRequest
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string RequestId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ToolCallId { get; set; } = string.Empty;

    public string QueryId { get; set; } = string.Empty;

    public CadQueryFilter Filter { get; set; } = new();

    public int PageSize { get; set; } = DrawingIndexContractConstants.DefaultPageSize;

    public string Cursor { get; set; } = string.Empty;
}

/// <summary>
/// 受信AutoCAD Host返回的只读整图查询结果。外层身份必须逐项回显，内层CadQueryResponse
/// 则携带Host拥有的索引、文档和修订绑定。
/// </summary>
public sealed class AgentDrawingQueryResponse
{
    public int ContractVersion { get; set; } = AgentBridgeContractConstants.CurrentVersion;

    public string RequestId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ToolCallId { get; set; } = string.Empty;

    public string QueryId { get; set; } = string.Empty;

    public CadQueryResponse Query { get; set; } = new();
}

public static class AgentBridgeContractValidator
{
    private const int MaximumIdentifierLength = 256;
    private const int MaximumPromptLength = 128 * 1024;
    private const int MaximumDisplayTextLength = 128 * 1024;
    private const int MaximumErrorLength = 4 * 1024;
    private const double MaximumCoordinateMagnitude = 1_000_000_000d;

    private static readonly string[] KnownMethods =
    [
        AgentBridgeMethods.GetCapabilities,
        AgentBridgeMethods.StartThread,
        AgentBridgeMethods.StartTurn,
        AgentBridgeMethods.StartTurnV2,
        AgentBridgeMethods.InterruptTurn,
        AgentBridgeMethods.ResolveApproval,
        AgentBridgeMethods.ProposeLine,
        AgentBridgeMethods.QueryDrawing,
        AgentBridgeMethods.EventNotification,
    ];

    private static readonly string[] KnownEventKinds =
    [
        AgentBridgeEventKinds.ConnectionStateChanged,
        AgentBridgeEventKinds.ThreadStarted,
        AgentBridgeEventKinds.TurnStarted,
        AgentBridgeEventKinds.UserMessage,
        AgentBridgeEventKinds.AssistantMessageStarted,
        AgentBridgeEventKinds.AssistantMessageDelta,
        AgentBridgeEventKinds.AssistantMessageCompleted,
        AgentBridgeEventKinds.ToolStarted,
        AgentBridgeEventKinds.ToolProgress,
        AgentBridgeEventKinds.ToolCompleted,
        AgentBridgeEventKinds.ToolFailed,
        AgentBridgeEventKinds.ApprovalRequested,
        AgentBridgeEventKinds.ApprovalResolved,
        AgentBridgeEventKinds.TurnCompleted,
        AgentBridgeEventKinds.TurnFailed,
        AgentBridgeEventKinds.TurnCancelled,
    ];

    private static readonly string[] KnownApprovalDecisions =
    [
        AgentBridgeApprovalDecisions.AllowOnce,
        AgentBridgeApprovalDecisions.DeclineAndContinue,
        AgentBridgeApprovalDecisions.DeclineAndCancelTurn,
    ];

    private static readonly string[] KnownConnectionStates =
    [
        AgentBridgeConnectionStates.Offline,
        AgentBridgeConnectionStates.Connecting,
        AgentBridgeConnectionStates.Online,
        AgentBridgeConnectionStates.Degraded,
        AgentBridgeConnectionStates.Closed,
    ];

    private static readonly string[] KnownErrorCodes =
    [
        AgentBridgeErrorCodes.Offline,
        AgentBridgeErrorCodes.ContractMismatch,
        AgentBridgeErrorCodes.AuthenticationFailed,
        AgentBridgeErrorCodes.ReplayRejected,
        AgentBridgeErrorCodes.RequestInvalid,
        AgentBridgeErrorCodes.ContextInvalid,
        AgentBridgeErrorCodes.ContextHashMismatch,
        AgentBridgeErrorCodes.AgentUnavailable,
        AgentBridgeErrorCodes.ConnectionLost,
        AgentBridgeErrorCodes.Timeout,
        AgentBridgeErrorCodes.Busy,
        AgentBridgeErrorCodes.RequestCancelled,
        AgentBridgeErrorCodes.TurnNotFound,
        AgentBridgeErrorCodes.ApprovalInvalid,
        AgentBridgeErrorCodes.ApprovalExpired,
        AgentBridgeErrorCodes.ApprovalAlreadyConsumed,
        AgentBridgeErrorCodes.DrawingQueryUnavailable,
        AgentBridgeErrorCodes.ResultIdentityMismatch,
        AgentBridgeErrorCodes.InternalError,
    ];

    public static CadValidationFailure[] Validate(AgentCapabilitiesRequest? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure(
                "capabilities_request_required", "$", "能力协商请求不能为空。")];
        }

        ValidateContractVersion(request.ContractVersion, "$.contractVersion", failures);
        RequireIdentifier(request.ClientName, "client_name", "$.clientName", failures);
        RequireIdentifier(request.ClientVersion, "client_version", "$.clientVersion", failures);
        RequireIdentifier(request.HostTarget, "host_target", "$.hostTarget", failures);
        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(AgentCapabilitiesResponse? response)
    {
        var failures = new List<CadValidationFailure>();
        if (response is null)
        {
            return [new CadValidationFailure(
                "capabilities_response_required", "$", "能力协商响应不能为空。")];
        }

        ValidateContractVersion(response.ContractVersion, "$.contractVersion", failures);
        Require(response.MinimumCompatibleVersion == AgentBridgeContractConstants.MinimumCompatibleVersion,
            failures, "minimum_contract_version", "$.minimumCompatibleVersion",
            "最小兼容契约版本不受支持。" );
        RequireIdentifier(response.AgentInstanceId, "agent_instance_id", "$.agentInstanceId", failures);
        Require(string.Equals(response.CadContextSchema, CadContextJsonV1Constants.Schema,
                StringComparison.Ordinal),
            failures, "capabilities_context_schema", "$.cadContextSchema",
            "能力响应必须绑定CadContextJson v1 schema。" );
        Require(response.CadContextSchemaVersion == CadContextJsonV1Constants.SchemaVersion,
            failures, "capabilities_context_schema_version", "$.cadContextSchemaVersion",
            "能力响应必须绑定CadContextJson v1版本。" );
        ValidateSupportedCadContextSchemas(response.SupportedCadContextSchemas, failures);
        ValidateKnownSet(response.Methods, KnownMethods, false,
            "capabilities_method", "$.methods", failures);
        ValidateKnownSet(response.EventKinds, KnownEventKinds, false,
            "capabilities_event", "$.eventKinds", failures);
        ValidateKnownSet(response.ApprovalDecisions, KnownApprovalDecisions, true,
            "capabilities_approval", "$.approvalDecisions", failures);
        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(AgentThreadStartRequest? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure(
                "thread_request_required", "$", "线程请求不能为空。")];
        }

        ValidateContractVersion(request.ContractVersion, "$.contractVersion", failures);
        RequireIdentifier(request.ConversationId, "conversation_id", "$.conversationId", failures);
        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(AgentTurnStartRequest? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure("turn_request_required", "$", "回合请求不能为空。")];
        }

        ValidateContractVersion(request.ContractVersion, "$.contractVersion", failures);
        RequireIdentifier(request.ThreadId, "thread_id", "$.threadId", failures);
        RequireIdentifier(request.ClientTurnId, "client_turn_id", "$.clientTurnId", failures);
        Require(!string.IsNullOrWhiteSpace(request.Prompt)
                && IsSafeDisplayText(request.Prompt, MaximumPromptLength),
            failures, "prompt_length", "$.prompt", "提示词不能为空且不能超过安全长度。");
        if (request.Context is not null)
        {
            var contextFailures = CadContextJsonV1Validator.Validate(request.Context);
            foreach (var failure in contextFailures)
            {
                var suffix = failure.Path == "$" ? string.Empty : failure.Path.Substring(1);
                failures.Add(new CadValidationFailure(
                    failure.Code,
                    "$.context" + suffix,
                    failure.Message));
            }

            if (contextFailures.Length == 0)
            {
                Require(IsLowerSha256(request.ContextSha256), failures,
                    "context_hash", "$.contextSha256",
                    "上下文身份必须是64位小写ASCII十六进制SHA-256。" );
                if (IsLowerSha256(request.ContextSha256))
                {
                    var expected = CadContextJsonV1Codec.ComputeCanonicalSha256(request.Context);
                    Require(string.Equals(expected, request.ContextSha256, StringComparison.Ordinal),
                        failures, "context_hash_mismatch", "$.contextSha256",
                        "上下文身份与规范CadContextJson v1字节不一致。" );
                }
            }
        }
        else
        {
            Require(string.IsNullOrEmpty(request.ContextSha256), failures,
                "context_hash_without_context", "$.contextSha256",
                "没有CAD上下文时不得携带上下文哈希。" );
        }

        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(AgentTurnStartV2Request? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure("turn_v2_request_required", "$", "回合v2请求不能为空。")];
        }

        ValidateContractVersion(request.ContractVersion, "$.contractVersion", failures);
        RequireIdentifier(request.ThreadId, "thread_id", "$.threadId", failures);
        RequireIdentifier(request.ClientTurnId, "client_turn_id", "$.clientTurnId", failures);
        Require(!string.IsNullOrWhiteSpace(request.Prompt)
                && IsSafeDisplayText(request.Prompt, MaximumPromptLength),
            failures, "prompt_length", "$.prompt", "提示词不能为空且不能超过安全长度。");
        if (request.ContextV2 is not null)
        {
            var contextFailures = CadContextJsonV2Validator.Validate(request.ContextV2);
            foreach (var failure in contextFailures)
            {
                var suffix = failure.Path == "$" ? string.Empty : failure.Path.Substring(1);
                failures.Add(new CadValidationFailure(
                    failure.Code,
                    "$.contextV2" + suffix,
                    failure.Message));
            }

            if (contextFailures.Length == 0)
            {
                Require(IsLowerSha256(request.ContextV2Sha256), failures,
                    "context_v2_hash", "$.contextV2Sha256",
                    "上下文v2身份必须是64位小写ASCII十六进制SHA-256。");
                if (IsLowerSha256(request.ContextV2Sha256))
                {
                    var expected = CadContextJsonV2Codec.ComputeCanonicalSha256(request.ContextV2);
                    Require(string.Equals(expected, request.ContextV2Sha256, StringComparison.Ordinal),
                        failures, "context_v2_hash_mismatch", "$.contextV2Sha256",
                        "上下文v2身份与规范CadContextJson v2字节不一致。");
                }
            }
        }
        else
        {
            Require(string.IsNullOrEmpty(request.ContextV2Sha256), failures,
                "context_v2_hash_without_context", "$.contextV2Sha256",
                "没有CAD上下文v2时不得携带上下文v2哈希。");
        }

        return failures.ToArray();
    }

    public static CadValidationFailure[] ValidateTurnV2Acceptance(
        AgentTurnStartV2Request? request,
        AgentTurnStartV2Response? response)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            failures.Add(new CadValidationFailure(
                "turn_v2_request_required", "$.request", "原始回合v2请求不能为空。"));
            return failures.ToArray();
        }

        if (response is null)
        {
            failures.Add(new CadValidationFailure(
                "turn_v2_response_required", "$.response", "回合v2接受响应不能为空。"));
            return failures.ToArray();
        }

        ValidateContractVersion(response.ContractVersion, "$.response.contractVersion", failures);
        RequireIdentifier(response.ThreadId, "response_thread_id", "$.response.threadId", failures);
        RequireIdentifier(response.TurnId, "response_turn_id", "$.response.turnId", failures);
        Require(string.Equals(response.ThreadId, request.ThreadId, StringComparison.Ordinal),
            failures, "response_thread_mismatch", "$.response.threadId",
            "回合v2响应ThreadId与请求不一致。");
        Require(string.Equals(response.AcceptedContextV2Sha256, request.ContextV2Sha256,
                StringComparison.Ordinal),
            failures, "response_context_v2_mismatch", "$.response.acceptedContextV2Sha256",
            "回合v2响应未绑定到请求的精确CAD上下文v2。");
        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(AgentTurnInterruptRequest? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure(
                "interrupt_request_required", "$", "中断请求不能为空。")];
        }

        ValidateContractVersion(request.ContractVersion, "$.contractVersion", failures);
        RequireIdentifier(request.ThreadId, "thread_id", "$.threadId", failures);
        RequireIdentifier(request.TurnId, "turn_id", "$.turnId", failures);
        return failures.ToArray();
    }

    public static CadValidationFailure[] ValidateTurnAcceptance(
        AgentTurnStartRequest? request,
        AgentTurnStartResponse? response)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            failures.Add(new CadValidationFailure(
                "turn_request_required", "$.request", "原始回合请求不能为空。"));
            return failures.ToArray();
        }

        if (response is null)
        {
            failures.Add(new CadValidationFailure(
                "turn_response_required", "$.response", "回合接受响应不能为空。"));
            return failures.ToArray();
        }

        ValidateContractVersion(response.ContractVersion, "$.response.contractVersion", failures);
        RequireIdentifier(response.ThreadId, "response_thread_id", "$.response.threadId", failures);
        RequireIdentifier(response.TurnId, "response_turn_id", "$.response.turnId", failures);
        Require(string.Equals(response.ThreadId, request.ThreadId, StringComparison.Ordinal),
            failures, "response_thread_mismatch", "$.response.threadId",
            "回合响应ThreadId与请求不一致。" );
        Require(string.Equals(response.AcceptedContextSha256, request.ContextSha256,
                StringComparison.Ordinal),
            failures, "response_context_mismatch", "$.response.acceptedContextSha256",
            "回合响应未绑定到请求的精确CAD上下文。" );
        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(AgentApprovalResolveRequest? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure(
                "approval_request_required", "$", "审批决定不能为空。")];
        }

        ValidateContractVersion(request.ContractVersion, "$.contractVersion", failures);
        RequireIdentifier(request.ThreadId, "thread_id", "$.threadId", failures);
        RequireIdentifier(request.TurnId, "turn_id", "$.turnId", failures);
        RequireIdentifier(request.ApprovalId, "approval_id", "$.approvalId", failures);
        Require(IsKnown(request.Decision, KnownApprovalDecisions), failures,
            "approval_decision", "$.decision",
            "审批只能拒绝或一次允许，不支持会话级永久允许。" );
        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(AgentBridgeEvent? bridgeEvent)
    {
        var failures = new List<CadValidationFailure>();
        if (bridgeEvent is null)
        {
            return [new CadValidationFailure(
                "bridge_event_required", "$", "Agent事件不能为空。")];
        }

        ValidateContractVersion(bridgeEvent.ContractVersion, "$.contractVersion", failures);
        Require(IsKnown(bridgeEvent.Kind, KnownEventKinds), failures,
            "bridge_event_kind", "$.kind", "Agent事件类型不在白名单中。" );
        RequireIdentifier(bridgeEvent.EventId, "event_id", "$.eventId", failures);
        Require(bridgeEvent.Sequence > 0, failures,
            "event_sequence", "$.sequence", "事件sequence必须严格为正数。" );
        Require(IsUtcTimestamp(bridgeEvent.OccurredAtUtc), failures,
            "event_occurred_at", "$.occurredAtUtc", "事件时间必须是规范UTC时间。" );
        Require(IsSafeDisplayText(bridgeEvent.Content, MaximumDisplayTextLength), failures,
            "event_content", "$.content", "事件内容超过安全限制或包含无效Unicode。" );
        Require(IsSafeDisplayText(bridgeEvent.Delta, MaximumDisplayTextLength), failures,
            "event_delta", "$.delta", "事件增量超过安全限制或包含无效Unicode。" );

        if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.ConnectionStateChanged,
                StringComparison.Ordinal))
        {
            Require(IsKnown(bridgeEvent.ConnectionState, KnownConnectionStates), failures,
                "connection_state", "$.connectionState", "连接状态不在白名单中。" );
        }
        else
        {
            RequireIdentifier(bridgeEvent.ThreadId, "thread_id", "$.threadId", failures);
        }

        if (RequiresTurnId(bridgeEvent.Kind))
        {
            RequireIdentifier(bridgeEvent.TurnId, "turn_id", "$.turnId", failures);
        }

        if (!string.IsNullOrEmpty(bridgeEvent.ContextSha256))
        {
            Require(IsLowerSha256(bridgeEvent.ContextSha256), failures,
                "event_context_hash", "$.contextSha256",
                "事件上下文身份必须是64位小写ASCII十六进制SHA-256。" );
        }

        if (string.Equals(bridgeEvent.Kind, AgentBridgeEventKinds.ApprovalRequested,
                StringComparison.Ordinal))
        {
            RequireIdentifier(bridgeEvent.ApprovalId, "approval_id", "$.approvalId", failures);
            ValidateKnownSet(bridgeEvent.AllowedDecisions, KnownApprovalDecisions, false,
                "event_approval_decision", "$.allowedDecisions", failures);
        }

        if (IsFailureEvent(bridgeEvent.Kind))
        {
            Require(IsKnown(bridgeEvent.ErrorCode, KnownErrorCodes), failures,
                "event_error_code", "$.errorCode", "失败事件必须携带白名单错误码。" );
            Require(IsSafeDisplayText(bridgeEvent.Error, MaximumErrorLength)
                    && !string.IsNullOrWhiteSpace(bridgeEvent.Error),
                failures, "event_error", "$.error", "失败事件必须携带受限错误说明。" );
        }

        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(AgentBridgeFailure? failure)
    {
        var failures = new List<CadValidationFailure>();
        if (failure is null)
        {
            return [new CadValidationFailure(
                "bridge_failure_required", "$", "Bridge失败信息不能为空。")];
        }

        ValidateContractVersion(failure.ContractVersion, "$.contractVersion", failures);
        Require(IsKnown(failure.Code, KnownErrorCodes), failures,
            "bridge_error_code", "$.code", "Bridge错误码不在白名单中。" );
        Require(!string.IsNullOrWhiteSpace(failure.Message)
                && IsSafeDisplayText(failure.Message, MaximumErrorLength),
            failures, "bridge_error_message", "$.message", "Bridge错误说明无效。" );
        Require(IsUtcTimestamp(failure.OccurredAtUtc), failures,
            "bridge_error_time", "$.occurredAtUtc", "Bridge错误时间必须是规范UTC时间。" );
        return failures.ToArray();
    }

    public static CadValidationFailure[] ValidateEventIdentity(
        AgentBridgeEvent? bridgeEvent,
        string expectedThreadId,
        string expectedTurnId,
        string expectedContextSha256)
    {
        var failures = new List<CadValidationFailure>(Validate(bridgeEvent));
        if (bridgeEvent is null)
        {
            return failures.ToArray();
        }

        Require(string.Equals(bridgeEvent.ThreadId, expectedThreadId, StringComparison.Ordinal),
            failures, "event_thread_mismatch", "$.threadId",
            "事件ThreadId与当前线程不一致。" );
        Require(string.Equals(bridgeEvent.TurnId, expectedTurnId, StringComparison.Ordinal),
            failures, "event_turn_mismatch", "$.turnId",
            "事件TurnId与当前回合不一致。" );
        Require(string.Equals(bridgeEvent.ContextSha256, expectedContextSha256,
                StringComparison.Ordinal),
            failures, "event_context_mismatch", "$.contextSha256",
            "事件未绑定到当前回合的精确CAD上下文。" );
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

    public static CadValidationFailure[] Validate(AgentDrawingQueryRequest? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure(
                "drawing_query_request_required", "$", "反向整图查询请求不能为空。")];
        }

        ValidateContractVersion(request.ContractVersion, "$.contractVersion", failures);
        RequireIdentifier(request.RequestId, "request_id", "$.requestId", failures);
        RequireIdentifier(request.ThreadId, "thread_id", "$.threadId", failures);
        RequireIdentifier(request.TurnId, "turn_id", "$.turnId", failures);
        RequireIdentifier(request.ToolCallId, "tool_call_id", "$.toolCallId", failures);
        RequireIdentifier(request.QueryId, "drawing_query_id", "$.queryId", failures);

        failures.AddRange(DrawingIndexContractValidator.Validate(new CadQueryRequest
        {
            IndexId = "host-owned-index",
            DocumentId = "host-owned-document",
            DocumentRevision = 0,
            QueryId = request.QueryId,
            Filter = request.Filter,
            PageSize = request.PageSize,
            Cursor = request.Cursor,
        }));
        return failures.ToArray();
    }

    public static CadValidationFailure[] ValidateDrawingQueryResponse(
        AgentDrawingQueryRequest? request,
        AgentDrawingQueryResponse? response)
    {
        var failures = new List<CadValidationFailure>(Validate(request));
        if (response is null)
        {
            failures.Add(new CadValidationFailure(
                "drawing_query_response_required", "$", "反向整图查询响应不能为空。"));
            return failures.ToArray();
        }

        ValidateContractVersion(response.ContractVersion, "$.contractVersion", failures);
        RequireIdentifier(response.RequestId, "request_id", "$.requestId", failures);
        RequireIdentifier(response.ThreadId, "thread_id", "$.threadId", failures);
        RequireIdentifier(response.TurnId, "turn_id", "$.turnId", failures);
        RequireIdentifier(response.ToolCallId, "tool_call_id", "$.toolCallId", failures);
        RequireIdentifier(response.QueryId, "drawing_query_id", "$.queryId", failures);
        failures.AddRange(DrawingIndexContractValidator.Validate(response.Query));

        if (request is not null)
        {
            Require(string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal),
                failures, "drawing_query_response_request_mismatch", "$.requestId",
                "整图查询响应RequestId与请求不一致。");
            Require(string.Equals(response.ThreadId, request.ThreadId, StringComparison.Ordinal),
                failures, "drawing_query_response_thread_mismatch", "$.threadId",
                "整图查询响应ThreadId与请求不一致。");
            Require(string.Equals(response.TurnId, request.TurnId, StringComparison.Ordinal),
                failures, "drawing_query_response_turn_mismatch", "$.turnId",
                "整图查询响应TurnId与请求不一致。");
            Require(string.Equals(response.ToolCallId, request.ToolCallId, StringComparison.Ordinal),
                failures, "drawing_query_response_tool_call_mismatch", "$.toolCallId",
                "整图查询响应ToolCallId与请求不一致。");
            Require(string.Equals(response.QueryId, request.QueryId, StringComparison.Ordinal),
                failures, "drawing_query_response_query_mismatch", "$.queryId",
                "整图查询响应QueryId与请求不一致。");
        }

        if (response.Query is not null)
        {
            Require(string.Equals(response.Query.QueryId, response.QueryId, StringComparison.Ordinal),
                failures, "drawing_query_response_payload_mismatch", "$.query.queryId",
                "CadQuery响应QueryId与外层响应不一致。");
        }

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

    private static readonly string[] KnownCadContextSchemas =
    [
        CadContextJsonV1Constants.Schema,
    ];

    private static readonly string[] KnownCadContextSchemaVersions =
    [
        CadContextJsonV1Constants.SchemaVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture),
        CadContextJsonV2Constants.SchemaVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture),
    ];

    private static void ValidateSupportedCadContextSchemas(
        CadContextSchemaVersionEntry[]? schemas,
        ICollection<CadValidationFailure> failures)
    {
        schemas ??= [];
        Require(schemas.Length > 0, failures,
            "capabilities_schemas_required", "$.supportedCadContextSchemas",
            "能力响应必须列出至少一个支持的CadContext schema。");
        Require(schemas.Length <= KnownCadContextSchemaVersions.Length, failures,
            "capabilities_schemas_limit", "$.supportedCadContextSchemas",
            "支持的CadContext schema超过已知版本数。");
        var hasV1 = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < schemas.Length; index++)
        {
            var entry = schemas[index];
            var path = "$.supportedCadContextSchemas[" + index.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + "]";
            if (entry is null)
            {
                failures.Add(new CadValidationFailure(
                    "capabilities_schema_entry", path, "schema条目不能为空。"));
                continue;
            }

            var version = entry.SchemaVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            var identity = (entry.Schema ?? string.Empty) + "\n" + version;
            if (!seen.Add(identity))
            {
                failures.Add(new CadValidationFailure(
                    "capabilities_schema_duplicate", path,
                    "支持的CadContext schema/version条目不能重复。"));
            }

            Require(IsKnown(entry.Schema, KnownCadContextSchemas), failures,
                "capabilities_schema_name", path + ".schema",
                "schema名称不在已知列表中。");
            Require(IsKnown(version, KnownCadContextSchemaVersions),
                failures, "capabilities_schema_version", path + ".schemaVersion",
                "schema版本不在已知列表中。");
            if (string.Equals(entry.Schema, CadContextJsonV1Constants.Schema,
                    StringComparison.Ordinal)
                && entry.SchemaVersion == CadContextJsonV1Constants.SchemaVersion)
            {
                hasV1 = true;
            }
        }

        Require(hasV1, failures,
            "capabilities_schemas_v1_required", "$.supportedCadContextSchemas",
            "支持的schema列表必须始终包含v1。");
    }

    private static void ValidateContractVersion(
        int version,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(version == AgentBridgeContractConstants.CurrentVersion, failures,
            "agent_contract_version", path, "Host/Agent/UI公共契约版本不受支持。" );
    }

    private static void ValidateKnownSet(
        string[]? values,
        string[] known,
        bool allowEmpty,
        string code,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        values ??= new string[0];
        Require(allowEmpty || values.Length > 0, failures,
            code + "_required", path, "能力集合不能为空。" );
        Require(values.Length <= known.Length, failures,
            code + "_limit", path, "能力集合超过冻结白名单大小。" );
        if (values.Length > known.Length)
        {
            return;
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index++)
        {
            Require(IsKnown(values[index], known), failures,
                code, path + "[" + index + "]", "能力值不在冻结白名单中。" );
            Require(unique.Add(values[index]), failures,
                code + "_duplicate", path + "[" + index + "]", "能力值不能重复。" );
        }
    }

    private static bool RequiresTurnId(string kind)
    {
        return !string.Equals(kind, AgentBridgeEventKinds.ConnectionStateChanged,
                StringComparison.Ordinal)
            && !string.Equals(kind, AgentBridgeEventKinds.ThreadStarted, StringComparison.Ordinal);
    }

    private static bool IsFailureEvent(string kind)
    {
        return string.Equals(kind, AgentBridgeEventKinds.ToolFailed, StringComparison.Ordinal)
            || string.Equals(kind, AgentBridgeEventKinds.TurnFailed, StringComparison.Ordinal);
    }

    private static bool IsKnown(string? value, IEnumerable<string> known)
    {
        return value is not null
            && known.Any(item => string.Equals(item, value, StringComparison.Ordinal));
    }

    private static bool IsLowerSha256(string? value)
    {
        return value is { Length: 64 }
            && value.All(static character =>
                character is >= '0' and <= '9'
                or >= 'a' and <= 'f');
    }

    private static bool IsSafeDisplayText(string? value, int maximumLength)
    {
        if (value is null || value.Length > maximumLength)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\0')
            {
                return false;
            }

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUtcTimestamp(string? value)
    {
        DateTimeOffset parsed;
        return value is not null
            && DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out parsed)
            && parsed.Offset == TimeSpan.Zero;
    }

    private static void RequireIdentifier(
        string? value,
        string code,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(value is not null
                && !string.IsNullOrWhiteSpace(value)
                && value.Length <= MaximumIdentifierLength
                && IsSafeIdentifier(value),
            failures, code, path, "标识不能为空且不能超过安全长度。");
    }

    private static bool IsSafeIdentifier(string value)
    {
        return value.All(static character =>
            character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '-' or '_' or '.' or ':');
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
