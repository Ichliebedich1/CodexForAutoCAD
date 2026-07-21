using System.Diagnostics;
using System.Security.Cryptography;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.AgentLauncher;

public static class AgentHostBootstrapService
{
    private const int IdentifierBytes = 16;
    private const string PipeNamePrefix = "codex-autocad-";

    public static Task<AgentHostServiceSession> StartAsync(
        AgentHostBootstrapOptions options,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return RunSupervisedAsync(options, cancellationToken);
    }

    private static async Task<AgentHostServiceSession> RunSupervisedAsync(
        AgentHostBootstrapOptions options,
        CancellationToken cancellationToken)
    {
        var controller = new AgentBootstrapDeadlineController(
            options.GetValidatedStartupTimeout(),
            cancellationToken);
        Task<AgentHostServiceSession> workerTask;
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

    private static async Task<AgentHostServiceSession> RunWorkerAsync(
        AgentHostBootstrapOptions options,
        AgentBootstrapDeadlineController controller)
    {
        var gateHeld = false;
        try
        {
            try
            {
                AgentHostBootstrapDoctor.LaunchGate.Wait(controller.AbortToken);
            }
            catch (OperationCanceledException) when (controller.IsAbortRequested)
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
                    AgentHostBootstrapDoctor.LaunchGate.Release();
                }
            }
        }
    }

    private static async Task<AgentHostServiceSession> RunCoreAsync(
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
        controller.Checkpoint();
        var sessionId = CreateRandomIdentifier();
        var pipeName = PipeNamePrefix + CreateRandomIdentifier();
        using (var payload = AgentBootstrapPayload.CreateRandom(sessionId, pipeName))
        {
            var bootstrapId = payload.CopyBootstrapId();
            WindowsInheritedBootstrapProcess? child = null;
            AgentBootstrapDirectionKeys? hostKeys = null;
            Task<AgentHostStandardErrorCapture>? standardErrorTask = null;
            try
            {
                try
                {
                    controller.BeginStartAttempt();
                    child = WindowsInheritedBootstrapProcess.Start(
                        executableIdentity,
                        AgentHostBootstrapCommand.Serve,
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
                        "Starting AgentHost service failed.",
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
                            "AgentHost service exited during bootstrap write with code "
                            + exitCode.ToString(
                                System.Globalization.CultureInfo.InvariantCulture)
                            + ".",
                            exception);
                    }

                    throw new AgentBootstrapLaunchException(
                        AgentBootstrapLaunchFailure.BootstrapWriteFailed,
                        "Writing the one-use AgentHost service bootstrap packet failed.",
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
                        "Resuming the validated AgentHost service failed.",
                        exception);
                }

                hostKeys = payload.DeriveDirectionKeys();
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
                    var result = AgentBootstrapConfirmationProtocol.ValidateHostConfirmation(
                        confirmation,
                        incomingGuard,
                        bootstrapId,
                        sessionId,
                        pipeName,
                        child.ProcessId,
                        child.ProcessCreationFileTime,
                        child.ExecutableSha256,
                        0,
                        false);

                    int exitCode;
                    if (child.WaitForExit(0, out exitCode))
                    {
                        throw new AgentBootstrapLaunchException(
                            AgentBootstrapLaunchFailure.ChildExitedWithError,
                            "AgentHost service exited immediately after confirmation with code "
                            + exitCode.ToString(
                                System.Globalization.CultureInfo.InvariantCulture)
                            + ".");
                    }

                    child.MarkConfirmed();
                    child.RequireTerminationOnDispose();
                    controller.CommitSuccess(child);
                    var serviceSession = new AgentHostServiceSession(
                        child,
                        hostKeys,
                        standardErrorTask,
                        result);
                    child = null;
                    hostKeys = null;
                    standardErrorTask = null;
                    return serviceSession;
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
                    "AgentHost service bootstrap failed closed.",
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
                hostKeys?.Dispose();
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
                    "AgentHost service exited before confirmation with code "
                    + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ".",
                    exception);
            }

            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ConfirmationInvalid,
                "AgentHost service confirmation was missing, truncated, duplicated, or malformed.",
                exception);
        }
    }

    private static Task<AgentHostStandardErrorCapture> CaptureStandardErrorAsync(
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

                return new AgentHostStandardErrorCapture(capturedBytes, truncated);
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

    private static void ObserveFault(Task task)
    {
        task.ContinueWith(
            completed =>
            {
                var ignored = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

public sealed class AgentHostServiceSession : IDisposable
{
    private const int TerminationWaitMilliseconds = 5000;
    private static readonly TimeSpan StandardErrorDrainTimeout = TimeSpan.FromSeconds(1);

    private readonly object _sync = new object();
    private WindowsInheritedBootstrapProcess? _child;
    private AgentBootstrapDirectionKeys? _directionKeys;
    private Task<AgentHostStandardErrorCapture>? _standardErrorTask;
    private Task? _stopTask;
    private int _disposeSignaled;
    private bool _stopping;
    private int _standardErrorBytes;
    private bool _standardErrorTruncated;

    internal AgentHostServiceSession(
        WindowsInheritedBootstrapProcess child,
        AgentBootstrapDirectionKeys directionKeys,
        Task<AgentHostStandardErrorCapture> standardErrorTask,
        AgentBootstrapDoctorResult result)
    {
        _child = child ?? throw new ArgumentNullException(nameof(child));
        _directionKeys = directionKeys ?? throw new ArgumentNullException(nameof(directionKeys));
        _standardErrorTask = standardErrorTask
            ?? throw new ArgumentNullException(nameof(standardErrorTask));
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        ProcessId = result.ProcessId;
        ProcessCreationFileTime = result.ProcessCreationFileTime;
        BootstrapId = result.BootstrapId;
        SessionId = result.SessionId;
        PipeName = result.PipeName;
        ExecutableSha256 = result.ExecutableSha256;
    }

    public int ProcessId { get; }

    public long ProcessCreationFileTime { get; }

    public string BootstrapId { get; }

    public string SessionId { get; }

    public string PipeName { get; }

    public string ExecutableSha256 { get; }

    public int StandardErrorBytes
    {
        get
        {
            lock (_sync)
            {
                return _standardErrorBytes;
            }
        }
    }

    public bool StandardErrorTruncated
    {
        get
        {
            lock (_sync)
            {
                return _standardErrorTruncated;
            }
        }
    }

    public AgentBootstrapDirectionKeys ClaimDirectionKeys()
    {
        lock (_sync)
        {
            if (_stopping || Volatile.Read(ref _disposeSignaled) != 0)
            {
                throw new ObjectDisposedException(nameof(AgentHostServiceSession));
            }

            if (_directionKeys == null)
            {
                throw new AgentBootstrapException(
                    AgentBootstrapValidationCode.AlreadyConsumed,
                    "AgentHost service direction keys have already been claimed.");
            }

            var claimed = _directionKeys;
            _directionKeys = null;
            return claimed;
        }
    }

    public Task StopAsync(
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var stopTask = GetOrStartStopTask();
        return AwaitWithCancellationAsync(stopTask, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeSignaled, 1) != 0)
        {
            return;
        }

        GetOrStartStopTask().GetAwaiter().GetResult();
    }

    private Task GetOrStartStopTask()
    {
        lock (_sync)
        {
            if (_stopTask != null)
            {
                return _stopTask;
            }

            _stopping = true;
            var child = _child;
            var directionKeys = _directionKeys;
            var standardErrorTask = _standardErrorTask;
            _child = null;
            _directionKeys = null;
            _standardErrorTask = null;
            _stopTask = Task.Run(
                () => StopCore(child, directionKeys, standardErrorTask));
            return _stopTask;
        }
    }

    private void StopCore(
        WindowsInheritedBootstrapProcess? child,
        AgentBootstrapDirectionKeys? directionKeys,
        Task<AgentHostStandardErrorCapture>? standardErrorTask)
    {
        var failures = new List<Exception>();
        directionKeys?.Dispose();
        var terminationProved = child == null;
        if (child != null)
        {
            try
            {
                terminationProved = child.TerminateAndWait(TerminationWaitMilliseconds);
                if (!terminationProved)
                {
                    failures.Add(new InvalidOperationException(
                        "AgentHost service did not terminate inside the hard cleanup deadline."));
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (standardErrorTask != null)
        {
            try
            {
                var completed = Task.WhenAny(
                        standardErrorTask,
                        Task.Delay(StandardErrorDrainTimeout))
                    .GetAwaiter()
                    .GetResult();
                if (completed != standardErrorTask)
                {
                    failures.Add(new TimeoutException(
                        "AgentHost stderr drain did not settle after process termination."));
                }
                else
                {
                    var capture = standardErrorTask.GetAwaiter().GetResult();
                    lock (_sync)
                    {
                        _standardErrorBytes = capture.Bytes;
                        _standardErrorTruncated = capture.Truncated;
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (child != null)
        {
            var ioFailure = child.AbortIo();
            if (ioFailure != null)
            {
                failures.Add(ioFailure);
            }

            try
            {
                child.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (!terminationProved || failures.Count != 0)
        {
            var failure = new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ChildTerminationFailed,
                "Stopping AgentHost service could not prove complete bounded cleanup.",
                failures.Count == 1
                    ? failures[0]
                    : new AggregateException(failures));
            AgentBootstrapLateFailureRegistry.Record(failure);
            throw failure;
        }
    }

    private static async Task AwaitWithCancellationAsync(
        Task task,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            await task.ConfigureAwait(false);
            return;
        }

        var cancellationTask = new TaskCompletionSource<bool>();
        using (cancellationToken.Register(
            state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationTask))
        {
            var completed = await Task.WhenAny(task, cancellationTask.Task)
                .ConfigureAwait(false);
            if (completed == cancellationTask.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            await task.ConfigureAwait(false);
        }
    }
}

internal sealed class AgentHostStandardErrorCapture
{
    internal AgentHostStandardErrorCapture(int bytes, bool truncated)
    {
        Bytes = bytes;
        Truncated = truncated;
    }

    internal int Bytes { get; }

    internal bool Truncated { get; }
}
