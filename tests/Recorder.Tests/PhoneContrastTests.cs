using Recorder.Analysis;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 4b of docs/architecture/android-checks.md: measuring the colours of
// text on the phone's pictures, as ATF's ContrastSwatch does with one text
// colour, and finding the text with OCR's words matched to the app's
// items. The pictures and items are made up, one rule each.
public sealed class PhoneContrastTests
{
    private const uint White = 0xFFFFFF;

    private const uint Black = 0x000000;

    private const string TextView = "android.widget.TextView";

    private const string ImageView = "android.widget.ImageView";

    private const string Frame = "android.widget.FrameLayout";

    private const string ScrollView = "android.widget.ScrollView";

    private static readonly PhoneBounds Screen = new(0, 0, 1080, 2404);

    // A picture of one colour with rectangles of others painted on it, in order.
    private static PhonePixels Picture(int width, int height, uint background, params (int Left, int Top, int Right, int Bottom, uint Colour)[] paint)
    {
        var rgb = new uint[width * height];
        Array.Fill(rgb, background);
        foreach (var (left, top, right, bottom, colour) in paint)
        {
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    rgb[y * width + x] = colour;
                }
            }
        }

        return new PhonePixels(width, height, rgb);
    }

    // Colours

    [Fact]
    public void Measure_OneColour_IsOneToOne()
    {
        var swatch = PhoneColours.Measure(Picture(10, 10, White), new PhoneBounds(0, 0, 10, 10))!;

        Assert.Equal(White, swatch.Background);
        Assert.Equal(White, swatch.Foreground);
        Assert.Equal(1.0, swatch.Ratio);
        Assert.Equal(1, swatch.Colours);
    }

    [Fact]
    public void Measure_BlackTextOnWhite_IsTwentyOneToOne_WithTheMoreCommonColourAsBackground()
    {
        // 30 of 100 pixels black.
        var swatch = PhoneColours.Measure(Picture(10, 10, White, (0, 0, 10, 3, Black)), new PhoneBounds(0, 0, 10, 10))!;

        Assert.Equal(White, swatch.Background);
        Assert.Equal(Black, swatch.Foreground);
        Assert.Equal(21.0, swatch.Ratio);
        Assert.Equal("21.00:1, #000000 on #FFFFFF", swatch.Text);
    }

    [Fact]
    public void Measure_ATie_MakesTheDarkerColourTheBackground_AsATFDoes()
    {
        // ContrastSwatch.separateColors: the lighter is the background only
        // when it is more common.
        var swatch = PhoneColours.Measure(Picture(10, 10, White, (0, 0, 10, 5, Black)), new PhoneBounds(0, 0, 10, 10))!;

        Assert.Equal(Black, swatch.Background);
        Assert.Equal(White, swatch.Foreground);
    }

    [Fact]
    public void Measure_RoundsTheRatioToTwoPlaces()
    {
        // #767676 on white is 4.5422 to 1.
        var swatch = PhoneColours.Measure(Picture(10, 10, White, (0, 0, 10, 2, 0x767676)), new PhoneBounds(0, 0, 10, 10))!;

        Assert.Equal(4.54, swatch.Ratio);
    }

    [Fact]
    public void Measure_JoinsNearColours_SoABackgroundTheVideoSplits_IsOne()
    {
        // Near white 25, black 40, white 35: unjoined, black is more common
        // than white and is taken as the background; joined, the near white
        // (Delta E under 1) is counted as white, 60 to 40.
        var picture = Picture(10, 10, White,
            (0, 0, 10, 2, 0xFEFEFE), (0, 2, 5, 3, 0xFEFEFE),
            (5, 2, 10, 3, Black), (0, 3, 10, 6, Black), (0, 6, 5, 7, Black));

        var unjoined = PhoneColours.Measure(picture, new PhoneBounds(0, 0, 10, 10), joinBelow: 0)!;
        var joined = PhoneColours.Measure(picture, new PhoneBounds(0, 0, 10, 10))!;

        Assert.Equal(Black, unjoined.Background);
        Assert.Equal(3, unjoined.Joined);
        Assert.Equal(White, joined.Background);
        Assert.Equal(Black, joined.Foreground);
        Assert.Equal(2, joined.Joined);
        Assert.Equal(3, joined.Colours);
        Assert.Equal(21.0, joined.Ratio);
    }

    [Fact]
    public void Difference_IsATFsDeltaE()
    {
        Assert.Equal(0.0, PhoneColours.Difference(0x336699, 0x336699));
        Assert.Equal(100.0, PhoneColours.Difference(White, Black), 2);
        Assert.True(PhoneColours.Difference(White, 0xFEFEFE) < 1.0);
    }

    [Fact]
    public void Measure_IsWithinThePicture()
    {
        var picture = Picture(10, 10, White, (8, 8, 10, 10, Black));

        Assert.Null(PhoneColours.Measure(picture, new PhoneBounds(20, 20, 30, 30)));
        var clipped = PhoneColours.Measure(picture, new PhoneBounds(8, 8, 30, 30))!;
        Assert.Equal(1.0, clipped.Ratio);
        Assert.Equal(Black, clipped.Background);
    }

    // Reading PNG files

    [Fact]
    public void FromPng_ReadsEachPixel_AndNotAPng_IsNull()
    {
        var rgb = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 16, 32, 48 };

        var pixels = PhonePictures.FromPng(PngWriter.Encode(2, 2, rgb))!;

        Assert.Equal((2, 2), (pixels.Width, pixels.Height));
        Assert.Equal([0xFF0000u, 0x00FF00u, 0x0000FFu, 0x102030u], [pixels.At(0, 0), pixels.At(1, 0), pixels.At(0, 1), pixels.At(1, 1)]);
        Assert.Null(PhonePictures.FromPng([1, 2, 3]));
    }

    // Matching OCR's words to the app's items

    private static AndroidTreeNode Item(
        int index, int? parent, string className, (int Left, int Top, int Right, int Bottom) bounds,
        string? text = null, string? description = null, bool scrolls = false) =>
        new()
        {
            Index = index,
            Parent = parent,
            ClassName = className,
            Bounds = new AndroidNodeBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            Text = text,
            ContentDescription = description,
            ImportantForAccessibility = true,
            VisibleToUser = true,
            Actions = scrolls ? [new AndroidNodeAction(0x1000, null)] : [],
            Checked = "unchecked",
        };

    private static PhoneCheckWindow Window(params AndroidTreeNode[] items) =>
        new(512, 1, [Item(0, null, Frame, (0, 0, 1080, 2404)), .. items]);

    private static PhoneOcrWord Word(string text, int left, int top, int right, int bottom, int line = 0) =>
        new(text, new PhoneBounds(left, top, right, bottom), line);

    private static IReadOnlyList<PhoneTextLine> Lines(PhoneCheckWindow window, params PhoneOcrWord[] words) =>
        PhoneTextMatching.Lines(window, Screen, [], words, PictureScale.One);

    [Fact]
    public void Spelling_TakesTheCharactersOCRConfusesAsOne()
    {
        Assert.Equal("slow", PhoneTextMatching.Spelling("Sl0w"));
        Assert.Equal("sale", PhoneTextMatching.Spelling("5ale"));
        Assert.Equal("slgn", PhoneTextMatching.Spelling("Sign"));
        Assert.Equal("lll", PhoneTextMatching.Spelling("I|1"));
        Assert.Equal("whatsnew", PhoneTextMatching.Spelling("What's new?"));
    }

    [Fact]
    public void Lines_AWordGoesToTheSmallestItemWithIt_WidenedWithinTheItem()
    {
        var window = Window(
            Item(1, 0, Frame, (40, 400, 1040, 600), description: "Okay"),
            Item(2, 1, TextView, (95, 450, 400, 560), text: "Okay"));

        var line = Assert.Single(Lines(window, Word("Okay", 100, 480, 200, 530)));

        Assert.Equal(PhoneTextPlace.AppTextPlacedByOcr, line.Place);
        Assert.Equal(2, line.Item!.Node.Index);
        Assert.Equal("Okay", line.AppText);
        Assert.Equal("Okay", line.OcrText);
        // 20% of the 50-pixel height on each side, but not past the item's left.
        Assert.Equal(new PhoneBounds(95, 470, 210, 540), line.Box);
        // The words' own height, before widening, for the text contrast check.
        Assert.Equal(50, line.WordsHeight);
    }

    [Fact]
    public void Lines_ARowOfTilesInOneOCRLine_IsALinePerTile()
    {
        var window = Window(
            Item(1, 0, TextView, (50, 600, 250, 700), text: "😫\nTerrible"),
            Item(2, 0, TextView, (250, 600, 450, 700), text: "😕\nPoor"));

        var lines = Lines(window, Word("Terrible", 90, 640, 210, 670), Word("poor", 310, 640, 380, 670));

        Assert.Equal(["Terrible", "poor"], lines.Select(line => line.OcrText));
        Assert.Equal([1, 2], lines.Select(line => line.Item!.Node.Index));
    }

    [Fact]
    public void Lines_ANearMisspelling_IsTheItemsWord()
    {
        var window = Window(Item(1, 0, TextView, (40, 400, 1040, 600), text: "Sleep Hygiene"));

        var line = Assert.Single(Lines(window, Word("Sleeq", 100, 450, 300, 500), Word("Hygiene", 310, 450, 500, 500)));

        Assert.Equal(PhoneTextPlace.AppTextPlacedByOcr, line.Place);
        Assert.Equal("Sleeq Hygiene", line.OcrText);
    }

    [Fact]
    public void Lines_WordsUnderAWindowAbove_OrOutsideTheWindow_AreNotTheApps()
    {
        var window = Window(Item(1, 0, TextView, (40, 40, 1040, 2300), text: "Library Today"));
        var statusBar = new PhoneBounds(0, 0, 1080, 130);

        var lines = PhoneTextMatching.Lines(
            window, new PhoneBounds(0, 0, 1080, 2200), [statusBar],
            [Word("Library", 100, 80, 300, 120), Word("Today", 100, 2250, 300, 2290)],
            PictureScale.One);

        // Neither word is placed, so the item is measured on its bounds.
        var line = Assert.Single(lines);
        Assert.Equal(PhoneTextPlace.AppTextOnItemBounds, line.Place);
    }

    [Fact]
    public void Lines_TextInNoItem_IsFoundByOCR_IfItHasTwoLettersOrDigits()
    {
        var window = Window(Item(1, 0, ImageView, (40, 400, 1040, 900), description: "Banner"));

        var line = Assert.Single(Lines(window, Word("SALE", 100, 450, 300, 520), Word("x", 400, 450, 420, 520, line: 1)));

        Assert.Equal(PhoneTextPlace.FoundByOcr, line.Place);
        Assert.Null(line.Item);
        Assert.Equal(1, line.Container!.Node.Index);
        Assert.Equal("SALE", line.OcrText);
    }

    [Fact]
    public void Lines_TextOCRMissed_IsMeasuredOnTheItem_UnlessAnImage_AParent_OrCutOffByAList()
    {
        var window = Window(
            Item(1, 0, TextView, (900, 120, 1000, 230), text: "3"),
            Item(2, 0, ImageView, (40, 300, 200, 460), description: "Profile picture"),
            Item(3, 0, Frame, (40, 500, 1040, 700), description: "Card"),
            Item(4, 3, TextView, (60, 520, 400, 560), text: "Placed"),
            Item(5, 0, ScrollView, (0, 800, 1080, 2100), scrolls: true),
            Item(6, 5, TextView, (100, 2040, 900, 2100), text: "Help & FAQ"));

        var lines = Lines(window, Word("Placed", 70, 525, 300, 555));

        Assert.Equal(
            [(1, PhoneTextPlace.AppTextOnItemBounds), (4, PhoneTextPlace.AppTextPlacedByOcr)],
            lines.Select(line => (line.Item!.Node.Index, line.Place)));
        Assert.Equal(new PhoneBounds(900, 120, 1000, 230), lines[0].Box);
    }

    [Fact]
    public void Lines_OnAPictureSmallerThanTheScreen_AreInTheScreensPixels()
    {
        var window = Window(Item(1, 0, TextView, (40, 400, 1040, 600), text: "Okay"));
        var half = new PictureScale(0.5, 0.5);

        var line = Assert.Single(PhoneTextMatching.Lines(window, Screen, [], [Word("Okay", 50, 240, 100, 265)], half));

        Assert.Equal(new PhoneBounds(90, 470, 210, 540), line.Box);
        Assert.Equal(new PhoneBounds(45, 235, 105, 270), half.ToPicture(line.Box));
    }

    // Choosing the picture

    private static PhoneContrastPicture Shot(long time) =>
        new(PhonePictureKind.Screenshot, time, $"screenshots/android/{time}.png", $"/r/screenshots/android/{time}.png");

    private static SessionVideoFrame VideoFrame(long time) =>
        new(time, $"frames/android/{time}.png", $"/r/frames/android/{time}.png", 1080, 2404);

    [Fact]
    public void PictureOf_IsTheFirstScreenshotFromTheSnapshotUntilTheNextListing()
    {
        var frames = new[] { VideoFrame(100), VideoFrame(200) };

        var picture = PhoneContrastRun.PictureOf([Shot(50), Shot(120), Shot(150), Shot(300)], frames, [100, 200], 0, 100, 300, "/r")!;

        Assert.Equal(PhonePictureKind.Screenshot, picture.Kind);
        Assert.Equal(120, picture.Time);
    }

    [Fact]
    public void PictureOf_WithoutAScreenshot_IsTheLastFrameBeforeTheNextListing_InTheVisit()
    {
        var frames = new[] { VideoFrame(100), VideoFrame(200), VideoFrame(300), VideoFrame(400) };
        long[] times = [100, 200, 300, 400];

        var picture = PhoneContrastRun.PictureOf([Shot(300)], frames, times, 0, 100, 300, "/r")!;

        Assert.Equal(PhonePictureKind.Frame, picture.Kind);
        Assert.Equal(200, picture.Time);
        Assert.Equal("frames/android/200.png", picture.Path);
        Assert.Null(PhoneContrastRun.PictureOf([], frames, times, 250, 260, 300, "/r"));
        Assert.Null(PhoneContrastRun.PictureOf([], [], [], 0, 100, 300, "/r"));
    }

    // The screenshots in the playback index

    private static readonly CollectorDescriptor ScreenshotCollector = CollectorDescriptor.Create(
        "android.screenshots", "AndroidScreenshotCollector", "1", [AndroidScreenshotRecords.Channel], "test");

    [Fact]
    public void Archive_HasTheScreenshotsTheIndexKept_InTimeOrder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "phone-contrast-" + Guid.NewGuid().ToString("N"));
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var sequence = 0UL;
        foreach (var (time, shot) in new[] { (3_000_000_000L, 2L), (2_000_000_000L, 1L) })
        {
            var path = $"screenshots/android/{shot:D10}.png";
            var payload = new AndroidScreenshot("serial", shot, path, "settled", 1080, 2404, 100, "00", "sRGB IEC61966-2.1", false, 1, 2, 3, 4, null, time);
            builder.Add((long)++sequence, RecorderEventFactory.Create("s", ScreenshotCollector, AndroidScreenshotRecords.Channel, sequence, time, AndroidScreenshotRecords.ScreenshotEventType, payload));
        }

        var index = builder.Build();
        var archiveBuilder = new SessionPlaybackArchiveBuilder(folder, retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(10), 10_000_000_000, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));

        Assert.Equal(32, index.Version);
        Assert.Equal([2_000_000_000L, 3_000_000_000L], archive.PhoneScreenshots.Select(shot => shot.MonotonicNanoseconds));
        Assert.Equal("screenshots/android/0000000001.png", archive.PhoneScreenshots[0].Path);
        Assert.Equal(Path.Combine(Path.GetFullPath(folder), "screenshots", "android", "0000000001.png"), archive.PhoneScreenshots[0].AbsolutePath);
    }
}
