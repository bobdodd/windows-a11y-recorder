using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The assistive technology records (system.assistive-technology): finding
/// periods of sound from meter samples, matching NVDA's processes and copy,
/// the payload rules, and the properties panel's Screen reader rows. See
/// docs/architecture/screen-reader-activity.md, "Tracking NVDA".
/// </summary>
public sealed class AssistiveTechnologyTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "windows.assistive-technology", "AssistiveTechnologyCollector", "1", [AssistiveTechnologyRecords.Channel], "test");

    private static SoundPeriodTracker Tracker() =>
        new(AssistiveTechnologyRecords.SoundThreshold, AssistiveTechnologyRecords.SoundGapMilliseconds * Millisecond);

    [Fact]
    public void ASoundPeriodStartsAtTheFirstLoudSampleAndEndsAfterTheGap()
    {
        var tracker = Tracker();
        Assert.Null(tracker.Observe(0, 0));
        Assert.Null(tracker.Observe(20 * Millisecond, 0.0005));

        var started = tracker.Observe(40 * Millisecond, 0.2);
        Assert.NotNull(started);
        Assert.True(started.Started);
        Assert.Equal(40 * Millisecond, started.At);
        Assert.True(tracker.Sounding);

        // A pause between words shorter than the gap does not end it.
        Assert.Null(tracker.Observe(60 * Millisecond, 0.4));
        Assert.Null(tracker.Observe(200 * Millisecond, 0));
        Assert.Null(tracker.Observe(300 * Millisecond, 0.1));
        Assert.Null(tracker.Observe(540 * Millisecond, 0));

        var ended = tracker.Observe(550 * Millisecond, 0);
        Assert.NotNull(ended);
        Assert.False(ended.Started);
        Assert.Equal(40 * Millisecond, ended.StartedAt);
        Assert.Equal(300 * Millisecond, ended.LastSoundAt);
        Assert.Equal(0.4, ended.Peak);
        Assert.Equal("silence", ended.EndedBy);
        Assert.False(tracker.Sounding);
    }

    [Fact]
    public void APeriodInProgressEndsWhenTheProcessExitsOrTheRecordingStops()
    {
        var tracker = Tracker();
        Assert.Null(tracker.End(Second, "stop"));
        tracker.Observe(Second, 0.3);
        var ended = tracker.End(Second + 10 * Millisecond, "process-exited");
        Assert.NotNull(ended);
        Assert.Equal(Second, ended.StartedAt);
        Assert.Equal(Second, ended.LastSoundAt);
        Assert.Equal("process-exited", ended.EndedBy);
        Assert.Null(tracker.End(2 * Second, "stop"));
    }

    [Fact]
    public void NvdaIsMatchedByItsExecutableAndModuleAndItsHelpersByFolder()
    {
        Assert.Same(AssistiveTechnologyRecords.Nvda, AssistiveTechnologyRecords.ByExecutable("NVDA.EXE"));
        Assert.Null(AssistiveTechnologyRecords.ByExecutable("nvda_slave.exe"));
        Assert.Same(AssistiveTechnologyRecords.Nvda, AssistiveTechnologyRecords.ByModule("nvdaHelperRemote.dll"));
        Assert.Null(AssistiveTechnologyRecords.ByModule("uiautomationcore.dll"));

        const string main = @"C:\Users\User\Desktop\NVDA\nvda.exe";
        Assert.True(AssistiveTechnologyRecords.IsInFolderOf(@"C:\Users\User\Desktop\NVDA\nvda_slave.exe", main));
        Assert.True(AssistiveTechnologyRecords.IsInFolderOf(@"c:\users\user\desktop\nvda\lib\x86\nvdaHelperRemoteLoader.exe", main));
        Assert.False(AssistiveTechnologyRecords.IsInFolderOf(@"C:\Users\User\Desktop\NVDA2\nvda_slave.exe", main));
        Assert.False(AssistiveTechnologyRecords.IsInFolderOf(@"C:\Windows\explorer.exe", main));
        Assert.False(AssistiveTechnologyRecords.IsInFolderOf(@"C:\x.exe", "nvda.exe"));
    }

    [Fact]
    public void TheCopyIsInstalledOnlyWhenTheUninstallKeyNamesItsFolder()
    {
        const string installed = @"C:\Program Files\NVDA";
        Assert.Equal("installed", AssistiveTechnologyRecords.CopyOf(@"C:\Program Files\NVDA\nvda.exe", installed));
        Assert.Equal("installed", AssistiveTechnologyRecords.CopyOf(@"c:\program files\nvda\nvda.exe", installed + "\\"));
        Assert.Equal("portable", AssistiveTechnologyRecords.CopyOf(@"C:\Users\User\Desktop\NVDA\nvda.exe", installed));
        Assert.Equal("portable", AssistiveTechnologyRecords.CopyOf(@"C:\Users\User\Desktop\NVDA\nvda.exe", null));
        Assert.Equal("unknown", AssistiveTechnologyRecords.CopyOf("nvda.exe", installed));
    }

    [Fact]
    public void ThePayloadRulesRejectInconsistentRecords()
    {
        static List<EventValidationIssue> Validate(string eventType, string json, long time = 2000)
        {
            var issues = new List<EventValidationIssue>();
            using var document = JsonDocument.Parse(json.Replace('\'', '"'));
            EventPayloadValidator.Validate(AssistiveTechnologyRecords.Channel, eventType, document.RootElement, time, issues);
            return issues;
        }

        Assert.Contains(
            Validate(
                AssistiveTechnologyRecords.ProcessStartedEventType,
                "{'product':'NVDA','role':'helper','basis':'known-executable','executablePath':'C:\\\\a.exe','fileVersion':null,'productVersion':null,'processId':1,'parentProcessId':null,'startedUtc':null,'runningAtStart':false,'copy':'portable','problem':null}"),
            issue => issue.Code == "assistive-technology-process-basis-inconsistent");
        Assert.Contains(
            Validate(
                AssistiveTechnologyRecords.ProcessStartedEventType,
                "{'product':'NVDA','role':'screen-reader','basis':'known-executable','executablePath':null,'fileVersion':null,'productVersion':null,'processId':1,'parentProcessId':null,'startedUtc':null,'runningAtStart':false,'copy':'unknown','problem':null}"),
            issue => issue.Code == "assistive-technology-process-problem-inconsistent");
        Assert.Contains(
            Validate(
                AssistiveTechnologyRecords.ModuleLoadedEventType,
                "{'product':'NVDA','moduleName':'nvdahelperremote.dll','modulePath':'C:\\\\n.dll','fileVersion':null,'hostProcessId':4,'hostExecutablePath':'C:\\\\chrome.exe','hostExited':true}"),
            issue => issue.Code == "assistive-technology-module-host-exited");
        Assert.Contains(
            Validate(
                AssistiveTechnologyRecords.SoundEndedEventType,
                "{'product':'NVDA','processId':1,'startedAt':1500,'lastSoundAt':1000,'maxPeak':0.1,'endedBy':'silence'}"),
            issue => issue.Code == "assistive-technology-sound-times-inconsistent");
        Assert.Contains(
            Validate(
                AssistiveTechnologyRecords.SoundEndedEventType,
                "{'product':'NVDA','processId':1,'startedAt':1000,'lastSoundAt':3000,'maxPeak':0.1,'endedBy':'silence'}"),
            issue => issue.Code == "assistive-technology-sound-times-inconsistent");
        Assert.NotEmpty(
            Validate(
                AssistiveTechnologyRecords.SoundEndedEventType,
                "{'product':'NVDA','processId':1,'startedAt':1000,'lastSoundAt':1000,'maxPeak':0.1,'endedBy':'speech'}"));
    }

    // A recording with a browser: NVDA running from before the start, its
    // module seen in the browser at 2 s, a sound from 3 s to 3.5 s and
    // another from 6 s to 6.2 s, its exit at 8 s, and a new copy at 9 s.
    private static List<AssistiveTechnologyRecord> Records(string? soundProblem = null) =>
    [
        Record(0, AssistiveTechnologyRecords.WatchEventType,
            "{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'processPollMilliseconds':250,'modulePollMilliseconds':1000,'soundSampleMilliseconds':20,'soundThreshold':0.001,'soundGapMilliseconds':250,'browserExecutablePath':'C:\\\\b\\\\chrome.exe','soundProblem':" +
            (soundProblem is null ? "null" : $"'{soundProblem}'") + "}"),
        Record(10 * Millisecond, AssistiveTechnologyRecords.ProcessStartedEventType,
            "{'product':'NVDA','role':'screen-reader','basis':'known-executable','executablePath':'C:\\\\NVDA\\\\nvda.exe','fileVersion':'2026.2.0.1','productVersion':'2026.2','processId':5120,'parentProcessId':null,'startedUtc':'2026-10-10T17:59:12+00:00','runningAtStart':true,'copy':'portable','problem':null}"),
        Record(20 * Millisecond, AssistiveTechnologyRecords.ProcessStartedEventType,
            "{'product':'NVDA','role':'helper','basis':'child-in-folder','executablePath':'C:\\\\NVDA\\\\nvda_slave.exe','fileVersion':null,'productVersion':null,'processId':5200,'parentProcessId':5120,'startedUtc':null,'runningAtStart':true,'copy':'portable','problem':null}"),
        Record(2 * Second, AssistiveTechnologyRecords.ModuleLoadedEventType,
            "{'product':'NVDA','moduleName':'nvdahelperremote.dll','modulePath':'C:\\\\NVDA\\\\nvdaHelperRemote.dll','fileVersion':null,'hostProcessId':4000,'hostExecutablePath':'C:\\\\b\\\\chrome.exe','hostExited':false}"),
        Record(3 * Second, AssistiveTechnologyRecords.SoundStartedEventType, "{'product':'NVDA','processId':5120,'peak':0.2}"),
        Record(3750 * Millisecond, AssistiveTechnologyRecords.SoundEndedEventType,
            $"{{'product':'NVDA','processId':5120,'startedAt':{3 * Second},'lastSoundAt':{3500 * Millisecond},'maxPeak':0.3,'endedBy':'silence'}}"),
        Record(6 * Second, AssistiveTechnologyRecords.SoundStartedEventType, "{'product':'NVDA','processId':5120,'peak':0.2}"),
        Record(6450 * Millisecond, AssistiveTechnologyRecords.SoundEndedEventType,
            $"{{'product':'NVDA','processId':5120,'startedAt':{6 * Second},'lastSoundAt':{6200 * Millisecond},'maxPeak':0.3,'endedBy':'silence'}}"),
        Record(8 * Second, AssistiveTechnologyRecords.ModuleUnloadedEventType,
            "{'product':'NVDA','moduleName':'nvdahelperremote.dll','modulePath':'C:\\\\NVDA\\\\nvdaHelperRemote.dll','fileVersion':null,'hostProcessId':4000,'hostExecutablePath':'C:\\\\b\\\\chrome.exe','hostExited':false}"),
        Record(8 * Second, AssistiveTechnologyRecords.ProcessExitedEventType,
            "{'product':'NVDA','role':'screen-reader','processId':5120,'startedUtc':null,'exitedUtc':null,'exitCode':0}"),
        Record(9 * Second, AssistiveTechnologyRecords.ProcessStartedEventType,
            "{'product':'NVDA','role':'screen-reader','basis':'known-executable','executablePath':'C:\\\\Program Files\\\\NVDA\\\\nvda.exe','fileVersion':null,'productVersion':'2026.3','processId':6100,'parentProcessId':null,'startedUtc':null,'runningAtStart':false,'copy':'installed','problem':null}")
    ];

    private static AssistiveTechnologyRecord Record(long time, string eventType, string json) =>
        new(time, eventType, JsonDocument.Parse(json.Replace('\'', '"')).RootElement.Clone());

    private static PropertyRow RowAt(AssistiveTechnologyTimeline timeline, long time, string key) =>
        Assert.Single(timeline.RowsAt(time), row => row.Key == key);

    [Fact]
    public void EverySampleRecordIsValid()
    {
        foreach (var record in Records())
        {
            var issues = new List<EventValidationIssue>();
            EventPayloadValidator.Validate(
                AssistiveTechnologyRecords.Channel, record.EventType, record.Payload, record.MonotonicNanoseconds, issues);
            Assert.True(issues.Count == 0, $"{record.EventType}: {string.Join("; ", issues.Select(issue => issue.Message))}");
        }
    }

    [Fact]
    public void TheScreenReaderRowsShowRunningBrowserAndSound()
    {
        var timeline = new AssistiveTechnologyTimeline(Records());
        Assert.True(timeline.Recorded);
        var running = AssistiveTechnologyTimeline.RunningKey("NVDA");
        var browser = AssistiveTechnologyTimeline.BrowserKey("NVDA");
        var sound = AssistiveTechnologyTimeline.SoundKey("NVDA");

        var atOne = RowAt(timeline, Second, running);
        Assert.Equal(AssistiveTechnologyTimeline.Group, atOne.Group);
        Assert.Equal("NVDA", atOne.Setting);
        Assert.Equal(
            "running, version 2026.2, portable copy, started before the recording, at 2026-10-10 17:59:12 UTC, process 5120",
            atOne.Value);
        Assert.Equal("at start", atOne.WhenSet);
        Assert.Equal("its module not loaded", RowAt(timeline, Second, browser).Value);
        Assert.Equal("silent", RowAt(timeline, Second, sound).Value);

        Assert.Equal("its module loaded in 1 browser process", RowAt(timeline, 2500 * Millisecond, browser).Value);
        Assert.True(RowAt(timeline, 2500 * Millisecond, browser).Changed);

        var speaking = RowAt(timeline, 3200 * Millisecond, sound);
        Assert.Equal("playing sound", speaking.Value);
        Assert.Equal(3 * Second, speaking.SetAt);

        // Silent from the last sound, not from the record after the gap.
        var after = RowAt(timeline, 3600 * Millisecond, sound);
        Assert.Equal("silent", after.Value);
        Assert.Equal(3500 * Millisecond, after.SetAt);

        Assert.Equal("not running", RowAt(timeline, 8500 * Millisecond, running).Value);
        Assert.Equal(8 * Second, RowAt(timeline, 8500 * Millisecond, running).SetAt);
        Assert.Equal("its module not loaded", RowAt(timeline, 8500 * Millisecond, browser).Value);
        Assert.Equal("running, version 2026.3, installed copy, process 6100", RowAt(timeline, 9 * Second, running).Value);
    }

    [Fact]
    public void TheRowsStepThroughTheirOwnChanges()
    {
        var timeline = new AssistiveTechnologyTimeline(Records());
        var empty = WindowsPreferenceTimeline.Empty;
        PropertyRow Row(string key) => RowAt(timeline, 0, key);
        IReadOnlyList<long>? Times(string key) =>
            PropertyChangeSteps.TimesOf(Row(key), empty, MagnifierChangeTimeline.Empty, null, timeline);

        // The copy running at the start is not a change; its exit and the
        // new copy are.
        Assert.Equal([8 * Second, 9 * Second], Times(AssistiveTechnologyTimeline.RunningKey("NVDA")));
        Assert.Equal([2 * Second, 8 * Second], Times(AssistiveTechnologyTimeline.BrowserKey("NVDA")));
        Assert.Equal([3 * Second, 6 * Second], Times(AssistiveTechnologyTimeline.SoundKey("NVDA")));
        Assert.Null(timeline.ChangeTimesOf("assistive-technology.Other.sound"));
    }

    [Fact]
    public void ARecordingWithoutTheRecordsOrTheMetersSaysSo()
    {
        var empty = AssistiveTechnologyTimeline.Empty;
        Assert.False(empty.Recorded);
        Assert.All(empty.RowsAt(Second), row => Assert.Equal("not recorded", row.Value));
        Assert.Null(PropertyChangeSteps.TimesOf(
            empty.RowsAt(0)[0], WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty, null, empty));

        var unmeasured = new AssistiveTechnologyTimeline(Records("The audio devices could not be listed"));
        Assert.Equal(
            "not measured: The audio devices could not be listed",
            RowAt(unmeasured, Second, AssistiveTechnologyTimeline.SoundKey("NVDA")).Value);
    }

    [Fact]
    public void TheArchiveKeepsTheRecordsAndSummarizesThem()
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        ulong sequence = 0;
        foreach (var record in Records())
        {
            sequence++;
            builder.Add((long)sequence, RecorderEventFactory.Create(
                "s", Collector, AssistiveTechnologyRecords.Channel, sequence, record.MonotonicNanoseconds,
                record.EventType, record.Payload) with { EventId = "e" + sequence });
        }

        var index = builder.Build();
        Assert.True(PlaybackIndex.CurrentVersion >= 9);
        Assert.Equal(Records().Count, index.Events.Count(item => item.Channel == AssistiveTechnologyRecords.Channel));

        var archiveBuilder = new SessionPlaybackArchiveBuilder(Path.GetTempPath(), retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(10), 10 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 1, 0, null));
        Assert.True(archive.AssistiveTechnology.Recorded);
        Assert.Equal("playing sound", RowAt(archive.AssistiveTechnology, 3200 * Millisecond, AssistiveTechnologyTimeline.SoundKey("NVDA")).Value);
        Assert.True(SessionPlaybackArchiveBuilder.BuildsFrom(AssistiveTechnologyRecords.Channel));
        Assert.True(SessionPlaybackArchiveBuilder.ReadsPayload(AssistiveTechnologyRecords.Channel));

        string Summary(int index) =>
            SessionPlaybackArchiveBuilder.CreateSummary(AssistiveTechnologyRecords.Channel, Records()[index].EventType, Records()[index].Payload);
        Assert.Equal("assistive-technology-watch: watching NVDA", Summary(0));
        Assert.Equal(
            "assistive-technology-process-started: NVDA, running at the start, 2026.2, portable copy, process 5120",
            Summary(1));
        Assert.Equal("assistive-technology-process-started: NVDA, helper running at the start, portable copy, process 5200", Summary(2));
        Assert.Equal("assistive-technology-module-loaded: NVDA, nvdahelperremote.dll seen in browser process 4000", Summary(3));
        Assert.Equal("assistive-technology-sound-ended: NVDA, sound ended, 500 ms of sound, silence", Summary(5));
        Assert.Equal("assistive-technology-process-exited: NVDA, exited, process 5120", Summary(9));
    }
}
