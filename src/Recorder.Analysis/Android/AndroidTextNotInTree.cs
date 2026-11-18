using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// The question of the check of text on screen not in the tree for the
/// model chosen for vision-based checks (android-checks.md, step 25, rules
/// 8 to 10): for each line the rules leave
/// (<see cref="PhoneTextNotInTreeRun.Questions"/>), the whole screen with
/// the line outlined and a close-up of it, as the image checks give them,
/// the line as OCR read it, the item it is drawn in, whether an item
/// marked not shown holds the same text, and what a screen reader says on
/// the screen. The model answers whether the line is drawn text a screen
/// reader cannot reach, text behind a dialog or scrim or covered, not
/// text, or cannot tell, reason first. Each question is asked twice, again
/// of the model for complex questions where the first answers do not
/// settle it, and each answer is kept against the line's own pixels, its
/// text and item and what a screen reader says on the screen, so the same
/// line is given the first answer again (rule 10).
/// </summary>
public static class AndroidTextNotInTree
{
    public const string Template = "android-text-not-in-tree";

    public const int TemplateVersion = 1;

    public const string DrawnText = "drawn-text";

    public const string Covered = "covered";

    public const string NotText = "not-text";

    public const string CannotTell = "cannot-tell";

    public static readonly IReadOnlyList<string> Answers = [DrawnText, Covered, NotText, CannotTell];

    public const string Instructions =
        "You judge one line of text that OCR read on a screen of an Android app, which no item the app gives a screen reader holds, " +
        "for WCAG 2.2 success criteria 1.1.1, Non-text Content, and 4.1.2, Name, Role, Value: whether it is text shown to users that a screen reader cannot reach. " +
        "Answer only from what the pictures show and what a screen reader says, as given, with one of the fixed answers.";

    /// <summary>What the question gives of the line and the screen, before what it asks (rule 8).</summary>
    public static string Context(PhoneTextNotInTreeQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var item = question.ContainerClass is null
            ? "The line is in no item of the app's but the window.\n"
            : question.ContainerText is null
                ? $"The line is drawn inside an item of class {question.ContainerClass} that has no text of its own for a screen reader.\n"
                : $"The line is drawn inside an item of class {question.ContainerClass} whose own text for a screen reader is \"{question.ContainerText}\".\n";
        var hidden = question.HeldByHidden
            ? "An item the app marks as not shown holds the same text, so a screen reader does not reach it there either.\n"
            : "";
        return $"Picture A is the whole screen of the Android app {question.App}, on its screen \"{question.ScreenName}\", with one line of text outlined in cyan. " +
            "Picture B is the outlined line and a little of the screen around it, enlarged when it is small.\n" +
            $"OCR read the outlined line as \"{question.Ocr}\", which may misread some letters. No item the app gives a screen reader holds this text.\n" +
            item +
            hidden +
            (question.ScreenText.Length == 0
                ? "A screen reader says nothing on this screen.\n"
                : $"What a screen reader says on this screen, item by item in the app's order: \"{question.ScreenText}\".\n");
    }

    /// <summary>The question, after the context.</summary>
    public const string Question =
        "Which one of these is true of the outlined line?\n" +
        "- drawn-text: it is text shown to users, to be read now, that a screen reader cannot reach, as nothing a screen reader says on this screen gives it.\n" +
        "- covered: it is text behind a dialog, sheet or dimmed layer, or covered by something else, so not meant to be read now.\n" +
        "- not-text: it is not text, such as a shape, icon, pattern or picture that OCR read as letters, or a garbled reading of text a screen reader says.\n" +
        "- cannot-tell: the pictures and the text do not show enough to judge.\n" +
        "First write one sentence saying what in the pictures and the text shows the answer, then give the answer.";

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// question whose picture cannot be read, or whose line is not on it,
    /// is left out, and not judged.
    /// </summary>
    public static IReadOnlyList<(PhoneTextNotInTreeQuestion Question, DescribedImageRequest Request)> Requests(
        PhoneTextNotInTreeRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<(PhoneTextNotInTreeQuestion, DescribedImageRequest)>();
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
    public static DescribedImageRequest RequestOf(PhoneTextNotInTreeQuestion question, PhonePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pixels);
        var box = AndroidImageAlternative.Clamp(question.Bounds, pixels) ?? throw new ArgumentException("The line is not on the picture.", nameof(question));
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels, box));
        var closeUp = AndroidImagesOfText.Png(AndroidImageAlternative.CloseUp(pixels, box));
        var input = Context(question) + Question;
        var signature = AndroidImageAlternative.Signature(pixels, box, SignedText(question), question.ScreenText);
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
        return new DescribedImageRequest(request, "text-not-in-tree-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16], signature);
    }

    /// <summary>
    /// The text an answer is kept against besides what a screen reader says
    /// on the screen (rule 10): the app, the line as OCR read it, the item
    /// it is drawn in and whether an item not shown holds it.
    /// </summary>
    internal static string SignedText(PhoneTextNotInTreeQuestion question) =>
        $"{question.App}\n{question.Ocr}\n{question.ContainerClass}\n{question.ContainerText}\n{question.HeldByHidden}";

    /// <summary>
    /// Asks the model chosen for vision-based checks about each line the
    /// rules leave in the audit, and gives the audit with the answers
    /// judged. A line the same as one answered before, in this run or on a
    /// task of the project (or, for a recording in no project, of the
    /// recording) by the same model, is given that answer again, copied
    /// into this task (rule 10). Where the task may use no model for
    /// vision-based checks, nothing is asked and nothing is judged (rule 9).
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
        if (audit.TextNotInTree is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        if (context.NoModel(ModelUses.VisionChecks) is { } none)
        {
            return audit.WithTextNotInTree(run.Judged(new Dictionary<PhoneTextNotInTreeQuestion, PhoneTextNotInTreeAnswer>(), NotAsked(run.Questions.Count, none)));
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
        var answers = new Dictionary<PhoneTextNotInTreeQuestion, PhoneTextNotInTreeAnswer>();
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
            note += string.Create(CultureInfo.CurrentCulture, $"; {asker.Again:N0} {(asker.Again == 1 ? "answer was" : "answers were")} the one first given for the same line");
        }

        if (requests.Count < run.Questions.Count)
        {
            note += string.Create(CultureInfo.CurrentCulture, $"; {Count(run.Questions.Count - requests.Count)} not asked as the picture could not be read");
        }

        if (failures.Count > 0)
        {
            note += "; not answered: " + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return audit.WithTextNotInTree(run.Judged(answers, note));
    }

    /// <summary>The note of questions not asked, as no model may be used for vision-based checks (rule 9).</summary>
    public static string NotAsked(int questions, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions)} left by the rules {(questions == 1 ? "was" : "were")} not judged, as no model may be used for vision-based checks: {reason}");

    // What became of the questions: how many were answered, and what the
    // answers made of them.
    private static string Note(PhoneTextNotInTreeRun judged, string start)
    {
        var findings = judged.Findings.Count;
        return start + string.Create(CultureInfo.CurrentCulture,
            $"; {Count(judged.Answers.Count)} answered: {judged.Covered:N0} judged covered and {judged.NotText:N0} judged not text, not confirmed, and {findings:N0} {(findings == 1 ? "warning" : "warnings")}");
    }

    /// <summary>What the answers make of the line (rule 9), the deciding model's.</summary>
    internal static PhoneTextNotInTreeAnswer Judge(QuestionAnswers answers, PhoneTextNotInTreeQuestion question, bool again)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(question);
        var deciding = answers.Escalated ?? answers.First;
        var agreed = deciding.One.Answer == deciding.Two.Answer ? deciding.One.Answer : null;
        var outcome = agreed switch
        {
            Covered => PhoneTextNotInTreeOutcome.Covered,
            NotText => PhoneTextNotInTreeOutcome.NotText,
            _ => PhoneTextNotInTreeOutcome.Finding,
        };
        var inference = CheckQuestions.Judge(answers, group => Interpret(group.One, group.Two, group.Model, group.Cloud, question, again));
        return new PhoneTextNotInTreeAnswer(outcome, inference);
    }

    /// <summary>
    /// What two answers make of a line (rule 9): agreed that it is drawn
    /// text, a warning for a person to verify; agreed that it is covered or
    /// not text, information, not a finding; agreed that the pictures do
    /// not tell, or not agreed, a warning to check manually. Where the
    /// answers were first given for the same line elsewhere, it says so.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud, PhoneTextNotInTreeQuestion question, bool again = false)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(question);
        var who = $"{model}, {(cloud ? "a cloud model" : "a local model")},";
        var twice = ReferenceEquals(first, second) ? "" : " twice";
        var where = again ? " for the same line, by its pixels, its text and item and what a screen reader says on the screen," : "";
        var line = $"the text \"{question.Ocr}\"";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice{where} whether {line} is drawn where a screen reader cannot reach it and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            DrawnText => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} inferred from the pictures{twice}{where} that {line} is shown to users where a screen reader cannot reach it: \"{first.Reason}\"; a person should verify this",
                Inferred: true, model, cloud),
            Covered => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice}{where} that {line} is behind a dialog, sheet or dimmed layer, or covered, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            NotText => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice}{where} that {line} is not text, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell from the pictures{twice}{where} whether {line} is drawn where a screen reader cannot reach it: \"{first.Reason}\"; check manually",
                Inferred: true, model, cloud),
        };
    }

    /// <summary>
    /// What kept answers make of a run, for the Checks tab: the first
    /// answers the project's model for vision-based checks gave for the
    /// same line as each request (rule 10), with the model for complex
    /// questions' where the first do not settle a question. A question
    /// with none is not judged, and the note counts it.
    /// </summary>
    public static PhoneTextNotInTreeRun FromKept(
        PhoneTextNotInTreeRun run,
        IReadOnlyList<(PhoneTextNotInTreeQuestion Question, DescribedImageRequest Request)> requests,
        IReadOnlyList<SignedAnswer> signed,
        string model,
        IReadOnlyList<SignedAnswer>? complexSigned = null,
        string? complexModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(model);
        var answers = new Dictionary<PhoneTextNotInTreeQuestion, PhoneTextNotInTreeAnswer>();
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
                $"; {questions:N0} {(questions == 1 ? "line" : "lines")} asked again of the model for complex questions");

    private static string Count(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "line" : "lines")}");
}
