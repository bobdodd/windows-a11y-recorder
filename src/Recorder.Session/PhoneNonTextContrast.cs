using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Recorder.Session;

/// <summary>A side of a control's bounds.</summary>
public enum PhoneSide
{
    Top,
    Left,
    Bottom,
    Right,
}

/// <summary>
/// One side of a control as drawn: the colour next to it, outside the
/// bounds, and the first colour drawn that differs from it, scanning in
/// from just outside the bounds (a border or a fill), with their ratio;
/// <paramref name="Shape"/> is null when nothing is drawn there.
/// </summary>
public sealed record PhoneControlSide(PhoneSide Side, uint Adjacent, uint? Shape, double Ratio);

/// <summary>
/// What step 5 of android-checks.md measured of a control on a settled
/// snapshot's picture: its sides that could be measured, the commonest
/// colour inside it (its fill), its most contrasting colour with enough
/// pixels against that fill (its icon), and the words OCR read in it, or,
/// when OCR was not used, the app's text in it; and why it is not judged,
/// when something is drawn over it (<see cref="PhoneCover"/>).
/// </summary>
public sealed record PhoneControlMeasure(
    PhoneCheckItem Item,
    IReadOnlyList<PhoneControlSide> Sides,
    uint Fill,
    uint? Icon,
    double IconRatio,
    string? VisibleText,
    bool TextFromOcr,
    string? Covered = null)
{
    /// <summary>The side of least contrast: one with nothing drawn, else the lowest ratio; null when no side was measured.</summary>
    public PhoneControlSide? Weakest => Sides.Count == 0
        ? null
        : Sides.OrderBy(side => side.Shape is null ? 0 : 1).ThenBy(side => side.Ratio).First();

    /// <summary>The boundary's ratio: 1 when a side has nothing drawn, as the boundary is not seen there.</summary>
    public double BoundaryRatio => Weakest is { } side ? side.Shape is null ? 1.0 : side.Ratio : 1.0;
}

/// <summary>
/// The measurement for non-text contrast (android-checks.md, step 5), on a
/// picture's pixels: distances are in the screen's pixels, turned into the
/// picture's by its scale.
/// </summary>
public static class PhoneControlMeasuring
{
    // A side is measured along its middle half, to miss rounded corners.
    // The colour next to it is the commonest 5 to 8 pixels outside; the
    // drawn shape is looked for from 4 pixels outside to 16 inside, and is
    // the colour of highest contrast in its first 4 pixels, to pass over
    // the anti-aliased edge (W3C: the colours set, not those rendered).
    private const int AdjacentFrom = -8;
    private const int AdjacentTo = -5;
    private const int ScanFrom = -4;
    private const int ScanTo = 16;
    private const int ShapeDepth = 3;

    // An icon's colour has at least this share of the control's pixels,
    // and at least this many pixels.
    internal const double IconShare = 0.005;
    internal const int IconPixels = 9;

    private const int ScrollForwardAction = 0x1000;
    private const int ScrollBackwardAction = 0x2000;

    /// <summary>Whether an item is a control: visible, enabled (1.4.11 excepts inactive ones), and one that can be acted on.</summary>
    public static bool IsControl(PhoneCheckItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var node = item.Node;
        return node.VisibleToUser && node.Enabled && !item.Bounds.IsEmpty &&
            (node.Clickable || node.LongClickable || node.Checkable || node.Editable);
    }

    /// <summary>
    /// Measures the window's controls whose middle is in the app's window
    /// and under no window above it. <paramref name="lines"/> are the lines
    /// of text measured on the picture; <paramref name="ocr"/> says whether
    /// OCR read them.
    /// </summary>
    public static IReadOnlyList<PhoneControlMeasure> Measure(
        PhoneCheckWindow window,
        PhoneBounds windowBounds,
        IReadOnlyList<PhoneBounds> above,
        IReadOnlyList<PhoneTextLine> lines,
        bool ocr,
        PhonePixels pixels,
        PictureScale scale)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(windowBounds);
        ArgumentNullException.ThrowIfNull(above);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(pixels);
        var measures = new List<PhoneControlMeasure>();
        foreach (var item in window.Items.Where(IsControl))
        {
            var bounds = item.Bounds;
            var x = (bounds.Left + bounds.Right) / 2;
            var y = (bounds.Top + bounds.Bottom) / 2;
            if (!Inside(windowBounds, x, y) || above.Any(other => Inside(other, x, y)))
            {
                continue;
            }

            var sides = new List<PhoneControlSide>();
            foreach (var side in Enum.GetValues<PhoneSide>())
            {
                if (Measurable(item, side, windowBounds, above) && MeasureSide(pixels, scale, bounds, side) is { } measured)
                {
                    sides.Add(measured);
                }
            }

            var box = scale.ToPicture(bounds);
            var counts = Joined(pixels, Inset(box, Scaled(2, scale)));
            if (counts.Count == 0)
            {
                continue;
            }

            var fill = counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
            var total = counts.Values.Sum();
            var least = Math.Max(IconPixels, (int)Math.Ceiling(total * IconShare));
            uint? icon = null;
            var iconRatio = 1.0;
            foreach (var (colour, count) in counts)
            {
                var ratio = RatioOf(colour, fill);
                if (colour != fill && count >= least && ratio > iconRatio)
                {
                    icon = colour;
                    iconRatio = ratio;
                }
            }

            measures.Add(new PhoneControlMeasure(item, sides, fill, icon, iconRatio, TextIn(item, lines, ocr), ocr));
        }

        return measures;
    }

    // The words seen in a control: with OCR, the lines OCR read whose middle
    // is in it; without, the app's text of it or an item inside it.
    private static string? TextIn(PhoneCheckItem control, IReadOnlyList<PhoneTextLine> lines, bool ocr)
    {
        var bounds = control.Bounds;
        if (ocr)
        {
            var words = lines
                .Where(line => line.Place != PhoneTextPlace.AppTextOnItemBounds && !string.IsNullOrWhiteSpace(line.OcrText) &&
                    Inside(bounds, (line.Box.Left + line.Box.Right) / 2, (line.Box.Top + line.Box.Bottom) / 2))
                .OrderBy(line => line.Box.Top)
                .ThenBy(line => line.Box.Left)
                .Select(line => line.OcrText!)
                .ToList();
            return words.Count == 0 ? null : string.Join(' ', words);
        }

        var stack = new Stack<PhoneCheckItem>([control]);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            var text = new[] { item.Text, item.Node.ContentDescription }
                .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part) && part.Any(char.IsLetterOrDigit));
            if (text is not null && item.Node.VisibleToUser)
            {
                return text;
            }

            foreach (var child in item.Children)
            {
                stack.Push(child);
            }
        }

        return null;
    }

    // A side is measured when the pixels outside it are the app's: in its
    // window, under no window above, and not where a list that scrolls
    // cuts the control off (ATF's "against a scrollable edge").
    private static bool Measurable(PhoneCheckItem item, PhoneSide side, PhoneBounds windowBounds, IReadOnlyList<PhoneBounds> above)
    {
        var bounds = item.Bounds;
        var (x, y) = side switch
        {
            PhoneSide.Top => ((bounds.Left + bounds.Right) / 2, bounds.Top + AdjacentFrom),
            PhoneSide.Bottom => ((bounds.Left + bounds.Right) / 2, bounds.Bottom - 1 - AdjacentFrom),
            PhoneSide.Left => (bounds.Left + AdjacentFrom, (bounds.Top + bounds.Bottom) / 2),
            _ => (bounds.Right - 1 - AdjacentFrom, (bounds.Top + bounds.Bottom) / 2),
        };
        if (!Inside(windowBounds, x, y) || above.Any(other => Inside(other, x, y)))
        {
            return false;
        }

        for (var ancestor = item.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!ancestor.Node.Scrollable && !ancestor.Node.Actions.Any(action => action.Id is ScrollForwardAction or ScrollBackwardAction))
            {
                continue;
            }

            var list = ancestor.Bounds;
            var cut = side switch
            {
                PhoneSide.Top => bounds.Top <= list.Top,
                PhoneSide.Bottom => bounds.Bottom >= list.Bottom,
                PhoneSide.Left => bounds.Left <= list.Left,
                _ => bounds.Right >= list.Right,
            };
            if (cut)
            {
                return false;
            }
        }

        return true;
    }

    private static PhoneControlSide? MeasureSide(PhonePixels pixels, PictureScale scale, PhoneBounds bounds, PhoneSide side)
    {
        var box = scale.ToPicture(bounds);
        var horizontal = side is PhoneSide.Top or PhoneSide.Bottom;
        var length = horizontal ? box.Width : box.Height;
        var from = (horizontal ? box.Left : box.Top) + length / 4;
        var to = (horizontal ? box.Left : box.Top) + length - length / 4;
        if (to <= from)
        {
            return null;
        }

        uint? At(int depth)
        {
            var offset = (int)Math.Round(depth * (horizontal ? scale.Y : scale.X));
            var line = side switch
            {
                PhoneSide.Top => box.Top + offset,
                PhoneSide.Bottom => box.Bottom - 1 - offset,
                PhoneSide.Left => box.Left + offset,
                _ => box.Right - 1 - offset,
            };
            var counts = new Dictionary<uint, int>();
            for (var along = from; along < to; along++)
            {
                var (px, py) = horizontal ? (along, line) : (line, along);
                if (px < 0 || py < 0 || px >= pixels.Width || py >= pixels.Height)
                {
                    return null;
                }

                var colour = pixels.At(px, py) & 0xFFFFFF;
                counts[colour] = counts.TryGetValue(colour, out var count) ? count + 1 : 1;
            }

            return counts.Count == 0 ? null : counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
        }

        var outside = new List<uint>();
        for (var depth = AdjacentFrom; depth <= AdjacentTo; depth++)
        {
            if (At(depth) is not { } colour)
            {
                return null;
            }

            outside.Add(colour);
        }

        var adjacent = outside.GroupBy(colour => colour).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
        for (var depth = ScanFrom; depth <= ScanTo; depth++)
        {
            if (At(depth) is not { } colour || PhoneColours.Difference(colour, adjacent) < PhoneColours.JoinBelow)
            {
                continue;
            }

            var shape = colour;
            var ratio = RatioOf(colour, adjacent);
            for (var next = depth + 1; next <= depth + ShapeDepth; next++)
            {
                if (At(next) is { } further && RatioOf(further, adjacent) > ratio)
                {
                    shape = further;
                    ratio = RatioOf(further, adjacent);
                }
            }

            return new PhoneControlSide(side, adjacent, shape, ratio);
        }

        return new PhoneControlSide(side, adjacent, null, 1.0);
    }

    // The colours of a box, with colours closer than the swatch's limit joined into the commoner.
    private static Dictionary<uint, int> Joined(PhonePixels pixels, PhoneBounds box)
    {
        var counts = new Dictionary<uint, int>();
        for (var y = Math.Max(box.Top, 0); y < Math.Min(box.Bottom, pixels.Height); y++)
        {
            for (var x = Math.Max(box.Left, 0); x < Math.Min(box.Right, pixels.Width); x++)
            {
                var colour = pixels.At(x, y) & 0xFFFFFF;
                counts[colour] = counts.TryGetValue(colour, out var count) ? count + 1 : 1;
            }
        }

        var kept = new List<(uint Colour, int Count)>();
        foreach (var (colour, count) in counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
        {
            var index = kept.FindIndex(entry => PhoneColours.Difference(entry.Colour, colour) < PhoneColours.JoinBelow);
            if (index >= 0)
            {
                kept[index] = (kept[index].Colour, kept[index].Count + count);
            }
            else
            {
                kept.Add((colour, count));
            }
        }

        return kept.ToDictionary(entry => entry.Colour, entry => entry.Count);
    }

    internal static double RatioOf(uint first, uint second) =>
        PhoneColours.Ratio(PhoneColours.Luminance(first), PhoneColours.Luminance(second));

    private static int Scaled(int pixels, PictureScale scale) => Math.Max(1, (int)Math.Round(pixels * Math.Max(scale.X, scale.Y)));

    private static PhoneBounds Inset(PhoneBounds box, int by) => new(box.Left + by, box.Top + by, box.Right - by, box.Bottom - by);

    private static bool Inside(PhoneBounds bounds, int x, int y) =>
        x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
}

/// <summary>
/// Step 5 of android-checks.md: the non-text contrast check, WCAG 2.2's
/// 1.4.11 (level AA only), from the controls step 5 measured, by the
/// owner's decisions of 2026-11-12:
/// a control with no visible text and no icon of 3:1 needs a boundary of
/// 3:1, else an error; any other control's boundary below 3:1 is info; and
/// a control that shows its state only by a fill or shape below 3:1 is an
/// error. The state is the tree's, when it marks one control of a row
/// selected or checked; else the picture's, when one control of a row of
/// the same class and size is drawn differently from the others. From the
/// video, a ratio within 12% of 3:1 is a warning, near the limit, as for
/// text. Graphics with no item in the tree are left for the vision model.
/// By the owner's decisions of 2026-11-16, with no model: a text field's
/// border, bottom line or background below 3:1 is an error, whatever text
/// it shows; text OCR read with no word in the item's text in the tree is
/// a warning, as the picture may not show the item; and a text field shown
/// only by a bottom line is a warning of its own check,
/// <see cref="PhoneCheckNames.FieldBottomLineOnly"/>.
/// </summary>
public sealed class PhoneNonTextContrastRun
{
    /// <summary>WCAG 2.2's 1.4.11 limit, compared unrounded.</summary>
    public const double Limit = 3.0;

    private readonly Dictionary<PhoneScreen, List<PhoneFinding>> _byScreen = [];
    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, int> _controlsByVisit = [];

    private PhoneNonTextContrastRun()
    {
    }

    public static PhoneNonTextContrastRun Empty { get; } = new();

    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The controls measured, counted once for each settled snapshot they were measured on.</summary>
    public int Controls { get; private set; }

    /// <summary>The controls with no finding.</summary>
    public int Passed { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneScreen screen) =>
        _byScreen.TryGetValue(screen, out var list) ? list : [];

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    public int ControlsIn(PhoneVisit visit) =>
        _controlsByVisit.TryGetValue(visit, out var count) ? count : 0;

    public static PhoneNonTextContrastRun Run(PhoneContrastRun measured, PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneNonTextContrastRun();
        var findings = new Dictionary<(string, PhoneVisit, PhoneCheckLevel, string), PhoneFinding>();
        foreach (var snapshot in measured.Snapshots)
        {
            var visit = snapshot.Visit;
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            var source = snapshot.Picture.Kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video";
            var states = States(snapshot.Controls);

            void Add(string check, PhoneCheckItem item, PhoneCheckLevel level, string measured)
            {
                var key = (check, visit, level, item.Identity);
                if (!findings.TryGetValue(key, out var finding))
                {
                    findings[key] = finding = new PhoneFinding(check, level, visit, item.Identity, item.Label);
                }

                if (finding._times.Count > 0 && finding._times[^1].Snapshot.EventId == snapshot.Settled.EventId)
                {
                    return;
                }

                finding._times.Add(new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, item.Bounds, item.TreeKey,
                    $"{measured}; {source}", state));
            }

            foreach (var control in snapshot.Controls)
            {
                if (control.Covered is { } covered)
                {
                    Add(PhoneCheckNames.NonTextContrast, control.Item, PhoneCheckLevel.NotRun, $"not measured, as {covered}");
                    continue;
                }

                run.Controls++;
                run._controlsByVisit[visit] = run.ControlsIn(visit) + 1;

                // A text field is judged by its border, bottom line or
                // background alone, never as a control showing a state.
                var judged = !control.Item.Node.Editable && states.TryGetValue(control, out var shown)
                    ? JudgeState(control, shown, snapshot.Picture.Kind)
                    : Judge(control, snapshot.Picture.Kind, snapshot.Engine);
                if (JudgeBottomLine(control) is { } line)
                {
                    Add(PhoneCheckNames.FieldBottomLineOnly, control.Item, line.Level, line.Measured);
                }

                if (judged is not { } result)
                {
                    run.Passed++;
                    continue;
                }

                Add(PhoneCheckNames.NonTextContrast, control.Item, result.Level, result.Measured);
            }
        }

        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Findings = [.. findings.Values
            .OrderBy(finding => screenOrder.TryGetValue(finding.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(finding => finding.Visit.Start)
            .ThenBy(finding => finding.Level)
            .ThenBy(finding => finding.Times[0].Time)
            .ThenBy(finding => finding.Times[0].Bounds.Top)
            .ThenBy(finding => finding.Times[0].Bounds.Left)];
        foreach (var finding in run.Findings)
        {
            if (!run._byScreen.TryGetValue(finding.Screen, out var list))
            {
                run._byScreen[finding.Screen] = list = [];
            }

            list.Add(finding);
            if (!run._byVisit.TryGetValue(finding.Visit, out var ofVisit))
            {
                run._byVisit[finding.Visit] = ofVisit = [];
            }

            ofVisit.Add(finding);
        }

        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>How one control of a row shows its state: from the tree or the picture, and the others' fill.</summary>
    public sealed record StateShown(bool FromTree, string Marked, int Others, uint OthersFill);

    /// <summary>
    /// The controls of the snapshot that show a state: in each row (or
    /// column) of two or more controls with one parent, one class and
    /// the same top and height (or left and width), the one the tree
    /// marks selected or checked, when it marks just one; else, in a row
    /// (not a column, as cards stacked down the screen differ in colour
    /// with no state), when all but one share a fill (within the swatch's
    /// join limit) and one does not, that one; of two that touch with
    /// different fills, the one less like the colour around them.
    /// </summary>
    public static IReadOnlyDictionary<PhoneControlMeasure, StateShown> States(IReadOnlyList<PhoneControlMeasure> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        var shown = new Dictionary<PhoneControlMeasure, StateShown>();
        foreach (var family in controls.GroupBy(control => (control.Item.Parent, control.Item.Node.ClassName)))
        {
            if (family.Key.Parent is null)
            {
                continue;
            }

            foreach (var (row, across) in Rows([.. family]))
            {
                var marked = row.Where(control => control.Item.Node.Selected || IsChecked(control.Item)).ToList();
                if (marked.Count == 1)
                {
                    var others = row.Where(control => control != marked[0]).ToList();
                    shown[marked[0]] = new StateShown(true, marked[0].Item.Node.Selected ? "selected" : "checked", others.Count, Commonest(others));
                    continue;
                }

                if (marked.Count > 1)
                {
                    continue;
                }

                if (across && Odd(row) is { } odd)
                {
                    var others = row.Where(control => control != odd).ToList();
                    shown[odd] = new StateShown(false, "selected", others.Count, Commonest(others));
                }
            }
        }

        return shown;
    }

    private static uint Commonest(IReadOnlyList<PhoneControlMeasure> controls) =>
        controls.GroupBy(control => control.Fill).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;

    private static bool IsChecked(PhoneCheckItem item) =>
        item.Node.Checkable && string.Equals(item.Node.Checked, "checked", StringComparison.OrdinalIgnoreCase);

    // The rows and columns of a family: a row has the same top and height,
    // within a tenth of the height, as tabs' widths follow their words; a
    // column the same left and width, within a tenth of the width.
    private static IEnumerable<(List<PhoneControlMeasure> Row, bool Across)> Rows(List<PhoneControlMeasure> family)
    {
        static bool Near(int first, int second, int size) => Math.Abs(first - second) <= Math.Max(2, size / 10);

        var used = new HashSet<PhoneControlMeasure>();
        foreach (var first in family.OrderBy(control => control.Item.Bounds.Top).ThenBy(control => control.Item.Bounds.Left))
        {
            if (used.Contains(first))
            {
                continue;
            }

            var a = first.Item.Bounds;
            var row = family.Where(other => !used.Contains(other) &&
                Near(other.Item.Bounds.Height, a.Height, a.Height) && Near(other.Item.Bounds.Top, a.Top, a.Height)).ToList();
            var across = row.Count >= 2;
            if (!across)
            {
                row = [.. family.Where(other => !used.Contains(other) &&
                    Near(other.Item.Bounds.Width, a.Width, a.Width) && Near(other.Item.Bounds.Left, a.Left, a.Width))];
            }

            if (row.Count >= 2)
            {
                used.UnionWith(row);
                yield return (row, across);
            }
        }
    }

    // The one control drawn differently from the rest of its row.
    private static PhoneControlMeasure? Odd(List<PhoneControlMeasure> row)
    {
        static bool Same(uint first, uint second) => PhoneColours.Difference(first, second) < PhoneColours.JoinBelow;

        if (row.Count == 2)
        {
            // Two side by side are taken as one control's parts, such as a
            // segmented button, only when they touch: two cards in a row
            // can differ in colour with no state.
            var (left, right) = row[0].Item.Bounds.Left <= row[1].Item.Bounds.Left
                ? (row[0].Item.Bounds, row[1].Item.Bounds)
                : (row[1].Item.Bounds, row[0].Item.Bounds);
            if (Same(row[0].Fill, row[1].Fill) || right.Left - left.Right > Math.Max(2, left.Height / 10))
            {
                return null;
            }

            // Of two, the one less like the colour around them.
            var around = row.SelectMany(control => control.Sides).Select(side => side.Adjacent)
                .GroupBy(colour => colour).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).FirstOrDefault()?.Key;
            if (around is not { } colour)
            {
                return null;
            }

            var first = PhoneControlMeasuring.RatioOf(row[0].Fill, colour);
            var second = PhoneControlMeasuring.RatioOf(row[1].Fill, colour);
            return first == second ? null : first > second ? row[0] : row[1];
        }

        foreach (var candidate in row)
        {
            var others = row.Where(control => control != candidate).ToList();
            if (!Same(candidate.Fill, others[0].Fill) && others.All(other => Same(other.Fill, others[0].Fill)))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>A control that shows a state: its fill against the others' and against the colour around it.</summary>
    public static (PhoneCheckLevel Level, string Measured)? JudgeState(PhoneControlMeasure control, StateShown shown, PhonePictureKind kind)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(shown);
        var againstOthers = PhoneControlMeasuring.RatioOf(control.Fill, shown.OthersFill);
        var around = control.Sides.Select(side => side.Adjacent).Distinct()
            .Select(colour => (Colour: colour, Ratio: PhoneControlMeasuring.RatioOf(control.Fill, colour)))
            .OrderBy(pair => pair.Ratio)
            .FirstOrDefault();
        var hasAround = control.Sides.Count > 0;
        var ratio = hasAround ? Math.Min(againstOthers, around.Ratio) : againstOthers;
        var others = shown.Others == 1 ? "the other in its row is not" : string.Create(CultureInfo.CurrentCulture, $"the {shown.Others:N0} others in its row are not");
        var how = shown.FromTree
            ? $"marked {shown.Marked} in the tree, and filled {PhoneColours.Hex(control.Fill)}"
            : $"taken to show its state, as it is filled {PhoneColours.Hex(control.Fill)} and {others}";
        var fill = shown.Others == 1 ? "the other's fill" : "the others' fill";
        var measured = hasAround && PhoneColours.Difference(around.Colour, shown.OthersFill) < PhoneColours.JoinBelow
            ? $"{how}: {Shown(ratio)}:1 against {PhoneColours.Hex(shown.OthersFill)}, {fill} and the colour around it"
            : $"{how}: {Shown(againstOthers)}:1 against {PhoneColours.Hex(shown.OthersFill)}, {fill}" +
                (hasAround ? $", and {Shown(around.Ratio)}:1 against {PhoneColours.Hex(around.Colour)} around it" : string.Empty);

        return Verdict(ratio, kind) is { } level ? (level, measured + "; a state needs 3:1" + Near(ratio, kind)) : null;
    }

    /// <summary>
    /// A control that does not show a state: what identifies it. A text
    /// field is identified only by its border, bottom line or background,
    /// which needs 3:1 whatever text or icon it shows (W3C's Understanding
    /// 1.4.11, figures 31, 32 and 34; owner, 2026-11-16): below, an error.
    /// Text OCR read identifies a control only when a word of it is in the
    /// item's text in the tree; else the picture may not show the item, a
    /// warning (owner, 2026-11-16).
    /// </summary>
    public static (PhoneCheckLevel Level, string Measured)? Judge(PhoneControlMeasure control, PhonePictureKind kind, string? engine)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.Item.Node.Editable)
        {
            return JudgeField(control, kind);
        }

        var boundary = Boundary(control);
        if (control.VisibleText is { } text)
        {
            if (control.Sides.Count == 0 || control.BoundaryRatio >= Limit)
            {
                return null;
            }

            var seen = control.TextFromOcr
                ? $"has visible text, '{text}', found by {(string.IsNullOrEmpty(engine) ? "OCR" : $"OCR ({engine})")}"
                : $"has text, '{text}', from the tree, as OCR was not used";
            if (control.TextFromOcr && TreeText(control.Item) is var tree && !SharesAWord(text, tree))
            {
                var against = tree is null
                    ? "the item has no text in the tree to confirm it by"
                    : $"no word of it is in the item's text in the tree, '{tree}'";
                return (PhoneCheckLevel.Warning,
                    $"{seen}, but {against}, so the picture may not show this item, and whether it needs a boundary is not judged; {boundary}");
            }

            return (PhoneCheckLevel.Info, $"{seen}, so needs no boundary; {boundary}");
        }

        var icon = control.Icon is { } colour
            ? $"its icon, {PhoneColours.Hex(colour)} on {PhoneColours.Hex(control.Fill)}, is {Shown(control.IconRatio)}:1"
            : $"nothing is drawn in it on {PhoneColours.Hex(control.Fill)}";
        var best = Math.Max(control.IconRatio, control.BoundaryRatio);
        if (Verdict(best, kind) is { } level)
        {
            if (control.Sides.Count == 0)
            {
                return (PhoneCheckLevel.NotRun, $"no visible text; {icon}; no side could be measured, as each is cut off or under another window, so its boundary is not known");
            }

            return (level, $"no visible text; {icon}; {boundary}; a control with no text needs an icon or a boundary of 3:1{Near(best, kind)}");
        }

        // Identified by its icon: a boundary below 3:1 is info, as for text.
        return control.Sides.Count == 0 || control.BoundaryRatio >= Limit
            ? null
            : (PhoneCheckLevel.Info, $"no visible text, but {icon}, so it needs no boundary; {boundary}");
    }

    // A text field: its bottom line when that is all that is drawn, else
    // its weakest side, needs 3:1.
    private static (PhoneCheckLevel Level, string Measured)? JudgeField(PhoneControlMeasure control, PhonePictureKind kind)
    {
        if (control.Sides.Count == 0)
        {
            return (PhoneCheckLevel.NotRun,
                "a text field; no side could be measured, as each is cut off or under another window, so whether its border, bottom line or background meets 3:1 is not known");
        }

        var line = BottomLineOnly(control);
        var ratio = line?.Ratio ?? control.BoundaryRatio;
        if (Verdict(ratio, kind) is not { } level)
        {
            return null;
        }

        var shown = line is { } bottom
            ? $"shown only by a line along its bottom, {PhoneColours.Hex(bottom.Shape!.Value)} against {PhoneColours.Hex(bottom.Adjacent)}, {Shown(bottom.Ratio)}:1"
            : Boundary(control);
        return (level, $"a text field; {shown}; a field's border, bottom line or background is what shows it is a field, and needs 3:1 whatever text or icon it shows{Near(ratio, kind)}");
    }

    /// <summary>
    /// A text field shown only by a line along its bottom: its bottom side
    /// has a line, its top side was measured, and nothing is drawn on any
    /// other side measured. Null for any other control.
    /// </summary>
    public static PhoneControlSide? BottomLineOnly(PhoneControlMeasure control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (!control.Item.Node.Editable)
        {
            return null;
        }

        var bottom = control.Sides.FirstOrDefault(side => side.Side == PhoneSide.Bottom);
        if (bottom?.Shape is null || !control.Sides.Any(side => side.Side == PhoneSide.Top))
        {
            return null;
        }

        return control.Sides.All(side => side.Side == PhoneSide.Bottom || side.Shape is null) ? bottom : null;
    }

    /// <summary>
    /// The warning for a text field shown only by a line along its bottom,
    /// whatever the line's contrast (owner, 2026-11-16): with magnification,
    /// a person moving down the screen can see the field's area without
    /// its line, and not know it is a field. Null for any other control.
    /// </summary>
    public static (PhoneCheckLevel Level, string Measured)? JudgeBottomLine(PhoneControlMeasure control)
    {
        if (BottomLineOnly(control) is not { } bottom)
        {
            return null;
        }

        var others = control.Sides.Count - 1;
        return (PhoneCheckLevel.Warning, string.Create(CultureInfo.CurrentCulture,
            $"a text field shown only by a line along its bottom, {PhoneColours.Hex(bottom.Shape!.Value)} against {PhoneColours.Hex(bottom.Adjacent)}, {Shown(bottom.Ratio)}:1, " +
            $"with nothing drawn on the other {others:N0} side{(others == 1 ? "" : "s")} measured; with magnification, the line can be outside the part of the screen shown, so the field may not be seen to be one"));
    }

    /// <summary>
    /// The item's text in the tree: the text, content description, hint and
    /// state description of it and of the items inside it that are seen, in
    /// tree order, or null when there is none.
    /// </summary>
    public static string? TreeText(PhoneCheckItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var parts = new List<string>();
        var stack = new Stack<PhoneCheckItem>([item]);
        while (stack.Count > 0)
        {
            var each = stack.Pop();
            if (each.Node.VisibleToUser)
            {
                foreach (var part in new[] { each.Node.Text, each.Node.ContentDescription, each.Node.Hint, each.Node.StateDescription })
                {
                    if (!string.IsNullOrWhiteSpace(part) && part.Any(char.IsLetterOrDigit) && !parts.Contains(part, StringComparer.Ordinal))
                    {
                        parts.Add(part.Trim());
                    }
                }
            }

            for (var index = each.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(each.Children[index]);
            }
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// Whether text OCR read shares a word with the tree's text: words are
    /// runs of two or more letters or digits, compared ignoring case, and
    /// match when the same, or when one of three or more is inside the
    /// other, as OCR can cut a word at a control's edge ("ouTube").
    /// </summary>
    public static bool SharesAWord(string ocr, string? tree)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        if (tree is null)
        {
            return false;
        }

        var treeWords = Words(tree);
        return Words(ocr).Any(word => treeWords.Any(other =>
            string.Equals(word, other, StringComparison.Ordinal) ||
            (Math.Min(word.Length, other.Length) >= 3 && (other.Contains(word, StringComparison.Ordinal) || word.Contains(other, StringComparison.Ordinal)))));
    }

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in text + " ")
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (current.Length >= 2)
            {
                words.Add(current.ToString());
            }

            current.Clear();
        }

        return words;
    }

    // The boundary in words.
    private static string Boundary(PhoneControlMeasure control)
    {
        if (control.Sides.Count == 0)
        {
            return "no side could be measured";
        }

        var empty = control.Sides.Count(side => side.Shape is null);
        if (empty > 0)
        {
            return string.Create(CultureInfo.CurrentCulture,
                $"no boundary is drawn on {empty:N0} of the {control.Sides.Count:N0} sides measured, against {PhoneColours.Hex(control.Weakest!.Adjacent)}");
        }

        var weakest = control.Weakest!;
        return $"its boundary, {PhoneColours.Hex(weakest.Shape!.Value)} against {PhoneColours.Hex(weakest.Adjacent)}, is {Shown(weakest.Ratio)}:1 at its weakest side";
    }

    // Below 3:1 (unrounded), an error; from the video, a ratio that could be
    // 12% higher or lower and so on the other side of 3:1, a warning.
    private static PhoneCheckLevel? Verdict(double ratio, PhonePictureKind kind)
    {
        var video = kind == PhonePictureKind.Frame;
        var high = video ? ratio * (1 + PhoneTextContrastRun.VideoMargin) : ratio;
        var low = video ? ratio * (1 - PhoneTextContrastRun.VideoMargin) : ratio;
        if (high < Limit)
        {
            return PhoneCheckLevel.Error;
        }

        return low < Limit ? PhoneCheckLevel.Warning : null;
    }

    private static string Near(double ratio, PhonePictureKind kind)
    {
        if (kind != PhonePictureKind.Frame)
        {
            return string.Empty;
        }

        var high = ratio * (1 + PhoneTextContrastRun.VideoMargin);
        var low = ratio * (1 - PhoneTextContrastRun.VideoMargin);
        return high >= Limit && low < Limit ? ", and it is near the limit, so it may fail or pass, as the video's colours are approximate" : string.Empty;
    }

    // Two decimals, or three when two would round it up to 3:1.
    private static string Shown(double ratio)
    {
        var two = Math.Round(ratio, 2, MidpointRounding.AwayFromZero);
        return ratio.ToString(ratio < Limit && two >= Limit ? "0.000" : "0.00", CultureInfo.InvariantCulture);
    }
}
