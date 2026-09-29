using System.Diagnostics;
using System.Text.Json;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>How the state of a document was matched to a captured frame.</summary>
/// <param name="Basis">
/// "presented": the state after the document's last rendering update
/// presented at or before the frame's composition. "by-time": no update of
/// the document was presented at or before it, and the state is the state at
/// the composition time. "time": the state was asked for at a time, not for
/// a frame.
/// </param>
/// <param name="CutTime">The time of the document's last record included.</param>
/// <param name="PresentedTime">When the update was presented, for the basis "presented".</param>
public sealed record BrowserStateBasis(string Basis, long CutTime, long? PresentedTime);

/// <summary>What rebuilding a state read and did.</summary>
/// <param name="Source">"snapshots" when it started from snapshots, "records" when from the first record.</param>
/// <param name="HeldStatesRead">States of documents whose cut is before the latest index record, held from an earlier call.</param>
/// <param name="ScanStartTime">The earliest record time whose chunks were read, or null when read from the start.</param>
public sealed record BrowserStateCost(
    string Source,
    int SnapshotsRead,
    int HeldStatesRead,
    int ChunksRead,
    long RecordsRead,
    long RecordsApplied,
    long RecordsSkipped,
    long? ScanStartTime,
    double Milliseconds);

/// <summary>A document at a time or frame, how it was matched, and its state when it was asked for.</summary>
/// <param name="State">The rebuilt state, or null when the document's state was not asked for.</param>
public sealed record BrowserDocumentAt(string Key, BrowserDocumentState? State, BrowserStateBasis Basis);

/// <summary>
/// The browser documents at a time or a captured frame, with the recorded
/// state of those asked for.
/// </summary>
/// <param name="WriterLosses">
/// The app writer's omissions at or before the time: records it could not
/// write, of any channel, which the state may lack.
/// </param>
public sealed record BrowserStateAt(
    long Time,
    IReadOnlyList<BrowserDocumentAt> Documents,
    IReadOnlyList<string> WriterLosses,
    string? StateStopped,
    BrowserStateCost Cost);

/// <summary>
/// Rebuilds the recorded state of each browser document of a recording file
/// at a time, or for a captured desktop frame. Each document's state is read
/// from its latest snapshot at or before the time, when the file has
/// snapshots, and the records after the snapshot are applied up to the time.
/// A file without snapshots, or a time before the first, is rebuilt from the
/// first record. The result is the same either way; snapshots only make it
/// faster. Every document is named, from the index and the records read, and
/// only the documents asked for are rebuilt. The state of a document whose
/// cut is before the latest index
/// record, such as one not drawn since, is held between calls, up to
/// <see cref="HeldStateLimitBytes"/>. One call is made at a time.
/// </summary>
public sealed class RecordingFileBrowserState
{
    private readonly RecordingFileReader _reader;
    private readonly PlaybackIndex? _index;
    private readonly List<IndexRecord> _records = [];
    private readonly Dictionary<long, List<(RecordingFileChunk Chunk, int Offset)>> _snapshots = [];
    private readonly RecordingFileChunk[] _stateChunks;
    private readonly string? _stopped;
    private readonly List<(long Time, string Text)> _writerLosses = [];
    private readonly Dictionary<string, (long Presented, long Completed)[]> _presented = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Key, long Cut), byte[]> _heldStates = [];
    private long _heldBytes;

    /// <summary>The most bytes of states held for documents whose cut is before the latest index record.</summary>
    public const long HeldStateLimitBytes = 512L * 1024 * 1024;

    private sealed record IndexEntry(
        long? SnapshotEventKey,
        long? SnapshotLogTime,
        long? FirstUnsnapshottedTime,
        long LastTime);

    private sealed record IndexRecord(long EventKey, long EventTime, long Time);

    // Each document's entries, in index record order, each with the ordinal
    // of the index record from which it holds, until the next.
    private readonly Dictionary<string, List<(int Record, IndexEntry Entry)>> _history = new(StringComparer.Ordinal);

    /// <param name="reader">The open file. It is not disposed.</param>
    /// <param name="index">The file's playback index, for matching frames, or null.</param>
    public RecordingFileBrowserState(RecordingFileReader reader, PlaybackIndex? index)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _index = index;
        var hasStateStream = reader.Chunks.Any(chunk => chunk.Stream == "browser-state");
        _stateChunks = [.. reader.Chunks.Where(chunk => chunk.Stream == (hasStateStream ? "browser-state" : "browser"))];

        // Index records are in the order the state thread wrote them. Each
        // lists the documents whose entry changed; a record that lists every
        // document adds only the entries that differ from the last.
        foreach (var chunk in reader.Chunks.Where(chunk => chunk.Stream == RecordingFileStateRecorder.IndexStream))
        {
            foreach (var message in reader.ReadChunk(chunk))
            {
                using var json = JsonDocument.Parse(message.Data);
                var root = json.RootElement;
                switch (root.GetProperty("kind").GetString())
                {
                    case "state-index" when root.GetProperty("formatVersion").GetInt32() == BrowserStateSnapshot.FormatVersion:
                        ReadIndexRecord(root);
                        break;
                    case "state-stopped":
                        _stopped ??= root.GetProperty("reason").GetString();
                        break;
                    case "state-summary" when root.GetProperty("stopped").ValueKind == JsonValueKind.String:
                        _stopped ??= root.GetProperty("stopped").GetString();
                        break;
                }
            }
        }

        var snapshotChannels = reader.Channels.Values
            .Where(channel => channel.Topic == RecordingFileStateRecorder.SnapshotTopic)
            .Select(channel => channel.Id)
            .ToArray();
        foreach (var chunk in reader.Chunks.Where(chunk => chunk.Stream == RecordingFileStateRecorder.SnapshotStream))
        {
            foreach (var channel in snapshotChannels)
            {
                foreach (var (logTime, offset) in reader.ReadMessageIndex(chunk, channel))
                {
                    if (!_snapshots.TryGetValue(logTime, out var located))
                    {
                        located = [];
                        _snapshots.Add(logTime, located);
                    }
                    located.Add((chunk, offset));
                }
            }
        }

        foreach (var chunk in reader.Chunks.Where(chunk => chunk.Stream == "recorder"))
        {
            foreach (var message in reader.ReadChunk(chunk))
            {
                if (message.Channel.Topic != RecordingFileBatchTarget.WriterTopic)
                {
                    continue;
                }
                using var json = JsonDocument.Parse(message.Data);
                var root = json.RootElement;
                if (root.GetProperty("kind").GetString() == "writer-omission")
                {
                    _writerLosses.Add((
                        message.LogTime,
                        $"{root.GetProperty("eventCount").GetInt64()} events from {root.GetProperty("firstMonotonicNanoseconds").GetInt64()} to {root.GetProperty("lastMonotonicNanoseconds").GetInt64()} ns"));
                }
            }
        }

        if (index is not null)
        {
            foreach (var group in index.PresentedCheckpoints.GroupBy(item => item.DocumentToken, StringComparer.Ordinal))
            {
                _presented[group.Key] = [.. group
                    .Select(item => (item.PresentedNanoseconds, item.CheckpointNanoseconds))
                    .OrderBy(item => item.PresentedNanoseconds)];
            }
        }
    }

    /// <summary>True when the file holds state index records, and so snapshots.</summary>
    public bool HasSnapshots => _records.Count > 0;

    /// <summary>The number of state index records.</summary>
    public int IndexRecordCount => _records.Count;

    /// <summary>Why the app's state thread stopped while recording, or null.</summary>
    public string? StateStopped => _stopped;

    /// <summary>
    /// The documents with a record at or before the time, and the state of
    /// those asked for.
    /// </summary>
    /// <param name="load">The keys of the documents whose state is read, or null for every document.</param>
    public BrowserStateAt At(
        long time,
        IReadOnlySet<string>? load = null,
        bool useSnapshots = true,
        CancellationToken cancellationToken = default) =>
        Rebuild(time, _ => (time, new BrowserStateBasis("time", time, null)), load, useSnapshots, cancellationToken);

    /// <summary>
    /// The state each document had in the captured frame at the frame time:
    /// for each document, the state after its last rendering update presented
    /// at or before the frame's composition, or, when none was, the state at
    /// the composition time.
    /// </summary>
    /// <param name="load">The keys of the documents whose state is read, or null for every document.</param>
    public BrowserStateAt AtFrame(
        long frameNanoseconds,
        IReadOnlySet<string>? load = null,
        bool useSnapshots = true,
        CancellationToken cancellationToken = default)
    {
        var composed = _index?.FrameCompositions
            .Where(item => item.FrameNanoseconds == frameNanoseconds)
            .Select(item => (long?)item.CompositedNanoseconds)
            .FirstOrDefault() ?? frameNanoseconds;
        return Rebuild(composed, key => Cut(key, composed), load, useSnapshots, cancellationToken);
    }

    private (long Cut, BrowserStateBasis Basis) Cut(string key, long composed)
    {
        var basis = FrameBasis(_presented.GetValueOrDefault(key.Split(' ', 2)[0]) ?? [], composed);
        return (basis.CutTime, basis);
    }

    /// <summary>
    /// The basis of a document's state in a frame composed at a time: the
    /// last of its rendering updates, by completion, presented at or before
    /// the composition, or the composition time when none was.
    /// </summary>
    /// <param name="updates">The document's presented updates: when each was presented and when it completed.</param>
    internal static BrowserStateBasis FrameBasis(IReadOnlyList<(long Presented, long Completed)> updates, long composed)
    {
        long? completed = null;
        long? presented = null;
        foreach (var (presentedTime, completedTime) in updates)
        {
            if (presentedTime <= composed && (completed is null || completedTime >= completed))
            {
                completed = completedTime;
                presented = presentedTime;
            }
        }
        return completed is { } cut
            ? new BrowserStateBasis("presented", cut, presented)
            : new BrowserStateBasis("by-time", composed, null);
    }

    private BrowserStateAt Rebuild(
        long time,
        Func<string, (long Cut, BrowserStateBasis Basis)> cutFor,
        IReadOnlySet<string>? load,
        bool useSnapshots,
        CancellationToken cancellationToken)
    {
        bool Wanted(string key) => load is null || load.Contains(key);
        var named = new HashSet<string>(StringComparer.Ordinal);
        var started = Stopwatch.GetTimestamp();
        var builder = new BrowserStateBuilder();
        var cuts = new Dictionary<string, (long Cut, BrowserStateBasis Basis)>(StringComparer.Ordinal);
        (long Cut, BrowserStateBasis Basis) CutOf(string key)
        {
            if (!cuts.TryGetValue(key, out var cut))
            {
                cut = cutFor(key);
                cuts.Add(key, cut);
            }
            return cut;
        }

        // Each document is read from its own window of records: from the
        // snapshot the index gives for it, or its first record, up to its
        // cut. A document the latest index record does not list is read
        // from that record's time. Without an index record at or before the
        // time, every document is read from the first record.
        var windows = new Dictionary<string, long>(StringComparer.Ordinal);
        long? mainStart = null;
        var snapshotsRead = 0;
        var heldRead = 0;
        var chunkCache = new System.Collections.Concurrent.ConcurrentDictionary<RecordingFileChunk, Lazy<byte[]>>();
        var requests = new List<(string Key, long EventKey, long LogTime)>();
        var latestIndex = useSnapshots ? _records.FindLastIndex(record => record.EventTime <= time) : -1;
        if (latestIndex >= 0)
        {
            var latest = _records[latestIndex];
            mainStart = latest.EventTime;
            foreach (var (key, history) in _history)
            {
                if (history[0].Record > latestIndex)
                {
                    continue;
                }
                named.Add(key);
                if (!Wanted(key))
                {
                    continue;
                }
                // The document's state is read from the latest index record
                // whose state of it is not after its cut. Before its first
                // entry, the document had no record.
                var cut = CutOf(key).Cut;
                // A document whose cut is before the latest index record,
                // such as one not drawn since, keeps its state at that cut
                // between frames, so it is rebuilt once.
                if (cut < latest.EventTime && _heldStates.TryGetValue((key, cut), out var held))
                {
                    builder.Load(BrowserStateSnapshot.Read(held));
                    windows[key] = long.MaxValue;
                    heldRead++;
                    continue;
                }
                var current = history.Count - 1;
                while (history[current].Record > latestIndex)
                {
                    current--;
                }
                IndexEntry? entry = null;
                var position = history[0].Record - 1;
                for (var slot = current; slot >= 0; slot--)
                {
                    if (history[slot].Entry.LastTime <= cut)
                    {
                        entry = history[slot].Entry;
                        position = slot == current ? latestIndex : history[slot + 1].Record - 1;
                        break;
                    }
                }
                if (position < 0)
                {
                    windows[key] = long.MinValue;
                    continue;
                }
                var start = _records[position].EventTime;
                if (entry?.FirstUnsnapshottedTime is { } first)
                {
                    start = Math.Min(start, first);
                }
                windows[key] = start;
                if (entry?.SnapshotEventKey is { } snapshotKey)
                {
                    requests.Add((key, snapshotKey, entry.SnapshotLogTime!.Value));
                }
            }

            // Snapshots are read and parsed on all processors.
            var read = new BrowserDocumentState?[requests.Count];
            Parallel.For(
                0,
                requests.Count,
                new ParallelOptions { CancellationToken = cancellationToken },
                slot => read[slot] = ReadSnapshot(requests[slot].Key, requests[slot].EventKey, requests[slot].LogTime, chunkCache));
            for (var slot = 0; slot < read.Length; slot++)
            {
                builder.Load(read[slot] ??
                    throw new InvalidDataException($"The snapshot of document {requests[slot].Key} at event {requests[slot].EventKey} is not in the file."));
            }
            snapshotsRead = read.Length;
        }
        var scanStart = windows.Count == 0 ? mainStart : Math.Min(mainStart ?? long.MaxValue, windows.Values.Min());
        if (scanStart == long.MaxValue)
        {
            scanStart = mainStart;
        }
        if (scanStart == long.MinValue)
        {
            scanStart = null;
        }

        // Chunks are in the order their records were accepted, so every
        // record after a window's start is in the first chunk that ends at
        // or after it, or in a later one.
        long FirstChunk(long? start)
        {
            if (start is not { } from || from == long.MinValue)
            {
                return 0;
            }
            if (from == long.MaxValue)
            {
                return int.MaxValue;
            }
            for (var position = 0; position < _stateChunks.Length; position++)
            {
                if (_stateChunks[position].EndTime >= from)
                {
                    return position;
                }
            }
            return _stateChunks.Length;
        }
        var mainFirst = FirstChunk(mainStart);
        var firstChunks = windows.ToDictionary(item => item.Key, item => FirstChunk(item.Value), StringComparer.Ordinal);
        var windowEnds = firstChunks.Select(item => (First: item.Value, CutOf(item.Key).Cut)).ToArray();
        var earliest = firstChunks.Count == 0 ? mainFirst : Math.Min(mainFirst, firstChunks.Values.Min());

        var chunksRead = 0;
        var recordsRead = 0L;
        for (var position = (int)earliest; position < _stateChunks.Length; position++)
        {
            var chunk = _stateChunks[position];
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.StartTime > time)
            {
                continue;
            }
            // A chunk only some windows reach is read for their documents: a
            // window reaches the chunks from its first to the last that
            // starts at or before its cut.
            var forMain = position >= mainFirst;
            if (!forMain && !windowEnds.Any(window => window.First <= position && chunk.StartTime <= window.Cut))
            {
                continue;
            }
            chunksRead++;
            foreach (var message in _reader.ReadChunk(chunk))
            {
                if (!BrowserStateBuilder.IsStateChannel(message.Channel.Topic) || message.LogTime > time)
                {
                    continue;
                }
                recordsRead++;
                var stored = RecordingEventCodec.Decode(message.Data.Span);
                var record = stored.Event;
                if (record.EventType != "collector-omission" &&
                    DomTreeRebuilder.DocumentKey(record.Payload) is { } key)
                {
                    named.Add(key);
                    if (!Wanted(key) ||
                        record.MonotonicNanoseconds > CutOf(key).Cut ||
                        (firstChunks.TryGetValue(key, out var first) ? position < first : !forMain))
                    {
                        continue;
                    }
                }
                // An omission marks only the documents whose cut is not before it.
                var omissionTime = record.MonotonicNanoseconds;
                builder.Apply(
                    stored.EventKey,
                    record.MonotonicNanoseconds,
                    record.Channel,
                    record.EventType,
                    record.Payload,
                    document => omissionTime <= CutOf(document.Key).Cut);
            }
        }

        if (latestIndex >= 0)
        {
            var latestTime = _records[latestIndex].EventTime;
            foreach (var (key, start) in windows)
            {
                var cut = CutOf(key).Cut;
                if (start == long.MaxValue || cut >= latestTime || !builder.Documents.TryGetValue(key, out var document))
                {
                    continue;
                }
                var bytes = BrowserStateSnapshot.Serialize(document);
                if (_heldBytes + bytes.Length > HeldStateLimitBytes)
                {
                    _heldStates.Clear();
                    _heldBytes = 0;
                }
                _heldStates[(key, cut)] = bytes;
                _heldBytes += bytes.Length;
            }
        }

        // A document asked for is named when it has a state at its cut; one
        // not asked for, when the index or a record read names it.
        var documents = named
            .Where(key => !Wanted(key) || builder.Documents.ContainsKey(key))
            .Concat(builder.Documents.Keys.Where(key => !named.Contains(key)))
            .Order(StringComparer.Ordinal)
            .Select(key => new BrowserDocumentAt(key, builder.Documents.GetValueOrDefault(key), CutOf(key).Basis))
            .ToArray();
        return new BrowserStateAt(
            time,
            documents,
            [.. _writerLosses.Where(item => item.Time <= time).Select(item => item.Text)],
            _stopped,
            new BrowserStateCost(
                latestIndex < 0 ? "records" : "snapshots",
                snapshotsRead,
                heldRead,
                chunksRead,
                recordsRead,
                builder.RecordsApplied,
                builder.RecordsSkipped,
                scanStart,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds));
    }

    private BrowserDocumentState? ReadSnapshot(
        string key,
        long eventKey,
        long logTime,
        System.Collections.Concurrent.ConcurrentDictionary<RecordingFileChunk, Lazy<byte[]>> chunks)
    {
        if (!_snapshots.TryGetValue(logTime, out var located))
        {
            return null;
        }
        foreach (var (chunk, offset) in located)
        {
            var records = chunks.GetOrAdd(chunk, item => new Lazy<byte[]>(() => _reader.ReadChunkRecords(item))).Value;
            var message = _reader.ReadMessageAt(records, offset);
            if (Names(message.Data.Span, key, eventKey))
            {
                return BrowserStateSnapshot.Read(message.Data);
            }
        }
        return null;
    }

    // Whether a snapshot is of the document at the event, read from its
    // first properties, which name them before its state.
    private static bool Names(ReadOnlySpan<byte> data, string key, long eventKey)
    {
        var reader = new Utf8JsonReader(data);
        string? documentKey = null;
        reader.Read();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
        {
            var name = reader.GetString();
            reader.Read();
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                return false;
            }
            switch (name)
            {
                case "documentKey":
                    documentKey = reader.GetString();
                    if (documentKey != key)
                    {
                        return false;
                    }
                    break;
                case "eventKey":
                    return documentKey == key && reader.GetInt64() == eventKey;
            }
        }
        return false;
    }

    private void ReadIndexRecord(JsonElement root)
    {
        var ordinal = _records.Count;
        _records.Add(new IndexRecord(
            root.GetProperty("eventKey").GetInt64(),
            root.GetProperty("eventTime").GetInt64(),
            root.GetProperty("time").GetInt64()));
        foreach (var item in root.GetProperty("documents").EnumerateArray())
        {
            var key = item.GetProperty("documentKey").GetString()!;
            var entry = new IndexEntry(
                Number(item, "snapshotEventKey"),
                Number(item, "snapshotLogTime"),
                Number(item, "firstUnsnapshottedTime"),
                item.GetProperty("lastTime").GetInt64());
            if (!_history.TryGetValue(key, out var history))
            {
                history = [];
                _history.Add(key, history);
            }
            if (history.Count == 0 || history[^1].Entry != entry)
            {
                history.Add((ordinal, entry));
            }
        }
    }

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
}
