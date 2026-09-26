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
                    ])));

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
                    new ListField(
                        "visiblePathIndexes",
                        R,
                        ScalarList("browser_dispatch_path_scope_visible_indexes", ScalarType.Integer)),
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
        Int("maximumNodes"));

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
        Text("sourceCheckpointId"),
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
    /// The migration that adds each group of evidence tables, in version
    /// order. A table is created by the first migration whose evidence tables
    /// reach it, so a later group adds only the tables it introduces, and an
    /// applied migration is never changed.
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
            [("graphics.desktop.frames", "desktop-frame")] = DesktopFrames,
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
            [("browser.dom", "dom-attribute-changed")] = DomAttributeChanges,
            [("browser.dom", "dom-character-data-changed")] = DomCharacterDataChanges,
            [("browser.interaction", "focus-changed")] = FocusChanges,
            [("browser.interaction", "selection-changed")] = SelectionChanges,
            [("browser.interaction", "text-control-value-changed")] = TextControlValueChanges,
            [("browser.interaction", "active-descendant-reference-set")] = ActiveDescendantReferences,
            [("browser.interaction", "interaction-checkpoint-started")] = InteractionCheckpointStarts,
            [("browser.interaction", "interaction-checkpoint-text-control")] = InteractionCheckpointTextControls,
            [("browser.interaction", "interaction-checkpoint-completed")] = InteractionCheckpointCompletions
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
                     "browser.layout", "browser.presentation", "browser.network"
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

    private static Field Double(string json, Presence presence = R) =>
        new ScalarField(json, ScalarType.Double, presence);

    private static Field Bool(string json, Presence presence = R) =>
        new ScalarField(json, ScalarType.Boolean, presence);

    private static Field Name(string json, Presence presence = R) =>
        new NameField(json, presence);

    private static Field IntegerRectangle(string json, Presence presence) =>
        new InlineField(json, presence, [Int("x"), Int("y"), Int("width"), Int("height")]);

    private static Field NumberRectangle(string json, Presence presence) =>
        new InlineField(json, presence, [Double("x"), Double("y"), Double("width"), Double("height")]);

    private static EvidenceTable ScalarList(string name, ScalarType type, Presence item = R) =>
        new(name, TableKind.Child, [new ScalarField(string.Empty, type, item, "value")], scalarItem: true);

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
