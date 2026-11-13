using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.Session;

/// <summary>
/// Step 9 of android-checks.md, Label in Name (WCAG 2.5.3, level A, which
/// WCAG2ICT applies as written), on each settled snapshot measured for
/// contrast: a control, an item visible to the user that is clickable,
/// long clickable, checkable or editable, whose name is set apart from its
/// text by a content description, has its visible label compared with
/// that name. The label (owner, 2026-11-12) is all the text inside the
/// control, in reading order, from the tree where the tree has it and from
/// OCR where it does not, saying so; and the text of each item the control
/// is labelled by in the tree (getLabeledBy), with no guessing from text
/// nearby. Two discrete findings: the name does not contain the label,
/// with its words together and in order (an error, from OCR as well,
/// saying so), and the name contains it but does not start with it (a
/// warning, as the criterion's note calls that a best practice).
/// Capitalization and punctuation are ignored, as Understanding 2.5.3 says
/// they are not relevant; text in parentheses may be left out of the name.
/// </summary>
public sealed partial class PhoneLabelInNameRun
{
    /// <summary>The checks' name in the Checks tab's rows with no finding and in its summary.</summary>
    public const string Title = "Label in name (WCAG 2.5.3)";

    /// <summary>
    /// The fewest letters and digits a label must have to be checked: text
    /// of one character, such as "B" for bold or "&gt;" for next, is likely
    /// a symbol, which Understanding 2.5.3 says is not a label (an
    /// assumption, 2026-11-12).
    /// </summary>
    public const int MinimumCharacters = 2;

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, int> _controlsByVisit = [];

    private PhoneLabelInNameRun()
    {
    }

    public static PhoneLabelInNameRun Empty { get; } = new();

    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The controls checked, once for each settled snapshot they were on.</summary>
    public int Controls { get; private set; }

    /// <summary>The controls checked with no finding.</summary>
    public int Passed { get; private set; }

    /// <summary>The controls with a content description and no label read, which are not checked.</summary>
    public int Unread { get; private set; }

    /// <summary>Whether any label was read by OCR.</summary>
    public bool UsedOcr { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    public int ControlsIn(PhoneVisit visit) =>
        _controlsByVisit.TryGetValue(visit, out var count) ? count : 0;

    public static PhoneLabelInNameRun Run(PhoneContrastRun measured, PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneLabelInNameRun();
        var findings = new Dictionary<(PhoneVisit, string, string), PhoneFinding>();
        foreach (var snapshot in measured.Snapshots)
        {
            if (snapshot.Window is not { } window)
            {
                continue;
            }

            var visit = snapshot.Visit;
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            var ocrLines = string.IsNullOrEmpty(snapshot.Engine) ? [] : snapshot.Lines;
            var controls = window.Items.Where(IsControl).ToList();
            foreach (var control in controls)
            {
                if (string.IsNullOrWhiteSpace(control.Node.ContentDescription))
                {
                    continue;
                }

                var labels = LabelsOf(control, controls, window, ocrLines)
                    .Where(label => label.Text.Count(char.IsLetterOrDigit) >= MinimumCharacters)
                    .ToList();
                if (labels.Count == 0)
                {
                    run.Unread++;
                    continue;
                }

                run.Controls++;
                run._controlsByVisit[visit] = run.ControlsIn(visit) + 1;
                run.UsedOcr |= labels.Any(label => label.FromOcr);
                var name = control.Node.ContentDescription!;
                var judged = labels.Select(label => (Label: label, Match: Match(label, name))).ToList();
                var missing = judged.Where(each => each.Match is null).ToList();
                var late = judged.Where(each => each.Match is > 0).ToList();
                if (missing.Count == 0 && late.Count == 0)
                {
                    run.Passed++;
                    continue;
                }

                if (missing.Count > 0)
                {
                    Add(PhoneCheckNames.LabelNotInName, PhoneCheckLevel.Error,
                        $"its name {Quoted(name)} does not contain {string.Join(", nor ", missing.Select(each => Missing(each.Label, name)))}{Source(missing.Select(each => each.Label), snapshot)}");
                }

                if (late.Count > 0)
                {
                    Add(PhoneCheckNames.LabelNotAtStart, PhoneCheckLevel.Warning,
                        $"its name {Quoted(name)} contains {string.Join(" and ", late.Select(each => Describe(each.Label)))}, but does not start with it; a best practice is to have the label's text at the start of the name{Source(late.Select(each => each.Label), snapshot)}");
                }

                void Add(string check, PhoneCheckLevel level, string measuredText)
                {
                    var key = (visit, check, control.Identity);
                    if (!findings.TryGetValue(key, out var finding))
                    {
                        findings[key] = finding = new PhoneFinding(check, level, visit, control.Identity, control.Label);
                    }

                    if (finding._times.Count > 0 && finding._times[^1].Snapshot.EventId == snapshot.Settled.EventId)
                    {
                        return;
                    }

                    finding._times.Add(new PhoneFindingTime(
                        snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, control.Bounds, control.TreeKey,
                        measuredText, state));
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
            .ThenBy(finding => finding.Check == PhoneCheckNames.LabelNotInName ? 0 : 1)
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

    /// <summary>A label: its text, and whether OCR read it, where it is and what it is.</summary>
    internal sealed record Label(string Text, bool FromOcr, bool FromTree, bool LabelledBy);

    // A control: visible, with an area, and one a person acts on.
    private static bool IsControl(PhoneCheckItem item) =>
        item.Node.VisibleToUser && !item.Bounds.IsEmpty &&
        (item.Node.Clickable || item.Node.LongClickable || item.Node.Checkable || item.Node.Editable);

    /// <summary>
    /// A control's labels: the text inside it, then each item it is
    /// labelled by. An editable item's own text is what was typed, not a
    /// label, so a field has only the items it is labelled by.
    /// </summary>
    internal static IEnumerable<Label> LabelsOf(
        PhoneCheckItem control,
        IReadOnlyList<PhoneCheckItem> controls,
        PhoneCheckWindow window,
        IReadOnlyList<PhoneTextLine> ocrLines)
    {
        if (!control.Node.Editable && Inside(control, controls, ocrLines, labelledBy: false) is { } inside)
        {
            yield return inside;
        }

        foreach (var index in control.Node.LabeledBy)
        {
            var by = window.Items.FirstOrDefault(item => item.Node.Index == index);
            if (by is not null && by.Node.VisibleToUser && !ReferenceEquals(by, control) && Inside(by, controls, ocrLines, labelledBy: true) is { } label)
            {
                yield return label;
            }
        }
    }

    // The text inside an item, in reading order: each visible item in it,
    // itself included, with text in the tree, and each line OCR read in it
    // that is no item's text in the tree; leaving out what is inside
    // another control in it, which is that control's own label.
    private static Label? Inside(
        PhoneCheckItem item,
        IReadOnlyList<PhoneCheckItem> controls,
        IReadOnlyList<PhoneTextLine> ocrLines,
        bool labelledBy)
    {
        var nested = controls.Where(other => !ReferenceEquals(other, item) && Within(other.Bounds, item.Bounds) && IsBelow(other, item)).ToList();
        bool InNested(PhoneBounds box) =>
            nested.Any(other => Contains(other.Bounds, (box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2));

        var pieces = new List<(int Top, int Left, string Text, bool Ocr)>();
        foreach (var each in Subtree(item))
        {
            if (each.Node.VisibleToUser && !string.IsNullOrWhiteSpace(each.Text) && !each.Node.Editable && !nested.Any(other => IsBelow(each, other) || ReferenceEquals(each, other)))
            {
                pieces.Add((each.Bounds.Top, each.Bounds.Left, each.Text!, false));
            }
        }

        foreach (var line in ocrLines)
        {
            if (string.IsNullOrWhiteSpace(line.OcrText) || line.Place == PhoneTextPlace.AppTextOnItemBounds ||
                (line.Item is { } owner && !string.IsNullOrWhiteSpace(owner.Text)))
            {
                continue;
            }

            var x = (line.Box.Left + line.Box.Right) / 2;
            var y = (line.Box.Top + line.Box.Bottom) / 2;
            if (Contains(item.Bounds, x, y) && !InNested(line.Box))
            {
                pieces.Add((line.Box.Top, line.Box.Left, line.OcrText!, true));
            }
        }

        if (pieces.Count == 0)
        {
            return null;
        }

        var ordered = pieces.OrderBy(piece => piece.Top).ThenBy(piece => piece.Left).ToList();
        return new Label(
            string.Join(' ', ordered.Select(piece => piece.Text.Trim())),
            ordered.Any(piece => piece.Ocr),
            ordered.Any(piece => !piece.Ocr),
            labelledBy);
    }

    private static IEnumerable<PhoneCheckItem> Subtree(PhoneCheckItem item)
    {
        var stack = new Stack<PhoneCheckItem>();
        stack.Push(item);
        var guard = 0;
        while (stack.Count > 0 && guard++ < 10_000)
        {
            var each = stack.Pop();
            yield return each;
            for (var index = each.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(each.Children[index]);
            }
        }
    }

    private static bool IsBelow(PhoneCheckItem item, PhoneCheckItem ancestor)
    {
        var guard = 0;
        for (var step = item.Parent; step is not null && guard++ < 1000; step = step.Parent)
        {
            if (ReferenceEquals(step, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Within(PhoneBounds inner, PhoneBounds outer) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static bool Contains(PhoneBounds bounds, int x, int y) =>
        x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;

    /// <summary>
    /// Where the name holds the label, its words together and in order:
    /// the index of the name's word it starts at, or null when it does
    /// not. A label with text in parentheses is tried with it left out as
    /// well. Words are spelt as for matching OCR, so capitalization and
    /// punctuation are ignored; a word OCR read may differ from the name's
    /// by a letter, or two in a long word, as OCR misreads.
    /// </summary>
    internal static int? Match(Label label, string name)
    {
        var names = Words(name);
        var at = Find(Words(label.Text), names, label.FromOcr);
        if (at is null && label.Text.Contains('(', StringComparison.Ordinal))
        {
            at = Find(Words(Parentheses().Replace(label.Text, " ")), names, label.FromOcr);
        }

        return at;
    }

    internal static string[] Words(string text) =>
        [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => PhoneTextMatching.Spelling(word.Replace("'", string.Empty, StringComparison.Ordinal).Replace("\u2019", string.Empty, StringComparison.Ordinal)))
            .Where(word => word.Length > 0)];

    private static int? Find(string[] label, string[] name, bool ocr)
    {
        if (label.Length == 0)
        {
            return null;
        }

        for (var start = 0; start + label.Length <= name.Length; start++)
        {
            var all = true;
            for (var index = 0; index < label.Length && all; index++)
            {
                all = Alike(name[start + index], label[index], ocr);
            }

            if (all)
            {
                return start;
            }
        }

        // OCR joins and splits words: the label run together, against the
        // name's words run together from a word's start to a word's end.
        var joined = string.Concat(label);
        for (var start = 0; start < name.Length; start++)
        {
            var run = string.Empty;
            for (var end = start; end < name.Length && run.Length < joined.Length; end++)
            {
                run += name[end];
                if (run == joined)
                {
                    return start;
                }
            }
        }

        return null;
    }

    private static bool Alike(string name, string label, bool ocr) =>
        name == label ||
        (ocr && label.Length >= 4 && Math.Abs(name.Length - label.Length) <= 1 &&
            PhoneTextMatching.Distance(name, label) <= (label.Length >= 8 ? 2 : 1));

    // What is missing: the label, and which of its words the name lacks,
    // or that they are all in it but not together in that order.
    private static string Missing(Label label, string name)
    {
        var names = Words(name);
        var absent = label.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => Words(word) is { Length: > 0 } spelt && spelt.All(part => !names.Any(each => Alike(each, part, label.FromOcr))))
            .Select(word => word.Trim('.', ',', ';', ':', '!', '?', '"', '(', ')'))
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var which = absent.Count == 0
            ? "its words are all in the name, but not together in this order"
            : absent.Count == 1
                ? $"\"{absent[0]}\" is not in the name"
                : $"{string.Join(", ", absent.Take(absent.Count - 1).Select(word => $"\"{word}\""))} and \"{absent[^1]}\" are not in the name";
        return $"{Describe(label)}: {which}";
    }

    private static string Describe(Label label)
    {
        var where = label.LabelledBy ? "the text of the item it is labelled by" : "the text inside it";
        return $"{where}, {Quoted(label.Text)}";
    }

    // How the labels were read: from the tree, by OCR, or both, and from
    // which picture, as OCR's reading is approximate on the video.
    private static string Source(IEnumerable<Label> labels, PhoneContrastSnapshot snapshot)
    {
        var list = labels.ToList();
        var ocr = list.Any(label => label.FromOcr);
        var tree = list.Any(label => label.FromTree);
        if (!ocr)
        {
            return "; the label is the app's text, from the tree";
        }

        var engine = string.IsNullOrEmpty(snapshot.Engine) ? "OCR" : $"OCR ({snapshot.Engine})";
        var picture = snapshot.Picture.Kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video";
        return tree
            ? $"; the label is the app's text, from the tree, with text read by {engine} that the tree does not have as text, {picture}"
            : $"; the label was read by {engine}, as the tree has no text for it, {picture}";
    }

    private static string Quoted(string text) =>
        $"\"{string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))}\"";

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parentheses();
}
