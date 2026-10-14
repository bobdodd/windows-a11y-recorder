using System.Text.Json;
using Recorder.Analysis;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

/// <summary>
/// What the player's analysis controls rest on: reading a report back for
/// the reading view, the unread count, what to announce, the worker's line,
/// and the default profiles. See docs/architecture/analysis-runner.md,
/// "Decisions on the player (2026-10-13)".
/// </summary>
public sealed class AnalysisPlayerTests
{
    private const string Report = """
        # Summary of recording 20261013-173155

        Inferred by qwen (Q4_K_M), profile "This PC", 2026-10-13.

        ## The site tested

        ### The site or application

        A shop selling books. [E2] [E3]
        It has a basket. [E3]

        ## Page visits

        - 00:00:08, #content: a skip link to main (rule). [E4]

        > Uses NVDA. [E1]

        ## Evidence

        | Ref | Time | Evidence |
        | --- | --- | --- |
        | E1 | not in the recording | The participant's capabilities |
        | E2 | 00:00:01.000 | Navigation committed, https://shop.test/ |
        | E3 | 01:02:03.456 | The page \| as recorded |
        | E4 | 00:00:08.250 | In-page jump |

        ## How this was made

        - Generation settings: `{"seed": 1}`.
        """;

    private static AnalysisTaskSummary Task(Guid id, string state, DateTime? read = null, string? failure = null) =>
        new(id, Guid.Empty, RecordingSummaryKind.Name, state, new Dictionary<string, string> { ["text"] = "This PC" },
            new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc), state is "done" or "failed" ? DateTime.UtcNow : null, failure, read);

    [Fact]
    public void ReportBlocksKeepTheirTextAndReferences()
    {
        var report = ReportDocument.Parse(Report);

        Assert.Equal("Summary of recording 20261013-173155", report.Title);
        var paragraph = Assert.Single(report.Blocks, block => block.Kind == ReportBlockKind.Paragraph && block.Text.StartsWith("A shop", StringComparison.Ordinal));
        Assert.Equal("A shop selling books. [E2] [E3] It has a basket. [E3]", paragraph.Text);
        Assert.Equal(["E2", "E3"], paragraph.References);
        Assert.Equal(["E4"], report.Blocks.Single(block => block.Kind == ReportBlockKind.ListItem && block.Text.Contains("skip", StringComparison.Ordinal)).References);
        Assert.Equal("Uses NVDA. [E1]", report.Blocks.Single(block => block.Kind == ReportBlockKind.Quote).Text);
        Assert.Equal(3, report.Blocks.Single(block => block is { Kind: ReportBlockKind.Heading, Text: "The site or application" }).Level);
    }

    [Fact]
    public void EvidenceListGivesEachReferencesTime()
    {
        var report = ReportDocument.Parse(Report);

        Assert.Equal(1_000_000_000, report.Evidence["E2"].Nanoseconds);
        Assert.Equal(((1 * 3600 + 2 * 60 + 3) * 1000 + 456) * 1_000_000L, report.Evidence["E3"].Nanoseconds);
        Assert.Equal("The page | as recorded", report.Evidence["E3"].Text);
        Assert.Null(report.Evidence["E1"].Nanoseconds);
    }

    [Fact]
    public void ReferenceButtonsAreLabelledWithTheirTimeToTheSecond()
    {
        var report = ReportDocument.Parse(Report);

        Assert.Equal("E4 00:00:08", report.Label("E4"));
        Assert.Equal("E1", report.Label("E1"));
        Assert.Equal("E9", report.Label("E9"));
    }

    [Fact]
    public void TimeThatIsNotOneIsNull()
    {
        Assert.Null(ReportDocument.ParseTime("unknown"));
        Assert.Null(ReportDocument.ParseTime("1:02:03"));
        Assert.Equal(0, ReportDocument.ParseTime("00:00:00.000"));
    }

    [Fact]
    public void WrittenReportReadsBackWithEveryCitedReferenceTimed()
    {
        var evidence = new FakeSummaryEvidence { Duration = 20_000_000_000 };
        evidence.CommitList.Add(new NavigationCommit(1_500_000_000, "nav-1", "b/frame-1", "https://shop.test/", false, "doc-1", true, false));
        var collation = SummaryCollator.Collate(evidence, new ParticipantCapabilities("Uses NVDA.", DateTime.UtcNow));
        var visit = collation.Visits[0];
        var step = new AcceptedStep(Guid.NewGuid(), "input", JsonDocument.Parse("{}").RootElement.Clone(),
            [new Claim("page", 0, 0, Claim.ClaimKind, "A page. | With a pipe.", ["E2"])]);
        var written = SummaryReport.Write("key", collation, new ParticipantCapabilities("Uses NVDA.", DateTime.UtcNow), [(visit, step)], null, [],
            CollationLimits.Default, DateTime.UtcNow);

        var report = ReportDocument.Parse(written);

        Assert.Equal(1_500_000_000, report.Evidence["E2"].Nanoseconds);
        Assert.Null(report.Evidence["E1"].Nanoseconds);
        var cited = report.Blocks.SelectMany(block => block.References).Distinct();
        Assert.All(cited, reference => Assert.True(report.Evidence.ContainsKey(reference), reference));
    }

    [Fact]
    public void UnreadCountsFinishedAndFailedTasksNotYetOpened()
    {
        var tasks = new[]
        {
            Task(Guid.NewGuid(), AnalysisTaskStates.Done),
            Task(Guid.NewGuid(), AnalysisTaskStates.Failed),
            Task(Guid.NewGuid(), AnalysisTaskStates.Done, read: DateTime.UtcNow),
            Task(Guid.NewGuid(), AnalysisTaskStates.Running),
            Task(Guid.NewGuid(), AnalysisTaskStates.Cancelled)
        };

        Assert.Equal(2, AnalysisPlayer.Unread(tasks));
    }

    [Fact]
    public void ChangesAnnounceTasksThatEndedSinceTheLastRead()
    {
        var finished = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var waiting = Guid.NewGuid();
        var before = new[] { Task(finished, AnalysisTaskStates.Running), Task(failed, AnalysisTaskStates.Queued), Task(waiting, AnalysisTaskStates.Queued) };
        var after = new[] { Task(finished, AnalysisTaskStates.Done), Task(failed, AnalysisTaskStates.Failed, failure: "The model is not loaded."), Task(waiting, AnalysisTaskStates.Queued) };

        var messages = AnalysisPlayer.Changes(before, after);

        Assert.Contains("Recording summary finished. Its result is in the Analysis tab.", messages);
        Assert.Contains("Recording summary failed: The model is not loaded.", messages);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void ResultsWaitingWhenThePlayerOpensAreNotAnnounced()
    {
        Assert.Empty(AnalysisPlayer.Changes(null, [Task(Guid.NewGuid(), AnalysisTaskStates.Done)]));
        Assert.Empty(AnalysisPlayer.Changes([], [Task(Guid.NewGuid(), AnalysisTaskStates.Done)]));
    }

    [Fact]
    public void WorkerLineSaysWhatTheWorkerIsDoing()
    {
        var status = new WorkerStatus("Running recording-summary, step visit-0003, attempt 1, with This PC.", "This PC", [], DateTime.UtcNow, 42);

        Assert.Equal(
            "The analysis worker is running: Running recording-summary, step visit-0003, attempt 1, with This PC. Model loaded: This PC.",
            AnalysisPlayer.WorkerLine(status, running: true));
        Assert.StartsWith("The analysis worker is stopped. Its last status, at ", AnalysisPlayer.WorkerLine(status, running: false), StringComparison.Ordinal);
        Assert.Equal("The analysis worker is stopped.", AnalysisPlayer.WorkerLine(null, running: false));
    }

    [Fact]
    public void SettingADefaultKeepsTheRestOfTheFile()
    {
        var settings = """{"version":1,"maxAttempts":4,"profiles":[{"name":"This PC"}],"defaults":{"image":"Vision"}}""";

        var changed = AnalysisPlayer.WithDefault(settings, ModelCapabilities.Text, "This PC");
        var removed = AnalysisPlayer.WithDefault(changed, ModelCapabilities.Image, null);

        using var document = JsonDocument.Parse(removed);
        Assert.Equal(4, document.RootElement.GetProperty("maxAttempts").GetInt32());
        Assert.Equal("This PC", document.RootElement.GetProperty("profiles")[0].GetProperty("name").GetString());
        Assert.Equal("This PC", document.RootElement.GetProperty("defaults").GetProperty("text").GetString());
        Assert.False(document.RootElement.GetProperty("defaults").TryGetProperty("image", out _));
    }

    [Fact]
    public void EmptySettingsFileReadsWithNoProblems()
    {
        var (settings, problems) = AnalysisSettingsFile.Parse(AnalysisPlayer.EmptySettings);

        Assert.Empty(settings.Profiles);
        Assert.Empty(problems);
    }

    [Fact]
    public void DefaultProfileIsTheNamedOneThatCanDoIt()
    {
        static ModelProfile Profile(string name, params string[] capabilities) =>
            new(name, new Uri("http://127.0.0.1:8080/"), "m", capabilities, new ModelFile("m.gguf", null, null, null), null, GenerationSettings.Default, null);
        var settings = new AnalysisSettings(
            [Profile("Vision", ModelCapabilities.Image), Profile("This PC", ModelCapabilities.Text), Profile("Server", ModelCapabilities.Text)],
            new Dictionary<string, string> { [ModelCapabilities.Text] = "Server", [ModelCapabilities.Image] = "This PC" },
            null,
            3);

        Assert.Equal("Server", AnalysisPlayer.DefaultProfile(settings, ModelCapabilities.Text)?.Name);
        Assert.Equal("Vision", AnalysisPlayer.DefaultProfile(settings, ModelCapabilities.Image)?.Name);
    }
}
