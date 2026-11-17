using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// One question about a described image as it is sent: the request, its
/// step key for each run, and the signature its answer is kept against.
/// </summary>
public sealed record DescribedImageRequest(ModelRequest Request, string Key, byte[] Signature)
{
    /// <summary>The step key of a run, 1 to 4.</summary>
    public string StepKey(int run) => $"{Key}-{run.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The questions of one described image: for a control, what kind of
/// image it is (<see cref="Kind"/>) and whether the icon fits its name
/// (<see cref="Main"/>); for an image that is not a control, whether its
/// description matches it (<see cref="Main"/>, and no kind question).
/// </summary>
public sealed record DescribedImageRequests(PhoneDescribedImageQuestion Question, DescribedImageRequest? Kind, DescribedImageRequest Main);

/// <summary>
/// The questions of the icon and description checks for the model chosen
/// for vision-based checks (android-checks.md, step 21, rules 4 to 7): for
/// each described image the rules leave
/// (<see cref="PhoneDescribedImageRun.Questions"/>), the whole screen with
/// the image outlined and a close-up of it, enlarged when it is small, as
/// the check of images with no text alternative gives them
/// (<see cref="AndroidImageAlternative"/>), the image's name or
/// description, what a screen reader says for the item that says it, and
/// what a screen reader says on the screen with the image's place marked.
/// A control's image is first asked what kind of image it is, and its
/// name is judged against it unless both answers agree it is not an icon;
/// an image that is not a control is asked whether its description fits
/// it. Each question is asked twice, once for a model that gives the same
/// answer to the same request, again of the model for complex questions
/// where the first answers do not settle it, and each answer is kept
/// against the image's own pixels, its description and what a screen
/// reader says on the screen, so the same image is given the first answer
/// again (rule 6). The questions are those measured on 51 described
/// images on 2026-11-17, with the reason asked for first.
/// </summary>
public static class AndroidDescribedImages
{
    public const string KindTemplate = "android-image-kind";

    public const string IconTemplate = "android-icon-name";

    public const string DescriptionTemplate = "android-image-description";

    public const int TemplateVersion = 1;

    public const string CannotTell = "cannot-tell";

    public const string Icon = "icon";

    public static readonly IReadOnlyList<string> Kinds =
        ["photograph", "illustration", Icon, "logo", "chart-or-diagram", "text", "decoration", CannotTell];

    public const string FitsTheName = "fits-the-name";

    public const string SymbolForSomethingElse = "symbol-for-something-else";

    public static readonly IReadOnlyList<string> IconAnswers = [FitsTheName, SymbolForSomethingElse, CannotTell];

    public const string Matches = "matches";

    public const string DoesNotMatch = "does-not-match";

    public static readonly IReadOnlyList<string> DescriptionAnswers = [Matches, DoesNotMatch, CannotTell];

    public const string Instructions =
        "You judge one image in an Android app for WCAG 2.2 success criterion 1.1.1, Non-text Content. " +
        "Answer only from what the pictures show and what a screen reader says, as given, with one of the fixed answers.";

    private const string Ending = "First write one sentence saying what in the pictures shows the answer, then give the answer.";

    /// <summary>What the question gives of the image and the screen, before what it asks.</summary>
    public static string Context(PhoneDescribedImageQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var about = question.IsControl
            ? $"The outlined image is a control. Its name, which a screen reader says for it, is \"{question.Description}\".\n"
            : $"The outlined image's text alternative, as the app gives it, is \"{question.Description}\". " +
              (question.SpokenBy == PhoneDescriptionSpeaker.Holder
                  ? $"A screen reader says it as part of what it says for the item that holds the image: \"{question.Spoken}\".\n"
                  : "A screen reader says it for the image.\n");
        return $"Picture A is the whole screen of the Android app {question.App}, on its screen \"{question.ScreenName}\", with one image outlined in cyan. " +
            "Picture B is the outlined image and a little of the screen around it, enlarged when the image is small.\n" +
            about +
            $"What a screen reader says on this screen, item by item in the app's order, with {PhoneImageAlternativeRun.ImageMark} at the image's place: \"{question.ScreenText}\".\n" +
            "Text that can be seen in the pictures but is not in what a screen reader says is not given to a screen reader user.\n";
    }

    /// <summary>The kind question, asked of a control's image first (rule 4).</summary>
    public const string KindQuestion =
        "What kind of image is the outlined image?\n" +
        "- photograph: a photograph of people, places or things.\n" +
        "- illustration: a drawing, animation or illustration that is not a small symbol.\n" +
        "- icon: a small symbol, such as an arrow, a star, a magnifier or a weather symbol.\n" +
        "- logo: the logo or mark of a company, product or app.\n" +
        "- chart-or-diagram: a chart, graph, map or diagram that shows data or how things relate.\n" +
        "- text: mostly words or numbers drawn as an image.\n" +
        "- decoration: a line, a background, a shape or a flourish.\n" +
        "- cannot-tell: the pictures do not show enough to judge, such as when nothing can be seen inside the outline.\n" +
        Ending;

    /// <summary>The icon question, of a control's image and its name.</summary>
    public static string IconQuestion(string name) =>
        $"Which one of these is true of the outlined icon and the control's name, \"{name}\"?\n" +
        "- fits-the-name: the icon shows what the name says the control is or does, or is a symbol commonly used for it, such as a left-pointing arrow for \"Navigate up\" or a magnifier for \"Search\", or is the product's own icon for the product the name gives.\n" +
        "- symbol-for-something-else: the icon is the usual symbol for a different, well-known action or thing, so a person who sees it and a person who hears the name would expect different things, such as a bin for \"Save\" or a star for \"Delete\".\n" +
        "- cannot-tell: neither of these: the icon is not a symbol you know, or the pictures do not show enough to judge.\n" +
        Ending;

    /// <summary>The description question, of an image that is not a control.</summary>
    public static string DescriptionQuestion(string description) =>
        $"Does the text alternative, \"{description}\", fit what the outlined image shows?\n" +
        "- matches: it names or describes what the image shows, or what the image stands for.\n" +
        "- does-not-match: it names or describes something the image does not show, or contradicts it.\n" +
        "- cannot-tell: the pictures do not show enough to judge.\n" +
        Ending;

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// question whose picture cannot be read is left out, and not judged.
    /// </summary>
    public static IReadOnlyList<DescribedImageRequests> Requests(PhoneDescribedImageRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<DescribedImageRequests>();
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

            requests.Add(RequestsOf(question, pixels));
        }

        return requests;
    }

    /// <summary>A question's requests, from its picture's pixels.</summary>
    public static DescribedImageRequests RequestsOf(PhoneDescribedImageQuestion question, PhonePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pixels);
        var box = AndroidImageAlternative.Clamp(question.Bounds, pixels) ?? throw new ArgumentException("The image is not on the picture.", nameof(question));
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels, box));
        var closeUp = AndroidImagesOfText.Png(AndroidImageAlternative.CloseUp(pixels, box));
        var context = Context(question);
        var signature = AndroidImageAlternative.Signature(pixels, box, SignedText(question), question.ScreenText);
        if (question.IsControl)
        {
            return new DescribedImageRequests(
                question,
                Request(KindTemplate, "image-kind-", context + KindQuestion, Kinds, screen, closeUp, signature),
                Request(IconTemplate, "icon-name-", context + IconQuestion(question.Description), IconAnswers, screen, closeUp, signature));
        }

        return new DescribedImageRequests(
            question,
            null,
            Request(DescriptionTemplate, "image-description-", context + DescriptionQuestion(question.Description), DescriptionAnswers, screen, closeUp, signature));
    }

    /// <summary>
    /// The text an answer is kept against besides the screen's (rule 6):
    /// whether the image is a control, its description, and for an image
    /// that is not a control, which item a screen reader says it for and
    /// what it says there.
    /// </summary>
    internal static string SignedText(PhoneDescribedImageQuestion question) =>
        question.IsControl
            ? "control\n" + question.Description
            : $"image\n{question.Description}\n{question.SpokenBy}\n{question.Spoken}";

    private static DescribedImageRequest Request(
        string template, string prefix, string input, IReadOnlyList<string> answers, byte[] screen, byte[] closeUp, byte[] signature)
    {
        var request = new ModelRequest(
            template,
            TemplateVersion,
            Instructions,
            input,
            [new ModelImage("A", "image/png", screen), new ModelImage("B", "image/png", closeUp)],
            [],
            new HashSet<string>(StringComparer.Ordinal))
        {
            Use = ModelUses.VisionChecks,
            Answers = answers,
            ReusedAcrossTasks = true,
            Signature = signature,
        };

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{template}\n{TemplateVersion.ToString(CultureInfo.InvariantCulture)}\n{Instructions}\n{input}\n"));
        hash.AppendData(screen);
        hash.AppendData(closeUp);
        return new DescribedImageRequest(request, prefix + Convert.ToHexStringLower(hash.GetHashAndReset())[..16], signature);
    }

    /// <summary>
    /// Asks the model chosen for vision-based checks about each described
    /// image the rules leave in the audit, and gives the audit with the
    /// answers judged. An image the same as one answered before, in this
    /// run or on a task of the project (or, for a recording in no project,
    /// of the recording) by the same model, is given that answer again,
    /// copied into this task (rule 6). Where the task may use no model for
    /// vision-based checks, nothing is asked and nothing is judged (rule
    /// 8). A question whose step fails is not judged, and the note says why.
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
        if (audit.DescribedImages is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        if (context.NoModel(ModelUses.VisionChecks) is { } none)
        {
            return audit.WithDescribedImages(run.Judged(new Dictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer>(), NotAsked(run.Questions.Count, none)));
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
        var copied = new HashSet<Guid>();

        async Task<Asker> AskerAsync(string template)
        {
            IReadOnlyList<SignedAnswer> signed = profileName is null || model is null
                ? []
                : await context.Catalog.SignedAnswersAsync(template, TemplateVersion, profileName, model, project, recording, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<SignedAnswer> signedComplex = complexName is null || complexModel is null
                ? []
                : await context.Catalog.SignedAnswersAsync(template, TemplateVersion, complexName, complexModel, project, recording, cancellationToken).ConfigureAwait(false);
            return new Asker(context, profileName, signed, complexName, signedComplex, held, copied, failures);
        }

        var kinds = await AskerAsync(KindTemplate).ConfigureAwait(false);
        var icons = await AskerAsync(IconTemplate).ConfigureAwait(false);
        var descriptions = await AskerAsync(DescriptionTemplate).ConfigureAwait(false);
        var answers = new Dictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Kind is { } kindRequest)
            {
                if (await kinds.AskAsync(kindRequest, cancellationToken).ConfigureAwait(false) is not { } kind)
                {
                    continue;
                }

                if (NotAnIcon(kind.Answers, kind.Again) is { } notIcon)
                {
                    answers[request.Question] = notIcon;
                    continue;
                }

                if (await icons.AskAsync(request.Main, cancellationToken).ConfigureAwait(false) is { } icon)
                {
                    answers[request.Question] = Judge(icon.Answers, request.Question, icon.Again);
                }

                continue;
            }

            if (await descriptions.AskAsync(request.Main, cancellationToken).ConfigureAwait(false) is { } description)
            {
                answers[request.Question] = Judge(description.Answers, request.Question, description.Again);
            }
        }

        var judged = run.Judged(answers, null);
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "was" : "were")} for the model for vision-based checks")
            + AskedAgain(kinds.Escalated + icons.Escalated + descriptions.Escalated);
        var again = kinds.Again + icons.Again + descriptions.Again;
        if (again > 0)
        {
            note += string.Create(CultureInfo.CurrentCulture, $"; {again:N0} {(again == 1 ? "answer was" : "answers were")} the one first given for the same image");
        }

        if (requests.Count < run.Questions.Count)
        {
            note += string.Create(CultureInfo.CurrentCulture, $"; {Count(run.Questions.Count - requests.Count)} not asked as the picture could not be read");
        }

        if (failures.Count > 0)
        {
            note += "; not answered: " + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return audit.WithDescribedImages(run.Judged(answers, note));
    }

    /// <summary>The note of questions not asked, as no model may be used for vision-based checks (rule 8).</summary>
    public static string NotAsked(int questions, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions)} left by the rules {(questions == 1 ? "was" : "were")} not judged, as no model may be used for vision-based checks: {reason}");

    // What became of the questions: how many were answered, and what the
    // answers made of them.
    private static string Note(PhoneDescribedImageRun judged, string start)
    {
        var findings = judged.Findings.Count;
        return start + string.Create(CultureInfo.CurrentCulture,
            $"; {Count(judged.Answers.Count)} answered: {judged.Fit:N0} judged to fit, not confirmed, {judged.NotIcons:N0} judged not to be icons, so not judged against their names, and {findings:N0} {(findings == 1 ? "warning" : "warnings")}");
    }

    /// <summary>
    /// The answer of a control whose image both answers, the deciding
    /// model's, agree is not an icon (rule 4); null where the icon question
    /// is to be asked.
    /// </summary>
    internal static PhoneDescribedImageAnswer? NotAnIcon(QuestionAnswers answers, bool again)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var deciding = answers.Escalated ?? answers.First;
        if (deciding.One.Answer != deciding.Two.Answer || deciding.One.Answer is Icon or CannotTell)
        {
            return null;
        }

        return new PhoneDescribedImageAnswer(PhoneDescribedImageOutcome.NotAnIcon, CheckQuestions.Judge(answers, group =>
        {
            var (who, twice, where) = Words(group, again);
            return new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice}{where} that the image is not an icon but {Article(group.One.Answer)}, so its name was not judged against it: \"{group.One.Reason}\"",
                Inferred: true, group.Model, group.Cloud);
        }));
    }

    /// <summary>What the answers to the icon or description question make of the image (rule 7), the deciding model's.</summary>
    internal static PhoneDescribedImageAnswer Judge(QuestionAnswers answers, PhoneDescribedImageQuestion question, bool again)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(question);
        var deciding = answers.Escalated ?? answers.First;
        var fits = deciding.One.Answer == deciding.Two.Answer && deciding.One.Answer is FitsTheName or Matches;
        var inference = CheckQuestions.Judge(answers, group => Interpret(group.One, group.Two, group.Model, group.Cloud, question, again));
        return new PhoneDescribedImageAnswer(fits ? PhoneDescribedImageOutcome.Fits : PhoneDescribedImageOutcome.Finding, inference);
    }

    /// <summary>
    /// What two answers to the icon or description question make of an
    /// image (rule 7): agreed that the icon is the usual symbol for
    /// something else, or that the description does not match, a warning
    /// for a person to verify; agreed that it fits, information, not a
    /// finding; agreed that the pictures do not tell, or not agreed, a
    /// warning to check manually. Where the answers were first given for
    /// the same image elsewhere, it says so.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud, PhoneDescribedImageQuestion question, bool again = false)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(question);
        var (who, twice, where) = Words(new GroupAnswers(first, second, model, cloud), again);
        var whether = question.IsControl
            ? $"whether the icon fits the control's name, \"{question.Description}\""
            : $"whether the description, \"{question.Description}\", matches what the image shows";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice{where} {whether} and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            SymbolForSomethingElse => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} inferred from the pictures{twice}{where} that the icon is the usual symbol for something other than the control's name, \"{question.Description}\", says: \"{first.Reason}\"; a person should verify this",
                Inferred: true, model, cloud),
            DoesNotMatch => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} inferred from the pictures{twice}{where} that the description, \"{question.Description}\", does not match what the image shows: \"{first.Reason}\"; a person should verify this",
                Inferred: true, model, cloud),
            FitsTheName => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice}{where} that the icon fits the control's name, \"{question.Description}\", not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            Matches => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice}{where} that the description, \"{question.Description}\", matches what the image shows, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell from the pictures{twice}{where} {whether}: \"{first.Reason}\"; check manually",
                Inferred: true, model, cloud),
        };
    }

    private static (string Who, string Twice, string Where) Words(GroupAnswers group, bool again) =>
        ($"{group.Model}, {(group.Cloud ? "a cloud model" : "a local model")},",
            ReferenceEquals(group.One, group.Two) ? "" : " twice",
            again ? " for the same image, by its pixels, its description and what a screen reader says on the screen," : "");

    private static string Article(string kind) =>
        kind switch
        {
            "illustration" => "an illustration",
            "chart-or-diagram" => "a chart or diagram",
            "text" => "text",
            "decoration" => "a decoration",
            _ => "a " + kind,
        };

    /// <summary>
    /// What kept answers make of a run, for the Checks tab: the first
    /// answers the project's model for vision-based checks gave for the
    /// same image as each request, by template (rule 6), with the model for
    /// complex questions' where the first do not settle a question. A
    /// question with none is not judged, and the note counts it.
    /// </summary>
    public static PhoneDescribedImageRun FromKept(
        PhoneDescribedImageRun run,
        IReadOnlyList<DescribedImageRequests> requests,
        IReadOnlyDictionary<string, IReadOnlyList<SignedAnswer>> signed,
        string model,
        IReadOnlyDictionary<string, IReadOnlyList<SignedAnswer>>? complexSigned = null,
        string? complexModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(model);
        var answers = new Dictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer>();
        int waiting = 0, askedAgain = 0;

        // The kept answers of one question: None where there are none,
        // Waiting where the model for complex questions has not answered
        // one the first did not settle.
        (QuestionAnswers? Answers, bool Waiting, bool Again) KeptOf(string template, DescribedImageRequest request)
        {
            var simple = signed.GetValueOrDefault(template) ?? [];
            if (AndroidImageAlternative.Kept(simple, request.Signature) is not { } kept)
            {
                return (null, false, false);
            }

            var first = new GroupAnswers(kept.One, kept.Two, model, kept.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
            var again = kept.Sources[0].StepKey != request.StepKey(1);
            if (complexModel is null || !CheckQuestions.Unsettled(kept.One, kept.Two, CannotTell))
            {
                return (new QuestionAnswers(first, null), false, again);
            }

            var complex = complexSigned?.GetValueOrDefault(template) ?? [];
            if (AndroidImageAlternative.Kept(complex, request.Signature, CheckQuestions.EscalatedRun) is not { } escalated)
            {
                return (null, true, again);
            }

            askedAgain++;
            return (new QuestionAnswers(first, new GroupAnswers(escalated.One, escalated.Two, complexModel,
                escalated.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis)), false, again);
        }

        foreach (var request in requests)
        {
            if (request.Kind is { } kindRequest)
            {
                var kind = KeptOf(KindTemplate, kindRequest);
                if (kind.Answers is not { } kindAnswers)
                {
                    waiting += kind.Waiting ? 1 : 0;
                    continue;
                }

                if (NotAnIcon(kindAnswers, kind.Again) is { } notIcon)
                {
                    answers[request.Question] = notIcon;
                    continue;
                }
            }

            var main = KeptOf(request.Kind is null ? DescriptionTemplate : IconTemplate, request.Main);
            if (main.Answers is not { } mainAnswers)
            {
                waiting += main.Waiting ? 1 : 0;
                continue;
            }

            answers[request.Question] = Judge(mainAnswers, request.Question, main.Again);
        }

        var judged = run.Judged(answers, null);
        var unanswered = run.Questions.Count - answers.Count - waiting;
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "is" : "are")} for {model}")
            + AskedAgain(askedAgain)
            + CheckQuestions.Waiting(waiting, Count(waiting))
            + (unanswered > 0 ? string.Create(CultureInfo.CurrentCulture, $", {Count(unanswered)} not yet asked") : "");
        return run.Judged(answers, note);
    }

    // Questions, not images, as a control's kind and icon questions can
    // each be asked again.
    private static string AskedAgain(int questions) =>
        questions == 0
            ? ""
            : string.Create(CultureInfo.CurrentCulture,
                $"; {questions:N0} {(questions == 1 ? "question" : "questions")} asked again of the model for complex questions");

    private static string Count(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "described image" : "described images")}");

    /// <summary>
    /// Asks one template's questions, each answer kept against its
    /// signature: an image the same as one answered earlier in the run is
    /// given that answer; one answered on an earlier task is given its
    /// first answer, copied into this task; any other is asked, and asked
    /// again of the model for complex questions where it is not settled.
    /// </summary>
    private sealed class Asker(
        AnalysisTaskContext context,
        string? profileName,
        IReadOnlyList<SignedAnswer> signed,
        string? complexName,
        IReadOnlyList<SignedAnswer> signedComplex,
        HashSet<string> held,
        HashSet<Guid> copied,
        List<string> failures)
    {
        private readonly List<(byte[] Signature, QuestionAnswers Answers)> _given = [];

        public int Escalated { get; private set; }

        public int Again { get; private set; }

        public async Task<(QuestionAnswers Answers, bool Again)?> AskAsync(DescribedImageRequest request, CancellationToken cancellationToken)
        {
            if (_given.FirstOrDefault(item => AndroidImageAlternative.SameImage(item.Signature, request.Signature)) is { Signature: not null } earlier)
            {
                Again++;
                return (earlier.Answers, true);
            }

            GroupAnswers first;
            var reused = false;
            if (AndroidImageAlternative.Kept(signed, request.Signature) is { } kept)
            {
                await CopyAsync(kept.Sources, cancellationToken).ConfigureAwait(false);
                first = new GroupAnswers(kept.One, kept.Two, profileName!, kept.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
                reused = true;
            }
            else if (await CheckQuestions.AskGroupAsync(context, request.StepKey, request.Request, 1, failures, cancellationToken).ConfigureAwait(false) is { } answered)
            {
                first = answered;
            }
            else
            {
                return null;
            }

            GroupAnswers? second = null;
            if (complexName is not null && CheckQuestions.Unsettled(first.One, first.Two, CannotTell))
            {
                if (AndroidImageAlternative.Kept(signedComplex, request.Signature, CheckQuestions.EscalatedRun) is { } keptComplex)
                {
                    await CopyAsync(keptComplex.Sources, cancellationToken).ConfigureAwait(false);
                    second = new GroupAnswers(keptComplex.One, keptComplex.Two, complexName, keptComplex.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
                }
                else
                {
                    second = await CheckQuestions.EscalateAsync(context, request.StepKey, request.Request, first, CannotTell, failures, cancellationToken).ConfigureAwait(false);
                }
            }

            Escalated += second is null ? 0 : 1;
            Again += reused ? 1 : 0;
            var answers = new QuestionAnswers(first, second);
            _given.Add((request.Signature, answers));
            return (answers, reused);
        }

        // An answer kept on an earlier task, copied into this task.
        private async Task CopyAsync(IReadOnlyList<SignedAnswer> sources, CancellationToken cancellationToken)
        {
            foreach (var source in sources.Where(source => source.TaskId != context.Task.TaskId && !held.Contains(source.StepKey) && copied.Add(source.AttemptId)))
            {
                var number = await context.Catalog.AttemptCountAsync(context.Task.TaskId, source.StepKey, cancellationToken).ConfigureAwait(false) + 1;
                await context.Catalog.ReuseAttemptAsync(source.AttemptId, context.Task.TaskId, number, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
