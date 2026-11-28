using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using Recorder.Analysis;
using Recorder.Collectors.Android;

namespace Recorder.App;

public partial class AndroidInteractionWindow : Window
{
    private readonly CancellationTokenSource _closed = new();
    private bool _running = true;
    private bool _isClosed;

    public AndroidInteractionWindow(AndroidLivePreviewFrame? frame,
        AndroidLiveTreeSnapshot tree, IPhoneDisplay? display,
        AndroidInteractionExploration? exploration, AndroidInteractionSnapshot? start,
        Func<CancellationToken, Task<(ModelProfile? Profile, LocalServerSettings? Server, string? Problem)>> vision,
        Func<string, bool> marker, Action<Task> track)
    {
        InitializeComponent();
        RunStatus.Text = "Preparing one screenshot for model discovery.";
        Closed += (_, _) =>
        {
            _isClosed = true;
            Stop();
        };
        ContentRendered += Start;

        async void Start(object? sender, EventArgs args)
        {
            ContentRendered -= Start;
            if (_isClosed)
            {
                return;
            }
            RunStatus.Focus();
            var progress = new Progress<string>(status =>
            {
                if (!_isClosed && _running)
                {
                    ShowStatus(status);
                }
            });
            var task = RunAsync(progress);
            track(task.ContinueWith(_ => { }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
            try
            {
                var result = await task;
                if (!_isClosed)
                {
                    Report.Document = AndroidInteractionReportDocument.Create(result);
                    ShowStatus(result.Status);
                }
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested)
            {
                if (!_isClosed)
                {
                    ShowStatus("Screen investigation was cancelled.");
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Android screen investigation stopped: {0}", ex.GetType().Name);
                if (!_isClosed)
                {
                    ShowStatus("The screen investigation report could not be completed.");
                }
            }
            finally
            {
                _running = false;
                if (!_isClosed)
                {
                    CloseReport.Content = "_Close report";
                    CloseReport.IsEnabled = true;
                }
            }
        }

        async Task<AndroidModelDiscoveryReport> RunAsync(IProgress<string> progress)
        {
            if (frame is null)
            {
                return new("Visual discovery not assessed. No recorded phone screenshot was available.");
            }
            var tag = Guid.NewGuid().ToString("N")[..8];
            if (!marker($"Recorder model discovery {tag}: started; frame {frame.Sequence}."))
            {
                return new("Visual discovery not assessed. The recording no longer accepts annotations.");
            }
            var token = _closed.Token;
            AndroidModelDiscoveryReport discovered;
            try
            {
                discovered = await AndroidInteractionScan.ReadAsync(frame, vision, progress, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (discovered.Proposals is { } modelProposals)
                {
                    progress.Report("Comparing the model's proposals with the starting Android accessibility tree.");
                    var matching = await Task.Run(() => AndroidProposalTreeMatching.Compare(frame, tree, display, modelProposals, token), token)
                        .ConfigureAwait(false);
                    discovered = discovered with { ScreenSize = (frame.Width, frame.Height), Tree = matching };
                }
            }
            finally
            {
                marker($"Recorder model discovery {tag}: ended; frame {frame.Sequence}.");
            }
            if (discovered.Proposals is not { Count: > 0 } proposals)
            {
                return discovered with { InputUnavailable = "No model proposals were available. No phone input was sent." };
            }
            if (exploration is null || start is null || discovered.Tree is not { } match)
            {
                return discovered with { InputUnavailable = "The starting app viewport was not available for input testing. No phone input was sent." };
            }
            var plan = AndroidProposalInputPlan.Build(start, proposals, match);
            if (plan.Candidates.Count == 0)
            {
                return discovered with { InputUnavailable = plan.Coverage + " No phone input was sent." };
            }
            var input = await exploration.InvestigateProposalsAsync(start, plan, marker, progress, token).ConfigureAwait(false);
            return discovered with { Status = input.Status, Input = input };
        }
    }

    internal void Stop() => _closed.Cancel();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            Stop();
            CloseReport.IsEnabled = false;
            ShowStatus("Stopping screen investigation.");
        }
        else
        {
            Close();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close_Click(sender, e);
        }
    }

    private void Report_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy)
        {
            return;
        }
        // Copy selected text and captions without adding screenshots to the clipboard.
        e.Handled = true;
        var text = Report.Selection.Text;
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Trace.TraceWarning("The clipboard was unavailable when copying the Android investigation report.");
            }
        }
    }

    private void ShowStatus(string text)
    {
        RunStatus.Text = text;
        var peer = UIElementAutomationPeer.FromElement(RunStatus) ?? UIElementAutomationPeer.CreatePeerForElement(RunStatus);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
