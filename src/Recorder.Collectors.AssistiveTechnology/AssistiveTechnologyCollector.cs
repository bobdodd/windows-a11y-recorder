using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Recorder.Contracts;

namespace Recorder.Collectors.AssistiveTechnology;

/// <summary>
/// Records when a known assistive technology, NVDA first, is running, has
/// reached the instrumented Chromium, and is making sound, on
/// <c>system.assistive-technology</c>, without loading anything into it or
/// changing its settings:
/// <list type="bullet">
/// <item>Its processes, from the process list every 250 ms: a main process by
/// its executable's name, and a helper, a child of a main process started
/// from that process's folder. Each process's start and exit times are
/// Windows' own, from GetProcessTimes, so the interval does not blur them.</item>
/// <item>Its modules in the instrumented Chromium's processes, from each
/// process's module list every second while it runs or a module is
/// seen.</item>
/// <item>Its sound, from the peak meter of each of its processes' audio
/// sessions on every active output device, every 20 ms. The meter is the
/// process's own stream, before the system mixes it, so the microphone and
/// other programs' sound do not reach it.</item>
/// </list>
/// Agreed with the owner on 2026-10-10. See
/// docs/architecture/screen-reader-activity.md, "Tracking NVDA".
/// </summary>
public sealed class AssistiveTechnologyCollector : ICaptureCollector
{
    private readonly object _gate = new();
    private readonly string? _browserExecutablePath;
    private readonly ManualResetEventSlim _stop = new(false);
    private Thread? _thread;
    private TaskCompletionSource<bool>? _ready;
    private CollectorInitializationContext? _context;
    private long _eventSequence = -1;
    private long _lifecycleSequence = -1;
    private bool _disposed;

    // Owned by the collector thread.
    private readonly Dictionary<(int ProcessId, long CreationTime), WatchedProcess> _processes = [];
    private readonly Dictionary<(int ProcessId, long CreationTime), BrowserHost> _hosts = [];
    private readonly Dictionary<(int ProcessId, long CreationTime), string> _otherChromePaths = [];
    private readonly HashSet<(int, long)> _passed = [];
    private readonly List<(int ProcessId, AudioSessionControl Session)> _sessions = [];
    private MMDeviceEnumerator? _devices;
    private string? _soundProblem;

    // Whether a running main process has no audio session listed yet. Its
    // first sound opens one, so the sessions are listed every 100 ms until
    // it has one, and the start of that sound is not missed; then every
    // second. Found in the first target-machine recording of 2026-10-10,
    // where NVDA's startup sound began 0.7 s before its session was listed.
    private bool _awaitingSession;

    /// <param name="browserExecutablePath">
    /// The instrumented Chromium's executable, whose processes' modules are
    /// read, or null when the recording has no browser.
    /// </param>
    public AssistiveTechnologyCollector(string? browserExecutablePath)
    {
        _browserExecutablePath = browserExecutablePath is null ? null : Path.GetFullPath(browserExecutablePath);
        Descriptor = CollectorDescriptor.Create(
            "windows.assistive-technology",
            nameof(AssistiveTechnologyCollector),
            typeof(AssistiveTechnologyCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AssistiveTechnologyRecords.Channel],
            "toolhelp-processes+toolhelp-modules+audio-session-meters");
    }

    public CollectorDescriptor Descriptor { get; }

    /// <summary>
    /// Whether a watched product's main process is running, raised on the
    /// collector's thread when it changes, with true also for each further
    /// main process seen to start; for the keyboard hook collector, which
    /// keeps its hook first in the chain while a screen reader runs. The
    /// second value says a main process started during the recording.
    /// </summary>
    public event Action<bool, bool>? ScreenReaderRunningChanged;

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;
    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;
    public string? HealthReason { get; private set; }

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

    public async ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready)
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Starting;
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "Assistive technology"
            };
            // Core Audio's objects are used from one multithreaded apartment.
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        try
        {
            await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
            HealthReason = ex.Message;
            return CollectorTransitionResult.Reject(
                LifecycleState,
                "assistive-technology-start-failed",
                ex.Message);
        }

        LifecycleState = CollectorLifecycleState.Running;
        EmitLifecycle("started", boundary);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
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

        _stop.Set();
        if (_thread is { } thread)
        {
            await Task.Run(() => thread.Join(TimeSpan.FromSeconds(5)), cancellationToken).ConfigureAwait(false);
        }

        LifecycleState = CollectorLifecycleState.Stopped;
        EmitLifecycle("stopped", boundary);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (LifecycleState is CollectorLifecycleState.Running or CollectorLifecycleState.Failed)
        {
            var now = _context?.Clock.GetElapsedNanoseconds() ?? 0;
            await StopAsync(new SessionBoundary(now, DateTimeOffset.UtcNow), CancellationToken.None)
                .ConfigureAwait(false);
        }

        _disposed = true;
        _stop.Dispose();
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    // --- The collector thread -------------------------------------------------

    private void Loop()
    {
        try
        {
            try
            {
                _devices = new MMDeviceEnumerator();
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                _soundProblem = $"The audio devices could not be listed: {ex.Message}";
            }

            EmitWatch();
            ScanProcesses(atStart: true);
            ScanModules();
            RefreshSessions();
            _ready?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _ready?.TrySetException(ex);
            return;
        }

        var processDue = Now();
        var moduleDue = processDue;
        var sessionsListedAt = processDue;
        var processInterval = Milliseconds(AssistiveTechnologyRecords.ProcessPollMilliseconds);
        var moduleInterval = Milliseconds(AssistiveTechnologyRecords.ModulePollMilliseconds);
        var newSessionInterval = Milliseconds(AssistiveTechnologyRecords.NewSessionListMilliseconds);
        while (!_stop.Wait(AssistiveTechnologyRecords.SoundSampleMilliseconds))
        {
            try
            {
                var now = Now();
                SampleSound(now);
                if (now >= processDue)
                {
                    processDue = now + processInterval;
                    ScanProcesses(atStart: false);
                }

                if (now >= moduleDue)
                {
                    moduleDue = now + moduleInterval;
                    ScanModules();
                }

                if (now - sessionsListedAt >= (_awaitingSession ? newSessionInterval : moduleInterval))
                {
                    RefreshSessions();
                    sessionsListedAt = now;
                }
            }
            catch (Exception ex) when (ex is COMException or Win32Exception or InvalidOperationException)
            {
                HealthState = CollectorHealthState.Degraded;
                HealthReason = ex.Message;
            }
        }

        // A period of sound in progress ends with the recording.
        var stoppedAt = Now();
        foreach (var process in _processes.Values)
        {
            EmitSoundEnd(process, process.Sound.End(stoppedAt, "stop"));
        }

        foreach (var (_, session) in _sessions)
        {
            session.Dispose();
        }

        _sessions.Clear();
        foreach (var process in _processes.Values)
        {
            process.Handle.Dispose();
        }

        _devices?.Dispose();
    }

    private void EmitWatch()
    {
        Emit(AssistiveTechnologyRecords.WatchEventType, new
        {
            products = AssistiveTechnologyRecords.Known.Select(product => product.Product).ToArray(),
            executables = AssistiveTechnologyRecords.Known.SelectMany(product => product.Executables).ToArray(),
            modules = AssistiveTechnologyRecords.Known.SelectMany(product => product.Modules).ToArray(),
            processPollMilliseconds = AssistiveTechnologyRecords.ProcessPollMilliseconds,
            modulePollMilliseconds = AssistiveTechnologyRecords.ModulePollMilliseconds,
            soundSampleMilliseconds = AssistiveTechnologyRecords.SoundSampleMilliseconds,
            soundThreshold = AssistiveTechnologyRecords.SoundThreshold,
            soundGapMilliseconds = AssistiveTechnologyRecords.SoundGapMilliseconds,
            browserExecutablePath = _browserExecutablePath,
            soundProblem = _soundProblem
        }, Now());
    }

    // --- Processes ------------------------------------------------------------

    private sealed class WatchedProcess(
        KnownAssistiveTechnology product,
        string role,
        int processId,
        long creationTime,
        string? executablePath,
        SafeProcessHandle handle,
        SoundPeriodTracker sound)
    {
        public KnownAssistiveTechnology Product { get; } = product;
        public string Role { get; } = role;
        public int ProcessId { get; } = processId;
        public long CreationTime { get; } = creationTime;
        public string? ExecutablePath { get; } = executablePath;
        public SafeProcessHandle Handle { get; } = handle;
        public SoundPeriodTracker Sound { get; } = sound;
    }

    private sealed record BrowserHost(int ProcessId, string ExecutablePath, SafeProcessHandle Handle)
    {
        public Dictionary<string, (KnownAssistiveTechnology Product, string Path, string? FileVersion)> Modules { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private void ScanProcesses(bool atStart)
    {
        var entries = ProcessEntries();

        // Exits first, so a process ID Windows reuses is a new process.
        foreach (var (key, process) in _processes.ToArray())
        {
            if (WaitForSingleObject(process.Handle, 0) != WaitObject0)
            {
                continue;
            }

            _processes.Remove(key);
            var exitedAt = Now();
            EmitSoundEnd(process, process.Sound.End(exitedAt, "process-exited"));
            var times = Times(process.Handle);
            Emit(AssistiveTechnologyRecords.ProcessExitedEventType, new
            {
                product = process.Product.Product,
                role = process.Role,
                processId = process.ProcessId,
                startedUtc = Utc(process.CreationTime),
                exitedUtc = times is { } read ? Utc(read.Exit) : null,
                exitCode = GetExitCodeProcess(process.Handle, out var code) ? (long?)code : null
            }, exitedAt);
            process.Handle.Dispose();
            if (process.Role == AssistiveTechnologyRecords.ScreenReaderRole &&
                !_processes.Values.Any(other => other.Role == AssistiveTechnologyRecords.ScreenReaderRole))
            {
                ScreenReaderRunningChanged?.Invoke(false, false);
            }
        }

        foreach (var entry in entries)
        {
            var main = AssistiveTechnologyRecords.ByExecutable(entry.ExeFile);
            var parent = main is null
                ? _processes.Values.FirstOrDefault(process =>
                    process.ProcessId == entry.ParentProcessId && process.Role == AssistiveTechnologyRecords.ScreenReaderRole)
                : null;
            if (main is null && parent is null)
            {
                continue;
            }

            var handle = OpenProcess(ProcessQueryLimitedInformation | Synchronize, false, entry.ProcessId);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                continue;
            }

            var times = Times(handle);
            var creation = times?.Creation ?? 0;
            var key = (entry.ProcessId, creation);
            if (_processes.ContainsKey(key) || _passed.Contains(key))
            {
                handle.Dispose();
                continue;
            }

            var path = ImagePath(handle);
            if (parent is not null &&
                (path is null || parent.ExecutablePath is null ||
                 !AssistiveTechnologyRecords.IsInFolderOf(path, parent.ExecutablePath) ||
                 creation < parent.CreationTime))
            {
                // A program the screen reader started, not a part of it.
                _passed.Add(key);
                handle.Dispose();
                continue;
            }

            var product = main ?? parent!.Product;
            var role = main is not null ? AssistiveTechnologyRecords.ScreenReaderRole : AssistiveTechnologyRecords.HelperRole;
            var process = new WatchedProcess(
                product,
                role,
                entry.ProcessId,
                creation,
                path,
                handle,
                new SoundPeriodTracker(
                    AssistiveTechnologyRecords.SoundThreshold,
                    Milliseconds(AssistiveTechnologyRecords.SoundGapMilliseconds)));
            _processes[key] = process;
            if (main is not null)
            {
                // Listed at once, and often until it has a session.
                _awaitingSession = true;
                ScreenReaderRunningChanged?.Invoke(true, !atStart);
            }
            var version = path is null ? null : FileVersion(path);
            Emit(AssistiveTechnologyRecords.ProcessStartedEventType, new
            {
                product = product.Product,
                role,
                basis = main is not null
                    ? AssistiveTechnologyRecords.KnownExecutableBasis
                    : AssistiveTechnologyRecords.ChildInFolderBasis,
                executablePath = path,
                fileVersion = AssistiveTechnologyRecords.VersionOrNull(version?.FileVersion),
                productVersion = AssistiveTechnologyRecords.VersionOrNull(version?.ProductVersion),
                processId = entry.ProcessId,
                // As the process list gives it: the parent may have exited.
                parentProcessId = entry.ParentProcessId == 0 ? null : (long?)entry.ParentProcessId,
                startedUtc = times is null ? null : Utc(creation),
                runningAtStart = atStart,
                copy = path is null ? "unknown" : AssistiveTechnologyRecords.CopyOf(path, InstalledNvdaFolder()),
                problem = path is null ? "The process's executable path could not be read." : null
            }, Now());
        }

        // The browser's processes, matched by their executable's full path.
        if (_browserExecutablePath is null)
        {
            return;
        }

        var live = new HashSet<(int, long)>();
        foreach (var entry in entries.Where(entry =>
                     string.Equals(entry.ExeFile, Path.GetFileName(_browserExecutablePath), StringComparison.OrdinalIgnoreCase)))
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation | ProcessQueryInformation | ProcessVmRead | Synchronize, false, entry.ProcessId);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                continue;
            }

            var key = (entry.ProcessId, Times(handle)?.Creation ?? 0);
            live.Add(key);
            if (_hosts.ContainsKey(key) || _otherChromePaths.ContainsKey(key))
            {
                handle.Dispose();
                continue;
            }

            var path = ImagePath(handle);
            if (path is null || !string.Equals(path, _browserExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                _otherChromePaths[key] = path ?? string.Empty;
                handle.Dispose();
                continue;
            }

            _hosts[key] = new BrowserHost(entry.ProcessId, path, handle);
        }

        foreach (var key in _otherChromePaths.Keys.Where(key => !live.Contains(key)).ToArray())
        {
            _otherChromePaths.Remove(key);
        }

        foreach (var (key, host) in _hosts.ToArray())
        {
            if (live.Contains(key) && WaitForSingleObject(host.Handle, 0) != WaitObject0)
            {
                continue;
            }

            foreach (var (name, module) in host.Modules)
            {
                EmitModule(AssistiveTechnologyRecords.ModuleUnloadedEventType, host, name, module, hostExited: true);
            }

            _hosts.Remove(key);
            host.Handle.Dispose();
        }
    }

    // --- Modules --------------------------------------------------------------

    private void ScanModules()
    {
        var running = _processes.Values.Any(process => process.Role == AssistiveTechnologyRecords.ScreenReaderRole);
        foreach (var host in _hosts.Values)
        {
            if (!running && host.Modules.Count == 0)
            {
                continue;
            }

            if (Modules(host.ProcessId) is not { } modules)
            {
                continue;
            }

            var seen = new Dictionary<string, (KnownAssistiveTechnology Product, string Path, string? FileVersion)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, path) in modules)
            {
                if (AssistiveTechnologyRecords.ByModule(name) is { } product)
                {
                    seen[name] = (product, path, host.Modules.TryGetValue(name, out var known) && known.Path == path
                        ? known.FileVersion
                        : AssistiveTechnologyRecords.VersionOrNull(FileVersion(path)?.FileVersion));
                }
            }

            foreach (var (name, module) in seen)
            {
                if (!host.Modules.ContainsKey(name))
                {
                    host.Modules[name] = module;
                    EmitModule(AssistiveTechnologyRecords.ModuleLoadedEventType, host, name, module, hostExited: false);
                }
            }

            foreach (var (name, module) in host.Modules.ToArray())
            {
                if (!seen.ContainsKey(name))
                {
                    host.Modules.Remove(name);
                    EmitModule(AssistiveTechnologyRecords.ModuleUnloadedEventType, host, name, module, hostExited: false);
                }
            }
        }
    }

    private void EmitModule(
        string eventType,
        BrowserHost host,
        string name,
        (KnownAssistiveTechnology Product, string Path, string? FileVersion) module,
        bool hostExited) =>
        Emit(eventType, new
        {
            product = module.Product.Product,
            moduleName = name.ToLowerInvariant(),
            modulePath = module.Path,
            fileVersion = module.FileVersion,
            hostProcessId = host.ProcessId,
            hostExecutablePath = host.ExecutablePath,
            hostExited
        }, Now());

    // --- Sound ----------------------------------------------------------------

    // The audio sessions of the watched processes on every active output
    // device, listed again each second, as a process opens its stream when
    // it first plays and a device can be added.
    private void RefreshSessions()
    {
        foreach (var (_, session) in _sessions)
        {
            session.Dispose();
        }

        _sessions.Clear();
        _awaitingSession = false;
        if (_devices is null || _processes.Count == 0)
        {
            return;
        }

        var watched = _processes.Values.Select(process => process.ProcessId).ToHashSet();
        try
        {
            foreach (var device in _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        var processId = (int)session.GetProcessID;
                        if (watched.Contains(processId))
                        {
                            _sessions.Add((processId, session));
                        }
                        else
                        {
                            session.Dispose();
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            HealthState = CollectorHealthState.Degraded;
            HealthReason = $"The audio sessions could not be read: {ex.Message}";
        }

        var listed = _sessions.Select(session => session.ProcessId).ToHashSet();
        _awaitingSession = _processes.Values.Any(process =>
            process.Role == AssistiveTechnologyRecords.ScreenReaderRole && !listed.Contains(process.ProcessId));
    }

    private void SampleSound(long now)
    {
        if (_processes.Count == 0)
        {
            return;
        }

        var peaks = new Dictionary<int, double>();
        foreach (var (processId, session) in _sessions)
        {
            try
            {
                var peak = session.AudioMeterInformation.MasterPeakValue;
                peaks[processId] = Math.Max(peaks.GetValueOrDefault(processId), peak);
            }
            catch (COMException)
            {
                // The session ended; the next listing drops it.
            }
        }

        foreach (var process in _processes.Values)
        {
            var change = process.Sound.Observe(now, peaks.GetValueOrDefault(process.ProcessId));
            if (change is { Started: true })
            {
                Emit(AssistiveTechnologyRecords.SoundStartedEventType, new
                {
                    product = process.Product.Product,
                    processId = process.ProcessId,
                    peak = Math.Round(change.Peak, 6)
                }, change.At);
            }
            else
            {
                EmitSoundEnd(process, change);
            }
        }
    }

    private void EmitSoundEnd(WatchedProcess process, SoundPeriodChange? change)
    {
        if (change is not { Started: false })
        {
            return;
        }

        Emit(AssistiveTechnologyRecords.SoundEndedEventType, new
        {
            product = process.Product.Product,
            processId = process.ProcessId,
            startedAt = change.StartedAt,
            lastSoundAt = change.LastSoundAt,
            maxPeak = Math.Round(change.Peak, 6),
            endedBy = change.EndedBy
        }, change.At);
    }

    // --- Reading ----------------------------------------------------------------

    // NVDA's installed copy's folder, as config.isInstalledCopy reads it,
    // in both registry views, or null.
    private static string? InstalledNvdaFolder()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NVDA");
                if (key?.GetValue("UninstallDirectory") is string folder && folder.Length > 0)
                {
                    return folder;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Read as not installed, as NVDA does.
            }
        }

        return null;
    }

    private static FileVersionInfo? FileVersion(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? ImagePath(SafeProcessHandle handle)
    {
        var buffer = new char[1024];
        var size = buffer.Length;
        return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
    }

    private static (long Creation, long Exit)? Times(SafeProcessHandle handle) =>
        GetProcessTimes(handle, out var creation, out var exit, out _, out _) ? (creation, exit) : null;

    private static string Utc(long fileTime) =>
        DateTimeOffset.FromFileTime(fileTime).ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private readonly record struct ProcessEntry(int ProcessId, int ParentProcessId, string ExeFile);

    private static List<ProcessEntry> ProcessEntries()
    {
        var entries = new List<ProcessEntry>();
        using var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The process list could not be read.");
        }

        var entry = new ProcessEntry32W { Size = (uint)Marshal.SizeOf<ProcessEntry32W>() };
        for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
        {
            entries.Add(new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId, entry.ExeFile));
        }

        return entries;
    }

    // A process's modules, by file name and path, or null when they could
    // not be read.
    private static List<(string Name, string Path)>? Modules(int processId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var snapshot = CreateToolhelp32Snapshot(Th32csSnapModule | Th32csSnapModule32, (uint)processId);
            if (snapshot.IsInvalid)
            {
                // ERROR_BAD_LENGTH: the module list changed while it was read.
                if (Marshal.GetLastWin32Error() == 24)
                {
                    continue;
                }

                return null;
            }

            var modules = new List<(string, string)>();
            var entry = new ModuleEntry32W { Size = (uint)Marshal.SizeOf<ModuleEntry32W>() };
            for (var more = Module32FirstW(snapshot, ref entry); more; more = Module32NextW(snapshot, ref entry))
            {
                modules.Add((entry.Module, entry.ExePath));
            }

            return modules;
        }

        return null;
    }

    // --- Events -----------------------------------------------------------------

    private long Now() => _context?.Clock.GetElapsedNanoseconds() ?? 0;

    private static long Milliseconds(int milliseconds) => milliseconds * 1_000_000L;

    private void Emit(string eventType, object payload, long monotonicNanoseconds)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var sequence = unchecked((ulong)Interlocked.Increment(ref _eventSequence));
        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            AssistiveTechnologyRecords.Channel,
            sequence,
            monotonicNanoseconds,
            eventType,
            payload));
    }

    private void EmitLifecycle(string action, SessionBoundary boundary)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var sequence = unchecked((ulong)Interlocked.Increment(ref _lifecycleSequence));
        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "collector.lifecycle",
            sequence,
            boundary.MonotonicNanoseconds,
            "collector-lifecycle",
            new { action, state = LifecycleState.ToString(), boundary.Utc }));
    }

    // --- Native -----------------------------------------------------------------

    private const uint Th32csSnapProcess = 0x00000002;
    private const uint Th32csSnapModule = 0x00000008;
    private const uint Th32csSnapModule32 = 0x00000010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitObject0 = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry32W
    {
        public uint Size;
        public uint ModuleId;
        public uint ProcessId;
        public uint GlobalUsage;
        public uint ProcessUsage;
        public nint BaseAddress;
        public uint BaseSize;
        public nint Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Module;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExePath;
    }

    private sealed class SafeSnapshotHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeSnapshotHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    private sealed class SafeProcessHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeProcessHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(SafeSnapshotHandle snapshot, ref ProcessEntry32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(SafeSnapshotHandle snapshot, ref ProcessEntry32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32FirstW(SafeSnapshotHandle snapshot, ref ModuleEntry32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32NextW(SafeSnapshotHandle snapshot, ref ModuleEntry32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        SafeProcessHandle process,
        uint flags,
        [Out] char[] name,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out long creation,
        out long exit,
        out long kernel,
        out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
