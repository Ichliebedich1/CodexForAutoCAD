namespace Codex.AutoCAD.Contracts;

public sealed class CadDocumentRef
{
    public string DocumentId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PathHash { get; set; } = string.Empty;

    public string DrawingFingerprint { get; set; } = string.Empty;

    public long Revision { get; set; }

    public string CurrentSpace { get; set; } = string.Empty;

    public string DrawingVersion { get; set; } = string.Empty;
}

public sealed class CadEntityRef
{
    public string Handle { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string StateHash { get; set; } = string.Empty;

    public string Layer { get; set; } = string.Empty;

    public CadExtents3? Extents { get; set; }
}

public sealed class CadSelectionSnapshot
{
    public string SnapshotHash { get; set; } = string.Empty;

    public CadEntityRef[] Entities { get; set; } = Array.Empty<CadEntityRef>();

    public string CapturedAtUtc { get; set; } = string.Empty;
}

public sealed class CadContextEnvelope
{
    public int ProtocolVersion { get; set; } = ProtocolConstants.CurrentVersion;

    public CadDocumentRef Document { get; set; } = new();

    public CadSelectionSnapshot Selection { get; set; } = new();

    public string Units { get; set; } = string.Empty;

    public string UcsName { get; set; } = string.Empty;

    public string[] Provenance { get; set; } = Array.Empty<string>();

    public CadRiskLevel EgressRisk { get; set; } = CadRiskLevel.ContextEgress;
}
