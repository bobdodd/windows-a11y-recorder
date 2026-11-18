using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>The question of one heading as it is sent: the request, its step key and the signature its answer is kept against.</summary>
public sealed record HeadingSectionRequest(PhoneHeadingSectionQuestion Question, DescribedImageRequest Request)
{
    /// <summary>The template the question is asked with: the pictured one where images cover half the section (rule 6).</summary>
    public string Template => Question.Pictured ? AndroidHeadingSections.PicturedTemplate : AndroidHeadingSections.TextTemplate;

    /// <summary>The model use the question is for: vision-based checks where pictured, else text-based (rule 9).</summary>
    public string Use => Question.Pictured ? ModelUses.VisionChecks : ModelUses.TextChecks;
}

/// <summary>
/// The questions of the check of a heading that does not describe its
/// section (android-checks.md, step 24, rules 5 to 10): for each heading
/// the rules leave (<see cref="PhoneHeadingSectionRun.Questions"/>),
/// whether it describes its section, from the heading, the screen's title
/// and the app's package, and the section's texts, each marked with where
/// it came from: what a screen reader says in it on screen, what the tree
/// holds in it that is not on screen, and the text OCR read in it. A
/// section that images cover at least half of is given the screen's
/// picture too, with the heading outlined, and asked of the project's
/// model for vision-based checks; any other is asked with only text, of
/// the model for text-based checks. Each question is asked twice, again of
/// the model for complex questions where the first answers do not settle
/// it, and each answer is kept against the heading, the section's texts
/// and, where given, the picture, so the same heading and section are
/// given the first answer again (rule 10).
/// </summary>
public static class AndroidHeadingSections
{
    public const string TextTemplate = "android-heading-section";

    public const string PicturedTemplate = "android-heading-section-pictured";

    public const int TemplateVersion = 1;

    public const string DescribesTheSection = "describes-the-section";

    public const string DoesNotDescribe = "does-not-describe";

    public const string CannotTell = "cannot-tell";

    public static readonly IReadOnlyList<string> Answers = [DescribesTheSection, DoesNotDescribe, CannotTell];

    public const string Instructions =
        "You judge one heading on a screen of an Android app for WCAG 2.2 success criterion 2.4.6, Headings and Labels: " +
        "a heading describes the topic or purpose of the section it heads. Answer only from what is given, with one of the fixed answers.";

    /// <summary>What the question gives of the heading and its section, before what it asks.</summary>
    public static string Context(PhoneHeadingSectionQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var title = question.ScreenTitle is null
            ? $"A screen of the Android app {question.App} has no title.\n"
            : $"A screen of the Android app {question.App} has the title \"{question.ScreenTitle}\".\n";
        var heading = question.Marked
            ? $"On it, \"{question.Heading}\" is marked as a heading for a screen reader. Its section is what follows it, up to the next heading.\n"
            : $"On it, the text \"{question.Heading}\" is shown as a heading, though not marked as one for a screen reader. Its section is what follows it, up to the next heading.\n";
        var spoken = question.Spoken.Length == 0
            ? "A screen reader says nothing in the section on screen.\n"
            : $"What a screen reader says in the section on screen, item by item in the app's order: \"{question.Spoken}\".\n";
        var off = question.OffScreen.Length == 0
            ? ""
            : $"Items of the section the app holds that are not on screen, which a screen reader reaches by scrolling, in the app's order: \"{question.OffScreen}\".\n";
        var ocr = question.Ocr.Length == 0
            ? "OCR read no text in the section on a picture of the screen.\n"
            : $"The text OCR read in the section on a picture of the screen, line by line from the top, which may misread some words: \"{question.Ocr}\".\n";
        var more = question.MayContinue
            ? "The section is the last on the screen and its area can scroll further, so it may continue below the screen.\n"
            : "";
        var picture = question.Pictured
            ? "Picture A is the whole screen, with the heading outlined. Images cover much of the section, so it may show what the text does not.\n"
            : "";
        return title + heading + spoken + off + ocr + more + picture;
    }

    /// <summary>The question, of the heading judged.</summary>
    public static string Question(PhoneHeadingSectionQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        return $"Which one of these is true of the heading \"{question.Heading}\"?\n" +
            "- describes-the-section: it says what its section is about or for, so that a person could tell from the heading what the section holds.\n" +
            "- does-not-describe: it does not, such as a word that would fit any section, a heading of a different topic than its section's, or text that names nothing the section holds.\n" +
            "- cannot-tell: what is given does not show enough to judge.\n" +
            (question.Pictured
                ? "First write one sentence saying what in the picture or the text shows the answer, then give the answer."
                : "First write one sentence saying what in the text shows the answer, then give the answer.");
    }

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// pictured question whose picture cannot be read is left out, and not
    /// judged.
    /// </summary>
    public static IReadOnlyList<HeadingSectionRequest> Requests(PhoneHeadingSectionRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<HeadingSectionRequest>();
        var pixelsOf = new Dictionary<string, PhonePixels?>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in run.Questions)
        {
            if (!question.Pictured)
            {
                requests.Add(RequestOf(question, null));
                continue;
            }

            var path = question.Picture.AbsolutePath;
            if (!pixelsOf.TryGetValue(path, out var pixels))
            {
                pixelsOf[path] = pixels = load(path);
            }

            if (pixels is null || pixels.Width == 0 || pixels.Height == 0)
            {
                continue;
            }

            requests.Add(RequestOf(question, pixels));
        }

        return requests;
    }

    /// <summary>A question's request; the pixels of its picture where it is pictured, else null.</summary>
    public static HeadingSectionRequest RequestOf(PhoneHeadingSectionQuestion question, PhonePixels? pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        var input = Context(question) + Question(question);
        var signed = SignedText(question);
        if (!question.Pictured)
        {
            return new HeadingSectionRequest(question, Request(TextTemplate, ModelUses.TextChecks, input, null, AndroidImageAlternative.TextSignature(signed, question.Spoken)));
        }

        ArgumentNullException.ThrowIfNull(pixels);
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels, question.Bounds));
        var signature = AndroidImageAlternative.Signature(pixels, new PhoneBounds(0, 0, pixels.Width, pixels.Height), signed, question.Spoken);
        return new HeadingSectionRequest(question, Request(PicturedTemplate, ModelUses.VisionChecks, input, screen, signature));
    }

    /// <summary>
    /// The text an answer is kept against besides what a screen reader says
    /// in the section (rule 10): the app, the screen's title, the heading
    /// and whether it is marked, the section's items not on screen, the
    /// text OCR read and whether the section may continue.
    /// </summary>
    internal static string SignedText(PhoneHeadingSectionQuestion question) =>
        $"{question.App}\n{question.ScreenTitle}\n{question.Marked}\n{question.Heading}\n{question.OffScreen}\n{question.Ocr}\n{question.MayContinue}";

    private static DescribedImageRequest Request(string template, string use, string input, byte[]? screen, byte[] signature)
    {
        var request = new ModelRequest(
            template,
            TemplateVersion,
            Instructions,
            input,
            screen is null ? [] : [new ModelImage("A", "image/png", screen)],
            [],
            new HashSet<string>(StringComparer.Ordinal))
        {
            Use = use,
            Answers = Answers,
            ReusedAcrossTasks = true,
            Signature = signature,
        };

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{template}\n{TemplateVersion.ToString(CultureInfo.InvariantCulture)}\n{Instructions}\n{input}\n"));
        if (screen is not null)
        {
            hash.AppendData(screen);
        }

        return new DescribedImageRequest(request, "heading-section-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16], signature);
    }

    /// <summary>
    /// Asks the project's models about each heading the rules leave, with
    /// the rules run again on the audit's check of text shown as a heading
    /// as it stands, so that text the model judged a heading is among them
    /// (rule 2), and gives the audit with the answers judged: a pictured
    /// question of the model for vision-based checks, any other of the
    /// model for text-based checks. A heading and section the same as one
    /// answered before, in this run or on a task of the project (or, for a
    /// recording in no project, of the recording) by the same model, are
    /// given that answer again, copied into this task (rule 10). Where the
    /// task may use no model for a question's use, it is not asked and not
    /// judged (rule 9). A question whose step fails is not judged, and the
    /// note says why.
    /// </summary>
    public static async Task<AndroidAudit> AskAsync(
        AnalysisTaskContext context,
        AndroidAudit audit,
        Func<string, PhonePixels?> load,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(load);
        if (audit.HeadingSections is not { } rules)
        {
            return audit;
        }

        var run = rules.WithHeadingLook(audit.HeadingLook);
        if (run.Questions.Count == 0)
        {
            return audit.WithHeadingSections(run);
        }

        var textNone = run.TextQuestions > 0 ? context.NoModel(ModelUses.TextChecks) : null;
        var visionNone = run.PicturedQuestions > 0 ? context.NoModel(ModelUses.VisionChecks) : null;
        if ((run.TextQuestions == 0 || textNone is not null) && (run.PicturedQuestions == 0 || visionNone is not null))
        {
            return audit.WithHeadingSections(run.Judged(new Dictionary<PhoneHeadingSectionQuestion, PhoneHeadingSectionAnswer>(), NotAsked(run, textNone, visionNone)));
        }

        var requests = Requests(run, load);
        var recording = context.Task.RecordingId;
        var project = context.Task.ProjectId ??
            (recording is { } id ? (await context.Catalog.ProjectOfAsync(id, cancellationToken).ConfigureAwait(false))?.ProjectId : null);
        var held = (await context.Catalog.AttemptsAsync(context.Task.TaskId, cancellationToken).ConfigureAwait(false))
            .Where(item => item.Outcome == "accepted")
            .Select(item => item.StepKey)
            .ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();
        var copied = new HashSet<Guid>();

        async Task<SignedAsker?> AskerAsync(string use, string template, string? none, int questions)
        {
            if (questions == 0 || none is not null)
            {
                return null;
            }

            var profileName = context.ModelFor(use);
            var model = profileName is null ? null : context.Settings.ForProject(null).Profile(profileName)?.Model;
            var complexName = CheckQuestions.EscalationProfile(context, use);
            var complexModel = complexName is null ? null : context.Settings.ForProject(null).Profile(complexName)?.Model;
            IReadOnlyList<SignedAnswer> signed = profileName is null || model is null
                ? []
                : await context.Catalog.SignedAnswersAsync(template, TemplateVersion, profileName, model, project, recording, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<SignedAnswer> signedComplex = complexName is null || complexModel is null
                ? []
                : await context.Catalog.SignedAnswersAsync(template, TemplateVersion, complexName, complexModel, project, recording, cancellationToken).ConfigureAwait(false);
            return new SignedAsker(context, profileName, signed, complexName, signedComplex, held, copied, failures, CannotTell);
        }

        var texts = await AskerAsync(ModelUses.TextChecks, TextTemplate, textNone, run.TextQuestions).ConfigureAwait(false);
        var pictured = await AskerAsync(ModelUses.VisionChecks, PicturedTemplate, visionNone, run.PicturedQuestions).ConfigureAwait(false);
        var answers = new Dictionary<PhoneHeadingSectionQuestion, PhoneHeadingSectionAnswer>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asker = request.Question.Pictured ? pictured : texts;
            if (asker is not null && await asker.AskAsync(request.Request, cancellationToken).ConfigureAwait(false) is { } answered)
            {
                answers[request.Question] = Judge(answered.Answers, request.Question, answered.Again);
            }
        }

        var judged = run.Judged(answers, null);
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "was" : "were")} for the project's models")
            + AskedAgain((texts?.Escalated ?? 0) + (pictured?.Escalated ?? 0));
        var again = (texts?.Again ?? 0) + (pictured?.Again ?? 0);
        if (again > 0)
        {
            note += string.Create(CultureInfo.CurrentCulture, $"; {again:N0} {(again == 1 ? "answer was" : "answers were")} the one first given for the same heading and section");
        }

        var unread = run.PicturedQuestions - requests.Count(request => request.Question.Pictured);
        if (unread > 0)
        {
            note += string.Create(CultureInfo.CurrentCulture, $"; {Count(unread)} not asked as the picture could not be read");
        }

        if (textNone is not null)
        {
            note += "; " + NotJudged(run.TextQuestions, "text-based", textNone);
        }

        if (visionNone is not null)
        {
            note += "; " + NotJudged(run.PicturedQuestions, "vision-based", visionNone);
        }

        if (failures.Count > 0)
        {
            note += "; not answered: " + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return audit.WithHeadingSections(run.Judged(answers, note));
    }

    /// <summary>The note of questions not asked, as no model may be used for their checks (rule 9).</summary>
    public static string NotAsked(PhoneHeadingSectionRun run, string? textReason, string? visionReason)
    {
        ArgumentNullException.ThrowIfNull(run);
        var parts = new List<string>();
        if (run.TextQuestions > 0 && textReason is not null)
        {
            parts.Add(NotJudged(run.TextQuestions, "text-based", textReason));
        }

        if (run.PicturedQuestions > 0 && visionReason is not null)
        {
            parts.Add(NotJudged(run.PicturedQuestions, "vision-based", visionReason));
        }

        return string.Join("; ", parts);
    }

    private static string NotJudged(int questions, string kind, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions)} left by the rules {(questions == 1 ? "was" : "were")} not judged, as no model may be used for {kind} checks: {reason}");

    // What became of the questions: how many were answered, and what the
    // answers made of them.
    private static string Note(PhoneHeadingSectionRun judged, string start)
    {
        var findings = judged.Findings.Count;
        return start + string.Create(CultureInfo.CurrentCulture,
            $"; {Count(judged.Answers.Count)} answered: {judged.Described:N0} judged to describe {(judged.Described == 1 ? "its section" : "their sections")}, not confirmed, and {findings:N0} {(findings == 1 ? "warning" : "warnings")}");
    }

    /// <summary>What the answers make of a heading (rule 8), the deciding model's.</summary>
    internal static PhoneHeadingSectionAnswer Judge(QuestionAnswers answers, PhoneHeadingSectionQuestion question, bool again)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(question);
        var deciding = answers.Escalated ?? answers.First;
        var describes = deciding.One.Answer == deciding.Two.Answer && deciding.One.Answer == DescribesTheSection;
        var inference = CheckQuestions.Judge(answers, group => Interpret(group.One, group.Two, group.Model, group.Cloud, question, again));
        return new PhoneHeadingSectionAnswer(describes ? PhoneHeadingSectionOutcome.Describes : PhoneHeadingSectionOutcome.Finding, inference);
    }

    /// <summary>
    /// What two answers make of a heading (rule 8): agreed that it does not
    /// describe its section, a warning for a person to verify; agreed that
    /// it does, information, not a finding; agreed that what was given does
    /// not tell, or not agreed, a warning to check manually. Where the
    /// answers were first given for the same heading and section elsewhere,
    /// it says so.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud, PhoneHeadingSectionQuestion question, bool again = false)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(question);
        var who = $"{model}, {(cloud ? "a cloud model" : "a local model")},";
        var twice = ReferenceEquals(first, second) ? "" : " twice";
        var where = again
            ? question.Pictured
                ? " for the same heading, by the heading, its section's text and the screen's picture,"
                : " for the same heading, by the heading and its section's text,"
            : "";
        var from = question.Pictured
            ? "from the screen's picture and what a screen reader and OCR give of the section"
            : "from what a screen reader and OCR give of the section";
        var heading = $"the heading \"{question.Heading}\"";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice{where} whether {heading} describes its section and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            DoesNotDescribe => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} inferred {from}{twice}{where} that {heading} does not describe its section: \"{first.Reason}\"; a person should verify this",
                Inferred: true, model, cloud),
            DescribesTheSection => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred {from}{twice}{where} that {heading} describes its section, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell {from}{twice}{where} whether {heading} describes its section: \"{first.Reason}\"; check manually",
                Inferred: true, model, cloud),
        };
    }

    /// <summary>
    /// What kept answers make of a run, for the Checks tab: the first
    /// answers the project's model for each question's use gave for the
    /// same heading and section (rule 10), with the model for complex
    /// questions' where the first do not settle a question. A question
    /// whose use has no kept answers given or none for it is not judged,
    /// and the note counts it; where a reason is given that no model may
    /// be used for a use, the note says so of its questions (rule 9).
    /// </summary>
    public static PhoneHeadingSectionRun FromKept(
        PhoneHeadingSectionRun run,
        IReadOnlyList<HeadingSectionRequest> requests,
        ScreenTitleKept? text,
        ScreenTitleKept? vision,
        string? textNone = null,
        string? visionNone = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        var answers = new Dictionary<PhoneHeadingSectionQuestion, PhoneHeadingSectionAnswer>();
        int waiting = 0, askedAgain = 0;
        foreach (var request in requests)
        {
            if ((request.Question.Pictured ? vision : text) is not { } kept ||
                AndroidImageAlternative.Kept(kept.Signed, request.Request.Signature) is not { } found)
            {
                continue;
            }

            var first = new GroupAnswers(found.One, found.Two, kept.Model, found.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
            var again = found.Sources[0].StepKey != request.Request.StepKey(1);
            GroupAnswers? escalated = null;
            if (kept.ComplexModel is not null && CheckQuestions.Unsettled(found.One, found.Two, CannotTell))
            {
                if (AndroidImageAlternative.Kept(kept.ComplexSigned ?? [], request.Request.Signature, CheckQuestions.EscalatedRun) is not { } complex)
                {
                    waiting++;
                    continue;
                }

                askedAgain++;
                escalated = new GroupAnswers(complex.One, complex.Two, kept.ComplexModel, complex.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
            }

            answers[request.Question] = Judge(new QuestionAnswers(first, escalated), request.Question, again);
        }

        var judged = run.Judged(answers, null);
        var models = string.Join(" and ", new[] { text?.Model, vision?.Model }.OfType<string>().Distinct(StringComparer.Ordinal));
        var noModel = (textNone is null ? 0 : run.TextQuestions) + (visionNone is null ? 0 : run.PicturedQuestions);
        var unanswered = run.Questions.Count - answers.Count - waiting - noModel;
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "is" : "are")} for {(models.Length == 0 ? "the project's models" : models)}")
            + AskedAgain(askedAgain)
            + CheckQuestions.Waiting(waiting, Count(waiting))
            + (unanswered > 0 ? string.Create(CultureInfo.CurrentCulture, $", {Count(unanswered)} not yet asked") : "")
            + (NotAsked(run, textNone, visionNone) is { Length: > 0 } notAsked ? "; " + notAsked : "");
        return run.Judged(answers, note);
    }

    private static string AskedAgain(int questions) =>
        questions == 0
            ? ""
            : string.Create(CultureInfo.CurrentCulture,
                $"; {questions:N0} {(questions == 1 ? "question" : "questions")} asked again of the model for complex questions");

    private static string Count(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "heading" : "headings")}");
}
