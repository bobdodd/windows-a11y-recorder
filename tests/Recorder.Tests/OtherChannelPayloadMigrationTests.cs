using Npgsql;
using Recorder.Database;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// Migration 0008 keeps the jsonb payload table for channels the recorder
/// does not define, and removes each recording that still holds a built-in
/// payload there.
/// </summary>
public sealed class OtherChannelPayloadMigrationTests(EmbeddedPostgresFixture fixture)
    : IClassFixture<EmbeddedPostgresFixture>
{
    [Fact]
    public async Task RemovesRecordingsWithBuiltInJsonPayloadsAndKeepsOtherChannels()
    {
        var token = TestContext.Current.CancellationToken;
        var database = "upgrade_" + Guid.NewGuid().ToString("N");
        await using (var create = fixture.Server.DataSource.CreateCommand($"CREATE DATABASE {database}"))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        var builder = new NpgsqlConnectionStringBuilder(fixture.Server.ConnectionString) { Database = database };
        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await DatabaseMigrator.ApplyAsync(dataSource, 7, token);

        var store = new RecordingStore(dataSource);
        var project = await store.EnsureProjectAsync("Upgrade", token);
        var removed = await RecordAsync(dataSource, store, project, withBuiltInJson: true, token);
        var kept = await RecordAsync(dataSource, store, project, withBuiltInJson: false, token);

        await DatabaseMigrator.ApplyAsync(dataSource, 8, token);

        Assert.Equal(0, await ScalarAsync(dataSource, "SELECT count(*) FROM recordings WHERE recording_id = $1", removed, token));
        Assert.Equal(0, await ScalarAsync(dataSource, "SELECT count(*) FROM pg_class WHERE relname LIKE '%' || $1", removed.ToString("N"), token));
        Assert.Equal(1, await ScalarAsync(dataSource, "SELECT count(*) FROM recordings WHERE recording_id = $1", kept, token));
        Assert.Equal(2, await ScalarAsync(dataSource, "SELECT count(*) FROM events WHERE recording_id = $1", kept, token));
        Assert.Equal(1, await ScalarAsync(dataSource, "SELECT count(*) FROM event_payloads_other_channels WHERE recording_id = $1", kept, token));
        Assert.Equal(1, await ScalarAsync(dataSource, "SELECT count(*) FROM pg_class WHERE relname = 'evpo_' || $1", kept.ToString("N"), token));
        Assert.Equal(0, await ScalarAsync(dataSource, "SELECT count(*) FROM pg_class WHERE relname LIKE 'evpu%' OR relname = 'event_payloads_unmapped'", null, token));
        Assert.Equal(
            1,
            await ScalarAsync(dataSource, "SELECT count(*) FROM recording_partitioned_tables WHERE table_name = 'event_payloads_other_channels' AND partition_prefix = 'evpo'", null, token));

        // A recording kept through the upgrade, and through 0009, can still
        // be removed.
        await DatabaseMigrator.ApplyAsync(dataSource, token);
        await store.DeleteRecordingAsync(kept, token);
        Assert.Equal(0, await ScalarAsync(dataSource, "SELECT count(*) FROM recordings", null, token));
    }

    // A recording of one session marker and one event on another channel.
    // The writer no longer stores a payload in the table 0008 renames, so the
    // second event is written as a marker and then moved to the other channel
    // with its payload in the jsonb table, as the writer before 0008 stored
    // it. A recording made before session markers had evidence tables also
    // held the marker's payload in the jsonb table.
    private async Task<Guid> RecordAsync(
        NpgsqlDataSource dataSource,
        RecordingStore store,
        Guid project,
        bool withBuiltInJson,
        CancellationToken token)
    {
        var sessionKey = "upgrade-" + Guid.NewGuid().ToString("N");
        var recordingId = await store.CreateRecordingAsync(project, Definition(sessionKey), token);
        await CreateLegacyPartitionsAsync(dataSource, recordingId, token);
        var collector = Collector("test.upgrade", "session.annotations");
        var writer = new RecordingEventWriter(
            new PostgresEventBatchTarget(dataSource, recordingId),
            sessionKey,
            new RecordingEventWriterOptions
            {
                SpillPath = Path.Combine(fixture.DataDirectory, "spill", Guid.NewGuid().ToString("N") + ".ndjson"),
                ChannelCapacity = 2
            },
            await PostgresEventBatchTarget.NextEventKeyAsync(dataSource, recordingId, token));
        await using (writer)
        {
            Assert.True(writer.TryWrite(Event(sessionKey, collector, 0, 10, "session.annotations", "session-marker",
                Json("""{"note":"marker"}"""))));
            Assert.True(writer.TryWrite(Event(sessionKey, collector, 1, 20, "session.annotations", "session-marker",
                Json("""{"note":"other"}"""))));
            var result = await writer.CompleteAsync();
            Assert.Equal(2, result.WrittenCount);
        }

        const string last = "(SELECT max(event_key) FROM events WHERE recording_id = $1)";
        foreach (var sql in new[]
                 {
                     "INSERT INTO channels (name) SELECT 'test.channel' WHERE $1::uuid IS NOT NULL ON CONFLICT (name) DO NOTHING",
                     $"DELETE FROM session_markers WHERE recording_id = $1 AND event_key = {last}",
                     "UPDATE events SET channel_id = (SELECT channel_id FROM channels WHERE name = 'test.channel') " +
                     $"WHERE recording_id = $1 AND event_key = {last}",
                     "INSERT INTO event_payloads_unmapped (recording_id, event_key, payload) " +
                     $"SELECT $1, {last}, '{{\"value\":1}}'"
                 })
        {
            await using var move = dataSource.CreateCommand(sql);
            move.Parameters.AddWithValue(recordingId);
            await move.ExecuteNonQueryAsync(token);
        }

        if (withBuiltInJson)
        {
            await using var command = dataSource.CreateCommand(
                "INSERT INTO event_payloads_unmapped (recording_id, event_key, payload) " +
                "SELECT $1, min(event_key), '{\"note\":\"marker\"}' FROM events WHERE recording_id = $1");
            command.Parameters.AddWithValue(recordingId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync(token));
        }

        return recordingId;
    }

    private static async Task<long> ScalarAsync(
        NpgsqlDataSource dataSource,
        string sql,
        object? parameter,
        CancellationToken token)
    {
        await using var command = dataSource.CreateCommand(sql);
        if (parameter is not null)
        {
            command.Parameters.AddWithValue(parameter);
        }

        return (long)(await command.ExecuteScalarAsync(token))!;
    }
}
