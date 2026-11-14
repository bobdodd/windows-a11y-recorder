using System.Globalization;
using System.Text.RegularExpressions;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>The packages whose windows the recording has seen, in the order first seen.</summary>
public interface IPhonePackagesSeen
{
    IReadOnlyList<string> PackagesSeen { get; }
}

/// <summary>
/// Records the phone's display density and the versions and bundled-framework
/// markers of the apps seen
/// (android-checks.md, step 2), with adb, as
/// <see cref="AndroidDeviceRecords"/> describes: <c>wm density</c> at the
/// start and the stop, and <c>dumpsys package</c> at the stop for each
/// package the accessibility tree collector saw, then on-phone APK entry
/// listings for its framework metadata. It comes before that
/// collector in the list, and so stops after it, when every snapshot has
/// been read. A density or version that cannot be read does not stop the
/// recording: the collector records why, reports itself degraded, and the
/// recording goes on.
/// </summary>
public sealed partial class AndroidDeviceCollector : ICaptureCollector
{
    public const string Type = "android.device";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);

    private readonly IAdb _adb;
    private readonly string _serial;
    private readonly IPhonePackagesSeen _packages;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _closing = new();

    private CollectorInitializationContext? _context;
    private Task? _running;
    private long _lastTime = long.MinValue;
    private volatile string? _unavailable;
    private volatile string? _density;
    private volatile string? _versions;
    private ulong _sequence;
    private ulong _lifecycleSequence;

    /// <param name="packages">The accessibility tree collector, which notes the package of each window.</param>
    public AndroidDeviceCollector(IAdb adb, string serial, IPhonePackagesSeen packages)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(packages);
        _adb = adb;
        _serial = serial;
        _packages = packages;
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidDeviceCollector),
            typeof(AndroidDeviceCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidDeviceRecords.Channel],
            "adb-wm-density-and-dumpsys-package");
    }

    public CollectorDescriptor Descriptor { get; }

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;

    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public string? HealthReason => _unavailable;

    public string? Summary
    {
        get
        {
            if (_density is null && _versions is null)
            {
                return null;
            }

            var parts = new List<string>();
            if (_density is { } density)
            {
                parts.Add(density);
            }

            if (_versions is { } versions)
            {
                parts.Add(versions);
            }

            return "Phone: " + string.Join("; ", parts) + ".";
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
        var token = _closing.Token;
        _running = Task.Run(() => ReadDensityAsync(AndroidDeviceRecords.StartReason, token), CancellationToken.None);
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
        }

        // The start's read finishes first, so that the records are in order.
        if (_running is not null)
        {
            await _running.ConfigureAwait(false);
        }

        await ReadDensityAsync(AndroidDeviceRecords.StopReason, CancellationToken.None).ConfigureAwait(false);
        await ReadVersionsAsync(CancellationToken.None).ConfigureAwait(false);
        var closing = CollectorClosingTimestamp.Resolve(_context?.Clock, boundary);
        LifecycleState = CollectorLifecycleState.Stopped;
        EmitLifecycle("stopped", closing, boundary.Utc);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask DisposeAsync()
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

        _closing.Dispose();
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private long Now() => _context?.Clock.GetElapsedNanoseconds() ?? 0;

    private async Task ReadDensityAsync(string reason, CancellationToken cancellation)
    {
        var started = Now();
        AndroidDisplayDensity density;
        try
        {
            var result = await _adb.RunAsync(["-s", _serial, "shell", "wm", "density"], ReadTimeout, cancellation).ConfigureAwait(false);
            density = result.ExitCode != 0
                ? new AndroidDisplayDensity(_serial, reason, null, null, [], $"adb could not run wm density: {Sentence(result.Text.Length == 0 ? "no reason given" : result.Text)}", started, Now())
                : ParseDensity(result.Output) with { Serial = _serial, Reason = reason, ReadStartedAtNanoseconds = started, ReadEndedAtNanoseconds = Now() };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (IOException ex)
        {
            density = new AndroidDisplayDensity(_serial, reason, null, null, [], $"adb could not run wm density: {Sentence(ex.Message)}", started, Now());
        }

        var flags = Array.Empty<string>();
        Emit(Hold(started, ref flags), AndroidDeviceRecords.DensityEventType, density, flags);
        if (density.Problem is { } problem)
        {
            Unavailable($"the display density was not read at the {reason}: {problem}");
            return;
        }

        if (density.InUseDpi is { } inUse)
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"density {inUse} dpi");
            _density = reason == AndroidDeviceRecords.StartReason
                ? text + " at the start"
                : _density == text + " at the start"
                    ? text + " at the start and the stop"
                    : (_density is { } start ? start + ", " : string.Empty) + string.Create(CultureInfo.InvariantCulture, $"{inUse} dpi at the stop");
        }
    }

    private async Task ReadVersionsAsync(CancellationToken cancellation)
    {
        var started = Now();
        var apps = new List<AndroidAppVersion>();
        var problems = new List<string>();
        foreach (var package in _packages.PackagesSeen)
        {
            if (!PackageName().IsMatch(package))
            {
                problems.Add($"The package name \"{package}\" was not read, as it is not a package name.");
                continue;
            }

            try
            {
                var result = await _adb.RunAsync(["-s", _serial, "shell", "dumpsys", "package", package], ReadTimeout, cancellation).ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    problems.Add($"adb could not read the version of {package}: {Sentence(result.Text.Length == 0 ? "no reason given" : result.Text)}");
                    continue;
                }

                var app = ParseVersion(package, result.Output);
                var framework = app.Found
                    ? await AndroidFrameworkDetector.ReadAsync(_adb, _serial, package, cancellation).ConfigureAwait(false)
                    : AndroidFrameworkDetector.Unavailable("The app was not found at the stop.");
                apps.Add(app with { Framework = framework });
                if (app.Found && framework.Problem is { } problem)
                {
                    problems.Add($"The framework of {package} could not be fully checked: {problem}");
                }
            }
            catch (IOException ex)
            {
                problems.Add($"adb could not read the version of {package}: {Sentence(ex.Message)}");
            }
        }

        var versions = new AndroidAppVersions(_serial, apps, problems, started, Now());
        var flags = Array.Empty<string>();
        Emit(Hold(started, ref flags), AndroidDeviceRecords.AppVersionsEventType, versions, flags);
        var found = apps.Count(app => app.Found);
        _versions = string.Create(CultureInfo.InvariantCulture, $"{found} of {_packages.PackagesSeen.Count} {(_packages.PackagesSeen.Count == 1 ? "app's version" : "apps' versions")} read at the stop");
        if (problems.Count > 0)
        {
            Unavailable($"not every app's metadata was read at the stop: {string.Join(" ", problems)}");
        }
    }

    /// <summary>
    /// What <c>wm density</c> printed: "Physical density: N", and "Override
    /// density: N" when the density in use differs. The lines are kept as
    /// printed; a problem is given when no physical density was printed.
    /// </summary>
    public static AndroidDisplayDensity ParseDensity(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var lines = Lines(output).Where(line => line.Length > 0).ToArray();
        int? Number(string label)
        {
            var line = lines.FirstOrDefault(line => line.StartsWith(label, StringComparison.Ordinal));
            return line is not null && int.TryParse(line[label.Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : null;
        }

        var physical = Number("Physical density:");
        return new AndroidDisplayDensity(
            string.Empty,
            AndroidDeviceRecords.StartReason,
            physical,
            Number("Override density:"),
            lines,
            physical is null ? "wm density printed no physical density." : null,
            0,
            0);
    }

    /// <summary>
    /// One package's version from what <c>dumpsys package</c> printed: the
    /// block of the first "Package [name]" in the "Packages:" section, up to
    /// the next package or section. The lines kept are those read: the
    /// block's first line, and its versionCode, versionName, lastUpdateTime
    /// and installerPackageName lines.
    /// </summary>
    public static AndroidAppVersion ParseVersion(string package, string output)
    {
        ArgumentException.ThrowIfNullOrEmpty(package);
        ArgumentNullException.ThrowIfNull(output);
        var kept = new List<string>();
        string? versionName = null;
        long? versionCode = null;
        int? minSdk = null;
        int? targetSdk = null;
        string? lastUpdate = null;
        string? installer = null;
        var inPackages = false;
        var inBlock = false;
        foreach (var line in Lines(output))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // A section's heading starts its line and ends with a colon.
            if (!char.IsWhiteSpace(line[0]) && trimmed.EndsWith(':'))
            {
                if (inBlock)
                {
                    break;
                }

                inPackages = trimmed == "Packages:";
                continue;
            }

            if (!inPackages)
            {
                continue;
            }

            if (trimmed.StartsWith("Package [", StringComparison.Ordinal))
            {
                if (inBlock)
                {
                    break;
                }

                if (trimmed.StartsWith($"Package [{package}]", StringComparison.Ordinal))
                {
                    inBlock = true;
                    kept.Add(trimmed);
                }

                continue;
            }

            if (!inBlock)
            {
                continue;
            }

            if (trimmed.StartsWith("versionCode=", StringComparison.Ordinal))
            {
                kept.Add(trimmed);
                foreach (var part in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var equals = part.IndexOf('=', StringComparison.Ordinal);
                    if (equals <= 0)
                    {
                        continue;
                    }

                    var value = part[(equals + 1)..];
                    switch (part[..equals])
                    {
                        case "versionCode" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code):
                            versionCode = code;
                            break;
                        case "minSdk" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min):
                            minSdk = min;
                            break;
                        case "targetSdk" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target):
                            targetSdk = target;
                            break;
                    }
                }
            }
            else if (Value(trimmed, "versionName=") is { } name)
            {
                kept.Add(trimmed);
                versionName ??= name;
            }
            else if (Value(trimmed, "lastUpdateTime=") is { } updated)
            {
                kept.Add(trimmed);
                lastUpdate ??= updated;
            }
            else if (Value(trimmed, "installerPackageName=") is { } installedBy)
            {
                kept.Add(trimmed);
                installer ??= installedBy;
            }
        }

        return new AndroidAppVersion(package, inBlock, versionName, versionCode, minSdk, targetSdk, lastUpdate, installer, kept);
    }

    private static string? Value(string line, string label) =>
        line.StartsWith(label, StringComparison.Ordinal) ? line[label.Length..] : null;

    private static IEnumerable<string> Lines(string output) =>
        output.Split('\n').Select(line => line.TrimEnd('\r'));

    // Letters, digits and underscores in dot-separated parts, as Android
    // allows in a package name; checked before it is put in a shell command.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)*$")]
    private static partial Regex PackageName();

    /// <summary>The text ending with a full stop, for a message that does not end with one.</summary>
    private static string Sentence(string text) =>
        text.Length == 0 || text[^1] is '.' or '!' or '?' ? text : text + ".";

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

    private void Unavailable(string reason)
    {
        var text = $"The phone's density and app versions were not all recorded: {reason}";
        var flags = Array.Empty<string>();
        Emit(Hold(Now(), ref flags), AndroidDeviceRecords.UnavailableEventType, new AndroidDeviceUnavailable(_serial, text), flags);

        // After the record, so that whoever sees the reason finds it.
        HealthState = CollectorHealthState.Degraded;
        _unavailable = _unavailable is { } earlier ? earlier + " " + text : text;
    }

    private void Emit<T>(long monotonicNanoseconds, string eventType, T payload, string[] qualityFlags)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            AndroidDeviceRecords.Channel,
            Interlocked.Increment(ref _sequence),
            monotonicNanoseconds,
            eventType,
            payload,
            qualityFlags));
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
