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
          "dataRecorded": true
        }
        """;
}
