using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Analysis.Summary;
using Recorder.Database.RecordingFiles;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// The Android report (android-report.md): every Android check run on a
/// phone recording, written by screen as a Word document and as Markdown,
/// with the screens' pictures. No model writes it; where the project
/// chooses a model for vision-based checks, the images-of-text check asks
/// it about the images its rules leave (AndroidImagesOfText), the check of
/// text shown as a heading about the text its rules leave
/// (AndroidHeadingLook), the check of images with no text alternative
/// about the images its rules leave (AndroidImageAlternative), the icon and
/// description checks about the described images theirs leave
/// (AndroidDescribedImages), and the report says AI was used. With no model
/// it gives the same report for the same recording, settings and guidance.
/// </summary>
public sealed class AndroidReportKind : IAnalysisTaskKind
{
    public const string Name = "android-report";

    public const string BaseName = "android-report";

    public string Kind => Name;

    public int Version => 1;

    public Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken) =>
        AndroidReportRun.RunAsync(context, BaseName, summarize: false, cancellationToken);
}

/// <summary>
/// The project's Android report (projects.md): the Android report of every
/// recording in a project with phone screens, taken together, with a
/// screen seen in more than one recording given once and its findings
/// grouped across them. No model writes it; the images-of-text check, the
/// check of text shown as a heading, the check of images with no text
/// alternative and the icon and description checks ask the project's model
/// for vision-based checks, where one is chosen.
/// </summary>
public sealed class AndroidProjectReportKind : IAnalysisTaskKind
{
    public const string Name = "android-project-report";

    public const string BaseName = "android-project-report";

    /// <summary>The folder, under the recordings' folder, that a project's reports are in.</summary>
    public const string ProjectsFolder = "Projects";

    public string Kind => Name;

    public int Version => 1;

    public Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken) =>
        AndroidReportRun.RunProjectAsync(context, BaseName, cancellationToken);

    /// <summary>Where a project's task writes its reports, relative to the recordings' folder.</summary>
    public static string FolderOf(Guid projectId, Guid taskId) =>
        Path.Combine(ProjectsFolder, projectId.ToString("D"), "analysis", taskId.ToString("D"));
}

/// <summary>
/// The Android summary (android-report.md, "The Android summary"): the
/// Android report with a model's summary of each screen with findings and
/// of the whole app, written from the findings. The findings themselves are
/// still the recorder's.
/// </summary>
public sealed class AndroidSummaryKind : IAnalysisTaskKind
{
    public const string Name = "android-summary";

    public const string BaseName = "android-summary";

    public const string ScreenTemplate = "android-summary-screen";

    public const string RecordingTemplate = "android-summary-recording";

    // Version 2: every fact has a reference, the screen and its app
    // included (target machine, 2026-11-14).
    public const int TemplateVersion = 3;

    public static readonly IReadOnlyList<string> ScreenSections = ["screen", "issues"];

    public static readonly IReadOnlyList<string> RecordingSections = ["app", "barriers", "priorities"];

    public const string ScreenInstructions =
        "You summarise one screen of an Android app from an accessibility test recording, for an accessibility auditor and the app's developers. " +
        "You are given the recorder's facts about the screen: the screen and how the recorder named it, its app, its visits and the findings of the recorder's automated checks, each fact beginning with its evidence reference such as [E3]. " +
        "Describe only what the facts support. Name the app, the screen and its items only as the facts name them. " +
        "An error is a check that failed on the recorded evidence. A warning or information is a likely issue that a person has not yet confirmed: " +
        "open a statement about one with that, such as \"a likely issue, not yet confirmed, is\", never state it as a failure. " +
        "Each finding's impact, high, medium or low, is the recorder's default judgement of that kind of issue once confirmed, not measured from this app. " +
        "Do not add issues, causes or fixes the facts do not give, and do not say whether the screen meets WCAG as a whole. " +
        "Answer in JSON with two sections: \"screen\", what the screen appears to be for and what it holds, as far as the facts show; " +
        "and \"issues\", the main barriers on the screen, who they affect and how, most serious first, grouping findings of the same kind into one statement citing each of their references. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. " +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} for something the facts support, citing every reference it rests on, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []} for something the facts leave open, " +
        "or {\"text\": ..., \"kind\": \"none\", \"evidence\": []} for something of which the facts hold no evidence. " +
        "Cite only references that appear in the facts. Write plain sentences without Markdown.";

    public const string RecordingInstructions =
        "You summarise the accessibility of an Android app from a test recording, for an accessibility auditor and the app's developers. " +
        "You are given the recorder's facts, each beginning with its evidence reference such as [E3]: the app, its framework as the recorder found it, the counts of findings by WCAG success criterion, " +
        "and each screen as a model summarised it from the recorder's findings, labelled inferred; these summaries are inferences, not facts. " +
        "Describe only what these support, and name the app and its screens only as they are named. " +
        "A warning is a likely issue that a person has not yet confirmed; never state one as a failure. " +
        "Each finding's impact, high, medium or low, is the recorder's default judgement of that kind of issue once confirmed. " +
        "Do not say whether the app meets WCAG as a whole, and do not add issues or fixes the input does not give. " +
        "Answer in JSON with three sections: \"app\", what the app appears to be and what the recording covered; " +
        "\"barriers\", the main barriers across the app and who they affect; and \"priorities\", which kinds of issue to fix first and why, from their impact and how widely they were found. " +
        "Each section is a list of paragraphs, and each paragraph a list of statements. " +
        "A statement is {\"text\": ..., \"kind\": \"claim\", \"evidence\": [...]} citing every reference it rests on, " +
        "or {\"text\": ..., \"kind\": \"unknown\", \"evidence\": []} for something the input leaves open, " +
        "or {\"text\": ..., \"kind\": \"none\", \"evidence\": []} for something of which the input holds no evidence. Cite only references that appear in the input. Write plain sentences without Markdown.";

    public string Kind => Name;

    public int Version => 1;

    public Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken) =>
        AndroidReportRun.RunAsync(context, BaseName, summarize: true, cancellationToken);

    /// <summary>A screen's step key, screen-0001 upward.</summary>
    public static string ScreenStepKey(PhoneScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return $"screen-{screen.Number.ToString("D4", CultureInfo.InvariantCulture)}";
    }

    private static string Count(int count, string one, string many) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

    /// <summary>A screen's facts as its request is given them, and their references.</summary>
    public static (string Input, HashSet<string> References) ScreenInput(PhoneScreen screen, AndroidEvidence evidence, AndroidGuidance guidance, PhoneDeviceTimeline device)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentNullException.ThrowIfNull(device);
        var screenReference = evidence.ScreenReference(screen);
        var appReference = evidence.AppReference(screen.Package);
        var references = new HashSet<string>(StringComparer.Ordinal) { screenReference, appReference };
        var input = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"[{screenReference}] The recorder found the screen \"{screen.Name}\" of the app {screen.Package}, {screen.NamedBy}.\n")
            .Append(CultureInfo.InvariantCulture, $"[{appReference}] The app {screen.Package}: {device.DescribeFramework(screen.Package)}\n");
        foreach (var finding in evidence.Repeated(screen))
        {
            var seen = finding.Visits.Select(on => on.Reference).ToList();
            references.UnionWith(seen);
            input.Append(CultureInfo.InvariantCulture, $"{string.Join(" ", seen.Select(reference => $"[{reference}]"))} {FindingLine(finding, guidance)}\n");
        }

        foreach (var visit in screen.Visits)
        {
            var reference = evidence.VisitReference(visit);
            references.Add(reference);
            input.Append(CultureInfo.InvariantCulture, $"[{reference}] Visit {visit.Number}, {visit.When}: {visit.CauseText}.\n");
            foreach (var finding in evidence.For(visit))
            {
                references.Add(finding.Reference);
                input.Append(CultureInfo.InvariantCulture, $"[{finding.Reference}] {FindingLine(finding, guidance)}\n");
            }
        }

        return (input.ToString(), references);
    }

    /// <summary>A finding as a model is given it: number, level, impact, criteria, kind, item and what was measured.</summary>
    public static string FindingLine(NumberedFinding finding, AndroidGuidance guidance)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(guidance);
        var check = finding.Guidance;
        var level = finding.Finding.Level switch
        {
            PhoneCheckLevel.Error => "error",
            PhoneCheckLevel.Warning => "warning, not yet confirmed",
            _ => "information, not yet confirmed",
        };
        var impact = finding.Impact?.Level ?? "not given";
        var criteria = check is null || check.Criteria.Count == 0 ? "no WCAG success criterion, best practice" : "WCAG " + string.Join("; ", check.Criteria.Select(guidance.CriterionText));
        var affects = check is null ? "" : " Affects: " + string.Join("; ", check.Affects) + ".";
        var first = finding.Finding.Times[0];
        var visits = finding.OnSeveralVisits
            ? $" On visits {string.Join(" and ", finding.Visits.Select(on => on.Visit.Number.ToString(CultureInfo.InvariantCulture)))} of the screen."
            : "";
        var times = finding.Visits.Sum(on => on.Finding.Times.Count);
        return string.Create(CultureInfo.InvariantCulture,
            $"Finding {finding.Number}, {level}, impact {impact}, {criteria}: {check?.Title ?? PhoneCheckNames.Title(finding.Finding.Check)}. Item: {finding.Finding.Label}. Measured: {first.Measured}.{(finding.Finding.Inference is { } inference ? " A model's answer, an inference, not a fact: " + inference.Text + "." : "")}{(first.State is null ? "" : " State: " + first.State + ".")}{visits} Seen {Count(times, "time", "times")}.{affects}");
    }

    /// <summary>The whole app's request: the apps, the counts by criterion, the app's findings, and each screen as a model summarised it.</summary>
    public static (string Input, HashSet<string> References) RecordingInput(
        PhoneScreenTimeline screens,
        AndroidEvidence evidence,
        AndroidGuidance guidance,
        PhoneDeviceTimeline device,
        IReadOnlyDictionary<PhoneScreen, AcceptedStep> summaries)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(summaries);
        var references = new HashSet<string>(StringComparer.Ordinal);
        var input = new StringBuilder();
        foreach (var app in screens.Screens.Select(screen => screen.Package).Distinct(StringComparer.Ordinal))
        {
            var reference = evidence.AppReference(app);
            references.Add(reference);
            input.Append(CultureInfo.InvariantCulture, $"[{reference}] The app {app}: {device.DescribeVersion(app)}. {device.DescribeFramework(app)}\n");
        }

        input.Append(CultureInfo.InvariantCulture, $"The recorder found {Count(screens.Screens.Count, "screen", "screens")} in {Count(screens.Visits.Count, "visit", "visits")}, and {Count(evidence.All.Count, "finding", "findings")}.\n");
        references.Add(evidence.CountsReference);
        input.Append(CultureInfo.InvariantCulture, $"[{evidence.CountsReference}] The recorder's counts of findings by WCAG success criterion:\n");
        foreach (var group in evidence.All
            .SelectMany(finding => (finding.Guidance?.Criteria ?? []).DefaultIfEmpty("").Select(criterion => (Key: criterion, Finding: finding)))
            .GroupBy(pair => pair.Key, pair => pair.Finding)
            .OrderBy(group => group.Key.Length == 0 ? 1 : 0).ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            var name = group.Key.Length == 0 ? "No WCAG success criterion, best practice" : guidance.CriterionText(group.Key);
            input.Append(CultureInfo.InvariantCulture,
                $"  {name}: {Count(group.Count(), "finding", "findings")}, {Count(group.Count(f => f.Finding.Level == PhoneCheckLevel.Error), "error", "errors")}, {group.Count(f => f.Finding.Level != PhoneCheckLevel.Error)} not yet confirmed, highest impact {guidance.OfRank(group.Max(f => f.Rank))?.Level ?? "not given"}.\n");
        }

        foreach (var finding in evidence.App)
        {
            references.Add(finding.Reference);
            input.Append(CultureInfo.InvariantCulture, $"[{finding.Reference}] Across the app: {FindingLine(finding, guidance)}\n");
        }

        input.Append("Each screen as a model summarised it from the recorder's findings. These are inferences, not facts:\n");
        foreach (var screen in screens.Screens)
        {
            if (summaries.TryGetValue(screen, out var step))
            {
                input.Append(CultureInfo.InvariantCulture, $"Screen {screen.Number}, \"{screen.Name}\":\n");
                RecordingSummaryKind.AppendClaims(input, step.Claims, references);
            }
        }

        return (input.ToString(), references);
    }
}

/// <summary>
/// The model checks of a phone recording (android-vision-checks.md): the
/// checks run as the Android report runs them, and the project's model for
/// vision-based checks asked about each image of text, each piece of text
/// shown as a heading, each image with no text alternative and each
/// described image their rules leave, as the Checks tab's "Ask the
/// project's model" queues it. Its
/// answers are kept by their requests, and an image's also by its pixels
/// and the text beside it, so the Checks tab and a report show them
/// without asking again. It writes the questions and answers as Markdown.
/// </summary>
public sealed class AndroidModelChecksKind : IAnalysisTaskKind
{
    public const string Name = "android-model-checks";

    public const string BaseName = "android-model-checks";

    public string Kind => Name;

    public int Version => 1;

    public Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken) =>
        AndroidReportRun.RunAsync(context, BaseName, summarize: false, cancellationToken, modelChecksOnly: true);

    /// <summary>The Markdown of the questions and what their answers made of each finding.</summary>
    public static string Markdown(string recording, AndroidAudit audit)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(audit);
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"# Model checks: {recording}\n\n");
        if (audit.ImagesOfText is not { } run)
        {
            return text.Append("The colours could not be measured, so the images-of-text check, the check of text shown as a heading, the check of images with no text alternative and the icon and description checks did not run")
                .Append(audit.MeasureProblem is { } problem ? ": " + problem : "").Append(".\n").ToString();
        }

        text.Append("## Images of text\n\n")
            .Append(CultureInfo.InvariantCulture,
                $"Of {run.Images} images on the pictures OCR read, the rules left {run.Questions.Count} for the model for vision-based checks, and {run.ManualChecks} for a manual check.");
        if (run.ModelNote is { } note)
        {
            text.Append(' ').Append(char.ToUpperInvariant(note[0])).Append(note.AsSpan(1)).Append('.');
        }

        text.Append("\n\n");
        foreach (var finding in run.Findings.Where(finding => finding.Check == PhoneCheckNames.ImageOfText && finding.Inference is not null))
        {
            text.Append(CultureInfo.InvariantCulture,
                $"- {finding.Screen.Package}, \"{finding.Screen.Name}\", visit {finding.Visit.Number}, {finding.Label}: {finding.Text}\n");
        }

        if (audit.HeadingLook is { } headings)
        {
            Headings(text, headings);
        }

        if (audit.ImageAlternative is { } alternatives)
        {
            text.Append("\n## Images with no text alternative\n\n")
                .Append(CultureInfo.InvariantCulture,
                    $"Of {alternatives.Images} images on the pictures OCR read, the rules left {alternatives.Questions.Count} for the model for vision-based checks.");
            if (alternatives.ModelNote is { } alternativeNote)
            {
                text.Append(' ').Append(char.ToUpperInvariant(alternativeNote[0])).Append(alternativeNote.AsSpan(1)).Append('.');
            }

            text.Append("\n\n");
            foreach (var finding in alternatives.Findings.Where(finding => finding.Inference is not null))
            {
                text.Append(CultureInfo.InvariantCulture,
                    $"- {finding.Screen.Package}, \"{finding.Screen.Name}\", visit {finding.Visit.Number}, {finding.Label}: {finding.Text}\n");
            }
        }

        if (audit.DescribedImages is { } described)
        {
            text.Append("\n## Icons and descriptions\n\n")
                .Append(CultureInfo.InvariantCulture,
                    $"Of {described.Images} described images on the pictures OCR read, {described.Controls} of them controls, the rules left {described.Questions.Count} for the model for vision-based checks.");
            if (described.ModelNote is { } describedNote)
            {
                text.Append(' ').Append(char.ToUpperInvariant(describedNote[0])).Append(describedNote.AsSpan(1)).Append('.');
            }

            text.Append("\n\n");
            foreach (var finding in described.Findings)
            {
                text.Append(CultureInfo.InvariantCulture,
                    $"- {PhoneCheckNames.Title(finding.Check)}, {finding.Screen.Package}, \"{finding.Screen.Name}\", visit {finding.Visit.Number}, {finding.Label}: {finding.Text}\n");
            }
        }

        return text.ToString();
    }

    private static void Headings(StringBuilder text, PhoneHeadingLookRun headings)
    {
        text.Append("\n## Text shown as a heading\n\n")
            .Append(CultureInfo.InvariantCulture,
                $"Of {headings.Items} pieces of text OCR placed on the pictures it read, the rules left {headings.Questions.Count} for the model for vision-based checks.");
        if (headings.ModelNote is { } headingNote)
        {
            text.Append(' ').Append(char.ToUpperInvariant(headingNote[0])).Append(headingNote.AsSpan(1)).Append('.');
        }

        text.Append("\n\n");
        foreach (var finding in headings.Findings.Where(finding => finding.Inference is not null))
        {
            text.Append(CultureInfo.InvariantCulture,
                $"- {finding.Screen.Package}, \"{finding.Screen.Name}\", visit {finding.Visit.Number}, {finding.Label}: {finding.Text}\n");
        }
    }
}

/// <summary>What both Android task kinds do: open the recording, run the checks, ask for the summaries if wanted, and write the reports.</summary>
internal static class AndroidReportRun
{
    public static async Task RunAsync(
        AnalysisTaskContext context,
        string baseName,
        bool summarize,
        CancellationToken cancellationToken,
        bool modelChecksOnly = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        var task = context.Task;
        var session = task.SessionDirectory ??
            throw new StepFailedException("The task has no session folder, so the recording cannot be read.");
        var recordingFile = await context.Catalog.RecordingFilePathAsync(task.Recording, cancellationToken).ConfigureAwait(false) ??
            throw new StepFailedException("This recording's events are in the database's evidence tables, not a recording file; the Android report reads recording files only.");
        var filePath = Path.GetFullPath(Path.Combine(session, recordingFile));
        if (!File.Exists(filePath))
        {
            throw new StepFailedException($"The recording file {filePath} is not there.");
        }

        var opened = await RecordingFilePlayback.OpenAsync(session, filePath, cancellationToken).ConfigureAwait(false);
        var archive = opened.Archive;
        try
        {
            if (!archive.PhoneScreens.Recorded)
            {
                throw new StepFailedException("This recording has no phone screens, so there is nothing for the Android report.");
            }

            var settings = PhoneCheckSettings.Load(PhoneCheckSettings.DefaultPath);
            var audit = await Task.Run(
                () => AndroidAudit.Run(archive, settings, new WindowsPhoneTextReader(), PhonePictures.Load, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            audit = await AndroidImagesOfText.AskAsync(context, audit, PhonePictures.Load, cancellationToken).ConfigureAwait(false);
            audit = await AndroidHeadingLook.AskAsync(context, audit, PhonePictures.Load, cancellationToken).ConfigureAwait(false);
            audit = await AndroidImageAlternative.AskAsync(context, audit, PhonePictures.Load, cancellationToken).ConfigureAwait(false);
            audit = await AndroidDescribedImages.AskAsync(context, audit, PhonePictures.Load, cancellationToken).ConfigureAwait(false);
            if (modelChecksOnly)
            {
                var named = await context.Catalog.RecordingAsync(task.Recording, cancellationToken).ConfigureAwait(false);
                var folderRelative = Path.Combine("analysis", task.TaskId.ToString("D"));
                await AddAsync(context, Path.Combine(session, folderRelative), folderRelative, baseName + ".md",
                    new UTF8Encoding(false).GetBytes(AndroidModelChecksKind.Markdown(named?.Label ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(session)), audit)),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var guidance = AndroidGuidance.Load();
            var evidence = AndroidEvidence.Build(archive.PhoneScreens, audit, guidance, archive.PhoneDevice);

            AndroidModelText? model = null;
            if (summarize)
            {
                model = await SummarizeAsync(context, archive, evidence, guidance, cancellationToken).ConfigureAwait(false);
            }

            var recording = await context.Catalog.RecordingAsync(task.Recording, cancellationToken).ConfigureAwait(false);
            var input = Input(archive, audit, recording?.Label ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(session)), opened.Incomplete);
            await WriteAsync(context, session, baseName, input, guidance, evidence, model, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The recording file stays open while its records are read.
            (archive.Timeline as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// A project's report: each of its recordings, in the order they were
    /// made, opened from its session folder in the recordings' folder the
    /// task was queued with and checked as a recording's report checks it;
    /// a recording not there, not ended, or with no phone screens is left
    /// out and the report says why.
    /// </summary>
    public static async Task RunProjectAsync(AnalysisTaskContext context, string baseName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var task = context.Task;
        var projectId = task.ProjectId ??
            throw new StepFailedException("This task is of one recording; the project's Android report is of a project.");
        var root = task.SessionDirectory ??
            throw new StepFailedException("The task has no recordings folder, so the project's recordings cannot be read.");
        var project = await context.Catalog.ProjectAsync(projectId, cancellationToken).ConfigureAwait(false) ??
            throw new StepFailedException("The project is no longer in the catalog.");
        var settings = PhoneCheckSettings.Load(PhoneCheckSettings.DefaultPath);
        var guidance = AndroidGuidance.Load();
        var parts = new List<AndroidReportInput>();
        var keys = new List<string>();
        var skipped = new List<string>();
        var open = new List<SessionPlaybackArchive>();
        try
        {
            foreach (var recording in project.Recordings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = recording.SessionKey;
                var label = recording.Label;
                if (!recording.Ended)
                {
                    skipped.Add($"{label}, as it was still being recorded.");
                    continue;
                }

                if (recording.RecordingFile is null)
                {
                    skipped.Add($"{label}, as its events are in the database's evidence tables, not a recording file.");
                    continue;
                }

                var session = Path.Combine(root, key);
                var filePath = Path.GetFullPath(Path.Combine(session, recording.RecordingFile));
                if (!File.Exists(filePath))
                {
                    skipped.Add($"{label}, as its recording file is not in {root}.");
                    continue;
                }

                var opened = await RecordingFilePlayback.OpenAsync(session, filePath, cancellationToken).ConfigureAwait(false);
                var archive = opened.Archive;
                if (!archive.PhoneScreens.Recorded)
                {
                    (archive.Timeline as IDisposable)?.Dispose();
                    skipped.Add($"{label}, as it has no phone screens.");
                    continue;
                }

                open.Add(archive);
                var audit = await Task.Run(
                    () => AndroidAudit.Run(archive, settings, new WindowsPhoneTextReader(), PhonePictures.Load, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                audit = await AndroidImagesOfText.AskAsync(context, audit, PhonePictures.Load, cancellationToken).ConfigureAwait(false);
                audit = await AndroidHeadingLook.AskAsync(context, audit, PhonePictures.Load, cancellationToken).ConfigureAwait(false);
                audit = await AndroidImageAlternative.AskAsync(context, audit, PhonePictures.Load, cancellationToken).ConfigureAwait(false);
                audit = await AndroidDescribedImages.AskAsync(context, audit, PhonePictures.Load, cancellationToken).ConfigureAwait(false);
                parts.Add(Input(archive, audit, label, opened.Incomplete) with { StartedUtc = new DateTimeOffset(recording.StartedUtc, TimeSpan.Zero) });
                keys.Add(key);
            }

            if (parts.Count == 0)
            {
                throw new StepFailedException(project.Recordings.Count == 0
                    ? "The project has no recordings."
                    : "None of the project's recordings could be reported on: " + string.Join(" ", skipped.Select(item => "Left out " + item)));
            }

            var evidence = AndroidEvidence.Build([.. parts.Select(part => new ReportPart(part.Screens, part.Audit, part.Device))], guidance);
            var input = new AndroidProjectInput(project.Name, parts, skipped);
            await WriteFilesAsync(context, root, AndroidProjectReportKind.FolderOf(projectId, task.TaskId), baseName,
                (pictures, written, audience) => AndroidReport.Write(input, guidance, evidence, pictures, "pictures", written, audience),
                evidence, keys, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var archive in open)
            {
                (archive.Timeline as IDisposable)?.Dispose();
            }
        }
    }

    internal static AndroidReportInput Input(SessionPlaybackArchive archive, AndroidAudit audit, string recording, string? incomplete)
    {
        var kinds = new List<string>();
        if (archive.PhoneScreenshots.Count > 0)
        {
            kinds.Add(string.Create(CultureInfo.InvariantCulture, $"{archive.PhoneScreenshots.Count} screenshots from the companion app"));
        }

        if (archive.FrameSource == SessionFrameSource.Phone)
        {
            kinds.Add(string.Create(CultureInfo.InvariantCulture, $"the phone's video, {archive.Frames.Count} {(archive.Frames.Count == 1 ? "frame" : "frames")}"));
        }

        kinds.Add(archive.PhoneTouches.Recorded ? "touches" : "no touches");
        kinds.Add(archive.PhoneSettings.Recorded ? "the phone's settings" : "no settings");
        return new AndroidReportInput(
            recording,
            archive.DurationNanoseconds,
            archive.PhoneScreens,
            archive.PhoneDevice,
            audit,
            PhoneSnapshotPictures.Of(archive),
            archive.PhoneAccessibility.ScreenAt,
            PhonePictures.Load,
            kinds,
            incomplete);
    }

    private static async Task<AndroidModelText> SummarizeAsync(
        AnalysisTaskContext context,
        SessionPlaybackArchive archive,
        AndroidEvidence evidence,
        AndroidGuidance guidance,
        CancellationToken cancellationToken)
    {
        var screens = new Dictionary<PhoneScreen, AcceptedStep>();
        foreach (var screen in archive.PhoneScreens.Screens)
        {
            if (!evidence.For(screen).Any())
            {
                continue;
            }

            var (input, references) = AndroidSummaryKind.ScreenInput(screen, evidence, guidance, archive.PhoneDevice);
            screens[screen] = await context.RunStepAsync(
                AndroidSummaryKind.ScreenStepKey(screen),
                new ModelRequest(AndroidSummaryKind.ScreenTemplate, AndroidSummaryKind.TemplateVersion, AndroidSummaryKind.ScreenInstructions, input, [], AndroidSummaryKind.ScreenSections, references),
                cancellationToken).ConfigureAwait(false);
        }

        AcceptedStep? overall = null;
        if (evidence.All.Count > 0)
        {
            var (input, references) = AndroidSummaryKind.RecordingInput(archive.PhoneScreens, evidence, guidance, archive.PhoneDevice, screens);
            overall = await context.RunStepAsync(
                "recording",
                new ModelRequest(AndroidSummaryKind.RecordingTemplate, AndroidSummaryKind.TemplateVersion, AndroidSummaryKind.RecordingInstructions, input, [], AndroidSummaryKind.RecordingSections, references),
                cancellationToken).ConfigureAwait(false);
        }

        var attempts = await context.Catalog.AttemptsAsync(context.Task.TaskId, cancellationToken).ConfigureAwait(false);
        return new AndroidModelText(overall, screens, attempts);
    }

    /// <summary>
    /// Writes the pictures, the client's Word document and the auditor's
    /// Markdown to the task's folder, and records the reports, the Markdown last, as the player
    /// opens a task's last report in its reading view.
    /// </summary>
    internal static async Task WriteAsync(
        AnalysisTaskContext context,
        string session,
        string baseName,
        AndroidReportInput input,
        AndroidGuidance guidance,
        AndroidEvidence evidence,
        AndroidModelText? model,
        CancellationToken cancellationToken)
    {
        await WriteFilesAsync(context, session, Path.Combine("analysis", context.Task.TaskId.ToString("D")), baseName,
            (pictures, written, audience) => AndroidReport.Write(input, guidance, evidence, model, pictures, "pictures", written, audience),
            evidence, [Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(session)))], cancellationToken).ConfigureAwait(false);
    }

    // The reports, written to a folder relative to the task's folder, with
    // the evidence the Markdown lists for an MP4 export (mp4-export.md,
    // "Evidence numbers"); keys are the session keys of the report's
    // recordings, in their numbers' order.
    private static async Task WriteFilesAsync(
        AnalysisTaskContext context,
        string session,
        string folderRelative,
        string baseName,
        Func<string, DateTime, ReportAudience, string> write,
        AndroidEvidence evidence,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(session, folderRelative);
        var pictures = Path.Combine(folder, "pictures");
        if (Directory.Exists(pictures))
        {
            Directory.Delete(pictures, recursive: true);
        }

        // The Markdown is the auditor's, with where each statement came
        // from; the Word document is the client's (android-report.md,
        // "Two readers"). Both write the same pictures.
        var written = DateTime.UtcNow;
        var markdown = write(pictures, written, ReportAudience.Auditor);
        var client = write(pictures, written, ReportAudience.Client);
        var word = WordReport.Write(client, folder, AndroidReport.Labels);
        var listed = ReportEvidenceList.Listed(markdown);
        await ReportEvidenceList.WriteAsync(folder, evidence.Entries
            .Where(entry => listed.Contains(entry.Reference))
            .Select(entry => new ReportEvidenceItem(entry.Reference, EvidenceRecording(entry.Recording, keys), entry.Time, entry.Kind)),
            cancellationToken).ConfigureAwait(false);
        await AddAsync(context, folder, folderRelative, baseName + ".docx", word, cancellationToken).ConfigureAwait(false);
        await AddAsync(context, folder, folderRelative, baseName + ".md", new UTF8Encoding(false).GetBytes(markdown), cancellationToken).ConfigureAwait(false);
    }

    // A recording's session key from its number, from 1; one report's
    // evidence about every recording is about its one recording.
    internal static string? EvidenceRecording(int recording, IReadOnlyList<string> keys) =>
        recording >= 1 && recording <= keys.Count ? keys[recording - 1] : keys.Count == 1 ? keys[0] : null;

    private static async Task AddAsync(AnalysisTaskContext context, string folder, string folderRelative, string fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes, cancellationToken).ConfigureAwait(false);
        await context.Catalog.AddReportAsync(context.Task.TaskId, fileName, Path.Combine(folderRelative, fileName), Convert.ToHexStringLower(SHA256.HashData(bytes)), cancellationToken)
            .ConfigureAwait(false);
    }
}
