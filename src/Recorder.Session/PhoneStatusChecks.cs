using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// An announcement or a toast the phone's accessibility events record: a
/// <c>TYPE_ANNOUNCEMENT</c> (View.announceForAccessibility) or a
/// <c>TYPE_NOTIFICATION_STATE_CHANGED</c>, which Android sends for a toast
/// and for a notification.
/// </summary>
public sealed record PhoneAnnouncement(long Time, int EventType, string? Package, string? ClassName, string Text)
{
    /// <summary>Whether it is a toast's, not a notification's: Android names a toast's class android.widget.Toast.</summary>
    public bool IsToast =>
        EventType == PhoneAnnouncementTimeline.NotificationEventType &&
        (ClassName?.Contains("Toast", StringComparison.Ordinal) ?? false);

    public bool IsAnnouncement => EventType == PhoneAnnouncementTimeline.AnnouncementEventType;
}

/// <summary>
/// The phone's announcements and toasts through a recording, for the
/// status message checks (android-checks.md, step 12); the playback index
/// keeps their events whole.
/// </summary>
public sealed class PhoneAnnouncementTimeline
{
    /// <summary><c>AccessibilityEvent.TYPE_ANNOUNCEMENT</c>.</summary>
    public const int AnnouncementEventType = 0x4000;

    /// <summary><c>AccessibilityEvent.TYPE_NOTIFICATION_STATE_CHANGED</c>.</summary>
    public const int NotificationEventType = 0x40;

    public PhoneAnnouncementTimeline(IEnumerable<(long Time, JsonElement Payload)> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var all = new List<PhoneAnnouncement>();
        foreach (var (time, payload) in records)
        {
            if (payload.ValueKind != JsonValueKind.Object ||
                PhoneScreenEvidence.ReadInt32(payload, "eventType") is not { } type)
            {
                continue;
            }

            var words = new List<string>();
            if (payload.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.Array)
            {
                words.AddRange(text.EnumerateArray()
                    .Where(part => part.ValueKind == JsonValueKind.String)
                    .Select(part => part.GetString() ?? string.Empty)
                    .Where(part => part.Length > 0));
            }

            if (words.Count == 0 && Read(payload, "contentDescription") is { Length: > 0 } description)
            {
                words.Add(description);
            }

            all.Add(new PhoneAnnouncement(time, type, Read(payload, "packageName"), Read(payload, "className"), string.Join(" ", words)));
        }

        All = [.. all.OrderBy(record => record.Time)];
    }

    public static PhoneAnnouncementTimeline Empty { get; } = new([]);

    /// <summary>The announcements and toasts, and notifications, in time order.</summary>
    public IReadOnlyList<PhoneAnnouncement> All { get; }

    /// <summary>Whether a record is a TYPE_ANNOUNCEMENT or TYPE_NOTIFICATION_STATE_CHANGED event, which the playback index keeps whole.</summary>
    public static bool IsAnnouncementRecord(string channel, string eventType, JsonElement payload) =>
        channel == AndroidAccessibilityRecords.EventsChannel &&
        eventType == AndroidAccessibilityRecords.EventType &&
        payload.ValueKind == JsonValueKind.Object &&
        PhoneScreenEvidence.ReadInt32(payload, "eventType") is AnnouncementEventType or NotificationEventType;

    private static string? Read(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// Step 12 of android-checks.md, status messages (WCAG 4.1.3, which
/// WCAG2ICT applies to software as written), two checks, each one discrete
/// issue (owner, 2026-11-13):
/// <list type="bullet">
/// <item>a status message not announced, an error: text that is shown,
/// is not a control, and goes away by itself within 5 seconds, with no
/// touch between, as a loading or saved message does, and is not in a live
/// region, came with no announcement and is in no titled pane that appeared
/// with it. Text is shown when it is new to the window: not in the
/// window's items as last taken, shown or not, so text a list shows again
/// as it closes is not a message. Texts that come and go together are one
/// message;</item>
/// <item>no live region in the app, a warning: in the whole recording no
/// item of the app's windows was in a live region, and it made no
/// announcement and showed no toast, so its status messages, if any, are
/// likely not told to a screen reader; to be looked for (owner).</item>
/// </list>
/// The first follows each app window through every snapshot, not only the
/// settled ones, as a loading message can be gone in 0.3 seconds
/// (measured). Without touches recorded it does not run, as text going
/// away by itself cannot be told from text a touch took away.
/// </summary>
public static class PhoneStatusChecks
{
    /// <summary>Text gone within this long, with no touch between, is a status message.</summary>
    public const long BriefNanoseconds = 5_000_000_000;

    /// <summary>An announcement this long before a message is shown is taken as its own.</summary>
    public const long AnnouncedBeforeNanoseconds = 1_000_000_000;

    /// <summary>What the checks give: the findings and the counts the summary reports.</summary>
    public sealed record Result(
        IReadOnlyList<PhoneFinding> Brief,
        IReadOnlyList<PhoneFinding> Apps,
        bool BriefRun,
        int BriefMessages,
        int BriefExposed,
        int TakingsRead,
        int LiveRegionItems,
        int Announcements,
        int Toasts,
        IReadOnlyList<string> Problems);

    private sealed class Open
    {
        public required long Start { get; init; }

        public required string Package { get; init; }

        public bool Exposed { get; set; }

        public List<(SessionTimelineEvent Taking, PhoneCheckItem Item)> Seen { get; } = [];
    }

    public static Result Run(
        PhoneScreenTimeline screens,
        PhoneAccessibilityTimeline trees,
        PhoneTouchTimeline touches,
        PhoneAnnouncementTimeline announcements,
        ISessionEventRecordSource source,
        PhoneSnapshotPictures pictures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(touches);
        ArgumentNullException.ThrowIfNull(announcements);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pictures);
        var briefRun = touches.Recorded;
        var problems = new List<string>();
        var findings = new Dictionary<(PhoneVisit, string), PhoneFinding>();
        var livePackages = new HashSet<string>(StringComparer.Ordinal);
        var readPackages = new HashSet<string>(StringComparer.Ordinal);
        var liveItems = new HashSet<(int, string)>();
        int takings = 0, messages = 0, exposed = 0;
        foreach (var windowId in screens.Visits.Select(visit => visit.WindowId).Distinct())
        {
            var visits = screens.Visits.Where(visit => visit.WindowId == windowId).ToList();
            var open = new Dictionary<string, Open>(StringComparer.Ordinal);
            var closed = new List<(string Text, Open Message, long End)>();
            HashSet<string>? before = null;
            HashSet<string> panesBefore = new(StringComparer.Ordinal);
            foreach (var (snapshot, listed, taken) in trees.ListingsOf(windowId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!listed)
                {
                    // The window went, and its text with it: not a message
                    // going away by itself.
                    open.Clear();
                    before = null;
                    continue;
                }

                if (!taken)
                {
                    continue;
                }

                PhoneCheckWindow? window;
                try
                {
                    window = PhoneCheckRun.ReadWindow(source, snapshot, windowId);
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or InvalidOperationException or
                        JsonException or KeyNotFoundException or UnauthorizedAccessException)
                {
                    problems.Add($"{WindowsPreferenceTimeline.Clock(snapshot.MonotonicNanoseconds)}: {exception.Message}");
                    continue;
                }

                if (window is null)
                {
                    continue;
                }

                takings++;
                var time = snapshot.MonotonicNanoseconds;
                var package = window.Items.Select(item => item.Node.PackageName).FirstOrDefault(name => !string.IsNullOrEmpty(name)) ?? string.Empty;
                readPackages.Add(package);
                foreach (var item in window.Items.Where(item => item.Node.LiveRegion != 0))
                {
                    liveItems.Add((windowId, item.Identity));
                    livePackages.Add(package);
                }

                var shown = new Dictionary<string, PhoneCheckItem>(StringComparer.Ordinal);
                foreach (var item in window.Items)
                {
                    if (Message(item) is { } words)
                    {
                        shown.TryAdd(words, item);
                    }
                }

                foreach (var text in open.Keys.Where(text => !shown.ContainsKey(text)).ToList())
                {
                    closed.Add((text, open[text], time));
                    open.Remove(text);
                }

                foreach (var (text, item) in shown)
                {
                    if (!open.TryGetValue(text, out var message))
                    {
                        if (before is not null && before.Contains(text))
                        {
                            continue;
                        }

                        open[text] = message = new Open
                        {
                            Start = time,
                            Package = package,
                            Exposed = InLiveRegion(item) || InNewPane(item, panesBefore),
                        };
                    }

                    message.Seen.Add((snapshot, item));
                }

                before = new HashSet<string>(window.Items.SelectMany(item => Words(item.Node)), StringComparer.Ordinal);
                panesBefore = new HashSet<string>(
                    window.Items.Select(item => item.Node.PaneTitle).Where(title => !string.IsNullOrWhiteSpace(title)).Select(title => title!.Trim()),
                    StringComparer.Ordinal);
            }

            if (!briefRun)
            {
                continue;
            }

            // Texts that came and went together are one message.
            foreach (var group in closed
                .Where(entry => entry.End - entry.Message.Start <= BriefNanoseconds &&
                    !touches.TouchedBetween(entry.Message.Start, entry.End))
                .GroupBy(entry => (entry.Message.Start, entry.End)))
            {
                messages++;
                var (start, end) = group.Key;
                var package = group.First().Message.Package;
                var announced = announcements.All.Any(record =>
                    record.IsAnnouncement && record.Package == package &&
                    record.Time >= start - AnnouncedBeforeNanoseconds && record.Time <= end);
                if (announced || group.Any(entry => entry.Message.Exposed))
                {
                    exposed++;
                    continue;
                }

                if (visits.LastOrDefault(visit => visit.Start <= start) is not { } visitOf)
                {
                    continue;
                }

                var main = group.OrderByDescending(entry => entry.Text.Length).ThenBy(entry => entry.Text, StringComparer.Ordinal).First();
                var shownAt = pictures.Between(start, end)?.Time ?? start;
                var (taking, item) = main.Message.Seen.LastOrDefault(seen => seen.Taking.MonotonicNanoseconds <= shownAt) is { Taking: not null } at
                    ? at
                    : main.Message.Seen[0];
                // The item is named by its row's label; the texts shown with
                // it are quoted.
                var others = group.Where(entry => entry.Text != main.Text).Select(entry => entry.Text)
                    .OrderByDescending(text => text.Length).ThenBy(text => text, StringComparer.Ordinal).Select(Quote).ToList();
                var seconds = ((end - start) / 1e9).ToString("0.00", CultureInfo.CurrentCulture);
                var shownWith = others.Count == 0
                    ? "it was shown and went away by itself"
                    : $"it was shown with {(others.Count == 1 ? others[0] : string.Join(", ", others.Take(others.Count - 1)) + " and " + others[^1])} and they went away by themselves";
                var measured =
                    $"{shownWith} {seconds} seconds later, with no touch between, as a status message does; " +
                    "it is not in a live region, no announcement was made and no titled pane appeared with it, so a screen reader is not told of it";
                var key = (visitOf, item.Identity);
                if (!findings.TryGetValue(key, out var finding))
                {
                    findings[key] = finding = new PhoneFinding(PhoneCheckNames.StatusNotAnnounced, PhoneCheckLevel.Error, visitOf, item.Identity, item.Label);
                }

                finding._times.Add(new PhoneFindingTime(
                    shownAt, visitOf, taking, taking, windowId, item.Bounds, item.TreeKey, measured, PhoneCheckRun.StateAt(visitOf, shownAt)));
            }
        }

        var apps = new List<PhoneFinding>();
        foreach (var app in screens.Apps)
        {
            if (!readPackages.Contains(app.Package) || livePackages.Contains(app.Package) ||
                announcements.All.Any(record => record.Package == app.Package && (record.IsAnnouncement || record.IsToast)))
            {
                continue;
            }

            var first = app.Screens.SelectMany(screen => screen.Visits).OrderBy(visit => visit.Start).First();
            var shownOn = first.SettledSnapshots.Count > 0 ? first.SettledSnapshots[0] : first.Snapshots.FirstOrDefault();
            if (shownOn is null || trees.TakenBy(shownOn, first.WindowId) is not { } taking)
            {
                continue;
            }

            PhoneCheckWindow? window;
            try
            {
                window = PhoneCheckRun.ReadWindow(source, taking, first.WindowId);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or InvalidOperationException or
                    JsonException or KeyNotFoundException or UnauthorizedAccessException)
            {
                problems.Add($"{WindowsPreferenceTimeline.Clock(taking.MonotonicNanoseconds)}: {exception.Message}");
                continue;
            }

            if (window is not { Items.Count: > 0 })
            {
                continue;
            }

            var visitCount = app.Screens.Sum(screen => screen.Visits.Count);
            var root = window.Items[0];
            var finding = new PhoneFinding(PhoneCheckNames.NoLiveRegion, PhoneCheckLevel.Warning, first, root.Identity, root.Label);
            finding._times.Add(new PhoneFindingTime(
                pictures.TimeOf(first), first, shownOn, taking, first.WindowId, root.Bounds, root.TreeKey,
                string.Create(CultureInfo.CurrentCulture, $"in its {visitCount:N0} {(visitCount == 1 ? "visit" : "visits")} in this recording, ") +
                    "no item of the app was in a live region, and it made no announcement and showed no toast; " +
                    "look for status messages, such as saved, sent, loading, progress or error messages, that should be in a live region",
                null));
            apps.Add(finding);
        }

        return new Result(
            [.. findings.Values],
            apps,
            briefRun,
            messages,
            exposed,
            takings,
            liveItems.Count,
            announcements.All.Count(record => record.IsAnnouncement),
            announcements.All.Count(record => record.IsToast),
            problems);
    }

    // The words of an item a status message can be: shown, with text or a
    // description, and not a control.
    private static string? Message(PhoneCheckItem item)
    {
        var node = item.Node;
        if (!node.VisibleToUser || item.Bounds.IsEmpty ||
            node.Clickable || node.LongClickable || node.Checkable || node.Editable)
        {
            return null;
        }

        return Words(node).FirstOrDefault();
    }

    private static IEnumerable<string> Words(AndroidTreeNode node)
    {
        foreach (var words in new[] { node.Text, node.ContentDescription })
        {
            if (!string.IsNullOrWhiteSpace(words))
            {
                yield return words.Trim();
            }
        }
    }

    private static bool InLiveRegion(PhoneCheckItem item)
    {
        var guard = 0;
        for (var step = item; step is not null && guard++ < 1000; step = step.Parent)
        {
            if (step.Node.LiveRegion != 0)
            {
                return true;
            }
        }

        return false;
    }

    // In a pane with a title that the window's items as last taken did not
    // have, so the pane appeared with it and a screen reader says its title.
    // The client's app gives its whole window the pane title " ", which says
    // nothing (measured).
    private static bool InNewPane(PhoneCheckItem item, HashSet<string> panesBefore)
    {
        var guard = 0;
        for (var step = item; step is not null && guard++ < 1000; step = step.Parent)
        {
            if (!string.IsNullOrWhiteSpace(step.Node.PaneTitle) && !panesBefore.Contains(step.Node.PaneTitle.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static string Quote(string words)
    {
        var one = string.Join(" ", words.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return "\"" + (one.Length > 60 ? string.Concat(one.AsSpan(0, 60), "...") : one) + "\"";
    }
}
