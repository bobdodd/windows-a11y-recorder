using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 29 of docs/architecture/android-checks.md, step 4: the two checks of
// WCAG 1.4.1, use of colour, measured on made-up pictures. The text is
// drawn as upright strokes, 4 pixels wide (6 when bold), 10 pixels apart,
// in the fixture app's colours: the text #202124, the link #1558B0 (2.34:1
// against the text), the tabs #5F6368 and the selected tab #B0306A (1.00:1
// against the tabs), on white.
public sealed class PhoneColourOnlyTests
{
    private const uint White = 0xFFFFFF;
    private const uint TextInk = 0x202124;
    private const uint LinkInk = 0x1558B0;
    private const uint TabInk = 0x5F6368;
    private const uint SelectedInk = 0xB0306A;

    private const string Words = "See the glossary for help";

    private sealed class Picture
    {
        private readonly uint[] _rgb = Enumerable.Repeat(White, 1080 * 1200).ToArray();

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

        // Upright strokes from left to right, every 10 pixels.
        public Picture Strokes(int left, int top, int right, int bottom, uint colour, int width = 4)
        {
            for (var x = left; x + width <= right; x += 10)
            {
                Fill(x, top, x + width, bottom, colour);
            }

            return this;
        }

        public PhonePixels Pixels => new(1080, 1200, _rgb);
    }

    private static AndroidTreeNode Node(
        int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string? text = null,
        IReadOnlyList<AndroidTextLink>? links = null, bool selected = false, string className = "android.widget.TextView") =>
        new()
        {
            Index = index,
            Parent = parent,
            ClassName = className,
            Bounds = new AndroidNodeBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            Text = text,
            Links = links,
            ImportantForAccessibility = true,
            VisibleToUser = true,
            Enabled = true,
            Selected = selected,
            Actions = [],
            Checked = "unchecked",
        };

    private static PhoneCheckWindow Window(params AndroidTreeNode[] items) =>
        new(512, 1, [Node(0, null, (0, 0, 1080, 1200), links: []), Node(1, 0, (0, 0, 1080, 1200), links: []), .. items]);

    // "glossary", characters 8 to 16, on the first line, at x 300 to 500.
    private static AndroidTextLink Link(string location = AndroidAccessibilityRecords.TextLinkLocated, int? located = 8, int start = 8, int end = 16) =>
        new(start, end, "clickable", null, location, located,
            location == AndroidAccessibilityRecords.TextLinkLocated ? [new AndroidNodeBounds(300, 120, 500, 160)] : []);

    // A paragraph of two lines of text, 40 to 1040 across, with a link on the first.
    private static (PhoneCheckWindow Window, Picture Picture) Paragraph(
        uint link = LinkInk, int linkStroke = 4, bool underlined = false, uint? linkBackground = null, AndroidTextLink? place = null)
    {
        var picture = new Picture()
            .Strokes(50, 120, 300, 160, TextInk)
            .Strokes(500, 120, 1030, 160, TextInk)
            .Strokes(50, 180, 1030, 220, TextInk);
        if (linkBackground is { } background)
        {
            picture.Fill(300, 120, 500, 160, background);
        }

        picture.Strokes(300, 122, 500, 154, link, linkStroke);
        if (underlined)
        {
            picture.Fill(300, 156, 500, 158, link);
        }

        return (Window(Node(5, 1, (40, 100, 1040, 300), Words, [place ?? Link()])), picture);
    }

    private static PhoneLinkLook OneLink(PhoneCheckWindow window, Picture? picture, PhoneFocusOutline? outline = null) =>
        Assert.Single(PhoneColourCues.Links(window, picture?.Pixels, PictureScale.One, outline, []));

    [Fact]
    public void Link_ToldApartOnlyByItsColour_IsAnError()
    {
        var (window, picture) = Paragraph();

        var look = OneLink(window, picture);

        Assert.Null(look.NotMeasured);
        Assert.Equal((uint?)LinkInk, look.Own!.Ink);
        Assert.Equal((uint?)TextInk, look.Around!.Ink);
        Assert.Equal((White, White), (look.Own.Background, look.Around.Background));
        Assert.Equal(4, look.Own.Stroke, 2);
        Assert.Equal(4, look.Around.Stroke, 2);
        Assert.False(look.Underlined);
        Assert.Equal(
            "its colour, #1558B0, against the text's around it, #202124, is 2.34:1, under the 3:1 a colour alone needs; " +
            "it is not underlined, nor bold (its strokes 1.00 times as wide as the text's), and is on the same background, #FFFFFF; " +
            "so it is told apart from the text only by colour",
            PhoneColourOnlyRun.JudgeLink(look));
    }

    [Fact]
    public void Link_Underlined_IsNoFinding()
    {
        var (window, picture) = Paragraph(underlined: true);

        var look = OneLink(window, picture);

        Assert.True(look.Underlined);
        Assert.Equal(4, look.Own!.Stroke, 2);
        Assert.Null(PhoneColourOnlyRun.JudgeLink(look));
    }

    [Fact]
    public void Link_Bold_IsNoFinding()
    {
        var (window, picture) = Paragraph(linkStroke: 6);

        var look = OneLink(window, picture);

        Assert.Equal(1.5, look.Own!.Stroke / look.Around!.Stroke, 2);
        Assert.Null(PhoneColourOnlyRun.JudgeLink(look));
    }

    [Fact]
    public void Link_JustUnderBold_IsAnError()
    {
        // 5 pixels against 4 is 1.25 times, under the 1.3 chosen.
        var (window, picture) = Paragraph(linkStroke: 5);

        var look = OneLink(window, picture);

        Assert.Equal(1.25, look.Own!.Stroke / look.Around!.Stroke, 2);
        Assert.Contains("its strokes 1.25 times as wide as the text's", PhoneColourOnlyRun.JudgeLink(look), StringComparison.Ordinal);
    }

    [Fact]
    public void Link_ThreeToOneInLightness_IsNoFinding()
    {
        var (window, picture) = Paragraph(link: 0x4A90E2);

        var look = OneLink(window, picture);

        Assert.True(PhoneColours.Ratio(PhoneColours.Luminance(0x4A90E2), PhoneColours.Luminance(TextInk)) >= 3);
        Assert.Null(PhoneColourOnlyRun.JudgeLink(look));
    }

    [Fact]
    public void Link_OnABackground_ThreeToOneFromTheTexts_IsNoFinding()
    {
        var (window, picture) = Paragraph(linkBackground: 0x707070);

        var look = OneLink(window, picture);

        Assert.Equal((0x707070u, White), (look.Own!.Background, look.Around!.Background));
        Assert.Null(PhoneColourOnlyRun.JudgeLink(look));
    }

    [Fact]
    public void Link_InTheTextsColour_IsNoFinding()
    {
        var (window, picture) = Paragraph(link: TextInk);

        Assert.Null(PhoneColourOnlyRun.JudgeLink(OneLink(window, picture)));
    }

    [Fact]
    public void Link_NotLocated_PartlyLocated_OrTheWholeText_IsNotMeasured()
    {
        string? Why(AndroidTextLink place)
        {
            var (window, picture) = Paragraph(place: place);
            return OneLink(window, picture).NotMeasured;
        }

        Assert.Equal("its place on the screen was not recorded (not-located)", Why(Link(location: "not-located", located: null)));
        Assert.Equal(
            "only 5 of its 8 characters have a place on the screen, so some of it is off the screen",
            Why(Link(located: 5)));
        Assert.Equal(
            "it is the item's whole text, so there is no text around it to compare it with",
            Why(Link(located: Words.Length, start: 0, end: Words.Length)));
    }

    [Fact]
    public void Link_OnlyOnAVideoFrame_OrUnderTheFocusOutline_IsNotMeasured()
    {
        var (window, picture) = Paragraph();

        Assert.Equal(PhoneColourCues.FromVideo, OneLink(window, null).NotMeasured);
        var outline = new PhoneFocusOutline(window.Items.Single(item => item.Node.Index == 5), 0x00FF00, 10);
        Assert.StartsWith("an outline, #00FF00, 10 pixels thick, is drawn over it, round it", OneLink(window, picture, outline).NotMeasured, StringComparison.Ordinal);
    }

    [Fact]
    public void Links_NotRecorded_OrNone_AreNotMeasured()
    {
        var before = new PhoneCheckWindow(512, 1, [Node(0, null, (0, 0, 1080, 1200)), Node(5, 0, (40, 100, 1040, 300), Words)]);
        var none = Window(Node(5, 1, (40, 100, 1040, 300), Words, []));

        Assert.Empty(PhoneColourCues.Links(before, new Picture().Pixels, PictureScale.One, null, []));
        Assert.Empty(PhoneColourCues.Links(none, new Picture().Pixels, PictureScale.One, null, []));
    }

    // Three tabs, 300 pixels wide, in a strip, the first selected.
    private static (PhoneCheckWindow Window, Picture Picture) Tabs(
        uint selectedInk = SelectedInk, int selectedStroke = 4, bool underlined = false, bool bar = false)
    {
        var picture = new Picture();
        var tabs = new List<AndroidTreeNode> { Node(2, 1, (40, 1000, 940, 1100), className: "android.widget.LinearLayout", links: []) };
        for (var index = 0; index < 3; index++)
        {
            var left = 40 + (index * 300);
            var first = index == 0;
            picture.Strokes(left + 100, 1030, left + 200, 1066, first ? selectedInk : TabInk, first ? selectedStroke : 4);
            tabs.Add(Node(10 + index, 2, (left, 1000, left + 300, 1100), $"Tab {index + 1}", [], selected: first));
        }

        if (underlined)
        {
            picture.Fill(140, 1068, 240, 1070, selectedInk);
        }

        if (bar)
        {
            picture.Fill(40, 1094, 340, 1100, selectedInk);
        }

        return (Window([.. tabs]), picture);
    }

    private static PhoneSelectedLook OneSelected(PhoneCheckWindow window, Picture? picture, PhoneFocusOutline? outline = null) =>
        Assert.Single(PhoneColourCues.Selected(window, picture?.Pixels, PictureScale.One, outline, []));

    [Fact]
    public void Selected_ShownOnlyByItsHue_IsAnError()
    {
        var (window, picture) = Tabs();

        var look = OneSelected(window, picture);

        Assert.Null(look.NotMeasured);
        Assert.Equal(2, look.Others);
        Assert.Equal(((uint?)SelectedInk, (uint?)TabInk), (look.Own!.Ink, look.Theirs!.Ink));
        Assert.False(look.Underlined || look.Bar || look.OthersUnderlined || look.OthersBar);
        Assert.Equal(
            "marked selected in the tree, and drawn in #B0306A on #FFFFFF, where the 2 items beside it not selected are in #5F6368 on #FFFFFF: " +
            "1.00:1, under 3:1; it has no underline or bar they lack, and is not bold (its strokes 1.00 times as wide as theirs); " +
            "so its state is shown only by colour",
            PhoneColourOnlyRun.JudgeSelected(look));
    }

    [Fact]
    public void Selected_Bold_Underlined_OrWithABar_IsNoFinding()
    {
        static PhoneSelectedLook Of((PhoneCheckWindow Window, Picture Picture) tabs) => OneSelected(tabs.Window, tabs.Picture);
        var bold = Of(Tabs(selectedStroke: 6));
        var underlined = Of(Tabs(underlined: true));
        var bar = Of(Tabs(bar: true));

        Assert.Equal(1.5, bold.Own!.Stroke / bold.Theirs!.Stroke, 2);
        Assert.True(underlined.Underlined);
        Assert.True(bar.Bar);
        Assert.All([bold, underlined, bar], look => Assert.Null(PhoneColourOnlyRun.JudgeSelected(look)));
    }

    [Fact]
    public void Selected_DrawnLikeTheOthers_IsNoFinding()
    {
        var (window, picture) = Tabs(selectedInk: TabInk);

        Assert.Null(PhoneColourOnlyRun.JudgeSelected(OneSelected(window, picture)));
    }

    [Fact]
    public void Selected_WithAnItemBesideItCovered_IsMeasuredAgainstTheRest()
    {
        var (window, picture) = Tabs();
        var second = window.Items.Single(item => item.Node.Index == 11);

        var look = OneSelected(window, picture, new PhoneFocusOutline(second, 0x00FF00, 10));
        Assert.Null(look.NotMeasured);
        Assert.Equal(1, look.Others);
        Assert.Contains("where the item beside it not selected is in #5F6368", PhoneColourOnlyRun.JudgeSelected(look), StringComparison.Ordinal);

        // With the selected item itself covered, it is not measured.
        var covered = OneSelected(window, picture, new PhoneFocusOutline(window.Items.Single(item => item.Node.Index == 10), 0x00FF00, 10));
        Assert.StartsWith("an outline, #00FF00", covered.NotMeasured, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_OnlyOnAVideoFrame_IsNotMeasured()
    {
        var (window, _) = Tabs();

        Assert.Equal(PhoneColourCues.FromVideo, OneSelected(window, null).NotMeasured);
    }
}
