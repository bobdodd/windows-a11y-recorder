using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// Builds a playback archive from event records as another reader parses
/// them, so one pass over the event log can serve both validation and
/// playback.
/// </summary>
/// <remarks>
/// Records must be added in event-log order. A record the caller could not
/// parse as a JSON object is reported with <see cref="AddUnreadable"/>, and
/// building then fails in the same way <see cref="SessionArchiveReader"/>
/// fails on that record.
/// </remarks>
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
        "accessibility.uia.events",
        "session.annotations",
        "graphics.desktop.frames"
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
        "device",
        "eventName",
        "height",
        "name",
        "navigationId",
        "navigationKind",
        "note",
        "outcome",
        "path",
        "primaryPage",
        "processName",
        "reason",
        "registrationKind",
        "rendererProcessId",
        "sameDocument",
        "stream",
        "title",
        "truncated",
        "url",
        "width"
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
    private readonly bool _retainEvents;
    private long _maximumTimestamp;
    private string? _unreadable;
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

    /// <param name="byteOffset">
    /// Where the record's line starts in events.ndjson, in bytes.
    /// </param>
    /// <param name="byteLength">
    /// The line's length in bytes, without its line ending.
    /// </param>
    public void Add(long lineNumber, long byteOffset, int byteLength, JsonElement record)
    {
        ThrowIfBuilt();
        if (record.ValueKind != JsonValueKind.Object)
        {
            AddUnreadable(lineNumber, "The event is not a JSON object.");
            return;
        }

        var timelineEvent = SessionArchiveReader.CreateTimelineEvent(
            lineNumber,
            byteOffset,
            byteLength,
            record,
            out var payload);
        AddCore(timelineEvent, payload);
    }

    /// <summary>
    /// Adds an event read from a store other than the event log. Payload
    /// need hold only <see cref="PayloadProperties"/>, and only for channels
    /// where <see cref="ReadsPayload"/> is true; otherwise pass default.
    /// </summary>
    public void AddEvent(
        long line,
        string eventId,
        string evidenceClass,
        string channel,
        string eventType,
        long monotonicNanoseconds,
        JsonElement payload,
        long eventKey)
    {
        ThrowIfBuilt();
        AddCore(
            new SessionTimelineEvent(
                line,
                eventId,
                evidenceClass,
                channel,
                eventType,
                monotonicNanoseconds,
                SessionArchiveReader.CreateSummary(channel, eventType, payload),
                0,
                0,
                eventKey),
            payload);
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
            SessionArchiveReader.AddFrame(_root, timestamp, payload, _frames);
        }

        if (timelineEvent.Channel.StartsWith("audio.", StringComparison.Ordinal) &&
            timelineEvent.EventType == "audio-stream-started" &&
            payload.ValueKind == JsonValueKind.Object)
        {
            SessionArchiveReader.AddAudioTrack(
                _root,
                timestamp,
                payload,
                _audioTracks);
        }
    }

    public void AddUnreadable(long lineNumber, string reason)
    {
        ThrowIfBuilt();
        _unreadable ??= $"Event line {lineNumber} could not be read: {reason}";
    }

    public async Task<SessionPlaybackArchive> BuildAsync(
        CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(_root, "manifest.json");
        if (!File.Exists(manifestPath) ||
            !File.Exists(Path.Combine(_root, "events.ndjson")))
        {
            throw new InvalidDataException(
                "The selected folder does not contain manifest.json and events.ndjson.");
        }

        var manifest = await SessionArchiveReader.ReadManifestAsync(
            manifestPath,
            cancellationToken).ConfigureAwait(false);
        return Build(manifest);
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

        var manifest = await SessionArchiveReader.ReadManifestAsync(
            manifestPath,
            cancellationToken).ConfigureAwait(false);
        return Build(manifest) with { RecordSource = recordSource };
    }

    internal SessionPlaybackArchive Build(SessionManifest manifest)
    {
        ThrowIfBuilt();
        if (_unreadable is not null)
        {
            throw new InvalidDataException(_unreadable);
        }

        _built = true;
        _events.Sort(InMemorySessionTimeline.Compare);
        _frames.Sort(static (left, right) =>
            left.MonotonicNanoseconds.CompareTo(right.MonotonicNanoseconds));
        var duration = Math.Max(manifest.DurationNanoseconds ?? 0, _maximumTimestamp);
        var browserNavigations = BrowserNavigationCorrelator.Build(
            _browserProjections,
            duration);
        return new SessionPlaybackArchive(
            _root,
            manifest,
            duration,
            _events,
            _frames,
            _audioTracks.Values
                .OrderBy(track => track.Stream, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            browserNavigations);
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "The playback archive has already been built.");
        }
    }
}
