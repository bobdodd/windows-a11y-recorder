using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;
using Recorder.Analysis;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

/// <summary>
/// The summary with vision, step 4a: the moments of each visit described
/// from their captured frames, run by the analysis worker after a summary
/// of the same recording. See docs/architecture/recording-summary.md,
/// "Stage 2: the summary with vision".
/// </summary>
public sealed partial class RecordingSummaryTaskTests
{
    private const string WithImages = "This PC, with images";

    [GeneratedRegex(@"The picture: \[(E\d+)\]")]
    private static partial Regex PictureReference();

    private static AnalysisSettings VisionSettings()
    {
        var text = Settings().Profiles[0];
        var images = text with
        {
            Name = WithImages,
            Model = "qwen3.6-35b-a3b-images",
            Capabilities = [ModelCapabilities.Text, ModelCapabilities.Image]
        };
        return Settings() with
        {
            Profiles = [text, images],
            Defaults = new Dictionary<string, string> { [ModelCapabilities.Text] = text.Name, [ModelCapabilities.Image] = WithImages }
        };
    }

    // Answers each request with one statement in each of its sections: a
    // moment's citing its picture, the others the first reference given.
    private static StandInEndpoint VisionEndpoint()
    {
        var endpoint = new StandInEndpoint();
        endpoint.Answer = (_, body, _) =>
        {
            var input = Input(body);
            var picture = PictureReference().Match(input);
            var reference = picture.Success ? picture.Groups[1].Value : Reference().Match(input).Groups[1].Value;
            var sections = body.Contains("\"screen\"", StringComparison.Ordinal) ? RecordingSummaryVisionKind.MomentSections
                : body.Contains("\"journey\"", StringComparison.Ordinal) ? RecordingSummaryVisionKind.VisitSections
                : body.Contains("\"audience\"", StringComparison.Ordinal) ? RecordingSummaryKind.RecordingSections
                : RecordingSummaryKind.VisitSections;
            var output = new JsonObject();
            foreach (var section in sections)
            {
                output[section] = new JsonArray(new JsonArray(new JsonObject
                {
                    ["kind"] = "claim",
                    ["text"] = $"A statement about {section}.",
                    ["evidence"] = new JsonArray(reference)
                }));
            }

            return Task.FromResult(new ModelResponse(output.ToJsonString(), "stop", 300, 80, 400, 1200));
        };
        return endpoint;
    }

    private async Task<(AnalysisCatalog Catalog, Guid Summary, Guid Vision)> QueueBothAsync(bool frames = true)
    {
        WriteRecording(frames);
        var (catalog, recording) = await CatalogAsync();
        await catalog.SaveCapabilitiesAsync(recording, "Uses NVDA with speech and a magnifier at 200%.", Token);
        var summary = await catalog.QueueTaskAsync(
            recording, RecordingSummaryKind.Name, 1, new Dictionary<string, string> { [ModelCapabilities.Text] = "This PC" },
            sessionDirectory: _session, cancellationToken: Token);
        var vision = await catalog.QueueTaskAsync(
            recording, RecordingSummaryVisionKind.Name, 1,
            new Dictionary<string, string> { [ModelCapabilities.Text] = WithImages, [ModelCapabilities.Image] = WithImages },
            afterTaskId: summary, sessionDirectory: _session, cancellationToken: Token);
        return (catalog, summary, vision);
    }

    private static Task RunAsync(AnalysisCatalog catalog, StandInEndpoint endpoint) =>
        new AnalysisWorker(catalog, VisionSettings(), [new RecordingSummaryKind(), new RecordingSummaryVisionKind()], endpoint, options: Fast)
            .RunUntilEmptyAsync(Token);

    private async Task<(string State, string? Failure)> StateAsync(Guid task)
    {
        var row = (await RowsAsync("SELECT state, failure FROM analysis_tasks WHERE task_id = $1", task))[0];
        return ((string)row["state"]!, (string?)row["failure"]);
    }

    [Fact]
    public async Task Vision_describes_each_moment_from_its_frame_then_each_visit_and_the_recording()
    {
        var (catalog, summary, vision) = await QueueBothAsync();
        var endpoint = VisionEndpoint();

        await RunAsync(catalog, endpoint);

        Assert.Equal((AnalysisTaskStates.Done, (string?)null), await StateAsync(summary));
        Assert.Equal((AnalysisTaskStates.Done, (string?)null), await StateAsync(vision));
        var attempts = await RowsAsync(
            "SELECT step_key, outcome, prompt_template, input_references FROM analysis_attempts WHERE task_id = $1 ORDER BY started_utc", vision);
        Assert.Equal(
            ["visit-0001-moment-01", "visit-0001-moment-02", "visit-0001", "visit-0002-moment-01", "visit-0002-moment-02", "visit-0002", "recording"],
            attempts.Select(row => (string)row["step_key"]!));
        Assert.All(attempts, row => Assert.Equal(AttemptOutcomes.Accepted, row["outcome"]));
        Assert.Equal(
            [RecordingSummaryVisionKind.MomentTemplate, RecordingSummaryVisionKind.MomentTemplate, RecordingSummaryVisionKind.VisitTemplate],
            attempts.Take(3).Select(row => (string)row["prompt_template"]!));
        Assert.Equal(RecordingSummaryVisionKind.RecordingTemplate, attempts[^1]["prompt_template"]);

        // Each moment's request carries its frame's picture, labelled with
        // its reference; the visit and recording requests carry none.
        var bodies = endpoint.Bodies.Skip(3).ToList();
        Assert.Equal(7, bodies.Count);
        foreach (var index in new[] { 0, 1, 3, 4 })
        {
            Assert.Contains("data:image/png;base64," + Convert.ToBase64String(FramePicture), bodies[index], StringComparison.Ordinal);
            var picture = PictureReference().Match(Input(bodies[index])).Groups[1].Value;
            Assert.Contains($"Picture {picture}:", bodies[index], StringComparison.Ordinal);
            Assert.Contains(picture, (string[])attempts[index]["input_references"]!);
        }

        Assert.All(new[] { 2, 5, 6 }, index => Assert.DoesNotContain("image_url", bodies[index], StringComparison.Ordinal));
        var first = Input(bodies[0]);
        Assert.Contains("Moment 1 of the visit, at 00:00:02.500. Why the recorder chose it:", first, StringComparison.Ordinal);
        Assert.Contains("No change in the page's layout was recorded during the visit", first, StringComparison.Ordinal);
        var visit = Input(bodies[2]);
        Assert.Contains("The visit as a model described it from the recorder's facts alone. These are inferences, not facts:", visit, StringComparison.Ordinal);
        Assert.Contains("(page, inferred) A statement about page.", visit, StringComparison.Ordinal);
        Assert.Contains("(screen, inferred) A statement about screen.", visit, StringComparison.Ordinal);
        Assert.Contains("The last captured frame before the visit ended.", visit, StringComparison.Ordinal);

        var report = Assert.Single(await catalog.ReportsAsync(vision, Token));
        Assert.Equal(RecordingSummaryVisionKind.FileName, report.FileName);
        var text = await File.ReadAllTextAsync(Path.Combine(_session, report.RelativePath), Token);
        Assert.StartsWith($"# Summary with vision of recording {Path.GetFileName(_session)}", text, StringComparison.Ordinal);
        Assert.Contains($"This adds what captured frames of the screen show to the summary of task {summary:D}", text, StringComparison.Ordinal);
        Assert.Contains("#### The participant's journey", text, StringComparison.Ordinal);
        Assert.Contains("#### The moments", text, StringComparison.Ordinal);
        Assert.Matches(@"Moment 1, 00:00:02: No change in the page's layout .* \[E\d+\] Captured frame \[E\d+\]\.", text);
        Assert.Matches(@"On the screen: A statement about screen\. \[E\d+\]", text);
        Assert.Matches(@"What changed: A statement about change\. \[E\d+\]", text);
        Assert.Contains("- Moment limits: 10 moments for each visit; moments less than 2 s apart merged; settled when the layout has not changed for 1 s, or 5 s after the visit starts; a large change is 50 elements or more at once.", text, StringComparison.Ordinal);
        Assert.Contains($"{RecordingSummaryVisionKind.MomentTemplate} version 1 for each moment", text, StringComparison.Ordinal);
        Assert.Contains("The captured frame at 00:00:02.500: the whole screen, 1920 by 1080 pixels, file frames/desktop/0000000001.png", text, StringComparison.Ordinal);
        var read = ReportDocument.Parse(text);
        Assert.All(read.Blocks.SelectMany(block => block.References).Distinct(),
            reference => Assert.True(read.Evidence.ContainsKey(reference), reference));
    }

    [Fact]
    public async Task Vision_named_after_no_summary_fails_saying_why()
    {
        WriteRecording(frames: true);
        var (catalog, recording) = await CatalogAsync();
        var vision = await catalog.QueueTaskAsync(
            recording, RecordingSummaryVisionKind.Name, 1,
            new Dictionary<string, string> { [ModelCapabilities.Text] = WithImages, [ModelCapabilities.Image] = WithImages },
            sessionDirectory: _session, cancellationToken: Token);

        await RunAsync(catalog, VisionEndpoint());

        var (state, failure) = await StateAsync(vision);
        Assert.Equal(AnalysisTaskStates.Failed, state);
        Assert.Contains("Summarise the recording first", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vision_is_not_added_to_a_summary_written_from_other_facts()
    {
        WriteRecording(frames: true);
        var (catalog, recording) = await CatalogAsync();
        var summary = await catalog.QueueTaskAsync(
            recording, RecordingSummaryKind.Name, 1, new Dictionary<string, string> { [ModelCapabilities.Text] = "This PC" },
            sessionDirectory: _session, cancellationToken: Token);
        await RunAsync(catalog, VisionEndpoint());

        // The summary's first visit is recorded as given other references,
        // as if the recorder's facts had changed since it was written.
        await using (var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token))
        await using (var command = new NpgsqlCommand(
            "UPDATE analysis_attempts SET input_references = ARRAY['E1'] WHERE task_id = $1 AND step_key = 'visit-0001'", connection))
        {
            command.Parameters.AddWithValue(summary);
            Assert.Equal(1, await command.ExecuteNonQueryAsync(Token));
        }

        var vision = await catalog.QueueTaskAsync(
            recording, RecordingSummaryVisionKind.Name, 1,
            new Dictionary<string, string> { [ModelCapabilities.Text] = WithImages, [ModelCapabilities.Image] = WithImages },
            afterTaskId: summary, sessionDirectory: _session, cancellationToken: Token);
        await RunAsync(catalog, VisionEndpoint());

        var (state, failure) = await StateAsync(vision);
        Assert.Equal(AnalysisTaskStates.Failed, state);
        Assert.Contains("The recorder's facts for visit 1 are not the ones its summary was written from", failure, StringComparison.Ordinal);
        Assert.Empty(await RowsAsync("SELECT step_key FROM analysis_attempts WHERE task_id = $1", vision));
    }

    [Fact]
    public async Task Vision_of_a_recording_without_frames_fails_saying_why()
    {
        var (catalog, summary, vision) = await QueueBothAsync(frames: false);

        await RunAsync(catalog, VisionEndpoint());

        Assert.Equal(AnalysisTaskStates.Done, (await StateAsync(summary)).State);
        var (state, failure) = await StateAsync(vision);
        Assert.Equal(AnalysisTaskStates.Failed, state);
        Assert.Equal("No captured frames were recorded during the page visits, so there is nothing to add vision from.", failure);
    }

    [Fact]
    public async Task The_recording_file_gives_the_moments_their_evidence()
    {
        const string context = "\"context\":{\"documentToken\":\"token-a\"}";
        var path = WriteRecording(frames: true, extra:
        [
            (2 * Second, "browser.layout", "layout-changes-completed", "{" + context + ",\"recordedNodeCount\":60}"),
            (3 * Second, "browser.layout", "layout-scroll-offset-changed", "{" + context + ",\"scrollElementId\":\"root\",\"scrollOffset\":{\"x\":0,\"y\":900}}"),
            (4 * Second, "browser.compositor", "compositor-animation-started", "{" + context + ",\"compositorAnimationId\":7}"),
            (5 * Second, "browser.compositor", "compositor-animation-started", "{" + context + ",\"compositorAnimationId\":8}"),
            (6 * Second, "browser.compositor", "compositor-animation-ended", "{" + context + ",\"compositorAnimationId\":7}"),
            (7 * Second, "browser.interaction", "page-popup-closed", "{" + context + "}")
        ]);

        var opened = await Recorder.Database.RecordingFiles.RecordingFilePlayback.OpenAsync(_session, path, Token);
        try
        {
            var evidence = new RecordedSummaryEvidence(opened.Archive, opened.Documents!, path);

            Assert.Equal(15, evidence.Frames.Count);
            Assert.Equal((1_500_000_000L, "frames/desktop/0000000000.png", 1920, 1080), (evidence.Frames[0].Time, evidence.Frames[0].Path, evidence.Frames[0].Width, evidence.Frames[0].Height));
            var layout = Assert.Single(evidence.LayoutChanges);
            Assert.Equal((2 * Second, "token-a", (long?)60), (layout.Time, layout.DocumentToken, layout.ChangedNodes));
            var scroll = Assert.Single(evidence.Scrolls);
            Assert.Equal((3 * Second, "root", 900d), (scroll.Time, scroll.Scroller, scroll.Y));
            Assert.Equal([(4 * Second, (long?)(6 * Second)), (5 * Second, null)], evidence.Animations.Select(item => (item.Start, item.End)));
            Assert.Equal(7 * Second, Assert.Single(evidence.PopupsClosed).Time);
        }
        finally
        {
            (opened.Archive.Timeline as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Two_profiles_served_by_this_PC_need_model_names_of_their_own()
    {
        const string file = """
            {"version": 1, "localServer": {"executable": "C:\\llama\\llama-server.exe", "port": 8091},
             "profiles": [
              {"name": "This PC", "endpoint": "http://127.0.0.1:8091", "model": "qwen", "capabilities": ["text"],
               "local": {"modelPath": "C:\\models\\q.gguf"}},
              {"name": "This PC, with images", "endpoint": "http://127.0.0.1:8091", "model": "qwen", "capabilities": ["text", "image"],
               "local": {"modelPath": "C:\\models\\q.gguf", "imageAddOnPath": "C:\\models\\mmproj.gguf"}}]}
            """;

        var (settings, problems) = AnalysisSettingsFile.Parse(file);

        Assert.Contains(
            "Profiles \"This PC\" and \"This PC, with images\" are both served by this PC as model \"qwen\"; each needs a model name of its own.",
            problems);
        Assert.Single(settings.Profiles);
    }
}
