using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Recorder.Export;

/// <summary>
/// An audio stream's sound on the recording's timeline from its start, at
/// the file's rate and channels: silence where nothing was recorded, the
/// file's data where the placement puts it, and silence after the last run.
/// </summary>
internal sealed class PlacedSampleProvider : ISampleProvider, IDisposable
{
    private readonly WaveFileReader _reader;
    private readonly ISampleProvider _samples;
    private readonly IReadOnlyList<PlacedAudioRun> _runs;
    private readonly int _channels;
    private int _run;
    private long _frame;
    private bool _positioned;

    public PlacedSampleProvider(string path, Func<WaveFileReader, AudioPlacementPlan> plan)
    {
        _reader = new WaveFileReader(path);
        try
        {
            _samples = _reader.ToSampleProvider();
            _channels = _reader.WaveFormat.Channels;
            Plan = plan(_reader);
            _runs = Plan.Runs;
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    public AudioPlacementPlan Plan { get; }

    public WaveFormat WaveFormat => _samples.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / _channels;
        var written = 0;
        while (written < frames)
        {
            // Past the runs, or before the next one: silence.
            if (_run >= _runs.Count || _frame < _runs[_run].SessionFrame)
            {
                var silence = _run >= _runs.Count
                    ? frames - written
                    : (int)Math.Min(frames - written, _runs[_run].SessionFrame - _frame);
                Array.Clear(buffer, offset + (written * _channels), silence * _channels);
                written += silence;
                _frame += silence;
                continue;
            }

            var run = _runs[_run];
            var into = _frame - run.SessionFrame;
            if (!_positioned)
            {
                _reader.Position = (run.FileFrame + into) * _reader.WaveFormat.BlockAlign;
                _positioned = true;
            }

            var wanted = (int)Math.Min(frames - written, run.FrameCount - into);
            var read = _samples.Read(buffer, offset + (written * _channels), wanted * _channels) / _channels;
            if (read < wanted)
            {
                // The file ended early: silence for the rest of the run.
                Array.Clear(
                    buffer,
                    offset + ((written + read) * _channels),
                    (wanted - read) * _channels);
            }

            written += wanted;
            _frame += wanted;
            if (_frame >= run.SessionFrame + run.FrameCount)
            {
                _run++;
                _positioned = false;
            }
        }

        return frames * _channels;
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// Two channels from any number: one channel is copied to both; more than
/// two are folded down with front left and right as they are. With six or
/// more channels (the 5.1 and 7.1 layouts) the centre goes to both sides at
/// 0.707 and the low-frequency channel is left out; every other channel
/// goes at 0.707 to the left when its index is even and to the right when
/// it is odd. Sums beyond full scale are clipped when mixed.
/// </summary>
internal sealed class StereoSampleProvider : ISampleProvider
{
    private const float Side = 0.7071f;
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _buffer = [];

    public StereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / 2;
        if (_channels == 2)
        {
            return _source.Read(buffer, offset, frames * 2);
        }

        var needed = frames * _channels;
        if (_buffer.Length < needed)
        {
            _buffer = new float[needed];
        }

        var read = _source.Read(_buffer, 0, needed) / _channels;
        for (var frame = 0; frame < read; frame++)
        {
            var at = frame * _channels;
            float left;
            float right;
            if (_channels == 1)
            {
                left = right = _buffer[at];
            }
            else
            {
                left = _buffer[at];
                right = _buffer[at + 1];
                for (var channel = 2; channel < _channels; channel++)
                {
                    // In the 5.1 and 7.1 layouts the fourth channel is the
                    // low-frequency one; the centre and the rear and side
                    // channels follow in left, right order.
                    if (channel == 3 && _channels >= 6)
                    {
                        continue;
                    }

                    var value = _buffer[at + channel] * Side;
                    if (channel == 2 && _channels >= 6)
                    {
                        left += value;
                        right += value;
                    }
                    else if (channel % 2 == 0)
                    {
                        left += value;
                    }
                    else
                    {
                        right += value;
                    }
                }
            }

            buffer[offset + (frame * 2)] = left;
            buffer[offset + (frame * 2) + 1] = right;
        }

        return read * 2;
    }
}

/// <summary>A source of the export: one recorded audio stream.</summary>
/// <param name="Stream">microphone, system, or screen-reader.</param>
/// <param name="Path">The stream's WAV file.</param>
/// <param name="GainDecibels">The player's level for the stream, applied in the mixed track only; above 0 is taken as 0.</param>
public sealed record ExportAudioSource(
    string Stream,
    string Path,
    long StartNanoseconds,
    IReadOnlyList<Recorder.Session.SessionAudioBuffer> Buffers,
    double GainDecibels);

/// <summary>
/// The export's sound, read in order from the start: the mixed track and
/// each source's own, all 48 kHz stereo 16-bit PCM.
/// </summary>
public sealed class ExportAudioMixer : IDisposable
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int BlockAlign = 4;

    private readonly List<(ExportAudioSource Source, float Gain, ISampleProvider Mixed, ISampleProvider Own)> _sources = [];
    private readonly List<IDisposable> _files = [];
    private float[] _read = [];
    private float[] _sum = [];

    public ExportAudioMixer(IReadOnlyList<ExportAudioSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        try
        {
            foreach (var source in sources)
            {
                // Each track reads its own copy of the file, so the mixed
                // track and the source's own stay independent.
                var mixed = Open(source, out var plan);
                var own = Open(source, out _);
                Plans.Add(source.Stream, plan);
                _sources.Add((source, (float)Math.Pow(10, Math.Min(0, source.GainDecibels) / 20), mixed, own));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Each source's placement, by stream.</summary>
    public Dictionary<string, AudioPlacementPlan> Plans { get; } = new(StringComparer.Ordinal);

    /// <summary>The number of tracks: the mixed one and each source's own.</summary>
    public int TrackCount => _sources.Count == 0 ? 0 : _sources.Count + 1;

    /// <summary>
    /// The next <paramref name="frames"/> sample frames of each track, in
    /// track order, into <paramref name="tracks"/>, each
    /// <paramref name="frames"/> times 4 bytes long.
    /// </summary>
    public void Read(int frames, IReadOnlyList<byte[]> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var samples = frames * Channels;
        if (_read.Length < samples)
        {
            _read = new float[samples];
            _sum = new float[samples];
        }

        Array.Clear(_sum, 0, samples);
        for (var index = 0; index < _sources.Count; index++)
        {
            var (_, gain, mixed, own) = _sources[index];
            Fill(mixed, samples);
            for (var sample = 0; sample < samples; sample++)
            {
                _sum[sample] += _read[sample] * gain;
            }

            Fill(own, samples);
            ToPcm16(_read, samples, tracks[index + 1]);
        }

        ToPcm16(_sum, samples, tracks[0]);
    }

    public void Dispose()
    {
        foreach (var file in _files)
        {
            file.Dispose();
        }

        _files.Clear();
        _sources.Clear();
    }

    private ISampleProvider Open(ExportAudioSource source, out AudioPlacementPlan plan)
    {
        var placed = new PlacedSampleProvider(
            source.Path,
            reader => AudioPlacement.Plan(
                source.Buffers,
                reader.WaveFormat.SampleRate,
                reader.WaveFormat.BlockAlign,
                reader.Length / reader.WaveFormat.BlockAlign,
                source.StartNanoseconds));
        _files.Add(placed);
        plan = placed.Plan;
        ISampleProvider stereo = new StereoSampleProvider(placed);
        return stereo.WaveFormat.SampleRate == SampleRate
            ? stereo
            : new WdlResamplingSampleProvider(stereo, SampleRate);
    }

    private void Fill(ISampleProvider provider, int samples)
    {
        var filled = 0;
        while (filled < samples)
        {
            var read = provider.Read(_read, filled, samples - filled);
            if (read <= 0)
            {
                Array.Clear(_read, filled, samples - filled);
                return;
            }

            filled += read;
        }
    }

    private static void ToPcm16(float[] samples, int count, byte[] destination)
    {
        for (var index = 0; index < count; index++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[index], -1f, 1f) * 32767f);
            destination[index * 2] = (byte)value;
            destination[(index * 2) + 1] = (byte)(value >> 8);
        }
    }
}
