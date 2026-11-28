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
        var model = new TaskCompletionSource<IReadOnlyList<(AndroidNodeBounds, string)>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = service.InvestigateAsync(async (snapshot, token) =>
        {
            var suggestions = await model.Task.WaitAsync(token);
            var mapping = AndroidInvestigationCandidates.Map(snapshot, suggestions);
            Assert.Equal(2, mapping.Duplicates);
            return new(mapping.Candidates, "Fixture image model.");
        }, rig.Marker, null, TestContext.Current.CancellationToken);
        Assert.Single(rig.Markers);
        Assert.Empty(modes.Changes);
        Assert.Equal((0, 0, 0, 0), (touchTests, tabs, nativeTaps, canvasTaps));
        model.SetResult([(new(77, 148, 101, 175), "Canvas icon"), (new(20, 42, 55, 70), "Native icon"),
            (control.Bounds, "Fixture control"), (Canvas, "Canvas target")]);
        var result = await run;

        Assert.Equal("Investigation finished on the selected viewport.", result.Status);
        Assert.Equal(new[] { "on", "off", "restored" }, modes.Changes);
        Assert.Equal((2, 3, 1, 1), (touchTests, tabs, nativeTaps, canvasTaps));
        Assert.Contains("2 corroborated as touch exploration", result.TalkBack);
        Assert.Contains("1 items verified", result.Tab);
        Assert.Contains("1 matched clicks; 1 local visual changes", result.DirectTouch);
        Assert.Contains("matched to Android", result.Findings[0].Item.Evidence);
        Assert.Contains("no matching Android tree node", result.Findings[1].Item.Evidence);
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
    public void ModelSuggestionsAreMatchedWithoutTurningNoisyFeedPixelsOrStaticTextIntoTargets()
    {
        var rig = new Rig();
        var native = new AndroidNodeBounds(10, 35, 100, 85);
        var link = new AndroidNodeBounds(25, 290, 71, 315);
        var image = Paint();
        // Synthetic feed full of high-contrast shapes that are not controls.
        for (var y = 320; y < 490; y++)
        {
            for (var x = 10; x < 290; x++)
            {
                image.AsSpan((y * 300 + x) * 3, 3).Fill((byte)(((x / 11 + y / 13) % 2) * 255));
            }
        }
        rig.Preview.Publish(1, 2, 300, 500, image, Started);
        rig.Publish(3, nodes:
        [
            Node(0, Viewport) with { Scrollable = true, ChildCount = 5 },
            Node(1, native) with { Clickable = true, Text = "Native", ContentDescription = "Native fixture action" },
            Node(2, new(10, 210, 170, 243)) with { Text = "Status", Focusable = true },
            Node(3, new(5, 275, 280, 317)) with { Text = "See help", Links = [new(4, 8, "url", null, "located", 4, [link])] },
            Node(4, new(230, 10, 270, 50)) with { Clickable = true, Text = "Tree only" },
            Node(5, new(110, 90, 240, 128)) with { Text = "Settings" }
        ]);
        var baseline = rig.Service(() => throw new InvalidOperationException()).Capture()!;
        var scan = AndroidInvestigationCandidates.Map(baseline, [
            (new(18, 48, 73, 72), "Native"), (Canvas, "Canvas control"), (Canvas, "Duplicate canvas"),
            (link, "help"), (new(126, 94, 205, 120), "Settings"), (Viewport, "Container"),
            (new(12, 320, 15, 325), "Too small")]);
        Assert.Equal((3, 1, 2, 3), (scan.Matched, scan.ModelOnly, scan.TreeOnly, scan.NotUsed));
        Assert.Equal(1, scan.Duplicates);
        var candidate = Assert.Single(scan.Candidates, row => row.Item.Label == "Canvas control");
        Assert.True(candidate.Tap);
        Assert.Null(candidate.Item.NodeIndex);
        Assert.Contains("no matching", candidate.Item.Evidence);
        Assert.Contains("matched to Android", Assert.Single(scan.Candidates, row => row.Item.NodeIndex == 1).Item.Evidence);
        Assert.Contains("matched to Android", Assert.Single(scan.Candidates, row => row.Item.Role == "Inline link").Item.Evidence);
        Assert.Contains("no declared action", Assert.Single(scan.Candidates, row => row.Item.NodeIndex == 5).Item.Evidence);
        Assert.DoesNotContain(scan.Candidates, row => row.Item.NodeIndex == 2);

        var unavailable = AndroidInvestigationCandidates.Map(baseline, []);
        Assert.Equal((0, 0, 4), (unavailable.Matched, unavailable.ModelOnly, unavailable.TreeOnly));
        Assert.All(unavailable.Candidates, row => Assert.Contains("Android tree only", row.Item.Evidence));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedAndShiftedProposalsUseOneWholeTargetButKeepRepeatedControlsAtDifferentLocations(bool reverse)
    {
        var rig = new Rig();
        var whole = new AndroidNodeBounds(60, 150, 180, 210);
        var repeated = new AndroidNodeBounds(60, 250, 180, 310);
        (AndroidNodeBounds, string)[] proposals =
        [
            (new(70, 158, 106, 190), "Illustration detail"),
            (new(112, 172, 128, 188), "Small center symbol"),
            (new(66, 154, 177, 212), "Alternate thumbnail"),
            (whole, "Thumbnail"),
            (repeated, "Thumbnail")
        ];
        var snapshot = rig.Service(() => throw new InvalidOperationException()).Capture()!;
        var map = AndroidInvestigationCandidates.Map(snapshot, reverse ? proposals.Reverse().ToArray() : proposals);
        Assert.Equal((0, 2, 0, 3), (map.Matched, map.ModelOnly, map.TreeOnly, map.Duplicates));
        Assert.All(map.Candidates, row => Assert.Equal("Thumbnail", row.Item.Label));
        Assert.Equal(new[] { whole, repeated }, map.Candidates.SelectMany(row => row.Item.Boxes).OrderBy(box => box.Top));
    }

    [Fact]
    public void IndependentlyDeclaredMenuAndSurfaceKeepTheirDistinctTapLocations()
    {
        var rig = new Rig();
        var surface = new AndroidNodeBounds(40, 125, 176, 210);
        var menu = new AndroidNodeBounds(146, 132, 175, 182);
        rig.Publish(3, nodes:
        [
            Node(0, Viewport) with { ChildCount = 2 },
            Node(1, surface) with { Clickable = true, Text = "Image" },
            Node(2, menu) with { Clickable = true, Text = "Menu" }
        ]);
        var snapshot = rig.Service(() => throw new InvalidOperationException()).Capture()!;
        var map = AndroidInvestigationCandidates.Map(snapshot, [(menu, "Menu"), (surface, "Image")]);
        Assert.Equal((2, 0, 0, 0), (map.Matched, map.ModelOnly, map.TreeOnly, map.Duplicates));
        Assert.Equal(new int?[] { 1, 2 }, map.Candidates.Select(row => row.Item.NodeIndex).Order());
        Assert.All(map.Candidates, row => Assert.True(row.Tap));
    }

    [Fact]
    public void SuppressedAliasDoesNotHideConsequentialActionFromTheTapGuard()
    {
        var rig = new Rig();
        var icon = new AndroidNodeBounds(90, 150, 132, 177);
        var snapshot = rig.Service(() => throw new InvalidOperationException()).Capture()!;
        var map = AndroidInvestigationCandidates.Map(snapshot, [(icon, "Send"), (Canvas, "Paper plane control")]);
        var row = Assert.Single(map.Candidates);
        Assert.Equal(1, map.Duplicates);
        Assert.Equal("Paper plane control", row.Item.Label);
        Assert.False(row.Tap);
        Assert.Contains("potentially consequential", row.DuplicateTapRestriction);
    }

    [Fact]
    public void AmbiguousMatchesAndProtectedAreasDoNotBecomeUnrestrictedDirectTaps()
    {
        var rig = new Rig();
        var ambiguous = new AndroidNodeBounds(20, 40, 100, 80);
        var unique = new AndroidNodeBounds(20, 90, 100, 130);
        var password = new AndroidNodeBounds(20, 240, 100, 280);
        rig.Publish(3, overlay: true, nodes:
        [
            Node(0, Viewport) with { ChildCount = 4 },
            Node(1, ambiguous) with { Clickable = true, Text = "First" },
            Node(2, ambiguous) with { Clickable = true, Enabled = false, Text = "Second" },
            Node(3, unique) with { Clickable = true, Enabled = false, Text = "Disabled" },
            Node(4, password) with { Password = true, Text = "Secret", Editable = true }
        ]);
        var snapshot = rig.Service(() => throw new InvalidOperationException()).Capture()!;
        var map = AndroidInvestigationCandidates.Map(snapshot, [(ambiguous, "An icon"), (unique, "Disabled"),
            (Canvas, "Covered"), (password, "Private")]);
        Assert.Equal((1, 1, 3, 2), (map.Matched, map.ModelOnly, map.TreeOnly, map.NotUsed));
        var unresolved = Assert.Single(map.Candidates, row => row.Item.NodeIndex is null);
        Assert.Contains("ambiguous", unresolved.Item.Evidence);
        Assert.False(unresolved.Tap);
        Assert.False(Assert.Single(map.Candidates, row => row.Item.NodeIndex == 3).Item.Enabled);
        Assert.DoesNotContain(map.Candidates, row => row.VisionLabel is "Covered" or "Private");
        Assert.DoesNotContain(map.Candidates, row => row.Item.Label.Contains("Secret", StringComparison.Ordinal));
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

    [Fact]
    public async Task RestorationReportsIndependentChangeAfterTalkBackReturnedToItsStartingMode()
    {
        var adb = new SettingsAdb();
        var modes = new AndroidInvestigationModes(adb, "fixture");
        var token = TestContext.Current.CancellationToken;
        Assert.Null(await modes.SetTalkBackAsync(true, token));
        Assert.Null(await modes.SetTalkBackAsync(false, token));
        adb.Services += ":org.example.synthetic/.OtherService";
        var expected = adb.Services;
        Assert.Contains("without overwriting a separate change", await modes.RestoreAsync());
        Assert.Equal(expected, adb.Services);
    }

    private static AndroidLiveWindowSnapshot Window(params AndroidTreeNode[] nodes) =>
        new(App, new(1, 0, nodes, false, 0, Started - 30_000_000));

    private static AndroidTreeNode Node(int index, AndroidNodeBounds bounds) => new()
    {
        Index = index, WindowId = App.Id, Bounds = bounds, PackageName = PackageName,
        VisibleToUser = true, Checked = "unchecked", Enabled = true
    };

    [Theory]
    [InlineData("empty", "No model proposals")]
    [InlineData("physical", "Physical phone input was detected")]
    [InlineData("scroll", "Android reported scrolling")]
    [InlineData("picture", "picture changed substantially during discovery")]
    [InlineData("feeds", "starting app tree or input evidence feed was incomplete")]
    [InlineData("unaligned", "No model proposals")]
    [InlineData("window", "app window moved or resized")]
    [InlineData("transition", "new transition for the active app window")]
    [InlineData("rotation", "phone display rotated")]
    [InlineData("display", "could not be aligned")]
    [InlineData("stream", "video stream stopped or changed")]
    [InlineData("navigation", "Android reported a window change")]
    [InlineData("missing event", "Accessibility events were missing")]
    public async Task FrozenModelInvestigationDoesNotSendInputIfStartingEvidenceIsLost(string failure, string reason)
    {
        var rig = new Rig { Ready = failure != "feeds" };
        var modes = new ModeRig();
        var service = rig.Service(() => throw new InvalidOperationException());
        service.ConfigureInvestigation((_, _, _, _) => throw new InvalidOperationException("Unexpected input"),
            () => modes, _ => Task.CompletedTask);
        var model = new TaskCompletionSource<IReadOnlyList<AndroidVisionProposal>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = service.Capture()!;
        var run = AfterModelAsync();
        Assert.Empty(modes.Changes);
        Assert.Empty(rig.Markers);
        switch (failure)
        {
            case "physical": rig.Input = new(1, 0, false); break;
            case "scroll": rig.Event("TYPE_VIEW_SCROLLED", null); break;
            case "picture": rig.Preview.Publish(1, 2, 300, 500, Paint(Viewport), Started); break;
            case "feeds": rig.Ready = true; break;
            case "window": rig.Publish(2, bounds: new(0, 10, 300, 500)); break;
            case "transition": rig.Publish(2, transition: Started / 1_000_000); break;
            case "rotation": rig.Display = rig.Display with { Rotation = 90 }; break;
            case "display": rig.Display = rig.Display with { ShownWidth = 299 }; break;
            case "stream":
                rig.Preview.BeginSession(2);
                rig.Preview.Publish(2, 2, 300, 500, Paint(), Started);
                break;
            case "navigation": rig.Event(AndroidAccessibilityRecords.WindowStateChangedName, null); break;
            case "missing event": rig.Signals.Observe(null); break;
        }
        model.SetResult(failure == "empty" ? [] : [Proposal("Canvas switch", Canvas)]);
        var report = await run;
        Assert.Empty(modes.Changes);
        Assert.Contains(reason, report.Status);
        Assert.Equal("Not run.", report.Swipe);
        Assert.Equal("Not run.", report.TalkBack);
        Assert.Equal("Not run.", report.Tab);
        Assert.Equal("Not run.", report.DirectTouch);
        if (failure is "unaligned" or "empty")
        {
            Assert.Empty(report.Findings);
        }
        else
        {
            Assert.Equal(0, Assert.Single(report.Findings).ProposalIndex);
        }

        async Task<AndroidInvestigationReport> AfterModelAsync()
        {
            var proposals = await model.Task.WaitAsync(TestContext.Current.CancellationToken);
            var matches = failure == "unaligned"
                ? new AndroidProposalTreeReport("Not aligned", [])
                : new AndroidProposalTreeReport("Fixture tree", proposals.Select(_ => NoMatch).ToArray());
            var plan = AndroidProposalInputPlan.Build(start, proposals, matches);
            return await service.InvestigateProposalsAsync(start, plan, rig.Marker, null, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ProposalInputObservationsUseExactNodeAndSourceAndKeepFourMethodsSeparate()
    {
        var rig = new Rig();
        var root = Node(0, Viewport);
        var first = Node(1, new(15, 35, 120, 85)) with { Text = "First switch", Clickable = true, Focusable = true };
        var second = Node(2, Canvas) with { Text = "Second switch", Clickable = true, Focusable = true };
        var other = Node(3, new(40, 245, 120, 295)) with { Text = "Fixture other", Focusable = true };
        var refreshed = new[] { root, other with { Text = "Fixture updated detail", Index = 1 },
            second with { Depth = 3 }, first with { Index = 3, Depth = 2 } };
        int tree = 2, tab = 0;
        long started = Started;
        rig.Publish(tree, nodes: [root, first, second, other]);
        var modes = new ModeRig
        {
            OnTab = () =>
            {
                switch (++tab)
                {
                    case 1: Focus(first, AndroidTalkBackSignals.Focus, true); break;
                    case 2: Focus(second, AndroidInteractionExploration.InputFocus, false); break;
                }
            }
        };
        var service = rig.Service(() => throw new InvalidOperationException());
        var start = service.Capture()!;
        var plan = AndroidProposalInputPlan.Build(start,
            [Proposal(first.Text, first.Bounds), Proposal(second.Text, second.Bounds), Proposal("Overlay glyph", first.Bounds)],
            new("Fixture tree", [Match(first), Match(second), NoMatch]));
        // A fresh tree during discovery reindexes siblings, changes depth and
        // refreshes an unrelated focusable label outside the proposal points.
        rig.Publish(++tree, nodes: refreshed);
        int explored = 0, swipe = 0, tap = 0;
        service.ConfigureInvestigation((_, _, kind, _) =>
        {
            started += 2_000_000_000;
            if (kind == AndroidInvestigationInput.Explore)
            {
                rig.Event(AndroidInteractionExploration.ExploreStart, null, started);
                Focus(++explored == 1 ? first : second, AndroidTalkBackSignals.Focus, true);
                rig.Event(AndroidInteractionExploration.ExploreEnd, null, started);
            }
            if (kind == AndroidInvestigationInput.Tap)
            {
                // Only the first click comes from its matched Android node.
                rig.Event("TYPE_VIEW_CLICKED", ++tap == 1 ? first : root, started);
            }
            return Task.FromResult(new CompanionNavigationReply(1, kind == AndroidInvestigationInput.Tab ? "ready" : "dispatched", started));
        }, () => modes, _ =>
        {
            var enabling = modes.Changes[^1] == "on";
            rig.Publish(++tree, nodes: refreshed, layer: enabling ? 2 : 0);
            rig.Event(AndroidAccessibilityRecords.WindowsChangedName, null, started, windowChanges: 0x90);
            return Task.CompletedTask;
        }, (right, _, _) =>
        {
            started += 2_000_000_000;
            Assert.Equal(swipe < 2, right);
            switch (++swipe)
            {
                case 1: Focus(refreshed[1], AndroidTalkBackSignals.Focus, true); break;
                case 2: Focus(second, AndroidInteractionExploration.HoverEnter, true); break;
                case 3: Focus(first, AndroidTalkBackSignals.Focus, true); break;
                case 4: Focus(first, AndroidTalkBackSignals.Focus, true); break; // same focus re-emitted
            }
            return Task.FromResult(new CompanionNavigationReply(1, "dispatched", started));
        });
        var report = await service.InvestigateProposalsAsync(start, plan, rig.Marker, null, TestContext.Current.CancellationToken);
        Assert.Equal((2, 4, 4, 2), (explored, swipe, tab, tap));
        Assert.Equal(["on", "off", "restored"], modes.Changes);
        Assert.Equal([0, 1, 2], report.Findings.Select(finding => finding.ProposalIndex!.Value));
        Assert.Contains("2 focus stops observed; 1 matched", report.Swipe);
        Assert.Contains("left", Assert.Single(report.Findings[0].Swipe));
        Assert.Contains("Not verified", Assert.Single(report.Findings[1].Swipe));
        Assert.Contains("reached at", Assert.Single(report.Findings[0].ExploreByTouch));
        Assert.Contains("this proposal was not verified", Assert.Single(report.Findings[2].ExploreByTouch));
        Assert.Contains("Not verified", Assert.Single(report.Findings[0].Tab));
        Assert.Contains("on Tab 2; input focus", Assert.Single(report.Findings[1].Tab));
        Assert.Contains("click on the matched node", Assert.Single(report.Findings[0].DirectTouch));
        Assert.Contains("click on another starting node", Assert.Single(report.Findings[1].DirectTouch));
        Assert.Contains("overlaps proposal 1", Assert.Single(report.Findings[2].DirectTouch));
        Assert.All(rig.Markers, marker => Assert.DoesNotContain("Fixture other", marker));

        void Focus(AndroidTreeNode target, string eventName, bool accessibility)
        {
            rig.Event(eventName, target, started);
            rig.Publish(++tree, nodes: refreshed.Select(node => node with
            {
                AccessibilityFocused = accessibility && node.Bounds == target.Bounds,
                Focused = !accessibility && node.Bounds == target.Bounds
            }).ToArray(), at: started + 70_000_000, accessibilityFocused: accessibility, layer: accessibility ? 2 : 0);
        }
    }

    [Theory]
    [InlineData("moved", "matched Android node changed")]
    [InlineData("duplicate", "not uniquely identifiable")]
    [InlineData("local pixels", "picture at this point changed")]
    [InlineData("new hit", "Android touch targets at this point changed")]
    public async Task ProposalWithLocalDriftIsSkippedWithoutBlockingAnUnchangedPoint(string change, string reason)
    {
        var rig = new Rig();
        var root = Node(0, Viewport);
        var first = Node(1, new(15, 35, 103, 75)) with { Text = "First switch", Clickable = true };
        var second = Node(2, Canvas) with { Text = "Second switch", Clickable = true };
        rig.Publish(2, nodes: [root, first, second]);
        var service = rig.Service(() => throw new InvalidOperationException());
        var start = service.Capture()!;
        var plan = AndroidProposalInputPlan.Build(start, [Proposal(first.Text, first.Bounds), Proposal(second.Text, second.Bounds)],
            new("Fixture tree", [change == "new hit" ? NoMatch : Match(first), Match(second)]));
        var modes = new ModeRig();
        var points = new List<AndroidInvestigationInput>();
        service.ConfigureInvestigation((_, point, kind, _) =>
        {
            if (kind is AndroidInvestigationInput.Explore or AndroidInvestigationInput.Tap)
            {
                Assert.Equal((125, 161), (point.X, point.Y));
                points.Add(kind);
            }
            return Task.FromResult(new CompanionNavigationReply(1, kind == AndroidInvestigationInput.Tab ? "ready" : "dispatched", Started));
        }, () => modes, _ => Task.CompletedTask);
        switch (change)
        {
            case "moved":
                rig.Publish(3, nodes: [root, first with { Bounds = new(15, 80, 103, 120) }, second]);
                break;
            case "duplicate":
                rig.Publish(3, nodes: [root, first, second, first with { Index = 3 }]);
                break;
            case "local pixels":
                rig.Preview.Publish(1, 2, 300, 500, Paint(first.Bounds), Started);
                break;
            case "new hit":
                rig.Publish(3, nodes: [root, first, second, Node(3, first.Bounds) with { Clickable = true, Text = "New switch" }]);
                break;
        }

        var report = await service.InvestigateProposalsAsync(start, plan, rig.Marker, null, TestContext.Current.CancellationToken);
        Assert.Equal([AndroidInvestigationInput.Explore, AndroidInvestigationInput.Tap], points);
        Assert.Contains("2 Tab keys sent", report.Tab);
        Assert.Contains(reason, Assert.Single(report.Findings[0].ExploreByTouch));
        Assert.Contains(reason, Assert.Single(report.Findings[0].DirectTouch));
        Assert.Equal(["on", "off", "restored"], modes.Changes);
    }

    [Theory]
    [InlineData(false, "Most of the original matched controls moved")]
    [InlineData(true, "Android reported a window change")]
    public async Task InvestigationDoesNotSendInputWhenTheScreenChangesWhileTalkBackSettles(bool navigation, string reason)
    {
        var rig = new Rig();
        var root = Node(0, Viewport);
        var first = Node(1, new(15, 35, 103, 75)) with { Text = "First switch", Clickable = true };
        var second = Node(2, Canvas) with { Text = "Second switch", Clickable = true };
        rig.Publish(2, nodes: [root, first, second]);
        var service = rig.Service(() => throw new InvalidOperationException());
        var start = service.Capture()!;
        var plan = AndroidProposalInputPlan.Build(start, [Proposal(first.Text, first.Bounds), Proposal(second.Text, second.Bounds)],
            new("Fixture tree", [Match(first), Match(second)]));
        var modes = new ModeRig();
        service.ConfigureInvestigation((_, _, _, _) => throw new InvalidOperationException("Unexpected input"), () => modes, _ =>
        {
            if (navigation)
            {
                rig.Event(AndroidAccessibilityRecords.WindowStateChangedName, null, windowId: 12, package: "org.example.other");
            }
            else
            {
                rig.Publish(3, nodes: [root, first with { Bounds = new(15, 80, 103, 120) }, second with { Text = "Replacement" }]);
            }
            return Task.CompletedTask;
        });
        var report = await service.InvestigateProposalsAsync(start, plan, rig.Marker, null, TestContext.Current.CancellationToken);
        Assert.Contains(reason, report.Status);
        Assert.Equal("Not run.", report.TalkBack);
        Assert.Equal("Not run.", report.Tab);
        Assert.Equal(["on", "restored"], modes.Changes);
    }

    [Fact]
    public async Task DirectTouchRejectsConsequentialUnderlyingNodeAndUnmatchedOverlappingAlias()
    {
        var rig = new Rig();
        var top = Node(1, new(15, 35, 120, 85)) with { Clickable = true, Actions = [new(16, "Follow")] };
        var bottom = Node(2, Canvas) with { Clickable = true, Text = "Fixture switch" };
        var badge = new AndroidNodeBounds(110, 156, 140, 167);
        rig.Publish(2, nodes: [Node(0, Viewport), top, bottom]);
        var service = rig.Service(() => throw new InvalidOperationException());
        var start = service.Capture()!;
        var plan = AndroidProposalInputPlan.Build(start,
            [Proposal("Top symbol", top.Bounds), Proposal(bottom.Text, bottom.Bounds), Proposal("Share", badge)],
            new("Fixture tree", [NoMatch, Match(bottom), NoMatch]));
        service.ConfigureInvestigation((_, _, kind, _) =>
        {
            Assert.NotEqual(AndroidInvestigationInput.Tap, kind);
            return Task.FromResult(new CompanionNavigationReply(1, kind == AndroidInvestigationInput.Tab ? "ready" : "dispatched", Started));
        }, () => new ModeRig(), _ => Task.CompletedTask);
        var report = await service.InvestigateProposalsAsync(start, plan, rig.Marker, null, TestContext.Current.CancellationToken);
        Assert.Contains("Android control at the point suggests a consequential action", Assert.Single(report.Findings[0].DirectTouch));
        Assert.Contains("label suggests a consequential action", Assert.Single(report.Findings[1].DirectTouch));
        Assert.Contains("overlaps proposal 2", Assert.Single(report.Findings[2].DirectTouch));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedSwipeStopsOtherInputsAndStillRestoresTalkBack(bool cancelledByUser)
    {
        var rig = new Rig();
        var node = Node(1, Canvas) with { AccessibilityFocused = true, Text = "Fixture switch", Clickable = true };
        rig.Publish(2, nodes: [Node(0, Viewport), node], accessibilityFocused: true);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var modes = new ModeRig();
        var service = rig.Service(() => throw new InvalidOperationException());
        var start = service.Capture()!;
        var plan = AndroidProposalInputPlan.Build(start, [Proposal(node.Text, node.Bounds)], new("Fixture tree", [Match(node)]));
        service.ConfigureInvestigation((_, _, input, _) =>
        {
            Assert.Equal(AndroidInvestigationInput.Explore, input);
            return Task.FromResult(new CompanionNavigationReply(1, "dispatched", Started));
        }, () => modes, _ => Task.CompletedTask, (_, _, _) =>
        {
            if (cancelledByUser)
            {
                cancel.Cancel();
            }
            return Task.FromResult(new CompanionNavigationReply(1, "gesture-cancelled", Started));
        });
        var report = await service.InvestigateProposalsAsync(start, plan, rig.Marker, null, cancel.Token);
        Assert.Contains(cancelledByUser ? "Investigation stopped" : "Android cancelled the gesture", report.Status);
        Assert.Equal("Not run.", report.Tab);
        Assert.Equal("Not run.", report.DirectTouch);
        Assert.Equal(["on", "restored"], modes.Changes);
        Assert.Equal(2, rig.Markers.Count);
    }

    [Fact]
    public void NestedTapPlanKeepsSeparateControlsAndPreservesDuplicateProposals()
    {
        var rig = new Rig();
        var outer = Node(1, Canvas) with { Clickable = true, Text = "Large switch" };
        var inner = Node(2, new(77, 148, 96, 174)) with { Clickable = true, Text = "Small switch" };
        rig.Publish(2, nodes: [Node(0, Viewport), outer, inner]);
        var plan = AndroidProposalInputPlan.Build(rig.Service(() => throw new InvalidOperationException()).Capture()!,
            [Proposal(outer.Text, outer.Bounds), Proposal(inner.Text, inner.Bounds), Proposal(inner.Text, inner.Bounds)],
            new("Fixture tree", [Match(outer), Match(inner), Match(inner)]));
        Assert.Equal([0, 1, 2], plan.Candidates.Select(candidate => candidate.ProposalIndex!.Value));
        Assert.All(plan.Candidates, candidate => Assert.True(candidate.Tap));
        Assert.Equal(plan.Candidates[1].Item.Boxes, plan.Candidates[2].Item.Boxes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EdgeBadgeGetsSeparateTapOnlyWithSeparateAndroidControl(bool separatelyDeclared)
    {
        var rig = new Rig();
        var outer = Node(1, Canvas) with { Clickable = true, Text = "Large switch" };
        var edge = Node(2, new(156, 150, 188, 172)) with { Clickable = true, Text = "Small switch" };
        rig.Publish(2, nodes: separatelyDeclared ? [Node(0, Viewport), outer, edge] : [Node(0, Viewport), outer]);
        var plan = AndroidProposalInputPlan.Build(rig.Service(() => throw new InvalidOperationException()).Capture()!,
            [Proposal(outer.Text, outer.Bounds), Proposal(edge.Text, edge.Bounds)],
            new("Fixture tree", [Match(outer), separatelyDeclared ? Match(edge) : NoMatch]));
        Assert.Equal(2, plan.Candidates.Count);
        Assert.True(plan.Candidates[0].Tap);
        Assert.Equal(separatelyDeclared, plan.Candidates[1].Tap);
    }

    private static AndroidVisionProposal Proposal(string? label, AndroidNodeBounds box) =>
        new(label!, new(box.Left * 10 / 3, box.Top * 2, (box.Right * 10 + 2) / 3, box.Bottom * 2));

    private static AndroidProposalTreeMatch Match(AndroidTreeNode node) =>
        new(AndroidProposalTreeMatchKind.Matched,
            [new(new(node.Text!, "Activate", [node.Bounds], node.Enabled, node.Index), node.Bounds)]);

    private static AndroidProposalTreeMatch NoMatch { get; } = new(AndroidProposalTreeMatchKind.NoMatch, []);

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
            long at = Started - 30_000_000, bool accessibilityFocused = false, AndroidNodeBounds? bounds = null,
            int layer = 0, long? transition = null)
        {
            var app = App with { Items = nodes ?? [Node(0, Viewport)], Focused = true,
                AccessibilityFocused = accessibilityFocused, Bounds = bounds ?? App.Bounds, Layer = layer,
                TransitionTimeMilliseconds = transition };
            _tree.Publish(new AndroidAccessibilityTree
            {
                Index = index, EveryWindow = true, StartedPhoneNanoseconds = at,
                FinishedPhoneNanoseconds = at + 10_000_000, Windows = overlay
                    ? [app, App with { Id = 12, Layer = 1, Active = false, Bounds = Canvas }]
                    : [app]
            });
        }

        internal void Event(string name, AndroidAccessibilityNode? source, long started = Started,
            int windowChanges = 0, int? windowId = null, string package = PackageName) => Signals.Observe(new()
        {
            Index = ++_eventIndex, EventType = 1, EventTypeName = name, EventTimeMilliseconds = started / 1_000_000 + 60,
            ReceivedPhoneNanoseconds = started + 61_000_000, HandledPhoneNanoseconds = started + 63_000_000,
            WindowId = windowId ?? App.Id, DisplayId = 0, PackageName = package, Source = source, WindowChanges = windowChanges
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
