using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The keyboard hook records (input.keyboard-hook): finding that the hook
/// was removed from generated sequences of hook calls and raw input keys,
/// the payload rules, and the player's summaries. See
/// docs/architecture/screen-reader-activity.md, "The keyboard hook".
/// </summary>
public sealed class KeyboardHookTests
{
    private const long Millisecond = 1_000_000;
    private const int H = 0x23;
    private const int Tab = 0x0F;

    private static HookLossDetector Detector() =>
        new(KeyboardHookRecords.JoinWindowMilliseconds * Millisecond);

    [Fact]
    public void AKeyPassedOnIsInBothRecordsAndIsNotALoss()
    {
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, H, up: false, injected: false);
        detector.ObserveRaw(102 * Millisecond, H, 0x48, up: false, fromDevice: true);
        detector.ObserveHook(180 * Millisecond, H, up: true, injected: false);
        detector.ObserveRaw(181 * Millisecond, H, 0x48, up: true, fromDevice: true);
        Assert.Null(detector.Poll(1000 * Millisecond));
    }

    [Fact]
    public void AKeyTheScreenReaderKeptIsOnlyInTheHookAndIsNotALoss()
    {
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, H, up: false, injected: false);
        detector.ObserveHook(180 * Millisecond, H, up: true, injected: false);
        Assert.Null(detector.Poll(1000 * Millisecond));
    }

    [Fact]
    public void AKeptTabReplacedByAnInjectedOneIsNotALoss()
    {
        // NVDA kept the physical Tab and injected its own, which raw input
        // records with no device.
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, Tab, up: false, injected: false);
        detector.ObserveHook(105 * Millisecond, Tab, up: false, injected: true);
        detector.ObserveRaw(106 * Millisecond, Tab, 0x09, up: false, fromDevice: false);
        Assert.Null(detector.Poll(1000 * Millisecond));
    }

    [Fact]
    public void ARawKeyFromAKeyboardWithNoHookCallIsALoss()
    {
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, H, up: false, injected: false);
        detector.ObserveRaw(101 * Millisecond, H, 0x48, up: false, fromDevice: true);
        detector.ObserveRaw(2000 * Millisecond, 0x1E, 0x41, up: false, fromDevice: true);

        // Not decided until the window has passed.
        Assert.Null(detector.Poll(2100 * Millisecond));

        var loss = detector.Poll(2400 * Millisecond);
        Assert.NotNull(loss);
        Assert.Equal(2000 * Millisecond, loss.UnmatchedRawKeyAt);
        Assert.Equal(0x1E, loss.UnmatchedScanCode);
        Assert.False(loss.UnmatchedUp);
        Assert.Equal(100 * Millisecond, loss.LastHookKeyAt);

        // The pending keys are dropped with it, so one loss is one record.
        Assert.Null(detector.Poll(5000 * Millisecond));
    }

    [Fact]
    public void TheDirectionMustMatch()
    {
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, H, up: false, injected: false);
        detector.ObserveRaw(150 * Millisecond, H, 0x48, up: true, fromDevice: true);
        Assert.NotNull(detector.Poll(1000 * Millisecond));
    }

    [Fact]
    public void ShiftIsMatchedByScanCodeThoughRawInputGivesNoSide()
    {
        // The hook gives VK_LSHIFT; raw input gives VK_SHIFT. Only the
        // scan code is compared.
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, 0x2A, up: false, injected: false);
        detector.ObserveRaw(100 * Millisecond, 0x2A, 0x10, up: false, fromDevice: true);
        Assert.Null(detector.Poll(1000 * Millisecond));
    }

    [Fact]
    public void AHookCallJustAfterTheRawKeyStillMatches()
    {
        var detector = Detector();
        detector.ObserveRaw(100 * Millisecond, H, 0x48, up: false, fromDevice: true);
        detector.ObserveHook(390 * Millisecond, H, up: false, injected: false);
        Assert.Null(detector.Poll(1000 * Millisecond));
    }

    [Fact]
    public void InjectedAndFakeRawKeysAreNotChecked()
    {
        var detector = Detector();
        detector.ObserveRaw(100 * Millisecond, H, 0x48, up: false, fromDevice: false);
        detector.ObserveRaw(110 * Millisecond, 0x2A, 0xFF, up: false, fromDevice: true);
        detector.ObserveRaw(120 * Millisecond, 0xFF, 0x48, up: false, fromDevice: true);
        detector.ObserveRaw(130 * Millisecond, 0, 0x48, up: false, fromDevice: true);
        Assert.Null(detector.Poll(1000 * Millisecond));
    }

    [Fact]
    public void AnInjectedHookCallDoesNotMatchAKeyFromAKeyboard()
    {
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, H, up: false, injected: true);
        detector.ObserveRaw(101 * Millisecond, H, 0x48, up: false, fromDevice: true);
        Assert.NotNull(detector.Poll(1000 * Millisecond));
    }

    [Fact]
    public void OldHookCallsAreDroppedAndCannotMatchALaterKey()
    {
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, H, up: false, injected: false);
        Assert.Null(detector.Poll(5000 * Millisecond));
        detector.ObserveRaw(5000 * Millisecond, H, 0x48, up: false, fromDevice: true);
        var loss = detector.Poll(6000 * Millisecond);
        Assert.NotNull(loss);
        Assert.Equal(100 * Millisecond, loss.LastHookKeyAt);
    }

    private static List<EventValidationIssue> Validate(string eventType, string json)
    {
        using var document = JsonDocument.Parse(json.Replace('\'', '"'));
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(KeyboardHookRecords.Channel, eventType, document.RootElement, 2000, issues);
        return issues;
    }

    private const string Key =
        "{'installation':1,'virtualKey':72,'scanCode':35,'flags':0,'up':false,'extended':false,'injected':false,'lowerIntegrityInjected':false,'altDown':false,'extraInformation':0,'eventTimeMilliseconds':5}";

    private const string Installed =
        "{'installation':2,'reason':'refresh','installed':true,'problem':null,'previousInstallation':1,'previousKeys':3,'previousMaxCallbackMicroseconds':12.5,'keysDropped':0,'lastHookKeyAt':null,'unmatchedRawKeyAt':null,'unmatchedScanCode':null}";

    [Fact]
    public void TheSampleRecordsAreValid()
    {
        Assert.Empty(Validate(KeyboardHookRecords.KeyEventType, Key));
        Assert.Empty(Validate(KeyboardHookRecords.InstalledEventType, Installed));
        Assert.Empty(Validate(
            KeyboardHookRecords.InstalledEventType,
            Installed.Replace("'refresh'", "'hook-lost'")
                .Replace("'unmatchedRawKeyAt':null", "'unmatchedRawKeyAt':1500")
                .Replace("'unmatchedScanCode':null", "'unmatchedScanCode':30")));
    }

    [Fact]
    public void AKeysFlagsMustMatchItsBooleans()
    {
        var issues = Validate(KeyboardHookRecords.KeyEventType, Key.Replace("'flags':0", "'flags':128"));
        Assert.Contains(issues, issue => issue.Code == "hook-keyboard-flag-inconsistent" && issue.Path == "#/payload/up");

        issues = Validate(
            KeyboardHookRecords.KeyEventType,
            Key.Replace("'flags':0", "'flags':16").Replace("'injected':false", "'injected':true"));
        Assert.Empty(issues);
    }

    [Fact]
    public void AnInstallationHasAProblemExactlyWhenItFailed()
    {
        Assert.Contains(
            Validate(KeyboardHookRecords.InstalledEventType, Installed.Replace("'installed':true", "'installed':false")),
            issue => issue.Code == "hook-installed-problem-inconsistent");
        Assert.Contains(
            Validate(KeyboardHookRecords.InstalledEventType, Installed.Replace("'problem':null", "'problem':'x'")),
            issue => issue.Code == "hook-installed-problem-inconsistent");
    }

    [Fact]
    public void OnlyAnInstallationAfterALossNamesTheRawKey()
    {
        Assert.Contains(
            Validate(KeyboardHookRecords.InstalledEventType, Installed.Replace("'refresh'", "'hook-lost'")),
            issue => issue.Code == "hook-installed-loss-inconsistent");
        Assert.Contains(
            Validate(KeyboardHookRecords.InstalledEventType, Installed.Replace("'unmatchedScanCode':null", "'unmatchedScanCode':30")),
            issue => issue.Code == "hook-installed-loss-inconsistent");
        Assert.NotEmpty(Validate(KeyboardHookRecords.InstalledEventType, Installed.Replace("'refresh'", "'later'")));
    }

    [Fact]
    public void ThePlayerNamesTheKeyAndTheInstallation()
    {
        string Summary(string eventType, string json)
        {
            using var document = JsonDocument.Parse(json.Replace('\'', '"'));
            return SessionPlaybackArchiveBuilder.CreateSummary(KeyboardHookRecords.Channel, eventType, document.RootElement);
        }

        Assert.Equal("hook-keyboard: H down", Summary(KeyboardHookRecords.KeyEventType, Key));
        Assert.Equal(
            "hook-keyboard: Tab up, injected",
            Summary(
                KeyboardHookRecords.KeyEventType,
                Key.Replace("'virtualKey':72", "'virtualKey':9").Replace("'up':false", "'up':true").Replace("'injected':false", "'injected':true")));
        Assert.Equal("hook-installed: refresh, installation 2", Summary(KeyboardHookRecords.InstalledEventType, Installed));
    }

    [Theory]
    [InlineData(0x48, "H")]
    [InlineData(0x37, "7")]
    [InlineData(0x09, "Tab")]
    [InlineData(0x2D, "Insert")]
    [InlineData(0x61, "Numpad 1")]
    [InlineData(0x76, "F7")]
    [InlineData(0xA0, "Left Shift")]
    [InlineData(0xDC, "\\")]
    [InlineData(0xE7, "key 0xE7")]
    public void KeysHaveTheirNames(int virtualKey, string name) =>
        Assert.Equal(name, KeyboardHookRecords.KeyName(virtualKey));
}
