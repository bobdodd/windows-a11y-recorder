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
public sealed class RecreationServer : IAsyncDisposable
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
    private readonly List<BlockedNavigation> _blocked = [];
    private readonly List<RecreationTiming> _timings = [];

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
        _policy = content.ScriptNonce is not { } nonce
            ? PageContentSecurityPolicy
            : _fontAddress is { } fonts
                ? RecordedPageContentSecurityPolicy(nonce, fonts)
                : RecordedPageContentSecurityPolicy(nonce);
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
    public RecreationAnswer? Answer(string url, string? resourceType)
    {
        if (resourceType != "Document")
        {
            return ResourceAnswer(url, resourceType);
        }
        if (_documentUrl is null || !SameDocument(url, _documentUrl))
        {
            return null;
        }
        return new RecreationAnswer(200,
        [
            new("Content-Type", "text/html; charset=utf-8"),
            new("Content-Security-Policy", _policy),
            new("Cache-Control", "no-store"),
            new("X-Content-Type-Options", "nosniff"),
            new("Referrer-Policy", "no-referrer"),
        ], _page);
    }

    private RecreationAnswer? ResourceAnswer(string url, string? resourceType)
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
                !_resources.Faces.Any(face => face.Digest == digest) ||
                _resources.FontFile(digest) is not { } file)
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
            if (_resources.StyleSheets.ArrivedDigest(url) is not { } sheetDigest ||
                _resources.StyleSheetText(sheetDigest) is not { } sheetText)
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
        if (resourceType != "Image" || _resources.Image(url) is not { } image ||
            _resources.ImageBytes(image.Digest) is not { } bytes)
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
        if (_resources.ImageFrames.Frame(url) is { } frame)
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
            case "timings.json":
                response.ContentType = "application/json; charset=utf-8";
                body = JsonSerializer.SerializeToUtf8Bytes(Timings, EvidenceJson);
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
    }
}
