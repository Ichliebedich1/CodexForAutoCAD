namespace Codex.AutoCAD.Security;

/// <summary>
/// Security-relevant actions understood by the trusted broker. Values not listed here are
/// deliberately classified as denied.
/// </summary>
public enum CadActionKind
{
    Unknown = 0,
    LocalUi,
    ReadSelectionSummary,
    ReadSelectedGeometry,
    ReadFullDrawing,
    CaptureView,
    Measure,
    Preview,
    CreateEntity,
    ModifyEntity,
    TransformEntity,
    DeleteEntity,
    BulkEdit,
    RedefineBlock,
    BooleanSubtract,
    Purge,
    ChangeExternalReference,
    SaveDrawing,
    OverwriteFile,
    Export,
    Print,
    NetworkAccess,
    LaunchExternalProcess,
    LoadUnsignedPlugin,
    ExecuteInProcessCode,
    ModifySecurityPolicy,
    RegistryWrite,
}

public enum CadRiskLevel
{
    R0LocalOnly = 0,
    R1ContextEgress = 1,
    R2Transient = 2,
    R3CadMutation = 3,
    R4DestructiveOrBulk = 4,
    R5ExternalEffect = 5,
    HardDeny = 100,
}

public enum PolicyDecision
{
    AllowAutomatically,
    RequireApproval,
    Deny,
}

public enum ApprovalScope
{
    Once,
    Session,
}

public sealed record CadActionDescriptor(CadActionKind Kind)
{
    public int OperationCount { get; init; }

    public int AffectedEntityCount { get; init; }

    public int DeletedEntityCount { get; init; }

    public int TargetEntityCount { get; init; }

    public bool UserExplicitlyAttachedContext { get; init; }
}

public sealed record RiskAssessment(
    CadActionKind Action,
    CadRiskLevel Level,
    PolicyDecision Decision,
    bool IsCadWrite,
    bool IsExternalEffect,
    bool RequiresCheckpoint,
    bool SessionGrantAllowed,
    string ReasonCode);

/// <summary>
/// Maps only known action kinds to policy. Invalid metadata and unknown enum values fail closed.
/// </summary>
public static class CadRiskClassifier
{
    public static RiskAssessment Assess(CadActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!Enum.IsDefined(action.Kind)
            || action.Kind == CadActionKind.Unknown
            || action.OperationCount < 0
            || action.AffectedEntityCount < 0
            || action.DeletedEntityCount < 0
            || action.TargetEntityCount < 0
            || action.DeletedEntityCount > action.AffectedEntityCount && action.AffectedEntityCount > 0)
        {
            return Deny(action.Kind, "POLICY_UNKNOWN_OR_INVALID_ACTION");
        }

        return action.Kind switch
        {
            CadActionKind.LocalUi => Allow(action.Kind, CadRiskLevel.R0LocalOnly, sessionGrantAllowed: true),
            CadActionKind.ReadSelectionSummary => Allow(action.Kind, CadRiskLevel.R1ContextEgress, sessionGrantAllowed: true),
            CadActionKind.ReadSelectedGeometry when action.UserExplicitlyAttachedContext
                => Allow(action.Kind, CadRiskLevel.R1ContextEgress, sessionGrantAllowed: true),
            CadActionKind.ReadSelectedGeometry
                => RequireApproval(action.Kind, CadRiskLevel.R1ContextEgress, reason: "POLICY_CONTEXT_NOT_ATTACHED"),
            CadActionKind.ReadFullDrawing or CadActionKind.CaptureView
                => RequireApproval(action.Kind, CadRiskLevel.R1ContextEgress, reason: "POLICY_BROAD_CONTEXT_EGRESS"),
            CadActionKind.Measure or CadActionKind.Preview
                => Allow(action.Kind, CadRiskLevel.R2Transient, sessionGrantAllowed: true),
            CadActionKind.CreateEntity or CadActionKind.ModifyEntity or CadActionKind.TransformEntity
                => RequireApproval(action.Kind, CadRiskLevel.R3CadMutation, isCadWrite: true),
            CadActionKind.DeleteEntity or CadActionKind.BulkEdit or CadActionKind.RedefineBlock
                or CadActionKind.BooleanSubtract or CadActionKind.Purge or CadActionKind.ChangeExternalReference
                => RequireApproval(
                    action.Kind,
                    CadRiskLevel.R4DestructiveOrBulk,
                    isCadWrite: true,
                    requiresCheckpoint: true),
            CadActionKind.SaveDrawing or CadActionKind.OverwriteFile or CadActionKind.Export
                or CadActionKind.Print or CadActionKind.NetworkAccess or CadActionKind.LaunchExternalProcess
                => RequireApproval(
                    action.Kind,
                    CadRiskLevel.R5ExternalEffect,
                    isCadWrite: action.Kind == CadActionKind.SaveDrawing,
                    isExternalEffect: true,
                    requiresCheckpoint: action.Kind is CadActionKind.SaveDrawing or CadActionKind.OverwriteFile),
            CadActionKind.LoadUnsignedPlugin or CadActionKind.ExecuteInProcessCode
                or CadActionKind.ModifySecurityPolicy or CadActionKind.RegistryWrite
                => Deny(action.Kind, "POLICY_HARD_DENY"),
            _ => Deny(action.Kind, "POLICY_DEFAULT_DENY"),
        };
    }

    private static RiskAssessment Allow(
        CadActionKind action,
        CadRiskLevel level,
        bool sessionGrantAllowed = false) =>
        new(
            action,
            level,
            PolicyDecision.AllowAutomatically,
            IsCadWrite: false,
            IsExternalEffect: false,
            RequiresCheckpoint: false,
            SessionGrantAllowed: sessionGrantAllowed,
            ReasonCode: "POLICY_ALLOW");

    private static RiskAssessment RequireApproval(
        CadActionKind action,
        CadRiskLevel level,
        bool isCadWrite = false,
        bool isExternalEffect = false,
        bool requiresCheckpoint = false,
        string reason = "POLICY_APPROVAL_REQUIRED") =>
        new(
            action,
            level,
            PolicyDecision.RequireApproval,
            isCadWrite,
            isExternalEffect,
            requiresCheckpoint,
            SessionGrantAllowed: false,
            reason);

    private static RiskAssessment Deny(CadActionKind action, string reason) =>
        new(
            action,
            CadRiskLevel.HardDeny,
            PolicyDecision.Deny,
            IsCadWrite: false,
            IsExternalEffect: false,
            RequiresCheckpoint: false,
            SessionGrantAllowed: false,
            reason);
}

public static class SessionAuthorizationPolicy
{
    /// <summary>
    /// Session grants are only available for low-risk, read-only, non-external actions that the
    /// classifier explicitly opted in. CAD writes are therefore never session-authorizable.
    /// </summary>
    public static bool IsAllowed(CadActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var assessment = CadRiskClassifier.Assess(action);

        return assessment.SessionGrantAllowed
            && assessment.Decision != PolicyDecision.Deny
            && assessment.Level <= CadRiskLevel.R2Transient
            && !assessment.IsCadWrite
            && !assessment.IsExternalEffect;
    }
}

public enum QuotaDisposition
{
    Allow,
    SummarizeOnly,
    BoundingBoxesOnly,
    EscalateToHighRisk,
    Reject,
}

public sealed record ResourceQuotaAssessment(QuotaDisposition Disposition, string ReasonCode)
{
    public bool IsAllowed => Disposition != QuotaDisposition.Reject;
}

public sealed record ResourceQuotaOptions(
    int MaxIpcMessageBytes = 8 * 1024 * 1024,
    int MaxJsonDepth = 32,
    int MaxContextEntityCount = 10_000,
    int MaxContextPayloadBytes = 32 * 1024 * 1024,
    int MaxPlanOperationCount = 5_000,
    int HighRiskOperationCount = 500,
    int HighRiskDeletedEntityCount = 100,
    double HighRiskDeletedTargetFraction = 0.05,
    int MaxDetailedPreviewObjectCount = 2_000,
    long MaxWorkerMemoryBytes = 1024L * 1024 * 1024,
    TimeSpan? SoftSimulationTimeout = null,
    TimeSpan? HardSimulationTimeout = null)
{
    public TimeSpan EffectiveSoftSimulationTimeout => SoftSimulationTimeout ?? TimeSpan.FromSeconds(30);

    public TimeSpan EffectiveHardSimulationTimeout => HardSimulationTimeout ?? TimeSpan.FromSeconds(120);
}

/// <summary>
/// Central resource limits. Limits can be made stricter by configuration but invalid or
/// unbounded-looking settings are rejected at construction time.
/// </summary>
public sealed class ResourceQuotaPolicy
{
    public ResourceQuotaPolicy(ResourceQuotaOptions? options = null)
    {
        Options = options ?? new ResourceQuotaOptions();
        ValidateOptions(Options);
    }

    public ResourceQuotaOptions Options { get; }

    public ResourceQuotaAssessment AssessIpcMessage(int payloadBytes, int jsonDepth)
    {
        if (payloadBytes < 0 || jsonDepth < 0)
        {
            return Reject("QUOTA_INVALID_IPC_USAGE");
        }

        return payloadBytes > Options.MaxIpcMessageBytes || jsonDepth > Options.MaxJsonDepth
            ? Reject("QUOTA_IPC_EXCEEDED")
            : Allow();
    }

    public ResourceQuotaAssessment AssessContext(int entityCount, int payloadBytes)
    {
        if (entityCount < 0 || payloadBytes < 0)
        {
            return Reject("QUOTA_INVALID_CONTEXT_USAGE");
        }

        return entityCount > Options.MaxContextEntityCount || payloadBytes > Options.MaxContextPayloadBytes
            ? new ResourceQuotaAssessment(QuotaDisposition.SummarizeOnly, "QUOTA_CONTEXT_SUMMARY_REQUIRED")
            : Allow();
    }

    public ResourceQuotaAssessment AssessPlan(int operationCount, int deletedEntityCount, int targetEntityCount)
    {
        if (operationCount < 0
            || deletedEntityCount < 0
            || targetEntityCount < 0)
        {
            return Reject("QUOTA_INVALID_PLAN_USAGE");
        }

        if (operationCount > Options.MaxPlanOperationCount)
        {
            return Reject("QUOTA_PLAN_REQUIRES_SPLIT");
        }

        var deletedFraction = targetEntityCount == 0
            ? (deletedEntityCount == 0 ? 0 : 1)
            : (double)deletedEntityCount / targetEntityCount;

        return operationCount > Options.HighRiskOperationCount
            || deletedEntityCount > Options.HighRiskDeletedEntityCount
            || deletedFraction > Options.HighRiskDeletedTargetFraction
            ? new ResourceQuotaAssessment(QuotaDisposition.EscalateToHighRisk, "QUOTA_PLAN_HIGH_RISK")
            : Allow();
    }

    public ResourceQuotaAssessment AssessPreview(int objectCount)
    {
        if (objectCount < 0)
        {
            return Reject("QUOTA_INVALID_PREVIEW_USAGE");
        }

        return objectCount > Options.MaxDetailedPreviewObjectCount
            ? new ResourceQuotaAssessment(QuotaDisposition.BoundingBoxesOnly, "QUOTA_PREVIEW_BOUNDING_BOXES")
            : Allow();
    }

    private static void ValidateOptions(ResourceQuotaOptions options)
    {
        if (options.MaxIpcMessageBytes <= 0
            || options.MaxJsonDepth <= 0
            || options.MaxContextEntityCount <= 0
            || options.MaxContextPayloadBytes <= 0
            || options.MaxPlanOperationCount <= 0
            || options.HighRiskOperationCount <= 0
            || options.HighRiskOperationCount >= options.MaxPlanOperationCount
            || options.HighRiskDeletedEntityCount <= 0
            || options.HighRiskDeletedTargetFraction is <= 0 or > 1
            || options.MaxDetailedPreviewObjectCount <= 0
            || options.MaxWorkerMemoryBytes <= 0
            || options.EffectiveSoftSimulationTimeout <= TimeSpan.Zero
            || options.EffectiveHardSimulationTimeout <= options.EffectiveSoftSimulationTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Resource quota settings must remain finite and internally consistent.");
        }
    }

    private static ResourceQuotaAssessment Allow() => new(QuotaDisposition.Allow, "QUOTA_ALLOW");

    private static ResourceQuotaAssessment Reject(string reason) => new(QuotaDisposition.Reject, reason);
}
