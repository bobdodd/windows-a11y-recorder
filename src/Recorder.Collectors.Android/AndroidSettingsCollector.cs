using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Records the phone's accessibility-related settings
/// (android-implementation-plan.md, slice 8, step 8a): every setting in
/// <see cref="AndroidSettingsRecords.All"/> at the start and the stop, and
/// each change between.
///
/// At the start it asks the companion app, through the companion
/// collector's connection, to say each time one of the settings is written,
/// then reads them all with <c>adb shell settings list</c>, one table at a
/// time. On each notice it reads that setting with <c>settings get</c> and
/// records it when its value differs from the last one recorded, placed at
/// the notice's phone time through the companion collector's clock fit, and
/// otherwise at the time the recorder read the notice. A setting written
/// twice before its first read, as when it is turned on and quickly off
/// again, is read once with its last value and so is not recorded as
/// changed. At the stop it reads them all again, and records a setting that
/// differs as changed at the stop, with no time of its own: so a change
/// whose notice was missed, or one made while the companion app was not
/// connected, is still in the recording. A phone whose settings cannot be
/// read, or whose companion app cannot be reached, does not stop the
/// recording: the collector records why, reports itself degraded, and the
/// recording goes on.
/// </summary>
public sealed partial class AndroidSettingsCollector : ICaptureCollector
{
    public const string Type = "android.settings";

    /// <summary>How long the notices wait for the companion collector's first clock sample before placing them by their receipt.</summary>
    public static readonly TimeSpan ClockWait = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly IAdb _adb;
    private readonly string _serial;
    private readonly IPhoneSettingChanges _changes;
    private readonly IPhoneClock? _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, string?> _known = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _closing = new();

    private CollectorInitializationContext? _context;
    private Task? _running;
    private ChannelReader<ReceivedSettingChange>? _notices;
    private bool _clockReady;
    private long _lastTime = long.MinValue;
    private volatile string? _unavailable;
    private volatile string? _latest;
    private volatile bool _read;
    private volatile bool _readAtStop;
    private ulong _sequence;
    private ulong _lifecycleSequence;
    private int _startCount;
    private long _changeCount;
    private long _unchanged;
    private long _unreadable;
    private long _foundAtStop;
    private bool _stopping;

    /// <param name="changes">The companion collector, which asks its app for the notices.</param>
    /// <param name="clock">The companion collector's clock fit; null to place every change at its notice's receipt.</param>
    public AndroidSettingsCollector(IAdb adb, string serial, IPhoneSettingChanges changes, IPhoneClock? clock)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(changes);
        _adb = adb;
        _serial = serial;
        _changes = changes;
        _clock = clock;
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidSettingsCollector),
            typeof(AndroidSettingsCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidSettingsRecords.Channel],
            "adb-settings-and-companion-observer");
    }

    public CollectorDescriptor Descriptor { get; }

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;

    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public string? HealthReason => _unavailable;

    public string? Summary
    {
        get
        {
            if (!_read)
            {
                return null;
            }

            int count;
            lock (_known)
            {
                count = _startCount;
            }

            var changes = Interlocked.Read(ref _changeCount);
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"Phone settings: {count} read at the start, {changes} {(changes == 1 ? "change" : "changes")}");
            if (_latest is { } latest)
            {
                text += ", latest " + latest;
            }

            if (Interlocked.Read(ref _unchanged) is var unchanged and > 0)
            {
                text += string.Create(
                    CultureInfo.InvariantCulture,
                    $", {unchanged} {(unchanged == 1 ? "notice" : "notices")} with no change");
            }

            if (Interlocked.Read(ref _unreadable) is var unreadable and > 0)
            {
                text += string.Create(
                    CultureInfo.InvariantCulture,
                    $", {unreadable} {(unreadable == 1 ? "read" : "reads")} failed");
            }

            if (_readAtStop)
            {
                var found = Interlocked.Read(ref _foundAtStop);
                text += found == 0
                    ? ", read again at the stop"
                    : string.Create(CultureInfo.InvariantCulture, $", read again at the stop, which found {found} more");
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
            _lastTime = boundary.MonotonicNanoseconds;
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

        // The notices that arrived before the stop are read first, so that
        // a change made just before it keeps its own time.
        if (_notices is { } notices)
        {
            while (notices.TryRead(out var received))
            {
                await HandleAsync(received, CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (_read)
        {
            await ReadAtStopAsync().ConfigureAwait(false);
        }

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

    private long Now() => _context?.Clock.GetElapsedNanoseconds() ?? 0;

    private async Task RunAsync()
    {
        var token = _closing.Token;
        try
        {
            // Asked for before the first read, so that a change made while
            // it runs has a notice.
            var notices = await _changes.RequestSettingChangesAsync(AndroidSettingsRecords.All, token).ConfigureAwait(false);
            _notices = notices;
            var start = await ReadAllAsync(token).ConfigureAwait(false);
            if (start.Settings.Count == 0)
            {
                Unavailable(
                    $"the phone's settings could not be read: {string.Join(" ", start.Problems)} Nothing is recorded of them.",
                    connectionLost: false);
                return;
            }

            lock (_known)
            {
                foreach (var (id, value) in start.Settings)
                {
                    _known[id] = value;
                }

                _startCount = start.Settings.Count;
            }

            EmitSnapshot(AndroidSettingsRecords.StartReason, start);
            _read = true;
            if (start.Problems.Count > 0)
            {
                Unavailable(
                    $"some of the phone's settings could not be read at the start: {string.Join(" ", start.Problems)}",
                    connectionLost: false);
            }

            if (notices is null)
            {
                if (!Stopping())
                {
                    Unavailable(
                        "the companion app is not connected, so changes during the recording are not timed. " +
                        "The settings are read again at the stop, and any that changed are recorded then. The android.companion line says why.",
                        connectionLost: false);
                }

                return;
            }

            _clockReady = await WaitForClockAsync(token).ConfigureAwait(false);
            await foreach (var received in notices.ReadAllAsync(token).ConfigureAwait(false))
            {
                await HandleAsync(received, token).ConfigureAwait(false);
            }

            if (!Stopping())
            {
                Unavailable(
                    "the connection to the companion app ended, so later changes are not timed: the phone was unplugged, adb stopped, " +
                    "or the companion app was turned off. The settings are read again at the stop.",
                    connectionLost: true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopping: StopAsync reads the notices left, then the settings.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Unavailable($"they could not be recorded: {Sentence(ex.Message)}", connectionLost: false);
        }
    }

    private async Task HandleAsync(ReceivedSettingChange received, CancellationToken cancellation)
    {
        var notice = received.Change;
        if (AndroidSettingsRecords.Find(notice.Namespace, notice.Name) is not { } setting)
        {
            // The app was asked only about these; a newer one may say more.
            return;
        }

        string? value;
        try
        {
            value = await GetAsync(setting, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException ex)
        {
            Interlocked.Increment(ref _unreadable);
            var flags = Array.Empty<string>();
            Emit(
                Hold(Now(), ref flags),
                AndroidSettingsRecords.UnavailableEventType,
                new AndroidSettingsUnavailable(_serial, $"{setting.Label} ({setting.Id}) changed, but could not be read: {Sentence(ex.Message)}", false),
                qualityFlags: flags);
            return;
        }

        var readAt = Now();
        string? previous;
        lock (_known)
        {
            if (_known.TryGetValue(setting.Id, out previous) && previous == value)
            {
                Interlocked.Increment(ref _unchanged);
                return;
            }

            _known[setting.Id] = value;
        }

        var mapping = _clockReady ? _clock?.Map(notice.Time) : null;
        string[] timeFlags = mapping is null ? [AndroidScreenRecords.ReceiptTimeFlag] : [AndroidSettingsRecords.PhoneNoticeTimeFlag];
        var time = mapping?.SessionNanoseconds ?? received.ReceivedAt;
        Interlocked.Increment(ref _changeCount);
        _latest = $"{setting.Label} {AndroidSettingValues.Describe(setting, value)}";
        Emit(
            Hold(time, ref timeFlags),
            AndroidSettingsRecords.ChangeEventType,
            new AndroidSettingChanged(
                _serial,
                setting.Id,
                value,
                previous,
                AndroidSettingsRecords.NoticeSource,
                notice.Time,
                received.ReceivedAt,
                readAt),
            notice.Time,
            mapping?.UncertaintyNanoseconds,
            mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            timeFlags);
    }

    private async Task ReadAtStopAsync()
    {
        AndroidSettingsSnapshot stop;
        try
        {
            stop = await ReadAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Unavailable($"the phone's settings could not be read again at the stop: {Sentence(ex.Message)}", connectionLost: false);
            return;
        }

        var at = stop.ReadStartedAtNanoseconds;
        foreach (var setting in AndroidSettingsRecords.All)
        {
            if (!stop.Settings.TryGetValue(setting.Id, out var value))
            {
                continue;
            }

            string? previous;
            lock (_known)
            {
                if (!_known.TryGetValue(setting.Id, out previous) || previous == value)
                {
                    continue;
                }

                _known[setting.Id] = value;
            }

            Interlocked.Increment(ref _changeCount);
            Interlocked.Increment(ref _foundAtStop);
            _latest = $"{setting.Label} {AndroidSettingValues.Describe(setting, value)}";
            string[] flags = [AndroidSettingsRecords.FoundAtStopFlag];
            Emit(
                Hold(at, ref flags),
                AndroidSettingsRecords.ChangeEventType,
                new AndroidSettingChanged(
                    _serial,
                    setting.Id,
                    value,
                    previous,
                    AndroidSettingsRecords.StopSource,
                    null,
                    null,
                    stop.ReadEndedAtNanoseconds),
                qualityFlags: flags);
        }

        if (stop.Settings.Count > 0)
        {
            EmitSnapshot(AndroidSettingsRecords.StopReason, stop);
        }

        if (stop.Problems.Count > 0)
        {
            Unavailable(
                $"some of the phone's settings could not be read again at the stop: {string.Join(" ", stop.Problems)}",
                connectionLost: false);
        }

        _readAtStop = true;
    }

    private void EmitSnapshot(string reason, AndroidSettingsSnapshot snapshot)
    {
        var flags = Array.Empty<string>();
        Emit(
            Hold(snapshot.ReadStartedAtNanoseconds, ref flags),
            AndroidSettingsRecords.SnapshotEventType,
            snapshot with { Reason = reason },
            qualityFlags: flags);
    }

    /// <summary>
    /// Reads every recorded setting, a table at a time. A table that cannot
    /// be read is told in the snapshot's problems, and its settings are
    /// missing from it.
    /// </summary>
    private async Task<AndroidSettingsSnapshot> ReadAllAsync(CancellationToken cancellation)
    {
        var started = Now();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var table in AndroidSettingsRecords.Namespaces)
        {
            AdbResult result;
            try
            {
                result = await _adb.RunAsync(
                    ["-s", _serial, "shell", "settings", "list", table],
                    ReadTimeout,
                    cancellation).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                problems.Add($"adb could not read the {table} settings: {Sentence(ex.Message)}");
                continue;
            }

            if (result.ExitCode != 0)
            {
                problems.Add($"adb could not read the {table} settings: {Sentence(result.Text.Length == 0 ? "no reason given" : result.Text)}");
                continue;
            }

            var listed = ParseList(result.Output);
            foreach (var setting in AndroidSettingsRecords.All.Where(setting => setting.Namespace == table))
            {
                settings[setting.Id] = listed.TryGetValue(setting.Name, out var value) ? value : null;
            }
        }

        return new AndroidSettingsSnapshot(
            _serial,
            AndroidSettingsRecords.StartReason,
            settings,
            problems,
            started,
            Now());
    }

    private async Task<string?> GetAsync(AndroidSetting setting, CancellationToken cancellation)
    {
        var result = await _adb.RunAsync(
            ["-s", _serial, "shell", "settings", "get", setting.Namespace, setting.Name],
            ReadTimeout,
            cancellation).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new IOException(result.Text.Length == 0 ? "adb gave no reason" : result.Text);
        }

        return ValueOf(result.Output.TrimEnd('\r', '\n'));
    }

    /// <summary>
    /// The settings <c>settings list</c> printed, one name=value a line; a
    /// line without a name, as in a value with a line break, continues the
    /// value before it. The command prints "null" for a setting with no value.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ParseList(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? last = null;
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
        {
            var line = lines[i];
            var match = ListLine().Match(line);
            if (match.Success)
            {
                last = match.Groups[1].Value;
                values[last] = match.Groups[2].Value;
            }
            else if (last is not null)
            {
                values[last] += "\n" + line;
            }
        }

        foreach (var name in values.Keys.ToArray())
        {
            values[name] = ValueOf(values[name]!);
        }

        return values;
    }

    private static string? ValueOf(string printed) => printed == "null" ? null : printed;

    [GeneratedRegex(@"^([A-Za-z0-9_.:\-]+)=(.*)$")]
    private static partial Regex ListLine();

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

        var first = await Task.WhenAny(_clock.FirstSample, Task.Delay(ClockWait, cancellation)).ConfigureAwait(false);
        return first == _clock.FirstSample && await _clock.FirstSample.ConfigureAwait(false);
    }

    /// <summary>The text ending with a full stop, for a message that does not end with one.</summary>
    private static string Sentence(string text) =>
        text.Length == 0 || text[^1] is '.' or '!' or '?' ? text : text + ".";

    private void Unavailable(string reason, bool connectionLost)
    {
        var text = connectionLost
            ? $"The phone's settings stopped being recorded during the recording: {reason}"
            : $"The phone's settings were not all recorded: {reason}";
        var flags = Array.Empty<string>();
        Emit(
            Hold(Now(), ref flags),
            AndroidSettingsRecords.UnavailableEventType,
            new AndroidSettingsUnavailable(_serial, text, connectionLost),
            qualityFlags: flags);

        // After the record, so that whoever sees the reason finds it.
        HealthState = CollectorHealthState.Degraded;
        _unavailable = _unavailable is { } earlier ? earlier + " " + text : text;
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
            AndroidSettingsRecords.Channel,
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
