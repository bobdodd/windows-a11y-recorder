using System.Diagnostics;
using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// One image a model is to be asked about (android-checks.md, step 20): the
/// visit and the image's identity, the first time it was judged with the
/// picture and the image's bounds there, and the text of the items beside
/// it, as the app gives it.
/// </summary>
public sealed record PhoneImageAlternativeQuestion(
    PhoneVisit Visit,
    string Identity,
    PhoneFindingTime Time,
    PhoneContrastPicture Picture,
    string Nearby)
{
    public string App => Visit.Screen.Package;

    public string ScreenName => Visit.Screen.Name;

    public PhoneBounds Bounds => Time.Bounds;
}

/// <summary>
/// Step 20 of android-checks.md, an image with no text alternative (WCAG
/// 1.1.1): an image in the tree, an ImageView or a class drawn from one,
/// visible, with no description, text or label of its own, that is not a
/// control (the tree's checks of names judge a control with none) and that
/// no control with a name holds, so a screen reader says nothing for it.
/// Five rules, chosen by the owner on 2026-11-16 from a measurement of 576
/// images on 20 recordings, then 49 candidates: (1) an image at least half
/// of whose area is under or over controls with a name that neither hold
/// it nor are inside it is not checked, as it is drawn under them; (2) an
/// image under 2 dp on its shorter side is not asked about, a line, where
/// the recording holds the density; (3) an image on a picture out of step
/// with the tree is not asked about there, as the images-of-text check
/// finds it; (4) the answer for an image is kept by its own pixels and the
/// text beside it, so the same image is given the first answer again
/// (AndroidImageAlternative); (5) the answers set the level: informative an
/// error, cannot tell a warning, decorative information. Each image left
/// is a warning, and a question for the model chosen for vision-based
/// checks. The pictures are needed for rule 3, so a snapshot OCR did not
/// read is not checked.
/// </summary>
public sealed class PhoneImageAlternativeRun
{
    /// <summary>The share of an image's area that controls with a name may cover before it is not checked (rule 1).</summary>
    public const double MostCovered = 0.5;

    /// <summary>The shorter side, in dp, below which an image is not asked about (rule 2).</summary>
    public const double LeastSideDp = 2;

    /// <summary>The longest text of the items beside an image the model is given.</summary>
    public const int MostNearbyCharacters = 400;

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, int> _imagesByVisit = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];
    private readonly Dictionary<(PhoneVisit, string), PhoneImageAlternativeQuestion> _questions = [];

    private PhoneImageAlternativeRun()
    {
    }

    public static PhoneImageAlternativeRun Empty { get; } = new();

    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The images on the pictures OCR read, counted once for each settled snapshot they were on.</summary>
    public int Images { get; private set; }

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>The images with a description, text or label of their own, once each in a visit.</summary>
    public int Named { get; private set; }

    /// <summary>The images with no name that are controls, left to the tree's checks of names, once each in a visit.</summary>
    public int Controls { get; private set; }

    /// <summary>The images with no name inside a control with a name, which a screen reader says, once each in a visit.</summary>
    public int InNamedControls { get; private set; }

    /// <summary>The images not checked as controls with a name cover at least half of them (rule 1), once each in a visit.</summary>
    public int Covered { get; private set; }

    /// <summary>The images not asked about as under 2 dp on their shorter side (rule 2), once each in a visit.</summary>
    public int Thin { get; private set; }

    /// <summary>The images on a picture out of step with the tree (rule 3), once for each snapshot.</summary>
    public int OutOfStep { get; private set; }

    /// <summary>The images the model chosen for vision-based checks is to be asked about, one for each finding with one, in the findings' order.</summary>
    public IReadOnlyList<PhoneImageAlternativeQuestion> Questions { get; private set; } = [];

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its images were checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    public int ImagesIn(PhoneVisit visit) =>
        _imagesByVisit.TryGetValue(visit, out var count) ? count : 0;

    /// <summary>The question of a finding, or null where the finding is not one for the model, or has its answer.</summary>
    public PhoneImageAlternativeQuestion? QuestionOf(PhoneFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return _questions.TryGetValue((finding.Visit, finding.Identity), out var question) &&
            finding.Level == PhoneCheckLevel.Warning && finding.Inference is null
            ? question
            : null;
    }

    public static PhoneImageAlternativeRun Run(
        PhoneContrastRun measured,
        PhoneScreenTimeline screens,
        PhoneDeviceTimeline device,
        TalkBackSpeechTimeline? speech = null,
        PhoneAccessibilityTimeline? accessibility = null)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(device);
        speech ??= TalkBackSpeechTimeline.Empty;
        accessibility ??= PhoneAccessibilityTimeline.Empty;
        var talkBack = speech.Recorded || accessibility.FocusRecorded;
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneImageAlternativeRun();
        var findings = new Dictionary<(PhoneVisit, string), PhoneFinding>();
        var notJudged = new Dictionary<(PhoneVisit, string), PhoneFinding>();
        var named = new HashSet<(PhoneVisit, string)>();
        var controls = new HashSet<(PhoneVisit, string)>();
        var inNamed = new HashSet<(PhoneVisit, string)>();
        var covered = new HashSet<(PhoneVisit, string)>();
        var thin = new HashSet<(PhoneVisit, string)>();
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
            var perDp = device.DensityAt(snapshot.TakenBy.MonotonicNanoseconds)?.PixelsPerDp;
            var images = window.Items
                .Where(item => item.InstanceOf(PhoneTextMatching.ImageViewClass) && item.Node.VisibleToUser && !item.Bounds.IsEmpty)
                .ToList();
            var step = new Lazy<(bool OutOfStep, int Unexplained, int Found)>(() =>
                PhoneImagesOfTextRun.OutOfStepAt(snapshot, images, talkBack, speech, accessibility));
            foreach (var image in images)
            {
                run.Images++;
                run._imagesByVisit[visit] = run.ImagesIn(visit) + 1;
                var key = (visit, image.Identity);
                if (HasOwnName(image))
                {
                    named.Add(key);
                    continue;
                }

                if (IsControl(image))
                {
                    controls.Add(key);
                    continue;
                }

                if (NamedControlHolding(image))
                {
                    inNamed.Add(key);
                    continue;
                }

                // Rule 1: drawn under or over controls with a name.
                var share = CoveredShare(image, window);
                if (share >= MostCovered)
                {
                    covered.Add(key);
                    continue;
                }

                // Rule 2: a line, where the density is known.
                var bounds = image.Bounds;
                var shorter = Math.Min(bounds.Width, bounds.Height);
                if (perDp is { } density && shorter / density < LeastSideDp)
                {
                    thin.Add(key);
                    continue;
                }

                var size = perDp is { } known
                    ? string.Create(CultureInfo.CurrentCulture, $"{bounds.Width:N0} by {bounds.Height:N0} pixels ({bounds.Width / known:0.#} by {bounds.Height / known:0.#} dp)")
                    : string.Create(CultureInfo.CurrentCulture, $"{bounds.Width:N0} by {bounds.Height:N0} pixels");

                // Rule 3: a picture out of step with the tree is not asked
                // about; the image is a check not run where no picture of
                // the visit is in step.
                if (step.Value.OutOfStep)
                {
                    run.OutOfStep++;
                    var why = $"an image of {size} with no text alternative; not judged, as {PhoneImagesOfTextRun.OutOfStepText(step.Value.Unexplained, step.Value.Found)}; {Source(snapshot.Picture.Kind)}";
                    Add(notJudged, PhoneCheckLevel.NotRun, why);
                    continue;
                }

                var nearby = Nearby(image);
                var measuredText = $"an image of {size} with no description, text or label, that no control with a name holds, so a screen reader says nothing for it; {NotJudged}; {Source(snapshot.Picture.Kind)}";
                var time = Add(findings, PhoneCheckLevel.Warning, measuredText);
                if (time is not null && !run._questions.ContainsKey(key))
                {
                    run._questions[key] = new PhoneImageAlternativeQuestion(visit, image.Identity, time, snapshot.Picture, nearby);
                }

                PhoneFindingTime? Add(Dictionary<(PhoneVisit, string), PhoneFinding> into, PhoneCheckLevel level, string text)
                {
                    if (!into.TryGetValue(key, out var finding))
                    {
                        into[key] = finding = new PhoneFinding(PhoneCheckNames.ImageNoTextAlternative, level, visit, image.Identity, image.Label);
                    }

                    if (finding._times.Count > 0 && finding._times[^1].Snapshot.EventId == snapshot.Settled.EventId)
                    {
                        return null;
                    }

                    var added = new PhoneFindingTime(
                        snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, image.Bounds, image.TreeKey, text, state);
                    finding._times.Add(added);
                    return added;
                }
            }
        }

        foreach (var (key, finding) in notJudged)
        {
            findings.TryAdd(key, finding);
        }

        (run.Named, run.Controls, run.InNamedControls, run.Covered, run.Thin) =
            (named.Count, controls.Count, inNamed.Count, covered.Count, thin.Count);
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
            .Where(finding => finding.Level == PhoneCheckLevel.Warning)
            .Select(finding => run._questions.GetValueOrDefault((finding.Visit, finding.Identity)))
            .OfType<PhoneImageAlternativeQuestion>()];
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// The run with the model's answers given: each finding of a question
    /// answered takes the answer's level and words, and every other finding
    /// is as it was. The note says what became of the questions.
    /// </summary>
    public PhoneImageAlternativeRun Judged(IReadOnlyDictionary<PhoneImageAlternativeQuestion, PhoneInference> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneImageAlternativeRun
        {
            Images = Images,
            SnapshotsRead = SnapshotsRead,
            Named = Named,
            Controls = Controls,
            InNamedControls = InNamedControls,
            Covered = Covered,
            Thin = Thin,
            OutOfStep = OutOfStep,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        foreach (var (key, question) in _questions)
        {
            judged._questions[key] = question;
        }

        foreach (var (visit, count) in _imagesByVisit)
        {
            judged._imagesByVisit[visit] = count;
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

    private const string NotJudged = "whether it gives information is not judged";

    private static string Source(PhonePictureKind kind) =>
        kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video";

    // A description, text or label of the image's own.
    private static bool HasOwnName(PhoneCheckItem image) =>
        !string.IsNullOrWhiteSpace(image.Node.ContentDescription) || !string.IsNullOrWhiteSpace(image.Node.Text) || image.Node.LabeledBy.Count > 0;

    // An item a screen reader can act on or move to: visible, important
    // for accessibility, and clickable, long-clickable or focusable.
    private static bool IsControl(PhoneCheckItem item)
    {
        var node = item.Node;
        return node.VisibleToUser && node.ImportantForAccessibility &&
            (node.Clickable || node.LongClickable || node.Focusable || node.ScreenReaderFocusable);
    }

    // The nearest control holding the image has a name, so a screen reader
    // says that name for the image's place.
    private static bool NamedControlHolding(PhoneCheckItem image)
    {
        for (var step = image.Parent; step is not null; step = step.Parent)
        {
            if (IsControl(step))
            {
                return PhoneTreeChecks.SpeakableText(step).Trim().Length > 0;
            }
        }

        return false;
    }

    /// <summary>
    /// The share of the image's area under or over the controls with a name
    /// that neither hold it nor are inside it (rule 1), on a grid of at most
    /// 60 points along the image's shorter side.
    /// </summary>
    internal static double CoveredShare(PhoneCheckItem image, PhoneCheckWindow window)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(window);
        var bounds = image.Bounds;
        var holding = new HashSet<PhoneCheckItem>();
        for (var step = image.Parent; step is not null; step = step.Parent)
        {
            holding.Add(step);
        }

        var over = window.Items
            .Where(item => item != image && !holding.Contains(item) && !Inside(item, image) && IsControl(item) && !item.Bounds.IsEmpty &&
                Overlaps(item.Bounds, bounds) && PhoneTreeChecks.SpeakableText(item).Trim().Length > 0)
            .Select(item => item.Bounds)
            .ToList();
        if (over.Count == 0)
        {
            return 0;
        }

        var stride = Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 60);
        int points = 0, under = 0;
        for (var y = bounds.Top; y < bounds.Bottom; y += stride)
        {
            for (var x = bounds.Left; x < bounds.Right; x += stride)
            {
                points++;
                under += over.Any(box => x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom) ? 1 : 0;
            }
        }

        return points == 0 ? 0 : under / (double)points;
    }

    private static bool Inside(PhoneCheckItem item, PhoneCheckItem image)
    {
        for (var step = item.Parent; step is not null; step = step.Parent)
        {
            if (step == image)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Overlaps(PhoneBounds a, PhoneBounds b) =>
        Math.Min(a.Right, b.Right) > Math.Max(a.Left, b.Left) && Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Top, b.Top);

    /// <summary>
    /// The text of the items beside the image, as a screen reader would
    /// say each: its parent's other children, or where none has text, its
    /// grandparent's; at most 400 characters.
    /// </summary>
    internal static string Nearby(PhoneCheckItem image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var child = image;
        for (var level = 0; level < 2 && child.Parent is { } parent; level++, child = parent)
        {
            var texts = parent.Children
                .Where(other => other != child)
                .Select(other => PhoneTreeChecks.SpeakableText(other).Trim())
                .Where(text => text.Length > 0)
                .ToList();
            if (texts.Count > 0)
            {
                var joined = string.Join(" | ", texts);
                return joined.Length <= MostNearbyCharacters ? joined : joined[..(MostNearbyCharacters - 3)] + "...";
            }
        }

        return string.Empty;
    }
}
