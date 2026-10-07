using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Recorder.Recreation;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// Slice 5c: each frame's own evidence, how the recorder serves it, each
/// frame's times from its builder's report, and the panel's Select into
/// frames. See docs/architecture/page-recreation.md, "Build plan for 5c".
/// </summary>
public sealed class RecreationFrameEvidenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recreation-frame-evidence-" + Guid.NewGuid().ToString("N"));

    public RecreationFrameEvidenceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static void Timer(BrowserDocumentState state, string id) =>
        state.Script.Apply(1, 1_000_000_000, "timer-scheduled", Json($$"""
            {"timerId":"{{id}}","timerKind":"timeout","requestedDelayMilliseconds":5000,"effectiveDelayMilliseconds":5000}
            """));

    private static void Listener(BrowserDocumentState state, string id, long node, string eventName) =>
        state.Script.Apply(2, 1_000_000_000, "listener-registered", Json($$$"""
            {"listenerId":"{{{id}}}","eventName":"{{{eventName}}}","capture":false,"once":false,"passive":false,
             "registrationKind":"add-event-listener","target":{"kind":"node","nodeId":{{{node}}},"documentId":"d"}}
            """));

    // One recorded script, with its text, as a frame's resources hold it.
    private static (RecordedScripts Scripts, string Digest) Script(string id, string url, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (new RecordedScripts(
            [new RecordedScriptState(id, "classic", null, url, null, null, 1, 0, null, false, digest, bytes.Length, true, 500_000_000)],
            true,
            new HashSet<string>([digest], StringComparer.Ordinal),
            asked => asked == digest ? bytes : null), digest);
    }

    // The page of slice 5b's tests, with a timer and a listener in the
    // cross-site frame, a script in its nested frame, and a script in the
    // frame not built for its sandbox.
    private static (RecreationContent Content, string Url, string NestedDigest, string SandboxedDigest) Content(int portA, int portB)
    {
        var (top, url, frames) = RecreationFramesTests.Page(portA, portB);
        var cross = frames[0];
        Timer(cross.State!, "timer-cross");
        Listener(cross.State!, "listener-cross", 4, "click");
        var (nestedScripts, nestedDigest) = Script("31", $"http://127.0.0.1:{portA}/nested.js", "document.title = 'nested';");
        var (sandboxedScripts, sandboxedDigest) = Script("41", $"http://127.0.0.1:{portA}/sandboxed.js", "void 'sandboxed';");
        var nested = cross.Children[0] with { Resources = new RecordedPageResources([], new Dictionary<string, RecordedImage>(), (_, _) => null, []) { Scripts = nestedScripts } };
        var changed = frames.ToArray();
        changed[0] = cross with { Children = [nested] };
        changed[5] = frames[5] with { Resources = new RecordedPageResources([], new Dictionary<string, RecordedImage>(), (_, _) => null, []) { Scripts = sandboxedScripts } };
        var content = RecordedPage.Content(top, url, 4_000_000_000, 4_000_000_000, "presented",
            RecreationFramesTests.Images(($"http://127.0.0.1:{portA}/top.png", RecreationFramesTests.Png)), frames: changed);
        return (content, url, nestedDigest, sandboxedDigest);
    }

    [Fact]
    public void EachFrameHasItsOwnEvidenceBuiltOrNot()
    {
        var (content, _, _, _) = Content(8001, 8002);
        Assert.Empty(content.Evidence.Timers);
        Assert.DoesNotContain(content.Evidence.InteractiveElements, item => item.Listeners.Count > 0);

        var cross = content.Frames[0];
        Assert.Equal("/0", cross.Key);
        Assert.Equal("/0/0", cross.Children[0].Key);
        Assert.Equal("/7", content.Frames[7].Key);
        var evidence = cross.Evidence!;
        Assert.Equal("C dom-document-1", evidence.Recreation.DocumentKey);
        Assert.Equal("http://localhost:8002/cross.html", evidence.Recreation.Url);
        Assert.Equal("timer-cross", Assert.Single(evidence.Timers).TimerId);
        var listened = Assert.Single(evidence.InteractiveElements, item => item.Listeners.Count > 0);
        Assert.Equal("/html[1]/body[1]/h1[1]", listened.Node.Display);
        Assert.Contains(evidence.Notes, note => note.StartsWith("The frame of the iframe element 6 at /html[1]/body[1]/iframe[1], at depth 1:", StringComparison.Ordinal));
        Assert.DoesNotContain(evidence.Notes, note => note.Contains("at depth 2", StringComparison.Ordinal));
        Assert.Empty(evidence.Scripts!);

        // The nested frame's script is in its own evidence only.
        var nested = cross.Children[0].Evidence!;
        Assert.Equal("31", Assert.Single(nested.Scripts!).ScriptId);
        Assert.Empty(content.Evidence.Scripts!);

        // A frame not built keeps its evidence, and its resources for its
        // scripts' text; the object's frame, with no scripts, does not.
        var sandboxed = content.Frames[5];
        Assert.Equal("not-built", sandboxed.Way);
        Assert.Equal("41", Assert.Single(sandboxed.Evidence!.Scripts!).ScriptId);
        Assert.NotNull(sandboxed.Resources);
        Assert.Null(content.Frames[4].Resources);
        Assert.NotNull(content.Frames[4].Evidence);

        // Origins, read from the address, or the parent's.
        Assert.Equal("http://localhost:8002", cross.Origin);
        Assert.False(cross.OriginInherited);
        Assert.Equal("http://127.0.0.1:8001", cross.Children[0].Origin);
        Assert.Equal("http://127.0.0.1:8001", content.Frames[2].Origin);
        Assert.True(content.Frames[2].OriginInherited);
        Assert.True(content.Frames[3].OriginInherited);
        Assert.Equal("http://127.0.0.1:8001", content.Frames[5].Origin);
        Assert.Equal("committed by the navigation at 1.000 s", cross.Choice);
        Assert.Equal("presented", cross.Basis);

        // A served frame's key is in its page's data; the top page has none.
        Assert.Contains("\"frameKey\":\"/0\"", cross.Html, StringComparison.Ordinal);
        Assert.Contains("\"frameKey\":\"/0/0\"", cross.Children[0].Html, StringComparison.Ordinal);
        Assert.DoesNotContain("\"frameKey\"", content.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOriginIsReadFromTheAddressOrThePartsOfItsOwner()
    {
        Assert.Equal("https://example.com", RecordedPage.Origin("https://Example.com/a/b?c#d", null, null));
        Assert.Equal("http://example.com:8080", RecordedPage.Origin("http://example.com:8080/", null, null));
        Assert.Equal("https://parent.test", RecordedPage.Origin("about:blank#top", "https://parent.test", null));
        Assert.Equal("https://parent.test", RecordedPage.Origin("about:srcdoc", "https://parent.test", "allow-scripts allow-same-origin"));
        Assert.Equal("opaque", RecordedPage.Origin("https://example.com/", null, "allow-scripts"));
        Assert.Equal("opaque", RecordedPage.Origin("https://example.com/", null, ""));
        Assert.Null(RecordedPage.Origin("data:text/html,x", null, null));
        Assert.Null(RecordedPage.Origin(null, null, null));
    }

    [Fact]
    public async Task TheServerListsEachFramesEvidenceAndAnswersItsScripts()
    {
        var (content, _, nestedDigest, sandboxedDigest) = Content(8001, 8002);
        await using var server = await RecreationServer.StartAsync(content, TestContext.Current.CancellationToken);
        var statuses = server.FrameStatuses.ToDictionary(status => status.Key);
        Assert.Equal(9, statuses.Count);
        Assert.Equal("evidence/0.json", statuses["/0"].EvidenceAddress);
        Assert.Equal("evidence/1.json", statuses["/0/0"].EvidenceAddress);
        Assert.Equal("evidence/2.json", statuses["/1"].EvidenceAddress);
        Assert.Equal("/0", statuses["/0/0"].ParentKey);
        Assert.Equal("", statuses["/0"].ParentKey);
        Assert.Equal("/html[1]/body[1]/iframe[1]", statuses["/0/0"].Owner!.Display);
        Assert.Equal("http://localhost:8002", statuses["/0"].Origin);
        Assert.True(statuses["/2"].OriginInherited);
        Assert.Equal("C dom-document-1", statuses["/0"].DocumentKey);
        Assert.Null(statuses["/0"].Times);

        using var http = new HttpClient();
        var token = TestContext.Current.CancellationToken;
        using (var frame = JsonDocument.Parse(await http.GetStringAsync(server.BaseAddress + "evidence/0.json", token)))
        {
            Assert.Equal("C dom-document-1", frame.RootElement.GetProperty("recreation").GetProperty("documentKey").GetString());
            Assert.Equal("timer-cross", frame.RootElement.GetProperty("timers")[0].GetProperty("timerId").GetString());
        }
        using (var frames = JsonDocument.Parse(await http.GetStringAsync(server.BaseAddress + "frames.json", token)))
        {
            var first = frames.RootElement[0];
            Assert.Equal("evidence/0.json", first.GetProperty("evidenceAddress").GetString());
            Assert.Equal("/html[1]/body[1]/iframe[1]", first.GetProperty("owner").GetProperty("display").GetString());
            Assert.Equal(JsonValueKind.Array, first.GetProperty("owner").GetProperty("scopes").ValueKind);
        }
        foreach (var refused in new[] { "evidence/9.json", "evidence/01.json", "evidence/.json", "evidence/x.json", "evidence/0.jsonx" })
        {
            using var answer = await http.GetAsync(server.BaseAddress + refused, token);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, answer.StatusCode);
        }
        // A frame's script, built or not, is answered from its resources; a
        // digest no document lists is not.
        Assert.Equal("document.title = 'nested';", await http.GetStringAsync(server.BaseAddress + "script/" + nestedDigest, token));
        Assert.Equal("void 'sandboxed';", await http.GetStringAsync(server.BaseAddress + "script/" + sandboxedDigest, token));
        using var missing = await http.GetAsync(server.BaseAddress + "script/" + new string('a', 64), token);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task FramesAreListedWithTheirEvidenceWhenThePageIsNotServed()
    {
        var (top, _, frames) = RecreationFramesTests.Page(8001, 8002);
        var content = RecordedPage.Content(top, "file:///C:/page.html", 4_000_000_000, 4_000_000_000, "presented", frames: frames);
        Assert.All(content.Frames, frame => Assert.Equal("not-built", frame.Way));
        Assert.NotNull(content.Frames[0].Evidence);
        Assert.Equal("http://localhost:8002", content.Frames[0].Origin);
        // A frame not built has no frames of its own listed, as in 5b.
        Assert.Empty(content.Frames[0].Children);
        await using var server = await RecreationServer.StartAsync(content, TestContext.Current.CancellationToken);
        Assert.Equal(8, server.FrameStatuses.Count);
        Assert.Equal("evidence/0.json", server.FrameStatuses.Single(status => status.Key == "/0").EvidenceAddress);
    }

    [Fact]
    public async Task EachFramesTimesAreReadFromTheBuildReports()
    {
        var (content, _, _, _) = Content(8001, 8002);
        await using var server = await RecreationServer.StartAsync(content, TestContext.Current.CancellationToken);
        // Before the top document reports, no frame has times.
        Assert.True(server.DocumentBuilt("""{"frameKey":"/0","timeOrigin":1100.5,"times":{"builderFinished":30,"firstPaint":40},"frames":[]}"""));
        Assert.Null(server.FrameStatuses.Single(status => status.Key == "/0").Times);
        Assert.True(server.DocumentBuilt("""
            {"frameKey":"","timeOrigin":1000,"times":{"builderStarted":5,"builderFinished":50,"firstPaint":60,"notANumber":"x"},
             "frames":[{"ownerNodeId":8,"started":20,"built":25.5},{"ownerNodeId":9,"started":26},{"ownerNodeId":16,"started":30,"built":31}]}
            """));
        var statuses = server.FrameStatuses.ToDictionary(status => status.Key);
        var cross = statuses["/0"].Times!;
        Assert.Equal(100.5, cross.LoadStarted);
        Assert.Equal(130.5, cross.Built);
        Assert.Equal(140.5, cross.FirstPaint);
        Assert.Equal(40, cross.Took);
        var blank = statuses["/2"].Times!;
        Assert.Null(blank.LoadStarted);
        Assert.Equal(25.5, blank.Built);
        Assert.Equal(60, blank.FirstPaint);
        Assert.Equal(5.5, blank.Took);
        Assert.Contains("top document's builder", blank.Basis, StringComparison.Ordinal);
        // The srcdoc frame's build did not finish.
        Assert.Null(statuses["/3"].Times!.Built);
        Assert.Null(statuses["/3"].Times!.Took);
        Assert.Equal(31.0, statuses["/7"].Times!.Built);
        // Not built, and served but not reported.
        Assert.Null(statuses["/4"].Times);
        Assert.Null(statuses["/0/0"].Times);

        // Reports not of the form the builder writes are ignored.
        foreach (var payload in new[]
        {
            "not json",
            "[]",
            """{"frameKey":"/9","timeOrigin":1,"times":{},"frames":[]}""",
            """{"frameKey":"/2","timeOrigin":1,"times":{},"frames":[]}""",
            """{"frameKey":"/4","timeOrigin":1,"times":{},"frames":[]}""",
            """{"frameKey":"","times":{},"frames":[]}""",
            """{"frameKey":"","timeOrigin":"1","times":{},"frames":[]}""",
            """{"frameKey":"","timeOrigin":1,"times":[],"frames":[]}""",
            """{"frameKey":"","timeOrigin":1,"times":{},"frames":[{"ownerNodeId":"8","started":1}]}""",
            """{"frameKey":"","timeOrigin":1,"times":{},"frames":[{"ownerNodeId":8}]}""",
            """{"frameKey":3,"timeOrigin":1,"times":{},"frames":[]}""",
        })
        {
            Assert.False(server.DocumentBuilt(payload), payload);
        }
        Assert.Equal(100.5, server.FrameStatuses.Single(status => status.Key == "/0").Times!.LoadStarted);
    }

    // The panel's Select expression, as its own source writes it, run in
    // the contexts the extension API would choose: the main frame's, and
    // the first frame target at an address. Returns what the panel would
    // say, and the selected node's text in place of inspecting it.
    private sealed class PanelSelect(string findNode, string selectExpression, Func<string?, Task<Func<string, Task<string>>>> context)
    {
        public static PanelSelect From(string panel, Func<string?, Task<Func<string, Task<string>>>> context) => new(
            panel[panel.IndexOf("function findNode", StringComparison.Ordinal)..panel.IndexOf("// Selects the node", StringComparison.Ordinal)],
            panel[panel.IndexOf("function selectExpression", StringComparison.Ordinal)..panel.IndexOf("function withoutFragment", StringComparison.Ordinal)],
            context);

        public async Task<string> SelectAsync(IReadOnlyList<(NodePath Owner, string Key, string Url)> chain, NodePath path)
        {
            var top = await context(null);
            var from = 0;
            string? expectedKey = null;
            var evaluate = top;
            for (var round = 0; round < 10; round++)
            {
                var owners = chain.Skip(from).Select(link => link.Owner.Scopes).ToArray();
                var build = $"(() => {{ {findNode} {selectExpression} return selectExpression({JsonSerializer.Serialize(owners)}, {JsonSerializer.Serialize(path.Scopes)}, {JsonSerializer.Serialize(expectedKey)}); }})()";
                var expression = (await top(build))
                    .Replace("inspect(found.node);\n    return JSON.stringify({ result: \"selected\" });", "return JSON.stringify({ result: \"selected\", text: found.node.textContent });", StringComparison.Ordinal);
                Assert.Contains("text: found.node.textContent", expression, StringComparison.Ordinal);
                using var answer = JsonDocument.Parse(await evaluate(expression));
                var result = answer.RootElement.GetProperty("result").GetString();
                if (result == "selected")
                {
                    return "selected: " + answer.RootElement.GetProperty("text").GetString();
                }
                if (result != "cross")
                {
                    return result!;
                }
                var link = chain[from + answer.RootElement.GetProperty("owner").GetInt32()];
                from += answer.RootElement.GetProperty("owner").GetInt32() + 1;
                expectedKey = link.Key;
                evaluate = await context(link.Url);
            }
            return "no end";
        }
    }

    // Set RECORDER_RECREATION_CHROMIUM to a Chromium executable to open the
    // page of slice 5b's tests with every site in a process of its own, and
    // check that every built document reports its times, and that the
    // panel's Select expression selects a node in a same-origin frame, a
    // cross-site frame, a frame of that frame, and frames built in place,
    // and is refused in a closed shadow root.
    [Fact]
    public async Task EachFrameReportsItsTimesAndItsNodesAreSelectedThroughItsOwners()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_RECREATION_CHROMIUM") is not { } chromium)
        {
            return;
        }
        var token = TestContext.Current.CancellationToken;
        using var listenerA = new RecreationFramesTests.Listener();
        using var listenerB = new RecreationFramesTests.Listener();
        var (content, url, _, _) = Content(listenerA.Port, listenerB.Port);
        content = content with { Viewport = new RecreationViewport(800, 600, 1, 1) };
        await using var session = await RecreationSession.OpenAsync(
            chromium, Path.Combine(_directory, "frames"), content, token, ["--headless=new", "--site-per-process"]);

        var expected = new[] { "/0", "/0/0", "/1", "/2", "/3", "/6", "/7" };
        IReadOnlyDictionary<string, RecreationFrameStatus> statuses = new Dictionary<string, RecreationFrameStatus>();
        for (var attempt = 0; attempt < 150; attempt++)
        {
            statuses = session.FrameStatuses.ToDictionary(status => status.Key);
            if (expected.All(key => statuses[key].Times is { Built: not null }))
            {
                break;
            }
            await Task.Delay(100, token);
        }
        foreach (var key in expected)
        {
            var times = statuses[key].Times;
            Assert.True(times is { Built: not null, FirstPaint: not null, Took: not null }, key);
            Assert.InRange(times!.Built!.Value, 0, 60_000);
        }
        Assert.InRange(statuses["/0"].Times!.LoadStarted!.Value, 0, 60_000);
        Assert.True(statuses["/0/0"].Times!.LoadStarted >= statuses["/0"].Times!.LoadStarted);
        Assert.Null(statuses["/4"].Times);
        Assert.Null(statuses["/5"].Times);

        var panel = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Recorder.Recreation", "EvidencePanel", "panel.js"), token);
        using var http = new HttpClient();
        var list = $"http://127.0.0.1:{session.DevToolsAddress.Port}/json/list";
        var clients = new List<ClientWebSocket>();
        async Task<Func<string, Task<string>>> Context(string? address)
        {
            string? socketAddress = null;
            for (var attempt = 0; attempt < 100 && socketAddress is null; attempt++)
            {
                using var targets = JsonDocument.Parse(await http.GetStringAsync(list, token));
                socketAddress = targets.RootElement.EnumerateArray()
                    .Where(item => address is null
                        ? item.GetProperty("type").GetString() == "page" && item.GetProperty("url").GetString() == url
                        : item.GetProperty("type").GetString() == "iframe" && item.GetProperty("url").GetString() == address)
                    .Select(item => item.GetProperty("webSocketDebuggerUrl").GetString())
                    .FirstOrDefault();
                if (socketAddress is null)
                {
                    await Task.Delay(100, token);
                }
            }
            Assert.NotNull(socketAddress);
            var socket = new ClientWebSocket();
            clients.Add(socket);
            await socket.ConnectAsync(new Uri(socketAddress!), token);
            var client = new TestDevToolsClient(socket);
            return async expression =>
            {
                using var answer = await client.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, token);
                return answer.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").ToString();
            };
        }
        try
        {
            var select = PanelSelect.From(panel, Context);
            var a = $"http://127.0.0.1:{listenerA.Port}";
            var b = $"http://localhost:{listenerB.Port}";
            var crossLink = (content.Frames[0].OwnerPath!, "/0", $"{b}/cross.html");
            var nestedLink = (content.Frames[0].Children[0].OwnerPath!, "/0/0", $"{a}/nested.html");
            var h1 = NodePath.Of("/html[1]/body[1]/h1[1]");
            Assert.Equal("selected: cross child", await select.SelectAsync([crossLink], h1));
            Assert.Equal("selected: nested child", await select.SelectAsync([crossLink, nestedLink], h1));
            Assert.Equal("selected: moved child", await select.SelectAsync([(content.Frames[1].OwnerPath!, "/1", $"{a}/moved-to.html")], h1));
            Assert.Equal("selected: built in place", await select.SelectAsync([(content.Frames[2].OwnerPath!, "/2", "about:blank")], NodePath.Of("/html[1]/body[1]/h1[1]")));
            Assert.Equal("selected: built after srcdoc", await select.SelectAsync([(content.Frames[3].OwnerPath!, "/3", "about:srcdoc")], NodePath.Of("/html[1]/body[1]/p[1]")));
            Assert.Equal("no-shadow-root", await select.SelectAsync([(content.Frames[6].OwnerPath!, "/6", $"{a}/shadowed.html")], h1));
            Assert.Equal("not-found", await select.SelectAsync([crossLink], NodePath.Of("/html[1]/body[1]/h2[1]")));
            // A node of the top document itself: an owner, whose text is empty.
            Assert.Equal("selected: ", await select.SelectAsync([], NodePath.Of("/html[1]/body[1]/iframe[3]")));
        }
        finally
        {
            foreach (var socket in clients)
            {
                socket.Dispose();
            }
        }
        Assert.Equal(0, listenerA.Connections);
        Assert.Equal(0, listenerB.Connections);
    }

    // Two cross-site frames at one address: the extension API reaches the
    // first only, so the second's key check refuses it, as the panel says.
    [Fact]
    public async Task OfTwoCrossSiteFramesAtOneAddressOnlyOneIsSelected()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_RECREATION_CHROMIUM") is not { } chromium)
        {
            return;
        }
        var token = TestContext.Current.CancellationToken;
        using var listenerA = new RecreationFramesTests.Listener();
        using var listenerB = new RecreationFramesTests.Listener();
        var a = $"http://127.0.0.1:{listenerA.Port}";
        var b = $"http://localhost:{listenerB.Port}";
        var top = RecreationFramesTests.Tree(
            (1, null, "document", "#document", null, null),
            (3, 1, "element", "HTML", null, null),
            (4, 3, "element", "HEAD", null, null),
            (5, 3, "element", "BODY", null, null),
            (6, 5, "element", "IFRAME", null, [("id", "twin-1"), ("src", $"{b}/twin.html")]),
            (7, 5, "element", "IFRAME", null, [("id", "twin-2"), ("src", $"{b}/twin.html")]));
        DomDocumentTree Twin(string text) => RecreationFramesTests.Tree(
            (1, null, "document", "#document", null, null),
            (2, 1, "element", "HTML", null, null),
            (3, 2, "element", "BODY", null, null),
            (4, 3, "element", "H1", null, null),
            (5, 4, "text", "#text", text, null));
        var frames = new[]
        {
            new RecordedFrame(6, "F-1", "W1 dom-document-1", $"{b}/twin.html", "committed", RecreationFramesTests.State("W1 dom-document-1", Twin("first twin")), "presented", false, []),
            new RecordedFrame(7, "F-2", "W2 dom-document-1", $"{b}/twin.html", "committed", RecreationFramesTests.State("W2 dom-document-1", Twin("second twin")), "presented", false, []),
        };
        var url = $"{a}/top.html";
        var content = RecordedPage.Content(RecreationFramesTests.State("T dom-document-1", top), url, 4_000_000_000, 4_000_000_000, "presented", frames: frames) with
        {
            Viewport = new RecreationViewport(800, 600, 1, 1),
        };
        await using var session = await RecreationSession.OpenAsync(
            chromium, Path.Combine(_directory, "twins"), content, token, ["--headless=new", "--site-per-process"]);
        for (var attempt = 0; attempt < 150 && session.FrameStatuses.Any(status => status.Times is null); attempt++)
        {
            await Task.Delay(100, token);
        }
        Assert.All(session.FrameStatuses, status => Assert.NotNull(status.Times));

        var panel = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Recorder.Recreation", "EvidencePanel", "panel.js"), token);
        using var http = new HttpClient();
        var list = $"http://127.0.0.1:{session.DevToolsAddress.Port}/json/list";
        var clients = new List<ClientWebSocket>();
        async Task<Func<string, Task<string>>> Context(string? address)
        {
            using var targets = JsonDocument.Parse(await http.GetStringAsync(list, token));
            // The first target at the address, for both twins, as the
            // extension API takes the first frame it finds there.
            var socketAddress = targets.RootElement.EnumerateArray()
                .Where(item => address is null
                    ? item.GetProperty("type").GetString() == "page" && item.GetProperty("url").GetString() == url
                    : item.GetProperty("type").GetString() == "iframe" && item.GetProperty("url").GetString() == address)
                .Select(item => item.GetProperty("webSocketDebuggerUrl").GetString())
                .First();
            var socket = new ClientWebSocket();
            clients.Add(socket);
            await socket.ConnectAsync(new Uri(socketAddress!), token);
            var client = new TestDevToolsClient(socket);
            return async expression =>
            {
                using var answer = await client.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, token);
                return answer.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").ToString();
            };
        }
        try
        {
            var select = PanelSelect.From(panel, Context);
            var h1 = NodePath.Of("/html[1]/body[1]/h1[1]");
            var results = new[]
            {
                await select.SelectAsync([(content.Frames[0].OwnerPath!, "/0", $"{b}/twin.html")], h1),
                await select.SelectAsync([(content.Frames[1].OwnerPath!, "/1", $"{b}/twin.html")], h1),
            };
            Assert.Single(results, result => result == "other-frame");
            Assert.Single(results, result => result is "selected: first twin" or "selected: second twin");
        }
        finally
        {
            foreach (var socket in clients)
            {
                socket.Dispose();
            }
        }
    }
}
