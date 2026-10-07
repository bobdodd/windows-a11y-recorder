using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>
/// Reads the scripts of a recorded document at a recording time (slice 4h,
/// protocol 0.54) from a recording file's browser.script records, up to that
/// time. A script's text is read from the file only when it is asked for,
/// through the reader, which must stay open while the scripts are used.
/// </summary>
public static class RecordingFileScripts
{
    private sealed record Location(RecordingFileChunk Chunk, int Offset);

    public static RecordedScripts Read(
        RecordingFileReader reader,
        string documentKey,
        long atNanoseconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(documentKey);
        var channel = reader.Channels.Values.FirstOrDefault(item => item.Topic == BrowserEvidenceChannels.Script);
        if (channel is null)
        {
            return RecordedScripts.None;
        }
        var records = new List<(long Time, ulong Sequence, RecorderEvent Event, Location Location)>();
        foreach (var chunk in reader.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.StartTime > atNanoseconds)
            {
                continue;
            }
            var index = reader.ReadMessageIndex(chunk, channel.Id);
            if (index.Length == 0)
            {
                continue;
            }
            var data = reader.ReadChunkRecords(chunk);
            foreach (var (logTime, offset) in index)
            {
                if (logTime > atNanoseconds)
                {
                    continue;
                }
                var stored = RecordingEventCodec.Decode(reader.ReadMessageAt(data, offset).Data.Span);
                records.Add((stored.Event.MonotonicNanoseconds, stored.Event.Sequence, stored.Event, new Location(chunk, offset)));
            }
        }
        var scripts = new RecordedScriptReader(documentKey);
        var texts = new Dictionary<string, Location>(StringComparer.Ordinal);
        foreach (var record in records.OrderBy(item => item.Time).ThenBy(item => item.Sequence))
        {
            if (scripts.Add(record.Event) &&
                record.Event.Payload.TryGetProperty("digest", out var digest) &&
                digest.GetString() is { } text)
            {
                texts.TryAdd(text, record.Location);
            }
        }
        byte[]? Bytes(string digest)
        {
            if (!texts.TryGetValue(digest, out var location))
            {
                return null;
            }
            lock (reader)
            {
                var data = reader.ReadChunkRecords(location.Chunk);
                var stored = RecordingEventCodec.Decode(reader.ReadMessageAt(data, location.Offset).Data.Span);
                return stored.Event.Payload.TryGetProperty("bytes", out var bytes) && bytes.GetString() is { } base64
                    ? Convert.FromBase64String(base64)
                    : null;
            }
        }
        return scripts.Build(Bytes);
    }
}
