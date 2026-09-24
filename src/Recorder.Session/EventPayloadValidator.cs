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
        "accessibility.uia.events",
        "graphics.desktop.frames",
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
        "browser.network"
    ];

    public static void Validate(
        JsonElement record,
        ICollection<ArchiveValidationIssue> issues,
        long lineNumber)
    {
        var channel = ReadString(record, "channel");
        var eventType = ReadString(record, "eventType");
        if (channel is null || eventType is null)
        {
            return;
        }

        if (!record.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object)
        {
            if (BuiltInChannels.Contains(channel))
            {
                AddError(
                    issues,
                    "event-payload-not-object",
                    "events.ndjson#/payload",
                    "Built-in event payloads must be JSON objects.",
                    lineNumber);
            }

            return;
        }

        switch ((channel, eventType))
        {
            case ("collector.lifecycle", "collector-lifecycle"):
                ValidateLifecycle(payload, issues, lineNumber);
                break;
            case ("session.annotations", "session-marker"):
                ValidateAnnotation(payload, issues, lineNumber);
                break;
            case ("input.keyboard", "raw-keyboard"):
                ValidateRawKeyboard(payload, issues, lineNumber);
                break;
            case ("input.mouse", "raw-mouse"):
                ValidateRawMouse(payload, issues, lineNumber);
                break;
            case ("window.foreground", "foreground-window"):
                ValidateForegroundWindow(payload, issues, lineNumber);
                break;
            case ("accessibility.uia.events", "focus-changed"):
            case ("accessibility.uia.events", "automation-event"):
            case ("accessibility.uia.events", "structure-changed"):
            case ("accessibility.uia.events", "property-changed"):
                ValidateUiaEvent(payload, issues, lineNumber);
                break;
            case ("graphics.desktop.frames", "desktop-frame"):
                ValidateDesktopFrame(payload, issues, lineNumber);
                break;
            case ("audio.microphone", "audio-stream-started"):
            case ("audio.microphone", "audio-stream-stopped"):
            case ("audio.system", "audio-stream-started"):
            case ("audio.system", "audio-stream-stopped"):
                ValidateAudioStream(payload, issues, lineNumber);
                break;
            case ("audio.microphone", "audio-buffer"):
            case ("audio.system", "audio-buffer"):
                ValidateAudioBuffer(payload, issues, lineNumber);
                break;
            case ("audio.microphone", "audio-stream-error"):
            case ("audio.system", "audio-stream-error"):
                ValidateAudioError(payload, issues, lineNumber);
                break;
            case ("browser.lifecycle", "browser-connected"):
                ValidateBrowserConnected(payload, issues, lineNumber);
                break;
            case ("browser.lifecycle", "browser-clock-synchronized"):
                ValidateBrowserClockSynchronized(payload, issues, lineNumber);
                break;
            case ("browser.accessibility", "accessibility-checkpoint-started"):
                ValidateBrowserAccessibilityCheckpointStarted(
                    payload,
                    issues,
                    lineNumber);
                break;
            case ("browser.accessibility", "accessibility-checkpoint-node"):
                ValidateBrowserAccessibilityCheckpointNode(
                    payload,
                    issues,
                    lineNumber);
                break;
            case (
                "browser.accessibility",
                "accessibility-checkpoint-completed"):
                ValidateBrowserAccessibilityCheckpointCompleted(
                    payload,
                    issues,
                    lineNumber);
                break;
            case ("browser.listener", "listener-registered"):
            case ("browser.listener", "listener-removed"):
            case ("browser.listener", "listener-callback-replaced"):
                ValidateBrowserListener(payload, issues, lineNumber);
                break;
            case ("browser.dispatch", "dispatch-started"):
            case ("browser.dispatch", "listener-invoked"):
            case ("browser.dispatch", "dispatch-completed"):
                ValidateBrowserDispatch(
                    payload,
                    issues,
                    lineNumber,
                    requireDefaultAction: false);
                break;
            case ("browser.dispatch", "default-action"):
                ValidateBrowserDispatch(
                    payload,
                    issues,
                    lineNumber,
                    requireDefaultAction: true);
                break;
            case ("browser.timer", "timer-scheduled"):
            case ("browser.timer", "timer-fired"):
            case ("browser.timer", "timer-cancelled"):
                ValidateBrowserTimer(payload, issues, lineNumber);
                break;
            case ("browser.scheduler", "wake-up-deferred"):
                ValidateBrowserScheduler(payload, issues, lineNumber);
                break;
            case ("browser.navigation", "navigation-started"):
                ValidateBrowserNavigation(
                    payload,
                    issues,
                    lineNumber,
                    completed: false);
                break;
            case ("browser.navigation", "navigation-completed"):
                ValidateBrowserNavigation(
                    payload,
                    issues,
                    lineNumber,
                    completed: true);
                break;
            case ("browser.dom", "dom-checkpoint-started"):
                ValidateBrowserDomCheckpointStarted(payload, issues, lineNumber);
                break;
            case ("browser.dom", "dom-checkpoint-node"):
                ValidateBrowserDomCheckpointNode(payload, issues, lineNumber);
                break;
            case ("browser.dom", "dom-checkpoint-node-attribute"):
                ValidateBrowserDomCheckpointNodeAttribute(payload, issues, lineNumber);
                break;
            case ("browser.dom", "dom-checkpoint-completed"):
                ValidateBrowserDomCheckpointCompleted(payload, issues, lineNumber);
                break;
            case ("browser.dom", "dom-attribute-changed"):
                ValidateBrowserDomAttributeChanged(payload, issues, lineNumber);
                break;
            case ("browser.dom", "dom-character-data-changed"):
                ValidateBrowserDomCharacterDataChanged(payload, issues, lineNumber);
                break;
            case ("browser.cookie", "document-cookie-read"):
                ValidateBrowserDocumentCookieRead(payload, issues, lineNumber);
                break;
            case ("browser.cookie", "document-cookie-write"):
                ValidateBrowserDocumentCookieWrite(payload, issues, lineNumber);
                break;
            case ("browser.cookie", "cookie-store-request"):
                ValidateBrowserCookieStoreRequest(payload, issues, lineNumber);
                break;
            case ("browser.cookie", "cookie-store-result"):
                ValidateBrowserCookieStoreResult(payload, issues, lineNumber);
                break;
            case ("browser.cookie", "cookie-store-change"):
                ValidateBrowserCookieStoreChange(payload, issues, lineNumber);
                break;
            case ("browser.cookie", "cookie-access"):
                ValidateBrowserCookieAccess(payload, issues, lineNumber);
                break;
            case ("browser.interaction", "focus-changed"):
                ValidateBrowserFocusChanged(payload, issues, lineNumber);
                break;
            case ("browser.interaction", "selection-changed"):
                ValidateBrowserSelectionChanged(payload, issues, lineNumber);
                break;
            case ("browser.interaction", "text-control-value-changed"):
                ValidateBrowserTextControlValueChanged(payload, issues, lineNumber);
                break;
            case ("browser.interaction", "active-descendant-reference-set"):
                ValidateBrowserActiveDescendantReferenceSet(
                    payload, issues, lineNumber);
                break;
            case ("browser.layout", "layout-checkpoint-started"):
                ValidateBrowserLayoutCheckpointStarted(payload, issues, lineNumber);
                break;
            case ("browser.layout", "layout-checkpoint-node"):
                ValidateBrowserLayoutCheckpointNode(payload, issues, lineNumber);
                break;
            case ("browser.layout", "layout-checkpoint-completed"):
                ValidateBrowserLayoutCheckpointCompleted(payload, issues, lineNumber);
                break;
            case ("browser.network", "request-will-be-sent"):
                ValidateBrowserNetworkRequestWillBeSent(payload, issues, lineNumber);
                break;
            case ("browser.network", "response-received"):
                ValidateBrowserNetworkResponseReceived(payload, issues, lineNumber);
                break;
            case ("browser.network", "request-finished"):
                ValidateBrowserNetworkRequestFinished(payload, issues, lineNumber);
                break;
            case ("browser.network", "request-failed"):
                ValidateBrowserNetworkRequestFailed(payload, issues, lineNumber);
                break;
            case ("browser.network", "memory-cache-hit"):
                ValidateBrowserNetworkMemoryCacheHit(payload, issues, lineNumber);
                break;
            case ("browser.network", "request-headers-sent"):
            case ("browser.network", "response-headers-received"):
                ValidateBrowserNetworkWireHeaders(
                    payload,
                    eventType == "response-headers-received",
                    issues,
                    lineNumber);
                break;
            case ("browser.network", "navigation-response"):
                ValidateBrowserNetworkNavigationResponse(payload, issues, lineNumber);
                break;
            case ("window.foreground", "collector-omission"):
            case ("accessibility.uia.events", "collector-omission"):
            case ("graphics.desktop.frames", "collector-omission"):
            case ("audio.microphone", "collector-omission"):
            case ("audio.system", "collector-omission"):
                ValidateOmission(payload, issues, lineNumber);
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
            case ("browser.network", "collector-omission"):
                ValidateBrowserOmission(payload, issues, lineNumber);
                break;
            default:
                if (BuiltInChannels.Contains(channel))
                {
                    AddError(
                        issues,
                        "event-type-unsupported",
                        "events.ndjson#/eventType",
                        $"Event type '{eventType}' is not defined for built-in channel '{channel}'.",
                        lineNumber);
                }

                break;
        }
    }

    private static void ValidateLifecycle(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
        ValidateShape(
            payload,
            [
                RequiredString("action"),
                RequiredString("state"),
                RequiredDateTime("utc")
            ],
            issues,
            line);

    private static void ValidateAnnotation(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
        ValidateShape(
            payload,
            [NullableString("note")],
            issues,
            line);

    private static void ValidateRawKeyboard(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
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
            issues,
            line);

    private static void ValidateRawMouse(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
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
            issues,
            line);

    private static void ValidateForegroundWindow(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);

        ValidateOptionalObject(payload, "bounds", ValidateIntegerRectangle, issues, line);
        ValidateOptionalObject(payload, "monitor", ValidateMonitor, issues, line);
    }

    private static void ValidateUiaEvent(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);

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
                    RequiredStringArray("qualityFlags")
                ],
                issues,
                line,
                "events.ndjson#/payload/element");
            ValidateOptionalObject(
                element,
                "boundingRectangle",
                ValidateNumberRectangle,
                issues,
                line,
                "events.ndjson#/payload/element");
        }
    }

    private static void ValidateDesktopFrame(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
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
                RequiredInteger("gdiFallbackFrameCount", nonnegative: true)
            ],
            issues,
            line);

    private static void ValidateAudioStream(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);

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
                line,
                "events.ndjson#/payload/format");
        }
    }

    private static void ValidateAudioBuffer(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
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
            issues,
            line);

    private static void ValidateAudioError(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
        ValidateShape(
            payload,
            [
                RequiredEnum("stream", "microphone", "system"),
                NullableString("errorType"),
                NullableString("message")
            ],
            issues,
            line);

    // A lifecycle record states which process connected and what it spoke. The
    // receiver already requires a browser process to carry neither a parent nor
    // a child process identifier and a renderer to carry both, so both are
    // present here and null for the browser process rather than absent.
    private static void ValidateBrowserConnected(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
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
            issues,
            line);

    // The clock record carries the mapping identity and the uncertainty the
    // recorder estimated for it, which is a nonnegative half round trip rather
    // than a signed offset. The browser's tick frequency is a decimal string,
    // because it does not fit a JSON number on every platform.
    private static void ValidateBrowserClockSynchronized(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
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
            issues,
            line);

    private static void ValidateBrowserAccessibilityCheckpointStarted(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererTokenContext(payload, issues, line);
    }

    private static void ValidateBrowserAccessibilityCheckpointNode(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererTokenContext(payload, issues, line);
    }

    private static void ValidateBrowserAccessibilityCheckpointCompleted(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererTokenContext(payload, issues, line);
    }

    private static void ValidateBrowserListener(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
                NullableObject("world")
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateBrowserEventTargetProperty(payload, "target", issues, line);
        ValidateBrowserLocationProperty(payload, issues, line);
        ValidateBrowserExecutionWorldProperty(payload, issues, line);
    }

    private static void ValidateBrowserDispatch(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
                    : OptionalNullableObject("currentTarget")
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateBrowserEventTargetProperty(payload, "originalTarget", issues, line);
        ValidateBrowserEventTargetProperty(payload, "currentTarget", issues, line);
        ValidateBrowserEventTargetArrayProperty(payload, "composedPath", issues, line);
    }

    private static void ValidateBrowserTimer(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateBrowserLocationProperty(
            payload,
            issues,
            line,
            "callbackLocation");
    }

    private static void ValidateBrowserScheduler(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);

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
                "events.ndjson#/payload",
                "Scheduler wake-up ticks must be nonnegative decimal integers with allowedWakeUpTicks greater than desiredWakeUpTicks.",
                line);
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
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
        ValidateBrowserLocationProperty(payload, issues, line);
        ValidateBrowserExecutionWorldProperty(payload, issues, line);
    }

    private static void ValidateBrowserFocusChanged(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateInteractionCommon(payload, issues, line);

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
                "events.ndjson#/payload/outcome",
                $"Outcome '{outcome}' does not follow from the requested and " +
                    $"focused nodes, which give '{expected}'.",
                line);
        }
        if (focused is null &&
            ReadNullableInteger(payload, "activeDescendantNodeId") is not null)
        {
            AddError(
                issues,
                "browser-focus-active-descendant-without-focus",
                "events.ndjson#/payload/activeDescendantNodeId",
                "An active descendant is reported while no element is focused.",
                line);
        }
    }

    private static void ValidateBrowserSelectionChanged(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateInteractionCommon(payload, issues, line);

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
                issues,
                line);
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
                "events.ndjson#/payload/textControlNodeId",
                "Text-control selection fields must be all present or all null.",
                line);
        }
        ValidateOrderedRange(
            payload,
            "textControlSelectionStart",
            "textControlSelectionEnd",
            issues,
            line);
    }

    private static void ValidateBrowserTextControlValueChanged(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateInteractionCommon(payload, issues, line);
        ValidateTruncatedText(
            payload, "value", "valueLength", "valueTruncated", issues, line);
        ValidateOrderedRange(
            payload, "selectionStart", "selectionEnd", issues, line);
        var value = ReadString(payload, "value");
        var maximum = ReadNullableInteger(payload, "maximumValueLength");
        if (value is not null && maximum is not null && value.Length > maximum)
        {
            AddError(
                issues,
                "browser-text-control-value-over-maximum",
                "events.ndjson#/payload/value",
                $"The recorded value holds {value.Length} units, more than " +
                    $"the stated maximum of {maximum}.",
                line);
        }
    }

    private static void ValidateBrowserActiveDescendantReferenceSet(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateInteractionCommon(payload, issues, line);
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
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateNetworkScopeProperty(payload, issues, line);
        ValidateBrowserLocationProperty(payload, issues, line);
        ValidateBrowserExecutionWorldProperty(payload, issues, line);
        ValidateOptionalObject(payload, "request", ValidateNetworkRequest, issues, line);
        ValidateOptionalObject(
            payload, "redirectResponse", ValidateNetworkResponse, issues, line);

        var redirect = payload.TryGetProperty("redirect", out var redirectValue) &&
            redirectValue.ValueKind == JsonValueKind.True;
        if (redirect != HasNonnullProperty(payload, "redirectResponse"))
        {
            AddError(
                issues,
                "browser-network-redirect-response",
                "events.ndjson#/payload/redirectResponse",
                "A redirect reports its redirect response and a first request reports none.",
                line);
        }
    }

    private static void ValidateBrowserNetworkResponseReceived(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateNetworkScopeProperty(payload, issues, line);
        ValidateInspectorId(payload, issues, line);
        ValidateOptionalObject(payload, "response", ValidateNetworkResponse, issues, line);
    }

    private static void ValidateBrowserNetworkRequestFinished(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateNetworkScopeProperty(payload, issues, line);
        ValidateInspectorId(payload, issues, line);
    }

    private static void ValidateBrowserNetworkRequestFailed(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateNetworkScopeProperty(payload, issues, line);
        ValidateInspectorId(payload, issues, line);
        ValidateOptionalObject(
            payload,
            "corsError",
            (value, list, number, path) => ValidateShape(
                value,
                [RequiredString("error"), NullableString("failedParameter")],
                list,
                number,
                path),
            issues,
            line);
    }

    private static void ValidateBrowserNetworkMemoryCacheHit(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateNetworkScopeProperty(payload, issues, line);
        ValidateOptionalObject(payload, "request", ValidateNetworkRequest, issues, line);
        ValidateOptionalObject(payload, "response", ValidateNetworkResponse, issues, line);
    }

    private static void ValidateBrowserNetworkWireHeaders(
        JsonElement payload,
        bool response,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
        ValidateShape(payload, rules, issues, line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateNetworkHeaders(
            payload,
            "headers",
            "headerCount",
            "headersTruncated",
            issues,
            line,
            "events.ndjson#/payload");

        if (payload.TryGetProperty("cookies", out var cookies) &&
            cookies.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var cookie in cookies.EnumerateArray())
            {
                if (cookie.ValueKind == JsonValueKind.Object)
                {
                    ValidateCookieAccessEntry(cookie, index, issues, line);
                }

                index++;
            }

            ValidateCookieNameCount(payload, "cookies", "cookiesTruncated", issues, line);
        }
    }

    private static void ValidateBrowserNetworkNavigationResponse(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateNetworkHeaders(
            payload,
            "requestHeaders",
            "requestHeaderCount",
            "requestHeadersTruncated",
            issues,
            line,
            "events.ndjson#/payload");
        ValidateOptionalObject(
            payload,
            "response",
            (value, list, number, path) =>
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
                    number,
                    path);
                ValidateOptionalObject(
                    value, "remoteAddress", ValidateNetworkRemoteAddress, list, number, path);
                ValidateNetworkHeaders(
                    value, "headers", "headerCount", "headersTruncated", list, number, path);
            },
            issues,
            line);
        ValidateOptionalObject(
            payload,
            "timing",
            (value, list, number, path) => ValidateNetworkTiming(
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
                number,
                path),
            issues,
            line);
    }

    private static void ValidateNetworkScopeProperty(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateOptionalObject(
            payload,
            "scope",
            (value, list, number, path) =>
            {
                ValidateShape(
                    value,
                    [
                        RequiredEnum("contextKind", NetworkContextKinds),
                        NullableString("workerToken"),
                        NullableString("globalObjectUrl")
                    ],
                    list,
                    number,
                    path);
                if (ReadString(value, "contextKind") == "window" &&
                    HasNonnullProperty(value, "workerToken"))
                {
                    AddError(
                        list,
                        "browser-network-scope-invalid",
                        $"{path}/workerToken",
                        "A window scope carries no worker token.",
                        number);
                }
            },
            issues,
            line);
    }

    private static void ValidateInspectorId(
        JsonElement value,
        ICollection<ArchiveValidationIssue> issues,
        long line,
        string path = "events.ndjson#/payload")
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
                "An inspector id must be an unsigned decimal integer string.",
                line);
        }
    }

    private static void ValidateNetworkRequest(
        JsonElement value,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
            line,
            path);
        ValidateInspectorId(value, issues, line, path);
        ValidateOptionalObject(
            value,
            "initiator",
            (initiator, list, number, initiatorPath) => ValidateShape(
                initiator,
                [
                    NullableString("type"),
                    NullableString("url"),
                    NullableInteger("line", nonnegative: true),
                    NullableInteger("column", nonnegative: true),
                    RequiredBoolean("linkPreload")
                ],
                list,
                number,
                initiatorPath),
            issues,
            line,
            path);
        ValidateNetworkHeaders(
            value, "headers", "headerCount", "headersTruncated", issues, line, path);
    }

    private static void ValidateNetworkResponse(
        JsonElement value,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
            line,
            path);
        ValidateOptionalObject(
            value, "remoteAddress", ValidateNetworkRemoteAddress, issues, line, path);
        ValidateNetworkHeaders(
            value, "headers", "headerCount", "headersTruncated", issues, line, path);
        ValidateOptionalObject(
            value,
            "timing",
            (timing, list, number, timingPath) => ValidateNetworkTiming(
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
                number,
                timingPath),
            issues,
            line,
            path);
    }

    private static void ValidateNetworkRemoteAddress(
        JsonElement value,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
            line,
            path);

    private static void ValidateNetworkTiming(
        JsonElement value,
        string startProperty,
        IReadOnlyList<string> phases,
        ICollection<ArchiveValidationIssue> issues,
        long line,
        string path)
    {
        List<PropertyRule> rules = [RequiredNullableNumber(startProperty)];
        rules.AddRange(phases.Select(phase => RequiredNullableNumber(phase)));
        ValidateShape(value, rules, issues, line, path);
    }

    // Checks one header list against its count and truncation flag, and checks
    // that every withheld value is null with a reason and that no credential
    // header value was written.
    private static void ValidateNetworkHeaders(
        JsonElement parent,
        string listProperty,
        string countProperty,
        string truncatedProperty,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
                line,
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
                    "A withheld header value is null with a reason, and a recorded one has no reason.",
                    line);
            }

            var name = ReadString(header, "name");
            if (name is not null && NetworkCredentialHeaders.Contains(name) && hasValue)
            {
                AddError(
                    issues,
                    "browser-network-credential-header-value",
                    $"{headerPath}/value",
                    $"The value of a '{name}' header must not be recorded.",
                    line);
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
                $"{countProperty} must equal the listed headers unless the list is marked truncated, in which case it must exceed them.",
                line);
        }
    }

    private static void ValidateBrowserLayoutCheckpointStarted(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "rendering-update"),
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
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
                line,
                "events.ndjson#/payload/viewport");
        }
        if (payload.TryGetProperty("scrollOffset", out var scroll) &&
            scroll.ValueKind == JsonValueKind.Object)
        {
            ValidateShape(
                scroll,
                [RequiredNumber("x"), RequiredNumber("y")],
                issues,
                line,
                "events.ndjson#/payload/scrollOffset");
        }
        var previous = ReadString(payload, "previousCheckpointId");
        if (previous is not null && previous == ReadString(payload, "checkpointId"))
        {
            AddError(
                issues,
                "browser-layout-checkpoint-previous-self",
                "events.ndjson#/payload/previousCheckpointId",
                "A layout checkpoint names itself as its previous checkpoint.",
                line);
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
                    "events.ndjson#/payload/styleProperties",
                    "The style property list must be nonempty and hold no duplicates.",
                    line);
            }
        }
    }

    private static void ValidateBrowserLayoutCheckpointNode(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredInteger("nodeIndex", nonnegative: true),
                RequiredInteger("nodeId", positive: true),
                RequiredEnum("nodeType", "element", "text"),
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
                    "must be an object of string or null values, or null")
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);

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
                line,
                "events.ndjson#/payload/boundingClientRect");
        }
        if (payload.TryGetProperty("layoutObjectPresent", out var layoutObject) &&
            IsBoolean(layoutObject) &&
            layoutObject.GetBoolean() != hasRect)
        {
            AddError(
                issues,
                "browser-layout-rect-inconsistent",
                "events.ndjson#/payload/boundingClientRect",
                "A bounding rectangle must be present exactly when the node has " +
                    "a layout object.",
                line);
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
                    "events.ndjson#/payload/nodeType",
                    "A text node record must have a layout object and no " +
                        "computed style.",
                    line);
            }
        }
    }

    private static void ValidateBrowserLayoutCheckpointCompleted(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "rendering-update"),
                RequiredInteger("nodeCount", nonnegative: true),
                RequiredBoolean("truncated"),
                RequiredInteger("maximumNodes", positive: true)
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
        var count = ReadNullableInteger(payload, "nodeCount");
        var maximum = ReadNullableInteger(payload, "maximumNodes");
        if (count is not null && maximum is not null && count > maximum)
        {
            AddError(
                issues,
                "browser-layout-node-count-over-maximum",
                "events.ndjson#/payload/nodeCount",
                $"The checkpoint reports {count} nodes, more than the stated " +
                    $"maximum of {maximum}.",
                line);
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
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        var consistent = properties.All(property =>
            (payload.TryGetProperty(property, out var value) &&
                value.ValueKind != JsonValueKind.Null) == present);
        if (!consistent)
        {
            AddError(
                issues,
                code,
                $"events.ndjson#/payload/{properties[0]}",
                message,
                line);
        }
    }

    private static void ValidateOrderedRange(
        JsonElement payload,
        string startProperty,
        string endProperty,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        var start = ReadNullableInteger(payload, startProperty);
        var end = ReadNullableInteger(payload, endProperty);
        if (start is not null && end is not null && end < start)
        {
            AddError(
                issues,
                "browser-selection-range-reversed",
                $"events.ndjson#/payload/{endProperty}",
                $"Property '{endProperty}' ({end}) precedes " +
                    $"'{startProperty}' ({start}).",
                line);
        }
    }

    private static void ValidateBrowserDocumentCookieRead(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateBrowserLocationProperty(payload, issues, line);
        ValidateBrowserExecutionWorldProperty(payload, issues, line);
        ValidateCookieNameCount(
            payload, "cookieNames", "cookieNamesTruncated", issues, line);
    }

    private static void ValidateBrowserDocumentCookieWrite(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateBrowserLocationProperty(payload, issues, line);
        ValidateBrowserExecutionWorldProperty(payload, issues, line);
        ValidateCookieWriteAttributes(payload, documentCookie: true, issues, line);
    }

    private static void ValidateBrowserCookieStoreRequest(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateBrowserLocationProperty(payload, issues, line);
        ValidateBrowserExecutionWorldProperty(payload, issues, line);

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
                "events.ndjson#/payload/attributes",
                "A Cookie Store write reports its attributes and a read reports null attributes.",
                line);
        }

        if (hasAttributes)
        {
            ValidateCookieWriteAttributes(payload, documentCookie: false, issues, line);
        }
    }

    private static void ValidateBrowserCookieStoreResult(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);

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
                "events.ndjson#/payload",
                "A Cookie Store read result reports cookie names and a write result reports success.",
                line);
        }

        if (hasNames)
        {
            ValidateCookieNameCount(
                payload, "cookieNames", "cookieNamesTruncated", issues, line);
        }
    }

    private static void ValidateBrowserCookieStoreChange(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
    }

    private static void ValidateBrowserCookieAccess(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);

        var navigationObserver = payload.TryGetProperty("observer", out var observer) &&
            observer.ValueKind == JsonValueKind.String &&
            observer.GetString() == "navigation";
        if (navigationObserver != HasNonnullProperty(payload, "navigationId"))
        {
            AddError(
                issues,
                "browser-cookie-access-observer",
                "events.ndjson#/payload/navigationId",
                "A navigation-observed cookie access names its navigation and a frame-observed one does not.",
                line);
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
                ValidateCookieAccessEntry(cookie, index, issues, line);
            }

            index++;
        }

        ValidateCookieNameCount(payload, "cookies", "cookiesTruncated", issues, line);
    }

    private static void ValidateCookieAccessEntry(
        JsonElement cookie,
        int index,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        var path = $"events.ndjson#/payload/cookies/{index}";
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
            line,
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
                    "A parsed cookie reports every attribute and an unparsed Set-Cookie line reports none.",
                    line);
            }
        }
    }

    private static void ValidateCookieWriteAttributes(
        JsonElement payload,
        bool documentCookie,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            line,
            "events.ndjson#/payload/attributes");
    }

    // A cookie list reports the full count and holds every entry unless it
    // says it was cut, so a reader can tell a short list from a cut one.
    private static void ValidateCookieNameCount(
        JsonElement payload,
        string listProperty,
        string truncatedProperty,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
                $"events.ndjson#/payload/{listProperty}",
                "cookieCount must equal the listed cookies unless the list is marked truncated, in which case it must exceed them.",
                line);
        }
    }

    private static void ValidateBrowserNavigation(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);

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
                "events.ndjson#/payload/parentFrameId",
                "A subframe must identify the same direct parent and owning document frame.",
                line);
        }

        if (frameType != "subframe" && parentFrameId is not null)
        {
            AddError(
                issues,
                "browser-navigation-main-frame-parent-present",
                "events.ndjson#/payload/parentFrameId",
                "A main frame must not identify a direct parent frame.",
                line);
        }

        if (frameType == "primary-main-frame" && !primaryPage)
        {
            AddError(
                issues,
                "browser-navigation-primary-page-mismatch",
                "events.ndjson#/payload/primaryPage",
                "A primary main frame must belong to the primary page.",
                line);
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
                    "events.ndjson#/payload/context",
                    "Navigation evidence must have browser-process provenance and page and frame identities.",
                    line);
            }

            if (frameType == "subframe" && pageId == frameId)
            {
                AddError(
                    issues,
                    "browser-navigation-subframe-page-mismatch",
                    "events.ndjson#/payload/context/pageId",
                    "A subframe must have distinct page and frame identities.",
                    line);
            }

            if (frameType != "subframe" && pageId != frameId)
            {
                AddError(
                    issues,
                    "browser-navigation-main-frame-page-mismatch",
                    "events.ndjson#/payload/context/pageId",
                    "A main frame must identify the root of its own page.",
                    line);
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
                    "events.ndjson#/payload/context/documentId",
                    "Document identity and token must be null before commit and after an uncommitted completion.",
                    line);
            }

            if (completed && committed &&
                (documentId is null || documentToken is null))
            {
                AddError(
                    issues,
                    "browser-navigation-committed-document-missing",
                    "events.ndjson#/payload/context/documentId",
                    "A committed navigation must identify its resulting document and document token.",
                    line);
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
                    "events.ndjson#/payload/rendererProcessId",
                    "Renderer process identity must be null before commit and after an uncommitted completion.",
                    line);
            }

            if (completed && committed &&
                (rendererProcessId is null || rendererProcessId <= 0))
            {
                AddError(
                    issues,
                    "browser-navigation-committed-renderer-missing",
                    "events.ndjson#/payload/rendererProcessId",
                    "A committed navigation must identify the renderer process hosting its document.",
                    line);
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
                "events.ndjson#/payload/navigationKind",
                "navigationKind must agree with sameDocument.",
                line);
        }

        if (completed &&
            payload.TryGetProperty("committed", out var committedValue) &&
            committedValue.ValueKind == JsonValueKind.False &&
            ReadString(payload, "outcome") != "not-committed")
        {
            AddError(
                issues,
                "browser-navigation-outcome-mismatch",
                "events.ndjson#/payload/outcome",
                "An uncommitted navigation must have outcome not-committed.",
                line);
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
                    "events.ndjson#/payload/outcome",
                    $"A committed navigation must have outcome {expectedOutcome}.",
                    line);
            }
        }
    }

    private static void ValidateBrowserDomCheckpointStarted(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "finished-parsing", "post-mutation"),
                RequiredInteger("maximumNodes", positive: true)
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
    }

    private static void ValidateBrowserDomCheckpointNode(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
                    "other"),
                RequiredString("nodeName")
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
    }

    private static void ValidateBrowserDomCheckpointNodeAttribute(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredInteger("nodeId", positive: true),
                RequiredInteger("attributeIndex", nonnegative: true),
                NullableString("attributeNamespace"),
                RequiredString("attributeName"),
                RequiredText("attributeValue"),
                RequiredInteger("attributeValueLength", nonnegative: true),
                RequiredBoolean("attributeValueTruncated"),
                RequiredInteger("maximumValueLength", positive: true)
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
        ValidateTruncatedText(
            payload,
            "attributeValue",
            "attributeValueLength",
            "attributeValueTruncated",
            issues,
            line);
    }

    // A checkpoint either covers no transition and names neither bound, or
    // covers at least one and names both. A half-stated range would leave a
    // consumer unable to decide whether a transition was covered.
    private static void ValidateTransitionCoverage(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
                "events.ndjson#/payload/coveredTransitionCount",
                "A checkpoint covering no transition named a transition bound.",
                line);
            return;
        }

        if (count > 0 && (!hasFirst || !hasLast))
        {
            AddError(
                issues,
                "browser-dom-checkpoint-coverage-inconsistent",
                "events.ndjson#/payload/coveredTransitionCount",
                "A checkpoint covering transitions did not name the first and " +
                "last transition it covers.",
                line);
        }
    }

    private static void ValidateBrowserDomCheckpointCompleted(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredString("checkpointId"),
                RequiredEnum("reason", "finished-parsing", "post-mutation"),
                RequiredInteger("nodeCount", nonnegative: true),
                RequiredBoolean("truncated"),
                RequiredInteger("maximumNodes", positive: true),
                RequiredInteger("attributeCount", nonnegative: true),
                RequiredBoolean("attributesTruncated"),
                RequiredInteger("maximumAttributesPerNode", positive: true),
                RequiredInteger("maximumValueLength", positive: true),
                RequiredInteger("coveredTransitionCount", nonnegative: true),
                NullableString("coveredTransitionFirstId"),
                NullableString("coveredTransitionLastId")
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
        ValidateTransitionCoverage(payload, issues, line);
    }

    private static void ValidateBrowserDomAttributeChanged(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
        ValidateTruncatedText(
            payload,
            "attributeValue",
            "attributeValueLength",
            "attributeValueTruncated",
            issues,
            line);
        ValidateTruncatedText(
            payload,
            "previousAttributeValue",
            "previousAttributeValueLength",
            "previousAttributeValueTruncated",
            issues,
            line);
        ValidateAttributeChangeTransition(payload, issues, line);
    }

    private static void ValidateBrowserDomCharacterDataChanged(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
        ValidateTruncatedText(
            payload,
            "text",
            "textLength",
            "textTruncated",
            issues,
            line);
        ValidateTruncatedText(
            payload,
            "previousText",
            "previousTextLength",
            "previousTextTruncated",
            issues,
            line);
    }

    private static void ValidateAttributeChangeTransition(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            "events.ndjson#/payload/changeType",
            $"An attribute change of type '{changeType}' does not carry the " +
            "value and previous value that change type requires.",
            line);
    }

    private static void ValidateTruncatedText(
        JsonElement payload,
        string textProperty,
        string lengthProperty,
        string truncatedProperty,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            $"events.ndjson#/payload/{lengthProperty}",
            $"Property '{lengthProperty}' reports {reportedLength} units for a " +
            $"recorded value of {recordedLength} units while " +
            $"'{truncatedProperty}' is {(isTruncated ? "true" : "false")}.",
            line);
    }

    private static void ValidateRendererDocumentContext(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
                "events.ndjson#/payload/context",
                "DOM checkpoint evidence must identify a renderer document and its Chromium document token.",
                line);
        }
    }

    // An accessibility checkpoint is taken by a renderer and names the Chromium
    // document token it serialized, but it carries no DOM document node
    // identity, because the serialization is not taken at a DOM checkpoint.
    private static void ValidateRendererTokenContext(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
                "events.ndjson#/payload/context",
                "Accessibility checkpoint evidence must identify a renderer " +
                    "and the Chromium document token it serialized.",
                line);
        }
    }

    private static void ValidateBrowserContextProperty(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            line,
            "events.ndjson#/payload/context");
    }

    private static void ValidateBrowserEventTargetProperty(
        JsonElement payload,
        string property,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        if (!payload.TryGetProperty(property, out var target) ||
            target.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ValidateBrowserEventTarget(
            target,
            issues,
            line,
            $"events.ndjson#/payload/{property}");
    }

    private static void ValidateBrowserEventTargetArrayProperty(
        JsonElement payload,
        string property,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
                    line,
                    $"events.ndjson#/payload/{property}/{index}");
            }
            index++;
        }
    }

    // Validates one EventTarget reference. A Node carries a DOM node
    // identifier; a Window or other non-Node EventTarget has none and carries a
    // target identifier instead, so the identity required depends on the kind.
    private static void ValidateBrowserEventTarget(
        JsonElement target,
        ICollection<ArchiveValidationIssue> issues,
        long line,
        string path)
    {
        ValidateShape(
            target,
            [
                RequiredEnum("kind", "node", "window", "other"),
                NullableString("interfaceName"),
                NullableString("targetId"),
                RequiredString("documentId"),
                NullableInteger("nodeId", nonnegative: true),
                NullableString("backendNodeId"),
                NullableString("tagName"),
                NullableString("elementId"),
                RequiredStringArray("classes")
            ],
            issues,
            line,
            path);

        var kind = ReadString(target, "kind");
        if (kind == "node" && !HasNonnullProperty(target, "nodeId"))
        {
            AddError(
                issues,
                "browser-event-target-identity",
                path,
                "a node event target must report its nodeId",
                line);
        }

        if (kind is "window" or "other")
        {
            if (HasNonnullProperty(target, "nodeId"))
            {
                AddError(
                    issues,
                    "browser-event-target-identity",
                    path,
                    $"a {kind} event target has no nodeId",
                    line);
            }

            if (!HasNonnullProperty(target, "targetId"))
            {
                AddError(
                    issues,
                    "browser-event-target-identity",
                    path,
                    $"a {kind} event target must report its targetId",
                    line);
            }
        }
    }

    private static void ValidateBrowserLocationProperty(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
            line,
            $"events.ndjson#/payload/{property}");
    }

    // A listener record that names a world must also report that world in its
    // context, because the context field is what correlates records from the
    // same world. A world named in only one of the two places would let a
    // consumer read two different answers from one record.
    private static void ValidateBrowserExecutionWorldProperty(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
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
            line,
            "events.ndjson#/payload/world");

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
                "events.ndjson#/payload/context/executionWorldId",
                "a record that names a world must report its executionWorldId",
                line);
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
                "events.ndjson#/payload/context/executionWorldId",
                $"a record whose world is {worldId} must report {expected}",
                line);
        }
    }

    private static void ValidateOmission(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line) =>
        ValidateShape(
            payload,
            [
                RequiredString("reason"),
                OptionalInteger("count", nonnegative: true),
                OptionalEnum("stream", "microphone", "system")
            ],
            issues,
            line);

    // A browser omission names how many records were lost and, when the
    // reporter knows which browser process lost them, carries that process
    // context. A reporter that cannot attribute the loss to one process omits
    // the context rather than naming a process it did not observe.
    private static void ValidateBrowserOmission(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateShape(
            payload,
            [
                RequiredString("reason"),
                OptionalInteger("count", nonnegative: true),
                OptionalNullableObject("context")
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
    }

    private static void ValidateIntegerRectangle(
        JsonElement value,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
            line,
            path);

    private static void ValidateNumberRectangle(
        JsonElement value,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
            line,
            path);

    private static void ValidateMonitor(
        JsonElement value,
        ICollection<ArchiveValidationIssue> issues,
        long line,
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
            line,
            path);
        ValidateOptionalObject(value, "bounds", ValidateIntegerRectangle, issues, line, path);
        ValidateOptionalObject(value, "workArea", ValidateIntegerRectangle, issues, line, path);
    }

    private static void ValidateOptionalObject(
        JsonElement parent,
        string property,
        Action<JsonElement, ICollection<ArchiveValidationIssue>, long, string> validate,
        ICollection<ArchiveValidationIssue> issues,
        long line,
        string parentPath = "events.ndjson#/payload")
    {
        if (parent.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            validate(value, issues, line, $"{parentPath}/{property}");
        }
    }

    private static void ValidateShape(
        JsonElement payload,
        IReadOnlyList<PropertyRule> rules,
        ICollection<ArchiveValidationIssue> issues,
        long line,
        string path = "events.ndjson#/payload")
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
                    $"Required payload property '{rule.Name}' is missing.",
                    line);
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
                    $"Payload property '{property.Name}' is not defined for this event type.",
                    line);
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
                    $"Payload property '{property.Name}' {rule.Expectation}.",
                    line);
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
        bool nonnegative = false) =>
        new(
            name,
            true,
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
        ICollection<ArchiveValidationIssue> issues,
        string code,
        string path,
        string message,
        long line) =>
        issues.Add(new ArchiveValidationIssue(
            code,
            ArchiveValidationSeverity.Error,
            path,
            message,
            line));

    private sealed record PropertyRule(
        string Name,
        bool Required,
        bool Nullable,
        Func<JsonElement, bool> Validate,
        string Expectation);
}
