using System.Collections.Concurrent;

namespace Recorder.Contracts;

/// <summary>
/// Receives the SHA-256 hash of a session file from the component that wrote
/// it, computed over the bytes as they were written.
/// </summary>
public interface IArtifactHashRegistry
{
    /// <summary>
    /// Records the hash of a file the caller has finished writing and closed.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="sizeBytes">The number of bytes the caller wrote.</param>
    /// <param name="sha256">The SHA-256 hash of those bytes.</param>
    void Record(string path, long sizeBytes, ReadOnlySpan<byte> sha256);
}

/// <summary>
/// Holds hashes computed while session files were written, so the manifest
/// can list them without reading each file again at stop.
/// </summary>
/// <remarks>
/// A recorded hash is used only while the file still has the size the writer
/// reported and the last-write time it had when the hash was recorded. A file
/// that was changed afterwards, or that no writer reported, is hashed from
/// disk instead.
/// </remarks>
public sealed class ArtifactHashRegistry : IArtifactHashRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _entries.Count;

    public void Record(string path, long sizeBytes, ReadOnlySpan<byte> sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (sha256.Length != 32)
        {
            throw new ArgumentException(
                "A SHA-256 hash is 32 bytes.",
                nameof(sha256));
        }

        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists || file.Length != sizeBytes)
        {
            // The file on disk is not what the writer describes, so the hash
            // cannot stand for it. The inventory hashes it from disk.
            _entries.TryRemove(fullPath, out _);
            return;
        }

        _entries[fullPath] = new Entry(
            sizeBytes,
            file.LastWriteTimeUtc,
            Convert.ToHexString(sha256).ToLowerInvariant());
    }

    /// <summary>
    /// Returns the recorded hash, as lowercase hexadecimal, when the file is
    /// unchanged since it was recorded.
    /// </summary>
    public bool TryGetUnchanged(string path, out string sha256)
    {
        sha256 = string.Empty;
        var fullPath = Path.GetFullPath(path);
        if (!_entries.TryGetValue(fullPath, out var entry))
        {
            return false;
        }

        var file = new FileInfo(fullPath);
        if (!file.Exists ||
            file.Length != entry.SizeBytes ||
            file.LastWriteTimeUtc != entry.LastWriteUtc)
        {
            return false;
        }

        sha256 = entry.Sha256;
        return true;
    }

    private sealed record Entry(long SizeBytes, DateTime LastWriteUtc, string Sha256);
}
