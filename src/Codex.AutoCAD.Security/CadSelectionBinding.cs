namespace Codex.AutoCAD.Security;

/// <summary>审批绑定使用的显式选择语义，避免把旧哈希直接复制成“重验结果”。</summary>
public static class CadSelectionBinding
{
    public static string NoSelectionSnapshotHash { get; } =
        SecurityHash.ComputeSha256Hex("codex-autocad:no-selection:v1");
}
