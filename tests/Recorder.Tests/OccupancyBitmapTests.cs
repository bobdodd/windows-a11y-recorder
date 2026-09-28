using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The occupancy bitmap kept while recording gives the same grid as the
/// events, at every duration it grows to, and survives its export.
/// </summary>
public sealed class OccupancyBitmapTests
{
    [Fact]
    public void GivesTheGridTheEventsGiveAsTheRecordingGrows()
    {
        var random = new Random(97);
        var bitmap = new OccupancyBitmap();
        var events = new List<SessionTimelineEvent>();
        var time = 0L;
        string[] channels = ["a", "b", "c"];
        for (var index = 0; index < 20_000; index++)
        {
            // Gaps from nanoseconds to minutes, and events up to a second
            // earlier than the latest, so the bitmap halves many times.
            time += random.Next(4) switch
            {
                0 => random.Next(0, 10),
                1 => random.Next(0, 1_000_000),
                2 => random.NextInt64(0, 1_000_000_000),
                _ => random.NextInt64(0, 60_000_000_000)
            };
            var at = Math.Max(0, time - (random.Next(10) == 0 ? random.NextInt64(0, 1_000_000_000) : 0));
            var channel = channels[random.Next(channels.Length)];
            bitmap.Add(channel, at);
            events.Add(new SessionTimelineEvent(index, $"e{index}", "observed", channel, "test", at, ""));
            if (index % 997 == 0 || index == 19_999)
            {
                var duration = Math.Max(bitmap.LatestTime, events.Max(item => item.MonotonicNanoseconds)) + random.Next(0, 1_000);
                AssertSame(TimelineOccupancy.FromEvents(events, duration), bitmap.ToOccupancy(duration), channels, duration);
                var imported = OccupancyBitmap.Import(bitmap.Export());
                AssertSame(bitmap.ToOccupancy(duration), imported.ToOccupancy(duration), channels, duration);
            }
        }
    }

    private static void AssertSame(TimelineOccupancy expected, TimelineOccupancy actual, string[] channels, long duration)
    {
        Assert.Equal(expected.BucketWidth, actual.BucketWidth);
        foreach (var subset in new[] { channels, ["a"], ["b", "c"] })
        {
            var set = subset.ToHashSet();
            foreach (var columns in new[] { 1, 640, 4_001 })
            {
                Assert.Equal(
                    expected.OccupiedColumns(set, 0, duration + 1, columns),
                    actual.OccupiedColumns(set, 0, duration + 1, columns));
            }
        }
    }
}
