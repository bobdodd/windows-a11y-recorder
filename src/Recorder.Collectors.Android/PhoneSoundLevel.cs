using System.Buffers.Binary;

namespace Recorder.Collectors.Android;

/// <summary>
/// How loud the phone's sound is while it is recorded (android-implementation-plan.md,
/// slice 4): the loudest sample over the last <see cref="Window"/>, for the
/// status and the quiet message, and the peak and RMS over the whole
/// recording, for the stream's records.
///
/// Check 1 found a loudest half second of 2.9% of full scale too quiet to
/// use and 32.0% at about 80% Accessibility volume usable. Sound is taken
/// as too quiet when its loudest sample over the window is above 0 but
/// below <see cref="QuietPeak"/>, 10%, about a third of the usable level and
/// over three times the unusable one; where between the two the line is
/// best drawn is a choice, not a measurement. Exact silence, which the
/// phone gives between sounds, is not taken as quiet.
/// </summary>
public sealed class PhoneSoundLevel
{
    public const double QuietPeak = 0.10;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly Queue<(long Time, double Peak)> _recent = new();
    private double _peak;
    private double _sumSquares;
    private long _samples;

    /// <summary>Adds a packet of 16-bit little-endian samples received at a session time.</summary>
    /// <returns>The packet's loudest sample, as a fraction of full scale.</returns>
    public double Add(long time, ReadOnlySpan<byte> pcm)
    {
        var peak = 0d;
        var sumSquares = 0d;
        var count = pcm.Length / 2;
        for (var index = 0; index < count; index++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(pcm[(index * 2)..]) / 32768d;
            peak = Math.Max(peak, Math.Abs(sample));
            sumSquares += sample * sample;
        }

        lock (_gate)
        {
            _recent.Enqueue((time, peak));
            _peak = Math.Max(_peak, peak);
            _sumSquares += sumSquares;
            _samples += count;
        }

        return peak;
    }

    /// <summary>The loudest sample over the window up to a session time, or null when nothing was received in it.</summary>
    public double? Loudest(long now)
    {
        lock (_gate)
        {
            var from = now - (long)Window.TotalMilliseconds * 1_000_000;
            while (_recent.Count > 0 && _recent.Peek().Time < from)
            {
                _recent.Dequeue();
            }

            return _recent.Count == 0 ? null : _recent.Max(packet => packet.Peak);
        }
    }

    /// <summary>Whether sound over the window is present but below <see cref="QuietPeak"/>.</summary>
    public bool Quiet(long now) => Loudest(now) is > 0d and < QuietPeak;

    /// <summary>The peak and RMS over everything added, as fractions of full scale; null before any sound.</summary>
    public (double? Peak, double? Rms) Overall()
    {
        lock (_gate)
        {
            return _samples == 0 ? (null, null) : (_peak, Math.Sqrt(_sumSquares / _samples));
        }
    }

    public static double? Decibels(double? amplitude) =>
        amplitude is { } value && value > 0 ? 20 * Math.Log10(value) : null;
}
