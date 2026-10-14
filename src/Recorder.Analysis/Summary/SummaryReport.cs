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
        ["session"] = "The session"
    };

    public static string Write(
        string sessionKey,
        RecordingCollation collation,
        ParticipantCapabilities capabilities,
        IReadOnlyList<(VisitCollation Visit, AcceptedStep Step)> visits,
        AcceptedStep? overall,
        IReadOnlyList<AttemptRecord> attempts,
        CollationLimits limits,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(collation);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(visits);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(limits);
        var cited = new SortedSet<int>();
        var builder = new StringBuilder();

        string Refs(IEnumerable<string> references)
        {
            var list = references.Distinct(StringComparer.Ordinal).ToList();
            foreach (var reference in list)
            {
                if (reference.Length > 1 && int.TryParse(reference.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    cited.Add(number);
                }
            }

            return list.Count == 0 ? "" : " " + string.Join(' ', list.Select(reference => $"[{reference}]"));
        }

        string? RefOf(string? eventId) =>
            eventId is null ? null : collation.Evidence.Entries.FirstOrDefault(entry => entry.EventId == eventId)?.Reference;

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

        var accepted = attempts.FirstOrDefault(attempt => attempt.Outcome == "accepted") ?? attempts.FirstOrDefault();
        builder.Append("# Summary of recording ").Append(sessionKey).Append("\n\n");
        var date = nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (accepted is not null)
        {
            var quantization = accepted.ModelQuantization is { Length: > 0 } value ? $" ({value})" : "";
            builder.Append(CultureInfo.InvariantCulture,
                $"Inferred by {accepted.Model}{quantization}, profile \"{accepted.ProfileName}\", {date}. ");
            builder.Append("Every statement below is a model's inference from the recorded evidence cited after it. Page visits are derived by the recorder.\n\n");
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture, $"Written by the recorder on {date}. No model was asked, because no page visits were recorded.\n\n");
        }

        // The participant's capabilities, as given to the model.
        builder.Append("## The participant's capabilities\n\n");
        if (capabilities.Description is { Length: > 0 } description)
        {
            var written = capabilities.WrittenUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "an unrecorded date";
            var reference = collation.Evidence.Entries.FirstOrDefault(entry => entry.EventId is null && entry.Text.StartsWith("The participant's capabilities", StringComparison.Ordinal));
            builder.Append(CultureInfo.InvariantCulture, $"Reported by the auditor on {written}, a person's statement and not an observation:\n\n");
            builder.Append("> ").Append(OneLine(description)).Append(reference is null ? "" : Refs([reference.Reference])).Append("\n\n");
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
            : observed.Text + ". This shows what was running, not what the participant is able to do." + Refs([observed.Reference])).Append("\n\n");

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
            builder.Append(CultureInfo.InvariantCulture, $"Derived by the recorder: a visit to a {visit.Visit.Label}, {start.Url}.");
            builder.Append(RefOf(start.EventId) is { } startReference ? Refs([startReference]) : "").Append('\n');
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
                    builder.Append(RefOf(step.Commit.EventId) is { } stepReference ? Refs([stepReference]) : "").Append('\n');
                }
            }

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

        builder.Append("## Other windows\n\n");
        if (collation.OtherWindows.Count == 0)
        {
            builder.Append("No other program's window was recorded in front.\n\n");
        }
        else
        {
            builder.Append("Other programs' windows the recorder found in front, derived from the front window records:\n\n");
            foreach (var window in collation.OtherWindows)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"- {Clock(window.Start)} to {Clock(window.End)}: {window.Process}, \"{OneLine(window.Title)}\".{Refs([window.Reference])}\n");
            }

            builder.Append('\n');
        }

        builder.Append("## Evidence\n\n");
        builder.Append("| Ref | Time | Evidence |\n| --- | --- | --- |\n");
        foreach (var number in cited)
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
        builder.Append("## How this was made\n\n");
        if (accepted is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $"- Model profile: \"{accepted.ProfileName}\", model {accepted.Model}.\n");
            builder.Append(CultureInfo.InvariantCulture, $"- Model file: {accepted.ModelFileName ?? "unknown"}, quantization {accepted.ModelQuantization ?? "unknown"}.\n");
            builder.Append(CultureInfo.InvariantCulture, $"- Model SHA-256: {accepted.ModelSha256 ?? "unknown"} ({BasisText(accepted.ModelSha256Basis)}).\n");
            builder.Append(CultureInfo.InvariantCulture, $"- Server build: {accepted.ServerBuild ?? "unknown"}.\n");
            builder.Append(CultureInfo.InvariantCulture, $"- Generation settings: `{accepted.Parameters}`.\n");
        }

        builder.Append(CultureInfo.InvariantCulture,
            $"- Prompt templates: {RecordingSummaryKind.VisitTemplate} version {RecordingSummaryKind.TemplateVersion} for each visit, " +
            $"{RecordingSummaryKind.RecordingTemplate} version {RecordingSummaryKind.TemplateVersion} for the whole recording.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- Collation limits: {limits.Landmarks} landmarks, {limits.Headings} headings, {limits.Fields} form fields, {limits.Buttons} buttons, " +
            $"{limits.MainTextCharacters} characters of opening text, {limits.KeyKinds} kinds of key, {limits.Commands} NVDA commands, " +
            $"{limits.Clicks} clicks, {limits.FocusChanges} focus changes, {limits.Announcements} NVDA announcements, {limits.TypedFields} text fields, " +
            $"{limits.Steps} in-page jumps and {limits.Popups} popups for each visit.\n");
        var leftOut = collation.Visits.Where(visit => visit.LeftOut > 0).ToList();
        builder.Append(leftOut.Count == 0
            ? "- Nothing was left out of any visit's facts.\n"
            : "- Facts left out to keep within the limits: " + string.Join("; ", leftOut.Select(visit =>
                string.Create(CultureInfo.InvariantCulture, $"visit {visit.Visit.Number}, {visit.LeftOut}"))) + ".\n");
        builder.Append("- What NVDA had to announce is the recorder's inference from the page, not a recording of speech.\n");
        if (attempts.Count > 0)
        {
            builder.Append("\nAttempts:\n\n| Step | Attempt | Outcome | Prompt tokens | Output tokens | Prompt ms | Output ms |\n| --- | --- | --- | --- | --- | --- | --- |\n");
            foreach (var attempt in attempts)
            {
                var outcome = attempt.Failure is { Length: > 0 } failure ? $"{attempt.Outcome}: {failure}" : attempt.Outcome;
                builder.Append(CultureInfo.InvariantCulture,
                    $"| {attempt.StepKey} | {attempt.AttemptNumber} | {Cell(outcome)} | {Count(attempt.PromptTokens)} | {Count(attempt.CompletionTokens)} | " +
                    $"{Milliseconds(attempt.PromptMilliseconds)} | {Milliseconds(attempt.CompletionMilliseconds)} |\n");
            }
        }

        return builder.ToString();
    }

    private static string BasisText(string basis) => basis switch
    {
        "hashed" => "computed from the file",
        "stated" => "as the profile states it, not checked",
        _ => "unknown"
    };

    private static string Count(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

    private static string Milliseconds(double? value) => value?.ToString("0", CultureInfo.InvariantCulture) ?? "unknown";

    /// <summary>A time as hours, minutes, and seconds.</summary>
    public static string Clock(long nanoseconds) => SummaryCollator.Time(nanoseconds)[..8];

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Cell(string text) => OneLine(text).Replace("|", "\\|", StringComparison.Ordinal);
}
