using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Session;

namespace Recorder.Coordinator;

public sealed class SessionCoordinator : IAsyncDisposable
{
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly Func<RecordingOptions, IReadOnlyList<ICaptureCollector>> _collectorFactory;
    private readonly List<CollectorRuntime> _collectors = [];
    private readonly SessionDatabase _database;
    private SessionClock? _clock;
    private IRecorderEventSink? _sink;
    private DatabaseRecording? _databaseRecording;
    private DatabaseRecordingResult? _databaseResult;
    private RecordingEventWriterResult? _written;
    private string? _databaseProblem;
    private ArtifactHashRegistry _artifactHashes = new();
    private RecordingOptions? _options;
    private string? _sessionId;
    private string? _sessionDirectory;
    private DateTimeOffset? _startedUtc;
    private string? _message;
    private bool _disposed;
    private long _annotationSequence = -1;

    /// <param name="database">
    /// The database each recording's events are written to. The session
    /// folder holds only the manifest and media files.
    /// </param>
    public SessionCoordinator(
        Func<RecordingOptions, IReadOnlyList<ICaptureCollector>> collectorFactory,
        SessionDatabase database)
    {
        ArgumentNullException.ThrowIfNull(collectorFactory);
        ArgumentNullException.ThrowIfNull(database);
        _collectorFactory = collectorFactory;
        _database = database;
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
            AcceptedCount(),
            DroppedCount(),
            _collectors.Select(runtime => new CollectorStatus(
                runtime.Collector.Descriptor.CollectorType,
                runtime.Collector.LifecycleState,
                runtime.Collector.HealthState,
                runtime.Capability?.Status,
                runtime.Capability?.Limitations ?? [],
                runtime.Collector.HealthReason)).ToArray(),
            _message,
            DatabaseStatus());
    }

    // Counts once writing has finished, when the writer's result is final.
    private long AcceptedCount() =>
        _databaseResult?.Writer.AcceptedCount ??
        _written?.AcceptedCount ??
        _databaseRecording?.Writer.AcceptedCount ?? 0;

    private long DroppedCount() =>
        _databaseResult?.Writer.DroppedCount ??
        _written?.DroppedCount ??
        _databaseRecording?.Writer.DroppedCount ?? 0;

    private RecordingDatabaseStatus? DatabaseStatus()
    {
        if (_databaseRecording is null)
        {
            return null;
        }

        var writer = _databaseRecording.Writer;
        var result = _databaseResult?.Writer ?? _written;
        return new RecordingDatabaseStatus(
            result?.AcceptedCount ?? writer.AcceptedCount,
            result?.WrittenCount ?? writer.WrittenCount,
            result?.RejectedCount ?? writer.RejectedCount,
            result?.DroppedCount ?? writer.DroppedCount,
            result?.UnwrittenCount ?? 0,
            result is null && writer.IsDatabaseUnavailable,
            _databaseProblem);
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
            _databaseRecording = null;
            _databaseResult = null;
            _written = null;
            _databaseProblem = null;
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

            _artifactHashes = new ArtifactHashRegistry();
            _databaseRecording = await _database.BeginRecordingAsync(
                new RecordingDefinition(
                    _sessionId,
                    _startedUtc.Value,
                    _clock.Frequency,
                    _clock.OriginTimestamp,
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    CaptureSettings(options)),
                _sessionDirectory,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _sink = _databaseRecording.Writer;

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
                _sink,
                _artifactHashes);

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

            await RegisterCollectorsAsync(cancellationToken).ConfigureAwait(false);
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
            _sink is null ||
            _clock is null ||
            _sessionId is null)
        {
            return false;
        }

        var descriptor = AnnotationDescriptor.Value;
        var sequence = unchecked(
            (ulong)Interlocked.Increment(ref _annotationSequence));
        return _sink.TryWrite(RecorderEventFactory.Create(
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

            await RecordCollectorStatesAsync().ConfigureAwait(false);
            await DisposeCollectorsAsync().ConfigureAwait(false);
            var endedUtc = DateTimeOffset.UtcNow;

            // Every event is in the recording file, or reported as not
            // written, before the manifest states the recording's outcome
            // and counts.
            _written = await _databaseRecording!.FinishWritingAsync().ConfigureAwait(false);
            var collectorFailure = failures.Count == 0
                ? null
                : string.Join(Environment.NewLine, failures);
            failures.AddRange(DatabaseRecording.WritingFailures(_written));
            if (_databaseRecording.FileProblem is { } fileProblem)
            {
                failures.Add(fileProblem);
            }
            State = failures.Count == 0
                ? RecordingSessionState.Completed
                : RecordingSessionState.Failed;
            _message = failures.Count == 0
                ? "Recording completed."
                : string.Join(Environment.NewLine, failures);
            await WriteManifestAsync(
                failures.Count == 0 ? "completed" : "failed",
                endedUtc,
                failures.Count == 0 ? null : _message,
                cancellationToken).ConfigureAwait(false);
            DeleteRecordingMarker();
            await CompleteDatabaseRecordingAsync(
                collectorFailure is null ? RecordingStatus.Completed : RecordingStatus.Failed,
                endedUtc,
                collectorFailure).ConfigureAwait(false);
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
        }

        if (_databaseRecording is not null)
        {
            await _databaseRecording.DisposeAsync().ConfigureAwait(false);
        }

        _transitionLock.Dispose();
        _disposed = true;
    }

    private static RecordingCaptureSettings CaptureSettings(RecordingOptions options) =>
        new(
            options.CaptureKeyboardAndMouse,
            options.CaptureUiAutomation,
            options.CaptureForegroundWindow,
            options.CaptureDesktopFrames,
            options.FramesPerSecond,
            options.CaptureMicrophone,
            options.CaptureSystemAudio);

    private Task RegisterCollectorsAsync(CancellationToken cancellationToken) =>
        _databaseRecording is null
            ? Task.CompletedTask
            : _databaseRecording.RegisterCollectorsAsync(
                _collectors.Select(runtime => new CollectorRegistration(
                    runtime.Collector.Descriptor,
                    runtime.Capability?.Status,
                    runtime.Capability?.Limitations ?? [],
                    runtime.Collector.LifecycleState,
                    runtime.Collector.HealthState)).ToArray(),
                cancellationToken);

    // Collector states at stop go to the database as they go to the
    // manifest. A database that cannot take them does not change how the
    // recording stops; the problem is reported with the database status.
    private async Task RecordCollectorStatesAsync()
    {
        try
        {
            await RegisterCollectorsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AddDatabaseProblem($"Collector states were not stored: {exception.Message}");
        }
    }

    private async Task CompleteDatabaseRecordingAsync(
        RecordingStatus status,
        DateTimeOffset endedUtc,
        string? failure)
    {
        if (_databaseRecording is null || _databaseResult is not null)
        {
            return;
        }

        _databaseResult = await _databaseRecording.CompleteAsync(
            status,
            endedUtc,
            _clock?.GetElapsedNanoseconds() ?? 0,
            failure,
            CancellationToken.None).ConfigureAwait(false);
        WriteWriterTimings();
        var writer = _databaseResult.Writer;
        if (writer.UnwrittenCount > 0)
        {
            AddDatabaseProblem(
                $"{writer.UnwrittenCount:N0} events were not written to the recording file " +
                $"and remain in {writer.SpillPath}.");
        }

        if (_databaseRecording.IndexProblem is { } indexProblem)
        {
            AddDatabaseProblem(indexProblem);
        }

        if (_databaseResult.CompletionError is { } error)
        {
            AddDatabaseProblem($"The recording's final status was not stored: {error}");
        }
    }

    // Measurements of the recorder's own event writing, kept beside the
    // recording so a slow or incomplete write can be examined. They are not
    // evidence and are not stored in the database.
    private void WriteWriterTimings()
    {
        if (_databaseRecording?.Timings is not { } timings || _sessionDirectory is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(
                Path.Combine(_sessionDirectory, "database-writer-timings.json"),
                timings.ToJson());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void AddDatabaseProblem(string problem) =>
        _databaseProblem = _databaseProblem is null
            ? problem
            : _databaseProblem + Environment.NewLine + problem;

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
        await CompleteDatabaseRecordingAsync(
            RecordingStatus.Failed,
            DateTimeOffset.UtcNow,
            exception.Message).ConfigureAwait(false);
        if (_sessionDirectory is not null)
        {
            await WriteManifestAsync(
                "failed",
                DateTimeOffset.UtcNow,
                exception.ToString(),
                CancellationToken.None).ConfigureAwait(false);
            DeleteRecordingMarker();
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
        CancellationToken cancellationToken,
        IReadOnlyList<SessionArtifact>? artifactInventory = null)
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
            ? artifactInventory ??
                await BuildArtifactInventoryAsync(cancellationToken).ConfigureAwait(false)
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
            AcceptedCount(),
            DroppedCount(),
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
                         StringComparison.OrdinalIgnoreCase))
                 .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(_sessionDirectory!, path)
                .Replace('\\', '/');
            // A collector that hashed the file as it wrote it, as the frame
            // collector does, has already supplied the hash. Files no
            // collector reported, such as audio and logs written by other
            // processes, or files changed since they were reported, are read
            // and hashed here.
            if (_artifactHashes.TryGetUnchanged(path, out var writtenHash))
            {
                artifacts.Add(new SessionArtifact(
                    relativePath,
                    new FileInfo(path).Length,
                    writtenHash));
                continue;
            }

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
                relativePath,
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
