using System.Globalization;

namespace Recorder.Analysis.Summary;

/// <summary>A rectangle in screen pixels, or in a frame's pixels, by its top left corner and size.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public ScreenRect Intersect(ScreenRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= x || bottom <= y ? default : new ScreenRect(x, y, right - x, bottom - y);
    }

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public static ScreenRect Bounding(IEnumerable<ScreenRect> rects)
    {
        var list = rects.Where(rect => !rect.IsEmpty).ToList();
        if (list.Count == 0)
        {
            return default;
        }

        var x = list.Min(rect => rect.X);
        var y = list.Min(rect => rect.Y);
        return new ScreenRect(x, y, list.Max(rect => rect.Right) - x, list.Max(rect => rect.Bottom) - y);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}) to ({Right}, {Bottom})");
}

/// <summary>
/// What of a moment's frame the report describes. Where limited, the
/// browser's window and NVDA's windows, in the frame's pixels; where not,
/// the whole frame, with why.
/// </summary>
/// <param name="Windows">The in-scope windows, clipped to the frame and, where magnified, to the magnified view, in the frame's pixels.</param>
/// <param name="Crop">Their bounding rectangle, the picture cut from the frame.</param>
/// <param name="Why">Why the whole frame is described, where it is.</param>
public sealed record FrameScope(bool Limited, IReadOnlyList<ScreenRect> Windows, ScreenRect Crop, string? Why)
{
    public static FrameScope Whole(string why) => new(false, [], default, why);
}

/// <summary>
/// The report's scope at a frame, with the owner's setting "Limit the
/// report to the browser while it is in front" on (owner, 2026-10-18:
/// "limit the report to the browser only when that is open, and not
/// report on the other windows or background ... Without the browser open,
/// we would still describe everything"; NVDA's own windows stay in; the
/// browser counts only when it is in front; on by default). See
/// report-fixes-2026-10-18.md, part 4.
/// </summary>
public static class ReportScope
{
    /// <summary>
    /// The scope at a frame: limited where the window last recorded in
    /// front, other than NVDA's, is the browser's, with its bounds recorded
    /// and not minimized. The windows kept are the browser's, at its bounds
    /// as last recorded in front, and every NVDA window recorded in front
    /// before the frame, at its bounds then, as whether it was closed is not
    /// recorded. The recorder's own windows are not in the front window
    /// records, so a frame with the recorder in front is taken as the
    /// window before it.
    /// </summary>
    public static FrameScope At(IReadOnlyList<ForegroundFact> foreground, string? browserProcess, CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(frame);
        var before = foreground.Where(window => window.Time <= frame.Time).OrderBy(window => window.Time).ToList();
        var front = before.LastOrDefault(window => !window.Nvda);
        if (front is null)
        {
            return FrameScope.Whole("no window other than NVDA's was recorded in front before it");
        }

        if (!IsBrowser(front, browserProcess))
        {
            return FrameScope.Whole($"{front.Process ?? "a program not recorded"} was in front, not the browser");
        }

        if (front.Minimized || front.Bounds is not { IsEmpty: false } browser)
        {
            return FrameScope.Whole("the browser was in front, but its window's bounds were not recorded");
        }

        var frameRect = new ScreenRect(0, 0, frame.Width, frame.Height);
        var visible = frameRect;
        if (frame.Magnification is > 1 and var level)
        {
            // The magnified view, as the player draws it (MagnifiedView).
            var x = frame.MagnifiedX + (frame.X / level) - frame.X;
            var y = frame.MagnifiedY + (frame.Y / level) - frame.Y;
            visible = new ScreenRect((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Ceiling(frame.Width / level), (int)Math.Ceiling(frame.Height / level))
                .Intersect(frameRect);
        }

        ScreenRect InFrame(ScreenRect screen) => new ScreenRect(screen.X - frame.X, screen.Y - frame.Y, screen.Width, screen.Height).Intersect(visible);

        var windows = new List<ScreenRect> { InFrame(browser) };
        windows.AddRange(before.Where(window => window.Nvda && !window.Minimized && window.Bounds is { IsEmpty: false })
            .Select(window => InFrame(window.Bounds!.Value)));
        windows = [.. windows.Where(rect => !rect.IsEmpty).Distinct()];
        if (windows.Count == 0)
        {
            return FrameScope.Whole("the browser's window was outside the picture");
        }

        return new FrameScope(true, windows, ScreenRect.Bounding(windows), null);
    }

    public static bool IsBrowser(ForegroundFact window, string? browserProcess) =>
        browserProcess is not null && window.Process is { } process &&
        (string.Equals(process, browserProcess, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Path.GetFileNameWithoutExtension(process), Path.GetFileNameWithoutExtension(browserProcess), StringComparison.OrdinalIgnoreCase));
}
