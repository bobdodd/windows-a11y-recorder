using System.Runtime.InteropServices;
using Recorder.Session;

namespace Recorder.Export;

/// <summary>Draws a recorded frame as the export shows it.</summary>
public interface IExportFrameRenderer
{
    /// <summary>
    /// Fills <paramref name="destination"/>, <paramref name="width"/> times
    /// <paramref name="height"/> times 4 bytes, with the frame's picture,
    /// or black where there is no frame yet, as 32-bit blue, green, red,
    /// and unused bytes, top row first, with the overlay's evidence
    /// numbers and whatever else the renderer draws at the time, such as
    /// the phone's touches, over it.
    /// </summary>
    void Render(SessionVideoFrame? frame, long timeNanoseconds, ExportOverlay overlay, int width, int height, byte[] destination);

    /// <summary>
    /// A value that differs from one time to another when what the
    /// renderer draws over the frame, beyond the overlay, differs, such as
    /// the phone's touches; null where it draws nothing more. A sample is
    /// drawn again when its value is not equal to the sample before's.
    /// </summary>
    object? DrawnAt(long timeNanoseconds);
}

/// <summary>What an export writes: the whole recording, with the chosen sounds.</summary>
/// <param name="Audio">The sounds, in the order their own tracks follow the mixed track.</param>
/// <param name="FrameSource">What the frames show, which sets the export's size and rate.</param>
/// <param name="Annotations">A report's evidence numbers to show on the picture and name chapters after; none by default.</param>
/// <param name="Metadata">The MP4's title and comment, naming its recording and report; none by default.</param>
public sealed record Mp4ExportRequest(
    string OutputPath,
    IReadOnlyList<SessionVideoFrame> Frames,
    long DurationNanoseconds,
    IReadOnlyList<ExportAudioSource> Audio,
    SessionFrameSource FrameSource = SessionFrameSource.Desktop,
    IReadOnlyList<ExportAnnotation>? Annotations = null,
    Mp4Metadata? Metadata = null);

/// <summary>How far an export has got, in the recording's time.</summary>
public readonly record struct Mp4ExportProgress(long DoneNanoseconds, long TotalNanoseconds);

/// <summary>What an export wrote.</summary>
/// <param name="Encoder">Which attempt wrote the file; see <see cref="Mp4Exporter"/>.</param>
/// <param name="EarlierFailures">The errors of the attempts before it, if any.</param>
public sealed record Mp4ExportResult(
    string Path,
    VideoFormat Format,
    int AudioTracks,
    string Encoder,
    IReadOnlyList<string> EarlierFailures,
    IReadOnlyDictionary<string, AudioPlacementPlan> Placements)
{
    /// <summary>The chapters written, one for each evidence number within the recording.</summary>
    public int Chapters { get; init; }

    /// <summary>The title and comment written, or null for none.</summary>
    public Mp4Metadata? Metadata { get; init; }
}

/// <summary>
/// Writes a whole recording as an MP4 file: the frames as H.264 video on
/// the recording's timeline, and when sounds are chosen, one AAC track of
/// them mixed at the player's levels followed by each sound's own track at
/// its recorded level. The file is written under a temporary name and
/// renamed when it is complete, so a cancelled or failed export leaves no
/// partial MP4. See docs/architecture/mp4-export.md.
/// </summary>
public static class Mp4Exporter
{
    // At most a tenth of a second of sound is written at a time, so the
    // sound stays close to the picture in the file.
    private const int AudioChunkFrames = ExportAudioMixer.SampleRate / 10;

    // Windows is first allowed to use the graphics card's encoder; if that
    // fails, its own software encoder is used, and if that refuses the
    // variable bit rate setting, the encoder's defaults.
    private static readonly (bool Hardware, bool Settings, string Name)[] Attempts =
    [
        (true, true, "Windows encoder, graphics card allowed"),
        (false, true, "Windows software encoder"),
        (false, false, "Windows software encoder, default settings")
    ];

    public static Mp4ExportResult Export(
        Mp4ExportRequest request,
        IExportFrameRenderer renderer,
        IProgress<Mp4ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(renderer);
        var format = VideoSchedule.FormatOf(request.Frames, request.FrameSource);
        var schedule = VideoSchedule.Plan(request.Frames, request.DurationNanoseconds, format.FramesPerSecond);
        if (schedule.Count == 0)
        {
            throw new InvalidOperationException("The recording has no frames to export.");
        }

        var annotations = request.Annotations ?? [];
        var overlays = ExportAnnotations.Plan(schedule, annotations);
        var end = Math.Max(schedule[^1].StartNanoseconds + schedule[^1].DurationNanoseconds, request.DurationNanoseconds);
        var chapters = ExportAnnotations.Chapters(annotations, end);
        var temporary = request.OutputPath + ".partial";
        var failures = new List<string>();
        for (var attempt = 0; attempt < Attempts.Length; attempt++)
        {
            var (hardware, settings, name) = Attempts[attempt];
            try
            {
                var placements = Write(temporary, request, schedule, overlays, format, renderer, hardware, settings, progress, cancellationToken);
                if (request.Audio.Count > 0)
                {
                    Mp4TrackGroups.Apply(temporary);
                }

                var chaptered = Mp4Chapters.Apply(temporary, chapters);
                var titled = request.Metadata is { } metadata && Mp4MetadataWriter.Apply(temporary, metadata);

                File.Move(temporary, request.OutputPath, overwrite: true);
                return new Mp4ExportResult(
                    request.OutputPath,
                    format,
                    request.Audio.Count == 0 ? 0 : request.Audio.Count + 1,
                    name,
                    failures,
                    placements)
                {
                    Chapters = chaptered ? chapters.Count : 0,
                    Metadata = titled ? request.Metadata : null
                };
            }
            catch (COMException exception) when (attempt + 1 < Attempts.Length && !cancellationToken.IsCancellationRequested)
            {
                failures.Add($"{name}: {exception.Message}");
                Delete(temporary);
            }
            catch
            {
                Delete(temporary);
                throw;
            }
        }

        throw new InvalidOperationException("No encoder attempt remained.");
    }

    private static Dictionary<string, AudioPlacementPlan> Write(
        string path,
        Mp4ExportRequest request,
        IReadOnlyList<ScheduledFrame> schedule,
        IReadOnlyList<ExportOverlay> overlays,
        VideoFormat format,
        IExportFrameRenderer renderer,
        bool hardware,
        bool settings,
        IProgress<Mp4ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var mixer = new ExportAudioMixer(request.Audio);
        using var writer = new Mp4Writer(path, format, mixer.TrackCount, hardware, settings);
        var tracks = Enumerable.Range(0, mixer.TrackCount)
            .Select(_ => new byte[AudioChunkFrames * ExportAudioMixer.BlockAlign])
            .ToArray();
        var totalAudioFrames = AudioPlacement.FramesAt(Math.Max(0, request.DurationNanoseconds), ExportAudioMixer.SampleRate);
        var audioFrames = 0L;
        var end = schedule[^1].StartNanoseconds + schedule[^1].DurationNanoseconds;
        var total = Math.Max(end, request.DurationNanoseconds);
        var pixels = new byte[format.Width * format.Height * 4];

        void WriteAudioUntil(long nanoseconds)
        {
            if (mixer.TrackCount == 0)
            {
                return;
            }

            var target = Math.Min(totalAudioFrames, AudioPlacement.FramesAt(nanoseconds, ExportAudioMixer.SampleRate));
            while (audioFrames < target)
            {
                var frames = (int)Math.Min(AudioChunkFrames, target - audioFrames);
                mixer.Read(frames, tracks);
                var start = TimeOf(audioFrames);
                var duration = TimeOf(audioFrames + frames) - start;
                for (var track = 0; track < tracks.Length; track++)
                {
                    writer.WriteAudio(track, tracks[track].AsSpan(0, frames * ExportAudioMixer.BlockAlign), start, duration);
                }

                audioFrames += frames;
            }
        }

        ExportOverlay? previousOverlay = null;
        object? previousDrawn = null;
        for (var index = 0; index < schedule.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = schedule[index];
            var sampleEnd = sample.StartNanoseconds + sample.DurationNanoseconds;
            WriteAudioUntil(sampleEnd);

            // A sample is drawn again where its frame, its evidence numbers
            // or the touches drawn on it are not the sample before's.
            var middle = sample.StartNanoseconds + (sample.DurationNanoseconds / 2);
            var overlay = overlays[index];
            var drawn = renderer.DrawnAt(middle);
            if (index == 0 || !sample.Repeat || !ReferenceEquals(overlay, previousOverlay) || !Equals(drawn, previousDrawn))
            {
                if (sample.Frame is null && overlay.References.Count == 0 && drawn is null)
                {
                    Array.Clear(pixels);
                }
                else
                {
                    renderer.Render(sample.Frame, middle, overlay, format.Width, format.Height, pixels);
                }
            }

            previousOverlay = overlay;
            previousDrawn = drawn;

            writer.WriteVideo(pixels, sample.StartNanoseconds, sample.DurationNanoseconds);
            progress?.Report(new Mp4ExportProgress(sampleEnd, total));
        }

        WriteAudioUntil(total);
        cancellationToken.ThrowIfCancellationRequested();
        writer.Finish();
        progress?.Report(new Mp4ExportProgress(total, total));
        return mixer.Plans;
    }

    private static long TimeOf(long sampleFrames) =>
        (long)((decimal)sampleFrames * 1_000_000_000m / ExportAudioMixer.SampleRate);

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temporary file that cannot be removed is left; it is not
            // named as an MP4.
        }
    }
}
