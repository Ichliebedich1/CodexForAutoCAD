using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AppServer.Protocol;

namespace Codex.AutoCAD.AgentHost;

/// <summary>
/// Public, path-free AgentHost health output. Raw App Server initialization metadata remains
/// process-local and is reduced to booleans before the status crosses stdout.
/// </summary>
internal sealed class AgentHostPublicStatus
{
    private AgentHostPublicStatus(
        string state,
        CodexExecutableSource executableSource,
        CodexVersionPreflightResult version,
        AppServerInitializeResponse initialized)
    {
        State = state;
        WorkspaceReady = true;
        CodexExecutableSource = executableSource.ToString();
        CodexVersion = version.Version.ToString();
        CodexVersionCompatibility = version.Compatibility.ToString();
        CodexHomeConfigured = !string.IsNullOrWhiteSpace(initialized.CodexHome);
        PlatformCompatible =
            string.Equals(initialized.PlatformFamily, "windows", StringComparison.OrdinalIgnoreCase)
            && string.Equals(initialized.PlatformOs, "windows", StringComparison.OrdinalIgnoreCase);
        Sandbox = new AgentHostPublicSandboxStatus();
    }

    public bool Ok => true;

    public string State { get; }

    public bool WorkspaceReady { get; }

    public string CodexExecutableSource { get; }

    public string CodexVersion { get; }

    public string CodexVersionCompatibility { get; }

    public bool CodexHomeConfigured { get; }

    public bool PlatformCompatible { get; }

    public AgentHostPublicSandboxStatus Sandbox { get; }

    internal static AgentHostPublicStatus CreateDoctor(
        AppServerClientState state,
        CodexExecutableSource executableSource,
        CodexVersionPreflightResult version,
        AppServerInitializeResponse initialized)
    {
        if (state != AppServerClientState.Running)
        {
            throw new InvalidOperationException(
                "The AgentHost doctor status requires a running App Server.");
        }

        return new AgentHostPublicStatus(
            state.ToString(),
            executableSource,
            version ?? throw new ArgumentNullException(nameof(version)),
            initialized ?? throw new ArgumentNullException(nameof(initialized)));
    }

    internal static AgentHostPublicStatus CreateReady(
        CodexExecutableSource executableSource,
        CodexVersionPreflightResult version,
        AppServerInitializeResponse initialized)
        => new(
            "ready",
            executableSource,
            version ?? throw new ArgumentNullException(nameof(version)),
            initialized ?? throw new ArgumentNullException(nameof(initialized)));
}

internal sealed class AgentHostPublicSandboxStatus
{
    public string Mode => "workspace-write";

    public string Approvals => "on-request";

    public bool CadSessionApproval => false;
}
