using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Recorder.Contracts;

namespace Recorder.App;

internal static class AndroidInteractionReportDocument
{
    private const double MaximumImageWidth = 560;
    private static readonly Pen Outer = Stroke(Brushes.White, 5);
    private static readonly Pen Edge = Stroke(Brushes.Black, 3.5);
    private static readonly Pen Center = Stroke(Brushes.Yellow, 1.5);

    public static FlowDocument Create(AndroidModelDiscoveryReport report)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0) };
        if (report.Model is { } model)
        {
            Line(document, $"Complex vision model: {model}.");
        }
        Line(document, "Model suggestions only. No tree matching or phone input was performed.", bottom: 8);
        if (report.InputPng is not { } png)
        {
            return document;
        }

        // Decode exactly the PNG submitted to the model, not a later live frame.
        using var stream = new MemoryStream(png, writable: false);
        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var full = new AndroidNodeBounds(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
        var input = $"Model input screenshot: recording frame {report.Frame}, {full.Right} by {full.Bottom} pixels.";
        Line(document, input, bottom: 4);
        Picture(document, input, bitmap, full, null, maxHeight: 500);

        if (report.Proposals is not { Count: > 0 } proposals)
        {
            return document;
        }
        Line(document, "Outlines show the model's proposed locations, not verified controls. Overlapping and repeated proposals are shown as returned.",
            top: 8, bottom: 8);
        for (var index = 0; index < proposals.Count; index++)
        {
            var proposal = proposals[index];
            var title = $"{index + 1}. {proposal.Label}";
            var heading = Line(document, title, top: 13, bottom: 4);
            heading.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
            var bounds = proposal.NormalizedBox;
            var caption = $"Model box (0 to 1000): [{bounds.Left}, {bounds.Top}, {bounds.Right}, {bounds.Bottom}].";
            Line(document, caption, bottom: 4);
            var outline = proposal.ImageBounds(full.Right, full.Bottom);
            var pad = Math.Clamp(Math.Min(full.Right, full.Bottom) / 22, 8, 48);
            var crop = new AndroidNodeBounds(Math.Max(0, outline.Left - pad), Math.Max(0, outline.Top - pad),
                Math.Min(full.Right, outline.Right + pad), Math.Min(full.Bottom, outline.Bottom + pad));
            Picture(document, $"{title}. {caption}", bitmap, crop, outline, maxHeight: 320);
        }
        return document;
    }

    private static void Picture(FlowDocument document, string caption, BitmapSource bitmap,
        AndroidNodeBounds region, AndroidNodeBounds? box, double maxHeight)
    {
        var image = new Image
        {
            Source = Draw(bitmap, region, box, maxHeight), Stretch = Stretch.Uniform, Focusable = false,
            StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(image, caption);
        document.Blocks.Add(new BlockUIContainer(image) { Margin = new Thickness(0, 1, 0, 5) });
    }

    private static DrawingImage Draw(BitmapSource bitmap, AndroidNodeBounds region, AndroidNodeBounds? box, double maxHeight)
    {
        var width = region.Right - region.Left;
        var height = region.Bottom - region.Top;
        var scale = Math.Min(box is null ? 1 : 2, Math.Min(MaximumImageWidth / width, maxHeight / height));
        var size = new Rect(0, 0, width * scale, height * scale);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(size) };
        using (var drawing = group.Open())
        {
            drawing.DrawImage(bitmap,
                new Rect(-region.Left * scale, -region.Top * scale, bitmap.PixelWidth * scale, bitmap.PixelHeight * scale));
            if (box is not null)
            {
                var outline = new Rect((box.Left - region.Left) * scale, (box.Top - region.Top) * scale,
                    (box.Right - box.Left) * scale, (box.Bottom - box.Top) * scale);
                // Clip the stroke to the image, not the box, so small edge proposals stay visible.
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

    private static Pen Stroke(Brush brush, double width)
    {
        var pen = new Pen(brush, width);
        pen.Freeze();
        return pen;
    }
}
