using System.Text.Json;

namespace Recorder.Session;

/// <summary>How much of a part of a document's recorded state is known.</summary>
public enum BrowserStateCompleteness
{
    /// <summary>No full walk of the document was recorded yet, so the part has no state.</summary>
    NotWalked,

    /// <summary>
    /// The document has not finished parsing, and the parser's insertions are
    /// not recorded, so nodes the parser added since the last walk are
    /// missing: a recording before protocol 0.42, or a document with no walk
    /// when its parser was created. DOM only.
    /// </summary>
    Parsing,

    /// <summary>Taken from a full walk and changed by every record since.</summary>
    Complete,

    /// <summary>
    /// A record of the part's channel was lost after the last walk, and the
    /// state lacks that change: for the DOM and interaction state until the
    /// next walk, and for the layout state, which is rebuilt from change
    /// records alone, for the rest of the document.
    /// </summary>
    AfterLoss,

    /// <summary>The document's last DOM walk was cut, so the DOM part has no state until the next.</summary>
    WalkCut,
}

/// <summary>The selection a record gives: its type, and its anchor and focus nodes and offsets.</summary>
public sealed record SelectionState(string? Type, long? AnchorNodeId, long? AnchorOffset, long? FocusNodeId, long? FocusOffset);

/// <summary>A text control's recorded value, null when it was cut, and its selection.</summary>
public sealed record TextControlState(
    long NodeId,
    string? ControlType,
    string? Value,
    long? SelectionStart,
    long? SelectionEnd,
    string? SelectionDirection);

/// <summary>
/// The viewport size in CSS pixels, device pixel ratio, and layout zoom
/// factor of a layout checkpoint's start record, and when it was recorded.
/// </summary>
public sealed record RecordedViewport(double Width, double Height, double DevicePixelRatio, double LayoutZoomFactor, long Time);

/// <summary>The focused node, selection, and text controls the interaction records give.</summary>
public sealed record InteractionCurrent(
    long? FocusedNodeId,
    SelectionState? Selection,
    IReadOnlyDictionary<long, TextControlState> TextControls);

/// <summary>
/// The interaction state of one document: the records of its latest completed
/// interaction checkpoint, and the interaction changes after it, in record
/// order. Records are kept as the payloads they were read from.
/// </summary>
public sealed class InteractionDocumentState
{
    private List<JsonElement>? _pending;
    private List<JsonElement> _checkpoint = [];
    private readonly List<(string EventType, JsonElement Payload)> _changes = [];
    private readonly Dictionary<long, bool> _optionSelectedness = [];

    /// <summary>The start, text control, and completion records of the latest completed checkpoint.</summary>
    public IReadOnlyList<JsonElement> Checkpoint => _checkpoint;

    /// <summary>The focus, selection, value, and active descendant changes after it.</summary>
    public IReadOnlyList<(string EventType, JsonElement Payload)> Changes => _changes;

    /// <summary>
    /// Each option's latest recorded selectedness (protocol 0.43), by node.
    /// Selectedness is recorded only as changes, and no checkpoint holds it,
    /// so it is kept across checkpoints for the life of the document. An
    /// option with no record keeps the selectedness its attributes give.
    /// </summary>
    public IReadOnlyDictionary<long, bool> OptionSelectedness => _optionSelectedness;

    /// <summary>True between a checkpoint's start record and its completion.</summary>
    public bool IsOpen => _pending is not null;

    /// <summary>
    /// The interaction state the records give: the focused node, the
    /// selection, and each text control's value and selection, from the
    /// latest checkpoint and each change after it in record order.
    /// </summary>
    public InteractionCurrent Current()
    {
        long? focused = null;
        SelectionState? selection = null;
        var controls = new Dictionary<long, TextControlState>();
        foreach (var record in _checkpoint)
        {
            if (record.TryGetProperty("focusedNodeId", out _))
            {
                focused = Number(record, "focusedNodeId");
                selection = SelectionOf(record);
            }
            else if (Number(record, "nodeId") is { } node && record.TryGetProperty("controlType", out _))
            {
                controls[node] = ControlOf(node, record);
            }
        }
        foreach (var (eventType, payload) in _changes)
        {
            switch (eventType)
            {
                case "focus-changed":
                    focused = Number(payload, "focusedNodeId");
                    break;
                case "selection-changed":
                    selection = SelectionOf(payload);
                    if (Number(payload, "textControlNodeId") is { } control && controls.TryGetValue(control, out var state))
                    {
                        controls[control] = state with
                        {
                            SelectionStart = Number(payload, "textControlSelectionStart"),
                            SelectionEnd = Number(payload, "textControlSelectionEnd"),
                            SelectionDirection = Text(payload, "textControlSelectionDirection"),
                        };
                    }
                    break;
                case "text-control-value-changed" when Number(payload, "nodeId") is { } node:
                    controls[node] = ControlOf(node, payload);
                    break;
            }
        }
        return new InteractionCurrent(focused, selection, controls);
    }

    private static SelectionState SelectionOf(JsonElement record) => new(
        Text(record, "selectionType"),
        Number(record, "anchorNodeId"),
        Number(record, "anchorOffset"),
        Number(record, "focusNodeId"),
        Number(record, "focusOffset"));

    private static TextControlState ControlOf(long node, JsonElement record) => new(
        node,
        Text(record, "controlType"),
        record.TryGetProperty("valueTruncated", out var truncated) && truncated.ValueKind == JsonValueKind.True
            ? null
            : Text(record, "value"),
        Number(record, "selectionStart"),
        Number(record, "selectionEnd"),
        Text(record, "selectionDirection"));

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal void Load(
        IEnumerable<JsonElement> checkpoint,
        IEnumerable<(string EventType, JsonElement Payload)> changes,
        IEnumerable<KeyValuePair<long, bool>> optionSelectedness)
    {
        _pending = null;
        _checkpoint = [.. checkpoint.Select(record => record.Clone())];
        _changes.Clear();
        _changes.AddRange(changes.Select(change => (change.EventType, change.Payload.Clone())));
        _optionSelectedness.Clear();
        foreach (var (node, selected) in optionSelectedness)
        {
            _optionSelectedness[node] = selected;
        }
    }

    /// <summary>Applies one browser.interaction record of the document. Returns true when it was a state record.</summary>
    public bool Apply(string eventType, JsonElement payload)
    {
        switch (eventType)
        {
            case "interaction-checkpoint-started":
                _pending = [payload.Clone()];
                return true;
            case "interaction-checkpoint-text-control":
                _pending?.Add(payload.Clone());
                return true;
            case "interaction-checkpoint-completed":
                if (_pending is null)
                {
                    return true;
                }
                _pending.Add(payload.Clone());
                _checkpoint = _pending;
                _pending = null;
                _changes.Clear();
                return true;
            case "focus-changed":
            case "selection-changed":
            case "text-control-value-changed":
            case "active-descendant-reference-set":
                _changes.Add((eventType, payload.Clone()));
                return true;
            case "option-selectedness-changed":
                if (Number(payload, "nodeId") is { } option &&
                    payload.TryGetProperty("selected", out var selected) &&
                    selected.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    _optionSelectedness[option] = selected.ValueKind == JsonValueKind.True;
                }
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// The recorded state of one browser document, rebuilt from its records in
/// record order: its DOM tree, its layout state, its interaction state, its
/// listeners and timers, and its accessibility data, with how complete each
/// is and the record that last changed it.
/// </summary>
public sealed class BrowserDocumentState(string key)
{
    /// <summary>The document's key: its token and its document identity.</summary>
    public string Key { get; } = key;

    public string? BrowserInstanceId { get; internal set; }
    public long? ProcessId { get; internal set; }
    public string? PageId { get; internal set; }
    public string? FrameId { get; internal set; }
    public string? DocumentId { get; internal set; }
    public string? DocumentToken { get; internal set; }

    /// <summary>The rebuilt DOM tree, or null while the DOM part has no state.</summary>
    public DomDocumentTree? Dom { get; internal set; }

    public LayoutDocumentChangeState Layout { get; internal set; } = new();

    /// <summary>
    /// The viewport of the document's latest layout checkpoint, or null
    /// before one. Change sets do not record the viewport size.
    /// </summary>
    public RecordedViewport? Viewport { get; internal set; }

    public InteractionDocumentState Interaction { get; internal set; } = new();

    /// <summary>The listeners registered and timers pending in the document.</summary>
    public ScriptDocumentState Script { get; internal set; } = new();

    /// <summary>The latest recorded accessibility data of each DOM node of the document.</summary>
    public AccessibilityDocumentState Accessibility { get; internal set; } = new();

    public BrowserStateCompleteness DomCompleteness { get; internal set; } = BrowserStateCompleteness.NotWalked;
    public BrowserStateCompleteness LayoutCompleteness { get; internal set; } = BrowserStateCompleteness.NotWalked;
    public BrowserStateCompleteness InteractionCompleteness { get; internal set; } = BrowserStateCompleteness.NotWalked;

    /// <summary>
    /// The listener and timer state, and the accessibility state, are
    /// rebuilt from change records alone: complete from the document's first
    /// record, and after a loss of one of their records for the rest of the
    /// document.
    /// </summary>
    public BrowserStateCompleteness ScriptCompleteness { get; internal set; } = BrowserStateCompleteness.Complete;
    public BrowserStateCompleteness AccessibilityCompleteness { get; internal set; } = BrowserStateCompleteness.Complete;

    /// <summary>True once a DOM walk at a finished parse was recorded.</summary>
    public bool FinishedParsing { get; internal set; }

    /// <summary>
    /// True once a DOM walk when the document's parser was created was
    /// recorded (protocol 0.42): the parser's changes after it are recorded,
    /// so the DOM is complete while the document parses.
    /// </summary>
    public bool ParserChangesRecorded { get; internal set; }

    /// <summary>The event key and session time of the document's first and last records.</summary>
    public long FirstEventKey { get; internal set; } = -1;
    public long FirstTime { get; internal set; }
    public long LastEventKey { get; internal set; } = -1;
    public long LastTime { get; internal set; }

    /// <summary>The event key and time of the record that last changed each part, or -1.</summary>
    public long DomEventKey { get; internal set; } = -1;
    public long DomTime { get; internal set; }
    public long LayoutEventKey { get; internal set; } = -1;
    public long LayoutTime { get; internal set; }
    public long InteractionEventKey { get; internal set; } = -1;
    public long InteractionTime { get; internal set; }
}
