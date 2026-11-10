using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 4 of docs/architecture/android-implementation-plan.md, first step:
// scrcpy 5.0.1's audio socket and the sound collector, against bytes laid
// out as the 5.0.1 server's AudioRawRecorder.java and Streamer.java write
// them, a fake phone clock and a fake player.
public sealed class AndroidSoundTests : IDisposable
{
    private const string Serial = "58191FDCQ00ABZ";

    // The phone clock in the fake is this far ahead of the session clock.
    private const long PhoneOffset = 7_000_000_000_000;

    // 1024 sample frames, as the server reads them (AudioConfig.java).
    private const int PacketFrames = 1024;
    private const int PacketBytes = PacketFrames * 4;
    private const long PacketNanoseconds = 21_333_333;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "android-sound-" + Guid.NewGuid().ToString("N"));

    public AndroidSoundTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // The protocol

    [Fact]
    public void Protocol_AsksForTheWholeOutputRaw()
    {
        var command = ScrcpyProtocol.ServerCommand(0x1234, "5.0.1");

        Assert.Contains("audio=true", command);
        Assert.Contains("audio_codec=raw", command);
        Assert.Contains("audio_source=output", command);
        Assert.DoesNotContain("audio_dup=true", command);
        Assert.Equal("raw", ScrcpyProtocol.CodecName(0x00726177));
        Assert.Equal(4, ScrcpyProtocol.SoundBlockAlign);
    }

    [Fact]
    public async Task Codec_Raw_IsRead()
    {
        await using var connection = new ScrcpyAudioConnection(new MemoryStream([.. Codec(ScrcpyProtocol.RawCodecId), .. Header(1000, 4), 1, 2, 3, 4]));

        await connection.ReadCodecAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(new ScrcpyPacketHeader(1000, false, false, 4), await connection.ReadHeaderAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Codec_Disabled_SaysThePhoneCouldNotCapture()
    {
        await using var connection = new ScrcpyAudioConnection(new MemoryStream(Codec(ScrcpyProtocol.DisabledCodecId)), serverLog: () => "WARN: Audio disabled");

        var failure = await Assert.ThrowsAsync<ScrcpyUnavailableException>(() =>
            connection.ReadCodecAsync(TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal(
            "scrcpy's server did not start the sound: the phone could not capture its sound. The server said: WARN: Audio disabled",
            failure.Message);
    }

    [Fact]
    public async Task Codec_ClosedAtOnce_SaysSo()
    {
        await using var connection = new ScrcpyAudioConnection(new MemoryStream());

        var failure = await Assert.ThrowsAsync<ScrcpyUnavailableException>(() =>
            connection.ReadCodecAsync(TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal("scrcpy's server on the phone closed the sound's connection at once.", failure.Message);
    }

    // The level

    [Fact]
    public void Level_QuietOnlyWhenSoundIsPresentButLow()
    {
        var level = new PhoneSoundLevel();
        level.Add(0, Pcm(PacketFrames, 0));
        Assert.Equal(0, level.Loudest(0));
        Assert.False(level.Quiet(0));

        // Check 1's unusable level, 2.9% of full scale.
        level.Add(1_000_000_000, Pcm(PacketFrames, 0.029));
        Assert.True(level.Quiet(1_000_000_000));

        // Check 1's usable level, 32.0%.
        level.Add(2_000_000_000, Pcm(PacketFrames, 0.32));
        Assert.False(level.Quiet(2_000_000_000));
        Assert.Equal(0.32, level.Loudest(2_000_000_000)!.Value, 3);

        // Ten seconds on, only the loud packet is left, and then nothing.
        Assert.Equal(0.32, level.Loudest(11_500_000_000)!.Value, 3);
        Assert.Null(level.Loudest(12_500_000_000));
        var (peak, rms) = level.Overall();
        Assert.Equal(0.32, peak!.Value, 3);
        Assert.True(rms > 0);
    }

    // The collector

    [Fact]
    public async Task Collector_StoresTheSoundPlaysItAndPlacesEachPacket()
    {
        var phoneStart = PhoneOffset + 1_000_000_000;
        var first = Pcm(PacketFrames, 0.5);
        var second = Pcm(PacketFrames, -0.25);
        byte[] stream =
        [
            .. Codec(ScrcpyProtocol.RawCodecId),
            .. Header(phoneStart / 1000, PacketBytes), .. first,
            .. Header(phoneStart / 1000 + 21_333, PacketBytes), .. second
        ];
        await using var phone = await PhoneSocket.OpenAsync();
        var connection = await phone.ConnectAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        var speakers = new FakeOutput();
        var captured = 0;
        await using var collector = new AndroidSoundCollector(
            Serial,
            () => Task.FromResult(new PhoneSoundSource(connection, null)),
            new FakePhoneClock(true),
            () => speakers,
            () => Interlocked.Increment(ref captured));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Equal(0, captured);
        await phone.WriteAsync(stream);
        await WaitUntilAsync(() => sink.Records.Count(record => record.EventType == AndroidSoundRecords.BufferEventType) == 2);
        Assert.Matches(
            @"^Phone sound: 2 packets, 0\.0 MB, loudest in the last 10 seconds 50% of full scale, median delay from phone to recorder -?\d+\.\d ms, playing on Fake speakers\.$",
            collector.Summary);
        Assert.Null(collector.HealthReason);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        // Told once, at the first packet, for the Accessibility volume (step 4c).
        Assert.Equal(1, captured);
        Assert.Equal([.. first, .. second], speakers.Played);
        Assert.True(speakers.Disposed);

        // The WAV's data is the packets as received.
        var file = Path.Combine(_folder, "audio", "device.wav");
        using (var reader = new NAudio.Wave.WaveFileReader(file))
        {
            Assert.Equal(48_000, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
            var data = new byte[reader.Length];
            Assert.Equal(data.Length, reader.Read(data, 0, data.Length));
            Assert.Equal([.. first, .. second], data);
        }

        var records = sink.Records.Where(record => record.Channel == AndroidSoundRecords.Channel).ToList();
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
        Assert.Equal(
            [
                AndroidSoundRecords.StreamStartedEventType, AndroidSoundRecords.BufferEventType,
                AndroidSoundRecords.BufferEventType, AndroidSoundRecords.StreamStoppedEventType
            ],
            records.Select(record => record.EventType));

        // The stream starts at the first sample: the first packet's phone
        // time, its last sample's, less its length.
        var started = records[0];
        Assert.Equal(1_000_000_000 - PacketNanoseconds, started.MonotonicNanoseconds);
        Assert.Equal("device", started.Payload.GetProperty("stream").GetString());
        Assert.Equal("audio/device.wav", started.Payload.GetProperty("path").GetString());
        Assert.Equal(48_000, started.Payload.GetProperty("format").GetProperty("sampleRate").GetInt32());

        var buffer = records[1];
        Assert.Equal(1_000_000_000, buffer.MonotonicNanoseconds);
        Assert.Equal(1_000_000_000, buffer.Payload.GetProperty("callbackMonotonicNanoseconds").GetInt64());
        Assert.Equal(1_000_000_000 - PacketNanoseconds, buffer.Payload.GetProperty("estimatedFirstSampleMonotonicNanoseconds").GetInt64());
        Assert.Equal(PacketFrames, buffer.Payload.GetProperty("sampleFrames").GetInt32());
        Assert.Equal(phoneStart / 1000, buffer.Payload.GetProperty("presentationMicroseconds").GetInt64());
        Assert.Equal(phoneStart, buffer.NativeTimestamp!.Value);
        Assert.Equal(AndroidCompanionRecords.ClockDomain, buffer.NativeTimestamp.Domain);
        Assert.Equal(AndroidCompanionRecords.FittedClockMapping, buffer.ClockMappingId);
        Assert.Equal(FakePhoneClock.Uncertainty, buffer.TimestampUncertaintyNanoseconds);
        Assert.Equal([AndroidSoundRecords.FirstSampleEstimatedFlag, AndroidSoundRecords.PhoneAudioTimeFlag], buffer.QualityFlags);
        Assert.Equal(PacketBytes, records[2].Payload.GetProperty("dataByteOffset").GetInt64());
        Assert.Equal(1_021_333_000, records[2].MonotonicNanoseconds);

        // The export reads the buffers as it reads the desktop's.
        Assert.NotNull(SessionAudioBuffer.FromPayload(buffer.Payload));
        var stopped = records[3];
        Assert.Equal(2 * PacketBytes, stopped.Payload.GetProperty("dataBytes").GetInt64());
        Assert.Equal(0.5, stopped.Payload.GetProperty("peakAmplitude").GetDouble(), 3);
    }

    [Fact]
    public async Task Collector_QuietSound_AsksForTheVolumeToBeRaised()
    {
        var phoneStart = PhoneOffset + 1_000_000_000;
        byte[] stream = [.. Codec(ScrcpyProtocol.RawCodecId), .. Header(phoneStart / 1000, PacketBytes), .. Pcm(PacketFrames, 0.029)];
        await using var phone = await PhoneSocket.OpenAsync();
        var connection = await phone.ConnectAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidSoundCollector(
            Serial,
            () => Task.FromResult(new PhoneSoundSource(connection, null)),
            new FakePhoneClock(true),
            null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await phone.WriteAsync(stream);
        await WaitUntilAsync(() => sink.Records.Any(record => record.EventType == AndroidSoundRecords.BufferEventType));

        Assert.Equal("The phone's sound is very quiet. Raise the phone's Accessibility volume now.", collector.HealthReason);
        Assert.Contains("loudest in the last 10 seconds 2.9% of full scale", collector.Summary);
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
    }

    [Fact]
    public async Task Collector_NoPlayer_IsStillRecorded()
    {
        var phoneStart = PhoneOffset + 1_000_000_000;
        byte[] stream = [.. Codec(ScrcpyProtocol.RawCodecId), .. Header(phoneStart / 1000, PacketBytes), .. Pcm(PacketFrames, 0)];
        await using var phone = await PhoneSocket.OpenAsync();
        var connection = await phone.ConnectAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidSoundCollector(
            Serial,
            () => Task.FromResult(new PhoneSoundSource(connection, null)),
            new FakePhoneClock(true),
            () => throw new InvalidOperationException("There is no playback device."));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await phone.WriteAsync(stream);
        await WaitUntilAsync(() => sink.Records.Any(record => record.EventType == AndroidSoundRecords.BufferEventType));

        Assert.Matches(@", silent in the last 10 seconds, .*, not played on this PC: There is no playback device\.$", collector.Summary);
        Assert.Null(collector.HealthReason);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
    }

    [Fact]
    public async Task Collector_NoClock_PlacesByReceipt()
    {
        byte[] stream = [.. Codec(ScrcpyProtocol.RawCodecId), .. Header(5_000_000, PacketBytes), .. Pcm(PacketFrames, 0.5)];
        await using var phone = await PhoneSocket.OpenAsync();
        var connection = await phone.ConnectAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidSoundCollector(
            Serial,
            () => Task.FromResult(new PhoneSoundSource(connection, null)),
            new FakePhoneClock(false),
            null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await phone.WriteAsync(stream);
        await WaitUntilAsync(() => sink.Records.Any(record => record.EventType == AndroidSoundRecords.BufferEventType));
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        var buffer = sink.Records.Single(record => record.EventType == AndroidSoundRecords.BufferEventType);
        Assert.Contains(AndroidScreenRecords.ReceiptTimeFlag, buffer.QualityFlags);
        Assert.Equal(buffer.Payload.GetProperty("receivedAtNanoseconds").GetInt64(), buffer.MonotonicNanoseconds);
        Assert.Null(buffer.ClockMappingId == AndroidCompanionRecords.FittedClockMapping ? buffer.ClockMappingId : null);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
    }

    [Fact]
    public async Task Collector_NoScreenStream_SaysWhyAndGoesOn()
    {
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidSoundCollector(
            Serial,
            () => Task.FromResult(new PhoneSoundSource(null, "the phone's screen stream did not start, and the sound comes from the same server. adb is not running.")),
            new FakePhoneClock(true),
            null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        var started = await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(started.Accepted);
        Assert.Equal(CollectorHealthState.Degraded, collector.HealthState);
        Assert.Equal(
            "The phone's sound could not be recorded: the phone's screen stream did not start, and the sound comes from the same server. adb is not running. The recording goes on without it.",
            collector.HealthReason);
        var error = sink.Records.Single(record => record.Channel == AndroidSoundRecords.Channel);
        Assert.Equal(AndroidSoundRecords.ErrorEventType, error.EventType);
        Assert.Equal("unavailable", error.Payload.GetProperty("errorType").GetString());
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
        Assert.False(File.Exists(Path.Combine(_folder, "audio", "device.wav")));
    }

    [Fact]
    public async Task Collector_PhoneCannotCapture_SaysWhy()
    {
        await using var phone = await PhoneSocket.OpenAsync();
        var connection = await phone.ConnectAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidSoundCollector(
            Serial,
            () => Task.FromResult(new PhoneSoundSource(connection, null)),
            new FakePhoneClock(true),
            null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await phone.WriteAsync(Codec(ScrcpyProtocol.DisabledCodecId));
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(
            "The phone's sound could not be recorded: scrcpy's server did not start the sound: the phone could not capture its sound. The recording goes on without it.",
            collector.HealthReason);
        Assert.Null(collector.Summary);
    }

    [Fact]
    public async Task Collector_PhoneUnplugged_SaysTheSoundStopped()
    {
        var phoneStart = PhoneOffset + 1_000_000_000;
        byte[] stream = [.. Codec(ScrcpyProtocol.RawCodecId), .. Header(phoneStart / 1000, PacketBytes), .. Pcm(PacketFrames, 0.5)];
        await using var phone = await PhoneSocket.OpenAsync();
        var connection = await phone.ConnectAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidSoundCollector(
            Serial,
            () => Task.FromResult(new PhoneSoundSource(connection, null)),
            new FakePhoneClock(true),
            null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await phone.WriteAsync(stream);
        await WaitUntilAsync(() => sink.Records.Any(record => record.EventType == AndroidSoundRecords.BufferEventType));
        await phone.CloseAsync();
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.StartsWith("The phone's sound stopped being recorded during the recording. The sound stream ended: the connection closed", collector.HealthReason);
        var types = sink.Records.Where(record => record.Channel == AndroidSoundRecords.Channel).Select(record => record.EventType).ToList();
        Assert.Equal(
            [
                AndroidSoundRecords.StreamStartedEventType, AndroidSoundRecords.BufferEventType,
                AndroidSoundRecords.ErrorEventType, AndroidSoundRecords.StreamStoppedEventType
            ],
            types);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
    }

    [Fact]
    public async Task Collector_ScreenNotStarted_DoesNotWait()
    {
        var never = new TaskCompletionSource<PhoneSoundSource>();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidSoundCollector(Serial, () => never.Task, new FakePhoneClock(true), null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(
            "The phone's sound could not be recorded: the phone's screen collector had not started. The recording goes on without it.",
            collector.HealthReason);
    }

    private static byte[] Codec(uint id)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, id);
        return bytes;
    }

    private static byte[] Header(long presentationMicroseconds, int size) =>
        ScrcpyProtocol.PacketHeader(presentationMicroseconds, false, size);

    /// <summary>Sample frames of two channels all at one level, as a fraction of full scale.</summary>
    private static byte[] Pcm(int frames, double level)
    {
        var bytes = new byte[frames * 4];
        var sample = (short)Math.Round(level * 32768);
        for (var index = 0; index < frames * 2; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), sample);
        }

        return bytes;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Timed out.");
            await Task.Delay(20);
        }
    }

    /// <summary>A phone clock exactly <see cref="PhoneOffset"/> ahead of the session clock.</summary>
    private sealed class FakePhoneClock(bool sampled) : IPhoneClock
    {
        public const long Uncertainty = 1_500_000;

        public Task<bool> FirstSample { get; } = Task.FromResult(sampled);

        public PhoneTimeMapping? Map(long phoneNanoseconds) =>
            sampled ? new PhoneTimeMapping(phoneNanoseconds - PhoneOffset, Uncertainty, 1) : null;
    }

    private sealed class FakeOutput : IPhoneSoundOutput
    {
        private readonly List<byte> _played = [];

        public string Name => "Fake speakers";

        public bool Disposed { get; private set; }

        public byte[] Played
        {
            get
            {
                lock (_played)
                {
                    return [.. _played];
                }
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            lock (_played)
            {
                _played.AddRange(buffer.AsSpan(offset, count).ToArray());
            }
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>The phone's end of an audio socket, over loopback.</summary>
    private sealed class PhoneSocket : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private TcpClient? _phone;

        public static Task<PhoneSocket> OpenAsync()
        {
            var socket = new PhoneSocket();
            socket._listener.Start();
            return Task.FromResult(socket);
        }

        public async Task<ScrcpyAudioConnection> ConnectAsync()
        {
            var recorder = new TcpClient();
            var accepting = _listener.AcceptTcpClientAsync();
            await recorder.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port);
            _phone = await accepting;
            return new ScrcpyAudioConnection(recorder.GetStream(), () =>
            {
                recorder.Dispose();
                return ValueTask.CompletedTask;
            });
        }

        public async Task WriteAsync(byte[] bytes) => await _phone!.GetStream().WriteAsync(bytes);

        public Task CloseAsync()
        {
            _phone?.Dispose();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _phone?.Dispose();
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ListSink : IRecorderEventSink
    {
        private readonly List<RecorderEvent> _records = [];

        public IReadOnlyList<RecorderEvent> Records
        {
            get
            {
                lock (_records)
                {
                    return [.. _records];
                }
            }
        }

        public bool TryWrite(RecorderEvent record)
        {
            lock (_records)
            {
                _records.Add(record);
            }

            return true;
        }
    }
}
