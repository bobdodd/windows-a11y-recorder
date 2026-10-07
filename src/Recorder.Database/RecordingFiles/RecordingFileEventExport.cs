using System.Text.Json;

namespace Recorder.Database.RecordingFiles;

/// <summary>
/// Writes the events of a recording file as newline-delimited JSON, one
/// event per line in the order of their keys, each in the recorder's event
/// JSON form without whitespace. The writer's records and the state
/// thread's snapshots and index records are not events and are not written.
/// The Windows validation scripts read this form, which is the form of the
/// retired events.ndjson.
/// </summary>
public static class RecordingFileEventExport
{
    private static readonly byte[] NewLine = "\n"u8.ToArray();

    /// <summary>
    /// Writes every event of a finished recording file and returns how many
    /// were written. A file cut short is refused, because its events are not
    /// the recording's.
    /// </summary>
    public static long Write(RecordingFileReader reader, Stream output)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(output);
        if (reader.Incomplete is { } incomplete)
        {
            throw new InvalidDataException($"The recording file was cut short: {incomplete}");
        }

        var events = new List<(long Key, byte[] Json)>();
        foreach (var message in reader.ReadAll())
        {
            if (!RecordingFileBatchTarget.IsEventTopic(message.Channel.Topic))
            {
                continue;
            }

            using var document = JsonDocument.Parse(message.Data);
            var root = document.RootElement;
            // A payload is held as the text it was received with, which may
            // span lines, so each event is written again without whitespace.
            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                root.GetProperty("event").WriteTo(writer);
            }
            events.Add((root.GetProperty("eventKey").GetInt64(), buffer.WrittenSpan.ToArray()));
        }

        events.Sort((first, second) => first.Key.CompareTo(second.Key));
        for (var index = 1; index < events.Count; index++)
        {
            if (events[index].Key == events[index - 1].Key)
            {
                throw new InvalidDataException($"The recording file holds event {events[index].Key} twice.");
            }
        }

        foreach (var (_, json) in events)
        {
            output.Write(json);
            output.Write(NewLine);
        }

        return events.Count;
    }
}
