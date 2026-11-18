using System.Diagnostics;
using System.Globalization;

namespace Recorder.Session;

/// <summary>What became of one marked heading, once a visit, in the check of a heading not shown as one.</summary>
public enum PhoneHeadingNotShownState
{
    /// <summary>OCR's text is not the app's, so the heading may be placed on other text (rule 3).</summary>
    OcrDiffers,

    /// <summary>Another line covers at least half of the heading's box (rule 3).</summary>
    Covered,

    /// <summary>Another line in the same row, close beside the heading's box, so OCR's line goes on past it (rule 3).</summary>
    LineGoesOn,

    /// <summary>For the model to judge (rule 4).</summary>
    Asked,
}

/// <summary>One marked heading as the check found it, once a visit: its text, and what became of it.</summary>
public sealed record PhoneHeadingNotShownEntry(PhoneVisit Visit, string Text, PhoneHeadingNotShownState State, string? Why);

/// <summary>What the model's answers made of one marked heading.</summary>
public enum PhoneHeadingNotShownOutcome
{
    /// <summary>Judged shown as a heading: counted, no finding (rule 7).</summary>
    Shown,

    /// <summary>A warning: judged not shown as a heading, or the model could not tell, or gave two answers (rule 7).</summary>
    Finding,
}

/// <summary>One marked heading's answer: what it made of the heading, and the model's inference.</summary>
public sealed record PhoneHeadingNotShownAnswer(PhoneHeadingNotShownOutcome Outcome, PhoneInference Inference);

/// <summary>
/// Step 27 of android-checks.md, text marked as a heading but not shown as
/// one (WCAG 1.3.1; F43), a discrete issue, apart from text shown as a
/// heading but not marked (step 19). The owner chose the rules on
/// 2026-11-18 from two probes of 59 marked headings on 20 recordings, the
/// second with the model's answers: (1) the issue is its own; (2) each
/// marked heading, or text inside one, placed by OCR on a settled
/// snapshot, is taken once a visit, from the first such snapshot, and a
/// heading never placed is counted with no finding; (3) a heading is left
/// out, and counted, where OCR's text differs from the app's, another line
/// covers at least half of its box, or another line in the same row is
/// within half its height of its box, as OCR's line goes on past it; (4)
/// every heading left goes to the model, with no pass without one; (5)
/// text in an item that can be tapped is included; (6) the question,
/// pictures and answers are those of step 19, saying the text is marked
/// as a heading; (7) agreed not a heading is put to the model for complex
/// questions too, where there is one, whose answers decide: agreed not a
/// heading, a warning for a person to confirm, with the model's reason;
/// agreed a heading, no finding; cannot tell or two answers, a warning;
/// (8) with no model, nothing is judged and no finding is made; (9) an
/// answer is kept by its request, so given again only where the text and
/// pictures are unchanged.
/// </summary>
public sealed class PhoneHeadingNotShownRun
{
    /// <summary>The least share of a heading's box another line must cover for it to be left out (rule 3).</summary>
    public const double MostCovered = 0.5;

    /// <summary>How close beside a heading's box another line in its row may be, in the box's heights, for OCR's line to go on past it (rule 3).</summary>
    public const double LineGap = 0.5;

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly List<PhoneHeadingNotShownEntry> _entries = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];

    private PhoneHeadingNotShownRun()
    {
    }

    public static PhoneHeadingNotShownRun Empty { get; } = new();

    /// <summary>The warnings the model's answers made; none before any answer (rules 7 and 8).</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>The marked headings, once a visit, that OCR placed on no picture of the visit (rule 2).</summary>
    public int NotPlaced { get; private set; }

    /// <summary>Every marked heading OCR placed, once a visit, in the order found.</summary>
    public IReadOnlyList<PhoneHeadingNotShownEntry> Entries => _entries;

    /// <summary>The headings left out as OCR's text differs from the app's (rule 3).</summary>
    public int OcrDiffers => Count(PhoneHeadingNotShownState.OcrDiffers);

    /// <summary>The headings left out as another line covers half of the box (rule 3).</summary>
    public int Covered => Count(PhoneHeadingNotShownState.Covered);

    /// <summary>The headings left out as OCR's line goes on past the box (rule 3).</summary>
    public int LineGoesOn => Count(PhoneHeadingNotShownState.LineGoesOn);

    /// <summary>The headings to be asked about, once a visit each, in screen order (rule 4).</summary>
    public IReadOnlyList<PhoneHeadingQuestion> Questions { get; private set; } = [];

    /// <summary>The questions answered, with what each answer made of its heading; empty until a model's answers are given.</summary>
    public IReadOnlyDictionary<PhoneHeadingQuestion, PhoneHeadingNotShownAnswer> Answers { get; private set; } =
        new Dictionary<PhoneHeadingQuestion, PhoneHeadingNotShownAnswer>();

    /// <summary>The headings judged shown as headings, not confirmed.</summary>
    public int Shown => Answers.Values.Count(answer => answer.Outcome == PhoneHeadingNotShownOutcome.Shown);

    /// <summary>The questions with no answer, not judged.</summary>
    public int Unanswered => Questions.Count - Answers.Count;

    /// <summary>
    /// Every inference the model's answers made, findings or not, so that
    /// a report says AI was used even where every heading was judged shown.
    /// </summary>
    public IEnumerable<PhoneInference> Inferences => Answers.Values.Select(answer => answer.Inference);

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its headings were checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    /// <summary>The marked headings OCR placed on a visit, once each.</summary>
    public IEnumerable<PhoneHeadingNotShownEntry> In(PhoneVisit visit) => _entries.Where(entry => entry.Visit == visit);

    /// <summary>Runs rules 1 to 5 on a measurement; no model is used.</summary>
    public static PhoneHeadingNotShownRun Run(PhoneContrastRun measured, PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneHeadingNotShownRun();
        var marked = new HashSet<(PhoneVisit, string)>();
        var seen = new HashSet<(PhoneVisit, string)>();
        var questions = new List<PhoneHeadingQuestion>();
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
            foreach (var item in window.Items.Where(item => item.Node.Heading))
            {
                marked.Add((visit, item.Identity));
            }

            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            var lines = snapshot.Lines;
            foreach (var group in lines
                .Where(line => line.Place == PhoneTextPlace.AppTextPlacedByOcr && line.Item is not null && HeadingOf(line.Item) is not null)
                .GroupBy(line => HeadingOf(line.Item!)!))
            {
                // Rule 2: once a visit, from the first snapshot it is placed on.
                var heading = group.Key;
                if (!seen.Add((visit, heading.Identity)))
                {
                    continue;
                }

                var own = group.ToList();
                var text = TextOf(heading, own);
                var ocr = string.Join(" ", own.Select(line => line.OcrText).Where(read => !string.IsNullOrWhiteSpace(read)));
                var appText = string.Join(" ", own.Select(line => line.AppText).Where(app => !string.IsNullOrEmpty(app)).Distinct(StringComparer.Ordinal));
                var box = new PhoneBounds(own.Min(line => line.Box.Left), own.Min(line => line.Box.Top), own.Max(line => line.Box.Right), own.Max(line => line.Box.Bottom));
                var others = lines.Where(line => !own.Contains(line) && !line.Box.IsEmpty).ToList();

                // Rule 3, in this order.
                PhoneHeadingNotShownState entryState;
                string? why = null;
                if (!SameText(ocr, appText))
                {
                    entryState = PhoneHeadingNotShownState.OcrDiffers;
                    why = $"OCR read \"{ocr}\" for \"{appText}\"";
                }
                else if (others.FirstOrDefault(line => Share(line.Box, box) >= MostCovered) is { } over)
                {
                    entryState = PhoneHeadingNotShownState.Covered;
                    why = $"another line, \"{over.AppText ?? over.OcrText}\", covers it";
                }
                else if (others.FirstOrDefault(line => SameRow(line.Box, box) && SideGap(line.Box, box) <= box.Height * LineGap) is { } next)
                {
                    entryState = PhoneHeadingNotShownState.LineGoesOn;
                    why = $"OCR's line goes on past it, \"{next.OcrText ?? next.AppText}\"";
                }
                else
                {
                    entryState = PhoneHeadingNotShownState.Asked;
                }

                run._entries.Add(new PhoneHeadingNotShownEntry(visit, text, entryState, why));
                if (entryState != PhoneHeadingNotShownState.Asked)
                {
                    continue;
                }

                var time = new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, heading.Bounds, heading.TreeKey,
                    Measured(ocr, snapshot.Picture.Kind), state);
                questions.Add(new PhoneHeadingQuestion(visit, heading.Identity, time, snapshot.Picture, box, text));
            }
        }

        run.NotPlaced = marked.Count(key => !seen.Contains(key));
        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Questions = [.. questions
            .OrderBy(question => screenOrder.TryGetValue(question.Visit.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(question => question.Visit.Start)
            .ThenBy(question => question.Lines.Top)
            .ThenBy(question => question.Lines.Left)];
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// The run with the model's answers given: each answer that makes a
    /// finding (rule 7) is a finding on the visit its heading was found on,
    /// at the answer's level; one judged shown is counted, not a finding.
    /// The note says what became of the questions.
    /// </summary>
    public PhoneHeadingNotShownRun Judged(IReadOnlyDictionary<PhoneHeadingQuestion, PhoneHeadingNotShownAnswer> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneHeadingNotShownRun
        {
            SnapshotsRead = SnapshotsRead,
            NotPlaced = NotPlaced,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        judged._entries.AddRange(_entries);
        judged._visitsRead.UnionWith(_visitsRead);
        var kept = new Dictionary<PhoneHeadingQuestion, PhoneHeadingNotShownAnswer>();
        var findings = new List<PhoneFinding>();
        foreach (var question in Questions)
        {
            if (!answers.TryGetValue(question, out var answer))
            {
                continue;
            }

            kept[question] = answer;
            if (answer.Outcome != PhoneHeadingNotShownOutcome.Finding)
            {
                continue;
            }

            var finding = new PhoneFinding(PhoneCheckNames.HeadingNotShown, answer.Inference.Level, question.Visit, question.Identity, $"heading '{question.Text}'")
            {
                Inference = answer.Inference,
            };
            finding._times.Add(question.Time);
            findings.Add(finding);
        }

        judged.Answers = kept;
        judged.Findings = findings;
        foreach (var finding in findings)
        {
            if (!judged._byVisit.TryGetValue(finding.Visit, out var list))
            {
                judged._byVisit[finding.Visit] = list = [];
            }

            list.Add(finding);
        }

        return judged;
    }

    /// <summary>
    /// The marked heading an item's text belongs to (rule 2): the item, or
    /// the nearest item it is inside, marked as a heading; else the first
    /// marked heading the item holds, as the probe measured it.
    /// </summary>
    internal static PhoneCheckItem? HeadingOf(PhoneCheckItem item)
    {
        for (var step = item; step is not null; step = step.Parent)
        {
            if (step.Node.Heading)
            {
                return step;
            }
        }

        return Held(item);

        static PhoneCheckItem? Held(PhoneCheckItem item)
        {
            foreach (var child in item.Children)
            {
                if (child.Node.Heading)
                {
                    return child;
                }

                if (Held(child) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Whether OCR's text is the app's (rule 3): letters and digits only,
    /// lower case, with "o" as "0" and "l" and "i" as "1", within a fifth
    /// of the app's length in edits, and at least one.
    /// </summary>
    internal static bool SameText(string ocr, string app)
    {
        var (read, given) = (Normal(ocr), Normal(app));
        return read.Length > 0 && Distance(read, given) <= Math.Max(1, given.Length / 5);
    }

    /// <summary>The share of box b that box a covers.</summary>
    internal static double Share(PhoneBounds a, PhoneBounds b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return width <= 0 || height <= 0 || b.Width <= 0 || b.Height <= 0 ? 0 : width * (double)height / (b.Width * (double)b.Height);
    }

    /// <summary>Whether two boxes are in one row: they overlap by at least half the shorter one's height.</summary>
    internal static bool SameRow(PhoneBounds a, PhoneBounds b)
    {
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return height > 0 && height >= Math.Min(a.Height, b.Height) / 2.0;
    }

    /// <summary>The space between two boxes side by side, 0 where they overlap across.</summary>
    internal static int SideGap(PhoneBounds a, PhoneBounds b) => Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));

    // The heading's text as the app gives it: its text or description, or
    // its lines' text.
    private static string TextOf(PhoneCheckItem heading, List<PhoneTextLine> own)
    {
        var text = heading.Node.Text ?? heading.Node.ContentDescription ?? own[0].AppText ?? string.Empty;
        return string.IsNullOrWhiteSpace(text)
            ? string.Join(" ", own.Select(line => line.AppText).Where(app => !string.IsNullOrEmpty(app)).Distinct(StringComparer.Ordinal))
            : text;
    }

    private static string Measured(string ocr, PhonePictureKind kind) =>
        string.Create(CultureInfo.CurrentCulture,
            $"marked as a heading for a screen reader, its text read by OCR as \"{ocr}\"; {(kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video")}");

    private static string Normal(string text) => new([.. text.ToLowerInvariant()
        .Where(char.IsLetterOrDigit)
        .Select(character => character switch { 'o' => '0', 'l' or 'i' => '1', _ => character })]);

    private static int Distance(string a, string b)
    {
        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            row[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }

        return row[b.Length];
    }

    private int Count(PhoneHeadingNotShownState state) => _entries.Count(entry => entry.State == state);
}
