using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Records the screenshots the companion app takes of the phone's display
/// (android-checks.md, step 4a): one when the recording starts and one each
/// time a window has had no change for half a second, without the video's
/// compression, for measuring colours.
///
/// At the start it asks the companion app, through the companion
/// collector's connection, for screenshots. Each is written as a PNG under
/// <see cref="AndroidScreenshotRecords.Folder"/>, its hash given to the
/// recording's manifest, and recorded on
/// <see cref="AndroidScreenshotRecords.Channel"/> at Android's time for it
/// through the companion collector's clock fit; without a fit, at the time
/// the recorder read it. A screenshot Android refused is recorded with its
/// code. A phone whose companion app cannot be reached does not stop the
/// recording: the collector records why, reports itself degraded, and the
/// recording goes on.
/// </summary>
public sealed class AndroidScreenshotCollector : ICaptureCollector
{
    public const string Type = "android.screenshots";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly string _serial;
    private readonly IPhoneScreenshots _screenshots;
    private readonly IPhoneClock? _clock;
    private readonly object _gate = new();
    private readonly List<long> _delays = [];
    private readonly List<long> _makeTimes = [];
    private readonly List<long> _sizes = [];
    private readonly SortedDictionary<string, int> _refusals = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _closing = new();

    private CollectorInitializationContext? _context;
    private Task? _running;
    private long _start;
    private long _lastTime = long.MinValue;
    private volatile string? _unavailable;
    private volatile bool _reading;
    private ulong _sequence;
    private ulong _lifecycleSequence;
    private long _written;
    private long _requested;
    private long _unreadable;
    private long _earlier;
    private long _callTimes;
    private long _lastIndex;
    private long _skipped;
    private string? _colourSpace;
    private long _converted;
    private bool _stopping;
    private bool _clockless;

    /// <param name="screenshots">The companion collector, which asks its app for the screenshots.</param>
    /// <param name="clock">The companion collector's clock fit; null to place every screenshot at its receipt.</param>
    public AndroidScreenshotCollector(string serial, IPhoneScreenshots screenshots, IPhoneClock? clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(screenshots);
        _serial = serial;
        _screenshots = screenshots;
        _clock = clock;
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidScreenshotCollector),
            typeof(AndroidScreenshotCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidScreenshotRecords.Channel],
            "companion-accessibility-service");
    }

    public CollectorDescriptor Descriptor { get; }

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;

    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public string? HealthReason => _unavailable;

    public string? Summary
    {
        get
        {
            if (!_reading)
            {
                return null;
            }

            var written = Interlocked.Read(ref _written);
            string text;
            lock (_gate)
            {
                text = written == 0
                    ? "Phone screenshots: none yet"
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Phone screenshots: {written} ({Interlocked.Read(ref _requested)} at the start, {written - Interlocked.Read(ref _requested)} as a window settled)");
                if (written > 0 && Median(_sizes) is { } size)
                {
                    text += string.Create(CultureInfo.InvariantCulture, $", a median {size / 1024.0:0} KB each");
                }

                if (_colourSpace is not null)
                {
                    text += Interlocked.Read(ref _converted) > 0
                        ? $", converted to sRGB from {_colourSpace}"
                        : $", in {_colourSpace}";
                }

                if (Median(_makeTimes) is { } make)
                {
                    text += string.Create(
                        CultureInfo.InvariantCulture,
                        $", the phone took a median {make / 1_000_000.0:0} ms from asking to the PNG");
                }

                if (_refusals.Count > 0)
                {
                    var refused = string.Join(
                        ", ",
                        _refusals.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Value} {pair.Key}")));
                    text += $", refused by Android: {refused}";
                }
            }

            if (Interlocked.Read(ref _callTimes) is var calls and > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {calls} placed by the companion's call as Android's time was outside it");
            }

            if (Interlocked.Read(ref _skipped) is var skipped and > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {skipped} missing");
            }

            if (Interlocked.Read(ref _unreadable) is var unreadable and > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {unreadable} could not be read");
            }

            if (Median(_delays) is { } delay)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", median delay from PNG to recorder {delay / 1_000_000.0:0} ms");
            }
            else if (_clockless)
            {
                text += ", placed by the time each arrived, as there is no phone clock";
            }

            return text + ".";
        }
    }

    public ValueTask<CapabilityResult> InitializeAsync(
        CollectorInitializationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Created)
            {
                return ValueTask.FromResult(new CapabilityResult(
                    CapabilityStatus.Incompatible,
                    Descriptor.Channels,
                    ["collector-already-initialized"],
                    false,
                    false));
            }

            _context = context;
            LifecycleState = CollectorLifecycleState.Ready;
            HealthState = CollectorHealthState.Healthy;
            return ValueTask.FromResult(CapabilityResult.Supported(Descriptor.Channels.ToArray()));
        }
    }

    public ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready || _context is null)
            {
                return ValueTask.FromResult(CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}."));
            }

            LifecycleState = CollectorLifecycleState.Running;
            _start = boundary.MonotonicNanoseconds;
        }

        EmitLifecycle("started", boundary.MonotonicNanoseconds, boundary.Utc);
        _running = Task.Run(RunAsync, CancellationToken.None);
        return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
    }

    public async ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        lock (_gate)
        {
            if (LifecycleState is CollectorLifecycleState.Stopped or CollectorLifecycleState.Disposed)
            {
                return CollectorTransitionResult.Success(LifecycleState);
            }

            if (LifecycleState is not (CollectorLifecycleState.Running or CollectorLifecycleState.Failed))
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot stop from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Stopping;
            _stopping = true;
        }

        await CloseAsync().ConfigureAwait(false);
        var closing = CollectorClosingTimestamp.Resolve(_context?.Clock, boundary);
        LifecycleState = CollectorLifecycleState.Stopped;
        EmitLifecycle("stopped", closing, boundary.Utc);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopping = true;
        }

        await CloseAsync().ConfigureAwait(false);
        _closing.Dispose();
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private async Task CloseAsync()
    {
        try
        {
            _closing.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Closed already.
        }

        if (_running is not null)
        {
            await _running.ConfigureAwait(false);
        }
    }

    private bool Stopping()
    {
        lock (_gate)
        {
            return _stopping;
        }
    }

    private async Task RunAsync()
    {
        var token = _closing.Token;
        try
        {
            var reader = await _screenshots.RequestScreenshotsAsync(token).ConfigureAwait(false);
            if (reader is null)
            {
                if (!Stopping())
                {
                    Unavailable(
                        "the companion app is not connected, so they could not be asked for. The android.companion line says why.",
                        connectionLost: false);
                }

                return;
            }

            var ready = await WaitForClockAsync(token).ConfigureAwait(false);
            _clockless = !ready;
            _reading = true;
            try
            {
                await foreach (var received in reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    Handle(received, ready);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Stopping: the screenshots that arrived before Stop are kept.
                while (reader.TryRead(out var received))
                {
                    Handle(received, ready);
                }

                return;
            }

            if (!Stopping())
            {
                Unavailable(
                    "the connection to the companion app ended: the phone was unplugged, adb stopped, or the companion app was turned off.",
                    connectionLost: true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopped before the screenshots were asked for.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Unavailable($"they could not be read: {Sentence(ex.Message)}", connectionLost: false);
        }
    }

    private void Handle(ReceivedScreenshot received, bool ready)
    {
        var (message, problem) = CompanionProtocol.ReadScreenshot(received.Line);
        if (message is null)
        {
            Unreadable(received.ReceivedAt, problem);
            return;
        }

        NoteIndex(message.Index);
        switch (message)
        {
            case CompanionScreenshot screenshot:
                Write(screenshot, received.ReceivedAt, ready);
                break;
            case CompanionScreenshotRefusal refusal:
                Refused(refusal, received.ReceivedAt, ready);
                break;
        }
    }

    private void Write(CompanionScreenshot screenshot, long receivedAt, bool ready)
    {
        if (PngSize(screenshot.Png) is not { } size || size.Width != screenshot.Width || size.Height != screenshot.Height)
        {
            Unreadable(receivedAt, "its PNG is not a PNG of the size the companion gave");
            return;
        }

        // Android's time, when it is inside the companion's call; else
        // halfway through the call.
        var call = Math.Max(0, screenshot.Captured - screenshot.Requested);
        var android = screenshot.TimestampMillis * 1_000_000;
        var inside = android >= screenshot.Requested - 1_000_000 && android <= screenshot.Captured + 1_000_000;
        var phone = inside ? android : screenshot.Requested + (call / 2);
        var extra = inside ? 1_000_000 : call - (call / 2);
        var placement = inside ? AndroidScreenshotRecords.PhoneScreenshotTimeFlag : AndroidScreenshotRecords.PhoneCallTimeFlag;
        var mapping = ready ? _clock?.Map(phone) : null;
        long time;
        string[] flags;
        long? uncertainty = null;
        if (mapping is null)
        {
            time = receivedAt;
            flags = [AndroidScreenRecords.ReceiptTimeFlag];
        }
        else
        {
            time = mapping.SessionNanoseconds;
            flags = [placement];
            uncertainty = mapping.UncertaintyNanoseconds + extra;
            if (time < _start)
            {
                // From before the recording.
                Interlocked.Increment(ref _earlier);
                return;
            }

            if (_clock?.Map(screenshot.Encoded) is { } encoded)
            {
                lock (_delays)
                {
                    _delays.Add(receivedAt - encoded.SessionNanoseconds);
                }
            }
        }

        var context = _context;
        if (context is null)
        {
            return;
        }

        var number = Interlocked.Increment(ref _written);
        var relative = AndroidScreenshotRecords.ScreenshotPath(number);
        var path = Path.Combine(context.SessionDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        var hash = SHA256.HashData(screenshot.Png);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                stream.Write(screenshot.Png);
            }

            File.Move(temporary, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Interlocked.Decrement(ref _written);
            Unreadable(receivedAt, $"it could not be written: {ex.Message}");
            return;
        }

        context.ArtifactHashes?.Record(path, screenshot.Png.Length, hash);
        if (screenshot.Reason == "requested")
        {
            Interlocked.Increment(ref _requested);
        }

        if (!inside)
        {
            Interlocked.Increment(ref _callTimes);
        }

        if (screenshot.Converted)
        {
            Interlocked.Increment(ref _converted);
        }

        lock (_gate)
        {
            _colourSpace = screenshot.ColourSpace;
            _sizes.Add(screenshot.Png.Length);
            _makeTimes.Add(Math.Max(0, screenshot.Encoded - screenshot.Requested));
        }

        Emit(
            Hold(time, ref flags),
            AndroidScreenshotRecords.ScreenshotEventType,
            new AndroidScreenshot(
                _serial,
                screenshot.Index,
                relative,
                screenshot.Reason,
                screenshot.Width,
                screenshot.Height,
                screenshot.Png.Length,
                Convert.ToHexStringLower(hash),
                screenshot.ColourSpace,
                screenshot.Converted,
                screenshot.TimestampMillis,
                screenshot.Requested,
                screenshot.Captured,
                screenshot.Encoded,
                screenshot.LastChangeMillis,
                receivedAt),
            phone,
            uncertainty,
            mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            flags);
    }

    private void Refused(CompanionScreenshotRefusal refusal, long receivedAt, bool ready)
    {
        var name = AndroidScreenshotRecords.RefusalName(refusal.Code);
        lock (_gate)
        {
            _refusals[name] = _refusals.GetValueOrDefault(name) + 1;
        }

        var mapping = ready ? _clock?.Map(refusal.Failed) : null;
        long time;
        string[] flags;
        if (mapping is null)
        {
            time = receivedAt;
            flags = [AndroidScreenRecords.ReceiptTimeFlag];
        }
        else
        {
            time = Math.Max(_start, mapping.SessionNanoseconds);
            flags = [AndroidScreenshotRecords.PhoneCallTimeFlag];
        }

        Emit(
            Hold(time, ref flags),
            AndroidScreenshotRecords.RefusedEventType,
            new AndroidScreenshotRefused(
                _serial,
                refusal.Index,
                refusal.Reason,
                refusal.Code,
                name,
                AndroidScreenshotRecords.RefusalText(refusal.Code),
                refusal.Requested,
                refusal.Failed),
            refusal.Failed,
            mapping?.UncertaintyNanoseconds,
            mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            flags);
    }

    // A gap in the app's count is a screenshot the recorder never saw.
    private void NoteIndex(long index)
    {
        lock (_gate)
        {
            if (_lastIndex > 0 && index > _lastIndex + 1)
            {
                _skipped += index - _lastIndex - 1;
            }

            _lastIndex = Math.Max(_lastIndex, index);
        }
    }

    /// <summary>The width and height in a PNG's header; null for bytes that are not a PNG.</summary>
    internal static (int Width, int Height)? PngSize(ReadOnlySpan<byte> png)
    {
        // The signature, then the IHDR chunk: length, "IHDR", width, height.
        if (png.Length < 24 || !png[..8].SequenceEqual(PngSignature) ||
            png[12] != (byte)'I' || png[13] != (byte)'H' || png[14] != (byte)'D' || png[15] != (byte)'R')
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(png[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(png[20..24]);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    private void Unreadable(long receivedAt, string? problem)
    {
        Interlocked.Increment(ref _unreadable);
        var flags = new[] { AndroidScreenRecords.ReceiptTimeFlag };
        Emit(
            Hold(receivedAt, ref flags),
            AndroidScreenshotRecords.UnavailableEventType,
            new AndroidScreenshotsUnavailable(
                _serial,
                $"A screenshot of the phone could not be read: {Sentence(problem ?? "no reason given")}",
                false),
            qualityFlags: flags);
    }

    /// <summary>The time held at the channel's last time, so that its times do not go backward.</summary>
    private long Hold(long time, ref string[] flags)
    {
        lock (_gate)
        {
            if (time < _lastTime)
            {
                time = _lastTime;
                flags = [.. flags, AndroidCompanionRecords.FitHeldFlag];
            }

            _lastTime = time;
            return time;
        }
    }

    private async Task<bool> WaitForClockAsync(CancellationToken cancellation)
    {
        if (_clock is null)
        {
            return false;
        }

        var first = await Task.WhenAny(_clock.FirstSample, Task.Delay(AndroidAccessibilityCollector.ClockWait, cancellation)).ConfigureAwait(false);
        return first == _clock.FirstSample && await _clock.FirstSample.ConfigureAwait(false);
    }

    private static long? Median(List<long> values)
    {
        lock (values)
        {
            if (values.Count == 0)
            {
                return null;
            }

            var sorted = values.ToArray();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }
    }

    /// <summary>The text ending with a full stop, for a message that does not end with one.</summary>
    private static string Sentence(string text) =>
        text.Length == 0 || text[^1] is '.' or '!' or '?' ? text : text + ".";

    private void Unavailable(string reason, bool connectionLost)
    {
        var text = connectionLost
            ? $"The phone's screenshots stopped being recorded during the recording: {reason} The recording goes on without them."
            : $"The phone's screenshots could not be recorded: {reason} The recording goes on without them.";
        var at = _context?.Clock.GetElapsedNanoseconds() ?? 0;
        var flags = Array.Empty<string>();
        Emit(
            Hold(at, ref flags),
            AndroidScreenshotRecords.UnavailableEventType,
            new AndroidScreenshotsUnavailable(_serial, text, connectionLost),
            qualityFlags: flags);

        // After the record, so that whoever sees the reason finds it.
        HealthState = CollectorHealthState.Degraded;
        _unavailable = text;
    }

    private void Emit<T>(
        long monotonicNanoseconds,
        string eventType,
        T payload,
        long? native = null,
        long? uncertainty = null,
        string? clockMapping = null,
        string[]? qualityFlags = null)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var record = RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            AndroidScreenshotRecords.Channel,
            Interlocked.Increment(ref _sequence),
            monotonicNanoseconds,
            eventType,
            payload,
            qualityFlags ?? []);
        record = record with
        {
            NativeTimestamp = native is { } value
                ? new NativeTimestamp(AndroidCompanionRecords.ClockDomain, value, "ns")
                : null,
            TimestampUncertaintyNanoseconds = uncertainty,
            ClockMappingId = clockMapping ?? record.ClockMappingId
        };
        context.EventSink.TryWrite(record);
    }

    private void EmitLifecycle(string action, long monotonicNanoseconds, DateTimeOffset utc)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var sequence = Interlocked.Increment(ref _lifecycleSequence);
        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "collector.lifecycle",
            sequence,
            monotonicNanoseconds,
            "collector-lifecycle",
            new { action, state = LifecycleState.ToString(), Utc = utc }));
    }
}
