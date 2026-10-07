using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// What playback needs when a recording opens, derived from its events:
/// counts, occupancy, the events that frames, audio tracks, and browser
/// navigation are built from, counts of the other browser events, and the
/// presented layout checkpoints and frame composition times that choose
/// each navigation's first frame. Built by <see cref="PlaybackIndexBuilder"/>
/// while a recording is written, or from its stored events.
/// </summary>
public sealed record PlaybackIndex
{
    // Version 2 keeps the page popup and popup widget records (protocol
    // 0.43 and 0.44) whole. Version 3 adds the frame documents (protocol
    // 0.55, slice 5b).
    public const int CurrentVersion = 3;

    public required int Version { get; init; }

    /// <summary>The latest event time.</summary>
    public required long LatestTime { get; init; }

    public required IReadOnlyDictionary<string, long> ChannelCounts { get; init; }

    public required PlaybackOccupancy Occupancy { get; init; }

    /// <summary>
    /// The desktop frame, audio stream start, browser navigation, and page
    /// popup and popup widget events, with the payload properties playback
    /// reads.
    /// </summary>
    public required IReadOnlyList<PlaybackIndexEvent> Events { get; init; }

    /// <summary>The other browser events, counted by segment between navigation starts.</summary>
    public required IReadOnlyList<BrowserEventCount> BrowserCounts { get; init; }

    /// <summary>
    /// False when a navigation start was added after an event later than it
    /// was counted, so <see cref="BrowserCounts"/> may put events in the
    /// wrong segment.
    /// </summary>
    public required bool BrowserCountsExact { get; init; }

    public required IReadOnlyList<BrowserPresentedCheckpoint> PresentedCheckpoints { get; init; }

    public required IReadOnlyList<CapturedFrameComposition> FrameCompositions { get; init; }

    /// <summary>
    /// The first DOM walk of each document that named its frame (protocol
    /// 0.55, slice 5b), so that the documents of a frame are found without
    /// reading every document's state.
    /// </summary>
    public IReadOnlyList<FrameDocumentRecord> FrameDocuments { get; init; } = [];
}

/// <summary>
/// The first DOM walk of a document that named its frame (protocol 0.55):
/// when it started, the document's key, the DevTools token of its frame,
/// whether that is a main frame, and the renderer process it was recorded in.
/// </summary>
public sealed record FrameDocumentRecord(long Time, string DocumentKey, string FrameToken, bool? MainFrame, int? ProcessId);

/// <summary>An event kept whole in a playback index, with the payload properties playback reads.</summary>
public sealed record PlaybackIndexEvent(
    long EventKey,
    string EventId,
    string EvidenceClass,
    string Channel,
    string EventType,
    long MonotonicNanoseconds,
    JsonElement Payload);

/// <summary>
/// The number of browser events of one identity, type, and truncation in
/// the segment that starts at <see cref="SegmentStart"/>: at or after that
/// navigation start and before the next. The first segment starts at
/// <see cref="long.MinValue"/>.
/// </summary>
public sealed record BrowserEventCount(
    long SegmentStart,
    string? BrowserInstanceId,
    int? ProcessId,
    string? DocumentId,
    string? DocumentToken,
    string EventType,
    bool Truncated,
    int Count);

/// <summary>
/// Derives a <see cref="PlaybackIndex"/> from events added in any order.
/// Browser events other than navigation events are held for
/// <c>holdback</c> of recording time after the latest browser event, and
/// then counted into the segment between navigation starts that holds them.
/// Not safe for use from more than one thread at a time.
/// </summary>
public sealed class PlaybackIndexBuilder
{
    private const string NavigationChannel = "browser.navigation";

    private static readonly IReadOnlySet<string> PlaybackProperties =
        SessionPlaybackArchiveBuilder.PayloadProperties.ToHashSet(StringComparer.Ordinal);

    private readonly long _clockFrequency;
    private readonly long _holdback;
    private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);
    private readonly OccupancyBitmap _occupancy = new();
    private readonly List<PlaybackIndexEvent> _events = [];
    private readonly List<long> _boundaries = [];
    private readonly Dictionary<CountKey, int> _keys = [];
    private readonly List<CountKey> _keyList = [];
    private readonly PriorityQueue<(long Time, int Key), long> _pending = new();
    private readonly Dictionary<(long Segment, int Key), int> _segmentCounts = [];
    private readonly List<CapturedFrameComposition> _compositions = [];
    private readonly Dictionary<string, FrameDocumentRecord> _frameDocuments = new(StringComparer.Ordinal);
    private readonly List<LayoutCompletion> _completions = [];
    // Protocol 0.46: a change set read for the rendering update its named
    // checkpoint recorded, by browser instance, process, and change set,
    // and the completion time of each, by its checkpoint.
    private readonly Dictionary<(string?, long?, string), string> _checkpointUpdateChangeSets = [];
    private readonly Dictionary<(string?, long?, string), long> _checkpointUpdateCompletions = [];
    private readonly List<PresentationRequest> _requests = [];
    private readonly List<PresentationFeedback> _feedback = [];
    private readonly List<ClockSynchronization> _synchronizations = [];
    private readonly Dictionary<long, long> _latestSettled = [];
    private long _latestBrowserTime = long.MinValue;
    private bool _exact = true;

    /// <param name="clockFrequency">
    /// The recording's clock frequency, used for a browser process whose
    /// clock synchronization was not recorded.
    /// </param>
    /// <param name="holdback">
    /// How long, in recording time, browser events are held before they are
    /// counted. Zero counts them when they are added, which is exact when
    /// every navigation start was given to <see cref="AddNavigationStarts"/>.
    /// </param>
    public PlaybackIndexBuilder(long clockFrequency, TimeSpan holdback)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(clockFrequency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(holdback, TimeSpan.Zero);
        _clockFrequency = clockFrequency;
        _holdback = checked(holdback.Ticks * 100);
    }

    /// <summary>
    /// Whether an event starts a segment of the browser counts: a navigation
    /// start with a payload.
    /// </summary>
    /// <summary>
    /// True for the records that say which page popups were open and where:
    /// a popup's opening, window requests, and closing, and the browser's
    /// popup widget records. They are few, and kept with their whole payloads
    /// for recreation.
    /// </summary>
    public static bool IsPopupRecord(string channel, string eventType) =>
        channel == "browser.interaction" &&
        (eventType is "page-popup-opened" or "page-popup-window-rect" or "page-popup-closed" ||
            eventType.StartsWith("popup-widget-", StringComparison.Ordinal));

    public static bool IsNavigationStart(RecorderEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Channel == NavigationChannel &&
            record.EventType == "navigation-started" &&
            record.Payload.ValueKind == JsonValueKind.Object;
    }

    /// <summary>
    /// Adds navigation start times known before the events are added, so a
    /// start added later does not divide a segment already counted. Must be
    /// called before <see cref="Add"/>.
    /// </summary>
    public void AddNavigationStarts(IEnumerable<long> times)
    {
        ArgumentNullException.ThrowIfNull(times);
        if (_counts.Count > 0)
        {
            throw new InvalidOperationException("Navigation starts must be added before events.");
        }

        foreach (var time in times)
        {
            AddBoundary(time);
        }
    }

    /// <summary>The default holdback while recording.</summary>
    public static TimeSpan RecordingHoldback { get; } = TimeSpan.FromSeconds(30);

    public void Add(long eventKey, RecorderEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var channel = record.Channel;
        var time = record.MonotonicNanoseconds;
        _counts[channel] = _counts.GetValueOrDefault(channel) + 1;
        _occupancy.Add(channel, time);
        var payload = record.Payload;

        if (channel == "graphics.desktop.frames" && record.EventType == "desktop-frame")
        {
            Keep(eventKey, record);
            _compositions.Add(new CapturedFrameComposition(time, CompositionTime(time, payload)));
            return;
        }

        if (channel.StartsWith("audio.", StringComparison.Ordinal) &&
            record.EventType == "audio-stream-started")
        {
            Keep(eventKey, record);
            return;
        }

        if (!channel.StartsWith("browser.", StringComparison.Ordinal) ||
            payload.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (channel == NavigationChannel)
        {
            Keep(eventKey, record);
            if (IsNavigationStart(record))
            {
                AddBoundary(time);
            }

            return;
        }

        if (IsPopupRecord(channel, record.EventType))
        {
            Keep(eventKey, record);
        }

        AddFrameDocument(channel, record.EventType, time, payload);

        CollectPresentation(eventKey, record);
        var context = payload.TryGetProperty("context", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        var key = new CountKey(
            ReadString(context, "browserInstanceId"),
            ReadInt32(context, "processId"),
            ReadString(context, "documentId"),
            ReadString(context, "documentToken"),
            record.EventType,
            ReadBoolean(payload, "truncated") ?? false);
        if (!_keys.TryGetValue(key, out var id))
        {
            id = _keyList.Count;
            _keys.Add(key, id);
            _keyList.Add(key);
        }

        _pending.Enqueue((time, id), time);
        _latestBrowserTime = Math.Max(_latestBrowserTime, time);
        Settle(_holdback == 0 ? long.MaxValue : _latestBrowserTime - _holdback);
    }

    public PlaybackIndex Build()
    {
        Settle(long.MaxValue);
        return new PlaybackIndex
        {
            Version = PlaybackIndex.CurrentVersion,
            LatestTime = _occupancy.LatestTime,
            ChannelCounts = new Dictionary<string, long>(_counts, StringComparer.Ordinal),
            Occupancy = _occupancy.Export(),
            Events = [.. _events],
            BrowserCounts = _segmentCounts
                .OrderBy(pair => pair.Key.Segment)
                .ThenBy(pair => pair.Key.Key)
                .Select(pair =>
                {
                    var key = _keyList[pair.Key.Key];
                    return new BrowserEventCount(
                        pair.Key.Segment,
                        key.BrowserInstanceId,
                        key.ProcessId,
                        key.DocumentId,
                        key.DocumentToken,
                        key.EventType,
                        key.Truncated,
                        pair.Value);
                })
                .ToArray(),
            BrowserCountsExact = _exact,
            PresentedCheckpoints = PresentedCheckpoints(),
            FrameCompositions = [.. _compositions],
            FrameDocuments = [.. _frameDocuments.Values
                .OrderBy(item => item.Time)
                .ThenBy(item => item.DocumentKey, StringComparer.Ordinal)]
        };
    }

    // Slice 5b: the earliest DOM walk of each document that named its frame
    // token. Events may arrive out of order, so the earliest is kept.
    private void AddFrameDocument(string channel, string eventType, long time, JsonElement payload)
    {
        if (channel != "browser.dom" || eventType != "dom-checkpoint-started" ||
            !payload.TryGetProperty("frameToken", out var token) || token.ValueKind != JsonValueKind.String ||
            token.GetString() is not { Length: > 0 } frameToken ||
            DomTreeRebuilder.DocumentKey(payload) is not { } key)
        {
            return;
        }
        if (_frameDocuments.TryGetValue(key, out var known) && known.Time <= time)
        {
            return;
        }
        var context = payload.TryGetProperty("context", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
        _frameDocuments[key] = new FrameDocumentRecord(
            time,
            key,
            frameToken,
            payload.TryGetProperty("mainFrame", out var main) && main.ValueKind is JsonValueKind.True or JsonValueKind.False ? main.GetBoolean() : null,
            ReadInt32(context, "processId"));
    }

    private void Keep(long eventKey, RecorderEvent record) =>
        _events.Add(new PlaybackIndexEvent(
            eventKey,
            record.EventId,
            record.EvidenceClass,
            record.Channel,
            record.EventType,
            record.MonotonicNanoseconds,
            IsPopupRecord(record.Channel, record.EventType) && record.Payload.ValueKind == JsonValueKind.Object
                ? record.Payload.Clone()
                : PlaybackPayload(record.Payload)));

    private void AddBoundary(long time)
    {
        var index = _boundaries.BinarySearch(time);
        if (index >= 0)
        {
            return;
        }

        // An event at or after this start was counted into the segment
        // that the start now divides.
        if (_latestSettled.TryGetValue(SegmentOf(time), out var latest) && latest >= time)
        {
            _exact = false;
        }

        _boundaries.Insert(~index, time);
    }

    private void Settle(long before)
    {
        while (_pending.TryPeek(out var item, out var time) && time < before)
        {
            _pending.Dequeue();
            var segment = SegmentOf(time);
            var slot = (segment, item.Key);
            _segmentCounts[slot] = _segmentCounts.GetValueOrDefault(slot) + 1;
            _latestSettled[segment] = _latestSettled.TryGetValue(segment, out var latest)
                ? Math.Max(latest, time)
                : time;
        }
    }

    // The start of the segment that holds a time: the latest navigation
    // start at or before it.
    private long SegmentOf(long time)
    {
        var index = _boundaries.BinarySearch(time);
        return index >= 0 ? _boundaries[index]
            : ~index == 0 ? long.MinValue
            : _boundaries[~index - 1];
    }

    // The earliest composition time of the frame's monitors, or the event's
    // time when none was recorded.
    private static long CompositionTime(long time, JsonElement payload)
    {
        long? earliest = null;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("monitorFrames", out var monitors) &&
            monitors.ValueKind == JsonValueKind.Array)
        {
            foreach (var monitor in monitors.EnumerateArray())
            {
                if (ReadInt64(monitor, "compositedAtNanoseconds") is { } composed)
                {
                    earliest = earliest is { } value ? Math.Min(value, composed) : composed;
                }
            }
        }

        return earliest ?? time;
    }

    private void CollectPresentation(long eventKey, RecorderEvent record)
    {
        var payload = record.Payload;
        var context = payload.TryGetProperty("context", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        switch ((record.Channel, record.EventType))
        {
            // From protocol 0.35 a rendering update that was not walked is
            // presented after its layout change set, which takes the
            // checkpoint's place. The two identities never share a value.
            case ("browser.layout", "layout-changes-started"):
                if (ReadBoolean(payload, "checkpointUpdate") == true &&
                    ReadString(payload, "changeSetId") is { } updateChangeSet &&
                    ReadString(payload, "layoutCheckpointId") is { } updateCheckpoint)
                {
                    _checkpointUpdateChangeSets[(ReadString(context, "browserInstanceId"), ReadInt64(context, "processId"), updateChangeSet)] =
                        updateCheckpoint;
                }

                break;
            case ("browser.layout", "layout-checkpoint-completed"):
            case ("browser.layout", "layout-changes-completed"):
                if (record.EventType == "layout-changes-completed" &&
                    ReadString(payload, "changeSetId") is { } completedChangeSet &&
                    _checkpointUpdateChangeSets.TryGetValue(
                        (ReadString(context, "browserInstanceId"), ReadInt64(context, "processId"), completedChangeSet),
                        out var ofCheckpoint))
                {
                    _checkpointUpdateCompletions[(ReadString(context, "browserInstanceId"), ReadInt64(context, "processId"), ofCheckpoint)] =
                        record.MonotonicNanoseconds;
                }

                if (ReadString(context, "documentToken") is { } token &&
                    (record.EventType == "layout-checkpoint-completed"
                        ? ReadString(payload, "checkpointId")
                        : ReadString(payload, "changeSetId")) is { } checkpoint)
                {
                    _completions.Add(new LayoutCompletion(
                        ReadString(context, "browserInstanceId"),
                        ReadInt64(context, "processId"),
                        token,
                        checkpoint,
                        record.MonotonicNanoseconds));
                }

                break;
            case ("browser.presentation", "presentation-requested"):
                if ((ReadString(payload, "layoutCheckpointId") ??
                        ReadString(payload, "layoutChangeSetId")) is { } layoutCheckpoint &&
                    ReadString(payload, "requestId") is { } request)
                {
                    _requests.Add(new PresentationRequest(
                        ReadString(context, "browserInstanceId"),
                        ReadInt64(context, "processId"),
                        layoutCheckpoint,
                        request));
                }

                break;
            case ("browser.presentation", "presentation-feedback"):
                if (ReadString(payload, "requestId") is { } requestId &&
                    ReadString(payload, "presentedTicks") is { } presentedText &&
                    decimal.TryParse(presentedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var presented) &&
                    record.NativeTimestamp is { } native &&
                    !HasFlag(payload, "failure"))
                {
                    _feedback.Add(new PresentationFeedback(
                        ReadString(context, "browserInstanceId"),
                        ReadInt64(context, "processId"),
                        requestId,
                        presented,
                        native.Value,
                        record.MonotonicNanoseconds));
                }

                break;
            case ("browser.lifecycle", "browser-clock-synchronized"):
                if (ReadString(payload, "monotonicFrequency") is { } frequencyText &&
                    decimal.TryParse(frequencyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var frequency))
                {
                    _synchronizations.Add(new ClockSynchronization(
                        ReadString(payload, "browserInstanceId"),
                        ReadInt64(payload, "processId"),
                        frequency,
                        eventKey));
                }

                break;
        }
    }

    // As the database reader's query: a request joins its checkpoint, and
    // feedback its request, by browser instance, process, and identifier.
    // The presentation time maps to session time through the feedback
    // event's native timestamp, scaled by the frequency in the process's
    // first clock synchronization, or the recording's.
    private BrowserPresentedCheckpoint[] PresentedCheckpoints()
    {
        var frequencies = _synchronizations
            .GroupBy(item => (item.BrowserInstanceId, item.ProcessId))
            .ToDictionary(group => group.Key, group => group.MinBy(item => item.EventKey)!.Frequency);
        var requests = _requests.ToLookup(item => (item.BrowserInstanceId, item.ProcessId, item.LayoutCheckpointId));
        var feedback = _feedback.ToLookup(item => (item.BrowserInstanceId, item.ProcessId, item.RequestId));
        var result = new List<BrowserPresentedCheckpoint>();
        foreach (var completion in _completions)
        {
            if (completion.BrowserInstanceId is null || completion.ProcessId is null)
            {
                continue;
            }

            foreach (var request in requests[(completion.BrowserInstanceId, completion.ProcessId, completion.CheckpointId)])
            {
                foreach (var item in feedback[(completion.BrowserInstanceId, completion.ProcessId, request.RequestId)])
                {
                    var frequency = frequencies.TryGetValue((item.BrowserInstanceId, item.ProcessId), out var recorded)
                        ? recorded
                        : _clockFrequency;
                    if (frequency == 0)
                    {
                        continue;
                    }

                    var offset = decimal.Round(
                        (item.PresentedTicks - item.NativeValue) * 1_000_000_000m / frequency,
                        MidpointRounding.AwayFromZero);
                    // A walked update is presented through its checkpoint,
                    // and its own change set follows the checkpoint, so the
                    // update's state is cut after that change set.
                    var cut = _checkpointUpdateCompletions.TryGetValue(
                        (completion.BrowserInstanceId, completion.ProcessId, completion.CheckpointId),
                        out var updateCompleted)
                        ? updateCompleted
                        : completion.Time;
                    result.Add(new BrowserPresentedCheckpoint(
                        completion.BrowserInstanceId,
                        completion.DocumentToken,
                        cut,
                        item.Time + (long)offset));
                }
            }
        }

        return [.. result];
    }

    /// <summary>
    /// The payload properties playback reads, as a new element, with the
    /// properties whose value is null left out.
    /// </summary>
    public static JsonElement PlaybackPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in payload.EnumerateObject())
            {
                if (PlaybackProperties.Contains(property.Name) &&
                    property.Value.ValueKind != JsonValueKind.Null)
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static bool HasFlag(JsonElement payload, string flag) =>
        payload.TryGetProperty("flags", out var flags) &&
        flags.ValueKind == JsonValueKind.Array &&
        flags.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String && item.GetString() == flag);

    private static string? ReadString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static int? ReadInt32(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetInt32(out var result)
            ? result
            : null;

    private static long? ReadInt64(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetInt64(out var result)
            ? result
            : null;

    private static bool? ReadBoolean(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var item) &&
        item.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? item.GetBoolean()
            : null;

    private readonly record struct CountKey(
        string? BrowserInstanceId,
        int? ProcessId,
        string? DocumentId,
        string? DocumentToken,
        string EventType,
        bool Truncated);

    private sealed record LayoutCompletion(
        string? BrowserInstanceId,
        long? ProcessId,
        string DocumentToken,
        string CheckpointId,
        long Time);

    private sealed record PresentationRequest(
        string? BrowserInstanceId,
        long? ProcessId,
        string LayoutCheckpointId,
        string RequestId);

    private sealed record PresentationFeedback(
        string? BrowserInstanceId,
        long? ProcessId,
        string RequestId,
        decimal PresentedTicks,
        long NativeValue,
        long Time);

    private sealed record ClockSynchronization(
        string? BrowserInstanceId,
        long? ProcessId,
        decimal Frequency,
        long EventKey);
}
