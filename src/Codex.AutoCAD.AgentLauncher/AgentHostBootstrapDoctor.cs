using System.Security.Cryptography;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.AgentLauncher;

public static class AgentHostBootstrapDoctor
{
    private const int IdentifierBytes = 16;
    private const string PipeNamePrefix = "codex-autocad-";
    internal static readonly SemaphoreSlim LaunchGate = new SemaphoreSlim(1, 1);

    public static Task<AgentBootstrapDoctorResult> RunAsync(
        AgentHostBootstrapOptions options,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return RunSupervisedAsync(options, cancellationToken);
    }

    private static async Task<AgentBootstrapDoctorResult> RunSupervisedAsync(
        AgentHostBootstrapOptions options,
        CancellationToken cancellationToken)
    {
        var controller = new AgentBootstrapDeadlineController(
            options.GetValidatedStartupTimeout(),
            cancellationToken);
        Task<AgentBootstrapDoctorResult> workerTask;
        try
        {
            workerTask = Task.Run(() => RunWorkerAsync(options, controller));
        }
        catch
        {
            controller.PrepareForWorkerExit();
            throw;
        }

        var completed = await Task.WhenAny(workerTask, controller.AbortCompletion)
            .ConfigureAwait(false);
        if (completed == workerTask)
        {
            return await workerTask.ConfigureAwait(false);
        }

        ObserveFault(workerTask);
        throw controller.GetTerminalFailure();
    }

    private static async Task<AgentBootstrapDoctorResult> RunWorkerAsync(
        AgentHostBootstrapOptions options,
        AgentBootstrapDeadlineController controller)
    {
        var gateHeld = false;
        try
        {
            try
            {
                LaunchGate.Wait(controller.AbortToken);
            }
            catch (OperationCanceledException)
                when (controller.IsAbortRequested)
            {
                throw controller.GetTerminalFailure();
            }
            gateHeld = true;
            controller.Checkpoint();
            AgentBootstrapLateFailureRegistry.ThrowIfPoisoned();
            return await RunCoreAsync(options, controller).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                controller.PrepareForWorkerExit();
            }
            finally
            {
                if (gateHeld)
                {
                    LaunchGate.Release();
                }
            }
        }
    }

    private static async Task<AgentBootstrapDoctorResult> RunCoreAsync(
        AgentHostBootstrapOptions options,
        AgentBootstrapDeadlineController controller)
    {
        controller.Checkpoint();
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.InvalidConfiguration,
                "AgentHost inherited-handle bootstrap is supported only on Windows.");
        }

        var executableIdentity = options.GetValidatedExecutableIdentity();
        var processTreeLimits = options.GetValidatedProcessTreeLimits();
        controller.Checkpoint();
        var sessionId = CreateRandomIdentifier();
        var pipeName = PipeNamePrefix + CreateRandomIdentifier();
        using (var payload = AgentBootstrapPayload.CreateRandom(sessionId, pipeName))
        {
            var bootstrapId = payload.CopyBootstrapId();
            WindowsInheritedBootstrapProcess? child = null;
            Task<StandardErrorCapture>? standardErrorTask = null;
            try
            {
                try
                {
                    controller.BeginStartAttempt();
                    child = WindowsInheritedBootstrapProcess.Start(
                        executableIdentity,
                        processTreeLimits,
                        controller.Checkpoint);
                    controller.PublishSuspended(child);
                }
                catch (AgentBootstrapLaunchException)
                {
                    controller.EndStartAttempt();
                    throw;
                }
                catch (Exception exception)
                {
                    controller.EndStartAttempt();
                    throw new AgentBootstrapLaunchException(
                        AgentBootstrapLaunchFailure.ProcessStartFailed,
                        "Starting AgentHost failed.",
                        exception);
                }

                standardErrorTask = CaptureStandardErrorAsync(
                    child.StandardErrorInput,
                    options.MaximumStandardErrorBytes);

                try
                {
                    AgentBootstrapInheritedChannel.WriteSingleBootstrapPacket(
                        child.BootstrapOutput,
                        payload);
                }
                catch (Exception exception)
                {
                    if (controller.IsAbortRequested)
                    {
                        throw controller.GetTerminalFailure();
                    }

                    int exitCode;
                    if (child.WaitForExit(1000, out exitCode) && exitCode != 0)
                    {
                        throw new AgentBootstrapLaunchException(
                            AgentBootstrapLaunchFailure.ChildExitedWithError,
                            "AgentHost exited during bootstrap write with code "
                            + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + ".",
                            exception);
                    }

                    throw new AgentBootstrapLaunchException(
                        AgentBootstrapLaunchFailure.BootstrapWriteFailed,
                        "Writing the one-use AgentHost bootstrap packet failed.",
                        exception);
                }
                finally
                {
                    child.CloseBootstrapOutput();
                }

                try
                {
                    controller.ResumePublished(child);
                }
                catch (AgentBootstrapLaunchException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new AgentBootstrapLaunchException(
                        AgentBootstrapLaunchFailure.ProcessStartFailed,
                        "Resuming the validated AgentHost failed.",
                        exception);
                }

                using (var hostKeys = payload.DeriveDirectionKeys())
                using (var incomingGuard = hostKeys.CreateConfirmationInboundGuard())
                {
                    var confirmationTask = Task.Run(
                        () => AgentBootstrapConfirmationProtocol.ReadSingleFrame(
                            child.ConfirmationInput));
                    var confirmation = await WaitForConfirmationAsync(
                            child,
                            confirmationTask,
                            controller)
                        .ConfigureAwait(false);

                    var exitCode = await WaitForExitAsync(child, controller)
                        .ConfigureAwait(false);
                    var standardError = await WaitForStandardErrorAsync(
                            standardErrorTask,
                            controller)
                        .ConfigureAwait(false);
                    if (exitCode != 0)
                    {
                        throw new AgentBootstrapLaunchException(
                            AgentBootstrapLaunchFailure.ChildExitedWithError,
                            "AgentHost bootstrap doctor exited with code "
                            + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + FormatStandardErrorSummary(standardError));
                    }

                    var result = AgentBootstrapConfirmationProtocol.ValidateHostConfirmation(
                        confirmation,
                        incomingGuard,
                        bootstrapId,
                        sessionId,
                        pipeName,
                        child.ProcessId,
                        child.ProcessCreationFileTime,
                        child.ExecutableSha256,
                        standardError.Bytes,
                        standardError.Truncated);
                    child.MarkConfirmed();
                    controller.CommitSuccess(child);
                    return result;
                }
            }
            catch (AgentBootstrapLaunchException exception)
            {
                if (child != null)
                {
                    var terminal = await controller.AbortForFailureAsync(child, exception)
                        .ConfigureAwait(false);
                    throw terminal;
                }

                if (controller.IsAbortRequested)
                {
                    await controller.AbortCompletion.ConfigureAwait(false);
                    var controllerFailure = controller.GetTerminalFailure();
                    if (exception.Failure == AgentBootstrapLaunchFailure.ChildTerminationFailed
                        && !ReferenceEquals(exception, controllerFailure))
                    {
                        throw;
                    }

                    throw controllerFailure;
                }

                throw;
            }
            catch (Exception exception)
            {
                var wrapped = new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ConfirmationInvalid,
                    "AgentHost bootstrap failed closed.",
                    exception);
                if (child != null)
                {
                    var terminal = await controller.AbortForFailureAsync(child, wrapped)
                        .ConfigureAwait(false);
                    throw terminal;
                }

                if (controller.IsAbortRequested)
                {
                    await controller.AbortCompletion.ConfigureAwait(false);
                    throw controller.GetTerminalFailure();
                }

                throw wrapped;
            }
            finally
            {
                Array.Clear(bootstrapId, 0, bootstrapId.Length);
                if (standardErrorTask != null && !standardErrorTask.IsCompleted)
                {
                    ObserveFault(standardErrorTask);
                }

                child?.Dispose();
            }
        }
    }

    private static async Task<IpcEnvelope> WaitForConfirmationAsync(
        WindowsInheritedBootstrapProcess child,
        Task<IpcEnvelope> confirmationTask,
        AgentBootstrapDeadlineController controller)
    {
        var completed = await Task.WhenAny(confirmationTask, controller.AbortCompletion)
            .ConfigureAwait(false);
        if (completed == controller.AbortCompletion || controller.IsAbortRequested)
        {
            child.ConfirmationInput.Dispose();
            ObserveFault(confirmationTask);
            await controller.AbortCompletion.ConfigureAwait(false);
            throw controller.GetTerminalFailure();
        }

        try
        {
            var confirmation = await confirmationTask.ConfigureAwait(false);
            controller.Checkpoint();
            return confirmation;
        }
        catch (AgentBootstrapLaunchException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (controller.IsAbortRequested)
            {
                await controller.AbortCompletion.ConfigureAwait(false);
                throw controller.GetTerminalFailure();
            }

            int exitCode;
            if (child.WaitForExit(1000, out exitCode) && exitCode != 0)
            {
                throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ChildExitedWithError,
                    "AgentHost exited before confirmation with code "
                    + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ".",
                    exception);
            }

            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ConfirmationInvalid,
                "AgentHost confirmation was missing, truncated, duplicated, or malformed.",
                exception);
        }
    }

    private static async Task<int> WaitForExitAsync(
        WindowsInheritedBootstrapProcess child,
        AgentBootstrapDeadlineController controller)
    {
        while (true)
        {
            controller.Checkpoint();
            int exitCode;
            if (child.WaitForExit(0, out exitCode))
            {
                return exitCode;
            }

            var remaining = controller.GetRemainingOrThrow();
            var delay = remaining < TimeSpan.FromMilliseconds(25)
                ? remaining
                : TimeSpan.FromMilliseconds(25);
            var completed = await Task.WhenAny(
                    Task.Delay(delay),
                    controller.AbortCompletion)
                .ConfigureAwait(false);
            if (completed == controller.AbortCompletion)
            {
                throw controller.GetTerminalFailure();
            }
        }
    }

    private static async Task<StandardErrorCapture> WaitForStandardErrorAsync(
        Task<StandardErrorCapture> standardErrorTask,
        AgentBootstrapDeadlineController controller)
    {
        var completed = await Task.WhenAny(standardErrorTask, controller.AbortCompletion)
            .ConfigureAwait(false);
        if (completed == controller.AbortCompletion || controller.IsAbortRequested)
        {
            ObserveFault(standardErrorTask);
            await controller.AbortCompletion.ConfigureAwait(false);
            throw controller.GetTerminalFailure();
        }

        var result = await standardErrorTask.ConfigureAwait(false);
        controller.Checkpoint();
        return result;
    }

    private static Task<StandardErrorCapture> CaptureStandardErrorAsync(
        Stream input,
        int maximumBytes)
    {
        return Task.Run(() =>
        {
            var buffer = new byte[4096];
            var capturedBytes = 0;
            var truncated = false;
            try
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    var remaining = maximumBytes - capturedBytes;
                    if (remaining > 0)
                    {
                        capturedBytes += Math.Min(read, remaining);
                    }
                    if (read > remaining)
                    {
                        truncated = true;
                    }

                    Array.Clear(buffer, 0, read);
                }

                return new StandardErrorCapture(capturedBytes, truncated);
            }
            finally
            {
                Array.Clear(buffer, 0, buffer.Length);
            }
        });
    }

    private static string CreateRandomIdentifier()
    {
        var bytes = new byte[IdentifierBytes];
        try
        {
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return AgentBootstrapConfirmationProtocol.FormatLowerHex(bytes);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static string FormatStandardErrorSummary(StandardErrorCapture standardError)
    {
        return ". stderrBytes="
            + standardError.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ", stderrTruncated="
            + (standardError.Truncated ? "true" : "false")
            + ".";
    }

    private static void ObserveFault(Task task)
    {
        task.ContinueWith(
            completed =>
            {
                var ignored = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed class StandardErrorCapture
    {
        internal StandardErrorCapture(int bytes, bool truncated)
        {
            Bytes = bytes;
            Truncated = truncated;
        }

        internal int Bytes { get; }

        internal bool Truncated { get; }
    }
}
