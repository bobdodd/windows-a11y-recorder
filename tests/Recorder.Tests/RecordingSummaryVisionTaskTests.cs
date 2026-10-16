using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;
using Recorder.Analysis;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

/// <summary>
/// The summary with vision, the layered report: the moments of each visit
/// recorded from their captured frames and recreated pages, the layers
/// added at each, and the report's two chapters, run by the analysis worker
/// after a summary of the same recording. See
/// docs/architecture/recording-summary.md, "The layered report".
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

    [GeneratedRegex(@"the captured frame \[(E\d+)\]")]
    private static partial Regex StoryFrame();

    // Answers each request with one statement in each of its sections: a
    // moment's citing its picture, the others the first reference given.
    // A layer's elaboration also cites K1, the visit's first statement, and
    // chapter 2's statements are a heading, a sentence, and a picture of
    // the first captured frame its request gives.
    private static StandInEndpoint VisionEndpoint()
    {
        var endpoint = new StandInEndpoint();
        endpoint.Answer = (_, body, _) =>
        {
            var input = Input(body);
            var picture = PictureReference().Match(input);
            var reference = picture.Success ? picture.Groups[1].Value : Reference().Match(input).Groups[1].Value;
            var output = new JsonObject();
            JsonObject Statement(string text, params string[] evidence) => new()
            {
                ["text"] = text,
                ["kind"] = "claim",
                ["evidence"] = new JsonArray([.. evidence.Select(item => JsonValue.Create(item))])
            };

            JsonObject Formed(string form, string text, params string[] evidence)
            {
                var statement = new JsonObject { ["form"] = form };
                foreach (var (name, value) in Statement(text, evidence))
                {
                    statement[name] = value?.DeepClone();
                }

                return statement;
            }

            if (body.Contains("\"elaborations\"", StringComparison.Ordinal))
            {
                var layer = body.Contains("what the web page adds", StringComparison.Ordinal) ? "layer 3" : "layer 2";
                output["elaborations"] = new JsonArray(new JsonArray(Statement($"An elaboration from {layer}.", "K1", reference)));
                output["corrections"] = new JsonArray();
                output["new"] = new JsonArray(new JsonArray(Statement($"Something new from {layer}.", reference)));
            }
            else if (body.Contains("\"story\"", StringComparison.Ordinal) || body.Contains("\"opening\"", StringComparison.Ordinal))
            {
                var section = body.Contains("\"story\"", StringComparison.Ordinal) ? "story" : "opening";
                var frame = StoryFrame().Match(input);
                var paragraph = new JsonArray(Formed("heading-1", $"A heading of the {section}.", reference), Formed("sentence", $"A sentence of the {section}.", reference));
                if (frame.Success)
                {
                    paragraph.Add(Formed("picture", $"A picture of the {section}.", frame.Groups[1].Value));
                }

                paragraph.Add(Formed("heading-2", $"A smaller heading of the {section}.", reference));
                paragraph.Add(Formed("sentence", $"Another sentence of the {section}.", reference));
                output[section] = new JsonArray(paragraph);
            }
            else
            {
                var sections = body.Contains("\"changes\"", StringComparison.Ordinal) ? RecordingSummaryVisionKind.PageChapterSections
                    : body.Contains("You describe part of a web page", StringComparison.Ordinal) ? RecordingSummaryVisionKind.PageSections
                    : body.Contains("\"screen\"", StringComparison.Ordinal) ? RecordingSummaryVisionKind.MomentSections
                    : body.Contains("\"audience\"", StringComparison.Ordinal) ? RecordingSummaryKind.RecordingSections
                    : RecordingSummaryKind.VisitSections;
                foreach (var section in sections)
                {
                    output[section] = new JsonArray(new JsonArray(Statement($"A statement about {section}.", reference)));
                }
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

    // Recreates each moment's page from a plan: for each moment in turn,
    // "-" for a page not recreated, or a letter for each tile's picture,
    // each letter a picture of its own colour, with "@" and the viewport's
    // height where it is not 925, such as "ABCDE" or "ABCDEF@900".
    private sealed class PlannedPages(params string[] plan) : IMomentPages
    {
        private int _asked;

        public async Task<RecreatedPage> RecreateAsync(Visit visit, Moment moment, string folder, string relativeFolder, string prefix, CancellationToken cancellationToken)
        {
            var step = plan[_asked++];
            if (step == "-")
            {
                return RecreatedPage.Not("the plan has no page at this moment");
            }

            var parts = step.Split('@');
            var viewport = parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 925;
            var letters = parts[0];
            Directory.CreateDirectory(folder);
            var tiles = new List<PageTile>();
            foreach (var (top, height) in RecordedMomentPages.Tiles(viewport * letters.Length, viewport))
            {
                var letter = (byte)letters[tiles.Count];
                var name = $"{prefix}-tile-{tiles.Count + 1:D2}.png";
                await File.WriteAllBytesAsync(Path.Combine(folder, name), TestPng.Rgba(1, 1, [letter, (byte)(255 - letter), 7, 255]), cancellationToken);
                tiles.Add(new PageTile(tiles.Count + 1, top, height, 922, relativeFolder + "/" + name, Path.Combine(folder, name), 922, viewport));
            }

            return new RecreatedPage(tiles, 922, viewport * letters.Length, 0, 0, 922, viewport, 0, [], 0, true, null);
        }
    }

    private async Task<IReadOnlyList<string>> TileStepsSentAsync(params string[] plan)
    {
        var (catalog, _, vision) = await QueueBothAsync();
        var planned = new PlannedPages(plan);
        await new AnalysisWorker(catalog, VisionSettings(), [new RecordingSummaryKind(), new RecordingSummaryVisionKind { Pages = (_, _) => planned }], VisionEndpoint(), options: Fast)
            .RunUntilEmptyAsync(Token);
        Assert.Equal((AnalysisTaskStates.Done, (string?)null), await StateAsync(vision));
        var rows = await RowsAsync("SELECT step_key FROM analysis_attempts WHERE task_id = $1 AND step_key ~ '-page-[0-9]+$' ORDER BY started_utc", vision);
        return [.. rows.Select(row => (string)row["step_key"]!)];
    }

    // A group of tiles is sent only where no group before it in the
    // recording had the same pictures at the same places: recording-
    // summary.md, "Unique images across the recording", decision 4. The
    // test recording has two visits of two moments each; five tiles of 925
    // are sent in groups of four and one.
    [Fact]
    public async Task A_group_the_same_pictures_as_at_the_moment_before_is_not_sent() =>
        Assert.Equal(["visit-0001-moment-01-page-01", "visit-0001-moment-01-page-02"], await TileStepsSentAsync("ABCDE", "ABCDE", "-", "-"));

    [Fact]
    public async Task A_group_with_one_tile_different_is_sent() =>
        Assert.Equal(["visit-0001-moment-01-page-01", "visit-0001-moment-01-page-02", "visit-0001-moment-02-page-01"],
            await TileStepsSentAsync("ABCDE", "ABXDE", "-", "-"));

    [Fact]
    public async Task Content_moved_below_the_first_group_sends_the_second_group_only() =>
        Assert.Equal(["visit-0001-moment-01-page-01", "visit-0001-moment-01-page-02", "visit-0001-moment-02-page-02"],
            await TileStepsSentAsync("ABCDE", "ABCDX", "-", "-"));

    [Fact]
    public async Task A_group_the_same_as_at_a_moment_of_an_earlier_visit_with_other_pictures_between_is_not_sent() =>
        Assert.Equal(["visit-0001-moment-01-page-01", "visit-0001-moment-01-page-02", "visit-0001-moment-02-page-01", "visit-0001-moment-02-page-02"],
            await TileStepsSentAsync("ABCDE", "FGHIJ", "ABCDE", "-"));

    [Fact]
    public async Task A_group_the_same_as_before_a_moment_not_recreated_is_not_sent() =>
        Assert.Equal(["visit-0001-moment-01-page-01", "visit-0001-moment-01-page-02"], await TileStepsSentAsync("ABCDE", "-", "ABCDE", "-"));

    [Fact]
    public async Task The_same_pictures_at_other_places_are_sent() =>
        Assert.Equal(["visit-0001-moment-01-page-01", "visit-0001-moment-01-page-02", "visit-0001-moment-02-page-01", "visit-0001-moment-02-page-02"],
            await TileStepsSentAsync("ABCDE", "ABCDE@900", "-", "-"));

    // The integration test of the store: the same unchanging page at every
    // moment of both visits, its tiles sent at the first moment only, each
    // picture kept once, and a run again keeping no new picture.
    [Fact]
    public async Task The_same_page_at_every_moment_of_two_visits_is_sent_once_and_its_pictures_kept_once()
    {
        var (catalog, summary, vision) = await QueueBothAsync();
        var endpoint = VisionEndpoint();
        await new AnalysisWorker(catalog, VisionSettings(),
                [new RecordingSummaryKind(), new RecordingSummaryVisionKind { Pages = (_, _) => new PlannedPages("ABCDE", "ABCDE", "ABCDE", "ABCDE") }], endpoint, options: Fast)
            .RunUntilEmptyAsync(Token);
        Assert.Equal((AnalysisTaskStates.Done, (string?)null), await StateAsync(vision));

        Assert.Equal(2, endpoint.Bodies.Count(body => body.Contains("You describe part of a web page", StringComparison.Ordinal)));
        var store = Path.Combine(_session, "analysis", "images");
        var kept = Directory.GetFiles(store).ToDictionary(path => path, File.GetLastWriteTimeUtc);
        Assert.Equal(5, kept.Count);
        var report = Assert.Single(await catalog.ReportsAsync(vision, Token));
        var text = await File.ReadAllTextAsync(Path.Combine(_session, report.RelativePath), Token);
        Assert.Contains("6 groups of tiles were not sent", text, StringComparison.Ordinal);
        Assert.Contains("visit 2 moment 2 group 2 (tile 5), the answer of group 2 at moment 1 of visit 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("moment-02-page-0", text, StringComparison.Ordinal);

        var recording = (Guid)(await RowsAsync("SELECT recording_id FROM analysis_tasks WHERE task_id = $1", vision))[0]["recording_id"]!;
        var again = await catalog.QueueTaskAsync(recording, RecordingSummaryVisionKind.Name, VisionVersion,
            new Dictionary<string, string> { [ModelCapabilities.Text] = WithImages, [ModelCapabilities.Image] = WithImages },
            afterTaskId: summary, sessionDirectory: _session, rerunOfTaskId: vision, cancellationToken: Token);
        var once = VisionEndpoint();
        await new AnalysisWorker(catalog, VisionSettings(),
                [new RecordingSummaryKind(), new RecordingSummaryVisionKind { Pages = (_, _) => new PlannedPages("ABCDE", "ABCDE", "ABCDE", "ABCDE") }], once, options: Fast)
            .RunUntilEmptyAsync(Token);

        Assert.Equal((AnalysisTaskStates.Done, (string?)null), await StateAsync(again));
        Assert.Empty(once.Bodies);
        Assert.Equal(kept, Directory.GetFiles(store).ToDictionary(path => path, File.GetLastWriteTimeUtc));
        Assert.Equal(20, ImageIndex.Read(Path.Combine(_session, "analysis", again.ToString("D"), "images.json")).Count);
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
    public async Task Vision_records_each_moment_adds_its_layers_then_writes_each_page_and_the_story()
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
                "visit-0001-moment-01", "visit-0001-moment-01-page-01", "visit-0001-moment-01-page-02", "visit-0001-moment-01-layer-2", "visit-0001-moment-01-layer-3",
                // Moment 2's tiles are the same pictures at the same places
                // as moment 1's, so its groups are not sent, and its layer 3
                // is not sent, as they are already in the visit's knowledge.
                "visit-0001-moment-02", "visit-0001-moment-02-layer-2",
                "visit-0002-moment-01", "visit-0002-moment-01-layer-2", "visit-0002-moment-02", "visit-0002-moment-02-layer-2",
                "page-01", "page-02", "visit-0001-story", "visit-0002-story", "story-opening"
            ],
            attempts.Select(row => (string)row["step_key"]!));
        Assert.All(attempts, row => Assert.Equal(AttemptOutcomes.Accepted, row["outcome"]));
        Assert.Equal(
            [
                RecordingSummaryVisionKind.MomentTemplate, RecordingSummaryVisionKind.PageTemplate, RecordingSummaryVisionKind.PageTemplate,
                RecordingSummaryVisionKind.Layer2Template, RecordingSummaryVisionKind.Layer3Template, RecordingSummaryVisionKind.MomentTemplate,
                RecordingSummaryVisionKind.Layer2Template
            ],
            attempts.Take(7).Select(row => (string)row["prompt_template"]!));
        Assert.Equal(
            [
                RecordingSummaryVisionKind.PageChapterTemplate, RecordingSummaryVisionKind.PageChapterTemplate,
                RecordingSummaryVisionKind.StoryTemplate, RecordingSummaryVisionKind.StoryTemplate, RecordingSummaryVisionKind.OpeningTemplate
            ],
            attempts.TakeLast(5).Select(row => (string)row["prompt_template"]!));

        // Each frame's request carries its picture, and each group of tiles
        // as many tiles of 841 image tokens as fit 4,096: four, then one.
        // No layer or chapter request carries a picture.
        var bodies = endpoint.Bodies.Skip(3).ToList();
        Assert.Equal(16, bodies.Count);
        var picture = "data:image/png;base64," + Convert.ToBase64String(FramePicture);
        int Pictures(string body) => (body.Length - body.Replace(picture, "", StringComparison.Ordinal).Length) / picture.Length;
        Assert.Equal([1, 4, 1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0], bodies.Select(Pictures));
        var tiles = Input(bodies[1]);
        Assert.Contains("This is group 1 of 2 of the tiles.", tiles, StringComparison.Ordinal);
        Assert.DoesNotContain(", file ", tiles, StringComparison.Ordinal);

        // Layer 2 is given the knowledge, numbered, starting as layer 1,
        // the visit's description from the recorded data, and the frame's
        // description; no schema of a form.
        var layer2 = Input(bodies[3]);
        Assert.Contains("The visit's knowledge so far, in layers. These are inferences, not facts:", layer2, StringComparison.Ordinal);
        Assert.Matches(@"  \[K1\] \(layer 1, the recorded data, page, inferred\) A statement about page\. \[E\d+\]\n", layer2);
        Assert.Contains("[K3] (layer 1, the recorded data, activity, inferred)", layer2, StringComparison.Ordinal);
        Assert.Contains("The frame as a model described it, from the picture and the recorder's facts. These are inferences, not facts:", layer2, StringComparison.Ordinal);
        Assert.Contains("(screen, inferred) A statement about screen.", layer2, StringComparison.Ordinal);
        Assert.Contains("K1", (string[])attempts[3]["input_references"]!);
        Assert.DoesNotContain("\"form\"", bodies[3], StringComparison.Ordinal);

        // Layer 3 is given layer 2's additions in place, under the
        // statement they elaborate, and each group's description.
        var layer3 = Input(bodies[4]);
        Assert.Matches(@"  \[K1\] \(layer 1, the recorded data, page, inferred\) A statement about page\. \[E\d+\]\n    \[K4\] \(layer 2, the captured frame at moment 1, elaborates K1, inferred\) An elaboration from layer 2\. \[E\d+\]\n", layer3);
        Assert.Contains("[K5] (layer 2, the captured frame at moment 1, inferred) Something new from layer 2.", layer3, StringComparison.Ordinal);
        Assert.Contains("The recreated page as a model described it from tile 1 [", layer3, StringComparison.Ordinal);
        Assert.Contains("The recreated page as a model described it from tile 5 [", layer3, StringComparison.Ordinal);
        Assert.Contains("so the part on the screen was CSS pixels 1850 to 2775:", layer3, StringComparison.Ordinal);

        // Moment 2's layer 2 has layer 3's additions too.
        Assert.Contains("(layer 3, the web page at moment 1, elaborates K1, inferred) An elaboration from layer 3.", Input(bodies[6]), StringComparison.Ordinal);

        // Chapter 1's pages are given the knowledge without numbers, and
        // the story its visit's facts, knowledge, moments, and section.
        var page = Input(bodies[11]);
        Assert.Contains("Page 1 of the report's first chapter, derived by the recorder:", page, StringComparison.Ordinal);
        Assert.DoesNotContain("[K", page, StringComparison.Ordinal);
        Assert.Contains("(layer 2, the captured frame at moment 1, elaborating the statement before it, inferred) An elaboration from layer 2.", page, StringComparison.Ordinal);
        var story = Input(bodies[13]);
        Assert.Contains("The visit's page is described in the report's first chapter, section 1.1.", story, StringComparison.Ordinal);
        Assert.Matches(@"Moment 1, at 00:00:02\.500: the captured frame \[E\d+\]; the page as the recorder rebuilt it, in tiles: tile 1 \[E\d+\], CSS pixels 0 to 925; .*tile 3 \[E\d+\], CSS pixels 1850 to 2775, on the screen;", story);
        Assert.Contains("The story so far: this is the first part.", story, StringComparison.Ordinal);
        Assert.Contains("\"form\"", bodies[13], StringComparison.Ordinal);
        var secondStory = Input(bodies[14]);
        Assert.Contains("the page was not recreated", secondStory, StringComparison.Ordinal);
        Assert.Contains("The part for visit 1:\n  (heading-1) A heading of the story.", secondStory, StringComparison.Ordinal);
        Assert.Contains("(picture) A picture of the story.", secondStory, StringComparison.Ordinal);
        Assert.Contains("Section 1.2:", Input(bodies[15]), StringComparison.Ordinal);

        var report = Assert.Single(await catalog.ReportsAsync(vision, Token));
        Assert.Equal(RecordingSummaryVisionKind.FileName, report.FileName);
        var text = await File.ReadAllTextAsync(Path.Combine(_session, report.RelativePath), Token);
        Assert.StartsWith($"# Report of recording {Path.GetFileName(_session)}", text, StringComparison.Ordinal);
        Assert.Contains($"in the summary of task {summary:D}", text, StringComparison.Ordinal);
        Assert.Contains($"is in analysis/{vision:D}/moments.json.", text, StringComparison.Ordinal);
        Assert.Contains("## 1. The pages\n\n### 1.1 ", text, StringComparison.Ordinal);
        Assert.Matches(@"#### 1\.1\.1 How the page changed\n\nA statement about changes\. \[E\d+\]\n\n### 1\.2 ", text);

        // Chapter 2: the opening and each visit's part, the headings
        // numbered on from one to the next, the sentences between them one
        // paragraph, and each picture put in from its frame's file.
        Assert.Contains("## 2. The user experience\n\n### 2.1 A heading of the opening.\n\n", text, StringComparison.Ordinal);
        Assert.Matches(@"### 2\.2 A heading of the story\.\n\nA sentence of the story\. \[E\d+\]\n\n!\[Figure 1\. A picture of the story\.\]\(\.\./\.\./frames/desktop/\d+\.png\)\n\nFigure 1\. A picture of the story\. \[E\d+\]\n\n#### 2\.2\.1 A smaller heading of the story\.\n\nAnother sentence of the story\. \[E\d+\]\n\n### 2\.3 A heading of the story\.", text);
        Assert.Contains("Figure 2. A picture of the story.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[K", text, StringComparison.Ordinal);
        Assert.Contains($"{RecordingSummaryVisionKind.Layer2Template} and {RecordingSummaryVisionKind.Layer3Template} version 1 for each moment's layers", text, StringComparison.Ordinal);
        Assert.Contains("- Layer 3 steps not sent, as every group of the moment's tiles was already in the visit's knowledge: visit-0001-moment-02-layer-3.\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "2 groups of tiles were not sent, as their tiles were the same pictures, pixel for pixel, at the same places as a group already described, whose answer each uses unchanged: " +
            "visit 1 moment 2 group 1 (tiles 1 to 4), the answer of group 1 at moment 1 of visit 1, at 00:00:02, step visit-0001-moment-01-page-01; " +
            "visit 1 moment 2 group 2 (tile 5), the answer of group 2 at moment 1 of visit 1, at 00:00:02, step visit-0001-moment-01-page-02.\n", text, StringComparison.Ordinal);
        var stored = Assert.Single(Directory.GetFiles(Path.Combine(_session, "analysis", "images")));
        var key = Path.GetFileNameWithoutExtension(stored);
        Assert.False(Directory.Exists(Path.Combine(_session, "analysis", vision.ToString("D"), "pages")));

        // The reading view's document has the pictures, each file there,
        // and every reference it cites in the evidence list.
        var read = ReportDocument.Parse(text);
        var shown = read.Blocks.Where(block => block.Kind == ReportBlockKind.Image).ToList();
        Assert.Equal(2, shown.Count);
        Assert.All(shown, block => Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(Path.Combine(_session, report.RelativePath))!, block.Source!))));
        Assert.All(read.Blocks.SelectMany(block => block.References).Distinct(),
            reference => Assert.True(read.Evidence.ContainsKey(reference), reference));

        // The task's indexes give every tile of every moment recreated, and
        // every moment's record.
        Assert.Equal(10, ImageIndex.Read(Path.Combine(_session, "analysis", vision.ToString("D"), "images.json")).Count);
        var records = MomentIndex.Read(Path.Combine(_session, "analysis", vision.ToString("D"), "moments.json"));
        Assert.Equal([(1, 1), (1, 2), (2, 1), (2, 2)], records.Select(record => (record.Visit, record.Moment)));
        Assert.Equal(("visit-0001-moment-01", true, 5), (records[0].FrameStep, records[0].Page.Recreated, records[0].Page.Tiles.Count));
        Assert.Equal(
            [new MomentRecordGroup(1, 1, 4, "visit-0001-moment-01-page-01", false, 1, 1), new MomentRecordGroup(2, 5, 5, "visit-0001-moment-01-page-02", false, 1, 1)],
            records[1].Page.Groups);
        Assert.True(records[1].Page.Tiles[2].OnScreen);
        Assert.Equal($"analysis/images/{key}.png", records[1].Page.Tiles[0].File);
        Assert.False(records[2].Page.Recreated);
        Assert.Equal("the visit's page is not among the pages recorded at the frame", records[2].Page.NotRecreated);
    }

    [Fact]
    public void The_layer_templates_ask_for_additions_in_place_and_the_chapter_2_templates_for_forms()
    {
        foreach (var instructions in new[] { RecordingSummaryVisionKind.Layer2Instructions, RecordingSummaryVisionKind.Layer3Instructions })
        {
            Assert.Contains("\"elaborations\", statements that add detail to a numbered statement", instructions, StringComparison.Ordinal);
            Assert.Contains("Where it adds nothing, give the three sections empty.", instructions, StringComparison.Ordinal);
            Assert.Contains("never say that NVDA said it or that the participant heard it", instructions, StringComparison.Ordinal);
        }

        Assert.Contains("however small", RecordingSummaryVisionKind.Layer3Instructions, StringComparison.Ordinal);
        Assert.Contains("Describe the page, not the participant.", RecordingSummaryVisionKind.PageChapterInstructions, StringComparison.Ordinal);
        foreach (var instructions in new[] { RecordingSummaryVisionKind.StoryInstructions, RecordingSummaryVisionKind.OpeningInstructions })
        {
            Assert.Contains("\"heading-1\", \"heading-2\", or \"heading-3\"", instructions, StringComparison.Ordinal);
            Assert.Contains("{\"form\": ..., \"text\": ..., \"kind\": \"claim\", \"evidence\": [...]}", instructions, StringComparison.Ordinal);
        }

        Assert.Equal(["heading-1", "heading-2", "heading-3", "sentence", "picture"], RecordingSummaryVisionKind.StoryForms);
        Assert.Equal(3, new RecordingSummaryVisionKind().Version);
    }

    [Fact]
    public void The_templates_from_version_2_say_what_the_first_run_of_step_4b_showed_was_needed()
    {
        Assert.Contains("Do not restate the facts you are given", RecordingSummaryVisionKind.PageInstructions, StringComparison.Ordinal);
        foreach (var instructions in new[]
                 {
                     RecordingSummaryVisionKind.Layer2Instructions, RecordingSummaryVisionKind.Layer3Instructions, RecordingSummaryVisionKind.PageChapterInstructions,
                     RecordingSummaryVisionKind.StoryInstructions, RecordingSummaryVisionKind.OpeningInstructions
                 })
        {
            Assert.Contains("Cite each reference as it is written without its brackets, such as \"E3\".", instructions, StringComparison.Ordinal);
            Assert.Contains("A statement that says something is unknown, or that cites no reference, is of kind unknown; every claim cites at least one reference.", instructions, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("without its brackets", RecordingSummaryKind.RecordingInstructions, StringComparison.Ordinal);
        foreach (var instructions in new[] { RecordingSummaryVisionKind.MomentInstructions, RecordingSummaryVisionKind.PageInstructions })
        {
            Assert.DoesNotContain("is of kind unknown", instructions, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_template_shows_a_statement_with_its_text_before_its_kind()
    {
        Assert.Equal((4, 3, 4, 1, 1), (RecordingSummaryKind.TemplateVersion, RecordingSummaryVisionKind.MomentTemplateVersion,
            RecordingSummaryVisionKind.PageTemplateVersion, RecordingSummaryVisionKind.LayerTemplateVersion, RecordingSummaryVisionKind.ChapterTemplateVersion));
        foreach (var instructions in new[]
                 {
                     RecordingSummaryKind.VisitInstructions, RecordingSummaryKind.RecordingInstructions,
                     RecordingSummaryVisionKind.MomentInstructions, RecordingSummaryVisionKind.PageInstructions,
                     RecordingSummaryVisionKind.Layer2Instructions, RecordingSummaryVisionKind.Layer3Instructions, RecordingSummaryVisionKind.PageChapterInstructions,
                     RecordingSummaryVisionKind.StoryInstructions, RecordingSummaryVisionKind.OpeningInstructions,
                 })
        {
            Assert.Contains("\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]}", instructions, StringComparison.Ordinal);
            Assert.Contains("\"text\": ..., \"kind\": \"unknown\", \"evidence\": []}", instructions, StringComparison.Ordinal);
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
        const string VisitStart = "You write one part of the second chapter";
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
        Assert.Equal(3, sent.Count);
        Assert.Equal(refused, sent[0]);
        Assert.DoesNotContain(sent, body => body.Contains("Moment 1 of the visit", StringComparison.Ordinal) && body.Contains("Page visit 1,", StringComparison.Ordinal));
        var visitOne = await RowsAsync(
            "SELECT step_key, reused_from_attempt_id FROM analysis_attempts WHERE task_id = $1 AND step_key LIKE 'visit-0001-moment-%'", second);
        Assert.Equal(7, visitOne.Count);
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
                     RecordingSummaryVisionKind.MomentInstructions, RecordingSummaryVisionKind.Layer2Instructions,
                     RecordingSummaryVisionKind.Layer3Instructions, RecordingSummaryVisionKind.PageChapterInstructions,
                     RecordingSummaryVisionKind.StoryInstructions, RecordingSummaryVisionKind.OpeningInstructions
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
        var store = Path.Combine(_session, "analysis", "images");
        var index = Path.Combine(_session, "analysis", vision.ToString("D"), "images.json");
        var before = Directory.GetFiles(store).Append(index).ToDictionary(path => path, File.GetLastWriteTimeUtc);

        var trialPages = new StandInPages();
        var trial = await new AnalysisWorker(catalog, VisionSettings(), [new RecordingSummaryKind(), new RecordingSummaryVisionKind { Pages = (_, _) => trialPages }], VisionEndpoint(), options: Fast)
            .TrialAsync(vision, "visit-0001-moment-02-layer-2", new Dictionary<string, double>(), Token);

        Assert.NotEmpty(trialPages.Folders);
        Assert.All(trialPages.Folders, folder =>
        {
            Assert.StartsWith(Path.Combine(_session, "analysis", "trials", vision.ToString("D") + "-"), folder, StringComparison.Ordinal);
            Assert.EndsWith(Path.DirectorySeparatorChar + "pages", folder, StringComparison.Ordinal);
        });
        Assert.Equal(before, Directory.GetFiles(store).Append(index).ToDictionary(path => path, File.GetLastWriteTimeUtc));
        var trialFolder = Path.GetDirectoryName(trialPages.Folders[0])!;
        Assert.Single(Directory.GetFiles(Path.Combine(trialFolder, "images")));
        Assert.True(File.Exists(Path.Combine(trialFolder, "images.json")));
        Assert.Equal(trial.RequestSha256, trial.SentSha256);
        var attempt = Assert.Single(trial.TaskAttempts);
        Assert.Equal(trial.RequestSha256, attempt.InputSha256);
        var described = trial.Describe();
        Assert.Contains($"The task's attempt 1, accepted: SHA-256 {trial.RequestSha256}, the same request.", described, StringComparison.Ordinal);
        Assert.Contains("analysis/trials/" + vision.ToString("D") + "-", described, StringComparison.Ordinal);
    }

    // A group of tiles not sent at a moment has no request to try, so a
    // trial of it says so (recording-summary.md, "Unique images across the
    // recording").
    [Fact]
    public async Task Vision_trial_of_a_group_of_tiles_not_sent_says_whose_answer_it_uses()
    {
        var (catalog, _, vision) = await QueueBothAsync();
        await RunAsync(catalog, VisionEndpoint());
        Assert.Equal(AnalysisTaskStates.Done, (await StateAsync(vision)).State);

        var pages = new StandInPages();
        var failure = await Assert.ThrowsAsync<StepFailedException>(() =>
            new AnalysisWorker(catalog, VisionSettings(), [new RecordingSummaryKind(), new RecordingSummaryVisionKind { Pages = (_, _) => pages }], VisionEndpoint(), options: Fast)
                .TrialAsync(vision, "visit-0001-moment-02-page-01", new Dictionary<string, double>(), Token));

        Assert.Equal(
            "Step visit-0001-moment-02-page-01 is not sent in this task: its tiles are the same pictures at the same places as group 1 at moment 1 of visit 1, at 00:00:02.500, " +
            "so that group's answer, step visit-0001-moment-01-page-01, is used.",
            failure.Message);
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
