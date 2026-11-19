using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>One question about a control's name as it is sent: the question, its request, and its step key for each run.</summary>
public sealed record ControlNameRequest(PhoneControlNameQuestion Question, ModelRequest Request, string Key)
{
    /// <summary>The step key of a run, 1 to 4.</summary>
    public string StepKey(int run) => $"{Key}-{run.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The question of the check of a control name that does not say what the
/// control does for the model chosen for vision-based checks
/// (android-checks.md, step 28, rules 4 to 7): for each name the rules
/// leave (<see cref="PhoneControlNameRun.Questions"/>), once for each
/// screen, the whole screen with the control outlined and a close-up of
/// it, enlarged when it is small, as the icon check gives them
/// (<see cref="AndroidDescribedImages"/>); the control's name, class and
/// state, the text OCR read inside it, and what a screen reader says on
/// the screen with the control's place marked; and one of three fixed
/// answers, reason first. It is asked twice, once for a local model at
/// temperature 0. A question the first answers do not settle is asked
/// again of the model for complex questions, and so is one they settle as
/// does-not-describe, to confirm it (rule 7); that model's answers decide.
/// Each answer is kept by its request, so it is given again only for the
/// same name, text and pictures.
/// </summary>
public static class AndroidControlNames
{
    public const string Template = "android-control-name";

    public const int TemplateVersion = 1;

    public const string Describes = "describes";

    public const string DoesNotDescribe = "does-not-describe";

    public const string CannotTell = "cannot-tell";

    public static readonly IReadOnlyList<string> Answers = [Describes, DoesNotDescribe, CannotTell];

    public const string Instructions =
        "You judge one control in an Android app for WCAG 2.2 success criteria 1.1.1, Non-text Content, and 2.4.6, Headings and Labels: " +
        "whether its name says what the control does. " +
        "Answer only from what the pictures show and what a screen reader says, as given, with one of the fixed answers.";

    /// <summary>The question's text (rule 6).</summary>
    public static string Question(PhoneControlNameQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var state = question.State is null ? "" : $", and its state is {question.State}";
        var shown = question.ShownText.Length > 0
            ? $"The text OCR read inside the outlined control: \"{question.ShownText}\".\n"
            : "OCR read no text inside the outlined control.\n";
        return $"Picture A is the whole screen of the Android app {question.App}, on its screen \"{question.ScreenName}\", with one control outlined in cyan. " +
            "Picture B is the outlined control and a little of the screen around it, enlarged when the control is small.\n" +
            $"The outlined control's name, which a screen reader says for it, is \"{question.Name}\". Its class is {question.Role}{state}.\n" +
            shown +
            $"What a screen reader says on this screen, item by item in the app's order, with {PhoneControlNameRun.ControlMark} at the control's place: \"{question.ScreenText}\".\n" +
            $"Does the name, \"{question.Name}\", say what the control does?\n" +
            "- describes: the name says what the control does or opens, such as \"Navigate up\" or \"Decrease font size\", or names the item, app, option or value it stands for, so a person who hears it on this screen knows what the control does.\n" +
            "- does-not-describe: a person who hears the name would not know what the control does, such as a name that is only a number or a count, a symbol, a developer's word, or a word that could mean several things on this screen.\n" +
            "- cannot-tell: the pictures and what a screen reader says do not show enough to judge.\n" +
            "First write one sentence saying what in the pictures shows the answer, then give the answer.";
    }

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// question whose picture cannot be read, or whose control is not on
    /// it, is left out, and not judged.
    /// </summary>
    public static IReadOnlyList<ControlNameRequest> Requests(PhoneControlNameRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<ControlNameRequest>();
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

            requests.Add(RequestOf(question, pixels));
        }

        return requests;
    }

    /// <summary>A question's request, from its picture's pixels (rule 6).</summary>
    public static ControlNameRequest RequestOf(PhoneControlNameQuestion question, PhonePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pixels);
        var box = AndroidImageAlternative.Clamp(question.Bounds, pixels) ?? throw new ArgumentException("The control is not on the picture.", nameof(question));
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels, box));
        var closeUp = AndroidImagesOfText.Png(AndroidImageAlternative.CloseUp(pixels, box));
        var input = Question(question);
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
        };

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{Template}\n{TemplateVersion.ToString(CultureInfo.InvariantCulture)}\n{Instructions}\n{input}\n"));
        hash.AppendData(screen);
        hash.AppendData(closeUp);
        return new ControlNameRequest(question, request, "control-name-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16]);
    }

    /// <summary>
    /// Whether the first answers are to be put to the model for complex
    /// questions (rule 7): they do not settle the question, or they agree
    /// that the name does not say what the control does, which that model
    /// confirms.
    /// </summary>
    public static bool AskAgain(GroupAnswers first)
    {
        ArgumentNullException.ThrowIfNull(first);
        return CheckQuestions.Unsettled(first.One, first.Two, CannotTell) || Confirming(first);
    }

    /// <summary>
    /// The audit with no model asked about the names, images or headings
    /// the rules leave, and the reason why: what <see cref="AskAsync"/>
    /// gives where the task may use no model for vision-based checks, and
    /// what the fixture tests run (android-fixtures.md, "Rule tests").
    /// </summary>
    public static AndroidAudit Unasked(AndroidAudit audit, string reason)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(reason);
        return audit.ControlNames is { } run && run.Questions.Count > 0
            ? audit.WithControlNames(run.Judged(new Dictionary<PhoneControlNameQuestion, PhoneControlNameAnswer>(), NotAsked(run.Questions.Count, reason)))
            : audit;
    }

    /// <summary>
    /// Asks the model chosen for vision-based checks about each name the
    /// rules leave in the audit, and gives the audit with the answers
    /// judged. Where the task may use no model for vision-based checks,
    /// nothing is asked, and only the placeholder warnings stand. A
    /// question whose step fails is not judged, and the note says why;
    /// where the question put to the model for complex questions fails,
    /// the first answers stand.
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
        if (audit.ControlNames is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        if (context.NoModel(ModelUses.VisionChecks) is { } none)
        {
            return Unasked(audit, none);
        }

        var requests = Requests(run, load);
        var failures = new List<string>();
        var answers = new Dictionary<PhoneControlNameQuestion, PhoneControlNameAnswer>();
        var escalated = 0;
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await AskQuestionAsync(context, request.StepKey, request.Request, failures, cancellationToken).ConfigureAwait(false) is { } asked)
            {
                answers[request.Question] = Judge(asked, request.Question.Name);
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

        return audit.WithControlNames(run.Judged(answers, note));
    }

    /// <summary>
    /// Asks one question (rule 7): twice of the model for vision-based
    /// checks, then, as runs 3 and 4, of the model for complex questions
    /// where there is another and the first answers do not settle it or
    /// agree that the name does not say what the control does. Null where
    /// the first answers could not be had; where the question put to the
    /// model for complex questions fails, the first answers stand. Each
    /// failure is added to the failures.
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

    /// <summary>The note of questions not asked, as no model may be used for vision-based checks.</summary>
    public static string NotAsked(int questions, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions)} left by the rules {(questions == 1 ? "was" : "were")} not judged, as no model may be used for vision-based checks: {reason}");

    /// <summary>
    /// What the answers make of a control's name (rule 7), the deciding
    /// model's: agreed describes, counted, no finding; otherwise a warning.
    /// </summary>
    public static PhoneControlNameAnswer Judge(QuestionAnswers answers, string name)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(name);
        var deciding = answers.Escalated ?? answers.First;
        var describes = deciding.One.Answer == deciding.Two.Answer && deciding.One.Answer == Describes;
        var inference = answers.Escalated is { } escalated
            ? Escalated(Interpret(escalated.One, escalated.Two, escalated.Model, escalated.Cloud, name), answers.First)
            : Interpret(answers.First.One, answers.First.Two, answers.First.Model, answers.First.Cloud, name);
        return new PhoneControlNameAnswer(describes ? PhoneControlNameOutcome.Describes : PhoneControlNameOutcome.Finding, inference);
    }

    /// <summary>
    /// What two answers make of a control's name (rule 7): agreed that it
    /// does not say what the control does, a warning for a person to
    /// verify, with the model's reason; agreed that it does, information,
    /// not a finding; agreed that the pictures do not tell, or not agreed,
    /// a warning to check manually.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud, string name)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(name);
        var who = $"{model}, {(cloud ? "a cloud model" : "a local model")},";
        var twice = ReferenceEquals(first, second) ? "" : " twice";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice whether the control's name, \"{name}\", says what the control does and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            DoesNotDescribe => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} inferred from the pictures{twice} that the control's name, \"{name}\", does not say what the control does: \"{first.Reason}\"; a person should verify this",
                Inferred: true, model, cloud),
            Describes => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice} that the control's name, \"{name}\", says what the control does, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell from the pictures{twice} whether the control's name, \"{name}\", says what the control does: \"{first.Reason}\"; check manually",
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
    public static PhoneControlNameRun FromKept(
        PhoneControlNameRun run,
        IReadOnlyList<ControlNameRequest> requests,
        IReadOnlyDictionary<string, KeptAnswer> kept,
        string model,
        IReadOnlyDictionary<string, KeptAnswer>? complexKept = null,
        string? complexModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(model);
        var answers = new Dictionary<PhoneControlNameQuestion, PhoneControlNameAnswer>();
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

            answers[request.Question] = Judge(new QuestionAnswers(first, escalated), request.Question.Name);
        }

        var judged = run.Judged(answers, null);
        var unanswered = run.Questions.Count - answers.Count - waiting;
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "is" : "are")} for {model}")
            + AskedAgain(askedAgain)
            + CheckQuestions.Waiting(waiting, Count(waiting))
            + (unanswered > 0 ? string.Create(CultureInfo.CurrentCulture, $", {Count(unanswered)} not yet asked") : "");
        return run.Judged(answers, note);
    }

    // Whether the first answers agree that the name does not say what the
    // control does, to be confirmed by the model for complex questions.
    private static bool Confirming(GroupAnswers first) =>
        first.One.Answer == DoesNotDescribe && first.Two.Answer == DoesNotDescribe;

    // The model for complex questions' inference, after why it was asked:
    // the first answers did not settle the question, or agreed that the
    // name does not say what the control does, which it was asked to
    // confirm.
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
            Text = $"{who} answered does-not-describe{twice}, so the question was put to the project's model for complex questions to confirm; {inference.Text}",
        };
    }

    // What became of the questions: how many were answered, and what the
    // answers made of them, with the warnings for each visit.
    private static string Note(PhoneControlNameRun judged, string start)
    {
        var findings = judged.Findings.Count - judged.PlaceholderFindings.Count;
        return start + string.Create(CultureInfo.CurrentCulture,
            $"; {Count(judged.Answers.Count)} answered: {judged.Described:N0} judged to say what the control does, not confirmed, and {findings:N0} {(findings == 1 ? "warning" : "warnings")} on the visits with the name");
    }

    private static string AskedAgain(int questions) =>
        questions == 0
            ? ""
            : string.Create(CultureInfo.CurrentCulture,
                $"; {questions:N0} asked again of the model for complex questions, to settle or to confirm the first answers");

    private static string Count(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "control name" : "control names")}");
}
