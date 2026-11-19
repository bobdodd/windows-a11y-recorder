using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// An outline seen on a picture round the item with accessibility focus,
/// as a screen reader shows its focus: the item, the outline's colour, and
/// its thickness in the screen's pixels, inside the item's bounds.
/// </summary>
public sealed record PhoneFocusOutline(PhoneCheckItem Item, uint Colour, int Thickness)
{
    /// <summary>Whether a box crosses the outline: it meets the item's bounds and is not wholly inside the outline.</summary>
    public bool Crosses(PhoneBounds box)
    {
        ArgumentNullException.ThrowIfNull(box);
        var outer = Item.Bounds;
        if (!PhoneCover.Meet(box, outer))
        {
            return false;
        }

        var inner = new PhoneBounds(outer.Left + Thickness, outer.Top + Thickness, outer.Right - Thickness, outer.Bottom - Thickness);
        return inner.IsEmpty ||
            box.Left < inner.Left || box.Top < inner.Top || box.Right > inner.Right || box.Bottom > inner.Bottom;
    }
}

/// <summary>A window drawn over the app's window, as a snapshot lists it.</summary>
public sealed record PhoneWindowAbove(int Id, string? TypeName, string? Title, PhoneBounds Bounds);

/// <summary>
/// What is drawn over the app where the contrast checks measure (owner,
/// 2026-11-19, from the second recording of the fixture tour): the
/// outline round the item with accessibility focus, and other windows,
/// such as the accessibility button. Text or a control they cover is not
/// measured, and the finding says why, as the colours measured there are
/// partly theirs, not the app's.
/// </summary>
public static class PhoneCover
{
    // An outline is at least this thick, in the screen's pixels: thinner,
    // it may be the item's own border. The fixture tour's outline was 10
    // pixels, about 4 dp at 390 dpi (measured, 2026-11-19).
    internal const int LeastThickness = 4;

    // Colours within this distance, summed over red, green and blue, are
    // one colour of the outline, for its anti-aliased and blended edges.
    private const int Tolerance = 48;

    /// <summary>
    /// The outline round the window's item with accessibility focus, when
    /// one is seen on the picture: at the middle of each side, a run of one
    /// colour from the item's edge inward, at least
    /// <see cref="LeastThickness"/> pixels and under a quarter of the
    /// item's size, the same colour on every side, and not the colour just
    /// outside the item. Null when no item has accessibility focus or no
    /// such outline is seen round it.
    /// </summary>
    public static PhoneFocusOutline? OutlineOf(PhoneCheckWindow window, PhonePixels pixels, PictureScale scale)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(pixels);
        var item = window.Items.FirstOrDefault(entry => entry.Node.AccessibilityFocused && !entry.Bounds.IsEmpty);
        if (item is null)
        {
            return null;
        }

        var box = scale.ToPicture(item.Bounds);
        if (box.Left < 0 || box.Top < 0 || box.Right > pixels.Width || box.Bottom > pixels.Height || box.Width < 8 || box.Height < 8)
        {
            return null;
        }

        var middleX = (box.Left + box.Right) / 2;
        var middleY = (box.Top + box.Bottom) / 2;
        var limitX = box.Width / 4;
        var limitY = box.Height / 4;
        (int X, int Y, int StepX, int StepY, int Limit)[] sides =
        [
            (middleX, box.Top, 0, 1, limitY),
            (middleX, box.Bottom - 1, 0, -1, limitY),
            (box.Left, middleY, 1, 0, limitX),
            (box.Right - 1, middleY, -1, 0, limitX),
        ];
        var colour = pixels.At(sides[0].X, sides[0].Y);
        var thickest = 0;
        foreach (var (x, y, stepX, stepY, limit) in sides)
        {
            if (!Near(pixels.At(x, y), colour))
            {
                return null;
            }

            var outsideX = x - stepX;
            var outsideY = y - stepY;
            if (outsideX >= 0 && outsideY >= 0 && outsideX < pixels.Width && outsideY < pixels.Height &&
                Near(pixels.At(outsideX, outsideY), colour))
            {
                return null;
            }

            var run = 0;
            while (run < limit && Near(pixels.At(x + stepX * run, y + stepY * run), colour))
            {
                run++;
            }

            if (run >= limit)
            {
                return null;
            }

            thickest = Math.Max(thickest, run);
        }

        var thickness = (int)Math.Ceiling(thickest / Math.Min(scale.X, scale.Y));
        return thickness < LeastThickness ? null : new PhoneFocusOutline(item, colour, thickness);
    }

    /// <summary>
    /// Why a box is not measured, or null when nothing is drawn over it:
    /// it crosses the focus outline, or meets a window above the app's.
    /// <paramref name="item"/> is the item the box measures, if any.
    /// </summary>
    public static string? Why(PhoneBounds box, PhoneCheckItem? item, PhoneFocusOutline? outline, IReadOnlyList<PhoneWindowAbove> above)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(above);
        if (outline is not null && outline.Crosses(box))
        {
            var round = ReferenceEquals(outline.Item, item) ? "round it" : $"round {outline.Item.Label}";
            return string.Create(CultureInfo.InvariantCulture,
                $"an outline, {PhoneColours.Hex(outline.Colour)}, {outline.Thickness} pixels thick, is drawn over it, {round}, the item with accessibility focus, as a screen reader shows its focus");
        }

        foreach (var window in above)
        {
            if (Meet(box, window.Bounds))
            {
                var name = window.Title is { Length: > 0 } title ? $"'{title}'" : "untitled";
                var type = window.TypeName is { Length: > 0 } typeName ? $", {typeName}" : string.Empty;
                return $"another window, {name}{type}, is drawn over part of it";
            }
        }

        return null;
    }

    /// <summary>Whether two boxes share any pixel.</summary>
    public static bool Meet(PhoneBounds first, PhoneBounds second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return first.Left < second.Right && second.Left < first.Right && first.Top < second.Bottom && second.Top < first.Bottom;
    }

    private static bool Near(uint first, uint second)
    {
        var red = Math.Abs((int)((first >> 16) & 0xFF) - (int)((second >> 16) & 0xFF));
        var green = Math.Abs((int)((first >> 8) & 0xFF) - (int)((second >> 8) & 0xFF));
        var blue = Math.Abs((int)(first & 0xFF) - (int)(second & 0xFF));
        return red + green + blue <= Tolerance;
    }
}
