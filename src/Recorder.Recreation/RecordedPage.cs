using System.Text;
using System.Text.Json;
using Recorder.Session;

namespace Recorder.Recreation;

// The page served for a recorded document: a short document holding the
// recorded tree as JSON in a data block, which is not run, and the builder
// script, which the page's content security policy allows by a nonce. See
// docs/architecture/page-recreation.md, "Building the document exactly".
public static class RecordedPage
{
    public const string BuilderResource = "builder.js";
    public const string TreeElementId = "recorder-recreation-tree";

    // The HTML markup of the served document. The recorded document type,
    // when there is one, is written by its name only, since its identifiers
    // are not recorded: standards mode with a document type, quirks mode
    // without.
    public static string Markup(byte[] tree, string? documentTypeName, string nonce)
    {
        var markup = new StringBuilder();
        if (documentTypeName is not null)
        {
            markup.Append("<!DOCTYPE ").Append(documentTypeName).Append('>');
        }
        markup.Append("<html><head><meta charset=\"utf-8\"><script type=\"application/json\" id=\"")
            .Append(TreeElementId)
            .Append("\">")
            .Append(Encoding.UTF8.GetString(tree))
            .Append("</script><script nonce=\"")
            .Append(nonce)
            .Append("\" src=\"")
            .Append(BuilderResource)
            .Append("\" defer></script></head><body></body></html>");
        return markup.ToString();
    }

    // The content of a recreation of a recorded document at a frame, not yet
    // checked against the recording.
    public static RecreationContent Content(
        BrowserDocumentState state,
        string? url,
        long frameNanoseconds,
        long recordingNanoseconds,
        string basis)
    {
        var tree = state.Dom ?? throw new InvalidOperationException("The document has no DOM state.");
        var nonce = RecreationServer.NewToken();
        var notes = new List<string>();
        if (state.DomCompleteness is not BrowserStateCompleteness.Complete)
        {
            notes.Add($"The recorded DOM is {BrowserStateSnapshot.Name(state.DomCompleteness)} at the frame, so nodes may be missing.");
        }
        var cut = tree.Nodes.Values.Count(node => node.Data == DomTreeRebuilder.Cut || node.Attributes.ContainsValue(DomTreeRebuilder.Cut));
        if (cut > 0)
        {
            notes.Add($"{cut} nodes have an attribute value or character data cut in the recording; those values are not built.");
        }
        notes.Add("Element namespaces are not recorded: an element named in capitals is built in the HTML namespace, and any other in the namespace of an svg or math ancestor.");
        var evidence = RecordedEvidence.Create(
            state,
            url,
            frameNanoseconds,
            recordingNanoseconds,
            basis,
            new RecreationFidelity("not-checked", "The recreation is not yet compared with the recording.", []),
            notes);
        return new RecreationContent(
            Markup(Tree(state), DocumentTypeName(tree, DocumentNodeId(tree)), nonce),
            evidence,
            nonce);
    }

    public static byte[] Builder()
    {
        using var resource = typeof(RecordedPage).Assembly.GetManifestResourceStream("Builder.builder.js")
            ?? throw new InvalidOperationException("The builder script is not in the assembly.");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }

    // The document type's name: a node of type "other" among the document's
    // children before its element, the only place a document type can be.
    public static string? DocumentTypeName(DomDocumentTree tree, long documentId)
    {
        foreach (var child in tree.Nodes[documentId].Children)
        {
            if (!tree.Nodes.TryGetValue(child, out var node))
            {
                continue;
            }
            if (node.NodeType == "element")
            {
                return null;
            }
            if (node.NodeType == "other")
            {
                return node.NodeName;
            }
        }
        return null;
    }

    public static long DocumentNodeId(DomDocumentTree tree) =>
        tree.Nodes.Values.Where(node => node.NodeType == "document" && node.ParentId is null)
            .Select(node => (long?)node.Id)
            .Min() ?? throw new InvalidDataException("The recorded tree has no document node.");

    // The data the builder reads: the tree from the document node, the
    // manually assigned slots, the text controls, the scroll offsets, the
    // selection, and the focused node. Characters that could end the data
    // block, '<' among them, are written as escapes.
    public static byte[] Tree(BrowserDocumentState state)
    {
        var tree = state.Dom ?? throw new InvalidOperationException("The document has no DOM state.");
        var documentId = DocumentNodeId(tree);
        var interaction = state.Interaction.Current();
        var manualSlots = new List<(long Slot, long[] Assigned)>();
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
        }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("document");
            WriteNode(writer, tree, documentId, documentId, manualSlots, manual: false);

            writer.WriteStartArray("manualSlots");
            foreach (var (slot, assigned) in manualSlots)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(slot);
                writer.WriteStartArray();
                foreach (var id in assigned)
                {
                    writer.WriteNumberValue(id);
                }
                writer.WriteEndArray();
                writer.WriteEndArray();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("textControls");
            foreach (var control in interaction.TextControls.Values.OrderBy(item => item.NodeId))
            {
                writer.WriteStartObject();
                writer.WriteNumber("nodeId", control.NodeId);
                WriteText(writer, "value", control.Value);
                WriteNumber(writer, "selectionStart", control.SelectionStart);
                WriteNumber(writer, "selectionEnd", control.SelectionEnd);
                WriteText(writer, "selectionDirection", control.SelectionDirection);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("scrollOffsets");
            foreach (var (node, record) in state.Layout.ScrollOffsets.OrderBy(item => item.Key))
            {
                var offset = record.TryGetProperty("webExposedScrollOffset", out var exposed) && exposed.ValueKind == JsonValueKind.Object
                    ? exposed
                    : record.GetProperty("scrollOffset");
                writer.WriteStartObject();
                writer.WriteNumber("nodeId", node);
                writer.WriteNumber("x", offset.GetProperty("x").GetDouble());
                writer.WriteNumber("y", offset.GetProperty("y").GetDouble());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("selection");
            if (interaction.Selection is { AnchorNodeId: { } anchor, FocusNodeId: { } focus } selection)
            {
                writer.WriteStartObject();
                writer.WriteNumber("anchorNodeId", anchor);
                writer.WriteNumber("anchorOffset", selection.AnchorOffset ?? 0);
                writer.WriteNumber("focusNodeId", focus);
                writer.WriteNumber("focusOffset", selection.FocusOffset ?? 0);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }
            WriteNumber(writer, "focusedNodeId", interaction.FocusedNodeId);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WriteNode(
        Utf8JsonWriter writer,
        DomDocumentTree tree,
        long id,
        long documentId,
        List<(long Slot, long[] Assigned)> manualSlots,
        bool manual)
    {
        var node = tree.Nodes[id];
        writer.WriteStartObject();
        writer.WriteNumber("id", id);
        var type = node.NodeType == "other" && node.ParentId == documentId ? "document-type" : node.NodeType;
        WriteText(writer, "type", type);
        WriteText(writer, "name", node.NodeName);
        writer.WriteStartArray("attributes");
        foreach (var (key, value) in node.Attributes)
        {
            writer.WriteStartArray();
            if (key.StartsWith('{') && key.IndexOf('}') is var end and > 0)
            {
                writer.WriteStringValue(key[1..end]);
                writer.WriteStringValue(key[(end + 1)..]);
            }
            else
            {
                writer.WriteNullValue();
                writer.WriteStringValue(key);
            }
            if (value is null || value == DomTreeRebuilder.Cut)
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
        WriteText(writer, "data", node.Data == DomTreeRebuilder.Cut ? null : node.Data);
        if (node.NodeName == "SLOT" && manual && node.AssignedNodes is { } assigned && assigned != DomTreeRebuilder.Cut)
        {
            manualSlots.Add((id, JsonSerializer.Deserialize<long[]>(assigned) ?? []));
        }

        writer.WritePropertyName("shadowRoot");
        var shadow = node.ShadowRootId is { } rootId && tree.Nodes.TryGetValue(rootId, out var root) ? root : null;
        using var parsed = shadow?.ShadowRootFields is { } text ? JsonDocument.Parse($"[{text}]") : null;
        var fields = parsed?.RootElement ?? default;
        // A user agent shadow root is made by the browser itself.
        if (shadow is null || fields.ValueKind != JsonValueKind.Array || fields[1].GetString() == "user-agent")
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", shadow.Id);
            writer.WriteString("mode", fields[1].GetString());
            writer.WriteBoolean("delegatesFocus", fields[2].GetBoolean());
            writer.WriteString("slotAssignment", fields[3].GetString());
            writer.WriteBoolean("clonable", fields[4].GetBoolean());
            writer.WriteBoolean("serializable", fields[5].GetBoolean());
            writer.WritePropertyName("referenceTarget");
            fields[8].WriteTo(writer);
            WriteChildren(writer, tree, shadow, documentId, manualSlots, fields[3].GetString() == "manual");
            writer.WriteEndObject();
        }
        WriteChildren(writer, tree, node, documentId, manualSlots, manual);
        writer.WriteEndObject();
    }

    private static void WriteChildren(
        Utf8JsonWriter writer,
        DomDocumentTree tree,
        DomNode node,
        long documentId,
        List<(long Slot, long[] Assigned)> manualSlots,
        bool manual)
    {
        writer.WriteStartArray("children");
        foreach (var child in node.Children)
        {
            if (tree.Nodes.ContainsKey(child))
            {
                WriteNode(writer, tree, child, documentId, manualSlots, manual);
            }
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
}
