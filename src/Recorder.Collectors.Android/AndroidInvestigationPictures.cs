using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public sealed record AndroidInvestigationCrop(AndroidNodeBounds Region, IReadOnlyList<AndroidNodeBounds> Outlines);

public sealed record AndroidInvestigationTapPictures(AndroidInvestigationCrop Crop,
    AndroidLivePreviewFrame Before, AndroidLivePreviewFrame After);

internal static class AndroidInvestigationPictures
{
    // A run takes at most 36 taps; even square pairs at this size use under 48 MiB in total.
    private const int TapSideLimit = 480;

    internal static bool CanUse(AndroidInteractionSnapshot snapshot) => snapshot.HasScreenCoordinates &&
        snapshot.Preview.State == AndroidLivePreviewState.ShowingFrames &&
        snapshot.Frame is { Width: > 0, Height: > 0 } frame &&
        frame.Rgb.Length % 3 == 0 && (long)frame.Width * frame.Height == frame.Rgb.Length / 3;

    internal static AndroidInvestigationCrop? Locate(AndroidInteractionSnapshot snapshot,
        IReadOnlyList<AndroidNodeBounds> boxes) => BuildCrop(snapshot, boxes, []);

    internal static AndroidInvestigationTapPictures? TapPair(AndroidInteractionSnapshot before,
        AndroidInteractionSnapshot after, AndroidNodeBounds box)
    {
        if (!CanUse(before) || !CanUse(after) || after.Preview.StreamSession != before.Preview.StreamSession ||
            after.App.Window.Id != before.App.Window.Id || after.PackageName != before.PackageName ||
            before.Display is not { } originalDisplay || after.Display is not { } laterDisplay ||
            originalDisplay.Rotation != laterDisplay.Rotation ||
            after.App.Window.Bounds != before.App.Window.Bounds ||
            (after.Frame!.Width, after.Frame.Height) != (before.Frame!.Width, before.Frame.Height) ||
            after.Frame.Sequence <= before.Frame.Sequence ||
            BuildCrop(before, [box], ProtectedAreas(after)) is not { } crop)
        {
            return null;
        }
        return new(crop, Pixels(before.Frame, crop.Region), Pixels(after.Frame, crop.Region));
    }

    private static AndroidInvestigationCrop? BuildCrop(AndroidInteractionSnapshot snapshot,
        IReadOnlyList<AndroidNodeBounds> boxes, IEnumerable<AndroidNodeBounds> alsoProtected)
    {
        if (!CanUse(snapshot) || snapshot.Frame is not { } frame ||
            AndroidInteractionInventory.Intersection(new(0, 0, frame.Width, frame.Height),
                snapshot.App.Window.Bounds) is not { } viewport)
        {
            return null;
        }
        var protectedAreas = ProtectedAreas(snapshot).Concat(alsoProtected).Distinct().ToArray();
        var outlines = boxes.Select(box => AndroidInteractionInventory.Intersection(viewport, box))
            .OfType<AndroidNodeBounds>().Distinct().ToArray();
        // Don't show a field Android marked as a password or draw through an overlapping window.
        bool Clear(AndroidNodeBounds region) => !protectedAreas.Any(area =>
            AndroidInteractionInventory.Intersection(region, area) is not null);
        if (outlines.Length == 0)
        {
            return null;
        }
        var tight = new AndroidNodeBounds(outlines.Min(box => box.Left), outlines.Min(box => box.Top),
            outlines.Max(box => box.Right), outlines.Max(box => box.Bottom));
        if (!Clear(tight))
        {
            return null;
        }
        var pad = Math.Clamp(Math.Min(frame.Width, frame.Height) / 22, 8, 48);
        var padded = new AndroidNodeBounds(Math.Max(viewport.Left, tight.Left - pad), Math.Max(viewport.Top, tight.Top - pad),
            (int)Math.Min(viewport.Right, (long)tight.Right + pad), (int)Math.Min(viewport.Bottom, (long)tight.Bottom + pad));
        return new(Clear(padded) ? padded : tight, outlines);
    }

    private static IEnumerable<AndroidNodeBounds> ProtectedAreas(AndroidInteractionSnapshot snapshot) =>
        snapshot.Occluders.Concat(snapshot.App.Items!.Nodes.Where(node => node.Password).Select(node => node.Bounds));

    private static AndroidLivePreviewFrame Pixels(AndroidLivePreviewFrame frame, AndroidNodeBounds region)
    {
        var width = region.Right - region.Left;
        var height = region.Bottom - region.Top;
        var longSide = Math.Max(width, height);
        var scaledWidth = longSide > TapSideLimit ? Math.Max(1, (int)((long)width * TapSideLimit / longSide)) : width;
        var scaledHeight = longSide > TapSideLimit ? Math.Max(1, (int)((long)height * TapSideLimit / longSide)) : height;
        var source = frame.Rgb.Span;
        var pixels = new byte[scaledWidth * scaledHeight * 3];
        for (var y = 0; y < scaledHeight; y++)
        {
            var row = (region.Top + y * (long)height / scaledHeight) * frame.Width * 3;
            if (scaledWidth == width)
            {
                source.Slice((int)(row + (long)region.Left * 3), width * 3).CopyTo(pixels.AsSpan(y * width * 3));
                continue;
            }
            for (var x = 0; x < scaledWidth; x++)
            {
                var offset = row + (region.Left + x * (long)width / scaledWidth) * 3;
                var destination = (y * scaledWidth + x) * 3;
                source.Slice((int)offset, 3).CopyTo(pixels.AsSpan(destination, 3));
            }
        }
        return new(frame.Sequence, scaledWidth, scaledHeight, pixels) { PhoneNanoseconds = frame.PhoneNanoseconds };
    }
}
