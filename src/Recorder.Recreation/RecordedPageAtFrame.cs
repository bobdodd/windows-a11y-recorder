using Recorder.Database.RecordingFiles;
using Recorder.Session;

namespace Recorder.Recreation;

/// <summary>
/// Writes the recreation of a recorded page at a frame of the recording, as
/// the player's Inspect page at this frame opens it, and as the analysis
/// worker rebuilds it for a moment of the summary with vision. Moved from
/// the player on 2026-10-14, so that both use the same code. See
/// docs/architecture/recording-summary.md, decision 7.
/// </summary>
public static class RecordedPageAtFrame
{
    /// <summary>
    /// The page's content at the frame: its state, popups, fonts and images,
    /// frames, and recorded preferences. Each step's time is added to the
    /// timings. The content holds the resources it reads; the recreation
    /// server disposes of them once it holds the content.
    /// </summary>
    public static RecreationContent Content(
        RecordingFileDocuments documents,
        BrowserPreferenceTimeline browserPreferences,
        RecordedDocumentChoice chosen,
        long frame,
        Func<long, string> formatTime,
        List<RecreationTiming> timings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(browserPreferences);
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(formatTime);
        ArgumentNullException.ThrowIfNull(timings);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var found = documents.Document(chosen.Key, frame, cancellationToken)
            ?? throw new InvalidDataException("The page's state at this frame could not be read from the recording.");
        timings.Add(new RecreationTiming("Reading the page's state at the frame from the recording", Math.Round(clock.Elapsed.TotalMilliseconds, 1)));
        clock.Restart();
        if (found.State!.Dom is null)
        {
            throw new InvalidDataException("No DOM walk of this page was recorded at or before this frame, so it cannot be recreated here. A page can be drawn before its first DOM walk; a later frame may have one.");
        }
        var basis = found.Basis is { Basis: "presented", PresentedTime: { } presented }
            ? $"the state after the page's last rendering update drawn at or before the frame, drawn at {formatTime(presented)}"
            : "no rendering update of the page was drawn at or before the frame, so this is its state at the frame's composition time";
        // Slice 4d sub-step 2: the page popups the page owned open at the
        // frame, each at its own popup widget's last presented rendering
        // update.
        var popups = documents.Popups(chosen.Key, frame, cancellationToken)
            .Select(item => new RecordedPopup(
                item.Popup,
                item.State?.State,
                item.State?.Basis is { Basis: "presented", PresentedTime: { } popupPresented }
                    ? $"its state is the one after its last rendering update drawn at or before the frame, drawn at {formatTime(popupPresented)}"
                    : "no rendering update of it was drawn at or before the frame, so its state is the one at the frame's composition time"))
            .ToArray();
        timings.Add(new RecreationTiming("Reading the page's popups at the frame from the recording", Math.Round(clock.Elapsed.TotalMilliseconds, 1)));
        clock.Restart();
        // Slice 4b sub-step 2a: the frame each animated image is held at,
        // chosen at the frame's composition.
        var resources = documents.Resources(chosen.Key, found.Basis.CutTime, cancellationToken, compositionNanoseconds: documents.CompositionTime(frame));
        var resourcesMilliseconds = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
        var framesMilliseconds = resources.ImageFramesMilliseconds ?? 0;
        timings.Add(new RecreationTiming("Reading the page's fonts and images from the recording", Math.Round(resourcesMilliseconds - framesMilliseconds, 1)));
        if (resources.ImageFramesMilliseconds is { } imageFrames)
        {
            timings.Add(new RecreationTiming("Choosing the frame of each animated image and the compositor values from the recording's compositor records", imageFrames));
        }
        clock.Restart();
        RecordedFrame[] frames = [];
        try
        {
            // Slice 5b: the page's frames, each with the document it showed
            // at the frame, its state, and its fonts and images.
            frames = RecordedFrames.Read(documents, documents.Frames(chosen.Key, found.State, frame, cancellationToken), frame, formatTime, cancellationToken);
            timings.Add(new RecreationTiming("Reading the page's frames, their states, and their fonts and images from the recording", Math.Round(clock.Elapsed.TotalMilliseconds, 1)));
            clock.Restart();
            // Accessibility preferences stage 3: the page's recorded values
            // at the state's time.
            var pageValues = browserPreferences.PageValuesAt(
                documents.PageFrameTreeNodeIdOf(chosen.Key),
                chosen.Url,
                found.Basis.CutTime);
            // Protocol 0.59: the participant's browser theme at the same
            // time, for the recreation browser's window.
            var browserTheme = browserPreferences.ThemeAt(found.Basis.CutTime);
            var written = RecordedPage.Content(found.State, chosen.Url, frame, found.Basis.CutTime, basis, resources, popups, frames: frames, preferences: pageValues, browserTheme: browserTheme);
            timings.Add(new RecreationTiming("Writing the page", Math.Round(clock.Elapsed.TotalMilliseconds, 1)));
            return written;
        }
        catch
        {
            resources.Dispose();
            RecordedFrame.DisposeAll(frames);
            throw;
        }
    }
}
