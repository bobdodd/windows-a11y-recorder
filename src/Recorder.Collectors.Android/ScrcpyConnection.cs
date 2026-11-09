using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Recorder.Collectors.Android;

/// <summary>Why the phone's screen could not be recorded, in words for the operator.</summary>
public sealed class ScrcpyUnavailableException(string message, Exception? inner = null)
    : IOException(message, inner);

/// <summary>
/// scrcpy's video socket, after the codec id. Read its headers and packets
/// with <see cref="ReadHeaderAsync"/> and <see cref="ReadPacketAsync"/>.
/// Disposing it closes the socket, which ends the server on the phone.
/// </summary>
public sealed class ScrcpyVideoConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly Func<ValueTask>? _onDispose;
    private readonly byte[] _header = new byte[ScrcpyProtocol.HeaderSize];
    private int _disposed;

    public ScrcpyVideoConnection(Stream stream, uint codecId, Func<ValueTask>? onDispose = null, Func<string?>? serverLog = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        CodecId = codecId;
        _onDispose = onDispose;
        ServerLog = serverLog ?? (() => null);
    }

    public uint CodecId { get; }

    /// <summary>The server's last lines, for a reason the stream ended.</summary>
    public Func<string?> ServerLog { get; }

    /// <summary>The next header, or null when the stream ended cleanly between packets.</summary>
    /// <exception cref="FormatException">The bytes are not a 5.0.1 header.</exception>
    /// <exception cref="EndOfStreamException">The stream ended inside a header.</exception>
    public async ValueTask<ScrcpyHeader?> ReadHeaderAsync(CancellationToken cancellation)
    {
        var read = await _stream.ReadAtLeastAsync(_header, _header.Length, throwOnEndOfStream: false, cancellation).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < _header.Length)
        {
            throw new EndOfStreamException("The stream ended inside a header.");
        }

        return ScrcpyProtocol.ParseHeader(_header);
    }

    /// <exception cref="EndOfStreamException">The stream ended inside the packet.</exception>
    public async ValueTask ReadPacketAsync(Memory<byte> packet, CancellationToken cancellation) =>
        await _stream.ReadExactlyAsync(packet, cancellation).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stream.DisposeAsync().ConfigureAwait(false);
        if (_onDispose is not null)
        {
            await _onDispose().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the codec id from a stream that has just connected, and returns
    /// the connection. A stream that is not H.264 is closed and reported.
    /// </summary>
    public static async Task<ScrcpyVideoConnection> ConnectAsync(
        Stream stream,
        TimeSpan timeout,
        CancellationToken cancellation,
        Func<ValueTask>? onDispose = null,
        Func<string?>? serverLog = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var log = serverLog ?? (() => null);
        var codec = new byte[4];
        int read;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(timeout);
            read = await stream.ReadAtLeastAsync(codec, codec.Length, throwOnEndOfStream: false, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            await CloseAsync().ConfigureAwait(false);
            throw new ScrcpyUnavailableException(
                $"scrcpy's server on the phone connected but sent nothing within {timeout.TotalSeconds:0} seconds.{Said(log())}");
        }
        catch (IOException ex)
        {
            await CloseAsync().ConfigureAwait(false);
            throw new ScrcpyUnavailableException($"The connection to scrcpy's server failed: {ex.Message}{Said(log())}", ex);
        }

        if (read < codec.Length)
        {
            await CloseAsync().ConfigureAwait(false);
            throw new ScrcpyUnavailableException($"scrcpy's server on the phone closed the connection at once.{Said(log())}");
        }

        var id = ScrcpyProtocol.ParseCodecId(codec);
        if (id != ScrcpyProtocol.H264CodecId)
        {
            await CloseAsync().ConfigureAwait(false);
            var meaning = id switch
            {
                ScrcpyProtocol.DisabledCodecId => "the phone could not capture its screen",
                ScrcpyProtocol.ErrorCodecId => "the server reported a configuration error",
                _ => $"it sent codec {ScrcpyProtocol.CodecName(id)}, not H.264"
            };
            throw new ScrcpyUnavailableException($"scrcpy's server did not start the screen stream: {meaning}.{Said(log())}");
        }

        return new ScrcpyVideoConnection(stream, id, onDispose, log);

        async ValueTask CloseAsync()
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            if (onDispose is not null)
            {
                await onDispose().ConfigureAwait(false);
            }
        }
    }

    /// <summary>" The server said: ..." when it printed anything.</summary>
    public static string Said(string? log) =>
        string.IsNullOrWhiteSpace(log) ? "" : " The server said: " + log.Trim();
}

/// <summary>
/// scrcpy's audio socket, from its start. The server writes the codec id
/// only once it has started capturing, or 0 when it cannot capture the
/// sound, in which case the screen goes on without it
/// (AudioRawRecorder.java, Streamer.writeDisableStream); read it with
/// <see cref="ReadCodecAsync"/>, then the packets as on the video socket,
/// which for the raw codec are all sound, with no session or configuration
/// headers. Disposing it closes the socket; the server then stops capturing
/// the sound and goes on with the screen.
/// </summary>
public sealed class ScrcpyAudioConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly Func<ValueTask>? _onDispose;
    private readonly byte[] _header = new byte[ScrcpyProtocol.HeaderSize];
    private int _disposed;

    public ScrcpyAudioConnection(Stream stream, Func<ValueTask>? onDispose = null, Func<string?>? serverLog = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _onDispose = onDispose;
        ServerLog = serverLog ?? (() => null);
    }

    /// <summary>The server's last lines, for a reason the sound ended.</summary>
    public Func<string?> ServerLog { get; }

    /// <summary>
    /// The codec id, once the server has started capturing. A sound that is
    /// not raw PCM is reported, not read.
    /// </summary>
    /// <exception cref="ScrcpyUnavailableException">The server sent no codec, or not the raw one.</exception>
    public async Task ReadCodecAsync(TimeSpan timeout, CancellationToken cancellation)
    {
        var codec = new byte[4];
        int read;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(timeout);
            read = await _stream.ReadAtLeastAsync(codec, codec.Length, throwOnEndOfStream: false, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new ScrcpyUnavailableException(
                $"scrcpy's server on the phone did not start the sound within {timeout.TotalSeconds:0} seconds.{ScrcpyVideoConnection.Said(ServerLog())}");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw new ScrcpyUnavailableException($"The sound's connection to scrcpy's server failed: {ex.Message}{ScrcpyVideoConnection.Said(ServerLog())}", ex);
        }

        if (read < codec.Length)
        {
            throw new ScrcpyUnavailableException($"scrcpy's server on the phone closed the sound's connection at once.{ScrcpyVideoConnection.Said(ServerLog())}");
        }

        var id = ScrcpyProtocol.ParseCodecId(codec);
        if (id != ScrcpyProtocol.RawCodecId)
        {
            var meaning = id switch
            {
                ScrcpyProtocol.DisabledCodecId => "the phone could not capture its sound",
                ScrcpyProtocol.ErrorCodecId => "the server reported a configuration error",
                _ => $"it sent codec {ScrcpyProtocol.CodecName(id)}, not raw sound"
            };
            throw new ScrcpyUnavailableException($"scrcpy's server did not start the sound: {meaning}.{ScrcpyVideoConnection.Said(ServerLog())}");
        }
    }

    /// <summary>The next packet's header, or null when the stream ended cleanly between packets.</summary>
    /// <exception cref="FormatException">The bytes are not a 5.0.1 header.</exception>
    /// <exception cref="EndOfStreamException">The stream ended inside a header.</exception>
    public async ValueTask<ScrcpyHeader?> ReadHeaderAsync(CancellationToken cancellation)
    {
        var read = await _stream.ReadAtLeastAsync(_header, _header.Length, throwOnEndOfStream: false, cancellation).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < _header.Length)
        {
            throw new EndOfStreamException("The stream ended inside a header.");
        }

        return ScrcpyProtocol.ParseHeader(_header);
    }

    /// <exception cref="EndOfStreamException">The stream ended inside the packet.</exception>
    public async ValueTask ReadPacketAsync(Memory<byte> packet, CancellationToken cancellation) =>
        await _stream.ReadExactlyAsync(packet, cancellation).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stream.DisposeAsync().ConfigureAwait(false);
        if (_onDispose is not null)
        {
            await _onDispose().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// The server's two sockets. The server on the phone is stopped once both
/// are closed.
/// </summary>
public sealed record ScrcpyConnections(ScrcpyVideoConnection Video, ScrcpyAudioConnection Audio);

/// <summary>
/// Starts scrcpy's server on the phone and connects to its video and audio
/// sockets (android-implementation-plan.md, slices 3 and 4).
///
/// The recorder listens on a free loopback port and has adb reverse the
/// server's socket name to it, so the server, once started, connects to the
/// recorder, as scrcpy itself does by default: the video socket first, then
/// the audio socket (DesktopConnection.java). Once both are connected the
/// reverse is removed. The server's printed lines go to a log in the
/// recording.
/// </summary>
public static class ScrcpyLink
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan CodecTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How many of the server's last lines a reason quotes.</summary>
    private const int QuotedLines = 3;

    /// <param name="serverPath">The bundled server, scrcpy-server-v5.0.1.</param>
    /// <param name="logPath">Where the server's printed lines are written.</param>
    public static async Task<ScrcpyConnections> OpenAsync(
        IAdb adb,
        string serial,
        string serverPath,
        string logPath,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        if (!File.Exists(serverPath))
        {
            throw new ScrcpyUnavailableException(
                $"The scrcpy server {AndroidTools.ScrcpyVersion}, for the phone's screen, is missing: {serverPath}. Install the recorder again.");
        }

        await RunAsync(
            adb,
            ["-s", serial, "push", serverPath, ScrcpyProtocol.DeviceServerPath],
            TimeSpan.FromSeconds(30),
            "adb could not copy scrcpy's server to the phone",
            cancellation).ConfigureAwait(false);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(2);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var scid = Random.Shared.Next() & 0x7FFF_FFFF;
        var socketName = ScrcpyProtocol.SocketName(scid);
        var reversed = false;
        IAdbRunning? server = null;
        TcpClient? video = null;
        TcpClient? audio = null;
        var log = new ServerLog(logPath);
        try
        {
            await RunAsync(
                adb,
                ["-s", serial, "reverse", "localabstract:" + socketName, "tcp:" + port.ToString(CultureInfo.InvariantCulture)],
                TimeSpan.FromSeconds(10),
                "adb could not connect the phone's screen stream to the recorder",
                cancellation).ConfigureAwait(false);
            reversed = true;

            server = adb.Start(["-s", serial, "shell", .. ScrcpyProtocol.ServerCommand(scid, AndroidTools.ScrcpyVersion)], log.Add);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(ConnectTimeout);
            video = await AcceptAsync(listener, server, log, limit, cancellation).ConfigureAwait(false);
            audio = await AcceptAsync(listener, server, log, limit, cancellation).ConfigureAwait(false);
            listener.Stop();
            await RemoveReverseAsync(adb, serial, socketName).ConfigureAwait(false);
            reversed = false;

            // The server is stopped once both sockets are closed: the sound
            // collector closes its socket before the screen collector closes
            // the video socket, and the server goes on with the screen alone
            // (Server.java, Completion).
            var running = server;
            server = null;
            var open = 2;
            async ValueTask Release()
            {
                if (Interlocked.Decrement(ref open) == 0)
                {
                    await running.DisposeAsync().ConfigureAwait(false);
                    log.Dispose();
                }
            }

            var audioClient = audio;
            var sound = new ScrcpyAudioConnection(
                audioClient.GetStream(),
                async () =>
                {
                    audioClient.Dispose();
                    await Release().ConfigureAwait(false);
                },
                () => log.Last(QuotedLines));
            audio = null;
            var videoClient = video;
            video = null;
            ScrcpyVideoConnection screen;
            try
            {
                screen = await ScrcpyVideoConnection.ConnectAsync(
                    videoClient.GetStream(),
                    CodecTimeout,
                    cancellation,
                    async () =>
                    {
                        videoClient.Dispose();
                        await Release().ConfigureAwait(false);
                    },
                    () => log.Last(QuotedLines)).ConfigureAwait(false);
            }
            catch
            {
                await sound.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return new ScrcpyConnections(screen, sound);
        }
        catch
        {
            listener.Stop();
            video?.Dispose();
            audio?.Dispose();
            if (server is not null)
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }

            if (reversed)
            {
                await RemoveReverseAsync(adb, serial, socketName).ConfigureAwait(false);
            }

            log.Dispose();
            throw;
        }
    }

    /// <summary>The server's next connection, or why it did not come.</summary>
    private static async Task<TcpClient> AcceptAsync(
        TcpListener listener,
        IAdbRunning server,
        ServerLog log,
        CancellationTokenSource limit,
        CancellationToken cancellation)
    {
        var accepting = listener.AcceptTcpClientAsync(limit.Token).AsTask();
        var first = await Task.WhenAny(accepting, server.Exited).ConfigureAwait(false);
        if (first != accepting)
        {
            limit.Cancel();
            _ = accepting.ContinueWith(task => task.Result.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
            throw new ScrcpyUnavailableException(
                $"scrcpy's server on the phone stopped before it connected.{ScrcpyVideoConnection.Said(log.Last(QuotedLines))}");
        }

        TcpClient client;
        try
        {
            client = await accepting.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new ScrcpyUnavailableException(
                $"scrcpy's server on the phone did not connect within {ConnectTimeout.TotalSeconds:0} seconds.{ScrcpyVideoConnection.Said(log.Last(QuotedLines))}");
        }

        client.NoDelay = true;
        return client;
    }

    private static async Task<AdbResult> RunAsync(
        IAdb adb,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string failure,
        CancellationToken cancellation)
    {
        AdbResult result;
        try
        {
            result = await adb.RunAsync(arguments, timeout, cancellation).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new ScrcpyUnavailableException($"{failure}: {ex.Message}", ex);
        }

        if (result.ExitCode != 0)
        {
            throw new ScrcpyUnavailableException($"{failure}: {(result.Text.Length == 0 ? $"adb exited with {result.ExitCode}" : result.Text)}");
        }

        return result;
    }

    private static async Task RemoveReverseAsync(IAdb adb, string serial, string socketName)
    {
        try
        {
            await adb.RunAsync(
                ["-s", serial, "reverse", "--remove", "localabstract:" + socketName],
                TimeSpan.FromSeconds(10),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // adb's server ends its reverses when the phone is unplugged.
        }
    }

    /// <summary>The server's printed lines: written to the recording's log, and the last few kept for reasons.</summary>
    private sealed class ServerLog(string path) : IDisposable
    {
        private static readonly System.Text.RegularExpressions.Regex InformationLine =
            new(@"^\s*\[server\]\s+(VERBOSE|DEBUG|INFO):", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private readonly object _gate = new();
        private readonly Queue<string> _last = new();
        private StreamWriter? _writer;
        private bool _closed;

        public void Add(string line)
        {
            lock (_gate)
            {
                if (_closed)
                {
                    return;
                }

                // Only warnings, errors and lines with no level are quoted
                // in a reason: the server's INFO lines, such as its
                // greeting naming the device, say nothing about a failure.
                if (Quotable(line))
                {
                    _last.Enqueue(line);
                }

                while (_last.Count > QuotedLines)
                {
                    _last.Dequeue();
                }

                try
                {
                    if (_writer is null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        _writer = new StreamWriter(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
                    }

                    _writer.WriteLine(line);
                }
                catch (IOException)
                {
                    // The log is for diagnosis; the stream goes on without it.
                }
            }
        }

        /// <summary>Whether a printed line may explain a failure: not one of the server's VERBOSE, DEBUG or INFO lines.</summary>
        internal static bool Quotable(string line) =>
            !string.IsNullOrWhiteSpace(line) &&
            !InformationLine.IsMatch(line);

        public string? Last(int count)
        {
            lock (_gate)
            {
                return _last.Count == 0 ? null : string.Join(" ", _last.TakeLast(count));
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _closed = true;
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
