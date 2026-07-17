namespace Codex.AutoCAD.Contracts;

public sealed class CadApprovalBinding
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string DrawingFingerprint { get; set; } = string.Empty;

    public long DrawingRevision { get; set; }

    public string SelectionSnapshotHash { get; set; } = string.Empty;

    public string NormalizedPlanHash { get; set; } = string.Empty;
}

public sealed class CadApprovalRequest
{
    public string ApprovalId { get; set; } = string.Empty;

    public CadApprovalBinding Binding { get; set; } = new();

    public CadOperationDiff Diff { get; set; } = new();

    public CadRiskLevel Risk { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string ExpiresAtUtc { get; set; } = string.Empty;

    public bool SupportsSingleUndo { get; set; }
}

public sealed class CadApprovalDecision
{
    public string ApprovalId { get; set; } = string.Empty;

    public CadApprovalDecisionKind Decision { get; set; }

    public string DecidedAtUtc { get; set; } = string.Empty;
}

public sealed class CadChangeSet
{
    public string ChangeSetId { get; set; } = string.Empty;

    public string BatchId { get; set; } = string.Empty;

    public string TransactionId { get; set; } = string.Empty;

    public string UndoMark { get; set; } = string.Empty;

    public string RecoverySnapshotPathHash { get; set; } = string.Empty;

    public CadApprovalState FinalState { get; set; }
}
