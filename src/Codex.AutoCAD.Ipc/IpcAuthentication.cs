using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Ipc;

public static class IpcSessionSecret
{
    public const int SizeInBytes = 32;

    public static byte[] Generate()
    {
        return RandomNumberGenerator.GetBytes(SizeInBytes);
    }
}

public sealed class IpcEnvelopeAuthenticator
{
    private readonly byte[] _sessionSecret;

    public IpcEnvelopeAuthenticator(ReadOnlySpan<byte> sessionSecret)
    {
        if (sessionSecret.Length < IpcSessionSecret.SizeInBytes)
        {
            throw new ArgumentException("IPC会话密钥至少需要256位。", nameof(sessionSecret));
        }

        _sessionSecret = sessionSecret.ToArray();
    }

    public string Sign(IpcEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        using var hmac = new HMACSHA256(_sessionSecret);
        return Convert.ToHexString(hmac.ComputeHash(BuildCanonicalBytes(envelope)));
    }

    public bool Verify(IpcEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.Mac))
        {
            return false;
        }

        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(envelope.Mac);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(_sessionSecret);
        var expected = hmac.ComputeHash(BuildCanonicalBytes(envelope));
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static byte[] BuildCanonicalBytes(IpcEnvelope envelope)
    {
        var builder = new StringBuilder();
        Append(builder, envelope.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, envelope.MessageId);
        Append(builder, envelope.CorrelationId);
        Append(builder, envelope.SessionId);
        Append(builder, envelope.Sequence.ToString(CultureInfo.InvariantCulture));
        Append(builder, envelope.MessageType);
        Append(builder, envelope.PayloadJson);
        Append(builder, envelope.Nonce);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }
}

public enum IpcValidationCode
{
    Accepted = 0,
    InvalidProtocol = 1,
    InvalidSession = 2,
    InvalidSequence = 3,
    MissingNonce = 4,
    ReplayedNonce = 5,
    InvalidMac = 6,
    MessageTooLarge = 7
}

public sealed class IpcSessionGuard
{
    private readonly object _sync = new();
    private readonly string _sessionId;
    private readonly IpcEnvelopeAuthenticator _authenticator;
    private readonly HashSet<string> _usedNonces = new(StringComparer.Ordinal);
    private long _lastAcceptedSequence;

    public IpcSessionGuard(string sessionId, ReadOnlySpan<byte> sessionSecret)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId不能为空。", nameof(sessionId));
        }

        _sessionId = sessionId;
        _authenticator = new IpcEnvelopeAuthenticator(sessionSecret);
    }

    public IpcValidationCode ValidateAndAccept(IpcEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_sync)
        {
            if (envelope.ProtocolVersion != ProtocolConstants.CurrentVersion)
            {
                return IpcValidationCode.InvalidProtocol;
            }

            if (!string.Equals(envelope.SessionId, _sessionId, StringComparison.Ordinal))
            {
                return IpcValidationCode.InvalidSession;
            }

            if (Encoding.UTF8.GetByteCount(envelope.PayloadJson ?? string.Empty) > ProtocolConstants.MaximumMessageBytes)
            {
                return IpcValidationCode.MessageTooLarge;
            }

            if (envelope.Sequence != _lastAcceptedSequence + 1)
            {
                return IpcValidationCode.InvalidSequence;
            }

            if (string.IsNullOrWhiteSpace(envelope.Nonce))
            {
                return IpcValidationCode.MissingNonce;
            }

            if (_usedNonces.Contains(envelope.Nonce))
            {
                return IpcValidationCode.ReplayedNonce;
            }

            if (!_authenticator.Verify(envelope))
            {
                return IpcValidationCode.InvalidMac;
            }

            _usedNonces.Add(envelope.Nonce);
            _lastAcceptedSequence = envelope.Sequence;
            return IpcValidationCode.Accepted;
        }
    }
}
