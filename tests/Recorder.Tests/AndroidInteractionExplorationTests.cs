using System.Text.Json;
using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class AndroidInteractionExplorationTests
{
    private const string PackageName = "org.example.synthetic";
    private const long Started = 2_000_000_000;
    private static readonly AndroidNodeBounds Viewport = new(0, 0, 300, 500);
    private static readonly AndroidNodeBounds Canvas = new(75, 145, 175, 177);
    private static readonly AndroidAccessibilityWindow App = new()
    {
        Id = 11, DisplayId = 0, Type = 1, TypeName = "TYPE_APPLICATION", Active = true, Bounds = Viewport
    };

    [Fact]
    public void DeclarationUsesActionsRatherThanFocusOrRoleAlone()
    {
        var snapshot = Window(
            Node(0, Viewport) with { Focusable = true, Actions = [new(64, null), new(0x01020036, null)] },
            Node(1, new(10, 30, 80, 90)) with { Text = "Fixture control", LongClickable = true, Checkable = true, Enabled = false },
            Node(2, new(10, 91, 80, 139)) with
            {
                Editable = true, Scrollable = true,
                Actions = [new(0x0102003a, null), new(0x0102003d, null), new(0x01020049, null), new(0x00ffffff, "Fixture task")]
            },
            Node(3, new(10, 140, 200, 200)) with
            {
                Text = "Open help", Links =
                [new(5, 9, "clickable", null, "located", 4, [new(50, 150, 70, 190)]),
                    new(0, 4, "url", null, "not-requested", null, [])]
            },
            Node(4, new(0, 600, 100, 700)) with { Clickable = true },
            Node(5, new(10, 220, 80, 260)) with { Editable = true, Password = true, Text = "Fixture secret", Hint = "Password" });
        var entries = AndroidInteractionInventory.Declared(snapshot);
        Assert.DoesNotContain(entries, item => item.NodeIndex is 0 or 4);
        var native = Assert.Single(entries, item => item.NodeIndex == 1);
        Assert.False(native.Enabled);
        Assert.Contains("Long press", native.Evidence);
        Assert.Contains("Checkable", native.Evidence);
        var editable = Assert.Single(entries, item => item.NodeIndex == 2);
        foreach (var expected in new[] { "Editable", "Scroll down", "Set progress", "Page right", "Fixture task" })
        {
            Assert.Contains(expected, editable.Evidence);
        }
        Assert.Equal(new AndroidNodeBounds(50, 150, 70, 190), Assert.Single(Assert.Single(entries, item => item.Label == "help").Boxes));
        Assert.Empty(Assert.Single(entries, item => item.Label == "Open").Boxes);
        Assert.Equal("Password", Assert.Single(entries, item => item.NodeIndex == 5).Label);
    }

    [Fact]
    public void VisualPassFindsFixtureCanvasTextInsideScrollContainerAndKeepsStaticTextUnverified()
    {
        var snapshot = Window(
            Node(0, Viewport) with { Scrollable = true },
            Node(1, new(0, 29, 280, 99)) with { Text = "Native action", Clickable = true },
            Node(2, new(0, 385, 300, 425)) with { Editable = true, Password = true });
        var entries = AndroidInteractionInventory.Visual(snapshot, AndroidInteractionInventory.Declared(snapshot),
            [new("Native", new(10, 49, 70, 59), 0), new("action", new(72, 49, 101, 59), 0),
                new("Canvas-only", new(75, 146, 126, 176), 1), new("action", new(129, 146, 174, 176), 1),
                new("Fixture status", new(10, 205, 160, 234), 2), new("Covered", new(5, 255, 85, 285), 3),
                new("Fixture secret", new(20, 395, 130, 410), 4), new("Outside", new(10, 501, 120, 520), 5)],
            Viewport, [new(0, 245, 300, 300)]);
        Assert.Equal(new[] { "Canvas-only action", "Fixture status" }, entries.Select(item => item.Label));
        Assert.All(entries, entry =>
        {
            Assert.Null(entry.NodeIndex);
            Assert.Null(entry.Enabled);
            Assert.Contains("may be static", entry.Evidence);
        });
    }

    [Fact]
    public void VisualOutlineCanFindAnUnlabelledShapeButHonorsKnownControlsAndOcclusion()
    {
        var stroke = new AndroidNodeBounds(42, 70, 80, 110);
        var frame = new AndroidLivePreviewFrame(1, 300, 500, Paint(stroke));
        var snapshot = Window(Node(0, Viewport));
        var outline = Assert.Single(AndroidInteractionShapes.Find(frame, snapshot, [], [], out var limited));
        Assert.False(limited);
        Assert.Contains("may be decoration", outline.Evidence);
        Assert.Null(outline.Enabled);
        var bounds = Assert.Single(outline.Boxes);
        Assert.Equal(new AndroidNodeBounds(41, 69, 81, 111), bounds);
        Assert.Empty(AndroidInteractionShapes.Find(frame, snapshot, [], [stroke], out _));
        Assert.Empty(AndroidInteractionShapes.Find(frame, snapshot,
            [new("Fixture control", "Activate", [stroke], true, 1)], [], out _));
    }

    [Fact]
    public void LocalPictureEvidencePicksUpFixtureBackgroundChangeWithoutNewTextInTheCrop()
    {
        var surface = new AndroidNodeBounds(16, 130, 284, 185);
        var before = new AndroidLivePreviewFrame(1, 300, 500, Paint(surface, 38, 56, 80));
        var after = new AndroidLivePreviewFrame(2, 300, 500, Paint(surface, 41, 74, 61));
        var compressionNoise = new AndroidLivePreviewFrame(2, 300, 500, Paint(surface, 42, 58, 78));
        Assert.True(AndroidInteractionExploration.PixelsChanged(before, after, Canvas));
        Assert.False(AndroidInteractionExploration.PixelsChanged(before, compressionNoise, Canvas));
    }

    [Theory]
    [InlineData("canvas response", "Pixels at the test target changed", false)]
    [InlineData("matched click", "Android reported a click", true)]
    [InlineData("background response", "No target response was corroborated", false)]
    [InlineData("other source", "No target response was corroborated", false)]
    [InlineData("early frame", "No target response was corroborated", false)]
    [InlineData("event loss", "missing evidence", false)]
    [InlineData("physical overlap", "other touch input", false)]
    [InlineData("scrolled", "viewport changed", false)]
    [InlineData("refused", "Tap refused", false)]
    public async Task ProbeOutcomeDistinguishesObservedActionsFromUnknowns(string kind, string expected, bool matched)
    {
        var rig = new Rig();
        var service = rig.Service(() =>
        {
            switch (kind)
            {
                case "canvas response": rig.Preview.Publish(1, 2, 300, 500, Paint(Canvas), Started + 70_000_000); break;
                case "background response": rig.Preview.Publish(1, 2, 300, 500, Paint(new(5, 250, 80, 290)), Started + 70_000_000); break;
                case "early frame": rig.Preview.Publish(1, 2, 300, 500, Paint(Canvas), Started - 100); break;
                case "matched click": rig.Event("TYPE_VIEW_CLICKED", Node(1, Canvas)); break;
                case "other source": rig.Event("TYPE_VIEW_CLICKED", Node(1, new(30, 30, 50, 50))); break;
                case "event loss": rig.Signals.Observe(null); break;
                case "physical overlap": rig.Input = new(2, 0, true); break;
                case "scrolled": rig.Event("TYPE_VIEW_SCROLLED", null); break;
                case "refused": return new(1, "scope-changed", 0);
            }
            return new(1, "dispatched", Started);
        });
        var baseline = Assert.IsType<AndroidInteractionSnapshot>(service.Capture());
        Assert.Equal(0, rig.Dispatched);
        var result = await service.TapAsync(baseline, Canvas, 1, rig.Marker, TestContext.Current.CancellationToken);
        Assert.Contains(expected, result.Evidence);
        Assert.Equal(matched, result.ClickEvent.HasValue);
        Assert.Equal(kind == "canvas response", result.AfterFrame.HasValue);
        Assert.Equal(1, rig.Dispatched);
        Assert.Equal(2, rig.Markers.Count);
        Assert.Contains("started; candidate 1", rig.Markers[0]);
        Assert.Contains("ended.", rig.Markers[1]);
    }

    [Theory]
    [InlineData("new frame", "picture")]
    [InlineData("no monitoring", "monitoring")]
    [InlineData("new tree", "tree")]
    [InlineData("physical touch", "physical input")]
    [InlineData("covering window", "covered")]
    [InlineData("different size", "mapped")]
    [InlineData("outside display", "bounds")]
    [InlineData("marker rejected", "No tap was sent")]
    public async Task ProbeDoesNotDispatchWithUncertainScope(string kind, string expected)
    {
        var rig = new Rig();
        if (kind == "covering window") rig.Publish(2, overlay: true);
        if (kind == "different size") rig.Display = rig.Display with { ShownWidth = 400 };
        var service = rig.Service(() => new(1, "dispatched", Started));
        var baseline = Assert.IsType<AndroidInteractionSnapshot>(service.Capture());
        switch (kind)
        {
            case "new frame": rig.Preview.Publish(1, 2, 300, 500, Paint(Canvas), Started); break;
            case "new tree": rig.Publish(2); break;
            case "no monitoring": rig.Ready = false; break;
            case "physical touch": rig.Input = new(1, 0, false); break;
        }
        var result = await service.TapAsync(baseline, kind == "outside display" ? Canvas with { Right = 400 } : Canvas, 1,
            kind == "marker rejected" ? _ => false : rig.Marker, TestContext.Current.CancellationToken);
        Assert.Contains(expected, result.Evidence);
        Assert.Equal(0, rig.Dispatched);
        Assert.Empty(rig.Markers);
    }

    [Fact]
    public void ProtocolCarriesScopePointAndLogicalDisplaySeparatelyFromTalkBack()
    {
        using var request = JsonDocument.Parse(CompanionProtocol.InteractionProbeRequest(5, new(App, PackageName),
            new(45, 75, 500, 300, 90)));
        var json = request.RootElement;
        Assert.Equal("interaction-probe", json.GetProperty("type").GetString());
        Assert.Equal(PackageName, json.GetProperty("packageName").GetString());
        Assert.Equal(11, json.GetProperty("windowId").GetInt32());
        Assert.Equal(300, json.GetProperty("bounds").GetProperty("right").GetInt32());
        Assert.Equal(90, json.GetProperty("rotation").GetInt32());
        Assert.Equal(45, json.GetProperty("x").GetInt32());
        Assert.Equal(75, json.GetProperty("y").GetInt32());
        Assert.False(json.TryGetProperty("direction", out _));
        Assert.Equal(new CompanionNavigationReply(5, "dispatched", 2),
            CompanionProtocol.Parse("""{"type":"interaction-probe","id":5,"status":"dispatched","started":2}"""));
        var display = Assert.IsType<CompanionDisplay>(CompanionProtocol.Parse("""
            {"type":"display","reason":"changed","rotation":90,"modeWidth":300,"modeHeight":500,"shownWidth":500,"shownHeight":300,"time":2}
            """));
        Assert.Equal((500, 300), (display.ShownWidth, display.ShownHeight));
        using var archive = JsonDocument.Parse(JsonSerializer.Serialize(
            new AndroidDisplayState(display.Reason, display.Rotation, display.ModeWidth, display.ModeHeight)
            { ShownWidth = display.ShownWidth, ShownHeight = display.ShownHeight }, JsonSerializerOptions.Web));
        Assert.False(archive.RootElement.TryGetProperty("shownWidth", out _));
        Assert.False(archive.RootElement.TryGetProperty("shownHeight", out _));
    }

    private static AndroidLiveWindowSnapshot Window(params AndroidTreeNode[] nodes) =>
        new(App, new(1, 0, nodes, false, 0, Started - 30_000_000));

    private static AndroidTreeNode Node(int index, AndroidNodeBounds bounds) => new()
    {
        Index = index, WindowId = App.Id, Bounds = bounds, PackageName = PackageName,
        VisibleToUser = true, Checked = "unchecked", Enabled = true
    };

    private static byte[] Paint(AndroidNodeBounds? box = null, byte r = 255, byte g = 255, byte b = 255)
    {
        var rgb = new byte[300 * 500 * 3];
        if (box is not null)
        {
            for (int row = box.Top; row < box.Bottom; row++)
            {
                for (int col = box.Left; col < box.Right; col++)
                {
                    var pos = (row * 300 + col) * 3;
                    rgb[pos] = r;
                    rgb[pos + 1] = g;
                    rgb[pos + 2] = b;
                }
            }
        }
        return rgb;
    }

    private sealed class Rig
    {
        internal readonly AndroidLivePreview Preview = new();
        internal readonly AndroidTalkBackSignals Signals = new();
        internal readonly List<string> Markers = [];
        internal AndroidTalkBackInput Input;
        internal AndroidDisplayState Display = new("start", 0, 300, 500) { ShownWidth = 300, ShownHeight = 500 };
        internal int Dispatched;
        internal bool Ready = true;
        private readonly AndroidLiveTree _tree = new();

        internal Rig()
        {
            Preview.BeginSession(1);
            Preview.Publish(1, 1, 300, 500, Paint(), Started - 50_000_000);
            Publish(1);
        }

        internal AndroidInteractionExploration Service(Func<CompanionNavigationReply> dispatch) =>
            new(Preview, _tree, Signals, () => Ready, () => Input, _ => Display, (scope, point, _) =>
            {
                Assert.Equal(App.Id, scope.Window.Id);
                Assert.Equal(PackageName, scope.PackageName);
                Assert.Equal((125, 161, 300, 500, 0), (point.X, point.Y, point.Width, point.Height, point.Rotation));
                Dispatched++;
                return Task.FromResult(dispatch());
            }, _ => Task.CompletedTask);

        internal void Publish(long index, bool overlay = false)
        {
            var app = App with { Items = [Node(0, Viewport)] };
            _tree.Publish(new AndroidAccessibilityTree
            {
                Index = index, EveryWindow = true, StartedPhoneNanoseconds = Started - 30_000_000,
                FinishedPhoneNanoseconds = Started - 20_000_000, Windows = overlay
                    ? [app, App with { Id = 12, Layer = 1, Active = false, Bounds = Canvas }]
                    : [app]
            });
        }

        internal void Event(string name, AndroidAccessibilityNode? source) => Signals.Observe(new()
        {
            Index = 1, EventType = 1, EventTypeName = name, EventTimeMilliseconds = 2060,
            ReceivedPhoneNanoseconds = Started + 61_000_000, HandledPhoneNanoseconds = Started + 63_000_000,
            WindowId = App.Id, DisplayId = 0, PackageName = PackageName, Source = source
        });

        internal bool Marker(string marker)
        {
            Markers.Add(marker);
            return true;
        }
    }
}
