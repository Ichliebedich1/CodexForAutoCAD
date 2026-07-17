using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.AutoCAD.AppServer.Protocol;

namespace Codex.AutoCAD.AppServer;

public enum AppServerClientState
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
    Disposed,
}

public delegate ValueTask<CommandApprovalResponse?> CommandApprovalRequestedHandler(
    RpcApprovalEvent<CommandApprovalRequest> approval,
    CancellationToken cancellationToken);

public delegate ValueTask<FileChangeApprovalResponse?> FileChangeApprovalRequestedHandler(
    RpcApprovalEvent<FileChangeApprovalRequest> approval,
    CancellationToken cancellationToken);

public delegate ValueTask<PermissionsApprovalResponse?> PermissionsApprovalRequestedHandler(
    RpcApprovalEvent<PermissionsApprovalRequest> approval,
    CancellationToken cancellationToken);

public delegate ValueTask<CadApprovalResponse?> CadApprovalRequestedHandler(
    RpcApprovalEvent<CadApprovalRequest> approval,
    CancellationToken cancellationToken);

public delegate ValueTask<ServerRequestResolution?> ServerRequestReceivedHandler(
    AppServerServerRequest request,
    CancellationToken cancellationToken);

/// <summary>
/// Zero-dependency, bidirectional JSON-RPC client for <c>codex app-server --stdio</c>.
/// App Server JSON-RPC deliberately omits the usual <c>jsonrpc</c> wire property.
/// </summary>
public sealed class CodexAppServerClient : IAsyncDisposable
{
    public const string CommandApprovalMethod = "item/commandExecution/requestApproval";
    public const string FileChangeApprovalMethod = "item/fileChange/requestApproval";
    public const string PermissionsApprovalMethod = "item/permissions/requestApproval";
    public const string CadApprovalMethod = "cad/approval/request";

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly AppServerClientOptions _options;
    private readonly IAppServerTransport _transport;
    private readonly bool _ownsTransport;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<JsonRpcId, PendingRequest> _pendingRequests = new();
    private readonly ConcurrentDictionary<long, Task> _serverRequestTasks = new();
    private CancellationTokenSource? _connectionCancellation;
    private Task? _readLoop;
    private long _nextRequestId = -1;
    private long _nextServerTaskId;
    private int _state = (int)AppServerClientState.Created;
    private int _processExitReported;

    public CodexAppServerClient(AppServerClientOptions? options = null)
    {
        _options = options ?? new AppServerClientOptions();
        _options.Validate();
        _transport = new CodexProcessTransport(_options);
        _ownsTransport = true;
        SubscribeToTransport();
    }

    public CodexAppServerClient(IAppServerTransport transport, AppServerClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _options = options ?? new AppServerClientOptions();
        _options.Validate();
        _transport = transport;
        _ownsTransport = false;
        SubscribeToTransport();
    }

    public AppServerClientState State => (AppServerClientState)Volatile.Read(ref _state);

    public AppServerInitializeResponse? InitializeResponse { get; private set; }

    public event EventHandler<AppServerNotification>? NotificationReceived;

    public event CommandApprovalRequestedHandler? CommandApprovalRequested;

    public event FileChangeApprovalRequestedHandler? FileChangeApprovalRequested;

    public event PermissionsApprovalRequestedHandler? PermissionsApprovalRequested;

    public event CadApprovalRequestedHandler? CadApprovalRequested;

    public event ServerRequestReceivedHandler? ServerRequestReceived;

    public event EventHandler<AppServerProcessExitedEventArgs>? ProcessExited;

    public event EventHandler<AppServerProtocolFaultEventArgs>? ProtocolFaulted;

    public event EventHandler<AppServerStandardErrorEventArgs>? StandardErrorReceived;

    /// <summary>Starts App Server, performs initialize, then sends initialized.</summary>
    public async Task<AppServerInitializeResponse> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State == AppServerClientState.Running)
            {
                return InitializeResponse!;
            }

            if (State is AppServerClientState.Starting or AppServerClientState.Stopping)
            {
                throw new InvalidOperationException($"Cannot start while client state is {State}.");
            }

            Volatile.Write(ref _state, (int)AppServerClientState.Starting);
            Interlocked.Exchange(ref _processExitReported, 0);
            InitializeResponse = null;
            _connectionCancellation?.Dispose();
            _connectionCancellation = new CancellationTokenSource();

            try
            {
                await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
                _readLoop = RunReadLoopAsync(_connectionCancellation.Token);

                var initializeParams = new AppServerInitializeParams(_options.ClientInfo, _options.Capabilities);
                var response = await SendRequestCoreAsync<AppServerInitializeResponse>(
                    "initialize",
                    initializeParams,
                    allowWhileStarting: true,
                    cancellationToken).ConfigureAwait(false);

                await SendNotificationCoreAsync(
                    "initialized",
                    new { },
                    allowWhileStarting: true,
                    cancellationToken).ConfigureAwait(false);

                InitializeResponse = response;
                Volatile.Write(ref _state, (int)AppServerClientState.Running);
                return response;
            }
            catch
            {
                _connectionCancellation.Cancel();
                FailAllPending(new AppServerProcessExitedException(null));
                try
                {
                    await _transport.StopAsync(_options.ShutdownTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the startup exception.
                }

                Volatile.Write(ref _state, (int)AppServerClientState.Stopped);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is AppServerClientState.Created or AppServerClientState.Stopped or AppServerClientState.Disposed)
            {
                return;
            }

            Volatile.Write(ref _state, (int)AppServerClientState.Stopping);
            _connectionCancellation?.Cancel();
            await _transport.StopAsync(_options.ShutdownTimeout, cancellationToken).ConfigureAwait(false);

            if (_readLoop is not null)
            {
                try
                {
                    await _readLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected during an explicit stop.
                }
                catch (AppServerProcessExitedException)
                {
                    // Expected when the process closes stdout during stop.
                }
            }

            FailAllPending(new AppServerProcessExitedException(null));
            Volatile.Write(ref _state, (int)AppServerClientState.Stopped);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<TResult> SendRequestAsync<TResult>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
        => SendRequestCoreAsync<TResult>(method, parameters, allowWhileStarting: false, cancellationToken);

    public Task SendNotificationAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
        => SendNotificationCoreAsync(method, parameters, allowWhileStarting: false, cancellationToken);

    /// <summary>Interrupts an active turn. Local request cancellation alone does not stop model work.</summary>
    public async Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        _ = await SendRequestAsync<EmptyResponse>(
            "turn/interrupt",
            new TurnInterruptParams(threadId, turnId),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (State == AppServerClientState.Disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        Volatile.Write(ref _state, (int)AppServerClientState.Disposed);
        UnsubscribeFromTransport();
        if (_ownsTransport)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }

        _connectionCancellation?.Dispose();
        _lifecycleGate.Dispose();
        _writeGate.Dispose();
    }

    private async Task<TResult> SendRequestCoreAsync<TResult>(
        string method,
        object? parameters,
        bool allowWhileStarting,
        CancellationToken cancellationToken)
    {
        ValidateMethod(method);
        EnsureCanSend(allowWhileStarting);
        cancellationToken.ThrowIfCancellationRequested();

        var requestId = new JsonRpcId(Interlocked.Increment(ref _nextRequestId));
        var pending = new PendingRequest();
        if (!_pendingRequests.TryAdd(requestId, pending))
        {
            throw new InvalidOperationException($"Duplicate JSON-RPC request id {requestId}.");
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (_pendingRequests.TryRemove(requestId, out var removed))
            {
                removed.Completion.TrySetCanceled(cancellationToken);
            }
        });

        try
        {
            await WriteMessageAsync(new RpcRequestWire(requestId, method, parameters), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (_pendingRequests.TryRemove(requestId, out var removed))
            {
                removed.Completion.TrySetException(exception);
            }

            throw;
        }

        var result = await pending.Completion.Task.ConfigureAwait(false);
        if (typeof(TResult) == typeof(JsonElement))
        {
            return (TResult)(object)result.Clone();
        }

        try
        {
            return JsonSerializer.Deserialize<TResult>(result.GetRawText(), SerializerOptions)!;
        }
        catch (JsonException exception)
        {
            throw new AppServerProtocolException(
                $"Response to '{method}' could not be deserialized as {typeof(TResult).Name}.",
                exception);
        }
    }

    private Task SendNotificationCoreAsync(
        string method,
        object? parameters,
        bool allowWhileStarting,
        CancellationToken cancellationToken)
    {
        ValidateMethod(method);
        EnsureCanSend(allowWhileStarting);
        return WriteMessageAsync(new RpcNotificationWire(method, parameters), cancellationToken);
    }

    private async Task RunReadLoopAsync(CancellationToken cancellationToken)
    {
        var frameReader = new JsonLineFrameReader(_transport.ReadStream, _options.MaximumFrameBytes);
        try
        {
            while (true)
            {
                var frame = await frameReader.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    throw new AppServerProcessExitedException(null);
                }

                if (string.IsNullOrWhiteSpace(frame))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(
                    frame,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = _options.MaximumJsonDepth,
                    });
                HandleIncomingMessage(document.RootElement);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal stop or process-exit path.
        }
        catch (Exception exception)
        {
            var protocolException = exception as AppServerException
                ?? new AppServerProtocolException("Invalid App Server JSONL message.", exception);
            FailAllPending(protocolException);
            ReportProtocolFault(protocolException);
            if (State is AppServerClientState.Starting or AppServerClientState.Running)
            {
                Volatile.Write(ref _state, (int)AppServerClientState.Faulted);
                _connectionCancellation?.Cancel();
                _ = StopTransportAfterFaultAsync();
            }
        }
    }

    private void HandleIncomingMessage(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            throw new AppServerProtocolException("App Server JSON-RPC message must be an object.");
        }

        var hasMethod = message.TryGetProperty("method", out var methodElement);
        var hasId = message.TryGetProperty("id", out var idElement);

        if (hasMethod)
        {
            if (methodElement.ValueKind != JsonValueKind.String)
            {
                throw new AppServerProtocolException("JSON-RPC method must be a string.");
            }

            var method = methodElement.GetString()!;
            var parameters = message.TryGetProperty("params", out var paramsElement)
                ? paramsElement.Clone()
                : (JsonElement?)null;

            if (hasId)
            {
                if (!JsonRpcId.TryRead(idElement, out var requestId))
                {
                    throw new AppServerProtocolException("Server request id must be an integer or string.");
                }

                QueueServerRequest(new AppServerServerRequest(requestId, method, parameters));
            }
            else
            {
                RaiseNotification(new AppServerNotification(method, parameters));
            }

            return;
        }

        if (!hasId || !JsonRpcId.TryRead(idElement, out var responseId))
        {
            throw new AppServerProtocolException("JSON-RPC response is missing a valid id.");
        }

        HandleResponse(responseId, message);
    }

    private void HandleResponse(JsonRpcId responseId, JsonElement message)
    {
        if (!_pendingRequests.TryRemove(responseId, out var pending))
        {
            // Late responses are expected after caller-side cancellation.
            return;
        }

        var hasResult = message.TryGetProperty("result", out var result);
        var hasError = message.TryGetProperty("error", out var error);
        if (hasResult == hasError)
        {
            pending.Completion.TrySetException(
                new AppServerProtocolException("JSON-RPC response must contain exactly one of result or error."));
            return;
        }

        if (hasResult)
        {
            pending.Completion.TrySetResult(result.Clone());
            return;
        }

        if (error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out var codeElement)
            || !codeElement.TryGetInt64(out var code)
            || !error.TryGetProperty("message", out var errorMessageElement)
            || errorMessageElement.ValueKind != JsonValueKind.String)
        {
            pending.Completion.TrySetException(new AppServerProtocolException("Malformed JSON-RPC error response."));
            return;
        }

        var data = error.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : (JsonElement?)null;
        pending.Completion.TrySetException(new AppServerRpcException(code, errorMessageElement.GetString()!, data));
    }

    private void QueueServerRequest(AppServerServerRequest request)
    {
        var taskId = Interlocked.Increment(ref _nextServerTaskId);
        var task = DispatchServerRequestAsync(request, _connectionCancellation?.Token ?? CancellationToken.None);
        _serverRequestTasks[taskId] = task;
        _ = task.ContinueWith(
            _completed => _serverRequestTasks.TryRemove(taskId, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DispatchServerRequestAsync(AppServerServerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            switch (request.Method)
            {
                case CommandApprovalMethod:
                {
                    var approval = DeserializeRequiredParams<CommandApprovalRequest>(request);
                    var response = await InvokeCommandApprovalHandlersAsync(
                        new RpcApprovalEvent<CommandApprovalRequest>(request.Id, approval),
                        cancellationToken).ConfigureAwait(false) ?? CommandApprovalResponse.Decline;
                    await WriteResultAsync(request.Id, response.ToWireResponse(), cancellationToken).ConfigureAwait(false);
                    return;
                }

                case FileChangeApprovalMethod:
                {
                    var approval = DeserializeRequiredParams<FileChangeApprovalRequest>(request);
                    var response = await InvokeFileChangeApprovalHandlersAsync(
                        new RpcApprovalEvent<FileChangeApprovalRequest>(request.Id, approval),
                        cancellationToken).ConfigureAwait(false)
                        ?? new FileChangeApprovalResponse(FileChangeApprovalDecision.Decline);
                    await WriteResultAsync(request.Id, response, cancellationToken).ConfigureAwait(false);
                    return;
                }

                case PermissionsApprovalMethod:
                {
                    var approval = DeserializeRequiredParams<PermissionsApprovalRequest>(request);
                    var response = await InvokePermissionsApprovalHandlersAsync(
                        new RpcApprovalEvent<PermissionsApprovalRequest>(request.Id, approval),
                        cancellationToken).ConfigureAwait(false)
                        ?? new PermissionsApprovalResponse(new PermissionProfile());
                    await WriteResultAsync(request.Id, response, cancellationToken).ConfigureAwait(false);
                    return;
                }

                case CadApprovalMethod:
                {
                    var approval = DeserializeRequiredParams<CadApprovalRequest>(request);
                    var response = await InvokeCadApprovalHandlersAsync(
                        new RpcApprovalEvent<CadApprovalRequest>(request.Id, approval),
                        cancellationToken).ConfigureAwait(false)
                        ?? new CadApprovalResponse(
                            CadApprovalDecision.Decline,
                            approval.ApprovalId,
                            approval.NormalizedPlanHash);
                    await WriteResultAsync(request.Id, response, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var resolution = await InvokeServerRequestHandlersAsync(request, cancellationToken).ConfigureAwait(false);
            if (resolution is null)
            {
                await WriteErrorAsync(
                    request.Id,
                    new AppServerRpcError(-32601, $"Client does not implement server request '{request.Method}'."),
                    cancellationToken).ConfigureAwait(false);
            }
            else if (resolution.Error is not null)
            {
                await WriteErrorAsync(request.Id, resolution.Error, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteResultAsync(request.Id, resolution.Result ?? new { }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Connection closed before the user could answer.
        }
        catch (Exception exception)
        {
            ReportProtocolFault(exception);
            try
            {
                await WriteErrorAsync(
                    request.Id,
                    new AppServerRpcError(-32603, "Client failed to handle the server request."),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The connection is already gone; the original error was reported above.
            }
        }
    }

    private static T DeserializeRequiredParams<T>(AppServerServerRequest request)
    {
        if (request.Params is null)
        {
            throw new AppServerProtocolException($"Server request '{request.Method}' is missing params.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(request.Params.Value.GetRawText(), SerializerOptions)
                ?? throw new AppServerProtocolException($"Server request '{request.Method}' params were null.");
        }
        catch (JsonException exception)
        {
            throw new AppServerProtocolException($"Server request '{request.Method}' has invalid params.", exception);
        }
    }

    private async ValueTask<CommandApprovalResponse?> InvokeCommandApprovalHandlersAsync(
        RpcApprovalEvent<CommandApprovalRequest> approval,
        CancellationToken cancellationToken)
    {
        if (CommandApprovalRequested is null) return null;
        foreach (CommandApprovalRequestedHandler handler in CommandApprovalRequested.GetInvocationList())
        {
            var response = await handler(approval, cancellationToken).ConfigureAwait(false);
            if (response is not null) return response;
        }

        return null;
    }

    private async ValueTask<FileChangeApprovalResponse?> InvokeFileChangeApprovalHandlersAsync(
        RpcApprovalEvent<FileChangeApprovalRequest> approval,
        CancellationToken cancellationToken)
    {
        if (FileChangeApprovalRequested is null) return null;
        foreach (FileChangeApprovalRequestedHandler handler in FileChangeApprovalRequested.GetInvocationList())
        {
            var response = await handler(approval, cancellationToken).ConfigureAwait(false);
            if (response is not null) return response;
        }

        return null;
    }

    private async ValueTask<PermissionsApprovalResponse?> InvokePermissionsApprovalHandlersAsync(
        RpcApprovalEvent<PermissionsApprovalRequest> approval,
        CancellationToken cancellationToken)
    {
        if (PermissionsApprovalRequested is null) return null;
        foreach (PermissionsApprovalRequestedHandler handler in PermissionsApprovalRequested.GetInvocationList())
        {
            var response = await handler(approval, cancellationToken).ConfigureAwait(false);
            if (response is not null) return response;
        }

        return null;
    }

    private async ValueTask<CadApprovalResponse?> InvokeCadApprovalHandlersAsync(
        RpcApprovalEvent<CadApprovalRequest> approval,
        CancellationToken cancellationToken)
    {
        if (CadApprovalRequested is null) return null;
        foreach (CadApprovalRequestedHandler handler in CadApprovalRequested.GetInvocationList())
        {
            var response = await handler(approval, cancellationToken).ConfigureAwait(false);
            if (response is not null) return response;
        }

        return null;
    }

    private async ValueTask<ServerRequestResolution?> InvokeServerRequestHandlersAsync(
        AppServerServerRequest request,
        CancellationToken cancellationToken)
    {
        if (ServerRequestReceived is null) return null;
        foreach (ServerRequestReceivedHandler handler in ServerRequestReceived.GetInvocationList())
        {
            var response = await handler(request, cancellationToken).ConfigureAwait(false);
            if (response is not null) return response;
        }

        return null;
    }

    private Task WriteResultAsync(JsonRpcId id, object result, CancellationToken cancellationToken)
        => WriteMessageAsync(new RpcResultWire(id, result), cancellationToken);

    private Task WriteErrorAsync(JsonRpcId id, AppServerRpcError error, CancellationToken cancellationToken)
        => WriteMessageAsync(new RpcErrorWire(id, error), cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        if (payload.Length > _options.MaximumFrameBytes)
        {
            throw new AppServerProtocolException($"Outgoing App Server JSONL frame exceeds {_options.MaximumFrameBytes} bytes.");
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.WriteStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _transport.WriteStream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await _transport.WriteStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void EnsureCanSend(bool allowWhileStarting)
    {
        ThrowIfDisposed();
        var state = State;
        if (state != AppServerClientState.Running && !(allowWhileStarting && state == AppServerClientState.Starting))
        {
            throw new InvalidOperationException($"Cannot send App Server messages while client state is {state}.");
        }
    }

    private static void ValidateMethod(string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (method.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(method), "JSON-RPC method exceeds 256 characters.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(State == AppServerClientState.Disposed, this);
    }

    private void FailAllPending(Exception exception)
    {
        foreach (var (requestId, pending) in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(requestId, out _))
            {
                pending.Completion.TrySetException(exception);
            }
        }
    }

    private void RaiseNotification(AppServerNotification notification)
    {
        if (NotificationReceived is null) return;
        foreach (EventHandler<AppServerNotification> handler in NotificationReceived.GetInvocationList())
        {
            try
            {
                handler(this, notification);
            }
            catch (Exception exception)
            {
                ReportProtocolFault(exception);
            }
        }
    }

    private void ReportProtocolFault(Exception exception)
    {
        if (ProtocolFaulted is null) return;
        foreach (EventHandler<AppServerProtocolFaultEventArgs> handler in ProtocolFaulted.GetInvocationList())
        {
            try
            {
                handler(this, new AppServerProtocolFaultEventArgs(exception));
            }
            catch
            {
                // Diagnostic observers must never tear down the transport.
            }
        }
    }

    private async Task StopTransportAfterFaultAsync()
    {
        try
        {
            await _transport.StopAsync(_options.ShutdownTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The protocol failure has already been reported.
        }
    }

    private void SubscribeToTransport()
    {
        _transport.Exited += OnTransportExited;
        _transport.StandardErrorReceived += OnStandardErrorReceived;
    }

    private void UnsubscribeFromTransport()
    {
        _transport.Exited -= OnTransportExited;
        _transport.StandardErrorReceived -= OnStandardErrorReceived;
    }

    private void OnTransportExited(object? sender, AppServerTransportExitedEventArgs args)
    {
        _connectionCancellation?.Cancel();
        FailAllPending(new AppServerProcessExitedException(args.ExitCode, args.StandardErrorTail));

        if (State != AppServerClientState.Disposed)
        {
            Volatile.Write(
                ref _state,
                (int)(args.Expected ? AppServerClientState.Stopped : AppServerClientState.Faulted));
        }

        if (Interlocked.Exchange(ref _processExitReported, 1) == 0 && ProcessExited is not null)
        {
            var eventArgs = new AppServerProcessExitedEventArgs(args.ExitCode, args.Expected, args.StandardErrorTail);
            foreach (EventHandler<AppServerProcessExitedEventArgs> handler in ProcessExited.GetInvocationList())
            {
                try
                {
                    handler(this, eventArgs);
                }
                catch (Exception exception)
                {
                    ReportProtocolFault(exception);
                }
            }
        }
    }

    private void OnStandardErrorReceived(object? sender, AppServerStandardErrorEventArgs args)
        => StandardErrorReceived?.Invoke(this, args);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonRpcIdJsonConverter());
        return options;
    }

    private sealed class PendingRequest
    {
        public TaskCompletionSource<JsonElement> Completion { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record RpcRequestWire(JsonRpcId Id, string Method, object? Params);

    private sealed record RpcNotificationWire(string Method, object? Params);

    private sealed record RpcResultWire(JsonRpcId Id, object Result);

    private sealed record RpcErrorWire(JsonRpcId Id, AppServerRpcError Error);
}
