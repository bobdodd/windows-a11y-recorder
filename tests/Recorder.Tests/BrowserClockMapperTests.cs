using Recorder.Collectors.Browser;

namespace Recorder.Tests;

public sealed class BrowserClockMapperTests
{
    [Fact]
    public void MapsBrowserTicksUsingRoundTripMidpoints()
    {
        var mapper = BrowserClockMapper.Create(
            recorderSendNanoseconds: 1_000_000,
            browserReceiveTicks: 10_000,
            browserSendTicks: 10_200,
            recorderReceiveNanoseconds: 1_600_000,
            browserFrequency: 1_000_000);

        Assert.Equal(1_700_000, mapper.MapToSessionNanoseconds(10_500));
        Assert.Equal(200_000, mapper.UncertaintyNanoseconds);
    }

    [Fact]
    public void RejectsNonPositiveBrowserFrequency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrowserClockMapper.Create(0, 0, 0, 100, 0));
    }

    [Fact]
    public void RejectsReversedClockSamples()
    {
        Assert.Throws<ArgumentException>(() =>
            BrowserClockMapper.Create(200, 0, 0, 100, 1_000));
        Assert.Throws<ArgumentException>(() =>
            BrowserClockMapper.Create(0, 200, 100, 300, 1_000));
    }
}
