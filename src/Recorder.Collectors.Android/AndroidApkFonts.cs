using System.ComponentModel;
using System.IO.Compression;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>Reads only supported font metadata from the selected app's installed APKs.</summary>
internal static class AndroidApkFonts
{
    private const string FlutterManifest = "assets/flutter_assets/FontManifest.json";
    private const int MaxManifestBytes = 128 * 1024;
    private const int MaxEvidenceItems = 512;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public static async Task<AndroidApkFontSnapshot> ReadAsync(
        IAdb adb, string serial, string package, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(package);
        if (!AndroidRecordingPreflight.IsPackageName(package))
        {
            throw new ArgumentException("Invalid Android package.", nameof(package));
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(Timeout);
        var token = deadline.Token;
        var paths = Array.Empty<string>();
        var files = new SortedSet<string>(StringComparer.Ordinal);
        var families = new HashSet<AndroidApkFontFamily>();
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        var read = 0;
        var manifests = 0;
        AndroidAppVersion? version = null;
        try
        {
            version = await VersionAsync(adb, serial, package, token).ConfigureAwait(false);
            var location = await adb.RunAsync(["-s", serial, "shell", "pm", "path", package], Timeout, token).ConfigureAwait(false);
            var lines = location.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (location.ExitCode != 0 || lines.Length == 0 || !lines.All(AndroidFrameworkDetector.IsApkPathLine))
            {
                problems.Add("Installed APK paths were not available as a safe package listing.");
            }
            else
            {
                paths = lines.Select(line => line[8..]).Distinct(StringComparer.Ordinal).ToArray();
                if (paths.Length > 64)
                {
                    problems.Add("The installed package exceeded the 64-APK collection limit.");
                }

                foreach (var remote in paths.Take(64))
                {
                    token.ThrowIfCancellationRequested();
                    // apk files are never unpacked into loose files, opened as
                    // executable content, logged, or included in the recording.
                    var temporary = Path.Combine(Path.GetTempPath(), $"recorder-apk-{Guid.NewGuid():N}.apk");
                    try
                    {
                        var pull = await adb.RunAsync(["-s", serial, "pull", remote, temporary], Timeout, token).ConfigureAwait(false);
                        if (pull.ExitCode != 0)
                        {
                            problems.Add("At least one APK could not be pulled.");
                            continue;
                        }

                        using var stream = File.OpenRead(temporary);
                        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                        var found = Inspect(archive, token);
                        files.UnionWith(found.Files);
                        families.UnionWith(found.Families);
                        read++;
                        manifests += found.ManifestRead ? 1 : 0;
                        if (found.Incomplete)
                        {
                            problems.Add("At least one APK's font metadata was invalid or exceeded collection limits.");
                        }
                        if (files.Count > MaxEvidenceItems || families.Count > MaxEvidenceItems)
                        {
                            problems.Add("The recording's font evidence exceeded its collection limit.");
                            break;
                        }
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception)
                    {
                        problems.Add("At least one APK could not be read as a ZIP archive.");
                    }
                    finally
                    {
                        // Report a cleanup failure explicitly; do not start a
                        // recording while an APK this attempt pulled remains.
                        try
                        {
                            File.Delete(temporary);
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        {
                            throw new AndroidTextSizeRefusedException(
                                $"The temporary APK could not be deleted. Close any program using it and delete this file: {temporary}");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            problems.Add("The APK font read reached its 90-second time limit.");
        }
        catch (Exception error) when (error is not AndroidTextSizeRefusedException &&
            error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            problems.Add("The APK font read could not finish.");
        }

        cancellation.ThrowIfCancellationRequested();
        if (read > 0)
        {
            // If we cannot establish a stable version, do not attribute the
            // files already read to the package at the start.
            AndroidAppVersion? after = null;
            try
            {
                after = await VersionAsync(adb, serial, package, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or Win32Exception ||
                error is OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                // Handled as unavailable below.
            }

            if (version?.VersionName is null || version.VersionCode is null || version.LastUpdateTime is null ||
                version.VersionName != after?.VersionName || version.VersionCode != after.VersionCode ||
                version.LastUpdateTime != after.LastUpdateTime)
            {
                files.Clear();
                families.Clear();
                manifests = 0;
                read = 0;
                problems.Add("The installed version was not stable or could not be verified across the APK read.");
            }
        }

        return new(DateTimeOffset.UtcNow, version?.VersionName, version?.VersionCode, read, paths.Length, manifests,
            files.Take(MaxEvidenceItems).ToArray(), families.OrderBy(item => item.Family, StringComparer.Ordinal)
                .ThenBy(item => item.Asset, StringComparer.Ordinal).ThenBy(item => item.Weight)
                .ThenBy(item => item.Style, StringComparer.Ordinal).Take(MaxEvidenceItems).ToArray(),
            problems.Count == 0 ? null : string.Join(' ', problems));
    }

    private static async Task<AndroidAppVersion?> VersionAsync(IAdb adb, string serial, string package, CancellationToken token)
    {
        var result = await adb.RunAsync(["-s", serial, "shell", "dumpsys", "package", package],
            TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        return result.ExitCode == 0 ? AndroidDeviceCollector.ParseVersion(package, result.Output) : null;
    }

    internal static (IReadOnlyList<string> Files, IReadOnlyList<AndroidApkFontFamily> Families, bool ManifestRead, bool Incomplete)
        Inspect(ZipArchive archive, CancellationToken token)
    {
        var files = new List<string>();
        var families = new List<AndroidApkFontFamily>();
        var incomplete = false;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetExtension(entry.FullName).ToLowerInvariant() is not (".otf" or ".ttf" or ".ttc" or ".woff" or ".woff2"))
            {
                continue;
            }
            if (files.Count == MaxEvidenceItems)
            {
                incomplete = true;
                break;
            }
            if (Clean(entry.FullName) is { } name)
            {
                files.Add(name);
            }
            else
            {
                incomplete = true;
            }
        }

        var manifest = archive.GetEntry(FlutterManifest);
        if (manifest is null)
        {
            return (files, families, false, incomplete);
        }
        if (manifest.Length is <= 0 or > MaxManifestBytes)
        {
            return (files, families, false, true);
        }

        try
        {
            using var source = manifest.Open();
            var buffer = new byte[(int)manifest.Length];
            source.ReadExactly(buffer);
            using var doc = JsonDocument.Parse(buffer, new JsonDocumentOptions { MaxDepth = 8 });
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (StringProperty(item, "family") is not { } family ||
                    !item.TryGetProperty("fonts", out var fontEntries) || fontEntries.ValueKind != JsonValueKind.Array)
                {
                    incomplete = true;
                    continue;
                }

                foreach (var font in fontEntries.EnumerateArray())
                {
                    if (families.Count == MaxEvidenceItems)
                    {
                        return (files, families, true, true);
                    }
                    if (StringProperty(font, "asset") is not { } asset)
                    {
                        incomplete = true;
                        continue;
                    }

                    var weight = font.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var parsed)
                        ? parsed : (int?)null;
                    families.Add(new(family, asset, weight, StringProperty(font, "style")));
                }
            }
            return (files, families, true, incomplete);
        }
        catch (Exception error) when (error is JsonException or IOException or InvalidDataException or InvalidOperationException)
        {
            return (files, [], false, true);
        }
    }

    private static string? StringProperty(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? Clean(value.GetString()) : null;

    private static string? Clean(string? value) =>
        value is { Length: > 0 and <= 300 } && !value.Any(char.IsControl) ? value : null;
}
