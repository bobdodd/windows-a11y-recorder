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

/// <summary>The limits on each visit's facts, so that a long page cannot overflow the model's context.</summary>
public sealed record CollationLimits(
    int Landmarks = 20,
    int Headings = 40,
    int Fields = 30,
    int Buttons = 30,
    int MainTextCharacters = 1500,
    int KeyKinds = 20,
    int Commands = 30,
    int Clicks = 20,
    int FocusChanges = 30,
    int Announcements = 20,
    int TypedFields = 20,
    int Steps = 20,
    int Popups = 10,
    long InputBeforeNanoseconds = 3_000_000_000)
{
    public static CollationLimits Default { get; } = new();
}

/// <summary>One visit's facts: its lines for the model, the references they cite, and how many facts were cut.</summary>
public sealed record VisitCollation(Visit Visit, string? Title, IReadOnlyList<string> Lines, IReadOnlySet<string> References, int LeftOut)
{
    public string Text => string.Join('\n', Lines);
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
    public static RecordingCollation Collate(ISummaryEvidence evidence, ParticipantCapabilities capabilities, CollationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(capabilities);
        limits ??= CollationLimits.Default;
        var register = new EvidenceRegister();
        // The capabilities come first, so a reported description is E1.
        var capabilityLines = CapabilityLines(evidence, capabilities, register);
        var visits = VisitDivider.Divide(evidence.Commits, evidence.Duration, (commit, previous, until) => JudgeFragment(evidence, commit, until));
        var collated = visits.Select(visit => CollateVisit(evidence, visit, register, limits)).ToList();
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
        return name.Length > 0 ? $"{role} {Quote(Shorten(name, 80))}" : role;
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length] + "...";

    private static VisitCollation CollateVisit(ISummaryEvidence evidence, Visit visit, EvidenceRegister register, CollationLimits limits)
    {
        var lines = new List<string>();
        var references = new HashSet<string>(StringComparer.Ordinal);
        var leftOut = 0;
        var start = visit.Start;
        string Cite(long time, string text, string? eventId, string? key = null)
        {
            var reference = register.Add(time, text, eventId, key);
            references.Add(reference);
            return reference;
        }

        void Line(long time, string text, string? eventId, string? key = null) =>
            lines.Add($"[{Cite(time, text, eventId, key)}] {Time(time)} {text}");

        void More(int count, string what)
        {
            if (count > 0)
            {
                leftOut += count;
                lines.Add($"({count.ToString(CultureInfo.InvariantCulture)} more {what} left out)");
            }
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
            .Where(fact => fact.Time <= start.Time && fact.Time >= start.Time - limits.InputBeforeNanoseconds)
            .MaxBy(fact => fact.Time);
        if (before is not null)
        {
            Line(before.Time, $"The participant's last input before it: {before.Text}.", before.EventId);
        }
        else
        {
            lines.Add("No key or click was recorded in the 3 seconds before it.");
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
                : "  Landmarks: " + string.Join("; ", landmarks.Take(limits.Landmarks).Select(item => item.Name.Length > 0 ? $"{item.Role} {Quote(item.Name)}" : item.Role)) + ".");
            More(landmarks.Count - limits.Landmarks, "landmarks");
            var headings = page.Headings;
            if (headings.Count == 0)
            {
                lines.Add("  Headings: none.");
            }
            else
            {
                lines.Add("  Headings, in order:");
                foreach (var heading in headings.Take(limits.Headings))
                {
                    var level = heading.Level is { } value ? "level " + value.ToString(CultureInfo.InvariantCulture) : "level not recorded";
                    lines.Add($"    {level}: {Quote(Shorten(heading.Name, 160))}");
                }

                More(headings.Count - limits.Headings, "headings");
            }

            var fields = page.Fields;
            lines.Add(fields.Count == 0
                ? "  Form fields: none."
                : "  Form fields: " + string.Join("; ", fields.Take(limits.Fields).Select(item => $"{item.Role} {(item.Name.Length > 0 ? Quote(Shorten(item.Name, 80)) : "with no name")}")) + ".");
            More(fields.Count - limits.Fields, "form fields");
            var buttons = page.Buttons;
            lines.Add(buttons.Count == 0
                ? "  Buttons: none."
                : "  Buttons: " + string.Join("; ", buttons.Take(limits.Buttons).Select(item => item.Name.Length > 0 ? Quote(Shorten(item.Name, 80)) : "a button with no name")) + ".");
            More(buttons.Count - limits.Buttons, "buttons");
            lines.Add($"  Links: {page.LinkCount.ToString(CultureInfo.InvariantCulture)}. Images: {page.ImageCount.ToString(CultureInfo.InvariantCulture)}.");
            if (page.Dialogs is { Count: > 0 } dialogs)
            {
                lines.Add("  Dialogs in the page: " + string.Join("; ", dialogs.Select(item => item.Name.Length > 0 ? $"{item.Role} {Quote(item.Name)}" : item.Role)) + ".");
            }

            var container = page.Main ?? page.InOrder.FirstOrDefault(node => node.Tag == "body");
            if (container is not null)
            {
                var (text, more) = page.TextOf(container, limits.MainTextCharacters);
                var where = page.Main is not null ? "the main landmark" : "the page, which has no main landmark";
                var reference = Cite(endTime, $"The opening text of {where}, at the end of visit {visit.Number}", null, $"text:{visit.Number}");
                lines.Add(text.Length == 0
                    ? $"[{reference}] The opening text of {where}: none."
                    : $"[{reference}] The opening text of {where}: {Quote(text)}" + (more > 0 ? $" ({more.ToString(CultureInfo.InvariantCulture)} more characters left out)" : ""));
                if (more > 0)
                {
                    leftOut++;
                }
            }
        }

        // In-page jumps.
        foreach (var step in visit.Steps.Take(limits.Steps))
        {
            var link = step.LinkName is { Length: > 0 } name ? $" {Quote(name)}" : "";
            Line(step.Commit.Time, $"In-page jump to #{step.Fragment}, {step.Target}: a {step.Name}{link} ({step.Rule}).", step.Commit.EventId);
        }

        More(visit.Steps.Count - limits.Steps, "in-page jumps");

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
                string.Join("; ", kinds.Take(limits.KeyKinds).Select(group => $"{group.Key} x{group.Count().ToString(CultureInfo.InvariantCulture)}")) + ".");
            More(kinds.Count - limits.KeyKinds, "kinds of key");
        }
        else
        {
            lines.Add("No keys were recorded during the visit.");
        }

        var commands = evidence.Commands.Where(fact => Within(fact.Time)).ToList();
        foreach (var command in commands.Take(limits.Commands))
        {
            Line(command.Time, command.Text, command.EventId);
        }

        More(commands.Count - limits.Commands, "NVDA commands");
        var clicks = evidence.Clicks.Where(fact => Within(fact.Time)).ToList();
        foreach (var click in clicks.Take(limits.Clicks))
        {
            Line(click.Time, click.Text, click.EventId);
        }

        More(clicks.Count - limits.Clicks, "clicks");
        if (start.DocumentToken is { } document)
        {
            var focus = evidence.Focus.Where(fact => fact.DocumentToken == document && Within(fact.Time)).ToList();
            foreach (var change in focus.Take(limits.FocusChanges))
            {
                var what = change.NodeId is { } node && page?.Node(node) is { } focused
                    ? Describe(page, focused)
                    : change.NodeId is null ? "the document itself" : "an element no longer in the page at the visit's end";
                Line(change.Time, $"The page's focus moved to {what}.", change.EventId);
            }

            More(focus.Count - limits.FocusChanges, "focus changes");

            // Text typed: each text control whose value at the end differs
            // from its value at the start, given verbatim.
            var atStart = evidence.InteractionAt(document, start.Time)?.TextControls ?? new Dictionary<long, TextControlState>();
            var atEnd = evidence.InteractionAt(document, endTime)?.TextControls ?? new Dictionary<long, TextControlState>();
            var typed = atEnd.Values
                .Where(control => control.Value is { Length: > 0 } value &&
                    (!atStart.TryGetValue(control.NodeId, out var earlierState) || earlierState.Value != value))
                .OrderBy(control => page?.Node(control.NodeId) is { } node ? page.OrderOf(node) : int.MaxValue)
                .ToList();
            foreach (var control in typed.Take(limits.TypedFields))
            {
                var field = page?.Node(control.NodeId) is { } node ? Describe(page, node) : "a text field";
                var kind = control.ControlType is { Length: > 0 } type ? $" (type {type})" : "";
                Line(endTime, $"Text in {field}{kind} at the end of the visit: {Quote(control.Value!)}.", null, $"typed:{visit.Number}:{control.NodeId}");
            }

            More(typed.Count - limits.TypedFields, "text fields");
        }

        var announcements = evidence.Announcements.Where(fact => Within(fact.Time)).ToList();
        foreach (var announcement in announcements.Take(limits.Announcements))
        {
            Line(announcement.Time, announcement.Text, announcement.EventId);
        }

        More(announcements.Count - limits.Announcements, "NVDA announcements");
        var popups = evidence.Popups.Where(fact => Within(fact.Time)).ToList();
        foreach (var popup in popups.Take(limits.Popups))
        {
            Line(popup.Time, popup.Text, popup.EventId);
        }

        More(popups.Count - limits.Popups, "popups");
        return new VisitCollation(visit, title, lines, references, leftOut);
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

    /// <summary>The whole recording's facts: each visit's URL, title, and headings, as the overall request is given them.</summary>
    public static string Overview(RecordingCollation collation, int headingsPerVisit = 10)
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
                .TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal)).Take(headingsPerVisit).ToList();
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
