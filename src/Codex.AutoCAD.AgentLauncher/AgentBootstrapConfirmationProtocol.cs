using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.AgentLauncher;

public static class AgentBootstrapConfirmationProtocol
{
    public const ushort CurrentVersion = 1;
    public const string ConfirmationMessageType = "agent.bootstrap.confirm";
    public const int MaximumFrameBytes = 2048;

    private const int HeaderSize = 16;
    private const int NonceBytes = 16;
    private const string PayloadPrefix = "{\"bootstrapId\":\"";
    private const string ProcessIdMarker = "\",\"processId\":";
    private const string CreationTimeMarker = ",\"processCreationFileTime\":";

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CDXCACF1");
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static IpcEnvelope CreateAgentConfirmation(
        string sessionId,
        byte[] bootstrapId,
        int processId,
        long processCreationFileTime,
        IpcEnvelopeAuthenticator authenticator)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (bootstrapId == null || bootstrapId.Length != AgentBootstrapProtocol.BootstrapIdSize)
        {
            throw new ArgumentException("Bootstrap id is invalid.", nameof(bootstrapId));
        }

        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (processCreationFileTime <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processCreationFileTime));
        }

        if (authenticator == null)
        {
            throw new ArgumentNullException(nameof(authenticator));
        }

        var bootstrapIdHex = FormatLowerHex(bootstrapId);
        var nonceBytes = new byte[NonceBytes];
        try
        {
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonceBytes);
            }

            var envelope = new IpcEnvelope
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                MessageId = bootstrapIdHex,
                CorrelationId = string.Empty,
                SessionId = sessionId,
                Sequence = 1,
                MessageType = ConfirmationMessageType,
                PayloadJson = PayloadPrefix
                    + bootstrapIdHex
                    + ProcessIdMarker
                    + processId.ToString(CultureInfo.InvariantCulture)
                    + CreationTimeMarker
                    + processCreationFileTime.ToString(CultureInfo.InvariantCulture)
                    + "}",
                Nonce = FormatLowerHex(nonceBytes)
            };
            envelope.Mac = authenticator.Sign(envelope);
            return envelope;
        }
        finally
        {
            Array.Clear(nonceBytes, 0, nonceBytes.Length);
        }
    }

    public static AgentBootstrapDoctorResult ValidateHostConfirmation(
        IpcEnvelope envelope,
        IpcSessionGuard incomingGuard,
        byte[] expectedBootstrapId,
        string expectedSessionId,
        string expectedPipeName,
        int expectedProcessId,
        long expectedProcessCreationFileTime,
        string executableSha256,
        int standardErrorBytes,
        bool standardErrorTruncated)
    {
        if (envelope == null)
        {
            throw ConfirmationFailure("AgentHost confirmation is missing.");
        }

        if (incomingGuard == null)
        {
            throw new ArgumentNullException(nameof(incomingGuard));
        }

        if (expectedBootstrapId == null
            || expectedBootstrapId.Length != AgentBootstrapProtocol.BootstrapIdSize)
        {
            throw new ArgumentException("Expected bootstrap id is invalid.", nameof(expectedBootstrapId));
        }

        var snapshot = SnapshotEnvelope(envelope);
        var validation = incomingGuard.ValidateAndAccept(snapshot);
        if (validation != IpcValidationCode.Accepted)
        {
            throw ConfirmationFailure("AgentHost confirmation authentication failed: " + validation + ".");
        }

        var expectedBootstrapIdHex = FormatLowerHex(expectedBootstrapId);
        if (snapshot.ProtocolVersion != ProtocolConstants.CurrentVersion
            || snapshot.Sequence != 1
            || !string.Equals(snapshot.MessageType, ConfirmationMessageType, StringComparison.Ordinal)
            || !string.Equals(snapshot.SessionId, expectedSessionId, StringComparison.Ordinal)
            || !string.Equals(snapshot.MessageId, expectedBootstrapIdHex, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(snapshot.CorrelationId))
        {
            throw ConfirmationFailure("AgentHost confirmation envelope identity is invalid.");
        }

        string actualBootstrapId;
        int actualProcessId;
        long actualStartTicks;
        ParsePayload(
            snapshot.PayloadJson,
            out actualBootstrapId,
            out actualProcessId,
            out actualStartTicks);

        if (!string.Equals(actualBootstrapId, expectedBootstrapIdHex, StringComparison.Ordinal)
            || actualProcessId != expectedProcessId
            || actualStartTicks != expectedProcessCreationFileTime)
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.IdentityMismatch,
                "AgentHost confirmation does not match the launched process identity.");
        }

        return new AgentBootstrapDoctorResult(
            actualProcessId,
            actualStartTicks,
            actualBootstrapId,
            expectedSessionId,
            expectedPipeName,
            executableSha256,
            standardErrorBytes,
            standardErrorTruncated);
    }

    public static void WriteSingleFrame(Stream output, IpcEnvelope envelope)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (!output.CanWrite)
        {
            throw new ArgumentException("Confirmation output must be writable.", nameof(output));
        }

        if (envelope == null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        byte[]? body = null;
        byte[]? frame = null;
        try
        {
            body = EncodeBody(envelope);
            if (body.Length <= 0 || body.Length > MaximumFrameBytes - HeaderSize)
            {
                throw new InvalidDataException("Confirmation body length is invalid.");
            }

            frame = new byte[checked(HeaderSize + body.Length)];
            Buffer.BlockCopy(Magic, 0, frame, 0, Magic.Length);
            WriteUInt16(frame, 8, CurrentVersion);
            WriteUInt16(frame, 10, 0);
            WriteInt32(frame, 12, body.Length);
            Buffer.BlockCopy(body, 0, frame, HeaderSize, body.Length);
            output.Write(frame, 0, frame.Length);
            output.Flush();
        }
        finally
        {
            Clear(body);
            Clear(frame);
        }
    }

    public static async Task<IpcEnvelope> ReadSingleFrameAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (!input.CanRead)
        {
            throw new ArgumentException("Confirmation input must be readable.", nameof(input));
        }

        var header = new byte[HeaderSize];
        byte[]? body = null;
        try
        {
            await ReadExactAsync(input, header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < Magic.Length; index++)
            {
                if (header[index] != Magic[index])
                {
                    throw new InvalidDataException("Confirmation magic is invalid.");
                }
            }

            if (ReadUInt16(header, 8) != CurrentVersion || ReadUInt16(header, 10) != 0)
            {
                throw new InvalidDataException("Confirmation header version or flags are invalid.");
            }

            var bodyLength = ReadInt32(header, 12);
            if (bodyLength <= 0 || bodyLength > MaximumFrameBytes - HeaderSize)
            {
                throw new InvalidDataException("Confirmation body length is invalid.");
            }

            body = new byte[bodyLength];
            await ReadExactAsync(input, body, 0, body.Length, cancellationToken).ConfigureAwait(false);
            var envelope = DecodeBody(body);
            await EnsureEndOfStreamAsync(input, cancellationToken).ConfigureAwait(false);
            return envelope;
        }
        finally
        {
            Clear(header);
            Clear(body);
        }
    }

    public static IpcEnvelope ReadSingleFrame(Stream input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (!input.CanRead)
        {
            throw new ArgumentException("Confirmation input must be readable.", nameof(input));
        }

        var header = new byte[HeaderSize];
        byte[]? body = null;
        try
        {
            ReadExact(input, header, 0, header.Length);
            for (var index = 0; index < Magic.Length; index++)
            {
                if (header[index] != Magic[index])
                {
                    throw new InvalidDataException("Confirmation magic is invalid.");
                }
            }

            if (ReadUInt16(header, 8) != CurrentVersion || ReadUInt16(header, 10) != 0)
            {
                throw new InvalidDataException("Confirmation header version or flags are invalid.");
            }

            var bodyLength = ReadInt32(header, 12);
            if (bodyLength <= 0 || bodyLength > MaximumFrameBytes - HeaderSize)
            {
                throw new InvalidDataException("Confirmation body length is invalid.");
            }

            body = new byte[bodyLength];
            ReadExact(input, body, 0, body.Length);
            var envelope = DecodeBody(body);
            EnsureEndOfStream(input);
            return envelope;
        }
        finally
        {
            Clear(header);
            Clear(body);
        }
    }

    public static string FormatLowerHex(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        const string digits = "0123456789abcdef";
        var characters = new char[checked(bytes.Length * 2)];
        for (var index = 0; index < bytes.Length; index++)
        {
            characters[index * 2] = digits[bytes[index] >> 4];
            characters[(index * 2) + 1] = digits[bytes[index] & 0x0F];
        }

        return new string(characters);
    }

    private static byte[] EncodeBody(IpcEnvelope envelope)
    {
        using (var stream = new MemoryStream())
        {
            WriteInt32(stream, envelope.ProtocolVersion);
            WriteString(stream, envelope.MessageId, 128);
            WriteString(stream, envelope.CorrelationId, 128);
            WriteString(stream, envelope.SessionId, 128);
            WriteInt64(stream, envelope.Sequence);
            WriteString(stream, envelope.MessageType, 128);
            WriteString(stream, envelope.PayloadJson, 1024);
            WriteString(stream, envelope.Nonce, 128);
            WriteString(stream, envelope.Mac, 256);
            return stream.ToArray();
        }
    }

    private static IpcEnvelope DecodeBody(byte[] body)
    {
        using (var stream = new MemoryStream(body, false))
        {
            var envelope = new IpcEnvelope
            {
                ProtocolVersion = ReadInt32(stream),
                MessageId = ReadString(stream, 128),
                CorrelationId = ReadString(stream, 128),
                SessionId = ReadString(stream, 128),
                Sequence = ReadInt64(stream),
                MessageType = ReadString(stream, 128),
                PayloadJson = ReadString(stream, 1024),
                Nonce = ReadString(stream, 128),
                Mac = ReadString(stream, 256)
            };

            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("Confirmation body contains trailing data.");
            }

            return envelope;
        }
    }

    private static IpcEnvelope SnapshotEnvelope(IpcEnvelope envelope)
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
            Mac = envelope.Mac
        };
    }

    private static void ParsePayload(
        string payload,
        out string bootstrapId,
        out int processId,
        out long processStartUtcTicks)
    {
        if (payload == null
            || !payload.StartsWith(PayloadPrefix, StringComparison.Ordinal)
            || !payload.EndsWith("}", StringComparison.Ordinal))
        {
            throw ConfirmationFailure("AgentHost confirmation payload is invalid.");
        }

        var bootstrapStart = PayloadPrefix.Length;
        var processMarker = payload.IndexOf(ProcessIdMarker, bootstrapStart, StringComparison.Ordinal);
        if (processMarker < 0)
        {
            throw ConfirmationFailure("AgentHost confirmation payload is invalid.");
        }

        var startMarker = payload.IndexOf(
            CreationTimeMarker,
            processMarker + ProcessIdMarker.Length,
            StringComparison.Ordinal);
        if (startMarker < 0)
        {
            throw ConfirmationFailure("AgentHost confirmation payload is invalid.");
        }

        bootstrapId = payload.Substring(bootstrapStart, processMarker - bootstrapStart);
        var processText = payload.Substring(
            processMarker + ProcessIdMarker.Length,
            startMarker - (processMarker + ProcessIdMarker.Length));
        var ticksText = payload.Substring(
            startMarker + CreationTimeMarker.Length,
            payload.Length - 1 - (startMarker + CreationTimeMarker.Length));

        if (bootstrapId.Length != AgentBootstrapProtocol.BootstrapIdSize * 2
            || !IsLowerHex(bootstrapId)
            || !int.TryParse(processText, NumberStyles.None, CultureInfo.InvariantCulture, out processId)
            || processId <= 0
            || !long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out processStartUtcTicks)
            || processStartUtcTicks <= 0)
        {
            throw ConfirmationFailure("AgentHost confirmation payload values are invalid.");
        }
    }

    private static bool IsLowerHex(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteString(Stream stream, string value, int maximumBytes)
    {
        if (value == null)
        {
            throw new InvalidDataException("Confirmation string is null.");
        }

        byte[] encoded;
        try
        {
            encoded = StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException("Confirmation string is not strict UTF-8.", exception);
        }

        try
        {
            if (encoded.Length > maximumBytes)
            {
                throw new InvalidDataException("Confirmation string exceeds its byte limit.");
            }

            WriteInt32(stream, encoded.Length);
            stream.Write(encoded, 0, encoded.Length);
        }
        finally
        {
            Clear(encoded);
        }
    }

    private static string ReadString(Stream stream, int maximumBytes)
    {
        var length = ReadInt32(stream);
        if (length < 0 || length > maximumBytes || length > stream.Length - stream.Position)
        {
            throw new InvalidDataException("Confirmation string length is invalid.");
        }

        var encoded = new byte[length];
        try
        {
            ReadExact(stream, encoded, 0, encoded.Length);
            try
            {
                return StrictUtf8.GetString(encoded);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Confirmation string is not strict UTF-8.", exception);
            }
        }
        finally
        {
            Clear(encoded);
        }
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        unchecked
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }

    private static int ReadInt32(byte[] buffer, int offset)
    {
        unchecked
        {
            return buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24);
        }
    }

    private static void WriteInt32(Stream stream, int value)
    {
        var bytes = new byte[4];
        try
        {
            WriteInt32(bytes, 0, value);
            stream.Write(bytes, 0, bytes.Length);
        }
        finally
        {
            Clear(bytes);
        }
    }

    private static int ReadInt32(Stream stream)
    {
        var bytes = new byte[4];
        try
        {
            ReadExact(stream, bytes, 0, bytes.Length);
            return ReadInt32(bytes, 0);
        }
        finally
        {
            Clear(bytes);
        }
    }

    private static void WriteInt64(Stream stream, long value)
    {
        var bytes = new byte[8];
        try
        {
            unchecked
            {
                for (var shift = 0; shift < 64; shift += 8)
                {
                    bytes[shift / 8] = (byte)(value >> shift);
                }
            }

            stream.Write(bytes, 0, bytes.Length);
        }
        finally
        {
            Clear(bytes);
        }
    }

    private static long ReadInt64(Stream stream)
    {
        var bytes = new byte[8];
        try
        {
            ReadExact(stream, bytes, 0, bytes.Length);
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 8)
            {
                value |= (ulong)bytes[shift / 8] << shift;
            }

            return unchecked((long)value);
        }
        finally
        {
            Clear(bytes);
        }
    }

    private static void ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        var read = 0;
        while (read < count)
        {
            var current = stream.Read(buffer, offset + read, count - read);
            if (current <= 0)
            {
                throw new EndOfStreamException("Confirmation frame is truncated.");
            }

            read += current;
        }
    }

    private static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < count)
        {
            var current = await stream.ReadAsync(
                    buffer,
                    offset + read,
                    count - read,
                    cancellationToken)
                .ConfigureAwait(false);
            if (current <= 0)
            {
                throw new EndOfStreamException("Confirmation frame is truncated.");
            }

            read += current;
        }
    }

    private static void EnsureEndOfStream(Stream input)
    {
        if (input.ReadByte() >= 0)
        {
            throw new InvalidDataException("Confirmation channel contains trailing data.");
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
                throw new InvalidDataException("Confirmation channel contains trailing data.");
            }
        }
        finally
        {
            Clear(trailing);
        }
    }

    private static AgentBootstrapLaunchException ConfirmationFailure(string message)
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.ConfirmationInvalid,
            message);
    }

    private static void Clear(byte[]? bytes)
    {
        if (bytes != null)
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }
}
