using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// A rectangle derived from layout change records: the value
/// getBoundingClientRect would return, in CSS pixels relative to the frame's
/// viewport. It is computed from the recorded geometry and transform nodes,
/// not observed, and is labelled as derived wherever it is shown.
/// </summary>
public readonly record struct LayoutDerivedRect(double X, double Y, double Width, double Height);

/// <summary>
/// Why a node's client rectangle could not be derived.
/// </summary>
public enum LayoutDerivationFailure
{
    None,
    NodeNotRecorded,
    NoLayoutObject,
    RectangleNotMapped,
    TransformNodeNotRecorded,
    TransformChainDoesNotReachView,
    ProjectionNotInvertible,
}

/// <summary>
/// The state of each document rebuilt from its layout change records
/// (protocol 0.32), in record order: the last record of every node and
/// transform node, and the view named by the latest change set. Node records
/// are kept as the payloads they were read from.
/// </summary>
public sealed class LayoutChangeState
{
    private readonly Dictionary<string, LayoutDocumentChangeState> _documents =
        new(StringComparer.Ordinal);

    /// <summary>The documents with change records, by document token.</summary>
    public IReadOnlyDictionary<string, LayoutDocumentChangeState> Documents => _documents;

    /// <summary>
    /// Applies one browser.layout record. Records of other types are
    /// ignored. Returns the document token the record belongs to, or null.
    /// </summary>
    public string? Apply(string eventType, JsonElement payload)
    {
        if (eventType is not ("layout-changes-started" or "layout-transform-node" or
            "layout-node-changed" or "layout-changes-completed"))
        {
            return null;
        }
        var token = DocumentToken(payload);
        if (token is null)
        {
            return null;
        }
        if (!_documents.TryGetValue(token, out var document))
        {
            document = new LayoutDocumentChangeState();
            _documents.Add(token, document);
        }
        switch (eventType)
        {
            case "layout-changes-started":
                document.ApplyStarted(payload);
                break;
            case "layout-transform-node":
                document.ApplyTransformNode(payload);
                break;
            case "layout-node-changed":
                document.ApplyNode(payload);
                break;
        }
        return token;
    }

    /// <summary>The document token of a browser record's context, or null.</summary>
    public static string? DocumentToken(JsonElement payload) =>
        payload.TryGetProperty("context", out var context) &&
        context.ValueKind == JsonValueKind.Object &&
        context.TryGetProperty("documentToken", out var token) &&
        token.ValueKind == JsonValueKind.String
            ? token.GetString()
            : null;
}

/// <summary>The rebuilt layout state of one document.</summary>
public sealed class LayoutDocumentChangeState
{
    private readonly Dictionary<string, TransformNode> _transforms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _transformRecords = new(StringComparer.Ordinal);
    private readonly Dictionary<long, JsonElement> _nodes = [];
    private readonly Dictionary<long, JsonElement> _scrollOffsets = [];

    private sealed record TransformNode(string? ParentId, double[] Matrix, bool Flattens);

    /// <summary>The view's transform node named by the latest change set.</summary>
    public string? ViewTransformNodeId { get; private set; }

    public double ViewPaintOffsetX { get; private set; }

    public double ViewPaintOffsetY { get; private set; }

    /// <summary>The layout zoom factor named by the latest change set, or null before one.</summary>
    public double? LayoutZoomFactor { get; private set; }

    /// <summary>The start record of the latest change set, or null before one.</summary>
    public JsonElement? LastStarted { get; private set; }

    /// <summary>True between a change set's start record and its completion.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The last record of each transform node, by transform node identity.</summary>
    public IReadOnlyDictionary<string, JsonElement> TransformRecords => _transformRecords;

    /// <summary>The last scroll offset record of each scrolling node, by node identity.</summary>
    public IReadOnlyDictionary<long, JsonElement> ScrollOffsets => _scrollOffsets;

    /// <summary>The last change record of each node, by node identity.</summary>
    public IReadOnlyDictionary<long, JsonElement> Nodes => _nodes;

    public int TransformNodeCount => _transforms.Count;

    /// <summary>
    /// Applies one browser.layout change record of this document: a change
    /// set's start, transform node, node, and scroll offset records, and its
    /// completion. Other records are ignored.
    /// </summary>
    public void Apply(string eventType, JsonElement payload)
    {
        switch (eventType)
        {
            case "layout-changes-started":
                ApplyStarted(payload);
                break;
            case "layout-transform-node":
                ApplyTransformNode(payload);
                break;
            case "layout-node-changed":
                ApplyNode(payload);
                break;
            case "layout-scroll-offset-changed":
                _scrollOffsets[payload.GetProperty("nodeId").GetInt64()] = payload.Clone();
                break;
            case "layout-changes-completed":
                IsOpen = false;
                break;
        }
    }

    // Restores the state a snapshot holds: the latest change set's start,
    // and the last record of each transform node, node, and scroll offset.
    internal void Load(
        JsonElement? started,
        IEnumerable<JsonElement> transforms,
        IEnumerable<JsonElement> nodes,
        IEnumerable<JsonElement> scrollOffsets)
    {
        if (started is { } start)
        {
            ApplyStarted(start);
            IsOpen = false;
        }
        foreach (var transform in transforms)
        {
            ApplyTransformNode(transform);
        }
        // A snapshot holds each node's merged record, which is stored as it
        // is: one whose style is not whole is not a record of changes.
        foreach (var node in nodes)
        {
            _nodes[node.GetProperty("nodeId").GetInt64()] = node.Clone();
        }
        foreach (var scroll in scrollOffsets)
        {
            _scrollOffsets[scroll.GetProperty("nodeId").GetInt64()] = scroll.Clone();
        }
    }

    internal void ApplyStarted(JsonElement payload)
    {
        LastStarted = payload.Clone();
        IsOpen = true;
        LayoutZoomFactor = payload.TryGetProperty("layoutZoomFactor", out var zoom) &&
            zoom.ValueKind == JsonValueKind.Number
                ? zoom.GetDouble()
                : null;
        ViewTransformNodeId = payload.GetProperty("viewTransformNodeId").GetString();
        var offset = payload.GetProperty("viewPaintOffset");
        ViewPaintOffsetX = offset.GetProperty("x").GetDouble();
        ViewPaintOffsetY = offset.GetProperty("y").GetDouble();
    }

    internal void ApplyTransformNode(JsonElement payload)
    {
        var id = payload.GetProperty("transformNodeId").GetString()!;
        var parent = payload.GetProperty("parentTransformNodeId");
        var matrix = payload.GetProperty("matrix").EnumerateArray()
            .Select(value => value.GetDouble())
            .ToArray();
        _transforms[id] = new TransformNode(
            parent.ValueKind == JsonValueKind.String ? parent.GetString() : null,
            matrix,
            payload.GetProperty("flattensInheritedTransform").GetBoolean());
        _transformRecords[id] = payload.Clone();
    }

    // From protocol 0.37 a node record after the node's first holds only the
    // style values that changed. The state keeps each node's whole style: a
    // record of changes is merged into the node's last record, and the
    // merged record states that it is complete only when that last record
    // was. A merged record is then what a snapshot holds.
    internal void ApplyNode(JsonElement payload)
    {
        var nodeId = payload.GetProperty("nodeId").GetInt64();
        if (payload.TryGetProperty("computedStyleComplete", out var complete) &&
            complete.ValueKind == JsonValueKind.False)
        {
            _nodes[nodeId] = MergeStyleChanges(
                _nodes.TryGetValue(nodeId, out var last) ? last : null,
                payload);
            return;
        }
        _nodes[nodeId] = payload.Clone();
    }

    internal static JsonElement MergeStyleChanges(JsonElement? last, JsonElement changes)
    {
        var lastStyle = last is { } record &&
            record.TryGetProperty("computedStyle", out var style) &&
            style.ValueKind == JsonValueKind.Object
                ? style
                : (JsonElement?)null;
        var lastCustom = last is { } customRecord &&
            customRecord.TryGetProperty("customProperties", out var custom) &&
            custom.ValueKind == JsonValueKind.Object
                ? custom
                : (JsonElement?)null;
        // The merged style is whole only when the last record's was: a node
        // whose first record was lost, or whose last record had no style,
        // keeps the changed values alone and says so.
        var baseComplete = lastStyle is not null &&
            (!last!.Value.TryGetProperty("computedStyleComplete", out var lastComplete) ||
                lastComplete.ValueKind != JsonValueKind.False);
        var removed = changes.TryGetProperty("removedCustomProperties", out var names) &&
            names.ValueKind == JsonValueKind.Array
                ? names.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in changes.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "computedStyle":
                        writer.WritePropertyName(property.Name);
                        WriteMerged(writer, lastStyle, property.Value, removed: null);
                        break;
                    case "customProperties":
                        writer.WritePropertyName(property.Name);
                        WriteMerged(writer, lastCustom, property.Value, removed);
                        break;
                    case "computedStyleComplete":
                        writer.WriteBoolean(property.Name, baseComplete);
                        break;
                    case "removedCustomProperties":
                        writer.WriteNull(property.Name);
                        break;
                    default:
                        property.WriteTo(writer);
                        break;
                }
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    // Writes the last values with each changed value in its place, in the
    // last record's order, then the values the last record did not hold.
    private static void WriteMerged(
        Utf8JsonWriter writer,
        JsonElement? last,
        JsonElement changed,
        HashSet<string>? removed)
    {
        var changes = changed.ValueKind == JsonValueKind.Object
            ? changed.EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var written = new HashSet<string>(StringComparer.Ordinal);
        writer.WriteStartObject();
        if (last is { } values)
        {
            foreach (var property in values.EnumerateObject())
            {
                if (removed is not null && removed.Contains(property.Name))
                {
                    continue;
                }
                writer.WritePropertyName(property.Name);
                (changes.TryGetValue(property.Name, out var value) ? value : property.Value).WriteTo(writer);
                written.Add(property.Name);
            }
        }
        foreach (var (name, value) in changes)
        {
            if (written.Add(name))
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Derives the client rectangle of a node from its last change record:
    /// its local rectangle is mapped from its transform node to the view's
    /// node the way Blink's GeometryMapper projects between them, the view's
    /// paint offset is subtracted, and the bounds are scaled to CSS pixels.
    /// </summary>
    public LayoutDerivedRect? DeriveClientRect(long nodeId, out LayoutDerivationFailure failure)
    {
        if (!_nodes.TryGetValue(nodeId, out var node))
        {
            failure = LayoutDerivationFailure.NodeNotRecorded;
            return null;
        }
        if (!node.TryGetProperty("geometry", out var geometry) ||
            geometry.ValueKind != JsonValueKind.Object)
        {
            failure = LayoutDerivationFailure.NoLayoutObject;
            return null;
        }
        if (geometry.GetProperty("clientRectEmpty").GetBoolean())
        {
            failure = LayoutDerivationFailure.None;
            return new LayoutDerivedRect(0, 0, 0, 0);
        }
        if (!geometry.GetProperty("localRectMapped").GetBoolean())
        {
            failure = LayoutDerivationFailure.RectangleNotMapped;
            return null;
        }
        var toView = ToView(geometry.GetProperty("transformNodeId").GetString()!, out failure);
        if (toView is null)
        {
            return null;
        }
        // Blink unites the bounds of each quad mapped to the viewport. A node
        // with more than one quad states the bounds of each (protocol 0.36);
        // otherwise the local rectangle is the one quad's bounds.
        var rects = new List<JsonElement>();
        if (geometry.TryGetProperty("localQuadRects", out var quadRects) &&
            quadRects.ValueKind == JsonValueKind.Array)
        {
            rects.AddRange(quadRects.EnumerateArray());
        }
        else
        {
            rects.Add(geometry.GetProperty("localRect"));
        }
        var scale = geometry.GetProperty("clientRectScale").GetDouble();
        // United as gfx::RectF::Union unites: an empty rectangle is taken only
        // while the union is empty, and is otherwise skipped.
        double left = 0, top = 0, right = 0, bottom = 0;
        foreach (var rect in rects)
        {
            var x = rect.GetProperty("x").GetDouble();
            var y = rect.GetProperty("y").GetDouble();
            var width = rect.GetProperty("width").GetDouble();
            var height = rect.GetProperty("height").GetDouble();
            double rectLeft = double.PositiveInfinity, rectTop = double.PositiveInfinity;
            double rectRight = double.NegativeInfinity, rectBottom = double.NegativeInfinity;
            foreach (var (cornerX, cornerY) in new[]
                     {
                         (x, y), (x + width, y), (x, y + height), (x + width, y + height)
                     })
            {
                if (!MapPoint(toView, cornerX, cornerY, out var mappedX, out var mappedY))
                {
                    failure = LayoutDerivationFailure.ProjectionNotInvertible;
                    return null;
                }
                rectLeft = Math.Min(rectLeft, mappedX);
                rectTop = Math.Min(rectTop, mappedY);
                rectRight = Math.Max(rectRight, mappedX);
                rectBottom = Math.Max(rectBottom, mappedY);
            }
            var unionEmpty = right <= left || bottom <= top;
            if (unionEmpty)
            {
                (left, top, right, bottom) = (rectLeft, rectTop, rectRight, rectBottom);
            }
            else if (rectRight > rectLeft && rectBottom > rectTop)
            {
                left = Math.Min(left, rectLeft);
                top = Math.Min(top, rectTop);
                right = Math.Max(right, rectRight);
                bottom = Math.Max(bottom, rectBottom);
            }
        }
        failure = LayoutDerivationFailure.None;
        return new LayoutDerivedRect(
            (left - ViewPaintOffsetX) * scale,
            (top - ViewPaintOffsetY) * scale,
            (right - left) * scale,
            (bottom - top) * scale);
    }

    // The flattened transform from a node's space to the view's space. The
    // view's own matrix is not applied: the records' geometry is relative to
    // the view's space, as the checkpoint's is to the viewport.
    private double[]? ToView(string transformNodeId, out LayoutDerivationFailure failure)
    {
        var chain = new List<TransformNode>();
        var current = transformNodeId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current != ViewTransformNodeId)
        {
            if (current is null || !visited.Add(current))
            {
                failure = LayoutDerivationFailure.TransformChainDoesNotReachView;
                return null;
            }
            if (!_transforms.TryGetValue(current, out var node))
            {
                failure = LayoutDerivationFailure.TransformNodeNotRecorded;
                return null;
            }
            chain.Add(node);
            current = node.ParentId;
        }
        // Accumulated from the view down: each node's matrix applies after
        // its ancestors', flattening the inherited transform where the node
        // flattens it, as the transform cache of GeometryMapper does.
        var accumulated = Identity();
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var node = chain[index];
            if (node.Flattens)
            {
                accumulated = Flatten(accumulated);
            }
            accumulated = Multiply(accumulated, node.Matrix);
        }
        failure = LayoutDerivationFailure.None;
        return Flatten(accumulated);
    }

    // Matrices are 16 values in column-major order: value[column * 4 + row].
    internal static double[] Identity() =>
        [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    internal static double[] Multiply(double[] left, double[] right)
    {
        var result = new double[16];
        for (var column = 0; column < 4; column++)
        {
            for (var row = 0; row < 4; row++)
            {
                double sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += left[k * 4 + row] * right[column * 4 + k];
                }
                result[column * 4 + row] = sum;
            }
        }
        return result;
    }

    // gfx::Transform::Flatten: the z row and column become those of the
    // identity, so a point is mapped onto the plane z = 0.
    internal static double[] Flatten(double[] matrix)
    {
        var result = (double[])matrix.Clone();
        result[0 * 4 + 2] = 0;
        result[1 * 4 + 2] = 0;
        result[3 * 4 + 2] = 0;
        result[2 * 4 + 0] = 0;
        result[2 * 4 + 1] = 0;
        result[2 * 4 + 3] = 0;
        result[2 * 4 + 2] = 1;
        return result;
    }

    internal static bool MapPoint(double[] matrix, double x, double y, out double mappedX, out double mappedY)
    {
        var w = matrix[0 * 4 + 3] * x + matrix[1 * 4 + 3] * y + matrix[3 * 4 + 3];
        mappedX = mappedY = 0;
        if (!double.IsFinite(w) || Math.Abs(w) < 1e-12)
        {
            return false;
        }
        mappedX = (matrix[0 * 4 + 0] * x + matrix[1 * 4 + 0] * y + matrix[3 * 4 + 0]) / w;
        mappedY = (matrix[0 * 4 + 1] * x + matrix[1 * 4 + 1] * y + matrix[3 * 4 + 1]) / w;
        return double.IsFinite(mappedX) && double.IsFinite(mappedY);
    }
}
