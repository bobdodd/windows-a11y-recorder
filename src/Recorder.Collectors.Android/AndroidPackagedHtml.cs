using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Recorder.Collectors.Android;

/// <summary>Local markup only. The parser is not given a browsing context, loader, or script engine.</summary>
internal static class AndroidPackagedHtml
{
    private const string Html = "http://www.w3.org/1999/xhtml";
    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "role", "lang", "dir", "tabindex", "autofocus", "hidden", "inert", "accesskey",
        "alt", "title", "required", "disabled", "readonly", "autocomplete", "inputmode",
        "placeholder", "minlength", "maxlength", "pattern", "multiple", "novalidate", "formnovalidate",
        "scope", "headers"
    };
    private static readonly HashSet<string> Components = new(StringComparer.Ordinal)
    {
        "label", "form", "input", "textarea", "select", "button", "fieldset", "legend",
        "h1", "h2", "h3", "h4", "h5", "h6", "main", "nav", "header", "footer", "aside",
        "img", "svg", "table", "caption", "th", "details", "summary", "dialog"
    };

    public static void Read(Stream markup, Action<string, string, string> emit, CancellationToken token)
    {
        using var document = new HtmlParser().ParseDocument(markup);
        var ordinal = 0;
        foreach (var element in document.All)
        {
            token.ThrowIfCancellationRequested();
            var tag = element.LocalName;
            var locator = $"{tag} #{++ordinal}" + (element.Id is { Length: > 0 and <= 96 } id ? $" (id: {id})" : "");
            if (Components.Contains(tag))
            {
                emit("Web component", locator, tag == "input" ? $"input type: {element.GetAttribute("type") ?? "text (default)"}" : tag);
            }
            if (tag == "label" && DirectLabelText(element) is { Length: > 0 } label)
            {
                emit("Form label declaration", $"{locator} / direct text", label);
            }
            foreach (var attr in element.Attributes)
            {
                if (Attributes.Contains(attr.LocalName) || attr.LocalName.StartsWith("aria-", StringComparison.Ordinal) ||
                    tag == "label" && attr.LocalName == "for" || tag == "track" && attr.LocalName is "kind" or "srclang" or "label")
                {
                    emit(attr.LocalName == "for" ? "Form association" : "Web accessibility attribute",
                        $"{locator} / {attr.LocalName}", attr.Value.Length == 0 ? "(empty attribute)" : attr.Value);
                }
            }
            if (element.NamespaceUri != Html || tag != "meta" ||
                !string.Equals(element.GetAttribute("http-equiv"), "refresh", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var content = element.GetAttribute("content")?.Split(';', 2)[0].Trim();
            if (uint.TryParse(content, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                emit("Timing declaration", $"{locator} / refresh", $"{seconds} seconds (HTML refresh declaration, not an inactivity timeout)");
            }
        }
    }

    private static string DirectLabelText(IElement element) => string.Concat(element.ChildNodes
        .Where(child => child.NodeType == NodeType.Text).Select(child => child.TextContent)).Trim();
}
