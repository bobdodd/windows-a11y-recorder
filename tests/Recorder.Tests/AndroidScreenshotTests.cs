using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 4a of docs/architecture/android-checks.md: screenshots of the
// phone's display, as the companion app sends them (Screenshots.java and
// RecorderLink.java), read, written as PNG files and placed on the session
// clock with a fake companion connection and a fake phone clock.
public sealed class AndroidScreenshotTests : IDisposable
{
    private const string Serial = "58191FDCQ00ABZ";

    // As in AndroidAccessibilityTreeTests: the fake phone clock is this far
    // ahead of the session clock.
    private const long PhoneOffset = 5_000_000_000_000;

    private const long Second = 1_000_000_000;

    private const long Millisecond = 1_000_000;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "screenshots-" + Guid.NewGuid().ToString("N"));

    public AndroidScreenshotTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    // The protocol

    [Fact]
    public void Protocol_AsksForScreenshots()
    {
        Assert.True(CompanionProtocol.Version >= 6);
        Assert.Equal("{\"type\":\"screenshots\"}\n", CompanionProtocol.ScreenshotsRequest);
    }

    [Fact]
    public void Protocol_ReadsAScreenshotWithItsPng()
    {
        var png = Png(4, 3);
        var line = ScreenshotLine(7, "settled", 2 * Second, png, lastChange: 1_234);

        Assert.IsType<CompanionScreenshotLine>(CompanionProtocol.Parse(line));
        var (message, problem) = CompanionProtocol.ReadScreenshot(line);

        Assert.Null(problem);
        var screenshot = Assert.IsType<CompanionScreenshot>(message);
        Assert.Equal(7, screenshot.Index);
        Assert.Equal("settled", screenshot.Reason);
        Assert.Equal(2 * Second + PhoneOffset, screenshot.Requested);
        Assert.Equal((2 * Second + PhoneOffset + 5 * Millisecond) / Millisecond, screenshot.TimestampMillis);
        Assert.Equal(1_234, screenshot.LastChangeMillis);
        Assert.Equal(4, screenshot.Width);
        Assert.Equal(3, screenshot.Height);
        Assert.Equal("sRGB IEC61966-2.1", screenshot.ColourSpace);
        Assert.False(screenshot.Converted);
        Assert.Equal(png, screenshot.Png);
    }

    [Fact]
    public void Protocol_ReadsARefusal_AndNoChangeYetAsNull()
    {
        var refusal = RefusalLine(3, 6, 2 * Second);
        Assert.IsType<CompanionScreenshotLine>(CompanionProtocol.Parse(refusal));
        var read = Assert.IsType<CompanionScreenshotRefusal>(CompanionProtocol.ReadScreenshot(refusal).Message);
        Assert.Equal(6, read.Code);
        Assert.Equal(2 * Second + PhoneOffset + 4 * Millisecond, read.Failed);

        var first = CompanionProtocol.ReadScreenshot(ScreenshotLine(1, "requested", 2 * Second, Png(4, 3), lastChange: -1));
        Assert.Null(Assert.IsType<CompanionScreenshot>(first.Message).LastChangeMillis);
    }

    [Fact]
    public void Protocol_AScreenshotItCannotRead_IsAProblemNotAFailure()
    {
        var line = ScreenshotLine(1, "requested", 2 * Second, Png(4, 3)).Replace("\"png\":\"", "\"png\":\"%%", StringComparison.Ordinal);

        var (message, problem) = CompanionProtocol.ReadScreenshot(line);

        Assert.Null(message);
        Assert.Equal("its PNG is not base64", problem);
    }

    [Theory]
    [InlineData(3, "interval-time-short")]
    [InlineData(6, "secure-window")]
    [InlineData(42, "unknown")]
    public void Refusals_AreNamedByAndroidsCodes(int code, string name)
    {
        Assert.Equal(name, AndroidScreenshotRecords.RefusalName(code));
        Assert.False(string.IsNullOrEmpty(AndroidScreenshotRecords.RefusalText(code)));
    }

    // The collector

    [Fact]
    public async Task Collector_WritesEachScreenshotAsAPng_PlacedAtAndroidsTime_AndCountsThem()
    {
        var screenshots = new FakeScreenshots();
        var sink = new ListSink();
        var hashes = new ArtifactHashRegistry();
        await using var collector = new AndroidScreenshotCollector(Serial, screenshots, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, new SessionClock(), sink, hashes), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await screenshots.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var first = Png(4, 3);
        var second = Png(4, 3, shade: 200);
        screenshots.Send(Received(ScreenshotLine(1, "requested", 2 * Second, first), 2 * Second));
        // Android's time outside the call, so placed halfway through it.
        screenshots.Send(Received(ScreenshotLine(2, "settled", 3 * Second, second, timestampMillis: 1), 3 * Second));
        // The third never arrived.
        screenshots.Send(new ReceivedScreenshot(RefusalLine(4, 6, 3 * Second + 500 * Millisecond), 3 * Second + 510 * Millisecond));
        screenshots.Send(new ReceivedScreenshot("{\"index\":5,\"type\":\"screenshot\"}", 4 * Second));
        var expected =
            "Phone screenshots: 2 (1 at the start, 1 as a window settled), a median 0 KB each, in sRGB IEC61966-2.1, " +
            "the phone took a median 120 ms from asking to the PNG, refused by Android: 1 secure-window, " +
            "1 placed by the companion's call as Android's time was outside it, 1 missing, 1 could not be read, " +
            "median delay from PNG to recorder 30 ms.";
        await WaitUntilAsync(() => collector.Summary == expected, () => collector.Summary);
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        var records = sink.Records.ToList();
        var validator = new EventRecordValidator("session-1");
        foreach (var record in records)
        {
            Assert.Empty(validator.Validate(record));
        }

        var kept = records.Where(record => record.EventType == AndroidScreenshotRecords.ScreenshotEventType).ToList();
        Assert.Equal(2, kept.Count);
        Assert.All(kept, record => Assert.Equal(AndroidScreenshotRecords.Channel, record.Channel));
        Assert.All(kept, record => Assert.Equal(AndroidCompanionRecords.FittedClockMapping, record.ClockMappingId));

        var one = kept[0];
        Assert.Equal([AndroidScreenshotRecords.PhoneScreenshotTimeFlag], one.QualityFlags);
        Assert.Equal(2 * Second + 5 * Millisecond, one.MonotonicNanoseconds);
        Assert.Equal(2 * Second + 5 * Millisecond + PhoneOffset, one.NativeTimestamp!.Value);
        Assert.Equal(FakePhoneClock.Uncertainty + Millisecond, one.TimestampUncertaintyNanoseconds);
        Assert.Equal("screenshots/android/0000000001.png", one.Payload.GetProperty("path").GetString());
        Assert.Equal("requested", one.Payload.GetProperty("reason").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(first)), one.Payload.GetProperty("sha256").GetString());
        Assert.Equal(first.Length, one.Payload.GetProperty("byteLength").GetInt64());
        Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(_folder, "screenshots", "android", "0000000001.png"), TestContext.Current.CancellationToken));

        var two = kept[1];
        Assert.Equal([AndroidScreenshotRecords.PhoneCallTimeFlag], two.QualityFlags);
        Assert.Equal(3 * Second + 10 * Millisecond, two.MonotonicNanoseconds);
        Assert.Equal(FakePhoneClock.Uncertainty + 10 * Millisecond, two.TimestampUncertaintyNanoseconds);
        Assert.Equal(second, await File.ReadAllBytesAsync(Path.Combine(_folder, "screenshots", "android", "0000000002.png"), TestContext.Current.CancellationToken));
        Assert.Equal(2, hashes.Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "screenshots", "android"), "*.tmp"));

        var refused = Assert.Single(records, record => record.EventType == AndroidScreenshotRecords.RefusedEventType);
        Assert.Equal(3 * Second + 504 * Millisecond, refused.MonotonicNanoseconds);
        Assert.Equal("secure-window", refused.Payload.GetProperty("codeName").GetString());

        var unreadable = Assert.Single(records, record => record.EventType == AndroidScreenshotRecords.UnavailableEventType);
        Assert.Equal(4 * Second, unreadable.MonotonicNanoseconds);
        Assert.StartsWith("A screenshot of the phone could not be read: ", unreadable.Payload.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Collector_APngOfAnotherSize_IsNotKept()
    {
        var screenshots = new FakeScreenshots();
        var sink = new ListSink();
        await using var collector = new AndroidScreenshotCollector(Serial, screenshots, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, new SessionClock(), sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await screenshots.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        screenshots.Send(Received(ScreenshotLine(1, "requested", 2 * Second, Png(5, 3), width: 4), 2 * Second));
        await WaitUntilAsync(() => collector.Summary?.Contains("1 could not be read", StringComparison.Ordinal) == true, () => collector.Summary);
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.DoesNotContain(sink.Records, record => record.EventType == AndroidScreenshotRecords.ScreenshotEventType);
        Assert.False(Directory.Exists(Path.Combine(_folder, "screenshots")));
    }

    [Fact]
    public async Task Collector_WithoutAPhoneClock_PlacesScreenshotsAsTheyArrive()
    {
        var screenshots = new FakeScreenshots();
        var sink = new ListSink();
        await using var collector = new AndroidScreenshotCollector(Serial, screenshots, clock: null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, new SessionClock(), sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await screenshots.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => collector.Summary == "Phone screenshots: none yet, placed by the time each arrived, as there is no phone clock.", () => collector.Summary);

        screenshots.Send(Received(ScreenshotLine(1, "requested", 2 * Second, Png(4, 3)), 2 * Second));
        await WaitUntilAsync(() => collector.Summary?.StartsWith("Phone screenshots: 1 ", StringComparison.Ordinal) == true, () => collector.Summary);
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        var record = Assert.Single(sink.Records, record => record.EventType == AndroidScreenshotRecords.ScreenshotEventType);
        Assert.Equal(2 * Second + 150 * Millisecond, record.MonotonicNanoseconds);
        Assert.Equal([AndroidScreenRecords.ReceiptTimeFlag], record.QualityFlags);
        Assert.Null(record.TimestampUncertaintyNanoseconds);
        Assert.Empty(new EventRecordValidator("session-1").Validate(record));
    }

    [Fact]
    public async Task Collector_NoCompanionConnection_RecordsWhyAndGoesOn()
    {
        var screenshots = new FakeScreenshots(connected: false);
        var sink = new ListSink();
        await using var collector = new AndroidScreenshotCollector(Serial, screenshots, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, new SessionClock(), sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded, () => collector.HealthState.ToString());
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Null(collector.Summary);
        Assert.Equal(
            "The phone's screenshots could not be recorded: the companion app is not connected, so they could not be asked for. " +
            "The android.companion line says why. The recording goes on without them.",
            collector.HealthReason);
        var record = Assert.Single(sink.Records, record => record.EventType == AndroidScreenshotRecords.UnavailableEventType);
        Assert.Equal(AndroidScreenshotRecords.Channel, record.Channel);
        Assert.Empty(new EventRecordValidator("session-1").Validate(record));
    }

    [Fact]
    public async Task Collector_ConnectionEnds_RecordsItAndKeepsWhatCame()
    {
        var screenshots = new FakeScreenshots();
        var sink = new ListSink();
        await using var collector = new AndroidScreenshotCollector(Serial, screenshots, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", _folder, new SessionClock(), sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await screenshots.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        screenshots.Send(Received(ScreenshotLine(1, "requested", 2 * Second, Png(4, 3)), 2 * Second));
        screenshots.End();
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded, () => collector.HealthState.ToString());
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.StartsWith("Phone screenshots: 1 (1 at the start, 0 as a window settled)", collector.Summary);
        Assert.Equal(
            "The phone's screenshots stopped being recorded during the recording: the connection to the companion app ended: " +
            "the phone was unplugged, adb stopped, or the companion app was turned off. The recording goes on without them.",
            collector.HealthReason);
        var lost = Assert.Single(sink.Records, record => record.EventType == AndroidScreenshotRecords.UnavailableEventType);
        Assert.True(lost.Payload.GetProperty("connectionLost").GetBoolean());
        Assert.Single(sink.Records, record => record.EventType == AndroidScreenshotRecords.ScreenshotEventType);
    }

    // The validator

    [Fact]
    public void Validator_AcceptsTheCollectorsScreenshot()
    {
        Assert.Empty(Validate(Payload()));
    }

    [Fact]
    public void Validator_RejectsAPathOutsideTheScreenshotsFolder()
    {
        var payload = Payload();
        payload["path"] = "frames/android/0000000001.png";

        Assert.Single(Validate(payload), issue => issue.Code == "android-screenshot-path");
    }

    [Fact]
    public void Validator_RejectsAHashThatIsNotSha256()
    {
        var payload = Payload();
        payload["sha256"] = "ABC";

        Assert.Single(Validate(payload), issue => issue.Code == "android-screenshot-hash");
    }

    private static JsonObject Payload()
    {
        var screenshot = new AndroidScreenshot(
            Serial,
            1,
            AndroidScreenshotRecords.ScreenshotPath(1),
            "requested",
            1080,
            2404,
            245_000,
            new string('a', 64),
            "sRGB IEC61966-2.1",
            false,
            5_000_002,
            5_000_002_000_000,
            5_000_002_020_000,
            5_000_002_120_000,
            null,
            2 * Second);
        return JsonNode.Parse(JsonSerializer.Serialize(screenshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
    }

    private static IReadOnlyList<EventValidationIssue> Validate(JsonObject payload)
    {
        var descriptor = CollectorDescriptor.Create(
            AndroidScreenshotCollector.Type,
            nameof(AndroidScreenshotCollector),
            "1.0.0",
            [AndroidScreenshotRecords.Channel],
            "companion-accessibility-service");
        var record = RecorderEventFactory.Create(
            "session-1",
            descriptor,
            AndroidScreenshotRecords.Channel,
            1,
            2 * Second,
            AndroidScreenshotRecords.ScreenshotEventType,
            JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone(),
            []);
        return new EventRecordValidator("session-1").Validate(record);
    }

    private static byte[] Png(int width, int height, byte shade = 100)
    {
        var rgb = new byte[width * height * 3];
        Array.Fill(rgb, shade);
        return PngWriter.Encode(width, height, rgb);
    }

    /// <summary>
    /// A screenshot line as Screenshots.java writes it, with the "index"
    /// RecorderLink.java puts first: asked for at a session time, as the fake
    /// phone clock maps it, answered 20 ms later with Android's time 5 ms
    /// after the asking, and written as PNG 100 ms after the answer.
    /// </summary>
    private static string ScreenshotLine(
        long index,
        string reason,
        long sessionRequested,
        byte[] png,
        long? timestampMillis = null,
        long lastChange = 1_000,
        int? width = null)
    {
        var requested = sessionRequested + PhoneOffset;
        var size = AndroidScreenshotCollector.PngSize(png)!.Value;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"index\":{index},\"type\":\"screenshot\",\"reason\":\"{reason}\"," +
            $"\"timestampMillis\":{timestampMillis ?? (requested + (5 * Millisecond)) / Millisecond}," +
            $"\"requested\":{requested},\"captured\":{requested + (20 * Millisecond)},\"encoded\":{requested + (120 * Millisecond)}," +
            $"\"lastChangeMillis\":{lastChange},\"width\":{width ?? size.Width},\"height\":{size.Height}," +
            $"\"colourSpace\":\"sRGB IEC61966-2.1\",\"converted\":false,\"png\":\"{Convert.ToBase64String(png)}\"}}");
    }

    private static string RefusalLine(long index, int code, long sessionRequested)
    {
        var requested = sessionRequested + PhoneOffset;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"index\":{index},\"type\":\"screenshot-failed\",\"reason\":\"settled\",\"code\":{code}," +
            $"\"requested\":{requested},\"failed\":{requested + (4 * Millisecond)}}}");
    }

    // Read by the recorder 30 ms after the PNG was written.
    private static ReceivedScreenshot Received(string line, long sessionRequested) =>
        new(line, sessionRequested + (150 * Millisecond));

    private static async Task WaitUntilAsync(Func<bool> condition, Func<string?> state)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out; the state is: {state()}");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeScreenshots(bool connected = true) : IPhoneScreenshots
    {
        private readonly Channel<ReceivedScreenshot> _channel = Channel.CreateUnbounded<ReceivedScreenshot>();

        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Send(ReceivedScreenshot received) => Assert.True(_channel.Writer.TryWrite(received));

        public void End() => _channel.Writer.Complete();

        public Task<ChannelReader<ReceivedScreenshot>?> RequestScreenshotsAsync(CancellationToken cancellation)
        {
            Asked.TrySetResult();
            return Task.FromResult(connected ? _channel.Reader : null);
        }
    }

    private sealed class FakePhoneClock(bool sampled) : IPhoneClock
    {
        public const long Uncertainty = 1_500_000;

        public Task<bool> FirstSample { get; } = Task.FromResult(sampled);

        public PhoneTimeMapping? Map(long phoneNanoseconds) =>
            sampled ? new PhoneTimeMapping(phoneNanoseconds - PhoneOffset, Uncertainty, 1) : null;
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
