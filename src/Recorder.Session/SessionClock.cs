using System.Diagnostics;
using Recorder.Contracts;

namespace Recorder.Session;

public sealed class SessionClock : ISessionClock
{
    public SessionClock()
    {
        OriginTimestamp = Stopwatch.GetTimestamp();
        OriginUtc = DateTimeOffset.UtcNow;
    }

    public long Frequency => Stopwatch.Frequency;
    public long OriginTimestamp { get; }
    public DateTimeOffset OriginUtc { get; }

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public long GetElapsedNanoseconds()
    {
        var elapsed = Stopwatch.GetTimestamp() - OriginTimestamp;
        return (long)(elapsed * (1_000_000_000d / Stopwatch.Frequency));
    }
}
