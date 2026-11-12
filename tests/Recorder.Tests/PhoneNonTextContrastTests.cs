using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 5 of docs/architecture/android-checks.md: non-text contrast, WCAG
// 2.2's 1.4.11, from made-up pictures drawn in the colours measured in
// tests 16 and 18's recordings: the page #F6F6E2, the bar #F3CB51, the
// selected tab's pill #F5E19A (1.20:1 on the bar), the border #003B3D
// (11.34:1 on the page), WEEK VIEW's fill #81C4C1 (1.98:1 on white).
public sealed class PhoneNonTextContrastTests
{
    private const uint Page = 0xF6F6E2;
    private const uint Bar = 0xF3CB51;
    private const uint Pill = 0xF5E19A;
    private const uint Border = 0x003B3D;
    private const uint White = 0xFFFFFF;
    private const uint Teal = 0x81C4C1;
    private const uint Black = 0x000000;

    private static readonly PhoneBounds Screen = new(0, 0, 1080, 1200);

    private sealed class Picture
    {
        private readonly uint[] _rgb = Enumerable.Repeat(Page, 1080 * 1200).ToArray();

        public Picture Fill(int left, int top, int right, int bottom, uint colour)
        {
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    _rgb[y * 1080 + x] = colour;
                }
            }

            return this;
        }

        // A box with a border of the given width and a fill inside it.
        public Picture Boxed(int left, int top, int right, int bottom, uint border, int width, uint fill) =>
            Fill(left, top, right, bottom, border).Fill(left + width, top + width, right - width, bottom - width, fill);

        public PhonePixels Pixels => new(1080, 1200, _rgb);
    }

    private static AndroidTreeNode Node(
        int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string? description = null,
        bool clickable = true, bool enabled = true, bool selected = false, bool scrolls = false, string className = "android.view.View") =>
        new()
        {
            Index = index,
            Parent = parent,
            ClassName = className,
            Bounds = new AndroidNodeBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            ContentDescription = description,
            ImportantForAccessibility = true,
            VisibleToUser = true,
            Enabled = enabled,
            Clickable = clickable,
            Selected = selected,
            Actions = scrolls ? [new AndroidNodeAction(0x1000, null)] : [],
            Checked = "unchecked",
        };

    private static PhoneCheckWindow Window(params AndroidTreeNode[] items) =>
        new(512, 1, [Node(0, null, (0, 0, 1080, 1200), clickable: false), Node(1, 0, (0, 0, 1080, 1200), clickable: false), .. items]);

    // A line of text OCR read, in the screen's pixels.
    private static PhoneTextLine Text(string words, int left, int top, int right, int bottom) =>
        new(null, null, PhoneTextPlace.FoundByOcr, null, words, new PhoneBounds(left, top, right, bottom), null, bottom - top);

    private static IReadOnlyList<PhoneControlMeasure> Measure(PhoneCheckWindow window, Picture picture, params PhoneTextLine[] lines) =>
        PhoneControlMeasuring.Measure(window, Screen, [], lines, ocr: true, picture.Pixels, PictureScale.One);

    // The bottom bar: four tabs with their words, the first on a pill.
    private static (PhoneCheckWindow Window, Picture Picture, PhoneTextLine[] Lines) Tabs(bool marked = false)
    {
        var picture = new Picture().Fill(0, 1000, 1080, 1200, Bar).Fill(48, 1020, 211, 1169, Pill);
        var lines = new List<PhoneTextLine>();
        var tabs = new List<AndroidTreeNode>();
        string[] names = ["HOME", "LEARN", "DIARY", "PROFILE"];
        int[] lefts = [48, 307, 575, 835];
        int[] widths = [163, 172, 165, 197];
        for (var index = 0; index < 4; index++)
        {
            var (left, right) = (lefts[index], lefts[index] + widths[index]);
            picture.Fill(left + 40, 1080, right - 40, 1110, Black);
            lines.Add(Text(names[index], left + 40, 1080, right - 40, 1110));
            tabs.Add(Node(10 + index, 1, (left, 1020, right, 1169), names[index], selected: marked && index == 0));
        }

        return (Window([.. tabs]), picture, [.. lines]);
    }

    private static PhoneControlMeasure Of(IReadOnlyList<PhoneControlMeasure> measures, string description) =>
        measures.Single(measure => measure.Item.Node.ContentDescription == description);

    [Fact]
    public void Measure_FindsABorderOnTheEdge_AndTheColourNextToIt()
    {
        var picture = new Picture().Boxed(100, 100, 600, 220, Border, 3, White).Fill(200, 150, 500, 170, Border);
        var measures = Measure(Window(Node(5, 1, (100, 100, 600, 220), "Save")), picture, Text("Save", 200, 150, 500, 170));

        var save = Assert.Single(measures);
        Assert.Equal(4, save.Sides.Count);
        Assert.All(save.Sides, side => Assert.Equal((Page, (uint?)Border), (side.Adjacent, side.Shape)));
        Assert.Equal(11.34, save.BoundaryRatio, 2);
        Assert.Equal(White, save.Fill);
        Assert.Equal("Save", save.VisibleText);
    }

    [Fact]
    public void Judge_ASelectedTabShownOnlyByAPalePill_IsAnError_AndTheOtherTabsAreInfo()
    {
        var (window, picture, lines) = Tabs();
        var measures = Measure(window, picture, lines);

        var states = PhoneNonTextContrastRun.States(measures);

        var home = Of(measures, "HOME");
        var shown = Assert.Single(states);
        Assert.Same(home, shown.Key);
        Assert.False(shown.Value.FromTree);
        Assert.Equal((3, Bar), (shown.Value.Others, shown.Value.OthersFill));
        var judged = PhoneNonTextContrastRun.JudgeState(home, shown.Value, PhonePictureKind.Screenshot);
        Assert.Equal(PhoneCheckLevel.Error, judged?.Level);
        Assert.Equal(
            "taken to show its state, as it is filled #F5E19A and the 3 others in its row are not: 1.20:1 against #F3CB51, " +
            "the others' fill and the colour around it; a state needs 3:1",
            judged?.Measured);

        // A tab with its words and no boundary needs none: info.
        var learn = PhoneNonTextContrastRun.Judge(Of(measures, "LEARN"), PhonePictureKind.Screenshot, "Windows OCR, en-US");
        Assert.Equal(PhoneCheckLevel.Info, learn?.Level);
        Assert.Equal(
            "has visible text, 'LEARN', found by OCR (Windows OCR, en-US), so needs no boundary; " +
            "no boundary is drawn on 4 of the 4 sides measured, against #F3CB51",
            learn?.Measured);
    }

    [Fact]
    public void States_TheTreesMarkIsUsed_WhenItMarksOneOfTheRow()
    {
        var (window, picture, lines) = Tabs(marked: true);
        var measures = Measure(window, picture, lines);

        var shown = Assert.Single(PhoneNonTextContrastRun.States(measures));

        Assert.Equal("HOME", shown.Key.Item.Node.ContentDescription);
        Assert.True(shown.Value.FromTree);
        Assert.StartsWith(
            "marked selected in the tree, and filled #F5E19A: 1.20:1",
            PhoneNonTextContrastRun.JudgeState(shown.Key, shown.Value, PhonePictureKind.Screenshot)?.Measured,
            StringComparison.Ordinal);
    }

    [Fact]
    public void States_OfTwoThatTouch_TheOneLessLikeTheColourAround_ShowsTheState()
    {
        var picture = new Picture()
            .Boxed(59, 400, 540, 517, Border, 3, White)
            .Boxed(540, 400, 1022, 517, Border, 3, Teal);
        var measures = Measure(
            Window(Node(5, 1, (59, 400, 540, 517), "CALENDAR"), Node(6, 1, (540, 400, 1022, 517), "WEEK VIEW")),
            picture, Text("CALENDAR", 200, 440, 400, 470), Text("WEEK VIEW", 680, 440, 880, 470));

        var shown = Assert.Single(PhoneNonTextContrastRun.States(measures));

        Assert.Equal("WEEK VIEW", shown.Key.Item.Node.ContentDescription);
        var judged = PhoneNonTextContrastRun.JudgeState(shown.Key, shown.Value, PhonePictureKind.Screenshot);
        Assert.Equal(PhoneCheckLevel.Error, judged?.Level);
        Assert.Contains("1.98:1 against #FFFFFF, the other's fill, and 1.81:1 against #F6F6E2 around it", judged?.Measured, StringComparison.Ordinal);
    }

    [Fact]
    public void States_CardsStackedDownTheScreen_OrTwoApart_ShowNoState()
    {
        // In test 16's recording the "Start your streak" card, white, is
        // above the "Good morning" card, #B0D8CE: a column, not a state.
        var picture = new Picture()
            .Boxed(59, 100, 1022, 222, Border, 3, White)
            .Boxed(59, 281, 1022, 403, Border, 3, 0xB0D8CE)
            .Boxed(59, 600, 500, 700, Border, 3, White)
            .Boxed(580, 600, 1022, 700, Border, 3, Teal);
        var measures = Measure(
            Window(
                Node(5, 1, (59, 100, 1022, 222), "Start"), Node(6, 1, (59, 281, 1022, 403), "Morning"),
                Node(7, 1, (59, 600, 500, 700), "Left card"), Node(8, 1, (580, 600, 1022, 700), "Right card")),
            picture);

        Assert.Empty(PhoneNonTextContrastRun.States(measures));
    }

    [Fact]
    public void Judge_AControlWithNoText_NeedsAnIconOrABoundaryOf3To1()
    {
        // A black bell on the bar, an icon of 13.47:1; a pale grey icon
        // on white, 1.92:1, with no boundary; the same with a border.
        var picture = new Picture()
            .Fill(0, 0, 1080, 300, Bar).Fill(930, 150, 980, 200, Black)
            .Fill(0, 300, 1080, 600, White).Fill(130, 430, 180, 480, 0xBBBBBB)
            .Boxed(600, 400, 760, 520, Border, 3, White).Fill(660, 440, 700, 480, 0xBBBBBB);
        picture.Fill(0, 300, 1080, 330, White);
        var measures = Measure(
            Window(
                Node(5, 1, (895, 117, 1012, 234), "Notifications"),
                Node(6, 1, (100, 400, 210, 510), "Add"),
                Node(7, 1, (600, 400, 760, 520), "Edit")),
            picture);

        var bell = PhoneNonTextContrastRun.Judge(Of(measures, "Notifications"), PhonePictureKind.Screenshot, null);
        Assert.Equal(PhoneCheckLevel.Info, bell?.Level);
        Assert.StartsWith("no visible text, but its icon, #000000 on #F3CB51, is 13.47:1, so it needs no boundary", bell?.Measured, StringComparison.Ordinal);

        var add = PhoneNonTextContrastRun.Judge(Of(measures, "Add"), PhonePictureKind.Screenshot, null);
        Assert.Equal(PhoneCheckLevel.Error, add?.Level);
        Assert.Equal(
            "no visible text; its icon, #BBBBBB on #FFFFFF, is 1.92:1; no boundary is drawn on 4 of the 4 sides measured, against #FFFFFF; " +
            "a control with no text needs an icon or a boundary of 3:1",
            add?.Measured);

        Assert.Null(PhoneNonTextContrastRun.Judge(Of(measures, "Edit"), PhonePictureKind.Screenshot, null));
    }

    [Theory]
    [InlineData(0x999999u, PhoneCheckLevel.Warning)] // 2.85:1, could be 3.19:1 from the video
    [InlineData(0x949494u, PhoneCheckLevel.Warning)] // 3.03:1, could be 2.67:1
    [InlineData(0x777777u, null)] // 4.48:1
    [InlineData(0xAAAAAAu, PhoneCheckLevel.Error)] // 2.32:1
    public void Judge_FromTheVideo_ARatioWithin12PercentOf3To1_IsAWarning(uint border, PhoneCheckLevel? expected)
    {
        var picture = new Picture().Fill(0, 0, 1080, 1200, White).Boxed(100, 100, 260, 220, border, 3, White);
        var measures = Measure(Window(Node(5, 1, (100, 100, 260, 220), "Add")), picture);

        var judged = PhoneNonTextContrastRun.Judge(Assert.Single(measures), PhonePictureKind.Frame, null);

        Assert.Equal(expected, judged?.Level);
        if (expected == PhoneCheckLevel.Warning)
        {
            Assert.EndsWith("and it is near the limit, so it may fail or pass, as the video's colours are approximate", judged?.Measured, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Measure_LeavesOutInactiveControls_AndSidesWhereAListCutsTheControlOff()
    {
        var picture = new Picture().Boxed(100, 900, 600, 1000, Border, 3, White);
        var window = Window(
            Node(4, 1, (0, 500, 1080, 1000), clickable: false, scrolls: true),
            Node(5, 4, (100, 900, 600, 1000), "Help"),
            Node(6, 1, (100, 100, 600, 200), "Off", enabled: false));

        var help = Assert.Single(Measure(window, picture));

        Assert.Equal("Help", help.Item.Node.ContentDescription);
        Assert.DoesNotContain(help.Sides, side => side.Side == PhoneSide.Bottom);
        Assert.Equal(3, help.Sides.Count);
    }

    // The bottom bar's tabs measured, with their words, for PhoneCheckRunTests.
    internal static (IReadOnlyList<PhoneTextLine> Lines, IReadOnlyList<PhoneControlMeasure> Controls) TabsMeasured()
    {
        var (window, picture, lines) = Tabs();
        return (lines, Measure(window, picture, lines));
    }
}
