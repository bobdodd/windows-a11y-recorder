using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Recreation;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// Slice 5b: the document each frame shows at a frame, the frames of a
/// recreation, how each is built, and how the recorder answers their
/// requests. See docs/architecture/page-recreation.md, "Build plan for 5b".
/// </summary>
public sealed class RecreationFramesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recreation-frames-" + Guid.NewGuid().ToString("N"));

    public RecreationFramesTests() => Directory.CreateDirectory(_directory);

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

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static IReadOnlyDictionary<string, IReadOnlyList<long>> Commits(params (string Token, long Time)[] commits) =>
        commits.GroupBy(item => item.Token).ToDictionary(group => group.Key, group => (IReadOnlyList<long>)group.Select(item => item.Time).ToList());

    [Fact]
    public void AFrameShowsTheDocumentCommittedLastAtOrBeforeTheTime()
    {
        // The frame's initial empty document in its parent's process, then
        // a cross-site document committed in another process.
        var documents = new[]
        {
            new FrameDocumentRecord(100, "E1 dom-document-1", "F", false, 10),
            new FrameDocumentRecord(250, "C1 dom-document-1", "F", true, 20),
            new FrameDocumentRecord(90, "X1 dom-document-1", "OTHER", false, 10),
        };
        var commits = Commits(("C1", 200));
        Assert.Null(BrowserFrames.Choose("F", 50, documents, commits));
        var initial = BrowserFrames.Choose("F", 150, documents, commits)!;
        Assert.Equal("E1 dom-document-1", initial.Document.DocumentKey);
        Assert.False(initial.Committed);
        Assert.Equal(100, initial.From);
        // Committed at 200, before its first walk at 250: it is shown, though
        // its state has no DOM walk yet.
        var committed = BrowserFrames.Choose("F", 220, documents, commits)!;
        Assert.Equal("C1 dom-document-1", committed.Document.DocumentKey);
        Assert.True(committed.Committed);
        Assert.Equal(200, committed.From);
        Assert.Equal("C1 dom-document-1", BrowserFrames.Choose("F", 400, documents, commits)!.Document.DocumentKey);
    }

    [Fact]
    public void OfTwoDocumentsUnderOneTokenTheLaterIsShown()
    {
        // As the script-written about:blank frame in the 5a recording: two
        // document identities under one document token and one commit.
        var documents = new[]
        {
            new FrameDocumentRecord(300, "B1 dom-document-122", "F", false, 10),
            new FrameDocumentRecord(310, "B1 dom-document-126", "F", false, 10),
        };
        var chosen = BrowserFrames.Choose("F", 500, documents, Commits(("B1", 290)))!;
        Assert.Equal("B1 dom-document-126", chosen.Document.DocumentKey);
        Assert.Equal(290, chosen.From);
        // A later navigation of the same frame replaces both.
        var later = documents.Append(new FrameDocumentRecord(420, "N1 dom-document-1", "F", false, 10)).ToArray();
        Assert.Equal("N1 dom-document-1", BrowserFrames.Choose("F", 500, later, Commits(("B1", 290), ("N1", 400)))!.Document.DocumentKey);
        Assert.Equal("B1 dom-document-126", BrowserFrames.Choose("F", 399, later, Commits(("B1", 290), ("N1", 400)))!.Document.DocumentKey);
    }

    [Fact]
    public void ThePlaybackIndexKeepsTheFirstWalkOfEachDocumentThatNamedItsFrame()
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        RecorderEvent Walk(long time, string document, string? frameJson)
        {
            var frame = frameJson is null ? "" : "," + frameJson;
            var payload = $$"""{"checkpointId":"c","context":{"browserInstanceId":"b","processId":7,"documentId":"{{document}}","documentToken":"T"}{{frame}}}""";
            return DatabaseTestSupport.Event("s", DatabaseTestSupport.Collector("test.browser", "browser.dom"), (ulong)time, time, "browser.dom", "dom-checkpoint-started") with
            {
                Payload = DatabaseTestSupport.Json(payload),
            };
        }
        builder.Add(1, Walk(500, "dom-document-1", "\"frameToken\":\"F\",\"mainFrame\":false"));
        builder.Add(2, Walk(300, "dom-document-1", "\"frameToken\":\"F\",\"mainFrame\":false"));
        builder.Add(3, Walk(400, "dom-document-2", null));
        builder.Add(4, Walk(450, "dom-document-3", "\"frameToken\":null,\"mainFrame\":null"));
        var index = builder.Build();
        Assert.Equal(3, index.Version);
        var kept = Assert.Single(index.FrameDocuments);
        Assert.Equal(new FrameDocumentRecord(300, "T dom-document-1", "F", false, 7), kept);
    }

    // A tree from (id, parent, type, name, data, attributes) rows.
    private static DomDocumentTree Tree(params (long Id, long? Parent, string Type, string Name, string? Data, (string, string)[]? Attributes)[] rows)
    {
        var tree = new DomDocumentTree();
        foreach (var (id, parent, type, name, data, attributes) in rows)
        {
            // A shadow root's data is its recorded fields.
            var shadow = type == "shadow-root";
            var node = new DomNode(id)
            {
                ParentId = parent,
                NodeType = type,
                NodeName = name,
                Data = shadow ? null : data,
                ShadowRootFields = shadow ? data : null,
            };
            foreach (var (key, value) in attributes ?? [])
            {
                node.Attributes[key] = value;
            }
            tree.Nodes.Add(id, node);
            if (parent is { } parentId)
            {
                if (shadow)
                {
                    tree.Nodes[parentId].ShadowRootId = id;
                }
                else
                {
                    tree.Nodes[parentId].Children.Add(id);
                }
            }
        }
        return tree;
    }

    private static BrowserDocumentState State(string key, DomDocumentTree tree) =>
        new(key) { Dom = tree, DomCompleteness = BrowserStateCompleteness.Complete };

    private static RecordedPageResources Images(params (string Url, byte[] Bytes)[] images) =>
        new(
            [],
            images.ToDictionary(item => item.Url, item => new RecordedImage(item.Url, item.Url, 200, "image/png", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(item.Bytes)))),
            (kind, digest) => kind == "image-data"
                ? images.Select(item => item.Bytes).FirstOrDefault(bytes => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) == digest)
                : null,
            []);

    // The page of the integration test, at two loopback addresses that no
    // server answers as the recorded page: a top document with a cross-site
    // frame holding a frame of its own, a frame whose owner asks for another
    // address than its document's, a frame built in place, a srcdoc frame,
    // an object, and a frame sandboxed without allow-scripts.
    private static (BrowserDocumentState Top, string TopUrl, IReadOnlyList<RecordedFrame> Frames) Page(int portA, int portB)
    {
        var a = $"http://127.0.0.1:{portA}";
        var b = $"http://localhost:{portB}";
        var top = Tree(
            (1, null, "document", "#document", null, null),
            (3, 1, "element", "HTML", null, null),
            (4, 3, "element", "HEAD", null, null),
            (5, 3, "element", "BODY", null, null),
            (6, 5, "element", "IFRAME", null, [("id", "cross"), ("src", $"{b}/cross.html")]),
            (7, 5, "element", "IFRAME", null, [("id", "moved"), ("src", "moved-from.html")]),
            (8, 5, "element", "IFRAME", null, [("id", "blank")]),
            (9, 5, "element", "IFRAME", null, [("id", "sd"), ("srcdoc", "<p>markup</p><script>parent.document.title = 'ran'</script>")]),
            (10, 5, "element", "OBJECT", null, [("id", "object"), ("data", $"{a}/object.html")]),
            (11, 5, "element", "IFRAME", null, [("id", "sandboxed"), ("sandbox", "allow-same-origin"), ("src", $"{a}/sandboxed.html")]),
            (12, 5, "element", "IMG", null, [("id", "image"), ("src", "top.png")]),
            (13, 5, "element", "X-HOST", null, null),
            (14, 13, "shadow-root", "#document-fragment", "13,\"closed\",false,\"named\",false,false,false,false,null", null),
            (15, 14, "element", "IFRAME", null, [("id", "shadowed"), ("src", $"{a}/shadowed.html")]),
            (16, 5, "element", "IFRAME", null, [("id", "explicit"), ("src", "about:blank")]));
        var cross = Tree(
            (1, null, "document", "#document", null, null),
            (2, 1, "element", "HTML", null, null),
            (3, 2, "element", "BODY", null, null),
            (4, 3, "element", "H1", null, null),
            (5, 4, "text", "#text", "cross child", null),
            (6, 3, "element", "IMG", null, [("id", "image"), ("src", "c.png")]),
            (7, 3, "element", "IFRAME", null, [("id", "nested"), ("src", $"{a}/nested.html")]));
        var nested = Tree(
            (1, null, "document", "#document", null, null),
            (2, 1, "element", "HTML", null, null),
            (3, 2, "element", "BODY", null, null),
            (4, 3, "element", "H1", null, null),
            (5, 4, "text", "#text", "nested child", null));
        var moved = Tree(
            (1, null, "document", "#document", null, null),
            (2, 1, "element", "HTML", null, null),
            (3, 2, "element", "BODY", null, null),
            (4, 3, "element", "H1", null, null),
            (5, 4, "text", "#text", "moved child", null));
        var blank = Tree(
            (1, null, "document", "#document", null, null),
            (2, 1, "element", "HTML", null, null),
            (3, 2, "element", "HEAD", null, null),
            (4, 2, "element", "BODY", null, null),
            (5, 4, "element", "H1", null, null),
            (6, 5, "text", "#text", "built in place", null),
            (7, 4, "element", "IMG", null, [("id", "image"), ("src", "blank.png")]));
        var srcdoc = Tree(
            (1, null, "document", "#document", null, null),
            (2, 1, "element", "HTML", null, null),
            (3, 2, "element", "BODY", null, null),
            (4, 3, "element", "P", null, null),
            (5, 4, "text", "#text", "built after srcdoc", null));
        var shadowed = Tree(
            (1, null, "document", "#document", null, null),
            (2, 1, "element", "HTML", null, null),
            (3, 2, "element", "BODY", null, null),
            (4, 3, "element", "H1", null, null),
            (5, 4, "text", "#text", "shadowed child", null),
            (6, 3, "element", "A", null, [("id", "away"), ("href", "elsewhere.html")]),
            (7, 6, "text", "#text", "away", null));
        var explicitBlank = Tree(
            (1, null, "document", "#document", null, null),
            (2, 1, "element", "HTML", null, null),
            (3, 2, "element", "BODY", null, null),
            (4, 3, "element", "H1", null, null),
            (5, 4, "text", "#text", "explicit blank", null));
        var frames = new[]
        {
            new RecordedFrame(6, "F-cross", "C dom-document-1", $"{b}/cross.html", "committed by the navigation at 1.000 s", State("C dom-document-1", cross), "presented", false,
            [
                new RecordedFrame(7, "F-nested", "N dom-document-1", $"{a}/nested.html", "committed", State("N dom-document-1", nested), "presented", false, []),
            ])
            {
                Resources = Images(($"{b}/c.png", Png)),
            },
            new RecordedFrame(7, "F-moved", "M dom-document-1", $"{a}/moved-to.html", "committed", State("M dom-document-1", moved), "presented", true, []),
            new RecordedFrame(8, "F-blank", "B dom-document-1", null, "from its first DOM walk", State("B dom-document-1", blank), "presented", true, [])
            {
                Resources = Images(($"{a}/blank.png", Png)),
            },
            new RecordedFrame(9, "F-sd", "S dom-document-1", "about:srcdoc", "committed", State("S dom-document-1", srcdoc), "presented", true, []),
            new RecordedFrame(10, "F-object", "O dom-document-1", $"{a}/object.html", "committed", State("O dom-document-1", nested), "presented", true, []),
            new RecordedFrame(11, "F-sandboxed", "X dom-document-1", $"{a}/sandboxed.html", "committed", State("X dom-document-1", nested), "presented", true, []),
            new RecordedFrame(15, "F-shadowed", "H dom-document-1", $"{a}/shadowed.html", "committed", State("H dom-document-1", shadowed), "presented", true, []),
            new RecordedFrame(16, "F-explicit", "E dom-document-1", "about:blank", "committed", State("E dom-document-1", explicitBlank), "presented", true, []),
        };
        return (State("T dom-document-1", top), $"{a}/top.html", frames);
    }

    [Fact]
    public async Task EachFrameIsWrittenAndAnsweredAsItIsBuilt()
    {
        var (top, url, frames) = Page(8001, 8002);
        var content = RecordedPage.Content(top, url, 4_000_000_000, 4_000_000_000, "presented", Images(("http://127.0.0.1:8001/top.png", Png)), frames: frames);
        Assert.Equal(["served", "served", "in-place", "srcdoc", "not-built", "not-built", "served", "in-place"], content.Frames.Select(frame => frame.Way));
        Assert.Equal("/html[1]/body[1]/x-host[1]/#shadow-root(closed)/iframe[1]", content.Frames[6].OwnerPath!.Display);
        Assert.Equal("/html[1]/body[1]/iframe[1]", content.Frames[0].OwnerPath!.Display);
        Assert.Equal("http://127.0.0.1:8001/moved-from.html", content.Frames[1].OwnerAddress);
        Assert.Null(content.Frames[0].OwnerAddress);
        Assert.Contains("an object element's frame is not built", content.Frames[4].Reason, StringComparison.Ordinal);
        Assert.Contains("sandboxed without allow-scripts", content.Frames[5].Reason, StringComparison.Ordinal);
        Assert.Equal("served", Assert.Single(content.Frames[0].Children).Way);
        Assert.Contains(content.Evidence.Notes, note => note.StartsWith("The frame of the iframe element 6 at /html[1]/body[1]/iframe[1], at depth 1: It showed document C dom-document-1", StringComparison.Ordinal));
        Assert.Contains(content.Evidence.Notes, note => note.Contains("at depth 2", StringComparison.Ordinal) && note.Contains("N dom-document-1", StringComparison.Ordinal));
        // The trees of the frames built in place are in the top page's data,
        // and a served frame's own frames are in its page.
        Assert.Contains("built in place", content.Html, StringComparison.Ordinal);
        Assert.Contains("built after srcdoc", content.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("cross child", content.Html, StringComparison.Ordinal);
        Assert.Contains("cross child", content.Frames[0].Html, StringComparison.Ordinal);
        Assert.DoesNotContain("nested child", content.Frames[0].Html, StringComparison.Ordinal);
        Assert.Contains("nested child", content.Frames[0].Children[0].Html, StringComparison.Ordinal);

        await using var server = await RecreationServer.StartAsync(content, TestContext.Current.CancellationToken);
        string Policy(RecreationAnswer answer) => answer.Headers.Single(header => header.Key == "Content-Security-Policy").Value;
        var page = server.Answer(url, "Document", "")!;
        Assert.Contains(
            "frame-src http://127.0.0.1:8001/moved-from.html http://127.0.0.1:8001/moved-to.html http://127.0.0.1:8001/shadowed.html http://localhost:8002/cross.html https://127.0.0.1:8001/moved-from.html https://127.0.0.1:8001/moved-to.html https://127.0.0.1:8001/shadowed.html https://localhost:8002/cross.html;",
            Policy(page), StringComparison.Ordinal);
        var crossKey = server.ChildKey("", content.Frames[0].OwnerPath!)!;
        Assert.Equal("/0", crossKey);
        var cross = server.Answer("http://localhost:8002/cross.html", "Document", crossKey)!;
        Assert.Equal(200, cross.Status);
        Assert.Contains("frame-src http://127.0.0.1:8001/nested.html https://127.0.0.1:8001/nested.html;", Policy(cross), StringComparison.Ordinal);
        Assert.DoesNotContain(content.ScriptNonce!, Policy(cross), StringComparison.Ordinal);
        // A frame's image is answered from its own document's resources only.
        Assert.Equal(Png, server.Answer("http://localhost:8002/c.png", "Image", crossKey)!.Body);
        Assert.Null(server.Answer("http://localhost:8002/c.png", "Image", ""));
        Assert.Null(server.Answer("http://127.0.0.1:8001/top.png", "Image", crossKey));
        Assert.Equal("/0/0", server.ChildKey(crossKey, NodePath.Of("/html[1]/body[1]/iframe[1]")));
        Assert.Null(server.ChildKey(crossKey, NodePath.Of("/html[1]/body[1]/iframe[2]")));
        Assert.Null(server.ChildKey("/9", NodePath.Of("/html[1]/body[1]/iframe[1]")));
        // The moved frame's owner address is answered with a redirect.
        var movedKey = server.ChildKey("", NodePath.Of("/html[1]/body[1]/iframe[2]"))!;
        var redirect = server.Answer("http://127.0.0.1:8001/moved-from.html", "Document", movedKey)!;
        Assert.Equal(302, redirect.Status);
        Assert.Equal("http://127.0.0.1:8001/moved-to.html", redirect.Headers.Single(header => header.Key == "Location").Value);
        Assert.Equal(200, server.Answer("http://127.0.0.1:8001/moved-to.html", "Document", movedKey)!.Status);
        Assert.Null(server.Answer("http://127.0.0.1:8001/other.html", "Document", movedKey));
        // A frame built in place has its resources answered, not documents.
        var blankKey = server.ChildKey("", NodePath.Of("/html[1]/body[1]/iframe[3]"))!;
        Assert.Equal(Png, server.Answer("http://127.0.0.1:8001/blank.png", "Image", blankKey)!.Body);
        Assert.Null(server.Answer("http://127.0.0.1:8001/blank.html", "Document", blankKey));
        // A frame not built has a key, and nothing is answered for it.
        var sandboxedKey = server.ChildKey("", NodePath.Of("/html[1]/body[1]/iframe[5]"))!;
        Assert.Null(server.Answer("http://127.0.0.1:8001/sandboxed.html", "Document", sandboxedKey));
        // Its load is refused, but it is not a navigation the recreation
        // blocked, so the control does not list it as one.
        Assert.False(server.FrameAskedFor(sandboxedKey));
        Assert.True(server.FrameAskedFor(movedKey));
        Assert.Equal(9, server.FrameStatuses.Count);
        Assert.Equal("/6", server.ChildKey("", NodePath.Create(["/html[1]/body[1]/x-host[1]", "/iframe[1]"], ["closed"])));
        Assert.Null(server.ChildKey("", NodePath.Create(["/html[1]/body[1]/x-host[1]", "/iframe[1]"], ["open"])));
    }

    [Fact]
    public void FramesAreNotBuiltWhenThePageIsNotServedAtItsRecordedAddress()
    {
        var (top, _, frames) = Page(8001, 8002);
        var content = RecordedPage.Content(top, "file:///C:/page.html", 4_000_000_000, 4_000_000_000, "presented", frames: frames);
        Assert.All(content.Frames, frame => Assert.Equal("not-built", frame.Way));
        Assert.Contains("frame-src 'none'", RecreationServer.WithFrameSources(RecreationServer.PageContentSecurityPolicy, content.Frames), StringComparison.Ordinal);
    }

    [Fact]
    public void AFrameSourceHoldsNoQueryAndEscapesSemicolonsAndCommas()
    {
        var frames = new[]
        {
            new RecreationFrame(1, NodePath.Of("/html[1]"), "iframe", "served", "http://example.test/a;b,c.html?x=1#y", []),
        };
        Assert.Contains(
            "frame-src http://example.test/a%3Bb%2Cc.html https://example.test/a%3Bb%2Cc.html;",
            RecreationServer.WithFrameSources(RecreationServer.PageContentSecurityPolicy, frames),
            StringComparison.Ordinal);
    }

    // Counts the connections made to a loopback port: any is a request that
    // reached the network.
    private sealed class Listener : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private int _connections;

        public Listener()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Connections => Volatile.Read(ref _connections);

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _connections);
                    _ = ReadAsync(client);
                }
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
            }
        }

        public List<string> Requests { get; } = [];

        private async Task ReadAsync(TcpClient client)
        {
            using (client)
            {
                var buffer = new byte[4096];
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var read = await client.GetStream().ReadAsync(buffer, timeout.Token);
                    if (read > 0)
                    {
                        lock (Requests)
                        {
                            Requests.Add(Encoding.ASCII.GetString(buffer, 0, read).Split('\n')[0].Trim());
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
                {
                }
            }
        }

        public void Dispose() => _listener.Stop();
    }

    // Set RECORDER_RECREATION_CHROMIUM to a Chromium executable to open the
    // page above with every site in a process of its own, and check that
    // each frame is built under its owner, that the cross-site frame and
    // its own frame are out of process and answered in their own sessions,
    // that nothing reaches either loopback port, and that the browser's own
    // session refused nothing, since every request was paused in a frame
    // session. Stock Chromium has no recreation mode, so nothing of the
    // recorded style or layout is checked.
    [Fact]
    public async Task EachFrameIsBuiltUnderItsOwnerAndNothingReachesTheNetwork()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_RECREATION_CHROMIUM") is not { } chromium)
        {
            return;
        }
        var token = TestContext.Current.CancellationToken;
        using var listenerA = new Listener();
        using var listenerB = new Listener();
        var (top, url, frames) = Page(listenerA.Port, listenerB.Port);
        var content = RecordedPage.Content(
            top, url, 4_000_000_000, 4_000_000_000, "presented",
            Images(($"http://127.0.0.1:{listenerA.Port}/top.png", Png)), frames: frames) with
        {
            Viewport = new RecreationViewport(800, 600, 1, 1),
        };
        await using var session = await RecreationSession.OpenAsync(
            chromium, Path.Combine(_directory, "frames"), content, token, ["--headless=new", "--site-per-process"]);

        using var http = new HttpClient();
        var list = $"http://127.0.0.1:{session.DevToolsAddress.Port}/json/list";
        async Task<string?> Target(string type, string address)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                using var targets = JsonDocument.Parse(await http.GetStringAsync(list, token));
                var found = targets.RootElement.EnumerateArray()
                    .Where(item => item.GetProperty("type").GetString() == type && item.GetProperty("url").GetString() == address)
                    .Select(item => item.GetProperty("webSocketDebuggerUrl").GetString())
                    .FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }
                await Task.Delay(100, token);
            }
            return null;
        }
        TestDevToolsClient? topClient = null;
        async Task<Func<string, Task<string>>> Connect(string? address)
        {
            Assert.NotNull(address);
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(address!), token);
            var client = new TestDevToolsClient(socket);
            topClient ??= client;
            return async expression =>
            {
                using var answer = await client.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, token);
                return answer.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").ToString();
            };
        }
        async Task<string> Until(Func<string, Task<string>> evaluate, string expression, string expected)
        {
            var value = "";
            for (var attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    value = await evaluate(expression);
                }
                catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
                {
                    value = "";
                }
                if (value == expected)
                {
                    break;
                }
                await Task.Delay(100, token);
            }
            return value;
        }

        var page = await Connect(await Target("page", url));
        Assert.Equal("True", await Until(page, "Boolean(window.__recorderRecreation && window.__recorderRecreation.built)", "True"));
        Assert.Equal("built in place", await page("document.getElementById('blank').contentDocument.querySelector('h1').textContent"));
        Assert.Equal("about:blank", await page("document.getElementById('blank').contentDocument.URL"));
        Assert.Equal("built after srcdoc", await page("document.getElementById('sd').contentDocument.querySelector('p').textContent"));
        Assert.Equal("", await page("document.title"));
        Assert.Equal("True", await Until(page, "document.getElementById('image').complete && document.getElementById('image').naturalWidth > 0", "True"));
        Assert.Equal("True", await Until(page, "(() => { const i = document.getElementById('blank').contentDocument.getElementById('image'); return i.complete && i.naturalWidth > 0; })()", "True"));
        Assert.Equal($"http://127.0.0.1:{listenerA.Port}/moved-to.html", await Until(page, "document.getElementById('moved').contentWindow.location.href", $"http://127.0.0.1:{listenerA.Port}/moved-to.html"));
        Assert.Equal("moved child", await Until(page, "document.getElementById('moved').contentDocument.querySelector('h1')?.textContent ?? ''", "moved child"));

        // The frame in the closed shadow root, which is in the top
        // document's process, read in an isolated world of its own.
        async Task<string> InFrame(string address, string expression)
        {
            using var tree = await topClient!.CallAsync("Page.getFrameTree", new { }, token);
            var frameId = tree.RootElement.GetProperty("result").GetProperty("frameTree").GetProperty("childFrames").EnumerateArray()
                .Select(child => child.GetProperty("frame"))
                .Where(frame => frame.GetProperty("url").GetString() == address)
                .Select(frame => frame.GetProperty("id").GetString())
                .FirstOrDefault();
            if (frameId is null)
            {
                return "";
            }
            using var world = await topClient.CallAsync("Page.createIsolatedWorld", new { frameId, worldName = "test" }, token);
            var context = world.RootElement.GetProperty("result").GetProperty("executionContextId").GetInt32();
            using var answer = await topClient.CallAsync("Runtime.evaluate", new { expression, contextId = context, returnByValue = true }, token);
            return answer.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").ToString();
        }
        var shadowedAddress = $"http://127.0.0.1:{listenerA.Port}/shadowed.html";
        var shadowedText = "";
        for (var attempt = 0; attempt < 100 && shadowedText != "shadowed child"; attempt++)
        {
            shadowedText = await InFrame(shadowedAddress, "document.querySelector('h1')?.textContent ?? ''");
            await Task.Delay(100, token);
        }
        Assert.Equal("shadowed child", shadowedText);
        await Task.Delay(1000, token);
        Assert.Equal("explicit blank", await page("document.getElementById('explicit').contentDocument.querySelector('h1')?.textContent ?? ''"));

        var cross = await Connect(await Target("iframe", $"http://localhost:{listenerB.Port}/cross.html"));
        Assert.Equal("cross child", await Until(cross, "document.querySelector('h1')?.textContent ?? ''", "cross child"));
        Assert.Equal("True", await Until(cross, "document.getElementById('image').complete && document.getElementById('image').naturalWidth > 0", "True"));
        var nested = await Connect(await Target("iframe", $"http://127.0.0.1:{listenerA.Port}/nested.html"));
        Assert.Equal("nested child", await Until(nested, "document.querySelector('h1')?.textContent ?? ''", "nested child"));

        var statuses = session.FrameStatuses.ToDictionary(status => status.Key);
        Assert.True(statuses["/0"].AskedFor);
        Assert.True(statuses["/0"].OutOfProcess);
        Assert.True(statuses["/0/0"].AskedFor);
        Assert.True(statuses["/0/0"].OutOfProcess);
        Assert.True(statuses["/1"].AskedFor);
        Assert.False(statuses["/1"].OutOfProcess);
        Assert.False(statuses["/2"].AskedFor);
        Assert.False(statuses["/4"].AskedFor);
        Assert.False(statuses["/5"].AskedFor);
        // The frames built in place asked for nothing under their own frame
        // IDs: their image was asked for under the top document's.
        Assert.Equal(["/0", "/0/0", "/1", "/6"], session.JoinedFrames.Values.Order(StringComparer.Ordinal));
        await Task.Delay(1000, token);
        // Not even a connection with nothing sent on it, as Chromium's
        // network prediction opens, which the profile turns off.
        Assert.Empty(listenerA.Requests.Concat(listenerB.Requests));
        Assert.Equal(0, listenerA.Connections);
        Assert.Equal(0, listenerB.Connections);
        Assert.Equal(0, session.RefusedByBrowser);
        Assert.True(statuses["/6"].AskedFor);
        Assert.False(statuses["/7"].AskedFor);
        Assert.Empty(session.Blocked);

        // A link followed in a frame, as only DevTools could in the
        // recreation, is refused by the page's frame-src before any
        // request, and the frame shows the browser's error page for it, as
        // the frame not built for its sandbox does for its own address.
        await InFrame(shadowedAddress, "document.getElementById('away').click(), 'clicked'");
        await Task.Delay(2000, token);
        using (var after = await topClient!.CallAsync("Page.getFrameTree", new { }, token))
        {
            var children = after.RootElement.GetProperty("result").GetProperty("frameTree").GetProperty("childFrames").EnumerateArray()
                .Select(child => child.GetProperty("frame"))
                .ToDictionary(frame => frame.GetProperty("name").GetString()!);
            Assert.Equal($"http://127.0.0.1:{listenerA.Port}/elsewhere.html", children["shadowed"].GetProperty("unreachableUrl").GetString());
            Assert.Equal($"http://127.0.0.1:{listenerA.Port}/sandboxed.html", children["sandboxed"].GetProperty("unreachableUrl").GetString());
            Assert.Equal("about:blank", children["explicit"].GetProperty("url").GetString());
        }
        Assert.Empty(session.Blocked);
        Assert.Equal(0, listenerA.Connections);
    }
}
