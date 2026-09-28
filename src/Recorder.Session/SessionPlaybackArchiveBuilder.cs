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
        "frameType",
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
            ReadInt32(payload, "height") ?? 0));
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

        if (channel == "window.foreground")
        {
            var title = ReadString(payload, "title");
            var process = ReadString(payload, "processName");
            return JoinSummary(eventType, process, title);
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
