namespace Recorder.Session;

/// <summary>
/// Reads an NDJSON file as UTF-8 byte lines and reports where each line
/// starts in the file, so a caller can keep a record's location instead of
/// its text and read the record again later.
/// </summary>
/// <remarks>
/// Lines end at a line feed. A carriage return before the line feed is not
/// part of the line. A UTF-8 byte order mark at the start of the file is
/// skipped. <see cref="Line"/> is valid only until the next call to
/// <see cref="ReadLineAsync"/>.
/// </remarks>
internal sealed class NdjsonLineReader : IDisposable
{
    private const int InitialBufferSize = 64 * 1024;

    private readonly FileStream _stream;
    private byte[] _buffer = new byte[InitialBufferSize];
    private int _start;
    private int _end;
    private long _bufferFileOffset;
    private bool _endOfFile;

    public NdjsonLineReader(string path)
    {
        _stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 1,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
    }

    public long LineNumber { get; private set; }

    /// <summary>The file offset, in bytes, of the first byte of the line.</summary>
    public long LineOffset { get; private set; }

    public ReadOnlyMemory<byte> Line { get; private set; }

    public bool IsBlankLine
    {
        get
        {
            foreach (var value in Line.Span)
            {
                if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or 0x0B or 0x0C))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public async ValueTask<bool> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var newline = _buffer.AsSpan(_start, _end - _start).IndexOf((byte)'\n');
            if (newline >= 0)
            {
                SetLine(_start, newline);
                _start += newline + 1;
                return true;
            }

            if (_endOfFile)
            {
                if (_start < _end)
                {
                    SetLine(_start, _end - _start);
                    _start = _end;
                    return true;
                }

                Line = ReadOnlyMemory<byte>.Empty;
                return false;
            }

            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                _bufferFileOffset += _start;
                _end -= _start;
                _start = 0;
            }

            if (_end == _buffer.Length)
            {
                Array.Resize(ref _buffer, checked(_buffer.Length * 2));
            }

            var read = await _stream
                .ReadAsync(_buffer.AsMemory(_end), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                _endOfFile = true;
            }
            else
            {
                _end += read;
            }
        }
    }

    public void Dispose() => _stream.Dispose();

    private void SetLine(int start, int length)
    {
        LineNumber++;
        var offset = _bufferFileOffset + start;
        if (offset == 0 &&
            length >= 3 &&
            _buffer[start] == 0xEF &&
            _buffer[start + 1] == 0xBB &&
            _buffer[start + 2] == 0xBF)
        {
            start += 3;
            length -= 3;
            offset = 3;
        }

        if (length > 0 && _buffer[start + length - 1] == (byte)'\r')
        {
            length--;
        }

        LineOffset = offset;
        Line = _buffer.AsMemory(start, length);
    }
}
