using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.App;

internal static class AndroidInteractionReportDocument
{
    private const double MaximumImageWidth = 560;
    private static readonly Pen Outer = Stroke(Brushes.White, 5);
    private static readonly Pen Edge = Stroke(Brushes.Black, 3.5);
    private static readonly Pen Center = Stroke(Brushes.Yellow, 1.5);
    private static readonly Pen TreeEdge = Stroke(Brushes.Black, 4, dashed: true);
    private static readonly Pen TreeCenter = Stroke(Brushes.Cyan, 2, dashed: true);

    public static FlowDocument Create(AndroidModelDiscoveryReport report)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0) };
        if (report.Model is { } model)
        {
            Line(document, $"Complex vision model: {model}.");
        }
        Line(document, "Model proposals and Android declarations are not proof of interaction. Focus is not activation. Proposals that share an Android node are not verified as separate controls. Untested or unverified does not mean unreachable.", bottom: 8);
        Line(document, report.Tree?.Summary ?? "Android tree matching not run. There was no usable model proposal list.", bottom: 8);
        var observed = new Dictionary<int, AndroidInvestigationFinding>();
        if (report.Input is { } tested)
        {
            Line(document, $"Input testing: {tested.Status}");
            Line(document, $"TalkBack swipes: {tested.Swipe}");
            Line(document, $"TalkBack Explore by Touch: {tested.TalkBack}");
            Line(document, $"Keyboard Tab: {tested.Tab}");
            Line(document, $"Direct touch: {tested.DirectTouch}");
            if (tested.Restoration is { } restoration)
            {
                Line(document, restoration);
            }
            foreach (var finding in tested.Findings)
            {
                if (finding.ProposalIndex is { } index)
                {
                    observed[index] = finding;
                }
            }
        }
        else
        {
            Line(document, report.InputUnavailable ?? "No input was tested.");
        }
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
        Picture(document, input, bitmap, full, null, null, maxHeight: 500);

        if (report.Proposals is not { Count: > 0 } proposals)
        {
            return document;
        }
        Line(document, "Solid outlines show model proposals. Dashed outlines show Android tree bounds for clear matches. Overlapping and repeated proposals are shown as returned.",
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
            AndroidNodeBounds? treeOutline = null;
            var match = report.Tree?.Matches is { } matches && index < matches.Count ? matches[index] : null;
            var alt = "";
            if (match is { Kind: AndroidProposalTreeMatchKind.Matched })
            {
                var candidate = match.Candidates[0];
                Line(document, $"Android tree match: {Describe(candidate)}", bottom: 4);
                treeOutline = Scale(candidate.Bounds, report.ScreenSize, full);
                alt = " Solid model box and dashed Android tree box.";
            }
            else if (match is { Kind: AndroidProposalTreeMatchKind.Ambiguous })
            {
                var shown = match.PlausibleCount > match.Candidates.Count ? $" Showing {match.Candidates.Count}." : "";
                Line(document, $"Android tree match ambiguous: {match.PlausibleCount} plausible candidates.{shown}");
                foreach (var candidate in match.Candidates)
                {
                    Line(document, Describe(candidate));
                }
            }
            else if (match is not null)
            {
                Line(document, "No clear match in the captured app tree.", bottom: 4);
            }
            observed.TryGetValue(index, out var finding);
            Line(document, $"TalkBack swipes: {Evidence(finding?.Swipe)}");
            Line(document, $"TalkBack Explore by Touch: {Evidence(finding?.ExploreByTouch)}");
            Line(document, $"Keyboard Tab: {Evidence(finding?.Tab)}");
            Line(document, $"Direct touch: {Evidence(finding?.DirectTouch)}", bottom: 4);
            var tight = treeOutline is { } treeBox
                ? new AndroidNodeBounds(Math.Min(outline.Left, treeBox.Left), Math.Min(outline.Top, treeBox.Top),
                    Math.Max(outline.Right, treeBox.Right), Math.Max(outline.Bottom, treeBox.Bottom))
                : outline;
            var pad = Math.Clamp(Math.Min(full.Right, full.Bottom) / 22, 8, 48);
            var crop = new AndroidNodeBounds(Math.Max(0, tight.Left - pad), Math.Max(0, tight.Top - pad),
                Math.Min(full.Right, tight.Right + pad), Math.Min(full.Bottom, tight.Bottom + pad));
            Picture(document, $"{title}. {caption}{alt}", bitmap, crop, outline, treeOutline, maxHeight: 320);
            if (finding is not null)
            {
                foreach (var pair in finding.TapPictures)
                {
                    TapCrop(document, pair.Before, pair.Crop, "Before tap");
                    TapCrop(document, pair.After, pair.Crop, "After tap");
                }
            }
        }
        return document;
    }

    private static string Evidence(IReadOnlyList<string>? evidence) =>
        evidence is { Count: > 0 } ? string.Join(" ", evidence) : "Not tested for this proposal.";

    private static void TapCrop(FlowDocument document, AndroidLivePreviewFrame frame, AndroidInvestigationCrop crop, string caption)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Rgb24, null,
            frame.Rgb.ToArray(), frame.Width * 3);
        var entire = new AndroidNodeBounds(0, 0, frame.Width, frame.Height);
        var region = crop.Region;
        var box = crop.Outlines[0];
        var outline = Scale(new(box.Left - region.Left, box.Top - region.Top, box.Right - region.Left,
            box.Bottom - region.Top), (region.Right - region.Left, region.Bottom - region.Top), entire);
        var name = $"{caption}: recording frame {frame.Sequence}, with the sampled region outlined.";
        Line(document, name, top: 4);
        Picture(document, name, bitmap, entire, outline, null, maxHeight: 240);
    }

    private static string Describe(AndroidProposalTreeCandidate candidate)
    {
        var item = candidate.Item;
        var role = string.IsNullOrWhiteSpace(item.Role) ? "Android node" : item.Role;
        var enabled = item.Enabled switch { true => "reported enabled", false => "reported disabled", null => "enabled state not reported" };
        var declaration = string.IsNullOrEmpty(item.Evidence) ? "no control action declared" : $"declared: {item.Evidence}";
        var box = candidate.Bounds;
        return $"{item.Label} ({role}, node {item.NodeIndex}), {enabled}; {declaration}. " +
            $"On-screen Android bounds: [{box.Left}, {box.Top}, {box.Right}, {box.Bottom}].";
    }

    private static AndroidNodeBounds Scale(AndroidNodeBounds bounds, (int Width, int Height) source, AndroidNodeBounds target) =>
        new((int)((long)bounds.Left * target.Right / source.Width), (int)((long)bounds.Top * target.Bottom / source.Height),
            (int)(((long)bounds.Right * target.Right + source.Width - 1) / source.Width),
            (int)(((long)bounds.Bottom * target.Bottom + source.Height - 1) / source.Height));

    private static void Picture(FlowDocument document, string caption, BitmapSource bitmap,
        AndroidNodeBounds region, AndroidNodeBounds? box, AndroidNodeBounds? treeBox, double maxHeight)
    {
        var image = new Image
        {
            Source = Draw(bitmap, region, box, treeBox, maxHeight), Stretch = Stretch.Uniform, Focusable = false,
            StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(image, caption);
        document.Blocks.Add(new BlockUIContainer(image) { Margin = new Thickness(0, 1, 0, 5) });
    }

    private static DrawingImage Draw(BitmapSource bitmap, AndroidNodeBounds region,
        AndroidNodeBounds? box, AndroidNodeBounds? treeBox, double maxHeight)
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
            Rect InCrop(AndroidNodeBounds bounds) => new((bounds.Left - region.Left) * scale, (bounds.Top - region.Top) * scale,
                (bounds.Right - bounds.Left) * scale, (bounds.Bottom - bounds.Top) * scale);
            if (box is not null)
            {
                var outline = InCrop(box);
                // Clip the stroke to the image, not the box, so small edge proposals stay visible.
                drawing.DrawRectangle(null, Outer, outline);
                drawing.DrawRectangle(null, Edge, outline);
                drawing.DrawRectangle(null, Center, outline);
            }
            if (treeBox is not null)
            {
                var outline = InCrop(treeBox);
                drawing.DrawRectangle(null, TreeEdge, outline);
                drawing.DrawRectangle(null, TreeCenter, outline);
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

    private static Pen Stroke(Brush brush, double width, bool dashed = false)
    {
        var pen = new Pen(brush, width);
        if (dashed)
        {
            // Dash distances in WPF are multiples of the pen thickness.
            pen.DashStyle = new DashStyle([6 / width, 5 / width], 0);
        }
        pen.Freeze();
        return pen;
    }
}
