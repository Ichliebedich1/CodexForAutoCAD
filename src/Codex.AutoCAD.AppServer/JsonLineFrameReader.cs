using System.Buffers;
using System.Text;

namespace Codex.AutoCAD.AppServer;

/// <summary>Stateful, size-bounded reader for App Server JSONL frames.</summary>
internal sealed class JsonLineFrameReader
{
    private readonly Stream _stream;
    private readonly int _maximumFrameBytes;
    private readonly byte[] _readBuffer;
    private readonly ArrayBufferWriter<byte> _frame = new();
    private int _offset;
    private int _count;

    public JsonLineFrameReader(Stream stream, int maximumFrameBytes, int readBufferBytes = 8 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maximumFrameBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));
        if (readBufferBytes <= 0) throw new ArgumentOutOfRangeException(nameof(readBufferBytes));

        _stream = stream;
        _maximumFrameBytes = maximumFrameBytes;
        _readBuffer = new byte[readBufferBytes];
    }

    public async ValueTask<string?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_offset < _count)
            {
                var newline = Array.IndexOf(_readBuffer, (byte)'\n', _offset, _count - _offset);
                if (newline >= 0)
                {
                    Append(_readBuffer.AsSpan(_offset, newline - _offset));
                    _offset = newline + 1;

                    return DecodeAndClearFrame();
                }

                Append(_readBuffer.AsSpan(_offset, _count - _offset));
                _offset = _count;
            }

            _count = await _stream.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
            _offset = 0;
            if (_count == 0)
            {
                if (_frame.WrittenCount != 0)
                {
                    throw new AppServerProtocolException("App Server stdout ended with an unterminated JSONL frame.");
                }

                return null;
            }
        }
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        if (_frame.WrittenCount + bytes.Length > _maximumFrameBytes)
        {
            throw new AppServerProtocolException($"App Server JSONL frame exceeds {_maximumFrameBytes} bytes.");
        }

        _frame.Write(bytes);
    }

    private string DecodeAndClearFrame()
    {
        var bytes = _frame.WrittenSpan;
        if (!bytes.IsEmpty && bytes[^1] == (byte)'\r')
        {
            bytes = bytes[..^1];
        }

        var frame = Encoding.UTF8.GetString(bytes);
        _frame.Clear();
        return frame;
    }
}
