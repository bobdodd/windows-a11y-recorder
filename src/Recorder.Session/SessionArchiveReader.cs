using System.Text.Json;

namespace Recorder.Session;

public sealed record SessionPlaybackArchive(
    string SessionDirectory,
    SessionManifest Manifest,
    long DurationNanoseconds,
    IReadOnlyList<SessionTimelineEvent> Events,
    IReadOnlyList<SessionVideoFrame> Frames,
    IReadOnlyList<SessionAudioTrack> AudioTracks);

public sealed record SessionTimelineEvent(
    long Line,
    string EventId,
    string EvidenceClass,
    string Channel,
    string EventType,
    long MonotonicNanoseconds,
    string Summary,
    string RawJson);

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

        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<SessionManifest>(
            manifestStream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false) ??
            throw new InvalidDataException("The session manifest is empty.");

        var events = new List<SessionTimelineEvent>();
        var frames = new List<SessionVideoFrame>();
        var audioTracks = new Dictionary<string, SessionAudioTrack>(
            StringComparer.OrdinalIgnoreCase);
        long maximumTimestamp = 0;
        long lineNumber = 0;

        using var reader = new StreamReader(eventPath);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var record = document.RootElement;
            var channel = ReadString(record, "channel") ?? "unknown";
            var eventType = ReadString(record, "eventType") ?? "unknown";
            var timestamp = ReadInt64(record, "monotonicNanoseconds") ?? 0;
            maximumTimestamp = Math.Max(maximumTimestamp, timestamp);

            var payload = record.TryGetProperty("payload", out var payloadValue)
                ? payloadValue
                : default;
            var eventId = ReadString(record, "eventId") ??
                CreateLegacyEventId(record, channel, lineNumber);
            events.Add(new SessionTimelineEvent(
                lineNumber,
                eventId,
                ReadString(record, "evidenceClass") ?? "observed",
                channel,
                eventType,
                timestamp,
                CreateSummary(channel, eventType, payload),
                record.GetRawText()));

            if (channel == "graphics.desktop.frames" &&
                eventType == "desktop-frame" &&
                payload.ValueKind == JsonValueKind.Object)
            {
                AddFrame(root, timestamp, payload, frames);
            }

            if (channel.StartsWith("audio.", StringComparison.Ordinal) &&
                eventType == "audio-stream-started" &&
                payload.ValueKind == JsonValueKind.Object)
            {
                AddAudioTrack(root, timestamp, payload, audioTracks);
            }
        }

        events.Sort(static (left, right) =>
            left.MonotonicNanoseconds.CompareTo(right.MonotonicNanoseconds));
        frames.Sort(static (left, right) =>
            left.MonotonicNanoseconds.CompareTo(right.MonotonicNanoseconds));
        var duration = Math.Max(manifest.DurationNanoseconds ?? 0, maximumTimestamp);
        return new SessionPlaybackArchive(
            root,
            manifest,
            duration,
            events,
            frames,
            audioTracks.Values
                .OrderBy(track => track.Stream, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void AddFrame(
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

    private static void AddAudioTrack(
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
