using System.Globalization;
using System.Text.Json;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>A recording opened for playback from its recording file.</summary>
/// <param name="Incomplete">
/// Why the file was read without its summary, as
/// <see cref="RecordingFileReader.Incomplete"/>, or null.
/// </param>
/// <param name="IndexDerived">
/// Why the playback index was derived by reading every chunk rather than
/// read from the file, or null when it was read from the file.
/// </param>
public sealed record RecordingFilePlaybackResult(
    SessionPlaybackArchive Archive,
    string? Incomplete,
    string? IndexDerived)
{
    /// <summary>The recording's browser documents, for recreating a page at a frame.</summary>
    public RecordingFileDocuments? Documents { get; init; }
}

/// <summary>
/// Opens a recording for playback from its recording file. The file's
/// summary and its playback index are read when it opens; the timeline and
/// complete records are read from the file's chunks as playback needs them.
/// A file without a usable playback index, such as a recording the app did
/// not finish, has its index derived by reading every chunk once.
/// </summary>
public static class RecordingFilePlayback
{
    /// <summary>The name of the attachment that holds the playback index.</summary>
    public const string IndexAttachment = "playback-index";

    public const string IndexMediaType = "application/json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static byte[] SerializeIndex(PlaybackIndex index) =>
        JsonSerializer.SerializeToUtf8Bytes(index, JsonOptions);

    /// <summary>
    /// Opens the recording file of the session folder. The folder must hold
    /// manifest.json; frames and audio are read from it.
    /// </summary>
    public static Task<RecordingFilePlaybackResult> OpenAsync(
        string sessionDirectory,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(() => OpenCoreAsync(sessionDirectory, filePath, cancellationToken), cancellationToken);
    }

    private static async Task<RecordingFilePlaybackResult> OpenCoreAsync(
        string sessionDirectory,
        string filePath,
        CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        var reader = RecordingFileReader.Open(filePath);
        try
        {
            var (index, derived) = ReadIndex(reader, cancellationToken);
            var builder = new SessionPlaybackArchiveBuilder(root, retainEvents: false);
            builder.AddIndex(index);
            var timeline = default(RecordingFileTimeline);
            var archive = await builder.BuildAsync(new DeferredRecordSource(() => timeline), cancellationToken)
                .ConfigureAwait(false);
            timeline = new RecordingFileTimeline(
                reader,
                index.ChannelCounts,
                OccupancyBitmap.Import(index.Occupancy).ToOccupancy(archive.DurationNanoseconds));
            var shown = archive.Frames.Select(frame => frame.MonotonicNanoseconds).ToHashSet();
            archive = archive with
            {
                BrowserNavigations = archive.BrowserNavigations.Count == 0
                    ? archive.BrowserNavigations
                    : BrowserNavigationFrames.Apply(
                        archive.BrowserNavigations,
                        index.PresentedCheckpoints,
                        index.FrameCompositions.Where(frame => shown.Contains(frame.FrameNanoseconds))),
                Timeline = timeline,
                RecordSource = timeline
            };
            return new RecordingFilePlaybackResult(archive, reader.Incomplete, derived)
            {
                Documents = new RecordingFileDocuments(reader, index)
            };
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    // The index stored in the file, or one derived from its chunks, and why
    // it was derived.
    internal static (PlaybackIndex Index, string? Derived) ReadIndex(
        RecordingFileReader reader,
        CancellationToken cancellationToken)
    {
        string reason;
        try
        {
            var bytes = reader.ReadAttachment(IndexAttachment);
            var stored = bytes is null ? null : JsonSerializer.Deserialize<PlaybackIndex>(bytes, JsonOptions);
            reason =
                stored is null ? "The recording file holds no playback index." :
                stored.Version != PlaybackIndex.CurrentVersion ? $"The recording file's playback index is version {stored.Version}." :
                !stored.BrowserCountsExact ? "The recording file's browser counts are not exact." :
                string.Empty;
            if (reason.Length == 0)
            {
                return (stored!, null);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            reason = $"The recording file's playback index cannot be read: {exception.Message}";
        }

        return (DeriveIndex(reader, cancellationToken), reason);
    }

    /// <summary>
    /// Derives the playback index by reading every chunk of the file, on all
    /// processors. The browser chunks are read twice: first for the
    /// navigation starts, so every browser event is counted in its segment
    /// when it is read.
    /// </summary>
    public static PlaybackIndex DeriveIndex(RecordingFileReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var frequency = reader.Metadata.TryGetValue("recording", out var recording) &&
            recording.TryGetValue("clockFrequency", out var text) &&
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : throw new InvalidDataException("The recording file does not state its clock frequency.");
        var chunks = reader.Chunks
            .Where(chunk => chunk.Stream is not ("recorder" or RecordingFileStateRecorder.SnapshotStream or RecordingFileStateRecorder.IndexStream))
            .ToArray();
        var builder = new PlaybackIndexBuilder(frequency, TimeSpan.Zero);
        var starts = new List<long>();
        ForEachGroup(
            reader,
            chunks.Where(chunk => chunk.Stream == "browser").ToArray(),
            topic => topic == "browser.navigation",
            stored =>
            {
                if (PlaybackIndexBuilder.IsNavigationStart(stored.Event))
                {
                    starts.Add(stored.Event.MonotonicNanoseconds);
                }
            },
            cancellationToken);
        builder.AddNavigationStarts(starts);
        ForEachGroup(
            reader,
            chunks,
            RecordingFileBatchTarget.IsEventTopic,
            stored => builder.Add(stored.EventKey, stored.Event),
            cancellationToken);
        return builder.Build();
    }

    // Decodes chunks in groups, on all processors, and passes their events
    // on in file order, so only one group's events are held at a time.
    private static void ForEachGroup(
        RecordingFileReader reader,
        RecordingFileChunk[] chunks,
        Func<string, bool> topics,
        Action<StoredEvent> add,
        CancellationToken cancellationToken)
    {
        var group = Math.Max(1, Environment.ProcessorCount) * 2;
        for (var start = 0; start < chunks.Length; start += group)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decoded = new List<StoredEvent>[Math.Min(group, chunks.Length - start)];
            Parallel.For(
                0,
                decoded.Length,
                new ParallelOptions { CancellationToken = cancellationToken },
                slot =>
                {
                    var events = new List<StoredEvent>();
                    foreach (var message in reader.ReadChunk(chunks[start + slot]))
                    {
                        if (topics(message.Channel.Topic))
                        {
                            events.Add(RecordingEventCodec.Decode(message.Data.Span));
                        }
                    }

                    decoded[slot] = events;
                });
            foreach (var events in decoded)
            {
                foreach (var stored in events)
                {
                    add(stored);
                }
            }
        }
    }

    // The archive is built before the timeline that reads its records.
    private sealed class DeferredRecordSource(Func<ISessionEventRecordSource?> source) : ISessionEventRecordSource
    {
        public string ReadEventJson(SessionTimelineEvent item) =>
            (source() ?? throw new InvalidOperationException("The recording file is not open yet.")).ReadEventJson(item);
    }
}
