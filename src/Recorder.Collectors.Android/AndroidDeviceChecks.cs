using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.Collectors.Android;

/// <summary>What the checks found of the phone.</summary>
public enum PhoneState
{
    /// <summary>The phone was not looked for: adb is missing or blocked.</summary>
    NotChecked,

    /// <summary>adb sees no phone.</summary>
    NotFound,

    /// <summary>adb sees more than one phone or emulator.</summary>
    MoreThanOne,

    /// <summary>The phone has not allowed this PC ("unauthorized").</summary>
    NotAllowed,

    /// <summary>adb sees the phone but cannot reach it ("offline").</summary>
    Offline,

    /// <summary>adb reports another state, given in the text.</summary>
    Other,

    /// <summary>Connected and allowed ("device").</summary>
    Connected
}

/// <summary>One line of the checks panel: what was checked, and what was found.</summary>
public sealed record AndroidCheck(string Name, string Text, bool Ok);

/// <summary>
/// The checks panel's content (android-implementation-plan.md, slice 1),
/// in the order shown.
/// </summary>
public sealed record AndroidCheckReport(PhoneState Phone, IReadOnlyList<AndroidCheck> Checks)
{
    /// <summary>Each check as "Name: text", one to a line.</summary>
    public string Text => string.Join(Environment.NewLine, Checks.Select(check => $"{check.Name}: {check.Text}"));
}

/// <summary>A line of adb devices -l: the serial, the state, and the details after them.</summary>
public sealed record AdbDevice(string Serial, string State, IReadOnlyDictionary<string, string> Details);

/// <summary>What the phone's own settings say, read in one adb shell command.</summary>
public sealed record PhoneDetails(
    string Model,
    string AndroidVersion,
    string Build,
    string SecurityPatch,
    int? ScreenWidth,
    int? ScreenHeight,
    int? OverrideWidth,
    int? OverrideHeight,
    string EnabledServices)
{
    /// <summary>TalkBack is on when its service is among the enabled accessibility services.</summary>
    public bool TalkBackOn =>
        EnabledServices.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(service => service.StartsWith(AndroidDeviceChecks.TalkBackPackage + "/", StringComparison.Ordinal));
}

/// <summary>
/// Checks adb, the adb server and the phone for the checks panel, as the
/// feasibility checks did in PowerShell (scripts/Test-AndroidFeasibility.ps1).
/// Nothing here changes a setting on the phone.
/// </summary>
public sealed partial class AndroidDeviceChecks(IAdb adb, IAdbServerProbe server, Func<string, bool> fileExists)
{
    /// <summary>TalkBack's package, as installed on the Pixel 10 Pro XL.</summary>
    public const string TalkBackPackage = "com.google.android.marvin.talkback";

    /// <summary>
    /// The steps the feasibility checks needed for a phone adb does not
    /// see (docs/validation/android-feasibility-plan.md).
    /// </summary>
    public static readonly IReadOnlyList<string> PhoneNotFoundSteps =
    [
        "On the phone, turn on USB debugging: Settings, System, Developer options, USB debugging.",
        "Unlock the phone and connect it with a USB cable that carries data, to a port on the PC itself rather than a hub.",
        "On the phone, pull down the notifications, tap the USB notification, and under \"Use USB for\" choose \"File transfer\".",
        "On the phone, in \"Allow USB debugging?\", tick \"Always allow from this computer\" and choose Allow."
    ];

    private static readonly TimeSpan QuickCommand = TimeSpan.FromSeconds(10);

    // One shell command for everything the panel shows, each part after a
    // marker so that a part that prints nothing or two lines is still found.
    internal const string DetailsScript =
        "echo @model; getprop ro.product.model; " +
        "echo @release; getprop ro.build.version.release; " +
        "echo @build; getprop ro.build.id; " +
        "echo @patch; getprop ro.build.version.security_patch; " +
        "echo @size; wm size; " +
        "echo @services; settings get secure enabled_accessibility_services; " +
        "echo @end";

    private AdbClientVersion? _client;

    /// <summary>Runs every check once.</summary>
    public async Task<AndroidCheckReport> CheckAsync(CancellationToken cancellation)
    {
        var checks = new List<AndroidCheck>();
        if (!fileExists(adb.Path))
        {
            checks.Add(new AndroidCheck(
                "adb",
                "not installed. Open the Android menu and choose \"Set up Android recording...\" to download it.",
                false));
            return new AndroidCheckReport(PhoneState.NotChecked, checks);
        }

        try
        {
            _client ??= ParseVersion((await adb.RunAsync(["version"], QuickCommand, cancellation).ConfigureAwait(false)).Text);
        }
        catch (IOException exception)
        {
            checks.Add(new AndroidCheck("adb", $"does not run: {exception.Message}", false));
            return new AndroidCheckReport(PhoneState.NotChecked, checks);
        }

        if (_client is null)
        {
            checks.Add(new AndroidCheck("adb", $"{adb.Path} did not report its version.", false));
            return new AndroidCheckReport(PhoneState.NotChecked, checks);
        }

        checks.Add(new AndroidCheck("adb", $"ready, version {_client.Text}.", true));

        var running = await server.ReadAsync(cancellation).ConfigureAwait(false);
        if (running is not null && !running.Programs.Any(IsOurs))
        {
            var from = running.Programs.Count == 0
                ? "its program could not be found"
                : "from " + string.Join(", ", running.Programs);
            if (running.Version != _client.Server)
            {
                checks.Add(new AndroidCheck(
                    "adb server",
                    $"another program's adb server is running ({from}), version {VersionText(running.Version)}. " +
                    $"The recorder's adb is version {_client.Server}, and using it would stop that server, so the recorder leaves it alone. " +
                    "Close the program that started it, such as Android Studio, or run that program's \"adb kill-server\". The checks then go on.",
                    false));
                return new AndroidCheckReport(PhoneState.NotChecked, checks);
            }

            checks.Add(new AndroidCheck(
                "adb server",
                $"another program's adb server is running ({from}). It is the same version as the recorder's, so the recorder uses it and leaves it running.",
                true));
        }
        else
        {
            if (running is null)
            {
                await adb.StartServerAsync(TimeSpan.FromSeconds(30), cancellation).ConfigureAwait(false);
            }

            checks.Add(new AndroidCheck("adb server", "the recorder's own.", true));
        }

        AdbResult listed;
        try
        {
            listed = await adb.RunAsync(["devices", "-l"], QuickCommand, cancellation).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            checks.Add(new AndroidCheck("Phone", $"adb could not list phones: {exception.Message}", false));
            return new AndroidCheckReport(PhoneState.NotChecked, checks);
        }

        if (listed.ExitCode != 0)
        {
            checks.Add(new AndroidCheck("Phone", $"adb could not list phones: {listed.Text}", false));
            return new AndroidCheckReport(PhoneState.NotChecked, checks);
        }

        var devices = ParseDevices(listed.Output);
        if (devices.Count == 0)
        {
            checks.Add(new AndroidCheck("Phone", "not found.", false));
            for (var step = 0; step < PhoneNotFoundSteps.Count; step++)
            {
                checks.Add(new AndroidCheck($"Step {step + 1}", PhoneNotFoundSteps[step], false));
            }

            return new AndroidCheckReport(PhoneState.NotFound, checks);
        }

        if (devices.Count > 1)
        {
            checks.Add(new AndroidCheck(
                "Phone",
                $"more than one phone or emulator is connected ({string.Join(", ", devices.Select(Describe))}). Connect only the phone to record.",
                false));
            return new AndroidCheckReport(PhoneState.MoreThanOne, checks);
        }

        var device = devices[0];
        switch (device.State)
        {
            case "unauthorized":
                checks.Add(new AndroidCheck(
                    "Phone",
                    $"found ({Describe(device)}) but it has not allowed this PC yet. Unlock the phone; in \"Allow USB debugging?\", tick \"Always allow from this computer\" and choose Allow.",
                    false));
                return new AndroidCheckReport(PhoneState.NotAllowed, checks);
            case "offline":
                checks.Add(new AndroidCheck(
                    "Phone",
                    $"found ({Describe(device)}) but offline. Unplug the cable and plug it in again.",
                    false));
                return new AndroidCheckReport(PhoneState.Offline, checks);
            case "device":
                break;
            default:
                checks.Add(new AndroidCheck("Phone", $"found ({Describe(device)}), in the state \"{device.State}\".", false));
                return new AndroidCheckReport(PhoneState.Other, checks);
        }

        PhoneDetails details;
        try
        {
            var shell = await adb.RunAsync(["-s", device.Serial, "shell", DetailsScript], QuickCommand, cancellation).ConfigureAwait(false);
            if (shell.ExitCode != 0)
            {
                checks.Add(new AndroidCheck("Phone", $"found ({Describe(device)}) but its settings could not be read: {shell.Text}", false));
                return new AndroidCheckReport(PhoneState.Other, checks);
            }

            details = ParseDetails(shell.Output);
        }
        catch (IOException exception)
        {
            checks.Add(new AndroidCheck("Phone", $"found ({Describe(device)}) but its settings could not be read: {exception.Message}", false));
            return new AndroidCheckReport(PhoneState.Other, checks);
        }

        checks.Add(new AndroidCheck("Phone", $"{Or(details.Model, "model not reported")}, connected and allowed (serial {device.Serial}).", true));
        checks.Add(new AndroidCheck(
            "System",
            $"Android {Or(details.AndroidVersion, "version not reported")}, build {Or(details.Build, "not reported")}, security patch {Or(details.SecurityPatch, "not reported")}.",
            true));
        checks.Add(new AndroidCheck("Screen", ScreenText(details), details.ScreenWidth is not null));
        // TalkBack is one of the things tested, not needed for every
        // recording: magnification, layout and colour are tested without it
        // (owner, 2026-11-08). So off is reported, not asked to be changed,
        // and the reminder for TalkBack's own setting shows only with it on.
        checks.Add(new AndroidCheck("TalkBack", details.TalkBackOn ? "on." : "off.", true));
        if (details.TalkBackOn)
        {
            checks.Add(new AndroidCheck(
                "Display speech output",
                "the recorder cannot read this setting. Check that it is on: TalkBack's settings, Advanced settings, Developer settings, \"Display speech output\".",
                false));
        }

        return new AndroidCheckReport(PhoneState.Connected, checks);
    }

    /// <summary>The devices in adb devices -l output; the heading and daemon messages are skipped.</summary>
    public static IReadOnlyList<AdbDevice> ParseDevices(string output)
    {
        var devices = new List<AdbDevice>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('*') || line.StartsWith("List of devices", StringComparison.Ordinal))
            {
                continue;
            }

            var match = DeviceLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var details = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match pair in DetailPair().Matches(match.Groups["rest"].Value))
            {
                details[pair.Groups["key"].Value] = pair.Groups["value"].Value;
            }

            devices.Add(new AdbDevice(match.Groups["serial"].Value, match.Groups["state"].Value, details));
        }

        return devices;
    }

    /// <summary>The parts of <see cref="DetailsScript"/>'s output.</summary>
    public static PhoneDetails ParseDetails(string output)
    {
        var parts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith('@'))
            {
                current = [];
                parts[line[1..]] = current;
                continue;
            }

            if (line.Length > 0)
            {
                current?.Add(line);
            }
        }

        string Part(string name) => parts.TryGetValue(name, out var lines) ? string.Join(" ", lines) : "";

        int? width = null, height = null, overrideWidth = null, overrideHeight = null;
        if (parts.TryGetValue("size", out var sizes))
        {
            foreach (var line in sizes)
            {
                var size = ScreenSize().Match(line);
                if (!size.Success)
                {
                    continue;
                }

                var w = int.Parse(size.Groups["w"].Value, CultureInfo.InvariantCulture);
                var h = int.Parse(size.Groups["h"].Value, CultureInfo.InvariantCulture);
                if (size.Groups["kind"].Value == "Physical")
                {
                    (width, height) = (w, h);
                }
                else
                {
                    (overrideWidth, overrideHeight) = (w, h);
                }
            }
        }

        var services = Part("services");
        if (services == "null")
        {
            services = "";
        }

        return new PhoneDetails(
            Part("model"),
            Part("release"),
            Part("build"),
            Part("patch"),
            width,
            height,
            overrideWidth,
            overrideHeight,
            services);
    }

    /// <summary>
    /// The version adb version prints, such as "1.0.41 (Platform-Tools
    /// 37.0.1)". The last number of the first line is the server version
    /// a client expects (adb.cpp in the adb sources).
    /// </summary>
    internal static AdbClientVersion? ParseVersion(string text)
    {
        var first = AdbVersionLine().Match(text);
        if (!first.Success)
        {
            return null;
        }

        var tools = ToolsVersionLine().Match(text);
        var version = first.Groups["version"].Value;
        var server = int.Parse(first.Groups["server"].Value, CultureInfo.InvariantCulture);
        return new AdbClientVersion(tools.Success ? $"{version} (Platform-Tools {tools.Groups["tools"].Value})" : version, server);
    }

    private bool IsOurs(string program) =>
        string.Equals(Path.GetFullPath(program), Path.GetFullPath(adb.Path), StringComparison.OrdinalIgnoreCase);

    private static string VersionText(int version) => version == 0 ? "unknown" : version.ToString(CultureInfo.InvariantCulture);

    private static string Describe(AdbDevice device) =>
        device.Details.TryGetValue("model", out var model) ? $"{model.Replace('_', ' ')}, serial {device.Serial}" : $"serial {device.Serial}";

    private static string Or(string value, string missing) => value.Length > 0 ? value : missing;

    private static string ScreenText(PhoneDetails details)
    {
        if (details.ScreenWidth is null || details.ScreenHeight is null)
        {
            return "size not reported.";
        }

        var text = $"{details.ScreenWidth} by {details.ScreenHeight} pixels";
        return details.OverrideWidth is not null
            ? text + $", shown at {details.OverrideWidth} by {details.OverrideHeight}."
            : text + ".";
    }

    [GeneratedRegex(@"^(?<serial>\S+)\s+(?<state>device|unauthorized|offline|recovery|sideload|bootloader|rescue|authorizing|connecting|host|no permissions[^\s]*)(?<rest>.*)$")]
    private static partial Regex DeviceLine();

    [GeneratedRegex(@"(?<key>[A-Za-z_]+):(?<value>\S+)")]
    private static partial Regex DetailPair();

    [GeneratedRegex(@"^(?<kind>Physical|Override) size:\s*(?<w>\d+)x(?<h>\d+)")]
    private static partial Regex ScreenSize();

    [GeneratedRegex(@"Android Debug Bridge version (?<version>\d+\.\d+\.(?<server>\d+))")]
    private static partial Regex AdbVersionLine();

    [GeneratedRegex(@"(?m)^Version (?<tools>[0-9.]+)")]
    private static partial Regex ToolsVersionLine();
}

/// <summary>adb's version for the panel, and the server version it expects.</summary>
internal sealed record AdbClientVersion(string Text, int Server);
