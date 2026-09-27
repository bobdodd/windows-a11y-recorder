namespace Recorder.Session;

/// <summary>
/// The evidence a navigation's first frame was chosen from.
/// </summary>
public enum BrowserNavigationFrameBasis
{
    /// <summary>No first frame was determined.</summary>
    None,

    /// <summary>
    /// The first captured frame composed at or after the time Chromium
    /// reported presenting the navigation document's first rendering update.
    /// </summary>
    PresentationFeedback,

    /// <summary>
    /// No rendering update of the navigation's document was reported as
    /// presented before the next navigation: the first captured frame
    /// composed at or after the navigation completed.
    /// </summary>
    NavigationCompletion
}

/// <summary>
/// A layout checkpoint whose rendering update Chromium reported as presented,
/// with the presentation time on the session clock.
/// </summary>
public sealed record BrowserPresentedCheckpoint(
    string BrowserInstanceId,
    string DocumentToken,
    long CheckpointNanoseconds,
    long PresentedNanoseconds);

/// <summary>
/// A captured desktop frame: the time of its event, which playback seeks to,
/// and when the Windows compositor composed it.
/// </summary>
public sealed record CapturedFrameComposition(
    long FrameNanoseconds,
    long CompositedNanoseconds);

/// <summary>
/// Chooses, for each navigation, the first captured frame that can show its
/// page. A navigation's start and completion come before its page is drawn:
/// the address bar and title can change a frame or more before the content,
/// and a same-document navigation completes within milliseconds. The first
/// rendering update of the navigation's document after it started, and the
/// time Chromium reported presenting it, bound the earliest frame that can
/// show the page.
/// </summary>
public static class BrowserNavigationFrames
{
    /// <summary>
    /// Returns the navigations with their first frames. For each navigation,
    /// the checkpoint is the first presented layout checkpoint of the same
    /// browser instance and document token at or after the navigation
    /// started and before its end. The frame is the first whose composition
    /// time is at or after that checkpoint's presentation time. Without such
    /// a checkpoint, the frame is the first composed at or after the
    /// navigation completed.
    /// </summary>
    public static IReadOnlyList<BrowserNavigationCorrelation> Apply(
        IReadOnlyList<BrowserNavigationCorrelation> navigations,
        IEnumerable<BrowserPresentedCheckpoint> checkpoints,
        IEnumerable<CapturedFrameComposition> frames)
    {
        ArgumentNullException.ThrowIfNull(navigations);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(frames);

        var byDocument = checkpoints
            .GroupBy(item => (item.BrowserInstanceId, item.DocumentToken))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(item => item.CheckpointNanoseconds)
                    .ThenBy(item => item.PresentedNanoseconds)
                    .ToArray());
        var composed = frames
            .OrderBy(item => item.CompositedNanoseconds)
            .ThenBy(item => item.FrameNanoseconds)
            .ToArray();
        var compositionTimes = composed
            .Select(item => item.CompositedNanoseconds)
            .ToArray();

        var results = new BrowserNavigationCorrelation[navigations.Count];
        for (var index = 0; index < navigations.Count; index++)
        {
            var navigation = navigations[index];
            var presented = FirstPresented(navigation, byDocument);
            long? frame = presented is { } presentedAt
                ? FirstComposedAtOrAfter(presentedAt, composed, compositionTimes)
                : null;
            var basis = BrowserNavigationFrameBasis.PresentationFeedback;
            if (frame is null && presented is null && navigation.CompletedNanoseconds is { } completed)
            {
                frame = FirstComposedAtOrAfter(completed, composed, compositionTimes);
                basis = BrowserNavigationFrameBasis.NavigationCompletion;
            }

            results[index] = navigation with
            {
                FirstFrameNanoseconds = frame,
                FirstFrameBasis = frame is null ? BrowserNavigationFrameBasis.None : basis
            };
        }

        return results;
    }

    private static long? FirstPresented(
        BrowserNavigationCorrelation navigation,
        IReadOnlyDictionary<(string, string), BrowserPresentedCheckpoint[]> byDocument)
    {
        if (navigation.BrowserInstanceId is not { } instance ||
            string.IsNullOrWhiteSpace(navigation.DocumentToken) ||
            !byDocument.TryGetValue((instance, navigation.DocumentToken), out var candidates))
        {
            return null;
        }

        var low = 0;
        var high = candidates.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (candidates[middle].CheckpointNanoseconds < navigation.StartNanoseconds)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low < candidates.Length &&
            candidates[low].CheckpointNanoseconds < navigation.EndNanoseconds
                ? candidates[low].PresentedNanoseconds
                : null;
    }

    private static long? FirstComposedAtOrAfter(
        long time,
        IReadOnlyList<CapturedFrameComposition> composed,
        long[] compositionTimes)
    {
        var index = Array.BinarySearch(compositionTimes, time);
        if (index < 0)
        {
            index = ~index;
        }
        else
        {
            // Equal times: the first of them.
            while (index > 0 && compositionTimes[index - 1] == time)
            {
                index--;
            }
        }

        return index < composed.Count ? composed[index].FrameNanoseconds : null;
    }
}
