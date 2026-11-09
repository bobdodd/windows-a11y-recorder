using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 3 of docs/architecture/android-implementation-plan.md, first step:
// scrcpy 5.0.1's video socket, the server's start, and the screen collector,
// against bytes laid out as the 5.0.1 server's Streamer.java writes them, a
// fake adb, and a fake phone clock.
public sealed class AndroidScreenTests : IDisposable
{
    private const string Serial = "58191FDCQ00ABZ";

    // The phone clock in the fake is this far ahead of the session clock.
    private const long PhoneOffset = 7_000_000_000_000;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "android-screen-" + Guid.NewGuid().ToString("N"));

    public AndroidScreenTests() => Directory.CreateDirectory(_folder);

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

    // The protocol, against bytes written out by hand from Streamer.java

    [Fact]
    public void Header_Session()
    {
        // writeSessionMeta: the session flag in the top bit, then 1080 (0x438) and 2404 (0x964).
        byte[] bytes = [0x80, 0, 0, 0, 0, 0, 0x04, 0x38, 0, 0, 0x09, 0x64];

        var header = Assert.IsType<ScrcpySessionHeader>(ScrcpyProtocol.ParseHeader(bytes));

        Assert.Equal(new ScrcpySessionHeader(1080, 2404, false), header);
        Assert.Equal(bytes, ScrcpyProtocol.SessionHeader(1080, 2404));
    }

    [Fact]
    public void Header_SessionClientResized()
    {
        byte[] bytes = [0x80, 0, 0, 1, 0, 0, 0x09, 0x64, 0, 0, 0x04, 0x38];

        Assert.Equal(new ScrcpySessionHeader(2404, 1080, true), ScrcpyProtocol.ParseHeader(bytes));
    }

    [Fact]
    public void Header_ConfigPacket()
    {
        // writeFrameMeta for a config packet: PACKET_FLAG_CONFIG alone, then 31 bytes.
        byte[] bytes = [0x40, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 31];

        Assert.Equal(new ScrcpyPacketHeader(null, true, false, 31), ScrcpyProtocol.ParseHeader(bytes));
        Assert.Equal(bytes, ScrcpyProtocol.PacketHeader(null, false, 31));
    }

    [Fact]
    public void Header_KeyFrame()
    {
        // 5,000,123,456 microseconds is 0x12A07D440; with PACKET_FLAG_KEY_FRAME, then 183,402 (0x2CC6A) bytes.
        byte[] bytes = [0x20, 0, 0, 0x01, 0x2A, 0x07, 0xD4, 0x40, 0, 0x02, 0xCC, 0x6A];

        Assert.Equal(new ScrcpyPacketHeader(5_000_123_456, false, true, 183_402), ScrcpyProtocol.ParseHeader(bytes));
        Assert.Equal(bytes, ScrcpyProtocol.PacketHeader(5_000_123_456, true, 183_402));
    }

    [Fact]
    public void Header_Picture()
    {
        byte[] bytes = [0, 0, 0, 0x01, 0x2A, 0x08, 0x16, 0xDB, 0, 0, 0x08, 0xA2];

        Assert.Equal(new ScrcpyPacketHeader(5_000_140_507, false, false, 2_210), ScrcpyProtocol.ParseHeader(bytes));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x09, 0x64 })]
    [InlineData(new byte[] { 0, 0, 0 })]
    public void Header_OutsideTheProtocol_Throws(byte[] bytes) =>
        Assert.Throws<FormatException>(() => ScrcpyProtocol.ParseHeader(bytes));

    [Fact]
    public void CodecIds()
    {
        Assert.Equal(ScrcpyProtocol.H264CodecId, ScrcpyProtocol.ParseCodecId("h264"u8));
        Assert.Equal("h264", ScrcpyProtocol.CodecName(ScrcpyProtocol.H264CodecId));
        Assert.Equal("h265", ScrcpyProtocol.CodecName(ScrcpyProtocol.ParseCodecId("h265"u8)));
        Assert.Equal("disabled", ScrcpyProtocol.CodecName(0));
    }

    [Fact]
    public void ServerCommand_ChangesNothingOnThePhone()
    {
        var command = ScrcpyProtocol.ServerCommand(0x1234abcd, "5.0.1");

        Assert.Equal(
            [
                "CLASSPATH=/data/local/tmp/a11yrecorder-scrcpy-server.jar", "app_process", "/", "com.genymobile.scrcpy.Server", "5.0.1",
                "scid=1234abcd", "log_level=info", "video=true", "audio=false", "control=false", "video_codec=h264",
                "video_bit_rate=8000000", "send_device_meta=false", "cleanup=false"
            ],
            command);
        Assert.DoesNotContain(command, argument => argument.StartsWith("show_touches", StringComparison.Ordinal));
        Assert.DoesNotContain(command, argument => argument.StartsWith("stay_awake", StringComparison.Ordinal));
        Assert.Equal("scrcpy_0000abcd", ScrcpyProtocol.SocketName(0xabcd));
    }

    // The connection

    [Theory]
    [InlineData(0u, "the phone could not capture its screen")]
    [InlineData(1u, "the server reported a configuration error")]
    [InlineData(0x68323635u, "it sent codec h265, not H.264")]
    public async Task Connect_NotH264_SaysWhy(uint codec, string meaning)
    {
        var stream = new MemoryStream(Codec(codec));

        var failure = await Assert.ThrowsAsync<ScrcpyUnavailableException>(() =>
            ScrcpyVideoConnection.ConnectAsync(stream, TimeSpan.FromSeconds(5), CancellationToken.None, serverLog: () => "ERROR: no encoder"));

        Assert.Equal($"scrcpy's server did not start the screen stream: {meaning}. The server said: ERROR: no encoder", failure.Message);
    }

    [Fact]
    public async Task Connect_ClosedAtOnce_SaysSo()
    {
        var failure = await Assert.ThrowsAsync<ScrcpyUnavailableException>(() =>
            ScrcpyVideoConnection.ConnectAsync(new MemoryStream(), TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal("scrcpy's server on the phone closed the connection at once.", failure.Message);
    }

    [Fact]
    public async Task Link_MissingServer_SaysToInstallAgain()
    {
        var adb = new ServerAdb(_ => { });
        var missing = Path.Combine(_folder, "scrcpy-server-v5.0.1");

        var failure = await Assert.ThrowsAsync<ScrcpyUnavailableException>(() =>
            ScrcpyLink.OpenAsync(adb, Serial, missing, Path.Combine(_folder, "log.txt"), CancellationToken.None));

        Assert.Contains("is missing", failure.Message);
        Assert.Contains("Install the recorder again.", failure.Message);
        Assert.Empty(adb.Commands);
    }

    [Fact]
    public async Task Link_PushFails_SaysWhy()
    {
        var adb = new ServerAdb(_ => { }, push: new AdbResult(1, "", "adb: error: failed to copy: device offline"));

        var failure = await Assert.ThrowsAsync<ScrcpyUnavailableException>(() =>
            ScrcpyLink.OpenAsync(adb, Serial, ServerFile(), Path.Combine(_folder, "log.txt"), CancellationToken.None));

        Assert.Equal("adb could not copy scrcpy's server to the phone: adb: error: failed to copy: device offline", failure.Message);
    }

    [Fact]
    public async Task Link_ServerStopsBeforeConnecting_QuotesIt()
    {
        var adb = new ServerAdb(phone => phone.Print("[server] ERROR: Could not create video encoder"), connects: false);

        var failure = await Assert.ThrowsAsync<ScrcpyUnavailableException>(() =>
            ScrcpyLink.OpenAsync(adb, Serial, ServerFile(), Path.Combine(_folder, "log.txt"), CancellationToken.None));

        Assert.Equal(
            "scrcpy's server on the phone stopped before it connected. The server said: [server] ERROR: Could not create video encoder",
            failure.Message);
        Assert.Contains(adb.Commands, command => command.StartsWith($"-s {Serial} reverse --remove localabstract:scrcpy_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Link_PushesReversesStartsAndConnects()
    {
        var adb = new ServerAdb(phone =>
        {
            phone.Print("[server] INFO: Device: [Google] google Pixel 10 Pro XL (Android 17)");
            phone.Write([.. Codec(ScrcpyProtocol.H264CodecId), .. ScrcpyProtocol.SessionHeader(1080, 2404)]);
        });
        var log = Path.Combine(_folder, "android", "scrcpy-server.log");

        await using (var connection = await ScrcpyLink.OpenAsync(adb, Serial, ServerFile(), log, CancellationToken.None))
        {
            Assert.Equal(ScrcpyProtocol.H264CodecId, connection.CodecId);
            Assert.Equal(new ScrcpySessionHeader(1080, 2404, false), await connection.ReadHeaderAsync(CancellationToken.None));
        }

        var commands = adb.Commands;
        Assert.Equal($"-s {Serial} push {ServerFile()} /data/local/tmp/a11yrecorder-scrcpy-server.jar", commands[0]);
        Assert.Matches($"^-s {Serial} reverse localabstract:scrcpy_[0-9a-f]{{8}} tcp:\\d+$", commands[1]);
        Assert.StartsWith($"-s {Serial} shell CLASSPATH=/data/local/tmp/a11yrecorder-scrcpy-server.jar app_process / com.genymobile.scrcpy.Server 5.0.1 scid=", commands[2]);
        var socket = commands[1].Split(' ')[3];
        Assert.Equal($"-s {Serial} reverse --remove {socket}", commands[3]);
        Assert.Contains("scid=" + socket["localabstract:scrcpy_".Length..], commands[2]);
        Assert.Equal("[server] INFO: Device: [Google] google Pixel 10 Pro XL (Android 17)" + Environment.NewLine, File.ReadAllText(log));
        Assert.True(adb.Disposed);
    }

    // The collector

    [Fact]
    public async Task Collector_StoresTheStreamAndPlacesEachPacket()
    {
        var config = Bytes(31, 1);
        var key = Bytes(5000, 2);
        var picture = Bytes(700, 3);
        var phoneStart = PhoneOffset + 1_000_000_000;
        byte[] stream =
        [
            .. ScrcpyProtocol.SessionHeader(1080, 2404),
            .. ScrcpyProtocol.PacketHeader(null, false, config.Length), .. config,
            .. ScrcpyProtocol.PacketHeader(phoneStart / 1000, true, key.Length), .. key,
            .. ScrcpyProtocol.PacketHeader(phoneStart / 1000 + 16_667, false, picture.Length), .. picture
        ];
        await using var phone = await PhoneSocket.OpenAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        var hashes = new ArtifactHashRegistry();
        var phoneClock = new FakePhoneClock(true);
        await using var collector = new AndroidScreenCollector(new ServerAdb(_ => { }), Serial, "unused", phoneClock, (_, _) => phone.ConnectAsync());
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink, hashes), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await phone.WriteAsync(stream);
        await WaitUntilAsync(() => sink.Records.Count(record => record.EventType == AndroidScreenRecords.PacketEventType) == 3);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        var file = Path.Combine(_folder, "android", "screen.h264");
        Assert.Equal([.. config, .. key, .. picture], File.ReadAllBytes(file));
        Assert.True(hashes.TryGetUnchanged(file, out var hash));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant(), hash);

        var records = sink.Records.Where(record => record.Channel == AndroidScreenRecords.VideoChannel).ToList();
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
        Assert.Equal(
            [
                AndroidScreenRecords.StreamEventType, AndroidScreenRecords.SessionEventType, AndroidScreenRecords.PacketEventType,
                AndroidScreenRecords.PacketEventType, AndroidScreenRecords.PacketEventType
            ],
            records.Select(record => record.EventType));
        Assert.Equal("h264", records[0].Payload.GetProperty("codec").GetString());
        Assert.Equal("android/screen.h264", records[0].Payload.GetProperty("path").GetString());

        var session = records[1];
        Assert.Equal(1080, session.Payload.GetProperty("width").GetInt32());
        Assert.Equal(0, session.Payload.GetProperty("byteOffset").GetInt64());
        var configRecord = records[2];
        Assert.True(configRecord.Payload.GetProperty("config").GetBoolean());
        Assert.Null(configRecord.NativeTimestamp);
        var keyRecord = records[3];
        // A session and a configuration packet are placed at the picture after them.
        Assert.Equal(keyRecord.MonotonicNanoseconds, session.MonotonicNanoseconds);
        Assert.Equal(keyRecord.MonotonicNanoseconds, configRecord.MonotonicNanoseconds);
        Assert.Contains(AndroidScreenCollector.NextPacketTimeFlag, session.QualityFlags);
        Assert.Contains(AndroidScreenCollector.NextPacketTimeFlag, configRecord.QualityFlags);

        Assert.True(keyRecord.Payload.GetProperty("keyFrame").GetBoolean());
        Assert.Equal(31, keyRecord.Payload.GetProperty("byteOffset").GetInt64());
        Assert.Equal(phoneStart / 1000 * 1000, keyRecord.NativeTimestamp!.Value);
        Assert.Equal(AndroidCompanionRecords.ClockDomain, keyRecord.NativeTimestamp.Domain);
        Assert.Equal(AndroidCompanionRecords.FittedClockMapping, keyRecord.ClockMappingId);
        Assert.Equal(1_000_000_000, keyRecord.MonotonicNanoseconds);
        Assert.Equal(FakePhoneClock.Uncertainty, keyRecord.TimestampUncertaintyNanoseconds);
        var pictureRecord = records[4];
        Assert.Equal(1_016_667_000, pictureRecord.MonotonicNanoseconds);
        Assert.Equal(5031, pictureRecord.Payload.GetProperty("byteOffset").GetInt64());
        Assert.Empty(pictureRecord.QualityFlags);
        Assert.Matches(@"^Phone screen: 3 packets, 0\.0 MB, 1080 by 2404, median delay from phone to recorder -?\d+\.\d ms\.$", collector.Summary);
    }

    [Fact]
    public async Task Collector_Rotation_StartsACaptureSession()
    {
        var phoneStart = PhoneOffset + 2_000_000_000;
        byte[] stream =
        [
            .. ScrcpyProtocol.SessionHeader(1080, 2404),
            .. ScrcpyProtocol.PacketHeader(phoneStart / 1000, true, 10), .. Bytes(10, 1),
            .. ScrcpyProtocol.SessionHeader(2404, 1080),
            .. ScrcpyProtocol.PacketHeader(phoneStart / 1000 + 500_000, true, 20), .. Bytes(20, 2)
        ];
        await using var phone = await PhoneSocket.OpenAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidScreenCollector(new ServerAdb(_ => { }), Serial, "unused", new FakePhoneClock(true), (_, _) => phone.ConnectAsync());
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await phone.WriteAsync(stream);
        await WaitUntilAsync(() => sink.Records.Count(record => record.EventType == AndroidScreenRecords.PacketEventType) == 2);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        var sessions = sink.Records.Where(record => record.EventType == AndroidScreenRecords.SessionEventType).ToList();
        Assert.Equal([1, 2], sessions.Select(record => record.Payload.GetProperty("session").GetInt32()));
        Assert.Equal(2404, sessions[1].Payload.GetProperty("width").GetInt32());
        Assert.Equal(10, sessions[1].Payload.GetProperty("byteOffset").GetInt64());
        Assert.Equal(2_500_000_000, sessions[1].MonotonicNanoseconds);
        var packets = sink.Records.Where(record => record.EventType == AndroidScreenRecords.PacketEventType).ToList();
        Assert.Equal([1, 2], packets.Select(record => record.Payload.GetProperty("session").GetInt32()));
        Assert.Contains(", 2 capture sessions, now 2404 by 1080,", collector.Summary);
    }

    [Fact]
    public async Task Collector_NoPhoneClock_PlacesByReceipt()
    {
        byte[] stream =
        [
            .. ScrcpyProtocol.SessionHeader(1080, 2404),
            .. ScrcpyProtocol.PacketHeader(123_456_789, true, 10), .. Bytes(10, 1)
        ];
        await using var phone = await PhoneSocket.OpenAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidScreenCollector(new ServerAdb(_ => { }), Serial, "unused", new FakePhoneClock(false), (_, _) => phone.ConnectAsync());
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await phone.WriteAsync(stream);
        await WaitUntilAsync(() => sink.Records.Any(record => record.EventType == AndroidScreenRecords.PacketEventType));
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        var packet = Assert.Single(sink.Records, record => record.EventType == AndroidScreenRecords.PacketEventType);
        Assert.Contains(AndroidScreenRecords.ReceiptTimeFlag, packet.QualityFlags);
        Assert.Equal(packet.Payload.GetProperty("receivedAtNanoseconds").GetInt64(), packet.MonotonicNanoseconds);
        Assert.Equal(123_456_789_000, packet.NativeTimestamp!.Value);
        Assert.Equal("session-stopwatch-origin", packet.ClockMappingId);
        Assert.EndsWith(", placed by the time each packet arrived, as there is no phone clock.", collector.Summary);
    }

    [Fact]
    public async Task Collector_StreamEnds_RecordsTheLoss()
    {
        await using var phone = await PhoneSocket.OpenAsync();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidScreenCollector(new ServerAdb(_ => { }), Serial, "unused", new FakePhoneClock(true), (_, _) => phone.ConnectAsync());
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);

        await phone.WriteAsync([.. ScrcpyProtocol.SessionHeader(1080, 2404)]);
        await phone.CloseAsync();
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        var lost = Assert.Single(sink.Records, record => record.EventType == AndroidScreenRecords.UnavailableEventType);
        Assert.True(lost.Payload.GetProperty("connectionLost").GetBoolean());
        Assert.Equal(
            "The phone's screen stopped being recorded during the recording. The screen stream ended: the phone's server closed the stream. The recording goes on without it.",
            collector.HealthReason);
        // The session header with no picture after it is placed at its receipt.
        var session = Assert.Single(sink.Records, record => record.EventType == AndroidScreenRecords.SessionEventType);
        Assert.Contains(AndroidScreenRecords.ReceiptTimeFlag, session.QualityFlags);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
    }

    [Fact]
    public async Task Collector_ScreenUnavailable_RecordsWhyAndGoesOn()
    {
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidScreenCollector(
            new ServerAdb(_ => { }),
            Serial,
            "unused",
            new FakePhoneClock(true),
            (_, _) => throw new ScrcpyUnavailableException("scrcpy's server on the phone stopped before it connected."));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, clock, sink), CancellationToken.None);

        var started = await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var stopped = await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(started.Accepted);
        Assert.True(stopped.Accepted);
        Assert.Equal(CollectorHealthState.Degraded, collector.HealthState);
        Assert.Equal(
            "The phone's screen could not be recorded: scrcpy's server on the phone stopped before it connected. The recording goes on without it.",
            collector.HealthReason);
        Assert.Null(collector.Summary);
        var unavailable = Assert.Single(sink.Records, record => record.EventType == AndroidScreenRecords.UnavailableEventType);
        Assert.False(unavailable.Payload.GetProperty("connectionLost").GetBoolean());
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
    }

    private string ServerFile()
    {
        var path = Path.Combine(_folder, "scrcpy-server-v5.0.1");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "server");
        }

        return path;
    }

    private static byte[] Codec(uint id) => [(byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id];

    private static byte[] Bytes(int count, byte value) => Enumerable.Repeat(value, count).ToArray();

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

    /// <summary>The phone's end of a video socket, over loopback.</summary>
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

        public async Task<ScrcpyVideoConnection> ConnectAsync()
        {
            var recorder = new TcpClient();
            var accepting = _listener.AcceptTcpClientAsync();
            await recorder.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port);
            _phone = await accepting;
            await _phone.GetStream().WriteAsync(Codec(ScrcpyProtocol.H264CodecId));
            return await ScrcpyVideoConnection.ConnectAsync(recorder.GetStream(), TimeSpan.FromSeconds(5), CancellationToken.None, () =>
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

    /// <summary>
    /// A fake adb whose <c>shell</c> plays scrcpy's server: it connects to the
    /// port the recorder had reversed, unless told not to, and runs the test's
    /// script on that connection.
    /// </summary>
    private sealed class ServerAdb(Action<ServerAdb.Phone> server, AdbResult? push = null, bool connects = true) : IAdb
    {
        private readonly List<string> _commands = [];
        private int _port;

        public string Path => "C:\\adb.exe";

        public bool Disposed { get; private set; }

        public List<string> Commands
        {
            get
            {
                lock (_commands)
                {
                    return [.. _commands];
                }
            }
        }

        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
        {
            lock (_commands)
            {
                _commands.Add(string.Join(' ', arguments));
            }

            if (arguments[2] == "push")
            {
                return Task.FromResult(push ?? new AdbResult(0, "1 file pushed.", ""));
            }

            if (arguments[2] == "reverse" && arguments[3] != "--remove")
            {
                _port = int.Parse(arguments[4]["tcp:".Length..], System.Globalization.CultureInfo.InvariantCulture);
            }

            return Task.FromResult(new AdbResult(0, "", ""));
        }

        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => Task.FromResult(0);

        public IAdbRunning Start(IReadOnlyList<string> arguments, Action<string> onLine)
        {
            lock (_commands)
            {
                _commands.Add(string.Join(' ', arguments));
            }

            var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            TcpClient? client = null;
            _ = Task.Run(async () =>
            {
                Phone? phone = null;
                if (connects)
                {
                    client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, _port);
                }

                phone = new Phone(onLine, client?.GetStream());
                server(phone);
                if (!connects)
                {
                    exited.TrySetResult(1);
                }
            });
            return new Running(exited, () =>
            {
                client?.Dispose();
                Disposed = true;
            });
        }

        public sealed class Phone(Action<string> print, NetworkStream? stream)
        {
            public void Print(string line) => print(line);

            public void Write(byte[] bytes) => stream!.Write(bytes);
        }

        private sealed class Running(TaskCompletionSource<int> exited, Action dispose) : IAdbRunning
        {
            public Task<int> Exited => exited.Task;

            public ValueTask DisposeAsync()
            {
                exited.TrySetResult(0);
                dispose();
                return ValueTask.CompletedTask;
            }
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
