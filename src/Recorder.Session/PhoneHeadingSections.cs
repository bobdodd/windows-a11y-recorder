using System.Diagnostics;
using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// One heading a model is to be asked about (android-checks.md, step 24):
/// the visit and the settled snapshot it was first seen on, the heading's
/// text as a screen reader says it, whether it is marked as a heading or
/// is text the check of text shown as a heading judged one, and its
/// section: what a screen reader says on screen in it, what the tree holds
/// in it that is not on screen, the text OCR read in it, the share of it
/// images cover, and whether it may continue below the screen; with the
/// screen's title, as the model is given it (rule 5).
/// </summary>
public sealed record PhoneHeadingSectionQuestion(
    PhoneVisit Visit,
    string Identity,
    PhoneFindingTime Time,
    PhoneContrastPicture Picture,
    string Heading,
    bool Marked,
    string? ScreenTitle,
    string Spoken,
    string OffScreen,
    string Ocr,
    double ImageShare,
    bool MayContinue)
{
    public string App => Visit.Screen.Package;

    public string ScreenName => Visit.Screen.Name;

    /// <summary>The heading's bounds on the picture.</summary>
    public PhoneBounds Bounds => Time.Bounds;

    /// <summary>Whether the screen's picture is given with the section's text, as images cover at least half of the section (rule 6).</summary>
    public bool Pictured => ImageShare >= PhoneHeadingSectionRun.PicturedShare;

    /// <summary>Whether part of the section is only in the accessibility tree, not on screen (rule 7).</summary>
    public bool PartlyFromTree => OffScreen.Length > 0;
}

/// <summary>What became of one heading in the check of a heading that does not describe its section.</summary>
public enum PhoneHeadingSectionState
{
    /// <summary>Text not marked as a heading that the check of text shown as a heading has not judged a heading (rule 2): not checked.</summary>
    NotJudgedHeading,

    /// <summary>The heading is the screen's title, left to the screen title check (rule 3): no model asked.</summary>
    ScreenTitle,

    /// <summary>Nothing is under the heading, on screen, in the tree or in the text OCR read (rule 4): not judged.</summary>
    Empty,

    /// <summary>The heading is for a model to judge (rule 5).</summary>
    Asked,
}

/// <summary>Where a heading not marked as one stands in the check of text shown as a heading (rule 2).</summary>
public enum PhoneHeadingVerdict
{
    /// <summary>Marked as a heading for a screen reader.</summary>
    Marked,

    /// <summary>Not marked, and the model judged it a heading.</summary>
    Heading,

    /// <summary>Not marked, and the model judged it not a heading.</summary>
    NotHeading,

    /// <summary>Not marked, and the model could not tell, or gave two answers.</summary>
    Unsettled,

    /// <summary>Not marked, and not yet judged.</summary>
    NotJudged,
}

/// <summary>One heading as the check found it, once a screen: its text, whether it is marked, what became of it, and the visit it was checked on.</summary>
public sealed record PhoneHeadingSectionEntry(PhoneVisit Visit, string Heading, PhoneHeadingVerdict Verdict, PhoneHeadingSectionState State);

/// <summary>What the model's answers made of one heading.</summary>
public enum PhoneHeadingSectionOutcome
{
    /// <summary>The heading describes its section: no finding (rule 8).</summary>
    Describes,

    /// <summary>A warning: the heading does not describe its section, or the model could not tell, or gave two answers (rule 8).</summary>
    Finding,
}

/// <summary>One heading's answer: what it made of the heading, and the model's inference.</summary>
public sealed record PhoneHeadingSectionAnswer(PhoneHeadingSectionOutcome Outcome, PhoneInference Inference);

/// <summary>
/// Step 24 of android-checks.md, the check of a heading that does not
/// describe its section (WCAG 2.4.6), for headings marked as headings and
/// for text the check of text shown as a heading (step 19) judged one. Ten
/// rules, chosen by the owner on 2026-11-17 ("Build the heading check with
/// rules 1 to 10 as revised, with the same-area and scrolling changes")
/// from probes of 79 headings on 20 recordings, with no model: (1) one
/// issue, "Heading does not describe its section", beside the issue of
/// text shown as a heading but not marked as one, both made where both
/// apply; (2) marked headings, and unmarked text the model judged a
/// heading, each once a screen from its first settled snapshot OCR read,
/// unmarked text judged not a heading, unsettled or not yet judged being
/// counted, not checked; (3) a heading that is the screen's title,
/// letters and digits compared, is left to the screen title check; (4) a
/// heading with nothing under it, on screen, in the tree or in the text
/// OCR read, is counted as having its section not on screen; (5) the
/// section is the tree's items after the heading up to the next heading,
/// on screen or off it, in the heading's scrolling area or one inside it
/// (all items where the heading is in none), those on screen not above
/// the heading, and the text OCR read between the heading and the next
/// heading below it, within the scrolling area, each marked with where it
/// came from; (6) the picture is given where images cover at least half
/// of the section; (7) a section partly only in the tree says so, and a
/// last section on screen whose area can scroll down may continue; (8) a
/// heading judged not to describe its section is a warning for a person
/// to verify, medium impact if confirmed, cannot tell or two answers a
/// warning to check manually, and describes no finding, counted; (9) the
/// project's model for text-based checks, or for vision-based checks where
/// pictured, asked twice, with no finding and no model; (10) an answer is
/// reused only for the same heading, section texts and, where given,
/// picture.
/// </summary>
public sealed class PhoneHeadingSectionRun
{
    /// <summary>The share of a section images cover from which the screen's picture is given (rule 6, measured 2026-11-17).</summary>
    public const double PicturedShare = 0.5;

    /// <summary>The most characters of each of a section's texts given to a model; the start is kept.</summary>
    public const int MostCharacters = 4000;

    // How far, in pixels, the next heading or a line OCR read may start
    // above the heading's bottom and still be below it.
    private const int Slack = 2;

    // ACTION_SCROLL_FORWARD and ACTION_SCROLL_BACKWARD, and the ids of
    // android.R.id.accessibilityActionScrollUp and ScrollDown.
    private const int ScrollForward = 0x1000;
    private const int ScrollBackward = 0x2000;
    private const int ScrollUp = 0x01020038;
    private const int ScrollDown = 0x0102003A;

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly List<PhoneHeadingSectionEntry> _entries = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];
    private PhoneContrastRun? _measured;
    private PhoneScreenTimeline? _screens;

    private PhoneHeadingSectionRun()
    {
    }

    public static PhoneHeadingSectionRun Empty { get; } = new();

    /// <summary>The warnings the model's answers made; none before any answer (rule 9).</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>Every heading found, marked or not, once a screen, in the order found.</summary>
    public IReadOnlyList<PhoneHeadingSectionEntry> Entries => _entries;

    /// <summary>The marked headings, once a screen.</summary>
    public int Marked => _entries.Count(entry => entry.Verdict == PhoneHeadingVerdict.Marked);

    /// <summary>The unmarked text judged a heading, once a screen.</summary>
    public int Unmarked => _entries.Count(entry => entry.Verdict == PhoneHeadingVerdict.Heading);

    /// <summary>The unmarked text not checked as not judged a heading (rule 2): judged not a heading, unsettled or not yet judged.</summary>
    public int NotJudgedHeadings => _entries.Count(entry => entry.State == PhoneHeadingSectionState.NotJudgedHeading);

    /// <summary>The headings that are their screens' titles, left to the screen title check (rule 3).</summary>
    public int ScreenTitles => _entries.Count(entry => entry.State == PhoneHeadingSectionState.ScreenTitle);

    /// <summary>The headings with nothing under them, their sections not on screen (rule 4).</summary>
    public int EmptySections => _entries.Count(entry => entry.State == PhoneHeadingSectionState.Empty);

    /// <summary>The headings to be asked about, once a screen each, in screen order (rule 5).</summary>
    public IReadOnlyList<PhoneHeadingSectionQuestion> Questions { get; private set; } = [];

    /// <summary>The questions answered, with what each answer made of its heading; empty until a model's answers are given.</summary>
    public IReadOnlyDictionary<PhoneHeadingSectionQuestion, PhoneHeadingSectionAnswer> Answers { get; private set; } =
        new Dictionary<PhoneHeadingSectionQuestion, PhoneHeadingSectionAnswer>();

    /// <summary>The headings judged to describe their sections, not confirmed.</summary>
    public int Described => Answers.Values.Count(answer => answer.Outcome == PhoneHeadingSectionOutcome.Describes);

    /// <summary>The questions with no answer, not judged.</summary>
    public int Unanswered => Questions.Count - Answers.Count;

    /// <summary>The questions given with only text, for the model for text-based checks.</summary>
    public int TextQuestions => Questions.Count(question => !question.Pictured);

    /// <summary>The questions given with the screen's picture, for the model for vision-based checks.</summary>
    public int PicturedQuestions => Questions.Count(question => question.Pictured);

    /// <summary>
    /// Every inference the model's answers made, findings or not, so that
    /// a report says AI was used even where every heading was judged to
    /// describe its section.
    /// </summary>
    public IEnumerable<PhoneInference> Inferences => Answers.Values.Select(answer => answer.Inference);

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its headings were checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    /// <summary>The headings checked on a visit, each screen's once.</summary>
    public IEnumerable<PhoneHeadingSectionEntry> In(PhoneVisit visit) => _entries.Where(entry => entry.Visit == visit);

    /// <summary>
    /// Runs the rules on a measurement. Unmarked text is taken from the
    /// check of text shown as a heading as it stands: with no answers yet,
    /// none is judged a heading, so the check is run again once its
    /// answers are given.
    /// </summary>
    public static PhoneHeadingSectionRun Run(PhoneContrastRun measured, PhoneScreenTimeline screens, PhoneHeadingLookRun? headingLook)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneHeadingSectionRun { _measured = measured, _screens = screens };
        var verdicts = new Dictionary<(PhoneVisit, string), PhoneHeadingVerdict>();
        foreach (var finding in headingLook?.Findings ?? [])
        {
            verdicts[(finding.Visit, finding.Identity)] = VerdictOf(finding);
        }

        var questions = new List<PhoneHeadingSectionQuestion>();
        var seen = new HashSet<(PhoneScreen, bool, string)>();
        foreach (var snapshot in measured.Snapshots)
        {
            if (snapshot.Window is not { Items.Count: > 0 } window || string.IsNullOrEmpty(snapshot.Engine))
            {
                continue;
            }

            run.SnapshotsRead++;
            var visit = snapshot.Visit;
            run._visitsRead.Add(visit);
            var items = window.Items;
            var shown = new List<(PhoneCheckItem Item, PhoneHeadingVerdict Verdict)>();
            foreach (var item in items)
            {
                if (!item.Node.VisibleToUser || item.Bounds.IsEmpty || PhoneTreeChecks.SpeakableText(item).Trim().Length == 0)
                {
                    continue;
                }

                if (item.Node.Heading)
                {
                    shown.Add((item, PhoneHeadingVerdict.Marked));
                }
                else if (verdicts.TryGetValue((visit, item.Identity), out var verdict))
                {
                    shown.Add((item, verdict));
                }
            }

            if (shown.Count == 0)
            {
                continue;
            }

            // The headings a section ends at (rule 5): those shown that are
            // not judged not to be headings, and marked headings not on screen.
            var ends = shown.Where(entry => entry.Verdict != PhoneHeadingVerdict.NotHeading).Select(entry => entry.Item)
                .Concat(items.Where(item => item.Node.Heading && !item.Node.VisibleToUser))
                .ToHashSet();
            var order = new Dictionary<PhoneCheckItem, int>();
            for (var i = 0; i < items.Count; i++)
            {
                order[items[i]] = i;
            }

            var endsAt = ends.Select(item => order[item]).Order().ToList();
            var title = PhoneScreenChecks.ScreenTitle(window)?.Title;
            var page = title is null ? null : PhoneScreenTitleRun.PageTitle(visit.Screen.Package, title);
            var frame = items[0].Bounds;
            var images = items
                .Where(item => item.InstanceOf(PhoneTextMatching.ImageViewClass) && item.Node.VisibleToUser && !item.Bounds.IsEmpty)
                .Select(item => item.Bounds)
                .ToList();
            var lines = snapshot.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.OcrText))
                .OrderBy(line => line.Box.Top).ThenBy(line => line.Box.Left)
                .ToList();
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            foreach (var (heading, verdict) in shown)
            {
                var text = PhoneTreeChecks.SpeakableText(heading).Trim();
                var marked = verdict == PhoneHeadingVerdict.Marked;

                // Rule 2: each heading once a screen, from its first snapshot.
                if (!seen.Add((visit.Screen, marked, Letters(text))))
                {
                    continue;
                }

                if (verdict is not (PhoneHeadingVerdict.Marked or PhoneHeadingVerdict.Heading))
                {
                    run._entries.Add(new(visit, text, verdict, PhoneHeadingSectionState.NotJudgedHeading));
                    continue;
                }

                // Rule 3: the screen's title is the screen title check's.
                var letters = Letters(text);
                if (letters.Length > 0 && ((title is not null && Letters(title) == letters) || (page is not null && Letters(page) == letters)))
                {
                    run._entries.Add(new(visit, text, verdict, PhoneHeadingSectionState.ScreenTitle));
                    continue;
                }

                var section = SectionOf(heading, items, order, endsAt, ends, frame, lines);
                if (section.Spoken.Count == 0 && section.OffScreen.Count == 0 && section.Lines.Count == 0)
                {
                    run._entries.Add(new(visit, text, verdict, PhoneHeadingSectionState.Empty));
                    continue;
                }

                run._entries.Add(new(visit, text, verdict, PhoneHeadingSectionState.Asked));
                var share = PhoneScreenTitleRun.ImageShare(section.Area, images);
                var measuredText = Measured(marked, section.Spoken.Count, section.OffScreen.Count, section.Lines.Count, section.MayContinue);
                var time = new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, heading.Bounds, heading.TreeKey, measuredText, state);
                questions.Add(new PhoneHeadingSectionQuestion(
                    visit, heading.Identity, time, snapshot.Picture, text, marked, title,
                    Start(string.Join(" | ", section.Spoken)), Start(string.Join(" | ", section.OffScreen)), Start(string.Join(" / ", section.Lines)),
                    share, section.MayContinue));
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
    /// The rules run again on the same measurement, with the check of text
    /// shown as a heading as it now stands, such as once its answers are
    /// given; the run itself where it was not made from a measurement.
    /// </summary>
    public PhoneHeadingSectionRun WithHeadingLook(PhoneHeadingLookRun? headingLook) =>
        _measured is null || _screens is null ? this : Run(_measured, _screens, headingLook);

    /// <summary>
    /// The run with the model's answers given: each answer that makes a
    /// finding (rule 8) is a finding on the visit its heading was checked
    /// on, at the answer's level; an answer that the heading describes its
    /// section is counted, not a finding. The note says what became of
    /// the questions.
    /// </summary>
    public PhoneHeadingSectionRun Judged(IReadOnlyDictionary<PhoneHeadingSectionQuestion, PhoneHeadingSectionAnswer> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneHeadingSectionRun
        {
            _measured = _measured,
            _screens = _screens,
            SnapshotsRead = SnapshotsRead,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        judged._entries.AddRange(_entries);
        judged._visitsRead.UnionWith(_visitsRead);
        var kept = new Dictionary<PhoneHeadingSectionQuestion, PhoneHeadingSectionAnswer>();
        var findings = new List<PhoneFinding>();
        foreach (var question in Questions)
        {
            if (!answers.TryGetValue(question, out var answer))
            {
                continue;
            }

            kept[question] = answer;
            if (answer.Outcome != PhoneHeadingSectionOutcome.Finding)
            {
                continue;
            }

            var finding = new PhoneFinding(PhoneCheckNames.HeadingNotDescriptive, answer.Inference.Level, question.Visit, question.Identity, question.Heading)
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
            if (!judged._byVisit.TryGetValue(finding.Visit, out var ofVisit))
            {
                judged._byVisit[finding.Visit] = ofVisit = [];
            }

            ofVisit.Add(finding);
        }

        return judged;
    }

    /// <summary>Where a finding of the check of text shown as a heading stands (rule 2).</summary>
    public static PhoneHeadingVerdict VerdictOf(PhoneFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.Inference is null
            ? PhoneHeadingVerdict.NotJudged
            : finding.Level switch
            {
                PhoneCheckLevel.Error => PhoneHeadingVerdict.Heading,
                PhoneCheckLevel.Info => PhoneHeadingVerdict.NotHeading,
                _ => PhoneHeadingVerdict.Unsettled,
            };
    }

    /// <summary>
    /// The nearest item a heading or other item is inside that scrolls:
    /// one marked scrollable, or that can scroll forward or back, up or
    /// down; null where there is none.
    /// </summary>
    public static PhoneCheckItem? ScrollingArea(PhoneCheckItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        for (var at = item.Parent; at is not null; at = at.Parent)
        {
            if (at.Node.Scrollable || at.Node.Actions.Any(action => action.Id is ScrollForward or ScrollBackward or ScrollUp or ScrollDown))
            {
                return at;
            }
        }

        return null;
    }

    /// <summary>Whether a scrolling area can scroll further down, by its actions (rule 7).</summary>
    public static bool CanScrollDown(PhoneCheckItem area)
    {
        ArgumentNullException.ThrowIfNull(area);
        return area.Node.Actions.Any(action => action.Id is ScrollForward or ScrollDown);
    }

    // A heading's section (rule 5): the items a screen reader stops on
    // after it, on screen and not above it, and the outermost items after
    // it not on screen that a screen reader would read, up to the next
    // heading in the tree's order, each in the heading's scrolling area or
    // one inside it; the lines OCR read from the heading's bottom to the
    // next heading below it, within the area; the part of the screen
    // they cover; and whether the section may continue below the screen.
    private static (List<string> Spoken, List<string> OffScreen, List<string> Lines, PhoneBounds Area, bool MayContinue) SectionOf(
        PhoneCheckItem heading,
        IReadOnlyList<PhoneCheckItem> items,
        Dictionary<PhoneCheckItem, int> order,
        List<int> endsAt,
        HashSet<PhoneCheckItem> ends,
        PhoneBounds frame,
        List<PhoneTextLine> lines)
    {
        var place = order[heading];
        var next = endsAt.FirstOrDefault(at => at > place, items.Count);
        var area = ScrollingArea(heading);
        bool InArea(PhoneCheckItem item) => area is null || Within(item, area);
        var stops = new List<PhoneCheckItem>();
        var off = new List<PhoneCheckItem>();
        for (var i = place + 1; i < next; i++)
        {
            var item = items[i];
            if (Within(item, heading) || !InArea(item))
            {
                continue;
            }

            if (item.Node.VisibleToUser)
            {
                if (item.Bounds.Bottom > heading.Bounds.Top && PhoneTreeChecks.ShouldFocusView(item) && PhoneTreeChecks.SpeakableText(item).Trim().Length > 0)
                {
                    stops.Add(item);
                }

                continue;
            }

            var node = item.Node;
            var reads = node.ScreenReaderFocusable || node.Clickable || node.Focusable || node.LongClickable ||
                !string.IsNullOrWhiteSpace(node.ContentDescription) || !string.IsNullOrWhiteSpace(item.Text);
            if (node.ImportantForAccessibility && reads && !stops.Any(stop => Within(item, stop)) && !off.Any(stop => Within(item, stop)) &&
                PhoneTreeChecks.SpeakableText(item).Trim().Length > 0)
            {
                off.Add(item);
            }
        }

        var below = ends
            .Where(other => other != heading && other.Node.VisibleToUser && !other.Bounds.IsEmpty && other.Bounds.Top >= heading.Bounds.Bottom - Slack)
            .OrderBy(other => other.Bounds.Top)
            .FirstOrDefault();
        var areaBottom = area is null || area.Bounds.IsEmpty ? frame.Bottom : Math.Min(frame.Bottom, area.Bounds.Bottom);
        var bottom = Math.Max(heading.Bounds.Bottom, below?.Bounds.Top ?? areaBottom);
        var read = lines
            .Where(line => line.Box.Top >= heading.Bounds.Bottom - Slack && line.Box.Top < bottom)
            .Select(line => line.OcrText!.Trim())
            .ToList();
        var mayContinue = below is null && area is not null && CanScrollDown(area);
        return (
            [.. stops.Select(item => PhoneTreeChecks.SpeakableText(item).Trim())],
            [.. off.Select(item => PhoneTreeChecks.SpeakableText(item).Trim())],
            read,
            new PhoneBounds(frame.Left, heading.Bounds.Bottom, frame.Right, bottom),
            mayContinue);
    }

    // What was measured of a heading's section, for its finding (rule 7).
    private static string Measured(bool marked, int spoken, int offScreen, int lines, bool mayContinue)
    {
        var kind = marked ? "a heading" : "text shown as a heading, not marked as one,";
        var parts = string.Create(CultureInfo.CurrentCulture,
            $"{kind} over a section of {Count(spoken, "item")} a screen reader says on screen and {Count(lines, "line")} OCR read");
        if (offScreen > 0)
        {
            parts += string.Create(CultureInfo.CurrentCulture,
                $"; part of the section was judged from the accessibility tree only, as {Count(offScreen, "item")} in it {(offScreen == 1 ? "is" : "are")} not on screen");
        }

        if (mayContinue)
        {
            parts += "; the section may continue below the screen";
        }

        return parts;
    }

    private static string Count(int count, string noun) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {noun}{(count == 1 ? "" : "s")}");

    private static bool Within(PhoneCheckItem item, PhoneCheckItem ancestor)
    {
        for (var at = item.Parent; at is not null; at = at.Parent)
        {
            if (at == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static string Letters(string text) =>
        new([.. text.ToLower(CultureInfo.InvariantCulture).Where(char.IsLetterOrDigit)]);

    private static string Start(string text) =>
        text.Length <= MostCharacters ? text : text[..(MostCharacters - 3)] + "...";
}
