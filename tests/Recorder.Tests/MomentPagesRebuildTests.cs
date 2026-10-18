using Recorder.Analysis;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

// A fault of the browser rebuilding a moment's page is rebuilt once more in
// a new browser; where it faults again the step fails; a page that cannot be
// recreated for a reason of the recording is not built again (owner,
// 2026-10-17; target machine the same day: "The DevTools protocol
// connection closed" at moment 3 of a trial).
public sealed class MomentPagesRebuildTests
{
    private static readonly RecreatedPage Built =
        new([new PageTile(1, 0, 925, 922, "pages/tile-01.png", "C:/pages/tile-01.png", 922, 925)], 922, 925, 0, 0, 922, 925, 0, [], 0, true, null);

    private static Func<Task<(RecreatedPage? Page, string? Fault)>> Builds(List<int> calls, params (RecreatedPage? Page, string? Fault)[] results) =>
        () =>
        {
            calls.Add(calls.Count + 1);
            return Task.FromResult(results[Math.Min(calls.Count - 1, results.Length - 1)]);
        };

    [Fact]
    public async Task A_page_whose_browser_faulted_is_built_again_in_a_new_browser_and_that_page_is_kept()
    {
        var calls = new List<int>();

        var page = await RecordedMomentPages.RebuildAsync(1, 3, 19_344_538_200, Builds(calls, (null, "InvalidOperationException: The DevTools protocol connection closed."), (Built, null)));

        Assert.Same(Built, page);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task A_page_whose_browser_faulted_twice_fails_the_step_saying_why_each_build_faulted()
    {
        var calls = new List<int>();

        var failure = await Assert.ThrowsAsync<StepFailedException>(() =>
            RecordedMomentPages.RebuildAsync(1, 3, 19_344_538_200, Builds(calls, (null, "First fault."), (null, "Second fault."))));

        Assert.Equal(2, calls.Count);
        Assert.Equal(
            "The page of visit 1 at moment 3, at 00:00:19.344, could not be rebuilt: the browser rebuilding it faulted each of the 2 times. " +
            "Build 1: First fault. Build 2: Second fault.",
            failure.Message);
    }

    [Fact]
    public async Task A_page_that_cannot_be_recreated_for_a_reason_of_the_recording_is_not_built_again()
    {
        var calls = new List<int>();
        var not = RecreatedPage.Not("the recreated page measured 0 by 0 CSS pixels with a viewport 0 high, so it could not be pictured");

        var page = await RecordedMomentPages.RebuildAsync(1, 3, 19_344_538_200, Builds(calls, (not, null)));

        Assert.Same(not, page);
        Assert.Single(calls);
    }

    [Fact]
    public void A_fault_says_what_failed_what_failed_under_it_and_that_the_browser_was_not_ready()
    {
        var fault = RecordedMomentPages.Fault(
            new InvalidOperationException("The DevTools protocol connection closed.", new System.Net.WebSockets.WebSocketException("The remote party closed the WebSocket connection.")),
            null);

        Assert.Equal(
            "InvalidOperationException: The DevTools protocol connection closed. " +
            "Under it, WebSocketException: The remote party closed the WebSocket connection. It faulted before the browser was ready.",
            fault);
    }
}
