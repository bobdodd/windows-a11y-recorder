using System.Buffers;
using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// The JSON form of a document's rebuilt state, as a snapshot holds it. The
/// DOM tree is written node by node; the layout and interaction state are
/// written as the records they were rebuilt from, as are the listeners,
/// timers, and accessibility data, whose payloads keep their
/// recorded text, so reading a snapshot and applying the records after it
/// gives the state applying every record gives. Nodes, transform nodes, and
/// scroll offsets are written in order of their identities, so two equal
/// states have the same form.
/// </summary>
public static class BrowserStateSnapshot
{
    /// <summary>The snapshot format's version.</summary>
    public const int FormatVersion = 2;

    public static byte[] Serialize(BrowserDocumentState document)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true }))
        {
            Write(writer, document);
        }
        return buffer.WrittenSpan.ToArray();
    }

    public static void Write(Utf8JsonWriter writer, BrowserDocumentState document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        writer.WriteStartObject();
        writer.WriteString("kind", "document-state");
        writer.WriteNumber("formatVersion", FormatVersion);
        writer.WriteString("documentKey", document.Key);
        WriteText(writer, "browserInstanceId", document.BrowserInstanceId);
        if (document.ProcessId is { } process)
        {
            writer.WriteNumber("processId", process);
        }
        else
        {
            writer.WriteNull("processId");
        }
        WriteText(writer, "pageId", document.PageId);
        WriteText(writer, "frameId", document.FrameId);
        WriteText(writer, "documentId", document.DocumentId);
        WriteText(writer, "documentToken", document.DocumentToken);
        writer.WriteNumber("firstEventKey", document.FirstEventKey);
        writer.WriteNumber("firstTime", document.FirstTime);
        writer.WriteNumber("eventKey", document.LastEventKey);
        writer.WriteNumber("time", document.LastTime);

        writer.WriteStartObject("dom");
        writer.WriteString("completeness", Name(document.DomCompleteness));
        writer.WriteBoolean("finishedParsing", document.FinishedParsing);
        writer.WriteNumber("eventKey", document.DomEventKey);
        writer.WriteNumber("time", document.DomTime);
        if (document.Dom is { } tree)
        {
            writer.WriteStartArray("nodes");
            foreach (var node in tree.Nodes.Values.OrderBy(node => node.Id))
            {
                WriteNode(writer, node);
            }
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteNull("nodes");
        }
        writer.WriteEndObject();

        var layout = document.Layout;
        writer.WriteStartObject("layout");
        writer.WriteString("completeness", Name(document.LayoutCompleteness));
        writer.WriteNumber("eventKey", document.LayoutEventKey);
        writer.WriteNumber("time", document.LayoutTime);
        writer.WritePropertyName("started");
        if (layout.LastStarted is { } started)
        {
            writer.WriteRawValue(started.GetRawText(), skipInputValidation: true);
        }
        else
        {
            writer.WriteNullValue();
        }
        writer.WritePropertyName("viewport");
        if (document.Viewport is { } viewport)
        {
            writer.WriteStartObject();
            writer.WriteNumber("width", viewport.Width);
            writer.WriteNumber("height", viewport.Height);
            writer.WriteNumber("devicePixelRatio", viewport.DevicePixelRatio);
            writer.WriteNumber("layoutZoomFactor", viewport.LayoutZoomFactor);
            writer.WriteNumber("time", viewport.Time);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNullValue();
        }
        WriteRecords(writer, "transformNodes", layout.TransformRecords.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value));
        WriteRecords(writer, "nodes", layout.Nodes.OrderBy(item => item.Key).Select(item => item.Value));
        WriteRecords(writer, "scrollOffsets", layout.ScrollOffsets.OrderBy(item => item.Key).Select(item => item.Value));
        writer.WriteEndObject();

        writer.WriteStartObject("interaction");
        writer.WriteString("completeness", Name(document.InteractionCompleteness));
        writer.WriteNumber("eventKey", document.InteractionEventKey);
        writer.WriteNumber("time", document.InteractionTime);
        WriteRecords(writer, "checkpoint", document.Interaction.Checkpoint);
        writer.WriteStartArray("changes");
        foreach (var (eventType, payload) in document.Interaction.Changes)
        {
            writer.WriteStartObject();
            writer.WriteString("eventType", eventType);
            writer.WritePropertyName("payload");
            writer.WriteRawValue(payload.GetRawText(), skipInputValidation: true);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();

        var script = document.Script;
        writer.WriteStartObject("script");
        writer.WriteString("completeness", Name(document.ScriptCompleteness));
        writer.WriteNumber("eventKey", script.EventKey);
        writer.WriteNumber("time", script.Time);
        WriteRecords(writer, "listeners", script.Listeners.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value));
        writer.WriteStartArray("timers");
        foreach (var (_, timer) in script.Timers.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteNumber("scheduledTime", timer.ScheduledTime);
            WriteNumber(writer, "lastRunTime", timer.LastRunTime);
            writer.WritePropertyName("scheduled");
            writer.WriteRawValue(timer.Scheduled.GetRawText(), skipInputValidation: true);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();

        var accessibility = document.Accessibility;
        writer.WriteStartObject("accessibility");
        writer.WriteString("completeness", Name(document.AccessibilityCompleteness));
        writer.WriteNumber("eventKey", accessibility.EventKey);
        writer.WriteNumber("time", accessibility.Time);
        writer.WriteStartArray("nodes");
        foreach (var (_, (record, time)) in accessibility.Nodes.OrderBy(item => item.Key))
        {
            writer.WriteStartObject();
            writer.WriteNumber("time", time);
            writer.WritePropertyName("record");
            writer.WriteRawValue(record.GetRawText(), skipInputValidation: true);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    public static BrowserDocumentState Read(ReadOnlyMemory<byte> data)
    {
        using var json = JsonDocument.Parse(data);
        return Read(json.RootElement);
    }

    public static BrowserDocumentState Read(JsonElement root)
    {
        if (root.GetProperty("formatVersion").GetInt32() != FormatVersion)
        {
            throw new InvalidDataException("The snapshot's format version is not supported.");
        }
        var document = new BrowserDocumentState(root.GetProperty("documentKey").GetString()!)
        {
            BrowserInstanceId = Text(root, "browserInstanceId"),
            ProcessId = root.GetProperty("processId").ValueKind == JsonValueKind.Number
                ? root.GetProperty("processId").GetInt64()
                : null,
            PageId = Text(root, "pageId"),
            FrameId = Text(root, "frameId"),
            DocumentId = Text(root, "documentId"),
            DocumentToken = Text(root, "documentToken"),
            FirstEventKey = root.GetProperty("firstEventKey").GetInt64(),
            FirstTime = root.GetProperty("firstTime").GetInt64(),
            LastEventKey = root.GetProperty("eventKey").GetInt64(),
            LastTime = root.GetProperty("time").GetInt64(),
        };

        var dom = root.GetProperty("dom");
        document.DomCompleteness = Parse(dom.GetProperty("completeness").GetString());
        document.FinishedParsing = dom.GetProperty("finishedParsing").GetBoolean();
        document.DomEventKey = dom.GetProperty("eventKey").GetInt64();
        document.DomTime = dom.GetProperty("time").GetInt64();
        if (dom.GetProperty("nodes") is { ValueKind: JsonValueKind.Array } nodes)
        {
            var tree = new DomDocumentTree();
            foreach (var item in nodes.EnumerateArray())
            {
                var node = ReadNode(item);
                tree.Nodes.Add(node.Id, node);
            }
            document.Dom = tree;
        }

        var layout = root.GetProperty("layout");
        document.LayoutCompleteness = Parse(layout.GetProperty("completeness").GetString());
        document.LayoutEventKey = layout.GetProperty("eventKey").GetInt64();
        document.LayoutTime = layout.GetProperty("time").GetInt64();
        var viewport = layout.GetProperty("viewport");
        document.Viewport = viewport.ValueKind == JsonValueKind.Object
            ? new RecordedViewport(
                viewport.GetProperty("width").GetDouble(),
                viewport.GetProperty("height").GetDouble(),
                viewport.GetProperty("devicePixelRatio").GetDouble(),
                viewport.GetProperty("layoutZoomFactor").GetDouble(),
                viewport.GetProperty("time").GetInt64())
            : null;
        var started = layout.GetProperty("started");
        document.Layout.Load(
            started.ValueKind == JsonValueKind.Object ? started : null,
            layout.GetProperty("transformNodes").EnumerateArray(),
            layout.GetProperty("nodes").EnumerateArray(),
            layout.GetProperty("scrollOffsets").EnumerateArray());

        var interaction = root.GetProperty("interaction");
        document.InteractionCompleteness = Parse(interaction.GetProperty("completeness").GetString());
        document.InteractionEventKey = interaction.GetProperty("eventKey").GetInt64();
        document.InteractionTime = interaction.GetProperty("time").GetInt64();
        document.Interaction.Load(
            interaction.GetProperty("checkpoint").EnumerateArray(),
            interaction.GetProperty("changes").EnumerateArray()
                .Select(change => (change.GetProperty("eventType").GetString()!, change.GetProperty("payload"))));

        var script = root.GetProperty("script");
        document.ScriptCompleteness = Parse(script.GetProperty("completeness").GetString());
        document.Script.Load(
            script.GetProperty("listeners").EnumerateArray(),
            script.GetProperty("timers").EnumerateArray().Select(timer => new PendingTimer(
                timer.GetProperty("scheduled"),
                timer.GetProperty("scheduledTime").GetInt64(),
                Number(timer, "lastRunTime"))),
            script.GetProperty("eventKey").GetInt64(),
            script.GetProperty("time").GetInt64());

        var accessibility = root.GetProperty("accessibility");
        document.AccessibilityCompleteness = Parse(accessibility.GetProperty("completeness").GetString());
        document.Accessibility.Load(
            accessibility.GetProperty("nodes").EnumerateArray()
                .Select(node => (node.GetProperty("record"), node.GetProperty("time").GetInt64())),
            accessibility.GetProperty("eventKey").GetInt64(),
            accessibility.GetProperty("time").GetInt64());
        return document;
    }

    /// <summary>The name a completeness is written with.</summary>
    public static string Name(BrowserStateCompleteness completeness) => completeness switch
    {
        BrowserStateCompleteness.NotWalked => "not-walked",
        BrowserStateCompleteness.Parsing => "parsing",
        BrowserStateCompleteness.Complete => "complete",
        BrowserStateCompleteness.AfterLoss => "after-loss",
        BrowserStateCompleteness.WalkCut => "walk-cut",
        _ => throw new ArgumentOutOfRangeException(nameof(completeness)),
    };

    private static BrowserStateCompleteness Parse(string? name) => name switch
    {
        "not-walked" => BrowserStateCompleteness.NotWalked,
        "parsing" => BrowserStateCompleteness.Parsing,
        "complete" => BrowserStateCompleteness.Complete,
        "after-loss" => BrowserStateCompleteness.AfterLoss,
        "walk-cut" => BrowserStateCompleteness.WalkCut,
        _ => throw new InvalidDataException($"The snapshot names an unknown completeness \"{name}\"."),
    };

    private static void WriteNode(Utf8JsonWriter writer, DomNode node)
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", node.Id);
        WriteNumber(writer, "parentId", node.ParentId);
        WriteText(writer, "type", node.NodeType);
        WriteText(writer, "name", node.NodeName);
        writer.WriteStartArray("children");
        foreach (var child in node.Children)
        {
            writer.WriteNumberValue(child);
        }
        writer.WriteEndArray();
        WriteNumber(writer, "shadowRootId", node.ShadowRootId);
        writer.WriteStartArray("attributes");
        foreach (var (name, value) in node.Attributes)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(name);
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value);
            }
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        WriteText(writer, "data", node.Data);
        WriteText(writer, "shadowRootFields", node.ShadowRootFields);
        WriteText(writer, "assignedNodes", node.AssignedNodes);
        writer.WriteEndObject();
    }

    private static DomNode ReadNode(JsonElement item)
    {
        var node = new DomNode(item.GetProperty("id").GetInt64())
        {
            ParentId = Number(item, "parentId"),
            NodeType = Text(item, "type"),
            NodeName = Text(item, "name"),
            ShadowRootId = Number(item, "shadowRootId"),
            Data = Text(item, "data"),
            ShadowRootFields = Text(item, "shadowRootFields"),
            AssignedNodes = Text(item, "assignedNodes"),
        };
        foreach (var child in item.GetProperty("children").EnumerateArray())
        {
            node.Children.Add(child.GetInt64());
        }
        foreach (var attribute in item.GetProperty("attributes").EnumerateArray())
        {
            node.Attributes[attribute[0].GetString()!] = attribute[1].ValueKind == JsonValueKind.String
                ? attribute[1].GetString()
                : null;
        }
        return node;
    }

    private static void WriteRecords(Utf8JsonWriter writer, string name, IEnumerable<JsonElement> records)
    {
        writer.WriteStartArray(name);
        foreach (var record in records)
        {
            writer.WriteRawValue(record.GetRawText(), skipInputValidation: true);
        }
        writer.WriteEndArray();
    }

    private static void WriteText(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
}
