using System.Globalization;
using System.Text;

namespace Recorder.Analysis.Summary;

/// <summary>
/// Writes the recording summary's Markdown report from the recorder's
/// collation and the accepted, checked model output, never from the
/// model's text as written. Each statement ends with the references it
/// cites, and the evidence list gives each cited reference's time and what
/// it is. See docs/architecture/recording-summary.md, "The report".
/// </summary>
public static class SummaryReport
{
    private static readonly Dictionary<string, string> SectionHeadings = new(StringComparer.Ordinal)
    {
        ["page"] = "The page",
        ["purpose"] = "What it is for",
        ["activity"] = "What the participant did",
        ["site"] = "The site or application",
        ["audience"] = "Who it appears to be for",
        ["session"] = "The session",
        ["journey"] = "The participant's journey",
        ["screen"] = "On the screen",
        ["change"] = "What changed",
        ["changes"] = "How the page changed"
    };

    public static string Write(
        string sessionKey,
        RecordingCollation collation,
        ParticipantCapabilities capabilities,
        IReadOnlyList<(VisitCollation Visit, AcceptedStep Step)> visits,
        AcceptedStep? overall,
        IReadOnlyList<AttemptRecord> attempts,
        CollationSettings settings,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(collation);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(visits);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(settings);
        var citations = new ReportCitations(collation);
        var builder = new StringBuilder();
        string Refs(IEnumerable<string> references) => citations.Refs(references);

        void Claims(AcceptedStep step, IReadOnlyList<string> sections, string level)
        {
            foreach (var section in sections)
            {
                var claims = step.Claims.Where(claim => claim.Section == section).ToList();
                builder.Append(level).Append(' ').Append(SectionHeadings[section]).Append("\n\n");
                if (claims.Count == 0)
                {
                    builder.Append("The model gave nothing for this part.\n\n");
                    continue;
                }

                foreach (var paragraph in claims.GroupBy(claim => claim.Paragraph).OrderBy(group => group.Key))
                {
                    builder.Append(string.Join(' ', paragraph.OrderBy(claim => claim.Ordinal)
                        .Select(claim => OneLine(claim.Text) + Refs(claim.Evidence))));
                    builder.Append("\n\n");
                }
            }
        }

        var accepted = Accepted(attempts);
        builder.Append("# Summary of recording ").Append(sessionKey).Append("\n\n");
        var date = nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (accepted is not null)
        {
            AppendInferredBy(builder, accepted, date);
            builder.Append("Every statement below is a model's inference from the recorded evidence cited after it. Page visits are derived by the recorder.\n\n");
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture, $"Written by the recorder on {date}. No model was asked, because no page visits were recorded.\n\n");
        }

        AppendCapabilities(builder, collation, capabilities, citations);

        if (overall is not null)
        {
            builder.Append("## The site tested\n\n");
            Claims(overall, ["site", "audience"], "###");
            builder.Append("## The session\n\n");
            builder.Append(string.Join("\n\n", overall.Claims.Where(claim => claim.Section == "session")
                .GroupBy(claim => claim.Paragraph).OrderBy(group => group.Key)
                .Select(paragraph => string.Join(' ', paragraph.OrderBy(claim => claim.Ordinal).Select(claim => OneLine(claim.Text) + Refs(claim.Evidence))))));
            if (!overall.Claims.Any(claim => claim.Section == "session"))
            {
                builder.Append("The model gave nothing for this part.");
            }

            builder.Append("\n\n");
        }

        builder.Append("## Page visits\n\n");
        if (collation.Visits.Count == 0)
        {
            builder.Append("No page visits were recorded: the recording holds no committed navigation of a tab's page.\n\n");
        }

        foreach (var visit in collation.Visits)
        {
            var start = visit.Visit.Start;
            var heading = visit.Title is { Length: > 0 } title ? OneLine(title) : start.Url;
            builder.Append(CultureInfo.InvariantCulture,
                $"### {visit.Visit.Number}. {heading}, {Clock(start.Time)} to {Clock(visit.Visit.End)}\n\n");
            AppendDerived(builder, visit, citations);
            builder.Append('\n');
            var described = visits.FirstOrDefault(item => item.Visit.Visit.Number == visit.Visit.Number);
            if (described.Step is null)
            {
                builder.Append("No model description of this visit was accepted.\n\n");
            }
            else
            {
                Claims(described.Step, RecordingSummaryKind.VisitSections, "####");
            }
        }

        AppendOtherWindows(builder, collation, citations);
        AppendEvidence(builder, collation, citations);
        builder.Append("## How this was made\n\n");
        AppendModel(builder, accepted);
        builder.Append(CultureInfo.InvariantCulture,
            $"- Prompt templates: {RecordingSummaryKind.VisitTemplate} version {RecordingSummaryKind.TemplateVersion} for each visit, " +
            $"{RecordingSummaryKind.RecordingTemplate} version {RecordingSummaryKind.TemplateVersion} for the whole recording.\n");
        AppendLimitsAndAttempts(builder, collation, settings, attempts);
        return builder.ToString();
    }

    /// <summary>The attempt whose model and profile the report names: the first accepted, or else the first.</summary>
    internal static AttemptRecord? Accepted(IReadOnlyList<AttemptRecord> attempts) =>
        attempts.FirstOrDefault(attempt => attempt.Outcome == "accepted") ?? attempts.FirstOrDefault();

    internal static void AppendInferredBy(StringBuilder builder, AttemptRecord accepted, string date)
    {
        var quantization = accepted.ModelQuantization is { Length: > 0 } value ? $" ({value})" : "";
        builder.Append(CultureInfo.InvariantCulture,
            $"Inferred by {accepted.Model}{quantization}, profile \"{accepted.ProfileName}\", {date}. ");
    }

    /// <summary>The participant's capabilities, as given to the model, and the assistive technology observed running.</summary>
    internal static void AppendCapabilities(
        StringBuilder builder, RecordingCollation collation, ParticipantCapabilities capabilities, ReportCitations citations, string heading = "## The participant's capabilities")
    {
        builder.Append(heading).Append("\n\n");
        if (capabilities.Description is { Length: > 0 } description)
        {
            var written = capabilities.WrittenUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "an unrecorded date";
            var reference = collation.Evidence.Entries.FirstOrDefault(entry => entry.EventId is null && entry.Text.StartsWith("The participant's capabilities", StringComparison.Ordinal));
            builder.Append(CultureInfo.InvariantCulture, $"Reported by the auditor on {written}, a person's statement and not an observation:\n\n");
            builder.Append("> ").Append(OneLine(description)).Append(reference is null ? "" : citations.Refs([reference.Reference])).Append("\n\n");
        }
        else
        {
            builder.Append("Unknown: no description of the participant's capabilities was given.\n\n");
        }

        // Found by its evidence entry, not by the line's start: since the line
        // gained its reference it starts "[E1]", and on the target machine on
        // 2026-10-14 the report said nothing was running while NVDA was.
        var observed = collation.Evidence.Entries.FirstOrDefault(entry =>
            entry.EventId is null && entry.Text.StartsWith("Observed running at the start of the recording", StringComparison.Ordinal));
        builder.Append(observed is null
            ? "No assistive technology was observed running at the start of the recording."
            : observed.Text + ". This shows what was running, not what the participant is able to do." + citations.Refs([observed.Reference])).Append("\n\n");
    }

    /// <summary>What the recorder derived of a visit: its kind and address, why it is a visit of its own, the visit it returns to, and its in-page jumps.</summary>
    internal static void AppendDerived(StringBuilder builder, VisitCollation visit, ReportCitations citations)
    {
        var start = visit.Visit.Start;
        builder.Append(CultureInfo.InvariantCulture, $"Derived by the recorder: a visit to a {visit.Visit.Label}, {start.Url}.");
        builder.Append(citations.RefOf(start.EventId) is { } startReference ? citations.Refs([startReference]) : "").Append('\n');
        if (visit.Visit.Kind == VisitKind.FragmentRoute && visit.Visit.RouteReason is { } reason)
        {
            builder.Append(CultureInfo.InvariantCulture, $"It is a visit of its own because {reason}.\n");
        }

        if (visit.Visit.ReturnOf is { } earlier)
        {
            builder.Append(CultureInfo.InvariantCulture, $"A return to the URL of visit {earlier}, judged by URL alone.\n");
        }

        if (visit.Visit.Steps.Count > 0)
        {
            builder.Append("\nIn-page jumps within the visit, derived by the recorder:\n\n");
            foreach (var step in visit.Visit.Steps)
            {
                var link = step.LinkName is { Length: > 0 } name ? $" \"{OneLine(name)}\"" : "";
                builder.Append(CultureInfo.InvariantCulture,
                    $"- {Clock(step.Commit.Time)}, #{step.Fragment}: a {step.Name}{link} to {step.Target} ({step.Rule}).");
                builder.Append(citations.RefOf(step.Commit.EventId) is { } stepReference ? citations.Refs([stepReference]) : "").Append('\n');
            }
        }
    }

    internal static void AppendOtherWindows(StringBuilder builder, RecordingCollation collation, ReportCitations citations, string heading = "## Other windows")
    {
        builder.Append(heading).Append("\n\n");
        if (collation.OtherWindows.Count == 0)
        {
            builder.Append("No other program's window was recorded in front.\n\n");
            return;
        }

        builder.Append("Other programs' windows the recorder found in front, derived from the front window records:\n\n");
        foreach (var window in collation.OtherWindows)
        {
            builder.Append(CultureInfo.InvariantCulture,
                // To the millisecond, as a window in front for less than a
                // second read "00:00:00 to 00:00:00" (target report,
                // 2026-10-18).
                $"- {SummaryCollator.Time(window.Start)} to {SummaryCollator.Time(window.End)}: {window.Process}, \"{OneLine(window.Title)}\".{citations.Refs([window.Reference])}\n");
        }

        builder.Append('\n');
    }

    /// <summary>The evidence list: each reference cited, with its time and what it is.</summary>
    internal static void AppendEvidence(StringBuilder builder, RecordingCollation collation, ReportCitations citations, string heading = "## Evidence")
    {
        builder.Append(heading).Append("\n\n");
        // Only cited evidence is listed, so the numbers have gaps (target
        // report, 2026-10-18).
        builder.Append("The evidence the report cites, in the order of its numbers. Evidence given to the model but not cited is not listed, so some numbers are missing.\n\n");
        builder.Append("| Ref | Time | Evidence |\n| --- | --- | --- |\n");
        foreach (var number in citations.Cited)
        {
            var reference = "E" + number.ToString(CultureInfo.InvariantCulture);
            if (collation.Evidence.Of(reference) is { } entry)
            {
                var record = entry.EventId is { } eventId ? $" (record {eventId})" : "";
                builder.Append(CultureInfo.InvariantCulture,
                    $"| {reference} | {(entry.Time is { } time ? SummaryCollator.Time(time) : "not in the recording")} | {Cell(entry.Text + record)} |\n");
            }
            else
            {
                builder.Append(CultureInfo.InvariantCulture, $"| {reference} | unknown | Not in the recorder's evidence list. |\n");
            }
        }

        builder.Append('\n');
    }

    internal static void AppendModel(StringBuilder builder, AttemptRecord? accepted)
    {
        if (accepted is null)
        {
            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $"- Model profile: \"{accepted.ProfileName}\", model {accepted.Model}.\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Model file: {accepted.ModelFileName ?? "unknown"}, quantization {accepted.ModelQuantization ?? "unknown"}.\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Model SHA-256: {accepted.ModelSha256 ?? "unknown"} ({BasisText(accepted.ModelSha256Basis)}).\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Server build: {accepted.ServerBuild ?? "unknown"}.\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Generation settings: `{accepted.Parameters}`.\n");
    }

    /// <summary>
    /// The model, then the generation settings of the accepted answers, once
    /// where all steps share them and otherwise each set with its steps: one
    /// line for the whole report hid that the presence penalty is given to
    /// the moment descriptions only (target report, 2026-10-18).
    /// </summary>
    internal static void AppendModelPerStep(StringBuilder builder, IReadOnlyList<AttemptRecord> attempts)
    {
        var accepted = Accepted(attempts);
        if (accepted is null)
        {
            return;
        }

        var answers = attempts.Where(attempt => attempt.Outcome == AttemptOutcomes.Accepted).ToList();
        if (answers.Count == 0)
        {
            answers = [accepted];
        }

        var models = answers.GroupBy(attempt => (attempt.ProfileName, attempt.Model, attempt.ModelFileName, attempt.ModelQuantization, attempt.ModelSha256, attempt.ModelSha256Basis, attempt.ServerBuild)).ToList();
        foreach (var model in models)
        {
            var first = model.First();
            if (models.Count > 1)
            {
                builder.Append(CultureInfo.InvariantCulture, $"- For steps {string.Join(", ", model.Select(attempt => attempt.StepKey).Distinct(StringComparer.Ordinal))}:\n");
            }

            builder.Append(CultureInfo.InvariantCulture, $"- Model profile: \"{first.ProfileName}\", model {first.Model}.\n");
            builder.Append(CultureInfo.InvariantCulture, $"- Model file: {first.ModelFileName ?? "unknown"}, quantization {first.ModelQuantization ?? "unknown"}.\n");
            builder.Append(CultureInfo.InvariantCulture, $"- Model SHA-256: {first.ModelSha256 ?? "unknown"} ({BasisText(first.ModelSha256Basis)}).\n");
            builder.Append(CultureInfo.InvariantCulture, $"- Server build: {first.ServerBuild ?? "unknown"}.\n");
        }

        // The number of images and the answer's sections belong to the
        // step's request, not to how the model generated it, so they are
        // left out of the comparison and the settings shown.
        var settings = answers.GroupBy(attempt => GenerationOnly(attempt.Parameters), StringComparer.Ordinal).ToList();
        if (settings.Count == 1)
        {
            builder.Append(CultureInfo.InvariantCulture, $"- Generation settings, the same for every step: `{settings[0].Key}`.\n");
            return;
        }

        builder.Append("- Generation settings of each accepted answer, which differ between steps:\n");
        foreach (var group in settings)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"  - For {string.Join(", ", group.Select(attempt => attempt.StepKey).Distinct(StringComparer.Ordinal))}: `{group.Key}`.\n");
        }
    }

    private static string GenerationOnly(string parameters)
    {
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(parameters) is System.Text.Json.Nodes.JsonObject json)
            {
                json.Remove("images");
                json.Remove("sections");
                return Spaced(json);
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return parameters;
    }

    // As the catalog writes the parameters: a space after each colon and
    // comma.
    private static string Spaced(System.Text.Json.Nodes.JsonNode? node) => node switch
    {
        System.Text.Json.Nodes.JsonObject json => "{" + string.Join(", ", json.Select(item =>
            System.Text.Json.JsonSerializer.Serialize(item.Key) + ": " + Spaced(item.Value))) + "}",
        System.Text.Json.Nodes.JsonArray array => "[" + string.Join(", ", array.Select(Spaced)) + "]",
        null => "null",
        _ => node.ToJsonString()
    };

    /// <summary>The facts given, the reused steps, and the attempts.</summary>
    internal static void AppendLimitsAndAttempts(StringBuilder builder, RecordingCollation collation, CollationSettings settings, IReadOnlyList<AttemptRecord> attempts)
    {
        builder.Append("- Every fact of each visit is given in full, with no limit on how many or how long: a request longer than the server's context is refused by the server, " +
            "and the step then fails with the server's message, which gives the request's tokens and the context's, in the attempts below.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- An input counts as the one before a visit within {Seconds(settings.InputBeforeNanoseconds)} before it; one scroller's scroll records with no pause longer than {Seconds(settings.ScrollPauseNanoseconds)} between them are one scroll.\n");
        builder.Append("- What NVDA had to announce is the recorder's inference from the page, not a recording of speech.\n");
        var reused = attempts.Where(attempt => attempt.ReusedFromTaskId is not null).ToList();
        if (reused.Count > 0)
        {
            // Reused steps, and the tasks whose attempts the model wrote
            // (analysis-runner.md, "Reusing steps when run again").
            var steps = attempts.Select(attempt => attempt.StepKey).Distinct(StringComparer.Ordinal).Count();
            var sources = string.Join("; ", reused.GroupBy(attempt => attempt.ReusedFromTaskId!.Value).Select(group =>
                $"task {group.Key}: {string.Join(", ", group.Select(attempt => attempt.StepKey))}"));
            builder.Append(CultureInfo.InvariantCulture,
                $"- Steps reused, unchanged, from an earlier run: {reused.Count} of {steps}. Their answers were written in {sources}.\n");
        }

        if (attempts.Count > 0)
        {
            builder.Append("\nAttempts:\n\n| Step | Attempt | Outcome | Prompt tokens | Output tokens | Prompt ms | Output ms |\n| --- | --- | --- | --- | --- | --- | --- |\n");
            foreach (var attempt in attempts)
            {
                var outcome = attempt.Failure is { Length: > 0 } failure ? $"{attempt.Outcome}: {failure}"
                    : attempt.ReusedFromTaskId is { } from ? $"{attempt.Outcome}, reused from task {from}"
                    : attempt.Outcome;
                builder.Append(CultureInfo.InvariantCulture,
                    $"| {attempt.StepKey} | {attempt.AttemptNumber} | {Cell(outcome)} | {Count(attempt.PromptTokens)} | {Count(attempt.CompletionTokens)} | " +
                    $"{Milliseconds(attempt.PromptMilliseconds)} | {Milliseconds(attempt.CompletionMilliseconds)} |\n");
            }
        }
    }

    private static string BasisText(string basis) => basis switch
    {
        "hashed" => "computed from the file",
        "stated" => "as the profile states it, not checked",
        _ => "unknown"
    };

    internal static string Seconds(long nanoseconds) => (nanoseconds / 1e9).ToString("0.###", CultureInfo.InvariantCulture) + " s";

    private static string Count(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

    private static string Milliseconds(double? value) => value?.ToString("0", CultureInfo.InvariantCulture) ?? "unknown";

    /// <summary>A time as hours, minutes, and seconds.</summary>
    public static string Clock(long nanoseconds) => SummaryCollator.Time(nanoseconds)[..8];

    internal static string TileRange(int first, int last) =>
        first == last ? string.Create(CultureInfo.InvariantCulture, $"tile {first}") : string.Create(CultureInfo.InvariantCulture, $"tiles {first} to {last}");

    internal static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    internal static string Cell(string text) => OneLine(text).Replace("|", "\\|", StringComparison.Ordinal);
}

/// <summary>
/// The references a report cites, for its evidence list: each reference
/// written after a statement is noted, except the numbers of a visit's
/// knowledge, which are not in the evidence list.
/// </summary>
internal sealed class ReportCitations(RecordingCollation collation)
{
    public SortedSet<int> Cited { get; } = [];

    public string Refs(IEnumerable<string> references)
    {
        var list = references.Where(reference => !Knowledge.IsStatementReference(reference)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var reference in list)
        {
            if (reference.Length > 1 && int.TryParse(reference.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                Cited.Add(number);
            }
        }

        return list.Count == 0 ? "" : " " + string.Join(' ', list.Select(reference => $"[{reference}]"));
    }

    public string? RefOf(string? eventId) =>
        eventId is null ? null : collation.Evidence.Entries.FirstOrDefault(entry => entry.EventId == eventId)?.Reference;
}
