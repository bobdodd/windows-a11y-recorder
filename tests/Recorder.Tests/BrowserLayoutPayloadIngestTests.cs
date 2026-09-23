using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

// Evidence ingest rejects unmapped members, so every browser.layout shape the
// bridge writes must map onto a contract or the renderer's whole stream is
// refused.
public sealed class BrowserLayoutPayloadIngestTests
{
    public static TheoryData<string, string> LayoutRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserLayoutPayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LayoutRecords))]
    public void AcceptsEveryLayoutRecordTheBridgeWrites(string eventType, string json)
    {
        using var document = JsonDocument.Parse(json);

        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Layout,
            eventType,
            document.RootElement);
    }

    [Theory]
    [MemberData(nameof(LayoutRecords))]
    public void RejectsAnUndeclaredMemberInEveryLayoutRecord(string eventType, string json)
    {
        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Layout,
                eventType,
                document.RootElement));
    }

    [Fact]
    public void ReadsLayoutStateAsWritten()
    {
        using var started = JsonDocument.Parse(BrowserLayoutPayloads.LaterCheckpointStarted);
        var startedPayload = BrowserProtocol.Deserialize<BrowserLayoutCheckpointStartedPayload>(
            started.RootElement);
        Assert.Equal("layout-checkpoint-1", startedPayload.PreviousCheckpointId);
        Assert.Equal(240.5, startedPayload.ScrollOffset.Y);
        Assert.Equal(1280, startedPayload.Viewport.Width);
        Assert.Equal(["display", "width", "color"], startedPayload.StyleProperties);

        using var element = JsonDocument.Parse(BrowserLayoutPayloads.ElementNode);
        var elementPayload = BrowserProtocol.Deserialize<BrowserLayoutCheckpointNodePayload>(
            element.RootElement);
        Assert.Equal(30.5, elementPayload.BoundingClientRect!.Y);
        Assert.Equal("120px", elementPayload.ComputedStyle!["width"]);

        using var missing = JsonDocument.Parse(BrowserLayoutPayloads.StyleValueMissingNode);
        var missingPayload = BrowserProtocol.Deserialize<BrowserLayoutCheckpointNodePayload>(
            missing.RootElement);
        Assert.True(missingPayload.DisplayLocked);
        Assert.Null(missingPayload.BoundingClientRect);
        Assert.True(missingPayload.ComputedStyle!.ContainsKey("width"));
        Assert.Null(missingPayload.ComputedStyle["width"]);

        using var text = JsonDocument.Parse(BrowserLayoutPayloads.TextNode);
        var textPayload = BrowserProtocol.Deserialize<BrowserLayoutCheckpointNodePayload>(
            text.RootElement);
        Assert.Null(textPayload.ComputedStyle);
    }
}
