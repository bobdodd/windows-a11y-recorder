using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Recorder.Session;

/// <summary>
/// One image a model is to be asked about (android-vision-checks.md,
/// "Images of text with Claude, and four rules"): the visit and the
/// image's identity, the first time it was judged with the picture and the
/// image's bounds there, what OCR read inside it once the rules have left
/// lines out, and its description, for a web image its alt text, and
/// whether it is a web image (android-checks.md, step 26). The 1.4.5
/// finding and, at AAA, the 1.4.9 finding of the image share one question.
/// </summary>
public sealed record PhoneImageOfTextQuestion(
    PhoneVisit Visit,
    string Identity,
    PhoneFindingTime Time,
    PhoneContrastPicture Picture,
    string Ocr,
    string? Description,
    bool Web = false)
{
    public string App => Visit.Screen.Package;

    public string ScreenName => Visit.Screen.Name;

    public PhoneBounds Bounds => Time.Bounds;
}

/// <summary>
/// What a model's answers, or the lack of them, make of an image's
/// finding: its level and the words the finding gives for it, whether it
/// is a model's inference, and the model, by its profile's name, and
/// whether it was a cloud model. A finding left a warning because the model
/// gave no answer is not inferred.
/// </summary>
public sealed record PhoneInference(PhoneCheckLevel Level, string Text, bool Inferred, string? Model = null, bool Cloud = false);

/// <summary>
/// Step 8 of android-checks.md, images of text (WCAG 1.4.5; 1.4.9 as well at
/// the AAA audit level), from the words OCR read on each settled snapshot's
/// picture (step 4b): an image in the tree, an ImageView or a class drawn
/// from one, or a web image (android.widget.Image, step 26), inside whose
/// bounds OCR read words that no text item holds is a probable image of text, a warning, as the criterion's exceptions are not
/// judged. Words go to the smallest image they are in; words held by an item's
/// text or hint, as an item drawn over the image, are the app's text. A
/// description holding the words does not make them text: it is what a
/// screen reader says, not what is shown. The findings are merged in each
/// visit by check and image, as the tree checks' are by item.
/// Four rules without a model, chosen by the owner on 2026-11-15
/// (android-vision-checks.md, "Images of text with Claude, and four
/// rules"), come first: TalkBack's caption is left out (1), a picture out
/// of step with the tree is not judged (2), a line not wholly inside the
/// image or held by an item beside it is left out (3), and in an app that
/// draws its own text with Flutter an image whose description holds every
/// word left is a manual check (4). What is left is a question for the
/// model chosen for vision-based checks (<see cref="Questions"/>).
/// </summary>
public sealed class PhoneImagesOfTextRun
{
    /// <summary>
    /// The fewest letters and digits OCR must read inside an image for a
    /// finding, so that a shape read as a letter or two, or a symbol such
    /// as "B" on a bold button, is not one (an assumption, 2026-11-12).
    /// </summary>
    public const int MinimumCharacters = 3;

    /// <summary>
    /// How far back TalkBack's utterances count as its caption, from the
    /// picture's snapshot: ten seconds (measured, 2026-11-15).
    /// </summary>
    public const long CaptionNanoseconds = 10_000_000_000L;

    /// <summary>
    /// The fewest lines outside every image, in no item's text and not
    /// TalkBack's, for a picture to be out of step with the tree (rule 2):
    /// measured 13 to 38 in the pictures out of step, 0 to 6 in step.
    /// </summary>
    public const int OutOfStepLines = 3;

    // TalkBack's own phrases, which its caption shows after an item's words
    // when the recording holds TalkBack's speech or focus.
    private static readonly string[] TalkBackPhrases =
    [
        "Double-tap to activate", "Double-tap and hold to long press", "Double-tap and hold to drag",
        "Double-tap to toggle", "Double-tap to edit text", "Swipe up or down to adjust",
    ];

    // The lines of an image a finding quotes.
    private const int QuotedLines = 3;

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, int> _imagesByVisit = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];
    private readonly Dictionary<(PhoneVisit, string), PhoneImageOfTextQuestion> _questions = [];

    private PhoneImagesOfTextRun(PhoneAuditLevel level)
    {
        Level = level;
    }

    public static PhoneImagesOfTextRun Empty { get; } = new(PhoneAuditLevel.AA);

    public PhoneAuditLevel Level { get; }

    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The images on pictures OCR read, counted once for each settled snapshot they were on.</summary>
    public int Images { get; private set; }

    /// <summary>The images with no finding.</summary>
    public int Passed { get; private set; }

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>The lines left out as TalkBack's caption (rule 1), once for each image on each snapshot.</summary>
    public int CaptionLines { get; private set; }

    /// <summary>The lines left out as not wholly inside the image (rule 3).</summary>
    public int EdgeLines { get; private set; }

    /// <summary>The lines left out as held by a text item beside the image (rule 3).</summary>
    public int NeighbourLines { get; private set; }

    /// <summary>The images with words not judged, as their picture was out of step with the tree (rule 2).</summary>
    public int OutOfStep { get; private set; }

    /// <summary>The images left to a manual check, as the app draws its own text (rule 4).</summary>
    public int ManualChecks { get; private set; }

    /// <summary>
    /// The images the model chosen for vision-based checks is to be asked
    /// about, one for each image with a finding in a visit, in the
    /// findings' order.
    /// </summary>
    public IReadOnlyList<PhoneImageOfTextQuestion> Questions { get; private set; } = [];

    /// <summary>
    /// What became of the questions, such as that the model was not asked
    /// and why; null until a model's answers are given, or where there are
    /// no questions.
    /// </summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its images were checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    public int ImagesIn(PhoneVisit visit) =>
        _imagesByVisit.TryGetValue(visit, out var count) ? count : 0;

    /// <summary>The question of a finding's image, or null where the finding is not one for the model.</summary>
    public PhoneImageOfTextQuestion? QuestionOf(PhoneFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return _questions.TryGetValue((finding.Visit, finding.Identity), out var question) &&
            finding.Level == PhoneCheckLevel.Warning && finding.Inference is null
            ? question
            : null;
    }

    public static PhoneImagesOfTextRun Run(
        PhoneContrastRun measured,
        PhoneScreenTimeline screens,
        PhoneDeviceTimeline device,
        PhoneAuditLevel level,
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
        var run = new PhoneImagesOfTextRun(level);
        var findings = new Dictionary<(PhoneVisit, string, string), PhoneFinding>();
        var notJudged = new Dictionary<(PhoneVisit, string, string), PhoneFinding>();
        foreach (var snapshot in measured.Snapshots)
        {
            // With no OCR there are no words to find in an image.
            if (snapshot.Window is not { } window || string.IsNullOrEmpty(snapshot.Engine))
            {
                continue;
            }

            run.SnapshotsRead++;
            var visit = snapshot.Visit;
            run._visitsRead.Add(visit);
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            var density = device.DensityAt(snapshot.TakenBy.MonotonicNanoseconds)?.PixelsPerDp;
            // Step 26, rule 1: a web image is an image as an ImageView is,
            // for every rule.
            var images = window.Items
                .Where(item => PhoneTextMatching.IsImage(item) && item.Node.VisibleToUser && !item.Bounds.IsEmpty)
                .ToList();
            var inside = InImages(images, snapshot.Lines, PhoneTextMatching.IsImage);
            var caption = new Lazy<Caption>(() => CaptionAt(snapshot.Settled.MonotonicNanoseconds, talkBack, speech, accessibility));
            var step = new Lazy<(bool OutOfStep, int Unexplained, int Found)>(() => Step(snapshot, images, caption.Value, PhoneTextMatching.IsImage));
            var flutter = DrawsWithFlutter(device, visit.Screen.Package);
            foreach (var image in images)
            {
                run.Images++;
                run._imagesByVisit[visit] = run.ImagesIn(visit) + 1;
                var lines = inside.TryGetValue(image, out var found) ? found : [];
                if (Characters(lines) < MinimumCharacters)
                {
                    run.Passed++;
                    continue;
                }

                // Rule 2: a picture out of step with the tree is not judged.
                if (step.Value.OutOfStep)
                {
                    run.OutOfStep++;
                    var (unexplained, placed) = (step.Value.Unexplained, step.Value.Found);
                    var why = $"not judged, as {OutOfStepText(unexplained, placed)}; {Source(snapshot.Picture.Kind)}";
                    Add(notJudged, PhoneCheckNames.ImageOfText, PhoneCheckLevel.NotRun, why);
                    if (level == PhoneAuditLevel.AAA)
                    {
                        Add(notJudged, PhoneCheckNames.ImageOfTextAaa, PhoneCheckLevel.NotRun, why);
                    }

                    continue;
                }

                // Rules 1 and 3: TalkBack's caption, then lines past the
                // image's edge, then lines a text item beside it holds.
                var said = lines.Where(caption.Value.Said).ToList();
                var rest = lines.Except(said).ToList();
                var edge = rest.Where(line => !Within(line.Box, image.Bounds)).ToList();
                rest = [.. rest.Except(edge)];
                var beside = rest.Where(line => HeldBeside(line, image, window)).ToList();
                var alt = AltText(image);
                var kept = rest.Except(beside).ToList();
                run.CaptionLines += said.Count;
                run.EdgeLines += edge.Count;
                run.NeighbourLines += beside.Count;
                if (Characters(kept) < MinimumCharacters)
                {
                    run.Passed++;
                    continue;
                }

                var read = Read(image, kept, snapshot, density) + LeftOut(said.Count, edge.Count, beside.Count);

                // Rule 4: an app that draws its own text, with an image
                // whose description holds every word left.
                if (flutter && DescriptionHolds(alt, kept))
                {
                    run.ManualChecks++;
                    var manual = $"{read}; {OwnText}; {Source(snapshot.Picture.Kind)}";
                    Add(findings, PhoneCheckNames.ImageOfText, PhoneCheckLevel.Warning, manual);
                    if (level == PhoneAuditLevel.AAA)
                    {
                        Add(findings, PhoneCheckNames.ImageOfTextAaa, PhoneCheckLevel.Warning, manual);
                    }

                    continue;
                }

                var time = Add(findings, PhoneCheckNames.ImageOfText, PhoneCheckLevel.Warning, $"{read}; {Exceptions145}; {Source(snapshot.Picture.Kind)}");
                if (level == PhoneAuditLevel.AAA)
                {
                    Add(findings, PhoneCheckNames.ImageOfTextAaa, PhoneCheckLevel.Warning, $"{read}; {Exceptions149}; {Source(snapshot.Picture.Kind)}");
                }

                if (time is not null && !run._questions.ContainsKey((visit, image.Identity)))
                {
                    run._questions[(visit, image.Identity)] = new PhoneImageOfTextQuestion(
                        visit, image.Identity, time, snapshot.Picture, Ocr(kept), alt, PhoneTextMatching.IsWebImage(image));
                }

                PhoneFindingTime? Add(Dictionary<(PhoneVisit, string, string), PhoneFinding> into, string check, PhoneCheckLevel findingLevel, string measuredText)
                {
                    var key = (visit, check, image.Identity);
                    if (!into.TryGetValue(key, out var finding))
                    {
                        into[key] = finding = new PhoneFinding(check, findingLevel, visit, image.Identity, image.Label) { Web = IsWeb(image) };
                    }

                    if (finding._times.Count > 0 && finding._times[^1].Snapshot.EventId == snapshot.Settled.EventId)
                    {
                        return null;
                    }

                    var added = new PhoneFindingTime(
                        snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, image.Bounds, image.TreeKey,
                        measuredText, state);
                    finding._times.Add(added);
                    return added;
                }
            }
        }

        // An image judged on no picture of the visit, as each was out of
        // step, is a check not run, so that a missing finding is not read
        // as a pass.
        foreach (var (key, finding) in notJudged)
        {
            if (!findings.ContainsKey(key))
            {
                findings[key] = finding;
            }
        }

        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Place([.. findings.Values
            .OrderBy(finding => screenOrder.TryGetValue(finding.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(finding => finding.Visit.Start)
            .ThenBy(finding => finding.Check == PhoneCheckNames.ImageOfText ? 0 : 1)
            .ThenBy(finding => finding.Times[0].Time)
            .ThenBy(finding => finding.Times[0].Bounds.Top)
            .ThenBy(finding => finding.Times[0].Bounds.Left)]);
        run.Questions = [.. run.Findings
            .Where(finding => finding.Check == PhoneCheckNames.ImageOfText)
            .Select(finding => run._questions.GetValueOrDefault((finding.Visit, finding.Identity)))
            .OfType<PhoneImageOfTextQuestion>()];
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// The run with the model's answers given: each finding of a question
    /// answered takes the answer's level and words, and every other finding
    /// is as it was. The note says what became of the questions, such as
    /// that no model was chosen, for the summary of the check.
    /// </summary>
    public PhoneImagesOfTextRun Judged(IReadOnlyDictionary<PhoneImageOfTextQuestion, PhoneInference> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneImagesOfTextRun(Level)
        {
            Images = Images,
            Passed = Passed,
            SnapshotsRead = SnapshotsRead,
            CaptionLines = CaptionLines,
            EdgeLines = EdgeLines,
            NeighbourLines = NeighbourLines,
            OutOfStep = OutOfStep,
            ManualChecks = ManualChecks,
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

            // The exceptions are no longer "not judged" where a model's
            // answer judged them; the answer says what it made of them.
            var copy = new PhoneFinding(finding.Check, answer.Level, finding.Visit, finding.Identity, finding.Label) { Inference = answer, Web = finding.Web };
            copy._times.AddRange(finding._times.Select(time => time with
            {
                Measured = time.Measured.Replace("; " + Exceptions145, "", StringComparison.Ordinal)
                    .Replace("; " + Exceptions149, "", StringComparison.Ordinal),
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

    private const string Exceptions145 =
        "a logotype, a picture with significant other visual content and an image that can be customized are not judged";

    private const string Exceptions149 =
        "1.4.9 allows an image of text only as pure decoration or where essential, as a logotype; neither is judged, nor a picture with significant other visual content";

    private const string OwnText =
        "the app draws its own text with Flutter, as detected at the recording's stop, and the image's description holds every word, " +
        "so whether the words are drawn in the image cannot be told from the recording: check manually";

    private static int Characters(IEnumerable<PhoneTextLine> lines) =>
        lines.Sum(line => PhoneTextMatching.Spelling(line.OcrText!).Length);

    private static string Lines(int count) =>
        count == 1 ? "1 line" : string.Create(CultureInfo.CurrentCulture, $"{count:N0} lines");

    // Rule 4's framework: Flutter, as detected at the recording's stop
    // (android-framework-metadata.md), from the recording itself only
    // (owner, 2026-11-15).
    private static bool DrawsWithFlutter(PhoneDeviceTimeline device, string package) =>
        device.VersionOf(package)?.Framework is { Status: "detected" } framework &&
        framework.Names.Any(name => name.Contains("Flutter", StringComparison.OrdinalIgnoreCase));

    // What TalkBack said or was reading at a time: its own phrases when the
    // recording holds its speech or focus, the utterance then and those of
    // the ten seconds before, and the focused item's words.
    private sealed class Caption(string spelt, HashSet<string> words)
    {
        public bool Said(PhoneTextLine line)
        {
            var spelling = PhoneTextMatching.Spelling(line.OcrText!);
            if (spelling.Length < 2 || words.Count == 0)
            {
                return false;
            }

            if (spelt.Contains(spelling, StringComparison.Ordinal))
            {
                return true;
            }

            // OCR misreads a letter of the caption now and then
            // ("Doublertap"): every word of the line within one letter of
            // a word said.
            var lineWords = Words(line.OcrText!);
            return lineWords.Count > 0 && lineWords.All(word =>
                words.Contains(word) ||
                (word.Length >= 4 && words.Any(said => Math.Abs(said.Length - word.Length) <= 1 && WithinOne(said, word))));
        }
    }

    private static Caption CaptionAt(long at, bool talkBack, TalkBackSpeechTimeline speech, PhoneAccessibilityTimeline accessibility)
    {
        var text = new StringBuilder();
        if (talkBack)
        {
            foreach (var phrase in TalkBackPhrases)
            {
                text.Append(phrase).Append(' ');
            }
        }

        if (speech.At(at) is { } last)
        {
            text.Append(last.Text).Append(' ');
        }

        foreach (var said in speech.Utterances.Where(utterance => utterance.Start <= at && utterance.Start >= at - CaptionNanoseconds))
        {
            text.Append(said.Text).Append(' ');
        }

        if (accessibility.FocusAt(at) is { } focus)
        {
            text.Append(focus.Item.Text).Append(' ').Append(focus.Item.ContentDescription).Append(' ').Append(focus.Item.Label).Append(' ');
        }

        var all = text.ToString();
        return new Caption(PhoneTextMatching.Spelling(all), [.. Words(all)]);
    }

    /// <summary>
    /// Rule 2, for another check that needs a picture in step with the tree
    /// (the check of images with no text alternative, android-checks.md,
    /// step 20, rule 3): whether the snapshot's picture is out of step, with
    /// the lines OCR read that no item's text explains and the text items it
    /// found where the tree places them. TalkBack's caption is left out as
    /// here.
    /// </summary>
    internal static (bool OutOfStep, int Unexplained, int Found) OutOfStepAt(
        PhoneContrastSnapshot snapshot,
        List<PhoneCheckItem> images,
        bool talkBack,
        TalkBackSpeechTimeline speech,
        PhoneAccessibilityTimeline accessibility) =>
        Step(snapshot, images, CaptionAt(snapshot.Settled.MonotonicNanoseconds, talkBack, speech, accessibility),
            item => item.InstanceOf(PhoneTextMatching.ImageViewClass));

    /// <summary>
    /// Rule 1, for the check of text on screen not in the tree
    /// (android-checks.md, step 25, rule 3): whether a line OCR read is
    /// TalkBack's caption, or what it was saying or reading, at a time.
    /// </summary>
    internal static Func<PhoneTextLine, bool> CaptionSaid(long at, bool talkBack, TalkBackSpeechTimeline speech, PhoneAccessibilityTimeline accessibility) =>
        CaptionAt(at, talkBack, speech, accessibility).Said;

    /// <summary>What a finding not judged as its picture is out of step says (rule 2).</summary>
    internal static string OutOfStepText(int unexplained, int found) =>
        string.Create(CultureInfo.CurrentCulture,
            $"the picture is out of step with the tree: OCR read {Lines(unexplained)} outside every image that no item's text holds, more than the {found:N0} text {(found == 1 ? "item" : "items")} it found where the tree places them");

    // Rule 2: the picture is out of step with the tree when OCR read at
    // least 3 lines outside every image that are in no item's text, and not
    // TalkBack's, and those outnumber the text items it found where the tree
    // places them. Too few found alone is not enough: OCR misses small
    // labels over a photograph.
    private static (bool OutOfStep, int Unexplained, int Found) Step(
        PhoneContrastSnapshot snapshot, List<PhoneCheckItem> images, Caption caption, Func<PhoneCheckItem, bool> isImage)
    {
        var found = snapshot.Lines
            .Where(line => line.Item is { } owner && !isImage(owner) &&
                line.Place is PhoneTextPlace.AppTextPlacedByOcr or PhoneTextPlace.AppTextOnItemBounds)
            .GroupBy(line => line.Item!)
            .Count(group => group.Any(line => line.Place == PhoneTextPlace.AppTextPlacedByOcr));
        var unexplained = snapshot.Lines.Count(line =>
            line.Place == PhoneTextPlace.FoundByOcr && !string.IsNullOrWhiteSpace(line.OcrText) &&
            PhoneTextMatching.Spelling(line.OcrText).Length >= 3 && !caption.Said(line) && !InAny(line.Box, images));
        return (unexplained >= OutOfStepLines && unexplained > found, unexplained, found);
    }

    private static bool InAny(PhoneBounds box, List<PhoneCheckItem> images)
    {
        var x = (box.Left + box.Right) / 2;
        var y = (box.Top + box.Bottom) / 2;
        return images.Any(each => x >= each.Bounds.Left && x < each.Bounds.Right && y >= each.Bounds.Top && y < each.Bounds.Bottom);
    }

    private static bool Within(PhoneBounds box, PhoneBounds bounds) =>
        box.Left >= bounds.Left && box.Top >= bounds.Top && box.Right <= bounds.Right && box.Bottom <= bounds.Bottom;

    // Rule 3: a line a visible item, not an image, holds in its text or
    // description, whose bounds touch the image's within the line's height.
    private static bool HeldBeside(PhoneTextLine line, PhoneCheckItem image, PhoneCheckWindow window)
    {
        var spelt = PhoneTextMatching.Spelling(line.OcrText!);
        if (spelt.Length < 2)
        {
            return false;
        }

        var reach = Math.Max(2, line.WordsHeight);
        var bounds = image.Bounds;
        return window.Items.Any(other =>
            other != image && !PhoneTextMatching.IsImage(other) && other.Node.VisibleToUser && !other.Bounds.IsEmpty &&
            other.Bounds.Left <= bounds.Right + reach && other.Bounds.Right >= bounds.Left - reach &&
            other.Bounds.Top <= bounds.Bottom + reach && other.Bounds.Bottom >= bounds.Top - reach &&
            PhoneTextMatching.Spelling(other.Text + " " + other.Node.ContentDescription).Contains(spelt, StringComparison.Ordinal));
    }

    // The lines the rules left out, for the finding to say.
    private static string LeftOut(int said, int edge, int beside)
    {
        var parts = new List<string>();
        if (said > 0)
        {
            parts.Add($"{Lines(said)} TalkBack was saying");
        }

        if (edge > 0)
        {
            parts.Add($"{Lines(edge)} past the image's edge");
        }

        if (beside > 0)
        {
            parts.Add($"{Lines(beside)} that an item beside the image holds");
        }

        return parts.Count == 0 ? string.Empty : "; left out: " + string.Join(", ", parts);
    }

    // What OCR read inside the image, as the model is told it.
    private static string Ocr(IReadOnlyList<PhoneTextLine> lines)
    {
        var ordered = lines.OrderBy(line => line.Box.Top).ThenBy(line => line.Box.Left).ToList();
        return $"OCR read {Lines(ordered.Count)} of text inside the image: " + string.Join(", ", ordered.Select(line => $"\"{line.OcrText}\""));
    }

    private static List<string> Words(string text) =>
        [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(PhoneTextMatching.Spelling).Where(word => word.Length >= 2)];

    // One letter added, left out or changed.
    internal static bool WithinOne(string a, string b)
    {
        if (a == b)
        {
            return true;
        }

        if (a.Length > b.Length)
        {
            (a, b) = (b, a);
        }

        var i = 0;
        while (i < a.Length && a[i] == b[i])
        {
            i++;
        }

        return a.Length == b.Length
            ? a.AsSpan(i + 1).SequenceEqual(b.AsSpan(i + 1))
            : a.AsSpan(i).SequenceEqual(b.AsSpan(i + 1));
    }

    // Whether the description holds every word of the lines, as spelt for
    // matching.
    private static bool DescriptionHolds(string? description, IReadOnlyList<PhoneTextLine> lines)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var spelt = PhoneTextMatching.Spelling(description);
        var words = lines.SelectMany(line => Words(line.OcrText!)).ToList();
        return words.Count > 0 && words.All(word => spelt.Contains(word, StringComparison.Ordinal));
    }

    private static string Source(PhonePictureKind kind) =>
        kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video";

    /// <summary>
    /// The lines OCR read inside each image, at the smallest image their
    /// centre is in, leaving out the lines an item's text or hint holds.
    /// </summary>
    internal static Dictionary<PhoneCheckItem, List<PhoneTextLine>> InImages(
        IReadOnlyList<PhoneCheckItem> images,
        IReadOnlyList<PhoneTextLine> lines,
        Func<PhoneCheckItem, bool>? isImage = null)
    {
        isImage ??= item => item.InstanceOf(PhoneTextMatching.ImageViewClass);
        var bySize = images
            .OrderBy(image => (long)image.Bounds.Width * image.Bounds.Height)
            .ThenBy(image => image.Node.Index)
            .ToList();
        var inside = new Dictionary<PhoneCheckItem, List<PhoneTextLine>>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.OcrText) || IsAppsText(line, isImage))
            {
                continue;
            }

            var x = (line.Box.Left + line.Box.Right) / 2;
            var y = (line.Box.Top + line.Box.Bottom) / 2;
            var image = bySize.FirstOrDefault(each =>
                x >= each.Bounds.Left && x < each.Bounds.Right && y >= each.Bounds.Top && y < each.Bounds.Bottom);
            if (image is null)
            {
                continue;
            }

            if (!inside.TryGetValue(image, out var list))
            {
                inside[image] = list = [];
            }

            list.Add(line);
        }

        return inside;
    }

    // A line the app shows as text: its item, not an image, has text or a
    // hint that OCR's words matched; an item matched by its description
    // alone shows no text of its own.
    private static bool IsAppsText(PhoneTextLine line, Func<PhoneCheckItem, bool> isImage) =>
        line.Item is { } item &&
        !isImage(item) &&
        (!string.IsNullOrWhiteSpace(item.Text) || !string.IsNullOrWhiteSpace(item.Node.Hint));

    // What OCR read inside the image, how tall, and whether its description
    // holds it.
    private static string Read(PhoneCheckItem image, List<PhoneTextLine> lines, PhoneContrastSnapshot snapshot, double? pixelsPerDp)
    {
        var ordered = lines.OrderBy(line => line.Box.Top).ThenBy(line => line.Box.Left).ToList();
        var quoted = string.Join(", ", ordered.Take(QuotedLines).Select(line => $"\"{line.OcrText}\""));
        if (ordered.Count > QuotedLines)
        {
            quoted += string.Create(CultureInfo.CurrentCulture, $" and {ordered.Count - QuotedLines:N0} more");
        }

        var count = ordered.Count == 1 ? "1 line" : string.Create(CultureInfo.CurrentCulture, $"{ordered.Count:N0} lines");
        var read = $"OCR ({snapshot.Engine}) read {count} of text inside the image: {quoted}";
        var heights = ordered.Select(line => line.WordsHeight).Where(height => height > 0).ToList();
        if (heights.Count > 0)
        {
            var (low, high) = (heights.Min(), heights.Max());
            read += pixelsPerDp is { } scale && scale > 0
                ? low == high
                    ? string.Create(CultureInfo.CurrentCulture, $"; the words are {low / scale:0.#} dp tall in OCR's box")
                    : string.Create(CultureInfo.CurrentCulture, $"; the words are {low / scale:0.#} to {high / scale:0.#} dp tall in OCR's box")
                : low == high
                    ? string.Create(CultureInfo.CurrentCulture, $"; the words are {low:N0} pixels tall in OCR's box, as no density was recorded")
                    : string.Create(CultureInfo.CurrentCulture, $"; the words are {low:N0} to {high:N0} pixels tall in OCR's box, as no density was recorded");
        }

        return $"{read}; {Described(image, ordered)}";
    }

    // Whether the image's description holds the words OCR read, as spelt for
    // matching (letters and digits, the characters OCR confuses as one).
    private static string Described(PhoneCheckItem image, IReadOnlyList<PhoneTextLine> lines)
    {
        var alt = AltText(image);
        if (string.IsNullOrWhiteSpace(alt))
        {
            return PhoneTextMatching.IsWebImage(image) ? "it has no alt text" : "it has no description";
        }

        var description = PhoneTextMatching.Spelling(alt);
        var words = lines
            .SelectMany(line => line.OcrText!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(PhoneTextMatching.Spelling)
            .Where(word => word.Length >= 2)
            .ToList();
        var held = words.Count(word => description.Contains(word, StringComparison.Ordinal));
        var its = PhoneTextMatching.IsWebImage(image) ? "its alt text" : "its description";
        return held == words.Count
            ? $"{its} holds them"
            : held == 0 ? $"{its} holds none of them" : $"{its} holds some of them";
    }

    /// <summary>
    /// What a screen reader is given for the image: its description; for a
    /// web image, whose alt text a browser may give as its text or its
    /// description, both (android-checks.md, step 26, rule 2).
    /// </summary>
    internal static string? AltText(PhoneCheckItem image)
    {
        if (!PhoneTextMatching.IsWebImage(image))
        {
            return image.Node.ContentDescription;
        }

        var parts = new[] { image.Node.Text, image.Node.ContentDescription }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    // Web content (step 26, rule 4): a web image, or an item inside a
    // WebView.
    private static bool IsWeb(PhoneCheckItem image) =>
        PhoneTextMatching.IsWebImage(image) || PhoneTreeChecks.HasWebViewAncestor(image);
}
