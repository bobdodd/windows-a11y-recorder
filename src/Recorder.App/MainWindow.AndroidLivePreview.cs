using System.Windows;
using Recorder.Collectors.Android;
using Recorder.Coordinator;

namespace Recorder.App;

public partial class MainWindow
{
    private AndroidLivePreview? _androidLivePreview;
    private AndroidLiveTree? _androidLiveTree;
    private AndroidTalkBackExploration? _androidExploration;
    private AndroidLivePreviewWindow? _androidLivePreviewWindow;
    private Task _androidExplorationCompletion = Task.CompletedTask;

    private void EnableAndroidLivePreview()
    {
        var available = _androidLivePreview is not null;
        AndroidLivePreviewMenuItem.IsEnabled = available;
        AndroidLivePreviewButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task CloseAndroidLivePreviewAsync()
    {
        _androidLivePreview = null;
        _androidLiveTree = null;
        _androidExploration = null;
        AndroidLivePreviewMenuItem.IsEnabled = false;
        AndroidLivePreviewButton.Visibility = Visibility.Collapsed;
        _androidLivePreviewWindow?.StopExploration();
        _androidLivePreviewWindow?.Close();
        await _androidExplorationCompletion;
    }

    private void AndroidLivePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_transitioning || _coordinator?.State != RecordingSessionState.Recording ||
            _androidLivePreview is not { } preview || _androidLiveTree is not { } tree ||
            _androidExploration is not { } exploration)
        {
            return;
        }

        if (_androidLivePreviewWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }
            open.Activate();
            return;
        }

        var coordinator = _coordinator;
        var window = new AndroidLivePreviewWindow(preview, tree, exploration, coordinator.AddMarker, task =>
        {
            // If the preview was just reopened, keep waiting for any previous
            // scan's pending gesture/end marker as well.
            _androidExplorationCompletion = _androidExplorationCompletion.IsCompleted
                ? task : Task.WhenAll(_androidExplorationCompletion, task);
        }) { Owner = this };
        _androidLivePreviewWindow = window;
        window.Closed += (_, _) =>
        {
            _androidLivePreviewWindow = null;
            if (_androidLivePreview is not null && !_transitioning && IsVisible)
            {
                Activate();
                if (AndroidLivePreviewButton.IsVisible)
                {
                    AndroidLivePreviewButton.Focus();
                }
                else
                {
                    SettingsToggle.Focus();
                }
            }
        };
        window.Show();
    }
}
