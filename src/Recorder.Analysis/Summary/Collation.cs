using System.Globalization;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Summary;

/// <summary>A recorded fact with a time, and the record it rests on where there is one.</summary>
public sealed record TimedFact(long Time, string? EventId, string Text);

/// <summary>A window coming to the front: its process and title, as recorded.</summary>
public sealed record ForegroundFact(long Time, string? EventId, string? Process, string? Title);

/// <summary>The page's focus moving to a node of a document.</summary>
public sealed record FocusFact(long Time, string? EventId, string DocumentToken, long? NodeId);

/// <summary>
/// What the summary reads from a recording. The recorded implementation
/// reads a recording file; the tests give their own.
/// </summary>
public interface ISummaryEvidence
{
    long Duration { get; }

    IReadOnlyList<NavigationCommit> Commits { get; }

    /// <summary>The page of a document as recorded at a time; null where its DOM was not recorded.</summary>
    PageSnapshot? PageAt(string documentToken, string url, long time);

    /// <summary>The focused node and text controls of a document at a time; null where not recorded.</summary>
    InteractionCurrent? InteractionAt(string documentToken, long time);

    /// <summary>Each key pressed, described with what happened to it, such as "Tab, passed to the page".</summary>
    IReadOnlyList<TimedFact> Keys { get; }

    /// <summary>The keys NVDA kept as its commands, with the commands' names.</summary>
    IReadOnlyList<TimedFact> Commands { get; }

    /// <summary>Each mouse button pressed, with what is known of it.</summary>
    IReadOnlyList<TimedFact> Clicks { get; }

    IReadOnlyList<FocusFact> Focus { get; }

    /// <summary>What NVDA had to announce, each line labelled as the player labels it.</summary>
    IReadOnlyList<TimedFact> Announcements { get; }

    IReadOnlyList<ForegroundFact> Foreground { get; }

    /// <summary>The browser's process name, as the front window records name it, or null.</summary>
    string? BrowserProcess { get; }

    /// <summary>The assistive technology observed running at a time, such as "NVDA 2026.2" or "full screen magnification at 200%".</summary>
    IReadOnlyList<string> AssistiveTechnologyAt(long time);

    /// <summary>Page popups opening, such as a select list.</summary>
    IReadOnlyList<TimedFact> Popups { get; }

    /// <summary>
    /// The accessibility and assistive technology settings at the start and
    /// their changes, for the layered report's appendices and story, given
    /// the first page visit's start; none where they were not read.
    /// </summary>
    RecordedSettings Settings(long? firstPageTime) => RecordedSettings.None;

    /// <summary>Page popups closing; none where not read.</summary>
    IReadOnlyList<TimedFact> PopupsClosed => [];

    /// <summary>The spans in which one of NVDA's own windows was in front; none where not read.</summary>
    IReadOnlyList<NvdaWindowFact> NvdaWindows => [];

    /// <summary>The page's changes to its live regions; none where not read.</summary>
    IReadOnlyList<LiveRegionFact> LiveRegions => [];

    /// <summary>Each scroll position recorded; none where not read.</summary>
    IReadOnlyList<ScrollFact> Scrolls => [];

    /// <summary>The document's viewport height in CSS pixels at a time; null where not recorded.</summary>
    double? ViewportHeightAt(string documentToken, long time) => null;
}

/// <summary>An entry of the evidence list: its reference, time, what it is, and its record.</summary>
/// <param name="Time">The time in the recording; null for evidence that is not of the recording, such as the auditor's description.</param>
public sealed record EvidenceEntry(string Reference, long? Time, string Text, string? EventId);

/// <summary>Numbers the evidence the facts cite, E1 upward, once for each record or fact.</summary>
public sealed class EvidenceRegister
{
    private readonly List<EvidenceEntry> _entries = [];
    private readonly Dictionary<string, string> _byKey = new(StringComparer.Ordinal);

    public IReadOnlyList<EvidenceEntry> Entries => _entries;

    public string Add(long? time, string text, string? eventId, string? key = null)
    {
        key ??= eventId;
        if (key is not null && _byKey.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var reference = "E" + (_entries.Count + 1).ToString(CultureInfo.InvariantCulture);
        _entries.Add(new EvidenceEntry(reference, time, text, eventId));
        if (key is not null)
        {
            _byKey[key] = reference;
        }

        return reference;
    }

    public EvidenceEntry? Of(string reference) => _entries.FirstOrDefault(entry => entry.Reference == reference);
}

/// <summary>
/// The settings of the collation. There is no limit on how many facts a
/// visit has or how long they are: a limit is shown to be forced only by
/// a server refusing a real request as longer than its context (owner,
/// 2026-10-16; recording-summary.md, "The collation limits removed").
/// </summary>
/// <param name="InputBeforeNanoseconds">An input counts as the one before a visit within this long before it.</param>
/// <param name="ScrollPauseNanoseconds">One scroller's scroll records with no pause between them longer than this are one scroll.</param>
public sealed record CollationSettings(
    long InputBeforeNanoseconds = 3_000_000_000,
    long ScrollPauseNanoseconds = 1_000_000_000)
{
    public static CollationSettings Default { get; } = new();
}

/// <summary>
/// One visit's facts: its lines for the model, the page's changes during
/// it in time order, and the references both cite. The moments' requests
/// are given the lines only, as each moment's reasons already name the
/// changes that chose it (owner, 2026-10-16).
/// </summary>
/// <summary>A fact of a visit at its time, as its line is given, and whether it is the participant's input.</summary>
public sealed record TimedLine(long Time, string Line, bool Input);

public sealed record VisitCollation(Visit Visit, string? Title, IReadOnlyList<string> Lines, IReadOnlySet<string> References, IReadOnlyList<string> Changes)
{
    /// <summary>
    /// The visit's inputs and what was recorded after each, in time order:
    /// each key and click, then the focus moves, NVDA's commands and
    /// inferred announcements, in-page jumps, and page changes before the
    /// next, so that the story pairs each action with its response (target
    /// report, 2026-10-18).
    /// </summary>
    public IReadOnlyList<TimedLine> Timeline { get; init; } = [];

    /// <summary>The heading of the changes, as each request that is given them has it.</summary>
    public const string ChangesHeading = "The page's changes during the visit, in time order: its live regions, its popups opening and closing, NVDA's windows, and its scrolls:";

    /// <summary>The changes with their heading, or a line that none was recorded.</summary>
    public string ChangesText => Changes.Count == 0
        ? "The page's changes during the visit: no change to a live region, popup opening or closing, NVDA window, or scroll was recorded."
        : ChangesHeading + "\n" + string.Join('\n', Changes);

    public string Text => string.Join('\n', Lines) + "\n" + ChangesText;
}

/// <summary>A period another program's window was in front.</summary>
public sealed record OtherWindow(string Reference, long Start, long End, string Process, string Title);

/// <summary>The participant's capabilities as given to the model.</summary>
/// <param name="Description">The auditor's description, or null when there is none.</param>
public sealed record ParticipantCapabilities(string? Description, DateTime? WrittenUtc)
{
    public static ParticipantCapabilities Unknown { get; } = new(null, null);
}

/// <summary>The recorder's collation of a recording, derived by its own code.</summary>
public sealed record RecordingCollation(
    IReadOnlyList<VisitCollation> Visits,
    IReadOnlyList<OtherWindow> OtherWindows,
    IReadOnlyList<string> CapabilityLines,
    EvidenceRegister Evidence,
    long Duration);

/// <summary>
/// Collates a recording's facts for the summary: the visits, and for each
/// its address, how it began, its title, its structure and opening text,
/// what the participant did, what NVDA had to announce, and its popups and
/// dialogs, each with an evidence reference. See
/// docs/architecture/recording-summary.md, "Collation by the recorder's code".
/// </summary>
public static class SummaryCollator
{
    public static RecordingCollation Collate(ISummaryEvidence evidence, ParticipantCapabilities capabilities, CollationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(capabilities);
        settings ??= CollationSettings.Default;
        var register = new EvidenceRegister();
        // The capabilities come first, so a reported description is E1.
        var capabilityLines = CapabilityLines(evidence, capabilities, register);
        var visits = VisitDivider.Divide(evidence.Commits, evidence.Duration, (commit, previous, until) => JudgeFragment(evidence, commit, until));
        var collated = visits.Select(visit => CollateVisit(evidence, visit, register, settings)).ToList();
        var windows = OtherWindows(evidence, register);
        return new RecordingCollation(collated, windows, capabilityLines, register, evidence.Duration);
    }

    /// <summary>A time as the player shows it, hours to milliseconds.</summary>
    public static string Time(long nanoseconds) =>
        TimeSpan.FromTicks(Math.Max(0, nanoseconds) / 100).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private static string Quote(string text) => "\"" + text.Replace("\"", "'", StringComparison.Ordinal) + "\"";

    // A fragment-only change: a step when the document has the place it
    // points to and its main content is not replaced before the tab's next
    // commit; otherwise a route.
    internal static FragmentJudgement JudgeFragment(ISummaryEvidence evidence, NavigationCommit commit, long until)
    {
        var (_, fragment) = PageSnapshot.SplitFragment(commit.Url);
        fragment ??= "";
        if (commit.DocumentToken is not { } token || evidence.PageAt(token, commit.Url, commit.Time) is not { } page)
        {
            return new FragmentJudgement(null, "the page's DOM at the change is not recorded, so where the fragment points is unknown");
        }

        var target = page.IndicatedPart(fragment);
        if (target.Kind == IndicatedPartKind.None)
        {
            return new FragmentJudgement(null, $"the recorded document has no place #{fragment} points to: {target.Rule}");
        }

        if (evidence.PageAt(token, commit.Url, Math.Max(commit.Time, until - 1)) is { } later)
        {
            var before = page.MainContent();
            var after = later.MainContent();
            if (before.Count > 0 && after.Count > 0 && !before.Overlaps(after))
            {
                return new FragmentJudgement(null, "the page's main content was replaced after the change");
            }
        }

        var where = target.Kind == IndicatedPartKind.Top
            ? $"the top of the document ({target.Rule})"
            : $"{Describe(page, target.Element!)} ({target.Rule})";
        var links = page.LinksTo(fragment);
        PageNode? link = null;
        var linkBasis = "";
        if (evidence.InteractionAt(token, commit.Time)?.FocusedNodeId is { } focused && links.FirstOrDefault(item => item.Id == focused) is { } focusedLink)
        {
            link = focusedLink;
            linkBasis = "the link focused when the change committed";
        }
        else if (links.Count == 1)
        {
            link = links[0];
            linkBasis = "the only link in the page to that place";
        }

        if (link is null)
        {
            return new FragmentJudgement(
                new FragmentStep(commit, fragment, "in-page jump", links.Count == 0
                    ? "no link in the page points there, so what caused it is not recorded"
                    : $"{links.Count} links point there and which one caused it is not recorded", where, null),
                null);
        }

        var named = page.NameLink(link, target);
        return new FragmentJudgement(
            new FragmentStep(commit, fragment, named.Text, $"{linkBasis}; {named.Rule}", where, page.NameOf(link)),
            null);
    }

    private static string Describe(PageSnapshot page, PageNode node)
    {
        var role = page.RoleOf(node) ?? (node.Tag.Length > 0 ? node.Tag + " element" : "node");
        var name = page.NameOf(node);
        return name.Length > 0 ? $"{role} {Quote(name)}" : role;
    }

    private static VisitCollation CollateVisit(ISummaryEvidence evidence, Visit visit, EvidenceRegister register, CollationSettings settings)
    {
        var lines = new List<string>();
        var references = new HashSet<string>(StringComparer.Ordinal);
        var start = visit.Start;
        string Cite(long time, string text, string? eventId, string? key = null)
        {
            var reference = register.Add(time, text, eventId, key);
            references.Add(reference);
            return reference;
        }

        var timeline = new List<TimedLine>();
        void Line(long time, string text, string? eventId, string? key = null) =>
            lines.Add($"[{Cite(time, text, eventId, key)}] {Time(time)} {text}");

        void Timed(long time, string text, string? eventId, bool input = false, string? key = null)
        {
            var line = $"[{Cite(time, text, eventId, key)}] {Time(time)} {text}";
            lines.Add(line);
            timeline.Add(new TimedLine(time, line, input));
        }

        var how = visit.Kind switch
        {
            VisitKind.Page => "a new document",
            VisitKind.SameDocument => "the same document, with a new URL",
            _ => $"the same document, a fragment route: {visit.RouteReason}"
        };
        var initiated = start.RendererInitiated switch
        {
            true => "Chromium classified it as started by the page's content",
            false => "Chromium classified it as not started by the page's content",
            null => "whether the page's content started it is not recorded"
        };
        Line(start.Time, $"Navigation committed, {how}, {start.Url}. {initiated}." + (start.ErrorPage ? " It ended in an error page." : ""), start.EventId);
        if (visit.ReturnOf is { } earlier)
        {
            lines.Add($"(A return to the URL of visit {earlier.ToString(CultureInfo.InvariantCulture)}, by URL alone.)");
        }

        lines.Add($"The visit lasts from {Time(start.Time)} to {Time(visit.End)}.");
        var before = evidence.Keys.Concat(evidence.Clicks)
            .Where(fact => fact.Time <= start.Time && fact.Time >= start.Time - settings.InputBeforeNanoseconds)
            .MaxBy(fact => fact.Time);
        if (before is not null)
        {
            Line(before.Time, $"The participant's last input before it: {before.Text}.", before.EventId);
        }
        else
        {
            lines.Add($"No key or click was recorded in the {settings.InputBeforeNanoseconds / 1e9:0.###} seconds before it.");
        }

        // Observed running, with a reference like every other fact, so that a
        // statement resting on it can cite it (target machine, 2026-10-14).
        foreach (var running in evidence.AssistiveTechnologyAt(start.Time))
        {
            Line(start.Time, $"Observed running at the visit's start: {running}.", null, $"running:{start.Time}:{running}");
        }

        // The page at the end of the visit: its title, structure, and text.
        var endTime = Math.Max(start.Time, visit.End - 1);
        var page = start.DocumentToken is { } token ? evidence.PageAt(token, start.Url, endTime) : null;
        string? title = null;
        if (page is null)
        {
            lines.Add("The page's DOM is not recorded, so its structure and text are unknown.");
        }
        else
        {
            title = page.Title;
            var basis = page.HasAccessibility
                ? "roles and names as Chromium's accessibility records give them"
                : "Chromium's accessibility records are not recorded, so roles and names are read from the tags";
            var structure = Cite(endTime, $"The page as recorded at the end of visit {visit.Number}", null, $"structure:{visit.Number}");
            lines.Add($"[{structure}] {Time(endTime)} The page as recorded at the end of the visit ({basis}):");
            lines.Add(title is null ? "  Title: none recorded." : $"  Title: {Quote(title)}.");
            var landmarks = page.Landmarks;
            lines.Add(landmarks.Count == 0
                ? "  Landmarks: none."
                : "  Landmarks: " + string.Join("; ", landmarks.Select(item => item.Name.Length > 0 ? $"{item.Role} {Quote(item.Name)}" : item.Role)) + ".");
            var headings = page.Headings;
            if (headings.Count == 0)
            {
                lines.Add("  Headings: none.");
            }
            else
            {
                lines.Add("  Headings, in order:");
                foreach (var heading in headings)
                {
                    var level = heading.Level is { } value ? "level " + value.ToString(CultureInfo.InvariantCulture) : "level not recorded";
                    lines.Add($"    {level}: {Quote(heading.Name)}");
                }
            }

            var fields = page.Fields;
            lines.Add(fields.Count == 0
                ? "  Form fields: none."
                : "  Form fields: " + string.Join("; ", fields.Select(item => $"{item.Role} {(item.Name.Length > 0 ? Quote(item.Name) : "with no name")}")) + ".");
            var buttons = page.Buttons;
            lines.Add(buttons.Count == 0
                ? "  Buttons: none."
                : $"  Buttons, {buttons.Count.ToString(CultureInfo.InvariantCulture)}: " + string.Join("; ", buttons.Select(item => item.Name.Length > 0 ? Quote(item.Name) : "a button with no name")) + ".");
            // Each link and image by name, not their numbers alone, so that
            // what a picture shows as a button can be set beside what it is
            // built as (owner, 2026-10-18: "we describe as visual and note it
            // is built on other semantics").
            var links = page.Links;
            lines.Add(links.Count == 0
                ? "  Links: none."
                : $"  Links, {links.Count.ToString(CultureInfo.InvariantCulture)}, in order: " + string.Join("; ", links.Select(item => item.Name.Length > 0 ? Quote(item.Name) : "a link with no name")) + ".");
            var images = page.Images;
            lines.Add(images.Count == 0
                ? "  Images: none."
                : $"  Images, {images.Count.ToString(CultureInfo.InvariantCulture)}, in order, by name: " + string.Join("; ", images.Select(item => item.Name.Length > 0 ? Quote(item.Name) : "an image with no name")) + ".");
            if (page.Dialogs is { Count: > 0 } dialogs)
            {
                lines.Add("  Dialogs in the page: " + string.Join("; ", dialogs.Select(item => item.Name.Length > 0 ? $"{item.Role} {Quote(item.Name)}" : item.Role)) + ".");
            }

            var container = page.Main ?? page.InOrder.FirstOrDefault(node => node.Tag == "body");
            if (container is not null)
            {
                var (text, _) = page.TextOf(container, int.MaxValue);
                var where = page.Main is not null ? "the main landmark" : "the page, which has no main landmark";
                var reference = Cite(endTime, $"The text of {where}, at the end of visit {visit.Number}", null, $"text:{visit.Number}");
                lines.Add(text.Length == 0
                    ? $"[{reference}] The text of {where}: none."
                    : $"[{reference}] The text of {where}, in full: {Quote(text)}");
            }
        }

        // In-page jumps.
        foreach (var step in visit.Steps)
        {
            var link = step.LinkName is { Length: > 0 } name ? $" {Quote(name)}" : "";
            Timed(step.Commit.Time, $"In-page jump to #{step.Fragment}, {step.Target}: a {step.Name}{link} ({step.Rule}).", step.Commit.EventId);
        }

        bool Within(long time) => time >= start.Time && time < visit.End;

        // What the participant did.
        var keys = evidence.Keys.Where(fact => Within(fact.Time)).ToList();
        if (keys.Count > 0)
        {
            var kinds = keys.GroupBy(fact => fact.Text, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            var reference = Cite(keys[0].Time, $"The keys pressed in visit {visit.Number}", keys[0].EventId, $"keys:{visit.Number}");
            lines.Add($"[{reference}] Keys pressed during the visit, {keys.Count.ToString(CultureInfo.InvariantCulture)} in all: " +
                string.Join("; ", kinds.Select(group => $"{group.Key} x{group.Count().ToString(CultureInfo.InvariantCulture)}")) + ".");
        }
        else
        {
            lines.Add("No keys were recorded during the visit.");
        }

        var commands = evidence.Commands.Where(fact => Within(fact.Time)).ToList();
        foreach (var command in commands)
        {
            Timed(command.Time, command.Text, command.EventId);
        }

        var clicks = evidence.Clicks.Where(fact => Within(fact.Time)).ToList();
        foreach (var click in clicks)
        {
            Timed(click.Time, click.Text, click.EventId, input: true);
        }

        if (start.DocumentToken is { } document)
        {
            var focus = evidence.Focus.Where(fact => fact.DocumentToken == document && Within(fact.Time)).ToList();
            foreach (var change in focus)
            {
                var what = change.NodeId is { } node && page?.Node(node) is { } focused
                    ? Describe(page, focused)
                    : change.NodeId is null ? "the document itself" : "an element no longer in the page at the visit's end";
                Timed(change.Time, $"The page's focus moved to {what}.", change.EventId);
            }

            // Text typed: each text control whose value at the end differs
            // from its value at the start, given verbatim.
            var atStart = evidence.InteractionAt(document, start.Time)?.TextControls ?? new Dictionary<long, TextControlState>();
            var atEnd = evidence.InteractionAt(document, endTime)?.TextControls ?? new Dictionary<long, TextControlState>();
            var typed = atEnd.Values
                .Where(control => control.Value is { Length: > 0 } value &&
                    (!atStart.TryGetValue(control.NodeId, out var earlierState) || earlierState.Value != value))
                .OrderBy(control => page?.Node(control.NodeId) is { } node ? page.OrderOf(node) : int.MaxValue)
                .ToList();
            foreach (var control in typed)
            {
                var field = page?.Node(control.NodeId) is { } node ? Describe(page, node) : "a text field";
                var kind = control.ControlType is { Length: > 0 } type ? $" (type {type})" : "";
                Line(endTime, $"Text in {field}{kind} at the end of the visit: {Quote(control.Value!)}.", null, $"typed:{visit.Number}:{control.NodeId}");
            }

        }

        var announcements = evidence.Announcements.Where(fact => Within(fact.Time)).ToList();
        foreach (var announcement in announcements)
        {
            Timed(announcement.Time, announcement.Text, announcement.EventId);
        }

        var changes = Changes(evidence, visit, page, settings, Cite);
        timeline.AddRange(changes.Select(change => new TimedLine(change.Time, change.Line, false)));

        // Each key at its time for the timeline alone, as the facts above
        // give the keys counted: a run of characters typed one after another
        // is one input, citing its first key.
        foreach (var (time, text, eventId) in KeyInputs(keys))
        {
            var line = $"[{Cite(time, text, eventId, $"key:{time.ToString(CultureInfo.InvariantCulture)}")}] {Time(time)} {text}";
            timeline.Add(new TimedLine(time, line, true));
        }

        return new VisitCollation(visit, title, lines, references, [.. changes.Select(change => change.Line)])
        {
            Timeline = [.. timeline.OrderBy(item => item.Time).ThenBy(item => item.Input ? 0 : 1)]
        };
    }

    // The keys as inputs: each key "Key pressed: Tab, passed to the page.",
    // and a run of single characters with nothing else between them as one,
    // "Characters typed, 12 keys, from "h" to "o", passed to the page.".
    internal static IEnumerable<(long Time, string Text, string? EventId)> KeyInputs(IReadOnlyList<TimedFact> keys)
    {
        static (string Key, string Tail) Split(string text)
        {
            var comma = text.IndexOf(", ", StringComparison.Ordinal);
            return comma < 0 ? (text, "") : (text[..comma], text[comma..]);
        }

        var run = new List<TimedFact>();
        IEnumerable<(long, string, string?)> Flush()
        {
            if (run.Count == 1)
            {
                yield return (run[0].Time, $"Key pressed: {run[0].Text}.", run[0].EventId);
            }
            else if (run.Count > 1)
            {
                var (first, rest) = Split(run[0].Text);
                var last = Split(run[^1].Text).Key;
                yield return (run[0].Time, string.Create(CultureInfo.InvariantCulture,
                    $"Characters typed, {run.Count} keys, from {Quote(first)} to {Quote(last)}{(run.All(item => Split(item.Text).Tail == rest) ? rest : ", each as the keys' facts give it")}."), run[0].EventId);
            }

            run.Clear();
        }

        foreach (var key in keys)
        {
            var (name, rest) = Split(key.Text);
            if (name.Length == 1 && (run.Count == 0 || Split(run[0].Text).Tail == rest))
            {
                run.Add(key);
                continue;
            }

            foreach (var item in Flush())
            {
                yield return item;
            }

            if (name.Length == 1)
            {
                run.Add(key);
                continue;
            }

            yield return (key.Time, $"Key pressed: {key.Text}.", key.EventId);
        }

        foreach (var item in Flush())
        {
            yield return item;
        }
    }

    // The page's changes during a visit, in time order, each with its
    // time and reference: each change to a live region, each popup opening
    // and closing, each of NVDA's windows coming to the front and leaving
    // it, and each scroll, one scroller's records with no pause longer than
    // the setting being one movement (owner, 2026-10-16; recording-summary.md,
    // "The page's changes during a visit").
    private static List<(long Time, string Line)> Changes(
        ISummaryEvidence evidence, Visit visit, PageSnapshot? page, CollationSettings settings, Func<long, string, string?, string?, string> cite)
    {
        var start = visit.Start;
        bool Within(long time) => time >= start.Time && time < visit.End;
        var changes = new List<(long Time, string Text, string? EventId, string? Key)>();
        var token = start.DocumentToken;
        if (token is not null)
        {
            foreach (var change in evidence.LiveRegions.Where(fact => fact.DocumentToken == token && Within(fact.Time)))
            {
                changes.Add((change.Time, LiveRegionChanges.Reason(change) + $" Its politeness: {change.Politeness}.", change.EventId, null));
            }
        }

        foreach (var popup in evidence.Popups.Where(fact => Within(fact.Time)))
        {
            changes.Add((popup.Time, popup.Text, popup.EventId, null));
        }

        foreach (var popup in evidence.PopupsClosed.Where(fact => Within(fact.Time)))
        {
            changes.Add((popup.Time, popup.Text, popup.EventId, null));
        }

        foreach (var window in evidence.NvdaWindows)
        {
            if (Within(window.Start))
            {
                changes.Add((window.Start, $"NVDA's window {Quote(window.Name)} came to the front.", window.StartEventId,
                    window.StartEventId ?? $"nvda-window:{window.Start}:{window.Name}"));
            }

            if (Within(window.End) && window.End > window.Start)
            {
                changes.Add((window.End, $"NVDA's window {Quote(window.Name)} left the front.", window.EndEventId,
                    window.EndEventId ?? $"nvda-window-end:{window.End}:{window.Name}"));
            }
        }

        if (token is not null)
        {
            changes.AddRange(ScrollMovements(evidence, token, start.Time, visit.End, page, settings));
        }

        return [.. changes.OrderBy(change => change.Time).Select(change => (change.Time, $"[{cite(change.Time, change.Text, change.EventId, change.Key)}] {Time(change.Time)} {change.Text}"))];
    }

    // Each scroll as a movement: one scroller's records with no pause
    // between them longer than the setting, from the position before it
    // where one is recorded to its last, with what was on the screen at
    // its end where the scroller is the page's own.
    private static IEnumerable<(long Time, string Text, string? EventId, string? Key)> ScrollMovements(
        ISummaryEvidence evidence, string token, long start, long end, PageSnapshot? page, CollationSettings settings)
    {
        var records = evidence.Scrolls.Where(item => item.DocumentToken == token && item.Time < end).OrderBy(item => item.Time).ToList();
        foreach (var scroller in records.GroupBy(item => item.NodeId is { } node ? "node:" + node.ToString(CultureInfo.InvariantCulture) : item.Scroller, StringComparer.Ordinal))
        {
            var all = scroller.ToList();
            var movement = new List<ScrollFact>();
            ScrollFact? before = null;
            foreach (var record in all)
            {
                if (record.Time < start)
                {
                    before = record;
                    continue;
                }

                if (movement.Count > 0 && record.Time - movement[^1].Time > settings.ScrollPauseNanoseconds)
                {
                    yield return Movement(evidence, token, before, movement, page);
                    before = movement[^1];
                    movement = [];
                }

                movement.Add(record);
            }

            if (movement.Count > 0)
            {
                yield return Movement(evidence, token, before, movement, page);
            }
        }
    }

    private static (long Time, string Text, string? EventId, string? Key) Movement(
        ISummaryEvidence evidence, string token, ScrollFact? before, List<ScrollFact> movement, PageSnapshot? page)
    {
        var first = movement[0];
        var last = movement[^1];
        var node = first.NodeId is { } id ? page?.Node(id) : null;
        var isPage = node is not null && (node.Kind == "document" || node.Tag is "html" or "body");
        var what = isPage ? "The page"
            : node is not null ? "The scroller " + Describe(page!, node)
            : "A scroller not in the page as recorded at the visit's end";
        var from = before is not null
            ? string.Create(CultureInfo.InvariantCulture, $"from {before.Y:0}")
            : "from a position not recorded in the visit";
        // One record is a scroll at one time, not one "from 00:00:40.661 to
        // 00:00:40.661, 1 position recorded" (target report, 2026-10-18).
        var text = movement.Count == 1
            ? string.Create(CultureInfo.InvariantCulture,
                $"{what} scrolled {from} to {last.Y:0} CSS pixels from the top at {Time(first.Time)}, 1 position recorded.")
            : string.Create(CultureInfo.InvariantCulture,
                $"{what} scrolled {from} to {last.Y:0} CSS pixels from the top, from {Time(first.Time)} to {Time(last.Time)}, {movement.Count} positions recorded.");
        if (isPage && evidence.ViewportHeightAt(token, last.Time) is { } height && height > 0)
        {
            text += string.Create(CultureInfo.InvariantCulture, $" With a viewport {height:0} CSS pixels high, CSS pixels {last.Y:0} to {last.Y + height:0} were then in it.");
        }

        return (first.Time, text, first.EventId, null);
    }

    // The periods another program's window was in front: from each front
    // window record that is not the browser's to the next record.
    private static List<OtherWindow> OtherWindows(ISummaryEvidence evidence, EvidenceRegister register)
    {
        var windows = new List<OtherWindow>();
        var records = evidence.Foreground.OrderBy(fact => fact.Time).ToList();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.Process is { } process && evidence.BrowserProcess is { } browser &&
                string.Equals(process, browser, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end = index + 1 < records.Count ? records[index + 1].Time : evidence.Duration;
            var title = record.Title ?? "no title recorded";
            var reference = register.Add(record.Time, $"{record.Process ?? "A program"} came to the front: {Quote(title)}", record.EventId);
            windows.Add(new OtherWindow(reference, record.Time, end, record.Process ?? "not recorded", title));
        }

        return windows;
    }

    private static List<string> CapabilityLines(ISummaryEvidence evidence, ParticipantCapabilities capabilities, EvidenceRegister register)
    {
        var lines = new List<string>();
        if (capabilities.Description is { Length: > 0 } description)
        {
            var written = capabilities.WrittenUtc is { } when
                ? when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "an unrecorded date";
            var reference = register.Add(null, $"The participant's capabilities, as the auditor reported them on {written}", null, "capabilities");
            lines.Add($"[{reference}] The participant's capabilities, reported by the auditor on {written}, a person's statement and not an observation: {Quote(description)}");
        }
        else
        {
            lines.Add("The participant's capabilities are unknown: no description was given. Do not say what the participant saw or heard; say what was on the screen and what NVDA had to announce.");
        }

        // Observed running, with a reference like every other fact, so that a
        // statement resting on it can cite it. Without one, the whole
        // recording's step failed on the target machine on 2026-10-14: "NVDA
        // 2026.2 was observed running at the start of the recording" was a
        // true claim with nothing it could cite.
        var running = evidence.AssistiveTechnologyAt(0);
        if (running.Count > 0)
        {
            var observed = "Observed running at the start of the recording: " + string.Join("; ", running);
            var reference = register.Add(0, observed, null, "running:start");
            lines.Add($"[{reference}] {observed}. This shows what was running, not what the participant is able to do.");
        }

        return lines;
    }

    /// <summary>The whole recording's facts: each visit's URL, title, and every heading, as the overall request is given them.</summary>
    public static string Overview(RecordingCollation collation)
    {
        ArgumentNullException.ThrowIfNull(collation);
        var builder = new StringBuilder();
        foreach (var visit in collation.Visits)
        {
            var start = visit.Visit.Start;
            var reference = collation.Evidence.Entries.First(entry => entry.EventId == start.EventId).Reference;
            builder.Append(CultureInfo.InvariantCulture, $"Visit {visit.Visit.Number} [{reference}], {Time(start.Time)} to {Time(visit.Visit.End)}, {visit.Visit.Label}, {start.Url}");
            builder.Append(visit.Title is { } title ? $", title {Quote(title)}" : ", no title recorded").Append('\n');
            var headings = visit.Lines.SkipWhile(line => !line.StartsWith("  Headings, in order:", StringComparison.Ordinal)).Skip(1)
                .TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal)).ToList();
            if (headings.Count > 0)
            {
                var structure = collation.Evidence.Entries.FirstOrDefault(entry => entry.Text == $"The page as recorded at the end of visit {visit.Visit.Number}");
                builder.Append(CultureInfo.InvariantCulture, $"  Headings [{structure?.Reference}]:\n");
                foreach (var heading in headings)
                {
                    builder.Append(heading).Append('\n');
                }
            }
        }

        if (collation.OtherWindows.Count > 0)
        {
            builder.Append("Other programs' windows in front:\n");
            foreach (var window in collation.OtherWindows)
            {
                builder.Append(CultureInfo.InvariantCulture, $"[{window.Reference}] {Time(window.Start)} to {Time(window.End)}: {window.Process}, {Quote(window.Title)}\n");
            }
        }

        return builder.ToString();
    }
}
