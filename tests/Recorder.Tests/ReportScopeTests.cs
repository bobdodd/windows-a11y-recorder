using Recorder.Analysis;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

// The setting "Limit the report to the browser while it is in front"
// (owner, 2026-10-18; report-fixes-2026-10-18.md, part 4).
public sealed class ReportScopeTests
{
    private const long Second = 1_000_000_000;

    private static ForegroundFact Window(long at, string process, int x, int y, int width, int height, bool nvda = false, bool minimized = false) =>
        new(at * Second, $"fg-{at}", process, process) { Bounds = new ScreenRect(x, y, width, height), Nvda = nvda, Minimized = minimized };

    private static CapturedFrame Frame(long at, int width = 1920, int height = 1080, double? magnification = null, int x = 0, int y = 0, int magnifiedX = 0, int magnifiedY = 0) =>
        new(at * Second, "frames/desktop/1.png", "/x/1.png", width, height, magnification) { X = x, Y = y, MagnifiedX = magnifiedX, MagnifiedY = magnifiedY };

    [Fact]
    public void With_the_browser_in_front_the_frame_is_cut_to_its_window()
    {
        var scope = ReportScope.At([Window(1, "chrome", 100, 50, 1200, 900)], "chrome", Frame(2));
        Assert.True(scope.Limited);
        Assert.Equal(new ScreenRect(100, 50, 1200, 900), scope.Crop);
    }

    [Fact]
    public void An_NVDA_window_in_front_over_the_browser_is_kept_with_it()
    {
        var scope = ReportScope.At([Window(1, "chrome", 0, 0, 1000, 800), Window(2, "nvda", 1200, 100, 400, 300, nvda: true)], "chrome", Frame(3));
        Assert.True(scope.Limited);
        Assert.Equal([new ScreenRect(0, 0, 1000, 800), new ScreenRect(1200, 100, 400, 300)], scope.Windows);
        Assert.Equal(new ScreenRect(0, 0, 1600, 800), scope.Crop);
    }

    [Fact]
    public void With_another_program_in_front_the_whole_frame_is_described()
    {
        var scope = ReportScope.At([Window(1, "chrome", 0, 0, 1000, 800), Window(2, "notepad", 0, 0, 800, 600)], "chrome", Frame(3));
        Assert.Equal((false, "notepad was in front, not the browser"), (scope.Limited, scope.Why));
    }

    [Fact]
    public void With_the_recorder_in_front_the_whole_frame_is_described()
    {
        // Owner, 2026-10-18: "The recorder is a window like any other:
        // ignored if the browser window has focus, otherwise reported on".
        var scope = ReportScope.At([Window(1, "chrome", 0, 0, 1000, 800), Window(2, "Recorder.App", 100, 100, 900, 700)], "chrome", Frame(3));
        Assert.Equal((false, "Recorder.App was in front, not the browser"), (scope.Limited, scope.Why));
        Assert.True(ReportScope.At([Window(1, "Recorder.App", 100, 100, 900, 700), Window(2, "chrome", 0, 0, 1000, 800)], "chrome", Frame(3)).Limited);
    }

    [Fact]
    public void A_browser_minimized_or_without_bounds_gives_the_whole_frame()
    {
        Assert.False(ReportScope.At([Window(1, "chrome", 0, 0, 1000, 800, minimized: true)], "chrome", Frame(2)).Limited);
        Assert.False(ReportScope.At([new ForegroundFact(Second, "fg", "chrome", "Page")], "chrome", Frame(2)).Limited);
        Assert.Equal("no window other than NVDA's was recorded in front before it", ReportScope.At([], "chrome", Frame(2)).Why);
    }

    [Fact]
    public void The_virtual_screen_origin_and_the_magnified_view_place_the_cut()
    {
        // A virtual screen starting at (-1920, 0): the browser on the right
        // monitor is at frame pixels 1920 on.
        var offset = ReportScope.At([Window(1, "chrome", 0, 0, 1920, 1040)], "chrome", Frame(2, 3840, 1080, x: -1920));
        Assert.Equal(new ScreenRect(1920, 0, 1920, 1040), offset.Crop);

        // At 200%, the magnified view from (400, 300) is 960 by 540.
        var magnified = ReportScope.At([Window(1, "chrome", 0, 0, 1920, 1040)], "chrome", Frame(2, magnification: 2, magnifiedX: 400, magnifiedY: 300));
        Assert.Equal(new ScreenRect(400, 300, 960, 540), magnified.Crop);
    }

    [Fact]
    public void A_cut_keeps_the_windows_pixels_and_paints_the_rest_grey()
    {
        // A 4 by 3 picture, each pixel's red its x and green its y, cut to
        // two windows, (0, 0) 2 by 1 and (3, 2) 1 by 1.
        var rows = Enumerable.Range(0, 3).Select(y => Enumerable.Range(0, 4).SelectMany(x => new byte[] { (byte)x, (byte)y, 200, 255 }).ToArray()).ToList();
        var png = FrameCut.Encode(4, 3, false, rows, new Dictionary<string, byte[]>());
        var scope = new FrameScope(true, [new ScreenRect(0, 0, 2, 1), new ScreenRect(3, 2, 1, 1)], new ScreenRect(0, 0, 4, 3), null);

        var (cut, painted) = FrameCut.Cut(png, scope)!.Value;
        var pixels = PngKey.Decode(cut)!;

        Assert.True(painted);
        Assert.Equal((4, 3), (pixels.Width, pixels.Height));
        Assert.Equal(new byte[] { 0, 0, 200, 255, 1, 0, 200, 255, 128, 128, 128, 255, 128, 128, 128, 255 }, pixels.Rows[0]);
        Assert.Equal(new byte[] { 3, 2, 200, 255 }, pixels.Rows[2][12..]);
        Assert.Equal(PngKey.Of(cut), PngKey.Of(FrameCut.Encode(4, 3, false, pixels.Rows, new Dictionary<string, byte[]>())));
    }

    [Fact]
    public void A_cut_of_one_window_is_its_rectangle_unpainted()
    {
        var rows = Enumerable.Range(0, 3).Select(y => Enumerable.Range(0, 4).SelectMany(x => new byte[] { (byte)x, (byte)y, 0, 255 }).ToArray()).ToList();
        var png = FrameCut.Encode(4, 3, false, rows, new Dictionary<string, byte[]>());
        var (cut, painted) = FrameCut.Cut(png, new FrameScope(true, [new ScreenRect(1, 1, 2, 2)], new ScreenRect(1, 1, 2, 2), null))!.Value;
        var pixels = PngKey.Decode(cut)!;
        Assert.False(painted);
        Assert.Equal(new byte[] { 1, 1, 0, 255, 2, 1, 0, 255 }, pixels.Rows[0]);
    }

    [Fact]
    public void A_picture_that_is_not_a_PNG_is_not_cut() =>
        Assert.Null(FrameCut.Cut([1, 2, 3], new FrameScope(true, [new ScreenRect(0, 0, 1, 1)], new ScreenRect(0, 0, 1, 1), null)));

    [Fact]
    public void The_setting_is_on_by_default_and_read_and_written_as_reportBrowserOnly()
    {
        Assert.True(AnalysisSettings.Empty.BrowserOnly);
        Assert.True(AnalysisSettingsFile.Parse("""{ "version": 1 }""").Settings.BrowserOnly);
        Assert.False(AnalysisSettingsFile.Parse("""{ "version": 1, "reportBrowserOnly": false }""").Settings.BrowserOnly);
        var (settings, problems) = AnalysisSettingsFile.Parse("""{ "version": 1, "reportBrowserOnly": "no" }""");
        Assert.True(settings.BrowserOnly);
        Assert.Single(problems);

        var written = AnalysisSettingsFile.WithBrowserOnly("""{ "version": 1, "maxAttempts": 2 }""", false)!;
        var read = AnalysisSettingsFile.Parse(written).Settings;
        Assert.Equal((false, 2), (read.BrowserOnly, read.MaxAttempts));
        Assert.True(AnalysisSettingsFile.Parse(AnalysisSettingsFile.WithBrowserOnly(null, true)!).Settings.BrowserOnly);
        Assert.Null(AnalysisSettingsFile.WithBrowserOnly("[1]", true));
    }
}
