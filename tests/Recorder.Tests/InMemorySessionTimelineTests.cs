using Recorder.Session;

namespace Recorder.Tests;

public sealed class InMemorySessionTimelineTests
{
    private static readonly HashSet<string> All = ["a", "b", "c"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void RejectsEventsOutOfTimelineOrder()
    {
        Assert.Throws<ArgumentException>(() => Build([Event("a", 20, 1), Event("a", 10, 2)]));
        Assert.Throws<ArgumentException>(() => Build([Event("a", 10, 2), Event("a", 10, 1)]));
    }

    [Fact]
    public async Task CountsEventsByChannel()
    {
        var timeline = Build([Event("a", 0, 1), Event("b", 1, 2), Event("a", 2, 3)]);

        Assert.Equal(3, timeline.Count);
        Assert.Equal(2, timeline.ChannelCounts["a"]);
        Assert.Equal(1, timeline.ChannelCounts["b"]);
        Assert.Null(await timeline.EndAsync(last: false, new HashSet<string> { "missing" }, Token));
    }

    [Fact]
    public async Task FindsTheLastEventAtOrBeforeATimeInTheChannels()
    {
        var events = new[] { Event("a", 10, 1), Event("b", 20, 2), Event("a", 20, 3), Event("b", 30, 4) };
        var timeline = Build(events);

        Assert.Same(events[2], await timeline.AtOrBeforeAsync(25, All, Token));
        Assert.Same(events[1], await timeline.AtOrBeforeAsync(25, new HashSet<string> { "b" }, Token));
        Assert.Same(events[0], await timeline.AtOrBeforeAsync(19, All, Token));
        Assert.Null(await timeline.AtOrBeforeAsync(9, All, Token));
    }

    [Fact]
    public async Task StepsThroughEventsWithTheSameTimeInLineOrder()
    {
        var events = new[]
        {
            Event("a", 10, 1), Event("b", 10, 2), Event("a", 10, 3), Event("c", 20, 4)
        };
        var timeline = Build(events);
        var visible = new HashSet<string> { "a", "c" };

        Assert.Same(events[1], await timeline.AdjacentAsync(events[0], forward: true, All, Token));
        Assert.Same(events[2], await timeline.AdjacentAsync(events[0], forward: true, visible, Token));
        Assert.Same(events[0], await timeline.AdjacentAsync(events[2], forward: false, visible, Token));
        Assert.Same(events[3], await timeline.AdjacentAsync(events[1], forward: true, new HashSet<string> { "c" }, Token));
        Assert.Null(await timeline.AdjacentAsync(events[3], forward: true, All, Token));
        Assert.Same(events[0], await timeline.EndAsync(last: false, All, Token));
        Assert.Same(events[3], await timeline.EndAsync(last: true, All, Token));
    }

    [Fact]
    public async Task FindsTheNearestEventWithinTheRange()
    {
        var events = new[]
        {
            Event("a", 0, 1), Event("b", 45, 2), Event("a", 50, 3), Event("a", 60, 4), Event("a", 200, 5)
        };
        var timeline = Build(events);
        var a = new HashSet<string> { "a" };

        Assert.Same(events[2], await timeline.NearestAsync(54, 0, 100, a, Token));
        Assert.Same(events[3], await timeline.NearestAsync(56, 0, 100, a, Token));
        Assert.Same(events[2], await timeline.NearestAsync(55, 0, 100, a, Token));
        Assert.Same(events[3], await timeline.NearestAsync(190, 0, 100, a, Token));
        Assert.Same(events[2], await timeline.NearestAsync(5, 40, 100, a, Token));
        Assert.Null(await timeline.NearestAsync(50, 46, 100, new HashSet<string> { "b" }, Token));
        Assert.Null(await timeline.NearestAsync(50, 0, 100, new HashSet<string> { "c" }, Token));
        Assert.Same(events[1], await timeline.NearestAsync(44, 0, 100, All, Token));
    }

    [Fact]
    public async Task PrefersTheFirstOfEventsWithTheSameTime()
    {
        var events = new[] { Event("a", 10, 1), Event("a", 10, 2), Event("a", 30, 3), Event("a", 30, 4) };
        var timeline = Build(events);

        Assert.Same(events[0], await timeline.NearestAsync(12, 0, 100, All, Token));
        Assert.Same(events[2], await timeline.NearestAsync(28, 0, 100, All, Token));
        Assert.Same(events[0], await timeline.NearestAsync(20, 0, 100, All, Token));
    }

    [Fact]
    public void ReportsEachOccupiedColumnOnce()
    {
        var timeline = Build(
        [
            Event("a", 0, 1), Event("a", 5, 2), Event("a", 9, 3),
            Event("a", 35, 4), Event("a", 99, 5), Event("a", 100, 6), Event("a", 150, 7)
        ]);

        Assert.Equal(new[] { 0, 3, 9 }, timeline.Occupancy.OccupiedColumns(["a"], 0, 100, 10));
        Assert.Equal(new[] { 1 }, timeline.Occupancy.OccupiedColumns(["a"], 30, 20, 4));
        Assert.Empty(timeline.Occupancy.OccupiedColumns(["b"], 0, 100, 10));
    }

    [Fact]
    public void KeepsTheOccupancyGridBoundedForLongRecordings()
    {
        const long hour = 3_600_000_000_000;
        var events = Enumerable.Range(0, 10_000)
            .Select(index => Event("a", index * (hour / 10_000), index))
            .ToArray();
        var occupancy = TimelineOccupancy.FromEvents(events, hour);

        Assert.True(hour / occupancy.BucketWidth <= TimelineOccupancy.BucketCount);

        // At 32 times zoom, on a timeline 4,096 columns wide, a bucket is no
        // wider than a column.
        Assert.True(occupancy.BucketWidth <= hour / 32 / 4_096);
        Assert.Equal(
            Enumerable.Range(0, 1_000).ToArray(),
            occupancy.OccupiedColumns(["a"], 0, hour, 1_000));
    }

    [Fact]
    public async Task MatchesADirectScanOfEveryEvent()
    {
        var random = new Random(1234);
        var time = 0L;
        string[] lanes = ["a", "b", "c"];
        var events = Enumerable.Range(0, 5_000)
            .Select(index => Event(lanes[random.Next(lanes.Length)], time += random.Next(0, 50), index))
            .ToArray();
        var timeline = Build(events);
        const long start = 20_000;
        const long duration = 60_000;
        const int width = 333;

        var expected = events
            .Where(item => item.Channel == "b" &&
                item.MonotonicNanoseconds >= start &&
                item.MonotonicNanoseconds <= start + duration)
            .Select(item => (int)Math.Floor(
                (item.MonotonicNanoseconds - start) / (double)duration * width))
            .Select(column => Math.Min(column, width - 1))
            .Distinct()
            .ToArray();
        Assert.Equal(expected, timeline.Occupancy.OccupiedColumns(["b"], start, duration, width));

        var c = new HashSet<string> { "c" };
        for (var trial = 0; trial < 200; trial++)
        {
            var timestamp = random.NextInt64(start, start + duration);
            var direct = events
                .Where(item => item.Channel == "c" &&
                    item.MonotonicNanoseconds >= start &&
                    item.MonotonicNanoseconds <= start + duration)
                .MinBy(item => Math.Abs(item.MonotonicNanoseconds - timestamp));
            Assert.Same(direct, await timeline.NearestAsync(timestamp, start, start + duration, c, Token));
            Assert.Same(
                events.LastOrDefault(item => item.Channel == "c" && item.MonotonicNanoseconds <= timestamp),
                await timeline.AtOrBeforeAsync(timestamp, c, Token));
        }
    }

    private static InMemorySessionTimeline Build(IReadOnlyList<SessionTimelineEvent> events) =>
        new(events, events.Count == 0 ? 0 : events[^1].MonotonicNanoseconds);

    private static SessionTimelineEvent Event(string channel, long time, long key) =>
        new(key, Guid.NewGuid().ToString("N"), "observed", channel, "test", time, "");
}
