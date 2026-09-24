namespace Recorder.Tests;

// The JSON the bridge writes for each browser.network record in protocol 0.27,
// shared by the ingest and archive tests so both check the same shapes. Byte
// counts are written as JSON doubles. No shape has a field for a body, and a
// withheld header value is null.
internal static class BrowserNetworkPayloads
{
    private const string RendererContextJson = """
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

    private const string WorkerContextJson = """
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

    private const string BrowserContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 1200,
          "processType": "browser",
          "profileId": null,
          "browserContextId": null,
          "pageId": "frame-1",
          "frameId": "frame-1",
          "documentId": null,
          "executionWorldId": null,
          "documentToken": null
        }
        """;

    private const string NavigationContextJson = """
        {
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
        }
        """;

    private const string WindowScopeJson = """
        { "contextKind": "window", "workerToken": null, "globalObjectUrl": null }
        """;

    private const string WorkerScopeJson = """
        {
          "contextKind": "dedicated-worker",
          "workerToken": "5A0D8C1E6B2F4A97A1C3E5F7092B4D6E",
          "globalObjectUrl": "https://example.test/worker.js"
        }
        """;

    private const string LocationJson = """
        {
          "scriptId": "12",
          "url": "https://example.test/app.js",
          "line": 4,
          "column": 17,
          "functionName": "load",
          "sourceHash": null
        }
        """;

    private const string WorldJson = """
        { "kind": "main", "blinkWorldId": 0, "name": null, "stableId": null }
        """;

    private const string LoadTimingJson = """
        {
          "requestStartBeforeRecordMilliseconds": 62.5,
          "proxyStart": null,
          "proxyEnd": null,
          "domainLookupStart": 0.25,
          "domainLookupEnd": 3.5,
          "connectStart": 3.5,
          "connectEnd": 21.0,
          "sslStart": 8.0,
          "sslEnd": 21.0,
          "workerStart": null,
          "workerReady": null,
          "workerFetchStart": null,
          "workerRespondWithSettled": null,
          "workerRouterEvaluationStart": null,
          "workerCacheLookupStart": null,
          "sendStart": 21.25,
          "sendEnd": 21.5,
          "receiveHeadersStart": 40.0,
          "receiveHeadersEnd": 41.0,
          "receiveNonInformationalHeadersStart": 40.0,
          "receiveEarlyHintsStart": null,
          "pushStart": null,
          "pushEnd": null,
          "responseEnd": 44.0
        }
        """;

    public static readonly string Request = $$"""
        {
          "inspectorId": "17",
          "requestId": "3440.17",
          "url": "https://example.test/api/items?page=2&key=abc",
          "method": "GET",
          "resourceType": "Fetch",
          "initiator": {
            "type": "script",
            "url": "https://example.test/app.js",
            "line": 4,
            "column": 17,
            "linkPreload": false
          },
          "internal": false,
          "destination": "empty",
          "mode": "cors",
          "credentialsMode": "same-origin",
          "redirectMode": "follow",
          "cacheMode": "default",
          "priority": "high",
          "initialPriority": "high",
          "fetchPriorityHint": "auto",
          "renderBlocking": "non-blocking",
          "referrer": "https://example.test/",
          "referrerPolicy": "strict-origin-when-cross-origin",
          "keepalive": false,
          "userGesture": true,
          "adResource": false,
          "formSubmission": false,
          "headerCount": 3,
          "headers": [
            { "name": "Accept", "value": "application/json", "valueRedacted": false, "redactionReason": null },
            { "name": "Authorization", "value": null, "valueRedacted": true, "redactionReason": "credential-header" },
            { "name": "X-Api-Key", "value": null, "valueRedacted": true, "redactionReason": "credential-name" }
          ],
          "headersTruncated": false
        }
        """;

    public static readonly string Response = $$"""
        {
          "url": "https://example.test/api/items?page=2&key=abc",
          "responseUrl": null,
          "status": 200,
          "statusText": "OK",
          "mimeType": "application/json",
          "charset": "utf-8",
          "alpnProtocol": "h2",
          "connectionInfo": "h2",
          "remoteAddress": { "ip": "192.0.2.10", "port": 443 },
          "connectionId": 42.0,
          "connectionReused": false,
          "wasCached": false,
          "fetchedViaServiceWorker": false,
          "serviceWorkerResponseSource": "unspecified",
          "inPrefetchCache": false,
          "networkAccessed": true,
          "fromArchive": false,
          "cookieInRequest": true,
          "responseType": "cors",
          "encodedDataLength": 1834.0,
          "expectedContentLength": -1.0,
          "headerCount": 2,
          "headers": [
            { "name": "content-type", "value": "application/json; charset=utf-8", "valueRedacted": false, "redactionReason": null },
            { "name": "set-cookie", "value": null, "valueRedacted": true, "redactionReason": "credential-header" }
          ],
          "headersTruncated": false,
          "timing": {{LoadTimingJson}}
        }
        """;

    private static readonly string RedirectResponse = """
        {
          "url": "https://example.test/old",
          "responseUrl": null,
          "status": 301,
          "statusText": "Moved Permanently",
          "mimeType": "text/html",
          "charset": null,
          "alpnProtocol": null,
          "connectionInfo": null,
          "remoteAddress": null,
          "connectionId": 0.0,
          "connectionReused": true,
          "wasCached": false,
          "fetchedViaServiceWorker": false,
          "serviceWorkerResponseSource": "unspecified",
          "inPrefetchCache": false,
          "networkAccessed": true,
          "fromArchive": false,
          "cookieInRequest": false,
          "responseType": "basic",
          "encodedDataLength": 212.0,
          "expectedContentLength": 0.0,
          "headerCount": 1,
          "headers": [
            { "name": "location", "value": "/new", "valueRedacted": false, "redactionReason": null }
          ],
          "headersTruncated": false,
          "timing": null
        }
        """;

    public static readonly string RequestWillBeSent = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "request": {{Request}},
          "redirect": false,
          "redirectResponse": null,
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string RedirectRequestWillBeSent = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "request": {{Request}},
          "redirect": true,
          "redirectResponse": {{RedirectResponse}},
          "location": null,
          "world": null
        }
        """;

    public static readonly string ResponseReceived = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "17",
          "requestId": "3440.17",
          "responseSource": "loader",
          "response": {{Response}}
        }
        """;

    public static readonly string RequestFinished = $$"""
        {
          "context": {{WorkerContextJson}},
          "scope": {{WorkerScopeJson}},
          "inspectorId": "17",
          "encodedDataLength": 1834.0,
          "decodedBodyLength": 5120.0,
          "finishBeforeRecordMilliseconds": 0.75
        }
        """;

    public static readonly string RequestFailed = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "18",
          "url": "https://other.test/data",
          "netError": -3,
          "netErrorName": "net::ERR_ABORTED",
          "cancellation": false,
          "timeout": false,
          "accessCheck": true,
          "blockedByResponse": false,
          "blockedByOrb": false,
          "hasCopyInCache": false,
          "cancelledFromHttpError": false,
          "internal": false,
          "blockedReason": null,
          "corsError": { "error": "MissingAllowOriginHeader", "failedParameter": null }
        }
        """;

    public static readonly string MemoryCacheHit = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "staticData": false,
          "request": {{Request}},
          "response": {{Response}}
        }
        """;

    public static readonly string RequestHeadersSent = $$"""
        {
          "context": {{BrowserContextJson}},
          "devtoolsAgentId": null,
          "requestId": "3440.17",
          "sentBeforeRecordMilliseconds": 40.5,
          "headerCount": 3,
          "headers": [
            { "name": ":authority", "value": "example.test", "valueRedacted": false, "redactionReason": null },
            { "name": "cookie", "value": null, "valueRedacted": true, "redactionReason": "credential-header" },
            { "name": "user-agent", "value": "Mozilla/5.0", "valueRedacted": false, "redactionReason": null }
          ],
          "headersTruncated": false,
          "cookieCount": 1,
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
            }
          ],
          "cookiesTruncated": false
        }
        """;

    public static readonly string ResponseHeadersReceived = $$"""
        {
          "context": {{BrowserContextJson}},
          "devtoolsAgentId": "B1C2D3E4F5A6B7C8D9E0F1A2B3C4D5E6",
          "requestId": "3440.17",
          "status": 200,
          "headerCount": 1,
          "headers": [
            { "name": "set-cookie", "value": null, "valueRedacted": true, "redactionReason": "credential-header" }
          ],
          "headersTruncated": false,
          "cookieCount": 1,
          "cookies": [
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

    public static readonly string NavigationResponse = $$"""
        {
          "context": {{NavigationContextJson}},
          "navigationId": "navigation-3",
          "requestId": "A0B1C2D3E4F5A6B7C8D9E0F1A2B3C4D5",
          "url": "https://example.test/new",
          "method": "GET",
          "committed": true,
          "errorPage": false,
          "sameDocument": false,
          "download": false,
          "backForwardCache": false,
          "netError": 0,
          "netErrorName": null,
          "redirectChain": ["https://example.test/old", "https://example.test/new"],
          "requestHeaderCount": 1,
          "requestHeaders": [
            { "name": "Upgrade-Insecure-Requests", "value": "1", "valueRedacted": false, "redactionReason": null }
          ],
          "requestHeadersTruncated": false,
          "response": {
            "status": 200,
            "statusText": "OK",
            "mimeType": "text/html",
            "wasCached": false,
            "remoteAddress": { "ip": "2001:db8::10", "port": 443 },
            "connectionInfo": "h2",
            "headerCount": 1,
            "headers": [
              { "name": "content-type", "value": "text/html", "valueRedacted": false, "redactionReason": null }
            ],
            "headersTruncated": false
          },
          "timing": {
          "navigationStartBeforeRecordMilliseconds": 180.25,
          "loaderStart": 1.0,
          "firstRequestStart": 2.0,
          "firstResponseStart": 30.0,
          "firstLoaderCallback": 31.0,
          "finalRequestStart": 33.0,
          "finalResponseStart": 60.0,
          "finalNonInformationalResponseStart": 60.0,
          "finalLoaderCallback": 61.0,
          "requestFailed": null,
          "commitSent": 62.0,
          "commitReceived": 70.0,
          "commitReplySent": 71.0,
          "didCommit": 72.0,
          "finalRequestDomainLookupStart": 33.5,
          "finalRequestDomainLookupEnd": 34.0,
          "finalRequestConnectStart": 34.0,
          "finalRequestConnectEnd": 50.0,
          "finalRequestSslStart": 40.0
        }
        }
        """;

    // A navigation that failed before any response reports no response head.
    public static readonly string FailedNavigationResponse = $$"""
        {
          "context": {{NavigationContextJson}},
          "navigationId": "navigation-4",
          "requestId": null,
          "url": "https://unreachable.test/",
          "method": "GET",
          "committed": true,
          "errorPage": true,
          "sameDocument": false,
          "download": false,
          "backForwardCache": false,
          "netError": -105,
          "netErrorName": "net::ERR_NAME_NOT_RESOLVED",
          "redirectChain": ["https://unreachable.test/"],
          "requestHeaderCount": 0,
          "requestHeaders": [],
          "requestHeadersTruncated": false,
          "response": null,
          "timing": null
        }
        """;

    private const string RealtimeHeadersJson = """
        "headerCount": 2,
        "headers": [
          { "name": "Upgrade", "value": "websocket", "valueRedacted": false, "redactionReason": null },
          { "name": "Sec-WebSocket-Key", "value": null, "valueRedacted": true, "redactionReason": "credential-name" }
        ],
        "headersTruncated": false
        """;

    public static readonly string WebSocketCreated = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "31",
          "url": "ws://127.0.0.1:8123/socket?room=lobby",
          "requestedProtocols": "chat",
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string WebSocketHandshakeRequest = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "31",
          "url": "ws://127.0.0.1:8123/socket?room=lobby",
          "cookieNames": [],
          {{RealtimeHeadersJson}}
        }
        """;

    public static readonly string WebSocketHandshakeResponse = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "31",
          "extensions": null,
          "url": "ws://127.0.0.1:8123/socket?room=lobby",
          "httpVersion": "1.1",
          "status": 101,
          "statusText": "Switching Protocols",
          "remoteAddress": { "ip": "127.0.0.1", "port": 8123 },
          "selectedProtocol": "chat",
          "setCookieNames": ["room_pref"],
          "headerCount": 2,
          "headers": [
            { "name": "Upgrade", "value": "websocket", "valueRedacted": false, "redactionReason": null },
            { "name": "Set-Cookie", "value": null, "valueRedacted": true, "redactionReason": "credential-header" }
          ],
          "headersTruncated": false
        }
        """;

    // The token value in the sent text was withheld and the marker written in
    // its place.
    public static readonly string WebSocketMessageSent = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "31",
          "opcode": "text",
          "payloadLength": 58.0,
          "payload": {
            "text": "{\"type\":\"auth\",\"token\":\"[withheld]\",\"room\":\"lobby\"}",
            "truncated": false,
            "withheld": [{ "offset": 24, "reason": "credential-value" }]
          },
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string WebSocketBinaryMessageReceived = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "31",
          "opcode": "binary",
          "payloadLength": 1024.0,
          "payload": null
        }
        """;

    public static readonly string WebSocketCloseRequested = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "31",
          "code": 1000,
          "reason": { "text": "done", "truncated": false, "withheld": [] },
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string WebSocketError = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "32",
          "message": "WebSocket connection to 'ws://127.0.0.1:1/' failed"
        }
        """;

    public static readonly string WebSocketClosed = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "31",
          "cause": "dropped",
          "wasClean": true,
          "code": 1000,
          "reason": { "text": "done", "truncated": false, "withheld": [] }
        }
        """;

    public static readonly string WebSocketDisconnected = $$"""
        {
          "context": {{WorkerContextJson}},
          "scope": {{WorkerScopeJson}},
          "inspectorId": "33",
          "cause": "disconnected",
          "wasClean": null,
          "code": null,
          "reason": null
        }
        """;

    public static readonly string EventSourceMessage = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "inspectorId": "34",
          "url": "http://127.0.0.1:8123/events",
          "eventType": "message",
          "lastEventId": { "text": "7", "truncated": false, "withheld": [] },
          "dataLength": 11.0,
          "data": { "text": "status=open", "truncated": false, "withheld": [] }
        }
        """;

    public static readonly string WebTransportCreated = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "transportId": "1",
          "url": "https://127.0.0.1:4433/wt",
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string WebTransportEstablished = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "transportId": "1",
          "maxDatagramSize": 1200.0,
          "url": null,
          "httpVersion": "3.0",
          "status": 200,
          "statusText": null,
          "remoteAddress": null,
          "selectedProtocol": null,
          "setCookieNames": [],
          "headerCount": 0,
          "headers": [],
          "headersTruncated": false
        }
        """;

    public static readonly string WebTransportCloseRequested = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "transportId": "1",
          "code": null,
          "reason": null,
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string WebTransportClosed = $$"""
        {
          "context": {{RendererContextJson}},
          "scope": {{WindowScopeJson}},
          "transportId": "1",
          "abrupt": true,
          "code": null,
          "reason": null
        }
        """;

    public static IEnumerable<(string EventType, string Json)> All()
    {
        yield return ("request-will-be-sent", RequestWillBeSent);
        yield return ("request-will-be-sent", RedirectRequestWillBeSent);
        yield return ("response-received", ResponseReceived);
        yield return ("request-finished", RequestFinished);
        yield return ("request-failed", RequestFailed);
        yield return ("memory-cache-hit", MemoryCacheHit);
        yield return ("request-headers-sent", RequestHeadersSent);
        yield return ("response-headers-received", ResponseHeadersReceived);
        yield return ("navigation-response", NavigationResponse);
        yield return ("navigation-response", FailedNavigationResponse);
        yield return ("websocket-created", WebSocketCreated);
        yield return ("websocket-handshake-request", WebSocketHandshakeRequest);
        yield return ("websocket-handshake-response", WebSocketHandshakeResponse);
        yield return ("websocket-message-sent", WebSocketMessageSent);
        yield return ("websocket-message-received", WebSocketBinaryMessageReceived);
        yield return ("websocket-close-requested", WebSocketCloseRequested);
        yield return ("websocket-error", WebSocketError);
        yield return ("websocket-closed", WebSocketClosed);
        yield return ("websocket-closed", WebSocketDisconnected);
        yield return ("event-source-message", EventSourceMessage);
        yield return ("web-transport-created", WebTransportCreated);
        yield return ("web-transport-established", WebTransportEstablished);
        yield return ("web-transport-close-requested", WebTransportCloseRequested);
        yield return ("web-transport-closed", WebTransportClosed);
    }
}
