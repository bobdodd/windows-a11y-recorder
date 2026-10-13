using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Recorder.Session;

namespace Recorder.App;

// The list of the reading position's steps (screen-reader-activity.md, "4b,
// the list of steps"): each step with its time, what is known, what moved
// it, and where it went. A step moves the playhead to it and selects its
// record, so Selected event values gives the basis. The step at the
// playhead is marked as playback moves, but the list never scrolls by
// itself, so rows do not move under a head pointer; Show current step
// scrolls to it. Agreed with the owner on 2026-10-12.
public partial class MainWindow
{
    private readonly ObservableCollection<ReadingStepView> _readingSteps = [];
    private ReadingStepView? _currentReadingStep;
    private bool _readingStepsBound;

    private void BindReadingSteps()
    {
        if (!_readingStepsBound)
        {
            ReadingStepsListBox.ItemsSource = _readingSteps;
            _readingStepsBound = true;
        }

        _readingSteps.Clear();
        _currentReadingStep = null;
        var steps = _playbackArchive?.ReadingPosition.Steps ?? [];
        for (var index = 0; index < steps.Count; index++)
        {
            _readingSteps.Add(new ReadingStepView(steps[index], index));
        }

        ShowCurrentStepButton.IsEnabled = _readingSteps.Count > 0;
        DisplayReadingStepAt(_playbackPositionNanoseconds);
    }

    private void DisplayReadingStepAt(long positionNanoseconds)
    {
        if (!_readingStepsBound)
        {
            return;
        }

        if (_playbackArchive is not { ReadingPosition.Recorded: true } archive || _readingSteps.Count == 0)
        {
            ReadingStepsStatusTextBlock.Text = _playbackArchive is null
                ? "Open a recording with accessibility requests to list the reading position's steps."
                : "This recording holds no requests to the page, so the reading position has no steps.";
            return;
        }

        var index = archive.ReadingPosition.IndexAt(positionNanoseconds);
        var current = index >= 0 && index < _readingSteps.Count ? _readingSteps[index] : null;
        if (!ReferenceEquals(current, _currentReadingStep))
        {
            if (_currentReadingStep is not null)
            {
                _currentReadingStep.IsCurrent = false;
            }

            if (current is not null)
            {
                current.IsCurrent = true;
            }

            _currentReadingStep = current;
        }

        ReadingStepsStatusTextBlock.Text = current is null
            ? $"Before the first of {_readingSteps.Count:N0} steps."
            : $"Step {index + 1:N0} of {_readingSteps.Count:N0} at the playhead.";
    }

    private void ShowCurrentStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (_readingSteps.Count == 0)
        {
            return;
        }

        ReadingStepsListBox.ScrollIntoView(_currentReadingStep ?? _readingSteps[0]);
    }

    private void ReadingStep_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReadingStepView step })
        {
            GoToReadingStep(step);
        }
    }

    private void ReadingStep_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && sender is FrameworkElement { DataContext: ReadingStepView step })
        {
            GoToReadingStep(step);
            e.Handled = true;
        }
    }

    // Selects the step's record, which moves the playhead to it and shows
    // its values; a step without a record moves the playhead only.
    private void GoToReadingStep(ReadingStepView view)
    {
        if (_playbackArchive is null)
        {
            return;
        }

        if (view.Step.Event is { } item)
        {
            TimelineControl.SelectTimelineEvent(item);
            return;
        }

        PausePlayback();
        SeekTo(view.Step.Time, synchronizeAudio: false);
    }
}

/// <summary>A row of the list of reading position steps.</summary>
public sealed class ReadingStepView : INotifyPropertyChanged
{
    private bool _isCurrent;

    public ReadingStepView(ReadingPositionStep step, int index)
    {
        Step = step;
        Index = index;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadingPositionStep Step { get; }

    public int Index { get; }

    public string Time => Step.When;

    public string State => Step.State;

    public string Cause => Step.Cause;

    public string Value => Step.Value;

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
        $"{Time}, {State}, {Cause}, {Value}{(IsCurrent ? ", at the playhead" : string.Empty)}";
}
