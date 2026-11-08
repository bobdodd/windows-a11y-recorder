using System.Security.Cryptography;

namespace Recorder.Collectors.Android;

/// <summary>What the Android setup window shows about one program.</summary>
/// <param name="Ready">True when the program is there and is the one the recorder expects.</param>
/// <param name="Text">A sentence saying what was found, with its path.</param>
public sealed record AndroidToolState(bool Ready, string Text);

/// <summary>
/// Whether each program Android recording needs is in place: adb in the
/// person's folder, and the scrcpy server and companion app in the install
/// folder, each checked against what the recorder expects.
/// </summary>
public sealed record AndroidToolsStatus(AndroidToolState PlatformTools, AndroidToolState ScrcpyServer, AndroidToolState Companion)
{
    public static AndroidToolsStatus Read(string dataFolder, string baseDirectory) =>
        new(ReadPlatformTools(dataFolder), ReadScrcpyServer(baseDirectory), ReadCompanion(baseDirectory));

    public static AndroidToolState ReadPlatformTools(string dataFolder)
    {
        var folder = AndroidTools.PlatformToolsFolder(dataFolder);
        var adb = AndroidTools.Adb(dataFolder);
        if (!File.Exists(adb))
        {
            return new(false, $"Platform-Tools is not installed. It will be installed in {folder}.");
        }

        var revision = PlatformToolsInstaller.ReadRevision(folder);
        return revision == AndroidTools.PlatformToolsVersion
            ? new(true, $"Platform-Tools {revision} installed in {folder}.")
            : new(false, $"Platform-Tools {revision ?? "of unknown version"} is in {folder}; the recorder needs {AndroidTools.PlatformToolsVersion}. Install it again.");
    }

    public static AndroidToolState ReadScrcpyServer(string baseDirectory)
    {
        var path = Path.Combine(AndroidTools.BundledFolder(baseDirectory), AndroidTools.ScrcpyServer.Name);
        if (!File.Exists(path))
        {
            return new(false, $"The scrcpy server {AndroidTools.ScrcpyVersion}, for the phone's screen and sound, is missing: {path}. Install the recorder again.");
        }

        return Sha256(path) == AndroidTools.ScrcpyServer.Sha256
            ? new(true, $"The scrcpy server {AndroidTools.ScrcpyVersion}, for the phone's screen and sound, is in place and checked: {path}.")
            : new(false, $"The scrcpy server at {path} is not the {AndroidTools.ScrcpyVersion} release the recorder was built for. Install the recorder again.");
    }

    public static AndroidToolState ReadCompanion(string baseDirectory)
    {
        var folder = AndroidTools.BundledFolder(baseDirectory);
        var path = Path.Combine(folder, AndroidTools.CompanionApkName);
        if (!File.Exists(path))
        {
            return new(false, $"The companion app, for the phone's accessibility evidence, is missing: {path}. Install the recorder again.");
        }

        var info = ReadInfo(Path.Combine(folder, AndroidTools.CompanionInfoName));
        if (!info.TryGetValue("sha256", out var expected) || expected != Sha256(path))
        {
            return new(false, $"The companion app at {path} does not match its description in {AndroidTools.CompanionInfoName}. Install the recorder again.");
        }

        var version = info.GetValueOrDefault("versionName", "of unknown version");
        var code = info.GetValueOrDefault("versionCode", "unknown");
        return new(true, $"The companion app {version}, version code {code}, for the phone's accessibility evidence, is in place and checked: {path}.");
    }

    /// <summary>The "key value" lines of companion-app.txt.</summary>
    public static IReadOnlyDictionary<string, string> ReadInfo(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim().TrimStart('\uFEFF');
            var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0)
            {
                values[trimmed[..space]] = trimmed[(space + 1)..].Trim();
            }
        }

        return values;
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
