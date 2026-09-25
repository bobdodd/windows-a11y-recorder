using System.Security.Cryptography;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class NdjsonEventWriterTests
{
    [Fact]
    public async Task WritesOneCompleteJsonObjectPerLine()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "events.ndjson");
        var descriptor = CollectorDescriptor.Create(
            "test.collector",
            "test",
            "1.0",
            ["test.events"],
            "test");

        await using (var writer = new NdjsonEventWriter(path, capacity: 16))
        {
            for (ulong sequence = 0; sequence < 5; sequence++)
            {
                var accepted = writer.TryWrite(RecorderEventFactory.Create(
                    "session",
                    descriptor,
                    "test.events",
                    sequence,
                    (long)sequence,
                    "test",
                    new { value = sequence }));
                Assert.True(accepted);
            }
        }

        var lines = await File.ReadAllLinesAsync(
            path,
            TestContext.Current.CancellationToken);
        Assert.Equal(5, lines.Length);

        for (var index = 0; index < lines.Length; index++)
        {
            using var document = JsonDocument.Parse(lines[index]);
            Assert.Equal((ulong)index, document.RootElement.GetProperty("sequence").GetUInt64());
        }

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task ReportsHashOfWrittenBytesWhenDisposed()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "events.ndjson");
        var descriptor = CollectorDescriptor.Create(
            "test.collector",
            "test",
            "1.0",
            ["test.events"],
            "test");
        var registry = new ArtifactHashRegistry();

        await using (var writer = new NdjsonEventWriter(
            path,
            capacity: 64,
            artifactHashes: registry))
        {
            for (ulong sequence = 0; sequence < 50; sequence++)
            {
                Assert.True(writer.TryWrite(RecorderEventFactory.Create(
                    "session",
                    descriptor,
                    "test.events",
                    sequence,
                    (long)sequence,
                    "test",
                    new { value = sequence })));
            }
        }

        var bytes = await File.ReadAllBytesAsync(
            path,
            TestContext.Current.CancellationToken);
        Assert.True(registry.TryGetUnchanged(path, out var hash));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            hash);

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task PreservesRecordBoundariesUnderLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "events.ndjson");
        var descriptor = CollectorDescriptor.Create(
            "test.collector",
            "test",
            "1.0",
            ["test.events"],
            "test");
        const int recordCount = 5_000;

        await using (var writer = new NdjsonEventWriter(path, capacity: recordCount))
        {
            for (var sequence = 0; sequence < recordCount; sequence++)
            {
                Assert.True(writer.TryWrite(RecorderEventFactory.Create(
                    "session",
                    descriptor,
                    "test.events",
                    (ulong)sequence,
                    sequence,
                    "test",
                    new
                    {
                        sequence,
                        text = "A value with a newline\nand \"quoted\" text."
                    })));
            }
        }

        var lines = await File.ReadAllLinesAsync(
            path,
            TestContext.Current.CancellationToken);
        Assert.Equal(recordCount, lines.Length);

        for (var index = 0; index < lines.Length; index++)
        {
            using var document = JsonDocument.Parse(lines[index]);
            Assert.Equal(
                index,
                document.RootElement.GetProperty("payload").GetProperty("sequence").GetInt32());
        }

        Directory.Delete(directory, recursive: true);
    }
}
