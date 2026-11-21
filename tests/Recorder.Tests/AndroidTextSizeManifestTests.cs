using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class AndroidTextSizeManifestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreRecordingSnapshotIsOptionalAndRoundTrips(bool collect)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var file = Path.Combine(Path.GetTempPath(), $"recorder-{Guid.NewGuid():N}.json");
        var clock = DateTimeOffset.UtcNow;
        var snapshot = collect
            ? new AndroidTextSizeSnapshot("org.example.synthetic", clock.AddSeconds(-1), "partial",
                [new(0, null, false, "available", 34, 1), new(1, null, true, "not-advertised", null, null)])
            : null;
        var manifest = new SessionManifest(SessionSchemaVersions.Manifest, "synthetic", "completed", clock,
            clock.AddSeconds(1), 1_000_000_000, 100, 0, "test", "test", "test",
            new(false, false, false, false, 0, false, false), [], [], 0, 0, null, snapshot);
        try
        {
            await SessionManifestWriter.WriteAsync(file, manifest, cancellation);
            var json = await File.ReadAllTextAsync(file, cancellation);
            Assert.Equal(collect, json.Contains("\"androidPreRecordingTextSizes\"", StringComparison.Ordinal));
            var read = Assert.IsType<SessionManifest>(JsonSerializer.Deserialize<SessionManifest>(
                json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            if (snapshot is null)
            {
                Assert.Null(read.AndroidPreRecordingTextSizes);
            }
            else
            {
                var recorded = Assert.IsType<AndroidTextSizeSnapshot>(read.AndroidPreRecordingTextSizes);
                Assert.Equal(snapshot.PackageName, recorded.PackageName);
                Assert.Equal(snapshot.Status, recorded.Status);
                Assert.Equal(snapshot.ReceivedUtc, recorded.ReceivedUtc);
                Assert.True(recorded.ReceivedUtc < read.StartedUtc);
                Assert.Equal(snapshot.Nodes.AsEnumerable(), recorded.Nodes);
            }
        }
        finally
        {
            File.Delete(file);
        }
    }
}
