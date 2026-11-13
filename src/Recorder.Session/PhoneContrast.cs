using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>A word OCR read, with its box in the picture's pixels and the number of its line.</summary>
public sealed record PhoneOcrWord(string Text, PhoneBounds Box, int Line);

/// <summary>What OCR read in a picture, and the engine that read it, such as "Windows OCR, en-US".</summary>
public sealed record PhoneOcrReading(IReadOnlyList<PhoneOcrWord> Words, string Engine);

/// <summary>
/// Reads the words in a picture file (android-checks.md, "Finding the
/// text"). <see cref="Unavailable"/> says why it cannot, such as no OCR
/// language installed, or is null.
/// </summary>
public interface IPhoneTextReader
{
    string? Unavailable { get; }

    PhoneOcrReading Read(string path);
}

/// <summary>How a line of text to measure was found.</summary>
public enum PhoneTextPlace
{
    /// <summary>The app's text, hint or description, placed on the screen by OCR's words.</summary>
    AppTextPlacedByOcr,

    /// <summary>Words OCR read that are in no item's text, such as words drawn in a picture.</summary>
    FoundByOcr,

    /// <summary>The app's text where OCR read none of it: measured on the item's bounds.</summary>
    AppTextOnItemBounds,
}

/// <summary>What a picture of a settled snapshot is.</summary>
public enum PhonePictureKind
{
    /// <summary>The companion app's screenshot, lossless (step 4a).</summary>
    Screenshot,

    /// <summary>A frame of the video, approximate (decision 4).</summary>
    Frame,
}

/// <summary>The picture a settled snapshot is measured on, with its time and its path in the recording.</summary>
public sealed record PhoneContrastPicture(PhonePictureKind Kind, long Time, string Path, string AbsolutePath);

/// <summary>
/// The pictures of a recording's settled snapshots, by one rule for every
/// check (PhoneContrastRun.PictureOf): the companion's screenshot taken
/// from the snapshot until the window was next listed, else the last frame
/// of the video before then. The tree can change before the video does: in
/// test 16's recording the tree showed "Sleep Diary" at 00:00:31.326, but
/// the frame then, from 00:00:31.322, still showed the loading screen, and
/// the first with "Sleep Diary" was from 00:00:31.340 (measured). So a
/// visit and a finding of the tree are shown at their snapshot's picture,
/// not at the snapshot's time.
/// </summary>
public sealed class PhoneSnapshotPictures
{
    private readonly IReadOnlyList<PhoneContrastPicture> _screenshots;
    private readonly IReadOnlyList<SessionVideoFrame> _frames;
    private readonly long[] _frameTimes;
    private readonly PhoneAccessibilityTimeline _trees;
    private readonly string _sessionDirectory;

    private PhoneSnapshotPictures(
        IReadOnlyList<PhoneContrastPicture> screenshots,
        IReadOnlyList<SessionVideoFrame> frames,
        PhoneAccessibilityTimeline trees,
        string sessionDirectory)
    {
        _screenshots = screenshots;
        _frames = frames;
        _frameTimes = frames.Select(frame => frame.MonotonicNanoseconds).ToArray();
        _trees = trees;
        _sessionDirectory = sessionDirectory;
    }

    /// <summary>The phone's video frames, in time order, each written only when the picture changed; none when the video is not the phone's.</summary>
    public IReadOnlyList<SessionVideoFrame> Frames => _frames;

    /// <summary>No pictures: every snapshot is shown at its own time.</summary>
    public static PhoneSnapshotPictures Empty { get; } = new([], [], PhoneAccessibilityTimeline.Empty, string.Empty);

    /// <summary>Only the phone's video frames, for tests.</summary>
    internal static PhoneSnapshotPictures OfFrames(IReadOnlyList<SessionVideoFrame> frames, PhoneAccessibilityTimeline trees) =>
        new([], frames, trees, string.Empty);

    public static PhoneSnapshotPictures Of(SessionPlaybackArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var screenshots = archive.PhoneScreenshots
            .Select(shot => new PhoneContrastPicture(PhonePictureKind.Screenshot, shot.MonotonicNanoseconds, shot.Path, shot.AbsolutePath))
            .ToList();
        var frames = archive.FrameSource == SessionFrameSource.Phone ? archive.Frames : [];
        return new PhoneSnapshotPictures(screenshots, frames, archive.PhoneAccessibility, archive.SessionDirectory);
    }

    /// <summary>The picture of a settled snapshot of a visit; null when none shows it.</summary>
    public PhoneContrastPicture? Of(PhoneVisit visit, SessionTimelineEvent settled)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(settled);
        var time = settled.MonotonicNanoseconds;
        var end = Math.Min(_trees.NextListing(time, visit.WindowId) ?? long.MaxValue, visit.End);
        return PhoneContrastRun.PictureOf(_screenshots, _frames, _frameTimes, visit.Start, time, end, _sessionDirectory);
    }

    /// <summary>
    /// The picture of a time between two: the first screenshot from the
    /// first, else the last frame before the second, from the first; null
    /// when none. For text shown only briefly (android-checks.md, step 12).
    /// </summary>
    public PhoneContrastPicture? Between(long start, long end) =>
        PhoneContrastRun.PictureOf(_screenshots, _frames, _frameTimes, start, start, end, _sessionDirectory);

    /// <summary>
    /// The time to show a visit at: its first settled snapshot's picture;
    /// else, with none settled, the first screenshot in the visit or the
    /// last frame before it ended; else its start.
    /// </summary>
    public long TimeOf(PhoneVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        foreach (var settled in visit.SettledSnapshots)
        {
            if (Of(visit, settled) is { } picture)
            {
                return picture.Time;
            }
        }

        return PhoneContrastRun.PictureOf(_screenshots, _frames, _frameTimes, visit.Start, visit.Start, visit.End, _sessionDirectory)?.Time
            ?? visit.Start;
    }
}

/// <summary>
/// A line of text measured: the item whose text it is (null for words in
/// no item's text), the smallest item it is in, how it was found, the
/// app's text and OCR's, its box in the screen's pixels, its colours, and
/// the height of OCR's box round its words in the screen's pixels before
/// the box was widened (0 when measured on an item's bounds).
/// </summary>
public sealed record PhoneTextLine(
    PhoneCheckItem? Item,
    PhoneCheckItem? Container,
    PhoneTextPlace Place,
    string? AppText,
    string? OcrText,
    PhoneBounds Box,
    PhoneSwatch? Colours,
    int WordsHeight = 0);

/// <summary>A settled snapshot's measurement: its picture, the lines of text measured on it, and its controls.</summary>
public sealed record PhoneContrastSnapshot(
    PhoneVisit Visit,
    SessionTimelineEvent Settled,
    SessionTimelineEvent TakenBy,
    PhoneContrastPicture Picture,
    string? Engine,
    IReadOnlyList<PhoneTextLine> Lines,
    IReadOnlyList<PhoneControlMeasure>? MeasuredControls = null,
    PhoneCheckWindow? Window = null)
{
    /// <summary>The controls measured for non-text contrast (step 5).</summary>
    public IReadOnlyList<PhoneControlMeasure> Controls => MeasuredControls ?? [];
}

/// <summary>
/// Step 4b of android-checks.md: for each visit's settled snapshots, the
/// picture to measure (the companion's screenshot when one shows it, else
/// the last frame of the video before the window changes), the lines of
/// text on it, from the app's items and OCR, and each line's colours. The
/// levels and findings are step 4c.
/// </summary>
public sealed class PhoneContrastRun
{
    private PhoneContrastRun()
    {
    }

    public static PhoneContrastRun Empty { get; } = new();

    /// <summary>A measurement of the given snapshots, for tests of what is made of it.</summary>
    internal static PhoneContrastRun Of(IReadOnlyList<PhoneContrastSnapshot> snapshots) => new() { Snapshots = snapshots };

    public IReadOnlyList<PhoneContrastSnapshot> Snapshots { get; private set; } = [];

    /// <summary>Why OCR could not be used, when it could not; the lines are then the app's text on its items' bounds.</summary>
    public string? OcrUnavailable { get; private set; }

    /// <summary>Why a snapshot could not be measured, one for each such snapshot.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    public TimeSpan Elapsed { get; private set; }

    public int FromScreenshots => Snapshots.Count(snapshot => snapshot.Picture.Kind == PhonePictureKind.Screenshot);

    public int FromFrames => Snapshots.Count(snapshot => snapshot.Picture.Kind == PhonePictureKind.Frame);

    /// <param name="load">Reads a picture file's pixels; null when it cannot be read.</param>
    /// <param name="reader">OCR; null to use none.</param>
    public static PhoneContrastRun Run(
        SessionPlaybackArchive archive,
        Func<string, PhonePixels?> load,
        IPhoneTextReader? reader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(load);
        var run = new PhoneContrastRun();
        var screens = archive.PhoneScreens;
        var trees = archive.PhoneAccessibility;
        if (!screens.Recorded || archive.RecordSource is not { } source)
        {
            return run;
        }

        var stopwatch = Stopwatch.StartNew();
        if (reader?.Unavailable is { } unavailable)
        {
            run.OcrUnavailable = unavailable;
            reader = null;
        }
        else if (reader is null)
        {
            run.OcrUnavailable = "no OCR was given";
        }

        var snapshotPictures = PhoneSnapshotPictures.Of(archive);
        var pictures = new Dictionary<string, PhonePixels?>(StringComparer.Ordinal);
        var readings = new Dictionary<string, PhoneOcrReading?>(StringComparer.Ordinal);
        var problems = new List<string>();
        var measured = new List<PhoneContrastSnapshot>();
        foreach (var visit in screens.Visits)
        {
            var takings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var settled in visit.SettledSnapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (trees.TakenBy(settled, visit.WindowId) is not { } taking || !takings.Add(taking.EventId))
                {
                    continue;
                }

                var time = settled.MonotonicNanoseconds;
                var clock = WindowsPreferenceTimeline.Clock(time);
                var picture = snapshotPictures.Of(visit, settled);
                if (picture is null)
                {
                    problems.Add($"{clock}: no screenshot or frame of the video shows the screen");
                    continue;
                }

                try
                {
                    var window = PhoneCheckRun.ReadWindow(source, taking, visit.WindowId);
                    var windows = WindowsOf(source, settled);
                    if (window is null || !windows.TryGetValue(visit.WindowId, out var app))
                    {
                        problems.Add($"{clock}: the snapshot does not hold window {visit.WindowId.ToString(CultureInfo.InvariantCulture)}");
                        continue;
                    }

                    if (!pictures.TryGetValue(picture.AbsolutePath, out var pixels))
                    {
                        pictures[picture.AbsolutePath] = pixels = load(picture.AbsolutePath);
                    }

                    if (pixels is null)
                    {
                        problems.Add($"{clock}: {picture.Path} could not be read");
                        continue;
                    }

                    PhoneOcrReading? reading = null;
                    if (reader is not null && !readings.TryGetValue(picture.AbsolutePath, out reading))
                    {
                        readings[picture.AbsolutePath] = reading = reader.Read(picture.AbsolutePath);
                    }

                    // The tree's bounds are in the screen's pixels; a frame
                    // can be smaller than the screen.
                    var screen = trees.ScreenAt(taking.MonotonicNanoseconds) ?? (pixels.Width, pixels.Height);
                    var scale = new PictureScale(pixels.Width / (double)screen.Width, pixels.Height / (double)screen.Height);
                    var above = windows.Values
                        .Where(entry => entry.Id != app.Id && entry.Layer > app.Layer)
                        .Select(entry => entry.Bounds)
                        .ToList();
                    var lines = PhoneTextMatching.Lines(window, app.Bounds, above, reading?.Words, scale)
                        .Select(line => line with { Colours = PhoneColours.Measure(pixels, scale.ToPicture(line.Box)) })
                        .ToList();
                    var controls = PhoneControlMeasuring.Measure(window, app.Bounds, above, lines, reading is not null, pixels, scale);
                    measured.Add(new PhoneContrastSnapshot(visit, settled, taking, picture, reading?.Engine, lines, controls, window));
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or InvalidOperationException or
                        JsonException or KeyNotFoundException or UnauthorizedAccessException)
                {
                    problems.Add($"{clock}: {exception.Message}");
                }
            }
        }

        run.Snapshots = measured;
        run.Problems = problems;
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    // The first of the companion's screenshots taken from the settled
    // snapshot until the window was next listed; else the last frame of the
    // video before then, as the first frames after a change are still
    // sharpening (android-checks.md, "Step 4: what the measurement found"),
    // if that frame is in the visit.
    internal static PhoneContrastPicture? PictureOf(
        IReadOnlyList<PhoneContrastPicture> screenshots,
        IReadOnlyList<SessionVideoFrame> frames,
        long[] frameTimes,
        long visitStart,
        long time,
        long end,
        string sessionDirectory)
    {
        foreach (var screenshot in screenshots)
        {
            if (screenshot.Time >= time && screenshot.Time < end)
            {
                return screenshot;
            }
        }

        var index = Array.BinarySearch(frameTimes, end);
        index = index < 0 ? ~index - 1 : index - 1;
        while (index >= 0 && frameTimes[index] >= end)
        {
            index--;
        }

        if (index < 0 || frameTimes[index] < visitStart)
        {
            return null;
        }

        var frame = frames[index];
        return new PhoneContrastPicture(PhonePictureKind.Frame, frame.MonotonicNanoseconds, frame.Path,
            string.IsNullOrEmpty(frame.AbsolutePath) ? Path.Combine(sessionDirectory, frame.Path) : frame.AbsolutePath);
    }

    // The windows a snapshot lists, with their layers and bounds.
    internal static Dictionary<int, (int Id, int Layer, PhoneBounds Bounds)> WindowsOf(ISessionEventRecordSource source, SessionTimelineEvent snapshot)
    {
        var windows = new Dictionary<int, (int, int, PhoneBounds)>();
        using var document = JsonDocument.Parse(source.ReadEventJson(snapshot));
        if (!document.RootElement.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("windows", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return windows;
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number &&
                entry.TryGetProperty("bounds", out var bounds) && bounds.ValueKind == JsonValueKind.Object)
            {
                var layer = entry.TryGetProperty("layer", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
                windows[id.GetInt32()] = (id.GetInt32(), layer, new PhoneBounds(
                    Side(bounds, "left"), Side(bounds, "top"), Side(bounds, "right"), Side(bounds, "bottom")));
            }
        }

        return windows;

        static int Side(JsonElement bounds, string name) =>
            bounds.TryGetProperty(name, out var side) && side.ValueKind == JsonValueKind.Number ? side.GetInt32() : 0;
    }
}

/// <summary>How a picture's pixels relate to the screen's.</summary>
public readonly record struct PictureScale(double X, double Y)
{
    public static PictureScale One { get; } = new(1, 1);

    public PhoneBounds ToPicture(PhoneBounds box) => new(
        (int)Math.Floor(box.Left * X), (int)Math.Floor(box.Top * Y), (int)Math.Ceiling(box.Right * X), (int)Math.Ceiling(box.Bottom * Y));

    public PhoneBounds ToScreen(PhoneBounds box) => new(
        (int)Math.Floor(box.Left / X), (int)Math.Floor(box.Top / Y), (int)Math.Ceiling(box.Right / X), (int)Math.Ceiling(box.Bottom / Y));
}

/// <summary>
/// Matches OCR's words to the app's items (android-checks.md, "Finding the
/// text"): a word goes to the smallest item it is in whose text, hint or
/// description has it, allowing for the characters OCR mistakes for
/// others; the words of one OCR line that go to one item are a line to
/// measure. A word in no item's text, of at least two letters or digits,
/// is text OCR found. An item with text of which OCR read nothing, not an
/// image, with no children and not cut off by a list that scrolls (ATF's
/// "against a scrollable edge"), is measured on its bounds. Words outside
/// the app's window, or under a window above it, such as the status bar,
/// are not the app's.
/// </summary>
public static class PhoneTextMatching
{
    internal const string ImageViewClass = "android.widget.ImageView";

    // A box is widened by this share of its height on each side, so the
    // background around the letters is in it, within the item.
    private const double Margin = 0.2;

    public static IReadOnlyList<PhoneTextLine> Lines(
        PhoneCheckWindow window,
        PhoneBounds windowBounds,
        IReadOnlyList<PhoneBounds> above,
        IReadOnlyList<PhoneOcrWord>? words,
        PictureScale scale)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(windowBounds);
        ArgumentNullException.ThrowIfNull(above);
        var visible = window.Items
            .Where(item => item.Node.VisibleToUser && !item.Bounds.IsEmpty)
            .Select(item => (Item: item, Area: (long)item.Bounds.Width * item.Bounds.Height, Text: TextOf(item)))
            .OrderBy(entry => entry.Area)
            .ThenBy(entry => entry.Item.Node.Index)
            .ToList();
        var groups = new Dictionary<(int Item, int Container, int Line), Group>();
        var placed = new HashSet<PhoneCheckItem>();
        foreach (var word in words ?? [])
        {
            var box = scale.ToScreen(word.Box);
            var x = (box.Left + box.Right) / 2;
            var y = (box.Top + box.Bottom) / 2;
            if (!Inside(windowBounds, x, y) || above.Any(bounds => Inside(bounds, x, y)))
            {
                continue;
            }

            var spelt = Spelling(word.Text);
            if (spelt.Length == 0)
            {
                continue;
            }

            PhoneCheckItem? owner = null;
            PhoneCheckItem? container = null;
            foreach (var (item, _, text) in visible)
            {
                if (!Inside(item.Bounds, x, y))
                {
                    continue;
                }

                container ??= item;
                if (text is not null && Has(text, spelt))
                {
                    owner = item;
                    break;
                }
            }

            if (owner is null && spelt.Length < 2)
            {
                continue;
            }

            var key = (owner?.Node.Index ?? -1, owner is null ? container?.Node.Index ?? -1 : -1, word.Line);
            if (!groups.TryGetValue(key, out var group))
            {
                groups[key] = group = new Group(owner, owner ?? container, box);
            }

            group.Add(word.Text, box);
            if (owner is not null)
            {
                placed.Add(owner);
            }
        }

        var lines = groups.Values
            .Select(group => new PhoneTextLine(
                group.Owner,
                group.Container,
                group.Owner is null ? PhoneTextPlace.FoundByOcr : PhoneTextPlace.AppTextPlacedByOcr,
                group.Owner is null ? null : AppText(group.Owner),
                group.Words,
                Widened(group.Box, group.Container?.Bounds ?? windowBounds),
                null,
                group.Box.Height))
            .ToList();
        foreach (var (item, _, text) in visible)
        {
            if (text is not null && !placed.Contains(item) && item.Children.Count == 0 && !item.InstanceOf(ImageViewClass) &&
                !PhoneTreeChecks.IsAgainstScrollableEdge(item) &&
                Inside(windowBounds, (item.Bounds.Left + item.Bounds.Right) / 2, (item.Bounds.Top + item.Bounds.Bottom) / 2))
            {
                lines.Add(new PhoneTextLine(item, item, PhoneTextPlace.AppTextOnItemBounds, AppText(item), null, item.Bounds, null));
            }
        }

        return [.. lines.OrderBy(line => line.Box.Top).ThenBy(line => line.Box.Left)];
    }

    /// <summary>
    /// A word as matched: lower case, letters and digits only, with the
    /// characters OCR reads for one another as one ("0" and "o"; "1", "l",
    /// "i" and "|"; "5" and "s"). Windows' OCR read "0" as "O" in test 16's
    /// screenshot 13 (measured).
    /// </summary>
    internal static string Spelling(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.ToLowerInvariant())
        {
            var mapped = character switch
            {
                '0' => 'o',
                '1' or 'i' or '|' or '!' => 'l',
                '5' => 's',
                _ => character,
            };
            if (char.IsLetterOrDigit(mapped))
            {
                builder.Append(mapped);
            }
        }

        return builder.ToString();
    }

    // Whether an item's text has a word: one of its words spelt the same,
    // or nearly, for a word of four or more letters, or a part of its text
    // run together, as OCR joins and splits words.
    private static bool Has(ItemText text, string word)
    {
        foreach (var each in text.Words)
        {
            if (each == word || (word.Length >= 4 && Math.Abs(each.Length - word.Length) <= 1 && Distance(each, word) <= (word.Length >= 8 ? 2 : 1)))
            {
                return true;
            }
        }

        return word.Length >= 2 && text.Joined.Contains(word, StringComparison.Ordinal);
    }

    internal static int Distance(string first, string second)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];
        for (var j = 0; j <= second.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= first.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= second.Length; j++)
            {
                var cost = first[i - 1] == second[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }

    private static ItemText? TextOf(PhoneCheckItem item)
    {
        var all = string.Join(' ', new[] { item.Text, item.Node.Hint, item.Node.ContentDescription }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (all.Length == 0)
        {
            return null;
        }

        var words = all.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Spelling).Where(word => word.Length > 0).ToArray();
        return words.Length == 0 ? null : new ItemText(words, string.Concat(words));
    }

    private static string? AppText(PhoneCheckItem item) =>
        new[] { item.Text, item.Node.Hint, item.Node.ContentDescription }.FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));

    private static bool Inside(PhoneBounds bounds, int x, int y) =>
        x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;

    private static PhoneBounds Widened(PhoneBounds box, PhoneBounds within)
    {
        var margin = Math.Max(2, (int)Math.Round(box.Height * Margin));
        return new PhoneBounds(
            Math.Max(box.Left - margin, Math.Min(within.Left, box.Left)),
            Math.Max(box.Top - margin, Math.Min(within.Top, box.Top)),
            Math.Min(box.Right + margin, Math.Max(within.Right, box.Right)),
            Math.Min(box.Bottom + margin, Math.Max(within.Bottom, box.Bottom)));
    }

    private sealed record ItemText(string[] Words, string Joined);

    private sealed class Group(PhoneCheckItem? owner, PhoneCheckItem? container, PhoneBounds box)
    {
        private readonly List<string> _words = [];

        public PhoneCheckItem? Owner { get; } = owner;

        public PhoneCheckItem? Container { get; } = container;

        public PhoneBounds Box { get; private set; } = box;

        public string Words => string.Join(' ', _words);

        public void Add(string word, PhoneBounds box)
        {
            _words.Add(word);
            Box = new PhoneBounds(Math.Min(Box.Left, box.Left), Math.Min(Box.Top, box.Top), Math.Max(Box.Right, box.Right), Math.Max(Box.Bottom, box.Bottom));
        }
    }
}
