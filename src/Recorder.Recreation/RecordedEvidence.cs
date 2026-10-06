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
        IReadOnlyList<string> notes,
        RecordedAnimations? animations = null,
        RecordedScripts? scripts = null)
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

        // Slice 4h: the document's scripts at the frame's recording time.
        var recordedScripts = scripts ?? RecordedScripts.None;
        var scriptRows = recordedScripts.Scripts
            .Select(item => Script(item, recordedScripts, state.Script, tree))
            .ToArray();

        var timers = state.Script.Timers.Values
            .OrderBy(timer => timer.ScheduledTime)
            .ThenBy(timer => Text(timer.Scheduled, "timerId"), StringComparer.Ordinal)
            .Select(timer => Timer(timer, recordingNanoseconds, TimerOrigins.Of(timer, state.Script, tree, recordedScripts)))
            .ToArray();

        // Slice 4g: the document's animations at the frame's recording time.
        var recordedAnimations = animations ?? RecordedAnimations.None;
        var animationRows = recordedAnimations.Animations
            .Select(item => Animation(item, tree))
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
            animationRows,
            interactive,
            new RecordedInteraction(PathOf(current.FocusedNodeId), selection, formValues))
        {
            OtherListeners = others,
            Notes = notes,
            AnimationsNotRead = recordedAnimations.Recorded
                ? null
                : "The recording holds no animation records, which are recorded from protocol 0.53, so running animations and transitions are not listed.",
            AnimationNotes = recordedAnimations.Recorded ? AnimationNotes : [],
            Scripts = scriptRows,
            ScriptsNotRead = recordedScripts.Recorded
                ? null
                : "The recording holds no script records, which are recorded from protocol 0.54, so the page's script source was not recorded.",
            ScriptNotes = recordedScripts.Recorded ? ScriptNotes : [],
        };
    }

    // What the panel says of the scripts it lists (slice 4h).
    public static readonly IReadOnlyList<string> ScriptNotes =
    [
        "Scripts are read from the recording's script-parsed records, made by V8 when it instantiates a script, or fails to compile one, in the document's main thread, once per script. A script is listed when its record is at or before the recording time.",
        "The text is the source V8 compiled, after decoding, shown as text and never run. It is not the bytes the server sent, and not the original of a minified or transpiled script; a source map is named, not recorded.",
        "Compiled is not the same as ran: a script's top level runs after it is compiled, but a function in it may never have been called. A script that failed to compile did not run.",
        "Scripts compiled while a DevTools protocol command ran, such as expressions typed in the Console, and scripts of DevTools' own world are not recorded. Scripts of iframes, workers, and WebAssembly are not recorded.",
    ];

    private static RecordedScript Script(RecordedScriptState item, RecordedScripts scripts, ScriptDocumentState state, DomDocumentTree tree)
    {
        string? element = null;
        NodePath? path = null;
        if (state.Scripts.TryGetValue(item.ScriptId, out var source))
        {
            (element, path) = TimerOrigins.Describe(source, tree);
        }
        string? evalFrom = null;
        if (item.EvalFromScriptId is { } caller)
        {
            var callerScript = scripts.Scripts.FirstOrDefault(other => other.ScriptId == caller);
            evalFrom = callerScript is null
                ? $"script {caller}, which is not listed"
                : $"script {caller}" + ((callerScript.Url ?? callerScript.SourceUrl) is { } address ? $", {address}" : "");
        }
        return new RecordedScript(
            item.ScriptId,
            item.Kind,
            TimerOrigins.OwnerOf(item.World, [item.Url, item.SourceUrl]),
            element,
            path,
            item.Url,
            item.SourceUrl,
            item.SourceMapUrl,
            item.Line,
            item.Column,
            item.EvalFromScriptId,
            evalFrom,
            item.CompileError,
            scripts.HasText(item.Digest) ? item.Digest : null,
            item.Size,
            item.RecordedNanoseconds);
    }

    // What the panel says of the animations it lists (slice 4g).
    public static readonly IReadOnlyList<string> AnimationNotes =
    [
        "Animations are read from the recording's animation-updated records, made by Blink when an animation starts and each time its play state, start time, playback rate, timing, or timeline changes, and from its animation-removed records.",
        "An animation is listed when its latest record at or before the recording time is running, paused, or pending, or finished with a fill that holds its effect. Idle, cancelled, and released animations are not listed.",
        "For a running animation on a document timeline, the current time at the frame is computed from its start time and the timeline's recorded zero time; Blink's own time for the rendering update shown is that update's animation frame time, which can differ by up to one display refresh. For any other animation the current time is the one recorded.",
        "The progress is the directed progress of the Web Animations model, computed from the current time and the recorded timing, before the easing is applied; the easing is shown as recorded. The progress of an animation on a scroll or view timeline is that of its latest record, not computed at the frame.",
        "SVG animation elements, such as animate and animateTransform, are not Blink animations and are not listed.",
    ];

    private static RecordedAnimation Animation(RecordedAnimationState item, DomDocumentTree tree)
    {
        var timeline = item.TimelineKind switch
        {
            "document" => "document timeline",
            "scroll" or "view" => $"{item.TimelineKind} timeline" +
                (item.TimelineAxis is { } axis ? $", {axis}" : "") +
                (item.TimelineSourceNodeId is { } source ? $", source node {source.ToString(CultureInfo.InvariantCulture)}" : ""),
            "none" => "no timeline",
            _ => "another timeline",
        };
        var timing = item.Timing;
        return new RecordedAnimation(
            item.Kind,
            item.Name,
            item.TargetNodeId is { } target ? RecordedPaths.Of(tree, target) : null,
            item.PseudoElement,
            item.PlayState,
            item.Pending,
            item.StartTimeMilliseconds,
            item.StartRecordingNanoseconds,
            timing?.DelayMilliseconds,
            timing?.DurationMilliseconds,
            timing?.Iterations,
            timing?.Direction,
            timing?.Fill,
            timing?.Easing,
            item.CurrentTimeMilliseconds,
            item.CurrentTimeBasis,
            item.CurrentIteration,
            item.Progress,
            timeline,
            item.CompositorAnimationId is not null,
            item.RecordedNanoseconds,
            item.RecordedProgress);
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

    public static RecordedTimer Timer(PendingTimer timer, long recordingNanoseconds, RecordedTimerOrigin? scheduledBy = null)
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
            remaining,
            scheduledBy);
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

// Slice 4f (protocol 0.52): who scheduled a timer, read from its timer-origin
// record and joined to the script-compiled records of its document by V8
// script ID. The panel shows what was recorded; nothing here infers an owner
// the records do not give. See docs/architecture/page-recreation.md, "Slice
// 4f: who scheduled each timer".
public static class TimerOrigins
{
    public const string NotRecorded =
        "not recorded; who scheduled a timer is recorded from protocol 0.52";

    public static RecordedTimerOrigin Of(PendingTimer timer, ScriptDocumentState script, DomDocumentTree tree, RecordedScripts? scripts = null)
    {
        var callbackRecord = timer.Scheduled.TryGetProperty("callbackLocation", out var value) ? value : default;
        var callback = Location(callbackRecord);
        // Slice 4h: a location whose script has recorded text links to its
        // line in the viewer.
        var callbackSource = Link(callbackRecord, scripts);
        if (timer.Origin is not { } origin)
        {
            return new RecordedTimerOrigin(NotRecorded, null, null, null, null, callback, null)
            {
                CallbackSource = callbackSource,
            };
        }
        var frames = origin.TryGetProperty("stack", out var stack) && stack.ValueKind == JsonValueKind.Array
            ? stack.EnumerateArray().ToArray()
            : [];
        var handler = Text(origin, "handler");
        var world = origin.TryGetProperty("world", out var recordedWorld) && recordedWorld.ValueKind == JsonValueKind.Object
            ? recordedWorld
            : (JsonElement?)null;

        string? element = null;
        NodePath? path = null;
        string? note = null;
        for (var index = 0; index < frames.Length; index++)
        {
            if (Text(frames[index], "scriptId") is not { } id || !script.Scripts.TryGetValue(id, out var source))
            {
                continue;
            }
            (element, path) = Describe(source, tree);
            if (index > 0)
            {
                note = $"Found from stack frame {index + 1}, counting from the innermost; " +
                    (frames.Take(index).Any(frame => Flag(frame, "isEval"))
                        ? "the frames inside it include eval code, which has no element of its own."
                        : "the frames inside it have no script element or attribute recorded.");
            }
            break;
        }
        if (element is null && frames.Length > 0)
        {
            note = frames.Any(frame => Flag(frame, "isEval"))
                ? "No frame of the stack has a script element or attribute recorded; the stack includes eval code."
                : "No frame of the stack has a script element or attribute recorded.";
        }
        return new RecordedTimerOrigin(
            OwnerOf(world, frames.Select(frame => Text(frame, "url"))),
            element,
            path,
            note,
            frames.Length > 0 ? Frame(frames[0]) : null,
            callback,
            handler)
        {
            CallerSource = frames.Length > 0 ? Link(frames[0], scripts) : null,
            CallbackSource = callbackSource,
        };
    }

    // The text and line of a recorded location, when its script is one of
    // the document's scripts with recorded text.
    private static RecordedSourceLink? Link(JsonElement location, RecordedScripts? scripts)
    {
        if (scripts is null || Text(location, "scriptId") is not { } id ||
            scripts.Scripts.FirstOrDefault(item => item.ScriptId == id) is not { } script ||
            !scripts.HasText(script.Digest))
        {
            return null;
        }
        var line = Number(location, "line") is { } recorded && recorded > 0 ? (int)recorded : 1;
        var column = Number(location, "column") is { } at && at > 0 ? (int?)at : null;
        return new RecordedSourceLink(script.ScriptId, script.Digest, line, column);
    }

    public static string OwnerOf(JsonElement? world, IEnumerable<string?> urls)
    {
        if (world is not { } recorded)
        {
            return "no script was running";
        }
        var name = Text(recorded, "name");
        var stableId = Text(recorded, "stableId");
        switch (Text(recorded, "kind"))
        {
            case "main":
                var extension = urls
                    .FirstOrDefault(url => url is not null && url.StartsWith("chrome-extension://", StringComparison.Ordinal));
                return extension is null
                    ? "the page"
                    : $"an extension's script in the page's own world, {extension}";
            case "isolated":
                return name is null && stableId is null
                    ? "an isolated world, such as an extension's content script, with no name or ID recorded"
                    : $"an isolated world, such as an extension's content script: {name ?? "no name recorded"}, ID {stableId ?? "not recorded"}";
            case "inspector-isolated":
                return "DevTools";
            case { } kind:
                return $"script in a {kind} world";
            default:
                return "not recorded";
        }
    }

    public static (string Element, NodePath? Path) Describe(JsonElement source, DomDocumentTree tree)
    {
        var url = Text(source, "url");
        var description = Text(source, "kind") switch
        {
            "classic" => url is null ? "the inline script element" : $"the script element loading {url}",
            "module" => url is null ? "the inline module script element" : $"the module script element loading {url}",
            "event-handler-attribute" => $"the {Text(source, "attributeName") ?? "on..."} attribute of the element",
            _ => "a script element",
        };
        if (!source.TryGetProperty("elementNodeId", out var node) || node.ValueKind != JsonValueKind.Number)
        {
            return ($"{description}; the element was not recorded", null);
        }
        var id = node.GetInt64();
        return RecordedPaths.Of(tree, id) is { } path
            ? (description, path)
            : ($"{description}, node {id.ToString(CultureInfo.InvariantCulture)}, which is not in the document at the frame", null);
    }

    private static string Frame(JsonElement frame)
    {
        var text = Text(frame, "url") ?? "a script with no address";
        if (Number(frame, "line") is { } line)
        {
            text += ":" + line.ToString(CultureInfo.InvariantCulture);
            if (Number(frame, "column") is { } column)
            {
                text += ":" + column.ToString(CultureInfo.InvariantCulture);
            }
        }
        text += Text(frame, "functionName") is { } function ? $", in {function}" : ", at the top level or in an anonymous function";
        if (Flag(frame, "isEval"))
        {
            text += ", eval code";
        }
        return text;
    }

    private static string? Location(JsonElement location)
    {
        if (location.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var text = Text(location, "url") ?? "a script with no address";
        if (Number(location, "line") is { } line)
        {
            text += ":" + line.ToString(CultureInfo.InvariantCulture);
            if (Number(location, "column") is { } column)
            {
                text += ":" + column.ToString(CultureInfo.InvariantCulture);
            }
        }
        if (Text(location, "functionName") is { } function)
        {
            text += $", {function}";
        }
        return text;
    }

    private static bool Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
}
