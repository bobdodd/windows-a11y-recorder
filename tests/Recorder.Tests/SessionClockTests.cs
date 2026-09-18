using Recorder.Session;

namespace Recorder.Tests;

public sealed class SessionClockTests
{
    [Fact]
    public void ElapsedTimeIsMonotonic()
    {
        var clock = new SessionClock();
        var previous = clock.GetElapsedNanoseconds();

        for (var index = 0; index < 10_000; index++)
        {
            var current = clock.GetElapsedNanoseconds();
            Assert.True(current >= previous);
            previous = current;
        }
    }
}
