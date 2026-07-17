using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

var specs = new (string Name, Action Run)[]
{
    ("合法信封被接受", ValidEnvelopePasses),
    ("篡改载荷被拒绝", TamperedPayloadFails),
    ("跨会话信封被拒绝", CrossSessionFails),
    ("重复序号被拒绝", ReplayedSequenceFails),
    ("重复nonce被拒绝", ReplayedNonceFails),
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
