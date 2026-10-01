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
    public void ReadsLayoutChangesAsWritten()
    {
        using var started = JsonDocument.Parse(BrowserLayoutPayloads.ChangesStartedWithoutCheckpoint);
        var startedPayload = BrowserProtocol.Deserialize<BrowserLayoutChangesStartedPayload>(
            started.RootElement);
        Assert.Null(startedPayload.LayoutCheckpointId);
        Assert.Equal("layout-transform-1", startedPayload.ViewTransformNodeId);
        Assert.Equal(2, startedPayload.ViewPaintOffset.Y);

        using var transform = JsonDocument.Parse(BrowserLayoutPayloads.ScrollTransformNode);
        var transformPayload = BrowserProtocol.Deserialize<BrowserLayoutTransformNodePayload>(
            transform.RootElement);
        Assert.Equal(16, transformPayload.Matrix.Count);
        Assert.Equal(-300.625, transformPayload.Matrix[13]);
        Assert.True(transformPayload.ScrollTranslation);

        using var node = JsonDocument.Parse(BrowserLayoutPayloads.ChangedElementNode);
        var nodePayload = BrowserProtocol.Deserialize<BrowserLayoutNodeChangedPayload>(
            node.RootElement);
        Assert.Equal(["style", "layout"], nodePayload.Reasons);
        Assert.Equal(338.75, nodePayload.Geometry!.LocalRect!.Y);
        Assert.Equal(0.8, nodePayload.Geometry.ClientRectScale);

        using var changes = JsonDocument.Parse(BrowserLayoutPayloads.ChangedElementStyleChanges);
        var changesPayload = BrowserProtocol.Deserialize<BrowserLayoutNodeChangedPayload>(
            changes.RootElement);
        Assert.False(changesPayload.ComputedStyleComplete);
        Assert.Equal("green", changesPayload.CustomProperties!["--accent"]);
        Assert.Equal(["--gap"], changesPayload.RemovedCustomProperties);

        using var boxed = JsonDocument.Parse(BrowserLayoutPayloads.ChangedBoxedElementNode);
        var boxedPayload = BrowserProtocol.Deserialize<BrowserLayoutNodeChangedPayload>(
            boxed.RootElement);
        var fragments = boxedPayload.BoxFragments!;
        Assert.Equal(1.25, fragments.EffectiveZoom);
        var fragment = Assert.Single(fragments.Fragments);
        Assert.Equal(187.5, fragment.Width);
        Assert.Equal(31.25, fragment.BreakToken!.ConsumedBlockSize);
        Assert.Equal(0, fragment.BreakToken.SequenceNumber);
        Assert.Equal(40.015625, fragment.ScrollableOverflow!.Height);
        Assert.Equal(["box", "anonymous", "column", "line"], fragment.Children.Select(child => child.Kind));
        Assert.Equal(44, fragment.Children[0].NodeId);
        Assert.Equal(1.25, fragment.Children[1].Fragment!.Height);
        Assert.Null(fragments.NaturalSize);

        using var paragraph = JsonDocument.Parse(BrowserLayoutPayloads.TextBlockNode);
        var paragraphFragments = BrowserProtocol.Deserialize<BrowserLayoutCheckpointNodePayload>(
            paragraph.RootElement).BoxFragments!;
        Assert.Equal("Hiabc", paragraphFragments.TextContent);
        Assert.Equal("HIABC", paragraphFragments.FirstLineText);
        Assert.False(paragraphFragments.TextContentUnchanged);
        var items = paragraphFragments.Fragments[0].Items!;
        Assert.Equal(["line", "text", "box", "text", "generated-text"], items.Select(item => item.Type));
        Assert.Equal(5, items[0].DescendantsCount);
        Assert.Equal((0, 2), (items[1].Start, items[1].End));
        Assert.Equal("rtl", items[3].Direction);
        Assert.Equal("-", items[4].GeneratedText);
        var run = Assert.Single(items[1].GlyphRuns!);
        Assert.Equal("ArialMT", run.Font.PostScriptName);
        Assert.Equal(
            [new BrowserLayoutGlyph(43, 0, 0, 0, 0), new BrowserLayoutGlyph(76, 1, 8.5f, 0, -0.25f)],
            BrowserLayoutGlyphs.Unpack(run.Glyphs));
        Assert.Throws<FormatException>(() => BrowserLayoutGlyphs.Unpack("KwAAAAAAAAAAAAAA"));
        var anonymous = paragraphFragments.Fragments[0].Children[0].Fragment!;
        Assert.Equal("R", anonymous.TextContent);
        Assert.Equal(2, anonymous.Items!.Count);

        using var image = JsonDocument.Parse(BrowserLayoutPayloads.BoxedElementNode);
        var imagePayload = BrowserProtocol.Deserialize<BrowserLayoutCheckpointNodePayload>(
            image.RootElement);
        Assert.Equal(400, imagePayload.BoxFragments!.NaturalSize!.Width);

        using var text = JsonDocument.Parse(BrowserLayoutPayloads.ChangedEmptyTextNode);
        var textPayload = BrowserProtocol.Deserialize<BrowserLayoutNodeChangedPayload>(
            text.RootElement);
        Assert.True(textPayload.Geometry!.ClientRectEmpty);
        Assert.Null(textPayload.Geometry.LocalRect);

        using var completed = JsonDocument.Parse(BrowserLayoutPayloads.ChangesCompleted);
        var completedPayload = BrowserProtocol.Deserialize<BrowserLayoutChangesCompletedPayload>(
            completed.RootElement);
        Assert.Equal(5, completedPayload.NotedNodeCount);
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
