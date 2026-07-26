using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.AgentLauncher;

public enum AgentCredentialDeliveryMode
{
    Disabled = 0,
    AccessToken = 1,
}

public sealed class AgentCredentialDelivery : IDisposable
{
    internal AgentCredentialDelivery(
        AgentCredentialDeliveryMode mode,
        AgentHostCredentialSecret? secret)
    {
        Mode = mode;
        Secret = secret;
    }

    public AgentCredentialDeliveryMode Mode { get; }

    public AgentHostCredentialSecret? Secret { get; private set; }

    public void Dispose()
    {
        var secret = Secret;
        Secret = null;
        secret?.Dispose();
    }
}

/// <summary>
/// One-use, bootstrap-bound credential delivery. Authenticated metadata contains only identity,
/// length, and a digest; credential bytes remain binary and never enter an IPC string.
/// </summary>
public static class AgentCredentialDeliveryProtocol
{
    public const ushort CurrentVersion = 1;
    public const int MaximumCredentialBytes = 4 * 1024;
    public const int MaximumMetadataBytes = 2 * 1024;
    public const string MessageType = "agent.credential.delivery";

    private const int HeaderSize = 20;
    private const int NonceBytes = 16;
    private const string PayloadPrefix = "{\"bootstrapId\":\"";
    private const string ProcessIdMarker = "\",\"processId\":";
    private const string CreationTimeMarker = ",\"processCreationFileTime\":";
    private const string ModeMarker = ",\"mode\":";
    private const string LengthMarker = ",\"credentialLength\":";
    private const string DigestMarker = ",\"credentialSha256\":\"";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CDXCACR1");

    public static void WriteSingleFrame(
        Stream output,
        string sessionId,
        byte[] bootstrapId,
        int processId,
        long processCreationFileTime,
        AgentCredentialDeliveryMode mode,
        AgentHostCredentialSecret? secret,
        IpcEnvelopeAuthenticator authenticator)
    {
        if (output == null || !output.CanWrite)
        {
            throw Failure("Credential output is unavailable.");
        }
        if (authenticator == null)
        {
            throw new ArgumentNullException(nameof(authenticator));
        }
        ValidateIdentity(sessionId, bootstrapId, processId, processCreationFileTime);
        ValidateModeAndSecret(mode, secret);

        if (secret == null)
        {
            WriteCore(
                output,
                sessionId,
                bootstrapId,
                processId,
                processCreationFileTime,
                mode,
                new byte[0],
                authenticator);
            return;
        }

        secret.UseBytes(bytes =>
        {
            WriteCore(
                output,
                sessionId,
                bootstrapId,
                processId,
                processCreationFileTime,
                mode,
                bytes,
                authenticator);
            return 0;
        });
    }

    public static AgentCredentialDelivery ReadSingleFrame(
        Stream input,
        string expectedSessionId,
        byte[] expectedBootstrapId,
        int expectedProcessId,
        long expectedProcessCreationFileTime,
        IpcSessionGuard incomingGuard)
    {
        if (input == null || !input.CanRead)
        {
            throw Failure("Credential input is unavailable.");
        }
        if (incomingGuard == null)
        {
            throw new ArgumentNullException(nameof(incomingGuard));
        }
        ValidateIdentity(
            expectedSessionId,
            expectedBootstrapId,
            expectedProcessId,
            expectedProcessCreationFileTime);

        var header = new byte[HeaderSize];
        byte[]? metadata = null;
        byte[]? credentialBytes = null;
        try
        {
            ReadExact(input, header, 0, header.Length);
            ValidateHeader(header, out var metadataLength, out var credentialLength);
            metadata = new byte[metadataLength];
            credentialBytes = new byte[credentialLength];
            ReadExact(input, metadata, 0, metadata.Length);
            ReadExact(input, credentialBytes, 0, credentialBytes.Length);
            EnsureEndOfStream(input);

            IpcEnvelope envelope;
            using (var metadataStream = new MemoryStream(metadata, false))
            {
                envelope = AgentBootstrapConfirmationProtocol.ReadSingleFrame(metadataStream);
            }
            if (incomingGuard.ValidateAndAccept(Snapshot(envelope)) != IpcValidationCode.Accepted)
            {
                throw Failure("Credential metadata authentication failed.");
            }

            var expectedBootstrapIdHex =
                AgentBootstrapConfirmationProtocol.FormatLowerHex(expectedBootstrapId);
            if (envelope.ProtocolVersion != ProtocolConstants.CurrentVersion
                || envelope.Sequence != 1
                || !string.Equals(envelope.MessageType, MessageType, StringComparison.Ordinal)
                || !string.Equals(envelope.SessionId, expectedSessionId, StringComparison.Ordinal)
                || !string.Equals(envelope.MessageId, expectedBootstrapIdHex, StringComparison.Ordinal)
                || !string.IsNullOrEmpty(envelope.CorrelationId))
            {
                throw Failure("Credential metadata identity is invalid.");
            }

            ParsePayload(
                envelope.PayloadJson,
                out var actualBootstrapId,
                out var actualProcessId,
                out var actualCreationTime,
                out var mode,
                out var declaredLength,
                out var declaredDigest);
            if (!string.Equals(actualBootstrapId, expectedBootstrapIdHex, StringComparison.Ordinal)
                || actualProcessId != expectedProcessId
                || actualCreationTime != expectedProcessCreationFileTime
                || declaredLength != credentialBytes.Length)
            {
                Clear(declaredDigest);
                throw Failure("Credential metadata does not match the bootstrap identity.");
            }

            ValidateModeAndLength(mode, credentialBytes.Length);
            var actualDigest = ComputeSha256(credentialBytes);
            try
            {
                if (!FixedTimeEquals(actualDigest, declaredDigest))
                {
                    throw Failure("Credential payload digest is invalid.");
                }
            }
            finally
            {
                Clear(actualDigest);
                Clear(declaredDigest);
            }

            if (mode == AgentCredentialDeliveryMode.Disabled)
            {
                return new AgentCredentialDelivery(mode, null);
            }

            var secret = new AgentHostCredentialSecret(credentialBytes);
            credentialBytes = null;
            return new AgentCredentialDelivery(mode, secret);
        }
        catch (AgentBootstrapLaunchException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure("Credential frame is invalid.", exception);
        }
        finally
        {
            Clear(header);
            Clear(metadata);
            Clear(credentialBytes);
        }
    }

    private static void WriteCore(
        Stream output,
        string sessionId,
        byte[] bootstrapId,
        int processId,
        long processCreationFileTime,
        AgentCredentialDeliveryMode mode,
        byte[] credentialBytes,
        IpcEnvelopeAuthenticator authenticator)
    {
        ValidateModeAndLength(mode, credentialBytes.Length);
        var digest = ComputeSha256(credentialBytes);
        var nonce = new byte[NonceBytes];
        byte[]? metadata = null;
        var header = new byte[HeaderSize];
        try
        {
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }
            var bootstrapIdHex =
                AgentBootstrapConfirmationProtocol.FormatLowerHex(bootstrapId);
            var envelope = new IpcEnvelope
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                MessageId = bootstrapIdHex,
                CorrelationId = string.Empty,
                SessionId = sessionId,
                Sequence = 1,
                MessageType = MessageType,
                PayloadJson = PayloadPrefix
                    + bootstrapIdHex
                    + ProcessIdMarker
                    + processId.ToString(CultureInfo.InvariantCulture)
                    + CreationTimeMarker
                    + processCreationFileTime.ToString(CultureInfo.InvariantCulture)
                    + ModeMarker
                    + ((int)mode).ToString(CultureInfo.InvariantCulture)
                    + LengthMarker
                    + credentialBytes.Length.ToString(CultureInfo.InvariantCulture)
                    + DigestMarker
                    + FormatLowerHex(digest)
                    + "\"}",
                Nonce = FormatLowerHex(nonce),
            };
            envelope.Mac = authenticator.Sign(envelope);

            using (var metadataStream = new MemoryStream())
            {
                AgentBootstrapConfirmationProtocol.WriteSingleFrame(metadataStream, envelope);
                metadata = metadataStream.ToArray();
            }
            if (metadata.Length <= 0 || metadata.Length > MaximumMetadataBytes)
            {
                throw Failure("Credential metadata length is invalid.");
            }

            Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
            WriteUInt16(header, 8, CurrentVersion);
            WriteUInt16(header, 10, 0);
            WriteInt32(header, 12, metadata.Length);
            WriteInt32(header, 16, credentialBytes.Length);
            output.Write(header, 0, header.Length);
            output.Write(metadata, 0, metadata.Length);
            output.Write(credentialBytes, 0, credentialBytes.Length);
            output.Flush();
        }
        catch (AgentBootstrapLaunchException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure("Credential frame could not be written.", exception);
        }
        finally
        {
            Clear(header);
            Clear(digest);
            Clear(nonce);
            Clear(metadata);
        }
    }

    private static void ValidateHeader(
        byte[] header,
        out int metadataLength,
        out int credentialLength)
    {
        for (var index = 0; index < Magic.Length; index++)
        {
            if (header[index] != Magic[index])
            {
                throw Failure("Credential frame magic is invalid.");
            }
        }
        if (ReadUInt16(header, 8) != CurrentVersion || ReadUInt16(header, 10) != 0)
        {
            throw Failure("Credential frame version or flags are invalid.");
        }
        metadataLength = ReadInt32(header, 12);
        credentialLength = ReadInt32(header, 16);
        if (metadataLength <= 0
            || metadataLength > MaximumMetadataBytes
            || credentialLength < 0
            || credentialLength > MaximumCredentialBytes)
        {
            throw Failure("Credential frame length is invalid.");
        }
    }

    private static void ValidateIdentity(
        string sessionId,
        byte[] bootstrapId,
        int processId,
        long processCreationFileTime)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || bootstrapId == null
            || bootstrapId.Length != AgentBootstrapProtocol.BootstrapIdSize
            || processId <= 0
            || processCreationFileTime <= 0)
        {
            throw Failure("Credential bootstrap identity is invalid.");
        }
    }

    private static void ValidateModeAndSecret(
        AgentCredentialDeliveryMode mode,
        AgentHostCredentialSecret? secret)
    {
        if ((mode == AgentCredentialDeliveryMode.Disabled && secret != null)
            || (mode == AgentCredentialDeliveryMode.AccessToken && secret == null)
            || (mode != AgentCredentialDeliveryMode.Disabled
                && mode != AgentCredentialDeliveryMode.AccessToken))
        {
            throw Failure("Credential delivery mode is invalid.");
        }
    }

    private static void ValidateModeAndLength(
        AgentCredentialDeliveryMode mode,
        int credentialLength)
    {
        if ((mode == AgentCredentialDeliveryMode.Disabled && credentialLength != 0)
            || (mode == AgentCredentialDeliveryMode.AccessToken
                && (credentialLength <= 0 || credentialLength > MaximumCredentialBytes))
            || (mode != AgentCredentialDeliveryMode.Disabled
                && mode != AgentCredentialDeliveryMode.AccessToken))
        {
            throw Failure("Credential delivery length is invalid.");
        }
    }

    private static void ParsePayload(
        string payload,
        out string bootstrapId,
        out int processId,
        out long creationTime,
        out AgentCredentialDeliveryMode mode,
        out int credentialLength,
        out byte[] digest)
    {
        digest = new byte[0];
        if (payload == null
            || !payload.StartsWith(PayloadPrefix, StringComparison.Ordinal)
            || !payload.EndsWith("\"}", StringComparison.Ordinal))
        {
            throw Failure("Credential metadata payload is invalid.");
        }

        var bootstrapStart = PayloadPrefix.Length;
        var processMarker = payload.IndexOf(ProcessIdMarker, bootstrapStart, StringComparison.Ordinal);
        var creationMarker = Find(payload, CreationTimeMarker, processMarker, ProcessIdMarker.Length);
        var modeMarker = Find(payload, ModeMarker, creationMarker, CreationTimeMarker.Length);
        var lengthMarker = Find(payload, LengthMarker, modeMarker, ModeMarker.Length);
        var digestMarker = Find(payload, DigestMarker, lengthMarker, LengthMarker.Length);
        if (processMarker < 0
            || creationMarker < 0
            || modeMarker < 0
            || lengthMarker < 0
            || digestMarker < 0)
        {
            throw Failure("Credential metadata payload is invalid.");
        }

        bootstrapId = payload.Substring(bootstrapStart, processMarker - bootstrapStart);
        var processText = Slice(payload, processMarker, ProcessIdMarker, creationMarker);
        var creationText = Slice(payload, creationMarker, CreationTimeMarker, modeMarker);
        var modeText = Slice(payload, modeMarker, ModeMarker, lengthMarker);
        var lengthText = Slice(payload, lengthMarker, LengthMarker, digestMarker);
        var digestText = payload.Substring(
            digestMarker + DigestMarker.Length,
            payload.Length - 2 - (digestMarker + DigestMarker.Length));

        int modeValue;
        if (bootstrapId.Length != AgentBootstrapProtocol.BootstrapIdSize * 2
            || !IsLowerHex(bootstrapId)
            || !int.TryParse(processText, NumberStyles.None, CultureInfo.InvariantCulture, out processId)
            || processId <= 0
            || !long.TryParse(
                creationText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out creationTime)
            || creationTime <= 0
            || !int.TryParse(modeText, NumberStyles.None, CultureInfo.InvariantCulture, out modeValue)
            || !int.TryParse(
                lengthText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out credentialLength)
            || digestText.Length != 64
            || !TryDecodeLowerHex(digestText, out digest))
        {
            throw Failure("Credential metadata payload values are invalid.");
        }
        mode = (AgentCredentialDeliveryMode)modeValue;
    }

    private static int Find(string text, string marker, int previous, int previousLength)
    {
        return previous < 0
            ? -1
            : text.IndexOf(marker, previous + previousLength, StringComparison.Ordinal);
    }

    private static string Slice(string text, int marker, string markerText, int nextMarker)
    {
        var start = marker + markerText.Length;
        return text.Substring(start, nextMarker - start);
    }

    private static IpcEnvelope Snapshot(IpcEnvelope envelope)
    {
        return new IpcEnvelope
        {
            ProtocolVersion = envelope.ProtocolVersion,
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            SessionId = envelope.SessionId,
            Sequence = envelope.Sequence,
            MessageType = envelope.MessageType,
            PayloadJson = envelope.PayloadJson,
            Nonce = envelope.Nonce,
            Mac = envelope.Mac,
        };
    }

    private static byte[] ComputeSha256(byte[] bytes)
    {
        using (var sha = SHA256.Create())
        {
            return sha.ComputeHash(bytes);
        }
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        var difference = left.Length ^ right.Length;
        var count = Math.Min(left.Length, right.Length);
        for (var index = 0; index < count; index++)
        {
            difference |= left[index] ^ right[index];
        }
        return difference == 0;
    }

    private static string FormatLowerHex(byte[] bytes)
    {
        const string digits = "0123456789abcdef";
        var characters = new char[checked(bytes.Length * 2)];
        for (var index = 0; index < bytes.Length; index++)
        {
            characters[index * 2] = digits[bytes[index] >> 4];
            characters[(index * 2) + 1] = digits[bytes[index] & 0x0f];
        }
        return new string(characters);
    }

    private static bool TryDecodeLowerHex(string value, out byte[] bytes)
    {
        bytes = new byte[value.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            var high = DecodeLowerHex(value[index * 2]);
            var low = DecodeLowerHex(value[(index * 2) + 1]);
            if (high < 0 || low < 0)
            {
                Clear(bytes);
                bytes = new byte[0];
                return false;
            }
            bytes[index] = (byte)((high << 4) | low);
        }
        return true;
    }

    private static int DecodeLowerHex(char value)
    {
        if (value >= '0' && value <= '9')
        {
            return value - '0';
        }
        return value >= 'a' && value <= 'f' ? value - 'a' + 10 : -1;
    }

    private static bool IsLowerHex(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (DecodeLowerHex(value[index]) < 0)
            {
                return false;
            }
        }
        return true;
    }

    private static void ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, offset + total, count - total);
            if (read <= 0)
            {
                throw Failure("Credential frame is truncated.");
            }
            total += read;
        }
    }

    private static void EnsureEndOfStream(Stream stream)
    {
        if (stream.ReadByte() >= 0)
        {
            throw Failure("Credential frame has trailing data.");
        }
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }

    private static int ReadInt32(byte[] bytes, int offset)
    {
        return bytes[offset]
            | (bytes[offset + 1] << 8)
            | (bytes[offset + 2] << 16)
            | (bytes[offset + 3] << 24);
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void Clear(byte[]? bytes)
    {
        if (bytes != null)
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static AgentBootstrapLaunchException Failure(
        string unsafeDiagnostic,
        Exception? unsafeException = null)
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.CredentialUnavailable,
            unsafeDiagnostic,
            unsafeException);
    }
}
