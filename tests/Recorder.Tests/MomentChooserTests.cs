using Recorder.Analysis.Summary;
using Recorder.Session;

namespace Recorder.Tests;

internal sealed class FakeMomentEvidence : IMomentEvidence
{
    public List<CapturedFrame> FrameList { get; } = [];

    public IReadOnlyList<CapturedFrame> Frames => FrameList;

    public List<LayoutChangeFact> LayoutList { get; } = [];

    public IReadOnlyList<LayoutChangeFact> LayoutChanges => LayoutList;

    public List<ScrollFact> ScrollList { get; } = [];

    public IReadOnlyList<ScrollFact> Scrolls => ScrollList;

    public List<AnimationFact> AnimationList { get; } = [];

    public IReadOnlyList<AnimationFact> Animations => AnimationList;

    public List<TimedFact> ClosedList { get; } = [];

    public IReadOnlyList<TimedFact> PopupsClosed => ClosedList;

    public List<NvdaWindowFact> WindowList { get; } = [];

    public IReadOnlyList<NvdaWindowFact> NvdaWindows => WindowList;

    public double? Viewport { get; set; } = 800;

    public double? ViewportHeightAt(string documentToken, long time) => Viewport;

    /// <summary>A frame every half second from one time to another.</summary>
    public FakeMomentEvidence EveryHalfSecond(long fromSeconds, long toSeconds)
    {
        for (var time = fromSeconds * 1_000_000_000; time < toSeconds * 1_000_000_000; time += 500_000_000)
        {
            FrameList.Add(new CapturedFrame(time, $"frames/{time}.png", $"/frames/{time}.png", 1920, 1080, null));
        }

        return this;
    }
}

/// <summary>
/// Choosing a visit's moments from the recorded evidence, as derived facts.
/// See docs/architecture/recording-summary.md, "The moments of a visit".
/// </summary>
public sealed class MomentChooserTests
{
    private const long Second = 1_000_000_000;
    private const long Milli = 1_000_000;

    // A visit of document doc-1 from 1 s to 30 s.
    private static Visit Visit(long start = 1, long end = 30) =>
        new(1, VisitKind.Page, new NavigationCommit(start * Second, "nav-1", "b/frame-1", "https://example.test/", false, "doc-1", true, false), end * Second, [], null, null);

    private static VisitMoments Choose(FakeMomentEvidence evidence, FakeSummaryEvidence? summary = null, MomentLimits? limits = null) =>
        MomentChooser.Choose(summary ?? new FakeSummaryEvidence(), evidence, Visit(), limits);

    private static LayoutChangeFact Layout(long millis, long? nodes = 3) => new(millis * Milli, $"layout-{millis}", "doc-1", nodes);

    [Fact]
    public void The_first_moment_is_when_the_layout_has_not_changed_for_a_second()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);
        evidence.LayoutList.AddRange([Layout(1_200), Layout(1_500), Layout(1_900), Layout(4_000)]);

        var moments = Choose(evidence);

        var first = moments.Moments[0];
        var settled = Assert.Single(first.Reasons, reason => reason.Kind == MomentKinds.Settled);
        Assert.Equal(2_900 * Milli, settled.Time);
        Assert.Equal("layout-1900", settled.EventId);
        Assert.Equal(3 * Second, first.Frame.Time);
    }

    [Fact]
    public void A_page_that_never_settles_has_its_first_moment_five_seconds_in()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);
        for (var millis = 1_100L; millis < 12_000; millis += 400)
        {
            evidence.LayoutList.Add(Layout(millis));
        }

        var settled = Choose(evidence).Moments[0].Reasons.Single(reason => reason.Kind == MomentKinds.Settled);

        Assert.Equal(6 * Second, settled.Time);
        Assert.Contains("within 5 s of the visit's start", settled.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_with_no_layout_changes_is_settled_a_second_in()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);

        var settled = Choose(evidence).Moments[0].Reasons.Single(reason => reason.Kind == MomentKinds.Settled);

        Assert.Equal(2 * Second, settled.Time);
        Assert.Contains("No change in the page's layout was recorded", settled.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Reasons_less_than_two_seconds_apart_are_one_moment_at_the_frame_for_the_latest()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);
        var summary = new FakeSummaryEvidence();
        summary.ClickList.Add(new TimedFact(10 * Second, "click-1", "Left click at (5, 5)."));
        summary.PopupList.Add(new TimedFact(11_200 * Milli, "popup-1", "A page popup opened."));

        var moment = Choose(evidence, summary).Moments.Single(item => item.Reasons.Any(reason => reason.Kind == MomentKinds.Click));

        Assert.Equal([MomentKinds.Click, MomentKinds.Popup], moment.Reasons.Select(reason => reason.Kind));
        Assert.Equal(11_500 * Milli, moment.Frame.Time);
    }

    [Fact]
    public void Over_the_limit_the_settled_and_last_moments_and_dialogs_are_kept_and_the_rest_counted()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);
        evidence.WindowList.Add(new NvdaWindowFact("NVDA Settings", 27 * Second, "window-1", 28_500 * Milli, null));
        var summary = new FakeSummaryEvidence();
        for (var seconds = 4L; seconds <= 25; seconds += 3)
        {
            summary.ClickList.Add(new TimedFact(seconds * Second, $"click-{seconds}", "Left click."));
        }

        var moments = Choose(evidence, summary, new MomentLimits(PerVisit: 4));

        Assert.Equal([1, 2, 3, 4], moments.Moments.Select(moment => moment.Number));
        Assert.Contains(moments.Moments[0].Reasons, reason => reason.Kind == MomentKinds.Settled);
        Assert.Contains(moments.Moments[^1].Reasons, reason => reason.Kind == MomentKinds.Last);
        Assert.Contains(moments.Moments.SelectMany(moment => moment.Reasons), reason => reason.Kind == MomentKinds.NvdaWindow);
        Assert.Equal("click-4", moments.Moments[1].Reasons.Single().EventId);
        Assert.Equal(7, moments.LeftOut);
        Assert.True(moments.Moments.Select(moment => moment.Frame.Time).SequenceEqual(moments.Moments.Select(moment => moment.Frame.Time).Order()));
    }

    [Fact]
    public void The_last_frame_of_the_visit_is_always_a_moment()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 40);

        var last = Choose(evidence).Moments[^1];

        Assert.Equal(29_500 * Milli, last.Frame.Time);
        Assert.Contains(last.Reasons, reason => reason.Kind == MomentKinds.Last);
    }

    [Fact]
    public void A_visit_with_no_frames_has_no_moments()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(31, 40);

        var moments = Choose(evidence);

        Assert.Empty(moments.Moments);
        Assert.Equal(0, moments.LeftOut);
    }

    [Fact]
    public void Typing_into_a_field_is_a_moment_at_its_last_key()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);
        var summary = new FakeSummaryEvidence();
        summary.FocusList.Add(new FocusFact(9 * Second, "focus-1", "doc-1", 42));
        summary.Interactions["doc-1"] = [(0, new InteractionCurrent(42, null, new Dictionary<long, TextControlState>
        {
            [42] = new(42, "text", "", null, null, null)
        }))];
        foreach (var millis in new long[] { 10_000, 10_300, 10_600 })
        {
            summary.KeyList.Add(new TimedFact(millis * Milli, $"key-{millis}", "A, passed, received by the page"));
        }

        summary.KeyList.Add(new TimedFact(20 * Second, "key-tab", "Tab, kept, screen reader running: NVDA"));

        var typed = Choose(evidence, summary).Moments.SelectMany(moment => moment.Reasons).Single(reason => reason.Kind == MomentKinds.Typed);

        Assert.Equal(10_600 * Milli, typed.Time);
        Assert.Equal("key-10600", typed.EventId);
        Assert.Contains("3 keys received by the page", typed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scroll_is_a_moment_when_it_reaches_a_new_part_of_the_page()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);
        evidence.ScrollList.AddRange([
            new ScrollFact(5 * Second, "scroll-1", "doc-1", "root", 300),
            new ScrollFact(9 * Second, "scroll-2", "doc-1", "root", 900),
            new ScrollFact(13 * Second, "scroll-3", "doc-1", "root", 1_200),
            new ScrollFact(17 * Second, "scroll-4", "doc-1", "root", 1_700),
            new ScrollFact(21 * Second, "scroll-5", "doc-1", "carousel", 0)
        ]);

        var scrolls = Choose(evidence).Moments.SelectMany(moment => moment.Reasons).Where(reason => reason.Kind == MomentKinds.Scroll).ToList();

        Assert.Equal(["scroll-2", "scroll-4"], scrolls.Select(reason => reason.EventId));
    }

    [Fact]
    public void Scrolls_are_not_moments_where_the_viewport_was_not_recorded()
    {
        var evidence = new FakeMomentEvidence { Viewport = null }.EveryHalfSecond(1, 30);
        evidence.ScrollList.Add(new ScrollFact(9 * Second, "scroll-1", "doc-1", "root", 5_000));

        Assert.DoesNotContain(Choose(evidence).Moments.SelectMany(moment => moment.Reasons), reason => reason.Kind == MomentKinds.Scroll);
    }

    [Fact]
    public void An_animation_gives_its_start_middle_and_end()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);
        evidence.AnimationList.Add(new AnimationFact(10 * Second, "animation-start", 16 * Second, "animation-end"));
        evidence.AnimationList.Add(new AnimationFact(22 * Second, "animation-open", null, null));

        var animations = Choose(evidence).Moments.SelectMany(moment => moment.Reasons).Where(reason => reason.Kind == MomentKinds.Animation).ToList();

        Assert.Equal([10 * Second, 13 * Second, 16 * Second, 22 * Second], animations.Select(reason => reason.Time));
        Assert.Equal("animation-end", animations[2].EventId);
    }

    [Fact]
    public void A_large_layout_change_after_the_page_settles_is_a_moment()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);
        evidence.LayoutList.AddRange([Layout(1_200, 400), Layout(10_000, 49), Layout(14_000, 50)]);

        var large = Choose(evidence).Moments.SelectMany(moment => moment.Reasons).Single(reason => reason.Kind == MomentKinds.LargeChange);

        Assert.Equal("layout-14000", large.EventId);
        Assert.Contains("50 elements", large.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NVDA_windows_coming_to_the_front_and_leaving_it_are_moments()
    {
        var evidence = new FakeMomentEvidence().EveryHalfSecond(1, 30);
        evidence.WindowList.Add(new NvdaWindowFact("Elements List", 12 * Second, "window-in", 18 * Second, "window-out"));

        var windows = Choose(evidence).Moments.SelectMany(moment => moment.Reasons).Where(reason => reason.Kind == MomentKinds.NvdaWindow).ToList();

        Assert.Equal(["window-in", "window-out"], windows.Select(reason => reason.EventId));
        Assert.Equal("NVDA's window \"Elements List\" came to the front.", windows[0].Text);
    }
}
