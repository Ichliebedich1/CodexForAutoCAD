using System.Buffers;

namespace Codex.AutoCAD.AppServer;

/// <summary>
/// Bounded, content-free stderr telemetry for a local Codex child process.
/// </summary>
public sealed class AppServerStandardErrorSummary
{
    public AppServerStandardErrorSummary(int bytes, bool truncated)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        Bytes = bytes;
        Truncated = truncated;
    }

    public int Bytes { get; }

    public bool Truncated { get; }
}

internal static class AppServerStandardErrorCapture
{
    private const int BufferBytes = 4 * 1024;

    internal static async Task<AppServerStandardErrorSummary> DrainAsync(
        Stream input,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead)
        {
            throw new ArgumentException("Standard error stream must be readable.", nameof(input));
        }

        if (maximumBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
        var capturedBytes = 0;
        var truncated = false;
        try
        {
            while (true)
            {
                var read = await input.ReadAsync(
                        buffer.AsMemory(0, BufferBytes),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var remaining = maximumBytes - capturedBytes;
                if (remaining > 0)
                {
                    capturedBytes += Math.Min(read, remaining);
                }

                if (read > remaining)
                {
                    truncated = true;
                }

                Array.Clear(buffer, 0, read);
            }

            return new AppServerStandardErrorSummary(capturedBytes, truncated);
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
