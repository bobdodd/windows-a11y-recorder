using System.Globalization;
using System.Text;
using System.Text.Json;
using Recorder.Session;

namespace Recorder.Recreation;

// The page served for a recorded document: a short document holding the
// recorded tree as JSON in a data block, which is not run, and the builder
// script, written into the page, which the page's content security policy
// allows by a nonce. See
// docs/architecture/page-recreation.md, "Building the document exactly".
/// <summary>
/// A page popup open at the frame, with its document's state at the frame,
/// null when none was read, and how that state was matched (slice 4d
/// sub-step 2).
/// </summary>
public sealed record RecordedPopup(PagePopupAtFrame Popup, BrowserDocumentState? State, string Basis);

/// <summary>A popup as the builder reads it: its served markup, its place in the page, and its iframe's label.</summary>
public sealed record PopupData(string Markup, PopupRect Place, string Kind, long OwnerNodeId, string Label);

public static class RecordedPage
{
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
            .Append("\">")
            .Append(BuilderText.Value)
            .Append("</script></head><body></body></html>");
        return markup.ToString();
    }

    // The builder script, written into the served page. It holds neither
    // "</script" nor "<!--", so it cannot end its script element early.
    private static readonly Lazy<string> BuilderText = new(() =>
    {
        var text = Encoding.UTF8.GetString(Builder());
        if (text.Contains("</script", StringComparison.OrdinalIgnoreCase) || text.Contains("<!--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The builder script cannot be written into a script element.");
        }
        return text;
    });

    // The content of a recreation of a recorded document at a frame, not yet
    // checked against the recording.
    public static RecreationContent Content(
        BrowserDocumentState state,
        string? url,
        long frameNanoseconds,
        long recordingNanoseconds,
        string basis,
        RecordedPageResources? resources = null,
        IReadOnlyList<RecordedPopup>? popups = null)
    {
        var tree = state.Dom ?? throw new InvalidOperationException("The document has no DOM state.");
        var nonce = RecreationServer.NewToken();
        var servedAtRecordedAddress = url is not null && RecreationServer.IsServableAddress(url);
        // The recorded fonts and images are used only when the page is
        // served at its recorded address, where the recorder answers every
        // request of the tab.
        // The content takes the resources, which the recreation's server
        // disposes; resources it does not use are disposed here.
        // The compositor values do not depend on where the page is served.
        var compositorValues = resources?.CompositorValues ?? RecordedCompositorValues.None;
        if (!servedAtRecordedAddress)
        {
            resources?.Dispose();
        }
        var used = servedAtRecordedAddress ? resources ?? RecordedPageResources.None : RecordedPageResources.None;
        var fontAddress = RecreationServer.FontAddress(RecreationServer.NewToken());
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
        var withoutLayoutObject = NoLayoutObjectDisplays(state);
        if (withoutLayoutObject.Count > 0)
        {
            var hidden = withoutLayoutObject.Values.Count(value => value == "none");
            var contents = withoutLayoutObject.Count - hidden;
            notes.Add($"{withoutLayoutObject.Count.ToString(CultureInfo.InvariantCulture)} elements had no layout object at the frame, so the recording holds no style for them. The recreation gives {hidden.ToString(CultureInfo.InvariantCulture)} of them display: none, as no node below them had a layout object, and {contents.ToString(CultureInfo.InvariantCulture)} display: contents, as a node below them had one. These values are inferred, not recorded: they are written in each element's data-a11y-recorded-no-layout-object attribute, and DevTools' Styles pane shows them as \"No layout object recorded\".");
        }
        notes.Add("A box or block whose recorded layout could not be imposed, as when Blink lays out different children or text from those recorded, keeps Blink's layout, and is listed in DevTools' Console with the reason. Text is drawn from its recorded glyphs only when the font Blink chose for it is the recorded font file, by digest, at the recorded size; other text keeps Blink's shaping, and the Console lists its block.");
        if (servedAtRecordedAddress)
        {
            notes.Add($"The page is served at its recorded address, {url}, so that its relative URLs resolve as they did. The recorder answers that address, and the page's images and the builder's font files from the recording; it refuses every other request of the page, such as a style sheet, which DevTools' Network panel lists, so nothing reaches the network.");
            notes.AddRange(used.Notes);
        }
        else
        {
            notes.Add("The recorded address is not an http or https URL, so the page is served from the recorder's loopback address, and its relative URLs do not resolve as they did.");
        }
        notes.Add("The recreation is a snapshot in time and takes no input except scrolling with its scrollbars and the wheel, the right-click that opens the context menu with Inspect, and the DevTools element picker: clicks, keys, touch, and hovering do nothing, and the page receives no wheel event, so focus, selection, and control state stay as recorded. Scrolling in the recreation changes that scroll offset from the one the recreation opened at, and DevTools then shows the moved offset.");
        notes.Add("The recreation holds the recorded moment: no CSS animation or transition is run in it, though the recorded style keeps their properties, and SVG animation elements are not held by this. The compositor's transforms, opacities, filters, backdrop filters, scroll offsets, and native paint worklet background colors and clip paths at the frame are imposed where recorded.");
        notes.AddRange(compositorValues.Notes);
        notes.AddRange(PaintWorkletAttributes(state, compositorValues).Notes);
        var scrolls = ScrollOffsets(state, compositorValues);
        notes.AddRange(scrolls.Notes);
        notes.Add("Element namespaces are not recorded: an element named in capitals is built in the HTML namespace, and any other in the namespace of an svg or math ancestor.");
        var documentId = DocumentNodeId(tree);
        var (scrollX, scrollY) = scrolls.Offsets.TryGetValue(documentId, out var rootScroll)
            ? rootScroll
            : (0, 0);
        var placed = new List<PopupData>();
        foreach (var popup in popups ?? [])
        {
            var open = popup.Popup;
            var name = $"The {open.Kind} popup of element {open.OwnerNodeId.ToString(CultureInfo.InvariantCulture)}";
            if (popup.State?.Dom is null)
            {
                notes.Add($"{name} was open at the frame, but no DOM walk of its document was recorded at or before it, so it is not drawn.");
                continue;
            }
            var place = open.InDocument(scrollX, scrollY);
            var popupTree = popup.State.Dom;
            placed.Add(new PopupData(
                Markup(Tree(popup.State), DocumentTypeName(popupTree, DocumentNodeId(popupTree)), nonce),
                place,
                open.Kind,
                open.OwnerNodeId,
                $"Recreation of the {open.Kind} popup of element {open.OwnerNodeId.ToString(CultureInfo.InvariantCulture)}"));
            var sink = open.FrameSinkId is { } joined
                ? $"joined to the browser's popup widget {joined}"
                : "joined to no popup widget record of the browser, so its window is the one the renderer asked for";
            notes.Add($"{name} was open at the frame: opened at {Seconds(open.OpenedTime)} s, {sink}; {open.OnScreenBasis}; {open.FadeBasis}. Its document is {open.DocumentKey}; {popup.Basis}. Its window rectangle in screen DIPs is {open.Window}, from the {open.WindowSource} record at {Seconds(open.WindowTime)} s, which puts it at ({Css(place.X)}, {Css(place.Y)}) in the page, {Css(place.Width)} by {Css(place.Height)} CSS pixels: the window rectangle less the owner's local root origin in screen, {open.OwnerLocalRootRectInScreen}, plus the root scroll offset at the frame, ({Css(scrollX)}, {Css(scrollY)}).");
            notes.Add($"The popup is rebuilt from its recorded DOM, styles, fragments, and glyphs in an iframe the recreation adds to the page and shows in the top layer as a manual popover, marked data-a11y-recorder-popup. The iframe and its attributes are the recreation's, not the recorded page's, and the popup's own scripts are not run.");
            notes.Add(open.AnchorMatchesOwner
                ? $"Check: the owner's visible bounds in its local root, {open.OwnerVisibleBoundsInLocalRoot}, plus the local root's origin are the popup's anchor rectangle, {open.AnchorRectInScreen}."
                : $"Check failed: the owner's visible bounds in its local root, {open.OwnerVisibleBoundsInLocalRoot}, plus the local root's origin, {open.OwnerLocalRootRectInScreen}, are not the popup's anchor rectangle, {open.AnchorRectInScreen}, so the popup's place may be wrong.");
            if (Math.Abs(open.ZoomFactor - 1) > 1e-6)
            {
                notes.Add($"The popup was opened with a zoom factor of {open.ZoomFactor.ToString(CultureInfo.InvariantCulture)}, which the recreation does not apply to its iframe.");
            }
        }
        var evidence = RecordedEvidence.Create(
            state,
            url,
            frameNanoseconds,
            recordingNanoseconds,
            basis,
            new RecreationFidelity("not-checked", "The recreation is not yet compared with the recording.", []),
            notes);
        return new RecreationContent(
            Markup(Tree(state, used.Faces, fontAddress, placed, compositorValues), DocumentTypeName(tree, documentId), nonce),
            evidence,
            nonce)
        {
            Viewport = viewport,
            DocumentUrl = servedAtRecordedAddress ? url : null,
            Resources = used,
            FontAddress = servedAtRecordedAddress ? fontAddress : null,
        };
    }

    /// <summary>
    /// The data-a11y-recorded-paint-worklet attribute of each node with a
    /// chosen paint worklet value (slice 4b, "Sub-step 2c design: paint
    /// worklet colors and clip paths imposed"), and what the evidence panel
    /// says of them. The attribute is its values separated by "; ": a
    /// background color as "background-color" and its four floats; a clip
    /// path as "clip-path", the recorded origin of the node's border box in
    /// its transform space, and the path. A clip path is left out when the
    /// node's recorded style gives it no clip path, when it was laid out in
    /// more than one fragment, or when its border box was not recorded.
    /// </summary>
    public static (IReadOnlyDictionary<long, string> Attributes, IReadOnlyList<string> Notes) PaintWorkletAttributes(
        BrowserDocumentState state,
        RecordedCompositorValues? compositorValues)
    {
        var attributes = new Dictionary<long, string>();
        var noClipPath = new List<string>();
        var fragmented = new List<string>();
        var noOrigin = new List<string>();
        foreach (var (node, values) in (compositorValues ?? RecordedCompositorValues.None).PaintWorklet.OrderBy(item => item.Key))
        {
            var nodeText = node.ToString(CultureInfo.InvariantCulture);
            JsonElement? record = state.Layout.Nodes.TryGetValue(node, out var found) ? found : null;
            var entries = new List<string>();
            foreach (var value in values)
            {
                if (value.Property != "clip-path")
                {
                    entries.Add($"{value.Property} {value.Text}");
                    continue;
                }
                // The recorded style must give the element a clip path, so
                // that the recreation has a clip path node to impose it on.
                if (record is not { } styled ||
                    !styled.TryGetProperty("computedStyle", out var style) || style.ValueKind != JsonValueKind.Object ||
                    !style.TryGetProperty("clip-path", out var clip) || clip.ValueKind != JsonValueKind.String ||
                    clip.GetString() is null or "none")
                {
                    noClipPath.Add(nodeText);
                    continue;
                }
                if (styled.TryGetProperty("boxFragments", out var boxes) && boxes.ValueKind == JsonValueKind.Object &&
                    boxes.TryGetProperty("fragments", out var list) && list.ValueKind == JsonValueKind.Array &&
                    list.GetArrayLength() > 1)
                {
                    fragmented.Add(nodeText);
                    continue;
                }
                // The recorded path holds the element's paint offset, which
                // is the origin of its border box in its transform space.
                if (!styled.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object ||
                    !geometry.TryGetProperty("localRect", out var rect) || rect.ValueKind != JsonValueKind.Object ||
                    !rect.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Number ||
                    !rect.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.Number)
                {
                    noOrigin.Add(nodeText);
                    continue;
                }
                entries.Add($"clip-path {x.GetRawText()} {y.GetRawText()} {value.Text}");
            }
            if (entries.Count > 0)
            {
                attributes[node] = string.Join("; ", entries);
            }
        }
        var notes = new List<string>();
        if (noClipPath.Count > 0)
        {
            notes.Add($"The recorded style gives no clip path to {noClipPath.Count.ToString(CultureInfo.InvariantCulture)} elements whose paint worklet clip path was recorded, so the recreation has no clip path to impose it on, and they are drawn unclipped: nodes {string.Join(", ", noClipPath)}.");
        }
        if (fragmented.Count > 0)
        {
            notes.Add($"{fragmented.Count.ToString(CultureInfo.InvariantCulture)} elements with a recorded paint worklet clip path were laid out in more than one fragment, for which one recorded path does not say where each fragment is clipped, so their clip paths are drawn from the recorded style: nodes {string.Join(", ", fragmented)}.");
        }
        if (noOrigin.Count > 0)
        {
            notes.Add($"{noOrigin.Count.ToString(CultureInfo.InvariantCulture)} elements with a recorded paint worklet clip path have no recorded border box in their transform space, so their clip paths are drawn from the recorded style: nodes {string.Join(", ", noOrigin)}.");
        }
        return (attributes, notes);
    }

    // A scroll offset record's offset: the web-exposed offset when recorded.
    /// <summary>
    /// The offset each recorded scroller is scrolled to, in CSS pixels, and
    /// what the evidence panel says of them (slice 4b sub-step 2b-ii): the
    /// compositor's drawn position at the frame, less the scroll origin and
    /// divided by the effective zoom of the scroller's latest main thread
    /// record, where that record names the scroll node's element ID, whether
    /// or not the compositor scrolled the node itself (sub-step 2b-iii
    /// change 1 withdrawn); otherwise the main thread's offset.
    /// </summary>
    public static (IReadOnlyDictionary<long, (double X, double Y)> Offsets, IReadOnlyList<string> Notes) ScrollOffsets(
        BrowserDocumentState state,
        RecordedCompositorValues? compositorValues)
    {
        var positions = (compositorValues ?? RecordedCompositorValues.None).ScrollPositions;
        var offsets = new Dictionary<long, (double X, double Y)>();
        var imposed = new List<string>();
        var painted = new List<string>();
        var joined = new HashSet<string>(StringComparer.Ordinal);
        var named = 0;
        foreach (var (node, record) in state.Layout.ScrollOffsets.OrderBy(item => item.Key))
        {
            var main = ScrollOffsetOf(record);
            offsets[node] = main;
            if (!record.TryGetProperty("scrollElementId", out var element))
            {
                continue;
            }
            named++;
            if (element.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            if (element.GetString() is not { } elementId || !positions.TryGetValue(elementId, out var drawn))
            {
                continue;
            }
            joined.Add(elementId);
            var nodeText = node.ToString(CultureInfo.InvariantCulture);
            var origin = record.GetProperty("scrollOrigin");
            var zoom = record.GetProperty("effectiveZoom").GetDouble();
            var offset = ((drawn.X - origin.GetProperty("x").GetDouble()) / zoom,
                          (drawn.Y - origin.GetProperty("y").GetDouble()) / zoom);
            offsets[node] = offset;
            imposed.Add($"node {nodeText} at ({Css(offset.Item1)}, {Css(offset.Item2)}), last changed in compositor frame {drawn.FrameToken}, in place of the main thread's ({Css(main.X)}, {Css(main.Y)})");
            if (drawn.IsComposited == false)
            {
                painted.Add($"node {nodeText}, with {(drawn.RepaintReasons.Length == 0 ? "no repaint reason recorded" : $"the repaint reasons {drawn.RepaintReasons}")}");
            }
        }
        var notes = new List<string>();
        if (state.Layout.ScrollOffsets.Count > 0 && named == 0)
        {
            notes.Add("The recording's scroll offset records do not name their scrollers' compositor element IDs, as before protocol 0.49, so every scroller is at its main thread offset.");
        }
        if (imposed.Count > 0)
        {
            notes.Add($"{imposed.Count.ToString(CultureInfo.InvariantCulture)} scrollers are scrolled to the offset the compositor drew at the frame, its position less the scroll origin and divided by the effective zoom of the scroller's latest main thread record: {string.Join("; ", imposed)}. The builder scrolls each once, after the page is built; the recreation's layout is still the main thread's recorded layout.");
        }
        if (painted.Count > 0)
        {
            notes.Add($"Of these, the compositor did not scroll {painted.Count.ToString(CultureInfo.InvariantCulture)} itself at the frame, so the main thread painted their content: {string.Join("; ", painted)}. They are given the compositor's position all the same, as captured frames of one machine's recordings showed such a scroller where the compositor drew it, not at the main thread's offset, which differed from it by up to 170 px during fast scrolling.");
        }
        var unjoined = positions.Where(item => !joined.Contains(item.Key)).ToList();
        var moved = unjoined.Count(item => item.Value.X != 0 || item.Value.Y != 0);
        if (named > 0 && moved > 0)
        {
            notes.Add($"{moved.ToString(CultureInfo.InvariantCulture)} scroll nodes of the compositor were at a nonzero offset at the frame but are named by no main thread scroll offset record of the page, such as the visual viewport's, so their offsets are not imposed.");
        }
        return (offsets, notes);
    }

    private static (double X, double Y) ScrollOffsetOf(JsonElement record)
    {
        var offset = record.TryGetProperty("webExposedScrollOffset", out var exposed) && exposed.ValueKind == JsonValueKind.Object
            ? exposed
            : record.GetProperty("scrollOffset");
        return (offset.GetProperty("x").GetDouble(), offset.GetProperty("y").GetDouble());
    }

    private static string Seconds(long nanoseconds) => (nanoseconds / 1e9).ToString("0.000", CultureInfo.InvariantCulture);

    private static string Css(double value) => value.ToString(CultureInfo.InvariantCulture);

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
    // selection, the focused node, and the font faces the builder adds before
    // it builds the tree (sub-step 3), each with the address of its font
    // file at the recorder. Characters that could end the data block, '<'
    // among them, are written as escapes.
    public static byte[] Tree(
        BrowserDocumentState state,
        IReadOnlyList<RecordedFontFace>? faces = null,
        string? fontAddress = null,
        IReadOnlyList<PopupData>? popups = null,
        RecordedCompositorValues? compositorValues = null)
    {
        var tree = state.Dom ?? throw new InvalidOperationException("The document has no DOM state.");
        var documentId = DocumentNodeId(tree);
        var compositor = compositorValues ?? RecordedCompositorValues.None;
        var interaction = state.Interaction.Current();
        var manualSlots = new List<(long Slot, long[] Assigned)>();
        var withoutLayoutObject = NoLayoutObjectDisplays(state);
        var paintWorklet = PaintWorkletAttributes(state, compositor).Attributes;
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
        }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("document");
            WriteNode(writer, tree, state.Layout, withoutLayoutObject, compositor, paintWorklet, documentId, documentId, manualSlots, manual: false);

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
            foreach (var (node, (x, y)) in ScrollOffsets(state, compositor).Offsets.OrderBy(item => item.Key))
            {
                writer.WriteStartObject();
                writer.WriteNumber("nodeId", node);
                writer.WriteNumber("x", x);
                writer.WriteNumber("y", y);
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

            // Slice 4d sub-step 2: each option's latest recorded
            // selectedness, as [node, selected], and the page popups open at
            // the frame, each with its own served markup and its place.
            writer.WriteStartArray("optionSelectedness");
            foreach (var (node, selected) in state.Interaction.OptionSelectedness.OrderBy(item => item.Key))
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(node);
                writer.WriteBooleanValue(selected);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("popups");
            foreach (var popup in popups ?? [])
            {
                writer.WriteStartObject();
                writer.WriteString("markup", popup.Markup);
                writer.WriteNumber("left", popup.Place.X);
                writer.WriteNumber("top", popup.Place.Y);
                writer.WriteNumber("width", popup.Place.Width);
                writer.WriteNumber("height", popup.Place.Height);
                writer.WriteString("kind", popup.Kind);
                writer.WriteNumber("ownerNodeId", popup.OwnerNodeId);
                writer.WriteString("label", popup.Label);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("fontFaces");
            foreach (var face in fontAddress is null ? [] : faces ?? [])
            {
                writer.WriteStartObject();
                writer.WriteString("family", face.Family);
                writer.WriteStartObject("descriptors");
                foreach (var (name, value) in face.Descriptors)
                {
                    writer.WriteString(name, value);
                }
                writer.WriteEndObject();
                writer.WriteString("digest", face.Digest);
                writer.WriteString("url", fontAddress + face.Digest);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WriteNode(
        Utf8JsonWriter writer,
        DomDocumentTree tree,
        LayoutDocumentChangeState layout,
        IReadOnlyDictionary<long, string> withoutLayoutObject,
        RecordedCompositorValues compositor,
        IReadOnlyDictionary<long, string> paintWorklet,
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
        WriteText(writer, "recordedLayout", record is { } laidOut ? RecordedLayout(laidOut, id) : null);
        WriteText(writer, "noLayoutObject", withoutLayoutObject.TryGetValue(id, out var display) ? display : null);
        // Slice 4b sub-step 2b-i: the compositor values the element takes.
        WriteText(writer, "recordedCompositor", node.NodeType == "element" ? compositor.Attribute(id) : null);
        // Sub-step 2c: the paint worklet values the element takes.
        WriteText(writer, "recordedPaintWorklet", node.NodeType == "element" && paintWorklet.TryGetValue(id, out var worklet) ? worklet : null);
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
            WriteChildren(writer, tree, layout, withoutLayoutObject, compositor, paintWorklet, shadow, documentId, manualSlots, fields[3].GetString() == "manual");
            writer.WriteEndObject();
        }
        WriteChildren(writer, tree, layout, withoutLayoutObject, compositor, paintWorklet, node, documentId, manualSlots, manual);
        writer.WriteEndObject();
    }

    private static void WriteChildren(
        Utf8JsonWriter writer,
        DomDocumentTree tree,
        LayoutDocumentChangeState layout,
        IReadOnlyDictionary<long, string> withoutLayoutObject,
        RecordedCompositorValues compositor,
        IReadOnlyDictionary<long, string> paintWorklet,
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
                WriteNode(writer, tree, layout, withoutLayoutObject, compositor, paintWorklet, child, documentId, manualSlots, manual);
            }
        }
        writer.WriteEndArray();
    }

    // The display each element without a layout object at the frame takes in
    // the recreation, for its data-a11y-recorded-no-layout-object attribute:
    // an element whose latest layout record states no layout object takes
    // "none" when no node in its subtree or shadow trees had one, and
    // "contents" when one did, as for an element styled display: contents.
    // The value is inferred from the recorded layout objects; the recording
    // holds no computed style for such an element. An element without a
    // layout record is left out.
    public static IReadOnlyDictionary<long, string> NoLayoutObjectDisplays(BrowserDocumentState state)
    {
        var tree = state.Dom ?? throw new InvalidOperationException("The document has no DOM state.");
        var layout = state.Layout.Nodes;
        var displays = new Dictionary<long, string>();
        var rendered = new Dictionary<long, bool>();
        // Iterative, so that a deep tree cannot exhaust the stack: a node is
        // finished once all its children and its shadow root are.
        var pending = new Stack<(long Id, bool Expanded)>();
        pending.Push((DocumentNodeId(tree), false));
        while (pending.Count > 0)
        {
            var (id, expanded) = pending.Pop();
            if (!tree.Nodes.TryGetValue(id, out var node) || rendered.ContainsKey(id) && !expanded)
            {
                continue;
            }
            var below = node.Children.Where(tree.Nodes.ContainsKey).ToList();
            if (node.ShadowRootId is { } shadow && tree.Nodes.ContainsKey(shadow))
            {
                below.Add(shadow);
            }
            if (!expanded)
            {
                pending.Push((id, true));
                foreach (var child in below)
                {
                    pending.Push((child, false));
                }
                continue;
            }
            var hasRecord = layout.TryGetValue(id, out var record);
            var present = hasRecord &&
                record.TryGetProperty("layoutObjectPresent", out var flag) &&
                flag.ValueKind == JsonValueKind.True;
            var renderedBelow = below.Any(child => rendered.TryGetValue(child, out var value) && value);
            rendered[id] = present || renderedBelow;
            if (node.NodeType == "element" && hasRecord && !present)
            {
                displays[id] = renderedBelow ? "contents" : "none";
            }
        }
        return displays;
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
    // as recorded, for the data-a11y-recorded-layout attribute, with the
    // element's recorded node first, as "node", so that the box hook can
    // match a child box to its parent's recorded child link by reading only
    // the start of the attribute (slice 4a). Returns null for a record
    // without box fragments.
    public static string? RecordedLayout(JsonElement record, long nodeId)
    {
        if (!record.TryGetProperty("boxFragments", out var fragments) || fragments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var text = new StringBuilder("{\"node\":").Append(nodeId.ToString(CultureInfo.InvariantCulture));
        foreach (var property in fragments.EnumerateObject())
        {
            if (property.NameEquals("node"))
            {
                continue;
            }
            text.Append(',').Append(JsonSerializer.Serialize(property.Name)).Append(':').Append(property.Value.GetRawText());
        }
        return text.Append('}').ToString();
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
