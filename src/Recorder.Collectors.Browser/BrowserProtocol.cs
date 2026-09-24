using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Recorder.Contracts;

namespace Recorder.Collectors.Browser;

internal static class BrowserProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async ValueTask<JsonDocument?> ReadFrameAsync(
        Stream stream,
        int maximumMessageBytes,
        CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        var headerRead = await ReadExactlyOrEndAsync(
            stream,
            header,
            cancellationToken).ConfigureAwait(false);
        if (!headerRead)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maximumMessageBytes)
        {
            throw new InvalidDataException(
                $"Browser protocol frame length {length} is invalid.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken)
            .ConfigureAwait(false);
        return JsonDocument.Parse(payload);
    }

    public static async ValueTask WriteFrameAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static T Deserialize<T>(JsonElement value) =>
        value.Deserialize<T>(JsonOptions) ??
        throw new InvalidDataException(
            $"Browser protocol message could not be read as {typeof(T).Name}.");

    public static JsonElement ValidateEvidencePayload(
        string channel,
        string eventType,
        JsonElement payload)
    {
        object? validated = (channel, eventType) switch
        {
            (BrowserEvidenceChannels.Listener,
                BrowserEvidenceEventTypes.ListenerRegistered or
                BrowserEvidenceEventTypes.ListenerRemoved or
                BrowserEvidenceEventTypes.ListenerCallbackReplaced) =>
                payload.Deserialize<BrowserListenerPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dispatch,
                BrowserEvidenceEventTypes.DispatchStarted or
                BrowserEvidenceEventTypes.ListenerInvoked or
                BrowserEvidenceEventTypes.DispatchCompleted or
                BrowserEvidenceEventTypes.DefaultAction) =>
                payload.Deserialize<BrowserDispatchPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Timer,
                BrowserEvidenceEventTypes.TimerScheduled or
                BrowserEvidenceEventTypes.TimerFired or
                BrowserEvidenceEventTypes.TimerCancelled) =>
                payload.Deserialize<BrowserTimerPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Scheduler,
                BrowserEvidenceEventTypes.WakeUpDeferred) =>
                payload.Deserialize<BrowserSchedulerPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Navigation,
                BrowserEvidenceEventTypes.NavigationStarted or
                BrowserEvidenceEventTypes.NavigationCompleted) =>
                payload.Deserialize<BrowserNavigationPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointStarted) =>
                payload.Deserialize<BrowserDomCheckpointStartedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointNode) =>
                payload.Deserialize<BrowserDomCheckpointNodePayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointNodeAttribute) =>
                payload.Deserialize<BrowserDomCheckpointNodeAttributePayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointCompleted) =>
                payload.Deserialize<BrowserDomCheckpointCompletedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomAttributeChanged) =>
                payload.Deserialize<BrowserDomAttributeChangedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCharacterDataChanged) =>
                payload.Deserialize<BrowserDomCharacterDataChangedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Accessibility,
                BrowserEvidenceEventTypes.AccessibilityCheckpointStarted) =>
                payload.Deserialize<BrowserAccessibilityCheckpointStartedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Accessibility,
                BrowserEvidenceEventTypes.AccessibilityCheckpointNode) =>
                payload.Deserialize<BrowserAccessibilityCheckpointNodePayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Accessibility,
                BrowserEvidenceEventTypes.AccessibilityCheckpointCompleted) =>
                payload.Deserialize<BrowserAccessibilityCheckpointCompletedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Cookie,
                BrowserEvidenceEventTypes.DocumentCookieRead) =>
                payload.Deserialize<BrowserDocumentCookieReadPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Cookie,
                BrowserEvidenceEventTypes.DocumentCookieWrite) =>
                payload.Deserialize<BrowserDocumentCookieWritePayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Cookie,
                BrowserEvidenceEventTypes.CookieStoreRequest) =>
                payload.Deserialize<BrowserCookieStoreRequestPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Cookie,
                BrowserEvidenceEventTypes.CookieStoreResult) =>
                payload.Deserialize<BrowserCookieStoreResultPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Cookie,
                BrowserEvidenceEventTypes.CookieStoreChange) =>
                payload.Deserialize<BrowserCookieStoreChangePayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Cookie,
                BrowserEvidenceEventTypes.CookieAccess) =>
                payload.Deserialize<BrowserCookieAccessPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.FocusChanged) =>
                payload.Deserialize<BrowserFocusChangedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.SelectionChanged) =>
                payload.Deserialize<BrowserSelectionChangedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.TextControlValueChanged) =>
                payload.Deserialize<BrowserTextControlValueChangedPayload>(JsonOptions)
                    as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.ActiveDescendantReferenceSet) =>
                payload.Deserialize<BrowserActiveDescendantReferenceSetPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Layout,
                BrowserEvidenceEventTypes.LayoutCheckpointStarted) =>
                payload.Deserialize<BrowserLayoutCheckpointStartedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Layout,
                BrowserEvidenceEventTypes.LayoutCheckpointNode) =>
                payload.Deserialize<BrowserLayoutCheckpointNodePayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Layout,
                BrowserEvidenceEventTypes.LayoutCheckpointCompleted) =>
                payload.Deserialize<BrowserLayoutCheckpointCompletedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkRequestWillBeSent) =>
                payload.Deserialize<BrowserNetworkRequestWillBeSentPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkResponseReceived) =>
                payload.Deserialize<BrowserNetworkResponseReceivedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkRequestFinished) =>
                payload.Deserialize<BrowserNetworkRequestFinishedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkRequestFailed) =>
                payload.Deserialize<BrowserNetworkRequestFailedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkMemoryCacheHit) =>
                payload.Deserialize<BrowserNetworkMemoryCacheHitPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkRequestHeadersSent) =>
                payload.Deserialize<BrowserNetworkRequestHeadersSentPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkResponseHeadersReceived) =>
                payload.Deserialize<BrowserNetworkResponseHeadersReceivedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkNavigationResponse) =>
                payload.Deserialize<BrowserNetworkNavigationResponsePayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebSocketCreated) =>
                payload.Deserialize<BrowserNetworkWebSocketCreatedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebSocketHandshakeRequest) =>
                payload.Deserialize<BrowserNetworkWebSocketHandshakeRequestPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebSocketHandshakeResponse) =>
                payload.Deserialize<BrowserNetworkWebSocketHandshakeResponsePayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebSocketMessageSent) =>
                payload.Deserialize<BrowserNetworkWebSocketMessagePayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebSocketMessageReceived) =>
                payload.Deserialize<BrowserNetworkWebSocketMessagePayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebSocketCloseRequested) =>
                payload.Deserialize<BrowserNetworkWebSocketCloseRequestedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebSocketError) =>
                payload.Deserialize<BrowserNetworkWebSocketErrorPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebSocketClosed) =>
                payload.Deserialize<BrowserNetworkWebSocketClosedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkEventSourceMessage) =>
                payload.Deserialize<BrowserNetworkEventSourceMessagePayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebTransportCreated) =>
                payload.Deserialize<BrowserNetworkWebTransportCreatedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebTransportEstablished) =>
                payload.Deserialize<BrowserNetworkWebTransportEstablishedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebTransportCloseRequested) =>
                payload.Deserialize<BrowserNetworkWebTransportCloseRequestedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.NetworkWebTransportClosed) =>
                payload.Deserialize<BrowserNetworkWebTransportClosedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Lifecycle or
                BrowserEvidenceChannels.Listener or
                BrowserEvidenceChannels.Dispatch or
                BrowserEvidenceChannels.Timer or
                BrowserEvidenceChannels.Scheduler or
                BrowserEvidenceChannels.Navigation or
                BrowserEvidenceChannels.Dom or
                BrowserEvidenceChannels.Accessibility or
                BrowserEvidenceChannels.Cookie or
                BrowserEvidenceChannels.Interaction or
                BrowserEvidenceChannels.Layout or
                BrowserEvidenceChannels.Network,
                BrowserEvidenceEventTypes.Omission) =>
                payload.Deserialize<BrowserOmissionPayload>(JsonOptions)
                    as object,
            _ => throw new InvalidDataException(
                $"Unsupported browser evidence event {channel}/{eventType}.")
        };
        _ = validated ?? throw new InvalidDataException(
            "Browser evidence payload was null.");

        return payload.Clone();
    }

    private static async ValueTask<bool> ReadExactlyOrEndAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var count = await stream.ReadAsync(
                buffer[total..],
                cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                if (total == 0)
                {
                    return false;
                }
                throw new EndOfStreamException(
                    "Browser protocol frame ended inside its length prefix.");
            }
            total += count;
        }
        return true;
    }
}

internal sealed record BrowserHelloMessage(
    string Kind,
    string ProtocolVersion,
    string AuthenticationToken,
    string BrowserInstanceId,
    int ProcessId,
    string ProcessType,
    string ChromiumVersion,
    string MonotonicFrequency,
    int? ParentProcessId,
    int? ChildProcessId);

internal sealed record BrowserClockSyncResponse(
    string Kind,
    string RequestId,
    string BrowserReceiveTicks,
    string BrowserSendTicks);

internal sealed record BrowserEvidenceMessage(
    string Kind,
    string ProtocolVersion,
    string BrowserTimestampTicks,
    string Channel,
    string EventType,
    JsonElement Payload,
    IReadOnlyList<string>? QualityFlags);
