using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Recorder.Collectors.Android;

namespace Recorder.App;

public partial class AndroidLivePreviewWindow : Window
{
    private static readonly Duration NoWait = new(TimeSpan.Zero);
    private readonly AndroidLivePreview _preview;
    private readonly AndroidLiveTree _liveTree;
    private readonly AndroidTalkBackExploration _exploration;
    private readonly Func<string, bool> _recordMarker;
    private readonly Action<Task> _trackExploration;
    private readonly DispatcherTimer _timer;
    private AndroidLiveTreeWindow? _treeWindow;
    private AndroidTalkBackExplorationWindow? _explorationWindow;
    private WriteableBitmap? _bitmap;
    private AndroidLivePreviewState? _state;
    private int _streamSession;
    private ulong? _sequence;
    private bool _renderFailed;

    public AndroidLivePreviewWindow(AndroidLivePreview preview, AndroidLiveTree liveTree,
        AndroidTalkBackExploration exploration, Func<string, bool> recordMarker, Action<Task> trackExploration)
    {
        _preview = preview;
        _liveTree = liveTree;
        _exploration = exploration;
        _recordMarker = recordMarker;
        _trackExploration = trackExploration;
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(34)
        };
        _timer.Tick += (_, _) => RefreshFrame();
        Loaded += (_, _) =>
        {
            _timer.Start();
            RefreshFrame();
            CloseButton.Focus();
        };
        Closed += (_, _) =>
        {
            StopExploration();
            _timer.Stop();
            ClearImage();
        };
    }

    internal void StopExploration() => _explorationWindow?.StopExploration();

    private void RefreshFrame()
    {
        if (_renderFailed || WindowState == WindowState.Minimized || !IsVisible)
        {
            return;
        }

        var snapshot = _preview.Current;
        if (_streamSession != snapshot.StreamSession || snapshot.Frame is null)
        {
            ClearImage();
            _streamSession = snapshot.StreamSession;
        }

        if (_state != snapshot.State)
        {
            _state = snapshot.State;
            SetStatus(snapshot.State switch
            {
                AndroidLivePreviewState.ShowingFrames => "Showing the live recording stream.",
                AndroidLivePreviewState.StreamUnavailable => "The phone's screen stream is unavailable. The recording can continue without it.",
                AndroidLivePreviewState.FramesUnavailable => "Preview frames are unavailable. The phone's video stream is still being recorded.",
                AndroidLivePreviewState.Stopped => "The Android recording has stopped.",
                _ => "Waiting for a picture from the phone."
            });
        }

        if (snapshot.Frame is not { } frame || frame.Sequence == _sequence ||
            !MemoryMarshal.TryGetArray(frame.Rgb, out var rgb) || rgb.Array is null)
        {
            return;
        }

        try
        {
            TryPresent(frame, rgb.Array, rgb.Offset);
        }
        catch (COMException exception)
        {
            Trace.TraceWarning("Android preview could not draw a frame: {0}", exception.Message);
            _renderFailed = true;
            _timer.Stop();
            ClearImage();
            SetStatus("Windows could not draw the preview. Close and reopen it to try again. This does not stop the recording.");
        }
    }

    private void TryPresent(AndroidLivePreviewFrame frame, byte[] rgb, int offset)
    {
        var bitmap = _bitmap ?? new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Rgb24, null);
        if (!bitmap.TryLock(NoWait))
        {
            // If the render thread is busy, try the newest picture next tick.
            return;
        }

        try
        {
            bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), rgb, checked(frame.Width * 3), offset);
        }
        finally
        {
            bitmap.Unlock();
        }

        if (_bitmap is null)
        {
            _bitmap = bitmap;
            PhoneImage.Source = bitmap;
        }
        _sequence = frame.Sequence;
    }

    private void SetStatus(string text)
    {
        PreviewStatus.Text = text;
        var peer = UIElementAutomationPeer.FromElement(PreviewStatus) ??
            UIElementAutomationPeer.CreatePeerForElement(PreviewStatus);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void ClearImage()
    {
        if (_bitmap is not null)
        {
            PhoneImage.Source = null;
            _bitmap = null;
            _sequence = null;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            StopExploration();
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        StopExploration();
        Close();
    }

    private void ExploreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_explorationWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }
            open.Activate();
            return;
        }

        var window = new AndroidTalkBackExplorationWindow(_exploration, _recordMarker, _trackExploration) { Owner = this };
        PlaceTreeWindow(window);
        _explorationWindow = window;
        window.Closed += (_, _) =>
        {
            _explorationWindow = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsActive && IsVisible)
                {
                    ExploreButton.Focus();
                }
            }));
        };
        window.Show();
    }

    private void TreeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_treeWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }
            open.Activate();
            return;
        }

        var window = new AndroidLiveTreeWindow(_liveTree) { Owner = this };
        PlaceTreeWindow(window);
        _treeWindow = window;
        window.Closed += (_, _) =>
        {
            _treeWindow = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsActive && IsVisible)
                {
                    TreeButton.Focus();
                }
            }));
        };
        window.Show();
    }

    private void PlaceTreeWindow(Window window)
    {
        // Put both views on screen when the preview is on the primary
        // display and there is room. Otherwise use the normal owned-window
        // placement so a preview on another display keeps its inspector there.
        var area = SystemParameters.WorkArea;
        if (!area.Contains(Left, Top) || Left + ActualWidth > area.Right)
        {
            return;
        }

        const double gap = 8;
        var right = area.Right - Left - ActualWidth - gap;
        var left = Left - area.Left - gap;
        var room = Math.Max(right, left);
        if (room < window.MinWidth || area.Height < window.MinHeight)
        {
            return;
        }
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = Math.Min(window.Width, room);
        window.Height = Math.Min(window.Height, area.Height);
        window.Left = right >= left ? Left + ActualWidth + gap : Left - gap - window.Width;
        window.Top = Math.Clamp(Top, area.Top, area.Bottom - window.Height);
    }
}
