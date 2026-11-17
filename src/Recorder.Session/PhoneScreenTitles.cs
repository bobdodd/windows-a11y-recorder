using System.Diagnostics;
using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// One screen whose title a model is to be asked about (android-checks.md,
/// step 22): the visit and the settled snapshot it was first read on by
/// OCR, its title as a screen reader is given it and where it came from,
/// for Chrome the web page's title within it, what a screen reader says on
/// the screen, the text OCR read on its picture, and the share of the
/// screen that images cover, with whether the picture is given too.
/// </summary>
public sealed record PhoneScreenTitleQuestion(
    PhoneVisit Visit,
    string Identity,
    PhoneFindingTime Time,
    PhoneContrastPicture Picture,
    string Title,
    string TitleSource,
    string? PageTitle,
    string Spoken,
    string Ocr,
    double ImageShare)
{
    public string App => Visit.Screen.Package;

    public string ScreenName => Visit.Screen.Name;

    public PhoneBounds Bounds => Time.Bounds;

    /// <summary>The title judged: the web page's title in Chrome (rule 5, option A), or else the screen's.</summary>
    public string Judged => PageTitle ?? Title;

    /// <summary>Whether the screen's picture is given with its text, as images cover at least half of it (rule 4).</summary>
    public bool Pictured => ImageShare >= PhoneScreenTitleRun.PicturedShare;
}

/// <summary>How a screen's title was found to describe it without a model (rule 3), or that it was not.</summary>
public enum PhoneTitleMatch
{
    /// <summary>The title is none of the screen's headings, lines of OCR text or items a screen reader says.</summary>
    None,

    /// <summary>The title is the text of a heading shown on the screen.</summary>
    Heading,

    /// <summary>The title is a whole line of the text OCR read on the screen's picture.</summary>
    OcrLine,

    /// <summary>The title is all that a screen reader says for one item on the screen.</summary>
    SpokenItem,
}

/// <summary>What became of one screen in the screen title check.</summary>
public enum PhoneScreenTitleState
{
    /// <summary>The screen has no title, which "No screen title" finds (rule 2): not checked here.</summary>
    Untitled,

    /// <summary>The title is the screen's own heading, OCR line or spoken item (rule 3): no finding, and no model asked.</summary>
    Matched,

    /// <summary>The title is for a model to judge (rule 4).</summary>
    Asked,
}

/// <summary>What the model's answers made of one screen's title.</summary>
public enum PhoneScreenTitleOutcome
{
    /// <summary>The title describes the screen: no finding (rule 6).</summary>
    Describes,

    /// <summary>A warning: the title does not describe the screen, or the model could not tell, or gave two answers (rule 6).</summary>
    Finding,
}

/// <summary>One screen title's answer: what it made of the title, and the model's inference.</summary>
public sealed record PhoneScreenTitleAnswer(PhoneScreenTitleOutcome Outcome, PhoneInference Inference);

/// <summary>
/// Step 22 of android-checks.md, the check of a screen title that does
/// not describe its screen (WCAG2ICT 2.4.2), beside "No screen title" and
/// "Screen title shared with other screens". Eight rules, chosen by the
/// owner on 2026-11-17 ("Add the screen title does not describe its
/// screen check with rules 1 to 8 and Chrome option A") from a probe of
/// 50 screens on 20 recordings, with no model: (1) one issue, "Screen
/// title does not describe its screen", made as well as the shared title
/// where both apply, as their remedies differ; (2) each titled screen
/// once, from its first settled snapshot OCR read, an untitled screen
/// being "No screen title"'s; (3) a title that is exactly a heading shown
/// on the screen, a whole line of the text OCR read, or all that a screen
/// reader says for one item, letters and digits compared, describes its
/// screen with no model asked (16 of the 50), and a title only within
/// such text does not; (4) any other is asked of a model with what a
/// screen reader says on the screen and the text OCR read, each marked
/// with where it came from, and the screen's picture where images cover
/// at least half of it (<see cref="PicturedShare"/>: 5 of the 50, the
/// next 29%); (5) in Chrome, whose window title is "Chrome: " and the web
/// page's, the page's title is judged (option A); (6) a title judged not
/// to describe its screen is a warning for a person to verify, medium
/// impact if confirmed, cannot tell or two answers a warning to check
/// manually, and describes no finding, counted; (7) with no model, no
/// finding, and the counts say how many were not judged; (8) an answer
/// is reused only for the same title, screen reader text, OCR text and,
/// where given, picture.
/// </summary>
public sealed class PhoneScreenTitleRun
{
    /// <summary>The share of a screen images cover from which its picture is given (rule 4, measured 2026-11-17).</summary>
    public const double PicturedShare = 0.5;

    /// <summary>The points a side of the grid the share of a screen images cover is sampled on.</summary>
    public const int Grid = 60;

    /// <summary>The most characters of what a screen reader says, or of OCR's text, given to a model; the start is kept.</summary>
    public const int MostCharacters = 4000;

    /// <summary>The packages whose window title is "Chrome: " and the web page's title (rule 5).</summary>
    public static readonly IReadOnlySet<string> ChromePackages =
        new HashSet<string>(["com.android.chrome", "com.chrome.beta", "com.chrome.dev", "com.chrome.canary"], StringComparer.Ordinal);

    private const string ChromePrefix = "Chrome: ";

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneScreen, (PhoneScreenTitleState State, PhoneTitleMatch Match, string? Title, PhoneVisit Visit)> _screens = [];

    private PhoneScreenTitleRun()
    {
    }

    public static PhoneScreenTitleRun Empty { get; } = new();

    /// <summary>The warnings the model's answers made; none before any answer (rule 7).</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>The screens OCR read a picture of, once each.</summary>
    public int Screens => _screens.Count;

    /// <summary>The screens with no title, left to "No screen title" (rule 2).</summary>
    public int Untitled => _screens.Values.Count(screen => screen.State == PhoneScreenTitleState.Untitled);

    /// <summary>The screens whose titles were found to describe them with no model (rule 3).</summary>
    public int Matched => _screens.Values.Count(screen => screen.State == PhoneScreenTitleState.Matched);

    /// <summary>The screens to be asked about, once each, in screen order (rule 4).</summary>
    public IReadOnlyList<PhoneScreenTitleQuestion> Questions { get; private set; } = [];

    /// <summary>The questions answered, with what each answer made of its title; empty until a model's answers are given.</summary>
    public IReadOnlyDictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer> Answers { get; private set; } =
        new Dictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer>();

    /// <summary>The titles judged to describe their screens, not confirmed.</summary>
    public int Described => Answers.Values.Count(answer => answer.Outcome == PhoneScreenTitleOutcome.Describes);

    /// <summary>The questions with no answer, not judged.</summary>
    public int Unanswered => Questions.Count - Answers.Count;

    /// <summary>The questions given with only text, for the model for text-based checks.</summary>
    public int TextQuestions => Questions.Count(question => !question.Pictured);

    /// <summary>The questions given with the screen's picture, for the model for vision-based checks.</summary>
    public int PicturedQuestions => Questions.Count(question => question.Pictured);

    /// <summary>
    /// Every inference the model's answers made, findings or not, so that
    /// a report says AI was used even where every title was judged to
    /// describe its screen.
    /// </summary>
    public IEnumerable<PhoneInference> Inferences => Answers.Values.Select(answer => answer.Inference);

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>
    /// What became of a screen: null where OCR read no picture of it, and
    /// otherwise its state, how its title matched, its title, and the
    /// visit it was checked on.
    /// </summary>
    public (PhoneScreenTitleState State, PhoneTitleMatch Match, string? Title, PhoneVisit Visit)? Of(PhoneScreen screen) =>
        _screens.TryGetValue(screen, out var found) ? found : null;

    public static PhoneScreenTitleRun Run(PhoneContrastRun measured, PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneScreenTitleRun();
        var questions = new List<PhoneScreenTitleQuestion>();
        foreach (var snapshot in measured.Snapshots)
        {
            if (snapshot.Window is not { Items.Count: > 0 } window || string.IsNullOrEmpty(snapshot.Engine))
            {
                continue;
            }

            run.SnapshotsRead++;
            var visit = snapshot.Visit;

            // Rule 2: each screen once, from its first snapshot OCR read.
            if (run._screens.ContainsKey(visit.Screen))
            {
                continue;
            }

            if (PhoneScreenChecks.ScreenTitle(window) is not { } title)
            {
                run._screens[visit.Screen] = (PhoneScreenTitleState.Untitled, PhoneTitleMatch.None, null, visit);
                continue;
            }

            var page = PageTitle(visit.Screen.Package, title.Title);
            var judged = page ?? title.Title;
            var stops = PhoneImageAlternativeRun.Stops(window, null, out _);
            var lines = snapshot.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.OcrText))
                .OrderBy(line => line.Box.Top).ThenBy(line => line.Box.Left)
                .Select(line => line.OcrText!.Trim())
                .ToList();
            var match = Match(judged, PhoneScreenChecks.Headings(window).Select(item => PhoneTreeChecks.SpeakableText(item)), lines, stops);
            if (match != PhoneTitleMatch.None)
            {
                run._screens[visit.Screen] = (PhoneScreenTitleState.Matched, match, judged, visit);
                continue;
            }

            run._screens[visit.Screen] = (PhoneScreenTitleState.Asked, PhoneTitleMatch.None, judged, visit);
            var root = window.Items[0];
            var share = ImageShare(root.Bounds, window.Items
                .Where(item => item.InstanceOf(PhoneTextMatching.ImageViewClass) && item.Node.VisibleToUser && !item.Bounds.IsEmpty)
                .Select(item => item.Bounds)
                .ToList());
            var measuredText = page is null
                ? $"its {title.Source}, \"{title.Title}\""
                : $"its {title.Source}, \"{title.Title}\", with the web page's title \"{page}\"";
            var time = new PhoneFindingTime(
                snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, root.Bounds, root.TreeKey, measuredText,
                PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds));
            questions.Add(new PhoneScreenTitleQuestion(
                visit, root.Identity, time, snapshot.Picture, title.Title, title.Source, page,
                Start(string.Join(" | ", stops)), Start(string.Join(" / ", lines)), share));
        }

        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Questions = [.. questions
            .OrderBy(question => screenOrder.TryGetValue(question.Visit.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(question => question.Visit.Start)];
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// The run with the model's answers given: each answer that makes a
    /// finding (rule 6) is a finding on the visit its screen was checked
    /// on, at the answer's level; an answer that the title describes its
    /// screen is counted, not a finding. The note says what became of the
    /// questions.
    /// </summary>
    public PhoneScreenTitleRun Judged(IReadOnlyDictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneScreenTitleRun
        {
            SnapshotsRead = SnapshotsRead,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        foreach (var (screen, state) in _screens)
        {
            judged._screens[screen] = state;
        }

        var kept = new Dictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer>();
        var findings = new List<PhoneFinding>();
        foreach (var question in Questions)
        {
            if (!answers.TryGetValue(question, out var answer))
            {
                continue;
            }

            kept[question] = answer;
            if (answer.Outcome != PhoneScreenTitleOutcome.Finding)
            {
                continue;
            }

            var finding = new PhoneFinding(PhoneCheckNames.TitleNotDescriptive, answer.Inference.Level, question.Visit, question.Identity, question.Title)
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

    /// <summary>
    /// The web page's title within Chrome's window title, "Chrome: " and
    /// the page's (rule 5, option A); null for any other app or title.
    /// </summary>
    public static string? PageTitle(string package, string title)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(title);
        return ChromePackages.Contains(package) && title.StartsWith(ChromePrefix, StringComparison.Ordinal) &&
            title[ChromePrefix.Length..].Trim() is { Length: > 0 } page
            ? page
            : null;
    }

    /// <summary>
    /// How the title matches the screen's own text (rule 3): exactly a
    /// heading, a line OCR read or an item a screen reader says, letters
    /// and digits compared; a title with neither matches nothing.
    /// </summary>
    public static PhoneTitleMatch Match(string title, IEnumerable<string> headings, IEnumerable<string> ocrLines, IEnumerable<string> spoken)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(headings);
        ArgumentNullException.ThrowIfNull(ocrLines);
        ArgumentNullException.ThrowIfNull(spoken);
        var wanted = Letters(title);
        if (wanted.Length == 0)
        {
            return PhoneTitleMatch.None;
        }

        return headings.Any(text => Letters(text) == wanted) ? PhoneTitleMatch.Heading
            : ocrLines.Any(text => Letters(text) == wanted) ? PhoneTitleMatch.OcrLine
            : spoken.Any(text => Letters(text) == wanted) ? PhoneTitleMatch.SpokenItem
            : PhoneTitleMatch.None;
    }

    /// <summary>The share of the frame's area under any of the boxes, sampled on a grid of <see cref="Grid"/> points a side (rule 4).</summary>
    public static double ImageShare(PhoneBounds frame, IReadOnlyList<PhoneBounds> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        if (frame.IsEmpty || boxes.Count == 0)
        {
            return 0;
        }

        var covered = 0;
        for (var i = 0; i < Grid; i++)
        {
            for (var j = 0; j < Grid; j++)
            {
                var x = frame.Left + ((frame.Right - frame.Left) * (i + 0.5) / Grid);
                var y = frame.Top + ((frame.Bottom - frame.Top) * (j + 0.5) / Grid);
                covered += boxes.Any(box => x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom) ? 1 : 0;
            }
        }

        return (double)covered / (Grid * Grid);
    }

    private static string Letters(string text) =>
        new([.. text.ToLower(CultureInfo.InvariantCulture).Where(char.IsLetterOrDigit)]);

    private static string Start(string text) =>
        text.Length <= MostCharacters ? text : text[..(MostCharacters - 3)] + "...";
}
