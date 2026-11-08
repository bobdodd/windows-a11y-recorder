using System.Diagnostics;

namespace Recorder.Collectors.Android;

/// <summary>What one adb command printed, and how it ended.</summary>
public sealed record AdbResult(int ExitCode, string Output, string Error)
{
    /// <summary>The output and error together, as adb prints both.</summary>
    public string Text => (Output + Error).Trim();
}

/// <summary>
/// Runs adb commands. The recorder uses <see cref="AdbProcess"/>; tests
/// use a fake that returns sample output, as the PowerShell checks were
/// tested against a fake adb (android-implementation-plan.md, slice 1).
/// </summary>
public interface IAdb
{
    /// <summary>The adb.exe this runs.</summary>
    string Path { get; }

    /// <summary>
    /// Runs adb with the arguments and returns what it printed. A command
    /// that does not finish within the timeout is stopped and reported as
    /// an <see cref="IOException"/>.
    /// </summary>
    Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation);

    /// <summary>
    /// Starts adb's server if none is running, and returns adb's exit code.
    /// Run on its own, so that the server, which outlives the command, is
    /// not started by a command whose output is being read.
    /// </summary>
    Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation);
}

/// <summary>Runs the recorder's own adb.exe as a process.</summary>
public sealed class AdbProcess(string path) : IAdb
{
    public string Path { get; } = path;

    public async Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation)
    {
        // Nothing is redirected: a server started with this command's
        // output handles could keep them open after the command ends.
        var info = new ProcessStartInfo(Path, "start-server")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(Path) ?? ""
        };
        using var process = Process.Start(info) ?? throw new IOException($"{Path} could not be started.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            cancellation.ThrowIfCancellationRequested();
            throw new IOException($"adb start-server did not finish within {timeout.TotalSeconds:0} seconds.");
        }
    }

    public async Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(Path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(Path) ?? ""
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new IOException($"{Path} could not be started.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return new AdbResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended between the timeout and the kill.
            }

            cancellation.ThrowIfCancellationRequested();
            throw new IOException(
                $"adb {string.Join(' ', arguments)} did not finish within {timeout.TotalSeconds:0} seconds.");
        }
    }
}
