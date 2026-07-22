using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Bridge.Client;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.Host2016;

var specs = new[]
{
    new SpecCase(
        "HOST2016_CAPABILITIES_IDENTITY",
        "Host.2016 capability request satisfies v1",
        CapabilitiesIdentityIsValid),
    new SpecCase(
        "HOST2016_V2_CAPABILITIES_ACCEPT",
        "v2 method and schema are accepted",
        V2CapabilitiesAccept),
    new SpecCase(
        "HOST2016_V2_CAPABILITIES_REJECT_NULL",
        "null capabilities are rejected",
        V2CapabilitiesRejectNull),
    new SpecCase(
        "HOST2016_V2_CAPABILITIES_REJECT_MISSING_METHOD",
        "missing v2 method is rejected",
        V2CapabilitiesRejectMissingMethod),
    new SpecCase(
        "HOST2016_V2_CAPABILITIES_REJECT_MISSING_SCHEMA",
        "missing v2 schema is rejected",
        V2CapabilitiesRejectMissingSchema),
    new SpecCase(
        "HOST2016_V2_CAPABILITIES_REJECT_EMPTY_SCHEMA_LIST",
        "empty schema list is rejected",
        V2CapabilitiesRejectEmptySchemaList),
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
    new SpecCase(
        "HOST2016_BRIDGE_FAULT_TRANSITIONS_OFFLINE",
        "A Bridge fault terminates the active turn and rejects later ASK calls before transport reuse",
        BridgeFaultTransitionsOffline),
    new SpecCase(
        "HOST2016_FAILURE_FORMATTER_SANITIZES_BOOTSTRAP",
        "AgentHost startup failures expose stable structured fields without local exception details",
        FailureFormatterSanitizesBootstrap),
    new SpecCase(
        "HOST2016_TURN_FAILURE_IS_STRUCTURED_AND_SANITIZED",
        "A failed Codex turn publishes stable fields without raw Provider error text",
        TurnFailureIsStructuredAndSanitized),
    new SpecCase(
        "HOST2016_TERMINATE_SUCCESS_STOPS_ONCE",
        "AutoCAD termination performs one cleanup when it succeeds",
        TerminateSuccessStopsOnce),
    new SpecCase(
        "HOST2016_TERMINATE_ASYNC_FAILURE_RETRIES",
        "AutoCAD termination retries one asynchronous cleanup failure",
        TerminateAsyncFailureRetries),
    new SpecCase(
        "HOST2016_TERMINATE_SYNC_FAILURE_RETRIES",
        "AutoCAD termination retries one synchronous cleanup failure",
        TerminateSynchronousFailureRetries),
    new SpecCase(
        "HOST2016_TERMINATE_NULL_TASK_RETRIES",
        "AutoCAD termination treats a null cleanup task as retryable failure",
        TerminateNullTaskRetries),
    new SpecCase(
        "HOST2016_TERMINATE_FINAL_FAILURE_IS_SANITIZED",
        "AutoCAD termination reports one sanitized error after both attempts fail",
        TerminateFinalFailureIsSanitized),
    new SpecCase(
        "HOST2016_TERMINATE_STATUS_FAILURE_IS_ISOLATED",
        "A failing exit status observer cannot escape termination cleanup",
        TerminateStatusFailureIsIsolated),
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

static Task V2CapabilitiesAccept()
{
    True(
        MvpAgentCapabilityPolicy.SupportsCadContextV2(CreateCapabilities(true, true)),
        "v2 capabilities should be accepted.");
    return Task.CompletedTask;
}

static Task V2CapabilitiesRejectNull()
{
    True(
        !MvpAgentCapabilityPolicy.SupportsCadContextV2(null),
        "null capabilities should be rejected.");
    return Task.CompletedTask;
}

static Task V2CapabilitiesRejectMissingMethod()
{
    True(
        !MvpAgentCapabilityPolicy.SupportsCadContextV2(CreateCapabilities(false, true)),
        "missing v2 method should be rejected.");
    return Task.CompletedTask;
}

static Task V2CapabilitiesRejectMissingSchema()
{
    True(
        !MvpAgentCapabilityPolicy.SupportsCadContextV2(CreateCapabilities(true, false)),
        "missing v2 schema should be rejected.");
    return Task.CompletedTask;
}

static Task V2CapabilitiesRejectEmptySchemaList()
{
    var capabilities = CreateCapabilities(true, true);
    capabilities.SupportedCadContextSchemas = Array.Empty<CadContextSchemaVersionEntry>();
    True(
        !MvpAgentCapabilityPolicy.SupportsCadContextV2(capabilities),
        "empty schema list should be rejected.");
    return Task.CompletedTask;
}

static AgentCapabilitiesResponse CreateCapabilities(bool includeV2Method, bool includeV2Schema)
{
    return new AgentCapabilitiesResponse
    {
        Methods = includeV2Method
            ? new[] { AgentBridgeMethods.StartTurn, AgentBridgeMethods.StartTurnV2 }
            : new[] { AgentBridgeMethods.StartTurn },
        SupportedCadContextSchemas = includeV2Schema
            ? new[]
            {
                new CadContextSchemaVersionEntry
                {
                    Schema = CadContextJsonV2Constants.Schema,
                    SchemaVersion = CadContextJsonV2Constants.SchemaVersion,
                },
            }
            : new[]
            {
                new CadContextSchemaVersionEntry
                {
                    Schema = CadContextJsonV1Constants.Schema,
                    SchemaVersion = CadContextJsonV1Constants.SchemaVersion,
                },
            },
    };
}

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

static async Task BridgeFaultTransitionsOffline()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-bridge-fault",
        "system-session-bridge-fault");
    var statuses = new List<string>();
    client.StatusChanged += statuses.Add;
    client.ErrorChanged += statuses.Add;
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2(),
        ContextSha256 = new string('a', 64),
    };

    await client.AskAsync(
            "first turn",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    Equal(1, bridge.StartTurnV2Count, "Initial turn start count");

    bridge.RaiseFault(new AgentBridgeClientException(
        "untrusted_transport_error",
        "sensitive transport detail"));

    True(!client.IsStarted, "Bridge fault did not transition the Host client offline.");
    var failure = await ExpectBridgeClientFailure(
            client.AskAsync(
                "must be rejected",
                context,
                () => true,
                CancellationToken.None))
        .ConfigureAwait(false);
    True(
        string.Equals(
            AgentBridgeErrorCodes.ConnectionLost,
            failure.Code,
            StringComparison.Ordinal),
        "Rejected ASK did not preserve the stable Bridge error code.");
    Equal(1, bridge.StartTurnV2Count, "Turn start count after Bridge fault");
    True(
        statuses.Exists(value =>
            value.Contains("当前回合已终止", StringComparison.Ordinal)
            && value.Contains(AgentBridgeErrorCodes.ConnectionLost, StringComparison.Ordinal)),
        "Offline status did not state that the active turn was terminated with a stable code.");
    True(
        statuses.TrueForAll(value =>
            !value.Contains("sensitive transport detail", StringComparison.Ordinal)),
        "Bridge fault status leaked transport exception details.");
}

static Task FailureFormatterSanitizesBootstrap()
{
    const string sensitiveDetail = @"C:\Users\Private\AgentHost\missing.exe secret-token";
    var failure = MvpAgentFailureFormatter.FromException(
        new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.InvalidConfiguration,
            sensitiveDetail),
        MvpAgentFailureStages.StartingAgentHost);

    True(
        string.Equals(
            "agenthost_invalid_configuration",
            failure.ErrorCode,
            StringComparison.Ordinal),
        "Bootstrap failure error_code was not stable.");
    True(
        string.Equals(
            MvpAgentFailureStages.StartingAgentHost,
            failure.ErrorStage,
            StringComparison.Ordinal),
        "Bootstrap failure error_stage was not stable.");
    True(!failure.Retryable, "Invalid AgentHost configuration was marked retryable.");

    var display = failure.FormatForUser("启动 AgentHost");
    True(
        display.Contains("error_code=agenthost_invalid_configuration", StringComparison.Ordinal)
        && display.Contains("error_stage=starting_agenthost", StringComparison.Ordinal)
        && display.Contains("retryable=false", StringComparison.Ordinal),
        "Structured bootstrap failure fields were not present in the user message.");
    True(
        !display.Contains(sensitiveDetail, StringComparison.Ordinal)
        && !display.Contains("C:\\Users", StringComparison.OrdinalIgnoreCase)
        && !display.Contains("secret-token", StringComparison.Ordinal),
        "Bootstrap failure user message leaked local exception details.");
    return Task.CompletedTask;
}

static async Task TurnFailureIsStructuredAndSanitized()
{
    const string sensitiveProviderError = @"C:\Private\drawing.dwg provider-secret";
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-turn-failure",
        "system-session-turn-failure");
    var statuses = new List<string>();
    client.ErrorChanged += statuses.Add;
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2(),
        ContextSha256 = new string('b', 64),
    };

    await client.AskAsync(
            "turn that fails",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnFailed,
        TurnId = "fake-turn-1",
        ErrorCode = AgentBridgeErrorCodes.InternalError,
        Error = sensitiveProviderError,
    });

    True(
        statuses.Exists(value =>
            value.Contains("error_code=internal_error", StringComparison.Ordinal)
            && value.Contains("error_stage=running_turn", StringComparison.Ordinal)
            && value.Contains("retryable=false", StringComparison.Ordinal)),
        "Turn failure did not publish stable structured fields.");
    True(
        statuses.TrueForAll(value =>
            !value.Contains(sensitiveProviderError, StringComparison.Ordinal)
            && !value.Contains("provider-secret", StringComparison.Ordinal)),
        "Turn failure status leaked raw Provider error text.");
}

static Task TerminateSuccessStopsOnce()
{
    var stopCount = 0;
    var statusCount = 0;
    MvpAgentTerminationCoordinator.Terminate(
        () =>
        {
            stopCount++;
            return Task.CompletedTask;
        },
        _ => statusCount++);

    Equal(1, stopCount, "Successful termination stop count");
    Equal(0, statusCount, "Successful termination failure status count");
    return Task.CompletedTask;
}

static Task TerminateAsyncFailureRetries()
{
    var stopCount = 0;
    var statusCount = 0;
    MvpAgentTerminationCoordinator.Terminate(
        () =>
        {
            stopCount++;
            return stopCount == 1
                ? Task.FromException(new TimeoutException("first async stop failed"))
                : Task.CompletedTask;
        },
        _ => statusCount++);

    Equal(2, stopCount, "Asynchronous termination retry count");
    Equal(0, statusCount, "Recovered asynchronous termination status count");
    return Task.CompletedTask;
}

static Task TerminateSynchronousFailureRetries()
{
    var stopCount = 0;
    var statusCount = 0;
    MvpAgentTerminationCoordinator.Terminate(
        () =>
        {
            stopCount++;
            if (stopCount == 1)
            {
                throw new InvalidOperationException("first synchronous stop failed");
            }

            return Task.CompletedTask;
        },
        _ => statusCount++);

    Equal(2, stopCount, "Synchronous termination retry count");
    Equal(0, statusCount, "Recovered synchronous termination status count");
    return Task.CompletedTask;
}

static Task TerminateNullTaskRetries()
{
    var stopCount = 0;
    var statusCount = 0;
    MvpAgentTerminationCoordinator.Terminate(
        () =>
        {
            stopCount++;
            return stopCount == 1 ? (Task)null! : Task.CompletedTask;
        },
        _ => statusCount++);

    Equal(2, stopCount, "Null termination task retry count");
    Equal(0, statusCount, "Recovered null termination task status count");
    return Task.CompletedTask;
}

static Task TerminateFinalFailureIsSanitized()
{
    var stopCount = 0;
    var statuses = new List<string>();
    MvpAgentTerminationCoordinator.Terminate(
        () =>
        {
            stopCount++;
            return Task.FromException(
                new InvalidOperationException("sensitive-local-detail"));
        },
        statuses.Add);

    Equal(2, stopCount, "Final termination failure attempt count");
    Equal(1, statuses.Count, "Final termination failure status count");
    True(
        statuses[0].Contains("error_code=invalid_state", StringComparison.Ordinal)
        && statuses[0].Contains("error_stage=terminating_agenthost", StringComparison.Ordinal)
        && statuses[0].Contains("retryable=false", StringComparison.Ordinal),
        "Final termination status omitted stable structured failure fields.");
    True(
        !statuses[0].Contains("sensitive-local-detail", StringComparison.Ordinal),
        "Final termination status leaked exception details.");
    return Task.CompletedTask;
}

static Task TerminateStatusFailureIsIsolated()
{
    var stopCount = 0;
    MvpAgentTerminationCoordinator.Terminate(
        () =>
        {
            stopCount++;
            throw new TimeoutException("cleanup timeout");
        },
        _ => throw new InvalidOperationException("Palette observer failed"));

    Equal(2, stopCount, "Termination attempts before status callback failure");
    return Task.CompletedTask;
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

static async Task<AgentBridgeClientException> ExpectBridgeClientFailure(Task task)
{
    try
    {
        await task.ConfigureAwait(false);
    }
    catch (AgentBridgeClientException exception)
    {
        return exception;
    }

    throw new InvalidOperationException("Expected Agent Bridge failure was not observed.");
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

internal sealed class FakeAgentBridgeClient : IAgentBridgeClient
{
    internal int StartTurnV2Count { get; private set; }

    public event EventHandler<AgentBridgeEventReceivedEventArgs>? EventReceived;

    public event EventHandler<AgentBridgeConnectionFaultedEventArgs>? ConnectionFaulted;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<AgentCapabilitiesResponse> GetCapabilitiesAsync(
        AgentCapabilitiesRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<AgentThreadStartResponse> StartThreadAsync(
        AgentThreadStartRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<AgentTurnStartResponse> StartTurnAsync(
        AgentTurnStartRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<AgentTurnStartV2Response> StartTurnV2Async(
        AgentTurnStartV2Request request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartTurnV2Count++;
        return Task.FromResult(new AgentTurnStartV2Response
        {
            ThreadId = request.ThreadId,
            TurnId = "fake-turn-" + StartTurnV2Count,
            AcceptedContextV2Sha256 = request.ContextV2Sha256,
        });
    }

    public Task InterruptTurnAsync(
        AgentTurnInterruptRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task ResolveApprovalAsync(
        AgentApprovalResolveRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public void RaiseFault(AgentBridgeClientException exception)
    {
        ConnectionFaulted?.Invoke(
            this,
            new AgentBridgeConnectionFaultedEventArgs(exception));
    }

    public void RaiseEvent(AgentBridgeEvent bridgeEvent)
    {
        EventReceived?.Invoke(
            this,
            new AgentBridgeEventReceivedEventArgs(bridgeEvent));
    }

    public void Dispose()
    {
    }
}
