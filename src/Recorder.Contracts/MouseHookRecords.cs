namespace Recorder.Contracts;

/// <summary>
/// The records of the <c>input.mouse-hook</c> channel, written by the
/// keyboard hook collector from its low-level mouse hook
/// (<c>WH_MOUSE_LL</c>), on the keyboard hook's thread and on its terms: it
/// observes only and passes every event on unchanged. Every event the hook
/// is called with is recorded, moves included (the owner, 2026-10-11: "I
/// want to create heat maps so I will probably need that granularity"), and
/// every installation of the hook, with its reason. See
/// docs/architecture/screen-reader-activity.md, "The mouse hook".
/// </summary>
public static class MouseHookRecords
{
    public const string Channel = "input.mouse-hook";

    public const string EventType = "hook-mouse";
    public const string InstalledEventType = "hook-mouse-installed";

    public static IReadOnlyList<string> EventTypes { get; } = [EventType, InstalledEventType];

    /// <summary>
    /// The mouse hook is installed with the keyboard hook, so when the
    /// keyboard hook is found lost both are installed again.
    /// </summary>
    public const string KeyboardHookLostReason = "keyboard-hook-lost";

    public static IReadOnlyList<string> Reasons { get; } =
    [
        KeyboardHookRecords.RecordingStartedReason,
        KeyboardHookRecords.ScreenReaderStartedReason,
        KeyboardHookRecords.RefreshReason,
        KeyboardHookRecords.HookLostReason,
        KeyboardHookLostReason
    ];

    // MSLLHOOKSTRUCT flags
    // (https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-msllhookstruct).
    public const int InjectedFlag = 0x01;
    public const int LowerIntegrityInjectedFlag = 0x02;

    // The messages the hook is called with
    // (https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc).
    public const int MouseMove = 0x0200;
    public const int LeftButtonDown = 0x0201;
    public const int LeftButtonUp = 0x0202;
    public const int RightButtonDown = 0x0204;
    public const int RightButtonUp = 0x0205;
    public const int MiddleButtonDown = 0x0207;
    public const int MiddleButtonUp = 0x0208;
    public const int MouseWheel = 0x020A;
    public const int XButtonDown = 0x020B;
    public const int XButtonUp = 0x020C;
    public const int MouseHorizontalWheel = 0x020E;

    public const string MoveKind = "move";
    public const string ButtonDownKind = "button-down";
    public const string ButtonUpKind = "button-up";
    public const string WheelKind = "wheel";
    public const string HorizontalWheelKind = "horizontal-wheel";
    public const string OtherKind = "other";

    public static IReadOnlyList<string> Kinds { get; } =
        [MoveKind, ButtonDownKind, ButtonUpKind, WheelKind, HorizontalWheelKind, OtherKind];

    public static IReadOnlyList<string> Buttons { get; } = ["left", "right", "middle", "x1", "x2"];

    /// <summary>
    /// A message's kind and button. For an X button the high word of the
    /// mouse data says which, 1 or 2; an X button message with neither has
    /// no button.
    /// </summary>
    public static (string Kind, string? Button) Describe(int message, int mouseData)
    {
        var high = (mouseData >> 16) & 0xFFFF;
        string? xButton = high switch { 1 => "x1", 2 => "x2", _ => null };
        return message switch
        {
            MouseMove => (MoveKind, null),
            LeftButtonDown => (ButtonDownKind, "left"),
            LeftButtonUp => (ButtonUpKind, "left"),
            RightButtonDown => (ButtonDownKind, "right"),
            RightButtonUp => (ButtonUpKind, "right"),
            MiddleButtonDown => (ButtonDownKind, "middle"),
            MiddleButtonUp => (ButtonUpKind, "middle"),
            XButtonDown => (ButtonDownKind, xButton),
            XButtonUp => (ButtonUpKind, xButton),
            MouseWheel => (WheelKind, null),
            MouseHorizontalWheel => (HorizontalWheelKind, null),
            _ => (OtherKind, null)
        };
    }

    /// <summary>
    /// The wheel's movement, the high word of the mouse data as a signed
    /// number, in multiples of 120 for a notched wheel; null for another
    /// kind.
    /// </summary>
    public static int? WheelDelta(string kind, int mouseData) =>
        kind is WheelKind or HorizontalWheelKind ? (short)((mouseData >> 16) & 0xFFFF) : null;

    // A mouse message made by a pen or touch screen carries this signature
    // in its extra information, under this mask; the 0x80 bit then says
    // touch rather than pen
    // (https://learn.microsoft.com/en-us/windows/win32/tablet/system-events-and-mouse-messages).
    public const long PenOrTouchSignature = 0xFF515700;
    public const long PenOrTouchMask = 0xFFFFFF00;
    public const long TouchBit = 0x80;

    /// <summary>"touch" or "pen" for a mouse event made by a pen or touch screen, or null.</summary>
    public static string? PenOrTouch(long extraInformation) =>
        (extraInformation & PenOrTouchMask) == PenOrTouchSignature
            ? (extraInformation & TouchBit) != 0 ? "touch" : "pen"
            : null;

    // RAWMOUSE button flags
    // (https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse).
    private static readonly (int Flag, int Button, bool Up)[] RawButtonFlags =
    [
        (0x0001, LeftOrRightButton, false), (0x0002, LeftOrRightButton, true),
        (0x0004, LeftOrRightButton, false), (0x0008, LeftOrRightButton, true),
        (0x0010, MiddleButton, false), (0x0020, MiddleButton, true),
        (0x0040, X1Button, false), (0x0080, X1Button, true),
        (0x0100, X2Button, false), (0x0200, X2Button, true)
    ];

    // The buttons as the check that the hook is still installed matches
    // them. Left and right are one, as raw input gives the physical button
    // and whether the hook gives them swapped, when the buttons are
    // swapped in Windows, is not checked.
    public const int LeftOrRightButton = 1;
    public const int MiddleButton = 3;
    public const int X1Button = 4;
    public const int X2Button = 5;

    /// <summary>The button presses and releases of a raw input mouse record, as the loss check matches them.</summary>
    public static IEnumerable<(int Button, bool Up)> RawButtons(int buttonFlags)
    {
        foreach (var (flag, button, up) in RawButtonFlags)
        {
            if ((buttonFlags & flag) != 0)
            {
                yield return (button, up);
            }
        }
    }

    /// <summary>A hook button event as the loss check matches it, or null for a move or wheel.</summary>
    public static (int Button, bool Up)? HookButton(string kind, string? button) =>
        kind is ButtonDownKind or ButtonUpKind && button switch
        {
            "left" or "right" => LeftOrRightButton,
            "middle" => MiddleButton,
            "x1" => X1Button,
            "x2" => X2Button,
            _ => (int?)null
        } is { } code
            ? (code, kind == ButtonUpKind)
            : null;
}

/// <summary>The button presses and releases of a raw input mouse record, for the mouse hook's loss check.</summary>
public readonly record struct RawMouseObservation(long At, int ButtonFlags, bool FromDevice);
