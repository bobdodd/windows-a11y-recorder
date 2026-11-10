namespace Recorder.Contracts;

/// <summary>
/// The records of the Android touch collector, slice 5 of
/// docs/architecture/android-implementation-plan.md: the phone's raw input
/// events as <c>getevent</c> prints them, each touch report of its touch
/// screen in screen pixels, and its hardware keys.
///
/// <c>getevent</c> asks each input device for CLOCK_MONOTONIC times
/// (getevent.c, <c>EVIOCSCLOCKID</c>), the clock the companion app samples,
/// so each record is placed on the session clock through the companion
/// collector's clock fit, and otherwise at the time the recorder read it.
/// </summary>
public static class AndroidTouchRecords
{
    /// <summary>Every line <c>getevent</c> printed, a report at a time.</summary>
    public const string RawChannel = "input.android.raw";

    /// <summary>One record for each report of the touch screen while a finger is on it.</summary>
    public const string TouchChannel = "input.android.touch";

    /// <summary>One record for each press, release or repeat of a hardware key.</summary>
    public const string KeyChannel = "input.android.key";

    /// <summary>What <c>getevent -lp</c> printed at the start, and the touch screen chosen from it.</summary>
    public const string DevicesEventType = "input-devices";

    /// <summary>The lines of one report of one device, up to and including its SYN_REPORT, or one line that is not an event.</summary>
    public const string LinesEventType = "getevent-lines";

    /// <summary>Why the phone's input is missing from part or all of a recording.</summary>
    public const string UnavailableEventType = "input-unavailable";

    public const string TouchEventType = "touch-report";

    public const string KeyEventType = "hardware-key";

    /// <summary>The record's time is the phone time <c>getevent</c> printed for the event.</summary>
    public const string PhoneInputTimeFlag = "phone-input-time";

    /// <summary>
    /// No display state from the companion app was known for the report's
    /// time, so its position is given as if the phone were upright, on the
    /// screen size <c>wm size</c> gave.
    /// </summary>
    public const string RotationUnknownFlag = "rotation-unknown";

    public const string DownAction = "down";
    public const string MoveAction = "move";
    public const string UpAction = "up";

    /// <summary>
    /// Down when the finger's tracking id was set in the report, up when it
    /// was cleared, and move for a finger that stayed on the screen, whether
    /// or not its position changed.
    /// </summary>
    public static IReadOnlyList<string> Actions { get; } = [DownAction, MoveAction, UpAction];

    /// <summary>The values <c>getevent -l</c> prints for a key (getevent.c, key_value_labels).</summary>
    public static IReadOnlyList<string> KeyActions { get; } = ["down", "up", "repeat"];
}

/// <summary>An axis's range as <c>getevent -lp</c> reports it.</summary>
public sealed record AndroidAxisRange(int Minimum, int Maximum);

/// <summary>
/// The input devices at the start: every line <c>getevent -lp</c> printed,
/// the touch screen chosen from them, its axis ranges, and the screen's
/// size in its natural orientation from <c>wm size</c>. The touch screen
/// and its ranges are null when none was found.
/// </summary>
public sealed record AndroidInputDevices(
    string Serial,
    IReadOnlyList<string> Lines,
    string? TouchDevice,
    string? TouchName,
    AndroidAxisRange? X,
    AndroidAxisRange? Y,
    int? ScreenWidth,
    int? ScreenHeight);

/// <summary>
/// Lines as <c>getevent -lt</c> printed them: one device's report, or one
/// line that is not an event, such as a device being added.
/// </summary>
/// <param name="Device">The device the lines are from; null for a line that is not an event.</param>
/// <param name="PhoneMicroseconds">The time printed on the last line, in microseconds of the phone's CLOCK_MONOTONIC; null for a line that is not an event.</param>
/// <param name="ReceivedAtNanoseconds">The session time the recorder read the last line.</param>
public sealed record AndroidGeteventLines(
    string? Device,
    IReadOnlyList<string> Lines,
    long? PhoneMicroseconds,
    long ReceivedAtNanoseconds);

/// <summary>
/// One report of the touch screen, at its SYN_REPORT, with every finger on
/// the screen and every finger lifted in it.
/// </summary>
/// <param name="Rotation">The display's rotation, in degrees, that the positions were turned by.</param>
/// <param name="ScreenWidth">The screen's width as shown at that rotation, in pixels.</param>
/// <param name="ScreenHeight">The screen's height as shown at that rotation, in pixels.</param>
public sealed record AndroidTouchReport(
    string Device,
    long PhoneMicroseconds,
    long ReceivedAtNanoseconds,
    int Rotation,
    int ScreenWidth,
    int ScreenHeight,
    IReadOnlyList<AndroidTouchPointer> Pointers);

/// <summary>
/// One finger in a touch report. The raw position is in the panel's units;
/// the natural position is in screen pixels with the phone upright; the
/// position is on the screen as shown, turned by the display's rotation.
/// Pressure and sizes are in the panel's units, as last reported for the
/// finger, and null when the panel has not reported them.
/// </summary>
/// <param name="Slot">The multi-touch slot, counting from 0.</param>
/// <param name="TrackingId">The id the panel gave the finger when it went down.</param>
/// <param name="Action">down, move or up.</param>
public sealed record AndroidTouchPointer(
    int Slot,
    int TrackingId,
    string Action,
    int RawX,
    int RawY,
    double NaturalX,
    double NaturalY,
    double X,
    double Y,
    int? Pressure,
    int? TouchMajor,
    int? TouchMinor);

/// <summary>A hardware key, such as volume or power, as <c>getevent -l</c> names it.</summary>
/// <param name="Action">down, up or repeat.</param>
public sealed record AndroidHardwareKey(
    string Device,
    string? DeviceName,
    string Key,
    string Action,
    long PhoneMicroseconds,
    long ReceivedAtNanoseconds);

/// <summary>Why the phone's input is missing from part or all of a recording.</summary>
/// <param name="ConnectionLost">True when getevent ended during the recording.</param>
public sealed record AndroidInputUnavailable(string Serial, string Reason, bool ConnectionLost);
