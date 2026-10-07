using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Globalization;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>A collector whose events a recording file holds, and the channels it wrote.</summary>
public sealed record EventCollector(
    string InstanceId,
    string CollectorType,
    string ProducerVersion,
    string CaptureMethod,
    IReadOnlyList<string> Channels);

/// <summary>
/// A batch target that the writer asks, while it has nothing else to do,
/// whether written data is waiting to be stored, so that data does not wait
/// in memory for the next batch.
/// </summary>
public interface IPeriodicBatchTarget
{
    /// <summary>How long until data is due to be stored, or null when none is waiting.</summary>
    TimeSpan? TimeUntilDue();

    /// <summary>Stores the data that is due. Throws when the store is not accepting writes.</summary>
    void WriteDue();
}

/// <summary>
/// Stores a recording's event batches in its recording file. Every event is
/// stored as a message on a channel for its channel and collector, in the
/// stream its channel belongs to, and the writer's rejections and omissions
/// are stored as messages of the recorder stream. Nothing an event holds is
/// refused. Batches must be written one at a time and in order.
/// </summary>
public sealed class RecordingFileBatchTarget : IEventBatchTarget, IPeriodicBatchTarget, IDisposable
{
    public const string WriterTopic = "recorder.writer";

    private readonly RecordingFileWriter _file;
    private readonly PlaybackIndexBuilder _index;
    private readonly WriterTimings? _timings;
    private readonly Dictionary<(string Channel, string Instance, string Type, string Version, string Method), ushort> _channels = [];
    private readonly ArrayBufferWriter<byte> _encoded = new(64 * 1024);
    private readonly object _gate = new();
    private long _lastEventKey = -1;
    private int _lastRejection = -1;
    private int _lastOmission = -1;
    private ushort _writerChannel;
    private readonly RecordingFileStateRecorder? _state;
    private readonly Dictionary<string, ushort> _stateChannels = new(StringComparer.Ordinal);
    private readonly List<BufferedEvent> _stateEvents = [];

    public RecordingFileBatchTarget(
        string path,
        IReadOnlyDictionary<string, string> recording,
        RecordingFileWriterOptions? options = null,
        WriterTimings? timings = null,
        bool recordState = true)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var frequency = recording.TryGetValue("clockFrequency", out var text) &&
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : throw new ArgumentException("The recording's metadata must state its clock frequency.", nameof(recording));
        _index = new PlaybackIndexBuilder(frequency, PlaybackIndexBuilder.RecordingHoldback);
        _file = new RecordingFileWriter(path, options);
        _timings = timings;
        _file.AddMetadata("recording", recording);
        if (recordState)
        {
            _state = new RecordingFileStateRecorder(AddStateMessage, timings);
        }
    }

    /// <summary>What the state thread did, once the file is finished; null before, or when it made no snapshots.</summary>
    public RecordingFileStateSummary? StateSummary { get; private set; }

    public string Path => _file.Path;

    public IReadOnlyList<RecordingFileChunk> Chunks => _file.Chunks;

    public long MessageCount => _file.MessageCount;

    public long ByteLength => _file.Position;

    /// <summary>The collectors whose events have been added, in the order first seen.</summary>
    public IReadOnlyList<EventCollector> Collectors
    {
        get
        {
            lock (_gate)
            {
                return _channels.Keys
                    .GroupBy(key => (key.Instance, key.Type, key.Version, key.Method))
                    .Select(group => new EventCollector(
                        group.Key.Instance,
                        group.Key.Type,
                        group.Key.Version,
                        group.Key.Method,
                        group.Select(key => key.Channel).Distinct(StringComparer.Ordinal).ToArray()))
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// The stream a recorder channel is written in. The channels a
    /// document's state is rebuilt from are written in a stream of their own,
    /// so rebuilding it does not decompress the other browser records.
    /// </summary>
    public static string StreamOf(string channel) =>
        BrowserStateBuilder.IsStateChannel(channel) ? "browser-state"
        : channel.StartsWith("browser.", StringComparison.Ordinal) ? "browser"
        : channel.StartsWith("graphics.", StringComparison.Ordinal) ||
          channel.StartsWith("audio.", StringComparison.Ordinal) ? "media"
        : "desktop";

    public Task<IReadOnlyList<StoreRefusal>> WriteAsync(EventBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (_gate)
        {
            var adding = Stopwatch.GetTimestamp();
            // A batch retried after a failed write adds only what it did not
            // add before. Events, rejections, and omissions arrive in the
            // order of their keys and ordinals.
            foreach (var rejection in batch.Rejections.Where(item => item.Ordinal > _lastRejection))
            {
                _lastRejection = rejection.Ordinal;
                AddWriterRecord("writer-rejection", 0, writer =>
                {
                    writer.WriteNumber("ordinal", rejection.Ordinal);
                    writer.WriteString("reason", rejection.Reason);
                    writer.WriteString("channel", rejection.Channel);
                    writer.WriteString("eventType", rejection.EventType);
                    if (rejection.Sequence is { } sequence)
                    {
                        writer.WriteNumber("sequence", sequence);
                    }
                    else
                    {
                        writer.WriteNull("sequence");
                    }
                });
            }

            foreach (var omission in batch.Omissions.Where(item => item.Ordinal > _lastOmission))
            {
                _lastOmission = omission.Ordinal;
                AddWriterRecord("writer-omission", Math.Max(0, omission.FirstMonotonicNanoseconds), writer =>
                {
                    writer.WriteNumber("ordinal", omission.Ordinal);
                    writer.WriteNumber("firstMonotonicNanoseconds", omission.FirstMonotonicNanoseconds);
                    writer.WriteNumber("lastMonotonicNanoseconds", omission.LastMonotonicNanoseconds);
                    writer.WriteNumber("eventCount", omission.EventCount);
                });
            }

            foreach (var buffered in batch.Events)
            {
                if (buffered.EventKey <= _lastEventKey)
                {
                    continue;
                }

                _lastEventKey = buffered.EventKey;

                var record = buffered.Event;
                _encoded.Clear();
                RecordingEventCodec.Encode(_encoded, buffered.EventKey, record);
                _file.AddMessage(
                    ChannelFor(record),
                    unchecked((uint)record.Sequence),
                    record.MonotonicNanoseconds,
                    _encoded.WrittenSpan);
                _index.Add(buffered.EventKey, record);
                if (_state is not null && BrowserStateBuilder.IsStateChannel(record.Channel))
                {
                    _stateEvents.Add(buffered);
                }
            }

            if (_state is not null && _stateEvents.Count > 0)
            {
                if (_state.Pass(_stateEvents) is { } stopped)
                {
                    AddStateMessageLocked(
                        RecordingFileStateRecorder.IndexTopic,
                        Math.Max(0, _stateEvents[^1].Event.MonotonicNanoseconds),
                        RecordingFileStateRecorder.StoppedRecord(_stateEvents[^1].Event.MonotonicNanoseconds, stopped));
                }
                _stateEvents.Clear();
            }

            _timings?.Since("file.add", adding, batch.Events.Count);
            WritePending();
        }

        return Task.FromResult<IReadOnlyList<StoreRefusal>>([]);
    }

    public TimeSpan? TimeUntilDue()
    {
        lock (_gate)
        {
            return _file.PendingChunkCount > 0 ? TimeSpan.Zero : _file.TimeUntilDue();
        }
    }

    public void WriteDue()
    {
        lock (_gate)
        {
            WritePending();
        }
    }

    /// <summary>
    /// Writes the rest of the chunks, the playback index as an attachment
    /// after them, the summary, and the footer.
    /// </summary>
    public void Finish()
    {
        if (_state is not null && StateSummary is null)
        {
            // The state thread adds its last snapshots under the gate, so it
            // is completed before the gate is taken.
            var completing = Stopwatch.GetTimestamp();
            StateSummary = _state.Complete();
            _timings?.Since("complete.state", completing, StateSummary.Snapshots);
            _timings?.AddNote("state", JsonSerializer.Serialize(StateSummary));
        }

        lock (_gate)
        {
            var finishing = Stopwatch.GetTimestamp();
            _file.WriteAll();
            var indexing = Stopwatch.GetTimestamp();
            var index = _index.Build();
            var bytes = RecordingFilePlayback.SerializeIndex(index);
            _file.AddAttachment(
                RecordingFilePlayback.IndexAttachment,
                RecordingFilePlayback.IndexMediaType,
                Math.Max(0, index.LatestTime),
                bytes);
            _timings?.Since("complete.playback-index", indexing, bytes.Length);
            _file.Finish();
            _timings?.Since("complete.finish-file", finishing);
        }
    }

    public void Dispose()
    {
        _state?.Abandon();
        _file.Dispose();
    }

    /// <summary>
    /// True for a topic whose messages are recorder events, and false for the
    /// writer's records and the state thread's snapshots and index records.
    /// </summary>
    public static bool IsEventTopic(string topic) =>
        topic is not (WriterTopic or RecordingFileStateRecorder.SnapshotTopic or RecordingFileStateRecorder.IndexTopic);

    private void AddStateMessage(string topic, long time, byte[] data)
    {
        lock (_gate)
        {
            AddStateMessageLocked(topic, time, data);
        }
    }

    private void AddStateMessageLocked(string topic, long time, byte[] data)
    {
        if (!_stateChannels.TryGetValue(topic, out var channel))
        {
            channel = _file.AddChannel(
                topic == RecordingFileStateRecorder.SnapshotTopic
                    ? RecordingFileStateRecorder.SnapshotStream
                    : RecordingFileStateRecorder.IndexStream,
                topic,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["derived"] = "true" });
            _stateChannels.Add(topic, channel);
        }

        _file.AddMessage(channel, 0, Math.Max(0, time), data);
    }

    private void WritePending()
    {
        var writing = Stopwatch.GetTimestamp();
        var written = _file.WritePending();
        if (written > 0)
        {
            _timings?.Since("file.write-chunks", writing, written);
        }
    }

    private ushort ChannelFor(RecorderEvent record)
    {
        var key = (record.Channel, record.CollectorInstanceId, record.CollectorType, record.ProducerVersion, record.CaptureMethod);
        if (!_channels.TryGetValue(key, out var id))
        {
            id = _file.AddChannel(
                StreamOf(record.Channel),
                record.Channel,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["collectorInstanceId"] = record.CollectorInstanceId,
                    ["collectorType"] = record.CollectorType,
                    ["producerVersion"] = record.ProducerVersion,
                    ["captureMethod"] = record.CaptureMethod
                });
            _channels.Add(key, id);
        }

        return id;
    }

    private void AddWriterRecord(string kind, long time, Action<Utf8JsonWriter> write)
    {
        if (_writerChannel == 0)
        {
            _writerChannel = _file.AddChannel("recorder", WriterTopic, new Dictionary<string, string>());
        }

        _encoded.Clear();
        using (var writer = new Utf8JsonWriter(_encoded))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", kind);
            write(writer);
            writer.WriteEndObject();
        }

        _file.AddMessage(_writerChannel, 0, time, _encoded.WrittenSpan);
    }
}
