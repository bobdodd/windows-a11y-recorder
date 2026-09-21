using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class CollectorClosingTimestampTests
{
    [Fact]
    public void UsesTheClockWhenItHasAdvancedPastTheStopBoundary()
    {
        var clock = new StubClock(23_439_981_600);
        var boundary = new SessionBoundary(
            23_439_242_300,
            DateTimeOffset.UnixEpoch);

        var resolved = CollectorClosingTimestamp.Resolve(clock, boundary);

        Assert.Equal(23_439_981_600, resolved);
    }

    [Fact]
    public void NeverPrecedesEvidenceEmittedBeforeTheDrainCompleted()
    {
        // The stop boundary is captured before a collector drains its queued
        // observations, so the last drained record can carry a later timestamp
        // than the boundary. A closing record stamped with the boundary would
        // regress monotonic order within the collector's channel.
        var lastDrainedRecord = 23_439_981_600;
        var clock = new StubClock(lastDrainedRecord + 1_000);
        var boundary = new SessionBoundary(
            23_439_242_300,
            DateTimeOffset.UnixEpoch);

        var resolved = CollectorClosingTimestamp.Resolve(clock, boundary);

        Assert.True(resolved >= lastDrainedRecord);
        Assert.True(resolved >= boundary.MonotonicNanoseconds);
    }

    [Fact]
    public void KeepsTheStopBoundaryAsAFloor()
    {
        var clock = new StubClock(5);
        var boundary = new SessionBoundary(11, DateTimeOffset.UnixEpoch);

        var resolved = CollectorClosingTimestamp.Resolve(clock, boundary);

        Assert.Equal(11, resolved);
    }

    [Fact]
    public void FallsBackToTheStopBoundaryWithoutAClock()
    {
        var boundary = new SessionBoundary(17, DateTimeOffset.UnixEpoch);

        var resolved = CollectorClosingTimestamp.Resolve(null, boundary);

        Assert.Equal(17, resolved);
    }

    private sealed class StubClock(long elapsedNanoseconds) : ISessionClock
    {
        public long Frequency => 10_000_000;

        public long OriginTimestamp => 0;

        public DateTimeOffset OriginUtc => DateTimeOffset.UnixEpoch;

        public long GetTimestamp() => elapsedNanoseconds / 100;

        public long GetElapsedNanoseconds() => elapsedNanoseconds;
    }
}
