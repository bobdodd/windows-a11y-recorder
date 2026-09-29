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
    private readonly Dictionary<long, JsonElement> _nodes = [];

    private sealed record TransformNode(string? ParentId, double[] Matrix, bool Flattens);

    /// <summary>The view's transform node named by the latest change set.</summary>
    public string? ViewTransformNodeId { get; private set; }

    public double ViewPaintOffsetX { get; private set; }

    public double ViewPaintOffsetY { get; private set; }

    /// <summary>The last change record of each node, by node identity.</summary>
    public IReadOnlyDictionary<long, JsonElement> Nodes => _nodes;

    public int TransformNodeCount => _transforms.Count;

    internal void ApplyStarted(JsonElement payload)
    {
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
    }

    internal void ApplyNode(JsonElement payload) =>
        _nodes[payload.GetProperty("nodeId").GetInt64()] = payload.Clone();

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
