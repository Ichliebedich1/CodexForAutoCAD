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

public sealed class IpcEnvelopeAuthenticator : IDisposable
{
    private const int Sha256HexLength = 64;
    private readonly object _sync = new();
    private readonly byte[] _sessionSecret;
    private bool _disposed;

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
        lock (_sync)
        {
            ThrowIfDisposed();
            using var hmac = new HMACSHA256(_sessionSecret);
            return Convert.ToHexString(hmac.ComputeHash(BuildCanonicalBytes(envelope)));
        }
    }

    public bool Verify(IpcEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(envelope.Mac) || envelope.Mac.Length != Sha256HexLength)
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
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(_sessionSecret);
            _disposed = true;
        }
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

public sealed class IpcSessionGuardOptions
{
    public const int DefaultMaximumNonceHistoryEntries = 16 * 1024;
    public const int AbsoluteMaximumNonceHistoryEntries = 64 * 1024;
    public static readonly TimeSpan DefaultNonceRetention = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AbsoluteMaximumNonceRetention = TimeSpan.FromHours(1);

    public int MaximumNonceHistoryEntries { get; init; } = DefaultMaximumNonceHistoryEntries;

    public TimeSpan NonceRetention { get; init; } = DefaultNonceRetention;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

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

        ArgumentNullException.ThrowIfNull(TimeProvider);
    }
}

public sealed class IpcSessionGuard : IDisposable
{
    public const int MaximumIdentifierCharacters = 256;
    public const int MaximumNonceCharacters = 128;

    private readonly object _sync = new();
    private readonly string _sessionId;
    private readonly IpcEnvelopeAuthenticator _authenticator;
    private readonly Dictionary<string, DateTimeOffset> _usedNonces = new(StringComparer.Ordinal);
    private readonly Queue<NonceHistoryEntry> _nonceExpirations = new();
    private readonly int _maximumNonceHistoryEntries;
    private readonly TimeSpan _nonceRetention;
    private readonly TimeProvider _timeProvider;
    private long _lastAcceptedSequence;
    private bool _disposed;

    public IpcSessionGuard(
        string sessionId,
        ReadOnlySpan<byte> sessionSecret,
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
        _timeProvider = options.TimeProvider;
    }

    public IpcValidationCode ValidateAndAccept(IpcEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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
                || !IsRequiredIdentifier(envelope.MessageType))
            {
                return IpcValidationCode.InvalidMetadata;
            }

            if (Encoding.UTF8.GetByteCount(envelope.PayloadJson ?? string.Empty) > ProtocolConstants.MaximumMessageBytes)
            {
                return IpcValidationCode.MessageTooLarge;
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

            var now = _timeProvider.GetUtcNow();
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
        return !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumIdentifierCharacters;
    }

    private static bool IsOptionalIdentifier(string? value)
    {
        return string.IsNullOrEmpty(value) || value.Length <= MaximumIdentifierCharacters;
    }

    private void RemoveExpiredNonces(DateTimeOffset now)
    {
        while (_nonceExpirations.TryPeek(out var entry) && entry.ExpiresAt <= now)
        {
            _nonceExpirations.Dequeue();
            if (_usedNonces.TryGetValue(entry.Nonce, out var currentExpiration)
                && currentExpiration == entry.ExpiresAt)
            {
                _usedNonces.Remove(entry.Nonce);
            }
        }
    }

    private readonly record struct NonceHistoryEntry(string Nonce, DateTimeOffset ExpiresAt);
}
