using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Recorder.Session;

namespace Recorder.App;

// The page's accessibility tree at the playhead (screen-reader-activity.md,
// "4c, the tree view"), agreed with the owner on 2026-10-12: read only,
// rebuilt from the checkpoints, with the reading position and the object
// under the pointer marked in words. It updates as the playhead moves, but
// never scrolls, expands, or collapses by itself: when first shown for a
// recording only the path to the reading position is expanded (the root,
// when it is unknown), and the two Show buttons expand the path to their
// object and scroll to it. Clicking an object selects its version's
// record, which moves the playhead to when it was recorded.
public partial class MainWindow
{
    private readonly ObservableCollection<TreeRowView> _treeRoots = [];
    private readonly Dictionary<string, TreeRowView> _treeRows = new(StringComparer.Ordinal);
    private AccessibilityTreeSnapshot? _treeSnapshot;
    private long _treeVersion = -1;
    private bool _treeBound;
    private bool _treeExpansionSet;
    private bool _allowTreeScroll;
    private string? _readingKey;
    private string? _pointerKey;

    private void BindAccessibilityTree()
    {
        if (!_treeBound)
        {
            AccessibilityTreeView.ItemsSource = _treeRoots;
            AccessibilityTreeView.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(TreeView_RequestBringIntoView));
            _treeBound = true;
        }

        _treeRoots.Clear();
        _treeRows.Clear();
        _treeSnapshot = null;
        _treeVersion = -1;
        _treeExpansionSet = false;
        _readingKey = null;
        _pointerKey = null;
        DisplayAccessibilityTreeAt(_playbackPositionNanoseconds);
    }

    private void DisplayAccessibilityTreeAt(long positionNanoseconds)
    {
        if (!_treeBound)
        {
            return;
        }

        var archive = _playbackArchive;
        var tree = archive?.AccessibilityTree;
        if (archive is null || tree is not { Recorded: true })
        {
            AccessibilityTreeStatusTextBlock.Text = archive is null
                ? "Open a recording with accessibility checkpoints to see the page's tree."
                : "This recording holds no accessibility checkpoints of a page.";
            ShowReadingPositionButton.IsEnabled = false;
            ShowUnderPointerButton.IsEnabled = false;
            return;
        }

        var snapshot = tree.At(positionNanoseconds);
        if (snapshot is null)
        {
            if (_treeRoots.Count > 0)
            {
                _treeRoots.Clear();
                _treeRows.Clear();
            }

            _treeSnapshot = null;
            _treeVersion = -1;
            AccessibilityTreeStatusTextBlock.Text = "No page's tree is recorded at the playhead.";
            ShowReadingPositionButton.IsEnabled = false;
            ShowUnderPointerButton.IsEnabled = false;
            return;
        }

        var reading = ReadingObjectAt(archive, positionNanoseconds) is { } readingObject ? tree.KeyOf(readingObject) : null;
        var pointer = archive.AccessibilityRequests.PointerObjectAt(positionNanoseconds) is { } pointerObject ? tree.KeyOf(pointerObject) : null;
        if (snapshot.Version != _treeVersion)
        {
            Mark(_readingKey, _pointerKey, false);
            _treeSnapshot = snapshot;
            _treeVersion = snapshot.Version;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Sync(_treeRoots, snapshot.Roots, seen);
            foreach (var gone in _treeRows.Keys.Where(key => !seen.Contains(key)).ToList())
            {
                _treeRows.Remove(gone);
            }

            _readingKey = null;
            _pointerKey = null;
        }

        if (reading != _readingKey || pointer != _pointerKey)
        {
            Mark(_readingKey, _pointerKey, false);
            _readingKey = reading;
            _pointerKey = pointer;
            Mark(reading, pointer, true);
        }

        // When first shown for a recording: only the path to the reading
        // position, or the root.
        if (!_treeExpansionSet)
        {
            _treeExpansionSet = true;
            if (reading is null || !ExpandPathTo(reading))
            {
                foreach (var root in _treeRoots)
                {
                    root.IsExpanded = true;
                }
            }
        }

        AccessibilityTreeStatusTextBlock.Text =
            $"{snapshot.ObjectCount:N0} objects. {AccessibilityTreeTimeline.Note}" +
            (reading is null ? " The reading position is not in the tree." : string.Empty) +
            (pointer is null ? " No object under the pointer is in the tree." : string.Empty);
        ShowReadingPositionButton.IsEnabled = reading is not null && _treeRows.ContainsKey(reading);
        ShowUnderPointerButton.IsEnabled = pointer is not null && _treeRows.ContainsKey(pointer);
    }

    private static TreeObject? ReadingObjectAt(SessionPlaybackArchive archive, long time)
    {
        var index = archive.ReadingPosition.IndexAt(time);
        return index >= 0 ? archive.ReadingPosition.Steps[index].Object : null;
    }

    private void Mark(string? reading, string? pointer, bool on)
    {
        if (reading is not null && _treeRows.TryGetValue(reading, out var readingRow))
        {
            readingRow.IsReading = on;
        }

        if (pointer is not null && _treeRows.TryGetValue(pointer, out var pointerRow))
        {
            pointerRow.IsPointer = on;
        }
    }

    // Brings the views in line with the rows in place, keeping each row's
    // view, and so its expansion and the scroll position, where its key is
    // unchanged.
    private void Sync(ObservableCollection<TreeRowView> views, IReadOnlyList<AccessibilityTreeRow> rows, HashSet<string> seen)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            seen.Add(row.Key);
            var at = -1;
            for (var search = index; search < views.Count; search++)
            {
                if (views[search].Key == row.Key)
                {
                    at = search;
                    break;
                }
            }

            TreeRowView view;
            if (at < 0)
            {
                view = _treeRows.TryGetValue(row.Key, out var known) && !views.Contains(known) ? known : new TreeRowView(row.Key);
                views.Insert(index, view);
            }
            else
            {
                view = views[at];
                if (at != index)
                {
                    views.Move(at, index);
                }
            }

            _treeRows[row.Key] = view;
            view.Update(row);
            Sync(view.Children, row.Children, seen);
        }

        while (views.Count > rows.Count)
        {
            views.RemoveAt(views.Count - 1);
        }
    }

    private bool ExpandPathTo(string key)
    {
        if (_treeSnapshot?.PathTo(key) is not { } path)
        {
            return false;
        }

        foreach (var step in path.Take(path.Count - 1))
        {
            if (_treeRows.TryGetValue(step, out var view))
            {
                view.IsExpanded = true;
            }
        }

        return true;
    }

    private void ShowReadingPositionButton_Click(object sender, RoutedEventArgs e) => ShowTreeRow(_readingKey);

    private void ShowUnderPointerButton_Click(object sender, RoutedEventArgs e) => ShowTreeRow(_pointerKey);

    // Expands the path to a row and scrolls to it.
    private void ShowTreeRow(string? key)
    {
        if (key is null || !ExpandPathTo(key) || _treeSnapshot?.PathTo(key) is not { } path)
        {
            return;
        }

        AccessibilityTreeView.UpdateLayout();
        ItemsControl parent = AccessibilityTreeView;
        TreeViewItem? item = null;
        foreach (var step in path)
        {
            if (!_treeRows.TryGetValue(step, out var view) ||
                parent.ItemContainerGenerator.ContainerFromItem(view) is not TreeViewItem container)
            {
                return;
            }

            container.ApplyTemplate();
            container.UpdateLayout();
            item = container;
            parent = container;
        }

        if (item is not null)
        {
            _allowTreeScroll = true;
            item.BringIntoView();
            _allowTreeScroll = false;
        }
    }

    // The tree does not scroll by itself, as when a clicked row is selected;
    // only the Show buttons scroll it.
    private void TreeView_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (!_allowTreeScroll)
        {
            e.Handled = true;
        }
    }

    private void TreeRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || sender is not TreeViewItem { DataContext: TreeRowView view } item ||
            IsWithinButton(e.OriginalSource as DependencyObject, item))
        {
            return;
        }

        e.Handled = true;
        GoToTreeRow(view);
    }

    private void TreeRow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TreeViewItem { DataContext: TreeRowView view, IsSelected: true })
        {
            e.Handled = true;
            GoToTreeRow(view);
        }
    }

    // Selects the record of the object's version, which moves the playhead
    // to when it was recorded.
    private void GoToTreeRow(TreeRowView view)
    {
        if (_playbackArchive is null || view.EventId is not { } eventId)
        {
            return;
        }

        if (_playbackArchive.TimelineEvents.TryGetValue(eventId, out var item))
        {
            TimelineControl.SelectTimelineEvent(item);
        }
        else if (view.RecordedAt is { } at)
        {
            PausePlayback();
            SeekTo(at, synchronizeAudio: false);
        }
    }
}

/// <summary>A row of the tree view.</summary>
public sealed class TreeRowView : INotifyPropertyChanged
{
    private string _label = string.Empty;
    private string _detail = string.Empty;
    private bool _isExpanded;
    private bool _isReading;
    private bool _isPointer;

    public TreeRowView(string key)
    {
        Key = key;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public ObservableCollection<TreeRowView> Children { get; } = [];

    public string? EventId { get; private set; }

    public long? RecordedAt { get; private set; }

    public string Label => _label;

    /// <summary>When the object's version was recorded; empty for a note.</summary>
    public string Detail => _detail;

    /// <summary>The words marking the reading position and the object under the pointer, so they do not rest on colour.</summary>
    public string Markers => string.Join(", ", new[]
    {
        _isReading ? "reading position" : null,
        _isPointer ? "under the pointer" : null
    }.OfType<string>());

    public string Spoken =>
        string.Join(", ", new[] { _label, _detail, Markers }.Where(text => text.Length > 0));

    public string ExpandName => (_isExpanded ? "Collapse " : "Expand ") + _label;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                Changed(nameof(IsExpanded));
                Changed(nameof(ExpandName));
            }
        }
    }

    public bool IsReading
    {
        get => _isReading;
        set
        {
            if (_isReading != value)
            {
                _isReading = value;
                Changed(nameof(IsReading));
                Changed(nameof(Markers));
                Changed(nameof(Spoken));
            }
        }
    }

    public bool IsPointer
    {
        get => _isPointer;
        set
        {
            if (_isPointer != value)
            {
                _isPointer = value;
                Changed(nameof(IsPointer));
                Changed(nameof(Markers));
                Changed(nameof(Spoken));
            }
        }
    }

    public void Update(AccessibilityTreeRow row)
    {
        var detail = row.Kind == AccessibilityTreeRowKind.Object && row.RecordedAt is { } at
            ? $"recorded at {WindowsPreferenceTimeline.ClockOf(at)}"
            : string.Empty;
        EventId = row.EventId;
        RecordedAt = row.RecordedAt;
        if (_label != row.Label || _detail != detail)
        {
            _label = row.Label;
            _detail = detail;
            Changed(nameof(Label));
            Changed(nameof(Detail));
            Changed(nameof(Spoken));
            Changed(nameof(ExpandName));
        }
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
