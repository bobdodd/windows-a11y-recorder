using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Recorder.Contracts;

namespace Recorder.Database.RecordingFiles;

/// <summary>An event as it is stored in a recording file, with its key in the recording.</summary>
public sealed record StoredEvent(long EventKey, RecorderEvent Event);

/// <summary>
/// The JSON form of each event in a recording file: the event's key in the
/// recording and the event in the recorder's event JSON form. The payload
/// is written as the text the event was received with, unchanged.
/// </summary>
public static class RecordingEventCodec
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static void Encode(IBufferWriter<byte> output, long eventKey, RecorderEvent record)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(record);
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { SkipValidation = true });
        JsonSerializer.Serialize(writer, new StoredEvent(eventKey, record), JsonOptions);
    }

    public static StoredEvent Decode(ReadOnlySpan<byte> data) =>
        JsonSerializer.Deserialize<StoredEvent>(data, JsonOptions)
        ?? throw new InvalidDataException("A recording file message holds no event.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new RawPayloadConverter());
        return options;
    }

    // Writes a JSON element as its original text, so a payload keeps its
    // bytes, property order, and escapes as received.
    private sealed class RawPayloadConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.Clone();
        }

        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
    }
}
