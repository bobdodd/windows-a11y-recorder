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
/// Slice 4e (protocol 0.51): a recorded document's style sheets at a frame
/// are read from the recording file, the recreation answers a sheet's address
/// with the text it arrived with, and the builder is given the CSSOM text of
/// the sheets script changed or constructed, and each scope's adopted sheets.
/// </summary>
public sealed class RecordedStyleSheetsTests : IDisposable
{
    private const string SessionId = "session-s";
    private const string DocumentKey = "T1 dom-document-1";
    private static readonly byte[] SiteCss = Encoding.UTF8.GetBytes("@import url(more.css);\nbody { color: red; }\n");
    private static readonly byte[] MoreCss = Encoding.UTF8.GetBytes("p { margin: 0; }\n");
    private static readonly byte[] Changed = Encoding.UTF8.GetBytes("h1 { color: blue; }\nh2 { color: green; }");
    private static readonly byte[] Constructed = Encoding.UTF8.GetBytes(":host { display: block; }");
    private static readonly byte[] Later = Encoding.UTF8.GetBytes("body { color: black; }\n");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recorded-sheets-" + Guid.NewGuid().ToString("N"));

    public RecordedStyleSheetsTests() => Directory.CreateDirectory(_directory);

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

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Context(string? document) =>
        document is null
            ? """{"browserInstanceId":"b1","processId":5,"documentId":null,"documentToken":null}"""
            : $$"""{"browserInstanceId":"b1","processId":5,"documentId":"{{document}}","documentToken":"T1"}""";

    private static string Bytes(byte[] bytes) =>
        $$"""{"context":{{Context(null)}},"digest":"{{Digest(bytes)}}","size":"{{bytes.Length}}","bytes":"{{Convert.ToBase64String(bytes)}}"}""";

    private static string Resource(string url, byte[] bytes, bool recorded = true) =>
        $$"""{"context":{{Context(null)}},"url":"{{url}}","responseUrl":null,"status":200,"mimeType":"text/css","size":"{{bytes.Length}}","digest":"{{Digest(bytes)}}","textRecorded":{{(recorded ? "true" : "false")}}}""";

    private static string Entry(string sheet, string kind, long? owner, string? parent, int? rule, string? href, string source, byte[]? text, bool disabled = false, string media = "") =>
        $$"""{"sheet":"{{sheet}}","kind":"{{kind}}","ownerNodeId":{{(owner?.ToString() ?? "null")}},"parentSheet":{{(parent is null ? "null" : $"\"{parent}\"")}},"ruleIndex":{{(rule?.ToString() ?? "null")}},"href":{{(href is null ? "null" : $"\"{href}\"")}},"media":"{{media}}","title":"","disabled":{{(disabled ? "true" : "false")}},"active":true,"textSource":"{{source}}","textDigest":{{(text is null ? "null" : $"\"{Digest(text)}\"")}}}""";

    private static string Update(string document, string scopes) =>
        $$"""{"context":{{Context(document)}},"scopes":[{{scopes}}]}""";

    private const string Site = "https://example.test/site.css";
    private const string More = "https://example.test/more.css";

    private async Task<string> WriteAsync()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector("test.browser", BrowserEvidenceChannels.Resources);
        var records = new (long Time, string Type, string Json)[]
        {
            (1_000, BrowserEvidenceEventTypes.StyleSheetText, Bytes(SiteCss)),
            (1_010, BrowserEvidenceEventTypes.StyleSheetResource, Resource(Site, SiteCss)),
            (1_100, BrowserEvidenceEventTypes.StyleSheetText, Bytes(MoreCss)),
            (1_110, BrowserEvidenceEventTypes.StyleSheetResource, Resource(More, MoreCss)),
            (1_200, BrowserEvidenceEventTypes.StyleSheetResource, Resource("https://example.test/lost.css", MoreCss, recorded: false)),
            (2_000, BrowserEvidenceEventTypes.StyleSheetsUpdated, Update("dom-document-1",
                $$"""{"scopeNodeId":1,"sheets":[{{Entry("1", "link", 10, null, null, Site, "arrived", SiteCss)}},{{Entry("2", "import", null, "1", 0, More, "arrived", MoreCss)}},{{Entry("3", "style", 11, null, null, null, "element", null)}}],"adopted":[]}""")),
            (3_000, BrowserEvidenceEventTypes.StyleSheetText, Bytes(Changed)),
            (3_010, BrowserEvidenceEventTypes.StyleSheetText, Bytes(Constructed)),
            // The style element's sheet was changed through the CSSOM, and a
            // shadow root adopted a constructed sheet; sheets 1 and 2 are
            // unchanged, so they are their numbers alone.
            (3_100, BrowserEvidenceEventTypes.StyleSheetsUpdated, Update("dom-document-1",
                $$"""{"scopeNodeId":1,"sheets":[{"sheet":"1"},{"sheet":"2"},{{Entry("3", "style", 11, null, null, null, "cssom", Changed, media: "screen")}}],"adopted":[]},{"scopeNodeId":20,"sheets":[],"adopted":[{{Entry("4", "constructed", null, null, null, null, "cssom", Constructed)}}]}""")),
            // Another document's sheets.
            (3_200, BrowserEvidenceEventTypes.StyleSheetsUpdated, Update("dom-document-9",
                $$"""{"scopeNodeId":1,"sheets":[{{Entry("9", "link", 10, null, null, Site, "arrived", Later)}}],"adopted":[]}""")),
            // After the frame, the link's address arrives again with new text.
            (9_000, BrowserEvidenceEventTypes.StyleSheetText, Bytes(Later)),
            (9_010, BrowserEvidenceEventTypes.StyleSheetResource, Resource(Site, Later)),
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
    public async Task TheDocumentsSheetsAtTheFrameAreRead()
    {
        var path = await WriteAsync();
        using var resources = RecordingFileResources.Read(path, DocumentKey, 6_000, TestContext.Current.CancellationToken);
        var sheets = resources.StyleSheets;
        Assert.True(sheets.Recorded);
        Assert.Equal([1L, 20L], sheets.Scopes.Select(scope => scope.ScopeNodeId));
        Assert.Equal(["1", "2", "3"], sheets.Scopes[0].Sheets);
        Assert.Equal(["4"], sheets.Scopes[1].Adopted);
        // Sheet 3's details are its latest full entry's; sheets 1 and 2 keep
        // the details of the record that last gave them.
        Assert.Equal("cssom", sheets.Sheets["3"].TextSource);
        Assert.Equal("screen", sheets.Sheets["3"].Media);
        Assert.Equal("arrived", sheets.Sheets["1"].TextSource);
        Assert.Equal("1", sheets.Sheets["2"].ParentSheet);
        Assert.False(sheets.Sheets.ContainsKey("9"));
        Assert.Equal(["1", "2", "3", "4"], sheets.InEffect().Select(sheet => sheet.Sheet));

        Assert.Equal(Digest(SiteCss), sheets.ArrivedDigest(Site + "#x"));
        Assert.Equal(Digest(MoreCss), sheets.ArrivedDigest(More));
        Assert.Null(sheets.ArrivedDigest("https://example.test/lost.css"));
        Assert.Equal(Changed, resources.StyleSheetText(Digest(Changed)));
        Assert.Null(resources.StyleSheetText(Digest(Later)));

        Assert.Contains(resources.Notes, note => note.StartsWith("4 style sheets were in effect at the frame (1 constructed, 1 import, 1 link, 1 style).", StringComparison.Ordinal));
        Assert.Contains(resources.Notes, note => note.StartsWith("1 sheets had been changed through the CSSOM", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARecordingWithoutSheetRecordsSaysSo()
    {
        var path = await WriteAsync();
        using var resources = RecordingFileResources.Read(path, DocumentKey, 500, TestContext.Current.CancellationToken);
        Assert.False(resources.StyleSheets.Recorded);
        Assert.Contains(resources.Notes, note => note.StartsWith("The recording holds no style sheet records", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheRecreationAnswersASheetsAddressWithTheTextItArrivedWith()
    {
        var path = await WriteAsync();
        var resources = RecordingFileResources.Read(path, DocumentKey, 6_000, TestContext.Current.CancellationToken);
        var content = new RecreationContent("<p>page</p>", FixedRecreation.Create().Evidence, "n0nce")
        {
            DocumentUrl = "https://example.test/page",
            Resources = resources,
            FontAddress = RecreationServer.FontAddress("f0nt"),
        };
        await using var server = await RecreationServer.StartAsync(content, TestContext.Current.CancellationToken);

        var page = server.Answer("https://example.test/page", "Document")!;
        var policy = page.Headers.Single(header => header.Key == "Content-Security-Policy").Value;
        Assert.Contains("style-src 'self' 'unsafe-inline' http: https:", policy, StringComparison.Ordinal);

        var site = server.Answer(Site, "Stylesheet")!;
        Assert.Equal(200, site.Status);
        Assert.Equal(SiteCss, site.Body);
        Assert.Contains(new KeyValuePair<string, string>("Content-Type", "text/css; charset=utf-8"), site.Headers);
        Assert.Contains(new KeyValuePair<string, string>("X-Content-Type-Options", "nosniff"), site.Headers);
        Assert.Equal(MoreCss, server.Answer(More, "Stylesheet")!.Body);
        // A sheet's text answers only a style sheet request, and an address
        // whose text is not recorded is refused.
        Assert.Null(server.Answer(Site, "Fetch"));
        Assert.Null(server.Answer("https://example.test/lost.css", "Stylesheet"));
        Assert.Null(server.Answer("https://example.test/other.css", "Stylesheet"));
    }

    [Fact]
    public async Task TheBuilderIsGivenTheChangedAndConstructedSheetsAndTheAdoptedOnes()
    {
        var path = await WriteAsync();
        using var resources = RecordingFileResources.Read(path, DocumentKey, 6_000, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(RecordedPage.Tree(
            RecordedPageTests.State(), styleSheets: resources.StyleSheets, styleSheetText: resources.StyleSheetText));
        var sheets = json.RootElement.GetProperty("styleSheets").EnumerateArray().ToList();
        Assert.Equal(["1", "2", "3", "4"], sheets.Select(sheet => sheet.GetProperty("sheet").GetString()));
        // A sheet as it arrived is loaded by the page itself: no text.
        Assert.Equal(JsonValueKind.Null, sheets[0].GetProperty("text").ValueKind);
        Assert.False(sheets[0].GetProperty("textExpected").GetBoolean());
        Assert.Equal("1", sheets[1].GetProperty("parentSheet").GetString());
        Assert.Equal(0, sheets[1].GetProperty("ruleIndex").GetInt32());
        Assert.Equal(Encoding.UTF8.GetString(Changed), sheets[2].GetProperty("text").GetString());
        Assert.Equal(11, sheets[2].GetProperty("ownerNodeId").GetInt64());
        Assert.Equal("screen", sheets[2].GetProperty("media").GetString());
        Assert.Equal("constructed", sheets[3].GetProperty("kind").GetString());
        Assert.Equal(Encoding.UTF8.GetString(Constructed), sheets[3].GetProperty("text").GetString());

        var adopted = json.RootElement.GetProperty("adoptedStyleSheets").EnumerateArray().ToList();
        Assert.Equal(20, adopted[1].GetProperty("scopeNodeId").GetInt64());
        Assert.Equal(["4"], adopted[1].GetProperty("sheets").EnumerateArray().Select(item => item.GetString()));

        using var none = JsonDocument.Parse(RecordedPage.Tree(RecordedPageTests.State()));
        Assert.Empty(none.RootElement.GetProperty("styleSheets").EnumerateArray());
    }
}
