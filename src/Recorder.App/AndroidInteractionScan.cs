using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Recorder.Analysis;
using Recorder.Collectors.Android;

namespace Recorder.App;

internal sealed record AndroidModelDiscoveryReport(string Status, string? Model = null, ulong? Frame = null,
    byte[]? InputPng = null, IReadOnlyList<AndroidVisionProposal>? Proposals = null);

internal static class AndroidInteractionScan
{
    private static readonly TimeSpan VisionBudget = TimeSpan.FromSeconds(55);

    public static async Task<AndroidModelDiscoveryReport> ReadAsync(AndroidLivePreviewFrame source,
        Func<CancellationToken, Task<(ModelProfile? Profile, LocalServerSettings? Server, string? Problem)>> selection,
        IProgress<string> progress, CancellationToken token)
    {
        if (source.Width <= 0 || source.Height <= 0 ||
            source.Rgb.Length % 3 != 0 || (long)source.Width * source.Height != source.Rgb.Length / 3)
        {
            return new("Visual discovery not assessed. The recorded screenshot could not be read.");
        }

        string? model = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(VisionBudget);
        try
        {
            var (profile, server, problem) = await selection(timeout.Token).ConfigureAwait(false);
            if (profile is null)
            {
                return Unavailable(problem ?? "No model is selected for complex vision-based checks.");
            }
            model = profile.Name;
            progress.Report($"Reading one Android app screenshot with the complex vision model, {model}.");
            return await Task.Run(() => ProposeAsync(source, profile, server, timeout.Token), timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return Unavailable($"The image model did not finish within {VisionBudget.TotalSeconds:0} seconds.");
        }
        catch (ModelServerBusyException)
        {
            return Unavailable("The recorder's local model server is in use by analysis. It was not interrupted.");
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is (ModelEndpointException or
            IOException or HttpRequestException or JsonException or InvalidOperationException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException or Npgsql.NpgsqlException))
        {
            // Do not expose endpoint errors that may quote the image or credentials.
            return Unavailable("The selected image model could not be used. Check the project's vision model settings and availability.");
        }

        AndroidModelDiscoveryReport Unavailable(string reason) =>
            new($"Visual discovery not assessed. {reason}", model, source.Sequence);
    }

    private static async Task<AndroidModelDiscoveryReport> ProposeAsync(
        AndroidLivePreviewFrame source, ModelProfile profile, LocalServerSettings? server, CancellationToken token)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var endpoint = new HttpModelEndpoint(client, new CurrentUserApiKeyProtector());
        // Use the same managed server lifetime as analysis. Never take over its
        // server or evict its loaded model to run a live scan.
        await using var host = profile.Local is null ? null :
            new LocalServerHost(new AnalysisSettings([profile], server, 1), AnalysisLocation.Directory, client, waitForLease: false);
        if (host is not null)
        {
            await host.EnsureStartedAsync(token).ConfigureAwait(false);
            await endpoint.LoadAsync(profile, token).ConfigureAwait(false);
        }
        var picture = Encode(source);
        var body = AndroidInteractionVision.Build(profile, picture, AndroidVisionProposals.Limit);
        var answer = await endpoint.CompleteAsync(profile, body,
            content => content.Length > AndroidInteractionVision.AnswerLimit ? "The control list exceeded its size limit." : null,
            token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var proposals = AndroidInteractionVision.Usable(answer) ? AndroidVisionProposals.Parse(answer.Content!) : null;
        var status = proposals is null ? "Visual discovery not assessed. The model did not return a usable proposal list." :
            $"The model returned {proposals.Count} screenshot {(proposals.Count == 1 ? "proposal" : "proposals")}." +
            (proposals.Count == AndroidVisionProposals.Limit ? " The proposal limit was reached." : "");
        return new(status, profile.Name, source.Sequence, picture, proposals);
    }

    private static byte[] Encode(AndroidLivePreviewFrame source)
    {
        BitmapSource picture = BitmapSource.Create(source.Width, source.Height, 96, 96, PixelFormats.Rgb24, null,
            source.Rgb.ToArray(), checked(source.Width * 3));
        var ratio = 1280d / Math.Max(source.Width, source.Height);
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
