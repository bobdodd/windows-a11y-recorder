namespace Recorder.Analysis;

/// <summary>
/// The task kinds the worker runs. The recording summary and its vision stage; see
/// docs/architecture/recording-summary.md.
/// </summary>
public static class AnalysisTaskKinds
{
    public static IReadOnlyList<IAnalysisTaskKind> All { get; } = [new Summary.RecordingSummaryKind(), new Summary.RecordingSummaryVisionKind()];
}
