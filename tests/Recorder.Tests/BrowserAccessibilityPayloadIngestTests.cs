using System.Text.Json;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

// These fixtures mirror the native bridge exactly. Browser payload ingest
// rejects unmapped members, so every accessibility record shape must be tested
// before a Chromium build is allowed to become the reference build.
public sealed class BrowserAccessibilityPayloadIngestTests
{
    private const string ContextJson = """
        {
          "browserInstanceId": "browser-instance-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": null,
          "executionWorldId": null,
          "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
        }
        """;

    [Fact]
    public void AcceptsAccessibilityCheckpointStartedAsWritten()
    {
        Accept(
            BrowserEvidenceEventTypes.AccessibilityCheckpointStarted,
            $$"""
            {
              "context": {{ContextJson}},
              "checkpointId": "accessibility-checkpoint-7",
              "reason": "renderer-serialization",
              "maximumNodes": 100000,
              "updateCount": 2,
              "eventCount": 3
            }
            """);
    }

    [Fact]
    public void AcceptsAccessibilityCheckpointNodeAsWritten()
    {
        Accept(
            BrowserEvidenceEventTypes.AccessibilityCheckpointNode,
            $$"""
            {
              "context": {{ContextJson}},
              "checkpointId": "accessibility-checkpoint-7",
              "nodeIndex": 4,
              "accessibilityNodeId": 91,
              "parentAccessibilityNodeId": 88,
              "domNodeId": 42,
              "role": 9,
              "roleName": "button",
              "name": "Save",
              "description": "Saves the form",
              "serializedProperties": "id=91 button name=Save",
              "focused": true
            }
            """);
    }

    [Fact]
    public void AcceptsAccessibilityCheckpointCompletedAsWritten()
    {
        Accept(
            BrowserEvidenceEventTypes.AccessibilityCheckpointCompleted,
            $$"""
            {
              "context": {{ContextJson}},
              "checkpointId": "accessibility-checkpoint-7",
              "reason": "renderer-serialization",
              "nodeCount": 42,
              "truncated": false,
              "maximumNodes": 100000,
              "updateCount": 2,
              "eventCount": 3
            }
            """);
    }

    private static void Accept(string eventType, string payload)
    {
        using var document = JsonDocument.Parse(payload);
        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Accessibility,
            eventType,
            document.RootElement);
    }
}
