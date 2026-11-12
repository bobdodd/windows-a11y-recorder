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
// The circles are about a fingertip across on the phone's picture, and at
// least MinimumTouchRadius across on this PC's screen, so they stay
// visible when the picture is shown small (test 46, 2026-11-10).
public partial class MainWindow
{
    // In device-independent pixels on this PC's screen: a circle at least
    // 24 across, with rings and paths at least 2 thick.
    private const double MinimumTouchRadius = 12;
    private const double MinimumTouchThickness = 2;

    private static readonly Brush TouchFillBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)));

    // The chosen finding's outline (android-checks.md, step 3), in this
    // PC's pixels: the gap from the item, each black band and the cyan one.
    private const double FindingGap = 2;
    private const double FindingBlack = 2;
    private const double FindingCyan = 4;

    private static readonly Brush FindingOutlineBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)));

    // The frame as shown without touches, and the words its help text has.
    private ImageSource? _frameSource;
    private string _frameHelpText = string.Empty;
    private string _videoHelpText = string.Empty;

    // The touches, and the outline of the item with accessibility focus
    // (Android slice 7, step 7c), over the phone's picture.
    private void DisplayTouchesAt(long positionNanoseconds)
    {
        var archive = _playbackArchive;
        if (archive is null ||
            archive.FrameSource != SessionFrameSource.Phone ||
            _frameSource is null ||
            _displayedFrameIndex < 0)
        {
            ShowPlainFrame();
            return;
        }

        var touches = archive.PhoneTouches.Recorded &&
            archive.PhoneTouches.At(positionNanoseconds) is { Fingers.Count: > 0 } down
                ? down
                : null;
        var frame = archive.Frames[_displayedFrameIndex];
        var focus = FocusOutlineAt(archive, positionNanoseconds, frame.Width, frame.Height);
        var finding = FindingOutlineAt(archive, positionNanoseconds, frame.Width, frame.Height);
        if (touches is null && focus is null && finding is null)
        {
            ShowPlainFrame();
            return;
        }

        // How many of this PC's device-independent pixels one of the
        // frame's pixels is shown as; 1 before the picture is laid out.
        var shown = VideoImage.ActualWidth > 0 && frame.Width > 0
            ? VideoImage.ActualWidth / frame.Width
            : 1;
        VideoImage.Source = WithTouches(_frameSource, frame.Width, frame.Height, shown, touches, focus?.Bounds, finding?.Bounds);
        SetVideoHelpText(string.Join(", ", new[]
        {
            touches?.Describe(),
            focus is null ? null : $"accessibility focus on {focus.Value.Label}",
            finding is null ? null : $"the chosen finding outlined on {finding.Value.Label}"
        }.Where(text => !string.IsNullOrEmpty(text))));
    }

    // The chosen finding's item at a time, in the frame's pixels, while the
    // phone's tree shows the items it was checked on: from the time the
    // playhead is moved to it until its window's items are taken again.
    private (Rect Bounds, string Label)? FindingOutlineAt(SessionPlaybackArchive archive, long time, int width, int height)
    {
        if (_shownFinding is not { } finding || _shownFindingTime >= finding.Times.Count)
        {
            return null;
        }

        var seen = finding.Times[_shownFindingTime];
        var phone = archive.PhoneAccessibility;
        if (phone.TakenAt(time, seen.WindowId)?.EventId != seen.TakenBy.EventId)
        {
            return null;
        }

        return ScaledToFrame(phone, time, seen.Bounds, width, height) is { } rect ? (rect, finding.Label) : null;
    }

    // Bounds in the pixels of the phone's screen as it was turned, scaled to
    // the frame's; null when empty, or when the frame and the screen are
    // turned differently, as for the moment after the phone is turned.
    private static Rect? ScaledToFrame(PhoneAccessibilityTimeline phone, long time, PhoneBounds bounds, int width, int height)
    {
        if (bounds.IsEmpty || width <= 0 || height <= 0)
        {
            return null;
        }

        double scaleX = 1;
        double scaleY = 1;
        if (phone.ScreenAt(time) is { } screen)
        {
            if (width > height != screen.Width > screen.Height)
            {
                return null;
            }

            scaleX = (double)width / screen.Width;
            scaleY = (double)height / screen.Height;
        }

        var rect = new Rect(
            bounds.Left * scaleX,
            bounds.Top * scaleY,
            bounds.Width * scaleX,
            bounds.Height * scaleY);
        rect.Intersect(new Rect(0, 0, width, height));
        return rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0 ? null : rect;
    }

    private void ShowPlainFrame()
    {
        if (!ReferenceEquals(VideoImage.Source, _frameSource))
        {
            VideoImage.Source = _frameSource;
        }

        SetVideoHelpText(string.Empty);
    }

    // The item with accessibility focus at a time, and its bounds in the
    // frame's pixels: its bounds in the latest snapshot that holds it after
    // it was given the focus, as when its list has scrolled since, or else
    // its bounds when it was given the focus. The bounds are in the pixels
    // of the phone's screen as it was turned, and are scaled to the frame's;
    // null when no item has the focus, or when the frame and the screen are
    // turned differently, as for the moment after the phone is turned.
    private static (Rect Bounds, string Label)? FocusOutlineAt(SessionPlaybackArchive archive, long time, int width, int height)
    {
        var phone = archive.PhoneAccessibility;
        if (!phone.FocusRecorded || phone.FocusAt(time) is not { } focus || width <= 0 || height <= 0)
        {
            return null;
        }

        var tree = phone.TreesRecorded ? phone.TreeAt(time, archive.RecordSource) : null;
        var bounds = tree is { FocusKey: not null, FocusBounds: { } newer } ? newer : focus.Item.Bounds;
        return ScaledToFrame(phone, time, bounds, width, height) is { } rect ? (rect, focus.Item.Label) : null;
    }

    // The circles' least size is on this PC's screen, so they are drawn
    // again when the picture's shown size changes.
    private void VideoImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_playbackArchive is { } archive &&
            (archive.PhoneTouches.Recorded || archive.PhoneAccessibility.FocusRecorded || _shownFinding is not null))
        {
            DisplayTouchesAt(_playbackPositionNanoseconds);
        }
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
    // and are scaled to the frame's. The outline of the item with
    // accessibility focus, and of the chosen finding's item, already in the
    // frame's pixels, are drawn under the fingers: the focus's in yellow over
    // black on its bounds, the finding's as a thick cyan band between black
    // ones, set back from the item (AddFindingOutline).
    private static ImageSource WithTouches(ImageSource frameImage, int width, int height, double shown, PhoneTouches? touches, Rect? focus, Rect? finding)
    {
        var whole = new Rect(0, 0, width, height);
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        group.Children.Add(new ImageDrawing(frameImage, whole));

        // About a fingertip on a phone, 80 pixels across on a screen 1080 wide,
        // and no smaller on this PC's screen than the least sizes.
        var shortSide = Math.Min(width, height);
        var radius = Math.Max(shortSide / 27.0, MinimumTouchRadius / shown);
        var thickness = Math.Max(shortSide / 240.0, MinimumTouchThickness / shown);
        var dark = Frozen(new Pen(Brushes.Black, thickness * 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var light = Frozen(new Pen(OutlineBrush, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var fingers = touches?.ScaledTo(width, height) ?? [];

        // A yellow rectangle over a black one, as the circles are drawn.
        if (focus is { } outline)
        {
            var box = new RectangleGeometry(outline);
            box.Freeze();
            group.Children.Add(new GeometryDrawing(null, dark, box));
            group.Children.Add(new GeometryDrawing(null, light, box));
        }

        if (finding is { } findingOutline)
        {
            AddFindingOutline(group, findingOutline, whole, shown);
        }

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

    // The chosen finding's outline, in this PC's pixels: a gap of 2 between
    // the item and the outline, so the item's own edge shows, then black 2,
    // cyan 4 and black 2, so the cyan stands out from light and dark
    // content (owner, 2026-11-12: "thick and 2 color to make it stand out
    // from the background, and 1-2 pixels pushed back from the item"). A
    // side that would leave the picture, as for an item as wide as the
    // screen, is drawn just inside the picture's edge instead.
    private static void AddFindingOutline(DrawingGroup group, Rect item, Rect whole, double shown)
    {
        var scale = shown > 0 ? 1 / shown : 1;
        var gap = FindingGap * scale;
        var band = (FindingBlack * 2 + FindingCyan) * scale;
        var outer = Rect.Inflate(item, gap + band, gap + band);
        outer.Intersect(whole);
        if (outer.IsEmpty || outer.Width <= band || outer.Height <= band)
        {
            return;
        }

        // Both strokes are centred on the band's middle line.
        var middle = Rect.Inflate(outer, -band / 2, -band / 2);
        var box = new RectangleGeometry(middle);
        box.Freeze();
        group.Children.Add(new GeometryDrawing(null, Frozen(new Pen(Brushes.Black, band) { LineJoin = PenLineJoin.Miter }), box));
        group.Children.Add(new GeometryDrawing(null, Frozen(new Pen(FindingOutlineBrush, FindingCyan * scale) { LineJoin = PenLineJoin.Miter }), box));
    }
}
