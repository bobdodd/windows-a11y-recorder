namespace Recorder.Recreation;

// Fixed content for slice 3a: a page and evidence written in code, so the
// inspector can be opened and tested before it reads a recording. None of it
// is evidence, and the evidence panel says so.
public static class FixedRecreation
{
    // The page's own script and event handler change the title if they run.
    // They must not: the recreation serves the page with a content security
    // policy that allows no page script.
    public const string ScriptRanTitle = "A page script ran";

    public const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>Fixed recreation</title>
        <style>
          body { font: 16px/1.5 system-ui, sans-serif; margin: 0 2rem 2rem; color: #1b1b1b; background: #fff; }
          header { border-bottom: 1px solid #767676; margin-bottom: 1rem; }
          nav a { margin-right: 1rem; }
          #status { transition: opacity 1s; }
          section { margin-bottom: 2rem; }
          .filler { max-width: 40rem; }
        </style>
        <script>document.title = "A page script ran";</script>
        </head>
        <body>
        <header>
          <h1>Fixed recreation</h1>
          <nav aria-label="Sections"><a href="#form">Form</a><a href="#card">Card</a><a href="#long">Long content</a></nav>
        </header>
        <main>
          <section id="form">
            <h2>Form</h2>
            <label for="name">Name</label>
            <input id="name" value="Ada">
            <button type="button" onclick="document.title = 'A page script ran'">Save</button>
            <p id="status">Saved</p>
          </section>
          <section id="card">
            <h2>Card with a shadow root</h2>
            <my-card><template shadowrootmode="open"><style>.card { border: 2px solid #005a9c; padding: 1rem; }</style><div class="card"><p>Inside the shadow root</p><button type="button">Open</button><button type="button">Close</button></div></template></my-card>
            <svg width="24" height="24" role="img" aria-label="Star"><circle cx="12" cy="12" r="10" fill="#005a9c"></circle></svg>
          </section>
          <section id="long">
            <h2>Long content</h2>
        {{FILLER}}
            <button type="button">End of page</button>
          </section>
        </main>
        </body>
        </html>
        """;

    public const long FrameNanoseconds = 12_500_000_000;

    public static RecreationContent Create()
    {
        var filler = string.Join(
            "\n",
            Enumerable.Range(1, 60).Select(index =>
                $"    <p class=\"filler\">Paragraph {index} of content below the first screen, so the whole page can be explored.</p>"));
        return new RecreationContent(Html.Replace("{{FILLER}}", filler, StringComparison.Ordinal), Evidence());
    }

    private const string Section1 = "/html[1]/body[1]/main[1]/section[1]";
    private const string Card = "/html[1]/body[1]/main[1]/section[2]/my-card[1]";

    public static readonly NodePath Input = NodePath.Of($"{Section1}/input[1]");
    public static readonly NodePath Save = NodePath.Of($"{Section1}/button[1]");
    public static readonly NodePath Status = NodePath.Of($"{Section1}/p[1]");
    public static readonly NodePath Open = NodePath.Create([Card, "/div[1]/button[1]"], ["open"]);
    public static readonly NodePath Close = NodePath.Create([Card, "/div[1]/button[2]"], ["open"]);
    public static readonly NodePath End = NodePath.Of("/html[1]/body[1]/main[1]/section[3]/button[1]");

    private static RecordedListener L(string eventName) =>
        new(eventName, "add-event-listener", false, false, false, null);

    private static NodePath Link(int position) => NodePath.Of($"/html[1]/body[1]/header[1]/nav[1]/a[{position}]");

    private static RecreationEvidence Evidence() => new(
        new RecreationDescription(
            "fixed",
            "Fixed content for slice 3a",
            "This page and every value in this panel are fixed content written in code to test the inspector. None of it is evidence from a recording.",
            FrameNanoseconds,
            FrameNanoseconds,
            "fixed"),
        new RecreationFidelity(
            "not-checked",
            "Fixed content is not compared with a recording.",
            []),
        [
            new RecordedTimer("timer-12", "timeout", 30_000, 30_000, 10_000_000_000, null, 27_500),
            new RecordedTimer("timer-13", "interval", 1_000, 1_000, 2_000_000_000, 11_500_000_000, 500)
        ],
        [
            new RecordedAnimation(
                "css-transition", "opacity", Status, null, "running", false, 12_000, 12_000_000_000, 0, 1_000, 1,
                "normal", "auto", "ease", 500, "computed", 0, 0.5, "document timeline", false, 12_000_000_000, null)
        ],
        [
            new RecordedInteractiveElement(Link(1), "a", [], true, "link", "Form", null, null),
            new RecordedInteractiveElement(Link(2), "a", [], true, "link", "Card", null, null),
            new RecordedInteractiveElement(Link(3), "a", [], true, "link", "Long content", null, null),
            new RecordedInteractiveElement(Input, "input", [L("input"), L("change")], true, "textbox", "Name", null, null),
            new RecordedInteractiveElement(Save, "button", [L("click")], true, "button", "Save", null, null),
            new RecordedInteractiveElement(Open, "button", [L("click")], true, "button", "Open", null, null),
            new RecordedInteractiveElement(Close, "button", [L("click"), L("keydown")], true, "button", "Close", null, null),
            new RecordedInteractiveElement(End, "button", [], true, "button", "End of page", null, null)
        ],
        new RecordedInteraction(Input, "Caret after \"Ada\" in the Name field", [new RecordedFormValue(Input, "Ada")]));
}
