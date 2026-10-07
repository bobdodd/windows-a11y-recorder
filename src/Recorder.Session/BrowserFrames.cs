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

    /// <summary>
    /// The document a frame shows at a time (slice 5b): of the documents
    /// whose DOM walks named the frame's token, the one committed last at or
    /// before the time, by a navigation-completed record with its document
    /// token. A document no navigation committed by then counts from its
    /// first DOM walk, as a frame's initial empty document does. Of two
    /// documents that count from the same time, as two documents recorded
    /// under one document token can, the one with the later first walk is
    /// chosen, then the later key. Null when no document counts by then.
    /// </summary>
    /// <param name="commits">The times of the navigation-completed records, by document token, in any order.</param>
    public static FrameDocumentChoice? Choose(
        string frameToken,
        long time,
        IEnumerable<FrameDocumentRecord> documents,
        IReadOnlyDictionary<string, IReadOnlyList<long>> commits)
    {
        ArgumentNullException.ThrowIfNull(frameToken);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(commits);
        FrameDocumentChoice? chosen = null;
        foreach (var document in documents)
        {
            if (document.FrameToken != frameToken)
            {
                continue;
            }
            var token = document.DocumentKey.Split(' ', 2)[0];
            long? committed = commits.TryGetValue(token, out var times)
                ? times.Where(item => item <= time).Select(item => (long?)item).Max()
                : null;
            if (committed is null && document.Time > time)
            {
                continue;
            }
            var candidate = new FrameDocumentChoice(document, committed ?? document.Time, committed is not null);
            if (chosen is null ||
                candidate.From > chosen.From ||
                (candidate.From == chosen.From && candidate.Document.Time > chosen.Document.Time) ||
                (candidate.From == chosen.From && candidate.Document.Time == chosen.Document.Time &&
                    string.CompareOrdinal(candidate.Document.DocumentKey, chosen.Document.DocumentKey) > 0))
            {
                chosen = candidate;
            }
        }
        return chosen;
    }
}

/// <summary>
/// The document chosen for a frame at a time (slice 5b), the time it counts
/// from, and whether that is the time a navigation committed it.
/// </summary>
public sealed record FrameDocumentChoice(FrameDocumentRecord Document, long From, bool Committed);
