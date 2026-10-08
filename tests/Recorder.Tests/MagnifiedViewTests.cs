using System.Text.Json;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class MagnifiedViewTests
{
    private static SessionVideoFrame Frame(
        FullscreenMagnification? magnification,
        int x = 0,
        int y = 0,
        int width = 1920,
        int height = 1080) =>
        new(0, "frames/desktop/0000000001.png", "C:/frame.png", width, height, x, y, magnification);

    [Fact]
    public void FrameWithoutReadingShowsTheWholeFrame()
    {
        var frame = Frame(null);

        Assert.False(MagnifiedView.IsMagnified(frame));
        Assert.Null(MagnifiedView.VisibleRegion(frame));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.0000001)]
    [InlineData(0.5)]
    public void LevelOfAtMostOneShowsTheWholeFrame(double level)
    {
        var frame = Frame(new FullscreenMagnification(level, 480, 270));

        Assert.False(MagnifiedView.IsMagnified(frame));
        Assert.Null(MagnifiedView.VisibleRegion(frame));
    }

    [Fact]
    public void ValidatedReadingGivesTheTopLeftQuarter()
    {
        // Validation 2026-10-07: level 2, offset 3, 2 on a 1920 by 1080
        // screen showed the top-left quarter.
        var region = MagnifiedView.VisibleRegion(Frame(new FullscreenMagnification(2, 3, 2)));

        Assert.Equal(new FrameRegion(3, 2, 960, 540), region);
    }

    [Fact]
    public void OffsetAtTheScreensFarEdgeGivesTheBottomRightPart()
    {
        var region = MagnifiedView.VisibleRegion(Frame(new FullscreenMagnification(4, 1440, 810)));

        Assert.Equal(new FrameRegion(1440, 810, 480, 270), region);
        var value = region!.Value;
        Assert.Equal(1920, value.X + value.Width);
        Assert.Equal(1080, value.Y + value.Height);
    }

    [Fact]
    public void FractionalLevelGivesAFractionalPart()
    {
        var region = MagnifiedView.VisibleRegion(Frame(new FullscreenMagnification(1.5, 0, 0)));

        Assert.Equal(new FrameRegion(0, 0, 1280, 720), region);
    }

    [Fact]
    public void MonitorLeftOfThePrimaryShiftsThePartByTheVirtualScreensCorner()
    {
        // Two 1920 by 1080 monitors, one left of the primary: the virtual
        // screen starts at -1920. An offset of -960 at level 2 puts the
        // virtual screen's left edge at the view's left edge.
        var frame = Frame(new FullscreenMagnification(2, -960, 0), x: -1920, width: 3840);

        Assert.Equal(new FrameRegion(0, 0, 1920, 540), MagnifiedView.VisibleRegion(frame));
    }

    [Fact]
    public void AnyMagnifiedFindsALevelAboveOne()
    {
        Assert.False(MagnifiedView.AnyMagnified([Frame(null), Frame(new FullscreenMagnification(1, 0, 0))]));
        Assert.True(MagnifiedView.AnyMagnified([Frame(null), Frame(new FullscreenMagnification(2, 0, 0))]));
    }

    private static List<EventValidationIssue> Validate(string magnification)
    {
        var payload =
            "{\"path\":\"frames/desktop/0000000001.png\",\"x\":0,\"y\":0,\"width\":1920,\"height\":1080," +
            "\"stride\":7680,\"pixelFormat\":\"B8G8R8A8\",\"encodedFormat\":\"png\",\"byteLength\":5," +
            "\"captureDurationNanoseconds\":1,\"framesPerSecond\":5,\"backend\":\"windows-graphics-capture\"," +
            "\"monitorCount\":1,\"fallbackReason\":null,\"gdiFallbackFrameCount\":0," +
            "\"fullscreenMagnification\":" + magnification + "}";
        using var document = JsonDocument.Parse(payload);
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate("graphics.desktop.frames", "desktop-frame", document.RootElement, 2000, issues);
        return issues;
    }

    [Theory]
    [InlineData("{\"level\":2,\"x\":3,\"y\":2,\"problem\":null}")]
    [InlineData("{\"level\":1,\"x\":0,\"y\":0,\"problem\":null}")]
    [InlineData("{\"level\":null,\"x\":null,\"y\":null,\"problem\":\"MagInitialize failed with error 5.\"}")]
    public void ValidReadingsAreAccepted(string magnification)
    {
        Assert.Empty(Validate(magnification));
    }

    [Theory]
    [InlineData("{\"level\":2,\"x\":3,\"y\":2,\"problem\":\"failed\"}")]
    [InlineData("{\"level\":2,\"x\":null,\"y\":2,\"problem\":null}")]
    [InlineData("{\"level\":null,\"x\":null,\"y\":null,\"problem\":null}")]
    [InlineData("{\"level\":0,\"x\":0,\"y\":0,\"problem\":null}")]
    [InlineData("{\"level\":2,\"x\":3,\"y\":2}")]
    [InlineData("{\"level\":2,\"x\":3.5,\"y\":2,\"problem\":null}")]
    [InlineData("{\"level\":2,\"x\":3,\"y\":2,\"problem\":null,\"extra\":1}")]
    [InlineData("null")]
    public void InvalidReadingsAreRejected(string magnification)
    {
        Assert.NotEmpty(Validate(magnification));
    }
}
