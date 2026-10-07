using System.Globalization;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>
/// Reads the frame each animated image of a recorded document is held at in
/// the recreation (slice 4b, "Sub-step 2a design: animated images held")
/// from a recording file: the records of the channels the chooser reads, in
/// recording order, up to a while after the frame's composition, as a
/// compositor frame's presentation is recorded after it is presented.
/// </summary>
public static class RecordingFileImageFrames
{
    /// <summary>
    /// How long after the composition the presentation records are read: a
    /// compositor frame presented at or before the composition whose
    /// presentation is recorded later than this is taken as not presented.
    /// </summary>
    public static readonly long PresentationWindowNanoseconds = 2_000_000_000;

    public static RecordedImageFrames Read(
        RecordingFileReader reader,
        string documentKey,
        long cutNanoseconds,
        long compositionNanoseconds,
        CancellationToken cancellationToken = default) =>
        ReadWithCompositorValues(reader, documentKey, cutNanoseconds, compositionNanoseconds, cancellationToken).ImageFrames;

    /// <summary>
    /// Reads the image frames and, from the same records, the compositor
    /// values the document's nodes take in the recreation (slice 4b,
    /// "Sub-step 2b-i design: compositor values imposed").
    /// </summary>
    public static (RecordedImageFrames ImageFrames, RecordedCompositorValues CompositorValues) ReadWithCompositorValues(
        RecordingFileReader reader,
        string documentKey,
        long cutNanoseconds,
        long compositionNanoseconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(documentKey);
        var frequency = reader.Metadata.TryGetValue("recording", out var recording) &&
            recording.TryGetValue("clockFrequency", out var text) &&
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : throw new InvalidDataException("The recording file does not state its clock frequency.");
        var channels = reader.Channels.Values
            .Where(channel => RecordedImageFrameChooser.Channels.Contains(channel.Topic) ||
                              RecordedCompositorValueChooser.Channels.Contains(channel.Topic) ||
                              RecordedDisplayLatencyReader.Channels.Contains(channel.Topic))
            .Select(channel => channel.Id)
            .ToArray();
        if (!reader.Channels.Values.Any(channel => channel.Topic == Recorder.Contracts.BrowserEvidenceChannels.Compositor))
        {
            return (RecordedImageFrames.None, RecordedCompositorValues.None);
        }
        var end = compositionNanoseconds + PresentationWindowNanoseconds;
        var records = new List<(long Time, ulong Sequence, Recorder.Contracts.RecorderEvent Event)>();
        foreach (var chunk in reader.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.StartTime > end)
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
                    if (logTime > end)
                    {
                        continue;
                    }
                    var stored = RecordingEventCodec.Decode(reader.ReadMessageAt(data, offset).Data.Span);
                    records.Add((stored.Event.MonotonicNanoseconds, stored.Event.Sequence, stored.Event));
                }
            }
        }
        var chooser = new RecordedImageFrameChooser(documentKey, frequency);
        var compositor = new RecordedCompositorValueChooser(documentKey, frequency);
        var latencies = new RecordedDisplayLatencyReader(documentKey);
        foreach (var record in records.OrderBy(item => item.Time).ThenBy(item => item.Sequence))
        {
            chooser.Add(record.Event);
            compositor.Add(record.Event);
            latencies.Add(record.Event);
        }
        // Sub-step 2b-iii change 2: the screen shows a compositor frame some
        // refreshes after it is presented, so the values are chosen as of the
        // composition less that display latency.
        var latency = latencies.At(compositionNanoseconds);
        var drawn = compositionNanoseconds - (latency?.Nanoseconds ?? 0);
        var frames = chooser.Choose(cutNanoseconds, drawn);
        var values = compositor.Choose(drawn);
        if (frames.ByUrl.Count > 0 || values.ByNode.Count > 0 || values.ScrollPositions.Count > 0)
        {
            frames = frames with { Notes = [latency?.Note ?? RecordedDisplayLatency.NoIntervalNote, .. frames.Notes] };
        }
        return (frames, values);
    }
}
