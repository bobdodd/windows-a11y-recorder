using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// One audio-buffer record: where a buffer's bytes are in the stream's WAV
/// data and when the recorder received it.
/// </summary>
public sealed record SessionAudioBuffer(
    long BufferSequence,
    long DataByteOffset,
    int ByteLength,
    int SampleFrames,
    long DurationNanoseconds,
    long CallbackMonotonicNanoseconds)
{
    /// <summary>The buffer read from an audio-buffer payload, or null when it is not one.</summary>
    public static SessionAudioBuffer? FromPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !TryInt64(payload, "bufferSequence", out var sequence) ||
            !TryInt64(payload, "dataByteOffset", out var offset) ||
            !TryInt64(payload, "byteLength", out var length) ||
            !TryInt64(payload, "sampleFrames", out var frames) ||
            !TryInt64(payload, "durationNanoseconds", out var duration) ||
            !TryInt64(payload, "callbackMonotonicNanoseconds", out var callback) ||
            offset < 0 || length <= 0 || length > int.MaxValue || frames <= 0 || frames > int.MaxValue ||
            duration <= 0)
        {
            return null;
        }

        return new SessionAudioBuffer(sequence, offset, (int)length, (int)frames, duration, callback);
    }

    private static bool TryInt64(JsonElement payload, string name, out long value)
    {
        value = 0;
        return payload.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out value);
    }
}

/// <summary>
/// Reads every audio-buffer record of one audio stream, which an export
/// needs to place the stream's sound where it was recorded.
/// </summary>
public interface ISessionAudioBufferSource
{
    /// <summary>
    /// The stream's buffer records in buffer order; empty when the stream
    /// has none.
    /// </summary>
    /// <param name="stream">The stream name: microphone, system, or screen-reader.</param>
    IReadOnlyList<SessionAudioBuffer> ReadAudioBuffers(string stream, CancellationToken cancellationToken);
}
