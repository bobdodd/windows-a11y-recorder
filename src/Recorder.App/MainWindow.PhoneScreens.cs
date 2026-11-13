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
    private PhoneImagesOfTextRun? _phoneImagesOfText;
    private PhoneLabelInNameRun? _phoneLabelInName;
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
        OrientationHoldComboBox.ItemsSource = PhoneOrientationChecks.HoldChoices
            .Select(seconds => new OrientationHoldChoice(seconds))
            .ToList();
        OrientationHoldComboBox.SelectedValue = OrientationHoldSeconds;
    }

    // A choice of how long the phone must be held turned, as the list says it.
    private sealed record OrientationHoldChoice(int Seconds)
    {
        public string Text => Seconds == 1 ? "1 second" : string.Create(CultureInfo.CurrentCulture, $"{Seconds:N0} seconds");

        public override string ToString() => Text;
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
            _phoneImagesOfText = null;
            _phoneLabelInName = null;
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
        int hold;
        PhoneCheckRun? run;
        string? problem;
        do
        {
            (level, hold, run, problem) = (AuditLevel, OrientationHoldSeconds, null, null);
            try
            {
                var at = level;
                var held = hold;
                run = await Task.Run(
                    () => PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, archive.PhoneDevice, archive.RecordSource, PhoneSnapshotPictures.Of(archive), token, at,
                        archive.PhoneTouches, archive.PhoneAnnouncements, archive.PhoneSettings, PhonePictures.Load, archive.PhoneInputs,
                        archive.PhoneOrientation, held),
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
        while (level != AuditLevel || hold != OrientationHoldSeconds);

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

        // Label in Name is level A, so the same at both audit levels.
        _phoneLabelInName = run is null ? null : PhoneLabelInNameRun.Run(run, archive.PhoneScreens);
        ShowPhoneScreenRows();
    }

    // The text contrast findings at the chosen level, from the measurement,
    // and the images of text, which add WCAG 1.4.9's findings at AAA.
    private void JudgeTextContrast()
    {
        if (_phoneContrast is not { } measured || _playbackArchive is not { } archive)
        {
            _phoneTextContrast = null;
            _phoneImagesOfText = null;
            return;
        }

        _phoneTextContrast = PhoneTextContrastRun.Run(measured, archive.PhoneScreens, archive.PhoneDevice, AuditLevel);
        _phoneImagesOfText = PhoneImagesOfTextRun.Run(measured, archive.PhoneScreens, archive.PhoneDevice, AuditLevel);
    }

    private PhoneAuditLevel AuditLevel => _checkSettings?.AuditLevel ?? PhoneAuditLevel.AA;

    private int OrientationHoldSeconds => _checkSettings?.OrientationHoldSeconds ?? PhoneOrientationChecks.DefaultHoldSeconds;

    // How long the phone must be held turned for the orientation check
    // (owner, 2026-11-13, a setting): changing it runs the tree's checks
    // again, as the audit level does.
    private void OrientationHoldComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_checkSettings is null || OrientationHoldComboBox.SelectedValue is not int seconds || seconds == OrientationHoldSeconds)
        {
            return;
        }

        _checkSettings = _checkSettings with { OrientationHoldSeconds = seconds };
        _checkSettings.TrySave(PhoneCheckSettings.DefaultPath);
        RerunTreeChecks();
    }

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
        RerunTreeChecks();
    }

    // Runs the tree's checks again with the settings now chosen, if they
    // have run or failed for the open recording. The chosen finding goes,
    // with its outline and its time buttons, as the findings are new.
    private void RerunTreeChecks()
    {
        if (!_phoneScreensBound)
        {
            return;
        }

        _shownFinding = null;
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

            // Step 12's warning for an app with no live region in the whole
            // recording, under the app, as it is about all its visits.
            if (_phoneChecks is { } appRun && appRun.AppFindings.Where(finding => finding.Visit.Package == app.Package).ToList() is { Count: > 0 } ofApp)
            {
                AddCheckRows(ofApp, indent: 1);
            }

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
            if (_phoneChecks is not null || _phoneTextContrast is not null || _phoneNonTextContrast is not null || _phoneImagesOfText is not null ||
                _phoneLabelInName is not null)
            {
                // The status message check follows every snapshot, settled
                // or not, so can find a message in a visit too brief to
                // settle, as the client's loading message was (measured).
                _phoneScreenRows.Add(new PhoneScreenRowView(3, "Not checked, as no snapshot of this visit settled, except for status messages, which are looked for in every snapshot.", shownAt, null)
                {
                    IsCheck = true,
                });
                if (_phoneChecks?.For(visit) is { Count: > 0 } brief)
                {
                    AddCheckRows(brief);
                }
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

        // Images of text need OCR's words, so say nothing of a recording OCR
        // did not read; the summary says why.
        if (_phoneImagesOfText is { SnapshotsRead: > 0 } images)
        {
            var findings = images.For(visit);
            var title = PhoneCheckNames.Title(PhoneCheckNames.ImageOfText);
            var count = images.ImagesIn(visit);
            if (!images.Read(visit))
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: not checked, as OCR read no picture of this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no image in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no findings, from {Count(count, "image")} read by OCR.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(count, "image")} read by OCR");
            }
        }

        if (_phoneLabelInName is { } labels)
        {
            var findings = labels.For(visit);
            var controls = labels.ControlsIn(visit);
            if (controls == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{PhoneLabelInNameRun.Title}: no control with a label and a content description in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{PhoneLabelInNameRun.Title}: no findings, from {Count(controls, "control")} checked.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(controls, "control")} checked");
            }
        }
    }

    // A row for each check of the findings, saying how many at each level,
    // then a row for each finding.
    private void AddCheckRows(IReadOnlyList<PhoneFinding> findings, string measured = "", int indent = 3)
    {
        foreach (var check in findings.GroupBy(finding => finding.Check))
        {
            _phoneScreenRows.Add(new PhoneScreenRowView(
                indent,
                $"{PhoneCheckNames.Title(check.Key)}: {Levels(check)}{measured}",
                check.First().Times[0].Time,
                null)
            {
                IsCheck = true,
            });
            foreach (var finding in check)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(indent + 1, finding.Text, finding.Times[0].Time, null)
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
        ? "Audit level AAA: text contrast is held to WCAG 1.4.6, targets to 44 by 44 dp (WCAG 2.5.5) as well as 2.5.8, and images of text to 1.4.9 as well as 1.4.5; 2.5.5's exceptions (an equivalent target, a target in text, one sized by Android, an essential presentation) are not judged."
        : "Audit level AA: text contrast is held to WCAG 1.4.3, targets to 2.5.8, and images of text to 1.4.5.";

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
        var unfocusable = run.Findings.Where(finding => finding.Check == PhoneCheckNames.ClickableNotFocusable).ToList();
        var unfocusableFindings = unfocusable.Count == 0
            ? "no findings"
            : $"{Count(unfocusable.Count, "finding")} ({Levels(unfocusable)})";
        summary += string.Create(
            CultureInfo.CurrentCulture,
            $" {PhoneCheckNames.Title(PhoneCheckNames.ClickableNotFocusable)}, one of them: {unfocusableFindings}, from {Count(run.Clickable, "clickable item")} checked, {run.ClickableSetAside:N0} set aside inside a focusable clickable item; each is a warning, until a keyboard test confirms it.");
        var fieldChecks = new HashSet<string>(StringComparer.Ordinal)
        {
            PhoneCheckNames.FieldNoLabel, PhoneCheckNames.FieldNoName, PhoneCheckNames.InvalidNoError,
            PhoneCheckNames.ExpandableNoState, PhoneCheckNames.SliderNoRange,
        };
        var fieldFound = run.Findings.Where(finding => fieldChecks.Contains(finding.Check)).ToList();
        var fieldFindings = fieldFound.Count == 0
            ? "no findings"
            : $"{Count(fieldFound.Count, "finding")} ({Levels(fieldFound)})";
        summary += string.Create(
            CultureInfo.CurrentCulture,
            $" Fields and states, five of them: {fieldFindings}, from {Count(run.Fields, "field")}, {run.Invalid:N0} marked invalid, {Count(run.Expandable, "expandable item")} and {Count(run.Sliders, "slider")} checked; a field's label is only an item linked to it as its label (labeledBy), as a hint is not shown once text is typed.");
        if (run.ExpandableNotRecorded > 0)
        {
            summary += $" The expanded state of {Count(run.ExpandableNotRecorded, "expandable item")} was not recorded, as the phone's Android is older than 16, so they were not checked.";
        }

        var statusFound = run.Findings.Where(finding => finding.Check == PhoneCheckNames.StatusNotAnnounced).Concat(run.AppFindings).ToList();
        var statusFindings = statusFound.Count == 0
            ? "no findings"
            : $"{Count(statusFound.Count, "finding")} ({Levels(statusFound)})";
        var brief = run.StatusRun
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{Count(run.BriefMessages, "brief message")} (text shown and gone within 5 seconds, with no touch between), {run.BriefExposed:N0} of them in a live region, announced or in a titled pane that appeared with them")
            : "brief messages not looked for, as no touches were recorded, so text going away by itself cannot be told from text a touch took away";
        summary += string.Create(
            CultureInfo.CurrentCulture,
            $" Status messages (WCAG 4.1.3), two checks: {statusFindings}; {brief}; {Count(run.LiveRegionItems, "item")} in a live region, {Count(run.Announcements, "announcement")} and {Count(run.Toasts, "toast")} in the recording.");
        var timingChecks = new[]
        {
            PhoneCheckNames.TextGoneByItself, PhoneCheckNames.TextGoneAsContentChanged, PhoneCheckNames.TextUpdatingByItself, PhoneCheckNames.MovingByItself,
        };
        var timingFound = run.Findings.Where(finding => timingChecks.Contains(finding.Check)).ToList();
        var timingFindings = timingFound.Count == 0
            ? "no findings"
            : $"{Count(timingFound.Count, "finding")} ({Levels(timingFound)})";
        var timeout = !run.SettingsRecorded
            ? "Time to take action was not recorded, so texts gone within 5 seconds were looked at"
            : run.TimeoutMilliseconds is { } milliseconds
                ? string.Create(CultureInfo.CurrentCulture, $"Time to take action was set, at most {milliseconds / 1000.0:0.##} seconds, so texts gone sooner than it were looked at")
                : "Time to take action was Default, so texts gone within 5 seconds were looked at";
        var timed = run.StatusRun
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{timeout}: {Count(run.TimedMessages, "text")} with no touch between, {run.TimedAlone:N0} of them gone with nothing new shown as they went; {Count(run.UpdatedItems, "item")} whose text changed in place with no touch")
            : "text going away or updating by itself not looked for, as no touches were recorded";
        var moving = run.MotionRun
            ? string.Create(CultureInfo.CurrentCulture, $"{Count(run.MovingRuns, "part")} of the phone's video that kept changing for more than 5 seconds with no touch (from the video, so approximate)")
            : "moving content not looked for, as it needs the phone's video and touches recorded";
        summary += $" Timing (WCAG 2.2.1 and 2.2.2), four checks: {timingFindings}; {timed}; {moving}.";
        var contextChecks = new[]
        {
            PhoneCheckNames.ContextOnFocus, PhoneCheckNames.ContextOnScreenReaderFocus, PhoneCheckNames.ContextOnInput,
        };
        var contextFound = run.Findings.Where(finding => contextChecks.Contains(finding.Check)).ToList();
        var contextFindings = contextFound.Count == 0
            ? "no findings"
            : $"{Count(contextFound.Count, "finding")} ({Levels(contextFound)})";
        var context = run.ContextRun
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{Count(run.FocusMoves, "focus move")} by a key or a tap on a field, {Count(run.ScreenReaderFocusMoves, "TalkBack focus move")} after a gesture or key, and {Count(run.SettingChanges, "setting change")} (typing, checking, choosing or moving a slider); {run.ContextChanges:N0} {(run.ContextChanges == 1 ? "change" : "changes")} of context (a new screen or app, a window that took the focus, a pane, or the focus moving by itself) within 1.5 seconds of one, with nothing touched, pressed or clicked between")
            : "not looked for, as no touches were recorded, so a tap that caused a change cannot be told from none";
        summary += $" Changes of context (WCAG 3.2.1 and 3.2.2), three checks: {contextFindings}; {context}.";
        var orientationFound = run.Findings.Where(finding => finding.Check == PhoneCheckNames.OrientationRestricted).ToList();
        var orientationFindings = orientationFound.Count == 0
            ? "no findings"
            : $"{Count(orientationFound.Count, "finding")} ({Levels(orientationFound)})";
        var orientation = run.OrientationRun
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"the phone was turned between upright and sideways {Count(run.OrientationTurns, "time")}, from the companion app's own sensor, and the screen stayed as it was, with Auto-rotate on, for {Count(run.OrientationHoldSeconds, "second")} or more after {Count(run.OrientationNotFollowed, "turn")} within a visit")
            : $"not looked for, as {run.OrientationNotRun ?? "the checks did not run"}";
        summary += $" Orientation (WCAG 1.3.4), one check: {orientationFindings}; {orientation}; it is looked for only while Auto-rotate is on.";
        if (run.TextFocusablePackages.Count > 0)
        {
            summary += $" In {string.Join(" and ", run.TextFocusablePackages)}, every item with text is focusable, as in an app drawn with Flutter, whose tree marks focusable what a screen reader can reach, so the tree does not show which items a keyboard reaches.";
        }
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

        if (_phoneImagesOfText is { } images)
        {
            var checks = images.Level == PhoneAuditLevel.AAA
                ? $"{PhoneCheckNames.Title(PhoneCheckNames.ImageOfText)} and {PhoneCheckNames.Title(PhoneCheckNames.ImageOfTextAaa)}"
                : PhoneCheckNames.Title(PhoneCheckNames.ImageOfText);
            var imageFindings = images.Findings.Count == 0
                ? "no findings"
                : $"{Count(images.Findings.Count, "finding")} ({Levels(images.Findings)})";
            summary += images.SnapshotsRead == 0
                ? $" {checks}: not checked, as it needs OCR's words."
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $" {checks}: {imageFindings}, from {Count(images.Images, "image")} read by OCR, {images.Passed:N0} passed; the words OCR read inside an image that no item's text holds are a probable image of text, a warning, as the exceptions, such as a logotype, are not judged.");
        }

        if (_phoneLabelInName is { } labels)
        {
            var labelFindings = labels.Findings.Count == 0
                ? "no findings"
                : $"{Count(labels.Findings.Count, "finding")} ({Levels(labels.Findings)})";
            var read = labels.UsedOcr ? "the tree's text and, where it has none, OCR's" : "the tree's text";
            summary += string.Create(
                CultureInfo.CurrentCulture,
                $" {PhoneLabelInNameRun.Title}: {labelFindings}, from {Count(labels.Controls, "control")} checked, {labels.Passed:N0} passed, {labels.Unread:N0} with no label read; the label is all the text inside a control, and the text of any item it is labelled by, from {read}, against its content description; text beside a control is not taken as its label.");
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
