using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace Recorder.Recreation;

// An event of the DevTools protocol: its method, the session it came from, or
// null for the browser, and its parameters.
public sealed record DevToolsEvent(string Method, string? SessionId, JsonElement Parameters);

// A connection to a browser over the DevTools protocol, with flat sessions
// for its targets. One task reads the socket, waiting on it without polling;
// answers complete their commands, and events are queued in order for one
// reader, so a handler of an event can send commands and wait for them.
public sealed class DevToolsConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<DevToolsEvent> _events = Channel.CreateUnbounded<DevToolsEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    private long _next;

    private DevToolsConnection(ClientWebSocket socket)
    {
        _socket = socket;
        _reader = Task.Run(ReadAsync);
    }

    // The events, in the order received, until the connection closes.
    public ChannelReader<DevToolsEvent> Events => _events.Reader;

    public static async Task<DevToolsConnection> ConnectAsync(Uri address, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        try
        {
            await socket.ConnectAsync(address, cancellationToken);
            return new DevToolsConnection(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // Sends a command and returns its result, or throws the protocol's error.
    public async Task<JsonElement> SendAsync(string method, object? parameters, string? sessionId, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _next);
        var answer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = answer;
        var request = sessionId is null
            ? JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new { } })
            : JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new { }, sessionId });
        await _send.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(request, WebSocketMessageType.Text, true, cancellationToken);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
        finally
        {
            _send.Release();
        }
        using var registration = cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));
        return await answer.Task;
    }

    private async Task ReadAsync()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        Exception? failure = null;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                message.Write(buffer, 0, received.Count);
                if (!received.EndOfMessage)
                {
                    continue;
                }
                Dispatch(message.ToArray());
                message.SetLength(0);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            failure = exception;
        }
        var closed = new InvalidOperationException("The DevTools protocol connection closed.", failure);
        foreach (var (_, answer) in _pending)
        {
            answer.TrySetException(closed);
        }
        _events.Writer.TryComplete();
    }

    private void Dispatch(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
        {
            if (_pending.TryRemove(id.GetInt64(), out var answer))
            {
                if (root.TryGetProperty("error", out var error))
                {
                    answer.TrySetException(new InvalidOperationException(
                        $"The DevTools protocol answered: {(error.TryGetProperty("message", out var text) ? text.GetString() : error.GetRawText())}"));
                }
                else
                {
                    answer.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
                }
            }
            return;
        }
        if (root.TryGetProperty("method", out var method) && method.GetString() is { } name)
        {
            var session = root.TryGetProperty("sessionId", out var value) ? value.GetString() : null;
            var parameters = root.TryGetProperty("params", out var given) ? given.Clone() : default;
            _events.Writer.TryWrite(new DevToolsEvent(name, session, parameters));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The browser has gone.
        }
        try
        {
            await _reader;
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
        {
        }
        _socket.Dispose();
        _stop.Dispose();
        _send.Dispose();
    }
}
