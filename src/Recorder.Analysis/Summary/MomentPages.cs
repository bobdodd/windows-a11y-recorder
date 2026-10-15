using System.Globalization;
using Recorder.Database.RecordingFiles;
using Recorder.Recreation;
using Recorder.Session;

namespace Recorder.Analysis.Summary;

/// <summary>
/// One tile of a recreated page: its number from 1, its top, height, and
/// width in the page's CSS pixels, its picture file, relative to the
/// session folder and absolute, and the picture's size in pixels.
/// </summary>
public sealed record PageTile(int Number, double Top, double Height, double Width, string Path, string AbsolutePath, int PixelWidth, int PixelHeight);

/// <summary>
/// The page of a moment as recreated: its tiles, top to bottom, covering
/// the whole page; its CSS size; the part in view, as the recreation was
/// scrolled to the recorded position; its images and the address of each
/// not loaded; the requests refused, as no answer was recorded; and
/// whether it was reported built. Where it could not be recreated, it has
/// no tiles and <see cref="NotRecreated"/> says why.
/// </summary>
public sealed record RecreatedPage(
    IReadOnlyList<PageTile> Tiles,
    double Width,
    double Height,
    double ScrollX,
    double ScrollY,
    double ViewportWidth,
    double ViewportHeight,
    int Images,
    IReadOnlyList<string> ImagesNotLoaded,
    int RefusedRequests,
    bool Built,
    string? NotRecreated)
{
    public static RecreatedPage Not(string reason) => new([], 0, 0, 0, 0, 0, 0, 0, [], 0, false, reason);
}

/// <summary>
/// Recreates the page of a moment and pictures it. The recorded
/// implementation runs the instrumented Chromium without a window; the
/// tests give their own.
/// </summary>
public interface IMomentPages
{
    /// <param name="folder">The folder for the tile pictures, absolute.</param>
    /// <param name="relativeFolder">The same folder relative to the session folder, with forward slashes.</param>
    /// <param name="prefix">The start of each tile's file name, such as visit-0001-moment-07.</param>
    Task<RecreatedPage> RecreateAsync(Visit visit, Moment moment, string folder, string relativeFolder, string prefix, CancellationToken cancellationToken);
}

/// <summary>
/// The page of each moment, rebuilt from the recording as the player's
/// Inspect page at this frame rebuilds it, at the moment's frame, in the
/// instrumented Chromium without a window (checked on the target machine,
/// 2026-10-14: recording-summary.md, "The check of the recreation without
/// a window"), and pictured in tiles the height of the viewport, the whole
/// page, at full size. See recording-summary.md, decision 7.
/// </summary>
public sealed class RecordedMomentPages(
    RecordingFileDocuments documents,
    SessionPlaybackArchive archive,
    string chromium,
    string recreationsDirectory) : IMomentPages
{
    public static readonly TimeSpan BuildTimeout = RecreationCheck.BuildTimeout;

    public async Task<RecreatedPage> RecreateAsync(Visit visit, Moment moment, string folder, string relativeFolder, string prefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(moment);
        if (!File.Exists(chromium))
        {
            return RecreatedPage.Not($"the instrumented Chromium was not found at {chromium}");
        }

        if (visit.Start.DocumentToken is not { } token)
        {
            return RecreatedPage.Not("the visit's document was not recorded with a token, so its page at the frame cannot be found");
        }

        var time = moment.Frame.Time;
        var choice = documents.At(time, cancellationToken)
            .FirstOrDefault(item => item.Key == token || item.Key.StartsWith(token + " ", StringComparison.Ordinal));
        if (choice is null)
        {
            return RecreatedPage.Not("the visit's page is not among the pages recorded at the frame");
        }

        var directory = Path.Combine(recreationsDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            var timings = new List<RecreationTiming>();
            var content = RecordedPageAtFrame.Content(documents, archive.BrowserPreferences, choice, time, RecreationCheck.FormatTime, timings, cancellationToken);
            await using var session = await RecreationSession.OpenAsync(
                chromium, directory, content, cancellationToken, [RecreationCheck.WithoutWindowArgument], timings, devTools: false).ConfigureAwait(false);
            var built = await session.WaitForBuildAsync(BuildTimeout, cancellationToken).ConfigureAwait(false);
            var info = await session.PageInfoAsync(cancellationToken).ConfigureAwait(false);
            if (info.ContentWidth <= 0 || info.ContentHeight <= 0 || info.ViewportHeight <= 0)
            {
                return RecreatedPage.Not(string.Create(CultureInfo.InvariantCulture,
                    $"the recreated page measured {info.ContentWidth} by {info.ContentHeight} CSS pixels with a viewport {info.ViewportHeight} high, so it could not be pictured"));
            }

            Directory.CreateDirectory(folder);
            var tiles = new List<PageTile>();
            foreach (var (top, height) in Tiles(info.ContentHeight, info.ViewportHeight))
            {
                var png = await session.CaptureAsync(0, top, info.ContentWidth, height, cancellationToken).ConfigureAwait(false);
                var name = $"{prefix}-tile-{(tiles.Count + 1).ToString("D2", CultureInfo.InvariantCulture)}.png";
                var path = Path.Combine(folder, name);
                await File.WriteAllBytesAsync(path, png, cancellationToken).ConfigureAwait(false);
                var (width, pixels) = RecreationCheck.PngSize(png);
                tiles.Add(new PageTile(tiles.Count + 1, top, height, info.ContentWidth, relativeFolder + "/" + name, path, width, pixels));
            }

            return new RecreatedPage(tiles, info.ContentWidth, info.ContentHeight, info.ScrollX, info.ScrollY, info.ViewportWidth, info.ViewportHeight,
                info.Images, info.ImagesNotLoaded, session.RefusedRequests, built, null);
        }
#pragma warning disable CA1031 // A page that cannot be recreated is described from its frame alone, and the reason given.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return RecreatedPage.Not($"the recreation failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
#pragma warning disable CA1031 // The recreation's own folder is left if it cannot be removed.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
#pragma warning restore CA1031
            {
            }
        }
    }

    /// <summary>The tiles of a page: the height of the viewport each, from the top, the last cut at the page's foot.</summary>
    public static IEnumerable<(double Top, double Height)> Tiles(double pageHeight, double viewportHeight)
    {
        for (var top = 0.0; top < pageHeight; top += viewportHeight)
        {
            yield return (top, Math.Min(viewportHeight, pageHeight - top));
        }
    }
}

/// <summary>
/// How a recreated page's tiles are sent to the model: in groups, each
/// request holding as many as fit its image token budget, and at least one.
/// </summary>
public static class TileGroups
{
    /// <summary>
    /// The model's image tokens for a picture, as llama.cpp's image
    /// add-on counts them for Qwen-VL models: one token for each 32 by 32
    /// pixels, a patch of 16 pixels merged two by two, each side rounded
    /// up; a picture of up to 4,096 tokens is not made smaller
    /// (https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/mtmd/clip.cpp,
    /// set_limit_image_tokens(8, 4096) and n_merge 2 for QWEN3VL).
    /// </summary>
    public static int ImageTokens(int width, int height) =>
        (int)(Math.Ceiling(width / 32.0) * Math.Ceiling(height / 32.0));

    public static IReadOnlyList<IReadOnlyList<PageTile>> Group(IReadOnlyList<PageTile> tiles, int imageTokensPerRequest)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        var groups = new List<IReadOnlyList<PageTile>>();
        var current = new List<PageTile>();
        var used = 0;
        foreach (var tile in tiles)
        {
            var tokens = ImageTokens(tile.PixelWidth, tile.PixelHeight);
            if (current.Count > 0 && used + tokens > imageTokensPerRequest)
            {
                groups.Add(current);
                current = [];
                used = 0;
            }

            current.Add(tile);
            used += tokens;
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups;
    }
}
