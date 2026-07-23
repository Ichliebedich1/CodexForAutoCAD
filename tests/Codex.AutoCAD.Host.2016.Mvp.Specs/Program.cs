using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Bridge.Client;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.Host2016;
using System.Reflection;

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
        "HOST2016_DRAWING_QUERY_CAPABILITIES_ACCEPT",
        "drawing query capability is accepted only when explicitly advertised",
        DrawingQueryCapabilitiesAccept),
    new SpecCase(
        "HOST2016_DRAWING_QUERY_CAPABILITIES_REJECT_MISSING",
        "missing drawing query capability is rejected",
        DrawingQueryCapabilitiesRejectMissing),
    new SpecCase(
        "HOST2016_DRAWING_SNAPSHOT_IS_DEEP_MANAGED",
        "the Agent drawing snapshot is detached from mutable source contracts",
        DrawingSnapshotIsDeepManaged),
    new SpecCase(
        "HOST2016_DRAWING_SNAPSHOT_TAKES_FROZEN_OWNERSHIP",
        "runtime publication reuses the already frozen private entity array",
        DrawingSnapshotTakesFrozenOwnership),
    new SpecCase(
        "HOST2016_DRAWING_INDEX_PERFORMANCE_TELEMETRY",
        "scan slices and both local and Agent queries produce monotonic host-local telemetry",
        DrawingIndexPerformanceTelemetry),
    new SpecCase(
        "HOST2016_DRAWING_SNAPSHOT_CANCEL_AND_STALE",
        "snapshot queries honor cancellation and reject invalidated generations",
        DrawingSnapshotCancelAndStale),
    new SpecCase(
        "HOST2016_DRAWING_INDEX_ONLY_TURN_AND_QUERY",
        "a valid DrawingIndex can start a context-free turn and serve a bound query",
        DrawingIndexOnlyTurnAndQuery),
    new SpecCase(
        "HOST2016_DRAWING_QUERY_BEFORE_START_RESPONSE",
        "an exact early reverse query binds the Provider turn before start returns",
        DrawingQueryBeforeStartResponse),
    new SpecCase(
        "HOST2016_DRAWING_QUERY_IDENTITY_MISMATCH",
        "request, thread, turn and snapshot identities cannot be mixed",
        DrawingQueryIdentityMismatch),
    new SpecCase(
        "HOST2016_DRAWING_QUERY_REJECTS_STALE_INDEX",
        "an invalidated DrawingIndex cannot serve an active turn",
        DrawingQueryRejectsStaleIndex),
    new SpecCase(
        "HOST2016_DRAWING_QUERY_REJECTS_TERMINAL_TURN",
        "a completed turn rejects late drawing queries",
        DrawingQueryRejectsTerminalTurn),
    new SpecCase(
        "HOST2016_SELECTION_AND_INDEX_DOCUMENT_MUST_MATCH",
        "selection context and DrawingIndex from different drawings fail closed",
        SelectionAndIndexDocumentMustMatch),
    new SpecCase(
        "HOST2016_DRAWING_QUERY_BOUNDARY_HAS_NO_AUTODESK_REFERENCE",
        "the tested drawing query worker boundary has no Autodesk assembly dependency",
        DrawingQueryBoundaryHasNoAutodeskReference),
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
        "HOST2016_REQUEST_ID_IS_HOST_OWNED_AND_TERMINAL",
        "Host request ids remain separate from Provider turn ids and terminal turns reject late events",
        RequestIdIsHostOwnedAndTerminal),
    new SpecCase(
        "HOST2016_ACTIVE_TURN_REJECTS_DUPLICATE_ASK",
        "A second ASK cannot overwrite the active Host request",
        ActiveTurnRejectsDuplicateAsk),
    new SpecCase(
        "HOST2016_CANCEL_IS_IDEMPOTENT",
        "Duplicate cancellation calls share one Provider interrupt and terminal state cannot regress",
        CancelIsIdempotent),
    new SpecCase(
        "HOST2016_CANCEL_BEFORE_PROVIDER_TURN_IS_BOUND",
        "Cancellation requested during turn startup is dispatched once after Provider identity arrives",
        CancelBeforeProviderTurnIsBound),
    new SpecCase(
        "HOST2016_CANCEL_FAILURE_CAN_RETRY",
        "A failed Provider interrupt restores running state and allows one explicit retry",
        CancelFailureCanRetry),
    new SpecCase(
        "HOST2016_TURN_TIMEOUT_FAILS_CLOSED",
        "A turn without a terminal event times out, interrupts once, and rejects late work",
        TurnTimeoutFailsClosed),
    new SpecCase(
        "HOST2016_NEW_CONVERSATION_CREATES_FRESH_THREAD",
        "A new Host conversation gets a fresh system id and Provider thread before the next turn",
        NewConversationCreatesFreshThread),
    new SpecCase(
        "HOST2016_NEW_CONVERSATION_REJECTS_ACTIVE_TURN",
        "A new conversation cannot overwrite an active Host turn",
        NewConversationRejectsActiveTurn),
    new SpecCase(
        "HOST2016_DOCUMENT_CHANGE_CREATES_FRESH_CONVERSATION",
        "A context from another drawing cannot reuse the previous drawing's Codex thread",
        DocumentChangeCreatesFreshConversation),
    new SpecCase(
        "HOST2016_DOCUMENT_ACTIVATION_INVALIDATES_ACTIVE_CONVERSATION",
        "A drawing activation terminates the old turn and rejects its late events",
        DocumentActivationInvalidatesActiveConversation),
    new SpecCase(
        "HOST2016_OLD_DOCUMENT_EVENTS_CANNOT_UPDATE_NEW_CONVERSATION",
        "A late event from drawing A cannot update drawing B even if Provider turn ids collide",
        OldDocumentEventsCannotUpdateNewConversation),
    new SpecCase(
        "HOST2016_CLEAR_CONVERSATION_DEFERS_FRESH_THREAD",
        "Clearing a completed conversation forces a fresh Provider thread on the next ASK",
        ClearConversationDefersFreshThread),
    new SpecCase(
        "HOST2016_SAME_DOCUMENT_RECAPTURE_KEEPS_CONVERSATION",
        "Clearing and recapturing CAD context in the same drawing keeps the Codex conversation",
        SameDocumentRecaptureKeepsConversation),
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
    new SpecCase(
        "HOST2016_PALETTE_INDEX_VIEW_MAPS_REAL_STATES",
        "the palette drawing-index view maps every protocol status without inventing state",
        PaletteIndexViewMapsRealStates),
    new SpecCase(
        "HOST2016_PALETTE_INDEX_VIEW_KEEPS_REAL_PROGRESS",
        "the palette drawing-index view carries descriptor counts and progress verbatim",
        PaletteIndexViewKeepsRealProgress),
    new SpecCase(
        "HOST2016_PALETTE_AGENT_STATUS_CLASSIFIES_KNOWN_MESSAGES",
        "the palette agent status view only colors known Host messages and stays neutral otherwise",
        PaletteAgentStatusClassifiesKnownMessages),
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

static Task DrawingQueryCapabilitiesAccept()
{
    True(
        MvpAgentCapabilityPolicy.SupportsDrawingQuery(
            CreateCapabilities(true, true, true)),
        "drawing query capability should be accepted.");
    return Task.CompletedTask;
}

static Task DrawingQueryCapabilitiesRejectMissing()
{
    True(
        !MvpAgentCapabilityPolicy.SupportsDrawingQuery(
            CreateCapabilities(true, true, false)),
        "missing drawing query capability should be rejected.");
    True(
        !MvpAgentCapabilityPolicy.SupportsDrawingQuery(null),
        "null drawing query capabilities should be rejected.");
    return Task.CompletedTask;
}

static Task DrawingSnapshotIsDeepManaged()
{
    var descriptor = CreateReadyDrawingDescriptor("deep-managed-document");
    var entity = CreateDrawingEntity("object-deep-managed", "Layer-A");
    var validity = new DrawingIndexSnapshotValidity();
    var snapshot = new DrawingIndexAgentSnapshot(
        7,
        descriptor,
        new[] { entity },
        validity);

    descriptor.DocumentId = "mutated-document";
    descriptor.IndexId = "mutated-index";
    entity.Layer = "Mutated-Layer";
    entity.EntityType = "circle";

    var request = CreateDrawingQueryRequest(
        "request-deep-managed",
        "thread-deep-managed",
        "turn-deep-managed",
        "query-deep-managed");
    var response = snapshot.Query(request, CancellationToken.None);
    True(snapshot.Generation == 7, "snapshot generation changed.");
    True(
        string.Equals(response.DocumentId, "deep-managed-document", StringComparison.Ordinal),
        "snapshot document identity was mutated through its source descriptor.");
    True(
        string.Equals(response.Entities[0].Layer, "Layer-A", StringComparison.Ordinal),
        "snapshot entity was mutated through its source contract.");
    True(
        string.Equals(response.Entities[0].EntityType, "line", StringComparison.Ordinal),
        "snapshot entity type was mutated through its source contract.");
    return Task.CompletedTask;
}

static Task DrawingSnapshotTakesFrozenOwnership()
{
    var descriptor = CreateReadyDrawingDescriptor("owned-frozen-document");
    var frozenEntities = new[] { CreateDrawingEntity("object-owned-frozen", "Layer-A") };
    var snapshot = DrawingIndexAgentSnapshot.CreateFromOwnedFrozenEntities(
        19,
        descriptor,
        frozenEntities,
        new DrawingIndexSnapshotValidity());
    var entitiesField = typeof(DrawingIndexAgentSnapshot).GetField(
        "entities",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("snapshot entity storage field was not found.");

    True(
        ReferenceEquals(frozenEntities, entitiesField.GetValue(snapshot)),
        "runtime publication cloned the already frozen entity array.");
    return Task.CompletedTask;
}

static Task DrawingSnapshotCancelAndStale()
{
    DrawingIndexSnapshotValidity validity;
    var snapshot = CreateDrawingSnapshot("cancel-stale-document", 8, out validity);
    var request = CreateDrawingQueryRequest(
        "request-cancel-stale",
        "thread-cancel-stale",
        "turn-cancel-stale",
        "query-cancel-stale");

    var cancellationObserved = false;
    try
    {
        snapshot.Query(request, new CancellationToken(true));
    }
    catch (OperationCanceledException)
    {
        cancellationObserved = true;
    }
    True(cancellationObserved, "snapshot query ignored cancellation.");

    validity.Invalidate();
    DrawingIndexQueryException? stale = null;
    try
    {
        snapshot.Query(request, CancellationToken.None);
    }
    catch (DrawingIndexQueryException exception)
    {
        stale = exception;
    }
    True(
        stale != null
        && string.Equals(stale.Code, "drawing_index_stale", StringComparison.Ordinal),
        "invalidated snapshot did not return the stable stale code.");
    return Task.CompletedTask;
}

static async Task DrawingIndexOnlyTurnAndQuery()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-index-only",
        "system-session-index-only");
    DrawingIndexSnapshotValidity validity;
    var snapshot = CreateDrawingSnapshot("document-index-only", 9, out validity);

    await client.AskAsync(
            "summarize the indexed drawing",
            null,
            null,
            snapshot,
            CancellationToken.None)
        .ConfigureAwait(false);
    var turnRequest = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("index-only turn request was not captured.");
    True(turnRequest.ContextV2 == null, "index-only turn unexpectedly sent selection context.");
    True(
        string.IsNullOrEmpty(turnRequest.ContextV2Sha256),
        "index-only turn unexpectedly sent a selection context hash.");

    var queryRequest = CreateDrawingQueryRequest(
        turnRequest.ClientTurnId,
        turnRequest.ThreadId,
        "fake-turn-1",
        "query-index-only");
    var response = await client.HandleDrawingQueryAsync(
            queryRequest,
            CancellationToken.None)
        .ConfigureAwait(false);
    True(
        AgentBridgeContractValidator.ValidateDrawingQueryResponse(
            queryRequest,
            response).Length == 0,
        "index-only query response violated the Bridge contract.");
    True(
        string.Equals(
            response.Query.DocumentId,
            "document-index-only",
            StringComparison.Ordinal),
        "index-only query was not bound to the Host-owned document.");
    Equal(1, response.Query.ReturnedCount, "Index-only returned entity count");
}

static async Task DrawingQueryBeforeStartResponse()
{
    var bridge = new FakeAgentBridgeClient
    {
        DelayStartTurnResponse = true,
    };
    using var client = new MvpAgentClient(
        bridge,
        "thread-query-before-start-response",
        "system-session-query-before-start-response");
    DrawingIndexSnapshotValidity validity;
    var snapshot = CreateDrawingSnapshot("document-query-before-start-response", 18, out validity);

    var askTask = client.AskAsync(
        "query before Provider start response",
        null,
        null,
        snapshot,
        CancellationToken.None);
    var turnRequest = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("early query turn request was not captured.");
    var pendingResponse = bridge.PendingStartTurnResponse
        ?? throw new InvalidOperationException("early query Provider response was not delayed.");
    var queryRequest = CreateDrawingQueryRequest(
        turnRequest.ClientTurnId,
        turnRequest.ThreadId,
        pendingResponse.TurnId,
        "query-before-start-response");

    var response = await client.HandleDrawingQueryAsync(
            queryRequest,
            CancellationToken.None)
        .ConfigureAwait(false);
    True(
        AgentBridgeContractValidator.ValidateDrawingQueryResponse(
            queryRequest,
            response).Length == 0,
        "early drawing query response violated the Bridge contract.");
    bridge.CompletePendingStartTurn();
    await askTask.ConfigureAwait(false);
}

static async Task DrawingQueryIdentityMismatch()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-query-identity",
        "system-session-query-identity");
    DrawingIndexSnapshotValidity validity;
    var snapshot = CreateDrawingSnapshot("document-query-identity", 10, out validity);
    await client.AskAsync(
            "query identity",
            null,
            null,
            snapshot,
            CancellationToken.None)
        .ConfigureAwait(false);
    var turnRequest = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("query identity turn request was not captured.");
    var mismatched = CreateDrawingQueryRequest(
        "different-request-id",
        turnRequest.ThreadId,
        "fake-turn-1",
        "query-identity-mismatch");

    var failure = await InvokeAndExpectBridgeClientFailure(
            () => client.HandleDrawingQueryAsync(mismatched, CancellationToken.None))
        .ConfigureAwait(false);
    True(
        string.Equals(
            failure.Code,
            AgentBridgeErrorCodes.ResultIdentityMismatch,
            StringComparison.Ordinal),
        "drawing query identity mismatch returned the wrong stable code.");
}

static async Task DrawingQueryRejectsStaleIndex()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-query-stale",
        "system-session-query-stale");
    DrawingIndexSnapshotValidity validity;
    var snapshot = CreateDrawingSnapshot("document-query-stale", 11, out validity);
    await client.AskAsync(
            "stale index",
            null,
            null,
            snapshot,
            CancellationToken.None)
        .ConfigureAwait(false);
    var turnRequest = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("stale query turn request was not captured.");
    validity.Invalidate();
    var queryRequest = CreateDrawingQueryRequest(
        turnRequest.ClientTurnId,
        turnRequest.ThreadId,
        "fake-turn-1",
        "query-stale-index");

    var failure = await InvokeAndExpectBridgeClientFailure(
            () => client.HandleDrawingQueryAsync(queryRequest, CancellationToken.None))
        .ConfigureAwait(false);
    True(
        string.Equals(
            failure.Code,
            AgentBridgeErrorCodes.DrawingQueryUnavailable,
            StringComparison.Ordinal),
        "stale DrawingIndex returned the wrong stable code.");
}

static async Task DrawingQueryRejectsTerminalTurn()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-query-terminal",
        "system-session-query-terminal");
    DrawingIndexSnapshotValidity validity;
    var snapshot = CreateDrawingSnapshot("document-query-terminal", 12, out validity);
    await client.AskAsync(
            "terminal query",
            null,
            null,
            snapshot,
            CancellationToken.None)
        .ConfigureAwait(false);
    var turnRequest = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("terminal query turn request was not captured.");
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnCompleted,
        ThreadId = turnRequest.ThreadId,
        TurnId = "fake-turn-1",
    });
    var queryRequest = CreateDrawingQueryRequest(
        turnRequest.ClientTurnId,
        turnRequest.ThreadId,
        "fake-turn-1",
        "query-terminal-turn");

    var failure = await InvokeAndExpectBridgeClientFailure(
            () => client.HandleDrawingQueryAsync(queryRequest, CancellationToken.None))
        .ConfigureAwait(false);
    True(
        string.Equals(
            failure.Code,
            AgentBridgeErrorCodes.ResultIdentityMismatch,
            StringComparison.Ordinal),
        "late terminal query returned the wrong stable code.");
}

static async Task SelectionAndIndexDocumentMustMatch()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-document-mismatch",
        "system-session-document-mismatch");
    DrawingIndexSnapshotValidity validity;
    var snapshot = CreateDrawingSnapshot("index-document", 13, out validity);
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "selection-document" },
        },
        ContextSha256 = new string('e', 64),
    };

    var rejected = false;
    try
    {
        await client.AskAsync(
                "mismatched drawing inputs",
                context,
                () => true,
                snapshot,
                CancellationToken.None)
            .ConfigureAwait(false);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }
    True(rejected, "selection context and index from different drawings were mixed.");
    Equal(0, bridge.StartTurnV2Count, "Mismatched document turn start count");
}

static Task DrawingQueryBoundaryHasNoAutodeskReference()
{
    var references = typeof(DrawingIndexAgentSnapshot).Assembly.GetReferencedAssemblies();
    True(
        !references.Any(reference =>
            string.Equals(reference.Name, "acmgd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reference.Name, "acdbmgd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reference.Name, "accoremgd", StringComparison.OrdinalIgnoreCase)
            || (reference.Name ?? string.Empty).StartsWith(
                "Autodesk.",
                StringComparison.OrdinalIgnoreCase)),
        "drawing query boundary acquired an Autodesk assembly dependency.");
    return Task.CompletedTask;
}

static AgentCapabilitiesResponse CreateCapabilities(
    bool includeV2Method,
    bool includeV2Schema,
    bool includeDrawingQuery = false)
{
    var methods = new List<string> { AgentBridgeMethods.StartTurn };
    if (includeV2Method)
    {
        methods.Add(AgentBridgeMethods.StartTurnV2);
    }
    if (includeDrawingQuery)
    {
        methods.Add(AgentBridgeMethods.QueryDrawing);
    }
    return new AgentCapabilitiesResponse
    {
        Methods = methods.ToArray(),
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

static DrawingIndexAgentSnapshot CreateDrawingSnapshot(
    string documentId,
    int generation,
    out DrawingIndexSnapshotValidity validity)
{
    validity = new DrawingIndexSnapshotValidity();
    return new DrawingIndexAgentSnapshot(
        generation,
        CreateReadyDrawingDescriptor(documentId),
        new[] { CreateDrawingEntity("object-" + generation, "Layer-A") },
        validity);
}

static Task DrawingIndexPerformanceTelemetry()
{
    var metrics = new DrawingIndexPerformanceMetrics();
    metrics.RecordIdleSlice(
        true,
        TimeSpan.FromMilliseconds(8.25),
        TimeSpan.FromMilliseconds(8.25));
    metrics.RecordIdleSlice(
        false,
        TimeSpan.FromMilliseconds(13.5),
        TimeSpan.FromMilliseconds(25));
    metrics.CompleteScan(TimeSpan.FromMilliseconds(24));

    var validity = new DrawingIndexSnapshotValidity();
    var drawingSnapshot = DrawingIndexAgentSnapshot.CreateFromOwnedFrozenEntities(
        1,
        CreateReadyDrawingDescriptor("doc-performance"),
        new[] { CreateDrawingEntity("object-performance", "Layer-A") },
        validity,
        metrics);
    drawingSnapshot.Query(
        CreateDrawingQueryRequest(
            "request-performance",
            "thread-performance",
            "turn-performance",
            "query-performance"),
        CancellationToken.None);

    var snapshot = metrics.Snapshot();
    Equal(2, snapshot.IdleSliceCount, "Idle slice count");
    Equal(1, snapshot.PreparationSliceCount, "Preparation slice count");
    Equal(1, snapshot.ReadSliceCount, "Read slice count");
    Equal(1, snapshot.QueryCount, "Query count");
    True(
        snapshot.MaximumIdleSliceDuration == TimeSpan.FromMilliseconds(13.5),
        "Maximum idle slice duration was not retained.");
    True(
        snapshot.TotalScanDuration == TimeSpan.FromMilliseconds(25),
        "A shorter completion sample regressed total scan duration.");
    True(
        DrawingIndexPerformanceMetrics.FormatMilliseconds(
            snapshot.MaximumIdleSliceDuration) == "13.500",
        "Performance milliseconds are not invariant and stable.");
    return Task.CompletedTask;
}

static DrawingIndexDescriptor CreateReadyDrawingDescriptor(string documentId)
{
    return new DrawingIndexDescriptor
    {
        IndexId = "index-" + documentId,
        DocumentId = documentId,
        DrawingFingerprint = new string('a', 64),
        DocumentRevision = 17,
        Scope = DrawingIndexScopes.Drawing,
        Status = DrawingIndexStatuses.Ready,
        Complete = true,
        Limited = false,
        EntityCount = 1,
        IndexedEntityCount = 1,
        UnsupportedEntityCount = 0,
        FailedEntityCount = 0,
        ProgressPercent = 100,
        EstimatedManagedBytes = 512,
        StartedAtUtc = "2026-07-22T00:00:00.000Z",
        CompletedAtUtc = "2026-07-22T00:00:01.000Z",
        LimitReason = string.Empty,
    };
}

static CadQueryEntity CreateDrawingEntity(string objectId, string layer)
{
    return new CadQueryEntity
    {
        ObjectId = objectId,
        EntityType = "line",
        ActualType = "Line",
        Layer = layer,
        Space = "model_space",
        BlockName = string.Empty,
        TextExcerpt = string.Empty,
        Unsupported = false,
        ReadStatus = CadQueryReadStatuses.Parsed,
    };
}

static AgentDrawingQueryRequest CreateDrawingQueryRequest(
    string requestId,
    string threadId,
    string turnId,
    string queryId)
{
    return new AgentDrawingQueryRequest
    {
        RequestId = requestId,
        ThreadId = threadId,
        TurnId = turnId,
        ToolCallId = "tool-" + queryId,
        QueryId = queryId,
        Filter = new CadQueryFilter(),
        PageSize = 20,
        Cursor = string.Empty,
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

static async Task RequestIdIsHostOwnedAndTerminal()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-request-identity",
        "system-session-request-identity");
    var statuses = new List<string>();
    var textEvents = new List<string>();
    client.StatusChanged += statuses.Add;
    client.TextChanged += textEvents.Add;
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2(),
        ContextSha256 = new string('c', 64),
    };

    await client.AskAsync(
            "first request",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    var firstRequest = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("First Host request was not captured.");
    True(
        Guid.TryParseExact(firstRequest.ClientTurnId, "N", out _),
        "Host request_id was not a canonical 32-character identifier.");
    True(
        !string.Equals(firstRequest.ClientTurnId, "fake-turn-1", StringComparison.Ordinal),
        "Host request_id was confused with the Provider turn id.");

    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnStarted,
        TurnId = "fake-turn-1",
    });
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageDelta,
        TurnId = "fake-turn-1",
        Delta = "accepted-text",
    });
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageCompleted,
        TurnId = "fake-turn-1",
    });
    True(
        statuses.TrueForAll(value =>
            !value.Contains("state=completed", StringComparison.Ordinal)),
        "Assistant message completion incorrectly finalized the Host request.");
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnCompleted,
        TurnId = "fake-turn-1",
    });

    True(
        statuses.Exists(value =>
            value.Contains("request_id=" + firstRequest.ClientTurnId, StringComparison.Ordinal)
            && value.Contains("state=completed", StringComparison.Ordinal)),
        "Completed state did not preserve the Host request_id.");
    var statusCountAtTerminal = statuses.Count;
    var textCountAtTerminal = textEvents.Count;
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnStarted,
        TurnId = "fake-turn-1",
    });
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageDelta,
        TurnId = "fake-turn-1",
        Delta = "late-text-must-be-ignored",
    });
    Equal(statusCountAtTerminal, statuses.Count, "Late terminal status event count");
    Equal(textCountAtTerminal, textEvents.Count, "Late terminal text event count");

    await client.AskAsync(
            "second request",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    var secondRequest = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("Second Host request was not captured.");
    True(
        !string.Equals(
            firstRequest.ClientTurnId,
            secondRequest.ClientTurnId,
            StringComparison.Ordinal),
        "Two Host requests reused the same request_id.");
    Equal(2, bridge.StartTurnV2Count, "Host request start count");
}

static async Task ActiveTurnRejectsDuplicateAsk()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-duplicate-ask",
        "system-session-duplicate-ask");
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2(),
        ContextSha256 = new string('d', 64),
    };

    await client.AskAsync(
            "first active request",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    var firstRequest = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("Active Host request was not captured.");
    var failure = await ExpectTurnFailure(
            client.AskAsync(
                "must not overwrite",
                context,
                () => true,
                CancellationToken.None))
        .ConfigureAwait(false);

    True(
        string.Equals(failure.RequestId, firstRequest.ClientTurnId, StringComparison.Ordinal),
        "Busy failure did not identify the active Host request.");
    True(
        string.Equals(failure.TurnState, MvpAgentTurnStates.Running, StringComparison.Ordinal),
        "Busy failure did not preserve the active running state.");
    True(
        failure.InnerException is AgentBridgeClientException bridgeFailure
        && string.Equals(bridgeFailure.Code, AgentBridgeErrorCodes.Busy, StringComparison.Ordinal),
        "Duplicate ASK did not return the stable busy error code.");
    var display = MvpAgentFailureFormatter
        .FromException(failure, MvpAgentFailureStages.SendingTurn)
        .FormatForUser("发送只读问题");
    True(
        display.Contains("error_code=busy", StringComparison.Ordinal)
        && display.Contains("request_id=" + firstRequest.ClientTurnId, StringComparison.Ordinal)
        && display.Contains("state=running", StringComparison.Ordinal),
        "Structured busy failure lost the Host request identity or state.");
    True(
        !display.Contains("已有只读 Codex 回合", StringComparison.Ordinal),
        "Structured busy failure leaked the internal exception message.");
    Equal(1, bridge.StartTurnV2Count, "Duplicate ASK Provider start count");
}

static async Task CancelIsIdempotent()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-cancel-idempotent",
        "system-session-cancel-idempotent");
    var statuses = new List<string>();
    var textEvents = new List<string>();
    client.StatusChanged += statuses.Add;
    client.TextChanged += textEvents.Add;
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2(),
        ContextSha256 = new string('e', 64),
    };

    await client.AskAsync(
            "request to cancel",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    var request = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("Cancelable Host request was not captured.");
    var firstCancel = client.CancelActiveTurnAsync(CancellationToken.None);
    var secondCancel = client.CancelActiveTurnAsync(CancellationToken.None);
    True(ReferenceEquals(firstCancel, secondCancel), "Duplicate cancellation did not share one task.");
    await Task.WhenAll(firstCancel, secondCancel).ConfigureAwait(false);

    Equal(1, bridge.InterruptTurnCount, "Duplicate cancellation Provider interrupt count");
    True(
        bridge.LastInterruptRequest != null
        && string.Equals(
            bridge.LastInterruptRequest.TurnId,
            "fake-turn-1",
            StringComparison.Ordinal),
        "Cancellation did not target the accepted Provider turn.");
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnCancelled,
        TurnId = "fake-turn-1",
    });
    True(
        statuses.Exists(value =>
            value.Contains("request_id=" + request.ClientTurnId, StringComparison.Ordinal)
            && value.Contains("state=cancelled", StringComparison.Ordinal)),
        "Cancelled terminal state did not preserve the Host request_id.");

    await client.CancelActiveTurnAsync(CancellationToken.None).ConfigureAwait(false);
    Equal(1, bridge.InterruptTurnCount, "Cancellation count after terminal state");
    var statusCountAtTerminal = statuses.Count;
    var textCountAtTerminal = textEvents.Count;
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnStarted,
        TurnId = "fake-turn-1",
    });
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageDelta,
        TurnId = "fake-turn-1",
        Delta = "late-after-cancel",
    });
    Equal(statusCountAtTerminal, statuses.Count, "Late cancelled status event count");
    Equal(textCountAtTerminal, textEvents.Count, "Late cancelled text event count");
}

static async Task CancelBeforeProviderTurnIsBound()
{
    var bridge = new FakeAgentBridgeClient
    {
        DelayStartTurnResponse = true,
    };
    using var client = new MvpAgentClient(
        bridge,
        "thread-cancel-before-bind",
        "system-session-cancel-before-bind");
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2(),
        ContextSha256 = new string('f', 64),
    };

    var askTask = client.AskAsync(
        "cancel before Provider response",
        context,
        () => true,
        CancellationToken.None);
    Equal(1, bridge.StartTurnV2Count, "Pending Provider start count");
    var firstCancel = client.CancelActiveTurnAsync(CancellationToken.None);
    var secondCancel = client.CancelActiveTurnAsync(CancellationToken.None);
    True(ReferenceEquals(firstCancel, secondCancel), "Pending-turn cancellation did not stay idempotent.");
    Equal(0, bridge.InterruptTurnCount, "Interrupt count before Provider turn binding");

    bridge.CompletePendingStartTurn();
    await askTask.ConfigureAwait(false);
    await Task.WhenAll(firstCancel, secondCancel).ConfigureAwait(false);
    Equal(1, bridge.InterruptTurnCount, "Interrupt count after Provider turn binding");
    True(
        bridge.LastInterruptRequest != null
        && string.Equals(
            bridge.LastInterruptRequest.TurnId,
            "fake-turn-1",
            StringComparison.Ordinal),
        "Pending cancellation targeted the wrong Provider turn.");
}

static async Task CancelFailureCanRetry()
{
    var bridge = new FakeAgentBridgeClient
    {
        InterruptFailuresRemaining = 1,
    };
    using var client = new MvpAgentClient(
        bridge,
        "thread-cancel-retry",
        "system-session-cancel-retry");
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2(),
        ContextSha256 = new string('1', 64),
    };

    await client.AskAsync(
            "cancel with one retry",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    var request = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("Retryable cancellation request was not captured.");
    var firstFailure = await ExpectTurnFailure(
            client.CancelActiveTurnAsync(CancellationToken.None))
        .ConfigureAwait(false);
    True(
        string.Equals(firstFailure.RequestId, request.ClientTurnId, StringComparison.Ordinal)
        && string.Equals(firstFailure.TurnState, MvpAgentTurnStates.Running, StringComparison.Ordinal),
        "Failed cancellation did not restore the active request to running.");

    await client.CancelActiveTurnAsync(CancellationToken.None).ConfigureAwait(false);
    Equal(2, bridge.InterruptTurnCount, "Cancellation retry Provider interrupt count");
}

static async Task TurnTimeoutFailsClosed()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-turn-timeout",
        "system-session-turn-timeout",
        TimeSpan.FromMilliseconds(40));
    var statuses = new List<string>();
    var textEvents = new List<string>();
    var timeoutStatus = new TaskCompletionSource<string>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    client.ErrorChanged += value =>
    {
        statuses.Add(value);
        if (value.Contains("error_code=timeout", StringComparison.Ordinal))
        {
            timeoutStatus.TrySetResult(value);
        }
    };
    client.TextChanged += textEvents.Add;
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2(),
        ContextSha256 = new string('2', 64),
    };

    await client.AskAsync(
            "request that never reaches a terminal event",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    var request = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("Timed Host request was not captured.");
    var completed = await Task.WhenAny(
            timeoutStatus.Task,
            Task.Delay(TimeSpan.FromSeconds(2)))
        .ConfigureAwait(false);
    True(ReferenceEquals(completed, timeoutStatus.Task), "Host turn timeout did not fire.");
    var timeoutDisplay = await timeoutStatus.Task.ConfigureAwait(false);
    True(
        timeoutDisplay.Contains("request_id=" + request.ClientTurnId, StringComparison.Ordinal)
        && timeoutDisplay.Contains("state=failed", StringComparison.Ordinal)
        && timeoutDisplay.Contains("retryable=true", StringComparison.Ordinal),
        "Turn timeout lost structured request identity or terminal state.");
    Equal(1, bridge.InterruptTurnCount, "Turn timeout best-effort interrupt count");

    var failure = await ExpectBridgeClientFailure(
            client.AskAsync(
                "must fail closed after timeout",
                context,
                () => true,
                CancellationToken.None))
        .ConfigureAwait(false);
    True(
        string.Equals(failure.Code, AgentBridgeErrorCodes.Timeout, StringComparison.Ordinal),
        "ASK after timeout did not preserve the stable timeout code.");
    var statusCountAtTimeout = statuses.Count;
    var textCountAtTimeout = textEvents.Count;
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageDelta,
        TurnId = "fake-turn-1",
        Delta = "late-after-timeout",
    });
    Equal(statusCountAtTimeout, statuses.Count, "Late timeout status event count");
    Equal(textCountAtTimeout, textEvents.Count, "Late timeout text event count");
}

static async Task NewConversationCreatesFreshThread()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-before-new-conversation",
        "system-session-before-new-conversation");

    await client.NewConversationAsync(
            "doc-new-conversation",
            CancellationToken.None)
        .ConfigureAwait(false);

    Equal(1, bridge.StartThreadCount, "New conversation Provider thread count");
    var startThread = bridge.LastStartThreadRequest
        ?? throw new InvalidOperationException("New conversation request was not captured.");
    True(
        Guid.TryParseExact(startThread.ConversationId, "N", out _),
        "New Host system session id was not a canonical 32-character identifier.");
    True(
        !string.Equals(
            startThread.ConversationId,
            bridge.LastStartedThreadId,
            StringComparison.Ordinal),
        "Host system session id was confused with the Provider thread id.");

    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-new-conversation",
            },
        },
        ContextSha256 = new string('3', 64),
    };
    await client.AskAsync(
            "first request in fresh conversation",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);

    True(
        bridge.LastStartTurnV2Request != null
        && string.Equals(
            bridge.LastStartTurnV2Request.ThreadId,
            bridge.LastStartedThreadId,
            StringComparison.Ordinal),
        "The first turn after a new conversation did not use the fresh Provider thread.");
}

static async Task NewConversationRejectsActiveTurn()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-active-before-new-conversation",
        "system-session-active-before-new-conversation");
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-active-before-new-conversation",
            },
        },
        ContextSha256 = new string('4', 64),
    };
    await client.AskAsync(
            "keep this request active",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);

    var failure = await ExpectTurnFailure(
            client.NewConversationAsync(
                "doc-active-before-new-conversation",
                CancellationToken.None))
        .ConfigureAwait(false);

    True(
        failure.InnerException is AgentBridgeClientException bridgeFailure
        && string.Equals(bridgeFailure.Code, AgentBridgeErrorCodes.Busy, StringComparison.Ordinal),
        "New conversation during an active turn did not return the stable busy error code.");
    Equal(0, bridge.StartThreadCount, "Provider thread count after rejected new conversation");
}

static async Task DocumentChangeCreatesFreshConversation()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-document-a",
        "system-session-document-a");
    var contextA = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "document-a" },
        },
        ContextSha256 = new string('5', 64),
    };
    await client.AskAsync(
            "question for drawing A",
            contextA,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnCompleted,
        TurnId = "fake-turn-1",
    });

    var contextB = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "document-b" },
        },
        ContextSha256 = new string('6', 64),
    };
    await client.AskAsync(
            "question for drawing B",
            contextB,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);

    Equal(1, bridge.StartThreadCount, "Cross-document fresh Provider thread count");
    True(
        bridge.LastStartTurnV2Request != null
        && string.Equals(
            bridge.LastStartTurnV2Request.ThreadId,
            bridge.LastStartedThreadId,
            StringComparison.Ordinal),
        "Drawing B reused drawing A's Provider thread.");
}

static async Task DocumentActivationInvalidatesActiveConversation()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-before-document-activation",
        "system-session-before-document-activation");
    var errors = new List<string>();
    var textEvents = new List<string>();
    client.ErrorChanged += errors.Add;
    client.TextChanged += textEvents.Add;
    var contextA = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "activated-document-a" },
        },
        ContextSha256 = new string('7', 64),
    };
    await client.AskAsync(
            "active request for drawing A",
            contextA,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    var request = bridge.LastStartTurnV2Request
        ?? throw new InvalidOperationException("Drawing A request was not captured.");
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageDelta,
        TurnId = "fake-turn-1",
        Delta = "visible-text-from-drawing-a",
    });

    client.InvalidateConversationForDocumentChange();

    Equal(1, bridge.InterruptTurnCount, "Document activation Provider interrupt count");
    True(
        textEvents.Count > 0
        && string.Equals(textEvents[textEvents.Count - 1], string.Empty, StringComparison.Ordinal),
        "Document activation did not clear drawing A's visible answer text.");
    True(
        errors.Exists(value =>
            value.Contains("error_code=context_invalid", StringComparison.Ordinal)
            && value.Contains("request_id=" + request.ClientTurnId, StringComparison.Ordinal)
            && value.Contains("state=failed", StringComparison.Ordinal)),
        "Document activation did not publish a structured terminal failure.");
    var textCountAfterActivation = textEvents.Count;
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageDelta,
        TurnId = "fake-turn-1",
        Delta = "late-text-from-drawing-a",
    });
    Equal(
        textCountAfterActivation,
        textEvents.Count,
        "Late drawing A text event count after activation");

    var contextB = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "activated-document-b" },
        },
        ContextSha256 = new string('8', 64),
    };
    await client.AskAsync(
            "fresh request for drawing B",
            contextB,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    Equal(1, bridge.StartThreadCount, "Fresh thread count after document activation");
}

static async Task OldDocumentEventsCannotUpdateNewConversation()
{
    var bridge = new FakeAgentBridgeClient
    {
        ReuseProviderTurnId = true,
    };
    using var client = new MvpAgentClient(
        bridge,
        "thread-late-document-a",
        "system-session-late-document-a");
    var textEvents = new List<string>();
    client.TextChanged += textEvents.Add;
    var contextA = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "late-document-a" },
        },
        ContextSha256 = new string('9', 64),
    };
    await client.AskAsync(
            "drawing A request",
            contextA,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    var drawingAThread = bridge.LastStartTurnV2Request?.ThreadId
        ?? throw new InvalidOperationException("Drawing A thread was not captured.");
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnCompleted,
        ThreadId = drawingAThread,
        TurnId = "shared-provider-turn",
    });

    var contextB = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "late-document-b" },
        },
        ContextSha256 = new string('a', 64),
    };
    await client.AskAsync(
            "drawing B request",
            contextB,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    var drawingBThread = bridge.LastStartTurnV2Request?.ThreadId
        ?? throw new InvalidOperationException("Drawing B thread was not captured.");
    var textCountBeforeLateEvent = textEvents.Count;
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageDelta,
        ThreadId = drawingAThread,
        TurnId = "shared-provider-turn",
        Delta = "late-text-from-drawing-a",
    });
    Equal(
        textCountBeforeLateEvent,
        textEvents.Count,
        "Drawing A late event count after drawing B started");

    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.AssistantMessageDelta,
        ThreadId = drawingBThread,
        TurnId = "shared-provider-turn",
        Delta = "accepted-text-from-drawing-b",
    });
    Equal(
        textCountBeforeLateEvent + 1,
        textEvents.Count,
        "Drawing B current event count");
}

static async Task ClearConversationDefersFreshThread()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-before-clear-all",
        "system-session-before-clear-all");
    var context = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "document-clear-all" },
        },
        ContextSha256 = new string('b', 64),
    };
    await client.AskAsync(
            "request before clear all",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnCompleted,
        TurnId = "fake-turn-1",
    });

    client.ClearConversation();
    Equal(0, bridge.StartThreadCount, "Provider thread count during local clear");

    await client.AskAsync(
            "request after clear all",
            context,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    Equal(1, bridge.StartThreadCount, "Provider thread count after local clear");
    True(
        bridge.LastStartTurnV2Request != null
        && string.Equals(
            bridge.LastStartTurnV2Request.ThreadId,
            bridge.LastStartedThreadId,
            StringComparison.Ordinal),
        "The first request after clearing the conversation reused the old Provider thread.");
}

static async Task SameDocumentRecaptureKeepsConversation()
{
    var bridge = new FakeAgentBridgeClient();
    using var client = new MvpAgentClient(
        bridge,
        "thread-same-document",
        "system-session-same-document");
    var firstContext = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "same-document" },
        },
        ContextSha256 = new string('c', 64),
    };
    await client.AskAsync(
            "first same-document request",
            firstContext,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);
    bridge.RaiseEvent(new AgentBridgeEvent
    {
        Kind = AgentBridgeEventKinds.TurnCompleted,
        TurnId = "fake-turn-1",
    });

    var recapturedContext = new UnifiedContextState
    {
        Published = true,
        Context = new CadContextJsonV2
        {
            Document = new CadContextDocumentV2 { DocumentId = "same-document" },
        },
        ContextSha256 = new string('d', 64),
    };
    await client.AskAsync(
            "second same-document request after context clear and recapture",
            recapturedContext,
            () => true,
            CancellationToken.None)
        .ConfigureAwait(false);

    Equal(0, bridge.StartThreadCount, "Same-document Provider thread creation count");
    Equal(2, bridge.StartTurnV2Count, "Same-document request count");
    True(
        bridge.LastStartTurnV2Request != null
        && string.Equals(
            bridge.LastStartTurnV2Request.ThreadId,
            "thread-same-document",
            StringComparison.Ordinal),
        "Same-document context recapture unexpectedly replaced the Codex conversation.");
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

static Task PaletteIndexViewMapsRealStates()
{
    var expectations = new[]
    {
        new { Status = DrawingIndexStatuses.NotBuilt, Label = "未建立", Tone = PaletteStatusTone.Neutral, CanStart = true, CanCancel = false },
        new { Status = DrawingIndexStatuses.Preparing, Label = "准备中", Tone = PaletteStatusTone.Busy, CanStart = false, CanCancel = true },
        new { Status = DrawingIndexStatuses.Scanning, Label = "扫描中", Tone = PaletteStatusTone.Busy, CanStart = false, CanCancel = true },
        new { Status = DrawingIndexStatuses.Ready, Label = "已完成", Tone = PaletteStatusTone.Success, CanStart = true, CanCancel = false },
        new { Status = DrawingIndexStatuses.Partial, Label = "部分完成", Tone = PaletteStatusTone.Warning, CanStart = true, CanCancel = false },
        new { Status = DrawingIndexStatuses.Limited, Label = "受限完成", Tone = PaletteStatusTone.Warning, CanStart = true, CanCancel = false },
        new { Status = DrawingIndexStatuses.Cancelled, Label = "已取消", Tone = PaletteStatusTone.Neutral, CanStart = true, CanCancel = false },
        new { Status = DrawingIndexStatuses.Stale, Label = "已失效", Tone = PaletteStatusTone.Warning, CanStart = true, CanCancel = false },
        new { Status = DrawingIndexStatuses.Failed, Label = "失败", Tone = PaletteStatusTone.Failure, CanStart = true, CanCancel = false },
    };

    foreach (var expectation in expectations)
    {
        var view = PaletteDrawingIndexView.FromDescriptor(new DrawingIndexDescriptor
        {
            IndexId = "index-1",
            Status = expectation.Status,
        });
        True(
            string.Equals(view.StatusLabel, expectation.Label, StringComparison.Ordinal),
            "status " + expectation.Status + " label mismatch: " + view.StatusLabel);
        True(
            view.Tone == expectation.Tone,
            "status " + expectation.Status + " tone mismatch: " + view.Tone);
        True(
            view.CanStart == expectation.CanStart,
            "status " + expectation.Status + " CanStart mismatch.");
        True(
            view.CanCancel == expectation.CanCancel,
            "status " + expectation.Status + " CanCancel mismatch.");
    }

    True(
        PaletteDrawingIndexView.FromDescriptor(null).StatusLabel == "未建立",
        "null descriptor must fall back to the not-built view.");
    return Task.CompletedTask;
}

static Task PaletteIndexViewKeepsRealProgress()
{
    var view = PaletteDrawingIndexView.FromDescriptor(new DrawingIndexDescriptor
    {
        IndexId = "index-2",
        Status = DrawingIndexStatuses.Scanning,
        Scope = DrawingIndexScopes.ModelSpace,
        EntityCount = 900,
        IndexedEntityCount = 378,
        UnsupportedEntityCount = 12,
        FailedEntityCount = 3,
        ProgressPercent = 42,
        Complete = false,
        Limited = false,
    });

    Equal(900, view.EntityCount, "descriptor entity count");
    Equal(378, view.IndexedEntityCount, "descriptor indexed count");
    Equal(12, view.UnsupportedEntityCount, "descriptor unsupported count");
    Equal(3, view.FailedEntityCount, "descriptor failed count");
    Equal(42, view.ProgressPercent, "descriptor real progress percent");
    True(
        string.Equals(view.ScopeLabel, "模型空间", StringComparison.Ordinal),
        "scope label mismatch: " + view.ScopeLabel);
    var stats = view.BuildStatsText();
    True(
        stats.IndexOf("42%", StringComparison.Ordinal) >= 0
            && stats.IndexOf("900", StringComparison.Ordinal) >= 0,
        "stats text must carry the real counts and progress: " + stats);
    return Task.CompletedTask;
}

static Task PaletteAgentStatusClassifiesKnownMessages()
{
    var expectations = new[]
    {
        new { Text = "AgentHost 在线；只读 Codex 会话已建立。", Tone = PaletteStatusTone.Success },
        new { Text = "Codex 回答完成（request_id=\"r1\", state=\"completed\"）", Tone = PaletteStatusTone.Success },
        new { Text = "正在启动并验证 AgentHost……", Tone = PaletteStatusTone.Busy },
        new { Text = "Codex 正在分析当前图纸数据（request_id=\"r2\", state=\"running\"）", Tone = PaletteStatusTone.Busy },
        new { Text = "Agent Bridge 状态：connecting", Tone = PaletteStatusTone.Busy },
        new { Text = "Agent Bridge 状态：online", Tone = PaletteStatusTone.Success },
        new { Text = "Agent Bridge 状态：degraded", Tone = PaletteStatusTone.Warning },
        new { Text = "Agent Bridge 状态：offline", Tone = PaletteStatusTone.Neutral },
        new { Text = "正在取消 Codex 回合（request_id=\"r3\", state=\"cancelling\"）", Tone = PaletteStatusTone.Warning },
        new { Text = "Codex 回合已取消（request_id=\"r3\", state=\"cancelled\"）", Tone = PaletteStatusTone.Warning },
        new { Text = "Codex 回合失败（error_code=timeout, error_stage=running_turn, retryable=true）：操作超时；连接或子进程已按 fail-closed 处理。", Tone = PaletteStatusTone.Failure },
        new { Text = "AgentHost 已停止；CAD 写入仍禁用。", Tone = PaletteStatusTone.Neutral },
        new { Text = "Agent 离线；只读模式。", Tone = PaletteStatusTone.Neutral },
        new { Text = "某种未分类的 Host 状态文本。", Tone = PaletteStatusTone.Neutral },
    };

    foreach (var expectation in expectations)
    {
        var view = PaletteAgentStatusView.FromHostStatus(expectation.Text);
        True(
            view.Tone == expectation.Tone,
            "tone mismatch for [" + expectation.Text + "]: " + view.Tone);
        True(
            string.Equals(view.DisplayText, expectation.Text, StringComparison.Ordinal),
            "display text must stay verbatim.");
    }

    True(
        PaletteAgentStatusView.FromHostStatus(null).Tone == PaletteStatusTone.Neutral,
        "null status must stay neutral.");
    True(
        PaletteAgentStatusView.FromHostStatus("   ").Tone == PaletteStatusTone.Neutral,
        "blank status must stay neutral.");
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

static async Task<AgentBridgeClientException> InvokeAndExpectBridgeClientFailure(
    Func<Task> action)
{
    try
    {
        await action().ConfigureAwait(false);
    }
    catch (AgentBridgeClientException exception)
    {
        return exception;
    }

    throw new InvalidOperationException("Expected Agent Bridge failure was not observed.");
}

static async Task<MvpAgentTurnException> ExpectTurnFailure(Task task)
{
    try
    {
        await task.ConfigureAwait(false);
    }
    catch (MvpAgentTurnException exception)
    {
        return exception;
    }

    throw new InvalidOperationException("Expected Host turn failure was not observed.");
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
    private TaskCompletionSource<AgentTurnStartV2Response>? pendingStartTurn;

    internal int StartTurnV2Count { get; private set; }

    internal int StartThreadCount { get; private set; }

    internal AgentThreadStartRequest? LastStartThreadRequest { get; private set; }

    internal string LastStartedThreadId { get; private set; } = string.Empty;

    internal AgentTurnStartV2Request? LastStartTurnV2Request { get; private set; }

    internal int InterruptTurnCount { get; private set; }

    internal AgentTurnInterruptRequest? LastInterruptRequest { get; private set; }

    internal bool DelayStartTurnResponse { get; set; }

    internal bool ReuseProviderTurnId { get; set; }

    internal int InterruptFailuresRemaining { get; set; }

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
        cancellationToken.ThrowIfCancellationRequested();
        StartThreadCount++;
        LastStartThreadRequest = request;
        LastStartedThreadId = "provider-thread-" + StartThreadCount;
        return Task.FromResult(new AgentThreadStartResponse
        {
            ThreadId = LastStartedThreadId,
        });
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
        LastStartTurnV2Request = request;
        var response = new AgentTurnStartV2Response
        {
            ThreadId = request.ThreadId,
            TurnId = ReuseProviderTurnId
                ? "shared-provider-turn"
                : "fake-turn-" + StartTurnV2Count,
            AcceptedContextV2Sha256 = request.ContextV2Sha256,
        };
        if (!DelayStartTurnResponse)
        {
            return Task.FromResult(response);
        }

        pendingStartTurn = new TaskCompletionSource<AgentTurnStartV2Response>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PendingStartTurnResponse = response;
        return pendingStartTurn.Task;
    }

    public Task InterruptTurnAsync(
        AgentTurnInterruptRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InterruptTurnCount++;
        LastInterruptRequest = request;
        if (InterruptFailuresRemaining > 0)
        {
            InterruptFailuresRemaining--;
            return Task.FromException(
                new AgentBridgeClientException(
                    AgentBridgeErrorCodes.InternalError,
                    "simulated interrupt failure"));
        }

        return Task.CompletedTask;
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

    internal AgentTurnStartV2Response? PendingStartTurnResponse { get; private set; }

    public void CompletePendingStartTurn()
    {
        var completion = pendingStartTurn
            ?? throw new InvalidOperationException("No pending Provider turn response exists.");
        var response = PendingStartTurnResponse
            ?? throw new InvalidOperationException("Pending Provider turn response is unavailable.");
        pendingStartTurn = null;
        PendingStartTurnResponse = null;
        completion.TrySetResult(response);
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
