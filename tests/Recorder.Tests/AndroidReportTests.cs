using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Database;
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

        // The contents: each entry a link to its heading's bookmark, with
        // a tab to a dotted right-aligned stop and a PAGEREF field to the
        // same bookmark, which Word updates on opening. No TOC field round
        // them, whose stored text LibreOffice would show unchanged.
        Assert.DoesNotContain(body.Descendants<FieldCode>(), code => code.Text.Contains("TOC", StringComparison.Ordinal));
        var bookmarks = body.Descendants<BookmarkStart>().Select(mark => mark.Name!.Value).ToHashSet(StringComparer.Ordinal);
        var anchors = body.Descendants<Hyperlink>().Where(link => link.Anchor is not null).ToList();
        Assert.Equal(11, anchors.Count);
        Assert.All(anchors, link =>
        {
            Assert.Contains(link.Anchor!.Value!, bookmarks);
            Assert.Single(link.Descendants<TabChar>());
            Assert.Equal($" PAGEREF {link.Anchor.Value} \\h ", Assert.Single(link.Descendants<FieldCode>()).Text);
        });
        var toc1 = main.StyleDefinitionsPart!.Styles!.Elements<Style>().Single(style => style.StyleId == "TOC1");
        var stop = Assert.Single(toc1.Descendants<TabStop>());
        Assert.Equal(TabStopValues.Right, stop.Val!.Value);
        Assert.Equal(TabStopLeaderCharValues.Dot, stop.Leader!.Value);
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

    [Fact]
    public void AFindingOnAReturnVisit_IsGivenOnceUnderTheScreen_WithEachVisitsPictureAndWhenSeen()
    {
        var (input, guidance, evidence) = Recording(returns: true);
        var settings = input.Screens.Screens.Single(screen => screen.Package == Settings);
        Assert.Equal(2, settings.Visits.Count);

        var repeated = Assert.Single(evidence.Repeated(settings));
        Assert.Equal(PhoneCheckNames.TouchTarget, repeated.Finding.Check);
        Assert.True(repeated.OnSeveralVisits);
        Assert.Equal(settings.Visits, repeated.Visits.Select(on => on.Visit));
        Assert.All(settings.Visits, visit => Assert.Empty(evidence.For(visit)));
        Assert.All(settings.Visits, visit => Assert.Equal(repeated.Number, Assert.Single(evidence.On(visit)).Numbered.Number));
        Assert.Single(evidence.For(settings));

        var markdown = AndroidReport.Write(input, guidance, evidence, null, Path.Combine(_folder, "pictures"), "pictures", DateTime.UtcNow);
        var document = ReportDocument.Parse(markdown);
        var headings = document.Blocks.Where(block => block.Kind == ReportBlockKind.Heading).Select(block => (block.Level, block.Text)).ToList();
        var section = headings.IndexOf((3, "Findings on more than one visit"));
        Assert.True(section > 0);
        Assert.Equal((4, repeated.Heading), headings[section + 1]);
        Assert.Single(headings, heading => heading.Text == repeated.Heading);
        // The grouped finding comes before the screen's visits.
        Assert.True(headings.FindIndex(heading => heading.Text.StartsWith("Visit 1", StringComparison.Ordinal)) > section);

        var first = repeated.Visits[0].Reference;
        var second = repeated.Visits[1].Reference;
        Assert.NotEqual(first, second);
        var table = document.Blocks.Single(block => block.Kind == ReportBlockKind.Table && block.Rows.Any(row => row[0] == "Item" && row[1].StartsWith("Button", StringComparison.Ordinal)));
        var seen = table.Rows.Single(row => row[0] == "Seen")[1];
        Assert.Equal($"On 2 visits: visit 1, twice, in 1 picture, at 00:00:01.500 [{first}]; visit 3, once, at 00:00:08.500 [{second}]", seen);
        // The same measurement on both visits is given once.
        Assert.DoesNotContain("Visit 1:", table.Rows.Single(row => row[0] == "Measured")[1], StringComparison.Ordinal);
        Assert.StartsWith($"Finding {repeated.Number}, on visit 1", document.Evidence[first].Text, StringComparison.Ordinal);
        Assert.StartsWith($"Finding {repeated.Number}, on visit 3", document.Evidence[second].Text, StringComparison.Ordinal);

        // A zoomed picture for each visit, and each visit's picture outlines it.
        var images = document.Blocks.Where(block => block.Kind == ReportBlockKind.Image).Select(block => block.Text).ToList();
        Assert.Contains($"Finding {repeated.Number}, Button \"Save\", on the screen Settings in visit 1, zoomed 3 times, outlined in cyan.", images);
        Assert.Contains($"Finding {repeated.Number}, Button \"Save\", on the screen Settings in visit 3, zoomed 3 times, outlined in cyan.", images);
        Assert.Equal(2, images.Count(text => text.StartsWith("The screen Settings at", StringComparison.Ordinal) && text.EndsWith($"with finding {repeated.Number} outlined in cyan and numbered.", StringComparison.Ordinal)));
        var paragraphs = document.Blocks.Where(block => block.Kind == ReportBlockKind.Paragraph).Select(block => block.Text).ToList();
        Assert.Equal(2, paragraphs.Count(text => text.StartsWith($"Finding {repeated.Number} on this visit is under", StringComparison.Ordinal)));

        // A screen summary's request is given the finding once, with both references.
        var (screenInput, references) = AndroidSummaryKind.ScreenInput(settings, evidence, guidance, input.Device);
        Assert.Contains($"[{first}] [{second}] Finding {repeated.Number}, error", screenInput, StringComparison.Ordinal);
        Assert.Contains("On visits 1 and 3 of the screen. Seen 3 times.", screenInput, StringComparison.Ordinal);
        Assert.Contains(first, references);
        Assert.Contains(second, references);
    }

    // recording-names.md: a named recording's report gives its name, then its session key.
    [Fact]
    public void ANamedRecordingsReport_GivesItsNameThenItsSessionKey()
    {
        var (input, guidance, evidence) = Recording(sessionKey: RecordingNames.Label("Sign in", "s"));

        var markdown = AndroidReport.Write(input, guidance, evidence, null, Path.Combine(_folder, "pictures"), "pictures", DateTime.UtcNow);

        var document = ReportDocument.Parse(markdown);
        Assert.Equal("Android accessibility report: Sign in (s)", document.Title);
        var about = document.Blocks.First(block => block.Kind == ReportBlockKind.Table && block.Rows.Any(row => row[0] == "Recording"));
        Assert.Equal("Sign in (s)", about.Rows.Single(row => row[0] == "Recording")[1]);
    }

    [Fact]
    public void AProjectsReport_GroupsTheSameScreenAcrossRecordings_AndEachFindingsVisitsWithTheirVersions()
    {
        var (one, guidance, _) = Recording(sessionKey: "first", version: "1.0", within: "first");
        var (two, _, _) = Recording(returns: true, sessionKey: "second", version: "1.1", within: "second");
        var evidence = AndroidEvidence.Build(
            [new ReportPart(one.Screens, one.Audit, one.Device), new ReportPart(two.Screens, two.Audit, two.Device)], guidance);
        var project = new AndroidProjectInput("Checkout", [one, two], ["third: it has no recording file."]);
        var markdown = AndroidReport.Write(project, guidance, evidence, Path.Combine(_folder, "pictures"), "pictures", DateTime.UtcNow);

        // Settings in both recordings is one screen; the launcher is the second.
        Assert.Equal(2, evidence.Recordings);
        Assert.Equal(2, evidence.Screens.Count);
        var settings = evidence.Screens[0];
        Assert.Equal((1, "Settings", 2, 3), (settings.Number, settings.Name, settings.Members.Count, settings.Visits.Count));
        Assert.Same(settings, evidence.ScreenOf(two.Screens.Screens.Single(screen => screen.Package == Settings)));
        Assert.Equal(2, evidence.RecordingOf(settings.Visits[1]));
        Assert.Equal("recording 2, visit 3", evidence.VisitName(settings.Visits[2]));

        // The touch target is one finding, seen on all three visits.
        var repeated = Assert.Single(evidence.Repeated(settings));
        Assert.Equal(PhoneCheckNames.TouchTarget, repeated.Finding.Check);
        Assert.Equal(settings.Visits, repeated.Visits.Select(on => on.Visit));

        var document = ReportDocument.Parse(markdown);
        Assert.Equal("Android accessibility report: project Checkout", document.Title);
        Assert.DoesNotContain('\u2014', markdown);
        var headings = document.Blocks.Where(block => block.Kind == ReportBlockKind.Heading).Select(block => (block.Level, block.Text)).ToList();
        Assert.Contains((2, "About this project"), headings);
        Assert.Contains((3, "The recordings"), headings);
        Assert.Single(headings, heading => heading.Text.StartsWith("Screen 1: Settings", StringComparison.Ordinal));
        Assert.Contains((3, "Recording 1, visit 1, 00:00:01.000 to 00:00:10.000"), headings);
        Assert.Contains((3, "Recording 2, visit 3, 00:00:08.000 to 00:00:10.000"), headings);
        Assert.Single(headings, heading => heading.Text == repeated.Heading);

        var recordings = document.Blocks.Single(block => block.Kind == ReportBlockKind.Table && block.Rows[0][0] == "Number");
        Assert.Equal([["1", "first"], ["2", "second"]], recordings.Rows.Skip(1).Select(row => row.Take(2).ToArray()));

        // When it was seen, by recording and visit, with the app's version in each.
        var table = document.Blocks.Single(block => block.Kind == ReportBlockKind.Table && block.Rows.Any(row => row[0] == "Item" && row[1].StartsWith("Button", StringComparison.Ordinal)));
        var seen = table.Rows.Single(row => row[0] == "Seen")[1];
        var references = repeated.Visits.Select(on => on.Reference).ToList();
        Assert.Equal(
            $"On 3 visits: recording 1, visit 1 (version 1.0), twice, in 1 picture, at 00:00:01.500 [{references[0]}]; " +
            $"recording 2, visit 1 (version 1.1), twice, in 1 picture, at 00:00:01.500 [{references[1]}]; " +
            $"recording 2, visit 3 (version 1.1), once, at 00:00:08.500 [{references[2]}]",
            seen);
        var about = document.Blocks.Single(block => block.Kind == ReportBlockKind.Table && block.Rows.Any(row => row[0] == "Project"));
        Assert.StartsWith("Recording 1: version 1.0 (code 1), targets API 36; Recording 2: version 1.1 (code 1), targets API 36.",
            about.Rows.Single(row => row[0] == "App " + Settings)[1], StringComparison.Ordinal);

        // Each recording's pictures are named apart, and the evidence by recording.
        var images = document.Blocks.Where(block => block.Kind == ReportBlockKind.Image).Select(block => block.Source!).ToList();
        Assert.Contains("pictures/recording-01-visit-0001-1500000000.png", images);
        Assert.Contains("pictures/recording-02-visit-0001-1500000000.png", images);
        Assert.Equal(images.Count, images.Distinct(StringComparer.Ordinal).Count());
        Assert.All(images, image => Assert.True(File.Exists(Path.Combine(_folder, image))));
        Assert.Equal("Recording 2, 00:00:08.500", document.Evidence[references[2]].Time);
        Assert.StartsWith("The app com.android.settings. In recording 1, version 1.0", document.Evidence["E1"].Text, StringComparison.Ordinal);
        Assert.Contains("| Ref | Recording and time | Evidence |", markdown, StringComparison.Ordinal);
        Assert.Contains("- Left out: third: it has no recording file.", markdown, StringComparison.Ordinal);

        // The client's Word document of the project is valid too.
        var client = AndroidReport.Write(project, guidance, evidence, Path.Combine(_folder, "pictures"), "pictures", DateTime.UtcNow, ReportAudience.Client);
        var bytes = WordReport.Write(client, _folder, AndroidReport.Labels);
        if (Environment.GetEnvironmentVariable("RECORDER_KEEP_PROJECT_DOCX") is { Length: > 0 } keep)
        {
            File.WriteAllBytes(keep, bytes);
        }

        using var stream = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(stream, false);
        var errors = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2016)
            .Validate(word, TestContext.Current.CancellationToken)
            .Select(error => error.Description + " at " + error.Path?.XPath);
        Assert.Equal("", string.Join("\n", errors));
    }

    [Fact]
    public void EachEvidenceEntry_HasAKindForAnMp4sChapters_AndTheReportListsItsTablesRows()
    {
        var (input, guidance, evidence) = Recording(returns: true);
        var markdown = AndroidReport.Write(input, guidance, evidence, null, Path.Combine(_folder, "pictures"), "pictures", DateTime.UtcNow);

        Assert.All(evidence.Entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Kind)));
        Assert.Contains(evidence.Entries, entry => entry.Kind == "counts of findings" && entry.Time is null);
        Assert.Contains(evidence.Entries, entry => entry.Kind.StartsWith("app ", StringComparison.Ordinal) && entry.Time is null);
        Assert.Contains(evidence.Entries, entry => entry.Kind.StartsWith("screen 1, ", StringComparison.Ordinal));
        Assert.Contains(evidence.Entries, entry => entry.Kind.StartsWith("visit 1 to ", StringComparison.Ordinal));
        Assert.Contains(evidence.Entries, entry => entry.Kind.StartsWith("finding 1", StringComparison.Ordinal));
        Assert.Contains(evidence.Entries, entry => entry.Kind.StartsWith("finding ", StringComparison.Ordinal) && entry.Kind.Contains(", on visit ", StringComparison.Ordinal));

        var listed = ReportEvidenceList.Listed(markdown);
        var section = markdown[markdown.LastIndexOf("## Evidence", StringComparison.Ordinal)..];
        Assert.Equal(section.Split('\n').Count(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\| E\d+ \|")), listed.Count);
        Assert.All(listed, reference => Assert.Contains($"| {reference} |", section, StringComparison.Ordinal));
    }

    [Fact]
    public void ARecordingsOwnReport_IsUnchanged_ByTheProjectsGrouping()
    {
        var (input, guidance, evidence) = Recording(returns: true);
        var alone = AndroidEvidence.Build([new ReportPart(input.Screens, input.Audit, input.Device)], guidance);
        var pictures = Path.Combine(_folder, "pictures");
        var written = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var markdown = AndroidReport.Write(input, guidance, evidence, null, pictures, "pictures", written);

        Assert.Equal(markdown, AndroidReport.Write(input, guidance, alone, null, pictures, "pictures", written));
        Assert.Equal(1, evidence.Recordings);
        Assert.DoesNotContain("Recording 1", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("recording-01-", markdown, StringComparison.Ordinal);
        Assert.Contains("| Ref | Time | Evidence |", markdown, StringComparison.Ordinal);
    }

    // The Settings window taken at 1 second and again at 3, its button
    // moved down 10 pixels, listed at 5 without being taken; a white frame
    // of the phone at 1.5 seconds.
    private (AndroidReportInput Input, AndroidGuidance Guidance, AndroidEvidence Evidence) Recording(
        bool returns = false, string sessionKey = "s", string? version = null, string? within = null)
    {
        var folder = within is null ? _folder : Path.Combine(_folder, within);
        Directory.CreateDirectory(folder);
        List<(long Time, string Channel, string EventType, object Payload)> records =
        [
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
            (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Items(0))),
            (At(3), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, Items(10))),
            (At(5), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(3, null)),
        ];
        if (returns)
        {
            // The launcher in front at 6 seconds, then Settings again at 8.
            records.Add((At(6), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(4, Launcher(), "Home")));
            records.Add((At(8), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(5, Items(0))));
        }

        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var recording = new FakeRecords();
        var sequence = 0UL;
        foreach (var (time, channel, eventType, payload) in records)
        {
            sequence++;
            var record = RecorderEventFactory.Create(sessionKey, Collector, channel, sequence, time, eventType, payload);
            recording.Add(time, record);
            builder.Add((long)sequence, record);
        }

        var archiveBuilder = new SessionPlaybackArchiveBuilder(folder, retainEvents: false);
        archiveBuilder.AddIndex(builder.Build());
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", sessionKey, "completed", started, started.AddSeconds(10), 10 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));

        var framePath = Path.Combine(folder, "frame.png");
        File.WriteAllBytes(framePath, ReportPictures.Png(White(1080, 2404)));
        List<SessionVideoFrame> frames = [new SessionVideoFrame(At(1.5), "frame.png", framePath, 1080, 2404)];
        if (returns)
        {
            frames.Add(new SessionVideoFrame(At(6.5), "frame.png", framePath, 1080, 2404));
            frames.Add(new SessionVideoFrame(At(8.5), "frame.png", framePath, 1080, 2404));
        }

        var pictures = PhoneSnapshotPictures.OfFrames(frames, archive.PhoneAccessibility);
        var device = Device(version);
        var tree = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, device, recording, pictures, TestContext.Current.CancellationToken);
        var audit = AndroidAudit.Of(tree, PhoneAuditLevel.AA, 5, null, null, archive.PhoneScreens, device);
        var guidance = AndroidGuidance.Load();
        var evidence = AndroidEvidence.Build(archive.PhoneScreens, audit, guidance, device);
        var input = new AndroidReportInput(sessionKey, 10 * Second, archive.PhoneScreens, device, audit, pictures,
            archive.PhoneAccessibility.ScreenAt, PhonePictures.Load, ["the phone's video, 1 frame"], null)
        {
            StartedUtc = started,
        };
        return (input, guidance, evidence);
    }

    // The app's version, when given, read at the stop as the recorder does.
    private static PhoneDeviceTimeline Device(string? version = null)
    {
        List<(long, string, JsonElement)> records =
        [
            (0L, AndroidDeviceRecords.DensityEventType, JsonSerializer.SerializeToElement(
                new AndroidDisplayDensity("58191FDCQ00ABZ", AndroidDeviceRecords.StartReason, 390, null, [], null, 0, 0),
                JsonSerializerOptions.Web)),
        ];
        if (version is not null)
        {
            records.Add((10 * Second, AndroidDeviceRecords.AppVersionsEventType, JsonSerializer.SerializeToElement(
                new AndroidAppVersions("58191FDCQ00ABZ", [new AndroidAppVersion(Settings, true, version, 1, 24, 36, null, null, [])], [], 10 * Second, 10 * Second),
                JsonSerializerOptions.Web)));
        }

        return new PhoneDeviceTimeline(records, [], 10 * Second);
    }

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

    private static JsonNode Launcher() =>
        JsonSerializer.SerializeToNode(
            new AndroidTreeNode[]
            {
                new()
                {
                    Index = 0, WindowId = 512, PackageName = "com.google.android.apps.nexuslauncher", ClassName = "android.widget.FrameLayout",
                    Bounds = new AndroidNodeBounds(0, 0, 1080, 2404), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true,
                },
            },
            JsonSerializerOptions.Web)!;

    private static AndroidAccessibilityTree Tree(long index, JsonNode? items, string? title = null)
    {
        var tree = JsonNode.Parse(AndroidAccessibilityTreeTests.TreeLine(
            index: index,
            everyWindow: index == 1,
            reason: index == 1 ? "requested" : "content-change"))!.AsObject();
        tree["windows"]![0]!["items"] = items;
        if (title is not null)
        {
            tree["windows"]![0]!["title"] = title;
        }

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
