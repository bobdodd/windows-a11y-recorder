namespace Recorder.Contracts;

/// <summary>
/// A full screen Magnifier transform as read with MagGetFullscreenTransform:
/// the level and the x and y of the magnified view's upper-left corner, or
/// nulls and the problem that stopped the reading. Written as a desktop
/// frame's <c>fullscreenMagnification</c>.
/// </summary>
public sealed record MagnificationReading(double? Level, int? X, int? Y, string? Problem);

/// <summary>
/// A full screen color effect as read with MagGetFullscreenColorEffect: the
/// 25 values of the matrix, row by row, or a null matrix and the problem.
/// Written as a desktop frame's <c>fullscreenColorEffect</c>.
/// </summary>
public sealed record ColorEffectReading(double[]? Matrix, string? Problem)
{
    public bool Equals(ColorEffectReading? other) =>
        other is not null &&
        Problem == other.Problem &&
        (Matrix is null
            ? other.Matrix is null
            : other.Matrix is not null && Matrix.AsSpan().SequenceEqual(other.Matrix));

    public override int GetHashCode() =>
        HashCode.Combine(Problem, Matrix?.Length);
}

/// <summary>
/// The Magnifier change records of the graphics.magnifier channel: one when
/// a desktop frame's reading of the level, the position, or the color effect
/// differs from the previous frame's. See
/// docs/architecture/accessibility-preferences.md, "Visible focus and the
/// Enter key".
/// </summary>
public static class MagnifierChanges
{
    public const string Channel = "graphics.magnifier";
    public const string ChangeEventType = "magnifier-changed";

    public const string Level = "level";
    public const string Position = "position";
    public const string ColorEffect = "colorEffect";

    /// <summary>What a change can name, in the order it names them.</summary>
    public static IReadOnlyList<string> Parts { get; } = [Level, Position, ColorEffect];

    /// <summary>
    /// What differs between two frames' readings, in the order of
    /// <see cref="Parts"/>; empty when nothing does. A transform whose
    /// problem differs changes both the level and the position, since
    /// neither was read on one side. A frame with no reader at all (null)
    /// is compared as nothing changed, as it has nothing to compare.
    /// </summary>
    public static IReadOnlyList<string> Compare(
        MagnificationReading? previousMagnification,
        ColorEffectReading? previousColorEffect,
        MagnificationReading? currentMagnification,
        ColorEffectReading? currentColorEffect)
    {
        var changed = new List<string>(3);
        if (previousMagnification is { } before && currentMagnification is { } after)
        {
            var problemChanged = before.Problem != after.Problem;
            if (problemChanged || before.Level != after.Level)
            {
                changed.Add(Level);
            }

            if (problemChanged || before.X != after.X || before.Y != after.Y)
            {
                changed.Add(Position);
            }
        }

        if (previousColorEffect is { } beforeEffect && currentColorEffect is { } afterEffect &&
            !beforeEffect.Equals(afterEffect))
        {
            changed.Add(ColorEffect);
        }

        return changed;
    }
}

/// <summary>
/// The previous frame's Magnifier readings and time, giving the payload of a
/// change record when a frame's readings differ. The first frame's readings
/// are the start and give none. Used by one capture thread.
/// </summary>
public sealed class MagnifierChangeTracker
{
    private MagnificationReading? _magnification;
    private ColorEffectReading? _colorEffect;
    private long? _frameAt;

    /// <summary>Forgets the previous frame, for a new recording.</summary>
    public void Reset()
    {
        _magnification = null;
        _colorEffect = null;
        _frameAt = null;
    }

    /// <summary>
    /// Takes a frame's readings, and returns the change record's payload
    /// when they differ from the previous frame's, or null.
    /// </summary>
    public object? Observe(
        long frameAt,
        ulong frameSequence,
        MagnificationReading? magnification,
        ColorEffectReading? colorEffect)
    {
        var previousMagnification = _magnification;
        var previousColorEffect = _colorEffect;
        var previousFrameAt = _frameAt;
        _magnification = magnification;
        _colorEffect = colorEffect;
        _frameAt = frameAt;
        if (previousFrameAt is not { } previousAt)
        {
            return null;
        }

        var changed = MagnifierChanges.Compare(previousMagnification, previousColorEffect, magnification, colorEffect);
        if (changed.Count == 0)
        {
            return null;
        }

        return new
        {
            frameSequence,
            previousFrameAt = previousAt,
            changed = new
            {
                level = changed.Contains(MagnifierChanges.Level),
                position = changed.Contains(MagnifierChanges.Position),
                colorEffect = changed.Contains(MagnifierChanges.ColorEffect)
            },
            previous = new
            {
                fullscreenMagnification = previousMagnification,
                fullscreenColorEffect = previousColorEffect
            },
            current = new
            {
                fullscreenMagnification = magnification,
                fullscreenColorEffect = colorEffect
            }
        };
    }
}
