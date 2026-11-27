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

    [Theory]
    [InlineData("Explore", "interaction-explore", "dispatched")]
    [InlineData("Tab", "interaction-tab-guard", "ready")]
    public void InvestigationProtocolUsesDistinctRequests(string kind, string type, string status)
    {
        using var request = JsonDocument.Parse(CompanionProtocol.InteractionProbeRequest(5, new(App, PackageName),
            new(45, 75, 300, 500, 0), Enum.Parse<AndroidInvestigationInput>(kind)));
        Assert.Equal(type, request.RootElement.GetProperty("type").GetString());
        Assert.False(request.RootElement.TryGetProperty("direction", out _));
        Assert.Equal(new CompanionNavigationReply(5, status, 2),
            CompanionProtocol.Parse(JsonSerializer.Serialize(new { type, id = 5, status, started = 2 })));
    }

    [Fact]
    public async Task OneInvestigationSeparatesThreeInputsAndRestoresTalkBack()
    {
        var rig = new Rig();
        var control = Node(1, new(15, 35, 120, 85)) with { ClassName = "android.widget.Button", Clickable = true, Focusable = true, Text = "Fixture control" };
        int picture = 1, nativeTaps = 0, canvasTaps = 0, touchTests = 0, tabs = 0, tree = 2;
        long started = Started;
        rig.Publish(tree, nodes: [Node(0, Viewport), control], accessibilityFocused: true);
        var modes = new ModeRig
        {
            OnTab = () =>
            {
                if (++tabs == 1)
                {
                    rig.Event(AndroidInteractionExploration.InputFocus, control, started);
                    rig.Publish(++tree, nodes: [Node(0, Viewport), control with { Focused = true }], at: started + 70_000_000);
                }
                else if (tabs == 2)
                {
                    // A fresh accessibility focus event during a real Tab is not input-focus evidence.
                    rig.Event(AndroidTalkBackSignals.Focus, control, started);
                    rig.Publish(++tree, nodes: [Node(0, Viewport), control with { AccessibilityFocused = true, Focused = true }],
                        at: started + 70_000_000, accessibilityFocused: true);
                }
            }
        };
        var service = rig.Service(() => throw new InvalidOperationException());
        service.ConfigureInvestigation((_, point, action, _) =>
        {
            started += 2_000_000_000;
            if (action == AndroidInvestigationInput.Explore)
            {
                rig.Event(AndroidInteractionExploration.ExploreStart, null, started);
                if (++touchTests == 1)
                {
                    rig.Event(AndroidTalkBackSignals.Focus, control, started);
                    rig.Publish(++tree, nodes: [Node(0, Viewport), control with { AccessibilityFocused = true }],
                        at: started + 70_000_000, accessibilityFocused: true);
                }
                rig.Event(AndroidInteractionExploration.ExploreEnd, null, started);
            }
            else if (action == AndroidInvestigationInput.Tap)
            {
                if (point.Y < 100)
                {
                    nativeTaps++;
                    rig.Event("TYPE_VIEW_CLICKED", control, started);
                }
                else
                {
                    canvasTaps++;
                    rig.Preview.Publish(1, (ulong)++picture, 300, 500, Paint(Canvas), started + 80_000_000);
                }
            }
            return Task.FromResult(new CompanionNavigationReply(1, action == AndroidInvestigationInput.Tab ? "ready" : "dispatched", started));
        }, () => modes, _ => Task.CompletedTask);
        var result = await service.InvestigateAsync((_, _) => Task.FromResult(new AndroidInvestigationInventory(
            [new(new("Fixture control", "Activate", [control.Bounds], true, 1), true, true),
                new(new("Canvas target", "Outline", [Canvas], null, null), true, true)], "Deterministic.")),
            rig.Marker, null, TestContext.Current.CancellationToken);

        Assert.Equal("Investigation finished on the selected viewport.", result.Status);
        Assert.Equal(new[] { "on", "off", "restored" }, modes.Changes);
        Assert.Equal((2, 3, 1, 1), (touchTests, tabs, nativeTaps, canvasTaps));
        Assert.Contains("2 corroborated as touch exploration", result.TalkBack);
        Assert.Contains("1 items verified", result.Tab);
        Assert.Contains("1 matched clicks; 1 local visual changes", result.DirectTouch);
        Assert.Contains("Reached at", Assert.Single(result.Findings[0].ExploreByTouch));
        Assert.Contains("input focus", Assert.Single(result.Findings[0].Tab));
        Assert.Contains("Android click source", Assert.Single(result.Findings[0].DirectTouch));
        Assert.Contains("no app item focus was verified", Assert.Single(result.Findings[1].ExploreByTouch));
        Assert.Contains("Not verified by Tab", Assert.Single(result.Findings[1].Tab));
        Assert.Contains("no Android click was matched", Assert.Single(result.Findings[1].DirectTouch));
        Assert.Equal(1UL, result.StartingPicture?.Sequence);
        Assert.Equal(control.Bounds, Assert.Single(result.Findings[0].StartingLocation!.Outlines));
        Assert.Empty(result.Findings[0].TapPictures);
        Assert.Equal(Canvas, Assert.Single(result.Findings[1].StartingLocation!.Outlines));
        var pair = Assert.Single(result.Findings[1].TapPictures);
        Assert.Equal((1UL, 2UL), (pair.Before.Sequence, pair.After.Sequence));
        Assert.Equal((pair.Before.Width, pair.Before.Height), (pair.After.Width, pair.After.Height));
        Assert.Equal(Canvas, Assert.Single(pair.Crop.Outlines));
        var target = ((Canvas.Top - pair.Crop.Region.Top) * pair.Before.Width + Canvas.Left - pair.Crop.Region.Left) * 3;
        Assert.Equal(0, pair.Before.Rgb.Span[target]);
        Assert.Equal(255, pair.After.Rgb.Span[target]);
        Assert.Equal(0, pair.After.Rgb.Span[0]); // The crop preserves surrounding pixels, without marking them.
        Assert.Equal(2, rig.Markers.Count);
        Assert.Equal(rig.Markers[0].Split(' ')[3], rig.Markers[1].Split(' ')[3]);
    }

    [Fact]
    public void ReportCropClipsToTheViewportAndOutlinesLinkFragmentsIndividually()
    {
        var rig = new Rig();
        var first = new AndroidNodeBounds(-5, 20, 30, 32);
        var second = new AndroidNodeBounds(10, 34, 55, 46);
        var snapshot = rig.Service(() => throw new InvalidOperationException()).Capture()!;
        var crop = Assert.IsType<AndroidInvestigationCrop>(
            AndroidInvestigationPictures.Locate(snapshot, [first, second, new(301, 2, 320, 12), second]));

        Assert.Equal(new AndroidNodeBounds(0, 7, 68, 59), crop.Region);
        Assert.Equal(new[] { new AndroidNodeBounds(0, 20, 30, 32), second }, crop.Outlines);
    }

    [Fact]
    public void ReportCropOmitsCoveredUnmappedAndPasswordPixels()
    {
        var rig = new Rig();
        var service = rig.Service(() => throw new InvalidOperationException());
        var adjacent = new AndroidNodeBounds(75, 126, 175, 144);
        rig.Publish(2, overlay: true);
        Assert.Null(AndroidInvestigationPictures.Locate(service.Capture()!, [Canvas]));
        // Preserve the target when only the surrounding context would include protected pixels.
        Assert.Equal(adjacent, AndroidInvestigationPictures.Locate(service.Capture()!, [adjacent])?.Region);

        rig.Publish(3, nodes: [Node(0, Viewport), Node(1, Canvas) with { Password = true }]);
        Assert.Null(AndroidInvestigationPictures.Locate(service.Capture()!, [Canvas]));
        Assert.Equal(adjacent, AndroidInvestigationPictures.Locate(service.Capture()!, [adjacent])?.Region);

        rig.Display = rig.Display with { ShownWidth = 301 };
        Assert.Null(AndroidInvestigationPictures.Locate(service.Capture()!, [adjacent]));
    }

    [Fact]
    public void TapPicturePairBoundsPixelCostAndRequiresMatchingFrameGeometry()
    {
        var rig = new Rig();
        var service = rig.Service(() => throw new InvalidOperationException());
        rig.Preview.Publish(1, 2, 800, 1000, new byte[800 * 1000 * 3], Started);
        rig.Display = rig.Display with { ShownWidth = 800, ShownHeight = 1000 };
        rig.Publish(2, bounds: new(0, 0, 800, 1000));
        var before = service.Capture()!;
        rig.Preview.Publish(1, 3, 800, 1000, new byte[800 * 1000 * 3], Started + 100_000_000);
        var box = new AndroidNodeBounds(100, 100, 700, 800);
        var pair = Assert.IsType<AndroidInvestigationTapPictures>(AndroidInvestigationPictures.TapPair(before, service.Capture()!, box));
        Assert.Equal((417, 480), (pair.Before.Width, pair.Before.Height));
        Assert.Equal(pair.Before.Rgb.Length, pair.After.Rgb.Length);
        Assert.Equal(box, Assert.Single(pair.Crop.Outlines));

        rig.Display = rig.Display with { Rotation = 180 };
        var rotated = service.Capture()!;
        Assert.Equal(0, before.Display!.Rotation);
        Assert.Equal(180, rotated.Display!.Rotation);
        Assert.True(rotated.HasScreenCoordinates);
        Assert.Null(AndroidInvestigationPictures.TapPair(before, rotated, box));
    }

    [Theory]
    [InlineData("navigation")]
    [InlineData("physical input")]
    [InlineData("no exploration events")]
    [InlineData("cancel")]
    public async Task InvestigationDoesNotSubstituteOrContinueOnLostScope(string failure)
    {
        var rig = new Rig();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var dispatched = new List<AndroidInvestigationInput>();
        var modes = new ModeRig();
        var service = rig.Service(() => throw new InvalidOperationException());
        service.ConfigureInvestigation((_, _, kind, _) =>
        {
            dispatched.Add(kind);
            if (kind == AndroidInvestigationInput.Explore)
            {
                switch (failure)
                {
                    case "navigation": rig.Event(AndroidAccessibilityRecords.WindowStateChangedName, null); break;
                    case "physical input": rig.Input = new(1, 0, true); break;
                    case "cancel": cancellation.Cancel(); break;
                }
            }
            return Task.FromResult(new CompanionNavigationReply(1, kind == AndroidInvestigationInput.Tab ?
                "direct-touch-unavailable" : "dispatched", Started));
        }, () => modes, token => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        var result = await service.InvestigateAsync((_, _) => Task.FromResult(new AndroidInvestigationInventory(
            [new(new("Canvas target", "Outline", [Canvas], null, null), true, true)], "")),
            rig.Marker, null, cancellation.Token);
        Assert.Equal("restored", modes.Changes[^1]);
        Assert.Equal(2, rig.Markers.Count);
        if (failure == "no exploration events")
        {
            Assert.Contains("touch-exploration events were not verified", Assert.Single(result.Findings[0].ExploreByTouch));
            Assert.Equal(AndroidInvestigationInput.Explore, dispatched[0]);
        }
        else
        {
            Assert.Equal(new[] { AndroidInvestigationInput.Explore }, dispatched);
            Assert.Equal("Not run.", result.Tab);
            Assert.Equal("Not run.", result.DirectTouch);
        }
    }

    [Fact]
    public void VisualDiscoveryPairsAWideCanvasControlWithItsLabelWithoutTreatingTextAloneAsActionable()
    {
        var rig = new Rig();
        var canvas = new AndroidNodeBounds(10, 130, 290, 176);
        rig.Preview.Publish(1, 2, 300, 500, Paint(canvas), Started);
        var baseline = rig.Service(() => throw new InvalidOperationException()).Capture()!;
        var scan = AndroidInvestigationCandidates.Find(baseline,
            [new("Canvas control", new(18, 145, 122, 159), 1), new("Status", new(20, 208, 91, 230), 2)]);
        var candidate = Assert.Single(scan.Candidates, row => row.Item.Label == "Canvas control");
        Assert.True(candidate.Tap);
        Assert.Null(candidate.Item.NodeIndex);
        Assert.Equal("Status", Assert.Single(scan.UnresolvedText).Label);
        Assert.True(scan.NeedsVision);
        var proposed = scan.WithProposals(baseline,
            [(canvas, "Duplicate canvas"), (new(196, 200, 222, 231), "Custom icon"), (Viewport, "Bad bounds")]);
        Assert.Equal(scan.Candidates.Count + 1, proposed.Count);
        Assert.Contains(proposed, entry => entry.Item.Label == "Custom icon" && entry.Tap);
    }

    [Fact]
    public void VisionParsesStrictLocationsAndNeverAcceptsModelActionCommands()
    {
        var proposed = AndroidInvestigationCandidates.ParseProposals(
            """{"targets":[{"label":"Tool","box":[200,100,900,400]}]}""", new(25, 50, 225, 450));
        Assert.Equal((new AndroidNodeBounds(65, 90, 205, 210), "Tool"), Assert.Single(proposed!));
        Assert.Null(AndroidInvestigationCandidates.ParseProposals(
            """{"targets":[{"label":"Tool","box":[200,100,900,400],"command":"tap"}]}""", Viewport));
        Assert.Null(AndroidInvestigationCandidates.ParseProposals(
            """{"targets":[{"label":"Tool","box":[200,100,1001,400]}]}""", Viewport));
    }

    [Fact]
    public void LargeUnexplainedPictureChangeStopsRemainingTapTargets()
    {
        var before = new AndroidLivePreviewFrame(1, 300, 500, Paint());
        Assert.True(AndroidInteractionExploration.FrameChangedOutside(before,
            new(2, 300, 500, Paint(new(0, 260, 300, 500))), Viewport, Canvas));
        Assert.False(AndroidInteractionExploration.FrameChangedOutside(before,
            new(2, 300, 500, Paint(Canvas)), Viewport, Canvas));
    }

    [Fact]
    public async Task ModesUseRealTabAndOnlyChangeTheKnownTalkBackComponentForTheSelectedUser()
    {
        var adb = new SettingsAdb();
        var original = adb.Services;
        var modes = new AndroidInvestigationModes(adb, "fixture");
        var token = TestContext.Current.CancellationToken;
        Assert.Null(await modes.SetTalkBackAsync(true, token));
        Assert.Equal(original + ":" + SettingsAdb.TalkBack, adb.Services);
        Assert.Null(await modes.SetTalkBackAsync(false, token));
        Assert.Equal(original, adb.Services);
        Assert.Null(await modes.TabAsync(token));
        Assert.Null(await modes.RestoreAsync());
        Assert.Single(adb.Commands, command => command.EndsWith("input -d 0 keyevent KEYCODE_TAB", StringComparison.Ordinal));
        Assert.All(adb.Commands.Where(command => command.Contains("settings", StringComparison.Ordinal)), command =>
            Assert.Contains("settings --user 10", command));
        Assert.DoesNotContain(adb.Commands, command => command.Contains(" secure accessibility_enabled", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestorationDoesNotClobberAnIndependentServiceChange(bool changed)
    {
        var adb = new SettingsAdb();
        var original = adb.Services = SettingsAdb.TalkBack + ":" + adb.Services;
        var modes = new AndroidInvestigationModes(adb, "fixture");
        Assert.Null(await modes.SetTalkBackAsync(false, TestContext.Current.CancellationToken));
        if (changed)
        {
            adb.Services += ":org.example.synthetic/.OtherService";
        }
        var before = adb.Services;
        var restored = await modes.RestoreAsync();
        if (changed)
        {
            Assert.Contains("without overwriting a separate change", restored);
            Assert.Equal(before, adb.Services);
        }
        else
        {
            Assert.Null(restored);
            Assert.Equal(original, adb.Services);
        }
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
        private long _eventIndex;
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

        internal void Publish(long index, bool overlay = false, IReadOnlyList<AndroidTreeNode>? nodes = null,
            long at = Started - 30_000_000, bool accessibilityFocused = false, AndroidNodeBounds? bounds = null)
        {
            var app = App with { Items = nodes ?? [Node(0, Viewport)], Focused = true,
                AccessibilityFocused = accessibilityFocused, Bounds = bounds ?? App.Bounds };
            _tree.Publish(new AndroidAccessibilityTree
            {
                Index = index, EveryWindow = true, StartedPhoneNanoseconds = at,
                FinishedPhoneNanoseconds = at + 10_000_000, Windows = overlay
                    ? [app, App with { Id = 12, Layer = 1, Active = false, Bounds = Canvas }]
                    : [app]
            });
        }

        internal void Event(string name, AndroidAccessibilityNode? source, long started = Started) => Signals.Observe(new()
        {
            Index = ++_eventIndex, EventType = 1, EventTypeName = name, EventTimeMilliseconds = started / 1_000_000 + 60,
            ReceivedPhoneNanoseconds = started + 61_000_000, HandledPhoneNanoseconds = started + 63_000_000,
            WindowId = App.Id, DisplayId = 0, PackageName = PackageName, Source = source
        });

        internal bool Marker(string marker)
        {
            Markers.Add(marker);
            return true;
        }
    }

    private sealed class ModeRig : IAndroidInvestigationModes
    {
        public Action? OnTab { get; init; }
        public List<string> Changes { get; } = [];

        public Task<string?> SetTalkBackAsync(bool on, CancellationToken cancellation)
        {
            Changes.Add(on ? "on" : "off");
            return Task.FromResult<string?>(null);
        }

        public Task<string?> TabAsync(CancellationToken cancellation)
        {
            OnTab?.Invoke();
            return Task.FromResult<string?>(null);
        }

        public Task<string?> RestoreAsync()
        {
            Changes.Add("restored");
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class SettingsAdb : IAdb
    {
        public const string TalkBack = AndroidDeviceChecks.TalkBackPackage + "/.TalkBackService";
        private const string SuiteSibling = AndroidDeviceChecks.TalkBackPackage + "/.AnotherService";
        public string Services { get; set; } = CompanionProtocol.ServiceComponent + ":" + SuiteSibling;
        public List<string> Commands { get; } = [];
        public string Path => "fixture-adb";

        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
        {
            var command = arguments[3];
            Commands.Add(command);
            var result = command switch
            {
                "am get-current-user" => new AdbResult(0, "10\n", ""),
                "settings --user 10 get secure enabled_accessibility_services" => new(0, Services + "\n", ""),
                _ when command.StartsWith("cmd package query-services --user 10", StringComparison.Ordinal) =>
                    new(0, SuiteSibling + "\n" + TalkBack + "\n", ""),
                _ when command.Contains("settings --user 10 put secure", StringComparison.Ordinal) => Write(command),
                _ when command == """[ "$(am get-current-user)" = '10' ] && input -d 0 keyevent KEYCODE_TAB""" => new(0, "", ""),
                _ => throw new InvalidOperationException()
            };
            return Task.FromResult(result);
        }

        private AdbResult Write(string command)
        {
            var fields = command.Split('\'');
            if (fields[1] != Services)
            {
                return new(1, "", "");
            }
            Services = fields[3];
            return new(0, "", "");
        }

        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => throw new InvalidOperationException();
    }
}
