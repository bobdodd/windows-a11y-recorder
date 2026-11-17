using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// Asks one template's questions, each answer kept against its
/// signature: an image the same as one answered earlier in the run is
/// given that answer; one answered on an earlier task is given its
/// first answer, copied into this task; any other is asked, and asked
/// again of the model for complex questions where it is not settled.
/// The icon and description checks' (step 21) and the screen title
/// check's (step 22); "the same image" is the same signature, which for a
/// question with no picture is the same texts.
/// </summary>
internal sealed class SignedAsker(
    AnalysisTaskContext context,
    string? profileName,
    IReadOnlyList<SignedAnswer> signed,
    string? complexName,
    IReadOnlyList<SignedAnswer> signedComplex,
    HashSet<string> held,
    HashSet<Guid> copied,
    List<string> failures,
    string cannotTell = AndroidDescribedImages.CannotTell)
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
        if (complexName is not null && CheckQuestions.Unsettled(first.One, first.Two, cannotTell))
        {
            if (AndroidImageAlternative.Kept(signedComplex, request.Signature, CheckQuestions.EscalatedRun) is { } keptComplex)
            {
                await CopyAsync(keptComplex.Sources, cancellationToken).ConfigureAwait(false);
                second = new GroupAnswers(keptComplex.One, keptComplex.Two, complexName, keptComplex.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
            }
            else
            {
                second = await CheckQuestions.EscalateAsync(context, request.StepKey, request.Request, first, cannotTell, failures, cancellationToken).ConfigureAwait(false);
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
