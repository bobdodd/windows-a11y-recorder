using System.Text.Json;

namespace Recorder.Analysis.Summary;

/// <summary>
/// One moment's record, as the task's index of moment records gives it:
/// the visit, the moment, its frame's time, why the recorder chose it, its
/// captured frame with its reference and the step that describes it alone,
/// and its page as the recorder rebuilt it, where it was. Any moment's
/// description can be found from it and read out of context; the records
/// are in the index, not in the report (owner, 2026-10-15;
/// recording-summary.md, "The layered report").
/// </summary>
public sealed record MomentRecord(
    int Visit,
    int Moment,
    long FrameTime,
    string Clock,
    IReadOnlyList<string> Reasons,
    string FrameFile,
    string FrameReference,
    string FrameStep,
    MomentRecordPage Page);

/// <summary>
/// A moment's page in its record: whether it was recreated, or why not;
/// its size and scroll; its tiles; and its groups of tiles, each with the
/// step that describes it.
/// </summary>
public sealed record MomentRecordPage(
    bool Recreated,
    string? NotRecreated,
    double Width,
    double Height,
    double ScrollY,
    double ViewportHeight,
    IReadOnlyList<MomentRecordTile> Tiles,
    IReadOnlyList<MomentRecordGroup> Groups);

/// <summary>A tile in a moment's record: its number, place, image key and file in the store, reference, and whether any of it was on the screen.</summary>
public sealed record MomentRecordTile(int Number, double Top, double Height, string Key, string File, string Reference, bool OnScreen);

/// <summary>
/// A group of tiles in a moment's record: its number, its tiles, and the
/// step whose answer describes it; whether it was sent at this moment, or
/// not, as the same pictures at the same places as a group described at
/// an earlier moment, given as its visit and moment.
/// </summary>
public sealed record MomentRecordGroup(int Group, int FirstTile, int LastTile, string Step, bool Sent, int? FromVisit = null, int? FromMoment = null);

/// <summary>The task's index of moment records, analysis/TASK/moments.json, written again after each moment.</summary>
public static class MomentIndex
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task WriteAsync(string path, IReadOnlyList<MomentRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(records, Options), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    public static IReadOnlyList<MomentRecord> Read(string path) =>
        JsonSerializer.Deserialize<List<MomentRecord>>(File.ReadAllText(path), Options) ?? [];
}
