using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

// Evidence ingest rejects unmapped members, so every browser.interaction shape
// the bridge writes must map onto a contract or the renderer's whole stream is
// refused.
public sealed class BrowserInteractionPayloadIngestTests
{
    public static TheoryData<string, string> InteractionRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserInteractionPayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InteractionRecords))]
    public void AcceptsEveryInteractionRecordTheBridgeWrites(
        string eventType,
        string json)
    {
        using var document = JsonDocument.Parse(json);

        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Interaction,
            eventType,
            document.RootElement);
    }

    [Theory]
    [MemberData(nameof(InteractionRecords))]
    public void RejectsAnUndeclaredMemberInEveryInteractionRecord(
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Interaction,
                eventType,
                document.RootElement));
    }

    [Fact]
    public void ReadsInteractionStateAsWritten()
    {
        using var focus = JsonDocument.Parse(BrowserInteractionPayloads.ScriptFocusChanged);
        var focusPayload = BrowserProtocol.Deserialize<BrowserFocusChangedPayload>(
            focus.RootElement);
        Assert.Equal(44, focusPayload.FocusedNodeId);
        Assert.Equal(52, focusPayload.ActiveDescendantNodeId);
        Assert.Equal(BrowserFocusOutcomes.Focused, focusPayload.Outcome);
        Assert.Null(focusPayload.FocusVisible);

        using var cleared = JsonDocument.Parse(BrowserInteractionPayloads.SelectionCleared);
        var selectionPayload = BrowserProtocol.Deserialize<BrowserSelectionChangedPayload>(
            cleared.RootElement);
        Assert.Null(selectionPayload.AnchorNodeId);
        Assert.Null(selectionPayload.TextControlSelectionDirection);

        using var value = JsonDocument.Parse(BrowserInteractionPayloads.ScriptSetValue);
        var valuePayload = BrowserProtocol.Deserialize<BrowserTextControlValueChangedPayload>(
            value.RootElement);
        Assert.Equal("line one\nline two", valuePayload.Value);
        Assert.Equal("value-set", valuePayload.Source);
        Assert.Equal("world-0", valuePayload.Context.ExecutionWorldId);
    }
}
