namespace Recorder.Session;

public enum ScreenReaderModeKind
{
    Browse,
    Focus,
    Changed
}

/// <summary>
/// A span of NVDA's inferred mode: browse or focus mode from its first
/// evidence to its last, or the span in which the mode changed between two
/// evidences that differ with no switch key between; with the basis.
/// </summary>
public sealed record ScreenReaderModeSpan(
    ScreenReaderModeKind Kind,
    long Start,
    long End,
    string StartBasis,
    string EndBasis);

/// <summary>A window brought to the front: its time and process name.</summary>
public sealed record ForegroundWindow(long Time, string? ProcessName);

/// <summary>
/// NVDA's browse or focus mode, inferred from the keys it kept or passed
/// while the instrumented browser was the front window, as spans for the
/// timeline's band and the "NVDA mode" row of the Screen reader group.
/// Where the recording does not show the mode, it is unknown. Agreed with
/// the owner on 2026-10-11; see docs/architecture/screen-reader-activity.md,
/// "2c-2, browse or focus mode".
/// </summary>
public sealed class ScreenReaderModeTimeline
{
    public const string Key = "screen-reader-mode.NVDA";
    public const string Setting = "NVDA mode";
    private const long ChangedWindowNanoseconds = 1_000_000_000;

    private readonly List<ScreenReaderModeSpan> _spans = [];

    // What ended an inference, for the unknown row: a page load, NVDA
    // starting or exiting.
    private readonly List<(long Time, string Reason)> _breaks = [];

    public ScreenReaderModeTimeline(
        IEnumerable<ModeEvidence> evidence,
        IEnumerable<ForegroundWindow> foreground,
        string? browserProcessName,
        IEnumerable<long> pageLoads,
        IEnumerable<(long Time, string Reason)> runs)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(pageLoads);
        ArgumentNullException.ThrowIfNull(runs);
        var windows = foreground.OrderBy(window => window.Time).ToList();
        bool BrowserInFront(long time)
        {
            var window = windows.LastOrDefault(item => item.Time <= time);
            return browserProcessName is not null && window is not null &&
                string.Equals(window.ProcessName, browserProcessName, StringComparison.OrdinalIgnoreCase);
        }

        _breaks.AddRange(pageLoads.Select(time => (time, "a page loaded")));
        _breaks.AddRange(runs);
        _breaks.Sort((left, right) => left.Time.CompareTo(right.Time));

        // The evidence and breaks in order; a break at the same time as a
        // key comes first.
        var items = evidence.Where(item => BrowserInFront(item.Time))
            .Select(item => (item.Time, Order: 1, Evidence: (ModeEvidence?)item))
            .Concat(_breaks.Select(item => (item.Time, Order: 0, Evidence: (ModeEvidence?)null)))
            .OrderBy(item => item.Time)
            .ThenBy(item => item.Order)
            .ToList();
        Recorded = browserProcessName is not null;

        (ScreenReaderModeKind Kind, long Start, string StartBasis, long Last, string LastBasis)? current = null;
        (ScreenReaderModeKind Kind, long Last, string LastBasis)? previous = null;
        void Close()
        {
            if (current is { } span)
            {
                _spans.Add(new ScreenReaderModeSpan(span.Kind, span.Start, span.Last, span.StartBasis, span.LastBasis));
                previous = (span.Kind, span.Last, span.LastBasis);
            }

            current = null;
        }

        foreach (var (time, _, item) in items)
        {
            if (item is null)
            {
                Close();
                previous = null;
                continue;
            }

            var basis = $"{item.Basis} at {WindowsPreferenceTimeline.Clock(time)}";
            switch (item.Kind)
            {
                case ModeEvidenceKind.Toggle:
                    var before = current?.Kind;
                    if (current is { } toggled)
                    {
                        current = toggled with { Last = time, LastBasis = basis };
                    }

                    Close();
                    previous = null;
                    if (before is { } kind)
                    {
                        var after = kind == ScreenReaderModeKind.Browse ? ScreenReaderModeKind.Focus : ScreenReaderModeKind.Browse;
                        current = (after, time, basis, time, basis);
                    }

                    break;
                case ModeEvidenceKind.ExitFocus:
                    if (current is { } exited)
                    {
                        current = exited with { Last = time, LastBasis = basis };
                    }

                    Close();
                    previous = null;
                    current = (ScreenReaderModeKind.Browse, time, basis, time, basis);
                    break;
                default:
                    var mode = item.Kind == ModeEvidenceKind.Browse ? ScreenReaderModeKind.Browse : ScreenReaderModeKind.Focus;
                    if (current is { } same && same.Kind == mode)
                    {
                        current = same with { Last = time, LastBasis = basis };
                        break;
                    }

                    Close();
                    if (previous is { } last && last.Kind != mode)
                    {
                        _spans.Add(new ScreenReaderModeSpan(ScreenReaderModeKind.Changed, last.Last, time, last.LastBasis, basis));
                    }

                    current = (mode, time, basis, time, basis);
                    break;
            }
        }

        Close();
        _spans.Sort((left, right) => left.Start != right.Start
            ? left.Start.CompareTo(right.Start)
            : left.Kind == ScreenReaderModeKind.Changed ? -1 : 1);
    }

    public static ScreenReaderModeTimeline Empty { get; } = new([], [], null, [], []);

    /// <summary>Whether the recording watched the browser, so the mode could be inferred.</summary>
    public bool Recorded { get; }

    /// <summary>The spans, in order.</summary>
    public IReadOnlyList<ScreenReaderModeSpan> Spans => _spans;

    public static string Name(ScreenReaderModeKind kind) => kind switch
    {
        ScreenReaderModeKind.Browse => "browse mode",
        ScreenReaderModeKind.Focus => "focus mode",
        _ => "changed"
    };

    /// <summary>The row's value at a time.</summary>
    public string ValueAt(long time)
    {
        // A one-key span covers its instant; the changed span its interval.
        var span = _spans.LastOrDefault(item => item.Start <= time && time <= item.End && item.Kind != ScreenReaderModeKind.Changed)
            ?? _spans.LastOrDefault(item => item.Start <= time && time <= item.End);
        if (span is not null)
        {
            if (span.Kind == ScreenReaderModeKind.Changed)
            {
                var from = _spans.LastOrDefault(item => item.Kind != ScreenReaderModeKind.Changed && item.End <= span.Start);
                var to = _spans.FirstOrDefault(item => item.Kind != ScreenReaderModeKind.Changed && item.Start >= span.End);
                return $"changed between {WindowsPreferenceTimeline.Clock(span.Start)} and {WindowsPreferenceTimeline.Clock(span.End)}" +
                    (from is not null && to is not null ? $", from {Name(from.Kind)} to {Name(to.Kind)}" : string.Empty) +
                    $", inferred; last {span.StartBasis}, then {span.EndBasis}";
            }

            return $"{Name(span.Kind)}, inferred from {span.StartBasis}" +
                (span.End > span.Start ? $" to {span.EndBasis}" : string.Empty);
        }

        var before = _spans.LastOrDefault(item => item.End < time && item.Kind != ScreenReaderModeKind.Changed);
        var broken = _breaks.LastOrDefault(item => item.Time <= time && (before is null || item.Time >= before.End));
        if (broken != default)
        {
            return $"unknown; {broken.Reason} at {WindowsPreferenceTimeline.Clock(broken.Time)}";
        }

        return before is null
            ? "unknown"
            : $"unknown; last seen {Name(before.Kind)}, {before.EndBasis}";
    }

    /// <summary>The times the row's value changes: each span's start, and the end of each span not followed by another at once.</summary>
    public IReadOnlyList<long> ChangeTimes()
    {
        var times = new SortedSet<long>();
        foreach (var span in _spans)
        {
            times.Add(span.Start);
            if (!_spans.Any(next => next != span && next.Start == span.End))
            {
                times.Add(span.End + 1);
            }
        }

        return [.. times];
    }

    /// <summary>The panel row at a time, in the Screen reader group; none where the browser was not watched.</summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time)
    {
        if (!Recorded)
        {
            return [];
        }

        var changes = ChangeTimes();
        long? setAt = changes.LastOrDefault(change => change <= time) is var at && at > 0 ? at : null;
        return
        [
            new PropertyRow(
                AssistiveTechnologyTimeline.Group,
                Setting,
                ValueAt(time),
                setAt is { } changed ? WindowsPreferenceTimeline.Clock(changed) : "at start",
                setAt,
                setAt is { } recent && time - recent < ChangedWindowNanoseconds,
                Key)
        ];
    }

    /// <summary>The times the row's value changed, for stepping; null for another key.</summary>
    public IReadOnlyList<long>? ChangeTimesOf(string key) => Recorded && key == Key ? ChangeTimes() : null;
}
