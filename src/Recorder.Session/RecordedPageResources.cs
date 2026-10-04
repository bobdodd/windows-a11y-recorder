namespace Recorder.Session;

/// <summary>
/// A face a document had loaded at the frame and not removed, as its
/// font-face-loaded record gives it (protocol 0.40). The descriptors are the
/// recorded strings of Blink's FontFace getters, by descriptor name.
/// </summary>
public sealed record RecordedFontFace(
    string Family,
    IReadOnlyList<KeyValuePair<string, string>> Descriptors,
    string Digest,
    int CollectionIndex,
    string? SourceUrl);

/// <summary>
/// An image the recording holds at the frame: the latest image-resource
/// record for its URL at or before the frame.
/// </summary>
public sealed record RecordedImage(
    string Url,
    string? ResponseUrl,
    int Status,
    string MimeType,
    string Digest);

/// <summary>
/// The fonts and images of a recorded document at a frame, read from the
/// recording's browser.resources records, for the recreation. The bytes of a
/// font file or an image are read from the recording when asked for, and are
/// checked against their digest. See docs/architecture/page-recreation.md,
/// "Sub-step 3 as built".
/// </summary>
public sealed class RecordedPageResources : IDisposable
{
    private readonly IReadOnlyDictionary<string, RecordedImage> _images;
    private readonly Func<string, string, byte[]?> _bytes;
    private readonly IDisposable? _owner;

    /// <param name="faces">The document's faces, in the order they were added.</param>
    /// <param name="images">The images by URL without its fragment: the URL requested, and the response's URL.</param>
    /// <param name="bytes">Reads the bytes of a record kind ("font-file" or "image-data") by digest, or null.</param>
    /// <param name="notes">What the recreation does not take from the recording, for the evidence panel.</param>
    /// <param name="owner">Disposed with these resources, such as the recording file reader the bytes are read from.</param>
    /// <param name="imageFrames">The frame each animated image is held at (slice 4b sub-step 2a), or none.</param>
    /// <param name="imageFramesMilliseconds">How long choosing the image frames took, or null when they were not read.</param>
    public RecordedPageResources(
        IReadOnlyList<RecordedFontFace> faces,
        IReadOnlyDictionary<string, RecordedImage> images,
        Func<string, string, byte[]?> bytes,
        IReadOnlyList<string> notes,
        IDisposable? owner = null,
        RecordedImageFrames? imageFrames = null,
        double? imageFramesMilliseconds = null)
    {
        ImageFrames = imageFrames ?? RecordedImageFrames.None;
        ImageFramesMilliseconds = imageFramesMilliseconds;
        Faces = faces ?? throw new ArgumentNullException(nameof(faces));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        Notes = notes ?? throw new ArgumentNullException(nameof(notes));
        _owner = owner;
    }

    public static RecordedPageResources None { get; } = new([], new Dictionary<string, RecordedImage>(), (_, _) => null, []);

    public IReadOnlyList<RecordedFontFace> Faces { get; }

    /// <summary>The frame each animated image is held at in the recreation, by URL.</summary>
    public RecordedImageFrames ImageFrames { get; }

    /// <summary>How long choosing the image frames took, or null when they were not read.</summary>
    public double? ImageFramesMilliseconds { get; }

    public int ImageCount => _images.Values.Distinct().Count();

    public IReadOnlyList<string> Notes { get; }

    /// <summary>The image recorded at a URL, compared without its fragment, or null.</summary>
    public RecordedImage? Image(string url) =>
        _images.TryGetValue(WithoutFragment(url), out var image) ? image : null;

    /// <summary>The bytes of a font file by digest, or null when the recording holds none.</summary>
    public byte[]? FontFile(string digest) => _bytes("font-file", digest);

    /// <summary>The bytes of an image by digest, or null when the recording holds none.</summary>
    public byte[]? ImageBytes(string digest) => _bytes("image-data", digest);

    public static string WithoutFragment(string url) =>
        url.IndexOf('#') is var hash and >= 0 ? url[..hash] : url;

    public void Dispose() => _owner?.Dispose();
}
