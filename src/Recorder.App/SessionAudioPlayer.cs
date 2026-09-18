using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Recorder.Session;

namespace Recorder.App;

internal sealed class SessionAudioPlayer : IDisposable
{
    private readonly List<TrackPlayer> _tracks;

    public SessionAudioPlayer(IReadOnlyList<SessionAudioTrack> tracks)
    {
        _tracks = tracks.Select(track => new TrackPlayer(track)).ToList();
    }

    public void SetGains(double microphoneGainDb, double systemGainDb)
    {
        foreach (var track in _tracks)
        {
            track.GainDecibels = track.Track.Stream.Equals(
                "microphone",
                StringComparison.OrdinalIgnoreCase)
                ? microphoneGainDb
                : systemGainDb;
        }
    }

    public void Seek(long sessionPositionNanoseconds, bool play)
    {
        foreach (var track in _tracks)
        {
            track.Seek(sessionPositionNanoseconds, play);
        }
    }

    public void StartDueTracks(long sessionPositionNanoseconds)
    {
        foreach (var track in _tracks)
        {
            track.StartIfDue(sessionPositionNanoseconds);
        }
    }

    public void Pause()
    {
        foreach (var track in _tracks)
        {
            track.Pause();
        }
    }

    public void Dispose()
    {
        foreach (var track in _tracks)
        {
            track.Dispose();
        }

        _tracks.Clear();
    }

    private sealed class TrackPlayer : IDisposable
    {
        private readonly AudioFileReader _reader;
        private readonly GainSampleProvider _gain;
        private readonly WaveOutEvent _output;
        private bool _playing;

        public TrackPlayer(SessionAudioTrack track)
        {
            Track = track;
            _reader = new AudioFileReader(track.AbsolutePath);
            _gain = new GainSampleProvider(_reader);
            _output = new WaveOutEvent
            {
                DesiredLatency = 100,
                NumberOfBuffers = 3
            };
            _output.Init(_gain);
        }

        public SessionAudioTrack Track { get; }

        public double GainDecibels
        {
            set => _gain.Gain = (float)Math.Pow(10, Math.Min(0, value) / 20);
        }

        public void Seek(long sessionPositionNanoseconds, bool play)
        {
            _output.Pause();
            _playing = false;
            if (sessionPositionNanoseconds < Track.StartNanoseconds)
            {
                _reader.Position = 0;
                return;
            }

            var trackTicks =
                (sessionPositionNanoseconds - Track.StartNanoseconds) / 100;
            var position = TimeSpan.FromTicks(
                Math.Clamp(trackTicks, 0, _reader.TotalTime.Ticks));
            _reader.CurrentTime = position;
            if (play && position < _reader.TotalTime)
            {
                _output.Play();
                _playing = true;
            }
        }

        public void StartIfDue(long sessionPositionNanoseconds)
        {
            if (_playing ||
                sessionPositionNanoseconds < Track.StartNanoseconds ||
                _reader.CurrentTime >= _reader.TotalTime)
            {
                return;
            }

            var trackTicks =
                (sessionPositionNanoseconds - Track.StartNanoseconds) / 100;
            _reader.CurrentTime = TimeSpan.FromTicks(
                Math.Clamp(trackTicks, 0, _reader.TotalTime.Ticks));
            _output.Play();
            _playing = true;
        }

        public void Pause()
        {
            _output.Pause();
            _playing = false;
        }

        public void Dispose()
        {
            _output.Dispose();
            _reader.Dispose();
        }
    }

    private sealed class GainSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;

        public GainSampleProvider(ISampleProvider source)
        {
            _source = source;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;
        public float Gain { get; set; } = 1;

        public int Read(float[] buffer, int offset, int count)
        {
            var samplesRead = _source.Read(buffer, offset, count);
            for (var index = 0; index < samplesRead; index++)
            {
                buffer[offset + index] = Math.Clamp(
                    buffer[offset + index] * Gain,
                    -1f,
                    1f);
            }

            return samplesRead;
        }
    }
}
