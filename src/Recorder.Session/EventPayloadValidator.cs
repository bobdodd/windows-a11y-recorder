using System.Text.Json;

namespace Recorder.Session;

internal static class EventPayloadValidator
{
    private static readonly HashSet<string> BuiltInChannels =
    [
        "collector.lifecycle",
        "session.annotations",
        "input.keyboard",
        "input.mouse",
        "window.foreground",
        "system.preferences",
        "accessibility.uia.events",
        "graphics.desktop.frames",
        "graphics.magnifier",
        "audio.microphone",
        "audio.system",
        "browser.lifecycle",
        "browser.listener",
        "browser.dispatch",
        "browser.timer",
        "browser.scheduler",
        "browser.navigation",
        "browser.dom",
        "browser.accessibility",
        "browser.cookie",
        "browser.interaction",
        "browser.layout",
        "browser.presentation",
        "browser.network",
        "browser.resources",
        "browser.compositor",
        "browser.animation",
        "browser.script",
        "browser.preferences"
    ];

    /// <summary>Whether the recorder defines the channel.</summary>
    public static bool IsBuiltInChannel(string channel) => BuiltInChannels.Contains(channel);

    /// <summary>
    /// Checks one event's payload against its channel and event type.
    /// Payloads on channels the recorder does not define are not checked.
    /// </summary>
    public static void Validate(
        string channel,
        string eventType,
        JsonElement payload,
        long monotonicNanoseconds,
        ICollection<EventValidationIssue> issues)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            if (BuiltInChannels.Contains(channel))
            {
                AddError(
                    issues,
                    "event-payload-not-object",
                    "#/payload",
                    "Built-in event payloads must be JSON objects.");
            }

            return;
        }

        switch ((channel, eventType))
        {
            case ("collector.lifecycle", "collector-lifecycle"):
                ValidateLifecycle(payload, issues);
                break;
            case ("session.annotations", "session-marker"):
                ValidateAnnotation(payload, issues);
                break;
            case ("input.keyboard", "raw-keyboard"):
                ValidateRawKeyboard(payload, issues);
                break;
            case ("input.mouse", "raw-mouse"):
                ValidateRawMouse(payload, issues);
                break;
            case ("window.foreground", "foreground-window"):
                ValidateForegroundWindow(payload, issues);
                break;
            case ("system.preferences", "windows-preferences"):
                ValidateWindowsPreferences(payload, issues);
                break;
            case ("system.preferences", "windows-preference-changed"):
                ValidateWindowsPreferenceChange(payload, issues);
                break;
            case ("accessibility.uia.events", "focus-changed"):
            case ("accessibility.uia.events", "automation-event"):
            case ("accessibility.uia.events", "structure-changed"):
            case ("accessibility.uia.events", "property-changed"):
                ValidateUiaEvent(payload, issues);
                break;
            case ("browser.preferences", "browser-preferences"):
                ValidateBrowserPreferences(payload, issues);
                break;
            case ("browser.preferences", "browser-preference-changed"):
                ValidateBrowserPreferenceChange(payload, issues);
                break;
            case ("browser.preferences", "web-preferences-sent"):
                ValidateWebPreferencesSent(payload, issues);
                break;
            case ("browser.preferences", "color-maps-sent"):
                ValidateColorMapsSent(payload, issues);
                break;
            case ("browser.preferences", "zoom-level-changed"):
                ValidateZoomLevelChange(payload, issues);
                break;
            case ("graphics.magnifier", "magnifier-changed"):
                ValidateMagnifierChange(payload, issues);
                break;
            case ("graphics.desktop.frames", "desktop-frame"):
                ValidateDesktopFrame(payload, issues);
                break;
            case ("audio.microphone", "audio-stream-started"):
            case ("audio.microphone", "audio-stream-stopped"):
            case ("audio.system", "audio-stream-started"):
            case ("audio.system", "audio-stream-stopped"):
                ValidateAudioStream(payload, issues);
                break;
            case ("audio.microphone", "audio-buffer"):
            case ("audio.system", "audio-buffer"):
                ValidateAudioBuffer(payload, issues);
                break;
            case ("audio.microphone", "audio-stream-error"):
            case ("audio.system", "audio-stream-error"):
                ValidateAudioError(payload, issues);
                break;
            case ("browser.lifecycle", "browser-connected"):
                ValidateBrowserConnected(payload, issues);
                break;
            case ("browser.lifecycle", "browser-exited"):
                ValidateBrowserExited(payload, issues);
                break;
            case ("browser.lifecycle", "browser-clock-synchronized"):
                ValidateBrowserClockSynchronized(payload, issues);
                break;
            case ("browser.accessibility", "accessibility-checkpoint-started"):
                ValidateBrowserAccessibilityCheckpointStarted(
                    payload,
                    issues);
                break;
            case ("browser.accessibility", "accessibility-checkpoint-node"):
                ValidateBrowserAccessibilityCheckpointNode(
                    payload,
                    issues);
                break;
            case (
                "browser.accessibility",
                "accessibility-checkpoint-completed"):
                ValidateBrowserAccessibilityCheckpointCompleted(
                    payload,
                    issues);
                break;
            case ("browser.listener", "listener-registered"):
            case ("browser.listener", "listener-removed"):
            case ("browser.listener", "listener-callback-replaced"):
                ValidateBrowserListener(payload, issues);
                break;
            case ("browser.dispatch", "dispatch-started"):
            case ("browser.dispatch", "listener-invoked"):
            case ("browser.dispatch", "dispatch-completed"):
                ValidateBrowserDispatch(
                    payload,
                    issues,
                    requireDefaultAction: false);
                break;
            case ("browser.dispatch", "default-action"):
                ValidateBrowserDispatch(
                    payload,
                    issues,
                    requireDefaultAction: true);
                break;
            case ("browser.timer", "timer-scheduled"):
            case ("browser.timer", "timer-fired"):
            case ("browser.timer", "timer-cancelled"):
                ValidateBrowserTimer(payload, issues);
                break;
            case ("browser.timer", "timer-origin"):
                ValidateBrowserTimerOrigin(payload, issues);
                break;
            case ("browser.timer", "script-compiled"):
                ValidateBrowserScriptCompiled(payload, issues);
                break;
            case ("browser.scheduler", "wake-up-deferred"):
                ValidateBrowserScheduler(payload, issues);
                break;
            case ("browser.navigation", "navigation-started"):
                ValidateBrowserNavigation(
                    payload,
                    issues,
                    completed: false);
                break;
            case ("browser.navigation", "navigation-completed"):
                ValidateBrowserNavigation(
                    payload,
                    issues,
                    completed: true);
                break;
            case ("browser.dom", "dom-checkpoint-started"):
                ValidateBrowserDomCheckpointStarted(payload, issues);
                break;
            case ("browser.dom", "dom-checkpoint-frame-owner"):
                ValidateBrowserDomCheckpointFrameOwner(payload, issues);
                break;
            case ("browser.dom", "dom-frame-owner-changed"):
                ValidateBrowserDomFrameOwnerChanged(payload, issues);
                break;
            case ("browser.dom", "dom-checkpoint-node"):
                ValidateBrowserDomCheckpointNode(payload, issues);
                break;
            case ("browser.dom", "dom-checkpoint-node-attribute"):
                ValidateBrowserDomCheckpointNodeAttribute(payload, issues);
                break;
            case ("browser.dom", "dom-checkpoint-node-character-data"):
                ValidateBrowserDomCheckpointNodeCharacterData(payload, issues);
                break;
            case ("browser.dom", "dom-checkpoint-shadow-root"):
                ValidateBrowserDomCheckpointShadowRoot(payload, issues);
                break;
            case ("browser.dom", "dom-checkpoint-slot-assignment"):
                ValidateBrowserDomCheckpointSlotAssignment(payload, issues);
                break;
            case ("browser.dom", "dom-checkpoint-completed"):
                ValidateBrowserDomCheckpointCompleted(payload, issues);
                break;
            case ("browser.dom", "dom-attribute-changed"):
                ValidateBrowserDomAttributeChanged(payload, issues);
                break;
            case ("browser.dom", "dom-character-data-changed"):
                ValidateBrowserDomCharacterDataChanged(payload, issues);
                break;
            case ("browser.dom", "dom-node-inserted"):
                ValidateBrowserDomNodeInserted(payload, issues);
                break;
            case ("browser.dom", "dom-inserted-node"):
                ValidateBrowserDomInsertedNode(payload, issues);
                break;
            case ("browser.dom", "dom-inserted-node-attribute"):
                ValidateBrowserDomCheckpointNodeAttribute(
                    payload, issues, "insertionId");
                ValidateLayoutIdentity(
                    payload,
                    "insertionId",
                    "dom-transition-",
                    "browser-dom-transition-id-invalid",
                    issues);
                break;
            case ("browser.dom", "dom-inserted-node-character-data"):
                ValidateBrowserDomCheckpointNodeCharacterData(
                    payload, issues, "insertionId");
                ValidateLayoutIdentity(
                    payload,
                    "insertionId",
                    "dom-transition-",
                    "browser-dom-transition-id-invalid",
                    issues);
                break;
            case ("browser.dom", "dom-inserted-shadow-root"):
                ValidateBrowserDomCheckpointShadowRoot(
                    payload, issues, "insertionId");
                ValidateLayoutIdentity(
                    payload,
                    "insertionId",
                    "dom-transition-",
                    "browser-dom-transition-id-invalid",
                    issues);
                break;
            case ("browser.dom", "dom-inserted-slot-assignment"):
                ValidateBrowserDomCheckpointSlotAssignment(
                    payload, issues, "insertionId");
                ValidateLayoutIdentity(
                    payload,
                    "insertionId",
                    "dom-transition-",
                    "browser-dom-transition-id-invalid",
                    issues);
                break;
            case ("browser.dom", "dom-insertion-completed"):
                ValidateBrowserDomInsertionCompleted(payload, issues);
                break;
            case ("browser.dom", "dom-node-removed"):
                ValidateBrowserDomNodeRemoved(payload, issues);
                break;
            case ("browser.dom", "dom-children-removed"):
                ValidateBrowserDomChildrenRemoved(payload, issues);
                break;
            case ("browser.dom", "dom-shadow-root-changed"):
                ValidateBrowserDomCheckpointShadowRoot(
                    payload, issues, "transitionId");
                ValidateLayoutIdentity(
                    payload,
                    "transitionId",
                    "dom-transition-",
                    "browser-dom-transition-id-invalid",
                    issues);
                break;
            case ("browser.dom", "dom-slot-assignment-changed"):
                ValidateBrowserDomCheckpointSlotAssignment(
                    payload, issues, "transitionId");
                ValidateLayoutIdentity(
                    payload,
                    "transitionId",
                    "dom-transition-",
                    "browser-dom-transition-id-invalid",
                    issues);
                break;
            case ("browser.cookie", "document-cookie-read"):
                ValidateBrowserDocumentCookieRead(payload, issues);
                break;
            case ("browser.cookie", "document-cookie-write"):
                ValidateBrowserDocumentCookieWrite(payload, issues);
                break;
            case ("browser.cookie", "cookie-store-request"):
                ValidateBrowserCookieStoreRequest(payload, issues);
                break;
            case ("browser.cookie", "cookie-store-result"):
                ValidateBrowserCookieStoreResult(payload, issues);
                break;
            case ("browser.cookie", "cookie-store-change"):
                ValidateBrowserCookieStoreChange(payload, issues);
                break;
            case ("browser.cookie", "cookie-access"):
                ValidateBrowserCookieAccess(payload, issues);
                break;
            case ("browser.interaction", "focus-changed"):
                ValidateBrowserFocusChanged(payload, issues);
                break;
            case ("browser.interaction", "selection-changed"):
                ValidateBrowserSelectionChanged(payload, issues);
                break;
            case ("browser.interaction", "text-control-value-changed"):
                ValidateBrowserTextControlValueChanged(payload, issues);
                break;
            case ("browser.interaction", "active-descendant-reference-set"):
                ValidateBrowserActiveDescendantReferenceSet(
                    payload, issues);
                break;
            case ("browser.interaction", "page-popup-opened"):
                ValidateBrowserPagePopupOpened(payload, issues);
                break;
            case ("browser.interaction", "page-popup-window-rect"):
                ValidateBrowserPagePopupWindowRect(payload, issues);
                break;
            case ("browser.interaction", "page-popup-closed"):
                ValidateBrowserPagePopupClosed(payload, issues);
                break;
            case ("browser.interaction", "popup-widget-created"):
                ValidateBrowserPopupWidgetCreated(payload, issues);
                break;
            case ("browser.interaction", "popup-widget-shown"):
                ValidateBrowserPopupWidgetShown(payload, issues);
                break;
            case ("browser.interaction", "popup-widget-bounds-requested"):
                ValidateBrowserPopupWidgetBoundsRequested(payload, issues);
                break;
            case ("browser.interaction", "popup-widget-screen-rects"):
                ValidateBrowserPopupWidgetScreenRects(payload, issues);
                break;
            case ("browser.interaction", "popup-widget-hidden"):
                ValidateBrowserPopupWidgetHidden(payload, issues);
                break;
            case ("browser.interaction", "option-selectedness-changed"):
                ValidateBrowserOptionSelectednessChanged(payload, issues);
                break;
            case ("browser.interaction", "interaction-checkpoint-started"):
                ValidateBrowserInteractionCheckpointStarted(
                    payload, issues);
                break;
            case ("browser.interaction", "interaction-checkpoint-text-control"):
                ValidateBrowserInteractionCheckpointTextControl(
                    payload, issues);
                break;
            case ("browser.interaction", "interaction-checkpoint-completed"):
                ValidateBrowserInteractionCheckpointCompleted(
                    payload, issues);
                break;
            case ("browser.layout", "layout-checkpoint-started"):
                ValidateBrowserLayoutCheckpointStarted(payload, issues);
                break;
            case ("browser.layout", "layout-checkpoint-node"):
                ValidateBrowserLayoutCheckpointNode(payload, issues);
                break;
            case ("browser.layout", "layout-checkpoint-completed"):
                ValidateBrowserLayoutCheckpointCompleted(payload, issues);
                break;
            case ("browser.layout", "layout-changes-started"):
                ValidateBrowserLayoutChangesStarted(payload, issues);
                break;
            case ("browser.layout", "layout-transform-node"):
                ValidateBrowserLayoutTransformNode(payload, issues);
                break;
            case ("browser.layout", "layout-node-changed"):
                ValidateBrowserLayoutNodeChanged(payload, issues);
                break;
            case ("browser.layout", "layout-changes-completed"):
                ValidateBrowserLayoutChangesCompleted(payload, issues);
                break;
            case ("browser.layout", "layout-scroll-offset-changed"):
                ValidateBrowserLayoutScrollOffsetChanged(payload, issues);
                break;
            case ("browser.presentation", "presentation-requested"):
                ValidateBrowserPresentationRequested(payload, issues);
                break;
            case ("browser.presentation", "presentation-not-swapped"):
                ValidateBrowserPresentationNotSwapped(payload, issues);
                break;
            case ("browser.presentation", "presentation-swapped"):
                ValidateBrowserPresentationSwapped(payload, issues);
                break;
            case ("browser.presentation", "presentation-feedback"):
                ValidateBrowserPresentationFeedback(payload, issues);
                break;
            case ("browser.animation", "animation-updated"):
                ValidateBrowserAnimationUpdated(payload, issues);
                break;
            case ("browser.script", "script-parsed"):
                ValidateBrowserScriptParsed(payload, issues);
                break;
            case ("browser.script", "script-text"):
                ValidateBrowserResourceBytes(payload, issues);
                break;
            case ("browser.animation", "animation-removed"):
                ValidateShape(
                    payload,
                    [
                        RequiredObject("context"),
                        RequiredDecimalText("sequenceNumber")
                    ],
                    issues);
                ValidateCompositorRendererContext(payload, false, issues);
                break;
            case ("browser.compositor", "compositor-animation-started"):
                ValidateBrowserCompositorAnimationStarted(payload, issues);
                break;
            case ("browser.compositor", "compositor-animation-ended"):
                ValidateBrowserCompositorAnimationEnded(payload, issues);
                break;
            case ("browser.compositor", "compositor-frame"):
                ValidateBrowserCompositorFrame(payload, issues);
                break;
            case ("browser.compositor", "compositor-frame-presented"):
                ValidateBrowserCompositorFramePresented(payload, issues);
                break;
            case ("browser.compositor", "paint-worklet-painted"):
                ValidateBrowserPaintWorkletPainted(payload, issues);
                break;
            case ("browser.resources", "font-file"):
            case ("browser.resources", "image-data"):
            case ("browser.resources", "style-sheet-text"):
                ValidateBrowserResourceBytes(payload, issues);
                break;
            case ("browser.resources", "font-face-added"):
            case ("browser.resources", "font-face-removed"):
                ValidateBrowserFontFace(payload, issues);
                break;
            case ("browser.resources", "font-face-loaded"):
                ValidateBrowserFontFaceLoaded(payload, issues);
                break;
            case ("browser.resources", "image-resource"):
                ValidateBrowserImageResource(payload, issues);
                break;
            case ("browser.resources", "image-paint-image"):
                ValidateBrowserImagePaintImage(payload, issues);
                break;
            case ("browser.resources", "style-sheet-resource"):
                ValidateBrowserStyleSheetResource(payload, issues);
                break;
            case ("browser.resources", "style-sheets-updated"):
                ValidateBrowserStyleSheetsUpdated(payload, issues);
                break;
            case ("browser.network", "request-will-be-sent"):
                ValidateBrowserNetworkRequestWillBeSent(payload, issues);
                break;
            case ("browser.network", "response-received"):
                ValidateBrowserNetworkResponseReceived(payload, issues);
                break;
            case ("browser.network", "request-finished"):
                ValidateBrowserNetworkRequestFinished(payload, issues);
                break;
            case ("browser.network", "request-failed"):
                ValidateBrowserNetworkRequestFailed(payload, issues);
                break;
            case ("browser.network", "memory-cache-hit"):
                ValidateBrowserNetworkMemoryCacheHit(payload, issues);
                break;
            case ("browser.network", "request-headers-sent"):
            case ("browser.network", "response-headers-received"):
                ValidateBrowserNetworkWireHeaders(
                    payload,
                    eventType == "response-headers-received",
                    issues);
                break;
            case ("browser.network", "navigation-response"):
                ValidateBrowserNetworkNavigationResponse(payload, issues);
                break;
            case ("browser.network", "websocket-created"):
            case ("browser.network", "websocket-handshake-request"):
            case ("browser.network", "websocket-handshake-response"):
            case ("browser.network", "websocket-message-sent"):
            case ("browser.network", "websocket-message-received"):
            case ("browser.network", "websocket-close-requested"):
            case ("browser.network", "websocket-error"):
            case ("browser.network", "websocket-closed"):
            case ("browser.network", "event-source-message"):
            case ("browser.network", "web-transport-created"):
            case ("browser.network", "web-transport-established"):
            case ("browser.network", "web-transport-close-requested"):
            case ("browser.network", "web-transport-closed"):
                ValidateBrowserNetworkRealtime(payload, eventType, issues);
                break;
            case ("accessibility.uia.events", "collector-omission"):
                ValidateUiaOmission(monotonicNanoseconds, payload, issues);
                break;
            case ("window.foreground", "collector-omission"):
            case ("graphics.desktop.frames", "collector-omission"):
            case ("audio.microphone", "collector-omission"):
            case ("audio.system", "collector-omission"):
                ValidateOmission(payload, issues);
                break;
            case ("browser.lifecycle", "collector-omission"):
            case ("browser.accessibility", "collector-omission"):
            case ("browser.listener", "collector-omission"):
            case ("browser.dispatch", "collector-omission"):
            case ("browser.timer", "collector-omission"):
            case ("browser.scheduler", "collector-omission"):
            case ("browser.navigation", "collector-omission"):
            case ("browser.dom", "collector-omission"):
            case ("browser.cookie", "collector-omission"):
            case ("browser.interaction", "collector-omission"):
            case ("browser.layout", "collector-omission"):
            case ("browser.presentation", "collector-omission"):
            case ("browser.network", "collector-omission"):
            case ("browser.resources", "collector-omission"):
            case ("browser.compositor", "collector-omission"):
            case ("browser.animation", "collector-omission"):
            case ("browser.script", "collector-omission"):
            case ("browser.preferences", "collector-omission"):
                ValidateBrowserOmission(payload, issues);
                break;
            default:
                if (BuiltInChannels.Contains(channel))
                {
                    AddError(
                        issues,
                        "event-type-unsupported",
                        "#/eventType",
                        $"Event type '{eventType}' is not defined for built-in channel '{channel}'.");
                }

                break;
        }
    }

    private static void ValidateLifecycle(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [
                RequiredString("action"),
                RequiredString("state"),
                RequiredDateTime("utc")
            ],
            issues);

    private static void ValidateAnnotation(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [NullableString("note")],
            issues);

    private static void ValidateRawKeyboard(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [
                RequiredInteger("deviceHandle"),
                RequiredInteger("makeCode", nonnegative: true),
                RequiredInteger("flags", nonnegative: true),
                RequiredInteger("virtualKey", nonnegative: true),
                RequiredInteger("message", nonnegative: true),
                RequiredInteger("extraInformation", nonnegative: true)
            ],
            issues);

    private static void ValidateRawMouse(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [
                RequiredInteger("deviceHandle"),
                RequiredEnum("movementMode", "absolute", "relative"),
                RequiredInteger("deltaX"),
                RequiredInteger("deltaY"),
                RequiredInteger("buttonFlags", nonnegative: true),
                RequiredInteger("buttonData", nonnegative: true),
                RequiredInteger("rawButtons", nonnegative: true),
                RequiredInteger("extraInformation", nonnegative: true),
                RequiredInteger("cursorX"),
                RequiredInteger("cursorY"),
                RequiredInteger("foregroundProcessId", nonnegative: true)
            ],
            issues);

    private static void ValidateForegroundWindow(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredString("reason"),
                NullableInteger("eventThreadId", nonnegative: true),
                NullableInteger("nativeEventTimeMilliseconds", nonnegative: true),
                RequiredInteger("windowHandle"),
                RequiredInteger("processId", nonnegative: true),
                RequiredInteger("threadId", nonnegative: true),
                NullableString("processName"),
                NullableString("processPath"),
                NullableString("title"),
                NullableString("className"),
                RequiredBoolean("isVisible"),
                RequiredBoolean("isMinimized"),
                RequiredBoolean("isMaximized"),
                NullableBoolean("isCloaked"),
                NullableInteger("dpi", nonnegative: true),
                NullableObject("bounds"),
                NullableObject("monitor")
            ],
            issues);

        ValidateOptionalObject(payload, "bounds", ValidateIntegerRectangle, issues);
        ValidateOptionalObject(payload, "monitor", ValidateMonitor, issues);
    }

    // The Windows settings at the start or stop of a recording. See
    // docs/architecture/accessibility-preferences.md, "Windows settings".
    private static void ValidateWindowsPreferences(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredEnum("reason", "start", "stop"),
                RequiredObject("uiSettingsEvents"),
                RequiredObject("settings")
            ],
            issues);
        if (payload.TryGetProperty("uiSettingsEvents", out var events) &&
            events.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                events,
                [.. Recorder.Contracts.WindowsPreferenceSettings.UiSettingsEvents.Select(RequiredBoolean)],
                issues,
                "#/payload/uiSettingsEvents");
        }

        if (payload.TryGetProperty("settings", out var settings) &&
            settings.ValueKind == JsonValueKind.Object)
        {
            ValidateWindowsSettings(settings, null, issues, "#/payload/settings");
        }
    }

    // Protocol 0.56 (accessibility preferences, stage 2). The listed browser
    // preferences of a profile, each a reading of its value, whether that is
    // the default, and the problem that stopped the reading.
    private static void ValidateBrowserPreferences(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredText("profileDirectory"),
                RequiredBoolean("newProfile"),
                RequiredObject("preferences")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, false, issues, "browser-preference-context-invalid", "Browser preference evidence");
        if (payload.TryGetProperty("preferences", out var preferences) &&
            preferences.ValueKind == JsonValueKind.Object)
        {
            ValidateBrowserPreferenceReadings(preferences, null, issues, "#/payload/preferences");
        }
    }

    private static void ValidateBrowserPreferenceChange(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredText("profileDirectory"),
                RequiredString("preference"),
                RequiredObject("previous"),
                RequiredObject("current")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, false, issues, "browser-preference-context-invalid", "Browser preference evidence");
        var preference = payload.TryGetProperty("preference", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()!
            : null;
        if (preference is not null && Recorder.Contracts.BrowserPreferenceSettings.FindBrowser(preference) is null)
        {
            AddError(
                issues,
                "browser-preference-unknown",
                "#/payload/preference",
                $"'{preference}' is not a recorded browser preference.");
            return;
        }

        if (preference is null)
        {
            return;
        }

        // The previous reading is absent when none was recorded before.
        if (payload.TryGetProperty("previous", out var previous) && previous.ValueKind == JsonValueKind.Object &&
            previous.EnumerateObject().Any())
        {
            ValidateBrowserPreferenceReadings(previous, preference, issues, "#/payload/previous");
        }

        if (payload.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object)
        {
            ValidateBrowserPreferenceReadings(current, preference, issues, "#/payload/current");
        }
    }

    // Every listed preference when only is null; otherwise exactly that one.
    private static void ValidateBrowserPreferenceReadings(
        JsonElement readings,
        string? only,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        var expected = only is null
            ? Recorder.Contracts.BrowserPreferenceSettings.Browser
            : [Recorder.Contracts.BrowserPreferenceSettings.FindBrowser(only)!];
        ValidateShape(readings, [.. expected.Select(setting => RequiredObject(setting.Name))], issues, path);
        foreach (var setting in expected)
        {
            if (!readings.TryGetProperty(setting.Name, out var reading) || reading.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var readingPath = $"{path}/{setting.Name}";
            ValidateShape(
                reading,
                [
                    new PropertyRule(
                        "value",
                        true,
                        true,
                        value => BrowserPreferenceValueIsValid(setting.Kind, value),
                        $"must be a {setting.Kind} value or null"),
                    NullableBoolean("isDefault"),
                    NullableString("problem")
                ],
                issues,
                readingPath);
            var hasValue = reading.TryGetProperty("value", out var value) && value.ValueKind != JsonValueKind.Null;
            var hasDefault = reading.TryGetProperty("isDefault", out var isDefault) &&
                isDefault.ValueKind != JsonValueKind.Null;
            var hasProblem = reading.TryGetProperty("problem", out var problem) && problem.ValueKind != JsonValueKind.Null;
            if (hasValue == hasProblem || hasDefault != hasValue)
            {
                AddError(
                    issues,
                    "browser-preference-reading-inconsistent",
                    readingPath,
                    "A reading holds a value, whether it is the default, and no problem, or only the problem.");
            }
        }
    }

    private static bool BrowserPreferenceValueIsValid(Recorder.Contracts.BrowserPreferenceKind kind, JsonElement value) =>
        kind switch
        {
            Recorder.Contracts.BrowserPreferenceKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            Recorder.Contracts.BrowserPreferenceKind.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            Recorder.Contracts.BrowserPreferenceKind.Number => value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number) && double.IsFinite(number),
            Recorder.Contracts.BrowserPreferenceKind.Text => value.ValueKind == JsonValueKind.String,
            Recorder.Contracts.BrowserPreferenceKind.TextList => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
            _ => false
        };

    // The preferences sent to a page's view. A first record holds every
    // field; a later one at least one, each of its listed type.
    private static void ValidateWebPreferencesSent(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredInteger("pageFrameTreeNodeId"),
                RequiredBoolean("primaryPage"),
                RequiredInteger("rendererProcessId"),
                RequiredDecimalText("viewId"),
                RequiredEnum("point", [.. Recorder.Contracts.BrowserPreferenceSettings.SendPoints]),
                RequiredBoolean("first"),
                RequiredObject("fields")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, false, issues, "browser-preference-context-invalid", "Browser preference evidence");
        if (!payload.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ValidateShape(
            fields,
            [
                .. Recorder.Contracts.BrowserPreferenceSettings.Page.Select(setting => new PropertyRule(
                    setting.Name,
                    false,
                    false,
                    value => BrowserPreferenceValueIsValid(setting.Kind, value),
                    $"must be a {setting.Kind} value"))
            ],
            issues,
            "#/payload/fields");
        var first = payload.TryGetProperty("first", out var firstValue) && firstValue.ValueKind == JsonValueKind.True;
        if (!first && !fields.EnumerateObject().Any())
        {
            AddError(
                issues,
                "browser-web-preferences-sent-empty",
                "#/payload/fields",
                "A record after the first holds at least one changed field.");
        }

        if (first)
        {
            var missing = Recorder.Contracts.BrowserPreferenceSettings.Page
                .Where(setting => !fields.TryGetProperty(setting.Name, out _))
                .Select(setting => setting.Name)
                .ToList();
            if (missing.Count > 0)
            {
                AddError(
                    issues,
                    "browser-web-preferences-sent-incomplete",
                    "#/payload/fields",
                    $"A view's first record holds every listed field; it lacks {string.Join(", ", missing)}.");
            }
        }
    }

    // Protocol 0.57: the color maps sent to a page's view. A first record
    // holds all three maps; a later one at least one. Each map holds every
    // listed color, written "#AARRGGBB".
    private static void ValidateColorMapsSent(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredInteger("pageFrameTreeNodeId"),
                RequiredBoolean("primaryPage"),
                RequiredInteger("rendererProcessId"),
                RequiredDecimalText("viewId"),
                RequiredEnum("point", [.. Recorder.Contracts.BrowserPreferenceSettings.ColorMapSendPoints]),
                RequiredBoolean("first"),
                RequiredObject("maps")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, false, issues, "browser-preference-context-invalid", "Browser preference evidence");
        if (!payload.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ValidateShape(
            maps,
            [
                .. Recorder.Contracts.BrowserPreferenceSettings.ColorMapNames.Select(name => new PropertyRule(
                    name,
                    false,
                    false,
                    value => value.ValueKind == JsonValueKind.Object,
                    "must be an object"))
            ],
            issues,
            "#/payload/maps");
        var first = payload.TryGetProperty("first", out var firstValue) && firstValue.ValueKind == JsonValueKind.True;
        var present = Recorder.Contracts.BrowserPreferenceSettings.ColorMapNames
            .Where(name => maps.TryGetProperty(name, out _))
            .ToList();
        if (first && present.Count != Recorder.Contracts.BrowserPreferenceSettings.ColorMapNames.Count)
        {
            AddError(
                issues,
                "browser-color-maps-sent-incomplete",
                "#/payload/maps",
                "A view's first record holds the light, dark, and forced colors maps.");
        }
        else if (!first && present.Count == 0)
        {
            AddError(
                issues,
                "browser-color-maps-sent-empty",
                "#/payload/maps",
                "A record after the first holds at least one changed map.");
        }

        foreach (var name in present)
        {
            var map = maps.GetProperty(name);
            if (map.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            ValidateShape(
                map,
                [
                    .. Recorder.Contracts.BrowserPreferenceSettings.RendererColorNames.Select(color => new PropertyRule(
                        color,
                        true,
                        false,
                        value => value.ValueKind == JsonValueKind.String && IsArgbColor(value.GetString()!),
                        "must be a color written #AARRGGBB"))
                ],
                issues,
                $"#/payload/maps/{name}");
        }
    }

    private static bool IsArgbColor(string text) =>
        text.Length == 9 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);

    private static void ValidateZoomLevelChange(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredEnum("mode", [.. Recorder.Contracts.BrowserPreferenceSettings.ZoomModes]),
                RequiredBoolean("followsDefault"),
                RequiredText("host"),
                RequiredText("scheme"),
                RequiredNumber("zoomLevel"),
                RequiredNumber("zoomPercent", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, false, issues, "browser-preference-context-invalid", "Browser preference evidence");
        if (payload.TryGetProperty("zoomLevel", out var level) && level.TryGetDouble(out var zoomLevel) &&
            payload.TryGetProperty("zoomPercent", out var percent) && percent.TryGetDouble(out var zoomPercent) &&
            Math.Abs(Math.Pow(1.2, zoomLevel) * 100.0 - zoomPercent) > 1e-6 * Math.Max(1.0, zoomPercent))
        {
            AddError(
                issues,
                "browser-zoom-percent-inconsistent",
                "#/payload/zoomPercent",
                "The percentage is 1.2 to the power of the zoom level, times 100.");
        }
    }

    // One setting's change, with its reading before and after and the notice
    // that led to the reading.
    private static void ValidateWindowsPreferenceChange(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredString("setting"),
                RequiredObject("previous"),
                RequiredObject("current"),
                RequiredObject("notice")
            ],
            issues);
        var setting = payload.TryGetProperty("setting", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()!
            : null;
        if (setting is not null &&
            (Recorder.Contracts.WindowsPreferenceSettings.Find(setting) is null ||
                setting == Recorder.Contracts.WindowsPreferenceSettings.CaretBlinkTime))
        {
            AddError(
                issues,
                "windows-preference-unknown",
                "#/payload/setting",
                $"'{setting}' is not a Windows setting recorded as a change.");
            setting = null;
        }

        foreach (var side in new[] { "previous", "current" })
        {
            if (setting is not null &&
                payload.TryGetProperty(side, out var readings) &&
                readings.ValueKind == JsonValueKind.Object)
            {
                ValidateWindowsSettings(readings, setting, issues, $"#/payload/{side}");
            }
        }

        if (payload.TryGetProperty("notice", out var notice) && notice.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                notice,
                [
                    RequiredEnum("kind", "setting-change", "registry", "ui-settings", "display-change", "dpi-changed", "stop"),
                    NullableInteger("uiAction", nonnegative: true),
                    NullableString("area"),
                    NullableString("source")
                ],
                issues,
                "#/payload/notice");
        }
    }

    // Every setting when only is null; otherwise exactly that one. Each is
    // {value, problem}: a value of the setting's type and no problem, or a
    // null value and the problem. Only a text value may be null without a
    // problem, as the contrast theme's name is when Windows gives none.
    private static void ValidateWindowsSettings(
        JsonElement settings,
        string? only,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        var expected = only is null
            ? Recorder.Contracts.WindowsPreferenceSettings.All
            : [Recorder.Contracts.WindowsPreferenceSettings.Find(only)!];
        ValidateShape(settings, [.. expected.Select(setting => RequiredObject(setting.Name))], issues, path);
        foreach (var setting in expected)
        {
            if (!settings.TryGetProperty(setting.Name, out var reading) || reading.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var readingPath = $"{path}/{setting.Name}";
            ValidateShape(
                reading,
                [
                    new PropertyRule(
                        "value",
                        true,
                        true,
                        value => WindowsSettingValueIsValid(setting.Kind, value),
                        $"must be a {setting.Kind} value or null"),
                    NullableString("problem")
                ],
                issues,
                readingPath);
            var hasValue = reading.TryGetProperty("value", out var value) && value.ValueKind != JsonValueKind.Null;
            var hasProblem = reading.TryGetProperty("problem", out var problem) && problem.ValueKind != JsonValueKind.Null;
            if (hasValue && hasProblem ||
                !hasValue && !hasProblem && setting.Kind != Recorder.Contracts.WindowsPreferenceKind.Text)
            {
                AddError(
                    issues,
                    "windows-preference-reading-inconsistent",
                    readingPath,
                    "A reading holds a value and no problem, or a null value and the problem.");
            }
        }
    }

    private static bool WindowsSettingValueIsValid(Recorder.Contracts.WindowsPreferenceKind kind, JsonElement value) =>
        value.ValueKind == JsonValueKind.Null || kind switch
        {
            Recorder.Contracts.WindowsPreferenceKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            Recorder.Contracts.WindowsPreferenceKind.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            Recorder.Contracts.WindowsPreferenceKind.Number => value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number) && double.IsFinite(number),
            Recorder.Contracts.WindowsPreferenceKind.Text => value.ValueKind == JsonValueKind.String,
            Recorder.Contracts.WindowsPreferenceKind.Monitors => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(MonitorReadingIsValid),
            _ => false
        };

    private static bool MonitorReadingIsValid(JsonElement monitor)
    {
        if (monitor.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var issues = new List<EventValidationIssue>();
        ValidateShape(
            monitor,
            [
                RequiredString("deviceName"),
                RequiredObject("bounds"),
                RequiredBoolean("isPrimary"),
                NullableInteger("dpiX", nonnegative: true),
                NullableInteger("dpiY", nonnegative: true)
            ],
            issues);
        ValidateOptionalObject(monitor, "bounds", ValidateIntegerRectangle, issues);
        return issues.Count == 0;
    }

    private static void ValidateUiaEvent(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredString("eventId"),
                NullableString("changeType"),
                NullableIntegerArray("runtimeId"),
                NullableString("newValue"),
                RequiredObject("element")
            ],
            issues);

        if (payload.TryGetProperty("element", out var element) &&
            element.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                element,
                [
                    NullableInteger("processId"),
                    NullableInteger("nativeWindowHandle"),
                    NullableString("automationId"),
                    NullableString("name"),
                    NullableString("className"),
                    NullableString("frameworkId"),
                    NullableString("controlType"),
                    NullableString("localizedControlType"),
                    NullableBoolean("hasKeyboardFocus"),
                    NullableBoolean("isKeyboardFocusable"),
                    NullableBoolean("isEnabled"),
                    NullableBoolean("isOffscreen"),
                    NullableObject("boundingRectangle"),
                    OptionalEnum("propertySource", "event-cache", "current-read"),
                    RequiredStringArray("qualityFlags")
                ],
                issues,
                "#/payload/element");
            ValidateOptionalObject(
                element,
                "boundingRectangle",
                ValidateNumberRectangle,
                issues,
                "#/payload/element");
        }
    }

    private static void ValidateDesktopFrame(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredSafePath("path"),
                RequiredInteger("x"),
                RequiredInteger("y"),
                RequiredInteger("width", positive: true),
                RequiredInteger("height", positive: true),
                RequiredInteger("stride", positive: true),
                RequiredString("pixelFormat"),
                RequiredString("encodedFormat"),
                RequiredInteger("byteLength", nonnegative: true),
                RequiredInteger("captureDurationNanoseconds", nonnegative: true),
                RequiredInteger("framesPerSecond", positive: true),
                RequiredEnum("backend", "windows-graphics-capture", "gdi-bitblt"),
                NullableInteger("monitorCount", nonnegative: true),
                NullableString("fallbackReason"),
                RequiredInteger("gdiFallbackFrameCount", nonnegative: true),
                OptionalNullableEnum("frameSelection", "newest-arrived"),
                OptionalObjectArray("monitorFrames"),
                OptionalObject("fullscreenMagnification"),
                OptionalObject("fullscreenColorEffect")
            ],
            issues);
        ValidateDesktopMonitorFrames(payload, issues);
        ValidateOptionalObject(payload, "fullscreenMagnification", ValidateFullscreenMagnification, issues);
        ValidateOptionalObject(payload, "fullscreenColorEffect", ValidateFullscreenColorEffect, issues);
    }

    // A change of the Magnifier's readings between two desktop frames, at the
    // later frame's time: what changed, from the readings of both frames,
    // which are as a desktop frame holds them. See
    // docs/architecture/accessibility-preferences.md.
    private static void ValidateMagnifierChange(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredInteger("frameSequence", nonnegative: true),
                RequiredInteger("previousFrameAt", nonnegative: true),
                RequiredObject("changed"),
                RequiredObject("previous"),
                RequiredObject("current")
            ],
            issues);
        List<string>? named = null;
        if (payload.TryGetProperty("changed", out var changed) && changed.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                changed,
                [.. Recorder.Contracts.MagnifierChanges.Parts.Select(RequiredBoolean)],
                issues,
                "#/payload/changed");
            if (Recorder.Contracts.MagnifierChanges.Parts.All(part =>
                    changed.TryGetProperty(part, out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                named = [.. Recorder.Contracts.MagnifierChanges.Parts.Where(part => changed.GetProperty(part).GetBoolean())];
                if (named.Count == 0)
                {
                    AddError(
                        issues,
                        "magnifier-change-empty",
                        "#/payload/changed",
                        "A Magnifier change has at least one of level, position, and colorEffect changed.");
                    named = null;
                }
            }
        }

        foreach (var side in new[] { "previous", "current" })
        {
            if (payload.TryGetProperty(side, out var readings) && readings.ValueKind == JsonValueKind.Object)
            {
                var path = $"#/payload/{side}";
                ValidateShape(
                    readings,
                    [RequiredObject("fullscreenMagnification"), RequiredObject("fullscreenColorEffect")],
                    issues,
                    path);
                ValidateOptionalObject(readings, "fullscreenMagnification", ValidateFullscreenMagnification, issues, path);
                ValidateOptionalObject(readings, "fullscreenColorEffect", ValidateFullscreenColorEffect, issues, path);
            }
        }

        if (named is not null &&
            MagnifierReadings(payload, "previous") is { } before &&
            MagnifierReadings(payload, "current") is { } after)
        {
            var expected = Recorder.Contracts.MagnifierChanges.Compare(before.Magnification, before.ColorEffect, after.Magnification, after.ColorEffect);
            if (!expected.SequenceEqual(named))
            {
                AddError(
                    issues,
                    "magnifier-change-inconsistent",
                    "#/payload/changed",
                    $"The readings before and after differ in {(expected.Count == 0 ? "nothing" : string.Join(", ", expected))}.");
            }
        }
    }

    // The two readings of one side of a Magnifier change, or null when either
    // is not of the shape a desktop frame holds.
    internal static (Recorder.Contracts.MagnificationReading Magnification, Recorder.Contracts.ColorEffectReading ColorEffect)? MagnifierReadings(
        JsonElement payload,
        string side)
    {
        if (!payload.TryGetProperty(side, out var readings) || readings.ValueKind != JsonValueKind.Object ||
            !readings.TryGetProperty("fullscreenMagnification", out var magnification) || magnification.ValueKind != JsonValueKind.Object ||
            !readings.TryGetProperty("fullscreenColorEffect", out var effect) || effect.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        static double? Number(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
        static int? Integer(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
        static string? Text(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        double[]? matrix = effect.TryGetProperty("matrix", out var values) && values.ValueKind == JsonValueKind.Array &&
            values.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number)
            ? [.. values.EnumerateArray().Select(item => item.GetDouble())]
            : null;
        return (
            new Recorder.Contracts.MagnificationReading(Number(magnification, "level"), Integer(magnification, "x"), Integer(magnification, "y"), Text(magnification, "problem")),
            new Recorder.Contracts.ColorEffectReading(matrix, Text(effect, "problem")));
    }

    // Archives written before the color effect was read with each frame omit
    // fullscreenColorEffect. When present it holds the 25 values of the
    // matrix read with MagGetFullscreenColorEffect, row by row, or a null
    // matrix and the problem. See docs/architecture/magnified-view-playback.md,
    // "Color effect".
    private static void ValidateFullscreenColorEffect(
        JsonElement effect,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        ValidateShape(
            effect,
            [
                new PropertyRule(
                    "matrix",
                    true,
                    true,
                    value => value.ValueKind == JsonValueKind.Array &&
                        value.GetArrayLength() == 25 &&
                        value.EnumerateArray().All(item =>
                            item.ValueKind == JsonValueKind.Number &&
                            item.TryGetDouble(out var number) &&
                            double.IsFinite(number)),
                    "must be an array of 25 finite numbers or null"),
                NullableString("problem")
            ],
            issues,
            path);
        var read = effect.TryGetProperty("matrix", out var matrix) &&
            matrix.ValueKind != JsonValueKind.Null;
        var problem = effect.TryGetProperty("problem", out var problemValue) &&
            problemValue.ValueKind == JsonValueKind.String;
        if (read == problem)
        {
            AddError(
                issues,
                "desktop-frame-color-effect-inconsistent",
                path,
                "A full screen color effect reading states its matrix and no problem, " +
                "or no matrix and the problem.");
        }
    }

    // Archives written before the transform was read with each frame omit
    // fullscreenMagnification. When present it holds the level and offsets
    // read with MagGetFullscreenTransform, or nulls and the problem that
    // stopped the reading. See docs/architecture/magnified-view-playback.md.
    private static void ValidateFullscreenMagnification(
        JsonElement magnification,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        ValidateShape(
            magnification,
            [
                RequiredNullableNumber("level", nonnegative: true),
                NullableInteger("x"),
                NullableInteger("y"),
                NullableString("problem")
            ],
            issues,
            path);
        var read = new[] { "level", "x", "y" }
            .Count(name => magnification.TryGetProperty(name, out var value) &&
                value.ValueKind != JsonValueKind.Null);
        var problem = magnification.TryGetProperty("problem", out var problemValue) &&
            problemValue.ValueKind == JsonValueKind.String;
        if (problem ? read != 0 : read != 3)
        {
            AddError(
                issues,
                "desktop-frame-magnification-inconsistent",
                path,
                "A full screen magnification reading states its level and both offsets " +
                "and no problem, or no level or offsets and the problem.");
        }

        if (magnification.TryGetProperty("level", out var level) &&
            level.ValueKind == JsonValueKind.Number &&
            level.TryGetDouble(out var number) &&
            number <= 0)
        {
            AddError(
                issues,
                "desktop-frame-magnification-level",
                $"{path}/level",
                "A full screen magnification level is positive.");
        }
    }

    // Archives written before per-monitor composition timing omit
    // monitorFrames. When it is present, a WGC frame states the compositor
    // time of every monitor's copied image and a GDI fallback frame states
    // none, because GDI has no composition time.
    private static void ValidateDesktopMonitorFrames(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("monitorFrames", out var monitorFrames) ||
            monitorFrames.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var isWindowsGraphicsCapture =
            payload.TryGetProperty("backend", out var backend) &&
            backend.ValueKind == JsonValueKind.String &&
            backend.GetString() == "windows-graphics-capture";
        // Archives written before the recorder kept only the newest arrived
        // frame have no frameSelection and no per-monitor selection fields.
        var selectsNewestArrived =
            payload.TryGetProperty("frameSelection", out var frameSelection) &&
            frameSelection.ValueKind == JsonValueKind.String;
        if (selectsNewestArrived && !isWindowsGraphicsCapture)
        {
            AddError(
                issues,
                "desktop-frame-selection-inconsistent",
                "#/payload/frameSelection",
                "Only a Windows Graphics Capture frame has a frame selection; " +
                    "a GDI fallback frame must leave it null.");
        }

        var index = 0;
        foreach (var monitorFrame in monitorFrames.EnumerateArray())
        {
            var pointer = $"#/payload/monitorFrames/{index}";
            index++;
            if (monitorFrame.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            ValidateShape(
                monitorFrame,
                [
                    RequiredInteger("monitorHandle"),
                    RequiredInteger("x"),
                    RequiredInteger("y"),
                    RequiredInteger("width", positive: true),
                    RequiredInteger("height", positive: true),
                    NullableInteger("systemRelativeTimeTicks", positive: true),
                    NullableInteger("compositedAtNanoseconds"),
                    NullableInteger("dequeuedAtNanoseconds"),
                    NullableInteger("tryGetNextFrameAttempts", positive: true),
                    OptionalNullableInteger("supersededFrameCount", nonnegative: true),
                    OptionalNullableBoolean("reusedPreviousImage")
                ],
                issues,
                pointer);

            var timed = new[]
            {
                "systemRelativeTimeTicks",
                "compositedAtNanoseconds",
                "dequeuedAtNanoseconds",
                "tryGetNextFrameAttempts"
            }.Select(name =>
                monitorFrame.TryGetProperty(name, out var value) &&
                value.ValueKind != JsonValueKind.Null)
            .ToArray();
            var expected = isWindowsGraphicsCapture;
            if (timed.Any(present => present != expected))
            {
                AddError(
                    issues,
                    "desktop-monitor-frame-timing-inconsistent",
                    pointer,
                    expected
                        ? "A Windows Graphics Capture frame must state the " +
                            "composition time and dequeue attempts of every monitor image."
                        : "A GDI fallback frame has no composition time, so its " +
                            "monitor entries must leave the timing fields null.");
            }

            var selectionStated = new[] { "supersededFrameCount", "reusedPreviousImage" }
                .Select(name =>
                    monitorFrame.TryGetProperty(name, out var value) &&
                    value.ValueKind != JsonValueKind.Null)
                .ToArray();
            var selectionExpected = selectsNewestArrived && isWindowsGraphicsCapture;
            if (selectionStated.Any(present => present != selectionExpected))
            {
                AddError(
                    issues,
                    "desktop-monitor-frame-selection-inconsistent",
                    pointer,
                    selectionExpected
                        ? "A frame that keeps the newest arrived image must state, " +
                            "for every monitor, how many arrived frames it released " +
                            "and whether it reused the previous image."
                        : "Only a frame that keeps the newest arrived image states " +
                            "released frames and image reuse.");
            }

            if (monitorFrame.TryGetProperty("reusedPreviousImage", out var reused) &&
                reused.ValueKind == JsonValueKind.True &&
                monitorFrame.TryGetProperty("supersededFrameCount", out var superseded) &&
                IsInteger(superseded) &&
                superseded.GetInt64() != 0)
            {
                AddError(
                    issues,
                    "desktop-monitor-frame-selection-inconsistent",
                    pointer,
                    "A reused image means no frame arrived since the previous " +
                        "capture, so no arrived frame can have been released.");
            }

            // The composition time is not ordered with the dequeue time.
            // Windows validation found SystemRelativeTime up to one display
            // refresh after the pool delivered the frame; see
            // docs/architecture/wgc-newest-frame-selection.md.
        }

        if (isWindowsGraphicsCapture &&
            payload.TryGetProperty("monitorCount", out var monitorCount) &&
            monitorCount.ValueKind == JsonValueKind.Number &&
            monitorCount.TryGetInt64(out var count) &&
            count != monitorFrames.GetArrayLength())
        {
            AddError(
                issues,
                "desktop-monitor-frame-count-inconsistent",
                "#/payload/monitorFrames",
                $"The frame records {monitorFrames.GetArrayLength()} monitor " +
                    $"images for {count} captured monitors.");
        }
    }

    private static void ValidateAudioStream(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredEnum("stream", "microphone", "system"),
                RequiredSafePath("path"),
                RequiredString("device"),
                OptionalNullableNumber("endpointVolumeScalar", nonnegative: true),
                RequiredObject("format"),
                RequiredInteger("dataBytes", nonnegative: true),
                RequiredInteger("buffersObserved", nonnegative: true),
                RequiredInteger("buffersDropped", nonnegative: true),
                OptionalNullableNumber("peakAmplitude", nonnegative: true),
                OptionalNullableNumber("peakDbfs"),
                OptionalNullableNumber("rmsAmplitude", nonnegative: true),
                OptionalNullableNumber("rmsDbfs")
            ],
            issues);

        if (payload.TryGetProperty("format", out var format) &&
            format.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                format,
                [
                    RequiredString("encoding"),
                    RequiredInteger("sampleRate", positive: true),
                    RequiredInteger("channels", positive: true),
                    RequiredInteger("bitsPerSample", positive: true),
                    RequiredInteger("blockAlign", positive: true),
                    RequiredInteger("averageBytesPerSecond", positive: true)
                ],
                issues,
                "#/payload/format");
        }
    }

    private static void ValidateAudioBuffer(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [
                RequiredEnum("stream", "microphone", "system"),
                RequiredSafePath("path"),
                RequiredInteger("bufferSequence", nonnegative: true),
                RequiredInteger("dataByteOffset", nonnegative: true),
                RequiredInteger("byteLength", nonnegative: true),
                RequiredInteger("sampleFrames", nonnegative: true),
                RequiredInteger("durationNanoseconds", nonnegative: true),
                RequiredInteger("estimatedFirstSampleMonotonicNanoseconds", nonnegative: true),
                RequiredInteger("callbackMonotonicNanoseconds", nonnegative: true)
            ],
            issues);

    private static void ValidateAudioError(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [
                RequiredEnum("stream", "microphone", "system"),
                NullableString("errorType"),
                NullableString("message")
            ],
            issues);

    // A lifecycle record states which process connected and what it spoke. The
    // receiver already requires a browser process to carry neither a parent nor
    // a child process identifier and a renderer to carry both, so both are
    // present here and null for the browser process rather than absent.
    private static void ValidateBrowserConnected(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [
                RequiredString("protocolVersion"),
                RequiredString("browserInstanceId"),
                RequiredInteger("processId", positive: true),
                RequiredEnum("processType", "browser", "renderer"),
                RequiredText("chromiumVersion"),
                NullableInteger("parentProcessId", nonnegative: true),
                NullableInteger("childProcessId", nonnegative: true)
            ],
            issues);

    // The exit code is the process exit status Windows reports, which is signed
    // here and also given as the unsigned hexadecimal form Windows status codes
    // are usually written in. The exit time is null when Windows could not
    // report it.
    private static void ValidateBrowserExited(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [
                RequiredString("browserInstanceId"),
                RequiredInteger("processId", positive: true),
                RequiredInteger("exitCode"),
                RequiredString("exitCodeHex"),
                NullableDateTime("exitedUtc"),
                RequiredBoolean("requestedByRecorder")
            ],
            issues);

    // The clock record carries the mapping identity and the uncertainty the
    // recorder estimated for it, which is a nonnegative half round trip rather
    // than a signed offset. The browser's tick frequency is a decimal string,
    // because it does not fit a JSON number on every platform.
    private static void ValidateBrowserClockSynchronized(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [
                RequiredString("protocolVersion"),
                RequiredString("browserInstanceId"),
                RequiredInteger("processId", positive: true),
                RequiredEnum("processType", "browser", "renderer"),
                NullableInteger("parentProcessId", nonnegative: true),
                NullableInteger("childProcessId", nonnegative: true),
                RequiredString("clockMappingId"),
                RequiredString("monotonicFrequency"),
                RequiredInteger("uncertaintyNanoseconds", nonnegative: true)
            ],
            issues);

    private static void ValidateBrowserAccessibilityCheckpointStarted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "renderer-serialization"),
                RequiredInteger("maximumNodes", positive: true),
                RequiredInteger("updateCount", nonnegative: true),
                RequiredInteger("eventCount", nonnegative: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererTokenContext(payload, issues);
    }

    private static void ValidateBrowserAccessibilityCheckpointNode(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredInteger("nodeIndex", nonnegative: true),
                RequiredInteger("accessibilityNodeId"),
                NullableInteger("parentAccessibilityNodeId"),
                NullableInteger("domNodeId", nonnegative: true),
                RequiredInteger("role", nonnegative: true),
                RequiredString("roleName"),
                RequiredText("name"),
                RequiredText("description"),
                RequiredText("serializedProperties"),
                RequiredBoolean("focused")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererTokenContext(payload, issues);
    }

    private static void ValidateBrowserAccessibilityCheckpointCompleted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "renderer-serialization"),
                RequiredInteger("nodeCount", nonnegative: true),
                RequiredBoolean("truncated"),
                RequiredInteger("maximumNodes", positive: true),
                RequiredInteger("updateCount", nonnegative: true),
                RequiredInteger("eventCount", nonnegative: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererTokenContext(payload, issues);
    }

    private static void ValidateBrowserListener(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("listenerId"),
                RequiredString("eventName"),
                RequiredEnum(
                    "registrationKind",
                    "add-event-listener",
                    "inline-attribute",
                    "event-handler-property",
                    "native"),
                RequiredObject("target"),
                RequiredBoolean("capture"),
                RequiredBoolean("passive"),
                RequiredBoolean("once"),
                NullableObject("location"),
                NullableObject("world"),
                OptionalObject("scope")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserEventTargetProperty(payload, "target", issues);
        ValidateBrowserLocationProperty(payload, issues);
        ValidateBrowserExecutionWorldProperty(payload, issues);
        ValidateNetworkScopeProperty(payload, issues);
        ValidateBrowserEventScope(payload, ["target"], issues);
    }

    private static void ValidateBrowserDispatch(
        JsonElement payload,
        ICollection<EventValidationIssue> issues,
        bool requireDefaultAction)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("dispatchId"),
                RequiredString("eventName"),
                RequiredBoolean("trusted"),
                NullableObject("originalTarget"),
                RequiredObjectArray("composedPath"),
                RequiredEnum("phase", "none", "capturing", "at-target", "bubbling"),
                NullableString("listenerId"),
                RequiredBoolean("defaultPrevented"),
                RequiredBoolean("propagationStopped"),
                RequiredBoolean("immediatePropagationStopped"),
                requireDefaultAction
                    ? RequiredEnum(
                        "defaultAction",
                        "blink-default-event-handler")
                    : NullableString("defaultAction"),
                requireDefaultAction
                    ? RequiredEnum(
                        "outcome",
                        "invoked",
                        "suppressed-by-event-handler",
                        "already-handled",
                        "ineligible-untrusted-event")
                    : NullableString("outcome"),
                requireDefaultAction
                    ? RequiredObject("currentTarget")
                    : OptionalNullableObject("currentTarget"),
                RequiredObjectArray("pathScopes"),
                OptionalObject("scope")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserEventTargetProperty(payload, "originalTarget", issues);
        ValidateBrowserEventTargetProperty(payload, "currentTarget", issues);
        ValidateBrowserEventTargetArrayProperty(payload, "composedPath", issues);
        ValidateBrowserDispatchPathScopes(payload, issues);
        ValidateNetworkScopeProperty(payload, issues);
        ValidateBrowserEventScope(
            payload,
            ["originalTarget", "currentTarget", "composedPath"],
            issues);
    }

    private static readonly string[] DocumentFreeScopeKinds =
    [
        "dedicated-worker", "shared-worker", "service-worker", "worklet"
    ];

    // From protocol 0.31 a listener or dispatch record names the execution
    // context it belongs to. A worker or worklet scope has no document, so its
    // record names none, and only a non-Node target can be recorded there. A
    // record in a window scope, or an earlier record with no scope, belongs to
    // a document and names it on every target.
    private static void ValidateBrowserEventScope(
        JsonElement payload,
        IReadOnlyList<string> targetProperties,
        ICollection<EventValidationIssue> issues)
    {
        string? scopeKind = null;
        if (payload.TryGetProperty("scope", out var scope) &&
            scope.ValueKind == JsonValueKind.Object)
        {
            scopeKind = ReadString(scope, "contextKind");
        }

        var documentFree = scopeKind is not null &&
            DocumentFreeScopeKinds.Contains(scopeKind, StringComparer.Ordinal);
        if (documentFree &&
            payload.TryGetProperty("context", out var context) &&
            context.ValueKind == JsonValueKind.Object &&
            HasNonnullProperty(context, "documentId"))
        {
            AddError(
                issues,
                "browser-event-scope-inconsistent",
                "#/payload/context/documentId",
                $"A record in a {scopeKind} scope belongs to no document.");
        }

        foreach (var property in targetProperties)
        {
            if (!payload.TryGetProperty(property, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                CheckTarget(value, $"#/payload/{property}");
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var entry in value.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object)
                    {
                        CheckTarget(
                            entry,
                            $"#/payload/{property}/{index}");
                    }
                    index++;
                }
            }
        }

        void CheckTarget(JsonElement target, string path)
        {
            var hasDocument = HasNonnullProperty(target, "documentId");
            var kind = ReadString(target, "kind");
            if (documentFree && hasDocument)
            {
                AddError(
                    issues,
                    "browser-event-scope-inconsistent",
                    $"{path}/documentId",
                    $"A target in a {scopeKind} scope belongs to no document.");
            }
            else if (documentFree && kind != "other")
            {
                AddError(
                    issues,
                    "browser-event-scope-inconsistent",
                    path,
                    $"A {scopeKind} scope has no {kind} event target.");
            }
            else if (!documentFree && !hasDocument)
            {
                AddError(
                    issues,
                    "browser-event-scope-inconsistent",
                    $"{path}/documentId",
                    scopeKind is null
                        ? "A record that names no scope must name its document."
                        : $"A target in a {scopeKind} scope must name its document.");
            }
        }
    }

    // Each composed path entry has one scope record at the same index, and
    // every visible path index names an entry of the recorded path.
    private static void ValidateBrowserDispatchPathScopes(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("pathScopes", out var scopes) ||
            scopes.ValueKind != JsonValueKind.Array ||
            !payload.TryGetProperty("composedPath", out var path) ||
            path.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var pathLength = path.GetArrayLength();
        if (scopes.GetArrayLength() != pathLength)
        {
            AddError(
                issues,
                "browser-dispatch-path-scopes-inconsistent",
                "#/payload/pathScopes",
                $"The dispatch records {scopes.GetArrayLength()} path scopes " +
                    $"for a composed path of {pathLength} entries.");
            return;
        }

        var index = 0;
        foreach (var scope in scopes.EnumerateArray())
        {
            var pointer = $"#/payload/pathScopes/{index}";
            ValidateShape(
                scope,
                [
                    NullableInteger("treeScopeRootNodeId", positive: true),
                    NullableEnum("shadowRootMode", "open", "closed", "user-agent"),
                    NullableInteger("targetNodeId", positive: true),
                    NullableInteger("relatedTargetNodeId", positive: true),
                    new PropertyRule(
                        "visiblePathIndexes",
                        true,
                        false,
                        value => value.ValueKind == JsonValueKind.Array &&
                            value.EnumerateArray().All(item =>
                                item.ValueKind == JsonValueKind.Number &&
                                item.TryGetInt32(out var visible) &&
                                visible >= 0 &&
                                visible < pathLength),
                        "must be an array of indexes into the composed path"),
                    RequiredInteger("unmatchedVisibleTargetCount", nonnegative: true)
                ],
                issues,
                pointer);
            if (HasNonnullProperty(scope, "shadowRootMode") &&
                !HasNonnullProperty(scope, "treeScopeRootNodeId"))
            {
                AddError(
                    issues,
                    "browser-dispatch-path-scopes-inconsistent",
                    pointer,
                    "A shadow root mode was recorded without the shadow root " +
                        "that roots the scope.");
            }
            index++;
        }
    }

    private static void ValidateBrowserTimer(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("timerId"),
                RequiredEnum(
                    "timerKind",
                    "timeout",
                    "interval",
                    "animation-frame",
                    "idle-callback",
                    "browser-task"),
                RequiredNullableNumber("requestedDelayMilliseconds", nonnegative: true),
                RequiredNullableNumber("effectiveDelayMilliseconds", nonnegative: true),
                RequiredInteger("nestingLevel", nonnegative: true),
                NullableBoolean("throttled"),
                RequiredEnum(
                    "pageLifecycleState",
                    "unknown",
                    "visible",
                    "hidden",
                    "frozen"),
                NullableObject("callbackLocation"),
                NullableString("cancellationReason"),
                OptionalNullableBoolean("didTimeout")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserLocationProperty(
            payload,
            issues,
            "callbackLocation");
    }

    // Protocol 0.52 (slice 4f): who scheduled a timer.
    private static void ValidateBrowserTimerOrigin(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("timerId"),
                NullableObject("world"),
                RequiredObjectArray("stack"),
                RequiredEnum("handler", "function", "string")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserExecutionWorldProperty(payload, issues);
        if (!payload.TryGetProperty("stack", out var stack) || stack.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        if (stack.GetArrayLength() > 16)
        {
            AddError(
                issues,
                "browser-timer-origin-stack",
                "#/payload/stack",
                "a timer origin carries at most 16 stack frames");
        }
        var index = 0;
        foreach (var frame in stack.EnumerateArray())
        {
            var pointer = $"#/payload/stack/{index}";
            index++;
            if (frame.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            ValidateShape(
                frame,
                [
                    NullableDecimalText("scriptId"),
                    NullableString("url"),
                    NullableString("functionName"),
                    NullableInteger("line", positive: true),
                    NullableInteger("column", positive: true),
                    RequiredBoolean("isEval")
                ],
                issues,
                pointer);
        }
    }

    // Protocol 0.52: the markup a V8 script came from.
    private static void ValidateBrowserScriptCompiled(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredDecimalText("scriptId"),
                RequiredEnum("kind", "classic", "module", "event-handler-attribute"),
                NullableInteger("elementNodeId", positive: true),
                NullableString("attributeName"),
                NullableString("url"),
                NullableInteger("line", positive: true),
                NullableInteger("column", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        var attribute = ReadString(payload, "kind") == "event-handler-attribute";
        if (attribute != HasNonnullProperty(payload, "attributeName"))
        {
            AddError(
                issues,
                "browser-script-compiled-attribute",
                "#/payload/attributeName",
                "An attribute handler names its attribute, and no other script does.");
        }
    }

    // Protocol 0.54 (slice 4h).
    private static void ValidateBrowserScriptParsed(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                NullableObject("world"),
                RequiredDecimalText("scriptId"),
                RequiredEnum("kind", "classic", "module", "eval", "function"),
                NullableString("url"),
                NullableString("sourceUrl"),
                NullableString("sourceMapUrl"),
                NullableInteger("line", positive: true),
                NullableInteger("column", positive: true),
                NullableDecimalText("evalFromScriptId"),
                RequiredBoolean("compileError"),
                DigestRule("digest"),
                RequiredDecimalText("size"),
                RequiredBoolean("textRecorded")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserExecutionWorldProperty(payload, issues);
        if (HasNonnullProperty(payload, "evalFromScriptId") && ReadString(payload, "kind") != "eval")
        {
            AddError(
                issues,
                "browser-script-parsed-eval-from",
                "#/payload/evalFromScriptId",
                "Only eval code names the script that called eval.");
        }
    }

    private static void ValidateBrowserScheduler(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("queueName"),
                RequiredInteger("queueType", nonnegative: true),
                RequiredEnum(
                    "throttlingType",
                    "foreground-unimportant",
                    "background",
                    "background-intensive"),
                RequiredString("desiredWakeUpTicks"),
                RequiredString("allowedWakeUpTicks"),
                RequiredNumber("deferralMilliseconds", positive: true),
                RequiredBoolean("hasReadyTask"),
                RequiredEnum(
                    "blockType",
                    "all-tasks",
                    "new-tasks-only"),
                RequiredEnum(
                    "decisionBoundary",
                    "task-queue-throttler")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);

        var desiredText = ReadString(payload, "desiredWakeUpTicks");
        var allowedText = ReadString(payload, "allowedWakeUpTicks");
        if (!long.TryParse(desiredText, out var desired) ||
            !long.TryParse(allowedText, out var allowed) ||
            desired < 0 ||
            allowed <= desired)
        {
            AddError(
                issues,
                "browser-scheduler-wake-up-order-invalid",
                "#/payload",
                "Scheduler wake-up ticks must be nonnegative decimal integers with allowedWakeUpTicks greater than desiredWakeUpTicks.");
        }
    }

    // Cookie payloads are closed shapes with no field that can hold a cookie
    // value. Because ValidateShape refuses any property a shape does not
    // declare, a record that carried a value under any name would fail here
    // rather than enter the archive.
    private static readonly string[] CookieContextKinds =
        ["window", "service-worker", "other"];

    private static readonly string[] CookieStoreMethods =
        ["get", "getAll", "set", "delete"];

    private static readonly string[] FocusTypes =
    [
        "none",
        "script",
        "forward",
        "backward",
        "spatial-navigation",
        "mouse",
        "access-key",
        "page"
    ];

    private static readonly string[] SelectionDirections =
        ["none", "forward", "backward"];

    private static void ValidateInteractionCommon(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateBrowserLocationProperty(payload, issues);
        ValidateBrowserExecutionWorldProperty(payload, issues);
    }

    private static void ValidateBrowserFocusChanged(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                NullableInteger("previousNodeId", nonnegative: true),
                NullableInteger("requestedNodeId", nonnegative: true),
                NullableInteger("focusedNodeId", nonnegative: true),
                RequiredEnum(
                    "outcome", "focused", "cleared", "redirected", "not-focused"),
                NullableInteger("activeDescendantNodeId", nonnegative: true),
                RequiredEnum("focusType", FocusTypes),
                RequiredEnum("focusTrigger", "script", "user-gesture"),
                RequiredBoolean("preventScroll"),
                NullableBoolean("focusVisible"),
                NullableObject("location"),
                NullableObject("world")
            ],
            issues);
        ValidateInteractionCommon(payload, issues);

        var requested = ReadNullableInteger(payload, "requestedNodeId");
        var focused = ReadNullableInteger(payload, "focusedNodeId");
        var expected = requested is null
            ? focused is null ? "cleared" : "redirected"
            : focused is null
                ? "not-focused"
                : focused == requested ? "focused" : "redirected";
        var outcome = ReadString(payload, "outcome");
        if (outcome is not null && outcome != expected)
        {
            AddError(
                issues,
                "browser-focus-outcome-inconsistent",
                "#/payload/outcome",
                $"Outcome '{outcome}' does not follow from the requested and " +
                    $"focused nodes, which give '{expected}'.");
        }
        if (focused is null &&
            ReadNullableInteger(payload, "activeDescendantNodeId") is not null)
        {
            AddError(
                issues,
                "browser-focus-active-descendant-without-focus",
                "#/payload/activeDescendantNodeId",
                "An active descendant is reported while no element is focused.");
        }
    }

    private static void ValidateBrowserSelectionChanged(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredEnum("setBy", "user", "system"),
                RequiredEnum("selectionType", "none", "caret", "range"),
                NullableInteger("anchorNodeId", nonnegative: true),
                NullableInteger("anchorOffset", nonnegative: true),
                NullableInteger("focusNodeId", nonnegative: true),
                NullableInteger("focusOffset", nonnegative: true),
                RequiredBoolean("directional"),
                NullableInteger("textControlNodeId", nonnegative: true),
                NullableInteger("textControlSelectionStart", nonnegative: true),
                NullableInteger("textControlSelectionEnd", nonnegative: true),
                NullableEnum("textControlSelectionDirection", SelectionDirections),
                NullableObject("location"),
                NullableObject("world")
            ],
            issues);
        ValidateInteractionCommon(payload, issues);

        var selectionType = ReadString(payload, "selectionType");
        if (selectionType is not null)
        {
            var hasPositions = selectionType != "none";
            ValidateAllOrNone(
                payload,
                ["anchorNodeId", "anchorOffset", "focusNodeId", "focusOffset"],
                hasPositions,
                "browser-selection-positions-inconsistent",
                $"Selection positions must be present exactly when the " +
                    $"selection type is not 'none'; it is '{selectionType}'.",
                issues);
        }
        string[] textControl =
        [
            "textControlNodeId",
            "textControlSelectionStart",
            "textControlSelectionEnd",
            "textControlSelectionDirection"
        ];
        var textControlPresent = textControl.Count(property =>
            payload.TryGetProperty(property, out var value) &&
            value.ValueKind != JsonValueKind.Null);
        if (textControlPresent is not (0 or 4))
        {
            AddError(
                issues,
                "browser-selection-text-control-inconsistent",
                "#/payload/textControlNodeId",
                "Text-control selection fields must be all present or all null.");
        }
        ValidateOrderedRange(
            payload,
            "textControlSelectionStart",
            "textControlSelectionEnd",
            issues);
    }

    private static void ValidateBrowserTextControlValueChanged(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredInteger("nodeId", positive: true),
                RequiredString("controlType"),
                RequiredEnum("source", "value-set", "user-edit"),
                RequiredText("value"),
                RequiredInteger("valueLength", nonnegative: true),
                RequiredBoolean("valueTruncated"),
                RequiredInteger("maximumValueLength", positive: true),
                RequiredInteger("selectionStart", nonnegative: true),
                RequiredInteger("selectionEnd", nonnegative: true),
                RequiredEnum("selectionDirection", SelectionDirections),
                NullableObject("location"),
                NullableObject("world")
            ],
            issues);
        ValidateInteractionCommon(payload, issues);
        ValidateTruncatedText(
            payload, "value", "valueLength", "valueTruncated", issues);
        ValidateOrderedRange(
            payload, "selectionStart", "selectionEnd", issues);
        var value = ReadString(payload, "value");
        var maximum = ReadNullableInteger(payload, "maximumValueLength");
        if (value is not null && maximum is not null && value.Length > maximum)
        {
            AddError(
                issues,
                "browser-text-control-value-over-maximum",
                "#/payload/value",
                $"The recorded value holds {value.Length} units, more than " +
                    $"the stated maximum of {maximum}.");
        }
    }

    private static void ValidateBrowserActiveDescendantReferenceSet(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredInteger("nodeId", positive: true),
                RequiredInteger("referencedNodeId", positive: true),
                NullableObject("location"),
                NullableObject("world")
            ],
            issues);
        ValidateInteractionCommon(payload, issues);
    }

    // Page popups (protocol 0.43). A popup record names the popup's own
    // document in its context; the rectangles are in screen DIPs, or in the
    // owner's local root, as Blink's gfx::Rect holds them.
    private static readonly PropertyRule[] PagePopupRectRules =
    [
        RequiredInteger("x"),
        RequiredInteger("y"),
        RequiredInteger("width", nonnegative: true),
        RequiredInteger("height", nonnegative: true)
    ];

    private static void ValidatePagePopupRect(
        JsonElement payload,
        string name,
        ICollection<EventValidationIssue> issues)
    {
        if (payload.TryGetProperty(name, out var rect) &&
            rect.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(rect, PagePopupRectRules, issues, $"#/payload/{name}");
        }
    }

    private static void ValidateBrowserPagePopupOpened(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredEnum("kind", "select-list", "date-time", "color", "other"),
                RequiredString("ownerDocumentId"),
                RequiredString("ownerDocumentToken"),
                RequiredString("ownerFrameToken"),
                RequiredInteger("ownerNodeId", positive: true),
                RequiredObject("ownerVisibleBoundsInLocalRoot"),
                RequiredObject("ownerLocalRootRectInScreen"),
                RequiredObject("anchorRectInScreen"),
                RequiredObject("initialWindowRect"),
                RequiredNumber("zoomFactor", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        foreach (var name in new[]
                 {
                     "ownerVisibleBoundsInLocalRoot", "ownerLocalRootRectInScreen",
                     "anchorRectInScreen", "initialWindowRect"
                 })
        {
            ValidatePagePopupRect(payload, name, issues);
        }

        var ownerDocument = ReadString(payload, "ownerDocumentId");
        if (ownerDocument is not null &&
            !IsCheckpointIdentity(ownerDocument, "dom-document-"))
        {
            AddError(
                issues,
                "browser-page-popup-owner-document-invalid",
                "#/payload/ownerDocumentId",
                $"'{ownerDocument}' is not a DOM document identity.");
        }

        if (ReadString(payload, "ownerDocumentToken") is { } token &&
            string.IsNullOrWhiteSpace(token))
        {
            AddError(
                issues,
                "browser-page-popup-owner-token-empty",
                "#/payload/ownerDocumentToken",
                "A popup's owner document must carry a nonempty token.");
        }

        if (ReadString(payload, "ownerFrameToken") is { } frameToken &&
            string.IsNullOrWhiteSpace(frameToken))
        {
            AddError(
                issues,
                "browser-page-popup-owner-frame-token-empty",
                "#/payload/ownerFrameToken",
                "A popup's owner frame must carry a nonempty token.");
        }
    }

    private static void ValidateBrowserPagePopupWindowRect(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredBoolean("deferred"),
                RequiredObject("windowRect")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidatePagePopupRect(payload, "windowRect", issues);
    }

    // Popup widget records (protocol 0.44) are made by the browser process.
    private static void ValidateBrowserProcessContext(
        JsonElement payload,
        bool requiresFrame,
        ICollection<EventValidationIssue> issues,
        string code = "browser-popup-widget-context-invalid",
        string subject = "Popup widget evidence")
    {
        if (!payload.TryGetProperty("context", out var context) ||
            context.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (ReadString(context, "processType") != "browser" ||
            (requiresFrame && (ReadString(context, "pageId") is null ||
                               ReadString(context, "frameId") is null)))
        {
            AddError(
                issues,
                code,
                "#/payload/context",
                requiresFrame
                    ? "A created popup widget must have browser-process " +
                        "provenance and name its opener's page and frame."
                    : $"{subject} must have browser-process provenance.");
        }
    }

    private static void ValidateBrowserPopupWidgetCreated(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                NullableInteger("rendererProcessId", positive: true),
                RequiredString("openerFrameToken"),
                RequiredFrameSinkId("frameSinkId")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, true, issues);
        if (ReadString(payload, "openerFrameToken") is { } token &&
            string.IsNullOrWhiteSpace(token))
        {
            AddError(
                issues,
                "browser-popup-widget-opener-token-empty",
                "#/payload/openerFrameToken",
                "A created popup widget must name its opener frame's token.");
        }
    }

    private static void ValidateBrowserPopupWidgetShown(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredFrameSinkId("frameSinkId"),
                RequiredEnum(
                    "outcome", "shown", "window-not-active", "not-visible",
                    "permission-exclusion"),
                RequiredObject("receivedRect"),
                RequiredObject("receivedAnchorRect"),
                NullableObject("transformedRect"),
                NullableObject("transformedAnchorRect"),
                NullableObject("constrainedRect"),
                NullableObject("viewBounds"),
                RequiredObject("windowsAnimationSettings")
            ],
            issues);
        if (payload.TryGetProperty("windowsAnimationSettings", out var settings) &&
            settings.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                settings,
                [
                    NullableBoolean("clientAreaAnimation"),
                    NullableBoolean("uiEffects"),
                    NullableBoolean("menuAnimation"),
                    NullableBoolean("menuFade"),
                    NullableBoolean("comboBoxAnimation")
                ],
                issues,
                "#/payload/windowsAnimationSettings");
        }

        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, false, issues);
        foreach (var name in new[]
                 {
                     "receivedRect", "receivedAnchorRect", "transformedRect",
                     "transformedAnchorRect", "constrainedRect", "viewBounds"
                 })
        {
            ValidatePagePopupRect(payload, name, issues);
        }

        // A popup refused because its window was not active is refused before
        // the transform; the others after the transform and the constraint.
        var outcome = ReadString(payload, "outcome");
        var transformed = HasNonnullProperty(payload, "transformedRect");
        var transformedAnchor =
            HasNonnullProperty(payload, "transformedAnchorRect");
        var constrained = HasNonnullProperty(payload, "constrainedRect");
        var viewBounds = HasNonnullProperty(payload, "viewBounds");
        var consistent = outcome switch
        {
            "window-not-active" =>
                !transformed && !transformedAnchor && !constrained && !viewBounds,
            "not-visible" or "permission-exclusion" =>
                transformed && transformedAnchor && constrained && !viewBounds,
            "shown" => transformed && transformedAnchor && constrained && viewBounds,
            _ => true
        };
        if (!consistent)
        {
            AddError(
                issues,
                "browser-popup-widget-shown-inconsistent",
                "#/payload",
                "A popup refused for an inactive window has no transformed, " +
                    "constrained, or view rectangle; one refused later has " +
                    "the transformed and constrained rectangles; a shown " +
                    "popup has all of them.");
        }
    }

    private static void ValidateBrowserPopupWidgetBoundsRequested(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredFrameSinkId("frameSinkId"),
                RequiredObject("requestedRect"),
                NullableObject("setRect")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, false, issues);
        ValidatePagePopupRect(payload, "requestedRect", issues);
        ValidatePagePopupRect(payload, "setRect", issues);
    }

    private static void ValidateBrowserPopupWidgetScreenRects(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredFrameSinkId("frameSinkId"),
                RequiredObject("viewRect"),
                RequiredObject("windowRect"),
                NullableObject("nativeWindowRect"),
                NullableObject("nativeClientRect"),
                RequiredNumber("deviceScaleFactor", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, false, issues);
        foreach (var name in new[]
                 {
                     "viewRect", "windowRect", "nativeWindowRect",
                     "nativeClientRect"
                 })
        {
            ValidatePagePopupRect(payload, name, issues);
        }
        if (HasNonnullProperty(payload, "nativeWindowRect") !=
            HasNonnullProperty(payload, "nativeClientRect"))
        {
            AddError(
                issues,
                "browser-popup-widget-native-rects-inconsistent",
                "#/payload",
                "The native window rectangle and client area are recorded " +
                    "together or not at all.");
        }
    }

    private static void ValidateBrowserPopupWidgetHidden(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredFrameSinkId("frameSinkId"),
                RequiredEnum("cause", "hidden", "destroyed"),
                NullableBoolean("nativeWindowVisible")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserProcessContext(payload, false, issues);
    }

    private static void ValidateBrowserPagePopupClosed(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredEnum("closedBy", "renderer", "browser")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
    }

    private static void ValidateBrowserOptionSelectednessChanged(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredInteger("nodeId", positive: true),
                NullableInteger("selectNodeId", positive: true),
                RequiredBoolean("selected"),
                NullableObject("location"),
                NullableObject("world")
            ],
            issues);
        ValidateInteractionCommon(payload, issues);
    }

    // Checkpoint identities are "<prefix><sequence>" with a positive decimal
    // sequence, as the bridge formats them.
    private static bool IsCheckpointIdentity(string? value, string prefix) =>
        value is not null &&
        value.StartsWith(prefix, StringComparison.Ordinal) &&
        value.Length > prefix.Length &&
        value[prefix.Length] != '0' &&
        value.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;

    private static void ValidateInteractionCheckpointIdentity(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        var checkpointId = ReadString(payload, "checkpointId");
        if (checkpointId is not null &&
            !IsCheckpointIdentity(checkpointId, "interaction-checkpoint-"))
        {
            AddError(
                issues,
                "browser-interaction-checkpoint-id-invalid",
                "#/payload/checkpointId",
                $"'{checkpointId}' is not an interaction checkpoint identity.");
        }
    }

    private static void ValidateBrowserInteractionCheckpointStarted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                NullableString("sourceCheckpointId"),
                NullableString("sourceChangeSetId"),
                RequiredEnum("sourceChannel", "browser.dom", "browser.layout"),
                RequiredEnum(
                    "reason", "started-parsing", "finished-parsing",
                    "post-mutation", "rendering-update"),
                RequiredBoolean("documentHasFocus"),
                NullableInteger("focusedNodeId", positive: true),
                RequiredBoolean("focusVisible"),
                NullableInteger("activeDescendantNodeId", positive: true),
                RequiredEnum("lastFocusType", FocusTypes),
                RequiredEnum("selectionType", "none", "caret", "range"),
                NullableInteger("anchorNodeId", positive: true),
                NullableInteger("anchorOffset", nonnegative: true),
                NullableInteger("focusNodeId", positive: true),
                NullableInteger("focusOffset", nonnegative: true),
                RequiredBoolean("directional"),
                RequiredInteger("maximumTextControls", positive: true),
                RequiredInteger("maximumValueLength", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateInteractionCheckpointIdentity(payload, issues);

        var sourceChannel = ReadString(payload, "sourceChannel");
        var reason = ReadString(payload, "reason");
        var sourceCheckpointId = ReadString(payload, "sourceCheckpointId");
        var (sourcePrefix, sourceReasons) = sourceChannel switch
        {
            "browser.dom" =>
                ("dom-checkpoint-", new[] { "started-parsing", "finished-parsing", "post-mutation" }),
            "browser.layout" =>
                ("layout-checkpoint-", new[] { "rendering-update" }),
            _ => ((string?)null, Array.Empty<string>())
        };
        if (sourcePrefix is not null && sourceCheckpointId is not null &&
            !IsCheckpointIdentity(sourceCheckpointId, sourcePrefix))
        {
            AddError(
                issues,
                "browser-interaction-checkpoint-source-invalid",
                "#/payload/sourceCheckpointId",
                $"'{sourceCheckpointId}' is not a checkpoint identity of the " +
                    $"'{sourceChannel}' channel.");
        }
        // From protocol 0.35 a snapshot may follow a mutation delivery that
        // was not walked, which names no source record, or a layout change
        // set. A DOM source names a checkpoint unless it is such a delivery;
        // a layout source names a checkpoint or a change set, not both.
        var sourceChangeSetId = ReadString(payload, "sourceChangeSetId");
        var sourceConsistent = sourceChannel switch
        {
            "browser.dom" => sourceChangeSetId is null &&
                (sourceCheckpointId is not null || reason == "post-mutation"),
            "browser.layout" => (sourceCheckpointId is null) != (sourceChangeSetId is null),
            _ => true
        };
        if (!sourceConsistent)
        {
            AddError(
                issues,
                "browser-interaction-checkpoint-source-inconsistent",
                "#/payload/sourceCheckpointId",
                $"The source records named are not ones the '{sourceChannel}' channel " +
                    "makes a snapshot after.");
        }
        if (sourceChangeSetId is not null &&
            !IsCheckpointIdentity(sourceChangeSetId, "layout-changes-"))
        {
            AddError(
                issues,
                "browser-interaction-checkpoint-change-set-invalid",
                "#/payload/sourceChangeSetId",
                $"'{sourceChangeSetId}' is not a layout change set identity.");
        }
        if (sourcePrefix is not null && reason is not null &&
            !sourceReasons.Contains(reason))
        {
            AddError(
                issues,
                "browser-interaction-checkpoint-reason-inconsistent",
                "#/payload/reason",
                $"Reason '{reason}' is not one the '{sourceChannel}' channel " +
                    "records.");
        }

        var focused = ReadNullableInteger(payload, "focusedNodeId");
        if (focused is null &&
            ReadNullableInteger(payload, "activeDescendantNodeId") is not null)
        {
            AddError(
                issues,
                "browser-interaction-checkpoint-active-descendant-without-focus",
                "#/payload/activeDescendantNodeId",
                "An active descendant is reported while no element is focused.");
        }
        if (focused is null &&
            payload.TryGetProperty("focusVisible", out var focusVisible) &&
            focusVisible.ValueKind == JsonValueKind.True)
        {
            AddError(
                issues,
                "browser-interaction-checkpoint-focus-visible-without-focus",
                "#/payload/focusVisible",
                "Focus is reported visible while no element is focused.");
        }

        var selectionType = ReadString(payload, "selectionType");
        if (selectionType is not null)
        {
            ValidateAllOrNone(
                payload,
                ["anchorNodeId", "anchorOffset", "focusNodeId", "focusOffset"],
                selectionType != "none",
                "browser-interaction-checkpoint-selection-inconsistent",
                $"Selection positions must be present exactly when the " +
                    $"selection type is not 'none'; it is '{selectionType}'.",
                issues);
        }
    }

    private static void ValidateBrowserInteractionCheckpointTextControl(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredInteger("textControlIndex", nonnegative: true),
                RequiredInteger("nodeId", positive: true),
                RequiredString("controlType"),
                RequiredText("value"),
                RequiredInteger("valueLength", nonnegative: true),
                RequiredBoolean("valueTruncated"),
                RequiredInteger("selectionStart", nonnegative: true),
                RequiredInteger("selectionEnd", nonnegative: true),
                RequiredEnum("selectionDirection", SelectionDirections)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateInteractionCheckpointIdentity(payload, issues);
        ValidateTruncatedText(
            payload, "value", "valueLength", "valueTruncated", issues);
        ValidateOrderedRange(
            payload, "selectionStart", "selectionEnd", issues);
    }

    private static void ValidateBrowserInteractionCheckpointCompleted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredInteger("textControlCount", nonnegative: true),
                RequiredBoolean("truncated"),
                RequiredInteger("maximumTextControls", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateInteractionCheckpointIdentity(payload, issues);
        var count = ReadNullableInteger(payload, "textControlCount");
        var maximum = ReadNullableInteger(payload, "maximumTextControls");
        if (count is not null && maximum is not null && count > maximum)
        {
            AddError(
                issues,
                "browser-interaction-checkpoint-count-over-maximum",
                "#/payload/textControlCount",
                $"The checkpoint reports {count} text controls, more than " +
                    $"the stated maximum of {maximum}.");
        }
    }

    private static readonly string[] NetworkContextKinds =
    [
        "window", "dedicated-worker", "shared-worker", "service-worker",
        "worklet", "other"
    ];

    private static readonly string[] NetworkRedactionReasons =
        ["credential-header", "credential-name", "credential-value"];

    // Header names whose values the recorder never writes.
    private static readonly HashSet<string> NetworkCredentialHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "cookie", "set-cookie", "set-cookie2", "authorization",
            "proxy-authorization"
        };

    private static void ValidateBrowserNetworkRequestWillBeSent(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredObject("scope"),
                RequiredObject("request"),
                RequiredBoolean("redirect"),
                NullableObject("redirectResponse"),
                NullableObject("location"),
                NullableObject("world")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateNetworkScopeProperty(payload, issues);
        ValidateBrowserLocationProperty(payload, issues);
        ValidateBrowserExecutionWorldProperty(payload, issues);
        ValidateOptionalObject(payload, "request", ValidateNetworkRequest, issues);
        ValidateOptionalObject(
            payload, "redirectResponse", ValidateNetworkResponse, issues);

        var redirect = payload.TryGetProperty("redirect", out var redirectValue) &&
            redirectValue.ValueKind == JsonValueKind.True;
        if (redirect != HasNonnullProperty(payload, "redirectResponse"))
        {
            AddError(
                issues,
                "browser-network-redirect-response",
                "#/payload/redirectResponse",
                "A redirect reports its redirect response and a first request reports none.");
        }
    }

    private static void ValidateBrowserNetworkResponseReceived(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredObject("scope"),
                RequiredString("inspectorId"),
                NullableString("requestId"),
                RequiredEnum("responseSource", "memory-cache", "loader"),
                RequiredObject("response")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateNetworkScopeProperty(payload, issues);
        ValidateInspectorId(payload, issues);
        ValidateOptionalObject(payload, "response", ValidateNetworkResponse, issues);
    }

    private static void ValidateBrowserNetworkRequestFinished(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredObject("scope"),
                RequiredString("inspectorId"),
                RequiredNullableNumber("encodedDataLength", nonnegative: true),
                RequiredNumber("decodedBodyLength", nonnegative: true),
                RequiredNullableNumber("finishBeforeRecordMilliseconds")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateNetworkScopeProperty(payload, issues);
        ValidateInspectorId(payload, issues);
    }

    private static void ValidateBrowserNetworkRequestFailed(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredObject("scope"),
                RequiredString("inspectorId"),
                RequiredText("url"),
                RequiredInteger("netError"),
                NullableString("netErrorName"),
                RequiredBoolean("cancellation"),
                RequiredBoolean("timeout"),
                RequiredBoolean("accessCheck"),
                RequiredBoolean("blockedByResponse"),
                RequiredBoolean("blockedByOrb"),
                RequiredBoolean("hasCopyInCache"),
                RequiredBoolean("cancelledFromHttpError"),
                RequiredBoolean("internal"),
                NullableString("blockedReason"),
                NullableObject("corsError")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateNetworkScopeProperty(payload, issues);
        ValidateInspectorId(payload, issues);
        ValidateOptionalObject(
            payload,
            "corsError",
            (value, list, path) => ValidateShape(
                value,
                [RequiredString("error"), NullableString("failedParameter")],
                list,
                path),
            issues);
    }

    private static void ValidateBrowserNetworkMemoryCacheHit(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredObject("scope"),
                RequiredBoolean("staticData"),
                RequiredObject("request"),
                RequiredObject("response")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateNetworkScopeProperty(payload, issues);
        ValidateOptionalObject(payload, "request", ValidateNetworkRequest, issues);
        ValidateOptionalObject(payload, "response", ValidateNetworkResponse, issues);
    }

    private static void ValidateBrowserNetworkWireHeaders(
        JsonElement payload,
        bool response,
        ICollection<EventValidationIssue> issues)
    {
        List<PropertyRule> rules =
        [
            RequiredObject("context"),
            NullableString("devtoolsAgentId"),
            RequiredString("requestId"),
            RequiredInteger("headerCount", nonnegative: true),
            RequiredObjectArray("headers"),
            RequiredBoolean("headersTruncated"),
            RequiredInteger("cookieCount", nonnegative: true),
            RequiredObjectArray("cookies"),
            RequiredBoolean("cookiesTruncated")
        ];
        rules.Add(
            response
                ? RequiredInteger("status", nonnegative: true)
                : RequiredNullableNumber("sentBeforeRecordMilliseconds"));
        ValidateShape(payload, rules, issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateNetworkHeaders(
            payload,
            "headers",
            "headerCount",
            "headersTruncated",
            issues,
            "#/payload");

        if (payload.TryGetProperty("cookies", out var cookies) &&
            cookies.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var cookie in cookies.EnumerateArray())
            {
                if (cookie.ValueKind == JsonValueKind.Object)
                {
                    ValidateCookieAccessEntry(cookie, index, issues);
                }

                index++;
            }

            ValidateCookieNameCount(payload, "cookies", "cookiesTruncated", issues);
        }
    }

    private static void ValidateBrowserNetworkNavigationResponse(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("navigationId"),
                NullableString("requestId"),
                RequiredText("url"),
                RequiredString("method"),
                RequiredBoolean("committed"),
                RequiredBoolean("errorPage"),
                RequiredBoolean("sameDocument"),
                RequiredBoolean("download"),
                RequiredBoolean("backForwardCache"),
                RequiredInteger("netError"),
                NullableString("netErrorName"),
                RequiredTextArray("redirectChain"),
                RequiredInteger("requestHeaderCount", nonnegative: true),
                RequiredObjectArray("requestHeaders"),
                RequiredBoolean("requestHeadersTruncated"),
                NullableObject("response"),
                NullableObject("timing")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateNetworkHeaders(
            payload,
            "requestHeaders",
            "requestHeaderCount",
            "requestHeadersTruncated",
            issues,
            "#/payload");
        ValidateOptionalObject(
            payload,
            "response",
            (value, list, path) =>
            {
                ValidateShape(
                    value,
                    [
                        RequiredInteger("status", nonnegative: true),
                        RequiredText("statusText"),
                        NullableString("mimeType"),
                        RequiredBoolean("wasCached"),
                        NullableObject("remoteAddress"),
                        NullableString("connectionInfo"),
                        RequiredInteger("headerCount", nonnegative: true),
                        RequiredObjectArray("headers"),
                        RequiredBoolean("headersTruncated")
                    ],
                    list,
                    path);
                ValidateOptionalObject(
                    value, "remoteAddress", ValidateNetworkRemoteAddress, list, path);
                ValidateNetworkHeaders(
                    value, "headers", "headerCount", "headersTruncated", list, path);
            },
            issues);
        ValidateOptionalObject(
            payload,
            "timing",
            (value, list, path) => ValidateNetworkTiming(
                value,
                "navigationStartBeforeRecordMilliseconds",
                [
                    "loaderStart", "firstRequestStart", "firstResponseStart",
                    "firstLoaderCallback", "finalRequestStart", "finalResponseStart",
                    "finalNonInformationalResponseStart", "finalLoaderCallback",
                    "requestFailed", "commitSent", "commitReceived",
                    "commitReplySent", "didCommit", "finalRequestDomainLookupStart",
                    "finalRequestDomainLookupEnd", "finalRequestConnectStart",
                    "finalRequestConnectEnd", "finalRequestSslStart"
                ],
                list,
                path),
            issues);
    }

    // The marker written where a credential was withheld. A recorded message,
    // event field, or close reason is otherwise recorded whole.
    private const string RealtimeWithheldMarker = "[withheld]";

    private static readonly string[] RealtimeWithheldReasons =
        ["credential-name", "credential-value"];

    private static void ValidateBrowserNetworkRealtime(
        JsonElement payload,
        string eventType,
        ICollection<EventValidationIssue> issues)
    {
        List<PropertyRule> rules =
        [
            RequiredObject("context"),
            RequiredObject("scope")
        ];
        var scriptCall = eventType is "websocket-created" or "websocket-message-sent"
            or "websocket-close-requested" or "web-transport-created"
            or "web-transport-close-requested";
        if (scriptCall)
        {
            rules.Add(NullableObject("location"));
            rules.Add(NullableObject("world"));
        }

        var transport = eventType.StartsWith("web-transport-", StringComparison.Ordinal);
        rules.Add(RequiredString(transport ? "transportId" : "inspectorId"));
        string[] textProperties = [];
        switch (eventType)
        {
            case "websocket-created":
                rules.Add(RequiredText("url"));
                rules.Add(NullableString("requestedProtocols"));
                break;
            case "websocket-handshake-request":
                rules.Add(RequiredText("url"));
                rules.Add(RequiredTextArray("cookieNames"));
                AddRealtimeHeaderRules(rules);
                break;
            case "websocket-handshake-response":
                AddRealtimeResponseRules(rules);
                rules.Add(NullableString("extensions"));
                break;
            case "websocket-message-sent":
            case "websocket-message-received":
                rules.Add(RequiredEnum("opcode", "text", "binary"));
                rules.Add(RequiredNumber("payloadLength", nonnegative: true));
                rules.Add(NullableObject("payload"));
                textProperties = ["payload"];
                break;
            case "websocket-close-requested":
                rules.Add(NullableInteger("code"));
                rules.Add(RequiredObject("reason"));
                textProperties = ["reason"];
                break;
            case "websocket-error":
                rules.Add(RequiredText("message"));
                break;
            case "websocket-closed":
                rules.Add(RequiredEnum("cause", "dropped", "disconnected"));
                rules.Add(NullableBoolean("wasClean"));
                rules.Add(NullableInteger("code"));
                rules.Add(NullableObject("reason"));
                textProperties = ["reason"];
                break;
            case "event-source-message":
                rules.Add(RequiredText("url"));
                rules.Add(RequiredText("eventType"));
                rules.Add(RequiredObject("lastEventId"));
                rules.Add(RequiredNumber("dataLength", nonnegative: true));
                rules.Add(RequiredObject("data"));
                textProperties = ["lastEventId", "data"];
                break;
            case "web-transport-created":
                rules.Add(RequiredText("url"));
                break;
            case "web-transport-established":
                AddRealtimeResponseRules(rules);
                rules.Add(RequiredNullableNumber("maxDatagramSize", nonnegative: true));
                break;
            case "web-transport-close-requested":
                rules.Add(RequiredNullableNumber("code", nonnegative: true));
                rules.Add(NullableObject("reason"));
                textProperties = ["reason"];
                break;
            case "web-transport-closed":
                rules.Add(RequiredBoolean("abrupt"));
                rules.Add(RequiredNullableNumber("code", nonnegative: true));
                rules.Add(NullableObject("reason"));
                textProperties = ["reason"];
                break;
        }

        ValidateShape(payload, rules, issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateNetworkScopeProperty(payload, issues);
        if (scriptCall)
        {
            ValidateBrowserLocationProperty(payload, issues);
            ValidateBrowserExecutionWorldProperty(payload, issues);
        }

        ValidateRealtimeId(payload, transport ? "transportId" : "inspectorId", issues);
        foreach (var property in textProperties)
        {
            ValidateOptionalObject(payload, property, ValidateRealtimeText, issues);
        }

        if (payload.TryGetProperty("headers", out _))
        {
            ValidateNetworkHeaders(
                payload,
                "headers",
                "headerCount",
                "headersTruncated",
                issues,
                "#/payload");
        }

        ValidateOptionalObject(
            payload, "remoteAddress", ValidateNetworkRemoteAddress, issues);
        ValidateRealtimeConsistency(payload, eventType, issues);
    }

    private static void AddRealtimeHeaderRules(List<PropertyRule> rules)
    {
        rules.Add(RequiredInteger("headerCount", nonnegative: true));
        rules.Add(RequiredObjectArray("headers"));
        rules.Add(RequiredBoolean("headersTruncated"));
    }

    private static void AddRealtimeResponseRules(List<PropertyRule> rules)
    {
        rules.Add(NullableString("url"));
        rules.Add(NullableString("httpVersion"));
        rules.Add(RequiredInteger("status", nonnegative: true));
        rules.Add(NullableString("statusText"));
        rules.Add(NullableObject("remoteAddress"));
        rules.Add(NullableString("selectedProtocol"));
        rules.Add(RequiredTextArray("setCookieNames"));
        AddRealtimeHeaderRules(rules);
    }

    private static void ValidateRealtimeId(
        JsonElement payload,
        string property,
        ICollection<EventValidationIssue> issues)
    {
        var id = ReadString(payload, property);
        if (id is not null && !ulong.TryParse(
                id,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out _))
        {
            AddError(
                issues,
                "browser-network-inspector-id-invalid",
                $"#/payload/{property}",
                $"{property} must be an unsigned decimal integer string.");
        }
    }

    // Checks one recorded text: its length, and that each withheld part is
    // reported, in order, where the text carries the withheld marker.
    private static void ValidateRealtimeText(
        JsonElement value,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        ValidateShape(
            value,
            [
                RequiredText("text"),
                RequiredBoolean("truncated"),
                RequiredObjectArray("withheld")
            ],
            issues,
            path);
        var text = ReadString(value, "text");
        if (text is null)
        {
            return;
        }

        if (!value.TryGetProperty("withheld", out var withheld) ||
            withheld.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        var next = 0;
        foreach (var part in withheld.EnumerateArray())
        {
            var partPath = $"{path}/withheld/{index}";
            index++;
            if (part.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            ValidateShape(
                part,
                [
                    RequiredInteger("offset", nonnegative: true),
                    RequiredEnum("reason", RealtimeWithheldReasons)
                ],
                issues,
                partPath);
            if (!part.TryGetProperty("offset", out var offsetValue) ||
                !offsetValue.TryGetInt32(out var offset) || offset < 0)
            {
                continue;
            }

            if (offset < next ||
                offset > text.Length - RealtimeWithheldMarker.Length ||
                string.CompareOrdinal(
                    text, offset, RealtimeWithheldMarker, 0,
                    RealtimeWithheldMarker.Length) != 0)
            {
                AddError(
                    issues,
                    "browser-network-withheld-offset",
                    $"{partPath}/offset",
                    "A withheld part is reported in order at an offset where the text holds the withheld marker.");
                continue;
            }

            next = offset + RealtimeWithheldMarker.Length;
        }
    }

    private static void ValidateRealtimeConsistency(
        JsonElement payload,
        string eventType,
        ICollection<EventValidationIssue> issues)
    {
        string? problem = null;
        string property = "payload";
        switch (eventType)
        {
            case "websocket-message-sent":
            case "websocket-message-received":
                if ((ReadString(payload, "opcode") == "text") !=
                    HasNonnullProperty(payload, "payload"))
                {
                    problem = "A text message carries its payload text and a binary message carries none.";
                }

                break;
            case "websocket-closed":
                var dropped = ReadString(payload, "cause") == "dropped";
                property = "cause";
                if (dropped != HasNonnullProperty(payload, "wasClean") ||
                    dropped != HasNonnullProperty(payload, "code") ||
                    dropped != HasNonnullProperty(payload, "reason"))
                {
                    problem = "A dropped channel reports whether it closed cleanly, its code, and its reason; a disconnected one reports none.";
                }

                break;
            case "web-transport-close-requested":
                property = "code";
                if (HasNonnullProperty(payload, "code") !=
                    HasNonnullProperty(payload, "reason"))
                {
                    problem = "A close request reports both its code and its reason, or neither.";
                }

                break;
            case "web-transport-closed":
                var abrupt = payload.TryGetProperty("abrupt", out var abruptValue) &&
                    abruptValue.ValueKind == JsonValueKind.True;
                property = "abrupt";
                if (abrupt == HasNonnullProperty(payload, "code") ||
                    abrupt == HasNonnullProperty(payload, "reason"))
                {
                    problem = "An abrupt close reports no code or reason, and a clean close reports both.";
                }

                break;
        }

        if (problem is not null)
        {
            AddError(
                issues,
                "browser-network-realtime-inconsistent",
                $"#/payload/{property}",
                problem);
        }
    }

    private static void ValidateNetworkScopeProperty(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateOptionalObject(
            payload,
            "scope",
            (value, list, path) =>
            {
                ValidateShape(
                    value,
                    [
                        RequiredEnum("contextKind", NetworkContextKinds),
                        NullableString("workerToken"),
                        NullableString("globalObjectUrl")
                    ],
                    list,
                    path);
                if (ReadString(value, "contextKind") == "window" &&
                    HasNonnullProperty(value, "workerToken"))
                {
                    AddError(
                        list,
                        "browser-network-scope-invalid",
                        $"{path}/workerToken",
                        "A window scope carries no worker token.");
                }
            },
            issues);
    }

    private static void ValidateInspectorId(
        JsonElement value,
        ICollection<EventValidationIssue> issues,
        string path = "#/payload")
    {
        var id = ReadString(value, "inspectorId");
        if (id is not null && !ulong.TryParse(
                id,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out _))
        {
            AddError(
                issues,
                "browser-network-inspector-id-invalid",
                $"{path}/inspectorId",
                "An inspector id must be an unsigned decimal integer string.");
        }
    }

    private static void ValidateNetworkRequest(
        JsonElement value,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        ValidateShape(
            value,
            [
                RequiredString("inspectorId"),
                NullableString("requestId"),
                RequiredText("url"),
                RequiredText("method"),
                RequiredText("resourceType"),
                RequiredObject("initiator"),
                RequiredBoolean("internal"),
                RequiredString("destination"),
                RequiredString("mode"),
                RequiredString("credentialsMode"),
                RequiredString("redirectMode"),
                RequiredString("cacheMode"),
                RequiredString("priority"),
                RequiredString("initialPriority"),
                RequiredString("fetchPriorityHint"),
                RequiredString("renderBlocking"),
                NullableString("referrer"),
                RequiredString("referrerPolicy"),
                RequiredBoolean("keepalive"),
                RequiredBoolean("userGesture"),
                RequiredBoolean("adResource"),
                RequiredBoolean("formSubmission"),
                RequiredInteger("headerCount", nonnegative: true),
                RequiredObjectArray("headers"),
                RequiredBoolean("headersTruncated")
            ],
            issues,
            path);
        ValidateInspectorId(value, issues, path);
        ValidateOptionalObject(
            value,
            "initiator",
            (initiator, list, initiatorPath) => ValidateShape(
                initiator,
                [
                    NullableString("type"),
                    NullableString("url"),
                    NullableInteger("line", nonnegative: true),
                    NullableInteger("column", nonnegative: true),
                    RequiredBoolean("linkPreload")
                ],
                list,
                initiatorPath),
            issues,
            path);
        ValidateNetworkHeaders(
            value, "headers", "headerCount", "headersTruncated", issues, path);
    }

    private static void ValidateNetworkResponse(
        JsonElement value,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        ValidateShape(
            value,
            [
                RequiredText("url"),
                NullableString("responseUrl"),
                RequiredInteger("status", nonnegative: true),
                RequiredText("statusText"),
                RequiredText("mimeType"),
                NullableString("charset"),
                NullableString("alpnProtocol"),
                NullableString("connectionInfo"),
                NullableObject("remoteAddress"),
                RequiredNumber("connectionId", nonnegative: true),
                RequiredBoolean("connectionReused"),
                RequiredBoolean("wasCached"),
                RequiredBoolean("fetchedViaServiceWorker"),
                RequiredString("serviceWorkerResponseSource"),
                RequiredBoolean("inPrefetchCache"),
                RequiredBoolean("networkAccessed"),
                RequiredBoolean("fromArchive"),
                RequiredBoolean("cookieInRequest"),
                RequiredString("responseType"),
                RequiredNullableNumber("encodedDataLength", nonnegative: true),
                RequiredNumber("expectedContentLength"),
                RequiredInteger("headerCount", nonnegative: true),
                RequiredObjectArray("headers"),
                RequiredBoolean("headersTruncated"),
                NullableObject("timing")
            ],
            issues,
            path);
        ValidateOptionalObject(
            value, "remoteAddress", ValidateNetworkRemoteAddress, issues, path);
        ValidateNetworkHeaders(
            value, "headers", "headerCount", "headersTruncated", issues, path);
        ValidateOptionalObject(
            value,
            "timing",
            (timing, list, timingPath) => ValidateNetworkTiming(
                timing,
                "requestStartBeforeRecordMilliseconds",
                [
                    "proxyStart", "proxyEnd", "domainLookupStart", "domainLookupEnd",
                    "connectStart", "connectEnd", "sslStart", "sslEnd",
                    "workerStart", "workerReady", "workerFetchStart",
                    "workerRespondWithSettled", "workerRouterEvaluationStart",
                    "workerCacheLookupStart", "sendStart", "sendEnd",
                    "receiveHeadersStart", "receiveHeadersEnd",
                    "receiveNonInformationalHeadersStart", "receiveEarlyHintsStart",
                    "pushStart", "pushEnd", "responseEnd"
                ],
                list,
                timingPath),
            issues,
            path);
    }

    private static void ValidateNetworkRemoteAddress(
        JsonElement value,
        ICollection<EventValidationIssue> issues,
        string path) =>
        ValidateShape(
            value,
            [
                RequiredString("ip"),
                new PropertyRule(
                    "port",
                    true,
                    false,
                    port => IsInteger(port) &&
                        port.GetInt64() is >= 0 and <= 65535,
                    "must be an integer from 0 to 65535")
            ],
            issues,
            path);

    private static void ValidateNetworkTiming(
        JsonElement value,
        string startProperty,
        IReadOnlyList<string> phases,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        List<PropertyRule> rules = [RequiredNullableNumber(startProperty)];
        rules.AddRange(phases.Select(phase => RequiredNullableNumber(phase)));
        ValidateShape(value, rules, issues, path);
    }

    // Checks one header list against its count and truncation flag, and checks
    // that every withheld value is null with a reason and that no credential
    // header value was written.
    private static void ValidateNetworkHeaders(
        JsonElement parent,
        string listProperty,
        string countProperty,
        string truncatedProperty,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        if (!parent.TryGetProperty(listProperty, out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var header in list.EnumerateArray())
        {
            var headerPath = $"{path}/{listProperty}/{index}";
            index++;
            if (header.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            ValidateShape(
                header,
                [
                    RequiredString("name"),
                    NullableText("value"),
                    RequiredBoolean("valueRedacted"),
                    NullableEnum("redactionReason", NetworkRedactionReasons)
                ],
                issues,
                headerPath);
            var redacted = header.TryGetProperty("valueRedacted", out var redactedValue) &&
                redactedValue.ValueKind == JsonValueKind.True;
            var hasValue = HasNonnullProperty(header, "value");
            var hasReason = HasNonnullProperty(header, "redactionReason");
            if (redacted == hasValue || redacted != hasReason)
            {
                AddError(
                    issues,
                    "browser-network-header-redaction",
                    headerPath,
                    "A withheld header value is null with a reason, and a recorded one has no reason.");
            }

            var name = ReadString(header, "name");
            if (name is not null && NetworkCredentialHeaders.Contains(name) && hasValue)
            {
                AddError(
                    issues,
                    "browser-network-credential-header-value",
                    $"{headerPath}/value",
                    $"The value of a '{name}' header must not be recorded.");
            }
        }

        if (!parent.TryGetProperty(countProperty, out var countValue) ||
            !countValue.TryGetInt64(out var count) ||
            !parent.TryGetProperty(truncatedProperty, out var truncatedValue) ||
            truncatedValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return;
        }

        var length = list.GetArrayLength();
        if (truncatedValue.ValueKind == JsonValueKind.True ? length >= count : length != count)
        {
            AddError(
                issues,
                "browser-network-header-count",
                $"{path}/{listProperty}",
                $"{countProperty} must equal the listed headers unless the list is marked truncated, in which case it must exceed them.");
        }
    }

    private static void ValidateBrowserLayoutCheckpointStarted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "rendering-update"),
                RequiredEnum("walkReason", "first", "after-loss", "check"),
                NullableString("previousCheckpointId"),
                RequiredInteger("styleResolutionCount", nonnegative: true),
                RequiredInteger("layoutCount", nonnegative: true),
                RequiredObject("viewport"),
                RequiredObject("scrollOffset"),
                RequiredNumber("devicePixelRatio", positive: true),
                RequiredNumber("layoutZoomFactor", positive: true),
                RequiredInteger("maximumNodes", positive: true),
                RequiredStringArray("styleProperties")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        if (payload.TryGetProperty("viewport", out var viewport) &&
            viewport.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                viewport,
                [
                    RequiredNumber("width", nonnegative: true),
                    RequiredNumber("height", nonnegative: true)
                ],
                issues,
                "#/payload/viewport");
        }
        if (payload.TryGetProperty("scrollOffset", out var scroll) &&
            scroll.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                scroll,
                [RequiredNumber("x"), RequiredNumber("y")],
                issues,
                "#/payload/scrollOffset");
        }
        var previous = ReadString(payload, "previousCheckpointId");
        if (previous is not null && previous == ReadString(payload, "checkpointId"))
        {
            AddError(
                issues,
                "browser-layout-checkpoint-previous-self",
                "#/payload/previousCheckpointId",
                "A layout checkpoint names itself as its previous checkpoint.");
        }
        if (payload.TryGetProperty("styleProperties", out var properties) &&
            properties.ValueKind == JsonValueKind.Array)
        {
            var names = properties.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .ToList();
            if (names.Count == 0 || names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            {
                AddError(
                    issues,
                    "browser-layout-style-properties-invalid",
                    "#/payload/styleProperties",
                    "The style property list must be nonempty and hold no duplicates.");
            }
        }
    }

    private static void ValidateBrowserLayoutCheckpointNode(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredInteger("nodeIndex", nonnegative: true),
                RequiredInteger("nodeId", positive: true),
                RequiredEnum("nodeType", "element", "text", "pseudo-element"),
                RequiredString("nodeName"),
                RequiredBoolean("layoutObjectPresent"),
                RequiredBoolean("displayLocked"),
                NullableObject("boundingClientRect"),
                new PropertyRule(
                    "computedStyle",
                    true,
                    true,
                    value => value.ValueKind == JsonValueKind.Object &&
                        value.EnumerateObject().All(entry =>
                            entry.Name.Length > 0 &&
                            entry.Value.ValueKind is
                                JsonValueKind.String or JsonValueKind.Null),
                    "must be an object of string or null values, or null"),
                CustomPropertiesRule(),
                NullableObject("pseudoElement"),
                NullableInteger("shadowHostNodeId", positive: true),
                NullableEnum("shadowRootMode", "open", "closed", "user-agent"),
                BoxFragmentsRule()
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateBrowserLayoutPseudoElement(payload, issues);
        ValidateBrowserLayoutStyleCompleteness(payload, changeRecord: false, issues);
        ValidateBrowserLayoutBoxFragments(payload, issues);
        if (HasNonnullProperty(payload, "shadowHostNodeId") !=
            HasNonnullProperty(payload, "shadowRootMode"))
        {
            AddError(
                issues,
                "browser-layout-shadow-scope-inconsistent",
                "#/payload/shadowHostNodeId",
                "A shadow host and a shadow root mode must be recorded together.");
        }

        var hasRect = payload.TryGetProperty("boundingClientRect", out var rect) &&
            rect.ValueKind == JsonValueKind.Object;
        if (hasRect)
        {
            ValidateShape(
                rect,
                [
                    RequiredNumber("x"),
                    RequiredNumber("y"),
                    RequiredNumber("width", nonnegative: true),
                    RequiredNumber("height", nonnegative: true)
                ],
                issues,
                "#/payload/boundingClientRect");
        }
        if (payload.TryGetProperty("layoutObjectPresent", out var layoutObject) &&
            IsBoolean(layoutObject) &&
            layoutObject.GetBoolean() != hasRect)
        {
            AddError(
                issues,
                "browser-layout-rect-inconsistent",
                "#/payload/boundingClientRect",
                "A bounding rectangle must be present exactly when the node has " +
                    "a layout object.");
        }
        if (ReadString(payload, "nodeType") == "text")
        {
            var hasStyle = payload.TryGetProperty("computedStyle", out var style) &&
                style.ValueKind != JsonValueKind.Null;
            if (hasStyle || !hasRect)
            {
                AddError(
                    issues,
                    "browser-layout-text-node-inconsistent",
                    "#/payload/nodeType",
                    "A text node record must have a layout object and no " +
                        "computed style.");
            }
        }
    }

    // A pseudo-element record carries its pseudo-element description, and no
    // other record does.
    private static void ValidateBrowserLayoutPseudoElement(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        var isPseudo = ReadString(payload, "nodeType") == "pseudo-element";
        var hasPseudo = payload.TryGetProperty("pseudoElement", out var pseudo) &&
            pseudo.ValueKind == JsonValueKind.Object;
        if (isPseudo != hasPseudo)
        {
            AddError(
                issues,
                "browser-layout-pseudo-element-inconsistent",
                "#/payload/pseudoElement",
                "A pseudo-element description must be present exactly when the " +
                    "record is a pseudo-element.");
        }
        if (!hasPseudo)
        {
            return;
        }

        const string pointer = "#/payload/pseudoElement";
        ValidateShape(
            pseudo,
            [
                NullableInteger("originatingNodeId", positive: true),
                RequiredString("pseudoType"),
                RequiredText("generatedText"),
                RequiredInteger("generatedTextLength", nonnegative: true),
                RequiredBoolean("generatedTextTruncated")
            ],
            issues,
            pointer);
        ValidateTruncatedText(
            pseudo,
            "generatedText",
            "generatedTextLength",
            "generatedTextTruncated",
            issues);
    }

    private static void ValidateBrowserLayoutCheckpointCompleted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "rendering-update"),
                RequiredInteger("nodeCount", nonnegative: true),
                RequiredBoolean("truncated"),
                RequiredInteger("maximumNodes", positive: true),
                RequiredInteger("pseudoElementCount", nonnegative: true),
                RequiredInteger("shadowRootCount", nonnegative: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        var count = ReadNullableInteger(payload, "nodeCount");
        var maximum = ReadNullableInteger(payload, "maximumNodes");
        if (count is not null && maximum is not null && count > maximum)
        {
            AddError(
                issues,
                "browser-layout-node-count-over-maximum",
                "#/payload/nodeCount",
                $"The checkpoint reports {count} nodes, more than the stated " +
                    $"maximum of {maximum}.");
        }
        var pseudoCount = ReadNullableInteger(payload, "pseudoElementCount");
        if (count is not null && pseudoCount is not null && pseudoCount > count)
        {
            AddError(
                issues,
                "browser-layout-pseudo-element-count-over-node-count",
                "#/payload/pseudoElementCount",
                $"The checkpoint reports {pseudoCount} pseudo-elements among " +
                    $"{count} nodes.");
        }
    }

    // Layout change records (protocol 0.32). Change sets are named
    // "layout-changes-N" and transform nodes "layout-transform-N".
    private static readonly string[] LayoutChangeReasons =
        ["style", "layout", "paint-properties"];

    // Custom properties (protocol 0.37): each name with its value, or null
    // with no computed style. Absent from records of earlier versions.
    private static PropertyRule CustomPropertiesRule() =>
        new(
            "customProperties",
            false,
            true,
            value => value.ValueKind == JsonValueKind.Object &&
                value.EnumerateObject().All(entry =>
                    entry.Name.StartsWith("--", StringComparison.Ordinal) &&
                    entry.Name.Length > 2 &&
                    entry.Value.ValueKind is
                        JsonValueKind.String or JsonValueKind.Null),
            "must be an object of custom property values, or null");

    // A record with no computed style states no custom properties and, for
    // a change record, no completeness or removals. A change record whose
    // style is not complete holds the changed values and names the removed
    // custom properties; a complete one names none.
    private static void ValidateBrowserLayoutStyleCompleteness(
        JsonElement payload,
        bool changeRecord,
        ICollection<EventValidationIssue> issues)
    {
        var hasStyle = HasNonnullProperty(payload, "computedStyle");
        if (!hasStyle && HasNonnullProperty(payload, "customProperties"))
        {
            AddError(
                issues,
                "browser-layout-custom-properties-without-style",
                "#/payload/customProperties",
                "Custom properties are recorded only with a computed style.");
        }
        if (!changeRecord)
        {
            return;
        }
        var hasComplete = HasNonnullProperty(payload, "computedStyleComplete");
        var hasRemoved = HasNonnullProperty(payload, "removedCustomProperties");
        if (!hasStyle && (hasComplete || hasRemoved))
        {
            AddError(
                issues,
                "browser-layout-style-completeness-without-style",
                "#/payload/computedStyleComplete",
                "Style completeness and removals are recorded only with a computed style.");
            return;
        }
        if (hasComplete &&
            payload.GetProperty("computedStyleComplete").ValueKind == JsonValueKind.False)
        {
            if (!hasRemoved || !HasNonnullProperty(payload, "customProperties"))
            {
                AddError(
                    issues,
                    "browser-layout-style-changes-incomplete",
                    "#/payload/removedCustomProperties",
                    "A record of changed style values names its custom properties and their removals.");
            }
        }
        else if (hasRemoved)
        {
            AddError(
                issues,
                "browser-layout-style-removals-in-complete-style",
                "#/payload/removedCustomProperties",
                "A complete computed style names no removed custom properties.");
        }
    }

    // Box fragments (protocol 0.38): an object, or null for a node whose
    // layout object is not a box. Absent from records of earlier versions.
    private static PropertyRule BoxFragmentsRule() =>
        new(
            "boxFragments",
            false,
            true,
            value => value.ValueKind == JsonValueKind.Object,
            "must be an object, or null");

    private static readonly string[] FragmentChildKinds =
        ["box", "anonymous", "column", "page", "line"];

    // A node's box fragments: the effective zoom, each fragment, and a
    // replaced element's natural size. Only a node with a layout object other
    // than text has them.
    private static void ValidateBrowserLayoutBoxFragments(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("boxFragments", out var fragments) ||
            fragments.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        const string pointer = "#/payload/boxFragments";
        var hasLayoutObject = payload.TryGetProperty("layoutObjectPresent", out var layoutObject) &&
            layoutObject.ValueKind == JsonValueKind.True;
        if (!hasLayoutObject || ReadString(payload, "nodeType") == "text")
        {
            AddError(
                issues,
                "browser-layout-box-fragments-without-box",
                pointer,
                "Box fragments are recorded only for a node whose layout object is a box.");
        }
        ValidateShape(
            fragments,
            [
                RequiredNumber("effectiveZoom", positive: true),
                RequiredObjectArray("fragments"),
                NullableObject("naturalSize"),
                OptionalNullableText("textContent"),
                OptionalNullableText("firstLineText"),
                OptionalBoolean("textContentUnchanged")
            ],
            issues,
            pointer);
        var unchanged = fragments.TryGetProperty("textContentUnchanged", out var unchangedValue) &&
            unchangedValue.ValueKind == JsonValueKind.True;
        var text = ReadString(fragments, "textContent");
        if ((unchanged && (text is not null || HasNonnullProperty(fragments, "firstLineText"))) ||
            (text is null && HasNonnullProperty(fragments, "firstLineText")))
        {
            AddError(
                issues,
                "browser-layout-text-content-inconsistent",
                pointer,
                "Text left out as unchanged is null, and a first-line text comes only with a text content.");
        }
        var ownItems = false;
        if (fragments.TryGetProperty("fragments", out var list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var fragment in list.EnumerateArray())
            {
                if (fragment.ValueKind == JsonValueKind.Object)
                {
                    ownItems |= HasNonnullProperty(fragment, "items");
                    ValidateBrowserLayoutBoxFragment(
                        fragment,
                        $"{pointer}/fragments/{index}",
                        issues,
                        heldByLink: false,
                        nodeTextLength: text?.Length);
                }
                index++;
            }
        }
        if (ownItems != (text is not null || unchanged))
        {
            AddError(
                issues,
                "browser-layout-text-content-inconsistent",
                pointer,
                "A node states its text content, or that it is unchanged, exactly when one of its fragments holds items.");
        }
        if (fragments.TryGetProperty("naturalSize", out var natural) &&
            natural.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                natural,
                [
                    RequiredNumber("width", nonnegative: true),
                    RequiredNumber("height", nonnegative: true),
                    RequiredBoolean("hasWidth"),
                    RequiredBoolean("hasHeight"),
                    RequiredNumber("aspectRatioWidth", nonnegative: true),
                    RequiredNumber("aspectRatioHeight", nonnegative: true)
                ],
                issues,
                $"{pointer}/naturalSize");
        }
    }

    private static void ValidateBrowserLayoutBoxFragment(
        JsonElement fragment,
        string pointer,
        ICollection<EventValidationIssue> issues,
        bool heldByLink,
        int? nodeTextLength)
    {
        ValidateShape(
            fragment,
            [
                RequiredNumber("width", nonnegative: true),
                RequiredNumber("height", nonnegative: true),
                NullableObject("breakToken"),
                NullableObject("scrollableOverflow"),
                RequiredObjectArray("children"),
                OptionalNullableObjectArray("items"),
                OptionalNullableText("textContent"),
                OptionalNullableText("firstLineText")
            ],
            issues,
            pointer);
        var ownText = ReadString(fragment, "textContent");
        var hasItems = HasNonnullProperty(fragment, "items");
        if ((heldByLink ? hasItems != (ownText is not null) : ownText is not null) ||
            (ownText is null && HasNonnullProperty(fragment, "firstLineText")))
        {
            AddError(
                issues,
                "browser-layout-text-content-inconsistent",
                pointer,
                "A fragment held by a child link states its text exactly when it holds items; a node's own fragments leave their text to the node.");
        }
        if (hasItems && fragment.TryGetProperty("items", out var items))
        {
            ValidateBrowserLayoutFragmentItems(
                items,
                $"{pointer}/items",
                issues,
                heldByLink ? ownText?.Length : nodeTextLength);
        }
        if (fragment.TryGetProperty("breakToken", out var token) &&
            token.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                token,
                [
                    RequiredNumber("consumedBlockSize", nonnegative: true),
                    RequiredBoolean("breakBefore"),
                    NullableInteger("sequenceNumber", nonnegative: true),
                    RequiredBoolean("atBlockEnd")
                ],
                issues,
                $"{pointer}/breakToken");
            var breakBefore = token.TryGetProperty("breakBefore", out var before) &&
                before.ValueKind == JsonValueKind.True;
            if (breakBefore == HasNonnullProperty(token, "sequenceNumber"))
            {
                AddError(
                    issues,
                    "browser-layout-break-token-inconsistent",
                    $"{pointer}/breakToken",
                    "A break token states a sequence number exactly when it is not a break before.");
            }
        }
        if (fragment.TryGetProperty("scrollableOverflow", out var overflow) &&
            overflow.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                overflow,
                [
                    RequiredNumber("x"),
                    RequiredNumber("y"),
                    RequiredNumber("width", nonnegative: true),
                    RequiredNumber("height", nonnegative: true)
                ],
                issues,
                $"{pointer}/scrollableOverflow");
        }
        if (!fragment.TryGetProperty("children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        var index = 0;
        foreach (var child in children.EnumerateArray())
        {
            if (child.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }
            var childPointer = $"{pointer}/children/{index}";
            ValidateShape(
                child,
                [
                    RequiredEnum("kind", FragmentChildKinds),
                    RequiredNumber("x"),
                    RequiredNumber("y"),
                    NullableInteger("nodeId", positive: true),
                    NullableInteger("fragmentIndex", nonnegative: true),
                    NullableObject("fragment")
                ],
                issues,
                childPointer);
            var kind = ReadString(child, "kind");
            var isBox = kind == "box";
            if (isBox != HasNonnullProperty(child, "nodeId") ||
                (!isBox && HasNonnullProperty(child, "fragmentIndex")))
            {
                AddError(
                    issues,
                    "browser-layout-fragment-child-node-inconsistent",
                    childPointer,
                    "A child link names a node and its fragment index only when it is a box with a node.");
            }
            var holdsFragment = kind is "anonymous" or "column" or "page";
            var hasFragment = child.TryGetProperty("fragment", out var nested) &&
                nested.ValueKind == JsonValueKind.Object;
            if (kind is not null && holdsFragment != hasFragment)
            {
                AddError(
                    issues,
                    "browser-layout-fragment-child-fragment-inconsistent",
                    childPointer,
                    "A child link holds its own fragment exactly when it has no node: an anonymous box, column, or page.");
            }
            if (hasFragment)
            {
                ValidateBrowserLayoutBoxFragment(
                    nested,
                    $"{childPointer}/fragment",
                    issues,
                    heldByLink: true,
                    nodeTextLength: null);
            }
            index++;
        }
    }

    private static readonly string[] FragmentItemTypes =
        ["line", "text", "generated-text", "box"];

    // The items of a fragment that holds lines (protocol 0.39), in pre-order.
    // textLength is the length of the text their ranges index, in UTF-16 code
    // units, or null when it is not in the record, being unchanged.
    private static void ValidateBrowserLayoutFragmentItems(
        JsonElement items,
        string pointer,
        ICollection<EventValidationIssue> issues,
        int? textLength)
    {
        var count = items.GetArrayLength();
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var itemPointer = $"{pointer}/{index}";
            if (item.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }
            ValidateShape(
                item,
                [
                    RequiredEnum("type", FragmentItemTypes),
                    RequiredNumber("x"),
                    RequiredNumber("y"),
                    RequiredNumber("width", nonnegative: true),
                    RequiredNumber("height", nonnegative: true),
                    NullableInteger("descendantsCount", positive: true),
                    NullableInteger("nodeId", positive: true),
                    NullableInteger("start", nonnegative: true),
                    NullableInteger("end", nonnegative: true),
                    NullableBoolean("firstLineStyle"),
                    NullableEnum("direction", "ltr", "rtl"),
                    NullableBoolean("hiddenForPaint"),
                    NullableObjectArray("glyphRuns"),
                    NullableText("generatedText")
                ],
                issues,
                itemPointer);
            var type = ReadString(item, "type");
            var container = type is "line" or "box";
            var textLike = type is "text" or "generated-text";
            var spans = item.TryGetProperty("descendantsCount", out var descendants) &&
                descendants.ValueKind == JsonValueKind.Number &&
                descendants.TryGetInt32(out var spanned)
                    ? spanned
                    : (int?)null;
            var hasStart = item.TryGetProperty("start", out var startValue) &&
                startValue.ValueKind == JsonValueKind.Number;
            var hasEnd = item.TryGetProperty("end", out var endValue) &&
                endValue.ValueKind == JsonValueKind.Number;
            if (type is not null &&
                (container != HasNonnullProperty(item, "descendantsCount") ||
                 (spans is { } span && index + span > count) ||
                 (type == "text") != hasStart ||
                 hasStart != hasEnd ||
                 (type == "generated-text") != HasNonnullProperty(item, "generatedText") ||
                 textLike != HasNonnullProperty(item, "firstLineStyle") ||
                 textLike != HasNonnullProperty(item, "direction") ||
                 textLike != HasNonnullProperty(item, "hiddenForPaint") ||
                 textLike != HasNonnullProperty(item, "glyphRuns")))
            {
                AddError(
                    issues,
                    "browser-layout-fragment-item-inconsistent",
                    itemPointer,
                    "An item states what its type has: a line or box the items it spans, within the list; a text item its range; text and generated text their glyph runs; generated text its text.");
            }
            if (hasStart && hasEnd &&
                startValue.TryGetInt64(out var start) && endValue.TryGetInt64(out var end) &&
                (start > end || (textLength is { } length && end > length)))
            {
                AddError(
                    issues,
                    "browser-layout-fragment-item-range-outside-text",
                    itemPointer,
                    "A text item's range lies within the text content.");
            }
            if (item.TryGetProperty("glyphRuns", out var runs) &&
                runs.ValueKind == JsonValueKind.Array)
            {
                var runIndex = 0;
                foreach (var run in runs.EnumerateArray())
                {
                    if (run.ValueKind == JsonValueKind.Object)
                    {
                        ValidateBrowserLayoutGlyphRun(
                            run, $"{itemPointer}/glyphRuns/{runIndex}", issues);
                    }
                    runIndex++;
                }
            }
            index++;
        }
    }

    private static void ValidateBrowserLayoutGlyphRun(
        JsonElement run,
        string pointer,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            run,
            [
                RequiredObject("font"),
                RequiredBoolean("horizontal"),
                RequiredInteger("rotation", nonnegative: true),
                RequiredText("glyphs"),
                OptionalNullableObject("fontFile")
            ],
            issues,
            pointer);
        if (run.TryGetProperty("fontFile", out var fontFile) &&
            fontFile.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                fontFile,
                [
                    DigestRule("digest"),
                    RequiredInteger("index", nonnegative: true),
                    RequiredObjectArray("variations")
                ],
                issues,
                $"{pointer}/fontFile");
            if (fontFile.TryGetProperty("variations", out var variations) &&
                variations.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var variation in variations.EnumerateArray())
                {
                    if (variation.ValueKind == JsonValueKind.Object)
                    {
                        ValidateShape(
                            variation,
                            [RequiredText("axis"), RequiredNumber("value")],
                            issues,
                            $"{pointer}/fontFile/variations/{index}");
                    }
                    index++;
                }
            }
        }
        if (run.TryGetProperty("font", out var font) && font.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                font,
                [
                    RequiredText("family"),
                    RequiredText("postScriptName"),
                    RequiredNumber("size", nonnegative: true),
                    RequiredBoolean("syntheticBold"),
                    RequiredBoolean("syntheticItalic")
                ],
                issues,
                $"{pointer}/font");
        }
        var glyphs = ReadString(run, "glyphs");
        if (glyphs is null)
        {
            return;
        }
        var buffer = new byte[(glyphs.Length + 3) / 4 * 3];
        if (!Convert.TryFromBase64String(glyphs, buffer, out var written) ||
            written % Recorder.Contracts.BrowserLayoutGlyphs.PackedGlyphBytes != 0)
        {
            AddError(
                issues,
                "browser-layout-glyphs-invalid",
                $"{pointer}/glyphs",
                "Glyphs are base64 of whole packed glyphs, 18 bytes each.");
        }
    }

    private static PropertyRule ComputedStyleRule() =>
        new(
            "computedStyle",
            true,
            true,
            value => value.ValueKind == JsonValueKind.Object &&
                value.EnumerateObject().All(entry =>
                    entry.Name.Length > 0 &&
                    entry.Value.ValueKind is
                        JsonValueKind.String or JsonValueKind.Null),
            "must be an object of string or null values, or null");

    private static void ValidateLayoutIdentity(
        JsonElement payload,
        string property,
        string prefix,
        string code,
        ICollection<EventValidationIssue> issues,
        string path = "#/payload")
    {
        var value = ReadString(payload, property);
        if (value is not null && !IsCheckpointIdentity(value, prefix))
        {
            AddError(
                issues,
                code,
                $"{path}/{property}",
                $"'{value}' is not a '{prefix}N' identity.");
        }
    }

    private static void ValidateLayoutChangeSetIdentity(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateLayoutIdentity(
            payload,
            "changeSetId",
            "layout-changes-",
            "browser-layout-change-set-id-invalid",
            issues);
    }

    private static void ValidateBrowserLayoutChangesStarted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("changeSetId"),
                NullableString("layoutCheckpointId"),
                RequiredBoolean("checkpointUpdate"),
                RequiredString("viewTransformNodeId"),
                RequiredObject("viewPaintOffset"),
                RequiredNumber("layoutZoomFactor", positive: true),
                // Protocol 0.58.
                OptionalObject("viewport"),
                OptionalNumber("devicePixelRatio", positive: true)
            ],
            issues);
        if (payload.TryGetProperty("viewport", out var changesViewport) &&
            changesViewport.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                changesViewport,
                [
                    RequiredNumber("width", nonnegative: true),
                    RequiredNumber("height", nonnegative: true)
                ],
                issues,
                "#/payload/viewport");
        }
        if (payload.TryGetProperty("viewport", out _) != payload.TryGetProperty("devicePixelRatio", out _))
        {
            AddError(
                issues,
                "browser-layout-change-viewport-inconsistent",
                "#/payload/viewport",
                "A change set records its viewport and device pixel ratio together.");
        }
        ValidateLayoutChangeSetIdentity(payload, issues);
        ValidateLayoutIdentity(
            payload,
            "layoutCheckpointId",
            "layout-checkpoint-",
            "browser-layout-change-checkpoint-id-invalid",
            issues);
        if (payload.TryGetProperty("checkpointUpdate", out var update) &&
            update.ValueKind == JsonValueKind.True &&
            !HasNonnullProperty(payload, "layoutCheckpointId"))
        {
            AddError(
                issues,
                "browser-layout-change-checkpoint-update-inconsistent",
                "#/payload/checkpointUpdate",
                "A change set of a checkpoint's update names that checkpoint.");
        }
        ValidateLayoutIdentity(
            payload,
            "viewTransformNodeId",
            "layout-transform-",
            "browser-layout-transform-node-id-invalid",
            issues);
        if (payload.TryGetProperty("viewPaintOffset", out var offset) &&
            offset.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                offset,
                [RequiredNumber("x"), RequiredNumber("y")],
                issues,
                "#/payload/viewPaintOffset");
        }
    }

    private static void ValidateBrowserLayoutTransformNode(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("changeSetId"),
                RequiredString("transformNodeId"),
                NullableString("parentTransformNodeId"),
                new PropertyRule(
                    "matrix",
                    true,
                    false,
                    value => value.ValueKind == JsonValueKind.Array &&
                        value.GetArrayLength() == 16 &&
                        value.EnumerateArray().All(item =>
                            item.ValueKind == JsonValueKind.Number &&
                            double.IsFinite(item.GetDouble())),
                    "must be an array of 16 finite numbers"),
                RequiredBoolean("flattensInheritedTransform"),
                RequiredBoolean("scrollTranslation"),
                RequiredBoolean("sticky")
            ],
            issues);
        ValidateLayoutChangeSetIdentity(payload, issues);
        ValidateLayoutIdentity(
            payload,
            "transformNodeId",
            "layout-transform-",
            "browser-layout-transform-node-id-invalid",
            issues);
        ValidateLayoutIdentity(
            payload,
            "parentTransformNodeId",
            "layout-transform-",
            "browser-layout-transform-node-id-invalid",
            issues);
        var id = ReadString(payload, "transformNodeId");
        if (id is not null && id == ReadString(payload, "parentTransformNodeId"))
        {
            AddError(
                issues,
                "browser-layout-transform-node-parent-self",
                "#/payload/parentTransformNodeId",
                "A transform node names itself as its parent.");
        }
    }

    private static void ValidateBrowserLayoutNodeChanged(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("changeSetId"),
                new PropertyRule(
                    "reasons",
                    true,
                    false,
                    value => value.ValueKind == JsonValueKind.Array &&
                        value.GetArrayLength() > 0 &&
                        value.EnumerateArray().All(item =>
                            item.ValueKind == JsonValueKind.String &&
                            LayoutChangeReasons.Contains(item.GetString())) &&
                        value.EnumerateArray().Select(item => item.GetString())
                            .Distinct(StringComparer.Ordinal).Count() ==
                            value.GetArrayLength(),
                    "must be a nonempty array of distinct change reasons"),
                RequiredInteger("nodeId", positive: true),
                RequiredEnum("nodeType", "element", "text", "pseudo-element"),
                RequiredString("nodeName"),
                RequiredBoolean("layoutObjectPresent"),
                RequiredBoolean("displayLocked"),
                NullableObject("geometry"),
                ComputedStyleRule(),
                OptionalNullableBoolean("computedStyleComplete"),
                CustomPropertiesRule(),
                new PropertyRule(
                    "removedCustomProperties",
                    false,
                    true,
                    value => value.ValueKind == JsonValueKind.Array &&
                        value.EnumerateArray().All(item =>
                            item.ValueKind == JsonValueKind.String &&
                            item.GetString()!.StartsWith("--", StringComparison.Ordinal)) &&
                        value.EnumerateArray().Select(item => item.GetString())
                            .Distinct(StringComparer.Ordinal).Count() ==
                            value.GetArrayLength(),
                    "must be an array of distinct custom property names, or null"),
                NullableObject("pseudoElement"),
                NullableInteger("shadowHostNodeId", positive: true),
                NullableEnum("shadowRootMode", "open", "closed", "user-agent"),
                BoxFragmentsRule()
            ],
            issues);
        ValidateLayoutChangeSetIdentity(payload, issues);
        ValidateBrowserLayoutPseudoElement(payload, issues);
        ValidateBrowserLayoutStyleCompleteness(payload, changeRecord: true, issues);
        ValidateBrowserLayoutBoxFragments(payload, issues);
        if (HasNonnullProperty(payload, "shadowHostNodeId") !=
            HasNonnullProperty(payload, "shadowRootMode"))
        {
            AddError(
                issues,
                "browser-layout-shadow-scope-inconsistent",
                "#/payload/shadowHostNodeId",
                "A shadow host and a shadow root mode must be recorded together.");
        }
        var hasGeometry = payload.TryGetProperty("geometry", out var geometry) &&
            geometry.ValueKind == JsonValueKind.Object;
        if (hasGeometry)
        {
            const string pointer = "#/payload/geometry";
            ValidateShape(
                geometry,
                [
                    RequiredString("transformNodeId"),
                    NullableObject("localRect"),
                    NullableObjectArray("localQuadRects"),
                    RequiredBoolean("clientRectEmpty"),
                    RequiredBoolean("localRectMapped"),
                    RequiredNumber("clientRectScale", positive: true)
                ],
                issues,
                pointer);
            ValidateLayoutIdentity(
                geometry,
                "transformNodeId",
                "layout-transform-",
                "browser-layout-transform-node-id-invalid",
                issues,
                pointer);
            var hasLocalRect = geometry.TryGetProperty("localRect", out var rect) &&
                rect.ValueKind == JsonValueKind.Object;
            if (hasLocalRect)
            {
                ValidateShape(
                    rect,
                    [
                        RequiredNumber("x"),
                        RequiredNumber("y"),
                        RequiredNumber("width", nonnegative: true),
                        RequiredNumber("height", nonnegative: true)
                    ],
                    issues,
                    pointer + "/localRect");
            }
            var mapped = geometry.TryGetProperty("localRectMapped", out var mappedValue) &&
                IsBoolean(mappedValue) && mappedValue.GetBoolean();
            var empty = geometry.TryGetProperty("clientRectEmpty", out var emptyValue) &&
                IsBoolean(emptyValue) && emptyValue.GetBoolean();
            if (mapped != hasLocalRect || (empty && mapped))
            {
                AddError(
                    issues,
                    "browser-layout-local-rect-inconsistent",
                    pointer + "/localRect",
                    "A local rectangle must be present exactly when it was mapped, " +
                        "and an empty client rectangle is not mapped.");
            }
            // From protocol 0.36 a node with more than one quad states the
            // bounds of each in the transform node's space.
            if (geometry.TryGetProperty("localQuadRects", out var quadRects) &&
                quadRects.ValueKind == JsonValueKind.Array)
            {
                if (!mapped || quadRects.GetArrayLength() < 2)
                {
                    AddError(
                        issues,
                        "browser-layout-local-quad-rects-inconsistent",
                        pointer + "/localQuadRects",
                        "Quad rectangles are recorded only for a mapped rectangle " +
                            "united from more than one quad.");
                }
                var index = 0;
                foreach (var quadRect in quadRects.EnumerateArray())
                {
                    if (quadRect.ValueKind == JsonValueKind.Object)
                    {
                        ValidateShape(
                            quadRect,
                            [
                                RequiredNumber("x"),
                                RequiredNumber("y"),
                                RequiredNumber("width", nonnegative: true),
                                RequiredNumber("height", nonnegative: true)
                            ],
                            issues,
                            $"{pointer}/localQuadRects/{index}");
                    }
                    index++;
                }
            }
        }
        if (hasGeometry &&
            payload.TryGetProperty("layoutObjectPresent", out var layoutObject) &&
            IsBoolean(layoutObject) && !layoutObject.GetBoolean())
        {
            AddError(
                issues,
                "browser-layout-geometry-inconsistent",
                "#/payload/geometry",
                "Geometry is recorded only for a node with a layout object.");
        }
        if (ReadString(payload, "nodeType") == "text")
        {
            var hasStyle = payload.TryGetProperty("computedStyle", out var style) &&
                style.ValueKind != JsonValueKind.Null;
            // From protocol 0.41 a text node whose layout object was
            // destroyed is recorded with none, so a text node change record
            // need not state a layout object.
            if (hasStyle)
            {
                AddError(
                    issues,
                    "browser-layout-text-node-inconsistent",
                    "#/payload/nodeType",
                    "A text node record must have no computed style.");
            }
        }
    }

    private static void ValidateBrowserLayoutScrollOffsetChanged(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("changeSetId"),
                RequiredInteger("nodeId", positive: true),
                RequiredObject("scrollOffset"),
                RequiredObject("webExposedScrollOffset"),
                RequiredObject("scrollOrigin"),
                RequiredNumber("effectiveZoom", positive: true),
                NullableString("scrollTranslationNodeId"),
                // Protocol 0.49: absent from earlier recordings.
                new PropertyRule(
                    "scrollElementId",
                    false,
                    true,
                    value => value.ValueKind == JsonValueKind.String &&
                        IsPositiveDecimal(value.GetString(), ulong.MaxValue),
                    "must be a positive decimal integer string or null")
            ],
            issues);
        ValidateLayoutChangeSetIdentity(payload, issues);
        ValidateLayoutIdentity(
            payload,
            "scrollTranslationNodeId",
            "layout-transform-",
            "browser-layout-transform-node-id-invalid",
            issues);
        foreach (var property in new[]
            { "scrollOffset", "webExposedScrollOffset", "scrollOrigin" })
        {
            if (payload.TryGetProperty(property, out var point) &&
                point.ValueKind == JsonValueKind.Object)
            {
                ValidateShape(
                    point,
                    [RequiredNumber("x"), RequiredNumber("y")],
                    issues,
                    $"#/payload/{property}");
            }
        }
    }

    private static void ValidateBrowserLayoutChangesCompleted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("changeSetId"),
                RequiredInteger("notedNodeCount", nonnegative: true),
                RequiredInteger("recordedNodeCount", nonnegative: true),
                RequiredInteger("unchangedNodeCount", nonnegative: true),
                RequiredInteger("transformNodeCount", nonnegative: true),
                RequiredInteger("scrollOffsetCount", nonnegative: true)
            ],
            issues);
        ValidateLayoutChangeSetIdentity(payload, issues);
        var noted = ReadNullableInteger(payload, "notedNodeCount");
        var recorded = ReadNullableInteger(payload, "recordedNodeCount");
        var unchanged = ReadNullableInteger(payload, "unchangedNodeCount");
        if (noted is not null && recorded is not null && unchanged is not null &&
            (long)recorded + unchanged > noted)
        {
            AddError(
                issues,
                "browser-layout-change-counts-inconsistent",
                "#/payload/recordedNodeCount",
                $"{recorded} recorded and {unchanged} unchanged nodes exceed " +
                    $"the {noted} nodes noted.");
        }
    }

    // Presentation records. Frame tokens are unsigned 32-bit values, tick and
    // microsecond fields are decimal strings because they exceed the range a
    // JSON number carries exactly, and the frame sink is "clientId:sinkId".
    private static readonly string[] PresentationFeedbackFlagNames =
        ["vsync", "hw-clock", "hw-completion", "zero-copy", "failure"];

    private static readonly string[] PresentationTickProperties =
    [
        "presentedTicks",
        "receivedCompositorFrameTicks",
        "drawStartTicks",
        "swapStartTicks",
        "swapEndTicks"
    ];

    private static bool IsPositiveDecimal(string? value, ulong maximum) =>
        value is not null &&
        value.Length > 0 &&
        value[0] != '0' &&
        value.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0 &&
        ulong.TryParse(value, out var number) &&
        number <= maximum;

    private static bool IsNonnegativeDecimal(string? value) =>
        value is not null &&
        value.Length > 0 &&
        (value == "0" || value[0] != '0') &&
        value.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0 &&
        long.TryParse(value, out _);

    private static bool IsFrameSinkIdentity(string? value)
    {
        if (value is null)
        {
            return false;
        }
        var separator = value.IndexOf(':');
        return separator > 0 &&
            IsNonnegativeDecimal(value[..separator]) &&
            uint.TryParse(value[..separator], out _) &&
            IsNonnegativeDecimal(value[(separator + 1)..]) &&
            uint.TryParse(value[(separator + 1)..], out _);
    }

    private static PropertyRule RequiredFrameSinkId(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.String &&
                IsFrameSinkIdentity(value.GetString()),
            "must be a frame sink identity written clientId:sinkId");

    private static PropertyRule NullableFrameSinkId(string name) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.String &&
                IsFrameSinkIdentity(value.GetString()),
            "must be a frame sink identity written clientId:sinkId, or null");

    private static PropertyRule RequiredFrameToken(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.String &&
                IsPositiveDecimal(value.GetString(), uint.MaxValue),
            "must be a positive unsigned 32-bit decimal string");

    private static PropertyRule RequiredDecimalText(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.String &&
                IsNonnegativeDecimal(value.GetString()),
            "must be a nonnegative decimal integer string");

    private static PropertyRule NullableDecimalText(string name) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.String &&
                IsNonnegativeDecimal(value.GetString()),
            "must be a nonnegative decimal integer string or null");

    private static PropertyRule NullablePositiveDecimalText(string name) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.String &&
                IsPositiveDecimal(value.GetString(), long.MaxValue),
            "must be a positive decimal integer string or null");

    // From protocol 0.43 a widget is a frame widget, named by its frame sink,
    // or a page popup's widget, which is not told its frame sink.
    private static PropertyRule[] PresentationBaseRules(bool widgetRequired) =>
    [
        RequiredObject("context"),
        RequiredString("requestId"),
        widgetRequired
            ? RequiredEnum("widgetKind", "frame", "page-popup")
            : NullableEnum("widgetKind", "frame", "page-popup"),
        NullableFrameSinkId("frameSinkId"),
        widgetRequired
            ? RequiredString("localRootFrameToken")
            : NullableString("localRootFrameToken")
    ];

    private static void ValidatePresentationBase(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        var requestId = ReadString(payload, "requestId");
        if (requestId is not null &&
            !IsCheckpointIdentity(requestId, "presentation-request-"))
        {
            AddError(
                issues,
                "browser-presentation-request-id-invalid",
                "#/payload/requestId",
                $"'{requestId}' is not a presentation request identity.");
        }
        if (payload.TryGetProperty("localRootFrameToken", out var token) &&
            token.ValueKind == JsonValueKind.String &&
            string.IsNullOrWhiteSpace(token.GetString()))
        {
            AddError(
                issues,
                "browser-presentation-frame-token-empty",
                "#/payload/localRootFrameToken",
                "A named local root must carry a nonempty frame token.");
        }

        var kind = ReadString(payload, "widgetKind");
        var hasSink = HasNonnullProperty(payload, "frameSinkId");
        var hasToken = HasNonnullProperty(payload, "localRootFrameToken");
        var widgetConsistent = kind switch
        {
            "frame" => hasSink && hasToken,
            "page-popup" => !hasSink && hasToken,
            null => !hasSink && !hasToken,
            _ => true
        };
        if (!widgetConsistent)
        {
            AddError(
                issues,
                "browser-presentation-widget-inconsistent",
                "#/payload/widgetKind",
                "A frame widget names its frame sink and local root; a page " +
                    "popup's widget names its local root only; a request " +
                    "without a widget names neither.");
        }
    }

    private static void ValidateBrowserPresentationRequested(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                .. PresentationBaseRules(widgetRequired: false),
                NullableString("layoutCheckpointId"),
                NullableString("layoutChangeSetId"),
                RequiredBoolean("queued"),
                NullableEnum("notQueuedReason", "no-widget", "not-compositing"),
                NullableInteger("sourceFrameNumber", nonnegative: true),
                NullableBoolean("isMainFrameWidget"),
                RequiredBoolean("highResolutionTicks"),
                RequiredInteger("maximumNotSwappedRecords", positive: true)
            ],
            issues);
        ValidatePresentationBase(payload, issues);
        var checkpointId = ReadString(payload, "layoutCheckpointId");
        if (checkpointId is not null &&
            !IsCheckpointIdentity(checkpointId, "layout-checkpoint-"))
        {
            AddError(
                issues,
                "browser-presentation-checkpoint-id-invalid",
                "#/payload/layoutCheckpointId",
                $"'{checkpointId}' is not a layout checkpoint identity.");
        }
        // From protocol 0.35 a request follows a layout checkpoint or, for a
        // rendering update that was not walked, its layout change set.
        var changeSetId = ReadString(payload, "layoutChangeSetId");
        if (changeSetId is not null &&
            !IsCheckpointIdentity(changeSetId, "layout-changes-"))
        {
            AddError(
                issues,
                "browser-presentation-change-set-id-invalid",
                "#/payload/layoutChangeSetId",
                $"'{changeSetId}' is not a layout change set identity.");
        }
        if ((checkpointId is null) == (changeSetId is null))
        {
            AddError(
                issues,
                "browser-presentation-source-inconsistent",
                "#/payload/layoutCheckpointId",
                "A presentation request names one layout checkpoint or one layout change set.");
        }
        if (!payload.TryGetProperty("queued", out var queuedValue) ||
            !IsBoolean(queuedValue))
        {
            return;
        }
        var queued = queuedValue.GetBoolean();
        var reason = ReadString(payload, "notQueuedReason");
        var hasWidget = HasNonnullProperty(payload, "widgetKind");
        var hasToken = HasNonnullProperty(payload, "localRootFrameToken");
        var hasFrameNumber = HasNonnullProperty(payload, "sourceFrameNumber");
        var hasMainFrame = HasNonnullProperty(payload, "isMainFrameWidget");
        var consistent = queued
            ? reason is null && hasWidget && hasToken && hasFrameNumber &&
                hasMainFrame
            : reason switch
            {
                "no-widget" => !hasWidget && !hasToken && !hasFrameNumber &&
                    !hasMainFrame,
                "not-compositing" => hasWidget && hasToken && !hasFrameNumber &&
                    hasMainFrame,
                _ => false
            };
        if (!consistent)
        {
            AddError(
                issues,
                "browser-presentation-request-inconsistent",
                "#/payload",
                "A queued request names its widget and source frame number " +
                    "with no reason; a request without a widget names none; " +
                    "a request on a widget that does not composite names the " +
                    "widget but no frame number.");
        }
    }

    private static void ValidateBrowserPresentationNotSwapped(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                .. PresentationBaseRules(widgetRequired: true),
                RequiredEnum(
                    "reason",
                    "swap-fails",
                    "commit-fails",
                    "commit-no-update",
                    "activation-fails"),
                RequiredEnum("action", "kept-active", "broken"),
                RequiredInteger("notSwappedIndex", nonnegative: true),
                RequiredInteger("notSwappedCount", positive: true),
                NullablePositiveDecimalText("timestampTicks"),
                NullablePositiveDecimalText("timestampTimeTicksMicroseconds")
            ],
            issues);
        ValidatePresentationBase(payload, issues);
        var reason = ReadString(payload, "reason");
        var action = ReadString(payload, "action");
        // The promise breaks on the reasons Chromium's own presentation-time
        // promise treats as failures and stays active on the others.
        var breaks = reason is "swap-fails" or "commit-no-update";
        if (reason is not null && action is not null &&
            (action == "broken") != breaks)
        {
            AddError(
                issues,
                "browser-presentation-not-swapped-action-inconsistent",
                "#/payload/action",
                $"A '{reason}' outcome cannot be '{action}'.");
        }
        var index = ReadNullableInteger(payload, "notSwappedIndex");
        var count = ReadNullableInteger(payload, "notSwappedCount");
        if (index is not null && count is not null && count != index + 1)
        {
            AddError(
                issues,
                "browser-presentation-not-swapped-count-inconsistent",
                "#/payload/notSwappedCount",
                "The count of not-swapped calls must be one more than the index.");
        }
        if (HasNonnullProperty(payload, "timestampTicks") &&
            !HasNonnullProperty(payload, "timestampTimeTicksMicroseconds"))
        {
            AddError(
                issues,
                "browser-presentation-ticks-without-time",
                "#/payload/timestampTicks",
                "A counter value is derived from Chromium's time, so it cannot " +
                    "appear without it.");
        }
    }

    // Page resource records (protocol 0.40).
    private static PropertyRule DigestRule(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.String &&
                IsSha256Digest(value.GetString()),
            "must be a SHA-256 digest in lowercase hexadecimal");

    private static PropertyRule FaceNumberRule() =>
        new(
            "faceNumber",
            true,
            false,
            value => value.ValueKind == JsonValueKind.String &&
                IsPositiveDecimal(value.GetString(), ulong.MaxValue),
            "must be a positive decimal integer string");

    private static bool IsSha256Digest(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateBrowserResourceBytes(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                DigestRule("digest"),
                RequiredDecimalText("size"),
                RequiredString("bytes")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        var bytes = ReadString(payload, "bytes");
        var size = ReadString(payload, "size");
        if (bytes is null || size is null ||
            !long.TryParse(size, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var count))
        {
            return;
        }
        // The size of what the base64 decodes to, from its length and padding,
        // without decoding what may be many megabytes.
        var padding = bytes.EndsWith("==", StringComparison.Ordinal) ? 2 :
            bytes.EndsWith('=') ? 1 : 0;
        if (bytes.Length % 4 != 0 || (long)bytes.Length / 4 * 3 - padding != count)
        {
            AddError(
                issues,
                "browser-resource-bytes-invalid",
                "#/payload/bytes",
                "Bytes must be base64 of exactly size bytes.");
        }
    }

    private static void ValidateBrowserFontFace(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                FaceNumberRule()
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
    }

    private static void ValidateBrowserFontFaceLoaded(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                FaceNumberRule(),
                RequiredString("family"),
                RequiredObject("descriptors"),
                NullableObject("source"),
                NullableObject("fontFile")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        if (payload.TryGetProperty("descriptors", out var descriptors) &&
            descriptors.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                descriptors,
                [
                    RequiredString("style"),
                    RequiredString("weight"),
                    RequiredString("stretch"),
                    RequiredString("unicodeRange"),
                    RequiredString("variant"),
                    RequiredString("featureSettings"),
                    RequiredString("display"),
                    RequiredString("ascentOverride"),
                    RequiredString("descentOverride"),
                    RequiredString("lineGapOverride"),
                    RequiredString("sizeAdjust")
                ],
                issues,
                "#/payload/descriptors");
        }
        if (payload.TryGetProperty("source", out var source) &&
            source.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                source,
                [
                    RequiredEnum("kind", "url", "data-url", "binary", "local"),
                    NullableText("url")
                ],
                issues,
                "#/payload/source");
            var kind = ReadString(source, "kind");
            if (kind is not null &&
                (kind == "url") != HasNonnullProperty(source, "url"))
            {
                AddError(
                    issues,
                    "browser-font-face-source-url",
                    "#/payload/source/url",
                    "A url source carries its URL, and no other source does.");
            }
        }
        if (payload.TryGetProperty("fontFile", out var fontFile) &&
            fontFile.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                fontFile,
                [
                    DigestRule("digest"),
                    RequiredInteger("index", nonnegative: true)
                ],
                issues,
                "#/payload/fontFile");
        }
    }

    private static void ValidateBrowserImageResource(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredText("url"),
                NullableString("responseUrl"),
                RequiredInteger("status", nonnegative: true),
                RequiredString("mimeType"),
                RequiredDecimalText("size"),
                DigestRule("digest"),
                RequiredBoolean("dataRecorded"),
                NullableDecimalText("imageId")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
    }

    // Protocol 0.51 (slice 4e).
    private static void ValidateBrowserStyleSheetResource(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("url"),
                NullableString("responseUrl"),
                RequiredInteger("status", nonnegative: true),
                RequiredText("mimeType"),
                RequiredDecimalText("size"),
                DigestRule("digest"),
                RequiredBoolean("textRecorded")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
    }

    private static PropertyRule NullableDigestRule(string name) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.Null ||
                (value.ValueKind == JsonValueKind.String && IsSha256Digest(value.GetString())),
            "must be null or a SHA-256 digest in lowercase hexadecimal");

    private static readonly string[] StyleSheetKinds =
        ["link", "style", "import", "constructed", "processing-instruction", "other"];

    private static void ValidateBrowserStyleSheetsUpdated(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredObjectArray("scopes")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        if (!payload.TryGetProperty("scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        var scopeIndex = 0;
        foreach (var scope in scopes.EnumerateArray())
        {
            var scopePointer = $"#/payload/scopes/{scopeIndex}";
            scopeIndex++;
            if (scope.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            ValidateShape(
                scope,
                [
                    RequiredInteger("scopeNodeId", positive: true),
                    RequiredObjectArray("sheets"),
                    RequiredObjectArray("adopted")
                ],
                issues,
                scopePointer);
            foreach (var list in new[] { "sheets", "adopted" })
            {
                if (!scope.TryGetProperty(list, out var entries) || entries.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                var entryIndex = 0;
                foreach (var entry in entries.EnumerateArray())
                {
                    var pointer = $"{scopePointer}/{list}/{entryIndex}";
                    entryIndex++;
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    if (!entry.TryGetProperty("kind", out _))
                    {
                        // A sheet unchanged since the document's last record.
                        ValidateShape(entry, [RequiredDecimalText("sheet")], issues, pointer);
                        continue;
                    }
                    ValidateShape(
                        entry,
                        [
                            RequiredDecimalText("sheet"),
                            RequiredEnum("kind", StyleSheetKinds),
                            NullableInteger("ownerNodeId", positive: true),
                            NullableDecimalText("parentSheet"),
                            NullableInteger("ruleIndex", nonnegative: true),
                            NullableString("href"),
                            RequiredText("media"),
                            RequiredText("title"),
                            RequiredBoolean("disabled"),
                            RequiredBoolean("active"),
                            RequiredEnum("textSource", "arrived", "element", "cssom", "none"),
                            NullableDigestRule("textDigest")
                        ],
                        issues,
                        pointer);
                    var kind = ReadString(entry, "kind");
                    var hasParent = HasNonnullProperty(entry, "parentSheet");
                    if ((kind == "import") != hasParent || hasParent != HasNonnullProperty(entry, "ruleIndex"))
                    {
                        AddError(
                            issues,
                            "browser-style-sheet-import-parent",
                            pointer + "/parentSheet",
                            "An import names its parent sheet and rule index, and no other sheet does.");
                    }
                    var source = ReadString(entry, "textSource");
                    if ((source is "arrived" or "cssom") != HasNonnullProperty(entry, "textDigest"))
                    {
                        AddError(
                            issues,
                            "browser-style-sheet-text-digest",
                            pointer + "/textDigest",
                            "Arrived and CSSOM text is named by its digest, and no other text source is.");
                    }
                }
            }
        }
    }

    private static void ValidateBrowserImagePaintImage(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredDecimalText("imageId"),
                RequiredDecimalText("paintImageId"),
                RequiredEnum("sequence", "shared", "own"),
                NullableInteger("nodeId", positive: true),
                NullableDecimalText("syncTargetPaintImageId")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        if (ReadString(payload, "sequence") == "own" &&
            payload.TryGetProperty("nodeId", out var nodeId) &&
            nodeId.ValueKind == JsonValueKind.Null)
        {
            AddError(
                issues,
                "browser-image-paint-image-node-missing",
                "#/payload/nodeId",
                "A paint image with its own animation sequence is made for a node.");
        }
    }

    private static void ValidateBrowserPresentationSwapped(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                .. PresentationBaseRules(widgetRequired: true),
                RequiredFrameToken("frameToken"),
                RequiredInteger("notSwappedCount", nonnegative: true)
            ],
            issues);
        ValidatePresentationBase(payload, issues);
    }

    // Compositor records (protocol 0.48, slice 4b).
    private static readonly string[] CompositorTargetProperties =
    [
        "transform", "scale", "rotate", "translate", "opacity", "filter",
        "scroll-offset", "background-color", "bounds", "css-custom-property",
        "native-property", "backdrop-filter", "other"
    ];

    private static readonly string[] CompositorElementIdNamespaces =
    [
        "primary-effect", "primary-transform", "effect-filter", "scale-transform",
        "rotate-transform", "translate-transform", "scroll", "primary", "other", "none"
    ];

    // Protocol 0.50: whether the compositor scrolls the node, and the reasons
    // Chromium gives for repainting it on the main thread, each named once.
    private static readonly string[] ScrollRepaintReasons =
    [
        "has-background-attachment-fixed-objects", "not-opaque-for-text-and-lcd-text",
        "prefer-non-composited-scrolling", "background-needs-repaint-on-scroll",
    ];

    private static bool IsScrollCompositing(JsonElement value) =>
        value.EnumerateObject().Count() == 4 &&
        value.TryGetProperty("isComposited", out var composited) &&
        (composited.ValueKind is JsonValueKind.True or JsonValueKind.False) &&
        value.TryGetProperty("mainThreadRepaintReasons", out var reasons) &&
        reasons.ValueKind == JsonValueKind.Array &&
        reasons.EnumerateArray().All(reason =>
            reason.ValueKind == JsonValueKind.String && ScrollRepaintReasons.Contains(reason.GetString(), StringComparer.Ordinal)) &&
        reasons.EnumerateArray().Select(reason => reason.GetString()).Distinct(StringComparer.Ordinal).Count() == reasons.GetArrayLength();

    private static readonly string[] CompositorFilterTypes =
    [
        "grayscale", "sepia", "saturate", "hue-rotate", "invert", "brightness",
        "contrast", "opacity", "blur", "drop-shadow", "color-matrix", "zoom",
        "reference", "saturating-brightness", "alpha-threshold", "offset", "unknown"
    ];

    private static void ValidateCompositorRendererContext(
        JsonElement payload,
        bool requiresDocument,
        ICollection<EventValidationIssue> issues)
    {
        ValidateBrowserContextProperty(payload, issues);
        if (requiresDocument)
        {
            ValidateRendererDocumentContext(payload, issues);
            return;
        }
        if (payload.TryGetProperty("context", out var context) &&
            context.ValueKind == JsonValueKind.Object &&
            ReadString(context, "processType") != "renderer")
        {
            AddError(
                issues,
                "browser-compositor-context-invalid",
                "#/payload/context",
                "Compositor evidence must have renderer-process provenance.");
        }
    }

    private static void ValidateCompositorWidget(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("widget", out var widget) ||
            widget.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        ValidateShape(
            widget,
            [
                RequiredEnum("widgetKind", "frame", "page-popup"),
                NullableFrameSinkId("frameSinkId"),
                RequiredString("localRootFrameToken")
            ],
            issues,
            "#/payload/widget");
    }

    private static readonly string[] AnimationKinds = ["css-animation", "css-transition", "web-animation"];
    private static readonly string[] AnimationPlayStates = ["idle", "pending", "running", "paused", "finished"];
    private static readonly string[] AnimationTimelineKinds = ["document", "scroll", "view", "other", "none"];
    private static readonly string[] AnimationDirections = ["normal", "reverse", "alternate", "alternate-reverse"];
    private static readonly string[] AnimationFills = ["none", "forwards", "backwards", "both", "auto"];

    // Protocol 0.53 (slice 4g): a Blink animation.
    private static void ValidateBrowserAnimationUpdated(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredDecimalText("sequenceNumber"),
                RequiredEnum("kind", AnimationKinds),
                NullableString("name"),
                NullableString("id"),
                NullableInteger("targetNodeId", positive: true),
                NullableString("pseudoElement"),
                RequiredEnum("playState", AnimationPlayStates),
                RequiredBoolean("pending"),
                RequiredNullableNumber("playbackRate"),
                RequiredNullableNumber("startTimeMilliseconds"),
                RequiredNullableNumber("currentTimeMilliseconds"),
                RequiredObject("timeline"),
                NullableObject("effect"),
                NullableInteger("compositorAnimationId", positive: true)
            ],
            issues);
        ValidateCompositorRendererContext(payload, true, issues);
        if (payload.TryGetProperty("timeline", out var timeline) && timeline.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                timeline,
                [
                    RequiredEnum("kind", AnimationTimelineKinds),
                    NullablePositiveDecimalText("zeroTicks"),
                    NullablePositiveDecimalText("zeroTimeTicksMicroseconds"),
                    RequiredNullableNumber("playbackRate"),
                    NullableInteger("sourceNodeId", positive: true),
                    NullableInteger("subjectNodeId", positive: true),
                    NullableEnum("axis", "horizontal", "vertical")
                ],
                issues,
                "#/payload/timeline");
        }
        if (payload.TryGetProperty("effect", out var effect) && effect.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                effect,
                [
                    RequiredNullableNumber("delayMilliseconds"),
                    RequiredNullableNumber("endDelayMilliseconds"),
                    RequiredNullableNumber("iterationStart", nonnegative: true),
                    RequiredNullableNumber("iterations", nonnegative: true),
                    RequiredNullableNumber("durationMilliseconds", nonnegative: true),
                    RequiredEnum("direction", AnimationDirections),
                    RequiredEnum("fill", AnimationFills),
                    RequiredString("easing"),
                    RequiredNullableNumber("progress"),
                    RequiredNullableNumber("currentIteration")
                ],
                issues,
                "#/payload/effect");
        }
    }

    private static void ValidateBrowserCompositorAnimationStarted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredInteger("nodeId", positive: true),
                NullableInteger("compositorAnimationId", positive: true),
                RequiredObjectArray("keyframeModels")
            ],
            issues);
        ValidateCompositorRendererContext(payload, true, issues);
        if (!payload.TryGetProperty("keyframeModels", out var models) ||
            models.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        if (models.GetArrayLength() == 0)
        {
            AddError(
                issues,
                "browser-compositor-keyframe-models-empty",
                "#/payload/keyframeModels",
                "An animation started on the compositor has at least one keyframe model.");
        }
        var index = 0;
        foreach (var model in models.EnumerateArray())
        {
            if (model.ValueKind == JsonValueKind.Object)
            {
                ValidateShape(
                    model,
                    [
                        RequiredInteger("keyframeModelId", positive: true),
                        RequiredEnum("targetProperty", CompositorTargetProperties),
                        RequiredDecimalText("elementId"),
                        RequiredEnum("elementIdNamespace", CompositorElementIdNamespaces)
                    ],
                    issues,
                    $"#/payload/keyframeModels/{index}");
            }
            index++;
        }
    }

    private static void ValidateBrowserCompositorAnimationEnded(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                NullableInteger("nodeId", positive: true),
                NullableInteger("compositorAnimationId", positive: true),
                RequiredIntegerArray("keyframeModelIds")
            ],
            issues);
        ValidateCompositorRendererContext(payload, false, issues);
        if (payload.TryGetProperty("keyframeModelIds", out var ids) &&
            ids.ValueKind == JsonValueKind.Array &&
            (ids.GetArrayLength() == 0 ||
             ids.EnumerateArray().Any(id => !IsInteger(id) || id.GetInt64() <= 0)))
        {
            AddError(
                issues,
                "browser-compositor-keyframe-model-ids-invalid",
                "#/payload/keyframeModelIds",
                "An ended animation names at least one positive keyframe model ID.");
        }
    }

    private static PropertyRule RequiredIntegerArray(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(IsInteger),
            "must be an array of integers");

    private static bool IsFiniteNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) &&
        double.IsFinite(number);

    private static bool IsFiniteNumberArray(JsonElement value, int? length) =>
        value.ValueKind == JsonValueKind.Array &&
        (length is null || value.GetArrayLength() == length) &&
        value.EnumerateArray().All(IsFiniteNumber);

    private static bool IsCompositorValue(string? property, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return property is "transform" or "opacity" or "filter" or "backdrop-filter" or
                "background-color-progress" or "clip-path-progress" or "image-frame";
        }
        switch (property)
        {
            case "transform":
                return IsFiniteNumberArray(value, 16);
            case "opacity":
                return IsFiniteNumber(value);
            case "image-frame":
                return IsInteger(value) && value.GetInt64() >= 0;
            case "scroll-offset":
                return value.ValueKind == JsonValueKind.Object &&
                    value.TryGetProperty("x", out var x) && IsFiniteNumber(x) &&
                    value.TryGetProperty("y", out var y) && IsFiniteNumber(y) &&
                    (value.EnumerateObject().Count() == 2 || IsScrollCompositing(value));
            case "background-color-progress":
            case "clip-path-progress":
                return value.ValueKind == JsonValueKind.Object &&
                    value.EnumerateObject().Count() == 1 &&
                    value.TryGetProperty("progress", out var progress) &&
                    (progress.ValueKind == JsonValueKind.Null || IsFiniteNumber(progress));
            case "filter":
            case "backdrop-filter":
                return value.ValueKind == JsonValueKind.Array &&
                    value.EnumerateArray().All(operation =>
                        operation.ValueKind == JsonValueKind.Object &&
                        operation.EnumerateObject().Count() == 2 &&
                        operation.TryGetProperty("type", out var type) &&
                        type.ValueKind == JsonValueKind.String &&
                        CompositorFilterTypes.Contains(type.GetString(), StringComparer.Ordinal) &&
                        operation.TryGetProperty("numbers", out var numbers) &&
                        IsFiniteNumberArray(numbers, null));
            default:
                return false;
        }
    }

    private static void ValidateBrowserCompositorFrame(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredInteger("layerTreeHostId", positive: true),
                NullableObject("widget"),
                RequiredFrameToken("frameToken"),
                RequiredInteger("sourceFrameNumber"),
                NullablePositiveDecimalText("beginFrameTicks"),
                NullablePositiveDecimalText("beginFrameTimeTicksMicroseconds"),
                RequiredBoolean("highResolutionTicks"),
                RequiredObjectArray("changes")
            ],
            issues);
        ValidateCompositorRendererContext(payload, false, issues);
        ValidateCompositorWidget(payload, issues);
        if (!payload.TryGetProperty("changes", out var changes) ||
            changes.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        if (changes.GetArrayLength() == 0)
        {
            AddError(
                issues,
                "browser-compositor-frame-empty",
                "#/payload/changes",
                "A compositor frame is recorded only when a drawn value changed.");
        }
        var index = 0;
        foreach (var change in changes.EnumerateArray())
        {
            if (change.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }
            // An animated image's frame names its paint image; every other
            // change names its compositor element.
            var imageFrame = ReadString(change, "property") == "image-frame";
            ValidateShape(
                change,
                [
                    RequiredDecimalText(imageFrame ? "paintImageId" : "elementId"),
                    RequiredEnum("property", "transform", "opacity", "filter",
                        "backdrop-filter", "scroll-offset", "background-color-progress",
                        "clip-path-progress", "image-frame"),
                    new PropertyRule("value", true, true, _ => true, "must be present")
                ],
                issues,
                $"#/payload/changes/{index}");
            if (change.TryGetProperty("value", out var value) &&
                !IsCompositorValue(ReadString(change, "property"), value))
            {
                AddError(
                    issues,
                    "browser-compositor-value-invalid",
                    $"#/payload/changes/{index}/value",
                    "A compositor value must have its property's shape: 16 matrix " +
                        "entries, a number, filter operations, x and y, a progress, " +
                        "or a frame index.");
            }
            index++;
        }
    }

    private static readonly string[] PaintWorkletPathVerbs =
        ["move", "line", "quad", "conic", "cubic", "close"];

    // Points each Skia path verb takes, as SkPath stores them.
    private static int PathVerbPoints(string? verb) => verb switch
    {
        "move" or "line" => 1,
        "quad" or "conic" => 2,
        "cubic" => 3,
        _ => 0
    };

    private static void ValidateBrowserPaintWorkletPainted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredDecimalText("elementId"),
                RequiredEnum("property", "background-color", "clip-path"),
                new PropertyRule(
                    "progress",
                    true,
                    true,
                    value => value.ValueKind == JsonValueKind.Null || IsFiniteNumber(value),
                    "must be a finite number or null"),
                RequiredObject("value")
            ],
            issues);
        ValidateCompositorRendererContext(payload, false, issues);
        if (!payload.TryGetProperty("value", out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        var valid = ReadString(payload, "property") switch
        {
            "background-color" =>
                value.EnumerateObject().Count() == 1 &&
                value.TryGetProperty("color", out var color) &&
                IsFiniteNumberArray(color, 4),
            "clip-path" => IsPaintedClipPath(value),
            _ => true
        };
        if (!valid)
        {
            AddError(
                issues,
                "browser-paint-worklet-value-invalid",
                "#/payload/value",
                "A painted background color is four floats; a painted clip path is " +
                    "its fill type, verbs, points, conic weights, translation, and " +
                    "whether it was drawn as a rounded rectangle.");
        }
    }

    private static bool IsPaintedClipPath(JsonElement value)
    {
        if (value.EnumerateObject().Count() != 6 ||
            !value.TryGetProperty("fillType", out var fillType) ||
            fillType.ValueKind != JsonValueKind.String ||
            fillType.GetString() is not ("winding" or "even-odd" or "inverse-winding" or
                "inverse-even-odd") ||
            !value.TryGetProperty("verbs", out var verbs) ||
            verbs.ValueKind != JsonValueKind.Array ||
            !value.TryGetProperty("points", out var points) ||
            !IsFiniteNumberArray(points, null) ||
            !value.TryGetProperty("conicWeights", out var weights) ||
            !IsFiniteNumberArray(weights, null) ||
            !value.TryGetProperty("translation", out var translation) ||
            translation.ValueKind != JsonValueKind.Object ||
            translation.EnumerateObject().Count() != 2 ||
            !translation.TryGetProperty("x", out var x) || !IsFiniteNumber(x) ||
            !translation.TryGetProperty("y", out var y) || !IsFiniteNumber(y) ||
            !value.TryGetProperty("drawnAsRoundedRect", out var rounded) ||
            rounded.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }
        var pointCount = 0;
        var conicCount = 0;
        foreach (var verb in verbs.EnumerateArray())
        {
            if (verb.ValueKind != JsonValueKind.String ||
                !PaintWorkletPathVerbs.Contains(verb.GetString(), StringComparer.Ordinal))
            {
                return false;
            }
            pointCount += PathVerbPoints(verb.GetString());
            conicCount += verb.GetString() == "conic" ? 1 : 0;
        }
        return points.GetArrayLength() == pointCount * 2 &&
            weights.GetArrayLength() == conicCount;
    }

    private static void ValidateBrowserCompositorFramePresented(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredInteger("layerTreeHostId", positive: true),
                NullableObject("widget"),
                RequiredFrameToken("frameToken"),
                RequiredBoolean("failed"),
                NullablePositiveDecimalText("presentedTicks"),
                NullablePositiveDecimalText("presentedTimeTicksMicroseconds"),
                RequiredBoolean("highResolutionTicks")
            ],
            issues);
        ValidateCompositorRendererContext(payload, false, issues);
        ValidateCompositorWidget(payload, issues);
        if (payload.TryGetProperty("failed", out var failed) &&
            failed.ValueKind == JsonValueKind.True &&
            (ReadString(payload, "presentedTicks") is not null ||
             ReadString(payload, "presentedTimeTicksMicroseconds") is not null))
        {
            AddError(
                issues,
                "browser-compositor-presentation-invalid",
                "#/payload",
                "A failed presentation has no presentation time.");
        }
    }

    private static void ValidateBrowserPresentationFeedback(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                .. PresentationBaseRules(widgetRequired: true),
                RequiredFrameToken("frameToken"),
                NullablePositiveDecimalText("presentedTicks"),
                NullablePositiveDecimalText("presentedTimeTicksMicroseconds"),
                RequiredDecimalText("intervalMicroseconds"),
                RequiredStringArray("flags"),
                NullablePositiveDecimalText("receivedCompositorFrameTicks"),
                NullablePositiveDecimalText("drawStartTicks"),
                NullablePositiveDecimalText("swapStartTicks"),
                NullablePositiveDecimalText("swapEndTicks"),
                RequiredBoolean("highResolutionTicks"),
                RequiredInteger("notSwappedCount", nonnegative: true)
            ],
            issues);
        ValidatePresentationBase(payload, issues);
        if (payload.TryGetProperty("flags", out var flags) &&
            flags.ValueKind == JsonValueKind.Array)
        {
            var names = flags.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : null)
                .ToList();
            if (names.Any(name =>
                    name is null ||
                    !PresentationFeedbackFlagNames.Contains(
                        name, StringComparer.Ordinal)) ||
                names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            {
                AddError(
                    issues,
                    "browser-presentation-feedback-flags-invalid",
                    "#/payload/flags",
                    "Feedback flags must be distinct names of " +
                        "gfx::PresentationFeedback flags.");
            }
        }
        if (payload.TryGetProperty("highResolutionTicks", out var highResolution) &&
            highResolution.ValueKind == JsonValueKind.False &&
            PresentationTickProperties.Any(name => HasNonnullProperty(payload, name)))
        {
            AddError(
                issues,
                "browser-presentation-ticks-without-high-resolution",
                "#/payload",
                "Counter values are derived only from a high-resolution clock.");
        }
        if (HasNonnullProperty(payload, "presentedTicks") &&
            !HasNonnullProperty(payload, "presentedTimeTicksMicroseconds"))
        {
            AddError(
                issues,
                "browser-presentation-ticks-without-time",
                "#/payload/presentedTicks",
                "A counter value is derived from Chromium's time, so it cannot " +
                    "appear without it.");
        }
    }

    private static long? ReadNullableInteger(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : null;

    private static void ValidateAllOrNone(
        JsonElement payload,
        IReadOnlyList<string> properties,
        bool present,
        string code,
        string message,
        ICollection<EventValidationIssue> issues)
    {
        var consistent = properties.All(property =>
            (payload.TryGetProperty(property, out var value) &&
                value.ValueKind != JsonValueKind.Null) == present);
        if (!consistent)
        {
            AddError(
                issues,
                code,
                $"#/payload/{properties[0]}",
                message);
        }
    }

    private static void ValidateOrderedRange(
        JsonElement payload,
        string startProperty,
        string endProperty,
        ICollection<EventValidationIssue> issues)
    {
        var start = ReadNullableInteger(payload, startProperty);
        var end = ReadNullableInteger(payload, endProperty);
        if (start is not null && end is not null && end < start)
        {
            AddError(
                issues,
                "browser-selection-range-reversed",
                $"#/payload/{endProperty}",
                $"Property '{endProperty}' ({end}) precedes " +
                    $"'{startProperty}' ({start}).");
        }
    }

    private static void ValidateBrowserDocumentCookieRead(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("accessId"),
                NullableString("cookieUrl"),
                RequiredEnum(
                    "outcome",
                    "returned",
                    "not-attempted-no-cookie-url",
                    "cookie-manager-call-failed",
                    "refused-no-window-or-cookies-disabled",
                    "refused-security-error"),
                NullableEnum("servedFrom", "cookie-manager", "renderer-cache"),
                RequiredInteger("cookieCount", nonnegative: true),
                RequiredTextArray("cookieNames"),
                RequiredBoolean("cookieNamesTruncated"),
                NullableObject("location"),
                NullableObject("world")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserLocationProperty(payload, issues);
        ValidateBrowserExecutionWorldProperty(payload, issues);
        ValidateCookieNameCount(
            payload, "cookieNames", "cookieNamesTruncated", issues);
    }

    private static void ValidateBrowserDocumentCookieWrite(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("accessId"),
                NullableString("cookieUrl"),
                RequiredEnum(
                    "outcome",
                    "sent-to-cookie-manager",
                    "not-attempted-no-cookie-url",
                    "refused-no-window-or-cookies-disabled",
                    "refused-security-error"),
                RequiredText("name"),
                RequiredObject("attributes"),
                NullableObject("location"),
                NullableObject("world")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserLocationProperty(payload, issues);
        ValidateBrowserExecutionWorldProperty(payload, issues);
        ValidateCookieWriteAttributes(payload, documentCookie: true, issues);
    }

    private static void ValidateBrowserCookieStoreRequest(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("requestId"),
                RequiredEnum("method", CookieStoreMethods),
                RequiredEnum("contextKind", CookieContextKinds),
                RequiredEnum("outcome", "sent-to-cookie-manager", "threw"),
                NullableText("name"),
                NullableString("url"),
                NullableObject("attributes"),
                NullableObject("location"),
                NullableObject("world")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateBrowserLocationProperty(payload, issues);
        ValidateBrowserExecutionWorldProperty(payload, issues);

        var write = payload.TryGetProperty("method", out var method) &&
            method.ValueKind == JsonValueKind.String &&
            method.GetString() is "set" or "delete";
        var hasAttributes = payload.TryGetProperty("attributes", out var attributes) &&
            attributes.ValueKind == JsonValueKind.Object;
        if (write != hasAttributes)
        {
            AddError(
                issues,
                "browser-cookie-store-attributes",
                "#/payload/attributes",
                "A Cookie Store write reports its attributes and a read reports null attributes.");
        }

        if (hasAttributes)
        {
            ValidateCookieWriteAttributes(payload, documentCookie: false, issues);
        }
    }

    private static void ValidateBrowserCookieStoreResult(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("requestId"),
                RequiredEnum("method", CookieStoreMethods),
                RequiredEnum("outcome", "resolved", "rejected", "context-destroyed"),
                NullableBoolean("success"),
                NullableInteger("cookieCount", nonnegative: true),
                NullableTextArray("cookieNames"),
                NullableBoolean("cookieNamesTruncated")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);

        var read = payload.TryGetProperty("method", out var method) &&
            method.ValueKind == JsonValueKind.String &&
            method.GetString() is "get" or "getAll";
        var hasNames = HasNonnullProperty(payload, "cookieNames") &&
            HasNonnullProperty(payload, "cookieCount") &&
            HasNonnullProperty(payload, "cookieNamesTruncated");
        var hasSuccess = HasNonnullProperty(payload, "success");
        if (read ? !hasNames || hasSuccess : hasNames || !hasSuccess)
        {
            AddError(
                issues,
                "browser-cookie-store-result-shape",
                "#/payload",
                "A Cookie Store read result reports cookie names and a write result reports success.");
        }

        if (hasNames)
        {
            ValidateCookieNameCount(
                payload, "cookieNames", "cookieNamesTruncated", issues);
        }
    }

    private static void ValidateBrowserCookieStoreChange(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredEnum("contextKind", CookieContextKinds),
                RequiredText("name"),
                RequiredText("domain"),
                RequiredText("path"),
                RequiredString("cause"),
                RequiredBoolean("dispatched")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
    }

    private static void ValidateBrowserCookieAccess(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredEnum("observer", "frame", "navigation"),
                NullableString("navigationId"),
                NullableInteger("rendererProcessId", nonnegative: true),
                RequiredEnum("accessType", "read", "change"),
                RequiredString("url"),
                NullableString("frameOrigin"),
                NullableString("topFrameOrigin"),
                NullableString("requestId"),
                RequiredBoolean("adTagged"),
                RequiredInteger("cookieCount", nonnegative: true),
                RequiredObjectArray("cookies"),
                RequiredBoolean("cookiesTruncated")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);

        var navigationObserver = payload.TryGetProperty("observer", out var observer) &&
            observer.ValueKind == JsonValueKind.String &&
            observer.GetString() == "navigation";
        if (navigationObserver != HasNonnullProperty(payload, "navigationId"))
        {
            AddError(
                issues,
                "browser-cookie-access-observer",
                "#/payload/navigationId",
                "A navigation-observed cookie access names its navigation and a frame-observed one does not.");
        }

        if (!payload.TryGetProperty("cookies", out var cookies) ||
            cookies.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var cookie in cookies.EnumerateArray())
        {
            if (cookie.ValueKind == JsonValueKind.Object)
            {
                ValidateCookieAccessEntry(cookie, index, issues);
            }

            index++;
        }

        ValidateCookieNameCount(payload, "cookies", "cookiesTruncated", issues);
    }

    private static void ValidateCookieAccessEntry(
        JsonElement cookie,
        int index,
        ICollection<EventValidationIssue> issues)
    {
        var path = $"#/payload/cookies/{index}";
        ValidateShape(
            cookie,
            [
                RequiredText("name"),
                RequiredBoolean("parsed"),
                NullableText("domain"),
                NullableText("path"),
                NullableString("sameSite"),
                NullableBoolean("secure"),
                NullableBoolean("httpOnly"),
                NullableBoolean("hostOnly"),
                NullableBoolean("partitioned"),
                NullableBoolean("persistent"),
                NullableBoolean("expired"),
                RequiredBoolean("included"),
                RequiredStringArray("exclusionReasons"),
                RequiredStringArray("warningReasons"),
                NullableString("exemptionReason")
            ],
            issues,
            path);

        var parsed = cookie.TryGetProperty("parsed", out var parsedValue) &&
            parsedValue.ValueKind == JsonValueKind.True;
        string[] attributes =
        [
            "domain", "path", "sameSite", "secure", "httpOnly", "hostOnly",
            "partitioned", "persistent", "expired"
        ];
        foreach (var attribute in attributes)
        {
            if (parsed != HasNonnullProperty(cookie, attribute))
            {
                AddError(
                    issues,
                    "browser-cookie-access-entry-shape",
                    $"{path}/{attribute}",
                    "A parsed cookie reports every attribute and an unparsed Set-Cookie line reports none.");
            }
        }
    }

    private static void ValidateCookieWriteAttributes(
        JsonElement payload,
        bool documentCookie,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("attributes", out var attributes) ||
            attributes.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        List<PropertyRule> rules =
        [
            NullableString("domain"),
            NullableString("path"),
            NullableString("sameSite"),
            RequiredBoolean("partitioned"),
            RequiredBoolean("expiresPresent")
        ];
        if (documentCookie)
        {
            rules.Add(RequiredBoolean("secure"));
            rules.Add(RequiredBoolean("httpOnly"));
            rules.Add(RequiredBoolean("maxAgePresent"));
            rules.Add(RequiredStringArray("attributeNames"));
        }

        ValidateShape(
            attributes,
            rules,
            issues,
            "#/payload/attributes");
    }

    // A cookie list reports the full count and holds every entry unless it
    // says it was cut, so a reader can tell a short list from a cut one.
    private static void ValidateCookieNameCount(
        JsonElement payload,
        string listProperty,
        string truncatedProperty,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty(listProperty, out var list) ||
            list.ValueKind != JsonValueKind.Array ||
            !payload.TryGetProperty("cookieCount", out var countValue) ||
            !countValue.TryGetInt64(out var count) ||
            !payload.TryGetProperty(truncatedProperty, out var truncatedValue) ||
            truncatedValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return;
        }

        var length = list.GetArrayLength();
        var truncated = truncatedValue.ValueKind == JsonValueKind.True;
        if (truncated ? length >= count : length != count)
        {
            AddError(
                issues,
                "browser-cookie-count",
                $"#/payload/{listProperty}",
                "cookieCount must equal the listed cookies unless the list is marked truncated, in which case it must exceed them.");
        }
    }

    private static void ValidateBrowserNavigation(
        JsonElement payload,
        ICollection<EventValidationIssue> issues,
        bool completed)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                NullableString("parentFrameId"),
                NullableString("parentOrOuterDocumentFrameId"),
                RequiredEnum(
                    "frameType",
                    "subframe",
                    "primary-main-frame",
                    "prerender-main-frame",
                    "fenced-frame-root",
                    "guest-main-frame"),
                RequiredBoolean("primaryPage"),
                RequiredString("navigationId"),
                RequiredString("url"),
                RequiredEnum(
                    "navigationKind",
                    "cross-document",
                    "same-document"),
                RequiredBoolean("rendererInitiated"),
                RequiredBoolean("sameDocument"),
                completed
                    ? RequiredBoolean("committed")
                    : NullableBoolean("committed"),
                completed
                    ? RequiredBoolean("errorPage")
                    : NullableBoolean("errorPage"),
                completed
                    ? RequiredInteger("netErrorCode")
                    : NullableInteger("netErrorCode"),
                completed
                    ? RequiredEnum(
                        "outcome",
                        "committed",
                        "committed-error-page",
                        "not-committed")
                    : NullableString("outcome"),
                NullableInteger("rendererProcessId")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);

        var frameType = ReadString(payload, "frameType");
        var parentFrameId = ReadString(payload, "parentFrameId");
        var parentOrOuterDocumentFrameId =
            ReadString(payload, "parentOrOuterDocumentFrameId");
        var primaryPage = payload.TryGetProperty(
            "primaryPage",
            out var primaryPageValue) &&
            primaryPageValue.ValueKind == JsonValueKind.True;
        if (frameType == "subframe" &&
            (parentFrameId is null ||
             parentOrOuterDocumentFrameId != parentFrameId))
        {
            AddError(
                issues,
                "browser-navigation-subframe-parent-mismatch",
                "#/payload/parentFrameId",
                "A subframe must identify the same direct parent and owning document frame.");
        }

        if (frameType != "subframe" && parentFrameId is not null)
        {
            AddError(
                issues,
                "browser-navigation-main-frame-parent-present",
                "#/payload/parentFrameId",
                "A main frame must not identify a direct parent frame.");
        }

        if (frameType == "primary-main-frame" && !primaryPage)
        {
            AddError(
                issues,
                "browser-navigation-primary-page-mismatch",
                "#/payload/primaryPage",
                "A primary main frame must belong to the primary page.");
        }

        if (payload.TryGetProperty("context", out var context) &&
            context.ValueKind == JsonValueKind.Object)
        {
            var pageId = ReadString(context, "pageId");
            var frameId = ReadString(context, "frameId");
            var documentId = ReadString(context, "documentId");
            var documentToken = ReadString(context, "documentToken");
            if (ReadString(context, "processType") != "browser" ||
                pageId is null ||
                frameId is null)
            {
                AddError(
                    issues,
                    "browser-navigation-context-invalid",
                    "#/payload/context",
                    "Navigation evidence must have browser-process provenance and page and frame identities.");
            }

            if (frameType == "subframe" && pageId == frameId)
            {
                AddError(
                    issues,
                    "browser-navigation-subframe-page-mismatch",
                    "#/payload/context/pageId",
                    "A subframe must have distinct page and frame identities.");
            }

            if (frameType != "subframe" && pageId != frameId)
            {
                AddError(
                    issues,
                    "browser-navigation-main-frame-page-mismatch",
                    "#/payload/context/pageId",
                    "A main frame must identify the root of its own page.");
            }

            var committed = payload.TryGetProperty(
                "committed",
                out var contextCommittedValue) &&
                contextCommittedValue.ValueKind == JsonValueKind.True;
            if ((!completed || !committed) &&
                (documentId is not null || documentToken is not null))
            {
                AddError(
                    issues,
                    "browser-navigation-document-before-commit",
                    "#/payload/context/documentId",
                    "Document identity and token must be null before commit and after an uncommitted completion.");
            }

            if (completed && committed &&
                (documentId is null || documentToken is null))
            {
                AddError(
                    issues,
                    "browser-navigation-committed-document-missing",
                    "#/payload/context/documentId",
                    "A committed navigation must identify its resulting document and document token.");
            }

            var rendererProcessId = payload.TryGetProperty(
                "rendererProcessId",
                out var rendererProcessIdValue) &&
                rendererProcessIdValue.ValueKind == JsonValueKind.Number &&
                rendererProcessIdValue.TryGetInt32(out var processId)
                    ? processId
                    : (int?)null;
            if ((!completed || !committed) && rendererProcessId is not null)
            {
                AddError(
                    issues,
                    "browser-navigation-renderer-before-commit",
                    "#/payload/rendererProcessId",
                    "Renderer process identity must be null before commit and after an uncommitted completion.");
            }

            if (completed && committed &&
                (rendererProcessId is null || rendererProcessId <= 0))
            {
                AddError(
                    issues,
                    "browser-navigation-committed-renderer-missing",
                    "#/payload/rendererProcessId",
                    "A committed navigation must identify the renderer process hosting its document.");
            }
        }

        var sameDocument = payload.TryGetProperty(
            "sameDocument",
            out var sameDocumentValue) &&
            sameDocumentValue.ValueKind == JsonValueKind.True;
        var navigationKind = ReadString(payload, "navigationKind");
        if ((sameDocument && navigationKind != "same-document") ||
            (!sameDocument && navigationKind != "cross-document"))
        {
            AddError(
                issues,
                "browser-navigation-kind-mismatch",
                "#/payload/navigationKind",
                "navigationKind must agree with sameDocument.");
        }

        if (completed &&
            payload.TryGetProperty("committed", out var committedValue) &&
            committedValue.ValueKind == JsonValueKind.False &&
            ReadString(payload, "outcome") != "not-committed")
        {
            AddError(
                issues,
                "browser-navigation-outcome-mismatch",
                "#/payload/outcome",
                "An uncommitted navigation must have outcome not-committed.");
        }

        if (completed &&
            payload.TryGetProperty("committed", out committedValue) &&
            committedValue.ValueKind == JsonValueKind.True)
        {
            var errorPage = payload.TryGetProperty(
                "errorPage",
                out var errorPageValue) &&
                errorPageValue.ValueKind == JsonValueKind.True;
            var expectedOutcome = errorPage
                ? "committed-error-page"
                : "committed";
            if (ReadString(payload, "outcome") != expectedOutcome)
            {
                AddError(
                    issues,
                    "browser-navigation-outcome-mismatch",
                    "#/payload/outcome",
                    $"A committed navigation must have outcome {expectedOutcome}.");
            }
        }
    }

    // Protocol 0.55 (slice 5a): the frame a walked frame owner element held.
    private static void ValidateBrowserDomCheckpointFrameOwner(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredInteger("ownerNodeId", positive: true),
                FrameTokenRule("frameToken", required: true, nullable: false),
                RequiredEnum("frameLocation", "local", "remote")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
    }

    // Protocol 0.55 (slice 5a): a frame owner element given a frame or losing
    // it. A lost frame has neither a token nor a location.
    private static void ValidateBrowserDomFrameOwnerChanged(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredInteger("ownerNodeId", positive: true),
                FrameTokenRule("frameToken", required: true, nullable: true),
                NullableEnum("frameLocation", "local", "remote")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        if (HasNonnullProperty(payload, "frameToken") != HasNonnullProperty(payload, "frameLocation"))
        {
            AddError(
                issues,
                "browser-dom-frame-owner-inconsistent",
                "#/payload/frameLocation",
                "An owner given a frame names its token and location; an owner that lost its frame names neither.");
        }
    }

    // A DevTools frame token as Chromium writes base::UnguessableToken: 32
    // uppercase hexadecimal digits.
    private static PropertyRule FrameTokenRule(string name, bool required, bool nullable) =>
        new(
            name,
            required,
            nullable,
            value => value.ValueKind == JsonValueKind.String &&
                value.GetString() is { Length: 32 } token &&
                token.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F'),
            "must be a DevTools frame token of 32 uppercase hexadecimal digits");

    private static void ValidateBrowserDomCheckpointStarted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "started-parsing", "finished-parsing", "post-mutation"),
                RequiredEnum(
                    "walkReason", "first", "after-loss", "check",
                    "started-parsing", "finished-parsing"),
                RequiredInteger("maximumNodes", positive: true),
                // Protocol 0.55 (slice 5a); absent before it.
                FrameTokenRule("frameToken", required: false, nullable: true),
                OptionalNullableBoolean("mainFrame")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        if (HasNonnullProperty(payload, "frameToken") != HasNonnullProperty(payload, "mainFrame"))
        {
            AddError(
                issues,
                "browser-dom-checkpoint-frame-inconsistent",
                "#/payload/mainFrame",
                "A walked document with a frame names its token and whether it is a main frame; one with no frame names neither.");
        }
        // From protocol 0.35 a document is walked at a mutation delivery only
        // for a reason of its own; only a finished parse, and from protocol
        // 0.42 the start of a parse, is always walked, and names itself.
        var walkReason = ReadString(payload, "walkReason");
        if (walkReason is "started-parsing" or "finished-parsing" &&
            ReadString(payload, "reason") is { } requested &&
            requested != walkReason)
        {
            AddError(
                issues,
                "browser-dom-checkpoint-walk-reason-inconsistent",
                "#/payload/walkReason",
                "A checkpoint is walked for the start or the end of parsing only when it was requested for it; otherwise for a first walk, a loss, or a check.");
        }
    }

    private static void ValidateBrowserDomCheckpointNode(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredInteger("nodeIndex", nonnegative: true),
                RequiredInteger("nodeId", positive: true),
                NullableInteger("parentNodeId", nonnegative: true),
                RequiredEnum(
                    "nodeType",
                    "document",
                    "element",
                    "text",
                    "comment",
                    "shadow-root",
                    "other"),
                RequiredString("nodeName")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
    }

    // The same record shape names a checkpoint, or, from protocol 0.34, the
    // insertion or transition it belongs to.
    private static void ValidateBrowserDomCheckpointNodeAttribute(
        JsonElement payload,
        ICollection<EventValidationIssue> issues,
        string identityProperty = "checkpointId")
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString(identityProperty),
                RequiredInteger("nodeId", positive: true),
                RequiredInteger("attributeIndex", nonnegative: true),
                NullableString("attributeNamespace"),
                RequiredString("attributeName"),
                RequiredText("attributeValue"),
                RequiredInteger("attributeValueLength", nonnegative: true),
                RequiredBoolean("attributeValueTruncated"),
                RequiredInteger("maximumValueLength", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateTruncatedText(
            payload,
            "attributeValue",
            "attributeValueLength",
            "attributeValueTruncated",
            issues);
    }

    // The same record shape names a checkpoint, or, from protocol 0.34, the
    // insertion or transition it belongs to.
    private static void ValidateBrowserDomCheckpointNodeCharacterData(
        JsonElement payload,
        ICollection<EventValidationIssue> issues,
        string identityProperty = "checkpointId")
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString(identityProperty),
                RequiredInteger("nodeId", positive: true),
                RequiredText("data"),
                RequiredInteger("dataLength", nonnegative: true),
                RequiredBoolean("dataTruncated"),
                RequiredInteger("maximumValueLength", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateTruncatedText(
            payload,
            "data",
            "dataLength",
            "dataTruncated",
            issues);
    }

    // The same record shape names a checkpoint, or, from protocol 0.34, the
    // insertion or transition it belongs to.
    private static void ValidateBrowserDomCheckpointShadowRoot(
        JsonElement payload,
        ICollection<EventValidationIssue> issues,
        string identityProperty = "checkpointId")
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString(identityProperty),
                RequiredInteger("nodeId", positive: true),
                RequiredInteger("hostNodeId", positive: true),
                RequiredEnum("mode", "open", "closed", "user-agent"),
                RequiredBoolean("delegatesFocus"),
                RequiredEnum("slotAssignment", "named", "manual"),
                RequiredBoolean("clonable"),
                RequiredBoolean("serializable"),
                RequiredBoolean("declarative"),
                RequiredBoolean("availableToElementInternals"),
                NullableText("referenceTarget")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
    }

    // The same record shape names a checkpoint, or, from protocol 0.34, the
    // insertion or transition it belongs to.
    private static void ValidateBrowserDomCheckpointSlotAssignment(
        JsonElement payload,
        ICollection<EventValidationIssue> issues,
        string identityProperty = "checkpointId")
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString(identityProperty),
                RequiredInteger("nodeId", positive: true),
                new PropertyRule(
                    "assignedNodeIds",
                    true,
                    false,
                    value => value.ValueKind == JsonValueKind.Array &&
                        value.EnumerateArray().All(item =>
                            item.ValueKind == JsonValueKind.Null ||
                            (item.ValueKind == JsonValueKind.Number &&
                                item.TryGetInt64(out var id) &&
                                id > 0)),
                    "must be an array of positive node ids or nulls"),
                RequiredInteger("assignedNodeCount", nonnegative: true),
                RequiredBoolean("assignedNodesTruncated"),
                RequiredInteger("maximumAssignedNodes", positive: true),
                .. (identityProperty == "transitionId"
                    ? Array.Empty<PropertyRule>()
                    : [RequiredBoolean("assignmentCurrent")])
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        var count = ReadNullableInteger(payload, "assignedNodeCount");
        var maximum = ReadNullableInteger(payload, "maximumAssignedNodes");
        if (count is null || maximum is null ||
            !payload.TryGetProperty("assignedNodeIds", out var ids) ||
            ids.ValueKind != JsonValueKind.Array ||
            !payload.TryGetProperty("assignedNodesTruncated", out var truncated) ||
            !IsBoolean(truncated))
        {
            return;
        }

        var recorded = ids.GetArrayLength();
        var consistent = recorded <= count &&
            recorded <= maximum &&
            truncated.GetBoolean() == (recorded < count);
        if (!consistent)
        {
            AddError(
                issues,
                "browser-dom-slot-assignment-inconsistent",
                "#/payload/assignedNodeIds",
                $"The slot records {recorded} assigned nodes of {count}, which " +
                    "does not agree with its truncation flag and maximum.");
        }
    }

    // Structural DOM change records (protocol 0.34). Transitions and the
    // insertions they name are "dom-transition-N".
    private static void ValidateDomTransitionIdentity(
        JsonElement payload,
        string property,
        ICollection<EventValidationIssue> issues)
    {
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateLayoutIdentity(
            payload,
            property,
            "dom-transition-",
            "browser-dom-transition-id-invalid",
            issues);
    }

    private static void ValidateBrowserDomNodeInserted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("transitionId"),
                RequiredEnum("insertionKind", "child", "shadow-root"),
                RequiredInteger("containerNodeId", positive: true),
                RequiredInteger("nodeId", positive: true),
                NullableInteger("previousSiblingNodeId", positive: true)
            ],
            issues);
        ValidateDomTransitionIdentity(payload, "transitionId", issues);
        if (ReadString(payload, "insertionKind") == "shadow-root" &&
            HasNonnullProperty(payload, "previousSiblingNodeId"))
        {
            AddError(
                issues,
                "browser-dom-shadow-root-insertion-sibling",
                "#/payload/previousSiblingNodeId",
                "An attached shadow root has no previous sibling.");
        }
    }

    private static void ValidateBrowserDomInsertedNode(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("insertionId"),
                RequiredInteger("nodeIndex", nonnegative: true),
                RequiredInteger("nodeId", positive: true),
                RequiredInteger("parentNodeId", positive: true),
                RequiredEnum(
                    "nodeType",
                    "document",
                    "element",
                    "text",
                    "comment",
                    "shadow-root",
                    "other"),
                RequiredString("nodeName")
            ],
            issues);
        ValidateDomTransitionIdentity(payload, "insertionId", issues);
    }

    private static void ValidateBrowserDomInsertionCompleted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("insertionId"),
                RequiredInteger("nodeCount", positive: true),
                RequiredInteger("attributeCount", nonnegative: true),
                RequiredInteger("characterDataCount", nonnegative: true),
                RequiredInteger("shadowRootCount", nonnegative: true),
                RequiredInteger("slotCount", nonnegative: true)
            ],
            issues);
        ValidateDomTransitionIdentity(payload, "insertionId", issues);
    }

    private static void ValidateBrowserDomNodeRemoved(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("transitionId"),
                RequiredInteger("containerNodeId", positive: true),
                RequiredInteger("nodeId", positive: true)
            ],
            issues);
        ValidateDomTransitionIdentity(payload, "transitionId", issues);
    }

    private static void ValidateBrowserDomChildrenRemoved(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("transitionId"),
                RequiredInteger("containerNodeId", positive: true)
            ],
            issues);
        ValidateDomTransitionIdentity(payload, "transitionId", issues);
    }

    // A checkpoint either covers no transition and names neither bound, or
    // covers at least one and names both. A half-stated range would leave a
    // consumer unable to decide whether a transition was covered.
    private static void ValidateTransitionCoverage(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("coveredTransitionCount", out var countValue) ||
            countValue.ValueKind != JsonValueKind.Number ||
            !countValue.TryGetInt64(out var count))
        {
            return;
        }

        var hasFirst = HasNonnullProperty(payload, "coveredTransitionFirstId");
        var hasLast = HasNonnullProperty(payload, "coveredTransitionLastId");
        if (count == 0 && (hasFirst || hasLast))
        {
            AddError(
                issues,
                "browser-dom-checkpoint-coverage-inconsistent",
                "#/payload/coveredTransitionCount",
                "A checkpoint covering no transition named a transition bound.");
            return;
        }

        if (count > 0 && (!hasFirst || !hasLast))
        {
            AddError(
                issues,
                "browser-dom-checkpoint-coverage-inconsistent",
                "#/payload/coveredTransitionCount",
                "A checkpoint covering transitions did not name the first and " +
                "last transition it covers.");
        }
    }

    private static void ValidateBrowserDomCheckpointCompleted(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "started-parsing", "finished-parsing", "post-mutation"),
                RequiredInteger("nodeCount", nonnegative: true),
                RequiredBoolean("truncated"),
                RequiredInteger("maximumNodes", positive: true),
                RequiredInteger("attributeCount", nonnegative: true),
                RequiredBoolean("attributesTruncated"),
                RequiredInteger("maximumAttributesPerNode", positive: true),
                RequiredInteger("maximumValueLength", positive: true),
                RequiredInteger("coveredTransitionCount", nonnegative: true),
                NullableString("coveredTransitionFirstId"),
                NullableString("coveredTransitionLastId"),
                RequiredInteger("shadowRootCount", nonnegative: true),
                RequiredInteger("slotCount", nonnegative: true),
                // Protocol 0.33. Optional because the database evidence
                // tables, which are not written for a recording with a
                // recording file, have no column for it.
                OptionalInteger("characterDataCount", nonnegative: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateTransitionCoverage(payload, issues);
    }

    private static void ValidateBrowserDomAttributeChanged(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("transitionId"),
                RequiredInteger("nodeId", positive: true),
                RequiredString("nodeName"),
                NullableString("attributeNamespace"),
                RequiredString("attributeName"),
                RequiredEnum("changeType", "added", "removed", "changed"),
                NullableString("attributeValue"),
                NullableInteger("attributeValueLength", nonnegative: true),
                RequiredBoolean("attributeValueTruncated"),
                NullableString("previousAttributeValue"),
                NullableInteger(
                    "previousAttributeValueLength",
                    nonnegative: true),
                RequiredBoolean("previousAttributeValueTruncated"),
                RequiredInteger("maximumValueLength", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateTruncatedText(
            payload,
            "attributeValue",
            "attributeValueLength",
            "attributeValueTruncated",
            issues);
        ValidateTruncatedText(
            payload,
            "previousAttributeValue",
            "previousAttributeValueLength",
            "previousAttributeValueTruncated",
            issues);
        ValidateAttributeChangeTransition(payload, issues);
    }

    private static void ValidateBrowserDomCharacterDataChanged(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("transitionId"),
                RequiredInteger("nodeId", positive: true),
                NullableInteger("parentNodeId", nonnegative: true),
                RequiredEnum("nodeType", "text", "comment", "other"),
                RequiredText("text"),
                RequiredInteger("textLength", nonnegative: true),
                RequiredBoolean("textTruncated"),
                RequiredText("previousText"),
                RequiredInteger("previousTextLength", nonnegative: true),
                RequiredBoolean("previousTextTruncated"),
                RequiredInteger("maximumValueLength", positive: true)
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
        ValidateRendererDocumentContext(payload, issues);
        ValidateTruncatedText(
            payload,
            "text",
            "textLength",
            "textTruncated",
            issues);
        ValidateTruncatedText(
            payload,
            "previousText",
            "previousTextLength",
            "previousTextTruncated",
            issues);
    }

    private static void ValidateAttributeChangeTransition(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        var changeType = ReadString(payload, "changeType");
        if (changeType is null)
        {
            return;
        }

        var hasValue = HasNonnullProperty(payload, "attributeValue");
        var hasPrevious = HasNonnullProperty(payload, "previousAttributeValue");
        var consistent = changeType switch
        {
            "added" => hasValue && !hasPrevious,
            "removed" => !hasValue && hasPrevious,
            "changed" => hasValue && hasPrevious,
            _ => true
        };
        if (consistent)
        {
            return;
        }

        AddError(
            issues,
            "browser-dom-attribute-change-inconsistent",
            "#/payload/changeType",
            $"An attribute change of type '{changeType}' does not carry the " +
            "value and previous value that change type requires.");
    }

    private static void ValidateTruncatedText(
        JsonElement payload,
        string textProperty,
        string lengthProperty,
        string truncatedProperty,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty(textProperty, out var text) ||
            text.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty(lengthProperty, out var length) ||
            !length.TryGetInt64(out var reportedLength) ||
            !payload.TryGetProperty(truncatedProperty, out var truncated) ||
            truncated.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return;
        }

        var recordedLength = (text.GetString() ?? string.Empty).Length;
        var isTruncated = truncated.ValueKind == JsonValueKind.True;
        var consistent = isTruncated
            ? reportedLength > recordedLength
            : reportedLength == recordedLength;
        if (consistent)
        {
            return;
        }

        AddError(
            issues,
            "browser-dom-text-truncation-inconsistent",
            $"#/payload/{lengthProperty}",
            $"Property '{lengthProperty}' reports {reportedLength} units for a " +
            $"recorded value of {recordedLength} units while " +
            $"'{truncatedProperty}' is {(isTruncated ? "true" : "false")}.");
    }

    private static void ValidateRendererDocumentContext(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("context", out var context) ||
            context.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (ReadString(context, "processType") != "renderer" ||
            ReadString(context, "documentId") is null ||
            ReadString(context, "documentToken") is null)
        {
            AddError(
                issues,
                "browser-dom-context-invalid",
                "#/payload/context",
                "DOM checkpoint evidence must identify a renderer document and its Chromium document token.");
        }
    }

    // An accessibility checkpoint is taken by a renderer and names the Chromium
    // document token it serialized, but it carries no DOM document node
    // identity, because the serialization is not taken at a DOM checkpoint.
    private static void ValidateRendererTokenContext(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("context", out var context) ||
            context.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (ReadString(context, "processType") != "renderer" ||
            ReadString(context, "documentToken") is null)
        {
            AddError(
                issues,
                "browser-accessibility-context-invalid",
                "#/payload/context",
                "Accessibility checkpoint evidence must identify a renderer " +
                    "and the Chromium document token it serialized.");
        }
    }

    private static void ValidateBrowserContextProperty(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("context", out var context) ||
            context.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ValidateShape(
            context,
            [
                RequiredString("browserInstanceId"),
                RequiredInteger("processId", nonnegative: true),
                RequiredString("processType"),
                NullableString("profileId"),
                NullableString("browserContextId"),
                NullableString("pageId"),
                NullableString("frameId"),
                NullableString("documentId"),
                NullableString("executionWorldId"),
                NullableString("documentToken")
            ],
            issues,
            "#/payload/context");
    }

    private static void ValidateBrowserEventTargetProperty(
        JsonElement payload,
        string property,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty(property, out var target) ||
            target.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ValidateBrowserEventTarget(
            target,
            issues,
            $"#/payload/{property}");
    }

    private static void ValidateBrowserEventTargetArrayProperty(
        JsonElement payload,
        string property,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty(property, out var targets) ||
            targets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var target in targets.EnumerateArray())
        {
            if (target.ValueKind == JsonValueKind.Object)
            {
                ValidateBrowserEventTarget(
                    target,
                    issues,
                    $"#/payload/{property}/{index}");
            }
            index++;
        }
    }

    // Validates one EventTarget reference. A Node carries a DOM node
    // identifier; a Window or other non-Node EventTarget has none and carries a
    // target identifier instead, so the identity required depends on the kind.
    private static void ValidateBrowserEventTarget(
        JsonElement target,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        ValidateShape(
            target,
            [
                RequiredEnum("kind", "node", "window", "other"),
                NullableString("interfaceName"),
                NullableString("targetId"),
                NullableString("documentId"),
                NullableInteger("nodeId", nonnegative: true),
                NullableString("backendNodeId"),
                NullableString("tagName"),
                NullableString("elementId"),
                RequiredStringArray("classes")
            ],
            issues,
            path);

        var kind = ReadString(target, "kind");
        if (kind == "node" && !HasNonnullProperty(target, "nodeId"))
        {
            AddError(
                issues,
                "browser-event-target-identity",
                path,
                "a node event target must report its nodeId");
        }

        if (kind is "window" or "other")
        {
            if (HasNonnullProperty(target, "nodeId"))
            {
                AddError(
                    issues,
                    "browser-event-target-identity",
                    path,
                    $"a {kind} event target has no nodeId");
            }

            if (!HasNonnullProperty(target, "targetId"))
            {
                AddError(
                    issues,
                    "browser-event-target-identity",
                    path,
                    $"a {kind} event target must report its targetId");
            }
        }
    }

    private static void ValidateBrowserLocationProperty(
        JsonElement payload,
        ICollection<EventValidationIssue> issues,
        string property = "location")
    {
        if (!payload.TryGetProperty(property, out var location) ||
            location.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ValidateShape(
            location,
            [
                NullableString("scriptId"),
                NullableString("url"),
                NullableInteger("line", nonnegative: true),
                NullableInteger("column", nonnegative: true),
                NullableString("functionName"),
                NullableString("sourceHash")
            ],
            issues,
            $"#/payload/{property}");
    }

    // A listener record that names a world must also report that world in its
    // context, because the context field is what correlates records from the
    // same world. A world named in only one of the two places would let a
    // consumer read two different answers from one record.
    private static void ValidateBrowserExecutionWorldProperty(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        if (!payload.TryGetProperty("world", out var world) ||
            world.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ValidateShape(
            world,
            [
                RequiredEnum(
                    "kind",
                    "main",
                    "isolated",
                    "inspector-isolated",
                    "worker-or-worklet",
                    "shadow-realm",
                    "other"),
                RequiredInteger("blinkWorldId", nonnegative: true),
                NullableString("name"),
                NullableString("stableId")
            ],
            issues,
            "#/payload/world");

        if (!payload.TryGetProperty("context", out var context) ||
            context.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!HasNonnullProperty(context, "executionWorldId"))
        {
            AddError(
                issues,
                "browser-execution-world-identity",
                "#/payload/context/executionWorldId",
                "a record that names a world must report its executionWorldId");
            return;
        }

        if (!world.TryGetProperty("blinkWorldId", out var blinkWorldId) ||
            blinkWorldId.ValueKind != JsonValueKind.Number ||
            !blinkWorldId.TryGetInt32(out var worldId))
        {
            return;
        }

        var expected = $"world-{worldId}";
        if (context.GetProperty("executionWorldId").GetString() != expected)
        {
            AddError(
                issues,
                "browser-execution-world-identity",
                "#/payload/context/executionWorldId",
                $"a record whose world is {worldId} must report {expected}");
        }
    }

    private static void ValidateOmission(
        JsonElement payload,
        ICollection<EventValidationIssue> issues) =>
        ValidateShape(
            payload,
            [
                RequiredString("reason"),
                OptionalInteger("count", nonnegative: true),
                OptionalEnum("stream", "microphone", "system")
            ],
            issues);

    private static readonly string[] UiaObservationTypes =
        ["focus-changed", "automation-event", "structure-changed", "property-changed"];

    // A UI Automation queue-full omission written since per-episode drop
    // records states the first and last refused arrival times and the count
    // of each observation type, and is timed at the last refusal. Archives
    // written before then carry one total count at stop and none of these
    // fields.
    private static void ValidateUiaOmission(
        long recordedAt,
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredString("reason"),
                OptionalInteger("count", nonnegative: true),
                OptionalInteger("firstDroppedAtNanoseconds", nonnegative: true),
                OptionalInteger("lastDroppedAtNanoseconds", nonnegative: true),
                OptionalObject("droppedByObservationType")
            ],
            issues);

        var hasFirst = payload.TryGetProperty("firstDroppedAtNanoseconds", out var first);
        var hasLast = payload.TryGetProperty("lastDroppedAtNanoseconds", out var last);
        var hasCounts = payload.TryGetProperty("droppedByObservationType", out var counts);
        if (!hasFirst && !hasLast && !hasCounts)
        {
            return;
        }

        void Inconsistent(string message) =>
            AddError(
                issues,
                "uia-omission-episode-inconsistent",
                "#/payload",
                message);

        if (ReadString(payload, "reason") != "uia-observation-queue-full")
        {
            Inconsistent("Only a queue-full omission states a drop episode.");
            return;
        }

        if (!hasFirst || !hasLast || !hasCounts ||
            !payload.TryGetProperty("count", out var count) ||
            !IsInteger(first) || !IsInteger(last) || !IsInteger(count) ||
            counts.ValueKind != JsonValueKind.Object)
        {
            Inconsistent(
                "A drop episode states count, firstDroppedAtNanoseconds, " +
                "lastDroppedAtNanoseconds, and droppedByObservationType together.");
            return;
        }

        if (first.GetInt64() > last.GetInt64())
        {
            Inconsistent("The first refused arrival follows the last.");
        }

        if (recordedAt != last.GetInt64())
        {
            Inconsistent("A drop episode is timed at its last refused arrival.");
        }

        long sum = 0;
        foreach (var entry in counts.EnumerateObject())
        {
            if (!UiaObservationTypes.Contains(entry.Name, StringComparer.Ordinal))
            {
                Inconsistent($"'{entry.Name}' is not a UI Automation observation type.");
            }

            if (!IsInteger(entry.Value) || entry.Value.GetInt64() <= 0)
            {
                Inconsistent("Each dropped observation type count is a positive integer.");
                continue;
            }

            sum += entry.Value.GetInt64();
        }

        if (count.GetInt64() <= 0 || sum != count.GetInt64())
        {
            Inconsistent(
                "The dropped observation type counts sum to the episode count, " +
                "which is positive.");
        }
    }

    // A browser omission names how many records were lost and, when the
    // reporter knows which browser process lost them, carries that process
    // context. A reporter that cannot attribute the loss to one process omits
    // the context rather than naming a process it did not observe.
    private static void ValidateBrowserOmission(
        JsonElement payload,
        ICollection<EventValidationIssue> issues)
    {
        ValidateShape(
            payload,
            [
                RequiredString("reason"),
                OptionalInteger("count", nonnegative: true),
                OptionalNullableObject("context")
            ],
            issues);
        ValidateBrowserContextProperty(payload, issues);
    }

    private static void ValidateIntegerRectangle(
        JsonElement value,
        ICollection<EventValidationIssue> issues,
        string path) =>
        ValidateShape(
            value,
            [
                RequiredInteger("x"),
                RequiredInteger("y"),
                RequiredInteger("width", nonnegative: true),
                RequiredInteger("height", nonnegative: true)
            ],
            issues,
            path);

    private static void ValidateNumberRectangle(
        JsonElement value,
        ICollection<EventValidationIssue> issues,
        string path) =>
        ValidateShape(
            value,
            [
                RequiredNumber("x"),
                RequiredNumber("y"),
                RequiredNumber("width", nonnegative: true),
                RequiredNumber("height", nonnegative: true)
            ],
            issues,
            path);

    private static void ValidateMonitor(
        JsonElement value,
        ICollection<EventValidationIssue> issues,
        string path)
    {
        ValidateShape(
            value,
            [
                RequiredString("deviceName"),
                RequiredObject("bounds"),
                RequiredObject("workArea"),
                RequiredBoolean("isPrimary")
            ],
            issues,
            path);
        ValidateOptionalObject(value, "bounds", ValidateIntegerRectangle, issues, path);
        ValidateOptionalObject(value, "workArea", ValidateIntegerRectangle, issues, path);
    }

    private static void ValidateOptionalObject(
        JsonElement parent,
        string property,
        Action<JsonElement, ICollection<EventValidationIssue>, string> validate,
        ICollection<EventValidationIssue> issues,
        string parentPath = "#/payload")
    {
        if (parent.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            validate(value, issues, $"{parentPath}/{property}");
        }
    }

    private static void ValidateShape(
        JsonElement payload,
        IReadOnlyList<PropertyRule> rules,
        ICollection<EventValidationIssue> issues,
        string path = "#/payload")
    {
        var ruleMap = rules.ToDictionary(rule => rule.Name, StringComparer.Ordinal);
        foreach (var rule in rules.Where(rule => rule.Required))
        {
            if (!payload.TryGetProperty(rule.Name, out _))
            {
                AddError(
                    issues,
                    "payload-property-missing",
                    $"{path}/{rule.Name}",
                    $"Required payload property '{rule.Name}' is missing.");
            }
        }

        foreach (var property in payload.EnumerateObject())
        {
            if (!ruleMap.TryGetValue(property.Name, out var rule))
            {
                AddError(
                    issues,
                    "payload-property-unexpected",
                    $"{path}/{property.Name}",
                    $"Payload property '{property.Name}' is not defined for this event type.");
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null && rule.Nullable)
            {
                continue;
            }

            if (!rule.Validate(property.Value))
            {
                AddError(
                    issues,
                    "payload-property-invalid",
                    $"{path}/{property.Name}",
                    $"Payload property '{property.Name}' {rule.Expectation}.");
            }
        }
    }

    private static PropertyRule RequiredString(string name) =>
        new(name, true, false, IsNonemptyString, "must be a nonempty string");

    private static PropertyRule NullableString(string name) =>
        new(name, true, true, IsString, "must be a string or null");

    private static PropertyRule RequiredText(string name) =>
        new(name, true, false, IsString, "must be a string");

    private static PropertyRule RequiredInteger(
        string name,
        bool nonnegative = false,
        bool positive = false) =>
        new(
            name,
            true,
            false,
            value => IsInteger(value) &&
                (!nonnegative || value.GetInt64() >= 0) &&
                (!positive || value.GetInt64() > 0),
            positive
                ? "must be a positive integer"
                : nonnegative
                    ? "must be a nonnegative integer"
                    : "must be an integer");

    private static PropertyRule NullableInteger(
        string name,
        bool nonnegative = false,
        bool positive = false) =>
        new(
            name,
            true,
            true,
            value => IsInteger(value) &&
                (!nonnegative || value.GetInt64() >= 0) &&
                (!positive || value.GetInt64() > 0),
            positive
                ? "must be a positive integer or null"
                : nonnegative
                    ? "must be a nonnegative integer or null"
                    : "must be an integer or null");

    private static PropertyRule OptionalNullableInteger(
        string name,
        bool nonnegative = false) =>
        new(
            name,
            false,
            true,
            value => IsInteger(value) && (!nonnegative || value.GetInt64() >= 0),
            nonnegative
                ? "must be a nonnegative integer or null"
                : "must be an integer or null");

    private static PropertyRule OptionalInteger(
        string name,
        bool nonnegative = false) =>
        new(
            name,
            false,
            false,
            value => IsInteger(value) && (!nonnegative || value.GetInt64() >= 0),
            nonnegative
                ? "must be a nonnegative integer"
                : "must be an integer");

    private static PropertyRule RequiredNumber(
        string name,
        bool nonnegative = false,
        bool positive = false) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number) &&
                double.IsFinite(number) &&
                (!nonnegative || number >= 0) &&
                (!positive || number > 0),
            positive
                ? "must be a finite positive number"
                : nonnegative
                    ? "must be a finite nonnegative number"
                    : "must be a finite number");

    private static PropertyRule OptionalNumber(
        string name,
        bool positive = false) =>
        new(
            name,
            false,
            false,
            value => value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number) &&
                double.IsFinite(number) &&
                (!positive || number > 0),
            positive
                ? "must be a finite positive number"
                : "must be a finite number");

    private static PropertyRule OptionalNullableNumber(
        string name,
        bool nonnegative = false) =>
        new(
            name,
            false,
            true,
            value => value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number) &&
                double.IsFinite(number) &&
                (!nonnegative || number >= 0),
            nonnegative
                ? "must be a finite nonnegative number or null"
                : "must be a finite number or null");

    private static PropertyRule RequiredNullableNumber(
        string name,
        bool nonnegative = false) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number) &&
                double.IsFinite(number) &&
                (!nonnegative || number >= 0),
            nonnegative
                ? "must be a finite nonnegative number or null"
                : "must be a finite number or null");

    private static PropertyRule RequiredBoolean(string name) =>
        new(name, true, false, IsBoolean, "must be a boolean");

    private static PropertyRule NullableBoolean(string name) =>
        new(name, true, true, IsBoolean, "must be a boolean or null");

    private static PropertyRule OptionalBoolean(string name) =>
        new(name, false, false, IsBoolean, "must be a boolean");

    private static PropertyRule OptionalNullableText(string name) =>
        new(name, false, true, IsString, "must be a string or null");

    private static PropertyRule OptionalNullableBoolean(string name) =>
        new(name, false, true, IsBoolean, "must be a boolean or null");

    private static PropertyRule RequiredObject(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.Object,
            "must be an object");

    private static PropertyRule RequiredObjectArray(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(
                    item => item.ValueKind == JsonValueKind.Object),
            "must be an array of objects");

    private static PropertyRule OptionalNullableObjectArray(string name) =>
        new(
            name,
            false,
            true,
            value => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(
                    item => item.ValueKind == JsonValueKind.Object),
            "must be an array of objects or null");

    private static PropertyRule NullableObjectArray(string name) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(
                    item => item.ValueKind == JsonValueKind.Object),
            "must be an array of objects or null");

    private static PropertyRule OptionalObjectArray(string name) =>
        new(
            name,
            false,
            false,
            value => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(
                    item => item.ValueKind == JsonValueKind.Object),
            "must be an array of objects");

    private static PropertyRule OptionalObject(string name) =>
        new(
            name,
            false,
            false,
            value => value.ValueKind == JsonValueKind.Object,
            "must be an object");

    private static PropertyRule NullableObject(string name) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.Object,
            "must be an object or null");

    private static PropertyRule OptionalNullableObject(string name) =>
        new(
            name,
            false,
            true,
            value => value.ValueKind == JsonValueKind.Object,
            "must be an object or null");

    private static PropertyRule RequiredDateTime(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.String &&
                value.TryGetDateTimeOffset(out _),
            "must be a date-time string");

    private static PropertyRule NullableDateTime(string name) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.String &&
                value.TryGetDateTimeOffset(out _),
            "must be a date-time string or null");

    private static PropertyRule RequiredStringArray(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(IsNonemptyString),
            "must be an array of nonempty strings");

    private static PropertyRule NullableText(string name) =>
        new(name, true, true, IsString, "must be a string or null");

    private static PropertyRule RequiredTextArray(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(IsString),
            "must be an array of strings");

    private static PropertyRule NullableTextArray(string name) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(IsString),
            "must be an array of strings or null");

    private static PropertyRule NullableEnum(string name, params string[] values) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.String &&
                values.Contains(value.GetString(), StringComparer.Ordinal),
            $"must be null or one of: {string.Join(", ", values)}");

    private static PropertyRule NullableIntegerArray(string name) =>
        new(
            name,
            true,
            true,
            value => value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(IsInteger),
            "must be an array of integers or null");

    private static PropertyRule RequiredEnum(string name, params string[] values) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.String &&
                values.Contains(value.GetString(), StringComparer.Ordinal),
            $"must be one of: {string.Join(", ", values)}");

    private static PropertyRule OptionalNullableEnum(string name, params string[] values) =>
        new(
            name,
            false,
            true,
            value => value.ValueKind == JsonValueKind.String &&
                values.Contains(value.GetString(), StringComparer.Ordinal),
            $"must be null or one of: {string.Join(", ", values)}");

    private static PropertyRule OptionalEnum(string name, params string[] values) =>
        new(
            name,
            false,
            false,
            value => value.ValueKind == JsonValueKind.String &&
                values.Contains(value.GetString(), StringComparer.Ordinal),
            $"must be one of: {string.Join(", ", values)}");

    private static PropertyRule RequiredSafePath(string name) =>
        new(
            name,
            true,
            false,
            value => value.ValueKind == JsonValueKind.String &&
                IsSafeRelativePath(value.GetString()),
            "must be a safe canonical relative path");

    private static bool IsInteger(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _);

    private static bool IsString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String;

    private static bool IsNonemptyString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());

    private static bool IsBoolean(JsonElement value) =>
        value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool IsSafeRelativePath(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !Path.IsPathRooted(value) &&
        !value.Contains('\\') &&
        !value.Split('/').Any(segment => segment is "" or "." or "..");

    private static bool HasNonnullProperty(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind != JsonValueKind.Null;

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static void AddError(
        ICollection<EventValidationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(new EventValidationIssue(code, path, message));

    private sealed record PropertyRule(
        string Name,
        bool Required,
        bool Nullable,
        Func<JsonElement, bool> Validate,
        string Expectation);
}
