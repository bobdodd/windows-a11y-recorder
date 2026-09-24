using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

// Evidence ingest rejects unmapped members, so every browser.network shape the
// bridge writes must map onto a contract or the renderer's whole stream is
// refused.
public sealed class BrowserNetworkPayloadIngestTests
{
    public static TheoryData<string, string> NetworkRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserNetworkPayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NetworkRecords))]
    public void AcceptsEveryNetworkRecordTheBridgeWrites(string eventType, string json)
    {
        using var document = JsonDocument.Parse(json);

        BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Network,
            eventType,
            document.RootElement);
    }

    [Theory]
    [MemberData(nameof(NetworkRecords))]
    public void RejectsAnUndeclaredMemberInEveryNetworkRecord(string eventType, string json)
    {
        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Network,
                eventType,
                document.RootElement));
    }

    [Fact]
    public void RejectsAnUndeclaredMemberInANestedResponse()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.ResponseReceived)!.AsObject();
        payload["response"]!["body"] = "text";
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkResponseReceived,
                document.RootElement));
    }

    [Fact]
    public void ReadsRealtimeRecordsAsWritten()
    {
        using var sent = JsonDocument.Parse(BrowserNetworkPayloads.WebSocketMessageSent);
        var message = BrowserProtocol.Deserialize<BrowserNetworkWebSocketMessagePayload>(
            sent.RootElement);
        Assert.Equal("31", message.InspectorId);
        Assert.Equal("text", message.Opcode);
        var withheld = Assert.Single(message.Payload!.Withheld);
        Assert.Equal(24, withheld.Offset);
        Assert.Equal("credential-value", withheld.Reason);
        Assert.Equal("main", message.World!.Kind);

        using var received = JsonDocument.Parse(
            BrowserNetworkPayloads.WebSocketBinaryMessageReceived);
        var binary = BrowserProtocol.Deserialize<BrowserNetworkWebSocketMessagePayload>(
            received.RootElement);
        Assert.Null(binary.Payload);
        Assert.Null(binary.Location);

        using var response = JsonDocument.Parse(
            BrowserNetworkPayloads.WebSocketHandshakeResponse);
        var handshake = BrowserProtocol.Deserialize<
            BrowserNetworkWebSocketHandshakeResponsePayload>(response.RootElement);
        Assert.Equal(["room_pref"], handshake.SetCookieNames);
        Assert.Null(Assert.Single(handshake.Headers, header => header.Name == "Set-Cookie").Value);
    }

    [Fact]
    public void RejectsAnUndeclaredMemberInARecordedText()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.EventSourceMessage)!.AsObject();
        payload["data"]!["raw"] = "text";
        using var document = JsonDocument.Parse(payload.ToJsonString());

        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkEventSourceMessage,
                document.RootElement));
    }

    [Fact]
    public void ReadsNetworkRecordsAsWritten()
    {
        using var sent = JsonDocument.Parse(BrowserNetworkPayloads.RequestWillBeSent);
        var sentPayload = BrowserProtocol.Deserialize<BrowserNetworkRequestWillBeSentPayload>(
            sent.RootElement);
        Assert.Equal("17", sentPayload.Request.InspectorId);
        Assert.Equal("https://example.test/api/items?page=2&key=abc", sentPayload.Request.Url);
        Assert.Equal(4, sentPayload.Request.Initiator.Line);
        var authorization = Assert.Single(
            sentPayload.Request.Headers, header => header.Name == "Authorization");
        Assert.True(authorization.ValueRedacted);
        Assert.Null(authorization.Value);
        Assert.Equal("credential-header", authorization.RedactionReason);
        Assert.Equal("main", sentPayload.World!.Kind);

        using var received = JsonDocument.Parse(BrowserNetworkPayloads.ResponseReceived);
        var receivedPayload = BrowserProtocol.Deserialize<BrowserNetworkResponseReceivedPayload>(
            received.RootElement);
        Assert.Equal(1834, receivedPayload.Response.EncodedDataLength);
        Assert.Equal(-1, receivedPayload.Response.ExpectedContentLength);
        Assert.Equal(443, receivedPayload.Response.RemoteAddress!.Port);
        Assert.Equal(21.0, receivedPayload.Response.Timing!.ConnectEnd);
        Assert.Null(receivedPayload.Response.Timing.ProxyStart);

        using var wire = JsonDocument.Parse(BrowserNetworkPayloads.RequestHeadersSent);
        var wirePayload = BrowserProtocol.Deserialize<BrowserNetworkRequestHeadersSentPayload>(
            wire.RootElement);
        Assert.Equal("session", Assert.Single(wirePayload.Cookies).Name);
        Assert.Null(wirePayload.Headers.Single(header => header.Name == "cookie").Value);

        using var navigation = JsonDocument.Parse(BrowserNetworkPayloads.NavigationResponse);
        var navigationPayload =
            BrowserProtocol.Deserialize<BrowserNetworkNavigationResponsePayload>(
                navigation.RootElement);
        Assert.Equal(2, navigationPayload.RedirectChain.Count);
        Assert.Equal("2001:db8::10", navigationPayload.Response!.RemoteAddress!.Ip);
        Assert.Equal(180.25, navigationPayload.Timing!.NavigationStartBeforeRecordMilliseconds);

        using var failed = JsonDocument.Parse(BrowserNetworkPayloads.FailedNavigationResponse);
        var failedPayload = BrowserProtocol.Deserialize<BrowserNetworkNavigationResponsePayload>(
            failed.RootElement);
        Assert.Null(failedPayload.Response);
        Assert.Equal(-105, failedPayload.NetError);
    }
}
