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
    private readonly string _root;
    private readonly List<SessionTimelineEvent> _events = [];
    private readonly List<SessionVideoFrame> _frames = [];
    private readonly Dictionary<string, SessionAudioTrack> _audioTracks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<BrowserEventProjection> _browserProjections = [];
    private long _maximumTimestamp;
    private string? _unreadable;
    private bool _built;

    public SessionPlaybackArchiveBuilder(string sessionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        _root = Path.GetFullPath(sessionDirectory);
    }

    public string SessionDirectory => _root;

    public void Add(long lineNumber, JsonElement record)
    {
        ThrowIfBuilt();
        if (record.ValueKind != JsonValueKind.Object)
        {
            AddUnreadable(lineNumber, "The event is not a JSON object.");
            return;
        }

        var timelineEvent = SessionArchiveReader.CreateTimelineEvent(
            lineNumber,
            record,
            out var payload);
        var timestamp = timelineEvent.MonotonicNanoseconds;
        _maximumTimestamp = Math.Max(_maximumTimestamp, timestamp);
        _events.Add(timelineEvent);
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

    internal SessionPlaybackArchive Build(SessionManifest manifest)
    {
        ThrowIfBuilt();
        if (_unreadable is not null)
        {
            throw new InvalidDataException(_unreadable);
        }

        _built = true;
        _events.Sort(static (left, right) =>
            left.MonotonicNanoseconds.CompareTo(right.MonotonicNanoseconds));
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
