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
            var sections = body.Contains("combining two descriptions", StringComparison.Ordinal) ? RecordingSummaryVisionKind.CombinedSections
                : body.Contains("You describe part of a web page", StringComparison.Ordinal) ? RecordingSummaryVisionKind.PageSections
                : body.Contains("\"screen\"", StringComparison.Ordinal) ? RecordingSummaryVisionKind.MomentSections
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

    private static readonly int VisionVersion = new RecordingSummaryVisionKind().Version;

    // Recreates the first visit's pages in five tiles of 922 by 925
    // pixels, the size measured on the target machine, each the frame's
    // picture; the second visit's pages are not recreated.
    private sealed class StandInPages : IMomentPages
    {
        public List<(int Visit, int Moment, string Prefix)> Asked { get; } = [];

        public async Task<RecreatedPage> RecreateAsync(Visit visit, Moment moment, string folder, string relativeFolder, string prefix, CancellationToken cancellationToken)
        {
            Asked.Add((visit.Number, moment.Number, prefix));
            if (visit.Number != 1)
            {
                return RecreatedPage.Not("the visit's page is not among the pages recorded at the frame");
            }

            Directory.CreateDirectory(folder);
            var tiles = new List<PageTile>();
            foreach (var (top, height) in RecordedMomentPages.Tiles(4625, 925))
            {
                var name = $"{prefix}-tile-{tiles.Count + 1:D2}.png";
                await File.WriteAllBytesAsync(Path.Combine(folder, name), FramePicture, cancellationToken);
                tiles.Add(new PageTile(tiles.Count + 1, top, height, 922, relativeFolder + "/" + name, Path.Combine(folder, name), 922, 925));
            }

            return new RecreatedPage(tiles, 922, 4625, 0, 1850, 922, 925, 15, ["https://example.com/blogs.jpg"], 4, true, null);
        }
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
            recording, RecordingSummaryVisionKind.Name, VisionVersion,
            new Dictionary<string, string> { [ModelCapabilities.Text] = WithImages, [ModelCapabilities.Image] = WithImages },
            afterTaskId: summary, sessionDirectory: _session, cancellationToken: Token);
        return (catalog, summary, vision);
    }

    private static Task RunAsync(AnalysisCatalog catalog, StandInEndpoint endpoint, StandInPages? pages = null)
    {
        var standIn = pages ?? new StandInPages();
        return new AnalysisWorker(catalog, VisionSettings(), [new RecordingSummaryKind(), new RecordingSummaryVisionKind { Pages = (_, _) => standIn }], endpoint, options: Fast)
            .RunUntilEmptyAsync(Token);
    }

    private async Task<(string State, string? Failure)> StateAsync(Guid task)
    {
        var row = (await RowsAsync("SELECT state, failure FROM analysis_tasks WHERE task_id = $1", task))[0];
        return ((string)row["state"]!, (string?)row["failure"]);
    }

    [Fact]
    public async Task Vision_describes_each_moment_from_its_frame_and_its_recreated_page_then_each_visit_and_the_recording()
    {
        var (catalog, summary, vision) = await QueueBothAsync();
        var endpoint = VisionEndpoint();
        var pages = new StandInPages();

        await RunAsync(catalog, endpoint, pages);

        Assert.Equal((AnalysisTaskStates.Done, (string?)null), await StateAsync(summary));
        Assert.Equal((AnalysisTaskStates.Done, (string?)null), await StateAsync(vision));
        Assert.Equal(
            [(1, 1, "visit-0001-moment-01"), (1, 2, "visit-0001-moment-02"), (2, 1, "visit-0002-moment-01"), (2, 2, "visit-0002-moment-02")],
            pages.Asked);
        var attempts = await RowsAsync(
            "SELECT step_key, outcome, prompt_template, input_references FROM analysis_attempts WHERE task_id = $1 ORDER BY started_utc", vision);
        Assert.Equal(
            [
                "visit-0001-moment-01", "visit-0001-moment-01-page-01", "visit-0001-moment-01-page-02", "visit-0001-moment-01-described",
                "visit-0001-moment-02", "visit-0001-moment-02-page-01", "visit-0001-moment-02-page-02", "visit-0001-moment-02-described",
                "visit-0001", "visit-0002-moment-01", "visit-0002-moment-02", "visit-0002", "recording"
            ],
            attempts.Select(row => (string)row["step_key"]!));
        Assert.All(attempts, row => Assert.Equal(AttemptOutcomes.Accepted, row["outcome"]));
        Assert.Equal(
            [
                RecordingSummaryVisionKind.MomentTemplate, RecordingSummaryVisionKind.PageTemplate, RecordingSummaryVisionKind.PageTemplate,
                RecordingSummaryVisionKind.CombinedTemplate, RecordingSummaryVisionKind.MomentTemplate
            ],
            attempts.Take(5).Select(row => (string)row["prompt_template"]!));
        Assert.Equal(RecordingSummaryVisionKind.VisitTemplate, attempts[8]["prompt_template"]);
        Assert.Equal(RecordingSummaryVisionKind.RecordingTemplate, attempts[^1]["prompt_template"]);

        // Each frame's request carries its picture, and each group of tiles
        // as many tiles of 841 image tokens as fit 4,096: four, then one.
        var bodies = endpoint.Bodies.Skip(3).ToList();
        Assert.Equal(13, bodies.Count);
        var picture = "data:image/png;base64," + Convert.ToBase64String(FramePicture);
        int Pictures(string body) => (body.Length - body.Replace(picture, "", StringComparison.Ordinal).Length) / picture.Length;
        Assert.Equal([1, 4, 1, 0, 1, 4, 1, 0, 0, 1, 1, 0, 0], bodies.Select(Pictures));
        foreach (var index in new[] { 0, 4, 9, 10 })
        {
            var reference = PictureReference().Match(Input(bodies[index])).Groups[1].Value;
            Assert.Contains($"Picture {reference}:", bodies[index], StringComparison.Ordinal);
            Assert.Contains(reference, (string[])attempts[index]["input_references"]!);
        }

        var first = Input(bodies[0]);
        Assert.Contains("Moment 1 of the visit, at 00:00:02.500. Why the recorder chose it:", first, StringComparison.Ordinal);
        Assert.Contains("No change in the page's layout was recorded during the visit", first, StringComparison.Ordinal);
        var tiles = Input(bodies[1]);
        Assert.Contains("This is group 1 of 2 of the tiles.", tiles, StringComparison.Ordinal);
        Assert.Contains("At the frame the page was scrolled to 1850 CSS pixels from the top and 0 from the left, with a viewport 922 by 925, so the part on the screen was CSS pixels 1850 to 2775:", tiles, StringComparison.Ordinal);
        Assert.Contains("1 of its 15 images were not loaded in the recreation, so they are blank or missing in the tiles: https://example.com/blogs.jpg.", tiles, StringComparison.Ordinal);
        Assert.Contains("CSS pixels 925 to 1850 from the top of a page 4625 high and 922 wide, pictured at 922 by 925 pixels, none of it on the screen at the frame", tiles, StringComparison.Ordinal);
        Assert.Contains("CSS pixels 1850 to 2775 from the top of a page 4625 high and 922 wide, pictured at 922 by 925 pixels, part of it on the screen at the frame. A recreation", tiles, StringComparison.Ordinal);
        // The tiles' files are in the task's own folder, so the request
        // leaves them out, to be the same at every run.
        Assert.DoesNotContain(", file ", tiles, StringComparison.Ordinal);
        var combined = Input(bodies[3]);
        Assert.Contains("The moment as a model described it from the captured frame and the recorder's facts. These are inferences, not facts:", combined, StringComparison.Ordinal);
        Assert.Contains("The recreated page as a model described it from tile 5 [", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("image_url", bodies[3], StringComparison.Ordinal);
        // Both groups of tiles gave the same statement, which is given once.
        Assert.Single(Regex.Matches(combined, Regex.Escape("(page, inferred) A statement about page.")));
        var visit = Input(bodies[8]);
        Assert.Contains("The visit as a model described it from the recorder's facts alone. These are inferences, not facts:", visit, StringComparison.Ordinal);
        Assert.Contains("(page, inferred) A statement about page.", visit, StringComparison.Ordinal);
        Assert.Contains("(screen, inferred) A statement about screen.", visit, StringComparison.Ordinal);
        Assert.Contains(", the recreated page in tiles [E", visit, StringComparison.Ordinal);
        Assert.Contains("The last captured frame before the visit ended.", visit, StringComparison.Ordinal);
        Assert.Contains("the page not recreated because the visit's page is not among the pages recorded at the frame", Input(bodies[11]), StringComparison.Ordinal);

        var report = Assert.Single(await catalog.ReportsAsync(vision, Token));
        Assert.Equal(RecordingSummaryVisionKind.FileName, report.FileName);
        var text = await File.ReadAllTextAsync(Path.Combine(_session, report.RelativePath), Token);
        Assert.StartsWith($"# Summary with vision of recording {Path.GetFileName(_session)}", text, StringComparison.Ordinal);
        Assert.Contains($"This adds what captured frames of the screen show to the summary of task {summary:D}", text, StringComparison.Ordinal);
        Assert.Contains("#### The participant's journey", text, StringComparison.Ordinal);
        Assert.Contains("#### The moments", text, StringComparison.Ordinal);
        Assert.Matches(@"Moment 1, 00:00:02: No change in the page's layout .* \[E\d+\] Captured frame \[E\d+\]\.", text);
        Assert.Matches(@"Recreated page: 922 by 4625 CSS pixels, scrolled to 1850, in 5 tiles: 1 \[E\d+\], 2 \[E\d+\], 3 \[E\d+\] \(on the screen\), 4 \[E\d+\], 5 \[E\d+\]\.", text);
        Assert.Matches(@"The page as recreated: A statement about page\. \[E\d+\]", text);
        Assert.Contains("The page was not recreated, as the visit's page is not among the pages recorded at the frame, so this moment is described from its captured frame alone.", text, StringComparison.Ordinal);
        Assert.Matches(@"On the screen: A statement about screen\. \[E\d+\]", text);
        Assert.Matches(@"What changed: A statement about change\. \[E\d+\]", text);
        Assert.Contains("- Moment limits: no limit on the moments of a visit; moments less than 2 s apart merged; settled when the layout has not changed for 1 s, or 5 s after the visit starts; a large change is 50 elements or more at once.", text, StringComparison.Ordinal);
        Assert.Contains($"{RecordingSummaryVisionKind.MomentTemplate} version 3 for each moment's captured frame, {RecordingSummaryVisionKind.PageTemplate} version 3 for each group of its page's tiles, " +
            $"{RecordingSummaryVisionKind.CombinedTemplate} version 4 for each moment's description from both", text, StringComparison.Ordinal);
        Assert.Contains("each request holds as many tiles as fit 4096 image tokens", text, StringComparison.Ordinal);
        Assert.Contains("The captured frame at 00:00:02.500: the whole screen, 1920 by 1080 pixels, file frames/desktop/0000000001.png", text, StringComparison.Ordinal);
        Assert.Contains("part of it on the screen at the frame, file ", text, StringComparison.Ordinal);
        Assert.Matches($@"Tile 3 of 5 of the page as the recorder rebuilt it .* file analysis/{vision:D}/pages/visit-0001-moment-01-tile-03\.png", text);
        Assert.True(File.Exists(Path.Combine(_session, "analysis", vision.ToString("D"), "pages", "visit-0001-moment-02-tile-05.png")));
        var read = ReportDocument.Parse(text);
        Assert.All(read.Blocks.SelectMany(block => block.References).Distinct(),
            reference => Assert.True(read.Evidence.ContainsKey(reference), reference));
    }

    [Fact]
    public void A_statement_given_twice_to_the_combined_description_is_given_once_with_the_references_of_both()
    {
        Claim Statement(string text, params string[] evidence) => new("page", 0, 0, Claim.ClaimKind, text, evidence);
        IReadOnlyList<IReadOnlyList<Claim>> blocks =
        [
            [Statement("The page is titled Home | CNIB.", "E1"), Statement("A hero image.", "E27")],
            [Statement("The page is titled Home | CNIB.", "E1", "E31"), Statement("The footer.", "E34")],
        ];

        var given = RecordingSummaryVisionKind.Distinct(blocks);

        Assert.Equal(["The page is titled Home | CNIB.", "A hero image."], given[0].Select(claim => claim.Text));
        Assert.Equal(["E1", "E31"], given[0][0].Evidence);
        Assert.Equal(["The footer."], given[1].Select(claim => claim.Text));
    }

    [Fact]
    public void The_templates_from_version_2_say_what_the_first_run_of_step_4b_showed_was_needed()
    {
        Assert.Contains("Do not restate the facts you are given", RecordingSummaryVisionKind.PageInstructions, StringComparison.Ordinal);
        Assert.Contains("Combine the descriptions; do not copy them. State each thing once", RecordingSummaryVisionKind.CombinedInstructions, StringComparison.Ordinal);
        Assert.Contains("not tile by tile", RecordingSummaryVisionKind.CombinedInstructions, StringComparison.Ordinal);
        foreach (var instructions in new[] { RecordingSummaryVisionKind.CombinedInstructions, RecordingSummaryVisionKind.VisitInstructions, RecordingSummaryVisionKind.RecordingInstructions })
        {
            Assert.Contains("Cite each reference as it is written without its brackets, such as \"E3\".", instructions, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("without its brackets", RecordingSummaryKind.RecordingInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public void The_combined_template_from_version_3_says_a_statement_of_what_is_unknown_is_of_kind_unknown_and_no_other_template_does()
    {
        const string Sentence = "A statement that says something is unknown, or that cites no reference, is of kind unknown; every claim cites at least one reference.";
        Assert.Contains(Sentence, RecordingSummaryVisionKind.CombinedInstructions, StringComparison.Ordinal);
        foreach (var instructions in new[]
                 {
                     RecordingSummaryVisionKind.MomentInstructions, RecordingSummaryVisionKind.PageInstructions,
                     RecordingSummaryVisionKind.VisitInstructions, RecordingSummaryVisionKind.RecordingInstructions,
                 })
        {
            Assert.DoesNotContain("is of kind unknown", instructions, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_template_shows_a_statement_with_its_text_before_its_kind()
    {
        Assert.Equal((4, 3, 3, 4), (RecordingSummaryKind.TemplateVersion, RecordingSummaryVisionKind.MomentTemplateVersion,
            RecordingSummaryVisionKind.TemplateVersion, RecordingSummaryVisionKind.CombinedTemplateVersion));
        foreach (var instructions in new[]
                 {
                     RecordingSummaryKind.VisitInstructions, RecordingSummaryKind.RecordingInstructions,
                     RecordingSummaryVisionKind.MomentInstructions, RecordingSummaryVisionKind.PageInstructions,
                     RecordingSummaryVisionKind.CombinedInstructions, RecordingSummaryVisionKind.VisitInstructions,
                     RecordingSummaryVisionKind.RecordingInstructions,
                 })
        {
            Assert.Contains("{\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]}", instructions, StringComparison.Ordinal);
            Assert.Contains("{\"text\": ..., \"kind\": \"unknown\", \"evidence\": []}", instructions, StringComparison.Ordinal);
            Assert.DoesNotContain("{\"kind\"", instructions, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Vision_named_after_no_summary_fails_saying_why()
    {
        WriteRecording(frames: true);
        var (catalog, recording) = await CatalogAsync();
        var vision = await catalog.QueueTaskAsync(
            recording, RecordingSummaryVisionKind.Name, VisionVersion,
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
            recording, RecordingSummaryVisionKind.Name, VisionVersion,
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
            Assert.Empty(evidence.LiveRegions);
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
