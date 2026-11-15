using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

// The setting "Use AI in analysis" (owner, 2026-11-15; analysis-runner.md,
// "Turning AI analysis off"). The worker's refusal is in AnalysisWorkerTests.
public sealed class AiAnalysisSettingTests
{
    [Fact]
    public void AI_analysis_is_on_by_default_and_read_and_written_as_aiAnalysis()
    {
        Assert.True(AnalysisSettings.Empty.AiAnalysis);
        Assert.True(AnalysisSettingsFile.Parse("""{ "version": 1 }""").Settings.AiAnalysis);
        Assert.False(AnalysisSettingsFile.Parse("""{ "version": 1, "aiAnalysis": false }""").Settings.AiAnalysis);
        var (settings, problems) = AnalysisSettingsFile.Parse("""{ "version": 1, "aiAnalysis": "no" }""");
        Assert.True(settings.AiAnalysis);
        Assert.Equal(["\"aiAnalysis\" is not true or false, so AI analysis is on, the default."], problems);
        var written = AnalysisSettingsFile.WithAiAnalysis("""{ "version": 1, "maxAttempts": 2, "reportBrowserOnly": false }""", false)!;
        var read = AnalysisSettingsFile.Parse(written).Settings;
        Assert.Equal((false, 2, false), (read.AiAnalysis, read.MaxAttempts, read.BrowserOnly));
        Assert.True(AnalysisSettingsFile.Parse(AnalysisSettingsFile.WithAiAnalysis(null, true)!).Settings.AiAnalysis);
        Assert.Null(AnalysisSettingsFile.WithAiAnalysis("[1]", false));
    }

    [Fact]
    public void Only_the_summaries_need_a_model()
    {
        Assert.True(AnalysisPlayer.NeedsModel(RecordingSummaryKind.Name));
        Assert.True(AnalysisPlayer.NeedsModel(RecordingSummaryVisionKind.Name));
        Assert.True(AnalysisPlayer.NeedsModel(AndroidSummaryKind.Name));
        Assert.False(AnalysisPlayer.NeedsModel(AndroidReportKind.Name));
        Assert.False(AnalysisPlayer.NeedsModel(AndroidProjectReportKind.Name));
    }
}
