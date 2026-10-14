using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Database.RecordingFiles;
using Recorder.Session;

namespace Recorder.Analysis.Summary;

/// <summary>
/// The summary's evidence from a recording file, read as playback reads it:
/// the playback index's records, the archive's key outcomes, NVDA's
/// inferred announcements, the assistive technology records, and each
/// document's recorded state at a time.
/// </summary>
public sealed class RecordedSummaryEvidence : ISummaryEvidence
{
    private readonly SessionPlaybackArchive _archive;
    private readonly RecordingFileDocuments _documents;
    private readonly Dictionary<(string Token, long Time), BrowserDocumentState?> _states = [];

    public RecordedSummaryEvidence(SessionPlaybackArchive archive, RecordingFileDocuments documents)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        var index = documents.Index;
        var commits = new List<NavigationCommit>();
        var keys = new List<TimedFact>();
        var clicks = new List<TimedFact>();
        var focus = new List<FocusFact>();
        var foreground = new List<ForegroundFact>();
        var popups = new List<TimedFact>();
        foreach (var item in index.Events)
        {
            var payload = item.Payload;
            if (item.EventType == BrowserEvidenceEventTypes.NavigationCompleted && item.Channel == BrowserEvidenceChannels.Navigation)
            {
                if (Commit(item) is { } commit)
                {
                    commits.Add(commit);
                }
            }
            else if (item.Channel == KeyboardHookRecords.Channel && item.EventType == KeyboardHookRecords.KeyEventType &&
                archive.KeyDispositions.Of(item.EventId) is { Key: { } key } outcome && key.EndsWith(" down", StringComparison.Ordinal))
            {
                keys.Add(new TimedFact(item.MonotonicNanoseconds, item.EventId, $"{key[..^" down".Length]}, {outcome.Text}"));
            }
            else if (item.Channel == MouseHookRecords.Channel && item.EventType == MouseHookRecords.EventType &&
                Text(payload, "kind") == MouseHookRecords.ButtonDownKind)
            {
                var button = Text(payload, "button") ?? "a";
                var at = string.Create(CultureInfo.InvariantCulture, $"({Number(payload, "x")}, {Number(payload, "y")})");
                var mouse = archive.Mouse.Of(item.EventId)?.Text;
                clicks.Add(new TimedFact(item.MonotonicNanoseconds, item.EventId,
                    $"Mouse {button} button pressed at screen point {at}" + (mouse is null ? "" : $", {mouse}")));
            }
            else if (ReadingPositionTimeline.IsFocusEvidence(item.Channel, item.EventType) && Text(payload, "outcome") == "focused" &&
                Context(payload, "documentToken") is { } token)
            {
                focus.Add(new FocusFact(item.MonotonicNanoseconds, item.EventId, token, Number(payload, "focusedNodeId")));
            }
            else if (item.Channel == "window.foreground" && item.EventType == "foreground-window")
            {
                foreground.Add(new ForegroundFact(item.MonotonicNanoseconds, item.EventId, Text(payload, "processName"), Text(payload, "title")));
            }
            else if (item.EventType == BrowserEvidenceEventTypes.PagePopupOpened && payload.ValueKind == JsonValueKind.Object)
            {
                popups.Add(new TimedFact(item.MonotonicNanoseconds, item.EventId, $"A page popup opened, of kind {Text(payload, "kind") ?? "not recorded"}."));
            }
        }

        Commits = commits;
        Keys = keys;
        Clicks = clicks;
        Focus = focus;
        Foreground = foreground;
        Popups = popups;
        Commands = [.. archive.KeyDispositions.KeptCommands.Select(command => new TimedFact(
            command.Time,
            null,
            $"NVDA kept {command.Gesture} as its command: {string.Join(", ", command.Names)}."))];
        Announcements = [.. archive.NvdaAnnouncements.Announcements.Select(item => new TimedFact(item.Time, item.Step.EventId, item.Line))];
    }

    public long Duration => _archive.DurationNanoseconds;

    public IReadOnlyList<NavigationCommit> Commits { get; }

    public IReadOnlyList<TimedFact> Keys { get; }

    public IReadOnlyList<TimedFact> Commands { get; }

    public IReadOnlyList<TimedFact> Clicks { get; }

    public IReadOnlyList<FocusFact> Focus { get; }

    public IReadOnlyList<TimedFact> Announcements { get; }

    public IReadOnlyList<ForegroundFact> Foreground { get; }

    public IReadOnlyList<TimedFact> Popups { get; }

    public string? BrowserProcess => _archive.AssistiveTechnology.BrowserProcessName ?? "chrome";

    public IReadOnlyList<string> AssistiveTechnologyAt(long time)
    {
        var running = new List<string>();
        foreach (var product in _archive.AssistiveTechnology.ScreenReadersRunningAt(time))
        {
            var version = _archive.AssistiveTechnology.ScreenReaderAt(product, time)?.Version;
            running.Add(version is null ? product : $"{product} {version}");
        }

        var frame = _archive.Frames.LastOrDefault(item => item.MonotonicNanoseconds <= time) ?? _archive.Frames.FirstOrDefault();
        if (frame?.Magnification is { Level: > 1 } magnification)
        {
            running.Add(string.Create(CultureInfo.InvariantCulture, $"full screen magnification at {magnification.Level * 100:0}%"));
        }

        return running;
    }

    public PageSnapshot? PageAt(string documentToken, string url, long time) =>
        State(documentToken, time) is { } state ? PageSnapshot.From(state, url) : null;

    public InteractionCurrent? InteractionAt(string documentToken, long time) =>
        State(documentToken, time)?.Interaction.Current();

    private BrowserDocumentState? State(string documentToken, long time)
    {
        if (_states.TryGetValue((documentToken, time), out var cached))
        {
            return cached;
        }

        var named = _documents.State.At(time, new HashSet<string>(StringComparer.Ordinal));
        var key = named.Documents.Select(document => document.Key)
            .FirstOrDefault(candidate => candidate.Split(' ', 2)[0] == documentToken);
        BrowserDocumentState? state = null;
        if (key is not null)
        {
            state = _documents.State.At(time, new HashSet<string>(StringComparer.Ordinal) { key })
                .Documents.FirstOrDefault(document => document.Key == key)?.State;
        }

        _states[(documentToken, time)] = state;
        return state;
    }

    // A committed navigation of a tab's page, from its complete record.
    private NavigationCommit? Commit(PlaybackIndexEvent item)
    {
        JsonElement payload;
        try
        {
            using var document = JsonDocument.Parse(_archive.ReadEventJson(new SessionTimelineEvent(
                item.EventKey, item.EventId, item.EvidenceClass, item.Channel, item.EventType, item.MonotonicNanoseconds, "")));
            payload = (document.RootElement.TryGetProperty("payload", out var inner) ? inner : document.RootElement).Clone();
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or InvalidOperationException)
        {
            payload = item.Payload;
        }

        if (payload.ValueKind != JsonValueKind.Object ||
            !Flag(payload, "committed") || !Flag(payload, "primaryPage") ||
            Text(payload, "frameType") != "primary-main-frame" ||
            Text(payload, "url") is not { } url ||
            !ReadingPositionTimeline.IsPageUrl(url))
        {
            return null;
        }

        var browser = Context(payload, "browserInstanceId") ?? "";
        var page = Context(payload, "pageId") ?? "";
        return new NavigationCommit(
            item.MonotonicNanoseconds,
            item.EventId,
            browser + "/" + page,
            url,
            Flag(payload, "sameDocument"),
            Context(payload, "documentToken"),
            payload.TryGetProperty("rendererInitiated", out var initiated) && initiated.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? initiated.GetBoolean()
                : null,
            Flag(payload, "errorPage"));
    }

    private static bool Flag(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Number(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : null;

    private static string? Context(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.Object
            ? Text(context, name)
            : null;
}
