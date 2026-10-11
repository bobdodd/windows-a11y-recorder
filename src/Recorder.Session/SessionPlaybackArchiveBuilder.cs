using Recorder.Contracts;
using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// Builds a playback archive from the events a store reads, in the order it
/// stored them. The store provides the timeline and complete records; the
/// builder keeps what playback needs when a recording opens: its duration,
/// frames, audio tracks, and browser navigation.
/// </summary>
public sealed class SessionPlaybackArchiveBuilder
{
    /// <summary>
    /// The channels whose payloads playback reads, for summaries, frames,
    /// audio tracks, and browser navigation. The payloads of other channels
    /// are not needed to build the archive.
    /// </summary>
    public static IReadOnlyList<string> PayloadChannels { get; } =
    [
        "window.foreground",
        "system.preferences",
        "accessibility.uia.events",
        "session.annotations",
        "graphics.desktop.frames",
        "graphics.magnifier",
        "system.assistive-technology",
        "input.keyboard-hook"
    ];

    /// <summary>
    /// Channel prefixes whose payloads playback reads, as
    /// <see cref="PayloadChannels"/>.
    /// </summary>
    public static IReadOnlyList<string> PayloadChannelPrefixes { get; } =
    [
        "audio.",
        "browser."
    ];

    /// <summary>
    /// The top-level payload properties playback reads. A reader that stores
    /// payloads elsewhere can pass only these to <see cref="AddEvent"/>.
    /// </summary>
    public static IReadOnlyList<string> PayloadProperties { get; } =
    [
        "automationId",
        "checkpointId",
        "committed",
        "context",
        "controlType",
        // A Windows setting's change, and every setting at the start and
        // stop, for the properties panel.
        "current",
        "device",
        "eventName",
        // A browser preference record, for the properties panel.
        "fields",
        "frameType",
        // A desktop frame's Magnifier readings and its corner, for the
        // participant's view.
        "fullscreenColorEffect",
        "fullscreenMagnification",
        "height",
        "mode",
        "name",
        "navigationId",
        "navigationKind",
        "note",
        "outcome",
        "pageFrameTreeNodeId",
        "path",
        "preference",
        "preferences",
        "primaryPage",
        "processName",
        "reason",
        "registrationKind",
        "rendererProcessId",
        "sameDocument",
        "setting",
        "settings",
        "stream",
        "title",
        "truncated",
        "url",
        "width",
        "x",
        "y",
        "zoomPercent"
    ];

    /// <summary>
    /// Whether playback reads the payloads of a channel's events.
    /// </summary>
    public static bool ReadsPayload(string channel) =>
        PayloadChannels.Contains(channel, StringComparer.Ordinal) ||
        PayloadChannelPrefixes.Any(prefix => channel.StartsWith(prefix, StringComparison.Ordinal));

    private readonly string _root;
    private readonly List<SessionTimelineEvent> _events = [];
    private readonly List<SessionVideoFrame> _frames = [];
    private readonly Dictionary<string, SessionAudioTrack> _audioTracks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<BrowserEventProjection> _browserProjections = [];
    private readonly List<WindowsPreferenceRecord> _preferences = [];
    private readonly List<(long Time, JsonElement Payload)> _magnifierChanges = [];
    private readonly List<BrowserPreferenceRecord> _browserPreferences = [];
    private readonly List<AssistiveTechnologyRecord> _assistiveTechnology = [];
    private readonly List<InputRecordabilityRecord> _inputRecordability = [];
    private readonly List<KeyEvidenceRecord> _keyEvidence = [];
    private readonly List<BrowserPageCommit> _pageCommits = [];
    private readonly bool _retainEvents;
    private long _maximumTimestamp;
    private bool _built;

    /// <param name="retainEvents">
    /// Whether the archive holds every added event in
    /// <see cref="SessionPlaybackArchive.Events"/>. A reader whose store
    /// provides its own <see cref="ISessionTimeline"/> passes false, and adds
    /// only the events that frames, audio tracks, and browser navigation are
    /// built from.
    /// </param>
    public SessionPlaybackArchiveBuilder(string sessionDirectory, bool retainEvents = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        _root = Path.GetFullPath(sessionDirectory);
        _retainEvents = retainEvents;
    }

    /// <summary>
    /// Whether the archive's frames, audio tracks, or browser navigation are
    /// built from a channel's events: the events a reader must add when the
    /// builder does not retain events.
    /// </summary>
    public static bool BuildsFrom(string channel) =>
        channel == "graphics.desktop.frames" ||
        channel == WindowsPreferenceSettings.Channel ||
        channel == MagnifierChanges.Channel ||
        channel == AssistiveTechnologyRecords.Channel ||
        channel == InputRecordabilityRecords.Channel ||
        channel.StartsWith("audio.", StringComparison.Ordinal) ||
        channel.StartsWith("browser.", StringComparison.Ordinal);

    /// <summary>
    /// Includes the time of an event that was not added, so the archive's
    /// duration covers it.
    /// </summary>
    public void IncludeTimestamp(long monotonicNanoseconds)
    {
        ThrowIfBuilt();
        _maximumTimestamp = Math.Max(_maximumTimestamp, monotonicNanoseconds);
    }

    public string SessionDirectory => _root;

    /// <summary>
    /// Adds an event read from the store. Payload
    /// need hold only <see cref="PayloadProperties"/>, and only for channels
    /// where <see cref="ReadsPayload"/> is true; otherwise pass default.
    /// </summary>
    public void AddEvent(
        long eventKey,
        string eventId,
        string evidenceClass,
        string channel,
        string eventType,
        long monotonicNanoseconds,
        JsonElement payload)
    {
        ThrowIfBuilt();
        AddCore(
            new SessionTimelineEvent(
                eventKey,
                eventId,
                evidenceClass,
                channel,
                eventType,
                monotonicNanoseconds,
                CreateSummary(channel, eventType, payload)),
            payload);
    }

    /// <summary>
    /// Adds what a playback index holds: its events, its counts of the other
    /// browser events, and its latest time. Its occupancy, presented
    /// checkpoints, and frame compositions are applied by the reader.
    /// </summary>
    public void AddIndex(PlaybackIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        ThrowIfBuilt();
        IncludeTimestamp(index.LatestTime);
        // In the order the events were stored, as a database reader adds them.
        foreach (var item in index.Events.OrderBy(item => item.EventKey))
        {
            AddEvent(
                item.EventKey,
                item.EventId,
                item.EvidenceClass,
                item.Channel,
                item.EventType,
                item.MonotonicNanoseconds,
                item.Payload);
        }

        // Each count stands for its events at the start of their segment:
        // navigation correlation compares their times only with navigation
        // starts.
        foreach (var count in index.BrowserCounts)
        {
            _browserProjections.Add(new BrowserEventProjection(
                new SessionTimelineEvent(
                    -1,
                    string.Empty,
                    EvidenceClasses.Observed,
                    "browser",
                    count.EventType,
                    count.SegmentStart,
                    count.EventType),
                count.BrowserInstanceId,
                count.ProcessId,
                null,
                null,
                count.DocumentId,
                count.DocumentToken,
                null,
                null,
                null,
                null,
                false,
                false,
                null,
                null,
                count.Truncated)
            {
                Weight = count.Count
            });
        }
    }

    private void AddCore(SessionTimelineEvent timelineEvent, JsonElement payload)
    {
        var timestamp = timelineEvent.MonotonicNanoseconds;
        _maximumTimestamp = Math.Max(_maximumTimestamp, timestamp);
        if (_retainEvents)
        {
            _events.Add(timelineEvent);
        }

        var browserProjection = BrowserNavigationCorrelator.Project(
            timelineEvent,
            payload);
        if (browserProjection is not null)
        {
            _browserProjections.Add(browserProjection);
        }

        if (timelineEvent.Channel == "graphics.desktop.frames" &&
            timelineEvent.EventType == "desktop-frame" &&
            payload.ValueKind == JsonValueKind.Object)
        {
            AddFrame(_root, timestamp, payload, _frames);
        }

        if (timelineEvent.Channel == WindowsPreferenceSettings.Channel &&
            payload.ValueKind == JsonValueKind.Object)
        {
            _preferences.Add(new WindowsPreferenceRecord(timestamp, timelineEvent.EventType, payload.Clone()));
        }

        if (timelineEvent.Channel == MagnifierChanges.Channel &&
            timelineEvent.EventType == MagnifierChanges.ChangeEventType &&
            payload.ValueKind == JsonValueKind.Object)
        {
            _magnifierChanges.Add((timestamp, payload.Clone()));
        }

        if (timelineEvent.Channel == AssistiveTechnologyRecords.Channel &&
            payload.ValueKind == JsonValueKind.Object)
        {
            _assistiveTechnology.Add(new AssistiveTechnologyRecord(timestamp, timelineEvent.EventType, payload.Clone()));
        }

        if (timelineEvent.Channel == InputRecordabilityRecords.Channel &&
            InputRecordabilityRecords.EventTypes.Contains(timelineEvent.EventType) &&
            payload.ValueKind == JsonValueKind.Object)
        {
            _inputRecordability.Add(new InputRecordabilityRecord(timestamp, timelineEvent.EventType, payload.Clone()));
        }

        if (payload.ValueKind == JsonValueKind.Object &&
            KeyDispositions.IsKeyEvidence(timelineEvent.Channel, timelineEvent.EventType, payload))
        {
            _keyEvidence.Add(new KeyEvidenceRecord(
                timelineEvent.EventId, timestamp, timelineEvent.Channel, timelineEvent.EventType, payload.Clone()));
        }

        if (timelineEvent.Channel == BrowserPreferenceSettings.Channel &&
            payload.ValueKind == JsonValueKind.Object)
        {
            _browserPreferences.Add(new BrowserPreferenceRecord(timestamp, timelineEvent.EventType, payload.Clone()));
        }

        if (timelineEvent.Channel == BrowserEvidenceChannels.Navigation &&
            timelineEvent.EventType == BrowserEvidenceEventTypes.NavigationCompleted &&
            PageCommitOf(timestamp, payload) is { } commit)
        {
            _pageCommits.Add(commit);
        }

        if (timelineEvent.Channel.StartsWith("audio.", StringComparison.Ordinal) &&
            timelineEvent.EventType == "audio-stream-started" &&
            payload.ValueKind == JsonValueKind.Object)
        {
            AddAudioTrack(
                _root,
                timestamp,
                payload,
                _audioTracks);
        }
    }

    /// <summary>
    /// Builds an archive whose events were added with
    /// <see cref="AddEvent"/>, reading complete records from
    /// <paramref name="recordSource"/>. The session folder must hold
    /// manifest.json; frames and audio are read from it.
    /// </summary>
    public async Task<SessionPlaybackArchive> BuildAsync(
        ISessionEventRecordSource recordSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordSource);
        var manifestPath = Path.Combine(_root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException(
                "The recording's folder does not contain manifest.json.");
        }

        var manifest = await ReadManifestAsync(
            manifestPath,
            cancellationToken).ConfigureAwait(false);
        return Build(manifest) with { RecordSource = recordSource };
    }

    internal SessionPlaybackArchive Build(SessionManifest manifest)
    {
        ThrowIfBuilt();
        _built = true;
        _events.Sort(InMemorySessionTimeline.Compare);
        _frames.Sort(static (left, right) =>
            left.MonotonicNanoseconds.CompareTo(right.MonotonicNanoseconds));
        var duration = Math.Max(manifest.DurationNanoseconds ?? 0, _maximumTimestamp);
        var browserNavigations = BrowserNavigationCorrelator.Build(
            _browserProjections,
            duration);
        var assistiveTechnology = _assistiveTechnology.Count == 0
            ? AssistiveTechnologyTimeline.Empty
            : new AssistiveTechnologyTimeline(_assistiveTechnology);
        var inputRecordability = _inputRecordability.Count == 0
            ? InputRecordabilityTimeline.Empty
            : new InputRecordabilityTimeline(_inputRecordability);
        return new SessionPlaybackArchive(
            _root,
            manifest,
            duration,
            _events,
            _frames,
            _audioTracks.Values
                .OrderBy(track => track.Stream, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            browserNavigations)
        {
            WindowsPreferences = _preferences.Count == 0
                ? WindowsPreferenceTimeline.Empty
                : new WindowsPreferenceTimeline(_preferences),
            MagnifierChanges = _magnifierChanges.Count == 0
                ? MagnifierChangeTimeline.Empty
                : new MagnifierChangeTimeline(_magnifierChanges),
            BrowserPreferences = _browserPreferences.Count == 0
                ? BrowserPreferenceTimeline.Empty
                : new BrowserPreferenceTimeline(_browserPreferences, _pageCommits),
            AssistiveTechnology = assistiveTechnology,
            InputRecordability = inputRecordability,
            KeyDispositions = _keyEvidence.Count == 0
                ? KeyDispositions.Empty
                : new KeyDispositions(_keyEvidence, assistiveTechnology, inputRecordability.Unrecordable)
        };
    }

    // A committed cross-document navigation of a primary main frame, whose
    // context names its page "frame-" and the page's frame tree node id.
    internal static BrowserPageCommit? PageCommitOf(long timestamp, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("committed", out var committed) || committed.ValueKind != JsonValueKind.True ||
            !payload.TryGetProperty("primaryPage", out var primary) || primary.ValueKind != JsonValueKind.True ||
            !payload.TryGetProperty("frameType", out var frameType) || frameType.GetString() != "primary-main-frame" ||
            !payload.TryGetProperty("sameDocument", out var sameDocument) || sameDocument.ValueKind != JsonValueKind.False ||
            !payload.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("context", out var context) || context.ValueKind != JsonValueKind.Object ||
            !context.TryGetProperty("pageId", out var pageId) || pageId.ValueKind != JsonValueKind.String ||
            pageId.GetString() is not { } page || !page.StartsWith("frame-", StringComparison.Ordinal) ||
            !int.TryParse(page.AsSpan("frame-".Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var pageFrameTreeNodeId))
        {
            return null;
        }

        return new BrowserPageCommit(timestamp, pageFrameTreeNodeId, url.GetString()!);
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "The playback archive has already been built.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static async Task<SessionManifest> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using var manifestStream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<SessionManifest>(
            manifestStream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false) ??
            throw new InvalidDataException("The session manifest is empty.");
    }

    private static void AddFrame(
        string root,
        long timestamp,
        JsonElement payload,
        ICollection<SessionVideoFrame> frames)
    {
        var path = ReadString(payload, "path");
        if (!TryResolvePath(root, path, out var absolutePath) ||
            !File.Exists(absolutePath))
        {
            return;
        }

        frames.Add(new SessionVideoFrame(
            timestamp,
            path!,
            absolutePath,
            ReadInt32(payload, "width") ?? 0,
            ReadInt32(payload, "height") ?? 0,
            ReadInt32(payload, "x") ?? 0,
            ReadInt32(payload, "y") ?? 0,
            ReadMagnification(payload),
            ReadColorEffect(payload)));
    }

    // A reading that failed holds no matrix, so the frame plays as captured.
    private static FullscreenColorEffect? ReadColorEffect(JsonElement payload)
    {
        if (!payload.TryGetProperty("fullscreenColorEffect", out var value) ||
            value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("matrix", out var matrix) ||
            matrix.ValueKind != JsonValueKind.Array ||
            matrix.GetArrayLength() != 25)
        {
            return null;
        }

        var values = new double[25];
        var index = 0;
        foreach (var item in matrix.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number ||
                !item.TryGetDouble(out var number) ||
                !double.IsFinite(number))
            {
                return null;
            }

            values[index++] = number;
        }

        return new FullscreenColorEffect(values);
    }

    // A browser preference change with its new value, a zoom change with its
    // mode and percentage, and a send with the point it was sent at and the
    // fields it holds.
    private static string BrowserPreferenceSummary(string eventType, JsonElement payload)
    {
        switch (eventType)
        {
            case BrowserPreferenceSettings.ChangeEventType:
                var preference = ReadString(payload, "preference");
                if (preference is not null &&
                    BrowserPreferenceSettings.FindBrowser(preference) is { } known &&
                    payload.TryGetProperty("current", out var current) &&
                    current.ValueKind == JsonValueKind.Object &&
                    current.TryGetProperty(preference, out var reading))
                {
                    return JoinSummary(eventType, preference, BrowserPreferenceTimeline.DescribeReading(known, reading));
                }

                return JoinSummary(eventType, preference);
            case BrowserPreferenceSettings.ZoomEventType:
                var percent = payload.TryGetProperty("zoomPercent", out var value) && value.TryGetDouble(out var number)
                    ? $"{number.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}%"
                    : null;
                var host = ReadString(payload, "host");
                return JoinSummary(eventType, ReadString(payload, "mode"), string.IsNullOrEmpty(host) ? null : host, percent);
            case BrowserPreferenceSettings.SentEventType:
                var fields = payload.TryGetProperty("fields", out var sent) && sent.ValueKind == JsonValueKind.Object
                    ? string.Join(" ", sent.EnumerateObject().Select(field => field.Name))
                    : null;
                var isFirst = payload.TryGetProperty("first", out var first) && first.ValueKind == JsonValueKind.True;
                return JoinSummary(eventType, ReadString(payload, "point"), isFirst ? "all fields" : fields);
            case BrowserPreferenceSettings.ColorMapsEventType:
                if (ColorMapChangeSummary(payload) is { } colorChange)
                {
                    return JoinSummary(eventType, ReadString(payload, "point"), colorChange);
                }

                var maps = payload.TryGetProperty("maps", out var sentMaps) && sentMaps.ValueKind == JsonValueKind.Object
                    ? string.Join(" ", sentMaps.EnumerateObject().Select(map => map.Name))
                    : null;
                return JoinSummary(eventType, ReadString(payload, "point"), maps);
            case BrowserPreferenceSettings.SnapshotEventType:
                return JoinSummary(eventType, ReadString(payload, "profileDirectory"));
            default:
                return JoinSummary(eventType, ReadString(payload, "reason"));
        }
    }

    // A reading that failed holds no level, so the frame plays as captured.
    private static FullscreenMagnification? ReadMagnification(JsonElement payload)
    {
        if (!payload.TryGetProperty("fullscreenMagnification", out var value) ||
            value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("level", out var level) ||
            level.ValueKind != JsonValueKind.Number ||
            !level.TryGetDouble(out var number) ||
            !double.IsFinite(number) ||
            number <= 0 ||
            ReadInt32(value, "x") is not { } x ||
            ReadInt32(value, "y") is not { } y)
        {
            return null;
        }

        return new FullscreenMagnification(number, x, y);
    }

    private static void AddAudioTrack(
        string root,
        long timestamp,
        JsonElement payload,
        IDictionary<string, SessionAudioTrack> tracks)
    {
        var stream = ReadString(payload, "stream");
        var path = ReadString(payload, "path");
        if (string.IsNullOrWhiteSpace(stream) ||
            !TryResolvePath(root, path, out var absolutePath) ||
            !File.Exists(absolutePath))
        {
            return;
        }

        tracks[stream] = new SessionAudioTrack(
            stream,
            path!,
            absolutePath,
            timestamp);
    }

    /// <summary>
    /// The timeline's one-line description of an event, from its channel,
    /// type, and the payload properties playback reads.
    /// </summary>
    public static string CreateSummary(
        string channel,
        string eventType,
        JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return eventType;
        }

        if (channel == InputRecordabilityRecords.Channel &&
            InputRecordabilityRecords.EventTypes.Contains(eventType))
        {
            bool? Recordable() => payload.TryGetProperty("inputRecordable", out var value) &&
                value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? value.ValueKind == JsonValueKind.True
                    : null;
            return eventType switch
            {
                InputRecordabilityRecords.InputDesktopEventType => JoinSummary(
                    eventType,
                    ReadString(payload, "desktopName") is { } desktop ? "desktop " + desktop : "desktop not readable",
                    Recordable() == true ? "input recordable" : "input not recordable"),
                InputRecordabilityRecords.ForegroundIntegrityEventType => JoinSummary(
                    eventType,
                    ReadString(payload, "processName"),
                    ReadString(payload, "integrityLevel") ?? "level not readable",
                    Recordable() switch { true => "input recordable", false => "input not recordable", null => null }),
                _ => JoinSummary(eventType, ReadString(payload, "integrityLevel") ?? "level not readable")
            };
        }

        if (channel == "window.foreground")
        {
            var title = ReadString(payload, "title");
            var process = ReadString(payload, "processName");
            return JoinSummary(eventType, process, title);
        }

        if (channel == WindowsPreferenceSettings.Channel)
        {
            var setting = ReadString(payload, "setting");
            if (setting is not null &&
                WindowsPreferenceSettings.Find(setting) is { } known &&
                payload.TryGetProperty("current", out var current) &&
                current.ValueKind == JsonValueKind.Object &&
                current.TryGetProperty(setting, out var reading))
            {
                return JoinSummary(eventType, setting, WindowsPreferenceTimeline.Describe(known, reading));
            }

            return JoinSummary(eventType, setting ?? ReadString(payload, "reason"));
        }

        if (channel == BrowserPreferenceSettings.Channel)
        {
            return BrowserPreferenceSummary(eventType, payload);
        }

        if (channel == AssistiveTechnologyRecords.Channel)
        {
            return AssistiveTechnologySummary(eventType, payload);
        }

        if (channel == KeyboardHookRecords.Channel)
        {
            return KeyboardHookSummary(eventType, payload);
        }

        if (channel == MagnifierChanges.Channel &&
            payload.TryGetProperty("changed", out var changed) && changed.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("current", out var now) && now.ValueKind == JsonValueKind.Object)
        {
            bool Changed(string part) => changed.TryGetProperty(part, out var flag) && flag.ValueKind == JsonValueKind.True;
            var magnification = ReadMagnification(now);
            var effect = ReadColorEffect(now);
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var parts = new List<string>();
            if (Changed(MagnifierChanges.Level))
            {
                parts.Add(magnification is { } read
                    ? $"level {Math.Round(read.Level * 100).ToString(culture)} percent"
                    : "level not read");
            }

            if (Changed(MagnifierChanges.Position))
            {
                parts.Add(magnification is { } read
                    ? string.Create(culture, $"position {read.X}, {read.Y}")
                    : "position not read");
            }

            if (Changed(MagnifierChanges.ColorEffect))
            {
                parts.Add(effect is null ? "color effect not read"
                    : ColorEffect.IsIdentity(effect) ? "color effect none"
                    : ColorEffect.IsInversion(effect) ? "color effect inverted colors"
                    : "a color effect");
            }

            return JoinSummary(eventType, string.Join("; ", parts));
        }

        if (channel == "accessibility.uia.events")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "name"),
                ReadString(payload, "automationId"),
                ReadString(payload, "controlType"));
        }

        if (channel == "session.annotations")
        {
            return JoinSummary(eventType, ReadString(payload, "note"));
        }

        if (channel.StartsWith("audio.", StringComparison.Ordinal))
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "stream"),
                ReadString(payload, "device"));
        }

        if (channel == "browser.navigation")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "url"),
                ReadString(payload, "navigationKind"),
                ReadString(payload, "outcome"));
        }

        if (channel == "browser.dispatch")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "eventName"),
                ReadString(payload, "outcome"));
        }

        if (channel == "browser.listener")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "eventName"),
                ReadString(payload, "registrationKind"));
        }

        if (channel == "browser.dom")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "reason"),
                ReadString(payload, "checkpointId"));
        }

        return eventType;
    }

    // Protocol 0.59: a later color maps send names the colors that changed in
    // each map, so the summary says how many, in which maps, and which, as
    // "3 colors changed in light and dark: menu background, ...". Maps whose
    // changes differ are each described. Null for a record without them.
    private static string? ColorMapChangeSummary(JsonElement payload)
    {
        if (!payload.TryGetProperty("changedColors", out var changed) || changed.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var byMap = changed.EnumerateObject()
            .Where(map => map.Value.ValueKind == JsonValueKind.Array)
            .Select(map => (
                Map: map.Name == "forcedColors" ? "forced colors" : map.Name,
                Colors: map.Value.EnumerateArray()
                    .Where(color => color.ValueKind == JsonValueKind.String)
                    .Select(color => color.GetString()!)
                    .ToList()))
            .Where(map => map.Colors.Count > 0)
            .ToList();
        if (byMap.Count == 0)
        {
            return null;
        }

        var parts = byMap
            .GroupBy(map => string.Join("\n", map.Colors))
            .Select(group =>
            {
                var colors = group.First().Colors;
                var count = colors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var shown = colors.Take(5).Select(ColorLabel).ToList();
                if (colors.Count > 5)
                {
                    shown.Add($"and {(colors.Count - 5).ToString(System.Globalization.CultureInfo.InvariantCulture)} more");
                }

                var noun = colors.Count == 1 ? "color" : "colors";
                return $"{count} {noun} changed in {JoinAnd([.. group.Select(map => map.Map)])}: {string.Join(", ", shown)}";
            });
        return string.Join("; ", parts);
    }

    // A RendererColorId name as words: kColorMenuItemBackgroundSelected is
    // "menu item background selected".
    private static string ColorLabel(string name)
    {
        var bare = name.StartsWith("kColor", StringComparison.Ordinal) ? name["kColor".Length..] : name;
        var words = new System.Text.StringBuilder();
        for (var index = 0; index < bare.Length; index++)
        {
            if (index > 0 && char.IsUpper(bare[index]) && !char.IsUpper(bare[index - 1]))
            {
                words.Append(' ');
            }

            words.Append(char.ToLowerInvariant(bare[index]));
        }

        return words.ToString();
    }

    private static string JoinAnd(IReadOnlyList<string> items) =>
        items.Count <= 1
            ? string.Concat(items)
            : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    // A keyboard hook record as the event list shows it: the key, its
    // direction, and whether it was injected; or the installation's reason.
    private static string KeyboardHookSummary(string eventType, JsonElement payload)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        bool Flag(string name) => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        if (eventType == KeyboardHookRecords.KeyEventType)
        {
            var key = payload.TryGetProperty("virtualKey", out var virtualKey) && virtualKey.ValueKind == JsonValueKind.Number &&
                virtualKey.TryGetInt32(out var code)
                ? KeyboardHookRecords.KeyName(code)
                : null;
            return JoinSummary(
                eventType,
                key is null ? null : key + (Flag("up") ? " up" : " down"),
                Flag("lowerIntegrityInjected") ? "injected at lower integrity" : Flag("injected") ? "injected" : null);
        }

        if (eventType == KeyboardHookRecords.InstalledEventType)
        {
            var number = payload.TryGetProperty("installation", out var installation) && installation.ValueKind == JsonValueKind.Number &&
                installation.TryGetInt32(out var value)
                ? "installation " + value.ToString(culture)
                : null;
            return JoinSummary(
                eventType,
                ReadString(payload, "reason"),
                number,
                Flag("installed") ? null : "not installed",
                ReadString(payload, "problem"));
        }

        return eventType;
    }

    // An assistive technology record as the event list shows it: the
    // product, what happened, and the process.
    private static string AssistiveTechnologySummary(string eventType, JsonElement payload)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var product = ReadString(payload, "product");
        string? Process(string name) =>
            payload.TryGetProperty(name, out var id) && id.TryGetInt64(out var number)
                ? "process " + number.ToString(culture)
                : null;
        switch (eventType)
        {
            case AssistiveTechnologyRecords.WatchEventType:
                return JoinSummary(
                    eventType,
                    "watching " + string.Join(", ", payload.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array
                        ? products.EnumerateArray().Select(item => item.GetString())
                        : []),
                    ReadString(payload, "soundProblem"));
            case AssistiveTechnologyRecords.ProcessStartedEventType:
                var atStart = payload.TryGetProperty("runningAtStart", out var running) && running.ValueKind == JsonValueKind.True;
                return JoinSummary(
                    eventType,
                    product,
                    (ReadString(payload, "role") == AssistiveTechnologyRecords.HelperRole ? "helper " : string.Empty) +
                        (atStart ? "running at the start" : "started"),
                    ReadString(payload, "productVersion"),
                    ReadString(payload, "copy") is { } copy && copy != "unknown" ? copy + " copy" : null,
                    Process("processId"));
            case AssistiveTechnologyRecords.ProcessExitedEventType:
                return JoinSummary(
                    eventType,
                    product,
                    ReadString(payload, "role") == AssistiveTechnologyRecords.HelperRole ? "helper exited" : "exited",
                    Process("processId"));
            case AssistiveTechnologyRecords.ModuleLoadedEventType:
            case AssistiveTechnologyRecords.ModuleUnloadedEventType:
                return JoinSummary(
                    eventType,
                    product,
                    ReadString(payload, "moduleName") +
                        (eventType == AssistiveTechnologyRecords.ModuleLoadedEventType ? " seen in" : " gone from") +
                        " browser " + Process("hostProcessId"));
            case AssistiveTechnologyRecords.SoundStartedEventType:
                return JoinSummary(eventType, product, "sound started", Process("processId"));
            case AssistiveTechnologyRecords.SoundEndedEventType:
                var length = payload.TryGetProperty("startedAt", out var started) && started.TryGetInt64(out var startedAt) &&
                    payload.TryGetProperty("lastSoundAt", out var last) && last.TryGetInt64(out var lastSoundAt)
                        ? ((lastSoundAt - startedAt) / 1_000_000.0).ToString("0", culture) + " ms of sound"
                        : null;
                return JoinSummary(eventType, product, "sound ended", length, ReadString(payload, "endedBy"));
            default:
                return JoinSummary(eventType, product);
        }
    }

    private static string JoinSummary(string fallback, params string?[] values)
    {
        var useful = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return useful.Length == 0
            ? fallback
            : $"{fallback}: {string.Join(", ", useful)}";
    }

    private static bool TryResolvePath(
        string root,
        string? relativePath,
        out string absolutePath)
    {
        absolutePath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(root) +
            Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        absolutePath = candidate;
        return true;
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static int? ReadInt32(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetInt32(out var result)
            ? result
            : null;
}
