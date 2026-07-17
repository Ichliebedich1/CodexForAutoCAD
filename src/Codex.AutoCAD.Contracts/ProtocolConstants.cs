namespace Codex.AutoCAD.Contracts;

public static class ProtocolConstants
{
    public const int CurrentVersion = 1;
    public const int MaximumOperationsPerBatch = 5_000;
    public const int MaximumEntityHandlesPerOperation = 10_000;
    public const int MaximumContextEntities = 10_000;
    public const int MaximumMessageBytes = 8 * 1024 * 1024;
}

public enum CadRiskLevel
{
    LocalOnly = 0,
    ContextEgress = 1,
    Preview = 2,
    ReversibleWrite = 3,
    DestructiveWrite = 4,
    ExternalEffect = 5,
    Prohibited = 6
}

public enum CadApprovalDecisionKind
{
    AllowOnce = 0,
    DeclineAndContinue = 1,
    DeclineAndCancelTurn = 2
}

public enum CadApprovalState
{
    Proposed = 0,
    SchemaValidated = 1,
    PolicyValidated = 2,
    PreviewReady = 3,
    AwaitingUser = 4,
    ApprovedOnce = 5,
    Declined = 6,
    Expired = 7,
    Revalidating = 8,
    Executing = 9,
    Committed = 10,
    RolledBack = 11,
    ResultUncertain = 12
}
