using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 5 of docs/architecture/android-implementation-plan.md, step 5b: the
// touch reports kept in the playback index and read back as the fingers on
// the phone's screen at a time, with their paths, for the player to draw.
// The reports are laid out as the touch collector stores them.
public sealed class PhoneTouchTimelineTests : IDisposable
{
    private const long Second = 1_000_000_000;
    private const long Millisecond = 1_000_000;

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "android.touch", "AndroidTouchCollector", "1", [AndroidTouchRecords.TouchChannel], "test");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "phone-touches-" + Guid.NewGuid().ToString("N"));

    public PhoneTouchTimelineTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // The plan's target test: three taps at the top, middle and bottom, then
    // a two-finger swipe up.
    private static List<(long Time, AndroidTouchReport Report)> TapsAndSwipe()
    {
        var reports = new List<(long, AndroidTouchReport)>();
        var tracking = 1;
        foreach (var (start, y) in new[] { (1 * Second, 200.0), (2 * Second, 1200.0), (3 * Second, 2200.0) })
        {
            reports.Add((start, Report(Pointer(0, tracking, AndroidTouchRecords.DownAction, 540, y))));
            reports.Add((start + 50 * Millisecond, Report(Pointer(0, tracking, AndroidTouchRecords.MoveAction, 541, y))));
            reports.Add((start + 100 * Millisecond, Report(Pointer(0, tracking, AndroidTouchRecords.UpAction, 541, y))));
            tracking++;
        }

        for (var step = 0; step <= 5; step++)
        {
            var action = step == 0 ? AndroidTouchRecords.DownAction : AndroidTouchRecords.MoveAction;
            var y = 1800.0 - step * 200;
            reports.Add((4 * Second + step * 100 * Millisecond, Report(
                Pointer(0, 10, action, 400, y),
                Pointer(1, 11, action, 700, y))));
        }

        reports.Add((4 * Second + 600 * Millisecond, Report(
            Pointer(0, 10, AndroidTouchRecords.UpAction, 400, 800),
            Pointer(1, 11, AndroidTouchRecords.UpAction, 700, 800))));
        return reports;
    }

    private static AndroidTouchReport Report(params AndroidTouchPointer[] pointers) =>
        new("/dev/input/event1", 0, 0, 0, 1080, 2404, pointers);

    private static AndroidTouchReport Sideways(params AndroidTouchPointer[] pointers) =>
        new("/dev/input/event1", 0, 0, 90, 2404, 1080, pointers);

    private static AndroidTouchPointer Pointer(int slot, int trackingId, string action, double x, double y) =>
        new(slot, trackingId, action, 1, 2, x, y, x, y, 40, 300, 200);

    private PhoneTouchTimeline FromIndex(IEnumerable<(long Time, AndroidTouchReport Report)> reports, out PlaybackIndex index)
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var sequence = 0UL;
        foreach (var (time, report) in reports)
        {
            sequence++;
            builder.Add((long)sequence, RecorderEventFactory.Create(
                "s", Collector, AndroidTouchRecords.TouchChannel, sequence, time, AndroidTouchRecords.TouchEventType, report));
        }

        index = builder.Build();
        var archiveBuilder = new SessionPlaybackArchiveBuilder(_folder, retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(5), 5 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));
        return archive.PhoneTouches;
    }

    [Fact]
    public void Index_KeepsEachTouchReport_InCompactForm()
    {
        FromIndex(TapsAndSwipe(), out var index);

        Assert.Equal(29, index.Version);
        var kept = index.Events.Where(item => item.Channel == AndroidTouchRecords.TouchChannel).ToArray();
        Assert.Equal(16, kept.Length);
        var payload = kept[0].Payload;
        Assert.Equal(0, payload.GetProperty("rotation").GetInt32());
        Assert.Equal(1080, payload.GetProperty("screenWidth").GetInt32());
        Assert.Equal(2404, payload.GetProperty("screenHeight").GetInt32());
        var pointer = payload.GetProperty("pointers")[0];
        Assert.Equal(
            ["slot", "trackingId", "action", "x", "y"],
            pointer.EnumerateObject().Select(property => property.Name));
        Assert.Equal("down", pointer.GetProperty("action").GetString());
        Assert.False(payload.TryGetProperty("device", out _));
    }

    [Fact]
    public void At_EachTap_HasOneFingerAtItsPlace_UntilItIsLifted()
    {
        var touches = FromIndex(TapsAndSwipe(), out _);

        Assert.True(touches.Recorded);
        Assert.Null(touches.At(Second - 1));
        foreach (var (start, y) in new[] { (1 * Second, 200.0), (2 * Second, 1200.0), (3 * Second, 2200.0) })
        {
            var down = touches.At(start + 70 * Millisecond);
            Assert.NotNull(down);
            var finger = Assert.Single(down.Fingers);
            Assert.Equal((541.0, y), (finger.X, finger.Y));
            Assert.Equal(1, finger.Number);
            Assert.Equal([540.0, 541.0], finger.Path.Select(point => point.X));
            Assert.Equal(string.Create(System.Globalization.CultureInfo.CurrentCulture, $"1 finger on the screen at x 541, y {y:0}"), down.Describe());

            // Lifted: no finger, and nothing to describe.
            var up = touches.At(start + 100 * Millisecond);
            Assert.NotNull(up);
            Assert.Empty(up.Fingers);
            Assert.Equal(string.Empty, up.Describe());
            Assert.Empty(touches.At(start + 900 * Millisecond)!.Fingers);
        }
    }

    [Fact]
    public void At_TwoFingerSwipe_HasTwoNumberedFingersMovingTogether()
    {
        var touches = FromIndex(TapsAndSwipe(), out _);

        var during = touches.At(4 * Second + 350 * Millisecond);
        Assert.NotNull(during);
        Assert.Equal([1, 2], during.Fingers.Select(finger => finger.Number));
        Assert.Equal([400.0, 700.0], during.Fingers.Select(finger => finger.X));
        Assert.All(during.Fingers, finger => Assert.Equal(1200.0, finger.Y));
        Assert.All(during.Fingers, finger => Assert.Equal(
            [1800.0, 1600.0, 1400.0, 1200.0],
            finger.Path.Select(point => point.Y)));
        Assert.Equal("2 fingers on the screen, 1 at x 400, y 1200, 2 at x 700, y 1200", during.Describe());
        Assert.Empty(touches.At(4 * Second + 600 * Millisecond)!.Fingers);
    }

    [Fact]
    public void At_NumbersTheFingersInTheOrderTheyWentDown_NotBySlot()
    {
        var touches = FromIndex(
        [
            (Second, Report(Pointer(1, 20, AndroidTouchRecords.DownAction, 100, 100))),
            (Second + 10 * Millisecond, Report(
                Pointer(0, 21, AndroidTouchRecords.DownAction, 900, 900),
                Pointer(1, 20, AndroidTouchRecords.MoveAction, 100, 100)))
        ], out _);

        var both = touches.At(2 * Second)!;
        Assert.Equal([(1, 20), (2, 21)], both.Fingers.Select(finger => (finger.Number, finger.TrackingId)));
    }

    [Fact]
    public void Path_IsTheLastSecond_AndStartsWhereTheFingerWentDown()
    {
        var reports = new List<(long, AndroidTouchReport)>
        {
            // A finger lifted just before another goes down in the same slot.
            (Second, Report(Pointer(0, 1, AndroidTouchRecords.DownAction, 10, 10))),
            (Second + 100 * Millisecond, Report(Pointer(0, 1, AndroidTouchRecords.UpAction, 10, 10)))
        };
        for (var step = 0; step <= 30; step++)
        {
            reports.Add((Second + 200 * Millisecond + step * 100 * Millisecond, Report(Pointer(
                0, 2, step == 0 ? AndroidTouchRecords.DownAction : AndroidTouchRecords.MoveAction, 500, 100 + step))));
        }

        var touches = FromIndex(reports, out _);

        var early = Assert.Single(touches.At(Second + 500 * Millisecond)!.Fingers);
        Assert.Equal([100.0, 101.0, 102.0, 103.0], early.Path.Select(point => point.Y));

        var late = Assert.Single(touches.At(4 * Second)!.Fingers);
        Assert.Equal(128, late.Y);
        Assert.Equal(11, late.Path.Count);
        Assert.Equal(3 * Second, late.Path[0].Time);
        Assert.Equal(118, late.Path[0].Y);
    }

    [Fact]
    public void ScaledTo_ScalesFromTheReportsScreenToThePicture()
    {
        var touches = FromIndex(
        [
            (Second, Report(Pointer(0, 1, AndroidTouchRecords.DownAction, 540, 1202))),
            (2 * Second, Sideways(Pointer(0, 2, AndroidTouchRecords.DownAction, 2000, 100)))
        ], out _);

        var upright = Assert.Single(touches.At(Second)!.ScaledTo(540, 1202));
        Assert.Equal((270.0, 601.0), (upright.X, upright.Y));
        Assert.Equal((270.0, 601.0), (upright.Path[0].X, upright.Path[0].Y));

        // Sideways, the report's size is sideways too, as the frame's is.
        var turned = touches.At(2 * Second)!;
        Assert.Equal((90, 2404, 1080), (turned.Rotation, turned.ScreenWidth, turned.ScreenHeight));
        var sideways = Assert.Single(turned.ScaledTo(2404, 1080));
        Assert.Equal((2000.0, 100.0), (sideways.X, sideways.Y));
    }

    [Fact]
    public void Archive_FromWholeRecords_ReadsTheSameTouches()
    {
        var builder = new SessionPlaybackArchiveBuilder(_folder, retainEvents: true);
        var sequence = 0L;
        foreach (var (time, report) in TapsAndSwipe())
        {
            sequence++;
            var record = RecorderEventFactory.Create(
                "s", Collector, AndroidTouchRecords.TouchChannel, (ulong)sequence, time, AndroidTouchRecords.TouchEventType, report);
            builder.AddEvent(sequence, record.EventId, record.EvidenceClass, record.Channel, record.EventType, time, record.Payload);
        }

        var started = DateTimeOffset.UtcNow;
        var archive = builder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(5), 5 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));

        Assert.Equal(16, archive.PhoneTouches.Count);
        Assert.Equal(2, archive.PhoneTouches.At(4 * Second + 100 * Millisecond)!.Fingers.Count);
        Assert.False(PhoneTouchTimeline.Empty.Recorded);
    }
}
