using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

var specs = new[]
{
    new SpecCase("协议v1固定canonical bytes与HMAC向量一致", KnownProtocolVectorMatches),
    new SpecCase("合法信封被接受", ValidEnvelopePasses),
    new SpecCase("篡改载荷被拒绝", TamperedPayloadFails),
    new SpecCase("跨会话信封被拒绝", CrossSessionFails),
    new SpecCase("重复序号被拒绝", ReplayedSequenceFails),
    new SpecCase("首包序号跳号被拒绝", InitialSequenceGapFails),
    new SpecCase("无效MAC不推进序号或nonce状态", InvalidMacDoesNotAdvanceGuardState),
    new SpecCase("序号必须为正且达到最大值后失败关闭", SequenceBoundsFailClosed),
    new SpecCase("重复nonce被拒绝", ReplayedNonceFails),
    new SpecCase("nonce历史满载时拒绝且在过期边界恢复", NonceCapacityFailsClosedAndExpiresAtBoundary),
    new SpecCase("nonce洪泛不能突破历史容量", NonceFloodCannotExceedHistoryCapacity),
    new SpecCase("超长nonce被拒绝", OversizedNonceFails),
    new SpecCase("非法nonce历史配置被拒绝", InvalidNonceHistoryOptionsFail),
    new SpecCase("密钥长度必须恰好为32字节", InvalidSecretLengthsFail),
    new SpecCase("认证器释放时清零私有密钥副本", AuthenticatorSecretIsZeroedOnDispose),
    new SpecCase("null签名字段被拒绝且不等价于空字符串", NullSignedFieldsFail),
    new SpecCase("畸形Unicode不能进入认证字节", MalformedUnicodeFailsClosed)
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

static void KnownProtocolVectorMatches()
{
    const string secretHex = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";
    const string canonicalHex = "313A31383A6D73672DCEB12DF09F9880363A636F72722DE4B8AD31323A73657373696F6E2D32303136323A343231313A6361642E636F6E7465787432343A7B2274657874223A22E4B8ADE69687F09F9880222C226C696E65223A317D33323A3030313132323333343435353636373738383939414142424343444445454646";
    const string expectedMac = "46FFA5506FD595BA64CEAD67EDBAF8707E1A585988BC80298EBF569F69B38400";
    var secret = DecodeHex(secretHex);
    var envelope = new IpcEnvelope
    {
        ProtocolVersion = 1,
        MessageId = "msg-α-😀",
        CorrelationId = "corr-中",
        SessionId = "session-2016",
        Sequence = 42,
        MessageType = "cad.context",
        PayloadJson = "{\"text\":\"中文😀\",\"line\":1}",
        Nonce = "00112233445566778899AABBCCDDEEFF"
    };

    Equal(canonicalHex, EncodeHex(IpcCanonicalEnvelopeEncoding.GetBytes(envelope)));
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    Equal(expectedMac, authenticator.Sign(envelope));
    envelope.Mac = expectedMac;
    Equal(true, authenticator.Verify(envelope));
    Console.WriteLine("AUTH_VECTOR_V1 canonical=" + canonicalHex + " mac=" + expectedMac);
}

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

static void InvalidSecretLengthsFail()
{
    Throws<ArgumentException>(() => _ = new IpcEnvelopeAuthenticator(new byte[16]));
    Throws<ArgumentException>(() => _ = new IpcEnvelopeAuthenticator(new byte[33]));
}

static void InitialSequenceGapFails()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret);
    var skipped = CreateEnvelope("session-a", 2, "nonce-2");
    skipped.Mac = authenticator.Sign(skipped);
    Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(skipped));
}

static void InvalidMacDoesNotAdvanceGuardState()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret);
    var invalid = CreateEnvelope("session-a", 1, "nonce-1");
    invalid.Mac = new string('0', 64);
    Equal(IpcValidationCode.InvalidMac, guard.ValidateAndAccept(invalid));

    var valid = CreateEnvelope("session-a", 1, "nonce-1");
    valid.Mac = authenticator.Sign(valid);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(valid));
}

static void AuthenticatorSecretIsZeroedOnDispose()
{
    var original = Enumerable.Range(1, IpcSessionSecret.SizeInBytes).Select(value => (byte)value).ToArray();
    var authenticator = new IpcEnvelopeAuthenticator(original);
    var field = typeof(IpcEnvelopeAuthenticator).GetField(
        "_sessionSecret",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("IpcEnvelopeAuthenticator secret field not found.");
    var privateCopy = (byte[]?)field.GetValue(authenticator)
        ?? throw new InvalidOperationException("IpcEnvelopeAuthenticator secret copy missing.");
    Equal(false, ReferenceEquals(original, privateCopy));
    authenticator.Dispose();
    Equal(true, privateCopy.All(value => value == 0));
    Equal(false, original.All(value => value == 0));
}

static void NullSignedFieldsFail()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    var emptyCorrelation = CreateEnvelope("session-a", 1, "nonce-1");
    emptyCorrelation.CorrelationId = string.Empty;
    var emptyMac = authenticator.Sign(emptyCorrelation);

    var nullCorrelation = CreateEnvelope("session-a", 1, "nonce-1");
    nullCorrelation.CorrelationId = null!;
    Throws<ArgumentException>(() => IpcCanonicalEnvelopeEncoding.GetBytes(nullCorrelation));
    Throws<ArgumentException>(() => authenticator.Sign(nullCorrelation));
    nullCorrelation.Mac = emptyMac;
    Equal(false, authenticator.Verify(nullCorrelation));
    using var correlationGuard = new IpcSessionGuard("session-a", secret);
    Equal(IpcValidationCode.InvalidMetadata, correlationGuard.ValidateAndAccept(nullCorrelation));

    var nullPayload = CreateEnvelope("session-a", 1, "nonce-2");
    nullPayload.PayloadJson = null!;
    nullPayload.Mac = new string('0', 64);
    using var payloadGuard = new IpcSessionGuard("session-a", secret);
    Equal(IpcValidationCode.InvalidMetadata, payloadGuard.ValidateAndAccept(nullPayload));
}

static void MalformedUnicodeFailsClosed()
{
    var secret = IpcSessionSecret.Generate();
    var malformed = CreateEnvelope("session-a", 1, "nonce-1");
    malformed.PayloadJson = "\uD800";
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    Throws<System.Text.EncoderFallbackException>(() => authenticator.Sign(malformed));
    malformed.Mac = new string('0', 64);
    Equal(false, authenticator.Verify(malformed));
    using var guard = new IpcSessionGuard("session-a", secret);
    Equal(IpcValidationCode.InvalidMetadata, guard.ValidateAndAccept(malformed));
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
        Clock = clock
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
        Clock = clock
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

static byte[] DecodeHex(string value)
{
    if ((value.Length & 1) != 0)
    {
        throw new ArgumentException("Hex length must be even.", nameof(value));
    }

    var bytes = new byte[value.Length / 2];
    for (var index = 0; index < bytes.Length; index++)
    {
        bytes[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
    }

    return bytes;
}

static string EncodeHex(byte[] bytes)
{
    var builder = new System.Text.StringBuilder(bytes.Length * 2);
    foreach (var value in bytes)
    {
        builder.Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
    }

    return builder.ToString();
}

sealed class ManualTimeProvider : IIpcClock
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public DateTimeOffset GetUtcNow()
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

sealed class SpecCase
{
    public SpecCase(string name, Action run)
    {
        Name = name;
        Run = run;
    }

    public string Name { get; }

    public Action Run { get; }
}
