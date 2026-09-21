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
    public bool IsBrowserInternal =>
        Url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
        Url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
        Url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase);

    public string Label
    {
        get
        {
            var prefix = IsBrowserInternal ? "[Browser UI] " : string.Empty;
            return $"{FormatTime(StartNanoseconds)} | {prefix}{Url}";
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
    string? NavigationId,
    string? Url,
    bool PrimaryPage,
    bool SameDocument,
    bool? Committed,
    string? Outcome,
    bool Truncated);

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
                related.Count(item =>
                    item.Event.EventType == "dom-checkpoint-completed"),
                related.Count(item =>
                    item.Event.EventType == "dom-checkpoint-node"),
                related.Count(item =>
                    item.Event.EventType == "dom-checkpoint-completed" &&
                    item.Truncated),
                related.Count(item =>
                    item.Event.EventType ==
                    "accessibility-checkpoint-completed"),
                related.Count(item =>
                    item.Event.EventType ==
                    "accessibility-checkpoint-node"),
                related.Count(item =>
                    item.Event.EventType ==
                    "accessibility-checkpoint-completed" &&
                    item.Truncated),
                related.Count(item =>
                    item.Event.EventType == "dispatch-started"),
                related.Count(item =>
                    item.Event.EventType == "listener-invoked"),
                related.Length));
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
                current.PrimaryPage &&
                candidate.PrimaryPage)
            {
                return candidatePair.Started.Event.MonotonicNanoseconds;
            }

            if (string.IsNullOrWhiteSpace(current.FrameId) &&
                !current.PrimaryPage &&
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
