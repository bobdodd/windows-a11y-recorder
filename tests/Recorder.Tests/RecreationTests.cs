using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Recorder.Recreation;

namespace Recorder.Tests;

public sealed class RecreationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recreation-tests-" + Guid.NewGuid().ToString("N"));

    // Chromium's child processes can hold files in the profile for a moment
    // after the browser process has been killed and has exited.
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
    public void APathThroughShadowRootsIsShownWithTheModeOfEachRoot()
    {
        var path = NodePath.Create(
            ["/html[1]/body[1]/my-card[1]", "/div[1]/x-inner[1]", "/button[2]"],
            ["open", "closed"]);
        Assert.Equal(
            "/html[1]/body[1]/my-card[1]/#shadow-root(open)/div[1]/x-inner[1]/#shadow-root(closed)/button[2]",
            path.Display);
        Assert.Equal("/html[1]/body[1]", NodePath.Of("/html[1]/body[1]").Display);

        Assert.Throws<ArgumentException>(() => NodePath.Create([], []));
        Assert.Throws<ArgumentException>(() => NodePath.Create(["/html[1]", "/div[1]"], []));
        Assert.Throws<ArgumentException>(() => NodePath.Create(["/html[1]", "/div[1]"], ["hidden"]));
        Assert.Throws<ArgumentException>(() => NodePath.Create(["html[1]"], []));
    }

    [Fact]
    public void TheFixedContentIsAWholePageWithEvidenceStatedAsNotRecorded()
    {
        var content = FixedRecreation.Create();
        Assert.DoesNotContain("{{", content.Html, StringComparison.Ordinal);
        Assert.StartsWith("<!DOCTYPE html>", content.Html, StringComparison.Ordinal);
        Assert.Equal(60, content.Html.Split("class=\"filler\"").Length - 1);
        Assert.Contains("shadowrootmode=\"open\"", content.Html, StringComparison.Ordinal);
        // The page's own script and handler are there, so the system test can
        // see that neither runs.
        Assert.Contains(FixedRecreation.ScriptRanTitle, content.Html, StringComparison.Ordinal);
        Assert.Contains("onclick=", content.Html, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(content.Evidence, RecreationServer.EvidenceJson));
        var root = json.RootElement;
        Assert.Equal("fixed", root.GetProperty("recreation").GetProperty("source").GetString());
        Assert.Contains("None of it is evidence", root.GetProperty("recreation").GetProperty("notice").GetString(), StringComparison.Ordinal);
        Assert.Equal("not-checked", root.GetProperty("fidelity").GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("timers").GetArrayLength());
        Assert.Equal(1, root.GetProperty("animations").GetArrayLength());
        var elements = root.GetProperty("interactiveElements").EnumerateArray().ToArray();
        Assert.Equal(8, elements.Length);
        var open = elements.Single(item => item.GetProperty("name").GetString() == "Open").GetProperty("node");
        Assert.Equal(
            "/html[1]/body[1]/main[1]/section[2]/my-card[1]/#shadow-root(open)/div[1]/button[1]",
            open.GetProperty("display").GetString());
        Assert.Equal(2, open.GetProperty("scopes").GetArrayLength());
        Assert.Equal("Ada", root.GetProperty("interaction").GetProperty("formValues")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task TheServerAnswersOnlyAtItsTokenAndLoopbackAddress()
    {
        var token = TestContext.Current.CancellationToken;
        var content = FixedRecreation.Create();
        await using var server = await RecreationServer.StartAsync(content, token);
        Assert.StartsWith($"http://127.0.0.1:{server.Port}/", server.PageAddress, StringComparison.Ordinal);
        Assert.True(server.Token.Length >= 43);
        using var client = new HttpClient();

        using var page = await client.GetAsync(server.PageAddress, token);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Equal(RecreationServer.PageContentSecurityPolicy, Assert.Single(page.Headers.GetValues("Content-Security-Policy")));
        Assert.Contains("script-src 'none'", RecreationServer.PageContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Equal("no-store", Assert.Single(page.Headers.GetValues("Cache-Control")));
        Assert.Equal(content.Html, await page.Content.ReadAsStringAsync(token));

        using var evidence = await client.GetAsync(server.EvidenceAddress, token);
        Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
        Assert.Equal("application/json", evidence.Content.Headers.ContentType?.MediaType);
        using (var json = JsonDocument.Parse(await evidence.Content.ReadAsStringAsync(token)))
        {
            Assert.Equal("fixed", json.RootElement.GetProperty("recreation").GetProperty("source").GetString());
        }

        var other = RecreationServer.NewToken();
        foreach (var address in new[]
        {
            $"http://127.0.0.1:{server.Port}/",
            $"http://127.0.0.1:{server.Port}/{server.Token}",
            $"http://127.0.0.1:{server.Port}/{other}/",
            $"http://127.0.0.1:{server.Port}/{other}/evidence.json",
            server.BaseAddress + "other.html",
            server.BaseAddress + "../evidence.json"
        })
        {
            using var response = await client.GetAsync(address, token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // A name rebound to the loopback address is refused.
        using (var request = new HttpRequestMessage(HttpMethod.Get, server.PageAddress))
        {
            request.Headers.Host = $"localhost:{server.Port}";
            using var response = await client.SendAsync(request, token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using (var response = await client.PostAsync(server.PageAddress, new StringContent(""), token))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    [Fact]
    public void TheEvidencePanelIsWrittenWithTheAddressOfTheEvidence()
    {
        var extension = Path.Combine(_directory, RecreationBrowser.ExtensionFolder);
        RecreationBrowser.WriteExtension(extension, "http://127.0.0.1:5000/token/evidence.json");
        foreach (var name in RecreationBrowser.PanelFiles.Append("config.json"))
        {
            Assert.True(File.Exists(Path.Combine(extension, name)), name);
        }
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(extension, "manifest.json"))))
        {
            Assert.Equal(3, manifest.RootElement.GetProperty("manifest_version").GetInt32());
            Assert.Equal("devtools.html", manifest.RootElement.GetProperty("devtools_page").GetString());
            Assert.Equal("http://127.0.0.1/*", Assert.Single(manifest.RootElement.GetProperty("host_permissions").EnumerateArray()).GetString());
        }
        using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(extension, "config.json"))))
        {
            Assert.Equal("http://127.0.0.1:5000/token/evidence.json", config.RootElement.GetProperty("evidenceAddress").GetString());
        }
        // Recorded values are page content and are inserted as text only.
        var panel = File.ReadAllText(Path.Combine(extension, "panel.js"));
        Assert.DoesNotContain("innerHTML", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBrowserOpensThePageInItsOwnProfileWithDevToolsInItsOwnWindow()
    {
        var profile = Path.Combine(_directory, RecreationBrowser.ProfileFolder);
        var extension = Path.Combine(_directory, RecreationBrowser.ExtensionFolder);
        RecreationBrowser.WriteProfile(profile);
        using (var preferences = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "Default", "Preferences"))))
        {
            Assert.Equal(
                "\"undocked\"",
                preferences.RootElement.GetProperty("devtools").GetProperty("preferences").GetProperty("currentDockState").GetString());
        }

        var start = RecreationBrowser.CreateStartInfo("chrome.exe", profile, extension, "http://127.0.0.1:5000/token/");
        var arguments = start.ArgumentList.ToArray();
        Assert.Contains($"--user-data-dir={Path.GetFullPath(profile)}", arguments);
        Assert.Contains($"--load-extension={Path.GetFullPath(extension)}", arguments);
        Assert.Contains($"--disable-extensions-except={Path.GetFullPath(extension)}", arguments);
        Assert.Contains("--auto-open-devtools-for-tabs", arguments);
        Assert.Equal("http://127.0.0.1:5000/token/", arguments[^1]);
        // Without the bootstrap switch the instrumented browser records nothing.
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--a11y-recorder", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--remote-debugging", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARecreationIsNotOpenedWithoutTheBrowser()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await RecreationSession.OpenAsync(
                Path.Combine(_directory, "missing", "chrome.exe"),
                Path.Combine(_directory, "recreation"),
                FixedRecreation.Create(),
                TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(_directory, "recreation")));
    }

    // Set RECORDER_RECREATION_CHROMIUM to a Chromium executable, such as the
    // instrumented build, to open the fixed recreation in it without a window
    // and check that the page's script and event handler do not run and that
    // every path in the evidence selects its node, through the panel's own
    // path resolver.
    [Fact]
    public async Task TheFixedRecreationRunsNoPageScriptAndEveryPathSelectsItsNode()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_RECREATION_CHROMIUM") is not { } chromium)
        {
            return;
        }
        var token = TestContext.Current.CancellationToken;
        var content = FixedRecreation.Create();
        await using var server = await RecreationServer.StartAsync(content, token);
        var profile = Path.Combine(_directory, "headless-profile");
        using var process = Process.Start(new ProcessStartInfo(chromium)
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
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(page.GetProperty("webSocketDebuggerUrl").GetString()!), token);
            var client = new DevToolsClient(socket);
            await client.CallAsync("Page.enable", new { }, token);
            await client.CallAsync("Page.navigate", new { url = server.PageAddress }, token);
            await client.WaitForEventAsync("Page.loadEventFired", token);

            var panel = File.ReadAllText(Path.Combine(WriteExtensionForTest(server), "panel.js"));
            var findNode = panel[panel.IndexOf("function findNode", StringComparison.Ordinal)..panel.IndexOf("// Selects the node", StringComparison.Ordinal)];
            var evidence = content.Evidence;
            var paths = evidence.InteractiveElements.Select(item => item.Node)
                .Concat(evidence.Animations.Select(item => item.Target))
                .Concat(evidence.Interaction.FormValues.Select(item => item.Node))
                .Append(evidence.Interaction.Focus!)
                .ToArray();
            var expression = $$"""
                (() => {
                  {{findNode}}
                  document.querySelector("#form button").click();
                  return JSON.stringify({
                    title: document.title,
                    found: {{JsonSerializer.Serialize(paths.Select(path => path.Scopes))}}.map(scopes => {
                      const found = findNode(scopes);
                      return found.node ? found.node.localName : found.failure;
                    }),
                    missing: findNode(["/html[1]/body[1]/main[1]/section[9]"]).failure
                  });
                })()
                """;
            using var answer = await client.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, token);
            var value = answer.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString()!;
            using var result = JsonDocument.Parse(value);
            Assert.Equal("Fixed recreation", result.RootElement.GetProperty("title").GetString());
            Assert.Equal("not-found", result.RootElement.GetProperty("missing").GetString());
            var found = result.RootElement.GetProperty("found").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Equal(paths.Length, found.Length);
            for (var index = 0; index < paths.Length; index++)
            {
                var expected = paths[index].Scopes[^1].Split('/')[^1].Split('[')[0];
                Assert.True(expected == found[index], $"{paths[index].Display} selected {found[index]}");
            }
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private string WriteExtensionForTest(RecreationServer server)
    {
        var extension = Path.Combine(_directory, "extension-for-test");
        RecreationBrowser.WriteExtension(extension, server.EvidenceAddress);
        return extension;
    }

    private sealed class DevToolsClient(ClientWebSocket socket)
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
}
