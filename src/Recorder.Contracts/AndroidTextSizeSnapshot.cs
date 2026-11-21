namespace Recorder.Contracts;

/// <summary>One text-free report read from Android before the recording clock starts.</summary>
public sealed record AndroidTextSizeSnapshot(
    string PackageName,
    DateTimeOffset ReceivedUtc,
    string Status,
    IReadOnlyList<AndroidTextSizeNode> Nodes);

/// <summary>What Android reported for one node; pixels and the unit code are separate evidence.</summary>
public sealed record AndroidTextSizeNode(
    int Index, string? ViewId, bool VisibleToUser, string Status, double? TextSizePx, int? TextSizeUnit);
