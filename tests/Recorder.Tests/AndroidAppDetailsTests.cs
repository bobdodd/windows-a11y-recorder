using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class AndroidAppDetailsTests
{
    [Fact]
    public void DetailsFollowTheArchiveEvenWithoutVisitsOrSnapshots()
    {
        var desktop = CreateArchive();
        var legacy = desktop with { FrameSource = SessionFrameSource.Phone };
        var preflight = desktop with
        {
            Manifest = desktop.Manifest with
            {
                AndroidPreRecordingTextSizes = new("org.example.synthetic", DateTimeOffset.UnixEpoch, "complete", [])
            }
        };
        var withoutFrames = desktop with
        {
            Manifest = desktop.Manifest with
            {
                Collectors = [new("test", "test", "test", [AndroidCompanionRecords.CompanionChannel],
                    "test", null, [], "test", "test")]
            }
        };

        Assert.False(AndroidAppDetailsEvidence.IsAvailable(null));
        Assert.False(AndroidAppDetailsEvidence.IsAvailable(desktop));
        Assert.True(AndroidAppDetailsEvidence.IsAvailable(legacy));
        Assert.True(AndroidAppDetailsEvidence.IsAvailable(preflight));
        Assert.True(AndroidAppDetailsEvidence.IsAvailable(withoutFrames));
        Assert.Empty(AndroidAppDetailsEvidence.Packages(legacy));
        Assert.Equal("org.example.synthetic", Assert.Single(AndroidAppDetailsEvidence.Packages(preflight)));
        Assert.Null(AndroidApkDetails.From(preflight.Manifest));
        var forms = AndroidFormDetails.From(legacy, null);
        Assert.Empty(forms.Apps);
        Assert.Contains("No visits with application trees", forms.Summary);
    }

    [Fact]
    public void FormEvidenceKeepsApplicationsVisitsAndTreeTimesSeparate()
    {
        var first = Visit("org.example.synthetic", 1, 1);
        var second = Visit("org.example.synthetic", 1, 2);
        var third = Visit("org.example.synthetic.other", 2, 3);
        var forms = AndroidFormDetails.Group(
            [new(first, 20, ["Second tree"]), new(second, 30, ["Return visit"]),
             new(first, 10, ["First tree"]), new(third, 40, ["Other app"])]);

        Assert.Equal(2, forms.Count);
        Assert.Equal("org.example.synthetic", forms[0].Package);
        Assert.Equal(2, forms[0].Visits.Count);
        Assert.Equal((1, 1), (forms[0].Visits[0].Screen, forms[0].Visits[0].Visit));
        Assert.Equal([10L, 20L], forms[0].Visits[0].Trees.Select(tree => tree.TreeTime));
        Assert.Equal("First tree", Assert.Single(forms[0].Visits[0].Trees[0].Fields));
        Assert.Equal((1, 2), (forms[0].Visits[1].Screen, forms[0].Visits[1].Visit));
        Assert.Equal("org.example.synthetic.other", forms[1].Package);
        Assert.Equal(3, Assert.Single(forms[1].Visits).Visit);
    }

    private static PhoneVisit Visit(string package, int screen, int visit) =>
        new(visit, 0, PhoneVisitCause.NewWindow, 1, package, null, null, null)
        {
            Screen = new PhoneScreen(screen, package, null, null, PhoneTitleSource.Untitled, null)
        };

    private static SessionPlaybackArchive CreateArchive() => new("test",
        new SessionManifest("1.1", "test", "completed", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            0, 1, 0, "test", "test", "test", new(false, false, false, false, 0, false, false), [], [], 0, 0, null),
        0, [], [], [], []);
}
