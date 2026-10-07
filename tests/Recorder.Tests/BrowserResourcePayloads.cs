namespace Recorder.Tests;

// The JSON the bridge writes for each browser.resources record in protocol
// 0.40, shared by the validator and receiver tests so both check the same
// shapes.
internal static class BrowserResourcePayloads
{
    public const string Digest = "185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969";

    private const string DocumentContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": "dom-document-19",
          "executionWorldId": null,
          "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
        }
        """;

    private const string ProcessContextJson = """
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

    // "Hello" in base64, five bytes.
    public static readonly string FontFile = $$"""
        {
          "context": {{ProcessContextJson}},
          "digest": "{{Digest}}",
          "size": "5",
          "bytes": "SGVsbG8="
        }
        """;

    public static readonly string FaceAdded = $$"""
        {
          "context": {{DocumentContextJson}},
          "faceNumber": "3"
        }
        """;

    public static readonly string FaceLoaded = $$"""
        {
          "context": {{DocumentContextJson}},
          "faceNumber": "3",
          "family": "Open Sans",
          "descriptors": {
            "style": "normal",
            "weight": "400",
            "stretch": "normal",
            "unicodeRange": "U+0-10FFFF",
            "variant": "normal",
            "featureSettings": "normal",
            "display": "swap",
            "ascentOverride": "normal",
            "descentOverride": "normal",
            "lineGapOverride": "normal",
            "sizeAdjust": "100%"
          },
          "source": {
            "kind": "url",
            "url": "https://www.example.org/fonts/open-sans.woff2"
          },
          "fontFile": { "digest": "{{Digest}}", "index": 0 }
        }
        """;

    public static readonly string ImageResource = $$"""
        {
          "context": {{ProcessContextJson}},
          "url": "https://www.example.org/images/logo.png",
          "responseUrl": "https://cdn.example.org/images/logo.png",
          "status": 200,
          "mimeType": "image/png",
          "size": "5",
          "digest": "{{Digest}}",
          "dataRecorded": true,
          "imageId": "41"
        }
        """;

    // Protocol 0.48: a paint image made for an element's own animation.
    public static readonly string ImagePaintImage = $$"""
        {
          "context": {{ProcessContextJson}},
          "imageId": "41",
          "paintImageId": "57",
          "sequence": "own",
          "nodeId": 88,
          "syncTargetPaintImageId": "41"
        }
        """;

    // Protocol 0.51: a style sheet as it arrived.
    public static readonly string StyleSheetResource = $$"""
        {
          "context": {{ProcessContextJson}},
          "url": "https://www.example.org/site.css",
          "responseUrl": null,
          "status": 200,
          "mimeType": "text/css",
          "size": "5",
          "digest": "{{Digest}}",
          "textRecorded": true
        }
        """;

    // Protocol 0.51: an update of a document's active sheets, with a sheet
    // given in full, an import, a sheet unchanged since the last record, and
    // an adopted constructed sheet.
    public static readonly string StyleSheetsUpdated = $$"""
        {
          "context": {{DocumentContextJson}},
          "scopes": [
            {
              "scopeNodeId": 1,
              "sheets": [
                {
                  "sheet": "1", "kind": "link", "ownerNodeId": 12, "parentSheet": null, "ruleIndex": null,
                  "href": "https://www.example.org/site.css", "media": "", "title": "", "disabled": false,
                  "active": true, "textSource": "arrived", "textDigest": "{{Digest}}"
                },
                {
                  "sheet": "2", "kind": "import", "ownerNodeId": null, "parentSheet": "1", "ruleIndex": 0,
                  "href": "https://www.example.org/more.css", "media": "", "title": "", "disabled": false,
                  "active": true, "textSource": "arrived", "textDigest": "{{Digest}}"
                },
                { "sheet": "3" }
              ],
              "adopted": [
                {
                  "sheet": "4", "kind": "constructed", "ownerNodeId": null, "parentSheet": null, "ruleIndex": null,
                  "href": null, "media": "screen", "title": "", "disabled": false,
                  "active": true, "textSource": "cssom", "textDigest": "{{Digest}}"
                }
              ]
            }
          ]
        }
        """;
}
