using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// The WCAG level an audit is made to (android-checks.md, "The audit
/// level"), which guides the checks and their reporting (owner,
/// 2026-11-12). It began as the text contrast check's level (step 4,
/// decision 3).
/// </summary>
public enum PhoneAuditLevel
{
    /// <summary>
    /// WCAG 2.2's level AA: text contrast to 1.4.3, Contrast (Minimum),
    /// 4.5:1, or 3:1 for large text; target size to 2.5.8, 24 by 24 dp.
    /// </summary>
    AA,

    /// <summary>
    /// Level AAA as well: text contrast to 1.4.6, Contrast (Enhanced), 7:1,
    /// or 4.5:1 for large text, in place of 1.4.3; and target size to 2.5.5,
    /// Target Size (Enhanced), 44 by 44 dp, as well as 2.5.8.
    /// </summary>
    AAA,
}

/// <summary>
/// The checks' settings an auditor chose, kept per Windows user beside the
/// player's layout: the audit level, and how long the phone must be held
/// turned for the orientation check (android-checks.md, step 15). A file
/// from before the
/// audit level was named holds the text contrast check's level, as
/// "contrastLevel", which is read as the audit level.
/// </summary>
public sealed record PhoneCheckSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static PhoneCheckSettings Default { get; } = new();

    public PhoneAuditLevel AuditLevel { get; init; } = PhoneAuditLevel.AA;

    /// <summary>
    /// How long the phone must be held turned, in seconds, before a screen
    /// that did not turn with it is a finding; one of
    /// <see cref="PhoneOrientationChecks.HoldChoices"/>.
    /// </summary>
    public int OrientationHoldSeconds { get; init; } = PhoneOrientationChecks.DefaultHoldSeconds;

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Windows A11y Recorder",
            "check-settings.json");

    /// <summary>
    /// Reads the settings; a missing or unreadable file, or a level that is
    /// not AA or AAA, gives the defaults, and a hold time that is not one of
    /// the choices gives the default hold time.
    /// </summary>
    public static PhoneCheckSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Default;
            }

            var text = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<PhoneCheckSettings>(text, JsonOptions);
            if (settings is null)
            {
                return Default;
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                !document.RootElement.TryGetProperty("auditLevel", out _) &&
                document.RootElement.TryGetProperty("contrastLevel", out var legacy) &&
                legacy.TryGetInt32(out var level))
            {
                settings = settings with { AuditLevel = (PhoneAuditLevel)level };
            }

            if (!PhoneOrientationChecks.HoldChoices.Contains(settings.OrientationHoldSeconds))
            {
                settings = settings with { OrientationHoldSeconds = PhoneOrientationChecks.DefaultHoldSeconds };
            }

            return Enum.IsDefined(settings.AuditLevel) ? settings : Default;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Default;
        }
    }

    /// <summary>Writes the settings whole; false, leaving any earlier file, when they cannot be written.</summary>
    public bool TrySave(string path)
    {
        var temporary = path + ".partial";
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            return false;
        }
    }
}

/// <summary>
/// Step 4c of android-checks.md: the text contrast check, from the lines
/// step 4b measured. Each line's ratio is held to the chosen level's limits:
/// below the limit for large text, an error; between the limits, a warning,
/// as the text's size and weight are not known; from the video, a ratio
/// that could be 12% higher or lower than measured and so on the other
/// side of a limit is a warning, near the limit. The findings are merged
/// in each visit by level and line, as the tree checks' are by item.
/// </summary>
public sealed class PhoneTextContrastRun
{
    /// <summary>
    /// How far a ratio from the video may be from the true one, as a share:
    /// the measurement found up to 11.2% on whole items ("Step 4: what the
    /// measurement found") and up to 6.5% on OCR's lines (step 4b).
    /// </summary>
    public const double VideoMargin = 0.12;

    private readonly Dictionary<PhoneScreen, List<PhoneFinding>> _byScreen = [];
    private readonly Dictionary<PhoneScreen, int> _linesByScreen = [];
    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, int> _linesByVisit = [];

    private PhoneTextContrastRun(PhoneAuditLevel level)
    {
        Level = level;
    }

    public static PhoneTextContrastRun Empty { get; } = new(PhoneAuditLevel.AA);

    public PhoneAuditLevel Level { get; }

    /// <summary>The check's name for the level.</summary>
    public string Check => CheckFor(Level);

    /// <summary>The findings, by app, screen, visit, level, then first time.</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The lines measured, counted once for each settled snapshot they were measured on.</summary>
    public int Lines { get; private set; }

    /// <summary>The lines that passed.</summary>
    public int Passed { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    /// <summary>A screen's findings, from all its visits.</summary>
    public IReadOnlyList<PhoneFinding> For(PhoneScreen screen) =>
        _byScreen.TryGetValue(screen, out var list) ? list : [];

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>How many lines were measured in a visit, counted as <see cref="Lines"/> is.</summary>
    public int LinesIn(PhoneVisit visit) =>
        _linesByVisit.TryGetValue(visit, out var count) ? count : 0;

    /// <summary>How many lines were measured on a screen, counted as <see cref="Lines"/> is.</summary>
    public int LinesOn(PhoneScreen screen) =>
        _linesByScreen.TryGetValue(screen, out var count) ? count : 0;

    public static string CheckFor(PhoneAuditLevel level) =>
        level == PhoneAuditLevel.AAA ? PhoneCheckNames.TextContrastAaa : PhoneCheckNames.TextContrastAa;

    /// <summary>The level's limits: for text that is not large, and for large text.</summary>
    public static (double Normal, double Large) Limits(PhoneAuditLevel level) =>
        level == PhoneAuditLevel.AAA ? (7.0, 4.5) : (4.5, 3.0);

    public static PhoneTextContrastRun Run(
        PhoneContrastRun measured,
        PhoneScreenTimeline screens,
        PhoneDeviceTimeline device,
        PhoneAuditLevel level)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(device);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneTextContrastRun(level);
        var check = run.Check;
        var findings = new Dictionary<(PhoneVisit, PhoneCheckLevel, string), PhoneFinding>();
        foreach (var snapshot in measured.Snapshots)
        {
            var visit = snapshot.Visit;
            var time = snapshot.Settled.MonotonicNanoseconds;
            var state = PhoneCheckRun.StateAt(visit, time);
            var density = device.DensityAt(snapshot.TakenBy.MonotonicNanoseconds);

            // An item's lines are numbered from the top, so an item of
            // several lines has a finding for each.
            var lineOf = new Dictionary<PhoneTextLine, (int Number, int Count)>();
            foreach (var group in snapshot.Lines.Where(line => line.Item is not null).GroupBy(line => line.Item!))
            {
                var ordered = group.OrderBy(line => line.Box.Top).ThenBy(line => line.Box.Left).ToList();
                for (var index = 0; index < ordered.Count; index++)
                {
                    lineOf[ordered[index]] = (index + 1, ordered.Count);
                }
            }

            foreach (var line in snapshot.Lines)
            {
                if (line.Colours is not { } colours)
                {
                    continue;
                }

                run.Lines++;
                run._linesByScreen[visit.Screen] = run.LinesOn(visit.Screen) + 1;
                run._linesByVisit[visit] = run.LinesIn(visit) + 1;
                var judged = Judge(line, colours, snapshot.Picture.Kind, snapshot.Engine, level, density?.PixelsPerDp);
                if (judged is not { } result)
                {
                    run.Passed++;
                    continue;
                }

                var (identity, label) = Name(line, lineOf.TryGetValue(line, out var number) ? number : (1, 1));
                var key = (visit, result.Level, identity);
                if (!findings.TryGetValue(key, out var finding))
                {
                    findings[key] = finding = new PhoneFinding(check, result.Level, visit, identity, label);
                }

                // One time for each settled snapshot: a line read twice on
                // one picture, as OCR can split it, is the first reading.
                if (finding._times.Count > 0 && finding._times[^1].Snapshot.EventId == snapshot.Settled.EventId)
                {
                    continue;
                }

                // The time is the picture's, so the player shows what was
                // measured: the snapshot can settle before the video's
                // first frame, or the frame be taken later in the visit.
                var treeKey = (line.Item ?? line.Container)?.TreeKey ?? string.Empty;
                finding._times.Add(new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, line.Box, treeKey, result.Measured, state));
            }
        }

        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Findings = [.. findings.Values
            .OrderBy(finding => screenOrder.TryGetValue(finding.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(finding => finding.Visit.Start)
            .ThenBy(finding => finding.Level)
            .ThenBy(finding => finding.Times[0].Time)
            .ThenBy(finding => finding.Times[0].Bounds.Top)];
        foreach (var finding in run.Findings)
        {
            if (!run._byScreen.TryGetValue(finding.Screen, out var list))
            {
                run._byScreen[finding.Screen] = list = [];
            }

            list.Add(finding);
            if (!run._byVisit.TryGetValue(finding.Visit, out var ofVisit))
            {
                run._byVisit[finding.Visit] = ofVisit = [];
            }

            ofVisit.Add(finding);
        }

        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// A line's level and what was measured, or null when it passes. The
    /// ratio is compared unrounded, as WCAG's limits are minimums with no
    /// rounding; ATF allows 0.01 below them, which is not followed.
    /// </summary>
    public static (PhoneCheckLevel Level, string Measured)? Judge(
        PhoneTextLine line,
        PhoneSwatch colours,
        PhonePictureKind kind,
        string? engine,
        PhoneAuditLevel level,
        double? pixelsPerDp)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(colours);
        var source = Source(line, kind, engine);
        if (colours.Joined < 2)
        {
            return (PhoneCheckLevel.NotRun, $"one colour, {PhoneColours.Hex(colours.Background)}, in the box, so no text was seen there; {source}");
        }

        var ratio = PhoneColours.Ratio(PhoneColours.Luminance(colours.Background), PhoneColours.Luminance(colours.Foreground));
        var (normal, large) = Limits(level);
        var video = kind == PhonePictureKind.Frame;
        var low = video ? ratio * (1 - VideoMargin) : ratio;
        var high = video ? ratio * (1 + VideoMargin) : ratio;
        var colour = $"{Shown(ratio, normal, large)}:1, {PhoneColours.Hex(colours.Foreground)} on {PhoneColours.Hex(colours.Background)}";
        var size = Size(line, pixelsPerDp);
        string verdict;
        PhoneCheckLevel result;
        if (high < large)
        {
            result = PhoneCheckLevel.Error;
            verdict = $"below {Limit(large)}:1, the limit for large text";
        }
        else if (high < normal)
        {
            result = PhoneCheckLevel.Warning;
            verdict = $"fails unless the text is large: {Limit(normal)}:1 is needed, {Limit(large)}:1 for large text";
            if (low < large)
            {
                verdict += $", and it may be below {Limit(large)}:1 as the video's colours are approximate";
            }
        }
        else if (low < normal)
        {
            result = PhoneCheckLevel.Warning;
            verdict = $"near the {Limit(normal)}:1 limit, so it may fail, as the video's colours are approximate";
        }
        else
        {
            return null;
        }

        return (result, $"{colour}; {verdict}; {size}; {source}");
    }

    // Where the colours and the line's place came from (decision 5: a line
    // placed or found by OCR says so; one on the app's bounds says nothing
    // about OCR).
    private static string Source(PhoneTextLine line, PhonePictureKind kind, string? engine)
    {
        var picture = kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video";
        var ocr = string.IsNullOrEmpty(engine) ? "OCR" : $"OCR ({engine})";
        return line.Place switch
        {
            PhoneTextPlace.AppTextPlacedByOcr => $"{picture}; the app's text, placed on the screen by {ocr}",
            PhoneTextPlace.FoundByOcr => $"{picture}; text found by {ocr}, in no item's text",
            _ => $"{picture}; measured on the item's bounds",
        };
    }

    // The words' height, which is not the text's size: OCR's box runs from
    // the tallest letter to the lowest, and the font is not known.
    private static string Size(PhoneTextLine line, double? pixelsPerDp)
    {
        if (line.WordsHeight <= 0)
        {
            return "the text's size and weight are not known";
        }

        var height = pixelsPerDp is { } scale && scale > 0
            ? string.Create(CultureInfo.CurrentCulture, $"{line.WordsHeight / scale:0.#} dp tall in OCR's box")
            : string.Create(CultureInfo.CurrentCulture, $"{line.WordsHeight:N0} pixels tall in OCR's box, as no density was recorded");
        return $"the words are {height}; the text's size and weight are not known";
    }

    // The ratio as shown: two decimals, or three when two would round it up
    // to a limit it is below.
    private static string Shown(double ratio, double normal, double large)
    {
        var two = Math.Round(ratio, 2, MidpointRounding.AwayFromZero);
        var places = (ratio < normal && two >= normal) || (ratio < large && two >= large) ? "0.000" : "0.00";
        return ratio.ToString(places, CultureInfo.InvariantCulture);
    }

    private static string Limit(double limit) => limit.ToString("0.#", CultureInfo.InvariantCulture);

    // A line's identity within the screen and its label: the item's, with
    // the line's number when the item has several; for text found by OCR,
    // the item it is in and the words as OCR spelt them.
    private static (string Identity, string Label) Name(PhoneTextLine line, (int Number, int Count) number)
    {
        if (line.Item is { } item)
        {
            var label = number.Count > 1
                ? string.Create(CultureInfo.CurrentCulture, $"{item.Label}, line {number.Number:N0} of {number.Count:N0}, '{line.OcrText}'")
                : item.Label;
            return (string.Create(CultureInfo.InvariantCulture, $"{item.Identity}\u001Fline {number.Number}"), label);
        }

        var within = line.Container is { } container ? $" in {container.Label}" : string.Empty;
        return (
            $"{line.Container?.Identity ?? string.Empty}\u001Focr {PhoneTextMatching.Spelling(line.OcrText ?? string.Empty)}",
            $"'{line.OcrText}'{within}");
    }
}
