using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.Bridge;

public sealed class AuthenticatedPipeConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32
    };

    private readonly Stream _stream;
    private readonly object _disposeSync = new();
    private readonly string _sessionId;
    private readonly IpcEnvelopeAuthenticator _authenticator;
    private readonly IpcSessionGuard _incomingGuard;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ResponsePayload>> _pendingRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _handlerTasks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _pendingRequestSlots;
    private readonly SemaphoreSlim _pendingNotificationSlots;
    private readonly SemaphoreSlim _activeRequestSlots;
    private readonly SemaphoreSlim _handlerSlots;
    private readonly int _maximumPendingRequests;
    private readonly int _maximumPendingNotifications;
    private readonly int _maximumActiveRequests;
    private readonly int _maximumConcurrentHandlers;
    private readonly int _maximumFrameBytes;
    private readonly TimeSpan _shutdownTimeout;
    private BridgeRequestHandler? _requestHandler;
    private BridgeNotificationHandler? _notificationHandler;
    private Task? _receiveTask;
    private Task? _disposeTask;
    private Task? _cleanupTask;
    private Exception? _terminalError;
    private long _outgoingSequence;
    private int _started;
    private int _disposed;

    public AuthenticatedPipeConnection(
        Stream stream,
        string sessionId,
        byte[] sessionSecret,
        BridgeConnectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException("桥接流必须同时支持读取和写入。", nameof(stream));
        }

        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > IpcSessionGuard.MaximumIdentifierCharacters)
        {
            throw new ArgumentException(
                $"SessionId不能为空且不能超过{IpcSessionGuard.MaximumIdentifierCharacters}个字符。",
                nameof(sessionId));
        }

        if (sessionSecret is null)
        {
            throw new ArgumentNullException(nameof(sessionSecret));
        }

        if (sessionSecret.Length != IpcSessionSecret.SizeInBytes)
        {
            throw new ArgumentException("桥接会话密钥必须恰好为256位。", nameof(sessionSecret));
        }

        options ??= new BridgeConnectionOptions();
        options.Validate();
        _stream = stream;
        _sessionId = sessionId;
        _incomingGuard = new IpcSessionGuard(sessionId, sessionSecret, options.SessionGuard);
        _authenticator = new IpcEnvelopeAuthenticator(sessionSecret);
        _maximumPendingRequests = options.MaximumPendingRequests;
        _maximumPendingNotifications = options.MaximumPendingNotifications;
        _maximumActiveRequests = options.MaximumActiveRequests;
        _maximumConcurrentHandlers = options.MaximumConcurrentHandlers;
        _maximumFrameBytes = options.MaximumFrameBytes;
        _shutdownTimeout = options.ShutdownTimeout;
        _pendingRequestSlots = new SemaphoreSlim(_maximumPendingRequests, _maximumPendingRequests);
        _pendingNotificationSlots = new SemaphoreSlim(
            _maximumPendingNotifications,
            _maximumPendingNotifications);
        _activeRequestSlots = new SemaphoreSlim(_maximumActiveRequests, _maximumActiveRequests);
        _handlerSlots = new SemaphoreSlim(_maximumConcurrentHandlers, _maximumConcurrentHandlers);
    }

    public Task Completion => _receiveTask ?? Task.CompletedTask;

    public Exception? TerminalError => Volatile.Read(ref _terminalError);

    public void Start(
        BridgeRequestHandler? requestHandler = null,
        BridgeNotificationHandler? notificationHandler = null)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("桥接连接只能启动一次。");
        }

        _requestHandler = requestHandler;
        _notificationHandler = notificationHandler;
        _receiveTask = ReceiveLoopAsync(_lifetime.Token);
    }

    public async Task<string> RequestAsync(
        string method,
        string bodyJson,
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        ValidateMethod(method);
        ValidateJson(bodyJson, nameof(bodyJson));
        if (!_pendingRequestSlots.Wait(0))
        {
            throw new BridgeCapacityExceededException(
                BridgeCapacityKind.PendingRequests,
                _maximumPendingRequests);
        }

        var messageId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<ResponsePayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(messageId, completion))
        {
            _pendingRequestSlots.Release();
            throw new InvalidOperationException("无法登记IPC请求。");
        }

        try
        {
            await SendEnvelopeAsync(
                    BridgeMessageTypes.Request,
                    messageId,
                    string.Empty,
                    new RequestPayload { Method = method, BodyJson = bodyJson },
                    cancellationToken)
                .ConfigureAwait(false);

            ResponsePayload response;
            try
            {
                response = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TrySendCancellationAsync(messageId).ConfigureAwait(false);
                throw;
            }

            if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            {
                throw new BridgeRemoteException(response.ErrorCode, response.ErrorMessage);
            }

            ValidateJson(response.BodyJson, "远端响应");
            return response.BodyJson;
        }
        finally
        {
            _pendingRequests.TryRemove(messageId, out _);
            _pendingRequestSlots.Release();
        }
    }

    public async Task NotifyAsync(
        string method,
        string bodyJson,
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        ValidateMethod(method);
        ValidateJson(bodyJson, nameof(bodyJson));
        if (!_pendingNotificationSlots.Wait(0))
        {
            throw new BridgeCapacityExceededException(
                BridgeCapacityKind.PendingNotifications,
                _maximumPendingNotifications);
        }

        try
        {
            await SendEnvelopeAsync(
                    BridgeMessageTypes.Notification,
                    Guid.NewGuid().ToString("N"),
                    string.Empty,
                    new RequestPayload { Method = method, BodyJson = bodyJson },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _pendingNotificationSlots.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        TryCancel(_lifetime);
        CancelActiveRequests();

        var streamDisposeTask = DisposeStreamSafelyAsync();
        var transportCleanupTask = QuiesceTransportAndClearSecretsAsync();
        var handlerDrainTask = DrainHandlersAsync();
        _cleanupTask = CompleteDeferredCleanupAsync(
            streamDisposeTask,
            transportCleanupTask,
            handlerDrainTask);

        try
        {
            await _cleanupTask.WaitAsync(_shutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A non-cooperative stream or handler must not make DisposeAsync unbounded.
            // Deferred cleanup remains rooted and clears secrets only after receive/send quiesce.
        }
    }

    private async Task DisposeStreamSafelyAsync()
    {
        await Task.Yield();
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            RecordTerminalError(exception);
        }
    }

    private async Task QuiesceTransportAndClearSecretsAsync()
    {
        await ObserveShutdownTaskAsync(_receiveTask).ConfigureAwait(false);
        await _sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                _authenticator.Dispose();
            }
            catch (Exception exception)
            {
                RecordTerminalError(exception);
            }

            try
            {
                _incomingGuard.Dispose();
            }
            catch (Exception exception)
            {
                RecordTerminalError(exception);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task DrainHandlersAsync()
    {
        await ObserveShutdownTaskAsync(_receiveTask).ConfigureAwait(false);
        var handlers = _handlerTasks.Values.ToArray();
        if (handlers.Length > 0)
        {
            await ObserveShutdownTaskAsync(Task.WhenAll(handlers)).ConfigureAwait(false);
        }
    }

    private async Task CompleteDeferredCleanupAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordTerminalError(exception);
        }
    }

    private async Task ObserveShutdownTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            RecordTerminalError(exception);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var envelope = await LengthPrefixedFrameCodec.ReadAsync(
                        _stream,
                        cancellationToken,
                        _maximumFrameBytes)
                    .ConfigureAwait(false);
                if (envelope is null)
                {
                    break;
                }

                var validation = _incomingGuard.ValidateAndAccept(envelope);
                if (validation != IpcValidationCode.Accepted)
                {
                    throw new BridgeAuthenticationException(validation);
                }

                Dispatch(envelope, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RecordTerminalError(exception);
            throw;
        }
        finally
        {
            var error = Volatile.Read(ref _terminalError) ?? new EndOfStreamException("IPC连接已关闭。");
            TryCancel(_lifetime);
            foreach (var pending in _pendingRequests.Values)
            {
                pending.TrySetException(error);
            }

            CancelActiveRequests();

            try
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }

        var terminalError = Volatile.Read(ref _terminalError);
        if (terminalError is not null)
        {
            throw terminalError;
        }
    }

    private void Dispatch(IpcEnvelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.MessageType)
        {
            case BridgeMessageTypes.Request:
            {
                var payload = Deserialize<RequestPayload>(envelope.PayloadJson, "请求");
                ValidateMethod(payload.Method);
                ValidateJson(payload.BodyJson, "请求载荷");
                if (!_activeRequestSlots.Wait(0))
                {
                    throw new BridgeCapacityExceededException(
                        BridgeCapacityKind.ActiveRequests,
                        _maximumActiveRequests);
                }

                var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (!_activeRequests.TryAdd(envelope.MessageId, requestCancellation))
                {
                    requestCancellation.Dispose();
                    _activeRequestSlots.Release();
                    throw new BridgeProtocolException($"重复的请求ID：{envelope.MessageId}。");
                }

                try
                {
                    TrackHandler(
                        envelope.MessageId,
                        () => HandleRequestAsync(envelope, payload, requestCancellation, cancellationToken));
                }
                catch
                {
                    _activeRequests.TryRemove(envelope.MessageId, out _);
                    requestCancellation.Dispose();
                    _activeRequestSlots.Release();
                    throw;
                }

                break;
            }
            case BridgeMessageTypes.Response:
                HandleResponse(envelope);
                break;
            case BridgeMessageTypes.Notification:
            {
                var payload = Deserialize<RequestPayload>(envelope.PayloadJson, "通知");
                ValidateMethod(payload.Method);
                ValidateJson(payload.BodyJson, "通知载荷");
                TrackHandler(
                    envelope.MessageId,
                    () => HandleNotificationAsync(envelope, payload, cancellationToken));
                break;
            }
            case BridgeMessageTypes.Cancel:
                HandleCancellation(envelope);
                break;
            default:
                throw new BridgeProtocolException($"不支持的IPC消息类型：{envelope.MessageType}。");
        }
    }

    private void TrackHandler(string messageId, Func<Task> handlerFactory)
    {
        if (!_handlerSlots.Wait(0))
        {
            throw new BridgeCapacityExceededException(
                BridgeCapacityKind.ConcurrentHandlers,
                _maximumConcurrentHandlers);
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_handlerTasks.TryAdd(messageId, completion.Task))
        {
            _handlerSlots.Release();
            throw new BridgeProtocolException($"重复的IPC消息ID：{messageId}。");
        }

        _ = RunTrackedHandlerAsync(messageId, handlerFactory, completion);
    }

    private async Task RunTrackedHandlerAsync(
        string messageId,
        Func<Task> handlerFactory,
        TaskCompletionSource completion)
    {
        await Task.Yield();
        try
        {
            await handlerFactory().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RecordTerminalError(exception);
            await AbortTransportAsync().ConfigureAwait(false);
        }
        finally
        {
            _handlerTasks.TryRemove(messageId, out _);
            _handlerSlots.Release();
            completion.TrySetResult();
        }
    }

    private async Task HandleRequestAsync(
        IpcEnvelope envelope,
        RequestPayload payload,
        CancellationTokenSource requestCancellation,
        CancellationToken connectionToken)
    {
        using (requestCancellation)
        {
            ResponsePayload response;
            try
            {
                if (_requestHandler is null)
                {
                    response = new ResponsePayload
                    {
                        ErrorCode = "method_not_supported",
                        ErrorMessage = "此连接未注册请求处理器。"
                    };
                }
                else
                {
                    var request = new BridgeRequest(envelope.MessageId, payload.Method, payload.BodyJson);
                    var body = await _requestHandler(request, requestCancellation.Token).ConfigureAwait(false) ?? "null";
                    ValidateJson(body, "响应载荷");
                    response = new ResponsePayload { BodyJson = body };
                }
            }
            catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
            {
                response = new ResponsePayload
                {
                    ErrorCode = "request_cancelled",
                    ErrorMessage = "请求已取消。"
                };
            }
            catch (Exception)
            {
                response = new ResponsePayload
                {
                    ErrorCode = "handler_error",
                    ErrorMessage = "远端请求处理失败。"
                };
            }
            finally
            {
                if (_activeRequests.TryRemove(envelope.MessageId, out _))
                {
                    _activeRequestSlots.Release();
                }
            }

            if (!connectionToken.IsCancellationRequested)
            {
                await SendEnvelopeAsync(
                        BridgeMessageTypes.Response,
                        Guid.NewGuid().ToString("N"),
                        envelope.MessageId,
                        response,
                        connectionToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private void HandleResponse(IpcEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.CorrelationId))
        {
            throw new BridgeProtocolException("IPC响应缺少CorrelationId。");
        }

        var response = Deserialize<ResponsePayload>(envelope.PayloadJson, "响应");
        if (_pendingRequests.TryGetValue(envelope.CorrelationId, out var completion))
        {
            completion.TrySetResult(response);
        }
    }

    private async Task HandleNotificationAsync(
        IpcEnvelope envelope,
        RequestPayload payload,
        CancellationToken connectionToken)
    {
        if (_notificationHandler is not null)
        {
            var notification = new BridgeNotification(envelope.MessageId, payload.Method, payload.BodyJson);
            await _notificationHandler(notification, connectionToken).ConfigureAwait(false);
        }
    }

    private void HandleCancellation(IpcEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.CorrelationId))
        {
            throw new BridgeProtocolException("IPC取消消息缺少CorrelationId。");
        }

        _ = Deserialize<CancelPayload>(envelope.PayloadJson, "取消");
        if (_activeRequests.TryGetValue(envelope.CorrelationId, out var cancellation))
        {
            TryCancel(cancellation);
        }
    }

    private async Task TrySendCancellationAsync(string requestId)
    {
        try
        {
            await SendEnvelopeAsync(
                    BridgeMessageTypes.Cancel,
                    Guid.NewGuid().ToString("N"),
                    requestId,
                    new CancelPayload { Reason = "caller_cancelled" },
                    _lifetime.Token)
                .ConfigureAwait(false);
        }
        catch (Exception) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task SendEnvelopeAsync<TPayload>(
        string messageType,
        string messageId,
        string correlationId,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lifetime.IsCancellationRequested)
            {
                throw new EndOfStreamException("IPC连接已终止。");
            }

            var nextSequence = checked(_outgoingSequence + 1);
            var envelope = new IpcEnvelope
            {
                MessageId = messageId,
                CorrelationId = correlationId,
                SessionId = _sessionId,
                Sequence = nextSequence,
                MessageType = messageType,
                PayloadJson = JsonSerializer.Serialize(payload, SerializerOptions),
                Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            };
            envelope.Mac = _authenticator.Sign(envelope);
            try
            {
                await LengthPrefixedFrameCodec.WriteAsync(
                        _stream,
                        envelope,
                        cancellationToken,
                        _maximumFrameBytes)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is not BridgeProtocolException &&
                exception is IOException or ObjectDisposedException or OperationCanceledException)
            {
                await AbortTransportAsync().ConfigureAwait(false);
                throw;
            }

            _outgoingSequence = nextSequence;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task AbortTransportAsync()
    {
        TryCancel(_lifetime);
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private void CancelActiveRequests()
    {
        foreach (var request in _activeRequests.Values)
        {
            TryCancel(request);
        }
    }

    private void RecordTerminalError(Exception exception)
    {
        _ = Interlocked.CompareExchange(ref _terminalError, exception, null);
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static T Deserialize<T>(string json, string label)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions)
                ?? throw new BridgeProtocolException($"IPC{label}载荷为空。");
        }
        catch (JsonException exception)
        {
            throw new BridgeProtocolException($"IPC{label}载荷不是有效JSON。", exception);
        }
    }

    private static void ValidateMethod(string method)
    {
        if (string.IsNullOrWhiteSpace(method) || method.Length > 256)
        {
            throw new ArgumentException("IPC方法名不能为空且不能超过256个字符。", nameof(method));
        }
    }

    private void ValidateJson(string json, string parameterName)
    {
        if (json is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > _maximumFrameBytes)
        {
            throw new BridgeProtocolException(
                $"IPC JSON载荷大小{byteCount}字节，超过{_maximumFrameBytes}字节上限。");
        }

        try
        {
            using var _ = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("IPC载荷必须是有效JSON。", parameterName, exception);
        }
    }

    private void EnsureStarted()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _started) == 0)
        {
            throw new InvalidOperationException("使用桥接连接前必须调用Start。");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
