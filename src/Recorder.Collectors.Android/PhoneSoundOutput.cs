using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Recorder.Collectors.Android;

/// <summary>
/// Plays the phone's sound on the PC while it is recorded. The server's
/// <c>audio_source=output</c> "forwards the whole audio output, and disables
/// playback on the device" (scrcpy, audio.md), so without this the
/// participant would not hear TalkBack.
/// </summary>
public interface IPhoneSoundOutput : IDisposable
{
    /// <summary>Where the sound is played, for the status.</summary>
    string Name { get; }

    /// <summary>Adds a packet of 48 kHz, two-channel, 16-bit sound to be played.</summary>
    void Write(byte[] buffer, int offset, int count);
}

/// <summary>
/// Plays the phone's sound through Windows' default playback device, in
/// shared mode, as received.
///
/// The packets come about every 21 ms (1024 sample frames at 48 kHz,
/// AudioConfig.java), and over USB not always evenly. What is waiting to be
/// played is kept to <see cref="MaxWaiting"/>: when more has gathered, as
/// after a pause in the packets, it is dropped, so the sound heard does not
/// fall further and further behind the phone. Only what is heard is
/// affected; the recording keeps every packet.
/// </summary>
public sealed class WasapiPhoneSoundOutput : IPhoneSoundOutput
{
    /// <summary>Asked of Windows as the playback latency, in milliseconds.</summary>
    public const int LatencyMilliseconds = 60;

    public static readonly TimeSpan MaxWaiting = TimeSpan.FromMilliseconds(250);

    private readonly MMDevice _device;
    private readonly BufferedWaveProvider _waiting;
    private readonly WasapiOut _output;
    private int _disposed;

    public WasapiPhoneSoundOutput()
    {
        using var devices = new MMDeviceEnumerator();
        _device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        try
        {
            Name = _device.FriendlyName;
            _waiting = new BufferedWaveProvider(new WaveFormat(
                ScrcpyProtocol.SoundSampleRate,
                ScrcpyProtocol.SoundBitsPerSample,
                ScrcpyProtocol.SoundChannels))
            {
                BufferDuration = TimeSpan.FromSeconds(1),
                DiscardOnBufferOverflow = true
            };
            _output = new WasapiOut(_device, AudioClientShareMode.Shared, true, LatencyMilliseconds);
            _output.Init(_waiting);
            _output.Play();
        }
        catch
        {
            _device.Dispose();
            throw;
        }
    }

    public string Name { get; }

    public void Write(byte[] buffer, int offset, int count)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (_waiting.BufferedDuration > MaxWaiting)
        {
            _waiting.ClearBuffer();
        }

        _waiting.AddSamples(buffer, offset, count);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _output.Stop();
        _output.Dispose();
        _device.Dispose();
    }
}
