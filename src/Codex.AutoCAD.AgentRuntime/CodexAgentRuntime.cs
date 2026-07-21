using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AppServer.Protocol;

namespace Codex.AutoCAD.AgentRuntime;

/// <summary>
/// High-level Codex conversation runtime for rich clients. It owns request defaults and turns raw
/// App Server notifications into stable, strongly typed UI events.
/// </summary>
public sealed class CodexAgentRuntime : IAsyncDisposable
{
    private readonly IAgentAppServer _appServer;
    private readonly IAgentCadProposalBroker? _cadProposalBroker;
    private readonly AgentRuntimeOptions _options;
    private readonly bool _ownsAppServer;
    private readonly string? _managedWorkspaceRoot;
    private readonly string? _defaultWorkingDirectory;
    private readonly IReadOnlyList<string>? _runtimeWorkspaceRoots;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _cadProposalConcurrency;
    private readonly object _conversationSync = new();
    private readonly HashSet<string> _activeThreads = new(StringComparer.Ordinal);
    private readonly HashSet<AgentTurnKey> _activeTurns = new();
    private readonly Dictionary<AgentTurnKey, CadTurnLifecycle> _cadTurnLifecycles = new();
    private readonly object _cadCallSync = new();
    private readonly Dictionary<AgentCadCallKey, CadCallCacheEntry> _cadCalls = new();
    private readonly LinkedList<AgentCadCallKey> _cadCallOrder = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private int _started;
    private int _disposed;

    public CodexAgentRuntime(
        AgentRuntimeOptions? options = null,
        IAgentCadProposalBroker? cadProposalBroker = null)
        : this(
            new CodexAppServerAdapter(new CodexAppServerClient(), ownsClient: true),
            options,
            ownsAppServer: true,
            cadProposalBroker)
    {
    }

    public CodexAgentRuntime(
        AppServerClientOptions clientOptions,
        AgentRuntimeOptions? options,
        IAgentCadProposalBroker? cadProposalBroker = null)
        : this(
            new CodexAppServerAdapter(
                new CodexAppServerClient(EnableExperimentalApi(clientOptions)),
                ownsClient: true),
            options,
            ownsAppServer: true,
            cadProposalBroker)
    {
        ArgumentNullException.ThrowIfNull(clientOptions);
    }

    public CodexAgentRuntime(
        IAgentAppServer appServer,
        AgentRuntimeOptions? options = null,
        bool ownsAppServer = false,
        IAgentCadProposalBroker? cadProposalBroker = null)
    {
        ArgumentNullException.ThrowIfNull(appServer);
        _appServer = appServer;
        _options = ValidateOptions(options ?? new AgentRuntimeOptions());
        _ownsAppServer = ownsAppServer;
        _cadProposalBroker = cadProposalBroker;
        _managedWorkspaceRoot = NormalizeManagedRoot(
            _options.ManagedWorkspaceRoot,
            _options.MaximumPathCharacters);
        _defaultWorkingDirectory = ResolveWorkingDirectoryCore(
            _options.WorkingDirectory ?? _managedWorkspaceRoot,
            _managedWorkspaceRoot,
            _options.MaximumPathCharacters,
            "runtime working directory");
        if (_options.Sandbox == AgentSandboxMode.WorkspaceWrite && _managedWorkspaceRoot is null)
        {
            throw new ArgumentException(
                "Workspace-write sandboxing requires a trusted ManagedWorkspaceRoot.",
                nameof(options));
        }

        _runtimeWorkspaceRoots = _managedWorkspaceRoot is null ? null : new[] { _managedWorkspaceRoot };
        _cadProposalConcurrency = new SemaphoreSlim(
            _options.MaximumConcurrentCadProposals,
            _options.MaximumConcurrentCadProposals);

        _appServer.NotificationReceived += OnNotificationReceived;
        _appServer.CommandApprovalRequested += OnCommandApprovalRequestedAsync;
        _appServer.FileChangeApprovalRequested += OnFileChangeApprovalRequestedAsync;
        _appServer.PermissionsApprovalRequested += OnPermissionsApprovalRequestedAsync;
        _appServer.CadApprovalRequested += OnCadApprovalRequestedAsync;
        _appServer.ServerRequestReceived += OnServerRequestReceivedAsync;
    }

    public event EventHandler<AgentEvent>? EventReceived;

    public event EventHandler<AgentEventProjectionFailedEventArgs>? ProjectionFailed;

    public event EventHandler<AgentEventObserverFailedEventArgs>? EventObserverFailed;

    public event CommandApprovalRequestedHandler? CommandApprovalRequested;

    public event FileChangeApprovalRequestedHandler? FileChangeApprovalRequested;

    public event PermissionsApprovalRequestedHandler? PermissionsApprovalRequested;

    public event CadApprovalRequestedHandler? CadApprovalRequested;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _started) != 0)
        {
            return;
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_started != 0)
            {
                return;
            }

            await _appServer.StartAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _started, 1);
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task<AgentThreadHandle> CreateThreadAsync(
        AgentThreadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        options ??= new AgentThreadOptions();
        var workingDirectory = ResolveWorkingDirectory(options.WorkingDirectory);

        var parameters = new ThreadStartWireParams(
            _options.Sandbox.ToWire(),
            _options.ApprovalPolicy.ToWire(),
            _options.ApprovalsReviewer.ToWire(),
            workingDirectory,
            options.Model ?? _options.Model,
            options.ModelProvider ?? _options.ModelProvider,
            options.DeveloperInstructions,
            options.ServiceTier,
            options.Ephemeral,
            _runtimeWorkspaceRoots,
            options.EnableCadDynamicTools
                ? CadDynamicToolCatalog.CreateWireTools()
                : Array.Empty<DynamicToolNamespaceWire>());

        var response = await _appServer.SendRequestAsync<JsonElement>(
            "thread/start",
            parameters,
            cancellationToken).ConfigureAwait(false);
        var handle = ParseThreadHandle(response, "thread/start");
        ValidateIdentifier(handle.ThreadId, nameof(handle.ThreadId));
        RegisterThread(handle.ThreadId);
        return handle;
    }

    public async Task<AgentThreadHandle> ResumeThreadAsync(
        string threadId,
        AgentThreadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(threadId, nameof(threadId));
        await StartAsync(cancellationToken).ConfigureAwait(false);
        options ??= new AgentThreadOptions();
        var workingDirectory = ResolveWorkingDirectory(options.WorkingDirectory);

        var parameters = new ThreadResumeWireParams(
            threadId,
            _options.Sandbox.ToWire(),
            _options.ApprovalPolicy.ToWire(),
            _options.ApprovalsReviewer.ToWire(),
            workingDirectory,
            options.Model ?? _options.Model,
            options.ModelProvider ?? _options.ModelProvider,
            options.DeveloperInstructions,
            options.ServiceTier,
            _runtimeWorkspaceRoots);

        var response = await _appServer.SendRequestAsync<JsonElement>(
            "thread/resume",
            parameters,
            cancellationToken).ConfigureAwait(false);
        var handle = ParseThreadHandle(response, "thread/resume");
        ValidateIdentifier(handle.ThreadId, nameof(handle.ThreadId));
        if (!string.Equals(handle.ThreadId, threadId, StringComparison.Ordinal))
        {
            throw new AgentEventProjectionException(
                "thread/resume",
                "response thread id did not match the requested thread id.");
        }

        RegisterThread(handle.ThreadId);
        return handle;
    }

    public Task<AgentTurnHandle> StartTurnAsync(
        string threadId,
        string prompt,
        AgentTurnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (prompt.Length > _options.MaximumPromptCharacters)
        {
            throw new ArgumentException(
                $"Prompt exceeds {_options.MaximumPromptCharacters} characters.",
                nameof(prompt));
        }
        return StartTurnAsync(
            threadId,
            new AgentInput[] { new AgentTextInput(prompt) },
            options,
            cancellationToken);
    }

    public async Task<AgentTurnHandle> StartTurnAsync(
        string threadId,
        IReadOnlyList<AgentInput> input,
        AgentTurnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(threadId, nameof(threadId));
        ArgumentNullException.ThrowIfNull(input);
        if (input.Count is 0 || input.Count > _options.MaximumInputItems)
        {
            throw new ArgumentException(
                $"A turn requires between 1 and {_options.MaximumInputItems} input items.",
                nameof(input));
        }

        EnsureActiveThread(threadId);

        await StartAsync(cancellationToken).ConfigureAwait(false);
        options ??= new AgentTurnOptions();
        var workingDirectory = ResolveWorkingDirectory(options.WorkingDirectory);
        if (options.ClientUserMessageId is not null)
        {
            ValidateIdentifier(options.ClientUserMessageId, nameof(options.ClientUserMessageId));
        }

        var wireInput = new object[input.Count];
        var totalTextCharacters = 0;
        for (var index = 0; index < input.Count; index++)
        {
            var item = input[index] ?? throw new ArgumentException(
                "Turn input cannot contain null items.",
                nameof(input));
            wireInput[index] = ToValidatedWireInput(item, index, ref totalTextCharacters);
        }

        var parameters = new TurnStartWireParams(
            threadId,
            wireInput,
            _options.ApprovalPolicy.ToWire(),
            _options.ApprovalsReviewer.ToWire(),
            _options.Sandbox.ToSandboxPolicyWire(_runtimeWorkspaceRoots),
            workingDirectory,
            options.Model ?? _options.Model,
            options.ClientUserMessageId,
            options.ServiceTier,
            options.OutputSchema,
            _runtimeWorkspaceRoots);

        var response = await _appServer.SendRequestAsync<JsonElement>(
            "turn/start",
            parameters,
            cancellationToken).ConfigureAwait(false);
        var handle = ParseTurnHandle(threadId, response, "turn/start");
        ValidateIdentifier(handle.TurnId, nameof(handle.TurnId));
        if (handle.Status is AgentTurnStatus.Unknown or AgentTurnStatus.InProgress)
        {
            RegisterTurn(handle.ThreadId, handle.TurnId);
        }
        return handle;
    }

    public async Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(threadId, nameof(threadId));
        ValidateIdentifier(turnId, nameof(turnId));
        EnsureActiveTurn(threadId, turnId);
        await StartAsync(cancellationToken).ConfigureAwait(false);
        _ = await _appServer.SendRequestAsync<JsonElement>(
            "turn/interrupt",
            new TurnInterruptWireParams(threadId, turnId),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _appServer.NotificationReceived -= OnNotificationReceived;
        _appServer.CommandApprovalRequested -= OnCommandApprovalRequestedAsync;
        _appServer.FileChangeApprovalRequested -= OnFileChangeApprovalRequestedAsync;
        _appServer.PermissionsApprovalRequested -= OnPermissionsApprovalRequestedAsync;
        _appServer.CadApprovalRequested -= OnCadApprovalRequestedAsync;
        _appServer.ServerRequestReceived -= OnServerRequestReceivedAsync;
        _lifetimeCancellation.Cancel();

        if (_ownsAppServer)
        {
            await _appServer.DisposeAsync().ConfigureAwait(false);
        }

        _lifetimeCancellation.Dispose();
        _startGate.Dispose();
    }

    private void OnNotificationReceived(object? sender, AppServerNotification notification)
    {
        try
        {
            if (TryGetTerminalTurn(notification, out var terminalTurn))
            {
                TerminateTurn(terminalTurn.ThreadId, terminalTurn.TurnId);
            }

            var agentEvent = AgentEventProjector.Project(notification);
            if (agentEvent is not null)
            {
                UpdateConversationState(agentEvent);
                Publish(agentEvent);
            }
        }
        catch (Exception exception)
        {
            RaiseProjectionFailed(notification.Method, exception);
        }
    }

    private async ValueTask<CommandApprovalResponse?> OnCommandApprovalRequestedAsync(
        RpcApprovalEvent<CommandApprovalRequest> approval,
        CancellationToken cancellationToken)
    {
        Publish(new AgentCommandApprovalRequestedEvent(approval.Request));
        if (CommandApprovalRequested is null)
        {
            return null;
        }

        foreach (CommandApprovalRequestedHandler handler in CommandApprovalRequested.GetInvocationList())
        {
            var response = await handler(approval, cancellationToken).ConfigureAwait(false);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    private async ValueTask<FileChangeApprovalResponse?> OnFileChangeApprovalRequestedAsync(
        RpcApprovalEvent<FileChangeApprovalRequest> approval,
        CancellationToken cancellationToken)
    {
        Publish(new AgentFileChangeApprovalRequestedEvent(approval.Request));
        if (FileChangeApprovalRequested is null)
        {
            return null;
        }

        foreach (FileChangeApprovalRequestedHandler handler in FileChangeApprovalRequested.GetInvocationList())
        {
            var response = await handler(approval, cancellationToken).ConfigureAwait(false);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    private async ValueTask<PermissionsApprovalResponse?> OnPermissionsApprovalRequestedAsync(
        RpcApprovalEvent<PermissionsApprovalRequest> approval,
        CancellationToken cancellationToken)
    {
        Publish(new AgentPermissionsApprovalRequestedEvent(approval.Request));
        if (PermissionsApprovalRequested is null)
        {
            return null;
        }

        foreach (PermissionsApprovalRequestedHandler handler in PermissionsApprovalRequested.GetInvocationList())
        {
            var response = await handler(approval, cancellationToken).ConfigureAwait(false);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    private async ValueTask<CadApprovalResponse?> OnCadApprovalRequestedAsync(
        RpcApprovalEvent<CadApprovalRequest> approval,
        CancellationToken cancellationToken)
    {
        Publish(new AgentCadApprovalRequestedEvent(approval.Request));
        if (CadApprovalRequested is null)
        {
            return null;
        }

        foreach (CadApprovalRequestedHandler handler in CadApprovalRequested.GetInvocationList())
        {
            var response = await handler(approval, cancellationToken).ConfigureAwait(false);
            if (response is not null)
            {
                return response;
            }
        }

        return null;
    }

    private async ValueTask<ServerRequestResolution?> OnServerRequestReceivedAsync(
        AppServerServerRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Method, "item/tool/call", StringComparison.Ordinal))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await HandleDynamicToolCallAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ServerRequestResolution> HandleDynamicToolCallAsync(
        AppServerServerRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params;
        if (parameters is not { ValueKind: JsonValueKind.Object } value)
        {
            return DynamicToolResult(success: false, "Dynamic tool params must be an object.");
        }

        var threadId = OptionalString(value, "threadId") ?? string.Empty;
        var turnId = OptionalString(value, "turnId") ?? string.Empty;
        var callId = OptionalString(value, "callId") ?? string.Empty;
        var toolNamespace = OptionalString(value, "namespace");
        var tool = OptionalString(value, "tool") ?? string.Empty;

        if (!IsValidIdentifier(threadId)
            || !IsValidIdentifier(turnId)
            || !IsValidIdentifier(callId)
            || !IsValidIdentifier(tool))
        {
            return DynamicToolResult(success: false, "Dynamic tool identifiers are missing or invalid.");
        }

        if (!string.Equals(toolNamespace, CadDynamicToolCatalog.Namespace, StringComparison.Ordinal)
            || !string.Equals(tool, CadDynamicToolCatalog.ProposeOperations, StringComparison.Ordinal))
        {
            const string reason = "Only cad.propose_operations is supported.";
            Publish(new AgentDynamicToolRejectedEvent(
                threadId,
                turnId,
                callId,
                toolNamespace,
                tool,
                reason));
            return DynamicToolResult(success: false, reason);
        }

        if (!value.TryGetProperty("arguments", out var arguments))
        {
            const string reason = "Dynamic tool arguments are missing.";
            Publish(new AgentDynamicToolRejectedEvent(
                threadId,
                turnId,
                callId,
                toolNamespace,
                tool,
                reason));
            return DynamicToolResult(success: false, reason);
        }

        if (!IsActiveTurn(threadId, turnId))
        {
            const string reason = "Dynamic tool call does not belong to an active runtime turn.";
            PublishDynamicToolRejection(threadId, turnId, callId, toolNamespace, tool, reason);
            return DynamicToolResult(success: false, reason);
        }

        try
        {
            var proposal = CadDynamicToolCatalog.ParseProposal(
                CreateCadProposalId(callId),
                callId,
                threadId,
                turnId,
                arguments);
            var key = new AgentCadCallKey(threadId, turnId, callId);
            var fingerprint = ComputeArgumentsFingerprint(arguments);
            var execution = GetOrAddCadCall(
                key,
                fingerprint,
                turnLifecycle => ExecuteCadProposalAsync(
                    proposal,
                    toolNamespace,
                    tool,
                    cancellationToken,
                    turnLifecycle),
                out var cacheRejection);
            if (execution is null)
            {
                PublishDynamicToolRejection(
                    threadId,
                    turnId,
                    callId,
                    toolNamespace,
                    tool,
                    cacheRejection!);
                return DynamicToolResult(success: false, cacheRejection!);
            }

            return await execution.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CadProposalValidationException exception)
        {
            PublishDynamicToolRejection(
                threadId,
                turnId,
                callId,
                toolNamespace,
                tool,
                exception.Message);
            return DynamicToolResult(success: false, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            const string reason = "Dynamic tool arguments were invalid.";
            PublishDynamicToolRejection(
                threadId,
                turnId,
                callId,
                toolNamespace,
                tool,
                reason);
            return DynamicToolResult(success: false, reason);
        }
    }

    private async Task<ServerRequestResolution> ExecuteCadProposalAsync(
        AgentCadOperationBatchProposal proposal,
        string? toolNamespace,
        string tool,
        CancellationToken cancellationToken,
        CadTurnLifecycle turnLifecycle)
    {
        if (_cadProposalBroker is null)
        {
            const string reason = "No trusted AutoCAD proposal broker is connected.";
            PublishDynamicToolRejection(
                proposal.ThreadId,
                proposal.TurnId,
                proposal.CallId,
                toolNamespace,
                tool,
                reason);
            return DynamicToolResult(success: false, reason);
        }

        if (!_cadProposalConcurrency.Wait(0))
        {
            const string reason = "The trusted AutoCAD proposal broker is busy.";
            PublishDynamicToolRejection(
                proposal.ThreadId,
                proposal.TurnId,
                proposal.CallId,
                toolNamespace,
                tool,
                reason);
            return DynamicToolResult(success: false, reason);
        }

        var releaseConcurrency = true;
        try
        {
            var eventProposal = proposal.DeepClone();
            Publish(new AgentCadProposalCreatedEvent(
                proposal.ThreadId,
                proposal.TurnId,
                proposal.CallId,
                eventProposal));

            using var brokerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token,
                turnLifecycle.CancellationToken);
            AgentCadProposalResult result;
            try
            {
                var brokerProposal = proposal.DeepClone();
                var brokerExecution = _cadProposalBroker.ExecuteAsync(
                    brokerProposal,
                    brokerCancellation.Token).AsTask();
                var deadline = Task.Delay(_options.CadProposalTimeout, brokerCancellation.Token);
                var completed = await Task.WhenAny(brokerExecution, deadline).ConfigureAwait(false);
                if (completed != brokerExecution)
                {
                    brokerCancellation.Cancel();
                    ReleaseCadProposalConcurrencyAfterCompletion(brokerExecution);
                    releaseConcurrency = false;
                    result = AgentCadProposalResult.Failed(
                        proposal,
                        cancellationToken.IsCancellationRequested
                            || _lifetimeCancellation.IsCancellationRequested
                            || turnLifecycle.IsTerminal
                            ? "The trusted AutoCAD broker was cancelled."
                            : "The trusted AutoCAD broker timed out.");
                }
                else
                {
                    result = await brokerExecution.ConfigureAwait(false)
                        ?? AgentCadProposalResult.Failed(
                            proposal,
                            "The trusted AutoCAD broker returned no result.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = AgentCadProposalResult.Failed(
                    proposal,
                    "The trusted AutoCAD broker timed out.");
            }
            catch (OperationCanceledException)
            {
                result = AgentCadProposalResult.Failed(
                    proposal,
                    "The trusted AutoCAD broker was cancelled.");
            }
            catch
            {
                result = AgentCadProposalResult.Failed(
                    proposal,
                    "The trusted AutoCAD broker failed.");
            }

            if (!turnLifecycle.TryAcceptBrokerResult())
            {
                const string reason =
                    "The trusted AutoCAD broker result arrived after the runtime turn ended.";
                PublishDynamicToolRejection(
                    proposal.ThreadId,
                    proposal.TurnId,
                    proposal.CallId,
                    toolNamespace,
                    tool,
                    reason);
                return DynamicToolResult(success: false, reason);
            }

            if (!BrokerResultMatchesProposal(result, proposal))
            {
                const string reason =
                    "The trusted AutoCAD broker result identity did not match the proposal.";
                PublishDynamicToolRejection(
                    proposal.ThreadId,
                    proposal.TurnId,
                    proposal.CallId,
                    toolNamespace,
                    tool,
                    reason);
                return DynamicToolResult(success: false, reason);
            }

            var message = NormalizeBrokerMessage(result.Message);
            if (result.Outcome == AgentCadProposalOutcome.Applied)
            {
                var content = JsonSerializer.Serialize(new
                {
                    status = "applied",
                    proposalId = proposal.ProposalId,
                    callId = proposal.CallId,
                    operationCount = proposal.Operations.Count,
                    message,
                });
                return DynamicToolResult(success: true, content);
            }

            PublishDynamicToolRejection(
                proposal.ThreadId,
                proposal.TurnId,
                proposal.CallId,
                toolNamespace,
                tool,
                message);
            return DynamicToolResult(success: false, message);
        }
        finally
        {
            if (releaseConcurrency)
            {
                _cadProposalConcurrency.Release();
            }
        }
    }

    private void ReleaseCadProposalConcurrencyAfterCompletion(
        Task<AgentCadProposalResult> brokerExecution)
    {
        _ = brokerExecution.ContinueWith(
            static (completed, state) =>
            {
                if (completed.IsFaulted)
                {
                    _ = completed.Exception;
                }

                ((SemaphoreSlim)state!).Release();
            },
            _cadProposalConcurrency,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static ServerRequestResolution DynamicToolResult(bool success, string text)
        => ServerRequestResolution.Success(
            new DynamicToolCallResponseWire(
                new[] { new DynamicToolTextContentWire("inputText", text) },
                success));

    private void Publish(AgentEvent agentEvent)
    {
        if (Volatile.Read(ref _disposed) != 0 || EventReceived is null)
        {
            return;
        }

        foreach (EventHandler<AgentEvent> handler in EventReceived.GetInvocationList())
        {
            try
            {
                handler(this, agentEvent);
            }
            catch (Exception exception)
            {
                RaiseObserverFailed(agentEvent, exception);
            }
        }
    }

    private void RaiseProjectionFailed(string method, Exception exception)
    {
        if (ProjectionFailed is null)
        {
            return;
        }

        var args = new AgentEventProjectionFailedEventArgs(method, exception);
        foreach (EventHandler<AgentEventProjectionFailedEventArgs> handler in ProjectionFailed.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // Diagnostics must not tear down the App Server read loop.
            }
        }
    }

    private void RaiseObserverFailed(AgentEvent agentEvent, Exception exception)
    {
        if (EventObserverFailed is null)
        {
            return;
        }

        var args = new AgentEventObserverFailedEventArgs(agentEvent, exception);
        foreach (EventHandler<AgentEventObserverFailedEventArgs> handler in EventObserverFailed.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // Diagnostics must not tear down the App Server read loop.
            }
        }
    }

    private static AgentThreadHandle ParseThreadHandle(JsonElement response, string method)
    {
        var thread = RequiredObject(response, "thread", method);
        return new AgentThreadHandle(
            RequiredString(thread, "id", method),
            OptionalString(response, "cwd"),
            OptionalString(response, "model"),
            OptionalString(response, "modelProvider"));
    }

    private static AgentTurnHandle ParseTurnHandle(string threadId, JsonElement response, string method)
    {
        var turn = RequiredObject(response, "turn", method);
        return new AgentTurnHandle(
            threadId,
            RequiredString(turn, "id", method),
            AgentEventProjector.ToTurnStatus(OptionalString(turn, "status")));
    }

    private static JsonElement RequiredObject(JsonElement parent, string property, string method)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new AgentEventProjectionException(method, $"response '{property}' must be an object.");
        }

        return value;
    }

    private static string RequiredString(JsonElement parent, string property, string method)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new AgentEventProjectionException(method, $"response '{property}' must be a string.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement parent, string property)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static AgentRuntimeOptions ValidateOptions(AgentRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(typeof(AgentSandboxMode), options.Sandbox)
            || !Enum.IsDefined(typeof(AgentApprovalPolicy), options.ApprovalPolicy)
            || !Enum.IsDefined(typeof(AgentApprovalsReviewer), options.ApprovalsReviewer))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Runtime options contain an unknown enum value.");
        }

        RequireRange(options.MaximumIdentifierCharacters, 1, 4_096, nameof(options.MaximumIdentifierCharacters));
        RequireRange(options.MaximumPromptCharacters, 1, 1_000_000, nameof(options.MaximumPromptCharacters));
        RequireRange(options.MaximumInputItems, 1, 256, nameof(options.MaximumInputItems));
        RequireRange(options.MaximumPathCharacters, 32, 32_767, nameof(options.MaximumPathCharacters));
        RequireRange(options.MaximumConcurrentCadProposals, 1, 16, nameof(options.MaximumConcurrentCadProposals));
        RequireRange(options.MaximumTrackedCadCalls, 1, 16_384, nameof(options.MaximumTrackedCadCalls));
        RequireRange(options.MaximumTrackedThreads, 1, 4_096, nameof(options.MaximumTrackedThreads));
        RequireRange(options.MaximumActiveTurns, 1, 4_096, nameof(options.MaximumActiveTurns));
        if (options.CadProposalTimeout <= TimeSpan.Zero
            || options.CadProposalTimeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.CadProposalTimeout),
                "CAD proposal timeout must be between zero and ten minutes.");
        }

        if (options.AllowLocalFileInputs && string.IsNullOrWhiteSpace(options.ManagedWorkspaceRoot))
        {
            throw new ArgumentException(
                "Local file inputs require a trusted ManagedWorkspaceRoot.",
                nameof(options));
        }

        return options;
    }

    private static void RequireRange(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"Value must be between {minimum} and {maximum}.");
        }
    }

    private static string? NormalizeManagedRoot(string? value, int maximumPathCharacters)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Managed workspace root cannot be empty.", nameof(value));
        }

        return NormalizeExistingDirectory(value, maximumPathCharacters, "managed workspace root");
    }

    private string? ResolveWorkingDirectory(string? requestedDirectory)
        => ResolveWorkingDirectoryCore(
            requestedDirectory ?? _defaultWorkingDirectory,
            _managedWorkspaceRoot,
            _options.MaximumPathCharacters,
            "working directory");

    private static string? ResolveWorkingDirectoryCore(
        string? candidate,
        string? managedRoot,
        int maximumPathCharacters,
        string label)
    {
        if (candidate is null)
        {
            return null;
        }

        if (managedRoot is null)
        {
            throw new ArgumentException($"A {label} requires a trusted ManagedWorkspaceRoot.", nameof(candidate));
        }

        var fullPath = NormalizeExistingDirectory(candidate, maximumPathCharacters, label);
        if (!IsPathWithinRoot(fullPath, managedRoot))
        {
            throw new ArgumentException($"The {label} is outside ManagedWorkspaceRoot.", nameof(candidate));
        }

        return fullPath;
    }

    private static string NormalizeExistingDirectory(
        string candidate,
        int maximumPathCharacters,
        string label)
    {
        ValidatePathText(candidate, maximumPathCharacters, label);
        if (!Path.IsPathFullyQualified(candidate))
        {
            throw new ArgumentException($"The {label} must be an absolute path.", nameof(candidate));
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        RejectNetworkPath(fullPath, label);
        RejectAlternateDataStream(fullPath, label);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"The {label} does not exist.");
        }

        EnsureNoReparsePoints(fullPath, label);
        return fullPath;
    }

    private string ResolveLocalFile(string candidate, string label)
    {
        if (_managedWorkspaceRoot is null)
        {
            throw new InvalidOperationException("Local file inputs require a trusted ManagedWorkspaceRoot.");
        }

        ValidatePathText(candidate, _options.MaximumPathCharacters, label);
        if (!Path.IsPathFullyQualified(candidate))
        {
            throw new ArgumentException($"The {label} must be an absolute path.", nameof(candidate));
        }

        var fullPath = Path.GetFullPath(candidate);
        RejectNetworkPath(fullPath, label);
        RejectAlternateDataStream(fullPath, label);
        if (!IsPathWithinRoot(fullPath, _managedWorkspaceRoot))
        {
            throw new ArgumentException($"The {label} is outside ManagedWorkspaceRoot.", nameof(candidate));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The {label} does not exist.", fullPath);
        }

        EnsureNoReparsePoints(fullPath, label);
        return fullPath;
    }

    private static void ValidatePathText(string candidate, int maximumPathCharacters, string label)
    {
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.Length > maximumPathCharacters
            || candidate.IndexOf('\0') >= 0)
        {
            throw new ArgumentException($"The {label} is empty or exceeds the configured limit.", nameof(candidate));
        }
    }

    private static void RejectNetworkPath(string fullPath, string label)
    {
        if (OperatingSystem.IsWindows()
            && (fullPath.StartsWith("\\\\", StringComparison.Ordinal)
                || fullPath.StartsWith("//", StringComparison.Ordinal)))
        {
            throw new ArgumentException($"The {label} cannot use a network or device path.", nameof(fullPath));
        }
    }

    private static void RejectAlternateDataStream(string fullPath, string label)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pathRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
        if (fullPath[pathRoot.Length..].IndexOf(':') >= 0)
        {
            throw new ArgumentException(
                $"The {label} cannot use a Windows alternate data stream.",
                nameof(fullPath));
        }
    }

    private static bool IsPathWithinRoot(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(candidate, root, comparison))
        {
            return true;
        }

        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            || root.EndsWith(Path.AltDirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, comparison);
    }

    private static void EnsureNoReparsePoints(string fullPath, string label)
    {
        var pathRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(pathRoot))
        {
            throw new ArgumentException($"The {label} has no filesystem root.", nameof(fullPath));
        }

        var current = pathRoot;
        var remainder = fullPath[pathRoot.Length..];
        foreach (var segment in remainder.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException($"The {label} cannot traverse a symbolic link or junction.", nameof(fullPath));
            }
        }
    }

    private object ToValidatedWireInput(AgentInput input, int index, ref int totalTextCharacters)
    {
        switch (input)
        {
            case AgentTextInput text:
                if (string.IsNullOrWhiteSpace(text.Text))
                {
                    throw new ArgumentException($"Text input {index} cannot be empty.", nameof(input));
                }

                totalTextCharacters = checked(totalTextCharacters + text.Text.Length);
                if (totalTextCharacters > _options.MaximumPromptCharacters)
                {
                    throw new ArgumentException(
                        $"Combined text input exceeds {_options.MaximumPromptCharacters} characters.",
                        nameof(input));
                }

                return new TextInputWire("text", text.Text);

            case AgentLocalImageInput image:
                if (!_options.AllowLocalFileInputs)
                {
                    throw new InvalidOperationException("Local image inputs are disabled by policy.");
                }

                return new LocalImageInputWire("localImage", ResolveLocalFile(image.Path, $"image input {index}"));

            case AgentMentionInput mention:
                if (!_options.AllowLocalFileInputs)
                {
                    throw new InvalidOperationException("Local mention inputs are disabled by policy.");
                }

                if (!IsValidIdentifier(mention.Name))
                {
                    throw new ArgumentException($"Mention input {index} has an invalid name.", nameof(input));
                }

                return new MentionInputWire(
                    "mention",
                    mention.Name,
                    ResolveLocalFile(mention.Path, $"mention input {index}"));

            default:
                throw new ArgumentException("Unsupported agent input type.", nameof(input));
        }
    }

    private void ValidateIdentifier(string? value, string parameterName)
    {
        if (!IsValidIdentifier(value))
        {
            throw new ArgumentException(
                $"Identifier must be non-empty, contain no control characters, and be at most {_options.MaximumIdentifierCharacters} characters.",
                parameterName);
        }
    }

    private bool IsValidIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= _options.MaximumIdentifierCharacters
            && value.All(static character => !char.IsControl(character));

    private void RegisterThread(string threadId)
    {
        lock (_conversationSync)
        {
            if (_activeThreads.Contains(threadId))
            {
                return;
            }

            if (_activeThreads.Count >= _options.MaximumTrackedThreads)
            {
                throw new InvalidOperationException("The runtime thread registry is full.");
            }

            _activeThreads.Add(threadId);
        }
    }

    private void EnsureActiveThread(string threadId)
    {
        lock (_conversationSync)
        {
            if (!_activeThreads.Contains(threadId))
            {
                throw new InvalidOperationException("The requested thread is not active in this runtime.");
            }
        }
    }

    private void RegisterTurn(string threadId, string turnId)
    {
        lock (_conversationSync)
        {
            if (!_activeThreads.Contains(threadId))
            {
                throw new InvalidOperationException("Cannot register a turn for an inactive thread.");
            }

            var key = new AgentTurnKey(threadId, turnId);
            if (_activeTurns.Contains(key))
            {
                return;
            }

            if (_activeTurns.Count >= _options.MaximumActiveTurns)
            {
                throw new InvalidOperationException("The runtime active-turn registry is full.");
            }

            _activeTurns.Add(key);
            _cadTurnLifecycles.Add(key, new CadTurnLifecycle());
        }
    }

    private void EnsureActiveTurn(string threadId, string turnId)
    {
        if (!IsActiveTurn(threadId, turnId))
        {
            throw new InvalidOperationException("The requested turn is not active in this runtime.");
        }
    }

    private bool IsActiveTurn(string threadId, string turnId)
    {
        lock (_conversationSync)
        {
            return _activeThreads.Contains(threadId)
                && _activeTurns.Contains(new AgentTurnKey(threadId, turnId));
        }
    }

    private void UpdateConversationState(AgentEvent agentEvent)
    {
        if (agentEvent is not AgentTurnStateChangedEvent turn)
        {
            return;
        }

        if (turn.Status is AgentTurnStatus.Completed
            or AgentTurnStatus.Interrupted
            or AgentTurnStatus.Failed)
        {
            TerminateTurn(turn.ThreadId, turn.TurnId);
        }
    }

    private static string ComputeArgumentsFingerprint(JsonElement arguments)
    {
        var bytes = Encoding.UTF8.GetBytes(arguments.GetRawText());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string CreateCadProposalId(string callId)
    {
        string proposalId;
        do
        {
            proposalId = "cad-proposal-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        }
        while (string.Equals(proposalId, callId, StringComparison.Ordinal));

        return proposalId;
    }

    private Task<ServerRequestResolution>? GetOrAddCadCall(
        AgentCadCallKey key,
        string fingerprint,
        Func<CadTurnLifecycle, Task<ServerRequestResolution>> executionFactory,
        out string? rejection)
    {
        Lazy<Task<ServerRequestResolution>> execution;
        lock (_conversationSync)
        {
            if (!_activeThreads.Contains(key.ThreadId)
                || !_activeTurns.Contains(new AgentTurnKey(key.ThreadId, key.TurnId))
                || !_cadTurnLifecycles.TryGetValue(
                    new AgentTurnKey(key.ThreadId, key.TurnId),
                    out var turnLifecycle))
            {
                rejection = "Dynamic tool call does not belong to an active runtime turn.";
                return null;
            }

            lock (_cadCallSync)
            {
                if (_cadCalls.TryGetValue(key, out var existing))
                {
                    if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        rejection = "The same CAD tool call id was replayed with different arguments.";
                        return null;
                    }

                    rejection = null;
                    execution = existing.Execution;
                }
                else
                {
                    if (_cadCalls.Count >= _options.MaximumTrackedCadCalls)
                    {
                        rejection = "The CAD tool-call idempotence registry is full.";
                        return null;
                    }

                    execution = new Lazy<Task<ServerRequestResolution>>(
                        () => ExecuteCadCallWithinTurnAsync(turnLifecycle, executionFactory),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    _cadCalls.Add(key, new CadCallCacheEntry(fingerprint, execution));
                    _cadCallOrder.AddLast(key);
                    rejection = null;
                }
            }
        }

        return execution.Value;
    }

    private static async Task<ServerRequestResolution> ExecuteCadCallWithinTurnAsync(
        CadTurnLifecycle turnLifecycle,
        Func<CadTurnLifecycle, Task<ServerRequestResolution>> executionFactory)
    {
        using var lease = turnLifecycle.TryAcquire();
        if (lease is null)
        {
            return DynamicToolResult(
                success: false,
                "Dynamic tool call does not belong to an active runtime turn.");
        }

        return await executionFactory(turnLifecycle).ConfigureAwait(false);
    }

    private void TerminateTurn(string threadId, string turnId)
    {
        CadTurnLifecycle? lifecycle = null;
        lock (_conversationSync)
        {
            var key = new AgentTurnKey(threadId, turnId);
            _activeTurns.Remove(key);
            if (_cadTurnLifecycles.Remove(key, out lifecycle))
            {
                lifecycle.MarkTerminal();
            }

            RemoveCadCallsForTurn(threadId, turnId);
        }

        lifecycle?.CancelAfterTerminal();
    }

    private bool TryGetTerminalTurn(
        AppServerNotification notification,
        out AgentTurnKey turn)
    {
        turn = default;
        if (notification.Method is not (
                "turn/completed"
                or "turn/interrupted"
                or "turn/cancelled"
                or "turn/canceled"
                or "turn/failed")
            || notification.Params is not { ValueKind: JsonValueKind.Object } parameters
            || !parameters.TryGetProperty("threadId", out var threadIdElement)
            || threadIdElement.ValueKind != JsonValueKind.String
            || !parameters.TryGetProperty("turn", out var turnElement)
            || turnElement.ValueKind != JsonValueKind.Object
            || !turnElement.TryGetProperty("id", out var turnIdElement)
            || turnIdElement.ValueKind != JsonValueKind.String
            || !turnElement.TryGetProperty("status", out var statusElement)
            || statusElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var status = statusElement.GetString();
        if (status is not ("completed" or "interrupted" or "cancelled" or "canceled" or "failed"))
        {
            return false;
        }

        var threadId = threadIdElement.GetString();
        var turnId = turnIdElement.GetString();
        if (!IsValidIdentifier(threadId) || !IsValidIdentifier(turnId))
        {
            return false;
        }

        turn = new AgentTurnKey(threadId!, turnId!);
        return true;
    }

    private void RemoveCadCallsForTurn(string threadId, string turnId)
    {
        lock (_cadCallSync)
        {
            var node = _cadCallOrder.First;
            while (node is not null)
            {
                var next = node.Next;
                if (string.Equals(node.Value.ThreadId, threadId, StringComparison.Ordinal)
                    && string.Equals(node.Value.TurnId, turnId, StringComparison.Ordinal))
                {
                    _cadCalls.Remove(node.Value);
                    _cadCallOrder.Remove(node);
                }

                node = next;
            }
        }
    }

    private void PublishDynamicToolRejection(
        string threadId,
        string turnId,
        string callId,
        string? toolNamespace,
        string tool,
        string reason)
        => Publish(new AgentDynamicToolRejectedEvent(
            threadId,
            turnId,
            callId,
            toolNamespace,
            tool,
            reason));

    private static string NormalizeBrokerMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "The trusted AutoCAD broker returned no details.";
        }

        var sanitized = new string(message
            .Take(2_048)
            .Select(static character => char.IsControl(character) ? ' ' : character)
            .ToArray())
            .Trim();
        return sanitized.Length == 0
            ? "The trusted AutoCAD broker returned no details."
            : sanitized;
    }

    private static bool BrokerResultMatchesProposal(
        AgentCadProposalResult result,
        AgentCadOperationBatchProposal proposal)
        => string.Equals(result.ProposalId, proposal.ProposalId, StringComparison.Ordinal)
            && string.Equals(result.ThreadId, proposal.ThreadId, StringComparison.Ordinal)
            && string.Equals(result.TurnId, proposal.TurnId, StringComparison.Ordinal)
            && string.Equals(result.CallId, proposal.CallId, StringComparison.Ordinal);

    private readonly record struct AgentTurnKey(string ThreadId, string TurnId);

    private readonly record struct AgentCadCallKey(string ThreadId, string TurnId, string CallId);

    private sealed record CadCallCacheEntry(
        string Fingerprint,
        Lazy<Task<ServerRequestResolution>> Execution);

    private sealed class CadTurnLifecycle
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _cancellation = new();
        private int _leaseCount;
        private bool _terminal;
        private bool _cancellationCompleted;
        private bool _disposed;

        public CancellationToken CancellationToken => _cancellation.Token;

        public bool IsTerminal
        {
            get
            {
                lock (_sync)
                {
                    return _terminal;
                }
            }
        }

        public IDisposable? TryAcquire()
        {
            lock (_sync)
            {
                if (_terminal || _disposed)
                {
                    return null;
                }

                _leaseCount++;
                return new CadTurnLease(this);
            }
        }

        public bool TryAcceptBrokerResult()
        {
            lock (_sync)
            {
                return !_terminal && !_disposed;
            }
        }

        public void MarkTerminal()
        {
            lock (_sync)
            {
                _terminal = true;
            }
        }

        public void CancelAfterTerminal()
        {
            try
            {
                _cancellation.Cancel();
            }
            finally
            {
                var dispose = false;
                lock (_sync)
                {
                    _cancellationCompleted = true;
                    dispose = MarkDisposedIfReady();
                }

                if (dispose)
                {
                    _cancellation.Dispose();
                }
            }
        }

        private void Release()
        {
            var dispose = false;
            lock (_sync)
            {
                _leaseCount--;
                dispose = MarkDisposedIfReady();
            }

            if (dispose)
            {
                _cancellation.Dispose();
            }
        }

        private bool MarkDisposedIfReady()
        {
            if (_disposed || !_terminal || !_cancellationCompleted || _leaseCount != 0)
            {
                return false;
            }

            _disposed = true;
            return true;
        }

        private sealed class CadTurnLease(CadTurnLifecycle owner) : IDisposable
        {
            private CadTurnLifecycle? _owner = owner;

            public void Dispose()
                => Interlocked.Exchange(ref _owner, null)?.Release();
        }
    }

    private static AppServerClientOptions EnableExperimentalApi(AppServerClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options with
        {
            Capabilities = options.Capabilities with { ExperimentalApi = true },
        };
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
