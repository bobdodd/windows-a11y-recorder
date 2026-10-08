using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using Recorder.Session;

namespace Recorder.App;

// The properties panel beside the video: the Magnifier readings of the
// frame shown and the Windows settings in effect at its time. Its rows are
// updated in place, so the row a keyboard or screen reader user is on keeps
// its place during playback; nothing is announced as they change. See
// docs/architecture/accessibility-preferences.md, "The properties panel".
public partial class MainWindow
{
    private readonly ObservableCollection<PropertyRowView> _propertyRows = [];
    private bool _propertiesBound;

    private void DisplayPropertiesAt(long positionNanoseconds)
    {
        if (!PropertiesShown)
        {
            return;
        }

        if (!_propertiesBound)
        {
            var view = new ListCollectionView(_propertyRows);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PropertyRowView.Group)));
            PropertiesListView.ItemsSource = view;
            _propertiesBound = true;
        }

        IReadOnlyList<PropertyRow> rows;
        if (_playbackArchive is null)
        {
            rows = [];
        }
        else
        {
            var index = _playbackArchive.Frames.Count == 0
                ? -1
                : FindFrameAtOrBefore(_playbackArchive.Frames, positionNanoseconds);
            var frame = index < 0 ? null : _playbackArchive.Frames[index];
            rows = _playbackArchive.WindowsPreferences.RowsAt(positionNanoseconds, frame, _playbackArchive.MagnifierChanges);
        }

        if (rows.Count != _propertyRows.Count)
        {
            _propertyRows.Clear();
            foreach (var row in rows)
            {
                _propertyRows.Add(new PropertyRowView(row));
            }

            return;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            _propertyRows[i].Update(rows[i]);
        }
    }

    // Enter, or a double click, on a row set during the recording moves to
    // that change. Ctrl+Left and Ctrl+Right move to the row's previous and
    // next change. Taken before the list's own keys, as the list moves and
    // scrolls with the arrow keys.
    private void PropertiesListView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = SeekToSelectedProperty();
            return;
        }

        if (e.Key is Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.Control &&
            PropertiesListView.SelectedItem is PropertyRowView row)
        {
            StepProperty(row, forward: e.Key == Key.Right);
            e.Handled = true;
        }
    }

    // Moves to the row's previous or next change and announces the value and
    // time there, or announces that there is none and stays.
    private void StepProperty(PropertyRowView row, bool forward)
    {
        if (_playbackArchive is not { } archive)
        {
            return;
        }

        var name = char.ToLower(row.Setting[0], System.Globalization.CultureInfo.CurrentCulture) + row.Setting[1..];
        if (PropertyChangeSteps.TimesOf(row.Row, archive.WindowsPreferences, archive.MagnifierChanges) is not { } times)
        {
            _busy.AnnounceLayout(row.Group == WindowsPreferenceTimeline.MagnifierGroup
                ? "This recording has no Magnifier change records."
                : "This recording has no Windows settings records.");
            return;
        }

        var position = _playbackPositionNanoseconds;
        var target = forward
            ? PropertyChangeSteps.Next(times, position)
            : PropertyChangeSteps.Previous(times, position);
        if (target is not { } at)
        {
            _busy.AnnounceLayout(forward ? $"No later change of {name}." : $"No earlier change of {name}.");
            return;
        }

        SeekTo(at, synchronizeAudio: false);
        var index = archive.Frames.Count == 0 ? -1 : FindFrameAtOrBefore(archive.Frames, at);
        var there = archive.WindowsPreferences
            .RowsAt(at, index < 0 ? null : archive.Frames[index], archive.MagnifierChanges)
            .FirstOrDefault(candidate => candidate.Key == row.Row.Key);
        _busy.AnnounceLayout(there is null
            ? $"Moved to the change of {name} at {FormatTime(at)}."
            : $"{there.Setting} {there.Value}, set at {FormatTime(at)}.");
    }

    private void PropertiesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        SeekToSelectedProperty();

    private bool SeekToSelectedProperty()
    {
        if (PropertiesListView.SelectedItem is not PropertyRowView { SetAt: { } setAt } row ||
            _playbackArchive is null)
        {
            return false;
        }

        SeekTo(setAt, synchronizeAudio: false);
        _busy.AnnounceLayout($"Moved to the change of {row.Setting} at {FormatTime(setAt)}.");
        return true;
    }

    private void PropertiesSplitter_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        SaveLayout();

    /// <summary>A row of the panel, updated in place.</summary>
    public sealed class PropertyRowView : INotifyPropertyChanged
    {
        private PropertyRow _row;

        public PropertyRowView(PropertyRow row) => _row = row;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Group => _row.Group;
        public string Setting => _row.Setting;
        public string Value => _row.Value;
        public string WhenSetShown => _row.WhenSetShown;
        public long? SetAt => _row.SetAt;
        public bool Changed => _row.Changed;
        public string Spoken => _row.Spoken;
        public PropertyRow Row => _row;

        public void Update(PropertyRow row)
        {
            if (row == _row)
            {
                return;
            }

            _row = row;
            // The group and setting of a row do not change.
            foreach (var name in new[] { nameof(Value), nameof(WhenSetShown), nameof(SetAt), nameof(Changed), nameof(Spoken) })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
