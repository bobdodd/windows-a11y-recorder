using System.Globalization;
using Recorder.Analysis.Android;

namespace Recorder.Analysis;

/// <summary>
/// What a task of the model checks, queued by the Checks window's "Ask the
/// project's model", did between two reads of the queue.
/// </summary>
public enum ModelChecksStage
{
    /// <summary>Queued, and the worker has not yet started it.</summary>
    Waiting,

    /// <summary>The worker started it: the model is being asked.</summary>
    Started,

    /// <summary>It finished, and its answers are kept.</summary>
    Finished,

    /// <summary>It failed.</summary>
    Failed,

    /// <summary>It was cancelled.</summary>
    Cancelled
}

/// <summary>A change of a task of the model checks, for a toast.</summary>
public sealed record ModelChecksChange(Guid TaskId, ModelChecksStage Stage, string? Failure);

/// <summary>
/// The Checks window's toasts for "Ask the project's model" (owner,
/// 2026-11-18: "We need toasts to say when the checking has started and
/// when it is complete"): which changes of the model checks' tasks to tell
/// of, and what each toast says. See docs/architecture/android-checks.md,
/// "Toasts for the model checks".
/// </summary>
public static class ModelChecksProgress
{
    /// <summary>
    /// The changes of the model checks' tasks between two reads of the
    /// recording's tasks, at most one for each task: the one it has now.
    /// None on the first read after a recording opens, as nothing was
    /// asked then. A task new since the last read is told of in its
    /// state now, waiting, started or ended, so a task that started or
    /// ended between two reads is still told of; one already told of in
    /// that state is not told of again.
    /// </summary>
    public static IReadOnlyList<ModelChecksChange> Changes(IReadOnlyList<AnalysisTaskSummary>? previous, IReadOnlyList<AnalysisTaskSummary> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null)
        {
            return [];
        }

        var before = previous.ToDictionary(task => task.TaskId);
        var changes = new List<ModelChecksChange>();
        foreach (var task in current.Where(task => task.Kind == AndroidModelChecksKind.Name).OrderBy(task => task.QueuedUtc))
        {
            var earlier = before.TryGetValue(task.TaskId, out var was) ? was.State : null;
            if (earlier == task.State || StageOf(task.State) is not { } stage)
            {
                continue;
            }

            changes.Add(new ModelChecksChange(task.TaskId, stage, task.Failure));
        }

        return changes;
    }

    /// <summary>
    /// What a toast says of a change. A finished task's toast says how
    /// many of the questions the rules leave are still not judged, once
    /// its answers are shown, so the auditor knows whether to look in the
    /// Summary tab; <paramref name="stillUnanswered"/> is that count, and
    /// is used only for a finished task.
    /// </summary>
    public static string Message(ModelChecksChange change, int stillUnanswered)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change.Stage switch
        {
            ModelChecksStage.Waiting =>
                "Model checks queued. They start when the analysis worker is free; another toast will say when they start.",
            ModelChecksStage.Started =>
                "Model checks started. The project's models are being asked; another toast will say when they finish.",
            ModelChecksStage.Finished when stillUnanswered == 0 =>
                "Model checks complete. The model's answers are shown in the Findings and Summary tabs.",
            ModelChecksStage.Finished => string.Create(
                CultureInfo.CurrentCulture,
                $"Model checks complete. The model's answers are shown in the Findings and Summary tabs. {stillUnanswered:N0} {(stillUnanswered == 1 ? "question is" : "questions are")} still not judged; the Summary tab gives them by check."),
            ModelChecksStage.Failed =>
                $"Model checks failed: {Sentence(change.Failure)} The Analysis window has the task.",
            ModelChecksStage.Cancelled =>
                "Model checks were cancelled.",
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
    }

    // A failure's reason as a sentence, ending in one full stop.
    private static string Sentence(string? failure) =>
        string.IsNullOrWhiteSpace(failure)
            ? "no reason was recorded."
            : failure.Trim().TrimEnd('.') + ".";

    private static ModelChecksStage? StageOf(string state) => state switch
    {
        AnalysisTaskStates.Queued => ModelChecksStage.Waiting,
        AnalysisTaskStates.Running => ModelChecksStage.Started,
        AnalysisTaskStates.Done => ModelChecksStage.Finished,
        AnalysisTaskStates.Failed => ModelChecksStage.Failed,
        AnalysisTaskStates.Cancelled => ModelChecksStage.Cancelled,
        _ => null
    };
}
