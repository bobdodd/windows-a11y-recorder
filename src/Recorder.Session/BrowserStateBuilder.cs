using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// Rebuilds the recorded state of every browser document from its DOM,
/// layout, interaction, listener, timer, and accessibility records, applied
/// in record order (by event key).
/// The DOM tree is rebuilt by <see cref="DomTreeRebuilder"/> and the layout
/// state by <see cref="LayoutDocumentChangeState"/>, the classes the DOM and
/// layout checks compare with each full walk. A document's state can be
/// loaded from a snapshot, and a record whose event key is not after the
/// document's last applied record is then skipped, so records can be
/// replayed from any earlier point.
/// <para>
/// Listener and timer records name their document by its identity and
/// process, without its token, and accessibility records by its token and
/// process, without its identity. They are matched to the document with the
/// DOM records of that identity or token. Those recorded before the
/// document's first DOM record are held until it, and the document's first
/// record is then the first of them.
/// </para>
/// </summary>
public sealed class BrowserStateBuilder
{
    private readonly DomTreeRebuilder _dom = new();
    private readonly Dictionary<string, BrowserDocumentState> _documents = new(StringComparer.Ordinal);
    private readonly List<BrowserDocumentState> _omissionAffected = [];
    private BrowserDocumentState? _applying;
    private long _applyingKey;
    private long _applyingTime;

    // The document key of each identity and each token seen, kept when a
    // document is removed, so a record of a removed document is known as its.
    private readonly Dictionary<string, string> _byIdentity = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byToken = new(StringComparer.Ordinal);

    // Script and accessibility records of documents with no DOM record yet.
    private readonly Dictionary<string, (ScriptDocumentState State, long FirstKey, long FirstTime)> _pendingScript = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (AccessibilityDocumentState State, long FirstKey, long FirstTime)> _pendingAccessibility = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingLost = new(StringComparer.Ordinal);

    public BrowserStateBuilder()
    {
        _dom.CheckpointCompleted = CompleteDomCheckpoint;
    }

    /// <summary>The documents with a record, by document key.</summary>
    public IReadOnlyDictionary<string, BrowserDocumentState> Documents => _documents;

    /// <summary>The records applied, and the records skipped because their document's state was already after them.</summary>
    public long RecordsApplied { get; private set; }

    public long RecordsSkipped { get; private set; }

    /// <summary>The documents the last record applied marked as after a loss, when it was an omission record.</summary>
    public IReadOnlyList<BrowserDocumentState> LastOmissionAffected => _omissionAffected;

    /// <summary>The channels whose records make up a document's state.</summary>
    public static bool IsStateChannel(string channel) =>
        channel is "browser.dom" or "browser.layout" or "browser.interaction" or "browser.presentation" or
            "browser.listener" or "browser.timer" or "browser.accessibility";

    /// <summary>
    /// The key of the document a record of a state channel is of, or null
    /// when it names none, or names one by an identity or token not seen in
    /// a DOM, layout, or interaction record.
    /// </summary>
    public string? KeyOf(string channel, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return channel switch
        {
            "browser.listener" or "browser.timer" =>
                IdentityOf(payload) is { } identity ? _byIdentity.GetValueOrDefault(identity) : null,
            "browser.accessibility" =>
                TokenOf(payload) is { } token ? _byToken.GetValueOrDefault(token) : null,
            _ => DomTreeRebuilder.DocumentKey(payload),
        };
    }

    // A document's identity, "instance process documentId", from a record's
    // context, or null when it names no document identity.
    private static string? IdentityOf(JsonElement payload)
    {
        if (!payload.TryGetProperty("context", out var context) || context.ValueKind != JsonValueKind.Object ||
            Text(context, "documentId") is not { } id)
        {
            return null;
        }
        var (instance, process) = ProcessOf(payload);
        return $"{instance} {process?.ToString(System.Globalization.CultureInfo.InvariantCulture)} {id}";
    }

    private static string? TokenOf(JsonElement payload)
    {
        if (!payload.TryGetProperty("context", out var context) || context.ValueKind != JsonValueKind.Object ||
            Text(context, "documentToken") is not { } token)
        {
            return null;
        }
        var (instance, process) = ProcessOf(payload);
        return $"{instance} {process?.ToString(System.Globalization.CultureInfo.InvariantCulture)} {token}";
    }

    /// <summary>
    /// True while the document is between two records of one change: a DOM
    /// checkpoint or insertion, a layout change set, or an interaction
    /// checkpoint has started and not completed. A snapshot is not taken then.
    /// </summary>
    public bool IsOpen(BrowserDocumentState document) =>
        _dom.IsOpen(document.Key) || document.Layout.IsOpen || document.Interaction.IsOpen ||
        document.Accessibility.IsOpen;

    /// <summary>
    /// Applies one record of a state channel. Returns the document it changed,
    /// or null when it names no document, was skipped, or is on another
    /// channel.
    /// </summary>
    /// <param name="omits">
    /// For an omission record, which documents it can mark: by default,
    /// every document of the process whose last record is before it.
    /// </param>
    public BrowserDocumentState? Apply(
        long eventKey,
        long time,
        string channel,
        string eventType,
        JsonElement payload,
        Func<BrowserDocumentState, bool>? omits = null)
    {
        _omissionAffected.Clear();
        if (!IsStateChannel(channel) || payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (eventType == "collector-omission")
        {
            ApplyOmission(eventKey, channel, payload, omits);
            return null;
        }
        if (channel is "browser.listener" or "browser.timer" or "browser.accessibility")
        {
            return ApplyByReference(eventKey, time, channel, eventType, payload);
        }
        var key = DomTreeRebuilder.DocumentKey(payload);
        if (key is null)
        {
            return null;
        }
        if (!_documents.TryGetValue(key, out var document))
        {
            document = new BrowserDocumentState(key)
            {
                FirstEventKey = eventKey,
                FirstTime = time,
            };
            SetIdentity(document, payload);
            _documents.Add(key, document);
            Adopt(document);
        }
        else if (eventKey <= document.LastEventKey)
        {
            RecordsSkipped++;
            return null;
        }
        RecordsApplied++;
        document.LastEventKey = eventKey;
        document.LastTime = time;
        switch (channel)
        {
            case "browser.dom":
                _applying = document;
                _applyingKey = eventKey;
                _applyingTime = time;
                _dom.Apply(eventType, payload);
                _applying = null;
                document.Dom = _dom.Documents.GetValueOrDefault(key);
                if (eventType.StartsWith("dom-", StringComparison.Ordinal) && document.Dom is not null)
                {
                    document.DomEventKey = eventKey;
                    document.DomTime = time;
                }
                break;
            case "browser.layout":
                ApplyLayout(document, eventKey, time, eventType, payload);
                break;
            case "browser.interaction":
                if (document.Interaction.Apply(eventType, payload))
                {
                    document.InteractionEventKey = eventKey;
                    document.InteractionTime = time;
                    if (eventType == "interaction-checkpoint-completed")
                    {
                        document.InteractionCompleteness = BrowserStateCompleteness.Complete;
                    }
                }
                break;
        }
        return document;
    }

    // A listener, timer, or accessibility record: applied to its document,
    // or held until the document's first DOM record.
    private BrowserDocumentState? ApplyByReference(long eventKey, long time, string channel, string eventType, JsonElement payload)
    {
        var accessibility = channel == "browser.accessibility";
        var reference = accessibility ? TokenOf(payload) : IdentityOf(payload);
        if (reference is null)
        {
            return null;
        }
        var key = (accessibility ? _byToken : _byIdentity).GetValueOrDefault(reference);
        if (key is null)
        {
            if (accessibility)
            {
                if (!_pendingAccessibility.TryGetValue(reference, out var held))
                {
                    held = (new AccessibilityDocumentState(), eventKey, time);
                    _pendingAccessibility.Add(reference, held);
                }
                held.State.Apply(eventKey, time, eventType, payload);
            }
            else
            {
                if (!_pendingScript.TryGetValue(reference, out var held))
                {
                    held = (new ScriptDocumentState(), eventKey, time);
                    _pendingScript.Add(reference, held);
                }
                held.State.Apply(eventKey, time, eventType, payload);
            }
            return null;
        }
        if (!_documents.TryGetValue(key, out var document))
        {
            return null;
        }
        if (eventKey <= document.LastEventKey)
        {
            RecordsSkipped++;
            return null;
        }
        var applied = accessibility
            ? document.Accessibility.Apply(eventKey, time, eventType, payload)
            : document.Script.Apply(eventKey, time, eventType, payload);
        if (!applied)
        {
            return null;
        }
        RecordsApplied++;
        document.LastEventKey = eventKey;
        document.LastTime = time;
        return document;
    }

    // Notes a document's identity and token, and gives it the records held for them.
    private void Register(BrowserDocumentState document)
    {
        var process = document.ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (document.DocumentId is { } id)
        {
            _byIdentity[$"{document.BrowserInstanceId} {process} {id}"] = document.Key;
        }
        if (document.DocumentToken is { } token)
        {
            _byToken[$"{document.BrowserInstanceId} {process} {token}"] = document.Key;
        }
    }

    private void Adopt(BrowserDocumentState document)
    {
        Register(document);
        var process = document.ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (document.DocumentId is { } id &&
            _pendingScript.Remove($"{document.BrowserInstanceId} {process} {id}", out var script))
        {
            document.Script = script.State;
            Earlier(document, script.FirstKey, script.FirstTime);
            if (_pendingLost.Remove("script " + $"{document.BrowserInstanceId} {process} {id}"))
            {
                document.ScriptCompleteness = BrowserStateCompleteness.AfterLoss;
            }
        }
        if (document.DocumentToken is { } token &&
            _pendingAccessibility.Remove($"{document.BrowserInstanceId} {process} {token}", out var accessibility))
        {
            document.Accessibility = accessibility.State;
            Earlier(document, accessibility.FirstKey, accessibility.FirstTime);
            if (_pendingLost.Remove("accessibility " + $"{document.BrowserInstanceId} {process} {token}"))
            {
                document.AccessibilityCompleteness = BrowserStateCompleteness.AfterLoss;
            }
        }
    }

    private static void Earlier(BrowserDocumentState document, long eventKey, long time)
    {
        if (eventKey < document.FirstEventKey)
        {
            document.FirstEventKey = eventKey;
            document.FirstTime = time;
        }
    }

    /// <summary>Notes the insertions still open when the records ended.</summary>
    public void Finish() => _dom.Finish();

    /// <summary>Adds a document read from a snapshot, in place of any state it had.</summary>
    public void Load(BrowserDocumentState document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _documents[document.Key] = document;
        Register(document);
        if (document.Dom is { } tree)
        {
            _dom.SetDocument(document.Key, tree);
        }
    }

    /// <summary>
    /// Forgets a document that is not open, as when its state is kept only as
    /// a snapshot. <see cref="Load"/> adds it again.
    /// </summary>
    public void Remove(string key)
    {
        if (_documents.TryGetValue(key, out var document) && IsOpen(document))
        {
            throw new InvalidOperationException($"Document {key} is part way through a change.");
        }
        _dom.Remove(key);
        _documents.Remove(key);
    }

    /// <summary>Forgets every document.</summary>
    public void Clear()
    {
        foreach (var key in _documents.Keys.ToArray())
        {
            _documents.Remove(key);
        }
        _dom.Clear();
        _byIdentity.Clear();
        _byToken.Clear();
        _pendingScript.Clear();
        _pendingAccessibility.Clear();
        _pendingLost.Clear();
    }

    /// <summary>
    /// The process a record names, as its browser instance and process
    /// identifier, for finding the documents a lost-record notice concerns.
    /// </summary>
    public static (string? Instance, long? Process) ProcessOf(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("context", out var context) ||
            context.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }
        long? process = context.TryGetProperty("processId", out var id) && id.ValueKind == JsonValueKind.Number
            ? id.GetInt64()
            : null;
        return (Text(context, "browserInstanceId"), process);
    }

    private static void SetIdentity(BrowserDocumentState document, JsonElement payload)
    {
        var context = payload.GetProperty("context");
        document.BrowserInstanceId = Text(context, "browserInstanceId");
        document.ProcessId = context.TryGetProperty("processId", out var process) &&
            process.ValueKind == JsonValueKind.Number
                ? process.GetInt64()
                : null;
        document.PageId = Text(context, "pageId");
        document.FrameId = Text(context, "frameId");
        document.DocumentId = Text(context, "documentId");
        document.DocumentToken = Text(context, "documentToken");
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private void ApplyLayout(BrowserDocumentState document, long eventKey, long time, string eventType, JsonElement payload)
    {
        switch (eventType)
        {
            case "layout-changes-started":
            case "layout-transform-node":
            case "layout-node-changed":
            case "layout-scroll-offset-changed":
            case "layout-changes-completed":
                document.Layout.Apply(eventType, payload);
                document.LayoutEventKey = eventKey;
                document.LayoutTime = time;
                if (eventType == "layout-changes-started" &&
                    document.LayoutCompleteness == BrowserStateCompleteness.NotWalked)
                {
                    document.LayoutCompleteness = BrowserStateCompleteness.Complete;
                }
                break;
            case "layout-checkpoint-started":
                if (payload.TryGetProperty("viewport", out var viewport) && viewport.ValueKind == JsonValueKind.Object &&
                    viewport.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number &&
                    viewport.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number &&
                    payload.TryGetProperty("devicePixelRatio", out var ratio) && ratio.ValueKind == JsonValueKind.Number &&
                    payload.TryGetProperty("layoutZoomFactor", out var zoom) && zoom.ValueKind == JsonValueKind.Number)
                {
                    document.Viewport = new RecordedViewport(width.GetDouble(), height.GetDouble(), ratio.GetDouble(), zoom.GetDouble(), time);
                }
                // A layout walk after a lost record states the loss; the
                // change records that follow it lack the lost change.
                if (payload.TryGetProperty("walkReason", out var reason) &&
                    reason.ValueKind == JsonValueKind.String &&
                    reason.GetString() == "after-loss")
                {
                    document.LayoutCompleteness = BrowserStateCompleteness.AfterLoss;
                }
                break;
        }
    }

    // An omission record names the process that lost records on its channel,
    // not the document, so every document of the process whose state is
    // before the record is marked.
    private void ApplyOmission(long eventKey, string channel, JsonElement payload, Func<BrowserDocumentState, bool>? omits)
    {
        if (!payload.TryGetProperty("context", out var context) || context.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        var instance = Text(context, "browserInstanceId");
        long? process = context.TryGetProperty("processId", out var id) && id.ValueKind == JsonValueKind.Number
            ? id.GetInt64()
            : null;
        // Held records of documents with no DOM record yet are marked too.
        var prefix = $"{instance} {process?.ToString(System.Globalization.CultureInfo.InvariantCulture)} ";
        if (channel is "browser.listener" or "browser.timer")
        {
            foreach (var reference in _pendingScript.Keys.Where(item => item.StartsWith(prefix, StringComparison.Ordinal)))
            {
                _pendingLost.Add("script " + reference);
            }
        }
        else if (channel == "browser.accessibility")
        {
            foreach (var reference in _pendingAccessibility.Keys.Where(item => item.StartsWith(prefix, StringComparison.Ordinal)))
            {
                _pendingLost.Add("accessibility " + reference);
            }
        }
        foreach (var document in _documents.Values)
        {
            if (document.BrowserInstanceId != instance || document.ProcessId != process ||
                eventKey <= document.LastEventKey || (omits is not null && !omits(document)))
            {
                continue;
            }
            _omissionAffected.Add(document);
            switch (channel)
            {
                case "browser.dom" when document.DomCompleteness is not BrowserStateCompleteness.NotWalked:
                    document.DomCompleteness = BrowserStateCompleteness.AfterLoss;
                    break;
                case "browser.layout" when document.LayoutCompleteness is not BrowserStateCompleteness.NotWalked:
                    document.LayoutCompleteness = BrowserStateCompleteness.AfterLoss;
                    break;
                case "browser.interaction" when document.InteractionCompleteness is not BrowserStateCompleteness.NotWalked:
                    document.InteractionCompleteness = BrowserStateCompleteness.AfterLoss;
                    break;
                case "browser.listener" or "browser.timer":
                    document.ScriptCompleteness = BrowserStateCompleteness.AfterLoss;
                    break;
                case "browser.accessibility":
                    document.AccessibilityCompleteness = BrowserStateCompleteness.AfterLoss;
                    break;
            }
        }
    }

    private void CompleteDomCheckpoint(string key, DomCheckpointTree checkpoint, DomDocumentTree? rebuilt, bool truncated)
    {
        if (_applying is not { } document)
        {
            return;
        }
        document.DomEventKey = _applyingKey;
        document.DomTime = _applyingTime;
        if (truncated)
        {
            document.DomCompleteness = BrowserStateCompleteness.WalkCut;
            return;
        }
        if (checkpoint.FinishedParsing)
        {
            document.FinishedParsing = true;
        }
        document.DomCompleteness = document.FinishedParsing
            ? BrowserStateCompleteness.Complete
            : BrowserStateCompleteness.Parsing;
    }
}
