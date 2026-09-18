using System.Runtime.InteropServices;
using Recorder.Coordinator;
using Recorder.WindowsCapture;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Recorder.CaptureHost must run on Windows.");
    return 2;
}

NativeMethods.EnablePerMonitorDpiAwareness();

var outputRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "Windows A11y Recorder");
TimeSpan? duration = null;
var captureMicrophone = false;
var captureSystemAudio = false;
var captureBrowserEvidence = false;
string? chromiumExecutablePath = null;
string? browserStartUrl = null;

for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--output" when index + 1 < args.Length:
            outputRoot = Path.GetFullPath(args[++index]);
            break;
        case "--duration-seconds" when
            index + 1 < args.Length &&
            int.TryParse(args[++index], out var seconds) &&
            seconds > 0:
            duration = TimeSpan.FromSeconds(seconds);
            break;
        case "--audio":
            captureMicrophone = true;
            captureSystemAudio = true;
            break;
        case "--microphone":
            captureMicrophone = true;
            break;
        case "--system-audio":
            captureSystemAudio = true;
            break;
        case "--browser":
            captureBrowserEvidence = true;
            break;
        case "--browser-path" when index + 1 < args.Length:
            captureBrowserEvidence = true;
            chromiumExecutablePath = Path.GetFullPath(args[++index]);
            break;
        case "--browser-url" when index + 1 < args.Length:
            captureBrowserEvidence = true;
            browserStartUrl = args[++index];
            break;
        default:
            Console.Error.WriteLine(
                "Usage: Recorder.CaptureHost [--output PATH] " +
                "[--duration-seconds NUMBER] " +
                "[--audio | --microphone | --system-audio] " +
                "[--browser] [--browser-path PATH] [--browser-url URL]");
            return 64;
    }
}
await using var coordinator = new SessionCoordinator(WindowsCollectorFactory.Create);
try
{
    var status = await coordinator.StartAsync(new RecordingOptions
    {
        OutputRoot = outputRoot,
        CaptureMicrophone = captureMicrophone,
        CaptureSystemAudio = captureSystemAudio,
        CaptureBrowserEvidence = captureBrowserEvidence,
        ChromiumExecutablePath = chromiumExecutablePath,
        BrowserStartUrl = browserStartUrl
    });

    using var stopSignal = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        stopSignal.Cancel();
    };

    Console.WriteLine("Windows Accessibility Recorder");
    Console.WriteLine(
        $"Recording input, accessibility, windows, desktop frames" +
        $"{(captureMicrophone || captureSystemAudio ? ", audio" : string.Empty)}" +
        $"{(captureBrowserEvidence ? ", and instrumented browser evidence" : string.Empty)} to: " +
        status.SessionDirectory);
    Console.WriteLine(
        duration is null
            ? "Press Ctrl+C to stop."
            : $"Stopping automatically after {duration.Value.TotalSeconds:N0} seconds.");

    try
    {
        await Task.Delay(duration ?? Timeout.InfiniteTimeSpan, stopSignal.Token);
    }
    catch (OperationCanceledException) when (stopSignal.IsCancellationRequested)
    {
    }

    status = await coordinator.StopAsync();
    Console.WriteLine(
        $"Stopped. Accepted {status.AcceptedEvents:N0} records; " +
        $"dropped {status.DroppedEvents:N0}.");
    return status.State == RecordingSessionState.Completed ? 0 : 3;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

internal static class NativeMethods
{
    private static readonly nint PerMonitorAwareV2 = new(-4);

    public static void EnablePerMonitorDpiAwareness()
    {
        _ = SetProcessDpiAwarenessContext(PerMonitorAwareV2);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);
}
