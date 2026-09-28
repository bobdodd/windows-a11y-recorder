using System.Diagnostics;
using System.IO.Hashing;
using ZstdSharp;

namespace Recorder.Database.RecordingFiles;

/// <summary>A channel of a recording file: a topic, the stream it is written in, and what describes it.</summary>
public sealed record RecordingFileChannel(
    ushort Id,
    string Stream,
    string Topic,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Where a chunk of a recording file is and what it holds.</summary>
/// <param name="Ordinal">The chunk's position among the file's chunks, from zero.</param>
/// <param name="Stream">The stream every message in the chunk belongs to.</param>
/// <param name="StartTime">The earliest message time in the chunk, in session nanoseconds.</param>
/// <param name="EndTime">The latest message time in the chunk, in session nanoseconds.</param>
/// <param name="Offset">The byte offset of the chunk record from the start of the file.</param>
/// <param name="Length">The byte length of the chunk record, with its opcode and length.</param>
/// <param name="MessageCount">The messages in the chunk, or null when the reader has not counted them.</param>
public sealed record RecordingFileChunk(
    int Ordinal,
    string Stream,
    long StartTime,
    long EndTime,
    long Offset,
    long Length,
    long? MessageCount,
    long CompressedSize,
    long UncompressedSize);

public sealed record RecordingFileWriterOptions
{
    /// <summary>A stream's chunk is written once its uncompressed records reach this size.</summary>
    public int ChunkBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// A stream's chunk is written once its first message has waited this
    /// long, so a stream that is quiet is still written within this time.
    /// </summary>
    public TimeSpan ChunkInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The zstd compression level of each chunk.</summary>
    public int CompressionLevel { get; init; } = 3;

    /// <summary>The name and version written into the file's header.</summary>
    public string Library { get; init; } = "windows-a11y-recorder";
}

/// <summary>
/// Writes a recording file in the MCAP container format
/// (https://mcap.dev/spec). Messages are grouped into chunks by stream, so
/// reading one stream does not decompress the others. Each chunk is
/// compressed with zstd and checked with a CRC-32 of its uncompressed
/// records, and is followed by a message index for each of its channels.
/// Adding a message never writes to the file: a stream's chunk that is full
/// is closed and waits, and <see cref="WritePending"/> appends the closed and
/// due chunks. Earlier bytes are never rewritten, so every chunk written
/// before the writer stops remains readable, and a write that fails is cut
/// back to the last whole record and can be tried again. <see cref="Finish"/>
/// writes the summary and footer. Not safe for use from more than one thread
/// at a time.
/// </summary>
public sealed class RecordingFileWriter : IDisposable
{
    private readonly FileStream _file;
    private readonly RecordingFileWriterOptions _options;
    private readonly Compressor _compressor;
    private readonly Dictionary<string, OpenChunk> _open = new(StringComparer.Ordinal);
    private readonly List<RecordingFileChannel> _channels = [];
    private readonly Dictionary<ushort, long> _channelCounts = [];
    private readonly List<RecordingFileChunk> _chunks = [];
    private readonly List<(long Offset, long Length, string Name)> _metadata = [];
    private readonly List<OpenChunk> _closed = [];
    private readonly List<byte[]> _chunkIndexes = [];
    private readonly McapBuffer _scratch = new(64 * 1024);
    private readonly Crc32 _dataCrc = new();
    private long _nextChunkSequence;
    private long _position;
    private long _messageCount;
    private ulong? _startTime;
    private ulong _endTime;
    private bool _finished;

    /// <summary>Creates the file, which must not exist, and writes its magic and header.</summary>
    public RecordingFileWriter(string path, RecordingFileWriterOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _options = options ?? new RecordingFileWriterOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.ChunkBytes, 1);
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _file = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 1);
        _compressor = new Compressor(_options.CompressionLevel);
        _scratch.Bytes(Mcap.Magic);
        var header = _scratch.BeginRecord(Mcap.Header);
        _scratch.String(string.Empty);
        _scratch.String(_options.Library);
        _scratch.EndRecord(header);
        WriteData(_scratch.WrittenSpan);
    }

    public string Path { get; }

    public IReadOnlyList<RecordingFileChunk> Chunks => _chunks;

    public IReadOnlyList<RecordingFileChannel> Channels => _channels;

    public long MessageCount => _messageCount;

    /// <summary>Bytes written to the file so far.</summary>
    public long Position => _position;

    /// <summary>
    /// Writes a metadata record, outside any chunk, at the current end of
    /// the data section.
    /// </summary>
    public void AddMetadata(string name, IReadOnlyDictionary<string, string> values)
    {
        ThrowIfFinished();
        _scratch.Clear();
        var record = _scratch.BeginRecord(Mcap.Metadata);
        _scratch.String(name);
        _scratch.StringMap(values);
        _scratch.EndRecord(record);
        _metadata.Add((_position, _scratch.Length, name));
        WriteData(_scratch.WrittenSpan);
    }

    /// <summary>
    /// Adds a channel whose messages are written in <paramref name="stream"/>
    /// and returns its identifier. The channel record is written into the
    /// stream's chunk ahead of the channel's first message.
    /// </summary>
    public ushort AddChannel(string stream, string topic, IReadOnlyDictionary<string, string> metadata)
    {
        ThrowIfFinished();
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (_channels.Count >= ushort.MaxValue)
        {
            throw new InvalidOperationException("A recording file holds at most 65,535 channels.");
        }

        var values = new Dictionary<string, string>(metadata, StringComparer.Ordinal) { ["stream"] = stream };
        var channel = new RecordingFileChannel((ushort)(_channels.Count + 1), stream, topic, values);
        _channels.Add(channel);
        var chunk = OpenFor(stream);
        var record = chunk.Records.BeginRecord(Mcap.Channel);
        WriteChannel(chunk.Records, channel);
        chunk.Records.EndRecord(record);
        return channel.Id;
    }

    /// <summary>Adds a message to its channel's stream, writing the stream's chunk if it is full.</summary>
    public void AddMessage(ushort channelId, uint sequence, long logTime, ReadOnlySpan<byte> data)
    {
        ThrowIfFinished();
        if (channelId == 0 || channelId > _channels.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(logTime);
        var time = (ulong)logTime;
        var chunk = OpenFor(_channels[channelId - 1].Stream);
        var offset = (ulong)chunk.Records.Length;
        var record = chunk.Records.BeginRecord(Mcap.Message);
        chunk.Records.UInt16(channelId);
        chunk.Records.UInt32(sequence);
        chunk.Records.UInt64(time);
        chunk.Records.UInt64(time);
        chunk.Records.Bytes(data);
        chunk.Records.EndRecord(record);
        if (!chunk.Index.TryGetValue(channelId, out var entries))
        {
            entries = [];
            chunk.Index.Add(channelId, entries);
        }

        entries.Add((time, offset));
        chunk.StartTime = Math.Min(chunk.StartTime, time);
        chunk.EndTime = Math.Max(chunk.EndTime, time);
        chunk.MessageCount++;
        _messageCount++;
        _channelCounts[channelId] = _channelCounts.GetValueOrDefault(channelId) + 1;
        _startTime = _startTime is { } start ? Math.Min(start, time) : time;
        _endTime = Math.Max(_endTime, time);
        if (chunk.Records.Length >= _options.ChunkBytes)
        {
            _open.Remove(chunk.Stream);
            _closed.Add(chunk);
        }
    }

    /// <summary>Chunks closed or due and not yet written.</summary>
    public int PendingChunkCount => _closed.Count + DueChunks().Count();

    /// <summary>
    /// How long until the oldest open chunk is due to be written, zero when
    /// one is due now, or null when no chunk holds a message.
    /// </summary>
    public TimeSpan? TimeUntilDue()
    {
        TimeSpan? due = null;
        var now = Stopwatch.GetTimestamp();
        foreach (var chunk in _open.Values)
        {
            if (chunk.MessageCount == 0)
            {
                continue;
            }

            var remaining = _options.ChunkInterval - Stopwatch.GetElapsedTime(chunk.OpenedTimestamp, now);
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            due = due is { } earlier && earlier < remaining ? earlier : remaining;
        }

        return due;
    }

    /// <summary>
    /// Appends the closed chunks, and every open chunk whose first message
    /// has waited the chunk interval, in the order they were opened, and
    /// returns how many were written. When a write fails, the file is cut
    /// back to the end of the last chunk written and the exception is
    /// thrown; the chunks not written stay pending.
    /// </summary>
    public int WritePending()
    {
        ThrowIfFinished();
        foreach (var due in DueChunks().ToArray())
        {
            _open.Remove(due.Stream);
            _closed.Add(due);
        }

        _closed.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
        var written = 0;
        while (_closed.Count > 0)
        {
            WriteChunk(_closed[0]);
            _closed.RemoveAt(0);
            written++;
        }

        return written;
    }

    /// <summary>Asks the operating system to write the file's bytes to disk.</summary>
    public void FlushToDisk() => _file.Flush(flushToDisk: true);

    private IEnumerable<OpenChunk> DueChunks()
    {
        var now = Stopwatch.GetTimestamp();
        return _open.Values.Where(chunk =>
            chunk.MessageCount > 0 &&
            Stopwatch.GetElapsedTime(chunk.OpenedTimestamp, now) >= _options.ChunkInterval);
    }

    /// <summary>
    /// Writes the open chunks, the end of the data section, the summary, and
    /// the footer, and flushes the file to disk.
    /// </summary>
    public void Finish()
    {
        ThrowIfFinished();
        foreach (var chunk in _open.Values.Where(chunk => chunk.Records.Length > 0).ToArray())
        {
            _open.Remove(chunk.Stream);
            _closed.Add(chunk);
        }

        WritePending();
        _scratch.Clear();
        var dataEnd = _scratch.BeginRecord(Mcap.DataEnd);
        _scratch.UInt32(_dataCrc.GetCurrentHashAsUInt32());
        _scratch.EndRecord(dataEnd);
        WriteData(_scratch.WrittenSpan);

        var summaryStart = _position;
        var summary = new McapBuffer(64 * 1024);
        var groups = new List<(byte Opcode, long Start, long Length)>();

        void Group(byte opcode, Action write)
        {
            var start = summary.Length;
            write();
            if (summary.Length > start)
            {
                groups.Add((opcode, summaryStart + start, summary.Length - start));
            }
        }

        Group(Mcap.Channel, () =>
        {
            foreach (var channel in _channels)
            {
                var record = summary.BeginRecord(Mcap.Channel);
                WriteChannel(summary, channel);
                summary.EndRecord(record);
            }
        });
        Group(Mcap.Statistics, () =>
        {
            var record = summary.BeginRecord(Mcap.Statistics);
            summary.UInt64((ulong)_messageCount);
            summary.UInt16(0);
            summary.UInt32((uint)_channels.Count);
            summary.UInt32(0);
            summary.UInt32((uint)_metadata.Count);
            summary.UInt32((uint)_chunks.Count);
            summary.UInt64(_startTime ?? 0);
            summary.UInt64(_endTime);
            var counts = new McapBuffer();
            foreach (var (id, count) in _channelCounts.OrderBy(pair => pair.Key))
            {
                counts.UInt16(id);
                counts.UInt64((ulong)count);
            }

            summary.UInt32((uint)counts.Length);
            summary.Bytes(counts.WrittenSpan);
            summary.EndRecord(record);
        });
        Group(Mcap.ChunkIndex, () =>
        {
            foreach (var chunk in _chunkIndexes)
            {
                summary.Bytes(chunk);
            }
        });
        Group(Mcap.MetadataIndex, () =>
        {
            foreach (var (offset, length, name) in _metadata)
            {
                var record = summary.BeginRecord(Mcap.MetadataIndex);
                summary.UInt64((ulong)offset);
                summary.UInt64((ulong)length);
                summary.String(name);
                summary.EndRecord(record);
            }
        });

        var summaryOffsetStart = summaryStart + summary.Length;
        foreach (var (opcode, start, length) in groups)
        {
            var record = summary.BeginRecord(Mcap.SummaryOffset);
            summary.Byte(opcode);
            summary.UInt64((ulong)start);
            summary.UInt64((ulong)length);
            summary.EndRecord(record);
        }

        var footer = summary.BeginRecord(Mcap.Footer);
        summary.UInt64((ulong)summaryStart);
        summary.UInt64((ulong)summaryOffsetStart);

        // The CRC-32 covers the summary sections and the footer up to the
        // CRC field, including the footer's length, so it is filled in last.
        var crcAt = summary.Length;
        summary.UInt32(0);
        summary.EndRecord(footer);
        summary.SetUInt32(crcAt, Crc32.HashToUInt32(summary.WrittenSpan[..crcAt]));
        summary.Bytes(Mcap.Magic);
        Write(summary.WrittenSpan);
        _file.Flush(flushToDisk: true);
        _finished = true;
    }

    public void Dispose()
    {
        _file.Dispose();
        _compressor.Dispose();
    }

    private OpenChunk OpenFor(string stream)
    {
        if (!_open.TryGetValue(stream, out var chunk))
        {
            chunk = new OpenChunk(stream)
            {
                Sequence = _nextChunkSequence++,
                OpenedTimestamp = Stopwatch.GetTimestamp()
            };
            _open.Add(stream, chunk);
        }

        return chunk;
    }

    private void WriteChunk(OpenChunk chunk)
    {
        var records = chunk.Records.WrittenSpan;
        var compressed = _compressor.Wrap(records);
        var start = chunk.MessageCount == 0 ? 0 : chunk.StartTime;
        var end = chunk.MessageCount == 0 ? 0 : chunk.EndTime;
        var chunkOffset = _position;

        _scratch.Clear();
        var record = _scratch.BeginRecord(Mcap.Chunk);
        _scratch.UInt64(start);
        _scratch.UInt64(end);
        _scratch.UInt64((ulong)records.Length);
        _scratch.UInt32(Crc32.HashToUInt32(records));
        _scratch.String(Mcap.ZstdCompression);
        _scratch.UInt64((ulong)compressed.Length);
        _scratch.Bytes(compressed);
        _scratch.EndRecord(record);
        var chunkLength = _scratch.Length;

        var indexOffsets = new SortedDictionary<ushort, ulong>();
        foreach (var (channelId, entries) in chunk.Index.OrderBy(pair => pair.Key))
        {
            indexOffsets[channelId] = (ulong)(chunkOffset + _scratch.Length);
            var index = _scratch.BeginRecord(Mcap.MessageIndex);
            _scratch.UInt16(channelId);
            _scratch.UInt32((uint)(entries.Count * 16));
            foreach (var (time, offset) in entries)
            {
                _scratch.UInt64(time);
                _scratch.UInt64(offset);
            }

            _scratch.EndRecord(index);
        }

        var indexLength = _scratch.Length - chunkLength;
        WriteData(_scratch.WrittenSpan);

        var chunkIndex = new McapBuffer(128);
        var indexRecord = chunkIndex.BeginRecord(Mcap.ChunkIndex);
        chunkIndex.UInt64(start);
        chunkIndex.UInt64(end);
        chunkIndex.UInt64((ulong)chunkOffset);
        chunkIndex.UInt64((ulong)chunkLength);
        chunkIndex.UInt32((uint)(indexOffsets.Count * 10));
        foreach (var (channelId, offset) in indexOffsets)
        {
            chunkIndex.UInt16(channelId);
            chunkIndex.UInt64(offset);
        }

        chunkIndex.UInt64((ulong)indexLength);
        chunkIndex.String(Mcap.ZstdCompression);
        chunkIndex.UInt64((ulong)compressed.Length);
        chunkIndex.UInt64((ulong)records.Length);
        chunkIndex.EndRecord(indexRecord);
        _chunkIndexes.Add(chunkIndex.WrittenSpan.ToArray());

        _chunks.Add(new RecordingFileChunk(
            _chunks.Count,
            chunk.Stream,
            (long)start,
            (long)end,
            chunkOffset,
            chunkLength,
            chunk.MessageCount,
            compressed.Length,
            records.Length));
    }

    private static void WriteChannel(McapBuffer buffer, RecordingFileChannel channel)
    {
        buffer.UInt16(channel.Id);
        buffer.UInt16(0);
        buffer.String(channel.Topic);
        buffer.String(Mcap.JsonEncoding);
        buffer.StringMap(channel.Metadata);
    }

    // Bytes of the data section, which the data end record's CRC covers.
    private void WriteData(ReadOnlySpan<byte> bytes)
    {
        Write(bytes);
        _dataCrc.Append(bytes);
    }

    // Writes all of the bytes or, when the write fails, cuts the file back
    // to where it was, so a later write continues from a whole record.
    private void Write(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _file.Write(bytes);
            _file.Flush();
        }
        catch
        {
            try
            {
                _file.SetLength(_position);
                _file.Position = _position;
            }
            catch (IOException)
            {
            }

            throw;
        }

        _position += bytes.Length;
    }

    private void ThrowIfFinished() => ObjectDisposedException.ThrowIf(_finished, this);

    private sealed class OpenChunk(string stream)
    {
        public string Stream { get; } = stream;
        public McapBuffer Records { get; } = new(64 * 1024);
        public Dictionary<ushort, List<(ulong Time, ulong Offset)>> Index { get; } = [];
        public ulong StartTime = ulong.MaxValue;
        public ulong EndTime;
        public long MessageCount;
        public long Sequence;
        public long OpenedTimestamp;
    }
}
