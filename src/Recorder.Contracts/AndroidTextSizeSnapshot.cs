using System.Text.Json.Serialization;

namespace Recorder.Contracts;

/// <summary>One text-free report read from Android before the recording clock starts.</summary>
public sealed record AndroidTextSizeSnapshot(
    string PackageName,
    DateTimeOffset ReceivedUtc,
    string Status,
    IReadOnlyList<AndroidTextSizeNode> Nodes)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AndroidApkFontSnapshot? ApkFonts { get; init; }
}

/// <summary>What Android reported for one node; pixels and the unit code are separate evidence.</summary>
public sealed record AndroidTextSizeNode(
    int Index, string? ViewId, bool VisibleToUser, string Status, double? TextSizePx, int? TextSizeUnit);

/// <summary>App-level declarations, not the typeface or size of any rendered node.</summary>
public sealed record AndroidApkFontSnapshot(
    DateTimeOffset ReadUtc, string? VersionName, long? VersionCode,
    int ApksRead, int ApksTotal, int FlutterManifestsRead,
    IReadOnlyList<string> FontFiles, IReadOnlyList<AndroidApkFontFamily> FlutterFamilies, string? Problem);

public sealed record AndroidApkFontFamily(string Family, string? Asset, int? Weight, string? Style);
