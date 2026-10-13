using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The page's accessibility tree at a time (AccessibilityTreeTimeline),
/// rebuilt from the checkpoints: see docs/architecture/screen-reader-activity.md,
/// "4c, the tree view".
/// </summary>
public sealed class AccessibilityTreeTests
{
    private const long Second = 1_000_000_000;

    private static string J(string json) => json.Replace('\'', '"');

    private static KeyEvidenceRecord Node(string id, long time, string token, long node, string role, string name, string children = "", bool frame = false)
    {
        var properties = $"id={node} {role}{(children.Length == 0 ? "" : " child_ids=" + children)}{(frame ? " has_child_tree" : "")}";
        using var document = JsonDocument.Parse(J(
            $"{{'context':{{'documentToken':'{token}'}},'checkpointId':'c','nodeIndex':0,'accessibilityNodeId':{node},'parentAccessibilityNodeId':null," +
            $"'domNodeId':null,'role':0,'roleName':'{role}','name':'{name}','description':'','serializedProperties':'{properties}','focused':false}}"));
        return new KeyEvidenceRecord(id, time, BrowserEvidenceChannels.Accessibility, BrowserEvidenceEventTypes.AccessibilityCheckpointNode, document.RootElement.Clone());
    }

    private static KeyEvidenceRecord Received(string id, long time, string token, string tree)
    {
        using var document = JsonDocument.Parse(J(
            $"{{'context':{{'documentToken':'{token}'}},'treeId':'{tree}','action':'scrollToMakeVisible','targetNodeId':1,'anchorNodeId':null,'focusNodeId':null}}"));
        return new KeyEvidenceRecord(id, time, BrowserEvidenceChannels.Accessibility, BrowserEvidenceEventTypes.AccessibilityActionReceived, document.RootElement.Clone());
    }

    private static readonly PageDocument Page = new(Second / 2, "PAGE", "frame-4", true);

    private static string Outline(AccessibilityTreeSnapshot snapshot)
    {
        var lines = new List<string>();
        void Add(AccessibilityTreeRow row, int depth)
        {
            lines.Add(new string(' ', depth * 2) + row.Label);
            foreach (var child in row.Children)
            {
                Add(child, depth + 1);
            }
        }

        foreach (var root in snapshot.Roots)
        {
            Add(root, 0);
        }

        return string.Join("\n", lines);
    }

    [Fact]
    public void TheTreeIsEachObjectsLatestVersionInItsChildrensOrder()
    {
        var tree = new AccessibilityTreeTimeline(new AccessibilityRequests(
        [
            Node("n1", Second, "PAGE", 1, "rootWebArea", "Test page", "3,2"),
            Node("n2", Second, "PAGE", 2, "link", "Home"),
            Node("n3", Second, "PAGE", 3, "paragraph", "", "4"),
            Node("n4", Second, "PAGE", 4, "staticText", "Hello", "-1000000001"),
            Node("n5", Second, "PAGE", -1000000001, "inlineTextBox", ""),
            Node("n6", 3 * Second, "PAGE", 2, "link", "Start")
        ]), [Page]);

        Assert.True(tree.Recorded);
        Assert.Null(tree.At(Second / 4));
        Assert.Null(tree.At(Second * 3 / 4));
        var first = tree.At(2 * Second)!;
        Assert.Equal(
            "rootWebArea \"Test page\" (node 1)\n" +
            "  paragraph holding \"Hello\" (node 3)\n" +
            "    staticText \"Hello\" (node 4)\n" +
            "      inlineTextBox with no name (node -1000000001)\n" +
            "  link \"Home\" (node 2)",
            Outline(first));
        Assert.Equal(5, first.ObjectCount);
        var link = first.Roots[0].Children[1];
        Assert.Equal((Second, "n2", "PAGE:2"), (link.RecordedAt!.Value, link.EventId, link.Key));

        var later = tree.At(4 * Second)!;
        Assert.NotEqual(first.Version, later.Version);
        Assert.Equal(first.Version, tree.At(2 * Second + 1)!.Version);
        Assert.Equal(("link \"Start\" (node 2)", "n6"), (later.Roots[0].Children[1].Label, later.Roots[0].Children[1].EventId));
        Assert.Equal(["PAGE:1", "PAGE:3", "PAGE:4"], later.PathTo("PAGE:4"));
        Assert.Null(later.PathTo("PAGE:99"));
    }

    [Fact]
    public void ChildrenNotRecordedAndObjectsNoneListAreSaidToBe()
    {
        var tree = new AccessibilityTreeTimeline(new AccessibilityRequests(
        [
            Node("n1", Second, "PAGE", 1, "rootWebArea", "Test page", "2,5"),
            Node("n2", Second, "PAGE", 2, "link", "Home"),
            Node("n3", Second, "PAGE", 3, "button", "Orphan")
        ]), [Page]);

        var snapshot = tree.At(2 * Second)!;
        Assert.Equal(
            "rootWebArea \"Test page\" (node 1)\n" +
            "  link \"Home\" (node 2)\n" +
            "  node 5, listed as a child, not in the recording by the playhead\n" +
            "1 objects no recorded version lists as a child; they may have been removed\n" +
            "  button \"Orphan\" (node 3)",
            Outline(snapshot));
        Assert.Equal(AccessibilityTreeRowKind.Unplaced, snapshot.Roots[1].Kind);
    }

    [Fact]
    public void APagesOneFrameIsNestedUnderItsFrameObject()
    {
        var records = new List<KeyEvidenceRecord>
        {
            Node("n1", Second, "PAGE", 1, "rootWebArea", "Test page", "2"),
            Node("n2", Second, "PAGE", 2, "iframe", "Same-site frame", "", frame: true),
            Node("f1", 5 * Second, "FRAME", 10, "rootWebArea", "Frame", "11"),
            Node("f2", 5 * Second, "FRAME", 11, "button", "Frame button")
        };
        var tree = new AccessibilityTreeTimeline(new AccessibilityRequests(records), [Page, new PageDocument(Second * 3 / 4, "FRAME", "frame-4", false)]);

        Assert.Equal(
            "rootWebArea \"Test page\" (node 1)\n" +
            "  iframe \"Same-site frame\" (node 2)\n" +
            "    its document's tree is not yet in the recording",
            Outline(tree.At(2 * Second)!));
        Assert.Equal(
            "rootWebArea \"Test page\" (node 1)\n" +
            "  iframe \"Same-site frame\" (node 2)\n" +
            "    rootWebArea \"Frame\" (node 10)\n" +
            "      button \"Frame button\" (node 11)",
            Outline(tree.At(6 * Second)!));

        // A frame of another page is not this page's.
        var other = new AccessibilityTreeTimeline(new AccessibilityRequests(records), [Page, new PageDocument(Second, "FRAME", "frame-9", false)]);
        Assert.EndsWith("its document's tree is not in the recording", Outline(other.At(6 * Second)!), StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralFramesFollowThePagesTree()
    {
        var tree = new AccessibilityTreeTimeline(new AccessibilityRequests(
        [
            Node("n1", Second, "PAGE", 1, "rootWebArea", "Test page", "2,3"),
            Node("n2", Second, "PAGE", 2, "iframe", "One", "", frame: true),
            Node("n3", Second, "PAGE", 3, "iframe", "Two", "", frame: true),
            Node("f1", Second, "FRAME-1", 10, "rootWebArea", "Frame one")
        ]),
        [Page, new PageDocument(Second * 3 / 4, "FRAME-1", "frame-4", false), new PageDocument(Second * 3 / 4, "FRAME-2", "frame-4", false)]);

        Assert.Equal(
            "rootWebArea \"Test page\" (node 1)\n" +
            "  iframe \"One\" (node 2)\n" +
            "    which of the frame trees after the page's is its own is not recorded\n" +
            "  iframe \"Two\" (node 3)\n" +
            "    which of the frame trees after the page's is its own is not recorded\n" +
            "A frame's document tree\n" +
            "  rootWebArea \"Frame one\" (node 10)\n" +
            "A frame's document, whose tree is not yet in the recording",
            Outline(tree.At(2 * Second)!));
    }

    [Fact]
    public void ObjectsOfATreeAreFoundByTheirDocument()
    {
        var requests = new AccessibilityRequests(
        [
            Received("r1", Second / 2, "PAGE", "TREE-1"),
            Node("n1", Second, "PAGE", 1, "rootWebArea", "Test page")
        ]);
        var tree = new AccessibilityTreeTimeline(requests, [Page]);

        Assert.Equal("PAGE:1", tree.KeyOf(new TreeObject("TREE-1", 1, "rootWebArea", "Test page")));
        Assert.Null(tree.KeyOf(new TreeObject("TREE-9", 1, "rootWebArea", "Test page")));
        Assert.Equal("PAGE", requests.DocumentOfTree("TREE-1"));
        Assert.Null(requests.PointerObjectAt(Second));
        Assert.False(AccessibilityTreeTimeline.Empty.Recorded);
        Assert.Null(AccessibilityTreeTimeline.Empty.At(Second));
    }
}
