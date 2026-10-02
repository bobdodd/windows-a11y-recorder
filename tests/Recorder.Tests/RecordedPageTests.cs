using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Recorder.Recreation;
using Recorder.Session;

namespace Recorder.Tests;

// The recorded page of slice 3b: the tree data written for the builder, the
// paths of recorded nodes, and, with RECORDER_RECREATION_CHROMIUM set, the
// DOM the builder builds in Chromium.
public sealed class RecordedPageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recorded-page-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
                return;
            }
            catch (Exception exception) when (attempt < 40 && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    // A tree the HTML parser cannot return from markup: a div directly in a
    // table, a paragraph in a paragraph. It has a document type, a comment
    // before the document element, a script, an event handler attribute, a
    // javascript: link, SVG, an open shadow root with a manually assigned
    // slot, a closed shadow root, a user agent shadow root, a text control
    // with a recorded value and focus, and an attribute value cut in the
    // recording.
    private static BrowserDocumentState State()
    {
        var tree = new DomDocumentTree();
        DomNode Add(long id, long? parent, string type, string name, string? data = null, params (string Name, string? Value)[] attributes)
        {
            var node = new DomNode(id) { ParentId = parent, NodeType = type, NodeName = name, Data = data };
            foreach (var (key, value) in attributes)
            {
                node.Attributes[key] = value;
            }
            tree.Nodes.Add(id, node);
            if (parent is { } parentId)
            {
                if (type == "shadow-root")
                {
                    tree.Nodes[parentId].ShadowRootId = id;
                }
                else
                {
                    tree.Nodes[parentId].Children.Add(id);
                }
            }
            return node;
        }
        Add(1, null, "document", "#document");
        Add(2, 1, "other", "html");
        Add(3, 1, "comment", "#comment", "before");
        Add(4, 1, "element", "HTML", null, ("lang", "en"));
        Add(5, 4, "element", "HEAD");
        Add(6, 5, "element", "TITLE");
        Add(7, 6, "text", "#text", "Built <exactly> </script>");
        Add(8, 5, "element", "SCRIPT");
        Add(9, 8, "text", "#text", "window.__ran = true;");
        Add(10, 5, "element", "STYLE");
        Add(11, 10, "text", "#text", "p { color: rgb(1, 2, 3); }");
        Add(12, 4, "element", "BODY", null, ("onload", "window.__handler = true"));
        Add(13, 12, "element", "TABLE");
        Add(14, 13, "element", "DIV");
        Add(15, 14, "text", "#text", "in the table");
        Add(16, 12, "element", "P");
        Add(17, 16, "element", "P");
        Add(18, 17, "text", "#text", "nested");
        Add(19, 12, "element", "svg", null, ("viewBox", "0 0 10 10"), ("{http://www.w3.org/2000/xmlns/}xlink", "http://www.w3.org/1999/xlink"));
        Add(20, 19, "element", "linearGradient", null, ("id", "g"));
        Add(21, 19, "element", "use", null, ("{http://www.w3.org/1999/xlink}href", "#g"));
        Add(22, 12, "element", "MY-CARD");
        var open = Add(23, 22, "shadow-root", "#document-fragment");
        open.ShadowRootFields = "22,\"open\",true,\"manual\",false,true,false,false,null";
        var slot = Add(24, 23, "element", "SLOT");
        slot.AssignedNodes = "[27]";
        Add(25, 23, "element", "DIV", null, ("part", "inner"));
        Add(26, 22, "element", "SPAN");
        Add(27, 22, "element", "SPAN");
        Add(28, 27, "text", "#text", "slotted");
        Add(29, 12, "element", "X-CLOSED");
        var closed = Add(30, 29, "shadow-root", "#document-fragment");
        closed.ShadowRootFields = "29,\"closed\",false,\"named\",false,false,false,false,null";
        Add(31, 30, "element", "BUTTON");
        Add(32, 31, "text", "#text", "inner");
        Add(33, 12, "element", "INPUT", null, ("type", "text"), ("name", "who"));
        var agent = Add(34, 33, "shadow-root", "#document-fragment");
        agent.ShadowRootFields = "33,\"user-agent\",false,\"named\",false,false,false,false,null";
        Add(35, 34, "element", "DIV");
        Add(36, 12, "element", "A", null, ("href", "javascript:window.__link = true"), ("id", "link"));
        Add(37, 36, "text", "#text", "link");
        Add(38, 12, "element", "INPUT", null, ("value", DomTreeRebuilder.Cut));
        Add(39, 12, "comment", "#comment", "after");

        var state = new BrowserDocumentState("token-a dom-document-1") { Dom = tree, DomCompleteness = BrowserStateCompleteness.Complete };
        state.Interaction.Apply("interaction-checkpoint-started", J("""
            {"checkpointId":"interaction-checkpoint-1","focusedNodeId":33,"anchorNodeId":null,"anchorOffset":null,
             "focusNodeId":null,"focusOffset":null,"selectionType":"none"}
            """));
        state.Interaction.Apply("interaction-checkpoint-text-control", J("""
            {"checkpointId":"interaction-checkpoint-1","nodeId":33,"controlType":"input","value":"Ad","valueTruncated":false,
             "selectionStart":2,"selectionEnd":2,"selectionDirection":"forward"}
            """));
        state.Interaction.Apply("interaction-checkpoint-completed", J("""{"checkpointId":"interaction-checkpoint-1"}"""));
        state.Interaction.Apply("text-control-value-changed", J("""
            {"nodeId":33,"controlType":"input","value":"Ada","valueTruncated":false,"selectionStart":3,"selectionEnd":3,
             "selectionDirection":"forward"}
            """));
        state.Script.Apply(1, 1_000_000_000, "listener-registered", J("""
            {"listenerId":"listener-1","eventName":"click","capture":false,"once":false,"passive":false,
             "registrationKind":"add-event-listener","location":{"url":"https://example.test/app.js","line":12,"column":4},
             "target":{"kind":"node","nodeId":31,"documentId":"dom-document-1"}}
            """));
        state.Script.Apply(2, 1_000_000_000, "listener-registered", J("""
            {"listenerId":"listener-2","eventName":"resize","capture":false,"once":false,"passive":true,
             "registrationKind":"add-event-listener","target":{"kind":"window","nodeId":null,"interfaceName":"DOMWindow"}}
            """));
        state.Script.Apply(3, 2_000_000_000, "timer-scheduled", J("""
            {"timerId":"timer-1","timerKind":"timeout","requestedDelayMilliseconds":5000,"effectiveDelayMilliseconds":5000}
            """));
        state.Script.Apply(4, 2_000_000_000, "timer-scheduled", J("""
            {"timerId":"timer-2","timerKind":"interval","requestedDelayMilliseconds":1000,"effectiveDelayMilliseconds":1000}
            """));
        state.Script.Apply(5, 3_500_000_000, "timer-fired", J("""{"timerId":"timer-2","timerKind":"interval"}"""));
        state.Accessibility.Apply(6, 3_000_000_000, "accessibility-checkpoint-started", J("""{"checkpointId":"a-1"}"""));
        state.Accessibility.Apply(7, 3_000_000_000, "accessibility-checkpoint-node", J("""
            {"checkpointId":"a-1","accessibilityNodeId":9,"domNodeId":36,"roleName":"link","name":"link",
             "serializedProperties":"id=9 link FOCUSABLE LINKED"}
            """));
        state.Accessibility.Apply(8, 3_000_000_000, "accessibility-checkpoint-completed", J("""{"checkpointId":"a-1"}"""));
        return state;
    }

    [Fact]
    public void APathFollowsTheTreeThroughEachShadowRoot()
    {
        var tree = State().Dom!;
        Assert.Equal("/html[1]/body[1]/p[1]/p[1]/text()[1]", RecordedPaths.Of(tree, 18)!.Display);
        Assert.Equal("/html[1]/body[1]/*[local-name()='svg'][1]/*[local-name()='use'][1]", RecordedPaths.Of(tree, 21)!.Display);
        Assert.Equal("/html[1]/body[1]/my-card[1]/#shadow-root(open)/div[1]", RecordedPaths.Of(tree, 25)!.Display);
        Assert.Equal("/html[1]/body[1]/my-card[1]/span[2]/text()[1]", RecordedPaths.Of(tree, 28)!.Display);
        Assert.Equal(["/html[1]/body[1]/x-closed[1]", "/button[1]"], RecordedPaths.Of(tree, 31)!.Scopes);
        Assert.Equal("/html[1]/body[1]/input[2]", RecordedPaths.Of(tree, 38)!.Display);
        Assert.Equal("/comment()[1]", RecordedPaths.Of(tree, 3)!.Display);
        // A path does not enter a user agent shadow root.
        Assert.Null(RecordedPaths.Of(tree, 35));
        Assert.Null(RecordedPaths.Of(tree, 999));
    }

    [Fact]
    public void TheTreeDataHoldsTheRecordedTreeWithoutTheUserAgentShadowRootAndCannotEndItsDataBlock()
    {
        var state = State();
        var data = RecordedPage.Tree(state);
        var text = Encoding.UTF8.GetString(data);
        Assert.DoesNotContain("<", text);
        using var json = JsonDocument.Parse(data);
        var root = json.RootElement;
        var document = root.GetProperty("document");
        Assert.Equal("document-type", document.GetProperty("children")[0].GetProperty("type").GetString());
        var body = document.GetProperty("children")[2].GetProperty("children")[1];
        Assert.Equal("BODY", body.GetProperty("name").GetString());
        var card = body.GetProperty("children").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "MY-CARD");
        var shadow = card.GetProperty("shadowRoot");
        Assert.Equal("open", shadow.GetProperty("mode").GetString());
        Assert.True(shadow.GetProperty("delegatesFocus").GetBoolean());
        Assert.Equal("manual", shadow.GetProperty("slotAssignment").GetString());
        Assert.True(shadow.GetProperty("serializable").GetBoolean());
        var input = body.GetProperty("children").EnumerateArray().First(item => item.GetProperty("name").GetString() == "INPUT");
        Assert.Equal(JsonValueKind.Null, input.GetProperty("shadowRoot").ValueKind);
        var svg = body.GetProperty("children").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "svg");
        Assert.Equal("http://www.w3.org/2000/xmlns/", svg.GetProperty("attributes")[1][0].GetString());
        Assert.Equal("[[24,[27]]]", root.GetProperty("manualSlots").GetRawText());
        Assert.Equal(33, root.GetProperty("focusedNodeId").GetInt64());
        var control = Assert.Single(root.GetProperty("textControls").EnumerateArray());
        Assert.Equal("Ada", control.GetProperty("value").GetString());
        Assert.Equal(3, control.GetProperty("selectionStart").GetInt64());
        var cut = body.GetProperty("children").EnumerateArray().Last(item => item.GetProperty("name").GetString() == "INPUT");
        Assert.Equal(JsonValueKind.Null, cut.GetProperty("attributes")[0][2].ValueKind);

        Assert.Equal("html", RecordedPage.DocumentTypeName(state.Dom!, 1));
        var markup = RecordedPage.Markup(data, "html", "n0nce");
        Assert.StartsWith("<!DOCTYPE html><html><head>", markup);
        Assert.Contains("<script nonce=\"n0nce\" src=\"builder.js\" defer></script>", markup);
        Assert.Contains("script-src 'nonce-n0nce';", RecreationServer.RecordedPageContentSecurityPolicy("n0nce"));
        Assert.DoesNotContain("unsafe-inline'; img", RecreationServer.RecordedPageContentSecurityPolicy("n0nce").Split("script-src")[1].Split(';')[0]);
    }

    // Stage 3: an element with a layout record carries its recorded style, as
    // declarations, and its recorded box fragments, as recorded; a text node
    // and an element without a record carry neither.
    [Fact]
    public void TheTreeDataCarriesEachElementsRecordedStyleAndBoxFragments()
    {
        var state = State();
        state.Layout.ApplyNode(J("""
            {"nodeId":16,"computedStyle":{"display":"block","color":"rgb(0, 0, 0)","quotes":null},
             "customProperties":{"--gap":"4px"},
             "boxFragments":{"effectiveZoom":1,"fragments":[{"width":300,"height":20,"breakToken":null,"scrollableOverflow":null,
               "children":[{"kind":"line","x":0,"y":0,"nodeId":null,"fragmentIndex":null,"fragment":null}],
               "items":[{"type":"line","x":0,"y":0,"width":300,"height":20}]}],"naturalSize":null,"textContent":"Hello"}}
            """));
        // A record of changes is merged into the node's last record.
        state.Layout.ApplyNode(J("""
            {"nodeId":16,"computedStyleComplete":false,"computedStyle":{"color":"rgb(255, 0, 0)"},"customProperties":{},"removedCustomProperties":[],
             "boxFragments":{"effectiveZoom":1,"fragments":[{"width":310,"height":20,"breakToken":null,"scrollableOverflow":null,"children":[]}],
               "naturalSize":null,"textContent":null,"textContentUnchanged":true}}
            """));
        var record = state.Layout.Nodes[16];
        Assert.Equal("display: block; color: rgb(255, 0, 0); --gap: 4px;", RecordedPage.RecordedStyle(record));
        using (var layout = JsonDocument.Parse(RecordedPage.RecordedLayout(record)!))
        {
            Assert.Equal(310, layout.RootElement.GetProperty("fragments")[0].GetProperty("width").GetDouble());
            Assert.Equal("Hello", layout.RootElement.GetProperty("textContent").GetString());
        }
        Assert.Null(RecordedPage.RecordedStyle(J("""{"nodeId":3,"computedStyle":null}""")));
        Assert.Null(RecordedPage.RecordedLayout(J("""{"nodeId":3,"boxFragments":null}""")));

        using var json = JsonDocument.Parse(RecordedPage.Tree(state));
        JsonElement Find(JsonElement node, long id)
        {
            if (node.GetProperty("id").GetInt64() == id)
            {
                return node;
            }
            foreach (var child in node.GetProperty("children").EnumerateArray())
            {
                if (Find(child, id) is { ValueKind: JsonValueKind.Object } found)
                {
                    return found;
                }
            }
            return default;
        }
        var paragraph = Find(json.RootElement.GetProperty("document"), 16);
        Assert.Equal("display: block; color: rgb(255, 0, 0); --gap: 4px;", paragraph.GetProperty("recordedStyle").GetString());
        Assert.Contains("\"width\":310", paragraph.GetProperty("recordedLayout").GetString());
        var inner = Find(json.RootElement.GetProperty("document"), 17);
        Assert.Equal(JsonValueKind.Null, inner.GetProperty("recordedStyle").ValueKind);
        Assert.Equal(JsonValueKind.Null, inner.GetProperty("recordedLayout").ValueKind);
        var text = Find(json.RootElement.GetProperty("document"), 18);
        Assert.Equal(JsonValueKind.Null, text.GetProperty("recordedLayout").ValueKind);

        var content = RecordedPage.Content(state, "https://example.test/", 4_000_000_000, 4_000_000_000, "presented");
        Assert.Contains(content.Evidence.Notes, note => note.StartsWith("1 of the ", StringComparison.Ordinal) && note.Contains("data-a11y-recorded-layout", StringComparison.Ordinal));
        Assert.Contains(content.Evidence.Notes, note => note.Contains("DevTools' Console", StringComparison.Ordinal));
    }

    // Stage 3, with RECORDER_RECREATION_CHROMIUM set to the instrumented
    // Chromium: a page whose recorded box sizes, child offsets, and item
    // rectangles differ from those Blink would lay out is drawn with the
    // recorded ones, read back over the DevTools protocol.
    [Fact]
    public async Task TheRecreationModeImposesTheRecordedBoxFragmentsAndItems()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_RECREATION_CHROMIUM") is not { } chromium)
        {
            return;
        }
        var token = TestContext.Current.CancellationToken;
        var tree = new DomDocumentTree();
        void Add(long id, long? parent, string type, string name, string? data = null)
        {
            tree.Nodes.Add(id, new DomNode(id) { ParentId = parent, NodeType = type, NodeName = name, Data = data });
            if (parent is { } parentId)
            {
                tree.Nodes[parentId].Children.Add(id);
            }
        }
        Add(1, null, "document", "#document");
        Add(2, 1, "other", "html");
        Add(3, 1, "element", "HTML");
        Add(4, 3, "element", "HEAD");
        Add(5, 3, "element", "BODY");
        Add(6, 5, "element", "DIV");
        Add(7, 6, "text", "#text", "Hello");
        var state = new BrowserDocumentState("token-a dom-document-1") { Dom = tree, DomCompleteness = BrowserStateCompleteness.Complete };
        state.Layout.ApplyNode(J("""
            {"nodeId":5,"computedStyle":null,"boxFragments":{"effectiveZoom":1,"fragments":[{"width":784,"height":300,"breakToken":null,"scrollableOverflow":null,
              "children":[{"kind":"box","x":30,"y":40,"nodeId":6,"fragmentIndex":0,"fragment":null}]}],"naturalSize":null}}
            """));
        state.Layout.ApplyNode(J("""
            {"nodeId":6,"computedStyle":null,"boxFragments":{"effectiveZoom":1,"fragments":[{"width":250,"height":123,"breakToken":null,"scrollableOverflow":null,
              "children":[{"kind":"line","x":0,"y":0,"nodeId":null,"fragmentIndex":null,"fragment":null}],
              "items":[{"type":"line","x":0,"y":0,"width":250,"height":30,"descendantsCount":2},
                       {"type":"text","x":5,"y":7,"width":60,"height":18,"start":0,"end":5,"glyphRuns":null}]}],
              "naturalSize":null,"textContent":"Hello"}}
            """));
        var content = RecordedPage.Content(state, "https://example.test/", 4_000_000_000, 4_000_000_000, "presented") with
        {
            Viewport = new RecreationViewport(800, 600, 1, 1),
        };
        await using var session = await RecreationSession.OpenAsync(
            chromium, Path.Combine(_directory, "imposed"), content, token, ["--headless=new"]);

        using var http = new HttpClient();
        var list = $"http://127.0.0.1:{session.DevToolsAddress.Port}/json/list";
        string? address = null;
        for (var attempt = 0; attempt < 100 && address is null; attempt++)
        {
            using var targets = JsonDocument.Parse(await http.GetStringAsync(list, token));
            address = targets.RootElement.EnumerateArray()
                .Where(item => item.GetProperty("type").GetString() == "page" &&
                               item.GetProperty("url").GetString() == session.PageAddress)
                .Select(item => item.GetProperty("webSocketDebuggerUrl").GetString())
                .FirstOrDefault();
            if (address is null)
            {
                await Task.Delay(100, token);
            }
        }
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(address!), token);
        var client = new TestDevToolsClient(socket);
        async Task<string> Evaluate(string expression)
        {
            using var answer = await client.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, token);
            return answer.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").ToString();
        }
        for (var attempt = 0; attempt < 100 && await Evaluate("Boolean(window.__recorderRecreation)") != "True"; attempt++)
        {
            await Task.Delay(100, token);
        }
        using var measured = JsonDocument.Parse(await Evaluate("""
            JSON.stringify((() => {
              const body = document.body.getBoundingClientRect();
              const div = document.querySelector("div").getBoundingClientRect();
              const range = document.createRange();
              range.selectNodeContents(document.querySelector("div").firstChild);
              const text = range.getBoundingClientRect();
              return [body.height, div.x - body.x, div.y - body.y, div.width, div.height, text.x - div.x, text.y - div.y, text.width];
            })())
            """));
        Assert.Equal([300, 30, 40, 250, 123, 5, 7, 60], measured.RootElement.EnumerateArray().Select(value => value.GetDouble()).ToArray());
    }

    [Fact]
    public void TheEvidenceIsReadFromTheRecordedState()
    {
        var state = State();
        var evidence = RecordedEvidence.Create(state, "https://example.test/", 4_000_000_000, 4_000_000_000, "presented",
            new RecreationFidelity("not-checked", "", []), []);
        Assert.Equal("Built <exactly> </script>", evidence.Recreation.Title);
        Assert.Equal("https://example.test/", evidence.Recreation.Url);
        var timeout = evidence.Timers.Single(timer => timer.TimerId == "timer-1");
        Assert.Equal(3_000, timeout.RemainingMilliseconds);
        var interval = evidence.Timers.Single(timer => timer.TimerId == "timer-2");
        Assert.Equal(500, interval.RemainingMilliseconds);
        Assert.Equal(3_500_000_000, interval.LastRunNanoseconds);
        var button = evidence.InteractiveElements.Single(item => item.Element == "button");
        Assert.Equal(["/html[1]/body[1]/x-closed[1]", "/button[1]"], button.Node.Scopes);
        Assert.Equal("https://example.test/app.js:12:4", Assert.Single(button.Listeners).Location);
        Assert.Null(button.Focusable);
        var link = evidence.InteractiveElements.Single(item => item.Element == "a");
        Assert.True(link.Focusable);
        Assert.Equal("link", link.Role);
        Assert.Equal(3_000_000_000, link.AccessibilityNanoseconds);
        Assert.Equal("window (DOMWindow)", Assert.Single(evidence.OtherListeners).Target);
        Assert.Equal("/html[1]/body[1]/input[1]", evidence.Interaction.Focus!.Display);
        Assert.Equal("Ada", Assert.Single(evidence.Interaction.FormValues).Value);
    }

    // Set RECORDER_RECREATION_CHROMIUM to a Chromium executable to build the
    // tree in it without a window, and check that the DOM Chromium then
    // holds, read over the DevTools protocol through every shadow root,
    // equals the tree given, and that no page script, event handler, or
    // javascript: link runs.
    [Fact]
    public async Task TheBuilderBuildsTheRecordedTreeExactlyAndRunsNoPageScript()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_RECREATION_CHROMIUM") is not { } chromium)
        {
            return;
        }
        var token = TestContext.Current.CancellationToken;
        var state = State();
        var content = RecordedPage.Content(state, "https://example.test/", 4_000_000_000, 4_000_000_000, "presented");
        await using var server = await RecreationServer.StartAsync(content, token);
        await using var browser = await HeadlessChromium.StartAsync(chromium, Path.Combine(_directory, "profile"), token);
        var client = browser.Client;
        await client.CallAsync("Page.enable", new { }, token);
        await client.CallAsync("Page.navigate", new { url = server.PageAddress }, token);
        await client.WaitForEventAsync("Page.loadEventFired", token);

        using var document = await client.CallAsync("DOM.getDocument", new { depth = -1, pierce = true }, token);
        var built = Describe(document.RootElement.GetProperty("result").GetProperty("root"));
        Assert.Equal(Describe(state.Dom!, 1), built);

        using var answer = await client.CallAsync("Runtime.evaluate", new
        {
            expression = """
                (() => {
                  document.getElementById("link").click();
                  const input = document.querySelector("input");
                  return JSON.stringify({
                    ran: typeof window.__ran, handler: typeof window.__handler, link: typeof window.__link,
                    built: window.__recorderRecreation.built, notes: window.__recorderRecreation.notes.length,
                    svg: document.getElementsByTagNameNS("http://www.w3.org/2000/svg", "linearGradient").length,
                    xlink: document.querySelector("use").getAttributeNS("http://www.w3.org/1999/xlink", "href"),
                    value: input.value, caret: input.selectionStart, focused: document.activeElement === input,
                    assigned: document.querySelector("my-card").shadowRoot.querySelector("slot").assignedNodes().map(node => node.textContent),
                    mode: document.compatMode, title: document.title
                  });
                })()
                """,
            returnByValue = true,
        }, token);
        using var result = JsonDocument.Parse(answer.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString()!);
        var values = result.RootElement;
        Assert.Equal("undefined", values.GetProperty("ran").GetString());
        Assert.Equal("undefined", values.GetProperty("handler").GetString());
        Assert.Equal("undefined", values.GetProperty("link").GetString());
        Assert.True(values.GetProperty("built").GetBoolean());
        Assert.Equal(1, values.GetProperty("notes").GetInt32());
        Assert.Equal(1, values.GetProperty("svg").GetInt32());
        Assert.Equal("#g", values.GetProperty("xlink").GetString());
        Assert.Equal("Ada", values.GetProperty("value").GetString());
        Assert.Equal(3, values.GetProperty("caret").GetInt32());
        Assert.True(values.GetProperty("focused").GetBoolean());
        Assert.Equal("[\"slotted\"]", values.GetProperty("assigned").GetRawText());
        Assert.Equal("CSS1Compat", values.GetProperty("mode").GetString());
        Assert.Equal("Built <exactly> </script>", values.GetProperty("title").GetString());

        // Every path in the evidence selects its node through the panel's
        // own resolver, except the one in the closed shadow root, which the
        // page cannot reach.
        var evidence = content.Evidence;
        var paths = evidence.InteractiveElements.Select(item => item.Node)
            .Concat(evidence.Interaction.FormValues.Select(item => item.Node))
            .Append(evidence.Interaction.Focus!)
            .ToArray();
        var found = await ResolveAsync(await FindNodeSource(server), paths, client, token);
        Assert.Equal(["no-shadow-root", "a", "input", "input"], found.Select(item => item.Name ?? item.Failure));
    }

    private async Task<string> FindNodeSource(RecreationServer server)
    {
        var extension = Path.Combine(_directory, "extension-for-test");
        RecreationBrowser.WriteExtension(extension, server.EvidenceAddress);
        var panel = await File.ReadAllTextAsync(Path.Combine(extension, "panel.js"));
        return panel[panel.IndexOf("function findNode", StringComparison.Ordinal)..panel.IndexOf("// Selects the node", StringComparison.Ordinal)];
    }

    // The local name of the node each path selects in the page, or why none.
    internal static async Task<IReadOnlyList<(string? Name, string? Failure)>> ResolveAsync(
        string findNode, IReadOnlyList<NodePath> paths, TestDevToolsClient client, CancellationToken token)
    {
        var expression = $$"""
            (() => {
              {{findNode}}
              return JSON.stringify({{JsonSerializer.Serialize(paths.Select(path => path.Scopes))}}.map(scopes => {
                const found = findNode(scopes);
                return found.node ? [found.node.localName || found.node.nodeName, null] : [null, found.failure];
              }));
            })()
            """;
        using var answer = await client.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, token);
        using var result = JsonDocument.Parse(answer.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString()!);
        return result.RootElement.EnumerateArray().Select(item => (item[0].GetString(), item[1].GetString())).ToArray();
    }

    // The tree as recorded, one line per node, in the order the DevTools
    // protocol gives: each node's shadow root before its children. The
    // attribute cut in the recording is not built, and is left out; user
    // agent shadow roots are left out on both sides.
    // The protocol leaves out text nodes of white space only, so they are
    // left out here too, and character data is written escaped.
    internal static string Describe(DomDocumentTree tree, long id)
    {
        var lines = new StringBuilder();
        void Walk(long nodeId, int depth)
        {
            var node = tree.Nodes[nodeId];
            if (node.NodeType == "text" && (node.Data ?? "").All(character => character is ' ' or '\t' or '\n' or '\r' or '\f'))
            {
                return;
            }
            if (node.NodeType == "shadow-root")
            {
                var mode = RecordedPaths.ShadowMode(node);
                if (mode == "user-agent")
                {
                    return;
                }
                lines.Append(' ', depth).Append("#shadow-root ").Append(mode).Append('\n');
            }
            else
            {
                var name = node.NodeType == "other" ? "html" : node.NodeName;
                var attributes = node.Attributes
                    .Where(item => item.Value != DomTreeRebuilder.Cut)
                    .Select(item => $"{Local(item.Key)}={item.Value}")
                    .Order(StringComparer.Ordinal);
                lines.Append(' ', depth).Append(name).Append(' ').Append(string.Join(" ", attributes))
                    .Append(' ').Append(JsonSerializer.Serialize(node.Data ?? "")).Append('\n');
            }
            if (node.ShadowRootId is { } root)
            {
                Walk(root, depth + 1);
            }
            foreach (var child in node.Children)
            {
                Walk(child, depth + 1);
            }
        }
        Walk(id, 0);
        return lines.ToString();
    }

    // The protocol names an attribute by its qualified name.
    private static string Local(string key)
    {
        if (!key.StartsWith('{'))
        {
            return key;
        }
        var end = key.IndexOf('}');
        var name = key[(end + 1)..];
        return key[1..end] switch
        {
            "http://www.w3.org/2000/xmlns/" when name != "xmlns" => "xmlns:" + name,
            "http://www.w3.org/XML/1998/namespace" => "xml:" + name,
            "http://www.w3.org/1999/xlink" => "xlink:" + name,
            _ => name,
        };
    }

    internal static string Describe(JsonElement root)
    {
        var lines = new StringBuilder();
        void Walk(JsonElement node, int depth)
        {
            var type = node.GetProperty("nodeType").GetInt32();
            if (type == 11)
            {
                var mode = node.GetProperty("shadowRootType").GetString();
                if (mode == "user-agent")
                {
                    return;
                }
                lines.Append(' ', depth).Append("#shadow-root ").Append(mode).Append('\n');
            }
            else
            {
                var attributes = new List<string>();
                if (node.TryGetProperty("attributes", out var list))
                {
                    for (var index = 0; index < list.GetArrayLength(); index += 2)
                    {
                        attributes.Add($"{list[index].GetString()}={list[index + 1].GetString()}");
                    }
                }
                attributes.Sort(StringComparer.Ordinal);
                var data = type is 3 or 8 ? node.GetProperty("nodeValue").GetString() : "";
                lines.Append(' ', depth).Append(node.GetProperty("nodeName").GetString()).Append(' ')
                    .Append(string.Join(" ", attributes)).Append(' ').Append(JsonSerializer.Serialize(data)).Append('\n');
            }
            if (node.TryGetProperty("shadowRoots", out var roots))
            {
                foreach (var shadow in roots.EnumerateArray())
                {
                    Walk(shadow, depth + 1);
                }
            }
            if (node.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    Walk(child, depth + 1);
                }
            }
        }
        Walk(root, 0);
        return lines.ToString();
    }
}

// A Chromium without a window, driven over the DevTools protocol.
internal sealed class HeadlessChromium : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ClientWebSocket _socket;

    private HeadlessChromium(Process process, ClientWebSocket socket)
    {
        _process = process;
        _socket = socket;
        Client = new TestDevToolsClient(socket);
    }

    public TestDevToolsClient Client { get; }

    public static async Task<HeadlessChromium> StartAsync(string executable, string profile, CancellationToken token)
    {
        var process = Process.Start(new ProcessStartInfo(executable)
        {
            ArgumentList =
            {
                "--headless=new", $"--user-data-dir={profile}", "--remote-debugging-address=127.0.0.1",
                "--remote-debugging-port=0", "--no-first-run", "--no-default-browser-check", "about:blank"
            },
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        })!;
        try
        {
            var portFile = Path.Combine(profile, "DevToolsActivePort");
            for (var attempt = 0; attempt < 100 && !File.Exists(portFile); attempt++)
            {
                await Task.Delay(100, token);
            }
            var port = int.Parse((await File.ReadAllLinesAsync(portFile, token))[0]);
            // The page target can be listed a moment after the port is.
            using var http = new HttpClient();
            string? address = null;
            for (var attempt = 0; attempt < 100 && address is null; attempt++)
            {
                using var targets = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", token));
                address = targets.RootElement.EnumerateArray()
                    .Where(item => item.GetProperty("type").GetString() == "page")
                    .Select(item => item.GetProperty("webSocketDebuggerUrl").GetString())
                    .FirstOrDefault();
                if (address is null)
                {
                    await Task.Delay(100, token);
                }
            }
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.Zero;
            await socket.ConnectAsync(new Uri(address ?? throw new InvalidOperationException("Chromium listed no page.")), token);
            return new HeadlessChromium(process, socket);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _socket.Dispose();
        _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync(CancellationToken.None);
        _process.Dispose();
    }
}

internal sealed class TestDevToolsClient(ClientWebSocket socket)
{
    private int _next;

    public async Task<JsonDocument> CallAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = ++_next;
        var request = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
        await socket.SendAsync(request, WebSocketMessageType.Text, true, cancellationToken);
        while (true)
        {
            var message = await ReceiveAsync(cancellationToken);
            if (message.RootElement.TryGetProperty("id", out var answered) && answered.GetInt32() == id)
            {
                Assert.False(message.RootElement.TryGetProperty("error", out var error), $"{method}: {error}");
                return message;
            }
            message.Dispose();
        }
    }

    public async Task WaitForEventAsync(string method, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var message = await ReceiveAsync(cancellationToken);
            if (message.RootElement.TryGetProperty("method", out var name) && name.GetString() == method)
            {
                return;
            }
        }
    }

    private async Task<JsonDocument> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken);
            stream.Write(buffer, 0, received.Count);
            if (received.EndOfMessage)
            {
                return JsonDocument.Parse(stream.ToArray());
            }
        }
    }
}

// The recreation browser held to the recreation over the DevTools protocol,
// with RECORDER_RECREATION_CHROMIUM set.
public sealed class RecreationControlTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recreation-control-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
                return;
            }
            catch (Exception exception) when (attempt < 40 && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }
    }

    [Fact]
    public void TheWindowIsSizedSoItsPageAreaIsTheRecordedViewport()
    {
        Assert.Equal((1266, 795), RecreationControl.WindowSize(new RecreationViewport(1250, 712, 1.5, 1.5), 16, 82.4));
        Assert.Equal((800, 600), RecreationControl.WindowSize(new RecreationViewport(800, 600, 1, 1), -2, 0));
    }

    [Fact]
    public void OnlyTheRecreationAndDevToolsMayBeLoaded()
    {
        const string page = "http://127.0.0.1:5000/token/";
        Assert.True(RecreationControl.IsAllowed(page, page));
        Assert.True(RecreationControl.IsAllowed(page + "?reload", page));
        Assert.True(RecreationControl.IsAllowed("devtools://devtools/bundled/devtools_app.html", page));
        Assert.False(RecreationControl.IsAllowed("http://127.0.0.1:5000/other/", page));
        Assert.False(RecreationControl.IsAllowed("https://www.cnib.ca/en", page));
        Assert.False(RecreationControl.IsAllowed("chrome://settings/", page));
        Assert.False(RecreationControl.IsAllowed("data:text/html,x", page));
    }

    // A followed link, a link to a new tab, and a form submission are each
    // refused; the page stays, its DOM unchanged, the new tab is closed, and
    // the recorded viewport is emulated.
    [Fact]
    public async Task TheRecreationDoesNotLeaveThePage()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_RECREATION_CHROMIUM") is not { } chromium)
        {
            return;
        }
        var token = TestContext.Current.CancellationToken;
        var content = new RecreationContent(
            """
            <!DOCTYPE html><html><head><title>Held</title></head><body>
            <p><a id="away" href="https://example.test/away">away</a></p>
            <p><a id="tab" href="https://example.test/tab" target="_blank">tab</a></p>
            <form id="form" action="https://example.test/form"><button id="submit">submit</button></form>
            </body></html>
            """,
            FixedRecreation.Create().Evidence)
        {
            Viewport = new RecreationViewport(800, 600, 1, 1),
        };
        await using var session = await RecreationSession.OpenAsync(
            chromium, Path.Combine(_directory, "recreation"), content, token, ["--headless=new"]);

        using var http = new HttpClient();
        var list = $"http://127.0.0.1:{session.DevToolsAddress.Port}/json/list";
        string? address = null;
        for (var attempt = 0; attempt < 100 && address is null; attempt++)
        {
            using var targets = JsonDocument.Parse(await http.GetStringAsync(list, token));
            address = targets.RootElement.EnumerateArray()
                .Where(item => item.GetProperty("type").GetString() == "page" &&
                               item.GetProperty("url").GetString() == session.PageAddress)
                .Select(item => item.GetProperty("webSocketDebuggerUrl").GetString())
                .FirstOrDefault();
            if (address is null)
            {
                await Task.Delay(100, token);
            }
        }
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(address!), token);
        var client = new TestDevToolsClient(socket);

        async Task<string> Evaluate(string expression)
        {
            using var answer = await client.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, token);
            return answer.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").ToString();
        }
        async Task Click(string id)
        {
            using var rectangle = JsonDocument.Parse(await Evaluate(
                $"JSON.stringify((r => [r.x + r.width / 2, r.y + r.height / 2])(document.getElementById('{id}').getBoundingClientRect()))"));
            var x = rectangle.RootElement[0].GetDouble();
            var y = rectangle.RootElement[1].GetDouble();
            foreach (var type in new[] { "mousePressed", "mouseReleased" })
            {
                using var _ = await client.CallAsync("Input.dispatchMouseEvent", new { type, x, y, button = "left", clickCount = 1 }, token);
            }
        }
        async Task WaitForBlocked(int count)
        {
            for (var attempt = 0; attempt < 100 && session.Blocked.Count < count; attempt++)
            {
                await Task.Delay(100, token);
            }
        }

        Assert.Equal("800x600", await Evaluate("`${innerWidth}x${innerHeight}`"));
        var before = await Evaluate("document.documentElement.outerHTML");

        await Click("away");
        await WaitForBlocked(1);
        await Click("tab");
        await WaitForBlocked(2);
        await Click("submit");
        await Task.Delay(1000, token);

        var blocked = session.Blocked;
        using (var seen = JsonDocument.Parse(await http.GetStringAsync(list, token)))
        {
            Assert.True(blocked.Count >= 2, $"{string.Join(", ", blocked)} | {string.Join(" ; ", seen.RootElement.EnumerateArray().Select(item => item.GetProperty("type").GetString() + " " + item.GetProperty("url").GetString()![..Math.Min(60, item.GetProperty("url").GetString()!.Length)]))}");
        }
        Assert.Equal("https://example.test/away", blocked[0].Url);
        Assert.True(blocked[0].InRecreationTab);
        Assert.Equal("https://example.test/tab", blocked[1].Url);
        Assert.False(blocked[1].InRecreationTab);
        // The form is stopped by the page's content security policy, before
        // any request, so it is not listed.
        Assert.Equal(2, blocked.Count);
        Assert.Equal(session.PageAddress, await Evaluate("location.href"));
        Assert.Equal(before, await Evaluate("document.documentElement.outerHTML"));
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var targets = JsonDocument.Parse(await http.GetStringAsync(list, token));
            if (targets.RootElement.EnumerateArray().Count(item => item.GetProperty("type").GetString() == "page" &&
                    !item.GetProperty("url").GetString()!.StartsWith("devtools://", StringComparison.Ordinal)) == 1)
            {
                return;
            }
            await Task.Delay(100, token);
        }
        Assert.Fail("The tab opened by the link was not closed.");
    }
}
