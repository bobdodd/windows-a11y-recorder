using System.Security.Cryptography;
using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class ArtifactHashRegistryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        Guid.NewGuid().ToString("N"));

    public ArtifactHashRegistryTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ReturnsRecordedHashForUnchangedFile()
    {
        var path = Write("a.bin", [1, 2, 3]);
        var registry = new ArtifactHashRegistry();

        registry.Record(path, 3, SHA256.HashData(new byte[] { 1, 2, 3 }));

        Assert.True(registry.TryGetUnchanged(path, out var hash));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant(),
            hash);
    }

    [Fact]
    public void RejectsHashAfterFileChanges()
    {
        var path = Write("a.bin", [1, 2, 3]);
        var registry = new ArtifactHashRegistry();
        registry.Record(path, 3, SHA256.HashData(new byte[] { 1, 2, 3 }));

        File.AppendAllText(path, "x");

        Assert.False(registry.TryGetUnchanged(path, out _));
    }

    [Fact]
    public void RejectsHashAfterSameSizeRewrite()
    {
        var path = Write("a.bin", [1, 2, 3]);
        var registry = new ArtifactHashRegistry();
        registry.Record(path, 3, SHA256.HashData(new byte[] { 1, 2, 3 }));

        File.WriteAllBytes(path, [4, 5, 6]);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));

        Assert.False(registry.TryGetUnchanged(path, out _));
    }

    [Fact]
    public void IgnoresRecordWhoseSizeDoesNotMatchFile()
    {
        var path = Write("a.bin", [1, 2, 3]);
        var registry = new ArtifactHashRegistry();

        registry.Record(path, 2, SHA256.HashData(new byte[] { 1, 2 }));

        Assert.False(registry.TryGetUnchanged(path, out _));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void ReturnsNothingForUnreportedFile()
    {
        var path = Write("a.bin", [1, 2, 3]);

        Assert.False(new ArtifactHashRegistry().TryGetUnchanged(path, out _));
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
