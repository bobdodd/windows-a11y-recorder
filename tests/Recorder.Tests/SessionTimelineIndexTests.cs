using Recorder.Session;

namespace Recorder.Tests;

public sealed class SessionTimelineIndexTests
{
    private static readonly string[] Lanes = ["a", "b", "c"];

    [Fact]
    public void KeepsOnlyVisibleEventsInTimeOrder()
    {
        var events = new[]
        {
            Event("a", 10), Event("hidden", 20), Event("b", 30), Event("a", 40)
        };

        var index = Build(events, channel => channel != "hidden");

        Assert.Equal(
            new[] { events[0], events[2], events[3] },
            index.VisibleEvents);
    }

    [Fact]
    public void RejectsEventsOutOfTimeOrder()
    {
        Assert.Throws<ArgumentException>(
            () => Build([Event("a", 20), Event("a", 10)]));
    }

    [Fact]
    public void FindsEventsByReferenceAmongEqualTimestamps()
    {
        var first = Event("a", 10);
        var twin = first with { };
        var index = Build([first, twin, Event("b", 10)]);

        Assert.Equal(0, index.IndexOf(first));
        Assert.Equal(1, index.IndexOf(twin));
        Assert.Equal(-1, index.IndexOf(Event("a", 10)));
        Assert.Equal(-1, index.IndexOf(null));
    }

    [Fact]
    public void FindsTheNearestEventInALaneWithinTheRange()
    {
        var events = new[]
        {
            Event("a", 0), Event("b", 45), Event("a", 50), Event("a", 60), Event("a", 200)
        };
        var index = Build(events);

        Assert.Same(events[2], index.NearestInLane(0, 54, 0, 100));
        Assert.Same(events[3], index.NearestInLane(0, 56, 0, 100));
        Assert.Same(events[2], index.NearestInLane(0, 55, 0, 100));
        Assert.Same(events[3], index.NearestInLane(0, 190, 0, 100));
        Assert.Same(events[2], index.NearestInLane(0, 5, 40, 100));
        Assert.Null(index.NearestInLane(1, 50, 46, 100));
        Assert.Null(index.NearestInLane(2, 50, 0, 100));
    }

    [Fact]
    public void FindsTheNearestEventInAnyLane()
    {
        var events = new[] { Event("a", 10), Event("b", 20), Event("c", 40) };
        var index = Build(events);

        Assert.Same(events[1], index.Nearest(24, 0, 100));
        Assert.Same(events[2], index.Nearest(31, 0, 100));
        Assert.Null(index.Nearest(5, 50, 100));
    }

    [Fact]
    public void ReportsEachOccupiedColumnOnce()
    {
        var events = new[]
        {
            Event("a", 0), Event("a", 5), Event("a", 9),
            Event("a", 35), Event("a", 99), Event("a", 100), Event("a", 150)
        };
        var index = Build(events);

        Assert.Equal(
            new[] { 0, 3, 9 },
            index.OccupiedColumns(0, 0, 100, 10).ToArray());
        Assert.Equal(
            new[] { 1 },
            index.OccupiedColumns(0, 30, 20, 4).ToArray());
        Assert.Empty(index.OccupiedColumns(1, 0, 100, 10));
    }

    [Fact]
    public void MatchesADirectScanOfEveryEvent()
    {
        var random = new Random(1234);
        var time = 0L;
        var events = Enumerable.Range(0, 5_000)
            .Select(_ => Event(Lanes[random.Next(Lanes.Length)], time += random.Next(0, 50)))
            .ToArray();
        var index = Build(events);
        const long start = 20_000;
        const long duration = 60_000;
        const int width = 333;

        var expected = events
            .Where(item => item.Channel == "b" &&
                item.MonotonicNanoseconds >= start &&
                item.MonotonicNanoseconds <= start + duration)
            .Select(item => (int)Math.Floor(
                (item.MonotonicNanoseconds - start) / (double)duration * width))
            .Where(column => column < width)
            .Distinct()
            .ToArray();
        Assert.Equal(expected, index.OccupiedColumns(1, start, duration, width).ToArray());

        for (var trial = 0; trial < 200; trial++)
        {
            var timestamp = random.NextInt64(start, start + duration);
            var direct = events
                .Where(item => item.Channel == "c" &&
                    item.MonotonicNanoseconds >= start &&
                    item.MonotonicNanoseconds <= start + duration)
                .MinBy(item => Math.Abs(item.MonotonicNanoseconds - timestamp));
            Assert.Same(direct, index.NearestInLane(2, timestamp, start, start + duration));
        }
    }

    private static SessionTimelineIndex Build(
        IReadOnlyList<SessionTimelineEvent> events,
        Func<string, bool>? isVisible = null) =>
        new(
            events,
            isVisible ?? (_ => true),
            channel => Array.IndexOf(Lanes, channel) is var lane and >= 0 ? lane : 2,
            channel => Array.IndexOf(Lanes, channel) is var series and >= 0 ? series : 2,
            laneCount: 3,
            seriesCount: 3);

    private static SessionTimelineEvent Event(string channel, long time) =>
        new(0, Guid.NewGuid().ToString("N"), "observed", channel, "test", time, "", 0, 0);
}
