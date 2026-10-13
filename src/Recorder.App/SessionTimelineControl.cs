using System.Data.Common;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Recorder.Session;

namespace Recorder.App;

public sealed class SessionTimelineControl : FrameworkElement
{
    private static readonly IReadOnlyDictionary<string, Brush> ChannelBrushes =
        new Dictionary<string, Brush>(StringComparer.Ordinal)
        {
            ["input.keyboard"] = Freeze("#4F98A3"),
            ["input.mouse"] = Freeze("#5591C7"),
            ["accessibility.uia.events"] = Freeze("#FDAB43"),
            ["window.foreground"] = Freeze("#A86FDF"),
            ["graphics.desktop.frames"] = Freeze("#797876"),
            ["audio.microphone"] = Freeze("#DD6974"),
            ["audio.system"] = Freeze("#6DAA45"),
            ["system.preferences"] = Freeze("#D19900"),
            ["graphics.magnifier"] = Freeze("#B5C93F"),
            ["browser.preferences"] = Freeze("#C26BB0"),
            ["system.assistive-technology"] = Freeze("#7FB8E0"),
            ["session.annotations"] = Freeze("#E8AF34")
        };

    // Lane 7 holds the Windows settings records (system.preferences), and
    // lane 8 the Magnifier change records (graphics.magnifier), and lane 9
    // the browser settings records (browser.preferences), and lane 10 the
    // assistive technology records (system.assistive-technology).
    private const int LaneCount = 12;
    private const int OtherLane = 11;
    private const int AnnotationSeries = 11;
    private const int OtherSeries = 12;
    private const int SeriesCount = 13;

    // Series are drawn in this order, so markers stay visible over other
    // channels that share their lane.
    private static readonly int[] SeriesDrawOrder = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, OtherSeries, AnnotationSeries];

    private static readonly Brush BackgroundBrush = Freeze("#201F1D");

    // The band over the keyboard and mouse lanes where input could not be
    // recorded: diagonal stripes, so it does not rely on colour alone.
    private static readonly Brush UnrecordableBrush = CreateUnrecordableBrush();
    private static readonly Brush OtherChannelBrush = Freeze("#BAB9B4");

    // The band of NVDA's inferred mode, in a strip of its own directly under
    // the Screen reader lane, so the lane's event marks never cover it:
    // browse mode solid blue, focus mode solid amber, and the span in which
    // the mode changed grey stripes. The words "browse", "focus", and
    // "changed" are written in the band where they fit, so it does not
    // rely on colour. Where the mode is unknown, the strip is empty.
    // Agreed with the owner on 2026-10-11, after the band drawn within the
    // lane, about 6 pixels high, could not be read.
    private const double ModeStripHeight = 16;
    private static readonly Brush BrowseModeBrush = Freeze("#4F6F96");
    private static readonly Brush FocusModeBrush = Freeze("#C9A05A");
    private static readonly Brush ModeChangedBrush = CreateStripeBrush("#9A9894", new Point(0, 0), new Point(8, 8), 1.5);
    private static readonly Brush ModeLightTextBrush = Brushes.White;
    private static readonly Brush ModeDarkTextBrush = BackgroundBrush;
    private static readonly Pen HighlightPen = FreezePen(Brushes.White, 2);

    // NVDA's own windows in front, such as the Elements List, in a second
    // strip under the mode strip, as a mode span can cover them: solid green
    // with the window's name where it fits; where none is in front, empty.
    // White on #3F6B3A is about 6:1. Agreed with the owner on 2026-10-11.
    private const double WindowStripHeight = 16;
    private static readonly Brush NvdaWindowBrush = Freeze("#3F6B3A");

    // A mark at the foot of the window strip for each of its events: the
    // window coming to the front, each element focused in it, and the
    // window that replaced it. Clicking the strip selects the nearest, and
    // Left and Right step through them (the owner, 2026-10-11: "so that I
    // can click on the timeline and then arrow through the events for the
    // dialog").
    private static readonly Brush NvdaWindowMarkBrush = Freeze("#E4F0E1");

    // The reading position, inferred (screen-reader-activity.md, "The
    // reading position (step 4)"), in a third strip under the window strip:
    // a known object solid purple with its role and name where they fit, an
    // uncertain one purple stripes with "uncertain", and where it is unknown,
    // empty. Each step starts a span a pixel after the last, so moves show.
    // White on #6A4C93 is about 7:1. Agreed with the owner on 2026-10-12.
    private const double ReadingStripHeight = 16;
    private static readonly Brush ReadingKnownBrush = Freeze("#6A4C93");
    private static readonly Brush ReadingUncertainBrush = CreateStripeBrush("#6A4C93", new Point(0, 0), new Point(8, 8), 1.5);

    private readonly DrawingVisual _eventLayer = new();
    private readonly DrawingVisual _overlayLayer = new();
    private readonly VisualCollection _layers;
    private ISessionTimeline? _timeline;
    // The channels the filters show, as the window decides them, so the
    // timeline and the filter summary always agree. Null shows every channel.
    private IReadOnlySet<string>? _visibleChannels;

    // The recording's channels shown with the current filters, in all and
    // by lane and series.
    private HashSet<string> _shownChannels = new(StringComparer.Ordinal);
    private HashSet<string>[] _shownByLane = [];
    private List<string>[] _shownBySeries = [];

    // Lookups finish after the user may have moved on; only the latest
    // request's answer is used.
    private long _request;
    private long _durationNanoseconds;
    private long _positionNanoseconds;
    private long _viewportStartNanoseconds;
    private long _viewportDurationNanoseconds;
    private SessionTimelineEvent? _selectedEvent;
    private IReadOnlyList<Recorder.Session.UnrecordablePeriod> _unrecordable = [];
    private IReadOnlyList<Recorder.Session.ScreenReaderModeSpan> _modeSpans = [];
    private IReadOnlyList<Recorder.Session.ScreenReaderWindowSpan> _windowSpans = [];
    private IReadOnlyList<Recorder.Session.ReadingPositionSpan> _readingSpans = [];
    private List<SessionTimelineEvent> _windowEvents = [];

    // Whether the keys step through the window strip's events rather than a
    // lane's: set by a click in the strip, or moving into it with Up or Down.
    private bool _inWindowStrip;

    // The lane keyboard stepping moves in: the selected event's lane, or the
    // lane last clicked.
    private int _currentLane;

    public SessionTimelineControl()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        _layers = new VisualCollection(this) { _eventLayer, _overlayLayer };
    }

    public event EventHandler<TimelineEventSelectedEventArgs>? SelectedEventChanged;

    /// <summary>Raised with a message when the timeline cannot be read.</summary>
    public event EventHandler<string>? LookupFailed;

    /// <summary>
    /// Raised with the time clicked in the NVDA mode or window strip, so the
    /// window moves the playhead there and the NVDA mode and NVDA window rows
    /// show the mode and the window.
    /// </summary>
    public event EventHandler<long>? PositionRequested;

    // The strips' heights: each none when the recording has no spans of it.
    private double ModeHeight => _modeSpans.Count > 0 ? ModeStripHeight : 0;

    private double WindowHeight => _windowSpans.Count > 0 ? WindowStripHeight : 0;

    private double ReadingHeight => _readingSpans.Count > 0 ? ReadingStripHeight : 0;

    private double StripHeight => ModeHeight + WindowHeight + ReadingHeight;

    private double LaneHeight => Math.Max(3, (ActualHeight - StripHeight) / LaneCount);

    // A lane's top: the lanes after the Screen reader lane sit below the
    // mode strip.
    private double LaneTop(int lane, double laneHeight) =>
        lane * laneHeight + (lane > ModeLane ? StripHeight : 0);

    private double StripTop(double laneHeight) => (ModeLane + 1) * laneHeight;

    private static int ModeLane => GetLane(Recorder.Contracts.AssistiveTechnologyRecords.Channel);

    public long PositionNanoseconds
    {
        get => _positionNanoseconds;
        set
        {
            var position = Math.Clamp(value, 0, _durationNanoseconds);
            if (position == _positionNanoseconds)
            {
                return;
            }

            _positionNanoseconds = position;
            RedrawOverlay();
        }
    }

    protected override int VisualChildrenCount => _layers.Count;

    protected override Visual GetVisualChild(int index) => _layers[index];

    public void SetSession(
        ISessionTimeline timeline,
        long durationNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        _timeline = timeline;
        _request++;
        _durationNanoseconds = Math.Max(0, durationNanoseconds);
        _viewportStartNanoseconds = 0;
        _viewportDurationNanoseconds = _durationNanoseconds;
        _positionNanoseconds = 0;
        _unrecordable = [];
        _modeSpans = [];
        _readingSpans = [];
        RebuildShownChannels();
        SelectEvent(null);
        RedrawAll();
    }

    /// <summary>
    /// The periods when keyboard and mouse input could not be recorded,
    /// drawn as a band over the keyboard and mouse lanes. The properties
    /// panel's Keyboard and mouse row gives the reason in text.
    /// </summary>
    public void SetUnrecordablePeriods(IReadOnlyList<Recorder.Session.UnrecordablePeriod> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        _unrecordable = periods;
        RedrawAll();
    }

    /// <summary>
    /// The spans of NVDA's inferred mode, drawn as a band over the Screen
    /// reader lane. The properties panel's NVDA mode row gives the mode and
    /// its basis in text.
    /// </summary>
    public void SetScreenReaderModeSpans(IReadOnlyList<Recorder.Session.ScreenReaderModeSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        _modeSpans = spans;
        RedrawAll();
    }

    /// <summary>
    /// The spans of the inferred reading position, drawn as a strip under the
    /// window strip. The properties panel's Reading position (inferred) row
    /// gives the object and its basis in text.
    /// </summary>
    public void SetReadingPositionSpans(IReadOnlyList<Recorder.Session.ReadingPositionSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        _readingSpans = spans;
        RedrawAll();
    }

    /// <summary>
    /// The spans of NVDA's own windows in front, drawn as a strip under the
    /// mode strip. The properties panel's NVDA window row gives the window
    /// and the element focused in it in text.
    /// </summary>
    public void SetScreenReaderWindowSpans(IReadOnlyList<Recorder.Session.ScreenReaderWindowSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        _windowSpans = spans;
        _windowEvents = [.. spans.SelectMany(span => span.Events)
            .DistinctBy(item => item.EventKey)
            .OrderBy(item => item.MonotonicNanoseconds)
            .ThenBy(item => item.EventKey)];
        _inWindowStrip = false;
        RedrawAll();
    }

    public void SetVisibleChannels(IReadOnlySet<string> visibleChannels)
    {
        ArgumentNullException.ThrowIfNull(visibleChannels);
        _visibleChannels = visibleChannels;
        _request++;
        RebuildShownChannels();
        if (_selectedEvent is not null && !_inWindowStrip && !_shownChannels.Contains(_selectedEvent.Channel))
        {
            SelectEvent(null);
        }

        RedrawAll();
    }

    public void SetViewport(long startNanoseconds, long durationNanoseconds)
    {
        var duration = Math.Clamp(
            durationNanoseconds,
            Math.Min(1, _durationNanoseconds),
            _durationNanoseconds);
        var start = Math.Clamp(
            startNanoseconds,
            0,
            Math.Max(0, _durationNanoseconds - duration));
        if (duration == _viewportDurationNanoseconds &&
            start == _viewportStartNanoseconds)
        {
            return;
        }

        _viewportDurationNanoseconds = duration;
        _viewportStartNanoseconds = start;
        RedrawAll();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RedrawAll();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_viewportDurationNanoseconds <= 0 || ActualWidth <= 0)
        {
            return;
        }

        var point = e.GetPosition(this);
        var timestamp = _viewportStartNanoseconds +
            (long)(Math.Clamp(point.X / ActualWidth, 0, 1) *
                _viewportDurationNanoseconds);
        var laneHeight = LaneHeight;
        var stripTop = StripTop(laneHeight);
        if (WindowHeight > 0 && point.Y >= stripTop + ModeHeight && point.Y < stripTop + ModeHeight + WindowHeight)
        {
            // The window strip: the nearest event of the window clicked, or
            // the playhead there where no window was in front.
            e.Handled = true;
            _inWindowStrip = true;
            var clicked = _windowSpans.FirstOrDefault(span => span.Start <= timestamp && timestamp <= span.End && span.Events.Count > 0);
            if (clicked is null)
            {
                _request++;
                SelectEvent(null);
                PositionRequested?.Invoke(this, Math.Clamp(timestamp, 0, _durationNanoseconds));
                return;
            }

            SelectWindowEvent(clicked.Events.MinBy(item => Math.Abs(item.MonotonicNanoseconds - timestamp))!);
            return;
        }

        if (StripHeight > 0 && point.Y >= stripTop && point.Y < stripTop + StripHeight)
        {
            e.Handled = true;
            _inWindowStrip = false;
            PositionRequested?.Invoke(this, Math.Clamp(timestamp, 0, _durationNanoseconds));
            return;
        }

        _inWindowStrip = false;

        var lane = Math.Clamp(
            (int)((point.Y >= stripTop + StripHeight ? point.Y - StripHeight : point.Y) / laneHeight),
            0,
            LaneCount - 1);
        var start = _viewportStartNanoseconds;
        var end = start + _viewportDurationNanoseconds;
        var laneChannels = _shownByLane[lane];
        var shown = _shownChannels;
        _currentLane = lane;
        e.Handled = true;
        // The events drawn at the clicked event's point: within a pixel
        // column of it, or a bucket where buckets are wider, as they are
        // drawn by bucket.
        var reach = Math.Max(
            (long)Math.Ceiling(_viewportDurationNanoseconds / ActualWidth),
            _timeline?.Occupancy.BucketWidth ?? 0);
        SelectFromLookup(
            async timeline =>
                await timeline.NearestAsync(timestamp, start, end, laneChannels).ConfigureAwait(true) ??
                await timeline.NearestAsync(timestamp, start, end, shown).ConfigureAwait(true),
            (timeline, item) => AtPointAsync(timeline, item, reach));
    }

    /// <summary>The most events listed for one point; more are noted, not listed.</summary>
    public const int MostAtPoint = 50;

    // The shown events of the item's lane within reach of its time, in
    // timeline order, the item among them; at most MostAtPoint, and
    // whether there were more.
    private async Task<(IReadOnlyList<SessionTimelineEvent> Events, bool More)> AtPointAsync(
        ISessionTimeline timeline,
        SessionTimelineEvent item,
        long reach)
    {
        var channels = _shownByLane[GetLane(item.Channel)];
        var before = new List<SessionTimelineEvent>();
        var after = new List<SessionTimelineEvent>();
        var more = false;
        for (var from = item; ;)
        {
            var previous = await timeline.AdjacentAsync(from, forward: false, channels).ConfigureAwait(true);
            if (previous is null || item.MonotonicNanoseconds - previous.MonotonicNanoseconds > reach)
            {
                break;
            }

            if (before.Count + after.Count + 1 >= MostAtPoint)
            {
                more = true;
                break;
            }

            before.Add(previous);
            from = previous;
        }

        for (var from = item; ;)
        {
            var next = await timeline.AdjacentAsync(from, forward: true, channels).ConfigureAwait(true);
            if (next is null || next.MonotonicNanoseconds - item.MonotonicNanoseconds > reach)
            {
                break;
            }

            if (before.Count + after.Count + 1 >= MostAtPoint)
            {
                more = true;
                break;
            }

            after.Add(next);
            from = next;
        }

        before.Reverse();
        return ([.. before, item, .. after], more);
    }

    // Left, Right, Home, and End move within the current lane; Up and Down
    // move to the nearest event in the next lane above or below that has
    // shown events. At either end of a lane, or with no lane beyond, the
    // selection stays. With no selection, Left and Right select the lane's
    // first event, and Up and Down search from the playhead.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_timeline is null ||
            e.Key is not (Key.Left or Key.Right or Key.Home or Key.End or Key.Up or Key.Down))
        {
            return;
        }

        e.Handled = true;
        if (_inWindowStrip)
        {
            StepInWindowStrip(e.Key);
            return;
        }

        var selected = _selectedEvent is { } item && _shownChannels.Contains(item.Channel) ? item : null;
        var lane = selected is null ? FirstShownLane(_currentLane) : GetLane(selected.Channel);
        if (lane < 0)
        {
            return;
        }

        var key = e.Key;
        var laneChannels = _shownByLane[lane];
        if (key is Key.Up or Key.Down)
        {
            var next = NextShownLane(lane, key == Key.Down ? 1 : -1);

            // Moving past the Screen reader lane enters the window strip
            // under it.
            if (_windowEvents.Count > 0 &&
                (key == Key.Down ? lane <= ModeLane && (next < 0 || next > ModeLane) : lane > ModeLane && next <= ModeLane))
            {
                var at = selected?.MonotonicNanoseconds ?? _positionNanoseconds;
                SelectWindowEvent(_windowEvents.MinBy(item => Math.Abs(item.MonotonicNanoseconds - at))!);
                return;
            }

            if (next < 0)
            {
                return;
            }

            var nextChannels = _shownByLane[next];
            var time = selected?.MonotonicNanoseconds ?? _positionNanoseconds;
            var duration = _durationNanoseconds;
            _currentLane = next;
            SelectFromLookup(timeline => timeline.NearestAsync(time, 0, duration, nextChannels));
            return;
        }

        SelectFromLookup(async timeline => key switch
        {
            Key.Home => await timeline.EndAsync(last: false, laneChannels).ConfigureAwait(true),
            Key.End => await timeline.EndAsync(last: true, laneChannels).ConfigureAwait(true),
            _ when selected is null => await timeline.EndAsync(last: false, laneChannels).ConfigureAwait(true),
            _ => await timeline.AdjacentAsync(selected, key == Key.Right, laneChannels).ConfigureAwait(true) ??
                selected
        });
    }

    // Left, Right, Home, and End step through the window strip's events;
    // Up and Down leave it for the nearest shown lane above or below.
    private void StepInWindowStrip(Key key)
    {
        if (_windowEvents.Count == 0)
        {
            _inWindowStrip = false;
            return;
        }

        var selected = _selectedEvent is { } item ? _windowEvents.FindIndex(candidate => candidate.EventKey == item.EventKey) : -1;
        if (key is Key.Up or Key.Down)
        {
            var lane = key == Key.Up
                ? (ModeLane < _shownByLane.Length && _shownByLane[ModeLane].Count > 0 ? ModeLane : NextShownLane(ModeLane, -1))
                : NextShownLane(ModeLane, 1);
            if (lane < 0)
            {
                return;
            }

            _inWindowStrip = false;
            var channels = _shownByLane[lane];
            var time = selected >= 0 ? _windowEvents[selected].MonotonicNanoseconds : _positionNanoseconds;
            var duration = _durationNanoseconds;
            _currentLane = lane;
            SelectFromLookup(timeline => timeline.NearestAsync(time, 0, duration, channels));
            return;
        }

        var index = key switch
        {
            Key.Home => 0,
            Key.End => _windowEvents.Count - 1,
            Key.Right when selected >= 0 => Math.Min(selected + 1, _windowEvents.Count - 1),
            Key.Left when selected >= 0 => Math.Max(selected - 1, 0),
            // From the playhead: the next event after it, or the last before.
            Key.Right => _windowEvents.FindIndex(candidate => candidate.MonotonicNanoseconds > _positionNanoseconds),
            _ => _windowEvents.FindLastIndex(candidate => candidate.MonotonicNanoseconds < _positionNanoseconds)
        };
        if (index >= 0)
        {
            SelectWindowEvent(_windowEvents[index]);
        }
    }

    // Selects a window strip event, with the strip's other events drawn at
    // its point.
    private void SelectWindowEvent(SessionTimelineEvent item)
    {
        _request++;
        _inWindowStrip = true;
        var reach = ActualWidth > 0 ? (long)Math.Ceiling(_viewportDurationNanoseconds / ActualWidth) : 0;
        var together = _windowEvents
            .Where(candidate => Math.Abs(candidate.MonotonicNanoseconds - item.MonotonicNanoseconds) <= reach)
            .ToList();
        var more = together.Count > MostAtPoint;
        SelectEvent(item, together.Count > MostAtPoint ? together.Take(MostAtPoint).ToList() : together, more);
    }

    // The lane itself if it has shown events, or else the first lane that
    // does, or -1.
    private int FirstShownLane(int lane)
    {
        if (lane >= 0 && lane < _shownByLane.Length && _shownByLane[lane].Count > 0)
        {
            return lane;
        }

        return Array.FindIndex(_shownByLane, channels => channels.Count > 0);
    }

    private int NextShownLane(int lane, int direction)
    {
        for (var next = lane + direction; next >= 0 && next < _shownByLane.Length; next += direction)
        {
            if (_shownByLane[next].Count > 0)
            {
                return next;
            }
        }

        return -1;
    }

    private async void SelectFromLookup(
        Func<ISessionTimeline, Task<SessionTimelineEvent?>> lookup,
        Func<ISessionTimeline, SessionTimelineEvent, Task<(IReadOnlyList<SessionTimelineEvent> Events, bool More)>>? atPoint = null)
    {
        if (_timeline is not { } timeline)
        {
            return;
        }

        var request = ++_request;
        try
        {
            var item = await lookup(timeline).ConfigureAwait(true);
            var together = item is not null && atPoint is not null
                ? await atPoint(timeline, item).ConfigureAwait(true)
                : default;
            if (request == _request)
            {
                SelectEvent(item, together.Events, together.More);
            }
        }
        catch (Exception exception) when (
            exception is DbException or InvalidOperationException or
                InvalidDataException or ObjectDisposedException or IOException)
        {
            if (request == _request)
            {
                LookupFailed?.Invoke(this, exception.Message);
            }
        }
    }

    private void RedrawAll()
    {
        RedrawEvents();
        RedrawOverlay();
    }

    // The event layer changes only with the session, filters, viewport, or
    // size. Drawing is one rectangle per occupied pixel column per series,
    // so it is bounded by the control width rather than the event count.
    private void RedrawEvents()
    {
        using var drawingContext = _eventLayer.RenderOpen();
        var width = ActualWidth;
        var height = ActualHeight;
        drawingContext.DrawRectangle(
            BackgroundBrush,
            null,
            new Rect(0, 0, width, height));
        if (!CanDraw())
        {
            return;
        }

        var laneHeight = LaneHeight;
        var columns = (int)Math.Ceiling(width);
        var viewportEnd = _viewportStartNanoseconds + _viewportDurationNanoseconds;
        foreach (var period in _unrecordable)
        {
            if (period.End < _viewportStartNanoseconds || period.Start > viewportEnd)
            {
                continue;
            }

            var left = Math.Max(0, ToX(Math.Max(period.Start, _viewportStartNanoseconds)));
            var right = Math.Min(width, ToX(Math.Min(period.End, viewportEnd)));
            drawingContext.DrawRectangle(
                UnrecordableBrush,
                null,
                new Rect(left, LaneTop(GetLane("input.keyboard"), laneHeight), Math.Max(1, right - left), 2 * laneHeight - 1));
        }

        var stripTop = StripTop(laneHeight);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var span in _modeSpans)
        {
            if (span.End < _viewportStartNanoseconds || span.Start > viewportEnd)
            {
                continue;
            }

            var left = Math.Max(0, ToX(Math.Max(span.Start, _viewportStartNanoseconds)));
            var right = Math.Min(width, ToX(Math.Min(span.End, viewportEnd)));
            var band = new Rect(left, stripTop + 1, Math.Max(2, right - left), ModeStripHeight - 2);
            var (fill, word, textBrush) = span.Kind switch
            {
                Recorder.Session.ScreenReaderModeKind.Browse => (BrowseModeBrush, "browse", ModeLightTextBrush),
                Recorder.Session.ScreenReaderModeKind.Focus => (FocusModeBrush, "focus", ModeDarkTextBrush),
                _ => (ModeChangedBrush, "changed", ModeLightTextBrush)
            };
            drawingContext.DrawRectangle(fill, null, band);
            var text = new FormattedText(
                word,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                11,
                textBrush,
                pixelsPerDip);
            if (text.Width + 8 <= band.Width)
            {
                var origin = new Point(band.Left + 4, band.Top + (band.Height - text.Height) / 2);
                if (span.Kind == Recorder.Session.ScreenReaderModeKind.Changed)
                {
                    // The stripes would cross the word: a backing behind it.
                    drawingContext.DrawRectangle(
                        BackgroundBrush, null, new Rect(origin.X - 2, band.Top + 1, text.Width + 4, band.Height - 2));
                }

                drawingContext.DrawText(text, origin);
            }
        }

        var windowTop = stripTop + ModeHeight;
        foreach (var span in _windowSpans)
        {
            if (span.End < _viewportStartNanoseconds || span.Start > viewportEnd)
            {
                continue;
            }

            var left = Math.Max(0, ToX(Math.Max(span.Start, _viewportStartNanoseconds)));
            var right = Math.Min(width, ToX(Math.Min(span.End, viewportEnd)));
            // A pixel short at the right, so windows that follow one another
            // show as two.
            var band = new Rect(left, windowTop + 1, Math.Max(2, right - left - 1), WindowStripHeight - 2);
            drawingContext.DrawRectangle(NvdaWindowBrush, null, band);
            var text = new FormattedText(
                span.Name,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                11,
                ModeLightTextBrush,
                pixelsPerDip);
            if (text.Width + 8 <= band.Width)
            {
                drawingContext.DrawText(text, new Point(band.Left + 4, band.Top + (band.Height - text.Height) / 2));
            }

            foreach (var item in span.Events)
            {
                if (item.MonotonicNanoseconds >= _viewportStartNanoseconds && item.MonotonicNanoseconds <= viewportEnd)
                {
                    drawingContext.DrawRectangle(
                        NvdaWindowMarkBrush, null, new Rect(Math.Floor(ToX(item.MonotonicNanoseconds)), band.Bottom - 4, 1.5, 4));
                }
            }
        }

        var readingTop = windowTop + WindowHeight;
        foreach (var span in _readingSpans)
        {
            if (span.End < _viewportStartNanoseconds || span.Start > viewportEnd)
            {
                continue;
            }

            var left = Math.Max(0, ToX(Math.Max(span.Start, _viewportStartNanoseconds)));
            var right = Math.Min(width, ToX(Math.Min(span.End, viewportEnd)));
            var band = new Rect(left, readingTop + 1, Math.Max(2, right - left - 1), ReadingStripHeight - 2);
            var uncertain = span.Kind == Recorder.Session.ReadingPositionKind.Uncertain;
            drawingContext.DrawRectangle(uncertain ? ReadingUncertainBrush : ReadingKnownBrush, null, band);
            var text = new FormattedText(
                uncertain ? "uncertain" : span.Label,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                11,
                ModeLightTextBrush,
                pixelsPerDip);
            if (text.Width + 8 <= band.Width)
            {
                var origin = new Point(band.Left + 4, band.Top + (band.Height - text.Height) / 2);
                if (uncertain)
                {
                    // The stripes would cross the word: a backing behind it.
                    drawingContext.DrawRectangle(
                        ReadingKnownBrush, null, new Rect(origin.X - 2, band.Top + 1, text.Width + 4, band.Height - 2));
                }

                drawingContext.DrawText(text, origin);
            }
        }

        foreach (var series in SeriesDrawOrder)
        {
            var lane = LaneOfSeries(series);
            var brush = BrushOfSeries(series);
            foreach (var column in _timeline!.Occupancy.OccupiedColumns(
                         _shownBySeries[series],
                         _viewportStartNanoseconds,
                         _viewportDurationNanoseconds,
                         columns))
            {
                drawingContext.DrawRectangle(
                    brush,
                    null,
                    new Rect(column, LaneTop(lane, laneHeight), 1.5, laneHeight - 1));
            }
        }
    }

    // The overlay holds the playhead and the selection highlight, so moving
    // the playhead redraws two shapes instead of the whole timeline.
    private void RedrawOverlay()
    {
        using var drawingContext = _overlayLayer.RenderOpen();
        if (!CanDraw())
        {
            return;
        }

        var laneHeight = LaneHeight;
        var viewportEnd = _viewportStartNanoseconds + _viewportDurationNanoseconds;
        if (_selectedEvent is not null &&
            (_inWindowStrip || _shownChannels.Contains(_selectedEvent.Channel)) &&
            _selectedEvent.MonotonicNanoseconds >= _viewportStartNanoseconds &&
            _selectedEvent.MonotonicNanoseconds <= viewportEnd)
        {
            var x = Math.Floor(ToX(_selectedEvent.MonotonicNanoseconds));
            var lane = GetLane(_selectedEvent.Channel);
            drawingContext.DrawRectangle(
                null,
                HighlightPen,
                _inWindowStrip
                    ? new Rect(x - 3, StripTop(laneHeight) + ModeHeight, 7, WindowStripHeight - 1)
                    : new Rect(x - 3, LaneTop(lane, laneHeight), 7, laneHeight - 1));
        }

        if (_positionNanoseconds >= _viewportStartNanoseconds &&
            _positionNanoseconds <= viewportEnd)
        {
            var playheadX = ToX(_positionNanoseconds);
            drawingContext.DrawLine(
                HighlightPen,
                new Point(playheadX, 0),
                new Point(playheadX, ActualHeight));
        }
    }

    private bool CanDraw() =>
        _timeline is not null &&
        _durationNanoseconds > 0 &&
        _viewportDurationNanoseconds > 0 &&
        ActualWidth > 0 &&
        ActualHeight > 0;

    private double ToX(long timestamp) =>
        (timestamp - _viewportStartNanoseconds) /
        (double)_viewportDurationNanoseconds * ActualWidth;

    private void RebuildShownChannels()
    {
        _shownChannels = new HashSet<string>(StringComparer.Ordinal);
        _shownByLane = Enumerable.Range(0, LaneCount)
            .Select(_ => new HashSet<string>(StringComparer.Ordinal))
            .ToArray();
        _shownBySeries = Enumerable.Range(0, SeriesCount)
            .Select(_ => new List<string>())
            .ToArray();
        foreach (var channel in _timeline?.ChannelCounts.Keys ?? [])
        {
            if (!IsChannelVisible(channel))
            {
                continue;
            }

            _shownChannels.Add(channel);
            _shownByLane[GetLane(channel)].Add(channel);
            _shownBySeries[GetSeries(channel)].Add(channel);
        }
    }

    private bool IsChannelVisible(string channel) =>
        _visibleChannels is null || _visibleChannels.Contains(channel);

    /// <summary>
    /// Selects an event from outside the timeline, such as a reading
    /// position step, raising <see cref="SelectedEventChanged"/> even when
    /// it is already selected, so the playhead moves back to it.
    /// </summary>
    public void SelectTimelineEvent(SessionTimelineEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _selectedEvent = null;
        _inWindowStrip = false;
        SelectEvent(item);
    }

    private void SelectEvent(
        SessionTimelineEvent? item,
        IReadOnlyList<SessionTimelineEvent>? atPoint = null,
        bool moreAtPoint = false)
    {
        if (Equals(_selectedEvent, item) && (atPoint is null || atPoint.Count <= 1))
        {
            return;
        }

        _selectedEvent = item;
        if (item is not null)
        {
            _currentLane = GetLane(item.Channel);
        }
        RedrawOverlay();
        SelectedEventChanged?.Invoke(
            this,
            new TimelineEventSelectedEventArgs(item)
            {
                AtPoint = atPoint is { Count: > 1 } ? atPoint : item is null ? [] : [item],
                MoreAtPoint = moreAtPoint
            });
    }

    private static int GetLane(string channel) => channel switch
    {
        // The keyboard hook's keys share the keyboard lane with raw input's.
        "input.keyboard" or "input.keyboard-hook" => 0,
        // The mouse hook's events share the mouse lane with raw input's.
        "input.mouse" or "input.mouse-hook" => 1,
        "accessibility.uia.events" => 2,
        "window.foreground" => 3,
        "graphics.desktop.frames" => 4,
        "audio.microphone" => 5,
        "audio.system" => 6,
        "system.preferences" => 7,
        "graphics.magnifier" => 8,
        "browser.preferences" => 9,
        "system.assistive-technology" => 10,
        _ => OtherLane
    };

    private static int GetSeries(string channel)
    {
        var lane = GetLane(channel);
        if (lane != OtherLane)
        {
            return lane;
        }

        return channel == "session.annotations" ? AnnotationSeries : OtherSeries;
    }

    private static int LaneOfSeries(int series) =>
        series < OtherLane ? series : OtherLane;

    private static Brush BrushOfSeries(int series) => series switch
    {
        0 => ChannelBrushes["input.keyboard"],
        1 => ChannelBrushes["input.mouse"],
        2 => ChannelBrushes["accessibility.uia.events"],
        3 => ChannelBrushes["window.foreground"],
        4 => ChannelBrushes["graphics.desktop.frames"],
        5 => ChannelBrushes["audio.microphone"],
        6 => ChannelBrushes["audio.system"],
        7 => ChannelBrushes["system.preferences"],
        8 => ChannelBrushes["graphics.magnifier"],
        9 => ChannelBrushes["browser.preferences"],
        10 => ChannelBrushes["system.assistive-technology"],
        AnnotationSeries => ChannelBrushes["session.annotations"],
        _ => OtherChannelBrush
    };

    private static SolidColorBrush Freeze(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateStripeBrush(string color, Point from, Point to, double thickness)
    {
        var stripe = new GeometryDrawing(null, FreezePen(Freeze(color), thickness), new LineGeometry(from, to));
        var brush = new DrawingBrush(stripe)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 8, 8),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static Brush CreateUnrecordableBrush()
    {
        var stripe = new GeometryDrawing(
            null,
            FreezePen(Freeze("#9A9894"), 1.5),
            new LineGeometry(new Point(0, 8), new Point(8, 0)));
        var brush = new DrawingBrush(stripe)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 8, 8),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static Pen FreezePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}

public sealed class TimelineEventSelectedEventArgs : EventArgs
{
    public TimelineEventSelectedEventArgs(SessionTimelineEvent? timelineEvent)
    {
        TimelineEvent = timelineEvent;
    }

    public SessionTimelineEvent? TimelineEvent { get; }

    /// <summary>
    /// The events drawn at the selected event's point, for a click: more
    /// than one where the zoom draws several at one point, in timeline order.
    /// Otherwise the selected event alone.
    /// </summary>
    public IReadOnlyList<SessionTimelineEvent> AtPoint { get; init; } = [];

    /// <summary>Whether more events were at the point than <see cref="AtPoint"/> lists.</summary>
    public bool MoreAtPoint { get; init; }
}
