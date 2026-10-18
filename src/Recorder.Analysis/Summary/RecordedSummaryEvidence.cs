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
public sealed class RecordedSummaryEvidence : ISummaryEvidence, IMomentEvidence
{
    private readonly SessionPlaybackArchive _archive;
    private readonly RecordingFileDocuments _documents;
    private readonly Dictionary<(string Token, long Time), BrowserDocumentState?> _states = [];

    private readonly string? _recordingFile;
    private (List<LayoutChangeFact> Layout, List<ScrollFact> Scrolls, List<AnimationFact> Animations)? _moments;

    /// <param name="recordingFile">
    /// The recording file, for the vision stage's layout changes, scrolls,
    /// and animations, which the playback index does not keep; they are read
    /// from it in one pass when first asked for.
    /// </param>
    public RecordedSummaryEvidence(SessionPlaybackArchive archive, RecordingFileDocuments documents, string? recordingFile = null)
    {
        _recordingFile = recordingFile;
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        var index = documents.Index;
        var commits = new List<NavigationCommit>();
        var keys = new List<TimedFact>();
        var clicks = new List<TimedFact>();
        var focus = new List<FocusFact>();
        var foreground = new List<ForegroundFact>();
        var popups = new List<TimedFact>();
        var closed = new List<TimedFact>();
        foreach (var item in index.Events)
        {
            var payload = item.Payload;
            if (item.EventType == BrowserEvidenceEventTypes.PagePopupClosed)
            {
                closed.Add(new TimedFact(item.MonotonicNanoseconds, item.EventId, "A page popup closed."));
                continue;
            }

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
        PopupsClosed = closed;
        Frames = [.. archive.Frames.Select(frame => new CapturedFrame(
            frame.MonotonicNanoseconds, frame.Path, frame.AbsolutePath, frame.Width, frame.Height, frame.Magnification?.Level))];
        NvdaWindows = [.. archive.ScreenReaderWindows.Spans.Select(span => new NvdaWindowFact(
            span.Name, span.Start, span.Events.FirstOrDefault()?.EventId, span.End, span.Events.Count > 1 ? span.Events[^1].EventId : null))];
        Commands = [.. archive.KeyDispositions.KeptCommands.Select(command => new TimedFact(
            command.Time,
            null,
            $"NVDA kept {command.Gesture} as its command: {string.Join(", ", command.Names)}."))];
        Announcements = [.. archive.NvdaAnnouncements.Announcements.Select(item => new TimedFact(item.Time, item.Step.EventId, item.Line))];
    }

    public long Duration => _archive.DurationNanoseconds;

    /// <summary>The settings, read from the same records the properties panel shows.</summary>
    public RecordedSettings Settings(long? firstPageTime) => SettingsReader.Read(_archive, _documents.Index.Events, firstPageTime);

    public IReadOnlyList<NavigationCommit> Commits { get; }

    public IReadOnlyList<TimedFact> Keys { get; }

    public IReadOnlyList<TimedFact> Commands { get; }

    public IReadOnlyList<TimedFact> Clicks { get; }

    public IReadOnlyList<FocusFact> Focus { get; }

    public IReadOnlyList<TimedFact> Announcements { get; }

    public IReadOnlyList<ForegroundFact> Foreground { get; }

    public IReadOnlyList<TimedFact> Popups { get; }

    public IReadOnlyList<CapturedFrame> Frames { get; }

    public IReadOnlyList<LayoutChangeFact> LayoutChanges => MomentRecords().Layout;

    public IReadOnlyList<ScrollFact> Scrolls => MomentRecords().Scrolls;

    public IReadOnlyList<AnimationFact> Animations => MomentRecords().Animations;

    private (List<LayoutChangeFact> Layout, List<ScrollFact> Scrolls, List<AnimationFact> Animations) MomentRecords()
    {
        if (_moments is { } read)
        {
            return read;
        }

        var layout = new List<LayoutChangeFact>();
        var scrolls = new List<ScrollFact>();
        var started = new List<(long Time, string EventId, long? Id)>();
        var ended = new List<(long Time, string EventId, long? Id)>();
        if (_recordingFile is not null)
        {
            RecordingFilePlayback.ReadBrowserEvents(
                _recordingFile,
                new HashSet<string>(StringComparer.Ordinal) { BrowserEvidenceChannels.Layout, BrowserEvidenceChannels.Compositor },
                item =>
                {
                    var payload = item.Payload;
                    if (payload.ValueKind != JsonValueKind.Object)
                    {
                        return;
                    }

                    var time = item.MonotonicNanoseconds;
                    switch (item.EventType)
                    {
                        case BrowserEvidenceEventTypes.LayoutChangesCompleted when Context(payload, "documentToken") is { } token:
                            layout.Add(new LayoutChangeFact(time, item.EventId, token, Number(payload, "recordedNodeCount")));
                            break;
                        case BrowserEvidenceEventTypes.LayoutScrollOffsetChanged when Context(payload, "documentToken") is { } token &&
                            payload.TryGetProperty("scrollOffset", out var offset) && offset.ValueKind == JsonValueKind.Object &&
                            offset.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number:
                            scrolls.Add(new ScrollFact(time, item.EventId, token, Text(payload, "scrollElementId") ?? "a scroller not named", y.GetDouble(), Number(payload, "nodeId")));
                            break;
                        case BrowserEvidenceEventTypes.CompositorAnimationStarted:
                            started.Add((time, item.EventId, Number(payload, "compositorAnimationId")));
                            break;
                        case BrowserEvidenceEventTypes.CompositorAnimationEnded:
                            ended.Add((time, item.EventId, Number(payload, "compositorAnimationId")));
                            break;
                    }
                },
                CancellationToken.None);
        }

        // An animation's end is the first end of the same compositor
        // animation after its start; one with no ID is given no end.
        var animations = new List<AnimationFact>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in started)
        {
            var end = start.Id is { } id
                ? ended.FirstOrDefault(item => item.Id == id && item.Time >= start.Time && !used.Contains(item.EventId))
                : default;
            if (end.EventId is not null)
            {
                used.Add(end.EventId);
            }

            animations.Add(new AnimationFact(start.Time, start.EventId, end.EventId is null ? null : end.Time, end.EventId));
        }

        _moments = (layout, scrolls, animations);
        return _moments.Value;
    }

    private List<LiveRegionFact>? _liveRegions;

    // One pass over the DOM records, rebuilding each document's tree, read
    // the first time it is asked for.
    public IReadOnlyList<LiveRegionFact> LiveRegions
    {
        get
        {
            if (_liveRegions is { } read)
            {
                return read;
            }

            var changes = new LiveRegionChanges();
            if (_recordingFile is not null)
            {
                RecordingFilePlayback.ReadBrowserEvents(
                    _recordingFile,
                    new HashSet<string>(StringComparer.Ordinal) { BrowserEvidenceChannels.Dom },
                    item => changes.Add(item.MonotonicNanoseconds, item.EventId, item.EventType, item.Payload),
                    CancellationToken.None);
            }

            _liveRegions = [.. changes.Facts];
            return _liveRegions;
        }
    }

    public IReadOnlyList<TimedFact> PopupsClosed { get; }

    public IReadOnlyList<NvdaWindowFact> NvdaWindows { get; }

    // The viewport is read at most once a second of the recording, since a
    // scroll may be recorded on every frame.
    private readonly Dictionary<(string, long), double?> _viewports = [];

    public double? ViewportHeightAt(string documentToken, long time)
    {
        var second = time / 1_000_000_000;
        if (!_viewports.TryGetValue((documentToken, second), out var height))
        {
            height = State(documentToken, time)?.Viewport?.Height;
            _viewports[(documentToken, second)] = height;
        }

        return height;
    }

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
