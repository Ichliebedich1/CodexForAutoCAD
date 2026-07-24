using Codex.AutoCAD.AppServer;

namespace Codex.AutoCAD.AgentHost;

internal static class AgentHostCodexHealthCheck
{
    internal static async Task<T> StartAsync<T>(
        Func<CancellationToken, Task<T>> start,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            return await start(deadline.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new AgentHostCodexHealthException(
                AgentHostCodexHealthFailure.AppServerHandshakeTimedOut,
                "The local Codex App Server handshake timed out.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new AgentHostCodexHealthException(
                AgentHostCodexHealthFailure.AppServerHandshakeTimedOut,
                "The local Codex App Server handshake timed out.");
        }
        catch (Exception exception) when (exception is AppServerException
                                          or IOException
                                          or InvalidOperationException)
        {
            throw new AgentHostCodexHealthException(
                AgentHostCodexHealthFailure.AppServerHandshakeFailed,
                "The local Codex App Server handshake failed.");
        }
    }

    internal static async Task StartAsync(
        Func<CancellationToken, Task> start,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = await StartAsync(
                async token =>
                {
                    await start(token).ConfigureAwait(false);
                    return true;
                },
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
