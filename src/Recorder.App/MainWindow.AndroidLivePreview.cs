using System.Windows;
using Recorder.Collectors.Android;
using Recorder.Coordinator;

namespace Recorder.App;

public partial class MainWindow
{
    private AndroidLivePreview? _androidLivePreview;
    private AndroidLiveTree? _androidLiveTree;
    private AndroidInteractionExploration? _androidInteractions;
    private Guid? _androidInvestigationProject;
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
        _androidInteractions = null;
        _androidInvestigationProject = null;
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
            _androidInteractions is not { } interactions)
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
        var window = new AndroidLivePreviewWindow(preview, tree, interactions, InvestigationVisionProfileAsync,
            coordinator.AddMarker, task =>
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

    private Task<Recorder.Analysis.ModelProfile?> InvestigationVisionProfileAsync(CancellationToken cancellation)
    {
        var settings = _analysisSettings;
        var project = _androidInvestigationProject;
        var catalog = AnalysisCatalogOrNull;
        return ReadProfileAsync();

        async Task<Recorder.Analysis.ModelProfile?> ReadProfileAsync()
        {
            var models = project is { } id && catalog is not null
                ? await catalog.ProjectModelsAsync(id, cancellation).ConfigureAwait(false) : null;
            return models?.VisionChecks is { } selection ? settings.Profile(selection) : null;
        }
    }
}
