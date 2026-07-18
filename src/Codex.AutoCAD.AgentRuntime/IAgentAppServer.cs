using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AppServer.Protocol;

namespace Codex.AutoCAD.AgentRuntime;

/// <summary>
/// Narrow App Server seam used by the conversation runtime. Keeping this seam small makes the
/// orchestration and event projection independently testable without launching Codex.
/// </summary>
public interface IAgentAppServer : IAsyncDisposable
{
    event EventHandler<AppServerNotification>? NotificationReceived;

    event CommandApprovalRequestedHandler? CommandApprovalRequested;

    event FileChangeApprovalRequestedHandler? FileChangeApprovalRequested;

    event PermissionsApprovalRequestedHandler? PermissionsApprovalRequested;

    event CadApprovalRequestedHandler? CadApprovalRequested;

    event ServerRequestReceivedHandler? ServerRequestReceived;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task<TResult> SendRequestAsync<TResult>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Adapts the repository's JSONL App Server client to <see cref="IAgentAppServer"/>.</summary>
public sealed class CodexAppServerAdapter : IAgentAppServer
{
    private readonly CodexAppServerClient _client;
    private readonly bool _ownsClient;
    private int _disposed;

    public CodexAppServerAdapter(CodexAppServerClient client, bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _ownsClient = ownsClient;
    }

    public event EventHandler<AppServerNotification>? NotificationReceived
    {
        add => _client.NotificationReceived += value;
        remove => _client.NotificationReceived -= value;
    }

    public event CommandApprovalRequestedHandler? CommandApprovalRequested
    {
        add => _client.CommandApprovalRequested += value;
        remove => _client.CommandApprovalRequested -= value;
    }

    public event FileChangeApprovalRequestedHandler? FileChangeApprovalRequested
    {
        add => _client.FileChangeApprovalRequested += value;
        remove => _client.FileChangeApprovalRequested -= value;
    }

    public event PermissionsApprovalRequestedHandler? PermissionsApprovalRequested
    {
        add => _client.PermissionsApprovalRequested += value;
        remove => _client.PermissionsApprovalRequested -= value;
    }

    public event CadApprovalRequestedHandler? CadApprovalRequested
    {
        add => _client.CadApprovalRequested += value;
        remove => _client.CadApprovalRequested -= value;
    }

    public event ServerRequestReceivedHandler? ServerRequestReceived
    {
        add => _client.ServerRequestReceived += value;
        remove => _client.ServerRequestReceived -= value;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _ = await _client.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<TResult> SendRequestAsync<TResult>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _client.SendRequestAsync<TResult>(method, parameters, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsClient)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
