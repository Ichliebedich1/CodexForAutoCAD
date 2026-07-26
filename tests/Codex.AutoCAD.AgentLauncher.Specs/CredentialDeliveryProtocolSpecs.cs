using System.Reflection;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.Ipc;

internal static class CredentialDeliveryProtocolSpecs
{
    internal static void DisabledRoundTrips()
    {
        var fixture = new ProtocolFixture();
        using (fixture)
        using (var authenticator = new IpcEnvelopeAuthenticator(fixture.Key))
        using (var guard = new IpcSessionGuard(fixture.SessionId, fixture.Key))
        using (var frame = new MemoryStream())
        {
            AgentCredentialDeliveryProtocol.WriteSingleFrame(
                frame,
                fixture.SessionId,
                fixture.BootstrapId,
                fixture.ProcessId,
                fixture.ProcessCreationFileTime,
                AgentCredentialDeliveryMode.Disabled,
                null,
                authenticator);
            frame.Position = 0;

            using var delivery = AgentCredentialDeliveryProtocol.ReadSingleFrame(
                frame,
                fixture.SessionId,
                fixture.BootstrapId,
                fixture.ProcessId,
                fixture.ProcessCreationFileTime,
                guard);
            Equal(AgentCredentialDeliveryMode.Disabled, delivery.Mode);
            True(delivery.Secret == null, "Disabled delivery unexpectedly carried a secret.");
        }
    }

    internal static void AccessTokenRoundTripsAndZeroes()
    {
        var fixture = new ProtocolFixture();
        var source = new byte[] { 41, 57, 73, 89, 105, 121, 137 };
        byte[]? receivedBuffer = null;
        try
        {
            using (fixture)
            using (var authenticator = new IpcEnvelopeAuthenticator(fixture.Key))
            using (var guard = new IpcSessionGuard(fixture.SessionId, fixture.Key))
            using (var frame = new MemoryStream())
            using (var senderSecret = new AgentHostCredentialSecret(source))
            {
                AgentCredentialDeliveryProtocol.WriteSingleFrame(
                    frame,
                    fixture.SessionId,
                    fixture.BootstrapId,
                    fixture.ProcessId,
                    fixture.ProcessCreationFileTime,
                    AgentCredentialDeliveryMode.AccessToken,
                    senderSecret,
                    authenticator);
                frame.Position = 0;

                using var delivery = AgentCredentialDeliveryProtocol.ReadSingleFrame(
                    frame,
                    fixture.SessionId,
                    fixture.BootstrapId,
                    fixture.ProcessId,
                    fixture.ProcessCreationFileTime,
                    guard);
                Equal(AgentCredentialDeliveryMode.AccessToken, delivery.Mode);
                True(delivery.Secret != null, "Access-token delivery omitted its secret.");

                var field = typeof(AgentHostCredentialSecret).GetField(
                    "credentialBytes",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Credential buffer field was not found.");
                receivedBuffer = (byte[]?)field.GetValue(delivery.Secret)
                    ?? throw new InvalidOperationException("Received credential buffer was unavailable.");
                True(
                    receivedBuffer.SequenceEqual(source),
                    "Received access-token bytes differ from the sender bytes.");
                delivery.Dispose();
                True(
                    receivedBuffer.All(value => value == 0),
                    "Receiver credential buffer was not cleared in place.");
            }

            True(source.All(value => value == 0), "Sender credential buffer was not cleared in place.");
        }
        finally
        {
            Array.Clear(source, 0, source.Length);
            if (receivedBuffer != null)
            {
                Array.Clear(receivedBuffer, 0, receivedBuffer.Length);
            }
        }
    }

    internal static void AttacksFailClosed()
    {
        var fixture = new ProtocolFixture();
        var secretBytes = new byte[] { 3, 19, 37, 71, 109 };
        var frameBytes = new byte[0];
        try
        {
            using (var authenticator = new IpcEnvelopeAuthenticator(fixture.Key))
            using (var frame = new MemoryStream())
            using (var secret = new AgentHostCredentialSecret(secretBytes))
            {
                AgentCredentialDeliveryProtocol.WriteSingleFrame(
                    frame,
                    fixture.SessionId,
                    fixture.BootstrapId,
                    fixture.ProcessId,
                    fixture.ProcessCreationFileTime,
                    AgentCredentialDeliveryMode.AccessToken,
                    secret,
                    authenticator);
                frameBytes = frame.ToArray();
            }

            var tampered = (byte[])frameBytes.Clone();
            tampered[tampered.Length - 1] ^= 0x5a;
            ExpectCredentialFailure(fixture, tampered);
            Array.Clear(tampered, 0, tampered.Length);

            var truncated = new byte[frameBytes.Length - 1];
            Buffer.BlockCopy(frameBytes, 0, truncated, 0, truncated.Length);
            ExpectCredentialFailure(fixture, truncated);
            Array.Clear(truncated, 0, truncated.Length);

            var trailing = new byte[frameBytes.Length + 1];
            Buffer.BlockCopy(frameBytes, 0, trailing, 0, frameBytes.Length);
            trailing[trailing.Length - 1] = 0x7f;
            ExpectCredentialFailure(fixture, trailing);
            Array.Clear(trailing, 0, trailing.Length);

            using (var guard = new IpcSessionGuard(fixture.SessionId, fixture.Key))
            {
                using (var first = Read(fixture, frameBytes, guard))
                {
                    Equal(AgentCredentialDeliveryMode.AccessToken, first.Mode);
                }
                ExpectCredentialFailure(fixture, frameBytes, guard);
            }

            using (var wrongIdentityGuard = new IpcSessionGuard(fixture.SessionId, fixture.Key))
            using (var frame = new MemoryStream(frameBytes, false))
            {
                ExpectFailure(() => AgentCredentialDeliveryProtocol.ReadSingleFrame(
                    frame,
                    fixture.SessionId,
                    fixture.BootstrapId,
                    fixture.ProcessId + 1,
                    fixture.ProcessCreationFileTime,
                    wrongIdentityGuard));
            }
        }
        finally
        {
            fixture.Dispose();
            Array.Clear(secretBytes, 0, secretBytes.Length);
            if (frameBytes != null)
            {
                Array.Clear(frameBytes, 0, frameBytes.Length);
            }
        }
    }

    private static AgentCredentialDelivery Read(
        ProtocolFixture fixture,
        byte[] frameBytes,
        IpcSessionGuard guard)
    {
        using var frame = new MemoryStream(frameBytes, false);
        return AgentCredentialDeliveryProtocol.ReadSingleFrame(
            frame,
            fixture.SessionId,
            fixture.BootstrapId,
            fixture.ProcessId,
            fixture.ProcessCreationFileTime,
            guard);
    }

    private static void ExpectCredentialFailure(ProtocolFixture fixture, byte[] frameBytes)
    {
        using var guard = new IpcSessionGuard(fixture.SessionId, fixture.Key);
        ExpectCredentialFailure(fixture, frameBytes, guard);
    }

    private static void ExpectCredentialFailure(
        ProtocolFixture fixture,
        byte[] frameBytes,
        IpcSessionGuard guard)
    {
        using var frame = new MemoryStream(frameBytes, false);
        ExpectFailure(() => AgentCredentialDeliveryProtocol.ReadSingleFrame(
            frame,
            fixture.SessionId,
            fixture.BootstrapId,
            fixture.ProcessId,
            fixture.ProcessCreationFileTime,
            guard));
    }

    private static void ExpectFailure(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected credential delivery failure.");
        }
        catch (AgentBootstrapLaunchException exception)
        {
            Equal(AgentBootstrapLaunchFailure.CredentialUnavailable, exception.Failure);
            Equal("agenthost_credential_unavailable", exception.ErrorCode);
            Equal(
                "AgentBootstrapLaunchException: agenthost_credential_unavailable",
                exception.ToString());
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                "Expected " + expected + " but found " + actual + ".");
        }
    }

    private sealed class ProtocolFixture : IDisposable
    {
        internal ProtocolFixture()
        {
            BootstrapId = Enumerable.Range(1, AgentBootstrapProtocol.BootstrapIdSize)
                .Select(value => (byte)value)
                .ToArray();
            Key = Enumerable.Range(33, IpcSessionSecret.SizeInBytes)
                .Select(value => (byte)value)
                .ToArray();
        }

        internal string SessionId { get; } = "0123456789abcdef0123456789abcdef";

        internal byte[] BootstrapId { get; }

        internal byte[] Key { get; }

        internal int ProcessId { get; } = 4127;

        internal long ProcessCreationFileTime { get; } = 133999999999999999;

        public void Dispose()
        {
            Array.Clear(BootstrapId, 0, BootstrapId.Length);
            Array.Clear(Key, 0, Key.Length);
        }
    }
}
