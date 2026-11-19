using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// What is drawn over the app where the contrast checks measure (owner,
// 2026-11-19): the outline round the item with accessibility focus, as a
// screen reader shows its focus, and other windows. Made-up pictures in
// the colours and sizes of the fixture tour's second recording: an outline
// 10 pixels thick in #6BC840 round a line of grey text on white.
public sealed class PhoneCoverTests
{
    private const uint White = 0xFFFFFF;
    private const uint Green = 0x6BC840;
    private const uint Grey = 0xB0B0B0;

    private static readonly PhoneBounds Line = new(39, 455, 1041, 548);

    private static AndroidTreeNode Node(int index, int? parent, PhoneBounds bounds, bool focused = false) => new()
    {
        Index = index,
        Parent = parent,
        ClassName = "android.widget.TextView",
        Bounds = new AndroidNodeBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
        Text = index == 1 ? "Orders are packed" : null,
        ImportantForAccessibility = true,
        VisibleToUser = true,
        Enabled = true,
        AccessibilityFocused = focused,
        Checked = "unchecked",
    };

    private static PhoneCheckWindow Window(bool focused = true) =>
        new(512, 1, [Node(0, null, new PhoneBounds(0, 0, 1080, 1200)), Node(1, 0, Line, focused)]);

    private static PhonePixels Picture(int thickness, uint outline = Green, int width = 1080, int height = 1200)
    {
        var rgb = Enumerable.Repeat(White, width * height).ToArray();
        void Fill(int left, int top, int right, int bottom, uint colour)
        {
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    rgb[y * width + x] = colour;
                }
            }
        }

        Fill(Line.Left, Line.Top, Line.Right, Line.Bottom, outline);
        Fill(Line.Left + thickness, Line.Top + thickness, Line.Right - thickness, Line.Bottom - thickness, White);
        Fill(60, 480, 800, 520, Grey);
        return new PhonePixels(width, height, rgb);
    }

    [Fact]
    public void OutlineOf_FindsTheOutlineRoundTheFocusedItem_ItsColourAndThickness()
    {
        var window = Window();

        var outline = PhoneCover.OutlineOf(window, Picture(10), PictureScale.One);

        Assert.NotNull(outline);
        Assert.Same(window.Items[1], outline.Item);
        Assert.Equal((Green, 10), (outline.Colour, outline.Thickness));
    }

    [Fact]
    public void OutlineOf_WithNoItemFocused_OrNoOutlineDrawn_OrAThinBorder_IsNull()
    {
        Assert.Null(PhoneCover.OutlineOf(Window(focused: false), Picture(10), PictureScale.One));
        Assert.Null(PhoneCover.OutlineOf(Window(), Picture(10, outline: White), PictureScale.One));

        // A border thinner than 4 pixels may be the item's own.
        Assert.Null(PhoneCover.OutlineOf(Window(), Picture(2), PictureScale.One));
    }

    [Fact]
    public void OutlineOf_OnAHalfSizePicture_GivesTheThicknessInTheScreensPixels()
    {
        var scale = new PictureScale(0.5, 0.5);
        var window = new PhoneCheckWindow(512, 1, [Node(0, null, new PhoneBounds(0, 0, 2160, 2400)), Node(1, 0, new PhoneBounds(78, 910, 2082, 1096), focused: true)]);

        var outline = PhoneCover.OutlineOf(window, Picture(10), scale);

        Assert.NotNull(outline);
        Assert.Equal(20, outline.Thickness);
    }

    [Fact]
    public void Why_CoversWhatCrossesTheOutline_ButNotWhatIsInsideIt()
    {
        var window = Window();
        var outline = PhoneCover.OutlineOf(window, Picture(10), PictureScale.One)!;

        Assert.Equal(
            "an outline, #6BC840, 10 pixels thick, is drawn over it, round it, the item with accessibility focus, as a screen reader shows its focus",
            PhoneCover.Why(Line, window.Items[1], outline, []));
        Assert.Null(PhoneCover.Why(new PhoneBounds(60, 480, 800, 520), window.Items[1], outline, []));
        Assert.Null(PhoneCover.Why(new PhoneBounds(39, 600, 1041, 700), null, outline, []));
        Assert.Contains("round TextView", PhoneCover.Why(new PhoneBounds(39, 400, 1041, 470), null, outline, []), StringComparison.Ordinal);
    }

    [Fact]
    public void Why_CoversWhatAWindowAboveMeets_NamingTheWindow()
    {
        PhoneWindowAbove[] above = [new(9, "TYPE_SYSTEM", null, new PhoneBounds(0, 543, 118, 661))];

        Assert.Equal(
            "another window, untitled, TYPE_SYSTEM, is drawn over part of it",
            PhoneCover.Why(new PhoneBounds(39, 548, 1041, 641), null, null, above));
        Assert.Equal(
            "another window, 'Volume', is drawn over part of it",
            PhoneCover.Why(new PhoneBounds(39, 548, 1041, 641), null, null, [new(9, null, "Volume", new PhoneBounds(1000, 600, 1080, 900))]));
        Assert.Null(PhoneCover.Why(new PhoneBounds(39, 700, 1041, 800), null, null, above));
    }
}
