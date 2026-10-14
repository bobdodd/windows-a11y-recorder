using System.Globalization;
using System.Text;
using System.Text.Json;
using Recorder.Session;

namespace Recorder.Analysis.Summary;

/// <summary>
/// A node of a recorded page: from the rebuilt DOM, with Chromium's
/// accessibility record for it where one was recorded, its role name and
/// computed name.
/// </summary>
/// <param name="Kind">"element", "text", "document", or another DOM node type.</param>
/// <param name="Tag">The element's tag in lower case; empty for other nodes.</param>
/// <param name="Role">Chromium's role name, such as "main" or "heading", or null with no record.</param>
/// <param name="Name">Chromium's computed name, or null with no record.</param>
/// <param name="Text">A text node's character data.</param>
public sealed record PageNode(
    long Id,
    long? ParentId,
    string Kind,
    string Tag,
    IReadOnlyDictionary<string, string?> Attributes,
    string? Role,
    string? Name,
    string? Text,
    IReadOnlyList<long> Children);

/// <summary>Where a fragment points in a document, by HTML's rule.</summary>
public enum IndicatedPartKind
{
    /// <summary>No place: the document has no element the fragment names.</summary>
    None,

    /// <summary>The top of the document.</summary>
    Top,

    /// <summary>An element.</summary>
    Element
}

/// <param name="Rule">Which part of HTML's rule found it.</param>
public sealed record IndicatedPart(IndicatedPartKind Kind, PageNode? Element, string Rule);

/// <summary>What an in-page link is, from the page's structure.</summary>
public enum InPageLinkKind
{
    Skip,
    BackToTop,
    InPage
}

/// <param name="Rule">The rule that named it, in words.</param>
public sealed record InPageLinkName(InPageLinkKind Kind, string Rule)
{
    public string Text => Kind switch
    {
        InPageLinkKind.Skip => "skip link",
        InPageLinkKind.BackToTop => "back to top link",
        _ => "in-page link"
    };
}

/// <summary>A heading of the page: its level, where recorded, and its name.</summary>
public sealed record PageHeading(int? Level, string Name);

/// <summary>A landmark, form control, or button of the page: its role and name.</summary>
public sealed record PageItem(string Role, string Name);

/// <summary>
/// A recorded page at a time: its document tree in tree order, without
/// shadow trees, and the structure the summary reads from it. Roles and
/// names are Chromium's own where its accessibility records hold them
/// (Chromium is started with renderer accessibility on); a page without
/// them is read from its tags alone, and says so.
/// </summary>
public sealed class PageSnapshot
{
    private static readonly HashSet<string> LandmarkRoles =
        new(["banner", "navigation", "main", "complementary", "contentInfo", "search", "form", "region"], StringComparer.Ordinal);

    private static readonly HashSet<string> FieldRoles = new(
        [
            "textField", "searchBox", "textFieldWithComboBox", "comboBoxSelect", "comboBoxMenuButton", "checkBox",
            "radioButton", "slider", "spinButton", "switch", "listBox", "date", "dateTime", "inputTime", "colorWell"
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> ButtonRoles = new(["button", "toggleButton", "popUpButton"], StringComparer.Ordinal);

    private readonly List<PageNode> _order = [];
    private readonly Dictionary<long, int> _position = [];
    private readonly Dictionary<long, PageNode> _nodes;

    public PageSnapshot(string url, IEnumerable<PageNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        Url = url;
        _nodes = nodes.ToDictionary(node => node.Id);
        HasAccessibility = _nodes.Values.Any(node => node.Role is not null);
        var roots = _nodes.Values.Where(node => node.ParentId is null || !_nodes.ContainsKey(node.ParentId.Value))
            .OrderBy(node => node.Kind == "document" ? 0 : 1)
            .ThenBy(node => node.Id);
        var stack = new Stack<(PageNode Node, int Depth)>();
        foreach (var root in roots.Reverse())
        {
            stack.Push((root, 0));
        }

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (_position.ContainsKey(node.Id) || depth > 500)
            {
                continue;
            }

            _position[node.Id] = _order.Count;
            _order.Add(node);
            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                if (_nodes.TryGetValue(node.Children[index], out var child))
                {
                    stack.Push((child, depth + 1));
                }
            }
        }

        Main = _order.FirstOrDefault(node => RoleOf(node) == "main");
        FirstHeading = _order.FirstOrDefault(node => RoleOf(node) == "heading");
    }

    /// <summary>The document's URL, which in-page links are resolved against.</summary>
    public string Url { get; }

    /// <summary>The nodes in tree order.</summary>
    public IReadOnlyList<PageNode> InOrder => _order;

    /// <summary>Whether Chromium's accessibility records were recorded for the page.</summary>
    public bool HasAccessibility { get; }

    public PageNode? Main { get; }

    public PageNode? FirstHeading { get; }

    public PageNode? Node(long id) => _nodes.GetValueOrDefault(id);

    public int OrderOf(PageNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _position.TryGetValue(node.Id, out var order) ? order : -1;
    }

    /// <summary>
    /// The node's role: Chromium's where recorded, otherwise from its tag
    /// when the page has no accessibility records at all.
    /// </summary>
    public string? RoleOf(PageNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Role is not null || HasAccessibility || node.Kind != "element")
        {
            return node.Role;
        }

        return node.Tag switch
        {
            "main" => "main",
            "nav" => "navigation",
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "heading",
            "a" when node.Attributes.ContainsKey("href") => "link",
            "img" => "image",
            "button" => "button",
            "textarea" => "textField",
            "select" => "comboBoxSelect",
            "input" => InputRole(node),
            _ => node.Attributes.GetValueOrDefault("role") is { } role && role.Trim() == "main" ? "main" : null
        };
    }

    private static string? InputRole(PageNode node) =>
        (node.Attributes.GetValueOrDefault("type") ?? "text").Trim().ToLowerInvariant() switch
        {
            "hidden" => null,
            "checkbox" => "checkBox",
            "radio" => "radioButton",
            "button" or "submit" or "reset" or "image" => "button",
            "range" => "slider",
            "number" => "spinButton",
            "search" => "searchBox",
            _ => "textField"
        };

    /// <summary>The node's name: Chromium's where recorded, otherwise its text.</summary>
    public string NameOf(PageNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            return Collapse(node.Name);
        }

        if (node.Role is not null)
        {
            return "";
        }

        return node.Tag switch
        {
            "img" => Collapse(node.Attributes.GetValueOrDefault("alt") ?? ""),
            "input" => Collapse(node.Attributes.GetValueOrDefault("aria-label") ?? node.Attributes.GetValueOrDefault("placeholder") ?? ""),
            _ => Collapse(node.Attributes.GetValueOrDefault("aria-label") ?? TextOf(node, int.MaxValue).Text)
        };
    }

    public bool Contains(PageNode ancestor, PageNode node)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        ArgumentNullException.ThrowIfNull(node);
        for (var current = node; current is not null; current = current.ParentId is { } parent ? Node(parent) : null)
        {
            if (current.Id == ancestor.Id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The document's title: the root web area's name, else the title element's text.</summary>
    public string? Title
    {
        get
        {
            if (_order.FirstOrDefault(node => node.Role == "rootWebArea") is { } root && !string.IsNullOrWhiteSpace(root.Name))
            {
                return Collapse(root.Name);
            }

            return _order.FirstOrDefault(node => node.Tag == "title") is { } title
                ? Collapse(string.Concat(title.Children.Select(Node).Where(child => child?.Kind == "text").Select(child => child!.Text)))
                : null;
        }
    }

    public IReadOnlyList<PageItem> Landmarks =>
        [.. _order.Where(node => RoleOf(node) is { } role && LandmarkRoles.Contains(role) &&
                (role is not ("form" or "region") || NameOf(node).Length > 0))
            .Select(node => new PageItem(RoleOf(node)!, NameOf(node)))];

    public IReadOnlyList<PageHeading> Headings =>
        [.. _order.Where(node => RoleOf(node) == "heading").Select(node => new PageHeading(LevelOf(node), NameOf(node)))];

    public IReadOnlyList<PageItem> Fields =>
        [.. _order.Where(node => RoleOf(node) is { } role && FieldRoles.Contains(role)).Select(node => new PageItem(RoleOf(node)!, NameOf(node)))];

    public IReadOnlyList<PageItem> Buttons =>
        [.. _order.Where(node => RoleOf(node) is { } role && ButtonRoles.Contains(role)).Select(node => new PageItem(RoleOf(node)!, NameOf(node)))];

    public int LinkCount => _order.Count(node => RoleOf(node) == "link");

    public int ImageCount => _order.Count(node => RoleOf(node) == "image");

    /// <summary>Whether a dialog is in the page: a role of dialog or alert dialog.</summary>
    public IReadOnlyList<PageItem> Dialogs =>
        [.. _order.Where(node => RoleOf(node) is "dialog" or "alertDialog").Select(node => new PageItem(RoleOf(node)!, NameOf(node)))];

    private static int? LevelOf(PageNode node)
    {
        if (node.Attributes.GetValueOrDefault("aria-level") is { } level &&
            int.TryParse(level.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return node.Tag is ['h', >= '1' and <= '6'] ? node.Tag[1] - '0' : null;
    }

    /// <summary>
    /// The text under a node in tree order, with white space collapsed, up
    /// to a number of characters, and how many more there were. With
    /// accessibility records, only text Chromium exposed is counted, so
    /// text that was not rendered is left out.
    /// </summary>
    public (string Text, int More) TextOf(PageNode node, int limit)
    {
        ArgumentNullException.ThrowIfNull(node);
        var builder = new StringBuilder();
        var start = OrderOf(node);
        if (start < 0)
        {
            return ("", 0);
        }

        for (var index = start; index < _order.Count; index++)
        {
            var item = _order[index];
            if (index > start && !Contains(node, item))
            {
                break;
            }

            if (item.Kind != "text" || Hidden(item))
            {
                continue;
            }

            var text = HasAccessibility ? item.Role == "staticText" ? item.Name : null : item.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.Append(' ').Append(text);
            }
        }

        var all = Collapse(builder.ToString());
        return all.Length <= limit ? (all, 0) : (all[..limit], all.Length - limit);
    }

    // Text inside script, style, and similar elements is never page text.
    private bool Hidden(PageNode text)
    {
        for (var current = text.ParentId is { } parent ? Node(parent) : null; current is not null;
             current = current.ParentId is { } next ? Node(next) : null)
        {
            if (current.Tag is "script" or "style" or "noscript" or "template" or "head")
            {
                return true;
            }
        }

        return false;
    }

    internal static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Where a fragment points, by HTML's rule for selecting the indicated
    /// part: an empty fragment is the top of the document; otherwise the
    /// first element whose ID is the fragment, or the first a element whose
    /// name is; then the same with the fragment percent-decoded; then the
    /// top of the document for "top" in any case.
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#scroll-to-the-fragment-identifier
    /// </summary>
    public IndicatedPart IndicatedPart(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.Length == 0)
        {
            return new(IndicatedPartKind.Top, null, "the fragment is empty");
        }

        if (Potential(fragment) is { } found)
        {
            return found;
        }

        var decoded = PercentDecode(fragment);
        if (decoded is not null && Potential(decoded) is { } decodedFound)
        {
            return decodedFound with { Rule = decodedFound.Rule + ", after percent-decoding" };
        }

        return string.Equals(decoded ?? fragment, "top", StringComparison.OrdinalIgnoreCase)
            ? new(IndicatedPartKind.Top, null, "the fragment is \"top\" and no element has that ID or name")
            : new(IndicatedPartKind.None, null, "no element has that ID, and no a element that name");
    }

    private IndicatedPart? Potential(string fragment)
    {
        if (_order.FirstOrDefault(node => node.Kind == "element" && node.Attributes.GetValueOrDefault("id") == fragment) is { } byId)
        {
            return new(IndicatedPartKind.Element, byId, "the first element with that ID");
        }

        return _order.FirstOrDefault(node => node.Tag == "a" && node.Attributes.GetValueOrDefault("name") == fragment) is { } byName
            ? new(IndicatedPartKind.Element, byName, "the first a element with that name")
            : null;
    }

    // Percent-decoding then UTF-8 decoding without BOM; null when the bytes
    // are not UTF-8, which leaves the fragment as it was.
    private static string? PercentDecode(string text)
    {
        var bytes = new List<byte>(text.Length);
        var encoded = Encoding.UTF8.GetBytes(text);
        for (var index = 0; index < encoded.Length; index++)
        {
            if (encoded[index] == '%' && index + 2 < encoded.Length &&
                Uri.IsHexDigit((char)encoded[index + 1]) && Uri.IsHexDigit((char)encoded[index + 2]))
            {
                bytes.Add(Convert.ToByte(Encoding.ASCII.GetString(encoded, index + 1, 2), 16));
                index += 2;
            }
            else
            {
                bytes.Add(encoded[index]);
            }
        }

        try
        {
            var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var array = bytes.ToArray();
            var start = array.Length >= 3 && array[0] == 0xEF && array[1] == 0xBB && array[2] == 0xBF ? 3 : 0;
            return decoder.GetString(array, start, array.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// The links of the document whose address is this document with the
    /// fragment, resolved against the document's URL.
    /// </summary>
    public IReadOnlyList<PageNode> LinksTo(string fragment) =>
        [.. _order.Where(node => node.Tag == "a" && node.Attributes.GetValueOrDefault("href") is { } href &&
            Resolves(href) is { } target && target.Fragment == fragment)];

    /// <summary>The document's own address and the fragment an href resolves to; null when it is another document.</summary>
    public (string Document, string Fragment)? Resolves(string href)
    {
        ArgumentNullException.ThrowIfNull(href);
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var baseUri) || !Uri.TryCreate(baseUri, href.Trim(), out var resolved))
        {
            return null;
        }

        var (document, fragment) = SplitFragment(resolved.AbsoluteUri);
        if (fragment is null && href.TrimEnd().EndsWith('#'))
        {
            fragment = "";
        }

        var (own, _) = SplitFragment(baseUri.AbsoluteUri);
        return fragment is not null && document == own ? (document, fragment) : null;
    }

    /// <summary>A URL without its fragment, and the fragment, or null when it has none.</summary>
    public static (string Document, string? Fragment) SplitFragment(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? (url, null) : (url[..hash], url[(hash + 1)..]);
    }

    /// <summary>
    /// Names an in-page link from the page's structure: a skip link comes
    /// before the main landmark, or before the first heading where there is
    /// no main landmark, and points to it or to a place at its start; a back
    /// to top link comes after the start of the main content and points to
    /// the top of the document or before the main content; any other is an
    /// in-page link. See docs/architecture/recording-summary.md, "Fragment
    /// changes".
    /// </summary>
    public InPageLinkName NameLink(PageNode link, IndicatedPart target)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(target);
        var boundary = Main ?? FirstHeading;
        if (boundary is null)
        {
            return new(InPageLinkKind.InPage, "the page has no main landmark and no heading");
        }

        var what = Main is not null ? "the main landmark" : "the first heading";
        var linkOrder = OrderOf(link);
        var boundaryOrder = OrderOf(boundary);
        var inBoundary = Contains(boundary, link);
        if (linkOrder < boundaryOrder && !inBoundary && target.Element is { } element)
        {
            var elementOrder = OrderOf(element);
            if (element.Id == boundary.Id)
            {
                return new(InPageLinkKind.Skip, $"before {what}, pointing to it");
            }

            if (Main is not null && Contains(Main, element))
            {
                var mainHeading = _order.Skip(OrderOf(Main)).FirstOrDefault(node => RoleOf(node) == "heading" && Contains(Main, node));
                if (mainHeading is null || elementOrder <= OrderOf(mainHeading))
                {
                    return new(InPageLinkKind.Skip, "before the main landmark, pointing to its start");
                }
            }
            else if (Main is null && elementOrder > linkOrder && elementOrder <= boundaryOrder)
            {
                return new(InPageLinkKind.Skip, "before the first heading, pointing to a place before it");
            }
        }

        if (linkOrder > boundaryOrder || inBoundary)
        {
            if (target.Kind == IndicatedPartKind.Top)
            {
                return new(InPageLinkKind.BackToTop, $"after the start of {what}, pointing to the top of the document");
            }

            if (target.Element is { } before && OrderOf(before) < boundaryOrder && !Contains(before, boundary))
            {
                return new(InPageLinkKind.BackToTop, $"after the start of {what}, pointing to a place before it");
            }
        }

        return new(InPageLinkKind.InPage, "neither a skip link nor a back to top link by the page's structure");
    }

    /// <summary>The identities of the main content's children: the main landmark's, or the body's.</summary>
    public IReadOnlySet<long> MainContent()
    {
        var container = Main ?? _order.FirstOrDefault(node => node.Tag == "body");
        return container is null ? new HashSet<long>() : container.Children.Where(child => Node(child)?.Kind is "element" or "text").ToHashSet();
    }

    /// <summary>
    /// The page from a document's recorded state: its DOM, with Chromium's
    /// accessibility record for each node. Null when no DOM was recorded.
    /// </summary>
    public static PageSnapshot? From(BrowserDocumentState state, string url)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Dom is not { } dom)
        {
            return null;
        }

        var accessibility = state.Accessibility.Nodes;
        var nodes = dom.Nodes.Values.Select(node =>
        {
            string? role = null;
            string? name = null;
            if (accessibility.TryGetValue(node.Id, out var record))
            {
                role = record.Record.TryGetProperty("roleName", out var roleName) && roleName.ValueKind == JsonValueKind.String
                    ? roleName.GetString()
                    : null;
                name = record.Record.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String
                    ? nameValue.GetString()
                    : null;
            }

            var kind = node.NodeType ?? "";
            return new PageNode(
                node.Id,
                node.ParentId,
                kind,
                kind == "element" ? (node.NodeName ?? "").ToLowerInvariant() : "",
                node.Attributes,
                role,
                name,
                node.Data == DomTreeRebuilder.Cut ? null : node.Data,
                node.Children);
        });
        return new PageSnapshot(url, nodes);
    }
}
