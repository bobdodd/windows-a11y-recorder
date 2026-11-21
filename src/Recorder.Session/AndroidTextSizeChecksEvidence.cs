using System.Globalization;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>Display-only evidence for the open recording, with no playback position.</summary>
public sealed record AndroidTextSizeChecksEvidence(string Summary, IReadOnlyList<string> NodeLines)
{
    public static string SummarizeApkAccessibility(SessionManifest manifest)
    {
        if (manifest.AndroidPreRecordingTextSizes is not { ApkFonts: { } apk } snapshot)
        {
            return "APK accessibility evidence: not collected.";
        }

        var version = apk.VersionName is { } name && apk.VersionCode is { } code
            ? $", version {name} (code {code})" : ", version unavailable";
        var detail = apk.Accessibility is { SchemaVersion: 1 } evidence
            ? $" {evidence.Items.Count} source entries saved."
            : " Font-only inventory.";
        return $"APK accessibility evidence for {snapshot.PackageName}{version}: " +
            $"{apk.ApksRead} of {apk.ApksTotal} installed APKs read." +
            (apk.ApksRead > 0 ? detail : " No APK evidence available.");
    }

    public static string? DescribeApkAccessibility(SessionManifest manifest)
    {
        if (manifest.AndroidPreRecordingTextSizes is not { ApkFonts: { Accessibility: { SchemaVersion: 1 } evidence } apk } snapshot)
        {
            return DescribeAppFonts(manifest);
        }

        var version = apk.VersionName is { } name && apk.VersionCode is { } code
            ? $"version {name} (code {code})" : "version unavailable";
        var lines = new List<string>
        {
            $"APK accessibility evidence for {snapshot.PackageName}, {version}. " +
            $"Read before recording ({apk.ReadUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss 'UTC'}). " +
            "These are package artifacts and declarations, not observations from a recorded window. " +
            "A component locator is not a link to a screen visit."
        };
        if (apk.Problem is { } problem)
        {
            lines.Add($"Incomplete: {problem}");
        }
        lines.Add("");
        lines.Add("Coverage");
        lines.AddRange(evidence.Coverage.Select(item => $"{item.Reader}: {item.Status}. {item.Detail}"));
        foreach (var group in evidence.Items.GroupBy(item => (item.Kind, item.Scope, item.Category))
            .OrderBy(group => group.Key.Kind, StringComparer.Ordinal).ThenBy(group => group.Key.Category, StringComparer.Ordinal))
        {
            var scope = group.Key.Scope == "app" ? "app-wide" : "component";
            lines.Add("");
            lines.Add($"{group.Key.Category} ({group.Key.Kind}, {scope})");
            lines.AddRange(group.Select(item => $"{ShowValue(item.Value)} [{item.Split}: {item.Locator}]"));
        }
        // The legacy font lists are still available if the larger structured
        // inventory could not retain a particular entry.
        var savedFontPaths = evidence.Items.Where(item => item.Category == "Font file")
            .Select(item => item.Value).ToHashSet(StringComparer.Ordinal);
        lines.AddRange(apk.FontFiles.Where(path => !savedFontPaths.Contains(path)).Select(path => $"Bundled font file: {path}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string ShowValue(string value) => value.Any(char.IsControl)
        ? string.Concat(value.Select(character => char.IsControl(character) ? $"\\u{(int)character:X4}" : character.ToString()))
        : value;

    public static string? DescribeAppFonts(SessionManifest manifest)
    {
        if (manifest.AndroidPreRecordingTextSizes is not { ApkFonts: { } fonts } snapshot)
        {
            return null;
        }

        var version = fonts.VersionName is { } name && fonts.VersionCode is { } code
            ? $"version {name} (code {code})" : "version unavailable";
        var header = $"APK font inventory for {snapshot.PackageName}, {version}. Read {fonts.ApksRead} of {fonts.ApksTotal} installed APKs " +
            $"before recording ({fonts.ReadUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss 'UTC'}); Flutter font manifests read: {fonts.FlutterManifestsRead}.";
        var files = fonts.FontFiles.Select(item => $"Bundled font file: {item}");
        var families = fonts.FlutterFamilies.Select(item => $"Flutter font declaration: {item.Family}; asset {item.Asset}" +
            (item.Weight is { } weight ? $"; weight {weight}" : "") + (item.Style is { } style ? $"; style {style}" : ""));
        return string.Join(Environment.NewLine, new[] { header }.Concat(files).Concat(families)
            .Concat(fonts.Problem is { } problem ? [$"Incomplete: {problem}"] : [])
            .Append("These are app-wide files and declarations. They do not establish the font or size used by any screen. " +
                "This inventory does not inspect native styles, compiled framework code or form layouts."));
    }

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
