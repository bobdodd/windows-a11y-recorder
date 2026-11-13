using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// The phone's touch reports (input.android.touch, touch-report, Android
/// slice 5), for drawing the fingers on the phone's picture in the player
/// (step 5b): which fingers were on the screen at a time, where, and the
/// path each took just before it. Positions are on the screen as shown,
/// in the pixels of the screen size each report gives, so a report made
/// while the phone was sideways has a sideways size. Empty for a
/// recording without touch reports.
/// </summary>
public sealed class PhoneTouchTimeline
{
    /// <summary>How much of each finger's path is drawn: the last second.</summary>
    public static readonly long PathNanoseconds = 1_000_000_000;

    private readonly long[] _times;
    private readonly Report[] _reports;
    private readonly Pointer[] _pointers;

    public PhoneTouchTimeline(IEnumerable<(long Time, JsonElement Payload)> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var reports = new List<(long Time, Report Report)>();
        var pointers = new List<Pointer>();
        foreach (var (time, payload) in records)
        {
            if (payload.ValueKind != JsonValueKind.Object ||
                ReadInt32(payload, "screenWidth") is not { } width || width <= 0 ||
                ReadInt32(payload, "screenHeight") is not { } height || height <= 0 ||
                !payload.TryGetProperty("pointers", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var start = pointers.Count;
            foreach (var pointer in list.EnumerateArray())
            {
                if (pointer.ValueKind != JsonValueKind.Object ||
                    ReadInt32(pointer, "slot") is not { } slot ||
                    ReadInt32(pointer, "trackingId") is not { } trackingId ||
                    ReadDouble(pointer, "x") is not { } x ||
                    ReadDouble(pointer, "y") is not { } y)
                {
                    continue;
                }

                var up = pointer.TryGetProperty("action", out var action) &&
                    action.ValueKind == JsonValueKind.String &&
                    action.GetString() == AndroidTouchRecords.UpAction;
                pointers.Add(new Pointer(slot, trackingId, up, (float)x, (float)y));
            }

            reports.Add((time, new Report(
                ReadInt32(payload, "rotation") ?? 0, width, height, start, pointers.Count - start)));
        }

        // Records are added in the order they were stored, which for one
        // collector is time order; a stable sort keeps that order for equal
        // times.
        var ordered = reports
            .Select((item, index) => (item.Time, item.Report, Index: index))
            .OrderBy(item => item.Time)
            .ThenBy(item => item.Index)
            .ToArray();
        _times = ordered.Select(item => item.Time).ToArray();
        _reports = ordered.Select(item => item.Report).ToArray();
        _pointers = pointers.ToArray();
    }

    public static PhoneTouchTimeline Empty { get; } = new([]);

    /// <summary>Whether the recording holds any touch report.</summary>
    public bool Recorded => _times.Length > 0;

    /// <summary>The number of touch reports.</summary>
    public int Count => _times.Length;

    /// <summary>
    /// Whether a finger touched the screen after one time and up to
    /// another: a report between them, or a finger down at the first. The
    /// status message check (android-checks.md, step 12) tells text that
    /// went away by itself from text a touch took away.
    /// </summary>
    public bool TouchedBetween(long from, long to)
    {
        var index = Array.BinarySearch(_times, from);
        index = index < 0 ? ~index : index;
        while (index < _times.Length && _times[index] <= from)
        {
            index++;
        }

        return (index < _times.Length && _times[index] <= to) || At(from) is { Fingers.Count: > 0 };
    }

    public static bool IsTouchReport(string channel, string eventType) =>
        channel == AndroidTouchRecords.TouchChannel &&
        eventType == AndroidTouchRecords.TouchEventType;

    /// <summary>
    /// What the player reads of a touch report, for the playback index:
    /// its rotation and screen size, and each finger's slot, tracking id,
    /// action and position. The positions in the panel's units and on the
    /// upright screen, and the pressure and sizes, are read from the
    /// recording.
    /// </summary>
    public static JsonElement Compact(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var name in (ReadOnlySpan<string>)["rotation", "screenWidth", "screenHeight"])
            {
                if (payload.TryGetProperty(name, out var value))
                {
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                }
            }

            if (payload.TryGetProperty("pointers", out var pointers) &&
                pointers.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray("pointers");
                foreach (var pointer in pointers.EnumerateArray())
                {
                    if (pointer.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    writer.WriteStartObject();
                    foreach (var name in (ReadOnlySpan<string>)["slot", "trackingId", "action", "x", "y"])
                    {
                        if (pointer.TryGetProperty(name, out var value))
                        {
                            writer.WritePropertyName(name);
                            value.WriteTo(writer);
                        }
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// The fingers on the screen at a time: those down or moving in the
    /// last report at or before it. A finger lifted in that report, and
    /// any time before the first report or after a report with no finger
    /// down, has none. Null when there is no report at or before the time.
    /// </summary>
    public PhoneTouches? At(long time)
    {
        var index = Array.BinarySearch(_times, time);
        if (index < 0)
        {
            index = ~index - 1;
        }
        else
        {
            // The last report at that time.
            while (index + 1 < _times.Length && _times[index + 1] == time)
            {
                index++;
            }
        }

        if (index < 0)
        {
            return null;
        }

        var report = _reports[index];
        var fingers = new List<PhoneFinger>();
        for (var i = report.Start; i < report.Start + report.Count; i++)
        {
            var pointer = _pointers[i];
            if (pointer.Up)
            {
                continue;
            }

            fingers.Add(new PhoneFinger(
                0, pointer.Slot, pointer.TrackingId, pointer.X, pointer.Y,
                PathOf(index, pointer.TrackingId, time - PathNanoseconds)));
        }

        // Numbered in the order the fingers went down: the panel gives each
        // new finger a larger tracking id.
        var numbered = fingers
            .OrderBy(finger => finger.TrackingId)
            .ThenBy(finger => finger.Slot)
            .Select((finger, order) => finger with { Number = order + 1 })
            .ToArray();
        return new PhoneTouches(_times[index], report.Rotation, report.ScreenWidth, report.ScreenHeight, numbered);
    }

    // The finger's positions from the earliest report at or after the
    // start in which it was on the screen, up to and including the report
    // at the index, oldest first. A report without the finger ends it.
    private IReadOnlyList<PhoneTouchPoint> PathOf(int index, int trackingId, long start)
    {
        var path = new List<PhoneTouchPoint>();
        for (var i = index; i >= 0 && _times[i] >= start; i--)
        {
            var report = _reports[i];
            Pointer? found = null;
            for (var p = report.Start; p < report.Start + report.Count; p++)
            {
                if (_pointers[p].TrackingId == trackingId && !_pointers[p].Up)
                {
                    found = _pointers[p];
                    break;
                }
            }

            if (found is not { } pointer)
            {
                break;
            }

            path.Add(new PhoneTouchPoint(_times[i], pointer.X, pointer.Y));
        }

        path.Reverse();
        return path;
    }

    private static int? ReadInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static double? ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number)
            ? number
            : null;

    private readonly record struct Report(int Rotation, int ScreenWidth, int ScreenHeight, int Start, int Count);

    private readonly record struct Pointer(int Slot, int TrackingId, bool Up, float X, float Y);
}

/// <summary>
/// The fingers on the phone's screen at a time, from the touch report at
/// <see cref="Time"/>, with the rotation and the size of the screen as
/// shown that the positions are on.
/// </summary>
public sealed record PhoneTouches(
    long Time,
    int Rotation,
    int ScreenWidth,
    int ScreenHeight,
    IReadOnlyList<PhoneFinger> Fingers)
{
    /// <summary>
    /// The fingers' positions and paths scaled from the screen size to a
    /// picture's size, for drawing them on a phone frame whose size differs
    /// from the screen's.
    /// </summary>
    public IReadOnlyList<PhoneFinger> ScaledTo(double width, double height)
    {
        var scaleX = width / ScreenWidth;
        var scaleY = height / ScreenHeight;
        return Fingers
            .Select(finger => finger with
            {
                X = finger.X * scaleX,
                Y = finger.Y * scaleY,
                Path = finger.Path
                    .Select(point => point with { X = point.X * scaleX, Y = point.Y * scaleY })
                    .ToArray()
            })
            .ToArray();
    }

    /// <summary>
    /// The fingers in words, for the player's help text: for example "1
    /// finger on the screen at x 603, y 171", or "2 fingers on the screen,
    /// 1 at x 301, y 1459, 2 at x 702, y 1462", in screen pixels; empty
    /// with no finger down.
    /// </summary>
    public string Describe()
    {
        static string At(PhoneFinger finger) =>
            string.Create(
                System.Globalization.CultureInfo.CurrentCulture,
                $"x {Math.Round(finger.X):0}, y {Math.Round(finger.Y):0}");

        return Fingers.Count switch
        {
            0 => string.Empty,
            1 => $"1 finger on the screen at {At(Fingers[0])}",
            _ => $"{Fingers.Count} fingers on the screen, " +
                string.Join(", ", Fingers.Select(finger => $"{finger.Number} at {At(finger)}"))
        };
    }
}

/// <summary>
/// One finger on the phone's screen, numbered from 1 in the order the
/// fingers went down, with its path over the last
/// <see cref="PhoneTouchTimeline.PathNanoseconds"/>, oldest first, ending
/// at its position.
/// </summary>
public sealed record PhoneFinger(
    int Number,
    int Slot,
    int TrackingId,
    double X,
    double Y,
    IReadOnlyList<PhoneTouchPoint> Path);

/// <summary>A finger's position in one touch report.</summary>
public sealed record PhoneTouchPoint(long Time, double X, double Y);
