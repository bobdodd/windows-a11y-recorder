using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Recorder.Analysis;

/// <summary>What a block of a report is.</summary>
public enum ReportBlockKind
{
    Heading,
    Paragraph,
    ListItem,
    Quote,
    Table,
    Image
}

/// <summary>
/// A block of a report as the reading view shows it: its text, with the
/// references it cites in the order cited; for a heading, its level; for a
/// table, its rows of cells, the first row its header; for a picture, its
/// text the picture's description and its source the picture's path as
/// written, relative to the report's folder, with escapes undone.
/// </summary>
public sealed record ReportBlock(
    ReportBlockKind Kind,
    int Level,
    string Text,
    IReadOnlyList<string> References,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? Source = null);

/// <summary>An entry of a report's evidence list: its time in the recording, or null when it is not of the recording.</summary>
public sealed record ReportEvidence(string Reference, long? Nanoseconds, string Time, string Text);

/// <summary>
/// A Markdown report read back for the reading view: the Markdown the
/// recorder's reports are written in, which is headings, paragraphs,
/// list items, quotes, tables, and pictures, each on a line of its own, and the evidence list that gives each
/// reference's time. See docs/architecture/analysis-runner.md, "Reports are
/// Markdown files", and "Decisions on the player (2026-10-13)".
/// </summary>
public sealed partial class ReportDocument
{
    private ReportDocument(IReadOnlyList<ReportBlock> blocks, IReadOnlyDictionary<string, ReportEvidence> evidence)
    {
        Blocks = blocks;
        Evidence = evidence;
    }

    public IReadOnlyList<ReportBlock> Blocks { get; }

    public IReadOnlyDictionary<string, ReportEvidence> Evidence { get; }

    /// <summary>The title: the first level 1 heading, or null.</summary>
    public string? Title => Blocks.FirstOrDefault(block => block.Kind == ReportBlockKind.Heading && block.Level == 1)?.Text;

    [GeneratedRegex(@"\[(E\d+)\]")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"^(\d{2}):(\d{2}):(\d{2})\.(\d{3})$")]
    private static partial Regex TimePattern();

    // A picture on a line of its own: ![description](path).
    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^)\s]+)\)$")]
    private static partial Regex ImagePattern();

    /// <summary>The references a text cites, each once, in the order first cited.</summary>
    public static IReadOnlyList<string> ReferencesIn(string text) =>
        [.. ReferencePattern().Matches(text ?? "").Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    /// <summary>A time as the reports write it, hours to milliseconds, in nanoseconds; null when it is not one.</summary>
    public static long? ParseTime(string text)
    {
        var match = TimePattern().Match((text ?? "").Trim());
        if (!match.Success)
        {
            return null;
        }

        long Part(int group) => long.Parse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        return (((Part(1) * 60 + Part(2)) * 60 + Part(3)) * 1000 + Part(4)) * 1_000_000;
    }

    /// <summary>A reference's button label: the reference and its time to the second.</summary>
    public string Label(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Evidence.TryGetValue(reference, out var entry) && entry.Nanoseconds is not null && entry.Time.Length >= 8
            ? $"{reference} {entry.Time[..8]}"
            : reference;
    }

    public static ReportDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var blocks = new List<ReportBlock>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraph = new StringBuilder();
        var fence = false;

        void EndParagraph()
        {
            if (paragraph.Length > 0)
            {
                var text = paragraph.ToString().Trim();
                blocks.Add(new ReportBlock(ReportBlockKind.Paragraph, 0, text, ReferencesIn(text), []));
                paragraph.Clear();
            }
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                EndParagraph();
                fence = !fence;
                continue;
            }

            if (fence)
            {
                paragraph.Append(line).Append('\n');
                continue;
            }

            if (line.Length == 0)
            {
                EndParagraph();
                continue;
            }

            var level = line.TakeWhile(character => character == '#').Count();
            if (level is > 0 and <= 6 && line.Length > level && line[level] == ' ')
            {
                EndParagraph();
                var text = line[(level + 1)..].Trim();
                blocks.Add(new ReportBlock(ReportBlockKind.Heading, level, text, ReferencesIn(text), []));
                continue;
            }

            if (ImagePattern().Match(line) is { Success: true } image)
            {
                EndParagraph();
                blocks.Add(new ReportBlock(ReportBlockKind.Image, 0, image.Groups[1].Value, [], [], Uri.UnescapeDataString(image.Groups[2].Value)));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                EndParagraph();
                var text = line[2..].Trim();
                blocks.Add(new ReportBlock(ReportBlockKind.ListItem, 0, text, ReferencesIn(text), []));
                continue;
            }

            if (line.StartsWith('>'))
            {
                EndParagraph();
                var text = line[1..].Trim();
                blocks.Add(new ReportBlock(ReportBlockKind.Quote, 0, text, ReferencesIn(text), []));
                continue;
            }

            if (line.StartsWith('|'))
            {
                EndParagraph();
                var rows = new List<IReadOnlyList<string>>();
                for (; index < lines.Length && lines[index].TrimStart().StartsWith('|'); index++)
                {
                    var cells = Cells(lines[index].Trim());
                    if (!cells.All(cell => cell.Length > 0 && cell.All(character => character is '-' or ':' or ' ')))
                    {
                        rows.Add(cells);
                    }
                }

                index--;
                blocks.Add(new ReportBlock(ReportBlockKind.Table, 0, "", [], rows));
                continue;
            }

            paragraph.Append(paragraph.Length == 0 ? "" : " ").Append(line.Trim());
        }

        EndParagraph();
        return new ReportDocument(blocks, EvidenceOf(blocks));
    }

    // The table under the Evidence heading: Ref, Time, Evidence.
    private static Dictionary<string, ReportEvidence> EvidenceOf(List<ReportBlock> blocks)
    {
        var evidence = new Dictionary<string, ReportEvidence>(StringComparer.Ordinal);
        var heading = blocks.FindIndex(block => block.Kind == ReportBlockKind.Heading && block.Level == 2 && block.Text == "Evidence");
        if (heading < 0)
        {
            return evidence;
        }

        var table = blocks.Skip(heading + 1).TakeWhile(block => block.Kind != ReportBlockKind.Heading)
            .FirstOrDefault(block => block.Kind == ReportBlockKind.Table);
        foreach (var row in table?.Rows.Skip(1) ?? [])
        {
            if (row.Count >= 3 && row[0].StartsWith('E'))
            {
                evidence[row[0]] = new ReportEvidence(row[0], ParseTime(row[1]), row[1], row[2]);
            }
        }

        return evidence;
    }

    // A table row's cells, with escaped pipes kept as text.
    private static List<string> Cells(string row)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        // One leading and one trailing pipe bound the row.
        var body = row.StartsWith('|') ? row[1..] : row;
        if (body.EndsWith('|') && !body.EndsWith("\\|", StringComparison.Ordinal))
        {
            body = body[..^1];
        }

        for (var index = 0; index < body.Length; index++)
        {
            if (body[index] == '\\' && index + 1 < body.Length && body[index + 1] == '|')
            {
                cell.Append('|');
                index++;
            }
            else if (body[index] == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(body[index]);
            }
        }

        cells.Add(cell.ToString().Trim());
        return cells;
    }
}
