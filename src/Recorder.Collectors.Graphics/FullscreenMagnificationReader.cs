using System.Runtime.InteropServices;

namespace Recorder.Collectors.Graphics;

/// <summary>
/// Reads the full screen magnification transform with each desktop frame,
/// so playback can show the part of the screen the participant saw. It only
/// reads the transform; it never sets it. It must be created, read, and
/// disposed on one thread: the API answers only on the thread that called
/// MagInitialize, and fails with error 21 on any other, including one that
/// called MagInitialize itself (target machine, 2026-10-07). See
/// docs/architecture/magnified-view-playback.md.
/// </summary>
internal sealed class FullscreenMagnificationReader : IDisposable
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly bool _initialized;
    private readonly string? _initializationProblem;
    private bool _disposed;

    public FullscreenMagnificationReader()
    {
        try
        {
            _initialized = MagInitialize();
            if (!_initialized)
            {
                _initializationProblem =
                    $"MagInitialize failed with error {Marshal.GetLastWin32Error()}.";
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            _initializationProblem =
                $"The magnification API is not available: {exception.GetType().Name}.";
        }
    }

    /// <summary>
    /// The transform in effect now, as the payload's
    /// <c>fullscreenMagnification</c> object: the level and the x and y
    /// offsets of the magnified view's upper-left corner, or nulls and the
    /// problem when it could not be read.
    /// </summary>
    public object Read()
    {
        if (ReadProblem("transform") is { } problem)
        {
            return Unavailable(problem);
        }

        try
        {
            if (!MagGetFullscreenTransform(out var level, out var x, out var y))
            {
                return Unavailable(
                    $"MagGetFullscreenTransform failed with error {Marshal.GetLastWin32Error()}.");
            }

            if (!float.IsFinite(level) || level <= 0)
            {
                return Unavailable(
                    $"MagGetFullscreenTransform returned a level that is not positive: {level}.");
            }

            // A float has about seven significant digits; rounding keeps the
            // recorded level to the digits Windows gave, so 1.038 is not
            // written as 1.0379999876.
            return new
            {
                level = Math.Round((double)level, 6),
                x = (int?)x,
                y = (int?)y,
                problem = (string?)null
            };
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return Unavailable(
                $"The magnification API is not available: {exception.GetType().Name}.");
        }
    }

    /// <summary>
    /// The full screen color effect in effect now, as the payload's
    /// <c>fullscreenColorEffect</c> object: the 25 values of the matrix, row
    /// by row as Windows holds them, or a null matrix and the problem. The
    /// identity is recorded too, so a frame with no effect is told from a
    /// frame with no reading.
    /// </summary>
    public object ReadColorEffect()
    {
        if (ReadProblem("color effect") is { } problem)
        {
            return NoColorEffect(problem);
        }

        try
        {
            var matrix = new float[25];
            if (!MagGetFullscreenColorEffect(matrix))
            {
                return NoColorEffect(
                    $"MagGetFullscreenColorEffect failed with error {Marshal.GetLastWin32Error()}.");
            }

            if (Array.Exists(matrix, value => !float.IsFinite(value)))
            {
                return NoColorEffect("MagGetFullscreenColorEffect returned a value that is not finite.");
            }

            return new
            {
                matrix = Array.ConvertAll(matrix, value => Math.Round((double)value, 6)),
                problem = (string?)null
            };
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return NoColorEffect(
                $"The magnification API is not available: {exception.GetType().Name}.");
        }
    }

    // Why nothing can be read now, or null.
    private string? ReadProblem(string what) =>
        _disposed ? "The magnification reader is closed." :
        !_initialized ? _initializationProblem ?? "MagInitialize failed." :
        Environment.CurrentManagedThreadId != _threadId
            ? $"The {what} was to be read on a thread other than the one that called MagInitialize."
            : null;

    private static object NoColorEffect(string problem) => new
    {
        matrix = (double[]?)null,
        problem
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_initialized)
        {
            MagUninitialize();
        }
    }

    private static object Unavailable(string problem) => new
    {
        level = (double?)null,
        x = (int?)null,
        y = (int?)null,
        problem
    };

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagUninitialize();

    // MAGCOLOREFFECT is float transform[5][5], 25 floats in row order.
    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagGetFullscreenColorEffect(
        [Out] float[] effect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagGetFullscreenTransform(
        out float level,
        out int xOffset,
        out int yOffset);
}
