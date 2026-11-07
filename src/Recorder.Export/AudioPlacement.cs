using Recorder.Session;

namespace Recorder.Export;

/// <summary>
/// A run of an audio stream's WAV data placed on the recording's timeline:
/// <see cref="FrameCount"/> sample frames from <see cref="FileFrame"/> of
/// the file's data, starting at <see cref="SessionFrame"/>, counted in the
/// file's sample frames from the start of the recording.
/// </summary>
public readonly record struct PlacedAudioRun(long SessionFrame, long FileFrame, long FrameCount);

/// <summary>
/// Where an audio stream's sound goes on the recording's timeline, and how
/// much the placement had to correct.
/// </summary>
/// <param name="Runs">The runs in timeline order; they do not overlap.</param>
/// <param name="SilencesInserted">The gaps of more than the tolerance where nothing was recorded.</param>
/// <param name="SilenceFrames">The sample frames of silence those gaps add.</param>
/// <param name="FramesSkipped">The sample frames left out because the data ran ahead of its recorded times.</param>
/// <param name="FromBufferRecords">False when the stream had no buffer records and is placed as one run from its start.</param>
/// <param name="SampleRate">The WAV file's sample frames per second, in which the plan's frames are counted.</param>
public sealed record AudioPlacementPlan(
    IReadOnlyList<PlacedAudioRun> Runs,
    int SilencesInserted,
    long SilenceFrames,
    long FramesSkipped,
    bool FromBufferRecords,
    int SampleRate);

/// <summary>
/// Places an audio stream's data where its buffer records say it was
/// heard. A WAV file holds only what Windows delivered: system sound
/// capture delivers nothing while nothing plays, so the file has no
/// silence for those times and reading it straight through puts later
/// sound early. See docs/architecture/mp4-export.md, "Placing the sound".
/// </summary>
public static class AudioPlacement
{
    /// <summary>
    /// The difference, in time, between where the data would fall played
    /// straight through and where its buffer's record puts it, beyond which
    /// the placement follows the record.
    /// </summary>
    public const long ToleranceNanoseconds = 50_000_000;

    /// <param name="buffers">The stream's buffer records in buffer order.</param>
    /// <param name="sampleRate">The WAV file's sample frames per second.</param>
    /// <param name="fileFrames">The sample frames the WAV file holds.</param>
    /// <param name="trackStartNanoseconds">When the stream started, used only without buffer records.</param>
    /// <param name="blockAlign">The bytes of one sample frame of the WAV file.</param>
    public static AudioPlacementPlan Plan(
        IReadOnlyList<SessionAudioBuffer> buffers,
        int sampleRate,
        int blockAlign,
        long fileFrames,
        long trackStartNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockAlign);
        ArgumentOutOfRangeException.ThrowIfNegative(fileFrames);

        if (buffers.Count == 0)
        {
            // Without buffer records the file is played straight through
            // from the stream's start, as the player does.
            var runs = fileFrames == 0
                ? Array.Empty<PlacedAudioRun>()
                : [new PlacedAudioRun(FramesAt(Math.Max(0, trackStartNanoseconds), sampleRate), 0, fileFrames)];
            return new AudioPlacementPlan(runs, 0, 0, 0, false, sampleRate);
        }

        var tolerance = FramesAt(ToleranceNanoseconds, sampleRate);
        var placed = new List<PlacedAudioRun>();
        var silences = 0;
        var silenceFrames = 0L;
        var skipped = 0L;
        long? next = null;
        foreach (var buffer in buffers)
        {
            var fileFrame = buffer.DataByteOffset / blockAlign;
            var count = Math.Min(buffer.ByteLength / blockAlign, fileFrames - fileFrame);
            if (count <= 0)
            {
                // A record beyond the data the file holds, as when the
                // recording ended before the file was complete.
                continue;
            }

            var recorded = FramesAt(
                Math.Max(0, buffer.CallbackMonotonicNanoseconds - buffer.DurationNanoseconds),
                sampleRate);
            long at;
            if (next is not { } expected)
            {
                at = recorded;
            }
            else if (recorded - expected > tolerance)
            {
                // Nothing was recorded for a while: silence until the
                // buffer's recorded time.
                silences++;
                silenceFrames += recorded - expected;
                at = recorded;
            }
            else if (expected - recorded > tolerance)
            {
                // The data has run ahead of its recorded times, as a device
                // clock faster than the recorder's makes it: the part of
                // the buffer before the time already filled is left out.
                var overlap = Math.Min(expected - recorded, count);
                skipped += overlap;
                fileFrame += overlap;
                count -= overlap;
                at = expected;
                if (count == 0)
                {
                    continue;
                }
            }
            else
            {
                at = expected;
            }

            if (placed.Count > 0 && placed[^1] is var last &&
                last.SessionFrame + last.FrameCount == at &&
                last.FileFrame + last.FrameCount == fileFrame)
            {
                placed[^1] = last with { FrameCount = last.FrameCount + count };
            }
            else
            {
                placed.Add(new PlacedAudioRun(at, fileFrame, count));
            }

            next = at + count;
        }

        return new AudioPlacementPlan(placed, silences, silenceFrames, skipped, true, sampleRate);
    }

    /// <summary>The sample frame at a time, rounded to the nearest.</summary>
    public static long FramesAt(long nanoseconds, int sampleRate) =>
        (long)Math.Round((decimal)nanoseconds * sampleRate / 1_000_000_000m, MidpointRounding.AwayFromZero);
}
