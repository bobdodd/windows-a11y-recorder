using System.Diagnostics;
using System.Globalization;

namespace Recorder.Session;

/// <summary>What became of one described image in the decorative image check.</summary>
public enum PhoneDecorativeState
{
    /// <summary>A screen reader does not say the image's description (rule 2): not checked.</summary>
    NotSpoken,

    /// <summary>The image is a control, so its description is the control's name (rule 3): not asked.</summary>
    Control,

    /// <summary>The description is all that the item holding the image says, so it is that item's name (rule 3): not asked.</summary>
    ItemName,

    /// <summary>The image is for the model to judge (rule 4).</summary>
    Asked,
}

/// <summary>What the model's answers made of one image in the decorative image check.</summary>
public enum PhoneDecorativeOutcome
{
    /// <summary>The image is informative: no finding (rule 5).</summary>
    Informative,

    /// <summary>A warning: the image is decorative, or the model could not tell, or gave two answers (rule 5).</summary>
    Finding,
}

/// <summary>One image's answer in the decorative image check: what it made of the image, and the model's inference.</summary>
public sealed record PhoneDecorativeAnswer(PhoneDecorativeOutcome Outcome, PhoneInference Inference);

/// <summary>
/// Step 23 of android-checks.md, a decorative image that a screen reader
/// announces (WCAG 1.1.1). Eight rules, chosen by the owner on 2026-11-17
/// ("Build rules 1 to 8 for the decorative image check with low impact")
/// from a probe of 146 described images on 20 recordings, with no model:
/// (1) one issue, "Decorative image announced by a screen reader"; (2) an
/// image with a description of its own that a screen reader says, as the
/// image or as part of the item that holds it (all 146); (3) an image that
/// is a control (84), or whose description is all that the item holding
/// it says (51), gives that control or item its name, so is not asked
/// about, as a control needs a name whatever its image shows; (4) any
/// other is asked of the model for vision-based checks, with the whole
/// screen with the image outlined, a close-up, and what a screen reader
/// says on the screen, the image's own description marked as the image's,
/// whether it is informative, decorative or cannot tell, reason first
/// (AndroidDecorativeImages); (5) decorative is a warning for a person to
/// verify, low impact if confirmed, cannot tell or two answers a warning
/// to check manually, and informative no finding, counted; (6) the answer
/// is kept by the image's pixels, its description and what a screen
/// reader says, and asked again of the model for complex questions where
/// not settled; (7) with no model, no finding, and the counts say how many
/// were not judged; (8) an image on a picture out of step with the tree is
/// not asked about, as for the other image checks. The images are those
/// the icon and description checks choose (PhoneDescribedImageRun), once
/// each in a visit, on pictures in step.
/// </summary>
public sealed class PhoneDecorativeImageRun
{
    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneDescribedImageQuestion, PhoneDecorativeState> _states = [];
    private PhoneDescribedImageRun _images = PhoneDescribedImageRun.Empty;

    private PhoneDecorativeImageRun()
    {
    }

    public static PhoneDecorativeImageRun Empty { get; } = new();

    /// <summary>The warnings the model's answers made; none before any answer (rule 7).</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead => _images.SnapshotsRead;

    /// <summary>The described images on pictures in step, once each in a visit.</summary>
    public int Images => _states.Count;

    /// <summary>The described images on a picture out of step with the tree (rule 8), once for each snapshot.</summary>
    public int OutOfStep => _images.OutOfStep;

    /// <summary>The images whose description a screen reader does not say (rule 2).</summary>
    public int NotSpoken => Count(PhoneDecorativeState.NotSpoken);

    /// <summary>The images that are controls, whose description is the control's name (rule 3).</summary>
    public int Controls => Count(PhoneDecorativeState.Control);

    /// <summary>The images whose description is all the item holding them says, so that item's name (rule 3).</summary>
    public int ItemNames => Count(PhoneDecorativeState.ItemName);

    /// <summary>The images to be asked about, in screen and time order (rule 4).</summary>
    public IReadOnlyList<PhoneDescribedImageQuestion> Questions { get; private set; } = [];

    /// <summary>The questions answered, with what each answer made of its image; empty until a model's answers are given.</summary>
    public IReadOnlyDictionary<PhoneDescribedImageQuestion, PhoneDecorativeAnswer> Answers { get; private set; } =
        new Dictionary<PhoneDescribedImageQuestion, PhoneDecorativeAnswer>();

    /// <summary>The images judged informative, not confirmed.</summary>
    public int Informative => Answers.Values.Count(answer => answer.Outcome == PhoneDecorativeOutcome.Informative);

    /// <summary>The questions with no answer, not judged.</summary>
    public int Unanswered => Questions.Count - Answers.Count;

    /// <summary>
    /// Every inference the model's answers made, findings or not, so that
    /// a report says AI was used even where every image was judged
    /// informative.
    /// </summary>
    public IEnumerable<PhoneInference> Inferences => Answers.Values.Select(answer => answer.Inference);

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its images were checked.</summary>
    public bool Read(PhoneVisit visit) => _images.Read(visit);

    /// <summary>The described images in the visit on pictures in step, once each.</summary>
    public int ImagesIn(PhoneVisit visit) => _states.Keys.Count(question => question.Visit == visit);

    /// <summary>The images of the visit to be asked about.</summary>
    public int AskedIn(PhoneVisit visit) => Questions.Count(question => question.Visit == visit);

    /// <summary>What became of one described image.</summary>
    public PhoneDecorativeState? StateOf(PhoneDescribedImageQuestion image) =>
        _states.TryGetValue(image, out var state) ? state : null;

    /// <summary>The images of a visit and what became of each, in the order the icon and description checks give them.</summary>
    public IEnumerable<(PhoneDescribedImageQuestion Image, PhoneDecorativeState State)> In(PhoneVisit visit) =>
        _images.Questions.Where(question => question.Visit == visit && _states.ContainsKey(question)).Select(question => (question, _states[question]));

    /// <summary>The run's rules (2, 3 and 8) applied to the described images the icon and description checks chose.</summary>
    public static PhoneDecorativeImageRun Run(PhoneDescribedImageRun images)
    {
        ArgumentNullException.ThrowIfNull(images);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneDecorativeImageRun { _images = images };
        var questions = new List<PhoneDescribedImageQuestion>();
        foreach (var image in images.Questions)
        {
            var state = StateOf(image.IsControl, image.SpokenBy, image.Description, image.Spoken);
            run._states[image] = state;
            if (state == PhoneDecorativeState.Asked)
            {
                questions.Add(image);
            }
        }

        run.Questions = questions;
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// What rules 2 and 3 make of an image: not spoken where no item a
    /// screen reader stops on says its description; a control's name where
    /// it is a control; an item's name where the description is all the
    /// item holding it says, letters and digits compared; else asked.
    /// </summary>
    public static PhoneDecorativeState StateOf(bool isControl, PhoneDescriptionSpeaker spokenBy, string description, string spoken)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(spoken);
        var wanted = Letters(description);
        var said = Letters(spoken);
        if (spokenBy == PhoneDescriptionSpeaker.Nothing || wanted.Length == 0 || !said.Contains(wanted, StringComparison.Ordinal))
        {
            return PhoneDecorativeState.NotSpoken;
        }

        if (isControl)
        {
            return PhoneDecorativeState.Control;
        }

        return spokenBy == PhoneDescriptionSpeaker.Holder && said == wanted ? PhoneDecorativeState.ItemName : PhoneDecorativeState.Asked;
    }

    /// <summary>
    /// The run with the model's answers given: each answer that makes a
    /// finding (rule 5) is a finding with every time the image was seen in
    /// the visit on a picture in step; an answer that the image is
    /// informative is counted, not a finding. The note says what became of
    /// the questions.
    /// </summary>
    public PhoneDecorativeImageRun Judged(IReadOnlyDictionary<PhoneDescribedImageQuestion, PhoneDecorativeAnswer> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneDecorativeImageRun
        {
            _images = _images,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        foreach (var (image, state) in _states)
        {
            judged._states[image] = state;
        }

        var kept = new Dictionary<PhoneDescribedImageQuestion, PhoneDecorativeAnswer>();
        var findings = new List<PhoneFinding>();
        foreach (var question in Questions)
        {
            if (!answers.TryGetValue(question, out var answer))
            {
                continue;
            }

            kept[question] = answer;
            if (answer.Outcome != PhoneDecorativeOutcome.Finding)
            {
                continue;
            }

            var finding = new PhoneFinding(PhoneCheckNames.DecorativeAnnounced, answer.Inference.Level, question.Visit, question.Identity, _images.LabelOf(question))
            {
                Inference = answer.Inference,
            };
            finding._times.AddRange(_images.TimesOf(question));
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

    private int Count(PhoneDecorativeState state) => _states.Values.Count(value => value == state);

    private static string Letters(string text) =>
        new([.. text.ToLower(CultureInfo.InvariantCulture).Where(char.IsLetterOrDigit)]);
}
