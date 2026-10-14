namespace Recorder.Analysis.Summary;

/// <summary>
/// A committed navigation of a tab's page, the primary main frame, as the
/// summary reads it from its navigation-completed record.
/// </summary>
/// <param name="Tab">The tab's page, as the record's context names it, with its browser.</param>
/// <param name="RendererInitiated">Whether Chromium classified it as started by the page's content; null when not recorded.</param>
public sealed record NavigationCommit(
    long Time,
    string EventId,
    string Tab,
    string Url,
    bool SameDocument,
    string? DocumentToken,
    bool? RendererInitiated,
    bool ErrorPage);

/// <summary>What started a visit.</summary>
public enum VisitKind
{
    /// <summary>A cross-document commit: a new document.</summary>
    Page,

    /// <summary>A same-document commit that changed the URL beyond its fragment.</summary>
    SameDocument,

    /// <summary>A fragment-only change judged to be a route of the page's own.</summary>
    FragmentRoute
}

/// <summary>A fragment-only change within a visit: an in-page jump, named from the page's structure.</summary>
/// <param name="Name">"skip link", "back to top link", "in-page link", or "in-page jump" when the link is not known.</param>
/// <param name="Rule">Why it was named so.</param>
/// <param name="Target">Where it pointed, in words.</param>
/// <param name="LinkName">The link's name, where the link is known.</param>
public sealed record FragmentStep(NavigationCommit Commit, string Fragment, string Name, string Rule, string Target, string? LinkName);

/// <summary>How a fragment-only change was judged.</summary>
/// <param name="RouteReason">Why it is a visit of its own; null for a step.</param>
public sealed record FragmentJudgement(FragmentStep? Step, string? RouteReason);

/// <summary>A page visit, derived by the recorder.</summary>
/// <param name="ReturnOf">The number of the earlier visit to the same URL, where there is one.</param>
/// <param name="RouteReason">For a fragment route, why it is not a step.</param>
public sealed record Visit(
    int Number,
    VisitKind Kind,
    NavigationCommit Start,
    long End,
    IReadOnlyList<FragmentStep> Steps,
    int? ReturnOf,
    string? RouteReason)
{
    public string Label => Kind switch
    {
        VisitKind.Page => "new document",
        VisitKind.SameDocument => "same document",
        _ => "fragment route"
    };
}

/// <summary>
/// Divides a recording into page visits. Each tab's visits start at each
/// cross-document commit, and at each same-document commit that changes
/// the URL beyond its fragment, and end at the tab's next visit or the end
/// of the recording. A change of the fragment alone is judged from the page:
/// a step of the visit, or a visit of its own. See
/// docs/architecture/recording-summary.md, "What counts as a page visit".
/// </summary>
public static class VisitDivider
{
    /// <param name="judge">
    /// Judges a fragment-only change, given the commit, the URL before it,
    /// and the time the change lasts until: the tab's next commit or the end
    /// of the recording.
    /// </param>
    public static IReadOnlyList<Visit> Divide(
        IEnumerable<NavigationCommit> commits,
        long recordingEnd,
        Func<NavigationCommit, string, long, FragmentJudgement> judge)
    {
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(judge);
        var ordered = commits.OrderBy(commit => commit.Time).ThenBy(commit => commit.EventId, StringComparer.Ordinal).ToList();
        var starts = new List<(NavigationCommit Commit, VisitKind Kind, string? Reason, List<FragmentStep> Steps)>();
        var open = new Dictionary<string, int>(StringComparer.Ordinal);
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Count; index++)
        {
            var commit = ordered[index];
            var previous = urls.GetValueOrDefault(commit.Tab);
            urls[commit.Tab] = commit.Url;
            if (!commit.SameDocument || previous is null || !open.ContainsKey(commit.Tab))
            {
                open[commit.Tab] = starts.Count;
                starts.Add((commit, VisitKind.Page, null, []));
                continue;
            }

            if (previous == commit.Url)
            {
                continue;
            }

            var (before, _) = PageSnapshot.SplitFragment(previous);
            var (after, _) = PageSnapshot.SplitFragment(commit.Url);
            if (before != after)
            {
                open[commit.Tab] = starts.Count;
                starts.Add((commit, VisitKind.SameDocument, null, []));
                continue;
            }

            var until = ordered.Skip(index + 1).FirstOrDefault(next => next.Tab == commit.Tab)?.Time ?? recordingEnd;
            var judgement = judge(commit, previous, until);
            if (judgement.Step is { } step)
            {
                starts[open[commit.Tab]].Steps.Add(step);
            }
            else
            {
                open[commit.Tab] = starts.Count;
                starts.Add((commit, VisitKind.FragmentRoute, judgement.RouteReason, []));
            }
        }

        var visits = new List<Visit>(starts.Count);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < starts.Count; index++)
        {
            var (commit, kind, reason, steps) = starts[index];
            var end = starts.Skip(index + 1).FirstOrDefault(next => next.Commit.Tab == commit.Tab).Commit?.Time ?? recordingEnd;
            int? returnOf = seen.TryGetValue(commit.Url, out var earlier) ? earlier : null;
            visits.Add(new Visit(index + 1, kind, commit, Math.Max(end, commit.Time), steps, returnOf, reason));
            seen[commit.Url] = index + 1;
        }

        return visits;
    }
}
