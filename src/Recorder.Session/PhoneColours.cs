using System.Globalization;

namespace Recorder.Session;

/// <summary>A picture's pixels, each 0xRRGGBB, row by row from the top.</summary>
public sealed class PhonePixels
{
    public PhonePixels(int width, int height, uint[] rgb)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgb.LongLength != (long)width * height)
        {
            throw new ArgumentException("There is not one colour for each pixel.", nameof(rgb));
        }

        Width = width;
        Height = height;
        Rgb = rgb;
    }

    public int Width { get; }

    public int Height { get; }

    internal uint[] Rgb { get; }

    public uint At(int x, int y) => Rgb[(long)y * Width + x];
}

/// <summary>
/// The colours of a box of a picture as ATF's colour swatch separates them
/// with one text colour (<c>ContrastSwatch</c>,
/// <c>separateColorsUsingSingleForegroundMethod</c>, at commit
/// <c>c65cab0</c>): the background, the text's colour, and their ratio,
/// rounded to two places as ATF rounds it. <paramref name="Colours"/> is how
/// many exact colours the box has, <paramref name="Joined"/> how many are
/// left once colours closer than the limit are joined.
/// </summary>
public sealed record PhoneSwatch(uint Background, uint Foreground, double Ratio, int Colours, int Joined)
{
    public string Text =>
        string.Create(CultureInfo.InvariantCulture, $"{Ratio:0.00}:1, {PhoneColours.Hex(Foreground)} on {PhoneColours.Hex(Background)}");
}

/// <summary>
/// Colour measures as ATF's <c>ContrastUtils</c> makes them (at commit
/// <c>c65cab0</c>), and its colour swatch, after near colours are joined
/// (android-checks.md, "Step 4: what the measurement found").
/// </summary>
public static class PhoneColours
{
    /// <summary>
    /// Colours closer than this are joined before the swatch: measured on
    /// test 16's recording, 122 of 125 items then had the level from the
    /// video that the phone's screenshot gave, and on the screenshots it
    /// changed no ratio (android-checks.md, "As built: step 4b").
    /// </summary>
    public const double JoinBelow = 3.0;

    /// <summary>The relative luminance, as WCAG and ATF's <c>calculateLuminance</c> give it.</summary>
    public static double Luminance(uint rgb) =>
        0.2126 * Linear((rgb >> 16) & 0xFF) + 0.7152 * Linear((rgb >> 8) & 0xFF) + 0.0722 * Linear(rgb & 0xFF);

    /// <summary>The contrast ratio of two luminances, unrounded.</summary>
    public static double Ratio(double first, double second) =>
        (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);

    /// <summary>The difference of two colours as ATF's <c>colorDifference</c> gives it, CIE94's Delta E.</summary>
    public static double Difference(uint first, uint second) => Difference(Lab(first), Lab(second));

    public static string Hex(uint rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>
    /// ATF's swatch, one text colour, on a box of a picture, clipped to the
    /// picture, after joining colours closer than <paramref name="joinBelow"/>
    /// (0 joins none, which is ATF as written); null for an empty box.
    /// </summary>
    public static PhoneSwatch? Measure(PhonePixels picture, PhoneBounds box, double joinBelow = JoinBelow)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(box);
        var left = Math.Clamp(box.Left, 0, picture.Width);
        var right = Math.Clamp(box.Right, 0, picture.Width);
        var top = Math.Clamp(box.Top, 0, picture.Height);
        var bottom = Math.Clamp(box.Bottom, 0, picture.Height);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        var counts = new Dictionary<uint, int>();
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var colour = picture.At(x, y) & 0xFFFFFF;
                counts[colour] = counts.TryGetValue(colour, out var count) ? count + 1 : 1;
            }
        }

        var colours = counts.Count;
        var histogram = joinBelow > 0 ? Join(counts, joinBelow) : counts;
        return Separate(histogram, colours);
    }

    // Joins each colour, most common first, into the first kept colour
    // closer than the limit, which keeps its own colour and takes the
    // other's count; ATF does this for its swatch with several text
    // colours (reduceColors, at 2.0), not for one text colour.
    private static Dictionary<uint, int> Join(Dictionary<uint, int> counts, double limit)
    {
        var kept = new List<(uint Colour, (double L, double A, double B) Lab, int Count)>();
        foreach (var (colour, count) in counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
        {
            var lab = Lab(colour);
            var joined = false;
            for (var index = 0; index < kept.Count; index++)
            {
                // Delta E is at least the difference in lightness, so a kept
                // colour that far in lightness is not near.
                if (Math.Abs(kept[index].Lab.L - lab.L) < limit && Difference(kept[index].Lab, lab) < limit)
                {
                    kept[index] = (kept[index].Colour, kept[index].Lab, kept[index].Count + count);
                    joined = true;
                    break;
                }
            }

            if (!joined)
            {
                kept.Add((colour, lab, count));
            }
        }

        return kept.ToDictionary(entry => entry.Colour, entry => entry.Count);
    }

    // separateColors with one text colour: the most common colour above the
    // average luminance of the colours, and the most common below it; the
    // more common of the two is the background. The colours are taken in
    // ATF's order, by value, so a tie goes the way it goes in ATF.
    private static PhoneSwatch Separate(Dictionary<uint, int> histogram, int colours)
    {
        if (histogram.Count == 1)
        {
            var only = histogram.Keys.First();
            return new PhoneSwatch(only, only, 1.0, colours, 1);
        }

        var ordered = histogram.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value, Luminance(pair.Key))).ToList();
        var average = ordered.Average(entry => entry.Item3);
        uint? low = null, high = null;
        int lowCount = 0, highCount = 0;
        foreach (var (colour, count, luminance) in ordered)
        {
            if (luminance < average && count > lowCount)
            {
                lowCount = count;
                low = colour;
            }
            else if (luminance >= average && count > highCount)
            {
                highCount = count;
                high = colour;
            }
        }

        var (background, foreground) = highCount > lowCount ? (high, low) : (low, high);
        var back = background ?? foreground ?? 0;
        var fore = foreground ?? back;
        var ratio = Math.Floor(Ratio(Luminance(back), Luminance(fore)) * 100.0 + 0.5) / 100.0;
        return new PhoneSwatch(back, fore, ratio, colours, histogram.Count);
    }

    private static double Linear(uint component)
    {
        var value = component / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    // rgb2lab, as ATF has it: the D65 white, its constants.
    private static (double L, double A, double B) Lab(uint rgb)
    {
        var r = Linear((rgb >> 16) & 0xFF);
        var g = Linear((rgb >> 8) & 0xFF);
        var b = Linear(rgb & 0xFF);
        var x = Part((r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047);
        var y = Part(r * 0.2126 + g * 0.7152 + b * 0.0722);
        var z = Part((r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883);
        return (116 * y - 16, 500 * (x - y), 200 * (y - z));

        static double Part(double value) => value > 0.008856 ? Math.Cbrt(value) : 7.787 * value + 16.0 / 116;
    }

    private static double Difference((double L, double A, double B) first, (double L, double A, double B) second)
    {
        var deltaL = first.L - second.L;
        var deltaA = first.A - second.A;
        var deltaB = first.B - second.B;
        var c1 = Math.Sqrt(first.A * first.A + first.B * first.B);
        var c2 = Math.Sqrt(second.A * second.A + second.B * second.B);
        var deltaC = c1 - c2;
        var deltaH = deltaA * deltaA + deltaB * deltaB - deltaC * deltaC;
        deltaH = deltaH < 0 ? 0 : Math.Sqrt(deltaH);
        var chroma = deltaC / (1.0 + 0.045 * c1);
        var hue = deltaH / (1.0 + 0.015 * c1);
        return Math.Sqrt(deltaL * deltaL + chroma * chroma + hue * hue);
    }
}
