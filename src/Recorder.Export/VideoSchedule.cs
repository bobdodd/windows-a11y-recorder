using Recorder.Session;

namespace Recorder.Export;

/// <summary>
/// One video sample of the export: a recorded frame, or black before the
/// first, shown from <see cref="StartNanoseconds"/> for
/// <see cref="DurationNanoseconds"/>. <see cref="Repeat"/> is true when the
/// sample shows the same picture as the one before it.
/// </summary>
public readonly record struct ScheduledFrame(
    SessionVideoFrame? Frame,
    long StartNanoseconds,
    long DurationNanoseconds,
    bool Repeat);

/// <summary>
/// The export's picture size, its constant frame rate, and the rate the
/// recording's frames came at, from which the bit rate is set.
/// </summary>
public readonly record struct VideoFormat(int Width, int Height, int FramesPerSecond, int RecordedFramesPerSecond);

/// <summary>
/// When each recorded frame is shown in the export, and at what size. The
/// export's video has a constant frame rate: Windows' H.264 encoder times
/// its output at the frame rate it is given, not by each sample's own
/// duration, so samples of other lengths would move the picture away from
/// the sound. Each recorded frame is shown from the sample nearest its
/// time until the next frame's, the last until the end of the recording,
/// and black before the first, so the export's timeline is the
/// recording's. See docs/architecture/mp4-export.md.
/// </summary>
public static class VideoSchedule
{
    /// <summary>
    /// The largest picture Windows' H.264 decoder plays; an export is no
    /// larger, so it plays on any Windows PC.
    /// </summary>
    public const int MaximumWidth = 4096;
    public const int MaximumHeight = 2304;

    /// <summary>
    /// The lowest frame rate of the export. A recording's frames, 5 a
    /// second by default, are shown on the nearest of 20 a second, so each
    /// appears within 25 ms of its time.
    /// </summary>
    public const int MinimumFramesPerSecond = 20;

    private const int MaximumFramesPerSecond = 120;

    public static IReadOnlyList<ScheduledFrame> Plan(
        IReadOnlyList<SessionVideoFrame> frames,
        long durationNanoseconds,
        int framesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);
        var ordered = frames
            .Where(frame => frame.MonotonicNanoseconds >= 0)
            .OrderBy(frame => frame.MonotonicNanoseconds)
            .ToList();

        // Of frames at the same time, the last recorded is shown.
        var distinct = new List<SessionVideoFrame>();
        foreach (var frame in ordered)
        {
            if (distinct.Count > 0 && distinct[^1].MonotonicNanoseconds == frame.MonotonicNanoseconds)
            {
                distinct[^1] = frame;
            }
            else
            {
                distinct.Add(frame);
            }
        }

        var schedule = new List<ScheduledFrame>();
        if (distinct.Count == 0)
        {
            return schedule;
        }

        var end = Math.Max(durationNanoseconds, distinct[^1].MonotonicNanoseconds + NominalInterval(distinct));
        var samples = (long)Math.Ceiling((decimal)end * framesPerSecond / 1_000_000_000m);
        var next = 0;
        SessionVideoFrame? shown = null;
        for (var sample = 0L; sample < samples; sample++)
        {
            var start = TimeOf(sample, framesPerSecond);
            var length = TimeOf(sample + 1, framesPerSecond) - start;
            // A frame is shown from the sample whose middle is after it,
            // so it appears at the sample nearest its time.
            var middle = start + (length / 2);
            var previous = shown;
            while (next < distinct.Count && distinct[next].MonotonicNanoseconds < middle)
            {
                shown = distinct[next];
                next++;
            }

            schedule.Add(new ScheduledFrame(shown, start, length, sample > 0 && ReferenceEquals(shown, previous)));
        }

        return schedule;
    }

    /// <summary>
    /// The export's size: the largest frame's, made smaller to fit within
    /// 4096 by 2304 with its shape kept, and even in both directions, as
    /// H.264 in 4:2:0 color needs. The recorded rate is the frames' usual
    /// spacing, to the nearest whole frame per second; the export's rate is
    /// the smallest multiple of it of at least 20 a second, so frames at
    /// their usual spacing fall on samples evenly.
    /// </summary>
    public static VideoFormat FormatOf(IReadOnlyList<SessionVideoFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var largest = frames
            .Where(frame => frame.Width > 0 && frame.Height > 0)
            .MaxBy(frame => (long)frame.Width * frame.Height);
        var (width, height) = largest is null
            ? (1280, 720)
            : Fit(largest.Width, largest.Height, MaximumWidth, MaximumHeight);
        var ordered = frames.OrderBy(frame => frame.MonotonicNanoseconds).ToList();
        var recorded = (int)Math.Clamp(Math.Round(1_000_000_000d / NominalInterval(ordered)), 1, MaximumFramesPerSecond);
        var rate = recorded >= MinimumFramesPerSecond
            ? recorded
            : Math.Min(MaximumFramesPerSecond, recorded * (int)Math.Ceiling((double)MinimumFramesPerSecond / recorded));
        return new VideoFormat(width, height, rate, recorded);
    }

    /// <summary>
    /// The largest even size with the shape of width by height that fits
    /// within the box, never larger than width by height.
    /// </summary>
    public static (int Width, int Height) Fit(int width, int height, int boxWidth, int boxHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var scale = Math.Min(1d, Math.Min((double)boxWidth / width, (double)boxHeight / height));
        var fittedWidth = Math.Max(2, (int)Math.Floor(width * scale) & ~1);
        var fittedHeight = Math.Max(2, (int)Math.Floor(height * scale) & ~1);
        return (fittedWidth, fittedHeight);
    }

    /// <summary>
    /// Where a picture of width by height is drawn within the export's
    /// picture: as large as fits with its shape kept, centred, black around.
    /// </summary>
    public static (double X, double Y, double Width, double Height) Placement(
        double width,
        double height,
        int outputWidth,
        int outputHeight)
    {
        if (width <= 0 || height <= 0)
        {
            return (0, 0, outputWidth, outputHeight);
        }

        var scale = Math.Min(outputWidth / width, outputHeight / height);
        var drawnWidth = width * scale;
        var drawnHeight = height * scale;
        return ((outputWidth - drawnWidth) / 2, (outputHeight - drawnHeight) / 2, drawnWidth, drawnHeight);
    }

    // The median spacing of the frames, or a fifth of a second, the
    // recorder's default, when there are fewer than two.
    private static long NominalInterval(IReadOnlyList<SessionVideoFrame> frames)
    {
        var gaps = new List<long>();
        for (var index = 1; index < frames.Count; index++)
        {
            var gap = frames[index].MonotonicNanoseconds - frames[index - 1].MonotonicNanoseconds;
            if (gap > 0)
            {
                gaps.Add(gap);
            }
        }

        if (gaps.Count == 0)
        {
            return 200_000_000;
        }

        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    // The start of a sample, in nanoseconds, rounded down.
    private static long TimeOf(long sample, int framesPerSecond) =>
        (long)((decimal)sample * 1_000_000_000m / framesPerSecond);
}
