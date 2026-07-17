using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codex.AutoCAD.AppServer.Protocol;

public sealed record CommandAction(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("query")] string? Query = null);

public sealed record NetworkApprovalContext(
    [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("protocol")] string Protocol);

public sealed record NetworkPolicyAmendment(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("host")] string Host);

public sealed record FileSystemSandboxEntry(
    [property: JsonPropertyName("access")] string Access,
    [property: JsonPropertyName("path")] JsonElement Path);

public sealed record AdditionalFileSystemPermissions(
    [property: JsonPropertyName("entries")] IReadOnlyList<FileSystemSandboxEntry>? Entries = null,
    [property: JsonPropertyName("globScanMaxDepth")] uint? GlobScanMaxDepth = null,
    [property: JsonPropertyName("read")] IReadOnlyList<string>? Read = null,
    [property: JsonPropertyName("write")] IReadOnlyList<string>? Write = null);

public sealed record AdditionalNetworkPermissions(
    [property: JsonPropertyName("enabled")] bool? Enabled = null);

public sealed record PermissionProfile(
    [property: JsonPropertyName("fileSystem")] AdditionalFileSystemPermissions? FileSystem = null,
    [property: JsonPropertyName("network")] AdditionalNetworkPermissions? Network = null);

public sealed record CommandApprovalRequest(
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("startedAtMs")] long StartedAtMs,
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId,
    [property: JsonPropertyName("additionalPermissions")] PermissionProfile? AdditionalPermissions = null,
    [property: JsonPropertyName("approvalId")] string? ApprovalId = null,
    [property: JsonPropertyName("availableDecisions")] IReadOnlyList<JsonElement>? AvailableDecisions = null,
    [property: JsonPropertyName("command")] string? Command = null,
    [property: JsonPropertyName("commandActions")] IReadOnlyList<CommandAction>? CommandActions = null,
    [property: JsonPropertyName("cwd")] string? WorkingDirectory = null,
    [property: JsonPropertyName("environmentId")] string? EnvironmentId = null,
    [property: JsonPropertyName("networkApprovalContext")] NetworkApprovalContext? NetworkApprovalContext = null,
    [property: JsonPropertyName("proposedExecpolicyAmendment")] IReadOnlyList<string>? ProposedExecPolicyAmendment = null,
    [property: JsonPropertyName("proposedNetworkPolicyAmendments")] IReadOnlyList<NetworkPolicyAmendment>? ProposedNetworkPolicyAmendments = null,
    [property: JsonPropertyName("reason")] string? Reason = null);

public enum CommandApprovalDecisionKind
{
    Accept,
    AcceptForSession,
    AcceptWithExecPolicyAmendment,
    ApplyNetworkPolicyAmendment,
    Decline,
    Cancel,
}

/// <summary>Response to a Codex command approval. Factory methods preserve union wire shapes.</summary>
public sealed record CommandApprovalResponse
{
    private CommandApprovalResponse(
        CommandApprovalDecisionKind kind,
        IReadOnlyList<string>? execPolicyAmendment = null,
        NetworkPolicyAmendment? networkPolicyAmendment = null)
    {
        Kind = kind;
        ExecPolicyAmendment = execPolicyAmendment;
        NetworkPolicyAmendment = networkPolicyAmendment;
    }

    public CommandApprovalDecisionKind Kind { get; }

    public IReadOnlyList<string>? ExecPolicyAmendment { get; }

    public NetworkPolicyAmendment? NetworkPolicyAmendment { get; }

    public static CommandApprovalResponse AcceptOnce { get; } = new(CommandApprovalDecisionKind.Accept);

    public static CommandApprovalResponse AcceptForSession { get; } = new(CommandApprovalDecisionKind.AcceptForSession);

    public static CommandApprovalResponse Decline { get; } = new(CommandApprovalDecisionKind.Decline);

    public static CommandApprovalResponse Cancel { get; } = new(CommandApprovalDecisionKind.Cancel);

    public static CommandApprovalResponse WithExecPolicyAmendment(IReadOnlyList<string> amendment)
    {
        ArgumentNullException.ThrowIfNull(amendment);
        return new(CommandApprovalDecisionKind.AcceptWithExecPolicyAmendment, amendment);
    }

    public static CommandApprovalResponse WithNetworkPolicyAmendment(NetworkPolicyAmendment amendment)
    {
        ArgumentNullException.ThrowIfNull(amendment);
        return new(CommandApprovalDecisionKind.ApplyNetworkPolicyAmendment, networkPolicyAmendment: amendment);
    }

    internal object ToWireResponse() => new { decision = ToWireDecision() };

    private object ToWireDecision() => Kind switch
    {
        CommandApprovalDecisionKind.Accept => "accept",
        CommandApprovalDecisionKind.AcceptForSession => "acceptForSession",
        CommandApprovalDecisionKind.Decline => "decline",
        CommandApprovalDecisionKind.Cancel => "cancel",
        CommandApprovalDecisionKind.AcceptWithExecPolicyAmendment => new
        {
            acceptWithExecpolicyAmendment = new Dictionary<string, object?>
            {
                ["execpolicy_amendment"] = ExecPolicyAmendment,
            },
        },
        CommandApprovalDecisionKind.ApplyNetworkPolicyAmendment => new
        {
            applyNetworkPolicyAmendment = new Dictionary<string, object?>
            {
                ["network_policy_amendment"] = NetworkPolicyAmendment,
            },
        },
        _ => throw new InvalidOperationException($"Unsupported command approval decision: {Kind}"),
    };
}

public sealed record FileChangeApprovalRequest(
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("startedAtMs")] long StartedAtMs,
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId,
    [property: JsonPropertyName("grantRoot")] string? GrantRoot = null,
    [property: JsonPropertyName("reason")] string? Reason = null);

public enum FileChangeApprovalDecision
{
    Accept,
    AcceptForSession,
    Decline,
    Cancel,
}

public sealed record FileChangeApprovalResponse(
    [property: JsonPropertyName("decision")] FileChangeApprovalDecision Decision);

public sealed record PermissionsApprovalRequest(
    [property: JsonPropertyName("cwd")] string WorkingDirectory,
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("permissions")] PermissionProfile Permissions,
    [property: JsonPropertyName("startedAtMs")] long StartedAtMs,
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId,
    [property: JsonPropertyName("environmentId")] string? EnvironmentId = null,
    [property: JsonPropertyName("reason")] string? Reason = null);

public enum PermissionGrantScope
{
    Turn,
    Session,
}

public sealed record PermissionsApprovalResponse(
    [property: JsonPropertyName("permissions")] PermissionProfile Permissions,
    [property: JsonPropertyName("scope")] PermissionGrantScope Scope = PermissionGrantScope.Turn,
    [property: JsonPropertyName("strictAutoReview")] bool? StrictAutoReview = null);

public sealed record CadDocumentIdentity(
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("pathHash")] string? PathHash = null);

public sealed record CadChangeSummary(
    [property: JsonPropertyName("added")] int Added,
    [property: JsonPropertyName("modified")] int Modified,
    [property: JsonPropertyName("deleted")] int Deleted,
    [property: JsonPropertyName("description")] string Description);

public sealed record CadApprovalRequest(
    [property: JsonPropertyName("approvalId")] string ApprovalId,
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId,
    [property: JsonPropertyName("document")] CadDocumentIdentity Document,
    [property: JsonPropertyName("normalizedPlanHash")] string NormalizedPlanHash,
    [property: JsonPropertyName("riskLevel")] string RiskLevel,
    [property: JsonPropertyName("expiresAtMs")] long ExpiresAtMs,
    [property: JsonPropertyName("summary")] CadChangeSummary Summary,
    [property: JsonPropertyName("preview")] JsonElement? Preview = null);

public enum CadApprovalDecision
{
    Accept,
    Decline,
    Cancel,
}

public sealed record CadApprovalResponse(
    [property: JsonPropertyName("decision")] CadApprovalDecision Decision,
    [property: JsonPropertyName("approvalId")] string ApprovalId,
    [property: JsonPropertyName("normalizedPlanHash")] string NormalizedPlanHash);
