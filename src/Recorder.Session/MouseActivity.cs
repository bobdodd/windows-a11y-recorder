using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>What happened to a mouse hook record: the outcome as one line, and the records and settings it rests on.</summary>
public sealed record MouseOutcome(string Text, IReadOnlyList<string> Details);

/// <summary>
/// The mouse at playback, from the mouse hook's records
/// (<c>input.mouse-hook</c>), raw input's mouse records
/// (<c>input.mouse</c>), and NVDA's runs and settings
/// (<c>system.assistive-technology</c>): each mouse event's outcome, for
/// Selected event values, and the Pointer and NVDA mouse tracking rows of
/// the properties panel. What NVDA does with an event is inferred from its
/// settings at its start and its source code; NVDA's own records of it are
/// not read. See docs/architecture/screen-reader-activity.md, "The mouse
/// hook".
/// </summary>
public sealed class MouseActivity
{
    public const int RuleVersion = 1;

    /// <summary>A hook record and a raw input record of one event, either way.</summary>
    public const long HookRawWindowNanoseconds = 50_000_000;

    public const string PointerKey = "mouse.pointer";
    public const string NvdaTrackingKey = "mouse.nvda-tracking";
    public const string PointerSetting = "Pointer";
    public const string NvdaTrackingSetting = "NVDA mouse tracking";

    private const long ChangedWindowNanoseconds = 1_000_000_000;
    private const string Nvda = "NVDA";

    private readonly List<HookEvent> _events = [];
    private readonly List<long> _eventTimes = [];
    private readonly Dictionary<string, MouseOutcome> _outcomes = new(StringComparer.Ordinal);
    private readonly AssistiveTechnologyTimeline _assistiveTechnology;

    private sealed record HookEvent(
        string EventId,
        long Time,
        string Kind,
        string? Button,
        int X,
        int Y,
        int? WheelDelta,
        bool Injected,
        bool LowerIntegrityInjected,
        string? PenOrTouch,
        int Installation,
        int Flags);

    private sealed record RawEvent(string EventId, long Time, long Device, string Kind)
    {
        public bool Paired { get; set; }
    }

    public MouseActivity(IEnumerable<KeyEvidenceRecord> records, AssistiveTechnologyTimeline? assistiveTechnology = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        _assistiveTechnology = assistiveTechnology ?? AssistiveTechnologyTimeline.Empty;
        var raw = new Dictionary<string, List<RawEvent>>(StringComparer.Ordinal);
        foreach (var record in records.OrderBy(record => record.MonotonicNanoseconds))
        {
            var payload = record.Payload;
            if (payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (record.Channel == MouseHookRecords.Channel && record.EventType == MouseHookRecords.EventType)
            {
                _events.Add(new HookEvent(
                    record.EventId,
                    record.MonotonicNanoseconds,
                    Text(payload, "kind") ?? MouseHookRecords.OtherKind,
                    Text(payload, "button"),
                    Int(payload, "x") ?? 0,
                    Int(payload, "y") ?? 0,
                    Int(payload, "wheelDelta"),
                    Bool(payload, "injected"),
                    Bool(payload, "lowerIntegrityInjected"),
                    Text(payload, "penOrTouch"),
                    Int(payload, "installation") ?? 0,
                    Int(payload, "flags") ?? 0));
                _eventTimes.Add(record.MonotonicNanoseconds);
            }
            else if (record.Channel == RawChannel && record.EventType == RawEventType &&
                     payload.TryGetProperty("deviceHandle", out var device) && device.TryGetInt64(out var handle) && handle != 0)
            {
                foreach (var kind in RawKinds(payload))
                {
                    if (!raw.TryGetValue(kind, out var list))
                    {
                        raw[kind] = list = [];
                    }

                    list.Add(new RawEvent(record.EventId, record.MonotonicNanoseconds, handle, kind));
                }
            }
        }

        Recorded = _events.Count > 0;
        var starts = raw.ToDictionary(pair => pair.Key, _ => 0, StringComparer.Ordinal);
        foreach (var item in _events)
        {
            RawEvent? joined = null;
            var kind = JoinKind(item.Kind, item.Button);
            if (!item.Injected && kind is not null && raw.TryGetValue(kind, out var candidates))
            {
                // The first raw record of the kind not yet joined within the
                // window either side, in time order.
                var start = starts[kind];
                while (start < candidates.Count && candidates[start].Time < item.Time - HookRawWindowNanoseconds)
                {
                    start++;
                }

                starts[kind] = start;
                for (var i = start; i < candidates.Count && candidates[i].Time <= item.Time + HookRawWindowNanoseconds; i++)
                {
                    if (!candidates[i].Paired)
                    {
                        joined = candidates[i];
                        joined.Paired = true;
                        break;
                    }
                }
            }

            _outcomes[item.EventId] = Outcome(item, joined);
        }
    }

    public static MouseActivity Empty { get; } = new([]);

    /// <summary>Whether the recording holds the mouse hook's records.</summary>
    public bool Recorded { get; }

    public string Rule => $"rule {RuleVersion.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>A mouse hook record's outcome, or null for another record.</summary>
    public MouseOutcome? Of(string eventId) => _outcomes.GetValueOrDefault(eventId);

    public const string RawChannel = "input.mouse";
    public const string RawEventType = "raw-mouse";

    /// <summary>Whether playback keeps a record for the mouse outcomes.</summary>
    public static bool IsMouseEvidence(string channel, string eventType) =>
        (channel == MouseHookRecords.Channel && MouseHookRecords.EventTypes.Contains(eventType)) ||
        (channel == RawChannel && eventType == RawEventType);

    /// <summary>
    /// The Pointer and NVDA mouse tracking rows at a time, in the Screen
    /// reader group; none for a recording without the mouse hook's records.
    /// </summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time)
    {
        if (!Recorded)
        {
            return [];
        }

        var index = LastAtOrBefore(time);
        PropertyRow pointer;
        if (index < 0)
        {
            pointer = new PropertyRow(
                AssistiveTechnologyTimeline.Group, PointerSetting, "no mouse event yet", "at start", null, false, PointerKey);
        }
        else
        {
            var item = _events[index];
            pointer = new PropertyRow(
                AssistiveTechnologyTimeline.Group,
                PointerSetting,
                $"{Describe(item)}, {Source(item)}",
                WindowsPreferenceTimeline.Clock(item.Time),
                item.Time,
                time - item.Time < ChangedWindowNanoseconds,
                PointerKey);
        }

        var runs = NvdaChangeTimes();
        long? runChange = runs.LastOrDefault(change => change <= time) is var at && at > 0 ? at : null;
        var tracking = new PropertyRow(
            AssistiveTechnologyTimeline.Group,
            NvdaTrackingSetting,
            NvdaTrackingAt(time),
            runChange is { } changed ? WindowsPreferenceTimeline.Clock(changed) : "at start",
            runChange,
            runChange is { } recent && time - recent < ChangedWindowNanoseconds,
            NvdaTrackingKey);
        return [pointer, tracking];
    }

    /// <summary>
    /// The times a row's value changed, for stepping: each mouse event for
    /// the Pointer row, NVDA's starts and exits for the NVDA mouse tracking
    /// row; null for another key.
    /// </summary>
    public IReadOnlyList<long>? ChangeTimesOf(string key) =>
        !Recorded
            ? null
            : key switch
            {
                PointerKey => _eventTimes,
                NvdaTrackingKey => NvdaChangeTimes(),
                _ => null
            };

    /// <summary>What NVDA's mouse settings were at a time, as the NVDA mouse tracking row gives them.</summary>
    public string NvdaTrackingAt(long time)
    {
        if (_assistiveTechnology.ScreenReaderAt(Nvda, time) is not { } nvda)
        {
            return _assistiveTechnology.Recorded ? "NVDA not running" : "not recorded";
        }

        if (nvda.Settings is not { } settings || !settings.TryGetProperty("read", out _))
        {
            return "not known: NVDA's settings were not recorded";
        }

        if (!settings.TryGetProperty("enableMouseTracking", out _))
        {
            return "not known: NVDA's mouse settings were not recorded";
        }

        var (tracking, trackingText) = Setting(settings, "enableMouseTracking", NvdaSettings.DefaultEnableMouseTracking);
        var (ignore, ignoreText) = Setting(settings, "ignoreInjectedMouseInput", NvdaSettings.DefaultIgnoreInjectedMouseInput);
        var tracked = tracking switch
        {
            true => "tracking on",
            false => "tracking off",
            null => "tracking not known"
        };
        var injected = ignore switch
        {
            true => "ignores mouse input from other applications",
            false => "does not ignore mouse input from other applications",
            null => "whether it ignores mouse input from other applications is not known"
        };
        return $"{tracked} ({trackingText}), {injected} ({ignoreText}), as read at NVDA's start";
    }

    private List<long> NvdaChangeTimes() =>
        [.. _assistiveTechnology.ScreenReaderRuns(Nvda).Select(run => run.Time).Distinct().Order()];

    private int LastAtOrBefore(long time)
    {
        var index = _eventTimes.BinarySearch(time);
        if (index >= 0)
        {
            while (index + 1 < _eventTimes.Count && _eventTimes[index + 1] == time)
            {
                index++;
            }

            return index;
        }

        return ~index - 1;
    }

    // --- Outcomes ---------------------------------------------------------------

    private MouseOutcome Outcome(HookEvent item, RawEvent? raw)
    {
        var details = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"Mouse hook: installation {item.Installation}, flags 0x{item.Flags:X2}") +
            (item.PenOrTouch is { } penOrTouch ? $", extra information with the {penOrTouch} signature" : string.Empty)
        };
        if (raw is not null)
        {
            details.Add(string.Create(CultureInfo.InvariantCulture,
                $"Raw input: device 0x{raw.Device:X}, {Offset(raw.Time - item.Time)}"));
        }
        else if (!item.Injected && JoinKind(item.Kind, item.Button) is not null)
        {
            details.Add(string.Create(CultureInfo.InvariantCulture,
                $"Raw input: no record of it from a device within {HookRawWindowNanoseconds / 1_000_000} ms"));
        }

        var nvda = NvdaOutcome(item, details);
        var text = $"{Capitalize(Describe(item))}, {Source(item)}" + (nvda is null ? string.Empty : "; " + nvda);
        return new MouseOutcome(text, details);
    }

    // What NVDA does with the event, as its mouse handler decides
    // (https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/mouseHandler.py,
    // internal_mouseEvent): an injected event is ignored where it ignores
    // injected mouse input; every event where mouse tracking is off; a move
    // is noted, and what is under the pointer read at the latest position
    // the next time NVDA runs its queue; a left or right button press stops
    // speech. Null where NVDA was not running.
    private string? NvdaOutcome(HookEvent item, List<string> details)
    {
        if (_assistiveTechnology.ScreenReaderAt(Nvda, item.Time) is not { } nvda)
        {
            return null;
        }

        if (nvda.Settings is not { } settings || !settings.TryGetProperty("enableMouseTracking", out _))
        {
            details.Add("NVDA: its mouse settings were not recorded, so what it did with this event is not known.");
            return "NVDA running, its mouse settings not recorded";
        }

        var (tracking, trackingText) = Setting(settings, "enableMouseTracking", NvdaSettings.DefaultEnableMouseTracking);
        var (ignore, ignoreText) = Setting(settings, "ignoreInjectedMouseInput", NvdaSettings.DefaultIgnoreInjectedMouseInput);
        details.Add(string.Create(CultureInfo.InvariantCulture,
            $"NVDA (process {nvda.ProcessId}): mouse tracking {trackingText}, ignore mouse input from other applications {ignoreText}, as read at its start"));
        if (item.Injected)
        {
            details.Add("NVDA also ignores injected mouse events while it makes its own mouse clicks; that is not recorded.");
        }

        if (item.Injected && ignore != false)
        {
            return ignore == true
                ? "NVDA ignores it, inferred: injected, and NVDA ignores mouse input from other applications"
                : "whether NVDA ignores it is not known: injected, and NVDA's setting for mouse input from other applications was not read";
        }

        if (tracking != true)
        {
            return tracking == false
                ? "NVDA ignores it, inferred: NVDA's mouse tracking is off"
                : "whether NVDA follows it is not known: NVDA's mouse tracking setting was not read";
        }

        return item.Kind switch
        {
            MouseHookRecords.MoveKind =>
                "NVDA notes the position, inferred; it reads what is under the pointer at the latest position the next time it runs its queue, so not every move is read",
            MouseHookRecords.ButtonDownKind when item.Button is "left" or "right" =>
                "NVDA stops speech, inferred",
            _ => "NVDA notes the position and does nothing more with it, inferred"
        };
    }

    private static (bool? Value, string Text) Setting(JsonElement settings, string name, bool defaultValue)
    {
        var written = Text(settings, name);
        var value = NvdaSettings.Boolean(written, defaultValue);
        var text = written is null
            ? $"{(defaultValue ? "on" : "off")}, NVDA's default"
            : value switch
            {
                true => $"on, written \"{written}\"",
                false => $"off, written \"{written}\"",
                null => $"written \"{written}\", which NVDA does not read as on or off"
            };
        return (value, text);
    }

    private static string Describe(HookEvent item)
    {
        var at = string.Create(CultureInfo.InvariantCulture, $"({item.X}, {item.Y})");
        return item.Kind switch
        {
            MouseHookRecords.MoveKind => $"move to {at}",
            MouseHookRecords.ButtonDownKind => $"{ButtonName(item.Button)} down at {at}",
            MouseHookRecords.ButtonUpKind => $"{ButtonName(item.Button)} up at {at}",
            MouseHookRecords.WheelKind => $"wheel {Wheel(item.WheelDelta, "away from the user", "toward the user")} at {at}",
            MouseHookRecords.HorizontalWheelKind => $"horizontal wheel {Wheel(item.WheelDelta, "to the right", "to the left")} at {at}",
            _ => $"mouse event at {at}"
        };
    }

    private static string ButtonName(string? button) => button switch
    {
        "left" => "left button",
        "right" => "right button",
        "middle" => "middle button",
        "x1" => "X button 1",
        "x2" => "X button 2",
        _ => "X button"
    };

    // A wheel's turn, in notches of 120 where it turns by them
    // (https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel).
    private static string Wheel(int? delta, string positive, string negative) => delta switch
    {
        null or 0 => "turned",
        { } value when value % 120 == 0 =>
            string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(value) / 120} notch{(Math.Abs(value) == 120 ? string.Empty : "es")} {(value > 0 ? positive : negative)}"),
        { } value => string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(value)} units {(value > 0 ? positive : negative)}")
    };

    private static string Source(HookEvent item) =>
        item.PenOrTouch is { } penOrTouch
            ? $"from a {(penOrTouch == "touch" ? "touch screen" : "pen")}" +
              (item.Injected ? ", injected" : string.Empty)
            : item.LowerIntegrityInjected
                ? "injected by software at a lower integrity level"
                : item.Injected
                    ? "injected by software"
                    : "from a device";

    private static string Offset(long nanoseconds) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Math.Abs(nanoseconds) / 1_000_000.0:0.0} ms {(nanoseconds < 0 ? "before" : "after")} the hook's record");

    // The kinds a hook event and a raw input record are joined by: a move;
    // a button press or release, left and right as one, as for the loss
    // check (MouseHookRecords.LeftOrRightButton); a wheel's turn.
    private static string? JoinKind(string kind, string? button) => kind switch
    {
        MouseHookRecords.MoveKind => "move",
        MouseHookRecords.WheelKind => "wheel",
        MouseHookRecords.HorizontalWheelKind => "horizontal-wheel",
        _ => MouseHookRecords.HookButton(kind, button) is { } hook
            ? string.Create(CultureInfo.InvariantCulture, $"button-{hook.Button}-{(hook.Up ? "up" : "down")}")
            : null
    };

    private static IEnumerable<string> RawKinds(JsonElement payload)
    {
        var flags = Int(payload, "buttonFlags") ?? 0;
        if ((Int(payload, "deltaX") ?? 0) != 0 || (Int(payload, "deltaY") ?? 0) != 0 ||
            Text(payload, "movementMode") == "absolute")
        {
            yield return "move";
        }

        foreach (var (button, up) in MouseHookRecords.RawButtons(flags))
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"button-{button}-{(up ? "up" : "down")}");
        }

        // RI_MOUSE_WHEEL and RI_MOUSE_HWHEEL
        // (https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse).
        if ((flags & 0x0400) != 0)
        {
            yield return "wheel";
        }

        if ((flags & 0x0800) != 0)
        {
            yield return "horizontal-wheel";
        }
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];

    private static string? Text(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var read)
            ? read
            : null;

    private static bool Bool(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
