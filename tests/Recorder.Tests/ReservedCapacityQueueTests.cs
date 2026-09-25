using Recorder.Contracts;
using Xunit;

namespace Recorder.Tests;

public sealed class ReservedCapacityQueueTests
{
    [Fact]
    public void AdmitsOrdinaryItemsOnlyUpToTheUnreservedCapacity()
    {
        var queue = CreateQueue(capacity: 4, reserved: 2);

        Assert.True(Enqueue(queue, "property-changed", reserved: false));
        Assert.True(Enqueue(queue, "property-changed", reserved: false));
        Assert.False(Enqueue(queue, "property-changed", reserved: false));
        Assert.Equal(1, queue.TotalDropped);
    }

    [Fact]
    public void AdmitsReservedItemsIntoTheReserveAfterOrdinaryItemsAreRefused()
    {
        var queue = CreateQueue(capacity: 4, reserved: 2);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);

        Assert.True(Enqueue(queue, "focus-changed", reserved: true));
        Assert.True(Enqueue(queue, "focus-changed", reserved: true));
        Assert.False(Enqueue(queue, "focus-changed", reserved: true));
    }

    [Fact]
    public void WritesADropEpisodeBeforeTheNextAdmittedItem()
    {
        var clock = new StepClock();
        var queue = CreateQueue(capacity: 6, reserved: 2, clock);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "structure-changed", reserved: false);

        Assert.True(Enqueue(queue, "focus-changed", reserved: true));
        var items = Drain(queue);

        Assert.Equal(6, items.Count);
        var marker = Assert.IsType<Item>(items[4]);
        Assert.NotNull(marker.Episode);
        Assert.Equal(50, marker.Episode.FirstDroppedAt);
        Assert.Equal(60, marker.Episode.LastDroppedAt);
        Assert.Equal(2, marker.Episode.Count);
        Assert.Equal(1, marker.Episode.CountsByKind["property-changed"]);
        Assert.Equal(1, marker.Episode.CountsByKind["structure-changed"]);
        Assert.Equal("focus-changed", items[5].Kind);
        Assert.Equal(70, items[5].At);
    }

    [Fact]
    public void KeepsTheEpisodeOpenWhenThereIsNoRoomForItsMarker()
    {
        var queue = CreateQueue(capacity: 3, reserved: 1);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);

        // One reserved slot is free, but the marker and the item need two.
        Assert.False(Enqueue(queue, "focus-changed", reserved: true));

        Assert.Equal(2, queue.TotalDropped);
    }

    [Fact]
    public void StartsANewEpisodeAfterTheMarkerIsWritten()
    {
        var queue = CreateQueue(capacity: 6, reserved: 2);
        for (var index = 0; index < 5; index++)
        {
            Enqueue(queue, "property-changed", reserved: false);
        }

        Enqueue(queue, "focus-changed", reserved: true);
        queue.Reader.TryRead(out _);
        queue.Reader.TryRead(out _);
        queue.Reader.TryRead(out _);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);
        var items = Drain(queue);

        var markers = items.Where(item => item.Episode is not null).ToList();
        Assert.Single(markers);
        Assert.Equal(1, markers[0].Episode!.Count);
    }

    [Fact]
    public async Task CompletionWritesTheOpenEpisodeAndStopsAdmission()
    {
        var queue = CreateQueue(capacity: 3, reserved: 1);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);

        var unwritten = await queue.CompleteAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Null(unwritten);
        Assert.False(Enqueue(queue, "focus-changed", reserved: true));
        var items = Drain(queue);
        Assert.Equal(3, items.Count);
        Assert.Equal(1, items[2].Episode!.Count);
        Assert.True(queue.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task CompletionReturnsTheEpisodeWhenTheQueueStaysFull()
    {
        var queue = CreateQueue(capacity: 2, reserved: 0);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);
        Enqueue(queue, "property-changed", reserved: false);

        var unwritten = await queue.CompleteAsync(
            TimeSpan.FromMilliseconds(50),
            TestContext.Current.CancellationToken);

        Assert.NotNull(unwritten);
        Assert.Equal(1, unwritten.Count);
        Assert.Equal(2, Drain(queue).Count);
    }

    [Fact]
    public async Task ConcurrentProducersYieldNonDecreasingTimesAndAccountForEveryArrival()
    {
        var clock = new StepClock();
        var queue = CreateQueue(capacity: 64, reserved: 16, clock);
        var admitted = 0;
        var reader = Task.Run(async () =>
        {
            var items = new List<Item>();
            await foreach (var item in queue.Reader.ReadAllAsync())
            {
                items.Add(item);
            }

            return items;
        });

        Parallel.For(0, 5_000, index =>
        {
            if (Enqueue(queue, index % 10 == 0 ? "focus-changed" : "property-changed", index % 10 == 0))
            {
                Interlocked.Increment(ref admitted);
            }
        });
        await queue.CompleteAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var items = await reader;

        var times = items.Select(item => item.At).ToList();
        Assert.Equal(times.OrderBy(time => time), times);
        var dropped = items.Where(item => item.Episode is not null).Sum(item => item.Episode!.Count);
        Assert.Equal(queue.TotalDropped, dropped);
        Assert.Equal(5_000, admitted + dropped);
    }

    private static ReservedCapacityQueue<Item> CreateQueue(
        int capacity,
        int reserved,
        StepClock? clock = null)
    {
        var source = clock ?? new StepClock();
        return new ReservedCapacityQueue<Item>(
            capacity,
            reserved,
            source.Next,
            episode => new Item("marker", episode.LastDroppedAt, episode));
    }

    private static bool Enqueue(ReservedCapacityQueue<Item> queue, string kind, bool reserved) =>
        queue.TryEnqueue(kind, reserved, at => new Item(kind, at, null));

    private static List<Item> Drain(ReservedCapacityQueue<Item> queue)
    {
        var items = new List<Item>();
        while (queue.Reader.TryRead(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    private sealed record Item(string Kind, long At, DropEpisode? Episode);

    // Advances by 10 on every read, so arrival times are distinct and ordered
    // by the order in which the queue read them.
    private sealed class StepClock
    {
        private long _now;

        public long Next() => Interlocked.Add(ref _now, 10);
    }
}
