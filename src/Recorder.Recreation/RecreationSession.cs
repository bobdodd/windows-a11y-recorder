namespace Recorder.Recreation;

// One open recreation: the server that holds it, the browser that shows it,
// and the DevTools protocol control that holds the browser to it. Disposing
// it closes the browser, stops the server, and removes the profile.
public sealed class RecreationSession : IAsyncDisposable
{
    private readonly RecreationServer _server;
    private readonly RecreationBrowser _browser;
    private readonly RecreationControl _control;

    private RecreationSession(RecreationServer server, RecreationBrowser browser, RecreationControl control)
    {
        _server = server;
        _browser = browser;
        _control = control;
    }

    // The address the recreation's tab shows: the recorded document's when
    // the page is served at it, or the loopback page.
    public string PageAddress => _server.RecreationAddress;

    public int RefusedRequests => _control.RefusedRequests;

    // Slice 5b: the requests the browser's own session refused, the
    // recorded frame joined to each frame, by frame ID, and what the
    // recreation did with each recorded frame.
    public int RefusedByBrowser => _control.RefusedByBrowser;

    public IReadOnlyDictionary<string, string> JoinedFrames => _control.JoinedFrames;

    public IReadOnlyList<RecreationFrameStatus> FrameStatuses => _server.FrameStatuses;

    public bool BrowserHasExited => _browser.HasExited;

    public IReadOnlyList<BlockedNavigation> Blocked => _server.Blocked;

    public Uri DevToolsAddress { get; private init; } = null!;

    // The directory is a new folder of its own for this recreation.
    public static async Task<RecreationSession> OpenAsync(
        string executablePath,
        string directory,
        RecreationContent content,
        CancellationToken cancellationToken,
        IEnumerable<string>? extraArguments = null,
        IEnumerable<RecreationTiming>? earlierTimings = null)
    {
        RecreationServer server;
        try
        {
            server = await RecreationServer.StartAsync(content, cancellationToken);
        }
        catch
        {
            // The server disposes the content's resources once it holds them.
            content.Resources?.Dispose();
            RecreationServer.DisposeFrames(content.Frames);
            throw;
        }
        foreach (var timing in earlierTimings ?? [])
        {
            server.AddTiming(timing.Step, TimeSpan.FromMilliseconds(timing.Milliseconds));
        }
        RecreationBrowser? browser = null;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            browser = RecreationBrowser.Open(executablePath, directory, server, extraArguments, content.BrowserTheme);
            var address = await browser.DevToolsAddressAsync(cancellationToken);
            server.AddTiming("Starting the recreation browser, to its DevTools port", clock.Elapsed);
            clock.Restart();
            var control = await RecreationControl.StartAsync(
                address,
                server.RecreationAddress,
                content.Viewport,
                server.AddBlocked,
                cancellationToken,
                server.ServedAtRecordedAddress ? server : null,
                server.BaseAddress,
                fit => server.WindowFit = fit);
            server.AddTiming("Attaching to the tab and asking it to load the page", clock.Elapsed);
            return new RecreationSession(server, browser, control) { DevToolsAddress = address };
        }
        catch
        {
            if (browser is not null)
            {
                await browser.DisposeAsync();
            }
            await server.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _control.DisposeAsync();
        await _browser.DisposeAsync();
        await _server.DisposeAsync();
    }
}
