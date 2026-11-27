using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Recorder.Analysis;
using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.App;

public partial class AndroidInteractionWindow : Window
{
    private sealed record Target(int Number, string Name, AndroidNodeBounds Box);

    private readonly AndroidInteractionExploration _exploration;
    private readonly AndroidTalkBackExploration _talkBack;
    private readonly Func<string, bool> _recordMarker;
    private readonly Action<Task> _track;
    private readonly CancellationTokenSource _closed = new();
    private readonly List<string> _observations = [];
    private AndroidInteractionSnapshot? _snapshot;
    private bool _busy;
    private bool _spent;

    public AndroidInteractionWindow(AndroidInteractionExploration exploration, AndroidTalkBackExploration talkBack,
        Func<string, bool> recordMarker, Action<Task> track)
    {
        _exploration = exploration;
        _talkBack = talkBack;
        _recordMarker = recordMarker;
        _track = track;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            Refresh.Focus();
            await ScanAsync();
        };
        Closed += (_, _) => Stop();
    }

    internal void Stop() => _closed.Cancel();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private async Task ScanAsync()
    {
        if (_busy)
        {
            return;
        }
        SetBusy(true);
        _spent = false;
        _snapshot = _exploration.Capture();
        Targets.ItemsSource = null;
        Picture.Source = null;
        Pin.Children.Clear();
        Report.Clear();
        try
        {
            if (_snapshot is not { } snapshot)
            {
                SetStatus("No active app tree is available. Open the app and scan again.");
                return;
            }
            SetStatus("Scanning the Android tree and live picture without sending input.");
            var scan = await Task.Run(() => Read(snapshot, _talkBack.MatchedStops(snapshot)), _closed.Token);
            if (_closed.IsCancellationRequested)
            {
                return;
            }
            Report.Text = scan.Report + (_observations.Count == 0 ? "" :
                $"{Environment.NewLine}Test taps from this exploration window{Environment.NewLine}" + string.Join("", _observations));
            var counts = $"{scan.Declared} Android-declared candidates; {scan.Visual} visual candidates. No touches sent.";
            if (snapshot.Frame is { } frame && scan.Targets.Count > 0)
            {
                if (!TryPresent(frame))
                {
                    SetStatus(counts + " Windows could not show the test picture, so test taps are unavailable.");
                    return;
                }
                Targets.ItemsSource = scan.Targets;
            }
            SetStatus(counts);
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        finally
        {
            if (!_closed.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private static (string Report, List<Target> Targets, int Declared, int Visual) Read(AndroidInteractionSnapshot snapshot,
        IReadOnlyDictionary<int, AndroidTalkBackStop> stops)
    {
        var declared = AndroidInteractionInventory.Declared(snapshot.App);
        IReadOnlyList<AndroidInteractionCandidate> text = [], shapes = [];
        var ocr = "OCR not attempted: the live picture could not be mapped to the Android display.";
        bool limited = false;
        if (snapshot.HasScreenCoordinates && snapshot.Frame is { } frame)
        {
            var reader = new WindowsPhoneTextReader();
            ocr = reader.Unavailable ?? reader.Engine;
            if (reader.Unavailable is null)
            {
                try
                {
                    var reading = reader.ReadRgb(frame.Width, frame.Height, frame.Rgb);
                    text = AndroidInteractionInventory.Visual(snapshot.App, declared, reading.Words.Select(word => new AndroidInteractionWord(
                        word.Text, new(word.Box.Left, word.Box.Top, word.Box.Right, word.Box.Bottom), word.Line)),
                        new(0, 0, frame.Width, frame.Height), snapshot.Occluders);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
                {
                    ocr = "Windows OCR did not return text evidence: " + ex.Message;
                }
            }
            shapes = AndroidInteractionShapes.Find(frame, snapshot.App, declared.Concat(text), snapshot.Occluders, out limited);
        }
        var report = new StringBuilder();
        report.AppendLine($"Current app viewport: {snapshot.PackageName}, Android window {snapshot.App.Window.Id}.");
        report.AppendLine($"Tree snapshot {snapshot.App.Items?.SnapshotIndex}; " +
            (snapshot.Frame is { } picture ? $"recorded picture {picture.Sequence}." : "no recorded picture.") +
            " Tree and picture are independent observations.");
        report.AppendLine(ocr.TrimEnd('.') + ".");
        if (snapshot.App.Items is { } items && (items.Truncated || items.MissingChildren > 0 ||
            snapshot.Tree.EarlierUpdateMissing || items.SnapshotIndex != snapshot.Tree.Index))
        {
            report.AppendLine($"Tree coverage is limited or older for this window. Truncated: {items.Truncated}; " +
                $"missing children: {items.MissingChildren}; earlier tree update missing: {snapshot.Tree.EarlierUpdateMissing}.");
        }
        if (limited)
        {
            report.AppendLine($"The visual shape pass reached its limit of {AndroidInteractionShapes.Limit} candidates.");
        }
        report.AppendLine("A declared action is not proof it works. Visual candidates may be noninteractive. " +
            "Only matching stops from a preceding TalkBack pass are shown; otherwise reachability is unknown. " +
            "TalkBack activation was not tested. Hidden, off-screen and gesture-only targets may not be found.");
        var targets = new List<Target>();
        var number = 0;
        var focus = snapshot.Tree.FocusFor(snapshot.App);
        var focused = snapshot.App.Items?.Nodes.Where(node =>
            (focus.ForNode(node) & AndroidLiveFocusKind.Accessibility) != 0).Select(node => node.Index).ToHashSet() ?? [];
        void Section(string title, IEnumerable<AndroidInteractionCandidate> entries)
        {
            report.AppendLine().AppendLine(title);
            var first = true;
            foreach (var item in entries)
            {
                first = false;
                number++;
                report.Append($"{number}. {item.Label}: {item.Evidence}");
                if (item.Role is { } role && role != "Inline link")
                {
                    report.Append($". Role/class: {role}");
                }
                if (item.Enabled == false)
                {
                    report.Append(". Android reports disabled");
                }
                if (item.NodeIndex is { } index)
                {
                    report.Append($". Tree item {index}");
                    if (item.Role != "Inline link" && focused.Contains(index))
                    {
                        report.Append(focus.FromEarlierSnapshot ? ". Android accessibility focus in earlier tree" :
                            ". Android accessibility focus in this tree");
                    }
                    if (item.Role != "Inline link" && stops.TryGetValue(index, out var stop))
                    {
                        report.Append($". Matched an observed TalkBack stop (event {stop.EventIndex}, tree {stop.SnapshotIndex})");
                    }
                }
                foreach (var (box, part) in item.Boxes.Select((box, part) => (box, part)))
                {
                    var (x, y) = AndroidInteractionExploration.Center(box);
                    report.Append($". Point ({x}, {y})");
                    if (snapshot.HasScreenCoordinates && snapshot.Frame is { } frame && item.Enabled != false &&
                        box.Left >= 0 && box.Top >= 0 && box.Right <= frame.Width && box.Bottom <= frame.Height &&
                        !snapshot.Occluders.Any(other => x >= other.Left && x < other.Right && y >= other.Top && y < other.Bottom))
                    {
                        var suffix = item.Boxes.Count > 1 ? $", part {part + 1}" : "";
                        var kind = item.NodeIndex is null ? "visual" : "declared";
                        targets.Add(new(number, $"{number}. {item.Label} ({kind}, {x}, {y}{suffix})", box));
                    }
                }
                report.AppendLine(". Not tested.");
            }
            if (first)
            {
                report.AppendLine("None found in this evidence.");
            }
        }
        Section("Actions or capabilities declared by Android", declared);
        Section("Visual candidates without a matching label or known target", text.Concat(shapes));
        return (report.ToString(), targets, declared.Count, text.Count + shapes.Count);
    }

    private async void Tap_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not { } snapshot || Targets.SelectedItem is not Target selected || _busy || _spent)
        {
            return;
        }
        if (_exploration.TapProblem(snapshot, selected.Box) is { } problem)
        {
            SetStatus(problem);
            return;
        }
        if (MessageBox.Show(this,
            $"Send one real tap to {selected.Name}?\n\nIt may activate a control or change the app. Only continue on a screen and target you are prepared to test.",
            "Confirm one phone tap", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes ||
            _closed.IsCancellationRequested)
        {
            return;
        }
        SetBusy(true);
        _spent = true;
        SetStatus("Testing the selected point. Leave the phone untouched.");
        var task = _exploration.TapAsync(snapshot, selected.Box, selected.Number, _recordMarker, _closed.Token);
        _track(task);
        var result = await task;
        if (!_closed.IsCancellationRequested)
        {
            var observation = $"{Environment.NewLine}{snapshot.PackageName}, picture {snapshot.Frame?.Sequence}, " +
                $"{selected.Name}{Environment.NewLine}{result.Evidence}{Environment.NewLine}";
            if (_observations.Count == 0)
            {
                Report.AppendText($"{Environment.NewLine}Test taps from this exploration window{Environment.NewLine}");
            }
            _observations.Add(observation);
            Report.AppendText(observation);
            SetStatus(result.Evidence + " Scan again to test another point.");
            SetBusy(false);
        }
    }

    private bool TryPresent(AndroidLivePreviewFrame frame)
    {
        if (!MemoryMarshal.TryGetArray(frame.Rgb, out var rgb) || rgb.Array is null)
        {
            return false;
        }
        try
        {
            var bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Rgb24, null);
            bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), rgb.Array, frame.Width * 3, rgb.Offset);
            PictureLayout.Width = frame.Width;
            PictureLayout.Height = frame.Height;
            Picture.Source = bitmap;
            return true;
        }
        catch (Exception exception) when (exception is COMException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private void Targets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Pin.Children.Clear();
        if (Targets.SelectedItem is Target target && _snapshot?.Frame is { } frame)
        {
            var (x, y) = AndroidInteractionExploration.Center(target.Box);
            var size = Math.Max(26, frame.Width * .075);
            foreach (var (brush, width) in new[] { (Brushes.Black, size * .17), (Brushes.White, size * .07) })
            {
                var ring = new Ellipse { Width = size, Height = size, Stroke = brush, StrokeThickness = width };
                Canvas.SetLeft(ring, x - size / 2);
                Canvas.SetTop(ring, y - size / 2);
                Pin.Children.Add(ring);
            }
        }
        Tap.IsEnabled = !_busy && !_spent && Targets.SelectedItem is Target;
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        Refresh.IsEnabled = !value;
        Targets.IsEnabled = !value;
        Tap.IsEnabled = !value && !_spent && Targets.SelectedItem is Target;
    }

    private void SetStatus(string value)
    {
        ScanStatus.Text = value;
        (UIElementAutomationPeer.FromElement(ScanStatus) ?? UIElementAutomationPeer.CreatePeerForElement(ScanStatus))
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !Targets.IsDropDownOpen)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
