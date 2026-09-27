namespace Recorder.Session;

/// <summary>
/// What a navigated frame is, for listing and filtering navigations.
/// </summary>
public enum BrowserNavigationKind
{
    /// <summary>The tab's top-level page.</summary>
    Page,

    /// <summary>A frame inside a page.</summary>
    Iframe,

    /// <summary>The browser's own interface, such as its toolbar.</summary>
    BrowserUi,

    /// <summary>
    /// Another main frame: a prerendered page, a fenced frame, or a guest.
    /// </summary>
    OtherFrame
}

public static class BrowserNavigationKinds
{
    public const string PrimaryMainFrame = "primary-main-frame";

    public const string Subframe = "subframe";

    /// <summary>The kinds in the order the player lists them.</summary>
    public static IReadOnlyList<BrowserNavigationKind> All { get; } =
    [
        BrowserNavigationKind.Page,
        BrowserNavigationKind.Iframe,
        BrowserNavigationKind.BrowserUi,
        BrowserNavigationKind.OtherFrame
    ];

    /// <summary>
    /// Classifies a navigation by its recorded frame type and URL. The
    /// browser's interface is a <c>chrome://</c> host in Chromium's
    /// <c>top-chrome</c> domain, such as
    /// <c>chrome://omnibox-popup.top-chrome/</c>, or a <c>devtools://</c> URL;
    /// it is recorded as a primary main frame of its own. A navigation without
    /// a recorded frame type is a page when it is in the primary page.
    /// </summary>
    public static BrowserNavigationKind Classify(
        string? frameType,
        bool primaryPage,
        string? url)
    {
        if (IsBrowserInterface(url))
        {
            return BrowserNavigationKind.BrowserUi;
        }

        return frameType switch
        {
            PrimaryMainFrame => BrowserNavigationKind.Page,
            Subframe => BrowserNavigationKind.Iframe,
            null => primaryPage
                ? BrowserNavigationKind.Page
                : BrowserNavigationKind.OtherFrame,
            _ => BrowserNavigationKind.OtherFrame
        };
    }

    public static string Describe(BrowserNavigationKind kind) => kind switch
    {
        BrowserNavigationKind.Page => "Page",
        BrowserNavigationKind.Iframe => "Iframe",
        BrowserNavigationKind.BrowserUi => "Browser UI",
        _ => "Other frame"
    };

    /// <summary>The plural name the player's filter shows.</summary>
    public static string DescribePlural(BrowserNavigationKind kind) => kind switch
    {
        BrowserNavigationKind.Page => "Pages",
        BrowserNavigationKind.Iframe => "Iframes",
        BrowserNavigationKind.BrowserUi => "Browser UI",
        _ => "Other frames"
    };

    private static bool IsBrowserInterface(string? url)
    {
        if (url is null)
        {
            return false;
        }

        if (url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "chrome", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.Host.EndsWith(".top-chrome", StringComparison.OrdinalIgnoreCase);
    }
}
