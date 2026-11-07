using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Recorder.Analysis;
using Recorder.Analysis.Setup;

namespace Recorder.Tests;

/// <summary>
/// What the installer's first start and the model setup window rest on:
/// the list of models, the settings file's changes, Hugging Face
/// addresses, the downloads that continue after a stop and are checked
/// against the published SHA-256, and the comparison with this PC. See
/// docs/architecture/windows-installer.md.
/// </summary>
public sealed class ModelSetupTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "model-setup-" + Guid.NewGuid().ToString("N"));

    public ModelSetupTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private const string Bundled = @"C:\Users\test\AppData\Local\Programs\Windows A11y Recorder\llama\llama-server.exe";

    // The list of models

    [Fact]
    public void TheInstalledListOfModelsIsReadWithoutProblems()
    {
        var (catalog, problems) = ModelCatalog.Read(Path.Combine(AppContext.BaseDirectory, ModelCatalog.FileName));

        Assert.Empty(problems);
        Assert.Equal("b11607", catalog.LlamaServer);
        Assert.Equal(11, catalog.Models.Count);
        Assert.Equal(catalog.Models.Count, catalog.Models.Select(model => model.Id).Distinct().Count());
        var first = catalog.Models[0];
        Assert.Equal("Qwen3.6 35B A3B, UD-Q4_K_M", first.Name);
        // The file the development PC has used since 2026-10-13
        // (docs/validation/analysis-runner-2026-10-14.md).
        Assert.Equal("ac0e2c1189e055faa36eff361580e79c5bd6f8e76bffb4ce547f167d53e31a61", first.Model.Sha256);
        Assert.All(catalog.Models, model =>
        {
            Assert.Equal("huggingface.co", model.Model.Url.Host);
            Assert.Contains("/resolve/", model.Model.Url.AbsolutePath, StringComparison.Ordinal);
            Assert.NotNull(model.ImageAddOn);
            Assert.Null(LocalModelServer.PresetEntries(model.Arguments).Problem);
            Assert.False(model.Generation.Thinking);
        });
        var gemma = Assert.Single(catalog.Models, model => model.Id == "gemma-4-26b-a4b-ud-q4_k_m");
        Assert.Equal("f2c28b3dc4776931ac6f879e11f203dec637ea0f14267a86ec8f6165f63f293f", gemma.Model.Sha256);
        Assert.Equal(0.95, gemma.Generation.Sampling["top_p"]);
    }

    [Fact]
    public void AModelWithABadFileIsReportedAndLeftOut()
    {
        var (catalog, problems) = ModelCatalog.Parse("""
            { "version": 1, "models": [
              { "id": "a", "name": "A", "model": { "name": "a.gguf", "url": "http://example.test/a.gguf", "size": 10, "sha256": "00" } },
              { "id": "b", "name": "B", "arguments": "-fa on",
                "model": { "name": "b.gguf", "url": "https://huggingface.co/o/r/resolve/main/b.gguf", "size": 10,
                           "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } }
            ] }
            """);

        var model = Assert.Single(catalog.Models);
        Assert.Equal("b", model.Id);
        Assert.Contains(problems, problem => problem.Contains("\"A\"", StringComparison.Ordinal));
    }

    // The installed server

    [Fact]
    public void WithNoSettingsFileTheInstalledServerIsSet()
    {
        var text = ModelSetup.WithBundledServer(null, Bundled, path => path == Bundled);

        var (settings, problems) = AnalysisSettingsFile.Parse(text!);
        Assert.Empty(problems);
        Assert.Equal(Bundled, settings.LocalServer!.Executable);
        Assert.Equal(ModelSetup.DefaultPort, settings.LocalServer.Port);
    }

    [Fact]
    public void AServerThatIsNotThereIsReplacedAndItsPortKept()
    {
        var before = """{ "version": 1, "localServer": { "executable": "C:\\llama\\llama-server.exe", "port": 9000 }, "profiles": [] }""";

        var text = ModelSetup.WithBundledServer(before, Bundled, path => path == Bundled);

        var (settings, _) = AnalysisSettingsFile.Parse(text!);
        Assert.Equal(Bundled, settings.LocalServer!.Executable);
        Assert.Equal(9000, settings.LocalServer.Port);
    }

    [Fact]
    public void AServerThatIsThereIsKept()
    {
        var before = """{ "version": 1, "localServer": { "executable": "C:\\llama\\llama-server.exe", "port": 8091 } }""";

        Assert.Null(ModelSetup.WithBundledServer(before, Bundled, _ => true));
    }

    [Fact]
    public void WithoutAnInstalledServerNothingChanges()
    {
        Assert.Null(ModelSetup.WithBundledServer(null, Bundled, _ => false));
    }

    // Profiles

    private static string WithServer() => ModelSetup.WithBundledServer(null, Bundled, _ => true)!;

    private static IReadOnlyList<JsonObject> QwenProfiles(string? addOn = @"D:\models\unsloth\Qwen3.6-35B-A3B-GGUF\mmproj-F16.gguf") =>
        ModelSetup.Profiles(
            "Qwen3.6 35B A3B, UD-Q4_K_M",
            "qwen3.6-35b-a3b-ud-q4_k_m",
            "UD-Q4_K_M",
            @"D:\models\unsloth\Qwen3.6-35B-A3B-GGUF\Qwen3.6-35B-A3B-UD-Q4_K_M.gguf",
            "ac0e2c1189e055faa36eff361580e79c5bd6f8e76bffb4ce547f167d53e31a61",
            addOn,
            "-fa on -c 32768",
            new GenerationSettings(8192, 0, 1, false),
            ModelSetup.DefaultPort);

    [Fact]
    public void AModelWithAnImageAddOnMakesATextProfileAndAnImageProfileThatAreTheDefaults()
    {
        var text = ModelSetup.WithProfiles(WithServer(), QwenProfiles());

        var (settings, problems) = AnalysisSettingsFile.Parse(text);
        Assert.Empty(problems);
        Assert.Equal(2, settings.Profiles.Count);
        var textProfile = settings.Profile("Qwen3.6 35B A3B, UD-Q4_K_M")!;
        var imageProfile = settings.Profile("Qwen3.6 35B A3B, UD-Q4_K_M, with images")!;
        Assert.Equal([ModelCapabilities.Text], textProfile.Capabilities);
        Assert.True(imageProfile.Supports(ModelCapabilities.Image));
        Assert.True(imageProfile.Supports(ModelCapabilities.Text));
        Assert.Empty(textProfile.Generation.Sampling);
        Assert.Equal(1.5, imageProfile.Generation.Sampling["presence_penalty"]);
        Assert.Equal("qwen3.6-35b-a3b-ud-q4_k_m-images", imageProfile.Model);
        Assert.Equal(new Uri("http://127.0.0.1:8091"), textProfile.Endpoint);
        Assert.Equal("ac0e2c1189e055faa36eff361580e79c5bd6f8e76bffb4ce547f167d53e31a61", textProfile.File.Sha256);
        Assert.Equal(textProfile.Name, settings.Defaults[ModelCapabilities.Text]);
        Assert.Equal(imageProfile.Name, settings.Defaults[ModelCapabilities.Image]);
    }

    [Fact]
    public void AddingAModelAgainReplacesItsProfilesAndKeepsTheDefaults()
    {
        var gemma = ModelSetup.Profiles("Gemma", "gemma", "Q4_K_M", @"D:\m\g.gguf",
            "f2c28b3dc4776931ac6f879e11f203dec637ea0f14267a86ec8f6165f63f293f", null, "-fa on", GenerationSettings.Default, ModelSetup.DefaultPort);
        var text = ModelSetup.WithProfiles(ModelSetup.WithProfiles(WithServer(), gemma), QwenProfiles());
        text = ModelSetup.WithProfiles(text, QwenProfiles());

        var (settings, problems) = AnalysisSettingsFile.Parse(text);
        Assert.Empty(problems);
        Assert.Equal(3, settings.Profiles.Count);
        Assert.Equal("Gemma", settings.Defaults[ModelCapabilities.Text]);
        Assert.Equal("Qwen3.6 35B A3B, UD-Q4_K_M, with images", settings.Defaults[ModelCapabilities.Image]);
    }

    [Fact]
    public void AProfileThatTheWorkerWouldRefuseIsNotWritten()
    {
        // No local server: a local profile is refused.
        var error = Assert.Throws<InvalidDataException>(() => ModelSetup.WithProfiles(null, QwenProfiles(null)));

        Assert.Contains("no localServer is set", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingAProfileRemovesTheDefaultsThatNameIt()
    {
        var text = ModelSetup.WithProfiles(WithServer(), QwenProfiles());

        text = ModelSetup.WithoutProfile(text, "Qwen3.6 35B A3B, UD-Q4_K_M, with images");

        var (settings, problems) = AnalysisSettingsFile.Parse(text);
        Assert.Empty(problems);
        Assert.Single(settings.Profiles);
        Assert.False(settings.Defaults.ContainsKey(ModelCapabilities.Image));
    }

    [Fact]
    public void AProfilesSettingsAreChangedAndItsSamplingKept()
    {
        var text = ModelSetup.WithProfiles(WithServer(), QwenProfiles());

        text = ModelSetup.WithProfileSettings(text, "Qwen3.6 35B A3B, UD-Q4_K_M, with images", "-ngl 99 -ncmoe 20 -fa on -c 65536", 16384, 0.6, 7, true);

        var profile = AnalysisSettingsFile.Parse(text).Settings.Profile("Qwen3.6 35B A3B, UD-Q4_K_M, with images")!;
        Assert.Equal("-ngl 99 -ncmoe 20 -fa on -c 65536", profile.Local!.Arguments);
        Assert.Equal(new GenerationSettings(16384, 0.6, 7, true) { Sampling = profile.Generation.Sampling }, profile.Generation);
        Assert.Equal(1.5, profile.Generation.Sampling["presence_penalty"]);
    }

    [Fact]
    public void ServerArgumentsTheWorkerSetsAreRefused()
    {
        var text = ModelSetup.WithProfiles(WithServer(), QwenProfiles());

        var error = Assert.Throws<InvalidDataException>(() =>
            ModelSetup.WithProfileSettings(text, "Qwen3.6 35B A3B, UD-Q4_K_M", "--port 9000", 8192, 0, 1, false));

        Assert.Contains("set by the worker", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Qwen3.6-35B-A3B-UD-Q4_K_M.gguf", "qwen3.6-35b-a3b-ud-q4_k_m", "UD-Q4_K_M")]
    [InlineData("gemma-4-31B-it-Q5_K_M.gguf", "gemma-4-31b-it-q5_k_m", "Q5_K_M")]
    [InlineData("Qwen3.6-27B-IQ4_XS.gguf", "qwen3.6-27b-iq4_xs", "IQ4_XS")]
    [InlineData("My Model (test).gguf", "my-model--test", "")]
    public void FileNamesGiveAModelNameAndQuantization(string file, string model, string quantization)
    {
        Assert.Equal(model, ModelSetup.ModelName(file));
        Assert.Equal(quantization, ModelSetup.Quantization(file));
    }

    // Hugging Face addresses

    [Theory]
    [InlineData("https://huggingface.co/unsloth/Qwen3.6-27B-GGUF/blob/main/Qwen3.6-27B-Q4_K_M.gguf",
        "https://huggingface.co/unsloth/Qwen3.6-27B-GGUF/resolve/main/Qwen3.6-27B-Q4_K_M.gguf")]
    [InlineData(" https://huggingface.co/unsloth/Qwen3.6-27B-GGUF/resolve/main/Qwen3.6-27B-Q4_K_M.gguf?download=true ",
        "https://huggingface.co/unsloth/Qwen3.6-27B-GGUF/resolve/main/Qwen3.6-27B-Q4_K_M.gguf")]
    [InlineData("https://huggingface.co/unsloth/gemma-4-31B-it-GGUF/blob/main/BF16/gemma-4-31B-it-BF16-00001-of-00002.gguf",
        "https://huggingface.co/unsloth/gemma-4-31B-it-GGUF/resolve/main/BF16/gemma-4-31B-it-BF16-00001-of-00002.gguf")]
    public void HuggingFaceFileAddressesGiveTheDownloadAddress(string text, string expected)
    {
        Assert.Equal(new Uri(expected), HuggingFaceFile.DownloadAddress(text));
    }

    [Theory]
    [InlineData("http://huggingface.co/o/r/resolve/main/a.gguf")]
    [InlineData("https://example.test/o/r/resolve/main/a.gguf")]
    [InlineData("https://huggingface.co/unsloth/Qwen3.6-27B-GGUF")]
    [InlineData("https://huggingface.co/o/r/resolve/main/README.md")]
    [InlineData("not an address")]
    public void OtherAddressesAreRefused(string text)
    {
        Assert.Null(HuggingFaceFile.DownloadAddress(text));
    }

    [Fact]
    public async Task AHuggingFaceFileIsDescribedAndFixedToItsCommit()
    {
        const string commit = "82d411acf4a06cfb8d9b073a5211bf410bfc29bf";
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Add("X-Linked-Size", "16820000000");
            response.Headers.Add("X-Linked-ETag", "\"5ed60d0af4650a854b1755bd392f9aef4872643dc25a254bc68043fa638392a0\"");
            response.Headers.Add("X-Repo-Commit", commit);
            return response;
        }));

        var file = await HuggingFaceFile.DescribeAsync(client,
            new Uri("https://huggingface.co/unsloth/Qwen3.6-27B-GGUF/resolve/main/Qwen3.6-27B-Q4_K_M.gguf"), TestContext.Current.CancellationToken);

        Assert.Equal("Qwen3.6-27B-Q4_K_M.gguf", file.Name);
        Assert.Equal(16_820_000_000, file.Size);
        Assert.Equal("5ed60d0af4650a854b1755bd392f9aef4872643dc25a254bc68043fa638392a0", file.Sha256);
        Assert.Equal(new Uri($"https://huggingface.co/unsloth/Qwen3.6-27B-GGUF/resolve/{commit}/Qwen3.6-27B-Q4_K_M.gguf"), file.Url);
    }

    [Fact]
    public async Task AFileThatNeedsASignInIsRefusedWithTheReason()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => HuggingFaceFile.DescribeAsync(client,
            new Uri("https://huggingface.co/google/gated/resolve/main/a.gguf"), TestContext.Current.CancellationToken));

        Assert.Contains("sign-in", error.Message, StringComparison.Ordinal);
    }

    // Downloads

    private static (CatalogFile File, byte[] Bytes) Published(int length = 3_000_000)
    {
        var bytes = new byte[length];
        new Random(5).NextBytes(bytes);
        return (new CatalogFile("model.gguf", new Uri("https://huggingface.co/owner/repo/resolve/abc/model.gguf"), length,
            Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    private static ModelDownloader Downloader(Handler handler) => new(new HttpClient(handler)) { RetryDelay = _ => TimeSpan.Zero };

    [Fact]
    public async Task AFileIsDownloadedIntoItsRepositorysFolderAndChecked()
    {
        var (file, bytes) = Published();
        var handler = Handler.Serving(bytes);

        var path = await Downloader(handler).DownloadAsync(file, _folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_folder, "owner", "repo", "model.gguf"), path);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path + ModelDownloader.PartialSuffix));
        Assert.Equal(file.Sha256, ModelDownloader.ReadHash(path));
    }

    [Fact]
    public async Task APartialDownloadContinuesFromWhereItStopped()
    {
        var (file, bytes) = Published();
        var partial = ModelDownloader.TargetPath(_folder, file) + ModelDownloader.PartialSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        await File.WriteAllBytesAsync(partial, bytes[..1_000_000], TestContext.Current.CancellationToken);
        var handler = Handler.Serving(bytes);

        var path = await Downloader(handler).DownloadAsync(file, _folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(1_000_000, Assert.Single(handler.RangeStarts));
    }

    [Fact]
    public async Task AServerThatIgnoresTheRangeGivesTheWholeFileAgain()
    {
        var (file, bytes) = Published();
        var partial = ModelDownloader.TargetPath(_folder, file) + ModelDownloader.PartialSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        await File.WriteAllBytesAsync(partial, bytes[..1_000_000], TestContext.Current.CancellationToken);
        var handler = Handler.Serving(bytes, honourRange: false);

        var path = await Downloader(handler).DownloadAsync(file, _folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADroppedConnectionIsContinued()
    {
        var (file, bytes) = Published();
        var handler = Handler.Serving(bytes, dropAfter: 1_500_000);

        var path = await Downloader(handler).DownloadAsync(file, _folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Requests);
        Assert.Equal(1_500_000, Assert.Single(handler.RangeStarts));
    }

    [Fact]
    public async Task AFileWithTheWrongHashIsDeletedAndReported()
    {
        var (file, bytes) = Published();
        var wrong = (byte[])bytes.Clone();
        wrong[^1] ^= 1;

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Downloader(Handler.Serving(wrong)).DownloadAsync(file, _folder, null, TestContext.Current.CancellationToken));

        Assert.Contains(file.Sha256, error.Message, StringComparison.Ordinal);
        var target = ModelDownloader.TargetPath(_folder, file);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ModelDownloader.PartialSuffix));
    }

    [Fact]
    public async Task AFileAlreadyDownloadedIsNotDownloadedAgain()
    {
        var (file, bytes) = Published();
        var handler = Handler.Serving(bytes);
        await Downloader(handler).DownloadAsync(file, _folder, null, TestContext.Current.CancellationToken);

        await Downloader(handler).DownloadAsync(file, _folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ADifferentFileWithTheSameNameIsNotReplaced()
    {
        var (file, bytes) = Published();
        var target = ModelDownloader.TargetPath(_folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, new byte[10], TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<IOException>(() =>
            Downloader(Handler.Serving(bytes)).DownloadAsync(file, _folder, null, TestContext.Current.CancellationToken));

        Assert.Contains("Move or delete it", error.Message, StringComparison.Ordinal);
        Assert.Equal(10, new FileInfo(target).Length);
    }

    [Fact]
    public async Task AMissingFileIsNotRequestedAgain()
    {
        var (file, _) = Published();
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(() => Downloader(handler).DownloadAsync(file, _folder, null, TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public void FilesOfTheSameNameInDifferentRepositoriesGoInDifferentFolders()
    {
        var a = new CatalogFile("mmproj-F16.gguf", new Uri("https://huggingface.co/unsloth/Qwen3.6-27B-GGUF/resolve/x/mmproj-F16.gguf"), 1, new string('a', 64));
        var b = new CatalogFile("mmproj-F16.gguf", new Uri("https://huggingface.co/unsloth/gemma-4-31B-it-GGUF/resolve/x/mmproj-F16.gguf"), 1, new string('a', 64));

        Assert.NotEqual(ModelDownloader.TargetPath(_folder, a), ModelDownloader.TargetPath(_folder, b));
    }

    // This PC

    [Fact]
    public void NvidiaSmiIsRead()
    {
        var cards = PcHardware.ParseNvidiaSmi("NVIDIA GeForce RTX 3090, 24576, 581.57\r\nNVIDIA GeForce RTX 2060, 6144, 581.57\r\n");

        Assert.Equal(2, cards.Count);
        Assert.Equal(new GraphicsCard("NVIDIA GeForce RTX 3090", 24576L * 1024 * 1024, "581.57", "nvidia-smi"), cards[0]);
    }

    [Fact]
    public void AFileIsComparedWithThePc()
    {
        const long gb = 1L << 30;
        var alienware = new PcHardware([new GraphicsCard("NVIDIA GeForce RTX 3090", 24 * gb, null, "nvidia-smi")], 64 * gb, true);

        Assert.Contains("can all go on the card", FitComparison.Describe(17 * gb, alienware), StringComparison.Ordinal);
        Assert.Contains("part of it runs from the PC's memory", FitComparison.Describe(23 * gb, alienware), StringComparison.Ordinal);
        Assert.True(FitComparison.TooLarge(90 * gb, alienware));
        Assert.Equal("20.6 GB", ByteSize.Gigabytes(22_134_528_992));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public List<long> RangeStarts { get; } = [];

        public static Handler Serving(byte[] bytes, bool honourRange = true, int? dropAfter = null)
        {
            Handler? handler = null;
            var dropped = false;
            handler = new Handler(request =>
            {
                var from = request.Headers.Range?.Ranges.First().From ?? 0;
                if (request.Headers.Range is not null)
                {
                    handler!.RangeStarts.Add(from);
                }

                var start = honourRange ? from : 0;
                var body = bytes[(int)start..];
                Stream content = new MemoryStream(body);
                if (dropAfter is { } limit && !dropped)
                {
                    dropped = true;
                    content = new DroppingStream(body, limit - (int)start);
                }

                var response = new HttpResponseMessage(honourRange && start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                {
                    Content = new StreamContent(content)
                };
                if (honourRange && start > 0)
                {
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, bytes.Length - 1, bytes.Length);
                }

                return response;
            });
            return handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(answer(request));
        }
    }

    // Gives the first bytes, then fails as a dropped connection does.
    private sealed class DroppingStream(byte[] bytes, int limit) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= limit)
            {
                throw new IOException("The connection was reset.");
            }

            return base.Read(buffer, offset, (int)Math.Min(count, limit - Position));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= limit)
            {
                throw new IOException("The connection was reset.");
            }

            return base.ReadAsync(buffer[..(int)Math.Min(buffer.Length, limit - Position)], cancellationToken);
        }
    }
}
