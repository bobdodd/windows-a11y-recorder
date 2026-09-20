using System.Text.Json;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

// Evidence ingest rejects unmapped members, so a payload field the bridge emits
// without a matching contract property throws inside the receive loop. The
// receiver treats that as a rejected connection and closes the pipe, which
// silently costs the rest of that renderer's evidence for the whole session
// rather than failing one record. Protocol 0.16 renamed a transition field and
// added three checkpoint completion fields, and this shape of drift went
// undetected because the receiver test that exercises a live pipe does not run
// on every platform. These fixtures mirror what the bridge writes, so a
// contract that falls behind the bridge fails here instead of in a capture.
public sealed class BrowserDomPayloadIngestTests
{
    private const string ContextJson = """
        {
          "browserInstanceId": "browser-instance-1",
          "processId": 3440,
          "frameId": "frame-4",
          "documentId": "dom-document-19",
          "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
        }
        """;

    [Fact]
    public void AcceptsDomCheckpointStartedAsWritten()
    {
        Accept(
            BrowserEvidenceEventTypes.DomCheckpointStarted,
            $$"""
            {
              "context": {{ContextJson}},
              "checkpointId": "dom-checkpoint-7",
              "reason": "post-mutation",
              "maximumNodes": 512
            }
            """);
    }

    [Fact]
    public void AcceptsDomCheckpointNodeAsWritten()
    {
        Accept(
            BrowserEvidenceEventTypes.DomCheckpointNode,
            $$"""
            {
              "context": {{ContextJson}},
              "checkpointId": "dom-checkpoint-7",
              "nodeIndex": 4,
              "nodeId": 91,
              "parentNodeId": 88,
              "nodeType": "element",
              "nodeName": "button"
            }
            """);
    }

    [Fact]
    public void AcceptsDomCheckpointNodeAttributeAsWritten()
    {
        Accept(
            BrowserEvidenceEventTypes.DomCheckpointNodeAttribute,
            $$"""
            {
              "context": {{ContextJson}},
              "checkpointId": "dom-checkpoint-7",
              "nodeId": 91,
              "attributeIndex": 2,
              "attributeNamespace": null,
              "attributeName": "aria-expanded",
              "attributeValue": "true",
              "attributeValueLength": 4,
              "attributeValueTruncated": false,
              "maximumValueLength": 4096
            }
            """);
    }

    [Theory]
    [InlineData(0, "null", "null")]
    [InlineData(6, "\"dom-transition-1\"", "\"dom-transition-6\"")]
    public void AcceptsDomCheckpointCompletedAsWritten(
        int coveredTransitionCount,
        string coveredTransitionFirstId,
        string coveredTransitionLastId)
    {
        Accept(
            BrowserEvidenceEventTypes.DomCheckpointCompleted,
            $$"""
            {
              "context": {{ContextJson}},
              "checkpointId": "dom-checkpoint-7",
              "reason": "post-mutation",
              "nodeCount": 42,
              "truncated": false,
              "maximumNodes": 512,
              "attributeCount": 18,
              "attributesTruncated": false,
              "maximumAttributesPerNode": 64,
              "maximumValueLength": 4096,
              "coveredTransitionCount": {{coveredTransitionCount}},
              "coveredTransitionFirstId": {{coveredTransitionFirstId}},
              "coveredTransitionLastId": {{coveredTransitionLastId}}
            }
            """);
    }

    [Fact]
    public void AcceptsDomAttributeChangedAsWritten()
    {
        Accept(
            BrowserEvidenceEventTypes.DomAttributeChanged,
            $$"""
            {
              "context": {{ContextJson}},
              "transitionId": "dom-transition-3",
              "nodeId": 91,
              "nodeName": "button",
              "attributeNamespace": null,
              "attributeName": "aria-expanded",
              "changeType": "modified",
              "attributeValue": "true",
              "attributeValueLength": 4,
              "attributeValueTruncated": false,
              "previousAttributeValue": "false",
              "previousAttributeValueLength": 5,
              "previousAttributeValueTruncated": false,
              "maximumValueLength": 4096
            }
            """);
    }

    [Fact]
    public void AcceptsDomCharacterDataChangedAsWritten()
    {
        Accept(
            BrowserEvidenceEventTypes.DomCharacterDataChanged,
            $$"""
            {
              "context": {{ContextJson}},
              "transitionId": "dom-transition-4",
              "nodeId": 104,
              "parentNodeId": 103,
              "nodeType": "text",
              "text": "Live region after.",
              "textLength": 18,
              "textTruncated": false,
              "previousText": "Live region before.",
              "previousTextLength": 19,
              "previousTextTruncated": false,
              "maximumValueLength": 4096
            }
            """);
    }

    [Fact]
    public void RejectsATransitionFieldNoContractMaps()
    {
        var payload = $$"""
            {
              "context": {{ContextJson}},
              "transitionId": "dom-transition-3",
              "checkpointId": "dom-checkpoint-7",
              "nodeId": 91,
              "nodeName": "button",
              "attributeNamespace": null,
              "attributeName": "aria-expanded",
              "changeType": "modified",
              "attributeValue": "true",
              "attributeValueLength": 4,
              "attributeValueTruncated": false,
              "previousAttributeValue": "false",
              "previousAttributeValueLength": 5,
              "previousAttributeValueTruncated": false,
              "maximumValueLength": 4096
            }
            """;

        using var document = JsonDocument.Parse(payload);
        Assert.ThrowsAny<JsonException>(() => BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Dom,
            BrowserEvidenceEventTypes.DomAttributeChanged,
            document.RootElement));
    }

    private static void Accept(string eventType, string payload)
    {
        using var document = JsonDocument.Parse(payload);
        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Dom,
            eventType,
            document.RootElement);
    }
}
