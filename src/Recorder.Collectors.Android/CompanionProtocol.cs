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
    /// request; 3 from slice 7, step 7a, which added accessibility events.
    /// </summary>
    public const int Version = 3;

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

public sealed record CompanionError(string Message) : CompanionMessage;

/// <summary>A message of a type this recorder does not know, from a newer companion app.</summary>
public sealed record CompanionUnknown(string Type) : CompanionMessage;
