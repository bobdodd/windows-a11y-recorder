using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public static class AndroidInteractionShapes
{
    public const int Limit = 100;

    /// <summary>Compact connected contrast edges, not a classifier of clickable regions.</summary>
    public static IReadOnlyList<AndroidInteractionCandidate> Find(AndroidLivePreviewFrame frame,
        AndroidLiveWindowSnapshot app, IEnumerable<AndroidInteractionCandidate> known,
        IReadOnlyList<AndroidNodeBounds> overlays, out bool limited)
    {
        limited = false;
        if (frame.Width < 12 || frame.Height < 12 || frame.Rgb.Length != (long)frame.Width * frame.Height * 3)
        {
            return [];
        }
        var excluded = known.SelectMany(candidate => candidate.Boxes).ToArray();
        var password = app.Items?.Nodes.Where(node => node.Password).Select(node => node.Bounds).ToArray() ?? [];
        var stride = Math.Max(1, (Math.Max(frame.Width, frame.Height) + 511) / 512);
        var w = frame.Width / stride;
        var h = frame.Height / stride;
        var edge = new bool[w * h];
        var pixels = frame.Rgb.Span;
        for (var y = 0; y < h - 1; y++)
        {
            for (var x = 0; x < w - 1; x++)
            {
                var i = (y * frame.Width + x) * stride * 3;
                edge[y * w + x] = Different(pixels, i, i + stride * 3) ||
                    Different(pixels, i, i + frame.Width * stride * 3);
            }
        }
        var result = new List<AndroidInteractionCandidate>();
        var group = new Queue<int>();
        for (var i = 0; i < edge.Length; i++)
        {
            if (!edge[i])
            {
                continue;
            }
            edge[i] = false;
            group.Enqueue(i);
            int left = i % w, right = left, top = i / w, bottom = top, count = 0;
            while (group.TryDequeue(out var cell))
            {
                int x = cell % w, y = cell / w;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
                count++;
                for (var row = Math.Max(0, y - 1); row <= Math.Min(h - 1, y + 1); row++)
                {
                    for (var col = Math.Max(0, x - 1); col <= Math.Min(w - 1, x + 1); col++)
                    {
                        var next = row * w + col;
                        if (edge[next])
                        {
                            edge[next] = false;
                            group.Enqueue(next);
                        }
                    }
                }
            }
            var dx = right - left + 1;
            var dy = bottom - top + 1;
            if (dx < 4 || dy < 4 || dx > 8 * dy || dy > 8 * dx || dx * stride < 18 || dy * stride < 18 ||
                dx > w * .90 || dy > h * .35 || count < 10 || count > (dx + dy) * 7)
            {
                continue;
            }
            var box = new AndroidNodeBounds(left * stride, top * stride, (right + 2) * stride, (bottom + 2) * stride);
            if (!AndroidInteractionInventory.Contains(app.Window.Bounds, box) ||
                overlays.Any(other => AndroidInteractionInventory.Intersection(other, box) is not null) ||
                password.Any(other => AndroidInteractionInventory.Intersection(other, box) is not null) ||
                excluded.Any(other =>
                    AndroidInteractionInventory.Intersection(other, box) is { } overlap &&
                    Area(overlap) * 2 >= Math.Min(Area(box), Area(other)) &&
                    // A large scroll or canvas container is not evidence that its interior has no private targets.
                    Area(other) <= Area(box) * 16))
            {
                continue;
            }
            if (result.Count == Limit)
            {
                limited = true;
                break;
            }
            result.Add(new("Unlabelled screen shape", "Visible contrast outline; may be decoration", [box], null, null));
        }
        return result;
    }

    private static long Area(AndroidNodeBounds bounds) =>
        (long)(bounds.Right - bounds.Left) * (bounds.Bottom - bounds.Top);

    private static bool Different(ReadOnlySpan<byte> rgb, int a, int b) =>
        Math.Abs(rgb[a] - rgb[b]) + Math.Abs(rgb[a + 1] - rgb[b + 1]) +
        Math.Abs(rgb[a + 2] - rgb[b + 2]) >= 150;
}
