using System.Text.Json;

namespace Recorder.Session;

public sealed record BrowserNavigationCorrelation(
    string NavigationId,
    string Url,
    long StartNanoseconds,
    long EndNanoseconds,
    long? CompletedNanoseconds,
    bool PrimaryPage,
    bool SameDocument,
    bool? Committed,
    string? Outcome,
    string? DocumentToken,
    int? RendererProcessId,
    string CorrelationBasis,
    int CheckpointCount,
    int DomNodeCount,
    int TruncatedCheckpointCount,
    int AccessibilityCheckpointCount,
    int AccessibilityNodeCount,
    int TruncatedAccessibilityCheckpointCount,
    int DispatchCount,
    int ListenerInvocationCount,
    int RelatedEventCount)
{
    /// <summary>
    /// The browser instance the navigation was recorded in.
    /// </summary>
    public string? BrowserInstanceId { get; init; }

    /// <summary>
    /// Chromium's frame type for the navigated frame, such as
    /// <c>primary-main-frame</c> or <c>subframe</c>. Null when not recorded.
    /// </summary>
    public string? FrameType { get; init; }

    /// <summary>
    /// What the navigated frame is: the tab's page, an iframe, the browser's
    /// own interface, or another kind of main frame.
    /// </summary>
    public BrowserNavigationKind Kind => BrowserNavigationKinds.Classify(
        FrameType,
        PrimaryPage,
        Url);

    /// <summary>
    /// Whether the navigation is of the tab's top-level page, the primary
    /// main frame. <see cref="PrimaryPage"/> is also true for every iframe in
    /// that page, so it does not identify page navigations.
    /// </summary>
    public bool IsPageNavigation => Kind == BrowserNavigationKind.Page;

    /// <summary>
    /// The time of the first captured desktop frame that can show the
    /// navigation's page, by <see cref="FirstFrameBasis"/>. Null when no
    /// captured frame qualifies or the frame was not determined.
    /// </summary>
    public long? FirstFrameNanoseconds { get; init; }

    /// <summary>
    /// The evidence <see cref="FirstFrameNanoseconds"/> was chosen from.
    /// </summary>
    public BrowserNavigationFrameBasis FirstFrameBasis { get; init; }

    /// <summary>
    /// Where playback goes to show the navigation: its first frame when one
    /// was determined, and otherwise the time the navigation started.
    /// </summary>
    public long SeekNanoseconds => FirstFrameNanoseconds ?? StartNanoseconds;

    public string Label
    {
        get
        {
            // Only a page is expected to be drawn, so only a page is marked
            // when no presented rendering update was recorded for it.
            var marker = Kind == BrowserNavigationKind.Page &&
                FirstFrameBasis == BrowserNavigationFrameBasis.NavigationCompletion
                    ? "[No render evidence] "
                    : string.Empty;
            return $"{FormatTime(StartNanoseconds)} | " +
                $"[{BrowserNavigationKinds.Describe(Kind)}] {marker}{Url}";
        }
    }

    private static string FormatTime(long nanoseconds) =>
        TimeSpan.FromTicks(Math.Max(0, nanoseconds) / 100)
            .ToString(@"hh\:mm\:ss\.fff");
}

internal sealed record BrowserEventProjection(
    SessionTimelineEvent Event,
    string? BrowserInstanceId,
    int? ProcessId,
    string? ProcessType,
    int? RendererProcessId,
    string? DocumentId,
    string? DocumentToken,
    string? FrameId,
    string? FrameType,
    string? NavigationId,
    string? Url,
    bool PrimaryPage,
    bool SameDocument,
    bool? Committed,
    string? Outcome,
    bool Truncated)
{
    /// <summary>
    /// How many events this projection stands for: one, or the number of
    /// events with the same identity, type, and truncation that a playback
    /// index counted together.
    /// </summary>
    public int Weight { get; init; } = 1;
}

internal static class BrowserNavigationCorrelator
{

    public static BrowserEventProjection? Project(
        SessionTimelineEvent timelineEvent,
        JsonElement payload)
    {
        if (!timelineEvent.Channel.StartsWith("browser.", StringComparison.Ordinal) ||
            payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var context = payload.TryGetProperty("context", out var contextValue) &&
            contextValue.ValueKind == JsonValueKind.Object
                ? contextValue
                : default;
        return new BrowserEventProjection(
            timelineEvent,
            ReadString(context, "browserInstanceId"),
            ReadInt32(context, "processId"),
            ReadString(context, "processType"),
            ReadInt32(payload, "rendererProcessId"),
            ReadString(context, "documentId"),
            ReadString(context, "documentToken"),
            ReadString(context, "frameId"),
            ReadString(payload, "frameType"),
            ReadString(payload, "navigationId"),
            ReadString(payload, "url"),
            ReadBoolean(payload, "primaryPage") ?? false,
            ReadBoolean(payload, "sameDocument") ?? false,
            ReadBoolean(payload, "committed"),
            ReadString(payload, "outcome"),
            ReadBoolean(payload, "truncated") ?? false);
    }

    public static IReadOnlyList<BrowserNavigationCorrelation> Build(
        IReadOnlyList<BrowserEventProjection> projections,
        long sessionDurationNanoseconds)
    {
        var navigationGroups = projections
            .Where(item =>
                item.Event.Channel == "browser.navigation" &&
                !string.IsNullOrWhiteSpace(item.NavigationId))
            .GroupBy(
                item => (item.BrowserInstanceId, item.NavigationId),
                BrowserNavigationKeyComparer.Instance)
            .Select(group =>
            {
                var started = group
                    .Where(item => item.Event.EventType == "navigation-started")
                    .MinBy(item => item.Event.MonotonicNanoseconds);
                var completed = group
                    .Where(item => item.Event.EventType == "navigation-completed")
                    .MaxBy(item => item.Event.MonotonicNanoseconds);
                return started is null
                    ? null
                    : new NavigationPair(started, completed);
            })
            .Where(item => item is not null)
            .Cast<NavigationPair>()
            .OrderBy(item => item.Started.Event.MonotonicNanoseconds)
            .ToArray();

        var results = new List<BrowserNavigationCorrelation>(navigationGroups.Length);
        for (var index = 0; index < navigationGroups.Length; index++)
        {
            var pair = navigationGroups[index];
            var identity = pair.Completed ?? pair.Started;
            var start = pair.Started.Event.MonotonicNanoseconds;
            var end = FindEnd(navigationGroups, index, sessionDurationNanoseconds);
            var token = identity.DocumentToken;
            var documentKeys = projections
                .Where(item =>
                    SameBrowser(item, identity) &&
                    !string.IsNullOrWhiteSpace(token) &&
                    string.Equals(
                        item.DocumentToken,
                        token,
                        StringComparison.Ordinal) &&
                    item.ProcessId is not null &&
                    !string.IsNullOrWhiteSpace(item.DocumentId))
                .Select(item => (item.ProcessId!.Value, item.DocumentId!))
                .ToHashSet();

            var related = projections
                .Where(item =>
                    item.Event.MonotonicNanoseconds >= start &&
                    item.Event.MonotonicNanoseconds < end &&
                    SameBrowser(item, identity) &&
                    IsIdentityMatch(item, pair, token, documentKeys))
                .ToArray();
            var basis = !string.IsNullOrWhiteSpace(token)
                ? documentKeys.Count > 0
                    ? "document token and renderer document identity"
                    : "document token"
                : "navigation time window only";

            results.Add(new BrowserNavigationCorrelation(
                pair.Started.NavigationId!,
                identity.Url ?? pair.Started.Url ?? "(URL not recorded)",
                start,
                end,
                pair.Completed?.Event.MonotonicNanoseconds,
                identity.PrimaryPage || pair.Started.PrimaryPage,
                identity.SameDocument || pair.Started.SameDocument,
                identity.Committed,
                identity.Outcome,
                token,
                identity.RendererProcessId,
                basis,
                related.Where(item =>
                    item.Event.EventType == "dom-checkpoint-completed").Sum(item => item.Weight),
                related.Where(item =>
                    item.Event.EventType == "dom-checkpoint-node").Sum(item => item.Weight),
                related.Where(item =>
                    item.Event.EventType == "dom-checkpoint-completed" &&
                    item.Truncated).Sum(item => item.Weight),
                related.Where(item =>
                    item.Event.EventType ==
                    "accessibility-checkpoint-completed").Sum(item => item.Weight),
                related.Where(item =>
                    item.Event.EventType ==
                    "accessibility-checkpoint-node").Sum(item => item.Weight),
                related.Where(item =>
                    item.Event.EventType ==
                    "accessibility-checkpoint-completed" &&
                    item.Truncated).Sum(item => item.Weight),
                related.Where(item =>
                    item.Event.EventType == "dispatch-started").Sum(item => item.Weight),
                related.Where(item =>
                    item.Event.EventType == "listener-invoked").Sum(item => item.Weight),
                related.Sum(item => item.Weight))
            {
                BrowserInstanceId = identity.BrowserInstanceId,
                FrameType = identity.FrameType ?? pair.Started.FrameType
            });
        }

        return results;
    }

    private static long FindEnd(
        IReadOnlyList<NavigationPair> navigations,
        int currentIndex,
        long sessionDurationNanoseconds)
    {
        var currentPair = navigations[currentIndex];
        var current = currentPair.Completed ?? currentPair.Started;
        for (var index = currentIndex + 1; index < navigations.Count; index++)
        {
            var candidatePair = navigations[index];
            var candidate = candidatePair.Completed ?? candidatePair.Started;
            if (!SameBrowser(candidate, current))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current.FrameId) &&
                string.Equals(
                    candidate.FrameId,
                    current.FrameId,
                    StringComparison.Ordinal))
            {
                return candidatePair.Started.Event.MonotonicNanoseconds;
            }

            if (string.IsNullOrWhiteSpace(current.FrameId) &&
                IsPageNavigation(current) &&
                IsPageNavigation(candidate))
            {
                return candidatePair.Started.Event.MonotonicNanoseconds;
            }

            if (string.IsNullOrWhiteSpace(current.FrameId) &&
                !IsPageNavigation(current) &&
                string.Equals(
                    candidate.DocumentId,
                    current.DocumentId,
                    StringComparison.Ordinal))
            {
                return candidatePair.Started.Event.MonotonicNanoseconds;
            }
        }

        return Math.Max(
            current.Event.MonotonicNanoseconds + 1,
            sessionDurationNanoseconds + 1);
    }

    // A navigation of the tab's top-level page. Every iframe in the page is
    // also in the primary page, so primaryPage alone does not say this.
    private static bool IsPageNavigation(BrowserEventProjection item) =>
        BrowserNavigationKinds.Classify(item.FrameType, item.PrimaryPage, item.Url) ==
            BrowserNavigationKind.Page;

    private static bool IsIdentityMatch(
        BrowserEventProjection item,
        NavigationPair navigation,
        string? documentToken,
        IReadOnlySet<(int ProcessId, string DocumentId)> documentKeys)
    {
        if (item.NavigationId == navigation.Started.NavigationId &&
            item.Event.Channel == "browser.navigation")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(documentToken))
        {
            return false;
        }

        if (string.Equals(item.DocumentToken, documentToken, StringComparison.Ordinal))
        {
            return true;
        }

        return item.ProcessId is not null &&
            !string.IsNullOrWhiteSpace(item.DocumentId) &&
            documentKeys.Contains((item.ProcessId.Value, item.DocumentId));
    }

    private static bool SameBrowser(
        BrowserEventProjection left,
        BrowserEventProjection right) =>
        string.Equals(
            left.BrowserInstanceId,
            right.BrowserInstanceId,
            StringComparison.Ordinal);

    private static string? ReadString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static int? ReadInt32(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetInt32(out var result)
            ? result
            : null;

    private static bool? ReadBoolean(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var item) &&
        item.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? item.GetBoolean()
            : null;

    private sealed record NavigationPair(
        BrowserEventProjection Started,
        BrowserEventProjection? Completed);

    private sealed class BrowserNavigationKeyComparer :
        IEqualityComparer<(string? BrowserInstanceId, string? NavigationId)>
    {
        public static BrowserNavigationKeyComparer Instance { get; } = new();

        public bool Equals(
            (string? BrowserInstanceId, string? NavigationId) left,
            (string? BrowserInstanceId, string? NavigationId) right) =>
            string.Equals(
                left.BrowserInstanceId,
                right.BrowserInstanceId,
                StringComparison.Ordinal) &&
            string.Equals(
                left.NavigationId,
                right.NavigationId,
                StringComparison.Ordinal);

        public int GetHashCode(
            (string? BrowserInstanceId, string? NavigationId) value) =>
            HashCode.Combine(
                value.BrowserInstanceId is null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(value.BrowserInstanceId),
                value.NavigationId is null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(value.NavigationId));
    }
}
