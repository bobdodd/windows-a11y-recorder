using System.Net;

namespace Recorder.Analysis.Setup;

/// <summary>
/// A model file named by its Hugging Face address, for a model that is not
/// in the list: its size and SHA-256 are read from Hugging Face's answer to
/// a HEAD request (X-Linked-Size and X-Linked-ETag), and the address is
/// fixed to the repository's commit at that moment (X-Repo-Commit), so the
/// file downloaded is the one whose hash was read.
/// </summary>
public static class HuggingFaceFile
{
    /// <summary>
    /// The download address for a page or download address of a file:
    /// https://huggingface.co/owner/repository/blob/revision/file.gguf or
    /// .../resolve/revision/file.gguf. Null for any other address.
    /// </summary>
    public static Uri? DownloadAddress(string? text)
    {
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(url.Host, "huggingface.co", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 5 || segments[2] is not ("blob" or "resolve") ||
            !segments[^1].EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        segments[2] = "resolve";
        return new Uri("https://huggingface.co/" + string.Join('/', segments));
    }

    /// <summary>The address with its revision replaced by a commit.</summary>
    public static Uri AtCommit(Uri address, string commit)
    {
        ArgumentNullException.ThrowIfNull(address);
        var segments = address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        segments[3] = commit;
        return new Uri("https://huggingface.co/" + string.Join('/', segments));
    }

    /// <summary>
    /// Asks Hugging Face for the file's size, SHA-256 and commit. The
    /// client must not follow redirects, since the headers are on the
    /// first answer.
    /// </summary>
    public static async Task<CatalogFile> DescribeAsync(HttpClient noRedirects, Uri address, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(noRedirects);
        ArgumentNullException.ThrowIfNull(address);
        using var request = new HttpRequestMessage(HttpMethod.Head, address);
        using var response = await noRedirects.SendAsync(request, cancellation).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException("Hugging Face asks for a sign-in for this file, for example for a model whose licence must be accepted on its page. Only files that can be downloaded without signing in can be added here.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new HttpRequestException("Hugging Face has no file at this address.");
        }

        if ((int)response.StatusCode >= 400)
        {
            throw new HttpRequestException($"Hugging Face answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var size = Header(response, "X-Linked-Size");
        var sha = Header(response, "X-Linked-ETag")?.Trim('"').ToLowerInvariant();
        var commit = Header(response, "X-Repo-Commit");
        if (!long.TryParse(size, out var bytes) || bytes < 1 || sha is not { Length: 64 } || !sha.All(Uri.IsHexDigit))
        {
            throw new HttpRequestException("Hugging Face did not give this file's size and SHA-256, so it cannot be checked after the download.");
        }

        var pinned = commit is { Length: 40 } && commit.All(Uri.IsHexDigit) ? AtCommit(address, commit) : address;
        return new CatalogFile(Uri.UnescapeDataString(address.Segments[^1]), pinned, bytes, sha);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
