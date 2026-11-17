using System.Diagnostics;

namespace Recorder.Session;

/// <summary>
/// One described image a model is to be asked about (android-checks.md,
/// step 21): the visit and the image's identity, the first time it was
/// seen on a picture in step with the tree, with that picture and the
/// image's bounds there, whether the image is a control, its own
/// description (a control's name), what a screen reader says for the item
/// that says it, and what a screen reader says on the screen, with the
/// image's place marked.
/// </summary>
public sealed record PhoneDescribedImageQuestion(
    PhoneVisit Visit,
    string Identity,
    PhoneFindingTime Time,
    PhoneContrastPicture Picture,
    bool IsControl,
    string Description,
    PhoneDescriptionSpeaker SpokenBy,
    string Spoken,
    string ScreenText)
{
    public string App => Visit.Screen.Package;

    public string ScreenName => Visit.Screen.Name;

    public PhoneBounds Bounds => Time.Bounds;

    /// <summary>The check whose finding the question's answer can make.</summary>
    public string Check => IsControl ? PhoneCheckNames.IconContradictsName : PhoneCheckNames.DescriptionNotMatchingImage;
}

/// <summary>Which item a screen reader says an image's description for.</summary>
public enum PhoneDescriptionSpeaker
{
    /// <summary>No item a screen reader stops on holds the image.</summary>
    Nothing,

    /// <summary>The image itself.</summary>
    Image,

    /// <summary>The nearest item holding the image that a screen reader stops on, as part of what it says for that item.</summary>
    Holder,
}

/// <summary>What the model's answers made of one described image.</summary>
public enum PhoneDescribedImageOutcome
{
    /// <summary>The icon fits its control's name, or the description matches its image: no finding (rule 7).</summary>
    Fits,

    /// <summary>The control's image is not an icon, so its name is not judged against it (rule 4): no finding.</summary>
    NotAnIcon,

    /// <summary>A warning: a contradiction or mismatch, or the model could not tell, or gave two answers (rule 7).</summary>
    Finding,
}

/// <summary>One described image's answer: what it made of the image, and the model's inference.</summary>
public sealed record PhoneDescribedImageAnswer(PhoneDescribedImageOutcome Outcome, PhoneInference Inference);

/// <summary>
/// Step 21 of android-checks.md, the icon and description checks (WCAG
/// 1.1.1): an image in the tree, an ImageView or a class drawn from one,
/// visible, important for accessibility, with a description or text of
/// its own. Eight rules, chosen by the owner on 2026-11-17 ("Build rules 1
/// to 8 for the icon and description checks as proposed") from a probe of
/// 51 different described images on 20 recordings: (1) two findings, one
/// issue each, "Icon that contradicts its control's name" for an image
/// that is a control and "Description that does not match its image" for
/// one that is not; (2) the images chosen, as above; (3) an image on a
/// picture out of step with the tree is not asked about there, as the
/// images-of-text check finds it; (4) a control's image is first asked
/// what kind of image it is, and its name is judged against it unless both
/// answers agree it is not an icon (AndroidDescribedImages); (5) the
/// questions as measured, reason first, with what a screen reader says on
/// the screen; (6) the answer for an image is kept by its own pixels, its
/// description and what a screen reader says on the screen; (7) a
/// contradiction or mismatch is a warning recommending a person verify it,
/// high impact if confirmed, cannot tell or two answers a warning to check
/// manually, and fits or matches no finding, counted; (8) with no model,
/// no finding for each image, and the counts say how many were not judged.
/// The pictures are needed for rule 3, so a snapshot OCR did not read is
/// not checked.
/// </summary>
public sealed class PhoneDescribedImageRun
{
    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, int> _imagesByVisit = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];
    private readonly Dictionary<(PhoneVisit, string), List<PhoneFindingTime>> _times = [];
    private readonly Dictionary<(PhoneVisit, string), string> _labels = [];

    private PhoneDescribedImageRun()
    {
    }

    public static PhoneDescribedImageRun Empty { get; } = new();

    /// <summary>The warnings the model's answers made; none before any answer (rule 8).</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>The described images on the pictures OCR read, once each in a visit.</summary>
    public int Images { get; private set; }

    /// <summary>The described images that are controls, once each in a visit.</summary>
    public int Controls { get; private set; }

    /// <summary>The described images on a picture out of step with the tree (rule 3), once for each snapshot.</summary>
    public int OutOfStep { get; private set; }

    /// <summary>The images to be asked about, one for each image in a visit seen on a picture in step, in screen and time order.</summary>
    public IReadOnlyList<PhoneDescribedImageQuestion> Questions { get; private set; } = [];

    /// <summary>The questions answered, with what each answer made of its image; empty until a model's answers are given.</summary>
    public IReadOnlyDictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer> Answers { get; private set; } =
        new Dictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer>();

    /// <summary>The icons judged to fit their control's names, or descriptions to match their images, not confirmed.</summary>
    public int Fit => Answers.Values.Count(answer => answer.Outcome == PhoneDescribedImageOutcome.Fits);

    /// <summary>The controls whose images were judged not to be icons, so not judged against their names.</summary>
    public int NotIcons => Answers.Values.Count(answer => answer.Outcome == PhoneDescribedImageOutcome.NotAnIcon);

    /// <summary>The questions with no answer, not judged.</summary>
    public int Unanswered => Questions.Count - Answers.Count;

    /// <summary>
    /// Every inference the model's answers made, findings or not, so that
    /// a report says AI was used even where every answer was that the
    /// icon or description fits.
    /// </summary>
    public IEnumerable<PhoneInference> Inferences => Answers.Values.Select(answer => answer.Inference);

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its images were checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    /// <summary>The described images in the visit, once each.</summary>
    public int ImagesIn(PhoneVisit visit) =>
        _imagesByVisit.TryGetValue(visit, out var count) ? count : 0;

    public static PhoneDescribedImageRun Run(
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
        var run = new PhoneDescribedImageRun();
        var seen = new HashSet<(PhoneVisit, string)>();
        var controls = new HashSet<(PhoneVisit, string)>();
        var questions = new Dictionary<(PhoneVisit, string), PhoneDescribedImageQuestion>();
        foreach (var snapshot in measured.Snapshots)
        {
            // Rule 3 needs OCR's words to tell a picture out of step.
            if (snapshot.Window is not { } window || string.IsNullOrEmpty(snapshot.Engine))
            {
                continue;
            }

            run.SnapshotsRead++;
            var visit = snapshot.Visit;
            run._visitsRead.Add(visit);
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            var images = window.Items
                .Where(item => item.InstanceOf(PhoneTextMatching.ImageViewClass) && item.Node.VisibleToUser && !item.Bounds.IsEmpty)
                .ToList();
            var step = new Lazy<(bool OutOfStep, int Unexplained, int Found)>(() =>
                PhoneImagesOfTextRun.OutOfStepAt(snapshot, images, talkBack, speech, accessibility));
            foreach (var image in images)
            {
                // Rule 2: a description or text of the image's own.
                if (Description(image) is not { Length: > 0 } description)
                {
                    continue;
                }

                var key = (visit, image.Identity);
                var isControl = IsControl(image);
                if (seen.Add(key))
                {
                    run._imagesByVisit[visit] = run.ImagesIn(visit) + 1;
                    if (isControl)
                    {
                        controls.Add(key);
                    }
                }

                // Rule 3: not asked about on a picture out of step.
                if (step.Value.OutOfStep)
                {
                    run.OutOfStep++;
                    continue;
                }

                var measuredText = isControl
                    ? $"an image that is a control, named \"{description}\"; {Source(snapshot.Picture.Kind)}"
                    : $"an image that is not a control, with the description \"{description}\"; {Source(snapshot.Picture.Kind)}";
                if (!run._times.TryGetValue(key, out var times))
                {
                    run._times[key] = times = [];
                }

                if (times.Count > 0 && times[^1].Snapshot.EventId == snapshot.Settled.EventId)
                {
                    continue;
                }

                var time = new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, image.Bounds, image.TreeKey, measuredText, state);
                times.Add(time);
                if (!questions.ContainsKey(key))
                {
                    var (speaker, spoken) = Speaker(image);
                    questions[key] = new PhoneDescribedImageQuestion(
                        visit, image.Identity, time, snapshot.Picture, isControl, description, speaker, spoken,
                        PhoneImageAlternativeRun.ScreenText(window, image));
                    run._labels[key] = image.Label;
                }
            }
        }

        run.Images = seen.Count;
        run.Controls = controls.Count;
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
    /// finding (rule 7) is a finding of its question's check, at the
    /// answer's level, with every time the image was seen in the visit on
    /// a picture in step; an answer that the icon fits or the description
    /// matches, or that the image is not an icon, is counted, not a
    /// finding. The note says what became of the questions.
    /// </summary>
    public PhoneDescribedImageRun Judged(IReadOnlyDictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneDescribedImageRun
        {
            SnapshotsRead = SnapshotsRead,
            Images = Images,
            Controls = Controls,
            OutOfStep = OutOfStep,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        foreach (var (visit, count) in _imagesByVisit)
        {
            judged._imagesByVisit[visit] = count;
        }

        foreach (var (key, times) in _times)
        {
            judged._times[key] = times;
        }

        foreach (var (key, label) in _labels)
        {
            judged._labels[key] = label;
        }

        judged._visitsRead.UnionWith(_visitsRead);
        var kept = new Dictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer>();
        var findings = new List<PhoneFinding>();
        foreach (var question in Questions)
        {
            if (!answers.TryGetValue(question, out var answer))
            {
                continue;
            }

            kept[question] = answer;
            if (answer.Outcome != PhoneDescribedImageOutcome.Finding)
            {
                continue;
            }

            var key = (question.Visit, question.Identity);
            var finding = new PhoneFinding(question.Check, answer.Inference.Level, question.Visit, question.Identity,
                _labels.GetValueOrDefault(key, question.Description))
            {
                Inference = answer.Inference,
            };
            finding._times.AddRange(_times.TryGetValue(key, out var times) ? times : [question.Time]);
            findings.Add(finding);
        }

        judged.Answers = kept;
        judged.Place(findings);
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

    private static string Source(PhonePictureKind kind) =>
        kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video";

    /// <summary>
    /// The image's own description, or where it has none its own text,
    /// trimmed; empty where it has neither or is not important for
    /// accessibility (rule 2).
    /// </summary>
    internal static string Description(PhoneCheckItem image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var node = image.Node;
        if (!node.ImportantForAccessibility)
        {
            return string.Empty;
        }

        return !string.IsNullOrWhiteSpace(node.ContentDescription) ? node.ContentDescription.Trim()
            : !string.IsNullOrWhiteSpace(node.Text) ? node.Text.Trim()
            : string.Empty;
    }

    // An image a screen reader can act on or move to: clickable,
    // long-clickable or focusable, as in the probe.
    private static bool IsControl(PhoneCheckItem image)
    {
        var node = image.Node;
        return node.Clickable || node.LongClickable || node.Focusable || node.ScreenReaderFocusable;
    }

    /// <summary>
    /// The item a screen reader says the image's description for: the
    /// image, where a screen reader stops on it, or else the nearest item
    /// holding it that a screen reader stops on, and what it says there.
    /// </summary>
    internal static (PhoneDescriptionSpeaker Speaker, string Spoken) Speaker(PhoneCheckItem image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (PhoneTreeChecks.ShouldFocusView(image))
        {
            return (PhoneDescriptionSpeaker.Image, PhoneTreeChecks.SpeakableText(image).Trim());
        }

        for (var step = image.Parent; step is not null; step = step.Parent)
        {
            if (PhoneTreeChecks.ShouldFocusView(step))
            {
                return (PhoneDescriptionSpeaker.Holder, PhoneTreeChecks.SpeakableText(step).Trim());
            }
        }

        return (PhoneDescriptionSpeaker.Nothing, string.Empty);
    }
}
