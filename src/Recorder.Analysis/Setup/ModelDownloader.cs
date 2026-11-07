using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Recorder.Analysis.Setup;

/// <summary>Where a download or a check has got to.</summary>
/// <param name="Stage">"Downloading" or "Checking", the SHA-256 being computed.</param>
public sealed record DownloadProgress(string FileName, string Stage, long Done, long Total, double BytesPerSecond);

/// <summary>
/// Downloads model files: into a ".partial" file that a later download
/// continues from, with the request repeated after a dropped connection,
/// and the whole file's SHA-256 checked against the published one before
/// it is given its name. A file already there with the published size and
/// SHA-256 is not downloaded again.
/// </summary>
public sealed class ModelDownloader(HttpClient http)
{
    public const string PartialSuffix = ".partial";
    public const string HashSuffix = ".sha256";
    private const int BufferSize = 1 << 20;
    private const int Retries = 5;

    /// <summary>The wait before the request is repeated after a dropped connection: 2, 4, 8, 16, 32 s.</summary>
    public Func<int, TimeSpan> RetryDelay { get; init; } = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));

    /// <summary>Space kept free on the drive besides the file: 1 GB.</summary>
    public const long SpareSpace = 1L << 30;

    /// <summary>
    /// The folder a file goes in under the models folder: the Hugging Face
    /// repository it comes from, as owner\repository, since files in
    /// different repositories share names (mmproj-F16.gguf); else the
    /// address's host.
    /// </summary>
    public static string RelativeFolder(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parts = url.Host.EndsWith("huggingface.co", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2
            ? new[] { segments[0], segments[1] }
            : new[] { url.Host };
        return Path.Combine([.. parts.Select(Safe)]);
    }

    /// <summary>The path a file is downloaded to under the models folder.</summary>
    public static string TargetPath(string modelsFolder, CatalogFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Path.Combine(modelsFolder, RelativeFolder(file.Url), Safe(file.Name));
    }

    public async Task<string> DownloadAsync(
        CatalogFile file,
        string modelsFolder,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(file);
        var target = TargetPath(modelsFolder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            if (new FileInfo(target).Length == file.Size &&
                await IsCheckedAsync(target, file.Sha256, progress, cancellation).ConfigureAwait(false))
            {
                return target;
            }

            throw new IOException(
                $"{target} is already there, but it is not the published file (size {file.Size:N0} bytes, SHA-256 {file.Sha256}). Move or delete it, then download again.");
        }

        var partial = target + PartialSuffix;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await ContinueAsync(file, partial, progress, cancellation).ConfigureAwait(false);
                break;
            }
            catch (Exception exception) when (Transient(exception) && !cancellation.IsCancellationRequested && attempt < Retries)
            {
                await Task.Delay(RetryDelay(attempt), cancellation).ConfigureAwait(false);
            }
        }

        var sha = await Sha256Async(partial, file.Name, progress, cancellation).ConfigureAwait(false);
        if (sha != file.Sha256)
        {
            File.Delete(partial);
            throw new InvalidDataException(
                $"The downloaded {file.Name} has the SHA-256 {sha}, not the published {file.Sha256}, so it was deleted. Download it again.");
        }

        File.Move(partial, target);
        WriteHash(target, sha);
        return target;
    }

    // A dropped connection, a timeout or a server error is worth a second
    // try; a missing file, a refusal or a full drive is not.
    private static bool Transient(Exception exception) => exception switch
    {
        NotEnoughSpaceException => false,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } => (int)status >= 500 || status == HttpStatusCode.TooManyRequests,
        IOException => true,
        _ => false
    };

    private async Task ContinueAsync(CatalogFile file, string partial, IProgress<DownloadProgress>? progress, CancellationToken cancellation)
    {
        var have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (have > file.Size)
        {
            File.Delete(partial);
            have = 0;
        }

        if (have < file.Size)
        {
            var free = PcHardware.FreeSpace(partial);
            if (free > 0 && free < file.Size - have + SpareSpace)
            {
                throw new NotEnoughSpaceException(
                    $"{file.Name} needs {ByteSize.Gigabytes(file.Size - have)} more on the drive, with 1 GB to spare, and the drive has {ByteSize.Gigabytes(free)} free.");
            }
        }

        if (have == file.Size)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
        if (have > 0)
        {
            request.Headers.Range = new RangeHeaderValue(have, null);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partial);
            throw new IOException("The server would not continue the download from where it stopped, so it starts again.");
        }

        response.EnsureSuccessStatusCode();
        var append = have > 0 &&
                     response.StatusCode == HttpStatusCode.PartialContent &&
                     response.Content.Headers.ContentRange?.From == have;
        if (!append)
        {
            have = 0;
        }

        await using var output = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, useAsync: true);
        await using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        var buffer = new byte[BufferSize];
        var clock = Stopwatch.StartNew();
        var reported = TimeSpan.Zero;
        var startedAt = have;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            if (have + read > file.Size)
            {
                throw new InvalidDataException($"The server sent more than the published {file.Size:N0} bytes of {file.Name}.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
            have += read;
            if (clock.Elapsed - reported >= TimeSpan.FromMilliseconds(250))
            {
                reported = clock.Elapsed;
                progress?.Report(new DownloadProgress(file.Name, "Downloading", have, file.Size, (have - startedAt) / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));
            }
        }

        progress?.Report(new DownloadProgress(file.Name, "Downloading", have, file.Size, (have - startedAt) / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));
        if (have != file.Size)
        {
            throw new IOException($"The download of {file.Name} stopped at {have:N0} of {file.Size:N0} bytes.");
        }
    }

    /// <summary>
    /// True when the file's SHA-256 is the expected one: from the ".sha256"
    /// file written beside it when it was last checked, if that is newer
    /// than the file, else computed.
    /// </summary>
    private static async Task<bool> IsCheckedAsync(string path, string expected, IProgress<DownloadProgress>? progress, CancellationToken cancellation)
    {
        if (ReadHash(path) is { } known)
        {
            return known == expected;
        }

        var sha = await Sha256Async(path, Path.GetFileName(path), progress, cancellation).ConfigureAwait(false);
        if (sha == expected)
        {
            WriteHash(path, sha);
        }

        return sha == expected;
    }

    /// <summary>The SHA-256 recorded beside a file when it is newer than the file, else null.</summary>
    public static string? ReadHash(string path)
    {
        var hashPath = path + HashSuffix;
        try
        {
            if (!File.Exists(hashPath) || File.GetLastWriteTimeUtc(hashPath) < File.GetLastWriteTimeUtc(path))
            {
                return null;
            }

            // sha256sum's format: the hash, a space, "*" and the file name.
            var text = File.ReadAllText(hashPath).Trim();
            var sha = text.Split(' ', 2)[0].ToLowerInvariant();
            return sha.Length == 64 && sha.All(Uri.IsHexDigit) ? sha : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteHash(string path, string sha)
    {
        try
        {
            File.WriteAllText(path + HashSuffix, $"{sha} *{Path.GetFileName(path)}\n");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A file's SHA-256, lower-case hexadecimal, with progress.</summary>
    public static async Task<string> Sha256Async(string path, string name, IProgress<DownloadProgress>? progress, CancellationToken cancellation)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        var total = stream.Length;
        long done = 0;
        var clock = Stopwatch.StartNew();
        var reported = TimeSpan.Zero;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            done += read;
            if (clock.Elapsed - reported >= TimeSpan.FromMilliseconds(250))
            {
                reported = clock.Elapsed;
                progress?.Report(new DownloadProgress(name, "Checking", done, total, done / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));
            }
        }

        progress?.Report(new DownloadProgress(name, "Checking", done, total, done / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string Safe(string part)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':']).ToHashSet();
        var safe = new string([.. part.Select(character => invalid.Contains(character) ? '_' : character)]).Trim('.', ' ');
        return safe.Length == 0 ? "_" : safe;
    }

    /// <summary>The drive has too little space; repeating the request will not help.</summary>
    public sealed class NotEnoughSpaceException(string message) : IOException(message);

    /// <summary>A transfer rate as the window shows it, in MB of 1,048,576 bytes each second.</summary>
    public static string Rate(double bytesPerSecond) =>
        (bytesPerSecond / 1048576).ToString("0.0", CultureInfo.InvariantCulture) + " MB a second";
}
