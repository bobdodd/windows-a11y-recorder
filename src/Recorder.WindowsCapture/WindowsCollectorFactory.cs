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

        if (options.CaptureKeyboardAndMouse)
        {
            collectors.Add(new RawInputCollector());
        }

        if (options.CaptureUiAutomation)
        {
            collectors.Add(new UiAutomationCollector());
        }

        if (options.CaptureForegroundWindow)
        {
            collectors.Add(new ForegroundWindowCollector());
        }

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
                    RemoteDebuggingPort = options.BrowserRemoteDebuggingPort
                }));
        }

        return collectors;
    }
}
