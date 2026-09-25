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
            ["session.annotations"] = Freeze("#E8AF34")
        };

    private const int LaneCount = 8;
    private const int OtherLane = 7;
    private const int AnnotationSeries = 7;
    private const int OtherSeries = 8;
    private const int SeriesCount = 9;

    // Series are drawn in this order, so markers stay visible over other
    // channels that share their lane.
    private static readonly int[] SeriesDrawOrder = [0, 1, 2, 3, 4, 5, 6, OtherSeries, AnnotationSeries];

    private static readonly Brush BackgroundBrush = Freeze("#201F1D");
    private static readonly Brush OtherChannelBrush = Freeze("#BAB9B4");
    private static readonly Pen HighlightPen = FreezePen(Brushes.White, 2);

    private readonly DrawingVisual _eventLayer = new();
    private readonly DrawingVisual _overlayLayer = new();
    private readonly VisualCollection _layers;
    private IReadOnlyList<SessionTimelineEvent> _events = [];
    private IReadOnlySet<string> _visibleChannels = new HashSet<string>();
    private bool _showOtherChannels = true;
    private SessionTimelineIndex _index = CreateIndex([], _ => false);
    private long _durationNanoseconds;
    private long _positionNanoseconds;
    private long _viewportStartNanoseconds;
    private long _viewportDurationNanoseconds;
    private SessionTimelineEvent? _selectedEvent;
    private int _selectedIndex = -1;

    public SessionTimelineControl()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        _layers = new VisualCollection(this) { _eventLayer, _overlayLayer };
    }

    public event EventHandler<TimelineEventSelectedEventArgs>? SelectedEventChanged;

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
        IReadOnlyList<SessionTimelineEvent> events,
        long durationNanoseconds)
    {
        _events = events;
        _durationNanoseconds = Math.Max(0, durationNanoseconds);
        _viewportStartNanoseconds = 0;
        _viewportDurationNanoseconds = _durationNanoseconds;
        _positionNanoseconds = 0;
        RebuildIndex();
        SelectEvent(null);
        RedrawAll();
    }

    public void SetVisibleChannels(
        IReadOnlySet<string> visibleChannels,
        bool showOtherChannels)
    {
        _visibleChannels = visibleChannels;
        _showOtherChannels = showOtherChannels;
        RebuildIndex();
        if (_selectedEvent is not null && _selectedIndex < 0)
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
        var viewportEnd = _viewportStartNanoseconds + _viewportDurationNanoseconds;
        SelectEvent(
            _index.NearestInLane(lane, timestamp, _viewportStartNanoseconds, viewportEnd) ??
            _index.Nearest(timestamp, _viewportStartNanoseconds, viewportEnd));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var events = _index.VisibleEvents;
        if (events.Count == 0)
        {
            return;
        }

        var index = _selectedIndex;
        SessionTimelineEvent? next = e.Key switch
        {
            Key.Left => events[Math.Max(0, index - 1)],
            Key.Right => events[Math.Min(events.Count - 1, index + 1)],
            Key.Home => events[0],
            Key.End => events[^1],
            _ => null
        };
        if (next is null)
        {
            return;
        }

        SelectEvent(next);
        e.Handled = true;
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
            foreach (var column in _index.OccupiedColumns(
                         series,
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
            _selectedIndex >= 0 &&
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
        _durationNanoseconds > 0 &&
        _viewportDurationNanoseconds > 0 &&
        ActualWidth > 0 &&
        ActualHeight > 0;

    private double ToX(long timestamp) =>
        (timestamp - _viewportStartNanoseconds) /
        (double)_viewportDurationNanoseconds * ActualWidth;

    private void RebuildIndex()
    {
        _index = CreateIndex(_events, IsChannelVisible);
        _selectedIndex = _index.IndexOf(_selectedEvent);
    }

    private static SessionTimelineIndex CreateIndex(
        IReadOnlyList<SessionTimelineEvent> events,
        Func<string, bool> isVisible) =>
        new(events, isVisible, GetLane, GetSeries, LaneCount, SeriesCount);

    private bool IsChannelVisible(string channel) =>
        _visibleChannels.Contains(channel) ||
        (_showOtherChannels && !ChannelBrushes.ContainsKey(channel));

    private void SelectEvent(SessionTimelineEvent? item)
    {
        if (ReferenceEquals(_selectedEvent, item))
        {
            return;
        }

        _selectedEvent = item;
        _selectedIndex = _index.IndexOf(item);
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
