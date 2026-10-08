using System.Runtime.InteropServices;

namespace Recorder.Collectors.Graphics;

/// <summary>
/// Reads the full screen magnification transform with each desktop frame,
/// so playback can show the part of the screen the participant saw. It only
/// reads the transform; it never sets it. See
/// docs/architecture/magnified-view-playback.md.
/// </summary>
internal sealed class FullscreenMagnificationReader : IDisposable
{
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
        if (_disposed)
        {
            return Unavailable("The magnification reader is closed.");
        }

        if (!_initialized)
        {
            return Unavailable(_initializationProblem ?? "MagInitialize failed.");
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

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagGetFullscreenTransform(
        out float level,
        out int xOffset,
        out int yOffset);
}
