using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.Recreation;

namespace Recorder.Analysis.Summary;

/// <summary>
/// The check, before step 4b is built, that the recreation can be run by the
/// worker without a window (docs/architecture/recording-summary.md,
/// decision 7): the page at a frame is rebuilt as the player's Inspect page
/// at this frame rebuilds it, opened in the instrumented Chromium with a
/// window and then without one, and a picture of the whole page taken each
/// time. Nothing is written to the catalog or the recording's folder.
/// </summary>
public static class RecreationCheck
{
    public const string WithWindowFile = "with-window.png";
    public const string WithoutWindowFile = "without-window.png";
    public const string ReportFile = "recreation-check.txt";

    // Chromium's headless mode: headless and headful are unified, and since
    // Chrome 132 the old headless mode is a separate binary
    // (https://developer.chrome.com/docs/chromium/headless).
    public const string WithoutWindowArgument = "--headless";

    public static readonly TimeSpan BuildTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Reads a time in the recording as the player shows it, such as 00:00:48.950.</summary>
    public static bool TryParseTime(string text, out long nanoseconds)
    {
        nanoseconds = 0;
        if (!TimeSpan.TryParseExact(text, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var time) &&
            !TimeSpan.TryParseExact(text, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out time))
        {
            return false;
        }
        nanoseconds = time.Ticks * 100;
        return true;
    }

    public static string FormatTime(long nanoseconds)
    {
        var value = TimeSpan.FromTicks(Math.Max(0, nanoseconds) / 100);
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }

    /// <summary>
    /// Runs the check on the recording in the session folder, at the last
    /// captured frame at or before the time, and writes the two pictures and
    /// the report to the folder. Returns the report's path.
    /// </summary>
    public static async Task<string> RunAsync(
        string sessionDirectory,
        long timeNanoseconds,
        string folder,
        string chromium,
        string recreationsDirectory,
        CancellationToken cancellationToken)
    {
        var report = new StringBuilder();
        var filePath = Path.Combine(sessionDirectory, SessionDatabase.RecordingFileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The recording file {filePath} is not there.", filePath);
        }
        if (!File.Exists(chromium))
        {
            throw new FileNotFoundException($"The instrumented Chromium {chromium} is not there.", chromium);
        }
        Directory.CreateDirectory(folder);
        var opened = await RecordingFilePlayback.OpenAsync(sessionDirectory, filePath, cancellationToken).ConfigureAwait(false);
        var documents = opened.Documents ?? throw new InvalidDataException("The recording file's browser documents could not be read.");
        var archive = opened.Archive;
        var frame = archive.Frames.LastOrDefault(item => item.MonotonicNanoseconds <= timeNanoseconds)
            ?? throw new InvalidDataException($"No frame was captured at or before {FormatTime(timeNanoseconds)}.");
        var choices = documents.At(frame.MonotonicNanoseconds, cancellationToken);
        if (choices.Count == 0)
        {
            throw new InvalidDataException($"No top-level page was recorded at or before the frame at {FormatTime(frame.MonotonicNanoseconds)}.");
        }
        var chosen = choices[0];

        report.AppendLine("Check of the recreation with a window and without one, before step 4b (docs/architecture/recording-summary.md, decision 7).");
        report.AppendLine("Nothing was written to the catalog or the recording's folder.");
        report.AppendLine();
        report.AppendLine($"Recording: {sessionDirectory}");
        report.AppendLine($"Time asked for: {FormatTime(timeNanoseconds)}; the frame used: {FormatTime(frame.MonotonicNanoseconds)}");
        report.AppendLine($"Chromium: {chromium}");
        report.AppendLine("Pages at the frame, most recently drawn first, as the player lists them; the first is used, as the player's default:");
        foreach (var choice in choices)
        {
            report.AppendLine($"- {choice.Url}{(choice.BrowserInterface ? " (browser interface)" : "")}");
        }

        var pictures = new List<(string Name, RecreationPicture? Picture)>();
        foreach (var (name, file, arguments) in new[]
        {
            ("With a window", WithWindowFile, Array.Empty<string>()),
            ("Without a window", WithoutWindowFile, new[] { WithoutWindowArgument }),
        })
        {
            report.AppendLine();
            report.AppendLine($"## {name}");
            report.AppendLine(arguments.Length == 0 ? "Extra arguments: none" : $"Extra arguments: {string.Join(' ', arguments)}");
            var timings = new List<RecreationTiming>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            RecreationPicture? picture = null;
            try
            {
                var content = RecordedPageAtFrame.Content(documents, archive.BrowserPreferences, chosen, frame.MonotonicNanoseconds, FormatTime, timings, cancellationToken);
                var directory = Path.Combine(recreationsDirectory, Guid.NewGuid().ToString("N"));
                await using var session = await RecreationSession.OpenAsync(chromium, directory, content, cancellationToken, arguments, timings).ConfigureAwait(false);
                var built = await session.WaitForBuildAsync(BuildTimeout, cancellationToken).ConfigureAwait(false);
                report.AppendLine(built
                    ? $"Built and painted, with its fonts ready, {clock.Elapsed.TotalSeconds:0.0} s after the start."
                    : $"Not reported built within {BuildTimeout.TotalSeconds:0} s; the picture is of the page as it was then.");
                picture = await session.CaptureWholePageAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(folder, file), picture.Png, cancellationToken).ConfigureAwait(false);
                var (width, height) = PngSize(picture.Png);
                report.AppendLine($"Picture: {file}, {width} by {height} pixels, {picture.Png.Length} bytes, SHA-256 {Convert.ToHexStringLower(SHA256.HashData(picture.Png))}");
                report.AppendLine($"The page's CSS content size: {picture.ContentWidth} by {picture.ContentHeight}");
                report.AppendLine($"The page when the picture was taken: {picture.Page}");
                report.AppendLine(session.WindowFit is { } fit
                    ? $"Window fit: needed {fit.NeededWidth} by {fit.NeededHeight}, page area {fit.AreaWidth} by {fit.AreaHeight}, emulated {fit.Emulated}, scale {fit.Scale}{(fit.EmulationError is { } error ? $", emulation error: {error}" : "")}"
                    : "Window fit: none, as no viewport was recorded");
                report.AppendLine($"Requests refused, as no answer was recorded: {session.RefusedRequests}; navigations blocked: {session.Blocked.Count}");
                report.AppendLine("Timings, in ms:");
                foreach (var timing in session.Timings)
                {
                    report.AppendLine($"- {timing.Step}: {timing.Milliseconds.ToString("0.0", CultureInfo.InvariantCulture)}");
                }
            }
#pragma warning disable CA1031 // A failure of either way of opening is reported, and the other still tried.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                report.AppendLine($"Failed: {exception.GetType().Name}: {exception.Message}");
            }
            pictures.Add((name, picture));
        }

        report.AppendLine();
        report.AppendLine("## Comparison");
        if (pictures.All(item => item.Picture is not null))
        {
            var first = pictures[0].Picture!.Png;
            var second = pictures[1].Picture!.Png;
            report.AppendLine(first.AsSpan().SequenceEqual(second)
                ? "The two pictures are the same, byte for byte."
                : $"The two pictures differ: {PngSize(first).Width} by {PngSize(first).Height} with a window, {PngSize(second).Width} by {PngSize(second).Height} without one.");
        }
        else
        {
            report.AppendLine("Not compared, as a picture was not taken.");
        }

        var path = Path.Combine(folder, ReportFile);
        await File.WriteAllTextAsync(path, report.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return path;
    }

    // The width and height in a PNG's header chunk, which follows its
    // eight-byte signature (https://www.w3.org/TR/png-3/#11IHDR).
    internal static (int Width, int Height) PngSize(byte[] png) =>
        png.Length < 24
            ? (0, 0)
            : (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
               System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
}
