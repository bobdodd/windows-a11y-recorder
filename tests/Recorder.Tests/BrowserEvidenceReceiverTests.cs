using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class BrowserEvidenceReceiverTests
{
    [Fact]
    public void PipeSecuritySupportsChromiumLockdownWithoutMachineWideAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var descriptor =
            BrowserEvidencePipeFactory.CreateSecurityDescriptorSddl();

        Assert.Contains(
            $"(A;;GRGW;;;" +
            $"{BrowserEvidencePipeFactory.GetCurrentSessionSid().Value})",
            descriptor);
        Assert.Contains("(A;;GRGW;;;S-1-0-0)", descriptor);
        Assert.Contains("S:(ML;;;;;S-1-16-0)", descriptor);
        Assert.DoesNotContain("S-1-1-0", descriptor);
    }

    [Fact]
    public async Task AuthenticatesSynchronizesAndAcceptsBrowserEvidence()
    {
        var options = new BrowserEvidenceReceiverOptions
        {
            PipeName = $"recorder-browser-test-{Guid.NewGuid():N}",
            AuthenticationToken = "test-authentication-token",
            BrowserInstanceId = "browser-1",
            MaximumMessageBytes = 64 * 1024
        };
        var clock = new TestSessionClock();
        var sink = new TestEventSink();
        await using var receiver = new BrowserEvidenceReceiver(options);
        var context = new CollectorInitializationContext(
            "test-session",
            Path.GetTempPath(),
            clock,
            sink);

        var capability = await receiver.InitializeAsync(
            context,
            TestContext.Current.CancellationToken);
        Assert.Equal(CapabilityStatus.Supported, capability.Status);
        var started = await receiver.StartAsync(
            new SessionBoundary(
                clock.GetElapsedNanoseconds(),
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);
        Assert.True(started.Accepted);

        await using var client = new NamedPipeClientStream(
            ".",
            options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(
            5_000,
            TestContext.Current.CancellationToken);

        await WriteFrameAsync(
            client,
            new
            {
                kind = "hello",
                protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                authenticationToken = options.AuthenticationToken,
                browserInstanceId = "browser-1",
                processId = 1234,
                processType = "renderer",
                chromiumVersion = "test",
                monotonicFrequency = "1000000",
                parentProcessId = 1000,
                childProcessId = 17
            });
        using var request = await ReadFrameAsync(client);
        var requestId = request.RootElement
            .GetProperty("requestId")
            .GetString()!;
        Assert.Equal(
            "clock-sync-request",
            request.RootElement.GetProperty("kind").GetString());

        await WriteFrameAsync(
            client,
            new
            {
                kind = "clock-sync-response",
                requestId,
                browserReceiveTicks = "10000",
                browserSendTicks = "10100"
            });
        using var ready = await ReadFrameAsync(client);
        Assert.Equal(
            "ready",
            ready.RootElement.GetProperty("kind").GetString());

        var connected = await sink.WaitForRecordAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(BrowserEvidenceChannels.Lifecycle, connected.Channel);
        Assert.Equal(
            BrowserEvidenceEventTypes.Connected,
            connected.EventType);
        Assert.Equal(
            "browser-1",
            connected.Payload.GetProperty("browserInstanceId").GetString());
        Assert.Equal(
            1234,
            connected.Payload.GetProperty("processId").GetInt32());
        Assert.Equal(
            "renderer",
            connected.Payload.GetProperty("processType").GetString());
        Assert.Equal(
            "test",
            connected.Payload.GetProperty("chromiumVersion").GetString());
        Assert.Equal(
            1000,
            connected.Payload.GetProperty("parentProcessId").GetInt32());
        Assert.Equal(
            17,
            connected.Payload.GetProperty("childProcessId").GetInt32());
        Assert.False(
            connected.Payload.TryGetProperty("authenticationToken", out _));

        var synchronized = await sink.WaitForRecordAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(BrowserEvidenceChannels.Lifecycle, synchronized.Channel);
        Assert.Equal(
            BrowserEvidenceEventTypes.ClockSynchronized,
            synchronized.EventType);
        Assert.Equal(
            "chromium:browser-1:1234",
            synchronized.Payload.GetProperty("clockMappingId").GetString());
        Assert.Equal(
            "1000000",
            synchronized.Payload.GetProperty("monotonicFrequency").GetString());
        Assert.True(
            synchronized.Payload.GetProperty("uncertaintyNanoseconds")
                .GetInt64() >= 0);

        await WriteFrameAsync(
            client,
            new
            {
                kind = "evidence",
                protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                browserTimestampTicks = "10200",
                channel = BrowserEvidenceChannels.Cookie,
                eventType = BrowserEvidenceEventTypes.CookieOperation,
                payload = new
                {
                    context = new
                    {
                        browserInstanceId = "browser-1",
                        processId = 1234,
                        processType = "renderer",
                        profileId = "profile-1",
                        browserContextId = "context-1",
                        pageId = "page-1",
                        frameId = "frame-1",
                        documentId = "document-1",
                        executionWorldId = "main"
                    },
                    operation = "read",
                    name = "consent",
                    domain = "example.test",
                    path = "/",
                    sameSite = "Lax",
                    secure = true,
                    httpOnly = false,
                    partitioned = false,
                    source = "document-cookie",
                    result = "returned",
                    blockedReason = (string?)null
                },
                qualityFlags = Array.Empty<string>()
            });

        var record = await sink.WaitForRecordAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(BrowserEvidenceChannels.Cookie, record.Channel);
        Assert.Equal(
            BrowserEvidenceEventTypes.CookieOperation,
            record.EventType);
        Assert.Equal("consent", record.Payload.GetProperty("name").GetString());
        Assert.False(record.Payload.TryGetProperty("value", out _));
        Assert.Equal("chromium-monotonic", record.NativeTimestamp?.Domain);
        Assert.StartsWith("chromium:browser-1:1234", record.ClockMappingId);

        client.Close();
        var stopped = await receiver.StopAsync(
            new SessionBoundary(
                clock.GetElapsedNanoseconds(),
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);
        Assert.True(stopped.Accepted);
        Assert.Equal(CollectorLifecycleState.Stopped, stopped.State);
    }

    [Fact]
    public async Task RejectsUnauthenticatedBrowserWithoutConnectedRecord()
    {
        var options = new BrowserEvidenceReceiverOptions
        {
            PipeName = $"recorder-browser-test-{Guid.NewGuid():N}",
            AuthenticationToken = "expected-authentication-token",
            BrowserInstanceId = "browser-1",
            MaximumMessageBytes = 64 * 1024
        };
        var clock = new TestSessionClock();
        var sink = new TestEventSink();
        await using var receiver = new BrowserEvidenceReceiver(options);
        var context = new CollectorInitializationContext(
            "test-session",
            Path.GetTempPath(),
            clock,
            sink);

        await receiver.InitializeAsync(
            context,
            TestContext.Current.CancellationToken);
        await receiver.StartAsync(
            new SessionBoundary(
                clock.GetElapsedNanoseconds(),
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        await using var client = new NamedPipeClientStream(
            ".",
            options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(
            5_000,
            TestContext.Current.CancellationToken);
        await WriteFrameAsync(
            client,
            new
            {
                kind = "hello",
                protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                authenticationToken = "wrong-authentication-token",
                browserInstanceId = "browser-1",
                processId = 1234,
                processType = "browser",
                chromiumVersion = "test",
                monotonicFrequency = "1000000"
            });

        var rejected = await sink.WaitForRecordAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(BrowserEvidenceEventTypes.Omission, rejected.EventType);
        Assert.Equal(
            "browser-connection-rejected",
            rejected.Payload.GetProperty("reason").GetString());
        Assert.Equal(CollectorHealthState.Degraded, receiver.HealthState);

        var stopped = await receiver.StopAsync(
            new SessionBoundary(
                clock.GetElapsedNanoseconds(),
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);
        Assert.True(stopped.Accepted);
    }

    [Fact]
    public async Task RejectsChildWithoutProcessCorrelationIdentifiers()
    {
        var options = new BrowserEvidenceReceiverOptions
        {
            PipeName = $"recorder-browser-test-{Guid.NewGuid():N}",
            AuthenticationToken = "test-authentication-token",
            BrowserInstanceId = "browser-1",
            MaximumMessageBytes = 64 * 1024
        };
        var clock = new TestSessionClock();
        var sink = new TestEventSink();
        await using var receiver = new BrowserEvidenceReceiver(options);
        var context = new CollectorInitializationContext(
            "test-session",
            Path.GetTempPath(),
            clock,
            sink);

        await receiver.InitializeAsync(
            context,
            TestContext.Current.CancellationToken);
        await receiver.StartAsync(
            new SessionBoundary(
                clock.GetElapsedNanoseconds(),
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        await using var client = new NamedPipeClientStream(
            ".",
            options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(
            5_000,
            TestContext.Current.CancellationToken);
        await WriteFrameAsync(
            client,
            new
            {
                kind = "hello",
                protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                authenticationToken = options.AuthenticationToken,
                browserInstanceId = "browser-1",
                processId = 1234,
                processType = "renderer",
                chromiumVersion = "test",
                monotonicFrequency = "1000000"
            });

        var rejected = await sink.WaitForRecordAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(BrowserEvidenceEventTypes.Omission, rejected.EventType);
        Assert.Equal(
            "browser-connection-rejected",
            rejected.Payload.GetProperty("reason").GetString());
        Assert.Equal(CollectorHealthState.Degraded, receiver.HealthState);

        var stopped = await receiver.StopAsync(
            new SessionBoundary(
                clock.GetElapsedNanoseconds(),
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);
        Assert.True(stopped.Accepted);
    }

    [Theory]
    [InlineData("gpu-process")]
    [InlineData("utility")]
    public async Task RejectsChildProcessWithoutImplementedEvidenceHooks(
        string processType)
    {
        var options = new BrowserEvidenceReceiverOptions
        {
            PipeName = $"recorder-browser-test-{Guid.NewGuid():N}",
            AuthenticationToken = "test-authentication-token",
            BrowserInstanceId = "browser-1",
            MaximumMessageBytes = 64 * 1024
        };
        var clock = new TestSessionClock();
        var sink = new TestEventSink();
        await using var receiver = new BrowserEvidenceReceiver(options);
        var context = new CollectorInitializationContext(
            "test-session",
            Path.GetTempPath(),
            clock,
            sink);

        await receiver.InitializeAsync(
            context,
            TestContext.Current.CancellationToken);
        await receiver.StartAsync(
            new SessionBoundary(
                clock.GetElapsedNanoseconds(),
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        await using var client = new NamedPipeClientStream(
            ".",
            options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(
            5_000,
            TestContext.Current.CancellationToken);
        await WriteFrameAsync(
            client,
            new
            {
                kind = "hello",
                protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                authenticationToken = options.AuthenticationToken,
                browserInstanceId = "browser-1",
                processId = 1234,
                processType,
                chromiumVersion = "test",
                monotonicFrequency = "1000000",
                parentProcessId = 1000,
                childProcessId = 17
            });

        var rejected = await sink.WaitForRecordAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(BrowserEvidenceEventTypes.Omission, rejected.EventType);
        Assert.Equal(
            "browser-connection-rejected",
            rejected.Payload.GetProperty("reason").GetString());
        Assert.Equal(CollectorHealthState.Degraded, receiver.HealthState);

        var stopped = await receiver.StopAsync(
            new SessionBoundary(
                clock.GetElapsedNanoseconds(),
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);
        Assert.True(stopped.Accepted);
    }

    private static async Task WriteFrameAsync<T>(
        Stream stream,
        T message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(
            header,
            TestContext.Current.CancellationToken);
        await stream.WriteAsync(
            payload,
            TestContext.Current.CancellationToken);
        await stream.FlushAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<JsonDocument> ReadFrameAsync(Stream stream)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(
            header,
            TestContext.Current.CancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(
            payload,
            TestContext.Current.CancellationToken);
        return JsonDocument.Parse(payload);
    }

    private sealed class TestSessionClock : ISessionClock
    {
        private readonly long _origin = Stopwatch.GetTimestamp();

        public long Frequency => Stopwatch.Frequency;
        public long OriginTimestamp => _origin;
        public DateTimeOffset OriginUtc { get; } = DateTimeOffset.UtcNow;

        public long GetTimestamp() => Stopwatch.GetTimestamp();

        public long GetElapsedNanoseconds() =>
            (long)((decimal)(GetTimestamp() - _origin) *
                1_000_000_000m / Frequency);
    }

    private sealed class TestEventSink : IRecorderEventSink
    {
        private readonly Channel<RecorderEvent> _records =
            Channel.CreateUnbounded<RecorderEvent>();

        public bool TryWrite(RecorderEvent record) =>
            _records.Writer.TryWrite(record);

        public Task<RecorderEvent> WaitForRecordAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            _records.Reader.ReadAsync(cancellationToken)
                .AsTask()
                .WaitAsync(timeout, cancellationToken);
    }
}
