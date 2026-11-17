using System.Globalization;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// The answers one model gave to a check's closed question: two, or one
/// given as both where the model is repeatable, whose model and whether
/// it is a cloud model.
/// </summary>
public sealed record GroupAnswers(ClosedAnswerOf One, ClosedAnswerOf Two, string Model, bool Cloud);

/// <summary>
/// The answers to a check's closed question: those of the model for its
/// group, and, where the question was asked again of the model for
/// complex questions, that model's.
/// </summary>
public sealed record QuestionAnswers(GroupAnswers First, GroupAnswers? Escalated);

/// <summary>
/// Asking a check's closed question of the project's models
/// (projects.md, "Simple and complex questions"; owner, 2026-11-17: "We
/// need to be able to set the model per group of checks"). Each question
/// is simple or complex, by its request's use: the use of its kind of
/// check for a simple one, <see cref="ModelUses.Complex"/> of it for a
/// complex one. A simple question the model for simple questions does not
/// settle, as it gave two answers or agreed that it could not tell, is
/// asked again, as runs 3 and 4, of the model for complex questions where
/// that is another model; that model's answers decide the finding, which
/// says why it was asked.
/// </summary>
public static class CheckQuestions
{
    /// <summary>The first run of the question asked again of the model for complex questions.</summary>
    public const int EscalatedRun = 3;

    /// <summary>Whether the answers do not settle the question: two answers, or agreed that the pictures do not tell.</summary>
    public static bool Unsettled(ClosedAnswerOf one, ClosedAnswerOf two, string cannotTell)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(two);
        return one.Answer != two.Answer || one.Answer == cannotTell;
    }

    /// <summary>
    /// The profile a simple question of a use is asked again of, as the
    /// task was queued: its model for complex questions, or null where
    /// there is none, or it is the model for simple questions, or the use
    /// is already the complex questions'.
    /// </summary>
    public static string? EscalationProfile(AnalysisTaskContext context, string use)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (ModelUses.IsComplex(use) || use is not (ModelUses.TextChecks or ModelUses.VisionChecks))
        {
            return null;
        }

        var complex = ModelUses.Complex(use);
        return context.NoModel(complex) is null && context.ModelFor(complex) is { } name && name != context.ModelFor(use) ? name : null;
    }

    /// <summary>
    /// The profile a simple question is asked again of, from a project's
    /// models as they are now, for the Checks tab: as
    /// <see cref="EscalationProfile(AnalysisTaskContext, string)"/>.
    /// </summary>
    public static string? EscalationProfile(ProjectModels models, string use)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (use is not (ModelUses.TextChecks or ModelUses.VisionChecks))
        {
            return null;
        }

        return models.For(ModelUses.Complex(use)) is { } name && name != models.For(use) ? name : null;
    }

    /// <summary>
    /// Asks a question, and again of the model for complex questions where
    /// a simple one is not settled. Null where the first model's answers
    /// could not be had; each failure is added to the failures. Where the
    /// question asked again fails, the first model's answers stand.
    /// </summary>
    public static async Task<QuestionAnswers?> AskAsync(
        AnalysisTaskContext context,
        Func<int, string> stepKey,
        ModelRequest request,
        string cannotTell,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await AskGroupAsync(context, stepKey, request, 1, failures, cancellationToken).ConfigureAwait(false) is not { } first)
        {
            return null;
        }

        return new QuestionAnswers(first, await EscalateAsync(context, stepKey, request, first, cannotTell, failures, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The model for complex questions' answers to a simple question the
    /// first answers do not settle, asked as runs 3 and 4; null where the
    /// question is settled, there is no other model, or it failed.
    /// </summary>
    public static async Task<GroupAnswers?> EscalateAsync(
        AnalysisTaskContext context,
        Func<int, string> stepKey,
        ModelRequest request,
        GroupAnswers first,
        string cannotTell,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(first);
        if (!Unsettled(first.One, first.Two, cannotTell) || EscalationProfile(context, request.Use) is null)
        {
            return null;
        }

        return await AskGroupAsync(context, stepKey, request with { Use = ModelUses.Complex(request.Use) }, EscalatedRun, failures, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the model for the request's use, as two runs from the first
    /// run given, the second not asked where the model is repeatable, a
    /// local model at temperature 0 (android-vision-checks.md, "Keeping it
    /// repeatable"). Null where an answer could not be had.
    /// </summary>
    public static async Task<GroupAnswers?> AskGroupAsync(
        AnalysisTaskContext context,
        Func<int, string> stepKey,
        ModelRequest request,
        int firstRun,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(stepKey);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(failures);
        if (await TryAskAsync(context, stepKey(firstRun), request, failures, cancellationToken).ConfigureAwait(false) is not { } step)
        {
            return null;
        }

        var attempts = await context.Catalog.AttemptsAsync(context.Task.TaskId, cancellationToken).ConfigureAwait(false);
        var attempt = attempts.LastOrDefault(item => item.StepKey == stepKey(firstRun) && item.Outcome == "accepted");
        var model = attempt?.ProfileName ?? context.ModelFor(request.Use) ?? "the model";
        var cloud = attempt?.ModelSha256Basis == AnalysisWorker.ServiceBasis;
        var repeatable = attempt is not null && AndroidImagesOfText.Repeatable(attempt.ModelSha256Basis, attempt.Parameters);
        var one = ClosedAnswer.Read(step.Output);
        var two = one;
        if (!repeatable)
        {
            var second = await TryAskAsync(context, stepKey(firstRun + 1), request, failures, cancellationToken).ConfigureAwait(false);
            two = second is null ? null : ClosedAnswer.Read(second.Output);
        }

        return one is not null && two is not null ? new GroupAnswers(one, two, model, cloud) : null;
    }

    /// <summary>
    /// A model's kept answers to a question from the first run given, by
    /// step key, as the Checks tab reads them: null where the first is not
    /// kept, or the second is not where the model is not repeatable.
    /// </summary>
    public static GroupAnswers? FromKept(IReadOnlyDictionary<string, KeptAnswer> kept, Func<int, string> stepKey, int firstRun, string model)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(stepKey);
        ArgumentNullException.ThrowIfNull(model);
        if (!kept.TryGetValue(stepKey(firstRun), out var firstKept) || ClosedAnswer.Read(firstKept.Output) is not { } one)
        {
            return null;
        }

        var cloud = firstKept.ModelSha256Basis == AnalysisWorker.ServiceBasis;
        if (AndroidImagesOfText.Repeatable(firstKept.ModelSha256Basis, firstKept.Parameters))
        {
            return new GroupAnswers(one, one, model, cloud);
        }

        return kept.TryGetValue(stepKey(firstRun + 1), out var secondKept) && ClosedAnswer.Read(secondKept.Output) is { } two
            ? new GroupAnswers(one, two, model, cloud)
            : null;
    }

    /// <summary>
    /// What the answers make of a finding: the model for complex
    /// questions' inference where it was asked, saying why, and otherwise
    /// the first model's.
    /// </summary>
    public static PhoneInference Judge(QuestionAnswers answers, Func<GroupAnswers, PhoneInference> interpret)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(interpret);
        return answers.Escalated is { } escalated
            ? Escalated(interpret(escalated), answers.First)
            : interpret(answers.First);
    }

    /// <summary>
    /// The model for complex questions' inference, after why it was asked:
    /// the model for simple questions gave two answers, or could not tell.
    /// </summary>
    public static PhoneInference Escalated(PhoneInference inference, GroupAnswers first)
    {
        ArgumentNullException.ThrowIfNull(inference);
        ArgumentNullException.ThrowIfNull(first);
        var who = $"{first.Model}, {(first.Cloud ? "a cloud model" : "a local model")}, the project's model for simple questions,";
        var why = first.One.Answer != first.Two.Answer
            ? $"{who} gave two answers, {first.One.Answer} and {first.Two.Answer}"
            : ReferenceEquals(first.One, first.Two)
                ? $"{who} could not tell"
                : $"{who} could not tell, twice";
        return inference with { Text = $"{why}, so the question was asked again of the project's model for complex questions; {inference.Text}" };
    }

    /// <summary>The note's words for questions asked again of the model for complex questions, or empty for none.</summary>
    public static string AskedAgain(int questions) =>
        questions == 0
            ? ""
            : string.Create(CultureInfo.CurrentCulture,
                $", {questions:N0} of them asked again of the model for complex questions");

    /// <summary>The note's words for questions not yet answered by the model for complex questions, or empty for none.</summary>
    public static string Waiting(int questions, string counted) =>
        questions == 0 ? "" : $", {counted} waiting for the model for complex questions";

    private static async Task<AcceptedStep?> TryAskAsync(
        AnalysisTaskContext context,
        string stepKey,
        ModelRequest request,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.RunStepAsync(stepKey, request, cancellationToken).ConfigureAwait(false);
        }
        catch (StepFailedException exception)
        {
            failures.Add(exception.Message);
            return null;
        }
    }
}
