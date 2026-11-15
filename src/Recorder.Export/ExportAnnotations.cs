using System.Globalization;
using System.Text;

namespace Recorder.Export;

/// <summary>
/// A report's evidence reference drawn on an export: its number, such as
/// E12, shown at the bottom left of the picture from its time for
/// <see cref="ExportAnnotations.ShownNanoseconds"/>, and a chapter of the
/// MP4 named by <paramref name="Title"/>, such as "E12, finding 3". See
/// docs/architecture/mp4-export.md, "Evidence numbers".
/// </summary>
public sealed record ExportAnnotation(string Reference, long TimeNanoseconds, string Title);

/// <summary>What is drawn over a video sample: the evidence numbers shown on it, in number order.</summary>
public sealed record ExportOverlay(IReadOnlyList<string> References)
{
    public static readonly ExportOverlay None = new([]);

    /// <summary>Whether two overlays draw the same numbers.</summary>
    public bool SameAs(ExportOverlay? other) => other is not null && References.SequenceEqual(other.References, StringComparer.Ordinal);
}

/// <summary>A chapter of an export: its start, in milliseconds, and its title.</summary>
public readonly record struct Mp4Chapter(long StartMilliseconds, string Title);

/// <summary>
/// When each evidence number is shown on an export, and the export's
/// chapters. A number is shown on each video sample whose middle is from
/// its time to 4 seconds after it, as a recorded frame is shown from the
/// sample nearest its time, so it appears with the picture it is about
/// (owner, 2026-11-15: "for it to last 4 seconds from the time the
/// evidence is recorded against").
/// </summary>
public static class ExportAnnotations
{
    public const long ShownNanoseconds = 4_000_000_000;

    /// <summary>
    /// The longest chapter title, in UTF-8 bytes: VLC reads a chapter
    /// sample of 256 bytes, 2 of them its length (mp4-export.md, "Evidence
    /// numbers").
    /// </summary>
    public const int MaximumTitleBytes = 250;

    /// <summary>
    /// The overlay of each sample of a schedule, the same object for
    /// samples in a row that show the same numbers. Annotations before the
    /// recording's start or after its end are not shown.
    /// </summary>
    public static IReadOnlyList<ExportOverlay> Plan(IReadOnlyList<ScheduledFrame> schedule, IReadOnlyList<ExportAnnotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(annotations);
        var ordered = annotations.Where(annotation => annotation.TimeNanoseconds >= 0).OrderBy(annotation => annotation.TimeNanoseconds).ToList();
        var overlays = new List<ExportOverlay>(schedule.Count);
        var shown = new List<ExportAnnotation>();
        var next = 0;
        var previous = ExportOverlay.None;
        foreach (var sample in schedule)
        {
            var middle = sample.StartNanoseconds + (sample.DurationNanoseconds / 2);
            while (next < ordered.Count && ordered[next].TimeNanoseconds <= middle)
            {
                shown.Add(ordered[next]);
                next++;
            }

            shown.RemoveAll(annotation => middle >= annotation.TimeNanoseconds + ShownNanoseconds);
            var overlay = new ExportOverlay([.. shown.Select(annotation => annotation.Reference).Distinct(StringComparer.Ordinal).OrderBy(NumberOf).ThenBy(reference => reference, StringComparer.Ordinal)]);
            if (overlay.SameAs(previous))
            {
                overlay = previous;
            }

            overlays.Add(overlay);
            previous = overlay;
        }

        return overlays;
    }

    /// <summary>
    /// The export's chapters: one for each annotation within the
    /// recording, in time order and then number order, each starting a
    /// millisecond after the one before when they share a time, so that
    /// each is a chapter of its own. Titles are cut to
    /// <see cref="MaximumTitleBytes"/>.
    /// </summary>
    public static IReadOnlyList<Mp4Chapter> Chapters(IReadOnlyList<ExportAnnotation> annotations, long endNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        var chapters = new List<Mp4Chapter>();
        var last = -1L;
        foreach (var annotation in annotations
            .Where(annotation => annotation.TimeNanoseconds >= 0 && annotation.TimeNanoseconds < endNanoseconds)
            .OrderBy(annotation => annotation.TimeNanoseconds)
            .ThenBy(annotation => NumberOf(annotation.Reference))
            .ThenBy(annotation => annotation.Reference, StringComparer.Ordinal))
        {
            var start = Math.Max(annotation.TimeNanoseconds / 1_000_000, last + 1);
            chapters.Add(new Mp4Chapter(start, Cut(annotation.Title, MaximumTitleBytes)));
            last = start;
        }

        return chapters;
    }

    /// <summary>A title cut to at most the bytes given in UTF-8, at a character's end, with "..." where cut.</summary>
    public static string Cut(string title, int bytes)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (Encoding.UTF8.GetByteCount(title) <= bytes)
        {
            return title;
        }

        var cut = new StringBuilder();
        var used = 3;
        foreach (var rune in title.EnumerateRunes())
        {
            used += rune.Utf8SequenceLength;
            if (used > bytes)
            {
                break;
            }

            cut.Append(rune.ToString());
        }

        return cut.ToString().TrimEnd() + "...";
    }

    // The number of a reference such as E12, for number order.
    private static long NumberOf(string reference) =>
        reference.Length > 1 && long.TryParse(reference.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : long.MaxValue;
}
