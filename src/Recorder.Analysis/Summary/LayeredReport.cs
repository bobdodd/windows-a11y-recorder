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

        // Chapter 2's statements in their forms: each heading numbered by
        // the recorder, a heading with no heading above it raised a level;
        // the sentences of a paragraph between headings and pictures one
        // paragraph; each picture put in, numbered, with its caption.
        var counters = new List<int>();
        var figures = 0;
        void Story(AcceptedStep step, string section)
        {
            var claims = step.Claims.Where(claim => claim.Section == section).ToList();
            var sentences = new List<string>();
            void Flush()
            {
                if (sentences.Count > 0)
                {
                    builder.Append(string.Join(' ', sentences)).Append("\n\n");
                    sentences.Clear();
                }
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
                        builder.Append('#', level + 2).Append(" 3.").Append(string.Join('.', counters)).Append(' ').Append(text).Append("\n\n");
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
        }

        builder.Append("Every statement in the three chapters is a model's inference from the recorded evidence cited after it. Page visits and pages are derived by the recorder, and the appendices are written by the recorder from the recorded settings, not by a model.\n\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"The report is built in layers. Each visit's knowledge starts as layer 1, its description from the recorded data, in the summary of task {parts.SummaryTaskId:D}. ");
        builder.Append("At each moment of the visit the recorder chose, a model adds layer 2, what the captured frame of the screen shows, ");
        builder.Append("and layer 3, what the web page adds that the frame does not show, from the page as the recorder rebuilt it at that frame and pictured whole in tiles. ");
        builder.Append("Chapter 1 is the executive summary, chapter 2 describes each page from every visit to it, and chapter 3 tells the story of the user experience. ");
        builder.Append("Appendices A to C give the accessibility and assistive technology settings at the start, and Appendix D every change to them during the recording. ");
        builder.Append("A captured frame shows what was on the screen, not what the participant saw. ");
        builder.Append("A recreated page is the page as the recorder rebuilt it from the recorded evidence, not the page itself: what the recording lacks, such as an image, it lacks too. ");
        builder.Append(CultureInfo.InvariantCulture, $"Each moment's record, its frame and its tiles with the steps that describe them, is in {parts.MomentIndexFile}.\n\n");

        SummaryReport.AppendCapabilities(builder, collation, capabilities, citations);

        builder.Append("## 1. Executive summary\n\n");
        if (parts.Executive is { } executive)
        {
            Claims(executive, "summary");
        }
        else
        {
            builder.Append("The model gave nothing for this part.\n\n");
        }

        builder.Append("## 2. The pages\n\n");
        foreach (var page in parts.Pages)
        {
            builder.Append(CultureInfo.InvariantCulture, $"### 2.{page.Number} {PageTitle(page)}\n\n");
            foreach (var number in page.Visits)
            {
                var visit = collation.Visits.First(item => item.Visit.Number == number);
                builder.Append(CultureInfo.InvariantCulture,
                    $"Visit {number}, {SummaryReport.Clock(visit.Visit.Start.Time)} to {SummaryReport.Clock(visit.Visit.End)}. ");
                SummaryReport.AppendDerived(builder, visit, citations);
                builder.Append('\n');
            }

            Claims(page.Step, "page");
            builder.Append(CultureInfo.InvariantCulture, $"#### 2.{page.Number}.1 How the page changed\n\n");
            Claims(page.Step, "changes");
        }

        builder.Append("## 3. The user experience\n\n");
        Story(parts.Opening, "opening");
        foreach (var visit in collation.Visits)
        {
            if (parts.Visits.TryGetValue(visit.Visit.Number, out var part))
            {
                Story(part.Story, "story");
            }
        }

        AppendAppendices(builder, parts, citations);
        SummaryReport.AppendOtherWindows(builder, collation, citations);
        SummaryReport.AppendEvidence(builder, collation, citations);
        builder.Append("## How this was made\n\n");
        SummaryReport.AppendModel(builder, accepted);
        builder.Append(CultureInfo.InvariantCulture,
            $"- Prompt templates: {RecordingSummaryVisionKind.MomentTemplate} version {RecordingSummaryVisionKind.MomentTemplateVersion} for each moment's captured frame, " +
            $"{RecordingSummaryVisionKind.PageTemplate} version {RecordingSummaryVisionKind.PageTemplateVersion} for each group of its page's tiles, " +
            $"{RecordingSummaryVisionKind.Layer2Template} and {RecordingSummaryVisionKind.Layer3Template} version {RecordingSummaryVisionKind.LayerTemplateVersion} for each moment's layers, " +
            $"{RecordingSummaryVisionKind.PageChapterTemplate} version {RecordingSummaryVisionKind.PageChapterTemplateVersion} for each page of chapter 2, " +
            $"{RecordingSummaryVisionKind.StoryTemplate} version {RecordingSummaryVisionKind.StoryTemplateVersion} for each visit's part of chapter 3, " +
            $"{RecordingSummaryVisionKind.OpeningTemplate} version {RecordingSummaryVisionKind.StoryTemplateVersion} for its opening, " +
            $"and {RecordingSummaryVisionKind.ExecutiveTemplate} version {RecordingSummaryVisionKind.ExecutiveTemplateVersion} for chapter 1, the executive summary.\n");
        builder.Append(
            $"- The settings: read by the recorder from the records the player's properties panel shows, and the Magnifier's from each captured frame's reading. " +
            $"A period of panning is the Magnifier's view moving at one level, each move less than {SummaryReport.Seconds(SettingsReader.MagnifierGapNanoseconds)} after the one before; " +
            $"changes of its level less than {SummaryReport.Seconds(SettingsReader.MagnifierGapNanoseconds)} apart are one change. " +
            "A Windows setting's default is known only where Microsoft documents it, the caret width's; a browser setting's is Chromium's own answer, recorded with it; " +
            "an NVDA setting's is NVDA 2026.2's configuration specification, a setting not written in nvda.ini being at its default.\n");
        var moments = parts.Limits;
        builder.Append(CultureInfo.InvariantCulture,
            $"- Moment limits: {(moments.PerVisit is { } most ? $"{most} moments for each visit" : "no limit on the moments of a visit")}; moments less than {SummaryReport.Seconds(moments.MergeNanoseconds)} apart merged; " +
            $"settled when the layout has not changed for {SummaryReport.Seconds(moments.SettleQuietNanoseconds)}, or {SummaryReport.Seconds(moments.SettleFallbackNanoseconds)} after the visit starts; " +
            $"a large change is {moments.LargeChangeNodes} elements or more at once.\n");
        var leftOut = parts.Visits.OrderBy(pair => pair.Key).Where(pair => pair.Value.MomentsLeftOut > 0).ToList();
        if (leftOut.Count > 0)
        {
            builder.Append("- Moments left out to keep within the limit: ")
                .Append(string.Join("; ", leftOut.Select(pair => string.Create(CultureInfo.InvariantCulture, $"visit {pair.Key}, {pair.Value.MomentsLeftOut}"))))
                .Append(".\n");
        }

        builder.Append("- Each captured frame is sent to the model whole, as recorded. ");
        builder.Append("Each moment's page is rebuilt at its frame in the instrumented Chromium without a window, and pictured whole in tiles the height of its viewport, each sent at full size; ");
        builder.Append(CultureInfo.InvariantCulture,
            $"each request holds as many tiles as fit {parts.ImageTokensPerRequest} image tokens, counted as one token for each 32 by 32 pixels, and at least one.\n");

        // The groups of tiles not sent, each with the answer it uses
        // (recording-summary.md, "Unique images across the recording",
        // decision 9), and the layer 3 steps not sent.
        var notSent = parts.Visits.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value.Moments.SelectMany(moment =>
            (moment.NotSent ?? []).Select(group => (Visit: pair.Key, Moment: moment.Moment.Number, Group: group)))).ToList();
        builder.Append(CultureInfo.InvariantCulture,
            $"- Each tile's picture is kept once for the recording, in {parts.ImagesFolder}, named by the SHA-256 of its size, its pixels, and its colour profile. ");
        builder.Append(notSent.Count == 0
            ? "No group of tiles was the same pictures at the same places as a group already described, so every group was sent.\n"
            : string.Create(CultureInfo.InvariantCulture,
                  $"{notSent.Count} {(notSent.Count == 1 ? "group of tiles was" : "groups of tiles were")} not sent, as their tiles were the same pictures, pixel for pixel, at the same places as a group already described, whose answer each uses unchanged: ") +
              string.Join("; ", notSent.Select(item => string.Create(CultureInfo.InvariantCulture,
                  $"visit {item.Visit} moment {item.Moment} group {item.Group.Group} ({SummaryReport.TileRange(item.Group.FirstTile, item.Group.LastTile)}), the answer of group {item.Group.FromGroup} at moment {item.Group.FromMoment} of visit {item.Group.FromVisit}, at {SummaryReport.Clock(item.Group.FromTime)}, step {item.Group.FromStep}"))) +
              ".\n");
        var layersNotSent = parts.LayersNotSent ?? [];
        builder.Append(layersNotSent.Count == 0
            ? "- Every moment whose page was recreated had a layer 3 step.\n"
            : "- Layer 3 steps not sent, as every group of the moment's tiles was already in the visit's knowledge: " + string.Join(", ", layersNotSent) + ".\n");
        SummaryReport.AppendLimitsAndAttempts(builder, collation, limits, attempts);
        return builder.ToString();
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
            "The assistive technology the recorder watched for, as at the start of the recording; NVDA's settings as first read from its configuration folder, a setting not written in nvda.ini being at NVDA's default; and the Magnifier as read with the first captured frame.",
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
