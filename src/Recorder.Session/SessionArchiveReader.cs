using System.Text.Json;

namespace Recorder.Session;

public sealed record SessionPlaybackArchive(
    string SessionDirectory,
    SessionManifest Manifest,
    long DurationNanoseconds,
    IReadOnlyList<SessionTimelineEvent> Events,
    IReadOnlyList<SessionVideoFrame> Frames,
    IReadOnlyList<SessionAudioTrack> AudioTracks,
    IReadOnlyList<BrowserNavigationCorrelation> BrowserNavigations)
{
    /// <summary>
    /// Reads an event's complete record from the event log. Playback keeps
    /// only where each record is, so its text is read when it is needed.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The bytes at the recorded location are no longer that event, which
    /// means the event log changed after the recording was opened.
    /// </exception>
    public string ReadEventJson(SessionTimelineEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var bytes = new byte[item.ByteLength];
        using (var stream = new FileStream(
                   Path.Combine(SessionDirectory, "events.ndjson"),
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.ReadWrite,
                   bufferSize: 1))
        {
            stream.Position = item.ByteOffset;
            stream.ReadExactly(bytes);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var record = document.RootElement;
            if (record.ValueKind == JsonValueKind.Object &&
                record.GetProperty("channel").GetString() == item.Channel &&
                record.GetProperty("monotonicNanoseconds").GetInt64() ==
                    item.MonotonicNanoseconds &&
                (!record.TryGetProperty("eventId", out var eventId) ||
                    eventId.GetString() == item.EventId))
            {
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or
                KeyNotFoundException or FormatException)
        {
        }

        throw new InvalidDataException(
            $"Event line {item.Line} is no longer at its recorded location. " +
            "The event log changed after the recording was opened.");
    }
}

/// <summary>
/// One event in the playback timeline. The complete record stays in the
/// event log at <see cref="ByteOffset"/>; read it with
/// <see cref="SessionPlaybackArchive.ReadEventJson"/>.
/// </summary>
public sealed record SessionTimelineEvent(
    long Line,
    string EventId,
    string EvidenceClass,
    string Channel,
    string EventType,
    long MonotonicNanoseconds,
    string Summary,
    long ByteOffset,
    int ByteLength);

public sealed record SessionVideoFrame(
    long MonotonicNanoseconds,
    string Path,
    string AbsolutePath,
    int Width,
    int Height);

public sealed record SessionAudioTrack(
    string Stream,
    string Path,
    string AbsolutePath,
    long StartNanoseconds);

public static class SessionArchiveReader
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static async Task<SessionPlaybackArchive> LoadAsync(
        string sessionDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);

        var root = Path.GetFullPath(sessionDirectory);
        var manifestPath = Path.Combine(root, "manifest.json");
        var eventPath = Path.Combine(root, "events.ndjson");
        if (!File.Exists(manifestPath) || !File.Exists(eventPath))
        {
            throw new InvalidDataException(
                "The selected folder does not contain manifest.json and events.ndjson.");
        }

        var manifest = await ReadManifestAsync(manifestPath, cancellationToken)
            .ConfigureAwait(false);

        var builder = new SessionPlaybackArchiveBuilder(root);
        using var reader = new NdjsonLineReader(eventPath);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.IsBlankLine)
            {
                continue;
            }

            using var document = JsonDocument.Parse(reader.Line);
            builder.Add(
                reader.LineNumber,
                reader.LineOffset,
                reader.Line.Length,
                document.RootElement);
        }

        return builder.Build(manifest);
    }

    internal static async Task<SessionManifest> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using var manifestStream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<SessionManifest>(
            manifestStream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false) ??
            throw new InvalidDataException("The session manifest is empty.");
    }

    internal static SessionTimelineEvent CreateTimelineEvent(
        long lineNumber,
        long byteOffset,
        int byteLength,
        JsonElement record,
        out JsonElement payload)
    {
        var channel = ReadString(record, "channel") ?? "unknown";
        var eventType = ReadString(record, "eventType") ?? "unknown";
        var timestamp = ReadInt64(record, "monotonicNanoseconds") ?? 0;
        payload = record.TryGetProperty("payload", out var payloadValue)
            ? payloadValue
            : default;
        var eventId = ReadString(record, "eventId") ??
            CreateLegacyEventId(record, channel, lineNumber);
        return new SessionTimelineEvent(
            lineNumber,
            eventId,
            ReadString(record, "evidenceClass") ?? "observed",
            channel,
            eventType,
            timestamp,
            CreateSummary(channel, eventType, payload),
            byteOffset,
            byteLength);
    }

    internal static void AddFrame(
        string root,
        long timestamp,
        JsonElement payload,
        ICollection<SessionVideoFrame> frames)
    {
        var path = ReadString(payload, "path");
        if (!TryResolvePath(root, path, out var absolutePath) ||
            !File.Exists(absolutePath))
        {
            return;
        }

        frames.Add(new SessionVideoFrame(
            timestamp,
            path!,
            absolutePath,
            ReadInt32(payload, "width") ?? 0,
            ReadInt32(payload, "height") ?? 0));
    }

    internal static void AddAudioTrack(
        string root,
        long timestamp,
        JsonElement payload,
        IDictionary<string, SessionAudioTrack> tracks)
    {
        var stream = ReadString(payload, "stream");
        var path = ReadString(payload, "path");
        if (string.IsNullOrWhiteSpace(stream) ||
            !TryResolvePath(root, path, out var absolutePath) ||
            !File.Exists(absolutePath))
        {
            return;
        }

        tracks[stream] = new SessionAudioTrack(
            stream,
            path!,
            absolutePath,
            timestamp);
    }

    private static string CreateLegacyEventId(
        JsonElement record,
        string channel,
        long lineNumber)
    {
        var sessionId = ReadString(record, "sessionId") ?? "unknown-session";
        var collectorId = ReadString(record, "collectorInstanceId") ?? "unknown-collector";
        var sequence = ReadUInt64(record, "sequence");
        return sequence is null
            ? $"{sessionId}:{collectorId}:{channel}:line-{lineNumber}"
            : $"{sessionId}:{collectorId}:{channel}:{sequence.Value}";
    }

    private static string CreateSummary(
        string channel,
        string eventType,
        JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return eventType;
        }

        if (channel == "window.foreground")
        {
            var title = ReadString(payload, "title");
            var process = ReadString(payload, "processName");
            return JoinSummary(eventType, process, title);
        }

        if (channel == "accessibility.uia.events")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "name"),
                ReadString(payload, "automationId"),
                ReadString(payload, "controlType"));
        }

        if (channel == "session.annotations")
        {
            return JoinSummary(eventType, ReadString(payload, "note"));
        }

        if (channel.StartsWith("audio.", StringComparison.Ordinal))
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "stream"),
                ReadString(payload, "device"));
        }

        if (channel == "browser.navigation")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "url"),
                ReadString(payload, "navigationKind"),
                ReadString(payload, "outcome"));
        }

        if (channel == "browser.dispatch")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "eventName"),
                ReadString(payload, "outcome"));
        }

        if (channel == "browser.listener")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "eventName"),
                ReadString(payload, "registrationKind"));
        }

        if (channel == "browser.dom")
        {
            return JoinSummary(
                eventType,
                ReadString(payload, "reason"),
                ReadString(payload, "checkpointId"));
        }

        return eventType;
    }

    private static string JoinSummary(string fallback, params string?[] values)
    {
        var useful = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return useful.Length == 0
            ? fallback
            : $"{fallback}: {string.Join(", ", useful)}";
    }

    private static bool TryResolvePath(
        string root,
        string? relativePath,
        out string absolutePath)
    {
        absolutePath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(root) +
            Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        absolutePath = candidate;
        return true;
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static long? ReadInt64(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetInt64(out var result)
            ? result
            : null;

    private static ulong? ReadUInt64(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetUInt64(out var result)
            ? result
            : null;

    private static int? ReadInt32(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetInt32(out var result)
            ? result
            : null;
}
