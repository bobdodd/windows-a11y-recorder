using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using Recorder.Session;

namespace Recorder.App;

// The phone's touches drawn on its picture in the player (Android slice 5b,
// docs/architecture/android-recording.md, "Touches"): a circle for each
// finger while it is down, numbered when there is more than one, with its
// path over the last second. The touches are drawn by the player over the
// frame; the frames are not changed, and an MP4 export does not draw them.
public partial class MainWindow
{
    private static readonly Brush TouchFillBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)));

    // The frame as shown without touches, and the words its help text has.
    private ImageSource? _frameSource;
    private string _frameHelpText = string.Empty;
    private string _videoHelpText = string.Empty;

    private void DisplayTouchesAt(long positionNanoseconds)
    {
        var archive = _playbackArchive;
        if (archive is null ||
            archive.FrameSource != SessionFrameSource.Phone ||
            !archive.PhoneTouches.Recorded ||
            _frameSource is null ||
            _displayedFrameIndex < 0 ||
            archive.PhoneTouches.At(positionNanoseconds) is not { Fingers.Count: > 0 } touches)
        {
            if (!ReferenceEquals(VideoImage.Source, _frameSource))
            {
                VideoImage.Source = _frameSource;
            }

            SetVideoHelpText(string.Empty);
            return;
        }

        var frame = archive.Frames[_displayedFrameIndex];
        VideoImage.Source = WithTouches(_frameSource, frame.Width, frame.Height, touches);
        SetVideoHelpText(touches.Describe());
    }

    private void SetVideoHelpText(string touches)
    {
        var text = touches.Length == 0 ? _frameHelpText : $"{_frameHelpText}, {touches}";
        if (text != _videoHelpText)
        {
            _videoHelpText = text;
            AutomationProperties.SetHelpText(VideoImage, text);
        }
    }

    // The frame with the fingers over it, at the frame's size. The touch
    // positions are in the pixels of the screen size their report gives,
    // and are scaled to the frame's.
    private static ImageSource WithTouches(ImageSource frameImage, int width, int height, PhoneTouches touches)
    {
        var whole = new Rect(0, 0, width, height);
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        group.Children.Add(new ImageDrawing(frameImage, whole));

        // About a fingertip on a phone: 40 pixels on a screen 1080 wide.
        var shortSide = Math.Min(width, height);
        var radius = Math.Max(8, shortSide / 27.0);
        var thickness = Math.Max(2, shortSide / 240.0);
        var dark = Frozen(new Pen(Brushes.Black, thickness * 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var light = Frozen(new Pen(OutlineBrush, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var fingers = touches.ScaledTo(width, height);

        // The paths first, so that no path crosses a circle.
        foreach (var finger in fingers.Where(finger => finger.Path.Count > 1))
        {
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                context.BeginFigure(new Point(finger.Path[0].X, finger.Path[0].Y), false, false);
                context.PolyLineTo(
                    finger.Path.Skip(1).Select(point => new Point(point.X, point.Y)).ToArray(),
                    true,
                    true);
            }

            path.Freeze();
            group.Children.Add(new GeometryDrawing(null, dark, path));
            group.Children.Add(new GeometryDrawing(null, light, path));
        }

        // A yellow ring over a black one, as the Magnifier outline is drawn,
        // so the circle shows on light and dark content, over a dimmed
        // middle that keeps the number readable.
        foreach (var finger in fingers)
        {
            var circle = new EllipseGeometry(new Point(finger.X, finger.Y), radius, radius);
            circle.Freeze();
            group.Children.Add(new GeometryDrawing(TouchFillBrush, dark, circle));
            group.Children.Add(new GeometryDrawing(null, light, circle));
            if (fingers.Count > 1)
            {
                var number = new FormattedText(
                    finger.Number.ToString(CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    radius * 1.2,
                    OutlineBrush,
                    1.0);
                var origin = new Point(finger.X - number.Width / 2, finger.Y - number.Height / 2);
                var text = number.BuildGeometry(origin);
                text.Freeze();
                group.Children.Add(new GeometryDrawing(null, Frozen(new Pen(Brushes.Black, thickness)), text));
                group.Children.Add(new GeometryDrawing(OutlineBrush, null, text));
            }
        }

        group.ClipGeometry = new RectangleGeometry(whole);
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
