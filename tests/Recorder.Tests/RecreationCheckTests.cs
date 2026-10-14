using Recorder.Analysis.Summary;

namespace Recorder.Tests;

// The check of the recreation without a window, before step 4b
// (docs/architecture/recording-summary.md, decision 7).
public sealed class RecreationCheckTests
{
    [Fact]
    public void ATimeIsReadAsThePlayerShowsIt()
    {
        Assert.True(RecreationCheck.TryParseTime("00:00:48.950", out var time));
        Assert.Equal(48_950_000_000, time);
        Assert.True(RecreationCheck.TryParseTime("00:01:02", out time));
        Assert.Equal(62_000_000_000, time);
        Assert.False(RecreationCheck.TryParseTime("48.950", out _));
    }

    // A frame shown at 00:00:48.950 can be up to 999,999 ns after it, as a
    // time is shown cut short to the millisecond; it is the frame shown at
    // that time, and the frame shown at 00:00:48.951 is not.
    [Fact]
    public void TheFrameShownAtATimeIsWithinItsMillisecond()
    {
        Assert.True(RecreationCheck.TryParseTime("00:00:48.950", out var time));
        Assert.Equal("00:00:48.950", RecreationCheck.FormatTime(RecreationCheck.LastShownAs(time)));
        Assert.Equal("00:00:48.951", RecreationCheck.FormatTime(RecreationCheck.LastShownAs(time) + 1));
    }
}
