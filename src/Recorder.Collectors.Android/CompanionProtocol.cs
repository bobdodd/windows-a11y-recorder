using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// The messages between the recorder and the companion app, one JSON object
/// per line, over the abstract local socket
/// <see cref="SocketName"/>, which the recorder reaches with adb forward
/// (android-implementation-plan.md, slice 2). The app's side is
/// android/companion/src/.../RecorderLink.java; the two change together, and
/// <see cref="Version"/> goes up when either side's messages change.
/// </summary>
public static class CompanionProtocol
{
    /// <summary>
    /// 2 from slice 4, step 4c, which added the Accessibility volume
    /// request; 3 from slice 7, step 7a, which added accessibility events;
    /// 4 from step 7b, which added snapshots of the accessibility tree; 5
    /// from slice 8, step 8a, which added notices of settings changes; 6
    /// from android-checks.md, step 4a, which added screenshots.
    /// </summary>
    public const int Version = 6;

    /// <summary>The companion app's socket, in Linux's abstract namespace.</summary>
    public const string SocketName = "a11yrecorder_companion";

    public const string ServiceClass = AndroidTools.CompanionPackage + ".RecorderAccessibilityService";

    /// <summary>The service as Android names it in enabled_accessibility_services.</summary>
    public const string ServiceComponent = AndroidTools.CompanionPackage + "/" + ServiceClass;

    /// <summary>A clock request; the app answers with the same id.</summary>
    public static string ClockRequest(long id) =>
        "{\"type\":\"clock\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + "}\n";

    /// <summary>
    /// Asks the app to raise the phone's Accessibility volume to at least
    /// this share of its largest step, now and whenever it becomes separate
    /// from the media volume while the connection lasts.
    /// </summary>
    public static string AccessibilityVolumeRequest(int minimumPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumPercent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumPercent, 100);
        return "{\"type\":\"accessibility-volume\",\"minimumPercent\":" +
            minimumPercent.ToString(CultureInfo.InvariantCulture) + "}\n";
    }

    /// <summary>
    /// Asks the app to send this connection every accessibility event its
    /// service receives from now on, each as an "accessibility-event" line.
    /// </summary>
    public const string AccessibilityEventsRequest = "{\"type\":\"accessibility-events\"}\n";

    /// <summary>
    /// Asks the app for a snapshot of every window's accessibility tree now,
    /// and more on each window or content change, each as an
    /// "accessibility-tree" line.
    /// </summary>
    public const string AccessibilityTreesRequest = "{\"type\":\"accessibility-trees\"}\n";

    /// <summary>
    /// Asks the app for a screenshot of the display now, and another each
    /// time a window has had no change for half a second, each as a
    /// "screenshot" line, or a "screenshot-failed" line when Android
    /// refuses one.
    /// </summary>
    public const string ScreenshotsRequest = "{\"type\":\"screenshots\"}\n";

    /// <summary>
    /// Asks the app to tell this connection, from now on, each time one of
    /// these settings is written, each as a "setting-changed" line. The app
    /// says only which setting and when; the recorder reads the value.
    /// </summary>
    public static string SettingsRequest(IEnumerable<AndroidSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var request = new
        {
            type = "settings",
            settings = settings.Select(setting => new { @namespace = setting.Namespace, name = setting.Name }).ToArray()
        };
        return JsonSerializer.Serialize(request) + "\n";
    }

    // The app's names are the records' names (AccessibilityEventJson.java).
    private static readonly JsonSerializerOptions EventOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads one line from the app. A line that is not a JSON object with a
    /// type, or a known type without its fields, is a
    /// <see cref="FormatException"/>.
    /// </summary>
    public static CompanionMessage Parse(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The companion app sent a line that is not JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The companion app sent a line that is not a JSON object.");
            }

            var type = String(root, "type");
            return type switch
            {
                "hello" => new CompanionHello(
                    Int(root, "protocol"),
                    String(root, "versionName"),
                    Long(root, "versionCode"),
                    String(root, "androidRelease"),
                    Int(root, "sdk"),
                    Long(root, "time")),
                "clock" => new CompanionClockReply(
                    Long(root, "id"),
                    Long(root, "received"),
                    Long(root, "sent")),
                "display" => new CompanionDisplay(
                    String(root, "reason"),
                    Int(root, "rotation"),
                    Int(root, "modeWidth"),
                    Int(root, "modeHeight"),
                    Long(root, "time")),
                "accessibility-volume" => new CompanionAccessibilityVolume(
                    String(root, "reason"),
                    Int(root, "minimumPercent"),
                    Bool(root, "separate"),
                    Strings(root, "services"),
                    OptionalInt(root, "max"),
                    OptionalInt(root, "before"),
                    OptionalInt(root, "target"),
                    OptionalInt(root, "after"),
                    String(root, "outcome"),
                    root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String ? detail.GetString() : null,
                    Long(root, "time")),
                "accessibility-event" => AccessibilityEvent(root),
                // Read later, off the connection's reading, by ReadTree:
                // a snapshot can be large, and the clock replies behind it
                // are timed as they are read.
                "accessibility-tree" => new CompanionAccessibilityTreeLine(line),
                // Read later, off the connection's reading, by
                // ReadScreenshot, as for a snapshot: a screenshot is large.
                "screenshot" or "screenshot-failed" => new CompanionScreenshotLine(line),
                "setting-changed" => new CompanionSettingChanged(
                    String(root, "namespace"),
                    String(root, "name"),
                    Long(root, "time")),
                "error" => new CompanionError(String(root, "message")),
                _ => new CompanionUnknown(type)
            };
        }
    }

    // An event that cannot be read is told, not thrown, so that one such
    // event does not end the connection and with it the phone's clock.
    private static CompanionAccessibilityEvent AccessibilityEvent(JsonElement root)
    {
        try
        {
            return root.Deserialize<AndroidAccessibilityEvent>(EventOptions) is { } read
                ? new CompanionAccessibilityEvent(read, null)
                : new CompanionAccessibilityEvent(null, "the event is empty");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return new CompanionAccessibilityEvent(null, ex.Message);
        }
    }

    /// <summary>
    /// Reads a snapshot's line. One that cannot be read is told, not
    /// thrown, as for an event.
    /// </summary>
    public static (AndroidAccessibilityTree? Tree, string? Problem) ReadTree(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            return JsonSerializer.Deserialize<AndroidAccessibilityTree>(line, EventOptions) is { } read
                ? (read with { MessageBytes = System.Text.Encoding.UTF8.GetByteCount(line) }, null)
                : (null, "the snapshot is empty");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Reads a screenshot's line: a screenshot, with its PNG, or a refusal.
    /// One that cannot be read is told, not thrown, as for an event.
    /// </summary>
    public static (CompanionScreenshotMessage? Message, string? Problem) ReadScreenshot(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "the line is not a JSON object");
            }

            var index = Long(root, "index");
            var reason = String(root, "reason");
            var requested = Long(root, "requested");
            if (String(root, "type") == "screenshot-failed")
            {
                return (new CompanionScreenshotRefusal(index, reason, requested, Int(root, "code"), Long(root, "failed")), null);
            }

            var png = Member(root, "png");
            if (png.ValueKind != JsonValueKind.String || !png.TryGetBytesFromBase64(out var bytes) || bytes.Length == 0)
            {
                return (null, "its PNG is not base64");
            }

            var lastChange = Long(root, "lastChangeMillis");
            return (new CompanionScreenshot(
                index,
                reason,
                requested,
                Long(root, "timestampMillis"),
                Long(root, "captured"),
                Long(root, "encoded"),
                lastChange < 0 ? null : lastChange,
                Int(root, "width"),
                Int(root, "height"),
                String(root, "colourSpace"),
                Bool(root, "converted"),
                bytes), null);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return (null, ex.Message);
        }
    }

    private static JsonElement Member(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            ? value
            : throw new FormatException($"The companion app's message has no \"{name}\".");

    private static string String(JsonElement root, string name)
    {
        var value = Member(root, name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new FormatException($"The companion app's \"{name}\" is not text.");
    }

    private static long Long(JsonElement root, string name)
    {
        var value = Member(root, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : throw new FormatException($"The companion app's \"{name}\" is not a whole number.");
    }

    private static bool Bool(JsonElement root, string name)
    {
        var value = Member(root, name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException($"The companion app's \"{name}\" is not true or false.")
        };
    }

    private static IReadOnlyList<string> Strings(JsonElement root, string name)
    {
        var value = Member(root, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"The companion app's \"{name}\" is not a list.");
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new FormatException($"The companion app's \"{name}\" holds something that is not text."))
            .ToArray();
    }

    private static int? OptionalInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out _) ? Int(root, name) : null;

    private static int Int(JsonElement root, string name)
    {
        var value = Member(root, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new FormatException($"The companion app's \"{name}\" is not a whole number.");
    }
}

public abstract record CompanionMessage;

/// <summary>Sent first on every connection.</summary>
/// <param name="Time">The phone's CLOCK_MONOTONIC when it was sent, in nanoseconds.</param>
public sealed record CompanionHello(
    int Protocol,
    string VersionName,
    long VersionCode,
    string AndroidRelease,
    int Sdk,
    long Time) : CompanionMessage;

/// <summary>The phone's CLOCK_MONOTONIC when the request was read and when the reply was written.</summary>
public sealed record CompanionClockReply(long Id, long Received, long Sent) : CompanionMessage;

/// <summary>
/// The phone display's rotation in degrees and its display mode's size in
/// pixels, sent after the greeting and whenever either changes.
/// </summary>
public sealed record CompanionDisplay(
    string Reason,
    int Rotation,
    int ModeWidth,
    int ModeHeight,
    long Time) : CompanionMessage;

/// <summary>
/// What the app found and did when asked to raise the Accessibility volume
/// (RecorderLink.java and AccessibilityVolume.java), in volume steps.
/// </summary>
/// <param name="Reason">"requested", or "services-changed" when the volume became separate or stopped being separate.</param>
/// <param name="Separate">True when a service that asks for a separate Accessibility volume, such as TalkBack, is on.</param>
/// <param name="Services">Those services, as Android names them.</param>
/// <param name="Outcome">
/// "raised"; "already", at or above the target; "not-separate", left alone
/// as it is the media volume; "refused", Android did not change it; or
/// "failed", the phone has no audio service.
/// </param>
/// <param name="Time">The phone's CLOCK_MONOTONIC when it was sent, in nanoseconds.</param>
public sealed record CompanionAccessibilityVolume(
    string Reason,
    int MinimumPercent,
    bool Separate,
    IReadOnlyList<string> Services,
    int? Max,
    int? Before,
    int? Target,
    int? After,
    string Outcome,
    string? Detail,
    long Time) : CompanionMessage;

/// <summary>
/// An accessibility event, sent to a connection that asked for them (slice
/// 7, step 7a); <see cref="AndroidAccessibilityEvent.ReceivedAtNanoseconds"/>
/// is not yet set. Null, with the problem, for one the recorder could not read.
/// </summary>
public sealed record CompanionAccessibilityEvent(AndroidAccessibilityEvent? Event, string? Problem) : CompanionMessage;

/// <summary>
/// A snapshot of the accessibility tree, sent to a connection that asked
/// for them (slice 7, step 7b), as the line the app sent; read it with
/// <see cref="CompanionProtocol.ReadTree"/>.
/// </summary>
public sealed record CompanionAccessibilityTreeLine(string Line) : CompanionMessage;

/// <summary>
/// A screenshot or a refusal, sent to a connection that asked for them
/// (android-checks.md, step 4a), as the line the app sent; read it with
/// <see cref="CompanionProtocol.ReadScreenshot"/>.
/// </summary>
public sealed record CompanionScreenshotLine(string Line) : CompanionMessage;

/// <summary>A screenshot or a refusal, as read from its line.</summary>
/// <param name="Index">The app's count of screenshots and refusals on the connection, from 1.</param>
/// <param name="Reason">"requested" or "settled".</param>
/// <param name="Requested">The phone's CLOCK_MONOTONIC when the app asked, in nanoseconds.</param>
public abstract record CompanionScreenshotMessage(long Index, string Reason, long Requested);

/// <summary>A screenshot, with its PNG, sRGB.</summary>
/// <param name="TimestampMillis">Android's time for it, SystemClock.uptimeMillis().</param>
/// <param name="Captured">The phone's CLOCK_MONOTONIC when Android answered, in nanoseconds.</param>
/// <param name="Encoded">The phone's CLOCK_MONOTONIC when the PNG was written, in nanoseconds.</param>
/// <param name="LastChangeMillis">The last change the app saw before it asked, SystemClock.uptimeMillis(); null before any.</param>
/// <param name="ColourSpace">The colour space Android gave it.</param>
/// <param name="Converted">True when the app converted it to sRGB.</param>
public sealed record CompanionScreenshot(
    long Index,
    string Reason,
    long Requested,
    long TimestampMillis,
    long Captured,
    long Encoded,
    long? LastChangeMillis,
    int Width,
    int Height,
    string ColourSpace,
    bool Converted,
    byte[] Png) : CompanionScreenshotMessage(Index, Reason, Requested);

/// <summary>A screenshot Android refused, with its code (AccessibilityService, ERROR_TAKE_SCREENSHOT_*).</summary>
/// <param name="Failed">The phone's CLOCK_MONOTONIC when Android refused, in nanoseconds.</param>
public sealed record CompanionScreenshotRefusal(
    long Index,
    string Reason,
    long Requested,
    int Code,
    long Failed) : CompanionScreenshotMessage(Index, Reason, Requested);

/// <summary>
/// A setting the connection asked about was written (slice 8, step 8a),
/// which may or may not have changed its value.
/// </summary>
/// <param name="Namespace">secure, system or global.</param>
/// <param name="Time">The phone's CLOCK_MONOTONIC when the app was told, in nanoseconds.</param>
public sealed record CompanionSettingChanged(string Namespace, string Name, long Time) : CompanionMessage;

public sealed record CompanionError(string Message) : CompanionMessage;

/// <summary>A message of a type this recorder does not know, from a newer companion app.</summary>
public sealed record CompanionUnknown(string Type) : CompanionMessage;
