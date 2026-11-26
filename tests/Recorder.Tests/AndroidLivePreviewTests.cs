using Recorder.Collectors.Android;

namespace Recorder.Tests;

public sealed class AndroidLivePreviewTests
{
    [Fact]
    public void Preview_KeepsOnlyNewestFrame_WithoutConsumingIt()
    {
        var preview = new AndroidLivePreview();
        preview.BeginSession(1);
        var rgb = new byte[] { 20, 40, 60 };
        preview.Publish(1, 1, 1, 1, [1, 2, 3]);
        preview.Publish(1, 3, 1, 1, rgb);
        var current = preview.Current;
        preview.Publish(1, 2, 1, 1, [10, 20, 30]);
        preview.Publish(1, 3, 1, 1, [11, 21, 31]);

        Assert.Same(current, preview.Current);
        Assert.Equal(AndroidLivePreviewState.ShowingFrames, current.State);
        var frame = Assert.IsType<AndroidLivePreviewFrame>(current.Frame);
        Assert.Equal(3UL, frame.Sequence);
        Assert.True(frame.Rgb.Equals(rgb.AsMemory()), "The preview holds the existing RGB buffer rather than copying it.");
    }

    [Fact]
    public void Preview_NewSessionClearsTheImage_AndRejectsOldWorkers()
    {
        var preview = new AndroidLivePreview();
        preview.BeginSession(1);
        preview.Publish(1, 1, 1, 2, new byte[6]);
        preview.BeginSession(2);
        preview.Publish(1, 2, 1, 2, new byte[6]);
        preview.BeginSession(1);
        preview.Publish(3, 4, 2, 1, new byte[6]);

        Assert.Equal(new AndroidLivePreviewSnapshot(AndroidLivePreviewState.WaitingForFrame, 2, null), preview.Current);

        preview.Publish(2, 3, 2, 1, new byte[6]);
        preview.Publish(1, 4, 1, 2, new byte[6]);
        var frame = Assert.IsType<AndroidLivePreviewFrame>(preview.Current.Frame);
        Assert.Equal((3UL, 2, 1), (frame.Sequence, frame.Width, frame.Height));
    }

    [Theory]
    [InlineData(AndroidLivePreviewState.FramesUnavailable)]
    [InlineData(AndroidLivePreviewState.StreamUnavailable)]
    [InlineData(AndroidLivePreviewState.Stopped)]
    public void Preview_ClearsAndRejectsLateFramesOnTermination(AndroidLivePreviewState state)
    {
        var preview = new AndroidLivePreview();
        preview.BeginSession(1);
        preview.Publish(1, 1, 1, 1, [1, 2, 3]);
        switch (state)
        {
            case AndroidLivePreviewState.FramesUnavailable:
                preview.FramesUnavailable();
                break;
            case AndroidLivePreviewState.StreamUnavailable:
                preview.StreamUnavailable();
                preview.FramesUnavailable();
                break;
            default:
                preview.Stop();
                preview.StreamUnavailable();
                preview.FramesUnavailable();
                break;
        }

        preview.BeginSession(2);
        preview.Publish(1, 2, 1, 1, [1, 2, 3]);
        preview.Publish(2, 3, 1, 1, [1, 2, 3]);

        Assert.Equal(new AndroidLivePreviewSnapshot(state, 1, null), preview.Current);
    }

    [Fact]
    public void Preview_LostStreamSupersedesEarlierFrameFailure()
    {
        var preview = new AndroidLivePreview();
        preview.FramesUnavailable();
        preview.StreamUnavailable();
        Assert.Equal(AndroidLivePreviewState.StreamUnavailable, preview.Current.State);
        preview.Stop();
        Assert.Equal(AndroidLivePreviewState.Stopped, preview.Current.State);
    }
}
