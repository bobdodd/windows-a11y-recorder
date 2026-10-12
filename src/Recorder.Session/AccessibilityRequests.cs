using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>What an accessibility request record shows: one line, and the records it is joined with.</summary>
public sealed record AccessibilityRequestOutcome(string Text, IReadOnlyList<string> Details);

/// <summary>
/// The requests an assistive technology made of a page through the platform
/// accessibility API, and how Chromium answered them, from protocol 0.60's
/// records on the <c>browser.accessibility</c> channel: each record's
/// outcome, for Selected event values. A hit test is joined with the page's
/// precise result that follows it, and an action the browser process sent
/// with the renderer's receipt of it. Chromium cannot tell which program made
/// a request. See docs/architecture/accessibility-requests.md.
/// </summary>
public sealed class AccessibilityRequests
{
    public const int RuleVersion = 1;

    /// <summary>The longest a precise result or a receipt is joined after its request.</summary>
    public const long JoinWindowNanoseconds = 1_000_000_000;

    public const string Channel = BrowserEvidenceChannels.Accessibility;

    private static readonly HashSet<string> EventTypes = new(StringComparer.Ordinal)
    {
        BrowserEvidenceEventTypes.AccessibilityHitTest,
        BrowserEvidenceEventTypes.AccessibilityHitTestResult,
        BrowserEvidenceEventTypes.AccessibilityActionSent,
        BrowserEvidenceEventTypes.AccessibilityActionReceived,
        BrowserEvidenceEventTypes.AccessibilityHitTestComputed
    };

    private const string AnyProgram =
        "Chromium cannot tell which program asked: any program using Microsoft Active Accessibility, " +
        "IAccessible2, or UI Automation can.";

    private readonly Dictionary<string, AccessibilityRequestOutcome> _outcomes = new(StringComparer.Ordinal);

    private sealed record Node(string TreeId, long NodeId, string Role, string Name, string Bounds);

    private sealed record Item(string EventId, long Time, string EventType, JsonElement Payload);

    public AccessibilityRequests(IEnumerable<KeyEvidenceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var items = records
            .Where(record => IsRequestEvidence(record.Channel, record.EventType) &&
                record.Payload.ValueKind == JsonValueKind.Object)
            .OrderBy(record => record.MonotonicNanoseconds)
            .Select(record => new Item(record.EventId, record.MonotonicNanoseconds, record.EventType, record.Payload))
            .ToList();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var outcome = item.EventType switch
            {
                BrowserEvidenceEventTypes.AccessibilityHitTest => HitTest(items, index),
                BrowserEvidenceEventTypes.AccessibilityHitTestResult => HitTestResult(items, index),
                BrowserEvidenceEventTypes.AccessibilityActionSent => ActionSent(items, index),
                BrowserEvidenceEventTypes.AccessibilityActionReceived => ActionReceived(items, index),
                BrowserEvidenceEventTypes.AccessibilityHitTestComputed => HitTestComputed(item.Payload),
                _ => null
            };
            if (outcome is not null)
            {
                _outcomes[item.EventId] = outcome;
            }
        }
    }

    public static AccessibilityRequests Empty { get; } = new([]);

    public string Rule => $"rule {RuleVersion.ToString(CultureInfo.InvariantCulture)}";

    public AccessibilityRequestOutcome? Of(string eventId) => _outcomes.GetValueOrDefault(eventId);

    public static bool IsRequestEvidence(string channel, string eventType) =>
        channel == Channel && EventTypes.Contains(eventType);

    private static AccessibilityRequestOutcome HitTest(List<Item> items, int index)
    {
        var item = items[index];
        var payload = item.Payload;
        var answer = NodeOf(payload, "answer");
        var basis = ReadString(payload, "basis") == BrowserAccessibilityRequests.LastPreciseResult
            ? "the page's last precise result, whose bounds held the point"
            : "a guess from Chromium's own copy of the tree, by bounds";
        var text = answer is null
            ? $"Given at once: no object, from {basis}"
            : $"Given at once: {Describe(answer)}, from {basis}";
        var details = new List<string>();
        if (PointOf(payload, "point") is { } point)
        {
            details.Add($"Point: {point} on the screen, in physical pixels.");
        }

        if (payload.TryGetProperty("preciseRequested", out var requested) && requested.ValueKind == JsonValueKind.True)
        {
            var others = 0;
            Item? result = null;
            for (var next = index + 1; next < items.Count && items[next].Time - item.Time <= JoinWindowNanoseconds; next++)
            {
                if (items[next].EventType == BrowserEvidenceEventTypes.AccessibilityHitTestResult)
                {
                    result = items[next];
                    break;
                }

                if (items[next].EventType == BrowserEvidenceEventTypes.AccessibilityHitTest)
                {
                    others++;
                }
            }

            if (result is not null && NodeOf(result.Payload, "result") is { } found)
            {
                var same = answer is not null && answer.TreeId == found.TreeId && answer.NodeId == found.NodeId;
                details.Add(
                    $"The page's precise hit test then found {Describe(found)}, {Milliseconds(result.Time - item.Time)} later: " +
                    (same ? "the object given." : "not the object given."));
                if (others > 0)
                {
                    details.Add(
                        $"{Count(others, "other hit test was", "other hit tests were")} asked before it arrived, " +
                        "so it may answer one of them: Chromium does not say which request a result answers.");
                }
            }
            else
            {
                details.Add(
                    "The page was asked for a precise hit test; no result was recorded within " +
                    $"{Milliseconds(JoinWindowNanoseconds)}. A point with no object fires no result.");
            }
        }
        else
        {
            details.Add("The page was not asked for a precise hit test.");
        }

        details.Add(AnyProgram);
        return new AccessibilityRequestOutcome(text, details);
    }

    private static AccessibilityRequestOutcome? HitTestResult(List<Item> items, int index)
    {
        var item = items[index];
        if (NodeOf(item.Payload, "result") is not { } found)
        {
            return null;
        }

        var details = new List<string>
        {
            $"Kept for the next hit test whose point lies within its bounds, {found.Bounds} on the screen in physical pixels."
        };
        for (var previous = index - 1; previous >= 0 && item.Time - items[previous].Time <= JoinWindowNanoseconds; previous--)
        {
            if (items[previous].EventType == BrowserEvidenceEventTypes.AccessibilityHitTest)
            {
                details.Add(
                    $"The latest hit test before it was asked {Milliseconds(item.Time - items[previous].Time)} earlier, " +
                    (PointOf(items[previous].Payload, "point") is { } point ? $"at {point}." : "."));
                break;
            }
        }

        return new AccessibilityRequestOutcome($"The page's precise hit test found {Describe(found)}", details);
    }

    private static AccessibilityRequestOutcome? ActionSent(List<Item> items, int index)
    {
        var item = items[index];
        var payload = item.Payload;
        var action = ReadString(payload, "action");
        if (action is null)
        {
            return null;
        }

        var targetId = ReadInt64(payload, "targetNodeId");
        var target = NodeOf(payload, "target") is { } node
            ? Describe(node)
            : targetId is { } id ? $"node {id.ToString(CultureInfo.InvariantCulture)}, not in the browser's copy of the tree" : null;
        var details = new List<string>();
        if (RectOf(payload, "targetRect") is { } rect)
        {
            details.Add(rect == "0, 0, 0 by 0"
                ? "Scrolls the whole object into view."
                : $"Scrolls the part {rect} of the object, in its own coordinates, into view.");
        }

        if (PointOf(payload, "targetPoint") is { } point)
        {
            details.Add($"Point: {point}.");
        }

        var horizontal = ReadString(payload, "horizontalScrollAlignment");
        var vertical = ReadString(payload, "verticalScrollAlignment");
        if (horizontal is not null and not "none" || vertical is not null and not "none")
        {
            details.Add($"Alignment: horizontal {horizontal}, vertical {vertical}.");
        }

        if (ReadString(payload, "scrollBehavior") is { } behavior and not "none")
        {
            details.Add($"Scroll behavior: {behavior}.");
        }

        if (payload.TryGetProperty("selection", out var selection) && selection.ValueKind == JsonValueKind.Object)
        {
            details.Add(
                $"Selection: from node {ReadInt64(selection, "anchorNodeId")} offset {ReadInt64(selection, "anchorOffset")} " +
                $"to node {ReadInt64(selection, "focusNodeId")} offset {ReadInt64(selection, "focusOffset")}.");
        }

        var treeId = ReadString(payload, "treeId");
        Item? received = null;
        for (var next = index + 1; next < items.Count && items[next].Time - item.Time <= JoinWindowNanoseconds; next++)
        {
            if (items[next].EventType == BrowserEvidenceEventTypes.AccessibilityActionReceived &&
                ReadString(items[next].Payload, "treeId") == treeId &&
                ReadString(items[next].Payload, "action") == action &&
                ReadInt64(items[next].Payload, "targetNodeId") == targetId)
            {
                received = items[next];
                break;
            }
        }

        details.Add(received is null
            ? $"No receipt by the page's renderer was recorded within {Milliseconds(JoinWindowNanoseconds)}."
            : $"The page's renderer received it {Milliseconds(received.Time - item.Time)} later.");
        details.Add(AnyProgram);
        return new AccessibilityRequestOutcome($"Asked the page to {ActionText(action, target)}", details);
    }

    private static AccessibilityRequestOutcome? ActionReceived(List<Item> items, int index)
    {
        var item = items[index];
        var payload = item.Payload;
        var action = ReadString(payload, "action");
        if (action is null)
        {
            return null;
        }

        var targetId = ReadInt64(payload, "targetNodeId");
        var treeId = ReadString(payload, "treeId");
        var details = new List<string>();
        for (var previous = index - 1; previous >= 0 && item.Time - items[previous].Time <= JoinWindowNanoseconds; previous--)
        {
            if (items[previous].EventType == BrowserEvidenceEventTypes.AccessibilityActionSent &&
                ReadString(items[previous].Payload, "treeId") == treeId &&
                ReadString(items[previous].Payload, "action") == action &&
                ReadInt64(items[previous].Payload, "targetNodeId") == targetId)
            {
                details.Add($"Sent by the browser process {Milliseconds(item.Time - items[previous].Time)} earlier.");
                break;
            }
        }

        if (details.Count == 0)
        {
            details.Add($"No sending by the browser process was recorded in the {Milliseconds(JoinWindowNanoseconds)} before it.");
        }

        var target = targetId is { } id ? $"node {id.ToString(CultureInfo.InvariantCulture)}" : null;
        return new AccessibilityRequestOutcome($"The page's renderer was asked to {ActionText(action, target)}", details);
    }

    private static AccessibilityRequestOutcome HitTestComputed(JsonElement payload)
    {
        var point = PointOf(payload, "point") ?? "an unrecorded point";
        var node = ReadInt64(payload, "nodeId");
        var child = payload.TryGetProperty("childTree", out var value) && value.ValueKind == JsonValueKind.True;
        var text = node is not { } id
            ? $"The page's hit test at {point} found no object"
            : child
                ? $"The page's hit test at {point} found node {id.ToString(CultureInfo.InvariantCulture)}, which holds a child frame or tree, where it continues"
                : $"The page's hit test at {point} found node {id.ToString(CultureInfo.InvariantCulture)}";
        return new AccessibilityRequestOutcome(
            text,
            [
                "The point is in the frame's coordinates. The node is of the renderer's tree, whose accessibility checkpoints name it."
            ]);
    }

    private static string ActionText(string action, string? target)
    {
        var on = target ?? "no object";
        return action switch
        {
            "scrollToMakeVisible" => $"scroll {on} into view",
            "focus" => $"focus {on}",
            "blur" => $"remove the focus from {on}",
            "doDefault" => $"do the default action of {on}, such as activating it",
            "setSelection" => $"set the selection, in {on}",
            "scrollToPoint" => $"scroll {on} to a point",
            "setScrollOffset" => $"set the scroll offset of {on}",
            "showContextMenu" => $"show the context menu of {on}",
            "setValue" => $"set the value of {on}",
            "increment" => $"increment {on}",
            "decrement" => $"decrement {on}",
            "loadInlineTextBoxes" => $"load the inline text boxes of {on}",
            "setSequentialFocusNavigationStartingPoint" => $"start the next Tab navigation from {on}",
            _ => $"perform {action} on {on}"
        };
    }

    private static string Describe(Node node)
    {
        var id = node.NodeId.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(node.Name)
            ? $"{node.Role} with no name (node {id})"
            : $"{node.Role} \"{node.Name}\" (node {id})";
    }

    private static Node? NodeOf(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object ||
            ReadString(node, "treeId") is not { } tree || ReadInt64(node, "nodeId") is not { } id)
        {
            return null;
        }

        return new Node(tree, id, ReadString(node, "role") ?? "object", ReadString(node, "name") ?? string.Empty, RectOf(node, "bounds") ?? "unrecorded");
    }

    private static string? PointOf(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var point) && point.ValueKind == JsonValueKind.Object &&
        ReadInt64(point, "x") is { } x && ReadInt64(point, "y") is { } y
            ? $"{x.ToString(CultureInfo.InvariantCulture)}, {y.ToString(CultureInfo.InvariantCulture)}"
            : null;

    private static string? RectOf(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var rect) && rect.ValueKind == JsonValueKind.Object &&
        ReadInt64(rect, "x") is { } x && ReadInt64(rect, "y") is { } y &&
        ReadInt64(rect, "width") is { } width && ReadInt64(rect, "height") is { } height
            ? string.Create(CultureInfo.InvariantCulture, $"{x}, {y}, {width} by {height}")
            : null;

    private static string Milliseconds(long nanoseconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{nanoseconds / 1_000_000.0:0.#} ms");

    private static string Count(int count, string one, string many) =>
        count == 1 ? $"1 {one}" : $"{count.ToString(CultureInfo.InvariantCulture)} {many}";

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static long? ReadInt64(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number)
            ? number
            : null;
}
