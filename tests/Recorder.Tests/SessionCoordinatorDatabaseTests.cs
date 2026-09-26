using System.Diagnostics;
using Npgsql;
using Recorder.Contracts;
using Recorder.Coordinator;
using Recorder.Database;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// The coordinator writing recordings to a real PostgreSQL server as well as
/// to the session files.
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
    public async Task WritesTheRecordingToTheDatabaseAndTheSessionFiles()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await SessionDatabase.StartAsync(Options, token);
        Assert.Equal(0, database.InterruptedRecordingsAtStart);

        RecordingSessionStatus stopped;
        await using (var coordinator = new SessionCoordinator(_ => [new EventCollector()], database))
        {
            await coordinator.StartAsync(new RecordingOptions { OutputRoot = _outputRoot }, token);
            Assert.True(coordinator.AddMarker("In the database"));
            stopped = await coordinator.StopAsync(preparePlayback: true, token);
        }

        Assert.Equal(RecordingSessionState.Completed, stopped.State);
        var status = Assert.IsType<RecordingDatabaseStatus>(stopped.Database);
        Assert.Null(status.Problem);
        Assert.Equal(stopped.AcceptedEvents, status.Accepted);
        Assert.Equal(stopped.AcceptedEvents, status.Written);
        Assert.Equal(0, status.Rejected);
        Assert.Equal(0, status.Dropped);
        Assert.Equal(0, status.Unwritten);

        var fileEvents = (await File.ReadAllLinesAsync(
                Path.Combine(stopped.SessionDirectory!, "events.ndjson"),
                token))
            .Where(line => line.Length > 0)
            .Select(line =>
            {
                using var document = System.Text.Json.JsonDocument.Parse(line);
                return (
                    document.RootElement.GetProperty("channel").GetString()!,
                    document.RootElement.GetProperty("sequence").GetInt64());
            })
            .ToList();
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

            Assert.Equal(
                fileEvents.Order(),
                stored.Order());
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

        // The session files are complete whatever the database did.
        Assert.Equal(RecordingSessionState.Completed, stopped.State);
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

    private sealed class EventCollector(bool blockStart = false) : ICaptureCollector
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
            var sequence = unchecked((ulong)Interlocked.Increment(ref _sequence));
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
