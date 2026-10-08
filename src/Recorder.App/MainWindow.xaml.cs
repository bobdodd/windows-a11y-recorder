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
using Npgsql;
using Recorder.Coordinator;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.Recreation;
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
        "browser.presentation",
        "browser.network",
        "browser.resources",
        "browser.accessibility",
        "browser.compositor",
        "browser.animation",
        "browser.script"
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
    private readonly BusyIndicator _busy;
    private readonly HashSet<string> _announcedHealthReasons =
        new(StringComparer.Ordinal);
    private SessionCoordinator? _coordinator;
    private RecreationSession? _recreation;
    private SessionDatabase? _database;
    private string? _databaseStartError;
    private SessionAudioPlayer? _audioPlayer;
    private SessionPlaybackArchive? _playbackArchive;
    private RecordingFileDocuments? _recordedDocuments;
    private HashSet<string> _visibleTimelineChannels = new(StringComparer.Ordinal);
    private long _visibleTimelineEventCount;

    // The status line shows the event at or before the playhead. Lookups
    // are coalesced: while one runs, only the latest position waits, and a
    // result is discarded if the recording, filters, or status line changed
    // since it was requested.
    private long? _pendingNearestPosition;
    private bool _nearestLookupRunning;
    private long _nearestVersion;
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
        _busy = new BusyIndicator(
            this,
            BusyPanel,
            BusyProgressBar,
            BusyTextBlock);
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
        Loaded += OnLoaded;
    }

    // The database starts before a recording can. Recordings are stored in
    // it and opened from it, so while it cannot start the app neither
    // records nor opens recordings, and says why.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        LoadLayout();
        _transitioning = true;
        StartButton.IsEnabled = false;
        OpenRecordingButton.IsEnabled = false;
        StatusTextBlock.Text = "Starting the database.";
        var busy = _busy.Begin("Starting the database.");
        try
        {
            _database = await SessionDatabase.StartAsync(RecorderDatabaseLocation.ForCurrentUser());
            StatusTextBlock.Text = _database.InterruptedRecordingsAtStart == 0
                ? "Ready to record."
                : $"Ready to record. {_database.InterruptedRecordingsAtStart:N0} recordings left open " +
                    "by an earlier run were marked interrupted in the database.";
            busy.Dispose();
            _busy.AnnounceCompleted(StatusTextBlock.Text);
        }
        catch (Exception exception)
        {
            _databaseStartError = exception.Message;
            StatusTextBlock.Text = DatabaseUnavailableText;
            busy.Dispose();
            MessageBox.Show(
                this,
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                "Recordings cannot be made or opened until the database starts. " +
                "Close the app and start it again.",
                "The database could not start",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            busy.Dispose();
            _transitioning = false;
            StartButton.IsEnabled = _database is not null;
            OpenRecordingButton.IsEnabled = _database is not null;
        }
    }

    private string DatabaseUnavailableText =>
        "The database could not start, so recordings cannot be made or opened. " +
        _databaseStartError;

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_transitioning || _database is null)
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

        if (!TryValidateBrowserCapture(
                out var chromiumPath,
                out var browserStartUrl,
                out var fullWalkInterval))
        {
            return;
        }

        PausePlayback();
        _transitioning = true;
        SetConfigurationEnabled(false);
        OpenRecordingButton.IsEnabled = false;
        StatusTextBlock.Text = "Starting recording.";
        _announcedHealthReasons.Clear();
        var busy = _busy.Begin("Starting recording.");

        try
        {
            _coordinator = new SessionCoordinator(WindowsCollectorFactory.Create, _database);
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
                BrowserStartUrl = browserStartUrl,
                BrowserFullWalkInterval = fullWalkInterval
            });
            SessionFolderTextBox.Text = status.SessionDirectory;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            MarkerButton.IsEnabled = true;
            OpenFolderButton.IsEnabled = true;
            ApplyLayout();
            _statusTimer.Start();
            RefreshStatus();
            busy.Dispose();
            _busy.AnnounceCompleted("Recording started.");
            StopButton.Focus();
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = "Recording could not start.";
            busy.Dispose();
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
            busy.Dispose();
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
            await LoadSessionAsync(dialog.FolderName);
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

    private void TimelineControl_LookupFailed(object? sender, string message)
    {
        _nearestVersion++;
        PlaybackStatusTextBlock.Text = $"The timeline could not be read: {message}";
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
        if (!DetailsShown)
        {
            // With the details hidden, the status line names the selected
            // event, in place of the event at the playhead the seek looked
            // up; the lookup's result is discarded.
            _nearestVersion++;
            _pendingNearestPosition = null;
            PlaybackStatusTextBlock.Text =
                $"Selected {FormatTime(e.TimelineEvent.MonotonicNanoseconds)} | " +
                $"{e.TimelineEvent.Channel} | {e.TimelineEvent.EventType} | " +
                e.TimelineEvent.Summary;
        }

        try
        {
            var rawJson = _playbackArchive?.ReadEventJson(e.TimelineEvent) ??
                throw new InvalidOperationException("No recording is open.");
            using var document = JsonDocument.Parse(rawJson);
            EventDetailsTextBox.Text = JsonSerializer.Serialize(
                document.RootElement,
                InspectorJsonOptions);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or JsonException)
        {
            EventDetailsTextBox.Text =
                $"The complete record for event {e.TimelineEvent.EventId} " +
                $"could not be read: {exception.Message}";
        }
        EventDetailsTextBox.CaretIndex = 0;
        EventDetailsTextBox.ScrollToHome();
        AutomationProperties.SetHelpText(
            EventDetailsTextBox,
            $"Selected {e.TimelineEvent.Channel}, " +
            $"{e.TimelineEvent.EventType}, " +
            $"{FormatTime(e.TimelineEvent.MonotonicNanoseconds)}");
    }

    private void BrowserNavigationFilter_Click(object sender, RoutedEventArgs e) =>
        ApplyBrowserNavigationFilter();

    // Lists the navigations of the kinds selected in the filter, and shows
    // how many of each kind the recording holds. Every navigation stays in
    // the recording; the filter only chooses which are listed.
    private void ApplyBrowserNavigationFilter()
    {
        var navigations = _playbackArchive?.BrowserNavigations ?? [];
        var boxes = new (BrowserNavigationKind Kind, System.Windows.Controls.CheckBox Box)[]
        {
            (BrowserNavigationKind.Page, ShowPageNavigationsCheckBox),
            (BrowserNavigationKind.Iframe, ShowIframeNavigationsCheckBox),
            (BrowserNavigationKind.BrowserUi, ShowBrowserUiNavigationsCheckBox),
            (BrowserNavigationKind.OtherFrame, ShowOtherFrameNavigationsCheckBox)
        };
        var shown = new HashSet<BrowserNavigationKind>();
        foreach (var (kind, box) in boxes)
        {
            var count = navigations.Count(item => item.Kind == kind);
            box.Content = $"{BrowserNavigationKinds.DescribePlural(kind)} ({count:N0})";
            if (box.IsChecked == true)
            {
                shown.Add(kind);
            }
        }

        var selected = BrowserNavigationListBox.SelectedItem;
        var listed = navigations.Where(item => shown.Contains(item.Kind)).ToArray();
        _updatingBrowserNavigationSelection = true;
        BrowserNavigationListBox.ItemsSource = _playbackArchive is null ? null : listed;
        if (selected is BrowserNavigationCorrelation navigation &&
            listed.Contains(navigation))
        {
            BrowserNavigationListBox.SelectedItem = navigation;
            BrowserNavigationListBox.ScrollIntoView(navigation);
        }

        _updatingBrowserNavigationSelection = false;
        AutomationProperties.SetHelpText(
            BrowserNavigationListBox,
            $"{listed.Length:N0} of {navigations.Count:N0} navigations listed. " +
            "Select a navigation to seek to it and inspect its correlated browser evidence.");
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
        SeekTo(navigation.SeekNanoseconds, synchronizeAudio: false);
        _updatingBrowserNavigationSelection = false;
        DisplayBrowserCorrelation(navigation);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleLayoutKey(e))
        {
            e.Handled = true;
            return;
        }

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
        StatusTextBlock.Text = "Stopping and storing the recording.";
        string? completedSession = null;
        var busy = _busy.Begin("Stopping recording and storing its events.");

        try
        {
            var status = await _coordinator.StopAsync();
            completedSession = status.SessionDirectory;
            RefreshStatus(status);
            StatusTextBlock.Text = status.State == RecordingSessionState.Completed
                ? "Recording completed and stored."
                : $"Recording stopped with errors. {status.Message}";
            if (status.Database?.Problem is { } databaseProblem)
            {
                StatusTextBlock.Text += $" Database: {databaseProblem}";
            }
            _busy.AnnounceCompleted(StatusTextBlock.Text);
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = "Recording stopped with an error.";
            busy.Dispose();
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
            busy.Dispose();
            SetConfigurationEnabled(true);
            OpenRecordingButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            OpenFolderButton.IsEnabled =
                Directory.Exists(SessionFolderTextBox.Text);
            _transitioning = false;
            ApplyLayout();
            if (SidePanel.IsVisible)
            {
                StartButton.Focus();
            }
            else
            {
                SettingsToggle.Focus();
            }
        }

        if (Directory.Exists(completedSession))
        {
            await LoadSessionAsync(completedSession);
        }
    }

    // A recording is opened from the database; its frames and audio are
    // read from the session folder. A recording the database does not hold,
    // or one still being recorded, is not opened, and the reason is shown.
    // A recording stored as failed or interrupted opens with the events the
    // database holds, and its stored status is shown with it.
    private async Task LoadSessionAsync(string sessionDirectory)
    {
        PausePlayback();
        SetPlaybackEnabled(false);
        OpenRecordingButton.IsEnabled = false;
        PlaybackStatusTextBlock.Text = "Loading recording...";
        var busy = _busy.Begin("Loading recording for playback.");

        try
        {
            var opened = await OpenFromDatabaseAsync(sessionDirectory);
            var archive = opened.Archive ?? throw new InvalidDataException(
                opened.Reason ?? "The database could not open this recording.");
            var place = opened.RecordingFile is null ? "from the database" : "from its recording file";
            var source = opened.Status is null or RecordingStatus.Completed
                ? place
                : $"{place}. Stored as {opened.Status.Value.ToString().ToLowerInvariant()}";
            if (opened.FileNote is { } note)
            {
                source += $". {note.TrimEnd('.')}";
            }

            CloseAudio();
            CloseRecordingFile();
            _playbackArchive = archive;
            _recordedDocuments = opened.Documents;
            _playbackPositionNanoseconds = 0;
            _displayedFrameIndex = -1;
            ResetFrameView();
            TimelineControl.SetSession(
                _playbackArchive.Timeline,
                _playbackArchive.DurationNanoseconds);
            ApplyBrowserNavigationFilter();
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
                $"{_playbackArchive.Timeline.Count:N0} events | " +
                $"{_playbackArchive.AudioTracks.Count} audio tracks | " +
                $"Read {source}";
            _nearestVersion++;
            ApplyLayout();
            busy.Dispose();
            _busy.AnnounceCompleted(
                $"Recording loaded {source}. " +
                $"{_playbackArchive.Frames.Count:N0} frames, " +
                $"{_playbackArchive.Timeline.Count:N0} events.");
            PlayPauseButton.Focus();
        }
        catch (Exception exception)
        {
            CloseRecordingFile();
            _playbackArchive = null;
            ApplyBrowserNavigationFilter();
            BrowserCorrelationTextBox.Text =
                "The recording could not be opened.";
            VideoImage.Source = null;
            VideoPlaceholderTextBlock.Text = "The recording could not be opened.";
            VideoPlaceholderTextBlock.Visibility = Visibility.Visible;
            PlaybackStatusTextBlock.Text = "Recording load failed.";
            ResetFrameView();
            ApplyLayout();
            busy.Dispose();
            MessageBox.Show(
                this,
                exception.Message,
                "Could not open recording",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            busy.Dispose();
            OpenRecordingButton.IsEnabled =
                _coordinator?.State != RecordingSessionState.Recording;
        }
    }

    // A recording read from its recording file keeps the file open until
    // another recording is opened.
    private void CloseRecordingFile()
    {
        _recordedDocuments = null;
        (_playbackArchive?.Timeline as IDisposable)?.Dispose();
    }

    private async Task<DatabasePlaybackResult> OpenFromDatabaseAsync(
        string sessionDirectory)
    {
        if (_database is null)
        {
            return new DatabasePlaybackResult(
                null,
                null,
                DatabaseUnavailableText);
        }

        try
        {
            return await _database.OpenRecordingAsync(sessionDirectory);
        }
        catch (Exception exception) when (
            exception is NpgsqlException or InvalidOperationException or
                InvalidDataException or IOException or JsonException or
                ObjectDisposedException)
        {
            return new DatabasePlaybackResult(
                null,
                null,
                $"The database could not be read: {exception.Message}");
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

        // A navigation of the top-level page is shown from its first frame
        // until the next one's first frame, so the correlation describes the
        // page the frame shows. Iframe navigations are also in the primary
        // page, so the frame type, not primaryPage, selects them.
        var navigation = _playbackArchive.BrowserNavigations
            .Where(item =>
                item.IsPageNavigation &&
                item.SeekNanoseconds <= positionNanoseconds)
            .OrderBy(item => item.SeekNanoseconds)
            .ThenBy(item => item.StartNanoseconds)
            .LastOrDefault() ??
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
            $"{Environment.NewLine}{DescribeFirstFrame(navigation)}" +
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
            DescribeFirstFrame(navigation) + " " +
            $"{navigation.CheckpointCount:N0} DOM checkpoints, " +
            $"{navigation.AccessibilityCheckpointCount:N0} accessibility checkpoints, " +
            $"{navigation.DispatchCount:N0} dispatches, and " +
            $"{navigation.ListenerInvocationCount:N0} listener invocations. " +
            truncation + " " + accessibilityTruncation);
    }

    private static string DescribeFirstFrame(BrowserNavigationCorrelation navigation) =>
        navigation.FirstFrameBasis switch
        {
            BrowserNavigationFrameBasis.PresentationFeedback =>
                $"First frame: {FormatTime(navigation.SeekNanoseconds)}, the first captured frame " +
                "composed after Chromium presented the page's first rendering update.",
            BrowserNavigationFrameBasis.NavigationCompletion =>
                $"First frame: {FormatTime(navigation.SeekNanoseconds)}, the first captured frame " +
                "composed after the navigation completed. No rendering update of its document was " +
                (navigation.Kind == BrowserNavigationKind.Page
                    ? "recorded as presented, so this frame can still show the previous page."
                    : "recorded as presented. A frame of this kind is often not drawn, for " +
                      "example when it is hidden or off screen."),
            _ => "No captured frame was matched to this navigation; playback goes to its start."
        };

    private void DisplayFrameAt(long positionNanoseconds)
    {
        if (_playbackArchive is null || _playbackArchive.Frames.Count == 0)
        {
            return;
        }

        var index = FindFrameAtOrBefore(
            _playbackArchive.Frames,
            positionNanoseconds);
        if (index == _displayedFrameIndex && !_redrawFrame)
        {
            return;
        }

        _redrawFrame = false;
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
        VideoImage.Source = FrameImage(frame, bitmap, out var view);
        AutomationProperties.SetHelpText(
            VideoImage,
            $"Desktop frame {index + 1} of {_playbackArchive.Frames.Count}, " +
            $"{FormatTime(frame.MonotonicNanoseconds)}{view}");
    }

    private async void DisplayNearestEvent(long positionNanoseconds)
    {
        if (_playbackArchive is null || _visibleTimelineEventCount == 0)
        {
            _nearestVersion++;
            PlaybackStatusTextBlock.Text = "No events match the current filters.";
            return;
        }

        _pendingNearestPosition = positionNanoseconds;
        if (_nearestLookupRunning)
        {
            return;
        }

        _nearestLookupRunning = true;
        try
        {
            while (_pendingNearestPosition is { } position && _playbackArchive is { } archive)
            {
                _pendingNearestPosition = null;
                var version = _nearestVersion;
                var item = await archive.Timeline
                    .AtOrBeforeAsync(position, _visibleTimelineChannels)
                    .ConfigureAwait(true);
                if (version != _nearestVersion || !ReferenceEquals(archive, _playbackArchive))
                {
                    continue;
                }

                PlaybackStatusTextBlock.Text = item is null
                    ? "No matching event before this position."
                    : $"{FormatTime(item.MonotonicNanoseconds)} | " +
                      $"{item.Channel} | {item.Summary}";
            }
        }
        catch (Exception exception) when (
            exception is NpgsqlException or InvalidOperationException or
                InvalidDataException or ObjectDisposedException or IOException)
        {
            _pendingNearestPosition = null;
            PlaybackStatusTextBlock.Text =
                $"The timeline could not be read: {exception.Message}";
        }
        finally
        {
            _nearestLookupRunning = false;
        }
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
        InspectPageButton.IsEnabled = enabled && _recordedDocuments is not null;
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
            _visibleTimelineChannels = new HashSet<string>(StringComparer.Ordinal);
            _visibleTimelineEventCount = 0;
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

        var timeline = _playbackArchive.Timeline;
        _visibleTimelineChannels = timeline.ChannelCounts.Keys
            .Where(channel =>
                visibleChannels.Contains(channel) ||
                (showOther && !FilteredChannels.Contains(channel)))
            .ToHashSet(StringComparer.Ordinal);
        _visibleTimelineEventCount = _visibleTimelineChannels.Sum(
            channel => timeline.ChannelCounts[channel]);
        _nearestVersion++;
        TimelineControl.SetVisibleChannels(_visibleTimelineChannels);
        FilterSummaryTextBlock.Text =
            $"{_visibleTimelineEventCount:N0} of " +
            $"{timeline.Count:N0} events shown";
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
            $"{status.DroppedEvents:N0} dropped" +
            DatabaseEvidenceText(status.Database);
        CollectorStatusListBox.ItemsSource = status.Collectors.Select(collector =>
            collector.HealthReason is null
                ? $"{collector.CollectorType}: {collector.Lifecycle}, {collector.Health}"
                : $"{collector.CollectorType}: {collector.Lifecycle}, " +
                    $"{collector.Health}. {collector.HealthReason}")
            .ToArray();

        // A collector that stops supplying evidence while recording is spoken
        // once, because the recording continues and nothing else would tell a
        // screen-reader user that part of the evidence has ended.
        if (status.State == RecordingSessionState.Recording)
        {
            foreach (var collector in status.Collectors)
            {
                if (collector.HealthReason is { } reason &&
                    _announcedHealthReasons.Add(
                        $"{collector.CollectorType}\n{reason}"))
                {
                    _busy.AnnounceAlert(reason);
                }
            }

            // Spoken once per recording, for the same reason.
            if (status.Database?.Unavailable == true &&
                _announcedHealthReasons.Add("database\nunavailable"))
            {
                _busy.AnnounceAlert(
                    "The database is not accepting writes. Its events are being held " +
                    "until it does.");
            }
        }
    }

    private string DatabaseEvidenceText(RecordingDatabaseStatus? database)
    {
        if (database is null)
        {
            return _databaseStartError is null ? string.Empty : ". Database: not running";
        }

        var text = $". Database: {database.Written:N0} written";
        if (database.Rejected > 0)
        {
            text += $", {database.Rejected:N0} rejected";
        }

        if (database.Dropped > 0)
        {
            text += $", {database.Dropped:N0} dropped";
        }

        if (database.Unwritten > 0)
        {
            text += $", {database.Unwritten:N0} not written";
        }

        return database.Unavailable ? text + ", not accepting writes" : text;
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
        out string? browserStartUrl,
        out int fullWalkInterval)
    {
        chromiumPath = null;
        browserStartUrl = null;
        fullWalkInterval = 0;
        if (BrowserEvidenceCheckBox.IsChecked != true)
        {
            return true;
        }

        if (FullWalkCheckBox.IsChecked == true)
        {
            if (!int.TryParse(
                    FullWalkIntervalTextBox.Text.Trim(),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.CurrentCulture,
                    out fullWalkInterval) ||
                fullWalkInterval < 1)
            {
                fullWalkInterval = 0;
                ShowBrowserValidationError(
                    "Enter a whole number from 1 for how often each page is walked in full.",
                    FullWalkIntervalTextBox);
                return false;
            }
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

        SaveLayout();
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
        await CloseRecreationAsync();
        if (_database is not null)
        {
            var busy = _busy.Begin("Stopping the database.");
            try
            {
                await _database.DisposeAsync();
            }
            catch (Exception)
            {
                // The server is stopped with pg_ctl's fast mode. If that
                // fails, the next start attaches to the running server or
                // recovers the cluster, so closing is not held up.
            }
            finally
            {
                busy.Dispose();
                _database = null;
            }
        }

        _allowClose = true;
        Application.Current.Shutdown();
    }

    // Slice 3a: opens the fixed test page. A recreation already open is
    // closed first, so at most one is open.
    private async void OpenFixedRecreationButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFixedRecreationButton.IsEnabled = false;
        var busy = _busy.Begin("Opening the recreation.");
        try
        {
            await CloseRecreationAsync();
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Windows A11y Recorder",
                "recreations",
                Guid.NewGuid().ToString("N"));
            _recreation = await RecreationSession.OpenAsync(
                ChromiumPathTextBox.Text.Trim(),
                directory,
                FixedRecreation.Create(),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The recreation could not be opened. {exception.Message}",
                "Recreation",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            busy.Dispose();
            OpenFixedRecreationButton.IsEnabled = true;
        }
    }

    // Slice 3b: recreates a recorded page as it was at the frame shown. The
    // auditor chooses the page from the top-level documents at the frame. A
    // recreation already open is closed first, so at most one is open.
    private async void InspectPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_playbackArchive is null || _recordedDocuments is not { } documents || _displayedFrameIndex < 0)
        {
            MessageBox.Show(
                this,
                "No frame is shown, or this recording was not read from its recording file.",
                "Inspect page at this frame",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        var frame = _playbackArchive.Frames[_displayedFrameIndex].MonotonicNanoseconds;
        var chromium = ChromiumPathTextBox.Text.Trim();
        InspectPageButton.IsEnabled = false;
        PausePlayback();
        try
        {
            IReadOnlyList<RecordedDocumentChoice> choices;
            using (_busy.Begin("Finding the pages at this frame."))
            {
                choices = await Task.Run(() => documents.At(frame));
            }
            if (choices.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No top-level page was recorded at or before this frame.",
                    "Inspect page at this frame",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            var dialog = new RecordedDocumentDialog(this, FormatTime(frame), choices, FormatTime);
            if (dialog.ShowDialog() != true || dialog.Chosen is not { } chosen)
            {
                return;
            }
            using (_busy.Begin("Opening the recreation."))
            {
                var timings = new List<RecreationTiming>();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                await CloseRecreationAsync();
                timings.Add(new RecreationTiming("Closing the previous recreation", Math.Round(clock.Elapsed.TotalMilliseconds, 1)));
                var content = await Task.Run(() =>
                {
                    clock.Restart();
                    var found = documents.Document(chosen.Key, frame)
                        ?? throw new InvalidDataException("The page's state at this frame could not be read from the recording.");
                    timings.Add(new RecreationTiming("Reading the page's state at the frame from the recording", Math.Round(clock.Elapsed.TotalMilliseconds, 1)));
                    clock.Restart();
                    if (found.State!.Dom is null)
                    {
                        throw new InvalidDataException("No DOM walk of this page was recorded at or before this frame, so it cannot be recreated here. A page can be drawn before its first DOM walk; a later frame may have one.");
                    }
                    var basis = found.Basis is { Basis: "presented", PresentedTime: { } presented }
                        ? $"the state after the page's last rendering update drawn at or before the frame, drawn at {FormatTime(presented)}"
                        : "no rendering update of the page was drawn at or before the frame, so this is its state at the frame's composition time";
                    // Slice 4d sub-step 2: the page popups the page owned
                    // open at the frame, each at its own popup widget's
                    // last presented rendering update.
                    var popups = documents.Popups(chosen.Key, frame)
                        .Select(item => new RecordedPopup(
                            item.Popup,
                            item.State?.State,
                            item.State?.Basis is { Basis: "presented", PresentedTime: { } popupPresented }
                                ? $"its state is the one after its last rendering update drawn at or before the frame, drawn at {FormatTime(popupPresented)}"
                                : "no rendering update of it was drawn at or before the frame, so its state is the one at the frame's composition time"))
                        .ToArray();
                    timings.Add(new RecreationTiming("Reading the page's popups at the frame from the recording", Math.Round(clock.Elapsed.TotalMilliseconds, 1)));
                    clock.Restart();
                    // Slice 4b sub-step 2a: the frame each animated image
                    // is held at, chosen at the frame's composition.
                    var resources = documents.Resources(chosen.Key, found.Basis.CutTime, compositionNanoseconds: documents.CompositionTime(frame));
                    var resourcesMilliseconds = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
                    var framesMilliseconds = resources.ImageFramesMilliseconds ?? 0;
                    timings.Add(new RecreationTiming("Reading the page's fonts and images from the recording", Math.Round(resourcesMilliseconds - framesMilliseconds, 1)));
                    if (resources.ImageFramesMilliseconds is { } imageFrames)
                    {
                        timings.Add(new RecreationTiming("Choosing the frame of each animated image and the compositor values from the recording's compositor records", imageFrames));
                    }
                    clock.Restart();
                    RecordedFrame[] frames = [];
                    try
                    {
                        // Slice 5b: the page's frames, each with the document
                        // it showed at the frame, its state, and its fonts
                        // and images.
                        frames = RecordedFrames.Read(documents, documents.Frames(chosen.Key, found.State, frame), frame, FormatTime);
                        timings.Add(new RecreationTiming("Reading the page's frames, their states, and their fonts and images from the recording", Math.Round(clock.Elapsed.TotalMilliseconds, 1)));
                        clock.Restart();
                        var written = RecordedPage.Content(found.State, chosen.Url, frame, found.Basis.CutTime, basis, resources, popups, frames: frames);
                        timings.Add(new RecreationTiming("Writing the page", Math.Round(clock.Elapsed.TotalMilliseconds, 1)));
                        return written;
                    }
                    catch
                    {
                        resources.Dispose();
                        RecordedFrame.DisposeAll(frames);
                        throw;
                    }
                });
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Windows A11y Recorder",
                    "recreations",
                    Guid.NewGuid().ToString("N"));
                _recreation = await RecreationSession.OpenAsync(chromium, directory, content, CancellationToken.None, earlierTimings: timings);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The page could not be recreated. {exception.Message}",
                "Inspect page at this frame",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            InspectPageButton.IsEnabled = _playbackArchive is not null && _recordedDocuments is not null;
        }
    }

    private async Task CloseRecreationAsync()
    {
        if (_recreation is not null)
        {
            var recreation = _recreation;
            _recreation = null;
            await recreation.DisposeAsync();
        }
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
