using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Recorder.Recreation;

// Serves one recreation on the loopback interface, at a random port, under a
// random token, so that only a client given the address can read it. A request
// is answered only if its Host header names the loopback address and port, so
// a page on another site cannot reach the server by rebinding a name to
// 127.0.0.1.
public sealed class RecreationServer : IAsyncDisposable, IRecreationAnswers
{
    // No page script runs: 'none' for scripts also stops event handler
    // attributes. Nothing is fetched from another origin, so the current
    // version of a resource is never shown as if it were evidence. Style
    // elements and attributes are the page's own, recorded, and are allowed.
    public const string PageContentSecurityPolicy =
        "default-src 'none'; script-src 'none'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; media-src 'self'; " +
        "connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";

    // For a recorded page, only the builder script runs, allowed by its
    // nonce. Without 'unsafe-inline', 'unsafe-hashes', or 'strict-dynamic',
    // the recorded script elements, event handler attributes, and
    // javascript: URLs do not run.
    public static string RecordedPageContentSecurityPolicy(string nonce) =>
        PageContentSecurityPolicy.Replace("script-src 'none'", $"script-src 'nonce-{nonce}'", StringComparison.Ordinal);

    // For a recorded page served at its recorded address (sub-step 3), the
    // builder may also read the recorded font files, from the recorder's own
    // address, and images and style sheets (slice 4e) may be asked for at
    // any http or https address, since the recorder answers every request of
    // the tab itself, from the recording, or refuses it: nothing reaches the
    // network.
    public static string RecordedPageContentSecurityPolicy(string nonce, string fontAddress) =>
        RecordedPageContentSecurityPolicy(nonce)
            .Replace("img-src 'self' data:", "img-src 'self' data: http: https:", StringComparison.Ordinal)
            .Replace("style-src 'self' 'unsafe-inline'", "style-src 'self' 'unsafe-inline' http: https:", StringComparison.Ordinal)
            .Replace("connect-src 'none'", $"connect-src {fontAddress}", StringComparison.Ordinal);

    // The recorder's own address for the recorded font files: a name under
    // the reserved .invalid domain, which never resolves, so a request for
    // it can only be answered by the recorder.
    public const string FontHost = "https://a11y-recorder.invalid/";

    public static string FontAddress(string token) => $"{FontHost}{token}/font/";

    public static readonly JsonSerializerOptions EvidenceJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly WebApplication _application;
    private readonly byte[] _token;
    private readonly byte[] _page;
    private readonly byte[] _evidence;
    private readonly string _policy;
    private readonly string? _documentUrl;
    private readonly string? _fontAddress;
    private readonly Recorder.Session.RecordedPageResources _resources;
    private readonly Recorder.Session.RecordedScripts _scripts;
    private readonly IDisposable? _scriptsOwner;
    private readonly List<BlockedNavigation> _blocked = [];
    private readonly List<RecreationTiming> _timings = [];
    // Slice 5b: the frames of the page, by key: the parent's key, a slash,
    // and the frame's position among its parent's frames. The top
    // document's key is empty.
    private readonly Dictionary<string, FrameEntry> _frames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _askedFor = new(StringComparer.Ordinal);
    private readonly HashSet<string> _outOfProcess = new(StringComparer.Ordinal);
    // Slice 5c: each frame's evidence, by its place among the frames in
    // the order they are added, and each served document's build report,
    // by its key.
    private readonly List<string> _frameOrder = [];
    private readonly Dictionary<string, byte[]> _frameEvidence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BuildReport> _built = new(StringComparer.Ordinal);

    private sealed record FrameEntry(
        string Key,
        string ParentKey,
        RecreationFrame Frame,
        byte[]? Page,
        string? Policy,
        Recorder.Session.RecordedPageResources Resources);

    private RecreationServer(WebApplication application, string token, RecreationContent content)
    {
        _application = application;
        Token = token;
        _token = Encoding.ASCII.GetBytes(token);
        _page = Encoding.UTF8.GetBytes(content.Html);
        _evidence = JsonSerializer.SerializeToUtf8Bytes(content.Evidence, EvidenceJson);
        _documentUrl = content.DocumentUrl is { } url && IsServableAddress(url) ? url : null;
        _fontAddress = _documentUrl is not null ? content.FontAddress : null;
        _resources = content.Resources ?? Recorder.Session.RecordedPageResources.None;
        _scripts = content.Scripts ?? Recorder.Session.RecordedScripts.None;
        _scriptsOwner = content.ScriptsOwner;
        _policy = content.ScriptNonce is not { } nonce
            ? PageContentSecurityPolicy
            : _fontAddress is { } fonts
                ? RecordedPageContentSecurityPolicy(nonce, fonts)
                : RecordedPageContentSecurityPolicy(nonce);
        // Slice 5c: the frames are listed, with their evidence, whether or
        // not the page is served at its recorded address; they are built
        // only when it is.
        if (_documentUrl is not null)
        {
            _policy = WithFrameSources(_policy, content.Frames);
        }
        AddFrames("", content.Frames);
    }

    // Slice 5b: a document's frame-src lists the exact address of each of
    // its served frames, and of the served frames of the frames it builds in
    // place, whose policy is its own; with the address each such frame's
    // owner asks for, which is answered with a redirect, and the https form
    // of each http address, as the top document is answered. A source
    // expression holds no query or fragment, and a path's semicolons and
    // commas are escaped (Content Security Policy Level 3, "path-part").
    // 'none' when there are none.
    public static string WithFrameSources(string policy, IReadOnlyList<RecreationFrame> frames)
    {
        var sources = new SortedSet<string>(StringComparer.Ordinal);
        void Add(string? url)
        {
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return;
            }
            var path = uri.AbsolutePath.Replace(";", "%3B", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal);
            var authority = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
            sources.Add(authority + path);
            if (uri.Scheme == Uri.UriSchemeHttp)
            {
                sources.Add("https" + authority["http".Length..] + path);
            }
        }
        void Walk(IReadOnlyList<RecreationFrame> list)
        {
            foreach (var frame in list)
            {
                if (frame.Way == "served")
                {
                    Add(frame.DocumentUrl);
                    Add(frame.OwnerAddress);
                }
                else if (frame.Way is "in-place" or "srcdoc")
                {
                    Walk(frame.Children);
                }
            }
        }
        Walk(frames);
        var value = sources.Count == 0 ? "'none'" : string.Join(' ', sources);
        return policy.Replace("frame-src 'none'", "frame-src " + value, StringComparison.Ordinal);
    }

    private void AddFrames(string parentKey, IReadOnlyList<RecreationFrame> frames)
    {
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var key = parentKey + "/" + index.ToString(CultureInfo.InvariantCulture);
            byte[]? page = null;
            string? policy = null;
            if (frame is { Way: "served", Html: { } html, ScriptNonce: { } nonce })
            {
                page = Encoding.UTF8.GetBytes(html);
                policy = WithFrameSources(RecordedPageContentSecurityPolicy(nonce, _fontAddress!), frame.Children);
            }
            _frames[key] = new FrameEntry(key, parentKey, frame, page, policy, frame.Resources ?? Recorder.Session.RecordedPageResources.None);
            _frameOrder.Add(key);
            if (frame.Evidence is { } evidence)
            {
                _frameEvidence[key] = JsonSerializer.SerializeToUtf8Bytes(evidence, EvidenceJson);
            }
            AddFrames(key, frame.Children);
        }
    }

    public static void DisposeFrames(IReadOnlyList<RecreationFrame> frames)
    {
        foreach (var frame in frames)
        {
            frame.Resources?.Dispose();
            DisposeFrames(frame.Children);
        }
    }

    // The key of the frame whose owner has the path among a document's
    // frames, or null when it has none. A frame not built has a key, so that
    // its requests are known to be its own, and are refused.
    public string? ChildKey(string parentKey, NodePath ownerPath)
    {
        ArgumentNullException.ThrowIfNull(parentKey);
        ArgumentNullException.ThrowIfNull(ownerPath);
        if (parentKey.Length > 0 && !_frames.ContainsKey(parentKey))
        {
            return null;
        }
        var display = ownerPath.Display;
        return _frames.Values
            .Where(entry => entry.ParentKey == parentKey && entry.Frame.OwnerPath?.Display == display)
            .Select(entry => entry.Key)
            .FirstOrDefault();
    }

    // Records that the recreation asked for a frame's document, or that a
    // frame target was attached for it, for the panel.
    public bool FrameAskedFor(string key)
    {
        lock (_askedFor)
        {
            _askedFor.Add(key);
        }
        return !_frames.TryGetValue(key, out var entry) || entry.Frame.Way != "not-built";
    }

    public void FrameOutOfProcess(string key)
    {
        lock (_askedFor)
        {
            _outOfProcess.Add(key);
        }
    }

    public IReadOnlyList<RecreationFrameStatus> FrameStatuses
    {
        get
        {
            lock (_askedFor)
            {
                return [.. _frameOrder
                    .Select((key, place) => (Entry: _frames[key], Place: place))
                    .OrderBy(item => item.Entry.Key, StringComparer.Ordinal)
                    .Select(item => new RecreationFrameStatus(
                        item.Entry.Key,
                        item.Entry.Frame.OwnerNodeId,
                        item.Entry.Frame.OwnerPath?.Display,
                        item.Entry.Frame.Element,
                        item.Entry.Frame.Way,
                        item.Entry.Frame.DocumentUrl,
                        item.Entry.Frame.Reason,
                        item.Entry.Frame.SameProcessAsParent,
                        _askedFor.Contains(item.Entry.Key),
                        _outOfProcess.Contains(item.Entry.Key))
                    {
                        ParentKey = item.Entry.ParentKey,
                        Owner = item.Entry.Frame.OwnerPath,
                        DocumentKey = item.Entry.Frame.DocumentKey,
                        Choice = item.Entry.Frame.Choice,
                        Basis = item.Entry.Frame.Basis,
                        Origin = item.Entry.Frame.Origin,
                        OriginInherited = item.Entry.Frame.OriginInherited,
                        EvidenceAddress = _frameEvidence.ContainsKey(item.Entry.Key)
                            ? $"evidence/{item.Place.ToString(CultureInfo.InvariantCulture)}.json"
                            : null,
                        Times = TimesOf(item.Entry),
                    })];
            }
        }
    }

    // Slice 5c: what a document's builder reports when its first frame
    // after the build is painted: its key, its performance.timeOrigin, its
    // builder's times on its own clock, and when each frame it built in
    // place was started and built.
    private sealed record BuildReport(
        double TimeOrigin,
        IReadOnlyDictionary<string, double> Times,
        IReadOnlyList<(long OwnerNodeId, double Started, double? Built)> InPlace);

    /// <summary>
    /// Keeps a document's build report, sent by its builder through the
    /// recorder's binding (slice 5c). A report not of the form the builder
    /// writes, or of a key that is not the top document's or a served
    /// frame's, is ignored, and false is returned.
    /// </summary>
    public bool DocumentBuilt(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string key;
        BuildReport report;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("frameKey", out var keyValue) || keyValue.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("timeOrigin", out var origin) || !Number(origin, out var timeOrigin) ||
                !root.TryGetProperty("times", out var times) || times.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            key = keyValue.GetString()!;
            if (key.Length > 0 && !(_frames.TryGetValue(key, out var entry) && entry.Frame.Way == "served"))
            {
                return false;
            }
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var property in times.EnumerateObject())
            {
                if (Number(property.Value, out var value))
                {
                    values[property.Name] = value;
                }
            }
            var inPlace = new List<(long, double, double?)>();
            foreach (var item in frames.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("ownerNodeId", out var owner) || owner.ValueKind != JsonValueKind.Number || !owner.TryGetInt64(out var ownerNodeId) ||
                    !item.TryGetProperty("started", out var started) || !Number(started, out var startedValue))
                {
                    return false;
                }
                double? built = item.TryGetProperty("built", out var builtValue) && Number(builtValue, out var builtNumber)
                    ? builtNumber
                    : null;
                inPlace.Add((ownerNodeId, startedValue, built));
            }
            report = new BuildReport(timeOrigin, values, inPlace);
        }
        catch (JsonException)
        {
            return false;
        }
        lock (_askedFor)
        {
            _built[key] = report;
        }
        return true;
    }

    private static bool Number(JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number);
    }

    // The times of a frame, from the build reports, in milliseconds from
    // the top document's time origin. Called under the lock.
    private RecreationFrameTimes? TimesOf(FrameEntry entry)
    {
        if (!_built.TryGetValue("", out var top))
        {
            return null;
        }
        double? Get(BuildReport report, string name) => report.Times.TryGetValue(name, out var value) ? value : null;
        if (entry.Frame.Way == "served")
        {
            if (!_built.TryGetValue(entry.Key, out var own))
            {
                return null;
            }
            var start = own.TimeOrigin - top.TimeOrigin;
            return new RecreationFrameTimes(
                start,
                Get(own, "builderFinished") + start,
                Get(own, "firstPaint") + start,
                Get(own, "firstPaint"),
                "its own time origin, compared with the top document's");
        }
        if (entry.Frame.Way is "in-place" or "srcdoc")
        {
            // The served document, or the top document, whose builder built
            // this frame: the nearest ancestor not built in place.
            var builder = entry.ParentKey;
            while (builder.Length > 0 && _frames[builder].Frame.Way != "served")
            {
                builder = _frames[builder].ParentKey;
            }
            if (!_built.TryGetValue(builder, out var parent))
            {
                return null;
            }
            var offset = parent.TimeOrigin - top.TimeOrigin;
            foreach (var item in parent.InPlace)
            {
                if (item.OwnerNodeId == entry.Frame.OwnerNodeId)
                {
                    return new RecreationFrameTimes(
                        null,
                        item.Built + offset,
                        Get(parent, "firstPaint") + offset,
                        item.Built - item.Started,
                        builder.Length == 0
                            ? "built in place by the top document's builder, on its clock, from its start at " + item.Started.ToString("0.0", CultureInfo.InvariantCulture) + " ms; painted with it"
                            : $"built in place by the builder of frame {builder}, on its clock, from its start at {(item.Started + offset).ToString("0.0", CultureInfo.InvariantCulture)} ms; painted with it");
                }
            }
        }
        return null;
    }

    public string Token { get; }

    public int Port { get; private set; }

    public string BaseAddress => $"http://127.0.0.1:{Port}/{Token}/";

    public string PageAddress => BaseAddress;

    // The address the recreation's tab loads: the recorded document's, when
    // the page is served at it, or the loopback page.
    public string RecreationAddress => _documentUrl ?? PageAddress;

    // True when the page is served at its recorded address, and the
    // recorder answers every request of the recreation's tab.
    public bool ServedAtRecordedAddress => _documentUrl is not null;

    // An address the page can be served at: absolute, http or https.
    public static bool IsServableAddress(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // The recorder's answer to a request of the recreation's tab, or null
    // when it has none and the request is refused. Only the recorded
    // document's own address is answered, with the page and its policy; a
    // request's URL holds no fragment, so the recorded address's is left
    // out, and an http address is also answered at https, in case the
    // browser upgrades the navigation.
    public RecreationAnswer? Answer(string url) => Answer(url, "Document");

    // Sub-step 3: with the recording's resources, an image request is
    // answered with the image's latest recorded status, MIME type, and bytes,
    // and a request of the builder for a font file at the recorder's own
    // address with the file's recorded bytes. Anything else is refused.
    public RecreationAnswer? Answer(string url, string? resourceType) => Answer(url, resourceType, "");

    // Slice 5b: a request of a frame, by the frame's key. A served frame's
    // document request is answered with its page and policy at its recorded
    // address, and with a redirect to that address when it is for the
    // address its owner asks for; a frame's other requests are answered
    // from the resources of its own document. The top document's key is
    // empty.
    public RecreationAnswer? Answer(string url, string? resourceType, string frameKey)
    {
        ArgumentNullException.ThrowIfNull(frameKey);
        FrameEntry? frame = null;
        if (frameKey.Length > 0 && !_frames.TryGetValue(frameKey, out frame))
        {
            return null;
        }
        if (resourceType != "Document")
        {
            // A frame built in place asks for its resources under its
            // parent's frame ID, as Chromium 147 reported them in the
            // development sandbox (2026-10-07), so a document's resources
            // are answered first, then those of the frames it builds in
            // place, nearest first.
            foreach (var resources in InPlaceResources(frameKey, frame is null ? _resources : frame.Resources))
            {
                if (ResourceAnswer(url, resourceType, resources) is { } found)
                {
                    return found;
                }
            }
            return null;
        }
        if (frame is null)
        {
            return _documentUrl is not null && SameDocument(url, _documentUrl) ? Page(_page, _policy) : null;
        }
        if (frame is not { Frame.Way: "served", Page: { } page, Policy: { } policy, Frame.DocumentUrl: { } address })
        {
            return null;
        }
        if (SameDocument(url, address))
        {
            return Page(page, policy);
        }
        if (frame.Frame.OwnerAddress is { } asked && SameDocument(url, asked))
        {
            return new RecreationAnswer(302,
            [
                new("Location", address),
                new("Cache-Control", "no-store"),
                new("Referrer-Policy", "no-referrer"),
            ], []);
        }
        return null;
    }

    // A document's resources, then those of the frames it builds in place,
    // breadth first.
    private IEnumerable<Recorder.Session.RecordedPageResources> InPlaceResources(string key, Recorder.Session.RecordedPageResources own)
    {
        yield return own;
        var parents = new Queue<string>([key]);
        while (parents.Count > 0)
        {
            var parent = parents.Dequeue();
            foreach (var entry in _frames.Values
                .Where(entry => entry.ParentKey == parent && entry.Frame.Way is "in-place" or "srcdoc")
                .OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                yield return entry.Resources;
                parents.Enqueue(entry.Key);
            }
        }
    }

    private static RecreationAnswer Page(byte[] page, string policy) => new(200,
    [
        new("Content-Type", "text/html; charset=utf-8"),
        new("Content-Security-Policy", policy),
        new("Cache-Control", "no-store"),
        new("X-Content-Type-Options", "nosniff"),
        new("Referrer-Policy", "no-referrer"),
    ], page);

    private RecreationAnswer? ResourceAnswer(string url, string? resourceType, Recorder.Session.RecordedPageResources resources)
    {
        if (_documentUrl is null)
        {
            return null;
        }
        if (_fontAddress is { } fonts && url.StartsWith(fonts, StringComparison.Ordinal))
        {
            var digest = url[fonts.Length..];
            // The builder's fetch() is reported as "Fetch" by some Chromium
            // builds and as "XHR" by the instrumented Chromium on the target
            // machine (2026-10-02), so either is answered.
            if (resourceType is not ("Fetch" or "XHR") || digest.Length != 64 || !digest.All(Uri.IsHexDigit) ||
                !resources.Faces.Any(face => face.Digest == digest) ||
                resources.FontFile(digest) is not { } file)
            {
                return null;
            }
            return new RecreationAnswer(200,
            [
                new("Content-Type", "application/octet-stream"),
                new("Access-Control-Allow-Origin", "*"),
                new("Cache-Control", "no-store"),
                new("X-Content-Type-Options", "nosniff"),
            ], file);
        }
        // Slice 4e: a style sheet's address is answered with the text it
        // arrived with, in UTF-8, as Blink decoded it when it was recorded.
        if (resourceType == "Stylesheet")
        {
            if (resources.StyleSheets.ArrivedDigest(url) is not { } sheetDigest ||
                resources.StyleSheetText(sheetDigest) is not { } sheetText)
            {
                return null;
            }
            return new RecreationAnswer(200,
            [
                new("Content-Type", "text/css; charset=utf-8"),
                new("Cache-Control", "no-store"),
                new("X-Content-Type-Options", "nosniff"),
            ], sheetText);
        }
        if (resourceType != "Image" || resources.Image(url) is not { } image ||
            resources.ImageBytes(image.Digest) is not { } bytes)
        {
            return null;
        }
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Content-Type", image.MimeType),
            new("Cache-Control", "no-store"),
            new("X-Content-Type-Options", "nosniff"),
        };
        // Slice 4b sub-step 2a: the frame the image is held at, which the
        // instrumented Chromium reads in the recreation mode. An image with
        // no recorded frame has no header and is held at its first frame.
        if (resources.ImageFrames.Frame(url) is { } frame)
        {
            headers.Add(new(ImageFrameHeader, frame.Index.ToString(CultureInfo.InvariantCulture)));
        }
        return new RecreationAnswer(image.Status, headers, bytes);
    }

    /// <summary>The response header that names the frame an animated image is held at.</summary>
    public const string ImageFrameHeader = "X-A11y-Recorder-Image-Frame";

    public static bool SameDocument(string requested, string recorded)
    {
        static string WithoutFragment(string url) => url.IndexOf('#') is var hash and >= 0 ? url[..hash] : url;
        var request = WithoutFragment(requested);
        var document = WithoutFragment(recorded);
        return request == document ||
               (document.StartsWith("http:", StringComparison.Ordinal) && request == "https:" + document["http:".Length..]);
    }

    public string EvidenceAddress => BaseAddress + "evidence.json";

    public string BlockedAddress => BaseAddress + "blocked.json";

    // Records a navigation the recreation browser refused, for the panel.
    public void AddBlocked(BlockedNavigation navigation)
    {
        lock (_blocked)
        {
            _blocked.Add(navigation);
        }
    }

    // Records how long a step of opening the recreation took, for the panel.
    public void AddTiming(string step, TimeSpan duration)
    {
        lock (_timings)
        {
            _timings.Add(new RecreationTiming(step, Math.Round(duration.TotalMilliseconds, 1)));
        }
    }

    // How the recreation's window holds the recorded frame, set once the
    // window is sized, before the page is loaded; null without a viewport.
    private RecreationWindowFit? _windowFit;

    public RecreationWindowFit? WindowFit
    {
        get => Volatile.Read(ref _windowFit);
        set => Volatile.Write(ref _windowFit, value);
    }

    public IReadOnlyList<RecreationTiming> Timings
    {
        get
        {
            lock (_timings)
            {
                return [.. _timings];
            }
        }
    }

    public IReadOnlyList<BlockedNavigation> Blocked
    {
        get
        {
            lock (_blocked)
            {
                return [.. _blocked];
            }
        }
    }

    public static async Task<RecreationServer> StartAsync(RecreationContent content, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, 0);
        });
        var application = builder.Build();
        var server = new RecreationServer(application, NewToken(), content);
        application.Run(server.HandleAsync);
        await application.StartAsync(cancellationToken);
        var address = application.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        server.Port = new Uri(address).Port;
        return server;
    }

    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private async Task HandleAsync(HttpContext context)
    {
        var response = context.Response;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";

        var request = context.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }
        if (!string.Equals(request.Host.Value, $"127.0.0.1:{Port}", StringComparison.Ordinal) ||
            !TryResource(request.Path.Value, out var resource))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        byte[] body;
        switch (resource)
        {
            case "":
                response.ContentType = "text/html; charset=utf-8";
                response.Headers["Content-Security-Policy"] = _policy;
                body = _page;
                break;
            case "evidence.json":
                response.ContentType = "application/json; charset=utf-8";
                body = _evidence;
                break;
            case "blocked.json":
                response.ContentType = "application/json; charset=utf-8";
                body = JsonSerializer.SerializeToUtf8Bytes(Blocked, EvidenceJson);
                break;
            case "window.json":
                response.ContentType = "application/json; charset=utf-8";
                body = JsonSerializer.SerializeToUtf8Bytes(WindowFit, EvidenceJson);
                break;
            case "timings.json":
                response.ContentType = "application/json; charset=utf-8";
                body = JsonSerializer.SerializeToUtf8Bytes(Timings, EvidenceJson);
                break;
            case "frames.json":
                response.ContentType = "application/json; charset=utf-8";
                body = JsonSerializer.SerializeToUtf8Bytes(FrameStatuses, EvidenceJson);
                break;
            // Slice 5c: a frame's own evidence, by its place in the list.
            case var frameEvidence when FrameEvidence(frameEvidence) is { } evidence:
                response.ContentType = "application/json; charset=utf-8";
                body = evidence;
                break;
            // Slice 4h: a listed script's recorded text, for the evidence
            // panel's viewer, as plain text, which is never run.
            case var script when script.StartsWith(ScriptResourcePrefix, StringComparison.Ordinal) &&
                ScriptText(script[ScriptResourcePrefix.Length..]) is { } text:
                response.ContentType = "text/plain; charset=utf-8";
                response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
                body = text;
                break;
            default:
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
        }
        response.ContentLength = body.Length;
        if (HttpMethods.IsGet(request.Method))
        {
            await response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    /// <summary>The resource a script's text is served at, followed by its digest (slice 4h).</summary>
    public const string ScriptResourcePrefix = "script/";

    // The UTF-8 text of a script of the document's list, by its digest.
    private byte[]? ScriptText(string digest)
    {
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
        {
            return null;
        }
        // Slice 5c: a script listed in the top document's evidence or in a
        // frame's, read through that document's resources.
        var sources = new[] { _scripts }.Concat(_frames.Values
            .Where(entry => entry.Frame.Evidence is not null)
            .Select(entry => entry.Resources.Scripts));
        foreach (var scripts in sources)
        {
            if (scripts.Scripts.Any(item => item.Digest == digest) && scripts.Text(digest) is { } text)
            {
                return Encoding.UTF8.GetBytes(text);
            }
        }
        return null;
    }

    // "evidence/<n>.json": the evidence of the n-th frame added, when it
    // has evidence.
    private byte[]? FrameEvidence(string resource)
    {
        const string prefix = "evidence/";
        const string suffix = ".json";
        if (!resource.StartsWith(prefix, StringComparison.Ordinal) || !resource.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }
        var number = resource[prefix.Length..^suffix.Length];
        if (number.Length == 0 || number.Length > 4 || !number.All(char.IsAsciiDigit) ||
            !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var place) ||
            place >= _frameOrder.Count || number != place.ToString(CultureInfo.InvariantCulture))
        {
            return null;
        }
        return _frameEvidence.TryGetValue(_frameOrder[place], out var evidence) ? evidence : null;
    }

    // The path is /<token>/<resource>. The token is compared in constant time.
    private bool TryResource(string? path, out string resource)
    {
        resource = "";
        if (path is null || path.Length < 2 || path[0] != '/')
        {
            return false;
        }
        var end = path.IndexOf('/', 1);
        if (end < 0)
        {
            return false;
        }
        var token = Encoding.ASCII.GetBytes(path[1..end]);
        if (!CryptographicOperations.FixedTimeEquals(token, _token))
        {
            return false;
        }
        resource = path[(end + 1)..];
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();
        await _application.DisposeAsync();
        _resources.Dispose();
        foreach (var frame in _frames.Values)
        {
            frame.Resources.Dispose();
        }
        _scriptsOwner?.Dispose();
    }
}
