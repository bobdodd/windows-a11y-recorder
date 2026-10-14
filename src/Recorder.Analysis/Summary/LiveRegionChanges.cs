using System.Globalization;
using System.Text;
using System.Text.Json;
using Recorder.Session;

namespace Recorder.Analysis.Summary;

/// <summary>
/// A change the page made to a live region, as the recorded DOM shows it:
/// its time, its record, its document, the region, its politeness, what
/// changed, and the text the page put there, verbatim. Whether a screen
/// reader spoke it is not recorded here.
/// </summary>
public sealed record LiveRegionFact(long Time, string? EventId, string DocumentToken, string Region, string Politeness, string Change, string Text);

/// <summary>
/// Finds the page's changes to live regions in its recorded DOM records,
/// in record order, through <see cref="DomTreeRebuilder"/>. A live region
/// is the nearest element, the changed node or an ancestor, with an
/// aria-live attribute or a live region role: alert, log, marquee, status,
/// or timer (WAI-ARIA 1.2, https://www.w3.org/TR/wai-aria-1.2/#live_region_roles),
/// or an output element, whose role is status
/// (https://www.w3.org/TR/html-aam-1.0/#el-output). Its politeness is its
/// aria-live value where it has one, otherwise its role's implicit value:
/// assertive for alert, polite for log and status, off for marquee and
/// timer (https://www.w3.org/TR/wai-aria-1.2/#aria-live). Only polite and
/// assertive regions are counted, and only the changes of aria-relevant's
/// default, "additions text": nodes inserted into a region, and text
/// changed in one. An alert inserted with its text is counted too, as an
/// alert is a region whose appearance is announced. Removals, and content
/// shown by changing an attribute, are not counted.
/// </summary>
public sealed class LiveRegionChanges
{
    private readonly DomTreeRebuilder _rebuilder = new();
    private readonly Dictionary<string, (long Node, long Container)> _insertions = new(StringComparer.Ordinal);
    private readonly List<LiveRegionFact> _facts = [];
    private (long Time, string? EventId, string Token, string Key)? _current;

    public LiveRegionChanges()
    {
        _rebuilder.InsertionCompleted = (payload, insertion) =>
        {
            if (_current is not { } current ||
                !_insertions.Remove(current.Key + " " + insertion.Id, out var inserted) ||
                !insertion.Tree.Nodes.TryGetValue(inserted.Node, out var node))
            {
                return;
            }

            var text = TextOf(insertion.Tree, node);
            if (text.Length == 0)
            {
                return;
            }

            if (Region(insertion.Tree, inserted.Container) is { } region && Counted(region.Politeness))
            {
                _facts.Add(new LiveRegionFact(current.Time, current.EventId, current.Token, region.Text, region.Politeness, "added", text));
                return;
            }

            if (Alert(insertion.Tree, node) is { } alert)
            {
                var alertText = TextOf(insertion.Tree, alert.Node);
                if (alertText.Length > 0)
                {
                    _facts.Add(new LiveRegionFact(current.Time, current.EventId, current.Token, alert.Text, alert.Politeness, "alert", alertText));
                }
            }
        };
    }

    public IReadOnlyList<LiveRegionFact> Facts => _facts;

    /// <summary>Reads one browser.dom record.</summary>
    public void Add(long time, string? eventId, string eventType, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || DomTreeRebuilder.DocumentKey(payload) is not { } key ||
            LayoutChangeState.DocumentToken(payload) is not { } token)
        {
            return;
        }

        _current = (time, eventId, token, key);
        if (eventType == "dom-node-inserted" &&
            payload.TryGetProperty("transitionId", out var transition) && transition.ValueKind == JsonValueKind.String &&
            payload.TryGetProperty("nodeId", out var nodeId) && nodeId.ValueKind == JsonValueKind.Number &&
            payload.TryGetProperty("containerNodeId", out var container) && container.ValueKind == JsonValueKind.Number)
        {
            _insertions[key + " " + transition.GetString()] = (nodeId.GetInt64(), container.GetInt64());
        }

        string? previous = null;
        if (eventType == "dom-character-data-changed" && _rebuilder.Documents.TryGetValue(key, out var before) &&
            payload.TryGetProperty("nodeId", out var changed) && changed.ValueKind == JsonValueKind.Number &&
            before.Nodes.TryGetValue(changed.GetInt64(), out var changedNode))
        {
            previous = changedNode.Data;
        }

        _rebuilder.Apply(eventType, payload);
        if (eventType == "dom-character-data-changed" && _rebuilder.Documents.TryGetValue(key, out var tree) &&
            payload.TryGetProperty("nodeId", out var id) && id.ValueKind == JsonValueKind.Number &&
            tree.Nodes.TryGetValue(id.GetInt64(), out var node) && node.ParentId is { } parent &&
            node.Data != previous && Region(tree, parent) is { } region && Counted(region.Politeness))
        {
            var text = Clean(node.Data);
            if (text.Length > 0)
            {
                _facts.Add(new LiveRegionFact(time, eventId, token, region.Text, region.Politeness, "changed", text));
            }
        }

        _current = null;
    }

    private static bool Counted(string politeness) => politeness is "polite" or "assertive";

    // The nearest live region at or above a node, described for a reader.
    private static (DomNode Node, string Text, string Politeness)? Region(DomDocumentTree tree, long start)
    {
        var seen = new HashSet<long>();
        for (var id = (long?)start; id is { } current && seen.Add(current) && tree.Nodes.TryGetValue(current, out var node); id = node.ParentId)
        {
            if (Live(node) is { } live)
            {
                return live;
            }
        }

        return null;
    }

    // The first alert in an inserted subtree, the node itself first.
    private static (DomNode Node, string Text, string Politeness)? Alert(DomDocumentTree tree, DomNode root)
    {
        var pending = new Stack<DomNode>([root]);
        var seen = new HashSet<long>();
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node.Id))
            {
                continue;
            }

            if (Live(node) is { } live && Role(node) == "alert" && Counted(live.Politeness))
            {
                return live;
            }

            foreach (var child in Enumerable.Reverse(node.Children))
            {
                if (tree.Nodes.TryGetValue(child, out var item))
                {
                    pending.Push(item);
                }
            }
        }

        return null;
    }

    private static readonly string[] LiveRoles = ["alert", "log", "marquee", "status", "timer"];

    private static string? Role(DomNode node) =>
        node.Attributes.TryGetValue("role", out var role) && role is not null
            ? role.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(token => token.ToLowerInvariant()).FirstOrDefault(LiveRoles.Contains)
            : null;

    private static (DomNode Node, string Text, string Politeness)? Live(DomNode node)
    {
        if (node.NodeType != "element")
        {
            return null;
        }

        var role = Role(node);
        var name = (node.NodeName ?? "element").ToLowerInvariant();
        var id = node.Attributes.TryGetValue("id", out var value) && value is { Length: > 0 } && value != DomTreeRebuilder.Cut ? $" with id \"{value}\"" : "";
        if (node.Attributes.TryGetValue("aria-live", out var live) && live is not null)
        {
            var politeness = live.Trim().ToLowerInvariant() is "polite" or "assertive" ? live.Trim().ToLowerInvariant() : "off";
            var roleText = role is null ? "" : $" and role \"{role}\"";
            return (node, $"a {name} element{id} with aria-live \"{live.Trim()}\"{roleText}", politeness);
        }

        if (role is not null)
        {
            var politeness = role switch { "alert" => "assertive", "log" or "status" => "polite", _ => "off" };
            return (node, $"a {name} element{id} with role \"{role}\", {politeness} by its role", politeness);
        }

        if (name == "output")
        {
            return (node, $"an output element{id}, whose role is status, polite by its role", "polite");
        }

        return null;
    }

    private static string TextOf(DomDocumentTree tree, DomNode root)
    {
        var builder = new StringBuilder();
        var pending = new Stack<DomNode>([root]);
        var seen = new HashSet<long>();
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node.Id))
            {
                continue;
            }

            if (node.NodeType == "text" && node.Data is { } data)
            {
                builder.Append(data == DomTreeRebuilder.Cut ? " (text cut by the recorder) " : data);
            }

            var name = node.NodeName?.ToLowerInvariant();
            if (name is "script" or "style" or "template")
            {
                continue;
            }

            foreach (var child in Enumerable.Reverse(node.Children))
            {
                if (tree.Nodes.TryGetValue(child, out var item))
                {
                    pending.Push(item);
                }
            }
        }

        return Clean(builder.ToString());
    }

    private static string Clean(string? text) =>
        text is null || text == DomTreeRebuilder.Cut
            ? text is null ? "" : "(text cut by the recorder)"
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The reason a moment was chosen for a change, as stated in the report and to the model.</summary>
    public static string Reason(LiveRegionFact fact)
    {
        var quoted = "\"" + fact.Text.Replace("\"", "'", StringComparison.Ordinal) + "\"";
        return fact.Change switch
        {
            "alert" => string.Create(CultureInfo.InvariantCulture, $"The page inserted an alert, {fact.Region}, with the text {quoted}."),
            "changed" => string.Create(CultureInfo.InvariantCulture, $"The page changed text in a live region, {fact.Region}, to {quoted}."),
            _ => string.Create(CultureInfo.InvariantCulture, $"The page added content to a live region, {fact.Region}, with the text {quoted}.")
        };
    }
}
