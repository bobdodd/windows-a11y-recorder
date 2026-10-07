using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// One animation of a recorded document at a recording time (slice 4g,
/// protocol 0.53), from its latest animation-updated record at or before
/// that time. Times are milliseconds on the animation's timeline, except
/// the recording times, which are nanoseconds. The current time, iteration,
/// and progress are those at the time asked for: computed from the record
/// for a running animation on a document timeline, and as recorded
/// otherwise, as <see cref="CurrentTimeBasis"/> says. The progress is the
/// directed progress of the Web Animations model, before the easing.
/// </summary>
public sealed record RecordedAnimationState(
    string SequenceNumber,
    string Kind,
    string? Name,
    long? TargetNodeId,
    string? PseudoElement,
    string PlayState,
    bool Pending,
    double? PlaybackRate,
    double? StartTimeMilliseconds,
    string TimelineKind,
    long? TimelineSourceNodeId,
    string? TimelineAxis,
    RecordedAnimationTiming? Timing,
    int? CompositorAnimationId,
    long FirstRecordedNanoseconds,
    long RecordedNanoseconds,
    double? CurrentTimeMilliseconds,
    string CurrentTimeBasis,
    double? CurrentIteration,
    double? Progress,
    string Phase,
    double? RecordedProgress,
    double? RecordedCurrentIteration)
{
    /// <summary>
    /// The start time as a recording time, when the animation is on a
    /// document timeline whose zero time is recorded.
    /// </summary>
    public long? StartRecordingNanoseconds { get; init; }
}

/// <summary>
/// An animation effect's timing as recorded: delays and iteration duration
/// in milliseconds, iterations null when infinite.
/// </summary>
public sealed record RecordedAnimationTiming(
    double DelayMilliseconds,
    double EndDelayMilliseconds,
    double IterationStart,
    double? Iterations,
    double DurationMilliseconds,
    string Direction,
    string Fill,
    string Easing);

/// <summary>
/// The animations of a recorded document at a recording time, and what the
/// evidence panel says of them. Recorded is false when the recording holds
/// no animation records, as before protocol 0.53.
/// </summary>
public sealed record RecordedAnimations(
    IReadOnlyList<RecordedAnimationState> Animations,
    bool Recorded,
    IReadOnlyList<string> Notes)
{
    public static RecordedAnimations None { get; } = new([], false, []);
}

/// <summary>
/// Reads the animations of a recorded document (slice 4g) from its
/// animation-updated and animation-removed records and the browser's clock
/// synchronizations, given in recording order.
/// </summary>
public sealed class RecordedAnimationReader
{
    private readonly string _documentToken;
    private readonly long _recordingFrequency;
    private readonly Dictionary<(string? Instance, long? Process), decimal> _frequencies = [];
    private readonly Dictionary<(string? Instance, long? Process, string Sequence), Entry> _animations = [];
    private bool _recorded;

    private sealed class Entry
    {
        public required long First { get; init; }
        public required List<(long Time, JsonElement Payload, long? ZeroNanoseconds)> Updates { get; init; }
        public long? Removed { get; set; }
    }

    /// <param name="documentKey">The document's state key, its token and identity.</param>
    /// <param name="recordingFrequency">The recording's clock frequency, for a process with no clock synchronization record.</param>
    public RecordedAnimationReader(string documentKey, long recordingFrequency)
    {
        ArgumentNullException.ThrowIfNull(documentKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordingFrequency, 1);
        _documentToken = documentKey.Split(' ', 2)[0];
        _recordingFrequency = recordingFrequency;
    }

    /// <summary>The channels whose records the reader reads.</summary>
    public static IReadOnlySet<string> Channels { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "browser.lifecycle",
        BrowserEvidenceChannels.Animation,
    };

    /// <summary>Takes one record, in the order recorded.</summary>
    public void Add(RecorderEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var payload = record.Payload;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (record.Channel == "browser.lifecycle")
        {
            if (record.EventType == "browser-clock-synchronized" &&
                Text(payload, "monotonicFrequency") is { } frequencyText &&
                decimal.TryParse(frequencyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var frequency) &&
                frequency > 0)
            {
                _frequencies.TryAdd((Text(payload, "browserInstanceId"), Int64(payload, "processId")), frequency);
            }
            return;
        }
        if (record.Channel != BrowserEvidenceChannels.Animation)
        {
            return;
        }
        _recorded = true;
        var context = payload.TryGetProperty("context", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        if (Text(context, "documentToken") != _documentToken || Text(payload, "sequenceNumber") is not { } sequence)
        {
            return;
        }
        var key = (Text(context, "browserInstanceId"), Int64(context, "processId"), sequence);
        switch (record.EventType)
        {
            case BrowserEvidenceEventTypes.AnimationUpdated:
                if (!_animations.TryGetValue(key, out var entry) || entry.Removed is not null)
                {
                    entry = new Entry { First = record.MonotonicNanoseconds, Updates = [] };
                    _animations[key] = entry;
                }
                entry.Updates.Add((record.MonotonicNanoseconds, payload.Clone(), ZeroNanoseconds(record, payload, key.Item1, key.Item2)));
                break;
            case BrowserEvidenceEventTypes.AnimationRemoved:
                if (_animations.TryGetValue(key, out var removed) && removed.Removed is null)
                {
                    removed.Removed = record.MonotonicNanoseconds;
                }
                break;
        }
    }

    // The recording time of a document timeline's zero time: the record's
    // own time plus the counter ticks from its native timestamp to the zero
    // time, at the process's clock frequency.
    private long? ZeroNanoseconds(RecorderEvent record, JsonElement payload, string? instance, long? process)
    {
        if (!payload.TryGetProperty("timeline", out var timeline) || timeline.ValueKind != JsonValueKind.Object ||
            Text(timeline, "kind") != "document" ||
            Text(timeline, "zeroTicks") is not { } ticksText ||
            !decimal.TryParse(ticksText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) ||
            record.NativeTimestamp is not { } native)
        {
            return null;
        }
        var frequency = _frequencies.TryGetValue((instance, process), out var recorded) ? recorded : _recordingFrequency;
        var offset = decimal.Round((ticks - native.Value) * 1_000_000_000m / frequency, MidpointRounding.AwayFromZero);
        return record.MonotonicNanoseconds + (long)offset;
    }

    /// <summary>
    /// The document's animations at the recording time: each one whose
    /// latest record at or before it is running, paused, or pending, or
    /// finished with a fill that holds its effect, and that was not removed
    /// at or before it, in the order first recorded.
    /// </summary>
    public RecordedAnimations At(long nanoseconds)
    {
        if (!_recorded)
        {
            return RecordedAnimations.None;
        }
        var result = new List<RecordedAnimationState>();
        foreach (var entry in _animations.Values.OrderBy(item => item.First))
        {
            if (entry.First > nanoseconds || entry.Removed is { } removed && removed <= nanoseconds)
            {
                continue;
            }
            var latest = entry.Updates.LastOrDefault(update => update.Time <= nanoseconds);
            if (latest.Payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            if (State(latest.Payload, latest.Time, latest.ZeroNanoseconds, entry.First, nanoseconds) is { } state)
            {
                result.Add(state);
            }
        }
        return new RecordedAnimations(result, true, []);
    }

    private static RecordedAnimationState? State(JsonElement payload, long recorded, long? zero, long first, long at)
    {
        var playState = Text(payload, "playState") ?? "idle";
        var pending = payload.TryGetProperty("pending", out var pendingValue) && pendingValue.ValueKind == JsonValueKind.True;
        var timeline = payload.TryGetProperty("timeline", out var timelineValue) && timelineValue.ValueKind == JsonValueKind.Object
            ? timelineValue
            : default;
        var effect = payload.TryGetProperty("effect", out var effectValue) && effectValue.ValueKind == JsonValueKind.Object
            ? effectValue
            : default;
        RecordedAnimationTiming? timing = effect.ValueKind == JsonValueKind.Object
            ? new RecordedAnimationTiming(
                Number(effect, "delayMilliseconds") ?? 0,
                Number(effect, "endDelayMilliseconds") ?? 0,
                Number(effect, "iterationStart") ?? 0,
                Number(effect, "iterations"),
                Number(effect, "durationMilliseconds") ?? 0,
                Text(effect, "direction") ?? "normal",
                Text(effect, "fill") ?? "auto",
                Text(effect, "easing") ?? "")
            : null;
        var timelineKind = Text(timeline, "kind") ?? "none";
        var playbackRate = Number(payload, "playbackRate");
        var start = Number(payload, "startTimeMilliseconds");
        var recordedCurrent = Number(payload, "currentTimeMilliseconds");

        double? current;
        string basis;
        if (playState == "running" && !pending && timelineKind == "document" && zero is { } zeroTime &&
            start is { } startTime && playbackRate is { } rate)
        {
            var timelineRate = Number(timeline, "playbackRate") ?? 1;
            var timelineTime = (at - zeroTime) / 1e6 * timelineRate;
            current = (timelineTime - startTime) * rate;
            basis = "computed";
        }
        else
        {
            current = recordedCurrent;
            basis = "recorded";
        }

        var (phase, iteration, progress) = timing is null ? ("none", null, null) : Progress(timing, current, playbackRate ?? 1);
        var listed = playState switch
        {
            "running" or "paused" or "pending" => true,
            "finished" => phase != "none" && progress is not null,
            _ => pending,
        };
        if (!listed)
        {
            return null;
        }
        return new RecordedAnimationState(
            Text(payload, "sequenceNumber") ?? "",
            Text(payload, "kind") ?? "web-animation",
            Text(payload, "name"),
            Int64(payload, "targetNodeId"),
            Text(payload, "pseudoElement"),
            playState,
            pending,
            playbackRate,
            start,
            timelineKind,
            Int64(timeline, "sourceNodeId"),
            Text(timeline, "axis"),
            timing,
            Int64(payload, "compositorAnimationId") is { } compositor ? (int)compositor : null,
            first,
            recorded,
            current,
            basis,
            iteration,
            progress,
            phase,
            Number(effect, "progress"),
            Number(effect, "currentIteration"))
        {
            StartRecordingNanoseconds = timelineKind == "document" && zero is { } zeroAt && start is { } startAt
                ? zeroAt + (long)Math.Round(startAt * 1e6 / (Number(timeline, "playbackRate") is { } r && r != 0 ? r : 1))
                : null,
        };
    }

    /// <summary>
    /// The phase, current iteration, and directed progress of an effect at a
    /// local time, by the Web Animations procedures ("Calculating the active
    /// time", "the overall progress", "the simple iteration progress", "the
    /// current iteration", and "the directed progress",
    /// https://www.w3.org/TR/web-animations-1/#calculating-the-active-time and the sections after it). The
    /// phase is "before", "active", "after", or "none" for an unresolved
    /// local time; the iteration and progress are null when the effect is
    /// not in effect. A fill of auto is none, as for a keyframe effect.
    /// </summary>
    public static (string Phase, double? CurrentIteration, double? Progress) Progress(
        RecordedAnimationTiming timing,
        double? localTime,
        double playbackRate)
    {
        ArgumentNullException.ThrowIfNull(timing);
        if (localTime is not { } local)
        {
            return ("none", null, null);
        }
        var iterations = timing.Iterations ?? double.PositiveInfinity;
        var duration = timing.DurationMilliseconds;
        var active = duration == 0 || iterations == 0 ? 0 : duration * iterations;
        var endTime = Math.Max(timing.DelayMilliseconds + active + timing.EndDelayMilliseconds, 0);
        var beforeActive = Math.Max(Math.Min(timing.DelayMilliseconds, endTime), 0);
        var activeAfter = Math.Max(Math.Min(timing.DelayMilliseconds + active, endTime), 0);
        var backwards = playbackRate < 0;
        string phase;
        if (local < beforeActive || (backwards && local == beforeActive))
        {
            phase = "before";
        }
        else if (local > activeAfter || (!backwards && local == activeAfter))
        {
            phase = "after";
        }
        else
        {
            phase = "active";
        }
        var fill = timing.Fill;
        double? activeTime = phase switch
        {
            "before" when fill is "backwards" or "both" => Math.Max(local - timing.DelayMilliseconds, 0),
            "active" => local - timing.DelayMilliseconds,
            "after" when fill is "forwards" or "both" => Math.Max(Math.Min(local - timing.DelayMilliseconds, active), 0),
            _ => null,
        };
        if (activeTime is not { } activeValue)
        {
            return (phase, null, null);
        }
        double overall;
        if (duration == 0)
        {
            overall = phase == "before" ? timing.IterationStart : timing.IterationStart + iterations;
        }
        else
        {
            overall = activeValue / duration + timing.IterationStart;
        }
        var simple = double.IsInfinity(overall) ? timing.IterationStart % 1 : overall % 1;
        if (simple == 0 && phase is "active" or "after" && activeValue == active && iterations != 0)
        {
            simple = 1;
        }
        double current;
        if (phase == "after" && double.IsPositiveInfinity(iterations))
        {
            current = double.PositiveInfinity;
        }
        else if (simple == 1)
        {
            current = Math.Floor(overall) - 1;
        }
        else
        {
            current = Math.Floor(overall);
        }
        var forwards = timing.Direction switch
        {
            "reverse" => false,
            "alternate" => double.IsInfinity(current) || current % 2 == 0,
            "alternate-reverse" => double.IsInfinity(current) || (current + 1) % 2 == 0,
            _ => true,
        };
        return (phase, double.IsInfinity(current) ? null : current, forwards ? simple : 1 - simple);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Int64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var result)
            ? result
            : null;

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var result)
            ? result
            : null;
}
