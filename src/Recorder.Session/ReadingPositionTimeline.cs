using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>What is known of the reading position from a step on.</summary>
public enum ReadingPositionKind
{
    /// <summary>Nothing recorded shows where NVDA is reading.</summary>
    Unknown,

    /// <summary>An object, inferred from a request or a focus change.</summary>
    Known,

    /// <summary>The object of the step before, again; where within it is unknown.</summary>
    SameObject,

    /// <summary>An object, but a key since may have moved NVDA without a trace.</summary>
    Uncertain
}

/// <summary>
/// A step of the reading position: its time and record, what is known, the
/// rule that gave it, the object where there is one, the row's value, a
/// short label for the band, and the basis.
/// </summary>
public sealed record ReadingPositionStep(
    long Time,
    string? EventId,
    ReadingPositionKind Kind,
    int Rule,
    TreeObject? Object,
    string Value,
    string Label,
    string Basis)
{
    /// <summary>The record of the key or click the step's action is linked with, where it has one.</summary>
    public string? LinkEventId { get; init; }

    /// <summary>What moved it, for the list of steps: the key and its command, the click, the page's focus, or a page load.</summary>
    public string Cause { get; init; } = string.Empty;

    /// <summary>The step's record on the timeline, where playback has it, so a step can select it.</summary>
    public SessionTimelineEvent? Event { get; init; }

    /// <summary>The step's time as the player's clock gives it.</summary>
    public string When => WindowsPreferenceTimeline.Clock(Time);

    /// <summary>What is known, in words, so the list does not rest on colour.</summary>
    public string State => Kind switch
    {
        ReadingPositionKind.Known => "Known",
        ReadingPositionKind.SameObject => "Same object",
        ReadingPositionKind.Uncertain => "Uncertain",
        _ => "Unknown"
    };
}

/// <summary>A page loaded in the tab's main frame: its time and its record.</summary>
public sealed record PageLoad(long Time, string? EventId);

/// <summary>A span of the reading position band: a known or uncertain object from a step to the next.</summary>
public sealed record ReadingPositionSpan(ReadingPositionKind Kind, long Start, long End, string Label);

/// <summary>A page's focus moving: its record, time, document, and DOM node.</summary>
public sealed record PageFocusChange(string EventId, long Time, string DocumentToken, long DomNode);

/// <summary>
/// The screen reader's reading position, inferred at playback: the object
/// NVDA's browse mode caret was last on, from the requests made of Chromium
/// (AccessibilityRequests), the page's focus, the commands NVDA kept, the
/// windows in front, and the pages loaded, by the rules of
/// docs/architecture/screen-reader-activity.md, "The reading position (step
/// 4)" and "Decisions after the target run".
/// </summary>
public sealed class ReadingPositionTimeline
{
    public const int RuleVersion = 1;

    public const string Key = "screen-reader.reading-position";

    public const string Setting = "Reading position (inferred)";

    private const long ChangedWindowNanoseconds = 1_000_000_000;

    private readonly List<ReadingPositionStep> _steps = [];
    private readonly List<long> _times = [];
    private readonly Dictionary<string, ReadingPositionStep> _byEvent = new(StringComparer.Ordinal);
    private readonly long _duration;

    private static readonly HashSet<string> Moves = new(StringComparer.Ordinal)
    {
        "start of line", "end of line", "top of document", "bottom of document", "find next", "find previous"
    };

    /// <param name="requests">The requests made of the page, with the keys and clicks they are linked with.</param>
    /// <param name="kept">The keys NVDA kept, with its commands.</param>
    /// <param name="foreground">The windows brought to the front.</param>
    /// <param name="browserProcessName">The browser's process name, for the windows in front.</param>
    /// <param name="focus">The page's focus changes, of page documents only.</param>
    /// <param name="pageLoads">The pages loaded in the main frame, browser UI pages left out.</param>
    /// <param name="duration">The recording's length, for the band's last span.</param>
    /// <param name="events">The timeline events of the records, by record, so each step can select its record.</param>
    public ReadingPositionTimeline(
        AccessibilityRequests requests,
        IReadOnlyList<KeptCommand> kept,
        IReadOnlyList<ForegroundWindow> foreground,
        string? browserProcessName,
        IEnumerable<PageFocusChange> focus,
        IEnumerable<PageLoad> pageLoads,
        long duration,
        IReadOnlyDictionary<string, SessionTimelineEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(pageLoads);
        _duration = duration;
        Recorded = requests.Actions.Count > 0;
        var linkedKeys = requests.Actions
            .Where(action => action.Action is "scrollToMakeVisible" or "focus" && action.LinkTime is not null && action.Click is null)
            .Select(action => action.LinkTime!.Value)
            .ToHashSet();

        // The inputs in time order; at one time, a page load first, then a
        // key, then an action, then a focus change.
        var inputs = new List<(long Time, int Order, object Item)>();
        inputs.AddRange(pageLoads.Select(load => (load.Time, 0, (object)load)));
        inputs.AddRange(kept.Select(command => (command.Time, 1, (object)command)));
        inputs.AddRange(requests.Actions
            .Where(action => action.Action is "scrollToMakeVisible" or "focus")
            .Select(action => (action.Time, 2, (object)action)));
        inputs.AddRange(focus.Select(change => (change.Time, 3, (object)change)));
        var windows = foreground.OrderBy(window => window.Time).ToList();

        ReadingPositionStep? current = null;
        foreach (var (time, _, item) in inputs.OrderBy(input => input.Time).ThenBy(input => input.Order))
        {
            ReadingPositionStep? step = item switch
            {
                PageLoad load => Unknown(time, load.EventId, 5, $"unknown: a page was loaded at {Clock(time)}",
                    "A page was loaded in the tab, and nothing yet shows where NVDA is reading in it") with { Cause = "a page was loaded" },
                KeptCommand command => KeptStep(command, current, linkedKeys, windows, browserProcessName, requests),
                RequestedAction action => ActionStep(action, current, requests),
                PageFocusChange change => FocusStep(change, current, requests),
                _ => null
            };
            // An unknown position made unknown again by the same rule changes
            // nothing, and makes no step.
            if (step is null ||
                step.Kind == ReadingPositionKind.Unknown && current is { Kind: ReadingPositionKind.Unknown } &&
                current.Rule == step.Rule && step.Rule is 6 or 8)
            {
                continue;
            }

            if (events is not null && step.EventId is { } recorded && events.TryGetValue(recorded, out var timelineEvent))
            {
                step = step with { Event = timelineEvent };
            }

            current = step;
            _steps.Add(step);
            _times.Add(step.Time);
            if (step.EventId is { } eventId)
            {
                _byEvent.TryAdd(eventId, step);
            }

            if (step.LinkEventId is { } link)
            {
                _byEvent.TryAdd(link, step);
            }
        }

        Spans = BuildSpans();
    }

    public static ReadingPositionTimeline Empty { get; } = new(
        AccessibilityRequests.Empty, [], [], null, [], [], 0);

    /// <summary>The step at a time: the last at or before it; -1 before the first.</summary>
    public int IndexAt(long time)
    {
        var index = _times.BinarySearch(time);
        if (index < 0)
        {
            return ~index - 1;
        }

        while (index + 1 < _times.Count && _times[index + 1] == time)
        {
            index++;
        }

        return index;
    }

    /// <summary>Whether the recording holds requests made of the page, which the reading position rests on.</summary>
    public bool Recorded { get; }

    public string Rule => $"rule {RuleVersion.ToString(CultureInfo.InvariantCulture)}";

    public IReadOnlyList<ReadingPositionStep> Steps => _steps;

    /// <summary>The band's spans: known and uncertain objects; where the position is unknown, none.</summary>
    public IReadOnlyList<ReadingPositionSpan> Spans { get; }

    /// <summary>The reading position row at a time, in the Screen reader group; none for a recording without requests.</summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time)
    {
        if (!Recorded)
        {
            return [];
        }

        var index = _times.BinarySearch(time);
        if (index < 0)
        {
            index = ~index - 1;
        }
        else
        {
            // The last step at the time.
            while (index + 1 < _times.Count && _times[index + 1] == time)
            {
                index++;
            }
        }

        if (index < 0)
        {
            return
            [
                new PropertyRow(AssistiveTechnologyTimeline.Group, Setting, "unknown: nothing yet shows where NVDA is reading", "at start", null, false, Key)
            ];
        }

        var step = _steps[index];
        return
        [
            new PropertyRow(
                AssistiveTechnologyTimeline.Group,
                Setting,
                step.Value,
                WindowsPreferenceTimeline.Clock(step.Time),
                step.Time,
                time - step.Time < ChangedWindowNanoseconds,
                Key)
        ];
    }

    /// <summary>The times of the steps, for stepping; null for another key or a recording without requests.</summary>
    public IReadOnlyList<long>? ChangeTimesOf(string key) =>
        key == Key && Recorded && _times.Count > 0 ? _times.Distinct().ToList() : null;

    /// <summary>
    /// For a record that made a step, or the key or click its action is
    /// linked with, the reading position after it and its basis, for
    /// Selected event values; null otherwise.
    /// </summary>
    public string? Of(string eventId) =>
        _byEvent.TryGetValue(eventId, out var step)
            ? $"Reading position after it (inferred, rule {step.Rule.ToString(CultureInfo.InvariantCulture)} of step 4, {Rule}): " +
              $"{step.Value}.{Environment.NewLine}Basis: {step.Basis}."
            : null;

    // Rules 4, 6, 7, and 8: a key NVDA kept. One whose command sent a request
    // or focus change makes no step of its own; the action does.
    private static ReadingPositionStep? KeptStep(
        KeptCommand command,
        ReadingPositionStep? current,
        HashSet<long> linkedKeys,
        List<ForegroundWindow> windows,
        string? browserProcessName,
        AccessibilityRequests requests)
    {
        if (linkedKeys.Contains(command.Time))
        {
            return null;
        }

        // Rule 7: a key while another window than the browser is in front.
        if (browserProcessName is not null && InFront(windows, command.Time) is { ProcessName: { } process } &&
            !string.Equals(process, browserProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = string.Join(" or ", command.Names);
        var eventId = requests.KeyEventAt(command.Time);
        var said = $"{name} ({command.Gesture}) at {Clock(command.Time)}";
        var cause = $"{command.Gesture} ({name})";
        if (command.Names.Any(IsSayAll))
        {
            return Unknown(command.Time, eventId, 6, $"unknown: {said} moves the caret as NVDA reads, and sent nothing",
                $"NVDA kept {said}, which reads from the caret, moving it as it goes; nothing was sent to the page as it read") with { Cause = cause };
        }

        if (command.Names.Any(IsSelection))
        {
            return Unknown(command.Time, eventId, 8, $"unknown: {said} moved the caret with the selection, and sent nothing",
                $"NVDA kept {said}, which moves the caret as it selects; nothing was sent to the page") with { Cause = cause };
        }

        if (!command.Names.Any(IsMove) || current is not { Object: { } item })
        {
            return null;
        }

        // Rule 4: a move that sent nothing leaves the object, uncertain.
        return new ReadingPositionStep(
            command.Time,
            eventId,
            ReadingPositionKind.Uncertain,
            4,
            item,
            $"{requests.DescribeObject(item, command.Time)}, uncertain since {said} sent nothing",
            requests.LabelOf(item, command.Time),
            $"NVDA kept {said} and sent nothing to the page in the " +
            $"{(AccessibilityRequests.KeyWindowNanoseconds / 1_000_000).ToString(CultureInfo.InvariantCulture)} ms after it; " +
            "it may not have moved, as at the last of its kind, or may have moved without a trace")
        {
            Cause = cause
        };
    }

    // Rules 1, 3, and 9: a scroll request; rule 2: a focus request.
    private static ReadingPositionStep? ActionStep(RequestedAction action, ReadingPositionStep? current, AccessibilityRequests requests)
    {
        if (action.TreeId is not { } treeId || action.NodeId is not { } nodeId)
        {
            return null;
        }

        var item = new TreeObject(treeId, nodeId, action.Role, action.Name);
        var described = requests.DescribeObject(item, action.Time);
        var label = requests.LabelOf(item, action.Time);
        var after = LinkText(action);
        if (action.Action == "focus")
        {
            return new ReadingPositionStep(action.Time, action.EventId, ReadingPositionKind.Known, 2, item,
                $"{described}, focused", label, $"A request to focus it{after}; NVDA's caret follows the focus")
            {
                LinkEventId = action.LinkEventId,
                Cause = CauseOf(action)
            };
        }

        if (action.Command is { } command && command.Names.Any(name => name is "next page" or "previous page"))
        {
            return new ReadingPositionStep(action.Time, action.EventId, ReadingPositionKind.Uncertain, 9, item,
                $"{described}, uncertain: {string.Join(" or ", command.Names)} moves by NVDA's lines per page, and the request may name where the caret started",
                label,
                $"A request to scroll it into view{after}. NVDA moves Page Down and Page Up by its lines per page, 25 by default, " +
                "but in the target run of 2026-10-12 both Page Downs sent a request on the paragraph the caret started on")
            {
                LinkEventId = action.LinkEventId,
                Cause = CauseOf(action)
            };
        }

        if (current is { Object: { } before } && before.TreeId == treeId && before.NodeId == nodeId)
        {
            return new ReadingPositionStep(action.Time, action.EventId, ReadingPositionKind.SameObject, 3, item,
                $"{described}; where within it is unknown", label,
                $"A request to scroll it into view again{after}; the request names the whole object, not the line, word, or character")
            {
                LinkEventId = action.LinkEventId,
                Cause = CauseOf(action)
            };
        }

        return new ReadingPositionStep(action.Time, action.EventId, ReadingPositionKind.Known, 1, item, described, label,
            $"A request to scroll it into view{after}")
        {
            LinkEventId = action.LinkEventId,
            Cause = CauseOf(action)
        };
    }

    // Rule 2: the page's focus moving. The same object as the step before
    // makes no step, as after a focus request.
    private static ReadingPositionStep? FocusStep(PageFocusChange change, ReadingPositionStep? current, AccessibilityRequests requests)
    {
        if (requests.ObjectOfDomNode(change.DocumentToken, change.DomNode, change.Time) is not { } item)
        {
            return new ReadingPositionStep(change.Time, change.EventId, ReadingPositionKind.Unknown, 2, null,
                $"unknown: the page's focus moved to DOM node {change.DomNode.ToString(CultureInfo.InvariantCulture)}, which the recorded tree does not name",
                string.Empty,
                "The page's focus moved; NVDA's caret follows the focus, but the object is not in the recorded accessibility tree")
            {
                Cause = "the page's focus moved"
            };
        }

        if (current is { Object: { } before, Kind: ReadingPositionKind.Known or ReadingPositionKind.SameObject } &&
            before.TreeId == item.TreeId && before.NodeId == item.NodeId)
        {
            return null;
        }

        return new ReadingPositionStep(change.Time, change.EventId, ReadingPositionKind.Known, 2, item,
            $"{requests.DescribeObject(item, change.Time)}, focused",
            requests.LabelOf(item, change.Time),
            "The page's focus moved to it; NVDA's caret follows the focus, in browse mode as in focus mode")
        {
            Cause = "the page's focus moved"
        };
    }

    private static ReadingPositionStep Unknown(long time, string? eventId, int rule, string value, string basis) =>
        new(time, eventId, ReadingPositionKind.Unknown, rule, null, value, string.Empty, basis);

    // What moved it, for the list of steps.
    private static string CauseOf(RequestedAction action) =>
        action.Command is { } command
            ? $"{command.Gesture} ({string.Join(" or ", command.Names)})"
            : action.Click ?? (action.KeyName is { } key ? $"{key}, no NVDA command" : "no key or click");

    private static string LinkText(RequestedAction action)
    {
        if (action.LinkTime is not { } at)
        {
            return $", with no key or click in the {(AccessibilityRequests.KeyWindowNanoseconds / 1_000_000).ToString(CultureInfo.InvariantCulture)} ms before it";
        }

        var milliseconds = ((action.Time - at) / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture);
        return action.Click is { } click
            ? $", {milliseconds} ms after {click}"
            : action.Command is { } command
                ? $", {milliseconds} ms after NVDA kept {string.Join(" or ", command.Names)} ({command.Gesture})"
                : $", {milliseconds} ms after a key";
    }

    private static ForegroundWindow? InFront(List<ForegroundWindow> windows, long time)
    {
        ForegroundWindow? found = null;
        foreach (var window in windows)
        {
            if (window.Time > time)
            {
                break;
            }

            found = window;
        }

        return found;
    }

    private static bool IsSayAll(string name) => name == "say all" || name.StartsWith("say all ", StringComparison.Ordinal);

    private static bool IsSelection(string name) => name.StartsWith("select ", StringComparison.Ordinal);

    private static bool IsMove(string name) =>
        Moves.Contains(name) ||
        name.StartsWith("next ", StringComparison.Ordinal) ||
        name.StartsWith("previous ", StringComparison.Ordinal) ||
        name.StartsWith("move ", StringComparison.Ordinal);

    private List<ReadingPositionSpan> BuildSpans()
    {
        var spans = new List<ReadingPositionSpan>();
        for (var index = 0; index < _steps.Count; index++)
        {
            var step = _steps[index];
            if (step.Kind == ReadingPositionKind.Unknown)
            {
                continue;
            }

            var end = index + 1 < _steps.Count ? _steps[index + 1].Time : Math.Max(_duration, step.Time);
            spans.Add(new ReadingPositionSpan(
                step.Kind == ReadingPositionKind.Uncertain ? ReadingPositionKind.Uncertain : ReadingPositionKind.Known,
                step.Time,
                end,
                step.Label));
        }

        return spans;
    }

    private static string Clock(long time) => WindowsPreferenceTimeline.Clock(time);

    /// <summary>Whether playback keeps a record for the page's focus, which the reading position reads.</summary>
    public static bool IsFocusEvidence(string channel, string eventType) =>
        channel == BrowserEvidenceChannels.Interaction && eventType == BrowserEvidenceEventTypes.FocusChanged;

    /// <summary>A page focus record's change: the focused DOM node of a page document; null for another record.</summary>
    public static PageFocusChange? FocusOf(string eventId, long time, JsonElement payload, IReadOnlySet<string> pageDocuments)
    {
        ArgumentNullException.ThrowIfNull(pageDocuments);
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("outcome", out var outcome) || outcome.ValueKind != JsonValueKind.String || outcome.GetString() != "focused" ||
            !payload.TryGetProperty("focusedNodeId", out var node) || node.ValueKind != JsonValueKind.Number || !node.TryGetInt64(out var domNode) ||
            !payload.TryGetProperty("context", out var context) || context.ValueKind != JsonValueKind.Object ||
            !context.TryGetProperty("documentToken", out var token) || token.ValueKind != JsonValueKind.String ||
            token.GetString() is not { } documentToken || !pageDocuments.Contains(documentToken))
        {
            return null;
        }

        return new PageFocusChange(eventId, time, documentToken, domNode);
    }

    /// <summary>Whether a document loaded at a URL is a page, not the browser's own interface (chrome:).</summary>
    public static bool IsPageUrl(string? url) =>
        url is not null && !url.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) &&
        !url.StartsWith("chrome-untrusted:", StringComparison.OrdinalIgnoreCase) &&
        !url.StartsWith("devtools:", StringComparison.OrdinalIgnoreCase);
}
