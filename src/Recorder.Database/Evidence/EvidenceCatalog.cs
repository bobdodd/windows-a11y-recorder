namespace Recorder.Database.Evidence;

/// <summary>
/// The evidence model: which table holds the payload of each channel and
/// event type, and how each payload member is stored. Migration
/// 0003_evidence_tables.sql is generated from this catalog, and a test
/// requires the two to agree.
/// </summary>
internal static class EvidenceCatalog
{
    private const Presence R = Presence.Required;
    private const Presence N = Presence.Nullable;
    private const Presence O = Presence.Optional;

    // Identity tables. Each distinct value is stored once per recording.
    public static readonly EvidenceTable Texts = TextIdentity("recording_texts");

    public static readonly EvidenceTable Monitors = Identity(
        "monitors",
        Text("deviceName"),
        IntegerRectangle("bounds", R),
        IntegerRectangle("workArea", R),
        Bool("isPrimary"));

    public static readonly EvidenceTable Windows = Identity(
        "windows",
        BigInt("windowHandle"),
        BigInt("processId"),
        BigInt("threadId"),
        Name("processName", N),
        Text("processPath", N),
        Name("className", N));

    public static readonly EvidenceTable UiaElements = Identity(
        "uia_elements",
            BigInt("processId", N),
            BigInt("nativeWindowHandle", N),
            Text("automationId", N),
            Text("name", N),
            Name("className", N),
            Name("frameworkId", N),
            Name("controlType", N),
            Name("localizedControlType", N),
            Bool("hasKeyboardFocus", N),
            Bool("isKeyboardFocusable", N),
            Bool("isEnabled", N),
            Bool("isOffscreen", N),
            NumberRectangle("boundingRectangle", N),
            Name("propertySource", O),
        NameList("qualityFlags", R, "uia_element_quality_flags"));

    public static readonly EvidenceTable BrowserContexts = Identity(
        "browser_contexts",
        Text("browserInstanceId"),
        BigInt("processId"),
        Name("processType"),
        Text("profileId", N),
        Text("browserContextId", N),
        Text("pageId", N),
        Text("frameId", N),
        Text("documentId", N),
        Text("executionWorldId", N),
        Text("documentToken", N));

    public static readonly EvidenceTable BrowserEventTargets = Identity(
        "browser_event_targets",
        Name("kind"),
        Name("interfaceName", N),
        Text("targetId", N),
        Text("documentId", N),
        BigInt("nodeId", N),
        Text("backendNodeId", N),
        Name("tagName", N),
        Text("elementId", N),
        NameList("classes", R, "browser_event_target_classes"));

    public static readonly EvidenceTable ScriptLocations = Identity(
        "script_locations",
        Text("scriptId", N),
        Text("url", N),
        Int("line", N, "line_number"),
        Int("column", N, "column_number"),
        Text("functionName", N),
        Text("sourceHash", N));

    public static readonly EvidenceTable ExecutionWorlds = Identity(
        "execution_worlds",
        Name("kind"),
        Int("blinkWorldId"),
        Text("name", N),
        Text("stableId", N));

    public static readonly EvidenceTable ExecutionScopes = Identity(
        "execution_scopes",
        Name("contextKind"),
        Text("workerToken", N),
        Text("globalObjectUrl", N));

    // Evidence tables.
    public static readonly EvidenceTable CollectorLifecycle = Evidence(
        "collector_lifecycle_events",
        Name("action"),
        Name("state"),
        Scalar("utc", ScalarType.Utc));

    public static readonly EvidenceTable SessionMarkers = Evidence(
        "session_markers",
        Text("note", N));

    public static readonly EvidenceTable RawKeyboard = Evidence(
        "raw_keyboard_events",
        BigInt("deviceHandle"),
        Int("makeCode"),
        Int("flags"),
        Int("virtualKey"),
        Int("message"),
        BigInt("extraInformation"));

    public static readonly EvidenceTable RawMouse = Evidence(
        "raw_mouse_events",
        BigInt("deviceHandle"),
        Name("movementMode"),
        Int("deltaX"),
        Int("deltaY"),
        Int("buttonFlags"),
        Int("buttonData"),
        BigInt("rawButtons"),
        BigInt("extraInformation"),
        Int("cursorX"),
        Int("cursorY"),
        BigInt("foregroundProcessId"));

    public static readonly EvidenceTable ForegroundWindows = Evidence(
        "foreground_windows",
        Name("reason"),
        BigInt("eventThreadId", N),
        BigInt("nativeEventTimeMilliseconds", N),
        new GroupField("window_key", Windows),
        Text("title", N),
        Bool("isVisible"),
        Bool("isMinimized"),
        Bool("isMaximized"),
        Bool("isCloaked", N),
        Int("dpi", N),
        IntegerRectangle("bounds", N),
        new IdentityField("monitor", N, Monitors));

    // Added by 0021_windows_preferences.sql. The Windows settings at the
    // start and stop of a recording, and each change, one setting a record.
    // See docs/architecture/accessibility-preferences.md.
    public static readonly EvidenceTable WindowsPreferenceSnapshots = Evidence(
        "windows_preference_snapshots",
        Name("reason"),
        new InlineField(
            "uiSettingsEvents",
            R,
            [.. Recorder.Contracts.WindowsPreferenceSettings.UiSettingsEvents.Select(name => Bool(name))]),
        new InlineField("settings", R, WindowsSettings(R, "windows_preference_snapshot_monitors")));

    public static readonly EvidenceTable WindowsPreferenceChanges = Evidence(
        "windows_preference_changes",
        Name("setting"),
        new InlineField("previous", R, WindowsSettings(O, "windows_preference_change_previous_monitors")),
        new InlineField("current", R, WindowsSettings(O, "windows_preference_change_current_monitors")),
        new InlineField(
            "notice",
            R,
            [Name("kind"), BigInt("uiAction", N), Text("area", N), Text("source", N)]));

    // Added by 0022_magnifier_changes.sql. A change of the Magnifier's
    // readings between two desktop frames, with both frames' readings as a
    // desktop frame holds them. See
    // docs/architecture/accessibility-preferences.md.
    public static readonly EvidenceTable MagnifierChangeRecords = Evidence(
        "magnifier_changes",
        BigInt("frameSequence"),
        BigInt("previousFrameAt"),
        new InlineField(
            "changed",
            R,
            [.. Recorder.Contracts.MagnifierChanges.Parts.Select(part => Bool(part))]),
        new InlineField("previous", R, MagnifierReadings()),
        new InlineField("current", R, MagnifierReadings()));

    // Added by 0023_browser_preferences.sql (protocol 0.56). The listed
    // browser preferences of a profile and their changes, the preferences
    // each page's view is sent, and each zoom level change. See
    // docs/architecture/accessibility-preferences.md, "Stage 2".
    public static readonly EvidenceTable BrowserPreferenceSnapshots = Evidence(
        "browser_preference_snapshots",
        Context(),
        Text("profileDirectory"),
        Bool("newProfile"),
        new InlineField("preferences", R, BrowserPreferenceReadings(R, "browser_preference_snapshot_text_lists")));

    public static readonly EvidenceTable BrowserPreferenceChanges = Evidence(
        "browser_preference_changes",
        Context(),
        Text("profileDirectory"),
        Name("preference"),
        new InlineField("previous", R, BrowserPreferenceReadings(O, "browser_preference_change_previous_text_lists")),
        new InlineField("current", R, BrowserPreferenceReadings(O, "browser_preference_change_current_text_lists")));

    public static readonly EvidenceTable BrowserWebPreferencesSent = Evidence(
        "browser_web_preferences_sent",
        Context(),
        Int("pageFrameTreeNodeId"),
        Bool("primaryPage"),
        Int("rendererProcessId"),
        Text("viewId"),
        Name("point"),
        Bool("first"),
        new InlineField(
            "fields",
            R,
            [.. Recorder.Contracts.BrowserPreferenceSettings.Page.Select(setting => BrowserPreferenceValue(setting, setting.Name, O, null))]));

    public static readonly EvidenceTable BrowserZoomLevelChanges = Evidence(
        "browser_zoom_level_changes",
        Context(),
        Name("mode"),
        Bool("followsDefault"),
        Text("host"),
        Text("scheme"),
        Double("zoomLevel"),
        Double("zoomPercent"));

    public static readonly EvidenceTable UiaEvents = Evidence(
        "uia_events",
            Name("eventId"),
            Name("changeType", N),
            new ListField(
                "runtimeId",
                N,
                ScalarList("uia_event_runtime_ids", ScalarType.BigInt)),
            Text("newValue", N),
            new IdentityField("element", R, UiaElements));

    public static readonly EvidenceTable DesktopFrames = Evidence(
        "desktop_frames",
            Text("path"),
            Int("x"),
            Int("y"),
            Int("width"),
            Int("height"),
            Int("stride"),
            Name("pixelFormat"),
            Name("encodedFormat"),
            BigInt("byteLength"),
            BigInt("captureDurationNanoseconds"),
            Int("framesPerSecond"),
            Name("backend"),
            Int("monitorCount", N),
            Text("fallbackReason", N),
            BigInt("gdiFallbackFrameCount"),
            Name("frameSelection", O),
            new ListField(
                "monitorFrames",
                O,
                new EvidenceTable(
                    "desktop_frame_monitors",
                    TableKind.Child,
                    [
                        BigInt("monitorHandle"),
                        Int("x"),
                        Int("y"),
                        Int("width"),
                        Int("height"),
                        BigInt("systemRelativeTimeTicks", N),
                        BigInt("compositedAtNanoseconds", N),
                        BigInt("dequeuedAtNanoseconds", N),
                        Int("tryGetNextFrameAttempts", N),
                        BigInt("supersededFrameCount", O),
                        Bool("reusedPreviousImage", O)
                    ])),
            // Added by 0019_desktop_frame_magnification.sql.
            new InlineField(
                "fullscreenMagnification",
                O,
                [
                    Double("level", N),
                    Int("x", N),
                    Int("y", N),
                    Text("problem", N)
                ]),
            // Added by 0020_desktop_frame_color_effect.sql.
            new InlineField(
                "fullscreenColorEffect",
                O,
                [
                    new ArrayField("matrix", N, ScalarType.Double),
                    Text("problem", N)
                ]));

    public static readonly EvidenceTable AudioStreams = Evidence(
        "audio_stream_events",
        Name("stream"),
        Text("path"),
        Text("device"),
        Double("endpointVolumeScalar", O),
        new InlineField(
            "format",
            R,
            [
                Name("encoding"),
                Int("sampleRate"),
                Int("channels"),
                Int("bitsPerSample"),
                Int("blockAlign"),
                Int("averageBytesPerSecond")
            ]),
        BigInt("dataBytes"),
        BigInt("buffersObserved"),
        BigInt("buffersDropped"),
        Double("peakAmplitude", O),
        Double("peakDbfs", O),
        Double("rmsAmplitude", O),
        Double("rmsDbfs", O));

    public static readonly EvidenceTable AudioBuffers = Evidence(
        "audio_buffers",
        Name("stream"),
        new IdentityField("path", R, Texts),
        BigInt("bufferSequence"),
        BigInt("dataByteOffset"),
        BigInt("byteLength"),
        BigInt("sampleFrames"),
        BigInt("durationNanoseconds"),
        BigInt("estimatedFirstSampleMonotonicNanoseconds"),
        BigInt("callbackMonotonicNanoseconds"));

    public static readonly EvidenceTable AudioStreamErrors = Evidence(
        "audio_stream_errors",
        Name("stream"),
        Name("errorType", N),
        Text("message", N));

    public static readonly EvidenceTable BrowserConnections = Evidence(
        "browser_connections",
        Name("protocolVersion"),
        Text("browserInstanceId"),
        BigInt("processId"),
        Name("processType"),
        Name("chromiumVersion"),
        BigInt("parentProcessId", N),
        BigInt("childProcessId", N));

    public static readonly EvidenceTable BrowserExits = Evidence(
        "browser_exits",
        Text("browserInstanceId"),
        BigInt("processId"),
        BigInt("exitCode"),
        Text("exitCodeHex"),
        Scalar("exitedUtc", ScalarType.Utc, N),
        Bool("requestedByRecorder"));

    public static readonly EvidenceTable BrowserClockSynchronizations = Evidence(
        "browser_clock_synchronizations",
        Name("protocolVersion"),
        Text("browserInstanceId"),
        BigInt("processId"),
        Name("processType"),
        BigInt("parentProcessId", N),
        BigInt("childProcessId", N),
        Text("clockMappingId"),
        Text("monotonicFrequency"),
        BigInt("uncertaintyNanoseconds"));

    public static readonly EvidenceTable BrowserListeners = Evidence(
        "browser_listener_events",
        new IdentityField("context", R, BrowserContexts),
        Text("listenerId"),
        Name("eventName"),
        Name("registrationKind"),
        new IdentityField("target", R, BrowserEventTargets),
        Bool("capture"),
        Bool("passive"),
        Bool("once"),
        new IdentityField("location", N, ScriptLocations),
        new IdentityField("world", N, ExecutionWorlds),
        new IdentityField("scope", O, ExecutionScopes));

    public static readonly EvidenceTable BrowserDispatches = Evidence(
        "browser_dispatch_events",
        new IdentityField("context", R, BrowserContexts),
        Text("dispatchId"),
        Name("eventName"),
        Bool("trusted"),
        new IdentityField("originalTarget", N, BrowserEventTargets),
        new ListField(
            "composedPath",
            R,
            new EvidenceTable(
                "browser_dispatch_path_targets",
                TableKind.Child,
                [new IdentityField(string.Empty, R, BrowserEventTargets, "target_key")],
                scalarItem: true)),
        Name("phase"),
        Text("listenerId", N),
        Bool("defaultPrevented"),
        Bool("propagationStopped"),
        Bool("immediatePropagationStopped"),
        Name("defaultAction", N),
        Name("outcome", N),
        new IdentityField("currentTarget", O, BrowserEventTargets),
        new ListField(
            "pathScopes",
            R,
            new EvidenceTable(
                "browser_dispatch_path_scopes",
                TableKind.Child,
                [
                    BigInt("treeScopeRootNodeId", N),
                    Name("shadowRootMode", N),
                    BigInt("targetNodeId", N),
                    BigInt("relatedTargetNodeId", N),
                    new ArrayField("visiblePathIndexes", R, ScalarType.Integer),
                    Int("unmatchedVisibleTargetCount")
                ])),
        new IdentityField("scope", O, ExecutionScopes));

    public static readonly EvidenceTable BrowserTimers = Evidence(
        "browser_timer_events",
        new IdentityField("context", R, BrowserContexts),
        Text("timerId"),
        Name("timerKind"),
        Double("requestedDelayMilliseconds", N),
        Double("effectiveDelayMilliseconds", N),
        Int("nestingLevel"),
        Bool("throttled", N),
        Name("pageLifecycleState"),
        new IdentityField("callbackLocation", N, ScriptLocations),
        Name("cancellationReason", N),
        Bool("didTimeout", O));

    public static readonly EvidenceTable BrowserSchedulerDeferrals = Evidence(
        "browser_scheduler_deferrals",
        new IdentityField("context", R, BrowserContexts),
        Name("queueName"),
        Int("queueType"),
        Name("throttlingType"),
        Text("desiredWakeUpTicks"),
        Text("allowedWakeUpTicks"),
        Double("deferralMilliseconds"),
        Bool("hasReadyTask"),
        Name("blockType"),
        Name("decisionBoundary"));

    public static readonly EvidenceTable BrowserNavigations = Evidence(
        "browser_navigations",
        new IdentityField("context", R, BrowserContexts),
        Text("parentFrameId", N),
        Text("parentOrOuterDocumentFrameId", N),
        Name("frameType"),
        Bool("primaryPage"),
        Text("navigationId"),
        new IdentityField("url", R, Texts),
        Name("navigationKind"),
        Bool("rendererInitiated"),
        Bool("sameDocument"),
        Bool("committed", N),
        Bool("errorPage", N),
        Int("netErrorCode", N),
        Name("outcome", N),
        BigInt("rendererProcessId", N));

    // One table for the omission records of every channel. Each channel's
    // omission states a subset of these members; the validator states which.
    public static readonly EvidenceTable AccessibilityCheckpointStarts = Evidence(
        "browser_accessibility_checkpoint_starts",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Name("reason"),
        Int("maximumNodes"),
        Int("updateCount"),
        Int("eventCount"));

    public static readonly EvidenceTable AccessibilityCheckpointNodes = Evidence(
        "browser_accessibility_checkpoint_nodes",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Int("nodeIndex"),
        BigInt("accessibilityNodeId"),
        BigInt("parentAccessibilityNodeId", N),
        BigInt("domNodeId", N),
        Int("role"),
        Name("roleName"),
        Text("name"),
        Text("description"),
        Text("serializedProperties"),
        Bool("focused"));

    public static readonly EvidenceTable AccessibilityCheckpointCompletions = Evidence(
        "browser_accessibility_checkpoint_completions",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Name("reason"),
        Int("nodeCount"),
        Bool("truncated"),
        Int("maximumNodes"),
        Int("updateCount"),
        Int("eventCount"));

    public static readonly EvidenceTable DomCheckpointStarts = Evidence(
        "browser_dom_checkpoint_starts",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Name("reason"),
        Name("walkReason", N),
        Int("maximumNodes"),
        Text("frameToken", O),
        Bool("mainFrame", O));

    public static readonly EvidenceTable DomCheckpointNodes = Evidence(
        "browser_dom_checkpoint_nodes",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Int("nodeIndex"),
        BigInt("nodeId"),
        BigInt("parentNodeId", N),
        Name("nodeType"),
        Name("nodeName"));

    public static readonly EvidenceTable DomCheckpointAttributes = Evidence(
        "browser_dom_checkpoint_attributes",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        BigInt("nodeId"),
        Int("attributeIndex"),
        Name("attributeNamespace", N),
        Name("attributeName"),
        Text("attributeValue"),
        Int("attributeValueLength"),
        Bool("attributeValueTruncated"),
        Int("maximumValueLength"));

    public static readonly EvidenceTable DomCheckpointShadowRoots = Evidence(
        "browser_dom_checkpoint_shadow_roots",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        BigInt("nodeId"),
        BigInt("hostNodeId"),
        Name("mode"),
        Bool("delegatesFocus"),
        Name("slotAssignment"),
        Bool("clonable"),
        Bool("serializable"),
        Bool("declarative"),
        Bool("availableToElementInternals"),
        Text("referenceTarget", N));

    public static readonly EvidenceTable DomCheckpointSlotAssignments = Evidence(
        "browser_dom_checkpoint_slot_assignments",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        BigInt("nodeId"),
        new ListField(
            "assignedNodeIds",
            R,
            ScalarList("browser_dom_checkpoint_slot_assigned_nodes", ScalarType.BigInt, N)),
        Int("assignedNodeCount"),
        Bool("assignedNodesTruncated"),
        Int("maximumAssignedNodes"),
        Bool("assignmentCurrent"));

    public static readonly EvidenceTable DomCheckpointCompletions = Evidence(
        "browser_dom_checkpoint_completions",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Name("reason"),
        Int("nodeCount"),
        Bool("truncated"),
        Int("maximumNodes"),
        Int("attributeCount"),
        Bool("attributesTruncated"),
        Int("maximumAttributesPerNode"),
        Int("maximumValueLength"),
        Int("coveredTransitionCount"),
        Text("coveredTransitionFirstId", N),
        Text("coveredTransitionLastId", N),
        Int("shadowRootCount"),
        Int("slotCount"));

    // Protocol 0.55 (page recreation slice 5a), created by
    // 0018_dom_frame_owners.sql.
    public static readonly EvidenceTable DomCheckpointFrameOwners = Evidence(
        "browser_dom_checkpoint_frame_owners",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        BigInt("ownerNodeId"),
        Text("frameToken"),
        Name("frameLocation"));

    public static readonly EvidenceTable DomFrameOwnerChanges = Evidence(
        "browser_dom_frame_owner_changes",
        new IdentityField("context", R, BrowserContexts),
        BigInt("ownerNodeId"),
        Text("frameToken", N),
        Name("frameLocation", N));

    public static readonly EvidenceTable DomAttributeChanges = Evidence(
        "browser_dom_attribute_changes",
        new IdentityField("context", R, BrowserContexts),
        Text("transitionId"),
        BigInt("nodeId"),
        Name("nodeName"),
        Name("attributeNamespace", N),
        Name("attributeName"),
        Name("changeType"),
        Text("attributeValue", N),
        Int("attributeValueLength", N),
        Bool("attributeValueTruncated"),
        Text("previousAttributeValue", N),
        Int("previousAttributeValueLength", N),
        Bool("previousAttributeValueTruncated"),
        Int("maximumValueLength"));

    public static readonly EvidenceTable DomCharacterDataChanges = Evidence(
        "browser_dom_character_data_changes",
        new IdentityField("context", R, BrowserContexts),
        Text("transitionId"),
        BigInt("nodeId"),
        BigInt("parentNodeId", N),
        Name("nodeType"),
        Text("text"),
        Int("textLength"),
        Bool("textTruncated"),
        Text("previousText"),
        Int("previousTextLength"),
        Bool("previousTextTruncated"),
        Int("maximumValueLength"));

    public static readonly EvidenceTable FocusChanges = Evidence(
        "browser_focus_changes",
        new IdentityField("context", R, BrowserContexts),
        BigInt("previousNodeId", N),
        BigInt("requestedNodeId", N),
        BigInt("focusedNodeId", N),
        Name("outcome"),
        BigInt("activeDescendantNodeId", N),
        Name("focusType"),
        Name("focusTrigger"),
        Bool("preventScroll"),
        Bool("focusVisible", N),
        new IdentityField("location", N, ScriptLocations),
        new IdentityField("world", N, ExecutionWorlds));

    public static readonly EvidenceTable SelectionChanges = Evidence(
        "browser_selection_changes",
        new IdentityField("context", R, BrowserContexts),
        Name("setBy"),
        Name("selectionType"),
        BigInt("anchorNodeId", N),
        Int("anchorOffset", N),
        BigInt("focusNodeId", N),
        Int("focusOffset", N),
        Bool("directional"),
        BigInt("textControlNodeId", N),
        Int("textControlSelectionStart", N),
        Int("textControlSelectionEnd", N),
        Name("textControlSelectionDirection", N),
        new IdentityField("location", N, ScriptLocations),
        new IdentityField("world", N, ExecutionWorlds));

    public static readonly EvidenceTable TextControlValueChanges = Evidence(
        "browser_text_control_value_changes",
        new IdentityField("context", R, BrowserContexts),
        BigInt("nodeId"),
        Name("controlType"),
        Name("source"),
        Text("value"),
        Int("valueLength"),
        Bool("valueTruncated"),
        Int("maximumValueLength"),
        Int("selectionStart"),
        Int("selectionEnd"),
        Name("selectionDirection"),
        new IdentityField("location", N, ScriptLocations),
        new IdentityField("world", N, ExecutionWorlds));

    public static readonly EvidenceTable ActiveDescendantReferences = Evidence(
        "browser_active_descendant_references",
        new IdentityField("context", R, BrowserContexts),
        BigInt("nodeId"),
        BigInt("referencedNodeId"),
        new IdentityField("location", N, ScriptLocations),
        new IdentityField("world", N, ExecutionWorlds));

    public static readonly EvidenceTable InteractionCheckpointStarts = Evidence(
        "browser_interaction_checkpoint_starts",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Text("sourceCheckpointId", N),
        Text("sourceChangeSetId", N),
        Name("sourceChannel"),
        Name("reason"),
        Bool("documentHasFocus"),
        BigInt("focusedNodeId", N),
        Bool("focusVisible"),
        BigInt("activeDescendantNodeId", N),
        Name("lastFocusType"),
        Name("selectionType"),
        BigInt("anchorNodeId", N),
        Int("anchorOffset", N),
        BigInt("focusNodeId", N),
        Int("focusOffset", N),
        Bool("directional"),
        Int("maximumTextControls"),
        Int("maximumValueLength"));

    public static readonly EvidenceTable InteractionCheckpointTextControls = Evidence(
        "browser_interaction_checkpoint_text_controls",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Int("textControlIndex"),
        BigInt("nodeId"),
        Name("controlType"),
        Text("value"),
        Int("valueLength"),
        Bool("valueTruncated"),
        Int("selectionStart"),
        Int("selectionEnd"),
        Name("selectionDirection"));

    public static readonly EvidenceTable InteractionCheckpointCompletions = Evidence(
        "browser_interaction_checkpoint_completions",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Int("textControlCount"),
        Bool("truncated"),
        Int("maximumTextControls"));

    /// <summary>
    /// A distinct computed style of a recording, stored once and referred to
    /// by every layout node that reports it. See
    /// Migrations/0010_shared_computed_styles.sql.
    /// </summary>
    public static readonly EvidenceTable ComputedStyles = new(
        "browser_computed_styles",
        TableKind.Identity,
        [
            new MapField(
                string.Empty,
                R,
                new EvidenceTable(
                    "browser_computed_style_entries",
                    TableKind.Child,
                    [Text(string.Empty, N, "value")],
                    scalarItem: true,
                    mapEntry: true))
        ],
        scalarItem: true);

    public static readonly EvidenceTable LayoutCheckpointStarts = Evidence(
        "browser_layout_checkpoint_starts",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Name("reason"),
        Name("walkReason", N),
        Text("previousCheckpointId", N),
        Int("styleResolutionCount"),
        Int("layoutCount"),
        new InlineField("viewport", R, [Double("width"), Double("height")]),
        new InlineField("scrollOffset", R, [Double("x"), Double("y")]),
        Double("devicePixelRatio"),
        Double("layoutZoomFactor"),
        Int("maximumNodes"),
        NameList("styleProperties", R, "browser_layout_checkpoint_style_properties"));

    public static readonly EvidenceTable LayoutCheckpointNodes = Evidence(
        "browser_layout_checkpoint_nodes",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Int("nodeIndex"),
        BigInt("nodeId"),
        Name("nodeType"),
        Name("nodeName"),
        Bool("layoutObjectPresent"),
        Bool("displayLocked"),
        NumberRectangle("boundingClientRect", N),
        new IdentityField("computedStyle", N, ComputedStyles),
        new InlineField(
            "pseudoElement",
            N,
            [
                BigInt("originatingNodeId", N),
                Name("pseudoType"),
                Text("generatedText"),
                Int("generatedTextLength"),
                Bool("generatedTextTruncated")
            ]),
        BigInt("shadowHostNodeId", N),
        Name("shadowRootMode", N));

    public static readonly EvidenceTable LayoutCheckpointCompletions = Evidence(
        "browser_layout_checkpoint_completions",
        new IdentityField("context", R, BrowserContexts),
        Text("checkpointId"),
        Name("reason"),
        Int("nodeCount"),
        Bool("truncated"),
        Int("maximumNodes"),
        Int("pseudoElementCount"),
        Int("shadowRootCount"));

    public static readonly EvidenceTable PresentationRequests = Evidence(
        "browser_presentation_requests",
        new IdentityField("context", R, BrowserContexts),
        Text("requestId"),
        Name("widgetKind", N),
        Text("frameSinkId", N),
        Text("localRootFrameToken", N),
        Text("layoutCheckpointId", N),
        Text("layoutChangeSetId", N),
        Bool("queued"),
        Name("notQueuedReason", N),
        BigInt("sourceFrameNumber", N),
        Bool("isMainFrameWidget", N),
        Bool("highResolutionTicks"),
        Int("maximumNotSwappedRecords"));

    public static readonly EvidenceTable PresentationsNotSwapped = Evidence(
        "browser_presentations_not_swapped",
        new IdentityField("context", R, BrowserContexts),
        Text("requestId"),
        Name("widgetKind", N),
        Text("frameSinkId", N),
        Text("localRootFrameToken"),
        Name("reason"),
        Name("action"),
        Int("notSwappedIndex"),
        Int("notSwappedCount"),
        Text("timestampTicks", N),
        Text("timestampTimeTicksMicroseconds", N));

    public static readonly EvidenceTable PresentationSwaps = Evidence(
        "browser_presentation_swaps",
        new IdentityField("context", R, BrowserContexts),
        Text("requestId"),
        Name("widgetKind", N),
        Text("frameSinkId", N),
        Text("localRootFrameToken"),
        Text("frameToken"),
        Int("notSwappedCount"));

    public static readonly EvidenceTable PresentationFeedback = Evidence(
        "browser_presentation_feedback",
        new IdentityField("context", R, BrowserContexts),
        Text("requestId"),
        Name("widgetKind", N),
        Text("frameSinkId", N),
        Text("localRootFrameToken"),
        Text("frameToken"),
        Text("presentedTicks", N),
        Text("presentedTimeTicksMicroseconds", N),
        Text("intervalMicroseconds"),
        NameList("flags", R, "browser_presentation_feedback_flags"),
        Text("receivedCompositorFrameTicks", N),
        Text("drawStartTicks", N),
        Text("swapStartTicks", N),
        Text("swapEndTicks", N),
        Bool("highResolutionTicks"),
        Int("notSwappedCount"));

    public static readonly EvidenceTable NetworkScopes = Identity(
        "browser_network_scopes",
        Name("contextKind"),
        Text("workerToken", N),
        Text("globalObjectUrl", N));

    public static readonly EvidenceTable DocumentCookieReads = Evidence(
        "browser_document_cookie_reads",
        Context(),
        Text("accessId"),
        Text("cookieUrl", N),
        Name("outcome"),
        Name("servedFrom", N),
        Int("cookieCount"),
        TextList("cookieNames", R, "browser_document_cookie_read_names"),
        Bool("cookieNamesTruncated"),
        ScriptLocation(),
        World());

    public static readonly EvidenceTable DocumentCookieWrites = Evidence(
        "browser_document_cookie_writes",
        Context(),
        Text("accessId"),
        Text("cookieUrl", N),
        Name("outcome"),
        Text("name"),
        new InlineField(
            "attributes",
            R,
            [
                Text("domain", N),
                Text("path", N),
                Name("sameSite", N),
                Bool("partitioned"),
                Bool("expiresPresent"),
                Bool("secure"),
                Bool("httpOnly"),
                Bool("maxAgePresent"),
                NameList("attributeNames", R, "browser_document_cookie_write_attribute_names")
            ]),
        ScriptLocation(),
        World());

    public static readonly EvidenceTable CookieStoreRequests = Evidence(
        "browser_cookie_store_requests",
        Context(),
        Text("requestId"),
        Name("method"),
        Name("contextKind"),
        Name("outcome"),
        Text("name", N),
        Text("url", N),
        new InlineField(
            "attributes",
            N,
            [
                Text("domain", N),
                Text("path", N),
                Name("sameSite", N),
                Bool("partitioned"),
                Bool("expiresPresent")
            ]),
        ScriptLocation(),
        World());

    public static readonly EvidenceTable CookieStoreResults = Evidence(
        "browser_cookie_store_results",
        Context(),
        Text("requestId"),
        Name("method"),
        Name("outcome"),
        Bool("success", N),
        Int("cookieCount", N),
        TextList("cookieNames", N, "browser_cookie_store_result_names"),
        Bool("cookieNamesTruncated", N));

    public static readonly EvidenceTable CookieStoreChanges = Evidence(
        "browser_cookie_store_changes",
        Context(),
        Name("contextKind"),
        Text("name"),
        Text("domain"),
        Text("path"),
        Name("cause"),
        Bool("dispatched"));

    public static readonly EvidenceTable CookieAccesses = Evidence(
        "browser_cookie_accesses",
        Context(),
        Name("observer"),
        Text("navigationId", N),
        BigInt("rendererProcessId", N),
        Name("accessType"),
        Text("url"),
        Text("frameOrigin", N),
        Text("topFrameOrigin", N),
        Text("requestId", N),
        Bool("adTagged"),
        Int("cookieCount"),
        Cookies("browser_cookie_access_cookies"),
        Bool("cookiesTruncated"));

    public static readonly EvidenceTable NetworkRequests = Evidence(
        "browser_network_requests",
        Context(),
        Scope(),
        NetworkRequest("request", "browser_network_request_headers"),
        Bool("redirect"),
        NetworkResponse("redirectResponse", N, "browser_network_request_redirect_headers", "redirect"),
        ScriptLocation(),
        World());

    public static readonly EvidenceTable NetworkResponses = Evidence(
        "browser_network_responses",
        Context(),
        Scope(),
        Text("inspectorId"),
        Text("requestId", N),
        Name("responseSource"),
        NetworkResponse("response", R, "browser_network_response_headers"));

    public static readonly EvidenceTable NetworkRequestFinishes = Evidence(
        "browser_network_request_finishes",
        Context(),
        Scope(),
        Text("inspectorId"),
        Double("encodedDataLength", N),
        Double("decodedBodyLength"),
        Double("finishBeforeRecordMilliseconds", N));

    public static readonly EvidenceTable NetworkRequestFailures = Evidence(
        "browser_network_request_failures",
        Context(),
        Scope(),
        Text("inspectorId"),
        Text("url"),
        Int("netError"),
        Name("netErrorName", N),
        Bool("cancellation"),
        Bool("timeout"),
        Bool("accessCheck"),
        Bool("blockedByResponse"),
        Bool("blockedByOrb"),
        Bool("hasCopyInCache"),
        Bool("cancelledFromHttpError"),
        Bool("internal"),
        Name("blockedReason", N),
        new InlineField("corsError", N, [Name("error"), Text("failedParameter", N)]));

    public static readonly EvidenceTable NetworkMemoryCacheHits = Evidence(
        "browser_network_memory_cache_hits",
        Context(),
        Scope(),
        Bool("staticData"),
        NetworkRequest("request", "browser_network_memory_cache_hit_request_headers"),
        NetworkResponse("response", R, "browser_network_memory_cache_hit_response_headers"));

    public static readonly EvidenceTable NetworkRequestHeadersSent = new(
        "browser_network_request_headers_sent",
        TableKind.Evidence,
        [
            Context(),
            Text("devtoolsAgentId", N),
            Text("requestId"),
            .. Headers("headers", "headerCount", "headersTruncated", "browser_network_request_headers_sent_headers"),
            Int("cookieCount"),
            Cookies("browser_network_sent_request_cookies"),
            Bool("cookiesTruncated"),
            Double("sentBeforeRecordMilliseconds", N)
        ]);

    public static readonly EvidenceTable NetworkResponseHeadersReceived = new(
        "browser_network_response_headers_received",
        TableKind.Evidence,
        [
            Context(),
            Text("devtoolsAgentId", N),
            Text("requestId"),
            .. Headers("headers", "headerCount", "headersTruncated", "browser_network_response_headers_received_headers"),
            Int("cookieCount"),
            Cookies("browser_network_received_response_cookies"),
            Bool("cookiesTruncated"),
            Int("status")
        ]);

    public static readonly EvidenceTable NetworkNavigationResponses = new(
        "browser_network_navigation_responses",
        TableKind.Evidence,
        [
            Context(),
            Text("navigationId"),
            Text("requestId", N),
            Text("url"),
            Name("method"),
            Bool("committed"),
            Bool("errorPage"),
            Bool("sameDocument"),
            Bool("download"),
            Bool("backForwardCache"),
            Int("netError"),
            Name("netErrorName", N),
            TextList("redirectChain", R, "browser_network_navigation_redirect_chains"),
            .. Headers(
                "requestHeaders",
                "requestHeaderCount",
                "requestHeadersTruncated",
                "browser_network_navigation_request_headers"),
            new InlineField(
                "response",
                N,
                [
                    Int("status"),
                    Text("statusText"),
                    Name("mimeType", N),
                    Bool("wasCached"),
                    RemoteAddress(),
                    Name("connectionInfo", N),
                    .. Headers("headers", "headerCount", "headersTruncated", "browser_network_navigation_response_headers")
                ]),
            Timing("navigationStartBeforeRecordMilliseconds", NavigationTimingPhases)
        ]);

    public static readonly EvidenceTable WebSocketCreations = Evidence(
        "browser_websocket_creations",
        Context(),
        Scope(),
        ScriptLocation(),
        World(),
        Text("inspectorId"),
        Text("url"),
        Text("requestedProtocols", N));

    public static readonly EvidenceTable WebSocketHandshakeRequests = new(
        "browser_websocket_handshake_requests",
        TableKind.Evidence,
        [
            Context(),
            Scope(),
            Text("inspectorId"),
            Text("url"),
            TextList("cookieNames", R, "browser_websocket_handshake_request_cookie_names"),
            .. Headers("headers", "headerCount", "headersTruncated", "browser_websocket_handshake_request_headers")
        ]);

    public static readonly EvidenceTable WebSocketHandshakeResponses = new(
        "browser_websocket_handshake_responses",
        TableKind.Evidence,
        [
            Context(),
            Scope(),
            Text("inspectorId"),
            .. RealtimeResponse("browser_websocket_handshake_response"),
            Text("extensions", N)
        ]);

    public static readonly EvidenceTable WebSocketMessagesSent = Evidence(
        "browser_websocket_messages_sent",
        Context(),
        Scope(),
        ScriptLocation(),
        World(),
        Text("inspectorId"),
        Name("opcode"),
        Double("payloadLength"),
        RealtimeText("payload", N, "browser_websocket_message_sent_withheld"));

    public static readonly EvidenceTable WebSocketMessagesReceived = Evidence(
        "browser_websocket_messages_received",
        Context(),
        Scope(),
        Text("inspectorId"),
        Name("opcode"),
        Double("payloadLength"),
        RealtimeText("payload", N, "browser_websocket_message_received_withheld"));

    public static readonly EvidenceTable WebSocketCloseRequests = Evidence(
        "browser_websocket_close_requests",
        Context(),
        Scope(),
        ScriptLocation(),
        World(),
        Text("inspectorId"),
        Int("code", N),
        RealtimeText("reason", R, "browser_websocket_close_request_withheld"));

    public static readonly EvidenceTable WebSocketErrors = Evidence(
        "browser_websocket_errors",
        Context(),
        Scope(),
        Text("inspectorId"),
        Text("message"));

    public static readonly EvidenceTable WebSocketClosures = Evidence(
        "browser_websocket_closures",
        Context(),
        Scope(),
        Text("inspectorId"),
        Name("cause"),
        Bool("wasClean", N),
        Int("code", N),
        RealtimeText("reason", N, "browser_websocket_closure_withheld"));

    public static readonly EvidenceTable EventSourceMessages = Evidence(
        "browser_event_source_messages",
        Context(),
        Scope(),
        Text("inspectorId"),
        Text("url"),
        Text("eventType"),
        RealtimeText("lastEventId", R, "browser_event_source_last_event_id_withheld"),
        Double("dataLength"),
        RealtimeText("data", R, "browser_event_source_data_withheld"));

    public static readonly EvidenceTable WebTransportCreations = Evidence(
        "browser_web_transport_creations",
        Context(),
        Scope(),
        ScriptLocation(),
        World(),
        Text("transportId"),
        Text("url"));

    public static readonly EvidenceTable WebTransportEstablishments = new(
        "browser_web_transport_establishments",
        TableKind.Evidence,
        [
            Context(),
            Scope(),
            Text("transportId"),
            .. RealtimeResponse("browser_web_transport_establishment"),
            Double("maxDatagramSize", N)
        ]);

    public static readonly EvidenceTable WebTransportCloseRequests = Evidence(
        "browser_web_transport_close_requests",
        Context(),
        Scope(),
        ScriptLocation(),
        World(),
        Text("transportId"),
        Double("code", N),
        RealtimeText("reason", N, "browser_web_transport_close_request_withheld"));

    public static readonly EvidenceTable WebTransportClosures = Evidence(
        "browser_web_transport_closures",
        Context(),
        Scope(),
        Text("transportId"),
        Bool("abrupt"),
        Double("code", N),
        RealtimeText("reason", N, "browser_web_transport_closure_withheld"));

    public static readonly EvidenceTable CollectorOmissions = Evidence(
        "collector_omissions",
            Name("reason"),
            BigInt("count", O),
            Name("stream", O),
            BigInt("firstDroppedAtNanoseconds", O),
            BigInt("lastDroppedAtNanoseconds", O),
            new MapField(
                "droppedByObservationType",
                O,
                new EvidenceTable(
                    "collector_omission_dropped_counts",
                    TableKind.Child,
                    [BigInt(string.Empty, R, "value")],
                    scalarItem: true,
                    mapEntry: true)),
            new IdentityField("context", O, BrowserContexts));

    /// <summary>The table of each channel and event type the model covers.</summary>
    public static readonly IReadOnlyDictionary<(string Channel, string EventType), EvidenceTable> ByEventType =
        BuildEventTypes();

    /// <summary>
    /// The migration that introduced each group of evidence tables, in
    /// version order. Migrations 0003 to 0007 were generated from the catalog
    /// as it then stood; an applied migration is never changed, so a later
    /// change to a table is a migration of its own, such as 0010, which
    /// replaced the per-node computed style rows with shared styles. A test
    /// requires the tables the migrations leave to match the catalog.
    /// </summary>
    public static readonly IReadOnlyList<(int Version, string Name, EvidenceTable[] Evidence)> Migrations =
    [
        (3, "evidence_tables",
        [
            CollectorLifecycle, SessionMarkers, RawKeyboard, RawMouse, ForegroundWindows, DesktopFrames,
            BrowserConnections, BrowserExits, BrowserClockSynchronizations, UiaEvents, AudioStreams,
            AudioBuffers, AudioStreamErrors, CollectorOmissions
        ]),
        (4, "browser_script_evidence",
        [
            BrowserListeners, BrowserDispatches, BrowserTimers, BrowserSchedulerDeferrals, BrowserNavigations
        ]),
        (5, "browser_document_evidence",
        [
            AccessibilityCheckpointStarts, AccessibilityCheckpointNodes, AccessibilityCheckpointCompletions,
            DomCheckpointStarts, DomCheckpointNodes, DomCheckpointAttributes, DomCheckpointShadowRoots,
            DomCheckpointSlotAssignments, DomCheckpointCompletions, DomAttributeChanges, DomCharacterDataChanges,
            FocusChanges, SelectionChanges, TextControlValueChanges, ActiveDescendantReferences,
            InteractionCheckpointStarts, InteractionCheckpointTextControls, InteractionCheckpointCompletions
        ]),
        (6, "browser_rendering_evidence",
        [
            LayoutCheckpointStarts, LayoutCheckpointNodes, LayoutCheckpointCompletions, PresentationRequests,
            PresentationsNotSwapped, PresentationSwaps, PresentationFeedback
        ]),
        (7, "browser_network_evidence",
        [
            DocumentCookieReads, DocumentCookieWrites, CookieStoreRequests, CookieStoreResults,
            CookieStoreChanges, CookieAccesses, NetworkRequests, NetworkResponses, NetworkRequestFinishes,
            NetworkRequestFailures, NetworkMemoryCacheHits, NetworkRequestHeadersSent,
            NetworkResponseHeadersReceived, NetworkNavigationResponses, WebSocketCreations,
            WebSocketHandshakeRequests, WebSocketHandshakeResponses, WebSocketMessagesSent,
            WebSocketMessagesReceived, WebSocketCloseRequests, WebSocketErrors, WebSocketClosures,
            EventSourceMessages, WebTransportCreations, WebTransportEstablishments,
            WebTransportCloseRequests, WebTransportClosures
        ]),
        (18, "dom_frame_owners", [DomCheckpointFrameOwners, DomFrameOwnerChanges]),
        (21, "windows_preferences", [WindowsPreferenceSnapshots, WindowsPreferenceChanges]),
        (22, "magnifier_changes", [MagnifierChangeRecords]),
        (23, "browser_preferences",
        [
            BrowserPreferenceSnapshots, BrowserPreferenceChanges, BrowserWebPreferencesSent,
            BrowserZoomLevelChanges
        ])
    ];

    /// <summary>
    /// Every table with the migration that creates it, in creation order:
    /// by migration, and within one, identities first in dependency order,
    /// then evidence tables, then child tables after their owners.
    /// </summary>
    public static readonly IReadOnlyList<(int Version, EvidenceTable Table)> VersionedTables = BuildTables();

    /// <summary>Every table in creation order.</summary>
    public static readonly IReadOnlyList<EvidenceTable> Tables = [.. VersionedTables.Select(item => item.Table)];

    private static Dictionary<(string, string), EvidenceTable> BuildEventTypes()
    {
        var map = new Dictionary<(string, string), EvidenceTable>
        {
            [("collector.lifecycle", "collector-lifecycle")] = CollectorLifecycle,
            [("session.annotations", "session-marker")] = SessionMarkers,
            [("input.keyboard", "raw-keyboard")] = RawKeyboard,
            [("input.mouse", "raw-mouse")] = RawMouse,
            [("window.foreground", "foreground-window")] = ForegroundWindows,
            [("system.preferences", "windows-preferences")] = WindowsPreferenceSnapshots,
            [("system.preferences", "windows-preference-changed")] = WindowsPreferenceChanges,
            [("graphics.desktop.frames", "desktop-frame")] = DesktopFrames,
            [("graphics.magnifier", "magnifier-changed")] = MagnifierChangeRecords,
            [("browser.preferences", "browser-preferences")] = BrowserPreferenceSnapshots,
            [("browser.preferences", "browser-preference-changed")] = BrowserPreferenceChanges,
            [("browser.preferences", "web-preferences-sent")] = BrowserWebPreferencesSent,
            [("browser.preferences", "zoom-level-changed")] = BrowserZoomLevelChanges,
            [("browser.lifecycle", "browser-connected")] = BrowserConnections,
            [("browser.lifecycle", "browser-exited")] = BrowserExits,
            [("browser.lifecycle", "browser-clock-synchronized")] = BrowserClockSynchronizations,
            [("browser.scheduler", "wake-up-deferred")] = BrowserSchedulerDeferrals,
            [("browser.navigation", "navigation-started")] = BrowserNavigations,
            [("browser.navigation", "navigation-completed")] = BrowserNavigations,
            [("browser.accessibility", "accessibility-checkpoint-started")] = AccessibilityCheckpointStarts,
            [("browser.accessibility", "accessibility-checkpoint-node")] = AccessibilityCheckpointNodes,
            [("browser.accessibility", "accessibility-checkpoint-completed")] = AccessibilityCheckpointCompletions,
            [("browser.dom", "dom-checkpoint-started")] = DomCheckpointStarts,
            [("browser.dom", "dom-checkpoint-node")] = DomCheckpointNodes,
            [("browser.dom", "dom-checkpoint-node-attribute")] = DomCheckpointAttributes,
            [("browser.dom", "dom-checkpoint-shadow-root")] = DomCheckpointShadowRoots,
            [("browser.dom", "dom-checkpoint-slot-assignment")] = DomCheckpointSlotAssignments,
            [("browser.dom", "dom-checkpoint-completed")] = DomCheckpointCompletions,
            [("browser.dom", "dom-checkpoint-frame-owner")] = DomCheckpointFrameOwners,
            [("browser.dom", "dom-frame-owner-changed")] = DomFrameOwnerChanges,
            [("browser.dom", "dom-attribute-changed")] = DomAttributeChanges,
            [("browser.dom", "dom-character-data-changed")] = DomCharacterDataChanges,
            [("browser.interaction", "focus-changed")] = FocusChanges,
            [("browser.interaction", "selection-changed")] = SelectionChanges,
            [("browser.interaction", "text-control-value-changed")] = TextControlValueChanges,
            [("browser.interaction", "active-descendant-reference-set")] = ActiveDescendantReferences,
            [("browser.interaction", "interaction-checkpoint-started")] = InteractionCheckpointStarts,
            [("browser.interaction", "interaction-checkpoint-text-control")] = InteractionCheckpointTextControls,
            [("browser.interaction", "interaction-checkpoint-completed")] = InteractionCheckpointCompletions,
            [("browser.layout", "layout-checkpoint-started")] = LayoutCheckpointStarts,
            [("browser.layout", "layout-checkpoint-node")] = LayoutCheckpointNodes,
            [("browser.layout", "layout-checkpoint-completed")] = LayoutCheckpointCompletions,
            [("browser.presentation", "presentation-requested")] = PresentationRequests,
            [("browser.presentation", "presentation-not-swapped")] = PresentationsNotSwapped,
            [("browser.presentation", "presentation-swapped")] = PresentationSwaps,
            [("browser.presentation", "presentation-feedback")] = PresentationFeedback,
            [("browser.cookie", "document-cookie-read")] = DocumentCookieReads,
            [("browser.cookie", "document-cookie-write")] = DocumentCookieWrites,
            [("browser.cookie", "cookie-store-request")] = CookieStoreRequests,
            [("browser.cookie", "cookie-store-result")] = CookieStoreResults,
            [("browser.cookie", "cookie-store-change")] = CookieStoreChanges,
            [("browser.cookie", "cookie-access")] = CookieAccesses,
            [("browser.network", "request-will-be-sent")] = NetworkRequests,
            [("browser.network", "response-received")] = NetworkResponses,
            [("browser.network", "request-finished")] = NetworkRequestFinishes,
            [("browser.network", "request-failed")] = NetworkRequestFailures,
            [("browser.network", "memory-cache-hit")] = NetworkMemoryCacheHits,
            [("browser.network", "request-headers-sent")] = NetworkRequestHeadersSent,
            [("browser.network", "response-headers-received")] = NetworkResponseHeadersReceived,
            [("browser.network", "navigation-response")] = NetworkNavigationResponses,
            [("browser.network", "websocket-created")] = WebSocketCreations,
            [("browser.network", "websocket-handshake-request")] = WebSocketHandshakeRequests,
            [("browser.network", "websocket-handshake-response")] = WebSocketHandshakeResponses,
            [("browser.network", "websocket-message-sent")] = WebSocketMessagesSent,
            [("browser.network", "websocket-message-received")] = WebSocketMessagesReceived,
            [("browser.network", "websocket-close-requested")] = WebSocketCloseRequests,
            [("browser.network", "websocket-error")] = WebSocketErrors,
            [("browser.network", "websocket-closed")] = WebSocketClosures,
            [("browser.network", "event-source-message")] = EventSourceMessages,
            [("browser.network", "web-transport-created")] = WebTransportCreations,
            [("browser.network", "web-transport-established")] = WebTransportEstablishments,
            [("browser.network", "web-transport-close-requested")] = WebTransportCloseRequests,
            [("browser.network", "web-transport-closed")] = WebTransportClosures
        };

        foreach (var type in new[] { "listener-registered", "listener-removed", "listener-callback-replaced" })
        {
            map[("browser.listener", type)] = BrowserListeners;
        }

        foreach (var type in new[] { "dispatch-started", "listener-invoked", "dispatch-completed", "default-action" })
        {
            map[("browser.dispatch", type)] = BrowserDispatches;
        }

        foreach (var type in new[] { "timer-scheduled", "timer-fired", "timer-cancelled" })
        {
            map[("browser.timer", type)] = BrowserTimers;
        }

        foreach (var type in new[] { "focus-changed", "automation-event", "structure-changed", "property-changed" })
        {
            map[("accessibility.uia.events", type)] = UiaEvents;
        }

        foreach (var channel in new[] { "audio.microphone", "audio.system" })
        {
            map[(channel, "audio-stream-started")] = AudioStreams;
            map[(channel, "audio-stream-stopped")] = AudioStreams;
            map[(channel, "audio-buffer")] = AudioBuffers;
            map[(channel, "audio-stream-error")] = AudioStreamErrors;
        }

        foreach (var channel in new[]
                 {
                     "accessibility.uia.events", "window.foreground", "graphics.desktop.frames",
                     "audio.microphone", "audio.system", "browser.lifecycle", "browser.accessibility",
                     "browser.listener", "browser.dispatch", "browser.timer", "browser.scheduler",
                     "browser.navigation", "browser.dom", "browser.cookie", "browser.interaction",
                     "browser.layout", "browser.presentation", "browser.network",
                     "browser.resources", "browser.compositor", "browser.animation",
                     "browser.script", "browser.preferences"
                 })
        {
            map[(channel, "collector-omission")] = CollectorOmissions;
        }

        return map;
    }

    private static List<(int Version, EvidenceTable Table)> BuildTables()
    {
        foreach (var table in ByEventType.Values.Distinct())
        {
            table.BindChildren();
        }

        var unassigned = ByEventType.Values.Distinct()
            .Except(Migrations.SelectMany(migration => migration.Evidence))
            .ToArray();
        if (unassigned.Length > 0)
        {
            throw new InvalidOperationException(
                $"Evidence tables {string.Join(", ", unassigned.Select(table => table.Name))} have no migration.");
        }

        var result = new List<(int, EvidenceTable)>();
        var seen = new HashSet<EvidenceTable>();
        foreach (var (version, _, evidence) in Migrations)
        {
            var ordered = new List<EvidenceTable>();

            void AddIdentity(EvidenceTable table)
            {
                if (!seen.Add(table))
                {
                    return;
                }

                foreach (var dependency in Dependencies(table.Fields))
                {
                    AddIdentity(dependency);
                }

                table.BindChildren();
                ordered.Add(table);
                AddChildren(table);
            }

            void AddChildren(EvidenceTable table)
            {
                foreach (var child in table.Children)
                {
                    foreach (var dependency in Dependencies(child.Fields))
                    {
                        AddIdentity(dependency);
                    }

                    if (seen.Add(child))
                    {
                        ordered.Add(child);
                        AddChildren(child);
                    }
                }
            }

            foreach (var table in evidence)
            {
                foreach (var dependency in Dependencies(table.Fields))
                {
                    AddIdentity(dependency);
                }

                foreach (var child in AllChildren(table))
                {
                    foreach (var dependency in Dependencies(child.Fields))
                    {
                        AddIdentity(dependency);
                    }
                }
            }

            foreach (var table in evidence)
            {
                if (seen.Add(table))
                {
                    ordered.Add(table);
                }
            }

            foreach (var table in evidence)
            {
                AddChildren(table);
            }

            result.AddRange(ordered.Select(table => (version, table)));
        }

        return result;
    }

    private static IEnumerable<EvidenceTable> AllChildren(EvidenceTable table)
    {
        foreach (var child in table.Children)
        {
            yield return child;
            foreach (var grandchild in AllChildren(child))
            {
                yield return grandchild;
            }
        }
    }

    private static IEnumerable<EvidenceTable> Dependencies(IEnumerable<Field> fields)
    {
        foreach (var field in fields)
        {
            switch (field)
            {
                case IdentityField identity:
                    yield return identity.Identity;
                    break;
                case GroupField group:
                    yield return group.Identity;
                    break;
                case InlineField inline:
                    foreach (var dependency in Dependencies(inline.Fields))
                    {
                        yield return dependency;
                    }

                    break;
            }
        }
    }

    // Each listed browser preference as {value, isDefault, problem}, the
    // value stored by the preference's type.
    // The one text list, the sites without page colours, is a child table.
    private static Field[] BrowserPreferenceReadings(Presence presence, string listTable) =>
    [
        .. Recorder.Contracts.BrowserPreferenceSettings.Browser.Select(setting => (Field)new InlineField(
            setting.Name,
            presence,
            [BrowserPreferenceValue(setting, "value", N, listTable), Bool("isDefault", N), Text("problem", N)]))
    ];

    private static Field BrowserPreferenceValue(
        Recorder.Contracts.BrowserPreferenceSetting setting,
        string json,
        Presence presence,
        string? listTable) =>
        setting.Kind switch
        {
            Recorder.Contracts.BrowserPreferenceKind.Boolean => Bool(json, presence),
            Recorder.Contracts.BrowserPreferenceKind.Integer => BigInt(json, presence),
            Recorder.Contracts.BrowserPreferenceKind.Number => Double(json, presence),
            Recorder.Contracts.BrowserPreferenceKind.Text => Text(json, presence),
            _ => TextList(json, presence, listTable ??
                throw new ArgumentException($"'{setting.Name}' is a text list and needs a table.", nameof(listTable)))
        };

    // Each Windows setting as {value, problem}, the value stored by the
    // setting's type, and the monitors in a child table of their own.
    private static Field[] WindowsSettings(Presence presence, string monitorsTable) =>
    [
        .. Recorder.Contracts.WindowsPreferenceSettings.All.Select(setting => (Field)new InlineField(
            setting.Name,
            presence,
            [
                setting.Kind switch
                {
                    Recorder.Contracts.WindowsPreferenceKind.Boolean => Bool("value", N),
                    Recorder.Contracts.WindowsPreferenceKind.Integer => BigInt("value", N),
                    Recorder.Contracts.WindowsPreferenceKind.Number => Double("value", N),
                    Recorder.Contracts.WindowsPreferenceKind.Text => Text("value", N),
                    _ => new ListField(
                        "value",
                        N,
                        new EvidenceTable(
                            monitorsTable,
                            TableKind.Child,
                            [
                                Text("deviceName"),
                                IntegerRectangle("bounds", R),
                                Bool("isPrimary"),
                                BigInt("dpiX", N),
                                BigInt("dpiY", N)
                            ]))
                },
                Text("problem", N)
            ]))
    ];

    // A frame's Magnifier readings, as the desktop frame columns of 0019 and
    // 0020 store them.
    private static Field[] MagnifierReadings() =>
    [
        new InlineField(
            "fullscreenMagnification",
            R,
            [Double("level", N), Int("x", N), Int("y", N), Text("problem", N)]),
        new InlineField(
            "fullscreenColorEffect",
            R,
            [new ArrayField("matrix", N, ScalarType.Double), Text("problem", N)])
    ];

    private static EvidenceTable Evidence(string name, params Field[] fields) =>
        new(name, TableKind.Evidence, fields);

    private static EvidenceTable Identity(string name, params Field[] fields) =>
        new(name, TableKind.Identity, fields);

    private static EvidenceTable TextIdentity(string name) =>
        new(name, TableKind.Identity, [Text(string.Empty, R, "value")], scalarItem: true);

    private static Field Scalar(string json, ScalarType type, Presence presence = R) =>
        new ScalarField(json, type, presence);

    private static Field Text(string json, Presence presence = R, string? column = null) =>
        new ScalarField(json, ScalarType.Text, presence, column);

    private static Field Int(string json, Presence presence = R, string? column = null) =>
        new ScalarField(json, ScalarType.Integer, presence, column);

    private static Field BigInt(string json, Presence presence = R, string? column = null) =>
        new ScalarField(json, ScalarType.BigInt, presence, column);

    private static Field Double(string json, Presence presence = R, string? column = null) =>
        new ScalarField(json, ScalarType.Double, presence, column);

    private static Field Bool(string json, Presence presence = R) =>
        new ScalarField(json, ScalarType.Boolean, presence);

    private static Field Name(string json, Presence presence = R, string? column = null) =>
        new NameField(json, presence, column);

    private static Field IntegerRectangle(string json, Presence presence) =>
        new InlineField(json, presence, [Int("x"), Int("y"), Int("width"), Int("height")]);

    private static Field NumberRectangle(string json, Presence presence) =>
        new InlineField(json, presence, [Double("x"), Double("y"), Double("width"), Double("height")]);

    private static EvidenceTable ScalarList(string name, ScalarType type, Presence item = R) =>
        new(name, TableKind.Child, [new ScalarField(string.Empty, type, item, "value")], scalarItem: true);

    private static Field TextList(string json, Presence presence, string name) =>
        new ListField(json, presence, ScalarList(name, ScalarType.Text));

    private static Field Context() => new IdentityField("context", R, BrowserContexts);

    private static Field Scope() => new IdentityField("scope", R, NetworkScopes);

    private static Field ScriptLocation() => new IdentityField("location", N, ScriptLocations);

    private static Field World() => new IdentityField("world", N, ExecutionWorlds);

    private static Field RemoteAddress() =>
        new InlineField("remoteAddress", N, [Text("ip"), Int("port")]);

    // A header list, its count, and its truncation flag, as the network
    // payloads carry them.
    private static Field[] Headers(string list, string count, string truncated, string table) =>
    [
        Int(count),
        new ListField(
            list,
            R,
            new EvidenceTable(
                table,
                TableKind.Child,
                [Name("name"), Text("value", N), Bool("valueRedacted"), Name("redactionReason", N)])),
        Bool(truncated)
    ];

    // A cookie as a cookie access or a wire header list reports it, without
    // its value.
    private static Field Cookies(string table) =>
        new ListField(
            "cookies",
            R,
            new EvidenceTable(
                table,
                TableKind.Child,
                [
                    Text("name"),
                    Bool("parsed"),
                    Text("domain", N),
                    Text("path", N),
                    Name("sameSite", N),
                    Bool("secure", N),
                    Bool("httpOnly", N),
                    Bool("hostOnly", N),
                    Bool("partitioned", N),
                    Bool("persistent", N),
                    Bool("expired", N),
                    Bool("included"),
                    NameList("exclusionReasons", R, table + "_exclusion_reasons"),
                    NameList("warningReasons", R, table + "_warning_reasons"),
                    Name("exemptionReason", N)
                ]));

    private static Field NetworkRequest(string json, string headersTable) =>
        new InlineField(
            json,
            R,
            [
                Text("inspectorId"),
                Text("requestId", N),
                Text("url"),
                Name("method"),
                Name("resourceType"),
                new InlineField(
                    "initiator",
                    R,
                    [
                        Name("type", N),
                        Text("url", N),
                        Int("line", N, "line_number"),
                        Int("column", N, "column_number"),
                        Bool("linkPreload")
                    ]),
                Bool("internal"),
                Name("destination"),
                Name("mode"),
                Name("credentialsMode"),
                Name("redirectMode"),
                Name("cacheMode"),
                Name("priority"),
                Name("initialPriority"),
                Name("fetchPriorityHint"),
                Name("renderBlocking"),
                Text("referrer", N),
                Name("referrerPolicy"),
                Bool("keepalive"),
                Bool("userGesture"),
                Bool("adResource"),
                Bool("formSubmission"),
                .. Headers("headers", "headerCount", "headersTruncated", headersTable)
            ]);

    private static string[] ResponseTimingPhases =>
    [
        "proxyStart", "proxyEnd", "domainLookupStart", "domainLookupEnd", "connectStart", "connectEnd",
        "sslStart", "sslEnd", "workerStart", "workerReady", "workerFetchStart", "workerRespondWithSettled",
        "workerRouterEvaluationStart", "workerCacheLookupStart", "sendStart", "sendEnd",
        "receiveHeadersStart", "receiveHeadersEnd", "receiveNonInformationalHeadersStart",
        "receiveEarlyHintsStart", "pushStart", "pushEnd", "responseEnd"
    ];

    private static string[] NavigationTimingPhases =>
    [
        "loaderStart", "firstRequestStart", "firstResponseStart", "firstLoaderCallback", "finalRequestStart",
        "finalResponseStart", "finalNonInformationalResponseStart", "finalLoaderCallback", "requestFailed",
        "commitSent", "commitReceived", "commitReplySent", "didCommit", "finalRequestDomainLookupStart",
        "finalRequestDomainLookupEnd", "finalRequestConnectStart", "finalRequestConnectEnd",
        "finalRequestSslStart"
    ];

    private static Field Timing(string start, string[] phases) =>
        new InlineField(
            "timing",
            N,
            [Double(start, N), .. phases.Select(phase => Double(phase, N))]);

    private static Field NetworkResponse(string json, Presence presence, string headersTable, string? column = null) =>
        new InlineField(
            json,
            presence,
            [
                Text("url"),
                Text("responseUrl", N),
                Int("status"),
                Text("statusText"),
                Name("mimeType"),
                Name("charset", N),
                Name("alpnProtocol", N),
                Name("connectionInfo", N),
                RemoteAddress(),
                Double("connectionId"),
                Bool("connectionReused"),
                Bool("wasCached"),
                Bool("fetchedViaServiceWorker"),
                Name("serviceWorkerResponseSource"),
                Bool("inPrefetchCache"),
                Bool("networkAccessed"),
                Bool("fromArchive"),
                Bool("cookieInRequest"),
                Name("responseType"),
                Double("encodedDataLength", N),
                Double("expectedContentLength"),
                .. Headers("headers", "headerCount", "headersTruncated", headersTable),
                Timing("requestStartBeforeRecordMilliseconds", ResponseTimingPhases)
            ],
            column);

    // A recorded realtime text, with the offsets where a credential was
    // withheld.
    private static Field RealtimeText(string json, Presence presence, string withheldTable) =>
        new InlineField(
            json,
            presence,
            [
                Text("text"),
                Bool("truncated"),
                new ListField(
                    "withheld",
                    R,
                    new EvidenceTable(
                        withheldTable,
                        TableKind.Child,
                        [Int("offset", R, "text_offset"), Name("reason")]))
            ]);

    // The handshake response members a WebSocket or WebTransport connection
    // reports.
    private static Field[] RealtimeResponse(string table) =>
    [
        Text("url", N),
        Name("httpVersion", N),
        Int("status"),
        Text("statusText", N),
        RemoteAddress(),
        Text("selectedProtocol", N),
        TextList("setCookieNames", R, table + "_set_cookie_names"),
        .. Headers("headers", "headerCount", "headersTruncated", table + "_headers")
    ];

    private static Field NameList(string json, Presence presence, string name) =>
        new ListField(
            json,
            presence,
            new EvidenceTable(
                name,
                TableKind.Child,
                [new NameField(string.Empty, R, "value_name_id")],
                scalarItem: true));
}
