using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>What the state thread did while a recording was made.</summary>
public sealed record RecordingFileStateSummary(
    long RecordsApplied,
    long Snapshots,
    long SnapshotBytes,
    long IndexRecords,
    long IndexBytes,
    long LargestQueuedBytes,
    double ApplyMilliseconds,
    double SnapshotMilliseconds,
    int Documents,
    string? Stopped);

/// <summary>
/// Makes snapshots of the recorded state of each browser document while a
/// recording is made, on a thread of its own. The batch target passes it the
/// DOM, layout, interaction, and presentation records of each batch it has
/// added to the file, through a queue the thread waits on. The thread applies
/// them with a <see cref="BrowserStateBuilder"/>, and at most once a second of
/// recording time it writes a snapshot of each document that has changed and
/// whose last snapshot is at least <see cref="SnapshotInterval"/> of recording
/// time old, followed by a state index record that lists every document, its
/// latest snapshot, and the time of its first record after it. A snapshot is
/// not written while a document is between two records of one change.
///
/// When the queue holds more than <see cref="QueueLimitBytes"/>, the thread
/// stops for the rest of the recording and a record states when and why. No
/// evidence is lost: the file holds every record, and a reader rebuilds from
/// the last snapshot and index record before the stop. Passing records never
/// waits for the thread.
/// </summary>
public sealed class RecordingFileStateRecorder
{
    public const string SnapshotTopic = "recorder.state";
    public const string IndexTopic = "recorder.state-index";
    public const string SnapshotStream = "state";
    public const string IndexStream = "state-index";

    /// <summary>The recording time between two snapshots of a document that changes.</summary>
    public static readonly long SnapshotInterval = 10_000_000_000;

    /// <summary>The recording time between two checks for snapshots that are due.</summary>
    public static readonly long SweepInterval = 1_000_000_000;

    /// <summary>The estimated size of the records waiting for the thread at which it stops.</summary>
    public const long QueueLimitBytes = 256L * 1024 * 1024;

    private readonly Channel<BufferedEvent[]> _queue =
        Channel.CreateUnbounded<BufferedEvent[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly Action<string, long, byte[]> _write;
    private readonly WriterTimings? _timings;
    private readonly BrowserStateBuilder _builder = new();
    private readonly Dictionary<string, Tracked> _tracked = new(StringComparer.Ordinal);
    private readonly Task _thread;
    private readonly long _queueLimitBytes;
    private long _queuedBytes;
    private long _largestQueuedBytes;
    private volatile string? _stopped;
    private long _nextSweep = long.MinValue;
    private long _lastKey = -1;
    private long _lastKeyTime;
    private long _lastTime;
    private bool _indexDue;
    private long _snapshots;
    private long _snapshotBytes;
    private long _indexRecords;
    private long _indexBytes;
    private long _applyTicks;
    private long _snapshotTicks;

    private sealed class Tracked
    {
        public long SnapshotEventKey = -1;
        public long SnapshotTime;
        public long SnapshotLogTime;
        public long? LastSnapshotAt;
        public long? FirstUnsnapshottedTime;
    }

    /// <param name="write">
    /// Adds a message to the recording file: its topic, log time, and data.
    /// Called on the state thread.
    /// </param>
    /// <param name="queueLimitBytes">The estimated size of waiting records at which the thread stops.</param>
    public RecordingFileStateRecorder(
        Action<string, long, byte[]> write,
        WriterTimings? timings = null,
        long queueLimitBytes = QueueLimitBytes)
    {
        _queueLimitBytes = queueLimitBytes;
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _timings = timings;
        _thread = Task.Run(RunAsync);
    }

    /// <summary>Why the thread stopped before the recording ended, or null.</summary>
    public string? Stopped => _stopped;

    /// <summary>
    /// Passes the state records of a batch, in event key order. Returns at
    /// once. Returns the reason when this pass made the thread stop, so the
    /// caller can record it; otherwise null.
    /// </summary>
    public string? Pass(IReadOnlyList<BufferedEvent> events)
    {
        if (_stopped is not null || events.Count == 0)
        {
            return null;
        }
        var bytes = 0L;
        foreach (var item in events)
        {
            bytes += item.EstimatedBytes;
        }
        var queued = Interlocked.Add(ref _queuedBytes, bytes);
        if (queued > _largestQueuedBytes)
        {
            _largestQueuedBytes = queued;
        }
        if (queued > _queueLimitBytes)
        {
            return Stop($"The state thread fell behind: {queued} bytes of records were waiting for it, more than {_queueLimitBytes}.");
        }
        _queue.Writer.TryWrite([.. events]);
        return null;
    }

    /// <summary>
    /// Waits for the thread to apply every record passed, writes a snapshot
    /// of each document that changed since its last, and the last index
    /// record, and returns what the thread did.
    /// </summary>
    public RecordingFileStateSummary Complete()
    {
        _queue.Writer.TryComplete();
        _thread.GetAwaiter().GetResult();
        if (_stopped is null)
        {
            try
            {
                Sweep(_lastTime, final: true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Stop($"The state thread failed: {exception.Message}");
            }
        }
        var summary = new RecordingFileStateSummary(
            _builder.RecordsApplied,
            _snapshots,
            _snapshotBytes,
            _indexRecords,
            _indexBytes,
            _largestQueuedBytes,
            _applyTicks * 1000.0 / Stopwatch.Frequency,
            _snapshotTicks * 1000.0 / Stopwatch.Frequency,
            _builder.Documents.Count,
            _stopped);
        if (_builder.Documents.Count > 0 || _stopped is not null)
        {
            WriteSummary(summary);
        }
        return summary;
    }

    /// <summary>Writes the record that states the thread stopped, as the stop happens.</summary>
    public static byte[] StoppedRecord(long time, string reason)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "state-stopped");
            writer.WriteNumber("time", time);
            writer.WriteString("reason", reason);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private void WriteSummary(RecordingFileStateSummary summary)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "state-summary");
            writer.WriteNumber("recordsApplied", summary.RecordsApplied);
            writer.WriteNumber("snapshots", summary.Snapshots);
            writer.WriteNumber("snapshotBytes", summary.SnapshotBytes);
            writer.WriteNumber("indexRecords", summary.IndexRecords);
            writer.WriteNumber("indexBytes", summary.IndexBytes);
            writer.WriteNumber("largestQueuedBytes", summary.LargestQueuedBytes);
            writer.WriteNumber("applyMilliseconds", Math.Round(summary.ApplyMilliseconds, 3));
            writer.WriteNumber("snapshotMilliseconds", Math.Round(summary.SnapshotMilliseconds, 3));
            writer.WriteNumber("documents", summary.Documents);
            if (summary.Stopped is { } stopped)
            {
                writer.WriteString("stopped", stopped);
            }
            else
            {
                writer.WriteNull("stopped");
            }
            writer.WriteEndObject();
        }
        _write(IndexTopic, _lastTime, buffer.WrittenSpan.ToArray());
    }

    /// <summary>Lets the thread end without waiting for it, when the file is closed unfinished.</summary>
    public void Abandon() => Stop("The recording file was closed before it was finished.");

    private string? Stop(string reason)
    {
        if (_stopped is not null)
        {
            return null;
        }
        _stopped = reason;
        _queue.Writer.TryComplete();
        return reason;
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var events in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (_stopped is not null)
                {
                    continue;
                }
                var bytes = 0L;
                foreach (var item in events)
                {
                    bytes += item.EstimatedBytes;
                    Apply(item);
                }
                Interlocked.Add(ref _queuedBytes, -bytes);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (Stop($"The state thread failed: {exception.Message}") is { } reason)
            {
                _write(IndexTopic, _lastTime, StoppedRecord(_lastTime, reason));
            }
        }
    }

    private void Apply(BufferedEvent item)
    {
        var record = item.Event;
        var time = record.MonotonicNanoseconds;
        var applying = Stopwatch.GetTimestamp();
        var document = _builder.Apply(item.EventKey, time, record.Channel, record.EventType, record.Payload);
        _applyTicks += Stopwatch.GetTimestamp() - applying;
        foreach (var affected in _builder.LastOmissionAffected)
        {
            Changed(affected, time);
        }
        _lastKey = item.EventKey;
        _lastKeyTime = time;
        _lastTime = Math.Max(_lastTime, time);
        if (document is not null)
        {
            Changed(document, time);
        }
        if (_nextSweep == long.MinValue)
        {
            _nextSweep = _lastTime + SweepInterval;
        }
        else if (_lastTime >= _nextSweep)
        {
            _nextSweep = _lastTime + SweepInterval;
            Sweep(_lastTime, final: false);
        }
    }

    private void Changed(BrowserDocumentState document, long time)
    {
        if (!_tracked.TryGetValue(document.Key, out var tracked))
        {
            tracked = new Tracked();
            _tracked.Add(document.Key, tracked);
            _indexDue = true;
        }
        if (tracked.FirstUnsnapshottedTime is null)
        {
            tracked.FirstUnsnapshottedTime = time;
            _indexDue = true;
        }
    }

    private void Sweep(long now, bool final)
    {
        var sweeping = Stopwatch.GetTimestamp();
        foreach (var (key, tracked) in _tracked)
        {
            if (tracked.FirstUnsnapshottedTime is not { } first)
            {
                continue;
            }
            var since = tracked.LastSnapshotAt ?? first;
            var document = _builder.Documents[key];
            if ((!final && now - since < SnapshotInterval) || _builder.IsOpen(document))
            {
                continue;
            }
            var data = BrowserStateSnapshot.Serialize(document);
            _write(SnapshotTopic, now, data);
            _snapshots++;
            _snapshotBytes += data.Length;
            tracked.SnapshotEventKey = document.LastEventKey;
            tracked.SnapshotTime = document.LastTime;
            tracked.SnapshotLogTime = now;
            tracked.LastSnapshotAt = now;
            tracked.FirstUnsnapshottedTime = null;
            _indexDue = true;
        }
        if (_indexDue)
        {
            WriteIndex(now);
            _indexDue = false;
        }
        _snapshotTicks += Stopwatch.GetTimestamp() - sweeping;
    }

    private void WriteIndex(long now)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "state-index");
            writer.WriteNumber("formatVersion", BrowserStateSnapshot.FormatVersion);
            writer.WriteNumber("eventKey", _lastKey);
            writer.WriteNumber("eventTime", _lastKeyTime);
            writer.WriteNumber("time", now);
            writer.WriteNumber("snapshotInterval", SnapshotInterval);
            writer.WriteStartArray("documents");
            foreach (var (key, tracked) in _tracked)
            {
                writer.WriteStartObject();
                writer.WriteString("documentKey", key);
                writer.WriteNumber("lastTime", _builder.Documents[key].LastTime);
                if (tracked.SnapshotEventKey >= 0)
                {
                    writer.WriteNumber("snapshotEventKey", tracked.SnapshotEventKey);
                    writer.WriteNumber("snapshotTime", tracked.SnapshotTime);
                    writer.WriteNumber("snapshotLogTime", tracked.SnapshotLogTime);
                }
                else
                {
                    writer.WriteNull("snapshotEventKey");
                    writer.WriteNull("snapshotTime");
                    writer.WriteNull("snapshotLogTime");
                }
                if (tracked.FirstUnsnapshottedTime is { } first)
                {
                    writer.WriteNumber("firstUnsnapshottedTime", first);
                }
                else
                {
                    writer.WriteNull("firstUnsnapshottedTime");
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        var data = buffer.WrittenSpan.ToArray();
        _write(IndexTopic, now, data);
        _indexRecords++;
        _indexBytes += data.Length;
    }
}
