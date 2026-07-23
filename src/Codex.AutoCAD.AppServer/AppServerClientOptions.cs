using Codex.AutoCAD.AppServer.Protocol;

namespace Codex.AutoCAD.AppServer;

/// <summary>Controls the local Codex App Server process and JSONL connection.</summary>
public sealed record AppServerClientOptions
{
    public string CodexExecutablePath { get; init; } = "codex";

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> AdditionalArguments { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string?> Environment { get; init; }
        = new Dictionary<string, string?>();

    /// <summary>
    /// Maximum number of Codex stderr bytes counted in bounded diagnostics. The stream is still
    /// fully drained after this limit, but its text is never retained or forwarded.
    /// </summary>
    public int MaximumStandardErrorBytes { get; init; } = 16 * 1024;

    public AppServerClientInfo ClientInfo { get; init; }
        = new("codex_autocad", "Codex for AutoCAD", "0.1.0");

    public AppServerInitializeCapabilities Capabilities { get; init; }
        = new(ExperimentalApi: true);

    public int MaximumFrameBytes { get; init; } = 8 * 1024 * 1024;

    public int MaximumJsonDepth { get; init; } = 64;

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(CodexExecutablePath))
        {
            throw new ArgumentException("Codex executable path cannot be empty.", nameof(CodexExecutablePath));
        }

        if (MaximumFrameBytes < 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumFrameBytes), "Frame limit must be at least 1024 bytes.");
        }

        if (MaximumJsonDepth is < 8 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumJsonDepth), "JSON depth must be between 8 and 256.");
        }

        if (MaximumStandardErrorBytes is < 1_024 or > 1_048_576)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumStandardErrorBytes),
                "Standard error limit must be between 1024 and 1048576 bytes.");
        }

        if (ShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout));
        }
    }
}
