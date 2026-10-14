using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Database.RecordingFiles;

namespace Recorder.Analysis.Summary;

/// <summary>
/// The recording summary's first stage: the recorder's collation of each
/// page visit, a model's description of each visit, then of the whole
/// recording, written as summary.md in the task's folder under the session
/// folder's analysis folder. See docs/architecture/recording-summary.md.
/// </summary>
public sealed class RecordingSummaryKind : IAnalysisTaskKind
{
    public const string Name = "recording-summary";

    public const string FileName = "summary.md";

    public const string VisitTemplate = "recording-summary-visit";

    public const string RecordingTemplate = "recording-summary-recording";

    public const int TemplateVersion = 3;

    public static readonly IReadOnlyList<string> VisitSections = ["page", "purpose", "activity"];

    public static readonly IReadOnlyList<string> RecordingSections = ["site", "audience", "session"];

    private readonly Func<string, string, CancellationToken, Task<(Recorder.Session.SessionPlaybackArchive Archive, RecordingFileDocuments Documents)>> _open;

    public RecordingSummaryKind()
        : this(OpenAsync)
    {
    }

    /// <param name="open">Opens a recording file, given the session folder and the file's path.</param>
    public RecordingSummaryKind(Func<string, string, CancellationToken, Task<(Recorder.Session.SessionPlaybackArchive Archive, RecordingFileDocuments Documents)>> open)
    {
        _open = open;
    }

    public string Kind => Name;

    public int Version => 1;

    public CollationLimits Limits { get; init; } = CollationLimits.Default;

    private static async Task<(Recorder.Session.SessionPlaybackArchive, RecordingFileDocuments)> OpenAsync(
        string sessionDirectory,
        string filePath,
        CancellationToken cancellationToken)
    {
        var opened = await RecordingFilePlayback.OpenAsync(sessionDirectory, filePath, cancellationToken).ConfigureAwait(false);
        return (opened.Archive, opened.Documents ?? throw new StepFailedException("The recording file's browser documents could not be read."));
    }

    public const string VisitInstructions =
        "You describe one page visit from an accessibility test recording, for an accessibility auditor. " +
        "You are given facts the recorder collected from the recording; each fact begins with an evidence reference such as [E3]. " +
        "Describe only what the facts support. Name sites, programs, windows, and pages only as the facts name them. " +
        "Where the facts do not settle something, say that it is unknown; do not guess. " +
        "What NVDA had to announce is the recorder's inference from the page, not a recording of speech: say that NVDA had to announce something, " +
        "never that NVDA said it or that the participant heard it. " +
        "Use the participant's capabilities only as the facts give them: a reported description is the auditor's statement, " +
        "and assistive technology observed running shows what was running, not what the participant is able to do. " +
        "If the participant's capabilities are unknown, do not say what the participant saw or heard; say what was on the page and what was announced. " +
        "Typed text is test data from short-lived test accounts and may be quoted as recorded. " +
        "Answer in JSON with three sections: \"page\", what the page is and what it holds; \"purpose\", what the page appears to be for; " +
        "and \"activity\", what the participant did there, in order. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. " +
        "Each section holds only its own paragraphs: what the page is for goes only in \"purpose\", and what the participant did only in \"activity\". " +
        "In \"activity\", summarise what the participant did, such as moving through the headings or reading down the page, " +
        "and group repeated actions into one statement citing each of their references, rather than one statement for each fact. " +
        "A statement is {\"kind\": \"claim\", \"text\": ..., \"evidence\": [...]} for something the facts support, citing every reference it rests on, " +
        "or {\"kind\": \"unknown\", \"text\": ..., \"evidence\": []} for something the facts leave open. " +
        "Cite only references that appear in the facts. Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";

    public const string RecordingInstructions =
        "You describe a whole accessibility test recording, for an accessibility auditor. " +
        "You are given each page visit's URL, title, and headings from the recorder, each with an evidence reference such as [E3], " +
        "and a model's descriptions of each visit, which are inferences and not facts. " +
        "Describe only what these support, name sites and programs only as they are named, and say that something is unknown where they do not settle it. " +
        "Use the participant's capabilities only as given: a reported description is the auditor's statement, and observed assistive technology shows what was running, " +
        "not what the participant is able to do. If the capabilities are unknown, do not say what the participant saw or heard. " +
        "Answer in JSON with three sections: \"site\", the site or application tested; \"audience\", who it appears to be for; " +
        "and \"session\", the course of the session from visit to visit. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. " +
        "Each section holds only its own paragraphs: what the site or application is goes only in \"site\", who it is for only in \"audience\", " +
        "and what the participant did, visit by visit, only in \"session\". Do not repeat a visit's description; say what it adds to the whole. " +
        "A statement is {\"kind\": \"claim\", \"text\": ..., \"evidence\": [...]} citing every reference it rests on, " +
        "or {\"kind\": \"unknown\", \"text\": ..., \"evidence\": []}. Cite only references that appear in the input. Write plain sentences without Markdown. " +
        "Write the JSON on one line, with no indentation and no line breaks.";

    public async Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (archive, documents, session, _) = await OpenRecordingAsync(context, _open, cancellationToken).ConfigureAwait(false);
        try
        {
            await SummarizeAsync(context, session, archive, documents, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The recording file stays open while its records are read.
            (archive.Timeline as IDisposable)?.Dispose();
        }
    }

    private async Task SummarizeAsync(
        AnalysisTaskContext context,
        string session,
        Recorder.Session.SessionPlaybackArchive archive,
        RecordingFileDocuments documents,
        CancellationToken cancellationToken)
    {
        var task = context.Task;
        var capabilities = await CapabilitiesAsync(context, cancellationToken).ConfigureAwait(false);
        var collation = SummaryCollator.Collate(new RecordedSummaryEvidence(archive, documents), capabilities, Limits);

        var visits = new List<(VisitCollation Visit, AcceptedStep Step)>();
        foreach (var visit in collation.Visits)
        {
            var step = await context.RunStepAsync(
                VisitStepKey(visit),
                new ModelRequest(VisitTemplate, TemplateVersion, VisitInstructions, VisitInput(collation, visit), [], VisitSections, VisitReferences(collation, visit)),
                cancellationToken).ConfigureAwait(false);
            visits.Add((visit, step));
        }

        AcceptedStep? overall = null;
        if (visits.Count > 0)
        {
            var (input, references) = RecordingInput(collation, visits, "Each visit as a model described it from the recorder's facts. These are inferences, not facts:");
            overall = await context.RunStepAsync(
                "recording",
                new ModelRequest(RecordingTemplate, TemplateVersion, RecordingInstructions, input, [], RecordingSections, references),
                cancellationToken).ConfigureAwait(false);
        }

        var recording = await context.Catalog.RecordingAsync(task.RecordingId, cancellationToken).ConfigureAwait(false);
        var attempts = await context.Catalog.AttemptsAsync(task.TaskId, cancellationToken).ConfigureAwait(false);
        var report = SummaryReport.Write(
            recording?.SessionKey ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(session)),
            collation,
            capabilities,
            visits,
            overall,
            attempts,
            Limits,
            DateTime.UtcNow);
        await WriteReportAsync(context, session, FileName, report, cancellationToken).ConfigureAwait(false);
    }

    internal static IEnumerable<string> CapabilityReferences(RecordingCollation collation) =>
        collation.Evidence.Entries.Where(entry => collation.CapabilityLines.Any(line => line.StartsWith($"[{entry.Reference}]", StringComparison.Ordinal)))
            .Select(entry => entry.Reference);

    /// <summary>A visit's step key, visit-0001 upward.</summary>
    internal static string VisitStepKey(VisitCollation visit) =>
        $"visit-{visit.Visit.Number.ToString("D4", CultureInfo.InvariantCulture)}";

    /// <summary>The references a visit's request is given: its facts' and the capabilities'.</summary>
    internal static HashSet<string> VisitReferences(RecordingCollation collation, VisitCollation visit)
    {
        var references = new HashSet<string>(visit.References, StringComparer.Ordinal);
        references.UnionWith(CapabilityReferences(collation));
        return references;
    }

    /// <summary>A visit's facts as its request is given them, after the capabilities.</summary>
    internal static string VisitInput(RecordingCollation collation, VisitCollation visit)
    {
        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"Page visit {visit.Visit.Number}, derived by the recorder:\n")
            .Append(visit.Text);
        if (visit.LeftOut > 0)
        {
            input.Append(CultureInfo.InvariantCulture, $"\n\nThe recorder left {visit.LeftOut} facts out of this visit to keep it within the limit; do not describe what was left out as absent.");
        }

        return input.ToString();
    }

    /// <summary>The whole recording's request: the capabilities, the visits' overview, and each visit as a model described it.</summary>
    internal static (string Input, HashSet<string> References) RecordingInput(
        RecordingCollation collation,
        IReadOnlyList<(VisitCollation Visit, AcceptedStep Step)> visits,
        string describedAs)
    {
        var references = new HashSet<string>(CapabilityReferences(collation), StringComparer.Ordinal);
        var overview = SummaryCollator.Overview(collation);
        foreach (var entry in collation.Evidence.Entries.Where(entry => overview.Contains($"[{entry.Reference}]", StringComparison.Ordinal)))
        {
            references.Add(entry.Reference);
        }

        var input = new StringBuilder()
            .Append(string.Join('\n', collation.CapabilityLines)).Append("\n\n")
            .Append("The page visits, derived by the recorder:\n").Append(overview)
            .Append('\n').Append(describedAs).Append('\n');
        foreach (var (visit, step) in visits)
        {
            input.Append(CultureInfo.InvariantCulture, $"Visit {visit.Visit.Number}:\n");
            AppendClaims(input, step.Claims, references);
        }

        return (input.ToString(), references);
    }

    /// <summary>A model's statements as a later request is given them, each labelled inferred, with its references.</summary>
    internal static void AppendClaims(StringBuilder input, IEnumerable<Claim> claims, ISet<string> references, string indent = "  ")
    {
        foreach (var claim in claims)
        {
            references.UnionWith(claim.Evidence);
            input.Append(CultureInfo.InvariantCulture, $"{indent}({claim.Section}, inferred) {claim.Text}");
            if (claim.Evidence.Count > 0)
            {
                input.Append(' ').Append(string.Join(' ', claim.Evidence.Select(reference => $"[{reference}]")));
            }

            input.Append('\n');
        }
    }

    /// <summary>Opens a recording file for a task kind, given the session folder and the file's path.</summary>
    internal static async Task<(Recorder.Session.SessionPlaybackArchive Archive, RecordingFileDocuments Documents, string Session, string FilePath)> OpenRecordingAsync(
        AnalysisTaskContext context,
        Func<string, string, CancellationToken, Task<(Recorder.Session.SessionPlaybackArchive Archive, RecordingFileDocuments Documents)>> open,
        CancellationToken cancellationToken)
    {
        var task = context.Task;
        var session = task.SessionDirectory ??
            throw new StepFailedException("The task has no session folder, so the recording cannot be read.");
        var recordingFile = await context.Catalog.RecordingFilePathAsync(task.RecordingId, cancellationToken).ConfigureAwait(false) ??
            throw new StepFailedException("This recording's events are in the database's evidence tables, not a recording file; the summary reads recording files only.");
        var filePath = Path.GetFullPath(Path.Combine(session, recordingFile));
        if (!File.Exists(filePath))
        {
            throw new StepFailedException($"The recording file {filePath} is not there.");
        }

        var (archive, documents) = await open(session, filePath, cancellationToken).ConfigureAwait(false);
        return (archive, documents, session, filePath);
    }

    /// <summary>The participant's capabilities as last described for the recording.</summary>
    internal static async Task<ParticipantCapabilities> CapabilitiesAsync(AnalysisTaskContext context, CancellationToken cancellationToken)
    {
        var described = (await context.Catalog.CapabilitiesAsync(context.Task.RecordingId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        return described is null ? ParticipantCapabilities.Unknown : new ParticipantCapabilities(described.Description, described.WrittenUtc);
    }

    /// <summary>Writes a report to the task's folder and records it in the catalog.</summary>
    internal static async Task WriteReportAsync(AnalysisTaskContext context, string session, string fileName, string report, CancellationToken cancellationToken)
    {
        var relative = Path.Combine("analysis", context.Task.TaskId.ToString("D"), fileName);
        var path = Path.Combine(session, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(report);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        await context.Catalog.AddReportAsync(context.Task.TaskId, fileName, relative, Convert.ToHexStringLower(SHA256.HashData(bytes)), cancellationToken)
            .ConfigureAwait(false);
    }
}
