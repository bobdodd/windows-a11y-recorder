using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.Collectors.Android;

/// <summary>The recorder's own copy of the companion app, in the install folder.</summary>
/// <param name="Problem">Why the copy cannot be installed, or null when it can.</param>
public sealed record BundledCompanion(string ApkPath, string VersionName, long? VersionCode, string? Problem)
{
    /// <summary>Reads and checks the copy in the install folder's android folder.</summary>
    public static BundledCompanion Read(string baseDirectory)
    {
        var folder = AndroidTools.BundledFolder(baseDirectory);
        var path = Path.Combine(folder, AndroidTools.CompanionApkName);
        var state = AndroidToolsStatus.ReadCompanion(baseDirectory);
        var info = AndroidToolsStatus.ReadInfo(Path.Combine(folder, AndroidTools.CompanionInfoName));
        long? code = info.TryGetValue("versionCode", out var text)
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        return new BundledCompanion(
            path,
            info.GetValueOrDefault("versionName", ""),
            code,
            state.Ready ? null : state.Text);
    }
}

/// <summary>What was done to set the companion app up, and how it ended.</summary>
public sealed record CompanionSetupResult(bool Succeeded, string Text);

/// <summary>
/// Installs the companion app on the phone over adb, and turns its
/// accessibility service on (android-implementation-plan.md, slice 2).
/// Turning it on adds it to the phone's enabled accessibility services and
/// leaves every service already on, such as TalkBack, as it was.
/// </summary>
public static partial class CompanionApp
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan QuickCommand = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The installed version code in the output of
    /// <c>pm list packages --show-versioncode</c>, which lists every package
    /// whose name contains the one asked for, as
    /// <c>package:NAME versionCode:N</c>. Null when the app is not listed.
    /// </summary>
    public static long? ParseInstalledVersionCode(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var match = PackageLine().Match(raw.Trim());
            if (match.Success && match.Groups["name"].Value == AndroidTools.CompanionPackage)
            {
                return long.Parse(match.Groups["code"].Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    /// <summary>The companion's service is on when it is among the enabled accessibility services.</summary>
    public static bool IsOn(string enabledServices) =>
        Services(enabledServices).Any(IsCompanion);

    /// <summary>The enabled services with the companion's added, and every other service kept, in order.</summary>
    public static string WithCompanion(string enabledServices)
    {
        var services = Services(enabledServices).Where(service => !IsCompanion(service)).ToList();
        services.Add(CompanionProtocol.ServiceComponent);
        return string.Join(':', services);
    }

    /// <summary>
    /// Installs the recorder's copy of the app, replacing an older one. A
    /// copy signed with another key cannot be replaced, so it is removed
    /// first; the app keeps no data of its own. Then turns the app's service
    /// on.
    /// </summary>
    public static async Task<CompanionSetupResult> InstallAndTurnOnAsync(
        IAdb adb,
        string serial,
        string apkPath,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);

        var install = await InstallAsync(adb, serial, apkPath, cancellation).ConfigureAwait(false);
        if (!install.Succeeded && install.Text.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.Ordinal))
        {
            var removed = await adb.RunAsync(
                ["-s", serial, "uninstall", AndroidTools.CompanionPackage],
                InstallTimeout,
                cancellation).ConfigureAwait(false);
            if (removed.ExitCode != 0)
            {
                return new CompanionSetupResult(
                    false,
                    $"The companion app on the phone is signed with another key, and could not be removed to install the recorder's: {removed.Text}");
            }

            install = await InstallAsync(adb, serial, apkPath, cancellation).ConfigureAwait(false);
        }

        if (!install.Succeeded)
        {
            return install;
        }

        var on = await TurnOnAsync(adb, serial, cancellation).ConfigureAwait(false);
        return on.Succeeded
            ? new CompanionSetupResult(true, "The companion app is installed and on.")
            : new CompanionSetupResult(false, "The companion app is installed, but could not be turned on. " + on.Text);
    }

    /// <summary>Adds the companion's service to the phone's enabled accessibility services.</summary>
    public static async Task<CompanionSetupResult> TurnOnAsync(IAdb adb, string serial, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var read = await adb.RunAsync(
            ["-s", serial, "shell", "settings get secure enabled_accessibility_services"],
            QuickCommand,
            cancellation).ConfigureAwait(false);
        if (read.ExitCode != 0)
        {
            return new CompanionSetupResult(false, $"The phone's accessibility services could not be read: {read.Text}");
        }

        var current = read.Output.Trim();
        if (current == "null")
        {
            current = "";
        }

        var value = WithCompanion(current);
        // The value goes to the phone's shell inside single quotes; service
        // names are package and class names, so any other character means
        // the setting holds something this should not write back.
        if (!ServiceList().IsMatch(value))
        {
            return new CompanionSetupResult(
                false,
                $"The phone's enabled accessibility services hold characters the recorder does not expect, so it left them alone: {current}. Turn the companion app on in the phone's Settings, Accessibility.");
        }

        var write = await adb.RunAsync(
            ["-s", serial, "shell", $"settings put secure enabled_accessibility_services '{value}' && settings put secure accessibility_enabled 1"],
            QuickCommand,
            cancellation).ConfigureAwait(false);
        if (write.ExitCode != 0)
        {
            return new CompanionSetupResult(false, $"The phone did not accept the setting: {write.Text}");
        }

        var check = await adb.RunAsync(
            ["-s", serial, "shell", "settings get secure enabled_accessibility_services"],
            QuickCommand,
            cancellation).ConfigureAwait(false);
        return check.ExitCode == 0 && IsOn(check.Output.Trim())
            ? new CompanionSetupResult(true, "The companion app is on.")
            : new CompanionSetupResult(
                false,
                "The phone did not keep the setting. Turn the companion app on in the phone's Settings, Accessibility.");
    }

    private static async Task<CompanionSetupResult> InstallAsync(
        IAdb adb,
        string serial,
        string apkPath,
        CancellationToken cancellation)
    {
        AdbResult result;
        try
        {
            result = await adb.RunAsync(["-s", serial, "install", "-r", apkPath], InstallTimeout, cancellation)
                .ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return new CompanionSetupResult(false, $"The companion app could not be installed: {ex.Message}");
        }

        // adb install prints "Success" on success, and "Failure [CODE: ...]"
        // or "adb: failed to install ..." otherwise.
        return result.ExitCode == 0 && result.Output.Contains("Success", StringComparison.Ordinal)
            ? new CompanionSetupResult(true, "The companion app is installed.")
            : new CompanionSetupResult(false, $"The companion app could not be installed: {result.Text}");
    }

    private static IEnumerable<string> Services(string enabledServices) =>
        enabledServices == "null"
            ? []
            : enabledServices.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsCompanion(string service) =>
        service.StartsWith(AndroidTools.CompanionPackage + "/", StringComparison.Ordinal);

    [GeneratedRegex(@"^package:(?<name>\S+)\s+versionCode:(?<code>\d+)")]
    private static partial Regex PackageLine();

    [GeneratedRegex(@"^[A-Za-z0-9._/$:]+$")]
    private static partial Regex ServiceList();
}
