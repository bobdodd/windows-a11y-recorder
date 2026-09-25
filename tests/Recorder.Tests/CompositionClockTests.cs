using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class CompositionClockTests
{
    [Fact]
    public void ConvertsSystemRelativeTimeAtTheSessionOrigin()
    {
        // 10 MHz counter: one counter tick is one TimeSpan tick.
        Assert.Equal(
            0,
            CompositionClock.SystemRelativeTimeToSessionNanoseconds(
                systemRelativeTimeTicks: 123_456_789,
                originTimestamp: 123_456_789,
                frequency: 10_000_000));
    }

    [Fact]
    public void ConvertsSystemRelativeTimeAfterTheOrigin()
    {
        Assert.Equal(
            16_700_000,
            CompositionClock.SystemRelativeTimeToSessionNanoseconds(
                systemRelativeTimeTicks: 1_000_167_000,
                originTimestamp: 1_000_000_000,
                frequency: 10_000_000));
    }

    [Fact]
    public void ConvertsAFrameComposedBeforeTheOriginToANegativeTime()
    {
        Assert.Equal(
            -5_000_000,
            CompositionClock.SystemRelativeTimeToSessionNanoseconds(
                systemRelativeTimeTicks: 999_950_000,
                originTimestamp: 1_000_000_000,
                frequency: 10_000_000));
    }

    [Fact]
    public void ConvertsWithACounterFrequencyOtherThanTenMegahertz()
    {
        // 3 MHz counter at 4 hours of uptime; the origin is 43,200,000,000
        // counter ticks, which is 14,400 s, and the frame is 1 ms later.
        var originTimestamp = 43_200_000_000L;
        var frameTimeSpanTicks = 144_000_010_000L;

        Assert.Equal(
            1_000_000,
            CompositionClock.SystemRelativeTimeToSessionNanoseconds(
                frameTimeSpanTicks,
                originTimestamp,
                frequency: 3_000_000));
    }

    [Fact]
    public void RejectsANonPositiveFrequency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CompositionClock.SystemRelativeTimeToSessionNanoseconds(1, 1, 0));
    }
}
