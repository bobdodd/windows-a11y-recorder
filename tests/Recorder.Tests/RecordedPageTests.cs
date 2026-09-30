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
    }

    // The tree as recorded, one line per node, in the order the DevTools
    // protocol gives: each node's shadow root before its children. The
    // attribute cut in the recording is not built, and is left out; user
    // agent shadow roots are left out on both sides.
    private static string Describe(DomDocumentTree tree, long id)
    {
        var lines = new StringBuilder();
        void Walk(long nodeId, int depth)
        {
            var node = tree.Nodes[nodeId];
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
                    .Append(' ').Append(node.Data ?? "").Append('\n');
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
    private static string Local(string key) => key switch
    {
        "{http://www.w3.org/2000/xmlns/}xlink" => "xmlns:xlink",
        "{http://www.w3.org/1999/xlink}href" => "href",
        _ => key,
    };

    private static string Describe(JsonElement root)
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
                    .Append(string.Join(" ", attributes)).Append(' ').Append(data).Append('\n');
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
        Client = new DevToolsConnection(socket);
    }

    public DevToolsConnection Client { get; }

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
            using var http = new HttpClient();
            using var targets = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", token));
            var page = targets.RootElement.EnumerateArray().First(item => item.GetProperty("type").GetString() == "page");
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.Zero;
            await socket.ConnectAsync(new Uri(page.GetProperty("webSocketDebuggerUrl").GetString()!), token);
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

internal sealed class DevToolsConnection(ClientWebSocket socket)
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
