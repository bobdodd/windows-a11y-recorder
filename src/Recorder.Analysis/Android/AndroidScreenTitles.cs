using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>The question of one screen title as it is sent: the request, its step key and the signature its answer is kept against.</summary>
public sealed record ScreenTitleRequest(PhoneScreenTitleQuestion Question, DescribedImageRequest Request)
{
    /// <summary>The template the question is asked with: the pictured one where images cover half the screen (rule 4).</summary>
    public string Template => Question.Pictured ? AndroidScreenTitles.PicturedTemplate : AndroidScreenTitles.TextTemplate;

    /// <summary>The model use the question is for: vision-based checks where pictured, else text-based.</summary>
    public string Use => Question.Pictured ? ModelUses.VisionChecks : ModelUses.TextChecks;
}

/// <summary>
/// The answers kept for one model use's questions, for the Checks tab: the
/// first answers of the project's model for the use, with its name, and
/// those of the model for complex questions, with its name, where it is a
/// different model.
/// </summary>
public sealed record ScreenTitleKept(
    IReadOnlyList<SignedAnswer> Signed,
    string Model,
    IReadOnlyList<SignedAnswer>? ComplexSigned = null,
    string? ComplexModel = null);

/// <summary>
/// The questions of the screen title check (android-checks.md, step 22,
/// rules 4 to 8): for each titled screen whose title is not exactly its
/// own heading, a line OCR read or an item a screen reader says
/// (<see cref="PhoneScreenTitleRun.Questions"/>), whether the title
/// describes the screen, from the title, what a screen reader says on the
/// screen and the text OCR read on its picture, each marked with where it
/// came from; for Chrome, the web page's title within it (option A). A
/// screen that images cover at least half of is given its picture too,
/// and asked of the project's model for vision-based checks; any other is
/// asked with only text, of the model for text-based checks. Each question
/// is asked twice, once for a model that gives the same answer to the same
/// request, again of the model for complex questions where the first
/// answers do not settle it, and each answer is kept against the title,
/// both texts and, where given, the picture, so the same screen is given
/// the first answer again (rule 8).
/// </summary>
public static class AndroidScreenTitles
{
    public const string TextTemplate = "android-screen-title";

    public const string PicturedTemplate = "android-screen-title-pictured";

    public const int TemplateVersion = 1;

    public const string DescribesTheScreen = "describes-the-screen";

    public const string DoesNotDescribe = "does-not-describe";

    public const string CannotTell = "cannot-tell";

    public static readonly IReadOnlyList<string> Answers = [DescribesTheScreen, DoesNotDescribe, CannotTell];

    public const string Instructions =
        "You judge the title of one screen in an Android app for WCAG 2.2 success criterion 2.4.2, Page Titled, as WCAG2ICT applies it to software: " +
        "a screen's title describes its topic or purpose. Answer only from what is given, with one of the fixed answers.";

    /// <summary>What the question gives of the screen, before what it asks.</summary>
    public static string Context(PhoneScreenTitleQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var page = question.PageTitle is null
            ? ""
            : $"The app is the Chrome browser, whose title for a screen is \"Chrome: \" followed by the title of the web page it shows; the web page's title is \"{question.PageTitle}\".\n";
        var spoken = question.Spoken.Length == 0
            ? "A screen reader says nothing on this screen.\n"
            : $"What a screen reader says on this screen, item by item in the app's order: \"{question.Spoken}\".\n";
        var ocr = question.Ocr.Length == 0
            ? "OCR read no text on a picture of this screen.\n"
            : $"The text OCR read on a picture of this screen, line by line from the top, which may misread some words: \"{question.Ocr}\".\n";
        var picture = question.Pictured
            ? "Picture A is the whole screen. Images cover much of it, so it may show what the text does not.\n"
            : "";
        return $"A screen of the Android app {question.App} has the title \"{question.Title}\", which a screen reader is given as the screen's {question.TitleSource}.\n" +
            page + spoken + ocr + picture;
    }

    /// <summary>The question, of the title judged (rule 5).</summary>
    public static string Question(PhoneScreenTitleQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var title = question.PageTitle is null ? $"the screen's title, \"{question.Title}\"" : $"the web page's title, \"{question.PageTitle}\"";
        return $"Which one of these is true of {title}?\n" +
            "- describes-the-screen: it says what this screen is for or what it shows, so that a person could tell this screen from the app's other screens by it.\n" +
            "- does-not-describe: it does not, such as only the app's name on a screen with a purpose of its own, a word that would fit any screen, or the title of a different screen.\n" +
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
    public static IReadOnlyList<ScreenTitleRequest> Requests(PhoneScreenTitleRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<ScreenTitleRequest>();
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
    public static ScreenTitleRequest RequestOf(PhoneScreenTitleQuestion question, PhonePixels? pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        var input = Context(question) + Question(question);
        var signed = SignedText(question);
        if (!question.Pictured)
        {
            return new ScreenTitleRequest(question, Request(TextTemplate, ModelUses.TextChecks, input, null, AndroidImageAlternative.TextSignature(signed, question.Spoken)));
        }

        ArgumentNullException.ThrowIfNull(pixels);
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels));
        var signature = AndroidImageAlternative.Signature(pixels, new PhoneBounds(0, 0, pixels.Width, pixels.Height), signed, question.Spoken);
        return new ScreenTitleRequest(question, Request(PicturedTemplate, ModelUses.VisionChecks, input, screen, signature));
    }

    /// <summary>
    /// The text an answer is kept against besides what a screen reader says
    /// (rule 8): where the title came from, the title, the web page's title
    /// and the text OCR read.
    /// </summary>
    internal static string SignedText(PhoneScreenTitleQuestion question) =>
        $"{question.TitleSource}\n{question.Title}\n{question.PageTitle}\n{question.Ocr}";

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

        return new DescribedImageRequest(request, "screen-title-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16], signature);
    }

    /// <summary>
    /// Asks the project's models about each screen title the rules leave
    /// in the audit, and gives the audit with the answers judged: a
    /// pictured question of the model for vision-based checks, any other of
    /// the model for text-based checks. A screen the same as one answered
    /// before, in this run or on a task of the project (or, for a recording
    /// in no project, of the recording) by the same model, is given that
    /// answer again, copied into this task (rule 8). Where the task may use
    /// no model for a question's use, it is not asked and not judged (rule
    /// 7). A question whose step fails is not judged, and the note says why.
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
        if (audit.ScreenTitles is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        var textNone = run.TextQuestions > 0 ? context.NoModel(ModelUses.TextChecks) : null;
        var visionNone = run.PicturedQuestions > 0 ? context.NoModel(ModelUses.VisionChecks) : null;
        if ((run.TextQuestions == 0 || textNone is not null) && (run.PicturedQuestions == 0 || visionNone is not null))
        {
            return audit.WithScreenTitles(run.Judged(new Dictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer>(), NotAsked(run, textNone, visionNone)));
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
        var answers = new Dictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer>();
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
            note += string.Create(CultureInfo.CurrentCulture, $"; {again:N0} {(again == 1 ? "answer was" : "answers were")} the one first given for the same screen");
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

        return audit.WithScreenTitles(run.Judged(answers, note));
    }

    /// <summary>The note of questions not asked, as no model may be used for their checks (rule 7).</summary>
    public static string NotAsked(PhoneScreenTitleRun run, string? textReason, string? visionReason)
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
    private static string Note(PhoneScreenTitleRun judged, string start)
    {
        var findings = judged.Findings.Count;
        return start + string.Create(CultureInfo.CurrentCulture,
            $"; {Count(judged.Answers.Count)} answered: {judged.Described:N0} judged to describe {(judged.Described == 1 ? "its screen" : "their screens")}, not confirmed, and {findings:N0} {(findings == 1 ? "warning" : "warnings")}");
    }

    /// <summary>What the answers make of a screen's title (rule 6), the deciding model's.</summary>
    internal static PhoneScreenTitleAnswer Judge(QuestionAnswers answers, PhoneScreenTitleQuestion question, bool again)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(question);
        var deciding = answers.Escalated ?? answers.First;
        var describes = deciding.One.Answer == deciding.Two.Answer && deciding.One.Answer == DescribesTheScreen;
        var inference = CheckQuestions.Judge(answers, group => Interpret(group.One, group.Two, group.Model, group.Cloud, question, again));
        return new PhoneScreenTitleAnswer(describes ? PhoneScreenTitleOutcome.Describes : PhoneScreenTitleOutcome.Finding, inference);
    }

    /// <summary>
    /// What two answers make of a screen's title (rule 6): agreed that it
    /// does not describe its screen, a warning for a person to verify;
    /// agreed that it does, information, not a finding; agreed that what
    /// was given does not tell, or not agreed, a warning to check manually.
    /// Where the answers were first given for the same screen elsewhere, it
    /// says so.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud, PhoneScreenTitleQuestion question, bool again = false)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(question);
        var who = $"{model}, {(cloud ? "a cloud model" : "a local model")},";
        var twice = ReferenceEquals(first, second) ? "" : " twice";
        var where = again
            ? question.Pictured
                ? " for the same screen, by its title, what a screen reader says, the text OCR read and its picture,"
                : " for the same screen, by its title, what a screen reader says and the text OCR read,"
            : "";
        var from = question.Pictured
            ? "from the screen's picture, what a screen reader says on it and the text OCR read"
            : "from what a screen reader says on the screen and the text OCR read";
        var title = question.PageTitle is null
            ? $"the screen's title, \"{question.Title}\","
            : $"the web page's title, \"{question.PageTitle}\", in the screen's title,";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice{where} whether {title} describes its screen and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            DoesNotDescribe => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} inferred {from}{twice}{where} that {title} does not describe its screen: \"{first.Reason}\"; a person should verify this",
                Inferred: true, model, cloud),
            DescribesTheScreen => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred {from}{twice}{where} that {title} describes its screen, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell {from}{twice}{where} whether {title} describes its screen: \"{first.Reason}\"; check manually",
                Inferred: true, model, cloud),
        };
    }

    /// <summary>
    /// What kept answers make of a run, for the Checks tab: the first
    /// answers the project's model for each question's use gave for the
    /// same screen (rule 8), with the model for complex questions' where
    /// the first do not settle a question. A question whose use has no
    /// kept answers given or none for it is not judged, and the note counts
    /// it; where a reason is given that no model may be used for a use,
    /// the note says so of its questions (rule 7).
    /// </summary>
    public static PhoneScreenTitleRun FromKept(
        PhoneScreenTitleRun run,
        IReadOnlyList<ScreenTitleRequest> requests,
        ScreenTitleKept? text,
        ScreenTitleKept? vision,
        string? textNone = null,
        string? visionNone = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        var answers = new Dictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer>();
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
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "screen title" : "screen titles")}");
}
