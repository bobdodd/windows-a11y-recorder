using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>A record the key outcomes are derived from, as playback keeps it.</summary>
public sealed record KeyEvidenceRecord(
    string EventId,
    long MonotonicNanoseconds,
    string Channel,
    string EventType,
    JsonElement Payload);

public enum KeyOutcomeKind
{
    Passed,
    Kept,
    Injected,
    Unexplained,
    PageKeyDown
}

/// <summary>
/// What happened to a key record: its outcome, as the status line shows it,
/// and the records it was joined with, for the selected event's details.
/// </summary>
public sealed record KeyOutcome(KeyOutcomeKind Kind, string Text, IReadOnlyList<string> Details)
{
    /// <summary>The key and direction, such as "Tab down"; null for a page keydown.</summary>
    public string? Key { get; init; }
}

/// <summary>
/// The outcome of each key record, derived at playback from the keyboard
/// hook's records (input.keyboard-hook), raw input's (input.keyboard), the
/// page's trusted keydown dispatches (browser.dispatch), the screen
/// readers running (system.assistive-technology), and the periods when
/// input was not recordable. Rule 1, as in
/// docs/architecture/screen-reader-activity.md, "Key disposition".
/// </summary>
public sealed class KeyDispositions
{
    public const int RuleVersion = 1;

    /// <summary>A hook record and a raw input record of one key, either way.</summary>
    public const long HookRawWindowNanoseconds = 50_000_000;

    /// <summary>An injected key after the kept key it follows.</summary>
    public const long InjectedAfterKeptWindowNanoseconds = 50_000_000;

    /// <summary>The page's keydown after a key down.</summary>
    public const long PageWindowNanoseconds = 100_000_000;

    public const string DispatchChannel = "browser.dispatch";
    public const string DispatchStartedEventType = "dispatch-started";

    private readonly Dictionary<string, KeyOutcome> _outcomes = new(StringComparer.Ordinal);

    private sealed class Key
    {
        public required string EventId { get; init; }
        public required long Time { get; init; }
        public required bool Hook { get; init; }
        public required int ScanCode { get; init; }
        public required int VirtualKey { get; init; }
        public required bool Up { get; init; }
        public required bool Injected { get; init; }
        public Key? Partner { get; set; }
        public Key? KeptKey { get; set; }
        public (string EventId, long Time)? Page { get; set; }
        public string Name => KeyboardHookRecords.KeyName(VirtualKey) + (Up ? " up" : " down");
    }

    public KeyDispositions(
        IEnumerable<KeyEvidenceRecord> records,
        AssistiveTechnologyTimeline? assistiveTechnology = null,
        IReadOnlyList<UnrecordablePeriod>? unrecordable = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        assistiveTechnology ??= AssistiveTechnologyTimeline.Empty;
        unrecordable ??= [];
        var hook = new List<Key>();
        var raw = new List<Key>();
        var pages = new List<(string EventId, long Time)>();
        var installs = new List<(long Time, bool Installed, string? Reason)>();
        foreach (var record in records.OrderBy(record => record.MonotonicNanoseconds))
        {
            var payload = record.Payload;
            if (payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            switch (record.Channel, record.EventType)
            {
                case (KeyboardHookRecords.Channel, KeyboardHookRecords.KeyEventType)
                    when Integer(payload, "scanCode") is { } scan && Integer(payload, "virtualKey") is { } virtualKey:
                    hook.Add(new Key
                    {
                        EventId = record.EventId,
                        Time = record.MonotonicNanoseconds,
                        Hook = true,
                        ScanCode = scan,
                        VirtualKey = virtualKey,
                        Up = Flag(payload, "up"),
                        Injected = Flag(payload, "injected")
                    });
                    break;
                case ("input.keyboard", "raw-keyboard")
                    when Integer(payload, "makeCode") is { } make && Integer(payload, "flags") is { } flags:
                    raw.Add(new Key
                    {
                        EventId = record.EventId,
                        Time = record.MonotonicNanoseconds,
                        Hook = false,
                        ScanCode = make,
                        VirtualKey = Integer(payload, "virtualKey") ?? 0,
                        Up = (flags & KeyboardHookRecords.RawBreakFlag) != 0,
                        Injected = Long(payload, "deviceHandle") is null or 0
                    });
                    break;
                case (KeyboardHookRecords.Channel, KeyboardHookRecords.InstalledEventType):
                    installs.Add((record.MonotonicNanoseconds, Flag(payload, "installed"), Text(payload, "reason")));
                    break;
                case (DispatchChannel, DispatchStartedEventType) when IsPageKeyDown(payload):
                    pages.Add((record.EventId, record.MonotonicNanoseconds));
                    break;
            }
        }

        Join(hook, raw);
        LinkInjected(hook);
        JoinPage(hook, raw, pages);
        var notes = PeriodNotes(hook, raw, unrecordable);

        foreach (var key in hook)
        {
            var outcome = HookOutcome(key, assistiveTechnology);
            Add(key, outcome, notes);
            if (key.Partner is { } partner)
            {
                Add(partner, outcome, notes, key.Name);
            }
        }

        foreach (var key in raw.Where(key => key.Partner is null))
        {
            Add(key, RawOnlyOutcome(key, installs), notes);
        }

        foreach (var key in hook.Concat(raw))
        {
            if (key.Page is { } page && !_outcomes.ContainsKey(page.EventId))
            {
                _outcomes[page.EventId] = new KeyOutcome(
                    KeyOutcomeKind.PageKeyDown,
                    $"page keydown for {key.Name} at {WindowsPreferenceTimeline.Clock(key.Time)}",
                    [Line("key", key), $"page keydown at {Clock(page.Time)}, {Milliseconds(page.Time - key.Time)} after the key"]);
            }
        }

        Rule = $"key outcome rule {RuleVersion}";
    }

    public static KeyDispositions Empty { get; } = new([]);

    /// <summary>The rule the outcomes were derived with.</summary>
    public string Rule { get; }

    /// <summary>The number of records with an outcome.</summary>
    public int Count => _outcomes.Count;

    /// <summary>A record's outcome, or null for a record that has none.</summary>
    public KeyOutcome? Of(string eventId) => _outcomes.GetValueOrDefault(eventId);

    /// <summary>Whether playback keeps a record for the key outcomes.</summary>
    public static bool IsKeyEvidence(string channel, string eventType, JsonElement payload) =>
        channel == KeyboardHookRecords.Channel ||
        (channel == "input.keyboard" && eventType == "raw-keyboard") ||
        (channel == DispatchChannel && eventType == DispatchStartedEventType &&
            payload.ValueKind == JsonValueKind.Object && IsPageKeyDown(payload));

    private static bool IsPageKeyDown(JsonElement payload) =>
        Text(payload, "eventName") == "keydown" && Flag(payload, "trusted");

    // Each hook record, in order, takes the first raw input record not yet
    // taken of the same scan code, direction, and source within the window.
    private static void Join(List<Key> hook, List<Key> raw)
    {
        var first = 0;
        foreach (var key in hook)
        {
            while (first < raw.Count && raw[first].Time < key.Time - HookRawWindowNanoseconds)
            {
                first++;
            }

            for (var i = first; i < raw.Count && raw[i].Time <= key.Time + HookRawWindowNanoseconds; i++)
            {
                var candidate = raw[i];
                if (candidate.Partner is null &&
                    candidate.ScanCode == key.ScanCode &&
                    candidate.Up == key.Up &&
                    candidate.Injected == key.Injected)
                {
                    key.Partner = candidate;
                    candidate.Partner = key;
                    break;
                }
            }
        }
    }

    // An injected key follows the kept key down of the same virtual key it
    // stands for, within the window.
    private static void LinkInjected(List<Key> hook)
    {
        foreach (var key in hook.Where(key => key.Injected))
        {
            key.KeptKey = hook.LastOrDefault(kept =>
                !kept.Injected && !kept.Up && kept.Partner is null &&
                kept.VirtualKey == key.VirtualKey &&
                kept.Time <= key.Time && key.Time - kept.Time <= InjectedAfterKeptWindowNanoseconds);
        }
    }

    // Each key down that reached Windows, in order, takes the page's next
    // keydown not yet taken within the window. The keydown does not name
    // its key, so the join is by order and time only.
    private static void JoinPage(List<Key> hook, List<Key> raw, List<(string EventId, long Time)> pages)
    {
        var downs = hook.Where(key => !key.Up && key.Partner is not null)
            .Concat(raw.Where(key => !key.Up && key.Partner is null))
            .OrderBy(key => key.Partner?.Time ?? key.Time)
            .ToList();
        var next = 0;
        foreach (var key in downs)
        {
            var reached = key.Partner?.Time ?? key.Time;
            while (next < pages.Count && pages[next].Time < reached)
            {
                next++;
            }

            if (next < pages.Count && pages[next].Time - reached <= PageWindowNanoseconds)
            {
                key.Page = pages[next];
                if (key.Partner is { } partner)
                {
                    partner.Page = pages[next];
                }

                next++;
            }
        }
    }

    // The notes for a press or release that fell in a period when input was
    // not recordable: a down whose next up of the scan code (or the end)
    // is after a period's start, and an up whose last down of the scan code
    // (or the start) is before a period's end.
    private static Dictionary<Key, string> PeriodNotes(List<Key> hook, List<Key> raw, IReadOnlyList<UnrecordablePeriod> periods)
    {
        var notes = new Dictionary<Key, string>();
        if (periods.Count == 0)
        {
            return notes;
        }

        // One record of each key: the hook's, or raw input's where the hook
        // has none.
        var keys = hook.Concat(raw.Where(key => key.Partner is null))
            .Where(key => !key.Injected)
            .OrderBy(key => key.Time)
            .ToList();
        foreach (var group in keys.GroupBy(key => key.ScanCode))
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var key = list[i];
                if (!key.Up && (i + 1 == list.Count || list[i + 1].Up))
                {
                    var until = i + 1 < list.Count ? list[i + 1].Time : long.MaxValue;
                    if (periods.FirstOrDefault(period => period.Start > key.Time && period.Start < until) is { } period)
                    {
                        notes[key] = $"release not recorded: input not recordable from {WindowsPreferenceTimeline.Clock(period.Start)}";
                    }
                }
                else if (key.Up && (i == 0 || list[i - 1].Up))
                {
                    var since = i > 0 ? list[i - 1].Time : long.MinValue;
                    if (periods.LastOrDefault(period => period.End < key.Time && period.End > since) is { } period)
                    {
                        notes[key] = $"press not recorded: input not recordable until {WindowsPreferenceTimeline.Clock(period.End)}";
                    }
                }
            }
        }

        return notes;
    }

    private static KeyOutcome HookOutcome(Key key, AssistiveTechnologyTimeline assistiveTechnology)
    {
        var details = new List<string> { Line("hook record", key) };
        if (key.Partner is { } partner)
        {
            details.Add($"{Line("raw input record", partner)}, {Milliseconds(partner.Time - key.Time)} after the hook");
        }
        else
        {
            details.Add("no raw input record of the key within 50 ms");
        }

        if (key.Page is { } page)
        {
            details.Add($"page keydown at {Clock(page.Time)}");
        }

        if (key.Injected)
        {
            var text = "injected";
            if (key.KeptKey is { } kept)
            {
                text += $" after the kept {kept.Name} at {WindowsPreferenceTimeline.Clock(kept.Time)}";
                details.Add($"follows the kept key: {Line("hook record", kept)}, {Milliseconds(key.Time - kept.Time)} before");
            }

            if (key.Page is not null)
            {
                text += ", received by the page";
            }

            return new KeyOutcome(KeyOutcomeKind.Injected, text, details);
        }

        if (key.Partner is not null)
        {
            return new KeyOutcome(
                KeyOutcomeKind.Passed,
                key.Page is null ? "passed" : "passed, received by the page",
                details);
        }

        var running = assistiveTechnology.ScreenReadersRunningAt(key.Time);
        details.Add("inferred: Windows does not say which program kept the key");
        return new KeyOutcome(
            KeyOutcomeKind.Kept,
            running.Count == 0
                ? "kept, no screen reader running"
                : "kept, screen reader running: " + string.Join(", ", running),
            details);
    }

    private static KeyOutcome RawOnlyOutcome(Key key, List<(long Time, bool Installed, string? Reason)> installs)
    {
        var details = new List<string> { Line("raw input record", key), "no hook record of the key within 50 ms" };
        if (key.Page is { } page)
        {
            details.Add($"page keydown at {Clock(page.Time)}");
        }

        if (key.Injected)
        {
            return new KeyOutcome(
                KeyOutcomeKind.Injected,
                "injected, raw input only" + (key.Page is null ? string.Empty : ", received by the page"),
                details);
        }

        var last = installs.LastOrDefault(install => install.Time <= key.Time);
        var following = installs.FirstOrDefault(install => install.Time > key.Time);
        var reason = last == default || !last.Installed
            ? "raw input only, hook not installed"
            : following != default && following.Reason == KeyboardHookRecords.HookLostReason
                ? $"raw input only, the hook had been removed (installed again at {WindowsPreferenceTimeline.Clock(following.Time)})"
                : "raw input only, no hook record";
        return new KeyOutcome(KeyOutcomeKind.Unexplained, reason, details);
    }

    // A raw input record joined to a hook record takes the hook's name of
    // the key, which tells left from right.
    private void Add(Key key, KeyOutcome outcome, Dictionary<Key, string> notes, string? name = null)
    {
        var note = notes.GetValueOrDefault(key) ?? (key.Partner is { } partner ? notes.GetValueOrDefault(partner) : null);
        _outcomes[key.EventId] = (note is null
            ? outcome
            : outcome with { Text = outcome.Text + "; " + note, Details = [.. outcome.Details, note] }) with { Key = name ?? key.Name };
    }

    private static string Line(string what, Key key) =>
        $"{what}: {key.Name} at {Clock(key.Time)}{(key.Injected ? ", injected" : string.Empty)}";

    private static string Clock(long time) => WindowsPreferenceTimeline.Clock(time);

    private static string Milliseconds(long nanoseconds) =>
        (nanoseconds / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " ms";

    private static bool Flag(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Integer(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? Long(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}
