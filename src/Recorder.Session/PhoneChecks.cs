using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// One time a finding was seen: when it is shown (the settled snapshot's
/// picture, PhoneSnapshotPictures, or the snapshot's time when no picture
/// shows it), a settled snapshot of the visit, the
/// snapshot that took the window's items it was checked on, the item's
/// bounds and row key there, what was measured, and the state of the
/// screen, such as "in dialog 'Delete entry'", or null.
/// </summary>
public sealed record PhoneFindingTime(
    long Time,
    PhoneVisit Visit,
    SessionTimelineEvent Snapshot,
    SessionTimelineEvent TakenBy,
    int WindowId,
    PhoneBounds Bounds,
    string TreeKey,
    string Measured,
    string? State);

/// <summary>
/// A finding (android-checks.md, "Items and findings"): one check's result
/// for one item in one visit to a screen, at one level, with every settled
/// snapshot of the visit it was seen in. The same item failing on another
/// visit is another finding, as the screen's content can change between
/// visits (owner, 2026-11-12). Every finding of the tree checks is exact,
/// from the tree.
/// </summary>
public sealed class PhoneFinding
{
    internal readonly List<PhoneFindingTime> _times = [];

    internal PhoneFinding(string check, PhoneCheckLevel level, PhoneVisit visit, string identity, string label)
    {
        Check = check;
        Level = level;
        Visit = visit;
        Identity = identity;
        Label = label;
    }

    public string Check { get; }

    public PhoneCheckLevel Level { get; }

    public PhoneVisit Visit { get; }

    public PhoneScreen Screen => Visit.Screen;

    /// <summary>The item's identity within the screen.</summary>
    public string Identity { get; }

    /// <summary>The item as first seen, as the tree view names it.</summary>
    public string Label { get; }

    /// <summary>The times it was seen, in time order.</summary>
    public IReadOnlyList<PhoneFindingTime> Times => _times;

    /// <summary>The finding as its row says it.</summary>
    public string Text
    {
        get
        {
            var first = _times[0];
            var state = first.State is null ? string.Empty : $", {first.State}";
            var others = _times.Skip(1).Any(time => time.Measured != first.Measured) ? " (other values at other times)" : string.Empty;
            var seen = _times.Count == 1
                ? $"seen once, at {WindowsPreferenceTimeline.Clock(first.Time)}"
                : string.Create(CultureInfo.CurrentCulture,
                    $"seen {_times.Count:N0} times, first at {WindowsPreferenceTimeline.Clock(first.Time)}");
            return $"{Capitalized(PhoneCheckNames.Word(Level))}: {Label}, {first.Measured}{others}{state}; {seen}";
        }
    }

    private static string Capitalized(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}

/// <summary>
/// The checks from the tree (android-checks.md, step 3), run on a phone
/// recording: each visit's settled snapshots, on the app's window, its
/// items as the latest snapshot that took them gives them. The items a
/// snapshot took are checked once in a visit however many settled snapshots
/// show them, and are one time a finding was seen, at the first of those.
/// The findings are merged in each visit by check, level and item.
/// </summary>
public sealed class PhoneCheckRun
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<PhoneScreen, List<PhoneFinding>> _byScreen = [];
    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];

    private PhoneCheckRun()
    {
    }

    public static PhoneCheckRun Empty { get; } = new();

    /// <summary>The findings, by app, screen, visit, check (in <see cref="PhoneCheckNames.All"/> order), then first time.</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>How many settled snapshots were checked, and how many distinct takings of a window's items that was.</summary>
    public int SnapshotsChecked { get; private set; }

    public int WindowsChecked { get; private set; }

    /// <summary>Settled snapshots of a time with no density recorded, so the touch target checks did not run on them.</summary>
    public int WithoutDensity { get; private set; }

    /// <summary>
    /// Settled snapshots whose items were taken after the density changed
    /// during the visit, so may have been laid out at the old density or the
    /// new; the touch target checks did not run on them.
    /// </summary>
    public int DensityChanged { get; private set; }

    /// <summary>Why a snapshot could not be checked, one for each such snapshot.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    public TimeSpan Elapsed { get; private set; }

    /// <summary>A screen's findings, from all its visits.</summary>
    public IReadOnlyList<PhoneFinding> For(PhoneScreen screen) =>
        _byScreen.TryGetValue(screen, out var list) ? list : [];

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    public static PhoneCheckRun Run(
        PhoneScreenTimeline screens,
        PhoneAccessibilityTimeline trees,
        PhoneDeviceTimeline device,
        ISessionEventRecordSource? source,
        PhoneSnapshotPictures? pictures = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screens);
        pictures ??= PhoneSnapshotPictures.Empty;
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(device);
        var run = new PhoneCheckRun();
        if (!screens.Recorded || source is null)
        {
            return run;
        }

        var stopwatch = Stopwatch.StartNew();
        var findings = new Dictionary<(PhoneVisit, string, PhoneCheckLevel, string), PhoneFinding>();
        var problems = new List<string>();
        var checkedTakings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var visit in screens.Visits)
        {
            // The results of each taking of the window's items in the visit.
            var byTaking = new Dictionary<string, (PhoneCheckWindow Window, IReadOnlyList<PhoneCheckResult> Results)?>(StringComparer.Ordinal);
            foreach (var settled in visit.SettledSnapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var time = settled.MonotonicNanoseconds;
                if (trees.TakenBy(settled, visit.WindowId) is not { } taking)
                {
                    continue;
                }

                // The density and the screen's size when the items were
                // taken, as they were laid out then: in test 9's recording a
                // snapshot 0.07 seconds after Display size was changed still
                // listed Settings' old window with the items taken before
                // the change, and a new window replaced it 0.27 seconds
                // after (measured). When the density changed during the visit
                // before the items were taken, they may have been laid out
                // at either, so the touch target checks do not run on them.
                var takenAt = taking.MonotonicNanoseconds;
                var density = device.DensityAt(takenAt);
                if (density is null)
                {
                    run.WithoutDensity++;
                }
                else if (device.DensityAt(Math.Max(visit.Start, 0)) is not { } atStart || atStart.Start != density.Start)
                {
                    density = null;
                    run.DensityChanged++;
                }

                var screen = trees.ScreenAt(takenAt);
                var key = string.Create(CultureInfo.InvariantCulture, $"{taking.EventId}|{density?.Dpi ?? 0}|{screen?.Width ?? 0}x{screen?.Height ?? 0}");
                // A settled snapshot showing the same items as an earlier one
                // of the visit is the same sight of them, not another time.
                if (byTaking.TryGetValue(key, out var seenBefore))
                {
                    if (seenBefore is not null)
                    {
                        run.SnapshotsChecked++;
                    }

                    continue;
                }

                (PhoneCheckWindow Window, IReadOnlyList<PhoneCheckResult> Results)? checkedWindow = null;
                {
                    try
                    {
                        if (ReadWindow(source, taking, visit.WindowId) is { } window)
                        {
                            var context = new PhoneCheckContext((float)(density?.PixelsPerDp ?? 0), screen?.Width, screen?.Height);
                            checkedWindow = (window, PhoneTreeChecks.Run(window, context));
                            if (checkedTakings.Add(taking.EventId))
                            {
                                run.WindowsChecked++;
                            }
                        }
                        else
                        {
                            problems.Add($"{WindowsPreferenceTimeline.Clock(taking.MonotonicNanoseconds)}: the snapshot does not hold window {visit.WindowId.ToString(CultureInfo.InvariantCulture)}");
                        }
                    }
                    catch (Exception exception) when (
                        exception is IOException or InvalidDataException or InvalidOperationException or
                            JsonException or KeyNotFoundException or UnauthorizedAccessException)
                    {
                        problems.Add($"{WindowsPreferenceTimeline.Clock(taking.MonotonicNanoseconds)}: {exception.Message}");
                    }

                    byTaking[key] = checkedWindow;
                }

                if (checkedWindow is not { } done)
                {
                    continue;
                }

                run.SnapshotsChecked++;
                var state = StateAt(visit, time);

                // Shown at the snapshot's picture, as the video can lag the
                // tree (PhoneSnapshotPictures).
                var shownAt = pictures.Of(visit, settled)?.Time ?? time;
                var items = done.Window.Items.GroupBy(item => item.Node.Index).ToDictionary(group => group.Key, group => group.First());
                foreach (var result in done.Results)
                {
                    if (!items.TryGetValue(result.Item, out var item))
                    {
                        continue;
                    }

                    var findingKey = (visit, result.Check, result.Level, item.Identity);
                    if (!findings.TryGetValue(findingKey, out var finding))
                    {
                        findings[findingKey] = finding = new PhoneFinding(result.Check, result.Level, visit, item.Identity, item.Label);
                    }

                    finding._times.Add(new PhoneFindingTime(shownAt, visit, settled, taking, visit.WindowId, item.Bounds, item.TreeKey, result.Measured, state));
                }
            }
        }

        var order = PhoneCheckNames.All.Select((check, index) => (check, index)).ToDictionary(pair => pair.check, pair => pair.index);
        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Findings = [.. findings.Values
            .OrderBy(finding => screenOrder.TryGetValue(finding.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(finding => finding.Visit.Start)
            .ThenBy(finding => order.TryGetValue(finding.Check, out var at) ? at : int.MaxValue)
            .ThenBy(finding => finding.Level)
            .ThenBy(finding => finding.Times[0].Time)];
        foreach (var finding in run.Findings)
        {
            if (!run._byScreen.TryGetValue(finding.Screen, out var list))
            {
                run._byScreen[finding.Screen] = list = [];
            }

            list.Add(finding);
            if (!run._byVisit.TryGetValue(finding.Visit, out var ofVisit))
            {
                run._byVisit[finding.Visit] = ofVisit = [];
            }

            ofVisit.Add(finding);
        }

        run.Problems = problems;
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    // The state of the screen at a time: one in the app's window, as a pane,
    // or a window over it, whose items are not checked with the app's.
    internal static string? StateAt(PhoneVisit visit, long time)
    {
        foreach (var state in visit.States)
        {
            if (state.Start <= time && time < state.End)
            {
                var named = state.NamedBy == PhoneTitleSource.Untitled ? ", untitled" : $" '{state.Name}'";
                return state.WindowId is { } window && window != visit.WindowId
                    ? $"while {state.Kind}{named} was over it"
                    : $"in {state.Kind}{named}";
            }
        }

        return null;
    }

    /// <summary>A window of a snapshot, from its complete record; null when the snapshot does not hold its items.</summary>
    internal static PhoneCheckWindow? ReadWindow(ISessionEventRecordSource source, SessionTimelineEvent snapshot, int windowId)
    {
        using var document = JsonDocument.Parse(source.ReadEventJson(snapshot));
        if (!document.RootElement.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("windows", out var windows) ||
            windows.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in windows.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.Number ||
                id.GetInt32() != windowId)
            {
                continue;
            }

            var window = entry.Deserialize<AndroidAccessibilityWindow>(JsonOptions);
            return window?.Items is { } items ? new PhoneCheckWindow(windowId, window.Type, items) : null;
        }

        return null;
    }
}
