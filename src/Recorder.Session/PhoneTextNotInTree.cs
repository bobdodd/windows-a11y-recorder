using System.Diagnostics;
using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// One line of text OCR read that no item of the tree holds, for a model
/// to judge (android-checks.md, step 25): the visit and the settled
/// snapshot it was first seen on, the line as OCR read it and its box, the
/// item it is drawn in, whether an item marked not shown holds the same
/// text (rule 6), and what a screen reader says on the screen.
/// </summary>
public sealed record PhoneTextNotInTreeQuestion(
    PhoneVisit Visit,
    string Identity,
    PhoneFindingTime Time,
    PhoneContrastPicture Picture,
    string Ocr,
    string? ContainerClass,
    string? ContainerText,
    bool HeldByHidden,
    string ScreenText)
{
    public string App => Visit.Screen.Package;

    public string ScreenName => Visit.Screen.Name;

    /// <summary>The line's box on the screen.</summary>
    public PhoneBounds Bounds => Time.Bounds;
}

/// <summary>What became of one line OCR read in no item's text in the check of text on screen not in the tree.</summary>
public enum PhoneTextNotInTreeState
{
    /// <summary>TalkBack's caption, or what it was saying (rule 3).</summary>
    Caption,

    /// <summary>Inside an image, an ImageView or a web image, left to the images-of-text check (rule 4).</summary>
    Image,

    /// <summary>Fewer than 3 letters or digits (rule 1).</summary>
    Short,

    /// <summary>Held by a visible item elsewhere on the screen, so in the tree (rule 5).</summary>
    HeldElsewhere,

    /// <summary>Inside a small item with text of its own, taken as that text misread (rule 7).</summary>
    Misread,

    /// <summary>Held by an item shown in another snapshot of the same visit, so the tree was not complete when this one was taken (rule 2c).</summary>
    OtherSnapshot,

    /// <summary>For the model to judge (rule 8).</summary>
    Asked,
}

/// <summary>One line as the check found it, once a visit: its text, and what became of it.</summary>
public sealed record PhoneTextNotInTreeEntry(PhoneVisit Visit, string Ocr, PhoneTextNotInTreeState State);

/// <summary>What the model's answers made of one line.</summary>
public enum PhoneTextNotInTreeOutcome
{
    /// <summary>Text behind a dialog, sheet or scrim, or covered: counted, no finding (rule 9).</summary>
    Covered,

    /// <summary>Not text, such as a shape or icon read as letters: counted, no finding (rule 9).</summary>
    NotText,

    /// <summary>A warning: drawn text a screen reader cannot reach, or the model could not tell, or gave two answers (rule 9).</summary>
    Finding,
}

/// <summary>One line's answer: what it made of the line, and the model's inference.</summary>
public sealed record PhoneTextNotInTreeAnswer(PhoneTextNotInTreeOutcome Outcome, PhoneInference Inference);

/// <summary>
/// Step 25 of android-checks.md, text shown on screen that is not in the
/// accessibility tree (WCAG 1.1.1, 4.1.2). Ten rules, chosen by the owner
/// on 2026-11-18 ("Build the check with rules 1 to 10, including rule 2c,
/// at high impact") from probes of 493 lines on 20 recordings, with no
/// model: (1) the candidates are the lines OCR read in no item's text,
/// with at least 3 letters or digits, each once a visit from its first
/// settled snapshot; (2) a picture out of step with the tree is not
/// judged: out of step where the tree has at least 3 text items and OCR
/// found fewer than half of them where the tree places them (2b), and, as
/// rule 2c after rule 7, a line an item shown in another snapshot of the
/// same visit holds is set aside, as the tree was not complete when this
/// snapshot was taken; (3) TalkBack's caption is left out; (4) a line
/// inside an image, an ImageView or a web image (android.widget.Image), is
/// left to the images-of-text check; (5) a line a visible item elsewhere
/// on the screen holds is in the tree, counted, not reported; (6) a line
/// an item not shown holds is still asked about, and the question says
/// so; (7) a line inside an item covering at most a quarter of the screen
/// with its own text, hint or description, not a WebView, is that text
/// misread, left to the Label in Name check (2.5.3); (8) the rest are
/// asked of the project's model for vision-based checks with the screen,
/// the line outlined and a close-up, the item it is drawn in and what a
/// screen reader says on the screen: drawn text a screen reader cannot
/// reach, text behind a dialog or scrim or covered, not text, or cannot
/// tell; (9) drawn text is a warning for a person to verify, high impact
/// if confirmed, cannot tell or two answers a warning to check manually,
/// the others counted, and with no model nothing is judged; (10) one
/// finding a line a visit, its answer kept by signature.
/// </summary>
public sealed class PhoneTextNotInTreeRun
{
    /// <summary>The fewest text items the tree must have for a picture to be judged out of step (rule 2b).</summary>
    public const int LeastTextItems = 3;

    /// <summary>The largest share of the screen an item may cover for a line in it to be taken as its own text misread (rule 7).</summary>
    public const double MostMisreadShare = 0.25;

    /// <summary>The fewest letters or digits a line must have to be a candidate (rule 1).</summary>
    public const int LeastLetters = 3;

    /// <summary>The most characters of what a screen reader says on the screen given to a model; the start is kept.</summary>
    public const int MostCharacters = 4000;

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly List<PhoneTextNotInTreeEntry> _entries = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];

    private PhoneTextNotInTreeRun()
    {
    }

    public static PhoneTextNotInTreeRun Empty { get; } = new();

    /// <summary>The warnings the model's answers made; none before any answer (rule 9).</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>The snapshots whose picture was out of step with the tree, not judged (rule 2b).</summary>
    public int OutOfStepSnapshots { get; private set; }

    /// <summary>The lines in no item's text on pictures out of step, not judged, counted on each snapshot (rule 2b).</summary>
    public int OutOfStepLines { get; private set; }

    /// <summary>Every line found on a picture in step, once a visit, in the order found.</summary>
    public IReadOnlyList<PhoneTextNotInTreeEntry> Entries => _entries;

    /// <summary>The lines that were TalkBack's caption (rule 3).</summary>
    public int Captions => Count(PhoneTextNotInTreeState.Caption);

    /// <summary>The lines inside an image, left to the images-of-text check (rule 4).</summary>
    public int InImages => Count(PhoneTextNotInTreeState.Image);

    /// <summary>The lines with fewer than 3 letters or digits (rule 1).</summary>
    public int ShortLines => Count(PhoneTextNotInTreeState.Short);

    /// <summary>The lines a visible item elsewhere on the screen holds (rule 5).</summary>
    public int HeldElsewhere => Count(PhoneTextNotInTreeState.HeldElsewhere);

    /// <summary>The lines taken as a small item's own text misread (rule 7).</summary>
    public int Misreads => Count(PhoneTextNotInTreeState.Misread);

    /// <summary>The lines an item shown in another snapshot of the visit holds (rule 2c).</summary>
    public int OtherSnapshots => Count(PhoneTextNotInTreeState.OtherSnapshot);

    /// <summary>The lines to be asked about, once a visit each, in screen order (rule 8).</summary>
    public IReadOnlyList<PhoneTextNotInTreeQuestion> Questions { get; private set; } = [];

    /// <summary>The questions answered, with what each answer made of its line; empty until a model's answers are given.</summary>
    public IReadOnlyDictionary<PhoneTextNotInTreeQuestion, PhoneTextNotInTreeAnswer> Answers { get; private set; } =
        new Dictionary<PhoneTextNotInTreeQuestion, PhoneTextNotInTreeAnswer>();

    /// <summary>The lines judged covered, behind a dialog, sheet or scrim, not confirmed.</summary>
    public int Covered => Answers.Values.Count(answer => answer.Outcome == PhoneTextNotInTreeOutcome.Covered);

    /// <summary>The lines judged not text, not confirmed.</summary>
    public int NotText => Answers.Values.Count(answer => answer.Outcome == PhoneTextNotInTreeOutcome.NotText);

    /// <summary>The questions with no answer, not judged.</summary>
    public int Unanswered => Questions.Count - Answers.Count;

    /// <summary>
    /// Every inference the model's answers made, findings or not, so that
    /// a report says AI was used even where no line was judged drawn text.
    /// </summary>
    public IEnumerable<PhoneInference> Inferences => Answers.Values.Select(answer => answer.Inference);

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its lines were checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    /// <summary>The lines found on a visit, once each.</summary>
    public IEnumerable<PhoneTextNotInTreeEntry> In(PhoneVisit visit) => _entries.Where(entry => entry.Visit == visit);

    /// <summary>Runs rules 1 to 8 on a measurement; no model is used.</summary>
    public static PhoneTextNotInTreeRun Run(
        PhoneContrastRun measured,
        PhoneScreenTimeline screens,
        TalkBackSpeechTimeline? speech = null,
        PhoneAccessibilityTimeline? accessibility = null)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        speech ??= TalkBackSpeechTimeline.Empty;
        accessibility ??= PhoneAccessibilityTimeline.Empty;
        var talkBack = speech.Recorded || accessibility.FocusRecorded;
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneTextNotInTreeRun();

        // Rule 2c: each visit's snapshots with a tree, with their visible
        // items' words, made when first needed.
        var byVisit = measured.Snapshots
            .Where(snapshot => snapshot.Window is { Items.Count: > 0 })
            .GroupBy(snapshot => snapshot.Visit)
            .ToDictionary(group => group.Key, group => group.ToList());
        var shownOf = new Dictionary<PhoneContrastSnapshot, Shown>();
        Shown ShownOf(PhoneContrastSnapshot snapshot)
        {
            if (!shownOf.TryGetValue(snapshot, out var shown))
            {
                shownOf[snapshot] = shown = Shown.Of(snapshot.Window!.Items.Where(item => item.Node.VisibleToUser));
            }

            return shown;
        }

        var questions = new List<PhoneTextNotInTreeQuestion>();
        var seen = new HashSet<(PhoneVisit, string)>();
        foreach (var snapshot in measured.Snapshots)
        {
            if (snapshot.Window is not { Items.Count: > 0 } window || string.IsNullOrEmpty(snapshot.Engine))
            {
                continue;
            }

            run.SnapshotsRead++;
            var visit = snapshot.Visit;
            run._visitsRead.Add(visit);
            var found = snapshot.Lines
                .Where(line => line.Place == PhoneTextPlace.FoundByOcr && !string.IsNullOrWhiteSpace(line.OcrText))
                .ToList();
            if (found.Count == 0)
            {
                continue;
            }

            // Rule 2b.
            if (OutOfStep(snapshot))
            {
                run.OutOfStepSnapshots++;
                run.OutOfStepLines += found.Count;
                continue;
            }

            var items = window.Items;
            var frame = items[0].Bounds;
            var images = items
                .Where(item => PhoneTextMatching.IsImage(item) &&
                    item.Node.VisibleToUser && !item.Bounds.IsEmpty)
                .ToList();
            var said = PhoneImagesOfTextRun.CaptionSaid(snapshot.Settled.MonotonicNanoseconds, talkBack, speech, accessibility);
            var visible = ShownOf(snapshot);
            var hidden = Shown.Of(items.Where(item => !item.Node.VisibleToUser));
            var others = byVisit.TryGetValue(visit, out var ofVisit) ? ofVisit.Where(other => !ReferenceEquals(other, snapshot)).ToList() : [];
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            string? screenText = null;
            foreach (var line in found)
            {
                var text = line.OcrText!.Trim();
                var spelt = PhoneTextMatching.Spelling(text);

                // Rule 1 and 10: each line once a visit, from its first snapshot in step.
                if (!seen.Add((visit, spelt)))
                {
                    continue;
                }

                var words = Words(text);
                var lineState =
                    said(line) ? PhoneTextNotInTreeState.Caption :
                    InAny(line.Box, images) ? PhoneTextNotInTreeState.Image :
                    spelt.Length < LeastLetters ? PhoneTextNotInTreeState.Short :
                    visible.Holds(spelt, words) ? PhoneTextNotInTreeState.HeldElsewhere :
                    Misread(line, frame) ? PhoneTextNotInTreeState.Misread :
                    others.Any(other => ShownOf(other).Holds(spelt, words)) ? PhoneTextNotInTreeState.OtherSnapshot :
                    PhoneTextNotInTreeState.Asked;
                run._entries.Add(new(visit, text, lineState));
                if (lineState != PhoneTextNotInTreeState.Asked)
                {
                    continue;
                }

                var container = line.Container;
                var heldByHidden = hidden.Holds(spelt, words);
                var containerText = container is null ? null : LabelOf(container);
                var time = new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, line.Box, container?.TreeKey ?? string.Empty,
                    Measured(container, heldByHidden), state);
                screenText ??= string.Join(" | ", PhoneImageAlternativeRun.Stops(window, null, out _));
                questions.Add(new PhoneTextNotInTreeQuestion(
                    visit, $"{container?.Identity ?? string.Empty}\u001Focr {spelt}", time, snapshot.Picture, text,
                    container?.Node.ClassName, string.IsNullOrEmpty(containerText) ? null : containerText, heldByHidden, Start(screenText)));
            }
        }

        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Questions = [.. questions
            .OrderBy(question => screenOrder.TryGetValue(question.Visit.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(question => question.Visit.Start)
            .ThenBy(question => question.Bounds.Top)
            .ThenBy(question => question.Bounds.Left)];
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// Rule 2b: a snapshot's picture is out of step with the tree where the
    /// tree has at least 3 text items, not images, and OCR found fewer than
    /// half of them where the tree places them.
    /// </summary>
    public static bool OutOfStep(PhoneContrastSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var groups = snapshot.Lines
            .Where(line => line.Item is { } owner && !owner.InstanceOf(PhoneTextMatching.ImageViewClass) &&
                line.Place is PhoneTextPlace.AppTextPlacedByOcr or PhoneTextPlace.AppTextOnItemBounds)
            .GroupBy(line => line.Item!)
            .ToList();
        var placed = groups.Count(group => group.Any(line => line.Place == PhoneTextPlace.AppTextPlacedByOcr));
        return groups.Count >= LeastTextItems && placed * 2 < groups.Count;
    }

    /// <summary>
    /// The run with the model's answers given: each answer that makes a
    /// finding (rule 9) is a finding on the visit its line was found on, at
    /// the answer's level; covered and not text are counted, not findings.
    /// The note says what became of the questions.
    /// </summary>
    public PhoneTextNotInTreeRun Judged(IReadOnlyDictionary<PhoneTextNotInTreeQuestion, PhoneTextNotInTreeAnswer> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneTextNotInTreeRun
        {
            SnapshotsRead = SnapshotsRead,
            OutOfStepSnapshots = OutOfStepSnapshots,
            OutOfStepLines = OutOfStepLines,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        judged._entries.AddRange(_entries);
        judged._visitsRead.UnionWith(_visitsRead);
        var kept = new Dictionary<PhoneTextNotInTreeQuestion, PhoneTextNotInTreeAnswer>();
        var findings = new List<PhoneFinding>();
        foreach (var question in Questions)
        {
            if (!answers.TryGetValue(question, out var answer))
            {
                continue;
            }

            kept[question] = answer;
            if (answer.Outcome != PhoneTextNotInTreeOutcome.Finding)
            {
                continue;
            }

            var within = question.ContainerText is { } holder ? $" in {question.ContainerClass ?? "an item"} \"{holder}\"" : "";
            var finding = new PhoneFinding(PhoneCheckNames.TextNotInTree, answer.Inference.Level, question.Visit, question.Identity, $"'{question.Ocr}'{within}")
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

    // Rule 7: inside an item, not a WebView, covering at most a quarter of
    // the screen, with its own text, hint or description.
    private static bool Misread(PhoneTextLine line, PhoneBounds frame)
    {
        if (line.Container is not { } container || frame.IsEmpty ||
            (container.Node.ClassName ?? "").Contains("WebView", StringComparison.Ordinal))
        {
            return false;
        }

        var own = new[] { container.Text, container.Node.Hint, container.Node.ContentDescription }.Any(part => !string.IsNullOrWhiteSpace(part));
        var share = (double)container.Bounds.Width * container.Bounds.Height / ((double)frame.Width * frame.Height);
        return own && share <= MostMisreadShare;
    }

    // What was measured of a line, for its finding.
    private static string Measured(PhoneCheckItem? container, bool heldByHidden)
    {
        var where = container is null
            ? "text OCR read on the picture that no item of the accessibility tree holds"
            : $"text OCR read on the picture, inside {container.Label}, that no item of the accessibility tree holds";
        return heldByHidden ? where + ", though an item marked not shown holds the same text" : where;
    }

    // An item's own words, as the matching takes them: its text, hint,
    // description, state, tooltip, error and titles.
    private static string LabelOf(PhoneCheckItem item) =>
        string.Join(' ', new[]
        {
            item.Node.Text, item.Node.Hint, item.Node.ContentDescription, item.Node.StateDescription, item.Node.Tooltip,
            item.Node.Error, item.Node.PaneTitle, item.Node.ContainerTitle, item.Node.SupplementalDescription,
        }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

    private static List<string> Words(string text) =>
        [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(PhoneTextMatching.Spelling).Where(word => word.Length >= 2)];

    private static bool InAny(PhoneBounds box, List<PhoneCheckItem> images)
    {
        var x = (box.Left + box.Right) / 2;
        var y = (box.Top + box.Bottom) / 2;
        return images.Any(each => x >= each.Bounds.Left && x < each.Bounds.Right && y >= each.Bounds.Top && y < each.Bounds.Bottom);
    }

    private static string Start(string text) =>
        text.Length <= MostCharacters ? text : text[..(MostCharacters - 3)] + "...";

    private int Count(PhoneTextNotInTreeState state) => _entries.Count(entry => entry.State == state);

    // The words of a set of items, and their spellings run together: a
    // line is held where every word is among them, a word of four or more
    // letters within one letter of one counting, or its spelling is in the
    // run (rules 5, 6 and 2c).
    private sealed class Shown(HashSet<string> words, string run)
    {
        public static Shown Of(IEnumerable<PhoneCheckItem> items)
        {
            var each = items.Select(item => Words(LabelOf(item))).Where(list => list.Count > 0).ToList();
            return new Shown(each.SelectMany(list => list).ToHashSet(StringComparer.Ordinal), string.Concat(each.Select(list => string.Concat(list))));
        }

        public bool Holds(string spelt, List<string> lineWords)
        {
            if (spelt.Length == 0)
            {
                return false;
            }

            if (run.Contains(spelt, StringComparison.Ordinal))
            {
                return true;
            }

            return lineWords.Count > 0 && lineWords.All(word => words.Contains(word) ||
                (word.Length >= 4 && words.Any(other => Math.Abs(other.Length - word.Length) <= 1 && PhoneImagesOfTextRun.WithinOne(other, word))));
        }
    }
}
