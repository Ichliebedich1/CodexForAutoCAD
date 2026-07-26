using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codex.AutoCAD.AppServer.Protocol;

public sealed record CommandAction(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("query")] string? Query = null)
{
    public override string ToString()
        => $"{nameof(CommandAction)} {{ TypeConfigured = {!string.IsNullOrWhiteSpace(Type)}, "
            + $"CommandConfigured = {!string.IsNullOrWhiteSpace(Command)}, "
            + $"NamePresent = {Name is not null}, PathPresent = {Path is not null}, "
            + $"QueryPresent = {Query is not null} }}";
}

public sealed record NetworkApprovalContext(
    [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("protocol")] string Protocol)
{
    public override string ToString()
        => $"{nameof(NetworkApprovalContext)} {{ HostConfigured = {!string.IsNullOrWhiteSpace(Host)}, "
            + $"ProtocolConfigured = {!string.IsNullOrWhiteSpace(Protocol)} }}";
}

public sealed record NetworkPolicyAmendment(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("host")] string Host)
{
    public override string ToString()
        => $"{nameof(NetworkPolicyAmendment)} {{ ActionConfigured = {!string.IsNullOrWhiteSpace(Action)}, "
            + $"HostConfigured = {!string.IsNullOrWhiteSpace(Host)} }}";
}

public sealed record FileSystemSandboxEntry(
    [property: JsonPropertyName("access")] string Access,
    [property: JsonPropertyName("path")] JsonElement Path)
{
    public override string ToString()
        => $"{nameof(FileSystemSandboxEntry)} {{ AccessConfigured = {!string.IsNullOrWhiteSpace(Access)}, "
            + $"PathPresent = {Path.ValueKind is not JsonValueKind.Undefined} }}";
}

public sealed record AdditionalFileSystemPermissions(
    [property: JsonPropertyName("entries")] IReadOnlyList<FileSystemSandboxEntry>? Entries = null,
    [property: JsonPropertyName("globScanMaxDepth")] uint? GlobScanMaxDepth = null,
    [property: JsonPropertyName("read")] IReadOnlyList<string>? Read = null,
    [property: JsonPropertyName("write")] IReadOnlyList<string>? Write = null)
{
    public override string ToString()
        => $"{nameof(AdditionalFileSystemPermissions)} {{ EntryCount = {Entries?.Count ?? 0}, "
            + $"GlobScanMaxDepthPresent = {GlobScanMaxDepth.HasValue}, "
            + $"ReadCount = {Read?.Count ?? 0}, WriteCount = {Write?.Count ?? 0} }}";
}

public sealed record AdditionalNetworkPermissions(
    [property: JsonPropertyName("enabled")] bool? Enabled = null)
{
    public override string ToString()
        => $"{nameof(AdditionalNetworkPermissions)} {{ EnabledPresent = {Enabled.HasValue}, "
            + $"Enabled = {Enabled.GetValueOrDefault()} }}";
}

public sealed record PermissionProfile(
    [property: JsonPropertyName("fileSystem")] AdditionalFileSystemPermissions? FileSystem = null,
    [property: JsonPropertyName("network")] AdditionalNetworkPermissions? Network = null)
{
    public override string ToString()
        => $"{nameof(PermissionProfile)} {{ FileSystemPresent = {FileSystem is not null}, "
            + $"NetworkPresent = {Network is not null} }}";
}

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
    [property: JsonPropertyName("reason")] string? Reason = null)
{
    public override string ToString()
        => $"{nameof(CommandApprovalRequest)} {{ ItemIdConfigured = {!string.IsNullOrWhiteSpace(ItemId)}, "
            + $"ThreadIdConfigured = {!string.IsNullOrWhiteSpace(ThreadId)}, "
            + $"TurnIdConfigured = {!string.IsNullOrWhiteSpace(TurnId)}, "
            + $"AdditionalPermissionsPresent = {AdditionalPermissions is not null}, "
            + $"ApprovalIdPresent = {ApprovalId is not null}, "
            + $"AvailableDecisionCount = {AvailableDecisions?.Count ?? 0}, "
            + $"CommandPresent = {Command is not null}, CommandActionCount = {CommandActions?.Count ?? 0}, "
            + $"WorkingDirectoryPresent = {WorkingDirectory is not null}, "
            + $"EnvironmentIdPresent = {EnvironmentId is not null}, "
            + $"NetworkApprovalContextPresent = {NetworkApprovalContext is not null}, "
            + $"ExecPolicyAmendmentCount = {ProposedExecPolicyAmendment?.Count ?? 0}, "
            + $"NetworkPolicyAmendmentCount = {ProposedNetworkPolicyAmendments?.Count ?? 0}, "
            + $"ReasonPresent = {Reason is not null} }}";
}

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

    public override string ToString()
        => $"{nameof(CommandApprovalResponse)} {{ Kind = {Kind}, "
            + $"ExecPolicyAmendmentCount = {ExecPolicyAmendment?.Count ?? 0}, "
            + $"NetworkPolicyAmendmentPresent = {NetworkPolicyAmendment is not null} }}";

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
    [property: JsonPropertyName("reason")] string? Reason = null)
{
    public override string ToString()
        => $"{nameof(FileChangeApprovalRequest)} {{ ItemIdConfigured = {!string.IsNullOrWhiteSpace(ItemId)}, "
            + $"ThreadIdConfigured = {!string.IsNullOrWhiteSpace(ThreadId)}, "
            + $"TurnIdConfigured = {!string.IsNullOrWhiteSpace(TurnId)}, "
            + $"GrantRootPresent = {GrantRoot is not null}, ReasonPresent = {Reason is not null} }}";
}

public enum FileChangeApprovalDecision
{
    Accept,
    AcceptForSession,
    Decline,
    Cancel,
}

public sealed record FileChangeApprovalResponse(
    [property: JsonPropertyName("decision")] FileChangeApprovalDecision Decision)
{
    public override string ToString()
        => $"{nameof(FileChangeApprovalResponse)} {{ Decision = {Decision} }}";
}

public sealed record PermissionsApprovalRequest(
    [property: JsonPropertyName("cwd")] string WorkingDirectory,
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("permissions")] PermissionProfile Permissions,
    [property: JsonPropertyName("startedAtMs")] long StartedAtMs,
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId,
    [property: JsonPropertyName("environmentId")] string? EnvironmentId = null,
    [property: JsonPropertyName("reason")] string? Reason = null)
{
    public override string ToString()
        => $"{nameof(PermissionsApprovalRequest)} {{ WorkingDirectoryConfigured = "
            + $"{!string.IsNullOrWhiteSpace(WorkingDirectory)}, "
            + $"ItemIdConfigured = {!string.IsNullOrWhiteSpace(ItemId)}, "
            + $"PermissionsPresent = {Permissions is not null}, "
            + $"ThreadIdConfigured = {!string.IsNullOrWhiteSpace(ThreadId)}, "
            + $"TurnIdConfigured = {!string.IsNullOrWhiteSpace(TurnId)}, "
            + $"EnvironmentIdPresent = {EnvironmentId is not null}, ReasonPresent = {Reason is not null} }}";
}

public enum PermissionGrantScope
{
    Turn,
    Session,
}

public sealed record PermissionsApprovalResponse(
    [property: JsonPropertyName("permissions")] PermissionProfile Permissions,
    [property: JsonPropertyName("scope")] PermissionGrantScope Scope = PermissionGrantScope.Turn,
    [property: JsonPropertyName("strictAutoReview")] bool? StrictAutoReview = null)
{
    public override string ToString()
        => $"{nameof(PermissionsApprovalResponse)} {{ PermissionsPresent = {Permissions is not null}, "
            + $"Scope = {Scope}, StrictAutoReviewPresent = {StrictAutoReview.HasValue}, "
            + $"StrictAutoReview = {StrictAutoReview.GetValueOrDefault()} }}";
}

public sealed record CadDocumentIdentity(
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("pathHash")] string? PathHash = null)
{
    public override string ToString()
        => $"{nameof(CadDocumentIdentity)} {{ DocumentIdConfigured = {!string.IsNullOrWhiteSpace(DocumentId)}, "
            + $"FingerprintConfigured = {!string.IsNullOrWhiteSpace(Fingerprint)}, "
            + $"Revision = {Revision}, PathHashPresent = {PathHash is not null} }}";
}

public sealed record CadChangeSummary(
    [property: JsonPropertyName("added")] int Added,
    [property: JsonPropertyName("modified")] int Modified,
    [property: JsonPropertyName("deleted")] int Deleted,
    [property: JsonPropertyName("description")] string Description)
{
    public override string ToString()
        => $"{nameof(CadChangeSummary)} {{ Added = {Added}, Modified = {Modified}, Deleted = {Deleted}, "
            + $"DescriptionConfigured = {!string.IsNullOrWhiteSpace(Description)} }}";
}

public sealed record CadApprovalRequest(
    [property: JsonPropertyName("approvalId")] string ApprovalId,
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId,
    [property: JsonPropertyName("document")] CadDocumentIdentity Document,
    [property: JsonPropertyName("normalizedPlanHash")] string NormalizedPlanHash,
    [property: JsonPropertyName("riskLevel")] string RiskLevel,
    [property: JsonPropertyName("expiresAtMs")] long ExpiresAtMs,
    [property: JsonPropertyName("summary")] CadChangeSummary Summary,
    [property: JsonPropertyName("preview")] JsonElement? Preview = null)
{
    public override string ToString()
        => $"{nameof(CadApprovalRequest)} {{ ApprovalIdConfigured = {!string.IsNullOrWhiteSpace(ApprovalId)}, "
            + $"ThreadIdConfigured = {!string.IsNullOrWhiteSpace(ThreadId)}, "
            + $"TurnIdConfigured = {!string.IsNullOrWhiteSpace(TurnId)}, "
            + $"DocumentPresent = {Document is not null}, "
            + $"NormalizedPlanHashConfigured = {!string.IsNullOrWhiteSpace(NormalizedPlanHash)}, "
            + $"RiskLevelConfigured = {!string.IsNullOrWhiteSpace(RiskLevel)}, "
            + $"SummaryPresent = {Summary is not null}, PreviewPresent = {Preview.HasValue} }}";
}

public enum CadApprovalDecision
{
    Accept,
    Decline,
    Cancel,
}

public sealed record CadApprovalResponse(
    [property: JsonPropertyName("decision")] CadApprovalDecision Decision,
    [property: JsonPropertyName("approvalId")] string ApprovalId,
    [property: JsonPropertyName("normalizedPlanHash")] string NormalizedPlanHash)
{
    public override string ToString()
        => $"{nameof(CadApprovalResponse)} {{ Decision = {Decision}, "
            + $"ApprovalIdConfigured = {!string.IsNullOrWhiteSpace(ApprovalId)}, "
            + $"NormalizedPlanHashConfigured = {!string.IsNullOrWhiteSpace(NormalizedPlanHash)} }}";
}
