using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;
using Codex.AutoCAD.Bridge;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

var specs = new (string Name, Func<Task> Run)[]
{
    ("当前用户命名管道可完成请求响应", RequestResponseWorks),
    ("通知可单向投递", NotificationWorks),
    ("取消消息会取消远端请求", CancellationPropagates),
    ("远端错误被结构化返回", RemoteErrorPropagates),
    ("坏MAC被拒绝", BadMacIsRejected),
    ("重复序号被拒绝", ReplayedSequenceIsRejected),
    ("重复nonce被拒绝", ReplayedNonceIsRejected),
    ("乱序消息被拒绝", OutOfOrderSequenceIsRejected),
    ("超大入站帧在分配前被拒绝", OversizedIncomingFrameIsRejected),
    ("超大出站帧被拒绝且不破坏序号", OversizedOutgoingFrameIsRejectedWithoutSequenceGap),
    ("非32字节会话密钥被拒绝", InvalidSecretLengthIsRejected),
    ("超过32层JSON被拒绝且连接可复用", ExcessiveJsonDepthIsRejected),
    ("出站pending上限原子拒绝且释放后恢复", PendingRequestLimitRejectsAndRecovers),
    ("入站active请求洪泛会fail-closed断开", ActiveRequestFloodAbortsConnection),
    ("通知handler洪泛会fail-closed断开", HandlerFloodAbortsConnection),
    ("内存双工生命周期：同步阻塞handler不会阻塞接收循环", SynchronouslyBlockingHandlerDoesNotBlockReceiveLoop),
    ("内存双工生命周期：忽略取消handler不阻塞并发Dispose", NonCooperativeHandlerDoesNotBlockConcurrentDispose),
    ("内存双工生命周期：迟到handler fault被观察且可诊断", LateHandlerFaultIsObservedAndDiagnosable),
    ("内存双工生命周期：非协作stream和sendGate关闭有界且延迟清密钥", NonCooperativeStreamAndSendGateUseBoundedSafeCleanup),
    ("容量槽位完成后可反复复用", CapacitySlotsRecoverAfterCompletion),
    ("出站通知队列上限原子拒绝且释放后恢复", PendingNotificationLimitRejectsAndRecovers),
    ("自定义帧上限拒绝超限且不破坏连接", ConfiguredFrameLimitRejectsAndRecovers),
    ("自定义入站帧上限在分配前拒绝", ConfiguredIncomingFrameLimitRejectsBeforeAllocation),
    ("通知handler异常会终止连接", NotificationHandlerFailureAbortsConnection),
    ("非法容量或关闭超时配置被拒绝", InvalidCapacityOptionsAreRejected),
    ("正常EOF会终止连接", NormalEofTerminatesConnection),
    ("部分写入失败会中止连接", PartialWriteFailureAbortsConnection),
    ("认证后协议异常会中止连接", AuthenticatedProtocolErrorAbortsConnection),
    ("IPC密钥副本在释放时清零", IpcSecretsAreZeroedOnDispose)
};

if (args.Length > 1)
{
    throw new ArgumentException("Bridge Specs 最多接受一个名称筛选参数。");
}

var selectedSpecs = args.Length == 0
    ? specs
    : specs.Where(spec => spec.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)).ToArray();
if (selectedSpecs.Length == 0)
{
    throw new ArgumentException($"没有匹配的 Bridge Spec：{args[0]}。");
}

var failed = 0;
foreach (var spec in selectedSpecs)
{
    try
    {
        await spec.Run();
        Console.WriteLine("PASS " + spec.Name);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + spec.Name + ": " + exception);
    }
}

Console.WriteLine($"{selectedSpecs.Length - failed}/{selectedSpecs.Length} specs passed");
return failed == 0 ? 0 : 1;

static async Task RequestResponseWorks()
{
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start((request, _) =>
    {
        Equal("cad.selection.read", request.Method);
        Equal("{\"handles\":[\"1A\"]}", request.BodyJson);
        return ValueTask.FromResult<string?>("{\"count\":1}");
    });
    client.Start();

    var response = await client.RequestAsync("cad.selection.read", "{\"handles\":[\"1A\"]}")
        .WaitAsync(TimeSpan.FromSeconds(5));
    Equal("{\"count\":1}", response);
}

static async Task NotificationWorks()
{
    var received = new TaskCompletionSource<BridgeNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (notification, _) =>
    {
        received.TrySetResult(notification);
        return ValueTask.CompletedTask;
    });
    client.Start();

    await client.NotifyAsync("cad.selection.changed", "{\"count\":2}");
    var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Equal("cad.selection.changed", notification.Method);
    Equal("{\"count\":2}", notification.BodyJson);
}

static async Task CancellationPropagates()
{
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(async (_, cancellationToken) =>
    {
        started.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled.TrySetResult();
            throw;
        }

        return "null";
    });
    client.Start();

    using var requestCancellation = new CancellationTokenSource();
    var request = client.RequestAsync("cad.long_operation", "{}", requestCancellation.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    requestCancellation.Cancel();

    await ThrowsAsync<OperationCanceledException>(() => request);
    await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task RemoteErrorPropagates()
{
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start((_, _) => throw new InvalidOperationException("模拟处理失败"));
    client.Start();

    var exception = await ThrowsAsync<BridgeRemoteException>(
        () => client.RequestAsync("cad.fail", "{}"));
    Equal("handler_error", exception.Code);
    Equal("远端请求处理失败。", exception.Message);
    False(exception.Message.Contains("模拟处理失败", StringComparison.Ordinal));
}

static Task BadMacIsRejected()
{
    return AssertRawEnvelopeRejected(
        (secret, sessionId) =>
        {
            var envelope = CreateEnvelope(secret, sessionId, 1, "nonce-1");
            envelope.Mac = new string('0', 64);
            return [envelope];
        },
        IpcValidationCode.InvalidMac);
}

static Task ReplayedSequenceIsRejected()
{
    return AssertRawEnvelopeRejected(
        (secret, sessionId) =>
        [
            CreateEnvelope(secret, sessionId, 1, "nonce-1"),
            CreateEnvelope(secret, sessionId, 1, "nonce-2")
        ],
        IpcValidationCode.InvalidSequence);
}

static Task ReplayedNonceIsRejected()
{
    return AssertRawEnvelopeRejected(
        (secret, sessionId) =>
        [
            CreateEnvelope(secret, sessionId, 1, "nonce-1"),
            CreateEnvelope(secret, sessionId, 2, "nonce-1")
        ],
        IpcValidationCode.ReplayedNonce);
}

static Task OutOfOrderSequenceIsRejected()
{
    return AssertRawEnvelopeRejected(
        (secret, sessionId) => [CreateEnvelope(secret, sessionId, 2, "nonce-2")],
        IpcValidationCode.InvalidSequence);
}

static async Task OversizedIncomingFrameIsRejected()
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var accept = NamedPipeBridge.AcceptOneAsync(pipeName, sessionId, secret);
    await using var rawClient = CreateRawClient(pipeName);
    await rawClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await using var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    server.Start();

    var prefix = new byte[sizeof(int)];
    BinaryPrimitives.WriteInt32LittleEndian(prefix, ProtocolConstants.MaximumMessageBytes + 1);
    await rawClient.WriteAsync(prefix);
    await rawClient.FlushAsync();

    await ThrowsAsync<BridgeProtocolException>(() => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
}

static async Task OversizedOutgoingFrameIsRejectedWithoutSequenceGap()
{
    var notification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (_, _) =>
    {
        notification.TrySetResult();
        return ValueTask.CompletedTask;
    });
    client.Start();

    var oversizedJson = "\"" + new string('x', ProtocolConstants.MaximumMessageBytes) + "\"";
    await ThrowsAsync<BridgeProtocolException>(() => client.NotifyAsync("cad.too_large", oversizedJson));

    await client.NotifyAsync("cad.valid", "{}");
    await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task InvalidSecretLengthIsRejected()
{
    await ThrowsAsync<ArgumentException>(
        () => NamedPipeBridge.AcceptOneAsync(
            NewPipeName(),
            Guid.NewGuid().ToString("N"),
            new byte[16]));
}

static async Task ExcessiveJsonDepthIsRejected()
{
    var notification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (_, _) =>
    {
        notification.TrySetResult();
        return ValueTask.CompletedTask;
    });
    client.Start();

    var tooDeep = new string('[', 33) + "0" + new string(']', 33);
    await ThrowsAsync<ArgumentException>(() => client.NotifyAsync("cad.too_deep", tooDeep));

    await client.NotifyAsync("cad.valid", "{}");
    await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task PendingRequestLimitRejectsAndRecovers()
{
    var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var oneCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startedCount = 0;
    var clientOptions = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 2,
        MaximumActiveRequests = 2,
        MaximumConcurrentHandlers = 2
    };
    var pair = await CreatePairAsync(clientOptions: clientOptions);
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(async (request, cancellationToken) =>
    {
        if (request.Method == "cad.recovered")
        {
            return "{\"ok\":true}";
        }

        if (Interlocked.Increment(ref startedCount) == 2)
        {
            allStarted.TrySetResult();
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            oneCancelled.TrySetResult();
            throw;
        }

        return "null";
    });
    client.Start();

    using var firstCancellation = new CancellationTokenSource();
    using var secondCancellation = new CancellationTokenSource();
    var first = client.RequestAsync("cad.block.1", "{}", firstCancellation.Token);
    var second = client.RequestAsync("cad.block.2", "{}", secondCancellation.Token);
    await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

    var capacityError = await ThrowsAsync<BridgeCapacityExceededException>(
        () => client.RequestAsync("cad.over_limit", "{}"));
    Equal(BridgeCapacityKind.PendingRequests, capacityError.CapacityKind);
    Equal(2, capacityError.Limit);

    firstCancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => first);
    await oneCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

    var recovered = await client.RequestAsync("cad.recovered", "{}")
        .WaitAsync(TimeSpan.FromSeconds(5));
    Equal("{\"ok\":true}", recovered);

    secondCancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => second);
}

static async Task ActiveRequestFloodAbortsConnection()
{
    var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startedCount = 0;
    var serverOptions = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 4,
        MaximumActiveRequests = 2,
        MaximumConcurrentHandlers = 2
    };
    var clientOptions = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 4,
        MaximumActiveRequests = 2,
        MaximumConcurrentHandlers = 2
    };
    var pair = await CreatePairAsync(serverOptions, clientOptions);
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(async (_, cancellationToken) =>
    {
        if (Interlocked.Increment(ref startedCount) == 2)
        {
            allStarted.TrySetResult();
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return "null";
    });
    client.Start();

    var first = client.RequestAsync("cad.flood.1", "{}");
    var second = client.RequestAsync("cad.flood.2", "{}");
    await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var third = client.RequestAsync("cad.flood.3", "{}");

    var capacityError = await ThrowsAsync<BridgeCapacityExceededException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    Equal(BridgeCapacityKind.ActiveRequests, capacityError.CapacityKind);
    Equal(2, capacityError.Limit);
    await AssertFailsWithoutTimeoutAsync(first);
    await AssertFailsWithoutTimeoutAsync(second);
    await AssertFailsWithoutTimeoutAsync(third);
}

static async Task HandlerFloodAbortsConnection()
{
    var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startedCount = 0;
    var serverOptions = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 2,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2
    };
    var pair = await CreatePairAsync(serverOptions: serverOptions);
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: async (_, cancellationToken) =>
    {
        if (Interlocked.Increment(ref startedCount) == 2)
        {
            allStarted.TrySetResult();
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    });
    client.Start();

    await client.NotifyAsync("cad.flood.1", "{}");
    await client.NotifyAsync("cad.flood.2", "{}");
    await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await client.NotifyAsync("cad.flood.3", "{}");

    var capacityError = await ThrowsAsync<BridgeCapacityExceededException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    Equal(BridgeCapacityKind.ConcurrentHandlers, capacityError.CapacityKind);
    Equal(2, capacityError.Limit);
}

static async Task SynchronouslyBlockingHandlerDoesNotBlockReceiveLoop()
{
    using var releaseFirstHandler = new ManualResetEventSlim(false);
    var firstHandlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondHandlerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pair = CreateInMemoryPair();
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (notification, _) =>
    {
        if (notification.Method == "cad.blocking.first")
        {
            firstHandlerStarted.TrySetResult();
            releaseFirstHandler.Wait();
        }
        else if (notification.Method == "cad.blocking.second")
        {
            secondHandlerCompleted.TrySetResult();
        }

        return ValueTask.CompletedTask;
    });
    client.Start();

    try
    {
        await client.NotifyAsync("cad.blocking.first", "{}");
        await firstHandlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.NotifyAsync("cad.blocking.second", "{}");
        await secondHandlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    finally
    {
        releaseFirstHandler.Set();
    }
}

static async Task NonCooperativeHandlerDoesNotBlockConcurrentDispose()
{
    using var releaseHandler = new ManualResetEventSlim(false);
    var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var handlerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var serverOptions = new BridgeConnectionOptions
    {
        ShutdownTimeout = TimeSpan.FromMilliseconds(100)
    };
    var pair = CreateInMemoryPair(serverOptions: serverOptions);
    await using var server = pair.Server;
    await using var client = pair.Client;

    server.Start(notificationHandler: (_, _) =>
    {
        handlerStarted.TrySetResult();
        try
        {
            releaseHandler.Wait();
        }
        finally
        {
            handlerExited.TrySetResult();
        }

        return ValueTask.CompletedTask;
    });
    client.Start();

    try
    {
        await client.NotifyAsync("cad.blocking.dispose", "{}");
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var elapsed = Stopwatch.StartNew();
        var firstDispose = server.DisposeAsync().AsTask();
        var concurrentDispose = server.DisposeAsync().AsTask();
        await Task.WhenAll(firstDispose, concurrentDispose).WaitAsync(TimeSpan.FromSeconds(2));
        elapsed.Stop();

        True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
        False(handlerExited.Task.IsCompleted);
    }
    finally
    {
        releaseHandler.Set();
    }

    await handlerExited.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await server.DisposeAsync();
}

static async Task LateHandlerFaultIsObservedAndDiagnosable()
{
    var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var lateHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var lateException = new InvalidOperationException("模拟关闭超时后的迟到handler fault。");
    var unobservedLateFault = false;
    void OnUnobservedTaskException(object? _, UnobservedTaskExceptionEventArgs eventArgs)
    {
        if (eventArgs.Exception.Flatten().InnerExceptions.Any(exception =>
                ReferenceEquals(exception, lateException)))
        {
            unobservedLateFault = true;
        }
    }

    TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    try
    {
        var serverOptions = new BridgeConnectionOptions
        {
            ShutdownTimeout = TimeSpan.FromMilliseconds(100)
        };
        var pair = CreateInMemoryPair(serverOptions: serverOptions);
        await using var server = pair.Server;
        await using var client = pair.Client;
        server.Start(notificationHandler: (_, _) =>
        {
            handlerStarted.TrySetResult();
            return new ValueTask(lateHandler.Task);
        });
        client.Start();

        await client.NotifyAsync("cad.late.fault", "{}");
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        lateHandler.TrySetException(lateException);

        await WaitUntilAsync(
            () => ReferenceEquals(server.TerminalError, lateException),
            TimeSpan.FromSeconds(2));
        await server.DisposeAsync();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        False(unobservedLateFault);
    }
    finally
    {
        lateHandler.TrySetCanceled();
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }
}

static async Task NonCooperativeStreamAndSendGateUseBoundedSafeCleanup()
{
    var stream = new NonCooperativeShutdownStream();
    var options = new BridgeConnectionOptions
    {
        ShutdownTimeout = TimeSpan.FromMilliseconds(100)
    };
    await using var connection = new AuthenticatedPipeConnection(
        stream,
        Guid.NewGuid().ToString("N"),
        IpcSessionSecret.Generate(),
        options);
    var authenticatorSecret = GetConnectionAuthenticatorSecret(connection);
    connection.Start();
    Task? pendingNotification = null;

    try
    {
        pendingNotification = connection.NotifyAsync("cad.blocking.write", "{}");
        await stream.FirstWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

        var elapsed = Stopwatch.StartNew();
        await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        elapsed.Stop();
        True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
        False(authenticatorSecret.All(static value => value == 0));

        stream.ReleaseDispose();
        await connection.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        False(authenticatorSecret.All(static value => value == 0));

        stream.ReleaseWrites();
        await pendingNotification.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            () => authenticatorSecret.All(static value => value == 0),
            TimeSpan.FromSeconds(2));
    }
    finally
    {
        stream.ReleaseDispose();
        stream.ReleaseWrites();
        if (pendingNotification is not null)
        {
            try
            {
                await pendingNotification.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
        }
    }
}

static async Task CapacitySlotsRecoverAfterCompletion()
{
    var options = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 1,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2
    };
    var pair = await CreatePairAsync(options, options);
    await using var server = pair.Server;
    await using var client = pair.Client;
    server.Start((_, _) => ValueTask.FromResult<string?>("{\"ok\":true}"));
    client.Start();

    for (var attempt = 0; attempt < 100; attempt++)
    {
        var response = await client.RequestAsync("cad.reuse", "{}")
            .WaitAsync(TimeSpan.FromSeconds(5));
        Equal("{\"ok\":true}", response);
    }
}

static async Task PendingNotificationLimitRejectsAndRecovers()
{
    var stream = new BlockingWriteStream();
    var options = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 1,
        MaximumPendingNotifications = 2,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2
    };
    await using var connection = new AuthenticatedPipeConnection(
        stream,
        Guid.NewGuid().ToString("N"),
        IpcSessionSecret.Generate(),
        options);
    connection.Start();

    var first = connection.NotifyAsync("cad.queue.1", "{}");
    await stream.FirstWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));
    var second = connection.NotifyAsync("cad.queue.2", "{}");

    var capacityError = await ThrowsAsync<BridgeCapacityExceededException>(
        () => connection.NotifyAsync("cad.queue.3", "{}"));
    Equal(BridgeCapacityKind.PendingNotifications, capacityError.CapacityKind);
    Equal(2, capacityError.Limit);

    stream.ReleaseWrites();
    await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    await connection.NotifyAsync("cad.queue.recovered", "{}").WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task ConfiguredFrameLimitRejectsAndRecovers()
{
    const int maximumFrameBytes = 1024;
    var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var options = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 2,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2,
        MaximumFrameBytes = maximumFrameBytes
    };
    var pair = await CreatePairAsync(options, options);
    await using var server = pair.Server;
    await using var client = pair.Client;
    server.Start(notificationHandler: (_, _) =>
    {
        received.TrySetResult();
        return ValueTask.CompletedTask;
    });
    client.Start();

    var oversizedJson = "\"" + new string('x', maximumFrameBytes) + "\"";
    await ThrowsAsync<BridgeProtocolException>(
        () => client.NotifyAsync("cad.frame.too_large", oversizedJson));

    await client.NotifyAsync("cad.frame.valid", "{}");
    await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task ConfiguredIncomingFrameLimitRejectsBeforeAllocation()
{
    const int maximumFrameBytes = 1024;
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var options = new BridgeConnectionOptions
    {
        MaximumPendingRequests = 2,
        MaximumActiveRequests = 1,
        MaximumConcurrentHandlers = 2,
        MaximumFrameBytes = maximumFrameBytes
    };
    var accept = NamedPipeBridge.AcceptOneAsync(
        pipeName,
        sessionId,
        secret,
        options: options);
    await using var rawClient = CreateRawClient(pipeName);
    await rawClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await using var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    server.Start();

    var prefix = new byte[sizeof(int)];
    BinaryPrimitives.WriteInt32LittleEndian(prefix, maximumFrameBytes + 1);
    await rawClient.WriteAsync(prefix);
    await rawClient.FlushAsync();

    await ThrowsAsync<BridgeProtocolException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    await AssertPipeClosedAsync(rawClient);
}

static async Task NotificationHandlerFailureAbortsConnection()
{
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;
    server.Start(notificationHandler: (_, _) =>
        throw new InvalidOperationException("模拟通知handler失败。"));
    client.Start();

    await client.NotifyAsync("cad.notification.fail", "{}");
    var exception = await ThrowsAsync<InvalidOperationException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    Equal("模拟通知handler失败。", exception.Message);
    await client.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    await ThrowsAsync<EndOfStreamException>(
        () => client.NotifyAsync("cad.after_handler_failure", "{}"));
}

static Task InvalidCapacityOptionsAreRejected()
{
    using var stream = new MemoryStream();
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new AuthenticatedPipeConnection(
            stream,
            Guid.NewGuid().ToString("N"),
            IpcSessionSecret.Generate(),
            new BridgeConnectionOptions { MaximumPendingRequests = 0 }));
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new AuthenticatedPipeConnection(
            stream,
            Guid.NewGuid().ToString("N"),
            IpcSessionSecret.Generate(),
            new BridgeConnectionOptions { MaximumPendingNotifications = 0 }));
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new AuthenticatedPipeConnection(
            stream,
            Guid.NewGuid().ToString("N"),
            IpcSessionSecret.Generate(),
            new BridgeConnectionOptions { ShutdownTimeout = TimeSpan.Zero }));
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new AuthenticatedPipeConnection(
            stream,
            Guid.NewGuid().ToString("N"),
            IpcSessionSecret.Generate(),
            new BridgeConnectionOptions
            {
                ShutdownTimeout =
                    BridgeConnectionOptions.AbsoluteMaximumShutdownTimeout + TimeSpan.FromTicks(1)
            }));
    return Task.CompletedTask;
}

static async Task NormalEofTerminatesConnection()
{
    var pair = await CreatePairAsync();
    await using var server = pair.Server;
    await using var client = pair.Client;
    server.Start();
    client.Start();

    await client.DisposeAsync();
    await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    await ThrowsAsync<EndOfStreamException>(() => server.NotifyAsync("cad.after_eof", "{}"));
}

static async Task PartialWriteFailureAbortsConnection()
{
    var stream = new PartialWriteFailingStream();
    await using var connection = new AuthenticatedPipeConnection(
        stream,
        Guid.NewGuid().ToString("N"),
        IpcSessionSecret.Generate());
    connection.Start();

    await ThrowsAsync<IOException>(() => connection.NotifyAsync("cad.partial_write", "{}"));
    await connection.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    True(stream.IsDisposed);
    await ThrowsAsync<EndOfStreamException>(() => connection.NotifyAsync("cad.after_failure", "{}"));
}

static async Task AuthenticatedProtocolErrorAbortsConnection()
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var accept = NamedPipeBridge.AcceptOneAsync(pipeName, sessionId, secret);
    await using var rawClient = CreateRawClient(pipeName);
    await rawClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await using var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    server.Start();

    var malformed = CreateEnvelope(secret, sessionId, 1, "nonce-malformed");
    malformed.PayloadJson = "{";
    malformed.Mac = new IpcEnvelopeAuthenticator(secret).Sign(malformed);
    await LengthPrefixedFrameCodec.WriteAsync(rawClient, malformed);

    await ThrowsAsync<BridgeProtocolException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    await AssertPipeClosedAsync(rawClient);
}

static Task IpcSecretsAreZeroedOnDispose()
{
    var secret = IpcSessionSecret.Generate();
    var authenticator = new IpcEnvelopeAuthenticator(secret);
    var authenticatorSecret = GetAuthenticatorSecret(authenticator);
    False(authenticatorSecret.All(static value => value == 0));
    authenticator.Dispose();
    True(authenticatorSecret.All(static value => value == 0));
    Throws<ObjectDisposedException>(() => authenticator.Sign(new IpcEnvelope()));

    var guard = new IpcSessionGuard("session-zero-test", secret);
    var guardAuthenticatorField = typeof(IpcSessionGuard).GetField(
        "_authenticator",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("IpcSessionGuard authenticator field not found.");
    var guardAuthenticator = (IpcEnvelopeAuthenticator?)guardAuthenticatorField.GetValue(guard)
        ?? throw new InvalidOperationException("IpcSessionGuard authenticator missing.");
    var guardSecret = GetAuthenticatorSecret(guardAuthenticator);
    guard.Dispose();
    True(guardSecret.All(static value => value == 0));
    Throws<ObjectDisposedException>(() => guard.ValidateAndAccept(new IpcEnvelope()));
    return Task.CompletedTask;
}

static async Task AssertRawEnvelopeRejected(
    Func<byte[], string, IReadOnlyList<IpcEnvelope>> createEnvelopes,
    IpcValidationCode expectedCode)
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var accept = NamedPipeBridge.AcceptOneAsync(pipeName, sessionId, secret);
    await using var rawClient = CreateRawClient(pipeName);
    await rawClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await using var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    server.Start();

    foreach (var envelope in createEnvelopes(secret, sessionId))
    {
        await LengthPrefixedFrameCodec.WriteAsync(rawClient, envelope);
    }

    var exception = await ThrowsAsync<BridgeAuthenticationException>(
        () => server.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    Equal(expectedCode, exception.ValidationCode);

    await AssertPipeClosedAsync(rawClient);
}

static async Task AssertPipeClosedAsync(Stream stream)
{
    var probe = new byte[1];
    try
    {
        var read = await stream.ReadAsync(probe).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Equal(0, read);
    }
    catch (IOException)
    {
        // Windows may report a broken pipe instead of EOF; both prove the rejected connection was aborted.
    }
}

static byte[] GetAuthenticatorSecret(IpcEnvelopeAuthenticator authenticator)
{
    var field = typeof(IpcEnvelopeAuthenticator).GetField(
        "_sessionSecret",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("IpcEnvelopeAuthenticator secret field not found.");
    return (byte[]?)field.GetValue(authenticator)
        ?? throw new InvalidOperationException("IpcEnvelopeAuthenticator secret missing.");
}

static byte[] GetConnectionAuthenticatorSecret(AuthenticatedPipeConnection connection)
{
    var field = typeof(AuthenticatedPipeConnection).GetField(
        "_authenticator",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AuthenticatedPipeConnection authenticator field not found.");
    var authenticator = (IpcEnvelopeAuthenticator?)field.GetValue(connection)
        ?? throw new InvalidOperationException("AuthenticatedPipeConnection authenticator missing.");
    return GetAuthenticatorSecret(authenticator);
}

static async Task<(AuthenticatedPipeConnection Server, AuthenticatedPipeConnection Client)> CreatePairAsync(
    BridgeConnectionOptions? serverOptions = null,
    BridgeConnectionOptions? clientOptions = null)
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var pipeName = NewPipeName();
    var accept = NamedPipeBridge.AcceptOneAsync(
        pipeName,
        sessionId,
        secret,
        options: serverOptions);
    var client = await NamedPipeBridge.ConnectAsync(
        pipeName,
        sessionId,
        secret,
        TimeSpan.FromSeconds(5),
        options: clientOptions);
    var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    return (server, client);
}

static (AuthenticatedPipeConnection Server, AuthenticatedPipeConnection Client) CreateInMemoryPair(
    BridgeConnectionOptions? serverOptions = null,
    BridgeConnectionOptions? clientOptions = null)
{
    var secret = IpcSessionSecret.Generate();
    var sessionId = Guid.NewGuid().ToString("N");
    var streams = InMemoryDuplexStream.CreatePair();
    return (
        new AuthenticatedPipeConnection(streams.Left, sessionId, secret, serverOptions),
        new AuthenticatedPipeConnection(streams.Right, sessionId, secret, clientOptions));
}

static NamedPipeClientStream CreateRawClient(string pipeName)
{
    return new NamedPipeClientStream(
        ".",
        pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
}

static IpcEnvelope CreateEnvelope(byte[] secret, string sessionId, long sequence, string nonce)
{
    var envelope = new IpcEnvelope
    {
        MessageId = Guid.NewGuid().ToString("N"),
        SessionId = sessionId,
        Sequence = sequence,
        MessageType = BridgeMessageTypes.Notification,
        PayloadJson = "{\"method\":\"cad.test\",\"bodyJson\":\"{}\"}",
        Nonce = nonce
    };
    envelope.Mac = new IpcEnvelopeAuthenticator(secret).Sign(envelope);
    return envelope;
}

static string NewPipeName()
{
    return "codex-autocad-spec-" + Guid.NewGuid().ToString("N");
}

static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task AssertFailsWithoutTimeoutAsync(Task task)
{
    try
    {
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch (TimeoutException)
    {
        throw;
    }
    catch (Exception)
    {
        return;
    }

    throw new InvalidOperationException("Expected connection failure.");
}

static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!predicate())
    {
        if (DateTime.UtcNow >= deadline)
        {
            throw new TimeoutException("Expected condition was not reached before timeout.");
        }

        await Task.Delay(10);
    }
}

static TException Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void False(bool condition)
{
    if (condition)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }
}

sealed class PartialWriteFailingStream : Stream
{
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _writeCount;

    public bool IsDisposed => _disposed.Task.IsCompleted;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _disposed.Task.WaitAsync(cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _writeCount) == 1)
        {
            return ValueTask.CompletedTask;
        }

        return ValueTask.FromException(new IOException("模拟部分写入失败。"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed.TrySetResult();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        _disposed.TrySetResult();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

sealed class BlockingWriteStream : Stream
{
    private readonly TaskCompletionSource _allowWrites = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task FirstWriteStarted => _firstWriteStarted.Task;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void ReleaseWrites()
    {
        _allowWrites.TrySetResult();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _disposed.Task.WaitAsync(cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        _firstWriteStarted.TrySetResult();
        await _allowWrites.Task.WaitAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed.TrySetResult();
            _allowWrites.TrySetResult();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        _disposed.TrySetResult();
        _allowWrites.TrySetResult();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

sealed class InMemoryDuplexStream : Stream
{
    private readonly ChannelReader<byte[]> _incoming;
    private readonly ChannelWriter<byte[]> _incomingCompletion;
    private readonly ChannelWriter<byte[]> _outgoing;
    private readonly CancellationTokenSource _disposedCancellation = new();
    private byte[]? _currentRead;
    private int _currentReadOffset;
    private int _disposed;

    private InMemoryDuplexStream(
        ChannelReader<byte[]> incoming,
        ChannelWriter<byte[]> incomingCompletion,
        ChannelWriter<byte[]> outgoing)
    {
        _incoming = incoming;
        _incomingCompletion = incomingCompletion;
        _outgoing = outgoing;
    }

    public static (InMemoryDuplexStream Left, InMemoryDuplexStream Right) CreatePair()
    {
        var leftToRight = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        var rightToLeft = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        return (
            new InMemoryDuplexStream(
                rightToLeft.Reader,
                rightToLeft.Writer,
                leftToRight.Writer),
            new InMemoryDuplexStream(
                leftToRight.Reader,
                leftToRight.Writer,
                rightToLeft.Writer));
    }

    public override bool CanRead => Volatile.Read(ref _disposed) == 0;

    public override bool CanSeek => false;

    public override bool CanWrite => Volatile.Read(ref _disposed) == 0;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        while (_currentRead is null || _currentReadOffset >= _currentRead.Length)
        {
            _currentRead = null;
            _currentReadOffset = 0;
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposedCancellation.Token);
            if (!await _incoming.WaitToReadAsync(linkedCancellation.Token))
            {
                return 0;
            }

            if (!_incoming.TryRead(out _currentRead))
            {
                continue;
            }
        }

        var bytesToCopy = Math.Min(buffer.Length, _currentRead.Length - _currentReadOffset);
        _currentRead.AsMemory(_currentReadOffset, bytesToCopy).CopyTo(buffer);
        _currentReadOffset += bytesToCopy;
        return bytesToCopy;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_outgoing.TryWrite(buffer.ToArray()))
        {
            return ValueTask.FromException(new IOException("内存双工流的对端已关闭。"));
        }

        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCore();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposedCancellation.Cancel();
        _outgoing.TryComplete();
        _incomingCompletion.TryComplete();
    }
}

sealed class NonCooperativeShutdownStream : Stream
{
    private readonly TaskCompletionSource _disposeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _writeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task FirstWriteStarted => _firstWriteStarted.Task;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void ReleaseDispose()
    {
        _disposeRelease.TrySetResult();
    }

    public void ReleaseWrites()
    {
        _writeRelease.TrySetResult();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _disposeStarted.Task.WaitAsync(cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        _firstWriteStarted.TrySetResult();
        await _writeRelease.Task;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposeStarted.TrySetResult();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        _disposeStarted.TrySetResult();
        await _disposeRelease.Task;
        GC.SuppressFinalize(this);
    }
}
