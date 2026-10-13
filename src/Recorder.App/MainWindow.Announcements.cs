using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Recorder.Session;

namespace Recorder.App;

// What NVDA had to announce at each reading position step
// (screen-reader-activity.md, "What NVDA had to announce"): each with its
// time, what is known, what moved the reading position, and the text. As
// the list of steps, a row moves the playhead to its step and selects its
// record, so Selected event values gives the basis; the row at the playhead
// is marked, but the list never scrolls by itself, so rows do not move under
// a head pointer; Show current step scrolls to it. Agreed with the owner on
// 2026-10-12.
public partial class MainWindow
{
    private readonly ObservableCollection<AnnouncementView> _announcements = [];
    private AnnouncementView? _currentAnnouncement;
    private bool _announcementsBound;

    private void BindAnnouncements()
    {
        if (!_announcementsBound)
        {
            AnnouncementsListBox.ItemsSource = _announcements;
            _announcementsBound = true;
        }

        _announcements.Clear();
        _currentAnnouncement = null;
        var announcements = _playbackArchive?.NvdaAnnouncements.Announcements ?? [];
        for (var index = 0; index < announcements.Count; index++)
        {
            _announcements.Add(new AnnouncementView(announcements[index], index));
        }

        ShowCurrentAnnouncementButton.IsEnabled = _announcements.Count > 0;
        DisplayAnnouncementAt(_playbackPositionNanoseconds);
    }

    private void DisplayAnnouncementAt(long positionNanoseconds)
    {
        if (!_announcementsBound)
        {
            return;
        }

        if (_playbackArchive is not { } archive || !archive.NvdaAnnouncements.Recorded || _announcements.Count == 0)
        {
            AnnouncementsStatusTextBlock.Text = _playbackArchive is null
                ? "Open a recording with accessibility requests to list what NVDA had to announce."
                : "This recording holds no reading position steps with the page's tree, so what NVDA had to announce is not known.";
            return;
        }

        // The announcements are the reading position's steps, one each, in
        // their order.
        var index = archive.ReadingPosition.IndexAt(positionNanoseconds);
        var current = index >= 0 && index < _announcements.Count ? _announcements[index] : null;
        if (!ReferenceEquals(current, _currentAnnouncement))
        {
            if (_currentAnnouncement is not null)
            {
                _currentAnnouncement.IsCurrent = false;
            }

            if (current is not null)
            {
                current.IsCurrent = true;
            }

            _currentAnnouncement = current;
        }

        AnnouncementsStatusTextBlock.Text = current is null
            ? $"Before the first of {_announcements.Count:N0} steps. {NvdaAnnouncementTimeline.Note}"
            : $"Step {index + 1:N0} of {_announcements.Count:N0} at the playhead. {NvdaAnnouncementTimeline.Note}";
    }

    private void ShowCurrentAnnouncementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_announcements.Count == 0)
        {
            return;
        }

        AnnouncementsListBox.ScrollIntoView(_currentAnnouncement ?? _announcements[0]);
    }

    private void Announcement_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AnnouncementView view })
        {
            GoToReadingStep(view.Announcement.Step);
        }
    }

    private void Announcement_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && sender is FrameworkElement { DataContext: AnnouncementView view })
        {
            GoToReadingStep(view.Announcement.Step);
            e.Handled = true;
        }
    }
}

/// <summary>A row of the list of what NVDA had to announce.</summary>
public sealed class AnnouncementView : INotifyPropertyChanged
{
    private bool _isCurrent;

    public AnnouncementView(NvdaAnnouncement announcement, int index)
    {
        Announcement = announcement;
        Index = index;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public NvdaAnnouncement Announcement { get; }

    public int Index { get; }

    public string Time => Announcement.When;

    public string State => Announcement.State;

    public string Cause => Announcement.Step.Cause;

    public string Text => Announcement.Text;

    /// <summary>Whether the step is the one at the playhead.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
            {
                return;
            }

            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Spoken)));
        }
    }

    /// <summary>The row's accessible name: all its text, and whether it is at the playhead.</summary>
    public string Spoken =>
        $"{Time}, {State}, {Cause}, NVDA had to announce: {Text}{(IsCurrent ? ", at the playhead" : string.Empty)}";
}
