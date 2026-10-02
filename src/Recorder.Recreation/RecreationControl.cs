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
public sealed class RecreationControl : IAsyncDisposable
{
    private readonly DevToolsConnection _connection;
    private readonly string _allowed;
    private readonly RecreationViewport? _viewport;
    private readonly Action<BlockedNavigation> _blocked;
    private readonly Func<string, string?, RecreationAnswer?>? _answer;
    private int _refused;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<string> _firstTab = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, string> _targets = new(StringComparer.Ordinal);
    private Task? _events;
    private string? _recreationSession;

    private RecreationControl(
        DevToolsConnection connection,
        string allowed,
        RecreationViewport? viewport,
        Action<BlockedNavigation> blocked,
        Func<string, string?, RecreationAnswer?>? answer)
    {
        _connection = connection;
        _allowed = allowed;
        _viewport = viewport;
        _blocked = blocked;
        _answer = answer;
    }

    // How many requests of the tabs, other than refused navigations, were
    // refused because the recorder had no answer for them (slice 4a).
    public int RefusedRequests => Volatile.Read(ref _refused);

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
        Func<string, string?, RecreationAnswer?>? answer = null)
    {
        var connection = await DevToolsConnection.ConnectAsync(browserAddress, cancellationToken);
        var control = new RecreationControl(connection, pageAddress, viewport, blocked, answer);
        try
        {
            control._events = Task.Run(control.HandleEventsAsync);
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
                    width = (int)Math.Round(viewport.Width),
                    height = (int)Math.Round(viewport.Height),
                    deviceScaleFactor = viewport.DevicePixelRatio,
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
        ((int)Math.Ceiling(viewport.Width + Math.Max(0, frameWidth)),
         (int)Math.Ceiling(viewport.Height + Math.Max(0, frameHeight)));

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
                                _targets.Remove(id);
                            }
                            break;
                        case "Fetch.requestPaused" when item.SessionId is { } session:
                            await PausedAsync(session, item.Parameters);
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
        if (type == "page" && !url.StartsWith("devtools://", StringComparison.Ordinal))
        {
            _targets[session] = targetId;
            commands.Add(_connection.SendAsync("Fetch.enable", new
            {
                patterns = _answer is null
                    ? new object[] { new { urlPattern = "*", resourceType = "Document", requestStage = "Request" } }
                    : new object[] { new { urlPattern = "*", requestStage = "Request" } }
            }, session, token));
            if (_recreationSession is null)
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
        if (_answer is not null)
        {
            var requested = parameters.TryGetProperty("resourceType", out var requestedType) ? requestedType.GetString() : null;
            // An answer may read bytes from the recording file, so it is not
            // made on the thread that reads the DevTools connection.
            if (await Task.Run(() => _answer(url, requested), token) is { } answer)
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
            // A request that is not a tab's own navigation, such as an
            // image, a style sheet, or an iframe's document, is refused
            // without a page of its own, and is listed only in DevTools'
            // Network panel. The main frame's ID is its tab's target ID.
            var resourceType = parameters.TryGetProperty("resourceType", out var type) ? type.GetString() : null;
            var frameId = parameters.TryGetProperty("frameId", out var frame) ? frame.GetString() : null;
            var mainFrame = _targets.TryGetValue(session, out var tabTarget) && frameId == tabTarget;
            if (resourceType != "Document" || !mainFrame)
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

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _connection.DisposeAsync();
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

// The recorded viewport the recreation is shown at, in CSS pixels.
public sealed record RecreationViewport(double Width, double Height, double DevicePixelRatio, double LayoutZoomFactor);
