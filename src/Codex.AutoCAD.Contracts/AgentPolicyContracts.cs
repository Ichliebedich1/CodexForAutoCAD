namespace Codex.AutoCAD.Contracts;

/// <summary>
/// M4.1 分层 Agent 策略契约。机器策略、管理员配置和用户配置在此合并为唯一受信策略，
/// UI 或 Agent 提交的任意字符串必须先经过本契约校验，不得直接穿透到 AgentHost/Codex。
/// 该类型面向 net45 与 net8 双目标，因此只使用 sealed class 与可变属性。
/// </summary>
public static class AgentPolicyConstants
{
    public const string Schema = "codex.autocad.agent-policy";

    public const int SchemaVersion = 1;

    /// <summary>模型标识长度上限，防止任意长字符串进入进程参数或诊断。</summary>
    public const int MaximumModelLength = 64;

    /// <summary>单层白名单条目上限，防止配置膨胀导致的解析成本失控。</summary>
    public const int MaximumAllowedEntryCount = 32;
}

/// <summary>配置层标识。优先级由高到低：机器策略 &gt; 管理员 &gt; 用户。</summary>
public static class AgentPolicyLayers
{
    public const string MachinePolicy = "machine-policy";

    public const string Administrator = "administrator";

    public const string User = "user";

    public static bool IsKnown(string? layer)
        => layer == MachinePolicy || layer == Administrator || layer == User;
}

/// <summary>Codex 支持的思考强度闭集。未知值一律 fail-closed。</summary>
public static class AgentReasoningEfforts
{
    public const string Minimal = "minimal";

    public const string Low = "low";

    public const string Medium = "medium";

    public const string High = "high";

    public static string[] All()
        => new[] { Minimal, Low, Medium, High };

    public static bool IsKnown(string? effort)
        => effort == Minimal || effort == Low || effort == Medium || effort == High;
}

/// <summary>策略错误码稳定闭集。调用方据此排查，不依赖异常文本。</summary>
public static class AgentPolicyErrorCodes
{
    public const string SchemaUnsupported = "agent_policy_schema_unsupported";

    public const string LayerUnknown = "agent_policy_layer_unknown";

    public const string LayerOrderInvalid = "agent_policy_layer_order_invalid";

    public const string ModelInvalid = "agent_policy_model_invalid";

    public const string ModelNotAllowed = "agent_policy_model_not_allowed";

    public const string ReasoningEffortInvalid = "agent_policy_reasoning_effort_invalid";

    public const string ReasoningEffortNotAllowed = "agent_policy_reasoning_effort_not_allowed";

    public const string AllowListEmpty = "agent_policy_allow_list_empty";

    public const string AllowListTooLarge = "agent_policy_allow_list_too_large";

    public const string DefaultNotAllowed = "agent_policy_default_not_allowed";

    public const string DefaultMissing = "agent_policy_default_missing";

    public const string LayerWidensAllowList = "agent_policy_layer_widens_allow_list";

    public const string LockedByHigherLayer = "agent_policy_locked_by_higher_layer";

    public const string NoEffectiveLayer = "agent_policy_no_effective_layer";

    /// <summary>配置文件路径不满足安全边界（相对路径、UNC、设备命名空间、reparse 或非固定盘）。</summary>
    public const string PathRejected = "agent_policy_path_rejected";

    /// <summary>配置文件存在但无法读取。</summary>
    public const string FileUnreadable = "agent_policy_file_unreadable";

    /// <summary>配置文件超过有界读取上限。</summary>
    public const string FileTooLarge = "agent_policy_file_too_large";

    /// <summary>配置文件不是合法 JSON，或含未知字段、错误类型。</summary>
    public const string FileMalformed = "agent_policy_file_malformed";
}

/// <summary>
/// 单个配置层的原始输入。缺省值表示"该层未表态"，从而由更低优先级的层或上层继承值决定。
/// </summary>
public sealed class AgentPolicyLayerDocument
{
    public string Schema { get; set; } = AgentPolicyConstants.Schema;

    public int SchemaVersion { get; set; } = AgentPolicyConstants.SchemaVersion;

    public string Layer { get; set; } = string.Empty;

    /// <summary>空数组表示该层不收窄模型白名单。</summary>
    public string[] AllowedModels { get; set; } = new string[0];

    /// <summary>空字符串表示该层不指定默认模型。</summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>空数组表示该层不收窄思考强度白名单。</summary>
    public string[] AllowedReasoningEfforts { get; set; } = new string[0];

    /// <summary>空字符串表示该层不指定默认思考强度。</summary>
    public string DefaultReasoningEffort { get; set; } = string.Empty;

    /// <summary>锁定后，更低优先级的层不得再收窄或改变模型选择。</summary>
    public bool LockModel { get; set; }

    /// <summary>锁定后，更低优先级的层不得再收窄或改变思考强度选择。</summary>
    public bool LockReasoningEffort { get; set; }
}

/// <summary>三层合并后的唯一受信策略。</summary>
public sealed class ResolvedAgentPolicy
{
    public string[] AllowedModels { get; set; } = new string[0];

    public string DefaultModel { get; set; } = string.Empty;

    public string[] AllowedReasoningEfforts { get; set; } = new string[0];

    public string DefaultReasoningEffort { get; set; } = string.Empty;

    public bool ModelLocked { get; set; }

    public bool ReasoningEffortLocked { get; set; }

    /// <summary>锁定该项的层标识；未锁定时为空字符串。不含路径或用户名。</summary>
    public string ModelLockedByLayer { get; set; } = string.Empty;

    public string ReasoningEffortLockedByLayer { get; set; } = string.Empty;
}

/// <summary>策略合并结果。失败时不返回部分策略，避免调用方误用。</summary>
public sealed class AgentPolicyResolution
{
    public bool Accepted { get; set; }

    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>产生错误的配置层；不含路径、用户名或原始值。</summary>
    public string ErrorLayer { get; set; } = string.Empty;

    public ResolvedAgentPolicy? Policy { get; set; }

    public static AgentPolicyResolution Reject(string errorCode, string layer)
        => new AgentPolicyResolution { Accepted = false, ErrorCode = errorCode, ErrorLayer = layer };

    public static AgentPolicyResolution Accept(ResolvedAgentPolicy policy)
        => new AgentPolicyResolution { Accepted = true, Policy = policy };
}

/// <summary>
/// 针对一次实际请求的选择结果。<see cref="AcceptedModel"/> 与
/// <see cref="AcceptedReasoningEffort"/> 是真正会被下发的值，调用方必须使用它们，
/// 而不是原始请求值。
/// </summary>
public sealed class AgentPolicySelection
{
    public bool Accepted { get; set; }

    public string ErrorCode { get; set; } = string.Empty;

    public string AcceptedModel { get; set; } = string.Empty;

    public string AcceptedReasoningEffort { get; set; } = string.Empty;

    /// <summary>请求值被策略默认值取代时为 true，便于 UI 如实回显。</summary>
    public bool ModelCoercedToDefault { get; set; }

    public bool ReasoningEffortCoercedToDefault { get; set; }

    public static AgentPolicySelection Reject(string errorCode)
        => new AgentPolicySelection { Accepted = false, ErrorCode = errorCode };
}
