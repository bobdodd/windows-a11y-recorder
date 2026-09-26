using Npgsql;
using Recorder.Contracts;
using Recorder.Database;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>A PostgreSQL server in a temporary directory, shared by a test class.</summary>
public sealed class EmbeddedPostgresFixture : IAsyncLifetime
{
    public string DataDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "recorder-pg-" + Guid.NewGuid().ToString("N"));

    public EmbeddedPostgresServer Server { get; private set; } = null!;

    public EmbeddedPostgresOptions Options => new(
        BinaryDirectory(),
        DataDirectory,
        new PassThroughSecretProtector());

    public async ValueTask InitializeAsync() =>
        Server = await EmbeddedPostgresServer.StartAsync(Options);

    public async ValueTask DisposeAsync()
    {
        if (Server is not null)
        {
            await Server.DisposeAsync();
        }

        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class PostgresSessionStoreTests(EmbeddedPostgresFixture fixture)
    : IClassFixture<EmbeddedPostgresFixture>
{
    private NpgsqlDataSource DataSource => fixture.Server.DataSource;

    [Fact]
    public async Task CreatesTheClusterListeningOnLoopbackWithPasswordAuthentication()
    {
        await using var command = DataSource.CreateCommand(
            "SELECT current_setting('listen_addresses'), current_setting('password_encryption'), " +
            "current_setting('server_version_num')::int, " +
            "(SELECT count(*) FROM schema_migrations)");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("127.0.0.1", reader.GetString(0));
        Assert.Equal("scram-sha-256", reader.GetString(1));
        Assert.True(reader.GetInt32(2) >= 180000);
        Assert.Equal(DatabaseMigrator.Migrations.Count, reader.GetInt64(3));

        var hba = await File.ReadAllTextAsync(
            Path.Combine(fixture.Server.ClusterDirectory, "pg_hba.conf"),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            hba.Split('\n').Where(line => !line.TrimStart().StartsWith('#')),
            line => line.Contains("trust", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesAConnectionWithoutThePassword()
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.Server.ConnectionString)
        {
            Password = "wrong",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => connection.OpenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("28P01", exception.SqlState);
    }

    [Fact]
    public async Task AttachesToTheRunningServerAndLeavesItRunning()
    {
        var attached = await EmbeddedPostgresServer.StartAsync(
            fixture.Options,
            TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Server.Port, attached.Port);
        await attached.DisposeAsync();

        await using var command = DataSource.CreateCommand("SELECT 1");
        Assert.Equal(1, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApplyingMigrationsAgainChangesNothing()
    {
        await DatabaseMigrator.ApplyAsync(DataSource, TestContext.Current.CancellationToken);

        await using var command = DataSource.CreateCommand("SELECT count(*) FROM schema_migrations");
        Assert.Equal(
            (long)DatabaseMigrator.Migrations.Count,
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreatesARecordingWithItsOwnPartitions()
    {
        var (_, recordingId, _) = await CreateRecordingAsync();

        await using var command = DataSource.CreateCommand(
            "SELECT count(*) FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid " +
            "WHERE c.relname LIKE $1 " +
            "AND i.inhparent::regclass::text IN (SELECT table_name FROM recording_partitioned_tables)");
        command.Parameters.AddWithValue($"%_{recordingId:N}");
        await using var tables = DataSource.CreateCommand("SELECT count(*) FROM recording_partitioned_tables");
        var expected = (long)(await tables.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        Assert.True(expected > 6);
        Assert.Equal(expected, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            RecordingStatus.Recording,
            await new RecordingStore(DataSource).GetStatusAsync(recordingId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoresEveryEnvelopeFieldExactly()
    {
        var (sessionKey, recordingId, _) = await CreateRecordingAsync();
        var collector = Collector("test.collector", "browser.dom");
        var observed = new DateTimeOffset(2026, 9, 25, 12, 0, 1, TimeSpan.Zero).AddTicks(1_234_567);
        var record = Event(sessionKey, collector, 7, 1_500, "browser.dom", "dom-snapshot", new { node = "a" }, "clock-mapped", "partial")
            with
            {
                ClockMappingId = "chromium:browser-1:42",
                NativeTimestamp = new NativeTimestamp("chromium-monotonic", 987_654_321, "microseconds"),
                TimestampUncertaintyNanoseconds = 250_000,
                ObservedUtc = observed,
                RelatedEvidenceIds = ["other:1", "other:2"],
                Analysis = new AnalysisProvenance("analyzer", "2.0", "method", "high", ["a", "b"], null)
            };

        var result = await WriteAsync(sessionKey, recordingId, record);
        Assert.Equal(1, result.WrittenCount);

        await using var command = DataSource.CreateCommand(
            "SELECT e.sequence, e.monotonic_nanoseconds, c.name, t.name, m.name, " +
            "e.observed_utc, e.observed_utc_tick_remainder, d.name, e.native_timestamp_value, u.name, " +
            "e.timestamp_uncertainty_nanoseconds, rc.instance_id, k.collector_type, k.producer_version, " +
            "k.capture_method, v.name, x.name, p.payload::text, " +
            "(SELECT array_agg(q.name ORDER BY q.name) FROM event_quality_flags f " +
            " JOIN quality_flags q USING (quality_flag_id) WHERE f.recording_id = e.recording_id AND f.event_key = e.event_key), " +
            "(SELECT array_agg(r.related_event_id ORDER BY r.ordinal) FROM event_related_evidence r " +
            " WHERE r.recording_id = e.recording_id AND r.event_key = e.event_key), " +
            "a.analyzer, a.confidence_category, " +
            "(SELECT array_agg(i.interpretation ORDER BY i.ordinal) FROM event_analysis_competing_interpretations i " +
            " WHERE i.recording_id = e.recording_id AND i.event_key = e.event_key) " +
            "FROM events e " +
            "JOIN channels c USING (channel_id) JOIN event_types t USING (event_type_id) " +
            "JOIN clock_mappings m USING (clock_mapping_id) " +
            "JOIN timestamp_domains d ON d.timestamp_domain_id = e.native_timestamp_domain_id " +
            "JOIN timestamp_units u ON u.timestamp_unit_id = e.native_timestamp_unit_id " +
            "JOIN recording_collectors rc USING (recording_collector_id) " +
            "JOIN collector_kinds k ON k.collector_kind_id = rc.collector_kind_id " +
            "JOIN event_schema_versions v USING (event_schema_version_id) " +
            "JOIN evidence_classes x USING (evidence_class_id) " +
            "JOIN event_payloads_unmapped p ON p.recording_id = e.recording_id AND p.event_key = e.event_key " +
            "JOIN event_analysis a ON a.recording_id = e.recording_id AND a.event_key = e.event_key " +
            "WHERE e.recording_id = $1");
        command.Parameters.AddWithValue(recordingId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(7L, reader.GetInt64(0));
        Assert.Equal(1_500L, reader.GetInt64(1));
        Assert.Equal("browser.dom", reader.GetString(2));
        Assert.Equal("dom-snapshot", reader.GetString(3));
        Assert.Equal("chromium:browser-1:42", reader.GetString(4));
        var stored = reader.GetFieldValue<DateTime>(5).Ticks + reader.GetInt16(6);
        Assert.Equal(observed.UtcTicks, stored);
        Assert.Equal("chromium-monotonic", reader.GetString(7));
        Assert.Equal(987_654_321L, reader.GetInt64(8));
        Assert.Equal("microseconds", reader.GetString(9));
        Assert.Equal(250_000L, reader.GetInt64(10));
        Assert.Equal(collector.InstanceId, reader.GetString(11));
        Assert.Equal(collector.CollectorType, reader.GetString(12));
        Assert.Equal(collector.ImplementationVersion, reader.GetString(13));
        Assert.Equal(collector.CaptureMethod, reader.GetString(14));
        Assert.Equal(RecorderEvent.CurrentSchemaVersion, reader.GetString(15));
        Assert.Equal(EvidenceClasses.Observed, reader.GetString(16));
        Assert.Equal("{\"node\": \"a\"}", reader.GetString(17));
        Assert.Equal(["clock-mapped", "partial"], reader.GetFieldValue<string[]>(18));
        Assert.Equal(["other:1", "other:2"], reader.GetFieldValue<string[]>(19));
        Assert.Equal("analyzer", reader.GetString(20));
        Assert.Equal("high", reader.GetString(21));
        Assert.Equal(["a", "b"], reader.GetFieldValue<string[]>(22));
    }

    [Fact]
    public async Task RecordsRejectionsOnTheRecording()
    {
        var (sessionKey, recordingId, _) = await CreateRecordingAsync();
        var collector = Collector();

        var result = await WriteAsync(
            sessionKey,
            recordingId,
            Event(sessionKey, collector, 1, 10),
            Event(sessionKey, collector, 1, 20));

        Assert.Equal(1, result.RejectedCount);
        await using var command = DataSource.CreateCommand(
            "SELECT r.reason, r.sequence, rec.rejected_event_count FROM event_rejections r " +
            "JOIN recordings rec USING (recording_id) WHERE r.recording_id = $1");
        command.Parameters.AddWithValue(recordingId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("event-sequence-not-increasing", reader.GetString(0));
        Assert.Equal(1m, reader.GetDecimal(1));
        Assert.Equal(1L, reader.GetInt64(2));
    }

    [Fact]
    public async Task FindsTheEventsTheDatabaseRefusesAndStoresTheRest()
    {
        var (sessionKey, recordingId, _) = await CreateRecordingAsync();
        var collector = Collector();
        var target = new PostgresEventBatchTarget(DataSource, recordingId);
        var first = new BufferedEvent(0, Event(sessionKey, collector, 1, 10), "{}");
        await target.WriteAsync(new EventBatch([first], [], []), TestContext.Current.CancellationToken);

        // Key 1 is new; key 2 repeats the collector, channel, and sequence of key 0.
        var refusals = await target.WriteAsync(
            new EventBatch(
                [
                    new BufferedEvent(1, Event(sessionKey, collector, 2, 20), "{}"),
                    new BufferedEvent(2, Event(sessionKey, collector, 1, 30), "{}")
                ],
                [],
                []),
            TestContext.Current.CancellationToken);

        var refusal = Assert.Single(refusals);
        Assert.Equal(1, refusal.Index);
        Assert.Equal("database-refused:23505", refusal.Reason);
        Assert.Equal(2L, await CountEventsAsync(recordingId));
    }

    [Fact]
    public async Task RegistersCollectorsWithTheirChannelsAndLimitations()
    {
        var (sessionKey, recordingId, _) = await CreateRecordingAsync();
        var collector = Collector("test.collector", "a.channel", "b.channel");
        var store = new RecordingStore(DataSource);
        await store.RegisterCollectorsAsync(
            recordingId,
            [new CollectorRegistration(collector, CapabilityStatus.SupportedWithLimitations, ["one", "two"], CollectorLifecycleState.Ready, CollectorHealthState.Healthy)],
            TestContext.Current.CancellationToken);
        await WriteAsync(sessionKey, recordingId, Event(sessionKey, collector, 1, 1, "a.channel"));
        await store.RegisterCollectorsAsync(
            recordingId,
            [new CollectorRegistration(collector, CapabilityStatus.SupportedWithLimitations, ["one"], CollectorLifecycleState.Stopped, CollectorHealthState.Degraded)],
            TestContext.Current.CancellationToken);

        await using var command = DataSource.CreateCommand(
            "SELECT rc.lifecycle_state, rc.health_state, rc.capability_status, " +
            "(SELECT count(*) FROM recording_collector_channels ch WHERE ch.recording_collector_id = rc.recording_collector_id), " +
            "(SELECT array_agg(l.limitation ORDER BY l.ordinal) FROM recording_collector_limitations l WHERE l.recording_collector_id = rc.recording_collector_id) " +
            "FROM recording_collectors rc WHERE rc.recording_id = $1");
        command.Parameters.AddWithValue(recordingId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Stopped", reader.GetString(0));
        Assert.Equal("Degraded", reader.GetString(1));
        Assert.Equal("SupportedWithLimitations", reader.GetString(2));
        Assert.Equal(2L, reader.GetInt64(3));
        Assert.Equal(["one"], reader.GetFieldValue<string[]>(4));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompletesMarksInterruptedAndDeletesRecordings()
    {
        var store = new RecordingStore(DataSource);
        var (sessionKey, completed, _) = await CreateRecordingAsync();
        var (_, open, _) = await CreateRecordingAsync();
        await WriteAsync(sessionKey, completed, Event(sessionKey, Collector(), 1, 1));

        await store.CompleteRecordingAsync(
            completed,
            new RecordingCompletion(RecordingStatus.Completed, DateTimeOffset.UtcNow, 1_000, 1, 0, null),
            TestContext.Current.CancellationToken);
        Assert.True(await store.MarkOpenRecordingsInterruptedAsync(TestContext.Current.CancellationToken) >= 1);

        Assert.Equal(RecordingStatus.Completed, await store.GetStatusAsync(completed, TestContext.Current.CancellationToken));
        Assert.Equal(RecordingStatus.Interrupted, await store.GetStatusAsync(open, TestContext.Current.CancellationToken));

        await store.DeleteRecordingAsync(completed, TestContext.Current.CancellationToken);
        Assert.Null(await store.GetStatusAsync(completed, TestContext.Current.CancellationToken));
        await using var command = DataSource.CreateCommand(
            "SELECT count(*) FROM pg_class WHERE relname LIKE $1");
        command.Parameters.AddWithValue($"%_{completed:N}");
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private async Task<(string SessionKey, Guid RecordingId, Guid ProjectId)> CreateRecordingAsync()
    {
        var store = new RecordingStore(DataSource);
        var projectId = await store.EnsureProjectAsync("Test project", TestContext.Current.CancellationToken);
        var sessionKey = "session-" + Guid.NewGuid().ToString("N");
        var recordingId = await store.CreateRecordingAsync(
            projectId,
            Definition(sessionKey),
            TestContext.Current.CancellationToken);
        return (sessionKey, recordingId, projectId);
    }

    private async Task<PostgresEventWriterResult> WriteAsync(
        string sessionKey,
        Guid recordingId,
        params RecorderEvent[] events)
    {
        var writer = new PostgresEventWriter(
            new PostgresEventBatchTarget(DataSource, recordingId),
            sessionKey,
            new PostgresEventWriterOptions
            {
                SpillPath = Path.Combine(fixture.DataDirectory, "spill", recordingId + ".ndjson"),
                BatchInterval = TimeSpan.FromMilliseconds(10)
            },
            await PostgresEventBatchTarget.NextEventKeyAsync(DataSource, recordingId, TestContext.Current.CancellationToken));
        foreach (var record in events)
        {
            Assert.True(writer.TryWrite(record));
        }

        var result = await writer.CompleteAsync();
        Assert.Equal(0, result.UnwrittenCount);
        return result;
    }

    private async Task<long> CountEventsAsync(Guid recordingId)
    {
        await using var command = DataSource.CreateCommand("SELECT count(*) FROM events WHERE recording_id = $1");
        command.Parameters.AddWithValue(recordingId);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}

/// <summary>
/// Tests that stop the server, so they run against a server of their own.
/// </summary>
public sealed class PostgresServerLifecycleTests : IAsyncLifetime
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "recorder-pg-" + Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task RestartsWithTheSameClusterAndPassword()
    {
        var options = new EmbeddedPostgresOptions(BinaryDirectory(), _dataDirectory, new PassThroughSecretProtector());
        Guid projectId;
        await using (var first = await EmbeddedPostgresServer.StartAsync(options, TestContext.Current.CancellationToken))
        {
            projectId = await new RecordingStore(first.DataSource).EnsureProjectAsync("Kept", TestContext.Current.CancellationToken);
        }

        await using var second = await EmbeddedPostgresServer.StartAsync(options, TestContext.Current.CancellationToken);
        Assert.Equal(
            projectId,
            await new RecordingStore(second.DataSource).EnsureProjectAsync("Kept", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuffersEventsWhileTheServerIsStoppedAndWritesThemWhenItReturns()
    {
        var port = FreePort();
        var options = new EmbeddedPostgresOptions(BinaryDirectory(), _dataDirectory, new PassThroughSecretProtector())
        {
            Port = port
        };
        var server = await EmbeddedPostgresServer.StartAsync(options, TestContext.Current.CancellationToken);
        var store = new RecordingStore(server.DataSource);
        var projectId = await store.EnsureProjectAsync("Outage", TestContext.Current.CancellationToken);
        const string sessionKey = "outage-session";
        var recordingId = await store.CreateRecordingAsync(projectId, Definition(sessionKey), TestContext.Current.CancellationToken);

        // The writer's own data source outlives the server it was opened on.
        await using var writerSource = NpgsqlDataSource.Create(server.ConnectionString);
        var writer = new PostgresEventWriter(
            new PostgresEventBatchTarget(writerSource, recordingId),
            sessionKey,
            new PostgresEventWriterOptions
            {
                SpillPath = Path.Combine(_dataDirectory, "spill.ndjson"),
                BatchInterval = TimeSpan.FromMilliseconds(10),
                MemoryBufferBytes = 20_000,
                RetryInitialDelay = TimeSpan.FromMilliseconds(50),
                RetryMaximumDelay = TimeSpan.FromMilliseconds(200)
            });
        var collector = Collector();
        writer.TryWrite(Event(sessionKey, collector, 1, 1));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (writer.WrittenCount < 1)
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await server.StopAsync(TestContext.Current.CancellationToken);
        for (ulong sequence = 2; sequence <= 200; sequence++)
        {
            writer.TryWrite(Event(sessionKey, collector, sequence, (long)sequence));
        }

        deadline = DateTime.UtcNow.AddSeconds(10);
        while (!writer.IsDatabaseUnavailable || !File.Exists(Path.Combine(_dataDirectory, "spill.ndjson")))
        {
            Assert.True(DateTime.UtcNow < deadline, "The writer did not spill while the server was stopped.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await using var restarted = await EmbeddedPostgresServer.StartAsync(options, TestContext.Current.CancellationToken);
        var result = await writer.CompleteAsync();

        Assert.Equal(200, result.WrittenCount);
        Assert.Equal(0, result.DroppedCount);
        Assert.Equal(0, result.UnwrittenCount);
        await using var command = restarted.DataSource.CreateCommand(
            "SELECT count(DISTINCT sequence) FROM events WHERE recording_id = $1");
        command.Parameters.AddWithValue(recordingId);
        Assert.Equal(200L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
