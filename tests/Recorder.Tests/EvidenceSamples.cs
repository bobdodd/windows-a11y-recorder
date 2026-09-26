using System.Text.Json;

namespace Recorder.Tests;

/// <summary>
/// Payloads of the shapes the recorder's collectors write, one or more for
/// every channel and event type with evidence tables. Each is valid for the
/// session archive validator, and each is written the way System.Text.Json
/// writes it, so a payload rebuilt from its tables reads back unchanged.
/// </summary>
internal static class EvidenceSamples
{
    private const string Context =
        @"{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null," +
        @"""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1""," +
        @"""executionWorldId"":null,""documentToken"":""TOKEN-1""}";

    private const string Element =
        @"{""processId"":42,""nativeWindowHandle"":0,""automationId"":""SaveButton"",""name"":""Save"",""className"":""Button""," +
        @"""frameworkId"":""Win32"",""controlType"":""Button"",""localizedControlType"":""button"",""hasKeyboardFocus"":true," +
        @"""isKeyboardFocusable"":true,""isEnabled"":true,""isOffscreen"":false," +
        @"""boundingRectangle"":{""x"":10.5,""y"":20,""width"":75.25,""height"":23},""propertySource"":""event-cache""," +
        @"""qualityFlags"":[""cached-properties"",""name-from-cache""]}";

    private const string BareElement =
        @"{""processId"":null,""nativeWindowHandle"":null,""automationId"":null,""name"":null,""className"":null," +
        @"""frameworkId"":null,""controlType"":null,""localizedControlType"":null,""hasKeyboardFocus"":null," +
        @"""isKeyboardFocusable"":null,""isEnabled"":null,""isOffscreen"":null,""boundingRectangle"":null," +
        @"""qualityFlags"":[]}";

    private const string Monitor =
        @"{""deviceName"":""\\\\.\\DISPLAY1"",""bounds"":{""x"":0,""y"":0,""width"":1920,""height"":1080}," +
        @"""workArea"":{""x"":0,""y"":0,""width"":1920,""height"":1040},""isPrimary"":true}";

    private const string Format =
        @"{""encoding"":""pcm"",""sampleRate"":48000,""channels"":2,""bitsPerSample"":16,""blockAlign"":4," +
        @"""averageBytesPerSecond"":192000}";

    public static IReadOnlyList<(string Channel, string EventType, string Payload)> All { get; } =
    [
        ("collector.lifecycle", "collector-lifecycle",
            @"{""action"":""start"",""state"":""running"",""utc"":""2026-09-25T12:00:00.1234567+00:00""}"),
        ("collector.lifecycle", "collector-lifecycle",
            @"{""action"":""stop"",""state"":""stopped"",""utc"":""2026-09-25T12:00:01+00:00""}"),
        ("collector.lifecycle", "collector-lifecycle",
            @"{""action"":""stop"",""state"":""stopped"",""utc"":""2026-09-25T12:00:01.5+00:00""}"),
        ("session.annotations", "session-marker", @"{""note"":""Before the dialog""}"),
        ("session.annotations", "session-marker", @"{""note"":null}"),
        ("input.keyboard", "raw-keyboard",
            @"{""deviceHandle"":65539,""makeCode"":30,""flags"":0,""virtualKey"":65,""message"":256,""extraInformation"":0}"),
        ("input.mouse", "raw-mouse",
            @"{""deviceHandle"":-4294967291,""movementMode"":""relative"",""deltaX"":-3,""deltaY"":2,""buttonFlags"":1," +
            @"""buttonData"":0,""rawButtons"":0,""extraInformation"":0,""cursorX"":640,""cursorY"":-20,""foregroundProcessId"":42}"),
        ("window.foreground", "foreground-window",
            @"{""reason"":""event"",""eventThreadId"":7,""nativeEventTimeMilliseconds"":123456,""windowHandle"":1311000," +
            @"""processId"":42,""threadId"":7,""processName"":""notepad"",""processPath"":""C:\\Windows\\notepad.exe""," +
            @"""title"":""Untitled - Notepad"",""className"":""Notepad"",""isVisible"":true,""isMinimized"":false," +
            @"""isMaximized"":false,""isCloaked"":false,""dpi"":96," +
            @"""bounds"":{""x"":-8,""y"":10,""width"":800,""height"":600},""monitor"":" + Monitor + "}"),
        ("window.foreground", "foreground-window",
            @"{""reason"":""initial"",""eventThreadId"":null,""nativeEventTimeMilliseconds"":null,""windowHandle"":1311000," +
            @"""processId"":42,""threadId"":7,""processName"":""notepad"",""processPath"":""C:\\Windows\\notepad.exe""," +
            @"""title"":""Untitled - Notepad"",""className"":""Notepad"",""isVisible"":true,""isMinimized"":false," +
            @"""isMaximized"":false,""isCloaked"":null,""dpi"":null,""bounds"":null,""monitor"":" + Monitor + "}"),
        ("window.foreground", "foreground-window",
            @"{""reason"":""event"",""eventThreadId"":8,""nativeEventTimeMilliseconds"":123500,""windowHandle"":0," +
            @"""processId"":0,""threadId"":0,""processName"":null,""processPath"":null,""title"":null,""className"":null," +
            @"""isVisible"":false,""isMinimized"":false,""isMaximized"":false,""isCloaked"":null,""dpi"":null," +
            @"""bounds"":null,""monitor"":null}"),
        ("accessibility.uia.events", "focus-changed",
            @"{""eventId"":""UIA_AutomationFocusChangedEventId"",""changeType"":null,""runtimeId"":[42,1311000,-4]," +
            @"""newValue"":null,""element"":" + Element + "}"),
        ("accessibility.uia.events", "property-changed",
            @"{""eventId"":""UIA_NamePropertyId"",""changeType"":null,""runtimeId"":[],""newValue"":""Saved\ttwice""," +
            @"""element"":" + Element + "}"),
        ("accessibility.uia.events", "structure-changed",
            @"{""eventId"":""UIA_StructureChangedEventId"",""changeType"":""ChildAdded"",""runtimeId"":null," +
            @"""newValue"":null,""element"":" + BareElement + "}"),
        ("accessibility.uia.events", "automation-event",
            @"{""eventId"":""UIA_Window_WindowOpenedEventId"",""changeType"":null,""runtimeId"":[42],""newValue"":null," +
            @"""element"":" + Element + "}"),
        ("accessibility.uia.events", "collector-omission",
            @"{""reason"":""uia-observation-queue-full"",""count"":5,""firstDroppedAtNanoseconds"":100," +
            @"""lastDroppedAtNanoseconds"":2000,""droppedByObservationType"":{""focus-changed"":2,""property-changed"":3}}"),
        ("accessibility.uia.events", "collector-omission", @"{""reason"":""uia-observation-queue-full"",""count"":4}"),
        ("window.foreground", "collector-omission", @"{""reason"":""foreground-queue-full"",""count"":1}"),
        ("graphics.desktop.frames", "desktop-frame",
            @"{""path"":""frames/desktop/0000000001.png"",""x"":-1920,""y"":0,""width"":3840,""height"":1080,""stride"":15360," +
            @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":123456,""captureDurationNanoseconds"":8000000," +
            @"""framesPerSecond"":5,""backend"":""windows-graphics-capture"",""monitorCount"":2,""fallbackReason"":null," +
            @"""gdiFallbackFrameCount"":0,""frameSelection"":""newest-arrived"",""monitorFrames"":[" +
            @"{""monitorHandle"":65537,""x"":-1920,""y"":0,""width"":1920,""height"":1080,""systemRelativeTimeTicks"":900," +
            @"""compositedAtNanoseconds"":-5,""dequeuedAtNanoseconds"":100,""tryGetNextFrameAttempts"":1," +
            @"""supersededFrameCount"":2,""reusedPreviousImage"":false}," +
            @"{""monitorHandle"":65539,""x"":0,""y"":0,""width"":1920,""height"":1080,""systemRelativeTimeTicks"":901," +
            @"""compositedAtNanoseconds"":6,""dequeuedAtNanoseconds"":101,""tryGetNextFrameAttempts"":2," +
            @"""supersededFrameCount"":0,""reusedPreviousImage"":true}]}"),
        ("graphics.desktop.frames", "desktop-frame",
            @"{""path"":""frames/desktop/0000000002.png"",""x"":0,""y"":0,""width"":1920,""height"":1080,""stride"":7680," +
            @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":0,""captureDurationNanoseconds"":0," +
            @"""framesPerSecond"":5,""backend"":""gdi-bitblt"",""monitorCount"":null,""fallbackReason"":""capture item lost""," +
            @"""gdiFallbackFrameCount"":1,""monitorFrames"":[" +
            @"{""monitorHandle"":65537,""x"":0,""y"":0,""width"":1920,""height"":1080,""systemRelativeTimeTicks"":null," +
            @"""compositedAtNanoseconds"":null,""dequeuedAtNanoseconds"":null,""tryGetNextFrameAttempts"":null}]}"),
        ("graphics.desktop.frames", "desktop-frame",
            @"{""path"":""frames/desktop/0000000003.png"",""x"":0,""y"":0,""width"":1920,""height"":1080,""stride"":7680," +
            @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":5,""captureDurationNanoseconds"":1," +
            @"""framesPerSecond"":5,""backend"":""windows-graphics-capture"",""monitorCount"":1,""fallbackReason"":null," +
            @"""gdiFallbackFrameCount"":0}"),
        ("graphics.desktop.frames", "collector-omission", @"{""reason"":""frame-write-failed""}"),
        ("audio.microphone", "audio-stream-started",
            @"{""stream"":""microphone"",""path"":""audio/microphone.wav"",""device"":""Microphone (USB)""," +
            @"""endpointVolumeScalar"":0.75,""format"":" + Format + @",""dataBytes"":0,""buffersObserved"":0," +
            @"""buffersDropped"":0}"),
        ("audio.microphone", "audio-stream-stopped",
            @"{""stream"":""microphone"",""path"":""audio/microphone.wav"",""device"":""Microphone (USB)""," +
            @"""endpointVolumeScalar"":0.75,""format"":" + Format + @",""dataBytes"":192000,""buffersObserved"":100," +
            @"""buffersDropped"":1,""peakAmplitude"":0.5,""peakDbfs"":-6.020599913279624,""rmsAmplitude"":0.125," +
            @"""rmsDbfs"":-18.06179973983887}"),
        ("audio.system", "audio-stream-started",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""device"":""Speakers"",""format"":" + Format + "," +
            @"""dataBytes"":0,""buffersObserved"":0,""buffersDropped"":0}"),
        ("audio.system", "audio-stream-stopped",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""device"":""Speakers"",""format"":" + Format + "," +
            @"""dataBytes"":384000,""buffersObserved"":200,""buffersDropped"":0}"),
        ("audio.system", "audio-buffer",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""bufferSequence"":0,""dataByteOffset"":0," +
            @"""byteLength"":0,""sampleFrames"":0,""durationNanoseconds"":0," +
            @"""estimatedFirstSampleMonotonicNanoseconds"":0,""callbackMonotonicNanoseconds"":0}"),
        ("audio.microphone", "audio-buffer",
            @"{""stream"":""microphone"",""path"":""audio/microphone.wav"",""bufferSequence"":0,""dataByteOffset"":0," +
            @"""byteLength"":1920,""sampleFrames"":480,""durationNanoseconds"":10000000," +
            @"""estimatedFirstSampleMonotonicNanoseconds"":1000,""callbackMonotonicNanoseconds"":11000000}"),
        ("audio.microphone", "audio-buffer",
            @"{""stream"":""microphone"",""path"":""audio/microphone.wav"",""bufferSequence"":1,""dataByteOffset"":1920," +
            @"""byteLength"":1920,""sampleFrames"":480,""durationNanoseconds"":10000000," +
            @"""estimatedFirstSampleMonotonicNanoseconds"":10001000,""callbackMonotonicNanoseconds"":21000000}"),
        ("audio.system", "audio-stream-error",
            @"{""stream"":""system"",""errorType"":""COMException"",""message"":""The device was removed.""}"),
        ("audio.microphone", "audio-stream-error", @"{""stream"":""microphone"",""errorType"":null,""message"":null}"),
        ("audio.system", "collector-omission", @"{""reason"":""audio-buffer-dropped"",""count"":3,""stream"":""system""}"),
        ("audio.microphone", "collector-omission", @"{""reason"":""audio-device-missing""}"),
        ("browser.lifecycle", "browser-connected",
            @"{""protocolVersion"":""1"",""browserInstanceId"":""browser-1"",""processId"":4000,""processType"":""browser""," +
            @"""chromiumVersion"":""142.0.7400.1"",""parentProcessId"":null,""childProcessId"":null}"),
        ("browser.lifecycle", "browser-connected",
            @"{""protocolVersion"":""1"",""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer""," +
            @"""chromiumVersion"":""142.0.7400.1"",""parentProcessId"":4000,""childProcessId"":7}"),
        ("browser.lifecycle", "browser-exited",
            @"{""browserInstanceId"":""browser-1"",""processId"":4000,""exitCode"":-1073741510," +
            @"""exitCodeHex"":""0xC000013A"",""exitedUtc"":""2026-09-25T12:00:02.0000001+00:00"",""requestedByRecorder"":true}"),
        ("browser.lifecycle", "browser-exited",
            @"{""browserInstanceId"":""browser-1"",""processId"":4000,""exitCode"":0,""exitCodeHex"":""0x00000000""," +
            @"""exitedUtc"":null,""requestedByRecorder"":false}"),
        ("browser.lifecycle", "browser-clock-synchronized",
            @"{""protocolVersion"":""1"",""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer""," +
            @"""parentProcessId"":4000,""childProcessId"":7,""clockMappingId"":""chromium:browser-1:4100""," +
            @"""monotonicFrequency"":""10000000"",""uncertaintyNanoseconds"":250000}"),
        ("browser.lifecycle", "collector-omission",
            @"{""reason"":""browser-queue-full"",""count"":2,""context"":" + Context + "}"),
        ("browser.lifecycle", "collector-omission", @"{""reason"":""browser-queue-full"",""count"":1}")
    ];

    /// <summary>
    /// Payloads the tables store in a normal form, with the payload read
    /// back: an optional member written as null reads back absent, and a
    /// UTC time reads back without trailing fractional zeros.
    /// </summary>
    public static IReadOnlyList<(string Channel, string EventType, string Payload, string Expected)> Normalized { get; } =
    [
        ("audio.system", "audio-stream-stopped",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""device"":""Speakers"",""endpointVolumeScalar"":null,""format"":" + Format + "," +
            @"""dataBytes"":384000,""buffersObserved"":200,""buffersDropped"":0,""peakAmplitude"":null,""rmsDbfs"":null}",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""device"":""Speakers"",""format"":" + Format + "," +
            @"""dataBytes"":384000,""buffersObserved"":200,""buffersDropped"":0}"),
        ("collector.lifecycle", "collector-lifecycle",
            @"{""action"":""stop"",""state"":""stopped"",""utc"":""2026-09-25T12:00:01.5000000+00:00""}",
            @"{""action"":""stop"",""state"":""stopped"",""utc"":""2026-09-25T12:00:01.5+00:00""}"),
        ("browser.lifecycle", "collector-omission",
            @"{""reason"":""browser-queue-full"",""count"":1,""context"":null}",
            @"{""reason"":""browser-queue-full"",""count"":1}")
    ];

    /// <summary>A UI Automation focus change to the named element.</summary>
    public static string Focus(string name, string controlType = "Button") =>
        Element
            .Replace(@"""name"":""Save""", @"""name"":" + JsonSerializer.Serialize(name), StringComparison.Ordinal)
            .Replace(@"""controlType"":""Button""", @"""controlType"":" + JsonSerializer.Serialize(controlType), StringComparison.Ordinal)
            is var element
            ? @"{""eventId"":""UIA_AutomationFocusChangedEventId"",""changeType"":null,""runtimeId"":[42,7],""newValue"":null,""element"":" +
                element + "}"
            : throw new InvalidOperationException();

    /// <summary>A desktop frame image at the path.</summary>
    public static string DesktopFrame(string path) =>
        @"{""path"":" + JsonSerializer.Serialize(path) + @",""x"":0,""y"":0,""width"":1920,""height"":1080,""stride"":7680," +
        @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":3,""captureDurationNanoseconds"":1," +
        @"""framesPerSecond"":5,""backend"":""windows-graphics-capture"",""monitorCount"":1,""fallbackReason"":null," +
        @"""gdiFallbackFrameCount"":0}";

    /// <summary>The start of an audio stream written to the path.</summary>
    public static string AudioStarted(string stream, string path) =>
        @"{""stream"":" + JsonSerializer.Serialize(stream) + @",""path"":" + JsonSerializer.Serialize(path) +
        @",""device"":""Test device"",""format"":" + Format + @",""dataBytes"":0,""buffersObserved"":0,""buffersDropped"":0}";
}
