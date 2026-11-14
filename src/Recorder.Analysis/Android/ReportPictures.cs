using Recorder.Analysis.Summary;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>A box of a picture, in its pixels, right and bottom exclusive.</summary>
internal readonly record struct PictureBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PictureBox Inflate(int by) => new(Left - by, Top - by, Right + by, Bottom + by);

    public PictureBox Clamp(int width, int height) =>
        new(Math.Clamp(Left, 0, width), Math.Clamp(Top, 0, height), Math.Clamp(Right, 0, width), Math.Clamp(Bottom, 0, height));
}

/// <summary>A picture being drawn on, each pixel 0xRRGGBB.</summary>
internal sealed class ReportCanvas
{
    public ReportCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Rgb = new uint[(long)width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public uint[] Rgb { get; }

    public uint At(int x, int y) => Rgb[(long)y * Width + x];

    public void Fill(PictureBox box, uint colour)
    {
        var clamped = box.Clamp(Width, Height);
        for (var y = clamped.Top; y < clamped.Bottom; y++)
        {
            Array.Fill(Rgb, colour, (int)((long)y * Width + clamped.Left), clamped.Width);
        }
    }

    /// <summary>The frame of a box, the given thickness inward from its edges.</summary>
    public void Frame(PictureBox box, int thickness, uint colour)
    {
        Fill(new PictureBox(box.Left, box.Top, box.Right, box.Top + thickness), colour);
        Fill(new PictureBox(box.Left, box.Bottom - thickness, box.Right, box.Bottom), colour);
        Fill(new PictureBox(box.Left, box.Top, box.Left + thickness, box.Bottom), colour);
        Fill(new PictureBox(box.Right - thickness, box.Top, box.Right, box.Bottom), colour);
    }
}

/// <summary>
/// The report's pictures (android-report.md, "Pictures"): a screen's
/// picture at half size with each finding's item outlined and numbered, and
/// a zoomed crop of a small item at full resolution. Drawn here, with no
/// Windows graphics, so the same picture gives the same file.
/// </summary>
internal static class ReportPictures
{
    /// <summary>The outline's colour, a bright cyan, between black bands so it shows on light and dark screens.</summary>
    public const uint Cyan = 0x00E5FF;

    /// <summary>
    /// The grey of the edge round each picture, so that a light screen can
    /// be told from the white page; 7 to 1 against white.
    /// </summary>
    public const uint Edge = 0x595959;

    /// <summary>
    /// The width of the edge, in pixels, outside the picture; wide enough to
    /// stay visible when Word shows the picture smaller than its pixels.
    /// </summary>
    public const int EdgeWidth = 4;

    public const uint Black = 0x000000;

    /// <summary>An item covering at least this share of the screen is not zoomed.</summary>
    public const double ZoomLimit = 0.25;

    /// <summary>An item covering at least this share of the screen is the whole screen, so is not outlined.</summary>
    public const double WholeScreen = 0.9;

    // Each digit 3 wide and 5 high, row by row, the top row first.
    private static readonly string[] Digits =
    [
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
    ];

    /// <summary>The unit of the outline's bands for a picture: one pixel for each 300 of its shorter side, at least one.</summary>
    public static int Unit(int width, int height) => Math.Max(1, (int)Math.Round(Math.Min(width, height) / 300.0));

    /// <summary>A picture scaled down by a whole number, each pixel the mean of the pixels it covers.</summary>
    public static ReportCanvas Scaled(PhonePixels pixels, int divisor)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        divisor = Math.Max(1, divisor);
        var canvas = new ReportCanvas(Math.Max(1, pixels.Width / divisor), Math.Max(1, pixels.Height / divisor));
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                long red = 0, green = 0, blue = 0, count = 0;
                for (var dy = 0; dy < divisor; dy++)
                {
                    for (var dx = 0; dx < divisor; dx++)
                    {
                        var sx = x * divisor + dx;
                        var sy = y * divisor + dy;
                        if (sx >= pixels.Width || sy >= pixels.Height)
                        {
                            continue;
                        }

                        var colour = pixels.At(sx, sy);
                        red += (colour >> 16) & 0xFF;
                        green += (colour >> 8) & 0xFF;
                        blue += colour & 0xFF;
                        count++;
                    }
                }

                canvas.Rgb[(long)y * canvas.Width + x] = count == 0 ? 0 :
                    ((uint)(red / count) << 16) | ((uint)(green / count) << 8) | (uint)(blue / count);
            }
        }

        return canvas;
    }

    /// <summary>A box of a picture, each pixel made a square of the given size.</summary>
    public static ReportCanvas Zoomed(PhonePixels pixels, PictureBox box, int scale)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        box = box.Clamp(pixels.Width, pixels.Height);
        scale = Math.Max(1, scale);
        var canvas = new ReportCanvas(Math.Max(1, box.Width * scale), Math.Max(1, box.Height * scale));
        for (var y = 0; y < canvas.Height; y++)
        {
            var sy = Math.Min(pixels.Height - 1, box.Top + y / scale);
            for (var x = 0; x < canvas.Width; x++)
            {
                var sx = Math.Min(pixels.Width - 1, box.Left + x / scale);
                canvas.Rgb[(long)y * canvas.Width + x] = pixels.At(sx, sy);
            }
        }

        return canvas;
    }

    /// <summary>The picture with a grey edge round it, outside its pixels.</summary>
    public static ReportCanvas Edged(ReportCanvas picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var edged = new ReportCanvas(picture.Width + 2 * EdgeWidth, picture.Height + 2 * EdgeWidth);
        Array.Fill(edged.Rgb, Edge);
        for (var y = 0; y < picture.Height; y++)
        {
            Array.Copy(picture.Rgb, y * picture.Width, edged.Rgb, ((y + EdgeWidth) * edged.Width) + EdgeWidth, picture.Width);
        }

        return edged;
    }

    /// <summary>
    /// The outline of an item: a gap of one unit, then black one unit, cyan
    /// two, and black one, outward from the item's bounds, kept inside the
    /// picture where the item is at its edge. Gives the outline's outer box.
    /// </summary>
    public static PictureBox Outline(ReportCanvas canvas, PictureBox item, int unit)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var outer = item.Inflate(5 * unit).Clamp(canvas.Width, canvas.Height);
        if (outer.Width < 8 * unit || outer.Height < 8 * unit)
        {
            outer = Grow(outer, 8 * unit, canvas.Width, canvas.Height);
        }

        canvas.Frame(outer, 4 * unit, Black);
        canvas.Frame(outer.Inflate(-unit), 2 * unit, Cyan);
        return outer;
    }

    /// <summary>
    /// A finding's number in black on cyan, edged in black, above the
    /// outline's top left corner, else below it, else inside it.
    /// </summary>
    public static PictureBox Badge(ReportCanvas canvas, int number, PictureBox outline, int unit)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var dot = 2 * unit;
        var width = text.Length * 3 * dot + (text.Length - 1) * dot + 2 * dot + 2 * unit;
        var height = 5 * dot + 2 * dot + 2 * unit;
        var top = outline.Top - height >= 0 ? outline.Top - height
            : outline.Bottom + height <= canvas.Height ? outline.Bottom
            : outline.Top;
        var left = Math.Clamp(outline.Left, 0, Math.Max(0, canvas.Width - width));
        var box = new PictureBox(left, top, left + width, top + height);
        canvas.Fill(box, Black);
        canvas.Fill(box.Inflate(-unit), Cyan);
        var x = left + unit + dot;
        foreach (var character in text)
        {
            var pattern = Digits[character - '0'];
            for (var row = 0; row < 5; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    if (pattern[row * 3 + column] == '1')
                    {
                        var px = x + column * dot;
                        var py = top + unit + dot + row * dot;
                        canvas.Fill(new PictureBox(px, py, px + dot, py + dot), Black);
                    }
                }
            }

            x += 4 * dot;
        }

        return box;
    }

    /// <summary>A canvas as a PNG file's bytes, opaque, 8 bits a sample.</summary>
    public static byte[] Png(ReportCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var rows = new List<byte[]>(canvas.Height);
        for (var y = 0; y < canvas.Height; y++)
        {
            var row = new byte[canvas.Width * 4];
            for (var x = 0; x < canvas.Width; x++)
            {
                var colour = canvas.At(x, y);
                row[x * 4] = (byte)(colour >> 16);
                row[x * 4 + 1] = (byte)(colour >> 8);
                row[x * 4 + 2] = (byte)colour;
                row[x * 4 + 3] = 255;
            }

            rows.Add(row);
        }

        return FrameCut.Encode(canvas.Width, canvas.Height, wide: false, rows, new Dictionary<string, byte[]>());
    }

    /// <summary>
    /// The crop of a picture for a zoomed view of an item: the item with a
    /// margin of half its size, at least 24 pixels, and the scale, at most 4,
    /// that makes it about 720 pixels wide and at most 1200 high.
    /// </summary>
    public static (PictureBox Crop, int Scale) ZoomOf(PictureBox item, int width, int height)
    {
        var margin = Math.Max(24, Math.Max(item.Width, item.Height) / 2);
        var crop = item.Inflate(margin).Clamp(width, height);
        if (crop.IsEmpty)
        {
            return (crop, 1);
        }

        var scale = Math.Clamp(Math.Min(720 / crop.Width, 1200 / crop.Height), 1, 4);
        return (crop, scale);
    }

    // A box grown about its centre to at least a size, inside the picture.
    private static PictureBox Grow(PictureBox box, int size, int width, int height)
    {
        var w = Math.Min(width, Math.Max(box.Width, size));
        var h = Math.Min(height, Math.Max(box.Height, size));
        var left = Math.Clamp(box.Left + box.Width / 2 - w / 2, 0, Math.Max(0, width - w));
        var top = Math.Clamp(box.Top + box.Height / 2 - h / 2, 0, Math.Max(0, height - h));
        return new PictureBox(left, top, left + w, top + h);
    }
}
