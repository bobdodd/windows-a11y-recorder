namespace Recorder.Session;

/// <summary>
/// An owner element of a document and the documents recorded in the frame it
/// held (protocol 0.55, slice 5a). Documents are those whose DOM walks name
/// the owner's frame token, in any renderer process, in order of their first
/// record. Which of them the frame shows at a time is chosen in slice 5b.
/// </summary>
public sealed record FrameOwnerJoin(
    BrowserDocumentState Parent,
    FrameOwnerState Owner,
    IReadOnlyList<BrowserDocumentState> Documents);

/// <summary>
/// Joins the frame owner elements of recorded documents to the documents of
/// their frames by DevTools frame token (protocol 0.55, slice 5a). The token
/// names one frame in every renderer, so the parent's renderer and an out of
/// process child's renderer give it the same token. Nothing is inferred from
/// addresses, so a recording before protocol 0.55 joins nothing.
/// </summary>
public static class BrowserFrames
{
    /// <summary>The owners of a document that hold a frame, each with the documents recorded in it.</summary>
    public static IReadOnlyList<FrameOwnerJoin> OwnersOf(
        BrowserDocumentState parent,
        IEnumerable<BrowserDocumentState> documents)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(documents);
        var byToken = ByFrameToken(documents);
        return parent.Frames.Owners.Values
            .OrderBy(owner => owner.OwnerNodeId)
            .Select(owner => new FrameOwnerJoin(
                parent,
                owner,
                byToken.TryGetValue(owner.FrameToken, out var found) ? found : []))
            .ToList();
    }

    /// <summary>The documents recorded in each frame, by frame token, each list in order of first record.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<BrowserDocumentState>> ByFrameToken(
        IEnumerable<BrowserDocumentState> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return documents
            .Where(document => document.Frames.FrameToken is not null)
            .GroupBy(document => document.Frames.FrameToken!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BrowserDocumentState>)group
                    .OrderBy(document => document.FirstEventKey)
                    .ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// The owner elements, in any recorded document, that held the frame of a
    /// document, at their latest record. More than one is possible only when
    /// the recorded documents come from different times.
    /// </summary>
    public static IReadOnlyList<(BrowserDocumentState Parent, FrameOwnerState Owner)> OwnersOfFrame(
        BrowserDocumentState child,
        IEnumerable<BrowserDocumentState> documents)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(documents);
        if (child.Frames.FrameToken is not { } token)
        {
            return [];
        }
        return documents
            .SelectMany(parent => parent.Frames.Owners.Values
                .Where(owner => owner.FrameToken == token)
                .Select(owner => (parent, owner)))
            .OrderBy(item => item.parent.FirstEventKey)
            .ThenBy(item => item.owner.OwnerNodeId)
            .ToList();
    }
}
