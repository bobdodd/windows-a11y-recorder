using System.Runtime.CompilerServices;
using System.Text.Json;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>
/// A recording's timeline and complete records, read from its recording
/// file as they are needed. Only the chunk index, the channel counts, and
/// the occupancy grid are held; each lookup reads the message indexes of
/// the chunks that can hold its answer and decompresses the chunk that
/// holds it. Events are ordered by time, then by event key. The most
/// recently read chunks and message indexes are kept. Safe for use from
/// more than one thread at a time.
/// </summary>
public sealed class RecordingFileTimeline : ISessionTimeline, ISessionEventRecordSource, IDisposable
{
    private const int CachedChunks = 8;
    private const int CachedIndexes = 512;

    private readonly RecordingFileReader _reader;
    private readonly Dictionary<string, ushort[]> _channelIds;
    private readonly ChunkEntry[] _chunks;
    private readonly ConditionalWeakTable<SessionTimelineEvent, Location> _locations = new();
    private readonly object _cacheGate = new();
    private readonly LinkedList<(int Chunk, byte[] Records)> _records = new();
    private readonly Dictionary<(int Chunk, ushort Channel), (long Time, int Offset)[]> _indexes = [];
    private readonly Queue<(int Chunk, ushort Channel)> _indexOrder = new();

    /// <param name="reader">The open file, which the timeline disposes.</param>
    public RecordingFileTimeline(
        RecordingFileReader reader,
        IReadOnlyDictionary<string, long> channelCounts,
        TimelineOccupancy occupancy)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(channelCounts);
        ArgumentNullException.ThrowIfNull(occupancy);
        _reader = reader;
        ChannelCounts = channelCounts;
        Count = channelCounts.Values.Sum();
        Occupancy = occupancy;
        _channelIds = reader.Channels.Values
            .Where(channel => channel.Topic != RecordingFileBatchTarget.WriterTopic)
            .GroupBy(channel => channel.Topic, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(channel => channel.Id).ToArray(), StringComparer.Ordinal);
        _chunks = reader.Chunks
            .Select(chunk => new ChunkEntry(chunk, [.. chunk.MessageIndexOffsets.Keys]))
            .ToArray();
    }

    public long Count { get; }

    public IReadOnlyDictionary<string, long> ChannelCounts { get; }

    public TimelineOccupancy Occupancy { get; }

    public Task<SessionTimelineEvent?> AtOrBeforeAsync(
        long timestamp,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default) =>
        Run(ids => LastBefore(timestamp, long.MaxValue, ids), channels, cancellationToken);

    public Task<SessionTimelineEvent?> AdjacentAsync(
        SessionTimelineEvent from,
        bool forward,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(from);
        return Run(
            ids => forward
                ? FirstAfter(from.MonotonicNanoseconds, from.EventKey, ids)
                : LastBefore(from.MonotonicNanoseconds, from.EventKey, ids),
            channels,
            cancellationToken);
    }

    public Task<SessionTimelineEvent?> EndAsync(
        bool last,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default) =>
        Run(
            ids => last
                ? LastBefore(long.MaxValue, long.MaxValue, ids)
                : FirstAfter(long.MinValue, long.MinValue, ids),
            channels,
            cancellationToken);

    public Task<SessionTimelineEvent?> NearestAsync(
        long timestamp,
        long rangeStart,
        long rangeEnd,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (rangeStart > rangeEnd)
        {
            return Task.FromResult<SessionTimelineEvent?>(null);
        }

        return Run(ids =>
        {
            var target = Math.Clamp(timestamp, rangeStart, rangeEnd);

            // The first event at or after the target.
            var after = FirstAfter(target, long.MinValue, ids);
            if (after is { } found && found.Time > rangeEnd)
            {
                after = null;
            }

            // The latest time before the target, and the first event at it.
            Candidate? before = null;
            if (LastBefore(target, long.MinValue, ids) is { } latest && latest.Time >= rangeStart)
            {
                before = FirstAfter(latest.Time, long.MinValue, ids);
            }

            return before is null ? after :
                after is null ? before :
                target - before.Value.Time <= after.Value.Time - target ? before : after;
        }, channels, cancellationToken);
    }

    public string ReadEventJson(SessionTimelineEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Checked(() => ReadEventJsonCore(item));
    }

    private string ReadEventJsonCore(SessionTimelineEvent item)
    {
        if (!_locations.TryGetValue(item, out var location))
        {
            // An event this timeline did not return: found by its time and key.
            var ids = IdsOf(new HashSet<string>(StringComparer.Ordinal) { item.Channel });
            var found = LastBefore(item.MonotonicNanoseconds, item.EventKey + 1, ids);
            if (found is not { } candidate || candidate.Time != item.MonotonicNanoseconds ||
                KeyOf(candidate) != item.EventKey)
            {
                throw new InvalidDataException($"The recording file does not hold event {item.EventId}.");
            }

            location = new Location(candidate.Chunk, candidate.Offset);
        }

        var message = _reader.ReadMessageAt(RecordsOf(location.Chunk), location.Offset);
        using var document = JsonDocument.Parse(message.Data);
        var record = document.RootElement.GetProperty("event");
        if (record.GetProperty("eventId").GetString() != item.EventId)
        {
            throw new InvalidDataException($"The recording file no longer holds event {item.EventId} where it was read.");
        }

        return record.GetRawText();
    }

    public void Dispose() => _reader.Dispose();

    // A lookup reads the file, so it is done away from the caller's thread.
    private Task<SessionTimelineEvent?> Run(
        Func<ushort[], Candidate?> lookup,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var ids = IdsOf(channels);
        if (ids.Length == 0)
        {
            return Task.FromResult<SessionTimelineEvent?>(null);
        }

        return Task.Run(() => Checked(() => lookup(ids) is { } found ? Read(found) : null), cancellationToken);
    }

    // A message that is not an event as the recorder writes it is reported
    // as data that cannot be read.
    private static T Checked<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (
            exception is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException(
                $"A recording file message is not a recorder event: {exception.Message}", exception);
        }
    }

    private ushort[] IdsOf(IReadOnlySet<string> channels) =>
        channels
            .SelectMany(channel => _channelIds.TryGetValue(channel, out var ids) ? ids : [])
            .Order()
            .ToArray();

    // The last event before a time and key, in timeline order.
    private Candidate? LastBefore(long time, long key, ushort[] ids)
    {
        var chunks = _chunks
            .Where(chunk => chunk.Chunk.StartTime <= time && chunk.Holds(ids))
            .OrderByDescending(chunk => Math.Min(chunk.Chunk.EndTime, time))
            .ToArray();
        Candidate? best = null;
        foreach (var chunk in chunks)
        {
            if (best is { } current && current.Time > Math.Min(chunk.Chunk.EndTime, time))
            {
                break;
            }

            var entries = EntriesOf(chunk, ids);
            var index = UpperBound(entries, time) - 1;

            // Entries at the time itself are before it only when their key
            // is; within a chunk, keys ascend with offsets.
            while (index >= 0 && entries[index].Time == time && key != long.MaxValue)
            {
                if (key != long.MinValue && KeyOf(new Candidate(time, chunk.Chunk.Ordinal, entries[index].Offset)) < key)
                {
                    break;
                }

                index--;
            }

            if (index < 0)
            {
                continue;
            }

            var candidate = new Candidate(entries[index].Time, chunk.Chunk.Ordinal, entries[index].Offset);
            if (best is not { } previous || candidate.Time > previous.Time ||
                (candidate.Time == previous.Time && KeyOf(candidate) > KeyOf(previous)))
            {
                best = candidate;
            }
        }

        return best;
    }

    // The first event after a time and key, in timeline order.
    private Candidate? FirstAfter(long time, long key, ushort[] ids)
    {
        var chunks = _chunks
            .Where(chunk => chunk.Chunk.EndTime >= time && chunk.Holds(ids))
            .OrderBy(chunk => Math.Max(chunk.Chunk.StartTime, time))
            .ToArray();
        Candidate? best = null;
        foreach (var chunk in chunks)
        {
            if (best is { } current && current.Time < Math.Max(chunk.Chunk.StartTime, time))
            {
                break;
            }

            var entries = EntriesOf(chunk, ids);
            var index = UpperBound(entries, time - (time == long.MinValue ? 0 : 1));
            while (index < entries.Length && entries[index].Time == time && key != long.MinValue)
            {
                if (key != long.MaxValue && KeyOf(new Candidate(time, chunk.Chunk.Ordinal, entries[index].Offset)) > key)
                {
                    break;
                }

                index++;
            }

            if (index >= entries.Length)
            {
                continue;
            }

            var candidate = new Candidate(entries[index].Time, chunk.Chunk.Ordinal, entries[index].Offset);
            if (best is not { } previous || candidate.Time < previous.Time ||
                (candidate.Time == previous.Time && KeyOf(candidate) < KeyOf(previous)))
            {
                best = candidate;
            }
        }

        return best;
    }

    // The number of entries whose time is at or before a time.
    private static int UpperBound((long Time, int Offset)[] entries, long time)
    {
        var low = 0;
        var high = entries.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (entries[middle].Time <= time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // The chunk's messages on any of the channels, by time and then offset.
    private (long Time, int Offset)[] EntriesOf(ChunkEntry chunk, ushort[] ids)
    {
        var lists = ids
            .Where(id => chunk.Chunk.MessageIndexOffsets.ContainsKey(id))
            .Select(id => IndexOf(chunk.Chunk, id))
            .ToArray();
        if (lists.Length == 1)
        {
            return lists[0];
        }

        var merged = lists.SelectMany(list => list).ToArray();
        Array.Sort(merged, static (left, right) =>
            left.Time != right.Time ? left.Time.CompareTo(right.Time) : left.Offset.CompareTo(right.Offset));
        return merged;
    }

    private (long Time, int Offset)[] IndexOf(RecordingFileChunk chunk, ushort channel)
    {
        lock (_cacheGate)
        {
            if (_indexes.TryGetValue((chunk.Ordinal, channel), out var cached))
            {
                return cached;
            }
        }

        var entries = _reader.ReadMessageIndex(chunk, channel);
        Array.Sort(entries, static (left, right) =>
            left.LogTime != right.LogTime ? left.LogTime.CompareTo(right.LogTime) : left.Offset.CompareTo(right.Offset));
        lock (_cacheGate)
        {
            if (_indexes.TryAdd((chunk.Ordinal, channel), entries))
            {
                _indexOrder.Enqueue((chunk.Ordinal, channel));
                while (_indexOrder.Count > CachedIndexes)
                {
                    _indexes.Remove(_indexOrder.Dequeue());
                }
            }
        }

        return entries;
    }

    private byte[] RecordsOf(int chunk)
    {
        lock (_cacheGate)
        {
            for (var node = _records.First; node is not null; node = node.Next)
            {
                if (node.Value.Chunk == chunk)
                {
                    _records.Remove(node);
                    _records.AddFirst(node);
                    return node.Value.Records;
                }
            }
        }

        var records = _reader.ReadChunkRecords(_chunks[chunk].Chunk);
        lock (_cacheGate)
        {
            _records.AddFirst((chunk, records));
            while (_records.Count > CachedChunks)
            {
                _records.RemoveLast();
            }
        }

        return records;
    }

    private long KeyOf(Candidate candidate)
    {
        var message = _reader.ReadMessageAt(RecordsOf(candidate.Chunk), candidate.Offset);
        var json = new Utf8JsonReader(message.Data.Span);
        json.Read();
        while (json.Read() && json.TokenType == JsonTokenType.PropertyName)
        {
            if (json.ValueTextEquals("eventKey"u8))
            {
                json.Read();
                return json.GetInt64();
            }

            json.Skip();
        }

        throw new InvalidDataException("A recording file message holds no event key.");
    }

    private SessionTimelineEvent Read(Candidate candidate)
    {
        var message = _reader.ReadMessageAt(RecordsOf(candidate.Chunk), candidate.Offset);
        using var document = JsonDocument.Parse(message.Data);
        var root = document.RootElement;
        var record = root.GetProperty("event");
        var channel = record.GetProperty("channel").GetString() ?? string.Empty;
        var eventType = record.GetProperty("eventType").GetString() ?? string.Empty;
        var item = new SessionTimelineEvent(
            root.GetProperty("eventKey").GetInt64(),
            record.GetProperty("eventId").GetString() ?? string.Empty,
            record.GetProperty("evidenceClass").GetString() ?? string.Empty,
            channel,
            eventType,
            record.GetProperty("monotonicNanoseconds").GetInt64(),
            SessionPlaybackArchiveBuilder.CreateSummary(
                channel,
                eventType,
                record.TryGetProperty("payload", out var payload) ? payload : default));
        _locations.AddOrUpdate(item, new Location(candidate.Chunk, candidate.Offset));
        return item;
    }

    private readonly record struct Candidate(long Time, int Chunk, int Offset);

    private sealed record Location(int Chunk, int Offset);

    private sealed record ChunkEntry(RecordingFileChunk Chunk, ushort[] Channels)
    {
        public bool Holds(ushort[] ids)
        {
            foreach (var id in ids)
            {
                if (Array.IndexOf(Channels, id) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
