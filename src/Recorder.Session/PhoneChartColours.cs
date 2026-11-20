using System.Diagnostics;
using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// One colour filling part of an item, as measured on the phone's
/// screenshot (android-checks.md, step 32, rule 2): the colour, its share
/// of the item's box, the pieces it is drawn in of at least
/// <see cref="PhoneChartMeasuring.LeastPiece"/> pixels, and the median
/// share of its bounding box such a piece fills.
/// </summary>
public sealed record PhoneChartFill(uint Colour, double Share, int Pieces, double MedianFill)
{
    public string Hex => PhoneColours.Hex(Colour);
}

/// <summary>
/// An item measured for the check of a chart that relies on colour (step
/// 32, rule 1): why it was not measured, or null; the commonest colour in
/// its box, taken as its background; and its fills, the colours other
/// than that each covering at least
/// <see cref="PhoneChartMeasuring.LeastShare"/> of its box, the commonest
/// first.
/// </summary>
public sealed record PhoneChartLook(PhoneCheckItem Item, string? NotMeasured, uint Background = 0, IReadOnlyList<PhoneChartFill>? MeasuredFills = null)
{
    public IReadOnlyList<PhoneChartFill> Fills => MeasuredFills ?? [];

    /// <summary>
    /// Whether the item is for the model to judge (rule 1): measured, with
    /// two or more fills, at least one of them not grey.
    /// </summary>
    public bool Asked => NotMeasured is null && Fills.Count >= 2 && Fills.Any(fill => !PhoneChartMeasuring.Grey(fill.Colour));
}

/// <summary>
/// The measuring of the check of a chart that relies on colour
/// (android-checks.md, step 32, rules 1 and 2), with no model, on the
/// phone's screenshot only, as the video's colours are approximate. The
/// thresholds are the probe's of 2026-11-20 on the chart screens' tour:
/// the smallest fill built covered 1.3% of its chart's box, and the blends
/// at a bitmap's scaled edges at most 0.6%; a solid series was three to
/// five pieces each filling its bounding box, a hatched one 30 to 47 with
/// a median fill of 0.10 to 0.28.
/// </summary>
public static class PhoneChartMeasuring
{
    /// <summary>An item is measured only if it is at least this many dp wide and tall (rule 1).</summary>
    public const int LeastDp = 48;

    /// <summary>A colour is a fill if it covers at least this share of the item's box (rule 1).</summary>
    public const double LeastShare = 0.01;

    /// <summary>A piece of a fill is counted if it is at least this many pixels (rule 2).</summary>
    public const int LeastPiece = 40;

    /// <summary>A colour whose channels differ by less than this is grey, white or black (rule 1).</summary>
    public const int GreyBelow = 24;

    /// <summary>The most fills given for an item, the commonest first.</summary>
    public const int MostFills = 8;

    /// <summary>
    /// The window's items to measure (rule 1), each measured on the
    /// screenshot, or with why not: visible, with bounds, with no text of
    /// their own and no visible items inside them, and at least
    /// <see cref="LeastDp"/> dp wide and tall. Nothing is measured where
    /// only a frame of the video shows the screen (<paramref name="pixels"/>
    /// null). An item with fewer than two fills is left out.
    /// </summary>
    public static IReadOnlyList<PhoneChartLook> Charts(
        PhoneCheckWindow window,
        PhonePixels? pixels,
        PictureScale scale,
        double pixelsPerDp,
        PhoneFocusOutline? outline,
        IReadOnlyList<PhoneWindowAbove> above)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(above);
        var looks = new List<PhoneChartLook>();
        if (pixels is null)
        {
            return looks;
        }

        var least = (int)Math.Ceiling(LeastDp * pixelsPerDp);
        foreach (var item in window.Items.Where(entry => IsCandidate(entry, least)))
        {
            if (PhoneCover.Why(item.Bounds, item, outline, above) is { } covered)
            {
                looks.Add(new PhoneChartLook(item, covered));
                continue;
            }

            var box = scale.ToPicture(item.Bounds);
            if (box.Left < 0 || box.Top < 0 || box.Right > pixels.Width || box.Bottom > pixels.Height || box.IsEmpty)
            {
                looks.Add(new PhoneChartLook(item, "it is not wholly on the picture"));
                continue;
            }

            if (Measure(item, pixels, box) is { Fills.Count: >= 2 } look)
            {
                looks.Add(look);
            }
        }

        return looks;
    }

    /// <summary>Whether a colour is grey, white or black: its channels differ by less than <see cref="GreyBelow"/>.</summary>
    public static bool Grey(uint rgb)
    {
        var (red, green, blue) = ((int)((rgb >> 16) & 0xFF), (int)((rgb >> 8) & 0xFF), (int)(rgb & 0xFF));
        return Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) < GreyBelow;
    }

    /// <summary>The contrast ratio of two colours, unrounded.</summary>
    public static double RatioOf(uint first, uint second) =>
        PhoneColours.Ratio(PhoneColours.Luminance(first), PhoneColours.Luminance(second));

    /// <summary>An item's fills on a box of the picture (rule 2); the box is wholly on it.</summary>
    internal static PhoneChartLook Measure(PhoneCheckItem item, PhonePixels pixels, PhoneBounds box)
    {
        var counts = new Dictionary<uint, int>();
        for (var y = box.Top; y < box.Bottom; y++)
        {
            for (var x = box.Left; x < box.Right; x++)
            {
                var colour = pixels.At(x, y) & 0xFFFFFF;
                counts[colour] = counts.TryGetValue(colour, out var count) ? count + 1 : 1;
            }
        }

        var area = (double)box.Width * box.Height;
        var ordered = counts.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).ToList();
        var background = ordered[0].Key;
        var fills = ordered
            .Skip(1)
            .Where(entry => entry.Value >= LeastShare * area)
            .Take(MostFills)
            .Select(entry =>
            {
                var (pieces, median) = Pieces(pixels, box, entry.Key);
                return new PhoneChartFill(entry.Key, entry.Value / area, pieces, median);
            })
            .ToList();
        return new PhoneChartLook(item, null, background, fills);
    }

    // The pieces of a colour in a box, four-connected, of at least
    // LeastPiece pixels, and the median share of its bounding box a piece
    // fills.
    private static (int Pieces, double MedianFill) Pieces(PhonePixels pixels, PhoneBounds box, uint colour)
    {
        var (width, height) = (box.Width, box.Height);
        var seen = new bool[width * height];
        var fills = new List<double>();
        var stack = new Stack<int>();
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start] || !Is(start))
            {
                continue;
            }

            seen[start] = true;
            stack.Push(start);
            int size = 0, left = width, top = height, right = -1, bottom = -1;
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                var (x, y) = (at % width, at / width);
                size++;
                (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            if (size >= LeastPiece)
            {
                fills.Add(size / (double)((right - left + 1) * (bottom - top + 1)));
            }
        }

        if (fills.Count == 0)
        {
            return (0, 0);
        }

        fills.Sort();
        var middle = fills.Count / 2;
        return (fills.Count, fills.Count % 2 == 1 ? fills[middle] : (fills[middle - 1] + fills[middle]) / 2);

        bool Is(int at) => (pixels.At(box.Left + (at % width), box.Top + (at / width)) & 0xFFFFFF) == colour;

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            var at = (y * width) + x;
            if (!seen[at] && Is(at))
            {
                seen[at] = true;
                stack.Push(at);
            }
        }
    }

    // Rule 1: visible, with bounds of at least the least size, with no
    // text of its own and no visible item inside it.
    private static bool IsCandidate(PhoneCheckItem item, int least) =>
        item.Node.VisibleToUser && !item.Bounds.IsEmpty &&
        item.Bounds.Width >= least && item.Bounds.Height >= least &&
        string.IsNullOrWhiteSpace(item.Text) &&
        !item.Children.Any(child => child.Node.VisibleToUser && !child.Bounds.IsEmpty);
}

/// <summary>
/// A line of text near an item for the model (rule 3): its words, where it
/// is against the item, whether OCR read it, and its colour where it was
/// measured.
/// </summary>
public sealed record PhoneChartText(string Text, string Where, bool FromOcr, uint? Colour);

/// <summary>
/// One item a model is to be asked about (android-checks.md, step 32,
/// rule 3): asked once for each screen and item, from the first time it
/// was measured, with that visit, the item's identity, the picture and its
/// bounds there, its class, its description, the area of the screen
/// around it given as picture B, its background and fills, and the text
/// near it.
/// </summary>
public sealed record PhoneChartQuestion(
    PhoneVisit Visit,
    string Identity,
    PhoneFindingTime Time,
    PhoneContrastPicture Picture,
    string Role,
    string? Description,
    PhoneBounds Area,
    uint Background,
    IReadOnlyList<PhoneChartFill> Fills,
    IReadOnlyList<PhoneChartText> Near,
    bool UsedOcr)
{
    public string App => Visit.Screen.Package;

    public string ScreenName => Visit.Screen.Name;

    public PhoneBounds Bounds => Time.Bounds;
}

/// <summary>One item as the check found it in a visit, once, with its question.</summary>
public sealed record PhoneChartEntry(PhoneVisit Visit, string Identity, PhoneChartQuestion Question);

/// <summary>What the model's answers made of one item.</summary>
public enum PhoneChartOutcome
{
    /// <summary>Judged not to be a chart or other graphic whose colours carry information: counted, no finding (rule 4).</summary>
    NotAChart,

    /// <summary>Judged to show what its colours stand for in another visible way too: counted, no finding (rule 4).</summary>
    NotOnlyColour,

    /// <summary>A warning: judged to rely on colour alone, or the model could not tell, or gave two answers (rule 4).</summary>
    Finding,
}

/// <summary>One item's answer: what it made of the item, and the model's inference.</summary>
public sealed record PhoneChartAnswer(PhoneChartOutcome Outcome, PhoneInference Inference);

/// <summary>
/// Step 32 of android-checks.md, a chart that relies on colour (WCAG
/// 1.4.1, F13, G111). The owner chose on 2026-11-20 that only a model can
/// judge whether an item is a chart, what its colours stand for and
/// whether that can be told without colour: (1) each visible item with no
/// text of its own and no visible item inside it, at least 48 dp wide and
/// tall, shown whole on the phone's screenshot with nothing drawn over it,
/// whose box holds two or more fills, colours other than its commonest
/// each covering at least 1% of it, one or more of them not grey;
/// (2) each fill's share, its pieces and their median fill of their
/// bounding boxes, and each pair's contrast ratio, are measured as
/// evidence; (3) each such item is asked once for each screen, with the
/// whole screen with the item outlined, the item and the screen above and
/// below it, its class and description, the fills and ratios, and the text
/// near it from the tree and OCR, with four answers, reason first;
/// (4) agreed only-colour is put to the model for complex questions to
/// confirm, as are unsettled answers, whose answers decide: agreed
/// only-colour a warning for a person to verify; cannot tell or two
/// answers a warning to check manually; agreed not-a-chart or
/// not-only-colour counted, no finding. With no model nothing is judged
/// and no finding is made; a check that involves a model gives warnings
/// (owner, 2026-11-19).
/// </summary>
public sealed class PhoneChartColourRun
{
    /// <summary>The most lines of text near an item given in its question.</summary>
    public const int MostNearLines = 60;

    /// <summary>The most characters of a line near an item given in its question.</summary>
    public const int MostLineCharacters = 120;

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly List<PhoneChartEntry> _entries = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];
    private readonly Dictionary<PhoneChartEntry, List<PhoneFindingTime>> _times = [];
    private readonly Dictionary<PhoneChartEntry, string> _labels = [];

    private PhoneChartColourRun()
    {
    }

    public static PhoneChartColourRun Empty { get; } = new();

    /// <summary>The warnings the model's answers made (rule 4).</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The settled snapshots measured on a screenshot.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>The settled snapshots that only a frame of the video shows, not measured.</summary>
    public int FromVideo { get; private set; }

    /// <summary>The items with two or more fills, once a visit (rule 1).</summary>
    public int Items => _entries.Count;

    /// <summary>The items not measured, as something was drawn over them or they were not wholly on the picture, once a visit.</summary>
    public int NotMeasured { get; private set; }

    /// <summary>Every item asked about, once a visit, in the order found.</summary>
    public IReadOnlyList<PhoneChartEntry> Entries => _entries;

    /// <summary>The items to be asked about, once for each screen and item, in screen order (rule 3).</summary>
    public IReadOnlyList<PhoneChartQuestion> Questions { get; private set; } = [];

    /// <summary>The questions answered, with what each answer made of its item; empty until a model's answers are given.</summary>
    public IReadOnlyDictionary<PhoneChartQuestion, PhoneChartAnswer> Answers { get; private set; } =
        new Dictionary<PhoneChartQuestion, PhoneChartAnswer>();

    /// <summary>The items judged not to be charts, not confirmed, once for each screen.</summary>
    public int NotCharts => Answers.Values.Count(answer => answer.Outcome == PhoneChartOutcome.NotAChart);

    /// <summary>The charts judged to show what their colours stand for another way too, not confirmed, once for each screen.</summary>
    public int NotOnlyColour => Answers.Values.Count(answer => answer.Outcome == PhoneChartOutcome.NotOnlyColour);

    /// <summary>The questions with no answer, not judged.</summary>
    public int Unanswered => Questions.Count - Answers.Count;

    /// <summary>Every inference the model's answers made, findings or not, so that a report says AI was used.</summary>
    public IEnumerable<PhoneInference> Inferences => Answers.Values.Select(answer => answer.Inference);

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether a screenshot of the visit was measured, so its items were checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    /// <summary>The items asked about in a visit, once each.</summary>
    public IEnumerable<PhoneChartEntry> In(PhoneVisit visit) => _entries.Where(entry => entry.Visit == visit);

    /// <summary>Runs rules 1 to 3 on a measurement; no model is used and no finding is made.</summary>
    public static PhoneChartColourRun Run(PhoneContrastRun measured, PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneChartColourRun();
        var entries = new Dictionary<(PhoneVisit, string), PhoneChartEntry>();
        var notMeasured = new HashSet<(PhoneVisit, string)>();
        var questions = new Dictionary<(PhoneScreen, string), PhoneChartQuestion>();
        foreach (var snapshot in measured.Snapshots)
        {
            if (snapshot.Window is not { } window)
            {
                continue;
            }

            if (snapshot.Picture.Kind != PhonePictureKind.Screenshot)
            {
                run.FromVideo++;
                continue;
            }

            run.SnapshotsRead++;
            var visit = snapshot.Visit;
            run._visitsRead.Add(visit);
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            foreach (var look in snapshot.Charts)
            {
                var item = look.Item;
                if (look.NotMeasured is not null)
                {
                    if (!entries.ContainsKey((visit, item.Identity)))
                    {
                        notMeasured.Add((visit, item.Identity));
                    }

                    continue;
                }

                if (!look.Asked)
                {
                    continue;
                }

                notMeasured.Remove((visit, item.Identity));
                var time = new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, item.Bounds, item.TreeKey,
                    Measured(look), state);
                if (!entries.TryGetValue((visit, item.Identity), out var entry))
                {
                    if (!questions.TryGetValue((visit.Screen, item.Identity), out var question))
                    {
                        // Rule 3: once for the screen and item, from the first time measured.
                        var area = AreaOf(item.Bounds, window);
                        questions[(visit.Screen, item.Identity)] = question = new PhoneChartQuestion(
                            visit, item.Identity, time, snapshot.Picture, Role(item),
                            string.IsNullOrWhiteSpace(item.Node.ContentDescription) ? null : item.Node.ContentDescription.Trim(),
                            area, look.Background, look.Fills, NearText(snapshot.Lines, item, area), !string.IsNullOrEmpty(snapshot.Engine));
                    }

                    entry = new PhoneChartEntry(visit, item.Identity, question);
                    entries[(visit, item.Identity)] = entry;
                    run._entries.Add(entry);
                    run._times[entry] = [];
                    run._labels[entry] = item.Label;
                }

                if (run._times[entry].Count == 0 || run._times[entry][^1].Snapshot.EventId != snapshot.Settled.EventId)
                {
                    run._times[entry].Add(time);
                }
            }
        }

        run.NotMeasured = notMeasured.Count;
        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Questions = [.. questions.Values
            .OrderBy(question => screenOrder.TryGetValue(question.Visit.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(question => question.Visit.Start)
            .ThenBy(question => question.Time.Time)
            .ThenBy(question => question.Bounds.Top)
            .ThenBy(question => question.Bounds.Left)];
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// The run with the model's answers given: each answer that makes a
    /// finding (rule 4) is a finding on every visit where the item was
    /// asked about on its screen, at the answer's level, with every time it
    /// was measured in the visit; any other answer is counted, not a
    /// finding. The note says what became of the questions.
    /// </summary>
    public PhoneChartColourRun Judged(IReadOnlyDictionary<PhoneChartQuestion, PhoneChartAnswer> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneChartColourRun
        {
            SnapshotsRead = SnapshotsRead,
            FromVideo = FromVideo,
            NotMeasured = NotMeasured,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        judged._entries.AddRange(_entries);
        judged._visitsRead.UnionWith(_visitsRead);
        foreach (var (entry, times) in _times)
        {
            judged._times[entry] = times;
        }

        foreach (var (entry, label) in _labels)
        {
            judged._labels[entry] = label;
        }

        var kept = new Dictionary<PhoneChartQuestion, PhoneChartAnswer>();
        foreach (var question in Questions)
        {
            if (answers.TryGetValue(question, out var answer))
            {
                kept[question] = answer;
            }
        }

        var findings = new List<PhoneFinding>();
        foreach (var entry in _entries)
        {
            if (kept.TryGetValue(entry.Question, out var answer) && answer.Outcome == PhoneChartOutcome.Finding)
            {
                var finding = new PhoneFinding(PhoneCheckNames.ChartOnlyByColour, answer.Inference.Level, entry.Visit, entry.Identity, judged._labels[entry])
                {
                    Inference = answer.Inference,
                };
                finding._times.AddRange(judged._times[entry]);
                findings.Add(finding);
            }
        }

        judged.Answers = kept;
        judged.Findings = findings;
        foreach (var finding in findings)
        {
            if (!judged._byVisit.TryGetValue(finding.Visit, out var ofVisit))
            {
                judged._byVisit[finding.Visit] = ofVisit = [];
            }

            ofVisit.Add(finding);
        }

        return judged;
    }

    /// <summary>
    /// The fills and the pairs' contrast ratios, as a finding and a
    /// question give them (rule 2), such as "#C62828, 13% of it, in 4
    /// pieces filling a median 100% of their bounding boxes".
    /// </summary>
    public static string FillsText(IReadOnlyList<PhoneChartFill> fills)
    {
        ArgumentNullException.ThrowIfNull(fills);
        return string.Join("; ", fills.Select(fill => string.Create(CultureInfo.InvariantCulture,
            $"{fill.Hex}, {Percent(fill.Share)} of it, in {fill.Pieces} {(fill.Pieces == 1 ? "piece" : "pieces")} of {PhoneChartMeasuring.LeastPiece} pixels or more{(fill.Pieces == 0 ? "" : $" filling a median {Percent(fill.MedianFill)} of {(fill.Pieces == 1 ? "its bounding box" : "their bounding boxes")}")}")));
    }

    /// <summary>Each pair of fills' contrast ratio, such as "#C62828 and #2E7D32 1.10:1".</summary>
    public static string RatiosText(IReadOnlyList<PhoneChartFill> fills)
    {
        ArgumentNullException.ThrowIfNull(fills);
        var pairs = new List<string>();
        for (var first = 0; first < fills.Count; first++)
        {
            for (var second = first + 1; second < fills.Count; second++)
            {
                pairs.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{fills[first].Hex} and {fills[second].Hex} {PhoneChartMeasuring.RatioOf(fills[first].Colour, fills[second].Colour):0.00}:1"));
            }
        }

        return string.Join("; ", pairs);
    }

    /// <summary>The item's class, its last part, for rule 3.</summary>
    internal static string Role(PhoneCheckItem item)
    {
        var className = item.Node.ClassName;
        return string.IsNullOrEmpty(className) ? "View" : className[(className.LastIndexOf('.') + 1)..];
    }

    /// <summary>
    /// The area of the screen given with an item (rule 3): the item, and as
    /// much again of its height above and below it, across the window's
    /// width, where a legend, a title or labels may be.
    /// </summary>
    internal static PhoneBounds AreaOf(PhoneBounds bounds, PhoneCheckWindow window)
    {
        var roots = window.Items.Where(entry => entry.Parent is null && !entry.Bounds.IsEmpty).Select(entry => entry.Bounds).ToList();
        var within = roots.Count == 0
            ? bounds
            : new PhoneBounds(roots.Min(root => root.Left), roots.Min(root => root.Top), roots.Max(root => root.Right), roots.Max(root => root.Bottom));
        var left = Math.Min(bounds.Left, within.Left);
        var right = Math.Max(bounds.Right, within.Right);
        var top = Math.Max(Math.Min(bounds.Top, within.Top), bounds.Top - bounds.Height);
        var bottom = Math.Min(Math.Max(bounds.Bottom, within.Bottom), bounds.Bottom + bounds.Height);
        return new PhoneBounds(left, top, right, bottom);
    }

    /// <summary>
    /// The lines of text in an item's area (rule 3), top to bottom: OCR's,
    /// and the tree's where OCR read none; not the item's own description,
    /// measured on its bounds with no OCR, as it is not shown. At most
    /// <see cref="MostNearLines"/>.
    /// </summary>
    internal static IReadOnlyList<PhoneChartText> NearText(IReadOnlyList<PhoneTextLine> lines, PhoneCheckItem item, PhoneBounds area)
    {
        var near = new List<PhoneChartText>();
        foreach (var line in lines.OrderBy(line => line.Box.Top).ThenBy(line => line.Box.Left))
        {
            var ocr = !string.IsNullOrWhiteSpace(line.OcrText);
            var text = (ocr ? line.OcrText : line.AppText)?.Trim();
            if (string.IsNullOrEmpty(text) || (!ocr && ReferenceEquals(line.Item, item)) || !Middle(line.Box, area))
            {
                continue;
            }

            if (text.Length > MostLineCharacters)
            {
                text = text[..(MostLineCharacters - 3)] + "...";
            }

            near.Add(new PhoneChartText(text, Where(line.Box, item.Bounds), ocr, line.Colours?.Foreground));
            if (near.Count == MostNearLines)
            {
                break;
            }
        }

        return near;
    }

    // Where a line is against the item: inside it, or above, below, left
    // of or right of it.
    private static string Where(PhoneBounds line, PhoneBounds item)
    {
        var x = (line.Left + line.Right) / 2.0;
        var y = (line.Top + line.Bottom) / 2.0;
        if (x >= item.Left && x <= item.Right && y >= item.Top && y <= item.Bottom)
        {
            return "inside the item";
        }

        if (y < item.Top)
        {
            return "above the item";
        }

        if (y > item.Bottom)
        {
            return "below the item";
        }

        return x < item.Left ? "left of the item" : "right of the item";
    }

    private static bool Middle(PhoneBounds line, PhoneBounds area)
    {
        var x = (line.Left + line.Right) / 2.0;
        var y = (line.Top + line.Bottom) / 2.0;
        return x >= area.Left && x <= area.Right && y >= area.Top && y <= area.Bottom;
    }

    private static string Percent(double share) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(share * 100, share < 0.1 ? 1 : 0)}%");

    // What was measured of an item (rule 2), for its findings.
    private static string Measured(PhoneChartLook look)
    {
        var ratios = PhoneChartColourRun.RatiosText(look.Fills);
        return string.Create(CultureInfo.InvariantCulture,
            $"an item of class {Role(look.Item)} with no text of its own, on {PhoneColours.Hex(look.Background)}, with {look.Fills.Count} colours: {FillsText(look.Fills)}; contrast ratios between them: {ratios}; from the phone's screenshot");
    }
}
