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
        var secret = new byte[SizeInBytes];
        using (var random = RandomNumberGenerator.Create())
        {
            random.GetBytes(secret);
        }

        return secret;
    }
}

/// <summary>
/// Protocol v1 canonical encoding. Length prefixes are decimal UTF-16 code-unit counts
/// (the value returned by <see cref="string.Length"/>), while the completed canonical
/// string is encoded as strict UTF-8. This rule is frozen for net45/net8 interoperability.
/// </summary>
public static class IpcCanonicalEnvelopeEncoding
{
    public static byte[] GetBytes(IpcEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        var builder = new StringBuilder();
        Append(builder, envelope.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, RequireSignedString(envelope.MessageId, nameof(envelope.MessageId)));
        Append(builder, RequireSignedString(envelope.CorrelationId, nameof(envelope.CorrelationId)));
        Append(builder, RequireSignedString(envelope.SessionId, nameof(envelope.SessionId)));
        Append(builder, envelope.Sequence.ToString(CultureInfo.InvariantCulture));
        Append(builder, RequireSignedString(envelope.MessageType, nameof(envelope.MessageType)));
        Append(builder, RequireSignedString(envelope.PayloadJson, nameof(envelope.PayloadJson)));
        Append(builder, RequireSignedString(envelope.Nonce, nameof(envelope.Nonce)));
        return new UTF8Encoding(false, true).GetBytes(builder.ToString());
    }

    private static string RequireSignedString(string? value, string fieldName)
    {
        if (value is null)
        {
            throw new ArgumentException("IPC签名字段不能为null；可选字段必须使用显式空字符串。", fieldName);
        }

        return value;
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }
}

public sealed class IpcEnvelopeAuthenticator : IDisposable
{
    private const int Sha256Bytes = 32;
    private const int Sha256HexLength = Sha256Bytes * 2;
    private readonly object _sync = new object();
    private readonly byte[] _sessionSecret;
    private bool _disposed;

    public IpcEnvelopeAuthenticator(byte[] sessionSecret)
    {
        if (sessionSecret is null)
        {
            throw new ArgumentNullException(nameof(sessionSecret));
        }

        if (sessionSecret.Length != IpcSessionSecret.SizeInBytes)
        {
            throw new ArgumentException("IPC会话密钥必须恰好为256位。", nameof(sessionSecret));
        }

        _sessionSecret = (byte[])sessionSecret.Clone();
    }

    public string Sign(IpcEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            using (var hmac = new HMACSHA256(_sessionSecret))
            {
                return HexCodec.EncodeUpper(hmac.ComputeHash(IpcCanonicalEnvelopeEncoding.GetBytes(envelope)));
            }
        }
    }

    public bool Verify(IpcEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(envelope.Mac) || envelope.Mac.Length != Sha256HexLength)
            {
                return false;
            }

            var supplied = new byte[Sha256Bytes];
            if (!HexCodec.TryDecode(envelope.Mac, supplied))
            {
                return false;
            }

            try
            {
                using (var hmac = new HMACSHA256(_sessionSecret))
                {
                    var expected = hmac.ComputeHash(IpcCanonicalEnvelopeEncoding.GetBytes(envelope));
                    return FixedTime.Equals(supplied, expected);
                }
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            finally
            {
                Array.Clear(supplied, 0, supplied.Length);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            Array.Clear(_sessionSecret, 0, _sessionSecret.Length);
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(IpcEnvelopeAuthenticator));
        }
    }

    private static class HexCodec
    {
        private const string UpperHex = "0123456789ABCDEF";

        public static string EncodeUpper(byte[] bytes)
        {
            var characters = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                var value = bytes[index];
                characters[index * 2] = UpperHex[value >> 4];
                characters[(index * 2) + 1] = UpperHex[value & 0x0F];
            }

            return new string(characters);
        }

        public static bool TryDecode(string value, byte[] destination)
        {
            if (value.Length != destination.Length * 2)
            {
                return false;
            }

            for (var index = 0; index < destination.Length; index++)
            {
                var high = DecodeNibble(value[index * 2]);
                var low = DecodeNibble(value[(index * 2) + 1]);
                if (high < 0 || low < 0)
                {
                    Array.Clear(destination, 0, destination.Length);
                    return false;
                }

                destination[index] = (byte)((high << 4) | low);
            }

            return true;
        }

        private static int DecodeNibble(char character)
        {
            if (character >= '0' && character <= '9')
            {
                return character - '0';
            }

            if (character >= 'A' && character <= 'F')
            {
                return character - 'A' + 10;
            }

            if (character >= 'a' && character <= 'f')
            {
                return character - 'a' + 10;
            }

            return -1;
        }
    }

    private static class FixedTime
    {
        public static bool Equals(byte[] left, byte[] right)
        {
#if NET8_0_OR_GREATER
            return CryptographicOperations.FixedTimeEquals(left, right);
#else
            var difference = left.Length ^ right.Length;
            var count = Math.Min(left.Length, right.Length);
            for (var index = 0; index < count; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
#endif
        }
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
    MessageTooLarge = 7,
    NonceHistoryCapacityExceeded = 8,
    InvalidNonce = 9,
    InvalidMetadata = 10
}

public interface IIpcClock
{
    DateTimeOffset GetUtcNow();
}

public sealed class SystemIpcClock : IIpcClock
{
    public static readonly SystemIpcClock Instance = new SystemIpcClock();

    private SystemIpcClock()
    {
    }

    public DateTimeOffset GetUtcNow()
    {
        return DateTimeOffset.UtcNow;
    }
}

public sealed class IpcSessionGuardOptions
{
    public const int DefaultMaximumNonceHistoryEntries = 16 * 1024;
    public const int AbsoluteMaximumNonceHistoryEntries = 64 * 1024;
    public static readonly TimeSpan DefaultNonceRetention = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AbsoluteMaximumNonceRetention = TimeSpan.FromHours(1);

    public int MaximumNonceHistoryEntries { get; set; } = DefaultMaximumNonceHistoryEntries;

    public TimeSpan NonceRetention { get; set; } = DefaultNonceRetention;

    public IIpcClock Clock { get; set; } = SystemIpcClock.Instance;

    internal void Validate()
    {
        if (MaximumNonceHistoryEntries <= 0 || MaximumNonceHistoryEntries > AbsoluteMaximumNonceHistoryEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumNonceHistoryEntries),
                $"Nonce历史容量必须为1至{AbsoluteMaximumNonceHistoryEntries}。");
        }

        if (NonceRetention <= TimeSpan.Zero || NonceRetention > AbsoluteMaximumNonceRetention)
        {
            throw new ArgumentOutOfRangeException(
                nameof(NonceRetention),
                $"Nonce保留时间必须大于零且不超过{AbsoluteMaximumNonceRetention}。");
        }

        if (Clock is null)
        {
            throw new ArgumentNullException(nameof(Clock));
        }
    }
}

public sealed class IpcSessionGuard : IDisposable
{
    public const int MaximumIdentifierCharacters = 256;
    public const int MaximumNonceCharacters = 128;

    private readonly object _sync = new object();
    private readonly string _sessionId;
    private readonly IpcEnvelopeAuthenticator _authenticator;
    private readonly Dictionary<string, DateTimeOffset> _usedNonces =
        new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
    private readonly Queue<NonceHistoryEntry> _nonceExpirations = new Queue<NonceHistoryEntry>();
    private readonly int _maximumNonceHistoryEntries;
    private readonly TimeSpan _nonceRetention;
    private readonly IIpcClock _clock;
    private long _lastAcceptedSequence;
    private bool _disposed;

    public IpcSessionGuard(
        string sessionId,
        byte[] sessionSecret,
        IpcSessionGuardOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > MaximumIdentifierCharacters)
        {
            throw new ArgumentException($"SessionId不能为空且不能超过{MaximumIdentifierCharacters}个字符。", nameof(sessionId));
        }

        options ??= new IpcSessionGuardOptions();
        options.Validate();
        _sessionId = sessionId;
        _authenticator = new IpcEnvelopeAuthenticator(sessionSecret);
        _maximumNonceHistoryEntries = options.MaximumNonceHistoryEntries;
        _nonceRetention = options.NonceRetention;
        _clock = options.Clock;
    }

    public IpcValidationCode ValidateAndAccept(IpcEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            if (envelope.ProtocolVersion != ProtocolConstants.CurrentVersion)
            {
                return IpcValidationCode.InvalidProtocol;
            }

            if (!string.Equals(envelope.SessionId, _sessionId, StringComparison.Ordinal))
            {
                return IpcValidationCode.InvalidSession;
            }

            if (!IsRequiredIdentifier(envelope.MessageId)
                || !IsOptionalIdentifier(envelope.CorrelationId)
                || !IsRequiredIdentifier(envelope.MessageType)
                || envelope.PayloadJson is null)
            {
                return IpcValidationCode.InvalidMetadata;
            }

            try
            {
                if (new UTF8Encoding(false, true).GetByteCount(envelope.PayloadJson ?? string.Empty)
                    > ProtocolConstants.MaximumMessageBytes)
                {
                    return IpcValidationCode.MessageTooLarge;
                }
            }
            catch (EncoderFallbackException)
            {
                return IpcValidationCode.InvalidMetadata;
            }

            if (envelope.Sequence <= 0
                || _lastAcceptedSequence == long.MaxValue
                || envelope.Sequence != _lastAcceptedSequence + 1)
            {
                return IpcValidationCode.InvalidSequence;
            }

            if (string.IsNullOrWhiteSpace(envelope.Nonce))
            {
                return IpcValidationCode.MissingNonce;
            }

            if (envelope.Nonce.Length > MaximumNonceCharacters)
            {
                return IpcValidationCode.InvalidNonce;
            }

            var now = _clock.GetUtcNow();
            RemoveExpiredNonces(now);
            if (_usedNonces.ContainsKey(envelope.Nonce))
            {
                return IpcValidationCode.ReplayedNonce;
            }

            if (!_authenticator.Verify(envelope))
            {
                return IpcValidationCode.InvalidMac;
            }

            if (_usedNonces.Count >= _maximumNonceHistoryEntries)
            {
                return IpcValidationCode.NonceHistoryCapacityExceeded;
            }

            var expiresAt = now.Add(_nonceRetention);
            _usedNonces.Add(envelope.Nonce, expiresAt);
            _nonceExpirations.Enqueue(new NonceHistoryEntry(envelope.Nonce, expiresAt));
            _lastAcceptedSequence = envelope.Sequence;
            return IpcValidationCode.Accepted;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _authenticator.Dispose();
            _usedNonces.Clear();
            _nonceExpirations.Clear();
            _lastAcceptedSequence = 0;
            _disposed = true;
        }
    }

    private static bool IsRequiredIdentifier(string? value)
    {
        return value is not null
            && !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaximumIdentifierCharacters;
    }

    private static bool IsOptionalIdentifier(string? value)
    {
        return value is not null && value.Length <= MaximumIdentifierCharacters;
    }

    private void RemoveExpiredNonces(DateTimeOffset now)
    {
        while (_nonceExpirations.Count > 0)
        {
            var entry = _nonceExpirations.Peek();
            if (entry.ExpiresAt > now)
            {
                break;
            }

            _nonceExpirations.Dequeue();
            DateTimeOffset currentExpiration;
            if (_usedNonces.TryGetValue(entry.Nonce, out currentExpiration)
                && currentExpiration == entry.ExpiresAt)
            {
                _usedNonces.Remove(entry.Nonce);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(IpcSessionGuard));
        }
    }

    private sealed class NonceHistoryEntry
    {
        public NonceHistoryEntry(string nonce, DateTimeOffset expiresAt)
        {
            Nonce = nonce;
            ExpiresAt = expiresAt;
        }

        public string Nonce { get; }

        public DateTimeOffset ExpiresAt { get; }
    }
}
