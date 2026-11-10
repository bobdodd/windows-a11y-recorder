using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 7 of docs/architecture/android-implementation-plan.md, step 7a: the
// phone's accessibility events, as the companion app sends them
// (AccessibilityEventJson.java), read and placed on the session clock with
// a fake companion connection and a fake phone clock.
public sealed class AndroidAccessibilityTests
{
    private const string Serial = "58191FDCQ00ABZ";

    // The phone clock in the fake is this far ahead of the session clock,
    // a whole number of milliseconds so that event times map exactly.
    private const long PhoneOffset = 5_000_000_000_000;

    private const long Second = 1_000_000_000;

    private const long Millisecond = 1_000_000;

    /// <summary>
    /// An event line as AccessibilityEventJson.java writes it on Android 17,
    /// with every field, an extra record and its source, and the "index"
    /// RecorderLink.java adds.
    /// </summary>
    internal static string EventLine(
        long index = 1,
        string typeName = "TYPE_VIEW_ACCESSIBILITY_FOCUSED",
        long eventTimeMilliseconds = 5_002_000,
        long receivedPhoneNanoseconds = 5_002_000_400_000,
        string text = "Network & internet") =>
        "{\"type\":\"accessibility-event\",\"eventType\":32768,\"eventTypeName\":\"" + typeName + "\"," +
        "\"eventTimeMilliseconds\":" + eventTimeMilliseconds.ToString(CultureInfo.InvariantCulture) + "," +
        "\"receivedPhoneNanoseconds\":" + receivedPhoneNanoseconds.ToString(CultureInfo.InvariantCulture) + "," +
        "\"packageName\":\"com.android.settings\",\"className\":\"android.widget.LinearLayout\",\"windowId\":512,\"displayId\":0," +
        "\"text\":[" + JsonSerializer.Serialize(text) + ",\"\"],\"contentDescription\":null,\"beforeText\":null," +
        "\"contentChangeTypes\":3,\"windowChanges\":0,\"speechStateChangeTypes\":0,\"action\":0,\"movementGranularity\":0," +
        "\"fromIndex\":-1,\"toIndex\":-1,\"addedCount\":-1,\"removedCount\":-1,\"itemCount\":-1,\"currentItemIndex\":-1," +
        "\"scrollX\":0,\"scrollY\":0,\"maxScrollX\":0,\"maxScrollY\":0," +
        "\"checked\":false,\"enabled\":true,\"password\":false,\"fullScreen\":false,\"scrollable\":false," +
        "\"scrollDeltaX\":-1,\"scrollDeltaY\":-1,\"accessibilityDataSensitive\":false,\"parcelableDataClass\":null," +
        "\"records\":[{\"className\":\"android.widget.TextView\",\"windowId\":512,\"text\":[\"Wi-Fi\"],\"contentDescription\":null,\"beforeText\":null," +
        "\"fromIndex\":2,\"toIndex\":4,\"addedCount\":-1,\"removedCount\":-1,\"itemCount\":12,\"currentItemIndex\":-1," +
        "\"scrollX\":0,\"scrollY\":340,\"maxScrollX\":0,\"maxScrollY\":1200," +
        "\"checked\":false,\"enabled\":true,\"password\":false,\"fullScreen\":false,\"scrollable\":true}]," +
        "\"source\":{\"windowId\":512,\"className\":\"android.widget.LinearLayout\",\"packageName\":\"com.android.settings\"," +
        "\"viewId\":\"com.android.settings:id/item\",\"uniqueId\":null,\"text\":null,\"contentDescription\":null," +
        "\"stateDescription\":\"On\",\"hint\":null,\"paneTitle\":null,\"tooltip\":null,\"error\":null,\"containerTitle\":null," +
        "\"supplementalDescription\":null,\"bounds\":{\"left\":0,\"top\":420,\"right\":1080,\"bottom\":620}," +
        "\"childCount\":2,\"drawingOrder\":3,\"checked\":\"partial\",\"expandedState\":0," +
        "\"importantForAccessibility\":true,\"visibleToUser\":true,\"accessibilityFocused\":true,\"focused\":false," +
        "\"focusable\":true,\"screenReaderFocusable\":false,\"selected\":false,\"checkable\":false,\"enabled\":true," +
        "\"clickable\":true,\"longClickable\":false,\"contextClickable\":false,\"scrollable\":false,\"editable\":false," +
        "\"password\":false,\"heading\":false,\"showingHint\":false,\"multiLine\":false,\"dismissable\":false," +
        "\"contentInvalid\":false,\"textEntryKey\":false,\"fieldRequired\":false,\"inputType\":0,\"maxTextLength\":-1," +
        "\"liveRegion\":0,\"textSelectionStart\":-1,\"textSelectionEnd\":-1," +
        "\"actions\":[{\"id\":16,\"label\":null},{\"id\":2131362000,\"label\":\"Turn off\"}]," +
        "\"collectionInfo\":null,\"collectionItemInfo\":{\"rowIndex\":3,\"rowSpan\":1,\"columnIndex\":0,\"columnSpan\":1," +
        "\"selected\":false,\"rowTitle\":null,\"columnTitle\":null}," +
        "\"rangeInfo\":{\"type\":0,\"min\":0.0,\"max\":100.0,\"current\":null}}," +
        "\"handledPhoneNanoseconds\":" + (receivedPhoneNanoseconds + 300_000).ToString(CultureInfo.InvariantCulture) + "," +
        "\"index\":" + index.ToString(CultureInfo.InvariantCulture) + "}";

    // The protocol

    [Fact]
    public void Protocol_ReadsEveryFieldOfAnEvent()
    {
        var message = Assert.IsType<CompanionAccessibilityEvent>(CompanionProtocol.Parse(EventLine(index: 7)));

        Assert.Null(message.Problem);
        var read = message.Event!;
        Assert.Equal(7, read.Index);
        Assert.Equal(32768, read.EventType);
        Assert.Equal("TYPE_VIEW_ACCESSIBILITY_FOCUSED", read.EventTypeName);
        Assert.Equal(5_002_000, read.EventTimeMilliseconds);
        Assert.Equal(5_002_000_400_000, read.ReceivedPhoneNanoseconds);
        Assert.Equal(5_002_000_700_000, read.HandledPhoneNanoseconds);
        Assert.Equal("com.android.settings", read.PackageName);
        Assert.Equal(512, read.WindowId);
        Assert.Equal(0, read.DisplayId);
        Assert.Equal(["Network & internet", ""], read.Text);
        Assert.Null(read.ContentDescription);
        Assert.Equal(3, read.ContentChangeTypes);
        Assert.Equal(-1, read.FromIndex);
        Assert.Equal(-1, read.ScrollDeltaY);
        Assert.True(read.Enabled);
        Assert.False(read.AccessibilityDataSensitive);

        var record = Assert.Single(read.Records);
        Assert.Equal(["Wi-Fi"], record.Text);
        Assert.Equal(12, record.ItemCount);
        Assert.Equal(1200, record.MaxScrollY);
        Assert.True(record.Scrollable);

        var source = read.Source!;
        Assert.Equal("com.android.settings:id/item", source.ViewId);
        Assert.Equal("On", source.StateDescription);
        Assert.Equal(new AndroidNodeBounds(0, 420, 1080, 620), source.Bounds);
        Assert.Equal("partial", source.Checked);
        Assert.Equal(0, source.ExpandedState);
        Assert.True(source.AccessibilityFocused);
        Assert.False(source.FieldRequired);
        Assert.Equal(-1, source.MaxTextLength);
        Assert.Equal([new AndroidNodeAction(16, null), new AndroidNodeAction(2131362000, "Turn off")], source.Actions);
        Assert.Null(source.CollectionInfo);
        Assert.Equal(new AndroidCollectionItemInfo(3, 1, 0, 1, false, null, null), source.CollectionItemInfo);
        Assert.Equal(new AndroidRangeInfo(0, 0, 100, null), source.RangeInfo);
    }

    [Fact]
    public void Protocol_AnEventItCannotRead_IsAProblemNotAFailure()
    {
        // One bad event must not end the connection, so it is not a
        // FormatException.
        var message = Assert.IsType<CompanionAccessibilityEvent>(
            CompanionProtocol.Parse("{\"type\":\"accessibility-event\",\"index\":\"seven\"}"));

        Assert.Null(message.Event);
        Assert.False(string.IsNullOrWhiteSpace(message.Problem));
    }

    [Fact]
    public void Names_GivesEachBitSet_AndAnUnknownOneByValue()
    {
        Assert.Equal(
            ["SUBTREE", "TEXT", "0x20000"],
            AndroidAccessibilityRecords.Names(0x20003, AndroidAccessibilityRecords.ContentChangeTypes));
        Assert.Equal(
            ["ADDED", "ACCESSIBILITY_FOCUSED"],
            AndroidAccessibilityRecords.Names(0x81, AndroidAccessibilityRecords.WindowChanges));
        Assert.Empty(AndroidAccessibilityRecords.Names(0, AndroidAccessibilityRecords.WindowChanges));
    }

    [Fact]
    public void Describe_NamesTheItemByItsWords_OrItsClass()
    {
        var read = Read(EventLine());
        Assert.Equal("\"Network & internet\"", AndroidAccessibilityCollector.Describe(read));
        Assert.Equal(
            "\"On the left\"",
            AndroidAccessibilityCollector.Describe(read with { Source = read.Source! with { ContentDescription = "On  the\nleft" } }));
        Assert.Equal(
            "an item with no text (LinearLayout)",
            AndroidAccessibilityCollector.Describe(read with { Text = [""] }));
        var longText = new string('a', 80);
        Assert.Equal(
            "\"" + new string('a', AndroidAccessibilityCollector.StatusTextLength - 3) + "...\"",
            AndroidAccessibilityCollector.Describe(read with { Text = [longText] }));
    }

    // The collector

    [Fact]
    public async Task Collector_PlacesEachEventByThePhoneClock_AndCountsThem()
    {
        var events = new FakeEvents();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityCollector(Serial, events, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await events.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // From before the start, so not kept.
        events.Send(Event(1, AndroidAccessibilityRecords.ContentChangedName, Second / 2));
        events.Send(Event(2, AndroidAccessibilityRecords.AccessibilityFocusedName, 2 * Second, "Wi-Fi"));
        events.Send(Event(3, AndroidAccessibilityRecords.ContentChangedName, 2 * Second + 100 * Millisecond));
        events.Send(Event(4, AndroidAccessibilityRecords.ContentChangedName, 2 * Second + 500 * Millisecond));
        events.Send(Event(5, AndroidAccessibilityRecords.ContentChangedName, 3 * Second));
        events.Send(Event(6, AndroidAccessibilityRecords.WindowStateChangedName, 3 * Second + 500 * Millisecond));
        // No event time, so placed by when the service received it.
        events.Send(Event(7, AndroidAccessibilityRecords.AccessibilityFocusedName, 4 * Second, "Network & internet", timed: false));
        events.Send(Event(8, AndroidAccessibilityRecords.ContentChangedName, 4 * Second + 200 * Millisecond));
        events.Send(new ReceivedAccessibilityEvent(null, 5 * Second, "'s' is an invalid start of a value"));
        const string Expected =
            "Accessibility events: 7, including 4 content changes (at most 3 in one second), 2 accessibility focus events " +
            "and 1 window change, latest accessibility focus on \"Network & internet\", 1 could not be read, " +
            "the app took a median 0.3 ms to read each, median delay from phone to recorder 20.0 ms.";
        await WaitUntilAsync(() => collector.Summary == Expected, () => collector.Summary);
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        Assert.Null(collector.HealthReason);
        var records = sink.Records.ToList();
        var validator = new EventRecordValidator("session-1");
        foreach (var record in records)
        {
            Assert.Empty(validator.Validate(record));
        }

        var kept = records.Where(record => record.EventType == AndroidAccessibilityRecords.EventType).ToList();
        Assert.Equal([2L, 3, 4, 5, 6, 7, 8], kept.Select(record => record.Payload.GetProperty("index").GetInt64()));
        Assert.All(kept, record => Assert.Equal(AndroidAccessibilityRecords.EventsChannel, record.Channel));
        Assert.All(kept, record => Assert.Equal(AndroidCompanionRecords.FittedClockMapping, record.ClockMappingId));
        Assert.All(kept, record => Assert.Equal(AndroidCompanionRecords.ClockDomain, record.NativeTimestamp!.Domain));

        var focus = kept[0];
        Assert.Equal(2 * Second, focus.MonotonicNanoseconds);
        Assert.Equal(2 * Second + PhoneOffset, focus.NativeTimestamp!.Value);
        Assert.Equal(FakePhoneClock.Uncertainty + AndroidAccessibilityCollector.MillisecondUncertainty, focus.TimestampUncertaintyNanoseconds);
        Assert.Equal([AndroidAccessibilityRecords.PhoneEventTimeFlag], focus.QualityFlags);
        Assert.Equal(2 * Second + 20 * Millisecond, focus.Payload.GetProperty("receivedAtNanoseconds").GetInt64());
        Assert.Equal(["SUBTREE", "TEXT"], focus.Payload.GetProperty("contentChangeTypeNames").EnumerateArray().Select(name => name.GetString()));
        Assert.Equal("partial", focus.Payload.GetProperty("source").GetProperty("checked").GetString());

        var untimed = kept[5];
        Assert.Equal(4 * Second, untimed.MonotonicNanoseconds);
        Assert.Equal(FakePhoneClock.Uncertainty, untimed.TimestampUncertaintyNanoseconds);
        Assert.Equal([AndroidAccessibilityRecords.PhoneReceiptTimeFlag], untimed.QualityFlags);

        var unreadable = Assert.Single(records, record => record.EventType == AndroidAccessibilityRecords.UnavailableEventType);
        Assert.Equal(5 * Second, unreadable.MonotonicNanoseconds);
        Assert.Contains(AndroidScreenRecords.ReceiptTimeFlag, unreadable.QualityFlags);
        Assert.Equal(
            "An accessibility event from the phone could not be read: 's' is an invalid start of a value.",
            unreadable.Payload.GetProperty("reason").GetString());
        Assert.False(unreadable.Payload.GetProperty("connectionLost").GetBoolean());
    }

    [Fact]
    public async Task Collector_AnEarlierTime_IsHeldAtTheLastOne()
    {
        var events = new FakeEvents();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityCollector(Serial, events, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await events.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        events.Send(Event(1, AndroidAccessibilityRecords.ContentChangedName, 3 * Second));
        events.Send(Event(2, AndroidAccessibilityRecords.ContentChangedName, 2 * Second));
        await WaitUntilAsync(() => collector.Summary?.StartsWith("Accessibility events: 2,", StringComparison.Ordinal) == true, () => collector.Summary);
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        var kept = sink.Records.Where(record => record.EventType == AndroidAccessibilityRecords.EventType).ToList();
        Assert.Equal([3 * Second, 3 * Second], kept.Select(record => record.MonotonicNanoseconds));
        Assert.Equal([AndroidAccessibilityRecords.PhoneEventTimeFlag, AndroidCompanionRecords.FitHeldFlag], kept[1].QualityFlags);
        // The phone's own time is kept.
        Assert.Equal(2 * Second + PhoneOffset, kept[1].NativeTimestamp!.Value);
    }

    [Fact]
    public async Task Collector_WithoutAPhoneClock_PlacesEventsAsTheyArrive()
    {
        var events = new FakeEvents();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityCollector(Serial, events, clock: null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await events.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => collector.Summary == "Accessibility events: none yet.", () => collector.Summary);

        events.Send(Event(1, AndroidAccessibilityRecords.AccessibilityFocusedName, 2 * Second, "Wi-Fi"));
        await WaitUntilAsync(() => collector.Summary?.StartsWith("Accessibility events: 1,", StringComparison.Ordinal) == true, () => collector.Summary);
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(
            "Accessibility events: 1, including 0 content changes (at most 0 in one second), 1 accessibility focus event " +
            "and 0 window changes, latest accessibility focus on \"Wi-Fi\", the app took a median 0.3 ms to read each, " +
            "placed by the time each event arrived, as there is no phone clock.",
            collector.Summary);
        var record = Assert.Single(sink.Records, record => record.EventType == AndroidAccessibilityRecords.EventType);
        Assert.Equal(2 * Second + 20 * Millisecond, record.MonotonicNanoseconds);
        Assert.Equal([AndroidScreenRecords.ReceiptTimeFlag], record.QualityFlags);
        Assert.Null(record.TimestampUncertaintyNanoseconds);
        Assert.Empty(new EventRecordValidator("session-1").Validate(record));
    }

    [Fact]
    public async Task Collector_NoCompanionConnection_RecordsWhyAndGoesOn()
    {
        var events = new FakeEvents(connected: false);
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityCollector(Serial, events, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded, () => collector.HealthState.ToString());
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(CollectorLifecycleState.Stopped, collector.LifecycleState);
        Assert.Null(collector.Summary);
        Assert.Equal(
            "The phone's accessibility events could not be recorded: the companion app is not connected, so they could not be asked for. " +
            "The android.companion line says why. The recording goes on without them.",
            collector.HealthReason);
        var record = Assert.Single(sink.Records, record => record.EventType == AndroidAccessibilityRecords.UnavailableEventType);
        Assert.Equal(collector.HealthReason, record.Payload.GetProperty("reason").GetString());
        Assert.Equal(Serial, record.Payload.GetProperty("serial").GetString());
        Assert.Empty(new EventRecordValidator("session-1").Validate(record));
    }

    [Fact]
    public async Task Collector_ConnectionEnds_RecordsItAndKeepsWhatCame()
    {
        var events = new FakeEvents();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityCollector(Serial, events, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await events.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        events.Send(Event(1, AndroidAccessibilityRecords.WindowsChangedName, 2 * Second));
        events.End();
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded, () => collector.HealthState.ToString());
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.StartsWith("Accessibility events: 1, including 0 content changes (at most 0 in one second), 0 accessibility focus events and 1 window change,", collector.Summary);
        Assert.Equal(
            "The phone's accessibility events stopped being recorded during the recording: the connection to the companion app ended: " +
            "the phone was unplugged, adb stopped, or the companion app was turned off. The recording goes on without them.",
            collector.HealthReason);
        var lost = Assert.Single(sink.Records, record => record.EventType == AndroidAccessibilityRecords.UnavailableEventType);
        Assert.True(lost.Payload.GetProperty("connectionLost").GetBoolean());
        Assert.Single(sink.Records, record => record.EventType == AndroidAccessibilityRecords.EventType);
    }

    [Fact]
    public async Task Collector_EventsWaitingAtStop_AreKept()
    {
        var events = new FakeEvents();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityCollector(Serial, events, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await events.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        for (var index = 1; index <= 50; index++)
        {
            events.Send(Event(index, AndroidAccessibilityRecords.ContentChangedName, Second + index * 10 * Millisecond));
        }

        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(50, sink.Records.Count(record => record.EventType == AndroidAccessibilityRecords.EventType));
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
    }

    // The validator

    [Theory]
    [InlineData("\"checked\":\"partial\"", "\"checked\":\"maybe\"")]
    [InlineData("\"bounds\":{\"left\":0,", "\"bounds\":{\"left\":\"0\",")]
    [InlineData("{\"id\":16,\"label\":null}", "{\"id\":16,\"label\":3}")]
    [InlineData("\"rangeInfo\":{\"type\":0,", "\"rangeInfo\":{\"type\":-1,")]
    [InlineData("\"fromIndex\":2,", "\"fromIndex\":null,")]
    [InlineData("\"index\":1,", "\"index\":0,")]
    public void Validator_RejectsABadEvent(string good, string bad)
    {
        var payload = Payload(EventLine());
        Assert.Empty(Validate(payload));
        Assert.Contains(good, payload, StringComparison.Ordinal);

        Assert.NotEmpty(Validate(payload.Replace(good, bad, StringComparison.Ordinal)));
    }

    [Fact]
    public void Validator_RejectsAnEventReadBeforeItArrived()
    {
        var payload = Payload(EventLine()).Replace("\"handledPhoneNanoseconds\":5002000700000", "\"handledPhoneNanoseconds\":5002000000000", StringComparison.Ordinal);

        Assert.Contains(Validate(payload), issue => issue.Code == "android-accessibility-event-handled-early");
    }

    // The payload the collector writes for an event line.
    private static string Payload(string line)
    {
        var read = Read(line);
        return JsonSerializer.Serialize(
            read with
            {
                ContentChangeTypeNames = AndroidAccessibilityRecords.Names(read.ContentChangeTypes, AndroidAccessibilityRecords.ContentChangeTypes),
                ReceivedAtNanoseconds = 2 * Second
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static IReadOnlyList<EventValidationIssue> Validate(string payload)
    {
        var descriptor = CollectorDescriptor.Create(
            AndroidAccessibilityCollector.Type,
            nameof(AndroidAccessibilityCollector),
            "1.0.0",
            [AndroidAccessibilityRecords.EventsChannel],
            "companion-accessibility-service");
        var record = RecorderEventFactory.Create(
            "session-1",
            descriptor,
            AndroidAccessibilityRecords.EventsChannel,
            1,
            2 * Second,
            AndroidAccessibilityRecords.EventType,
            JsonDocument.Parse(payload).RootElement.Clone(),
            []);
        return new EventRecordValidator("session-1").Validate(record);
    }

    private static AndroidAccessibilityEvent Read(string line) =>
        Assert.IsType<CompanionAccessibilityEvent>(CompanionProtocol.Parse(line)).Event!;

    /// <summary>
    /// An event at a session time, as the fake phone clock maps it, read
    /// 0.3 ms after the service received it 0.4 ms after it happened, and
    /// read by the recorder 20 ms after it happened.
    /// </summary>
    private static ReceivedAccessibilityEvent Event(long index, string typeName, long sessionTime, string text = "", bool timed = true)
    {
        var phone = sessionTime + PhoneOffset;
        var read = Read(EventLine(
            index,
            typeName,
            eventTimeMilliseconds: timed ? phone / Millisecond : 0,
            receivedPhoneNanoseconds: timed ? phone + 400_000 : phone,
            text: text));
        return new ReceivedAccessibilityEvent(read, sessionTime + 20 * Millisecond);
    }

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

    private sealed class FakeEvents(bool connected = true) : IPhoneAccessibilityEvents
    {
        private readonly Channel<ReceivedAccessibilityEvent> _channel = Channel.CreateUnbounded<ReceivedAccessibilityEvent>();

        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Send(ReceivedAccessibilityEvent received) => Assert.True(_channel.Writer.TryWrite(received));

        public void End() => _channel.Writer.Complete();

        public Task<ChannelReader<ReceivedAccessibilityEvent>?> RequestAccessibilityEventsAsync(CancellationToken cancellation)
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
