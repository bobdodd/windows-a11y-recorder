using System.Globalization;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>Display-only evidence for the open recording, with no playback position.</summary>
public sealed record AndroidTextSizeChecksEvidence(string Summary, IReadOnlyList<string> NodeLines)
{
    public static AndroidTextSizeChecksEvidence? Describe(SessionManifest manifest, CultureInfo culture)
    {
        if (manifest.AndroidPreRecordingTextSizes is not { } snapshot)
        {
            return null;
        }

        var receipt = snapshot.ReceivedUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture);
        var summary = $"Text sizes before recording: {snapshot.PackageName}. {DescribeCoverage(snapshot.Nodes, culture)}" +
            (snapshot.Status == "partial" ? " Android returned a partial snapshot." : "") +
            $" Reply received at {receipt}. This is evidence from one active accessibility window before this recording started, not a reading at the playhead or a result for later screens.";
        return new AndroidTextSizeChecksEvidence(summary, snapshot.Nodes.Select(node => DescribeNode(node, culture)).ToArray());
    }

    private static string DescribeCoverage(IReadOnlyList<AndroidTextSizeNode> nodes, CultureInfo culture)
    {
        var measured = nodes.Count(node => node.TextSizePx.HasValue);
        if (measured > 0)
        {
            return string.Create(culture, $"{measured:N0} of {nodes.Count:N0} nodes reported a size.");
        }

        const string noSizes = "No text sizes available.";
        if (nodes.Count == 0)
        {
            return $"{noSizes} The saved snapshot contains no nodes.";
        }

        var notAdvertised = nodes.Count(node => node.Status == "not-advertised");
        var reason = notAdvertised switch
        {
            0 => string.Create(culture, $"No size could be read from the {nodes.Count:N0} saved {(nodes.Count == 1 ? "node" : "nodes")}."),
            var count when count == nodes.Count => string.Create(culture,
                $"No saved node advertised text-size data ({nodes.Count:N0} checked)."),
            _ => string.Create(culture,
                $"Of {nodes.Count:N0} saved nodes, {notAdvertised:N0} did not advertise text-size data and no size could be read from the other {nodes.Count - notAdvertised:N0}.")
        };
        return $"{noSizes} {reason}";
    }

    private static string DescribeNode(AndroidTextSizeNode node, CultureInfo culture)
    {
        var unit = node.TextSizeUnit switch
        {
            null => "unit not reported",
            0 => "px (0)",
            1 => "dp (1)",
            2 => "sp (2)",
            3 => "pt (3)",
            4 => "in (4)",
            5 => "mm (5)",
            var other => $"Android unit {other}"
        };
        var size = node.TextSizePx is { } px
            ? $"{px.ToString("G", culture)} px rendered; {unit}"
            : node.Status == "not-advertised" ? "size not advertised" : "size unavailable";
        return $"Node {node.Index}, {node.ViewId ?? "no view ID"}: {size}; " +
            $"Android visible to user: {(node.VisibleToUser ? "yes" : "no")}.";
    }
}
