using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// The question of the decorative image check for the model chosen for
/// vision-based checks (android-checks.md, step 23, rules 4 to 7): for each
/// image the rules leave (<see cref="PhoneDecorativeImageRun.Questions"/>),
/// an image that is not a control whose description a screen reader says
/// with other text, or for the image alone, the whole screen with the image
/// outlined and a close-up of it, as the other image checks give them
/// (<see cref="AndroidImageAlternative"/>), and what a screen reader says
/// on the screen, with the image's own description marked as the image's,
/// so that it is not taken as other text that says the same. The model
/// answers whether the image is informative, decorative, or cannot tell,
/// reason first. Each question is asked twice, again of the model for
/// complex questions where the first answers do not settle it, and each
/// answer is kept against the image's own pixels, its description and
/// what a screen reader says on the screen, so the same image is given the
/// first answer again (rule 6).
/// </summary>
public static class AndroidDecorativeImages
{
    public const string Template = "android-image-decorative";

    public const int TemplateVersion = 1;

    public const string Informative = "informative";

    public const string Decorative = "decorative";

    public const string CannotTell = "cannot-tell";

    public static readonly IReadOnlyList<string> Answers = [Informative, Decorative, CannotTell];

    public const string Instructions =
        "You judge one image in an Android app for WCAG 2.2 success criterion 1.1.1, Non-text Content: " +
        "whether a screen reader user needs the image, or it is decoration a screen reader should not announce. " +
        "Answer only from what the pictures show and what a screen reader says, as given, with one of the fixed answers.";

    /// <summary>What the question gives of the image and the screen, before what it asks (rule 4).</summary>
    public static string Context(PhoneDescribedImageQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var said = question.SpokenBy == PhoneDescriptionSpeaker.Holder
            ? $"A screen reader says it as part of what it says for the item that holds the image, \"{question.Spoken}\", which is the item just before {PhoneImageAlternativeRun.ImageMark} below; " +
              $"the words \"{question.Description}\" in it are the image's own text alternative, not other text.\n"
            : $"A screen reader stops on the image itself and says its text alternative, at {PhoneImageAlternativeRun.ImageMark} below.\n";
        return $"Picture A is the whole screen of the Android app {question.App}, on its screen \"{question.ScreenName}\", with one image outlined in cyan. " +
            "Picture B is the outlined image and a little of the screen around it, enlarged when the image is small.\n" +
            $"The outlined image's text alternative, as the app gives it, is \"{question.Description}\". " +
            said +
            $"What a screen reader says on this screen, item by item in the app's order, with {PhoneImageAlternativeRun.ImageMark} at the image's place: \"{question.ScreenText}\".\n" +
            "Text that can be seen in the pictures but is not in what a screen reader says is not given to a screen reader user.\n";
    }

    /// <summary>The question, after the context.</summary>
    public const string Question =
        "Is the outlined image informative or decorative for a screen reader user?\n" +
        "- informative: the image gives information a screen reader user needs that nothing else a screen reader says on this screen gives, " +
        "leaving out the image's own text alternative, such as a symbol whose meaning no other text says, a status, or a photograph or illustration that shows something the text does not.\n" +
        "- decorative: the image adds nothing a screen reader user needs: it is only for decoration or appearance, " +
        "or it only repeats what other text a screen reader says on this screen already gives, leaving out the image's own text alternative.\n" +
        "- cannot-tell: the pictures and the text do not show enough to judge.\n" +
        "First write one sentence saying what in the pictures and the text shows the answer, then give the answer.";

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// question whose picture cannot be read is left out, and not judged.
    /// </summary>
    public static IReadOnlyList<(PhoneDescribedImageQuestion Question, DescribedImageRequest Request)> Requests(
        PhoneDecorativeImageRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<(PhoneDescribedImageQuestion, DescribedImageRequest)>();
        var pixelsOf = new Dictionary<string, PhonePixels?>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in run.Questions)
        {
            var path = question.Picture.AbsolutePath;
            if (!pixelsOf.TryGetValue(path, out var pixels))
            {
                pixelsOf[path] = pixels = load(path);
            }

            if (pixels is null || AndroidImageAlternative.Clamp(question.Bounds, pixels) is null)
            {
                continue;
            }

            requests.Add((question, RequestOf(question, pixels)));
        }

        return requests;
    }

    /// <summary>A question's request, from its picture's pixels.</summary>
    public static DescribedImageRequest RequestOf(PhoneDescribedImageQuestion question, PhonePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pixels);
        var box = AndroidImageAlternative.Clamp(question.Bounds, pixels) ?? throw new ArgumentException("The image is not on the picture.", nameof(question));
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels, box));
        var closeUp = AndroidImagesOfText.Png(AndroidImageAlternative.CloseUp(pixels, box));
        var input = Context(question) + Question;
        var signature = AndroidImageAlternative.Signature(pixels, box, AndroidDescribedImages.SignedText(question), question.ScreenText);
        var request = new ModelRequest(
            Template,
            TemplateVersion,
            Instructions,
            input,
            [new ModelImage("A", "image/png", screen), new ModelImage("B", "image/png", closeUp)],
            [],
            new HashSet<string>(StringComparer.Ordinal))
        {
            Use = ModelUses.VisionChecks,
            Answers = Answers,
            ReusedAcrossTasks = true,
            Signature = signature,
        };

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{Template}\n{TemplateVersion.ToString(CultureInfo.InvariantCulture)}\n{Instructions}\n{input}\n"));
        hash.AppendData(screen);
        hash.AppendData(closeUp);
        return new DescribedImageRequest(request, "image-decorative-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16], signature);
    }

    /// <summary>
    /// Asks the model chosen for vision-based checks about each image the
    /// rules leave in the audit, and gives the audit with the answers
    /// judged. An image the same as one answered before, in this run or on
    /// a task of the project (or, for a recording in no project, of the
    /// recording) by the same model, is given that answer again, copied
    /// into this task (rule 6). Where the task may use no model for
    /// vision-based checks, nothing is asked and nothing is judged (rule 7).
    /// A question whose step fails is not judged, and the note says why.
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
        if (audit.DecorativeImages is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        if (context.NoModel(ModelUses.VisionChecks) is { } none)
        {
            return audit.WithDecorativeImages(run.Judged(new Dictionary<PhoneDescribedImageQuestion, PhoneDecorativeAnswer>(), NotAsked(run.Questions.Count, none)));
        }

        var requests = Requests(run, load);
        var profileName = context.ModelFor(ModelUses.VisionChecks);
        var model = profileName is null ? null : context.Settings.ForProject(null).Profile(profileName)?.Model;
        var complexName = CheckQuestions.EscalationProfile(context, ModelUses.VisionChecks);
        var complexModel = complexName is null ? null : context.Settings.ForProject(null).Profile(complexName)?.Model;
        var recording = context.Task.RecordingId;
        var project = context.Task.ProjectId ??
            (recording is { } id ? (await context.Catalog.ProjectOfAsync(id, cancellationToken).ConfigureAwait(false))?.ProjectId : null);
        var held = (await context.Catalog.AttemptsAsync(context.Task.TaskId, cancellationToken).ConfigureAwait(false))
            .Where(item => item.Outcome == "accepted")
            .Select(item => item.StepKey)
            .ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();
        IReadOnlyList<SignedAnswer> signed = profileName is null || model is null
            ? []
            : await context.Catalog.SignedAnswersAsync(Template, TemplateVersion, profileName, model, project, recording, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SignedAnswer> signedComplex = complexName is null || complexModel is null
            ? []
            : await context.Catalog.SignedAnswersAsync(Template, TemplateVersion, complexName, complexModel, project, recording, cancellationToken).ConfigureAwait(false);
        var asker = new SignedAsker(context, profileName, signed, complexName, signedComplex, held, [], failures, CannotTell);
        var answers = new Dictionary<PhoneDescribedImageQuestion, PhoneDecorativeAnswer>();
        foreach (var (question, request) in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await asker.AskAsync(request, cancellationToken).ConfigureAwait(false) is { } asked)
            {
                answers[question] = Judge(asked.Answers, question, asked.Again);
            }
        }

        var judged = run.Judged(answers, null);
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "was" : "were")} for the model for vision-based checks")
            + AskedAgain(asker.Escalated);
        if (asker.Again > 0)
        {
            note += string.Create(CultureInfo.CurrentCulture, $"; {asker.Again:N0} {(asker.Again == 1 ? "answer was" : "answers were")} the one first given for the same image");
        }

        if (requests.Count < run.Questions.Count)
        {
            note += string.Create(CultureInfo.CurrentCulture, $"; {Count(run.Questions.Count - requests.Count)} not asked as the picture could not be read");
        }

        if (failures.Count > 0)
        {
            note += "; not answered: " + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return audit.WithDecorativeImages(run.Judged(answers, note));
    }

    /// <summary>The note of questions not asked, as no model may be used for vision-based checks (rule 7).</summary>
    public static string NotAsked(int questions, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions)} left by the rules {(questions == 1 ? "was" : "were")} not judged, as no model may be used for vision-based checks: {reason}");

    // What became of the questions: how many were answered, and what the
    // answers made of them.
    private static string Note(PhoneDecorativeImageRun judged, string start)
    {
        var findings = judged.Findings.Count;
        return start + string.Create(CultureInfo.CurrentCulture,
            $"; {Count(judged.Answers.Count)} answered: {judged.Informative:N0} judged informative, not confirmed, and {findings:N0} {(findings == 1 ? "warning" : "warnings")}");
    }

    /// <summary>What the answers make of the image (rule 5), the deciding model's.</summary>
    internal static PhoneDecorativeAnswer Judge(QuestionAnswers answers, PhoneDescribedImageQuestion question, bool again)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(question);
        var deciding = answers.Escalated ?? answers.First;
        var informative = deciding.One.Answer == deciding.Two.Answer && deciding.One.Answer == Informative;
        var inference = CheckQuestions.Judge(answers, group => Interpret(group.One, group.Two, group.Model, group.Cloud, question, again));
        return new PhoneDecorativeAnswer(informative ? PhoneDecorativeOutcome.Informative : PhoneDecorativeOutcome.Finding, inference);
    }

    /// <summary>
    /// What two answers make of an image (rule 5): agreed that it is
    /// decorative, a warning for a person to verify; agreed that it is
    /// informative, information, not a finding; agreed that the pictures do
    /// not tell, or not agreed, a warning to check manually. Where the
    /// answers were first given for the same image elsewhere, it says so.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud, PhoneDescribedImageQuestion question, bool again = false)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(question);
        var who = $"{model}, {(cloud ? "a cloud model" : "a local model")},";
        var twice = ReferenceEquals(first, second) ? "" : " twice";
        var where = again ? " for the same image, by its pixels, its description and what a screen reader says on the screen," : "";
        var image = $"the image a screen reader announces as \"{question.Description}\"";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice{where} whether {image} is informative or decorative and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            Decorative => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} inferred from the pictures{twice}{where} that {image} is decorative, so a screen reader should not announce it: \"{first.Reason}\"; a person should verify this",
                Inferred: true, model, cloud),
            Informative => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice}{where} that {image} is informative, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell from the pictures{twice}{where} whether {image} is informative or decorative: \"{first.Reason}\"; check manually",
                Inferred: true, model, cloud),
        };
    }

    /// <summary>
    /// What kept answers make of a run, for the Checks tab: the first
    /// answers the project's model for vision-based checks gave for the
    /// same image as each request (rule 6), with the model for complex
    /// questions' where the first do not settle a question. A question
    /// with none is not judged, and the note counts it.
    /// </summary>
    public static PhoneDecorativeImageRun FromKept(
        PhoneDecorativeImageRun run,
        IReadOnlyList<(PhoneDescribedImageQuestion Question, DescribedImageRequest Request)> requests,
        IReadOnlyList<SignedAnswer> signed,
        string model,
        IReadOnlyList<SignedAnswer>? complexSigned = null,
        string? complexModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(model);
        var answers = new Dictionary<PhoneDescribedImageQuestion, PhoneDecorativeAnswer>();
        int waiting = 0, askedAgain = 0;
        foreach (var (question, request) in requests)
        {
            if (AndroidImageAlternative.Kept(signed, request.Signature) is not { } kept)
            {
                continue;
            }

            var first = new GroupAnswers(kept.One, kept.Two, model, kept.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
            var again = kept.Sources[0].StepKey != request.StepKey(1);
            if (complexModel is null || !CheckQuestions.Unsettled(kept.One, kept.Two, CannotTell))
            {
                answers[question] = Judge(new QuestionAnswers(first, null), question, again);
                continue;
            }

            if (AndroidImageAlternative.Kept(complexSigned ?? [], request.Signature, CheckQuestions.EscalatedRun) is not { } escalated)
            {
                waiting++;
                continue;
            }

            askedAgain++;
            answers[question] = Judge(new QuestionAnswers(first, new GroupAnswers(escalated.One, escalated.Two, complexModel,
                escalated.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis)), question, again);
        }

        var judged = run.Judged(answers, null);
        var unanswered = run.Questions.Count - answers.Count - waiting;
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "is" : "are")} for {model}")
            + AskedAgain(askedAgain)
            + CheckQuestions.Waiting(waiting, Count(waiting))
            + (unanswered > 0 ? string.Create(CultureInfo.CurrentCulture, $", {Count(unanswered)} not yet asked") : "");
        return run.Judged(answers, note);
    }

    private static string AskedAgain(int questions) =>
        questions == 0
            ? ""
            : string.Create(CultureInfo.CurrentCulture,
                $"; {questions:N0} {(questions == 1 ? "image" : "images")} asked again of the model for complex questions");

    private static string Count(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "image" : "images")}");
}
