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
            ["session.annotations"] = Freeze("#E8AF34")
        };

    // Lane 7 holds the Windows settings records (system.preferences), and
    // lane 8 the Magnifier change records (graphics.magnifier).
    private const int LaneCount = 10;
    private const int OtherLane = 9;
    private const int AnnotationSeries = 9;
    private const int OtherSeries = 10;
    private const int SeriesCount = 11;

    // Series are drawn in this order, so markers stay visible over other
    // channels that share their lane.
    private static readonly int[] SeriesDrawOrder = [0, 1, 2, 3, 4, 5, 6, 7, 8, OtherSeries, AnnotationSeries];

    private static readonly Brush BackgroundBrush = Freeze("#201F1D");
    private static readonly Brush OtherChannelBrush = Freeze("#BAB9B4");
    private static readonly Pen HighlightPen = FreezePen(Brushes.White, 2);

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
        RebuildShownChannels();
        SelectEvent(null);
        RedrawAll();
    }

    public void SetVisibleChannels(IReadOnlySet<string> visibleChannels)
    {
        ArgumentNullException.ThrowIfNull(visibleChannels);
        _visibleChannels = visibleChannels;
        _request++;
        RebuildShownChannels();
        if (_selectedEvent is not null && !_shownChannels.Contains(_selectedEvent.Channel))
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
        var laneHeight = Math.Max(3, ActualHeight / LaneCount);
        var lane = Math.Clamp((int)(point.Y / laneHeight), 0, LaneCount - 1);
        var start = _viewportStartNanoseconds;
        var end = start + _viewportDurationNanoseconds;
        var laneChannels = _shownByLane[lane];
        var shown = _shownChannels;
        _currentLane = lane;
        e.Handled = true;
        SelectFromLookup(async timeline =>
            await timeline.NearestAsync(timestamp, start, end, laneChannels).ConfigureAwait(true) ??
            await timeline.NearestAsync(timestamp, start, end, shown).ConfigureAwait(true));
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
        Func<ISessionTimeline, Task<SessionTimelineEvent?>> lookup)
    {
        if (_timeline is not { } timeline)
        {
            return;
        }

        var request = ++_request;
        try
        {
            var item = await lookup(timeline).ConfigureAwait(true);
            if (request == _request)
            {
                SelectEvent(item);
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

        var laneHeight = Math.Max(3, height / LaneCount);
        var columns = (int)Math.Ceiling(width);
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
                    new Rect(column, lane * laneHeight, 1.5, laneHeight - 1));
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

        var laneHeight = Math.Max(3, ActualHeight / LaneCount);
        var viewportEnd = _viewportStartNanoseconds + _viewportDurationNanoseconds;
        if (_selectedEvent is not null &&
            _shownChannels.Contains(_selectedEvent.Channel) &&
            _selectedEvent.MonotonicNanoseconds >= _viewportStartNanoseconds &&
            _selectedEvent.MonotonicNanoseconds <= viewportEnd)
        {
            var x = Math.Floor(ToX(_selectedEvent.MonotonicNanoseconds));
            var lane = GetLane(_selectedEvent.Channel);
            drawingContext.DrawRectangle(
                null,
                HighlightPen,
                new Rect(x - 3, lane * laneHeight, 7, laneHeight - 1));
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

    private void SelectEvent(SessionTimelineEvent? item)
    {
        if (Equals(_selectedEvent, item))
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
            new TimelineEventSelectedEventArgs(item));
    }

    private static int GetLane(string channel) => channel switch
    {
        "input.keyboard" => 0,
        "input.mouse" => 1,
        "accessibility.uia.events" => 2,
        "window.foreground" => 3,
        "graphics.desktop.frames" => 4,
        "audio.microphone" => 5,
        "audio.system" => 6,
        "system.preferences" => 7,
        "graphics.magnifier" => 8,
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
        AnnotationSeries => ChannelBrushes["session.annotations"],
        _ => OtherChannelBrush
    };

    private static SolidColorBrush Freeze(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
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
}
