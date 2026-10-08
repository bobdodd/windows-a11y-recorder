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
