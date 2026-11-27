using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.App;

public partial class AndroidLiveTreeWindow : Window
{
    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly AndroidLiveTree _feed;
    private readonly DispatcherTimer _timer;
    private AndroidLiveTreeSnapshot? _shown;
    private AndroidLiveWindowSnapshot? _selectedWindow;
    private IReadOnlyList<WindowChoice> _choices = [];
    private bool _selectedFromLatest;
    private bool _paused;
    private bool _updating;

    public AndroidLiveTreeWindow(AndroidLiveTree feed)
    {
        _feed = feed;
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
            if (WindowPicker.IsEnabled)
            {
                WindowPicker.Focus();
            }
            else
            {
                CloseButton.Focus();
            }
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        if (WindowState == WindowState.Minimized)
        {
            return;
        }

        var snapshot = _feed.Current;
        if (ReferenceEquals(snapshot, _shown) ||
            (_paused && snapshot.State is not (AndroidLiveTreeState.Unavailable or AndroidLiveTreeState.Stopped)))
        {
            return;
        }

        var announce = _shown?.State != snapshot.State;
        _shown = snapshot;
        PauseButton.IsEnabled = snapshot.Index is not null;
        if (!PauseButton.IsEnabled)
        {
            _paused = false;
            PauseButton.Content = "_Pause updates";
        }

        var choice = (WindowPicker.SelectedItem as WindowChoice)?.Key;
        var choices = snapshot.Windows.Select(window => new WindowChoice(KeyOf(window.Window), WindowCaption(window.Window))).ToArray();
        if (!_choices.SequenceEqual(choices))
        {
            _updating = true;
            try
            {
                _choices = choices;
                WindowPicker.ItemsSource = choices;
                WindowPicker.SelectedItem = choices.FirstOrDefault(entry => entry.Key == choice) ??
                    choices.FirstOrDefault(entry => snapshot.Windows.Any(window => window.Window.Active && KeyOf(window.Window) == entry.Key)) ??
                    choices.FirstOrDefault();
            }
            finally
            {
                _updating = false;
            }
        }
        WindowPicker.IsEnabled = choices.Length > 0;
        ShowSelectedWindow();
        SetStatus(announce);
    }

    private void SetStatus(bool announce)
    {
        if (_shown is not { } snapshot)
        {
            return;
        }

        var position = snapshot.Index is { } index
            ? $"Snapshot {index:N0}, received {Seconds(snapshot.ReceivedAtNanoseconds)} into the recording. {snapshot.Windows.Count:N0} phone windows."
            : "";
        var status = (_paused ? "Updates paused. " : "") + (snapshot.State switch
        {
            AndroidLiveTreeState.Observed => position,
            AndroidLiveTreeState.Unreadable when snapshot.Index is not null => "A tree update could not be read. Showing the last observed windows. " + position,
            AndroidLiveTreeState.Unreadable => "A tree update could not be read. Waiting for a readable snapshot.",
            AndroidLiveTreeState.Unavailable => "The live accessibility tree is unavailable. " + snapshot.Problem,
            AndroidLiveTreeState.Stopped => "Android recording has stopped.",
            _ => "Waiting for the first accessibility snapshot from the phone."
        });
        if (snapshot.State is AndroidLiveTreeState.Observed)
        {
            if (snapshot.EarlierUpdateMissing)
            {
                status += " An earlier update was missing; some windows may not have nodes yet.";
            }
            if (!string.IsNullOrWhiteSpace(snapshot.Problem))
            {
                status += " Android returned only part of this snapshot: " + Short(snapshot.Problem);
            }
        }

        TreeStatus.Text = status;
        if (announce)
        {
            var peer = UIElementAutomationPeer.FromElement(TreeStatus) ?? UIElementAutomationPeer.CreatePeerForElement(TreeStatus);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void ShowSelectedWindow()
    {
        var key = (WindowPicker.SelectedItem as WindowChoice)?.Key;
        var window = _shown?.Windows.FirstOrDefault(entry => KeyOf(entry.Window) == key);
        var fromLatest = window?.Items is { } observed && observed.SnapshotIndex == _shown?.Index;
        if (window == _selectedWindow)
        {
            if (fromLatest != _selectedFromLatest || NodeDetails.Text.Length == 0)
            {
                _selectedFromLatest = fromLatest;
                ShowDetails();
            }
            return;
        }

        var replaceRows = KeyOf(window?.Window) != KeyOf(_selectedWindow?.Window) || window?.Items != _selectedWindow?.Items;
        _selectedWindow = window;
        _selectedFromLatest = fromLatest;
        if (replaceRows)
        {
            // Indices belong to one snapshot. Do not silently select a new
            // node by reusing an index from a previous version of this window.
            NodeTree.ItemsSource = window?.Items is { } items ? RowsOf(items.Nodes) : Array.Empty<TreeRow>();
        }
        ShowDetails();
    }

    private void ShowDetails()
    {
        string text;
        if (_selectedWindow is not { } window)
        {
            text = "No phone window is available to inspect.";
        }
        else if (window.Items is not { } items)
        {
            text = WindowCaption(window.Window) + "\n\nNo nodes have been observed for this window in the available snapshots.";
        }
        else
        {
            var source = $"Window nodes: snapshot {items.SnapshotIndex:N0}, received {Seconds(items.ReceivedAtNanoseconds)} into the recording. {items.Nodes.Count:N0} nodes.";
            if (!_selectedFromLatest)
            {
                source += " This window’s nodes were not retaken in the latest snapshot.";
            }
            if (items.Truncated || items.MissingChildren > 0)
            {
                source += " This window’s nodes are incomplete.";
                if (items.Truncated) { source += " The capture reached its item limit."; }
                if (items.MissingChildren > 0) { source += $" {items.MissingChildren:N0} children could not be read."; }
            }

            if (NodeTree.SelectedItem is TreeRow row)
            {
                var node = row.Node.Password ? row.Node with { Text = null } : row.Node;
                text = row.Caption + "\n\n" + source +
                    "\n\n" + DescribeNode(node) +
                    "\n\nReported node fields (JSON)\n" + JsonSerializer.Serialize(node, DetailsJson);
            }
            else
            {
                text = WindowCaption(window.Window) + "\n\n" + source +
                    "\n\nSelect a node to inspect its reported properties." +
                    "\n\nReported window fields (JSON)\n" + JsonSerializer.Serialize(window.Window, DetailsJson);
            }
        }

        if (NodeDetails.Text != text)
        {
            NodeDetails.Text = text;
        }
    }

    private static string DescribeNode(AndroidTreeNode node)
    {
        var details = new StringBuilder();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                details.AppendLine($"{name}: {value}");
            }
        }

        Add("Class", node.ClassName);
        Add("Package", node.PackageName);
        Add("View ID", node.ViewId);
        Add("Text", node.Password ? "(password text is not shown)" : node.Text);
        Add("Content description", node.ContentDescription);
        Add("Hint", node.Hint);
        Add("State description", node.StateDescription);
        var bounds = node.Bounds;
        Add("Screen bounds", $"{bounds.Left}, {bounds.Top} to {bounds.Right}, {bounds.Bottom} (pixels)");
        var state = new List<string> { node.VisibleToUser ? "visible to user" : "not visible to user", node.Enabled ? "enabled" : "disabled" };
        if (node.AccessibilityFocused) { state.Add("accessibility focus"); }
        if (node.Focused) { state.Add("input focus"); }
        if (node.Heading) { state.Add("heading"); }
        if (node.Checkable) { state.Add(node.Checked); }
        if (node.Selected) { state.Add("selected"); }
        if (node.Clickable) { state.Add("clickable"); }
        if (node.Editable) { state.Add("editable"); }
        Add("Reported state", string.Join(", ", state));
        if (node.Actions.Count > 0)
        {
            Add("Actions", string.Join(", ", node.Actions.Select(action =>
                string.IsNullOrWhiteSpace(action.Label) ? $"0x{action.Id:X}" : $"{action.Label} (0x{action.Id:X})")));
        }
        if (node.LabeledBy.Count > 0)
        {
            Add("Labelled by node", string.Join(", ", node.LabeledBy));
        }
        return details.ToString().TrimEnd();
    }

    private static IReadOnlyList<TreeRow> RowsOf(IReadOnlyList<AndroidTreeNode> nodes)
    {
        var roots = new List<TreeRow>();
        var seen = new Dictionary<int, TreeRow>(nodes.Count);
        foreach (var node in nodes)
        {
            var row = new TreeRow(node);
            if (node.Parent is { } parent && seen.TryGetValue(parent, out var ancestor))
            {
                ancestor.Children.Add(row);
            }
            else
            {
                row.Expanded = true;
                roots.Add(row);
            }
            seen.TryAdd(node.Index, row);
        }
        return roots;
    }

    private static (int Display, int Id)? KeyOf(AndroidAccessibilityWindow? window) =>
        window is null ? null : (window.DisplayId, window.Id);

    private static string WindowCaption(AndroidAccessibilityWindow window) =>
        $"{Short(window.Title) ?? window.TypeName} (window {window.Id}, display {window.DisplayId}{(window.Active ? ", active" : "")})";

    private static string Seconds(long nanoseconds) =>
        (nanoseconds / 1_000_000_000.0).ToString("0.000", CultureInfo.CurrentCulture) + " s";

    private static string? Short(string? text)
    {
        var value = text?.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value.Length <= 160
            ? value
            : value[..(char.IsHighSurrogate(value[156]) ? 156 : 157)] + "...";
    }

    private void WindowPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating)
        {
            ShowSelectedWindow();
        }
    }

    private void NodeTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => ShowDetails();

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "_Resume updates" : "_Pause updates";
        if (!_paused)
        {
            Refresh();
        }
        SetStatus(announce: true);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !WindowPicker.IsDropDownOpen)
        {
            e.Handled = true;
            Close();
        }
    }

    private sealed record WindowChoice((int Display, int Id)? Key, string Caption);

    private sealed class TreeRow(AndroidTreeNode node)
    {
        public AndroidTreeNode Node { get; } = node;
        public string Caption { get; } =
            (node.Password ? "Password field" :
                Short(node.ContentDescription) ?? Short(node.Text) ?? Short(node.Hint) ??
                node.ClassName?.Split('.').Last() ?? "Unnamed node") +
            $" (node {node.Index}{(node.Heading ? ", heading" : "")}{(node.AccessibilityFocused ? ", accessibility focus" : "")})";
        public List<TreeRow> Children { get; } = [];
        public bool Expanded { get; set; }
    }
}
