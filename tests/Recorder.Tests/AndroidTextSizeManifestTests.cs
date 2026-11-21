using System.Globalization;
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
            {
                ApkFonts = new(clock.AddSeconds(-2), "1.0", 7, 1, 1, 0, ["res/font/synthetic.ttf"], [], null)
            }
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
                Assert.Null(AndroidTextSizeChecksEvidence.Describe(read, CultureInfo.InvariantCulture));
            }
            else
            {
                var recorded = Assert.IsType<AndroidTextSizeSnapshot>(read.AndroidPreRecordingTextSizes);
                Assert.Equal(snapshot.PackageName, recorded.PackageName);
                Assert.Equal(snapshot.Status, recorded.Status);
                Assert.Equal(snapshot.ReceivedUtc, recorded.ReceivedUtc);
                Assert.True(recorded.ReceivedUtc < read.StartedUtc);
                Assert.Equal(snapshot.Nodes.AsEnumerable(), recorded.Nodes);
                Assert.Equal(7L, recorded.ApkFonts?.VersionCode);
                Assert.Equal(["res/font/synthetic.ttf"], recorded.ApkFonts?.FontFiles);
                Assert.Contains("Bundled font file: res/font/synthetic.ttf", AndroidTextSizeChecksEvidence.DescribeAppFonts(read));
                var evidence = Assert.IsType<AndroidTextSizeChecksEvidence>(
                    AndroidTextSizeChecksEvidence.Describe(read, CultureInfo.InvariantCulture));
                Assert.Contains("org.example.synthetic", evidence.Summary);
                Assert.Contains("1 of 2 nodes reported a size", evidence.Summary);
                Assert.Contains("partial snapshot", evidence.Summary);
                Assert.Contains(recorded.ReceivedUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture),
                    evidence.Summary);
                Assert.Contains("before this recording started", evidence.Summary);
                Assert.Contains("not a reading at the playhead or a result for later screens", evidence.Summary);
                Assert.Equal(
                    ["Node 0, no view ID: 34 px rendered; dp (1); Android visible to user: no.",
                     "Node 1, no view ID: size not advertised; Android visible to user: yes."],
                    evidence.NodeLines);
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ChecksEvidenceDoesNotGuessMissingUnitsOrCarryOverToAnotherRecording()
    {
        var receipt = new DateTimeOffset(2026, 1, 1, 13, 0, 0, TimeSpan.FromHours(1));
        var snapshot = new AndroidTextSizeSnapshot("org.example.synthetic", receipt, "complete",
            [new(2, "org.example.synthetic:id/synthetic_label", true, "available", 51.1875, null),
             new(3, null, true, "unavailable", null, null)]);
        var manifest = new SessionManifest(SessionSchemaVersions.Manifest, "synthetic", "completed", receipt.AddSeconds(1),
            receipt.AddSeconds(2), 1_000_000_000, 100, 0, "test", "test", "test",
            new(false, false, false, false, 0, false, false), [], [], 0, 0, null, snapshot);

        var evidence = Assert.IsType<AndroidTextSizeChecksEvidence>(
            AndroidTextSizeChecksEvidence.Describe(manifest, CultureInfo.GetCultureInfo("en-US")));
        Assert.Contains("2026-01-01 12:00:00.000 UTC", evidence.Summary);
        Assert.DoesNotContain("partial", evidence.Summary);
        Assert.Equal("Node 2, org.example.synthetic:id/synthetic_label: 51.1875 px rendered; unit not reported; Android visible to user: yes.",
            evidence.NodeLines[0]);
        Assert.Equal("Node 3, no view ID: size unavailable; Android visible to user: yes.", evidence.NodeLines[1]);
        Assert.Null(AndroidTextSizeChecksEvidence.Describe(
            manifest with { SessionId = "another-synthetic", AndroidPreRecordingTextSizes = null }, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0, 0, "The saved snapshot contains no nodes.")]
    [InlineData(2, 0, "No saved node advertised text-size data (2 checked).")]
    [InlineData(0, 2, "No size could be read from the 2 saved nodes.")]
    [InlineData(1, 1, "Of 2 saved nodes, 1 did not advertise text-size data and no size could be read from the other 1.")]
    public void ChecksEvidenceExplainsWhyAStoredSnapshotHasNoSizes(int notAdvertised, int unavailable, string expectedReason)
    {
        var receipt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var nodes = Enumerable.Range(0, notAdvertised + unavailable)
            .Select(index => new AndroidTextSizeNode(index, null, true,
                index < notAdvertised ? "not-advertised" : "unavailable", null, null))
            .ToArray();
        var snapshot = new AndroidTextSizeSnapshot("org.example.synthetic", receipt, "complete", nodes);
        var manifest = new SessionManifest(SessionSchemaVersions.Manifest, "synthetic", "completed", receipt.AddSeconds(1),
            receipt.AddSeconds(2), 1_000_000_000, 100, 0, "test", "test", "test",
            new(false, false, false, false, 0, false, false), [], [], 0, 0, null, snapshot);

        var evidence = Assert.IsType<AndroidTextSizeChecksEvidence>(
            AndroidTextSizeChecksEvidence.Describe(manifest, CultureInfo.InvariantCulture));
        Assert.Contains($"No text sizes available. {expectedReason}", evidence.Summary);
        Assert.Contains("Reply received at 2026-01-01 12:00:00.000 UTC", evidence.Summary);
        Assert.Equal(nodes.Length, evidence.NodeLines.Count);
    }
}
