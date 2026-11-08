namespace Recorder.Collectors.Android;

/// <summary>A file the recorder downloads or ships, fixed to its size and SHA-256.</summary>
public sealed record PinnedFile(string Name, Uri Url, long Size, string Sha256);

/// <summary>
/// The programs Android recording needs and where each is kept (part 0 of
/// docs/architecture/android-implementation-plan.md).
///
/// adb comes from Google's Android SDK Platform-Tools, which the SDK
/// licence does not allow the recorder to redistribute (terms 3.4), so the
/// Android setup window downloads it, after the person accepts the
/// licence, into their own data folder. The scrcpy server (Apache 2.0) and
/// the companion app are built into the install folder's android folder.
/// </summary>
public static class AndroidTools
{
    public const string PlatformToolsVersion = "37.0.1";

    /// <summary>
    /// Platform-Tools r37.0.1, the current stable release in Google's
    /// repository list (https://dl.google.com/android/repository/repository2-3.xml),
    /// which gives its size and SHA-1 (e03e78b1d80b396f1c3358e31251cb31740e1110).
    /// The SHA-256 was computed from the download on 2026-11-07.
    /// </summary>
    public static readonly PinnedFile PlatformToolsArchive = new(
        "platform-tools_r37.0.1-win.zip",
        new Uri("https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip"),
        8_044_989,
        "45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99");

    public const string SdkLicenceName = "Android Software Development Kit License Agreement";
    public static readonly Uri SdkLicenceAddress = new("https://developer.android.com/studio/terms");

    public const string ScrcpyVersion = "5.0.1";

    /// <summary>The scrcpy server, as listed in https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1.</summary>
    public static readonly PinnedFile ScrcpyServer = new(
        "scrcpy-server-v5.0.1",
        new Uri("https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/scrcpy-server-v5.0.1"),
        733_930,
        "764eb6f79811d5211fe9df341120882ba9994c7a61b897d7bf3fb662e53bc536");

    public const string CompanionPackage = "io.github.bobdodd.a11yrecorder.companion";
    public const string CompanionApkName = "a11yrecorder-companion.apk";

    /// <summary>Written by scripts/Build-CompanionApp.ps1 beside the app: its versions, SHA-256 and signing certificate.</summary>
    public const string CompanionInfoName = "companion-app.txt";

    /// <summary>The person's Android folder: %LOCALAPPDATA%\Windows A11y Recorder\android.</summary>
    public static string DataFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Windows A11y Recorder",
            "android");

    public static string PlatformToolsFolder(string dataFolder) => Path.Combine(dataFolder, "platform-tools");

    /// <summary>Where the setup window keeps downloads, so a stopped download continues.</summary>
    public static string DownloadsFolder(string dataFolder) => Path.Combine(dataFolder, "downloads");

    public static string Adb(string dataFolder) => Path.Combine(PlatformToolsFolder(dataFolder), "adb.exe");

    /// <summary>The install folder's android folder, with the scrcpy server and the companion app.</summary>
    public static string BundledFolder(string baseDirectory) => Path.Combine(baseDirectory, "android");
}
