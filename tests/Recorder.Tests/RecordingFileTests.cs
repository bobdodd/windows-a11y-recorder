using System.Text;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// The recording file: every event written is read back with its envelope
/// and its payload text unchanged, chunks are grouped by stream and checked,
/// and a file cut short is read up to its last whole chunk.
/// </summary>
public sealed class RecordingFileTests : IDisposable
{
    private const string SessionId = "session-a";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recording-file-" + Guid.NewGuid().ToString("N"));

    public RecordingFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly IReadOnlyDictionary<string, string> Recording =
        new Dictionary<string, string> { ["sessionKey"] = SessionId };

    [Fact]
    public async Task ReadsBackEveryEventWithItsPayloadTextUnchanged()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector("test.browser", "browser.layout", "input.keyboard", "graphics.desktop.frames");
        // Payload text the recorder must keep as received: escapes, a NUL in
        // a string, property order, a duplicate key, and exponent notation.
        const string unusual =
            "{\"z\":1,\"a\":\"line\\nbreak \\u0000 nul \\u00e9\",\"a\":2,\"n\":1.50E+3,\"deep\":{\"list\":[1,2,{\"x\":null}]}}";
        var events = new List<RecorderEvent>();
        for (ulong sequence = 1; sequence <= 600; sequence++)
        {
            var channel = (sequence % 3) switch
            {
                0 => "browser.layout",
                1 => "input.keyboard",
                _ => "graphics.desktop.frames"
            };
            var record = Event(SessionId, collector, sequence, (long)sequence * 1_000, channel);
            if (sequence % 50 == 0)
            {
                record = record with { Payload = Json(unusual) };
            }

            events.Add(record);
        }

        using (var target = new RecordingFileBatchTarget(
                   path,
                   Recording,
                   new RecordingFileWriterOptions { ChunkBytes = 16 * 1024 }))
        {
            for (var start = 0; start < events.Count; start += 100)
            {
                var batch = events.Skip(start).Take(100)
                    .Select((record, index) => new BufferedEvent(start + index, record, record.Payload.GetRawText()))
                    .ToArray();
                Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
            }

            target.Finish();
            Assert.Equal(600, target.MessageCount);
        }

        using var reader = RecordingFileReader.Open(path);
        Assert.True(reader.HasSummary);
        Assert.Null(reader.Incomplete);
        Assert.Equal(SessionId, reader.Metadata["recording"]["sessionKey"]);
        Assert.True(reader.Chunks.Count > 3);
        Assert.Equal(["browser", "desktop", "media"], reader.Chunks.Select(chunk => chunk.Stream).Distinct().Order());

        var stored = new List<StoredEvent>();
        foreach (var chunk in reader.Chunks)
        {
            foreach (var message in reader.ReadChunk(chunk))
            {
                Assert.Equal(chunk.Stream, message.Channel.Stream);
                var decoded = RecordingEventCodec.Decode(message.Data.Span);
                Assert.Equal(decoded.Event.Channel, message.Channel.Topic);
                Assert.Equal(decoded.Event.MonotonicNanoseconds, message.LogTime);
                Assert.InRange(message.LogTime, chunk.StartTime, chunk.EndTime);
                stored.Add(decoded);
            }
        }

        Assert.Equal(Enumerable.Range(0, 600).Select(key => (long)key), stored.Select(item => item.EventKey).Order());
        foreach (var item in stored)
        {
            var expected = events[(int)item.EventKey];
            Assert.Equal(expected.Payload.GetRawText(), item.Event.Payload.GetRawText());
            Assert.Equal(expected.EventId, item.Event.EventId);
            Assert.Equal(expected.Sequence, item.Event.Sequence);
            Assert.Equal(expected.CollectorInstanceId, item.Event.CollectorInstanceId);
            Assert.Equal(expected.ObservedUtc, item.Event.ObservedUtc);
            Assert.Equal(expected.QualityFlags, item.Event.QualityFlags);
        }

        Assert.Contains(stored, item => item.Event.Payload.GetRawText() == unusual);

        // Within a stream, messages are in the order they were written.
        foreach (var stream in reader.Chunks.GroupBy(chunk => chunk.Stream))
        {
            var keys = stream.SelectMany(chunk => reader.ReadChunk(chunk))
                .Select(message => RecordingEventCodec.Decode(message.Data.Span).EventKey)
                .ToArray();
            Assert.Equal(keys.Order(), keys);
        }
    }

    [Fact]
    public async Task ReadsAFileCutShortUpToItsLastWholeChunk()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector();
        using (var target = new RecordingFileBatchTarget(
                   path,
                   Recording,
                   new RecordingFileWriterOptions { ChunkBytes = 2 * 1024 }))
        {
            var batch = Enumerable.Range(0, 40)
                .Select(index =>
                {
                    var record = Event(SessionId, collector, (ulong)index + 1, index * 10L);
                    return new BufferedEvent(index, record, record.Payload.GetRawText());
                })
                .ToArray();
            await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken);
            target.Finish();
        }

        var whole = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        IReadOnlyList<RecordingFileChunk> chunks;
        int[] counts;
        using (var reader = RecordingFileReader.Open(path))
        {
            chunks = reader.Chunks;
            counts = chunks.Select(chunk => reader.ReadChunk(chunk).Count()).ToArray();
        }

        Assert.True(chunks.Count >= 3);
        var last = chunks[^1];
        var cut = Path.Combine(_directory, "cut.mcap");
        // Every length from the start of the last chunk to one byte short
        // of the whole file.
        for (var length = (int)last.Offset; length < whole.Length; length++)
        {
            await File.WriteAllBytesAsync(cut, whole.AsMemory(0, length).ToArray(), TestContext.Current.CancellationToken);
            using var reader = RecordingFileReader.Open(cut);
            Assert.False(reader.HasSummary);
            var lastWhole = length >= last.Offset + last.Length;
            Assert.Equal(lastWhole ? chunks.Count : chunks.Count - 1, reader.Chunks.Count);
            var messages = reader.ReadAll().Count();
            Assert.Equal(
                counts.Take(reader.Chunks.Count).Sum(),
                messages);
            Assert.NotNull(reader.Incomplete);
        }

        Assert.Equal(40, counts.Sum());
    }

    [Fact]
    public async Task AChunkWhoseBytesChangedIsNotRead()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector();
        using (var target = new RecordingFileBatchTarget(path, Recording))
        {
            var record = Event(SessionId, collector, 1, 10);
            await target.WriteAsync(
                new EventBatch([new BufferedEvent(0, record, record.Payload.GetRawText())], [], []),
                TestContext.Current.CancellationToken);
            target.Finish();
        }

        RecordingFileChunk chunk;
        using (var reader = RecordingFileReader.Open(path))
        {
            chunk = Assert.Single(reader.Chunks);
        }

        // Changing the chunk's stated CRC-32 makes its records fail the check.
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        bytes[chunk.Offset + 9 + 24] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        using (var reader = RecordingFileReader.Open(path))
        {
            var exception = Assert.Throws<InvalidDataException>(() => reader.ReadAll().ToArray());
            Assert.Contains("CRC-32", exception.Message);
        }
    }

    [Fact]
    public async Task ABatchWrittenAgainAddsNothingTwice()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector();
        using (var target = new RecordingFileBatchTarget(path, Recording))
        {
            var record = Event(SessionId, collector, 1, 10);
            var batch = new EventBatch(
                [new BufferedEvent(0, record, record.Payload.GetRawText())],
                [new WriterRejection(0, "event-sequence-not-increasing", "test.channel", "test-event", 1)],
                [new WriterOmission(0, 20, 30, 4)]);
            await target.WriteAsync(batch, TestContext.Current.CancellationToken);
            await target.WriteAsync(batch, TestContext.Current.CancellationToken);
            target.Finish();
        }

        using var reader = RecordingFileReader.Open(path);
        var messages = reader.ReadAll().ToArray();
        Assert.Single(messages, message => message.Channel.Topic == "test.channel");
        var writer = messages.Where(message => message.Channel.Topic == RecordingFileBatchTarget.WriterTopic)
            .Select(message => JsonDocument.Parse(message.Data).RootElement.GetProperty("kind").GetString()!)
            .ToArray();
        Assert.Equal(["writer-rejection", "writer-omission"], writer);
        Assert.All(
            messages.Where(message => message.Channel.Topic == RecordingFileBatchTarget.WriterTopic),
            message => Assert.Equal("recorder", message.Channel.Stream));
    }

    [Fact]
    public async Task AQuietStreamIsWrittenWithinTheChunkInterval()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector();
        using var target = new RecordingFileBatchTarget(
            path,
            Recording,
            new RecordingFileWriterOptions { ChunkInterval = TimeSpan.FromMilliseconds(200) });
        var writer = new RecordingEventWriter(
            target,
            SessionId,
            new RecordingEventWriterOptions
            {
                SpillPath = Path.Combine(_directory, "spill.ndjson"),
                WriterConnections = 1,
                BatchInterval = TimeSpan.FromMilliseconds(10)
            });
        Assert.True(writer.TryWrite(Event(SessionId, collector, 1, 10)));

        // No other event arrives; the writer still writes the chunk.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (target.Chunks.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Single(target.Chunks);
        await writer.CompleteAsync();
        target.Finish();
        using var reader = RecordingFileReader.Open(path);
        Assert.Single(reader.ReadAll());
    }

    [Fact]
    public async Task AFileWhoseSummaryChangedIsReadFromItsChunks()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector();
        using (var target = new RecordingFileBatchTarget(path, Recording))
        {
            var record = Event(SessionId, collector, 1, 10);
            await target.WriteAsync(
                new EventBatch([new BufferedEvent(0, record, record.Payload.GetRawText())], [], []),
                TestContext.Current.CancellationToken);
            target.Finish();
        }

        // The byte before the footer's record is the last byte of the
        // summary offset section, which the summary CRC-32 covers.
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        bytes[bytes.Length - 8 - 29 - 1] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        using var reader = RecordingFileReader.Open(path);
        Assert.False(reader.HasSummary);
        Assert.NotNull(reader.Incomplete);
        Assert.Single(reader.ReadAll());
        Assert.Equal(SessionId, reader.Metadata["recording"]["sessionKey"]);
    }

    [Fact]
    public async Task AFinishedFileIsClosedSoItCanBeOpenedAlone()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector();
        using var target = new RecordingFileBatchTarget(path, Recording);
        var record = Event(SessionId, collector, 1, 10);
        await target.WriteAsync(
            new EventBatch([new BufferedEvent(0, record, record.Payload.GetRawText())], [], []),
            TestContext.Current.CancellationToken);

        // While the file is being written, it cannot be opened without
        // sharing, as on Windows the app's hash of the session files could not.
        Assert.Throws<IOException>(() =>
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None).Dispose());

        target.Finish();
        using (var alone = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.True(alone.Length > 0);
        }

        using var reader = RecordingFileReader.Open(path);
        Assert.True(reader.HasSummary);
    }

    [Fact]
    public void StreamsFollowTheChannelNames()
    {
        Assert.Equal("browser", RecordingFileBatchTarget.StreamOf("browser.layout"));
        Assert.Equal("media", RecordingFileBatchTarget.StreamOf("graphics.desktop.frames"));
        Assert.Equal("media", RecordingFileBatchTarget.StreamOf("audio.microphone"));
        Assert.Equal("desktop", RecordingFileBatchTarget.StreamOf("input.mouse"));
        Assert.Equal("desktop", RecordingFileBatchTarget.StreamOf("accessibility.uia.events"));
        Assert.Equal("desktop", RecordingFileBatchTarget.StreamOf("session.annotations"));
    }

    [Fact]
    public void AFileWithoutTheMagicBytesIsNotOpened()
    {
        var path = Path.Combine(_directory, "not.mcap");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("not a recording file"));
        Assert.Throws<InvalidDataException>(() => RecordingFileReader.Open(path).Dispose());
    }
}
