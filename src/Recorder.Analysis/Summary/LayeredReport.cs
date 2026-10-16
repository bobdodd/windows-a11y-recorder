using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Recorder.Analysis.Summary;

/// <summary>
/// Writes the layered report, summary-with-vision.md, from the recorder's
/// collation and the accepted, checked model output, never from the
/// model's text as written: chapter 1, the executive summary; chapter 2,
/// each page from every visit to it; chapter 3, the story of the user
/// experience, its headings numbered and its pictures put in by the
/// recorder from the forms the model gave; and the appendices, the
/// settings at the start and every change, written by the recorder. Each
/// statement ends with the references it cites, and the evidence list
/// gives each cited reference's time and what it is. See
/// docs/architecture/recording-summary.md, "The layered report" and "The
/// executive summary and the settings in use".
/// </summary>
public static class LayeredReport
{
    /// <summary>
    /// The form the model gave a statement, read from the step's answer as
    /// stored, as the claims are stored without it; a sentence where the
    /// answer gives none.
    /// </summary>
    public static string FormOf(AcceptedStep step, Claim claim)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(claim);
        var output = step.Output;
        if (output.ValueKind == JsonValueKind.Object &&
            output.TryGetProperty(claim.Section, out var section) && section.ValueKind == JsonValueKind.Array && claim.Paragraph < section.GetArrayLength() &&
            section[claim.Paragraph] is { ValueKind: JsonValueKind.Array } paragraph && claim.Ordinal < paragraph.GetArrayLength() &&
            paragraph[claim.Ordinal] is { ValueKind: JsonValueKind.Object } statement &&
            statement.TryGetProperty("form", out var form) && form.ValueKind == JsonValueKind.String &&
            RecordingSummaryVisionKind.StoryForms.Contains(form.GetString()!, StringComparer.Ordinal))
        {
            return form.GetString()!;
        }

        return RecordingSummaryVisionKind.Sentence;
    }

    /// <summary>A page's heading in chapter 1: its title as last recorded, or its address.</summary>
    public static string PageTitle(PageChapter page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.Title is { Length: > 0 } title ? SummaryReport.OneLine(title) : page.Url;
    }

    /// <summary>Whether a setting is at its default, as the appendices and the executive summary's request say it.</summary>
    public static string DefaultText(SettingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Default switch
        {
            DefaultState.Default => "the default",
            DefaultState.NotDefault => row.DefaultValue is { } value ? $"not the default, which is {value}" : "not the default",
            DefaultState.NotASetting => "not a setting: a value read",
            _ => "default unknown"
        };
    }

    /// <summary>A change as the story's requests and Appendix D give it.</summary>
    public static string ChangeText(SettingChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var when = change.End is { } end
            ? $"{SummaryCollator.Time(change.Time)} to {SummaryCollator.Time(end)}"
            : SummaryCollator.Time(change.Time);
        var what = change.End is not null
            ? $"{change.Group}, {change.Setting}: the view moved from {change.Before} to {change.After}"
            : $"{change.Group}, {change.Setting}: changed from {change.Before} to {change.After}";
        return $"{when} {what}" + (change.Detail is { } detail ? $", {detail}" : "") + ".";
    }

    /// <summary>A picture's path from the report, in the task's folder under analysis, with spaces escaped.</summary>
    public static string PicturePath(string file) => "../../" + (file ?? "").Replace(" ", "%20", StringComparison.Ordinal);

    public static string Write(
        string sessionKey,
        RecordingCollation collation,
        ParticipantCapabilities capabilities,
        IReadOnlyList<AttemptRecord> attempts,
        CollationLimits limits,
        DateTime nowUtc,
        VisionReportParts parts)
    {
        ArgumentNullException.ThrowIfNull(collation);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(parts);
        var citations = new ReportCitations(collation);
        var builder = new StringBuilder();
        string Refs(IEnumerable<string> references) => citations.Refs(references);

        // A section's statements, paragraph by paragraph.
        void Claims(AcceptedStep step, string section)
        {
            var claims = step.Claims.Where(claim => claim.Section == section).ToList();
            if (claims.Count == 0)
            {
                builder.Append("The model gave nothing for this part.\n\n");
                return;
            }

            foreach (var paragraph in claims.GroupBy(claim => claim.Paragraph).OrderBy(group => group.Key))
            {
                builder.Append(string.Join(' ', paragraph.OrderBy(claim => claim.Ordinal)
                    .Select(claim => SummaryReport.OneLine(claim.Text) + Refs(claim.Evidence))));
                builder.Append("\n\n");
            }
        }

        // Statements in their forms, in chapter 1 and chapter 4: each heading
        // numbered by the recorder, a heading with no heading above it
        // raised a level; the sentences of a paragraph between headings,
        // lists, and pictures one paragraph; consecutive items of a list one
        // list, a numbered list numbered from 1; each picture put in,
        // numbered, with its caption.
        var counters = new List<int>();
        var figures = 0;
        void Story(AcceptedStep step, string section)
        {
            var claims = step.Claims.Where(claim => claim.Section == section).ToList();
            if (claims.Count == 0)
            {
                builder.Append("The model gave nothing for this part.\n\n");
                return;
            }

            var sentences = new List<string>();
            string? list = null;
            var items = 0;
            void EndList()
            {
                if (list is not null)
                {
                    builder.Append('\n');
                    list = null;
                    items = 0;
                }
            }

            void Flush()
            {
                if (sentences.Count > 0)
                {
                    builder.Append(string.Join(' ', sentences)).Append("\n\n");
                    sentences.Clear();
                }

                EndList();
            }

            foreach (var paragraph in claims.GroupBy(claim => claim.Paragraph).OrderBy(group => group.Key))
            {
                foreach (var claim in paragraph.OrderBy(claim => claim.Ordinal))
                {
                    var form = FormOf(step, claim);
                    var text = SummaryReport.OneLine(claim.Text);
                    if (form.StartsWith("heading-", StringComparison.Ordinal))
                    {
                        Flush();
                        var level = Math.Min(int.Parse(form.AsSpan("heading-".Length), NumberStyles.None, CultureInfo.InvariantCulture), counters.Count + 1);
                        while (counters.Count > level)
                        {
                            counters.RemoveAt(counters.Count - 1);
                        }

                        if (counters.Count < level)
                        {
                            counters.Add(0);
                        }

                        counters[level - 1]++;
                        builder.Append('#', level + 2).Append(" 4.").Append(string.Join('.', counters)).Append(' ').Append(text).Append("\n\n");
                    }
                    else if (form is RecordingSummaryVisionKind.Bullet or RecordingSummaryVisionKind.Numbered)
                    {
                        if (sentences.Count > 0 || list != form)
                        {
                            Flush();
                        }

                        list = form;
                        items++;
                        builder.Append(form == RecordingSummaryVisionKind.Bullet ? "- " : items.ToString(CultureInfo.InvariantCulture) + ". ")
                            .Append(text).Append(Refs(claim.Evidence)).Append('\n');
                    }
                    else if (form == RecordingSummaryVisionKind.Picture &&
                        claim.Evidence.Select(reference => parts.Pictures.GetValueOrDefault(reference)).FirstOrDefault(picture => picture is not null) is { } picture)
                    {
                        Flush();
                        figures++;
                        var caption = string.Create(CultureInfo.InvariantCulture, $"Figure {figures}. {text}");
                        var alt = caption.Replace('[', '(').Replace(']', ')');
                        builder.Append("![").Append(alt).Append("](").Append(PicturePath(picture.File)).Append(")\n\n");
                        builder.Append(caption).Append(Refs(claim.Evidence)).Append("\n\n");
                    }
                    else if (form == RecordingSummaryVisionKind.Picture)
                    {
                        sentences.Add(text + " (The model gave this as a picture's caption, but it cites no captured frame or tile, so no picture is shown.)" + Refs(claim.Evidence));
                    }
                    else
                    {
                        EndList();
                        sentences.Add(text + Refs(claim.Evidence));
                    }
                }

                Flush();
            }
        }

        var accepted = SummaryReport.Accepted(attempts);
        builder.Append("# Report of recording ").Append(sessionKey).Append("\n\n");
        var date = nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (accepted is not null)
        {
            SummaryReport.AppendInferredBy(builder, accepted, date);
            builder.Append("How the report was made, and what its statements rest on, is in Appendix G.\n\n");
        }

        builder.Append("## 1. Executive summary\n\n");
        if (parts.Executive is { } executive)
        {
            Story(executive, "summary");
        }
        else
        {
            builder.Append("The model gave nothing for this part.\n\n");
        }

        SummaryReport.AppendCapabilities(builder, collation, capabilities, citations, "## 2. The participant");

        builder.Append("## 3. The pages\n\n");
        foreach (var page in parts.Pages)
        {
            builder.Append(CultureInfo.InvariantCulture, $"### 3.{page.Number} {PageTitle(page)}\n\n");
            AppendVisits(builder, page, collation, citations);
            Claims(page.Step, "page");
            builder.Append(CultureInfo.InvariantCulture, $"#### 3.{page.Number}.1 How the page changed\n\n");
            Claims(page.Step, "changes");
        }

        builder.Append("## 4. The user experience\n\n");
        Story(parts.Opening, "opening");
        foreach (var visit in collation.Visits)
        {
            if (parts.Visits.TryGetValue(visit.Visit.Number, out var part))
            {
                Story(part.Story, "story");
            }
        }

        AppendAppendices(builder, parts, citations);
        SummaryReport.AppendOtherWindows(builder, collation, citations, "## Appendix E. Other windows");
        SummaryReport.AppendEvidence(builder, collation, citations, "## Appendix F. Evidence");

        builder.Append("## Appendix G. How this was made\n\n");
        builder.Append("### G.1 How the report is built\n\n");
        builder.Append("- Every statement in chapters 1 and 4 and in the pages of chapter 3 is a model's inference from the recorded evidence cited after it.\n");
        builder.Append("- Chapter 2 quotes the participant's capabilities as the auditor gave them, and the assistive technology the recorder observed running.\n");
        builder.Append("- The page visits, the pages, and Appendices A to F are derived by the recorder from the recording, not by a model.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- The report is built in layers. Each visit's knowledge starts as layer 1, its description from the recorded data, in the summary of task {parts.SummaryTaskId:D}.\n");
        builder.Append("- At each moment of the visit the recorder chose, a model adds layer 2, what the captured frame of the screen shows.\n");
        builder.Append("- It then adds layer 3, what the web page adds that the frame does not show, from the page as the recorder rebuilt it at that frame and pictured whole in tiles.\n");
        builder.Append("- A captured frame shows what was on the screen, not what the participant saw.\n");
        builder.Append("- A recreated page is the page as the recorder rebuilt it from the recorded evidence, not the page itself: what the recording lacks, such as an image, it lacks too.\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Each moment's record, its frame and its tiles with the steps that describe them, is in {parts.MomentIndexFile}.\n\n");

        builder.Append("### G.2 The model\n\n");
        if (accepted is null)
        {
            builder.Append("No step was accepted, so no model is named.\n");
        }

        SummaryReport.AppendModel(builder, accepted);
        builder.Append('\n');

        builder.Append("### G.3 The prompt templates\n\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- {RecordingSummaryVisionKind.MomentTemplate} version {RecordingSummaryVisionKind.MomentTemplateVersion}, for each moment's captured frame.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- {RecordingSummaryVisionKind.PageTemplate} version {RecordingSummaryVisionKind.PageTemplateVersion}, for each group of its page's tiles.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- {RecordingSummaryVisionKind.Layer2Template} and {RecordingSummaryVisionKind.Layer3Template} version {RecordingSummaryVisionKind.LayerTemplateVersion}, for each moment's layers.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- {RecordingSummaryVisionKind.ExecutiveTemplate} version {RecordingSummaryVisionKind.ExecutiveTemplateVersion}, for chapter 1, the executive summary.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- {RecordingSummaryVisionKind.PageChapterTemplate} version {RecordingSummaryVisionKind.PageChapterTemplateVersion}, for each page of chapter 3.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- {RecordingSummaryVisionKind.OpeningTemplate} version {RecordingSummaryVisionKind.StoryTemplateVersion}, for the opening of chapter 4.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- {RecordingSummaryVisionKind.StoryTemplate} version {RecordingSummaryVisionKind.StoryTemplateVersion}, for each visit's part of chapter 4.\n\n");

        var gap = SummaryReport.Seconds(SettingsReader.MagnifierGapNanoseconds);
        builder.Append("### G.4 The settings\n\n");
        builder.Append("- The settings are read by the recorder from the records the player's properties panel shows, and the Magnifier's from each captured frame's reading.\n");
        builder.Append(CultureInfo.InvariantCulture, $"- A period of panning is the Magnifier's view moving at one level, each move less than {gap} after the one before.\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Changes of the Magnifier's level less than {gap} apart are one change.\n");
        builder.Append("- A Windows setting's default is known only where Microsoft documents it, which it does for the caret width alone.\n");
        builder.Append("- A browser setting's default is Chromium's own answer, recorded with the setting.\n");
        builder.Append("- An NVDA setting's default is from NVDA 2026.2's configuration specification; a setting not written in nvda.ini is at its default.\n\n");

        builder.Append("### G.5 The moments and their pictures\n\n");
        var moments = parts.Limits;
        builder.Append(CultureInfo.InvariantCulture,
            $"- Moments of a visit: {(moments.PerVisit is { } most ? $"at most {most} for each visit" : "no limit on their number")}; moments less than {SummaryReport.Seconds(moments.MergeNanoseconds)} apart are merged.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- A moment is settled when the layout has not changed for {SummaryReport.Seconds(moments.SettleQuietNanoseconds)}, or {SummaryReport.Seconds(moments.SettleFallbackNanoseconds)} after the visit starts.\n");
        builder.Append(CultureInfo.InvariantCulture, $"- A large change is {moments.LargeChangeNodes} elements or more at once.\n");
        var leftOut = parts.Visits.OrderBy(pair => pair.Key).Where(pair => pair.Value.MomentsLeftOut > 0).ToList();
        if (leftOut.Count > 0)
        {
            builder.Append("- Moments left out to keep within the limit: ")
                .Append(string.Join("; ", leftOut.Select(pair => string.Create(CultureInfo.InvariantCulture, $"visit {pair.Key}, {pair.Value.MomentsLeftOut}"))))
                .Append(".\n");
        }

        builder.Append("- Each captured frame is sent to the model whole, as recorded.\n");
        builder.Append("- Each moment's page is rebuilt at its frame in the instrumented Chromium without a window, and pictured whole in tiles the height of its viewport, each sent at full size.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- Each request holds as many tiles as fit {parts.ImageTokensPerRequest} image tokens, counted as one token for each 32 by 32 pixels, and at least one.\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"- Each tile's picture is kept once for the recording, in {parts.ImagesFolder}, named by the SHA-256 of its size, its pixels, and its colour profile.\n");

        // The groups of tiles not sent, each with the answer it uses
        // (recording-summary.md, "Unique images across the recording",
        // decision 9), and the layer 3 steps not sent.
        var notSent = parts.Visits.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value.Moments.SelectMany(moment =>
            (moment.NotSent ?? []).Select(group => (Visit: pair.Key, Moment: moment.Moment.Number, Group: group)))).ToList();
        var layersNotSent = parts.LayersNotSent ?? [];
        builder.Append(layersNotSent.Count == 0
            ? "- Every moment whose page was recreated had a layer 3 step.\n"
            : "- Layer 3 steps not sent, as every group of the moment's tiles was already in the visit's knowledge: " + string.Join(", ", layersNotSent) + ".\n");
        if (notSent.Count == 0)
        {
            builder.Append("- No group of tiles was the same pictures at the same places as a group already described, so every group was sent.\n\n");
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"\n{notSent.Count} {(notSent.Count == 1 ? "group of tiles was" : "groups of tiles were")} not sent, as their tiles were the same pictures, pixel for pixel, at the same places as a group already described, whose answer each uses unchanged:\n\n");
            foreach (var item in notSent)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"- Visit {item.Visit} moment {item.Moment} group {item.Group.Group} ({SummaryReport.TileRange(item.Group.FirstTile, item.Group.LastTile)}): the answer of group {item.Group.FromGroup} at moment {item.Group.FromMoment} of visit {item.Group.FromVisit}, at {SummaryReport.Clock(item.Group.FromTime)}, step {item.Group.FromStep}.\n");
            }

            builder.Append('\n');
        }

        builder.Append("### G.6 The limits, the steps reused, and the attempts\n\n");
        SummaryReport.AppendLimitsAndAttempts(builder, collation, limits, attempts);
        return builder.ToString();
    }

    // The visits to a page, derived by the recorder, as a list: each
    // visit's time, kind, and address, why it is a visit of its own, and the
    // visit it returns to; then each visit's in-page jumps, a list each.
    private static void AppendVisits(StringBuilder builder, PageChapter page, RecordingCollation collation, ReportCitations citations)
    {
        builder.Append(page.Visits.Count == 1 ? "The visit to the page, derived by the recorder:\n\n" : "The visits to the page, derived by the recorder:\n\n");
        var visits = page.Visits.Select(number => collation.Visits.First(item => item.Visit.Number == number)).ToList();
        foreach (var visit in visits)
        {
            var start = visit.Visit.Start;
            builder.Append(CultureInfo.InvariantCulture,
                $"- Visit {visit.Visit.Number}, {SummaryReport.Clock(start.Time)} to {SummaryReport.Clock(visit.Visit.End)}: a visit to a {visit.Visit.Label}, {start.Url}.");
            builder.Append(citations.RefOf(start.EventId) is { } startReference ? citations.Refs([startReference]) : "");
            if (visit.Visit.Kind == VisitKind.FragmentRoute && visit.Visit.RouteReason is { } reason)
            {
                builder.Append(CultureInfo.InvariantCulture, $" It is a visit of its own because {reason}.");
            }

            if (visit.Visit.ReturnOf is { } earlier)
            {
                builder.Append(CultureInfo.InvariantCulture, $" A return to the URL of visit {earlier}, judged by URL alone.");
            }

            builder.Append('\n');
        }

        builder.Append('\n');
        foreach (var visit in visits.Where(item => item.Visit.Steps.Count > 0))
        {
            builder.Append(CultureInfo.InvariantCulture, $"In-page jumps within visit {visit.Visit.Number}, derived by the recorder:\n\n");
            foreach (var step in visit.Visit.Steps)
            {
                var link = step.LinkName is { Length: > 0 } name ? $" \"{SummaryReport.OneLine(name)}\"" : "";
                builder.Append(CultureInfo.InvariantCulture,
                    $"- {SummaryReport.Clock(step.Commit.Time)}, #{step.Fragment}: a {step.Name}{link} to {step.Target} ({step.Rule}).");
                builder.Append(citations.RefOf(step.Commit.EventId) is { } stepReference ? citations.Refs([stepReference]) : "").Append('\n');
            }

            builder.Append('\n');
        }
    }

    // The appendices, written by the recorder: the settings at the start,
    // A to C, each a table of setting, value, whether it is the default, and
    // when it was read, citing its record; and D, every change.
    private static void AppendAppendices(StringBuilder builder, VisionReportParts parts, ReportCitations citations)
    {
        var settings = parts.Settings;
        string Cite(SettingSource source) =>
            parts.SettingReferences.TryGetValue(source.Key, out var reference) ? citations.Refs([reference]).Trim() : "not in the evidence list";

        void Table(string heading, string about, IReadOnlyList<SettingRow> rows, string? note)
        {
            builder.Append(heading).Append("\n\n").Append(about).Append("\n\n");
            if (note is not null)
            {
                builder.Append(note).Append("\n\n");
            }

            if (rows.Count == 0)
            {
                return;
            }

            builder.Append("| Group | Setting | Value | Default | Read at | Evidence |\n| --- | --- | --- | --- | --- | --- |\n");
            foreach (var row in rows)
            {
                builder.Append(
                    $"| {SummaryReport.Cell(row.Group)} | {SummaryReport.Cell(row.Setting)} | {SummaryReport.Cell(row.Value)} | {SummaryReport.Cell(DefaultText(row))} | " +
                    $"{(row.Source.Time is { } time ? SummaryCollator.Time(time) : "unknown")} | {Cite(row.Source)} |\n");
            }

            builder.Append('\n');
        }

        Table("## Appendix A. Windows settings at the start",
            "The Windows accessibility and display settings as the recorder read them at the start of the recording. Microsoft documents the default of the caret width alone, so the other defaults are unknown.",
            settings.Windows, settings.WindowsNote);
        Table("## Appendix B. Browser settings at the start",
            "The browser's settings as read when its profile was loaded, each with Chromium's own answer to whether it is the default, and the values the browser sent to the first page visited, which follow from the settings and are not settings themselves.",
            settings.Browser, settings.BrowserNote);
        Table("## Appendix C. Assistive technology at the start",
            "The assistive technology at the start of the recording, as the recorder read it:\n\n" +
            "- The products the recorder watched for, and whether each was running.\n" +
            "- NVDA's settings, as first read from its configuration folder. A setting not written in nvda.ini is at NVDA's default.\n" +
            "- The Magnifier, as read with the first captured frame.",
            settings.Assistive, settings.AssistiveNote);

        builder.Append("## Appendix D. Changes during the recording\n\n");
        builder.Append("Every change the recorder found to the settings, the assistive technology, and the Magnifier's view, in order. The story tells each where it happened; this list keeps them all.\n\n");
        if (settings.Changes.Count == 0)
        {
            builder.Append("No change was recorded.\n\n");
            return;
        }

        builder.Append("| Time | Group | Setting | Before | After | More | Evidence |\n| --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (var change in settings.Changes)
        {
            var when = change.End is { } end ? $"{SummaryCollator.Time(change.Time)} to {SummaryCollator.Time(end)}" : SummaryCollator.Time(change.Time);
            builder.Append(
                $"| {when} | {SummaryReport.Cell(change.Group)} | {SummaryReport.Cell(change.Setting)} | {SummaryReport.Cell(change.Before)} | {SummaryReport.Cell(change.After)} | " +
                $"{SummaryReport.Cell(change.Detail ?? "")} | {string.Join(' ', change.Sources.Select(Cite).Distinct(StringComparer.Ordinal))} |\n");
        }

        builder.Append('\n');
    }
}
