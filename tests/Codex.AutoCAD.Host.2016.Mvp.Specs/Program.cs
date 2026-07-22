using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016;

var specs = new[]
{
    new SpecCase(
        "HOST2016_CAPABILITIES_IDENTITY",
        "Host.2016 capability request satisfies v1",
        CapabilitiesIdentityIsValid),
    new SpecCase(
        "HOST2016_STOP_STARTS_BOTH_CLEANUPS",
        "Bridge and AgentHost cleanup begin before either side is awaited",
        StopStartsBothCleanupOperations),
    new SpecCase(
        "HOST2016_STOP_BRIDGE_FAILURE_STILL_STOPS_AGENTHOST",
        "Bridge stop failure cannot skip AgentHost termination",
        BridgeFailureStillStopsAgentHost),
    new SpecCase(
        "HOST2016_STOP_FAILURES_ARE_AGGREGATED",
        "Bridge and AgentHost failures from the same attempt remain observable",
        StopFailuresAreAggregated),
    new SpecCase(
        "HOST2016_STOP_FAILURE_CAN_RETRY",
        "A failed AgentHost cleanup remains owned and succeeds on the next STOP",
        StopFailureCanBeRetried),
    new SpecCase(
        "HOST2016_STOP_SYNCHRONOUS_FAILURE_CAN_RETRY",
        "A synchronously thrown stop failure remains owned and succeeds on the next STOP",
        SynchronousStopFailureCanBeRetried),
    new SpecCase(
        "HOST2016_STOP_NULL_TASK_CAN_RETRY",
        "A null stop task is a failure that remains owned and can be retried",
        NullStopTaskCanBeRetried),
    new SpecCase(
        "HOST2016_STOP_BRIDGE_FAILURE_CAN_RETRY",
        "A failed Bridge stop remains owned and is disposed only after a successful retry",
        BridgeStopFailureCanBeRetried),
    new SpecCase(
        "HOST2016_STOP_DISPOSE_FAILURE_RETRIES_ONLY_DISPOSE",
        "A failed Bridge dispose retries without repeating a successful Bridge stop",
        BridgeDisposeFailureRetriesOnlyDispose),
    new SpecCase(
        "HOST2016_STOP_CONCURRENT_CALLERS_SHARE_ATTEMPT",
        "Concurrent STOP callers observe the same in-flight cleanup",
        ConcurrentStopCallersShareAttempt),
    new SpecCase(
        "HOST2016_STOP_FAILED_CONCURRENT_CALLERS_SHARE_ATTEMPT",
        "Concurrent STOP callers share one failed attempt before a later retry",
        FailedConcurrentStopCallersShareAttempt),
    new SpecCase(
        "HOST2016_STOP_SUCCESS_IS_IDEMPOTENT",
        "A completed STOP does not execute cleanup again",
        CompletedStopIsIdempotent),
    new SpecCase(
        "HOST2016_STATUS_CALLBACK_CANNOT_BLOCK_STOP",
        "A failing Palette status observer cannot prevent AgentHost cleanup",
        StatusCallbackCannotBlockStop),
};

var failed = 0;
foreach (var spec in specs)
{
    try
    {
        await spec.Body().ConfigureAwait(false);
        Console.WriteLine("PASS " + spec.Id + " " + spec.Description);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine(
            "FAIL "
            + spec.Id
            + " "
            + exception.GetType().Name
            + " "
            + exception.Message);
    }
}

Console.WriteLine((specs.Length - failed) + "/" + specs.Length + " specs passed");
return failed == 0 ? 0 : 1;

static Task CapabilitiesIdentityIsValid()
{
    var request = MvpAgentProtocolIdentity.CreateCapabilitiesRequest();
    var failures = AgentBridgeContractValidator.Validate(request);
    if (failures.Length != 0)
    {
        throw new InvalidOperationException(
            failures[0].Code + " " + failures[0].Path);
    }

    return Task.CompletedTask;
}

static async Task StopStartsBothCleanupOperations()
{
    var bridgeStarted = false;
    var agentHostStarted = false;
    var disposeCount = 0;
    var bridgeRelease = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var agentHostRelease = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    var stopTask = new MvpAgentStopCoordinator(
        () =>
        {
            bridgeStarted = true;
            return bridgeRelease.Task;
        },
        () => disposeCount++,
        () =>
        {
            agentHostStarted = true;
            return agentHostRelease.Task;
        }).StopAsync();

    True(
        SpinWait.SpinUntil(() => bridgeStarted && agentHostStarted, 1000),
        "Bridge and AgentHost cleanup did not both start.");
    True(!stopTask.IsCompleted, "Stop completed before both cleanup tasks settled.");

    agentHostRelease.TrySetResult(true);
    bridgeRelease.TrySetResult(true);
    await stopTask.ConfigureAwait(false);
    Equal(1, disposeCount, "Bridge dispose count");
}

static async Task BridgeFailureStillStopsAgentHost()
{
    var agentHostStopCount = 0;
    var disposeCount = 0;
    MvpAgentStopException? observed = null;
    try
    {
        await new MvpAgentStopCoordinator(
                () => Task.FromException(new TimeoutException("bridge-timeout")),
                () => disposeCount++,
                () =>
                {
                    agentHostStopCount++;
                    return Task.CompletedTask;
                })
            .StopAsync()
            .ConfigureAwait(false);
    }
    catch (MvpAgentStopException exception)
    {
        observed = exception;
    }

    if (observed is null)
    {
        throw new InvalidOperationException("Bridge failure was not reported.");
    }

    Equal(1, observed.FailureCount, "Stop failure count");
    Equal(1, agentHostStopCount, "AgentHost stop count");
    Equal(0, disposeCount, "Bridge dispose count after failed stop");
}

static async Task StopFailuresAreAggregated()
{
    MvpAgentStopException? observed = null;
    try
    {
        await new MvpAgentStopCoordinator(
                () => Task.FromException(new TimeoutException("bridge-timeout")),
                () => throw new InvalidOperationException("bridge-dispose"),
                () => Task.FromException(new InvalidOperationException("agenthost-stop")))
            .StopAsync()
            .ConfigureAwait(false);
    }
    catch (MvpAgentStopException exception)
    {
        observed = exception;
    }

    if (observed is null)
    {
        throw new InvalidOperationException("Aggregate stop failure was not reported.");
    }

    Equal(2, observed.FailureCount, "Aggregated failure count");
}

static async Task StopFailureCanBeRetried()
{
    var bridgeStopCount = 0;
    var bridgeDisposeCount = 0;
    var agentHostStopCount = 0;
    var coordinator = new MvpAgentStopCoordinator(
        () =>
        {
            bridgeStopCount++;
            return Task.CompletedTask;
        },
        () => bridgeDisposeCount++,
        () =>
        {
            agentHostStopCount++;
            return agentHostStopCount == 1
                ? Task.FromException(new TimeoutException("first-stop-timeout"))
                : Task.CompletedTask;
        });

    await ExpectStopFailure(coordinator.StopAsync()).ConfigureAwait(false);
    True(!coordinator.IsComplete, "Failed AgentHost cleanup was incorrectly marked complete.");

    await coordinator.StopAsync().ConfigureAwait(false);
    True(coordinator.IsComplete, "Retry did not complete retained AgentHost cleanup.");
    Equal(1, bridgeStopCount, "Bridge stop retry count");
    Equal(1, bridgeDisposeCount, "Bridge dispose retry count");
    Equal(2, agentHostStopCount, "AgentHost stop retry count");
}

static async Task SynchronousStopFailureCanBeRetried()
{
    var agentHostStopCount = 0;
    var coordinator = new MvpAgentStopCoordinator(
        null,
        null,
        () =>
        {
            agentHostStopCount++;
            if (agentHostStopCount == 1)
            {
                throw new InvalidOperationException("synchronous-stop-failure");
            }

            return Task.CompletedTask;
        });

    await ExpectStopFailure(coordinator.StopAsync()).ConfigureAwait(false);
    True(!coordinator.IsComplete, "Synchronous stop failure was incorrectly marked complete.");

    await coordinator.StopAsync().ConfigureAwait(false);
    True(coordinator.IsComplete, "Synchronous stop failure was not retried.");
    Equal(2, agentHostStopCount, "Synchronous AgentHost stop retry count");
}

static async Task NullStopTaskCanBeRetried()
{
    var agentHostStopCount = 0;
    var coordinator = new MvpAgentStopCoordinator(
        null,
        null,
        () =>
        {
            agentHostStopCount++;
            return agentHostStopCount == 1 ? null! : Task.CompletedTask;
        });

    await ExpectStopFailure(coordinator.StopAsync()).ConfigureAwait(false);
    True(!coordinator.IsComplete, "Null stop task was incorrectly marked complete.");

    await coordinator.StopAsync().ConfigureAwait(false);
    True(coordinator.IsComplete, "Null stop task was not retried.");
    Equal(2, agentHostStopCount, "Null AgentHost stop task retry count");
}

static async Task BridgeStopFailureCanBeRetried()
{
    var bridgeStopCount = 0;
    var bridgeDisposeCount = 0;
    var coordinator = new MvpAgentStopCoordinator(
        () =>
        {
            bridgeStopCount++;
            return bridgeStopCount == 1
                ? Task.FromException(new TimeoutException("first-bridge-stop-timeout"))
                : Task.CompletedTask;
        },
        () => bridgeDisposeCount++,
        () => Task.CompletedTask);

    await ExpectStopFailure(coordinator.StopAsync()).ConfigureAwait(false);
    True(!coordinator.IsComplete, "Failed Bridge stop was incorrectly marked complete.");
    Equal(0, bridgeDisposeCount, "Bridge dispose count after failed stop");

    await coordinator.StopAsync().ConfigureAwait(false);
    True(coordinator.IsComplete, "Bridge stop retry did not complete cleanup.");
    Equal(2, bridgeStopCount, "Bridge stop retry count");
    Equal(1, bridgeDisposeCount, "Bridge dispose count after successful retry");
}

static async Task BridgeDisposeFailureRetriesOnlyDispose()
{
    var bridgeStopCount = 0;
    var bridgeDisposeCount = 0;
    var agentHostStopCount = 0;
    var coordinator = new MvpAgentStopCoordinator(
        () =>
        {
            bridgeStopCount++;
            return Task.CompletedTask;
        },
        () =>
        {
            bridgeDisposeCount++;
            if (bridgeDisposeCount == 1)
            {
                throw new InvalidOperationException("first-bridge-dispose-failure");
            }
        },
        () =>
        {
            agentHostStopCount++;
            return Task.CompletedTask;
        });

    await ExpectStopFailure(coordinator.StopAsync()).ConfigureAwait(false);
    True(!coordinator.IsComplete, "Failed Bridge dispose was incorrectly marked complete.");

    await coordinator.StopAsync().ConfigureAwait(false);
    True(coordinator.IsComplete, "Bridge dispose retry did not complete cleanup.");
    Equal(1, bridgeStopCount, "Bridge stop count after dispose retry");
    Equal(2, bridgeDisposeCount, "Bridge dispose retry count");
    Equal(1, agentHostStopCount, "AgentHost stop count after dispose retry");
}

static async Task ConcurrentStopCallersShareAttempt()
{
    var bridgeStopCount = 0;
    var agentHostStopCount = 0;
    var bridgeRelease = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var agentHostRelease = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var coordinator = new MvpAgentStopCoordinator(
        () =>
        {
            bridgeStopCount++;
            return bridgeRelease.Task;
        },
        () => { },
        () =>
        {
            agentHostStopCount++;
            return agentHostRelease.Task;
        });

    var first = coordinator.StopAsync();
    var second = coordinator.StopAsync();
    True(ReferenceEquals(first, second), "Concurrent STOP did not share one attempt.");
    True(!second.IsCompleted, "Second STOP completed before cleanup settled.");

    bridgeRelease.TrySetResult(true);
    agentHostRelease.TrySetResult(true);
    await Task.WhenAll(first, second).ConfigureAwait(false);
    Equal(1, bridgeStopCount, "Concurrent Bridge stop count");
    Equal(1, agentHostStopCount, "Concurrent AgentHost stop count");
}

static async Task FailedConcurrentStopCallersShareAttempt()
{
    var bridgeStopCount = 0;
    var bridgeDisposeCount = 0;
    var firstBridgeAttempt = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var coordinator = new MvpAgentStopCoordinator(
        () =>
        {
            bridgeStopCount++;
            return bridgeStopCount == 1
                ? firstBridgeAttempt.Task
                : Task.CompletedTask;
        },
        () => bridgeDisposeCount++,
        () => Task.CompletedTask);

    var first = coordinator.StopAsync();
    var second = coordinator.StopAsync();
    True(ReferenceEquals(first, second), "Concurrent failed STOP did not share one attempt.");

    firstBridgeAttempt.TrySetException(new TimeoutException("shared-bridge-stop-timeout"));
    await ExpectStopFailure(first).ConfigureAwait(false);
    await ExpectStopFailure(second).ConfigureAwait(false);
    Equal(1, bridgeStopCount, "Failed shared Bridge stop count");
    Equal(0, bridgeDisposeCount, "Dispose count after shared failure");

    await coordinator.StopAsync().ConfigureAwait(false);
    True(coordinator.IsComplete, "Retry after shared failure did not complete cleanup.");
    Equal(2, bridgeStopCount, "Bridge stop count after shared failure retry");
    Equal(1, bridgeDisposeCount, "Dispose count after shared failure retry");
}

static async Task CompletedStopIsIdempotent()
{
    var bridgeStopCount = 0;
    var bridgeDisposeCount = 0;
    var agentHostStopCount = 0;
    var coordinator = new MvpAgentStopCoordinator(
        () =>
        {
            bridgeStopCount++;
            return Task.CompletedTask;
        },
        () => bridgeDisposeCount++,
        () =>
        {
            agentHostStopCount++;
            return Task.CompletedTask;
        });

    await coordinator.StopAsync().ConfigureAwait(false);
    await coordinator.StopAsync().ConfigureAwait(false);
    Equal(1, bridgeStopCount, "Idempotent Bridge stop count");
    Equal(1, bridgeDisposeCount, "Idempotent Bridge dispose count");
    Equal(1, agentHostStopCount, "Idempotent AgentHost stop count");
}

static async Task StatusCallbackCannotBlockStop()
{
    var callbackCount = 0;
    var client = new MvpAgentClient();
    client.StatusChanged += _ =>
    {
        callbackCount++;
        throw new InvalidOperationException("simulated Palette callback failure");
    };

    await client.StopAsync(CancellationToken.None).ConfigureAwait(false);
    True(callbackCount >= 2, "Stop status callbacks were not exercised.");
    client.Dispose();
}

static async Task ExpectStopFailure(Task task)
{
    try
    {
        await task.ConfigureAwait(false);
    }
    catch (MvpAgentStopException)
    {
        return;
    }

    throw new InvalidOperationException("Expected stop failure was not observed.");
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Equal(int expected, int actual, string label)
{
    if (expected != actual)
    {
        throw new InvalidOperationException(
            label + " expected " + expected + " but was " + actual + ".");
    }
}

internal sealed class SpecCase
{
    internal SpecCase(string id, string description, Func<Task> body)
    {
        Id = id;
        Description = description;
        Body = body;
    }

    internal string Id { get; }

    internal string Description { get; }

    internal Func<Task> Body { get; }
}
