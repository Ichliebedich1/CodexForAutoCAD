using System.Diagnostics;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.AppServer;

namespace Codex.AutoCAD.AgentHost;

/// <summary>
/// Performs the one-use local Codex login without placing the access token in argv, the
/// environment, or a managed string. The caller owns the secret lifetime.
/// </summary>
internal static class CodexCredentialLogin
{
    internal static async Task LoginAsync(
        CodexLocalAppServerConfiguration configuration,
        string sessionHomePath,
        AgentHostCredentialSecret secret,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionHomePath);
        ArgumentNullException.ThrowIfNull(secret);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw Failure("Codex access-token login timeout is invalid.");
        }

        var options = configuration.CreateClientOptions();
        var authPath = Path.Combine(sessionHomePath, "auth.json");
        if (File.Exists(authPath))
        {
            throw Failure("The isolated Codex home already contains auth.json.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.CodexExecutablePath,
                WorkingDirectory = options.WorkingDirectory
                    ?? throw Failure("Codex login working directory is unavailable."),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add("login");
        process.StartInfo.ArgumentList.Add("--with-access-token");
        process.StartInfo.Environment.Clear();
        foreach (var (name, value) in options.Environment)
        {
            if (value is not null)
            {
                process.StartInfo.Environment[name] = value;
            }
        }

        if (!process.Start())
        {
            throw Failure("Codex access-token login process could not start.");
        }

        var outputTask = DrainAsync(process.StandardOutput);
        var errorTask = DrainAsync(process.StandardError);
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            secret.WriteTo(process.StandardInput.BaseStream);
            process.StandardInput.BaseStream.WriteByte((byte)'\n');
            process.StandardInput.BaseStream.Flush();
            process.StandardInput.Close();

            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw Failure("Codex access-token login returned a failure status.");
            }

            if (File.Exists(authPath))
            {
                throw Failure("Codex access-token login created auth.json.");
            }
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            TryKill(process);
            await DrainAfterTerminationAsync(process, outputTask, errorTask).ConfigureAwait(false);
            throw Failure(
                cancellationToken.IsCancellationRequested
                    ? "Codex access-token login was cancelled."
                    : "Codex access-token login timed out.");
        }
        catch (AgentBootstrapLaunchException)
        {
            TryKill(process);
            await DrainAfterTerminationAsync(process, outputTask, errorTask).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            TryKill(process);
            await DrainAfterTerminationAsync(process, outputTask, errorTask).ConfigureAwait(false);
            throw Failure("Codex access-token login failed.", exception);
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                Array.Clear(buffer, 0, read);
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
    }

    private static async Task DrainAfterTerminationAsync(
        Process process,
        Task outputTask,
        Task errorTask)
    {
        try
        {
            if (!process.HasExited)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try
        {
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static AgentBootstrapLaunchException Failure(
        string message,
        Exception? inner = null)
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.CredentialUnavailable,
            message,
            inner);
    }
}
