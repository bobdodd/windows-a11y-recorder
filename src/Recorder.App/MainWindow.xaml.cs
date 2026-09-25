using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Recorder.Coordinator;
using Recorder.Session;
using Recorder.WindowsCapture;

namespace Recorder.App;

public partial class MainWindow : Window
{
    private static readonly string[] BrowserChannels =
    [
        "browser.lifecycle",
        "browser.listener",
        "browser.dispatch",
        "browser.timer",
        "browser.scheduler",
        "browser.navigation",
        "browser.dom",
        "browser.cookie",
        "browser.interaction",
        "browser.layout",
        "browser.network",
        "browser.accessibility"
    ];
    private static readonly HashSet<string> FilteredChannels =
    [
        "input.keyboard",
        "input.mouse",
        "accessibility.uia.events",
        "window.foreground",
        "graphics.desktop.frames",
        "audio.microphone",
        "audio.system",
        "session.annotations",
        .. BrowserChannels
    ];
    private static readonly JsonSerializerOptions InspectorJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _playbackTimer;
    private readonly Stopwatch _playbackStopwatch = new();
    private SessionCoordinator? _coordinator;
    private SessionAudioPlayer? _audioPlayer;
    private SessionPlaybackArchive? _playbackArchive;
    private IReadOnlyList<SessionTimelineEvent> _visibleTimelineEvents = [];
    private long _playbackAnchorNanoseconds;
    private long _playbackPositionNanoseconds;
    private long _timelineViewportStartNanoseconds;
    private long _timelineViewportDurationNanoseconds;
    private int _displayedFrameIndex = -1;
    private bool _allowClose;
    private bool _transitioning;
    private bool _isPlaying;
    private bool _updatingSlider;
    private bool _updatingTimelineControls;
    private bool _updatingBrowserNavigationSelection;

    public MainWindow()
    {
        InitializeComponent();
        OutputRootTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Windows A11y Recorder");
        ChromiumPathTextBox.Text = GetDefaultChromiumExecutablePath();
        _statusTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            (_, _) => RefreshStatus(),
            Dispatcher);
        _playbackTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Render,
            (_, _) => AdvancePlayback(),
            Dispatcher);
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += OnClosing;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_transitioning)
        {
            return;
        }

        if (!AnyChannelSelected())
        {
            MessageBox.Show(
                this,
                "Select at least one capture channel.",
                "No capture channels selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryValidateBrowserCapture(out var chromiumPath, out var browserStartUrl))
        {
            return;
        }

        PausePlayback();
        _transitioning = true;
        SetConfigurationEnabled(false);
        OpenRecordingButton.IsEnabled = false;
        StatusTextBlock.Text = "Starting recording.";

        try
        {
            _coordinator = new SessionCoordinator(WindowsCollectorFactory.Create);
            var status = await _coordinator.StartAsync(new RecordingOptions
            {
                OutputRoot = OutputRootTextBox.Text,
                CaptureKeyboardAndMouse = InputCheckBox.IsChecked == true,
                CaptureUiAutomation = AutomationCheckBox.IsChecked == true,
                CaptureForegroundWindow = WindowCheckBox.IsChecked == true,
                CaptureDesktopFrames = FramesCheckBox.IsChecked == true,
                CaptureMicrophone = MicrophoneCheckBox.IsChecked == true,
                CaptureSystemAudio = SystemAudioCheckBox.IsChecked == true,
                CaptureBrowserEvidence = BrowserEvidenceCheckBox.IsChecked == true,
                ChromiumExecutablePath = chromiumPath,
                BrowserStartUrl = browserStartUrl
            });
            SessionFolderTextBox.Text = status.SessionDirectory;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            MarkerButton.IsEnabled = true;
            OpenFolderButton.IsEnabled = true;
            _statusTimer.Start();
            RefreshStatus();
            StopButton.Focus();
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = "Recording could not start.";
            MessageBox.Show(
                this,
                exception.Message,
                "Recording could not start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await DisposeCoordinatorAsync();
            SetConfigurationEnabled(true);
            OpenRecordingButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            StartButton.Focus();
        }
        finally
        {
            _transitioning = false;
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopRecordingAsync();
    }

    private void MarkerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator?.AddMarker(MarkerNoteTextBox.Text) == true)
        {
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(MarkerNoteTextBox.Text)
                ? "Marker added."
                : $"Marker added: {MarkerNoteTextBox.Text.Trim()}";
            MarkerNoteTextBox.Clear();
            MarkerNoteTextBox.Focus();
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose session output folder",
            InitialDirectory = OutputRootTextBox.Text,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            OutputRootTextBox.Text = dialog.FolderName;
        }
    }

    private void BrowseChromiumButton_Click(object sender, RoutedEventArgs e)
    {
        var initialDirectory = Path.GetDirectoryName(ChromiumPathTextBox.Text);
        var dialog = new OpenFileDialog
        {
            Title = "Choose instrumented Chromium executable",
            Filter = "Chromium executable (chrome.exe)|chrome.exe|Executable files (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }
        if (dialog.ShowDialog(this) == true)
        {
            ChromiumPathTextBox.Text = dialog.FileName;
        }
    }

    private async void OpenRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        var initialDirectory = Directory.Exists(SessionFolderTextBox.Text)
            ? SessionFolderTextBox.Text
            : OutputRootTextBox.Text;
        var dialog = new OpenFolderDialog
        {
            Title = "Open recorded session",
            InitialDirectory = initialDirectory,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            await LoadSessionAsync(dialog.FolderName, validate: true);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = SessionFolderTextBox.Text;
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            PausePlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
    {
        StepFrame(-1);
    }

    private void NextFrameButton_Click(object sender, RoutedEventArgs e)
    {
        StepFrame(1);
    }

    private void PlaybackSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || _playbackArchive is null)
        {
            return;
        }

        SeekTo((long)(e.NewValue * 1_000_000_000), synchronizeAudio: _isPlaying);
    }

    private void AudioVolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyAudioLevels();
    }

    private void TimelineFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            ApplyTimelineFilters();
        }
    }

    private void SelectAllFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        SetAllTimelineFilters(true);
    }

    private void ClearAllFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        SetAllTimelineFilters(false);
    }

    private void TimelineZoomSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized && !_updatingTimelineControls)
        {
            ApplyTimelineZoom();
        }
    }

    private void FitTimelineButton_Click(object sender, RoutedEventArgs e)
    {
        TimelineZoomSlider.Value = 1;
        ApplyTimelineZoom();
        TimelineZoomSlider.Focus();
    }

    private void TimelinePanScrollBar_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_playbackArchive is null || _updatingTimelineControls)
        {
            return;
        }

        SetTimelineViewport((long)(e.NewValue * 1_000_000_000));
    }

    private void TimelineControl_SelectedEventChanged(
        object? sender,
        TimelineEventSelectedEventArgs e)
    {
        if (e.TimelineEvent is null)
        {
            EventDetailsTextBox.Text =
                "Select an event in the timeline to inspect its complete recorded values.";
            return;
        }

        PausePlayback();
        SeekTo(e.TimelineEvent.MonotonicNanoseconds, synchronizeAudio: false);
        using var document = JsonDocument.Parse(e.TimelineEvent.RawJson);
        EventDetailsTextBox.Text = JsonSerializer.Serialize(
            document.RootElement,
            InspectorJsonOptions);
        EventDetailsTextBox.CaretIndex = 0;
        EventDetailsTextBox.ScrollToHome();
        AutomationProperties.SetHelpText(
            EventDetailsTextBox,
            $"Selected {e.TimelineEvent.Channel}, " +
            $"{e.TimelineEvent.EventType}, " +
            $"{FormatTime(e.TimelineEvent.MonotonicNanoseconds)}");
    }

    private void BrowserNavigationListBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingBrowserNavigationSelection ||
            BrowserNavigationListBox.SelectedItem is not
                BrowserNavigationCorrelation navigation)
        {
            return;
        }

        PausePlayback();
        _updatingBrowserNavigationSelection = true;
        SeekTo(navigation.StartNanoseconds, synchronizeAudio: false);
        _updatingBrowserNavigationSelection = false;
        DisplayBrowserCorrelation(navigation);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            TimelineZoomSlider.IsEnabled)
        {
            if (e.Key is Key.OemPlus or Key.Add)
            {
                TimelineZoomSlider.Value = Math.Min(
                    TimelineZoomSlider.Maximum,
                    TimelineZoomSlider.Value + 1);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.OemMinus or Key.Subtract)
            {
                TimelineZoomSlider.Value = Math.Max(
                    TimelineZoomSlider.Minimum,
                    TimelineZoomSlider.Value - 1);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.D0 or Key.NumPad0)
            {
                TimelineZoomSlider.Value = 1;
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Space && PlayPauseButton.IsEnabled)
        {
            if (_isPlaying)
            {
                PausePlayback();
            }
            else
            {
                StartPlayback();
            }

            e.Handled = true;
        }
    }

    private async Task StopRecordingAsync()
    {
        if (_transitioning ||
            _coordinator?.State != RecordingSessionState.Recording)
        {
            return;
        }

        _transitioning = true;
        _statusTimer.Stop();
        StopButton.IsEnabled = false;
        MarkerButton.IsEnabled = false;
        StatusTextBlock.Text = "Stopping and verifying session files.";
        string? completedSession = null;

        try
        {
            var status = await _coordinator.StopAsync();
            completedSession = status.SessionDirectory;
            RefreshStatus(status);
            StatusTextBlock.Text = status.State == RecordingSessionState.Completed
                ? "Recording completed and session files verified."
                : $"Recording stopped with errors. {status.Message}";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = "Recording stopped with an error.";
            MessageBox.Show(
                this,
                exception.Message,
                "Recording error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            await DisposeCoordinatorAsync();
            SetConfigurationEnabled(true);
            OpenRecordingButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            OpenFolderButton.IsEnabled =
                Directory.Exists(SessionFolderTextBox.Text);
            _transitioning = false;
            StartButton.Focus();
        }

        if (Directory.Exists(completedSession))
        {
            await LoadSessionAsync(completedSession, validate: false);
        }
    }

    private async Task LoadSessionAsync(string sessionDirectory, bool validate)
    {
        PausePlayback();
        SetPlaybackEnabled(false);
        OpenRecordingButton.IsEnabled = false;
        PlaybackStatusTextBlock.Text = validate
            ? "Validating recording..."
            : "Loading recording...";

        try
        {
            if (validate)
            {
                var validation = await SessionArchiveValidator.ValidateAsync(
                    sessionDirectory);
                if (!validation.IsValid)
                {
                    var problems = string.Join(
                        Environment.NewLine,
                        validation.Issues
                            .Where(issue =>
                                issue.Severity == ArchiveValidationSeverity.Error)
                            .Take(8)
                            .Select(issue =>
                                $"{issue.Code}: {issue.Message}"));
                    throw new InvalidDataException(
                        "The recording failed archive validation." +
                        Environment.NewLine +
                        problems);
                }
            }

            CloseAudio();
            _playbackArchive = await SessionArchiveReader.LoadAsync(sessionDirectory);
            _playbackPositionNanoseconds = 0;
            _displayedFrameIndex = -1;
            TimelineControl.SetSession(
                _playbackArchive.Events,
                _playbackArchive.DurationNanoseconds);
            BrowserNavigationListBox.ItemsSource =
                _playbackArchive.BrowserNavigations;
            BrowserCorrelationTextBox.Text =
                _playbackArchive.BrowserNavigations.Count == 0
                    ? "This recording contains no browser navigation evidence."
                    : "Select a browser navigation, or move through playback, " +
                      "to inspect its correlated DOM and interaction evidence.";
            TimelineZoomSlider.Value = 1;
            ApplyTimelineFilters();
            ApplyTimelineZoom();
            PlaybackSlider.Maximum =
                _playbackArchive.DurationNanoseconds / 1_000_000_000d;
            ConfigureAudio(_playbackArchive);
            SetPlaybackEnabled(_playbackArchive.Frames.Count > 0);
            SessionFolderTextBox.Text = _playbackArchive.SessionDirectory;
            OpenFolderButton.IsEnabled = true;
            VideoPlaceholderTextBlock.Visibility =
                _playbackArchive.Frames.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            VideoPlaceholderTextBlock.Text = _playbackArchive.Frames.Count == 0
                ? "This recording contains no desktop frames."
                : string.Empty;
            SeekTo(0, synchronizeAudio: false);
            PlaybackStatusTextBlock.Text =
                $"{_playbackArchive.Manifest.SessionId} | " +
                $"{_playbackArchive.Frames.Count:N0} frames | " +
                $"{_playbackArchive.Events.Count:N0} events | " +
                $"{_playbackArchive.AudioTracks.Count} audio tracks";
            PlayPauseButton.Focus();
        }
        catch (Exception exception)
        {
            _playbackArchive = null;
            BrowserNavigationListBox.ItemsSource = null;
            BrowserCorrelationTextBox.Text =
                "The recording could not be opened.";
            VideoImage.Source = null;
            VideoPlaceholderTextBlock.Text = "The recording could not be opened.";
            VideoPlaceholderTextBlock.Visibility = Visibility.Visible;
            PlaybackStatusTextBlock.Text = "Recording load failed.";
            MessageBox.Show(
                this,
                exception.Message,
                "Could not open recording",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            OpenRecordingButton.IsEnabled =
                _coordinator?.State != RecordingSessionState.Recording;
        }
    }

    private void StartPlayback()
    {
        if (_playbackArchive is null ||
            _playbackArchive.Frames.Count == 0)
        {
            return;
        }

        if (_playbackPositionNanoseconds >= _playbackArchive.DurationNanoseconds)
        {
            SeekTo(0, synchronizeAudio: false);
        }

        _playbackAnchorNanoseconds = _playbackPositionNanoseconds;
        _playbackStopwatch.Restart();
        _isPlaying = true;
        PlayPauseButton.Content = "_Pause";
        SynchronizeAudio(_playbackPositionNanoseconds, play: true);
        _playbackTimer.Start();
    }

    private void PausePlayback()
    {
        if (_isPlaying && _playbackArchive is not null)
        {
            var elapsedNanoseconds = checked(_playbackStopwatch.Elapsed.Ticks * 100);
            var position = Math.Min(
                _playbackAnchorNanoseconds + elapsedNanoseconds,
                _playbackArchive.DurationNanoseconds);
            _isPlaying = false;
            SetPlaybackPosition(position);
        }

        _playbackTimer.Stop();
        _playbackStopwatch.Stop();
        _isPlaying = false;
        PlayPauseButton.Content = "_Play";
        _audioPlayer?.Pause();
    }

    private void AdvancePlayback()
    {
        if (!_isPlaying || _playbackArchive is null)
        {
            return;
        }

        var elapsedNanoseconds = checked(_playbackStopwatch.Elapsed.Ticks * 100);
        var position = _playbackAnchorNanoseconds + elapsedNanoseconds;
        if (position >= _playbackArchive.DurationNanoseconds)
        {
            SeekTo(_playbackArchive.DurationNanoseconds, synchronizeAudio: false);
            PausePlayback();
            PlaybackStatusTextBlock.Text = "Playback completed.";
            return;
        }

        SetPlaybackPosition(position);
        StartDueAudio(position);
    }

    private void SeekTo(long positionNanoseconds, bool synchronizeAudio)
    {
        if (_playbackArchive is null)
        {
            return;
        }

        var position = Math.Clamp(
            positionNanoseconds,
            0,
            _playbackArchive.DurationNanoseconds);
        _playbackPositionNanoseconds = position;
        if (_isPlaying)
        {
            _playbackAnchorNanoseconds = position;
            _playbackStopwatch.Restart();
        }

        SetPlaybackPosition(position);
        if (synchronizeAudio)
        {
            SynchronizeAudio(position, play: true);
        }
        else if (!_isPlaying)
        {
            SynchronizeAudio(position, play: false);
        }
    }

    private void SetPlaybackPosition(long positionNanoseconds)
    {
        if (_playbackArchive is null)
        {
            return;
        }

        _playbackPositionNanoseconds = positionNanoseconds;
        _updatingSlider = true;
        PlaybackSlider.Value = positionNanoseconds / 1_000_000_000d;
        _updatingSlider = false;
        TimelineControl.PositionNanoseconds = positionNanoseconds;
        EnsurePlayheadVisible(positionNanoseconds);
        PlaybackTimeTextBlock.Text =
            $"{FormatTime(positionNanoseconds)} / " +
            $"{FormatTime(_playbackArchive.DurationNanoseconds)}";
        AutomationProperties.SetHelpText(
            PlaybackSlider,
            $"{FormatTime(positionNanoseconds)} of " +
            $"{FormatTime(_playbackArchive.DurationNanoseconds)}");
        DisplayFrameAt(positionNanoseconds);
        DisplayNearestEvent(positionNanoseconds);
        DisplayBrowserCorrelationAt(positionNanoseconds);
    }

    private void DisplayBrowserCorrelationAt(long positionNanoseconds)
    {
        if (_updatingBrowserNavigationSelection ||
            _playbackArchive is null ||
            _playbackArchive.BrowserNavigations.Count == 0)
        {
            return;
        }

        var navigation = _playbackArchive.BrowserNavigations
            .LastOrDefault(item =>
                item.StartNanoseconds <= positionNanoseconds &&
                positionNanoseconds < item.EndNanoseconds &&
                item.PrimaryPage) ??
            _playbackArchive.BrowserNavigations
                .LastOrDefault(item =>
                    item.StartNanoseconds <= positionNanoseconds &&
                    positionNanoseconds < item.EndNanoseconds);
        if (navigation is null)
        {
            return;
        }

        _updatingBrowserNavigationSelection = true;
        BrowserNavigationListBox.SelectedItem = navigation;
        BrowserNavigationListBox.ScrollIntoView(navigation);
        _updatingBrowserNavigationSelection = false;
        DisplayBrowserCorrelation(navigation);
    }

    private void DisplayBrowserCorrelation(
        BrowserNavigationCorrelation navigation)
    {
        var completion = navigation.CompletedNanoseconds is null
            ? "No matching navigation completion was recorded."
            : navigation.Committed == true
                ? $"Completed as {navigation.Outcome ?? "committed"}."
                : $"Completed as {navigation.Outcome ?? "not committed"}.";
        var truncation = navigation.TruncatedCheckpointCount == 0
            ? "No correlated checkpoint was marked truncated."
            : $"{navigation.TruncatedCheckpointCount:N0} of " +
              $"{navigation.CheckpointCount:N0} correlated checkpoints " +
              "were marked truncated.";
        var accessibilityTruncation =
            navigation.TruncatedAccessibilityCheckpointCount == 0
                ? "No correlated accessibility checkpoint was marked truncated."
                : $"{navigation.TruncatedAccessibilityCheckpointCount:N0} of " +
                  $"{navigation.AccessibilityCheckpointCount:N0} correlated " +
                  "accessibility checkpoints were marked truncated.";
        BrowserCorrelationTextBox.Text =
            $"{navigation.Url}{Environment.NewLine}" +
            $"{completion} Correlation basis: {navigation.CorrelationBasis}." +
            $"{Environment.NewLine}" +
            $"Renderer: {navigation.RendererProcessId?.ToString() ?? "not recorded"}; " +
            $"DOM checkpoints: {navigation.CheckpointCount:N0}; " +
            $"DOM nodes: {navigation.DomNodeCount:N0}; " +
            $"accessibility checkpoints: {navigation.AccessibilityCheckpointCount:N0}; " +
            $"accessibility nodes: {navigation.AccessibilityNodeCount:N0}; " +
            $"dispatches: {navigation.DispatchCount:N0}; " +
            $"listener invocations: {navigation.ListenerInvocationCount:N0}; " +
            $"related records: {navigation.RelatedEventCount:N0}." +
            $"{Environment.NewLine}{truncation} {accessibilityTruncation}";
        BrowserCorrelationTextBox.CaretIndex = 0;
        BrowserCorrelationTextBox.ScrollToHome();
        AutomationProperties.SetHelpText(
            BrowserCorrelationTextBox,
            $"Navigation at {FormatTime(navigation.StartNanoseconds)}. " +
            $"{navigation.CheckpointCount:N0} DOM checkpoints, " +
            $"{navigation.AccessibilityCheckpointCount:N0} accessibility checkpoints, " +
            $"{navigation.DispatchCount:N0} dispatches, and " +
            $"{navigation.ListenerInvocationCount:N0} listener invocations. " +
            truncation + " " + accessibilityTruncation);
    }

    private void DisplayFrameAt(long positionNanoseconds)
    {
        if (_playbackArchive is null || _playbackArchive.Frames.Count == 0)
        {
            return;
        }

        var index = FindFrameAtOrBefore(
            _playbackArchive.Frames,
            positionNanoseconds);
        if (index == _displayedFrameIndex)
        {
            return;
        }

        _displayedFrameIndex = index;
        if (index < 0)
        {
            VideoImage.Source = null;
            return;
        }

        var frame = _playbackArchive.Frames[index];
        using var stream = new FileStream(
            frame.AbsolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        VideoImage.Source = bitmap;
        AutomationProperties.SetHelpText(
            VideoImage,
            $"Desktop frame {index + 1} of {_playbackArchive.Frames.Count}, " +
            $"{FormatTime(frame.MonotonicNanoseconds)}");
    }

    private void DisplayNearestEvent(long positionNanoseconds)
    {
        if (_playbackArchive is null || _visibleTimelineEvents.Count == 0)
        {
            PlaybackStatusTextBlock.Text = "No events match the current filters.";
            return;
        }

        var index = FindEventAtOrBefore(
            _visibleTimelineEvents,
            positionNanoseconds);
        if (index < 0)
        {
            PlaybackStatusTextBlock.Text = "No matching event before this position.";
            return;
        }

        var item = _visibleTimelineEvents[index];
        PlaybackStatusTextBlock.Text =
            $"{FormatTime(item.MonotonicNanoseconds)} | " +
            $"{item.Channel} | {item.Summary}";
    }

    private void StepFrame(int direction)
    {
        if (_playbackArchive is null || _playbackArchive.Frames.Count == 0)
        {
            return;
        }

        PausePlayback();
        var current = FindFrameAtOrBefore(
            _playbackArchive.Frames,
            _playbackPositionNanoseconds);
        var target = direction < 0
            ? Math.Max(0, current - 1)
            : Math.Min(_playbackArchive.Frames.Count - 1, current + 1);
        SeekTo(
            _playbackArchive.Frames[target].MonotonicNanoseconds,
            synchronizeAudio: false);
    }

    private void ConfigureAudio(SessionPlaybackArchive archive)
    {
        _audioPlayer = new SessionAudioPlayer(archive.AudioTracks);
        ApplyAudioLevels();
    }

    private void ApplyAudioLevels()
    {
        var microphoneGain = MicrophoneVolumeSlider.Value;
        var systemGain = SystemAudioVolumeSlider.Value;
        _audioPlayer?.SetGains(microphoneGain, systemGain);
        MicrophoneVolumeTextBlock.Text = FormatGain(microphoneGain);
        SystemAudioVolumeTextBlock.Text = FormatGain(systemGain);
        AutomationProperties.SetHelpText(
            MicrophoneVolumeSlider,
            $"Microphone playback gain {FormatGain(microphoneGain)}");
        AutomationProperties.SetHelpText(
            SystemAudioVolumeSlider,
            $"System audio playback gain {FormatGain(systemGain)}");
    }

    private void SynchronizeAudio(long positionNanoseconds, bool play)
    {
        _audioPlayer?.Seek(positionNanoseconds, play);
    }

    private void StartDueAudio(long positionNanoseconds)
    {
        _audioPlayer?.StartDueTracks(positionNanoseconds);
    }

    private void CloseAudio()
    {
        _audioPlayer?.Dispose();
        _audioPlayer = null;
    }

    private void SetPlaybackEnabled(bool enabled)
    {
        PlayPauseButton.IsEnabled = enabled;
        PreviousFrameButton.IsEnabled = enabled;
        NextFrameButton.IsEnabled = enabled;
        PlaybackSlider.IsEnabled = enabled;
        var timelineEnabled = _playbackArchive is not null &&
            _playbackArchive.DurationNanoseconds > 0;
        TimelineZoomSlider.IsEnabled = timelineEnabled;
        FitTimelineButton.IsEnabled = timelineEnabled;
        TimelinePanScrollBar.IsEnabled = timelineEnabled &&
            TimelineZoomSlider.Value > 1;
    }

    private void ApplyTimelineFilters()
    {
        if (_playbackArchive is null)
        {
            _visibleTimelineEvents = [];
            FilterSummaryTextBlock.Text = "No recording loaded.";
            return;
        }

        var visibleChannels = new HashSet<string>(StringComparer.Ordinal);
        AddVisibleChannel(FilterKeyboardCheckBox, "input.keyboard", visibleChannels);
        AddVisibleChannel(FilterMouseCheckBox, "input.mouse", visibleChannels);
        AddVisibleChannel(
            FilterAutomationCheckBox,
            "accessibility.uia.events",
            visibleChannels);
        AddVisibleChannel(FilterWindowCheckBox, "window.foreground", visibleChannels);
        AddVisibleChannel(
            FilterFramesCheckBox,
            "graphics.desktop.frames",
            visibleChannels);
        AddVisibleChannel(FilterMicrophoneCheckBox, "audio.microphone", visibleChannels);
        AddVisibleChannel(FilterSystemAudioCheckBox, "audio.system", visibleChannels);
        AddVisibleChannel(
            FilterMarkersCheckBox,
            "session.annotations",
            visibleChannels);
        if (FilterBrowserCheckBox.IsChecked == true)
        {
            foreach (var channel in BrowserChannels)
            {
                visibleChannels.Add(channel);
            }
        }
        var showOther = FilterOtherCheckBox.IsChecked == true;

        _visibleTimelineEvents = _playbackArchive.Events
            .Where(item =>
                visibleChannels.Contains(item.Channel) ||
                (showOther && !FilteredChannels.Contains(item.Channel)))
            .ToArray();
        TimelineControl.SetVisibleChannels(visibleChannels, showOther);
        FilterSummaryTextBlock.Text =
            $"{_visibleTimelineEvents.Count:N0} of " +
            $"{_playbackArchive.Events.Count:N0} events shown";
        DisplayNearestEvent(_playbackPositionNanoseconds);
    }

    private static void AddVisibleChannel(
        System.Windows.Controls.CheckBox checkBox,
        string channel,
        ISet<string> channels)
    {
        if (checkBox.IsChecked == true)
        {
            channels.Add(channel);
        }
    }

    private void SetAllTimelineFilters(bool selected)
    {
        FilterKeyboardCheckBox.IsChecked = selected;
        FilterMouseCheckBox.IsChecked = selected;
        FilterAutomationCheckBox.IsChecked = selected;
        FilterWindowCheckBox.IsChecked = selected;
        FilterFramesCheckBox.IsChecked = selected;
        FilterMicrophoneCheckBox.IsChecked = selected;
        FilterSystemAudioCheckBox.IsChecked = selected;
        FilterMarkersCheckBox.IsChecked = selected;
        FilterBrowserCheckBox.IsChecked = selected;
        FilterOtherCheckBox.IsChecked = selected;
        ApplyTimelineFilters();
    }

    private void ApplyTimelineZoom()
    {
        if (_playbackArchive is null ||
            _playbackArchive.DurationNanoseconds <= 0)
        {
            return;
        }

        var zoom = Math.Max(1, TimelineZoomSlider.Value);
        var duration = Math.Max(
            1,
            (long)(_playbackArchive.DurationNanoseconds / zoom));
        _timelineViewportDurationNanoseconds = duration;
        TimelineZoomTextBlock.Text = $"{zoom:0}×";
        AutomationProperties.SetHelpText(
            TimelineZoomSlider,
            $"{zoom:0} times zoom, visible duration {FormatTime(duration)}");

        var centeredStart = _playbackPositionNanoseconds - duration / 2;
        SetTimelineViewport(centeredStart);
        TimelinePanScrollBar.IsEnabled = zoom > 1;
    }

    private void SetTimelineViewport(long startNanoseconds)
    {
        if (_playbackArchive is null)
        {
            return;
        }

        var maximumStart = Math.Max(
            0,
            _playbackArchive.DurationNanoseconds -
            _timelineViewportDurationNanoseconds);
        _timelineViewportStartNanoseconds = Math.Clamp(
            startNanoseconds,
            0,
            maximumStart);
        TimelineControl.SetViewport(
            _timelineViewportStartNanoseconds,
            _timelineViewportDurationNanoseconds);

        _updatingTimelineControls = true;
        TimelinePanScrollBar.Maximum = maximumStart / 1_000_000_000d;
        TimelinePanScrollBar.ViewportSize =
            _timelineViewportDurationNanoseconds / 1_000_000_000d;
        TimelinePanScrollBar.LargeChange =
            Math.Max(0.1, TimelinePanScrollBar.ViewportSize * 0.9);
        TimelinePanScrollBar.SmallChange =
            Math.Max(0.01, TimelinePanScrollBar.ViewportSize * 0.1);
        TimelinePanScrollBar.Value =
            _timelineViewportStartNanoseconds / 1_000_000_000d;
        _updatingTimelineControls = false;
        AutomationProperties.SetHelpText(
            TimelinePanScrollBar,
            $"Visible range starts at {FormatTime(_timelineViewportStartNanoseconds)}");
    }

    private void EnsurePlayheadVisible(long positionNanoseconds)
    {
        if (_timelineViewportDurationNanoseconds <= 0)
        {
            return;
        }

        var viewportEnd = _timelineViewportStartNanoseconds +
            _timelineViewportDurationNanoseconds;
        if (positionNanoseconds < _timelineViewportStartNanoseconds)
        {
            SetTimelineViewport(
                positionNanoseconds - _timelineViewportDurationNanoseconds / 10);
        }
        else if (positionNanoseconds > viewportEnd)
        {
            SetTimelineViewport(
                positionNanoseconds -
                _timelineViewportDurationNanoseconds * 9 / 10);
        }
    }

    private void RefreshStatus()
    {
        if (_coordinator is not null)
        {
            RefreshStatus(_coordinator.GetStatus());
        }
    }

    private void RefreshStatus(RecordingSessionStatus status)
    {
        if (status.State == RecordingSessionState.Recording)
        {
            StatusTextBlock.Text = status.DroppedEvents == 0
                ? "Recording."
                : $"Recording with {status.DroppedEvents:N0} dropped events.";
        }

        DurationTextBlock.Text =
            $"Duration: {status.Elapsed:hh\\:mm\\:ss}";
        EvidenceTextBlock.Text =
            $"Events: {status.AcceptedEvents:N0} accepted, " +
            $"{status.DroppedEvents:N0} dropped";
        CollectorStatusListBox.ItemsSource = status.Collectors.Select(collector =>
            $"{collector.CollectorType}: {collector.Lifecycle}, {collector.Health}")
            .ToArray();
    }

    private void SetConfigurationEnabled(bool enabled)
    {
        InputCheckBox.IsEnabled = enabled;
        AutomationCheckBox.IsEnabled = enabled;
        WindowCheckBox.IsEnabled = enabled;
        FramesCheckBox.IsEnabled = enabled;
        MicrophoneCheckBox.IsEnabled = enabled;
        SystemAudioCheckBox.IsEnabled = enabled;
        BrowserEvidenceCheckBox.IsEnabled = enabled;
        BrowserCaptureGroupBox.IsEnabled = enabled;
        OutputRootTextBox.IsEnabled = enabled;
        BrowseButton.IsEnabled = enabled;
    }

    private bool AnyChannelSelected() =>
        InputCheckBox.IsChecked == true ||
        AutomationCheckBox.IsChecked == true ||
        WindowCheckBox.IsChecked == true ||
        FramesCheckBox.IsChecked == true ||
        MicrophoneCheckBox.IsChecked == true ||
        SystemAudioCheckBox.IsChecked == true ||
        BrowserEvidenceCheckBox.IsChecked == true;

    private bool TryValidateBrowserCapture(
        out string? chromiumPath,
        out string? browserStartUrl)
    {
        chromiumPath = null;
        browserStartUrl = null;
        if (BrowserEvidenceCheckBox.IsChecked != true)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(ChromiumPathTextBox.Text))
        {
            ShowBrowserValidationError(
                "Choose the instrumented Chromium executable.",
                ChromiumPathTextBox);
            return false;
        }

        try
        {
            chromiumPath = Path.GetFullPath(ChromiumPathTextBox.Text.Trim());
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowBrowserValidationError(
                "Enter a valid path to the instrumented Chromium executable.",
                ChromiumPathTextBox);
            return false;
        }
        if (!File.Exists(chromiumPath))
        {
            ShowBrowserValidationError(
                "The selected instrumented Chromium executable does not exist.",
                ChromiumPathTextBox);
            return false;
        }

        var startUrlText = BrowserStartUrlTextBox.Text.Trim();
        if (startUrlText.Length == 0)
        {
            return true;
        }

        if (!Uri.TryCreate(startUrlText, UriKind.Absolute, out _))
        {
            ShowBrowserValidationError(
                "Enter an absolute starting website address, or about:blank.",
                BrowserStartUrlTextBox);
            return false;
        }

        browserStartUrl = startUrlText;
        return true;
    }

    private void ShowBrowserValidationError(
        string message,
        System.Windows.Controls.Control control)
    {
        MessageBox.Show(
            this,
            message,
            "Instrumented Chromium configuration",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        control.Focus();
    }

    private static string GetDefaultChromiumExecutablePath()
    {
        var bundledPath = Path.Combine(
            AppContext.BaseDirectory,
            "browser",
            "chrome.exe");
        var developmentPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "chromium-dev",
            "chromium",
            "src",
            "out",
            "A11yRecorder",
            "chrome.exe");

        return File.Exists(bundledPath) || !File.Exists(developmentPath)
            ? bundledPath
            : developmentPath;
    }

    private async void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_allowClose)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_transitioning)
        {
            return;
        }

        PausePlayback();
        CloseAudio();
        if (_coordinator?.State == RecordingSessionState.Recording)
        {
            await StopRecordingAsync();
        }

        await DisposeCoordinatorAsync();
        _allowClose = true;
        Application.Current.Shutdown();
    }

    private async Task DisposeCoordinatorAsync()
    {
        if (_coordinator is not null)
        {
            await _coordinator.DisposeAsync();
            _coordinator = null;
        }
    }

    private static int FindFrameAtOrBefore(
        IReadOnlyList<SessionVideoFrame> frames,
        long positionNanoseconds)
    {
        var low = 0;
        var high = frames.Count - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (frames[middle].MonotonicNanoseconds <= positionNanoseconds)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }

    private static int FindEventAtOrBefore(
        IReadOnlyList<SessionTimelineEvent> events,
        long positionNanoseconds)
    {
        var low = 0;
        var high = events.Count - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (events[middle].MonotonicNanoseconds <= positionNanoseconds)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }

    private static string FormatTime(long nanoseconds)
    {
        var value = TimeSpan.FromTicks(Math.Max(0, nanoseconds) / 100);
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:" +
            $"{value.Seconds:00}.{value.Milliseconds:000}";
    }

    private static string FormatGain(double decibels) =>
        decibels > 0
            ? $"+{decibels:0} dB"
            : $"{decibels:0} dB";
}
