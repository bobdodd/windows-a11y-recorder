using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Recorder.Analysis;

/// <summary>
/// The llama.cpp server the worker starts on this PC, in router mode: it
/// listens on the loopback address only, holds one model at a time, serves
/// one request at a time, and loads a model only when the worker asks.
/// Each locally served profile is a section of a preset file, with the
/// profile's settings written as the server's preset keys, which are its
/// command-line arguments without the leading dashes.
/// </summary>
public sealed class LocalModelServer : IAsyncDisposable
{
    // Settings the worker sets itself, which a profile may not change.
    private static readonly HashSet<string> WorkerKeys = new(StringComparer.Ordinal)
    {
        "host", "port", "api-key", "api-key-file", "m", "model", "mm", "mmproj",
        "models-max", "models-preset", "models-dir", "models-autoload", "no-models-autoload",
        "np", "parallel", "hf", "hf-repo", "hfr", "alias", "a"
    };

    private readonly Process _process;
    private readonly string _pidPath;
    private readonly Task _logCopy;

    private LocalModelServer(Process process, string pidPath, Task logCopy)
    {
        _process = process;
        _pidPath = pidPath;
        _logCopy = logCopy;
    }

    /// <summary>
    /// A profile's server arguments as preset entries, or why they cannot
    /// be. An option followed by a value gives that value; an option alone
    /// is a flag, written as true. A value with spaces is written in double
    /// quotes.
    /// </summary>
    public static (IReadOnlyList<KeyValuePair<string, string>> Entries, string? Problem) PresetEntries(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var character in arguments)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }

        if (quoted)
        {
            return ([], "The server arguments have an unclosed quote.");
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        var entries = new List<KeyValuePair<string, string>>();
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!IsOption(token))
            {
                return ([], $"The server argument \"{token}\" does not follow an option.");
            }

            var key = token.TrimStart('-');
            if (WorkerKeys.Contains(key))
            {
                return ([], $"The server argument \"{token}\" is set by the worker, not by a profile.");
            }

            if (index + 1 < tokens.Count && !IsOption(tokens[index + 1]))
            {
                entries.Add(new(key, tokens[index + 1]));
                index++;
            }
            else
            {
                entries.Add(new(key, "true"));
            }
        }

        return (entries, null);
    }

    private static bool IsOption(string token) =>
        token.Length > 1 && token[0] == '-' &&
        !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>The preset file for the locally served profiles.</summary>
    public static string Preset(IEnumerable<ModelProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var text = new StringBuilder();
        text.Append("version = 1\n");
        foreach (var profile in profiles.Where(profile => profile.Local is not null))
        {
            var local = profile.Local!;
            text.Append('\n').Append('[').Append(profile.Model).Append("]\n");
            text.Append("model = ").Append(local.ModelPath).Append('\n');
            if (local.ImageAddOnPath is { } addOn)
            {
                text.Append("mmproj = ").Append(addOn).Append('\n');
            }

            foreach (var (key, value) in PresetEntries(local.Arguments).Entries)
            {
                text.Append(key).Append(" = ").Append(value).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>The server's arguments, after the preset is written.</summary>
    public static IReadOnlyList<string> Arguments(string presetPath, int port) =>
    [
        "--models-preset", presetPath,
        "--models-max", "1",
        "--no-models-autoload",
        "--parallel", "1",
        "--host", "127.0.0.1",
        "--port", port.ToString(CultureInfo.InvariantCulture)
    ];

    /// <summary>
    /// Writes the preset, starts the server with its output in a log file,
    /// and waits until it answers its health request. A server left running
    /// by an earlier worker is stopped first.
    /// </summary>
    public static async Task<LocalModelServer> StartAsync(
        LocalServerSettings settings,
        IEnumerable<ModelProfile> profiles,
        string directory,
        HttpClient client,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(client);
        Directory.CreateDirectory(directory);
        var pidPath = Path.Combine(directory, "llama-server.pid");
        StopLeftOver(pidPath);

        var presetPath = Path.Combine(directory, "llama-server-models.ini");
        await File.WriteAllTextAsync(presetPath, Preset(profiles), cancellationToken).ConfigureAwait(false);

        var start = new ProcessStartInfo(settings.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(settings.Executable) ?? directory
        };
        foreach (var argument in Arguments(presetPath, settings.Port))
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new ModelEndpointException("The local model server did not start.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ModelEndpointException($"The local model server {settings.Executable} could not be started: {exception.Message}", exception);
        }

        await File.WriteAllTextAsync(pidPath, process.Id.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        var logPath = Path.Combine(directory, "llama-server.log");
        var log = TextWriter.Synchronized(new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true });
        var logCopy = Task.WhenAll(
            CopyAsync(process.StandardOutput, log),
            CopyAsync(process.StandardError, log))
            .ContinueWith(_ => log.Dispose(), TaskScheduler.Default);
        var server = new LocalModelServer(process, pidPath, logCopy);

        var health = new Uri($"http://127.0.0.1:{settings.Port}/health");
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (process.HasExited)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw new ModelEndpointException($"The local model server stopped as it started, with exit code {process.ExitCode}. Its log is {logPath}.");
            }

            try
            {
                using var response = await client.GetAsync(health, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return server;
                }
            }
            catch (HttpRequestException)
            {
            }

            if (DateTime.UtcNow > deadline)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw new ModelEndpointException($"The local model server did not answer within {timeout.TotalSeconds:0} seconds. Its log is {logPath}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
    }

    public bool HasExited => _process.HasExited;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
        }

        await _logCopy.ConfigureAwait(false);
        _process.Dispose();
        TryDelete(_pidPath);
    }

    private static void StopLeftOver(string pidPath)
    {
        if (!File.Exists(pidPath))
        {
            return;
        }

        try
        {
            if (int.TryParse(File.ReadAllText(pidPath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                using var process = Process.GetProcessById(id);
                if (process.ProcessName.StartsWith("llama-server", StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(10_000);
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            // The process has gone, or is not the server.
        }

        TryDelete(pidPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task CopyAsync(StreamReader reader, TextWriter log)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            log.WriteLine(line);
        }
    }
}
