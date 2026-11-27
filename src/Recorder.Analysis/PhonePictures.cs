using System.Runtime.InteropServices;
using Recorder.Analysis.Summary;
using Recorder.Session;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Recorder.Analysis;

/// <summary>The pixels of the phone's screenshots and frames, for the contrast measurement (android-checks.md, step 4b).</summary>
public static class PhonePictures
{
    /// <summary>
    /// A PNG file's pixels; null when it cannot be read. The companion's
    /// screenshots are sRGB, converted on the phone when Android gave
    /// another colour space (step 4a), and the video's frames are taken as
    /// sRGB, so the colour chunks are not applied. A 16-bit sample is taken
    /// by its high byte.
    /// </summary>
    public static PhonePixels? Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return FromPng(bytes);
    }

    public static PhonePixels? FromPng(byte[] png)
    {
        if (PngKey.Decode(png) is not { } decoded)
        {
            return null;
        }

        var step = decoded.Wide ? 8 : 4;
        var rgb = new uint[(long)decoded.Width * decoded.Height];
        for (var y = 0; y < decoded.Height; y++)
        {
            var row = decoded.Rows[y];
            for (var x = 0; x < decoded.Width; x++)
            {
                var at = x * step;
                var sample = decoded.Wide ? 2 : 1;
                rgb[(long)y * decoded.Width + x] =
                    ((uint)row[at] << 16) | ((uint)row[at + sample] << 8) | row[at + 2 * sample];
            }
        }

        return new PhonePixels(decoded.Width, decoded.Height, rgb);
    }
}

/// <summary>
/// Windows' own OCR, <c>Windows.Media.Ocr</c>, in the languages of the
/// user's profile (android-checks.md, "Finding the text"): part of Windows,
/// on this PC, and the same answer for the same picture.
/// </summary>
public sealed class WindowsPhoneTextReader : IPhoneTextReader
{
    private readonly OcrEngine? _engine;

    public WindowsPhoneTextReader()
    {
        try
        {
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();
            Unavailable = _engine is null ? "Windows has no OCR language for this user's languages" : null;
            Engine = _engine is null ? string.Empty : "Windows OCR, " + _engine.RecognizerLanguage.LanguageTag;
        }
        catch (Exception exception) when (exception is COMException or TypeLoadException or PlatformNotSupportedException or InvalidCastException)
        {
            Unavailable = "Windows OCR could not be started: " + exception.Message;
            Engine = string.Empty;
        }
    }

    public string? Unavailable { get; }

    public string Engine { get; }

    /// <summary>Read RGB24 pixels in memory and map word bounds back to the source picture.</summary>
    public PhoneOcrReading ReadRgb(int width, int height, ReadOnlyMemory<byte> rgb)
    {
        if (_engine is null)
        {
            throw new InvalidOperationException(Unavailable);
        }
        if (width <= 0 || height <= 0 || rgb.Length != (long)width * height * 3)
        {
            throw new InvalidDataException("The live OCR picture does not contain packed RGB pixels.");
        }
        var scale = Math.Min(1, (double)OcrEngine.MaxImageDimension / Math.Max(width, height));
        var w = Math.Max(1, (int)Math.Floor(width * scale));
        var h = Math.Max(1, (int)Math.Floor(height * scale));
        var pixels = new byte[checked(w * h * 4)];
        var source = rgb.Span;
        for (var y = 0; y < h; y++)
        {
            var row = (int)((long)y * height / h) * width;
            for (var x = 0; x < w; x++)
            {
                var from = (row + (int)((long)x * width / w)) * 3;
                var to = (y * w + x) * 4;
                pixels[to] = source[from + 2];
                pixels[to + 1] = source[from + 1];
                pixels[to + 2] = source[from];
                pixels[to + 3] = 255;
            }
        }
        try
        {
            using var writer = new DataWriter();
            writer.WriteBytes(pixels);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Bgra8,
                w, h, BitmapAlphaMode.Ignore);
            var result = _engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
            return new(result.Lines.SelectMany((line, i) => line.Words.Select(word =>
            {
                var box = word.BoundingRect;
                return new PhoneOcrWord(word.Text,
                    new PhoneBounds((int)Math.Floor(box.X * width / w), (int)Math.Floor(box.Y * height / h),
                        (int)Math.Ceiling((box.X + box.Width) * width / w),
                        (int)Math.Ceiling((box.Y + box.Height) * height / h)), i);
            })).ToArray(), Engine);
        }
        catch (COMException exception)
        {
            throw new IOException("Windows OCR could not read the live picture: " + exception.Message, exception);
        }
    }

    public PhoneOcrReading Read(string path)
    {
        if (_engine is null)
        {
            throw new InvalidOperationException(Unavailable);
        }

        try
        {
            var file = StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
            using var stream = file.OpenAsync(FileAccessMode.Read).AsTask().GetAwaiter().GetResult();
            var decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
            using var bitmap = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();
            if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
            {
                throw new InvalidDataException(
                    $"{Path.GetFileName(path)} is larger than Windows OCR reads ({OcrEngine.MaxImageDimension} pixels a side)");
            }

            var result = _engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
            var words = new List<PhoneOcrWord>();
            for (var line = 0; line < result.Lines.Count; line++)
            {
                foreach (var word in result.Lines[line].Words)
                {
                    var box = word.BoundingRect;
                    words.Add(new PhoneOcrWord(
                        word.Text,
                        new PhoneBounds(
                            (int)Math.Floor(box.X),
                            (int)Math.Floor(box.Y),
                            (int)Math.Ceiling(box.X + box.Width),
                            (int)Math.Ceiling(box.Y + box.Height)),
                        line));
                }
            }

            return new PhoneOcrReading(words, Engine);
        }
        catch (COMException exception)
        {
            throw new IOException($"Windows OCR could not read {Path.GetFileName(path)}: {exception.Message}", exception);
        }
    }
}
