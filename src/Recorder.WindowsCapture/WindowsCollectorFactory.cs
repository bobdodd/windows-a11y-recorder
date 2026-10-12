using Recorder.Collectors.AssistiveTechnology;
using Recorder.Collectors.Audio;
using Recorder.Collectors.Automation;
using Recorder.Collectors.Browser;
using Recorder.Collectors.Graphics;
using Recorder.Collectors.Input;
using Recorder.Collectors.Windowing;
using Recorder.Contracts;
using Recorder.Coordinator;

namespace Recorder.WindowsCapture;

public static class WindowsCollectorFactory
{
    public static IReadOnlyList<ICaptureCollector> Create(RecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var collectors = new List<ICaptureCollector>();

        RawInputCollector? rawInput = null;
        KeyboardHookCollector? keyboardHook = null;
        if (options.CaptureKeyboardAndMouse)
        {
            rawInput = new RawInputCollector();
            collectors.Add(rawInput);

            // The keys a screen reader keeps never reach raw input; the
            // low-level hook sees them. It is checked against raw input, and
            // kept first in the chain while a screen reader runs. See
            // docs/architecture/screen-reader-activity.md, "The keyboard hook".
            // Its mouse hook is checked against raw input's mouse buttons
            // ("The mouse hook").
            keyboardHook = new KeyboardHookCollector();
            rawInput.KeyboardObserved += keyboardHook.ObserveRawKey;
            rawInput.MouseObserved += keyboardHook.ObserveRawMouse;
            collectors.Add(keyboardHook);
        }

        NvdaWindowSnapshotCollector? windowSnapshots = null;
        if (options.CaptureUiAutomation)
        {
            var uiAutomation = new UiAutomationCollector();
            collectors.Add(uiAutomation);

            // What NVDA's windows show: their whole tree, read on the UI
            // Automation collector's events in NVDA's main process. See
            // docs/architecture/screen-reader-activity.md, "2c-4, what
            // NVDA's windows show".
            windowSnapshots = new NvdaWindowSnapshotCollector();
            uiAutomation.ObservationRecorded += windowSnapshots.Observe;
            collectors.Add(windowSnapshots);
        }

        if (options.CaptureForegroundWindow)
        {
            collectors.Add(new ForegroundWindowCollector());
        }

        // The Windows accessibility settings are always recorded: reading
        // them changes nothing and costs little. See
        // docs/architecture/accessibility-preferences.md.
        collectors.Add(new WindowsPreferencesCollector());

        // The assistive technology running, NVDA first, is always recorded:
        // its processes, its modules in the instrumented Chromium, and when
        // its audio makes sound. It reads only, and changes nothing in it.
        // See docs/architecture/screen-reader-activity.md, "Tracking NVDA".
        var assistiveTechnology = new AssistiveTechnologyCollector(
            options.CaptureBrowserEvidence
                ? options.ChromiumExecutablePath ?? Path.Combine(AppContext.BaseDirectory, "browser", "chrome.exe")
                : null);
        if (keyboardHook is not null)
        {
            assistiveTechnology.ScreenReaderRunningChanged += keyboardHook.SetScreenReaderRunning;
        }

        if (windowSnapshots is not null)
        {
            assistiveTechnology.ScreenReaderProcessesChanged += windowSnapshots.SetScreenReaderProcesses;
        }

        collectors.Add(assistiveTechnology);

        if (options.CaptureDesktopFrames)
        {
            collectors.Add(new DesktopFrameCollector(options.FramesPerSecond));
        }

        if (options.CaptureMicrophone || options.CaptureSystemAudio)
        {
            collectors.Add(new AudioCollector(new AudioCaptureOptions(
                options.CaptureMicrophone,
                options.CaptureSystemAudio)));
        }

        if (options.CaptureBrowserEvidence)
        {
            collectors.Add(new BrowserEvidenceReceiver(
                new BrowserEvidenceReceiverOptions
                {
                    ChromiumExecutablePath =
                        options.ChromiumExecutablePath ??
                        Path.Combine(
                            AppContext.BaseDirectory,
                            "browser",
                            "chrome.exe"),
                    StartUrl = options.BrowserStartUrl,
                    ProfileDirectory = options.BrowserProfileDirectory,
                    RemoteDebuggingPort = options.BrowserRemoteDebuggingPort,
                    FullWalkInterval = options.BrowserFullWalkInterval
                }));
        }

        return collectors;
    }
}
