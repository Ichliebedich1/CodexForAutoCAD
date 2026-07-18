using System.Buffers.Binary;
using System.Text.Json;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Bridge;

public static class LengthPrefixedFrameCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32
    };

    public static async ValueTask WriteAsync(
        Stream stream,
        IpcEnvelope envelope,
        CancellationToken cancellationToken = default,
        int maximumFrameBytes = ProtocolConstants.MaximumMessageBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateMaximumFrameBytes(maximumFrameBytes);

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        if (payload.Length > maximumFrameBytes)
        {
            throw new BridgeProtocolException(
                $"IPC帧大小{payload.Length}字节，超过{maximumFrameBytes}字节上限。");
        }

        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<IpcEnvelope?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default,
        int maximumFrameBytes = ProtocolConstants.MaximumMessageBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateMaximumFrameBytes(maximumFrameBytes);

        var prefix = new byte[sizeof(int)];
        var prefixBytes = await ReadAtMostAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
        if (prefixBytes == 0)
        {
            return null;
        }

        if (prefixBytes != prefix.Length)
        {
            throw new EndOfStreamException("IPC长度前缀不完整。");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > maximumFrameBytes)
        {
            throw new BridgeProtocolException(
                $"IPC帧长度{length}无效；允许范围为1至{maximumFrameBytes}字节。");
        }

        var payload = new byte[length];
        var payloadBytes = await ReadAtMostAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        if (payloadBytes != payload.Length)
        {
            throw new EndOfStreamException("IPC帧载荷不完整。");
        }

        try
        {
            return JsonSerializer.Deserialize<IpcEnvelope>(payload, SerializerOptions)
                ?? throw new BridgeProtocolException("IPC帧未包含有效信封。");
        }
        catch (JsonException exception)
        {
            throw new BridgeProtocolException("IPC帧不是有效JSON信封。", exception);
        }
    }

    private static async ValueTask<int> ReadAtMostAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static void ValidateMaximumFrameBytes(int maximumFrameBytes)
    {
        if (maximumFrameBytes <= 0 || maximumFrameBytes > ProtocolConstants.MaximumMessageBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameBytes),
                $"IPC帧上限必须为1至{ProtocolConstants.MaximumMessageBytes}字节。");
        }
    }
}
