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

    private IReadOnlyList<SessionTimelineEvent> _events = [];
    private IReadOnlySet<string> _visibleChannels = new HashSet<string>();
    private bool _showOtherChannels = true;
    private long _durationNanoseconds;
    private long _positionNanoseconds;
    private long _viewportStartNanoseconds;
    private long _viewportDurationNanoseconds;
    private SessionTimelineEvent? _selectedEvent;

    public SessionTimelineControl()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public event EventHandler<TimelineEventSelectedEventArgs>? SelectedEventChanged;

    public long PositionNanoseconds
    {
        get => _positionNanoseconds;
        set
        {
            _positionNanoseconds = Math.Clamp(value, 0, _durationNanoseconds);
            InvalidateVisual();
        }
    }

    public void SetSession(
        IReadOnlyList<SessionTimelineEvent> events,
        long durationNanoseconds)
    {
        _events = events;
        _durationNanoseconds = Math.Max(0, durationNanoseconds);
        _viewportStartNanoseconds = 0;
        _viewportDurationNanoseconds = _durationNanoseconds;
        _positionNanoseconds = 0;
        SelectEvent(null);
        InvalidateVisual();
    }

    public void SetVisibleChannels(
        IReadOnlySet<string> visibleChannels,
        bool showOtherChannels)
    {
        _visibleChannels = visibleChannels;
        _showOtherChannels = showOtherChannels;
        if (_selectedEvent is not null &&
            !IsChannelVisible(_selectedEvent.Channel))
        {
            SelectEvent(null);
        }

        InvalidateVisual();
    }

    public void SetViewport(long startNanoseconds, long durationNanoseconds)
    {
        var duration = Math.Clamp(
            durationNanoseconds,
            Math.Min(1, _durationNanoseconds),
            _durationNanoseconds);
        _viewportDurationNanoseconds = duration;
        _viewportStartNanoseconds = Math.Clamp(
            startNanoseconds,
            0,
            Math.Max(0, _durationNanoseconds - duration));
        InvalidateVisual();
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
        var laneHeight = Math.Max(3, ActualHeight / 8);
        var lane = Math.Clamp((int)(point.Y / laneHeight), 0, 7);
        var visible = GetVisibleEvents(inViewportOnly: true);
        var candidates = visible.Where(item => GetLane(item.Channel) == lane).ToArray();
        if (candidates.Length == 0)
        {
            candidates = visible.ToArray();
        }

        SelectEvent(candidates
            .MinBy(item => Math.Abs(item.MonotonicNanoseconds - timestamp)));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var events = GetVisibleEvents(inViewportOnly: false);
        if (events.Count == 0)
        {
            return;
        }

        var index = _selectedEvent is null
            ? -1
            : events.IndexOf(_selectedEvent);
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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(Freeze("#201F1D"), null, bounds);
        if (_durationNanoseconds <= 0 ||
            _viewportDurationNanoseconds <= 0 ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return;
        }

        var laneHeight = Math.Max(3, ActualHeight / 8);
        var viewportEnd = _viewportStartNanoseconds + _viewportDurationNanoseconds;
        foreach (var item in _events)
        {
            if (item.MonotonicNanoseconds < _viewportStartNanoseconds ||
                item.MonotonicNanoseconds > viewportEnd ||
                !IsChannelVisible(item.Channel))
            {
                continue;
            }

            var x = (item.MonotonicNanoseconds - _viewportStartNanoseconds) /
                (double)_viewportDurationNanoseconds * ActualWidth;
            var lane = GetLane(item.Channel);
            var brush = ChannelBrushes.TryGetValue(item.Channel, out var known)
                ? known
                : Freeze("#BAB9B4");
            drawingContext.DrawRectangle(
                brush,
                null,
                new Rect(Math.Floor(x), lane * laneHeight, 1.5, laneHeight - 1));
            if (ReferenceEquals(item, _selectedEvent))
            {
                drawingContext.DrawRectangle(
                    null,
                    new Pen(Brushes.White, 2),
                    new Rect(
                        Math.Floor(x) - 3,
                        lane * laneHeight,
                        7,
                        laneHeight - 1));
            }
        }

        if (_positionNanoseconds >= _viewportStartNanoseconds &&
            _positionNanoseconds <= viewportEnd)
        {
            var playheadX = (_positionNanoseconds - _viewportStartNanoseconds) /
                (double)_viewportDurationNanoseconds * ActualWidth;
            drawingContext.DrawLine(
                new Pen(Brushes.White, 2),
                new Point(playheadX, 0),
                new Point(playheadX, ActualHeight));
        }
    }

    private bool IsChannelVisible(string channel) =>
        _visibleChannels.Contains(channel) ||
        (_showOtherChannels && !ChannelBrushes.ContainsKey(channel));

    private List<SessionTimelineEvent> GetVisibleEvents(bool inViewportOnly)
    {
        var viewportEnd = _viewportStartNanoseconds + _viewportDurationNanoseconds;
        return _events
            .Where(item =>
                IsChannelVisible(item.Channel) &&
                (!inViewportOnly ||
                    (item.MonotonicNanoseconds >= _viewportStartNanoseconds &&
                     item.MonotonicNanoseconds <= viewportEnd)))
            .ToList();
    }

    private void SelectEvent(SessionTimelineEvent? item)
    {
        if (ReferenceEquals(_selectedEvent, item))
        {
            return;
        }

        _selectedEvent = item;
        InvalidateVisual();
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
        _ => 7
    };

    private static SolidColorBrush Freeze(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
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
