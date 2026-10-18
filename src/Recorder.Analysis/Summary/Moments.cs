using System.Globalization;

namespace Recorder.Analysis.Summary;

/// <summary>A captured frame: its time, its picture file, and its size in pixels.</summary>
public sealed record CapturedFrame(long Time, string Path, string AbsolutePath, int Width, int Height, double? Magnification);

/// <summary>A layout change set of a document, and how many of its elements it changed; null where not recorded.</summary>
public sealed record LayoutChangeFact(long Time, string? EventId, string DocumentToken, long? ChangedNodes);

/// <summary>A scroller of a document scrolling to a vertical position, in CSS pixels, with the scroller's DOM node where it is recorded.</summary>
public sealed record ScrollFact(long Time, string? EventId, string DocumentToken, string Scroller, double Y, long? NodeId = null);

/// <summary>An animation on the compositor: its start, and its end where it was recorded.</summary>
public sealed record AnimationFact(long Start, string? StartEventId, long? End, string? EndEventId);

/// <summary>A span in which one of NVDA's own windows was in front.</summary>
public sealed record NvdaWindowFact(string Name, long Start, string? StartEventId, long End, string? EndEventId);

/// <summary>
/// What the vision stage reads from a recording beyond the summary's facts.
/// The recorded implementation reads a recording file; the tests give their own.
/// </summary>
public interface IMomentEvidence
{
    IReadOnlyList<CapturedFrame> Frames { get; }

    IReadOnlyList<LayoutChangeFact> LayoutChanges { get; }

    IReadOnlyList<ScrollFact> Scrolls { get; }

    IReadOnlyList<AnimationFact> Animations { get; }

    IReadOnlyList<TimedFact> PopupsClosed { get; }

    IReadOnlyList<NvdaWindowFact> NvdaWindows { get; }

    /// <summary>The page's changes to its live regions; see <see cref="LiveRegionChanges"/>.</summary>
    IReadOnlyList<LiveRegionFact> LiveRegions { get; }

    /// <summary>The document's viewport height in CSS pixels at a time; null where not recorded.</summary>
    double? ViewportHeightAt(string documentToken, long time);
}

/// <summary>
/// The limits on choosing moments, all settings. Agreed with the owner on
/// 2026-10-14 as starting values; see recording-summary.md, "Decisions
/// (2026-10-14)".
/// </summary>
/// <param name="PerVisit">
/// The most moments a visit has, the number left out stated; null, the
/// default, for no limit (owner, 2026-10-14: no artificial limits unless
/// the platform forces them).
/// </param>
/// <param name="MergeNanoseconds">Moments closer together than this are one moment.</param>
/// <param name="SettleQuietNanoseconds">The page is settled when its layout has not changed for this long.</param>
/// <param name="SettleFallbackNanoseconds">If it never settles, the first moment is this long after the visit starts.</param>
/// <param name="LargeChangeNodes">A layout change set of at least this many elements, after the page settles, is a large change.</param>
public sealed record MomentLimits(
    int? PerVisit = null,
    long MergeNanoseconds = 2_000_000_000,
    long SettleQuietNanoseconds = 1_000_000_000,
    long SettleFallbackNanoseconds = 5_000_000_000,
    long LargeChangeNodes = 50)
{
    public static MomentLimits Default { get; } = new();
}

/// <summary>What chose a moment, as the recorder derived it.</summary>
public static class MomentKinds
{
    public const string Settled = "settled";
    public const string NvdaWindow = "nvda-window";
    public const string Popup = "popup";
    public const string LiveRegion = "live-region";
    public const string Typed = "typed";
    public const string Click = "click";
    public const string LargeChange = "large-change";
    public const string Animation = "animation";
    public const string Scroll = "scroll";
    public const string Last = "last";

    // Which moments are kept first when a visit has more than its limit:
    // the first settled moment and the last frame always, then dialogs and
    // popups and live region changes, typing, clicks, large changes, animations, and scrolls.
    internal static int Priority(string kind) => kind switch
    {
        Settled or Last => 0,
        NvdaWindow or Popup or LiveRegion => 1,
        Typed => 2,
        Click => 3,
        LargeChange => 4,
        Animation => 5,
        _ => 6
    };
}

/// <summary>One reason a moment was chosen: its kind, its time, the fact, and the record it rests on.</summary>
public sealed record MomentReason(string Kind, long Time, string Text, string? EventId);

/// <summary>A moment of a visit: its captured frame, and every reason that chose it.</summary>
public sealed record Moment(int Number, CapturedFrame Frame, IReadOnlyList<MomentReason> Reasons);

/// <summary>A visit's moments, and how many were left out to keep within the limit.</summary>
public sealed record VisitMoments(IReadOnlyList<Moment> Moments, int LeftOut);

/// <summary>
/// Chooses the moments of a visit from the recorded evidence, as derived
/// facts: the first settled moment, NVDA's windows and the page's popups
/// opening and closing, each change to a live region, each animation's start, middle, and end, each large
/// layout change, each click, each scroll to a new part of the page, each
/// field typed into, and the last frame. Moments close together are merged.
/// See docs/architecture/recording-summary.md, "The moments of a visit".
/// </summary>
public static class MomentChooser
{
    public static VisitMoments Choose(ISummaryEvidence summary, IMomentEvidence evidence, Visit visit, MomentLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(visit);
        limits ??= MomentLimits.Default;
        var start = visit.Start.Time;
        var end = visit.End;
        var frames = evidence.Frames.Where(frame => frame.Time >= start && frame.Time < end).OrderBy(frame => frame.Time).ToList();
        if (frames.Count == 0)
        {
            return new VisitMoments([], 0);
        }

        bool Within(long time) => time >= start && time < end;
        var token = visit.Start.DocumentToken;
        var reasons = new List<MomentReason>();

        // The first settled moment.
        var changes = token is null
            ? []
            : evidence.LayoutChanges.Where(change => change.DocumentToken == token && Within(change.Time)).OrderBy(change => change.Time).ToList();
        var settled = Settle(changes, start, end, limits);
        reasons.Add(settled);

        foreach (var window in evidence.NvdaWindows)
        {
            if (Within(window.Start))
            {
                reasons.Add(new MomentReason(MomentKinds.NvdaWindow, window.Start, $"NVDA's window {Quote(window.Name)} came to the front.", window.StartEventId));
            }

            if (Within(window.End) && window.End > window.Start)
            {
                reasons.Add(new MomentReason(MomentKinds.NvdaWindow, window.End, $"NVDA's window {Quote(window.Name)} left the front.", window.EndEventId));
            }
        }

        foreach (var popup in summary.Popups.Where(fact => Within(fact.Time)))
        {
            reasons.Add(new MomentReason(MomentKinds.Popup, popup.Time, popup.Text, popup.EventId));
        }

        foreach (var popup in evidence.PopupsClosed.Where(fact => Within(fact.Time)))
        {
            reasons.Add(new MomentReason(MomentKinds.Popup, popup.Time, popup.Text, popup.EventId));
        }

        if (token is not null)
        {
            foreach (var change in evidence.LiveRegions.Where(fact => fact.DocumentToken == token && Within(fact.Time)))
            {
                reasons.Add(new MomentReason(MomentKinds.LiveRegion, change.Time, LiveRegionChanges.Reason(change), change.EventId));
            }
        }

        foreach (var animation in evidence.Animations.Where(item => Within(item.Start)))
        {
            reasons.Add(new MomentReason(MomentKinds.Animation, animation.Start, "An animation started on the compositor.", animation.StartEventId));
            if (animation.End is { } finished && Within(finished) && finished > animation.Start)
            {
                var middle = animation.Start + (finished - animation.Start) / 2;
                reasons.Add(new MomentReason(MomentKinds.Animation, middle,
                    $"The middle of an animation on the compositor that ran from {SummaryCollator.Time(animation.Start)} to {SummaryCollator.Time(finished)}.",
                    animation.StartEventId));
                reasons.Add(new MomentReason(MomentKinds.Animation, finished, "An animation on the compositor ended.", animation.EndEventId));
            }
        }

        foreach (var change in changes.Where(change => change.Time > settled.Time && change.ChangedNodes >= limits.LargeChangeNodes))
        {
            reasons.Add(new MomentReason(MomentKinds.LargeChange, change.Time,
                $"The page's layout changed in {change.ChangedNodes!.Value.ToString(CultureInfo.InvariantCulture)} elements at once.", change.EventId));
        }

        foreach (var click in summary.Clicks.Where(fact => Within(fact.Time)))
        {
            reasons.Add(new MomentReason(MomentKinds.Click, click.Time, click.Text, click.EventId));
        }

        if (token is not null)
        {
            reasons.AddRange(Scrolls(evidence, token, start, end));
            reasons.AddRange(Typing(summary, token, start, end));
        }

        reasons.Add(new MomentReason(MomentKinds.Last, frames[^1].Time, "The last captured frame before the visit ended.", null));

        // Merge reasons close together into one moment, at the frame for the
        // latest of them, so the picture shows what each led to.
        var groups = new List<List<MomentReason>>();
        foreach (var reason in reasons.OrderBy(reason => reason.Time).ThenBy(reason => MomentKinds.Priority(reason.Kind)))
        {
            if (groups.Count > 0 && reason.Time - groups[^1][0].Time < limits.MergeNanoseconds)
            {
                groups[^1].Add(reason);
            }
            else
            {
                groups.Add([reason]);
            }
        }

        var byFrame = new List<(CapturedFrame Frame, List<MomentReason> Reasons)>();
        foreach (var group in groups)
        {
            var frame = group.Any(reason => reason.Kind == MomentKinds.Last)
                ? frames[^1]
                : frames.FirstOrDefault(item => item.Time >= group.Max(reason => reason.Time)) ?? frames[^1];
            if (byFrame.Count > 0 && byFrame[^1].Frame == frame)
            {
                byFrame[^1].Reasons.AddRange(group);
            }
            else
            {
                byFrame.Add((frame, group));
            }
        }

        var kept = byFrame
            .Select((item, index) => (item.Frame, item.Reasons, Index: index, Priority: item.Reasons.Min(reason => MomentKinds.Priority(reason.Kind))))
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Index)
            .Take(limits.PerVisit is { } most ? Math.Max(1, most) : int.MaxValue)
            .OrderBy(item => item.Index)
            .ToList();
        var moments = kept.Select((item, index) => new Moment(index + 1, item.Frame, item.Reasons)).ToList();
        return new VisitMoments(moments, byFrame.Count - moments.Count);
    }

    // The first moment the page is settled: after the last layout change
    // that is followed by a quiet period, or the fallback time if none is.
    private static MomentReason Settle(List<LayoutChangeFact> changes, long start, long end, MomentLimits limits)
    {
        var quietText = Seconds(limits.SettleQuietNanoseconds);
        var fallback = Seconds(limits.SettleFallbackNanoseconds);
        if (changes.Count == 0)
        {
            var at = Math.Min(start + limits.SettleQuietNanoseconds, end - 1);
            return new MomentReason(MomentKinds.Settled, at,
                $"No change in the page's layout was recorded during the visit, so the page is taken as settled {quietText} after the visit began.", null);
        }

        var deadline = start + limits.SettleFallbackNanoseconds;
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            var next = index + 1 < changes.Count ? changes[index + 1].Time : end;
            if (change.Time > deadline)
            {
                break;
            }

            if (next - change.Time >= limits.SettleQuietNanoseconds)
            {
                var at = Math.Min(change.Time + limits.SettleQuietNanoseconds, end - 1);
                return new MomentReason(MomentKinds.Settled, at,
                    $"The first moment the page was settled: its layout last changed at {SummaryCollator.Time(change.Time)} and did not change for {quietText} after.",
                    change.EventId);
            }
        }

        return new MomentReason(MomentKinds.Settled, Math.Min(deadline, end - 1),
            $"The page's layout did not stay unchanged for {quietText} within {fallback} of the visit's start, so the first moment is {fallback} after it began.", null);
    }

    // Each scroll to a new part of the page: a scroller's position moving
    // by at least the viewport's height from where it was at the last such
    // moment, or from the top.
    private static IEnumerable<MomentReason> Scrolls(IMomentEvidence evidence, string token, long start, long end)
    {
        var shown = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var scroll in evidence.Scrolls.Where(item => item.DocumentToken == token && item.Time >= start && item.Time < end).OrderBy(item => item.Time))
        {
            if (evidence.ViewportHeightAt(token, scroll.Time) is not { } height || height <= 0)
            {
                continue;
            }

            var from = shown.TryGetValue(scroll.Scroller, out var earlier) ? earlier : 0;
            if (Math.Abs(scroll.Y - from) >= height)
            {
                shown[scroll.Scroller] = scroll.Y;
                yield return new MomentReason(MomentKinds.Scroll, scroll.Time,
                    string.Create(CultureInfo.InvariantCulture,
                        $"The page scrolled to a new part: {scroll.Y:0} CSS pixels from the top, {Math.Abs(scroll.Y - from):0} from the last part chosen, with a viewport {height:0} high."),
                    scroll.EventId);
            }
        }
    }

    // Each field typed into: the keys received by the page while one text
    // control had the focus, at the last of them.
    private static IEnumerable<MomentReason> Typing(ISummaryEvidence summary, string token, long start, long end)
    {
        var focus = summary.Focus.Where(fact => fact.DocumentToken == token && fact.Time < end).OrderBy(fact => fact.Time).ToList();
        long? field = null;
        TimedFact? last = null;
        var count = 0;
        foreach (var key in summary.Keys.Where(fact => fact.Time >= start && fact.Time < end).OrderBy(fact => fact.Time))
        {
            var node = focus.LastOrDefault(fact => fact.Time <= key.Time)?.NodeId;
            var typing = key.Text.Contains("received by the page", StringComparison.Ordinal) && node is { } focused &&
                summary.InteractionAt(token, key.Time)?.TextControls.ContainsKey(focused) == true;
            if (field is not null && (!typing || node != field))
            {
                yield return Typed(last!, count);
                field = null;
                count = 0;
            }

            if (typing)
            {
                field = node;
                last = key;
                count++;
            }
        }

        if (field is not null)
        {
            yield return Typed(last!, count);
        }
    }

    private static MomentReason Typed(TimedFact last, int count) =>
        new(MomentKinds.Typed, last.Time,
            $"The participant typed into a text field: {count.ToString(CultureInfo.InvariantCulture)} keys received by the page while it had the focus, the last at {SummaryCollator.Time(last.Time)}.",
            last.EventId);

    private static string Seconds(long nanoseconds) =>
        (nanoseconds / 1e9).ToString("0.###", CultureInfo.InvariantCulture) + " s";

    private static string Quote(string text) => "\"" + text.Replace("\"", "'", StringComparison.Ordinal) + "\"";
}
