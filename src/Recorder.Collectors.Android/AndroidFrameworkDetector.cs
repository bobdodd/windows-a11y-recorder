using System.Text.RegularExpressions;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Lists the installed base and split APKs on the phone, without pulling,
/// extracting or decompiling them. Keeps only recognised library-entry names.
/// A bundled framework is not proof of the technology used by every screen.
/// </summary>
public static partial class AndroidFrameworkDetector
{
    public const string Source = "apk-entry-names-v1";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task<AndroidAppFramework> ReadAsync(
        IAdb adb, string serial, string package, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // AndroidDeviceCollector also checks names. Keep this entry point safe
        // when called directly: adb shell joins its arguments on the phone.
        if (!PackageName().IsMatch(package))
        {
            return Unavailable("The package name is not safe for an adb shell command.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(Timeout);
        var paths = new List<string>();
        var evidence = new SortedSet<string>(StringComparer.Ordinal);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var read = 0;
        try
        {
            var located = await adb.RunAsync(
                ["-s", serial, "shell", "pm", "path", package], Timeout, deadline.Token).ConfigureAwait(false);
            if (located.ExitCode != 0)
            {
                return Unavailable("The installed APK paths could not be read.");
            }

            var lines = located.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (!line.StartsWith("package:", StringComparison.Ordinal) || !ApkPath().IsMatch(line[8..]))
                {
                    // Do not pass shell metacharacters or diagnostics to unzip.
                    return Unavailable("The APK path listing was not recognised or contained an unsafe path.");
                }

                if (!paths.Contains(line[8..], StringComparer.Ordinal))
                {
                    paths.Add(line[8..]);
                }
            }

            if (paths.Count == 0)
            {
                return Unavailable("No installed APK paths were returned.");
            }

            foreach (var path in paths)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var listing = await adb.RunAsync(
                    ["-s", serial, "shell", "unzip", "-l", path], Timeout, deadline.Token).ConfigureAwait(false);
                if (listing.ExitCode != 0)
                {
                    problems.Add($"Could not list {ApkName(path)} (unzip unavailable, unreadable APK or command failure).");
                    continue;
                }

                var entries = ReadEntries(listing.Output);
                if (entries.Count == 0)
                {
                    problems.Add($"No recognised file listing was returned for {ApkName(path)}.");
                    continue;
                }

                read++;
                foreach (var entry in entries)
                {
                    var match = LibraryEntry().Match(entry);
                    var framework = match.Success ? match.Groups["library"].Value switch
                    {
                        "libflutter.so" => "Flutter",
                        "libreactnative.so" or "libreactnativejni.so" => "React Native",
                        _ => null
                    } : null;
                    if (framework is not null)
                    {
                        names.Add(framework);
                        evidence.Add($"{ApkName(path)}: {entry}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            problems.Add("The framework read exceeded its 15-second limit for this app.");
        }
        catch (IOException)
        {
            problems.Add("adb could not finish reading this app's APK file names.");
        }

        return new AndroidAppFramework(
            Source, names.Count > 0 ? "detected" : read > 0 ? "unknown" : "unavailable",
            [.. names], [.. evidence], read, paths.Count,
            problems.Count > 0 ? string.Join(" ", problems) : null);
    }

    public static AndroidAppFramework Unavailable(string problem) =>
        new(Source, "unavailable", [], [], 0, 0, problem);

    /// <summary>
    /// Android unzip -l's length/date/time/name lines. Ignore headings,
    /// totals, diagnostics and arbitrary strings containing marker names.
    /// No file contents are read and no archive entry is extracted.
    /// </summary>
    internal static IReadOnlyList<string> ReadEntries(string output) =>
        output.Split('\n').Select(line => ListingEntry().Match(line.TrimEnd('\r')))
            .Where(match => match.Success).Select(match => match.Groups["name"].Value).ToArray();

    private static string ApkName(string path) => path[(path.LastIndexOf('/') + 1)..];

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageName();

    [GeneratedRegex(@"^/[A-Za-z0-9_./=+~@%\-]+\.apk$", RegexOptions.CultureInvariant)]
    private static partial Regex ApkPath();

    [GeneratedRegex(@"^\s*\d+\s+\d{2,4}-\d{2}-\d{2,4}\s+\d{2}:\d{2}(?::\d{2})?\s+(?<name>\S+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ListingEntry();

    [GeneratedRegex(@"^lib/[A-Za-z0-9_\-]+/(?<library>libflutter\.so|libreactnative\.so|libreactnativejni\.so)$", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryEntry();
}
