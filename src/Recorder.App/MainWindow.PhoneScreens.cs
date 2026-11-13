using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Recorder.Analysis;
using Recorder.Session;

namespace Recorder.App;

// The Checks tab (android-checks.md, "The player", steps 1 and 2): a phone
// recording's display density, then its apps with their versions, then
// each app's screens with how each was named, then each screen's visits
// with their states. A density, visit or state row moves the playhead to
// its start; a screen's or app's row to its first visit. The
// visit at the playhead is marked as playback moves, but the list never
// scrolls by itself, as the reading position steps do not.
//
// Step 3: the checks from the tree run in the background when the
// recording is opened, and each screen's findings follow its visits,
// grouped by check. A finding's row moves the playhead to the first time
// it was seen, shows the item in the "Accessibility tree" tab and outlines
// it on the phone's picture; "Previous time" and "Next time" step through
// the other times.
//
// Step 4c: the text contrast check runs in the background too, after the
// checks of the tree: the colours of each line of text measured on the
// phone's screenshot or the video's frame (step 4b), held to AA or AAA as
// chosen in the tab. Changing the level works the findings out again from
// the same measurement.
public partial class MainWindow
{
    private readonly ObservableCollection<PhoneScreenRowView> _phoneScreenRows = [];
    private PhoneScreenRowView? _currentVisitRow;
    private bool _phoneScreensBound;

    // The checks of the open recording: running, done, or failed.
    private SessionPlaybackArchive? _phoneChecksArchive;
    private CancellationTokenSource? _phoneChecksCancellation;
    private PhoneCheckRun? _phoneChecks;
    private string? _phoneChecksProblem;

    // The contrast measurement of the open recording, and its findings at
    // the chosen level.
    private PhoneContrastRun? _phoneContrast;
    private string? _phoneContrastProblem;
    private PhoneTextContrastRun? _phoneTextContrast;
    private PhoneNonTextContrastRun? _phoneNonTextContrast;
    private PhoneCheckSettings? _checkSettings;

    // The finding chosen in the list, and which of its times is shown.
    private PhoneFinding? _shownFinding;
    private int _shownFindingTime;

    // The level chosen last time, shown as the window opens, so it can be
    // seen and changed before a recording is open.
    private void LoadCheckSettings()
    {
        _checkSettings = PhoneCheckSettings.Load(PhoneCheckSettings.DefaultPath);
        AuditAaRadioButton.IsChecked = _checkSettings.AuditLevel == PhoneAuditLevel.AA;
        AuditAaaRadioButton.IsChecked = _checkSettings.AuditLevel == PhoneAuditLevel.AAA;
    }

    private void BindPhoneScreens()
    {
        if (!_phoneScreensBound)
        {
            PhoneScreensListBox.ItemsSource = _phoneScreenRows;
            _phoneScreensBound = true;
        }

        if (!ReferenceEquals(_phoneChecksArchive, _playbackArchive))
        {
            _phoneChecksCancellation?.Cancel();
            _phoneChecksCancellation?.Dispose();
            _phoneChecksCancellation = null;
            _phoneChecksArchive = _playbackArchive;
            _phoneChecks = null;
            _phoneChecksProblem = null;
            _phoneContrast = null;
            _phoneContrastProblem = null;
            _phoneTextContrast = null;
            _phoneNonTextContrast = null;
            _shownFinding = null;
            if (_playbackArchive is { PhoneScreens.Recorded: true } opened)
            {
                var cancellation = new CancellationTokenSource();
                _phoneChecksCancellation = cancellation;
                _ = RunPhoneChecksAsync(opened, cancellation.Token);
            }
        }

        ShowPhoneScreenRows();
    }

    // Runs the checks from the tree away from the window's thread, and
    // lists their findings when they are done, if the recording is still
    // open.
    private async Task RunPhoneChecksAsync(SessionPlaybackArchive archive, CancellationToken token)
    {
        if (!await RunTreeChecksAsync(archive, token))
        {
            return;
        }

        await RunPhoneContrastAsync(archive, token);
    }

    // The tree's checks at the audit level, away from the window's thread;
    // false when the recording was closed meanwhile. When the level is
    // changed while they run, they are run again at the new level.
    private async Task<bool> RunTreeChecksAsync(SessionPlaybackArchive archive, CancellationToken token)
    {
        PhoneAuditLevel level;
        PhoneCheckRun? run;
        string? problem;
        do
        {
            (level, run, problem) = (AuditLevel, null, null);
            try
            {
                var at = level;
                run = await Task.Run(
                    () => PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, archive.PhoneDevice, archive.RecordSource, PhoneSnapshotPictures.Of(archive), token, at),
                    token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or InvalidOperationException or
                    JsonException or UnauthorizedAccessException or DbException)
            {
                problem = exception.Message;
            }

            if (!ReferenceEquals(_playbackArchive, archive) || token.IsCancellationRequested)
            {
                return false;
            }
        }
        while (level != AuditLevel);

        _phoneChecks = run;
        _phoneChecksProblem = problem;
        ShowPhoneScreenRows();
        return true;
    }

    // Measures the colours of the text on each settled snapshot's picture,
    // with Windows' OCR finding the text (android-checks.md, steps 4b and
    // 4c), away from the window's thread.
    private async Task RunPhoneContrastAsync(SessionPlaybackArchive archive, CancellationToken token)
    {
        PhoneContrastRun? run = null;
        string? problem = null;
        try
        {
            run = await Task.Run(
                () => PhoneContrastRun.Run(archive, PhonePictures.Load, new WindowsPhoneTextReader(), token),
                token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or
                JsonException or UnauthorizedAccessException or DbException)
        {
            problem = exception.Message;
        }

        if (!ReferenceEquals(_playbackArchive, archive) || token.IsCancellationRequested)
        {
            return;
        }

        _phoneContrast = run;
        _phoneContrastProblem = problem;
        JudgeTextContrast();

        // Non-text contrast has one level, AA (WCAG 2.2's 1.4.11), so is
        // judged once, not again when the text's level changes.
        _phoneNonTextContrast = run is null ? null : PhoneNonTextContrastRun.Run(run, archive.PhoneScreens);
        ShowPhoneScreenRows();
    }

    // The text contrast findings at the chosen level, from the measurement.
    private void JudgeTextContrast()
    {
        _phoneTextContrast = _phoneContrast is { } measured && _playbackArchive is { } archive
            ? PhoneTextContrastRun.Run(measured, archive.PhoneScreens, archive.PhoneDevice, AuditLevel)
            : null;
    }

    private PhoneAuditLevel AuditLevel => _checkSettings?.AuditLevel ?? PhoneAuditLevel.AA;

    // The audit level guides the checks (owner, 2026-11-12): changing it
    // works the text contrast findings out again from the same
    // measurement, and runs the tree's checks again, as WCAG 2.5.5's runs
    // only at AAA. The chosen finding goes, with its outline and its time
    // buttons, as the findings are new.
    private void AuditLevelRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        // Setting the buttons from the saved level, as the window opens.
        if (_checkSettings is null)
        {
            return;
        }

        var level = ReferenceEquals(sender, AuditAaaRadioButton) ? PhoneAuditLevel.AAA : PhoneAuditLevel.AA;
        if (level == AuditLevel)
        {
            return;
        }

        _checkSettings = (_checkSettings ?? PhoneCheckSettings.Default) with { AuditLevel = level };
        _checkSettings.TrySave(PhoneCheckSettings.DefaultPath);
        if (!_phoneScreensBound)
        {
            return;
        }

        _shownFinding = null;
        JudgeTextContrast();
        var rerun = _playbackArchive is { PhoneScreens.Recorded: true } archive &&
            ReferenceEquals(_phoneChecksArchive, archive) &&
            _phoneChecksCancellation is { } cancellation &&
            (_phoneChecks is not null || _phoneChecksProblem is not null);
        if (rerun)
        {
            _phoneChecks = null;
            _phoneChecksProblem = null;
        }

        ShowPhoneScreenRows();
        ShowFindingButtons();
        DisplayTouchesAt(_playbackPositionNanoseconds);
        if (rerun)
        {
            _ = RunTreeChecksAsync(_playbackArchive!, _phoneChecksCancellation!.Token);
        }
    }

    private void ShowPhoneScreenRows()
    {
        _phoneScreenRows.Clear();
        _currentVisitRow = null;
        var screens = _playbackArchive?.PhoneScreens ?? PhoneScreenTimeline.Empty;
        var device = _playbackArchive?.PhoneDevice ?? PhoneDeviceTimeline.Empty;
        // A visit's rows go to its first settled snapshot's picture, as
        // the video can lag the tree (PhoneSnapshotPictures).
        var pictures = _playbackArchive is { } archive ? PhoneSnapshotPictures.Of(archive) : PhoneSnapshotPictures.Empty;
        TimelineControl.SetPhoneVisits(screens.Visits);

        // The density first (android-checks.md, step 2): the checks turn
        // pixels into dp with it.
        if (screens.Recorded || device.Densities.Count > 0)
        {
            if (device.Densities.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(
                    0,
                    "Phone density not recorded: the recording was made before the recorder read it.",
                    0,
                    null));
            }

            foreach (var period in device.Densities)
            {
                var source = period.Source == PhoneDensitySource.Physical
                    ? "the phone's own density"
                    : "set by Display size";
                var foundAtStop = period.FoundAtStop ? ", found only by the read at the stop" : string.Empty;
                _phoneScreenRows.Add(new PhoneScreenRowView(
                    0,
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"Phone density {period.Dpi:N0} dpi, so 1 dp is {period.PixelsPerDp:0.###} pixels, {source}{foundAtStop}, {period.When}"),
                    period.Start,
                    null));
            }
        }

        foreach (var app in screens.Apps)
        {
            var visits = app.Screens.Sum(screen => screen.Visits.Count);
            _phoneScreenRows.Add(new PhoneScreenRowView(
                0,
                $"App {app.Package}, {device.DescribeVersion(app.Package)}: {Count(app.Screens.Count, "screen")}, {Count(visits, "visit")}",
                pictures.TimeOf(app.Screens[0].Visits[0]),
                null));
            foreach (var screen in app.Screens)
            {
                var activity = screen.Activity is null
                    ? "activity not given by a window-state event"
                    : "activity " + PhoneScreen.ShortClass(screen.Activity);
                _phoneScreenRows.Add(new PhoneScreenRowView(
                    1,
                    $"Screen {screen.Number}: {screen.Label}, {screen.NamedBy}, {activity}, {Count(screen.Visits.Count, "visit")}",
                    pictures.TimeOf(screen.Visits[0]),
                    null));
                foreach (var visit in screen.Visits)
                {
                    var settled = visit.SettledSnapshots.Count;
                    var shownAt = pictures.TimeOf(visit);
                    _phoneScreenRows.Add(new PhoneScreenRowView(
                        2,
                        $"Visit {visit.Number}, {visit.When}, started by {visit.CauseText}{(visit.IsReturn ? ", a return" : string.Empty)}; " +
                        $"{Count(visit.Snapshots.Count, "snapshot")}, {settled.ToString("N0", CultureInfo.CurrentCulture)} settled",
                        shownAt,
                        visit));
                    foreach (var state in visit.States)
                    {
                        var from = state.Package is null ? string.Empty : $", from {state.Package}";
                        var named = state.NamedBy == PhoneTitleSource.Untitled ? ", untitled" : $" '{state.Name}'";
                        var shows = state.FixedText is { } fixedText && fixedText != state.Name
                            ? $", first fixed text '{fixedText}'"
                            : string.Empty;
                        _phoneScreenRows.Add(new PhoneScreenRowView(
                            3,
                            $"In {state.Kind}{named}{shows}{from}, {state.When}",
                            state.Start,
                            null));
                    }

                    AddFindingRows(visit, shownAt);
                }
            }
        }

        ShowCurrentVisitButton.IsEnabled = screens.Recorded;
        ShowFindingButtons();
        DisplayPhoneVisitAt(_playbackPositionNanoseconds);
    }

    // A visit's findings, after its row and its states (owner,
    // 2026-11-12: the content can change between visits): a row for each
    // check that found something, saying how many at each level, then a
    // row for each finding. A visit with no settled snapshot is not
    // checked, as both checks run only on settled snapshots.
    private void AddFindingRows(PhoneVisit visit, long shownAt)
    {
        if (visit.SettledSnapshots.Count == 0)
        {
            if (_phoneChecks is not null || _phoneTextContrast is not null || _phoneNonTextContrast is not null)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, "Not checked, as no snapshot of this visit settled.", shownAt, null)
                {
                    IsCheck = true,
                });
            }

            return;
        }

        if (_phoneChecks is { } run)
        {
            var findings = run.For(visit);
            if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, "No findings from the checks of the tree in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings);
            }
        }

        if (_phoneTextContrast is { } contrast)
        {
            var findings = contrast.For(visit);
            var title = PhoneCheckNames.Title(contrast.Check);
            var lines = contrast.LinesIn(visit);
            if (lines == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no text was measured in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no findings, from {Count(lines, "line")} measured.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(lines, "line")} measured");
            }
        }

        if (_phoneNonTextContrast is { } nonText)
        {
            var findings = nonText.For(visit);
            var title = PhoneCheckNames.Title(PhoneCheckNames.NonTextContrast);
            var controls = nonText.ControlsIn(visit);
            if (controls == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no control was measured in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no findings, from {Count(controls, "control")} measured.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(controls, "control")} measured");
            }
        }
    }

    // A row for each check of the findings, saying how many at each level,
    // then a row for each finding.
    private void AddCheckRows(IReadOnlyList<PhoneFinding> findings, string measured = "")
    {
        foreach (var check in findings.GroupBy(finding => finding.Check))
        {
            _phoneScreenRows.Add(new PhoneScreenRowView(
                3,
                $"{PhoneCheckNames.Title(check.Key)}: {Levels(check)}{measured}",
                check.First().Times[0].Time,
                null)
            {
                IsCheck = true,
            });
            foreach (var finding in check)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(4, finding.Text, finding.Times[0].Time, null)
                {
                    Finding = finding,
                });
            }
        }
    }

    // How many findings at each level: "6 errors, 2 not run".
    private static string Levels(IEnumerable<PhoneFinding> findings) =>
        string.Join(", ", findings
            .GroupBy(finding => finding.Level)
            .OrderBy(level => level.Key)
            .Select(level => level.Key is PhoneCheckLevel.NotRun or PhoneCheckLevel.Info
                ? string.Create(CultureInfo.CurrentCulture, $"{level.Count():N0} {PhoneCheckNames.Word(level.Key)}")
                : Count(level.Count(), PhoneCheckNames.Word(level.Key))));

    private void DisplayPhoneVisitAt(long positionNanoseconds)
    {
        if (!_phoneScreensBound)
        {
            return;
        }

        if (_playbackArchive is not { PhoneScreens.Recorded: true } archive)
        {
            ChecksStatusTextBlock.Text = _playbackArchive is null
                ? "Open a recording of a phone to list its apps, screens and visits."
                : "This recording holds no snapshot of a phone's app window, so it has no screens to list.";
            return;
        }

        var visit = archive.PhoneScreens.VisitAt(positionNanoseconds);
        var current = visit is null ? null : _phoneScreenRows.FirstOrDefault(row => ReferenceEquals(row.Visit, visit));
        if (!ReferenceEquals(current, _currentVisitRow))
        {
            if (_currentVisitRow is not null)
            {
                _currentVisitRow.IsCurrent = false;
            }

            if (current is not null)
            {
                current.IsCurrent = true;
            }

            _currentVisitRow = current;
        }

        var screens = archive.PhoneScreens;
        var totals = $"{Count(screens.Apps.Count, "app")}, {Count(screens.Screens.Count, "screen")}, {Count(screens.Visits.Count, "visit")}.";
        ChecksStatusTextBlock.Text = (visit is null
            ? $"{totals} Before the first visit."
            : $"{totals} Visit {visit.Number}, to {visit.Screen.Label}, at the playhead.") + " " + AuditSummary() + " " + ChecksSummary();
    }

    // The audit level the checks were run at, and what it changes
    // (owner, 2026-11-12: the level guides the checks and their reporting).
    private string AuditSummary() => AuditLevel == PhoneAuditLevel.AAA
        ? "Audit level AAA: text contrast is held to WCAG 1.4.6, and targets to 44 by 44 dp (WCAG 2.5.5) as well as 2.5.8; 2.5.5's exceptions (an equivalent target, a target in text, one sized by Android, an essential presentation) are not judged."
        : "Audit level AA: text contrast is held to WCAG 1.4.3, and targets to 2.5.8.";

    // What the checks found, or why they have not.
    private string ChecksSummary()
    {
        if (_phoneChecksProblem is { } problem)
        {
            return $"The checks of the tree could not be run: {problem} {ContrastSummary()}";
        }

        if (_phoneChecks is not { } run)
        {
            return "Running the checks of the tree.";
        }

        var findings = run.Findings.Count == 0
            ? "no findings"
            : $"{Count(run.Findings.Count, "finding")} ({Levels(run.Findings)})";
        var summary = string.Create(
            CultureInfo.CurrentCulture,
            $"Checks of the tree: {findings}, from {Count(run.SnapshotsChecked, "settled snapshot")}, in {run.Elapsed.TotalSeconds:0.0} seconds.");
        if (run.WithoutDensity > 0)
        {
            summary += $" The touch target checks did not run on {Count(run.WithoutDensity, "settled snapshot")}, as no density was recorded for them.";
        }

        if (run.DensityChanged > 0)
        {
            summary += $" The touch target checks did not run on {Count(run.DensityChanged, "settled snapshot")} whose items were taken after the density changed during the visit, as they may have been laid out at either density.";
        }

        if (run.Problems.Count > 0)
        {
            summary += $" {Count(run.Problems.Count, "snapshot")} could not be checked: {string.Join("; ", run.Problems.Take(3))}{(run.Problems.Count > 3 ? "; and more" : string.Empty)}.";
        }

        summary += " " + ContrastSummary();
        if (_shownFinding is { } shown)
        {
            summary += string.Create(
                CultureInfo.CurrentCulture,
                $" Showing time {_shownFindingTime + 1:N0} of {shown.Times.Count:N0} of the chosen finding, outlined on the phone's picture while its items are shown.");
        }

        return summary;
    }

    // What the contrast checks found, of text and non-text, or why they have not.
    private string ContrastSummary()
    {
        if (_phoneContrastProblem is { } problem)
        {
            return $"The contrast checks, of text and non-text, could not be run: {problem}";
        }

        if (_phoneContrast is not { } measured || _phoneTextContrast is not { } contrast)
        {
            return "Measuring the contrast of text and non-text.";
        }

        var title = PhoneCheckNames.Title(contrast.Check);
        var findings = contrast.Findings.Count == 0
            ? "no findings"
            : $"{Count(contrast.Findings.Count, "finding")} ({Levels(contrast.Findings)})";
        var summary = string.Create(
            CultureInfo.CurrentCulture,
            $"{title}: {findings}, from {Count(contrast.Lines, "line")} of text measured, {contrast.Passed:N0} passed, on {Count(measured.Snapshots.Count, "settled snapshot")} ({measured.FromScreenshots:N0} from the phone's screenshots, {measured.FromFrames:N0} from the video), in {measured.Elapsed.TotalSeconds:0.0} seconds.");
        if (_phoneNonTextContrast is { } nonText)
        {
            var nonTextFindings = nonText.Findings.Count == 0
                ? "no findings"
                : $"{Count(nonText.Findings.Count, "finding")} ({Levels(nonText.Findings)})";
            summary += string.Create(
                CultureInfo.CurrentCulture,
                $" {PhoneCheckNames.Title(PhoneCheckNames.NonTextContrast)}, at AA, its only level: {nonTextFindings}, from {Count(nonText.Controls, "control")} measured, {nonText.Passed:N0} passed; graphics with no item in the tree are not checked.");
        }

        var engine = measured.Snapshots.Select(snapshot => snapshot.Engine).FirstOrDefault(name => !string.IsNullOrEmpty(name));
        summary += measured.OcrUnavailable is { } unavailable
            ? $" OCR was not used, as {unavailable}, so only the app's text was measured, on its items' bounds."
            : engine is null ? string.Empty : $" The text was found with {engine}.";
        if (measured.Problems.Count > 0)
        {
            summary += $" {Count(measured.Problems.Count, "snapshot")} could not be measured: {string.Join("; ", measured.Problems.Take(3))}{(measured.Problems.Count > 3 ? "; and more" : string.Empty)}.";
        }

        return summary;
    }

    private void ShowCurrentVisitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phoneScreenRows.Count == 0)
        {
            return;
        }

        PhoneScreensListBox.ScrollIntoView(_currentVisitRow ?? _phoneScreenRows[0]);
    }

    private void PhoneScreenRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhoneScreenRowView row })
        {
            GoToPhoneScreenRow(row);
        }
    }

    private void PhoneScreenRow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && sender is FrameworkElement { DataContext: PhoneScreenRowView row })
        {
            GoToPhoneScreenRow(row);
            e.Handled = true;
        }
    }

    private void GoToPhoneScreenRow(PhoneScreenRowView row)
    {
        if (_playbackArchive is null)
        {
            return;
        }

        if (row.Finding is { } finding)
        {
            ShowFindingTime(finding, 0);
            return;
        }

        _shownFinding = null;
        ShowFindingButtons();
        PausePlayback();
        SeekTo(row.Start, synchronizeAudio: false);
    }

    // Moves the playhead to one of a finding's times, shows its item in the
    // "Accessibility tree" tab, and outlines it on the phone's picture.
    private void ShowFindingTime(PhoneFinding finding, int index)
    {
        if (_playbackArchive is null || index < 0 || index >= finding.Times.Count)
        {
            return;
        }

        _shownFinding = finding;
        _shownFindingTime = index;
        ShowFindingButtons();
        PausePlayback();
        var time = finding.Times[index];
        SeekTo(time.Time, synchronizeAudio: false);
        ShowTreeRow(time.TreeKey, select: true);
        DisplayTouchesAt(_playbackPositionNanoseconds);
        DisplayPhoneVisitAt(_playbackPositionNanoseconds);
    }

    private void ShowFindingButtons()
    {
        PreviousFindingTimeButton.IsEnabled = _shownFinding is not null && _shownFindingTime > 0;
        NextFindingTimeButton.IsEnabled = _shownFinding is { } shown && _shownFindingTime < shown.Times.Count - 1;
    }

    private void PreviousFindingTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shownFinding is { } finding)
        {
            ShowFindingTime(finding, _shownFindingTime - 1);
        }
    }

    private void NextFindingTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shownFinding is { } finding)
        {
            ShowFindingTime(finding, _shownFindingTime + 1);
        }
    }

    private static string Count(int count, string noun) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? noun : noun + "s")}");
}

/// <summary>A row of the Checks tab: the density, an app, a screen, a visit, a state, a check or a finding.</summary>
public sealed class PhoneScreenRowView : INotifyPropertyChanged
{
    private bool _isCurrent;

    public PhoneScreenRowView(int level, string text, long start, PhoneVisit? visit)
    {
        Level = level;
        Text = text;
        Start = start;
        Visit = visit;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>0 for the density or an app, 1 a screen, 2 a visit, 3 a state or a check, 4 a finding.</summary>
    public int Level { get; }

    public string Text { get; }

    /// <summary>Where the row moves the playhead.</summary>
    public long Start { get; }

    /// <summary>The visit, for a visit's row.</summary>
    public PhoneVisit? Visit { get; }

    /// <summary>The finding, for a finding's row.</summary>
    public PhoneFinding? Finding { get; init; }

    /// <summary>Whether the row heads a check's findings, or says a visit has none or was not checked.</summary>
    public bool IsCheck { get; init; }

    public bool IsHeading => Level < 2 || IsCheck;

    public Thickness Indent => new(Level * 20, 0, 0, 0);

    /// <summary>Whether the row is the visit at the playhead.</summary>
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

    /// <summary>The row's accessible name: its text, and whether it is at the playhead.</summary>
    public string Spoken => Text + (IsCurrent ? ", at the playhead" : string.Empty);
}
