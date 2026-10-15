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
            var sections = body.Contains("You keep an understanding of one web page", StringComparison.Ordinal) ? RecordingSummaryVisionKind.PageSoFarSections
                : body.Contains("combining two descriptions", StringComparison.Ordinal) ? RecordingSummaryVisionKind.CombinedSections
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

        public List<string> Folders { get; } = [];

        public async Task<RecreatedPage> RecreateAsync(Visit visit, Moment moment, string folder, string relativeFolder, string prefix, CancellationToken cancellationToken)
        {
            Asked.Add((visit.Number, moment.Number, prefix));
            Folders.Add(folder);
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
                "visit-0001-moment-01", "visit-0001-moment-01-page-01", "visit-0001-moment-01-page-02", "visit-0001-moment-01-described", "visit-0001-moment-01-page-so-far",
                "visit-0001-moment-02", "visit-0001-moment-02-page-01", "visit-0001-moment-02-page-02", "visit-0001-moment-02-described", "visit-0001-moment-02-page-so-far",
                "visit-0001", "visit-0002-moment-01", "visit-0002-moment-01-page-so-far", "visit-0002-moment-02", "visit-0002-moment-02-page-so-far", "visit-0002", "recording"
            ],
            attempts.Select(row => (string)row["step_key"]!));
        Assert.All(attempts, row => Assert.Equal(AttemptOutcomes.Accepted, row["outcome"]));
        Assert.Equal(
            [
                RecordingSummaryVisionKind.MomentTemplate, RecordingSummaryVisionKind.PageTemplate, RecordingSummaryVisionKind.PageTemplate,
                RecordingSummaryVisionKind.CombinedTemplate, RecordingSummaryVisionKind.PageSoFarTemplate, RecordingSummaryVisionKind.MomentTemplate
            ],
            attempts.Take(6).Select(row => (string)row["prompt_template"]!));
        Assert.Equal(RecordingSummaryVisionKind.VisitTemplate, attempts[10]["prompt_template"]);
        Assert.Equal(RecordingSummaryVisionKind.RecordingTemplate, attempts[^1]["prompt_template"]);

        // Each frame's request carries its picture, and each group of tiles
        // as many tiles of 841 image tokens as fit 4,096: four, then one.
        var bodies = endpoint.Bodies.Skip(3).ToList();
        Assert.Equal(17, bodies.Count);
        var picture = "data:image/png;base64," + Convert.ToBase64String(FramePicture);
        int Pictures(string body) => (body.Length - body.Replace(picture, "", StringComparison.Ordinal).Length) / picture.Length;
        Assert.Equal([1, 4, 1, 0, 0, 1, 4, 1, 0, 0, 0, 1, 0, 1, 0, 0, 0], bodies.Select(Pictures));
        foreach (var index in new[] { 0, 5, 11, 13 })
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

        // The page understood so far: none at the first moment, then the
        // first's, with the recorder's comparison of the tiles, which are
        // the same picture at both moments.
        var firstSoFar = Input(bodies[4]);
        Assert.Contains("There is no understanding of the page yet: this is the first moment of the visit.", firstSoFar, StringComparison.Ordinal);
        Assert.Contains("This is the first moment of the visit whose page was recreated, so its tiles are compared with none.", firstSoFar, StringComparison.Ordinal);
        Assert.Contains("This moment as a model described it from its captured frame and its recreated page. These are inferences, not facts:", firstSoFar, StringComparison.Ordinal);
        Assert.Contains("(page, inferred) A statement about page.", firstSoFar, StringComparison.Ordinal);
        Assert.DoesNotContain("image_url", bodies[4], StringComparison.Ordinal);
        var secondSoFar = Input(bodies[9]);
        Assert.Contains("The understanding of the page so far, as a model wrote it after moment 1, at 00:00:02.500. These are inferences, not facts:", secondSoFar, StringComparison.Ordinal);
        Assert.Contains("(changes, inferred) A statement about changes.", secondSoFar, StringComparison.Ordinal);
        Assert.Matches(@"Compared by the recorder, file by file, with the tiles of the page as rebuilt at moment 1, at 00:00:02\.500, when the page was 922 by 4625 CSS pixels; it is 922 by 4625 now\. " +
            @"The same file, byte for byte, as the tile at the same place then, so that part of the recreated page did not change: tile 1 \[E\d+\], as tile 1 \[E\d+\] then; .*tile 5 \[E\d+\], as tile 5 \[E\d+\] then\.", secondSoFar);
        Assert.Contains("The page was not recreated at this moment, because the visit's page is not among the pages recorded at the frame", Input(bodies[12]), StringComparison.Ordinal);
        Assert.DoesNotContain("Compared by the recorder", Input(bodies[12]), StringComparison.Ordinal);

        // The visit is given the page as understood after its last moment,
        // and each moment's screen and change, not its page again.
        var visit = Input(bodies[10]);
        Assert.Contains("The visit as a model described it from the recorder's facts alone. These are inferences, not facts:", visit, StringComparison.Ordinal);
        Assert.Contains("The page through the visit as a model understood it, moment by moment, after the last moment, 2. These are inferences, not facts:", visit, StringComparison.Ordinal);
        Assert.Contains("(changes, inferred) A statement about changes.", visit, StringComparison.Ordinal);
        Assert.Contains("(screen, inferred) A statement about screen.", visit, StringComparison.Ordinal);
        Assert.Contains("(change, inferred) A statement about change.", visit, StringComparison.Ordinal);
        var moments = visit[visit.IndexOf("Moments of the visit chosen by the recorder", StringComparison.Ordinal)..];
        Assert.DoesNotContain("(page, inferred)", moments, StringComparison.Ordinal);
        Assert.Contains(", the recreated page in tiles [E", visit, StringComparison.Ordinal);
        Assert.Contains("The last captured frame before the visit ended.", visit, StringComparison.Ordinal);
        Assert.Contains("the page not recreated because the visit's page is not among the pages recorded at the frame", Input(bodies[15]), StringComparison.Ordinal);

        var report = Assert.Single(await catalog.ReportsAsync(vision, Token));
        Assert.Equal(RecordingSummaryVisionKind.FileName, report.FileName);
        var text = await File.ReadAllTextAsync(Path.Combine(_session, report.RelativePath), Token);
        Assert.StartsWith($"# Summary with vision of recording {Path.GetFileName(_session)}", text, StringComparison.Ordinal);
        Assert.Contains($"This adds what captured frames of the screen show to the summary of task {summary:D}", text, StringComparison.Ordinal);
        Assert.Contains("#### The participant's journey", text, StringComparison.Ordinal);
        Assert.Matches(@"#### The page\n\nA statement about page\. \[E\d+\]\n\n#### How the page changed\n\nA statement about changes\. \[E\d+\]\n\n#### What it is for\n\n", text);
        Assert.Equal(2, Regex.Matches(text, "#### The page\n").Count);
        Assert.Contains("#### The moments", text, StringComparison.Ordinal);
        Assert.Matches(@"Moment 1, 00:00:02: No change in the page's layout .* \[E\d+\] Captured frame \[E\d+\]\.", text);
        Assert.Matches(@"Recreated page: 922 by 4625 CSS pixels, scrolled to 1850, in 5 tiles: 1 \[E\d+\], 2 \[E\d+\], 3 \[E\d+\] \(on the screen\), 4 \[E\d+\], 5 \[E\d+\]\.", text);
        Assert.Matches(@"The page as recreated: A statement about page\. \[E\d+\]", text);
        Assert.Contains("The page was not recreated, as the visit's page is not among the pages recorded at the frame, so this moment is described from its captured frame alone.", text, StringComparison.Ordinal);
        Assert.Matches(@"On the screen: A statement about screen\. \[E\d+\]", text);
        Assert.Matches(@"What changed: A statement about change\. \[E\d+\]", text);
        Assert.Contains("- Moment limits: no limit on the moments of a visit; moments less than 2 s apart merged; settled when the layout has not changed for 1 s, or 5 s after the visit starts; a large change is 50 elements or more at once.", text, StringComparison.Ordinal);
        Assert.Contains($"{RecordingSummaryVisionKind.MomentTemplate} version 3 for each moment's captured frame, {RecordingSummaryVisionKind.PageTemplate} version 4 for each group of its page's tiles, " +
            $"{RecordingSummaryVisionKind.CombinedTemplate} version 4 for each moment's description from both, " +
            $"{RecordingSummaryVisionKind.PageSoFarTemplate} version 1 for the page as understood after each moment, {RecordingSummaryVisionKind.VisitTemplate} version 4 for each visit", text, StringComparison.Ordinal);
        Assert.Contains("each request holds as many tiles as fit 4096 image tokens", text, StringComparison.Ordinal);
        Assert.Contains("The captured frame at 00:00:02.500: the whole screen, 1920 by 1080 pixels, file frames/desktop/0000000001.png", text, StringComparison.Ordinal);
        Assert.Contains("part of it on the screen at the frame, file ", text, StringComparison.Ordinal);
        Assert.Matches($@"Tile 3 of 5 of the page as the recorder rebuilt it .* file analysis/{vision:D}/pages/visit-0001-moment-01-tile-03\.png", text);
        Assert.True(File.Exists(Path.Combine(_session, "analysis", vision.ToString("D"), "pages", "visit-0001-moment-02-tile-05.png")));
        var read = ReportDocument.Parse(text);
        Assert.All(read.Blocks.SelectMany(block => block.References).Distinct(),
            reference => Assert.True(read.Evidence.ContainsKey(reference), reference));
    }

    // The page understood moment by moment: decisions of 2026-10-15,
    // recording-summary.md.
    private static MomentDescription Recreated(int number, IReadOnlyList<(double Top, double Height)> places, int firstReference)
    {
        var frame = new CapturedFrame(number * 1_000_000_000L, $"frames/desktop/{number}.png", $"/frames/{number}.png", 1920, 1080, null);
        var tiles = places.Select((place, index) => new PageTile(index + 1, place.Top, place.Height, 922, $"t{index + 1}.png", $"/t{index + 1}.png", 922, 925)).ToList();
        var page = new RecreatedPage(tiles, 922, places[^1].Top + places[^1].Height, 0, 0, 922, 925, 0, [], 0, true, null);
        return new MomentDescription(
            new Moment(number, frame, []), $"E{firstReference}", [],
            new AcceptedStep(Guid.Empty, "", default, []), page,
            [.. tiles.Select(tile => $"E{firstReference + tile.Number}")]);
    }

    [Fact]
    public void The_recorder_compares_each_tile_with_the_tile_at_the_same_place_at_the_moment_before_file_by_file()
    {
        var before = Recreated(1, [(0, 925), (925, 925), (1850, 925)], 10);
        var now = Recreated(2, [(0, 925), (925, 925), (1850, 925), (2775, 925)], 20);

        var (text, references) = RecordingSummaryVisionKind.Comparison(now, ["A", "B", "Z", "D"], (before, ["A", "B", "C"]));

        Assert.Equal(
            "Compared by the recorder, file by file, with the tiles of the page as rebuilt at moment 1, at 00:00:01.000, when the page was 922 by 2775 CSS pixels; it is 922 by 3700 now. " +
            "The same file, byte for byte, as the tile at the same place then, so that part of the recreated page did not change: tile 1 [E21], as tile 1 [E11] then; tile 2 [E22], as tile 2 [E12] then. " +
            "A different file from the tile at the same place then, so that part of the recreated page changed or was pictured differently: tile 3 [E23], as tile 3 [E13] then. " +
            "With no tile at the same place then: tile 4 [E24].",
            text);
        Assert.Equal(["E11", "E12", "E13"], references);
        Assert.Equal(
            "This is the first moment of the visit whose page was recreated, so its tiles are compared with none.",
            RecordingSummaryVisionKind.Comparison(before, ["A", "B", "C"], null).Text);
    }

    [Fact]
    public void The_page_so_far_template_keeps_one_understanding_merging_descriptions_and_not_what_the_page_repeats()
    {
        var instructions = RecordingSummaryVisionKind.PageSoFarInstructions;
        Assert.Contains("Write the understanding again, whole, as it stands after this moment", instructions, StringComparison.Ordinal);
        Assert.Contains("a different description of it is a difference between descriptions, not a change of the page", instructions, StringComparison.Ordinal);
        Assert.Contains("where they conflict, say that they disagree, citing both, and do not settle it", instructions, StringComparison.Ordinal);
        Assert.Contains("Do not merge what the page itself repeats: a page may be badly written", instructions, StringComparison.Ordinal);
        Assert.Contains("A statement kept from the understanding so far keeps its references.", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("\"page\"", RecordingSummaryVisionKind.VisitInstructions, StringComparison.Ordinal);
        Assert.Contains("Do not describe the page again", RecordingSummaryVisionKind.VisitInstructions, StringComparison.Ordinal);
        Assert.Equal(["purpose", "activity", "journey"], RecordingSummaryVisionKind.VisitSections);
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
    public void The_combined_and_page_so_far_templates_say_a_statement_of_what_is_unknown_is_of_kind_unknown_and_no_other_template_does()
    {
        const string Sentence = "A statement that says something is unknown, or that cites no reference, is of kind unknown; every claim cites at least one reference.";
        Assert.Contains(Sentence, RecordingSummaryVisionKind.CombinedInstructions, StringComparison.Ordinal);
        Assert.Contains(Sentence, RecordingSummaryVisionKind.PageSoFarInstructions, StringComparison.Ordinal);
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
        Assert.Equal((4, 3, 4, 3, 4, 4, 1), (RecordingSummaryKind.TemplateVersion, RecordingSummaryVisionKind.MomentTemplateVersion,
            RecordingSummaryVisionKind.PageTemplateVersion, RecordingSummaryVisionKind.TemplateVersion, RecordingSummaryVisionKind.CombinedTemplateVersion,
            RecordingSummaryVisionKind.VisitTemplateVersion, RecordingSummaryVisionKind.PageSoFarTemplateVersion));
        foreach (var instructions in new[]
                 {
                     RecordingSummaryKind.VisitInstructions, RecordingSummaryKind.RecordingInstructions,
                     RecordingSummaryVisionKind.MomentInstructions, RecordingSummaryVisionKind.PageInstructions,
                     RecordingSummaryVisionKind.CombinedInstructions, RecordingSummaryVisionKind.VisitInstructions,
                     RecordingSummaryVisionKind.RecordingInstructions, RecordingSummaryVisionKind.PageSoFarInstructions,
                 })
        {
            Assert.Contains("{\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]}", instructions, StringComparison.Ordinal);
            Assert.Contains("{\"text\": ..., \"kind\": \"unknown\", \"evidence\": []}", instructions, StringComparison.Ordinal);
            Assert.DoesNotContain("{\"kind\"", instructions, StringComparison.Ordinal);
        }
    }

    // Run again, as on the target machine on 2026-10-14: a vision task
    // that failed at its first visit's step is run again, and must restart
    // there, every moment's step reused and the visit's request the same,
    // byte for byte; and run again once done, it sends nothing. See
    // analysis-runner.md, "Reusing steps when run again".
    [Fact]
    public async Task Vision_run_again_after_failing_at_a_visit_restarts_there_with_the_same_request_and_run_again_once_done_sends_nothing()
    {
        var (catalog, summary, first) = await QueueBothAsync();
        const string VisitStart = "You describe one page visit from an accessibility test recording, for an accessibility auditor, adding what captured frames";
        var refusing = VisionEndpoint();
        var answer = refusing.Answer;
        refusing.Answer = (profile, body, cancellationToken) => body.Contains(VisitStart, StringComparison.Ordinal)
            ? throw new ModelEndpointException("The model server answered 400 Bad Request: exceeds the available context size", refused: true)
            : answer(profile, body, cancellationToken);
        await RunAsync(catalog, refusing);
        Assert.Equal(AnalysisTaskStates.Failed, (await StateAsync(first)).State);
        var refused = Assert.Single(refusing.Bodies, body => body.Contains(VisitStart, StringComparison.Ordinal));
        var recording = (Guid)(await RowsAsync("SELECT recording_id FROM analysis_tasks WHERE task_id = $1", first))[0]["recording_id"]!;
        var profiles = new Dictionary<string, string> { [ModelCapabilities.Text] = WithImages, [ModelCapabilities.Image] = WithImages };

        var second = await catalog.QueueTaskAsync(recording, RecordingSummaryVisionKind.Name, VisionVersion, profiles,
            afterTaskId: summary, sessionDirectory: _session, rerunOfTaskId: first, cancellationToken: Token);
        var again = VisionEndpoint();
        await RunAsync(catalog, again);

        Assert.Equal((AnalysisTaskStates.Done, (string?)null), await StateAsync(second));
        var sent = again.Bodies.ToList();
        Assert.Equal(7, sent.Count);
        Assert.Equal(refused, sent[0]);
        Assert.DoesNotContain(sent, body => body.Contains("Moment 1 of the visit", StringComparison.Ordinal) && body.Contains("Page visit 1,", StringComparison.Ordinal));
        var visitOne = await RowsAsync(
            "SELECT step_key, reused_from_attempt_id FROM analysis_attempts WHERE task_id = $1 AND step_key LIKE 'visit-0001-moment-%'", second);
        Assert.Equal(10, visitOne.Count);
        Assert.All(visitOne, row => Assert.NotNull(row["reused_from_attempt_id"]));

        var third = await catalog.QueueTaskAsync(recording, RecordingSummaryVisionKind.Name, VisionVersion, profiles,
            afterTaskId: summary, sessionDirectory: _session, rerunOfTaskId: second, cancellationToken: Token);
        var once = VisionEndpoint();
        await RunAsync(catalog, once);

        Assert.Equal((AnalysisTaskStates.Done, (string?)null), await StateAsync(third));
        Assert.Empty(once.Bodies);
    }

    // Version 4 of the page template only: each statement says which of
    // like items it is about (owner, 2026-10-14).
    [Fact]
    public void The_page_template_asks_that_each_statement_name_the_item_it_is_about()
    {
        Assert.Contains(RecordingSummaryVisionKind.ItemNamed, RecordingSummaryVisionKind.PageInstructions, StringComparison.Ordinal);
        foreach (var instructions in new[]
                 {
                     RecordingSummaryVisionKind.MomentInstructions, RecordingSummaryVisionKind.CombinedInstructions,
                     RecordingSummaryVisionKind.VisitInstructions, RecordingSummaryVisionKind.RecordingInstructions,
                     RecordingSummaryVisionKind.PageSoFarInstructions
                 })
        {
            Assert.DoesNotContain(RecordingSummaryVisionKind.ItemNamed, instructions, StringComparison.Ordinal);
        }
    }

    // A trial keeps the pictures it takes in a folder of its own, never in
    // the task's, and says whether it sends the request the task sent
    // (owner, 2026-10-14, after a trial replaced a run's tiles).
    [Fact]
    public async Task Vision_trial_keeps_its_pictures_apart_from_the_tasks_and_says_whether_its_request_is_the_tasks()
    {
        var (catalog, _, vision) = await QueueBothAsync();
        var pages = new StandInPages();
        await RunAsync(catalog, VisionEndpoint(), pages);
        Assert.Equal(AnalysisTaskStates.Done, (await StateAsync(vision)).State);
        var taskFolder = Path.Combine(_session, "analysis", vision.ToString("D"), "pages");
        Assert.All(pages.Folders, folder => Assert.Equal(taskFolder, folder));
        var before = Directory.GetFiles(taskFolder).ToDictionary(path => path, File.GetLastWriteTimeUtc);

        var trialPages = new StandInPages();
        var trial = await new AnalysisWorker(catalog, VisionSettings(), [new RecordingSummaryKind(), new RecordingSummaryVisionKind { Pages = (_, _) => trialPages }], VisionEndpoint(), options: Fast)
            .TrialAsync(vision, "visit-0001-moment-02-described", new Dictionary<string, double>(), Token);

        Assert.NotEmpty(trialPages.Folders);
        Assert.All(trialPages.Folders, folder =>
        {
            Assert.StartsWith(Path.Combine(_session, "analysis", "trials", vision.ToString("D") + "-"), folder, StringComparison.Ordinal);
            Assert.EndsWith(Path.DirectorySeparatorChar + "pages", folder, StringComparison.Ordinal);
        });
        Assert.Equal(before, Directory.GetFiles(taskFolder).ToDictionary(path => path, File.GetLastWriteTimeUtc));
        Assert.Equal(trial.RequestSha256, trial.SentSha256);
        var attempt = Assert.Single(trial.TaskAttempts);
        Assert.Equal(trial.RequestSha256, attempt.InputSha256);
        var described = trial.Describe();
        Assert.Contains($"The task's attempt 1, accepted: SHA-256 {trial.RequestSha256}, the same request.", described, StringComparison.Ordinal);
        Assert.Contains("analysis/trials/" + vision.ToString("D") + "-", described, StringComparison.Ordinal);
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
