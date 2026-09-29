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
    /// missing. DOM only.
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

    /// <summary>The start, text control, and completion records of the latest completed checkpoint.</summary>
    public IReadOnlyList<JsonElement> Checkpoint => _checkpoint;

    /// <summary>The focus, selection, value, and active descendant changes after it.</summary>
    public IReadOnlyList<(string EventType, JsonElement Payload)> Changes => _changes;

    /// <summary>True between a checkpoint's start record and its completion.</summary>
    public bool IsOpen => _pending is not null;

    internal void Load(IEnumerable<JsonElement> checkpoint, IEnumerable<(string EventType, JsonElement Payload)> changes)
    {
        _pending = null;
        _checkpoint = [.. checkpoint.Select(record => record.Clone())];
        _changes.Clear();
        _changes.AddRange(changes.Select(change => (change.EventType, change.Payload.Clone())));
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
            default:
                return false;
        }
    }
}

/// <summary>
/// The recorded state of one browser document, rebuilt from its records in
/// record order: its DOM tree, its layout state, and its interaction state,
/// with how complete each is and the record that last changed it.
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

    public InteractionDocumentState Interaction { get; internal set; } = new();

    public BrowserStateCompleteness DomCompleteness { get; internal set; } = BrowserStateCompleteness.NotWalked;
    public BrowserStateCompleteness LayoutCompleteness { get; internal set; } = BrowserStateCompleteness.NotWalked;
    public BrowserStateCompleteness InteractionCompleteness { get; internal set; } = BrowserStateCompleteness.NotWalked;

    /// <summary>True once a DOM walk at a finished parse was recorded.</summary>
    public bool FinishedParsing { get; internal set; }

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
