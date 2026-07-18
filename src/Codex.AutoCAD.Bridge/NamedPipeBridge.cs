using System.IO.Pipes;
using System.Security.Cryptography;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.Bridge;

public static class NamedPipeBridge
{
    public const int MaximumConnectionsPerSession = 1;
    private const PipeOptions SecurePipeOptions = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    public static async Task<AuthenticatedPipeConnection> AcceptOneAsync(
        string pipeName,
        string sessionId,
        ReadOnlyMemory<byte> sessionSecret,
        CancellationToken cancellationToken = default,
        BridgeConnectionOptions? options = null)
    {
        ValidatePipeName(pipeName);
        ValidateBootstrap(sessionId, sessionSecret);
        options ??= new BridgeConnectionOptions();
        options.Validate();
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            MaximumConnectionsPerSession,
            PipeTransmissionMode.Byte,
            SecurePipeOptions);
        var bootstrapSecret = sessionSecret.ToArray();

        try
        {
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            return new AuthenticatedPipeConnection(pipe, sessionId, bootstrapSecret, options);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bootstrapSecret);
        }
    }

    public static async Task<AuthenticatedPipeConnection> ConnectAsync(
        string pipeName,
        string sessionId,
        ReadOnlyMemory<byte> sessionSecret,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        BridgeConnectionOptions? options = null)
    {
        ValidatePipeName(pipeName);
        ValidateBootstrap(sessionId, sessionSecret);
        options ??= new BridgeConnectionOptions();
        options.Validate();
        if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "连接超时必须为正数或无限。" );
        }

        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            SecurePipeOptions);
        var bootstrapSecret = sessionSecret.ToArray();

        try
        {
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            await pipe.ConnectAsync(timeoutCancellation.Token).ConfigureAwait(false);
            return new AuthenticatedPipeConnection(pipe, sessionId, bootstrapSecret, options);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bootstrapSecret);
        }
    }

    private static void ValidatePipeName(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > 200)
        {
            throw new ArgumentException("命名管道名称不能为空且不能超过200个字符。", nameof(pipeName));
        }

        if (pipeName.IndexOfAny(['\\', '/']) >= 0)
        {
            throw new ArgumentException("命名管道名称不能包含路径分隔符。", nameof(pipeName));
        }
    }

    private static void ValidateBootstrap(string sessionId, ReadOnlyMemory<byte> sessionSecret)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId不能为空。", nameof(sessionId));
        }

        if (sessionSecret.Length != IpcSessionSecret.SizeInBytes)
        {
            throw new ArgumentException("桥接会话密钥必须恰好为256位。", nameof(sessionSecret));
        }
    }
}
