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
        var magnified = _playbackArchive is { } archive && MagnifiedView.AnyChanged(archive.Frames);
        WholeScreenToggle.IsChecked = false;
        WholeScreenToggle.IsEnabled = magnified;
        AutomationProperties.SetHelpText(
            WholeScreenToggle,
            _playbackArchive is null
                ? "No recording is open."
                : magnified
                    ? "Off: the participant's view, the part of the screen Magnifier's full screen view showed, " +
                      "in the colors it showed. On: the whole screen as captured, with that part outlined."
                    : "Unavailable: no frame of this recording was magnified or had its colors changed by " +
                      "Magnifier's full screen view, so each frame is the screen as the participant saw it.");
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
        var colors = ColorEffect.IsIdentity(frame.ColorEffect)
            ? string.Empty
            : ColorEffect.IsInversion(frame.ColorEffect) ? ", colors inverted" : ", with a color effect";
        if (MagnifiedView.VisibleRegion(frame) is not { } region)
        {
            if (colors.Length == 0)
            {
                view = string.Empty;
                return bitmap;
            }

            // Not magnified, but colors changed: the whole frame is the part
            // seen.
            if (_showWholeScreen)
            {
                view = $", whole screen as captured; the participant saw it{colors}";
                return bitmap;
            }

            view = $", participant's view{colors}";
            return WithColorEffect(frame.ColorEffect!, bitmap, new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
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
            view = $", whole screen as captured, with the participant's view at {percent} percent magnification outlined{colors}";
        }
        else
        {
            // Black where the part seen extends beyond the captured screen.
            group.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(seen)));
            if (colors.Length == 0)
            {
                group.Children.Add(new ImageDrawing(bitmap, whole));
            }
            else
            {
                // The effect is applied to the captured pixels of the part
                // seen only, drawn where they lie in the frame.
                var pixels = PixelBounds(seen, bitmap);
                if (pixels.Width > 0 && pixels.Height > 0)
                {
                    group.Children.Add(new ImageDrawing(
                        WithColorEffect(frame.ColorEffect!, bitmap, pixels),
                        new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height)));
                }
            }

            group.ClipGeometry = new RectangleGeometry(seen);
            view = $", participant's view at {percent} percent magnification, offset {magnification.X}, {magnification.Y}{colors}";
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    // The whole pixels of the bitmap that cover a rectangle of it.
    private static Int32Rect PixelBounds(Rect rect, BitmapSource bitmap)
    {
        var left = Math.Clamp((int)Math.Floor(rect.X), 0, bitmap.PixelWidth);
        var top = Math.Clamp((int)Math.Floor(rect.Y), 0, bitmap.PixelHeight);
        var right = Math.Clamp((int)Math.Ceiling(rect.Right), 0, bitmap.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom), 0, bitmap.PixelHeight);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    // The pixels of part of the bitmap with the color effect applied, as
    // Windows Magnifier applied it to the screen. The frame is not changed.
    private static BitmapSource WithColorEffect(FullscreenColorEffect effect, BitmapSource bitmap, Int32Rect part)
    {
        var source = bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = part.Width * 4;
        var pixels = new byte[stride * part.Height];
        source.CopyPixels(part, pixels, stride, 0);
        ColorEffect.ApplyBgra(effect, pixels);
        var result = BitmapSource.Create(
            part.Width, part.Height, bitmap.DpiX, bitmap.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
