using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The key outcomes derived at playback (rules 1 and 2), from the patterns of the
/// recordings of 2026-10-10. See docs/architecture/screen-reader-activity.md,
/// "Key disposition".
/// </summary>
public sealed class KeyDispositionTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;
    private const int Tab = 0x09;
    private const int TabScan = 15;
    private const int H = 0x48;
    private const int HScan = 35;

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json.Replace('\'', '"'));
        return document.RootElement.Clone();
    }

    private static int _id;

    private static KeyEvidenceRecord Hook(long time, int virtualKey, int scan, bool up, bool injected = false) =>
        new($"hook-{++_id}", time, KeyboardHookRecords.Channel, KeyboardHookRecords.KeyEventType,
            J($"{{'installation':1,'virtualKey':{virtualKey},'scanCode':{scan},'flags':0,'up':{(up ? "true" : "false")},'extended':false," +
              $"'injected':{(injected ? "true" : "false")},'lowerIntegrityInjected':false,'altDown':false,'extraInformation':0,'eventTimeMilliseconds':1}}"));

    private static KeyEvidenceRecord Raw(long time, int virtualKey, int scan, bool up, bool injected = false) =>
        new($"raw-{++_id}", time, "input.keyboard", "raw-keyboard",
            J($"{{'deviceHandle':{(injected ? 0 : 65601)},'makeCode':{scan},'flags':{(up ? 1 : 0)},'virtualKey':{virtualKey},'message':{(up ? 257 : 256)},'extraInformation':0}}"));

    private static KeyEvidenceRecord Page(long time, bool trusted = true) =>
        new($"page-{++_id}", time, KeyDispositions.DispatchChannel, KeyDispositions.DispatchStartedEventType,
            J($"{{'eventName':'keydown','trusted':{(trusted ? "true" : "false")}}}"));

    private static KeyEvidenceRecord Installed(long time, string reason, bool installed = true) =>
        new($"install-{++_id}", time, KeyboardHookRecords.Channel, KeyboardHookRecords.InstalledEventType,
            J($"{{'installation':1,'reason':'{reason}','installed':{(installed ? "true" : "false")}}}"));

    private static AssistiveTechnologyTimeline NvdaFrom(long time) => new(
    [
        new AssistiveTechnologyRecord(time, AssistiveTechnologyRecords.ProcessStartedEventType,
            J("{'product':'NVDA','role':'screen-reader','processId':6688,'runningAtStart':false}"))
    ]);

    [Fact]
    public void AKeyBothRecordedPassedAndThePageReceivedIt()
    {
        var down = Hook(Second, H, HScan, up: false);
        var rawDown = Raw(Second + 2 * Millisecond, H, HScan, up: false);
        var page = Page(Second + 4 * Millisecond);
        var up = Hook(Second + 100 * Millisecond, H, HScan, up: true);
        var rawUp = Raw(Second + 101 * Millisecond, H, HScan, up: true);
        var keys = new KeyDispositions([Installed(0, "recording-started"), down, rawDown, page, up, rawUp]);

        Assert.Equal(KeyOutcomeKind.Passed, keys.Of(down.EventId)!.Kind);
        Assert.Equal("passed, received by the page", keys.Of(down.EventId)!.Text);
        Assert.Equal("passed, received by the page", keys.Of(rawDown.EventId)!.Text);
        Assert.Equal("passed", keys.Of(up.EventId)!.Text);
        Assert.Equal(KeyOutcomeKind.PageKeyDown, keys.Of(page.EventId)!.Kind);
        Assert.StartsWith("page keydown for H down at", keys.Of(page.EventId)!.Text, StringComparison.Ordinal);
        Assert.Contains(keys.Of(down.EventId)!.Details, line => line.Contains("2.0 ms after the hook", StringComparison.Ordinal));
        Assert.Equal("key outcome rule 2", keys.Rule);
    }

    [Fact]
    public void AKeyOnlyTheHookRecordedWasKeptAndAttributedToARunningScreenReader()
    {
        var down = Hook(10 * Second, H, HScan, up: false);
        var running = new KeyDispositions([down], NvdaFrom(5 * Second));
        var outcome = running.Of(down.EventId)!;
        Assert.Equal(KeyOutcomeKind.Kept, outcome.Kind);
        Assert.Equal("kept, screen reader running: NVDA; command: next heading in browse mode (h), inferred", outcome.Text);
        Assert.Contains(outcome.Details, line => line.StartsWith("inferred:", StringComparison.Ordinal));

        Assert.Equal("kept, no screen reader running", new KeyDispositions([down], NvdaFrom(20 * Second)).Of(down.EventId)!.Text);
    }

    [Fact]
    public void NvdasOwnTabFollowsTheTabItKept()
    {
        // Recording 20261010-211432 at 33.494 s.
        var kept = Hook(33_494 * Millisecond, Tab, TabScan, up: false);
        var injectedDown = Hook(33_496 * Millisecond, Tab, TabScan, up: false, injected: true);
        var rawInjectedDown = Raw(33_496 * Millisecond + 500_000, Tab, TabScan, up: false, injected: true);
        var injectedUp = Hook(33_496 * Millisecond + 700_000, Tab, TabScan, up: true, injected: true);
        var page = Page(33_499 * Millisecond);
        var keptUp = Hook(33_590 * Millisecond, Tab, TabScan, up: true);
        var keys = new KeyDispositions([kept, injectedDown, rawInjectedDown, injectedUp, page, keptUp], NvdaFrom(Second));

        Assert.Equal(KeyOutcomeKind.Kept, keys.Of(kept.EventId)!.Kind);
        Assert.Equal(
            "kept, screen reader running: NVDA; followed by an injected Tab, received by the page",
            keys.Of(kept.EventId)!.Text);
        Assert.Contains(
            keys.Of(kept.EventId)!.Details,
            line => line.StartsWith("followed by: hook record: Tab down at 00:00:33.496, injected, 2.0 ms later, received by the page", StringComparison.Ordinal));
        Assert.Contains(keys.Of(kept.EventId)!.Details, line => line.StartsWith("followed by: hook record: Tab up", StringComparison.Ordinal));
        Assert.Equal("kept, screen reader running: NVDA", keys.Of(keptUp.EventId)!.Text);
        var injected = keys.Of(injectedDown.EventId)!;
        Assert.Equal(KeyOutcomeKind.Injected, injected.Kind);
        Assert.StartsWith("injected after the kept Tab down at", injected.Text, StringComparison.Ordinal);
        Assert.EndsWith(", received by the page", injected.Text, StringComparison.Ordinal);
        Assert.Equal(injected.Text, keys.Of(rawInjectedDown.EventId)!.Text);
        Assert.Equal(KeyOutcomeKind.Injected, keys.Of(injectedUp.EventId)!.Kind);
        Assert.StartsWith("page keydown for Tab down", keys.Of(page.EventId)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInjectedKeyIsNotJoinedToAKeyFromTheKeyboard()
    {
        var hook = Hook(Second, Tab, TabScan, up: false);
        var raw = Raw(Second + Millisecond, Tab, TabScan, up: false, injected: true);
        var keys = new KeyDispositions([Installed(0, "recording-started"), hook, raw]);
        Assert.Equal(KeyOutcomeKind.Kept, keys.Of(hook.EventId)!.Kind);
        Assert.Equal("injected, raw input only", keys.Of(raw.EventId)!.Text);
    }

    [Fact]
    public void RecordsFurtherApartThanTheWindowAreNotJoined()
    {
        var hook = Hook(Second, H, HScan, up: false);
        var raw = Raw(Second + 60 * Millisecond, H, HScan, up: false);
        var keys = new KeyDispositions([Installed(0, "recording-started"), hook, raw]);
        Assert.Equal(KeyOutcomeKind.Kept, keys.Of(hook.EventId)!.Kind);
        Assert.Equal("raw input only, no hook record", keys.Of(raw.EventId)!.Text);
    }

    [Fact]
    public void ARawInputOnlyKeyGivesTheHooksState()
    {
        var before = Raw(Second, H, HScan, up: false);
        Assert.Equal("raw input only, hook not installed", new KeyDispositions([before]).Of(before.EventId)!.Text);

        var lost = Raw(3 * Second, H, HScan, up: false);
        var keys = new KeyDispositions([Installed(Second, "recording-started"), lost, Installed(4 * Second, KeyboardHookRecords.HookLostReason)]);
        var outcome = keys.Of(lost.EventId)!;
        Assert.Equal(KeyOutcomeKind.Unexplained, outcome.Kind);
        Assert.StartsWith("raw input only, the hook had been removed (installed again at", outcome.Text, StringComparison.Ordinal);

        var failed = Raw(3 * Second, H, HScan, up: false);
        Assert.Equal(
            "raw input only, hook not installed",
            new KeyDispositions([Installed(Second, "refresh", installed: false), failed]).Of(failed.EventId)!.Text);
    }

    [Fact]
    public void APressOrReleaseInAPeriodNotRecordableIsNoted()
    {
        // A key pressed before an elevated window came to the front, and
        // released while it was.
        var period = new UnrecordablePeriod(14 * Second, 28 * Second, "high integrity window, powershell");
        var down = Hook(13 * Second, H, HScan, up: false);
        var rawDown = Raw(13 * Second + Millisecond, H, HScan, up: false);
        var up = Hook(29 * Second, Tab, TabScan, up: true);
        var rawUp = Raw(29 * Second + Millisecond, Tab, TabScan, up: true);
        var keys = new KeyDispositions([Installed(0, "recording-started"), down, rawDown, up, rawUp], null, [period]);

        Assert.Equal("passed; release not recorded: input not recordable from 00:00:14.000", keys.Of(down.EventId)!.Text);
        Assert.Equal(keys.Of(down.EventId)!.Text, keys.Of(rawDown.EventId)!.Text);
        Assert.Equal("passed; press not recorded: input not recordable until 00:00:28.000", keys.Of(up.EventId)!.Text);

        // A key pressed and released before the period has no note.
        var other = Hook(Second, H, HScan, up: false);
        var otherUp = Hook(Second + 100 * Millisecond, H, HScan, up: true);
        var plain = new KeyDispositions([other, otherUp], null, [period]);
        Assert.DoesNotContain("not recorded", plain.Of(other.EventId)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARawRecordTakesTheHooksNameOfTheKey()
    {
        var hook = Hook(Second, 0xA0, 42, up: false);
        var raw = Raw(Second + Millisecond, 0x10, 42, up: false);
        var keys = new KeyDispositions([hook, raw]);
        Assert.Equal("Left Shift down", keys.Of(raw.EventId)!.Key);
    }

    [Fact]
    public void OnlyTheTrustedKeydownIsKeyEvidence()
    {
        Assert.True(KeyDispositions.IsKeyEvidence(Page(0).Channel, Page(0).EventType, Page(0).Payload));
        var untrusted = Page(0, trusted: false);
        Assert.False(KeyDispositions.IsKeyEvidence(untrusted.Channel, untrusted.EventType, untrusted.Payload));
        Assert.False(KeyDispositions.IsKeyEvidence(KeyDispositions.DispatchChannel, KeyDispositions.DispatchStartedEventType,
            J("{'eventName':'click','trusted':true}")));
    }

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "windows.raw-input", "RawInputCollector", "1", ["input.keyboard", KeyboardHookRecords.Channel], "test");

    [Fact]
    public void TheIndexKeepsTheKeyRecordsAndTheArchiveDescribesThem()
    {
        var hook = Hook(Second, Tab, TabScan, up: false);
        var raw = Raw(Second + Millisecond, Tab, TabScan, up: false);
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        builder.Add(1, RecorderEventFactory.Create("s", Collector, KeyboardHookRecords.Channel, 1, hook.MonotonicNanoseconds,
            hook.EventType, hook.Payload) with { EventId = "h1" });
        builder.Add(2, RecorderEventFactory.Create("s", Collector, "input.keyboard", 2, raw.MonotonicNanoseconds,
            raw.EventType, raw.Payload) with { EventId = "r1" });
        var index = builder.Build();
        Assert.Equal(11, PlaybackIndex.CurrentVersion);
        var keptRaw = Assert.Single(index.Events, item => item.Channel == "input.keyboard");
        Assert.Equal(raw.Payload.GetRawText(), keptRaw.Payload.GetRawText());

        var archiveBuilder = new SessionPlaybackArchiveBuilder(Path.GetTempPath(), retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(4), 4 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 1, 0, null));
        Assert.Equal("passed", archive.KeyDispositions.Of("h1")!.Text);
        Assert.Equal(
            "raw-keyboard: Tab down, passed",
            archive.Describe(new SessionTimelineEvent(2, "r1", "observed", "input.keyboard", "raw-keyboard", raw.MonotonicNanoseconds, "raw-keyboard")));
        Assert.Equal(
            "hook-keyboard: Tab down, passed",
            archive.Describe(new SessionTimelineEvent(1, "h1", "observed", KeyboardHookRecords.Channel, "hook-keyboard", hook.MonotonicNanoseconds, "hook-keyboard: Tab down")));
    }
}
