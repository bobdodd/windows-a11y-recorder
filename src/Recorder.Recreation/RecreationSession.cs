namespace Recorder.Recreation;

// One open recreation: the server that holds it and the browser that shows
// it. Disposing it closes the browser, stops the server, and removes the
// profile.
public sealed class RecreationSession : IAsyncDisposable
{
    private readonly RecreationServer _server;
    private readonly RecreationBrowser _browser;

    private RecreationSession(RecreationServer server, RecreationBrowser browser)
    {
        _server = server;
        _browser = browser;
    }

    public string PageAddress => _server.PageAddress;

    public bool BrowserHasExited => _browser.HasExited;

    // The directory is a new folder of its own for this recreation.
    public static async Task<RecreationSession> OpenAsync(
        string executablePath,
        string directory,
        RecreationContent content,
        CancellationToken cancellationToken)
    {
        var server = await RecreationServer.StartAsync(content, cancellationToken);
        try
        {
            return new RecreationSession(server, RecreationBrowser.Open(executablePath, directory, server));
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.DisposeAsync();
        await _server.DisposeAsync();
    }
}
