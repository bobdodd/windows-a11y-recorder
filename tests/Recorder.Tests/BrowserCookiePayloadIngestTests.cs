using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

// Evidence ingest rejects unmapped members, so every browser.cookie shape the
// bridge writes must map onto a contract or the renderer's whole stream is
// refused. The same rule is what keeps a cookie value out: no contract has a
// value member, so a payload carrying one cannot be ingested.
public sealed class BrowserCookiePayloadIngestTests
{
    public static TheoryData<string, string> CookieRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserCookiePayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CookieRecords))]
    public void AcceptsEveryCookieRecordTheBridgeWrites(
        string eventType,
        string json)
    {
        using var document = JsonDocument.Parse(json);

        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Cookie,
            eventType,
            document.RootElement);
    }

    [Theory]
    [MemberData(nameof(CookieRecords))]
    public void RejectsACookieValueInEveryCookieRecord(
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!.AsObject();
        payload["value"] = "must-not-be-recorded";
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Cookie,
                eventType,
                document.RootElement));
    }

    [Fact]
    public void RejectsACookieValueInsideACookieAccessEntry()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieAccess)!.AsObject();
        payload["cookies"]![0]!.AsObject()["value"] = "must-not-be-recorded";
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Cookie,
                BrowserEvidenceEventTypes.CookieAccess,
                document.RootElement));
    }

    [Fact]
    public void RejectsACookieValueInsideWriteAttributes()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.DocumentCookieWrite)!.AsObject();
        payload["attributes"]!.AsObject()["value"] = "must-not-be-recorded";
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Cookie,
                BrowserEvidenceEventTypes.DocumentCookieWrite,
                document.RootElement));
    }

    [Fact]
    public void ReadsCookieNamesAndAttributesAsWritten()
    {
        using var read = JsonDocument.Parse(BrowserCookiePayloads.DocumentCookieRead);
        var readPayload = BrowserProtocol.Deserialize<BrowserDocumentCookieReadPayload>(
            read.RootElement);
        Assert.Equal(["session", ""], readPayload.CookieNames);
        Assert.Equal("cookie-manager", readPayload.ServedFrom);
        Assert.Equal("world-0", readPayload.Context.ExecutionWorldId);

        using var access = JsonDocument.Parse(BrowserCookiePayloads.CookieAccess);
        var accessPayload = BrowserProtocol.Deserialize<BrowserCookieAccessPayload>(
            access.RootElement);
        Assert.Equal("change", accessPayload.AccessType);
        Assert.True(accessPayload.Cookies[0].Parsed);
        Assert.False(accessPayload.Cookies[1].Parsed);
        Assert.Null(accessPayload.Cookies[1].Domain);
        Assert.Equal(
            ["EXCLUDE_USER_PREFERENCES"],
            accessPayload.Cookies[1].ExclusionReasons);
    }
}
