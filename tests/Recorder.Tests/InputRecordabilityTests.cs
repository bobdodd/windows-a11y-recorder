using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The records of when keyboard and mouse input could not be recorded
/// (window.foreground: recorder-integrity, foreground-integrity,
/// input-desktop): the integrity levels, the payload rules, the periods the
/// player draws as a band, the panel's Keyboard and mouse row, and the
/// summaries. See docs/architecture/screen-reader-activity.md, "Input the
/// recorder cannot receive".
/// </summary>
public sealed class InputRecordabilityTests
{
    private const long Second = 1_000_000_000;

    private const string Recorder =
        "{'processId':4321,'integrityLevel':'medium','integrityRid':8192,'uiAccess':false,'problem':null}";

    private const string Browser =
        "{'windowHandle':1,'processId':10,'processName':'chrome','integrityLevel':'medium','integrityRid':8192,'uiAccess':false,'problem':null,'inputRecordable':true}";

    private const string Elevated =
        "{'windowHandle':2,'processId':22444,'processName':'powershell','integrityLevel':'high','integrityRid':12288,'uiAccess':false,'problem':null,'inputRecordable':false}";

    private const string Unreadable =
        "{'windowHandle':3,'processId':8,'processName':'secret','integrityLevel':null,'integrityRid':null,'uiAccess':null,'problem':'OpenProcessToken failed with error 5.','inputRecordable':null}";

    private const string UserDesktop =
        "{'reason':'start','desktopName':'Default','problem':null,'inputRecordable':true}";

    private const string SecureDesktop =
        "{'reason':'switch','desktopName':null,'problem':'OpenInputDesktop failed with error 5.','inputRecordable':false}";

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json.Replace('\'', '"'));
        return document.RootElement.Clone();
    }

    private static InputRecordabilityRecord R(double seconds, string eventType, string json) =>
        new((long)(seconds * Second), eventType, J(json));

    private const string RecorderType = InputRecordabilityRecords.RecorderIntegrityEventType;
    private const string ForegroundType = InputRecordabilityRecords.ForegroundIntegrityEventType;
    private const string DesktopType = InputRecordabilityRecords.InputDesktopEventType;

    [Theory]
    [InlineData(0x0000, "untrusted")]
    [InlineData(0x1000, "low")]
    [InlineData(0x2000, "medium")]
    [InlineData(0x2100, "medium-plus")]
    [InlineData(0x3000, "high")]
    [InlineData(0x4000, "system")]
    [InlineData(0x5000, "protected")]
    public void IntegrityLevelsHaveTheirNames(int rid, string name) =>
        Assert.Equal(name, InputRecordabilityRecords.IntegrityLevel(rid));

    [Fact]
    public void InputIsRecordableUpToTheRecordersOwnLevel()
    {
        Assert.Equal(true, InputRecordabilityRecords.InputRecordable(0x1000, 0x2000));
        Assert.Equal(true, InputRecordabilityRecords.InputRecordable(0x2000, 0x2000));
        Assert.Equal(false, InputRecordabilityRecords.InputRecordable(0x3000, 0x2000));
        Assert.Null(InputRecordabilityRecords.InputRecordable(null, 0x2000));
    }

    private static List<EventValidationIssue> Validate(string eventType, string json)
    {
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(InputRecordabilityRecords.Channel, eventType, J(json), 2000, issues);
        return issues;
    }

    [Fact]
    public void TheSampleRecordsAreValid()
    {
        Assert.Empty(Validate(RecorderType, Recorder));
        Assert.Empty(Validate(ForegroundType, Browser));
        Assert.Empty(Validate(ForegroundType, Elevated));
        Assert.Empty(Validate(ForegroundType, Unreadable));
        Assert.Empty(Validate(DesktopType, UserDesktop));
        Assert.Empty(Validate(DesktopType, SecureDesktop));
    }

    [Fact]
    public void AnIntegrityRecordHasALevelOrAProblem()
    {
        Assert.Contains(
            Validate(RecorderType, Recorder.Replace("'problem':null", "'problem':'x'")),
            issue => issue.Code == "integrity-problem-inconsistent");
        Assert.Contains(
            Validate(ForegroundType, Elevated.Replace("'integrityLevel':'high'", "'integrityLevel':'low'")),
            issue => issue.Code == "integrity-level-inconsistent");
        Assert.Contains(
            Validate(ForegroundType, Unreadable.Replace("'inputRecordable':null", "'inputRecordable':true")),
            issue => issue.Code == "integrity-recordable-inconsistent");
    }

    [Fact]
    public void AnInputDesktopIsRecordableExactlyWhenItIsTheUsers()
    {
        Assert.Contains(
            Validate(DesktopType, UserDesktop.Replace("'inputRecordable':true", "'inputRecordable':false")),
            issue => issue.Code == "input-desktop-recordable-inconsistent");
        Assert.Contains(
            Validate(DesktopType, SecureDesktop.Replace("'problem':'OpenInputDesktop failed with error 5.'", "'problem':null")),
            issue => issue.Code == "input-desktop-problem-inconsistent");
        Assert.NotEmpty(Validate(DesktopType, UserDesktop.Replace("'start'", "'later'")));
    }

    // The elevated window test of 2026-10-10: the browser, the permission
    // prompt's secure desktop, the elevated PowerShell, and back.
    private static InputRecordabilityTimeline ElevatedRun() => new(
    [
        R(1, RecorderType, Recorder),
        R(1, DesktopType, UserDesktop),
        R(1.5, ForegroundType, Browser),
        R(12.2, DesktopType, SecureDesktop),
        R(14.0, DesktopType, UserDesktop.Replace("'start'", "'switch'")),
        R(14.0, ForegroundType, Elevated),
        R(28.4, ForegroundType, Browser),
        R(98.7, ForegroundType, Elevated),
        R(116.3, ForegroundType, Browser)
    ]);

    [Fact]
    public void ThePeriodsAreTheElevatedWindowAndTheSecureDesktop()
    {
        var timeline = ElevatedRun();
        Assert.True(timeline.Recorded);
        Assert.Equal("medium", timeline.RecorderLevel);
        Assert.Collection(
            timeline.Unrecordable,
            period =>
            {
                Assert.Equal(12.2 * Second, period.Start, 1e3);
                Assert.Equal(14.0 * Second, period.End, 1e3);
                Assert.Equal("a desktop the recorder cannot open, such as the secure desktop", period.Reason);
            },
            period =>
            {
                Assert.Equal(14.0 * Second, period.Start, 1e3);
                Assert.Equal(28.4 * Second, period.End, 1e3);
                Assert.Equal("high integrity window, powershell", period.Reason);
            },
            period =>
            {
                Assert.Equal(98.7 * Second, period.Start, 1e3);
                Assert.Equal(116.3 * Second, period.End, 1e3);
            });
    }

    [Fact]
    public void OnAnotherDesktopTheForegroundWindowDoesNotMatter()
    {
        var timeline = new InputRecordabilityTimeline(
        [
            R(1, DesktopType, UserDesktop),
            R(2, ForegroundType, Elevated),
            R(3, DesktopType, SecureDesktop),
            R(4, DesktopType, UserDesktop.Replace("'start'", "'switch'"))
        ]);
        Assert.Equal(
            ["high integrity window, powershell", "a desktop the recorder cannot open, such as the secure desktop", "high integrity window, powershell"],
            timeline.Unrecordable.Select(period => period.Reason));
        Assert.Equal(long.MaxValue, timeline.Unrecordable[^1].End);
    }

    [Fact]
    public void AnUnreadableLevelIsUnknownAndNotAGap()
    {
        var timeline = new InputRecordabilityTimeline(
        [
            R(1, DesktopType, UserDesktop),
            R(2, ForegroundType, Unreadable)
        ]);
        Assert.Empty(timeline.Unrecordable);
        Assert.Equal(
            "unknown: integrity of secret not readable: OpenProcessToken failed with error 5.",
            timeline.RowsAt(3 * Second).Single().Value);
    }

    [Fact]
    public void ThePanelRowSaysWhyAndStepsThroughTheChanges()
    {
        var timeline = ElevatedRun();
        var row = timeline.RowsAt(20 * Second).Single();
        Assert.Equal(InputRecordabilityTimeline.Group, row.Group);
        Assert.Equal("not recordable: high integrity window, powershell", row.Value);
        Assert.Equal(14.0 * Second, (double)row.SetAt!, 1e3);
        Assert.Equal("recordable", timeline.RowsAt(5 * Second).Single().Value);

        var times = PropertyChangeSteps.TimesOf(
            row, WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty, null, null, timeline);
        Assert.NotNull(times);
        Assert.Equal(6, times.Count);

        Assert.Equal("not recorded", InputRecordabilityTimeline.Empty.RowsAt(0).Single().Value);
        Assert.Null(InputRecordabilityTimeline.Empty.ChangeTimesOf(InputRecordabilityTimeline.Key));
    }

    [Fact]
    public void TheEventListSaysWhetherInputWasRecordable()
    {
        string Summary(string eventType, string json) =>
            SessionPlaybackArchiveBuilder.CreateSummary(InputRecordabilityRecords.Channel, eventType, J(json));

        Assert.Equal("foreground-integrity: powershell, high, input not recordable", Summary(ForegroundType, Elevated));
        Assert.Equal("input-desktop: desktop not readable, input not recordable", Summary(DesktopType, SecureDesktop));
        Assert.Equal("recorder-integrity: medium", Summary(RecorderType, Recorder));
    }
}
