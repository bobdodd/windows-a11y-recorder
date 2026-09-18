using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Coordinator;

public sealed class SessionCoordinator : IAsyncDisposable
{
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly Func<RecordingOptions, IReadOnlyList<ICaptureCollector>> _collectorFactory;
    private readonly List<CollectorRuntime> _collectors = [];
    private SessionClock? _clock;
    private NdjsonEventWriter? _writer;
    private RecordingOptions? _options;
    private string? _sessionId;
    private string? _sessionDirectory;
    private DateTimeOffset? _startedUtc;
    private string? _message;
    private bool _disposed;
    private long _annotationSequence = -1;

    public SessionCoordinator(
        Func<RecordingOptions, IReadOnlyList<ICaptureCollector>> collectorFactory)
    {
        ArgumentNullException.ThrowIfNull(collectorFactory);
        _collectorFactory = collectorFactory;
    }

    public RecordingSessionState State { get; private set; } = RecordingSessionState.Idle;

    public RecordingSessionStatus GetStatus()
    {
        var elapsedNanoseconds = _clock?.GetElapsedNanoseconds() ?? 0;
        return new RecordingSessionStatus(
            State,
            _sessionId,
            _sessionDirectory,
            _startedUtc,
            TimeSpan.FromTicks(elapsedNanoseconds / 100),
            _writer?.AcceptedCount ?? 0,
            _writer?.DroppedCount ?? 0,
            _collectors.Select(runtime => new CollectorStatus(
                runtime.Collector.Descriptor.CollectorType,
                runtime.Collector.LifecycleState,
                runtime.Collector.HealthState,
                runtime.Capability?.Status,
                runtime.Capability?.Limitations ?? [])).ToArray(),
            _message);
    }

    public async Task<RecordingSessionStatus> StartAsync(
        RecordingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputRoot);

        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State is not (RecordingSessionState.Idle or
                RecordingSessionState.Completed or
                RecordingSessionState.Failed))
            {
                throw new InvalidOperationException($"Cannot start from {State}.");
            }

            State = RecordingSessionState.Starting;
            _message = "Preparing recording session.";
            _options = options;
            _annotationSequence = -1;
            _clock = new SessionClock();
            _startedUtc = _clock.OriginUtc;
            _sessionId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            _sessionDirectory = Path.Combine(
                Path.GetFullPath(options.OutputRoot),
                _sessionId);
            Directory.CreateDirectory(_sessionDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(_sessionDirectory, ".recording"),
                $"Recording started {_startedUtc:O}{Environment.NewLine}",
                cancellationToken).ConfigureAwait(false);

            _writer = new NdjsonEventWriter(
                Path.Combine(_sessionDirectory, "events.ndjson"));
            _collectors.Clear();
            foreach (var collector in _collectorFactory(options))
            {
                _collectors.Add(new CollectorRuntime(collector));
            }

            await WriteManifestAsync("starting", null, null, cancellationToken)
                .ConfigureAwait(false);
            var context = new CollectorInitializationContext(
                _sessionId,
                _sessionDirectory,
                _clock,
                _writer);

            foreach (var runtime in _collectors)
            {
                runtime.Capability = await runtime.Collector.InitializeAsync(
                    context,
                    cancellationToken).ConfigureAwait(false);
                if (runtime.Capability.BlocksSessionStart)
                {
                    throw new InvalidOperationException(
                        $"{runtime.Collector.Descriptor.CollectorType} unavailable: " +
                        string.Join(", ", runtime.Capability.Limitations));
                }
            }

            foreach (var runtime in _collectors)
            {
                var result = await runtime.Collector.StartAsync(
                    Boundary(),
                    cancellationToken).ConfigureAwait(false);
                if (!result.Accepted)
                {
                    throw new InvalidOperationException(
                        $"Could not start {runtime.Collector.Descriptor.CollectorType}: " +
                        result.Message);
                }
            }

            State = RecordingSessionState.Recording;
            _message = "Recording.";
            await WriteManifestAsync("recording", null, null, cancellationToken)
                .ConfigureAwait(false);
            return GetStatus();
        }
        catch (Exception exception)
        {
            State = RecordingSessionState.Failed;
            _message = exception.Message;
            await FinalizeAfterFailureAsync(exception).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public bool AddMarker(string? note = null)
    {
        if (State != RecordingSessionState.Recording ||
            _writer is null ||
            _clock is null ||
            _sessionId is null)
        {
            return false;
        }

        var descriptor = AnnotationDescriptor.Value;
        var sequence = unchecked(
            (ulong)Interlocked.Increment(ref _annotationSequence));
        return _writer.TryWrite(RecorderEventFactory.Create(
            _sessionId,
            descriptor,
            "session.annotations",
            sequence,
            _clock.GetElapsedNanoseconds(),
            "session-marker",
            new { note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() }));
    }

    public async Task<RecordingSessionStatus> StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State != RecordingSessionState.Recording)
            {
                throw new InvalidOperationException($"Cannot stop from {State}.");
            }

            State = RecordingSessionState.Stopping;
            _message = "Finalizing recording.";
            var failures = new List<string>();
            foreach (var runtime in _collectors.AsEnumerable().Reverse())
            {
                try
                {
                    var result = await runtime.Collector.StopAsync(
                        Boundary(),
                        cancellationToken).ConfigureAwait(false);
                    if (!result.Accepted)
                    {
                        failures.Add(
                            $"{runtime.Collector.Descriptor.CollectorType}: {result.Message}");
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"{runtime.Collector.Descriptor.CollectorType}: {exception.Message}");
                }
            }

            await DisposeCollectorsAsync().ConfigureAwait(false);
            if (_writer is not null)
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
            }

            var endedUtc = DateTimeOffset.UtcNow;
            var completion = failures.Count == 0 ? "completed" : "failed";
            State = failures.Count == 0
                ? RecordingSessionState.Completed
                : RecordingSessionState.Failed;
            _message = failures.Count == 0
                ? "Recording completed."
                : string.Join(Environment.NewLine, failures);
            await WriteManifestAsync(
                completion,
                endedUtc,
                failures.Count == 0 ? null : _message,
                cancellationToken).ConfigureAwait(false);
            DeleteRecordingMarker();
            var validation = await SessionArchiveValidator.ValidateAsync(
                _sessionDirectory!,
                cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                State = RecordingSessionState.Failed;
                _message = "Archive validation failed: " +
                    string.Join(
                        "; ",
                        validation.Issues
                            .Where(issue =>
                                issue.Severity == ArchiveValidationSeverity.Error)
                            .Take(5)
                            .Select(issue => $"{issue.Code} at {issue.Path}"));
                await WriteManifestAsync(
                    "failed",
                    endedUtc,
                    _message,
                    cancellationToken).ConfigureAwait(false);
                validation = await SessionArchiveValidator.ValidateAsync(
                    _sessionDirectory!,
                    cancellationToken).ConfigureAwait(false);
            }

            await SessionArchiveValidator.WriteReportAsync(
                _sessionDirectory!,
                validation,
                cancellationToken).ConfigureAwait(false);
            return GetStatus();
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (State == RecordingSessionState.Recording)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await DisposeCollectorsAsync().ConfigureAwait(false);
            if (_writer is not null)
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
            }
        }

        _transitionLock.Dispose();
        _disposed = true;
    }

    private SessionBoundary Boundary() =>
        new(_clock!.GetElapsedNanoseconds(), DateTimeOffset.UtcNow);

    private async Task FinalizeAfterFailureAsync(Exception exception)
    {
        foreach (var runtime in _collectors.AsEnumerable().Reverse())
        {
            try
            {
                if (runtime.Collector.LifecycleState is
                    CollectorLifecycleState.Running or CollectorLifecycleState.Failed)
                {
                    await runtime.Collector.StopAsync(
                        Boundary(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }

        await DisposeCollectorsAsync().ConfigureAwait(false);
        if (_writer is not null)
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
        }

        if (_sessionDirectory is not null)
        {
            await WriteManifestAsync(
                "failed",
                DateTimeOffset.UtcNow,
                exception.ToString(),
                CancellationToken.None).ConfigureAwait(false);
            DeleteRecordingMarker();
            var validation = await SessionArchiveValidator.ValidateAsync(
                _sessionDirectory,
                CancellationToken.None).ConfigureAwait(false);
            await SessionArchiveValidator.WriteReportAsync(
                _sessionDirectory,
                validation,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task DisposeCollectorsAsync()
    {
        foreach (var runtime in _collectors.AsEnumerable().Reverse())
        {
            try
            {
                await runtime.Collector.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private async Task WriteManifestAsync(
        string status,
        DateTimeOffset? endedUtc,
        string? failure,
        CancellationToken cancellationToken)
    {
        if (_sessionDirectory is null ||
            _sessionId is null ||
            _clock is null ||
            _options is null ||
            _startedUtc is null)
        {
            return;
        }

        var artifacts = status is "completed" or "failed"
            ? await BuildArtifactInventoryAsync(cancellationToken).ConfigureAwait(false)
            : [];
        var manifest = new SessionManifest(
            SessionSchemaVersions.Manifest,
            _sessionId,
            status,
            _startedUtc.Value,
            endedUtc,
            endedUtc is null ? null : _clock.GetElapsedNanoseconds(),
            _clock.Frequency,
            _clock.OriginTimestamp,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            new SessionRecordingConfiguration(
                _options.CaptureKeyboardAndMouse,
                _options.CaptureUiAutomation,
                _options.CaptureForegroundWindow,
                _options.CaptureDesktopFrames,
                _options.FramesPerSecond,
                _options.CaptureMicrophone,
                _options.CaptureSystemAudio),
            _collectors.Select(runtime => new SessionCollectorManifest(
                runtime.Collector.Descriptor.CollectorType,
                runtime.Collector.Descriptor.Implementation,
                runtime.Collector.Descriptor.ImplementationVersion,
                runtime.Collector.Descriptor.Channels,
                runtime.Collector.Descriptor.CaptureMethod,
                runtime.Capability?.Status.ToString(),
                runtime.Capability?.Limitations ?? [],
                runtime.Collector.LifecycleState.ToString(),
                runtime.Collector.HealthState.ToString())).ToArray(),
            artifacts,
            _writer?.AcceptedCount ?? 0,
            _writer?.DroppedCount ?? 0,
            failure);
        await SessionManifestWriter.WriteAsync(
            Path.Combine(_sessionDirectory, "manifest.json"),
            manifest,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SessionArtifact>> BuildArtifactInventoryAsync(
        CancellationToken cancellationToken)
    {
        var artifacts = new List<SessionArtifact>();
        foreach (var path in Directory.EnumerateFiles(
                     _sessionDirectory!,
                     "*",
                     SearchOption.AllDirectories)
                 .Where(path =>
                     !string.Equals(
                         Path.GetFileName(path),
                         "manifest.json",
                         StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(
                         Path.GetFileName(path),
                         ".recording",
                         StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(
                         Path.GetRelativePath(_sessionDirectory!, path).Replace('\\', '/'),
                         SessionArchiveValidator.ReportRelativePath,
                         StringComparison.OrdinalIgnoreCase))
                 .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            artifacts.Add(new SessionArtifact(
                Path.GetRelativePath(_sessionDirectory!, path).Replace('\\', '/'),
                stream.Length,
                Convert.ToHexString(hash).ToLowerInvariant()));
        }

        return artifacts;
    }

    private void DeleteRecordingMarker()
    {
        if (_sessionDirectory is not null)
        {
            File.Delete(Path.Combine(_sessionDirectory, ".recording"));
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class CollectorRuntime(ICaptureCollector collector)
    {
        public ICaptureCollector Collector { get; } = collector;
        public CapabilityResult? Capability { get; set; }
    }

    private static class AnnotationDescriptor
    {
        public static readonly CollectorDescriptor Value = CollectorDescriptor.Create(
            "session.coordinator",
            nameof(SessionCoordinator),
            typeof(SessionCoordinator).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            ["session.annotations"],
            "operator-action");
    }
}
