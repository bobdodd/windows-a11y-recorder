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

    /// <summary>
    /// Starts adb with the arguments and leaves it running, as for a program
    /// started on the phone with <c>shell</c>, passing each line it prints to
    /// <paramref name="onLine"/>.
    /// </summary>
    IAdbRunning Start(IReadOnlyList<string> arguments, Action<string> onLine) =>
        throw new NotSupportedException("This adb cannot start a program that keeps running.");
}

/// <summary>An adb command left running. Disposing it waits briefly for it to end, then stops it.</summary>
public interface IAdbRunning : IAsyncDisposable
{
    /// <summary>Completes with adb's exit code when it ends.</summary>
    Task<int> Exited { get; }

    /// <summary>
    /// Stops adb at once, for a program on the phone that never ends by
    /// itself, such as <c>getevent</c>, then disposes it.
    /// </summary>
    ValueTask StopAsync() => DisposeAsync();
}

/// <summary>Runs the recorder's own adb.exe as a process.</summary>
public sealed class AdbProcess(string path) : IAdb
{
    public string Path { get; } = path;

    // adb passes on the phone's output as it is, and Android writes UTF-8.
    // Without an encoding named, .NET reads a redirected stream on Windows
    // with the console's code page, so "°" from TalkBack's log arrived as
    // "Â°" (test 56, 2026-11-10).
    private static readonly System.Text.Encoding Utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// How adb is started for a command whose output is read: no window,
    /// in adb's folder, with its output and errors read as UTF-8.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string path, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(path) ?? ""
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

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

    public IAdbRunning Start(IReadOnlyList<string> arguments, Action<string> onLine)
    {
        ArgumentNullException.ThrowIfNull(onLine);
        var info = CreateStartInfo(Path, arguments);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, line) => { if (line.Data is { } text) onLine(text); };
        process.ErrorDataReceived += (_, line) => { if (line.Data is { } text) onLine(text); };
        process.Exited += (_, _) => exited.TrySetResult(process.ExitCode);
        if (!process.Start())
        {
            process.Dispose();
            throw new IOException($"{Path} could not be started.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new Running(process, exited.Task);
    }

    private sealed class Running(Process process, Task<int> exited) : IAdbRunning
    {
        public Task<int> Exited { get; } = exited;

        public ValueTask StopAsync()
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It had ended.
            }

            return DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Exited.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It ended between the wait and the kill.
                }
            }

            process.Dispose();
        }
    }

    public async Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
    {
        var info = CreateStartInfo(Path, arguments);

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
