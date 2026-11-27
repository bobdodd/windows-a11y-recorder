using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Recorder.Collectors.Android;

namespace Recorder.App;

internal static class AndroidInteractionReportDocument
{
    private const double MaximumImageWidth = 560;
    private const double MaximumImageHeight = 320;
    private static readonly Pen Outer = Stroke(Brushes.White, 5);
    private static readonly Pen Edge = Stroke(Brushes.Black, 3.5);
    private static readonly Pen Center = Stroke(Brushes.Yellow, 1.5);

    public static FlowDocument Create(AndroidInvestigationReport report)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0) };
        if (report.Tree is { } tree)
        {
            Line(document, $"Current viewport: Android tree {tree}; " +
                (report.Frame is { } frame ? $"recorded picture {frame}." : "no mapped recorded picture."));
        }
        if (!string.IsNullOrWhiteSpace(report.Coverage))
        {
            Line(document, report.Coverage);
        }
        Line(document, "TalkBack Explore by Touch: " + report.TalkBack);
        Line(document, "Keyboard Tab: " + report.Tab);
        Line(document, "Direct touch: " + report.DirectTouch);
        if (report.Restoration is { } warning)
        {
            Line(document, warning);
        }
        Line(document, "Outlines mark the target location, not focus or activation. An Android declaration is not proof of activation. " +
            "A local picture change is separate from an Android click. Not found or not tested does not mean inaccessible. " +
            "This is one viewport; off-screen and gesture-only targets may be missed.", top: 8, bottom: 8);
        if (report.Findings.Count == 0)
        {
            Line(document, "No candidates were reported.");
            return document;
        }

        // All starting-screen close-ups share one decoded picture.
        var startingPicture = report.StartingPicture is { } start ? Bitmap(start) : null;
        var taps = new Dictionary<AndroidInvestigationTapPictures, (BitmapSource Before, BitmapSource After)>(ReferenceEqualityComparer.Instance);
        var number = 0;
        foreach (var finding in report.Findings)
        {
            var title = $"{++number}. {finding.Item.Label}";
            var heading = Line(document, title, top: 13, bottom: 4);
            heading.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
            var declared = finding.Item.NodeIndex is { } index ? $" (tree item {index})" : "";
            Line(document, $"Discovery: {finding.Item.Evidence}{declared}.");
            Line(document, "TalkBack touch: " + string.Join(" ", finding.ExploreByTouch));
            Line(document, "Tab: " + string.Join(" ", finding.Tab));
            Line(document, "Direct touch: " + string.Join(" ", finding.DirectTouch));
            if (finding.StartingLocation is { } location && startingPicture is not null)
            {
                Picture(document, title, $"Starting screen, frame {report.StartingPicture!.Sequence}", startingPicture, location, alreadyCropped: false);
            }
            else
            {
                Line(document, "Starting-screen close-up unavailable for this item.", top: 4);
            }
            foreach (var tap in finding.TapPictures)
            {
                if (!taps.TryGetValue(tap, out var pictures))
                {
                    pictures = (Bitmap(tap.Before), Bitmap(tap.After));
                    taps.Add(tap, pictures);
                }
                Picture(document, title, $"Before direct tap, frame {tap.Before.Sequence}", pictures.Before, tap.Crop, alreadyCropped: true);
                Picture(document, title, $"After direct tap, frame {tap.After.Sequence}", pictures.After, tap.Crop, alreadyCropped: true);
            }
        }
        return document;
    }

    private static void Picture(FlowDocument document, string title, string caption, BitmapSource bitmap,
        AndroidInvestigationCrop crop, bool alreadyCropped)
    {
        var locations = string.Join("; ", crop.Outlines.Select(box => $"({box.Left}, {box.Top}) to ({box.Right}, {box.Bottom})"));
        Line(document, $"{caption}. Outlined phone bounds: {locations}.", top: 5);
        var image = new Image
        {
            Source = Outline(bitmap, crop, alreadyCropped), Stretch = Stretch.Uniform, Focusable = false,
            StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(image, $"{title}. {caption}. Highlighted target bounds: {locations}.");
        document.Blocks.Add(new BlockUIContainer(image) { Margin = new Thickness(0, 1, 0, 5) });
    }

    private static DrawingImage Outline(BitmapSource bitmap, AndroidInvestigationCrop crop, bool alreadyCropped)
    {
        var region = crop.Region;
        var width = region.Right - region.Left;
        var height = region.Bottom - region.Top;
        var scale = Math.Min(MaximumImageWidth / width, MaximumImageHeight / height);
        // Enlarge small phone crops for legibility without enlarging the surrounding document.
        scale = Math.Min(2, scale);
        var size = new Rect(0, 0, width * scale, height * scale);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(size) };
        using (var drawing = group.Open())
        {
            drawing.DrawImage(bitmap, alreadyCropped ? size :
                new Rect(-region.Left * scale, -region.Top * scale, bitmap.PixelWidth * scale, bitmap.PixelHeight * scale));
            foreach (var box in crop.Outlines)
            {
                var outline = new Rect((box.Left - region.Left) * scale, (box.Top - region.Top) * scale,
                    (box.Right - box.Left) * scale, (box.Bottom - box.Top) * scale);
                // Keep a line visible even if the crop meets the phone or app edge.
                var inside = size;
                inside.Inflate(-Math.Min(2.5, size.Width / 4), -Math.Min(2.5, size.Height / 4));
                outline.Intersect(inside);
                if (outline.IsEmpty)
                {
                    continue;
                }
                drawing.DrawRectangle(null, Outer, outline);
                drawing.DrawRectangle(null, Edge, outline);
                drawing.DrawRectangle(null, Center, outline);
            }
        }
        group.Freeze();
        var result = new DrawingImage(group);
        result.Freeze();
        return result;
    }

    private static Paragraph Line(FlowDocument document, string text, int top = 0, int bottom = 2)
    {
        var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(0, top, 0, bottom) };
        document.Blocks.Add(paragraph);
        return paragraph;
    }

    private static BitmapSource Bitmap(AndroidLivePreviewFrame frame)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Rgb24, null,
            frame.Rgb.ToArray(), checked(frame.Width * 3));
        bitmap.Freeze();
        return bitmap;
    }

    private static Pen Stroke(Brush brush, double width)
    {
        var pen = new Pen(brush, width);
        pen.Freeze();
        return pen;
    }
}
