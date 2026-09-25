using Recorder.Contracts;
using Xunit;

namespace Recorder.Tests;

public sealed class NewestArrivalSlotTests
{
    [Fact]
    public void TakeReturnsNothingBeforeAnyArrival()
    {
        using var slot = new NewestArrivalSlot<Tracked>();

        Assert.Null(slot.Take());
    }

    [Fact]
    public void TakeReturnsTheOnlyArrivalWithItsTimeAndNoReleases()
    {
        using var slot = new NewestArrivalSlot<Tracked>();
        var frame = new Tracked();

        Assert.True(slot.Offer(frame, 100));
        var taken = slot.Take();

        Assert.NotNull(taken);
        Assert.Same(frame, taken.Item);
        Assert.Equal(100, taken.ArrivedAt);
        Assert.Equal(0, taken.ReleasedBeforeTake);
        Assert.False(frame.Disposed);
    }

    [Fact]
    public void KeepsTheNewestArrivalAndReleasesEachOneItReplaces()
    {
        using var slot = new NewestArrivalSlot<Tracked>();
        var first = new Tracked();
        var second = new Tracked();
        var third = new Tracked();

        slot.Offer(first, 100);
        slot.Offer(second, 200);
        slot.Offer(third, 300);
        var taken = slot.Take();

        Assert.NotNull(taken);
        Assert.Same(third, taken.Item);
        Assert.Equal(300, taken.ArrivedAt);
        Assert.Equal(2, taken.ReleasedBeforeTake);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
        Assert.False(third.Disposed);
    }

    [Fact]
    public void TakeEmptiesTheSlotAndRestartsTheReleaseCount()
    {
        using var slot = new NewestArrivalSlot<Tracked>();
        slot.Offer(new Tracked(), 100);
        slot.Offer(new Tracked(), 200);
        slot.Take();

        Assert.Null(slot.Take());

        var next = new Tracked();
        slot.Offer(next, 300);
        var taken = slot.Take();

        Assert.NotNull(taken);
        Assert.Same(next, taken.Item);
        Assert.Equal(0, taken.ReleasedBeforeTake);
    }

    [Fact]
    public void DisposeReleasesTheHeldArrivalAndRejectsLaterOnes()
    {
        var slot = new NewestArrivalSlot<Tracked>();
        var held = new Tracked();
        var late = new Tracked();
        slot.Offer(held, 100);

        slot.Dispose();

        Assert.True(held.Disposed);
        Assert.False(slot.Offer(late, 200));
        Assert.True(late.Disposed);
        Assert.Null(slot.Take());
    }

    [Fact]
    public void ConcurrentArrivalsLeaveExactlyOneHeldAndReleaseTheRest()
    {
        using var slot = new NewestArrivalSlot<Tracked>();
        var frames = Enumerable.Range(0, 2000).Select(_ => new Tracked()).ToArray();

        Parallel.For(0, frames.Length, index => slot.Offer(frames[index], index));
        var taken = slot.Take();

        Assert.NotNull(taken);
        Assert.Equal(frames.Length - 1, taken.ReleasedBeforeTake);
        Assert.Single(frames, frame => !frame.Disposed);
        Assert.False(taken.Item.Disposed);
    }

    private sealed class Tracked : IDisposable
    {
        private int _disposeCount;

        public bool Disposed => Volatile.Read(ref _disposeCount) > 0;

        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposeCount) > 1)
            {
                throw new InvalidOperationException("Released twice.");
            }
        }
    }
}
