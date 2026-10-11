using System.Runtime.InteropServices;
using System.Text.Json;
using Recorder.Collectors.Automation;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// What NVDA's windows show: the whole tree of a window of NVDA's main
/// process, read when focus enters it and after it changes. See
/// docs/architecture/screen-reader-activity.md, "2c-4, what NVDA's windows
/// show".
/// </summary>
public sealed class WindowSnapshotTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;
    private const int NvdaProcess = 20008;

    // A fixed tree for the walk: each node's children, and nodes whose
    // reads fail.
    private sealed class Node(string name, params Node[] children)
    {
        public string Name { get; } = name;
        public Node[] Children { get; } = children;
        public Node? Parent { get; set; }
        public bool ChildrenFail { get; init; }
    }

    private sealed class Tree : IWindowTreeSource<Node>
    {
        public int Reads { get; private set; }

        public static Node Link(Node node)
        {
            foreach (var child in node.Children)
            {
                child.Parent = node;
                Link(child);
            }

            return node;
        }

        public Node? FirstChild(Node node) =>
            node.ChildrenFail ? throw new COMException("gone", unchecked((int)0x80040201)) : node.Children.FirstOrDefault();

        public Node? NextSibling(Node node)
        {
            var siblings = node.Parent!.Children;
            var index = Array.IndexOf(siblings, node);
            return index + 1 < siblings.Length ? siblings[index + 1] : null;
        }

        public WindowSnapshotElement Read(Node node, int? parent, int depth, int position)
        {
            Reads++;
            return Element(node.Name, "ControlType.Pane", parent, depth, position);
        }
    }

    private static WindowSnapshotElement Element(
        string? name,
        string controlType,
        int? parent,
        int depth,
        int position,
        string? value = null,
        bool? selected = null,
        bool focused = false,
        string? localized = null,
        int id = 0,
        bool? password = null) =>
        new(parent, depth, position, id == 0 ? null : [42, id], name, controlType, localized, null, null, "Win32",
            null, null, true, false, focused, true, password, new WindowSnapshotRectangle(0, 0, 10, 10),
            value, value is null ? null : false, null, selected, null, []);

    [Fact]
    public void TheWalkReadsEachElementBeforeItsChildrenWithItsParentDepthAndPosition()
    {
        var window = Tree.Link(new Node("window", new Node("a", new Node("a1"), new Node("a2")), new Node("b")));
        var reading = WindowTreeWalk.Read(new Tree(), window, () => 0);
        Assert.Equal(
            [("window", null, 0, 1), ("a", 0, 1, 1), ("a1", 1, 2, 1), ("a2", 1, 2, 2), ("b", 0, 1, 2)],
            reading.Elements.Select(item => (item.Name, item.Parent, item.Depth, item.Position)));
        Assert.Null(reading.Truncated);
        Assert.Empty(reading.Problems);
    }

    [Fact]
    public void TheWalkStopsAtTheElementLimitAndSaysSo()
    {
        var window = Tree.Link(new Node("window", [.. Enumerable.Range(0, 10).Select(index => new Node($"item {index}"))]));
        var reading = WindowTreeWalk.Read(new Tree(), window, () => 0, maximumElements: 4);
        Assert.Equal(4, reading.Elements.Count);
        Assert.Equal(WindowSnapshotRecords.ElementLimit, reading.Truncated);
    }

    [Fact]
    public void TheWalkStopsAtTheTimeLimitAndSaysSo()
    {
        var window = Tree.Link(new Node("window", [.. Enumerable.Range(0, 10).Select(index => new Node($"item {index}"))]));
        var clock = 0L;
        var tree = new Tree();
        // Each read of the clock is 100 ms later.
        var reading = WindowTreeWalk.Read(tree, window, () => clock += 100 * Millisecond, maximumNanoseconds: 350 * Millisecond);
        Assert.Equal(WindowSnapshotRecords.TimeLimit, reading.Truncated);
        Assert.True(reading.Elements.Count < 11);
        Assert.Equal(tree.Reads, reading.Elements.Count);
    }

    [Fact]
    public void AFailedChildListIsAProblemAndTheWalkGoesOn()
    {
        var window = Tree.Link(new Node("window", new Node("gone", new Node("hidden")) { ChildrenFail = true }, new Node("next")));
        var reading = WindowTreeWalk.Read(new Tree(), window, () => 0);
        Assert.Equal(["window", "gone", "next"], reading.Elements.Select(item => item.Name));
        var problem = Assert.Single(reading.Problems);
        Assert.StartsWith("Children of element 1 after 0: ", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepeatedFocusOnTheSameStateIsTheSameAndAChangedValueIsNot()
    {
        var before = Element("Filter by", "ControlType.Edit", 0, 1, 1, value: "");
        Assert.True(WindowTreeWalk.SameState(before, before with { Position = 2 }));
        Assert.False(WindowTreeWalk.SameState(before, before with { Value = "con" }));
        Assert.Equal("42.7", WindowTreeWalk.Key([42, 7]));
        Assert.Null(WindowTreeWalk.Key([]));
    }

    // The Elements List as NVDA 2026.2 builds it (elementsList.py): the
    // type radio buttons, the filter, the tree of the page's links, and the
    // two buttons. The window and the tree say they have the focus too, as
    // in recording 20261011-195613.
    private static IReadOnlyList<WindowSnapshotElement> ElementsList(string filter = "") =>
    [
        Element("Elements List", "ControlType.Window", null, 0, 1, focused: true, localized: "dialog", id: 1),
        Element("Type:", "ControlType.Group", 0, 1, 1, localized: "group", id: 2),
        Element("Links", "ControlType.RadioButton", 1, 2, 1, selected: true, localized: "radio button", id: 3),
        Element("Headings", "ControlType.RadioButton", 1, 2, 2, selected: false, localized: "radio button", id: 4),
        Element("Filter by:", "ControlType.Edit", 0, 1, 2, value: filter, localized: "edit", id: 5),
        Element(null, "ControlType.Tree", 0, 1, 3, focused: true, localized: "tree view", id: 6),
        Element("Contact Us", "ControlType.TreeItem", 5, 2, 1, selected: true, focused: true, localized: "tree item", id: 7),
        Element("Donate", "ControlType.TreeItem", 5, 2, 2, selected: false, localized: "tree item", id: 8),
        Element("CNIB Deafblind Community Services", "ControlType.TreeItem", 5, 2, 3, selected: false, localized: "tree item", id: 9),
        Element("Activate", "ControlType.Button", 0, 1, 4, localized: "button", id: 10),
        Element("Move to", "ControlType.Button", 0, 1, 5, localized: "button", id: 11)
    ];

    private static readonly CollectorDescriptor Snapshots = CollectorDescriptor.Create(
        WindowSnapshotRecords.CollectorType, "Test", "1.0.0", [WindowSnapshotRecords.Channel], "windows.ui-automation");

    private static RecorderEvent Snapshot(
        long time,
        IReadOnlyList<WindowSnapshotElement> elements,
        string reason = WindowSnapshotRecords.FocusEnteredReason,
        ulong sequence = 0,
        string? truncated = null) =>
        RecorderEventFactory.Create(
            "s",
            Snapshots,
            WindowSnapshotRecords.Channel,
            sequence,
            time,
            WindowSnapshotRecords.EventType,
            new
            {
                reason,
                trigger = new
                {
                    eventType = "focus-changed",
                    eventId = "AutomationElementIdentifiers.AutomationFocusChangedEvent",
                    atNanoseconds = time - Millisecond
                },
                processId = NvdaProcess,
                elementCount = elements.Count,
                truncated,
                readNanoseconds = 12 * Millisecond,
                maximumElements = WindowSnapshotRecords.MaximumElements,
                maximumReadNanoseconds = WindowSnapshotRecords.MaximumReadNanoseconds,
                elements,
                problems = Array.Empty<string>()
            });

    private static IReadOnlyList<EventValidationIssue> Validate(RecorderEvent record) =>
        new EventRecordValidator("s").Validate(record);

    [Fact]
    public void ASnapshotAsTheCollectorWritesItIsValid() =>
        Assert.Empty(Validate(Snapshot(6 * Second, ElementsList())));

    [Fact]
    public void ASnapshotWhoseElementsAreNotInTreeOrderIsRefused()
    {
        var elements = ElementsList().ToList();
        elements[6] = elements[6] with { Depth = 3 };
        Assert.Contains(Validate(Snapshot(6 * Second, elements)), issue => issue.Code == "window-snapshot-inconsistent");
    }

    [Fact]
    public void APasswordsValueIsRefused()
    {
        var elements = ElementsList().ToList();
        elements[4] = elements[4] with { IsPassword = true };
        Assert.Contains(Validate(Snapshot(6 * Second, elements)), issue => issue.Code == "window-snapshot-inconsistent");
    }

    [Fact]
    public void AnUnknownReasonIsRefused() =>
        Assert.NotEmpty(Validate(Snapshot(6 * Second, ElementsList(), reason: "guessed")));

    private static JsonElement PayloadOf(RecorderEvent record) => record.Payload;

    [Fact]
    public void TheSummaryCountsTheItemsAndGivesTheSelectionValuesAndFocus()
    {
        Assert.Equal(
            "2 radio buttons, 3 tree items; selected: \"Links\" (radio button, selected), " +
            "\"Contact Us\" (tree item, selected, focused); Filter by: \"con\"; focus: \"Contact Us\" (tree item, selected, focused)",
            WindowSnapshots.Summary(PayloadOf(Snapshot(6 * Second, ElementsList("con")))));
    }

    [Fact]
    public void TheOutlineIndentsEachElementUnderItsParent()
    {
        var outline = WindowSnapshots.Outline(PayloadOf(Snapshot(6 * Second, ElementsList())))
            .Split(Environment.NewLine);
        Assert.Equal(
            [
                "What the window showed: 11 elements, read in 12 ms, read when focus entered the window",
                "\"Elements List\" (dialog, focused)",
                "  \"Type:\" (group)",
                "    \"Links\" (radio button, selected)",
                "    \"Headings\" (radio button)",
                "  \"Filter by:\" (edit, value \"\")",
                "  (no name) (tree view, focused)",
                "    \"Contact Us\" (tree item, selected, focused)",
                "    \"Donate\" (tree item)",
                "    \"CNIB Deafblind Community Services\" (tree item)",
                "  \"Activate\" (button)",
                "  \"Move to\" (button)"
            ],
            outline);
    }

    [Fact]
    public void ATruncatedSnapshotSaysItsReadStopped()
    {
        var payload = PayloadOf(Snapshot(6 * Second, ElementsList(), truncated: WindowSnapshotRecords.TimeLimit));
        Assert.EndsWith("(read stopped at 2 seconds)", WindowSnapshots.Summary(payload), StringComparison.Ordinal);
        Assert.Contains("the read stopped at the limit of 2 seconds", WindowSnapshots.Outline(payload), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompactFormKeepsTheSummaryWithoutTheElements()
    {
        var compact = WindowSnapshots.Compact(PayloadOf(Snapshot(6 * Second, ElementsList())));
        Assert.False(compact.TryGetProperty("elements", out _));
        Assert.Equal("Elements List", compact.GetProperty("windowName").GetString());
        var snapshot = WindowSnapshots.Read(6 * Second, compact)!;
        Assert.Equal((NvdaProcess, 11, WindowSnapshotRecords.FocusEnteredReason), (snapshot.ProcessId, snapshot.ElementCount, snapshot.Reason));
        Assert.Equal(WindowSnapshots.Summary(PayloadOf(Snapshot(6 * Second, ElementsList()))), snapshot.Summary);

        // Compacting again gives it back as it is.
        Assert.Equal(compact.GetRawText(), WindowSnapshots.Compact(compact).GetRawText());
    }

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json.Replace('\'', '"'));
        return document.RootElement.Clone();
    }

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "test.collector", "Test", "1.0.0", ["window.foreground", "accessibility.uia.events", AssistiveTechnologyRecords.Channel], "test");

    [Fact]
    public void ThePlayerShowsWhatTheWindowShowedThroughThePlaybackIndex()
    {
        var records = new List<RecorderEvent>
        {
            RecorderEventFactory.Create("s", Collector, AssistiveTechnologyRecords.Channel, 1, 0, AssistiveTechnologyRecords.WatchEventType,
                J("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'browserExecutablePath':null,'soundProblem':null}")),
            RecorderEventFactory.Create("s", Collector, AssistiveTechnologyRecords.Channel, 2, Millisecond, AssistiveTechnologyRecords.ProcessStartedEventType,
                J($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess},'runningAtStart':true,'productVersion':'2026.2'}}")),
            RecorderEventFactory.Create("s", Collector, "window.foreground", 3, 2 * Millisecond, "foreground-window",
                J("{'windowHandle':1,'processId':4000,'processName':'chrome','title':'Home | CNIB','className':'Chrome_WidgetWin_1'}")),
            RecorderEventFactory.Create("s", Collector, "window.foreground", 4, 5 * Second, "foreground-window",
                J($"{{'windowHandle':2,'processId':{NvdaProcess},'processName':'nvda','title':'Elements List','className':'#32770'}}")),
            RecorderEventFactory.Create("s", Collector, "accessibility.uia.events", 5, 6 * Second, "focus-changed",
                J($"{{'eventId':'AutomationElementIdentifiers.AutomationFocusChangedEvent','changeType':null,'runtimeId':null,'newValue':null," +
                  $"'element':{{'processId':{NvdaProcess},'name':'Contact Us','controlType':'ControlType.TreeItem'}}}}")),
            Snapshot(6 * Second + 20 * Millisecond, ElementsList()),
            Snapshot(7 * Second, ElementsList("con"), WindowSnapshotRecords.WindowChangedReason, sequence: 1),
            RecorderEventFactory.Create("s", Collector, "window.foreground", 6, 8 * Second, "foreground-window",
                J("{'windowHandle':1,'processId':4000,'processName':'chrome','title':'Home | CNIB','className':'Chrome_WidgetWin_1'}"))
        };

        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var key = 0L;
        foreach (var record in records)
        {
            key++;
            builder.Add(key, record with { EventId = $"e{key}" });
        }

        var index = builder.Build();
        Assert.Equal(15, index.Version);
        var kept = index.Events.Where(item => item.EventType == WindowSnapshotRecords.EventType).ToList();
        Assert.Equal(2, kept.Count);
        Assert.All(kept, item => Assert.False(item.Payload.TryGetProperty("elements", out _)));

        var archiveBuilder = new SessionPlaybackArchiveBuilder(Path.GetTempPath(), retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(20), 20 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 1, 0, null));

        var windows = archive.ScreenReaderWindows;
        var span = Assert.Single(windows.Spans);
        Assert.Equal(2, span.Snapshots.Count);
        Assert.EndsWith(
            "; showing at 00:00:07.000: 2 radio buttons, 3 tree items; selected: \"Links\" (radio button, selected), " +
            "\"Contact Us\" (tree item, selected, focused); Filter by: \"con\"; focus: \"Contact Us\" (tree item, selected, focused)",
            windows.ValueAt(7500 * Millisecond),
            StringComparison.Ordinal);
        Assert.Contains("Filter by: \"\"", windows.ValueAt(6500 * Millisecond), StringComparison.Ordinal);

        // Each snapshot is a step of the row and an event of the strip.
        Assert.Contains(6 * Second + 20 * Millisecond, windows.ChangeTimes());
        Assert.Contains(7 * Second, windows.ChangeTimes());
        Assert.Equal(
            ["foreground-window", "focus-changed", "window-snapshot", "window-snapshot", "foreground-window"],
            windows.Events.Select(item => item.EventType));
        Assert.StartsWith(
            "This record: what it showed, snapshot 2 of 2, read when the window had been quiet for 300 ms after it changed: 2 radio buttons",
            windows.Describe(windows.Events[3])[1],
            StringComparison.Ordinal);
    }
}
