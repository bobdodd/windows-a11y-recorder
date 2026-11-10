using System.Globalization;
using System.Text.RegularExpressions;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>One input device as <c>getevent -lp</c> describes it.</summary>
public sealed record GeteventDevice(
    string Path,
    string? Name,
    IReadOnlyDictionary<string, GeteventAxis> Axes,
    IReadOnlySet<string> Properties);

/// <summary>An absolute axis: its value when it was read, and its range.</summary>
public sealed record GeteventAxis(int Value, int Minimum, int Maximum);

/// <summary>
/// Reads what <c>getevent -lp</c> prints (android-implementation-plan.md,
/// slice 5): an "add device" line for each device, its name, the events it
/// can send with each absolute axis's value and range, and its input
/// properties (getevent.c, open_device, print_possible_events and
/// print_input_props).
/// </summary>
public static partial class GeteventDevices
{
    public static IReadOnlyList<GeteventDevice> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var devices = new List<GeteventDevice>();
        string? path = null;
        string? name = null;
        Dictionary<string, GeteventAxis> axes = [];
        HashSet<string> properties = [];
        var inProperties = false;

        void Add()
        {
            if (path is not null)
            {
                devices.Add(new GeteventDevice(path, name, axes, properties));
            }
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (AddDevice().Match(line) is { Success: true } added)
            {
                Add();
                path = added.Groups[1].Value;
                name = null;
                axes = [];
                properties = [];
                inProperties = false;
                continue;
            }

            if (path is null)
            {
                continue;
            }

            if (DeviceName().Match(line) is { Success: true } named)
            {
                name = named.Groups[1].Value;
            }
            else if (line.TrimStart().StartsWith("input props:", StringComparison.Ordinal))
            {
                inProperties = true;
            }
            else if (line.TrimStart().StartsWith("events:", StringComparison.Ordinal))
            {
                inProperties = false;
            }
            else if (Axis().Match(line) is { Success: true } axis)
            {
                axes[axis.Groups[1].Value] = new GeteventAxis(
                    int.Parse(axis.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(axis.Groups[3].Value, CultureInfo.InvariantCulture),
                    int.Parse(axis.Groups[4].Value, CultureInfo.InvariantCulture));
            }
            else if (inProperties && Property().Match(line) is { Success: true } property)
            {
                properties.Add(property.Groups[1].Value);
            }
        }

        Add();
        return devices;
    }

    /// <summary>
    /// The touch screen: the first device with both multi-touch position
    /// axes and the direct input property, which marks a screen rather than a
    /// touch pad (Linux, Documentation/input/event-codes.rst); failing that,
    /// the first with both axes, as the feasibility script chose it.
    /// </summary>
    public static GeteventDevice? TouchScreen(IReadOnlyList<GeteventDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        static bool Positions(GeteventDevice device) =>
            device.Axes.ContainsKey("ABS_MT_POSITION_X") && device.Axes.ContainsKey("ABS_MT_POSITION_Y");
        return devices.FirstOrDefault(device => Positions(device) && device.Properties.Contains("INPUT_PROP_DIRECT"))
            ?? devices.FirstOrDefault(Positions);
    }

    [GeneratedRegex(@"^add device \d+:\s+(\S+)")]
    private static partial Regex AddDevice();

    [GeneratedRegex("^\\s*name:\\s+\"(.*)\"\\s*$")]
    private static partial Regex DeviceName();

    [GeneratedRegex(@"(ABS_[A-Z0-9_]+)\s*\*?\s*:\s*value (-?\d+), min (-?\d+), max (-?\d+)")]
    private static partial Regex Axis();

    [GeneratedRegex(@"^\s+(INPUT_PROP_[A-Z0-9_]+)\s*$")]
    private static partial Regex Property();
}

/// <summary>
/// One event line of <c>getevent -lt</c>: the time, the device when
/// getevent was given none, and the type, code and value, labelled
/// (getevent.c, print_event). A value without a label is printed as eight
/// hex digits of a signed number.
/// </summary>
public sealed partial record GeteventEvent(long PhoneMicroseconds, string? Device, string Type, string Code, string Value)
{
    public long PhoneNanoseconds => PhoneMicroseconds * 1000;

    public bool IsReport => Type == "EV_SYN" && Code == "SYN_REPORT";

    /// <summary>The value as a number, or null for a label such as DOWN.</summary>
    public int? Number =>
        Value.Length == 8 && uint.TryParse(Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number)
            ? unchecked((int)number)
            : null;

    public static GeteventEvent? TryParse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var match = Line().Match(line.TrimEnd('\r'));
        if (!match.Success)
        {
            return null;
        }

        // getevent prints the microseconds as six digits.
        var seconds = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var micros = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return new GeteventEvent(
            seconds * 1_000_000 + micros,
            match.Groups[3].Success ? match.Groups[3].Value : null,
            match.Groups[4].Value,
            match.Groups[5].Value,
            match.Groups[6].Value);
    }

    [GeneratedRegex(@"^\[\s*(\d+)\.(\d{6})\]\s+(?:(/\S+):\s+)?(\S+)\s+(\S+)\s+(\S+)\s*$")]
    private static partial Regex Line();
}

/// <summary>A finger as the tracker has it at a report, in the panel's units.</summary>
public sealed record TrackedPointer(
    int Slot,
    int TrackingId,
    string Action,
    int X,
    int Y,
    int? Pressure,
    int? TouchMajor,
    int? TouchMinor);

/// <summary>
/// Follows the fingers of a multi-touch screen through its events, by the
/// Linux multi-touch protocol B: ABS_MT_SLOT chooses the slot the events
/// after it are for, a tracking id puts a finger in the slot and -1 takes it
/// out, and each axis keeps its last value until the panel reports a new
/// one (Linux, Documentation/input/multi-touch-protocol.rst). The panel
/// reports a position only when it changes, so a finger keeps its slot's
/// last position, as the feasibility script kept the last x and y.
/// </summary>
public sealed class TouchTracker(int initialSlot = 0)
{
    private readonly SortedDictionary<int, SlotState> _slots = [];
    private readonly HashSet<int> _down = [];
    private readonly Dictionary<int, SlotState> _up = [];
    private int _slot = initialSlot;

    /// <summary>Takes one event of the touch screen; at a SYN_REPORT, returns its fingers, or null when no finger was on the screen.</summary>
    public IReadOnlyList<TrackedPointer>? Add(GeteventEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.IsReport)
        {
            return Report();
        }

        if (input.Type != "EV_ABS" || input.Number is not { } value)
        {
            return null;
        }

        if (input.Code == "ABS_MT_SLOT")
        {
            _slot = value;
            return null;
        }

        var slot = Slot(_slot);
        switch (input.Code)
        {
            case "ABS_MT_TRACKING_ID":
                if (slot.TrackingId >= 0 && slot.TrackingId != value)
                {
                    // Up, or a new finger in a slot whose last finger was
                    // not seen to go up.
                    _up[_slot] = slot with { };
                    _down.Remove(_slot);
                }

                if (value >= 0 && value != slot.TrackingId)
                {
                    _down.Add(_slot);
                }

                slot.TrackingId = value;
                break;
            case "ABS_MT_POSITION_X":
                slot.X = value;
                break;
            case "ABS_MT_POSITION_Y":
                slot.Y = value;
                break;
            case "ABS_MT_PRESSURE":
                slot.Pressure = value;
                break;
            case "ABS_MT_TOUCH_MAJOR":
                slot.TouchMajor = value;
                break;
            case "ABS_MT_TOUCH_MINOR":
                slot.TouchMinor = value;
                break;
        }

        return null;
    }

    private IReadOnlyList<TrackedPointer>? Report()
    {
        var pointers = new List<TrackedPointer>();
        foreach (var (number, slot) in _slots)
        {
            if (_up.TryGetValue(number, out var lifted) && lifted.X is { } ux && lifted.Y is { } uy)
            {
                pointers.Add(new TrackedPointer(number, lifted.TrackingId, AndroidTouchRecords.UpAction, ux, uy, lifted.Pressure, lifted.TouchMajor, lifted.TouchMinor));
            }

            if (slot.TrackingId >= 0 && slot.X is { } x && slot.Y is { } y)
            {
                var action = _down.Contains(number) ? AndroidTouchRecords.DownAction : AndroidTouchRecords.MoveAction;
                pointers.Add(new TrackedPointer(number, slot.TrackingId, action, x, y, slot.Pressure, slot.TouchMajor, slot.TouchMinor));
            }
        }

        _down.Clear();
        _up.Clear();
        return pointers.Count == 0 ? null : pointers;
    }

    private SlotState Slot(int number)
    {
        if (!_slots.TryGetValue(number, out var slot))
        {
            slot = new SlotState();
            _slots[number] = slot;
        }

        return slot;
    }

    private sealed record SlotState
    {
        public int TrackingId { get; set; } = -1;

        public int? X { get; set; }

        public int? Y { get; set; }

        public int? Pressure { get; set; }

        public int? TouchMajor { get; set; }

        public int? TouchMinor { get; set; }
    }
}

/// <summary>
/// Turns the panel's units into screen pixels. Each axis is scaled from its
/// range, (value - min) * size / (max - min + 1), and the position is then
/// turned by the display's rotation as Android's input reader turns it:
/// at 90 degrees x and y swap and y is reversed, at 180 both are reversed,
/// and at 270 they swap and x is reversed (TouchInputMapper.cpp,
/// rotateAndScale).
/// </summary>
public static class TouchGeometry
{
    public static (double X, double Y) Natural(int rawX, int rawY, AndroidAxisRange x, AndroidAxisRange y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        return (
            Math.Round((double)(rawX - x.Minimum) * width / (x.Maximum - x.Minimum + 1), 1),
            Math.Round((double)(rawY - y.Minimum) * height / (y.Maximum - y.Minimum + 1), 1));
    }

    /// <summary>A natural position on the screen as shown, and the shown screen's size.</summary>
    public static (double X, double Y, int Width, int Height) Shown(double x, double y, int width, int height, int rotation) =>
        rotation switch
        {
            90 => (y, Math.Round(width - x, 1), height, width),
            180 => (Math.Round(width - x, 1), Math.Round(height - y, 1), width, height),
            270 => (Math.Round(height - y, 1), x, height, width),
            _ => (x, y, width, height)
        };
}
