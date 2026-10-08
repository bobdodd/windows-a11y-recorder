using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Recorder.Session;

namespace Recorder.App;

// Playback of Windows Magnifier's full screen view: by default each frame
// shows the part of the screen the participant saw, drawn from the frame as
// captured; "Whole screen" shows the frame as captured with that part
// outlined. The frames are not changed. See
// docs/architecture/magnified-view-playback.md.
public partial class MainWindow
{
    private static readonly Brush OutlineBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x00)));

    private bool _showWholeScreen;
    private bool _redrawFrame;

    // Called when a recording is opened, or fails to open: playback starts
    // in the participant's view, and the toggle is offered only when some
    // frame was magnified.
    private void ResetFrameView()
    {
        _showWholeScreen = false;
        _redrawFrame = true;
        var magnified = _playbackArchive is { } archive && MagnifiedView.AnyMagnified(archive.Frames);
        WholeScreenToggle.IsChecked = false;
        WholeScreenToggle.IsEnabled = magnified;
        AutomationProperties.SetHelpText(
            WholeScreenToggle,
            _playbackArchive is null
                ? "No recording is open."
                : magnified
                    ? "Off: the participant's view, the part of the screen Magnifier's full screen view showed. " +
                      "On: the whole screen as captured, with that part outlined."
                    : "Unavailable: no frame of this recording was magnified with Magnifier's full screen view, " +
                      "so each frame is the screen as the participant saw it.");
    }

    private void WholeScreenToggle_Click(object sender, RoutedEventArgs e)
    {
        _showWholeScreen = WholeScreenToggle.IsChecked == true;
        _redrawFrame = true;
        DisplayFrameAt(_playbackPositionNanoseconds);
        _busy.AnnounceLayout(_showWholeScreen ? "Whole screen" : "Participant's view");
    }

    // The image shown for a frame, and the words its help text gains.
    private ImageSource FrameImage(SessionVideoFrame frame, BitmapSource bitmap, out string view)
    {
        if (MagnifiedView.VisibleRegion(frame) is not { } region)
        {
            view = string.Empty;
            return bitmap;
        }

        var magnification = frame.Magnification!;
        var percent = Math.Round(magnification.Level * 100).ToString(CultureInfo.CurrentCulture);
        // The region is in the frame's pixels; the bitmap is drawn at its
        // pixel size, which is the frame's unless the file differs.
        var width = (double)bitmap.PixelWidth;
        var height = (double)bitmap.PixelHeight;
        var scaleX = width / frame.Width;
        var scaleY = height / frame.Height;
        var seen = new Rect(region.X * scaleX, region.Y * scaleY, region.Width * scaleX, region.Height * scaleY);
        var whole = new Rect(0, 0, width, height);
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        if (_showWholeScreen)
        {
            group.Children.Add(new ImageDrawing(bitmap, whole));
            // A yellow line over a black one, so the outline shows on light
            // and dark content; drawn by the player, not part of the frame.
            var thickness = Math.Max(2, width / 480);
            group.Children.Add(new GeometryDrawing(null, Frozen(new Pen(Brushes.Black, thickness * 2)), new RectangleGeometry(seen)));
            group.Children.Add(new GeometryDrawing(null, Frozen(new Pen(OutlineBrush, thickness)), new RectangleGeometry(seen)));
            group.ClipGeometry = new RectangleGeometry(whole);
            view = $", whole screen as captured, with the participant's view at {percent} percent magnification outlined";
        }
        else
        {
            // Black where the part seen extends beyond the captured screen.
            group.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(seen)));
            group.Children.Add(new ImageDrawing(bitmap, whole));
            group.ClipGeometry = new RectangleGeometry(seen);
            view = $", participant's view at {percent} percent magnification, offset {magnification.X}, {magnification.Y}";
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
