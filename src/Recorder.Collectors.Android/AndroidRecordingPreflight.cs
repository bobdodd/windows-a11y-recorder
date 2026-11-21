using System.Text.RegularExpressions;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>Discovers visible activities and reads text sizes only on explicit request.</summary>
public sealed partial class AndroidRecordingPreflight(IAdb adb, string serial)
{
    public async Task<IReadOnlyList<string>> VisibleAppsAsync(CancellationToken cancellation)
    {
        var result = await adb.RunAsync(
            // Filter on the phone to keep the diagnostic dump out of Windows.
            ["-s", serial, "shell",
                "dumpsys activity visible | grep -E '^(ACTIVITY MANAGER VISIBLE ACTIVITIES|[[:blank:]]*packageName=)'"],
            TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false);
        if (result.ExitCode != 0 || !result.Output.StartsWith(
                "ACTIVITY MANAGER VISIBLE ACTIVITIES", StringComparison.Ordinal))
        {
            throw new IOException("Android did not return a visible-app list.");
        }

        return ReadVisibleApps(result.Output);
    }

    internal static IReadOnlyList<string> ReadVisibleApps(string output) => output
        .Split('\n')
        .Select(line => VisiblePackageLine().Match(line))
        .Where(match => match.Success)
        .Select(match => match.Groups["package"].Value)
        .Where(IsPackageName)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    internal static bool IsPackageName(string package) => package.Length <= 255 && PackageName().IsMatch(package);

    public async Task<AndroidTextSizeSnapshot> ReadTextSizesAsync(string package, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(package);
        if (!IsPackageName(package))
        {
            throw new ArgumentException("The selected package name is not valid.", nameof(package));
        }

        await using var connection = await CompanionLink.OpenAsync(
            adb, serial, CompanionLink.StopwatchNanoseconds, cancellation).ConfigureAwait(false);
        return await ReadTextSizesAsync(connection, package, TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false);
    }

    internal static async Task<AndroidTextSizeSnapshot> ReadTextSizesAsync(
        CompanionConnection connection, string package, TimeSpan timeout, CancellationToken cancellation)
    {
        var reply = await connection.ReadTextRenderingAsync(package, timeout, cancellation).ConfigureAwait(false);
        var received = DateTimeOffset.UtcNow;
        if (reply.Status is not ("complete" or "partial"))
        {
            throw new AndroidTextSizeRefusedException(reply.Status switch
            {
                "not-active" => "The selected app is not the active accessibility window. Bring it to the foreground on the phone.",
                "window-changed" => "The active window changed during the read. Return to the selected app and try again.",
                "busy" => "The companion is already reading text sizes. Try again.",
                _ => "The companion could not read text sizes from the selected app."
            });
        }

        return new AndroidTextSizeSnapshot(package, received, reply.Status, reply.Nodes.Select(node =>
            new AndroidTextSizeNode(node.Index, node.ViewId, node.VisibleToUser,
                node.Status, node.TextSizePx, node.TextSizeUnit)).ToArray());
    }

    [GeneratedRegex(@"^[ \t]*packageName=(?<package>[^ \t\r\n]+)[ \t]+processName=", RegexOptions.CultureInvariant)]
    private static partial Regex VisiblePackageLine();

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackageName();
}

/// <summary>A reason the selected app produced no snapshot, safe to show without adb details.</summary>
public sealed class AndroidTextSizeRefusedException(string message) : IOException(message);
