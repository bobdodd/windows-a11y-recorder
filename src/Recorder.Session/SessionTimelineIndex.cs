namespace Recorder.Session;

/// <summary>
/// Time-ordered lookups over the timeline events that pass the current
/// channel filters. Building the index is proportional to the event count
/// and happens only when the session or the filters change. Drawing, hit
/// testing, and keyboard stepping then use binary searches, so their cost
/// no longer grows with the number of recorded events.
/// </summary>
public sealed class SessionTimelineIndex
{
    private readonly long[] _visibleTimes;
    private readonly long[][] _laneTimes;
    private readonly int[][] _laneVisibleIndices;
    private readonly long[][] _seriesTimes;

    /// <param name="events">Events in ascending monotonic order.</param>
    /// <param name="isVisible">Whether a channel passes the filters.</param>
    /// <param name="laneOf">The lane a channel is drawn in.</param>
    /// <param name="seriesOf">
    /// The drawing series a channel belongs to. A series groups channels
    /// that share a lane and a colour.
    /// </param>
    public SessionTimelineIndex(
        IReadOnlyList<SessionTimelineEvent> events,
        Func<string, bool> isVisible,
        Func<string, int> laneOf,
        Func<string, int> seriesOf,
        int laneCount,
        int seriesCount)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(isVisible);
        ArgumentNullException.ThrowIfNull(laneOf);
        ArgumentNullException.ThrowIfNull(seriesOf);
        ArgumentOutOfRangeException.ThrowIfLessThan(laneCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seriesCount, 1);

        var visible = new List<SessionTimelineEvent>();
        var laneTimes = NewLists<long>(laneCount);
        var laneIndices = NewLists<int>(laneCount);
        var seriesTimes = NewLists<long>(seriesCount);
        var visibility = new Dictionary<string, (bool Visible, int Lane, int Series)>(
            StringComparer.Ordinal);
        var previous = long.MinValue;
        foreach (var item in events)
        {
            if (item.MonotonicNanoseconds < previous)
            {
                throw new ArgumentException(
                    "Timeline events must be in ascending monotonic order.",
                    nameof(events));
            }

            previous = item.MonotonicNanoseconds;
            if (!visibility.TryGetValue(item.Channel, out var placement))
            {
                placement = (
                    isVisible(item.Channel),
                    Math.Clamp(laneOf(item.Channel), 0, laneCount - 1),
                    Math.Clamp(seriesOf(item.Channel), 0, seriesCount - 1));
                visibility[item.Channel] = placement;
            }

            if (!placement.Visible)
            {
                continue;
            }

            laneTimes[placement.Lane].Add(item.MonotonicNanoseconds);
            laneIndices[placement.Lane].Add(visible.Count);
            seriesTimes[placement.Series].Add(item.MonotonicNanoseconds);
            visible.Add(item);
        }

        VisibleEvents = visible;
        _visibleTimes = visible.Select(item => item.MonotonicNanoseconds).ToArray();
        _laneTimes = laneTimes.Select(list => list.ToArray()).ToArray();
        _laneVisibleIndices = laneIndices.Select(list => list.ToArray()).ToArray();
        _seriesTimes = seriesTimes.Select(list => list.ToArray()).ToArray();
    }

    public IReadOnlyList<SessionTimelineEvent> VisibleEvents { get; }

    public int SeriesCount => _seriesTimes.Length;

    /// <summary>
    /// Returns the index of an event in <see cref="VisibleEvents"/> by
    /// reference, or -1 when it is not visible.
    /// </summary>
    public int IndexOf(SessionTimelineEvent? item)
    {
        if (item is null)
        {
            return -1;
        }

        for (var index = LowerBound(_visibleTimes, item.MonotonicNanoseconds, 0);
             index < _visibleTimes.Length &&
             _visibleTimes[index] == item.MonotonicNanoseconds;
             index++)
        {
            if (ReferenceEquals(VisibleEvents[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the visible event in a lane closest to a timestamp, limited to
    /// the inclusive range. When two events are equally close, the earlier
    /// one is returned.
    /// </summary>
    public SessionTimelineEvent? NearestInLane(
        int lane,
        long timestamp,
        long rangeStart,
        long rangeEnd)
    {
        if (lane < 0 || lane >= _laneTimes.Length)
        {
            return null;
        }

        var index = Nearest(_laneTimes[lane], timestamp, rangeStart, rangeEnd);
        return index < 0 ? null : VisibleEvents[_laneVisibleIndices[lane][index]];
    }

    /// <summary>
    /// Finds the visible event in any lane closest to a timestamp, limited
    /// to the inclusive range.
    /// </summary>
    public SessionTimelineEvent? Nearest(
        long timestamp,
        long rangeStart,
        long rangeEnd)
    {
        var index = Nearest(_visibleTimes, timestamp, rangeStart, rangeEnd);
        return index < 0 ? null : VisibleEvents[index];
    }

    /// <summary>
    /// Returns each pixel column, from 0 to <paramref name="width"/> - 1,
    /// that contains at least one event of the series inside the viewport.
    /// The work is bounded by the number of columns rather than the number
    /// of events.
    /// </summary>
    public IEnumerable<int> OccupiedColumns(
        int series,
        long viewportStart,
        long viewportDuration,
        int width)
    {
        if (series < 0 ||
            series >= _seriesTimes.Length ||
            viewportDuration <= 0 ||
            width <= 0)
        {
            yield break;
        }

        var times = _seriesTimes[series];
        var viewportEnd = viewportStart + viewportDuration;
        var index = LowerBound(times, viewportStart, 0);
        while (index < times.Length && times[index] <= viewportEnd)
        {
            var column = (int)Math.Floor(
                (times[index] - viewportStart) / (double)viewportDuration * width);
            if (column >= width)
            {
                yield break;
            }

            yield return column;
            var nextColumnStart = viewportStart + (long)Math.Ceiling(
                (column + 1) / (double)width * viewportDuration);
            index = LowerBound(
                times,
                Math.Max(nextColumnStart, times[index] + 1),
                index + 1);
        }
    }

    private static int Nearest(
        long[] times,
        long timestamp,
        long rangeStart,
        long rangeEnd)
    {
        var first = LowerBound(times, rangeStart, 0);
        var afterLast = UpperBound(times, rangeEnd, first);
        if (first >= afterLast)
        {
            return -1;
        }

        var target = Math.Clamp(timestamp, rangeStart, rangeEnd);
        var candidate = LowerBound(times, target, first);
        if (candidate >= afterLast)
        {
            return afterLast - 1;
        }

        if (candidate == first)
        {
            return first;
        }

        var before = candidate - 1;
        while (before > first && times[before - 1] == times[before])
        {
            before--;
        }

        return target - times[before] <= times[candidate] - target
            ? before
            : candidate;
    }

    private static int LowerBound(long[] times, long value, int start)
    {
        var low = Math.Clamp(start, 0, times.Length);
        var high = times.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (times[middle] < value)
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

    private static int UpperBound(long[] times, long value, int start)
    {
        var low = Math.Clamp(start, 0, times.Length);
        var high = times.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (times[middle] <= value)
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

    private static List<T>[] NewLists<T>(int count) =>
        Enumerable.Range(0, count).Select(_ => new List<T>()).ToArray();
}
