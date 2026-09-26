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

    // One table for the omission records of every channel. Each channel's
    // omission states a subset of these members; the validator states which.
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
    /// Every table in creation order: identities first, in dependency order,
    /// then evidence tables, then child tables after their owners.
    /// </summary>
    public static readonly IReadOnlyList<EvidenceTable> Tables = BuildTables();

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
            [("browser.lifecycle", "browser-clock-synchronized")] = BrowserClockSynchronizations
        };

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
                     "audio.microphone", "audio.system", "browser.lifecycle"
                 })
        {
            map[(channel, "collector-omission")] = CollectorOmissions;
        }

        return map;
    }

    private static List<EvidenceTable> BuildTables()
    {
        foreach (var table in ByEventType.Values.Distinct())
        {
            table.BindChildren();
        }

        var ordered = new List<EvidenceTable>();
        var seen = new HashSet<EvidenceTable>();

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

        var evidence = ByEventType.Values.Distinct().ToArray();
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

        return ordered;
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

    private static Field Int(string json, Presence presence = R) =>
        new ScalarField(json, ScalarType.Integer, presence);

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

    private static EvidenceTable ScalarList(string name, ScalarType type) =>
        new(name, TableKind.Child, [new ScalarField(string.Empty, type, R, "value")], scalarItem: true);

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
