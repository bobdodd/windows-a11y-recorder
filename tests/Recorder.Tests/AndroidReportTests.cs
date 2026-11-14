using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Recorder.Tests;

// docs/architecture/android-report.md: the Android report and summary. The
// recording is made up, as in PhoneCheckRunTests: a Settings window with a
// 40 dp "Save" button at the Pixel 10 Pro XL's 390 dpi and 1080 by 2404
// pixels, and one video frame of the phone, all white.
public sealed class AndroidReportTests : IDisposable
{
    private const long Second = 1_000_000_000;

    private const string Settings = "com.android.settings";

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "android.accessibility", "AndroidAccessibilityCollector", "1",
        [AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.TreeChannel, AndroidCompanionRecords.DisplayChannel],
        "test");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "android-report-" + Guid.NewGuid().ToString("N"));

    public AndroidReportTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static long At(double seconds) => (long)Math.Round(seconds * Second);

    [Fact]
    public void Guidance_CoversEveryCheck_AndEveryKeyItNamesResolves()
    {
        var guidance = AndroidGuidance.Load();

        string[] measured =
        [
            PhoneCheckNames.TextContrastAa, PhoneCheckNames.TextContrastAaa, PhoneCheckNames.NonTextContrast,
            PhoneCheckNames.ImageOfText, PhoneCheckNames.ImageOfTextAaa, PhoneCheckNames.LabelNotInName, PhoneCheckNames.LabelNotAtStart,
        ];
        var checks = PhoneCheckNames.All.Concat(measured).ToList();
        Assert.Equal(checks.Order(StringComparer.Ordinal), guidance.Checks.Select(check => check.Check).Order(StringComparer.Ordinal));
        Assert.Equal(["high", "medium", "low"], guidance.Impact.Select(step => step.Level));
        Assert.Matches("^[0-9a-f]{64}$", guidance.Sha256);
        foreach (var check in guidance.Checks)
        {
            Assert.InRange(guidance.Rank(check.Impact), 1, 3);
            Assert.NotEmpty(check.Affects);
            Assert.NotEmpty(check.Sources);
            Assert.All(new[] { check.Title, check.Issue, check.Why, check.Basis, check.Android, check.Flutter, check.ReactNative }, text => Assert.False(string.IsNullOrWhiteSpace(text), check.Check));
            Assert.All(check.Sources, key => Assert.True(guidance.Sources.ContainsKey(key), $"{check.Check}: {key}"));
            Assert.All(check.Criteria.Concat(check.Related), number => Assert.True(guidance.Criteria.ContainsKey(number), $"{check.Check}: {number}"));
        }

        Assert.All(guidance.Criteria.Values, criterion => Assert.True(guidance.Sources.ContainsKey(criterion.Source), criterion.Name));
        Assert.All(guidance.Sources.Values, source => Assert.StartsWith("https://", source.Url, StringComparison.Ordinal));

        // No em dashes in the guidance's prose (owner's rule for documentation).
        using var stream = typeof(AndroidGuidance).Assembly.GetManifestResourceStream(AndroidGuidance.ResourceName)!;
        using var reader = new StreamReader(stream);
        Assert.DoesNotContain('\u2014', reader.ReadToEnd());
        Assert.Equal("2.5.8 Target Size (Minimum) (AA)", guidance.CriterionText("2.5.8"));
    }

    [Fact]
    public void Outline_DrawsBlackCyanBlack_OutsideAGap_AndStaysInsideThePicture()
    {
        var canvas = White(100, 100);

        var outer = ReportPictures.Outline(canvas, new PictureBox(40, 40, 60, 60), 1);

        Assert.Equal(new PictureBox(35, 35, 65, 65), outer);
        Assert.Equal(ReportPictures.Black, canvas.At(35, 50));
        Assert.Equal(ReportPictures.Cyan, canvas.At(36, 50));
        Assert.Equal(ReportPictures.Cyan, canvas.At(37, 50));
        Assert.Equal(ReportPictures.Black, canvas.At(38, 50));
        Assert.Equal(0xFFFFFFu, canvas.At(39, 50));
        Assert.Equal(0xFFFFFFu, canvas.At(50, 50));

        // An item at the picture's edge keeps its outline inside it.
        var edge = ReportPictures.Outline(canvas, new PictureBox(0, 0, 20, 20), 1);
        Assert.Equal((0, 0), (edge.Left, edge.Top));
        Assert.Equal(ReportPictures.Black, canvas.At(0, 10));
        Assert.Equal(ReportPictures.Cyan, canvas.At(1, 10));
    }

    [Fact]
    public void Badge_DrawsTheNumberInBlackOnCyan_AboveTheOutline()
    {
        var canvas = White(200, 200);

        var badge = ReportPictures.Badge(canvas, 17, new PictureBox(50, 100, 120, 150), 1);

        // Two digits of 3 dots of 2 pixels, a dot's space between them, a
        // dot's margin each side and a pixel's edge: 2*6 + 2 + 4 + 2 = 20.
        Assert.Equal(new PictureBox(50, 84, 70, 100), badge);
        Assert.Equal(ReportPictures.Black, canvas.At(50, 84));
        Assert.Equal(ReportPictures.Cyan, canvas.At(51, 85));

        // The 1's top row is its middle dot only; the 7's top row is full.
        Assert.Equal(ReportPictures.Cyan, canvas.At(53, 87));
        Assert.Equal(ReportPictures.Black, canvas.At(55, 87));
        Assert.Equal(ReportPictures.Black, canvas.At(61, 87));
        Assert.Equal(ReportPictures.Black, canvas.At(65, 87));
    }

    [Fact]
    public void ZoomAndScale_KeepTheItsPixels_AndThePngReadsBack()
    {
        var pixels = new PhonePixels(4, 2, [0xFF0000, 0x00FF00, 0x0000FF, 0xFFFFFF, 0, 0, 0, 0x808080]);

        var zoomed = ReportPictures.Zoomed(pixels, new PictureBox(1, 0, 3, 2), 3);
        Assert.Equal((6, 6), (zoomed.Width, zoomed.Height));
        Assert.Equal(0x00FF00u, zoomed.At(2, 2));
        Assert.Equal(0x0000FFu, zoomed.At(3, 0));

        var half = ReportPictures.Scaled(pixels, 2);
        Assert.Equal((2, 1), (half.Width, half.Height));
        Assert.Equal(0x3F3F00u, half.At(0, 0));

        var read = PhonePictures.FromPng(ReportPictures.Png(zoomed))!;
        Assert.Equal((6, 6, 0x00FF00u), (read.Width, read.Height, read.At(2, 2)));

        var (crop, scale) = ReportPictures.ZoomOf(new PictureBox(100, 200, 198, 298), 1080, 2404);
        Assert.Equal((new PictureBox(51, 151, 247, 347), 3), (crop, scale));
    }

    [Fact]
    public void Report_IsByScreen_WithPicturesOutlinesAndEachFindingsFields_AndReadsBackWithItsEvidence()
    {
        var (input, guidance, evidence) = Recording();
        var pictures = Path.Combine(_folder, "pictures");

        var markdown = AndroidReport.Write(input, guidance, evidence, null, pictures, "pictures", new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc));

        var document = ReportDocument.Parse(markdown);
        Assert.Equal("Android accessibility report: s", document.Title);
        Assert.DoesNotContain('\u2014', markdown);
        var headings = document.Blocks.Where(block => block.Kind == ReportBlockKind.Heading).Select(block => (block.Level, block.Text)).ToList();
        Assert.Contains((2, "Screen 1: Settings"), headings);
        // The app as a whole has no live region; that finding comes first.
        Assert.Equal([PhoneCheckNames.NoLiveRegion, PhoneCheckNames.TouchTarget], evidence.All.Select(finding => finding.Finding.Check));
        Assert.Contains((3, "Finding 1: No live region anywhere in the app: FrameLayout containing \"Save\", \"Settings\""), headings);
        Assert.Contains((4, "Finding 2: Touch target smaller than Android's 48 by 48 dp: Button \"Save\""), headings);

        // The finding's fields, then its picture, its guidance and sources.
        var table = document.Blocks.Where(block => block.Kind == ReportBlockKind.Table && block.Rows[0][0] == "Field").Last();
        Assert.Equal(["Level", "Impact", "WCAG success criteria", "Item", "Measured", "Seen", "Check"], table.Rows.Skip(1).Select(row => row[0]));
        Assert.Equal("Error", table.Rows[1][1]);
        Assert.Equal("Medium", table.Rows[2][1]);
        Assert.Equal("None: best practice, related to 2.5.8 Target Size (Minimum) (AA)", table.Rows[3][1]);
        // Both snapshots are shown by the one video frame.
        Assert.Equal("Twice, in 1 picture, at 00:00:01.500 [E6]", table.Rows[6][1]);
        var paragraphs = document.Blocks.Where(block => block.Kind == ReportBlockKind.Paragraph).Select(block => block.Text).ToList();
        Assert.Contains(paragraphs, text => text.StartsWith("What the issue is: A control that can be tapped", StringComparison.Ordinal));
        Assert.Contains(paragraphs, text => text.StartsWith("Suggested remediation for Android:", StringComparison.Ordinal));
        Assert.Contains("The app com.android.settings; the screen is named by its window title. Visited once, with 1 finding. [E4]", paragraphs);
        Assert.Contains("The recording's first app window. [E5]", paragraphs);

        // No framework was found, so Android's remediation and sources only.
        Assert.DoesNotContain(paragraphs, text => text.StartsWith("Suggested remediation for Flutter:", StringComparison.Ordinal));
        var sources = document.Blocks.Where(block => block.Kind == ReportBlockKind.ListItem).Select(block => block.Text).ToList();
        Assert.Contains(sources, text => text.Contains("https://support.google.com/accessibility/android/answer/7101858", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, text => text.Contains("flutter.dev", StringComparison.Ordinal) || text.Contains("reactnative.dev", StringComparison.Ordinal));

        // The visit's picture with the item outlined, and the item zoomed.
        var images = document.Blocks.Where(block => block.Kind == ReportBlockKind.Image).ToList();
        Assert.Equal(2, images.Count);
        Assert.Equal("The screen Settings at 00:00:01.500, from a video frame, with finding 2 outlined in cyan and numbered.", images[0].Text);
        Assert.StartsWith("Finding 2, Button \"Save\", on the screen Settings, zoomed 3 times", images[1].Text, StringComparison.Ordinal);
        var overview = PhonePictures.Load(Path.Combine(_folder, images[0].Source!))!;
        // Half size, with a grey edge of 4 pixels round it.
        Assert.Equal((548, 1210), (overview.Width, overview.Height));
        Assert.Equal((ReportPictures.Edge, ReportPictures.Edge, 0xFFFFFFu), (overview.At(0, 0), overview.At(3, 600), overview.At(4, 600)));
        // The button's left edge is at 50 at half size, 54 with the edge,
        // the unit 2 pixels: a gap of 1 unit, black 1, cyan 2, black 1.
        Assert.Equal(0xFFFFFFu, overview.At(54 - 1, 129));
        Assert.Equal(ReportPictures.Black, overview.At(54 - 3, 129));
        Assert.Equal(ReportPictures.Cyan, overview.At(54 - 5, 129));
        Assert.Equal(ReportPictures.Black, overview.At(54 - 9, 129));
        var zoom = PhonePictures.Load(Path.Combine(_folder, images[1].Source!))!;
        Assert.Equal((596, 596), (zoom.Width, zoom.Height));

        // Each visit and finding cited is in the evidence list, at its time.
        Assert.Equal(At(1.5), document.Evidence["E6"].Nanoseconds);
        Assert.Equal(At(1), document.Evidence["E5"].Nanoseconds);
        Assert.StartsWith("Visit 1 to the screen Settings", document.Evidence["E5"].Text, StringComparison.Ordinal);
        // The screen and the app have references too, the app's for the whole recording.
        Assert.StartsWith("Screen 1, Settings (com.android.settings): the screen is named by its window title", document.Evidence["E4"].Text, StringComparison.Ordinal);
        Assert.Equal(At(1), document.Evidence["E4"].Nanoseconds);
        Assert.Null(document.Evidence["E1"].Nanoseconds);
        Assert.Equal("Whole recording", document.Evidence["E1"].Time);
        Assert.Contains(document.Blocks, block => block.Kind == ReportBlockKind.Heading && block.Text == "Limits");
    }

    [Fact]
    public void WordDocument_IsValid_WithHeadingsRepeatedHeaderRowsPicturesAltTextLinksAndLanguage()
    {
        var (input, guidance, evidence) = Recording();
        var markdown = AndroidReport.Write(input, guidance, evidence, null, Path.Combine(_folder, "pictures"), "pictures", DateTime.UtcNow, ReportAudience.Client);

        var bytes = WordReport.Write(markdown, _folder, AndroidReport.Labels);

        using var stream = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(stream, false);
        // Office 2016's schema, as Word writes tblLook's attributes, which
        // the 2007 schema lacks.
        var errors = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2016)
            .Validate(word, TestContext.Current.CancellationToken)
            .Select(error => error.Description + " at " + error.Path?.XPath);
        Assert.Equal("", string.Join("\n", errors));
        Assert.Equal("Accessibility report: com.android.settings", word.PackageProperties.Title);
        Assert.Equal("en-CA", word.PackageProperties.Language);
        var main = word.MainDocumentPart!;
        var body = main.Document!.Body!;
        var styles = body.Descendants<ParagraphStyleId>().Select(style => style.Val!.Value).ToList();
        Assert.Equal("Title", styles[0]);
        Assert.Single(styles, style => style == "Title");
        Assert.Contains("Heading1", styles);
        Assert.Contains("Heading3", styles);
        Assert.Contains("TOC1", styles);
        Assert.Contains("TOC2", styles);
        Assert.Contains(body.Descendants<TableHeader>(), _ => true);
        // Every table has a caption, so a screen reader's list of tables names it.
        Assert.All(body.Descendants<Table>(), table => Assert.False(string.IsNullOrEmpty(table.GetFirstChild<TableProperties>()!.GetFirstChild<TableCaption>()?.Val?.Value)));
        var pictures = body.Descendants<DW.DocProperties>().ToList();
        Assert.Equal(2, pictures.Count);
        Assert.All(pictures, picture => Assert.False(string.IsNullOrEmpty(picture.Description)));
        Assert.Equal(2, main.ImageParts.Count());
        Assert.Contains(main.HyperlinkRelationships, link => link.Uri.AbsoluteUri.StartsWith("https://support.google.com/accessibility/android/answer/7101858", StringComparison.Ordinal));
        // A link reads as its source's title, not its address.
        Assert.DoesNotContain(body.Descendants<Hyperlink>(), link => link.InnerText.StartsWith("http", StringComparison.Ordinal));
        Assert.Contains(body.Descendants<Run>(), run => run.RunProperties?.Bold is not null && run.InnerText == "What the issue is:");

        // The headings are numbered by their styles, 1, 1.1 and so on.
        var numbering = main.NumberingDefinitionsPart!.Numbering!;
        var headingList = numbering.Elements<DocumentFormat.OpenXml.Wordprocessing.AbstractNum>()
            .Single(list => list.Elements<Level>().Any(level => level.ParagraphStyleIdInLevel?.Val?.Value == "Heading1"));
        Assert.Equal("%1.%2", headingList.Elements<Level>().Single(level => level.LevelIndex!.Value == 1).LevelText!.Val!.Value);
        var heading1 = main.StyleDefinitionsPart!.Styles!.Elements<Style>().Single(style => style.StyleId == "Heading1");
        Assert.NotNull(heading1.StyleParagraphProperties!.NumberingProperties);

        // The contents: Word's TOC field, its entries linked to the headings' bookmarks, updated on opening.
        Assert.Contains(body.Descendants<FieldCode>(), code => code.Text.Contains("TOC \\o \"1-2\" \\h", StringComparison.Ordinal));
        var bookmarks = body.Descendants<BookmarkStart>().Select(mark => mark.Name!.Value).ToHashSet(StringComparer.Ordinal);
        var anchors = body.Descendants<Hyperlink>().Where(link => link.Anchor is not null).ToList();
        Assert.NotEmpty(anchors);
        Assert.All(anchors, link => Assert.Contains(link.Anchor!.Value!, bookmarks));
        Assert.StartsWith("1 ", anchors[0].InnerText, StringComparison.Ordinal);
        Assert.Contains(anchors, link => link.InnerText.StartsWith("3.1 By WCAG success criterion", StringComparison.Ordinal));
        Assert.True(main.DocumentSettingsPart!.Settings!.GetFirstChild<UpdateFieldsOnOpen>()!.Val!.Value);

        // The title in each page's header, and its number in the footer.
        var section = body.Elements<SectionProperties>().Single();
        Assert.Equal(12240U, section.GetFirstChild<PageSize>()!.Width!.Value);
        var header = (HeaderPart)main.GetPartById(section.GetFirstChild<HeaderReference>()!.Id!.Value!);
        Assert.Equal("Accessibility report: com.android.settings", header.Header!.InnerText);
        var footer = (FooterPart)main.GetPartById(section.GetFirstChild<FooterReference>()!.Id!.Value!);
        Assert.Equal([" PAGE ", " NUMPAGES "], footer.Footer!.Descendants<SimpleField>().Select(field => field.Instruction!.Value));

        // Nothing for the client's team about models.
        Assert.DoesNotContain("model", body.InnerText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The final report is manually inspected.", body.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientSummary_SaysOnlyThatAiIsUsed_AndThatTheReportIsManuallyInspected()
    {
        var (input, guidance, evidence) = Recording();
        var screen = Assert.Single(input.Screens.Screens);
        var empty = JsonDocument.Parse("{}").RootElement;
        var screenStep = new AcceptedStep(Guid.NewGuid(), "", empty,
            [new Claim("screen", 0, 0, Claim.ClaimKind, "Inferring from its heading, the screen holds the phone's settings.", ["E5"])]);
        var overall = new AcceptedStep(Guid.NewGuid(), "", empty,
            [new Claim("app", 0, 0, Claim.ClaimKind, "The app holds the phone's settings.", ["E4"])]);
        var model = new AndroidModelText(overall, new Dictionary<PhoneScreen, AcceptedStep> { [screen] = screenStep }, []);

        var markdown = AndroidReport.Write(input, guidance, evidence, model, Path.Combine(_folder, "pictures"), "pictures", DateTime.UtcNow, ReportAudience.Client);

        var document = ReportDocument.Parse(markdown);
        var headings = document.Blocks.Where(block => block.Kind == ReportBlockKind.Heading).Select(block => block.Text).ToList();
        Assert.Contains("Overview", headings);
        Assert.Contains("Summary of the screen", headings);
        Assert.DoesNotContain("How the summaries were written", headings);
        Assert.DoesNotContain(guidance.Sha256, markdown, StringComparison.Ordinal);
        var sentences = markdown.Split('.').Where(sentence => sentence.Contains("model", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(sentences);
        Assert.Contains("AI is used in generating this report, and the final report is manually inspected.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_GivesTheModelEachScreensFindings_AndWritesItsStatementsWithTheirReferences()
    {
        var (input, guidance, evidence) = Recording();
        var screen = Assert.Single(input.Screens.Screens);

        var (facts, references) = AndroidSummaryKind.ScreenInput(screen, evidence, guidance, input.Device);

        Assert.Equal(["E1", "E4", "E5", "E6"], references.Order(StringComparer.Ordinal));
        Assert.Contains("[E6] Finding 2, error, impact medium, no WCAG success criterion, best practice: Touch target smaller than Android's 48 by 48 dp. Item: Button \"Save\".", facts, StringComparison.Ordinal);
        Assert.Equal("screen-0001", AndroidSummaryKind.ScreenStepKey(screen));

        var empty = JsonDocument.Parse("{}").RootElement;
        var screenStep = new AcceptedStep(Guid.NewGuid(), "", empty,
        [
            new Claim("screen", 0, 0, Claim.ClaimKind, "Inferring from its heading, the screen holds the phone's settings.", ["E5"]),
            new Claim("issues", 0, 0, Claim.ClaimKind, "The Save button is smaller than Android recommends.", ["E6"]),
        ]);
        var overall = new AcceptedStep(Guid.NewGuid(), "", empty,
        [
            new Claim("app", 0, 0, Claim.UnknownKind, "What the app is for beyond its settings is unknown.", []),
        ]);
        var model = new AndroidModelText(overall, new Dictionary<PhoneScreen, AcceptedStep> { [screen] = screenStep }, []);

        var markdown = AndroidReport.Write(input, guidance, evidence, model, Path.Combine(_folder, "pictures"), "pictures", DateTime.UtcNow);

        var document = ReportDocument.Parse(markdown);
        Assert.Equal("Android accessibility summary: s", document.Title);
        var paragraphs = document.Blocks.Where(block => block.Kind == ReportBlockKind.Paragraph).Select(block => block.Text).ToList();
        Assert.Contains(paragraphs, text => text.Contains("The Save button is smaller than Android recommends", StringComparison.Ordinal));
        Assert.Contains("What the app is for beyond its settings is unknown.", paragraphs);
        Assert.Contains(document.Blocks, block => block.Kind == ReportBlockKind.Heading && block.Text == "Summary of the screen, inferred by a model");
        Assert.Contains(document.Blocks, block => block.Kind == ReportBlockKind.Heading && block.Text == "How the summaries were written");

        var (whole, wholeReferences) = AndroidSummaryKind.RecordingInput(input.Screens, evidence, guidance, input.Device, model.Screens);
        Assert.Contains("  (issues, inferred) The Save button is smaller than Android recommends. [E6]", whole, StringComparison.Ordinal);
        Assert.Contains("No WCAG success criterion, best practice: 1 finding, 1 error", whole, StringComparison.Ordinal);
        Assert.Contains("[E4] The recorder found the screen \"Settings\" of the app com.android.settings, named by its window title.\n[E1] The app com.android.settings: ", facts, StringComparison.Ordinal);
        Assert.Equal(["E1", "E2", "E3", "E5", "E6"], wholeReferences.Order(StringComparer.Ordinal));
        Assert.Contains("[E1] The app com.android.settings: ", whole, StringComparison.Ordinal);
        Assert.Contains("[E2] The recorder's counts of findings by WCAG success criterion:", whole, StringComparison.Ordinal);
    }

    [Fact]
    public void Kinds_AreRegistered_AndTheReportNeedsNoProfile()
    {
        Assert.Contains(AnalysisTaskKinds.All, kind => kind.Kind == AndroidReportKind.Name);
        Assert.Contains(AnalysisTaskKinds.All, kind => kind.Kind == AndroidSummaryKind.Name);
        Assert.Equal("Android report", AnalysisPlayer.KindName(AndroidReportKind.Name));
        Assert.Equal("Android summary", AnalysisPlayer.KindName(AndroidSummaryKind.Name));
    }

    private static ReportCanvas White(int width, int height)
    {
        var canvas = new ReportCanvas(width, height);
        Array.Fill(canvas.Rgb, 0xFFFFFFu);
        return canvas;
    }

    // The Settings window taken at 1 second and again at 3, its button
    // moved down 10 pixels, listed at 5 without being taken; a white frame
    // of the phone at 1.5 seconds.
    private (AndroidReportInput Input, AndroidGuidance Guidance, AndroidEvidence Evidence) Recording()
    {
        List<(long Time, string Channel, string EventType, object Payload)> records =
        [
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
            (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Items(0))),
            (At(3), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, Items(10))),
            (At(5), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(3, null)),
        ];
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var recording = new FakeRecords();
        var sequence = 0UL;
        foreach (var (time, channel, eventType, payload) in records)
        {
            sequence++;
            var record = RecorderEventFactory.Create("s", Collector, channel, sequence, time, eventType, payload);
            recording.Add(time, record);
            builder.Add((long)sequence, record);
        }

        var archiveBuilder = new SessionPlaybackArchiveBuilder(_folder, retainEvents: false);
        archiveBuilder.AddIndex(builder.Build());
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(10), 10 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));

        var framePath = Path.Combine(_folder, "frame.png");
        File.WriteAllBytes(framePath, ReportPictures.Png(White(1080, 2404)));
        var pictures = PhoneSnapshotPictures.OfFrames([new SessionVideoFrame(At(1.5), "frame.png", framePath, 1080, 2404)], archive.PhoneAccessibility);
        var device = Device();
        var tree = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, device, recording, pictures, TestContext.Current.CancellationToken);
        var audit = AndroidAudit.Of(tree, PhoneAuditLevel.AA, 5, null, null, archive.PhoneScreens, device);
        var guidance = AndroidGuidance.Load();
        var evidence = AndroidEvidence.Build(archive.PhoneScreens, audit, guidance, device);
        var input = new AndroidReportInput("s", 10 * Second, archive.PhoneScreens, device, audit, pictures,
            archive.PhoneAccessibility.ScreenAt, PhonePictures.Load, ["the phone's video, 1 frame"], null);
        return (input, guidance, evidence);
    }

    private static PhoneDeviceTimeline Device() =>
        new(
            [(0L, AndroidDeviceRecords.DensityEventType, JsonSerializer.SerializeToElement(
                new AndroidDisplayDensity("58191FDCQ00ABZ", AndroidDeviceRecords.StartReason, 390, null, [], null, 0, 0),
                JsonSerializerOptions.Web))],
            [],
            10 * Second);

    private static JsonNode Items(int moved) =>
        JsonSerializer.SerializeToNode(
            new AndroidTreeNode[]
            {
                new()
                {
                    Index = 0, WindowId = 512, PackageName = Settings, ClassName = "android.widget.FrameLayout",
                    Bounds = new AndroidNodeBounds(0, 0, 1080, 2404), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true,
                },
                new()
                {
                    Index = 1, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.Button",
                    ViewId = "com.android.settings:id/save", Text = "Save",
                    Bounds = new AndroidNodeBounds(100, 200 + moved, 198, 298 + moved), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true, Enabled = true, Clickable = true, Focusable = true,
                },
                new()
                {
                    Index = 2, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.TextView",
                    Text = "Settings", Bounds = new AndroidNodeBounds(100, 60, 600, 130), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true, Heading = true,
                },
            },
            JsonSerializerOptions.Web)!;

    private static AndroidAccessibilityTree Tree(long index, JsonNode? items)
    {
        var tree = JsonNode.Parse(AndroidAccessibilityTreeTests.TreeLine(
            index: index,
            everyWindow: index == 1,
            reason: index == 1 ? "requested" : "content-change"))!.AsObject();
        tree["windows"]![0]!["items"] = items;
        var (read, problem) = CompanionProtocol.ReadTree(tree.ToJsonString());
        Assert.Null(problem);
        return read!;
    }

    private sealed class FakeRecords : ISessionEventRecordSource
    {
        private readonly Dictionary<long, string> _records = [];

        public void Add(long time, RecorderEvent record) =>
            _records[time] = JsonSerializer.Serialize(new { payload = record.Payload });

        public string ReadEventJson(SessionTimelineEvent item) => _records[item.MonotonicNanoseconds];
    }
}
