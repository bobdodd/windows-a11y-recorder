using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Database.Evidence;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>The evidence migration, which needs no database.</summary>
public sealed class EvidenceMigrationTests
{
    // Set to 1 to rewrite the migration from the catalog, then review the
    // difference before committing it.
    private const string RegenerateVariable = "RECORDER_REGENERATE_EVIDENCE_MIGRATION";

    // A migration, once applied, is never changed: a test that fails here
    // after a catalog edit means the edit changes tables an earlier
    // migration created, and belongs in a new migration instead.
    [Fact]
    public void EachMigrationIsTheOneTheCatalogGenerates()
    {
        foreach (var (version, name, _) in EvidenceCatalog.Migrations)
        {
            var file = string.Create(CultureInfo.InvariantCulture, $"{version:D4}_{name}.sql");
            var generated = EvidenceSql.Migration(version);
            if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1")
            {
                File.WriteAllText(MigrationPath(file), generated.ReplaceLineEndings("\n"));
            }

            Assert.Contains(DatabaseMigrator.Migrations, item => item.Name.EndsWith(file, StringComparison.Ordinal));
            Assert.Equal(generated, File.ReadAllText(MigrationPath(file)).ReplaceLineEndings("\n"));
        }
    }

    private static string MigrationPath(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "windows-a11y-recorder.slnx")))
            {
                return Path.Combine(directory.FullName, "src", "Recorder.Database", "Migrations", file);
            }
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}

public sealed class EvidenceTableTests(EmbeddedPostgresFixture fixture)
    : IClassFixture<EmbeddedPostgresFixture>
{
    private NpgsqlDataSource DataSource => fixture.Server.DataSource;

    [Fact]
    public void EverySampleIsAValidPayloadAndEveryMappedEventTypeHasOne()
    {
        foreach (var (channel, eventType, payload) in EvidenceSamples.All.Concat(
                     EvidenceSamples.Normalized.Select(item => (item.Channel, item.EventType, item.Payload))))
        {
            var issues = new List<EventValidationIssue>();
            using var document = JsonDocument.Parse(payload);
            EventPayloadValidator.Validate(channel, eventType, document.RootElement, 2000, issues);
            Assert.True(issues.Count == 0, $"{channel} {eventType}: {string.Join("; ", issues.Select(item => item.Message))}");
        }

        var sampled = EvidenceSamples.All.Select(item => (item.Channel, item.EventType)).ToHashSet();
        Assert.All(EvidenceCatalog.ByEventType.Keys, key => Assert.Contains(key, sampled));
    }

    [Fact]
    public async Task StoresEveryPayloadInItsTablesAndReadsItBackUnchanged()
    {
        var token = TestContext.Current.CancellationToken;
        var (sessionKey, recordingId) = await CreateRecordingAsync();
        var events = SampleEvents(sessionKey);
        var expected = events.Select(record => Canonical(record.Payload)).ToList();
        var collector = events[0];
        foreach (var (channel, eventType, payload, normal) in EvidenceSamples.Normalized)
        {
            events.Add(events[0] with
            {
                EventId = RecorderEventFactory.CreateEventId(sessionKey, collector.CollectorInstanceId, channel, (ulong)(900 + events.Count)),
                Channel = channel,
                EventType = eventType,
                Sequence = (ulong)(900 + events.Count),
                MonotonicNanoseconds = 900_000 + events.Count,
                Payload = Json(payload)
            });
            expected.Add(Canonical(Json(normal)));
        }

        await WriteAsync(sessionKey, recordingId, events);

        Assert.Equal(events.Count, await CountAsync("events", recordingId));
        Assert.Equal(0, await CountAsync("event_payloads_other_channels", recordingId));
        var source = new DatabaseEventRecordSource(DataSource, recordingId, sessionKey);
        var keys = await EventKeysAsync(recordingId);
        for (var index = 0; index < events.Count; index++)
        {
            var record = events[index];
            var item = new SessionTimelineEvent(
                keys[index], record.EventId, record.EvidenceClass, record.Channel, record.EventType,
                record.MonotonicNanoseconds, record.EventType);
            using var read = JsonDocument.Parse(source.ReadEventJson(item));
            Assert.Equal(expected[index], Canonical(read.RootElement.GetProperty("payload")));
        }

        // Deleting the recording removes its rows from every per-recording
        // table with the rest.
        await new RecordingStore(DataSource).DeleteRecordingAsync(recordingId, token);
        await using var tables = DataSource.CreateCommand("SELECT table_name FROM recording_tables");
        var names = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                names.Add(reader.GetString(0));
            }
        }

        Assert.True(names.Count > 100);
        foreach (var name in names)
        {
            Assert.Equal(0, await CountAsync(name, recordingId));
        }
    }

    [Fact]
    public async Task StoresEachRepeatedIdentityOnceAndContinuesItsKeysOnResume()
    {
        var (sessionKey, recordingId) = await CreateRecordingAsync();

        // One window with a process and one without, one monitor, two UI
        // Automation elements, eight browser contexts, two audio paths and
        // two navigation URLs, three foreground windows, eight event
        // targets, two script locations, one world and two scopes.
        await WriteAsync(sessionKey, recordingId, SampleEvents(sessionKey));
        long[] once = [2, 1, 2, 8, 4, 3, 8, 2, 1, 2];
        Assert.Equal(once, await IdentityCountsAsync(recordingId));

        // A resumed recording has a new writer, which does not look up the
        // identities stored before; it stores its own, under new keys.
        await WriteAsync(sessionKey, recordingId, SampleEvents(sessionKey, firstSequence: 1_000));
        Assert.Equal(once.Select(count => count * 2), await IdentityCountsAsync(recordingId));
    }

    private async Task<long[]> IdentityCountsAsync(Guid recordingId) =>
    [
        await CountAsync("windows", recordingId),
        await CountAsync("monitors", recordingId),
        await CountAsync("uia_elements", recordingId),
        await CountAsync("browser_contexts", recordingId),
        await CountAsync("recording_texts", recordingId),
        await CountAsync("foreground_windows", recordingId),
        await CountAsync("browser_event_targets", recordingId),
        await CountAsync("script_locations", recordingId),
        await CountAsync("execution_worlds", recordingId),
        await CountAsync("execution_scopes", recordingId)
    ];

    [Fact]
    public async Task RefusesAPayloadWithAMemberTheModelDoesNotHold()
    {
        var (sessionKey, recordingId) = await CreateRecordingAsync();
        var collector = Collector("test.evidence", "session.annotations");
        var result = await WriteAsync(
            sessionKey,
            recordingId,
            [
                Event(sessionKey, collector, 0, 10, "session.annotations", "session-marker",
                    Json("""{"note":"kept"}""")),
                Event(sessionKey, collector, 1, 20, "session.annotations", "session-marker",
                    Json("""{"note":"refused","colour":"red"}""")),
                Event(sessionKey, collector, 2, 30, "session.annotations", "session-marker",
                    Json("""{"note":"has\u0000nul"}"""))
            ],
            expectRejections: true);

        Assert.Equal(1, result.WrittenCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.Equal(
            ["payload-property-unexpected session-marker", "payload-text-nul:payload/note session-marker"],
            await RejectionsAsync(recordingId));
        Assert.Equal(1, await CountAsync("session_markers", recordingId));
    }

    [Fact]
    public async Task StoresOnlyOtherChannelsPayloadsAsJsonAndRefusesBuiltInTypesWithoutTables()
    {
        var (sessionKey, recordingId) = await CreateRecordingAsync();
        var collector = Collector("test.evidence", "session.annotations", "test.channel");
        var result = await WriteAsync(
            sessionKey,
            recordingId,
            [
                Event(sessionKey, collector, 0, 10, "test.channel", "test-event", Json("""{"value":1}""")),
                Event(sessionKey, collector, 0, 20, "session.annotations", "marker", Json("""{"note":"refused"}"""))
            ],
            expectRejections: true);

        Assert.Equal(1, result.WrittenCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(["event-type-unsupported marker"], await RejectionsAsync(recordingId));
        Assert.Equal(1, await CountAsync("event_payloads_other_channels", recordingId));
    }

    private static List<RecorderEvent> SampleEvents(string sessionKey, ulong firstSequence = 0)
    {
        var channels = EvidenceSamples.All.Select(item => item.Channel).Distinct().ToArray();
        var collector = new CollectorDescriptor(
            "test.evidence", "evidence-collector", "Evidence collector", "1.0.0", "1.0", channels, "test-capture");
        var sequences = channels.ToDictionary(channel => channel, _ => firstSequence);
        return EvidenceSamples.All
            .Select((sample, index) =>
            {
                var time = (long)firstSequence * 1_000_000 + 10_000 + index * 1_000L;
                return Event(
                    sessionKey,
                    collector,
                    sequences[sample.Channel]++,
                    time,
                    sample.Channel,
                    sample.EventType,
                    Json(TimedAt(sample.Payload, time)));
            })
            .ToList();
    }

    // A drop episode is timed at its last refused arrival, so a sample that
    // states one is moved to the time of the event that carries it.
    private static string TimedAt(string payload, long time)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(payload)!.AsObject();
        if (!node.ContainsKey("lastDroppedAtNanoseconds"))
        {
            return payload;
        }

        var span = node["lastDroppedAtNanoseconds"]!.GetValue<long>() -
            node["firstDroppedAtNanoseconds"]!.GetValue<long>();
        node["lastDroppedAtNanoseconds"] = time;
        node["firstDroppedAtNanoseconds"] = time - span;
        return node.ToJsonString();
    }

    /// <summary>
    /// Migration 0009 moves every per-recording table out of its partitions
    /// into an ordinary table, keeping each row and each constraint and
    /// index under its name.
    /// </summary>
    [Fact]
    public async Task Migration0009KeepsEveryRowConstraintAndIndexInOrdinaryTables()
    {
        var token = TestContext.Current.CancellationToken;
        var database = "upgrade_" + Guid.NewGuid().ToString("N");
        await using (var create = DataSource.CreateCommand($"CREATE DATABASE {database}"))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        var builder = new NpgsqlConnectionStringBuilder(fixture.Server.ConnectionString) { Database = database };
        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await DatabaseMigrator.ApplyAsync(dataSource, 8, token);

        var recordings = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var (sessionKey, recordingId) = await CreateRecordingAsync(dataSource);
            await CreateLegacyPartitionsAsync(dataSource, recordingId, token);
            await WriteAsync(sessionKey, recordingId, SampleEvents(sessionKey), dataSource: dataSource);
            recordings.Add(recordingId);
        }

        const string Constraints =
            "SELECT c.conrelid::regclass::text || ' ' || c.conname || ' ' || pg_get_constraintdef(c.oid) " +
            "FROM pg_constraint c WHERE c.conparentid = 0 AND c.contype IN ('p', 'u', 'f', 'c') " +
            "AND c.conrelid::regclass::text IN (SELECT table_name FROM {0}) ORDER BY 1";
        const string Indexes =
            "SELECT replace(pg_get_indexdef(i.indexrelid), ' ON ONLY ', ' ON ') FROM pg_index i " +
            "JOIN pg_class t ON t.oid = i.indrelid WHERE t.relname::text IN (SELECT table_name FROM {0}) ORDER BY 1";
        var tables = await RowsAsync(dataSource, "SELECT table_name FROM recording_partitioned_tables ORDER BY 1", token);
        var contents = await ContentsAsync(dataSource, tables, token);
        var constraints = await RowsAsync(dataSource, string.Format(CultureInfo.InvariantCulture, Constraints, "recording_partitioned_tables"), token);
        var indexes = await RowsAsync(dataSource, string.Format(CultureInfo.InvariantCulture, Indexes, "recording_partitioned_tables"), token);
        Assert.StartsWith("events " + (3 * SampleEvents("count").Count).ToString(CultureInfo.InvariantCulture) + " ", contents.Single(row => row.StartsWith("events ", StringComparison.Ordinal)));

        await DatabaseMigrator.ApplyAsync(dataSource, token);

        Assert.Equal(tables, await RowsAsync(dataSource, "SELECT table_name FROM recording_tables ORDER BY 1", token));
        Assert.Equal(contents, await ContentsAsync(dataSource, tables, token));
        Assert.Equal(constraints, await RowsAsync(dataSource, string.Format(CultureInfo.InvariantCulture, Constraints, "recording_tables"), token));
        Assert.Equal(indexes, await RowsAsync(dataSource, string.Format(CultureInfo.InvariantCulture, Indexes, "recording_tables"), token));
        Assert.Empty(await RowsAsync(dataSource, "SELECT relname::text FROM pg_class WHERE relkind = 'p' OR relispartition", token));

        // A recording kept through the upgrade can be removed, and a new one
        // written.
        await new RecordingStore(dataSource).DeleteRecordingAsync(recordings[0], token);
        Assert.Empty(await RowsAsync(dataSource, $"SELECT event_key::text FROM events WHERE recording_id = '{recordings[0]:D}'", token));
        var (newKey, newRecording) = await CreateRecordingAsync(dataSource);
        await WriteAsync(newKey, newRecording, SampleEvents(newKey), dataSource: dataSource);
        Assert.NotEmpty(await RowsAsync(dataSource, $"SELECT event_key::text FROM events WHERE recording_id = '{newRecording:D}'", token));
    }

    // The row count and a digest of every row of each table.
    private static async Task<List<string>> ContentsAsync(
        NpgsqlDataSource dataSource,
        IEnumerable<string> tables,
        CancellationToken token)
    {
        var contents = new List<string>();
        foreach (var table in tables)
        {
            contents.AddRange(await RowsAsync(
                dataSource,
                $"SELECT '{table} ' || count(*) || ' ' || md5(coalesce(string_agg(t::text, '|' ORDER BY t::text), '')) FROM {table} t",
                token));
        }

        return contents;
    }

    private static async Task<List<string>> RowsAsync(NpgsqlDataSource dataSource, string sql, CancellationToken token)
    {
        await using var command = dataSource.CreateCommand(sql);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private async Task<(string SessionKey, Guid RecordingId)> CreateRecordingAsync(
        NpgsqlDataSource? dataSource = null)
    {
        var store = new RecordingStore(dataSource ?? DataSource);
        var projectId = await store.EnsureProjectAsync("Evidence", TestContext.Current.CancellationToken);
        var sessionKey = "evidence-" + Guid.NewGuid().ToString("N");
        var recordingId = await store.CreateRecordingAsync(
            projectId,
            Definition(sessionKey),
            TestContext.Current.CancellationToken);
        return (sessionKey, recordingId);
    }

    private async Task<PostgresEventWriterResult> WriteAsync(
        string sessionKey,
        Guid recordingId,
        IReadOnlyList<RecorderEvent> events,
        bool expectRejections = false,
        NpgsqlDataSource? dataSource = null)
    {
        dataSource ??= DataSource;
        var writer = new PostgresEventWriter(
            new PostgresEventBatchTarget(dataSource, recordingId),
            sessionKey,
            new PostgresEventWriterOptions
            {
                SpillPath = Path.Combine(fixture.DataDirectory, "spill", Guid.NewGuid().ToString("N") + ".ndjson"),
                ChannelCapacity = events.Count
            },
            await PostgresEventBatchTarget.NextEventKeyAsync(dataSource, recordingId, TestContext.Current.CancellationToken));
        await using (writer)
        {
            foreach (var record in events)
            {
                Assert.True(writer.TryWrite(record));
            }

            var result = await writer.CompleteAsync();
            Assert.Equal(0, result.UnwrittenCount);
            if (!expectRejections && result.RejectedCount != 0)
            {
                Assert.Fail("Rejected: " + string.Join("; ", await RejectionsAsync(recordingId)));
            }

            return result;
        }
    }

    private async Task<List<string>> RejectionsAsync(Guid recordingId)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT reason || ' ' || coalesce(event_type, '') FROM event_rejections WHERE recording_id = $1 ORDER BY 1");
        command.Parameters.AddWithValue(recordingId);
        var reasons = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            reasons.Add(reader.GetString(0));
        }

        return reasons;
    }

    private async Task<List<long>> EventKeysAsync(Guid recordingId)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT event_key FROM events WHERE recording_id = $1 ORDER BY event_key");
        command.Parameters.AddWithValue(recordingId);
        var keys = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            keys.Add(reader.GetInt64(0));
        }

        return keys;
    }

    private async Task<long> CountAsync(string table, Guid recordingId)
    {
        await using var command = DataSource.CreateCommand($"SELECT count(*) FROM {table} WHERE recording_id = $1");
        command.Parameters.AddWithValue(recordingId);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    // The JSON with every object's properties in name order.
    private static string Canonical(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(value, writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());

        static void Write(JsonElement value, Utf8JsonWriter writer)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        Write(property.Value, writer);
                    }

                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in value.EnumerateArray())
                    {
                        Write(item, writer);
                    }

                    writer.WriteEndArray();
                    break;
                default:
                    value.WriteTo(writer);
                    break;
            }
        }
    }
}
