using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// The browser.compositor records of protocol 0.48 (slice 4b sub-step 1), as
// the bridge writes them, against the receiver's contracts and the record
// validator.
public sealed class BrowserCompositorRecordTests
{
    private const string DocumentContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": "dom-document-19",
          "executionWorldId": null,
          "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
        }
        """;

    private const string ProcessContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": null,
          "executionWorldId": null,
          "documentToken": null
        }
        """;

    private const string WidgetJson = """
        {
          "widgetKind": "frame",
          "frameSinkId": "3:2",
          "localRootFrameToken": "5D1A4C2B0E9F8A7B6C5D4E3F2A1B0C9D"
        }
        """;

    internal const string AnimationStarted = $$"""
        {
          "context": {{DocumentContextJson}},
          "nodeId": 44,
          "compositorAnimationId": 7,
          "keyframeModels": [
            {
              "keyframeModelId": 12,
              "targetProperty": "transform",
              "elementId": "1048583",
              "elementIdNamespace": "primary-transform"
            },
            {
              "keyframeModelId": 13,
              "targetProperty": "opacity",
              "elementId": "1048582",
              "elementIdNamespace": "primary-effect"
            }
          ]
        }
        """;

    internal const string AnimationEnded = $$"""
        {
          "context": {{DocumentContextJson}},
          "nodeId": 44,
          "compositorAnimationId": 7,
          "keyframeModelIds": [12, 13]
        }
        """;

    internal const string Frame = $$"""
        {
          "context": {{ProcessContextJson}},
          "layerTreeHostId": 2,
          "widget": {{WidgetJson}},
          "frameToken": "318",
          "sourceFrameNumber": 41,
          "beginFrameTicks": "1234567890",
          "beginFrameTimeTicksMicroseconds": "123456789",
          "highResolutionTicks": true,
          "changes": [
            {
              "elementId": "1048583",
              "property": "transform",
              "value": [1, 0, 0, 37.25, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
            },
            { "elementId": "1048582", "property": "opacity", "value": 0.4375 },
            {
              "elementId": "1048590",
              "property": "filter",
              "value": [
                { "type": "blur", "numbers": [2.5] },
                { "type": "drop-shadow", "numbers": [3, 1, 2, 0, 0, 0, 0.5] }
              ]
            },
            { "elementId": "1048601", "property": "scroll-offset", "value": { "x": 0, "y": 120.5 } },
            { "elementId": "1048594", "property": "backdrop-filter", "value": null }
          ]
        }
        """;

    internal const string FramePresented = $$"""
        {
          "context": {{ProcessContextJson}},
          "layerTreeHostId": 2,
          "widget": {{WidgetJson}},
          "frameToken": "318",
          "failed": false,
          "presentedTicks": "1234599999",
          "presentedTimeTicksMicroseconds": "123459999",
          "highResolutionTicks": true
        }
        """;

    public static TheoryData<string, string> CompositorRecords() => new()
    {
        { BrowserEvidenceEventTypes.CompositorAnimationStarted, AnimationStarted },
        { BrowserEvidenceEventTypes.CompositorAnimationEnded, AnimationEnded },
        { BrowserEvidenceEventTypes.CompositorFrame, Frame },
        { BrowserEvidenceEventTypes.CompositorFramePresented, FramePresented }
    };

    [Theory]
    [MemberData(nameof(CompositorRecords))]
    public void TheReceiverAcceptsEveryCompositorRecordTheBridgeWrites(string eventType, string json)
    {
        using var document = JsonDocument.Parse(json);

        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Compositor, eventType, document.RootElement);
    }

    [Theory]
    [MemberData(nameof(CompositorRecords))]
    public void TheReceiverRejectsAnUndeclaredMember(string eventType, string json)
    {
        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Compositor, eventType, document.RootElement));
    }

    [Theory]
    [MemberData(nameof(CompositorRecords))]
    public void TheValidatorAcceptsEveryCompositorRecord(string eventType, string json)
    {
        Assert.Empty(ValidateRecord(eventType, JsonNode.Parse(json)!));
    }

    [Fact]
    public void TheCompositorChannelIsBuiltIn()
    {
        Assert.True(EventPayloadValidator.IsBuiltInChannel(BrowserEvidenceChannels.Compositor));
        Assert.NotEmpty(ValidateRecord("compositor-unknown", JsonNode.Parse("{}")!));
    }

    [Fact]
    public void AFrameWithoutAChangeIsRejected()
    {
        var payload = JsonNode.Parse(Frame)!;
        payload["changes"] = new JsonArray();

        Assert.Contains(
            ValidateRecord(BrowserEvidenceEventTypes.CompositorFrame, payload),
            issue => issue.Code == "browser-compositor-frame-empty");
    }

    [Theory]
    [InlineData("transform", "[1, 0, 0, 1]")]
    [InlineData("opacity", "\"0.5\"")]
    [InlineData("scroll-offset", "{ \"x\": 1 }")]
    [InlineData("scroll-offset", "null")]
    [InlineData("filter", "[{ \"type\": \"glow\", \"numbers\": [1] }]")]
    [InlineData("filter", "[{ \"type\": \"blur\" }]")]
    public void AValueWithoutItsPropertysShapeIsRejected(string property, string value)
    {
        var payload = JsonNode.Parse(Frame)!;
        payload["changes"] = new JsonArray(new JsonObject
        {
            ["elementId"] = "1048583",
            ["property"] = property,
            ["value"] = JsonNode.Parse(value)
        });

        Assert.Contains(
            ValidateRecord(BrowserEvidenceEventTypes.CompositorFrame, payload),
            issue => issue.Code == "browser-compositor-value-invalid");
    }

    [Fact]
    public void AFailedPresentationHasNoTime()
    {
        var payload = JsonNode.Parse(FramePresented)!;
        payload["failed"] = true;

        Assert.Contains(
            ValidateRecord(BrowserEvidenceEventTypes.CompositorFramePresented, payload),
            issue => issue.Code == "browser-compositor-presentation-invalid");

        payload["presentedTicks"] = null;
        payload["presentedTimeTicksMicroseconds"] = null;
        Assert.Empty(ValidateRecord(BrowserEvidenceEventTypes.CompositorFramePresented, payload));
    }

    [Fact]
    public void AFrameBeforeItsWidgetIsKnownIsAccepted()
    {
        var payload = JsonNode.Parse(Frame)!;
        payload["widget"] = null;

        Assert.Empty(ValidateRecord(BrowserEvidenceEventTypes.CompositorFrame, payload));
    }

    [Fact]
    public void AnAnimationStartedWithoutADocumentOrModelsIsRejected()
    {
        var withoutDocument = JsonNode.Parse(AnimationStarted)!;
        withoutDocument["context"]!["documentId"] = null;
        Assert.NotEmpty(ValidateRecord(BrowserEvidenceEventTypes.CompositorAnimationStarted, withoutDocument));

        var withoutModels = JsonNode.Parse(AnimationStarted)!;
        withoutModels["keyframeModels"] = new JsonArray();
        Assert.Contains(
            ValidateRecord(BrowserEvidenceEventTypes.CompositorAnimationStarted, withoutModels),
            issue => issue.Code == "browser-compositor-keyframe-models-empty");

        var unknownProperty = JsonNode.Parse(AnimationStarted)!;
        unknownProperty["keyframeModels"]![0]!["targetProperty"] = "colour";
        Assert.NotEmpty(ValidateRecord(BrowserEvidenceEventTypes.CompositorAnimationStarted, unknownProperty));
    }

    [Fact]
    public void AnAnimationEndedWithoutModelIdsIsRejected()
    {
        var payload = JsonNode.Parse(AnimationEnded)!;
        payload["keyframeModelIds"] = new JsonArray();

        Assert.Contains(
            ValidateRecord(BrowserEvidenceEventTypes.CompositorAnimationEnded, payload),
            issue => issue.Code == "browser-compositor-keyframe-model-ids-invalid");
    }

    [Fact]
    public void TheReceiverReadsAFrameAsWritten()
    {
        using var document = JsonDocument.Parse(Frame);
        var frame = BrowserProtocol.Deserialize<BrowserCompositorFramePayload>(document.RootElement);

        Assert.Equal(2, frame.LayerTreeHostId);
        Assert.Equal("3:2", frame.Widget!.FrameSinkId);
        Assert.Equal(5, frame.Changes.Count);
        Assert.Equal(37.25, frame.Changes[0].Value[3].GetDouble());
        Assert.Equal(0.4375, frame.Changes[1].Value.GetDouble());
        Assert.Equal(JsonValueKind.Null, frame.Changes[4].Value.ValueKind);
    }

    private static IReadOnlyList<EventValidationIssue> ValidateRecord(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var descriptor = new CollectorDescriptor(
            "test.collector",
            "0123456789abcdef0123456789abcdef",
            "test.collector",
            "1.0",
            "1.0",
            ["test.events"],
            "test");
        var record = RecorderEventFactory.Create(
            "test-session",
            descriptor,
            BrowserEvidenceChannels.Compositor,
            0,
            100,
            eventType,
            document.RootElement.Clone());
        var validator = new EventRecordValidator("test-session");
        return validator.Validate(record).ToList();
    }
}
