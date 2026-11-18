using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// The question of the check of text marked as a heading but not shown as
/// one for the model chosen for vision-based checks (android-checks.md,
/// step 27, rules 4 to 9): for each marked heading the rules leave
/// (<see cref="PhoneHeadingNotShownRun.Questions"/>), the pictures, the
/// instructions and the fixed answers of the check of text shown as a
/// heading (<see cref="AndroidHeadingLook"/>), with the question saying
/// the text is marked as a heading. It is asked twice, once for a local
/// model at temperature 0. A question the first answers do not settle is
/// asked again of the model for complex questions, and so is one they
/// settle as not a heading, to confirm it (rule 7); that model's answers
/// decide. Each answer is kept by its request, so it is given again only
/// for the same text and pictures (rule 9).
/// </summary>
public static class AndroidHeadingNotShown
{
    public const string Template = "android-heading-not-shown";

    public const int TemplateVersion = 1;

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// question whose picture cannot be read is left out, and not judged.
    /// </summary>
    public static IReadOnlyList<HeadingLookRequest> Requests(PhoneHeadingNotShownRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<HeadingLookRequest>();
        var pixelsOf = new Dictionary<string, PhonePixels?>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in run.Questions)
        {
            var path = question.Picture.AbsolutePath;
            if (!pixelsOf.TryGetValue(path, out var pixels))
            {
                pixelsOf[path] = pixels = load(path);
            }

            if (pixels is null || question.Lines.IsEmpty)
            {
                continue;
            }

            requests.Add(RequestOf(question, pixels));
        }

        return requests;
    }

    /// <summary>A question's request, from its picture's pixels (rule 6).</summary>
    public static HeadingLookRequest RequestOf(PhoneHeadingQuestion question, PhonePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pixels);
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels, question.Lines));
        var band = AndroidImagesOfText.Png(AndroidHeadingLook.Band(pixels, question.Lines));
        var input = AndroidHeadingLook.Question(question.App, question.ScreenName, question.Text, marked: true);
        var request = new ModelRequest(
            Template,
            TemplateVersion,
            AndroidHeadingLook.Instructions,
            input,
            [new ModelImage("A", "image/png", screen), new ModelImage("B", "image/png", band)],
            [],
            new HashSet<string>(StringComparer.Ordinal))
        {
            Use = ModelUses.VisionChecks,
            Answers = AndroidHeadingLook.Answers,
            ReusedAcrossTasks = true,
        };

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{Template}\n{TemplateVersion.ToString(CultureInfo.InvariantCulture)}\n{AndroidHeadingLook.Instructions}\n{input}\n"));
        hash.AppendData(screen);
        hash.AppendData(band);
        var key = "heading-not-shown-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
        return new HeadingLookRequest(question, request, key);
    }

    /// <summary>
    /// Whether the first answers are to be put to the model for complex
    /// questions (rule 7): they do not settle the question, or they agree
    /// that the text is not shown as a heading, which that model confirms.
    /// </summary>
    public static bool AskAgain(GroupAnswers first)
    {
        ArgumentNullException.ThrowIfNull(first);
        return CheckQuestions.Unsettled(first.One, first.Two, AndroidHeadingLook.CannotTell) || Confirming(first);
    }

    /// <summary>
    /// Asks the model chosen for vision-based checks about each marked
    /// heading the rules leave in the audit, and gives the audit with the
    /// answers judged. Where the task may use no model for vision-based
    /// checks, nothing is asked and nothing is judged (rule 8). A question
    /// whose step fails is not judged, and the note says why; where the
    /// question put to the model for complex questions fails, the first
    /// answers stand.
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
        if (audit.HeadingNotShown is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        if (context.NoModel(ModelUses.VisionChecks) is { } none)
        {
            return audit.WithHeadingNotShown(run.Judged(new Dictionary<PhoneHeadingQuestion, PhoneHeadingNotShownAnswer>(), NotAsked(run.Questions.Count, none)));
        }

        var requests = Requests(run, load);
        var failures = new List<string>();
        var answers = new Dictionary<PhoneHeadingQuestion, PhoneHeadingNotShownAnswer>();
        var escalated = 0;
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await AskQuestionAsync(context, request.StepKey, request.Request, failures, cancellationToken).ConfigureAwait(false) is { } asked)
            {
                answers[request.Question] = Judge(asked);
                escalated += asked.Escalated is null ? 0 : 1;
            }
        }

        var judged = run.Judged(answers, null);
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "was" : "were")} for the model for vision-based checks")
            + AskedAgain(escalated);
        if (requests.Count < run.Questions.Count)
        {
            note += string.Create(CultureInfo.CurrentCulture, $"; {Count(run.Questions.Count - requests.Count)} not asked as the picture could not be read");
        }

        if (failures.Count > 0)
        {
            note += "; not answered: " + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return audit.WithHeadingNotShown(run.Judged(answers, note));
    }

    /// <summary>
    /// Asks one question (rule 7): twice of the model for vision-based
    /// checks, then, as runs 3 and 4, of the model for complex questions
    /// where there is another and the first answers do not settle it or
    /// agree that the text is not shown as a heading. Null where the first
    /// answers could not be had; where the question put to the model for
    /// complex questions fails, the first answers stand. Each failure is
    /// added to the failures.
    /// </summary>
    public static async Task<QuestionAnswers?> AskQuestionAsync(
        AnalysisTaskContext context,
        Func<int, string> stepKey,
        ModelRequest request,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (await CheckQuestions.AskGroupAsync(context, stepKey, request, 1, failures, cancellationToken).ConfigureAwait(false) is not { } first)
        {
            return null;
        }

        GroupAnswers? again = null;
        if (AskAgain(first) && CheckQuestions.EscalationProfile(context, request.Use) is not null)
        {
            again = await CheckQuestions.AskGroupAsync(
                context, stepKey, request with { Use = ModelUses.Complex(request.Use) }, CheckQuestions.EscalatedRun, failures, cancellationToken).ConfigureAwait(false);
        }

        return new QuestionAnswers(first, again);
    }

    /// <summary>The note of questions not asked, as no model may be used for vision-based checks (rule 8).</summary>
    public static string NotAsked(int questions, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions)} left by the rules {(questions == 1 ? "was" : "were")} not judged, as no model may be used for vision-based checks: {reason}");

    /// <summary>
    /// What the answers make of a marked heading (rule 7), the deciding
    /// model's: agreed a heading, counted, no finding; otherwise a warning.
    /// </summary>
    public static PhoneHeadingNotShownAnswer Judge(QuestionAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var deciding = answers.Escalated ?? answers.First;
        var shown = deciding.One.Answer == deciding.Two.Answer && deciding.One.Answer == AndroidHeadingLook.Heading;
        var inference = answers.Escalated is { } escalated
            ? Escalated(Interpret(escalated.One, escalated.Two, escalated.Model, escalated.Cloud), answers.First)
            : Interpret(answers.First.One, answers.First.Two, answers.First.Model, answers.First.Cloud);
        return new PhoneHeadingNotShownAnswer(shown ? PhoneHeadingNotShownOutcome.Shown : PhoneHeadingNotShownOutcome.Finding, inference);
    }

    /// <summary>
    /// What two answers make of a marked heading (rule 7): agreed that it
    /// is not shown as a heading, a warning for a person to verify, with
    /// the model's reason; agreed that it is, information, not a finding;
    /// agreed that the pictures do not tell, or not agreed, a warning to
    /// check manually.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        var who = $"{model}, {(cloud ? "a cloud model" : "a local model")},";
        var twice = ReferenceEquals(first, second) ? "" : " twice";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice whether the text marked as a heading is shown as one and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            AndroidHeadingLook.NotHeading => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} inferred from the pictures{twice} that the text marked as a heading is not shown as one: \"{first.Reason}\"; a person should verify this",
                Inferred: true, model, cloud),
            AndroidHeadingLook.Heading => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice} that the text marked as a heading is shown as one, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell from the pictures{twice} whether the text marked as a heading is shown as one: \"{first.Reason}\"; check manually",
                Inferred: true, model, cloud),
        };
    }

    /// <summary>
    /// What kept answers make of a run, for the Checks tab: the answers the
    /// project's model for vision-based checks gave to the run's requests,
    /// by their step keys, with the model for complex questions' where the
    /// first are to be put to it (rule 7). A request with no answer to its
    /// first run is not judged; one waiting for the model for complex
    /// questions is not judged either; the note counts both.
    /// </summary>
    public static PhoneHeadingNotShownRun FromKept(
        PhoneHeadingNotShownRun run,
        IReadOnlyList<HeadingLookRequest> requests,
        IReadOnlyDictionary<string, KeptAnswer> kept,
        string model,
        IReadOnlyDictionary<string, KeptAnswer>? complexKept = null,
        string? complexModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(model);
        var answers = new Dictionary<PhoneHeadingQuestion, PhoneHeadingNotShownAnswer>();
        int waiting = 0, askedAgain = 0;
        foreach (var request in requests)
        {
            if (CheckQuestions.FromKept(kept, request.StepKey, 1, model) is not { } first)
            {
                continue;
            }

            GroupAnswers? escalated = null;
            if (complexModel is not null && AskAgain(first))
            {
                if (CheckQuestions.FromKept(complexKept ?? new Dictionary<string, KeptAnswer>(), request.StepKey, CheckQuestions.EscalatedRun, complexModel) is not { } again)
                {
                    waiting++;
                    continue;
                }

                escalated = again;
                askedAgain++;
            }

            answers[request.Question] = Judge(new QuestionAnswers(first, escalated));
        }

        var judged = run.Judged(answers, null);
        var unanswered = run.Questions.Count - answers.Count - waiting;
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "is" : "are")} for {model}")
            + AskedAgain(askedAgain)
            + CheckQuestions.Waiting(waiting, Count(waiting))
            + (unanswered > 0 ? string.Create(CultureInfo.CurrentCulture, $", {Count(unanswered)} not yet asked") : "");
        return run.Judged(answers, note);
    }

    // Whether the first answers agree that the text is not shown as a
    // heading, to be confirmed by the model for complex questions.
    private static bool Confirming(GroupAnswers first) =>
        first.One.Answer == AndroidHeadingLook.NotHeading && first.Two.Answer == AndroidHeadingLook.NotHeading;

    // The model for complex questions' inference, after why it was asked:
    // the first answers did not settle the question, or agreed that the
    // text is not shown as a heading, which it was asked to confirm.
    private static PhoneInference Escalated(PhoneInference inference, GroupAnswers first)
    {
        if (!Confirming(first))
        {
            return CheckQuestions.Escalated(inference, first);
        }

        var who = $"{first.Model}, {(first.Cloud ? "a cloud model" : "a local model")}, the project's model for simple questions,";
        var twice = ReferenceEquals(first.One, first.Two) ? "" : " twice";
        return inference with
        {
            Text = $"{who} answered not-heading{twice}, so the question was put to the project's model for complex questions to confirm; {inference.Text}",
        };
    }

    // What became of the questions: how many were answered, and what the
    // answers made of them.
    private static string Note(PhoneHeadingNotShownRun judged, string start)
    {
        var findings = judged.Findings.Count;
        return start + string.Create(CultureInfo.CurrentCulture,
            $"; {Count(judged.Answers.Count)} answered: {judged.Shown:N0} judged shown as headings, not confirmed, and {findings:N0} {(findings == 1 ? "warning" : "warnings")}");
    }

    private static string AskedAgain(int questions) =>
        questions == 0
            ? ""
            : string.Create(CultureInfo.CurrentCulture,
                $"; {questions:N0} asked again of the model for complex questions, to settle or to confirm the first answers");

    private static string Count(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "marked heading" : "marked headings")}");
}
