using System.Diagnostics;
using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// What a part of a picture's ink looks like (android-checks.md, step 29,
/// "As built (step 4)"): the commonest colour of its ink, or null when it
/// has none; the commonest colour of all its pixels, its background; and
/// the mean length across of its strokes, in the picture's pixels, or 0.
/// </summary>
public sealed record PhoneInkLook(uint? Ink, uint Background, double Stroke);

/// <summary>
/// A link in an item's text as measured on a screenshot (step 29): the
/// item, the link and its place among the item's links, from 1; its boxes
/// joined, in the screen's pixels; why it was not measured, or null; the
/// look of its ink and of the item's text around it; and whether every
/// line of it is underlined.
/// </summary>
public sealed record PhoneLinkLook(
    PhoneCheckItem Item,
    PhoneTextLink Link,
    int Number,
    PhoneBounds Box,
    string? NotMeasured,
    PhoneInkLook? Own = null,
    PhoneInkLook? Around = null,
    bool Underlined = false);

/// <summary>
/// An item the tree marks selected, as measured on a screenshot against
/// the items beside it not selected (step 29): how many there are; why it
/// was not measured, or null; its look and theirs (their commonest ink and
/// background and their mean stroke); and whether it, or any of them, is
/// underlined, or has a bar across or down it.
/// </summary>
public sealed record PhoneSelectedLook(
    PhoneCheckItem Item,
    int Others,
    string? NotMeasured,
    PhoneInkLook? Own = null,
    PhoneInkLook? Theirs = null,
    bool Underlined = false,
    bool OthersUnderlined = false,
    bool Bar = false,
    bool OthersBar = false);

/// <summary>
/// The measuring of the two checks of WCAG 1.4.1 (android-checks.md, step
/// 29): a link in an item's text, against the item's text around it, and
/// an item marked selected, against the items beside it of its class not
/// selected. The owner chose the rules on 2026-11-19, from the fixture
/// app's "Recipes" screen measured with no model. Only a screenshot is
/// measured, as the video's colours are approximate.
/// </summary>
public static class PhoneColourCues
{
    /// <summary>
    /// A difference in lightness of this ratio or more is a visual cue
    /// other than colour (Understanding 1.4.1, G183, F73), compared
    /// unrounded.
    /// </summary>
    public const double LightnessLimit = 3.0;

    /// <summary>
    /// Strokes this many times as wide as those compared with are bold:
    /// between 1.09 for a tab not bold and 1.56 for a bold one, measured on
    /// the fixture app's screen; chosen by the owner "for now" (2026-11-19).
    /// </summary>
    public const double BoldRatio = 1.3;

    /// <summary>A row of ink across this share of the ink's width, or more, is an underline; a bar spans this share of the item.</summary>
    public const double LineShare = 0.85;

    /// <summary>A pixel this far from the background, by Delta E, or more, may be ink.</summary>
    public const double InkDifference = 10.0;

    // Fewer pixels of a colour than this are not ink: an anti-aliased
    // edge, not a letter.
    private const int LeastInk = 10;

    internal const string FromVideo = "only a frame of the video shows it, whose colours are approximate";

    /// <summary>The links in the window's items' text, each measured on the picture, or with why not.</summary>
    public static IReadOnlyList<PhoneLinkLook> Links(
        PhoneCheckWindow window,
        PhonePixels? pixels,
        PictureScale scale,
        PhoneFocusOutline? outline,
        IReadOnlyList<PhoneWindowAbove> above)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(above);
        var looks = new List<PhoneLinkLook>();
        foreach (var item in window.Items)
        {
            if (item.Links is not { Count: > 0 } links || !item.Node.VisibleToUser || item.Bounds.IsEmpty)
            {
                continue;
            }

            var number = 0;
            foreach (var link in links)
            {
                number++;
                var look = new PhoneLinkLook(item, link, number, Joined(link.Boxes) ?? item.Bounds, null);
                var why = WhyNot(item, link, pixels, scale, outline, above);
                looks.Add(why is null ? MeasureLink(look, pixels!, scale) : look with { NotMeasured = why });
            }
        }

        return looks;
    }

    /// <summary>The window's items marked selected with items beside them not selected, each measured on the picture, or with why not.</summary>
    public static IReadOnlyList<PhoneSelectedLook> Selected(
        PhoneCheckWindow window,
        PhonePixels? pixels,
        PictureScale scale,
        PhoneFocusOutline? outline,
        IReadOnlyList<PhoneWindowAbove> above)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(above);
        var looks = new List<PhoneSelectedLook>();
        foreach (var item in window.Items.Where(entry => entry.Node.Selected && Shown(entry) && entry.Parent is not null))
        {
            var others = item.Parent!.Children
                .Where(other => !ReferenceEquals(other, item) && !other.Node.Selected && Shown(other) && other.Node.ClassName == item.Node.ClassName)
                .ToList();
            if (others.Count == 0)
            {
                continue;
            }

            if (pixels is null)
            {
                looks.Add(new PhoneSelectedLook(item, others.Count, FromVideo));
                continue;
            }

            if (PhoneCover.Why(item.Bounds, item, outline, above) is { } covered)
            {
                looks.Add(new PhoneSelectedLook(item, others.Count, covered));
                continue;
            }

            var box = scale.ToPicture(item.Bounds);
            if (!Inside(box, pixels))
            {
                looks.Add(new PhoneSelectedLook(item, others.Count, "it is not wholly on the picture"));
                continue;
            }

            // The others drawn over are left out; with none left, it is not measured.
            var measuredOthers = others
                .Where(other => PhoneCover.Why(other.Bounds, other, outline, above) is null && Inside(scale.ToPicture(other.Bounds), pixels))
                .Select(other => Tab(pixels, scale.ToPicture(other.Bounds)))
                .ToList();
            if (measuredOthers.Count == 0)
            {
                looks.Add(new PhoneSelectedLook(item, others.Count, "each item beside it not selected has something drawn over it or is not wholly on the picture"));
                continue;
            }

            var own = Tab(pixels, box);
            var inks = measuredOthers.Select(other => other.Look.Ink).OfType<uint>().ToList();
            var theirs = new PhoneInkLook(
                inks.Count == 0 ? null : Commonest(inks),
                Commonest([.. measuredOthers.Select(other => other.Look.Background)]),
                measuredOthers.Where(other => other.Look.Stroke > 0).Select(other => other.Look.Stroke).DefaultIfEmpty(0).Average());
            looks.Add(new PhoneSelectedLook(
                item, measuredOthers.Count, null, own.Look, theirs,
                own.Underlined, measuredOthers.Any(other => other.Underlined),
                own.Bar, measuredOthers.Any(other => other.Bar)));
        }

        return looks;
    }

    private static bool Shown(PhoneCheckItem item) => item.Node.VisibleToUser && !item.Bounds.IsEmpty;

    // Why a link is not measured, or null to measure it.
    private static string? WhyNot(
        PhoneCheckItem item,
        PhoneTextLink link,
        PhonePixels? pixels,
        PictureScale scale,
        PhoneFocusOutline? outline,
        IReadOnlyList<PhoneWindowAbove> above)
    {
        if (!link.Located)
        {
            return $"its place on the screen was not recorded ({link.Location})";
        }

        var length = link.End - link.Start;
        if (link.CharactersLocated is not { } located || located < length || link.Boxes.Count == 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"only {link.CharactersLocated ?? 0} of its {length} characters have a place on the screen, so some of it is off the screen");
        }

        if (link.Start == 0 && link.End >= (item.Node.Text?.Length ?? 0))
        {
            return "it is the item's whole text, so there is no text around it to compare it with";
        }

        if (pixels is null)
        {
            return FromVideo;
        }

        if (PhoneCover.Why(item.Bounds, item, outline, above) is { } covered)
        {
            return covered;
        }

        return Inside(scale.ToPicture(item.Bounds), pixels) && link.Boxes.All(box => Inside(scale.ToPicture(box), pixels))
            ? null
            : "it is not wholly on the picture";
    }

    // A located link: its ink in its boxes, against the item's ink outside them.
    private static PhoneLinkLook MeasureLink(PhoneLinkLook look, PhonePixels pixels, PictureScale scale)
    {
        var boxes = look.Link.Boxes.Select(scale.ToPicture).ToList();
        var item = scale.ToPicture(look.Item.Bounds);
        var lineHeight = (int)Math.Round(boxes.Average(box => box.Height));

        // The text around it leaves out every link of the item, not only this one.
        var allLinks = (look.Item.Links ?? []).SelectMany(link => link.Boxes).Select(scale.ToPicture).ToList();
        var inside = Count(pixels, [.. boxes], []);
        var outside = Count(pixels, [item], allLinks);
        var insideBackground = Commonest(inside);
        var outsideBackground = outside.Count == 0 ? insideBackground : Commonest(outside);
        var linkInk = InkOf(inside, insideBackground);
        var textInk = InkOf(outside, outsideBackground);
        if (linkInk is null)
        {
            return look with { NotMeasured = "no ink of it was found in its boxes" };
        }

        if (textInk is null)
        {
            return look with { NotMeasured = "no text around it was found in the item, so there is none to compare it with" };
        }

        var underlines = boxes.Select(box => UnderlineRows(pixels, box, linkInk.Value, insideBackground)).ToList();
        var ownStroke = Stroke(pixels, boxes, [], linkInk.Value, insideBackground, lineHeight, underlines.SelectMany(rows => rows).ToHashSet());
        var aroundStroke = Stroke(pixels, [item], allLinks, textInk.Value, outsideBackground, lineHeight, []);
        return look with
        {
            Own = new PhoneInkLook(linkInk, insideBackground, ownStroke),
            Around = new PhoneInkLook(textInk, outsideBackground, aroundStroke),
            Underlined = underlines.All(rows => rows.Count > 0),
        };
    }

    // An item's look, whether it is underlined, and whether a bar crosses it.
    private static (PhoneInkLook Look, bool Underlined, bool Bar) Tab(PhonePixels pixels, PhoneBounds box)
    {
        var counts = Count(pixels, [box], []);
        var background = Commonest(counts);
        var ink = InkOf(counts, background);
        if (ink is null)
        {
            return (new PhoneInkLook(null, background, 0), false, Bar(pixels, box, background));
        }

        var underline = UnderlineRows(pixels, box, ink.Value, background);
        var (first, last) = InkRows(pixels, box, ink.Value, background);
        var stroke = Stroke(pixels, [box], [], ink.Value, background, Math.Max(1, last - first + 1), underline);
        return (new PhoneInkLook(ink, background, stroke), underline.Count > 0, Bar(pixels, box, background));
    }

    private static Dictionary<uint, int> Count(PhonePixels pixels, IReadOnlyList<PhoneBounds> include, IReadOnlyList<PhoneBounds> exclude)
    {
        var counts = new Dictionary<uint, int>();
        var seen = new HashSet<long>();
        foreach (var box in include)
        {
            for (var y = box.Top; y < box.Bottom; y++)
            {
                for (var x = box.Left; x < box.Right; x++)
                {
                    if (Excluded(exclude, x, y) || !seen.Add(((long)y * pixels.Width) + x))
                    {
                        continue;
                    }

                    var colour = pixels.At(x, y) & 0xFFFFFF;
                    counts[colour] = counts.TryGetValue(colour, out var count) ? count + 1 : 1;
                }
            }
        }

        return counts;
    }

    private static uint Commonest(Dictionary<uint, int> counts) =>
        counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;

    private static uint Commonest(IReadOnlyList<uint> colours) =>
        colours.GroupBy(colour => colour).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;

    // The commonest colour far enough from the background to be ink, if
    // there are enough pixels of it.
    private static uint? InkOf(Dictionary<uint, int> counts, uint background)
    {
        var best = counts
            .Where(pair => PhoneColours.Difference(pair.Key, background) >= InkDifference)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .FirstOrDefault();
        return best.Value >= LeastInk ? best.Key : null;
    }

    // A pixel nearer the ink than the background, by Delta E: at least
    // half way from the background to the ink.
    private sealed class InkTest(uint ink, uint background)
    {
        private readonly Dictionary<uint, bool> _known = [];

        public bool Of(uint pixel)
        {
            var colour = pixel & 0xFFFFFF;
            if (!_known.TryGetValue(colour, out var isInk))
            {
                _known[colour] = isInk = PhoneColours.Difference(colour, ink) < PhoneColours.Difference(colour, background);
            }

            return isInk;
        }
    }

    // The rows of the box's lower half whose ink spans the share of the
    // ink's width from its first column to its last.
    private static HashSet<int> UnderlineRows(PhonePixels pixels, PhoneBounds box, uint ink, uint background)
    {
        var test = new InkTest(ink, background);
        int left = int.MaxValue, right = -1;
        for (var y = box.Top; y < box.Bottom; y++)
        {
            for (var x = box.Left; x < box.Right; x++)
            {
                if (test.Of(pixels.At(x, y)))
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }
        }

        var rows = new HashSet<int>();
        if (right < left)
        {
            return rows;
        }

        var width = right - left + 1;
        for (var y = box.Top + (box.Height / 2); y < box.Bottom; y++)
        {
            var count = 0;
            for (var x = left; x <= right; x++)
            {
                count += test.Of(pixels.At(x, y)) ? 1 : 0;
            }

            if (count >= LineShare * width)
            {
                rows.Add(y);
            }
        }

        return rows;
    }

    private static (int First, int Last) InkRows(PhonePixels pixels, PhoneBounds box, uint ink, uint background)
    {
        var test = new InkTest(ink, background);
        int first = -1, last = -1;
        for (var y = box.Top; y < box.Bottom; y++)
        {
            for (var x = box.Left; x < box.Right; x++)
            {
                if (test.Of(pixels.At(x, y)))
                {
                    first = first < 0 ? y : first;
                    last = y;
                    break;
                }
            }
        }

        return (first, last);
    }

    // The mean length of the runs of ink across each row, leaving out the
    // rows given (an underline's) and runs as long as a line is high or
    // longer, which are lines, not strokes; 0 with no run.
    private static double Stroke(
        PhonePixels pixels,
        IReadOnlyList<PhoneBounds> include,
        IReadOnlyList<PhoneBounds> exclude,
        uint ink,
        uint background,
        int lineHeight,
        HashSet<int> skipRows)
    {
        var test = new InkTest(ink, background);
        long total = 0;
        var runs = 0;
        foreach (var box in include)
        {
            for (var y = box.Top; y < box.Bottom; y++)
            {
                if (skipRows.Contains(y))
                {
                    continue;
                }

                var run = 0;
                for (var x = box.Left; x <= box.Right; x++)
                {
                    if (x < box.Right && !Excluded(exclude, x, y) && test.Of(pixels.At(x, y)))
                    {
                        run++;
                        continue;
                    }

                    if (run > 0 && run < lineHeight)
                    {
                        total += run;
                        runs++;
                    }

                    run = 0;
                }
            }
        }

        return runs == 0 ? 0 : total / (double)runs;
    }

    // A bar across or down the box: a row of pixels unlike the background
    // over the share of its width, or a column over the share of its height.
    private static bool Bar(PhonePixels pixels, PhoneBounds box, uint background)
    {
        var known = new Dictionary<uint, bool>();
        bool Unlike(int x, int y)
        {
            var colour = pixels.At(x, y) & 0xFFFFFF;
            if (!known.TryGetValue(colour, out var unlike))
            {
                known[colour] = unlike = PhoneColours.Difference(colour, background) >= InkDifference;
            }

            return unlike;
        }

        for (var y = box.Top; y < box.Bottom; y++)
        {
            var count = 0;
            for (var x = box.Left; x < box.Right; x++)
            {
                count += Unlike(x, y) ? 1 : 0;
            }

            if (count >= LineShare * box.Width)
            {
                return true;
            }
        }

        for (var x = box.Left; x < box.Right; x++)
        {
            var count = 0;
            for (var y = box.Top; y < box.Bottom; y++)
            {
                count += Unlike(x, y) ? 1 : 0;
            }

            if (count >= LineShare * box.Height)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Excluded(IReadOnlyList<PhoneBounds> exclude, int x, int y)
    {
        foreach (var box in exclude)
        {
            if (x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Inside(PhoneBounds box, PhonePixels pixels) =>
        !box.IsEmpty && box.Left >= 0 && box.Top >= 0 && box.Right <= pixels.Width && box.Bottom <= pixels.Height;

    private static PhoneBounds? Joined(IReadOnlyList<PhoneBounds> boxes) =>
        boxes.Count == 0
            ? null
            : new PhoneBounds(boxes.Min(box => box.Left), boxes.Min(box => box.Top), boxes.Max(box => box.Right), boxes.Max(box => box.Bottom));
}

/// <summary>
/// The two checks of WCAG 1.4.1, use of colour (android-checks.md, step
/// 29), judged from <see cref="PhoneColourCues"/>'s measurements with no
/// model, so each failure is an error (owner, 2026-11-19: "If there is no
/// model involved, error"). A link or a selected item is told apart only
/// by colour when its colour, or its background's, differs from what it
/// is compared with, and none of these does: a difference in lightness of
/// 3:1 or more (G183, F73), an underline, bold strokes (1.3 times or more),
/// a background 3:1 or more apart, or, for a selected item, an underline
/// or bar the items beside it lack. No difference of colour is no finding
/// of these checks. Italics are not detected (owner, 2026-11-19).
/// </summary>
public sealed class PhoneColourOnlyRun
{
    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, int> _linksByVisit = [];
    private readonly Dictionary<PhoneVisit, int> _selectedByVisit = [];
    private readonly HashSet<PhoneVisit> _unrecorded = [];

    // Only a screenshot is measured (PhoneColourCues).
    private const string Screenshot = "; from the phone's screenshot";

    private PhoneColourOnlyRun()
    {
    }

    public static PhoneColourOnlyRun Empty { get; } = new();

    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The links measured, once for each settled snapshot they were on.</summary>
    public int Links { get; private set; }

    /// <summary>The links measured with no finding.</summary>
    public int LinksPassed { get; private set; }

    /// <summary>The selected items measured, once for each settled snapshot they were on.</summary>
    public int Selected { get; private set; }

    /// <summary>The selected items measured with no finding.</summary>
    public int SelectedPassed { get; private set; }

    /// <summary>The settled snapshots measured whose items' links were not recorded, by a companion app before protocol 8.</summary>
    public int LinksNotRecorded { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    public int LinksIn(PhoneVisit visit) => _linksByVisit.TryGetValue(visit, out var count) ? count : 0;

    public int SelectedIn(PhoneVisit visit) => _selectedByVisit.TryGetValue(visit, out var count) ? count : 0;

    /// <summary>Whether a visit's links were not recorded, by a companion app before protocol 8.</summary>
    public bool LinksNotRecordedIn(PhoneVisit visit) => _unrecorded.Contains(visit);

    public static PhoneColourOnlyRun Run(PhoneContrastRun measured, PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneColourOnlyRun();
        var findings = new Dictionary<(PhoneVisit, string, string), PhoneFinding>();
        foreach (var snapshot in measured.Snapshots)
        {
            var visit = snapshot.Visit;
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            if (snapshot.Window is { Items.Count: > 0 } window && window.Items.All(item => !item.LinksRecorded))
            {
                run.LinksNotRecorded++;
                run._unrecorded.Add(visit);
            }

            void Add(string check, PhoneCheckLevel level, PhoneCheckItem item, string identity, string label, PhoneBounds bounds, string text)
            {
                var key = (visit, check, identity);
                if (!findings.TryGetValue(key, out var finding))
                {
                    findings[key] = finding = new PhoneFinding(check, level, visit, identity, label);
                }

                if (finding._times.Count > 0 && finding._times[^1].Snapshot.EventId == snapshot.Settled.EventId)
                {
                    return;
                }

                finding._times.Add(new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, bounds, item.TreeKey,
                    text, state));
            }

            foreach (var link in snapshot.Links)
            {
                var identity = string.Create(CultureInfo.InvariantCulture, $"{link.Item.Identity} link {link.Number}");
                var label = $"link '{link.Link.Text}' in {link.Item.Label}";
                if (link.NotMeasured is { } why)
                {
                    Add(PhoneCheckNames.LinkOnlyByColour, PhoneCheckLevel.NotRun, link.Item, identity, label, link.Box, $"not measured, as {why}");
                    continue;
                }

                run.Links++;
                run._linksByVisit[visit] = run.LinksIn(visit) + 1;
                if (JudgeLink(link) is { } text)
                {
                    Add(PhoneCheckNames.LinkOnlyByColour, PhoneCheckLevel.Error, link.Item, identity, label, link.Box, text + Screenshot);
                }
                else
                {
                    run.LinksPassed++;
                }
            }

            foreach (var selected in snapshot.Selected)
            {
                if (selected.NotMeasured is { } why)
                {
                    Add(PhoneCheckNames.SelectedOnlyByColour, PhoneCheckLevel.NotRun, selected.Item, selected.Item.Identity, selected.Item.Label,
                        selected.Item.Bounds, $"not measured, as {why}");
                    continue;
                }

                run.Selected++;
                run._selectedByVisit[visit] = run.SelectedIn(visit) + 1;
                if (JudgeSelected(selected) is { } text)
                {
                    Add(PhoneCheckNames.SelectedOnlyByColour, PhoneCheckLevel.Error, selected.Item, selected.Item.Identity, selected.Item.Label,
                        selected.Item.Bounds, text + Screenshot);
                }
                else
                {
                    run.SelectedPassed++;
                }
            }
        }

        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Findings = [.. findings.Values
            .OrderBy(finding => screenOrder.TryGetValue(finding.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(finding => finding.Visit.Start)
            .ThenBy(finding => finding.Check, StringComparer.Ordinal)
            .ThenBy(finding => finding.Level)
            .ThenBy(finding => finding.Times[0].Time)
            .ThenBy(finding => finding.Times[0].Bounds.Top)
            .ThenBy(finding => finding.Times[0].Bounds.Left)];
        foreach (var finding in run.Findings)
        {
            if (!run._byVisit.TryGetValue(finding.Visit, out var ofVisit))
            {
                run._byVisit[finding.Visit] = ofVisit = [];
            }

            ofVisit.Add(finding);
        }

        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>What makes a measured link an error, or null for no finding.</summary>
    public static string? JudgeLink(PhoneLinkLook link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.Own is not { Ink: { } ink } own || link.Around is not { Ink: { } text } around)
        {
            return null;
        }

        var inkDiffers = PhoneColours.Difference(ink, text) >= PhoneColours.JoinBelow;
        var backgroundDiffers = PhoneColours.Difference(own.Background, around.Background) >= PhoneColours.JoinBelow;
        if (!inkDiffers && !backgroundDiffers)
        {
            return null;
        }

        var ratio = RatioOf(ink, text);
        var backgrounds = RatioOf(own.Background, around.Background);
        var stroke = around.Stroke > 0 ? own.Stroke / around.Stroke : 0;
        if (ratio >= PhoneColourCues.LightnessLimit || link.Underlined || stroke >= PhoneColourCues.BoldRatio ||
            backgrounds >= PhoneColourCues.LightnessLimit)
        {
            return null;
        }

        var colours = $"its colour, {PhoneColours.Hex(ink)}, against the text's around it, {PhoneColours.Hex(text)}, is {Shown(ratio)}:1, under the 3:1 a colour alone needs";
        var background = backgroundDiffers
            ? $"on {PhoneColours.Hex(own.Background)}, against the text's {PhoneColours.Hex(around.Background)}, {Shown(backgrounds)}:1"
            : $"on the same background, {PhoneColours.Hex(own.Background)}";
        return $"{colours}; it is not underlined, nor bold (its strokes {Shown(stroke)} times as wide as the text's), and is {background}; " +
            "so it is told apart from the text only by colour";
    }

    /// <summary>What makes a measured selected item an error, or null for no finding.</summary>
    public static string? JudgeSelected(PhoneSelectedLook selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.Own is not { } own || selected.Theirs is not { } theirs)
        {
            return null;
        }

        var inkDiffers = own.Ink is { } a && theirs.Ink is { } b && PhoneColours.Difference(a, b) >= PhoneColours.JoinBelow;
        var backgroundDiffers = PhoneColours.Difference(own.Background, theirs.Background) >= PhoneColours.JoinBelow;
        if (!inkDiffers && !backgroundDiffers)
        {
            return null;
        }

        var inkRatio = own.Ink is { } ownInk && theirs.Ink is { } theirInk ? RatioOf(ownInk, theirInk) : 1;
        var backgrounds = RatioOf(own.Background, theirs.Background);
        var stroke = theirs.Stroke > 0 ? own.Stroke / theirs.Stroke : 0;
        if (inkRatio >= PhoneColourCues.LightnessLimit || backgrounds >= PhoneColourCues.LightnessLimit ||
            stroke >= PhoneColourCues.BoldRatio || (selected.Underlined && !selected.OthersUnderlined) || (selected.Bar && !selected.OthersBar))
        {
            return null;
        }

        var others = selected.Others == 1 ? "the item beside it not selected" : string.Create(CultureInfo.InvariantCulture, $"the {selected.Others} items beside it not selected");
        var lack = selected.Others == 1 ? "it lacks" : "they lack";
        var theirWord = selected.Others == 1 ? "its" : "theirs";
        var drawn = $"drawn in {Ink(own.Ink)} on {PhoneColours.Hex(own.Background)}, where {others} {(selected.Others == 1 ? "is" : "are")} in {Ink(theirs.Ink)} on {PhoneColours.Hex(theirs.Background)}";
        var ratios = inkDiffers && backgroundDiffers
            ? $"{Shown(inkRatio)}:1 and {Shown(backgrounds)}:1 on its background"
            : inkDiffers ? $"{Shown(inkRatio)}:1" : $"{Shown(backgrounds)}:1 on its background";
        return $"marked selected in the tree, and {drawn}: {ratios}, under 3:1; it has no underline or bar {lack}, " +
            $"and is not bold (its strokes {Shown(stroke)} times as wide as {theirWord}); so its state is shown only by colour";

        static string Ink(uint? ink) => ink is { } colour ? PhoneColours.Hex(colour) : "no ink";
    }

    private static double RatioOf(uint first, uint second) =>
        PhoneColours.Ratio(PhoneColours.Luminance(first), PhoneColours.Luminance(second));

    private static string Shown(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
