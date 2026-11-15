using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Recorder.Analysis;

/// <summary>
/// An entry of a report's evidence list as an MP4 export reads it: its
/// reference, the session key of its recording, or null for one about
/// every recording, its time in that recording, or null for one about the
/// whole recording, and what kind of evidence it is in a few words.
/// </summary>
public sealed record ReportEvidenceItem(
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("recording")] string? Recording,
    [property: JsonPropertyName("timeNanoseconds")] long? TimeNanoseconds,
    [property: JsonPropertyName("kind")] string Kind);

/// <summary>
/// The evidence a report lists, written next to the report as
/// evidence.json so that an MP4 export can show each reference's number
/// on the picture at its time and name a chapter after it (mp4-export.md,
/// "Evidence numbers"). It is not one of the task's reports: the player
/// does not open it, and it lists only what the report's evidence table
/// lists.
/// </summary>
public static partial class ReportEvidenceList
{
    public const string FileName = "evidence.json";

    public const string Format = "windows-a11y-recorder.report-evidence/1";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// The references a report's Markdown lists in its evidence table:
    /// the rows starting with a reference under the last heading ending
    /// in "Evidence", up to the next heading.
    /// </summary>
    public static IReadOnlySet<string> Listed(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = -1;
        for (var index = 0; index < lines.Length; index++)
        {
            if (EvidenceHeading().IsMatch(lines[index]))
            {
                start = index;
            }
        }

        var listed = new HashSet<string>(StringComparer.Ordinal);
        if (start < 0)
        {
            return listed;
        }

        for (var index = start + 1; index < lines.Length && !lines[index].StartsWith('#'); index++)
        {
            if (Row().Match(lines[index]) is { Success: true } row)
            {
                listed.Add(row.Groups[1].Value);
            }
        }

        return listed;
    }

    /// <summary>The file's bytes: the entries the report lists, in the order given.</summary>
    public static byte[] Serialize(IEnumerable<ReportEvidenceItem> entries) =>
        JsonSerializer.SerializeToUtf8Bytes(new Document(Format, [.. entries]), Options);

    /// <summary>Writes the file to a report's folder.</summary>
    public static async Task WriteAsync(string folder, IEnumerable<ReportEvidenceItem> entries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, FileName), Serialize(entries), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The entries of a report folder's evidence list, or null where there
    /// is none, as for a report written before the list was, or it cannot
    /// be read.
    /// </summary>
    public static IReadOnlyList<ReportEvidenceItem>? Read(string folder)
    {
        var path = Path.Combine(folder, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var document = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(path));
            return document is { Format: Format, Entries: { } entries } ? entries : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A recording's entries of a list, those of its session key, and how
    /// many of them, or of those about every recording, have no time, as
    /// for an app or the counts of findings, so cannot be shown at a time.
    /// </summary>
    public static (IReadOnlyList<ReportEvidenceItem> Timed, int Untimed) ForRecording(IReadOnlyList<ReportEvidenceItem> entries, string sessionKey)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var mine = entries.Where(entry => entry.Recording is null || string.Equals(entry.Recording, sessionKey, StringComparison.OrdinalIgnoreCase)).ToList();
        return ([.. mine.Where(entry => entry.TimeNanoseconds is not null && entry.Recording is not null)], mine.Count(entry => entry.TimeNanoseconds is null));
    }

    /// <summary>A chapter's title for an entry: its reference and kind, such as "E12, finding 3".</summary>
    public static string TitleOf(ReportEvidenceItem entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.IsNullOrWhiteSpace(entry.Kind) ? entry.Reference : $"{entry.Reference}, {entry.Kind}";
    }

    private sealed record Document(
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("entries")] IReadOnlyList<ReportEvidenceItem>? Entries);

    [GeneratedRegex(@"^#{1,6} .*Evidence\s*$")]
    private static partial Regex EvidenceHeading();

    [GeneratedRegex(@"^\| (E\d+) \|")]
    private static partial Regex Row();
}
