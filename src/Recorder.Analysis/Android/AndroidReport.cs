using System.Globalization;
using System.Text;
using Recorder.Analysis.Summary;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>What the Android report is written from, besides the audit.</summary>
/// <param name="ScreenAt">The phone's screen size at a time, in pixels, as the tree gives it; null when unknown.</param>
/// <param name="Load">Reads a picture file's pixels; null when it cannot be read.</param>
/// <param name="EvidenceKinds">What the recording holds, in words, such as "12 screenshots from the companion app".</param>
/// <param name="Incomplete">Why the recording file was read only in part, when it was.</param>
/// <param name="Recording">
/// The recording as the report shows it: its name and then its session key,
/// or its session key alone when it has no name (recording-names.md).
/// </param>
public sealed record AndroidReportInput(
    string Recording,
    long DurationNanoseconds,
    PhoneScreenTimeline Screens,
    PhoneDeviceTimeline Device,
    AndroidAudit Audit,
    PhoneSnapshotPictures Pictures,
    Func<long, (int Width, int Height)?> ScreenAt,
    Func<string, PhonePixels?> Load,
    IReadOnlyList<string> EvidenceKinds,
    string? Incomplete)
{
    /// <summary>When the recording started, which a project's report lists its recordings by.</summary>
    public DateTimeOffset? StartedUtc { get; init; }
}

/// <summary>
/// A project's recordings for its Android report (projects.md), in the
/// order they were made, and those left out, each with why.
/// </summary>
public sealed record AndroidProjectInput(
    string Name,
    IReadOnlyList<AndroidReportInput> Recordings,
    IReadOnlyList<string> Skipped);

/// <summary>
/// Who a report is written for (android-report.md, "Two readers"): the
/// auditor, in the player's reading view, with how each part was made; or
/// the client's team, in the Word document, which says only that AI is
/// used in writing the report and that the final report is manually
/// inspected (owner, 2026-11-14).
/// </summary>
public enum ReportAudience
{
    Auditor,
    Client,
}

/// <summary>A model's summaries for the Android summary: the whole app's, each screen's, and the attempts that wrote them.</summary>
public sealed record AndroidModelText(
    AcceptedStep? Overall,
    IReadOnlyDictionary<PhoneScreen, AcceptedStep> Screens,
    IReadOnlyList<AttemptRecord> Attempts)
{
    /// <summary>Whether any summary was written, so that the report used AI at all.</summary>
    public bool Written => Overall is not null || Screens.Count > 0;

    /// <summary>
    /// Whether the accepted answers came from a cloud model, a local one or
    /// both: "cloud", "local" or "cloud and local"; null when no accepted
    /// attempt is recorded. A cloud model's attempt has no model file, so
    /// its SHA-256's basis is the service's (cloud-models.md). A reused
    /// answer is copied as accepted with its model, so it counts as the
    /// model that wrote it.
    /// </summary>
    public string? Kind
    {
        get
        {
            var accepted = Attempts.Where(attempt => attempt.Outcome == AttemptOutcomes.Accepted).ToList();
            if (accepted.Count == 0)
            {
                return null;
            }

            var cloud = accepted.Any(attempt => attempt.ModelSha256Basis == AnalysisWorker.ServiceBasis);
            var local = accepted.Any(attempt => attempt.ModelSha256Basis != AnalysisWorker.ServiceBasis);
            return cloud && local ? "cloud and local" : cloud ? "cloud" : "local";
        }
    }
}

/// <summary>
/// Writes the Android report as Markdown, by screen, with the pictures it
/// shows (android-report.md), for the auditor or for the client's team
/// (<see cref="ReportAudience"/>). The Word document is made from the
/// client's Markdown. With a model's text it is the Android summary: the
/// same report with each screen's and the app's summaries.
/// </summary>
public static class AndroidReport
{
    /// <summary>The labels that open a finding's paragraphs, which the Word document sets in bold.</summary>
    public static readonly IReadOnlyList<string> Labels =
    [
        "What the issue is:", "Why it matters:", "Who it affects:", "Suggested remediation for Android:",
        "Suggested remediation for Flutter:", "Suggested remediation for React Native:", "Picture:", "Sources:",
    ];

    /// <summary>
    /// Writes the report and its pictures. The pictures are written to
    /// <paramref name="picturesFolder"/>, named in the Markdown by
    /// <paramref name="picturesRelative"/>, relative to the report's folder.
    /// </summary>
    public static string Write(
        AndroidReportInput input,
        AndroidGuidance guidance,
        AndroidEvidence evidence,
        AndroidModelText? model,
        string picturesFolder,
        string picturesRelative,
        DateTime writtenUtc,
        ReportAudience audience = ReportAudience.Auditor)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentNullException.ThrowIfNull(evidence);
        var writer = new Writer(null, [input], [], guidance, evidence, model, picturesFolder, picturesRelative, audience);
        return writer.Write(writtenUtc);
    }

    /// <summary>
    /// Writes a project's report, of its recordings taken together, with
    /// the evidence built from them in the same order (projects.md).
    /// </summary>
    public static string Write(
        AndroidProjectInput project,
        AndroidGuidance guidance,
        AndroidEvidence evidence,
        string picturesFolder,
        string picturesRelative,
        DateTime writtenUtc,
        ReportAudience audience = ReportAudience.Auditor)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentNullException.ThrowIfNull(evidence);
        if (project.Recordings.Count == 0)
        {
            throw new ArgumentException("A project's report needs at least one recording.", nameof(project));
        }

        var writer = new Writer(project.Name, project.Recordings, project.Skipped, guidance, evidence, null, picturesFolder, picturesRelative, audience);
        return writer.Write(writtenUtc);
    }

    /// <summary>The client's report's title, which its pages' header repeats: the apps it covers.</summary>
    public static string ClientTitle(PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        return ClientTitle(screens.Screens.Select(screen => screen.Package));
    }

    public static string ClientTitle(IEnumerable<string> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var apps = packages.Distinct(StringComparer.Ordinal).ToList();
        return "Accessibility report: " + (apps.Count == 0 ? "Android app" : string.Join(", ", apps));
    }

    /// <summary>A model statement's text with its references at its end, each once.</summary>
    internal static string Statement(Claim claim)
    {
        var (text, _) = LayeredReport.WithoutCitedReferences(SummaryReport.OneLine(claim.Text), claim.Evidence);
        var references = claim.Evidence.Distinct(StringComparer.Ordinal).ToList();
        return references.Count == 0 ? text : text + " " + string.Join(' ', references.Select(reference => $"[{reference}]"));
    }

    private sealed class Writer(
        string? projectName,
        IReadOnlyList<AndroidReportInput> parts,
        IReadOnlyList<string> skipped,
        AndroidGuidance guidance,
        AndroidEvidence evidence,
        AndroidModelText? model,
        string picturesFolder,
        string picturesRelative,
        ReportAudience audience)
    {
        private readonly bool _client = audience == ReportAudience.Client;
        private readonly bool _project = projectName is not null;
        private readonly bool _several = parts.Count > 1;
        private readonly StringBuilder _text = new();
        private readonly HashSet<string> _cited = new(StringComparer.Ordinal);
        private readonly Dictionary<PhoneFinding, string> _placed = [];
        private readonly Dictionary<PhoneFinding, (PhonePixels Pixels, PictureBox Box)> _zoomable = [];
        private readonly Dictionary<PhoneVisit, List<Block>> _overviews = [];

        // A paragraph, or a picture with its alternative text, written later.
        private sealed record Block(string Text, string? Picture);

        // The recording a visit is in.
        private AndroidReportInput Part(PhoneVisit visit) => parts[evidence.RecordingOf(visit) - 1];

        private IEnumerable<string> Apps => evidence.Screens.Select(screen => screen.Package).Distinct(StringComparer.Ordinal);

        private string Of => _project ? "recordings of the app in a project" : "a recording of the app";

        // A picture's file name, with its recording's number when the
        // report has more than one.
        private string Prefixed(PhoneVisit visit, string name) =>
            _several ? string.Create(CultureInfo.InvariantCulture, $"recording-{evidence.RecordingOf(visit):D2}-{name}") : name;

        public string Write(DateTime writtenUtc)
        {
            var summary = model is not null;
            var checks = ChecksKind;
            if (_client)
            {
                // For the client's team, only that AI is used, whether its
                // model was cloud or local, and that the report is manually
                // inspected (owner, 2026-11-14 and 2026-11-15). A summary
                // with nothing written used no AI, so it says nothing of it;
                // a check that asked a model did use AI (android-vision-checks.md,
                // decision 2).
                Heading(1, ClientTitle(Apps));
                var written = model is { Written: true };
                Paragraph(written || checks is not null
                    ? $"This report gives the results of automated accessibility checks of {Of}{(written ? ", with summaries of the app and of each screen" : "")}. {UsedAi(Merged(written ? model!.Kind : null, written && model!.Kind is null, checks))}"
                    : $"This report gives the results of automated accessibility checks of {Of}. The final report is manually inspected.");
            }
            else
            {
                Heading(1, (summary ? "Android accessibility summary: " : "Android accessibility report: ") + (_project ? "project " + projectName : parts[0].Recording));
                var findings = checks is not null
                    ? $"The findings in this {(summary ? "summary" : "report")} are the recorder's own checks of the recording, except where a check asked {Inferences(checks)} about an image or a piece of text: that finding says so, with the model and its answer."
                    : summary
                        ? "The findings in this summary are the recorder's own checks of the recording, with no model."
                        : "Every finding in this report is the recorder's own check of the recording; no model wrote any of it.";
                Paragraph(summary
                    ? $"{findings} The summaries of the app and of each screen are {Inferences(model!.Kind)} from those findings, and say so where they appear."
                    : findings);
            }

            About(writtenUtc);
            HowToRead();
            if (model is not null)
            {
                Overview(model);
            }

            AtAGlance();
            AcrossTheApp();
            if (_client)
            {
                Heading(2, "Findings by screen");
                Paragraph("Each screen in the order it was first visited, with its visits and each visit's findings.");
            }

            foreach (var screen in evidence.Screens)
            {
                Screen(screen);
            }

            Limits();
            if (model is not null && !_client)
            {
                Heading(2, "How the summaries were written");
                var lines = new StringBuilder();
                SummaryReport.AppendModelPerStep(lines, model.Attempts);
                EndList();
                _text.Append(lines.Length == 0 ? "No model step ran.\n" : lines.ToString()).Append('\n');
            }

            Evidence();
            return _text.ToString();
        }

        private void About(DateTime writtenUtc)
        {
            if (_project)
            {
                ProjectAbout(writtenUtc);
                return;
            }

            var input = parts[0];
            Heading(2, "About this recording");
            var rows = new List<(string, string)>
            {
                ("Recording", input.Recording),
                ("Length", WindowsPreferenceTimeline.ClockOf(input.DurationNanoseconds)),
            };
            foreach (var app in input.Screens.Screens.Select(screen => screen.Package).Distinct(StringComparer.Ordinal))
            {
                rows.Add(($"App {app}", $"{input.Device.DescribeVersion(app)}. {input.Device.DescribeFramework(app)}{Refs(evidence.AppReference(app))}"));
            }

            rows.Add(("Screens and visits", Count(evidence.Screens.Count, "screen", "screens") + ", " + Count(input.Screens.Visits.Count, "visit", "visits")));
            rows.Add(("Audit level", $"WCAG {input.Audit.Level}; the phone had to be held turned for {input.Audit.HoldSeconds.ToString(CultureInfo.InvariantCulture)} seconds for the orientation check"));
            rows.Add(("Evidence in the recording", input.EvidenceKinds.Count == 0 ? "No phone evidence besides the accessibility tree" : string.Join("; ", input.EvidenceKinds)));
            rows.Add(("Text recognition", input.Audit.OcrEngines.Count > 0
                ? string.Join("; ", input.Audit.OcrEngines)
                : input.Audit.Measured?.OcrUnavailable is { } unavailable ? "Not used: " + unavailable + "; the app's text was measured on its items' bounds"
                : input.Audit.MeasureProblem is { } problem ? "The colours could not be measured: " + problem
                : "Not used"));
            rows.Add(("Guidance", _client
                ? $"Version {guidance.Version.ToString(CultureInfo.InvariantCulture)}"
                : $"Version {guidance.Version.ToString(CultureInfo.InvariantCulture)}, SHA-256 {guidance.Sha256}"));
            rows.Add(("Written", writtenUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));
            if (input.Incomplete is { } incomplete)
            {
                rows.Add(("Recording file", "Read in part: " + incomplete));
            }

            Table(["Item", "Value"], rows.Select(row => (IReadOnlyList<string>)[row.Item1, row.Item2]));
        }

        // A project's recordings, each with its own row, and what they
        // have in common once.
        private void ProjectAbout(DateTime writtenUtc)
        {
            Heading(2, "About this project");
            var rows = new List<(string, string)>
            {
                ("Project", projectName!),
                ("Recordings", Count(parts.Count, "recording", "recordings") + (skipped.Count == 0 ? "" : ", and " + Count(skipped.Count, "recording", "recordings") + " left out, as given under \"Limits\"")),
                ("Length", WindowsPreferenceTimeline.ClockOf(parts.Sum(part => part.DurationNanoseconds)) + " in all"),
            };
            foreach (var app in Apps)
            {
                var versions = parts.Select((part, index) => (part, index))
                    .Where(pair => pair.part.Screens.Screens.Any(screen => screen.Package == app))
                    .Select(pair => $"Recording {(pair.index + 1).ToString(CultureInfo.InvariantCulture)}: {pair.part.Device.DescribeVersion(app)}")
                    .ToList();
                var frameworks = parts.Where(part => part.Screens.Screens.Any(screen => screen.Package == app))
                    .Select(part => part.Device.DescribeFramework(app)).Distinct(StringComparer.Ordinal).ToList();
                rows.Add(($"App {app}", string.Join("; ", versions) + ". " + string.Join(" ", frameworks) + Refs(evidence.AppReference(app))));
            }

            var visits = parts.Sum(part => part.Screens.Visits.Count);
            var shared = evidence.Screens.Count(screen => screen.Members.Count > 1);
            rows.Add(("Screens and visits", Count(evidence.Screens.Count, "screen", "screens") + ", " + Count(visits, "visit", "visits") +
                (_several ? $"; {Count(shared, "screen", "screens")} seen in more than one recording" : "")));
            var levels = parts.Select(part => $"WCAG {part.Audit.Level}; the phone had to be held turned for {part.Audit.HoldSeconds.ToString(CultureInfo.InvariantCulture)} seconds for the orientation check").Distinct(StringComparer.Ordinal).ToList();
            rows.Add(("Audit level", levels.Count == 1 ? levels[0]
                : string.Join("; ", parts.Select((part, index) => $"Recording {(index + 1).ToString(CultureInfo.InvariantCulture)}: WCAG {part.Audit.Level}"))));
            var engines = parts.SelectMany(part => part.Audit.OcrEngines).Distinct(StringComparer.Ordinal).ToList();
            rows.Add(("Text recognition", engines.Count > 0 ? string.Join("; ", engines) : "Not used"));
            rows.Add(("Guidance", _client
                ? $"Version {guidance.Version.ToString(CultureInfo.InvariantCulture)}"
                : $"Version {guidance.Version.ToString(CultureInfo.InvariantCulture)}, SHA-256 {guidance.Sha256}"));
            rows.Add(("Written", writtenUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));
            Table(["Item", "Value"], rows.Select(row => (IReadOnlyList<string>)[row.Item1, row.Item2]));

            Heading(3, "The recordings");
            Paragraph("Each recording in the order it was made, with its number in this report.");
            Table(["Number", "Recording", "Started", "Length", "Screens and visits", "Evidence in the recording"], parts.Select((part, index) => (IReadOnlyList<string>)
            [
                (index + 1).ToString(CultureInfo.InvariantCulture),
                part.Recording,
                part.StartedUtc is { } started ? started.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : "Not known",
                WindowsPreferenceTimeline.ClockOf(part.DurationNanoseconds),
                Count(part.Screens.Screens.Count, "screen", "screens") + ", " + Count(part.Screens.Visits.Count, "visit", "visits"),
                (part.EvidenceKinds.Count == 0 ? "No phone evidence besides the accessibility tree" : string.Join("; ", part.EvidenceKinds)) +
                    (part.Incomplete is { } incomplete ? ". Recording file read in part: " + incomplete : ""),
            ]));
        }

        private void HowToRead()
        {
            Heading(2, "How to read this report");
            Paragraph("The report is arranged by screen, in the order the screens were first visited, after the findings of the app as a whole. Each visit to a screen has the screen's picture, with each finding's item outlined and numbered, and each finding has its own section.");
            Item("An error is a check that failed on the recorded evidence.");
            Item("A warning is a likely issue that a person needs to confirm, for example with TalkBack or a keyboard; its impact is given as if confirmed.");
            Item("Information is a note that may point to an issue; its impact is also given as if confirmed.");
            Paragraph("The impact, high, medium or low, is the recorder's default judgement of each kind of issue once confirmed, from its guidance file; it is not measured from this app:");
            foreach (var step in guidance.Impact)
            {
                Item($"{step.Label}: {step.Meaning}");
            }

            if (_several)
            {
                Paragraph("The project's recordings are taken together. A screen seen in more than one recording, in the same app, with the same title and activity, is one screen, numbered by its first visit in the first recording it was seen in. Its visits are named by their recording and their number in it, as \"Recording 2, visit 3\".");
            }

            Paragraph(_several
                ? "A finding seen on more than one visit to a screen, in one recording or in several, the same check at the same level on the same item, is given once, under \"Findings on more than one visit\" before the screen's visits, with what was measured and when it was seen on each visit, and the app's version in each recording."
                : "A finding seen on more than one visit to a screen, the same check at the same level on the same item, is given once, under \"Findings on more than one visit\" before the screen's visits, with what was measured and when it was seen on each visit.");
            Paragraph("In the pictures, each item is outlined in cyan between two black bands, with the finding's number in black on cyan beside it. A small item also has a zoomed picture, taken from the picture at full size. A finding about the whole screen has no outline.");
            Paragraph("The remediation is given for Android's own Views and Jetpack Compose, and also for Flutter or React Native when the recorder found that framework's library in the app. The recorder reads which libraries are bundled in the app's files, not which framework drew each screen, and not finding one does not mean the app is native Android.");
            Paragraph(_several
                ? "References such as [E3] are to the evidence list at the end, which gives each one's recording and its time in that recording."
                : "References such as [E3] are to the evidence list at the end, which gives each one's time in the recording.");
        }

        // Whether a check's findings are a model's inferences, by the kind
        // of model: "cloud", "local" or "cloud and local"; null for none.
        private string? ChecksKind
        {
            get
            {
                var inferences = parts.SelectMany(part => part.Audit.Inferences).ToList();
                if (inferences.Count == 0)
                {
                    return null;
                }

                var cloud = inferences.Any(inference => inference.Cloud);
                var local = inferences.Any(inference => !inference.Cloud);
                return cloud && local ? "cloud and local" : cloud ? "cloud" : "local";
            }
        }

        // The kinds of model of the summaries and of the checks together;
        // null where the summaries' kind was not recorded.
        private static string? Merged(string? summaries, bool summariesUnknown, string? checks)
        {
            if (summariesUnknown)
            {
                return null;
            }

            var kinds = new[] { summaries, checks }.OfType<string>().SelectMany(kind => kind.Split(" and ")).ToHashSet(StringComparer.Ordinal);
            return kinds.Contains("cloud") && kinds.Contains("local") ? "cloud and local" : kinds.Contains("cloud") ? "cloud" : "local";
        }

        // The client's sentence on AI, with the kind of model that wrote the
        // summaries or answered a check (android-vision-checks.md, requirement 3).
        private static string UsedAi(string? kind) => kind switch
        {
            null => "AI is used in generating this report, though whether its model was cloud or local was not recorded, and the final report is manually inspected.",
            "cloud and local" => "AI is used in generating this report, with cloud and local models, and the final report is manually inspected.",
            _ => $"AI is used in generating this report, with a {kind} model, and the final report is manually inspected.",
        };

        private static string Inferences(string? kind) => kind switch
        {
            null => "a model's inferences",
            "cloud and local" => "cloud and local models' inferences",
            _ => $"a {kind} model's inferences",
        };

        private void Overview(AndroidModelText text)
        {
            Heading(2, _client ? "Overview" : "Overview, inferred by a model");
            if (text.Overall is null)
            {
                Paragraph(_client ? "The recording has no findings." : "No overview was written, as the recording has no findings.");
                return;
            }

            Claims(text.Overall, "app", _client ? "The app" : "The app, inferred");
            Claims(text.Overall, "barriers", _client ? "The main barriers" : "The main barriers, inferred");
            Claims(text.Overall, "priorities", _client ? "What to fix first" : "What to fix first, inferred");
        }

        private void Claims(AcceptedStep step, string section, string heading)
        {
            var claims = step.Claims.Where(claim => claim.Section == section).ToList();
            if (claims.Count == 0 && _client)
            {
                return;
            }

            Heading(3, heading);
            if (claims.Count == 0)
            {
                Paragraph("The model wrote nothing here.");
                return;
            }

            foreach (var paragraph in claims.GroupBy(claim => claim.Paragraph).OrderBy(group => group.Key))
            {
                Paragraph(string.Join(' ', paragraph.OrderBy(claim => claim.Ordinal).Select(Statement)));
            }
        }

        private void AtAGlance()
        {
            Heading(2, "At a glance");
            var all = evidence.All;
            Paragraph(Count(all.Count, "finding", "findings") + ": " +
                Count(all.Count(f => f.Finding.Level == PhoneCheckLevel.Error), "error", "errors") + ", " +
                Count(all.Count(f => f.Finding.Level == PhoneCheckLevel.Warning), "warning", "warnings") + " and " +
                Count(all.Count(f => f.Finding.Level == PhoneCheckLevel.Info), "note of information", "notes of information") + ".");
            Heading(3, "By WCAG success criterion");
            var byCriterion = all
                .SelectMany(finding => (finding.Guidance?.Criteria ?? []).DefaultIfEmpty("").Select(criterion => (Key: criterion, Finding: finding)))
                .GroupBy(pair => pair.Key, pair => pair.Finding)
                .OrderBy(group => group.Key.Length == 0 ? 1 : 0)
                .ThenBy(group => CriterionOrder(group.Key))
                .ToList();
            if (byCriterion.Count == 0)
            {
                Paragraph("No findings.");
            }
            else
            {
                Table(["Success criterion", "Findings", "Errors", "Warnings", "Highest impact"], byCriterion.Select(group => (IReadOnlyList<string>)
                [
                    group.Key.Length == 0 ? "None: best practice" : guidance.CriterionText(group.Key),
                    .. Counts(group.ToList()),
                ]));
            }

            Heading(3, "By screen");
            var rows = new List<IReadOnlyList<string>>();
            if (evidence.App.Count > 0)
            {
                rows.Add(["Across the app", "", .. Counts(evidence.App)]);
            }

            foreach (var screen in evidence.Screens)
            {
                rows.Add([ScreenHeading(screen), screen.Visits.Count.ToString(CultureInfo.InvariantCulture), .. Counts([.. evidence.For(screen)])]);
            }

            Table(["Screen", "Visits", "Findings", "Errors", "Warnings", "Highest impact"], rows);
        }

        private IReadOnlyList<string> Counts(IReadOnlyList<NumberedFinding> findings)
        {
            var highest = findings.Count == 0 ? 0 : findings.Max(finding => finding.Rank);
            return
            [
                findings.Count.ToString(CultureInfo.InvariantCulture),
                findings.Count(finding => finding.Finding.Level == PhoneCheckLevel.Error).ToString(CultureInfo.InvariantCulture),
                findings.Count(finding => finding.Finding.Level == PhoneCheckLevel.Warning).ToString(CultureInfo.InvariantCulture),
                guidance.OfRank(highest)?.Label ?? "None",
            ];
        }

        private static string CriterionOrder(string number) =>
            string.Join('.', number.Split('.').Select(part => part.PadLeft(3, '0')));

        private void AcrossTheApp()
        {
            if (evidence.App.Count == 0)
            {
                return;
            }

            Heading(2, _client ? "Findings across the app" : "Across the app");
            Paragraph("These findings are about the app as a whole, not one screen.");
            foreach (var finding in evidence.App)
            {
                Finding(finding, 3, pictures: false, app: true);
            }
        }

        private static string ScreenHeading(ReportScreen screen) =>
            $"Screen {screen.Number.ToString(CultureInfo.InvariantCulture)}: {screen.Name}";

        // In the client's report the screens are under "Findings by screen",
        // a level lower, so that no heading level is skipped.
        private int ScreenLevel => _client ? 3 : 2;

        private void Screen(ReportScreen screen)
        {
            Heading(ScreenLevel, ScreenHeading(screen));
            var findings = evidence.For(screen).ToList();
            var repeated = evidence.Repeated(screen);
            var several = repeated.Count == 0 ? "" : $", {repeated.Count.ToString(CultureInfo.InvariantCulture)} of them seen on more than one visit";
            var where = _several ? $" Seen in {RecordingsOf(screen)}." : "";
            Paragraph(string.Create(CultureInfo.InvariantCulture,
                $"The app {screen.Package}; the screen is {screen.NamedBy}.{where} Visited {Times(screen.Visits.Count)}, with {Count(findings.Count, "finding", "findings")}{several}.{Refs(evidence.ScreenReference(screen))}"));
            var summarized = model is null ? null : screen.Members.FirstOrDefault(model.Screens.ContainsKey);
            if (model is not null && (!_client || summarized is not null))
            {
                Heading(ScreenLevel + 1, _client ? "Summary of the screen" : "Summary of the screen, inferred by a model");
                if (summarized is not null && model.Screens.TryGetValue(summarized, out var step))
                {
                    foreach (var section in new[] { "screen", "issues" })
                    {
                        foreach (var paragraph in step.Claims.Where(claim => claim.Section == section).GroupBy(claim => claim.Paragraph).OrderBy(group => group.Key))
                        {
                            Paragraph(string.Join(' ', paragraph.OrderBy(claim => claim.Ordinal).Select(Statement)));
                        }
                    }
                }
                else
                {
                    Paragraph("No summary was asked for, as the screen has no findings.");
                }
            }

            // The visits' pictures are drawn first, as a finding on more
            // than one visit shows its item zoomed from each.
            foreach (var visit in screen.Visits)
            {
                _overviews[visit] = Overviews(visit);
            }

            if (repeated.Count > 0)
            {
                Heading(ScreenLevel + 1, "Findings on more than one visit");
                Paragraph(_several && screen.Members.Count > 1
                    ? "Each of these was found on more than one visit to the screen, in one recording or in several: the same check, at the same level, on the same item. It is given once here, and each visit's picture outlines it with the same number."
                    : "Each of these was found on more than one visit to the screen: the same check, at the same level, on the same item. It is given once here, and each visit's picture outlines it with the same number.");
                foreach (var finding in repeated)
                {
                    Finding(finding, ScreenLevel + 2, pictures: true);
                }
            }

            foreach (var visit in screen.Visits)
            {
                Visit(visit);
            }
        }

        // The recordings a screen was seen in, as "recordings 1 and 3".
        private string RecordingsOf(ReportScreen screen)
        {
            var numbers = screen.Visits.Select(evidence.RecordingOf).Distinct().Select(number => number.ToString(CultureInfo.InvariantCulture)).ToList();
            return numbers.Count == 1 ? "recording " + numbers[0] : "recordings " + List(numbers);
        }

        // The app's version in a visit's recording, as each finding in a
        // project's report is labelled (owner, 2026-11-14).
        private string VersionAt(PhoneVisit visit)
        {
            var part = Part(visit);
            var app = visit.Screen.Package;
            return !part.Device.VersionsRecorded ? "version not recorded"
                : part.Device.VersionOf(app) is { Found: true, VersionName: { Length: > 0 } name } ? "version " + name
                : "version not read";
        }

        private static string Count(int count, string one, string many) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

        private static string Sentence(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        private static string Times(int count) => count switch
        {
            1 => "once",
            2 => "twice",
            _ => count.ToString(CultureInfo.InvariantCulture) + " times",
        };

        private void Visit(PhoneVisit visit)
        {
            Heading(ScreenLevel + 1, $"{Sentence(evidence.VisitName(visit))}, {visit.When}");
            Paragraph($"{Sentence(visit.CauseText)}.{Refs(evidence.VisitReference(visit))}");
            var on = evidence.On(visit);
            if (on.Count == 0)
            {
                if (visit.IsReturn)
                {
                    Paragraph("No findings on this visit. The screen's picture is with its first visit.");
                    return;
                }

                if (FirstPicture(visit) is { } picture)
                {
                    Write(Overview(visit, picture, []));
                }
                else
                {
                    Paragraph("No picture of this visit was recorded.");
                }

                Paragraph(visit.SettledSnapshots.Count == 0
                    ? "Not checked: the screen did not settle while the tree was recorded."
                    : "No findings on this visit.");
                return;
            }

            Write(_overviews.GetValueOrDefault(visit) ?? []);
            var elsewhere = on.Where(pair => pair.Numbered.OnSeveralVisits).Select(pair => pair.Numbered.Number.ToString(CultureInfo.InvariantCulture)).ToList();
            if (elsewhere.Count > 0)
            {
                Paragraph(elsewhere.Count == 1
                    ? $"Finding {elsewhere[0]} on this visit is under \"Findings on more than one visit\" for the screen, as it was seen on other visits too."
                    : $"Findings {List(elsewhere)} on this visit are under \"Findings on more than one visit\" for the screen, as they were seen on other visits too.");
            }

            foreach (var finding in evidence.For(visit))
            {
                Finding(finding, ScreenLevel + 2, pictures: true);
            }
        }

        // The pictures of a visit with every finding seen on it outlined,
        // those grouped under the screen included, one picture for each
        // time the findings were first seen at.
        private List<Block> Overviews(PhoneVisit visit)
        {
            var blocks = new List<Block>();
            var groups = evidence.On(visit)
                .Select(pair => (Number: pair.Numbered.Number, pair.Seen.Finding, Picture: PictureOf(pair.Seen.Finding)))
                .GroupBy(item => item.Picture?.AbsolutePath ?? "", StringComparer.Ordinal)
                .ToList();
            foreach (var group in groups)
            {
                var picture = group.First().Picture;
                if (picture is null)
                {
                    foreach (var item in group)
                    {
                        _placed[item.Finding] = "No picture of this time was recorded.";
                    }

                    continue;
                }

                blocks.AddRange(Overview(visit, picture, [.. group.Select(item => (item.Number, item.Finding))]));
            }

            return blocks;
        }

        private void Write(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (block.Picture is { } name)
                {
                    Image(block.Text, name);
                }
                else
                {
                    Paragraph(block.Text);
                }
            }
        }

        private PhoneContrastPicture? FirstPicture(PhoneVisit visit)
        {
            var pictures = Part(visit).Pictures;
            foreach (var settled in visit.SettledSnapshots)
            {
                if (pictures.Of(visit, settled) is { } picture)
                {
                    return picture;
                }
            }

            return pictures.Between(visit.Start, visit.End);
        }

        private PhoneContrastPicture? PictureOf(PhoneFinding finding)
        {
            var first = finding.Times[0];
            var pictures = Part(finding.Visit).Pictures;
            return pictures.Of(finding.Visit, first.Snapshot) ?? pictures.Between(first.Time, finding.Visit.End);
        }

        // A picture of a visit at half size, with the findings shown on it
        // outlined and numbered.
        private List<Block> Overview(PhoneVisit visit, PhoneContrastPicture picture, IReadOnlyList<(int Number, PhoneFinding Finding)> findings)
        {
            var part = Part(visit);
            var pixels = part.Load(picture.AbsolutePath);
            if (pixels is null)
            {
                foreach (var finding in findings)
                {
                    _placed[finding.Finding] = "The picture of this time could not be read.";
                }

                return [new Block($"The picture at {WindowsPreferenceTimeline.ClockOf(picture.Time)} could not be read.", null)];
            }

            var divisor = Math.Max(1, pixels.Width / 540);
            var canvas = ReportPictures.Scaled(pixels, divisor);
            var unit = ReportPictures.Unit(canvas.Width, canvas.Height);
            var outlined = new List<int>();
            var screen = (long)pixels.Width * pixels.Height;
            foreach (var finding in findings)
            {
                var first = finding.Finding.Times[0];
                if (first.Bounds.IsEmpty)
                {
                    _placed[finding.Finding] = "This finding is about the whole screen, so no item is outlined.";
                    continue;
                }

                if (Scaled(part, first.Bounds, picture.Time, pixels.Width, pixels.Height) is not { } box)
                {
                    _placed[finding.Finding] = "Not outlined: the phone was turned between the snapshot and the picture.";
                    continue;
                }

                if (box.Area >= ReportPictures.WholeScreen * screen)
                {
                    _placed[finding.Finding] = "The item fills the screen, so it is not outlined.";
                    continue;
                }

                var small = new PictureBox(box.Left / divisor, box.Top / divisor, box.Right / divisor, box.Bottom / divisor);
                var outline = ReportPictures.Outline(canvas, small, unit);
                ReportPictures.Badge(canvas, finding.Number, outline, unit);
                outlined.Add(finding.Number);
                if (box.Area < ReportPictures.ZoomLimit * screen)
                {
                    _zoomable[finding.Finding] = (pixels, box);
                    _placed[finding.Finding] = $"Outlined as number {finding.Number.ToString(CultureInfo.InvariantCulture)} in the picture of {evidence.VisitName(visit)}, and zoomed here.";
                }
                else
                {
                    _placed[finding.Finding] = $"Outlined as number {finding.Number.ToString(CultureInfo.InvariantCulture)} in the picture of {evidence.VisitName(visit)}.";
                }
            }

            var name = Prefixed(visit, string.Create(CultureInfo.InvariantCulture, $"visit-{visit.Number:D4}-{picture.Time:D}.png"));
            Save(name, canvas);
            var source = picture.Kind == PhonePictureKind.Screenshot ? "screenshot" : "video frame";
            var at = _several ? $"in recording {evidence.RecordingOf(visit).ToString(CultureInfo.InvariantCulture)} at" : "at";
            var alt = $"The screen {evidence.ScreenOf(visit.Screen).Name} {at} {WindowsPreferenceTimeline.ClockOf(picture.Time)}, from a {source}";
            alt += outlined.Count switch
            {
                0 => ".",
                1 => $", with finding {outlined[0].ToString(CultureInfo.InvariantCulture)} outlined in cyan and numbered.",
                _ => $", with findings {List(outlined.Select(number => number.ToString(CultureInfo.InvariantCulture)).ToList())} outlined in cyan and numbered.",
            };
            return [new Block(alt, name)];
        }

        // Bounds in the phone's screen pixels scaled to a picture's; null
        // when the picture and the screen are turned differently, as
        // ScaledToFrame in the player.
        private static PictureBox? Scaled(AndroidReportInput part, PhoneBounds bounds, long time, int width, int height)
        {
            double scaleX = 1, scaleY = 1;
            if (part.ScreenAt(time) is { } screen && screen.Width > 0 && screen.Height > 0)
            {
                if (width > height != screen.Width > screen.Height)
                {
                    return null;
                }

                scaleX = (double)width / screen.Width;
                scaleY = (double)height / screen.Height;
            }

            var box = new PictureBox(
                (int)Math.Floor(bounds.Left * scaleX), (int)Math.Floor(bounds.Top * scaleY),
                (int)Math.Ceiling(bounds.Right * scaleX), (int)Math.Ceiling(bounds.Bottom * scaleY)).Clamp(width, height);
            return box.IsEmpty ? null : box;
        }

        // What was measured on one visit, the first time it was seen.
        private static string MeasuredOn(PhoneFinding finding)
        {
            var first = finding.Times[0];
            return first.Measured +
                (finding.Times.Skip(1).Any(time => time.Measured != first.Measured) ? " (other values at other times)" : "") +
                (first.State is null ? "" : $"; {first.State}");
        }

        // When it was seen on one visit. Two snapshots can be shown by the
        // same picture, when the video did not change between them; each
        // time is listed once.
        private string SeenOn(FindingVisit seen)
        {
            var finding = seen.Finding;
            var distinct = finding.Times.Select(time => time.Time).Distinct().ToList();
            var shown = distinct.Take(10).Select(WindowsPreferenceTimeline.ClockOf).ToList();
            return Sentence(Times(finding.Times.Count)) +
                (distinct.Count < finding.Times.Count ? ", in " + Count(distinct.Count, "picture", "pictures") : "") +
                ", at " + string.Join(", ", shown) +
                (distinct.Count > shown.Count ? $" and {(distinct.Count - shown.Count).ToString(CultureInfo.InvariantCulture)} more" : "") +
                Refs(seen.Reference);
        }

        private void Finding(NumberedFinding numbered, int headingLevel, bool pictures, bool app = false)
        {
            var finding = numbered.Finding;
            var check = numbered.Guidance;
            Heading(headingLevel, numbered.Heading);
            // A level a model's answers gave is its inference, not
            // confirmed (android-vision-checks.md, decision 2).
            var inferred = finding.Inference is { Inferred: true };
            var impact = numbered.Impact is not { } step ? "Not given: the guidance does not cover this check"
                : step.Label + (finding.Level == PhoneCheckLevel.Error && !inferred ? "" : ", if confirmed");
            var level = (finding.Level, inferred) switch
            {
                (PhoneCheckLevel.Error, true) => "Error, inferred by a model, for a person to confirm",
                (PhoneCheckLevel.Error, false) => "Error",
                (PhoneCheckLevel.Warning, _) => "Warning, for a person to confirm",
                (PhoneCheckLevel.Info, true) => "Information, inferred by a model, for a person to judge",
                (PhoneCheckLevel.Info, false) => "Information, for a person to judge",
                _ => "Not run",
            };
            // On more than one visit, what was measured is given once if
            // the same on each, and each visit's when seen.
            // In a project's report each is labelled with the app's
            // version in its recording; a finding of the app as a whole is
            // seen once in each recording.
            string Where(FindingVisit on) => app
                ? $"recording {evidence.RecordingOf(on.Visit).ToString(CultureInfo.InvariantCulture)}"
                : evidence.VisitName(on.Visit);
            string Version(FindingVisit on) => _several ? $" ({VersionAt(on.Visit)})" : "";
            var measures = numbered.Visits.Select(seen => MeasuredOn(seen.Finding)).ToList();
            var measured = measures.Distinct(StringComparer.Ordinal).Count() == 1 ? measures[0]
                : string.Join("; ", numbered.Visits.Select((seen, index) => $"{Sentence(Where(seen))}: {measures[index]}"));
            var seen = !numbered.OnSeveralVisits
                ? (_several ? $"{Sentence(Where(numbered.Visits[0]))}{Version(numbered.Visits[0])}, {Lower(SeenOn(numbered.Visits[0]))}" : SeenOn(numbered.Visits[0]))
                : (app ? $"In {Count(numbered.Visits.Count, "recording", "recordings")}: " : $"On {Count(numbered.Visits.Count, "visit", "visits")}: ") +
                    string.Join("; ", numbered.Visits.Select(on => $"{Where(on)}{Version(on)}, {Lower(SeenOn(on))}"));
            var rows = new List<IReadOnlyList<string>>
            {
                new[] { "Level", level },
                new[] { "Impact", impact },
                new[] { "WCAG success criteria", Criteria(check) },
                new[] { "Item", finding.Label },
                new[] { "Measured", measured },
            };
            var inferences = numbered.Visits.Select(on => on.Finding.Inference?.Text).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            if (inferences.Count > 0)
            {
                rows.Add(["Model's answer", string.Join("; ", inferences)]);
            }

            rows.AddRange(new List<IReadOnlyList<string>>
            {
                new[] { "Seen", seen },
                new[] { "Check", PhoneCheckNames.Title(finding.Check) + (check is null ? "" : ". " + check.Basis) },
            });
            Table(["Field", "Value"], rows);
            if (pictures)
            {
                foreach (var on in numbered.Visits)
                {
                    Picture(numbered, on);
                }
            }

            if (check is null)
            {
                Paragraph("The guidance file has no text for this check.");
                return;
            }

            Paragraph("What the issue is: " + check.Issue);
            Paragraph("Why it matters: " + check.Why);
            Paragraph("Who it affects: " + string.Join("; ", check.Affects) + ".");
            Paragraph("Suggested remediation for Android: " + check.Android);
            var frameworks = Frameworks(finding.Screen.Package);
            if (frameworks.Contains("Flutter"))
            {
                Paragraph("Suggested remediation for Flutter: " + check.Flutter);
            }

            if (frameworks.Contains("React Native"))
            {
                Paragraph("Suggested remediation for React Native: " + check.ReactNative);
            }

            Paragraph("Sources:");
            foreach (var key in check.Sources)
            {
                // A framework's sources go with its remediation, only when the
                // framework was found.
                var framework = key.StartsWith("flutter-", StringComparison.Ordinal) ? "Flutter"
                    : key.StartsWith("rn-", StringComparison.Ordinal) ? "React Native"
                    : null;
                if ((framework is null || frameworks.Contains(framework)) && guidance.Sources.TryGetValue(key, out var source))
                {
                    // The client's report links each source by its title,
                    // so that the link says where it goes.
                    Item(_client ? $"[{source.Title}]({source.Url})" : $"{source.Title}: {source.Url}");
                }
            }
        }

        private string Criteria(CheckGuidance? check)
        {
            if (check is null)
            {
                return "Unknown";
            }

            if (check.Criteria.Count > 0)
            {
                return string.Join("; ", check.Criteria.Select(guidance.CriterionText));
            }

            return check.Related.Count == 0 ? "None: best practice"
                : "None: best practice, related to " + string.Join("; ", check.Related.Select(guidance.CriterionText));
        }

        // The frameworks found in the app in any of the report's recordings.
        private HashSet<string> Frameworks(string package)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                if (part.Device.VersionOf(package)?.Framework is { Status: "detected" } framework)
                {
                    names.UnionWith(framework.Names);
                }
            }

            return names;
        }

        private void Picture(NumberedFinding numbered, FindingVisit seen)
        {
            var finding = seen.Finding;
            var visit = seen.Visit;
            var name2 = evidence.VisitName(visit);
            if (_zoomable.TryGetValue(finding, out var zoom))
            {
                var (crop, scale) = ReportPictures.ZoomOf(zoom.Box, zoom.Pixels.Width, zoom.Pixels.Height);
                if (!crop.IsEmpty)
                {
                    var canvas = ReportPictures.Zoomed(zoom.Pixels, crop, scale);
                    var item = new PictureBox(
                        (zoom.Box.Left - crop.Left) * scale, (zoom.Box.Top - crop.Top) * scale,
                        (zoom.Box.Right - crop.Left) * scale, (zoom.Box.Bottom - crop.Top) * scale);
                    var unit = Math.Max(2, ReportPictures.Unit(canvas.Width, canvas.Height));
                    var outline = ReportPictures.Outline(canvas, item, unit);
                    ReportPictures.Badge(canvas, numbered.Number, outline, unit);
                    var name = Prefixed(visit, string.Create(CultureInfo.InvariantCulture, $"finding-{numbered.Number:D4}-visit-{visit.Number:D4}.png"));
                    Save(name, canvas);
                    var times = scale == 1 ? "at full size" : $"zoomed {scale.ToString(CultureInfo.InvariantCulture)} times";
                    var screenName = evidence.ScreenOf(visit.Screen).Name;
                    var where = numbered.OnSeveralVisits || _several ? $"on the screen {screenName} in {name2}" : $"on the screen {screenName}";
                    Paragraph("Picture: " + _placed[finding]);
                    Image($"Finding {numbered.Number.ToString(CultureInfo.InvariantCulture)}, {finding.Label}, {where}, {times}, outlined in cyan.", name);
                    return;
                }
            }

            var placed = _placed.GetValueOrDefault(finding) ?? "No picture of this time was recorded.";
            Paragraph("Picture: " + ((numbered.OnSeveralVisits || _several) && !placed.Contains("visit ", StringComparison.Ordinal) ? $"On {name2}: {Lower(placed)}" : placed));
        }

        private static string Lower(string text) =>
            text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

        private void Limits()
        {
            Heading(2, "Limits");
            Item(_project
                ? "The checks cover what the recordings show: the screens visited, in the states they were seen in. A screen or state not recorded is not checked."
                : "The checks cover what the recording shows: the screens visited, in the states they were seen in. A screen or state not recorded is not checked.");
            Item("No finding on a screen does not show that it meets WCAG; many success criteria need a person's judgement or a test the recording cannot make.");
            Item("Warnings and information need a person to confirm them; their impact is given as if confirmed.");
            Item("The impact levels and the remediation are the recorder's guidance for each kind of issue, not advice written for this app.");
            Item("Colours measured on a video frame are approximate, as the video is compressed; those measured on the companion app's screenshots are exact.");
            if (_project)
            {
                Item("The report covers each of the project's recordings in whole.");
                Item("A screen is one screen across the recordings when its app, title and activity are the same, as within a recording. A screen named from its own text can be named differently in two recordings, if that text was shared by other screens in one of them, and is then given as two screens.");
                foreach (var left in skipped)
                {
                    Item("Left out: " + left);
                }
            }
            else
            {
                Item("The report covers the whole recording, whatever part of it was chosen when it was queued.");
            }

            for (var index = 0; index < parts.Count; index++)
            {
                var part = parts[index];
                var of = _several ? $"In recording {(index + 1).ToString(CultureInfo.InvariantCulture)}, the" : "The";
                var check = _several ? $"In recording {(index + 1).ToString(CultureInfo.InvariantCulture)}, a" : "A";
                if (part.Audit.MeasureProblem is { } problem)
                {
                    Item($"{of} colours could not be measured, so the contrast, image of text, label in name, text shown as a heading and image text alternative checks did not run: " + problem);
                }

                if (part.Audit.ImagesOfText?.ModelNote is { } note)
                {
                    Item($"{of} images-of-text check's model: {note}.");
                }

                if (part.Audit.HeadingLook?.ModelNote is { } headingNote)
                {
                    Item($"{of} text-shown-as-heading check's model: {headingNote}.");
                }

                if (part.Audit.ImageAlternative?.ModelNote is { } alternativeNote)
                {
                    Item($"{of} image-text-alternative check's model: {alternativeNote}.");
                }

                foreach (var problem2 in part.Audit.Tree.Problems)
                {
                    Item($"{check} check could not run: " + problem2);
                }
            }
        }

        private void Evidence()
        {
            Heading(2, "Evidence");
            if (!_several)
            {
                Paragraph("Each app, screen, visit and finding the report cites, with its time in the recording.");
                Table(["Ref", "Time", "Evidence"], evidence.Entries
                    .Where(entry => _cited.Contains(entry.Reference))
                    .Select(entry => (IReadOnlyList<string>)[entry.Reference, entry.Time is { } time ? WindowsPreferenceTimeline.ClockOf(time) : "Whole recording", entry.Text]));
                return;
            }

            Paragraph("Each app, screen, visit and finding the report cites, with its recording and its time in that recording.");
            Table(["Ref", "Recording and time", "Evidence"], evidence.Entries
                .Where(entry => _cited.Contains(entry.Reference))
                .Select(entry => (IReadOnlyList<string>)[entry.Reference, When(entry.Recording, entry.Time), entry.Text]));

            static string When(int recording, long? time) => recording == 0 ? "All recordings"
                : $"Recording {recording.ToString(CultureInfo.InvariantCulture)}, " + (time is { } at ? WindowsPreferenceTimeline.ClockOf(at) : "whole recording");
        }

        private string Refs(params string[] references)
        {
            foreach (var reference in references)
            {
                _cited.Add(reference);
            }

            return references.Length == 0 ? "" : " " + string.Join(' ', references.Select(reference => $"[{reference}]"));
        }

        private void Save(string name, ReportCanvas canvas)
        {
            Directory.CreateDirectory(picturesFolder);
            File.WriteAllBytes(Path.Combine(picturesFolder, name), ReportPictures.Png(ReportPictures.Edged(canvas)));
        }

        private void Heading(int level, string text)
        {
            EndList();
            _text.Append('#', level).Append(' ').Append(SummaryReport.OneLine(text)).Append("\n\n");
        }

        private void Paragraph(string text)
        {
            EndList();
            foreach (var reference in ReportDocument.ReferencesIn(text))
            {
                _cited.Add(reference);
            }

            _text.Append(Escaped(SummaryReport.OneLine(text))).Append("\n\n");
        }

        private void Item(string text)
        {
            foreach (var reference in ReportDocument.ReferencesIn(text))
            {
                _cited.Add(reference);
            }

            _text.Append("- ").Append(SummaryReport.OneLine(text)).Append('\n');
        }

        private void Image(string alt, string name)
        {
            EndList();
            var path = picturesRelative.TrimEnd('/') + "/" + name;
            _text.Append("![").Append(SummaryReport.OneLine(alt).Replace("]", ")", StringComparison.Ordinal)).Append("](")
                .Append(Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.Ordinal)).Append(")\n\n");
        }

        private void Table(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
        {
            EndList();
            _text.Append("| ").Append(string.Join(" | ", header.Select(SummaryReport.Cell))).Append(" |\n");
            _text.Append('|').Append(string.Concat(header.Select(_ => " --- |"))).Append('\n');
            foreach (var row in rows)
            {
                foreach (var reference in row.SelectMany(ReportDocument.ReferencesIn))
                {
                    _cited.Add(reference);
                }

                _text.Append("| ").Append(string.Join(" | ", row.Select(SummaryReport.Cell))).Append(" |\n");
            }

            _text.Append('\n');
        }

        // A list ends with a blank line before the next block.
        private void EndList()
        {
            if (_text.Length > 1 && _text[^1] == '\n' && _text[^2] != '\n')
            {
                _text.Append('\n');
            }
        }

        // A paragraph that would read as another block is kept a paragraph.
        private static string Escaped(string text) =>
            text.Length > 0 && (text[0] is '#' or '|' or '>' || text.StartsWith("- ", StringComparison.Ordinal) ||
                text.StartsWith("* ", StringComparison.Ordinal) || text.StartsWith("![", StringComparison.Ordinal)) ? "\u200B" + text : text;

        private static string List(IReadOnlyList<string> items) =>
            items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
    }
}
