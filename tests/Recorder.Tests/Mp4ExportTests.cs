using System.Text.Json;
using NAudio.Wave;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.Export;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// The MP4 export's timing and sound, which do not need Media Foundation:
/// where each stream's sound is placed, when each frame is shown, how the
/// tracks are mixed, and the buffer records read back from a recording
/// file. See docs/architecture/mp4-export.md.
/// </summary>
public sealed class Mp4ExportTests : IDisposable
{
    private const int Rate = 48_000;
    private const int Block = 4;
    private const long Millisecond = 1_000_000;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mp4-export-" + Guid.NewGuid().ToString("N"));

    public Mp4ExportTests() => Directory.CreateDirectory(_directory);

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

    // A 10 ms buffer of 480 frames whose callback came at the time given.
    private static SessionAudioBuffer Buffer(long sequence, long callbackMilliseconds, int frames = 480) =>
        new(sequence, sequence * 480 * Block, frames * Block, frames, frames * 1_000_000_000L / Rate,
            callbackMilliseconds * Millisecond);

    [Fact]
    public void PlacesAStreamWithoutBufferRecordsAsOneRunFromItsStart()
    {
        var plan = AudioPlacement.Plan([], Rate, Block, 96_000, 500 * Millisecond);

        Assert.False(plan.FromBufferRecords);
        Assert.Equal([new PlacedAudioRun(24_000, 0, 96_000)], plan.Runs);
        Assert.Empty(AudioPlacement.Plan([], Rate, Block, 0, 0).Runs);
    }

    [Fact]
    public void MergesBuffersThatFollowOnInOneRun()
    {
        // Callbacks 10 ms apart, the first buffer heard from 100 ms.
        var plan = AudioPlacement.Plan([Buffer(0, 110), Buffer(1, 120), Buffer(2, 131)], Rate, Block, 1_440, 0);

        Assert.True(plan.FromBufferRecords);
        Assert.Equal([new PlacedAudioRun(4_800, 0, 1_440)], plan.Runs);
        Assert.Equal(0, plan.SilencesInserted);
        Assert.Equal(0, plan.FramesSkipped);
    }

    [Fact]
    public void InsertsSilenceWhereNothingWasRecorded()
    {
        // System sound delivers nothing while nothing plays: the third
        // buffer was heard a second after the second ended.
        var plan = AudioPlacement.Plan([Buffer(0, 10), Buffer(1, 20), Buffer(2, 1_030)], Rate, Block, 1_440, 0);

        Assert.Equal([new PlacedAudioRun(0, 0, 960), new PlacedAudioRun(48_960, 960, 480)], plan.Runs);
        Assert.Equal(1, plan.SilencesInserted);
        Assert.Equal(48_000, plan.SilenceFrames);
        Assert.Equal(Rate, plan.SampleRate);
    }

    [Fact]
    public void LeavesOutDataThatRanAheadOfItsRecordedTimes()
    {
        // A 10 ms buffer heard from 990 ms, a 200 ms one from 1 s, and a
        // 200 ms one whose record puts it at 1.1 s, 100 ms before the
        // second ends: its first 100 ms overlap and are left out.
        SessionAudioBuffer[] buffers =
        [
            new(0, 0, 480 * Block, 480, 10 * Millisecond, 1_000 * Millisecond),
            new(1, 480 * Block, 9_600 * Block, 9_600, 200 * Millisecond, 1_200 * Millisecond),
            new(2, 10_080 * Block, 9_600 * Block, 9_600, 200 * Millisecond, 1_300 * Millisecond)
        ];
        var plan = AudioPlacement.Plan(buffers, Rate, Block, 19_680, 0);

        Assert.Equal([new PlacedAudioRun(47_520, 0, 10_080), new PlacedAudioRun(57_600, 14_880, 4_800)], plan.Runs);
        Assert.Equal(4_800, plan.FramesSkipped);
        Assert.Equal(0, plan.SilencesInserted);
    }

    [Fact]
    public void IgnoresRecordsBeyondTheDataTheFileHolds()
    {
        var plan = AudioPlacement.Plan([Buffer(0, 10), Buffer(1, 20), Buffer(2, 30)], Rate, Block, 700, 0);

        Assert.Equal([new PlacedAudioRun(0, 0, 700)], plan.Runs);
    }

    private static SessionVideoFrame Frame(long milliseconds, int width = 1920, int height = 1080) =>
        new(milliseconds * Millisecond, $"frames/desktop/{milliseconds}.png", $"/x/{milliseconds}.png", width, height);

    // The schedule as runs of one picture: what, from when, for how long.
    private static string[] Runs(IReadOnlyList<ScheduledFrame> schedule)
    {
        var runs = new List<(string What, long Start, long Length)>();
        foreach (var sample in schedule)
        {
            var what = sample.Frame?.Path ?? "black";
            if (sample.Repeat)
            {
                Assert.Equal(runs[^1].What, what);
                runs[^1] = runs[^1] with { Length = runs[^1].Length + sample.DurationNanoseconds };
            }
            else
            {
                runs.Add((what, sample.StartNanoseconds, sample.DurationNanoseconds));
            }
        }

        return [.. runs.Select(run => $"{run.What} {run.Start / Millisecond} {run.Length / Millisecond}")];
    }

    [Fact]
    public void ShowsBlackBeforeTheFirstFrameAndEachFrameUntilTheNext()
    {
        var schedule = VideoSchedule.Plan([Frame(300), Frame(500), Frame(700)], 900 * Millisecond, 20);

        Assert.Equal(
            ["black 0 300", "frames/desktop/300.png 300 200", "frames/desktop/500.png 500 200", "frames/desktop/700.png 700 200"],
            Runs(schedule));
    }

    [Fact]
    public void GivesEverySampleTheSameLengthAtTheExportsFrameRate()
    {
        // Windows' encoder times samples by the frame rate, so a frame
        // shown for 2.5 s is 50 samples of 50 ms, not one long sample.
        var schedule = VideoSchedule.Plan([Frame(0), Frame(200)], 2_700 * Millisecond, 20);

        Assert.Equal(54, schedule.Count);
        Assert.All(schedule, sample => Assert.Equal(50 * Millisecond, sample.DurationNanoseconds));
        Assert.Equal(Enumerable.Range(0, 54).Select(index => index * 50 * Millisecond), schedule.Select(sample => sample.StartNanoseconds));
        Assert.Equal(["frames/desktop/0.png 0 200", "frames/desktop/200.png 200 2500"], Runs(schedule));
    }

    [Fact]
    public void ShowsAFrameFromTheSampleNearestItsTime()
    {
        // 310 ms is nearer the sample at 300 ms; 330 ms nearer 350 ms.
        Assert.Equal(
            ["black 0 300", "frames/desktop/310.png 300 400"],
            Runs(VideoSchedule.Plan([Frame(310)], 700 * Millisecond, 20)));
        Assert.Equal(
            ["black 0 350", "frames/desktop/330.png 350 350"],
            Runs(VideoSchedule.Plan([Frame(330)], 700 * Millisecond, 20)));
    }

    [Fact]
    public void ShowsTheLastRecordedOfFramesAtTheSameTime()
    {
        var first = Frame(0);
        var second = first with { Path = "frames/desktop/again.png" };
        var schedule = VideoSchedule.Plan([first, second, Frame(200)], 400 * Millisecond, 20);

        Assert.Equal(["frames/desktop/again.png 0 200", "frames/desktop/200.png 200 200"], Runs(schedule));
    }

    [Fact]
    public void ShowsTheLastFrameForOneIntervalWhenTheRecordingEndsWithIt()
    {
        var schedule = VideoSchedule.Plan([Frame(0), Frame(200)], 200 * Millisecond, 20);

        Assert.Equal(["frames/desktop/0.png 0 200", "frames/desktop/200.png 200 200"], Runs(schedule));
    }

    [Fact]
    public void SizesTheExportToTheLargestFrameWithinWhatWindowsPlays()
    {
        // Five recorded frames a second are shown at 20 a second.
        Assert.Equal(new VideoFormat(1920, 1080, 20, 5), VideoSchedule.FormatOf([Frame(0, 1280, 720), Frame(200), Frame(400)]));
        Assert.Equal(21, VideoSchedule.FormatOf([Frame(0), Frame(143), Frame(286)]).FramesPerSecond);
        Assert.Equal(new VideoFormat(1920, 1080, 30, 30), VideoSchedule.FormatOf([Frame(0), Frame(33), Frame(67), Frame(100)]));
        // 7680 by 2160, two 4K screens side by side, is made smaller to
        // 4096 wide with its shape kept.
        Assert.Equal((4096, 1152), VideoSchedule.Fit(7680, 2160, 4096, 2304));
        Assert.Equal((1366, 768), VideoSchedule.Fit(1366, 768, 4096, 2304));
        Assert.Equal((1024, 766), VideoSchedule.Fit(1025, 767, 4096, 2304));
    }

    [Fact]
    public void SizesAPhoneExportForBothShapesAtSixtyFramesASecond()
    {
        // Upright only: 1080 by 2404 made smaller to 2304 high, its shape
        // kept, and even.
        Assert.Equal(
            new VideoFormat(1034, 2304, 60, 60),
            VideoSchedule.FormatOf([Frame(0, 1080, 2404), Frame(8, 1080, 2404), Frame(900, 1080, 2404)], SessionFrameSource.Phone));
        // Turned during the recording: wide enough for the wide pictures and
        // tall enough for the upright ones, made smaller to fit.
        Assert.Equal(
            new VideoFormat(2304, 2304, 60, 60),
            VideoSchedule.FormatOf([Frame(0, 1080, 2404), Frame(100, 2404, 1080)], SessionFrameSource.Phone));
        // The desktop's rule is unchanged: the largest frame, at a rate from
        // the frames' spacing.
        Assert.Equal(
            VideoSchedule.FormatOf([Frame(0, 1280, 720), Frame(200), Frame(400)]),
            VideoSchedule.FormatOf([Frame(0, 1280, 720), Frame(200), Frame(400)], SessionFrameSource.Desktop));
        // A wide picture in the square export is drawn its full width, centred.
        Assert.Equal((0d, 634.5d, 2304d, 1035.1d), Round(VideoSchedule.Placement(2404, 1080, 2304, 2304)));
    }

    private static (double, double, double, double) Round((double X, double Y, double Width, double Height) value) =>
        (Math.Round(value.X, 1), Math.Round(value.Y, 1), Math.Round(value.Width, 1), Math.Round(value.Height, 1));

    [Fact]
    public void SetsTheBitRateFromTheRecordedFramesNotTheExportsRate()
    {
        Assert.Equal(2_073_600u, Mp4Writer.BitrateOf(new VideoFormat(1920, 1080, 20, 5)));
        Assert.Equal(1_000_000u, Mp4Writer.BitrateOf(new VideoFormat(640, 480, 20, 1)));
        Assert.Equal(20_000_000u, Mp4Writer.BitrateOf(new VideoFormat(4096, 2304, 30, 30)));
    }

    [Fact]
    public void CentresAPictureOfAnotherShapeWithBlackAround()
    {
        Assert.Equal((240d, 0d, 1440d, 1080d), VideoSchedule.Placement(1024, 768, 1920, 1080));
        Assert.Equal((0d, 0d, 1920d, 1080d), VideoSchedule.Placement(3840, 2160, 1920, 1080));
    }

    private static byte[] Box(string type, params byte[][] content)
    {
        var body = content.SelectMany(part => part).ToArray();
        var box = new byte[8 + body.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }

    // A track of a handler type whose header has the version and flags given.
    private static byte[] Track(string handler, byte version, uint flags)
    {
        var header = new byte[version == 1 ? 92 : 80];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, ((uint)version << 24) | flags);
        var handlerBody = new byte[25];
        System.Text.Encoding.ASCII.GetBytes(handler).CopyTo(handlerBody, 8);
        return Box("trak", Box("tkhd", header), Box("mdia", Box("mdhd", new byte[24]), Box("hdlr", handlerBody)));
    }

    private static (uint Flags, short Group) HeaderOf(byte[] movie, int track)
    {
        var at = 8;
        for (var index = 0; index < track; index++)
        {
            at += System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(movie.AsSpan(at));
        }

        // The track's first child is its header.
        var header = movie.AsSpan(at + 8);
        var version = header[8];
        return (System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[8..]) & 0xFFFFFF,
            System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(header[(8 + (version == 1 ? 46 : 34))..]));
    }

    [Fact]
    public void MarksTheSoundTracksAsAlternativesWithOnlyTheMixedOneEnabled()
    {
        // As Windows writes them: every track enabled, no groups.
        var movie = Box("moov", Box("mvhd", new byte[100]), Track("vide", 0, 0x7), Track("soun", 0, 0x7),
            Track("soun", 1, 0x7), Track("soun", 0, 0x3));

        Assert.Equal(3, Mp4TrackGroups.Mark(movie));

        Assert.Equal((0x7u, (short)0), HeaderOf(movie, 1));
        Assert.Equal((0x7u, (short)1), HeaderOf(movie, 2));
        Assert.Equal((0x6u, (short)1), HeaderOf(movie, 3));
        Assert.Equal((0x2u, (short)1), HeaderOf(movie, 4));
    }

    [Fact]
    public void ReadsABufferRecordAndRejectsOneWithoutData()
    {
        var buffer = SessionAudioBuffer.FromPayload(Json(EvidenceSamples.Sample("audio.screen-reader", "audio-buffer")));
        Assert.Equal(new SessionAudioBuffer(4, 7_680, 1_920, 480, 10_000_000, 51_000_000), buffer);

        Assert.Null(SessionAudioBuffer.FromPayload(Json(
            "{\"bufferSequence\":0,\"dataByteOffset\":0,\"byteLength\":0,\"sampleFrames\":0," +
            "\"durationNanoseconds\":0,\"callbackMonotonicNanoseconds\":0}")));
        Assert.Null(SessionAudioBuffer.FromPayload(Json("{\"bufferSequence\":0}")));
    }

    private string Wave(string name, WaveFormat format, float value, int frames)
    {
        var path = Path.Combine(_directory, name);
        using var writer = new WaveFileWriter(path, format);
        var samples = Enumerable.Repeat(value, frames * format.Channels).ToArray();
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }

    private static float At(byte[] track, int frame, int channel) =>
        BitConverter.ToInt16(track, ((frame * 2) + channel) * 2) / 32767f;

    [Fact]
    public void MixesEachStreamAtItsLevelAndKeepsEachOwnTrackAtFullLevel()
    {
        // A mono 48 kHz microphone at 0.5 for 100 ms from the start, and a
        // stereo system stream at 0.25 for 100 ms from 50 ms.
        var microphone = Wave("microphone.wav", new WaveFormat(Rate, 16, 1), 0.5f, 4_800);
        var system = Wave("system.wav", new WaveFormat(Rate, 16, 2), 0.25f, 4_800);
        using var mixer = new ExportAudioMixer(
        [
            new ExportAudioSource("microphone", microphone, 0, [], -6.0206),
            new ExportAudioSource("system", system, 50 * Millisecond, [], 0)
        ]);
        Assert.Equal(3, mixer.TrackCount);

        var tracks = Enumerable.Range(0, 3).Select(_ => new byte[9_600 * Block]).ToArray();
        mixer.Read(9_600, tracks);

        // At 25 ms only the microphone: half its 0.5 in the mix, the full
        // 0.5 on both sides of its own track, and silence in the system's.
        Assert.Equal(0.25, At(tracks[0], 1_200, 0), 2);
        Assert.Equal(0.5, At(tracks[1], 1_200, 0), 2);
        Assert.Equal(0.5, At(tracks[1], 1_200, 1), 2);
        Assert.Equal(0, At(tracks[2], 1_200, 0), 3);
        // At 75 ms both; at 125 ms only the system.
        Assert.Equal(0.5, At(tracks[0], 3_600, 1), 2);
        Assert.Equal(0.25, At(tracks[0], 6_000, 0), 2);
        Assert.Equal(0, At(tracks[1], 6_000, 0), 3);
        Assert.Equal(0.25, At(tracks[2], 6_000, 1), 2);
    }

    [Fact]
    public void ResamplesToFortyEightKilohertzAndPlacesSoundAfterSilence()
    {
        // A 44.1 kHz stream with two 100 ms buffers recorded a second apart.
        var path = Wave("system.wav", new WaveFormat(44_100, 16, 2), 0.5f, 8_820);
        SessionAudioBuffer[] buffers =
        [
            new(0, 0, 4_410 * Block, 4_410, 100 * Millisecond, 100 * Millisecond),
            new(1, 4_410 * Block, 4_410 * Block, 4_410, 100 * Millisecond, 1_200 * Millisecond)
        ];
        using var mixer = new ExportAudioMixer([new ExportAudioSource("system", path, 0, buffers, 0)]);
        Assert.Equal(1, mixer.Plans["system"].SilencesInserted);

        var tracks = Enumerable.Range(0, 2).Select(_ => new byte[Rate * 2 * Block]).ToArray();
        mixer.Read(Rate * 2, tracks);

        // Sound at 50 ms, silence at 500 ms, and sound again at 1.15 s.
        Assert.Equal(0.5, At(tracks[1], 2_400, 0), 2);
        Assert.Equal(0, At(tracks[1], 24_000, 0), 3);
        Assert.Equal(0.5, At(tracks[1], 55_200, 1), 2);
        Assert.Equal(0, At(tracks[1], 72_000, 1), 3);
    }

    [Fact]
    public async Task ReadsAStreamsBufferRecordsBackFromARecordingFile()
    {
        var collector = Collector("audio", "audio.system", "audio.screen-reader");
        var events = new List<RecorderEvent>();
        for (ulong sequence = 1; sequence <= 40; sequence++)
        {
            var channel = sequence % 2 == 0 ? "audio.screen-reader" : "audio.system";
            var stream = channel["audio.".Length..];
            var index = (sequence - 1) / 2;
            events.Add(Event("session-a", collector, sequence, (long)sequence * Millisecond, channel, "audio-buffer",
                Json($"{{\"stream\":\"{stream}\",\"path\":\"audio/{stream}.wav\",\"bufferSequence\":{index}," +
                    $"\"dataByteOffset\":{index * 1_920},\"byteLength\":1920,\"sampleFrames\":480," +
                    $"\"durationNanoseconds\":10000000,\"estimatedFirstSampleMonotonicNanoseconds\":0," +
                    $"\"callbackMonotonicNanoseconds\":{sequence * Millisecond}}}")));
        }

        var path = Path.Combine(_directory, "recording.mcap");
        using (var target = new RecordingFileBatchTarget(
                   path,
                   new Dictionary<string, string> { ["sessionKey"] = "session-a", ["clockFrequency"] = "10000000" },
                   new RecordingFileWriterOptions { ChunkBytes = 1_024 }))
        {
            var batch = events.Select((record, index) => new BufferedEvent(index, record, record.Payload.GetRawText())).ToArray();
            Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
            target.Finish();
        }

        using var timeline = new RecordingFileTimeline(
            RecordingFileReader.Open(path),
            new Dictionary<string, long>(StringComparer.Ordinal) { ["audio.system"] = 20, ["audio.screen-reader"] = 20 },
            new TimelineOccupancy(40 * Millisecond, new Dictionary<string, int[]>(StringComparer.Ordinal)));
        var read = timeline.ReadAudioBuffers("screen-reader", TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(0, 20).Select(index => (long)index), read.Select(item => item.BufferSequence));
        Assert.All(read, item => Assert.Equal(item.BufferSequence * 1_920, item.DataByteOffset));
        Assert.Empty(timeline.ReadAudioBuffers("microphone", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadsThePhonesSoundBackFromARecordingFileAsTheSystemSoundsPlace()
    {
        // Android slice 4b: the export reads the phone's packets, which also
        // keep the phone's time and the time they were read, as it reads a
        // desktop stream's, and places them from their session times.
        var collector = Collector("android-sound", Recorder.Contracts.AndroidSoundRecords.Channel);
        var events = new List<RecorderEvent>();
        for (ulong sequence = 1; sequence <= 10; sequence++)
        {
            var index = sequence - 1;
            events.Add(Event("session-a", collector, sequence, (long)sequence * 100 * Millisecond,
                Recorder.Contracts.AndroidSoundRecords.Channel, Recorder.Contracts.AndroidSoundRecords.BufferEventType,
                Json($"{{\"stream\":\"device\",\"path\":\"audio/device.wav\",\"bufferSequence\":{index}," +
                    $"\"dataByteOffset\":{index * 4_096},\"byteLength\":4096,\"sampleFrames\":1024," +
                    $"\"durationNanoseconds\":21333333,\"estimatedFirstSampleMonotonicNanoseconds\":0," +
                    $"\"callbackMonotonicNanoseconds\":{sequence * 100 * Millisecond}," +
                    $"\"presentationMicroseconds\":{sequence * 100_000},\"receivedAtNanoseconds\":{sequence * 100 * Millisecond + 15 * Millisecond}}}")));
        }

        var path = Path.Combine(_directory, "phone.mcap");
        using (var target = new RecordingFileBatchTarget(
                   path,
                   new Dictionary<string, string> { ["sessionKey"] = "session-a", ["clockFrequency"] = "10000000" },
                   new RecordingFileWriterOptions { ChunkBytes = 1_024 }))
        {
            var batch = events.Select((record, index) => new BufferedEvent(index, record, record.Payload.GetRawText())).ToArray();
            Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
            target.Finish();
        }

        using var timeline = new RecordingFileTimeline(
            RecordingFileReader.Open(path),
            new Dictionary<string, long>(StringComparer.Ordinal) { [Recorder.Contracts.AndroidSoundRecords.Channel] = 10 },
            new TimelineOccupancy(1_000 * Millisecond, new Dictionary<string, int[]>(StringComparer.Ordinal)));
        var read = timeline.ReadAudioBuffers(
            Recorder.Contracts.AndroidSoundRecords.StreamName,
            TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(0, 10).Select(index => (long)index), read.Select(item => item.BufferSequence));
        Assert.All(read, item => Assert.Equal((item.BufferSequence + 1) * 100 * Millisecond, item.CallbackMonotonicNanoseconds));

        var wave = Wave("device.wav", new WaveFormat(Rate, 16, 2), 0.5f, 10 * 1_024);
        using var mixer = new ExportAudioMixer(
            [new ExportAudioSource(Recorder.Contracts.AndroidSoundRecords.StreamName, wave, 0, read, 0)]);
        Assert.True(mixer.Plans.ContainsKey(Recorder.Contracts.AndroidSoundRecords.StreamName));
    }

    [Fact]
    public void AcceptsTheScreenReaderStreamsEvents()
    {
        foreach (var eventType in new[] { "audio-stream-started", "audio-stream-stopped", "audio-buffer", "audio-stream-error", "collector-omission" })
        {
            using var document = JsonDocument.Parse(EvidenceSamples.Sample("audio.screen-reader", eventType));
            var issues = new List<EventValidationIssue>();
            EventPayloadValidator.Validate("audio.screen-reader", eventType, document.RootElement, 1, issues);
            Assert.Empty(issues);
        }

        using var wrong = JsonDocument.Parse(EvidenceSamples.Sample("audio.screen-reader", "audio-buffer").Replace("screen-reader", "speech"));
        var wrongIssues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate("audio.screen-reader", "audio-buffer", wrong.RootElement, 1, wrongIssues);
        Assert.NotEmpty(wrongIssues);
    }
}
