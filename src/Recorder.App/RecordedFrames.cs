using Recorder.Database.RecordingFiles;
using Recorder.Recreation;

namespace Recorder.App;

/// <summary>
/// The frames of a recorded page at a frame of the recording, as the
/// recreation writes them (slice 5b): each frame's chosen document, why it
/// was chosen, its state's basis, and its fonts and images. See
/// docs/architecture/page-recreation.md, "Build plan for 5b".
/// </summary>
internal static class RecordedFrames
{
    public static RecordedFrame[] Read(
        RecordingFileDocuments documents,
        IReadOnlyList<RecordedFrameAt> frames,
        long frameNanoseconds,
        Func<long, string> formatTime,
        CancellationToken cancellationToken = default)
    {
        var read = new List<RecordedFrame>();
        try
        {
            foreach (var frame in frames)
            {
                read.Add(Read(documents, frame, frameNanoseconds, formatTime, cancellationToken));
            }
            return [.. read];
        }
        catch
        {
            RecordedFrame.DisposeAll(read);
            throw;
        }
    }

    private static RecordedFrame Read(
        RecordingFileDocuments documents,
        RecordedFrameAt frame,
        long frameNanoseconds,
        Func<long, string> formatTime,
        CancellationToken cancellationToken)
    {
        var choice = frame.Choice switch
        {
            null => "no document of the frame was recorded by the frame's composition",
            { Committed: true } chosen => $"committed by the navigation at {formatTime(chosen.From)}",
            var chosen => $"counted from its first DOM walk at {formatTime(chosen.From)}, as no navigation committed it",
        };
        var basis = frame.State?.Basis is { Basis: "presented", PresentedTime: { } presented }
            ? $"its state is the one after its last rendering update drawn at or before the frame, drawn at {formatTime(presented)}"
            : "no rendering update of it was drawn at or before the frame, so its state is the one at the frame's composition time";
        var children = Read(documents, frame.Children, frameNanoseconds, formatTime, cancellationToken);
        try
        {
            // The fonts and images of a document with a DOM walk, which a
            // served frame or a frame built in place is answered from.
            var resources = frame.Omitted is null && frame.State is { State.Dom: not null } state
                ? documents.Resources(state.Key, state.Basis.CutTime, cancellationToken, documents.CompositionTime(frameNanoseconds))
                : null;
            return new RecordedFrame(
                frame.Owner.OwnerNodeId,
                frame.Owner.FrameToken,
                frame.Choice?.Document.DocumentKey,
                frame.Url,
                choice,
                frame.State?.State,
                basis,
                frame.SameProcessAsParent,
                children,
                frame.Omitted)
            {
                Resources = resources,
            };
        }
        catch
        {
            RecordedFrame.DisposeAll(children);
            throw;
        }
    }
}
