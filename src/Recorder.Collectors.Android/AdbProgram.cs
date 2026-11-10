using System.Diagnostics;

namespace Recorder.Collectors.Android;

/// <summary>Runs adb.exe for the setup window's check.</summary>
public static class AdbProgram
{
    /// <summary>
    /// The first line adb version prints, such as "Android Debug Bridge
    /// version 1.0.41", which shows the program runs on this PC. adb version
    /// does not start adb's server.
    /// </summary>
    public static async Task<string> VersionAsync(string adbPath, CancellationToken cancellation)
    {
        var info = AdbProcess.CreateStartInfo(adbPath, ["version"]);
        using var process = Process.Start(info) ?? throw new IOException($"{adbPath} could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new IOException($"{adbPath} version did not finish within 15 seconds.");
        }

        var text = (await output.ConfigureAwait(false)) + (await error.ConfigureAwait(false));
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        if (process.ExitCode != 0 || !first.StartsWith("Android Debug Bridge", StringComparison.Ordinal))
        {
            throw new IOException($"{adbPath} version ended with exit code {process.ExitCode}: {text.Trim()}");
        }

        return first;
    }
}
