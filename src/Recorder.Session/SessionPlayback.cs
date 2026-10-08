namespace Recorder.Session;

public sealed record SessionPlaybackArchive(
    string SessionDirectory,
    SessionManifest Manifest,
    long DurationNanoseconds,
    IReadOnlyList<SessionTimelineEvent> Events,
    IReadOnlyList<SessionVideoFrame> Frames,
    IReadOnlyList<SessionAudioTrack> AudioTracks,
    IReadOnlyList<BrowserNavigationCorrelation> BrowserNavigations)
{
    /// <summary>
    /// Where complete event records are read from. Null for an archive
    /// built without a store, whose records cannot be read.
    /// </summary>
    public ISessionEventRecordSource? RecordSource { get; init; }

    private ISessionTimeline? _timeline;

    /// <summary>
    /// The recording's timeline: for a recording read from the database, a
    /// timeline that queries it, and <see cref="Events"/> is empty;
    /// otherwise a timeline over <see cref="Events"/>.
    /// </summary>
    public ISessionTimeline Timeline
    {
        get => _timeline ??= new InMemorySessionTimeline(Events, DurationNanoseconds);
        init => _timeline = value;
    }

    /// <summary>
    /// Reads an event's complete record. Playback keeps only where each
    /// record is, so its text is read when it is needed.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The store no longer holds that event.
    /// </exception>
    public string ReadEventJson(SessionTimelineEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return RecordSource is not null
            ? RecordSource.ReadEventJson(item)
            : throw new InvalidOperationException(
                "The playback archive has no store to read event records from.");
    }
}

/// <summary>
/// Reads an event's complete record from where the recording is stored.
/// </summary>
public interface ISessionEventRecordSource
{
    /// <exception cref="InvalidDataException">
    /// The stored record is not that event.
    /// </exception>
    string ReadEventJson(SessionTimelineEvent item);
}

/// <summary>
/// One event in the playback timeline. The complete record stays where the
/// recording is stored; read it with
/// <see cref="SessionPlaybackArchive.ReadEventJson"/>.
/// </summary>
/// <param name="EventKey">
/// The event's key in the database, which follows the order the database
/// stored events in. Orders events that have the same time.
/// </param>
public sealed record SessionTimelineEvent(
    long EventKey,
    string EventId,
    string EvidenceClass,
    string Channel,
    string EventType,
    long MonotonicNanoseconds,
    string Summary);

/// <param name="X">The virtual screen's left edge, which the frame's left edge shows.</param>
/// <param name="Y">The virtual screen's top edge, which the frame's top edge shows.</param>
/// <param name="Magnification">
/// The full screen magnification read with the frame, or null when none was
/// read, as in recordings made before it was recorded.
/// </param>
public sealed record SessionVideoFrame(
    long MonotonicNanoseconds,
    string Path,
    string AbsolutePath,
    int Width,
    int Height,
    int X = 0,
    int Y = 0,
    FullscreenMagnification? Magnification = null);

/// <summary>
/// The full screen magnification transform read with a desktop frame: the
/// level, 1 meaning none, and the offset of the magnified view's upper-left
/// corner relative to the primary monitor's, in unmagnified coordinates.
/// </summary>
public sealed record FullscreenMagnification(double Level, int X, int Y);

public sealed record SessionAudioTrack(
    string Stream,
    string Path,
    string AbsolutePath,
    long StartNanoseconds);
