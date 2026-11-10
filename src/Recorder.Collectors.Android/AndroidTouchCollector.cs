using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Records the phone's touches and hardware keys from its raw input events
/// (android-implementation-plan.md, slice 5).
///
/// At the start, <c>getevent -lp</c> lists the input devices, and the touch
/// screen is chosen from them with its axis ranges
/// (<see cref="GeteventDevices"/>); <c>wm size</c> gives the screen's size
/// upright. Then <c>getevent -lt</c> runs for the whole recording through a
/// terminal on the phone (<c>adb shell -tt</c>), because check 4 found its
/// lines held back when read through a pipe. Every line it prints is kept
/// on <see cref="AndroidTouchRecords.RawChannel"/>, a report at a time;
/// each report of the touch screen with a finger on it becomes a
/// <c>touch-report</c> with the fingers in screen pixels
/// (<see cref="TouchTracker"/>, <see cref="TouchGeometry"/>), turned by the
/// display's rotation from the companion app at the report's phone time;
/// and each key line of another device becomes a <c>hardware-key</c>.
///
/// getevent sets each device to CLOCK_MONOTONIC (getevent.c,
/// <c>EVIOCSCLOCKID</c>), the clock the companion app samples, so each
/// record is placed on the session clock through the companion collector's
/// clock fit, and otherwise at the time the recorder read it. Phones
/// without the multi-touch protocol B, which reports each finger in a slot,
/// are not read for touches; their lines are still kept. A phone whose
/// input cannot be read does not stop the recording: the collector records
/// why, reports itself degraded, and the recording goes on.
/// </summary>
public sealed partial class AndroidTouchCollector : ICaptureCollector
{
    public const string Type = "android.touch";

    /// <summary>How long the touches wait for the companion collector's first clock sample before placing lines by their receipt.</summary>
    public static readonly TimeSpan ClockWait = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    private readonly IAdb _adb;
    private readonly string _serial;
    private readonly IPhoneClock? _clock;
    private readonly IPhoneDisplay? _display;
    private readonly object _gate = new();
    private readonly List<long> _delays = [];
    private readonly Dictionary<string, List<string>> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastTimes = new(StringComparer.Ordinal);
    private readonly Channel<(string Line, long ReceivedAt)> _lines =
        Channel.CreateUnbounded<(string, long)>(new UnboundedChannelOptions { SingleReader = true });

    private CollectorInitializationContext? _context;
    private IAdbRunning? _getevent;
    private Task? _running;
    private GeteventDevice? _touchScreen;
    private AndroidAxisRange? _xRange;
    private AndroidAxisRange? _yRange;
    private TouchTracker? _tracker;
    private (int Width, int Height)? _screenSize;
    private volatile string? _unavailable;
    private volatile string? _lastTouch;
    private volatile bool _reading;
    private ulong _rawSequence;
    private ulong _touchSequence;
    private ulong _keySequence;
    private ulong _lifecycleSequence;
    private long _touches;
    private long _reports;
    private long _keys;
    private long _lineCount;
    private bool _stopping;
    private bool _clockless;

    /// <param name="clock">The companion collector's clock fit; null to place every line at its receipt.</param>
    /// <param name="display">The companion collector's display states; null to give positions as if the phone were upright.</param>
    public AndroidTouchCollector(IAdb adb, string serial, IPhoneClock? clock, IPhoneDisplay? display)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        _adb = adb;
        _serial = serial;
        _clock = clock;
        _display = display;
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidTouchCollector),
            typeof(AndroidTouchCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidTouchRecords.RawChannel, AndroidTouchRecords.TouchChannel, AndroidTouchRecords.KeyChannel],
            "adb-shell-getevent");
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

            var touches = Interlocked.Read(ref _touches);
            var reports = Interlocked.Read(ref _reports);
            var keys = Interlocked.Read(ref _keys);
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"Touches: {touches} {(touches == 1 ? "touch" : "touches")}, {reports} {(reports == 1 ? "report" : "reports")}");
            if (_lastTouch is { } last)
            {
                text += ", last " + last;
            }

            text += string.Create(CultureInfo.InvariantCulture, $", {keys} hardware key {(keys == 1 ? "press" : "presses")}");
            if (MedianDelay() is { } delay)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", median delay from phone to recorder {delay / 1_000_000.0:0.0} ms");
            }
            else if (_clockless)
            {
                text += ", placed by the time each line arrived, as there is no phone clock";
            }

            if (_touchScreen is { } screen)
            {
                text += $", touch screen {screen.Path}" + (screen.Name is { } name ? $" \"{name}\"" : "");
                if (_xRange is { } x && _yRange is { } y)
                {
                    text += string.Create(CultureInfo.InvariantCulture, $" with x {x.Minimum} to {x.Maximum} and y {y.Minimum} to {y.Maximum}");
                }
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
            return ValueTask.FromResult(new CapabilityResult(
                CapabilityStatus.SupportedWithLimitations,
                Descriptor.Channels,
                ["multi-touch-protocol-b-only"],
                true,
                false));
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
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private async Task CloseAsync()
    {
        // getevent never ends by itself. Stopping adb closes the terminal
        // on the phone, which ends it there.
        IAdbRunning? getevent;
        lock (_gate)
        {
            getevent = _getevent;
            _getevent = null;
        }

        if (getevent is not null)
        {
            await getevent.StopAsync().ConfigureAwait(false);
        }

        _lines.Writer.TryComplete();
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
        var context = _context!;
        try
        {
            if (!await ReadDevicesAsync(context).ConfigureAwait(false) || Stopping())
            {
                return;
            }

            IAdbRunning getevent;
            try
            {
                getevent = _adb.Start(
                    ["-s", _serial, "shell", "-tt", "getevent", "-lt"],
                    line => _lines.Writer.TryWrite((line, context.Clock.GetElapsedNanoseconds())));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                Unavailable($"adb could not start getevent: {Sentence(ex.Message)}", connectionLost: false);
                return;
            }

            lock (_gate)
            {
                if (_stopping)
                {
                    _ = getevent.StopAsync().AsTask();
                    return;
                }

                _getevent = getevent;
            }

            _ = getevent.Exited.ContinueWith(_ => _lines.Writer.TryComplete(), TaskScheduler.Default);
            var ready = await WaitForClockAsync().ConfigureAwait(false);
            _clockless = !ready;
            _reading = true;
            var refused = false;
            await foreach (var (line, receivedAt) in _lines.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (!refused && line.Contains("Permission denied", StringComparison.Ordinal))
                {
                    refused = true;
                    Unavailable($"getevent was refused: {Sentence(line.Trim())}", connectionLost: false);
                }

                Handle(line, receivedAt, ready);
            }

            Flush(context.Clock.GetElapsedNanoseconds());
            if (!Stopping() && !refused)
            {
                var code = getevent.Exited.IsCompleted ? getevent.Exited.Result : (int?)null;
                Unavailable(
                    "getevent on the phone ended" + (code is { } exit ? $" with exit code {exit.ToString(CultureInfo.InvariantCulture)}" : "") +
                    ": the phone was unplugged, adb stopped, or the phone ended it.",
                    connectionLost: true);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Unavailable($"the phone's input could not be read: {Sentence(ex.Message)}", connectionLost: false);
        }
    }

    private async Task<bool> ReadDevicesAsync(CollectorInitializationContext context)
    {
        AdbResult listed;
        try
        {
            listed = await _adb.RunAsync(["-s", _serial, "shell", "getevent", "-lp"], CommandTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Unavailable($"adb could not list the phone's input devices: {Sentence(ex.Message)}", connectionLost: false);
            return false;
        }

        var lines = Lines(listed.Output + listed.Error);
        if (listed.ExitCode != 0 && lines.All(line => !line.StartsWith("add device", StringComparison.Ordinal)))
        {
            Unavailable($"adb could not list the phone's input devices: {Sentence(listed.Text)}", connectionLost: false);
            return false;
        }

        var devices = GeteventDevices.Parse(lines);
        foreach (var device in devices)
        {
            _names[device.Path] = device.Name;
        }

        var screen = GeteventDevices.TouchScreen(devices);
        int? width = null;
        int? height = null;
        try
        {
            var size = await _adb.RunAsync(["-s", _serial, "shell", "wm", "size"], CommandTimeout, CancellationToken.None).ConfigureAwait(false);
            if (PhysicalSize().Match(size.Output) is { Success: true } match)
            {
                width = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                height = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                _screenSize = (width.Value, height.Value);
            }
        }
        catch (IOException)
        {
            // The companion app's display size is used instead.
        }

        if (screen is not null)
        {
            var x = screen.Axes["ABS_MT_POSITION_X"];
            var y = screen.Axes["ABS_MT_POSITION_Y"];
            _touchScreen = screen;
            _xRange = new AndroidAxisRange(x.Minimum, x.Maximum);
            _yRange = new AndroidAxisRange(y.Minimum, y.Maximum);
            _tracker = new TouchTracker(screen.Axes.TryGetValue("ABS_MT_SLOT", out var slot) ? slot.Value : 0);
        }

        var at = context.Clock.GetElapsedNanoseconds();
        Emit(
            AndroidTouchRecords.RawChannel,
            ref _rawSequence,
            at,
            AndroidTouchRecords.DevicesEventType,
            new AndroidInputDevices(_serial, lines, screen?.Path, screen?.Name, _xRange, _yRange, width, height),
            qualityFlags: [AndroidScreenRecords.ReceiptTimeFlag]);
        if (screen is null)
        {
            // The keys are still read.
            Unavailable("no touch screen was found in what \"getevent -lp\" listed.", connectionLost: false);
        }

        return true;
    }

    private void Handle(string line, long receivedAt, bool ready)
    {
        var text = line.TrimEnd('\r', '\n');
        if (text.Trim().Length == 0)
        {
            return;
        }

        Interlocked.Increment(ref _lineCount);
        var input = GeteventEvent.TryParse(text);
        if (input is null)
        {
            // A device added or removed, its name, or an error.
            EmitLines(null, [text], null, receivedAt, ready);
            return;
        }

        var device = input.Device ?? "";
        if (!_pending.TryGetValue(device, out var group))
        {
            group = [];
            _pending[device] = group;
        }

        group.Add(text);
        if (_tracker is not null && _touchScreen is { } screen && device == screen.Path &&
            _tracker.Add(input) is { } pointers)
        {
            EmitTouch(screen.Path, input, pointers, receivedAt, ready);
        }

        if (input.Type == "EV_KEY" && input.Code.StartsWith("KEY_", StringComparison.Ordinal) &&
            input.Value is "DOWN" or "UP" or "REPEAT")
        {
            EmitKey(device, input, receivedAt, ready);
        }

        if (input.IsReport)
        {
            _pending.Remove(device);
            EmitLines(input.Device, group, input.PhoneMicroseconds, receivedAt, ready);
        }
    }

    private void Flush(long receivedAt)
    {
        foreach (var (device, group) in _pending.ToList())
        {
            var last = GeteventEvent.TryParse(group[^1]);
            EmitLines(device.Length == 0 ? null : device, group, last?.PhoneMicroseconds, receivedAt, !_clockless);
        }

        _pending.Clear();
    }

    private void EmitLines(string? device, List<string> lines, long? phoneMicroseconds, long receivedAt, bool ready)
    {
        var (time, mapping, flags) = Place(AndroidTouchRecords.RawChannel, phoneMicroseconds, receivedAt, ready, delay: false);
        Emit(
            AndroidTouchRecords.RawChannel,
            ref _rawSequence,
            time,
            AndroidTouchRecords.LinesEventType,
            new AndroidGeteventLines(device, [.. lines], phoneMicroseconds, receivedAt),
            native: phoneMicroseconds * 1000,
            uncertainty: mapping?.UncertaintyNanoseconds,
            clockMapping: mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            qualityFlags: flags);
    }

    private void EmitTouch(string device, GeteventEvent report, IReadOnlyList<TrackedPointer> tracked, long receivedAt, bool ready)
    {
        var phoneTime = report.PhoneNanoseconds;
        var display = _display?.DisplayAt(phoneTime);
        var rotation = display?.Rotation ?? 0;
        // wm size first, as the size the input reader scales touches to is
        // the display's, which wm size reports; the display mode's size
        // when wm size gave none. Both are upright sizes.
        var (width, height) = _screenSize ?? (display is { } shown ? (shown.ModeWidth, shown.ModeHeight) : (0, 0));
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var pointers = new List<AndroidTouchPointer>(tracked.Count);
        var shownWidth = width;
        var shownHeight = height;
        foreach (var pointer in tracked)
        {
            var (naturalX, naturalY) = TouchGeometry.Natural(pointer.X, pointer.Y, _xRange!, _yRange!, width, height);
            var (x, y, w, h) = TouchGeometry.Shown(naturalX, naturalY, width, height, rotation);
            shownWidth = w;
            shownHeight = h;
            pointers.Add(new AndroidTouchPointer(
                pointer.Slot,
                pointer.TrackingId,
                pointer.Action,
                pointer.X,
                pointer.Y,
                naturalX,
                naturalY,
                x,
                y,
                pointer.Pressure,
                pointer.TouchMajor,
                pointer.TouchMinor));
            if (pointer.Action == AndroidTouchRecords.DownAction)
            {
                Interlocked.Increment(ref _touches);
            }
        }

        Interlocked.Increment(ref _reports);
        var first = pointers[0];
        var fingers = pointers.Count > 1 ? $" with {pointers.Count.ToString(CultureInfo.InvariantCulture)} fingers" : "";
        _lastTouch = string.Create(
            CultureInfo.InvariantCulture,
            $"{first.Action} at x {Math.Round(first.X):0}, y {Math.Round(first.Y):0} of {shownWidth} by {shownHeight}{fingers}");
        var (time, mapping, flags) = Place(AndroidTouchRecords.TouchChannel, report.PhoneMicroseconds, receivedAt, ready, delay: true);
        if (display is null)
        {
            flags = [.. flags, AndroidTouchRecords.RotationUnknownFlag];
        }

        Emit(
            AndroidTouchRecords.TouchChannel,
            ref _touchSequence,
            time,
            AndroidTouchRecords.TouchEventType,
            new AndroidTouchReport(device, report.PhoneMicroseconds, receivedAt, rotation, shownWidth, shownHeight, pointers),
            native: phoneTime,
            uncertainty: mapping?.UncertaintyNanoseconds,
            clockMapping: mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            qualityFlags: flags);
    }

    private void EmitKey(string device, GeteventEvent input, long receivedAt, bool ready)
    {
        if (input.Value == "DOWN")
        {
            Interlocked.Increment(ref _keys);
        }

        var (time, mapping, flags) = Place(AndroidTouchRecords.KeyChannel, input.PhoneMicroseconds, receivedAt, ready, delay: false);
        Emit(
            AndroidTouchRecords.KeyChannel,
            ref _keySequence,
            time,
            AndroidTouchRecords.KeyEventType,
            new AndroidHardwareKey(
                device,
                _names.GetValueOrDefault(device),
                input.Code,
                input.Value.ToLowerInvariant(),
                input.PhoneMicroseconds,
                receivedAt),
            native: input.PhoneNanoseconds,
            uncertainty: mapping?.UncertaintyNanoseconds,
            clockMapping: mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            qualityFlags: flags);
    }

    /// <summary>
    /// The session time of a phone time, by the clock fit, or the time the
    /// line was read; held at the channel's last time so that its times do
    /// not go backward.
    /// </summary>
    private (long Time, PhoneTimeMapping? Mapping, string[] Flags) Place(
        string channel,
        long? phoneMicroseconds,
        long receivedAt,
        bool ready,
        bool delay)
    {
        var mapped = ready && phoneMicroseconds is { } micros ? _clock?.Map(micros * 1000) : null;
        long time;
        List<string> flags = [];
        if (mapped is null)
        {
            time = receivedAt;
            flags.Add(AndroidScreenRecords.ReceiptTimeFlag);
        }
        else
        {
            time = mapped.SessionNanoseconds;
            flags.Add(AndroidTouchRecords.PhoneInputTimeFlag);
            if (delay)
            {
                lock (_delays)
                {
                    _delays.Add(receivedAt - time);
                }
            }
        }

        var last = _lastTimes.GetValueOrDefault(channel, long.MinValue);
        if (time < last)
        {
            time = last;
            flags.Add(AndroidCompanionRecords.FitHeldFlag);
        }

        _lastTimes[channel] = time;
        return (time, mapped, [.. flags]);
    }

    private async Task<bool> WaitForClockAsync()
    {
        if (_clock is null)
        {
            return false;
        }

        var first = await Task.WhenAny(_clock.FirstSample, Task.Delay(ClockWait)).ConfigureAwait(false);
        return first == _clock.FirstSample && await _clock.FirstSample.ConfigureAwait(false);
    }

    private long? MedianDelay()
    {
        lock (_delays)
        {
            if (_delays.Count == 0)
            {
                return null;
            }

            var sorted = _delays.ToArray();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }
    }

    /// <summary>The text ending with a full stop, for a message that does not end with one.</summary>
    private static string Sentence(string text) =>
        text.Length == 0 || text[^1] is '.' or '!' or '?' ? text : text + ".";

    private static List<string> Lines(string text) =>
        [.. text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0)];

    private void Unavailable(string reason, bool connectionLost)
    {
        var text = connectionLost
            ? $"The phone's touches stopped being recorded during the recording. {reason} The recording goes on without them."
            : $"The phone's touches could not be recorded: {reason} The recording goes on without them.";
        var at = _context?.Clock.GetElapsedNanoseconds() ?? 0;
        var last = _lastTimes.GetValueOrDefault(AndroidTouchRecords.RawChannel, long.MinValue);
        if (at < last)
        {
            at = last;
        }

        _lastTimes[AndroidTouchRecords.RawChannel] = at;
        Emit(
            AndroidTouchRecords.RawChannel,
            ref _rawSequence,
            at,
            AndroidTouchRecords.UnavailableEventType,
            new AndroidInputUnavailable(_serial, text, connectionLost));

        // After the record, so that whoever sees the reason finds it.
        HealthState = CollectorHealthState.Degraded;
        _unavailable = text;
    }

    private void Emit<T>(
        string channel,
        ref ulong sequence,
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
            channel,
            Interlocked.Increment(ref sequence),
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

    [GeneratedRegex(@"Physical size:\s*(\d+)x(\d+)")]
    private static partial Regex PhysicalSize();
}
