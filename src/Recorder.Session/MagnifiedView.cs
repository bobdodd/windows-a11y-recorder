namespace Recorder.Session;

/// <summary>A rectangle of a frame, in the frame's pixels from its upper-left corner.</summary>
public readonly record struct FrameRegion(double X, double Y, double Width, double Height);

/// <summary>
/// The part of a desktop frame a participant saw with Windows Magnifier's
/// full screen view on. See docs/architecture/magnified-view-playback.md.
/// </summary>
public static class MagnifiedView
{
    // A level this close to 1 is no magnification.
    private const double UnmagnifiedTolerance = 1e-6;

    /// <summary>Whether the frame was read with a level above 1.</summary>
    public static bool IsMagnified(SessionVideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Magnification is { } magnification &&
            magnification.Level > 1 + UnmagnifiedTolerance &&
            frame.Width > 0 &&
            frame.Height > 0;
    }

    /// <summary>Whether any frame was read with a level above 1.</summary>
    public static bool AnyMagnified(IEnumerable<SessionVideoFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        return frames.Any(IsMagnified);
    }

    /// <summary>
    /// The part of the frame the participant saw, drawn at the frame's size
    /// by Magnifier, or null when the frame shows the screen as seen: no
    /// reading, or a level of at most 1.
    /// </summary>
    /// <remarks>
    /// A point P of the virtual screen shows the unmagnified content at the
    /// offset plus P divided by the level. The frame's upper-left corner is
    /// the virtual screen's, V, so the part seen starts at the offset plus V
    /// divided by the level, less V in the frame's pixels, and is the
    /// frame's size divided by the level. With one monitor V is 0 and the
    /// part starts at the offset, as validated on 2026-10-07; with several
    /// it is the inference the design describes, not yet tested. The part
    /// may extend beyond the frame, where nothing was captured.
    /// </remarks>
    public static FrameRegion? VisibleRegion(SessionVideoFrame frame)
    {
        if (!IsMagnified(frame))
        {
            return null;
        }

        var magnification = frame.Magnification!;
        var level = magnification.Level;
        return new FrameRegion(
            magnification.X + (frame.X / level) - frame.X,
            magnification.Y + (frame.Y / level) - frame.Y,
            frame.Width / level,
            frame.Height / level);
    }
}

/// <summary>
/// Windows Magnifier's full screen color effect applied to pixels. A pixel's
/// red, green, blue, and alpha, from 0 to 1, and 1 form a row vector that is
/// multiplied by the 5 by 5 matrix, so row i holds what input channel i adds
/// to each output and the fifth row is the translation; each output is
/// clamped to 0 to 1. Microsoft does not state the order; it is the reading
/// its grayscale example requires. See
/// docs/architecture/magnified-view-playback.md, "Color effect".
/// </summary>
public static class ColorEffect
{
    // A value this close to the identity's, or the inversion's, is taken
    // as it.
    private const double Tolerance = 1e-6;

    private static readonly double[] Identity =
    [
        1, 0, 0, 0, 0,
        0, 1, 0, 0, 0,
        0, 0, 1, 0, 0,
        0, 0, 0, 1, 0,
        0, 0, 0, 0, 1
    ];

    // Each color channel becomes 1 less itself; alpha is kept.
    private static readonly double[] Inversion =
    [
        -1, 0, 0, 0, 0,
        0, -1, 0, 0, 0,
        0, 0, -1, 0, 0,
        0, 0, 0, 1, 0,
        1, 1, 1, 0, 1
    ];

    /// <summary>Whether the effect changes nothing: none, or the identity.</summary>
    public static bool IsIdentity(FullscreenColorEffect? effect) =>
        effect is null || Matches(effect, Identity);

    /// <summary>Whether the effect inverts every color channel and keeps alpha.</summary>
    public static bool IsInversion(FullscreenColorEffect? effect) =>
        effect is not null && Matches(effect, Inversion);

    /// <summary>
    /// Applies the effect to pixels in place, four bytes each in blue,
    /// green, red, alpha order, as a WPF Bgra32 or Pbgra32 bitmap holds
    /// them; premultiplied pixels must be fully opaque.
    /// </summary>
    public static void ApplyBgra(FullscreenColorEffect effect, Span<byte> pixels)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (pixels.Length % 4 != 0)
        {
            throw new ArgumentException("Pixels are four bytes each.", nameof(pixels));
        }

        if (IsIdentity(effect))
        {
            return;
        }

        var m = effect.Matrix;
        // Each output channel of a byte input is a sum of one term per
        // input channel and the translation, so the terms are tabled once.
        Span<float> table = new float[4 * 4 * 256];
        for (var input = 0; input < 4; input++)
        {
            for (var output = 0; output < 4; output++)
            {
                var weight = (float)m[(input * 5) + output];
                var offset = ((input * 4) + output) * 256;
                for (var value = 0; value < 256; value++)
                {
                    table[offset + value] = weight * value;
                }
            }
        }

        Span<float> translation =
        [
            (float)m[20] * 255, (float)m[21] * 255, (float)m[22] * 255, (float)m[23] * 255
        ];
        Span<byte> result = stackalloc byte[4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            // Inputs in red, green, blue, alpha order.
            int r = pixels[index + 2], g = pixels[index + 1], b = pixels[index], a = pixels[index + 3];
            for (var output = 0; output < 4; output++)
            {
                var sum = translation[output] +
                    table[((0 * 4) + output) * 256 + r] +
                    table[((1 * 4) + output) * 256 + g] +
                    table[((2 * 4) + output) * 256 + b] +
                    table[((3 * 4) + output) * 256 + a];
                result[output] = (byte)Math.Clamp((int)MathF.Round(sum), 0, 255);
            }

            pixels[index + 2] = result[0];
            pixels[index + 1] = result[1];
            pixels[index] = result[2];
            pixels[index + 3] = result[3];
        }
    }

    private static bool Matches(FullscreenColorEffect effect, double[] expected)
    {
        for (var index = 0; index < 25; index++)
        {
            if (Math.Abs(effect.Matrix[index] - expected[index]) > Tolerance)
            {
                return false;
            }
        }

        return true;
    }
}
