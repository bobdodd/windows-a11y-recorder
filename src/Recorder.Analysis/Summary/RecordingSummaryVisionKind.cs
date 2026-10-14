using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Recorder.Database.RecordingFiles;

namespace Recorder.Analysis.Summary;

/// <summary>
/// One moment as the vision stage described it: the moment, its picture's
/// reference, its reasons' references, its recreated page and its tiles'
/// references, and the accepted step that describes it: the combined
/// description, or the frame's alone where the page was not recreated.
/// </summary>
public sealed record MomentDescription(
    Moment Moment,
    string FrameReference,
    IReadOnlyList<string> ReasonReferences,
    AcceptedStep Step,
    RecreatedPage? Page = null,
    IReadOnlyList<string>? TileReferences = null)
{
    /// <summary>Whether <see cref="Step"/> is the combined description, with its three sections.</summary>
    public bool Combined => Page is { Tiles.Count: > 0 };
}

/// <summary>A visit's part of the vision stage: its moments as described, how many were left out, and the enhanced description.</summary>
public sealed record VisitVision(IReadOnlyList<MomentDescription> Moments, int MomentsLeftOut);

/// <summary>What the report adds for the vision stage.</summary>
public sealed record VisionReportParts(IReadOnlyDictionary<int, VisitVision> Visits, MomentLimits Limits, Guid SummaryTaskId, int ImageTokensPerRequest = RecordingSummaryVisionKind.DefaultImageTokensPerRequest);

/// <summary>
/// The recording summary's second stage: each visit's moments, chosen by
/// the recorder's code, each described by a model from its captured frame
/// and the facts around it (step 4a); then from its page, recreated at the
/// moment's frame and pictured whole in tiles, and the two descriptions
/// combined (step 4b); then each visit's description written again with
/// them, and the whole recording's, written as summary-with-vision.md. It
/// builds on a finished summary of the same recording, the task it follows.
/// See docs/architecture/recording-summary.md, "Stage 2: the summary with
/// vision".
/// </summary>
public sealed partial class RecordingSummaryVisionKind : IAnalysisTaskKind
{
    public const string Name = "recording-summary-vision";

    public const string FileName = "summary-with-vision.md";

    public const string MomentTemplate = "recording-summary-vision-moment";

    public const string VisitTemplate = "recording-summary-vision-visit";

    public const string RecordingTemplate = "recording-summary-vision-recording";

    // Version 2 of the page, combined, visit, and recording templates, from
    // the first run of step 4b on the target machine, 2026-10-14: each
    // group of tiles restated the facts it was given before describing its
    // tiles, the combined description copied both groups' statements in
    // order and was stopped at the second group's first, the same as the
    // first's, and it cited "[E24]" with its brackets. The page template
    // now describes only what the tiles show; the combined one states each
    // thing once, as an overview; and the combined, visit, and recording
    // templates say to cite a reference without its brackets (agreed with
    // the owner, 2026-10-14; recording-summary.md, "Step 4b").
    // Each version was raised by one on 2026-10-14 when the statement's
    // fields were put in the order text, kind, evidence, so that the model
    // writes a statement before saying whether it is a claim: the answer
    // form is held to the schema's order, and twice the model began a
    // statement as a claim and then wrote that something was unknown
    // (agreed with the owner; analysis-runner.md, decision 7).
    public const int TemplateVersion = 3;

    // Version 3 of the combined template, from the second run of step 4b on
    // the target machine, 2026-10-14: moments 1 to 8 were accepted, and
    // moment 9's combined description was refused for a statement that
    // said something was unknown but was of kind claim, citing nothing.
    // The sentence is in the combined template only, so that the other
    // steps' requests are unchanged and a run again can reuse them (agreed
    // with the owner, 2026-10-14; recording-summary.md, "Step 4b").
    // Version 4 puts the statement's fields in the order text, kind,
    // evidence, as TemplateVersion above.
    public const int CombinedTemplateVersion = 4;

    /// <summary>What kind a statement of what is unknown is, said in the combined template from version 3.</summary>
    public const string UnknownIsUnknown =
        "A statement that says something is unknown, or that cites no reference, is of kind unknown; every claim cites at least one reference. ";

    /// <summary>How a reference is cited, said in the templates of version 2.</summary>
    public const string CiteWithoutBrackets = "Cite each reference as it is written without its brackets, such as \"E3\". ";

    // Version 2 asks for an overview of the screen, after a moment showing
    // the recorder's own player was answered with every control and time
    // on it, 128 statements, until the output limit (recording-summary.md,
    // "Stage 2 implementation status").
    // Version 3 puts the statement's fields in the order text, kind,
    // evidence, as TemplateVersion above.
    public const int MomentTemplateVersion = 3;

    public static readonly IReadOnlyList<string> MomentSections = ["screen", "change"];

    public const string PageTemplate = "recording-summary-vision-page";

    public const string CombinedTemplate = "recording-summary-vision-moment-combined";

    public static readonly IReadOnlyList<string> PageSections = ["page"];

    public static readonly IReadOnlyList<string> CombinedSections = ["screen", "page", "change"];

    /// <summary>
    /// The image tokens of the tiles in one request, a setting: on the
    /// target machine the context is 16,384 tokens and the answer up to
    /// 8,192, and 4,096 is the most llama.cpp gives one Qwen-VL picture
    /// (see <see cref="TileGroups.ImageTokens"/>). A tile of 922 by 925
    /// pixels is 841 tokens, so four go in one request.
    /// </summary>
    public const int DefaultImageTokensPerRequest = 4096;

    public static readonly IReadOnlyList<string> VisitSections = ["page", "purpose", "activity", "journey"];

    private readonly Func<string, string, CancellationToken, Task<(Recorder.Session.SessionPlaybackArchive Archive, RecordingFileDocuments Documents)>> _open;

    public RecordingSummaryVisionKind()
        : this(OpenAsync)
    {
    }

    /// <param name="open">Opens a recording file, given the session folder and the file's path.</param>
    public RecordingSummaryVisionKind(Func<string, string, CancellationToken, Task<(Recorder.Session.SessionPlaybackArchive Archive, RecordingFileDocuments Documents)>> open)
    {
        _open = open;
    }

    public string Kind => Name;

    // Version 2 is step 4b: the recreated page's tiles, and the combined
    // description of each moment.
    public int Version => 2;

    /// <summary>The summary's collation limits, which must be the summary's own for its references to match.</summary>
    public CollationLimits Limits { get; init; } = CollationLimits.Default;

    public MomentLimits Moments { get; init; } = MomentLimits.Default;

    public int ImageTokensPerRequest { get; init; } = DefaultImageTokensPerRequest;

    /// <summary>
    /// Makes what recreates each moment's page, from the recording's
    /// documents and archive. By default the instrumented Chromium where
    /// the player finds it, without a window, its recreations under the
    /// worker's folder.
    /// </summary>
    public Func<RecordingFileDocuments, Recorder.Session.SessionPlaybackArchive, IMomentPages> Pages { get; init; } =
        (documents, archive) => new RecordedMomentPages(
            documents,
            archive,
            Recorder.Recreation.RecreationBrowser.DefaultExecutablePath(AppContext.BaseDirectory),
            Path.Combine(AnalysisLocation.Directory, "recreations"));

    private static async Task<(Recorder.Session.SessionPlaybackArchive, RecordingFileDocuments)> OpenAsync(
        string sessionDirectory,
        string filePath,
        CancellationToken cancellationToken)
    {
        var opened = await RecordingFilePlayback.OpenAsync(sessionDirectory, filePath, cancellationToken).ConfigureAwait(false);
        return (opened.Archive, opened.Documents ?? throw new StepFailedException("The recording file's browser documents could not be read."));
    }

    public const string MomentInstructions =
        "You describe one moment of a page visit from an accessibility test recording, for an accessibility auditor. " +
        "You are given a captured frame, a picture of the whole screen at that moment, labelled with its evidence reference; " +
        "why the recorder chose the moment; and the recorder's facts about the visit up to that moment, each beginning with an evidence reference such as [E3]. " +
        "Describe only what the picture and the facts support. Name sites, programs, windows, and pages only as the picture or the facts name them. " +
        "Where they do not settle something, say that it is unknown; do not guess. " +
        "A captured frame shows what was on the screen, not what the participant saw: say that the participant saw something only if the participant's capabilities, " +
        "as given, say that they work by sight; otherwise do not say what the participant saw. " +
        "What NVDA had to announce is the recorder's inference from the page, not a recording of speech: never say that NVDA said it or that the participant heard it. " +
        "Assistive technology observed running shows what was running, not what the participant is able to do. " +
        "Typed text is test data from short-lived test accounts and may be quoted as recorded. " +
        "Summarise what is on the screen as an overview; do not list each control, label, or time shown. " +
        "Answer in JSON with two sections: \"screen\", what was on the screen: the page and its layout, imagery, colour, and emphasis, " +
        "what was open, such as a dialog, a menu, or a popup, which part of the page was in view, and any other window in view; " +
        "and \"change\", what changed at this moment, as the reasons and facts give it, and what the picture shows of it. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. Each section holds only its own paragraphs. " +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "with every statement about the picture citing the picture's reference, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []} for something the picture and facts leave open. " +
        "Cite only references that appear in the input. Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";

    public const string PageInstructions =
        "You describe part of a web page from an accessibility test recording, for an accessibility auditor. " +
        "You are given tiles: pictures of the page as the recorder rebuilt it from what it recorded at one moment, top to bottom, " +
        "each labelled with its evidence reference and its place on the page in CSS pixels; which part of the page was on the screen at that moment; " +
        "why the recorder chose the moment; and the recorder's facts about it, each beginning with an evidence reference such as [E3]. " +
        "A tile is a recreation, not the page itself: where the recording lacks something, such as an image that was not loaded, the tile lacks it too, " +
        "so say that it is missing in the recreation, not that the page lacked it. " +
        "Describe only what the tiles show. Do not restate the facts you are given, such as the page's address, title, or size, its scroll position, " +
        "which tiles were on the screen, the images not loaded, or the requests refused: the moment's description is given them directly. " +
        "Describe the page in these tiles as an overview, top to bottom: its regions, headings, imagery, colour, and emphasis, " +
        "and every message, notice, alert, toast, error, dialog, or popup, however small, whether or not it was in the part on the screen. " +
        "For each, say where it is on the page and whether it was in the part on the screen. " +
        "Describe only what the tiles and the facts support. Name sites and pages only as the tiles or the facts name them. " +
        "Where they do not settle something, say that it is unknown; do not guess. " +
        "Never say what the participant saw, heard, or did from the tiles: they show the page, not the participant. " +
        "Do not list each control, link, or line of text. " +
        "Answer in JSON with one section: \"page\", what these tiles show of the page. " +
        "The section is a list of paragraphs, and each paragraph a list of statements. " +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "with every statement about a tile citing that tile's reference, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []} for something the tiles and facts leave open. " +
        "Cite only references that appear in the input. Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";

    public const string CombinedInstructions =
        "You describe one moment of a page visit from an accessibility test recording, for an accessibility auditor, " +
        "combining two descriptions of it into one. You are given why the recorder chose the moment, with its facts, each beginning with an evidence reference such as [E3]; " +
        "a model's description of the captured frame, a picture of the whole screen at the moment, with the picture's reference; " +
        "and a model's descriptions of the page as the recorder rebuilt it at the moment and pictured it whole in tiles, with each tile's reference; " +
        "and what the recreation lacked. Both descriptions are inferences, not facts. " +
        "The captured frame is the screen as recorded; the tiles are a recreation of the page, which may lack what the recording lacks, such as images. " +
        "Where the two disagree, say so, citing both, and do not settle it. " +
        "Combine the descriptions; do not copy them. State each thing once: where statements say the same thing, write one statement citing all their references. " +
        "Describe the page as an overview of its regions from top to bottom, not tile by tile, and do not list each control, link, or line of text. " +
        "Describe only what these support, name sites, programs, windows, and pages only as they are named, and say that something is unknown where they do not settle it. " +
        "A captured frame shows what was on the screen, not what the participant saw: say that the participant saw something only if the participant's capabilities, " +
        "as given, say that they work by sight; otherwise do not say what the participant saw. " +
        "What NVDA had to announce is the recorder's inference from the page, not a recording of speech: never say that NVDA said it or that the participant heard it. " +
        "Answer in JSON with three sections: \"screen\", what was on the screen, from the captured frame: what was open, which part of the page was in view, and any other window in view; " +
        "\"page\", the page as recreated, top to bottom, including the parts not on the screen, with every message, notice, alert, toast, error, dialog, or popup on it and where it is; " +
        "and \"change\", what changed at this moment, as the reasons and facts give it, and what the frame and the tiles show of it, whether or not it was on the screen. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. Each section holds only its own paragraphs. " +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "with a statement from a description citing the picture or the tiles that description cites, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []}. " + UnknownIsUnknown + "Cite only references that appear in the input. " + CiteWithoutBrackets + "Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";

    public const string VisitInstructions =
        "You describe one page visit from an accessibility test recording, for an accessibility auditor, adding what captured frames of the screen show " +
        "to a description written from the recorder's facts alone. " +
        "You are given the recorder's facts about the visit, each beginning with an evidence reference such as [E3]; a model's description of the visit from those facts; " +
        "and a model's descriptions of moments of the visit, each from a captured frame of the whole screen with its own reference, " +
        "and, where the page was recreated, from tiles picturing the whole page as the recorder rebuilt it, each with its own reference. " +
        "Both descriptions are inferences, not facts. " +
        "Describe only what these support, name sites, programs, windows, and pages only as they are named, and say that something is unknown where they do not settle it. " +
        "A captured frame shows what was on the screen, not what the participant saw: say that the participant saw something only if the participant's capabilities, " +
        "as given, say that they work by sight; otherwise do not say what the participant saw. " +
        "What NVDA had to announce is the recorder's inference from the page, not a recording of speech: never say that NVDA said it or that the participant heard it. " +
        "Assistive technology observed running shows what was running, not what the participant is able to do. " +
        "Answer in JSON with four sections: \"page\", what the page is and what it holds, including its layout, imagery, colour, and emphasis; " +
        "\"purpose\", what the page appears to be for; \"activity\", what the participant did there, in order, with repeated actions grouped into one statement; " +
        "and \"journey\", the participant's course through the visit moment by moment: which part of the page was on the screen, what was open, and what changed. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. Each section holds only its own paragraphs. " +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "with a statement from a moment's description citing the picture or tiles it cites, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []}. Cite only references that appear in the input. " + CiteWithoutBrackets + "Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";

    public static readonly string RecordingInstructions = RecordingSummaryKind.RecordingInstructions.Replace(
        "and a model's descriptions of each visit, which are inferences and not facts.",
        "and a model's descriptions of each visit, from the recorder's facts, from captured frames of the screen, and from the pages as the recorder rebuilt them, which are inferences and not facts.",
        StringComparison.Ordinal).Replace(
        "Cite only references that appear in the input. ",
        "Cite only references that appear in the input. " + CiteWithoutBrackets,
        StringComparison.Ordinal);

    [GeneratedRegex(@"^\[(E\d+)\] (\d\d):(\d\d):(\d\d)\.(\d\d\d) ")]
    private static partial Regex TimedLine();

    [GeneratedRegex(@"\[(E\d+)\]")]
    private static partial Regex Reference();

    public async Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Task.AfterTaskId is not { } summaryTask)
        {
            throw new StepFailedException("Vision is added to a summary of the recording, and this task names none. Summarise the recording first.");
        }

        var (archive, documents, session, filePath) = await RecordingSummaryKind.OpenRecordingAsync(context, _open, cancellationToken).ConfigureAwait(false);
        try
        {
            var evidence = new RecordedSummaryEvidence(archive, documents, filePath);
            await DescribeAsync(context, summaryTask, session, evidence, evidence, Pages(documents, archive), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (archive.Timeline as IDisposable)?.Dispose();
        }
    }

    internal async Task DescribeAsync(
        AnalysisTaskContext context,
        Guid summaryTask,
        string session,
        ISummaryEvidence evidence,
        IMomentEvidence momentEvidence,
        IMomentPages pages,
        CancellationToken cancellationToken)
    {
        var task = context.Task;
        var capabilities = await RecordingSummaryKind.CapabilitiesAsync(context, cancellationToken).ConfigureAwait(false);
        var collation = SummaryCollator.Collate(evidence, capabilities, Limits);
        if (collation.Visits.Count == 0)
        {
            throw new StepFailedException("The recording has no page visits, so there is nothing to add vision to.");
        }

        // The summary's descriptions are used only if they were written
        // from the same facts, with the same references.
        var described = new List<(VisitCollation Visit, AcceptedStep Step)>();
        foreach (var visit in collation.Visits)
        {
            var key = RecordingSummaryKind.VisitStepKey(visit);
            var given = await context.Catalog.AcceptedInputReferencesAsync(summaryTask, key, cancellationToken).ConfigureAwait(false);
            var step = await context.Catalog.AcceptedStepAsync(summaryTask, key, cancellationToken).ConfigureAwait(false);
            if (given is null || step is null)
            {
                throw new StepFailedException($"The summary has no accepted description of visit {visit.Visit.Number}, so vision cannot be added to it.");
            }

            if (!RecordingSummaryKind.VisitReferences(collation, visit).SetEquals(given))
            {
                throw new StepFailedException(
                    $"The recorder's facts for visit {visit.Visit.Number} are not the ones its summary was written from, so vision cannot be added to that summary. " +
                    "Summarise the recording again, then add vision to the new summary.");
            }

            described.Add((visit, step));
        }

        var chosen = collation.Visits.ToDictionary(visit => visit.Visit.Number, visit => MomentChooser.Choose(evidence, momentEvidence, visit.Visit, Moments));
        if (chosen.Values.All(moments => moments.Moments.Count == 0))
        {
            throw new StepFailedException("No captured frames were recorded during the page visits, so there is nothing to add vision from.");
        }

        var register = collation.Evidence;
        var parts = new Dictionary<int, VisitVision>();
        var enhanced = new List<(VisitCollation Visit, AcceptedStep Step)>();
        foreach (var (visit, summary) in described)
        {
            var moments = chosen[visit.Visit.Number];
            var descriptions = new List<MomentDescription>();
            var previous = visit.Visit.Start.Time - 1;
            foreach (var moment in moments.Moments)
            {
                var frame = moment.Frame;
                var frameReference = register.Add(frame.Time, FrameText(frame), null, $"frame:{frame.Time.ToString(CultureInfo.InvariantCulture)}");
                var reasonReferences = moment.Reasons.Select(reason => register.Add(
                    reason.Time,
                    "Derived by the recorder, a reason for a moment: " + reason.Text,
                    reason.EventId,
                    $"moment:{visit.Visit.Number}:{reason.Kind}:{reason.Time.ToString(CultureInfo.InvariantCulture)}")).ToList();
                var (input, references) = MomentInput(collation, visit, moment, frameReference, reasonReferences, previous);
                var key = $"{RecordingSummaryKind.VisitStepKey(visit)}-moment-{moment.Number.ToString("D2", CultureInfo.InvariantCulture)}";
                var frameStep = await context.RunStepAsync(
                    key,
                    new ModelRequest(MomentTemplate, MomentTemplateVersion, MomentInstructions, input,
                        [new ModelImage(frameReference, MediaType(frame.AbsolutePath), await ReadFrameAsync(frame, cancellationToken).ConfigureAwait(false))],
                        MomentSections, references),
                    cancellationToken).ConfigureAwait(false);

                // The page at the moment's frame, recreated and pictured
                // whole, then described in groups of tiles, then the two
                // descriptions combined (decision 7).
                var relativeFolder = $"analysis/{task.TaskId:D}/pages";
                var page = await pages.RecreateAsync(
                    visit.Visit, moment, Path.Combine(session, "analysis", task.TaskId.ToString("D"), "pages"), relativeFolder, key, cancellationToken).ConfigureAwait(false);
                if (page.Tiles.Count == 0)
                {
                    descriptions.Add(new MomentDescription(moment, frameReference, reasonReferences, frameStep, page, []));
                    previous = frame.Time;
                    continue;
                }

                var tileReferences = page.Tiles.Select(tile => register.Add(
                    frame.Time, TileText(moment, page, tile), null,
                    $"tile:{frame.Time.ToString(CultureInfo.InvariantCulture)}:{tile.Number.ToString(CultureInfo.InvariantCulture)}")).ToList();
                var groups = TileGroups.Group(page.Tiles, ImageTokensPerRequest);
                var pageSteps = new List<(IReadOnlyList<PageTile> Tiles, AcceptedStep Step)>();
                for (var index = 0; index < groups.Count; index++)
                {
                    var group = groups[index];
                    var (pageInput, pageReferences) = PageInput(collation, visit, moment, page, group, tileReferences, reasonReferences, index + 1, groups.Count);
                    var images = new List<ModelImage>();
                    foreach (var tile in group)
                    {
                        images.Add(new ModelImage(tileReferences[tile.Number - 1], "image/png", await ReadFileAsync(tile.AbsolutePath, "tile", cancellationToken).ConfigureAwait(false)));
                    }

                    var pageStep = await context.RunStepAsync(
                        $"{key}-page-{(index + 1).ToString("D2", CultureInfo.InvariantCulture)}",
                        new ModelRequest(PageTemplate, TemplateVersion, PageInstructions, pageInput, images, PageSections, pageReferences),
                        cancellationToken).ConfigureAwait(false);
                    pageSteps.Add((group, pageStep));
                }

                var (combinedInput, combinedReferences) = CombinedInput(collation, visit, moment, frameReference, reasonReferences, frameStep, page, tileReferences, pageSteps);
                var combined = await context.RunStepAsync(
                    $"{key}-described",
                    new ModelRequest(CombinedTemplate, CombinedTemplateVersion, CombinedInstructions, combinedInput, [], CombinedSections, combinedReferences),
                    cancellationToken).ConfigureAwait(false);
                descriptions.Add(new MomentDescription(moment, frameReference, reasonReferences, combined, page, tileReferences));
                previous = frame.Time;
            }

            parts[visit.Visit.Number] = new VisitVision(descriptions, moments.LeftOut);
            var (visitInput, visitReferences) = VisitInput(collation, visit, summary, descriptions, moments.LeftOut);
            var visitStep = await context.RunStepAsync(
                RecordingSummaryKind.VisitStepKey(visit),
                new ModelRequest(VisitTemplate, TemplateVersion, VisitInstructions, visitInput, [], VisitSections, visitReferences),
                cancellationToken).ConfigureAwait(false);
            enhanced.Add((visit, visitStep));
        }

        var (recordingInput, recordingReferences) = RecordingSummaryKind.RecordingInput(
            collation,
            enhanced,
            "Each visit as a model described it from the recorder's facts and from captured frames of the screen. These are inferences, not facts:");
        var overall = await context.RunStepAsync(
            "recording",
            new ModelRequest(RecordingTemplate, TemplateVersion, RecordingInstructions, recordingInput, [], RecordingSummaryKind.RecordingSections, recordingReferences),
            cancellationToken).ConfigureAwait(false);

        var recording = await context.Catalog.RecordingAsync(task.RecordingId, cancellationToken).ConfigureAwait(false);
        var attempts = await context.Catalog.AttemptsAsync(task.TaskId, cancellationToken).ConfigureAwait(false);
        var report = SummaryReport.Write(
            recording?.SessionKey ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(session)),
            collation,
            capabilities,
            enhanced,
            overall,
            attempts,
            Limits,
            DateTime.UtcNow,
            new VisionReportParts(parts, Moments, summaryTask, ImageTokensPerRequest));
        await RecordingSummaryKind.WriteReportAsync(context, session, FileName, report, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A captured frame as the evidence list gives it.</summary>
    public static string FrameText(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var magnified = frame.Magnification is > 1 and var level
            ? string.Create(CultureInfo.InvariantCulture, $", recorded with full screen magnification at {level * 100:0}%")
            : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"The captured frame at {SummaryCollator.Time(frame.Time)}: the whole screen, {frame.Width} by {frame.Height} pixels{magnified}, file {frame.Path}");
    }

    /// <summary>
    /// A tile of a recreated page as the evidence list gives it, with its
    /// file. A model's request gives it without the file, as the file is in
    /// the task's own folder and would make the request differ at every run
    /// (owner, 2026-10-14; analysis-runner.md, "Reusing steps when run
    /// again").
    /// </summary>
    public static string TileText(Moment moment, RecreatedPage page, PageTile tile, bool withFile = true)
    {
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(tile);
        return string.Concat(
            FormattableString.Invariant($"Tile {tile.Number} of {page.Tiles.Count} of the page as the recorder rebuilt it at the captured frame at {SummaryCollator.Time(moment.Frame.Time)}: ") +
            FormattableString.Invariant($"CSS pixels {tile.Top:0} to {tile.Top + tile.Height:0} from the top of a page {page.Height:0} high and {page.Width:0} wide, ") +
            FormattableString.Invariant($"pictured at {tile.PixelWidth} by {tile.PixelHeight} pixels{(InView(page, tile) ? ", part of it on the screen at the frame" : ", none of it on the screen at the frame")}{(withFile ? $", file {tile.Path}" : "")}. ") +
            "A recreation from the recorded evidence, not the page itself: what the recording lacks, the recreation lacks");
    }

    // Whether any of a tile was in the viewport, as the recreation was
    // scrolled to the recorded position.
    internal static bool InView(RecreatedPage page, PageTile tile) =>
        tile.Top < page.ScrollY + page.ViewportHeight && tile.Top + tile.Height > page.ScrollY;

    // What the recreation lacked and how it was made, for the model and the report.
    internal static string RecreationNotes(RecreatedPage page)
    {
        var notes = new StringBuilder();
        notes.Append(page.Built
            ? "The recreation was reported built and painted, with its fonts ready. "
            : string.Create(CultureInfo.InvariantCulture, $"The recreation was not reported built within {RecordedMomentPages.BuildTimeout.TotalSeconds:0} s; the tiles are of the page as it was then. "));
        notes.Append(page.ImagesNotLoaded.Count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"All {page.Images} of its images were loaded. ")
            : string.Create(CultureInfo.InvariantCulture,
                $"{page.ImagesNotLoaded.Count} of its {page.Images} images were not loaded in the recreation, so they are blank or missing in the tiles: ") +
              string.Join(", ", page.ImagesNotLoaded.Select(address => address.Length == 0 ? "an image with no address" : address)) + ". ");
        notes.Append(string.Create(CultureInfo.InvariantCulture,
            $"{page.RefusedRequests} of the page's requests were refused, as no answer to them was recorded."));
        return notes.ToString();
    }

    // Which part of the page was on the screen, and the magnification.
    internal static string InViewText(Moment moment, RecreatedPage page, IReadOnlyList<string> tileReferences)
    {
        var shown = page.Tiles.Where(tile => InView(page, tile)).Select(tile => $"tile {tile.Number} [{tileReferences[tile.Number - 1]}]").ToList();
        var magnified = moment.Frame.Magnification is > 1 and var level
            ? string.Create(CultureInfo.InvariantCulture, $" The screen was recorded with full screen magnification at {level * 100:0}%, so less of the page may have been on the screen than the viewport holds.")
            : "";
        return string.Concat(
            FormattableString.Invariant($"At the frame the page was scrolled to {page.ScrollY:0} CSS pixels from the top and {page.ScrollX:0} from the left, with a viewport {page.ViewportWidth:0} by {page.ViewportHeight:0}, ") +
            FormattableString.Invariant($"so the part on the screen was CSS pixels {page.ScrollY:0} to {page.ScrollY + page.ViewportHeight:0}: {(shown.Count == 0 ? "no tile" : string.Join(", ", shown))}.{magnified}"));
    }

    // A group of tiles' request: the capabilities, the visit, why the
    // moment was chosen, the part on the screen, what the recreation
    // lacked, and the tiles with their places on the page.
    internal static (string Input, HashSet<string> References) PageInput(
        RecordingCollation collation,
        VisitCollation visit,
        Moment moment,
        RecreatedPage page,
        IReadOnlyList<PageTile> group,
        IReadOnlyList<string> tileReferences,
        IReadOnlyList<string> reasonReferences,
        int number,
        int count)
    {
        var references = new HashSet<string>(RecordingSummaryKind.CapabilityReferences(collation), StringComparer.Ordinal);
        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"Page visit {visit.Visit.Number}, derived by the recorder: {visit.Visit.Start.Url}")
            .Append(visit.Title is { Length: > 0 } title ? $", titled \"{title.Replace("\"", "'", StringComparison.Ordinal)}\"" : ", with no title recorded")
            .Append(".\n\n")
            .Append(CultureInfo.InvariantCulture, $"Moment {moment.Number} of the visit, at {SummaryCollator.Time(moment.Frame.Time)}. Why the recorder chose it:\n");
        for (var index = 0; index < moment.Reasons.Count; index++)
        {
            references.Add(reasonReferences[index]);
            input.Append(CultureInfo.InvariantCulture, $"[{reasonReferences[index]}] {SummaryCollator.Time(moment.Reasons[index].Time)} {moment.Reasons[index].Text}\n");
        }

        foreach (var tile in page.Tiles)
        {
            references.Add(tileReferences[tile.Number - 1]);
        }

        input.Append(CultureInfo.InvariantCulture,
                $"\nThe page as the recorder rebuilt it at this moment, {page.Width:0} by {page.Height:0} CSS pixels, pictured whole in {page.Tiles.Count} tiles the height of the viewport. ")
            .Append(InViewText(moment, page, tileReferences)).Append('\n')
            .Append(RecreationNotes(page)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"This is group {number} of {count} of the tiles. Its pictures, top to bottom:\n");
        foreach (var tile in group)
        {
            var reference = tileReferences[tile.Number - 1];
            input.Append(CultureInfo.InvariantCulture, $"[{reference}] {TileText(moment, page, tile, withFile: false)}. It is labelled Picture {reference}.\n");
        }

        return (input.ToString(), references);
    }

    // A moment's combined request: the capabilities, the visit, why the
    // moment was chosen, the frame's description, the tiles' descriptions,
    // the part on the screen, and what the recreation lacked.
    internal static (string Input, HashSet<string> References) CombinedInput(
        RecordingCollation collation,
        VisitCollation visit,
        Moment moment,
        string frameReference,
        IReadOnlyList<string> reasonReferences,
        AcceptedStep frameStep,
        RecreatedPage page,
        IReadOnlyList<string> tileReferences,
        IReadOnlyList<(IReadOnlyList<PageTile> Tiles, AcceptedStep Step)> pageSteps)
    {
        var references = new HashSet<string>(RecordingSummaryKind.CapabilityReferences(collation), StringComparer.Ordinal) { frameReference };
        references.UnionWith(tileReferences);
        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"Page visit {visit.Visit.Number}, derived by the recorder: {visit.Visit.Start.Url}")
            .Append(visit.Title is { Length: > 0 } title ? $", titled \"{title.Replace("\"", "'", StringComparison.Ordinal)}\"" : ", with no title recorded")
            .Append(".\n\n")
            .Append(CultureInfo.InvariantCulture, $"Moment {moment.Number} of the visit, at {SummaryCollator.Time(moment.Frame.Time)}. Why the recorder chose it:\n");
        for (var index = 0; index < moment.Reasons.Count; index++)
        {
            references.Add(reasonReferences[index]);
            input.Append(CultureInfo.InvariantCulture, $"[{reasonReferences[index]}] {SummaryCollator.Time(moment.Reasons[index].Time)} {moment.Reasons[index].Text}\n");
        }

        // A statement the same, word for word, as an earlier one in any of
        // the descriptions is given once, with the references of both
        // (agreed with the owner, 2026-10-14).
        var blocks = new List<IReadOnlyList<Claim>> { frameStep.Claims };
        blocks.AddRange(pageSteps.Select(item => item.Step.Claims));
        var given = Distinct(blocks);
        input.Append(CultureInfo.InvariantCulture, $"\nThe captured frame: [{frameReference}] {FrameText(moment.Frame)}.\n")
            .Append("The moment as a model described it from the captured frame and the recorder's facts. These are inferences, not facts:\n");
        RecordingSummaryKind.AppendClaims(input, given[0], references);
        input.Append(CultureInfo.InvariantCulture,
                $"\nThe page as the recorder rebuilt it at this moment, {page.Width:0} by {page.Height:0} CSS pixels, pictured whole in {page.Tiles.Count} tiles. ")
            .Append(InViewText(moment, page, tileReferences)).Append('\n')
            .Append(RecreationNotes(page)).Append('\n');
        for (var index = 0; index < pageSteps.Count; index++)
        {
            var tiles = pageSteps[index].Tiles;
            var named = string.Join(", ", tiles.Select(tile => $"tile {tile.Number} [{tileReferences[tile.Number - 1]}], CSS pixels {tile.Top.ToString("0", CultureInfo.InvariantCulture)} to {(tile.Top + tile.Height).ToString("0", CultureInfo.InvariantCulture)}"));
            input.Append(CultureInfo.InvariantCulture, $"\nThe recreated page as a model described it from {named}. These are inferences, not facts:\n");
            RecordingSummaryKind.AppendClaims(input, given[index + 1], references);
        }

        return (input.ToString(), references);
    }

    // Each block's statements without those whose kind and text are the
    // same as an earlier statement's in any block, the first given the
    // references of all of them, in order.
    internal static List<List<Claim>> Distinct(IReadOnlyList<IReadOnlyList<Claim>> blocks)
    {
        var result = blocks.Select(_ => new List<Claim>()).ToList();
        var first = new Dictionary<(string Kind, string Text), (int Block, int Index)>();
        for (var block = 0; block < blocks.Count; block++)
        {
            foreach (var claim in blocks[block])
            {
                if (first.TryGetValue((claim.Kind, claim.Text), out var at))
                {
                    var earlier = result[at.Block][at.Index];
                    result[at.Block][at.Index] = earlier with { Evidence = [.. earlier.Evidence.Union(claim.Evidence, StringComparer.Ordinal)] };
                    continue;
                }

                first[(claim.Kind, claim.Text)] = (block, result[block].Count);
                result[block].Add(claim);
            }
        }

        return result;
    }

    // A moment's request: the capabilities, the visit's address and title,
    // why the moment was chosen, and the visit's facts since the moment
    // before, with the picture's reference.
    internal static (string Input, HashSet<string> References) MomentInput(
        RecordingCollation collation,
        VisitCollation visit,
        Moment moment,
        string frameReference,
        IReadOnlyList<string> reasonReferences,
        long previous)
    {
        var references = new HashSet<string>(RecordingSummaryKind.CapabilityReferences(collation), StringComparer.Ordinal) { frameReference };
        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"Page visit {visit.Visit.Number}, derived by the recorder: {visit.Visit.Start.Url}")
            .Append(visit.Title is { Length: > 0 } title ? $", titled \"{title.Replace("\"", "'", StringComparison.Ordinal)}\"" : ", with no title recorded")
            .Append(".\n\n")
            .Append(CultureInfo.InvariantCulture, $"Moment {moment.Number} of the visit, at {SummaryCollator.Time(moment.Frame.Time)}. Why the recorder chose it:\n");
        for (var index = 0; index < moment.Reasons.Count; index++)
        {
            var reason = moment.Reasons[index];
            references.Add(reasonReferences[index]);
            input.Append(CultureInfo.InvariantCulture, $"[{reasonReferences[index]}] {SummaryCollator.Time(reason.Time)} {reason.Text}\n");
        }

        var facts = visit.Lines.Where(line => TimedLine().Match(line) is { Success: true } match && Within(match, previous, moment.Frame.Time)).ToList();
        input.Append(facts.Count == 0
            ? "\nThe recorder has no facts of the visit since the moment before.\n"
            : "\nThe recorder's facts of the visit since the moment before:\n" + string.Join('\n', facts) + "\n");
        foreach (var line in facts)
        {
            foreach (Match match in Reference().Matches(line))
            {
                references.Add(match.Groups[1].Value);
            }
        }

        input.Append(CultureInfo.InvariantCulture, $"\nThe picture: [{frameReference}] {FrameText(moment.Frame)}. It is labelled Picture {frameReference}.");
        return (input.ToString(), references);
    }

    private static bool Within(Match match, long after, long until)
    {
        var time = ((long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)) * 60 +
            long.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)) * 1_000_000_000 +
            long.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) * 1_000_000;
        return time > after && time <= until;
    }

    // A visit's enhanced request: its facts, its description from the
    // summary, and its moments' descriptions, both labelled inferred.
    internal static (string Input, HashSet<string> References) VisitInput(
        RecordingCollation collation,
        VisitCollation visit,
        AcceptedStep summary,
        IReadOnlyList<MomentDescription> moments,
        int leftOut)
    {
        var references = RecordingSummaryKind.VisitReferences(collation, visit);
        var input = new StringBuilder(RecordingSummaryKind.VisitInput(collation, visit))
            .Append("\n\nThe visit as a model described it from the recorder's facts alone. These are inferences, not facts:\n");
        RecordingSummaryKind.AppendClaims(input, summary.Claims, references);
        if (moments.Count == 0)
        {
            input.Append("\nNo captured frames were recorded during this visit, so it has no moments.\n");
        }
        else
        {
            input.Append("\nMoments of the visit chosen by the recorder, each as a model described it from its captured frame and, where it was recreated, its page pictured whole in tiles. These are inferences, not facts:\n");
            foreach (var moment in moments)
            {
                references.Add(moment.FrameReference);
                references.UnionWith(moment.ReasonReferences);
                references.UnionWith(moment.TileReferences ?? []);
                var reasons = string.Join("; ", moment.Moment.Reasons.Select((reason, index) => $"{reason.Text} [{moment.ReasonReferences[index]}]"));
                var tiles = moment.Combined
                    ? $", the recreated page in tiles {string.Join(", ", moment.TileReferences!.Select(reference => $"[{reference}]"))}"
                    : moment.Page?.NotRecreated is { } why ? $", the page not recreated because {why}" : "";
                input.Append(CultureInfo.InvariantCulture,
                    $"Moment {moment.Moment.Number}, at {SummaryCollator.Time(moment.Moment.Frame.Time)}, picture [{moment.FrameReference}]{tiles}, chosen because: {reasons}\n");
                RecordingSummaryKind.AppendClaims(input, moment.Step.Claims, references);
            }

            if (leftOut > 0)
            {
                input.Append(CultureInfo.InvariantCulture, $"The recorder left {leftOut} more moments out to keep within the limit; do not describe what was left out as absent.\n");
            }
        }

        return (input.ToString(), references);
    }

    private static string MediaType(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".JPG" or ".JPEG" => "image/jpeg",
        ".WEBP" => "image/webp",
        _ => "image/png"
    };

    private static async Task<byte[]> ReadFileAsync(string path, string what, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StepFailedException($"The {what} {path} could not be read: {exception.Message}");
        }
    }

    private static async Task<byte[]> ReadFrameAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(frame.AbsolutePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StepFailedException($"The captured frame {frame.AbsolutePath} could not be read: {exception.Message}");
        }
    }
}
