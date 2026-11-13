using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 6 of docs/architecture/android-checks.md, "Build order": the checks
// of a screen's title and headings, each one discrete issue (owner,
// 2026-11-12). The items are made up, at the Pixel 10 Pro XL's measured
// density of 390 dpi (1 dp = 2.4375 pixels) and screen of 1080 by 2404
// pixels.
public sealed class PhoneScreenChecksTests
{
    private static readonly PhoneCheckContext Pixel = new(390f / 160f, 1080, 2404);

    private const string Frame = "android.widget.FrameLayout";

    private const string TextView = "android.widget.TextView";

    private const string Button = "android.widget.Button";

    private static AndroidTreeNode Item(
        int index, int? parent, string className, (int Left, int Top, int Right, int Bottom) bounds,
        string? text = null, bool heading = false, string? paneTitle = null, bool clickable = false, bool visible = true) =>
        new()
        {
            Index = index,
            Parent = parent,
            ClassName = className,
            Bounds = new AndroidNodeBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            Text = text,
            Heading = heading,
            PaneTitle = paneTitle,
            Clickable = clickable,
            ImportantForAccessibility = true,
            VisibleToUser = visible,
            Checked = "unchecked",
        };

    private static AndroidTreeNode Page(string? paneTitle = null) => Item(0, null, Frame, (0, 0, 1080, 2404), paneTitle: paneTitle);

    private static PhoneCheckWindow Window(string? title, params AndroidTreeNode[] items) => new(512, 1, items, title);

    private static List<PhoneCheckResult> Of(string check, PhoneCheckWindow window) =>
        [.. PhoneScreenChecks.Run(window, Pixel).Where(result => result.Check == check)];

    // No screen title (WCAG2ICT 2.4.2)

    [Fact]
    public void Title_NoWindowTitle_AndABlankPaneTitle_IsAnError_OnThePage()
    {
        // The client's app gave its pane the title " " (measured).
        var window = Window(null, Page(" "), Item(1, 0, TextView, (100, 60, 600, 130), "Sleep Diary"));

        var result = Assert.Single(Of(PhoneCheckNames.TitleMissing, window));

        Assert.Equal((PhoneCheckLevel.Error, 0), (result.Level, result.Item));
        Assert.Equal(
            "the window has no title and the one pane title in it is blank, so a screen reader has no title to say for the screen",
            result.Measured);
        Assert.Null(PhoneScreenChecks.ScreenTitle(window));
    }

    [Fact]
    public void Title_AWindowTitle_OrAPaneTitle_IsATitle()
    {
        Assert.Empty(Of(PhoneCheckNames.TitleMissing, Window("Nova SleepCare", Page(" "))));
        Assert.Empty(Of(PhoneCheckNames.TitleMissing, Window("  ", Page("Sleep Diary"))));

        // The pane title is the screen's title before the window's.
        Assert.Equal<(string, string)?>(("Sleep Diary", "pane title"), PhoneScreenChecks.ScreenTitle(Window("Nova SleepCare", Page("Sleep Diary"))));
        Assert.Equal<(string, string)?>(("Nova SleepCare", "window title"), PhoneScreenChecks.ScreenTitle(Window(" Nova SleepCare ", Page(" "))));
    }

    [Fact]
    public void Title_ABlankWindowTitle_AndNoPane_SaysSo()
    {
        var result = Assert.Single(Of(PhoneCheckNames.TitleMissing, Window(" ", Page())));

        Assert.StartsWith("the window's title is blank and no pane in it has a title", result.Measured, StringComparison.Ordinal);
    }

    // No heading (WCAG 1.3.1)

    [Fact]
    public void NoHeading_NoItemMarkedAsAHeading_IsAnError_OnThePage()
    {
        var window = Window("Nova SleepCare", Page(), Item(1, 0, TextView, (100, 60, 600, 130), "Sleep Diary"));

        var result = Assert.Single(Of(PhoneCheckNames.NoHeading, window));

        Assert.Equal((PhoneCheckLevel.Error, 0), (result.Level, result.Item));
        Assert.Equal("no item on the screen is marked as a heading", result.Measured);
    }

    [Fact]
    public void NoHeading_HeadingsNotShown_DoNotCount()
    {
        // As on Settings' first screen (measured): items marked as headings,
        // not visible to the user, one with no height.
        var window = Window(
            "Settings",
            Page(),
            Item(1, 0, TextView, (0, 377, 1080, 377), heading: true),
            Item(2, 0, TextView, (0, 1942, 1080, 2000), heading: true, visible: false),
            Item(3, 0, TextView, (100, 500, 600, 560), "Network"));

        var result = Assert.Single(Of(PhoneCheckNames.NoHeading, window));

        Assert.Equal("no item on the screen is marked as a heading; the 2 items marked as headings are not shown", result.Measured);
    }

    [Fact]
    public void NoHeading_AHeadingShown_Passes()
    {
        var window = Window("Settings", Page(), Item(1, 0, TextView, (100, 161, 600, 297), "Settings", heading: true));

        Assert.Empty(PhoneScreenChecks.Run(window, Pixel));
    }

    // First heading far from the top

    [Fact]
    public void FirstHeading_InTheTopQuarter_Passes()
    {
        // 601 pixels down of 2404 is exactly a quarter.
        var window = Window("Settings", Page(), Item(1, 0, TextView, (100, 601, 600, 660), "Display", heading: true));

        Assert.Empty(Of(PhoneCheckNames.FirstHeadingLow, window));
    }

    [Fact]
    public void FirstHeading_BelowTheTopQuarter_IsAWarning_OnTheTopmostHeading()
    {
        // Two headings, the lower first in the tree's order; 1137 pixels is
        // 466 dp and 47% of 2404.
        var window = Window(
            "Settings",
            Page(),
            Item(1, 0, TextView, (100, 1962, 600, 2015), "Other", heading: true),
            Item(2, 0, TextView, (100, 1137, 600, 1190), "Display", heading: true));

        var result = Assert.Single(Of(PhoneCheckNames.FirstHeadingLow, window));

        Assert.Equal((PhoneCheckLevel.Warning, 2), (result.Level, result.Item));
        Assert.Equal("the first heading from the top starts 466 dp down the window, 47% of its height, below its top quarter", result.Measured);
    }

    [Fact]
    public void FirstHeading_WithoutTheDensity_SaysHowFarDown()
    {
        var window = Window("Settings", Page(), Item(1, 0, TextView, (100, 1202, 600, 1260), "Display", heading: true));

        var result = Assert.Single(PhoneScreenChecks.Run(window, new PhoneCheckContext(0, null, null)), found => found.Check == PhoneCheckNames.FirstHeadingLow);

        Assert.Equal("the first heading from the top starts 50% of the way down the window, below its top quarter", result.Measured);
    }

    // Many headings

    private static PhoneCheckWindow List(int headings, int buttons)
    {
        var items = new List<AndroidTreeNode> { Page() };
        for (var index = 1; index <= headings; index++)
        {
            items.Add(Item(index, 0, TextView, (100, 100 * index, 600, 100 * index + 60), $"Heading {index}", heading: true));
        }

        for (var index = headings + 1; index <= headings + buttons; index++)
        {
            items.Add(Item(index, 0, Button, (100, 100 * index, 600, 100 * index + 60), $"Button {index}", clickable: true));
        }

        return Window("Settings", [.. items]);
    }

    [Fact]
    public void ManyHeadings_MoreThanHalfTheStops_IsAWarning_OnThePage()
    {
        // 4 headings and 3 buttons: 7 stops, and 4 is more than 3.5.
        var result = Assert.Single(Of(PhoneCheckNames.ManyHeadings, List(4, 3)));

        Assert.Equal((PhoneCheckLevel.Warning, 0), (result.Level, result.Item));
        Assert.Equal("4 items on the screen are marked as headings, against 7 items a screen reader stops at", result.Measured);
    }

    [Fact]
    public void ManyHeadings_HalfTheStops_OrFewerThanThree_Passes()
    {
        Assert.Empty(Of(PhoneCheckNames.ManyHeadings, List(3, 3)));
        Assert.Empty(Of(PhoneCheckNames.ManyHeadings, List(2, 0)));
    }

    [Fact]
    public void AllTheChecks_AreListed_InTheChecksTabsOrder()
    {
        string[] checks =
        [
            PhoneCheckNames.TitleMissing, PhoneCheckNames.TitleShared, PhoneCheckNames.NoHeading,
            PhoneCheckNames.FirstHeadingLow, PhoneCheckNames.ManyHeadings,
        ];

        Assert.Equal(checks, PhoneCheckNames.All.TakeLast(5));
        Assert.Equal(
            [PhoneCheckNames.MovingByItself, PhoneCheckNames.ContextOnFocus, PhoneCheckNames.ContextOnScreenReaderFocus, PhoneCheckNames.ContextOnInput, PhoneCheckNames.OrientationRestricted],
            PhoneCheckNames.All.SkipLast(5).TakeLast(5));
        Assert.Equal(
            ["No screen title (WCAG2ICT 2.4.2)", "Screen title shared with other screens (WCAG2ICT 2.4.2)", "No heading (WCAG 1.3.1)", "First heading far from the top", "Many headings"],
            checks.Select(PhoneCheckNames.Title));
    }
}
