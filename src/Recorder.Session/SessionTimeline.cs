namespace Recorder.Session;

/// <summary>
/// Time-ordered access to a recording's timeline events, without requiring
/// every event to be held in memory. Events are ordered by monotonic time,
/// and events with the same time by <see cref="SessionTimelineEvent.EventKey"/>.
/// Every lookup is limited to the channels passed to it.
/// </summary>
public interface ISessionTimeline
{
    /// <summary>The number of events in the recording.</summary>
    long Count { get; }

    /// <summary>The number of events on each channel.</summary>
    IReadOnlyDictionary<string, long> ChannelCounts { get; }

    /// <summary>Which parts of the recording hold events, for drawing.</summary>
    TimelineOccupancy Occupancy { get; }

    /// <summary>The last event at or before a time.</summary>
    Task<SessionTimelineEvent?> AtOrBeforeAsync(
        long timestamp,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default);

    /// <summary>The event after, or before, an event in timeline order.</summary>
    Task<SessionTimelineEvent?> AdjacentAsync(
        SessionTimelineEvent from,
        bool forward,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default);

    /// <summary>The first, or last, event in timeline order.</summary>
    Task<SessionTimelineEvent?> EndAsync(
        bool last,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The event closest to a time, limited to the inclusive range. When
    /// two events are equally close, the earlier one is returned; among
    /// events with the same time, the first.
    /// </summary>
    Task<SessionTimelineEvent?> NearestAsync(
        long timestamp,
        long rangeStart,
        long rangeEnd,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Which fixed-width time buckets of a recording hold at least one event of
/// each channel. The recording is divided into <see cref="BucketCount"/>
/// buckets, so the grid's size does not grow with the number of events.
/// </summary>
/// <remarks>
/// A column is marked from the bucket that holds an event, not from the
/// event's own time, so an event can be drawn up to one bucket earlier than
/// its time. The width is a power of two, so more than half of the buckets
/// cover the recording, and at the player's greatest zoom, 32 times, a
/// bucket is no wider than one pixel column on a timeline up to 4,096 pixels
/// wide.
/// </remarks>
public sealed class TimelineOccupancy
{
    public const int BucketCount = 262_144;

    private readonly IReadOnlyDictionary<string, int[]> _buckets;

    /// <param name="durationNanoseconds">The recording's duration.</param>
    /// <param name="buckets">
    /// Each channel's occupied bucket indices, in ascending order, with no
    /// repeats. Bucket i covers the times from i × <see cref="BucketWidth"/>
    /// up to, but not including, (i + 1) × <see cref="BucketWidth"/>.
    /// </param>
    public TimelineOccupancy(
        long durationNanoseconds,
        IReadOnlyDictionary<string, int[]> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        BucketWidth = WidthFor(durationNanoseconds);
        _buckets = buckets;
    }

    public long BucketWidth { get; }

    /// <summary>
    /// The bucket width for a recording of this duration: the smallest power
    /// of two, in nanoseconds, for which <see cref="BucketCount"/> buckets
    /// cover every time from zero to the duration. A grid can be kept at a
    /// finer power of two while the duration grows and merged into this one,
    /// as <see cref="OccupancyBitmap"/> does.
    /// </summary>
    public static long WidthFor(long durationNanoseconds)
    {
        var width = 1L;
        while (Math.Max(0, durationNanoseconds) / width >= BucketCount)
        {
            width *= 2;
        }

        return width;
    }

    /// <summary>
    /// Builds the grid from events in any order.
    /// </summary>
    public static TimelineOccupancy FromEvents(
        IEnumerable<SessionTimelineEvent> events,
        long durationNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(events);
        var width = WidthFor(durationNanoseconds);
        var sets = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        foreach (var item in events)
        {
            if (!sets.TryGetValue(item.Channel, out var set))
            {
                set = [];
                sets[item.Channel] = set;
            }

            set.Add(BucketOf(item.MonotonicNanoseconds, width));
        }

        return new TimelineOccupancy(
            durationNanoseconds,
            sets.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
    }

    /// <summary>The bucket a time falls in.</summary>
    public static int BucketOf(long timestamp, long bucketWidth) =>
        (int)Math.Clamp(Math.Max(0, timestamp) / bucketWidth, 0, int.MaxValue);

    /// <summary>
    /// Returns each pixel column, from 0 to <paramref name="width"/> - 1, in
    /// ascending order, that holds a bucket occupied by any of the channels
    /// and overlapping the viewport. The work is bounded by the number of
    /// occupied buckets in the viewport.
    /// </summary>
    public IReadOnlyList<int> OccupiedColumns(
        IEnumerable<string> channels,
        long viewportStart,
        long viewportDuration,
        int width)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (viewportDuration <= 0 || width <= 0)
        {
            return [];
        }

        var viewportEnd = viewportStart + viewportDuration;
        var first = BucketOf(viewportStart, BucketWidth);
        var last = BucketOf(viewportEnd, BucketWidth);
        var occupied = new bool[width];
        foreach (var channel in channels)
        {
            if (!_buckets.TryGetValue(channel, out var buckets))
            {
                continue;
            }

            var index = Array.BinarySearch(buckets, first);
            for (index = index < 0 ? ~index : index;
                 index < buckets.Length && buckets[index] <= last;
                 index++)
            {
                var time = Math.Max(buckets[index] * BucketWidth, viewportStart);
                var column = (int)Math.Floor(
                    (time - viewportStart) / (double)viewportDuration * width);
                occupied[Math.Clamp(column, 0, width - 1)] = true;
            }
        }

        var columns = new List<int>();
        for (var column = 0; column < width; column++)
        {
            if (occupied[column])
            {
                columns.Add(column);
            }
        }

        return columns;
    }
}

/// <summary>
/// A timeline over events held in memory, for a recording read from its
/// session files. Each lookup is a binary search per channel.
/// </summary>
public sealed class InMemorySessionTimeline : ISessionTimeline
{
    private readonly SessionTimelineEvent[] _events;
    private readonly Dictionary<string, int[]> _positions;
    private readonly Lazy<TimelineOccupancy> _occupancy;

    /// <param name="events">
    /// Events in timeline order: ascending monotonic time, and ascending
    /// <see cref="SessionTimelineEvent.EventKey"/> among events with the same time.
    /// </param>
    public InMemorySessionTimeline(
        IReadOnlyList<SessionTimelineEvent> events,
        long durationNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(events);
        _events = events.ToArray();
        var positions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < _events.Length; index++)
        {
            if (index > 0 && Compare(_events[index - 1], _events[index]) > 0)
            {
                throw new ArgumentException(
                    "Timeline events must be in ascending time and line order.",
                    nameof(events));
            }

            var channel = _events[index].Channel;
            if (!positions.TryGetValue(channel, out var list))
            {
                list = [];
                positions[channel] = list;
            }

            list.Add(index);
        }

        _positions = positions.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
        ChannelCounts = _positions.ToDictionary(
            pair => pair.Key,
            pair => (long)pair.Value.Length,
            StringComparer.Ordinal);
        _occupancy = new Lazy<TimelineOccupancy>(
            () => TimelineOccupancy.FromEvents(_events, durationNanoseconds));
    }

    public long Count => _events.Length;

    public IReadOnlyDictionary<string, long> ChannelCounts { get; }

    public TimelineOccupancy Occupancy => _occupancy.Value;

    public Task<SessionTimelineEvent?> AtOrBeforeAsync(
        long timestamp,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(At(Best(channels, positions =>
            UpperBound(positions, item => item.MonotonicNanoseconds <= timestamp) - 1, preferLater: true)));

    public Task<SessionTimelineEvent?> AdjacentAsync(
        SessionTimelineEvent from,
        bool forward,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(from);
        return Task.FromResult(At(forward
            ? Best(channels, positions =>
                UpperBound(positions, item => Compare(item, from) <= 0), preferLater: false)
            : Best(channels, positions =>
                UpperBound(positions, item => Compare(item, from) < 0) - 1, preferLater: true)));
    }

    public Task<SessionTimelineEvent?> EndAsync(
        bool last,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(At(Best(
            channels,
            positions => last ? positions.Length - 1 : 0,
            preferLater: last)));

    public Task<SessionTimelineEvent?> NearestAsync(
        long timestamp,
        long rangeStart,
        long rangeEnd,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (rangeStart > rangeEnd)
        {
            return Task.FromResult<SessionTimelineEvent?>(null);
        }

        var target = Math.Clamp(timestamp, rangeStart, rangeEnd);
        var after = At(Best(channels, positions =>
            UpperBound(positions, item => item.MonotonicNanoseconds < target), preferLater: false));
        if (after is not null && after.MonotonicNanoseconds > rangeEnd)
        {
            after = null;
        }

        // The latest time before the target, and the first event at it.
        var latestBefore = At(Best(channels, positions =>
            UpperBound(positions, item => item.MonotonicNanoseconds < target) - 1, preferLater: true));
        SessionTimelineEvent? before = null;
        if (latestBefore is not null && latestBefore.MonotonicNanoseconds >= rangeStart)
        {
            var time = latestBefore.MonotonicNanoseconds;
            before = At(Best(channels, positions =>
                UpperBound(positions, item => item.MonotonicNanoseconds < time), preferLater: false));
        }

        return Task.FromResult(
            before is null ? after :
            after is null ? before :
            target - before.MonotonicNanoseconds <= after.MonotonicNanoseconds - target ? before : after);
    }

    /// <summary>Timeline order: time, then line.</summary>
    public static int Compare(SessionTimelineEvent left, SessionTimelineEvent right)
    {
        var time = left.MonotonicNanoseconds.CompareTo(right.MonotonicNanoseconds);
        return time != 0 ? time : left.EventKey.CompareTo(right.EventKey);
    }

    private SessionTimelineEvent? At(int index) =>
        index < 0 ? null : _events[index];

    // Each channel's candidate, given as a position in that channel's list,
    // becomes a position in the whole timeline; the earliest or latest is
    // returned, or -1.
    private int Best(IReadOnlySet<string> channels, Func<int[], int> candidateOf, bool preferLater)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var best = -1;
        foreach (var channel in channels)
        {
            if (!_positions.TryGetValue(channel, out var positions))
            {
                continue;
            }

            var candidate = candidateOf(positions);
            if (candidate < 0 || candidate >= positions.Length)
            {
                continue;
            }

            var index = positions[candidate];
            if (best < 0 || (preferLater ? index > best : index < best))
            {
                best = index;
            }
        }

        return best;
    }

    // The number of leading positions whose events satisfy a condition that
    // holds for a prefix of the channel's events.
    private int UpperBound(int[] positions, Func<SessionTimelineEvent, bool> inPrefix)
    {
        var low = 0;
        var high = positions.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (inPrefix(_events[positions[middle]]))
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
