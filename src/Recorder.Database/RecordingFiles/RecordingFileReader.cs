using System.Buffers.Binary;
using System.IO.Hashing;
using ZstdSharp;

namespace Recorder.Database.RecordingFiles;

/// <summary>A message read from a recording file.</summary>
public sealed record RecordingFileMessage(
    RecordingFileChannel Channel,
    uint Sequence,
    long LogTime,
    ReadOnlyMemory<byte> Data);

/// <summary>
/// Reads a recording file written by <see cref="RecordingFileWriter"/>. A
/// file with a footer is opened from its summary. A file without one, such
/// as a recording the app did not finish, is read from the start, chunk by
/// chunk, up to the last whole chunk whose records pass their CRC-32 check.
/// </summary>
public sealed class RecordingFileReader : IDisposable
{
    private readonly FileStream _file;
    private readonly Dictionary<ushort, RecordingFileChannel> _channels = [];
    private readonly List<RecordingFileChunk> _chunks = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _metadata =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Offset, long Length)> _attachments =
        new(StringComparer.Ordinal);

    private RecordingFileReader(FileStream file) => _file = file;

    /// <summary>True when the file was opened from its summary.</summary>
    public bool HasSummary { get; private set; }

    /// <summary>
    /// Why the file was read without its summary: the file ends before its
    /// summary, or the summary did not pass its check, and the chunks were
    /// read up to the last whole one. Null when the file was opened from its
    /// summary.
    /// </summary>
    public string? Incomplete { get; private set; }

    public IReadOnlyDictionary<ushort, RecordingFileChannel> Channels => _channels;

    /// <summary>The chunks, in the order they are in the file.</summary>
    public IReadOnlyList<RecordingFileChunk> Chunks => _chunks;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Metadata => _metadata;

    public static RecordingFileReader Open(string path)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1);
        var reader = new RecordingFileReader(file);
        try
        {
            reader.Load();
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Reads the messages of one chunk, in the order they were written.</summary>
    public IEnumerable<RecordingFileMessage> ReadChunk(RecordingFileChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return Messages(ReadChunkRecords(chunk.Offset, chunk.Length).Records);
    }

    /// <summary>The names of the file's attachments.</summary>
    public IReadOnlyCollection<string> AttachmentNames => _attachments.Keys;

    /// <summary>
    /// Reads an attachment's data, or returns null when the file has no
    /// attachment of that name.
    /// </summary>
    /// <exception cref="InvalidDataException">The attachment does not pass its CRC-32 check.</exception>
    public byte[]? ReadAttachment(string name)
    {
        if (!_attachments.TryGetValue(name, out var location))
        {
            return null;
        }

        var record = ReadAt(location.Offset, checked((int)location.Length));
        if (record.Length != location.Length || record[0] != Mcap.Attachment)
        {
            throw new InvalidDataException($"The attachment index does not lead to attachment {name}.");
        }

        var content = record.AsSpan(Mcap.RecordPrefixLength);
        var reader = new McapSpanReader(content);
        reader.UInt64();
        reader.UInt64();
        reader.String();
        reader.String();
        var data = reader.Bytes(checked((int)reader.UInt64())).ToArray();
        var fieldsLength = reader.Position;
        var crc = reader.UInt32();
        if (crc != 0 && Crc32.HashToUInt32(content[..fieldsLength]) != crc)
        {
            throw new InvalidDataException($"Attachment {name} does not match its CRC-32.");
        }

        return data;
    }

    /// <summary>
    /// Reads a chunk's records, decompressed and checked against their
    /// CRC-32. Safe to call from more than one thread at a time.
    /// </summary>
    public byte[] ReadChunkRecords(RecordingFileChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return ReadChunkRecords(chunk.Offset, chunk.Length).Records;
    }

    /// <summary>
    /// Reads the message index of one channel of a chunk: each message's log
    /// time and the offset of its record in the chunk's records, in the
    /// order the messages were written. Empty when the chunk holds no
    /// message of the channel.
    /// </summary>
    public (long LogTime, int Offset)[] ReadMessageIndex(RecordingFileChunk chunk, ushort channelId)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (!chunk.MessageIndexOffsets.TryGetValue(channelId, out var offset))
        {
            return [];
        }

        var prefix = ReadAt(offset, Mcap.RecordPrefixLength);
        if (prefix.Length != Mcap.RecordPrefixLength || prefix[0] != Mcap.MessageIndex)
        {
            throw new InvalidDataException("The chunk index does not lead to a message index.");
        }

        var length = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(prefix.AsSpan(1)));
        var reader = new McapSpanReader(ReadAt(offset + Mcap.RecordPrefixLength, length));
        if (reader.UInt16() != channelId)
        {
            throw new InvalidDataException("A message index is not for the channel the chunk index names.");
        }

        var entries = new (long, int)[checked((int)reader.UInt32() / 16)];
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = (checked((long)reader.UInt64()), checked((int)reader.UInt64()));
        }

        return entries;
    }

    /// <summary>Reads the message whose record begins at an offset of a chunk's records.</summary>
    public RecordingFileMessage ReadMessageAt(byte[] records, int offset)
    {
        ArgumentNullException.ThrowIfNull(records);
        var (opcode, _, _) = RecordAt(records, offset);
        if (opcode != Mcap.Message)
        {
            throw new InvalidDataException("A message index does not lead to a message.");
        }

        return Messages(records, offset).First();
    }

    /// <summary>
    /// Reads the messages of a chunk's records with the offset of each
    /// message's record.
    /// </summary>
    public IEnumerable<(RecordingFileMessage Message, int Offset)> ReadMessagesWithOffsets(byte[] records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var position = 0;
        while (position < records.Length)
        {
            var (opcode, _, next) = RecordAt(records, position);
            if (opcode == Mcap.Message)
            {
                yield return (Messages(records, position).First(), position);
            }
            else if (opcode == Mcap.Channel)
            {
                Messages(records, position).FirstOrDefault();
            }

            position = next;
        }
    }

    /// <summary>Reads every message, chunk by chunk, in the order the chunks are in the file.</summary>
    public IEnumerable<RecordingFileMessage> ReadAll()
    {
        foreach (var chunk in _chunks)
        {
            foreach (var message in ReadChunk(chunk))
            {
                yield return message;
            }
        }
    }

    public void Dispose() => _file.Dispose();

    private IEnumerable<RecordingFileMessage> Messages(byte[] records, int position = 0)
    {
        while (position < records.Length)
        {
            var (opcode, content, next) = RecordAt(records, position);
            position = next;
            if (opcode == Mcap.Channel)
            {
                // Every channel of a whole chunk is known once the file is
                // opened, so a chunk read later only finds its channels.
                var id = BinaryPrimitives.ReadUInt16LittleEndian(records.AsSpan(content.Start, 2));
                lock (_channels)
                {
                    if (!_channels.ContainsKey(id))
                    {
                        AddChannel(records.AsSpan(content.Start, content.Length));
                    }
                }
            }
            else if (opcode == Mcap.Message)
            {
                var reader = new McapSpanReader(records.AsSpan(content.Start, content.Length));
                var channelId = reader.UInt16();
                var sequence = reader.UInt32();
                var logTime = reader.UInt64();
                reader.UInt64();
                var dataStart = content.Start + reader.Position;
                RecordingFileChannel? channel;
                lock (_channels)
                {
                    _channels.TryGetValue(channelId, out channel);
                }

                if (channel is null)
                {
                    throw new InvalidDataException($"A message refers to channel {channelId}, which is not defined before it.");
                }

                yield return new RecordingFileMessage(
                    channel,
                    sequence,
                    checked((long)logTime),
                    records.AsMemory(dataStart, content.Start + content.Length - dataStart));
            }
        }
    }

    private static (byte Opcode, (int Start, int Length) Content, int Next) RecordAt(byte[] records, int position)
    {
        if (records.Length - position < Mcap.RecordPrefixLength)
        {
            throw new InvalidDataException("A chunk ends inside a record prefix.");
        }

        var opcode = records[position];
        var length = BinaryPrimitives.ReadUInt64LittleEndian(records.AsSpan(position + 1, 8));
        var start = position + Mcap.RecordPrefixLength;
        if (length > (ulong)(records.Length - start))
        {
            throw new InvalidDataException("A chunk ends inside a record.");
        }

        return (opcode, (start, (int)length), start + (int)length);
    }

    private void Load()
    {
        var magic = Mcap.Magic;
        var head = ReadAt(0, magic.Length);
        if (head.Length < magic.Length || !head.AsSpan().SequenceEqual(magic))
        {
            throw new InvalidDataException("The file does not begin with the MCAP magic bytes.");
        }

        bool loaded;
        try
        {
            loaded = TryLoadSummary();
        }
        catch (InvalidDataException)
        {
            // A summary that does not pass its checks is not used; the
            // chunks are read from the data section instead.
            loaded = false;
        }

        if (!loaded)
        {
            HasSummary = false;
            _channels.Clear();
            _chunks.Clear();
            _metadata.Clear();
            _attachments.Clear();
            Scan();
        }
    }

    private bool TryLoadSummary()
    {
        var length = _file.Length;
        var tailLength = Mcap.FooterRecordLength + Mcap.Magic.Length;
        if (length < Mcap.Magic.Length + tailLength)
        {
            return false;
        }

        var tail = ReadAt(length - tailLength, tailLength);
        if (!tail.AsSpan(Mcap.FooterRecordLength).SequenceEqual(Mcap.Magic) || tail[0] != Mcap.Footer)
        {
            return false;
        }

        var summaryStart = (long)BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(Mcap.RecordPrefixLength, 8));
        var summaryCrc = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(Mcap.RecordPrefixLength + 16, 4));
        if (summaryStart <= 0 || summaryStart > length - tailLength)
        {
            return false;
        }

        var footerStart = length - tailLength;
        var summary = ReadAt(summaryStart, checked((int)(footerStart - summaryStart)));
        if (summaryCrc != 0)
        {
            var crc = new Crc32();
            crc.Append(summary);
            crc.Append(tail.AsSpan(0, Mcap.FooterRecordLength - 4));
            if (crc.GetCurrentHashAsUInt32() != summaryCrc)
            {
                throw new InvalidDataException("The summary does not match its CRC-32.");
            }
        }

        var position = 0;
        var pendingChunks = new List<(long Start, long End, long Offset, long Length, Dictionary<ushort, long> Channels, long Compressed, long Uncompressed)>();
        while (position < summary.Length)
        {
            var (opcode, content, next) = RecordAt(summary, position);
            position = next;
            var span = summary.AsSpan(content.Start, content.Length);
            switch (opcode)
            {
                case Mcap.Channel:
                    AddChannel(span);
                    break;
                case Mcap.ChunkIndex:
                {
                    var reader = new McapSpanReader(span);
                    var start = reader.UInt64();
                    var end = reader.UInt64();
                    var offset = reader.UInt64();
                    var chunkLength = reader.UInt64();
                    var mapLength = (int)reader.UInt32();
                    var map = new McapSpanReader(reader.Bytes(mapLength));
                    var channels = new Dictionary<ushort, long>();
                    while (map.Remaining > 0)
                    {
                        var channelId = map.UInt16();
                        channels[channelId] = checked((long)map.UInt64());
                    }

                    reader.UInt64();
                    reader.String();
                    var compressed = reader.UInt64();
                    var uncompressed = reader.UInt64();
                    pendingChunks.Add(((long)start, (long)end, (long)offset, (long)chunkLength, channels, (long)compressed, (long)uncompressed));
                    break;
                }

                case Mcap.AttachmentIndex:
                {
                    var reader = new McapSpanReader(span);
                    var offset = checked((long)reader.UInt64());
                    var recordLength = checked((long)reader.UInt64());
                    reader.UInt64();
                    reader.UInt64();
                    reader.UInt64();
                    _attachments[reader.String()] = (offset, recordLength);
                    break;
                }

                case Mcap.MetadataIndex:
                {
                    var reader = new McapSpanReader(span);
                    var offset = (long)reader.UInt64();
                    var recordLength = checked((int)reader.UInt64());
                    ReadMetadata(ReadAt(offset, recordLength));
                    break;
                }
            }
        }

        foreach (var chunk in pendingChunks.OrderBy(chunk => chunk.Offset))
        {
            var stream = chunk.Channels.Count > 0 && _channels.TryGetValue(chunk.Channels.Keys.Min(), out var channel)
                ? channel.Stream
                : string.Empty;
            _chunks.Add(new RecordingFileChunk(
                _chunks.Count, stream, chunk.Start, chunk.End, chunk.Offset, chunk.Length,
                null, chunk.Compressed, chunk.Uncompressed)
            {
                MessageIndexOffsets = chunk.Channels
            });
        }

        HasSummary = true;
        return true;
    }

    // Reads the data section record by record, as far as whole records go.
    private void Scan()
    {
        var length = _file.Length;
        var position = (long)Mcap.Magic.Length;
        while (position < length)
        {
            if (length - position < Mcap.RecordPrefixLength)
            {
                Incomplete = $"The file ends inside a record prefix at byte {position}.";
                return;
            }

            var prefix = ReadAt(position, Mcap.RecordPrefixLength);
            var opcode = prefix[0];
            var contentLength = BinaryPrimitives.ReadUInt64LittleEndian(prefix.AsSpan(1));
            var recordLength = Mcap.RecordPrefixLength + (long)Math.Min(contentLength, (ulong)long.MaxValue / 2);
            if (contentLength > (ulong)(length - position - Mcap.RecordPrefixLength))
            {
                Incomplete = $"The file ends inside a record that begins at byte {position}.";
                return;
            }

            if (opcode == Mcap.DataEnd)
            {
                // The data section is whole; the summary or footer after it
                // is missing or does not pass its check.
                Incomplete = "The file's data section is whole, but its summary cannot be read.";
                return;
            }

            if (opcode == Mcap.Chunk)
            {
                (byte[] Records, long Start, long End, long Compressed) read;
                try
                {
                    read = ReadChunkRecords(position, recordLength);
                }
                catch (InvalidDataException exception)
                {
                    Incomplete = $"The chunk at byte {position} cannot be read: {exception.Message}";
                    return;
                }

                long count = 0;
                string? stream = null;
                foreach (var message in Messages(read.Records))
                {
                    stream ??= message.Channel.Stream;
                    count++;
                }

                _chunks.Add(new RecordingFileChunk(
                    _chunks.Count, stream ?? string.Empty, read.Start, read.End, position, recordLength,
                    count, read.Compressed, read.Records.Length)
                {
                    MessageIndexOffsets = new Dictionary<ushort, long>()
                });
            }
            else if (opcode == Mcap.MessageIndex && _chunks.Count > 0 && contentLength >= 2)
            {
                // Message indexes follow the chunk they index.
                var channelId = BinaryPrimitives.ReadUInt16LittleEndian(ReadAt(position + Mcap.RecordPrefixLength, 2));
                ((Dictionary<ushort, long>)_chunks[^1].MessageIndexOffsets)[channelId] = position;
            }
            else if (opcode == Mcap.Attachment)
            {
                var head = new McapSpanReader(ReadAt(position + Mcap.RecordPrefixLength, (int)Math.Min(contentLength, 64 * 1024)));
                head.UInt64();
                head.UInt64();
                _attachments[head.String()] = (position, recordLength);
            }
            else if (opcode == Mcap.Metadata)
            {
                ReadMetadata(ReadAt(position, checked((int)recordLength)));
            }
            else if (opcode == Mcap.Channel)
            {
                AddChannel(ReadAt(position + Mcap.RecordPrefixLength, checked((int)contentLength)));
            }

            position += recordLength;
        }

        Incomplete = $"The file ends at byte {position} without its data end record.";
    }

    private void ReadMetadata(byte[] record)
    {
        if (record.Length < Mcap.RecordPrefixLength || record[0] != Mcap.Metadata)
        {
            throw new InvalidDataException("A metadata index does not lead to a metadata record.");
        }

        var reader = new McapSpanReader(record.AsSpan(Mcap.RecordPrefixLength));
        var name = reader.String();
        _metadata[name] = reader.StringMap();
    }

    // Reads a chunk record's records, decompressed, and checks their CRC-32.
    private (byte[] Records, long Start, long End, long Compressed) ReadChunkRecords(long offset, long length)
    {
        var record = ReadAt(offset, checked((int)length));
        if (record.Length != length || record[0] != Mcap.Chunk)
        {
            throw new InvalidDataException("The chunk index does not lead to a chunk record.");
        }

        var reader = new McapSpanReader(record.AsSpan(Mcap.RecordPrefixLength));
        var start = (long)reader.UInt64();
        var end = (long)reader.UInt64();
        var uncompressedSize = checked((int)reader.UInt64());
        var crc = reader.UInt32();
        var compression = reader.String();
        var compressedSize = checked((int)reader.UInt64());
        var compressed = reader.Bytes(compressedSize);
        byte[] records;
        if (compression.Length == 0)
        {
            records = compressed.ToArray();
        }
        else if (compression == Mcap.ZstdCompression)
        {
            using var decompressor = new Decompressor();
            records = new byte[uncompressedSize];
            try
            {
                if (decompressor.Unwrap(compressed, records) != uncompressedSize)
                {
                    throw new InvalidDataException("A chunk does not decompress to its stated size.");
                }
            }
            catch (ZstdException exception)
            {
                throw new InvalidDataException($"A chunk cannot be decompressed: {exception.Message}", exception);
            }
        }
        else
        {
            throw new InvalidDataException($"A chunk uses compression '{compression}', which the recorder does not read.");
        }

        if (crc != 0 && Crc32.HashToUInt32(records) != crc)
        {
            throw new InvalidDataException("A chunk's records do not match their CRC-32.");
        }

        return (records, start, end, compressedSize);
    }

    private void AddChannel(ReadOnlySpan<byte> content)
    {
        var reader = new McapSpanReader(content);
        var id = reader.UInt16();
        reader.UInt16();
        var topic = reader.String();
        reader.String();
        var metadata = reader.StringMap();
        _channels[id] = new RecordingFileChannel(
            id,
            metadata.GetValueOrDefault("stream") ?? string.Empty,
            topic,
            metadata);
    }

    // Positional reads do not move a shared file position, so chunks can be
    // read from more than one thread at a time.
    private byte[] ReadAt(long offset, int count)
    {
        var bytes = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = RandomAccess.Read(_file.SafeFileHandle, bytes.AsSpan(read), offset + read);
            if (n == 0)
            {
                return bytes[..read];
            }

            read += n;
        }

        return bytes;
    }
}
