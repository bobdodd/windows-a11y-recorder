using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>What is known of what NVDA had to announce at a reading position step.</summary>
public enum NvdaAnnouncementKind
{
    /// <summary>Worked out by NVDA's rules from the recorded tree and settings.</summary>
    Inferred,

    /// <summary>Worked out, but resting on something the recording does not show.</summary>
    Uncertain,

    /// <summary>The step's command, or NVDA's mode, is not modelled yet.</summary>
    NotModelled,

    /// <summary>What NVDA had to announce cannot be worked out from the recording.</summary>
    Unknown
}

/// <summary>
/// What NVDA had to announce at a reading position step: the kind, the
/// text as NVDA's Speech Viewer joins the parts (two spaces between), and
/// the basis.
/// </summary>
public sealed record NvdaAnnouncement(
    ReadingPositionStep Step,
    NvdaAnnouncementKind Kind,
    string Text,
    IReadOnlyList<string> Basis)
{
    public long Time => Step.Time;

    public string When => Step.When;

    /// <summary>What is known, in words, so the list does not rest on colour.</summary>
    public string State => Kind switch
    {
        NvdaAnnouncementKind.Inferred => "Inferred",
        NvdaAnnouncementKind.Uncertain => "Uncertain",
        NvdaAnnouncementKind.NotModelled => "Not modelled",
        _ => "Unknown"
    };

    /// <summary>The line under the step in the list of steps, and the row of the tab.</summary>
    public string Line => Kind switch
    {
        NvdaAnnouncementKind.Inferred => $"NVDA had to announce (inferred): {Text}",
        NvdaAnnouncementKind.Uncertain => $"NVDA had to announce (inferred, uncertain): {Text}",
        NvdaAnnouncementKind.NotModelled => $"NVDA had to announce: not modelled yet; {Text}",
        _ => $"NVDA had to announce: unknown; {Text}"
    };
}

/// <summary>
/// What NVDA had to announce at each reading position step, worked out by
/// NVDA 2026.2's own rules (speech.py's getTextInfoSpeech and
/// getControlFieldSpeech, textInfos' presentation categories, and the
/// Chromium browse mode buffer of gecko_ia2.cpp) from the page's recorded
/// accessibility tree and NVDA's recorded settings. It is what NVDA had to
/// announce, not what it said: speech that was cut off, or not heard, is
/// not known. The first scope covers Tab and the page's focus moving, quick
/// navigation by heading, link, button, landmark, frame, list, list item,
/// and graphic, and Down and Up Arrow by line, in browse mode. Agreed with
/// the owner on 2026-10-12; see docs/architecture/screen-reader-activity.md,
/// "What NVDA had to announce".
/// </summary>
public sealed partial class NvdaAnnouncementTimeline
{
    // NVDA's Speech Viewer joins the parts of a sequence with two spaces
    // (speechViewer.py, SPEECH_ITEM_SEPARATOR).
    public const string Separator = "  ";

    public const string Note =
        "What NVDA had to announce, inferred from its rules, the recorded tree, and its recorded settings; not what it said.";

    private readonly List<NvdaAnnouncement> _announcements = [];
    private readonly Dictionary<ReadingPositionStep, NvdaAnnouncement> _byStep = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, NvdaAnnouncement> _byEvent = new(StringComparer.Ordinal);

    public NvdaAnnouncementTimeline(
        ReadingPositionTimeline reading,
        AccessibilityTreeTimeline tree,
        AssistiveTechnologyTimeline assistiveTechnology,
        ScreenReaderModeTimeline mode)
        : this(
            (reading ?? throw new ArgumentNullException(nameof(reading))).Recorded ? reading.Steps : [],
            tree,
            assistiveTechnology,
            mode)
    {
    }

    // From the steps alone, for the tests.
    internal NvdaAnnouncementTimeline(
        IReadOnlyList<ReadingPositionStep> steps,
        AccessibilityTreeTimeline tree,
        AssistiveTechnologyTimeline assistiveTechnology,
        ScreenReaderModeTimeline mode)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(assistiveTechnology);
        ArgumentNullException.ThrowIfNull(mode);
        Recorded = steps.Count > 0 && tree.Recorded;
        if (!Recorded)
        {
            return;
        }

        var model = new Model(tree, assistiveTechnology, mode);
        foreach (var step in steps)
        {
            var announcement = model.Announce(step);
            _announcements.Add(announcement);
            _byStep[step] = announcement;
            foreach (var id in new[] { step.EventId, step.LinkEventId })
            {
                if (id is not null)
                {
                    _byEvent.TryAdd(id, announcement);
                }
            }
        }
    }

    public static NvdaAnnouncementTimeline Empty { get; } =
        new(ReadingPositionTimeline.Empty, AccessibilityTreeTimeline.Empty, AssistiveTechnologyTimeline.Empty, ScreenReaderModeTimeline.Empty);

    /// <summary>Whether the recording holds reading position steps and the page's tree.</summary>
    public bool Recorded { get; }

    public IReadOnlyList<NvdaAnnouncement> Announcements => _announcements;

    /// <summary>What NVDA had to announce at a step; null for a step not of this timeline.</summary>
    public NvdaAnnouncement? Of(ReadingPositionStep step) => _byStep.GetValueOrDefault(step);

    /// <summary>
    /// For a record that made a step, or the key or click its action is
    /// linked with, what NVDA had to announce and its basis, for Selected
    /// event values; null otherwise.
    /// </summary>
    public string? OfEvent(string eventId) =>
        _byEvent.TryGetValue(eventId, out var announcement)
            ? $"{announcement.Line}{Environment.NewLine}Basis: {string.Join("; ", announcement.Basis)}."
            : null;

    // NVDA's output reasons the model covers (controlTypes.OutputReason).
    internal enum Reason
    {
        Focus,
        QuickNav,
        Caret
    }

    // NVDA's roles the model covers (controlTypes/role.py); Unknown for any other.
    internal enum Role
    {
        Unknown,
        Document,
        Section,
        Paragraph,
        TextFrame,
        Landmark,
        Region,
        Heading,
        Link,
        Button,
        Graphic,
        List,
        ListItem,
        StatusBar,
        InternalFrame
    }

    private enum Category
    {
        Layout,
        SingleLine,
        Container
    }

    // A control field of NVDA's browse mode buffer for an object.
    internal sealed class Field
    {
        public required string Key { get; init; }

        public required Role Role { get; init; }

        public required string ChromiumRole { get; init; }

        public string? Landmark { get; init; }

        // The name attribute: empty where the name is the content.
        public string Name { get; init; } = string.Empty;

        public bool AlwaysReportName { get; init; }

        // The states NVDA speaks, in its order (sorted by state value):
        // visited (bit 11), then same page (bit 51).
        public IReadOnlyList<string> States { get; init; } = [];

        public string? Level { get; init; }

        public int ChildControlCount { get; set; }

        public string Description { get; init; } = string.Empty;

        public bool IsBlock { get; init; }

        public bool NameIsContent { get; init; }

        public bool NameIsExplicit { get; init; }

        public string Label { get; init; } = string.Empty;
    }

    // The buffer as a list of commands, as getTextWithFields gives them,
    // with the block boundaries that end lines.
    internal abstract record Command;

    internal sealed record Start(Field Field) : Command;

    internal sealed record End(Field Field) : Command;

    internal sealed record Text(string Value, string Key) : Command;

    internal sealed record Break : Command;

    internal sealed record Gap(string Why, string Key) : Command;

    private sealed class Model(
        AccessibilityTreeTimeline tree,
        AssistiveTechnologyTimeline assistiveTechnology,
        ScreenReaderModeTimeline mode)
    {
        // NVDA's control field stack cache for the page (SpeakTextInfoState):
        // null where it is not known, with why.
        private List<Field>? _cache;
        private string _cacheUnknown = "nothing before it in the recording shows what NVDA last read";

        // NVDA's browse mode caret, as the object whose text it is in and
        // the offset in that text: null where it is not known, with why.
        private (string Key, int Offset)? _caret;
        private string _caretUnknown = "nothing before it in the recording shows where NVDA's caret was";
        private string? _pageToken;

        public NvdaAnnouncement Announce(ReadingPositionStep step)
        {
            var reason = ReasonOf(step, out var why);
            var snapshot = tree.At(step.Time);
            if (snapshot is not null && snapshot.PageToken != _pageToken)
            {
                _pageToken = snapshot.PageToken;
                _cache = null;
                _cacheUnknown = "a page was loaded, and NVDA read it on its own";
                _caret = null;
                _caretUnknown = "a page was loaded";
            }

            if (step.Cause == "a page was loaded")
            {
                return Forget(step, NvdaAnnouncementKind.NotModelled,
                    "on a page load NVDA reads the page's title and, with say all on page load, reads on from the top",
                    "A page was loaded; what NVDA reads on a page load is not modelled yet",
                    "a page was loaded, and NVDA read it on its own");
            }

            if (reason is null)
            {
                return Forget(step, NvdaAnnouncementKind.NotModelled, why,
                    $"The step's cause, {step.Cause}, is not one the model covers yet",
                    $"{step.Cause} at {step.When} is not modelled");
            }

            if (step.Object is not { } item)
            {
                return Forget(step, NvdaAnnouncementKind.Unknown, "the step's object is not known",
                    "The reading position step does not name an object",
                    $"the object at {step.When} is not known");
            }

            var basis = new List<string>();
            var uncertain = new List<string>();
            var modeSpan = mode.Spans.LastOrDefault(span => span.Start <= step.Time && step.Time <= span.End);
            if (reason == Reason.Focus)
            {
                if (modeSpan?.Kind == ScreenReaderModeKind.Focus)
                {
                    return Forget(step, NvdaAnnouncementKind.NotModelled,
                        "in focus mode NVDA reports the focused object, which is not modelled yet",
                        "NVDA was in focus mode (inferred); it reports the focused object rather than the browse mode text",
                        "NVDA was in focus mode");
                }

                if (modeSpan?.Kind != ScreenReaderModeKind.Browse)
                {
                    uncertain.Add("NVDA's mode is not known here, so browse mode is assumed");
                }
                else
                {
                    basis.Add("NVDA was in browse mode (inferred)");
                }
            }

            if (snapshot is null || tree.KeyOf(item) is not { } key || snapshot.PathTo(key) is not { } path)
            {
                return Forget(step, NvdaAnnouncementKind.Unknown, "the object is not in the recorded tree at the step",
                    "The page's recorded tree at the step does not hold the step's object",
                    $"the object at {step.When} is not in the recorded tree");
            }

            var nvda = assistiveTechnology.ScreenReaderAt(AssistiveTechnologyRecords.Nvda.Product, step.Time);
            var settings = Settings.Read(nvda?.Settings, nvda is not null);
            if (settings.Basis.Length > 0)
            {
                basis.Add(settings.Basis);
            }

            uncertain.AddRange(settings.Uncertain);

            // The buffer, as NVDA's Chromium backend renders the tree.
            var rows = snapshot.Roots.Where(row => row.Kind != AccessibilityTreeRowKind.Unplaced && row.Kind != AccessibilityTreeRowKind.FrameTree);
            var renderer = new Renderer(tree, step.Time, settings);
            foreach (var row in rows)
            {
                renderer.Render(row);
            }

            var commands = renderer.Commands;
            var positions = PositionsOf(commands);
            var (range, from, caretAfter, rangeWhy) = RangeOf(commands, positions, path, reason.Value, step, settings, uncertain);
            if (range is null)
            {
                return Forget(step, NvdaAnnouncementKind.Unknown, rangeWhy,
                    rangeWhy[..1].ToUpperInvariant() + rangeWhy[1..],
                    $"what NVDA read at {step.When} is not known");
            }

            basis.Add(rangeWhy);
            if (range.OfType<Gap>().FirstOrDefault() is { } gap)
            {
                return Forget(step, NvdaAnnouncementKind.Unknown, gap.Why,
                    $"Part of what NVDA read is not in the recording: {gap.Why}",
                    $"part of what NVDA read at {step.When} is not in the recording");
            }

            var open = OpenAt(commands, from);
            if (open.Concat(range.OfType<Start>().Select(start => start.Field)).FirstOrDefault(field => field.Role == Role.Unknown) is { } unmodelled)
            {
                return Forget(step, NvdaAnnouncementKind.Unknown,
                    $"it is within or holds a {unmodelled.ChromiumRole} object, a role the model does not cover yet",
                    $"The {unmodelled.ChromiumRole} object ({unmodelled.Label}) has a role the model does not cover yet",
                    $"what NVDA read at {step.When} is not known");
            }

            foreach (var field in open.Concat(range.OfType<Start>().Select(start => start.Field)))
            {
                if (field.Description.Length > 0)
                {
                    uncertain.Add($"{field.Label} has a description; whether NVDA reports it depends on where it came from, which is not recorded");
                }
            }

            var read = open.Concat(range.OfType<Start>().Select(start => start.Field)).Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
            if (reason == Reason.Caret)
            {
                // Seen on the target machine on 2026-10-13 (Page Down onto a
                // link holding only an image): the Speech Viewer showed the
                // line as two, the link, then "graphic" and its name.
                var fields = open.Concat(range.OfType<Start>().Select(start => start.Field)).ToList();
                var link = fields.FindIndex(field => field.Role == Role.Link);
                if (link >= 0 && range.OfType<Start>().Any(start => start.Field.Role == Role.Graphic) &&
                    fields.FindLastIndex(field => field.Role == Role.Graphic) > link)
                {
                    uncertain.Add("the line holds a graphic within a link, which NVDA was seen to speak as a second sequence after the link, for a reason not known; the words are shown as one line");
                }
            }

            uncertain.AddRange(renderer.Uncertain.Where(note => read.Contains(note.Key)).Select(note => note.Note));
            if (_cache is null && reason != Reason.Focus && reason != Reason.QuickNav)
            {
                uncertain.Add($"what NVDA last read is not known ({_cacheUnknown}), so it may also have said what it left");
            }
            else if (_cache is null)
            {
                uncertain.Add($"what NVDA last read is not known ({_cacheUnknown}), so it may not have repeated the containers it was already in");
            }

            var (parts, stack) = Speech.Of(range, open, _cache ?? [], reason.Value, settings);
            _cache = stack;
            _caret = caretAfter;
            _caretUnknown = caretAfter is null ? $"where NVDA's caret was after {step.When} is not known" : _caretUnknown;
            var text = string.Join(Separator, parts);
            basis.Add($"NVDA's rules for {Speech.ReasonName(reason.Value)} (speech.py, getTextInfoSpeech)");
            basis.Add("click handlers are not recorded; an object with one that is not a link or button would add \"clickable\"");
            basis.AddRange(uncertain.Distinct().Select(note => "uncertain: " + note));
            return new NvdaAnnouncement(step, uncertain.Count == 0 ? NvdaAnnouncementKind.Inferred : NvdaAnnouncementKind.Uncertain, text, basis);
        }

        // A step whose announcement is not worked out: the cache is not
        // known after it.
        private NvdaAnnouncement Forget(ReadingPositionStep step, NvdaAnnouncementKind kind, string text, string basis, string cacheWhy)
        {
            _cache = null;
            _cacheUnknown = cacheWhy;
            _caret = null;
            _caretUnknown = cacheWhy;
            return new NvdaAnnouncement(step, kind, text, [basis]);
        }

        private static Reason? ReasonOf(ReadingPositionStep step, out string why)
        {
            why = string.Empty;
            var names = step.CommandNames;
            if (names.Any(name => QuickNavItem(name) is not null))
            {
                return Reason.QuickNav;
            }

            if (names.Any(name => name is "next line" or "previous line" or "next page" or "previous page"))
            {
                return Reason.Caret;
            }

            if (names.Count > 0)
            {
                why = $"{string.Join(" or ", names)} is not modelled yet";
                return null;
            }

            if (step.Rule == 2 && step.Cause == "the page's focus moved")
            {
                return Reason.Focus;
            }

            if (step.Rule == 2 && step.KeyName is { } key &&
                (key.Equals("Tab", StringComparison.OrdinalIgnoreCase) || key.Equals("shift+Tab", StringComparison.OrdinalIgnoreCase)))
            {
                return Reason.Focus;
            }

            why = step.KeyName is { } other
                ? $"{other}, passed to the page, is not modelled yet"
                : $"{step.Cause} is not modelled yet";
            return null;
        }

        // The quick navigation item a command moves to, by NVDA's command
        // names (nvda-2026.2.json), with the level for a heading at a level;
        // null for another command.
        internal static (Func<Field, bool> Is, string Name, bool ByLine)? QuickNavItem(string command)
        {
            var name = command.StartsWith("next ", StringComparison.Ordinal) ? command[5..]
                : command.StartsWith("previous ", StringComparison.Ordinal) ? command[9..]
                : null;
            if (name is null)
            {
                return null;
            }

            if (name.StartsWith("heading at level ", StringComparison.Ordinal))
            {
                var level = name["heading at level ".Length..];
                return (field => field.Role == Role.Heading && field.Level == level, name, false);
            }

            // browseMode.py: the list, landmark, and frame items are read by
            // their first line (readUnit=textInfos.UNIT_LINE).
            return name switch
            {
                "heading" => (field => field.Role == Role.Heading, name, false),
                "link" => (field => field.Role == Role.Link, name, false),
                "unvisited link" => (field => field.Role == Role.Link && !field.States.Contains("visited"), name, false),
                "visited link" => (field => field.Role == Role.Link && field.States.Contains("visited"), name, false),
                "button" => (field => field.Role == Role.Button, name, false),
                "graphic" => (field => field.Role == Role.Graphic, name, false),
                "list item" => (field => field.Role == Role.ListItem, name, false),
                "list" => (field => field.Role == Role.List, name, true),
                "landmark" => (field => field.Role is Role.Landmark or Role.Region, name, true),
                "frame" => (field => field.Role == Role.InternalFrame, name, true),
                _ => null
            };
        }

        // The commands NVDA read, with the index in the buffer of the first
        // (for the fields open there), and where its caret was left: the
        // item's field for focus and quick navigation, or the line.
        private (List<Command>? Range, int From, (string Key, int Offset)? Caret, string Why) RangeOf(
            List<Command> commands,
            int[] positions,
            IReadOnlyList<string> path,
            Reason reason,
            ReadingPositionStep step,
            Settings settings,
            List<string> uncertain)
        {
            int StartOf(string key) => commands.FindIndex(command => command is Start start && start.Field.Key == key);
            int EndOf(int start)
            {
                var depth = 0;
                for (var index = start; index < commands.Count; index++)
                {
                    if (commands[index] is Start)
                    {
                        depth++;
                    }
                    else if (commands[index] is End && --depth == 0)
                    {
                        return index;
                    }
                }

                return commands.Count - 1;
            }

            List<Command> Whole(int from, int to) => [.. commands.Skip(from).Take(to - from + 1)];

            if (reason == Reason.Focus || reason == Reason.QuickNav)
            {
                (Func<Field, bool> Is, string Name, bool ByLine)? item = reason == Reason.QuickNav
                    ? step.CommandNames.Select(QuickNavItem).FirstOrDefault(found => found is not null)
                    : null;
                for (var index = path.Count - 1; index >= 0; index--)
                {
                    var start = StartOf(path[index]);
                    if (start < 0 || commands[start] is not Start { Field: var field })
                    {
                        continue;
                    }

                    if (item is { } wanted && !wanted.Is(field))
                    {
                        continue;
                    }

                    // NVDA puts its caret at the start of the item.
                    var caret = CaretAt(commands, positions, positions[start]);
                    var end = EndOf(start);
                    if (item is { ByLine: true })
                    {
                        var first = commands.FindIndex(start, end - start + 1, command => command is Text text && !string.IsNullOrWhiteSpace(text.Value));
                        if (first >= 0)
                        {
                            // Its first line, or the first part of a line
                            // longer than NVDA's maximum.
                            var line = LineAt(commands, first);
                            var breaks = BreaksOf(commands, positions, line.From, line.To, settings.MaxLineLength);
                            var partEnd = breaks.GetViewBetween(positions[first] + 1, int.MaxValue).Min;
                            var (part, partFrom) = Part(commands, positions, start, Math.Min(end, line.To), positions[start], partEnd);
                            return (part, partFrom, caret, $"NVDA moved by quick navigation ({item.Value.Name}) to {field.Label} and read its first line (browseMode.py, TextInfoQuickNavItem.report)");
                        }
                    }

                    return reason == Reason.Focus
                        ? (Whole(start, end), start, caret, $"The focus moved to {field.Label}; NVDA read it (browseMode.py, event_gainFocus)")
                        : (Whole(start, end), start, caret, $"NVDA moved by quick navigation ({item!.Value.Name}) to {field.Label} and read it (browseMode.py, TextInfoQuickNavItem.report)");
                }

                return (null, -1, null, reason == Reason.Focus
                    ? "the object focused is not in the recorded tree as NVDA's buffer would hold it"
                    : $"the object NVDA moved to is not a {item?.Name ?? "quick navigation item"} in the recorded tree");
            }

            if (!settings.UseScreenLayout)
            {
                uncertain.Add("screen layout is off, so NVDA puts each link and control on a line of its own, which the model does not do");
            }

            // A line: from where NVDA's caret was, the next or previous line
            // (cursorManager.py, _caretMovementScriptHelper, by
            // textInfos.UNIT_LINE), where it holds the reading position's
            // object.
            var byPage = step.CommandNames.Any(name => name is "next page" or "previous page");
            var next = !step.CommandNames.Any(name => name is "previous line" or "previous page");
            var lines = byPage ? settings.LinesPerPage : 1;
            var unit = byPage ? "page" : "line";
            if (byPage && !settings.LinesPerPageRecorded)
            {
                uncertain.Add($"NVDA's lines per page was not recorded (the recording is from before it was), so its default of {settings.LinesPerPage.ToString(CultureInfo.InvariantCulture)} is assumed");
            }

            var objectKey = path[^1];
            var caretWhy = _caretUnknown;
            if (_caret is { } was && PositionOf(commands, positions, was) is { } at)
            {
                // By page, NVDA moves its lines per page, or as many as there
                // are (textInfos/offsets.py, move).
                (int From, int To, int Start, int End, int Part, int Parts)? moved = null;
                var count = 0;
                for (var position = at; count < lines; count++)
                {
                    if (MoveByLine(commands, positions, position, next, settings.MaxLineLength) is not { } one)
                    {
                        break;
                    }

                    moved = one;
                    position = one.Start;
                }

                if (byPage && moved is null)
                {
                    moved = MoveByLine(commands, positions, at, !next, settings.MaxLineLength) is { } back
                        ? MoveByLine(commands, positions, back.Start, next, settings.MaxLineLength)
                        : null;
                }

                if (moved is { } landed)
                {
                    var movedWhy = byPage
                        ? $"NVDA moved by {unit} {count.ToString(CultureInfo.InvariantCulture)} lines from where its caret was{(count < lines ? $", as many as there were of its {lines.ToString(CultureInfo.InvariantCulture)} lines per page" : string.Empty)}, and read the line"
                        : "NVDA moved by line from where its caret was and read the line";
                    var (part, partFrom) = Part(commands, positions, landed.From, landed.To, landed.Start, landed.End);
                    var holds = part.Any(command => command is Text text && text.Key == objectKey || command is Start start && start.Field.Key == objectKey) ||
                        OpenAt(commands, partFrom).Any(field => field.Key == objectKey);
                    if (holds)
                    {
                        var parts = landed.Parts > 1
                            ? $", part {landed.Part.ToString(CultureInfo.InvariantCulture)} of {landed.Parts.ToString(CultureInfo.InvariantCulture)} of a line longer than NVDA's maximum line length of {settings.MaxLineLength.ToString(CultureInfo.InvariantCulture)}, split as NVDA splits it (vbufBase/storage.cpp, getLineOffsets)"
                            : string.Empty;
                        return (part, partFrom, CaretAt(commands, positions, landed.Start),
                            $"{movedWhy} with {DescribeText(part)}{parts} (cursorManager.py, _caretMovementScriptHelper)");
                    }

                    caretWhy = $"the line {(byPage ? $"{count.ToString(CultureInfo.InvariantCulture)} lines" : "after")} {(next ? "on" : "back")} from where NVDA's caret was does not hold the reading position's object";
                }
                else
                {
                    caretWhy = $"there is no {(next ? "next" : "previous")} line from where NVDA's caret was";
                }
            }

            // Where the caret was is not known, or does not agree with the
            // reading position: the reading position object's line.
            var found = -1;
            for (var index = path.Count - 1; index >= 0 && found < 0; index--)
            {
                var key = path[index];
                found = commands.FindIndex(command => command is Text text && text.Key == key && !string.IsNullOrWhiteSpace(text.Value));
                if (found < 0 && StartOf(key) is var start and >= 0)
                {
                    var end = EndOf(start);
                    found = commands.FindIndex(start, end - start + 1, command => command is Text text && !string.IsNullOrWhiteSpace(text.Value));
                }
            }

            if (found < 0)
            {
                return (null, -1, null, "the line NVDA moved to has no text in the recorded tree");
            }

            var (from, to) = LineAt(commands, found);
            var whole = Whole(from, to);
            if (byPage && commands.Select((command, index) => (command, index))
                .Any(entry => entry.command is Text text && text.Key == objectKey && !string.IsNullOrWhiteSpace(text.Value) && (entry.index < from || entry.index > to)))
            {
                uncertain.Add($"the reading position's object has more than one line, and which NVDA moved to is not known, as {caretWhy}, so its first is shown");
            }

            var lineBreaks = BreaksOf(commands, positions, from, to, settings.MaxLineLength);
            if (lineBreaks.Count > 2)
            {
                var length = lineBreaks.Max - lineBreaks.Min;
                uncertain.Add($"the line is {length.ToString(CultureInfo.InvariantCulture)} characters, more than NVDA's maximum line length of {settings.MaxLineLength.ToString(CultureInfo.InvariantCulture)}, so NVDA splits it into {(lineBreaks.Count - 1).ToString(CultureInfo.InvariantCulture)} parts; which it read is not known, as {caretWhy}, so the whole line is shown");
                return (whole, from, null, $"NVDA moved by {unit} and read part of the line with {DescribeText(whole)} (cursorManager.py, _caretMovementScriptHelper)");
            }

            if (_caret is not null)
            {
                uncertain.Add($"{caretWhy}, so the line is the reading position's");
            }

            return (whole, from, CaretAt(commands, positions, lineBreaks.Min),
                $"NVDA moved by {unit} and read the line with {DescribeText(whole)} (cursorManager.py, _caretMovementScriptHelper)");
        }

        // The offset in the buffer's text of each command: where a text
        // starts, or where a field starts or ends.
        private static int[] PositionsOf(List<Command> commands)
        {
            var positions = new int[commands.Count];
            var offset = 0;
            for (var index = 0; index < commands.Count; index++)
            {
                positions[index] = offset;
                if (commands[index] is Text text)
                {
                    offset += text.Value.Length;
                }
            }

            return positions;
        }

        // Where a line ends: after its last command.
        private static int EndOfLine(List<Command> commands, int[] positions, int to) =>
            positions[to] + (commands[to] is Text text ? text.Value.Length : 0);

        // Where NVDA splits a line longer than its maximum line length
        // (vbufBase/storage.cpp, getLineOffsets): the line's start and end,
        // and, each time the count reaches the maximum, the last possible
        // break at or before it, a node's start or end or a word's start,
        // where one is within the maximum, else there.
        internal static SortedSet<int> BreaksOf(List<Command> commands, int[] positions, int from, int to, int maxLineLength)
        {
            var lineStart = positions[from];
            var lineEnd = EndOfLine(commands, positions, to);
            var real = new SortedSet<int> { lineStart, lineEnd };
            if (maxLineLength <= 0)
            {
                return real;
            }

            var possible = new SortedSet<int>();
            for (var index = from; index <= to; index++)
            {
                possible.Add(positions[index]);
                if (commands[index] is Text text)
                {
                    possible.Add(positions[index] + text.Value.Length);
                    for (var offset = 1; offset < text.Value.Length; offset++)
                    {
                        if (!char.IsWhiteSpace(text.Value[offset]) && char.IsWhiteSpace(text.Value[offset - 1]))
                        {
                            possible.Add(positions[index] + offset);
                        }
                    }
                }
            }

            for (int offset = lineStart, count = 0; offset < lineEnd; ++offset, ++count)
            {
                if (count == maxLineLength)
                {
                    var before = possible.GetViewBetween(int.MinValue, offset);
                    if (before.Count > 0 && before.Max > offset - maxLineLength)
                    {
                        offset = before.Max;
                    }

                    real.Add(offset);
                    count = 0;
                }
            }

            return real;
        }

        // The line, or line part, NVDA's caret moves to from an offset by
        // next or previous line: the commands of its line, its offsets, and
        // which part of how many it is; null where there is none.
        private static (int From, int To, int Start, int End, int Part, int Parts)? MoveByLine(
            List<Command> commands, int[] positions, int at, bool next, int maxLineLength)
        {
            var holding = commands.FindIndex(command => command is Text);
            for (var index = 0; index < commands.Count; index++)
            {
                if (commands[index] is Text text && positions[index] <= at && at < positions[index] + text.Value.Length)
                {
                    holding = index;
                    break;
                }
            }

            if (holding < 0)
            {
                return null;
            }

            var (from, to) = LineAt(commands, holding);
            var breaks = BreaksOf(commands, positions, from, to, maxLineLength);
            var partStart = breaks.GetViewBetween(int.MinValue, at).Max;
            var partEnd = breaks.GetViewBetween(at + 1, int.MaxValue).Min;
            (int, int, int, int, int, int) PartOf(int lineFrom, int lineTo, SortedSet<int> lineBreaks, int start)
            {
                var list = lineBreaks.ToList();
                var number = list.IndexOf(start);
                return (lineFrom, lineTo, start, list[number + 1], number + 1, list.Count - 1);
            }

            if (next)
            {
                if (partEnd < breaks.Max)
                {
                    return PartOf(from, to, breaks, partEnd);
                }

                var following = commands.FindIndex(to + 1, command => command is Text text && text.Value.Length > 0);
                if (following < 0)
                {
                    return null;
                }

                var line = LineAt(commands, following);
                var lineBreaks = BreaksOf(commands, positions, line.From, line.To, maxLineLength);
                return PartOf(line.From, line.To, lineBreaks, lineBreaks.Min);
            }

            if (partStart > breaks.Min)
            {
                return PartOf(from, to, breaks, breaks.GetViewBetween(int.MinValue, partStart - 1).Max);
            }

            var preceding = commands.FindLastIndex(from - 1 < 0 ? 0 : from - 1, command => command is Text text && text.Value.Length > 0);
            if (from == 0 || preceding < 0)
            {
                return null;
            }

            var previous = LineAt(commands, preceding);
            var previousBreaks = BreaksOf(commands, positions, previous.From, previous.To, maxLineLength);
            return PartOf(previous.From, previous.To, previousBreaks, previousBreaks.GetViewBetween(int.MinValue, previousBreaks.Max - 1).Max);
        }

        // The commands of a line from one offset to another, a text cut
        // where they fall within it, with the index of the first; a field
        // ending is kept where it is open.
        private static (List<Command> Part, int From) Part(List<Command> commands, int[] positions, int from, int to, int start, int end)
        {
            var part = new List<Command>();
            var first = -1;
            var open = new List<Field>();
            for (var index = from; index <= to; index++)
            {
                var at = positions[index];
                var command = commands[index];
                if (first < 0)
                {
                    var begins = command switch
                    {
                        Text text => at < end && at + text.Value.Length > start,
                        Start => at >= start && at < end,
                        _ => false
                    };
                    if (!begins)
                    {
                        continue;
                    }

                    first = index;
                    open = OpenAt(commands, index);
                }

                switch (command)
                {
                    case Text text when at < end && at + text.Value.Length > start:
                        var cutStart = Math.Max(start, at) - at;
                        var cutEnd = Math.Min(end, at + text.Value.Length) - at;
                        part.Add(text with { Value = text.Value[cutStart..cutEnd] });
                        break;
                    case Start begin when at < end:
                        part.Add(begin);
                        open.Add(begin.Field);
                        break;
                    case End close when at <= end && open.Count > 0 && ReferenceEquals(open[^1], close.Field):
                        part.Add(close);
                        open.RemoveAt(open.Count - 1);
                        break;
                    case Break or Gap when at >= start && at < end:
                        part.Add(command);
                        break;
                }
            }

            return (part, first < 0 ? from : first);
        }

        // The buffer offset of a caret, by its object's text.
        private static int? PositionOf(List<Command> commands, int[] positions, (string Key, int Offset) caret)
        {
            var seen = 0;
            for (var index = 0; index < commands.Count; index++)
            {
                if (commands[index] is Text text && text.Key == caret.Key)
                {
                    if (caret.Offset < seen + text.Value.Length)
                    {
                        return positions[index] + caret.Offset - seen;
                    }

                    seen += text.Value.Length;
                }
            }

            return null;
        }

        // The caret at a buffer offset, by the text there or the first after.
        private static (string Key, int Offset)? CaretAt(List<Command> commands, int[] positions, int at)
        {
            for (var index = 0; index < commands.Count; index++)
            {
                if (commands[index] is Text text && text.Value.Length > 0 && at < positions[index] + text.Value.Length)
                {
                    var offset = Math.Max(0, at - positions[index]);
                    for (var before = 0; before < index; before++)
                    {
                        if (commands[before] is Text earlier && earlier.Key == text.Key)
                        {
                            offset += earlier.Value.Length;
                        }
                    }

                    return (text.Key, offset);
                }
            }

            return null;
        }

        // The line around a command: from after the block boundary before
        // it to before the one after it.
        private static (int From, int To) LineAt(List<Command> commands, int at)
        {
            var from = at;
            while (from > 0 && commands[from - 1] is not Break)
            {
                from--;
            }

            var to = at;
            while (to < commands.Count - 1 && commands[to + 1] is not Break)
            {
                to++;
            }

            return (from, to);
        }

        private static string DescribeText(List<Command> commands)
        {
            var text = Collapse(string.Concat(commands.OfType<Text>().Select(item => item.Value)));
            return text.Length > 40 ? $"\"{text[..40]}...\"" : $"\"{text}\"";
        }

        // The fields open where a range starts.
        private static List<Field> OpenAt(List<Command> commands, int index)
        {
            var open = new List<Field>();
            for (var at = 0; at < index; at++)
            {
                switch (commands[at])
                {
                    case Start start:
                        open.Add(start.Field);
                        break;
                    case End when open.Count > 0:
                        open.RemoveAt(open.Count - 1);
                        break;
                }
            }

            return open;
        }
    }

    // utils/urlUtils.py, isSamePageURL.
    internal static bool IsSamePage(string? target, string? root)
    {
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(root))
        {
            return false;
        }

        static (string? Scheme, string Remainder, string Fragment) Split(string url)
        {
            var hash = url.IndexOf('#', StringComparison.Ordinal);
            var fragment = hash < 0 ? string.Empty : url[(hash + 1)..];
            var withoutFragment = hash < 0 ? url : url[..hash];
            var colon = withoutFragment.IndexOf(':', StringComparison.Ordinal);
            return colon <= 0 ? (null, withoutFragment, fragment) : (withoutFragment[..colon].ToLowerInvariant(), withoutFragment[(colon + 1)..], fragment);
        }

        var (targetScheme, targetRest, targetFragment) = Split(target);
        var (rootScheme, rootRest, _) = Split(root);
        string[] valid = ["http", "https", "file"];
        return targetScheme is not null && valid.Contains(targetScheme) &&
            rootScheme is not null && valid.Contains(rootScheme) &&
            targetRest == rootRest && !targetFragment.Contains('/', StringComparison.Ordinal);
    }

    internal static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    // NVDA's settings that decide what it reports, from its settings record.
    internal sealed class Settings
    {
        private readonly Dictionary<string, bool> _flags = [];

        public int MaxLineLength { get; private set; } = 100;

        // [virtualBuffers] linesPerPage: how many lines Page Down and Page
        // Up move (cursorManager.py, script_moveByPage_forward).
        public int LinesPerPage { get; private set; } = 25;

        public bool LinesPerPageRecorded { get; private set; }

        public bool UseScreenLayout => Flag("useScreenLayout");

        public string Basis { get; private set; } = string.Empty;

        public List<string> Uncertain { get; } = [];

        public bool Flag(string key) => _flags.GetValueOrDefault(key);

        // The formatting settings the model leaves out, with the value at
        // which NVDA reports nothing more.
        private static readonly (string Key, string Off)[] Formatting =
        [
            ("detectFormatAfterCursor", "false"), ("reportFontName", "false"), ("reportFontSize", "false"),
            ("fontAttributeReporting", "0"), ("reportEmphasis", "false"), ("reportSuperscriptsAndSubscripts", "false"),
            ("reportColor", "false"), ("reportAlignment", "false"), ("reportLineSpacing", "false"),
            ("reportStyle", "false"), ("reportLineNumber", "false"), ("reportLineIndentation", "0"),
            ("reportParagraphIndentation", "false")
        ];

        public static Settings Read(JsonElement? record, bool running)
        {
            var settings = new Settings();
            string? why = null;
            if (!running)
            {
                why = "NVDA was not running at the step, as recorded";
            }
            else if (record is not { ValueKind: JsonValueKind.Object } payload)
            {
                why = "NVDA's settings were not recorded";
            }
            else if (!payload.TryGetProperty("read", out var read) || read.ValueKind != JsonValueKind.True)
            {
                why = "NVDA's settings could not be read";
            }
            else if (!payload.TryGetProperty("reportLinks", out _))
            {
                why = "NVDA's reporting settings were not recorded (the recording is from before they were)";
            }

            var written = new List<string>();
            foreach (var (_, key, defaultValue) in NvdaSettings.ReportingSettings)
            {
                var value = why is null && record is { } item && item.TryGetProperty(key, out var member) && member.ValueKind == JsonValueKind.String
                    ? member.GetString()
                    : null;
                if (key == "linesPerPage")
                {
                    settings.LinesPerPage = int.TryParse(value ?? defaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lines) && lines > 0 ? lines : 25;
                    settings.LinesPerPageRecorded = why is null && record is { } source && source.TryGetProperty(key, out _);
                    if (value is not null && value != defaultValue)
                    {
                        written.Add($"{key} {value}");
                    }

                    continue;
                }

                if (key == "maxLineLength")
                {
                    settings.MaxLineLength = int.TryParse(value ?? defaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ? length : 100;
                    if (value is not null && value != defaultValue)
                    {
                        written.Add($"{key} {value}");
                    }

                    continue;
                }

                if (defaultValue is "true" or "false")
                {
                    var flag = NvdaSettings.Boolean(value, defaultValue == "true");
                    if (flag is null)
                    {
                        settings.Uncertain.Add($"NVDA's {key} is written \"{value}\", which NVDA does not read as on or off; its default is assumed");
                        flag = defaultValue == "true";
                    }
                    else if (value is not null && flag != (defaultValue == "true"))
                    {
                        written.Add($"{key} {(flag.Value ? "on" : "off")}");
                    }

                    settings._flags[key] = flag.Value;
                }
                else if (value is not null && value != defaultValue)
                {
                    written.Add($"{key} {value}");
                }

                if (Formatting.FirstOrDefault(entry => entry.Key == key) is { Key: not null } formatting &&
                    (value ?? defaultValue).Trim().ToLowerInvariant() is var current && current != formatting.Off && current is not ("off" or "no"))
                {
                    settings.Uncertain.Add($"NVDA also reports formatting ({key} is on), which the model leaves out");
                }
            }

            if (why is not null)
            {
                settings.Uncertain.Add($"{why}, so NVDA's defaults are assumed");
            }
            else
            {
                settings.Basis = written.Count == 0
                    ? "NVDA's recorded settings, all at NVDA's defaults for what the model uses"
                    : $"NVDA's recorded settings, with {string.Join(", ", written)}";
                if (record is { } payload2 && payload2.TryGetProperty("profiles", out var profiles) &&
                    profiles.ValueKind == JsonValueKind.Array && profiles.GetArrayLength() > 0)
                {
                    settings.Uncertain.Add("NVDA has configuration profiles, which may change these settings and are not read");
                }
            }

            return settings;
        }
    }

    // NVDA's Chromium browse mode buffer, from the recorded tree
    // (nvdaHelper/vbufBackends/gecko_ia2/gecko_ia2.cpp).
    private sealed class Renderer(AccessibilityTreeTimeline tree, long time, Settings settings)
    {
        private string? _documentUrl;

        public List<Command> Commands { get; } = [];

        // What is uncertain of an object's field, by its key.
        public List<(string Key, string Note)> Uncertain { get; } = [];

        public void Render(AccessibilityTreeRow row) => Render(row, null);

        private void Render(AccessibilityTreeRow row, Field? parent)
        {
            switch (row.Kind)
            {
                case AccessibilityTreeRowKind.NotRecorded:
                    Commands.Add(new Gap($"an object, {row.Label}", row.Key));
                    return;
                case AccessibilityTreeRowKind.FrameNote:
                    Commands.Add(new Gap($"the frame's document: {row.Label}", row.Key));
                    return;
                case AccessibilityTreeRowKind.Object:
                    break;
                default:
                    return;
            }

            if (row.DocumentToken is not { } token || row.NodeId is not { } id || tree.NodeAt(token, id, time) is not { } node)
            {
                Commands.Add(new Gap("an object not in the recording", row.Key));
                return;
            }

            var properties = Properties.Of(node);
            if (node.Role == "inlineTextBox")
            {
                return;
            }

            if (properties.States.Contains("IGNORED") || node.Role == "none")
            {
                foreach (var child in row.Children)
                {
                    Render(child, parent);
                }

                return;
            }

            if (node.Role is "staticText" or "listMarker")
            {
                Commands.Add(new Text(node.Name, row.Key));
                return;
            }

            var field = FieldOf(row.Key, node, properties);
            if (parent is { Role: Role.List } && field.Role == Role.ListItem)
            {
                parent.ChildControlCount++;
            }

            if (field.IsBlock)
            {
                Commands.Add(new Break());
            }

            Commands.Add(new Start(field));
            var before = Commands.Count;
            if (field.NameIsContent && field.NameIsExplicit)
            {
                // An explicit name is the content, and the children are not
                // rendered.
                if (node.Name.Length > 0)
                {
                    Commands.Add(new Text(node.Name, row.Key));
                }
            }
            else
            {
                foreach (var child in row.Children)
                {
                    Render(child, field);
                }

                // With no useful content, a name that can be the content is.
                if (field.NameIsContent && node.Name.Length > 0 &&
                    !Commands.Skip(before).OfType<Text>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
                {
                    Commands.Add(new Text(node.Name, row.Key));
                }
            }

            Commands.Add(new End(field));
            if (field.IsBlock)
            {
                Commands.Add(new Break());
            }
        }

        private Field FieldOf(string key, AccessibilityRequests.TreeNode node, Properties properties)
        {
            var landmark = properties.AriaRole is "banner" or "complementary" or "contentinfo" or "main" or "navigation" or "search" or "form"
                ? properties.AriaRole
                : null;
            var role = node.Role switch
            {
                "rootWebArea" => Role.Document,
                "genericContainer" or "section" or "sectionWithoutName" or "sectionHeader" or "sectionFooter" => Role.Section,
                // Chromium's roles for a page's header and footer are banner
                // and contentInfo (ax_enums.mojom); header and footer elsewhere.
                "header" or "footer" or "banner" or "contentInfo" or "navigation" or "main" or "complementary" or "search" =>
                    landmark is null ? Role.Section : Role.Landmark,
                "form" when landmark is not null => Role.Landmark,
                "region" => Role.Region,
                "strong" or "emphasis" => Role.TextFrame,
                "paragraph" => Role.Paragraph,
                "heading" => Role.Heading,
                // A link that cannot be activated is a named destination,
                // which NVDA makes a text frame (gecko_ia2.py).
                "link" => properties.States.Contains("LINKED") ? Role.Link : Role.TextFrame,
                "button" => Role.Button,
                "image" => Role.Graphic,
                "list" => Role.List,
                "listItem" => Role.ListItem,
                "iframe" => Role.InternalFrame,
                "status" => Role.StatusBar,
                _ => Role.Unknown
            };
            if (role == Role.Document && _documentUrl is null)
            {
                _documentUrl = properties.Url;
            }

            // Chromium's explicit-name: a name not from the contents.
            var nameIsExplicit = properties.NameFrom is not (null or "" or "contents" or "none" or "prohibited" or "attributeExplicitlyEmpty");
            var nameIsContent = role is Role.Link or Role.Button or Role.Graphic or Role.Heading;
            var named = node.Name.Length > 0;
            var labelVisible = false;
            if (nameIsExplicit && named && !nameIsContent && properties.NameFrom == "relatedElement" && role != Role.Document)
            {
                // A label in another element may be visible, when NVDA does
                // not always report the name.
                labelVisible = true;
                Uncertain.Add((key, $"whether the label of {Label(node)} is visible is not recorded; it is taken as visible"));
            }

            var states = new List<string>();
            if (role == Role.Link)
            {
                if (properties.States.Contains("VISITED"))
                {
                    states.Add("visited");
                }

                if (settings.Flag("reportLinkType") && IsSamePage(properties.Url, _documentUrl))
                {
                    states.Add("same page");
                }
            }

            foreach (var state in properties.States.Except(QuietStates))
            {
                Uncertain.Add((key, $"{Label(node)} is {state}, a state the model does not cover"));
            }

            if (properties.HasRoleDescription)
            {
                Uncertain.Add((key, $"{Label(node)} has a role description, which the model does not cover"));
            }

            string? level = null;
            if (role == Role.Heading)
            {
                level = properties.Level ?? (properties.Tag is { Length: 2 } tag && tag[0] == 'h' && tag[1] is >= '1' and <= '6' ? tag[1..] : null);
                if (properties.Level is null && level is not null)
                {
                    Uncertain.Add((key, "a heading's level is taken from its tag, as Chromium did not send it"));
                }
            }

            return new Field
            {
                Key = key,
                Role = role,
                ChromiumRole = node.Role,
                Landmark = role == Role.Landmark ? landmark : null,
                Name = !nameIsContent && named ? node.Name : string.Empty,
                AlwaysReportName = nameIsExplicit && named && !nameIsContent && !labelVisible,
                States = states,
                Level = level,
                Description = node.Description,
                // gecko_ia2.cpp: the display attribute decides; Chromium's
                // serialization leaves out display=block.
                IsBlock = properties.Display is not ("inline" or "inline-block" or "inline-flex"),
                NameIsContent = nameIsContent,
                NameIsExplicit = nameIsExplicit,
                Label = Label(node)
            };
        }

        private static string Label(AccessibilityRequests.TreeNode node) =>
            node.Name.Length > 0 ? $"the {node.Role} \"{node.Name}\"" : $"a {node.Role}";

        // States NVDA does not speak in browse mode for these roles, or
        // that do not reach it as states.
        private static readonly HashSet<string> QuietStates =
            ["FOCUSABLE", "LINKED", "IGNORED", "INVISIBLE", "HOVERED", "VISITED", "HORIZONTAL", "VERTICAL", "FOCUSED", "OFFSCREEN"];
    }

    // What the model reads of Chromium's serialization of an object.
    private sealed record Properties(
        HashSet<string> States,
        string? Tag,
        string? Display,
        string? AriaRole,
        string? NameFrom,
        string? Url,
        string? Level,
        bool HasRoleDescription)
    {
        public static Properties Of(AccessibilityRequests.TreeNode node)
        {
            // The name and description hold spaces and may hold anything,
            // so they are taken out before the rest is read.
            var text = " " + node.Properties + " ";
            if (node.Name.Length > 0)
            {
                text = text.Replace(" name=" + node.Name + " ", " ", StringComparison.Ordinal);
            }

            if (node.Description.Length > 0)
            {
                text = text.Replace(" description=" + node.Description + " ", " ", StringComparison.Ordinal);
            }

            string? Value(string key) =>
                Regex.Match(text, $@"\s{key}=(\S+)", RegexOptions.CultureInvariant) is { Success: true } match ? match.Groups[1].Value : null;
            var tag = Regex.Match(text, @"\s<([^\s>.#]+)", RegexOptions.CultureInvariant) is { Success: true } found ? found.Groups[1].Value : null;
            var states = Regex.Matches(text, @"(?<=\s)[A-Z][A-Z_]+(?=\s)", RegexOptions.CultureInvariant)
                .Select(match => match.Value)
                .ToHashSet(StringComparer.Ordinal);
            return new Properties(
                states,
                tag,
                Value("display"),
                Value("aria_role"),
                Value("name_from"),
                Value("url"),
                Value("level"),
                text.Contains(" role_description=", StringComparison.Ordinal));
        }
    }

    // speech.py's getTextInfoSpeech and getControlFieldSpeech, for the
    // fields the model covers.
    internal static class Speech
    {
        public static string ReasonName(Reason reason) => reason switch
        {
            Reason.Focus => "a focus change",
            Reason.QuickNav => "quick navigation",
            _ => "moving by line"
        };

        public static (List<string> Parts, List<Field> Stack) Of(
            List<Command> range,
            List<Field> open,
            List<Field> cache,
            Reason reason,
            Settings settings)
        {
            // The leading field starts join the fields open where the range
            // starts; the trailing ends are dropped.
            var commands = range.Where(command => command is not Break).ToList();
            var stack = new List<Field>(open);
            var lead = 0;
            while (lead < commands.Count && commands[lead] is Start start)
            {
                stack.Add(start.Field);
                lead++;
            }

            var trail = commands.Count;
            while (trail > lead && commands[trail - 1] is End)
            {
                trail--;
            }

            var body = commands.Skip(lead).Take(trail - lead).ToList();
            var common = 0;
            while (common < Math.Min(stack.Count, cache.Count) && stack[common].Key == cache[common].Key)
            {
                common++;
            }

            var parts = new List<string>();
            var blank = true;
            if (reason != Reason.Focus && reason != Reason.QuickNav)
            {
                for (var index = cache.Count - 1; index >= common; index--)
                {
                    parts.AddRange(FieldSpeech(cache[index], "end_removedFromControlFieldStack", reason, settings));
                }
            }

            for (var index = 0; index < common; index++)
            {
                var said = FieldSpeech(stack[index], "start_inControlFieldStack", reason, settings);
                parts.AddRange(said);
                blank &= said.Count == 0;
            }

            for (var index = common; index < stack.Count; index++)
            {
                var said = FieldSpeech(stack[index], "start_addedToControlFieldStack", reason, settings);
                parts.AddRange(said);
                blank &= said.Count == 0;
                common++;
            }

            var relative = new List<string>();
            var inText = false;
            foreach (var command in body)
            {
                switch (command)
                {
                    case Text text when text.Value.Length > 0:
                        if (inText)
                        {
                            relative[^1] += text.Value;
                        }
                        else
                        {
                            relative.Add(text.Value);
                            inText = true;
                        }

                        break;
                    case Start start:
                        inText = false;
                        relative.AddRange(FieldSpeech(start.Field, "start_relative", reason, settings));
                        stack.Add(start.Field);
                        break;
                    case End when stack.Count > 0:
                        inText = false;
                        relative.AddRange(FieldSpeech(stack[^1], "end_relative", reason, settings));
                        stack.RemoveAt(stack.Count - 1);
                        common = Math.Min(common, stack.Count);
                        break;
                }
            }

            if (relative.Any(item => !string.IsNullOrWhiteSpace(item)))
            {
                parts.AddRange(relative);
                blank = false;
            }

            for (var index = Math.Min(stack.Count, common) - 1; index >= 0; index--)
            {
                var said = FieldSpeech(stack[index], "end_inControlFieldStack", reason, settings);
                parts.AddRange(said);
                blank &= said.Count == 0;
            }

            if (blank)
            {
                parts.Add("blank");
            }

            // As the Speech Viewer shows them: each part with its spaces
            // collapsed, and parts of only spaces left out.
            return ([.. parts.Select(Collapse).Where(part => part.Length > 0)], stack);
        }

        private static Category CategoryOf(Field field, Settings settings)
        {
            var layout =
                (field.Role == Role.Link && !settings.Flag("reportLinks")) ||
                (field.Role == Role.Graphic && !settings.Flag("reportGraphics")) ||
                (field.Role == Role.Heading && !settings.Flag("reportHeadings")) ||
                (field.Role is Role.List or Role.ListItem && !settings.Flag("reportLists")) ||
                (field.Role == Role.InternalFrame && !settings.Flag("reportFrames")) ||
                ((field.Role == Role.Landmark || field.Landmark is not null) && !settings.Flag("reportLandmarks")) ||
                (field.Role == Role.Region && (field.Name.Length == 0 || !settings.Flag("reportLandmarks")));
            if (layout)
            {
                return Category.Layout;
            }

            return field.Role switch
            {
                Role.Link or Role.Heading or Role.Button or Role.Graphic => Category.SingleLine,
                // Chromium's lists are read only, so containers.
                Role.Region or Role.InternalFrame or Role.List or Role.Landmark => Category.Container,
                _ => Category.Layout
            };
        }

        private static string RoleText(Field field) => field.Role switch
        {
            Role.Landmark when field.Landmark is { } landmark => $"{LandmarkLabel(landmark)} landmark",
            Role.Landmark => "landmark",
            Role.Region => "region",
            Role.Heading => "heading",
            Role.Link => "link",
            Role.Button => "button",
            Role.Graphic => "graphic",
            Role.List => "list",
            Role.ListItem => "list item",
            Role.InternalFrame => "frame",
            Role.Document => "document",
            Role.Section => "section",
            Role.Paragraph => "paragraph",
            Role.TextFrame => "text frame",
            Role.StatusBar => "status bar",
            _ => string.Empty
        };

        // aria.py, landmarkRoles.
        private static string LandmarkLabel(string landmark) => landmark switch
        {
            "contentinfo" => "content info",
            _ => landmark
        };

        internal static List<string> FieldSpeech(Field field, string fieldType, Reason reason, Settings settings)
        {
            var category = CategoryOf(field, settings);
            var name = reason is Reason.Focus or Reason.QuickNav || field.AlwaysReportName ? field.Name : string.Empty;
            var description = settings.Flag("reportObjectDescriptions") && reason is Reason.Focus or Reason.QuickNav && field.Description.Length > 0
                ? field.Description
                : null;
            var role = RoleText(field);
            var states = field.States;
            var level = field.Level is { } value ? $"level {value}" : null;
            var speakEntry = category != Category.Layout;
            var speakWithinForLine = category == Category.SingleLine;
            var speakExitForLine = category == Category.Container && field.Role != Role.Landmark;
            var contentFirst = reason is Reason.Focus or Reason.QuickNav &&
                category != Category.Container &&
                field.Role is not (Role.List or Role.Landmark or Role.Region);
            var statesFirst = field.Role == Role.Link;
            var contains = field.ChildControlCount > 0 && fieldType == "start_addedToControlFieldStack" && field.Role == Role.List
                ? field.ChildControlCount == 1 ? "with 1 item" : $"with {field.ChildControlCount.ToString(CultureInfo.InvariantCulture)} items"
                : string.Empty;

            if (name.Length > 0 && reason is Reason.Focus or Reason.QuickNav && fieldType == "start_addedToControlFieldStack" &&
                field.Role is Role.Landmark or Role.Region)
            {
                return settings.Flag("reportLandmarks") ? [name, role] : [name];
            }

            if ((speakEntry &&
                 ((contentFirst && fieldType is "end_relative" or "end_inControlFieldStack") ||
                  (!contentFirst && fieldType is "start_addedToControlFieldStack" or "start_relative"))) ||
                (speakWithinForLine && !contentFirst && fieldType == "start_inControlFieldStack"))
            {
                var output = new List<string>();
                if (name.Length > 0)
                {
                    output.Add(name);
                }

                if (statesFirst)
                {
                    output.AddRange(states);
                    output.Add(role);
                }
                else
                {
                    output.Add(role);
                    output.AddRange(states);
                }

                output.Add(contains);
                if (description is not null)
                {
                    output.Add(description);
                }

                if (level is not null)
                {
                    output.Add(level);
                }

                return [.. output.Where(item => item.Length > 0)];
            }

            if (fieldType is "end_removedFromControlFieldStack" or "end_relative" && role.Length > 0 && speakExitForLine)
            {
                return [$"out of {role}"];
            }

            return [];
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
