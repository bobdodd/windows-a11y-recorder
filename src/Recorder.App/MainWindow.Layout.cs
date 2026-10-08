using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Recorder.Coordinator;
using Recorder.Session;

namespace Recorder.App;

// The player layout: the settings side panel and the details region can be
// hidden, and the frame-only view shows only the video, the playback
// controls, and the timeline. See docs/architecture/player-layout.md.
public partial class MainWindow
{
    // The details' share of the region under the video when no height has
    // been saved, close to the fixed heights the layout had before.
    private const double DefaultDetailsHeight = 250;
    private const double MinimumDetailsHeight = 120;
    private const double MinimumVideoHeight = 160;

    private PlayerLayout _layout = PlayerLayout.Default;
    private bool _frameOnly;
    private IInputElement? _focusBeforeFrameOnly;
    private GridLength _sidePanelWidth = new(340);

    private bool IsRecording =>
        _coordinator?.State == RecordingSessionState.Recording;

    // The settings are needed to start a recording and while it runs, so
    // they cannot be hidden then.
    private bool SettingsRequired => _playbackArchive is null || IsRecording;

    private bool SettingsShown => !_frameOnly && (_layout.SettingsOpen || SettingsRequired);

    private bool DetailsShown => !_frameOnly && _layout.DetailsOpen;

    private void LoadLayout()
    {
        _layout = PlayerLayout.Load(PlayerLayout.DefaultPath);
        ApplyLayout();
        PlayerGrid.SizeChanged += PlayerGrid_SizeChanged;
    }

    private void SaveLayout()
    {
        if (DetailsShown && LowerRegionRow.ActualHeight > 0)
        {
            _layout = _layout with { LowerRegionHeight = LowerRegionRow.ActualHeight };
        }

        _layout.TrySave(PlayerLayout.DefaultPath);
    }

    // Shows and hides the parts of the layout, and sets the toggles to
    // match. Called after any change of the layout, of whether a recording
    // is open, or of whether one is being recorded.
    private void ApplyLayout()
    {
        if (_frameOnly && (_playbackArchive is null || IsRecording))
        {
            _frameOnly = false;
        }

        var settingsShown = SettingsShown;
        if (settingsShown)
        {
            SidePanelColumn.MinWidth = 300;
            SidePanelColumn.MaxWidth = 440;
            SidePanelColumn.Width = _sidePanelWidth;
            SidePanelSplitterColumn.Width = new GridLength(5);
        }
        else
        {
            if (SidePanelColumn.ActualWidth > 0)
            {
                _sidePanelWidth = new GridLength(SidePanelColumn.ActualWidth);
            }

            SidePanelColumn.MinWidth = 0;
            SidePanelColumn.Width = new GridLength(0);
            SidePanelSplitterColumn.Width = new GridLength(0);
        }

        SidePanel.Visibility = settingsShown ? Visibility.Visible : Visibility.Collapsed;
        SidePanelSplitter.Visibility = SidePanel.Visibility;

        var detailsShown = DetailsShown;
        DetailsRegion.Visibility = detailsShown ? Visibility.Visible : Visibility.Collapsed;
        LowerSplitter.Visibility = DetailsRegion.Visibility;
        VolumePanel.Visibility = _frameOnly ? Visibility.Collapsed : Visibility.Visible;
        StatusPanel.Visibility = _frameOnly ? Visibility.Collapsed : Visibility.Visible;
        if (detailsShown)
        {
            UpdateLayout();
            SetLowerRegionHeight(_layout.LowerRegionHeight ?? FixedLowerHeight + DefaultDetailsHeight);
        }
        else
        {
            LowerRegionRow.MinHeight = 0;
            LowerRegionRow.Height = GridLength.Auto;
        }

        SettingsToggle.IsChecked = settingsShown;
        SettingsToggle.IsEnabled = !_frameOnly && !SettingsRequired;
        DetailsToggle.IsChecked = detailsShown;
        DetailsToggle.IsEnabled = !_frameOnly;
        FrameOnlyToggle.IsChecked = _frameOnly;
        FrameOnlyToggle.IsEnabled = _playbackArchive is not null && !IsRecording;
    }

    // The height under the video of everything but the details.
    private double FixedLowerHeight =>
        TransportPanel.ActualHeight +
        TransportPanel.Margin.Top + TransportPanel.Margin.Bottom +
        TimelinePanel.ActualHeight +
        StatusPanel.ActualHeight + StatusPanel.Margin.Top +
        DetailsRegion.Margin.Top;

    // Sets the height of the region under the video, keeping the details
    // and the video at least their minimum heights where the window allows.
    private void SetLowerRegionHeight(double requested)
    {
        var minimum = FixedLowerHeight + MinimumDetailsHeight;
        var available = PlayerGrid.ActualHeight - MinimumVideoHeight -
            LowerSplitter.ActualHeight - LowerSplitter.Margin.Top;
        var height = Math.Max(minimum, Math.Min(requested, Math.Max(minimum, available)));
        LowerRegionRow.MinHeight = minimum;
        LowerRegionRow.Height = new GridLength(height);
    }

    private void PlayerGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DetailsShown && LowerRegionRow.Height.IsAbsolute)
        {
            SetLowerRegionHeight(LowerRegionRow.Height.Value);
        }
    }

    private void SetSettingsOpen(bool open)
    {
        if (!_frameOnly && SettingsRequired && !open)
        {
            ApplyLayout();
            _busy.AnnounceLayout(
                "Settings stay shown while no recording is open and while recording.");
            return;
        }

        if (_frameOnly || SettingsRequired || open == _layout.SettingsOpen)
        {
            ApplyLayout();
            return;
        }

        var focusInside = SidePanel.IsKeyboardFocusWithin;
        _layout = _layout with { SettingsOpen = open };
        ApplyLayout();
        if (focusInside && !open)
        {
            SettingsToggle.Focus();
        }

        SaveLayout();
        _busy.AnnounceLayout(open ? "Settings shown." : "Settings hidden.");
    }

    private void SetDetailsOpen(bool open)
    {
        if (_frameOnly || open == _layout.DetailsOpen)
        {
            ApplyLayout();
            return;
        }

        var focusInside = DetailsRegion.IsKeyboardFocusWithin;
        if (!open)
        {
            SaveLayout();
        }

        _layout = _layout with { DetailsOpen = open };
        ApplyLayout();
        if (focusInside && !open)
        {
            DetailsToggle.Focus();
        }

        SaveLayout();
        _busy.AnnounceLayout(open ? "Details shown." : "Details hidden.");
    }

    private void SetFrameOnly(bool frameOnly)
    {
        if (frameOnly == _frameOnly ||
            (frameOnly && (_playbackArchive is null || IsRecording)))
        {
            ApplyLayout();
            return;
        }

        if (frameOnly)
        {
            SaveLayout();
            _focusBeforeFrameOnly = Keyboard.FocusedElement;
            _frameOnly = true;
            ApplyLayout();
            if (_focusBeforeFrameOnly is not UIElement { IsVisible: true })
            {
                if (PlayPauseButton.IsEnabled)
                {
                    PlayPauseButton.Focus();
                }
                else
                {
                    FrameOnlyToggle.Focus();
                }
            }

            _busy.AnnounceLayout("Frame only view. Press F11 or Escape to return.");
            return;
        }

        _frameOnly = false;
        ApplyLayout();
        if (_focusBeforeFrameOnly is UIElement { IsVisible: true, Focusable: true, IsEnabled: true } previous)
        {
            previous.Focus();
        }

        _focusBeforeFrameOnly = null;
        _busy.AnnounceLayout("Frame only view closed.");
    }

    private void SettingsToggle_Click(object sender, RoutedEventArgs e) =>
        SetSettingsOpen(((ToggleButton)sender).IsChecked == true);

    private void DetailsToggle_Click(object sender, RoutedEventArgs e) =>
        SetDetailsOpen(((ToggleButton)sender).IsChecked == true);

    private void FrameOnlyToggle_Click(object sender, RoutedEventArgs e) =>
        SetFrameOnly(((ToggleButton)sender).IsChecked == true);

    private void LowerSplitter_DragCompleted(object sender, DragCompletedEventArgs e) =>
        SaveLayout();

    // The layout's keys apply wherever focus is, including in text boxes,
    // since none of them is a text editing key. Returns whether the key was
    // one of them.
    private bool HandleLayoutKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.F11 && modifiers == ModifierKeys.None)
        {
            if (_frameOnly || FrameOnlyToggle.IsEnabled)
            {
                SetFrameOnly(!_frameOnly);
            }

            return true;
        }

        if (key == Key.Escape && modifiers == ModifierKeys.None && _frameOnly)
        {
            SetFrameOnly(false);
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (key == Key.D)
            {
                SetDetailsOpen(!DetailsShown);
                return true;
            }

            if (key == Key.S)
            {
                SetSettingsOpen(!SettingsShown);
                return true;
            }
        }

        return false;
    }
}
