using System.Buffers.Binary;
using System.Text;
using Recorder.Analysis;
using Recorder.Export;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// A report's evidence numbers on an MP4 export: when each is shown, the
/// chapters named after them, the chapter list written into the file, and
/// the evidence list a report writes beside it. See
/// docs/architecture/mp4-export.md, "Evidence numbers".
/// </summary>
public sealed class Mp4AnnotationTests : IDisposable
{
    private const long Second = 1_000_000_000;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mp4-annotations-" + Guid.NewGuid().ToString("N"));

    public Mp4AnnotationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static SessionVideoFrame Frame(long nanoseconds) =>
        new(nanoseconds, "frame.png", "frame.png", 640, 360);

    [Fact]
    public void ANumberIsShown_FromTheSampleNearestItsTime_ForFourSeconds()
    {
        var schedule = VideoSchedule.Plan([Frame(0)], 10 * Second, 20);

        var overlays = ExportAnnotations.Plan(schedule, [new ExportAnnotation("E5", 1 * Second, "E5, click")]);

        // Samples are 50 ms; a sample shows it when its middle is from 1 s
        // to before 5 s: samples 20 to 99.
        var shown = Enumerable.Range(0, schedule.Count).Where(index => overlays[index].References.Count > 0).ToList();
        Assert.Equal(20, shown[0]);
        Assert.Equal(99, shown[^1]);
        Assert.Equal(80, shown.Count);
        Assert.Same(overlays[20], overlays[99]);
        Assert.Same(ExportOverlay.None, overlays[0]);
    }

    [Fact]
    public void NumbersShownTogether_AreInNumberOrder_AndThoseOutsideTheRecordingAreNot()
    {
        var schedule = VideoSchedule.Plan([Frame(0)], 10 * Second, 20);

        var overlays = ExportAnnotations.Plan(schedule,
        [
            new ExportAnnotation("E12", 2 * Second, "E12, finding 3"),
            new ExportAnnotation("E5", 3 * Second, "E5, visit 1 to Home"),
            new ExportAnnotation("E9", -1, "E9, before"),
            new ExportAnnotation("E7", 11 * Second, "E7, after")
        ]);

        Assert.Equal(["E12"], overlays[45].References);
        Assert.Equal(["E5", "E12"], overlays[70].References);
        Assert.Equal(["E5"], overlays[130].References);
        Assert.DoesNotContain(overlays, overlay => overlay.References.Contains("E9") || overlay.References.Contains("E7"));
    }

    [Fact]
    public void Chapters_AreInTimeAndNumberOrder_AMillisecondApartWhereTheyShareATime()
    {
        var chapters = ExportAnnotations.Chapters(
        [
            new ExportAnnotation("E10", 3 * Second, "E10, finding 2"),
            new ExportAnnotation("E2", 3 * Second, "E2, visit 1 to Home"),
            new ExportAnnotation("E1", 1_500_000_000, "E1, screen 1, Home"),
            new ExportAnnotation("E3", 20 * Second, "E3, after the end"),
            new ExportAnnotation("E4", -5, "E4, before the start")
        ], 10 * Second);

        Assert.Equal(
            [new Mp4Chapter(1500, "E1, screen 1, Home"), new Mp4Chapter(3000, "E2, visit 1 to Home"), new Mp4Chapter(3001, "E10, finding 2")],
            chapters);
    }

    [Fact]
    public void ALongTitle_IsCutAtACharactersEnd_WithinTheBytesVlcReads()
    {
        var title = "E1, " + new string('\u00e9', 300);

        var cut = ExportAnnotations.Cut(title, ExportAnnotations.MaximumTitleBytes);

        Assert.True(Encoding.UTF8.GetByteCount(cut) <= ExportAnnotations.MaximumTitleBytes);
        Assert.EndsWith("\u00e9...", cut, StringComparison.Ordinal);
        Assert.Equal("E1, short", ExportAnnotations.Cut("E1, short", 250));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheChapterList_IsATextTrackAndANeroList_AndTheMediaDoesNotMove(bool movieLast)
    {
        var path = Path.Combine(_directory, "movie.mp4");
        var media = Enumerable.Repeat((byte)0xAB, 100).ToArray();
        var (file, mediaOffset) = Movie(media, movieLast);
        File.WriteAllBytes(path, file);
        var chapters = new[] { new Mp4Chapter(1500, "E1, screen 1, Home"), new Mp4Chapter(3000, "E2, visit 1 to Caf\u00e9") };

        Assert.True(Mp4Chapters.Apply(path, chapters));

        var written = File.ReadAllBytes(path);
        Assert.Equal(media, written.AsSpan(mediaOffset, media.Length).ToArray());
        var top = Boxes(written, 0, written.Length);
        Assert.Single(top, box => box.Type == "moov");
        Assert.Equal(movieLast ? 0 : 1, top.Count(box => box.Type == "free"));
        var movie = top.Single(box => box.Type == "moov");
        var tracks = Boxes(written, movie.Start + 8, movie.End).Where(box => box.Type == "trak").ToList();
        Assert.Equal(2, tracks.Count);

        // The video track's media is where it was, and it refers to the
        // chapter track, which is the next track ID.
        Assert.Equal((uint)mediaOffset, BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(Find(written, tracks[0], "mdia", "minf", "stbl", "stco").Start + 16)));
        var chap = Find(written, tracks[0], "tref", "chap");
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(chap.Start + 8)));
        var header = Find(written, movie, "mvhd");
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(header.End - 4)));

        // The chapter track: disabled, ID 2, tx3g, an empty sample before
        // the first chapter and one for each.
        var text = tracks[1];
        var tkhd = Find(written, text, "tkhd");
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(tkhd.Start + 8)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(tkhd.Start + 20)));
        Assert.Equal("text", Encoding.Latin1.GetString(written, Find(written, text, "mdia", "hdlr").Start + 16, 4));
        var stsd = Find(written, text, "mdia", "minf", "stbl", "stsd");
        Assert.Equal("tx3g", Encoding.Latin1.GetString(written, stsd.Start + 20, 4));
        var stbl = Find(written, text, "mdia", "minf", "stbl");
        var stts = Find(written, stbl, "stts");
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(stts.Start + 12)));
        Assert.Equal([1500u, 1500u, 7000u], Enumerable.Range(0, 3).Select(index => BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(stts.Start + 20 + (index * 8)))).ToArray());
        var stsz = Find(written, stbl, "stsz");
        var sizes = Enumerable.Range(0, 3).Select(index => (int)BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(stsz.Start + 20 + (index * 4)))).ToArray();
        var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(Find(written, stbl, "stco").Start + 16));
        Assert.Equal(2, sizes[0]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(written.AsSpan(offset)));
        Assert.Equal("E1, screen 1, Home", Sample(written, offset + sizes[0]));
        Assert.Equal("E2, visit 1 to Caf\u00e9", Sample(written, offset + sizes[0] + sizes[1]));
        Assert.Equal("encd", Encoding.Latin1.GetString(written, offset + sizes[0] + sizes[1] - 8, 4));

        // The Nero list: each chapter's start in 100 ns units and its title.
        var chpl = Find(written, movie, "udta", "chpl");
        Assert.Equal(2, written[chpl.Start + 16]);
        Assert.Equal(15_000_000UL, BinaryPrimitives.ReadUInt64BigEndian(written.AsSpan(chpl.Start + 17)));
        Assert.Equal("E1, screen 1, Home", Encoding.UTF8.GetString(written, chpl.Start + 26, written[chpl.Start + 25]));
    }

    [Fact]
    public void MoreThan255Chapters_AreInTheTextTrackOnly()
    {
        var path = Path.Combine(_directory, "many.mp4");
        File.WriteAllBytes(path, Movie(new byte[10], movieLast: true).File);
        var chapters = Enumerable.Range(0, 300).Select(index => new Mp4Chapter(index * 10, $"E{index + 1}, finding {index + 1}")).ToList();

        Assert.True(Mp4Chapters.Apply(path, chapters));

        var written = File.ReadAllBytes(path);
        var movie = Boxes(written, 0, written.Length).Single(box => box.Type == "moov");
        Assert.DoesNotContain(Boxes(written, movie.Start + 8, movie.End), box => box.Type == "udta");
        var text = Boxes(written, movie.Start + 8, movie.End).Where(box => box.Type == "trak").Last();
        var stsz = Find(written, text, "mdia", "minf", "stbl", "stsz");
        Assert.Equal(300u, BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(stsz.Start + 16)));
    }

    [Fact]
    public void AReportsEvidenceList_IsTheRowsOfItsEvidenceTable_AndReadsBack()
    {
        const string markdown = """
            # Report

            | Ref | Screen |
            | --- | --- |
            | E40 | not evidence |

            ## 5. Evidence

            | Ref | Time | Evidence |
            | --- | --- | --- |
            | E1 | Whole recording | The app. |
            | E3 | 00:00:01.000 | Visit 1. |

            ## Appendix

            | E9 | after the table |
            """;

        Assert.Equal(new HashSet<string> { "E1", "E3" }, ReportEvidenceList.Listed(markdown));

        ReportEvidenceItem[] entries =
        [
            new("E1", "one", null, "app com.example"),
            new("E3", "one", 1_000_000_000, "visit 1 to Home"),
            new("E4", "two", 2_000_000_000, "visit 1 to Settings"),
            new("E5", null, null, "counts of findings")
        ];
        File.WriteAllBytes(Path.Combine(_directory, ReportEvidenceList.FileName), ReportEvidenceList.Serialize(entries));
        var read = ReportEvidenceList.Read(_directory);
        Assert.Equal(entries, read);

        var (timed, untimed) = ReportEvidenceList.ForRecording(read!, "one");
        Assert.Equal(["E3"], timed.Select(entry => entry.Reference));
        Assert.Equal(2, untimed);
        Assert.Equal("E3, visit 1 to Home", ReportEvidenceList.TitleOf(timed[0]));
        Assert.Null(ReportEvidenceList.Read(Path.Combine(_directory, "missing")));
    }

    // A file as Windows writes one: the file type, the media, and the
    // movie, last or first, of one video track whose one chunk is the
    // media, and a movie 10 seconds long at 1000 a second.
    private static (byte[] File, int MediaOffset) Movie(byte[] media, bool movieLast)
    {
        var ftyp = Box("ftyp", [.. "isom"u8, 0, 0, 2, 0, .. "isomavc1"u8]);
        byte[] MovieBox(int mediaOffset)
        {
            var mvhd = new byte[100];
            BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(12), 1000);
            BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(16), 10_000);
            BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(96), 2);
            var tkhd = new byte[84];
            BinaryPrimitives.WriteUInt32BigEndian(tkhd, 0x7);
            BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(12), 1);
            var stco = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(stco.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(stco.AsSpan(8), (uint)mediaOffset);
            var hdlr = new byte[25];
            "vide"u8.CopyTo(hdlr.AsSpan(8));
            return Box("moov", [.. Box("mvhd", mvhd), .. Box("trak", [.. Box("tkhd", tkhd),
                .. Box("mdia", [.. Box("hdlr", hdlr), .. Box("minf", Box("stbl", Box("stco", stco)))])])]);
        }

        if (movieLast)
        {
            var offset = ftyp.Length + 8;
            return ([.. ftyp, .. Box("mdat", media), .. MovieBox(offset)], offset);
        }

        var length = MovieBox(0).Length;
        var first = ftyp.Length + length + 8;
        return ([.. ftyp, .. MovieBox(first), .. Box("mdat", media)], first);
    }

    private static string Sample(byte[] file, int offset) =>
        Encoding.UTF8.GetString(file, offset + 2, BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(offset)));

    private static byte[] Box(string type, byte[] body)
    {
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.Latin1.GetBytes(type, box.AsSpan(4));
        body.CopyTo(box, 8);
        return box;
    }

    private readonly record struct Found(int Start, int End, string Type);

    private static List<Found> Boxes(byte[] file, int start, int end)
    {
        var found = new List<Found>();
        for (var at = start; at + 8 <= end;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            found.Add(new Found(at, at + size, Encoding.Latin1.GetString(file, at + 4, 4)));
            at += size;
        }

        return found;
    }

    private static Found Find(byte[] file, Found box, params string[] path)
    {
        foreach (var type in path)
        {
            box = Boxes(file, box.Start + 8, box.End).Single(child => child.Type == type);
        }

        return box;
    }
}
