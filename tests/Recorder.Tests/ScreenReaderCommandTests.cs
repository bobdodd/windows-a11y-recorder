using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// NVDA's settings as read from its configuration folder, and the commands
/// of kept keys worked out from them and the command data (key outcome rule
/// 2). See docs/architecture/screen-reader-activity.md, "2c-1, the commands".
/// </summary>
public sealed class ScreenReaderCommandTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json.Replace('\'', '"'));
        return document.RootElement.Clone();
    }

    private static int _id;

    private static KeyEvidenceRecord Hook(long time, int virtualKey, bool up, bool extended = false) =>
        new($"hook-{++_id}", time, KeyboardHookRecords.Channel, KeyboardHookRecords.KeyEventType,
            J($"{{'installation':1,'virtualKey':{virtualKey},'scanCode':{virtualKey},'flags':0,'up':{(up ? "true" : "false")},'extended':{(extended ? "true" : "false")}," +
              "'injected':false,'lowerIntegrityInjected':false,'altDown':false,'extraInformation':0,'eventTimeMilliseconds':1}"));

    private static KeyEvidenceRecord Raw(long time, int virtualKey, bool up) =>
        new($"raw-{++_id}", time, "input.keyboard", "raw-keyboard",
            J($"{{'deviceHandle':65601,'makeCode':{virtualKey},'flags':{(up ? 1 : 0)},'virtualKey':{virtualKey},'message':{(up ? 257 : 256)},'extraInformation':0}}"));

    private static AssistiveTechnologyTimeline Nvda(string? settings = null, string version = "2026.2.0.54321")
    {
        var records = new List<AssistiveTechnologyRecord>
        {
            new(0, AssistiveTechnologyRecords.ProcessStartedEventType,
                J($"{{'product':'NVDA','role':'screen-reader','processId':6688,'runningAtStart':true,'productVersion':'{version}'}}"))
        };
        if (settings is not null)
        {
            records.Add(new(Millisecond, NvdaSettings.SettingsEventType, J(settings)));
        }

        return new AssistiveTechnologyTimeline(records);
    }

    private const string TargetSettings =
        "{'product':'NVDA','processId':6688,'copy':'portable','configFolder':'C:\\\\NVDA\\\\userConfig','read':true,'problem':null," +
        "'keyboardLayout':null,'nvdaModifierKeys':'7','multiPressTimeout':null,'autoPassThroughOnFocusChange':null," +
        "'autoPassThroughOnCaretMove':null,'trapNonCommandGestures':null,'enableOnPageLoad':null,'profiles':[],'profileTriggers':false," +
        "'customGestures':[],'gesturesProblem':null}";

    // A key pressed with others held, all kept (no raw input).
    private static (KeyDispositions Keys, KeyEvidenceRecord Down) Press(AssistiveTechnologyTimeline nvda, int key, params (int VirtualKey, bool Extended)[] held)
    {
        var records = new List<KeyEvidenceRecord>();
        var time = 10 * Second;
        foreach (var (virtualKey, extended) in held)
        {
            records.Add(Hook(time, virtualKey, up: false, extended));
            time += 10 * Millisecond;
        }

        var down = Hook(time, key, up: false);
        records.Add(down);
        records.Add(Hook(time + 80 * Millisecond, key, up: true));
        foreach (var (virtualKey, extended) in held)
        {
            records.Add(Hook(time + 100 * Millisecond, virtualKey, up: true, extended));
        }

        return (new KeyDispositions(records, nvda), down);
    }

    [Fact]
    public void TheCommandDataIsInThePlayerWithItsSource()
    {
        var data = Assert.Single(ScreenReaderCommandData.All);
        Assert.Equal(("NVDA", "2026.2"), (data.Product, data.Version));
        Assert.Equal("elements list", Assert.Single(data.Of("NVDA+f7", "desktop")).Name);
        Assert.Equal("next landmark", Assert.Single(data.Of("d", "desktop")).Name);
        Assert.Equal("previous heading", Assert.Single(data.Of("shift+h", "laptop")).Name);
        Assert.Equal("toggle application sleep mode", Assert.Single(data.Of("NVDA+shift+z", "laptop")).Name);
        Assert.Empty(data.Of("NVDA+shift+z", "desktop"));
        Assert.Equal("report shortcut key", Assert.Single(data.Of("control+shift+NVDA+period", "laptop")).Name);
        Assert.Equal(
            "NVDA 2026.2 commands quick reference, Browse Mode, https://download.nvaccess.org/releases/2026.2/documentation/keyCommands.html#browse-mode",
            data.SourceOf(data.Of("NVDA+f7", "desktop")[0]));
    }

    [Fact]
    public void AVersionWithoutItsOwnDataUsesTheNearestEarlier()
    {
        Assert.Null(ScreenReaderCommandData.For("NVDA", "2026.2.0.1")!.Value.Note);
        var later = ScreenReaderCommandData.For("NVDA", "2026.4.1")!.Value;
        Assert.Equal("2026.2", later.Data.Version);
        Assert.Equal("no command data for NVDA 2026.4.1; using the nearest earlier version's, 2026.2", later.Note);
        Assert.Contains("or earlier", ScreenReaderCommandData.For("NVDA", "2025.3")!.Value.Note, StringComparison.Ordinal);
        Assert.Contains("not known", ScreenReaderCommandData.For("NVDA", null)!.Value.Note, StringComparison.Ordinal);
        Assert.Null(ScreenReaderCommandData.For("JAWS", "2026"));
    }

    [Fact]
    public void GesturesAreComparedInOneForm()
    {
        Assert.Equal("nvda+control+shift+period", ScreenReaderGestures.Canonical("kb:shift+NVDA+Control+."));
        Assert.Equal("nvda+f7", ScreenReaderGestures.Canonical("kb(desktop):NVDA+F7"));
        Assert.Equal("upArrow", ScreenReaderGestures.KeyName(0x26, extended: true));
        Assert.Equal("numpad8", ScreenReaderGestures.KeyName(0x26, extended: false));
        Assert.True(ScreenReaderGestures.IsNvdaKey(0x14, false, 7));
        Assert.False(ScreenReaderGestures.IsNvdaKey(0x14, false, 6));
        Assert.True(ScreenReaderGestures.IsNvdaKey(0x2D, true, 4));
        Assert.False(ScreenReaderGestures.IsNvdaKey(0x2D, false, 4));
    }

    [Fact]
    public void InsertF7IsTheElementsList()
    {
        // Recording 20261010-211432 at 92.8 s: Insert held, F7 kept.
        var (keys, down) = Press(Nvda(TargetSettings), 0x76, (0x2D, true));
        var outcome = keys.Of(down.EventId)!;
        Assert.Equal("kept, screen reader running: NVDA; command: elements list in browse mode (NVDA+f7), inferred", outcome.Text);
        Assert.Contains(outcome.Details, line => line.EndsWith("keyCommands.html#browse-mode", StringComparison.Ordinal));
        Assert.Contains("keyboard layout: desktop (NVDA's default)", outcome.Details);
        Assert.Contains("NVDA keys: Caps Lock, Numpad Insert, Insert (NVDAModifierKeys 7 in nvda.ini)", outcome.Details);
    }

    [Fact]
    public void CapsLockIsAnNvdaKeyOnlyWhereTheSettingsSaySo()
    {
        var (withCapsLock, down) = Press(Nvda(TargetSettings), 0x54, (0x14, false));
        Assert.EndsWith("command: report title (NVDA+t), inferred", withCapsLock.Of(down.EventId)!.Text, StringComparison.Ordinal);

        // Without settings, NVDA's default: Insert and Numpad Insert only.
        var (byDefault, plain) = Press(Nvda(), 0x54, (0x14, false));
        Assert.DoesNotContain("report title", byDefault.Of(plain.EventId)!.Text, StringComparison.Ordinal);
        Assert.Contains("NVDA's settings were not recorded; its defaults are assumed", byDefault.Of(plain.EventId)!.Details);
    }

    [Fact]
    public void TheLaptopLayoutHasItsOwnKeys()
    {
        var laptop = TargetSettings.Replace("'keyboardLayout':null", "'keyboardLayout':'laptop'", StringComparison.Ordinal);
        var (keys, down) = Press(Nvda(laptop), 0x5A, (0x2D, true), (0xA0, false));
        Assert.EndsWith("command: toggle application sleep mode (NVDA+shift+z), inferred", keys.Of(down.EventId)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACustomCommandComesFirst()
    {
        var custom = TargetSettings.Replace(
            "'customGestures':[]",
            "'customGestures':[{'section':'globalCommands.GlobalCommands','script':'reportCurrentFocus','gesture':'kb:NVDA+f7'}]",
            StringComparison.Ordinal);
        var (keys, down) = Press(Nvda(custom), 0x76, (0x2D, true));
        Assert.EndsWith("custom command: reportCurrentFocus (NVDA+f7), inferred", keys.Of(down.EventId)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameCommandAgainWithinTheTimeoutIsPressedTwice()
    {
        var records = new List<KeyEvidenceRecord>
        {
            Hook(10 * Second, 0x2D, up: false, extended: true),
            Hook(10 * Second + 50 * Millisecond, 0x54, up: false),
            Hook(10 * Second + 100 * Millisecond, 0x54, up: true),
            Hook(10 * Second + 300 * Millisecond, 0x54, up: false),
            Hook(10 * Second + 350 * Millisecond, 0x54, up: true),
            Hook(10 * Second + 400 * Millisecond, 0x2D, up: true, extended: true)
        };
        var keys = new KeyDispositions(records, Nvda(TargetSettings));
        Assert.EndsWith("report title (NVDA+t), inferred", keys.Of(records[1].EventId)!.Text, StringComparison.Ordinal);
        Assert.EndsWith("report title (NVDA+t), pressed twice, inferred", keys.Of(records[3].EventId)!.Text, StringComparison.Ordinal);
        Assert.EndsWith("; NVDA key", keys.Of(records[0].EventId)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALoneCtrlThatPassedIsStopSpeech()
    {
        var down = Hook(10 * Second, 0xA2, up: false);
        var rawDown = Raw(10 * Second + Millisecond, 0xA2, up: false);
        var up = Hook(10 * Second + 90 * Millisecond, 0xA2, up: true);
        var rawUp = Raw(10 * Second + 91 * Millisecond, 0xA2, up: true);
        var keys = new KeyDispositions([down, rawDown, up, rawUp], Nvda(TargetSettings));
        Assert.Equal("passed; command: stop speech (control), inferred", keys.Of(down.EventId)!.Text);
        Assert.Equal(keys.Of(down.EventId)!.Text, keys.Of(rawDown.EventId)!.Text);
        Assert.Contains(keys.Of(down.EventId)!.Details, line => line.Contains("which NVDA acts on and passes on", StringComparison.Ordinal));
    }

    [Fact]
    public void ALetterThatPassedHasNoCommandAndAKeptKeyWithoutOneStaysKept()
    {
        var down = Hook(10 * Second, 0x48, up: false);
        var rawDown = Raw(10 * Second + Millisecond, 0x48, up: false);
        Assert.Equal("passed", new KeyDispositions([down, rawDown], Nvda(TargetSettings)).Of(down.EventId)!.Text);

        var (keys, z) = Press(Nvda(TargetSettings), 0x5A);
        Assert.Equal("kept, screen reader running: NVDA", keys.Of(z.EventId)!.Text);
        Assert.Contains("no command of z in the command data (NVDA 2026.2)", keys.Of(z.EventId)!.Details);
    }

    [Fact]
    public void TheSettingsAreReadWithoutChangeFromTheConfigurationFolder()
    {
        var folder = Directory.CreateTempSubdirectory("nvda-config-").FullName;
        try
        {
            var ini = "[general]\n\tlanguage = Windows\n[keyboard]\n\tNVDAModifierKeys = 7\n\tkeyboardLayout = laptop\n" +
                "[[nested]]\n\tkeyboardLayout = desktop\n[virtualBuffers]\n\ttrapNonCommandGestures = False\n";
            File.WriteAllText(Path.Combine(folder, "nvda.ini"), ini);
            File.WriteAllText(Path.Combine(folder, "gestures.ini"),
                "[globalCommands.GlobalCommands]\n\treportCurrentFocus = kb:NVDA+shift+tab, kb:NVDA+f12\n\tNone = kb:NVDA+t\n");
            Directory.CreateDirectory(Path.Combine(folder, "profiles"));
            File.WriteAllText(Path.Combine(folder, "profiles", "Reading.ini"), "[speech]\n");
            var before = File.ReadAllText(Path.Combine(folder, "nvda.ini"));

            var reading = NvdaSettings.Read(folder);
            Assert.True(reading.Read);
            Assert.Null(reading.Problem);
            Assert.Equal("7", reading.NvdaModifierKeys);
            Assert.Equal("laptop", reading.KeyboardLayout);
            Assert.Equal("False", reading.TrapNonCommandGestures);
            Assert.Null(reading.MultiPressTimeout);
            Assert.Equal(["Reading"], reading.Profiles);
            Assert.False(reading.ProfileTriggers);
            Assert.Equal(3, reading.CustomGestures.Count);
            Assert.Contains(new NvdaCustomGesture("globalCommands.GlobalCommands", "None", "kb:NVDA+t"), reading.CustomGestures);
            Assert.Equal(before, File.ReadAllText(Path.Combine(folder, "nvda.ini")));

            var missing = NvdaSettings.Read(Path.Combine(folder, "absent"));
            Assert.False(missing.Read);
            Assert.NotNull(missing.Problem);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheConfigurationFolderIsTheCopysOwn()
    {
        Assert.Equal(
            Path.Combine("C:/NVDA", "userConfig"),
            NvdaSettings.ConfigFolder("C:/NVDA/nvda.exe", "portable", "C:/Users/User/AppData/Roaming"));
        Assert.Equal(
            Path.Combine("C:/Users/User/AppData/Roaming", "nvda"),
            NvdaSettings.ConfigFolder("C:/Program Files/NVDA/nvda.exe", "installed", "C:/Users/User/AppData/Roaming"));
        Assert.Null(NvdaSettings.ConfigFolder("x", "unknown", "y"));
        Assert.Equal(6, NvdaSettings.ModifierKeys(null));
        Assert.Null(NvdaSettings.ModifierKeys("9"));
    }
}
