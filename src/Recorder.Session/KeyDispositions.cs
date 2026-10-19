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
public enum ModeEvidenceKind
{
    Browse,
    Focus,
    Toggle,
    ExitFocus
}

/// <summary>
/// A key that shows the screen reader's mode, or switches it: its time,
/// record, kind, and basis, such as "H kept". See
/// docs/architecture/screen-reader-activity.md, "2c-2, browse or focus mode".
/// </summary>
public sealed record ModeEvidence(long Time, string EventId, ModeEvidenceKind Kind, string Basis);

/// <summary>A kept key labelled with NVDA commands: its time, the keys as NVDA names them, and the commands' names.</summary>
public sealed record KeptCommand(long Time, string Gesture, IReadOnlyList<string> Names);

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
    // Rule 2 adds the screen reader's command of a key (2c-1).
    public const int RuleVersion = 2;

    /// <summary>A hook record and a raw input record of one key, either way.</summary>
    public const long HookRawWindowNanoseconds = 50_000_000;

    /// <summary>An injected key after the kept key it follows.</summary>
    public const long InjectedAfterKeptWindowNanoseconds = 50_000_000;

    /// <summary>The page's keydown after a key down.</summary>
    public const long PageWindowNanoseconds = 100_000_000;

    public const string DispatchChannel = "browser.dispatch";
    public const string DispatchStartedEventType = "dispatch-started";

    private readonly Dictionary<string, KeyOutcome> _outcomes = new(StringComparer.Ordinal);
    private readonly List<ModeEvidence> _modeEvidence = [];
    private readonly List<KeptCommand> _keptCommands = [];

    private sealed class Key
    {
        public required string EventId { get; init; }
        public required long Time { get; init; }
        public required bool Hook { get; init; }
        public required int ScanCode { get; init; }
        public required int VirtualKey { get; init; }
        public required bool Up { get; init; }
        public required bool Injected { get; init; }
        public bool Extended { get; init; }
        public Key? Partner { get; set; }
        public Key? KeptKey { get; set; }
        public List<Key> Followers { get; } = [];
        public (string EventId, long Time)? Page { get; set; }
        public string Name => KeyboardHookRecords.KeyName(VirtualKey) + (Up ? " up" : " down");
    }

    public KeyDispositions(
        IEnumerable<KeyEvidenceRecord> records,
        AssistiveTechnologyTimeline? assistiveTechnology = null,
        IReadOnlyList<UnrecordablePeriod>? unrecordable = null,
        IReadOnlyList<ScreenReaderCommandData>? commandData = null)
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
                        Injected = Flag(payload, "injected"),
                        Extended = Flag(payload, "extended")
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

        LabelCommands(hook, assistiveTechnology, commandData);
        Rule = $"key outcome rule {RuleVersion}";
    }

    public static KeyDispositions Empty { get; } = new([]);

    /// <summary>The rule the outcomes were derived with.</summary>
    public string Rule { get; }

    /// <summary>
    /// The keys that show NVDA's mode or switch it, in order, wherever the
    /// front window was: a letter, digit, arrow, or Tab pressed with no
    /// modifier but Shift, kept (browse mode) or passed and received by the
    /// page (focus mode); a kept browse mode command; NVDA+Space kept (a
    /// switch); and Escape kept (back to browse mode).
    /// </summary>
    public IReadOnlyList<ModeEvidence> ModeEvidence => _modeEvidence;

    /// <summary>The kept keys labelled with a command from the command data, in order.</summary>
    public IReadOnlyList<KeptCommand> KeptCommands => _keptCommands;

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
            key.KeptKey?.Followers.Add(key);
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
        var keptText = running.Count == 0
            ? "kept, no screen reader running"
            : "kept, screen reader running: " + string.Join(", ", running);

        // The injected key that followed it, as NVDA's own Tab, so that it is
        // seen from the kept key: the two are too close to select apart.
        if (key.Followers.Count > 0)
        {
            keptText += $"; followed by an injected {KeyboardHookRecords.KeyName(key.VirtualKey)}" +
                (key.Followers.Any(follower => follower.Page is not null) ? ", received by the page" : string.Empty);
            foreach (var follower in key.Followers)
            {
                details.Add(
                    $"followed by: {Line("hook record", follower)}, {Milliseconds(follower.Time - key.Time)} later" +
                    (follower.Page is null ? string.Empty : ", received by the page"));
            }
        }

        return new KeyOutcome(KeyOutcomeKind.Kept, keptText, details);
    }

    // The settings the commands are worked out with, from NVDA's settings
    // record, or its defaults where a setting is not written or not recorded.
    private sealed record CommandSettings(
        int ModifierKeys,
        string Layout,
        long MultiPressNanoseconds,
        IReadOnlyList<NvdaCustomGesture> Custom,
        ScreenReaderCommandData? Data,
        IReadOnlyList<string> Basis);

    private static CommandSettings? SettingsAt(
        long time,
        AssistiveTechnologyTimeline assistiveTechnology,
        IReadOnlyList<ScreenReaderCommandData>? commandData,
        Dictionary<int, CommandSettings> cache)
    {
        var product = AssistiveTechnologyRecords.Nvda.Product;
        if (assistiveTechnology.ScreenReaderAt(product, time) is not { } running)
        {
            return null;
        }

        if (cache.TryGetValue(running.ProcessId, out var cached))
        {
            return cached;
        }

        var basis = new List<string>();
        var settings = running.Settings;
        string? Written(string name) => settings is { } payload ? Text(payload, name) : null;
        if (settings is null)
        {
            basis.Add($"{product}'s settings were not recorded; its defaults are assumed");
        }
        else if (Text(settings.Value, "problem") is { } problem)
        {
            basis.Add($"{product}'s settings: {problem}");
        }

        var layout = Written("keyboardLayout") ?? NvdaSettings.DefaultKeyboardLayout;
        basis.Add($"keyboard layout: {layout}" + (Written("keyboardLayout") is null ? " (NVDA's default)" : " (nvda.ini)"));
        var modifierKeys = NvdaSettings.ModifierKeys(Written("nvdaModifierKeys")) ?? NvdaSettings.DefaultNvdaModifierKeys;
        basis.Add($"NVDA keys: {ScreenReaderGestures.NvdaKeysText(modifierKeys)}" +
            (Written("nvdaModifierKeys") is { } keys ? $" (NVDAModifierKeys {keys} in nvda.ini)" : " (NVDA's default)"));
        var timeout = int.TryParse(Written("multiPressTimeout"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) && milliseconds > 0
            ? milliseconds
            : NvdaSettings.DefaultMultiPressTimeoutMilliseconds;
        var custom = new List<NvdaCustomGesture>();
        if (settings is { } recorded && recorded.TryGetProperty("customGestures", out var gestures) && gestures.ValueKind == JsonValueKind.Array)
        {
            foreach (var gesture in gestures.EnumerateArray())
            {
                if (Text(gesture, "section") is { } section && Text(gesture, "script") is { } script && Text(gesture, "gesture") is { } name)
                {
                    custom.Add(new NvdaCustomGesture(section, script, name));
                }
            }
        }

        if (settings is { } withProfiles && withProfiles.TryGetProperty("profiles", out var profiles) &&
            profiles.ValueKind == JsonValueKind.Array && profiles.GetArrayLength() > 0)
        {
            basis.Add("NVDA has configuration profiles, whose settings are not applied here");
        }

        ScreenReaderCommandData? data = null;
        if (ScreenReaderCommandData.For(product, running.Version, commandData) is { } chosen)
        {
            data = chosen.Data;
            if (chosen.Note is { } note)
            {
                basis.Add(note);
            }
        }
        else
        {
            basis.Add($"no command data for {product}");
        }

        return cache[running.ProcessId] = new CommandSettings(modifierKeys, layout, timeout * 1_000_000L, custom, data, basis);
    }

    // Each physical key down, in order, with the keys held down with it,
    // named as NVDA names the combination and looked up in the command
    // data; a kept key gains its command, as does a passed key whose
    // command NVDA acts on and passes on. A lone Ctrl or Shift is labelled
    // at its press when it is released with no other key between.
    private void LabelCommands(
        List<Key> hook,
        AssistiveTechnologyTimeline assistiveTechnology,
        IReadOnlyList<ScreenReaderCommandData>? commandData)
    {
        var cache = new Dictionary<int, CommandSettings>();
        var held = new List<Key>();
        Key? lone = null;
        (string Gesture, long Time, int Count)? last = null;
        foreach (var key in hook.Where(key => !key.Injected))
        {
            var settings = SettingsAt(key.Time, assistiveTechnology, commandData, cache);
            if (key.Up)
            {
                held.RemoveAll(down => down.VirtualKey == key.VirtualKey && down.Extended == key.Extended);
                if (lone is { } alone && alone.VirtualKey == key.VirtualKey && held.Count == 0 && settings is not null &&
                    ScreenReaderGestures.Modifier(alone.VirtualKey) is { } modifier)
                {
                    Label(alone, modifier, 1, settings);
                }

                lone = null;
                continue;
            }

            if (held.Any(down => down.VirtualKey == key.VirtualKey && down.Extended == key.Extended))
            {
                // Held down: the keyboard's repeat.
                continue;
            }

            held.Add(key);
            if (settings is null)
            {
                lone = null;
                continue;
            }

            if (ScreenReaderGestures.IsNvdaKey(key.VirtualKey, key.Extended, settings.ModifierKeys))
            {
                lone = null;
                Note(key, "NVDA key", $"an NVDA key: {string.Join("; ", settings.Basis.Where(line => line.StartsWith("NVDA keys", StringComparison.Ordinal)))}");
                continue;
            }

            if (ScreenReaderGestures.Modifier(key.VirtualKey) is not null)
            {
                lone = held.Count == 1 ? key : null;
                continue;
            }

            lone = null;
            if (ScreenReaderGestures.KeyName(key.VirtualKey, key.Extended) is not { } name)
            {
                last = null;
                continue;
            }

            var modifiers = held.Where(down => down != key)
                .Select(down => ScreenReaderGestures.IsNvdaKey(down.VirtualKey, down.Extended, settings.ModifierKeys)
                    ? "NVDA"
                    : ScreenReaderGestures.Modifier(down.VirtualKey))
                .OfType<string>()
                .Distinct();
            var gesture = string.Join("+", [.. modifiers, name]);
            var canonical = ScreenReaderGestures.Canonical(gesture);
            var count = last is { } previous && previous.Gesture == canonical && key.Time - previous.Time <= settings.MultiPressNanoseconds
                ? previous.Count + 1
                : 1;
            last = (canonical, key.Time, count);
            Label(key, gesture, count, settings);
            AddModeEvidence(key, gesture, held.Any(down => down != key && !IsShift(down.VirtualKey) &&
                (ScreenReaderGestures.Modifier(down.VirtualKey) is not null ||
                 ScreenReaderGestures.IsNvdaKey(down.VirtualKey, down.Extended, settings.ModifierKeys))), settings);
        }
    }

    private static bool IsShift(int virtualKey) => virtualKey is 0x10 or 0xA0 or 0xA1;

    private static bool ShowsMode(Key key) =>
        key.VirtualKey is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or 0x09 ||
        (key.VirtualKey is >= 0x25 and <= 0x28 && key.Extended);

    private void AddModeEvidence(Key key, string gesture, bool otherModifiers, CommandSettings settings)
    {
        if (!_outcomes.TryGetValue(key.EventId, out var outcome))
        {
            return;
        }

        var kept = outcome.Kind == KeyOutcomeKind.Kept;
        var canonical = ScreenReaderGestures.Canonical(gesture);
        var name = KeyboardHookRecords.KeyName(key.VirtualKey);
        if (kept && canonical == "nvda+space")
        {
            _modeEvidence.Add(new ModeEvidence(key.Time, key.EventId, ModeEvidenceKind.Toggle, "NVDA+Space kept"));
            return;
        }

        // NVDA keeps Escape in both modes. It returns to browse mode only
        // where it does not send the key on; in browse mode, in focus mode
        // entered with NVDA+Space, and in a menu, it sends the key on, seen
        // as an injected Escape, and the mode does not change, so that
        // Escape shows no mode (NVDA browseMode.py, script_disablePassThrough;
        // recording 20261011-144503 at 70.85 s).
        if (kept && canonical == "escape")
        {
            if (key.Followers.Count == 0)
            {
                _modeEvidence.Add(new ModeEvidence(key.Time, key.EventId, ModeEvidenceKind.ExitFocus, "Escape kept"));
            }

            return;
        }

        var shown = gesture.StartsWith("shift+", StringComparison.Ordinal) ? "Shift+" + name : name;
        if (kept && settings.Data?.Of(gesture, settings.Layout).FirstOrDefault(command => command.Context == "browse mode") is { } command)
        {
            _modeEvidence.Add(new ModeEvidence(key.Time, key.EventId, ModeEvidenceKind.Browse,
                $"{(otherModifiers ? gesture : shown)} kept ({command.Name})"));
            return;
        }

        if (otherModifiers || !ShowsMode(key))
        {
            return;
        }

        if (kept)
        {
            _modeEvidence.Add(new ModeEvidence(key.Time, key.EventId, ModeEvidenceKind.Browse, $"{shown} kept"));
        }
        else if (outcome.Kind == KeyOutcomeKind.Passed && key.Page is not null)
        {
            _modeEvidence.Add(new ModeEvidence(key.Time, key.EventId, ModeEvidenceKind.Focus, $"{shown} passed, received by the page"));
        }
    }

    private void Label(Key key, string gesture, int count, CommandSettings settings)
    {
        if (!_outcomes.TryGetValue(key.EventId, out var outcome))
        {
            return;
        }

        var canonical = ScreenReaderGestures.Canonical(gesture);
        var custom = settings.Custom.Where(item => ScreenReaderGestures.Canonical(item.Gesture) == canonical).ToList();
        var commands = settings.Data?.Of(gesture, settings.Layout) ?? [];
        var kept = outcome.Kind == KeyOutcomeKind.Kept;
        var shown = commands.Where(command => kept || command.Passes).ToList();
        var pressed = count switch
        {
            1 => string.Empty,
            2 => ", pressed twice",
            3 => ", pressed three times",
            _ => $", pressed {count} times"
        };
        string text;
        var details = new List<string>();
        if (custom.FirstOrDefault(item => item.Script != "None") is { } bound && (kept || shown.Count > 0))
        {
            text = $"inferring the custom command: {bound.Script} ({gesture}){pressed}";
            details.Add($"inferring from the keys held that {gesture} is bound to {bound.Script} in {bound.Section}, in gestures.ini");
        }
        else if (shown.Count > 0)
        {
            var names = string.Join(" or ", shown.Select(command =>
                command.Name + (command.Context == "any" ? string.Empty : $" in {command.Context}")));
            text = $"inferring the command: {names} ({gesture}){pressed}";
            if (kept)
            {
                _keptCommands.Add(new KeptCommand(key.Time, gesture, [.. shown.Select(command => command.Name)]));
            }

            foreach (var command in shown)
            {
                details.Add($"inferring from the keys held that {gesture} is {command.Name}" +
                    (command.Context == "any" ? string.Empty : $", in {command.Context}") +
                    (command.Passes ? ", which NVDA acts on and passes on" : string.Empty) +
                    $"; source: {settings.Data!.SourceOf(command)}");
            }

            if (custom.Any(item => item.Script == "None"))
            {
                details.Add($"note: gestures.ini unbinds {gesture} from a command; it may not have acted");
            }
        }
        else if (kept)
        {
            details.Add($"no command of {gesture} in the command data" +
                (settings.Data is { } data ? $" ({data.Product} {data.Version})" : string.Empty));
            details.AddRange(settings.Basis);
            _outcomes[key.EventId] = outcome with { Details = [.. outcome.Details, .. details] };
            return;
        }
        else
        {
            return;
        }

        details.AddRange(settings.Basis);
        Note(key, text, details);
    }

    private void Note(Key key, string text, params IEnumerable<string> details)
    {
        foreach (var id in key.Partner is { } partner ? [key.EventId, partner.EventId] : new[] { key.EventId })
        {
            if (_outcomes.TryGetValue(id, out var outcome))
            {
                _outcomes[id] = outcome with { Text = outcome.Text + "; " + text, Details = [.. outcome.Details, .. details] };
            }
        }
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
