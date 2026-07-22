using System.Collections.Generic;
using System.IO.Pipes;
using System.Security.Cryptography;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.Bridge.Client;

public sealed class AgentBridgeClient : IAgentBridgeClient
{
    private const string RequestMessageType = "bridge.request";
    private const string ResponseMessageType = "bridge.response";
    private const string NotificationMessageType = "bridge.notification";
    private const string CancelMessageType = "bridge.cancel";
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(1);
    private const int MaximumTrackedTurns = 256;

    private readonly object _sync = new object();
    private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
    private readonly string _pipeName;
    private readonly string _sessionId;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _shutdownTimeout;
    private readonly int _maximumFrameBytes;
    private readonly AgentDrawingQueryHandler? _drawingQueryHandler;
    private readonly SemaphoreSlim _drawingQuerySlots;
    private readonly Dictionary<string, TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>>
        _pendingRequests =
            new Dictionary<string, TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>>(
                StringComparer.Ordinal);
    private readonly Dictionary<string, TurnIdentity> _activeTurns =
        new Dictionary<string, TurnIdentity>(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingTurnIdentity> _pendingTurnStarts =
        new Dictionary<string, PendingTurnIdentity>(StringComparer.Ordinal);
    private readonly HashSet<string> _seenEventIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveDrawingQuery> _activeDrawingQueries =
        new Dictionary<string, ActiveDrawingQuery>(StringComparer.Ordinal);
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _lifetime;
    private IpcEnvelopeAuthenticator? _authenticator;
    private IpcSessionGuard? _incomingGuard;
    private Task? _receiveTask;
    private Task? _stopAttempt;
    private AgentBridgeClientException? _terminalError;
    private long _outgoingSequence;
    private long _lastEventSequence;
    private int _disposeSignaled;
    private ClientState _state;
    private bool _stopStarted;
    private bool _sendQuiesced;
    private bool _receiveSettled;
    private bool _drawingQueriesSettled;
    private bool _securityReleased;
    private bool _stopCompleted;

    public event EventHandler<AgentBridgeEventReceivedEventArgs>? EventReceived;

    public event EventHandler<AgentBridgeConnectionFaultedEventArgs>? ConnectionFaulted;

    public AgentBridgeClient(AgentBridgeClientOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        ValidatePipeName(options.PipeName);
        if (string.IsNullOrWhiteSpace(options.SessionId)
            || options.SessionId.Length > IpcSessionGuard.MaximumIdentifierCharacters)
        {
            throw new ArgumentException("SessionId为空或超过安全长度。", nameof(options));
        }

        if (options.SessionSecret is null
            || options.SessionSecret.Length != IpcSessionSecret.SizeInBytes)
        {
            throw new ArgumentException("Bridge会话密钥必须恰好为256位。", nameof(options));
        }

        ValidateTimeout(options.ConnectTimeout, nameof(options.ConnectTimeout));
        ValidateTimeout(options.RequestTimeout, nameof(options.RequestTimeout));
        ValidateTimeout(options.ShutdownTimeout, nameof(options.ShutdownTimeout));
        if (options.MaximumFrameBytes <= 0
            || options.MaximumFrameBytes > ProtocolConstants.MaximumMessageBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaximumFrameBytes));
        }
        if (options.MaximumConcurrentDrawingQueries < 1
            || options.MaximumConcurrentDrawingQueries > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaximumConcurrentDrawingQueries));
        }

        _pipeName = options.PipeName;
        _sessionId = options.SessionId;
        IpcEnvelopeAuthenticator? authenticator = null;
        try
        {
            authenticator = new IpcEnvelopeAuthenticator(options.SessionSecret);
            _incomingGuard = new IpcSessionGuard(_sessionId, options.SessionSecret);
            _authenticator = authenticator;
            authenticator = null;
        }
        finally
        {
            if (authenticator is not null)
            {
                authenticator.Dispose();
            }
        }
        _connectTimeout = options.ConnectTimeout;
        _requestTimeout = options.RequestTimeout;
        _shutdownTimeout = options.ShutdownTimeout;

        _maximumFrameBytes = options.MaximumFrameBytes;
        _drawingQueryHandler = options.DrawingQueryHandler;
        _drawingQuerySlots = new SemaphoreSlim(
            options.MaximumConcurrentDrawingQueries,
            options.MaximumConcurrentDrawingQueries);
    }
    public AgentBridgeClient(
        AgentBootstrapDirectionKeys directionKeys,
        TimeSpan connectTimeout,
        TimeSpan requestTimeout,
        int maximumFrameBytes = ProtocolConstants.MaximumMessageBytes,
        TimeSpan? shutdownTimeout = null,
        AgentDrawingQueryHandler? drawingQueryHandler = null,
        int maximumConcurrentDrawingQueries = 2)
    {
        if (directionKeys is null)
        {
            throw new ArgumentNullException(nameof(directionKeys));
        }

        ValidatePipeName(directionKeys.PipeName);
        if (string.IsNullOrWhiteSpace(directionKeys.SessionId)
            || directionKeys.SessionId.Length > IpcSessionGuard.MaximumIdentifierCharacters)
        {
            throw new ArgumentException(
                "Bootstrap SessionId为空或超过安全长度。",
                nameof(directionKeys));
        }

        ValidateTimeout(connectTimeout, nameof(connectTimeout));
        ValidateTimeout(requestTimeout, nameof(requestTimeout));
        var validatedShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5);
        ValidateTimeout(validatedShutdownTimeout, nameof(shutdownTimeout));
        if (maximumFrameBytes <= 0
            || maximumFrameBytes > ProtocolConstants.MaximumMessageBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));
        }
        if (maximumConcurrentDrawingQueries < 1 || maximumConcurrentDrawingQueries > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentDrawingQueries));
        }

        _pipeName = directionKeys.PipeName;
        _sessionId = directionKeys.SessionId;
        _connectTimeout = connectTimeout;
        _requestTimeout = requestTimeout;
        _shutdownTimeout = validatedShutdownTimeout;
        _maximumFrameBytes = maximumFrameBytes;
        _drawingQueryHandler = drawingQueryHandler;
        _drawingQuerySlots = new SemaphoreSlim(
            maximumConcurrentDrawingQueries,
            maximumConcurrentDrawingQueries);
        IpcEnvelopeAuthenticator? authenticator = null;
        try
        {
            authenticator = directionKeys.CreateOutboundAuthenticator();
            _incomingGuard = directionKeys.CreateInboundGuard();
            _authenticator = authenticator;
            authenticator = null;
        }
        finally
        {
            if (authenticator is not null)
            {
                authenticator.Dispose();
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe;
        lock (_sync)
        {
            if (_state != ClientState.Created)
            {
                throw CreateStateException("Agent Bridge Client只能启动一次。");
            }

            _state = ClientState.Starting;
            pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            _pipe = pipe;
        }

        try
        {
            await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var lifetime = new CancellationTokenSource();
            lock (_sync)
            {
                if (_state != ClientState.Starting
                    || _authenticator is null
                    || _incomingGuard is null)
                {
                    lifetime.Dispose();
                    throw CreateStateException("Agent Bridge Client启动期间已终止。");
                }

                _lifetime = lifetime;
                _state = ClientState.Online;
                _receiveTask = ReceiveLoopAsync(pipe, lifetime.Token);
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            FailConnection(new AgentBridgeClientException(
                AgentBridgeErrorCodes.Offline,
                "Agent Bridge启动已取消。",
                exception));
            throw;
        }
        catch (Exception exception)
        {
            var terminal = NormalizeTerminalError(exception, "offline");
            FailConnection(terminal);
            throw terminal;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task attempt;
        lock (_sync)
        {
            if (_state == ClientState.Disposed || _stopCompleted)
            {
                return Task.FromResult(0);
            }

            if (_stopAttempt is null)
            {
                CancellationTokenSource? lifetime = null;
                NamedPipeClientStream? pipe = null;
                var pending = new List<
                    TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>>();
                if (!_stopStarted)
                {
                    _stopStarted = true;
                    _state = ClientState.Stopped;
                    lifetime = _lifetime;
                    pipe = _pipe;
                    pending = _pendingRequests.Values.ToList();
                    _pendingRequests.Clear();
                    _activeTurns.Clear();
                    _pendingTurnStarts.Clear();
                    _seenEventIds.Clear();
                    _lastEventSequence = 0;
                }

                var completion = new TaskCompletionSource<bool>();
                attempt = completion.Task;
                _stopAttempt = attempt;
                _ = CompleteStopAttemptAsync(
                    completion,
                    attempt,
                    lifetime,
                    pipe,
                    _receiveTask,
                    pending);
            }
            else
            {
                attempt = _stopAttempt;
            }
        }

        return AwaitWithCancellationAsync(attempt, cancellationToken);
    }

    private async Task CompleteStopAttemptAsync(
        TaskCompletionSource<bool> completion,
        Task attempt,
        CancellationTokenSource? lifetime,
        NamedPipeClientStream? pipe,
        Task? receiveTask,
        List<TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>> pending)
    {
        Exception? stopFailure = null;
        try
        {
            stopFailure = await RunStopAttemptAsync(
                    lifetime,
                    pipe,
                    receiveTask,
                    pending)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            stopFailure = exception;
        }

        lock (_sync)
        {
            if (ReferenceEquals(_stopAttempt, attempt))
            {
                _stopAttempt = null;
            }

            if (stopFailure is null)
            {
                _stopCompleted = true;
            }
        }

        if (stopFailure is null)
        {
            completion.TrySetResult(true);
        }
        else
        {
            completion.TrySetException(stopFailure);
        }
    }

    private async Task<Exception?> RunStopAttemptAsync(
        CancellationTokenSource? lifetime,
        NamedPipeClientStream? pipe,
        Task? receiveTask,
        List<TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>> pending)
    {
        Exception? stopFailure = null;
        try
        {
            TryCancel(lifetime);
            SafeDispose(pipe);
            var stopped = new AgentBridgeClientException(
                AgentBridgeErrorCodes.ConnectionLost,
                "Agent Bridge连接已停止。");
            foreach (var pendingCompletion in pending)
            {
                pendingCompletion.TrySetException(stopped);
            }

            bool sendQuiesced;
            lock (_sync)
            {
                sendQuiesced = _sendQuiesced;
            }

            if (!sendQuiesced)
            {
                using (var sendTimeout = new CancellationTokenSource(_shutdownTimeout))
                {
                    try
                    {
                        await _sendGate.WaitAsync(sendTimeout.Token).ConfigureAwait(false);
                        _sendGate.Release();
                        lock (_sync)
                        {
                            _sendQuiesced = true;
                        }
                    }
                    catch (OperationCanceledException) when (sendTimeout.IsCancellationRequested)
                    {
                        stopFailure = new AgentBridgeClientException(
                            AgentBridgeErrorCodes.Timeout,
                            "Agent Bridge发送通道未在关闭期限内释放。");
                    }
                }
            }

            bool receiveSettled;
            lock (_sync)
            {
                receiveSettled = _receiveSettled;
            }

            if (!receiveSettled)
            {
                if (receiveTask is null)
                {
                    lock (_sync)
                    {
                        _receiveSettled = true;
                    }
                }
                else
                {
                    var completed = await Task.WhenAny(
                            receiveTask,
                            Task.Delay(_shutdownTimeout))
                        .ConfigureAwait(false);
                    if (completed == receiveTask)
                    {
                        // The receive loop has ended. Its connection failure was already
                        // projected through FailConnection; a faulted terminal task does not
                        // mean that cleanup still owns a live pipe or thread.
                        ObserveFault(receiveTask);
                        lock (_sync)
                        {
                            _receiveSettled = true;
                        }
                    }
                    else
                    {
                        ObserveFault(receiveTask);
                        stopFailure = stopFailure ?? new AgentBridgeClientException(
                            AgentBridgeErrorCodes.Timeout,
                            "Agent Bridge接收循环未在关闭期限内结束。");
                    }
                }
            }

            bool drawingQueriesSettled;
            lock (_sync)
            {
                drawingQueriesSettled = _drawingQueriesSettled;
            }

            if (!drawingQueriesSettled)
            {
                ActiveDrawingQuery[] activeQueries;
                lock (_sync)
                {
                    activeQueries = _activeDrawingQueries.Values.ToArray();
                }

                foreach (var activeQuery in activeQueries)
                {
                    TryCancel(activeQuery.Cancellation);
                }

                var handlers = activeQueries.Select(value => value.Completion.Task).ToArray();
                if (handlers.Length == 0)
                {
                    lock (_sync)
                    {
                        _drawingQueriesSettled = true;
                    }
                }
                else
                {
                    var allHandlers = Task.WhenAll(handlers);
                    var completed = await Task.WhenAny(
                            allHandlers,
                            Task.Delay(_shutdownTimeout))
                        .ConfigureAwait(false);
                    if (completed == allHandlers)
                    {
                        ObserveFault(allHandlers);
                        lock (_sync)
                        {
                            _drawingQueriesSettled = true;
                        }
                    }
                    else
                    {
                        ObserveFault(allHandlers);
                        stopFailure = stopFailure ?? new AgentBridgeClientException(
                            AgentBridgeErrorCodes.Timeout,
                            "Agent Bridge反向图纸查询未在关闭期限内结束。");
                    }
                }
            }

            bool releaseSecurity;
            lock (_sync)
            {
                releaseSecurity = _sendQuiesced
                    && _receiveSettled
                    && _drawingQueriesSettled
                    && !_securityReleased;
            }

            if (releaseSecurity)
            {
                DisposeSecurityMaterials();
                lock (_sync)
                {
                    _securityReleased = true;
                }
            }
        }
        catch (Exception exception)
        {
            stopFailure = stopFailure ?? exception;
        }

        if (stopFailure is null)
        {
            lock (_sync)
            {
                if (!_sendQuiesced
                    || !_receiveSettled
                    || !_drawingQueriesSettled
                    || !_securityReleased)
                {
                    stopFailure = new InvalidOperationException(
                        "Agent Bridge cleanup did not complete every owned shutdown phase.");
                }
            }
        }

        return stopFailure;
    }

    public async Task<AgentCapabilitiesResponse> GetCapabilitiesAsync(
        AgentCapabilitiesRequest request,
        CancellationToken cancellationToken)
    {
        var bodyJson = BridgeClientJsonCodec.SerializeCapabilitiesRequest(request);
        var responseJson = await RequestAsync(
                AgentBridgeMethods.GetCapabilities,
                bodyJson,
                cancellationToken)
            .ConfigureAwait(false);
        return BridgeClientJsonCodec.DeserializeCapabilitiesResponse(responseJson);
    }

    public async Task<AgentThreadStartResponse> StartThreadAsync(
        AgentThreadStartRequest request,
        CancellationToken cancellationToken)
    {
        var bodyJson = BridgeClientJsonCodec.SerializeThreadStartRequest(request);
        var responseJson = await RequestAsync(
                AgentBridgeMethods.StartThread,
                bodyJson,
                cancellationToken)
            .ConfigureAwait(false);
        return BridgeClientJsonCodec.DeserializeThreadStartResponse(responseJson);
    }

    public async Task<AgentTurnStartResponse> StartTurnAsync(
        AgentTurnStartRequest request,
        CancellationToken cancellationToken)
    {
        var bodyJson = BridgeClientJsonCodec.SerializeTurnStartRequest(request);
        var pending = RegisterPendingTurnStart(
            request.ClientTurnId,
            request.ThreadId,
            request.ContextSha256);
        var completed = false;
        try
        {
            var responseJson = await RequestAsync(
                    AgentBridgeMethods.StartTurn,
                    bodyJson,
                    cancellationToken)
                .ConfigureAwait(false);
            var response = BridgeClientJsonCodec.DeserializeTurnStartResponse(responseJson, request);
            CompletePendingTurnStart(
                pending,
                response.ThreadId,
                response.TurnId,
                response.AcceptedContextSha256);
            completed = true;
            return response;
        }
        finally
        {
            if (!completed)
            {
                AbandonPendingTurnStart(pending);
            }
        }
    }

    public async Task<AgentTurnStartV2Response> StartTurnV2Async(
        AgentTurnStartV2Request request,
        CancellationToken cancellationToken)
    {
        var bodyJson = BridgeClientJsonCodec.SerializeTurnStartV2Request(request);
        var pending = RegisterPendingTurnStart(
            request.ClientTurnId,
            request.ThreadId,
            request.ContextV2Sha256);
        var completed = false;
        try
        {
            var responseJson = await RequestAsync(
                    AgentBridgeMethods.StartTurnV2,
                    bodyJson,
                    cancellationToken)
                .ConfigureAwait(false);
            var response = BridgeClientJsonCodec.DeserializeTurnStartV2Response(responseJson, request);
            CompletePendingTurnStart(
                pending,
                response.ThreadId,
                response.TurnId,
                response.AcceptedContextV2Sha256);
            completed = true;
            return response;
        }
        finally
        {
            if (!completed)
            {
                AbandonPendingTurnStart(pending);
            }
        }
    }

    private PendingTurnIdentity RegisterPendingTurnStart(
        string requestId,
        string threadId,
        string contextSha256)
    {
        lock (_sync)
        {
            EnsureOnline();
            if (_pendingTurnStarts.ContainsKey(requestId))
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ResultIdentityMismatch,
                    "ClientTurnId已绑定到另一个待启动回合。");
            }
            foreach (var active in _activeTurns.Values)
            {
                if (string.Equals(active.RequestId, requestId, StringComparison.Ordinal))
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.ResultIdentityMismatch,
                        "ClientTurnId已被活动回合使用。");
                }
            }
            if (_activeTurns.Count + _pendingTurnStarts.Count >= MaximumTrackedTurns)
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.Busy,
                    "Agent Bridge活动回合数量已达到安全上限。");
            }

            var pending = new PendingTurnIdentity(requestId, threadId, contextSha256);
            _pendingTurnStarts.Add(requestId, pending);
            return pending;
        }
    }

    private void CompletePendingTurnStart(
        PendingTurnIdentity pending,
        string threadId,
        string turnId,
        string contextSha256)
    {
        lock (_sync)
        {
            EnsureOnline();
            PendingTurnIdentity current;
            if (!_pendingTurnStarts.TryGetValue(pending.RequestId, out current!)
                || !ReferenceEquals(current, pending)
                || !pending.Matches(threadId, contextSha256))
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ResultIdentityMismatch,
                    "Agent启动响应未绑定到当前待启动回合。");
            }

            var provisionallyBound = !string.IsNullOrEmpty(pending.ProviderTurnId);
            if (!pending.TryBindProviderTurn(turnId))
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ResultIdentityMismatch,
                    "Agent启动响应与早到反向查询的TurnId不一致。");
            }

            TurnIdentity active;
            if (_activeTurns.TryGetValue(turnId, out active!))
            {
                if (!provisionallyBound || !active.Matches(pending))
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.ResultIdentityMismatch,
                        "Agent返回了重复的TurnId。");
                }
            }
            else
            {
                if (provisionallyBound)
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.ResultIdentityMismatch,
                        "早到反向查询绑定的活动回合已终止。");
                }
                _activeTurns.Add(
                    turnId,
                    new TurnIdentity(
                        pending.RequestId,
                        pending.ThreadId,
                        pending.ContextSha256));
            }

            _pendingTurnStarts.Remove(pending.RequestId);
        }
    }

    private void AbandonPendingTurnStart(PendingTurnIdentity pending)
    {
        lock (_sync)
        {
            PendingTurnIdentity current;
            if (!_pendingTurnStarts.TryGetValue(pending.RequestId, out current!)
                || !ReferenceEquals(current, pending))
            {
                return;
            }

            _pendingTurnStarts.Remove(pending.RequestId);
            if (!string.IsNullOrEmpty(pending.ProviderTurnId))
            {
                TurnIdentity active;
                if (_activeTurns.TryGetValue(pending.ProviderTurnId, out active!)
                    && active.Matches(pending))
                {
                    _activeTurns.Remove(pending.ProviderTurnId);
                }
            }
        }
    }

    public async Task InterruptTurnAsync(
        AgentTurnInterruptRequest request,
        CancellationToken cancellationToken)
    {
        var bodyJson = BridgeClientJsonCodec.SerializeTurnInterruptRequest(request);
        lock (_sync)
        {
            EnsureOnline();
            TurnIdentity identity;
            if (!_activeTurns.TryGetValue(request.TurnId, out identity!)
                || !string.Equals(identity.ThreadId, request.ThreadId, StringComparison.Ordinal))
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.TurnNotFound,
                    "待中断turn不是当前连接的活动turn。");
            }
        }

        var responseJson = await RequestAsync(
                AgentBridgeMethods.InterruptTurn,
                bodyJson,
                cancellationToken)
            .ConfigureAwait(false);
        BridgeClientJsonCodec.ValidateNullResponse(responseJson);
    }

    public async Task ResolveApprovalAsync(
        AgentApprovalResolveRequest request,
        CancellationToken cancellationToken)
    {
        var bodyJson = BridgeClientJsonCodec.SerializeApprovalResolveRequest(request);
        lock (_sync)
        {
            EnsureOnline();
            TurnIdentity identity;
            if (!_activeTurns.TryGetValue(request.TurnId, out identity!)
                || !string.Equals(identity.ThreadId, request.ThreadId, StringComparison.Ordinal))
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.TurnNotFound,
                    "审批请求未绑定到当前活动turn。");
            }
        }

        var responseJson = await RequestAsync(
                AgentBridgeMethods.ResolveApproval,
                bodyJson,
                cancellationToken)
            .ConfigureAwait(false);
        BridgeClientJsonCodec.ValidateNullResponse(responseJson);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeSignaled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            lock (_sync)
            {
                _state = ClientState.Disposed;
            }

            DisposeSecurityMaterials();
            _sendGate.Dispose();
            _drawingQuerySlots.Dispose();
        }
        catch
        {
            Volatile.Write(ref _disposeSignaled, 0);
            throw;
        }
    }

    private async Task<string> RequestAsync(
        string method,
        string bodyJson,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>();
        var requestSent = false;
        lock (_sync)
        {
            EnsureOnline();
            _pendingRequests.Add(requestId, completion);
        }

        try
        {
            await SendRequestEnvelopeAsync(requestId, method, bodyJson, cancellationToken)
                .ConfigureAwait(false);
            requestSent = true;

            var timeout = Task.Delay(_requestTimeout);
            var cancellation = CreateCancellationTask(cancellationToken);
            var completed = await Task.WhenAny(completion.Task, timeout, cancellation.Task)
                .ConfigureAwait(false);
            cancellation.Dispose();

            if (completed == cancellation.Task)
            {
                if (requestSent)
                {
                    FailConnection(new AgentBridgeClientException(
                        AgentBridgeErrorCodes.ConnectionLost,
                        "Agent Bridge请求在发送后被取消；连接已按fail-closed终止。"));
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            if (completed == timeout)
            {
                var exception = new AgentBridgeClientException(
                    AgentBridgeErrorCodes.Timeout,
                    "Agent Bridge请求超时；连接已按fail-closed终止。");
                FailConnection(exception);
                throw exception;
            }

            var response = await completion.Task.ConfigureAwait(false);
            if (!string.IsNullOrEmpty(response.ErrorCode))
            {
                throw new AgentBridgeRemoteException(response.ErrorCode, response.ErrorMessage);
            }

            return response.BodyJson;
        }
        finally
        {
            lock (_sync)
            {
                _pendingRequests.Remove(requestId);
            }
        }
    }

    private async Task SendRequestEnvelopeAsync(
        string requestId,
        string method,
        string bodyJson,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NamedPipeClientStream pipe;
            IpcEnvelopeAuthenticator authenticator;
            long sequence;
            lock (_sync)
            {
                EnsureOnline();
                pipe = _pipe!;
                authenticator = _authenticator!;
                sequence = checked(_outgoingSequence + 1);
            }

            var envelope = new IpcEnvelope
            {
                MessageId = requestId,
                CorrelationId = string.Empty,
                SessionId = _sessionId,
                Sequence = sequence,
                MessageType = RequestMessageType,
                PayloadJson = BridgeClientJsonCodec.SerializeRequestPayload(method, bodyJson),
                Nonce = CreateNonce(),
            };
            envelope.Mac = authenticator.Sign(envelope);

            try
            {
                await BridgeClientFrameCodec.WriteAsync(
                        pipe,
                        envelope,
                        _maximumFrameBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                FailConnection(new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ConnectionLost,
                    "Agent Bridge发送被取消；连接已按fail-closed终止。",
                    exception));
                throw;
            }
            catch (Exception exception)
            {
                var terminal = NormalizeTerminalError(exception, "connection_lost");
                FailConnection(terminal);
                throw terminal;
            }

            lock (_sync)
            {
                if (_state != ClientState.Online)
                {
                    throw CreateStateException("Agent Bridge连接已终止。");
                }

                _outgoingSequence = sequence;
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task SendResponseEnvelopeAsync(
        string correlationId,
        string bodyJson,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NamedPipeClientStream pipe;
            IpcEnvelopeAuthenticator authenticator;
            long sequence;
            lock (_sync)
            {
                EnsureOnline();
                pipe = _pipe!;
                authenticator = _authenticator!;
                sequence = checked(_outgoingSequence + 1);
            }

            var envelope = new IpcEnvelope
            {
                MessageId = Guid.NewGuid().ToString("N"),
                CorrelationId = correlationId,
                SessionId = _sessionId,
                Sequence = sequence,
                MessageType = ResponseMessageType,
                PayloadJson = BridgeClientJsonCodec.SerializeResponsePayload(
                    bodyJson,
                    errorCode,
                    errorMessage),
                Nonce = CreateNonce(),
            };
            envelope.Mac = authenticator.Sign(envelope);

            try
            {
                await BridgeClientFrameCodec.WriteAsync(
                        pipe,
                        envelope,
                        _maximumFrameBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var terminal = NormalizeTerminalError(exception, AgentBridgeErrorCodes.ConnectionLost);
                FailConnection(terminal);
                throw terminal;
            }

            lock (_sync)
            {
                if (_state != ClientState.Online)
                {
                    throw CreateStateException("Agent Bridge连接已终止。");
                }

                _outgoingSequence = sequence;
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var envelope = await BridgeClientFrameCodec.ReadAsync(
                        pipe,
                        _maximumFrameBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (envelope is null)
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.ConnectionLost,
                        "Agent Bridge连接被远端关闭。");
                }

                IpcValidationCode validation;
                lock (_sync)
                {
                    if (_state != ClientState.Online || _incomingGuard is null)
                    {
                        return;
                    }

                    validation = _incomingGuard.ValidateAndAccept(envelope);
                }

                if (validation != IpcValidationCode.Accepted)
                {
                    throw new AgentBridgeAuthenticationException(validation);
                }

                if (string.Equals(envelope.MessageType, RequestMessageType, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(envelope.CorrelationId))
                    {
                        throw new AgentBridgeClientException(
                            AgentBridgeErrorCodes.RequestInvalid,
                            "Agent Bridge反向请求不得携带CorrelationId。");
                    }

                    var reverseRequest = BridgeClientJsonCodec.DeserializeRequestPayload(
                        envelope.PayloadJson);
                    if (!string.Equals(
                        reverseRequest.Method,
                        AgentBridgeMethods.QueryDrawing,
                        StringComparison.Ordinal))
                    {
                        throw new AgentBridgeClientException(
                            AgentBridgeErrorCodes.RequestInvalid,
                            "Agent Bridge收到未列入白名单的反向请求方法。");
                    }

                    await StartDrawingQueryHandlerAsync(
                            envelope.MessageId,
                            reverseRequest.BodyJson,
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(envelope.MessageType, CancelMessageType, StringComparison.Ordinal))
                {
                    HandleDrawingQueryCancellation(envelope);
                    continue;
                }

                if (string.Equals(
                    envelope.MessageType,
                    NotificationMessageType,
                    StringComparison.Ordinal))
                {
                    var notification = BridgeClientJsonCodec.DeserializeRequestPayload(
                        envelope.PayloadJson);
                    if (!string.Equals(
                        notification.Method,
                        AgentBridgeMethods.EventNotification,
                        StringComparison.Ordinal))
                    {
                        throw new AgentBridgeClientException(
                            "request_invalid",
                            "Agent Bridge收到未知通知方法。");
                    }

                    var bridgeEvent = BridgeClientJsonCodec.DeserializeAgentEvent(
                        notification.BodyJson);
                    if (ValidateAndTrackEvent(bridgeEvent))
                    {
                        var handler = EventReceived;
                        if (handler is not null)
                        {
                            handler(this, new AgentBridgeEventReceivedEventArgs(bridgeEvent));
                        }
                    }

                    continue;
                }

                if (!string.Equals(envelope.MessageType, ResponseMessageType, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(envelope.CorrelationId))
                {
                    throw new AgentBridgeClientException(
                        "request_invalid",
                        "Agent Bridge收到非预期消息类型或缺少关联身份。");
                }

                var response = BridgeClientJsonCodec.DeserializeResponsePayload(envelope.PayloadJson);
                TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>? completion;
                lock (_sync)
                {
                    if (!_pendingRequests.TryGetValue(envelope.CorrelationId, out completion))
                    {
                        throw new AgentBridgeClientException(
                            AgentBridgeErrorCodes.ResultIdentityMismatch,
                            "Agent Bridge响应未绑定到活动请求。");
                    }
                }

                completion.TrySetResult(response);
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
            FailConnection(NormalizeTerminalError(exception, "connection_lost"));
        }
    }

    private async Task StartDrawingQueryHandlerAsync(
        string bridgeRequestId,
        string bodyJson,
        CancellationToken connectionCancellation)
    {
        if (string.IsNullOrWhiteSpace(bridgeRequestId))
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.RequestInvalid,
                "Agent Bridge反向请求缺少MessageId。");
        }

        if (!_drawingQuerySlots.Wait(0))
        {
            await SendResponseEnvelopeAsync(
                    bridgeRequestId,
                    "null",
                    AgentBridgeErrorCodes.Busy,
                    "AutoCAD只读图纸查询当前繁忙。",
                    connectionCancellation)
                .ConfigureAwait(false);
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(connectionCancellation);
        var activeQuery = new ActiveDrawingQuery(cancellation);
        lock (_sync)
        {
            EnsureOnline();
            if (_activeDrawingQueries.ContainsKey(bridgeRequestId))
            {
                cancellation.Dispose();
                _drawingQuerySlots.Release();
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ResultIdentityMismatch,
                    "Agent Bridge反向请求MessageId重复。");
            }

            _activeDrawingQueries.Add(bridgeRequestId, activeQuery);
        }

        _ = RunDrawingQueryHandlerAsync(
            bridgeRequestId,
            bodyJson,
            activeQuery,
            connectionCancellation);
    }

    private async Task RunDrawingQueryHandlerAsync(
        string bridgeRequestId,
        string bodyJson,
        ActiveDrawingQuery activeQuery,
        CancellationToken connectionCancellation)
    {
        await Task.Yield();
        var responseBody = "null";
        var errorCode = string.Empty;
        var errorMessage = string.Empty;
        try
        {
            var request = BridgeClientJsonCodec.DeserializeDrawingQueryRequest(bodyJson);
            EnsureDrawingQueryTurnActive(request);
            if (_drawingQueryHandler is null)
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.DrawingQueryUnavailable,
                    "AutoCAD Host未注册只读图纸查询处理器。");
            }

            var response = await _drawingQueryHandler(request, activeQuery.Cancellation.Token)
                .ConfigureAwait(false);
            activeQuery.Cancellation.Token.ThrowIfCancellationRequested();
            EnsureDrawingQueryTurnActive(request);
            responseBody = BridgeClientJsonCodec.SerializeDrawingQueryResponse(request, response);
        }
        catch (OperationCanceledException) when (activeQuery.Cancellation.IsCancellationRequested)
        {
            errorCode = AgentBridgeErrorCodes.RequestCancelled;
            errorMessage = "AutoCAD只读图纸查询已取消。";
        }
        catch (AgentBridgeClientException exception)
        {
            errorCode = exception.Code;
            errorMessage = exception.Message;
        }
        catch
        {
            errorCode = AgentBridgeErrorCodes.InternalError;
            errorMessage = "AutoCAD只读图纸查询处理失败。";
        }

        try
        {
            if (!connectionCancellation.IsCancellationRequested)
            {
                await SendResponseEnvelopeAsync(
                        bridgeRequestId,
                        responseBody,
                        errorCode,
                        errorMessage,
                        connectionCancellation)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (connectionCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            // SendResponseEnvelopeAsync already transitions the connection to fail-closed.
        }
        finally
        {
            lock (_sync)
            {
                _activeDrawingQueries.Remove(bridgeRequestId);
            }

            activeQuery.Cancellation.Dispose();
            _drawingQuerySlots.Release();
            activeQuery.Completion.TrySetResult(true);
        }
    }

    private void EnsureDrawingQueryTurnActive(AgentDrawingQueryRequest request)
    {
        lock (_sync)
        {
            EnsureOnline();
            TurnIdentity identity;
            if (_activeTurns.TryGetValue(request.TurnId, out identity!))
            {
                if (identity.Matches(request.RequestId, request.ThreadId))
                {
                    return;
                }

                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ResultIdentityMismatch,
                    "反向整图查询未绑定到当前活动turn。");
            }

            PendingTurnIdentity pending;
            if (!_pendingTurnStarts.TryGetValue(request.RequestId, out pending!)
                || !string.Equals(pending.ThreadId, request.ThreadId, StringComparison.Ordinal)
                || !string.IsNullOrEmpty(pending.ProviderTurnId)
                || !pending.TryBindProviderTurn(request.TurnId))
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ResultIdentityMismatch,
                    "反向整图查询未绑定到当前活动turn。");
            }

            _activeTurns.Add(
                request.TurnId,
                new TurnIdentity(
                    pending.RequestId,
                    pending.ThreadId,
                    pending.ContextSha256));
        }
    }

    private void HandleDrawingQueryCancellation(IpcEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.CorrelationId))
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.RequestInvalid,
                "Agent Bridge取消消息缺少CorrelationId。");
        }

        _ = BridgeClientJsonCodec.DeserializeCancelReason(envelope.PayloadJson);
        ActiveDrawingQuery? activeQuery;
        lock (_sync)
        {
            _activeDrawingQueries.TryGetValue(envelope.CorrelationId, out activeQuery);
        }

        if (activeQuery is not null)
        {
            TryCancel(activeQuery.Cancellation);
        }
    }

    private bool ValidateAndTrackEvent(AgentBridgeEvent bridgeEvent)
    {
        lock (_sync)
        {
            if (bridgeEvent.Sequence <= 0
                || _lastEventSequence == long.MaxValue
                || bridgeEvent.Sequence != _lastEventSequence + 1)
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ReplayRejected,
                    "Agent事件sequence重复、乱序或跳号。");
            }

            if (!string.IsNullOrEmpty(bridgeEvent.TurnId))
            {
                TurnIdentity identity;
                if (!_activeTurns.TryGetValue(bridgeEvent.TurnId, out identity!)
                    || AgentBridgeContractValidator.ValidateEventIdentity(
                        bridgeEvent,
                        identity.ThreadId,
                        bridgeEvent.TurnId,
                        identity.ContextSha256).Length != 0)
                {
                    throw new AgentBridgeClientException(
                        AgentBridgeErrorCodes.ResultIdentityMismatch,
                        "Agent事件未绑定到活动thread/turn/context。");
                }
            }

            _lastEventSequence = bridgeEvent.Sequence;
            if (!_seenEventIds.Add(bridgeEvent.EventId))
            {
                return false;
            }

            if (_seenEventIds.Count > 4096)
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.Busy,
                    "Agent事件身份历史超过安全上限。");
            }
            if (IsTerminalTurnEvent(bridgeEvent.Kind))
            {
                _activeTurns.Remove(bridgeEvent.TurnId);
            }


            return true;
        }
    }

    private static bool IsTerminalTurnEvent(string kind)
    {
        return string.Equals(kind, AgentBridgeEventKinds.TurnCompleted, StringComparison.Ordinal)
            || string.Equals(kind, AgentBridgeEventKinds.TurnFailed, StringComparison.Ordinal)
            || string.Equals(kind, AgentBridgeEventKinds.TurnCancelled, StringComparison.Ordinal);
    }

    private async Task ConnectAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        var timeoutMilliseconds = checked((int)Math.Ceiling(_connectTimeout.TotalMilliseconds));
        var connectTask = Task.Factory.StartNew(
            () => pipe.Connect(timeoutMilliseconds),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var cancellation = CreateCancellationTask(cancellationToken);
        var completed = await Task.WhenAny(connectTask, cancellation.Task).ConfigureAwait(false);
        cancellation.Dispose();
        if (completed == cancellation.Task)
        {
            SafeDispose(pipe);
            cancellationToken.ThrowIfCancellationRequested();
        }

        await connectTask.ConfigureAwait(false);
    }

    private void FailConnection(AgentBridgeClientException exception)
    {
        CancellationTokenSource? lifetime;
        NamedPipeClientStream? pipe;
        List<TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>> pending;
        lock (_sync)
        {
            if (_state == ClientState.Faulted
                || _state == ClientState.Stopped
                || _state == ClientState.Disposed)
            {
                return;
            }

            _terminalError = exception;
            _state = ClientState.Faulted;
            lifetime = _lifetime;
            pipe = _pipe;
            pending = _pendingRequests.Values.ToList();
            _pendingRequests.Clear();
            _activeTurns.Clear();
            _pendingTurnStarts.Clear();
            _seenEventIds.Clear();
            _lastEventSequence = 0;
        }

        TryCancel(lifetime);
        SafeDispose(pipe);
        foreach (var completion in pending)
        {
            completion.TrySetException(exception);
        }

        DisposeSecurityMaterials();
        QueueConnectionFaulted(exception);
    }

    private void EnsureOnline()
    {
        if (_state != ClientState.Online)
        {
            throw CreateStateException("Agent Bridge Client当前不在线。");
        }
    }

    private AgentBridgeClientException CreateStateException(string message)
    {
        return _terminalError
            ?? new AgentBridgeClientException(AgentBridgeErrorCodes.Offline, message);
    }

    private void DisposeSecurityMaterials()
    {
        IpcEnvelopeAuthenticator? authenticator;
        IpcSessionGuard? guard;
        CancellationTokenSource? lifetime;
        lock (_sync)
        {
            authenticator = _authenticator;
            guard = _incomingGuard;
            lifetime = _lifetime;
            _authenticator = null;
            _incomingGuard = null;
            _lifetime = null;
            _pipe = null;
        }

        if (authenticator is not null)
        {
            authenticator.Dispose();
        }

        if (guard is not null)
        {
            guard.Dispose();
        }

        if (lifetime is not null)
        {
            lifetime.Dispose();
        }
    }

    private static AgentBridgeClientException NormalizeTerminalError(
        Exception exception,
        string defaultCode)
    {
        if (exception is AgentBridgeClientException bridgeException)
        {
            return bridgeException;
        }

        if (exception is TimeoutException)
        {
            return new AgentBridgeClientException(
                AgentBridgeErrorCodes.Timeout,
                "Agent Bridge操作超时；连接已按fail-closed终止。",
                exception);
        }

        return new AgentBridgeClientException(
            defaultCode,
            "Agent Bridge连接失败；未尝试未认证回退或自动重连。",
            exception);
    }

    private void QueueConnectionFaulted(AgentBridgeClientException exception)
    {
        ThreadPool.QueueUserWorkItem(_ => RaiseConnectionFaulted(exception));
    }

    private void RaiseConnectionFaulted(AgentBridgeClientException exception)
    {
        var handler = ConnectionFaulted;
        if (handler is null)
        {
            return;
        }

        var eventArgs = new AgentBridgeConnectionFaultedEventArgs(exception);
        foreach (EventHandler<AgentBridgeConnectionFaultedEventArgs> subscriber
                 in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, eventArgs);
            }
            catch
            {
            }
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

        var cancellation = CreateCancellationTask(cancellationToken);
        try
        {
            var completed = await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false);
            if (completed == cancellation.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            await task.ConfigureAwait(false);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static string CreateNonce()
    {
        var bytes = new byte[16];
        using (var random = RandomNumberGenerator.Create())
        {
            random.GetBytes(bytes);
        }

        try
        {
            const string upperHex = "0123456789ABCDEF";
            var characters = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                var value = bytes[index];
                characters[index * 2] = upperHex[value >> 4];
                characters[(index * 2) + 1] = upperHex[value & 0x0F];
            }

            return new string(characters);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static CancellationRegistration CreateCancellationTask(
        CancellationToken cancellationToken)
    {
        return new CancellationRegistration(cancellationToken);
    }

    private static void ValidatePipeName(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > 200)
        {
            throw new ArgumentException("命名管道名称为空或超过安全长度。", nameof(pipeName));
        }

        if (pipeName.IndexOfAny(new[] { '\\', '/' }) >= 0)
        {
            throw new ArgumentException("命名管道名称不能包含路径分隔符。", nameof(pipeName));
        }
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero || timeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void SafeDispose(IDisposable? disposable)
    {
        if (disposable is null)
        {
            return;
        }

        try
        {
            disposable.Dispose();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
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
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed class CancellationRegistration : IDisposable
    {
        private readonly CancellationTokenRegistration _registration;
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();

        public CancellationRegistration(CancellationToken cancellationToken)
        {
            _registration = cancellationToken.Register(
                state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                _completion);
        }

        public Task Task => _completion.Task;

        public void Dispose()
        {
            _registration.Dispose();
        }
    }

    private sealed class TurnIdentity
    {
        public TurnIdentity(string requestId, string threadId, string contextSha256)
        {
            RequestId = requestId;
            ThreadId = threadId;
            ContextSha256 = contextSha256;
        }

        public string RequestId { get; }

        public string ThreadId { get; }

        public string ContextSha256 { get; }

        public bool Matches(string requestId, string threadId)
        {
            return string.Equals(RequestId, requestId, StringComparison.Ordinal)
                && string.Equals(ThreadId, threadId, StringComparison.Ordinal);
        }

        public bool Matches(PendingTurnIdentity pending)
        {
            return pending != null
                && Matches(pending.RequestId, pending.ThreadId)
                && string.Equals(
                    ContextSha256,
                    pending.ContextSha256,
                    StringComparison.Ordinal);
        }
    }

    private sealed class PendingTurnIdentity
    {
        public PendingTurnIdentity(string requestId, string threadId, string contextSha256)
        {
            RequestId = requestId;
            ThreadId = threadId;
            ContextSha256 = contextSha256;
        }

        public string RequestId { get; }

        public string ThreadId { get; }

        public string ContextSha256 { get; }

        public string ProviderTurnId { get; private set; } = string.Empty;

        public bool TryBindProviderTurn(string turnId)
        {
            if (string.IsNullOrWhiteSpace(turnId))
            {
                return false;
            }
            if (string.IsNullOrEmpty(ProviderTurnId))
            {
                ProviderTurnId = turnId;
                return true;
            }
            return string.Equals(ProviderTurnId, turnId, StringComparison.Ordinal);
        }

        public bool Matches(string threadId, string contextSha256)
        {
            return string.Equals(ThreadId, threadId, StringComparison.Ordinal)
                && string.Equals(ContextSha256, contextSha256, StringComparison.Ordinal);
        }
    }

    private sealed class ActiveDrawingQuery
    {
        public ActiveDrawingQuery(CancellationTokenSource cancellation)
        {
            Cancellation = cancellation;
            Completion = new TaskCompletionSource<bool>();
        }

        public CancellationTokenSource Cancellation { get; }

        public TaskCompletionSource<bool> Completion { get; }
    }

    private enum ClientState
    {
        Created,
        Starting,
        Online,
        Faulted,
        Stopped,
        Disposed,
    }
}
