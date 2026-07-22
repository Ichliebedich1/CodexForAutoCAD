using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Codex.AutoCAD.Ipc;

public enum AgentBootstrapValidationCode
{
    InvalidMagic = 1,
    UnsupportedVersion = 2,
    UnknownFlags = 3,
    InvalidLength = 4,
    InvalidUtf8 = 5,
    InvalidSessionId = 6,
    InvalidPipeName = 7,
    InvalidBootstrapId = 8,
    InvalidSecret = 9,
    InvalidTag = 10,
    TruncatedFrame = 11,
    TrailingData = 12,
    AlreadyConsumed = 13,
    AuthenticationKeyReuse = 14,
    InvalidPayloadState = 15
}

internal enum AgentBootstrapEndpointRole
{
    Host = 1,
    Agent = 2
}

internal enum AgentBootstrapPayloadOrigin
{
    HostOutbound = 1,
    AgentInbound = 2
}

public sealed class AgentBootstrapException : IOException
{
    public AgentBootstrapException(AgentBootstrapValidationCode validationCode, string message)
        : base(message)
    {
        ValidationCode = validationCode;
    }

    public AgentBootstrapValidationCode ValidationCode { get; }
}

/// <summary>
/// Owns one bootstrap secret lifecycle. Random payloads are Host-origin outbound material and must
/// be written successfully exactly once before key derivation. Decoded payloads are Agent-origin
/// inbound material and can derive keys but can never be re-encoded or forwarded.
/// </summary>
public sealed class AgentBootstrapPayload : IDisposable
{
    private readonly object _sync = new object();
    private readonly AgentBootstrapPayloadOrigin _origin;
    private byte[] _bootstrapId;
    private byte[] _sessionSecret;
    private bool _writeStarted;
    private bool _writeCompleted;
    private bool _consumed;
    private bool _disposed;

    internal AgentBootstrapPayload(
        string sessionId,
        string pipeName,
        byte[] bootstrapId,
        byte[] sessionSecret,
        AgentBootstrapPayloadOrigin origin)
    {
        AgentBootstrapProtocol.ValidateSessionId(sessionId);
        AgentBootstrapProtocol.ValidatePipeName(pipeName);
        if (origin != AgentBootstrapPayloadOrigin.HostOutbound
            && origin != AgentBootstrapPayloadOrigin.AgentInbound)
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        byte[]? bootstrapIdCopy = null;
        byte[]? sessionSecretCopy = null;
        try
        {
            if (bootstrapId is null)
            {
                throw new ArgumentNullException(nameof(bootstrapId));
            }

            if (sessionSecret is null)
            {
                throw new ArgumentNullException(nameof(sessionSecret));
            }

            bootstrapIdCopy = (byte[])bootstrapId.Clone();
            sessionSecretCopy = (byte[])sessionSecret.Clone();
            AgentBootstrapProtocol.ValidateBootstrapId(bootstrapIdCopy);
            AgentBootstrapProtocol.ValidateSessionSecret(sessionSecretCopy);

            SessionId = sessionId;
            PipeName = pipeName;
            _origin = origin;
            _bootstrapId = bootstrapIdCopy;
            _sessionSecret = sessionSecretCopy;
            bootstrapIdCopy = null;
            sessionSecretCopy = null;
        }
        finally
        {
            AgentBootstrapProtocol.Clear(bootstrapIdCopy);
            AgentBootstrapProtocol.Clear(sessionSecretCopy);
        }
    }

    public string SessionId { get; }

    public string PipeName { get; }

    /// <summary>Creates fresh Host-origin material for one Agent bootstrap write.</summary>
    public static AgentBootstrapPayload CreateRandom(string sessionId, string pipeName)
    {
        AgentBootstrapProtocol.ValidateSessionId(sessionId);
        AgentBootstrapProtocol.ValidatePipeName(pipeName);

        var bootstrapId = new byte[AgentBootstrapProtocol.BootstrapIdSize];
        var sessionSecret = new byte[AgentBootstrapProtocol.SessionSecretSize];
        try
        {
            using (var random = RandomNumberGenerator.Create())
            {
                AgentBootstrapProtocol.FillNonZero(random, bootstrapId);
                AgentBootstrapProtocol.FillNonZero(random, sessionSecret);
            }

            return new AgentBootstrapPayload(
                sessionId,
                pipeName,
                bootstrapId,
                sessionSecret,
                AgentBootstrapPayloadOrigin.HostOutbound);
        }
        finally
        {
            AgentBootstrapProtocol.Clear(bootstrapId);
            AgentBootstrapProtocol.Clear(sessionSecret);
        }
    }

    public byte[] CopyBootstrapId()
    {
        lock (_sync)
        {
            ThrowIfUnavailable();
            return (byte[])_bootstrapId.Clone();
        }
    }

    /// <summary>
    /// Consumes the raw material and returns keys permanently bound to the payload origin's local
    /// endpoint role. Host-origin material requires a completed frame write first.
    /// </summary>
    public AgentBootstrapDirectionKeys DeriveDirectionKeys()
    {
        byte[] bootstrapId;
        byte[] sessionSecret;
        AgentBootstrapEndpointRole endpointRole;
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (_origin == AgentBootstrapPayloadOrigin.HostOutbound && !_writeCompleted)
            {
                throw new AgentBootstrapException(
                    AgentBootstrapValidationCode.InvalidPayloadState,
                    "Outbound bootstrap material cannot derive keys before a successful frame write.");
            }

            endpointRole = _origin == AgentBootstrapPayloadOrigin.HostOutbound
                ? AgentBootstrapEndpointRole.Host
                : AgentBootstrapEndpointRole.Agent;
            bootstrapId = _bootstrapId;
            sessionSecret = _sessionSecret;
            _bootstrapId = new byte[0];
            _sessionSecret = new byte[0];
            _consumed = true;
        }

        try
        {
            return AgentBootstrapProtocol.DeriveDirectionKeys(
                SessionId,
                PipeName,
                bootstrapId,
                sessionSecret,
                endpointRole);
        }
        finally
        {
            AgentBootstrapProtocol.Clear(bootstrapId);
            AgentBootstrapProtocol.Clear(sessionSecret);
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

            AgentBootstrapProtocol.Clear(_bootstrapId);
            AgentBootstrapProtocol.Clear(_sessionSecret);
            _bootstrapId = new byte[0];
            _sessionSecret = new byte[0];
            _consumed = true;
            _disposed = true;
        }
    }

    internal void BeginSingleFrameWrite(out byte[] bootstrapId, out byte[] sessionSecret)
    {
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (_origin != AgentBootstrapPayloadOrigin.HostOutbound)
            {
                AgentBootstrapProtocol.Clear(_bootstrapId);
                AgentBootstrapProtocol.Clear(_sessionSecret);
                _bootstrapId = new byte[0];
                _sessionSecret = new byte[0];
                _consumed = true;
                throw new AgentBootstrapException(
                    AgentBootstrapValidationCode.InvalidPayloadState,
                    "Inbound bootstrap material cannot be re-encoded or forwarded.");
            }

            if (_writeStarted)
            {
                throw new AgentBootstrapException(
                    AgentBootstrapValidationCode.AlreadyConsumed,
                    "Bootstrap payload has already been used for a frame write.");
            }

            _writeStarted = true;
            bootstrapId = new byte[0];
            sessionSecret = new byte[0];
            try
            {
                bootstrapId = (byte[])_bootstrapId.Clone();
                sessionSecret = (byte[])_sessionSecret.Clone();
            }
            catch
            {
                AgentBootstrapProtocol.Clear(bootstrapId);
                AgentBootstrapProtocol.Clear(sessionSecret);
                AgentBootstrapProtocol.Clear(_bootstrapId);
                AgentBootstrapProtocol.Clear(_sessionSecret);
                _bootstrapId = new byte[0];
                _sessionSecret = new byte[0];
                _consumed = true;
                throw;
            }
        }
    }

    internal void CompleteSingleFrameWrite()
    {
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (!_writeStarted || _writeCompleted)
            {
                throw new AgentBootstrapException(
                    AgentBootstrapValidationCode.AlreadyConsumed,
                    "Bootstrap frame write state is invalid.");
            }

            _writeCompleted = true;
        }
    }

    internal void FailSingleFrameWrite()
    {
        lock (_sync)
        {
            if (_disposed || _consumed || !_writeStarted || _writeCompleted)
            {
                return;
            }

            AgentBootstrapProtocol.Clear(_bootstrapId);
            AgentBootstrapProtocol.Clear(_sessionSecret);
            _bootstrapId = new byte[0];
            _sessionSecret = new byte[0];
            _consumed = true;
        }
    }

    private void ThrowIfUnavailable()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AgentBootstrapPayload));
        }

        if (_consumed)
        {
            throw new AgentBootstrapException(
                AgentBootstrapValidationCode.AlreadyConsumed,
                "Bootstrap payload has already been consumed.");
        }
    }
}

/// <summary>
/// Holds one endpoint-bound pair of direction keys. Each inbound guard and outbound authenticator
/// can be claimed only once; reconnecting requires a new bootstrap payload and session.
/// </summary>
public sealed class AgentBootstrapDirectionKeys : IDisposable
{
    private static readonly byte[] ConfirmationKeyDomain = Encoding.ASCII.GetBytes(
        "Codex.AutoCAD.AgentBootstrap.Confirmation.v1\0");
    private readonly object _sync = new object();
    private readonly string _sessionId;
    private readonly string _pipeName;
    private readonly AgentBootstrapEndpointRole _endpointRole;
    private byte[] _hostToAgentKey;
    private byte[] _agentToHostKey;
    private bool _outboundClaimed;
    private bool _inboundClaimed;
    private bool _confirmationOutboundClaimed;
    private bool _confirmationInboundClaimed;
    private bool _disposed;

    internal AgentBootstrapDirectionKeys(
        string sessionId,
        string pipeName,
        byte[] hostToAgentKey,
        byte[] agentToHostKey,
        AgentBootstrapEndpointRole endpointRole)
    {
        if (endpointRole != AgentBootstrapEndpointRole.Host
            && endpointRole != AgentBootstrapEndpointRole.Agent)
        {
            throw new ArgumentOutOfRangeException(nameof(endpointRole));
        }

        _sessionId = sessionId;
        _pipeName = pipeName;
        _endpointRole = endpointRole;

        byte[]? hostToAgentCopy = null;
        byte[]? agentToHostCopy = null;
        try
        {
            hostToAgentCopy = CopyAndValidateKey(hostToAgentKey, nameof(hostToAgentKey));
            agentToHostCopy = CopyAndValidateKey(agentToHostKey, nameof(agentToHostKey));
            _hostToAgentKey = hostToAgentCopy;
            _agentToHostKey = agentToHostCopy;
            hostToAgentCopy = null;
            agentToHostCopy = null;
        }
        finally
        {
            AgentBootstrapProtocol.Clear(hostToAgentCopy);
            AgentBootstrapProtocol.Clear(agentToHostCopy);
        }
    }

    public string SessionId => _sessionId;

    public string PipeName => _pipeName;

    public IpcEnvelopeAuthenticator CreateOutboundAuthenticator()
    {
        var key = ClaimDirectionalKey(outbound: true);
        try
        {
            return new IpcEnvelopeAuthenticator(key);
        }
        finally
        {
            AgentBootstrapProtocol.Clear(key);
        }
    }

    public IpcSessionGuard CreateInboundGuard(
        IpcSessionGuardOptions? options = null)
    {
        var key = ClaimDirectionalKey(outbound: false);
        try
        {
            return new IpcSessionGuard(_sessionId, key, options);
        }
        finally
        {
            AgentBootstrapProtocol.Clear(key);
        }
    }

    public IpcEnvelopeAuthenticator CreateConfirmationOutboundAuthenticator()
    {
        var key = ClaimConfirmationKey(outbound: true);
        try
        {
            return new IpcEnvelopeAuthenticator(key);
        }
        finally
        {
            AgentBootstrapProtocol.Clear(key);
        }
    }

    public IpcSessionGuard CreateConfirmationInboundGuard(
        IpcSessionGuardOptions? options = null)
    {
        var key = ClaimConfirmationKey(outbound: false);
        try
        {
            return new IpcSessionGuard(_sessionId, key, options);
        }
        finally
        {
            AgentBootstrapProtocol.Clear(key);
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

            AgentBootstrapProtocol.Clear(_hostToAgentKey);
            AgentBootstrapProtocol.Clear(_agentToHostKey);
            _hostToAgentKey = new byte[0];
            _agentToHostKey = new byte[0];
            _disposed = true;
        }
    }

    private byte[] ClaimDirectionalKey(bool outbound)
        => ClaimEndpointKey(outbound, confirmation: false);

    private byte[] ClaimConfirmationKey(bool outbound)
    {
        var directionKey = ClaimEndpointKey(outbound, confirmation: true);
        try
        {
            using (var hmac = new HMACSHA256(directionKey))
            {
                return hmac.ComputeHash(ConfirmationKeyDomain);
            }
        }
        finally
        {
            AgentBootstrapProtocol.Clear(directionKey);
        }
    }

    private byte[] ClaimEndpointKey(bool outbound, bool confirmation)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AgentBootstrapDirectionKeys));
            }

            var alreadyClaimed = confirmation
                ? outbound
                    ? _confirmationOutboundClaimed
                    : _confirmationInboundClaimed
                : outbound
                    ? _outboundClaimed
                    : _inboundClaimed;
            if (alreadyClaimed)
            {
                throw new AgentBootstrapException(
                    AgentBootstrapValidationCode.AlreadyConsumed,
                    "Bootstrap endpoint direction has already been claimed.");
            }

            if (confirmation && outbound)
            {
                _confirmationOutboundClaimed = true;
            }
            else if (confirmation)
            {
                _confirmationInboundClaimed = true;
            }
            else if (outbound)
            {
                _outboundClaimed = true;
            }
            else
            {
                _inboundClaimed = true;
            }

            var hostToAgent = _endpointRole == AgentBootstrapEndpointRole.Host
                ? outbound
                : !outbound;
            return (byte[])(hostToAgent ? _hostToAgentKey : _agentToHostKey).Clone();
        }
    }

    private static byte[] CopyAndValidateKey(byte[] key, string parameterName)
    {
        if (key is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var copy = (byte[])key.Clone();
        if (copy.Length != IpcSessionSecret.SizeInBytes || AgentBootstrapProtocol.IsAllZero(copy))
        {
            AgentBootstrapProtocol.Clear(copy);
            throw new ArgumentException("Derived direction key is invalid.", parameterName);
        }

        return copy;
    }
}

public static class AgentBootstrapProtocol
{
    public const ushort CurrentVersion = 1;
    public const ushort SupportedFlags = 0;
    public const int HeaderSize = 16;
    public const int BootstrapIdSize = 16;
    public const int SessionSecretSize = IpcSessionSecret.SizeInBytes;
    public const int AuthenticationKeySize = 32;
    public const int TagSize = 32;
    public const int SessionIdBytes = 32;
    public const int PipeNameBytes = 46;
    public const int MaximumBodyBytes = 164;

    private const int FixedBodyBytes = BootstrapIdSize + 6 + SessionSecretSize + TagSize;
    private const int ExpectedBodyBytes = FixedBodyBytes + SessionIdBytes + PipeNameBytes;
    private const string PipeNamePrefix = "codex-autocad-";
    private const int PipeNamePrefixLength = 14;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CDXCADB1");
    private static readonly byte[] TagDomain =
        Encoding.ASCII.GetBytes("Codex.AutoCAD.AgentBootstrap.Frame.v1\0");
    private static readonly byte[] DirectionDomain =
        Encoding.ASCII.GetBytes("Codex.AutoCAD.AgentBootstrap.Direction.v1\0");
    private static readonly byte[] HostToAgentLabel = Encoding.ASCII.GetBytes("host-to-agent");
    private static readonly byte[] AgentToHostLabel = Encoding.ASCII.GetBytes("agent-to-host");
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// Creates a one-use frame authentication key. The caller owns the returned array, must clear
    /// it after exactly one protocol call, and must deliver its peer copy outside command lines,
    /// environment variables, logs, and generally observable IPC.
    /// </summary>
    public static byte[] CreateAuthenticationKey()
    {
        var key = new byte[AuthenticationKeySize];
        var succeeded = false;
        try
        {
            using (var random = RandomNumberGenerator.Create())
            {
                FillNonZero(random, key);
            }

            succeeded = true;
            return key;
        }
        finally
        {
            if (!succeeded)
            {
                Clear(key);
            }
        }
    }

    /// <summary>
    /// Writes exactly one authenticated bootstrap frame. The frame carries the session secret in
    /// plaintext; HMAC authenticates it but does not provide confidentiality. The output therefore
    /// must be a dedicated, one-use, process-private channel such as an exclusively inherited
    /// handle, never a command line, environment variable, log, or generally observable IPC path.
    /// The supplied authentication key is always cleared before this method returns or throws. A
    /// payload permits only one write attempt; a failed write invalidates its raw secret so callers
    /// cannot retry an outcome that may already have been delivered. The launcher must close the
    /// write end after success and enforce its hard deadline outside the AutoCAD main thread.
    /// </summary>
    public static void WriteSingleFrameAndClearKey(
        Stream output,
        AgentBootstrapPayload payload,
        byte[] authenticationKey)
    {
        byte[]? keyCopy = null;
        byte[]? frame = null;
        byte[]? bootstrapId = null;
        byte[]? sessionSecret = null;
        var writeStarted = false;
        var writeCompleted = false;
        try
        {
            if (output is null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (!output.CanWrite)
            {
                throw new ArgumentException("Bootstrap output stream must be writable.", nameof(output));
            }

            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            keyCopy = CopyAndValidateAuthenticationKey(authenticationKey);
            payload.BeginSingleFrameWrite(out bootstrapId, out sessionSecret);
            writeStarted = true;
            frame = EncodeFrame(
                payload.SessionId,
                payload.PipeName,
                bootstrapId,
                sessionSecret,
                keyCopy);
            output.Write(frame, 0, frame.Length);
            output.Flush();
            payload.CompleteSingleFrameWrite();
            writeCompleted = true;
        }
        finally
        {
            Clear(frame);
            Clear(bootstrapId);
            Clear(sessionSecret);
            Clear(keyCopy);
            Clear(authenticationKey);
            if (writeStarted && !writeCompleted)
            {
                payload.FailSingleFrameWrite();
            }
        }
    }

    /// <summary>
    /// Reads one authenticated frame and requires EOF immediately after it. The input must be a
    /// dedicated confidential bootstrap channel because the frame contains a plaintext session
    /// secret. The supplied authentication key is always cleared before this method returns or
    /// throws. This blocking primitive must not run on the AutoCAD main thread.
    /// </summary>
    public static AgentBootstrapPayload ReadSingleFrameAndClearKey(
        Stream input,
        byte[] authenticationKey)
    {
        byte[]? keyCopy = null;
        var header = new byte[HeaderSize];
        byte[]? frame = null;
        AgentBootstrapPayload? payload = null;
        try
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (!input.CanRead)
            {
                throw new ArgumentException("Bootstrap input stream must be readable.", nameof(input));
            }

            keyCopy = CopyAndValidateAuthenticationKey(authenticationKey);
            ReadExact(input, header, 0, header.Length);
            var bodyLength = ValidateHeader(header);
            frame = new byte[checked(HeaderSize + bodyLength)];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            ReadExact(input, frame, HeaderSize, bodyLength);
            payload = DecodeFrameCore(frame, keyCopy);
            EnsureEndOfStream(input);
            var result = payload;
            payload = null;
            return result;
        }
        finally
        {
            if (payload is not null)
            {
                payload.Dispose();
            }
            Clear(header);
            Clear(frame);
            Clear(keyCopy);
            Clear(authenticationKey);
        }
    }

    /// <summary>
    /// Asynchronously reads one authenticated frame, requires EOF, and observes the supplied
    /// cancellation token when the underlying stream honors cancellation. The input must be a
    /// dedicated confidential bootstrap channel because the frame contains a plaintext session
    /// secret. The launcher must enforce a hard deadline by closing the bootstrap handle and
    /// terminating an unconfirmed child process; this protocol primitive alone does not provide
    /// that lifecycle guarantee and must not be awaited from the AutoCAD main thread.
    /// </summary>
    public static async Task<AgentBootstrapPayload> ReadSingleFrameAndClearKeyAsync(
        Stream input,
        byte[] authenticationKey,
        CancellationToken cancellationToken)
    {
        byte[]? keyCopy = null;
        var header = new byte[HeaderSize];
        byte[]? frame = null;
        AgentBootstrapPayload? payload = null;
        try
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (!input.CanRead)
            {
                throw new ArgumentException("Bootstrap input stream must be readable.", nameof(input));
            }

            keyCopy = CopyAndValidateAuthenticationKey(authenticationKey);
            await ReadExactAsync(input, header, 0, header.Length, cancellationToken)
                .ConfigureAwait(false);
            var bodyLength = ValidateHeader(header);
            frame = new byte[checked(HeaderSize + bodyLength)];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            await ReadExactAsync(input, frame, HeaderSize, bodyLength, cancellationToken)
                .ConfigureAwait(false);
            payload = DecodeFrameCore(frame, keyCopy);
            await EnsureEndOfStreamAsync(input, cancellationToken).ConfigureAwait(false);
            var result = payload;
            payload = null;
            return result;
        }
        finally
        {
            if (payload is not null)
            {
                payload.Dispose();
            }
            Clear(header);
            Clear(frame);
            Clear(keyCopy);
            Clear(authenticationKey);
        }
    }

    /// <summary>
    /// Decodes a single in-memory frame. The caller must provide exclusive ownership of both arrays
    /// for the entire call; concurrent mutation would violate the authentication/parse boundary.
    /// Both the caller-owned frame and authentication key are always cleared before this method
    /// returns or throws. The frame contains a plaintext session secret and must never be logged.
    /// </summary>
    public static AgentBootstrapPayload DecodeSingleFrameAndClear(
        byte[] frame,
        byte[] authenticationKey)
    {
        byte[]? keyCopy = null;
        byte[]? frameCopy = null;
        try
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            frameCopy = (byte[])frame.Clone();
            keyCopy = CopyAndValidateAuthenticationKey(authenticationKey);
            return DecodeFrameCore(frameCopy, keyCopy);
        }
        finally
        {
            Clear(frame);
            Clear(frameCopy);
            Clear(keyCopy);
            Clear(authenticationKey);
        }
    }

    internal static AgentBootstrapDirectionKeys DeriveDirectionKeys(
        string sessionId,
        string pipeName,
        byte[] bootstrapId,
        byte[] sessionSecret,
        AgentBootstrapEndpointRole endpointRole)
    {
        ValidateSessionId(sessionId);
        ValidatePipeName(pipeName);
        ValidateBootstrapId(bootstrapId);
        ValidateSessionSecret(sessionSecret);

        var sessionBytes = StrictUtf8.GetBytes(sessionId);
        var pipeBytes = StrictUtf8.GetBytes(pipeName);
        byte[]? hostToAgent = null;
        byte[]? agentToHost = null;
        try
        {
            hostToAgent = DeriveDirectionKey(
                sessionSecret,
                bootstrapId,
                sessionBytes,
                pipeBytes,
                HostToAgentLabel);
            agentToHost = DeriveDirectionKey(
                sessionSecret,
                bootstrapId,
                sessionBytes,
                pipeBytes,
                AgentToHostLabel);
            return new AgentBootstrapDirectionKeys(
                sessionId,
                pipeName,
                hostToAgent,
                agentToHost,
                endpointRole);
        }
        finally
        {
            Clear(sessionBytes);
            Clear(pipeBytes);
            Clear(hostToAgent);
            Clear(agentToHost);
        }
    }

    internal static void ValidateSessionId(string sessionId)
    {
        if (sessionId is null)
        {
            throw Failure(AgentBootstrapValidationCode.InvalidSessionId, "Bootstrap session id is invalid.");
        }

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(sessionId);
        }
        catch (EncoderFallbackException)
        {
            throw Failure(AgentBootstrapValidationCode.InvalidUtf8, "Bootstrap session id is not strict UTF-8.");
        }

        if (sessionId.Length != SessionIdBytes || byteCount != SessionIdBytes)
        {
            throw Failure(AgentBootstrapValidationCode.InvalidSessionId, "Bootstrap session id is invalid.");
        }

        for (var index = 0; index < sessionId.Length; index++)
        {
            if (!IsLowerHexCharacter(sessionId[index]))
            {
                throw Failure(AgentBootstrapValidationCode.InvalidSessionId, "Bootstrap session id is invalid.");
            }
        }
    }

    internal static void ValidatePipeName(string pipeName)
    {
        if (pipeName is null)
        {
            throw Failure(AgentBootstrapValidationCode.InvalidPipeName, "Bootstrap pipe name is invalid.");
        }

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(pipeName);
        }
        catch (EncoderFallbackException)
        {
            throw Failure(AgentBootstrapValidationCode.InvalidUtf8, "Bootstrap pipe name is not strict UTF-8.");
        }

        if (pipeName.Length != PipeNameBytes
            || byteCount != PipeNameBytes
            || !pipeName.StartsWith(PipeNamePrefix, StringComparison.Ordinal))
        {
            throw Failure(AgentBootstrapValidationCode.InvalidPipeName, "Bootstrap pipe name is invalid.");
        }

        for (var index = PipeNamePrefixLength; index < pipeName.Length; index++)
        {
            if (!IsLowerHexCharacter(pipeName[index]))
            {
                throw Failure(AgentBootstrapValidationCode.InvalidPipeName, "Bootstrap pipe name is invalid.");
            }
        }
    }

    internal static void ValidateBootstrapId(byte[] bootstrapId)
    {
        if (bootstrapId is null || bootstrapId.Length != BootstrapIdSize || IsAllZero(bootstrapId))
        {
            throw Failure(AgentBootstrapValidationCode.InvalidBootstrapId, "Bootstrap id is invalid.");
        }
    }

    internal static void ValidateSessionSecret(byte[] sessionSecret)
    {
        if (sessionSecret is null
            || sessionSecret.Length != SessionSecretSize
            || IsAllZero(sessionSecret))
        {
            throw Failure(AgentBootstrapValidationCode.InvalidSecret, "Bootstrap secret is invalid.");
        }
    }

    internal static bool IsAllZero(byte[] bytes)
    {
        var combined = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            combined |= bytes[index];
        }

        return combined == 0;
    }

    internal static void FillNonZero(RandomNumberGenerator random, byte[] bytes)
    {
        do
        {
            random.GetBytes(bytes);
        }
        while (IsAllZero(bytes));
    }

    internal static void Clear(byte[]? bytes)
    {
        if (bytes is not null)
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static AgentBootstrapPayload DecodeFrameCore(byte[] frame, byte[] authenticationKey)
    {
        if (frame.Length < HeaderSize)
        {
            throw Failure(AgentBootstrapValidationCode.TruncatedFrame, "Bootstrap frame is truncated.");
        }

        var bodyLength = ValidateHeader(frame);
        var expectedFrameLength = checked(HeaderSize + bodyLength);
        if (frame.Length < expectedFrameLength)
        {
            throw Failure(AgentBootstrapValidationCode.TruncatedFrame, "Bootstrap frame is truncated.");
        }

        if (frame.Length > expectedFrameLength)
        {
            throw Failure(AgentBootstrapValidationCode.TrailingData, "Bootstrap frame has trailing data.");
        }

        var tagOffset = expectedFrameLength - TagSize;
        var suppliedTag = new byte[TagSize];
        byte[]? expectedTag = null;
        try
        {
            Buffer.BlockCopy(frame, tagOffset, suppliedTag, 0, suppliedTag.Length);
            expectedTag = ComputeFrameTag(authenticationKey, frame, tagOffset);
            if (!FixedTimeEquals(expectedTag, suppliedTag))
            {
                throw Failure(AgentBootstrapValidationCode.InvalidTag, "Bootstrap frame authentication failed.");
            }
        }
        finally
        {
            Clear(suppliedTag);
            Clear(expectedTag);
        }

        var offset = HeaderSize;
        var bootstrapId = new byte[BootstrapIdSize];
        byte[]? sessionBytes = null;
        byte[]? pipeBytes = null;
        var sessionSecret = new byte[SessionSecretSize];
        try
        {
            Buffer.BlockCopy(frame, offset, bootstrapId, 0, bootstrapId.Length);
            offset += bootstrapId.Length;

            var sessionLength = ReadUInt16(frame, offset);
            offset += 2;
            var pipeLength = ReadUInt16(frame, offset);
            offset += 2;
            var secretLength = ReadUInt16(frame, offset);
            offset += 2;
            if (sessionLength != SessionIdBytes
                || pipeLength != PipeNameBytes
                || secretLength != SessionSecretSize)
            {
                throw Failure(AgentBootstrapValidationCode.InvalidLength, "Bootstrap field length is invalid.");
            }

            var expectedSecretOffset = checked(offset + sessionLength + pipeLength);
            if (expectedSecretOffset + SessionSecretSize != tagOffset)
            {
                throw Failure(AgentBootstrapValidationCode.InvalidLength, "Bootstrap field lengths do not match the frame.");
            }

            sessionBytes = new byte[sessionLength];
            Buffer.BlockCopy(frame, offset, sessionBytes, 0, sessionBytes.Length);
            offset += sessionBytes.Length;
            pipeBytes = new byte[pipeLength];
            Buffer.BlockCopy(frame, offset, pipeBytes, 0, pipeBytes.Length);
            offset += pipeBytes.Length;
            Buffer.BlockCopy(frame, offset, sessionSecret, 0, sessionSecret.Length);

            ValidateBootstrapId(bootstrapId);
            ValidateSessionSecret(sessionSecret);
            if (FixedTimeEquals(authenticationKey, sessionSecret))
            {
                throw Failure(
                    AgentBootstrapValidationCode.AuthenticationKeyReuse,
                    "Bootstrap authentication and session keys must be independent.");
            }

            string sessionId;
            string pipeName;
            try
            {
                sessionId = StrictUtf8.GetString(sessionBytes);
                pipeName = StrictUtf8.GetString(pipeBytes);
            }
            catch (DecoderFallbackException)
            {
                throw Failure(AgentBootstrapValidationCode.InvalidUtf8, "Bootstrap identifiers are not strict UTF-8.");
            }

            ValidateSessionId(sessionId);
            ValidatePipeName(pipeName);
            return new AgentBootstrapPayload(
                sessionId,
                pipeName,
                bootstrapId,
                sessionSecret,
                AgentBootstrapPayloadOrigin.AgentInbound);
        }
        finally
        {
            Clear(bootstrapId);
            Clear(sessionSecret);
            Clear(sessionBytes);
            Clear(pipeBytes);
        }
    }

    private static byte[] EncodeFrame(
        string sessionId,
        string pipeName,
        byte[] bootstrapId,
        byte[] sessionSecret,
        byte[] authenticationKey)
    {
        ValidateSessionId(sessionId);
        ValidatePipeName(pipeName);
        ValidateBootstrapId(bootstrapId);
        ValidateSessionSecret(sessionSecret);

        var sessionBytes = StrictUtf8.GetBytes(sessionId);
        var pipeBytes = StrictUtf8.GetBytes(pipeName);
        byte[]? frame = null;
        byte[]? tag = null;
        var succeeded = false;
        try
        {
            if (FixedTimeEquals(authenticationKey, sessionSecret))
            {
                throw Failure(
                    AgentBootstrapValidationCode.AuthenticationKeyReuse,
                    "Bootstrap authentication and session keys must be independent.");
            }

            var bodyLength = checked(FixedBodyBytes + sessionBytes.Length + pipeBytes.Length);
            if (bodyLength != ExpectedBodyBytes || bodyLength != MaximumBodyBytes)
            {
                throw Failure(AgentBootstrapValidationCode.InvalidLength, "Bootstrap frame body is invalid.");
            }

            frame = new byte[checked(HeaderSize + bodyLength)];
            Buffer.BlockCopy(Magic, 0, frame, 0, Magic.Length);
            WriteUInt16(frame, 8, CurrentVersion);
            WriteUInt16(frame, 10, SupportedFlags);
            WriteUInt32(frame, 12, (uint)bodyLength);

            var offset = HeaderSize;
            Buffer.BlockCopy(bootstrapId, 0, frame, offset, bootstrapId.Length);
            offset += bootstrapId.Length;
            WriteUInt16(frame, offset, sessionBytes.Length);
            offset += 2;
            WriteUInt16(frame, offset, pipeBytes.Length);
            offset += 2;
            WriteUInt16(frame, offset, SessionSecretSize);
            offset += 2;
            Buffer.BlockCopy(sessionBytes, 0, frame, offset, sessionBytes.Length);
            offset += sessionBytes.Length;
            Buffer.BlockCopy(pipeBytes, 0, frame, offset, pipeBytes.Length);
            offset += pipeBytes.Length;
            Buffer.BlockCopy(sessionSecret, 0, frame, offset, sessionSecret.Length);
            offset += sessionSecret.Length;

            tag = ComputeFrameTag(authenticationKey, frame, offset);
            Buffer.BlockCopy(tag, 0, frame, offset, tag.Length);
            succeeded = true;
            return frame;
        }
        finally
        {
            Clear(sessionBytes);
            Clear(pipeBytes);
            Clear(tag);
            if (!succeeded)
            {
                Clear(frame);
            }
        }
    }

    private static int ValidateHeader(byte[] frame)
    {
        if (frame.Length < HeaderSize)
        {
            throw Failure(AgentBootstrapValidationCode.TruncatedFrame, "Bootstrap header is truncated.");
        }

        for (var index = 0; index < Magic.Length; index++)
        {
            if (frame[index] != Magic[index])
            {
                throw Failure(AgentBootstrapValidationCode.InvalidMagic, "Bootstrap magic is invalid.");
            }
        }

        if (ReadUInt16(frame, 8) != CurrentVersion)
        {
            throw Failure(AgentBootstrapValidationCode.UnsupportedVersion, "Bootstrap version is unsupported.");
        }

        if (ReadUInt16(frame, 10) != SupportedFlags)
        {
            throw Failure(AgentBootstrapValidationCode.UnknownFlags, "Bootstrap flags are unsupported.");
        }

        var bodyLength = ReadUInt32(frame, 12);
        if (bodyLength != ExpectedBodyBytes || bodyLength != MaximumBodyBytes)
        {
            throw Failure(AgentBootstrapValidationCode.InvalidLength, "Bootstrap body length is invalid.");
        }

        return checked((int)bodyLength);
    }

    private static byte[] CopyAndValidateAuthenticationKey(byte[] authenticationKey)
    {
        if (authenticationKey is null)
        {
            throw new ArgumentNullException(nameof(authenticationKey));
        }

        var copy = (byte[])authenticationKey.Clone();
        if (copy.Length != AuthenticationKeySize || IsAllZero(copy))
        {
            Clear(copy);
            throw new ArgumentException(
                "Bootstrap authentication key must be a non-zero 256-bit value.",
                nameof(authenticationKey));
        }

        return copy;
    }

    private static byte[] ComputeFrameTag(
        byte[] authenticationKey,
        byte[] frame,
        int frameBytesToAuthenticate)
    {
        var input = new byte[checked(TagDomain.Length + frameBytesToAuthenticate)];
        try
        {
            Buffer.BlockCopy(TagDomain, 0, input, 0, TagDomain.Length);
            Buffer.BlockCopy(frame, 0, input, TagDomain.Length, frameBytesToAuthenticate);
            using (var hmac = new HMACSHA256(authenticationKey))
            {
                return hmac.ComputeHash(input);
            }
        }
        finally
        {
            Clear(input);
        }
    }

    private static byte[] DeriveDirectionKey(
        byte[] sessionSecret,
        byte[] bootstrapId,
        byte[] sessionBytes,
        byte[] pipeBytes,
        byte[] roleLabel)
    {
        var input = new byte[checked(
            DirectionDomain.Length
            + 2
            + 2 + roleLabel.Length
            + BootstrapIdSize
            + 2 + sessionBytes.Length
            + 2 + pipeBytes.Length)];
        try
        {
            var offset = 0;
            Buffer.BlockCopy(DirectionDomain, 0, input, offset, DirectionDomain.Length);
            offset += DirectionDomain.Length;
            WriteUInt16(input, offset, CurrentVersion);
            offset += 2;
            WriteUInt16(input, offset, roleLabel.Length);
            offset += 2;
            Buffer.BlockCopy(roleLabel, 0, input, offset, roleLabel.Length);
            offset += roleLabel.Length;
            Buffer.BlockCopy(bootstrapId, 0, input, offset, bootstrapId.Length);
            offset += bootstrapId.Length;
            WriteUInt16(input, offset, sessionBytes.Length);
            offset += 2;
            Buffer.BlockCopy(sessionBytes, 0, input, offset, sessionBytes.Length);
            offset += sessionBytes.Length;
            WriteUInt16(input, offset, pipeBytes.Length);
            offset += 2;
            Buffer.BlockCopy(pipeBytes, 0, input, offset, pipeBytes.Length);
            using (var hmac = new HMACSHA256(sessionSecret))
            {
                return hmac.ComputeHash(input);
            }
        }
        finally
        {
            Clear(input);
        }
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
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

    private static void EnsureEndOfStream(Stream input)
    {
        if (input.ReadByte() >= 0)
        {
            throw Failure(AgentBootstrapValidationCode.TrailingData, "Bootstrap stream has trailing data.");
        }
    }

    private static async Task EnsureEndOfStreamAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        var trailing = new byte[1];
        try
        {
            var read = await input.ReadAsync(trailing, 0, 1, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                throw Failure(AgentBootstrapValidationCode.TrailingData, "Bootstrap stream has trailing data.");
            }
        }
        finally
        {
            Clear(trailing);
        }
    }

    private static void ReadExact(Stream input, byte[] buffer, int offset, int count)
    {
        var remaining = count;
        while (remaining > 0)
        {
            var read = input.Read(buffer, offset, remaining);
            if (read <= 0)
            {
                throw Failure(
                    AgentBootstrapValidationCode.TruncatedFrame,
                    "Bootstrap stream ended before the frame completed.");
            }

            offset += read;
            remaining -= read;
        }
    }

    private static async Task ReadExactAsync(
        Stream input,
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var remaining = count;
        while (remaining > 0)
        {
            var read = await input.ReadAsync(buffer, offset, remaining, cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0)
            {
                throw Failure(
                    AgentBootstrapValidationCode.TruncatedFrame,
                    "Bootstrap stream ended before the frame completed.");
            }

            offset += read;
            remaining -= read;
        }
    }

    private static bool IsLowerHexCharacter(char character)
    {
        return (character >= '0' && character <= '9')
            || (character >= 'a' && character <= 'f');
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return (uint)(bytes[offset]
            | (bytes[offset + 1] << 8)
            | (bytes[offset + 2] << 16)
            | (bytes[offset + 3] << 24));
    }

    private static void WriteUInt16(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static AgentBootstrapException Failure(
        AgentBootstrapValidationCode validationCode,
        string message)
    {
        return new AgentBootstrapException(validationCode, message);
    }
}
