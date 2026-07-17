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
    IReadOnlyList<string>? standardErrorTail = null) : EventArgs
{
    public int? ExitCode { get; } = exitCode;

    public bool Expected { get; } = expected;

    public IReadOnlyList<string> StandardErrorTail { get; }
        = standardErrorTail ?? Array.Empty<string>();
}

public sealed class AppServerStandardErrorEventArgs(string line) : EventArgs
{
    public string Line { get; } = line;
}
