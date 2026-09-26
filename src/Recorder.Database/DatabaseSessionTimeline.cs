using System.Text.Json;
using Npgsql;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Database;

/// <summary>
/// The names the database stores as identifiers, loaded once per recording,
/// and the conversion of an event row into a timeline event.
/// </summary>
internal sealed class DatabaseEventNames
{
    // The payload properties playback reads, selected in the database so the
    // rest of each payload is not sent. Transitional, with the payload table.
    private static readonly string PayloadProjection =
        "jsonb_strip_nulls(jsonb_build_object(" +
        string.Join(
            ", ",
            SessionPlaybackArchiveBuilder.PayloadProperties.Select(property =>
                $"'{property}', p.payload -> '{property}'")) +
        "))::text";

    public required string SessionKey { get; init; }

    public required IReadOnlyDictionary<short, string> Channels { get; init; }

    public required IReadOnlyDictionary<int, string> EventTypes { get; init; }

    public required IReadOnlyDictionary<short, string> EvidenceClasses { get; init; }

    public required IReadOnlyDictionary<int, string> Collectors { get; init; }

    /// <summary>The channels whose payload properties playback reads.</summary>
    public required short[] PayloadChannelIds { get; init; }

    /// <summary>
    /// The columns <see cref="Read"/> reads, for events aliased e, with the
    /// recording as $1 and the payload channels as $2.
    /// </summary>
    public static string Columns =>
        "e.event_key, e.recording_collector_id, e.channel_id, e.event_type_id, " +
        "e.evidence_class_id, e.sequence, e.monotonic_nanoseconds, " +
        "CASE WHEN e.channel_id = ANY($2) THEN (SELECT " + PayloadProjection + " " +
        "FROM event_payloads_unmapped p WHERE p.recording_id = $1 AND p.event_key = e.event_key) END";

    public short[] ChannelIds(IEnumerable<string> names)
    {
        var wanted = names.ToHashSet(StringComparer.Ordinal);
        return Channels.Where(pair => wanted.Contains(pair.Value)).Select(pair => pair.Key).ToArray();
    }

    /// <summary>
    /// Reads a row of <see cref="Columns"/>. The payload holds only the
    /// properties playback reads, or is default.
    /// </summary>
    public (SessionTimelineEvent Event, JsonElement Payload) Read(NpgsqlDataReader reader)
    {
        var eventKey = reader.GetInt64(0);
        var channel = Channels[reader.GetInt16(2)];
        var eventType = EventTypes[reader.GetInt32(3)];
        JsonElement payload = default;
        if (!reader.IsDBNull(7))
        {
            using var document = JsonDocument.Parse(reader.GetString(7));
            payload = document.RootElement.Clone();
        }

        var item = new SessionTimelineEvent(
            eventKey,
            RecorderEventFactory.CreateEventId(
                SessionKey,
                Collectors[reader.GetInt32(1)],
                channel,
                unchecked((ulong)reader.GetInt64(5))),
            EvidenceClasses[reader.GetInt16(4)],
            channel,
            eventType,
            reader.GetInt64(6),
            SessionArchiveReader.CreateSummary(channel, eventType, payload),
            0,
            0,
            eventKey);
        return (item, payload);
    }
}

/// <summary>
/// A recording's timeline read from the database as it is needed. Only the
/// number of events on each channel and the occupancy grid are held in
/// memory. Each lookup reads, for each channel, the one event the channel's
/// index gives first, and returns the best of those, so its cost grows with
/// the number of channels rather than the number of events. Events are
/// ordered by time, then by event key.
/// </summary>
public sealed class DatabaseSessionTimeline : ISessionTimeline
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly Guid _recordingId;
    private readonly DatabaseEventNames _names;

    private DatabaseSessionTimeline(
        NpgsqlDataSource dataSource,
        Guid recordingId,
        DatabaseEventNames names,
        IReadOnlyDictionary<string, long> channelCounts,
        TimelineOccupancy occupancy)
    {
        _dataSource = dataSource;
        _recordingId = recordingId;
        _names = names;
        ChannelCounts = channelCounts;
        Count = channelCounts.Values.Sum();
        Occupancy = occupancy;
    }

    public long Count { get; }

    public IReadOnlyDictionary<string, long> ChannelCounts { get; }

    public TimelineOccupancy Occupancy { get; }

    /// <summary>
    /// Loads the per-channel counts and the occupancy grid in one grouped
    /// pass over the recording's events.
    /// </summary>
    internal static async Task<DatabaseSessionTimeline> LoadAsync(
        NpgsqlDataSource dataSource,
        Guid recordingId,
        DatabaseEventNames names,
        long durationNanoseconds,
        CancellationToken cancellationToken)
    {
        var bucketWidth = TimelineOccupancy.WidthFor(durationNanoseconds);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        await using (var command = dataSource.CreateCommand(
            "SELECT channel_id, GREATEST(monotonic_nanoseconds, 0) / $2, count(*) " +
            "FROM events WHERE recording_id = $1 GROUP BY 1, 2 ORDER BY 1, 2"))
        {
            command.Parameters.AddWithValue(recordingId);
            command.Parameters.AddWithValue(bucketWidth);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var channel = names.Channels[reader.GetInt16(0)];
                if (!buckets.TryGetValue(channel, out var list))
                {
                    list = [];
                    buckets[channel] = list;
                    counts[channel] = 0;
                }

                list.Add((int)Math.Min(reader.GetInt64(1), int.MaxValue));
                counts[channel] += reader.GetInt64(2);
            }
        }

        return new DatabaseSessionTimeline(
            dataSource,
            recordingId,
            names,
            counts,
            new TimelineOccupancy(
                durationNanoseconds,
                buckets.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal)));
    }

    public Task<SessionTimelineEvent?> AtOrBeforeAsync(
        long timestamp,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default) =>
        QueryAsync("e.monotonic_nanoseconds <= $4", Descending, channels, [timestamp], cancellationToken);

    public Task<SessionTimelineEvent?> AdjacentAsync(
        SessionTimelineEvent from,
        bool forward,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(from);
        return forward
            ? QueryAsync(
                "(e.monotonic_nanoseconds, e.event_key) > ($4, $5)",
                Ascending,
                channels,
                [from.MonotonicNanoseconds, from.Line],
                cancellationToken)
            : QueryAsync(
                "(e.monotonic_nanoseconds, e.event_key) < ($4, $5)",
                Descending,
                channels,
                [from.MonotonicNanoseconds, from.Line],
                cancellationToken);
    }

    public Task<SessionTimelineEvent?> EndAsync(
        bool last,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default) =>
        QueryAsync("TRUE", last ? Descending : Ascending, channels, [], cancellationToken);

    public async Task<SessionTimelineEvent?> NearestAsync(
        long timestamp,
        long rangeStart,
        long rangeEnd,
        IReadOnlySet<string> channels,
        CancellationToken cancellationToken = default)
    {
        if (rangeStart > rangeEnd)
        {
            return null;
        }

        var target = Math.Clamp(timestamp, rangeStart, rangeEnd);
        var after = await QueryAsync(
            "e.monotonic_nanoseconds >= $4 AND e.monotonic_nanoseconds <= $5",
            Ascending,
            channels,
            [target, rangeEnd],
            cancellationToken).ConfigureAwait(false);
        var latestBefore = await QueryAsync(
            "e.monotonic_nanoseconds < $4 AND e.monotonic_nanoseconds >= $5",
            Descending,
            channels,
            [target, rangeStart],
            cancellationToken).ConfigureAwait(false);

        // Among events at the latest time before the target, the first.
        var before = latestBefore is null
            ? null
            : await QueryAsync(
                "e.monotonic_nanoseconds = $4",
                Ascending,
                channels,
                [latestBefore.MonotonicNanoseconds],
                cancellationToken).ConfigureAwait(false);
        return before is null ? after :
            after is null ? before :
            target - before.MonotonicNanoseconds <= after.MonotonicNanoseconds - target ? before : after;
    }

    private const string Ascending = "e.monotonic_nanoseconds, e.event_key";
    private const string Descending = "e.monotonic_nanoseconds DESC, e.event_key DESC";

    // Returns the first event, in the order given, among the channels that
    // meets the condition. Each channel's first event is read on its own
    // from the index on (recording, channel, time, event key), then the
    // first of those is returned. The condition's parameters start at $4.
    private async Task<SessionTimelineEvent?> QueryAsync(
        string condition,
        string order,
        IReadOnlySet<string> channels,
        long[] parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var channelIds = _names.ChannelIds(channels);
        if (channelIds.Length == 0)
        {
            return null;
        }

        await using var command = _dataSource.CreateCommand(
            $"SELECT {DatabaseEventNames.Columns} FROM unnest($3::smallint[]) AS c (channel_id) " +
            "CROSS JOIN LATERAL (SELECT * FROM events e " +
            "WHERE e.recording_id = $1 AND e.channel_id = c.channel_id AND " + condition +
            " ORDER BY " + order + " LIMIT 1) AS e " +
            "ORDER BY " + order + " LIMIT 1");
        command.Parameters.AddWithValue(_recordingId);
        command.Parameters.AddWithValue(_names.PayloadChannelIds);
        command.Parameters.AddWithValue(channelIds);
        foreach (var value in parameters)
        {
            command.Parameters.AddWithValue(value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? _names.Read(reader).Event
            : null;
    }
}
