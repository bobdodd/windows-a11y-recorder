using System.Globalization;
using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// Step 13's moving picture check (android-checks.md, "Step 13: timing"),
/// WCAG 2.2.2, from the phone's video. The recorder writes a frame of the
/// phone's video only when the picture changed, so a run of frames each
/// less than half a second after the one before is a picture that kept
/// changing. A run lasting more than 5 seconds with no touch, starting a
/// second or more after the last touch, is content moving by itself, when
/// the frames differ inside the app's window and not under a window above
/// it, such as the status bar. The finding is a warning, at both audit
/// levels, on the smallest item holding the box that changed, outlined;
/// it is from the video, so approximate.
/// </summary>
public static class PhoneMotionChecks
{
    /// <summary>A gap between frames this long or longer ends a run.</summary>
    public const long GapNanoseconds = 500_000_000;

    /// <summary>A run this long or shorter is not looked at, as WCAG 2.2.2 allows 5 seconds.</summary>
    public const long LongestAllowedNanoseconds = 5_000_000_000;

    /// <summary>A run starting this soon after a touch is taken as the touch's.</summary>
    public const long TouchSettleNanoseconds = 1_000_000_000;

    // A pixel has changed when a channel differs by more than this, so the
    // video's own small differences are not changes.
    private const int ChannelChange = 24;

    // Pixels are compared this far apart each way.
    private const int Step = 4;

    // Fewer changed pixels than this, of those compared, is not movement.
    private const int LeastChanged = 16;

    // The pairs of frames compared in a run, spread through it.
    private const int Pairs = 8;

    public sealed record Result(IReadOnlyList<PhoneFinding> Findings, bool Run, int Runs, IReadOnlyList<string> Problems);

    public static Result Run(
        PhoneScreenTimeline screens,
        PhoneAccessibilityTimeline trees,
        PhoneTouchTimeline touches,
        ISessionEventRecordSource source,
        PhoneSnapshotPictures pictures,
        Func<string, PhonePixels?>? load,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(touches);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pictures);
        var frames = pictures.Frames;
        if (load is null || frames.Count < 2 || !touches.Recorded)
        {
            return new Result([], false, 0, []);
        }

        var findings = new Dictionary<(PhoneVisit, string), PhoneFinding>();
        var problems = new List<string>();
        var runs = 0;
        var first = 0;
        for (var index = 1; index <= frames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index < frames.Count &&
                frames[index].MonotonicNanoseconds - frames[index - 1].MonotonicNanoseconds < GapNanoseconds &&
                !touches.TouchedBetween(frames[index - 1].MonotonicNanoseconds, frames[index].MonotonicNanoseconds))
            {
                continue;
            }

            var (from, to) = (first, index - 1);
            first = index;
            var start = frames[from].MonotonicNanoseconds;
            var end = frames[to].MonotonicNanoseconds;
            if (end - start <= LongestAllowedNanoseconds || touches.TouchedBetween(start - TouchSettleNanoseconds, start))
            {
                continue;
            }

            if (screens.Visits.LastOrDefault(visit => visit.Start <= start && visit.End >= start) is not { } visit ||
                visit.Snapshots.LastOrDefault(snapshot => snapshot.MonotonicNanoseconds <= start) is not { } snapshot ||
                trees.TakenBy(snapshot, visit.WindowId) is not { } taking)
            {
                continue;
            }

            var clock = WindowsPreferenceTimeline.Clock(start);
            try
            {
                var windows = PhoneContrastRun.WindowsOf(source, snapshot);
                var window = PhoneCheckRun.ReadWindow(source, taking, visit.WindowId);
                if (window is null || !windows.TryGetValue(visit.WindowId, out var app))
                {
                    continue;
                }

                var above = windows.Values.Where(entry => entry.Id != app.Id && entry.Layer > app.Layer).Select(entry => entry.Bounds).ToList();
                if (Changed(frames, from, to, load, trees, app.Bounds, above) is not { } box)
                {
                    continue;
                }

                runs++;
                var item = window.Items
                    .Where(entry => entry.Node.VisibleToUser && Contains(entry.Bounds, box))
                    .OrderBy(entry => (long)entry.Bounds.Width * entry.Bounds.Height)
                    .FirstOrDefault() ?? window.Items[0];
                var seconds = ((end - start) / 1e9).ToString("0.0", CultureInfo.CurrentCulture);
                var key = (visit, item.Identity);
                if (!findings.TryGetValue(key, out var finding))
                {
                    findings[key] = finding = new PhoneFinding(PhoneCheckNames.MovingByItself, PhoneCheckLevel.Warning, visit, item.Identity, item.Label);
                }

                finding._times.Add(new PhoneFindingTime(
                    start, visit, snapshot, taking, visit.WindowId, box, item.TreeKey,
                    $"from the video, so approximate: the picture in the outlined box kept changing for {seconds} seconds with no touch, " +
                        "as moving, scrolling or auto-updating content does; if it is more than a loading indicator shown alone, " +
                        "look for a way to pause, stop or hide it, unless the movement is essential",
                    PhoneCheckRun.StateAt(visit, start)));
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or InvalidOperationException or
                    JsonException or KeyNotFoundException or UnauthorizedAccessException)
            {
                problems.Add($"{clock}: {exception.Message}");
            }
        }

        return new Result([.. findings.Values], true, runs, problems);
    }

    // The box, in the screen's pixels, holding what changed between pairs
    // of frames spread through a run, within the app's window and not under
    // a window above it; null when nothing did.
    private static PhoneBounds? Changed(
        IReadOnlyList<SessionVideoFrame> frames,
        int from,
        int to,
        Func<string, PhonePixels?> load,
        PhoneAccessibilityTimeline trees,
        PhoneBounds app,
        List<PhoneBounds> above)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue, changed = 0;
        var pairs = Math.Min(Pairs, to - from);
        for (var pair = 0; pair < pairs; pair++)
        {
            var at = from + (int)((long)(to - from - 1) * pair / Math.Max(1, pairs - 1));
            var a = load(frames[at].AbsolutePath);
            var b = load(frames[at + 1].AbsolutePath);
            if (a is null || b is null || a.Width != b.Width || a.Height != b.Height)
            {
                continue;
            }

            var screen = trees.ScreenAt(frames[at].MonotonicNanoseconds) ?? (a.Width, a.Height);
            var scale = new PictureScale(a.Width / (double)screen.Width, a.Height / (double)screen.Height);
            for (var y = 0; y < a.Height; y += Step)
            {
                for (var x = 0; x < a.Width; x += Step)
                {
                    if (!Differs(a.At(x, y), b.At(x, y)))
                    {
                        continue;
                    }

                    var sx = (int)(x / scale.X);
                    var sy = (int)(y / scale.Y);
                    if (sx < app.Left || sx >= app.Right || sy < app.Top || sy >= app.Bottom ||
                        above.Any(bounds => sx >= bounds.Left && sx < bounds.Right && sy >= bounds.Top && sy < bounds.Bottom))
                    {
                        continue;
                    }

                    changed++;
                    left = Math.Min(left, sx);
                    top = Math.Min(top, sy);
                    right = Math.Max(right, sx + 1);
                    bottom = Math.Max(bottom, sy + 1);
                }
            }
        }

        return changed < LeastChanged ? null : new PhoneBounds(left, top, right, bottom);
    }

    private static bool Differs(uint a, uint b) =>
        Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF)) > ChannelChange ||
        Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF)) > ChannelChange ||
        Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) > ChannelChange;

    private static bool Contains(PhoneBounds outer, PhoneBounds inner) =>
        outer.Left <= inner.Left && outer.Top <= inner.Top && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;
}
