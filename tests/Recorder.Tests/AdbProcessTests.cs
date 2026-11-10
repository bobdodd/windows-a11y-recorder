using System.Text;
using Recorder.Collectors.Android;

namespace Recorder.Tests;

public sealed class AdbProcessTests
{
    // A made-up TalkBack log line holding the text test 56 (2026-11-10)
    // showed as "12Â°C".
    private const string Line =
        "301230512.345678  1234  1234 V talkback: SpeechControllerImpl: Speaking fragment text=\"Partly cloudy day, 12\u00B0C\"";

    [Fact]
    public void StartInfo_ReadsOutputAndErrors_AsUtf8()
    {
        var info = AdbProcess.CreateStartInfo(
            Path.Combine("C:", "tools", "platform-tools", "adb.exe"),
            ["-s", "58191FDCQ00ABZ", "logcat"]);

        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(["-s", "58191FDCQ00ABZ", "logcat"], info.ArgumentList);
        Assert.Equal(Path.Combine("C:", "tools", "platform-tools"), info.WorkingDirectory);
        Assert.Equal(Encoding.UTF8.CodePage, info.StandardOutputEncoding?.CodePage);
        Assert.Equal(Encoding.UTF8.CodePage, info.StandardErrorEncoding?.CodePage);
    }

    [Fact]
    public void ALineFromThePhone_KeepsItsDegreeSign()
    {
        var bytes = Encoding.UTF8.GetBytes(Line);
        var info = AdbProcess.CreateStartInfo("adb.exe", []);

        using var reader = new StreamReader(new MemoryStream(bytes), info.StandardOutputEncoding!);
        Assert.Equal(Line, reader.ReadLine());

        // What test 56 showed: the same bytes read with a Western European
        // single-byte code page give "Â°".
        Assert.Contains("12\u00C2\u00B0C", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
    }
}
