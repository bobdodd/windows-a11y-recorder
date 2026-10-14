namespace Recorder.Analysis;

/// <summary>
/// The task kinds the worker runs. The first, the recording summary, is
/// added in step 2 of the build order in docs/architecture/analysis-runner.md.
/// </summary>
public static class AnalysisTaskKinds
{
    public static IReadOnlyList<IAnalysisTaskKind> All { get; } = [];
}
