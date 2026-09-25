namespace Recorder.Contracts;

// Windows reports compositor times, such as
// Direct3D11CaptureFrame.SystemRelativeTime, as a TimeSpan whose value is the
// QueryPerformanceCounter reading in 100 ns units. The session clock is the
// same counter measured from the session origin, so the conversion is exact up
// to the rounding Windows applied when it scaled the counter to TimeSpan ticks.
public static class CompositionClock
{
    private const long NanosecondsPerTimeSpanTick = 100;
    private const long NanosecondsPerSecond = 1_000_000_000;

    public static long SystemRelativeTimeToSessionNanoseconds(
        long systemRelativeTimeTicks,
        long originTimestamp,
        long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        var composed = (Int128)systemRelativeTimeTicks * NanosecondsPerTimeSpanTick;
        var origin = (Int128)originTimestamp * NanosecondsPerSecond / frequency;
        return checked((long)(composed - origin));
    }
}
