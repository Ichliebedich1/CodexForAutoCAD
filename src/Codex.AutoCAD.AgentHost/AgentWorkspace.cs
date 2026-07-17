namespace Codex.AutoCAD.AgentHost;

public sealed class AgentWorkspace
{
    private AgentWorkspace(string root)
    {
        Root = root;
        Inputs = Path.Combine(root, "inputs");
        Work = Path.Combine(root, "work");
        Outputs = Path.Combine(root, "outputs");
        Temp = Path.Combine(root, "temp");
    }

    public string Root { get; }

    public string Inputs { get; }

    public string Work { get; }

    public string Outputs { get; }

    public string Temp { get; }

    public static AgentWorkspace Create(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("Agent工作区必须使用绝对路径。", nameof(root));
        }

        if (root.StartsWith("\\\\", StringComparison.Ordinal)
            || root.StartsWith("\\\\?\\", StringComparison.Ordinal)
            || root.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("Agent工作区不能位于UNC或设备路径。", nameof(root));
        }

        var workspace = new AgentWorkspace(Path.GetFullPath(root));
        Directory.CreateDirectory(workspace.Inputs);
        Directory.CreateDirectory(workspace.Work);
        Directory.CreateDirectory(workspace.Outputs);
        Directory.CreateDirectory(workspace.Temp);
        return workspace;
    }
}
