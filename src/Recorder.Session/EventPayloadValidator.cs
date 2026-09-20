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
        "browser.listener",
        "browser.dispatch",
        "browser.timer",
        "browser.scheduler",
        "browser.navigation",
        "browser.dom",
        "browser.cookie"
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
            case ("browser.listener", "listener-registered"):
            case ("browser.listener", "listener-removed"):
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
            case ("browser.dom", "dom-checkpoint-completed"):
                ValidateBrowserDomCheckpointCompleted(payload, issues, lineNumber);
                break;
            case ("browser.cookie", "cookie-operation"):
                ValidateBrowserCookie(payload, issues, lineNumber);
                break;
            case ("window.foreground", "collector-omission"):
            case ("accessibility.uia.events", "collector-omission"):
            case ("graphics.desktop.frames", "collector-omission"):
            case ("audio.microphone", "collector-omission"):
            case ("audio.system", "collector-omission"):
            case ("browser.listener", "collector-omission"):
            case ("browser.dispatch", "collector-omission"):
            case ("browser.timer", "collector-omission"):
            case ("browser.scheduler", "collector-omission"):
            case ("browser.navigation", "collector-omission"):
            case ("browser.dom", "collector-omission"):
            case ("browser.cookie", "collector-omission"):
                ValidateOmission(payload, issues, lineNumber);
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
                NullableObject("location")
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateBrowserNodeProperty(payload, "target", issues, line);
        ValidateBrowserLocationProperty(payload, issues, line);
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
        ValidateBrowserNodeProperty(payload, "originalTarget", issues, line);
        ValidateBrowserNodeProperty(payload, "currentTarget", issues, line);
        ValidateBrowserNodeArrayProperty(payload, "composedPath", issues, line);
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

    private static void ValidateBrowserCookie(
        JsonElement payload,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        ValidateShape(
            payload,
            [
                RequiredObject("context"),
                RequiredEnum(
                    "operation",
                    "read",
                    "write",
                    "delete",
                    "send",
                    "receive",
                    "block"),
                RequiredString("name"),
                NullableString("domain"),
                NullableString("path"),
                NullableString("sameSite"),
                NullableBoolean("secure"),
                NullableBoolean("httpOnly"),
                NullableBoolean("partitioned"),
                RequiredString("source"),
                RequiredString("result"),
                NullableString("blockedReason")
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
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
                RequiredInteger("maximumNodes", positive: true)
            ],
            issues,
            line);
        ValidateBrowserContextProperty(payload, issues, line);
        ValidateRendererDocumentContext(payload, issues, line);
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

    private static void ValidateBrowserNodeProperty(
        JsonElement payload,
        string property,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        if (!payload.TryGetProperty(property, out var node) ||
            node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ValidateBrowserNode(node, issues, line, $"events.ndjson#/payload/{property}");
    }

    private static void ValidateBrowserNodeArrayProperty(
        JsonElement payload,
        string property,
        ICollection<ArchiveValidationIssue> issues,
        long line)
    {
        if (!payload.TryGetProperty(property, out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                ValidateBrowserNode(
                    node,
                    issues,
                    line,
                    $"events.ndjson#/payload/{property}/{index}");
            }
            index++;
        }
    }

    private static void ValidateBrowserNode(
        JsonElement node,
        ICollection<ArchiveValidationIssue> issues,
        long line,
        string path) =>
        ValidateShape(
            node,
            [
                RequiredString("documentId"),
                RequiredInteger("nodeId", nonnegative: true),
                NullableString("backendNodeId"),
                NullableString("tagName"),
                NullableString("elementId"),
                RequiredStringArray("classes")
            ],
            issues,
            line,
            path);

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
