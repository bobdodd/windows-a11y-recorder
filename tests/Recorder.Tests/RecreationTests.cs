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

        // Stage 3: the times of the steps of opening the recreation.
        server.AddTiming("Writing the page", TimeSpan.FromMilliseconds(12.34));
        Assert.Equal(
            """[{"step":"Writing the page","milliseconds":12.3}]""",
            await client.GetStringAsync(server.BaseAddress + "timings.json", token));

        // How the window holds the recorded frame, once it is sized.
        Assert.Equal("null", await client.GetStringAsync(server.BaseAddress + "window.json", token));
        server.WindowFit = new RecreationWindowFit(1864, 818, 1850, 800, true, 0.9779);
        using (var fit = JsonDocument.Parse(await client.GetStringAsync(server.BaseAddress + "window.json", token)))
        {
            Assert.True(fit.RootElement.GetProperty("emulated").GetBoolean());
            Assert.Equal(0.9779, fit.RootElement.GetProperty("scale").GetDouble());
            Assert.Equal(1864, fit.RootElement.GetProperty("emulatedWidth").GetInt32());
            Assert.Equal(818, fit.RootElement.GetProperty("emulatedHeight").GetInt32());
            Assert.Equal(1850, fit.RootElement.GetProperty("areaWidth").GetDouble());
            Assert.Equal(800, fit.RootElement.GetProperty("areaHeight").GetDouble());
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
    public void ThePanelComparesTheShownViewportWithThePagesOwn()
    {
        // The evidence names the viewport the page is meant to be shown at
        // as the panel reads it, and the panel reads the page's own size and
        // ratio once it has painted.
        var evidence = FixedRecreation.Create().Evidence with
        {
            ShownViewport = new RecreationShownViewport(1864, 818, 1, 1),
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(evidence, RecreationServer.EvidenceJson));
        var shown = json.RootElement.GetProperty("shownViewport");
        Assert.Equal(1864, shown.GetProperty("width").GetDouble());
        Assert.Equal(818, shown.GetProperty("height").GetDouble());
        Assert.Equal(1, shown.GetProperty("devicePixelRatio").GetDouble());
        Assert.Equal(1, shown.GetProperty("layoutZoomFactor").GetDouble());
        var extension = Path.Combine(_directory, RecreationBrowser.ExtensionFolder);
        RecreationBrowser.WriteExtension(extension, "http://127.0.0.1:5000/token/evidence.json");
        var panel = File.ReadAllText(Path.Combine(extension, "panel.js"));
        var check = panel[panel.IndexOf("function checkViewport", StringComparison.Ordinal)..panel.IndexOf("function watchTimings", StringComparison.Ordinal)];
        Assert.Contains("top.shownViewport", check, StringComparison.Ordinal);
        Assert.Contains("JSON.stringify([innerWidth, innerHeight, devicePixelRatio])", check, StringComparison.Ordinal);
        Assert.Contains("\"Viewport as shown\"", check, StringComparison.Ordinal);
        Assert.Contains("checkViewport();", panel, StringComparison.Ordinal);
        // The window's fit, and the scale the page is drawn at when its size
        // is emulated, are listed and announced.
        Assert.Contains("${recorderBase}window.json", check, StringComparison.Ordinal);
        Assert.Contains("fit.emulated", check, StringComparison.Ordinal);
        Assert.Contains("fit.emulationError", check, StringComparison.Ordinal);
        Assert.Contains("say(fitNote ?", check, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWindowFitEmulatesThePageSizeOnlyWhenTheScreenCannotHoldTheFrame()
    {
        // The owner's frame of 2026-10-09, 1864 by 818 screen pixels, on a
        // screen at a scale factor of 1.
        var viewport = new RecreationViewport(932, 409, 2, 2) { FrameLayoutZoomFactor = 1 };

        // The window holds it, or holds it but for less than a pixel.
        foreach (var (width, height) in new[] { (1864.0, 818.0), (1900.0, 900.0), (1863.5, 817.2) })
        {
            var fits = RecreationControl.Fit(viewport, 1, width, height);
            Assert.False(fits.Emulated);
            Assert.Equal(1, fits.Scale);
        }

        // The screen cannot hold it: the size is emulated at the frame's and
        // drawn at the largest scale the page area holds, rounded down.
        var shortOfHeight = RecreationControl.Fit(viewport, 1, 1864, 700);
        Assert.True(shortOfHeight.Emulated);
        Assert.Equal((1864, 818), (shortOfHeight.EmulatedWidth, shortOfHeight.EmulatedHeight));
        Assert.Equal(0.8557, shortOfHeight.Scale, 9);
        Assert.True(1864 * shortOfHeight.Scale <= 1864 && 818 * shortOfHeight.Scale <= 700);
        var shortOfBoth = RecreationControl.Fit(viewport, 1, 1850, 800);
        Assert.Equal(0.9779, shortOfBoth.Scale, 9);
        Assert.Equal(0.9924, RecreationControl.Fit(viewport, 1, 1850, 818).Scale, 9);

        // On a screen at 1.5, the frame needs fewer device-independent
        // pixels, and the emulated size is in them.
        var atOneAndAHalf = RecreationControl.Fit(viewport, 1.5, 1000, 545);
        Assert.True(atOneAndAHalf.Emulated);
        Assert.Equal((1243, 545), (atOneAndAHalf.EmulatedWidth, atOneAndAHalf.EmulatedHeight));
        Assert.Equal(0.8047, atOneAndAHalf.Scale, 9);

        // A page area of nothing still gives a scale DevTools takes.
        Assert.Equal(0.01, RecreationControl.Fit(viewport, 1, 0, 0).Scale, 9);
    }

    [Fact]
    public void TheBrowserOpensInItsOwnProfileWithDevToolsInItsOwnWindowAndItsProtocolPort()
    {
        var profile = Path.Combine(_directory, RecreationBrowser.ProfileFolder);
        var extension = Path.Combine(_directory, RecreationBrowser.ExtensionFolder);
        RecreationBrowser.WriteProfile(profile);
        using (var preferences = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "Default", "Preferences"))))
        {
            Assert.Equal(
                "\"undocked\"",
                preferences.RootElement.GetProperty("devtools").GetProperty("preferences").GetProperty("currentDockState").GetString());
            // Slice 5b: network prediction is off.
            Assert.Equal(2, preferences.RootElement.GetProperty("net").GetProperty("network_prediction_options").GetInt32());
        }

        var start = RecreationBrowser.CreateStartInfo("chrome.exe", profile, extension);
        var arguments = start.ArgumentList.ToArray();
        Assert.Contains($"--user-data-dir={Path.GetFullPath(profile)}", arguments);
        Assert.Contains($"--load-extension={Path.GetFullPath(extension)}", arguments);
        Assert.Contains($"--disable-extensions-except={Path.GetFullPath(extension)}", arguments);
        Assert.Contains("--auto-open-devtools-for-tabs", arguments);
        // The browser opens a blank tab; the recorder opens the page in it
        // once it holds the browser over the DevTools protocol, on a port
        // the browser chooses on the loopback interface.
        Assert.Equal("about:blank", arguments[^1]);
        Assert.Contains("--remote-debugging-port=0", arguments);
        // Stage 3: the renderers impose the recorded values.
        Assert.Contains("--a11y-recorder-recreation", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--remote-debugging-address", StringComparison.Ordinal));
        // Without the bootstrap switch the instrumented browser records nothing.
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--a11y-recorder-bootstrap", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRecreationBrowsersLogIsKeptBesideItsDirectory()
    {
        var recreations = Path.Combine(_directory, "recreations");
        var log = RecreationBrowser.LogPathFor(Path.Combine(recreations, "abc"));
        Assert.Equal(Path.Combine(Path.GetFullPath(recreations), RecreationBrowser.LogFolder, "abc.log"), log);
        Assert.True(Directory.Exists(Path.GetDirectoryName(log)));

        var arguments = RecreationBrowser.CreateStartInfo("chrome.exe", "profile", "extension", logPath: log).ArgumentList.ToArray();
        Assert.Contains("--enable-logging", arguments);
        Assert.Contains($"--log-file={log}", arguments);
        Assert.Equal("about:blank", arguments[^1]);
        Assert.DoesNotContain("--enable-logging", RecreationBrowser.CreateStartInfo("chrome.exe", "profile", "extension").ArgumentList);

        // Only the newest logs are kept.
        var logs = Path.GetDirectoryName(log)!;
        for (var index = 0; index < 25; index++)
        {
            var path = Path.Combine(logs, $"old{index}.log");
            File.WriteAllText(path, "log");
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index));
        }
        RecreationBrowser.LogPathFor(Path.Combine(recreations, "def"));
        var kept = Directory.GetFiles(logs, "*.log");
        Assert.Equal(19, kept.Length);
        Assert.Contains(Path.Combine(logs, "old24.log"), kept);
        Assert.DoesNotContain(Path.Combine(logs, "old0.log"), kept);
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
        await using var browser = await HeadlessChromium.StartAsync(chromium, Path.Combine(_directory, "headless-profile"), token);
        var client = browser.Client;
        await client.CallAsync("Page.enable", new { }, token);
        await client.CallAsync("Page.navigate", new { url = server.PageAddress }, token);
        await client.WaitForEventAsync("Page.loadEventFired", token);

        var panel = File.ReadAllText(Path.Combine(WriteExtensionForTest(server), "panel.js"));
        var findNode = panel[panel.IndexOf("function findNode", StringComparison.Ordinal)..panel.IndexOf("// Selects the node", StringComparison.Ordinal)];
        var evidence = content.Evidence;
        var paths = evidence.InteractiveElements.Select(item => item.Node)
            .Concat(evidence.Animations.Select(item => item.Target!))
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

    private string WriteExtensionForTest(RecreationServer server)
    {
        var extension = Path.Combine(_directory, "extension-for-test");
        RecreationBrowser.WriteExtension(extension, server.EvidenceAddress);
        return extension;
    }
}
