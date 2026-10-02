using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Recreation;

// Opens a recreation in the instrumented Chromium, in a profile of its own,
// with DevTools open in a window of its own and the evidence panel loaded as
// an unpacked extension. The browser runs without the recorder bootstrap, so
// it records nothing. It opens a blank tab and a DevTools protocol port on
// the loopback interface, which the recorder connects to before it opens the
// page, so that it holds the browser to the recreation. See
// docs/architecture/page-recreation.md, "Slice 3 design" and "Leaving the
// recreation".
public sealed class RecreationBrowser : IAsyncDisposable
{
    public const string ExtensionFolder = "evidence-panel";
    public const string ProfileFolder = "profile";

    // The switch of chromium/recorder_bridge/recorder_switches.h,
    // kRecreationSwitch.
    public const string RecreationSwitch = "a11y-recorder-recreation";

    public static readonly IReadOnlyList<string> PanelFiles =
        ["manifest.json", "devtools.html", "devtools.js", "panel.html", "panel.js", "panel.css"];

    private readonly Process _process;
    private readonly string _directory;

    private RecreationBrowser(Process process, string directory)
    {
        _process = process;
        _directory = directory;
    }

    public Process Process => _process;

    // Writes the evidence panel extension: its fixed files, and the address
    // from which it reads the evidence.
    public static void WriteExtension(string directory, string evidenceAddress)
    {
        Directory.CreateDirectory(directory);
        var assembly = typeof(RecreationBrowser).Assembly;
        foreach (var name in PanelFiles)
        {
            using var resource = assembly.GetManifestResourceStream($"EvidencePanel.{name}")
                ?? throw new InvalidOperationException($"The evidence panel file {name} is not in the assembly.");
            using var file = File.Create(Path.Combine(directory, name));
            resource.CopyTo(file);
        }
        File.WriteAllText(
            Path.Combine(directory, "config.json"),
            JsonSerializer.Serialize(new { evidenceAddress }));
    }

    // Opens DevTools in a window of its own, so that it and the page can each
    // be moved to any display and maximized there. DevTools keeps its dock
    // state in the profile's preferences.
    public static void WriteProfile(string directory)
    {
        var defaultProfile = Path.Combine(directory, "Default");
        Directory.CreateDirectory(defaultProfile);
        var preferences = new JsonObject
        {
            ["devtools"] = new JsonObject
            {
                ["preferences"] = new JsonObject
                {
                    ["currentDockState"] = "\"undocked\""
                }
            }
        };
        File.WriteAllText(Path.Combine(defaultProfile, "Preferences"), preferences.ToJsonString());
    }

    public const string DevToolsPortFile = "DevToolsActivePort";

    public static ProcessStartInfo CreateStartInfo(
        string executablePath,
        string profileDirectory,
        string extensionDirectory,
        IEnumerable<string>? extraArguments = null)
    {
        var result = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            UseShellExecute = false,
            CreateNoWindow = false
        };
        result.ArgumentList.Add($"--user-data-dir={Path.GetFullPath(profileDirectory)}");
        result.ArgumentList.Add("--no-first-run");
        result.ArgumentList.Add("--no-default-browser-check");
        result.ArgumentList.Add("--disable-background-mode");
        result.ArgumentList.Add("--disable-sync");
        result.ArgumentList.Add($"--disable-extensions-except={Path.GetFullPath(extensionDirectory)}");
        result.ArgumentList.Add($"--load-extension={Path.GetFullPath(extensionDirectory)}");
        result.ArgumentList.Add("--auto-open-devtools-for-tabs");
        result.ArgumentList.Add("--new-window");
        // Port 0 lets the browser choose a free port, which it writes to the
        // profile. It listens on the loopback interface only.
        result.ArgumentList.Add("--remote-debugging-port=0");
        // Stage 3: the instrumented Chromium's recreation mode, in which its
        // renderers impose the recorded values the builder writes on each
        // element. Without the recorder bootstrap it still records nothing.
        result.ArgumentList.Add("--" + RecreationSwitch);
        // Slice 4a: a page recorded at an http address is served at that
        // address, so the browser is kept from upgrading it to https first.
        result.ArgumentList.Add("--disable-features=HttpsUpgrades");
        foreach (var argument in extraArguments ?? [])
        {
            result.ArgumentList.Add(argument);
        }
        result.ArgumentList.Add("about:blank");
        return result;
    }

    // The directory holds the profile and the extension, and is removed when
    // the browser is closed.
    public static RecreationBrowser Open(
        string executablePath,
        string directory,
        RecreationServer server,
        IEnumerable<string>? extraArguments = null)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The instrumented Chromium executable was not found.", executablePath);
        }
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                throw new InvalidOperationException(
                    "The recreation is not opened from an elevated recorder, as the recording browser is not.");
            }
        }
        var profile = Path.Combine(directory, ProfileFolder);
        var extension = Path.Combine(directory, ExtensionFolder);
        WriteProfile(profile);
        WriteExtension(extension, server.EvidenceAddress);
        var process = Process.Start(CreateStartInfo(executablePath, profile, extension, extraArguments))
            ?? throw new InvalidOperationException("The instrumented Chromium did not start.");
        return new RecreationBrowser(process, directory);
    }

    // The browser's DevTools protocol address, once it has written its port
    // to the profile. The file is watched, not polled.
    public async Task<Uri> DevToolsAddressAsync(CancellationToken cancellationToken)
    {
        var profile = Path.Combine(_directory, ProfileFolder);
        var path = Path.Combine(profile, DevToolsPortFile);
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(profile, DevToolsPortFile)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Created += (_, _) => written.TrySetResult();
        watcher.Changed += (_, _) => written.TrySetResult();
        watcher.Renamed += (_, _) => written.TrySetResult();
        watcher.EnableRaisingEvents = true;
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => written.TrySetException(new InvalidOperationException("The instrumented Chromium closed before it opened its DevTools port."));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (TryReadPort(path) is { } address)
            {
                return address;
            }
            if (_process.HasExited)
            {
                throw new InvalidOperationException("The instrumented Chromium closed before it opened its DevTools port.");
            }
            await written.Task.WaitAsync(timeout.Token);
            written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    // The file holds the port and the browser target's path, each on a line.
    // It can be read while the browser is still writing it.
    private static Uri? TryReadPort(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            return lines.Length >= 2 && int.TryParse(lines[0], out var port) && port > 0 && lines[1].StartsWith('/')
                ? new Uri($"ws://127.0.0.1:{port}{lines[1]}")
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
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
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            // The process has gone, or could not be waited for; the profile
            // is still removed below if it can be.
        }
        finally
        {
            _process.Dispose();
        }
        await RemoveAsync(_directory);
    }

    // Chromium's child processes can hold files in the profile for a moment
    // after the browser process exits.
    private static async Task RemoveAsync(string directory)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(250);
            }
        }
    }
}
