using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.Session;

/// <summary>
/// One piece of text a model is to be asked about (android-checks.md, step
/// 19): the visit and the item's identity, the first time it was judged,
/// with the picture and the box of the item's lines there, and the item's
/// text as the app gives it.
/// </summary>
public sealed record PhoneHeadingQuestion(
    PhoneVisit Visit,
    string Identity,
    PhoneFindingTime Time,
    PhoneContrastPicture Picture,
    PhoneBounds Lines,
    string Text)
{
    public string App => Visit.Screen.Package;

    public string ScreenName => Visit.Screen.Name;
}

/// <summary>
/// Step 19 of android-checks.md, text shown as a heading but not marked as
/// one (WCAG 1.3.1; F2), from the lines OCR placed on each settled
/// snapshot's picture (step 4b). Four rules, chosen by the owner on
/// 2026-11-16 from a measurement of 1,865 lines on 20 recordings, find the
/// candidates without a model: (1) an item's text, placed on the picture by
/// OCR, in an item of at most two lines and ten words with a letter, that is
/// not an image, not a heading, not inside one and holding none, and whose
/// letters are at least 1.35 times as tall as the screen's body text, or
/// that is one line of at most six words all in capitals with no digit and
/// not directly above a field; (2) each is a question for the model chosen
/// for vision-based checks, and stays a warning with none; (3) the answers
/// set the level, as for images of text; (4) text inside an item that can
/// be tapped or a field is left out, a control's text being its name, and
/// counted. Bold is not measured: the stroke on the video was 2 to 8 pixels,
/// and tab labels measured bolder than the headings.
/// </summary>
public sealed partial class PhoneHeadingLookRun
{
    /// <summary>How much taller than the screen's body text a line's letters must be (rule 1; measured, 2026-11-16).</summary>
    public const double TallerBy = 1.35;

    /// <summary>The most lines an item may have (rule 1).</summary>
    public const int MostLines = 2;

    /// <summary>The most words an item's text may have (rule 1).</summary>
    public const int MostWords = 10;

    /// <summary>The most words a line in capitals may have (rule 1).</summary>
    public const int MostCapitalWords = 6;

    /// <summary>
    /// How far below a line in capitals a field may start for the line to be
    /// its label, in the line's heights (rule 1; an assumption, 2026-11-16).
    /// </summary>
    public const double FieldLabelReach = 2.0;

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, int> _itemsByVisit = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];
    private readonly Dictionary<(PhoneVisit, string), PhoneHeadingQuestion> _questions = [];

    private PhoneHeadingLookRun()
    {
    }

    public static PhoneHeadingLookRun Empty { get; } = new();

    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The text items OCR placed, counted once for each settled snapshot they were on.</summary>
    public int Items { get; private set; }

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>The text items in a visit that looked like a heading but are inside an item that can be tapped, or a field (rule 4), once each in a visit.</summary>
    public int InControls { get; private set; }

    /// <summary>The lines in capitals in a visit left out as directly above a field, a field's label (rule 1), once each in a visit.</summary>
    public int FieldLabels { get; private set; }

    /// <summary>The pieces of text the model chosen for vision-based checks is to be asked about, one for each finding, in the findings' order.</summary>
    public IReadOnlyList<PhoneHeadingQuestion> Questions { get; private set; } = [];

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its text was checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    public int ItemsIn(PhoneVisit visit) =>
        _itemsByVisit.TryGetValue(visit, out var count) ? count : 0;

    /// <summary>The question of a finding, or null where the finding is not one for the model, or has its answer.</summary>
    public PhoneHeadingQuestion? QuestionOf(PhoneFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return _questions.TryGetValue((finding.Visit, finding.Identity), out var question) &&
            finding.Level == PhoneCheckLevel.Warning && finding.Inference is null
            ? question
            : null;
    }

    public static PhoneHeadingLookRun Run(PhoneContrastRun measured, PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneHeadingLookRun();
        var findings = new Dictionary<(PhoneVisit, string), PhoneFinding>();
        var inControls = new HashSet<(PhoneVisit, string)>();
        var fieldLabels = new HashSet<(PhoneVisit, string)>();
        foreach (var snapshot in measured.Snapshots)
        {
            // With no OCR, no text is placed on the picture.
            if (snapshot.Window is not { } window || string.IsNullOrEmpty(snapshot.Engine))
            {
                continue;
            }

            run.SnapshotsRead++;
            var visit = snapshot.Visit;
            run._visitsRead.Add(visit);
            var body = BodyHeight(snapshot.Lines);
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            var fields = window.Items.Where(item => item.Node.Editable && item.Node.VisibleToUser && !item.Bounds.IsEmpty).ToList();
            foreach (var group in snapshot.Lines
                .Where(line => line.Place == PhoneTextPlace.AppTextPlacedByOcr && line.Item is not null)
                .GroupBy(line => line.Item!))
            {
                var item = group.Key;
                run.Items++;
                run._itemsByVisit[visit] = run.ItemsIn(visit) + 1;
                if (body <= 0 || IsHeading(item) || item.InstanceOf(PhoneTextMatching.ImageViewClass))
                {
                    continue;
                }

                var text = group.First().AppText ?? string.Empty;
                var lineCount = snapshot.Lines.Count(line => line.Item == item);
                if (!text.Any(char.IsLetter) || lineCount > MostLines || Words(text).Count > MostWords)
                {
                    continue;
                }

                var measuredLines = group.Where(line => line.Colours is not null).ToList();
                var tallest = measuredLines.Where(line => line.InkHeight > 0).MaxBy(line => line.InkHeight);
                var ratio = tallest is null ? 0 : tallest.InkHeight / body;
                var capitals = lineCount == 1 && measuredLines.Count == 1 && InCapitals(measuredLines[0], text) ? measuredLines[0] : null;
                if (ratio < TallerBy && capitals is null)
                {
                    continue;
                }

                // Rule 4: a control's text is its name, not a heading.
                if (InControl(item))
                {
                    inControls.Add((visit, item.Identity));
                    continue;
                }

                // A line in capitals directly above a field is the field's label.
                if (ratio < TallerBy && capitals is not null && LabelsAField(capitals, fields))
                {
                    fieldLabels.Add((visit, item.Identity));
                    continue;
                }

                var look = ratio >= TallerBy
                    ? string.Create(CultureInfo.CurrentCulture,
                        $"its letters are {ratio:0.##} times as tall as the screen's body text ({tallest!.InkHeight:N0} pixels against {body:0.#} on the picture)")
                    : $"it is in capitals, \"{capitals!.OcrText}\"";
                var measuredText = $"{look}, in {(lineCount == 1 ? "1 line" : $"{lineCount} lines")}, and is not marked as a heading; {NotJudged}; {Source(snapshot.Picture.Kind)}";
                var key = (visit, item.Identity);
                if (!findings.TryGetValue(key, out var finding))
                {
                    findings[key] = finding = new PhoneFinding(PhoneCheckNames.TextShownAsHeading, PhoneCheckLevel.Warning, visit, item.Identity, item.Label);
                }

                if (finding._times.Count > 0 && finding._times[^1].Snapshot.EventId == snapshot.Settled.EventId)
                {
                    continue;
                }

                var time = new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, item.Bounds, item.TreeKey, measuredText, state);
                finding._times.Add(time);
                if (!run._questions.ContainsKey(key))
                {
                    var boxes = group.Select(line => line.Box).ToList();
                    var lines = new PhoneBounds(boxes.Min(box => box.Left), boxes.Min(box => box.Top), boxes.Max(box => box.Right), boxes.Max(box => box.Bottom));
                    run._questions[key] = new PhoneHeadingQuestion(visit, item.Identity, time, snapshot.Picture, lines, text);
                }
            }
        }

        run.InControls = inControls.Count;
        run.FieldLabels = fieldLabels.Count;
        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Place([.. findings.Values
            .OrderBy(finding => screenOrder.TryGetValue(finding.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(finding => finding.Visit.Start)
            .ThenBy(finding => finding.Times[0].Time)
            .ThenBy(finding => finding.Times[0].Bounds.Top)
            .ThenBy(finding => finding.Times[0].Bounds.Left)]);
        run.Questions = [.. run.Findings
            .Select(finding => run._questions.GetValueOrDefault((finding.Visit, finding.Identity)))
            .OfType<PhoneHeadingQuestion>()];
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// The run with the model's answers given: each finding of a question
    /// answered takes the answer's level and words, and every other finding
    /// is as it was. The note says what became of the questions.
    /// </summary>
    public PhoneHeadingLookRun Judged(IReadOnlyDictionary<PhoneHeadingQuestion, PhoneInference> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneHeadingLookRun
        {
            Items = Items,
            SnapshotsRead = SnapshotsRead,
            InControls = InControls,
            FieldLabels = FieldLabels,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        foreach (var (key, question) in _questions)
        {
            judged._questions[key] = question;
        }

        foreach (var (visit, count) in _itemsByVisit)
        {
            judged._itemsByVisit[visit] = count;
        }

        judged._visitsRead.UnionWith(_visitsRead);
        judged.Place([.. Findings.Select(finding =>
        {
            if (QuestionOf(finding) is not { } question || !answers.TryGetValue(question, out var answer))
            {
                return finding;
            }

            var copy = new PhoneFinding(finding.Check, answer.Level, finding.Visit, finding.Identity, finding.Label) { Inference = answer };
            copy._times.AddRange(finding._times.Select(time => time with
            {
                Measured = time.Measured.Replace("; " + NotJudged, "", StringComparison.Ordinal),
            }));
            return copy;
        })]);
        return judged;
    }

    private void Place(IReadOnlyList<PhoneFinding> findings)
    {
        Findings = findings;
        foreach (var finding in findings)
        {
            if (!_byVisit.TryGetValue(finding.Visit, out var ofVisit))
            {
                _byVisit[finding.Visit] = ofVisit = [];
            }

            ofVisit.Add(finding);
        }
    }

    private const string NotJudged = "whether it is shown as a heading for the content after it is not judged";

    private static string Source(PhonePictureKind kind) =>
        kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video";

    /// <summary>
    /// The height of the screen's body text: the height of the letters
    /// that most of the snapshot's characters have, the median of its lines'
    /// ink heights with each line counted once for each character OCR read
    /// in it (measured, 2026-11-16); 0 where no line was measured.
    /// </summary>
    internal static double BodyHeight(IReadOnlyList<PhoneTextLine> lines)
    {
        var weighted = lines
            .Where(line => line.InkHeight > 0)
            .Select(line => (Height: line.InkHeight, Characters: (line.OcrText ?? line.AppText ?? string.Empty).Length))
            .Where(entry => entry.Characters > 0)
            .OrderBy(entry => entry.Height)
            .ToList();
        var total = weighted.Sum(entry => entry.Characters);
        var count = 0;
        foreach (var (height, characters) in weighted)
        {
            count += characters;
            if (count * 2 >= total)
            {
                return height;
            }
        }

        return 0;
    }

    // Marked as a heading, inside one, or holding one.
    private static bool IsHeading(PhoneCheckItem item)
    {
        for (var step = item; step is not null; step = step.Parent)
        {
            if (step.Node.Heading)
            {
                return true;
            }
        }

        return Holds(item);

        static bool Holds(PhoneCheckItem item) => item.Children.Any(child => child.Node.Heading || Holds(child));
    }

    // The item is a field, or it or an item it is inside can be tapped.
    private static bool InControl(PhoneCheckItem item)
    {
        if (item.Node.Editable)
        {
            return true;
        }

        for (var step = item; step is not null; step = step.Parent)
        {
            if (step.Node.Clickable || step.Node.LongClickable)
            {
                return true;
            }
        }

        return false;
    }

    // One line, as OCR read it, of at most six words, at least three
    // letters, all of them capitals, with no digit in it or in the app's
    // text (OCR reads "0" as "O").
    private static bool InCapitals(PhoneTextLine line, string text)
    {
        var read = line.OcrText ?? string.Empty;
        var letters = read.Where(char.IsLetter).ToList();
        return letters.Count >= 3 && letters.All(char.IsUpper) && Words(read).Count <= MostCapitalWords &&
            !read.Any(char.IsDigit) && !text.Any(char.IsDigit);
    }

    // A visible field starting below the line, within twice the line's
    // height, and overlapping it across.
    private static bool LabelsAField(PhoneTextLine line, List<PhoneCheckItem> fields)
    {
        var reach = FieldLabelReach * Math.Max(1, line.WordsHeight);
        return fields.Any(field =>
            field.Bounds.Top >= line.Box.Top && field.Bounds.Top - line.Box.Bottom <= reach &&
            field.Bounds.Left < line.Box.Right && field.Bounds.Right > line.Box.Left);
    }

    private static List<string> Words(string text) => [.. WordPattern().Matches(text).Select(match => match.Value)];

    [GeneratedRegex(@"[\p{L}\p{N}'\u2019]+")]
    private static partial Regex WordPattern();
}
