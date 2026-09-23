namespace Recorder.Tests;

// The JSON the bridge writes for each browser.cookie record in protocol 0.23,
// shared by the ingest and archive tests so both check the same shapes. None
// of these shapes has a field for a cookie value.
internal static class BrowserCookiePayloads
{
    public const string RendererContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": "dom-document-19",
          "executionWorldId": "world-0",
          "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
        }
        """;

    private const string LocationJson = """
        {
          "scriptId": "12",
          "url": "https://example.test/app.js",
          "line": 4,
          "column": 17,
          "functionName": "remember",
          "sourceHash": null
        }
        """;

    private const string WorldJson = """
        {
          "kind": "main",
          "blinkWorldId": 0,
          "name": null,
          "stableId": null
        }
        """;

    public static readonly string DocumentCookieRead = $$"""
        {
          "context": {{RendererContextJson}},
          "accessId": "cookie-access-1",
          "cookieUrl": "https://example.test/",
          "outcome": "returned",
          "servedFrom": "cookie-manager",
          "cookieCount": 2,
          "cookieNames": ["session", ""],
          "cookieNamesTruncated": false,
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string DocumentCookieWrite = $$"""
        {
          "context": {{RendererContextJson}},
          "accessId": "cookie-access-2",
          "cookieUrl": "https://example.test/",
          "outcome": "sent-to-cookie-manager",
          "name": "theme",
          "attributes": {
            "domain": null,
            "path": "/",
            "sameSite": "Lax",
            "partitioned": false,
            "expiresPresent": false,
            "secure": true,
            "httpOnly": false,
            "maxAgePresent": true,
            "attributeNames": ["path", "samesite", "secure", "max-age"]
          },
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string CookieStoreReadRequest = $$"""
        {
          "context": {{RendererContextJson}},
          "requestId": "cookie-store-request-1",
          "method": "getAll",
          "contextKind": "window",
          "outcome": "sent-to-cookie-manager",
          "name": null,
          "url": null,
          "attributes": null,
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string CookieStoreWriteRequest = $$"""
        {
          "context": {{RendererContextJson}},
          "requestId": "cookie-store-request-2",
          "method": "set",
          "contextKind": "window",
          "outcome": "sent-to-cookie-manager",
          "name": "theme",
          "url": null,
          "attributes": {
            "domain": null,
            "path": "/",
            "sameSite": "strict",
            "partitioned": false,
            "expiresPresent": false
          },
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    private const string ResultContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": null,
          "executionWorldId": null,
          "documentToken": null
        }
        """;

    public static readonly string CookieStoreReadResult = $$"""
        {
          "context": {{ResultContextJson}},
          "requestId": "cookie-store-request-1",
          "method": "getAll",
          "outcome": "resolved",
          "success": null,
          "cookieCount": 1,
          "cookieNames": ["theme"],
          "cookieNamesTruncated": false
        }
        """;

    public static readonly string CookieStoreWriteResult = $$"""
        {
          "context": {{ResultContextJson}},
          "requestId": "cookie-store-request-2",
          "method": "set",
          "outcome": "resolved",
          "success": true,
          "cookieCount": null,
          "cookieNames": null,
          "cookieNamesTruncated": null
        }
        """;

    public static readonly string CookieStoreChange = $$"""
        {
          "context": {{RendererContextJson}},
          "contextKind": "window",
          "name": "theme",
          "domain": "example.test",
          "path": "/",
          "cause": "INSERTED",
          "dispatched": true
        }
        """;

    public static readonly string CookieAccess = """
        {
          "context": {
            "browserInstanceId": "browser-1",
            "processId": 1200,
            "processType": "browser",
            "profileId": null,
            "browserContextId": null,
            "pageId": "frame-1",
            "frameId": "frame-1",
            "documentId": "document-navigation-3",
            "executionWorldId": null,
            "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
          },
          "observer": "frame",
          "navigationId": null,
          "rendererProcessId": 3440,
          "accessType": "change",
          "url": "https://example.test/set",
          "frameOrigin": "https://example.test",
          "topFrameOrigin": "https://example.test",
          "requestId": "7A1C9E3F2B",
          "adTagged": false,
          "cookieCount": 2,
          "cookies": [
            {
              "name": "session",
              "parsed": true,
              "domain": "example.test",
              "path": "/",
              "sameSite": "lax",
              "secure": true,
              "httpOnly": true,
              "hostOnly": true,
              "partitioned": false,
              "persistent": false,
              "expired": false,
              "included": true,
              "exclusionReasons": [],
              "warningReasons": [],
              "exemptionReason": null
            },
            {
              "name": "tracker",
              "parsed": false,
              "domain": null,
              "path": null,
              "sameSite": null,
              "secure": null,
              "httpOnly": null,
              "hostOnly": null,
              "partitioned": null,
              "persistent": null,
              "expired": null,
              "included": false,
              "exclusionReasons": ["EXCLUDE_USER_PREFERENCES"],
              "warningReasons": [],
              "exemptionReason": null
            }
          ],
          "cookiesTruncated": false
        }
        """;

    public static IEnumerable<(string EventType, string Json)> All()
    {
        yield return ("document-cookie-read", DocumentCookieRead);
        yield return ("document-cookie-write", DocumentCookieWrite);
        yield return ("cookie-store-request", CookieStoreReadRequest);
        yield return ("cookie-store-request", CookieStoreWriteRequest);
        yield return ("cookie-store-result", CookieStoreReadResult);
        yield return ("cookie-store-result", CookieStoreWriteResult);
        yield return ("cookie-store-change", CookieStoreChange);
        yield return ("cookie-access", CookieAccess);
    }
}
