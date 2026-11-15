namespace Recorder.Analysis;

/// <summary>
/// The task kinds the worker runs. The recording summary and its vision stage; see
/// docs/architecture/recording-summary.md. The Android report and summary; see
/// docs/architecture/android-report.md, and the project's Android report, projects.md.
/// </summary>
public static class AnalysisTaskKinds
{
    public static IReadOnlyList<IAnalysisTaskKind> All { get; } = 
    [
        new Summary.RecordingSummaryKind(), new Summary.RecordingSummaryVisionKind(),
        new Android.AndroidReportKind(), new Android.AndroidSummaryKind(), new Android.AndroidProjectReportKind(),
    ];
}
