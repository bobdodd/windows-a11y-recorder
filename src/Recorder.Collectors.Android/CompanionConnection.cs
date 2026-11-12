using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Recorder.Collectors.Android;

/// <summary>Why the companion app could not be reached, in words for the operator.</summary>
public sealed class CompanionUnavailableException(string message, Exception? inner = null)
    : IOException(message, inner);

/// <summary>A display message and the session time the recorder read it.</summary>
public sealed record ReceivedDisplay(CompanionDisplay Display, long ReceivedAt);

/// <summary>An Accessibility volume message and the session time the recorder read it.</summary>
public sealed record ReceivedAccessibilityVolume(CompanionAccessibilityVolume Volume, long ReceivedAt);

/// <summary>
/// An accessibility event and the session time the recorder read it; the
/// event is null, with the problem, for one the recorder could not read.
/// </summary>
public sealed record ReceivedAccessibilityEvent(
    Recorder.Contracts.AndroidAccessibilityEvent? Event,
    long ReceivedAt,
    string? Problem = null);

/// <summary>A snapshot's line, still to be read, and the session time the recorder read it.</summary>
public sealed record ReceivedAccessibilityTree(string Line, long ReceivedAt);

/// <summary>A screenshot's line, still to be read, and the session time the recorder read it.</summary>
public sealed record ReceivedScreenshot(string Line, long ReceivedAt);

/// <summary>A notice that a setting was written, and the session time the recorder read it.</summary>
public sealed record ReceivedSettingChange(CompanionSettingChanged Change, long ReceivedAt);

/// <summary>
/// One connection to the companion app. The app sends its greeting first;
/// after that the recorder asks for clock samples and the app sends display
/// changes as they happen, each on its own line. Every line is stamped with
/// the session time it was read, so a reply's receive time is never earlier
/// than its arrival.
/// </summary>
public sealed class CompanionConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly Func<long> _now;
    private readonly Func<ValueTask>? _onDispose;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<(CompanionClockReply Reply, long At)>> _pending = new();
    private readonly Channel<ReceivedDisplay> _displays = Channel.CreateUnbounded<ReceivedDisplay>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<ReceivedAccessibilityVolume> _volumes = Channel.CreateUnbounded<ReceivedAccessibilityVolume>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<ReceivedAccessibilityEvent> _events = Channel.CreateUnbounded<ReceivedAccessibilityEvent>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<ReceivedAccessibilityTree> _trees = Channel.CreateUnbounded<ReceivedAccessibilityTree>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<ReceivedScreenshot> _screenshots = Channel.CreateUnbounded<ReceivedScreenshot>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<ReceivedSettingChange> _settings = Channel.CreateUnbounded<ReceivedSettingChange>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _closing = new();
    private Task? _readLoop;
    private long _nextId;
    private int _disposed;

    private CompanionConnection(Stream stream, Func<long> now, Func<ValueTask>? onDispose)
    {
        _stream = stream;
        _reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        _now = now;
        _onDispose = onDispose;
    }

    public CompanionHello Hello { get; private set; } = null!;

    /// <summary>The session time the greeting was read.</summary>
    public long HelloReceivedAt { get; private set; }

    /// <summary>Display messages, in the order they arrived; completes when the connection ends.</summary>
    public ChannelReader<ReceivedDisplay> Displays => _displays.Reader;

    /// <summary>Accessibility volume messages, in the order they arrived; completes when the connection ends.</summary>
    public ChannelReader<ReceivedAccessibilityVolume> AccessibilityVolumes => _volumes.Reader;

    /// <summary>
    /// Accessibility events, in the order they arrived, once asked for with
    /// <see cref="RequestAccessibilityEventsAsync"/>; completes when the connection ends.
    /// </summary>
    public ChannelReader<ReceivedAccessibilityEvent> AccessibilityEvents => _events.Reader;

    /// <summary>
    /// Snapshots of the accessibility tree, in the order they arrived, once
    /// asked for with <see cref="RequestAccessibilityTreesAsync"/>; completes when the connection ends.
    /// </summary>
    public ChannelReader<ReceivedAccessibilityTree> AccessibilityTrees => _trees.Reader;

    /// <summary>
    /// Screenshots and refusals, in the order they arrived, once asked for
    /// with <see cref="RequestScreenshotsAsync"/>; completes when the connection ends.
    /// </summary>
    public ChannelReader<ReceivedScreenshot> Screenshots => _screenshots.Reader;

    /// <summary>
    /// Notices that a setting was written, in the order they arrived, once
    /// asked for with <see cref="RequestSettingChangesAsync"/>; completes when the connection ends.
    /// </summary>
    public ChannelReader<ReceivedSettingChange> SettingChanges => _settings.Reader;

    /// <summary>Completes when the connection ends, with the reason it ended.</summary>
    public Task<string> Closed => _closed.Task;

    /// <summary>The last error message the app sent, if any.</summary>
    public string? LastAppError { get; private set; }

    private readonly TaskCompletionSource<string> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Reads the app's greeting from an open stream. The stream is the
    /// connection's from then on, and is closed with it.
    /// </summary>
    /// <param name="now">The session time, in nanoseconds.</param>
    public static async Task<CompanionConnection> ConnectAsync(
        Stream stream,
        Func<long> now,
        TimeSpan timeout,
        CancellationToken cancellation,
        Func<ValueTask>? onDispose = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(now);
        var connection = new CompanionConnection(stream, now, onDispose);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(timeout);
            string? line;
            try
            {
                line = await connection._reader.ReadLineAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new CompanionUnavailableException(
                    $"The companion app did not greet the recorder within {timeout.TotalSeconds:0} seconds. Check that it is on in the phone's Accessibility settings.");
            }
            catch (IOException ex)
            {
                throw new CompanionUnavailableException($"The connection to the companion app failed: {ex.Message}", ex);
            }

            if (line is null)
            {
                // adb accepts the forwarded connection itself, and closes it
                // when nothing on the phone is listening on the socket.
                throw new CompanionUnavailableException(
                    "Nothing on the phone answered: the companion app is not installed, or not on in the phone's Accessibility settings.");
            }

            connection.HelloReceivedAt = now();
            CompanionMessage message;
            try
            {
                message = CompanionProtocol.Parse(line);
            }
            catch (FormatException ex)
            {
                throw new CompanionUnavailableException(ex.Message, ex);
            }

            if (message is not CompanionHello hello)
            {
                throw new CompanionUnavailableException("The companion app did not start with its greeting.");
            }

            if (hello.Protocol != CompanionProtocol.Version)
            {
                throw new CompanionUnavailableException(
                    $"The companion app speaks protocol {hello.Protocol}; this recorder speaks {CompanionProtocol.Version}. Install the recorder's own companion app.");
            }

            connection.Hello = hello;
            connection._readLoop = Task.Run(connection.ReadLoopAsync);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>One clock request and its reply.</summary>
    public async Task<PhoneClockExchange> ExchangeClockAsync(TimeSpan timeout, CancellationToken cancellation)
    {
        var id = Interlocked.Increment(ref _nextId);
        var reply = new TaskCompletionSource<(CompanionClockReply Reply, long At)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = reply;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(CompanionProtocol.ClockRequest(id));
            long sent;
            await _writeLock.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                sent = _now();
                await _stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
                await _stream.FlushAsync(cancellation).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                throw new CompanionUnavailableException($"The connection to the companion app failed: {ex.Message}", ex);
            }
            finally
            {
                _writeLock.Release();
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(timeout);
            (CompanionClockReply Reply, long At) answer;
            try
            {
                answer = await reply.Task.WaitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new CompanionUnavailableException(
                    $"The companion app did not answer a clock request within {timeout.TotalMilliseconds:0} milliseconds.");
            }

            return new PhoneClockExchange(sent, answer.Reply.Received, answer.Reply.Sent, answer.At);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Asks the app to raise the Accessibility volume; its answer comes on
    /// <see cref="AccessibilityVolumes"/>.
    /// </summary>
    public Task RequestAccessibilityVolumeAsync(int minimumPercent, CancellationToken cancellation) =>
        SendAsync(CompanionProtocol.AccessibilityVolumeRequest(minimumPercent), cancellation);

    /// <summary>
    /// Asks the app for every accessibility event from now on; they come on
    /// <see cref="AccessibilityEvents"/>.
    /// </summary>
    public Task RequestAccessibilityEventsAsync(CancellationToken cancellation) =>
        SendAsync(CompanionProtocol.AccessibilityEventsRequest, cancellation);

    /// <summary>
    /// Asks the app for a snapshot of every window's tree now and more on
    /// each change; they come on <see cref="AccessibilityTrees"/>.
    /// </summary>
    public Task RequestAccessibilityTreesAsync(CancellationToken cancellation) =>
        SendAsync(CompanionProtocol.AccessibilityTreesRequest, cancellation);

    /// <summary>
    /// Asks the app for a screenshot now and one each time a window
    /// settles; they come on <see cref="Screenshots"/>.
    /// </summary>
    public Task RequestScreenshotsAsync(CancellationToken cancellation) =>
        SendAsync(CompanionProtocol.ScreenshotsRequest, cancellation);

    /// <summary>
    /// Asks the app to say each time one of the settings is written; the
    /// notices come on <see cref="SettingChanges"/>.
    /// </summary>
    public Task RequestSettingChangesAsync(IEnumerable<Recorder.Contracts.AndroidSetting> settings, CancellationToken cancellation) =>
        SendAsync(CompanionProtocol.SettingsRequest(settings), cancellation);

    private async Task SendAsync(string line, CancellationToken cancellation)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        await _writeLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
            await _stream.FlushAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw new CompanionUnavailableException($"The connection to the companion app failed: {ex.Message}", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        // adb's forward closes the connection in the same way whether the
        // cable was unplugged, adb stopped, or the app was turned off, so
        // the reason does not say which (validation plan, test 14).
        var reason = "The connection closed: the phone was unplugged, adb stopped, or the companion app was turned off.";
        try
        {
            while (true)
            {
                var line = await _reader.ReadLineAsync(_closing.Token).ConfigureAwait(false);
                var at = _now();
                if (line is null)
                {
                    break;
                }

                CompanionMessage message;
                try
                {
                    message = CompanionProtocol.Parse(line);
                }
                catch (FormatException ex)
                {
                    reason = ex.Message;
                    break;
                }

                switch (message)
                {
                    case CompanionClockReply clock:
                        if (_pending.TryGetValue(clock.Id, out var waiting))
                        {
                            waiting.TrySetResult((clock, at));
                        }

                        break;
                    case CompanionDisplay display:
                        _displays.Writer.TryWrite(new ReceivedDisplay(display, at));
                        break;
                    case CompanionAccessibilityVolume volume:
                        _volumes.Writer.TryWrite(new ReceivedAccessibilityVolume(volume, at));
                        break;
                    case CompanionAccessibilityEvent accessibilityEvent:
                        _events.Writer.TryWrite(new ReceivedAccessibilityEvent(accessibilityEvent.Event, at, accessibilityEvent.Problem));
                        break;
                    case CompanionAccessibilityTreeLine tree:
                        _trees.Writer.TryWrite(new ReceivedAccessibilityTree(tree.Line, at));
                        break;
                    case CompanionScreenshotLine screenshot:
                        _screenshots.Writer.TryWrite(new ReceivedScreenshot(screenshot.Line, at));
                        break;
                    case CompanionSettingChanged setting:
                        _settings.Writer.TryWrite(new ReceivedSettingChange(setting, at));
                        break;
                    case CompanionError error:
                        LastAppError = error.Message;
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            reason = "The recorder closed the connection.";
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            reason = $"The connection to the companion app failed: {ex.Message}";
        }
        finally
        {
            _displays.Writer.TryComplete();
            _volumes.Writer.TryComplete();
            _events.Writer.TryComplete();
            _trees.Writer.TryComplete();
            _screenshots.Writer.TryComplete();
            _settings.Writer.TryComplete();
            foreach (var waiting in _pending.Values)
            {
                waiting.TrySetException(new CompanionUnavailableException(reason));
            }

            _closed.TrySetResult(reason);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _closing.Cancel();
        _stream.Dispose();
        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // Ended with the stream.
            }
        }

        _closed.TrySetResult("The recorder closed the connection.");
        _displays.Writer.TryComplete();
        _volumes.Writer.TryComplete();
        _events.Writer.TryComplete();
        _trees.Writer.TryComplete();
        _screenshots.Writer.TryComplete();
        _settings.Writer.TryComplete();
        _reader.Dispose();
        _closing.Dispose();
        if (_onDispose is not null)
        {
            await _onDispose().ConfigureAwait(false);
        }
    }
}

/// <summary>Opens connections to the companion app through adb forward.</summary>
public static class CompanionLink
{
    public static readonly TimeSpan GreetingTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Forwards a free local TCP port to the app's socket on the phone with
    /// the serial, connects to it, and reads the greeting. The forward is
    /// removed when the connection is disposed.
    /// </summary>
    public static async Task<CompanionConnection> OpenAsync(
        IAdb adb,
        string serial,
        Func<long> now,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        AdbResult forward;
        try
        {
            forward = await adb.RunAsync(
                ["-s", serial, "forward", "tcp:0", "localabstract:" + CompanionProtocol.SocketName],
                TimeSpan.FromSeconds(10),
                cancellation).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new CompanionUnavailableException($"adb could not forward a port to the phone: {ex.Message}", ex);
        }

        // With tcp:0, adb picks the port and prints it.
        if (forward.ExitCode != 0
            || !int.TryParse(forward.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is <= 0 or > 65535)
        {
            throw new CompanionUnavailableException(
                $"adb could not forward a port to the phone: {(forward.Text.Length == 0 ? "no port given" : forward.Text)}");
        }

        async ValueTask RemoveForwardAsync()
        {
            try
            {
                await adb.RunAsync(
                    ["-s", serial, "forward", "--remove", "tcp:" + port.ToString(CultureInfo.InvariantCulture)],
                    TimeSpan.FromSeconds(10),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // adb's server ends its forwards when it stops, or when the
                // phone is unplugged.
            }
        }

        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, cancellation).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            client.Dispose();
            await RemoveForwardAsync().ConfigureAwait(false);
            throw new CompanionUnavailableException($"The recorder could not connect to adb's forwarded port {port}: {ex.Message}", ex);
        }

        return await CompanionConnection.ConnectAsync(
            client.GetStream(),
            now,
            GreetingTimeout,
            cancellation,
            async () =>
            {
                client.Dispose();
                await RemoveForwardAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    /// <summary>The session-independent time used where no session clock exists, such as the checks.</summary>
    public static long StopwatchNanoseconds() =>
        (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));
}
