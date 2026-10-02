using System.Globalization;
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
        RecreationViewport? viewport = null;
        if (state.Viewport is { } recorded)
        {
            viewport = new RecreationViewport(recorded.Width, recorded.Height, recorded.DevicePixelRatio, recorded.LayoutZoomFactor);
            notes.Add($"The viewport is shown at {recorded.Width.ToString(CultureInfo.InvariantCulture)} by {recorded.Height.ToString(CultureInfo.InvariantCulture)} CSS pixels and a device pixel ratio of {recorded.DevicePixelRatio.ToString(CultureInfo.InvariantCulture)}, from the page's latest layout checkpoint, recorded at {(recorded.Time / 1e9).ToString("0.000", CultureInfo.InvariantCulture)} s. The window may have been resized after it.");
            // Chromium's layout zoom factor includes the device pixel ratio,
            // so a factor other than the ratio means the page was zoomed.
            if (Math.Abs(recorded.LayoutZoomFactor - recorded.DevicePixelRatio) > 1e-6)
            {
                notes.Add($"The recorded layout zoom factor, {recorded.LayoutZoomFactor.ToString(CultureInfo.InvariantCulture)}, differs from the device pixel ratio, so the page may have been zoomed; browser zoom is not set in the recreation.");
            }
        }
        else
        {
            notes.Add("No layout checkpoint of the page was recorded at or before the frame, so the viewport is the browser window's.");
        }
        var elements = tree.Nodes.Values.Where(node => node.NodeType == "element").ToList();
        var withLayout = elements.Count(node => state.Layout.Nodes.ContainsKey(node.Id));
        notes.Add($"{withLayout.ToString(CultureInfo.InvariantCulture)} of the {elements.Count.ToString(CultureInfo.InvariantCulture)} recorded elements have a layout record, whose recorded style and box fragments the recreation imposes. They are written on each element in its data-a11y-recorded-style and data-a11y-recorded-layout attributes, which are shown in the Elements pane but were not attributes of the recorded page. Pseudo-elements, such as ::before, take no recorded style: they appear only as far as the page's recorded style elements make them.");
        notes.Add("A box or block whose recorded layout could not be imposed, as when Blink lays out different children or text from those recorded, keeps Blink's layout, and is listed in DevTools' Console with the reason. Images draw nothing, and text in a font that is not on this machine keeps Blink's shaping in a fallback font.");
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
            nonce)
        {
            Viewport = viewport,
        };
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
            WriteNode(writer, tree, state.Layout, documentId, documentId, manualSlots, manual: false);

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
        LayoutDocumentChangeState layout,
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
        // Stage 3: an element's recorded style and box fragments, from its
        // latest layout record, which the recreation mode imposes.
        JsonElement? record = node.NodeType == "element" && layout.Nodes.TryGetValue(id, out var found) ? found : null;
        WriteText(writer, "recordedStyle", record is { } styled ? RecordedStyle(styled) : null);
        WriteText(writer, "recordedLayout", record is { } laidOut ? RecordedLayout(laidOut) : null);
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
            WriteChildren(writer, tree, layout, shadow, documentId, manualSlots, fields[3].GetString() == "manual");
            writer.WriteEndObject();
        }
        WriteChildren(writer, tree, layout, node, documentId, manualSlots, manual);
        writer.WriteEndObject();
    }

    private static void WriteChildren(
        Utf8JsonWriter writer,
        DomDocumentTree tree,
        LayoutDocumentChangeState layout,
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
                WriteNode(writer, tree, layout, child, documentId, manualSlots, manual);
            }
        }
        writer.WriteEndArray();
    }

    // The recorded computed style and custom properties of a layout record,
    // as CSS declarations, for the data-a11y-recorded-style attribute. A
    // property Blink gave no value is left out. Returns null for a record
    // without a computed style.
    public static string? RecordedStyle(JsonElement record)
    {
        if (!record.TryGetProperty("computedStyle", out var style) || style.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var declarations = new StringBuilder();
        foreach (var property in style.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                declarations.Append(property.Name).Append(": ").Append(property.Value.GetString()).Append("; ");
            }
        }
        if (record.TryGetProperty("customProperties", out var custom) && custom.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in custom.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    declarations.Append(property.Name).Append(": ").Append(property.Value.GetString()).Append("; ");
                }
            }
        }
        return declarations.Length == 0 ? null : declarations.ToString(0, declarations.Length - 1);
    }

    // The recorded box fragments of a layout record, its boxFragments object
    // as recorded, for the data-a11y-recorded-layout attribute. Returns null
    // for a record without one.
    public static string? RecordedLayout(JsonElement record) =>
        record.TryGetProperty("boxFragments", out var fragments) && fragments.ValueKind == JsonValueKind.Object
            ? fragments.GetRawText()
            : null;

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
