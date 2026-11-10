using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>A word the speech engine reached in an utterance, and when.</summary>
public sealed record TalkBackWordReached(long Time, string Word);

/// <summary>
/// An utterance TalkBack spoke: its id, when its first fragment started,
/// its text (every fragment in order), whether and when it was cut off, and
/// the words the speech engine reached in it.
/// </summary>
/// <param name="Event">The utterance's <c>utterance-started</c> record on the timeline, so the strip can select it.</param>
public sealed record TalkBackUtterance(
    string UtteranceId,
    long Start,
    string Text,
    long? InterruptedAt,
    IReadOnlyList<TalkBackWordReached> Words,
    SessionTimelineEvent? Event)
{
    public bool Interrupted => InterruptedAt is not null;
}

/// <summary>
/// A span of the "TalkBack said" strip: an utterance from its start to the
/// next utterance's start, or to its interruption where that came first.
/// The log does not say when speech ended, so the span is how long the
/// utterance was the latest TalkBack started, not how long it was heard.
/// </summary>
public sealed record TalkBackSpeechSpan(long Start, long End, string Text, bool Interrupted, SessionTimelineEvent? Event);

/// <summary>
/// What TalkBack said in a recording, from its <c>screen-reader.talkback.speech</c>
/// records (Android slice 6, step 6b): each utterance with its text, its
/// words and whether it was cut off, the "TalkBack said" strip's spans, and
/// the properties panel's TalkBack rows. An interruption or a word of an
/// utterance that started before the recording, or that names none, is
/// left out: it belongs to no utterance shown.
/// </summary>
public sealed class TalkBackSpeechTimeline
{
    /// <summary>The row of the utterance at the playhead.</summary>
    public const string SaidKey = "talkback.said";

    /// <summary>The row of the words reached in that utterance.</summary>
    public const string WordsKey = "talkback.words";

    public const string SaidSetting = "TalkBack said";

    public const string WordsSetting = "Words reached";

    private const long ChangedWindowNanoseconds = 1_000_000_000;

    private readonly List<TalkBackUtterance> _utterances;
    private readonly List<long> _starts;

    public static TalkBackSpeechTimeline Empty { get; } = new([], 0);

    /// <param name="records">The speech records, each with its timeline event and payload, in any order.</param>
    /// <param name="durationNanoseconds">The recording's duration, where the last span ends.</param>
    public TalkBackSpeechTimeline(
        IEnumerable<(SessionTimelineEvent Event, JsonElement Payload)> records,
        long durationNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ordered = records
            .Where(record => record.Payload.ValueKind == JsonValueKind.Object)
            .OrderBy(record => record.Event.MonotonicNanoseconds)
            .ThenBy(record => record.Event.EventKey)
            .ToList();

        var building = new Dictionary<string, Building>(StringComparer.Ordinal);
        var order = new List<Building>();
        foreach (var (item, payload) in ordered)
        {
            var id = ReadString(payload, "utteranceId");
            switch (item.EventType)
            {
                case AndroidTalkBackRecords.UtteranceStartedEventType when id is not null:
                    if (!building.ContainsKey(id))
                    {
                        var started = new Building(id, item.MonotonicNanoseconds, item);
                        started.Fragments.Add(ReadString(payload, "text") ?? string.Empty);
                        building.Add(id, started);
                        order.Add(started);
                    }

                    break;
                case AndroidTalkBackRecords.FragmentStartedEventType when id is not null && building.TryGetValue(id, out var later):
                    later.Fragments.Add(ReadString(payload, "text") ?? string.Empty);
                    break;
                case AndroidTalkBackRecords.WordReachedEventType when id is not null && building.TryGetValue(id, out var speaking):
                    if (ReadString(payload, "word") is { Length: > 0 } word)
                    {
                        speaking.Words.Add(new TalkBackWordReached(item.MonotonicNanoseconds, word));
                    }

                    break;
                case AndroidTalkBackRecords.UtteranceInterruptedEventType when id is not null && building.TryGetValue(id, out var cut):
                    cut.InterruptedAt ??= item.MonotonicNanoseconds;
                    break;
            }
        }

        _utterances = [.. order.Select(item => new TalkBackUtterance(
            item.Id,
            item.Start,
            string.Join(" ", item.Fragments.Select(text => text.Trim()).Where(text => text.Length > 0)),
            item.InterruptedAt,
            item.Words,
            item.Event))];
        _starts = [.. _utterances.Select(item => item.Start)];
        Recorded = ordered.Count > 0;
        Spans = BuildSpans(Math.Max(0, durationNanoseconds));
    }

    /// <summary>Whether the recording holds speech records.</summary>
    public bool Recorded { get; }

    public IReadOnlyList<TalkBackUtterance> Utterances => _utterances;

    /// <summary>The "TalkBack said" strip's spans, one for each utterance.</summary>
    public IReadOnlyList<TalkBackSpeechSpan> Spans { get; }

    /// <summary>Whether playback keeps a record: TalkBack's speech records, read for the strip and the rows.</summary>
    public static bool IsSpeechRecord(string channel, string eventType) =>
        channel == AndroidTalkBackRecords.SpeechChannel &&
        eventType is AndroidTalkBackRecords.UtteranceStartedEventType or
            AndroidTalkBackRecords.FragmentStartedEventType or
            AndroidTalkBackRecords.WordReachedEventType or
            AndroidTalkBackRecords.UtteranceInterruptedEventType;

    /// <summary>The utterance last started at or before a time, or null.</summary>
    public TalkBackUtterance? At(long time)
    {
        var index = _starts.BinarySearch(time);
        if (index < 0)
        {
            index = ~index - 1;
        }
        else
        {
            while (index + 1 < _starts.Count && _starts[index + 1] == time)
            {
                index++;
            }
        }

        return index < 0 ? null : _utterances[index];
    }

    /// <summary>
    /// The TalkBack rows at a time, in the Screen reader group: the
    /// utterance last started, and the words reached in it; none for a
    /// recording without speech records.
    /// </summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time)
    {
        if (!Recorded)
        {
            return [];
        }

        if (At(time) is not { } utterance)
        {
            return
            [
                new PropertyRow(AssistiveTechnologyTimeline.Group, SaidSetting, "nothing yet", "at start", null, false, SaidKey),
                new PropertyRow(AssistiveTechnologyTimeline.Group, WordsSetting, "none", "at start", null, false, WordsKey)
            ];
        }

        var when = WindowsPreferenceTimeline.Clock(utterance.Start);
        var changed = time - utterance.Start < ChangedWindowNanoseconds;
        return
        [
            new PropertyRow(AssistiveTechnologyTimeline.Group, SaidSetting, Said(utterance), when, utterance.Start, changed, SaidKey),
            new PropertyRow(AssistiveTechnologyTimeline.Group, WordsSetting, Words(utterance), when, utterance.Start, changed, WordsKey)
        ];
    }

    /// <summary>The times the utterance changed, for the TalkBack said row's buttons; null for another row.</summary>
    public IReadOnlyList<long>? ChangeTimesOf(string key) =>
        Recorded && key == SaidKey ? _starts.Distinct().ToArray() : null;

    /// <summary>The TalkBack said row's value: the text in quotation marks, and when it was cut off.</summary>
    public static string Said(TalkBackUtterance utterance)
    {
        ArgumentNullException.ThrowIfNull(utterance);
        var said = $"\u201C{utterance.Text}\u201D";
        return utterance.InterruptedAt is { } at
            ? $"{said}, interrupted at {WindowsPreferenceTimeline.Clock(at)}"
            : said;
    }

    /// <summary>The Words reached row's value: the words in order, and how many.</summary>
    public static string Words(TalkBackUtterance utterance)
    {
        ArgumentNullException.ThrowIfNull(utterance);
        var count = utterance.Words.Count;
        return count == 0
            ? "none"
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{string.Join(" ", utterance.Words.Select(word => word.Word))} ({count} {(count == 1 ? "word" : "words")})");
    }

    private List<TalkBackSpeechSpan> BuildSpans(long duration)
    {
        var spans = new List<TalkBackSpeechSpan>(_utterances.Count);
        for (var index = 0; index < _utterances.Count; index++)
        {
            var utterance = _utterances[index];
            var end = index + 1 < _utterances.Count ? _utterances[index + 1].Start : Math.Max(duration, utterance.Start);
            if (utterance.InterruptedAt is { } cut && cut < end)
            {
                end = Math.Max(utterance.Start, cut);
            }

            spans.Add(new TalkBackSpeechSpan(utterance.Start, end, utterance.Text, utterance.Interrupted, utterance.Event));
        }

        return spans;
    }

    private static string? ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed class Building(string id, long start, SessionTimelineEvent item)
    {
        public string Id { get; } = id;

        public long Start { get; } = start;

        public SessionTimelineEvent Event { get; } = item;

        public List<string> Fragments { get; } = [];

        public List<TalkBackWordReached> Words { get; } = [];

        public long? InterruptedAt { get; set; }
    }
}
