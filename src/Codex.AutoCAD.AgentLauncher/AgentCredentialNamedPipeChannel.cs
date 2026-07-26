using System.IO.Pipes;
#if NETFRAMEWORK
using System.Security.AccessControl;
using System.Security.Principal;
#endif
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.AgentLauncher;

public sealed class AgentCredentialPipeServer : IDisposable
{
    private const string PipeSuffix = "-credential";
    private NamedPipeServerStream? pipe;

    private AgentCredentialPipeServer(NamedPipeServerStream pipe)
    {
        this.pipe = pipe;
    }

    public static AgentCredentialPipeServer Create(string bootstrapPipeName)
    {
        return new AgentCredentialPipeServer(CreateServer(GetPipeName(bootstrapPipeName)));
    }

    public async Task DeliverAsync(
        string sessionId,
        byte[] bootstrapId,
        int processId,
        long processCreationFileTime,
        AgentCredentialDeliveryMode mode,
        AgentHostCredentialSecret? secret,
        IpcEnvelopeAuthenticator authenticator,
        CancellationToken cancellationToken)
    {
        var current = pipe;
        if (current == null)
        {
            throw Failure("Credential pipe server is unavailable.");
        }

        try
        {
#if NETFRAMEWORK
            using (cancellationToken.Register(() =>
            {
                try { current.Dispose(); }
                catch { }
            }))
            {
                await Task.Factory.FromAsync(
                    current.BeginWaitForConnection,
                    current.EndWaitForConnection,
                    null).ConfigureAwait(false);
            }
#else
            await current.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
#endif
            AgentCredentialDeliveryProtocol.WriteSingleFrame(
                current,
                sessionId,
                bootstrapId,
                processId,
                processCreationFileTime,
                mode,
                secret,
                authenticator);
            // The receiver validates the single frame by requiring EOF. This channel is
            // deliberately one-shot, so close the server side immediately after the frame.
            current.Dispose();
            Interlocked.CompareExchange(ref pipe, null, current);
        }
        catch (AgentBootstrapLaunchException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure("Credential pipe delivery failed.", exception);
        }
    }

    public void Dispose()
    {
        var current = Interlocked.Exchange(ref pipe, null);
        current?.Dispose();
    }

    public static string GetPipeName(string bootstrapPipeName)
    {
        if (string.IsNullOrWhiteSpace(bootstrapPipeName)
            || bootstrapPipeName.Length > 180
            || bootstrapPipeName.IndexOf('\\') >= 0
            || bootstrapPipeName.IndexOf('/') >= 0)
        {
            throw Failure("Credential bootstrap pipe name is invalid.");
        }

        return bootstrapPipeName + PipeSuffix;
    }

    private static NamedPipeServerStream CreateServer(string pipeName)
    {
#if NETFRAMEWORK
        var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User;
        if (userSid == null)
        {
            throw Failure("Credential pipe owner identity is unavailable.");
        }

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(
            new PipeAccessRule(
                userSid,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            4096,
            security);
#else
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
#endif
    }

    private static AgentBootstrapLaunchException Failure(
        string unsafeDiagnostic,
        Exception? unsafeException = null)
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.CredentialUnavailable,
            unsafeDiagnostic,
            unsafeException);
    }
}

public static class AgentCredentialPipeClient
{
    public static async Task<AgentCredentialDelivery> ReceiveAsync(
        string bootstrapPipeName,
        string sessionId,
        byte[] bootstrapId,
        int processId,
        long processCreationFileTime,
        IpcSessionGuard incomingGuard,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
#if NETFRAMEWORK
        await Task.Yield();
        throw new PlatformNotSupportedException(
            "Credential pipe client is hosted only by the net8 AgentHost.");
#else
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var pipeName = AgentCredentialPipeServer.GetPipeName(bootstrapPipeName);
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.In,
            PipeOptions.Asynchronous);
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await pipe.ConnectAsync(timeoutCancellation.Token).ConfigureAwait(false);
            return AgentCredentialDeliveryProtocol.ReadSingleFrame(
                pipe,
                sessionId,
                bootstrapId,
                processId,
                processCreationFileTime,
                incomingGuard);
        }
        catch (AgentBootstrapLaunchException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.CredentialUnavailable,
                "Credential pipe receive failed.",
                exception);
        }
#endif
    }
}
