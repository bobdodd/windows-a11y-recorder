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
            (BrowserEvidenceChannels.Timer,
                BrowserEvidenceEventTypes.TimerOrigin) =>
                payload.Deserialize<BrowserTimerOriginPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Timer,
                BrowserEvidenceEventTypes.ScriptCompiled) =>
                payload.Deserialize<BrowserScriptCompiledPayload>(JsonOptions) as object,
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
                BrowserEvidenceEventTypes.DomCheckpointNodeCharacterData) =>
                payload.Deserialize<BrowserDomCheckpointNodeCharacterDataPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointShadowRoot) =>
                payload.Deserialize<BrowserDomCheckpointShadowRootPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointSlotAssignment) =>
                payload.Deserialize<BrowserDomCheckpointSlotAssignmentPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointCompleted) =>
                payload.Deserialize<BrowserDomCheckpointCompletedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomAttributeChanged) =>
                payload.Deserialize<BrowserDomAttributeChangedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCharacterDataChanged) =>
                payload.Deserialize<BrowserDomCharacterDataChangedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomNodeInserted) =>
                payload.Deserialize<BrowserDomNodeInsertedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomInsertedNode) =>
                payload.Deserialize<BrowserDomInsertedNodePayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomInsertedNodeAttribute) =>
                payload.Deserialize<BrowserDomInsertedNodeAttributePayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomInsertedNodeCharacterData) =>
                payload.Deserialize<BrowserDomInsertedNodeCharacterDataPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomInsertedShadowRoot) =>
                payload.Deserialize<BrowserDomInsertedShadowRootPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomInsertedSlotAssignment) =>
                payload.Deserialize<BrowserDomInsertedSlotAssignmentPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomInsertionCompleted) =>
                payload.Deserialize<BrowserDomInsertionCompletedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomNodeRemoved) =>
                payload.Deserialize<BrowserDomNodeRemovedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomChildrenRemoved) =>
                payload.Deserialize<BrowserDomChildrenRemovedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomShadowRootChanged) =>
                payload.Deserialize<BrowserDomShadowRootChangedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomSlotAssignmentChanged) =>
                payload.Deserialize<BrowserDomSlotAssignmentChangedPayload>(JsonOptions) as object,
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
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.PagePopupOpened) =>
                payload.Deserialize<BrowserPagePopupOpenedPayload>(JsonOptions)
                    as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.PagePopupWindowRect) =>
                payload.Deserialize<BrowserPagePopupWindowRectPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.PagePopupClosed) =>
                payload.Deserialize<BrowserPagePopupClosedPayload>(JsonOptions)
                    as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.PopupWidgetCreated) =>
                payload.Deserialize<BrowserPopupWidgetCreatedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.PopupWidgetShown) =>
                payload.Deserialize<BrowserPopupWidgetShownPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.PopupWidgetBoundsRequested) =>
                payload.Deserialize<BrowserPopupWidgetBoundsRequestedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.PopupWidgetScreenRects) =>
                payload.Deserialize<BrowserPopupWidgetScreenRectsPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.PopupWidgetHidden) =>
                payload.Deserialize<BrowserPopupWidgetHiddenPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.OptionSelectednessChanged) =>
                payload.Deserialize<BrowserOptionSelectednessChangedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.InteractionCheckpointStarted) =>
                payload.Deserialize<BrowserInteractionCheckpointStartedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.InteractionCheckpointTextControl) =>
                payload.Deserialize<BrowserInteractionCheckpointTextControlPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Interaction,
                BrowserEvidenceEventTypes.InteractionCheckpointCompleted) =>
                payload.Deserialize<BrowserInteractionCheckpointCompletedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Presentation,
                BrowserEvidenceEventTypes.PresentationRequested) =>
                payload.Deserialize<BrowserPresentationRequestedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Presentation,
                BrowserEvidenceEventTypes.PresentationNotSwapped) =>
                payload.Deserialize<BrowserPresentationNotSwappedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Presentation,
                BrowserEvidenceEventTypes.PresentationSwapped) =>
                payload.Deserialize<BrowserPresentationSwappedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Presentation,
                BrowserEvidenceEventTypes.PresentationFeedback) =>
                payload.Deserialize<BrowserPresentationFeedbackPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Script,
                BrowserEvidenceEventTypes.ScriptParsed) =>
                payload.Deserialize<BrowserScriptParsedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Script,
                BrowserEvidenceEventTypes.ScriptText) =>
                payload.Deserialize<BrowserResourceBytesPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Animation,
                BrowserEvidenceEventTypes.AnimationUpdated) =>
                payload.Deserialize<BrowserAnimationUpdatedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Animation,
                BrowserEvidenceEventTypes.AnimationRemoved) =>
                payload.Deserialize<BrowserAnimationRemovedPayload>(JsonOptions) as object,
            (BrowserEvidenceChannels.Compositor,
                BrowserEvidenceEventTypes.CompositorAnimationStarted) =>
                payload.Deserialize<BrowserCompositorAnimationStartedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Compositor,
                BrowserEvidenceEventTypes.CompositorAnimationEnded) =>
                payload.Deserialize<BrowserCompositorAnimationEndedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Compositor,
                BrowserEvidenceEventTypes.CompositorFrame) =>
                payload.Deserialize<BrowserCompositorFramePayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Compositor,
                BrowserEvidenceEventTypes.CompositorFramePresented) =>
                payload.Deserialize<BrowserCompositorFramePresentedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Compositor,
                BrowserEvidenceEventTypes.PaintWorkletPainted) =>
                payload.Deserialize<BrowserPaintWorkletPaintedPayload>(
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
            (BrowserEvidenceChannels.Layout,
                BrowserEvidenceEventTypes.LayoutChangesStarted) =>
                payload.Deserialize<BrowserLayoutChangesStartedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Layout,
                BrowserEvidenceEventTypes.LayoutTransformNode) =>
                payload.Deserialize<BrowserLayoutTransformNodePayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Layout,
                BrowserEvidenceEventTypes.LayoutNodeChanged) =>
                payload.Deserialize<BrowserLayoutNodeChangedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Layout,
                BrowserEvidenceEventTypes.LayoutChangesCompleted) =>
                payload.Deserialize<BrowserLayoutChangesCompletedPayload>(
                    JsonOptions) as object,
            (BrowserEvidenceChannels.Layout,
                BrowserEvidenceEventTypes.LayoutScrollOffsetChanged) =>
                payload.Deserialize<BrowserLayoutScrollOffsetChangedPayload>(
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
            (BrowserEvidenceChannels.Resources,
                BrowserEvidenceEventTypes.FontFile or
                BrowserEvidenceEventTypes.ImageData or
                BrowserEvidenceEventTypes.StyleSheetText) =>
                payload.Deserialize<BrowserResourceBytesPayload>(JsonOptions)
                    as object,
            (BrowserEvidenceChannels.Resources,
                BrowserEvidenceEventTypes.FontFaceAdded or
                BrowserEvidenceEventTypes.FontFaceRemoved) =>
                payload.Deserialize<BrowserFontFacePayload>(JsonOptions)
                    as object,
            (BrowserEvidenceChannels.Resources,
                BrowserEvidenceEventTypes.FontFaceLoaded) =>
                payload.Deserialize<BrowserFontFaceLoadedPayload>(JsonOptions)
                    as object,
            (BrowserEvidenceChannels.Resources,
                BrowserEvidenceEventTypes.ImageResource) =>
                payload.Deserialize<BrowserImageResourcePayload>(JsonOptions)
                    as object,
            (BrowserEvidenceChannels.Resources,
                BrowserEvidenceEventTypes.ImagePaintImage) =>
                payload.Deserialize<BrowserImagePaintImagePayload>(JsonOptions)
                    as object,
            (BrowserEvidenceChannels.Resources,
                BrowserEvidenceEventTypes.StyleSheetResource) =>
                payload.Deserialize<BrowserStyleSheetResourcePayload>(JsonOptions)
                    as object,
            (BrowserEvidenceChannels.Resources,
                BrowserEvidenceEventTypes.StyleSheetsUpdated) =>
                payload.Deserialize<BrowserStyleSheetsUpdatedPayload>(JsonOptions)
                    as object,
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
                BrowserEvidenceChannels.Presentation or
                BrowserEvidenceChannels.Network or
                BrowserEvidenceChannels.Resources or
                BrowserEvidenceChannels.Compositor or
                BrowserEvidenceChannels.Animation or
                BrowserEvidenceChannels.Script,
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
