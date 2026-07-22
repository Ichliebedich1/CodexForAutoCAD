using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codex.AutoCAD.AgentRuntime;

public enum AgentSandboxMode
{
    ReadOnly,
    WorkspaceWrite,
}

public enum AgentApprovalPolicy
{
    Untrusted,
    OnRequest,
}

public enum AgentApprovalsReviewer
{
    User,
    AutoReview,
}

public enum AgentTurnStatus
{
    Unknown,
    InProgress,
    Completed,
    Interrupted,
    Failed,
}

/// <summary>Connection-independent defaults applied to every conversation request.</summary>
public sealed record AgentRuntimeOptions
{
    public AgentSandboxMode Sandbox { get; init; } = AgentSandboxMode.ReadOnly;

    public AgentApprovalPolicy ApprovalPolicy { get; init; } = AgentApprovalPolicy.OnRequest;

    public AgentApprovalsReviewer ApprovalsReviewer { get; init; } = AgentApprovalsReviewer.User;

    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Trusted root fixed by the host at runtime construction. Per-thread and per-turn working
    /// directories may narrow this root, but can never expand beyond it.
    /// </summary>
    public string? ManagedWorkspaceRoot { get; init; }

    /// <summary>
    /// Local image and mention inputs are disabled by default. When enabled, their paths must be
    /// located under <see cref="ManagedWorkspaceRoot"/>.
    /// </summary>
    public bool AllowLocalFileInputs { get; init; }

    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public int MaximumIdentifierCharacters { get; init; } = 256;

    public int MaximumPromptCharacters { get; init; } = 64 * 1024;

    public int MaximumInputItems { get; init; } = 16;

    public int MaximumPathCharacters { get; init; } = 2_048;

    public int MaximumConcurrentCadProposals { get; init; } = 1;

    public int MaximumConcurrentCadDrawingQueries { get; init; } = 4;

    public int MaximumTrackedCadCalls { get; init; } = 256;

    public int MaximumTrackedThreads { get; init; } = 128;

    public int MaximumActiveTurns { get; init; } = 128;

    public TimeSpan CadProposalTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan CadDrawingQueryTimeout { get; init; } = TimeSpan.FromSeconds(15);
}

public sealed record AgentThreadOptions
{
    public string? WorkingDirectory { get; init; }

    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public string? DeveloperInstructions { get; init; }

    public string? ServiceTier { get; init; }

    public bool? Ephemeral { get; init; }

    public bool EnableCadDynamicTools { get; init; } = true;

    /// <summary>
    /// Exposes the read-only drawing query tool without enabling any CAD write proposal tool.
    /// </summary>
    public bool EnableCadDrawingQueryTool { get; init; }
}

public sealed record AgentTurnOptions
{
    public string? WorkingDirectory { get; init; }

    public string? Model { get; init; }

    public string? ClientUserMessageId { get; init; }

    public string? ServiceTier { get; init; }

    public JsonElement? OutputSchema { get; init; }
}

public sealed record AgentThreadHandle(
    string ThreadId,
    string? WorkingDirectory,
    string? Model,
    string? ModelProvider);

public sealed record AgentTurnHandle(
    string ThreadId,
    string TurnId,
    AgentTurnStatus Status);

public abstract record AgentInput
{
    internal abstract object ToWire();
}

public sealed record AgentTextInput(string Text) : AgentInput
{
    internal override object ToWire()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Text);
        return new TextInputWire("text", Text);
    }
}

public sealed record AgentLocalImageInput(string Path) : AgentInput
{
    internal override object ToWire()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        return new LocalImageInputWire("localImage", Path);
    }
}

public sealed record AgentMentionInput(string Name, string Path) : AgentInput
{
    internal override object ToWire()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        return new MentionInputWire("mention", Name, Path);
    }
}

internal sealed record ThreadStartWireParams(
    [property: JsonPropertyName("sandbox")] string Sandbox,
    [property: JsonPropertyName("approvalPolicy")] string ApprovalPolicy,
    [property: JsonPropertyName("approvalsReviewer")] string ApprovalsReviewer,
    [property: JsonPropertyName("cwd")] string? WorkingDirectory,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("modelProvider")] string? ModelProvider,
    [property: JsonPropertyName("developerInstructions")] string? DeveloperInstructions,
    [property: JsonPropertyName("serviceTier")] string? ServiceTier,
    [property: JsonPropertyName("ephemeral")] bool? Ephemeral,
    [property: JsonPropertyName("runtimeWorkspaceRoots")] IReadOnlyList<string>? RuntimeWorkspaceRoots,
    [property: JsonPropertyName("dynamicTools")] IReadOnlyList<DynamicToolNamespaceWire> DynamicTools);

internal sealed record ThreadResumeWireParams(
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("sandbox")] string Sandbox,
    [property: JsonPropertyName("approvalPolicy")] string ApprovalPolicy,
    [property: JsonPropertyName("approvalsReviewer")] string ApprovalsReviewer,
    [property: JsonPropertyName("cwd")] string? WorkingDirectory,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("modelProvider")] string? ModelProvider,
    [property: JsonPropertyName("developerInstructions")] string? DeveloperInstructions,
    [property: JsonPropertyName("serviceTier")] string? ServiceTier,
    [property: JsonPropertyName("runtimeWorkspaceRoots")] IReadOnlyList<string>? RuntimeWorkspaceRoots);

internal sealed record TurnStartWireParams(
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("input")] IReadOnlyList<object> Input,
    [property: JsonPropertyName("approvalPolicy")] string ApprovalPolicy,
    [property: JsonPropertyName("approvalsReviewer")] string ApprovalsReviewer,
    [property: JsonPropertyName("sandboxPolicy")] object SandboxPolicy,
    [property: JsonPropertyName("cwd")] string? WorkingDirectory,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("clientUserMessageId")] string? ClientUserMessageId,
    [property: JsonPropertyName("serviceTier")] string? ServiceTier,
    [property: JsonPropertyName("outputSchema")] JsonElement? OutputSchema,
    [property: JsonPropertyName("runtimeWorkspaceRoots")] IReadOnlyList<string>? RuntimeWorkspaceRoots);

internal sealed record TurnInterruptWireParams(
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId);

internal sealed record TextInputWire(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

internal sealed record LocalImageInputWire(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("path")] string Path);

internal sealed record MentionInputWire(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path);

internal sealed record DynamicToolNamespaceWire(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("tools")] IReadOnlyList<DynamicToolFunctionWire> Tools);

internal sealed record DynamicToolFunctionWire(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("inputSchema")] JsonElement InputSchema);

internal sealed record WorkspaceWriteSandboxPolicyWire(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("networkAccess")] bool NetworkAccess,
    [property: JsonPropertyName("writableRoots")] IReadOnlyList<string> WritableRoots);

internal sealed record ReadOnlySandboxPolicyWire(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("networkAccess")] bool NetworkAccess);

internal sealed record DynamicToolCallResponseWire(
    [property: JsonPropertyName("contentItems")] IReadOnlyList<DynamicToolTextContentWire> ContentItems,
    [property: JsonPropertyName("success")] bool Success);

internal sealed record DynamicToolTextContentWire(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

internal static class AgentWireValues
{
    public static string ToWire(this AgentSandboxMode value) => value switch
    {
        AgentSandboxMode.ReadOnly => "read-only",
        AgentSandboxMode.WorkspaceWrite => "workspace-write",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sandbox mode."),
    };

    public static string ToWire(this AgentApprovalPolicy value) => value switch
    {
        AgentApprovalPolicy.Untrusted => "untrusted",
        AgentApprovalPolicy.OnRequest => "on-request",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported approval policy."),
    };

    public static string ToWire(this AgentApprovalsReviewer value) => value switch
    {
        AgentApprovalsReviewer.User => "user",
        AgentApprovalsReviewer.AutoReview => "auto_review",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported approvals reviewer."),
    };

    public static object ToSandboxPolicyWire(
        this AgentSandboxMode value,
        IReadOnlyList<string>? writableRoots = null) => value switch
    {
        AgentSandboxMode.ReadOnly => new ReadOnlySandboxPolicyWire("readOnly", NetworkAccess: false),
        AgentSandboxMode.WorkspaceWrite => new WorkspaceWriteSandboxPolicyWire(
            "workspaceWrite",
            NetworkAccess: false,
            writableRoots ?? Array.Empty<string>()),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sandbox mode."),
    };
}
