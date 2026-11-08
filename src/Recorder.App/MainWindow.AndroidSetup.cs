using System.IO;
using System.Windows;
using System.Windows.Threading;
using Recorder.Collectors.Android;

namespace Recorder.App;

// The Android menu and the Android device recording target
// (docs/architecture/android-implementation-plan.md). Part 0 adds Android
// setup, which downloads adb; part 1 adds the target and its checks panel;
// part 2 adds the companion app's installation and the recording of the
// companion, the phone clock and the display.
public partial class MainWindow
{
    // How often the checks run while the panel is shown, so that a phone
    // unplugged or allowed shows within a few seconds.
    private static readonly TimeSpan AndroidChecksInterval = TimeSpan.FromSeconds(2);

    private AndroidDeviceChecks? _androidChecks;
    private DispatcherTimer? _androidChecksTimer;
    private CancellationTokenSource? _androidChecksCancellation;
    private bool _androidChecking;
    private PhoneState? _androidPhoneState;
    private AndroidCheckReport? _androidReport;
    private bool _companionSetupRunning;

    private void AndroidSetupMenuItem_Click(object sender, RoutedEventArgs e) =>
        new AndroidSetupWindow(this, AndroidTools.DataFolder, AppContext.BaseDirectory).ShowDialog();

    private void RecordingTarget_Checked(object sender, RoutedEventArgs e)
    {
        // The desktop choice is checked while the window is still being
        // built, before the panels below it exist.
        if (AndroidChecksGroupBox is null || CaptureChannelsGroupBox is null || BrowserCaptureGroupBox is null)
        {
            return;
        }

        var android = AndroidTargetRadioButton.IsChecked == true;
        CaptureChannelsGroupBox.Visibility = android ? Visibility.Collapsed : Visibility.Visible;
        BrowserCaptureGroupBox.Visibility = android ? Visibility.Collapsed : Visibility.Visible;
        AndroidChecksGroupBox.Visibility = android ? Visibility.Visible : Visibility.Collapsed;
        if (android)
        {
            StartAndroidChecks();
        }
        else
        {
            StopAndroidChecks();
        }
    }

    private void StartAndroidChecks()
    {
        if (_androidChecks is null)
        {
            var adb = new AdbProcess(AndroidTools.Adb(AndroidTools.DataFolder));
            _androidChecks = new AndroidDeviceChecks(
                adb,
                new AdbServerProbe(),
                File.Exists,
                BundledCompanion.Read(AppContext.BaseDirectory),
                new AdbCompanionProbe(adb));
        }

        _androidChecksCancellation = new CancellationTokenSource();
        _androidPhoneState = null;
        AndroidChecksText.Text = "Checking the phone.";
        if (_androidChecksTimer is null)
        {
            _androidChecksTimer = new DispatcherTimer { Interval = AndroidChecksInterval };
            _androidChecksTimer.Tick += async (_, _) => await RunAndroidChecksAsync();
        }

        _androidChecksTimer.Start();
        _ = RunAndroidChecksAsync();
    }

    private void StopAndroidChecks()
    {
        _androidChecksTimer?.Stop();
        _androidChecksCancellation?.Cancel();
        _androidChecksCancellation?.Dispose();
        _androidChecksCancellation = null;
    }

    private async Task RunAndroidChecksAsync()
    {
        if (_androidChecking || _androidChecks is null || _androidChecksCancellation is not { } cancellation)
        {
            return;
        }

        _androidChecking = true;
        try
        {
            var checks = _androidChecks;
            var report = await Task.Run(() => checks.CheckAsync(cancellation.Token));
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            if (AndroidChecksText.Text != report.Text)
            {
                AndroidChecksText.Text = report.Text;
            }

            // A change of the phone's state is spoken; the first result is
            // not, as the panel has just been opened and can be read.
            if (_androidPhoneState is { } previous && previous != report.Phone)
            {
                var phone = report.Checks.FirstOrDefault(check => check.Name == "Phone") ?? report.Checks[^1];
                _busy.AnnounceAndroid($"{phone.Name}: {phone.Text}");
            }

            _androidPhoneState = report.Phone;
            _androidReport = report;
            ShowCompanionButton(report);
        }
        catch (OperationCanceledException)
        {
            // The desktop was chosen or the window is closing.
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Net.Sockets.SocketException)
        {
            AndroidChecksText.Text = $"The checks could not run: {exception.Message}";
        }
        finally
        {
            _androidChecking = false;
        }
    }

    private void ShowCompanionButton(AndroidCheckReport report)
    {
        if (_companionSetupRunning)
        {
            return;
        }

        var offer = report.Phone == PhoneState.Connected ? report.Companion : CompanionAction.None;
        CompanionButton.Content = offer == CompanionAction.TurnOn ? "Turn on companion app" : "Install companion app";
        CompanionButton.Visibility = offer == CompanionAction.None ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void CompanionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_companionSetupRunning || _androidReport is not { Phone: PhoneState.Connected, Serial: { } serial } report)
        {
            return;
        }

        var turnOnOnly = report.Companion == CompanionAction.TurnOn;
        _companionSetupRunning = true;
        CompanionButton.IsEnabled = false;
        var doing = turnOnOnly ? "Turning on the companion app." : "Installing the companion app on the phone.";
        CompanionResultText.Text = doing;
        CompanionResultText.Visibility = Visibility.Visible;
        var busy = _busy.Begin(doing);
        CompanionSetupResult result;
        try
        {
            var adb = new AdbProcess(AndroidTools.Adb(AndroidTools.DataFolder));
            var apk = BundledCompanion.Read(AppContext.BaseDirectory).ApkPath;
            result = await Task.Run(() => turnOnOnly
                ? CompanionApp.TurnOnAsync(adb, serial, CancellationToken.None)
                : CompanionApp.InstallAndTurnOnAsync(adb, serial, apk, CancellationToken.None));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            result = new CompanionSetupResult(false, $"The companion app could not be set up: {exception.Message}");
        }
        finally
        {
            busy.Dispose();
            _companionSetupRunning = false;
            CompanionButton.IsEnabled = _coordinator is null;
        }

        CompanionResultText.Text = result.Text;
        _busy.AnnounceAndroid(result.Text);
        await RunAndroidChecksAsync();
    }
}
