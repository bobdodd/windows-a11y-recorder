using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// A span in which a window of NVDA's main process was the front window:
/// its name, from its title, and how it came to the front and left it.
/// </summary>
public sealed record ScreenReaderWindowSpan(
    string Name,
    long Start,
    long End,
    string EndBasis,
    string? Opener,
    int ProcessId)
{
    /// <summary>
    /// The elements focused in the window while it was in front, in order,
    /// the same element focused again at once given once; each is a step of
    /// the NVDA window row.
    /// </summary>
    public IReadOnlyList<FocusedElement> Focus { get; init; } = [];
}

/// <summary>An element given the focus, as UI Automation recorded it.</summary>
public sealed record FocusedElement(long Time, int? ProcessId, string? ControlType, string? Name);

/// <summary>
/// NVDA's own windows in front, such as the Elements List, the NVDA menu,
/// and the Exit NVDA dialog, as spans for the timeline's strip and the
/// "NVDA window" row of the Screen reader group, with the element focused
/// in each. A span ends when another window comes to the front: the window
/// "left the front"; whether it was closed is not recorded. Agreed with the
/// owner on 2026-10-11; see docs/architecture/screen-reader-activity.md,
/// "2c-3, NVDA's windows".
/// </summary>
public sealed class ScreenReaderWindowTimeline
{
    public const string Key = "screen-reader-window.NVDA";
    public const string Setting = "NVDA window";
    public const string MainFrameTitle = "NVDA";
    private const long ChangedWindowNanoseconds = 1_000_000_000;

    // A kept key's command opens the window that comes to the front within
    // this time after it.
    public const long OpenerWindowNanoseconds = 1_000_000_000;

    // The window each command opens, by the command data's names.
    private static readonly Dictionary<string, string> Opens = new(StringComparer.Ordinal)
    {
        ["elements list"] = "Elements List",
        ["NVDA menu"] = "NVDA menu",
        ["quit NVDA"] = "Exit NVDA"
    };

    private readonly List<ScreenReaderWindowSpan> _spans = [];
    private readonly List<FocusedElement> _focus;

    public ScreenReaderWindowTimeline(
        IEnumerable<ForegroundWindow> foreground,
        IEnumerable<FocusedElement> focus,
        AssistiveTechnologyTimeline assistiveTechnology,
        IEnumerable<KeptCommand> commands,
        long duration)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(assistiveTechnology);
        ArgumentNullException.ThrowIfNull(commands);
        Recorded = assistiveTechnology.Recorded;
        _focus = [.. focus.OrderBy(item => item.Time)];
        var kept = commands.OrderBy(item => item.Time).ToList();
        var windows = foreground.OrderBy(window => window.Time).ToList();
        int? NvdaAt(long time) => assistiveTechnology.ScreenReaderAt(AssistiveTechnologyRecords.Nvda.Product, time)?.ProcessId;
        bool IsNvda(ForegroundWindow window) =>
            window.ProcessId is { } id && NvdaAt(window.Time) == id;

        var exits = assistiveTechnology.ScreenReaderRuns(AssistiveTechnologyRecords.Nvda.Product)
            .Where(run => run.Reason.EndsWith("exited", StringComparison.Ordinal))
            .Select(run => run.Time)
            .ToList();

        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            if (!IsNvda(window))
            {
                continue;
            }

            // NVDA's hidden main frame, titled "NVDA", comes to the front for
            // a moment before each of its dialogs and menus; it is folded
            // into the window that follows it.
            var start = window.Time;
            while (window.Title == MainFrameTitle && index + 1 < windows.Count &&
                   IsNvda(windows[index + 1]) && windows[index + 1].ProcessId == window.ProcessId)
            {
                window = windows[++index];
            }

            var next = index + 1 < windows.Count ? windows[index + 1] : null;
            var exit = exits.Where(time => time > window.Time).Select(time => (long?)time).FirstOrDefault();
            long end;
            string endBasis;
            if (exit is { } exited && (next is null || exited < next.Time))
            {
                end = exited;
                endBasis = $"NVDA exited at {WindowsPreferenceTimeline.Clock(exited)}";
            }
            else if (next is not null)
            {
                end = next.Time;
                endBasis = $"left the front at {WindowsPreferenceTimeline.Clock(next.Time)}, when " +
                    (string.IsNullOrWhiteSpace(next.Title)
                        ? $"a window of {next.ProcessName ?? "another process"}"
                        : $"\"{next.Title}\" ({next.ProcessName ?? "another process"})") +
                    " came to the front";
            }
            else
            {
                end = Math.Max(duration, window.Time);
                endBasis = "in front until the recording ended";
            }

            var name = NameOf(window, end);
            var opener = kept.LastOrDefault(command =>
                command.Time <= start && start - command.Time <= OpenerWindowNanoseconds &&
                command.Names.Any(item => Opens.TryGetValue(item, out var opened) && opened == name));
            _spans.Add(new ScreenReaderWindowSpan(
                name,
                start,
                end,
                endBasis,
                opener is null
                    ? null
                    : $"opened by {opener.Gesture} ({string.Join(" or ", opener.Names)}) at {WindowsPreferenceTimeline.Clock(opener.Time)}, inferred",
                window.ProcessId!.Value)
            {
                Focus = FocusIn(window.ProcessId!.Value, start, end)
            });
        }
    }

    public static ScreenReaderWindowTimeline Empty { get; } =
        new([], [], AssistiveTechnologyTimeline.Empty, [], 0);

    /// <summary>Whether the recording holds the assistive technology records.</summary>
    public bool Recorded { get; }

    /// <summary>The spans, in order.</summary>
    public IReadOnlyList<ScreenReaderWindowSpan> Spans => _spans;

    // The elements focused in a window, UI Automation's repeat of the same
    // element at once (as at 93.009 s and 93.016 s in recording
    // 20261010-211432) given once.
    private List<FocusedElement> FocusIn(int processId, long start, long end)
    {
        var focus = new List<FocusedElement>();
        foreach (var item in _focus.Where(item => item.ProcessId == processId && item.Time >= start && item.Time < end))
        {
            if (focus.Count > 0 && focus[^1].ControlType == item.ControlType && focus[^1].Name == item.Name)
            {
                continue;
            }

            focus.Add(item);
        }

        return focus;
    }

    // A window by its title; an untitled one by what was focused in it.
    private string NameOf(ForegroundWindow window, long end)
    {
        if (!string.IsNullOrWhiteSpace(window.Title))
        {
            return window.Title;
        }

        var menu = _focus.Any(item => item.Time >= window.Time && item.Time < end &&
            item.ProcessId == window.ProcessId &&
            item.ControlType is "ControlType.Menu" or "ControlType.MenuItem");
        return menu ? "NVDA menu" : "NVDA window (untitled)";
    }

    private static string ControlName(string? controlType)
    {
        var name = controlType is { } type && type.StartsWith("ControlType.", StringComparison.Ordinal)
            ? type["ControlType.".Length..]
            : controlType ?? "element";
        // TreeItem to "tree item", MenuItem to "menu item".
        var words = new System.Text.StringBuilder();
        foreach (var character in name)
        {
            if (char.IsUpper(character) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(char.ToLowerInvariant(character));
        }

        return words.ToString();
    }

    /// <summary>The row's value at a time.</summary>
    public string ValueAt(long time)
    {
        var span = _spans.LastOrDefault(item => item.Start <= time && time < item.End);
        if (span is null)
        {
            var before = _spans.LastOrDefault(item => item.End <= time);
            return before is null
                ? "none in front"
                : $"none in front; {before.Name} {before.EndBasis}";
        }

        var text = $"{span.Name}, in front from {WindowsPreferenceTimeline.Clock(span.Start)}";
        if (span.Opener is { } opener)
        {
            text += $", {opener}";
        }

        var focused = span.Focus.LastOrDefault(item => item.Time <= time);
        if (focused is not null)
        {
            text += $"; focus: {ControlName(focused.ControlType)}" +
                (string.IsNullOrEmpty(focused.Name) ? string.Empty : $" \"{focused.Name}\"") +
                $" at {WindowsPreferenceTimeline.Clock(focused.Time)}";
        }

        return text;
    }

    /// <summary>
    /// The times the row's value changes: each span's start and end, and
    /// each element focused in the window between, so the row's buttons step
    /// through what was done in the window (the owner, 2026-10-11: "I can't
    /// step through the actions when an NVDA window is open").
    /// </summary>
    public IReadOnlyList<long> ChangeTimes()
    {
        var times = new SortedSet<long>();
        foreach (var span in _spans)
        {
            times.Add(span.Start);
            times.Add(span.End);
            foreach (var item in span.Focus)
            {
                times.Add(item.Time);
            }
        }

        return [.. times];
    }

    /// <summary>The panel row at a time, in the Screen reader group; none where NVDA was not watched.</summary>
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
