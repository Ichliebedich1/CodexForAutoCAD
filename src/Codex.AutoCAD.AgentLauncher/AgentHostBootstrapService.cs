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
        var credentialOptions = options.GetValidatedCredential();
        var processTreeLimits = options.GetValidatedProcessTreeLimits();
        var maximumSessionRuntime = options.GetValidatedSessionRuntime();
        var gracefulStopTimeout = options.GetValidatedGracefulStopTimeout();
        controller.Checkpoint();
        var sessionId = CreateRandomIdentifier();
        var pipeName = PipeNamePrefix + CreateRandomIdentifier();
        AgentHostCredentialSecret? credentialSecret = null;
        if (credentialOptions.Mode == AgentHostCredentialMode.WindowsCredentialManagerAccessToken)
        {
            credentialSecret = new WindowsCredentialManagerCredentialReader()
                .Read(credentialOptions);
        }

        using (credentialSecret)
        using (var credentialServer = AgentCredentialPipeServer.Create(pipeName))
        {
        AgentSessionWorkspaceLease? workspaceLease =
            AgentSessionWorkspaceLease.CreateForCurrentUser(sessionId);
        controller.Checkpoint();
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
                        processTreeLimits,
                        AgentHostProcessIdentityProfile.CurrentUser,
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
                using (var credentialAuthenticator =
                    hostKeys.CreateConfirmationOutboundAuthenticator())
                {
                    await credentialServer.DeliverAsync(
                            sessionId,
                            bootstrapId,
                            child.ProcessId,
                            child.ProcessCreationFileTime,
                            credentialOptions.Mode == AgentHostCredentialMode.Disabled
                                ? AgentCredentialDeliveryMode.Disabled
                                : AgentCredentialDeliveryMode.AccessToken,
                            credentialSecret,
                            credentialAuthenticator,
                            controller.AbortToken)
                        .ConfigureAwait(false);
                }
                credentialSecret?.Dispose();

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
                    result = result.WithProcessIdentity(
                        child.ProcessIdentityProfile,
                        child.ProcessTokenIsRestricted,
                        child.UsesPrivateDesktop);

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
                        result,
                        maximumSessionRuntime,
                        gracefulStopTimeout,
                        workspaceLease);
                    child = null;
                    hostKeys = null;
                    standardErrorTask = null;
                    workspaceLease = null;
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
                try
                {
                    workspaceLease?.Dispose();
                }
                catch (AgentBootstrapLaunchException exception)
                {
                    AgentBootstrapLateFailureRegistry.Record(exception);
                    throw;
                }
            }
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
    private const int ProcessExitWatcherWaitMilliseconds = int.MaxValue;
    private const int AutomaticCleanupAttempts = 2;
    private static readonly TimeSpan StandardErrorDrainTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AutomaticCleanupRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly object _sync = new object();
    private readonly Func<int, bool> _waitForExit;
    private readonly Func<int, bool> _terminateAndWait;
    private readonly Func<Exception?> _abortIo;
    private readonly Action _disposeProcess;
    private readonly Func<int, AgentHostProcessTreeLimitNotification>? _waitForLimitNotification;
    private readonly Action? _cancelLimitNotificationWait;
    private readonly int _gracefulExitWaitMilliseconds;
    private readonly TaskCompletionSource<AgentHostResourceLimitFailure>
        _resourceLimitFailureCompletion = new TaskCompletionSource<AgentHostResourceLimitFailure>();
    private readonly TaskCompletionSource<AgentHostProcessExitFailure>
        _processExitFailureCompletion;
    private AgentSessionWorkspaceLease? _workspaceLease;
    private Timer? _runtimeDeadlineTimer;
    private AgentBootstrapDirectionKeys? _directionKeys;
    private Task<AgentHostStandardErrorCapture>? _standardErrorTask;
    private Task? _stopTask;
    private int _disposeSignaled;
    private bool _stopping;
    private bool _terminationProved;
    private bool _standardErrorSettled;
    private bool _ioAborted;
    private bool _processDisposed;
    private int _standardErrorBytes;
    private bool _standardErrorTruncated;
    private int _runtimeExpired;

    internal AgentHostServiceSession(
        WindowsInheritedBootstrapProcess child,
        AgentBootstrapDirectionKeys directionKeys,
        Task<AgentHostStandardErrorCapture> standardErrorTask,
        AgentBootstrapDoctorResult result,
        TimeSpan maximumSessionRuntime,
        TimeSpan gracefulStopTimeout,
        AgentSessionWorkspaceLease workspaceLease)
    {
        if (child == null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        _waitForExit = milliseconds => child.WaitForExit(milliseconds, out _);
        _terminateAndWait = child.TerminateAndWait;
        _abortIo = child.AbortIo;
        _disposeProcess = child.Dispose;
        _waitForLimitNotification = child.WaitForLimitNotification;
        _cancelLimitNotificationWait = child.CancelLimitNotificationWait;
        _gracefulExitWaitMilliseconds = ToGracefulStopMilliseconds(gracefulStopTimeout);
        _processExitFailureCompletion =
            new TaskCompletionSource<AgentHostProcessExitFailure>();
        _directionKeys = directionKeys ?? throw new ArgumentNullException(nameof(directionKeys));
        _standardErrorTask = standardErrorTask
            ?? throw new ArgumentNullException(nameof(standardErrorTask));
        _workspaceLease = workspaceLease
            ?? throw new ArgumentNullException(nameof(workspaceLease));
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
        StartRuntimeDeadline(maximumSessionRuntime);
        StartResourceLimitWatcher();
        StartProcessExitWatcher();
    }

    internal AgentHostServiceSession(
        Func<int, bool> terminateAndWait,
        Func<Exception?> abortIo,
        Action disposeProcess,
        Task<AgentHostStandardErrorCapture> standardErrorTask,
        AgentBootstrapDoctorResult result)
        : this(
            _ => false,
            terminateAndWait,
            abortIo,
            disposeProcess,
            standardErrorTask,
            result,
            AgentHostBootstrapOptions.DefaultGracefulStopTimeout)
    {
    }

    internal AgentHostServiceSession(
        Func<int, bool> waitForExit,
        Func<int, bool> terminateAndWait,
        Func<Exception?> abortIo,
        Action disposeProcess,
        Task<AgentHostStandardErrorCapture> standardErrorTask,
        AgentBootstrapDoctorResult result)
        : this(
            waitForExit,
            terminateAndWait,
            abortIo,
            disposeProcess,
            standardErrorTask,
            result,
            AgentHostBootstrapOptions.DefaultGracefulStopTimeout)
    {
    }

    internal AgentHostServiceSession(
        Func<int, bool> waitForExit,
        Func<int, bool> terminateAndWait,
        Func<Exception?> abortIo,
        Action disposeProcess,
        Task<AgentHostStandardErrorCapture> standardErrorTask,
        AgentBootstrapDoctorResult result,
        TimeSpan gracefulStopTimeout,
        AgentSessionWorkspaceLease? workspaceLease = null,
        TaskCompletionSource<AgentHostProcessExitFailure>? processExitFailureCompletion = null)
    {
        _waitForExit = waitForExit
            ?? throw new ArgumentNullException(nameof(waitForExit));
        _terminateAndWait = terminateAndWait
            ?? throw new ArgumentNullException(nameof(terminateAndWait));
        _abortIo = abortIo ?? throw new ArgumentNullException(nameof(abortIo));
        _disposeProcess = disposeProcess
            ?? throw new ArgumentNullException(nameof(disposeProcess));
        _waitForLimitNotification = null;
        _cancelLimitNotificationWait = null;
        _gracefulExitWaitMilliseconds = ToGracefulStopMilliseconds(gracefulStopTimeout);
        _processExitFailureCompletion = processExitFailureCompletion
            ?? new TaskCompletionSource<AgentHostProcessExitFailure>();
        _standardErrorTask = standardErrorTask
            ?? throw new ArgumentNullException(nameof(standardErrorTask));
        _workspaceLease = workspaceLease;
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

    internal AgentHostServiceSession(
        Func<int, bool> terminateAndWait,
        Func<Exception?> abortIo,
        Action disposeProcess,
        Task<AgentHostStandardErrorCapture> standardErrorTask,
        AgentBootstrapDoctorResult result,
        TimeSpan maximumSessionRuntime)
        : this(
            _ => false,
            terminateAndWait,
            abortIo,
            disposeProcess,
            standardErrorTask,
            result,
            AgentHostBootstrapOptions.DefaultGracefulStopTimeout)
    {
        StartRuntimeDeadline(maximumSessionRuntime);
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

    public bool RuntimeExpired
    {
        get { return Volatile.Read(ref _runtimeExpired) != 0; }
    }

    public Task<AgentHostResourceLimitFailure> ResourceLimitFailureTask
    {
        get { return _resourceLimitFailureCompletion.Task; }
    }

    public Task<AgentHostProcessExitFailure> ProcessExitFailureTask
    {
        get { return _processExitFailureCompletion.Task; }
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
        if (Volatile.Read(ref _disposeSignaled) != 0)
        {
            return;
        }

        GetOrStartStopTask().GetAwaiter().GetResult();
        Interlocked.Exchange(ref _disposeSignaled, 1);
    }

    private Task GetOrStartStopTask(bool cancelResourceMonitor = true)
    {
        Task attempt;
        Action? cancelLimitNotificationWait;
        lock (_sync)
        {
            if (_stopTask != null)
            {
                return _stopTask;
            }

            _stopping = true;
            if (cancelResourceMonitor)
            {
                _resourceLimitFailureCompletion.TrySetResult(
                    AgentHostResourceLimitFailure.None);
                _processExitFailureCompletion.TrySetResult(
                    AgentHostProcessExitFailure.None);
            }
            var runtimeDeadlineTimer = _runtimeDeadlineTimer;
            _runtimeDeadlineTimer = null;
            runtimeDeadlineTimer?.Dispose();
            var directionKeys = _directionKeys;
            _directionKeys = null;
            var completion = new TaskCompletionSource<bool>();
            attempt = completion.Task;
            _stopTask = attempt;
            cancelLimitNotificationWait = cancelResourceMonitor
                ? _cancelLimitNotificationWait
                : null;
            _ = CompleteStopAttemptAsync(completion, attempt, directionKeys);
        }

        try
        {
            cancelLimitNotificationWait?.Invoke();
        }
        catch
        {
            // Closing the owned Job completion port during process disposal is the final wake-up
            // boundary. A failed advisory wake-up must not replace the bounded STOP result.
        }

        return attempt;
    }

    private async Task CompleteStopAttemptAsync(
        TaskCompletionSource<bool> completion,
        Task attempt,
        AgentBootstrapDirectionKeys? directionKeys)
    {
        Exception? failure = null;
        try
        {
            await Task.Run(() => StopCore(directionKeys)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        lock (_sync)
        {
            if (ReferenceEquals(_stopTask, attempt)
                && failure != null)
            {
                _stopTask = null;
            }
        }

        if (failure == null)
        {
            completion.TrySetResult(true);
        }
        else
        {
            completion.TrySetException(failure);
        }
    }

    private void StopCore(AgentBootstrapDirectionKeys? directionKeys)
    {
        var failures = new List<Exception>();
        directionKeys?.Dispose();
        bool terminationProved;
        lock (_sync)
        {
            terminationProved = _terminationProved;
        }

        Exception? gracefulWaitFailure = null;
        if (!terminationProved)
        {
            try
            {
                terminationProved = _waitForExit(_gracefulExitWaitMilliseconds);
                if (terminationProved)
                {
                    lock (_sync)
                    {
                        _terminationProved = true;
                    }
                }
            }
            catch (Exception exception)
            {
                gracefulWaitFailure = exception;
            }
        }

        if (!terminationProved)
        {
            try
            {
                terminationProved = _terminateAndWait(TerminationWaitMilliseconds);
                if (terminationProved)
                {
                    lock (_sync)
                    {
                        _terminationProved = true;
                    }
                }
                else
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

        if (!terminationProved)
        {
            if (gracefulWaitFailure != null)
            {
                failures.Insert(0, gracefulWaitFailure);
            }

            throw CreateStopFailure(failures);
        }

        Task<AgentHostStandardErrorCapture>? standardErrorTask;
        lock (_sync)
        {
            standardErrorTask = _standardErrorSettled ? null : _standardErrorTask;
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
                else if (standardErrorTask.Status != TaskStatus.RanToCompletion)
                {
                    // AbortIo can make the bounded stderr reader finish faulted or cancelled.
                    // The task is nevertheless terminal and no longer owns running I/O, so it is
                    // cleanup-settled; the launch/runtime path already reports the underlying
                    // diagnostic failure separately.
                    var ignored = standardErrorTask.Exception;
                    lock (_sync)
                    {
                        _standardErrorSettled = true;
                        _standardErrorTask = null;
                    }
                }
                else
                {
                    var capture = standardErrorTask.GetAwaiter().GetResult();
                    lock (_sync)
                    {
                        _standardErrorBytes = capture.Bytes;
                        _standardErrorTruncated = capture.Truncated;
                        _standardErrorSettled = true;
                        _standardErrorTask = null;
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        else
        {
            lock (_sync)
            {
                if (!_standardErrorSettled)
                {
                    failures.Add(new InvalidOperationException(
                        "AgentHost stderr capture was unavailable before it settled."));
                }
            }
        }

        bool ioAborted;
        lock (_sync)
        {
            ioAborted = _ioAborted;
        }

        if (!ioAborted)
        {
            try
            {
                var ioFailure = _abortIo();
                if (ioFailure != null)
                {
                    failures.Add(ioFailure);
                }
                else
                {
                    ioAborted = true;
                    lock (_sync)
                    {
                        _ioAborted = true;
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        bool processDisposed;
        lock (_sync)
        {
            processDisposed = _processDisposed;
        }

        if (ioAborted && !processDisposed)
        {
            try
            {
                _disposeProcess();
                lock (_sync)
                {
                    _processDisposed = true;
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        bool standardErrorSettled;
        AgentSessionWorkspaceLease? workspaceLease;
        lock (_sync)
        {
            standardErrorSettled = _standardErrorSettled;
            processDisposed = _processDisposed;
            workspaceLease = _workspaceLease;
        }

        if (terminationProved
            && standardErrorSettled
            && ioAborted
            && processDisposed
            && workspaceLease != null)
        {
            try
            {
                workspaceLease.Dispose();
                lock (_sync)
                {
                    if (ReferenceEquals(_workspaceLease, workspaceLease))
                    {
                        _workspaceLease = null;
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count != 0)
        {
            throw CreateStopFailure(failures);
        }
    }

    private static AgentBootstrapLaunchException CreateStopFailure(
        IList<Exception> failures)
    {
        var failure = new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.ChildTerminationFailed,
            "Stopping AgentHost service could not prove complete bounded cleanup.",
            failures.Count == 1
                ? failures[0]
                : new AggregateException(failures));
        return failure;
    }

    private void StartResourceLimitWatcher()
    {
        if (_waitForLimitNotification == null)
        {
            return;
        }

        _ = Task.Factory.StartNew(
            WatchForResourceLimit,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void WatchForResourceLimit()
    {
        try
        {
            while (true)
            {
                var notification = _waitForLimitNotification!(
                    ProcessExitWatcherWaitMilliseconds);
                switch (notification)
                {
                    case AgentHostProcessTreeLimitNotification.None:
                        continue;
                    case AgentHostProcessTreeLimitNotification.MonitorClosed:
                        _resourceLimitFailureCompletion.TrySetResult(
                            AgentHostResourceLimitFailure.None);
                        return;
                    case AgentHostProcessTreeLimitNotification.RootProcessExited:
                        _resourceLimitFailureCompletion.TrySetResult(
                            AgentHostResourceLimitFailure.None);
                        return;
                    case AgentHostProcessTreeLimitNotification.ProcessCountExceeded:
                        OnResourceLimitFailure(
                            AgentHostResourceLimitFailure.ProcessCountExceeded);
                        return;
                    case AgentHostProcessTreeLimitNotification.JobMemoryExceeded:
                        OnResourceLimitFailure(
                            AgentHostResourceLimitFailure.JobMemoryExceeded);
                        return;
                    case AgentHostProcessTreeLimitNotification.JobUserTimeExceeded:
                        OnResourceLimitFailure(
                            AgentHostResourceLimitFailure.JobUserTimeExceeded);
                        return;
                    default:
                        throw new AgentBootstrapLaunchException(
                            AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                            "The AgentHost Job resource monitor returned an unknown notification.");
                }
            }
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (_stopping || _processDisposed || Volatile.Read(ref _disposeSignaled) != 0)
                {
                    return;
                }
            }

            var monitorFailure = exception as AgentBootstrapLaunchException
                ?? new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                    "The AgentHost Job resource monitor failed.",
                    exception);
            _resourceLimitFailureCompletion.TrySetException(monitorFailure);
            _processExitFailureCompletion.TrySetResult(
                AgentHostProcessExitFailure.None);
            var cleanupTask = CompleteResourceLimitCleanupAsync(
                "AgentHost Job resource-monitor cleanup failed.");
            ObserveFault(cleanupTask);
        }
    }

    private void OnResourceLimitFailure(AgentHostResourceLimitFailure failure)
    {
        if (!_resourceLimitFailureCompletion.TrySetResult(failure))
        {
            return;
        }

        _processExitFailureCompletion.TrySetResult(
            AgentHostProcessExitFailure.None);
        var cleanupTask = CompleteResourceLimitCleanupAsync(
            "AgentHost Job resource-limit cleanup failed.");
        ObserveFault(cleanupTask);
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

    // The launcher owns the Job handle. If AgentHost exits by itself, run the normal stop path so
    // KILL_ON_JOB_CLOSE also collects any descendant that outlived AgentHost.
    private void StartProcessExitWatcher()
    {
        _ = Task.Factory.StartNew(
            WatchForUnexpectedProcessExit,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void WatchForUnexpectedProcessExit()
    {
        AgentBootstrapLaunchException? processMonitorFailure = null;
        try
        {
            while (!_waitForExit(ProcessExitWatcherWaitMilliseconds))
            {
                lock (_sync)
                {
                    if (_stopping || _processDisposed || Volatile.Read(ref _disposeSignaled) != 0)
                    {
                        return;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            processMonitorFailure = exception as AgentBootstrapLaunchException
                ?? new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                    "The AgentHost process-exit monitor failed.",
                    exception);
        }

        lock (_sync)
        {
            if (_stopping || _processDisposed || Volatile.Read(ref _disposeSignaled) != 0)
            {
                return;
            }
        }

        if (processMonitorFailure != null)
        {
            _resourceLimitFailureCompletion.TrySetResult(
                AgentHostResourceLimitFailure.None);
            _processExitFailureCompletion.TrySetException(processMonitorFailure);
        }

        var cleanupTask = CompleteUnexpectedProcessExitCleanupAsync(
            processMonitorFailure);
        cleanupTask.ContinueWith(
            completed =>
            {
                var ignored = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task CompleteUnexpectedProcessExitCleanupAsync(
        AgentBootstrapLaunchException? processMonitorFailure)
    {
        Exception? terminalObservationFailure = processMonitorFailure;
        if (processMonitorFailure == null)
        {
            try
            {
                var resourceFailure = await _resourceLimitFailureCompletion.Task
                    .ConfigureAwait(false);
                _processExitFailureCompletion.TrySetResult(
                    resourceFailure == AgentHostResourceLimitFailure.None
                        ? AgentHostProcessExitFailure.UnexpectedExit
                        : AgentHostProcessExitFailure.None);
            }
            catch (Exception exception)
            {
                // A resource-monitor exception is already the authoritative runtime terminal.
                // Do not race it with a second "unexpected exit" classification.
                _processExitFailureCompletion.TrySetResult(
                    AgentHostProcessExitFailure.None);
                terminalObservationFailure = exception;
            }
        }

        AgentBootstrapLaunchException? lastFailure = null;
        for (var attempt = 0; attempt < AutomaticCleanupAttempts; attempt++)
        {
            try
            {
                await GetOrStartStopTask(cancelResourceMonitor: false).ConfigureAwait(false);
                return;
            }
            catch (AgentBootstrapLaunchException exception)
            {
                lastFailure = exception;
            }
            catch (Exception exception)
            {
                lastFailure = new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ChildTerminationFailed,
                    "AgentHost unexpected-exit cleanup failed.",
                    exception);
            }

            if (attempt + 1 < AutomaticCleanupAttempts)
            {
                await Task.Delay(AutomaticCleanupRetryDelay).ConfigureAwait(false);
            }
        }

        var cleanupFailure = lastFailure ?? new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.ChildTerminationFailed,
            "AgentHost unexpected-exit cleanup ended without a terminal failure detail.");
        AgentBootstrapLateFailureRegistry.Record(
            new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ChildTerminationFailed,
                "AgentHost exited or its exit monitor failed before complete process-tree cleanup after retry.",
                terminalObservationFailure == null
                    ? cleanupFailure
                    : new AggregateException(terminalObservationFailure, cleanupFailure)));
    }

    private void StartRuntimeDeadline(TimeSpan maximumSessionRuntime)
    {
        if (maximumSessionRuntime <= TimeSpan.Zero
            || maximumSessionRuntime > AgentHostBootstrapOptions.MaximumMaximumSessionRuntime)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSessionRuntime));
        }

        _runtimeDeadlineTimer = new Timer(
            OnRuntimeDeadline,
            null,
            maximumSessionRuntime,
            Timeout.InfiniteTimeSpan);
    }

    private static int ToGracefulStopMilliseconds(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero
            || timeout > AgentHostBootstrapOptions.MaximumGracefulStopTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        return checked((int)Math.Ceiling(timeout.TotalMilliseconds));
    }

    private void OnRuntimeDeadline(object? state)
    {
        lock (_sync)
        {
            if (_stopping
                || _runtimeDeadlineTimer == null
                || _runtimeExpired != 0)
            {
                return;
            }

            Volatile.Write(ref _runtimeExpired, 1);
            var runtimeDeadlineTimer = _runtimeDeadlineTimer;
            _runtimeDeadlineTimer = null;
            runtimeDeadlineTimer.Dispose();
        }

        if (!_resourceLimitFailureCompletion.TrySetResult(
                AgentHostResourceLimitFailure.SessionRuntimeExceeded))
        {
            return;
        }

        _processExitFailureCompletion.TrySetResult(
            AgentHostProcessExitFailure.None);
        var cleanupTask = CompleteResourceLimitCleanupAsync(
            "AgentHost session-runtime cleanup failed.");
        ObserveFault(cleanupTask);
    }

    private async Task CompleteResourceLimitCleanupAsync(string unsafeDiagnostic)
    {
        AgentBootstrapLaunchException? lastFailure = null;
        for (var attempt = 0; attempt < AutomaticCleanupAttempts; attempt++)
        {
            try
            {
                await GetOrStartStopTask(cancelResourceMonitor: false).ConfigureAwait(false);
                return;
            }
            catch (AgentBootstrapLaunchException exception)
            {
                lastFailure = exception;
            }
            catch (Exception exception)
            {
                lastFailure = new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ChildTerminationFailed,
                    unsafeDiagnostic,
                    exception);
            }

            if (attempt + 1 < AutomaticCleanupAttempts)
            {
                await Task.Delay(AutomaticCleanupRetryDelay).ConfigureAwait(false);
            }
        }

        AgentBootstrapLateFailureRegistry.Record(
            new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ChildTerminationFailed,
                "AgentHost resource-limit cleanup could not be proved after retry.",
                lastFailure));
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
