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
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(1);

    private readonly object _sync = new object();
    private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
    private readonly string _pipeName;
    private readonly string _sessionId;
    private readonly byte[] _sessionSecret;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _requestTimeout;
    private readonly int _maximumFrameBytes;
    private readonly Dictionary<string, TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>>
        _pendingRequests =
            new Dictionary<string, TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>>(
                StringComparer.Ordinal);
    private readonly Dictionary<string, TurnIdentity> _activeTurns =
        new Dictionary<string, TurnIdentity>(StringComparer.Ordinal);
    private readonly HashSet<string> _seenEventIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly TaskCompletionSource<bool> _stopCompletion =
        new TaskCompletionSource<bool>();

    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _lifetime;
    private IpcEnvelopeAuthenticator? _authenticator;
    private IpcSessionGuard? _incomingGuard;
    private Task? _receiveTask;
    private AgentBridgeClientException? _terminalError;
    private long _outgoingSequence;
    private long _lastEventSequence;
    private int _disposeSignaled;
    private ClientState _state;

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
        if (options.MaximumFrameBytes <= 0
            || options.MaximumFrameBytes > ProtocolConstants.MaximumMessageBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaximumFrameBytes));
        }

        _pipeName = options.PipeName;
        _sessionId = options.SessionId;
        _sessionSecret = (byte[])options.SessionSecret.Clone();
        _connectTimeout = options.ConnectTimeout;
        _requestTimeout = options.RequestTimeout;
        _maximumFrameBytes = options.MaximumFrameBytes;
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
            var authenticator = new IpcEnvelopeAuthenticator(_sessionSecret);
            var incomingGuard = new IpcSessionGuard(_sessionId, _sessionSecret);
            lock (_sync)
            {
                if (_state != ClientState.Starting)
                {
                    lifetime.Dispose();
                    authenticator.Dispose();
                    incomingGuard.Dispose();
                    throw CreateStateException("Agent Bridge Client启动期间已终止。");
                }

                _lifetime = lifetime;
                _authenticator = authenticator;
                _incomingGuard = incomingGuard;
                Array.Clear(_sessionSecret, 0, _sessionSecret.Length);
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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource? lifetime;
        NamedPipeClientStream? pipe;
        Task? receiveTask;
        List<TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>> pending;
        Task stopTask;
        var ownsStop = false;
        lock (_sync)
        {
            if (_state == ClientState.Disposed)
            {
                return;
            }

            stopTask = _stopCompletion.Task;
            if (_state == ClientState.Stopped)
            {
                lifetime = null;
                pipe = null;
                receiveTask = null;
                pending = new List<TaskCompletionSource<BridgeClientJsonCodec.ResponsePayloadValue>>();
            }
            else
            {
                ownsStop = true;
                _state = ClientState.Stopped;
                lifetime = _lifetime;
                pipe = _pipe;
                receiveTask = _receiveTask;
                pending = _pendingRequests.Values.ToList();
                _pendingRequests.Clear();
                _activeTurns.Clear();
                _seenEventIds.Clear();
                _lastEventSequence = 0;
            }
        }

        if (!ownsStop)
        {
            await AwaitWithCancellationAsync(stopTask, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            TryCancel(lifetime);
            SafeDispose(pipe);
            var stopped = new AgentBridgeClientException(
                AgentBridgeErrorCodes.ConnectionLost,
                "Agent Bridge连接已停止。");
            foreach (var completion in pending)
            {
                completion.TrySetException(stopped);
            }

            await _sendGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            _sendGate.Release();

            if (receiveTask is not null)
            {
                await receiveTask.ConfigureAwait(false);
            }
        }
        finally
        {
            DisposeSecurityMaterials();
            _stopCompletion.TrySetResult(true);
        }
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
        var responseJson = await RequestAsync(
                AgentBridgeMethods.StartTurn,
                bodyJson,
                cancellationToken)
            .ConfigureAwait(false);
        var response = BridgeClientJsonCodec.DeserializeTurnStartResponse(responseJson, request);
        lock (_sync)
        {
            EnsureOnline();
            if (_activeTurns.ContainsKey(response.TurnId))
            {
                throw new AgentBridgeClientException(
                    AgentBridgeErrorCodes.ResultIdentityMismatch,
                    "Agent返回了重复的TurnId。");
            }

            _activeTurns.Add(
                response.TurnId,
                new TurnIdentity(response.ThreadId, response.AcceptedContextSha256));
        }

        return response;
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
        if (Interlocked.Exchange(ref _disposeSignaled, 1) != 0)
        {
            return;
        }

        var stopCompleted = false;
        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            stopCompleted = true;
        }
        finally
        {
            lock (_sync)
            {
                _state = ClientState.Disposed;
            }

            DisposeSecurityMaterials();
            Array.Clear(_sessionSecret, 0, _sessionSecret.Length);
            if (stopCompleted)
            {
                _sendGate.Dispose();
            }
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
            Array.Clear(_sessionSecret, 0, _sessionSecret.Length);
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
        public TurnIdentity(string threadId, string contextSha256)
        {
            ThreadId = threadId;
            ContextSha256 = contextSha256;
        }

        public string ThreadId { get; }

        public string ContextSha256 { get; }
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
