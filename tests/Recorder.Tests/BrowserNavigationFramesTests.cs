using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// A navigation's first frame is the first captured frame composed after
/// Chromium presented the navigation document's first rendering update.
/// </summary>
public sealed class BrowserNavigationFramesTests
{
    [Fact]
    public void ChoosesTheFrameComposedAfterTheFirstPresentedUpdateOfTheDocument()
    {
        var navigation = Navigation("browser-1", "TOKEN-1", start: 1_000, end: 5_000, completed: 1_005);
        var result = Assert.Single(BrowserNavigationFrames.Apply(
            [navigation],
            [
                // Before the navigation started.
                new BrowserPresentedCheckpoint("browser-1", "TOKEN-1", 900, 950),
                // Another browser instance and another document.
                new BrowserPresentedCheckpoint("browser-2", "TOKEN-1", 1_100, 1_150),
                new BrowserPresentedCheckpoint("browser-1", "TOKEN-2", 1_100, 1_150),
                // The first of the document's updates after the start.
                new BrowserPresentedCheckpoint("browser-1", "TOKEN-1", 1_200, 3_500),
                new BrowserPresentedCheckpoint("browser-1", "TOKEN-1", 1_300, 1_400)
            ],
            [
                new CapturedFrameComposition(4_000, 3_990),
                new CapturedFrameComposition(1_000, 990),
                // Composed after the update's checkpoint but before it was
                // presented: the previous page.
                new CapturedFrameComposition(3_000, 2_900),
                new CapturedFrameComposition(3_600, 3_500)
            ]));

        Assert.Equal(3_600, result.FirstFrameNanoseconds);
        Assert.Equal(3_600, result.SeekNanoseconds);
        Assert.Equal(BrowserNavigationFrameBasis.PresentationFeedback, result.FirstFrameBasis);
        Assert.DoesNotContain("[No render evidence]", result.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesTheFrameAfterCompletionWhenNoUpdateWasPresentedBeforeTheNextNavigation()
    {
        var navigation = Navigation("browser-1", "TOKEN-1", start: 5_000, end: 9_000, completed: 5_005);
        var result = Assert.Single(BrowserNavigationFrames.Apply(
            [navigation],
            [new BrowserPresentedCheckpoint("browser-1", "TOKEN-1", 9_000, 9_100)],
            [
                new CapturedFrameComposition(5_000, 4_990),
                new CapturedFrameComposition(6_000, 5_990),
                new CapturedFrameComposition(9_200, 9_150)
            ]));

        Assert.Equal(6_000, result.FirstFrameNanoseconds);
        Assert.Equal(BrowserNavigationFrameBasis.NavigationCompletion, result.FirstFrameBasis);
        Assert.StartsWith("00:00:00.000 | [No render evidence] https://example.test/", result.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void GoesToTheStartWhenNoFrameIsMatched()
    {
        var incomplete = Navigation("browser-1", null, start: 1_000, end: 5_000, completed: null);
        var presentedAfterTheLastFrame = Navigation("browser-1", "TOKEN-1", start: 6_000, end: 9_000, completed: 6_005);
        var results = BrowserNavigationFrames.Apply(
            [incomplete, presentedAfterTheLastFrame],
            [new BrowserPresentedCheckpoint("browser-1", "TOKEN-1", 6_100, 8_000)],
            [new CapturedFrameComposition(7_000, 6_990)]);

        Assert.All(results, result =>
        {
            Assert.Null(result.FirstFrameNanoseconds);
            Assert.Equal(BrowserNavigationFrameBasis.None, result.FirstFrameBasis);
            Assert.Equal(result.StartNanoseconds, result.SeekNanoseconds);
        });
    }

    private static BrowserNavigationCorrelation Navigation(
        string browserInstanceId,
        string? documentToken,
        long start,
        long end,
        long? completed) =>
        new(
            "navigation-1",
            "https://example.test/",
            start,
            end,
            completed,
            PrimaryPage: true,
            SameDocument: true,
            Committed: completed is null ? null : true,
            Outcome: completed is null ? null : "committed",
            documentToken,
            RendererProcessId: 4100,
            "document token",
            0, 0, 0, 0, 0, 0, 0, 0, 0)
        {
            BrowserInstanceId = browserInstanceId
        };
}
