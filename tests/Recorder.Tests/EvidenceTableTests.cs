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

/// <summary>The migration files, which need no database.</summary>
public sealed class EvidenceMigrationTests
{
    // A migration, once applied, is never changed. Migrations 0003 to 0007
    // were generated from the catalog as it then stood; a later change to a
    // table is a migration of its own. A test that fails here means an
    // applied migration was edited.
    private static readonly (string File, string Sha256)[] AppliedMigrations =
    [
        ("0001_core.sql", "5b9e6d606a2abf63950654b3b7f936983fd58307103be12c5b3b94637291fc27"),
        ("0002_timeline_order.sql", "485785c7ce87c5b7d3f160330814ff5710f1d2c8c2cef78e8e5c327a206ba6d9"),
        ("0003_evidence_tables.sql", "66135c9e8d9592d886b5ce91f06f7186053410dc56545e7aea619f5d1efd597b"),
        ("0004_browser_script_evidence.sql", "8d25f574e5b6cec6e7237f811529e170855f6e2de6ffb89c413bce7723423c72"),
        ("0005_browser_document_evidence.sql", "8b31f8ebf2301f03d7c45542f061e5f6c44dec1e37e30ca6a306c84aa963a968"),
        ("0006_browser_rendering_evidence.sql", "30ad3a09cd2b0470e4ddd11fc8bc65ecd7bc9f07ab72367ed530687bc900b04c"),
        ("0007_browser_network_evidence.sql", "9424060a3d3d608bed9310c537a24fe71f8cdb23fdc8d34e8aad9bc272cecc14"),
        ("0008_other_channel_payloads.sql", "84fd19e366fc452632943b1fc728c400de0d1c164132efd153b7ef1e51ddd7ec"),
        ("0009_unpartitioned_recording_tables.sql", "1bae83d161af368b5d74a35314051e0392a46a01db37c613b30b1f0f1ccb762a"),
        ("0010_shared_computed_styles.sql", "c02a433d2c631188d087edcca5caaa6819793149083bd7c9263799c46e5e936e"),
        ("0011_bulk_reference_checks.sql", "c99eea0f8615e7137f6b80574d3036f798e66c3391ae0d5b1dfcfeb5362162ae")
    ];

    [Fact]
    public void NoAppliedMigrationIsChanged()
    {
        foreach (var (file, sha256) in AppliedMigrations)
        {
            Assert.Contains(DatabaseMigrator.Migrations, item => item.Name.EndsWith(file, StringComparison.Ordinal));
            var text = File.ReadAllText(MigrationPath(file)).ReplaceLineEndings("\n");
            var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            Assert.True(digest == sha256, $"{file} was changed after it was applied.");
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
    /// Batches written at once, each in its own transaction, store every
    /// payload, and store each identity once, as one writer does.
    /// </summary>
    [Fact]
    public async Task ParallelWritersStoreEveryPayloadAndEachIdentityOnce()
    {
        var (serialKey, serialId) = await CreateRecordingAsync();
        var (parallelKey, parallelId) = await CreateRecordingAsync();
        List<RecorderEvent> Rounds(string sessionKey) =>
            [.. Enumerable.Range(0, 12).SelectMany(round => SampleEvents(sessionKey, (ulong)round * 1_000))];

        var serial = Rounds(serialKey);
        var parallel = Rounds(parallelKey);
        await WriteAsync(serialKey, serialId, serial, options: new PostgresEventWriterOptions
        {
            SpillPath = string.Empty,
            WriterConnections = 1
        });
        var result = await WriteAsync(parallelKey, parallelId, parallel, options: new PostgresEventWriterOptions
        {
            SpillPath = string.Empty,
            BatchSize = 7,
            BatchInterval = TimeSpan.Zero,
            WriterConnections = 6
        });
        Assert.Equal(parallel.Count, result.WrittenCount);

        foreach (var table in EvidenceCatalog.Tables.Where(table => table.Kind == TableKind.Identity))
        {
            Assert.True(
                await CountAsync(table.Name, serialId) == await CountAsync(table.Name, parallelId),
                $"{table.Name} holds a different number of rows.");
        }

        var keys = await EventKeysAsync(parallelId);
        var source = new DatabaseEventRecordSource(DataSource, parallelId, parallelKey);
        for (var index = 0; index < parallel.Count; index++)
        {
            var record = parallel[index];
            var item = new SessionTimelineEvent(
                keys[index], record.EventId, record.EvidenceClass, record.Channel, record.EventType,
                record.MonotonicNanoseconds, record.EventType);
            using var read = JsonDocument.Parse(source.ReadEventJson(item));
            Assert.Equal(Canonical(record.Payload), Canonical(read.RootElement.GetProperty("payload")));
        }
    }

    /// <summary>
    /// The tables the migrations leave are the tables the catalog describes:
    /// the same columns, types, nullability, primary keys, and references.
    /// A reference to another per-recording table or to names is listed in
    /// recording_references and is not a foreign key; every remaining
    /// foreign key of a per-recording table is deferrable; and every table
    /// is listed in recording_tables after the tables it refers to.
    /// </summary>
    [Fact]
    public async Task TheMigratedTablesMatchTheCatalog()
    {
        var token = TestContext.Current.CancellationToken;
        var orders = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = DataSource.CreateCommand("SELECT table_name, table_order FROM recording_tables"))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                orders[reader.GetString(0)] = reader.GetInt16(1);
            }
        }

        foreach (var table in EvidenceCatalog.Tables)
        {
            var expected = new List<string> { "recording_id uuid not null" };
            foreach (var key in table.KeyColumns)
            {
                var type = key is "event_key" or "identity_key" or "owner_key" ? "bigint" : "integer";
                expected.Add($"{key} {type} not null");
            }

            foreach (var column in table.Columns)
            {
                var type = column.SqlType == "timestamptz" ? "timestamp with time zone" : column.SqlType;
                expected.Add($"{column.Name} {type}{(column.NotNull ? " not null" : string.Empty)}");
            }

            expected.Sort(StringComparer.Ordinal);
            Assert.Equal(expected, Sorted(await RowsAsync(
                DataSource,
                "SELECT attname || ' ' || format_type(atttypid, atttypmod) || CASE WHEN attnotnull THEN ' not null' ELSE '' END " +
                $"FROM pg_attribute WHERE attrelid = '{table.Name}'::regclass AND attnum > 0 AND NOT attisdropped",
                token)));

            Assert.Equal(
                [$"PRIMARY KEY (recording_id, {string.Join(", ", table.KeyColumns)})"],
                await RowsAsync(
                    DataSource,
                    $"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = '{table.Name}'::regclass AND contype = 'p'",
                    token));

            var references = new List<string>();
            if (table.Kind == TableKind.Evidence)
            {
                references.Add("(recording_id, event_key) events (recording_id, event_key)");
            }
            else if (table.Kind == TableKind.Child)
            {
                references.Add(
                    $"(recording_id, {string.Join(", ", table.ParentKeyColumns)}) {table.Owner!.Name} " +
                    $"(recording_id, {string.Join(", ", table.OwnerKeyColumns)})");
                if (table.MapEntry)
                {
                    references.Add("(entry_name_id) names (name_id)");
                }
            }

            foreach (var column in table.Columns.Where(column => column.References is not null))
            {
                references.Add(column.References == "names (name_id)"
                    ? $"({column.Name}) names (name_id)"
                    : $"(recording_id, {column.Name}) {column.References} (recording_id, identity_key)");
            }

            // Each is checked by RecordingStore.CheckReferencesAsync, not by
            // a foreign key.
            references.Sort(StringComparer.Ordinal);
            Assert.Equal(references, Sorted(await RowsAsync(
                DataSource,
                "SELECT '(' || array_to_string(columns, ', ') || ') ' || referenced_table || ' (' || " +
                "array_to_string(referenced_columns, ', ') || ')' " +
                $"FROM recording_references WHERE table_name = '{table.Name}'",
                token)));
            Assert.Empty(await RowsAsync(
                DataSource,
                "SELECT c.conname::text FROM pg_constraint c " +
                $"WHERE c.conrelid = '{table.Name}'::regclass AND c.contype = 'f'",
                token));

            Assert.True(orders.ContainsKey(table.Name), $"{table.Name} is not in recording_tables.");
        }

        // Each per-recording table comes after the tables it refers to; each
        // of its remaining foreign keys, to lookup tables and recordings, can
        // be deferred by the writer; and none refers to another per-recording
        // table or to names.
        Assert.Empty(await RowsAsync(
            DataSource,
            "SELECT c.conrelid::regclass::text || ' ' || c.conname FROM pg_constraint c " +
            "JOIN recording_tables r ON r.table_name = c.conrelid::regclass::text " +
            "LEFT JOIN recording_tables f ON f.table_name = c.confrelid::regclass::text " +
            "WHERE c.contype = 'f' AND (NOT c.condeferrable OR c.condeferred OR f.table_name IS NOT NULL " +
            "OR c.confrelid = 'names'::regclass)",
            token));
        Assert.Empty(await RowsAsync(
            DataSource,
            "SELECT x.table_name || ' ' || x.referenced_table FROM recording_references x " +
            "JOIN recording_tables r ON r.table_name = x.table_name " +
            "LEFT JOIN recording_tables f ON f.table_name = x.referenced_table " +
            "WHERE f.table_order >= r.table_order OR (f.table_name IS NULL AND x.referenced_table <> 'names')",
            token));
    }

    /// <summary>
    /// Migration 0010 moves the computed styles already stored into shared
    /// styles, each distinct style of a recording once, and every payload
    /// reads back as it was stored.
    /// </summary>
    [Fact]
    public async Task Migration0010StoresEachDistinctComputedStyleOnceAndKeepsEveryPayload()
    {
        var token = TestContext.Current.CancellationToken;
        var database = "upgrade_" + Guid.NewGuid().ToString("N");
        await using (var create = DataSource.CreateCommand($"CREATE DATABASE {database}"))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        var builder = new NpgsqlConnectionStringBuilder(fixture.Server.ConnectionString) { Database = database };
        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await DatabaseMigrator.ApplyAsync(dataSource, token);

        var (sessionKey, recordingId) = await CreateRecordingAsync(dataSource);
        var events = SampleEvents(sessionKey);
        var layout = events.Where(record => record.EventType == "layout-checkpoint-node").ToArray();
        var styled = layout.First(record => record.Payload.GetProperty("computedStyle") is { ValueKind: JsonValueKind.Object } style &&
            style.EnumerateObject().Any());
        events.Add(styled with
        {
            EventId = RecorderEventFactory.CreateEventId(sessionKey, styled.CollectorInstanceId, styled.Channel, 5_000),
            Sequence = 5_000,
            MonotonicNanoseconds = 5_000_000_000,
            Payload = Json(styled.Payload.GetRawText().Replace("\"nodeIndex\":0", "\"nodeIndex\":7", StringComparison.Ordinal))
        });
        await WriteAsync(sessionKey, recordingId, events, dataSource: dataSource);

        // Returns the layout nodes to the tables of version 9, which held
        // one row per property of each node's style, and applies 0010 again.
        await using (var downgrade = dataSource.CreateCommand(
            "CREATE TABLE browser_layout_checkpoint_computed_styles (" +
            "recording_id uuid NOT NULL, owner_key bigint NOT NULL, " +
            "entry_name_id integer NOT NULL REFERENCES names (name_id), value text, " +
            "PRIMARY KEY (recording_id, owner_key, entry_name_id), FOREIGN KEY (recording_id, owner_key) " +
            "REFERENCES browser_layout_checkpoint_nodes (recording_id, event_key));" +
            "ALTER TABLE browser_layout_checkpoint_nodes ADD COLUMN has_computed_style boolean;" +
            "UPDATE browser_layout_checkpoint_nodes SET has_computed_style = computed_style_key IS NOT NULL;" +
            "ALTER TABLE browser_layout_checkpoint_nodes ALTER COLUMN has_computed_style SET NOT NULL;" +
            "INSERT INTO browser_layout_checkpoint_computed_styles " +
            "SELECT n.recording_id, n.event_key, e.entry_name_id, e.value FROM browser_layout_checkpoint_nodes n " +
            "JOIN browser_computed_style_entries e ON e.recording_id = n.recording_id AND e.owner_key = n.computed_style_key;" +
            "ALTER TABLE browser_layout_checkpoint_nodes DROP COLUMN computed_style_key;" +
            "DROP TABLE browser_computed_style_entries, browser_computed_styles;" +
            "DELETE FROM recording_tables WHERE table_name IN ('browser_computed_style_entries', 'browser_computed_styles');" +
            "INSERT INTO recording_tables SELECT 'browser_layout_checkpoint_computed_styles', max(table_order) + 1 FROM recording_tables;" +
            "DELETE FROM schema_migrations WHERE version = 10;"))
        {
            await downgrade.ExecuteNonQueryAsync(token);
        }

        Assert.NotEmpty(await RowsAsync(dataSource, "SELECT owner_key::text FROM browser_layout_checkpoint_computed_styles", token));
        await DatabaseMigrator.ApplyAsync(dataSource, token);

        var keys = await EventKeysAsync(recordingId, dataSource);
        var source = new DatabaseEventRecordSource(dataSource, recordingId, sessionKey);
        for (var index = 0; index < events.Count; index++)
        {
            var record = events[index];
            var item = new SessionTimelineEvent(
                keys[index], record.EventId, record.EvidenceClass, record.Channel, record.EventType,
                record.MonotonicNanoseconds, record.EventType);
            using var read = JsonDocument.Parse(source.ReadEventJson(item));
            Assert.Equal(Canonical(record.Payload), Canonical(read.RootElement.GetProperty("payload")));
        }

        // The two nodes with the same style share it; the node without one
        // has none; the empty style is a style with no entries.
        var distinct = layout.Select(record => record.Payload.GetProperty("computedStyle"))
            .Where(style => style.ValueKind == JsonValueKind.Object)
            .Select(style => Canonical(style))
            .Distinct()
            .Count();
        Assert.Equal(
            [distinct.ToString(CultureInfo.InvariantCulture)],
            await RowsAsync(dataSource, $"SELECT count(*)::text FROM browser_computed_styles WHERE recording_id = '{recordingId:D}'", token));
        Assert.Equal(
            ["1"],
            await RowsAsync(
                dataSource,
                $"SELECT count(DISTINCT computed_style_key)::text FROM browser_layout_checkpoint_nodes WHERE recording_id = '{recordingId:D}' " +
                $"AND event_key IN ({keys[events.IndexOf(styled)]}, {keys[^1]})",
                token));
        Assert.Equal(
            ["t"],
            await RowsAsync(dataSource, "SELECT CASE WHEN to_regclass('browser_layout_checkpoint_computed_styles') IS NULL THEN 't' ELSE 'f' END", token));
    }

    /// <summary>
    /// Migration 0011 moves the visible path indexes already stored into the
    /// array column of their scope, and every payload reads back as it was
    /// stored.
    /// </summary>
    [Fact]
    public async Task Migration0011StoresVisiblePathIndexesAsArraysAndKeepsEveryPayload()
    {
        var token = TestContext.Current.CancellationToken;
        var database = "upgrade_" + Guid.NewGuid().ToString("N");
        await using (var create = DataSource.CreateCommand($"CREATE DATABASE {database}"))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        var builder = new NpgsqlConnectionStringBuilder(fixture.Server.ConnectionString) { Database = database };
        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await DatabaseMigrator.ApplyAsync(dataSource, token);

        var (sessionKey, recordingId) = await CreateRecordingAsync(dataSource);
        var events = SampleEvents(sessionKey);
        await WriteAsync(sessionKey, recordingId, events, dataSource: dataSource);
        var scopes = await RowsAsync(
            dataSource,
            "SELECT count(*)::text FROM browser_dispatch_path_scopes WHERE cardinality(visible_path_indexes) > 0",
            token);
        Assert.NotEqual(["0"], scopes);

        // Returns the scopes to the tables of version 10, which held one row
        // per visible index, and applies 0011 again.
        await using (var downgrade = dataSource.CreateCommand(
            "CREATE TABLE browser_dispatch_path_scope_visible_indexes (" +
            "recording_id uuid NOT NULL, owner_key bigint NOT NULL, ordinal_1 integer NOT NULL, " +
            "ordinal_2 integer NOT NULL, value integer NOT NULL, " +
            "PRIMARY KEY (recording_id, owner_key, ordinal_1, ordinal_2));" +
            "INSERT INTO browser_dispatch_path_scope_visible_indexes " +
            "SELECT s.recording_id, s.owner_key, s.ordinal_1, i.ordinality - 1, i.value " +
            "FROM browser_dispatch_path_scopes s, unnest(s.visible_path_indexes) WITH ORDINALITY i(value, ordinality);" +
            "ALTER TABLE browser_dispatch_path_scopes DROP COLUMN visible_path_indexes;" +
            "INSERT INTO recording_tables SELECT 'browser_dispatch_path_scope_visible_indexes', max(table_order) + 1 FROM recording_tables;" +
            "DROP TABLE recording_references;" +
            "DELETE FROM schema_migrations WHERE version = 11;"))
        {
            await downgrade.ExecuteNonQueryAsync(token);
        }

        Assert.NotEmpty(await RowsAsync(dataSource, "SELECT owner_key::text FROM browser_dispatch_path_scope_visible_indexes", token));
        await DatabaseMigrator.ApplyAsync(dataSource, token);

        Assert.Equal(scopes, await RowsAsync(
            dataSource,
            "SELECT count(*)::text FROM browser_dispatch_path_scopes WHERE cardinality(visible_path_indexes) > 0",
            token));
        var keys = await EventKeysAsync(recordingId, dataSource);
        var source = new DatabaseEventRecordSource(dataSource, recordingId, sessionKey);
        for (var index = 0; index < events.Count; index++)
        {
            var record = events[index];
            var item = new SessionTimelineEvent(
                keys[index], record.EventId, record.EvidenceClass, record.Channel, record.EventType,
                record.MonotonicNanoseconds, record.EventType);
            using var read = JsonDocument.Parse(source.ReadEventJson(item));
            Assert.Equal(Canonical(record.Payload), Canonical(read.RootElement.GetProperty("payload")));
        }

        Assert.Equal(
            ["t"],
            await RowsAsync(dataSource, "SELECT CASE WHEN to_regclass('browser_dispatch_path_scope_visible_indexes') IS NULL THEN 't' ELSE 'f' END", token));
    }

    /// <summary>
    /// The references between a recording's rows are checked when the
    /// recording is completed: none is missing after the writer stores
    /// every sample, and a row that refers to a missing row is reported by
    /// table and columns.
    /// </summary>
    [Fact]
    public async Task CheckingReferencesFindsARowThatRefersToAMissingRow()
    {
        var token = TestContext.Current.CancellationToken;
        var (sessionKey, recordingId) = await CreateRecordingAsync();
        await WriteAsync(sessionKey, recordingId, SampleEvents(sessionKey));
        var store = new RecordingStore(DataSource);
        Assert.Empty(await store.CheckReferencesAsync(recordingId, cancellationToken: token));
        Assert.NotEmpty(await RowsAsync(DataSource, "SELECT table_name FROM recording_references", token));

        await using (var orphan = DataSource.CreateCommand(
            "INSERT INTO browser_dispatch_path_targets (recording_id, owner_key, ordinal_1, target_key) " +
            "(SELECT recording_id, owner_key, 99, target_key FROM browser_dispatch_path_targets " +
            "WHERE recording_id = $1 LIMIT 1) UNION ALL " +
            "(SELECT recording_id, -1, 0, target_key FROM browser_dispatch_path_targets " +
            "WHERE recording_id = $1 LIMIT 1)"))
        {
            orphan.Parameters.AddWithValue(recordingId);
            await orphan.ExecuteNonQueryAsync(token);
        }

        var failure = Assert.Single(await store.CheckReferencesAsync(recordingId, cancellationToken: token));
        Assert.Equal(
            "1 rows of browser_dispatch_path_targets (recording_id, owner_key) refer to rows missing from " +
            "browser_dispatch_events (recording_id, event_key).",
            failure);
    }

    /// <summary>
    /// Given writer timings, the reference check notes the server's other
    /// activity and the tables' statistics when it starts, and the plan of
    /// each query that took at least the threshold, with each step's actual
    /// rows and buffers but not its time. The plans reach the timings, not the server log.
    /// </summary>
    [Fact]
    public async Task CheckingReferencesNotesThePlansOfSlowQueries()
    {
        var token = TestContext.Current.CancellationToken;
        var (sessionKey, recordingId) = await CreateRecordingAsync();
        await WriteAsync(sessionKey, recordingId, SampleEvents(sessionKey));
        var store = new RecordingStore(DataSource) { ExplainedCheckMilliseconds = 0 };
        using var timings = new WriterTimings();
        Assert.Empty(await store.CheckReferencesAsync(recordingId, timings, token));

        using var report = System.Text.Json.JsonDocument.Parse(timings.ToJson());
        var notes = report.RootElement.GetProperty("notes").EnumerateArray()
            .Select(note => (Name: note.GetProperty("name").GetString()!, Text: note.GetProperty("text").GetString()!))
            .ToList();
        Assert.Contains(notes, note => note.Name == "check-references-activity:start");
        Assert.Contains(
            notes,
            note => note.Name == "check-references-statistics:start" &&
                note.Text.Contains("browser_dispatch_path_targets | live ", StringComparison.Ordinal));
        var plan = Assert.Single(
            notes,
            note => note.Name == "check-references-plan:browser_dispatch_path_targets(recording_id,owner_key)->browser_dispatch_events");
        Assert.Contains("actual rows=", plan.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("actual time=", plan.Text, StringComparison.Ordinal);
        Assert.Contains("Buffers: shared", plan.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// With the default threshold, the quick queries of a small recording
    /// note no plans.
    /// </summary>
    [Fact]
    public async Task CheckingReferencesNotesNoPlanForAQuickQuery()
    {
        var token = TestContext.Current.CancellationToken;
        var (sessionKey, recordingId) = await CreateRecordingAsync();
        await WriteAsync(sessionKey, recordingId, SampleEvents(sessionKey));
        using var timings = new WriterTimings();
        Assert.Empty(await new RecordingStore(DataSource).CheckReferencesAsync(recordingId, timings, token));

        using var report = System.Text.Json.JsonDocument.Parse(timings.ToJson());
        Assert.DoesNotContain(
            report.RootElement.GetProperty("notes").EnumerateArray(),
            note => note.GetProperty("name").GetString()!.StartsWith("check-references-plan:", StringComparison.Ordinal));
    }

    /// <summary>
    /// The reference check gathers the planner statistics of each table its
    /// queries read before it runs them, and times each table's analysis.
    /// </summary>
    [Fact]
    public async Task CheckingReferencesGathersStatisticsForTheTablesItReads()
    {
        var token = TestContext.Current.CancellationToken;
        var (sessionKey, recordingId) = await CreateRecordingAsync();
        await WriteAsync(sessionKey, recordingId, SampleEvents(sessionKey));
        var before = DateTime.UtcNow;
        using var timings = new WriterTimings();
        Assert.Empty(await new RecordingStore(DataSource).CheckReferencesAsync(recordingId, timings, token));

        foreach (var table in new[] { "browser_dispatch_path_targets", "browser_dispatch_events", "events", "names" })
        {
            await using var analyzed = DataSource.CreateCommand(
                "SELECT last_analyze FROM pg_stat_user_tables WHERE relname = $1");
            analyzed.Parameters.AddWithValue(table);
            var at = await analyzed.ExecuteScalarAsync(token);
            Assert.True(at is DateTime time && time.ToUniversalTime() >= before.AddSeconds(-1), table);
        }

        using var report = System.Text.Json.JsonDocument.Parse(timings.ToJson());
        var stages = report.RootElement.GetProperty("stages").EnumerateArray()
            .Select(stage => stage.GetProperty("stage").GetString()!)
            .ToList();
        Assert.Contains("check-references.analyze", stages);
        Assert.Contains("check-references-analyze:browser_dispatch_path_targets", stages);
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

            // Migrations 0010 and 0011 changed how layout nodes and dispatch
            // path scopes are stored, so the writer cannot store them in the
            // tables of version 8.
            await WriteAsync(
                sessionKey,
                recordingId,
                [.. SampleEvents(sessionKey).Where(WritableAtVersion8)],
                dataSource: dataSource);
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
        Assert.StartsWith("events " + (3 * SampleEvents("count").Count(WritableAtVersion8)).ToString(CultureInfo.InvariantCulture) + " ", contents.Single(row => row.StartsWith("events ", StringComparison.Ordinal)));

        await DatabaseMigrator.ApplyAsync(dataSource, 9, token);

        Assert.Equal(tables, await RowsAsync(dataSource, "SELECT table_name FROM recording_tables ORDER BY 1", token));
        Assert.Equal(contents, await ContentsAsync(dataSource, tables, token));
        Assert.Equal(constraints, await RowsAsync(dataSource, string.Format(CultureInfo.InvariantCulture, Constraints, "recording_tables"), token));
        Assert.Equal(indexes, await RowsAsync(dataSource, string.Format(CultureInfo.InvariantCulture, Indexes, "recording_tables"), token));
        Assert.Empty(await RowsAsync(dataSource, "SELECT relname::text FROM pg_class WHERE relkind = 'p' OR relispartition", token));

        // A recording kept through the upgrade can be removed, and a new one
        // written.
        await DatabaseMigrator.ApplyAsync(dataSource, token);
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

    // The tables of version 8 have no computed style key on layout nodes and
    // hold a dispatch path scope's visible indexes as rows, so a sample with
    // either cannot be written into them.
    private static bool WritableAtVersion8(RecorderEvent record) =>
        record.EventType != "layout-checkpoint-node" &&
        !(record.Payload.TryGetProperty("pathScopes", out var scopes) &&
          scopes.ValueKind == JsonValueKind.Array &&
          scopes.GetArrayLength() > 0);

    private static List<string> Sorted(List<string> rows)
    {
        rows.Sort(StringComparer.Ordinal);
        return rows;
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
        NpgsqlDataSource? dataSource = null,
        PostgresEventWriterOptions? options = null)
    {
        dataSource ??= DataSource;
        var writer = new PostgresEventWriter(
            new PostgresEventBatchTarget(dataSource, recordingId),
            sessionKey,
            (options ?? new PostgresEventWriterOptions { SpillPath = string.Empty }) with
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

    private async Task<List<long>> EventKeysAsync(Guid recordingId, NpgsqlDataSource? dataSource = null)
    {
        await using var command = (dataSource ?? DataSource).CreateCommand(
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
