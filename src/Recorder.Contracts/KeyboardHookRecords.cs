namespace Recorder.Contracts;

/// <summary>
/// The records of the <c>input.keyboard-hook</c> channel, written by the
/// keyboard hook collector: every key its low-level keyboard hook was
/// called with, and every installation of the hook, with its reason. The
/// hook observes only: it passes every key on unchanged. See
/// docs/architecture/screen-reader-activity.md, "The keyboard hook".
/// </summary>
public static class KeyboardHookRecords
{
    public const string Channel = "input.keyboard-hook";

    public const string KeyEventType = "hook-keyboard";
    public const string InstalledEventType = "hook-installed";

    public static IReadOnlyList<string> EventTypes { get; } = [KeyEventType, InstalledEventType];

    /// <summary>The first installation, when the recording starts.</summary>
    public const string RecordingStartedReason = "recording-started";

    /// <summary>A screen reader's main process was seen to start.</summary>
    public const string ScreenReaderStartedReason = "screen-reader-started";

    /// <summary>
    /// Once a second while a screen reader runs, so that the hook is first
    /// in the chain again soon after the screen reader installs its own.
    /// </summary>
    public const string RefreshReason = "refresh";

    /// <summary>
    /// A key from a keyboard reached raw input with no call of the hook for
    /// it, so the hook had been removed.
    /// </summary>
    public const string HookLostReason = "hook-lost";

    public static IReadOnlyList<string> Reasons { get; } =
        [RecordingStartedReason, ScreenReaderStartedReason, RefreshReason, HookLostReason];

    public const int RefreshMilliseconds = 1000;

    /// <summary>
    /// How long the recorder waits for the hook call of a key raw input
    /// recorded before it judges the hook lost. Generous, so a slow call is
    /// not taken for a loss; the player's key outcomes join the records
    /// within a narrower window (KeyDispositions, and
    /// docs/architecture/screen-reader-activity.md, "Key disposition").
    /// </summary>
    public const int JoinWindowMilliseconds = 300;

    // KBDLLHOOKSTRUCT flags
    // (https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct).
    public const int ExtendedFlag = 0x01;
    public const int LowerIntegrityInjectedFlag = 0x02;
    public const int InjectedFlag = 0x10;
    public const int AltDownFlag = 0x20;
    public const int UpFlag = 0x80;

    // RAWKEYBOARD flags: RI_KEY_BREAK.
    public const int RawBreakFlag = 0x01;

    /// <summary>
    /// A virtual key's name, for the player
    /// (https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes),
    /// or its code where it has no name here.
    /// </summary>
    public static string KeyName(int virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x60 and <= 0x69 => "Numpad " + (virtualKey - 0x60).ToString(System.Globalization.CultureInfo.InvariantCulture),
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => KeyNames.TryGetValue(virtualKey, out var name)
            ? name
            : "key 0x" + virtualKey.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
    };

    private static readonly Dictionary<int, string> KeyNames = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x10] = "Shift", [0x11] = "Ctrl",
        [0x12] = "Alt", [0x13] = "Pause", [0x14] = "Caps Lock", [0x1B] = "Escape", [0x20] = "Space",
        [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2C] = "Print Screen", [0x2D] = "Insert", [0x2E] = "Delete",
        [0x5B] = "Left Windows", [0x5C] = "Right Windows", [0x5D] = "Applications",
        [0x6A] = "Numpad *", [0x6B] = "Numpad +", [0x6D] = "Numpad -", [0x6E] = "Numpad .", [0x6F] = "Numpad /",
        [0x90] = "Num Lock", [0x91] = "Scroll Lock",
        [0xA0] = "Left Shift", [0xA1] = "Right Shift", [0xA2] = "Left Ctrl", [0xA3] = "Right Ctrl",
        [0xA4] = "Left Alt", [0xA5] = "Right Alt",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'"
    };
}

/// <summary>A hook loss: the key that showed it and the hook's last key before it.</summary>
public sealed record HookLoss(long UnmatchedRawKeyAt, int UnmatchedScanCode, bool UnmatchedUp, long? LastHookKeyAt);

/// <summary>
/// Finds that the keyboard hook was removed: a key from a keyboard device in
/// raw input with no call of the hook for the same scan code and direction
/// within the join window either side. A key the hook saw and raw input did
/// not, such as one a screen reader kept, is not a loss. Injected keys, which
/// have no device, and raw input's fake keys are not checked. Keys are
/// matched by scan code, as raw input gives Shift, Ctrl, and Alt without
/// their side. Used under one lock.
/// </summary>
public sealed class HookLossDetector(long windowNanoseconds)
{
    private readonly List<(long At, int ScanCode, bool Up)> _hookKeys = [];
    private readonly List<(long At, int ScanCode, bool Up)> _pendingRaw = [];
    private long? _lastHookKeyAt;

    /// <summary>A key the hook was called with.</summary>
    public void ObserveHook(long at, int scanCode, bool up, bool injected)
    {
        _lastHookKeyAt = _lastHookKeyAt is { } last ? Math.Max(last, at) : at;
        if (!injected)
        {
            _hookKeys.Add((at, scanCode, up));
        }
    }

    /// <summary>A key raw input recorded.</summary>
    public void ObserveRaw(long at, int scanCode, int virtualKey, bool up, bool fromDevice)
    {
        // 0xFF is raw input's fake key and overrun code.
        if (fromDevice && scanCode is > 0 and < 0xFF && virtualKey != 0xFF)
        {
            _pendingRaw.Add((at, scanCode, up));
        }
    }

    /// <summary>
    /// Checks each raw key whose window has passed, and returns the first
    /// with no hook call, or null. After a loss the pending keys are
    /// dropped, as the hook is installed again.
    /// </summary>
    public HookLoss? Poll(long now)
    {
        HookLoss? loss = null;
        for (var i = 0; i < _pendingRaw.Count; i++)
        {
            var raw = _pendingRaw[i];
            if (raw.At + windowNanoseconds > now)
            {
                continue;
            }

            var matched = _hookKeys.Any(hook =>
                hook.ScanCode == raw.ScanCode && hook.Up == raw.Up &&
                Math.Abs(hook.At - raw.At) <= windowNanoseconds);
            if (!matched)
            {
                long? lastBefore = null;
                foreach (var hook in _hookKeys)
                {
                    if (hook.At <= raw.At)
                    {
                        lastBefore = hook.At;
                    }
                }

                loss = new HookLoss(raw.At, raw.ScanCode, raw.Up, lastBefore ?? (_lastHookKeyAt <= raw.At ? _lastHookKeyAt : null));
                _pendingRaw.Clear();
                break;
            }

            _pendingRaw.RemoveAt(i);
            i--;
        }

        // Hook calls are kept as long as a pending raw key may need them.
        var keepFrom = (_pendingRaw.Count == 0 ? now : Math.Min(now, _pendingRaw.Min(raw => raw.At))) - 2 * windowNanoseconds;
        _hookKeys.RemoveAll(hook => hook.At < keepFrom);
        return loss;
    }
}
