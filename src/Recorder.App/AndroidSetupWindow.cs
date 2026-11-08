using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Recorder.Analysis.Setup;
using Recorder.Collectors.Android;

namespace Recorder.App;

/// <summary>
/// Android setup (part 0 of docs/architecture/android-implementation-plan.md):
/// downloads Google's SDK Platform-Tools, which holds adb, after the person
/// accepts the Android SDK licence, since the licence does not allow the
/// recorder to include it. The download is checked against the size and
/// SHA-256 fixed in the recorder, continues after a stop as model downloads
/// do, and is unpacked into the person's own folder. The window also shows
/// whether the scrcpy server and the companion app, which the installer
/// puts beside the programs, are in place.
/// </summary>
internal sealed class AndroidSetupWindow : Window
{
    private const string ProgressActivityId = "Recorder.App.AndroidSetup";

    private readonly string _dataFolder;
    private readonly string _baseDirectory;
    private readonly SelectableText _platformTools;
    private readonly SelectableText _adbVersion;
    private readonly SelectableText _scrcpy;
    private readonly SelectableText _companion;
    private readonly CheckBox _accept;
    private readonly Button _install;
    private readonly Button _cancel;
    private readonly ProgressBar _progress;
    private readonly SelectableText _status;
    private CancellationTokenSource? _running;
    private int _announcedTenth = -1;

    public AndroidSetupWindow(Window owner, string dataFolder, string baseDirectory)
    {
        Owner = owner;
        _dataFolder = dataFolder;
        _baseDirectory = baseDirectory;
        Title = "Set up Android recording";
        Width = 820;
        Height = 680;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var page = new StackPanel { Margin = new Thickness(16) };
        page.Children.Add(Heading("Set up Android recording", 1));
        page.Children.Add(Paragraph(
            "Recording an Android phone over USB needs Google's adb program, which is part of the Android SDK Platform-Tools. " +
            "Google's licence does not allow the recorder to include it, so it is downloaded here from Google, checked against " +
            "the size and SHA-256 fixed in the recorder, and kept in your own folder."));

        page.Children.Add(Heading("Android SDK Platform-Tools", 2));
        _platformTools = Paragraph("");
        page.Children.Add(_platformTools);
        _adbVersion = Paragraph("");
        page.Children.Add(_adbVersion);
        page.Children.Add(Paragraph(
            $"Platform-Tools is used under the {AndroidTools.SdkLicenceName}, at {AndroidTools.SdkLicenceAddress}. " +
            "Read it before you accept it."));
        var licenceButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        licenceButtons.Children.Add(Command("Open the _licence in the browser", OpenLicence));
        page.Children.Add(licenceButtons);
        _accept = new CheckBox
        {
            Content = $"I have read and _accept the {AndroidTools.SdkLicenceName}",
            MinHeight = 44,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        page.Children.Add(_accept);

        var installButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        _install = Command($"_Download and install Platform-Tools {AndroidTools.PlatformToolsVersion}", () => { });
        _install.Click += async (_, _) => await InstallAsync();
        _install.IsEnabled = false;
        _accept.Checked += (_, _) => _install.IsEnabled = _running is null;
        _accept.Unchecked += (_, _) => _install.IsEnabled = false;
        installButtons.Children.Add(_install);
        _cancel = Command("_Stop the download", () => _running?.Cancel());
        _cancel.IsEnabled = false;
        installButtons.Children.Add(_cancel);
        page.Children.Add(installButtons);

        _progress = new ProgressBar { Height = 20, Minimum = 0, Maximum = 1000, Margin = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetName(_progress, "Download progress");
        page.Children.Add(_progress);
        _status = Paragraph("");
        AutomationProperties.SetName(_status, "Android setup status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        page.Children.Add(_status);

        page.Children.Add(Heading("Included with the recorder", 2));
        _scrcpy = Paragraph("");
        page.Children.Add(_scrcpy);
        _companion = Paragraph("");
        page.Children.Add(_companion);

        var close = new Button { Content = "Close", MinWidth = 88, MinHeight = 44, IsCancel = true, Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        page.Children.Add(close);

        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Closing += OnClosing;
        ShowStatus();
        Loaded += async (_, _) => await ShowAdbVersionAsync(CancellationToken.None);
    }

    private void ShowStatus()
    {
        var status = AndroidToolsStatus.Read(_dataFolder, _baseDirectory);
        _platformTools.Text = status.PlatformTools.Text;
        _scrcpy.Text = status.ScrcpyServer.Text;
        _companion.Text = status.Companion.Text;
    }

    // Runs adb version, which shows the installed adb runs on this PC.
    private async Task ShowAdbVersionAsync(CancellationToken cancellation)
    {
        var adb = AndroidTools.Adb(_dataFolder);
        if (!File.Exists(adb))
        {
            _adbVersion.Text = "";
            return;
        }

        try
        {
            _adbVersion.Text = $"adb runs on this PC and reports: {await AdbProgram.VersionAsync(adb, cancellation)}.";
        }
        catch (Exception exception) when (exception is IOException or Win32Exception)
        {
            _adbVersion.Text = $"adb did not run: {exception.Message}";
        }
    }

    private async Task InstallAsync()
    {
        if (_running is not null || _accept.IsChecked != true)
        {
            return;
        }

        _running = new CancellationTokenSource();
        SetRunning(true);
        try
        {
            var archive = AndroidTools.PlatformToolsArchive;
            using var http = Client();
            var downloader = new ModelDownloader(http);
            Say($"Downloading Platform-Tools {AndroidTools.PlatformToolsVersion} from Google.");
            var path = await downloader.DownloadAsync(
                new CatalogFile(archive.Name, archive.Url, archive.Size, archive.Sha256),
                AndroidTools.DownloadsFolder(_dataFolder),
                Progress(),
                _running.Token);
            Say("Unpacking Platform-Tools.");
            var folder = AndroidTools.PlatformToolsFolder(_dataFolder);
            await Task.Run(() => PlatformToolsInstaller.Install(path, folder, AndroidTools.PlatformToolsVersion), _running.Token);
            RecordAcceptance();
            ShowStatus();
            await ShowAdbVersionAsync(_running.Token);
            _progress.Value = _progress.Maximum;
            Say($"Platform-Tools {AndroidTools.PlatformToolsVersion} installed in {folder}.");
        }
        catch (OperationCanceledException)
        {
            Say("Stopped. The download continues from where it stopped when it is started again.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Say(exception.Message);
        }
        finally
        {
            _running.Dispose();
            _running = null;
            SetRunning(false);
            ShowStatus();
        }
    }

    // Kept beside the tools, as the record that the licence was accepted
    // before they were downloaded.
    private void RecordAcceptance() =>
        File.WriteAllLines(Path.Combine(_dataFolder, "platform-tools-licence-accepted.txt"),
        [
            $"{AndroidTools.SdkLicenceName}, {AndroidTools.SdkLicenceAddress}",
            $"Accepted in the recorder's Android setup window by {Environment.UserName} on {DateTime.UtcNow:u}",
            $"Platform-Tools {AndroidTools.PlatformToolsVersion}, {AndroidTools.PlatformToolsArchive.Url}, sha256 {AndroidTools.PlatformToolsArchive.Sha256}"
        ]);

    private void SetRunning(bool running)
    {
        _install.IsEnabled = !running && _accept.IsChecked == true;
        _accept.IsEnabled = !running;
        _cancel.IsEnabled = running;
        if (!running)
        {
            _announcedTenth = -1;
        }
    }

    private Progress<DownloadProgress> Progress() => new(progress =>
    {
        var fraction = progress.Total == 0 ? 1 : (double)progress.Done / progress.Total;
        _progress.Value = fraction * _progress.Maximum;
        var verb = progress.Stage == "Checking" ? "Checking the SHA-256 of" : "Downloading";
        _status.Text = $"{verb} {progress.FileName}: {fraction:P0}, {ModelDownloader.Rate(progress.BytesPerSecond)}.";
        // Spoken at each tenth, not at every update.
        var tenth = (int)(fraction * 10);
        if (tenth != _announcedTenth)
        {
            _announcedTenth = tenth;
            Announce(_status.Text);
        }
    });

    private void Say(string message)
    {
        _status.Text = message;
        Announce(message);
    }

    private void Announce(string message)
    {
        var peer = UIElementAutomationPeer.FromElement(_status) ?? UIElementAutomationPeer.CreatePeerForElement(_status);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, message, ProgressActivityId);
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_running is null)
        {
            return;
        }

        if (MessageBox.Show(this, "The download is running. Stop it and close? It continues from where it stopped when it is started again.",
                "Stop the download?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _running.Cancel();
        }
        else
        {
            eventArgs.Cancel = true;
        }
    }

    private void OpenLicence()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AndroidTools.SdkLicenceAddress.ToString()) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception exception)
        {
            Say($"The browser could not be opened: {exception.Message}. The licence is at {AndroidTools.SdkLicenceAddress}.");
        }
    }

    private static HttpClient Client()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsA11yRecorder/" + (typeof(AndroidSetupWindow).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return client;
    }

    private static SelectableText Heading(string text, int level)
    {
        var heading = new SelectableText
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            FontSize = level == 1 ? 22 : 17,
            Margin = new Thickness(0, level == 1 ? 0 : 16, 0, 6)
        };
        AutomationProperties.SetHeadingLevel(heading, level == 1 ? AutomationHeadingLevel.Level1 : AutomationHeadingLevel.Level2);
        return heading;
    }

    private static SelectableText Paragraph(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };

    private static Button Command(string content, Action click)
    {
        var button = new Button { Content = content, MinWidth = 44, MinHeight = 44, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => click();
        return button;
    }
}
