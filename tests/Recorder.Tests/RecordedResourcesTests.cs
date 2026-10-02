using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.Recreation;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// Sub-step 3: a recorded document's fonts and images at a frame are read
/// from the recording file's browser.resources records, and the recreation
/// answers the page's images and the builder's font files from them.
/// </summary>
public sealed class RecordedResourcesTests : IDisposable
{
    private const string SessionId = "session-r";
    private const string Token = "T1";
    private const string DocumentKey = "T1 dom-document-1";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recorded-resources-" + Guid.NewGuid().ToString("N"));

    public RecordedResourcesTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly byte[] FontBytes = Encoding.ASCII.GetBytes("\0\u0001\0\0 a font file");
    private static readonly byte[] ImageBytes = Encoding.ASCII.GetBytes("GIF89a an image");
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Context(string? document) =>
        document is null
            ? """{"browserInstanceId":"b1","processId":5,"documentId":null,"documentToken":null}"""
            : $$"""{"browserInstanceId":"b1","processId":5,"documentId":"{{document}}","documentToken":"{{Token}}"}""";

    private static string Bytes(byte[] bytes) =>
        $$"""{"context":{{Context(null)}},"digest":"{{Digest(bytes)}}","size":"{{bytes.Length}}","bytes":"{{Convert.ToBase64String(bytes)}}"}""";

    private static string Face(string number, string document = "dom-document-1") =>
        $$"""{"context":{{Context(document)}},"faceNumber":"{{number}}"}""";

    private static string Loaded(string number, string family, string? digest) =>
        $$"""{"context":{{Context("dom-document-1")}},"faceNumber":"{{number}}","family":"{{family}}","descriptors":{"style":"normal","weight":"700","unicodeRange":"U+0-10FFFF"},"source":{"kind":"url","url":"https://example.test/f.woff2"},"fontFile":{{(digest is null ? "null" : $$"""{"digest":"{{digest}}","index":0}""")}}}""";

    private async Task<string> WriteAsync()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector("test.browser", BrowserEvidenceChannels.Resources);
        var records = new (long Time, string Type, string Json)[]
        {
            (1_000, BrowserEvidenceEventTypes.FontFile, Bytes(FontBytes)),
            (2_000, BrowserEvidenceEventTypes.FontFaceAdded, Face("1")),
            (2_100, BrowserEvidenceEventTypes.FontFaceAdded, Face("2")),
            (2_200, BrowserEvidenceEventTypes.FontFaceAdded, Face("3", "dom-document-9")),
            (2_300, BrowserEvidenceEventTypes.FontFaceAdded, Face("5")),
            (3_000, BrowserEvidenceEventTypes.FontFaceLoaded, Loaded("1", "Web Sans", Digest(FontBytes))),
            (3_100, BrowserEvidenceEventTypes.FontFaceLoaded, Loaded("2", "Gone", Digest(FontBytes))),
            (3_200, BrowserEvidenceEventTypes.FontFaceLoaded, Loaded("3", "Other document", Digest(FontBytes))),
            (4_000, BrowserEvidenceEventTypes.ImageData, Bytes(ImageBytes)),
            (4_100, BrowserEvidenceEventTypes.ImageResource,
                $$"""{"context":{{Context(null)}},"url":"https://example.test/a.gif","responseUrl":"https://cdn.test/a.gif","status":200,"mimeType":"image/gif","size":"{{ImageBytes.Length}}","digest":"{{Digest(ImageBytes)}}","dataRecorded":true}"""),
            (4_200, BrowserEvidenceEventTypes.ImageResource,
                $$"""{"context":{{Context(null)}},"url":"https://example.test/b.png","responseUrl":null,"status":200,"mimeType":"image/png","size":"3","digest":"{{new string('0', 64)}}","dataRecorded":false}"""),
            (5_000, BrowserEvidenceEventTypes.FontFaceRemoved, Face("2")),
            (9_000, BrowserEvidenceEventTypes.FontFaceAdded, Face("4")),
            (9_100, BrowserEvidenceEventTypes.FontFaceLoaded, Loaded("4", "After the frame", Digest(FontBytes))),
        };
        var events = records.Select((record, index) =>
                Event(SessionId, collector, (ulong)index + 1, record.Time, BrowserEvidenceChannels.Resources, record.Type) with
                {
                    Payload = Json(record.Json),
                })
            .ToArray();
        using var target = new RecordingFileBatchTarget(
            path,
            new Dictionary<string, string> { ["sessionKey"] = SessionId, ["clockFrequency"] = "10000000" },
            new RecordingFileWriterOptions { ChunkBytes = 1024 });
        var batch = events.Select((record, index) => new BufferedEvent(index, record, record.Payload.GetRawText())).ToArray();
        Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
        target.Finish();
        return path;
    }

    [Fact]
    public async Task TheDocumentsLoadedFacesAndTheImagesAtTheFrameAreRead()
    {
        var path = await WriteAsync();
        using var resources = RecordingFileResources.Read(path, DocumentKey, 6_000, TestContext.Current.CancellationToken);

        // Face 1 only: face 2 was removed, face 3 is another document's,
        // face 5 did not load, and face 4 was added after the frame.
        var face = Assert.Single(resources.Faces);
        Assert.Equal("Web Sans", face.Family);
        Assert.Equal(Digest(FontBytes), face.Digest);
        Assert.Equal("https://example.test/f.woff2", face.SourceUrl);
        Assert.Contains(new KeyValuePair<string, string>("weight", "700"), face.Descriptors);
        Assert.Equal(FontBytes, resources.FontFile(face.Digest));
        Assert.Null(resources.FontFile(Digest(ImageBytes)));

        var image = resources.Image("https://example.test/a.gif#part")!;
        Assert.Same(image, resources.Image("https://cdn.test/a.gif"));
        Assert.Equal("image/gif", image.MimeType);
        Assert.Equal(ImageBytes, resources.ImageBytes(image.Digest));
        Assert.Null(resources.Image("https://example.test/b.png"));
        Assert.Equal(1, resources.ImageCount);

        Assert.Contains(resources.Notes, note => note.StartsWith("1 font faces were in the document's set at the frame but had not loaded", StringComparison.Ordinal));
        Assert.Contains(resources.Notes, note => note.StartsWith("1 images were recorded without their bytes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARecordWhoseBytesDoNotMatchItsDigestIsNotUsed()
    {
        var path = Path.Combine(_directory, "altered.mcap");
        var collector = Collector("test.browser", BrowserEvidenceChannels.Resources);
        var altered = Bytes(FontBytes).Replace(Convert.ToBase64String(FontBytes), Convert.ToBase64String(ImageBytes), StringComparison.Ordinal);
        var record = Event(SessionId, collector, 1, 1_000, BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.FontFile) with
        {
            Payload = Json(altered),
        };
        using (var target = new RecordingFileBatchTarget(
                   path,
                   new Dictionary<string, string> { ["sessionKey"] = SessionId, ["clockFrequency"] = "10000000" },
                   new RecordingFileWriterOptions()))
        {
            Assert.Empty(await target.WriteAsync(new EventBatch([new BufferedEvent(0, record, altered)], [], []), TestContext.Current.CancellationToken));
            target.Finish();
        }
        using var resources = RecordingFileResources.Read(path, DocumentKey, 6_000, TestContext.Current.CancellationToken);
        Assert.Null(resources.FontFile(Digest(FontBytes)));
    }

    [Fact]
    public async Task TheRecreationAnswersImagesAndFontFilesFromTheRecording()
    {
        var path = await WriteAsync();
        var resources = RecordingFileResources.Read(path, DocumentKey, 6_000, TestContext.Current.CancellationToken);
        var fonts = RecreationServer.FontAddress("f0nt");
        var content = new RecreationContent("<p>page</p>", FixedRecreation.Create().Evidence, "n0nce")
        {
            DocumentUrl = "https://example.test/dir/page",
            Resources = resources,
            FontAddress = fonts,
        };
        await using var server = await RecreationServer.StartAsync(content, TestContext.Current.CancellationToken);

        var page = server.Answer("https://example.test/dir/page", "Document")!;
        var policy = page.Headers.Single(header => header.Key == "Content-Security-Policy").Value;
        Assert.Contains($"connect-src {fonts};", policy, StringComparison.Ordinal);
        Assert.Contains("img-src 'self' data: http: https:;", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'nonce-n0nce'", policy, StringComparison.Ordinal);

        var font = server.Answer(fonts + Digest(FontBytes), "Fetch")!;
        Assert.Equal(200, font.Status);
        Assert.Equal(FontBytes, font.Body);
        Assert.Contains(new KeyValuePair<string, string>("Access-Control-Allow-Origin", "*"), font.Headers);
        // The instrumented Chromium reports the builder's fetch() as XHR.
        Assert.Equal(FontBytes, server.Answer(fonts + Digest(FontBytes), "XHR")!.Body);
        // Only the builder's fetch of a face's file is answered.
        Assert.Null(server.Answer(fonts + Digest(FontBytes), "Font"));
        Assert.Null(server.Answer(fonts + Digest(ImageBytes), "Fetch"));
        Assert.Null(server.Answer(RecreationServer.FontAddress("other") + Digest(FontBytes), "Fetch"));

        var image = server.Answer("https://example.test/a.gif", "Image")!;
        Assert.Equal(200, image.Status);
        Assert.Equal(ImageBytes, image.Body);
        Assert.Contains(new KeyValuePair<string, string>("Content-Type", "image/gif"), image.Headers);
        Assert.Null(server.Answer("https://example.test/a.gif", "Document"));
        Assert.Null(server.Answer("https://example.test/b.png", "Image"));
        Assert.Null(server.Answer("https://example.test/style.css", "Stylesheet"));
    }

    [Fact]
    public async Task TheBuilderIsGivenTheFacesToAddWithTheirFontFileAddresses()
    {
        var path = await WriteAsync();
        var resources = RecordingFileResources.Read(path, DocumentKey, 6_000, TestContext.Current.CancellationToken);
        var fonts = RecreationServer.FontAddress("f0nt");
        var tree = RecordedPage.Tree(RecordedPageTests.State(), resources.Faces, fonts);
        using var json = JsonDocument.Parse(tree);
        var face = Assert.Single(json.RootElement.GetProperty("fontFaces").EnumerateArray());
        Assert.Equal("Web Sans", face.GetProperty("family").GetString());
        Assert.Equal("700", face.GetProperty("descriptors").GetProperty("weight").GetString());
        Assert.Equal(fonts + Digest(FontBytes), face.GetProperty("url").GetString());
        resources.Dispose();

        // Without a font address, as for a page not served at its recorded
        // address, no face is given.
        using var none = JsonDocument.Parse(RecordedPage.Tree(RecordedPageTests.State()));
        Assert.Empty(none.RootElement.GetProperty("fontFaces").EnumerateArray());
    }
}
