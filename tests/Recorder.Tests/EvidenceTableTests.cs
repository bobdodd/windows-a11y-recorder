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
        var index = 0;
        foreach (var (channel, eventType, payload) in EvidenceSamples.All.Concat(
                     EvidenceSamples.Normalized.Select(item => (item.Channel, item.EventType, item.Payload))))
        {
            var issues = new List<ArchiveValidationIssue>();
            using var record = JsonDocument.Parse(
                $"{{\"channel\":\"{channel}\",\"eventType\":\"{eventType}\",\"monotonicNanoseconds\":2000,\"payload\":{payload}}}");
            EventPayloadValidator.Validate(record.RootElement, issues, index++);
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
                record.MonotonicNanoseconds, record.EventType, 0, 0, keys[index]);
            using var read = JsonDocument.Parse(source.ReadEventJson(item));
            Assert.Equal(expected[index], Canonical(read.RootElement.GetProperty("payload")));
        }

        // Deleting the recording drops its evidence partitions with the rest.
        await new RecordingStore(DataSource).DeleteRecordingAsync(recordingId, token);
        await using var partitions = DataSource.CreateCommand(
            "SELECT count(*) FROM pg_class WHERE relname LIKE $1");
        partitions.Parameters.AddWithValue($"%_{recordingId:N}");
        Assert.Equal(0L, await partitions.ExecuteScalarAsync(token));
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
            ["payload-member-unmapped:payload/colour session-marker", "payload-text-nul:payload/note session-marker"],
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
        Assert.Equal(["event-type-unmapped marker"], await RejectionsAsync(recordingId));
        Assert.Equal(1, await CountAsync("event_payloads_other_channels", recordingId));
    }

    private static List<RecorderEvent> SampleEvents(string sessionKey, ulong firstSequence = 0)
    {
        var channels = EvidenceSamples.All.Select(item => item.Channel).Distinct().ToArray();
        var collector = new CollectorDescriptor(
            "test.evidence", "evidence-collector", "Evidence collector", "1.0.0", "1.0", channels, "test-capture");
        var sequences = channels.ToDictionary(channel => channel, _ => firstSequence);
        return EvidenceSamples.All
            .Select((sample, index) => Event(
                sessionKey,
                collector,
                sequences[sample.Channel]++,
                (long)firstSequence * 1_000_000 + index * 1_000L,
                sample.Channel,
                sample.EventType,
                Json(sample.Payload)))
            .ToList();
    }

    private async Task<(string SessionKey, Guid RecordingId)> CreateRecordingAsync()
    {
        var store = new RecordingStore(DataSource);
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
        bool expectRejections = false)
    {
        var writer = new PostgresEventWriter(
            new PostgresEventBatchTarget(DataSource, recordingId),
            sessionKey,
            new PostgresEventWriterOptions
            {
                SpillPath = Path.Combine(fixture.DataDirectory, "spill", Guid.NewGuid().ToString("N") + ".ndjson"),
                ChannelCapacity = events.Count
            },
            await PostgresEventBatchTarget.NextEventKeyAsync(DataSource, recordingId, TestContext.Current.CancellationToken));
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
