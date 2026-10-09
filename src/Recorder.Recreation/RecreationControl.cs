using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Recorder.Recreation;

// A navigation the recreation browser refused: the address it would have
// loaded, when, and whether it was in the recreation's own tab.
public sealed record BlockedNavigation(string Url, DateTimeOffset Time, bool InRecreationTab);

// Holds the recreation browser to the recreation over the DevTools protocol.
// Every tab is attached before it runs, and every document request of a tab
// is paused: a request for the recreation's own address continues, and any
// other, such as a followed link, is refused, recorded, and shown in the
// evidence panel; a tab opened by a refused request is closed. Nothing is
// added to the page. The recreation's tab is also given the recorded
// viewport, and focus emulation, so the recorded focus holds while DevTools
// has the keyboard, and its window is sized so that its page area is that
// viewport. See docs/architecture/page-recreation.md, "Leaving the
// recreation".
//
// Slice 5b: every frame target of a tab, and of a frame, is attached before
// it runs too, and its requests are paused in its own session, since an out
// of process frame's own requests are paused nowhere else. The browser's
// own session pauses every request no frame session answered first, and
// refuses it unless it is DevTools', an extension's, or the recorder's own
// loopback address's. A frame's requests are joined to the recorded frame
// whose owner has the path of the frame's owner element, read in an
// isolated world, and answered from that frame's document. See "Build plan
// for 5b".
public sealed class RecreationControl : IAsyncDisposable
{
    private readonly DevToolsConnection _connection;
    private readonly string _allowed;
    private readonly string? _loopback;
    private readonly RecreationViewport? _viewport;
    private readonly Action<BlockedNavigation> _blocked;
    private readonly IRecreationAnswers? _answers;

    // Slice 5c: the binding through which each document's builder reports
    // its build; the builder names it too.
    public const string BuildBinding = "__a11yRecorderBuilt";
    private int _refused;
    private int _refusedByBrowser;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<string> _firstTab = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // The target ID of each tab's session, which is its main frame's ID.
    private readonly ConcurrentDictionary<string, string> _targets = new(StringComparer.Ordinal);
    // The target ID of each frame target's session, which is its frame's ID.
    private readonly ConcurrentDictionary<string, string> _frameTargets = new(StringComparer.Ordinal);
    // The recorded frame's key joined to each frame, by frame ID; empty for
    // a frame joined to none.
    private readonly ConcurrentDictionary<string, string> _frameKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, bool> _handling = new();
    private Task? _events;
    private string? _recreationSession;

    private RecreationControl(
        DevToolsConnection connection,
        string allowed,
        string? loopback,
        RecreationViewport? viewport,
        Action<BlockedNavigation> blocked,
        IRecreationAnswers? answers)
    {
        _connection = connection;
        _allowed = allowed;
        _loopback = loopback;
        _viewport = viewport;
        _blocked = blocked;
        _answers = answers;
    }

    // How many requests of the tabs and their frames, other than refused
    // navigations, were refused because the recorder had no answer for them
    // (slice 4a), including those the browser's own session refused.
    public int RefusedRequests => Volatile.Read(ref _refused);

    // How many requests no frame session paused, which the browser's own
    // session refused (slice 5b).
    public int RefusedByBrowser => Volatile.Read(ref _refusedByBrowser);

    // The recorded frame's key joined to each frame of the recreation, by
    // frame ID, for frames joined to one (slice 5b).
    public IReadOnlyDictionary<string, string> JoinedFrames =>
        _frameKeys.Where(item => item.Value.Length > 0).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    // Connects to the browser, attaches to its tab, and opens the page in it.
    // With an answer, the page is served at its recorded address: every
    // request of every tab is paused, and is answered by the recorder or
    // refused, so none reaches the network (slice 4a). Without one, only
    // document requests are paused, and the loopback page's continue.
    public static async Task<RecreationControl> StartAsync(
        Uri browserAddress,
        string pageAddress,
        RecreationViewport? viewport,
        Action<BlockedNavigation> blocked,
        CancellationToken cancellationToken,
        IRecreationAnswers? answers = null,
        string? loopbackAddress = null)
    {
        var connection = await DevToolsConnection.ConnectAsync(browserAddress, cancellationToken);
        var control = new RecreationControl(connection, pageAddress, loopbackAddress, viewport, blocked, answers);
        try
        {
            control._events = Task.Run(control.HandleEventsAsync);
            // Slice 5b: before any tab is attached, the browser's own session
            // pauses every request that no frame session answers first.
            await connection.SendAsync("Fetch.enable", new
            {
                patterns = new object[] { new { urlPattern = "*", requestStage = "Request" } }
            }, null, cancellationToken);
            await connection.SendAsync("Target.setAutoAttach", new
            {
                autoAttach = true,
                waitForDebuggerOnStart = true,
                flatten = true,
                filter = new object[]
                {
                    new { type = "page", exclude = false },
                    new { exclude = true }
                }
            }, null, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var session = await control._firstTab.Task.WaitAsync(timeout.Token);
            if (viewport is { Width: > 0, Height: > 0 })
            {
                await control.FitWindowAsync(session, viewport, cancellationToken);
                await connection.SendAsync("Emulation.setDeviceMetricsOverride", new
                {
                    width = (int)Math.Round(viewport.EmulatedWidth),
                    height = (int)Math.Round(viewport.EmulatedHeight),
                    deviceScaleFactor = viewport.EmulatedDeviceScaleFactor,
                    mobile = false
                }, session, cancellationToken);
            }
            await connection.SendAsync("Page.navigate", new { url = pageAddress }, session, cancellationToken);
            return control;
        }
        catch
        {
            await control.DisposeAsync();
            throw;
        }
    }

    // Sizes the recreation's window so that its page area is the recorded
    // viewport, so the whole recorded page and its scroll bars show. The
    // window's frame, its size less the page area, is read from the blank
    // tab before the viewport is emulated, in CSS pixels, which are the
    // window's own units at the default zoom. The viewport is still
    // emulated after, so a window the screen cannot hold keeps the recorded
    // layout.
    private async Task FitWindowAsync(string session, RecreationViewport viewport, CancellationToken cancellationToken)
    {
        var measured = await _connection.SendAsync("Runtime.evaluate", new
        {
            expression = "JSON.stringify([outerWidth - innerWidth, outerHeight - innerHeight])",
            returnByValue = true
        }, session, cancellationToken);
        using var frame = JsonDocument.Parse(measured.GetProperty("result").GetProperty("value").GetString()!);
        var target = await _connection.SendAsync("Target.getTargetInfo", null, session, cancellationToken);
        var window = await _connection.SendAsync("Browser.getWindowForTarget", new
        {
            targetId = target.GetProperty("targetInfo").GetProperty("targetId").GetString()
        }, null, cancellationToken);
        var size = WindowSize(viewport, frame.RootElement[0].GetDouble(), frame.RootElement[1].GetDouble());
        await _connection.SendAsync("Browser.setWindowBounds", new
        {
            windowId = window.GetProperty("windowId").GetInt32(),
            bounds = new { windowState = "normal" }
        }, null, cancellationToken);
        await _connection.SendAsync("Browser.setWindowBounds", new
        {
            windowId = window.GetProperty("windowId").GetInt32(),
            bounds = new { width = size.Width, height = size.Height }
        }, null, cancellationToken);
    }

    // The window size whose page area is the recorded viewport, given the
    // window's frame, in whole pixels, rounded up.
    public static (int Width, int Height) WindowSize(RecreationViewport viewport, double frameWidth, double frameHeight) =>
        ((int)Math.Ceiling(viewport.EmulatedWidth + Math.Max(0, frameWidth)),
         (int)Math.Ceiling(viewport.EmulatedHeight + Math.Max(0, frameHeight)));

    // True for a request the browser's own session lets continue: one of
    // DevTools, of an extension, such as the evidence panel, of a browser
    // page, or for the recorder's loopback address (slice 5b).
    public static bool ContinuesAtBrowser(string url, string? loopbackAddress) =>
        url.StartsWith("devtools://", StringComparison.Ordinal) ||
        url.StartsWith("chrome-extension://", StringComparison.Ordinal) ||
        url.StartsWith("chrome://", StringComparison.Ordinal) ||
        url.StartsWith("chrome-untrusted://", StringComparison.Ordinal) ||
        (loopbackAddress is not null && url.StartsWith(loopbackAddress, StringComparison.Ordinal));

    // True for an address the recreation may load: its own, or DevTools.
    public static bool IsAllowed(string url, string pageAddress) =>
        url.StartsWith(pageAddress, StringComparison.Ordinal) ||
        url.StartsWith("devtools://", StringComparison.Ordinal) ||
        url == "about:blank";

    private async Task HandleEventsAsync()
    {
        try
        {
            await foreach (var item in _connection.Events.ReadAllAsync(_stop.Token))
            {
                try
                {
                    switch (item.Method)
                    {
                        case "Target.attachedToTarget":
                            Attached(item.Parameters);
                            break;
                        case "Target.detachedFromTarget":
                            if (item.Parameters.TryGetProperty("sessionId", out var gone) && gone.GetString() is { } id)
                            {
                                _targets.TryRemove(id, out _);
                                _frameTargets.TryRemove(id, out _);
                            }
                            break;
                        // Each paused request is handled on its own, since
                        // joining a frame's request to its owner takes
                        // several commands, whose answers this loop reads.
                        case "Fetch.requestPaused" when item.SessionId is { } session:
                            Handle(PausedAsync(session, item.Parameters));
                            break;
                        case "Fetch.requestPaused":
                            Handle(PausedAtBrowserAsync(item.Parameters));
                            break;
                        case "Runtime.bindingCalled" when _answers is not null &&
                            item.Parameters.TryGetProperty("name", out var name) && name.GetString() == BuildBinding &&
                            item.Parameters.TryGetProperty("payload", out var payload) && payload.GetString() is { } report:
                            _answers.DocumentBuilt(report);
                            break;
                    }
                }
                catch (InvalidOperationException)
                {
                    // A target closed while it was being handled.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Handle(Task task)
    {
        _handling[task] = true;
        _ = task.ContinueWith(done =>
        {
            _ = done.Exception;
            _handling.TryRemove(done, out _);
        }, TaskScheduler.Default);
    }

    private async Task PausedAtBrowserAsync(JsonElement parameters)
    {
        var requestId = parameters.GetProperty("requestId").GetString()!;
        var url = parameters.GetProperty("request").GetProperty("url").GetString() ?? "";
        var token = _stop.Token;
        if (ContinuesAtBrowser(url, _loopback))
        {
            await _connection.SendAsync("Fetch.continueRequest", new { requestId }, null, token);
            return;
        }
        Interlocked.Increment(ref _refused);
        Interlocked.Increment(ref _refusedByBrowser);
        await _connection.SendAsync("Fetch.failRequest", new { requestId, errorReason = "BlockedByClient" }, null, token);
    }

    private void Attached(JsonElement parameters)
    {
        var session = parameters.GetProperty("sessionId").GetString()!;
        var info = parameters.GetProperty("targetInfo");
        var type = info.GetProperty("type").GetString();
        var url = info.GetProperty("url").GetString() ?? "";
        var targetId = info.GetProperty("targetId").GetString()!;
        var token = _stop.Token;
        // A new tab waits for the debugger before it runs. Its commands are
        // sent in order, and it is then released; the protocol applies a
        // session's commands in the order sent, so interception is on before
        // the tab's first request. Their answers are not awaited here: the
        // release is answered only once the tab's first request is paused
        // and handled, which this loop does.
        var commands = new List<Task>();
        var frame = type == "iframe";
        if ((type == "page" && !url.StartsWith("devtools://", StringComparison.Ordinal)) || frame)
        {
            if (frame)
            {
                _frameTargets[session] = targetId;
                // A frame target's ID is its frame's: one joined when its
                // document was asked for is in a process of its own.
                if (_answers is not null && _frameKeys.TryGetValue(targetId, out var joined) && joined.Length > 0)
                {
                    _answers.FrameOutOfProcess(joined);
                }
            }
            else
            {
                _targets[session] = targetId;
            }
            commands.Add(_connection.SendAsync("Fetch.enable", new
            {
                patterns = _answers is null
                    ? new object[] { new { urlPattern = "*", resourceType = "Document", requestStage = "Request" } }
                    : new object[] { new { urlPattern = "*", requestStage = "Request" } }
            }, session, token));
            // Slice 5b: the frame targets of this tab or frame are attached
            // before they run, as tabs are.
            commands.Add(_connection.SendAsync("Target.setAutoAttach", new
            {
                autoAttach = true,
                waitForDebuggerOnStart = true,
                flatten = true,
                filter = new object[]
                {
                    new { type = "iframe", exclude = false },
                    new { exclude = true }
                }
            }, session, token));
            // Slice 5c: each document's builder reports its build through
            // this binding, which is added to every context of the target,
            // and, while Runtime is enabled in this session, to each made
            // later (v8-runtime-agent-impl.cc, addBinding and addBindings).
            if (_answers is not null)
            {
                commands.Add(_connection.SendAsync("Runtime.addBinding", new { name = BuildBinding }, session, token));
                commands.Add(_connection.SendAsync("Runtime.enable", null, session, token));
            }
            if (!frame && _recreationSession is null)
            {
                _recreationSession = session;
                commands.Add(_connection.SendAsync("Page.enable", null, session, token));
                commands.Add(_connection.SendAsync("Emulation.setFocusEmulationEnabled", new { enabled = true }, session, token));
            }
        }
        if (parameters.TryGetProperty("waitingForDebugger", out var waiting) && waiting.GetBoolean())
        {
            commands.Add(_connection.SendAsync("Runtime.runIfWaitingForDebugger", null, session, token));
        }
        var sent = Task.WhenAll(commands);
        if (session == _recreationSession)
        {
            _ = sent.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    _firstTab.TrySetResult(session);
                }
                else
                {
                    _firstTab.TrySetException(task.Exception?.InnerException ?? new OperationCanceledException());
                }
            }, TaskScheduler.Default);
        }
        else
        {
            // A tab that closes while it is being set up has nothing to hold.
            _ = sent.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private async Task PausedAsync(string session, JsonElement parameters)
    {
        var requestId = parameters.GetProperty("requestId").GetString()!;
        var url = parameters.GetProperty("request").GetProperty("url").GetString() ?? "";
        var token = _stop.Token;
        if (_answers is not null)
        {
            var requested = parameters.TryGetProperty("resourceType", out var requestedType) ? requestedType.GetString() : null;
            var frameId = parameters.TryGetProperty("frameId", out var frame) ? frame.GetString() : null;
            var key = await FrameKeyAsync(session, frameId, 0, token);
            if (key is not null)
            {
                var built = true;
                if (requested == "Document" && key.Length > 0)
                {
                    built = _answers.FrameAskedFor(key);
                }
                // An answer may read bytes from the recording file, so it is
                // not made on the thread that reads the DevTools connection.
                if (await Task.Run(() => _answers.Answer(url, requested, key), token) is { } answer)
                {
                    await _connection.SendAsync("Fetch.fulfillRequest", new
                    {
                        requestId,
                        responseCode = answer.Status,
                        responseHeaders = answer.Headers.Select(header => new { name = header.Key, value = header.Value }).ToArray(),
                        body = Convert.ToBase64String(answer.Body)
                    }, session, token);
                    return;
                }
                // A joined frame's navigation the recorder does not answer,
                // such as a followed link, is refused as the tab's own is,
                // so the frame keeps its document.
                if (requested == "Document" && key.Length > 0)
                {
                    await _connection.SendAsync("Fetch.failRequest", new { requestId, errorReason = "Aborted" }, session, token);
                    // A frame not built keeps its initial empty document,
                    // and is listed in the evidence panel's frames, not as
                    // a blocked navigation.
                    if (built)
                    {
                        _blocked(new BlockedNavigation(url, DateTimeOffset.Now, true));
                    }
                    return;
                }
            }
            // A request that is not a tab's own navigation, such as an
            // image, a style sheet, or a frame's document, is refused
            // without a page of its own, and is listed only in DevTools'
            // Network panel. The main frame's ID is its tab's target ID.
            var mainFrame = _targets.TryGetValue(session, out var tabTarget) && frameId == tabTarget;
            if (requested != "Document" || !mainFrame)
            {
                Interlocked.Increment(ref _refused);
                await _connection.SendAsync("Fetch.failRequest", new { requestId, errorReason = "BlockedByClient" }, session, token);
                return;
            }
        }
        else if (IsAllowed(url, _allowed))
        {
            await _connection.SendAsync("Fetch.continueRequest", new { requestId }, session, token);
            return;
        }
        // Aborted, as a navigation the user stopped, so no error page
        // replaces the recreation.
        await _connection.SendAsync("Fetch.failRequest", new { requestId, errorReason = "Aborted" }, session, token);
        var inRecreation = session == _recreationSession;
        _blocked(new BlockedNavigation(url, DateTimeOffset.Now, inRecreation));
        if (!inRecreation && _targets.TryGetValue(session, out var targetId))
        {
            await _connection.SendAsync("Target.closeTarget", new { targetId }, null, token);
        }
    }

    // The key of the recorded frame joined to a frame (slice 5b): empty for
    // a tab's main frame; for another frame, the key of the frame whose
    // owner has the path of the frame's owner element among the frames of
    // the recorded frame joined to the frame's parent. Null when none is
    // joined. A frame's join is kept, since a frame keeps its ID across
    // navigations; a frame whose owner is in another process, such as the
    // main frame of a frame target, is joined when its document is asked
    // for in its parent's session.
    private async Task<string?> FrameKeyAsync(string session, string? frameId, int depth, CancellationToken token)
    {
        if (frameId is null || _answers is null || depth > 16)
        {
            return null;
        }
        if (_targets.TryGetValue(session, out var tab) && tab == frameId)
        {
            return "";
        }
        if (_frameKeys.TryGetValue(frameId, out var known))
        {
            return known.Length > 0 ? known : null;
        }
        string? key = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var owner = await _connection.SendAsync("DOM.getFrameOwner", new { frameId }, session, timeout.Token);
            var backendNodeId = owner.GetProperty("backendNodeId").GetInt64();
            var tree = await _connection.SendAsync("Page.getFrameTree", null, session, timeout.Token);
            if (ParentOf(tree.GetProperty("frameTree"), frameId, null) is { } parentId &&
                await FrameKeyAsync(session, parentId, depth + 1, token) is { } parentKey &&
                await OwnerPathAsync(session, parentId, backendNodeId, timeout.Token) is { } path)
            {
                key = _answers.ChildKey(parentKey, path);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or
            OperationCanceledException or JsonException or ArgumentException)
        {
            if (token.IsCancellationRequested)
            {
                throw;
            }
        }
        return (_frameKeys.GetOrAdd(frameId, key ?? "")) is { Length: > 0 } joined ? joined : null;
    }

    // The ID of a frame's parent in a session's frame tree.
    private static string? ParentOf(JsonElement node, string frameId, string? parentId)
    {
        if (node.GetProperty("frame").GetProperty("id").GetString() == frameId)
        {
            return parentId;
        }
        if (node.TryGetProperty("childFrames", out var children))
        {
            var id = node.GetProperty("frame").GetProperty("id").GetString();
            foreach (var child in children.EnumerateArray())
            {
                if (ParentOf(child, frameId, id) is { } found)
                {
                    return found;
                }
            }
        }
        return null;
    }

    // The path of a frame's owner element, read in an isolated world of its
    // parent frame, so that nothing of the page's own can change what the
    // path is read from: one positional XPath expression for each tree scope,
    // as RecordedPaths writes them. Null when it has none.
    private async Task<NodePath?> OwnerPathAsync(string session, string parentId, long backendNodeId, CancellationToken token)
    {
        var world = await _connection.SendAsync("Page.createIsolatedWorld", new
        {
            frameId = parentId,
            worldName = "Windows A11y Recorder frame owners",
            grantUniveralAccess = false
        }, session, token);
        var resolved = await _connection.SendAsync("DOM.resolveNode", new
        {
            backendNodeId,
            executionContextId = world.GetProperty("executionContextId").GetInt32()
        }, session, token);
        var objectId = resolved.GetProperty("object").GetProperty("objectId").GetString();
        try
        {
            var result = await _connection.SendAsync("Runtime.callFunctionOn", new
            {
                objectId,
                functionDeclaration = OwnerPathFunction,
                returnByValue = true
            }, session, token);
            var value = result.GetProperty("result");
            if (!value.TryGetProperty("value", out var path) || path.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            return NodePath.Create(
                [.. path.GetProperty("scopes").EnumerateArray().Select(item => item.GetString()!)],
                [.. path.GetProperty("modes").EnumerateArray().Select(item => item.GetString()!)]);
        }
        finally
        {
            await _connection.SendAsync("Runtime.releaseObject", new { objectId }, session, token);
        }
    }

    // Reads a node's path as RecordedPaths.Of writes it from the recorded
    // tree: an element in the HTML namespace of an HTML document, named in
    // capitals, by its lower case name, any other by a local name test, text
    // by text(), a comment by comment(), with positions counted among the
    // siblings that match the same test, and a new scope at each shadow
    // root, whose mode is recorded.
    public const string OwnerPathFunction = """
        function () {
          const test = (node) => {
            if (node.nodeType === 1) {
              const name = node.nodeName;
              return name !== name.toLowerCase() && name === name.toUpperCase()
                ? name.toLowerCase()
                : `*[local-name()='${name}']`;
            }
            if (node.nodeType === 3) {
              return "text()";
            }
            if (node.nodeType === 8) {
              return "comment()";
            }
            return null;
          };
          const join = (steps) => steps.length === 0 ? "" : "/" + steps.slice().reverse().join("/");
          const scopes = [];
          const modes = [];
          let steps = [];
          let node = this;
          for (;;) {
            if (node.nodeType === 9) {
              scopes.push(join(steps));
              break;
            }
            if (node.nodeType === 11 && node.host) {
              if (node.mode !== "open" && node.mode !== "closed") {
                return null;
              }
              scopes.push(join(steps));
              modes.push(node.mode);
              steps = [];
              node = node.host;
              continue;
            }
            const parent = node.parentNode;
            const own = test(node);
            if (!parent || own === null) {
              return null;
            }
            let position = 0;
            for (const sibling of parent.childNodes) {
              if (test(sibling) === own) {
                position++;
              }
              if (sibling === node) {
                break;
              }
            }
            steps.push(`${own}[${position}]`);
            node = parent;
          }
          if (scopes.some((scope) => scope.length === 0)) {
            return null;
          }
          scopes.reverse();
          modes.reverse();
          return { scopes, modes };
        }
        """;

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _connection.DisposeAsync();
        try
        {
            await Task.WhenAll(_handling.Keys);
        }
        catch (Exception)
        {
            // A request being handled when the browser closed has nothing to answer.
        }
        if (_events is not null)
        {
            try
            {
                await _events;
            }
            catch (Exception exception) when (exception is OperationCanceledException or ChannelClosedException)
            {
            }
        }
        _stop.Dispose();
    }
}

// The recorded viewport the recreation is shown at, in CSS pixels, from the
// page's latest layout checkpoint. From accessibility preferences stage 3
// the instrumented renderer zooms the page by the recorded browser zoom
// factor at the frame. Chromium's devicePixelRatio includes the browser
// zoom (LocalFrame::DevicePixelRatio), so the checkpoint's size and ratio
// are those of the zoom at the checkpoint: the window's size in
// device-independent pixels is the CSS size times that zoom, and the
// screen's scale factor the ratio over it. The viewport is emulated at those,
// and the renderer's zoom at the frame then gives the page the CSS size and
// devicePixelRatio it had at the frame, also when the zoom changed after the
// checkpoint (found in the owner's check of 2026-10-09).
public sealed record RecreationViewport(double Width, double Height, double DevicePixelRatio, double LayoutZoomFactor)
{
    /// <summary>The recorded browser zoom factor the page is shown at; 1 when none is applied.</summary>
    public double BrowserZoomFactor { get; init; } = 1.0;

    /// <summary>The recorded browser zoom factor at the checkpoint; when none is given, that of the frame.</summary>
    public double? CheckpointZoomFactor { get; init; }

    private double AtCheckpoint => CheckpointZoomFactor ?? BrowserZoomFactor;

    /// <summary>The emulated width, in device-independent pixels.</summary>
    public double EmulatedWidth => Width * AtCheckpoint;

    /// <summary>The emulated height, in device-independent pixels.</summary>
    public double EmulatedHeight => Height * AtCheckpoint;

    /// <summary>The emulated device scale factor.</summary>
    public double EmulatedDeviceScaleFactor => DevicePixelRatio / AtCheckpoint;

    /// <summary>The page's CSS width once the renderer zooms it.</summary>
    public double ShownWidth => EmulatedWidth / BrowserZoomFactor;

    /// <summary>The page's CSS height once the renderer zooms it.</summary>
    public double ShownHeight => EmulatedHeight / BrowserZoomFactor;

    /// <summary>The page's devicePixelRatio once the renderer zooms it.</summary>
    public double ShownDevicePixelRatio => EmulatedDeviceScaleFactor * BrowserZoomFactor;
}
