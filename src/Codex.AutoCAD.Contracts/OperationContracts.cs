namespace Codex.AutoCAD.Contracts;

public abstract class CadOperation
{
    public string OperationId { get; set; } = string.Empty;

    public abstract string Kind { get; }

    public abstract CadRiskLevel MinimumRisk { get; }
}

public sealed class CreateLineOperation : CadOperation
{
    public override string Kind => "create_line";

    public override CadRiskLevel MinimumRisk => CadRiskLevel.ReversibleWrite;

    public CadPoint3 Start { get; set; } = new();

    public CadPoint3 End { get; set; } = new();

    public string Layer { get; set; } = "0";
}

public sealed class EraseEntitiesOperation : CadOperation
{
    public override string Kind => "erase_entities";

    public override CadRiskLevel MinimumRisk => CadRiskLevel.DestructiveWrite;

    public string[] Handles { get; set; } = Array.Empty<string>();
}

public sealed class TransformEntitiesOperation : CadOperation
{
    public override string Kind => "transform_entities";

    public override CadRiskLevel MinimumRisk => CadRiskLevel.ReversibleWrite;

    public string[] Handles { get; set; } = Array.Empty<string>();

    public CadPoint3 Translation { get; set; } = new();

    public double RotationRadians { get; set; }

    public double UniformScale { get; set; } = 1d;
}

public sealed class CadOperationBatch
{
    public int ProtocolVersion { get; set; } = ProtocolConstants.CurrentVersion;

    public string BatchId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public CadDocumentRef Document { get; set; } = new();

    public string SelectionSnapshotHash { get; set; } = string.Empty;

    public CadRiskLevel DeclaredRisk { get; set; } = CadRiskLevel.ReversibleWrite;

    public CadOperation[] Operations { get; set; } = Array.Empty<CadOperation>();
}

public sealed class CadOperationDiff
{
    public int CreatedCount { get; set; }

    public int ModifiedCount { get; set; }

    public int ErasedCount { get; set; }

    public CadExtents3? AffectedExtents { get; set; }

    public string[] PartialPreviewReasons { get; set; } = Array.Empty<string>();
}

public sealed class CadPreviewResult
{
    public string PreviewId { get; set; } = string.Empty;

    public string NormalizedPlanHash { get; set; } = string.Empty;

    public CadOperationDiff Diff { get; set; } = new();

    public CadRiskLevel EffectiveRisk { get; set; }

    public bool IsComplete { get; set; }
}
