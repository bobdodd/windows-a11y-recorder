using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using Recorder.Collectors.Android;

namespace Recorder.App;

public partial class AndroidTalkBackExplorationWindow : Window
{
    private readonly AndroidTalkBackExploration _exploration;
    private readonly Func<string, bool> _recordMarker;
    private readonly Action<Task> _trackExploration;
    private CancellationTokenSource? _scan;
    private bool _closed;

    public AndroidTalkBackExplorationWindow(AndroidTalkBackExploration exploration,
        Func<string, bool> recordMarker, Action<Task> trackExploration)
    {
        _exploration = exploration;
        _recordMarker = recordMarker;
        _trackExploration = trackExploration;
        InitializeComponent();
        Loaded += (_, _) => DefaultGestures.Focus();
        Closed += (_, _) =>
        {
            _closed = true;
            StopExploration();
        };
    }

    internal void StopExploration() => _scan?.Cancel();

    private void DefaultGestures_Changed(object sender, RoutedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var canStart = DefaultGestures.IsChecked == true && _scan is null;
        StartButton.IsEnabled = VerifyButton.IsEnabled = canStart;
    }

    private void Start_Click(object sender, RoutedEventArgs e) => Start(singleSwipe: false);
    private void Verify_Click(object sender, RoutedEventArgs e) => Start(singleSwipe: true);

    private void Start(bool singleSwipe)
    {
        if (_scan is not null || DefaultGestures.IsChecked != true)
        {
            return;
        }
        var scan = new CancellationTokenSource();
        _scan = scan;
        UpdateButtons();
        DefaultGestures.IsEnabled = false;
        StopButton.IsEnabled = true;
        StopButton.Focus();
        Results.Text = "Waiting for matched accessibility focus evidence.";
        SetStatus("Exploring the current app screen. Leave the phone untouched.");
        _trackExploration(RunAsync(scan, singleSwipe));
    }

    private async Task RunAsync(CancellationTokenSource scan, bool singleSwipe)
    {
        try
        {
            var result = await _exploration.RunAsync(_recordMarker, singleSwipe, scan.Token);
            if (!_closed)
            {
                Results.Text = Format(result);
                SetStatus($"{result.Stops.Count} matched focus stops. {result.Reason}");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Trace.TraceWarning("TalkBack exploration stopped unexpectedly: {0}", exception.GetType().Name);
            if (!_closed)
            {
                Results.Text = "The exploration could not produce a result. The recording can continue.";
                SetStatus("TalkBack exploration stopped unexpectedly.");
            }
        }
        finally
        {
            _scan = null;
            scan.Dispose();
            if (!_closed)
            {
                var returnFocus = StopButton.IsKeyboardFocused;
                DefaultGestures.IsEnabled = true;
                UpdateButtons();
                if (returnFocus)
                {
                    (singleSwipe ? VerifyButton : StartButton).Focus();
                }
                StopButton.IsEnabled = false;
            }
        }
    }

    private static string Format(AndroidTalkBackExplorationResult result)
    {
        var text = new StringBuilder();
        text.AppendLine(result.Reason);
        text.AppendLine($"Swipes started (companion report): {result.GestureCount}. " +
            $"Both directions attempted: {(result.BothDirectionsTried ? "yes" : "no")}.");
        if (result.BaselineSnapshot is { } baseline)
        {
            text.AppendLine($"Starting tree snapshot: {baseline}. Starting accessibility focus: {result.StartingFocus}.");
        }
        text.AppendLine();
        text.AppendLine("Observed order");
        if (result.Stops.Count == 0)
        {
            text.AppendLine("No focus changes matched the scan.");
        }
        foreach (var stop in result.Stops)
        {
            text.AppendLine($"Swipe {stop.Gesture}, {stop.Direction}: {stop.Item} (event {stop.EventIndex}, snapshot {stop.SnapshotIndex})");
        }
        text.AppendLine();
        text.AppendLine("Candidates from the starting tree");
        if (result.Candidates.Count == 0)
        {
            text.AppendLine("No candidates were listed.");
        }
        foreach (var candidate in result.Candidates)
        {
            text.AppendLine($"{candidate.Observation}: {candidate.Item} (starting node {candidate.NodeIndex})");
        }
        if (result.UnlistedCandidates > 0)
        {
            text.AppendLine($"{result.UnlistedCandidates} additional candidates are not shown.");
        }
        text.AppendLine();
        text.Append("This is evidence of linear accessibility focus, not activation or speech. ");
        text.Append("Items absent from the starting tree could not be assessed; an item not reached in this pass is not necessarily inaccessible.");
        return text.ToString();
    }

    private void SetStatus(string text)
    {
        ScanStatus.Text = text;
        var peer = UIElementAutomationPeer.FromElement(ScanStatus) ?? UIElementAutomationPeer.CreatePeerForElement(ScanStatus);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopExploration();
        SetStatus("Stopping exploration. An in-flight swipe may finish; no more will be sent.");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_scan is not null)
            {
                Stop_Click(sender, e);
            }
            else
            {
                Close();
            }
        }
    }
}
