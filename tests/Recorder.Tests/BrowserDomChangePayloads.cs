namespace Recorder.Tests;

// The JSON the bridge writes for each structural DOM change record of
// protocol 0.34, shared by the ingest, validation, and check tests so all
// three read the same shapes. A button is inserted into the body (node 10) of
// a document whose checkpoint holds html (2), head (3), and body; it carries a
// shadow root with a slot. The button is then removed, the body emptied, the
// shadow root's reference target set, and the slot reassigned.
internal static class BrowserDomChangePayloads
{
    public const string ContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": "dom-document-19",
          "executionWorldId": null,
          "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
        }
        """;

    public static readonly string NodeInserted = $$"""
        {
          "context": {{ContextJson}},
          "transitionId": "dom-transition-4",
          "insertionKind": "child",
          "containerNodeId": 10,
          "nodeId": 20,
          "previousSiblingNodeId": null
        }
        """;

    public static readonly string ShadowRootInserted = $$"""
        {
          "context": {{ContextJson}},
          "transitionId": "dom-transition-5",
          "insertionKind": "shadow-root",
          "containerNodeId": 20,
          "nodeId": 21,
          "previousSiblingNodeId": null
        }
        """;

    public static readonly string InsertedNode = $$"""
        {
          "context": {{ContextJson}},
          "insertionId": "dom-transition-4",
          "nodeIndex": 0,
          "nodeId": 20,
          "parentNodeId": 10,
          "nodeType": "element",
          "nodeName": "BUTTON"
        }
        """;

    public static readonly string InsertedNodeAttribute = $$"""
        {
          "context": {{ContextJson}},
          "insertionId": "dom-transition-4",
          "nodeId": 20,
          "attributeIndex": 0,
          "attributeNamespace": null,
          "attributeName": "aria-pressed",
          "attributeValue": "false",
          "attributeValueLength": 5,
          "attributeValueTruncated": false,
          "maximumValueLength": 2147483647
        }
        """;

    public static readonly string InsertedNodeCharacterData = $$"""
        {
          "context": {{ContextJson}},
          "insertionId": "dom-transition-4",
          "nodeId": 22,
          "data": "Save",
          "dataLength": 4,
          "dataTruncated": false,
          "maximumValueLength": 2147483647
        }
        """;

    public static readonly string InsertedShadowRoot = $$"""
        {
          "context": {{ContextJson}},
          "insertionId": "dom-transition-4",
          "nodeId": 21,
          "hostNodeId": 20,
          "mode": "open",
          "delegatesFocus": false,
          "slotAssignment": "named",
          "clonable": false,
          "serializable": false,
          "declarative": false,
          "availableToElementInternals": false,
          "referenceTarget": null
        }
        """;

    public static readonly string InsertedSlotAssignment = $$"""
        {
          "context": {{ContextJson}},
          "insertionId": "dom-transition-4",
          "nodeId": 23,
          "assignedNodeIds": [22],
          "assignedNodeCount": 1,
          "assignedNodesTruncated": false,
          "maximumAssignedNodes": 2147483647,
          "assignmentCurrent": true
        }
        """;

    public static readonly string InsertionCompleted = $$"""
        {
          "context": {{ContextJson}},
          "insertionId": "dom-transition-4",
          "nodeCount": 4,
          "attributeCount": 1,
          "characterDataCount": 1,
          "shadowRootCount": 1,
          "slotCount": 1
        }
        """;

    public static readonly string NodeRemoved = $$"""
        {
          "context": {{ContextJson}},
          "transitionId": "dom-transition-6",
          "containerNodeId": 10,
          "nodeId": 20
        }
        """;

    public static readonly string ChildrenRemoved = $$"""
        {
          "context": {{ContextJson}},
          "transitionId": "dom-transition-7",
          "containerNodeId": 10
        }
        """;

    public static readonly string ShadowRootChanged = $$"""
        {
          "context": {{ContextJson}},
          "transitionId": "dom-transition-8",
          "nodeId": 21,
          "hostNodeId": 20,
          "mode": "open",
          "delegatesFocus": false,
          "slotAssignment": "named",
          "clonable": false,
          "serializable": false,
          "declarative": false,
          "availableToElementInternals": false,
          "referenceTarget": "inner"
        }
        """;

    public static readonly string SlotAssignmentChanged = $$"""
        {
          "context": {{ContextJson}},
          "transitionId": "dom-transition-9",
          "nodeId": 23,
          "assignedNodeIds": [],
          "assignedNodeCount": 0,
          "assignedNodesTruncated": false,
          "maximumAssignedNodes": 2147483647
        }
        """;

    public static IEnumerable<(string EventType, string Json)> All()
    {
        yield return ("dom-node-inserted", NodeInserted);
        yield return ("dom-node-inserted", ShadowRootInserted);
        yield return ("dom-inserted-node", InsertedNode);
        yield return ("dom-inserted-node-attribute", InsertedNodeAttribute);
        yield return ("dom-inserted-node-character-data", InsertedNodeCharacterData);
        yield return ("dom-inserted-shadow-root", InsertedShadowRoot);
        yield return ("dom-inserted-slot-assignment", InsertedSlotAssignment);
        yield return ("dom-insertion-completed", InsertionCompleted);
        yield return ("dom-node-removed", NodeRemoved);
        yield return ("dom-children-removed", ChildrenRemoved);
        yield return ("dom-shadow-root-changed", ShadowRootChanged);
        yield return ("dom-slot-assignment-changed", SlotAssignmentChanged);
    }
}
