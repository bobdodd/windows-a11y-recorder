using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

// Evidence ingest rejects unmapped members, so every browser.resources shape
// the bridge writes (protocol 0.40) must map onto a contract.
public sealed class BrowserResourcePayloadIngestTests
{
    public static TheoryData<string, string> ResourceRecords() => new()
    {
        { BrowserEvidenceEventTypes.FontFile, BrowserResourcePayloads.FontFile },
        { BrowserEvidenceEventTypes.ImageData, BrowserResourcePayloads.FontFile },
        { BrowserEvidenceEventTypes.FontFaceAdded, BrowserResourcePayloads.FaceAdded },
        { BrowserEvidenceEventTypes.FontFaceRemoved, BrowserResourcePayloads.FaceAdded },
        { BrowserEvidenceEventTypes.FontFaceLoaded, BrowserResourcePayloads.FaceLoaded },
        { BrowserEvidenceEventTypes.ImageResource, BrowserResourcePayloads.ImageResource },
        { BrowserEvidenceEventTypes.ImagePaintImage, BrowserResourcePayloads.ImagePaintImage },
        { BrowserEvidenceEventTypes.StyleSheetText, BrowserResourcePayloads.FontFile },
        { BrowserEvidenceEventTypes.StyleSheetResource, BrowserResourcePayloads.StyleSheetResource },
        { BrowserEvidenceEventTypes.StyleSheetsUpdated, BrowserResourcePayloads.StyleSheetsUpdated }
    };

    [Theory]
    [MemberData(nameof(ResourceRecords))]
    public void AcceptsEveryResourceRecordTheBridgeWrites(string eventType, string json)
    {
        using var document = JsonDocument.Parse(json);

        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Resources,
            eventType,
            document.RootElement);
    }

    [Theory]
    [MemberData(nameof(ResourceRecords))]
    public void RejectsAnUndeclaredMemberInEveryResourceRecord(string eventType, string json)
    {
        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Resources,
                eventType,
                document.RootElement));
    }

    [Fact]
    public void ReadsAFaceAsWritten()
    {
        using var document = JsonDocument.Parse(BrowserResourcePayloads.FaceLoaded);
        var face = BrowserProtocol.Deserialize<BrowserFontFaceLoadedPayload>(document.RootElement);

        Assert.Equal("3", face.FaceNumber);
        Assert.Equal("Open Sans", face.Family);
        Assert.Equal("swap", face.Descriptors.Display);
        Assert.Equal("url", face.Source!.Kind);
        Assert.Equal(BrowserResourcePayloads.Digest, face.FontFile!.Digest);
    }

    [Fact]
    public void AcceptsAGlyphRunsFontFile()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedTextBlockNode)!;
        var run = payload["boxFragments"]!["fragments"]![0]!["items"]![1]!["glyphRuns"]![0]!;
        run["fontFile"] = JsonNode.Parse($$"""
            {
              "digest": "{{BrowserResourcePayloads.Digest}}", "index": 1,
              "variations": [{ "axis": "wght", "value": 650 }]
            }
            """);
        using var document = JsonDocument.Parse(payload.ToJsonString());

        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Layout,
            BrowserEvidenceEventTypes.LayoutNodeChanged,
            document.RootElement);
        var read = BrowserProtocol.Deserialize<BrowserLayoutNodeChangedPayload>(document.RootElement);
        var fontFile = read.BoxFragments!.Fragments[0].Items![1].GlyphRuns![0].FontFile!;
        Assert.Equal(1, fontFile.Index);
        Assert.Equal("wght", fontFile.Variations[0].Axis);
        Assert.Equal(650, fontFile.Variations[0].Value);
    }
}
