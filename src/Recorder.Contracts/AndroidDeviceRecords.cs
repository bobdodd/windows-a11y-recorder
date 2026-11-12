namespace Recorder.Contracts;

/// <summary>
/// The records of the phone device collector, step 2 of
/// docs/architecture/android-checks.md: the display density at the start
/// and the stop, and the version of each app seen, read at the stop.
///
/// The density is read with <c>wm density</c>, which prints "Physical
/// density: N" and, when the density in use differs from it, "Override
/// density: N" (WindowManagerShellCommand.printInitialDisplayDensity,
/// AOSP main, read on 2026-11-12). The phone's "Display size" setting is
/// that override: for the built-in display, Android writes it to
/// <c>display_density_forced</c>, empty when it is the physical density
/// (DisplayWindowSettings.setForcedDensity), a setting the settings
/// collector already records with its changes. So the density in use at a
/// moment is the latest <c>display_density_forced</c> when it is a number,
/// and otherwise the density <c>wm density</c> gave.
///
/// Each app's version is read with <c>dumpsys package</c>, which prints, in
/// its "Packages:" section, "versionCode=N minSdk=N targetSdk=N" and
/// "versionName=..." (com.android.server.pm.Settings.dumpPackageLPr, AOSP
/// main, read on 2026-11-12). The output is not documented as an
/// interface, so the record keeps the lines it read beside the values.
/// </summary>
public static class AndroidDeviceRecords
{
    public const string Channel = "device.android";

    /// <summary>What <c>wm density</c> printed, at the start or the stop.</summary>
    public const string DensityEventType = "display-density";

    /// <summary>The version of each app seen in the recording, read at the stop.</summary>
    public const string AppVersionsEventType = "app-versions";

    /// <summary>Why the density or the versions are missing from a recording.</summary>
    public const string UnavailableEventType = "device-unavailable";

    public const string StartReason = "start";

    public const string StopReason = "stop";

    /// <summary>The density Android uses as 1 dp to 1 pixel (DisplayMetrics.DENSITY_DEFAULT).</summary>
    public const int BaselineDensity = 160;
}

/// <summary>What <c>wm density</c> printed at the start or the stop.</summary>
/// <param name="Reason"><see cref="AndroidDeviceRecords.StartReason"/> or <see cref="AndroidDeviceRecords.StopReason"/>.</param>
/// <param name="PhysicalDpi">The "Physical density" line's number, or null when it was not printed.</param>
/// <param name="OverrideDpi">The "Override density" line's number, or null when it was not printed: the physical density is in use.</param>
/// <param name="Lines">The lines printed, as they were.</param>
/// <param name="Problem">Why the density could not be read, one sentence; null when it was.</param>
/// <param name="ReadStartedAtNanoseconds">The session time the read started.</param>
/// <param name="ReadEndedAtNanoseconds">The session time the read ended.</param>
public sealed record AndroidDisplayDensity(
    string Serial,
    string Reason,
    int? PhysicalDpi,
    int? OverrideDpi,
    IReadOnlyList<string> Lines,
    string? Problem,
    long ReadStartedAtNanoseconds,
    long ReadEndedAtNanoseconds)
{
    /// <summary>The density in use: the override, else the physical density; null when neither was read.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? InUseDpi => OverrideDpi ?? PhysicalDpi;
}

/// <summary>The version of each app seen in the recording, read at the stop.</summary>
/// <param name="Apps">One for each package, in the order first seen.</param>
/// <param name="Problems">Why the versions could not be read, one sentence each.</param>
/// <param name="ReadStartedAtNanoseconds">The session time the read started.</param>
/// <param name="ReadEndedAtNanoseconds">The session time the read ended.</param>
public sealed record AndroidAppVersions(
    string Serial,
    IReadOnlyList<AndroidAppVersion> Apps,
    IReadOnlyList<string> Problems,
    long ReadStartedAtNanoseconds,
    long ReadEndedAtNanoseconds);

/// <summary>
/// One package's version, from the first package of that name in the
/// "Packages:" section of <c>dumpsys package</c>: the installed one, not a
/// hidden system copy it updates.
/// </summary>
/// <param name="Package">The package name, as seen in the accessibility tree.</param>
/// <param name="Found">Whether the package was listed; false when it was not installed at the stop.</param>
/// <param name="VersionName">The versionName line, or null.</param>
/// <param name="VersionCode">The versionCode, or null.</param>
/// <param name="MinSdk">The minSdk, or null.</param>
/// <param name="TargetSdk">The targetSdk, or null.</param>
/// <param name="LastUpdateTime">The lastUpdateTime line as printed, in the phone's local time, or null.</param>
/// <param name="Installer">The installerPackageName line, or null; "null" when it was installed over adb.</param>
/// <param name="Lines">The lines read for this package, trimmed, as they were.</param>
public sealed record AndroidAppVersion(
    string Package,
    bool Found,
    string? VersionName,
    long? VersionCode,
    int? MinSdk,
    int? TargetSdk,
    string? LastUpdateTime,
    string? Installer,
    IReadOnlyList<string> Lines);

/// <summary>Why the density or the versions are missing from a recording.</summary>
public sealed record AndroidDeviceUnavailable(string Serial, string Reason);
