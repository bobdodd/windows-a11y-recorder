using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// The checks of a screen's title and headings (android-checks.md, step 6),
/// run on the app's window as a settled snapshot shows it. Each is one
/// discrete issue (owner, 2026-11-12), so the advice for it can be
/// specific. The shared title is checked across the recording's screens,
/// by <see cref="PhoneCheckRun"/>, with <see cref="ScreenTitle"/>.
/// </summary>
public static class PhoneScreenChecks
{
    /// <summary>The first heading should start in this part of the window's height, from its top (my assumption, 2026-11-12).</summary>
    public const double TopPart = 0.25;

    /// <summary>"Many headings": at least this many on the screen...</summary>
    public const int ManyHeadingsAtLeast = 3;

    /// <summary>...and more headings than half the items a screen reader stops at (my assumption, 2026-11-12).</summary>
    public const double ManyHeadingsShare = 0.5;

    /// <summary>Runs the checks of one window: no title, no heading, first heading far from the top, many headings.</summary>
    public static IReadOnlyList<PhoneCheckResult> Run(PhoneCheckWindow window, PhoneCheckContext context)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);
        var results = new List<PhoneCheckResult>();
        if (window.Items.Count == 0)
        {
            return results;
        }

        TitleMissing(window, results);
        var headings = Headings(window);
        NoHeading(window, headings, results);
        FirstHeadingLow(window, context, headings, results);
        ManyHeadings(window, headings, results);
        return results;
    }

    /// <summary>
    /// The screen's title as Android gives it to a screen reader: the first
    /// pane title in the tree's order (<c>getPaneTitle</c>, Android's title
    /// of a part of a window, such as a single-activity app's screen), or
    /// else the window's title; null when neither has any text. A title of
    /// only spaces is none, as nothing would be said.
    /// </summary>
    public static (string Title, string Source)? ScreenTitle(PhoneCheckWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        foreach (var item in window.Items)
        {
            if (HasText(item.Node.PaneTitle))
            {
                return (item.Node.PaneTitle!.Trim(), "pane title");
            }
        }

        return HasText(window.Title) ? (window.Title!.Trim(), "window title") : null;
    }

    /// <summary>The items marked as headings that are shown: visible to the user, with an area.</summary>
    internal static List<PhoneCheckItem> Headings(PhoneCheckWindow window) =>
        [.. window.Items.Where(item => item.Node.Heading && Shown(item))];

    private static bool Shown(PhoneCheckItem item) =>
        item.Node.VisibleToUser && item.Bounds.Right > item.Bounds.Left && item.Bounds.Bottom > item.Bounds.Top;

    private static bool HasText(string? text) => !string.IsNullOrWhiteSpace(text);

    // WCAG2ICT's 2.4.2 Non-web Software Titled: a screen with no title.
    internal static void TitleMissing(PhoneCheckWindow window, List<PhoneCheckResult> results)
    {
        if (ScreenTitle(window) is not null)
        {
            return;
        }

        var blankPanes = window.Items.Count(item => item.Node.PaneTitle is { } pane && !HasText(pane));
        var windowTitle = window.Title is null ? "the window has no title" : "the window's title is blank";
        var panes = blankPanes switch
        {
            0 => "no pane in it has a title",
            1 => "the one pane title in it is blank",
            _ => string.Create(CultureInfo.InvariantCulture, $"the {blankPanes} pane titles in it are blank"),
        };
        results.Add(new PhoneCheckResult(
            PhoneCheckNames.TitleMissing,
            PhoneCheckLevel.Error,
            window.Items[0].Node.Index,
            $"{windowTitle} and {panes}, so a screen reader has no title to say for the screen"));
    }

    // WCAG 1.3.1 (owner, 2026-11-12): a screen with no heading shown fails,
    // as its structure is not evident; false positives, such as a page of
    // an e-book, are accepted.
    internal static void NoHeading(PhoneCheckWindow window, List<PhoneCheckItem> headings, List<PhoneCheckResult> results)
    {
        if (headings.Count > 0)
        {
            return;
        }

        var hidden = window.Items.Count(item => item.Node.Heading);
        var measured = hidden switch
        {
            0 => "no item on the screen is marked as a heading",
            1 => "no item on the screen is marked as a heading; the one item marked as a heading is not shown",
            _ => string.Create(CultureInfo.InvariantCulture, $"no item on the screen is marked as a heading; the {hidden} items marked as headings are not shown"),
        };
        results.Add(new PhoneCheckResult(PhoneCheckNames.NoHeading, PhoneCheckLevel.Error, window.Items[0].Node.Index, measured));
    }

    // The topmost heading should be close to the top of the window (owner,
    // 2026-11-12): in its top quarter (my assumption).
    internal static void FirstHeadingLow(PhoneCheckWindow window, PhoneCheckContext context, List<PhoneCheckItem> headings, List<PhoneCheckResult> results)
    {
        if (headings.Count == 0)
        {
            return;
        }

        var root = window.Items[0].Bounds;
        var height = root.Bottom - root.Top;
        if (height <= 0)
        {
            return;
        }

        var first = headings.OrderBy(item => item.Bounds.Top).ThenBy(item => item.Bounds.Left).First();
        var offset = first.Bounds.Top - root.Top;
        var share = (double)offset / height;
        if (share <= TopPart)
        {
            return;
        }

        var distance = context.Density > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{offset / context.Density:0} dp down the window, {share:0%} of its height")
            : string.Create(CultureInfo.InvariantCulture, $"{share:0%} of the way down the window");
        results.Add(new PhoneCheckResult(
            PhoneCheckNames.FirstHeadingLow,
            PhoneCheckLevel.Warning,
            first.Node.Index,
            $"the first heading from the top starts {distance}, below its top quarter"));
    }

    // An abundance of headings (owner, 2026-11-12): at least three, and
    // more than half as many as the items a screen reader stops at (my
    // assumption), as when every item of a list is marked as a heading.
    internal static void ManyHeadings(PhoneCheckWindow window, List<PhoneCheckItem> headings, List<PhoneCheckResult> results)
    {
        if (headings.Count < ManyHeadingsAtLeast)
        {
            return;
        }

        var stops = window.Items.Count(PhoneTreeChecks.ShouldFocusView);
        if (headings.Count <= stops * ManyHeadingsShare)
        {
            return;
        }

        results.Add(new PhoneCheckResult(
            PhoneCheckNames.ManyHeadings,
            PhoneCheckLevel.Warning,
            window.Items[0].Node.Index,
            string.Create(CultureInfo.InvariantCulture,
                $"{headings.Count} items on the screen are marked as headings, against {stops} items a screen reader stops at")));
    }
}
