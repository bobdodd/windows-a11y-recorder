using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// "Layout of a walked rendering update" (protocol 0.46): a walked update is
// presented through its checkpoint, and its own change set follows the
// checkpoint, so the presented state is cut after that change set.
public sealed class WalkedUpdateLayoutTests
{
    private const string Context =
        "{\"browserInstanceId\":\"b\",\"processId\":10,\"processType\":\"renderer\",\"documentId\":\"dom-document-1\",\"documentToken\":\"TOKEN\"}";

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static RecorderEvent Event(long time, string channel, string type, string payload, NativeTimestamp? native = null) => new()
    {
        SchemaVersion = RecorderEvent.CurrentSchemaVersion,
        EventId = "e" + time,
        EvidenceClass = EvidenceClasses.Observed,
        SessionId = "s",
        CollectorType = "c",
        CollectorInstanceId = "i",
        ProducerVersion = "p",
        Channel = channel,
        CaptureMethod = "m",
        Sequence = (ulong)time,
        MonotonicNanoseconds = time,
        ClockMappingId = "k",
        ObservedUtc = DateTimeOffset.UnixEpoch,
        EventType = type,
        Payload = J(payload),
        NativeTimestamp = native,
    };

    private static BrowserPresentedCheckpoint Presented(bool? checkpointUpdate, string changeSetCheckpoint = "layout-checkpoint-12")
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var key = 0L;
        void Add(RecorderEvent record) => builder.Add(++key, record);
        Add(Event(1_000_000, "browser.layout", "layout-checkpoint-completed",
            $$"""{"context":{{Context}},"checkpointId":"layout-checkpoint-12"}"""));
        Add(Event(1_000_100, "browser.presentation", "presentation-requested",
            $$"""{"context":{{Context}},"requestId":"presentation-request-1","layoutCheckpointId":"layout-checkpoint-12","layoutChangeSetId":null}"""));
        var update = checkpointUpdate is { } flag ? $",\"checkpointUpdate\":{(flag ? "true" : "false")}" : "";
        Add(Event(1_200_000, "browser.layout", "layout-changes-started",
            $$"""{"context":{{Context}},"changeSetId":"layout-changes-62","layoutCheckpointId":"{{changeSetCheckpoint}}"{{update}}}"""));
        Add(Event(1_300_000, "browser.layout", "layout-changes-completed",
            $$"""{"context":{{Context}},"changeSetId":"layout-changes-62"}"""));
        Add(Event(2_100_000, "browser.presentation", "presentation-feedback",
            $$"""{"context":{{Context}},"requestId":"presentation-request-1","presentedTicks":"1014000"}""",
            new NativeTimestamp("chromium-monotonic", 1_000_000, "ticks")));
        return Assert.Single(builder.Build().PresentedCheckpoints);
    }

    [Fact]
    public void AWalkedUpdateIsCutAfterItsOwnChangeSet()
    {
        var presented = Presented(true);
        Assert.Equal(1_300_000, presented.CheckpointNanoseconds);
        // The presented time is unchanged: 14,000 ticks of 100 ns after the
        // feedback's 2.1 ms.
        Assert.Equal(3_500_000, presented.PresentedNanoseconds);
    }

    [Fact]
    public void AWalkedUpdateWithoutItsChangeSetIsCutAtTheCheckpoint()
    {
        Assert.Equal(1_000_000, Presented(false).CheckpointNanoseconds);
        // A recording before protocol 0.46 has no field and is read as before.
        Assert.Equal(1_000_000, Presented(null).CheckpointNanoseconds);
        // A change set of another checkpoint's update does not move the cut.
        Assert.Equal(1_000_000, Presented(true, "layout-checkpoint-11").CheckpointNanoseconds);
    }
}
