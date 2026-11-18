using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

/// <summary>
/// The Checks window's toasts for "Ask the project's model": which changes
/// of the model checks' tasks are told of, and what each toast says. See
/// docs/architecture/android-checks.md, "Toasts for the model checks".
/// </summary>
public sealed class ModelChecksProgressTests
{
    private static readonly Guid Id = Guid.Parse("6f0e8d1c-2b4a-4c55-9d1e-0a7b3c9e5f21");

    private static AnalysisTaskSummary Task(string state, string kind = AndroidModelChecksKind.Name, Guid? id = null, string? failure = null) =>
        new(id ?? Id, Guid.Empty, kind, state, new Dictionary<string, string>(),
            new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc), null, failure, null);

    [Fact]
    public void NothingIsToldOnTheFirstRead()
    {
        Assert.Empty(ModelChecksProgress.Changes(null, [Task(AnalysisTaskStates.Running)]));
    }

    [Fact]
    public void ATaskIsToldOfAsItWaitsStartsAndFinishes()
    {
        var waiting = ModelChecksProgress.Changes([], [Task(AnalysisTaskStates.Queued)]);
        var started = ModelChecksProgress.Changes([Task(AnalysisTaskStates.Queued)], [Task(AnalysisTaskStates.Running)]);
        var finished = ModelChecksProgress.Changes([Task(AnalysisTaskStates.Running)], [Task(AnalysisTaskStates.Done)]);

        Assert.Equal(ModelChecksStage.Waiting, Assert.Single(waiting).Stage);
        Assert.Equal(ModelChecksStage.Started, Assert.Single(started).Stage);
        Assert.Equal(ModelChecksStage.Finished, Assert.Single(finished).Stage);
    }

    [Fact]
    public void AStateAlreadyToldOfIsNotToldAgain()
    {
        Assert.Empty(ModelChecksProgress.Changes([Task(AnalysisTaskStates.Running)], [Task(AnalysisTaskStates.Running)]));
    }

    [Fact]
    public void ATaskNewSinceTheLastReadIsToldOfInItsStateNow()
    {
        // Queued, started and perhaps ended between two reads of the queue.
        Assert.Equal(ModelChecksStage.Started, Assert.Single(ModelChecksProgress.Changes([], [Task(AnalysisTaskStates.Running)])).Stage);
        Assert.Equal(ModelChecksStage.Finished, Assert.Single(ModelChecksProgress.Changes([], [Task(AnalysisTaskStates.Done)])).Stage);
    }

    [Fact]
    public void OnlyTheModelChecksAreToldOf()
    {
        var other = Guid.Parse("0c3f2a8e-7d61-4b9a-8e2f-5a1d6c7b8e90");
        var changes = ModelChecksProgress.Changes([], [Task(AnalysisTaskStates.Running, RecordingSummaryKind.Name, other), Task(AnalysisTaskStates.Running)]);

        Assert.Equal(Id, Assert.Single(changes).TaskId);
    }

    [Fact]
    public void AFinishedToastSaysHowManyQuestionsAreStillNotJudged()
    {
        var finished = new ModelChecksChange(Id, ModelChecksStage.Finished, null);

        Assert.Equal(
            "Model checks complete. The model's answers are shown in the Findings and Summary tabs.",
            ModelChecksProgress.Message(finished, 0));
        Assert.EndsWith("1 question is still not judged; the Summary tab gives them by check.", ModelChecksProgress.Message(finished, 1), StringComparison.Ordinal);
        Assert.EndsWith("3 questions are still not judged; the Summary tab gives them by check.", ModelChecksProgress.Message(finished, 3), StringComparison.Ordinal);
    }

    [Fact]
    public void TheStartedToastSaysAnotherWillSayWhenTheyFinish()
    {
        Assert.Equal(
            "Model checks started. The project's models are being asked; another toast will say when they finish.",
            ModelChecksProgress.Message(new ModelChecksChange(Id, ModelChecksStage.Started, null), 0));
    }

    [Fact]
    public void AFailedToastGivesTheReasonAsOneSentence()
    {
        Assert.Equal(
            "Model checks failed: The model server did not answer. The Analysis window has the task.",
            ModelChecksProgress.Message(new ModelChecksChange(Id, ModelChecksStage.Failed, "The model server did not answer."), 0));
        Assert.Equal(
            "Model checks failed: no reason was recorded. The Analysis window has the task.",
            ModelChecksProgress.Message(new ModelChecksChange(Id, ModelChecksStage.Failed, null), 0));
    }
}
