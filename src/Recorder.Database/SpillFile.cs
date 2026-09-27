using System.Text;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Database;

/// <summary>
/// Holds accepted events on disk, in order, while the database is not
/// accepting writes and the memory buffer is full. Events are read back
/// oldest first. The file is removed when every event in it has been read.
/// </summary>
internal sealed class SpillFile(string path) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private FileStream? _writer;
    private long _readOffset;

    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public long Bytes { get; private set; }
    public long Count { get; private set; }
    public bool HasEvents => Count > 0;

    /// <summary>
    /// Appends the event unless the file would then exceed
    /// <paramref name="maximumBytes"/>.
    /// </summary>
    public bool TryAppend(BufferedEvent buffered, long maximumBytes)
    {
        var line = Encoding.UTF8.GetBytes(Serialize(buffered) + "\n");
        if (Bytes + line.Length > maximumBytes)
        {
            return false;
        }

        Write(line);
        return true;
    }

    /// <summary>Appends the event whatever the file's size.</summary>
    public void Append(BufferedEvent buffered) =>
        Write(Encoding.UTF8.GetBytes(Serialize(buffered) + "\n"));

    private void Write(byte[] line)
    {
        if (_writer is null)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            _writer = new FileStream(
                Path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 64 * 1024);
            Bytes = 0;
            _readOffset = 0;
        }

        _writer.Write(line);
        Bytes += line.Length;
        Count++;
    }

    /// <summary>Reads up to <paramref name="maximum"/> of the oldest unread events.</summary>
    public List<BufferedEvent> Read(int maximum) => Read(maximum, long.MaxValue, requireOne: true);

    /// <summary>
    /// Reads up to <paramref name="maximum"/> of the oldest unread events
    /// whose estimated sizes together fit in <paramref name="maximumBytes"/>.
    /// When <paramref name="requireOne"/> is true the oldest event is read
    /// even if it alone does not fit.
    /// </summary>
    public List<BufferedEvent> Read(int maximum, long maximumBytes, bool requireOne)
    {
        var events = new List<BufferedEvent>();
        if (_writer is null || Count == 0)
        {
            return events;
        }

        _writer.Flush();
        ReadInto(events, maximum, maximumBytes, requireOne);

        Count -= events.Count;
        if (Count == 0)
        {
            // The reader is closed by now: Windows does not delete a file
            // that is still open.
            Delete();
        }

        return events;
    }

    private void ReadInto(List<BufferedEvent> events, int maximum, long maximumBytes, bool requireOne)
    {
        long bytes = 0;
        var full = false;
        using var reader = new FileStream(
            Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 64 * 1024);
        reader.Position = _readOffset;
        using var line = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (events.Count < maximum && !full)
        {
            var read = reader.Read(chunk, 0, chunk.Length);
            if (read == 0)
            {
                break;
            }

            var start = 0;
            while (start < read && events.Count < maximum)
            {
                var end = Array.IndexOf(chunk, (byte)'\n', start, read - start);
                if (end < 0)
                {
                    line.Write(chunk, start, read - start);
                    start = read;
                    break;
                }

                line.Write(chunk, start, end - start);
                var buffered = Deserialize(line.GetBuffer().AsSpan(0, (int)line.Length));
                var fits = bytes + buffered.EstimatedBytes <= maximumBytes ||
                    (requireOne && events.Count == 0);
                if (!fits)
                {
                    full = true;
                    break;
                }

                _readOffset += line.Length + 1;
                bytes += buffered.EstimatedBytes;
                events.Add(buffered);
                line.SetLength(0);
                start = end + 1;
            }
        }
    }

    /// <summary>Writes buffered bytes to disk and keeps the file.</summary>
    public void Flush() => _writer?.Flush(flushToDisk: true);

    public void Dispose() => _writer?.Dispose();

    private void Delete()
    {
        _writer?.Dispose();
        _writer = null;
        File.Delete(Path);
        Bytes = 0;
        _readOffset = 0;
    }

    private static string Serialize(BufferedEvent buffered) =>
        JsonSerializer.Serialize(new SpillRecord(buffered.EventKey, buffered.Event), JsonOptions);

    private static BufferedEvent Deserialize(ReadOnlySpan<byte> line)
    {
        var record = JsonSerializer.Deserialize<SpillRecord>(line, JsonOptions)
            ?? throw new InvalidDataException("The spill file holds an empty record.");
        return new BufferedEvent(record.EventKey, record.Event, record.Event.Payload.GetRawText());
    }

    private sealed record SpillRecord(long EventKey, RecorderEvent Event);
}
