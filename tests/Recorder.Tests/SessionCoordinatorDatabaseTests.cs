using System.Diagnostics;
using Npgsql;
using Recorder.Contracts;
using Recorder.Coordinator;
using Recorder.Database;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// The coordinator writing recordings to a real PostgreSQL server. The session
/// folder holds only the manifest and media files.
/// </summary>
public sealed class SessionCoordinatorDatabaseTests : IAsyncLifetime
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "recorder-pg-" + Guid.NewGuid().ToString("N"));

    private readonly string _outputRoot =
        Path.Combine(Path.GetTempPath(), "recorder-out-" + Guid.NewGuid().ToString("N"));

    private EmbeddedPostgresOptions Options =>
        new(BinaryDirectory(), _dataDirectory, new PassThroughSecretProtector());

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        foreach (var directory in new[] { _dataDirectory, _outputRoot })
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task WritesTheRecordingToTheDatabase()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await SessionDatabase.StartAsync(Options, token);
        Assert.Equal(0, database.InterruptedRecordingsAtStart);

        RecordingSessionStatus stopped;
        await using (var coordinator = new SessionCoordinator(_ => [new EventCollector()], database))
        {
            await coordinator.StartAsync(new RecordingOptions { OutputRoot = _outputRoot }, token);
            Assert.True(coordinator.AddMarker("In the database"));
            stopped = await coordinator.StopAsync(token);
        }

        Assert.Equal(RecordingSessionState.Completed, stopped.State);
        var status = Assert.IsType<RecordingDatabaseStatus>(stopped.Database);
        Assert.Null(status.Problem);
        Assert.Equal(stopped.AcceptedEvents, status.Accepted);
        Assert.Equal(stopped.AcceptedEvents, status.Written);
        Assert.Equal(0, status.Rejected);
        Assert.Equal(0, status.Dropped);
        Assert.Equal(0, status.Unwritten);

        Assert.False(File.Exists(Path.Combine(stopped.SessionDirectory!, "events.ndjson")));
        Assert.False(Directory.Exists(Path.Combine(stopped.SessionDirectory!, "validation")));
        var sessionKey = Path.GetFileName(stopped.SessionDirectory!);
        var recordingId = await RecordingIdAsync(database, sessionKey, token);
        Assert.Equal(RecordingStatus.Completed, await database.Store.GetStatusAsync(recordingId, token));

        await using (var events = database.Server.DataSource.CreateCommand(
            "SELECT c.name, e.sequence FROM events e JOIN channels c USING (channel_id) " +
            "WHERE e.recording_id = $1 ORDER BY e.event_key"))
        {
            events.Parameters.AddWithValue(recordingId);
            var stored = new List<(string Channel, long Sequence)>();
            await using var reader = await events.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                stored.Add((reader.GetString(0), reader.GetInt64(1)));
            }

            Assert.Equal(stopped.AcceptedEvents, stored.Count);
            Assert.Contains(("session.annotations", 0L), stored);
            Assert.Equal(
                [0L, 1L],
                stored.Where(item => item.Channel == "test.database.events")
                    .Select(item => item.Sequence)
                    .Order());
        }

        await using (var recording = database.Server.DataSource.CreateCommand(
            "SELECT accepted_event_count, dropped_event_count, ended_utc IS NOT NULL, failure " +
            "FROM recordings WHERE recording_id = $1"))
        {
            recording.Parameters.AddWithValue(recordingId);
            await using var reader = await recording.ExecuteReaderAsync(token);
            Assert.True(await reader.ReadAsync(token));
            Assert.Equal(stopped.AcceptedEvents, reader.GetInt64(0));
            Assert.Equal(0, reader.GetInt64(1));
            Assert.True(reader.GetBoolean(2));
            Assert.True(reader.IsDBNull(3));
        }

        await using (var collectors = database.Server.DataSource.CreateCommand(
            "SELECT k.collector_type, rc.lifecycle_state, rc.capability_status " +
            "FROM recording_collectors rc JOIN collector_kinds k USING (collector_kind_id) " +
            "WHERE rc.recording_id = $1 ORDER BY k.collector_type"))
        {
            collectors.Parameters.AddWithValue(recordingId);
            var stored = new List<(string Type, string? Lifecycle, string? Capability)>();
            await using var reader = await collectors.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                stored.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }

            // The coordinator's own annotations collector is added by the
            // writer when its first event arrives; the test collector is
            // registered with the state it had at stop.
            Assert.Contains(
                ("test.database", (string?)nameof(CollectorLifecycleState.Stopped), (string?)nameof(CapabilityStatus.Supported)),
                stored);
            Assert.Contains(stored, collector => collector.Type == "session.coordinator");
        }

        Assert.False(File.Exists(database.SpillPathFor(sessionKey)));
    }

    [Fact]
    public async Task MarksARecordingThatCouldNotStartAsFailed()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await SessionDatabase.StartAsync(Options, token);
        await using var coordinator = new SessionCoordinator(
            _ => [new EventCollector(blockStart: true)],
            database);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(new RecordingOptions { OutputRoot = _outputRoot }, token));

        var sessionKey = Path.GetFileName(coordinator.GetStatus().SessionDirectory!);
        var recordingId = await RecordingIdAsync(database, sessionKey, token);
        Assert.Equal(RecordingStatus.Failed, await database.Store.GetStatusAsync(recordingId, token));
    }

    [Fact]
    public async Task ReportsEventsTheDatabaseDidNotTakeAndMarksTheRecordingInterruptedAtNextStart()
    {
        var token = TestContext.Current.CancellationToken;
        RecordingSessionStatus stopped;
        string spillPath;
        await using (var database = await SessionDatabase.StartAsync(Options, token))
        {
            database.WriterCompletionTimeout = TimeSpan.FromSeconds(2);
            await using var coordinator = new SessionCoordinator(_ => [new EventCollector()], database);
            var started = await coordinator.StartAsync(new RecordingOptions { OutputRoot = _outputRoot }, token);
            spillPath = database.SpillPathFor(Path.GetFileName(started.SessionDirectory!));

            // An outage the app did not ask for: the server stops under the
            // writer, as it would if its process ended.
            await StopServerImmediatelyAsync(database.Server.ClusterDirectory, token);
            Assert.True(coordinator.AddMarker("While the database is down"));
            stopped = await coordinator.StopAsync(token);
        }

        // Events the database did not take fail the recording.
        Assert.Equal(RecordingSessionState.Failed, stopped.State);
        Assert.Contains("were not written to the database", stopped.Message);
        var status = Assert.IsType<RecordingDatabaseStatus>(stopped.Database);
        Assert.True(status.Unwritten > 0);
        Assert.NotNull(status.Problem);
        Assert.Contains("were not written to the database", status.Problem);
        Assert.Contains("final status was not stored", status.Problem);
        Assert.True(File.Exists(spillPath));

        await using var restarted = await SessionDatabase.StartAsync(Options, token);
        Assert.Equal(1, restarted.InterruptedRecordingsAtStart);
        var recordingId = await RecordingIdAsync(
            restarted,
            Path.GetFileName(stopped.SessionDirectory!),
            token);
        Assert.Equal(RecordingStatus.Interrupted, await restarted.Store.GetStatusAsync(recordingId, token));
    }

    [Fact]
    public async Task FinalizesManifestAndRemovesRecordingMarker()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await SessionDatabase.StartAsync(Options, token);
        await using var coordinator = new SessionCoordinator(_ => [new EventCollector()], database);
        var started = await coordinator.StartAsync(new RecordingOptions { OutputRoot = _outputRoot }, token);
        Assert.Equal(RecordingSessionState.Recording, started.State);
        Assert.True(File.Exists(Path.Combine(started.SessionDirectory!, ".recording")));
        Assert.True(coordinator.AddMarker("Reached search results"));

        var stopped = await coordinator.StopAsync(token);

        Assert.Equal(RecordingSessionState.Completed, stopped.State);
        Assert.False(File.Exists(Path.Combine(stopped.SessionDirectory!, ".recording")));
        using var manifest = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(stopped.SessionDirectory!, "manifest.json"), token));
        var root = manifest.RootElement;
        Assert.Equal("1.1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, root.GetProperty("endedUtc").ValueKind);
        Assert.True(root.GetProperty("durationNanoseconds").GetInt64() > 0);
        Assert.Equal(3, root.GetProperty("acceptedEventCount").GetInt64());
        Assert.Equal(0, root.GetProperty("droppedEventCount").GetInt64());
        Assert.DoesNotContain(
            root.GetProperty("artifacts").EnumerateArray(),
            item => item.GetProperty("path").GetString() == "events.ndjson");

        var opened = await database.OpenRecordingAsync(stopped.SessionDirectory!, token);
        var archive = Assert.IsType<SessionPlaybackArchive>(opened.Archive);
        Assert.Equal(RecordingStatus.Completed, opened.Status);
        Assert.Equal(3, archive.Timeline.Count);
    }

    [Fact]
    public async Task HashesFromDiskAnArtifactChangedAfterItsHashWasReported()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await SessionDatabase.StartAsync(Options, token);
        await using var coordinator = new SessionCoordinator(
            _ => [new EventCollector(), new ReportingFileCollector()],
            database);
        await coordinator.StartAsync(new RecordingOptions { OutputRoot = _outputRoot }, token);

        var stopped = await coordinator.StopAsync(token);

        Assert.Equal(RecordingSessionState.Completed, stopped.State);
        var path = Path.Combine(stopped.SessionDirectory!, "reported.bin");
        var onDisk = await File.ReadAllBytesAsync(path, token);
        using var manifest = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(stopped.SessionDirectory!, "manifest.json"), token));
        var artifact = manifest.RootElement.GetProperty("artifacts")
            .EnumerateArray()
            .Single(item => item.GetProperty("path").GetString() == "reported.bin");
        Assert.Equal(onDisk.Length, artifact.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(onDisk)),
            artifact.GetProperty("sha256").GetString()!.ToLowerInvariant());
    }

    [Fact]
    public async Task AnEventThatFailsItsChecksFailsTheRecording()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await SessionDatabase.StartAsync(Options, token);
        await using var coordinator = new SessionCoordinator(
            _ => [new EventCollector(repeatSequence: true)],
            database);
        await coordinator.StartAsync(new RecordingOptions { OutputRoot = _outputRoot }, token);

        var stopped = await coordinator.StopAsync(token);

        Assert.Equal(RecordingSessionState.Failed, stopped.State);
        Assert.Contains("failed their checks and were not stored", stopped.Message);
        Assert.Contains("sequence-not-increasing", stopped.Message);
        Assert.Equal(1, Assert.IsType<RecordingDatabaseStatus>(stopped.Database).Rejected);
        using var manifest = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(stopped.SessionDirectory!, "manifest.json"), token));
        Assert.Equal("failed", manifest.RootElement.GetProperty("status").GetString());

        var recordingId = await RecordingIdAsync(
            database,
            Path.GetFileName(stopped.SessionDirectory!),
            token);
        Assert.Equal(RecordingStatus.Failed, await database.Store.GetStatusAsync(recordingId, token));
        var opened = await database.OpenRecordingAsync(stopped.SessionDirectory!, token);
        Assert.NotNull(opened.Archive);
        Assert.Equal(RecordingStatus.Failed, opened.Status);
    }

    [Fact]
    public async Task OnlyOneProcessUsesTheDatabaseAtATime()
    {
        var token = TestContext.Current.CancellationToken;
        await using (var database = await SessionDatabase.StartAsync(Options, token))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SessionDatabase.StartAsync(Options, token));
            Assert.Contains("Another Windows A11y Recorder process", exception.Message);
        }

        await using var restarted = await SessionDatabase.StartAsync(Options, token);
        Assert.Equal(0, restarted.InterruptedRecordingsAtStart);
    }

    private static async Task<Guid> RecordingIdAsync(
        SessionDatabase database,
        string sessionKey,
        CancellationToken token)
    {
        await using var command = database.Server.DataSource.CreateCommand(
            "SELECT recording_id FROM recordings WHERE session_key = $1");
        command.Parameters.AddWithValue(sessionKey);
        return Assert.IsType<Guid>(await command.ExecuteScalarAsync(token));
    }

    private static async Task StopServerImmediatelyAsync(string cluster, CancellationToken token)
    {
        var executable = Path.Combine(
            BinaryDirectory(),
            OperatingSystem.IsWindows() ? "pg_ctl.exe" : "pg_ctl");
        using var process = Process.Start(new ProcessStartInfo(
            executable,
            ["stop", "-D", cluster, "-m", "immediate", "-w"])
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        await process.WaitForExitAsync(token);
        Assert.Equal(0, process.ExitCode);
        NpgsqlConnection.ClearAllPools();
    }

    // Writes a file, reports its hash, then appends to it, as a writer that
    // reopened a finished file would. The manifest must describe the file on
    // disk, not the reported hash.
    private sealed class ReportingFileCollector : ICaptureCollector
    {
        private CollectorInitializationContext? _context;

        public CollectorDescriptor Descriptor { get; } =
            CollectorDescriptor.Create(
                "test.reporting",
                nameof(ReportingFileCollector),
                "1.0",
                [],
                "test");

        public CollectorLifecycleState LifecycleState { get; private set; } =
            CollectorLifecycleState.Created;

        public CollectorHealthState HealthState => CollectorHealthState.Healthy;

        public ValueTask<CapabilityResult> InitializeAsync(
            CollectorInitializationContext context,
            CancellationToken cancellationToken)
        {
            _context = context;
            LifecycleState = CollectorLifecycleState.Ready;
            return ValueTask.FromResult(CapabilityResult.Supported());
        }

        public ValueTask<CollectorTransitionResult> StartAsync(
            SessionBoundary boundary,
            CancellationToken cancellationToken)
        {
            LifecycleState = CollectorLifecycleState.Running;
            return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
        }

        public ValueTask<CollectorTransitionResult> StopAsync(
            SessionBoundary boundary,
            CancellationToken cancellationToken)
        {
            var path = Path.Combine(_context!.SessionDirectory, "reported.bin");
            byte[] written = [1, 2, 3];
            File.WriteAllBytes(path, written);
            Assert.NotNull(_context.ArtifactHashes);
            _context.ArtifactHashes.Record(
                path,
                written.Length,
                System.Security.Cryptography.SHA256.HashData(written));
            File.AppendAllText(path, "changed");
            LifecycleState = CollectorLifecycleState.Stopped;
            return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
        }

        public ValueTask DisposeAsync()
        {
            LifecycleState = CollectorLifecycleState.Disposed;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EventCollector(bool blockStart = false, bool repeatSequence = false) : ICaptureCollector
    {
        private CollectorInitializationContext? _context;
        private long _sequence = -1;

        public CollectorDescriptor Descriptor { get; } =
            CollectorDescriptor.Create(
                "test.database",
                nameof(EventCollector),
                "1.0",
                ["test.database.events"],
                "test");

        public CollectorLifecycleState LifecycleState { get; private set; } =
            CollectorLifecycleState.Created;

        public CollectorHealthState HealthState => CollectorHealthState.Healthy;

        public ValueTask<CapabilityResult> InitializeAsync(
            CollectorInitializationContext context,
            CancellationToken cancellationToken)
        {
            _context = context;
            LifecycleState = CollectorLifecycleState.Ready;
            return ValueTask.FromResult(CapabilityResult.Supported("test.database.events"));
        }

        public ValueTask<CollectorTransitionResult> StartAsync(
            SessionBoundary boundary,
            CancellationToken cancellationToken)
        {
            if (blockStart)
            {
                return ValueTask.FromResult(
                    CollectorTransitionResult.Reject(LifecycleState, "blocked", "blocked by the test"));
            }

            LifecycleState = CollectorLifecycleState.Running;
            Write("started", boundary.MonotonicNanoseconds);
            return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
        }

        public ValueTask<CollectorTransitionResult> StopAsync(
            SessionBoundary boundary,
            CancellationToken cancellationToken)
        {
            Write("stopped", boundary.MonotonicNanoseconds);
            LifecycleState = CollectorLifecycleState.Stopped;
            return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
        }

        public ValueTask DisposeAsync()
        {
            LifecycleState = CollectorLifecycleState.Disposed;
            return ValueTask.CompletedTask;
        }

        private void Write(string action, long monotonicNanoseconds)
        {
            var sequence = repeatSequence
                ? 0UL
                : unchecked((ulong)Interlocked.Increment(ref _sequence));
            Assert.True(_context!.EventSink.TryWrite(RecorderEventFactory.Create(
                _context.SessionId,
                Descriptor,
                "test.database.events",
                sequence,
                monotonicNanoseconds,
                "test-event",
                new { action })));
        }
    }
}
