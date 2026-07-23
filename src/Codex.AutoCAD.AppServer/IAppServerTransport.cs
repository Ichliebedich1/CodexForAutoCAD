namespace Codex.AutoCAD.AppServer;

/// <summary>Bidirectional byte transport: App Server stdout is read, stdin is written.</summary>
public interface IAppServerTransport : IAsyncDisposable
{
    Stream ReadStream { get; }

    Stream WriteStream { get; }

    bool IsRunning { get; }

    event EventHandler<AppServerTransportExitedEventArgs>? Exited;

    event EventHandler<AppServerStandardErrorEventArgs>? StandardErrorReceived;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default);
}

public sealed class AppServerTransportExitedEventArgs(
    int? exitCode,
    bool expected,
    IReadOnlyList<AppServerStandardErrorSummary>? standardErrorTail = null) : EventArgs
{
    public int? ExitCode { get; } = exitCode;

    public bool Expected { get; } = expected;

    public IReadOnlyList<AppServerStandardErrorSummary> StandardErrorTail { get; }
        = standardErrorTail ?? Array.Empty<AppServerStandardErrorSummary>();
}

public sealed class AppServerStandardErrorEventArgs(
    AppServerStandardErrorSummary summary) : EventArgs
{
    public AppServerStandardErrorSummary Summary { get; }
        = summary ?? throw new ArgumentNullException(nameof(summary));
}
