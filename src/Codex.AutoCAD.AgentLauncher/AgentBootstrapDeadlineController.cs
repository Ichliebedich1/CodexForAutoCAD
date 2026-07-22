using System.Diagnostics;

namespace Codex.AutoCAD.AgentLauncher;

internal sealed class AgentBootstrapDeadlineController
{
    private enum ControllerState
    {
        Active,
        Aborting,
        Aborted,
        Succeeded,
        Settled
    }

    private const int TerminationWaitMilliseconds = 5000;

    private readonly object stateGate = new object();
    private readonly object terminationGate = new object();
    private readonly long deadlineTimestamp;
    private readonly CancellationToken cancellationToken;
    private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
    private readonly ManualResetEvent cancellationEvent = new ManualResetEvent(false);
    private readonly ManualResetEvent abortSignalFinishedEvent = new ManualResetEvent(false);
    private readonly CancellationTokenSource abortTokenSource = new CancellationTokenSource();
    private readonly CancellationTokenRegistration cancellationRegistration;
    private readonly TaskCompletionSource<bool> abortCompletion = new TaskCompletionSource<bool>();
    private readonly Task supervisorTask;

    private ControllerState state;
    private WindowsInheritedBootstrapProcess? child;
    private AgentBootstrapLaunchException? primaryFailure;
    private AgentBootstrapLaunchException? terminalFailure;
    private bool preparedForWorkerExit;
    private int abortSignalsStarted;
    private bool startAttemptActive;
    private bool startAttemptSettled;
    private int supervisorThreadId;
    private int cleanupDeferredToSupervisor;
    private int resourcesDisposed;

    internal AgentBootstrapDeadlineController(
        TimeSpan startupTimeout,
        CancellationToken cancellationToken)
    {
        if (startupTimeout <= TimeSpan.Zero
            || startupTimeout > AgentHostBootstrapOptions.MaximumStartupTimeout)
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.InvalidConfiguration,
                "AgentHost startup timeout is invalid.");
        }

        this.cancellationToken = cancellationToken;
        deadlineTimestamp = AddDuration(Stopwatch.GetTimestamp(), startupTimeout);
        cancellationRegistration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(() => cancellationEvent.Set())
            : default(CancellationTokenRegistration);
        supervisorTask = Task.Factory.StartNew(
            RunSupervisor,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    internal Task AbortCompletion
    {
        get { return abortCompletion.Task; }
    }

    internal CancellationToken AbortToken
    {
        get { return abortTokenSource.Token; }
    }

    internal bool IsAbortRequested
    {
        get
        {
            lock (stateGate)
            {
                return state == ControllerState.Aborting || state == ControllerState.Aborted;
            }
        }
    }

    internal void Checkpoint()
    {
        AgentBootstrapLaunchException? failure;
        WindowsInheritedBootstrapProcess? ownedChild;
        bool ownsAbort;
        bool waitForAbort;
        if (!TryBeginRequestedAbort(
                out failure,
                out ownedChild,
                out ownsAbort,
                out waitForAbort))
        {
            return;
        }

        if (ownsAbort)
        {
            CompleteOwnedAbort(ownedChild, failure!);
        }
        else if (waitForAbort)
        {
            abortCompletion.Task.GetAwaiter().GetResult();
        }

        throw GetTerminalFailure();
    }

    internal TimeSpan GetRemainingOrThrow()
    {
        Checkpoint();
        var remainingTicks = deadlineTimestamp - Stopwatch.GetTimestamp();
        if (remainingTicks <= 0)
        {
            Checkpoint();
            throw CreateTimeoutFailure();
        }

        return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
    }

    internal void PublishSuspended(WindowsInheritedBootstrapProcess suspendedChild)
    {
        if (suspendedChild == null)
        {
            throw new ArgumentNullException(nameof(suspendedChild));
        }

        AgentBootstrapLaunchException? failure = null;
        var ownsAbort = false;
        lock (stateGate)
        {
            startAttemptActive = false;
            startAttemptSettled = true;
            if (state == ControllerState.Active)
            {
                failure = GetRequestedFailureNoLock();
                child = suspendedChild;
                if (failure == null)
                {
                    return;
                }

                primaryFailure = failure;
                state = ControllerState.Aborting;
                ownsAbort = true;
            }
            else
            {
                failure = GetCurrentFailureNoLock();
            }
        }

        if (ownsAbort)
        {
            CompleteOwnedAbort(suspendedChild, failure!);
            throw GetTerminalFailure();
        }

        var cleanupFailure = TerminateChild(suspendedChild, failure!);
        if (cleanupFailure != null)
        {
            AgentBootstrapLateFailureRegistry.Record(cleanupFailure);
            throw cleanupFailure;
        }

        throw failure!;
    }

    internal void BeginStartAttempt()
    {
        Checkpoint();
        lock (stateGate)
        {
            if (state != ControllerState.Active)
            {
                throw GetCurrentFailureNoLock();
            }

            startAttemptActive = true;
            startAttemptSettled = false;
        }
    }

    internal void EndStartAttempt()
    {
        lock (stateGate)
        {
            startAttemptActive = false;
            startAttemptSettled = true;
        }
    }

    internal void ResumePublished(WindowsInheritedBootstrapProcess publishedChild)
    {
        AgentBootstrapLaunchException? failure = null;
        var ownsAbort = false;
        lock (stateGate)
        {
            if (!ReferenceEquals(child, publishedChild))
            {
                throw new InvalidOperationException("AgentHost launch ownership is inconsistent.");
            }

            if (state == ControllerState.Active)
            {
                failure = GetRequestedFailureNoLock();
                if (failure == null)
                {
                    publishedChild.Resume();
                    return;
                }

                primaryFailure = failure;
                state = ControllerState.Aborting;
                ownsAbort = true;
            }
            else
            {
                failure = GetCurrentFailureNoLock();
            }
        }

        if (ownsAbort)
        {
            CompleteOwnedAbort(publishedChild, failure!);
        }

        throw GetTerminalFailure();
    }

    internal async Task<AgentBootstrapLaunchException> AbortForFailureAsync(
        WindowsInheritedBootstrapProcess publishedChild,
        AgentBootstrapLaunchException failure)
    {
        var ownsAbort = false;
        lock (stateGate)
        {
            if (state == ControllerState.Active)
            {
                if (!ReferenceEquals(child, publishedChild))
                {
                    throw new InvalidOperationException("AgentHost launch ownership is inconsistent.");
                }

                primaryFailure = failure;
                state = ControllerState.Aborting;
                ownsAbort = true;
            }
            else if (state == ControllerState.Succeeded || state == ControllerState.Settled)
            {
                return failure;
            }
        }

        if (ownsAbort)
        {
            CompleteOwnedAbort(publishedChild, failure);
        }
        else
        {
            await abortCompletion.Task.ConfigureAwait(false);
        }

        return GetTerminalFailure();
    }

    internal void CommitSuccess(WindowsInheritedBootstrapProcess publishedChild)
    {
        AgentBootstrapLaunchException? failure = null;
        var ownsAbort = false;
        lock (stateGate)
        {
            if (!ReferenceEquals(child, publishedChild))
            {
                throw new InvalidOperationException("AgentHost launch ownership is inconsistent.");
            }

            if (state == ControllerState.Active)
            {
                failure = GetRequestedFailureNoLock();
                if (failure == null)
                {
                    state = ControllerState.Succeeded;
                    stopEvent.Set();
                    return;
                }

                primaryFailure = failure;
                state = ControllerState.Aborting;
                ownsAbort = true;
            }
            else
            {
                failure = GetCurrentFailureNoLock();
            }
        }

        if (ownsAbort)
        {
            CompleteOwnedAbort(publishedChild, failure!);
        }

        throw GetTerminalFailure();
    }

    internal AgentBootstrapLaunchException GetTerminalFailure()
    {
        lock (stateGate)
        {
            return terminalFailure ?? primaryFailure ?? GetCurrentFailureNoLock();
        }
    }

    internal void PrepareForWorkerExit()
    {
        var calledFromSupervisor = Thread.CurrentThread.ManagedThreadId
            == Volatile.Read(ref supervisorThreadId);
        lock (stateGate)
        {
            if (preparedForWorkerExit)
            {
                return;
            }

            preparedForWorkerExit = true;
            if (state == ControllerState.Active)
            {
                state = ControllerState.Settled;
            }
            stopEvent.Set();
        }

        if (calledFromSupervisor)
        {
            Volatile.Write(ref cleanupDeferredToSupervisor, 1);
            return;
        }

        supervisorTask.GetAwaiter().GetResult();
        DisposeControllerResources();
    }

    private void RunSupervisor()
    {
        Volatile.Write(ref supervisorThreadId, Thread.CurrentThread.ManagedThreadId);
        try
        {
            var waitHandles = new WaitHandle[] { stopEvent, cancellationEvent };
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    RequestAbort(CreateCancellationFailure());
                    return;
                }

                var remainingMilliseconds = GetRemainingMilliseconds();
                if (remainingMilliseconds <= 0)
                {
                    RequestAbort(CreateTimeoutFailure());
                    return;
                }

                var result = WaitHandle.WaitAny(waitHandles, remainingMilliseconds);
                if (result == 0)
                {
                    return;
                }
                if (result == 1)
                {
                    RequestAbort(CreateCancellationFailure());
                    return;
                }
                if (result == WaitHandle.WaitTimeout)
                {
                    RequestAbort(CreateTimeoutFailure());
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            RequestAbort(new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ChildTerminationFailed,
                "AgentHost bootstrap deadline supervisor failed closed.",
                exception));
        }
        finally
        {
            if (Volatile.Read(ref cleanupDeferredToSupervisor) != 0)
            {
                DisposeControllerResources();
            }
        }
    }

    private void RequestAbort(AgentBootstrapLaunchException failure)
    {
        WindowsInheritedBootstrapProcess? ownedChild;
        lock (stateGate)
        {
            if (state != ControllerState.Active)
            {
                return;
            }

            primaryFailure = failure;
            if (child == null && startAttemptActive && !startAttemptSettled)
            {
                failure = CreateUnresolvedStartFailure(failure);
                primaryFailure = failure;
            }
            state = ControllerState.Aborting;
            ownedChild = child;
        }

        CompleteOwnedAbort(ownedChild, failure);
    }

    private bool TryBeginRequestedAbort(
        out AgentBootstrapLaunchException? failure,
        out WindowsInheritedBootstrapProcess? ownedChild,
        out bool ownsAbort,
        out bool waitForAbort)
    {
        lock (stateGate)
        {
            if (state == ControllerState.Active)
            {
                failure = GetRequestedFailureNoLock();
                if (failure == null)
                {
                    ownedChild = null;
                    ownsAbort = false;
                    waitForAbort = false;
                    return false;
                }

                if (child == null && startAttemptActive && !startAttemptSettled)
                {
                    failure = CreateUnresolvedStartFailure(failure);
                }

                primaryFailure = failure;
                state = ControllerState.Aborting;
                ownedChild = child;
                ownsAbort = true;
                waitForAbort = false;
                return true;
            }

            if (state == ControllerState.Aborting)
            {
                failure = GetCurrentFailureNoLock();
                ownedChild = null;
                ownsAbort = false;
                waitForAbort = true;
                return true;
            }

            if (state == ControllerState.Aborted)
            {
                failure = GetCurrentFailureNoLock();
                ownedChild = null;
                ownsAbort = false;
                waitForAbort = false;
                return true;
            }

            failure = null;
            ownedChild = null;
            ownsAbort = false;
            waitForAbort = false;
            return false;
        }
    }

    private void CompleteOwnedAbort(
        WindowsInheritedBootstrapProcess? ownedChild,
        AgentBootstrapLaunchException failure)
    {
        var cleanupFailure = ownedChild == null ? null : TerminateChild(ownedChild, failure);
        if (cleanupFailure != null)
        {
            AgentBootstrapLateFailureRegistry.Record(cleanupFailure);
        }
        lock (stateGate)
        {
            terminalFailure = cleanupFailure ?? failure;
            state = ControllerState.Aborted;
            stopEvent.Set();
        }

        StartAbortSignals();
    }

    private void StartAbortSignals()
    {
        if (Interlocked.Exchange(ref abortSignalsStarted, 1) != 0)
        {
            return;
        }

        try
        {
            var signalThread = new Thread(PublishAbortSignals);
            signalThread.IsBackground = true;
            signalThread.Name = "Codex.AgentBootstrap.AbortSignal";
            signalThread.Start();
        }
        catch
        {
            try
            {
                if (ThreadPool.QueueUserWorkItem(_ => PublishAbortSignals()))
                {
                    return;
                }
            }
            catch
            {
            }

            PublishAbortSignals();
        }
    }

    private void PublishAbortSignals()
    {
        Exception? signalFailure = null;
        try
        {
            abortTokenSource.Cancel();
        }
        catch (Exception exception)
        {
            signalFailure = exception;
        }
        finally
        {
            abortSignalFinishedEvent.Set();
        }

        if (signalFailure != null)
        {
            var failure = new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ChildTerminationFailed,
                "Cancelling AgentHost bootstrap waiters failed closed.",
                signalFailure);
            lock (stateGate)
            {
                terminalFailure = failure;
            }
            AgentBootstrapLateFailureRegistry.Record(failure);
        }

        abortCompletion.TrySetResult(true);
    }

    private void DisposeControllerResources()
    {
        if (Interlocked.Exchange(ref resourcesDisposed, 1) != 0)
        {
            return;
        }

        cancellationRegistration.Dispose();
        if (Volatile.Read(ref abortSignalsStarted) != 0)
        {
            abortSignalFinishedEvent.WaitOne();
        }
        lock (terminationGate)
        {
        }
        abortTokenSource.Dispose();
        abortSignalFinishedEvent.Dispose();
        cancellationEvent.Dispose();
        stopEvent.Dispose();
    }

    private AgentBootstrapLaunchException? TerminateChild(
        WindowsInheritedBootstrapProcess target,
        AgentBootstrapLaunchException originalFailure)
    {
        lock (terminationGate)
        {
            var failures = new List<Exception> { originalFailure };
            try
            {
                if (!target.TerminateAndWait(TerminationWaitMilliseconds))
                {
                    failures.Add(new InvalidOperationException(
                        "Unconfirmed AgentHost did not terminate inside the hard cleanup deadline."));
                }
            }
            catch (Exception cleanupException)
            {
                failures.Add(cleanupException);
            }

            var ioFailure = target.AbortIo();
            if (ioFailure != null)
            {
                failures.Add(ioFailure);
            }

            return failures.Count == 1
                ? null
                : new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ChildTerminationFailed,
                    "Terminating or closing unconfirmed AgentHost resources failed.",
                    new AggregateException(failures));
        }
    }

    private AgentBootstrapLaunchException? GetRequestedFailureNoLock()
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCancellationFailure();
        }

        return Stopwatch.GetTimestamp() >= deadlineTimestamp
            ? CreateTimeoutFailure()
            : null;
    }

    private AgentBootstrapLaunchException GetCurrentFailureNoLock()
    {
        return terminalFailure
            ?? primaryFailure
            ?? new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ConfirmationInvalid,
                "AgentHost bootstrap controller is no longer active.");
    }

    private int GetRemainingMilliseconds()
    {
        var remainingTicks = deadlineTimestamp - Stopwatch.GetTimestamp();
        if (remainingTicks <= 0)
        {
            return 0;
        }

        var milliseconds = Math.Ceiling(
            (double)remainingTicks * 1000.0 / Stopwatch.Frequency);
        return milliseconds >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)milliseconds);
    }

    private static long AddDuration(long timestamp, TimeSpan duration)
    {
        var durationTicks = checked((long)Math.Ceiling(duration.TotalSeconds * Stopwatch.Frequency));
        return checked(timestamp + durationTicks);
    }

    private static AgentBootstrapLaunchException CreateTimeoutFailure()
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.Timeout,
            "AgentHost did not complete inside the bootstrap hard deadline.");
    }

    private static AgentBootstrapLaunchException CreateCancellationFailure()
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.Cancellation,
            "AgentHost bootstrap was cancelled.");
    }

    private static AgentBootstrapLaunchException CreateUnresolvedStartFailure(
        AgentBootstrapLaunchException triggeringFailure)
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.ChildTerminationFailed,
            "AgentHost launch ownership was unresolved when the startup deadline ended; "
            + "the launch gate remains held until late cleanup settles.",
            triggeringFailure);
    }
}

internal static class AgentBootstrapLateFailureRegistry
{
    private static readonly object Sync = new object();
    private static AgentBootstrapLaunchException? failure;

    internal static void Record(AgentBootstrapLaunchException lateFailure)
    {
        lock (Sync)
        {
            if (failure == null)
            {
                failure = lateFailure;
            }
        }
    }

    internal static void ThrowIfPoisoned()
    {
        lock (Sync)
        {
            if (failure != null)
            {
                throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ChildTerminationFailed,
                    "A prior late AgentHost launch could not prove process termination.",
                    failure);
            }
        }
    }
}
