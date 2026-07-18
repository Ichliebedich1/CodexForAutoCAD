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

    /// <summary>
    /// 只读、显示用途的白名单属性。宿主按图元类型生成，绝不包含可执行表达式或任意 API 名称。
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CadSelectionSnapshot
{
    public string SnapshotHash { get; set; } = string.Empty;

    public CadEntityRef[] Entities { get; set; } = new CadEntityRef[0];

    public string CapturedAtUtc { get; set; } = string.Empty;
}

public sealed class CadContextEnvelope
{
    public int ProtocolVersion { get; set; } = ProtocolConstants.CurrentVersion;

    public CadDocumentRef Document { get; set; } = new();

    public CadSelectionSnapshot Selection { get; set; } = new();

    public string Units { get; set; } = string.Empty;

    public string UcsName { get; set; } = string.Empty;

    public string[] Provenance { get; set; } = new string[0];

    public CadRiskLevel EgressRisk { get; set; } = CadRiskLevel.ContextEgress;
}
