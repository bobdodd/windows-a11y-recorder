using System.Globalization;
using System.Text.Json;
using Recorder.Session;

namespace Recorder.Recreation;

// Paths of recorded nodes, as "Paths through shadow roots" in
// docs/architecture/page-recreation.md describes them: one XPath expression
// for each tree scope, with positional steps only.
public static class RecordedPaths
{
    // The path of a node of the recorded tree, or null when it is not in the
    // document's tree, or is in a user agent shadow root, which a path does
    // not enter.
    public static NodePath? Of(DomDocumentTree tree, long id)
    {
        var scopes = new List<string>();
        var modes = new List<string>();
        var steps = new List<string>();
        var current = id;
        while (true)
        {
            if (!tree.Nodes.TryGetValue(current, out var node))
            {
                return null;
            }
            if (node.NodeType == "document")
            {
                scopes.Add(Join(steps));
                break;
            }
            if (node.NodeType == "shadow-root")
            {
                var mode = ShadowMode(node);
                if (mode is not ("open" or "closed"))
                {
                    return null;
                }
                scopes.Add(Join(steps));
                modes.Add(mode);
                steps.Clear();
                if (node.ParentId is not { } host)
                {
                    return null;
                }
                current = host;
                continue;
            }
            if (node.ParentId is not { } parentId || !tree.Nodes.TryGetValue(parentId, out var parent))
            {
                return null;
            }
            var test = Test(node);
            if (test is null)
            {
                return null;
            }
            var position = 0;
            foreach (var sibling in parent.Children)
            {
                if (tree.Nodes.TryGetValue(sibling, out var other) && Test(other) == test)
                {
                    position++;
                }
                if (sibling == current)
                {
                    break;
                }
            }
            steps.Add($"{test}[{position.ToString(CultureInfo.InvariantCulture)}]");
            current = parentId;
        }
        if (scopes.Any(scope => scope.Length == 0))
        {
            return null;
        }
        scopes.Reverse();
        modes.Reverse();
        return NodePath.Create(scopes, modes);
    }

    public static string? ShadowMode(DomNode shadowRoot)
    {
        if (shadowRoot.ShadowRootFields is not { } fields)
        {
            return null;
        }
        using var parsed = JsonDocument.Parse($"[{fields}]");
        return parsed.RootElement[1].GetString();
    }

    // An element in the HTML namespace, named in capitals, by its lower case
    // name; any other element by a local name test.
    private static string? Test(DomNode node) => node.NodeType switch
    {
        "element" when node.NodeName is { } name && name != name.ToLowerInvariant() && name == name.ToUpperInvariant() =>
            name.ToLowerInvariant(),
        "element" when node.NodeName is { } name => $"*[local-name()='{name}']",
        "text" => "text()",
        "comment" => "comment()",
        _ => null,
    };

    private static string Join(List<string> steps)
    {
        var copy = new List<string>(steps);
        copy.Reverse();
        return copy.Count == 0 ? "" : "/" + string.Join("/", copy);
    }
}

// The evidence the panel shows for a recorded document at a frame, read from
// its rebuilt state. Every value is a recorded value or is computed from
// recorded values, as each record type states.
public static class RecordedEvidence
{
    public const string Notice =
        "This page is a recreation of the recorded document, built from the recording. It is not the live page, and no page script runs.";

    public static RecreationEvidence Create(
        BrowserDocumentState state,
        string? url,
        long frameNanoseconds,
        long recordingNanoseconds,
        string basis,
        RecreationFidelity fidelity,
        IReadOnlyList<string> notes)
    {
        var tree = state.Dom ?? throw new InvalidOperationException("The document has no DOM state.");
        string Element(long id) => tree.Nodes.TryGetValue(id, out var node) ? node.NodeName?.ToLowerInvariant() ?? "" : "";

        // Listeners on nodes of the tree, and on other targets.
        var byNode = new SortedDictionary<long, List<RecordedListener>>();
        var others = new List<RecordedTargetListener>();
        foreach (var (_, record) in state.Script.Listeners.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var listener = Listener(record);
            var target = record.TryGetProperty("target", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
            if (target.ValueKind == JsonValueKind.Object &&
                target.TryGetProperty("nodeId", out var nodeId) && nodeId.ValueKind == JsonValueKind.Number &&
                tree.Nodes.ContainsKey(nodeId.GetInt64()))
            {
                if (!byNode.TryGetValue(nodeId.GetInt64(), out var list))
                {
                    list = [];
                    byNode.Add(nodeId.GetInt64(), list);
                }
                list.Add(listener);
            }
            else
            {
                others.Add(new RecordedTargetListener(TargetName(target), listener));
            }
        }

        var interactive = new List<RecordedInteractiveElement>();
        var ids = new HashSet<long>(byNode.Keys);
        foreach (var (dom, (record, _)) in state.Accessibility.Nodes)
        {
            if (tree.Nodes.TryGetValue(dom, out var node) && node.NodeType == "element" && Focusable(record) == true)
            {
                ids.Add(dom);
            }
        }
        foreach (var id in TreeOrder(tree).Where(ids.Contains))
        {
            if (RecordedPaths.Of(tree, id) is not { } path)
            {
                continue;
            }
            var accessibility = state.Accessibility.Nodes.TryGetValue(id, out var found) ? found : ((JsonElement Record, long Time)?)null;
            interactive.Add(new RecordedInteractiveElement(
                path,
                Element(id),
                byNode.GetValueOrDefault(id) ?? [],
                accessibility is { } a ? Focusable(a.Record) : null,
                accessibility is { } b ? Text(b.Record, "roleName") : null,
                accessibility is { } c ? Text(c.Record, "name") : null,
                accessibility?.Time,
                accessibility is { } d ? Text(d.Record, "serializedProperties") : null));
        }

        var timers = state.Script.Timers.Values
            .OrderBy(timer => timer.ScheduledTime)
            .ThenBy(timer => Text(timer.Scheduled, "timerId"), StringComparer.Ordinal)
            .Select(timer => Timer(timer, recordingNanoseconds))
            .ToArray();

        var current = state.Interaction.Current();
        NodePath? PathOf(long? id) => id is { } value ? RecordedPaths.Of(tree, value) : null;
        var formValues = current.TextControls.Values
            .OrderBy(control => control.NodeId)
            .Where(control => control.Value is not null && PathOf(control.NodeId) is not null)
            .Select(control => new RecordedFormValue(PathOf(control.NodeId)!, control.Value!))
            .ToArray();
        string? selection = current.Selection is { Type: { } type } recorded
            ? $"{type}: anchor node {recorded.AnchorNodeId?.ToString(CultureInfo.InvariantCulture) ?? "none"} offset {recorded.AnchorOffset?.ToString(CultureInfo.InvariantCulture) ?? "none"}, focus node {recorded.FocusNodeId?.ToString(CultureInfo.InvariantCulture) ?? "none"} offset {recorded.FocusOffset?.ToString(CultureInfo.InvariantCulture) ?? "none"}"
            : null;

        var title = tree.Nodes.Values.FirstOrDefault(node => node.NodeName == "TITLE") is { } titleNode
            ? string.Concat(titleNode.Children.Select(child => tree.Nodes.TryGetValue(child, out var text) ? text.Data : null))
            : null;
        return new RecreationEvidence(
            new RecreationDescription(
                "recording",
                string.IsNullOrWhiteSpace(title) ? url ?? state.Key : title,
                Notice,
                frameNanoseconds,
                recordingNanoseconds,
                basis)
            {
                Url = url,
                DocumentKey = state.Key,
            },
            fidelity,
            timers,
            [],
            interactive,
            new RecordedInteraction(PathOf(current.FocusedNodeId), selection, formValues))
        {
            OtherListeners = others,
            Notes = notes,
            AnimationsNotRead = "Running animations and transitions are not yet read from the recording, so none are listed.",
        };
    }

    // The nodes of the tree in tree order, each shadow root before its
    // host's children, as the DevTools protocol orders them.
    private static IEnumerable<long> TreeOrder(DomDocumentTree tree)
    {
        var stack = new Stack<long>();
        stack.Push(RecordedPage.DocumentNodeId(tree));
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!tree.Nodes.TryGetValue(id, out var node))
            {
                continue;
            }
            yield return id;
            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(node.Children[index]);
            }
            if (node.ShadowRootId is { } shadow)
            {
                stack.Push(shadow);
            }
        }
    }

    public static RecordedTimer Timer(PendingTimer timer, long recordingNanoseconds)
    {
        var record = timer.Scheduled;
        var kind = Text(record, "timerKind") ?? "unknown";
        var effective = Number(record, "effectiveDelayMilliseconds");
        double? remaining = kind is "timeout" or "interval" && effective is { } delay
            ? ((timer.LastRunTime ?? timer.ScheduledTime) + delay * 1e6 - recordingNanoseconds) / 1e6
            : null;
        return new RecordedTimer(
            Text(record, "timerId") ?? "",
            kind,
            Number(record, "requestedDelayMilliseconds"),
            effective,
            timer.ScheduledTime,
            timer.LastRunTime,
            remaining);
    }

    private static RecordedListener Listener(JsonElement record)
    {
        string? location = null;
        if (record.TryGetProperty("location", out var value) && value.ValueKind == JsonValueKind.Object && Text(value, "url") is { } url)
        {
            location = url;
            if (Number(value, "line") is { } line)
            {
                location += ":" + line.ToString(CultureInfo.InvariantCulture);
                if (Number(value, "column") is { } column)
                {
                    location += ":" + column.ToString(CultureInfo.InvariantCulture);
                }
            }
        }
        return new RecordedListener(
            Text(record, "eventName") ?? "",
            Text(record, "registrationKind"),
            Flag(record, "capture"),
            Flag(record, "once"),
            Flag(record, "passive"),
            location);
    }

    private static string TargetName(JsonElement target)
    {
        if (target.ValueKind != JsonValueKind.Object)
        {
            return "not recorded";
        }
        var kind = Text(target, "kind") ?? "other";
        var name = Text(target, "interfaceName") ?? Text(target, "tagName");
        return name is null ? kind : $"{kind} ({name})";
    }

    // Whether Chromium's accessibility property text, as recorded, has the
    // FOCUSABLE state. The text is Chromium's diagnostic form, not a field
    // contract; the panel says so.
    public static bool? Focusable(JsonElement record) =>
        Text(record, "serializedProperties") is { } properties
            ? properties.Split(' ').Contains("FOCUSABLE", StringComparer.Ordinal)
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
