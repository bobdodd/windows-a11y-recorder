namespace Recorder.Collectors.Browser;

public sealed class BrowserClockMapper
{
    private readonly long _browserAnchorTicks;
    private readonly long _recorderAnchorNanoseconds;
    private readonly long _browserFrequency;

    private BrowserClockMapper(
        long browserAnchorTicks,
        long recorderAnchorNanoseconds,
        long browserFrequency,
        long uncertaintyNanoseconds)
    {
        _browserAnchorTicks = browserAnchorTicks;
        _recorderAnchorNanoseconds = recorderAnchorNanoseconds;
        _browserFrequency = browserFrequency;
        UncertaintyNanoseconds = uncertaintyNanoseconds;
    }

    public long UncertaintyNanoseconds { get; }

    public static BrowserClockMapper Create(
        long recorderSendNanoseconds,
        long browserReceiveTicks,
        long browserSendTicks,
        long recorderReceiveNanoseconds,
        long browserFrequency)
    {
        if (browserFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(browserFrequency),
                "Browser clock frequency must be positive.");
        }
        if (recorderReceiveNanoseconds < recorderSendNanoseconds)
        {
            throw new ArgumentException(
                "Recorder receive time cannot precede send time.");
        }
        if (browserSendTicks < browserReceiveTicks)
        {
            throw new ArgumentException(
                "Browser send time cannot precede receive time.");
        }

        var recorderMidpoint = recorderSendNanoseconds +
            ((recorderReceiveNanoseconds - recorderSendNanoseconds) / 2);
        var browserMidpoint = browserReceiveTicks +
            ((browserSendTicks - browserReceiveTicks) / 2);
        var roundTripNanoseconds =
            recorderReceiveNanoseconds - recorderSendNanoseconds;
        var browserProcessingNanoseconds = ScaleTicks(
            browserSendTicks - browserReceiveTicks,
            browserFrequency);
        var networkRoundTripNanoseconds = Math.Max(
            0,
            roundTripNanoseconds - browserProcessingNanoseconds);

        return new BrowserClockMapper(
            browserMidpoint,
            recorderMidpoint,
            browserFrequency,
            networkRoundTripNanoseconds / 2);
    }

    public long MapToSessionNanoseconds(long browserTicks)
    {
        var delta = ScaleTicks(
            browserTicks - _browserAnchorTicks,
            _browserFrequency);
        return checked(_recorderAnchorNanoseconds + delta);
    }

    private static long ScaleTicks(long ticks, long frequency)
    {
        var nanoseconds = (decimal)ticks * 1_000_000_000m / frequency;
        return checked((long)decimal.Round(
            nanoseconds,
            0,
            MidpointRounding.ToEven));
    }
}
