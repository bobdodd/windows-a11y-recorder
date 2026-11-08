using System.IO.Compression;
using System.Text;
using Recorder.Collectors.Android;

namespace Recorder.Tests;

// Part 0 of docs/architecture/android-implementation-plan.md: installing
// Platform-Tools, and the setup window's account of each Android program.
public sealed class AndroidToolsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "android-tools-" + Guid.NewGuid().ToString("N"));

    public AndroidToolsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Install_UnpacksTheArchiveAndTheStatusSaysSo()
    {
        var archive = Archive(PlatformToolsEntries("37.0.1"));
        var data = Path.Combine(_folder, "data");

        PlatformToolsInstaller.Install(archive, AndroidTools.PlatformToolsFolder(data), "37.0.1");

        Assert.True(File.Exists(AndroidTools.Adb(data)));
        Assert.True(File.Exists(Path.Combine(AndroidTools.PlatformToolsFolder(data), "lib64", "libc++.so")));
        Assert.False(Directory.Exists(AndroidTools.PlatformToolsFolder(data) + ".new"));
        var state = AndroidToolsStatus.ReadPlatformTools(data);
        Assert.True(state.Ready);
        Assert.Equal($"Platform-Tools 37.0.1 installed in {AndroidTools.PlatformToolsFolder(data)}.", state.Text);
    }

    [Fact]
    public void Install_ReplacesAnEarlierInstall()
    {
        var data = Path.Combine(_folder, "data");
        var target = AndroidTools.PlatformToolsFolder(data);
        PlatformToolsInstaller.Install(Archive(PlatformToolsEntries("37.0.1", ("platform-tools/old.txt", "old"))), target, "37.0.1");

        PlatformToolsInstaller.Install(Archive(PlatformToolsEntries("37.0.1")), target, "37.0.1");

        Assert.False(File.Exists(Path.Combine(target, "old.txt")));
        Assert.True(File.Exists(Path.Combine(target, "adb.exe")));
        Assert.False(Directory.Exists(target + ".old"));
    }

    [Fact]
    public void Install_RefusesAnotherRevisionAndKeepsTheEarlierInstall()
    {
        var data = Path.Combine(_folder, "data");
        var target = AndroidTools.PlatformToolsFolder(data);
        PlatformToolsInstaller.Install(Archive(PlatformToolsEntries("37.0.1")), target, "37.0.1");

        var error = Assert.Throws<InvalidDataException>(() =>
            PlatformToolsInstaller.Install(Archive(PlatformToolsEntries("36.0.0")), target, "37.0.1"));

        Assert.Contains("revision 36.0.0, not 37.0.1", error.Message, StringComparison.Ordinal);
        Assert.Equal("37.0.1", PlatformToolsInstaller.ReadRevision(target));
        Assert.False(Directory.Exists(target + ".new"));
    }

    [Theory]
    [InlineData("other/adb.exe")]
    [InlineData("platform-tools/../escaped.txt")]
    public void Install_RefusesFilesOutsideThePlatformToolsFolder(string entry)
    {
        var archive = Archive(PlatformToolsEntries("37.0.1", (entry, "x")));
        var target = Path.Combine(_folder, "data", "platform-tools");

        Assert.Throws<InvalidDataException>(() => PlatformToolsInstaller.Install(archive, target, "37.0.1"));

        Assert.False(Directory.Exists(target));
        Assert.False(File.Exists(Path.Combine(_folder, "data", "escaped.txt")));
    }

    [Fact]
    public void Install_RefusesAnArchiveWithoutAdb()
    {
        var entries = PlatformToolsEntries("37.0.1").Where(entry => entry.Name != "platform-tools/adb.exe").ToArray();
        var target = Path.Combine(_folder, "data", "platform-tools");

        var error = Assert.Throws<InvalidDataException>(() => PlatformToolsInstaller.Install(Archive(entries), target, "37.0.1"));

        Assert.Contains("adb.exe", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Status_SaysWherePlatformToolsWillGoWhenItIsNotInstalled()
    {
        var data = Path.Combine(_folder, "data");

        var state = AndroidToolsStatus.ReadPlatformTools(data);

        Assert.False(state.Ready);
        Assert.Equal($"Platform-Tools is not installed. It will be installed in {AndroidTools.PlatformToolsFolder(data)}.", state.Text);
    }

    [Fact]
    public void Status_ReportsAMissingOrChangedScrcpyServer()
    {
        Assert.Contains("is missing", AndroidToolsStatus.ReadScrcpyServer(_folder).Text, StringComparison.Ordinal);

        Directory.CreateDirectory(AndroidTools.BundledFolder(_folder));
        File.WriteAllText(Path.Combine(AndroidTools.BundledFolder(_folder), AndroidTools.ScrcpyServer.Name), "not the server");
        var state = AndroidToolsStatus.ReadScrcpyServer(_folder);

        Assert.False(state.Ready);
        Assert.Contains("is not the 5.0.1 release", state.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_ChecksTheCompanionAppAgainstItsDescription()
    {
        var bundled = AndroidTools.BundledFolder(_folder);
        Assert.Contains("is missing", AndroidToolsStatus.ReadCompanion(_folder).Text, StringComparison.Ordinal);

        Directory.CreateDirectory(bundled);
        var apk = Path.Combine(bundled, AndroidTools.CompanionApkName);
        File.WriteAllText(apk, "an app");
        var info = Path.Combine(bundled, AndroidTools.CompanionInfoName);
        // Windows PowerShell 5.1's Set-Content -Encoding UTF8 writes a byte order mark.
        File.WriteAllText(info, "package x\nversionName 1.1.0\nversionCode 678\nsha256 0000\n", new UTF8Encoding(true));
        Assert.Contains("does not match", AndroidToolsStatus.ReadCompanion(_folder).Text, StringComparison.Ordinal);

        File.WriteAllText(info, $"package x\nversionName 1.1.0\nversionCode 678\nsha256 {AndroidToolsStatus.Sha256(apk)}\n", new UTF8Encoding(true));
        var state = AndroidToolsStatus.ReadCompanion(_folder);

        Assert.True(state.Ready);
        Assert.Equal($"The companion app 1.1.0, version code 678, for the phone's accessibility evidence, is in place and checked: {apk}.", state.Text);
    }

    private static (string Name, string Text)[] PlatformToolsEntries(string revision, params (string Name, string Text)[] extra) =>
    [
        ("platform-tools/", ""),
        ("platform-tools/adb.exe", "adb"),
        ("platform-tools/AdbWinApi.dll", "api"),
        ("platform-tools/AdbWinUsbApi.dll", "usb"),
        ("platform-tools/NOTICE.txt", "notice"),
        ("platform-tools/lib64/libc++.so", "lib"),
        ("platform-tools/source.properties", $"Pkg.UserSrc=false\nPkg.Revision={revision}\n"),
        .. extra
    ];

    private string Archive((string Name, string Text)[] entries)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            var entry = zip.CreateEntry(name);
            if (!name.EndsWith('/'))
            {
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
            }
        }

        return path;
    }
}
