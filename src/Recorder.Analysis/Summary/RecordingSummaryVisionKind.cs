using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Recorder.Database.RecordingFiles;

namespace Recorder.Analysis.Summary;

/// <summary>One moment as the vision stage described it: the moment, its picture's reference, its reasons' references, and the accepted step.</summary>
public sealed record MomentDescription(Moment Moment, string FrameReference, IReadOnlyList<string> ReasonReferences, AcceptedStep Step);

/// <summary>A visit's part of the vision stage: its moments as described, how many were left out, and the enhanced description.</summary>
public sealed record VisitVision(IReadOnlyList<MomentDescription> Moments, int MomentsLeftOut);

/// <summary>What the report adds for the vision stage.</summary>
public sealed record VisionReportParts(IReadOnlyDictionary<int, VisitVision> Visits, MomentLimits Limits, Guid SummaryTaskId);

/// <summary>
/// The recording summary's second stage, step 4a: each visit's moments,
/// chosen by the recorder's code, each described by a model from its
/// captured frame and the facts around it; then each visit's description
/// written again with them, and the whole recording's, written as
/// summary-with-vision.md. It builds on a finished summary of the same
/// recording, the task it follows. The recreated page's tiles are step 4b.
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

    public const int TemplateVersion = 1;

    public static readonly IReadOnlyList<string> MomentSections = ["screen", "change"];

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

    public int Version => 1;

    /// <summary>The summary's collation limits, which must be the summary's own for its references to match.</summary>
    public CollationLimits Limits { get; init; } = CollationLimits.Default;

    public MomentLimits Moments { get; init; } = MomentLimits.Default;

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
        "Answer in JSON with two sections: \"screen\", what was on the screen: the page and its layout, imagery, colour, and emphasis, " +
        "what was open, such as a dialog, a menu, or a popup, which part of the page was in view, and any other window in view; " +
        "and \"change\", what changed at this moment, as the reasons and facts give it, and what the picture shows of it. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. Each section holds only its own paragraphs. " +
        "A statement is {\"kind\": \"claim\", \"text\": ..., \"evidence\": [...]} citing every reference it rests on, " +
        "with every statement about the picture citing the picture's reference, " +
        "or {\"kind\": \"unknown\", \"text\": ..., \"evidence\": []} for something the picture and facts leave open. " +
        "Cite only references that appear in the input. Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";

    public const string VisitInstructions =
        "You describe one page visit from an accessibility test recording, for an accessibility auditor, adding what captured frames of the screen show " +
        "to a description written from the recorder's facts alone. " +
        "You are given the recorder's facts about the visit, each beginning with an evidence reference such as [E3]; a model's description of the visit from those facts; " +
        "and a model's descriptions of moments of the visit, each from a captured frame of the whole screen with its own reference. " +
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
        "A statement is {\"kind\": \"claim\", \"text\": ..., \"evidence\": [...]} citing every reference it rests on, " +
        "with a statement from a moment's description citing that moment's picture, " +
        "or {\"kind\": \"unknown\", \"text\": ..., \"evidence\": []}. Cite only references that appear in the input. Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";

    public static readonly string RecordingInstructions = RecordingSummaryKind.RecordingInstructions.Replace(
        "and a model's descriptions of each visit, which are inferences and not facts.",
        "and a model's descriptions of each visit, from the recorder's facts and from captured frames of the screen, which are inferences and not facts.",
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
            await DescribeAsync(context, summaryTask, session, evidence, evidence, cancellationToken).ConfigureAwait(false);
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
                var step = await context.RunStepAsync(
                    $"{RecordingSummaryKind.VisitStepKey(visit)}-moment-{moment.Number.ToString("D2", CultureInfo.InvariantCulture)}",
                    new ModelRequest(MomentTemplate, TemplateVersion, MomentInstructions, input,
                        [new ModelImage(frameReference, MediaType(frame.AbsolutePath), await ReadFrameAsync(frame, cancellationToken).ConfigureAwait(false))],
                        MomentSections, references),
                    cancellationToken).ConfigureAwait(false);
                descriptions.Add(new MomentDescription(moment, frameReference, reasonReferences, step));
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
            new VisionReportParts(parts, Moments, summaryTask));
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
            input.Append("\nMoments of the visit chosen by the recorder, each as a model described it from its captured frame. These are inferences, not facts:\n");
            foreach (var moment in moments)
            {
                references.Add(moment.FrameReference);
                references.UnionWith(moment.ReasonReferences);
                var reasons = string.Join("; ", moment.Moment.Reasons.Select((reason, index) => $"{reason.Text} [{moment.ReasonReferences[index]}]"));
                input.Append(CultureInfo.InvariantCulture,
                    $"Moment {moment.Moment.Number}, at {SummaryCollator.Time(moment.Moment.Frame.Time)}, picture [{moment.FrameReference}], chosen because: {reasons}\n");
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
