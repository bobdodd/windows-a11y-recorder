using System.Diagnostics;
using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// Step 8 of android-checks.md, images of text (WCAG 1.4.5; 1.4.9 as well at
/// the AAA audit level), from the words OCR read on each settled snapshot's
/// picture (step 4b): an image in the tree, an ImageView or a class drawn
/// from one, inside whose bounds OCR read words that no text item holds is a
/// probable image of text, a warning, as the criterion's exceptions are not
/// judged. Words go to the smallest image they are in; words held by an item's
/// text or hint, as an item drawn over the image, are the app's text. A
/// description holding the words does not make them text: it is what a
/// screen reader says, not what is shown. The findings are merged in each
/// visit by check and image, as the tree checks' are by item.
/// </summary>
public sealed class PhoneImagesOfTextRun
{
    /// <summary>
    /// The fewest letters and digits OCR must read inside an image for a
    /// finding, so that a shape read as a letter or two, or a symbol such
    /// as "B" on a bold button, is not one (an assumption, 2026-11-12).
    /// </summary>
    public const int MinimumCharacters = 3;

    // The lines of an image a finding quotes.
    private const int QuotedLines = 3;

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, int> _imagesByVisit = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];

    private PhoneImagesOfTextRun(PhoneAuditLevel level)
    {
        Level = level;
    }

    public static PhoneImagesOfTextRun Empty { get; } = new(PhoneAuditLevel.AA);

    public PhoneAuditLevel Level { get; }

    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The images on pictures OCR read, counted once for each settled snapshot they were on.</summary>
    public int Images { get; private set; }

    /// <summary>The images with no finding.</summary>
    public int Passed { get; private set; }

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its images were checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    public int ImagesIn(PhoneVisit visit) =>
        _imagesByVisit.TryGetValue(visit, out var count) ? count : 0;

    public static PhoneImagesOfTextRun Run(
        PhoneContrastRun measured,
        PhoneScreenTimeline screens,
        PhoneDeviceTimeline device,
        PhoneAuditLevel level)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(device);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneImagesOfTextRun(level);
        var findings = new Dictionary<(PhoneVisit, string, string), PhoneFinding>();
        foreach (var snapshot in measured.Snapshots)
        {
            // With no OCR there are no words to find in an image.
            if (snapshot.Window is not { } window || string.IsNullOrEmpty(snapshot.Engine))
            {
                continue;
            }

            run.SnapshotsRead++;
            var visit = snapshot.Visit;
            run._visitsRead.Add(visit);
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            var density = device.DensityAt(snapshot.TakenBy.MonotonicNanoseconds)?.PixelsPerDp;
            var images = window.Items
                .Where(item => item.InstanceOf(PhoneTextMatching.ImageViewClass) && item.Node.VisibleToUser && !item.Bounds.IsEmpty)
                .ToList();
            var inside = InImages(images, snapshot.Lines);
            foreach (var image in images)
            {
                run.Images++;
                run._imagesByVisit[visit] = run.ImagesIn(visit) + 1;
                var lines = inside.TryGetValue(image, out var found) ? found : [];
                if (lines.Sum(line => PhoneTextMatching.Spelling(line.OcrText!).Length) < MinimumCharacters)
                {
                    run.Passed++;
                    continue;
                }

                var read = Read(image, lines, snapshot, density);
                Add(PhoneCheckNames.ImageOfText, $"{read}; {Exceptions145}; {Source(snapshot.Picture.Kind)}");
                if (level == PhoneAuditLevel.AAA)
                {
                    Add(PhoneCheckNames.ImageOfTextAaa, $"{read}; {Exceptions149}; {Source(snapshot.Picture.Kind)}");
                }

                void Add(string check, string measuredText)
                {
                    var key = (visit, check, image.Identity);
                    if (!findings.TryGetValue(key, out var finding))
                    {
                        findings[key] = finding = new PhoneFinding(check, PhoneCheckLevel.Warning, visit, image.Identity, image.Label);
                    }

                    if (finding._times.Count > 0 && finding._times[^1].Snapshot.EventId == snapshot.Settled.EventId)
                    {
                        return;
                    }

                    finding._times.Add(new PhoneFindingTime(
                        snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, image.Bounds, image.TreeKey,
                        measuredText, state));
                }
            }
        }

        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Findings = [.. findings.Values
            .OrderBy(finding => screenOrder.TryGetValue(finding.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(finding => finding.Visit.Start)
            .ThenBy(finding => finding.Check == PhoneCheckNames.ImageOfText ? 0 : 1)
            .ThenBy(finding => finding.Times[0].Time)
            .ThenBy(finding => finding.Times[0].Bounds.Top)
            .ThenBy(finding => finding.Times[0].Bounds.Left)];
        foreach (var finding in run.Findings)
        {
            if (!run._byVisit.TryGetValue(finding.Visit, out var ofVisit))
            {
                run._byVisit[finding.Visit] = ofVisit = [];
            }

            ofVisit.Add(finding);
        }

        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    private const string Exceptions145 =
        "a logotype, a picture with significant other visual content and an image that can be customized are not judged";

    private const string Exceptions149 =
        "1.4.9 allows an image of text only as pure decoration or where essential, as a logotype; neither is judged, nor a picture with significant other visual content";

    private static string Source(PhonePictureKind kind) =>
        kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video";

    /// <summary>
    /// The lines OCR read inside each image, at the smallest image their
    /// centre is in, leaving out the lines an item's text or hint holds.
    /// </summary>
    internal static Dictionary<PhoneCheckItem, List<PhoneTextLine>> InImages(
        IReadOnlyList<PhoneCheckItem> images,
        IReadOnlyList<PhoneTextLine> lines)
    {
        var bySize = images
            .OrderBy(image => (long)image.Bounds.Width * image.Bounds.Height)
            .ThenBy(image => image.Node.Index)
            .ToList();
        var inside = new Dictionary<PhoneCheckItem, List<PhoneTextLine>>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.OcrText) || IsAppsText(line))
            {
                continue;
            }

            var x = (line.Box.Left + line.Box.Right) / 2;
            var y = (line.Box.Top + line.Box.Bottom) / 2;
            var image = bySize.FirstOrDefault(each =>
                x >= each.Bounds.Left && x < each.Bounds.Right && y >= each.Bounds.Top && y < each.Bounds.Bottom);
            if (image is null)
            {
                continue;
            }

            if (!inside.TryGetValue(image, out var list))
            {
                inside[image] = list = [];
            }

            list.Add(line);
        }

        return inside;
    }

    // A line the app shows as text: its item, not an image, has text or a
    // hint that OCR's words matched; an item matched by its description
    // alone shows no text of its own.
    private static bool IsAppsText(PhoneTextLine line) =>
        line.Item is { } item &&
        !item.InstanceOf(PhoneTextMatching.ImageViewClass) &&
        (!string.IsNullOrWhiteSpace(item.Text) || !string.IsNullOrWhiteSpace(item.Node.Hint));

    // What OCR read inside the image, how tall, and whether its description
    // holds it.
    private static string Read(PhoneCheckItem image, List<PhoneTextLine> lines, PhoneContrastSnapshot snapshot, double? pixelsPerDp)
    {
        var ordered = lines.OrderBy(line => line.Box.Top).ThenBy(line => line.Box.Left).ToList();
        var quoted = string.Join(", ", ordered.Take(QuotedLines).Select(line => $"\"{line.OcrText}\""));
        if (ordered.Count > QuotedLines)
        {
            quoted += string.Create(CultureInfo.CurrentCulture, $" and {ordered.Count - QuotedLines:N0} more");
        }

        var count = ordered.Count == 1 ? "1 line" : string.Create(CultureInfo.CurrentCulture, $"{ordered.Count:N0} lines");
        var read = $"OCR ({snapshot.Engine}) read {count} of text inside the image: {quoted}";
        var heights = ordered.Select(line => line.WordsHeight).Where(height => height > 0).ToList();
        if (heights.Count > 0)
        {
            var (low, high) = (heights.Min(), heights.Max());
            read += pixelsPerDp is { } scale && scale > 0
                ? low == high
                    ? string.Create(CultureInfo.CurrentCulture, $"; the words are {low / scale:0.#} dp tall in OCR's box")
                    : string.Create(CultureInfo.CurrentCulture, $"; the words are {low / scale:0.#} to {high / scale:0.#} dp tall in OCR's box")
                : low == high
                    ? string.Create(CultureInfo.CurrentCulture, $"; the words are {low:N0} pixels tall in OCR's box, as no density was recorded")
                    : string.Create(CultureInfo.CurrentCulture, $"; the words are {low:N0} to {high:N0} pixels tall in OCR's box, as no density was recorded");
        }

        return $"{read}; {Described(image, ordered)}";
    }

    // Whether the image's description holds the words OCR read, as spelt for
    // matching (letters and digits, the characters OCR confuses as one).
    private static string Described(PhoneCheckItem image, IReadOnlyList<PhoneTextLine> lines)
    {
        if (string.IsNullOrWhiteSpace(image.Node.ContentDescription))
        {
            return "it has no description";
        }

        var description = PhoneTextMatching.Spelling(image.Node.ContentDescription);
        var words = lines
            .SelectMany(line => line.OcrText!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(PhoneTextMatching.Spelling)
            .Where(word => word.Length >= 2)
            .ToList();
        var held = words.Count(word => description.Contains(word, StringComparison.Ordinal));
        return held == words.Count
            ? "its description holds them"
            : held == 0 ? "its description holds none of them" : "its description holds some of them";
    }
}
