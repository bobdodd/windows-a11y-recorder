using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

// Evidence ingest rejects unmapped members, so every browser.presentation shape
// the bridge writes must map onto a contract or the renderer's whole stream is
// refused.
public sealed class BrowserPresentationPayloadIngestTests
{
    public static TheoryData<string, string> PresentationRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserPresentationPayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PresentationRecords))]
    public void AcceptsEveryPresentationRecordTheBridgeWrites(
        string eventType,
        string json)
    {
        using var document = JsonDocument.Parse(json);

        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Presentation,
            eventType,
            document.RootElement);
    }

    [Theory]
    [MemberData(nameof(PresentationRecords))]
    public void RejectsAnUndeclaredMemberInEveryPresentationRecord(
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Presentation,
                eventType,
                document.RootElement));
    }

    [Fact]
    public void ReadsPresentationRecordsAsWritten()
    {
        using var request = JsonDocument.Parse(BrowserPresentationPayloads.QueuedRequest);
        var requestPayload = BrowserProtocol.Deserialize<BrowserPresentationRequestedPayload>(
            request.RootElement);
        Assert.Equal("presentation-request-7", requestPayload.RequestId);
        Assert.Equal("layout-checkpoint-12", requestPayload.LayoutCheckpointId);
        Assert.Equal("3:2", requestPayload.FrameSinkId);
        Assert.Equal(41, requestPayload.SourceFrameNumber);
        Assert.True(requestPayload.Queued);

        using var feedback = JsonDocument.Parse(BrowserPresentationPayloads.Feedback);
        var feedbackPayload = BrowserProtocol.Deserialize<BrowserPresentationFeedbackPayload>(
            feedback.RootElement);
        Assert.Equal("4294967295", feedbackPayload.FrameToken);
        Assert.Equal(["vsync", "hw-completion"], feedbackPayload.Flags);
        Assert.Equal("123456989012", feedbackPayload.PresentedTicks);
    }

    [Fact]
    public void AcceptsAPresentationOmission()
    {
        using var document = JsonDocument.Parse("""{"reason":"browser-evidence-write-failed","count":3}""");

        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Presentation,
            BrowserEvidenceEventTypes.Omission,
            document.RootElement);
    }
}
