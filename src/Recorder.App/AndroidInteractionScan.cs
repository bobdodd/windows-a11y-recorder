using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Recorder.Analysis;
using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.App;

internal static class AndroidInteractionScan
{
    private static readonly TimeSpan VisionBudget = TimeSpan.FromSeconds(55);

    public static async Task<AndroidInvestigationInventory> ReadAsync(AndroidInteractionSnapshot snapshot,
        Func<CancellationToken, Task<(ModelProfile? Profile, LocalServerSettings? Server, string? Problem)>> selection,
        IProgress<string> progress, CancellationToken token)
    {
        IReadOnlyList<(AndroidNodeBounds Box, string Label)>? proposals = null;
        var appTree = snapshot.App.Items!;
        var crop = snapshot.HasScreenCoordinates && snapshot.Frame is { } frame
            ? AndroidInteractionInventory.Intersection(snapshot.App.Window.Bounds, new(0, 0, frame.Width, frame.Height)) : null;
        string vision;
        if (crop is null)
        {
            vision = "The screenshot could not be mapped to the app viewport.";
        }
        else if (snapshot.Tree.Problem is not null || snapshot.Tree.EarlierUpdateMissing ||
            appTree.Truncated || appTree.MissingChildren > 0 || appTree.Nodes.Any(node => node.Password))
        {
            vision = "The screenshot was not sent because the Android tree was incomplete or contained a password field.";
        }
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(VisionBudget);
            try
            {
                var (profile, server, problem) = await selection(timeout.Token).ConfigureAwait(false);
                vision = problem ?? "No model is selected for vision-based checks.";
                if (profile is not null)
                {
                    progress.Report($"Reading the app screenshot with {profile.Name}. No phone input has been sent.");
                    proposals = await Task.Run(() => ProposeAsync(snapshot, crop, profile, server, timeout.Token), timeout.Token)
                        .ConfigureAwait(false);
                    vision = proposals is null ? $"{profile.Name} did not return a usable control list." :
                        $"{profile.Name} suggested {proposals.Count} possible controls from the screenshot.";
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                vision = $"The image model did not finish within {VisionBudget.TotalSeconds:0} seconds.";
            }
            catch (ModelServerBusyException) when (!token.IsCancellationRequested)
            {
                vision = "The recorder's local model server is in use by analysis. It was not interrupted.";
            }
            catch (Exception ex) when (!token.IsCancellationRequested && ex is (ModelEndpointException or
                IOException or HttpRequestException or JsonException or InvalidOperationException or ArgumentException
                or Npgsql.NpgsqlException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception))
            {
                // Endpoint errors can contain response text. Do not expose it in a
                // report (or log), as it may quote the image or contain credentials.
                vision = "The selected image model could not be used. Check the project's vision model settings and availability.";
            }
        }
        token.ThrowIfCancellationRequested();
        progress.Report(proposals is null ? "Reading declared controls from the starting Android tree." :
            "Matching screenshot suggestions to the starting Android tree.");
        var mapping = await Task.Run(() => AndroidInvestigationCandidates.Map(snapshot, proposals ?? []), token)
            .ConfigureAwait(false);
        var coverage = (proposals is null ? $"Visual discovery not assessed. {vision}" : vision) +
            $" {mapping.Matched} matched to the tree, {mapping.ModelOnly} model-only, {mapping.TreeOnly} tree-only." +
            (mapping.NotUsed > 0 ? $" {mapping.NotUsed} model suggestions were duplicates, covered, sensitive, or outside usable bounds." : "") +
            (proposals?.Count == AndroidInvestigationCandidates.ProposalLimit ? " The screenshot proposal limit was reached." : "") +
            (appTree.Truncated || appTree.MissingChildren > 0 ? " Android reported an incomplete tree." : "");
        return new(mapping.Candidates.OrderBy(item => item.Item.Boxes.FirstOrDefault()?.Top ?? int.MaxValue)
            .ThenBy(item => item.Item.Boxes.FirstOrDefault()?.Left ?? int.MaxValue).ToArray(), coverage);
    }

    private static async Task<IReadOnlyList<(AndroidNodeBounds Box, string Label)>?> ProposeAsync(
        AndroidInteractionSnapshot snapshot, AndroidNodeBounds crop, ModelProfile profile, LocalServerSettings? server,
        CancellationToken token)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var endpoint = new HttpModelEndpoint(client, new CurrentUserApiKeyProtector());
        // Use the same managed server lifetime as analysis. Never take over its
        // server or evict its loaded model in order to run a live scan.
        await using var host = profile.Local is null ? null :
            new LocalServerHost(new AnalysisSettings([profile], server, 1), AnalysisLocation.Directory, client, waitForLease: false);
        if (host is not null)
        {
            await host.EnsureStartedAsync(token).ConfigureAwait(false);
            await endpoint.LoadAsync(profile, token).ConfigureAwait(false);
        }
        var picture = Encode(snapshot, crop);
        var body = AndroidInteractionVision.Build(profile, picture, AndroidInvestigationCandidates.ProposalLimit);
        var answer = await endpoint.CompleteAsync(profile, body,
            content => content.Length > AndroidInteractionVision.AnswerLimit ? "The control list exceeded its size limit." : null,
            token).ConfigureAwait(false);
        return AndroidInteractionVision.Usable(answer)
            ? AndroidInvestigationCandidates.ParseProposals(answer.Content!, crop) : null;
    }

    private static byte[] Encode(AndroidInteractionSnapshot snapshot, AndroidNodeBounds crop)
    {
        var frame = snapshot.Frame!;
        var width = crop.Right - crop.Left;
        var height = crop.Bottom - crop.Top;
        var stride = checked(width * 3);
        var pixels = new byte[checked(stride * height)];
        for (var y = 0; y < height; y++)
        {
            frame.Rgb.Span.Slice(((y + crop.Top) * frame.Width + crop.Left) * 3, stride).CopyTo(pixels.AsSpan(y * stride));
        }
        foreach (var bounds in snapshot.Occluders)
        {
            if (AndroidInteractionInventory.Intersection(crop, bounds) is { } mask)
            {
                for (var y = mask.Top; y < mask.Bottom; y++)
                {
                    pixels.AsSpan((y - crop.Top) * stride + (mask.Left - crop.Left) * 3, (mask.Right - mask.Left) * 3).Clear();
                }
            }
        }
        BitmapSource picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, stride);
        var ratio = 1280d / Math.Max(width, height);
        if (ratio < 1)
        {
            picture = new TransformedBitmap(picture, new ScaleTransform(ratio, ratio));
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var png = new MemoryStream();
        encoder.Save(png);
        return png.ToArray();
    }
}
