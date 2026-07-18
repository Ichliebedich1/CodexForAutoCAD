using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

var specs = new (string Name, Action Run)[]
{
    ("合法信封被接受", ValidEnvelopePasses),
    ("篡改载荷被拒绝", TamperedPayloadFails),
    ("跨会话信封被拒绝", CrossSessionFails),
    ("重复序号被拒绝", ReplayedSequenceFails),
    ("序号必须为正且达到最大值后失败关闭", SequenceBoundsFailClosed),
    ("重复nonce被拒绝", ReplayedNonceFails),
    ("nonce历史满载时拒绝且在过期边界恢复", NonceCapacityFailsClosedAndExpiresAtBoundary),
    ("nonce洪泛不能突破历史容量", NonceFloodCannotExceedHistoryCapacity),
    ("超长nonce被拒绝", OversizedNonceFails),
    ("非法nonce历史配置被拒绝", InvalidNonceHistoryOptionsFail),
    ("短密钥被拒绝", ShortSecretFails)
};

var failed = 0;
foreach (var spec in specs)
{
    try
    {
        spec.Run();
        Console.WriteLine("PASS " + spec.Name);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + spec.Name + ": " + exception.Message);
    }
}

Console.WriteLine($"{specs.Length - failed}/{specs.Length} specs passed");
return failed == 0 ? 0 : 1;

static void ValidEnvelopePasses()
{
    var secret = IpcSessionSecret.Generate();
    var envelope = CreateEnvelope("session-a", 1, "nonce-1");
    envelope.Mac = new IpcEnvelopeAuthenticator(secret).Sign(envelope);
    Equal(IpcValidationCode.Accepted, new IpcSessionGuard("session-a", secret).ValidateAndAccept(envelope));
}

static void TamperedPayloadFails()
{
    var secret = IpcSessionSecret.Generate();
    var envelope = CreateEnvelope("session-a", 1, "nonce-1");
    envelope.Mac = new IpcEnvelopeAuthenticator(secret).Sign(envelope);
    envelope.PayloadJson = "{\"unsafe\":true}";
    Equal(IpcValidationCode.InvalidMac, new IpcSessionGuard("session-a", secret).ValidateAndAccept(envelope));
}

static void CrossSessionFails()
{
    var secret = IpcSessionSecret.Generate();
    var envelope = CreateEnvelope("session-b", 1, "nonce-1");
    envelope.Mac = new IpcEnvelopeAuthenticator(secret).Sign(envelope);
    Equal(IpcValidationCode.InvalidSession, new IpcSessionGuard("session-a", secret).ValidateAndAccept(envelope));
}

static void ReplayedSequenceFails()
{
    var secret = IpcSessionSecret.Generate();
    var authenticator = new IpcEnvelopeAuthenticator(secret);
    var guard = new IpcSessionGuard("session-a", secret);
    var first = CreateEnvelope("session-a", 1, "nonce-1");
    first.Mac = authenticator.Sign(first);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(first));

    var replay = CreateEnvelope("session-a", 1, "nonce-2");
    replay.Mac = authenticator.Sign(replay);
    Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(replay));
}

static void SequenceBoundsFailClosed()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret);

    foreach (var invalidSequence in new[] { 0L, -1L, long.MinValue })
    {
        var invalid = CreateEnvelope("session-a", invalidSequence, "nonce-" + invalidSequence);
        invalid.Mac = authenticator.Sign(invalid);
        Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(invalid));
    }

    var sequenceField = typeof(IpcSessionGuard).GetField(
        "_lastAcceptedSequence",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("IpcSessionGuard sequence field not found.");
    sequenceField.SetValue(guard, long.MaxValue);

    var wrapped = CreateEnvelope("session-a", long.MinValue, "nonce-wrapped");
    wrapped.Mac = authenticator.Sign(wrapped);
    Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(wrapped));
}
static void ReplayedNonceFails()
{
    var secret = IpcSessionSecret.Generate();
    var authenticator = new IpcEnvelopeAuthenticator(secret);
    var guard = new IpcSessionGuard("session-a", secret);
    var first = CreateEnvelope("session-a", 1, "nonce-1");
    first.Mac = authenticator.Sign(first);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(first));

    var replay = CreateEnvelope("session-a", 2, "nonce-1");
    replay.Mac = authenticator.Sign(replay);
    Equal(IpcValidationCode.ReplayedNonce, guard.ValidateAndAccept(replay));
}

static void ShortSecretFails()
{
    try
    {
        _ = new IpcEnvelopeAuthenticator(new byte[16]);
        throw new InvalidOperationException("Expected ArgumentException.");
    }
    catch (ArgumentException)
    {
    }
}

static void NonceCapacityFailsClosedAndExpiresAtBoundary()
{
    var secret = IpcSessionSecret.Generate();
    var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
    var retention = TimeSpan.FromMinutes(1);
    var options = new IpcSessionGuardOptions
    {
        MaximumNonceHistoryEntries = 2,
        NonceRetention = retention,
        TimeProvider = clock
    };
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret, options);

    var first = CreateEnvelope("session-a", 1, "nonce-1");
    first.Mac = authenticator.Sign(first);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(first));

    var second = CreateEnvelope("session-a", 2, "nonce-2");
    second.Mac = authenticator.Sign(second);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(second));

    var third = CreateEnvelope("session-a", 3, "nonce-3");
    third.Mac = authenticator.Sign(third);
    Equal(IpcValidationCode.NonceHistoryCapacityExceeded, guard.ValidateAndAccept(third));

    clock.Advance(retention - TimeSpan.FromTicks(1));
    Equal(IpcValidationCode.NonceHistoryCapacityExceeded, guard.ValidateAndAccept(third));

    clock.Advance(TimeSpan.FromTicks(1));
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(third));
    Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(first));
}

static void NonceFloodCannotExceedHistoryCapacity()
{
    const int capacity = 32;
    var secret = IpcSessionSecret.Generate();
    var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
    var options = new IpcSessionGuardOptions
    {
        MaximumNonceHistoryEntries = capacity,
        NonceRetention = TimeSpan.FromMinutes(1),
        TimeProvider = clock
    };
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret, options);

    for (var sequence = 1; sequence <= capacity; sequence++)
    {
        var envelope = CreateEnvelope("session-a", sequence, "nonce-" + sequence);
        envelope.Mac = authenticator.Sign(envelope);
        Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(envelope));
    }

    for (var attempt = 0; attempt < 256; attempt++)
    {
        var flooded = CreateEnvelope("session-a", capacity + 1L, "flood-" + attempt);
        flooded.Mac = authenticator.Sign(flooded);
        Equal(IpcValidationCode.NonceHistoryCapacityExceeded, guard.ValidateAndAccept(flooded));
    }

    clock.Advance(TimeSpan.FromMinutes(1));
    var recovered = CreateEnvelope("session-a", capacity + 1L, "recovered");
    recovered.Mac = authenticator.Sign(recovered);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(recovered));
}

static void OversizedNonceFails()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret);
    var envelope = CreateEnvelope(
        "session-a",
        1,
        new string('n', IpcSessionGuard.MaximumNonceCharacters + 1));
    envelope.Mac = authenticator.Sign(envelope);
    Equal(IpcValidationCode.InvalidNonce, guard.ValidateAndAccept(envelope));
}

static void InvalidNonceHistoryOptionsFail()
{
    var options = new IpcSessionGuardOptions { MaximumNonceHistoryEntries = 0 };
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new IpcSessionGuard("session-a", IpcSessionSecret.Generate(), options));
}

static IpcEnvelope CreateEnvelope(string sessionId, long sequence, string nonce)
{
    return new IpcEnvelope
    {
        MessageId = Guid.NewGuid().ToString("N"),
        SessionId = sessionId,
        Sequence = sequence,
        MessageType = "cad.context",
        PayloadJson = "{}",
        Nonce = nonce
    };
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
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

sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        _utcNow = _utcNow.Add(elapsed);
    }
}
