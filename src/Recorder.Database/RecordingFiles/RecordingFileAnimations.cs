using System.Globalization;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>
/// Reads the animations of a recorded document at a recording time (slice
/// 4g, protocol 0.53) from a recording file: the records of the channels
/// the reader reads, in recording order, up to that time.
/// </summary>
public static class RecordingFileAnimations
{
    public static RecordedAnimations Read(
        RecordingFileReader reader,
        string documentKey,
        long atNanoseconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(documentKey);
        if (!reader.Channels.Values.Any(channel => channel.Topic == Recorder.Contracts.BrowserEvidenceChannels.Animation))
        {
            return RecordedAnimations.None;
        }
        var frequency = reader.Metadata.TryGetValue("recording", out var recording) &&
            recording.TryGetValue("clockFrequency", out var text) &&
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : throw new InvalidDataException("The recording file does not state its clock frequency.");
        var channels = reader.Channels.Values
            .Where(channel => RecordedAnimationReader.Channels.Contains(channel.Topic))
            .Select(channel => channel.Id)
            .ToArray();
        var records = new List<(long Time, ulong Sequence, Recorder.Contracts.RecorderEvent Event)>();
        foreach (var chunk in reader.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.StartTime > atNanoseconds)
            {
                continue;
            }
            byte[]? data = null;
            foreach (var channel in channels)
            {
                var index = reader.ReadMessageIndex(chunk, channel);
                if (index.Length == 0)
                {
                    continue;
                }
                data ??= reader.ReadChunkRecords(chunk);
                foreach (var (logTime, offset) in index)
                {
                    if (logTime > atNanoseconds)
                    {
                        continue;
                    }
                    var stored = RecordingEventCodec.Decode(reader.ReadMessageAt(data, offset).Data.Span);
                    records.Add((stored.Event.MonotonicNanoseconds, stored.Event.Sequence, stored.Event));
                }
            }
        }
        var animations = new RecordedAnimationReader(documentKey, frequency);
        foreach (var record in records.OrderBy(item => item.Time).ThenBy(item => item.Sequence))
        {
            animations.Add(record.Event);
        }
        return animations.At(atNanoseconds);
    }
}
