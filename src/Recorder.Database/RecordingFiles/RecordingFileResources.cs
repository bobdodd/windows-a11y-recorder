using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>
/// Reads the fonts and images of a recorded document at a frame from a
/// recording file's browser.resources records (protocol 0.40), for the
/// recreation. Only the chunks that hold records of the channel are read, up
/// to the frame. The resources keep a reader of their own on the file, from
/// which the bytes of a font file or image are read when the recreation asks
/// for them, so that bytes the page does not ask for are not decoded. See
/// docs/architecture/page-recreation.md, "Sub-step 3 as built".
/// </summary>
public static class RecordingFileResources
{
    private sealed record Location(RecordingFileChunk Chunk, int Offset);

    /// <param name="filePath">The recording file.</param>
    /// <param name="documentKey">The document's state key, its token and identity.</param>
    /// <param name="cutNanoseconds">The recording time the document's state is read at.</param>
    /// <param name="compositionNanoseconds">The recording time of the frame's composition, at which the frame of each animated image is chosen (slice 4b sub-step 2a), or null for none.</param>
    public static RecordedPageResources Read(
        string filePath,
        string documentKey,
        long cutNanoseconds,
        CancellationToken cancellationToken = default,
        long? compositionNanoseconds = null)
    {
        var reader = RecordingFileReader.Open(filePath);
        try
        {
            return Read(reader, documentKey, cutNanoseconds, cancellationToken, owner: reader, compositionNanoseconds: compositionNanoseconds);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <param name="owner">Disposed with the resources; the reader itself is not, unless it is the owner.</param>
    public static RecordedPageResources Read(
        RecordingFileReader reader,
        string documentKey,
        long cutNanoseconds,
        CancellationToken cancellationToken = default,
        IDisposable? owner = null,
        long? compositionNanoseconds = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(documentKey);
        // Slice 4g: the document's animations at the time its state is read
        // at, for the evidence panel.
        var animations = RecordingFileAnimations.Read(reader, documentKey, cutNanoseconds, cancellationToken);
        var channel = reader.Channels.Values.FirstOrDefault(item => item.Topic == BrowserEvidenceChannels.Resources);
        if (channel is null)
        {
            return new RecordedPageResources([], new Dictionary<string, RecordedImage>(), (_, _) => null,
                ["The recording holds no font or image records, which are recorded from protocol 0.40, so the recreation draws no image and uses no recorded font."],
                owner)
            {
                Animations = animations,
            };
        }

        var bytes = new Dictionary<(string Kind, string Digest), Location>();
        var images = new Dictionary<string, RecordedImage>(StringComparer.Ordinal);
        // A face is named by its browser instance, renderer, and number,
        // which is unique in the renderer. The faces in the document's set
        // are kept in the order they were added.
        var inSet = new List<string>();
        var loaded = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var imagesWithoutData = new HashSet<string>(StringComparer.Ordinal);
        var styleSheets = new RecordedStyleSheets.Builder();
        foreach (var chunk in reader.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.StartTime > cutNanoseconds)
            {
                continue;
            }
            var index = reader.ReadMessageIndex(chunk, channel.Id);
            if (index.Length == 0)
            {
                continue;
            }
            var records = reader.ReadChunkRecords(chunk);
            foreach (var (logTime, offset) in index)
            {
                if (logTime > cutNanoseconds)
                {
                    continue;
                }
                var message = reader.ReadMessageAt(records, offset);
                var stored = RecordingEventCodec.Decode(message.Data.Span);
                var payload = stored.Event.Payload;
                switch (stored.Event.EventType)
                {
                    case BrowserEvidenceEventTypes.FontFile:
                    case BrowserEvidenceEventTypes.ImageData:
                    case BrowserEvidenceEventTypes.StyleSheetText:
                        if (Text(payload, "digest") is { } digest)
                        {
                            bytes.TryAdd((stored.Event.EventType, digest), new Location(chunk, offset));
                        }
                        break;
                    case BrowserEvidenceEventTypes.ImageResource:
                        if (Text(payload, "url") is not { } url)
                        {
                            break;
                        }
                        if (!payload.TryGetProperty("dataRecorded", out var recorded) || recorded.ValueKind != JsonValueKind.True)
                        {
                            images.Remove(RecordedPageResources.WithoutFragment(url));
                            imagesWithoutData.Add(url);
                            break;
                        }
                        imagesWithoutData.Remove(url);
                        var image = new RecordedImage(
                            url,
                            Text(payload, "responseUrl"),
                            payload.GetProperty("status").GetInt32(),
                            Text(payload, "mimeType") ?? "",
                            Text(payload, "digest") ?? "");
                        images[RecordedPageResources.WithoutFragment(url)] = image;
                        if (image.ResponseUrl is { } response && response != url)
                        {
                            images[RecordedPageResources.WithoutFragment(response)] = image;
                        }
                        break;
                    case BrowserEvidenceEventTypes.StyleSheetResource:
                        styleSheets.AddResource(payload);
                        break;
                    case BrowserEvidenceEventTypes.StyleSheetsUpdated:
                        if (DomTreeRebuilder.DocumentKey(payload) == documentKey)
                        {
                            styleSheets.AddUpdate(payload);
                        }
                        break;
                    case BrowserEvidenceEventTypes.FontFaceAdded:
                    case BrowserEvidenceEventTypes.FontFaceRemoved:
                    case BrowserEvidenceEventTypes.FontFaceLoaded:
                        if (FaceName(payload) is not { } face)
                        {
                            break;
                        }
                        if (stored.Event.EventType == BrowserEvidenceEventTypes.FontFaceLoaded)
                        {
                            loaded[face] = payload.Clone();
                        }
                        else if (DomTreeRebuilder.DocumentKey(payload) == documentKey)
                        {
                            inSet.Remove(face);
                            if (stored.Event.EventType == BrowserEvidenceEventTypes.FontFaceAdded)
                            {
                                inSet.Add(face);
                            }
                        }
                        break;
                }
            }
        }

        var faces = new List<RecordedFontFace>();
        int notLoaded = 0, local = 0, inCollection = 0, withoutFile = 0;
        foreach (var face in inSet)
        {
            if (!loaded.TryGetValue(face, out var payload))
            {
                notLoaded++;
                continue;
            }
            if (!payload.TryGetProperty("fontFile", out var file) || file.ValueKind != JsonValueKind.Object)
            {
                local++;
                continue;
            }
            var digest = file.GetProperty("digest").GetString()!;
            var collectionIndex = file.GetProperty("index").GetInt32();
            if (collectionIndex != 0)
            {
                inCollection++;
                continue;
            }
            if (!bytes.ContainsKey((BrowserEvidenceEventTypes.FontFile, digest)))
            {
                withoutFile++;
                continue;
            }
            var descriptors = payload.GetProperty("descriptors").EnumerateObject()
                .Where(item => item.Value.ValueKind == JsonValueKind.String)
                .Select(item => new KeyValuePair<string, string>(item.Name, item.Value.GetString()!))
                .ToArray();
            string? source = payload.TryGetProperty("source", out var found) && found.ValueKind == JsonValueKind.Object
                ? Text(found, "url")
                : null;
            faces.Add(new RecordedFontFace(Text(payload, "family") ?? "", descriptors, digest, collectionIndex, source));
        }

        var notes = new List<string>();
        static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
        notes.Add($"{Count(faces.Count)} font faces the document had loaded at the frame are added to it from their recorded font files before the page is built. {Count(images.Values.Distinct().Count())} images recorded at or before the frame are answered from their recorded bytes when the page asks for them, by URL.");
        if (notLoaded > 0)
        {
            notes.Add($"{Count(notLoaded)} font faces were in the document's set at the frame but had not loaded, so they are not added.");
        }
        if (local > 0)
        {
            notes.Add($"{Count(local)} font faces loaded from an installed font (a local() source), whose name is not recorded, so they are not added; text in them uses the fonts of this machine.");
        }
        if (inCollection > 0)
        {
            notes.Add($"{Count(inCollection)} font faces loaded a font other than the first of a font collection, which a FontFace cannot be made from, so they are not added.");
        }
        if (withoutFile > 0)
        {
            notes.Add($"{Count(withoutFile)} font faces have no font-file record at or before the frame, so they are not added.");
        }
        if (imagesWithoutData.Count > 0)
        {
            notes.Add($"{Count(imagesWithoutData.Count)} images were recorded without their bytes, so the recreation refuses them.");
        }

        byte[]? Bytes(string kind, string digest)
        {
            if (!bytes.TryGetValue((kind, digest), out var location))
            {
                return null;
            }
            var records = reader.ReadChunkRecords(location.Chunk);
            var stored = RecordingEventCodec.Decode(reader.ReadMessageAt(records, location.Offset).Data.Span);
            var data = Convert.FromBase64String(stored.Event.Payload.GetProperty("bytes").GetString()!);
            // A record whose bytes do not match its digest is not used.
            return Convert.ToHexStringLower(SHA256.HashData(data)) == digest ? data : null;
        }

        RecordedImageFrames? frames = null;
        RecordedCompositorValues? compositorValues = null;
        double? framesMilliseconds = null;
        if (compositionNanoseconds is { } composition)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            (frames, compositorValues) = RecordingFileImageFrames.ReadWithCompositorValues(reader, documentKey, cutNanoseconds, composition, cancellationToken);
            framesMilliseconds = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
            notes.AddRange(frames.Notes);
        }

        // Slice 4e: the document's style sheets at the cut, and a sheet text
        // is used only when its record is held.
        var sheets = styleSheets.Build();
        notes.AddRange(sheets.Notes());

        return new RecordedPageResources(faces, images, Bytes, notes, owner, frames, framesMilliseconds, compositorValues, sheets)
        {
            Animations = animations,
        };
    }

    private static string? FaceName(JsonElement payload)
    {
        if (Text(payload, "faceNumber") is not { } number ||
            !payload.TryGetProperty("context", out var context) ||
            context.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var process = context.TryGetProperty("processId", out var id) && id.ValueKind == JsonValueKind.Number
            ? id.GetInt64().ToString(CultureInfo.InvariantCulture)
            : "";
        return $"{Text(context, "browserInstanceId")} {process} {number}";
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
