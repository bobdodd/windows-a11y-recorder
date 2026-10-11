using System.Globalization;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// A window snapshot as playback keeps it: when it was read, from which
/// process, why, the window, how many elements it held, whether the read
/// stopped at a limit, and a summary of what the window showed. The whole
/// tree stays in the recording, for Selected event values.
/// </summary>
public sealed record WindowSnapshot(
    long Time,
    int? ProcessId,
    string Reason,
    string? WindowName,
    int ElementCount,
    string? Truncated,
    string Summary)
{
    /// <summary>The timeline event of the record, where playback has it.</summary>
    public SessionTimelineEvent? Event { get; init; }
}

/// <summary>
/// Reads window snapshot records (screen-reader-activity.md, "2c-4, what
/// NVDA's windows show"): the compact form the playback index keeps, the
/// summary the NVDA window row gives, and the outline Selected event values
/// gives before the record.
/// </summary>
public static class WindowSnapshots
{
    private const int MostNamed = 3;

    // The control types that are a window's items, counted in the summary.
    private static readonly HashSet<string> ItemTypes = new(StringComparer.Ordinal)
    {
        "ControlType.TreeItem", "ControlType.ListItem", "ControlType.MenuItem", "ControlType.DataItem",
        "ControlType.TabItem", "ControlType.RadioButton", "ControlType.CheckBox"
    };

    public static bool IsSnapshot(string channel, string eventType) =>
        channel == WindowSnapshotRecords.Channel && eventType == WindowSnapshotRecords.EventType;

    /// <summary>
    /// The snapshot without its elements, with the summary in their place;
    /// a compact form is given back as it is.
    /// </summary>
    public static JsonElement Compact(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("elements", out _))
        {
            return payload.ValueKind == JsonValueKind.Object ? payload.Clone() : default;
        }

        var buffer = new System.Buffers.ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in payload.EnumerateObject())
            {
                if (property.Name is "reason" or "processId" or "elementCount" or "truncated")
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteString("windowName", WindowName(payload));
            writer.WriteString("summary", Summary(payload));
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    /// <summary>A snapshot from a record, full or compact; null for a record of another shape.</summary>
    public static WindowSnapshot? Read(long time, JsonElement payload)
    {
        var compact = Compact(payload);
        if (compact.ValueKind != JsonValueKind.Object ||
            !compact.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return new WindowSnapshot(
            time,
            compact.TryGetProperty("processId", out var process) && process.ValueKind == JsonValueKind.Number &&
                process.TryGetInt32(out var id) ? id : null,
            reason.GetString()!,
            Text(compact, "windowName"),
            compact.TryGetProperty("elementCount", out var count) && count.ValueKind == JsonValueKind.Number &&
                count.TryGetInt32(out var elements) ? elements : 0,
            Text(compact, "truncated"),
            Text(compact, "summary") ?? string.Empty);
    }

    /// <summary>
    /// What the window showed, in a line: how many of each kind of item,
    /// which were selected, each edit's or combo box's value, and where the
    /// focus was; and whether the read stopped at a limit.
    /// </summary>
    public static string Summary(JsonElement payload)
    {
        var elements = Elements(payload);
        var parts = new List<string>();
        var counts = elements.Skip(1)
            .Where(element => Text(element, "controlType") is { } type && ItemTypes.Contains(type))
            .GroupBy(element => Text(element, "localizedControlType") ?? ControlName(Text(element, "controlType")))
            .Select(group => $"{group.Count().ToString("N0", CultureInfo.InvariantCulture)} {Plural(group.Key, group.Count())}")
            .ToList();
        parts.Add(counts.Count == 0
            ? $"{elements.Count.ToString("N0", CultureInfo.InvariantCulture)} elements"
            : string.Join(", ", counts));

        var selected = elements.Skip(1).Where(element => Bool(element, "isSelected") == true).ToList();
        if (selected.Count > 0)
        {
            parts.Add("selected: " + Named(selected));
        }

        var on = elements.Skip(1).Where(element => Text(element, "toggleState") == "On").ToList();
        if (on.Count > 0)
        {
            parts.Add("checked: " + Named(on));
        }

        foreach (var element in elements.Skip(1).Where(element =>
                     Text(element, "controlType") is "ControlType.Edit" or "ControlType.ComboBox" &&
                     Text(element, "value") is not null))
        {
            var label = Text(element, "name") is { Length: > 0 } name ? name : ControlName(Text(element, "controlType"));
            parts.Add($"{label} \"{Text(element, "value")}\"");
        }

        if (elements.Skip(1).FirstOrDefault(element => Bool(element, "hasKeyboardFocus") == true) is { ValueKind: JsonValueKind.Object } focused)
        {
            parts.Add("focus: " + Describe(focused));
        }

        var text = string.Join("; ", parts);
        return Text(payload, "truncated") switch
        {
            WindowSnapshotRecords.ElementLimit => text + $" (read stopped at {WindowSnapshotRecords.MaximumElements:N0} elements)",
            WindowSnapshotRecords.TimeLimit => text + " (read stopped at 2 seconds)",
            _ => text
        };
    }

    /// <summary>
    /// The snapshot as an indented outline, for Selected event values: a
    /// heading, then each element under its parent with its type and
    /// states, then any read problems.
    /// </summary>
    public static string Outline(JsonElement payload)
    {
        var elements = Elements(payload);
        var text = new StringBuilder();
        var read = payload.TryGetProperty("readNanoseconds", out var nanoseconds) && nanoseconds.TryGetInt64(out var value)
            ? $", read in {(value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture)} ms"
            : string.Empty;
        text.Append(CultureInfo.InvariantCulture, $"What the window showed: {elements.Count:N0} elements{read}, {ReasonText(Text(payload, "reason"))}");
        text.AppendLine(Text(payload, "truncated") switch
        {
            WindowSnapshotRecords.ElementLimit => $"; the read stopped at the limit of {WindowSnapshotRecords.MaximumElements:N0} elements, so later elements are not recorded",
            WindowSnapshotRecords.TimeLimit => "; the read stopped at the limit of 2 seconds, so later elements are not recorded",
            _ => string.Empty
        });
        foreach (var element in elements)
        {
            var depth = element.TryGetProperty("depth", out var level) && level.TryGetInt32(out var number) ? number : 0;
            text.Append(' ', 2 * depth).AppendLine(Describe(element));
        }

        if (payload.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array &&
            problems.GetArrayLength() > 0)
        {
            text.AppendLine("Reads that failed:");
            foreach (var problem in problems.EnumerateArray())
            {
                text.Append("  ").AppendLine(problem.GetString());
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Why a snapshot was read, in words.</summary>
    public static string ReasonText(string? reason) => reason switch
    {
        WindowSnapshotRecords.FocusEnteredReason => "read when focus entered the window",
        WindowSnapshotRecords.WindowChangedReason => "read when the window had been quiet for 300 ms after it changed",
        WindowSnapshotRecords.FocusedElementNewReason => "read when focus moved to an element the last read did not hold",
        WindowSnapshotRecords.FocusedElementChangedReason => "read when focus moved to an element whose state had changed",
        _ => $"read for an unknown reason ({reason ?? "none"})"
    };

    // An element: its name in quotes, its type, and its states.
    private static string Describe(JsonElement element)
    {
        var name = Text(element, "name");
        var type = Text(element, "localizedControlType") is { Length: > 0 } localized
            ? localized
            : ControlName(Text(element, "controlType"));
        var states = new List<string>();
        if (Text(element, "value") is { } value)
        {
            states.Add($"value \"{value}\"");
        }

        if (Bool(element, "isPassword") == true)
        {
            states.Add("password, value not recorded");
        }

        if (Text(element, "toggleState") is { } toggle)
        {
            states.Add(toggle switch { "On" => "checked", "Off" => "not checked", _ => toggle.ToLowerInvariant() });
        }

        if (Bool(element, "isSelected") == true)
        {
            states.Add("selected");
        }

        if (Text(element, "expandCollapseState") is { } expand && expand != "LeafNode")
        {
            states.Add(expand switch
            {
                "Expanded" => "expanded",
                "Collapsed" => "collapsed",
                "PartiallyExpanded" => "partly expanded",
                _ => expand
            });
        }

        if (Bool(element, "hasKeyboardFocus") == true)
        {
            states.Add("focused");
        }

        if (Bool(element, "isEnabled") == false)
        {
            states.Add("unavailable");
        }

        if (Bool(element, "isOffscreen") == true)
        {
            states.Add("off screen");
        }

        return (string.IsNullOrEmpty(name) ? "(no name)" : $"\"{name}\"") +
            $" ({string.Join(", ", [type, .. states])})";
    }

    private static string Named(List<JsonElement> elements)
    {
        var names = elements.Take(MostNamed).Select(Describe).ToList();
        return elements.Count > MostNamed
            ? string.Join(", ", names) + $" and {elements.Count - MostNamed:N0} more"
            : string.Join(", ", names);
    }

    private static string? WindowName(JsonElement payload) =>
        Elements(payload) is { Count: > 0 } elements ? Text(elements[0], "name") : null;

    private static List<JsonElement> Elements(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array
            ? [.. elements.EnumerateArray().Where(element => element.ValueKind == JsonValueKind.Object)]
            : [];

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    private static string Plural(string word, int count) =>
        count == 1 ? word : word.EndsWith('x') || word.EndsWith('s') ? word + "es" : word + "s";

    private static string ControlName(string? controlType)
    {
        var name = controlType is { } type && type.StartsWith("ControlType.", StringComparison.Ordinal)
            ? type["ControlType.".Length..]
            : controlType ?? "element";
        var words = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsUpper(character) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(char.ToLowerInvariant(character));
        }

        return words.ToString();
    }
}
