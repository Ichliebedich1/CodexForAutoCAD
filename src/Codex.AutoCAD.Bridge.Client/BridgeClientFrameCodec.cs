using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Bridge.Client;

internal static class BridgeClientFrameCodec
{
    public static async Task WriteAsync(
        Stream stream,
        IpcEnvelope envelope,
        int maximumFrameBytes,
        CancellationToken cancellationToken)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        ValidateMaximumFrameBytes(maximumFrameBytes);
        var payload = BridgeClientJsonCodec.SerializeEnvelope(envelope);
        if (payload.Length == 0 || payload.Length > maximumFrameBytes)
        {
            throw new AgentBridgeClientException(
                "request_invalid",
                "Agent Bridge帧超过安全大小上限。");
        }

        var prefix = new byte[4];
        prefix[0] = (byte)payload.Length;
        prefix[1] = (byte)(payload.Length >> 8);
        prefix[2] = (byte)(payload.Length >> 16);
        prefix[3] = (byte)(payload.Length >> 24);
        await stream.WriteAsync(prefix, 0, prefix.Length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IpcEnvelope?> ReadAsync(
        Stream stream,
        int maximumFrameBytes,
        CancellationToken cancellationToken)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        ValidateMaximumFrameBytes(maximumFrameBytes);
        var prefix = new byte[4];
        var prefixBytes = await ReadAtMostAsync(
                stream,
                prefix,
                0,
                prefix.Length,
                cancellationToken)
            .ConfigureAwait(false);
        if (prefixBytes == 0)
        {
            return null;
        }

        if (prefixBytes != prefix.Length)
        {
            throw new EndOfStreamException("Agent Bridge长度前缀不完整。");
        }

        var length = prefix[0]
            | (prefix[1] << 8)
            | (prefix[2] << 16)
            | (prefix[3] << 24);
        if (length <= 0 || length > maximumFrameBytes)
        {
            throw new AgentBridgeClientException(
                "request_invalid",
                "Agent Bridge帧长度无效。");
        }

        var payload = new byte[length];
        var payloadBytes = await ReadAtMostAsync(
                stream,
                payload,
                0,
                payload.Length,
                cancellationToken)
            .ConfigureAwait(false);
        if (payloadBytes != payload.Length)
        {
            throw new EndOfStreamException("Agent Bridge帧载荷不完整。");
        }

        return BridgeClientJsonCodec.DeserializeEnvelope(payload);
    }

    private static async Task<int> ReadAtMostAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(
                    buffer,
                    offset + total,
                    count - total,
                    cancellationToken)
                .ConfigureAwait(false);
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
            throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));
        }
    }
}
