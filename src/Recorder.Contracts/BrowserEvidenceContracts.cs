namespace Recorder.Contracts;

public static class BrowserEvidenceProtocol
{
    public const string CurrentVersion = "0.25";
}

public static class BrowserEvidenceChannels
{
    public const string Lifecycle = "browser.lifecycle";
    public const string Listener = "browser.listener";
    public const string Dispatch = "browser.dispatch";
    public const string Timer = "browser.timer";
    public const string Scheduler = "browser.scheduler";
    public const string Navigation = "browser.navigation";
    public const string Dom = "browser.dom";
    public const string Accessibility = "browser.accessibility";
    public const string Cookie = "browser.cookie";
    public const string Interaction = "browser.interaction";
    public const string Layout = "browser.layout";
}

public static class BrowserEvidenceEventTypes
{
    public const string Connected = "browser-connected";
    public const string ClockSynchronized = "browser-clock-synchronized";
    public const string ListenerRegistered = "listener-registered";
    public const string ListenerRemoved = "listener-removed";
    public const string ListenerCallbackReplaced = "listener-callback-replaced";
    public const string DispatchStarted = "dispatch-started";
    public const string ListenerInvoked = "listener-invoked";
    public const string DispatchCompleted = "dispatch-completed";
    public const string DefaultAction = "default-action";
    public const string TimerScheduled = "timer-scheduled";
    public const string TimerFired = "timer-fired";
    public const string TimerCancelled = "timer-cancelled";
    public const string WakeUpDeferred = "wake-up-deferred";
    public const string NavigationStarted = "navigation-started";
    public const string NavigationCompleted = "navigation-completed";
    public const string DomCheckpointStarted = "dom-checkpoint-started";
    public const string DomCheckpointNode = "dom-checkpoint-node";
    public const string DomCheckpointNodeAttribute = "dom-checkpoint-node-attribute";
    public const string DomCheckpointCompleted = "dom-checkpoint-completed";
    public const string DomAttributeChanged = "dom-attribute-changed";
    public const string DomCharacterDataChanged = "dom-character-data-changed";
    public const string AccessibilityCheckpointStarted =
        "accessibility-checkpoint-started";
    public const string AccessibilityCheckpointNode =
        "accessibility-checkpoint-node";
    public const string AccessibilityCheckpointCompleted =
        "accessibility-checkpoint-completed";
    public const string DocumentCookieRead = "document-cookie-read";
    public const string DocumentCookieWrite = "document-cookie-write";
    public const string CookieStoreRequest = "cookie-store-request";
    public const string CookieStoreResult = "cookie-store-result";
    public const string CookieStoreChange = "cookie-store-change";
    public const string CookieAccess = "cookie-access";
    public const string FocusChanged = "focus-changed";
    public const string SelectionChanged = "selection-changed";
    public const string TextControlValueChanged = "text-control-value-changed";
    public const string ActiveDescendantReferenceSet =
        "active-descendant-reference-set";
    public const string LayoutCheckpointStarted = "layout-checkpoint-started";
    public const string LayoutCheckpointNode = "layout-checkpoint-node";
    public const string LayoutCheckpointCompleted = "layout-checkpoint-completed";
    public const string Omission = "collector-omission";
}

// Names the reasons a browser evidence record can be lost. A write that failed
// on the renderer's own pipe is reported by the bridge once the pipe accepts
// again. A record the recorder's event sink refused is reported by the receiver
// once the sink accepts again. Both report how many records were lost, so the
// archive states the loss instead of ending at a gap no reader can see.
public static class BrowserEvidenceOmissionReasons
{
    public const string EvidenceWriteFailed = "browser-evidence-write-failed";
    public const string SinkRefusedRecord = "browser-evidence-sink-refused";
}

public sealed record BrowserContext(
    string BrowserInstanceId,
    int ProcessId,
    string ProcessType,
    string? ProfileId,
    string? BrowserContextId,
    string? PageId,
    string? FrameId,
    string? DocumentId,
    string? ExecutionWorldId,
    string? DocumentToken);

// Reports browser evidence records that were lost rather than written. Context
// is present when the reporter knows which browser process lost them and absent
// when the loss is not attributable to one process.
public sealed record BrowserOmissionPayload(
    string Reason,
    long Count,
    BrowserContext? Context = null);

public static class BrowserEventTargetKinds
{
    public const string Node = "node";
    public const string Window = "window";
    public const string Other = "other";
}

// Describes the EventTarget a listener or dispatch record is about. A Node
// carries a DOM node identifier. A Window or other non-Node EventTarget has
// none, so its NodeId is absent and it is identified by Kind, by the Blink
// interface name, and by a target identifier that is stable for the lifetime of
// the renderer process that reported it.
public sealed record BrowserEventTargetReference(
    string Kind,
    string? InterfaceName,
    string? TargetId,
    string DocumentId,
    long? NodeId,
    string? BackendNodeId,
    string? TagName,
    string? ElementId,
    IReadOnlyList<string> Classes);

public static class BrowserExecutionWorldKinds
{
    public const string Main = "main";
    public const string Isolated = "isolated";
    public const string InspectorIsolated = "inspector-isolated";
    public const string WorkerOrWorklet = "worker-or-worklet";
    public const string ShadowRealm = "shadow-realm";
    public const string Other = "other";
}

// Describes the JavaScript world a recorded listener callback belongs to, as
// Blink reported it at the hook. BlinkWorldId is Blink's own per-thread world
// identifier, where zero is the main world. Name and StableId are the names an
// embedder or the inspector gave a world that is not the main world, and each
// is absent when Blink holds none. A listener Blink installed itself has no
// world, which a record reports by carrying no world at all rather than by
// naming one.
public sealed record BrowserExecutionWorld(
    string Kind,
    int BlinkWorldId,
    string? Name,
    string? StableId);

public sealed record BrowserScriptLocation(
    string? ScriptId,
    string? Url,
    int? Line,
    int? Column,
    string? FunctionName,
    string? SourceHash);

public sealed record BrowserListenerPayload(
    BrowserContext Context,
    string ListenerId,
    string EventName,
    string RegistrationKind,
    BrowserEventTargetReference Target,
    bool Capture,
    bool Passive,
    bool Once,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

public sealed record BrowserDispatchPayload(
    BrowserContext Context,
    string DispatchId,
    string EventName,
    bool Trusted,
    BrowserEventTargetReference? OriginalTarget,
    IReadOnlyList<BrowserEventTargetReference> ComposedPath,
    string Phase,
    string? ListenerId,
    bool DefaultPrevented,
    bool PropagationStopped,
    bool ImmediatePropagationStopped,
    string? DefaultAction,
    string? Outcome,
    BrowserEventTargetReference? CurrentTarget = null);

public sealed record BrowserTimerPayload(
    BrowserContext Context,
    string TimerId,
    string TimerKind,
    double? RequestedDelayMilliseconds,
    double? EffectiveDelayMilliseconds,
    int NestingLevel,
    bool? Throttled,
    string PageLifecycleState,
    BrowserScriptLocation? CallbackLocation,
    string? CancellationReason,
    bool? DidTimeout);

public sealed record BrowserSchedulerPayload(
    BrowserContext Context,
    string QueueName,
    int QueueType,
    string ThrottlingType,
    string DesiredWakeUpTicks,
    string AllowedWakeUpTicks,
    double DeferralMilliseconds,
    bool HasReadyTask,
    string BlockType,
    string DecisionBoundary);

public sealed record BrowserNavigationPayload(
    BrowserContext Context,
    string? ParentFrameId,
    string? ParentOrOuterDocumentFrameId,
    string FrameType,
    bool PrimaryPage,
    string NavigationId,
    string Url,
    string NavigationKind,
    bool RendererInitiated,
    bool SameDocument,
    bool? Committed,
    bool? ErrorPage,
    int? NetErrorCode,
    string? Outcome,
    int? RendererProcessId);

public sealed record BrowserDomCheckpointStartedPayload(
    BrowserContext Context,
    string CheckpointId,
    string Reason,
    int MaximumNodes);

public sealed record BrowserDomCheckpointNodePayload(
    BrowserContext Context,
    string CheckpointId,
    int NodeIndex,
    long NodeId,
    long? ParentNodeId,
    string NodeType,
    string NodeName);

public sealed record BrowserDomCheckpointNodeAttributePayload(
    BrowserContext Context,
    string CheckpointId,
    long NodeId,
    int AttributeIndex,
    string? AttributeNamespace,
    string AttributeName,
    string AttributeValue,
    int AttributeValueLength,
    bool AttributeValueTruncated,
    int MaximumValueLength);

public sealed record BrowserDomCheckpointCompletedPayload(
    BrowserContext Context,
    string CheckpointId,
    string Reason,
    int NodeCount,
    bool Truncated,
    int MaximumNodes,
    int AttributeCount,
    bool AttributesTruncated,
    int MaximumAttributesPerNode,
    int MaximumValueLength,
    int CoveredTransitionCount,
    string? CoveredTransitionFirstId,
    string? CoveredTransitionLastId);

public sealed record BrowserDomAttributeChangedPayload(
    BrowserContext Context,
    string TransitionId,
    long NodeId,
    string NodeName,
    string? AttributeNamespace,
    string AttributeName,
    string ChangeType,
    string? AttributeValue,
    int? AttributeValueLength,
    bool AttributeValueTruncated,
    string? PreviousAttributeValue,
    int? PreviousAttributeValueLength,
    bool PreviousAttributeValueTruncated,
    int MaximumValueLength);

public sealed record BrowserDomCharacterDataChangedPayload(
    BrowserContext Context,
    string TransitionId,
    long NodeId,
    long? ParentNodeId,
    string NodeType,
    string Text,
    int TextLength,
    bool TextTruncated,
    string PreviousText,
    int PreviousTextLength,
    bool PreviousTextTruncated,
    int MaximumValueLength);

public sealed record BrowserAccessibilityCheckpointStartedPayload(
    BrowserContext Context,
    string CheckpointId,
    string Reason,
    int MaximumNodes,
    int UpdateCount,
    int EventCount);

public sealed record BrowserAccessibilityCheckpointNodePayload(
    BrowserContext Context,
    string CheckpointId,
    int NodeIndex,
    int AccessibilityNodeId,
    int? ParentAccessibilityNodeId,
    int? DomNodeId,
    int Role,
    string RoleName,
    string Name,
    string Description,
    string SerializedProperties,
    bool Focused);

public sealed record BrowserAccessibilityCheckpointCompletedPayload(
    BrowserContext Context,
    string CheckpointId,
    string Reason,
    int NodeCount,
    bool Truncated,
    int MaximumNodes,
    int UpdateCount,
    int EventCount);

// Cookie records carry cookie names and non-value attributes only. No cookie
// payload has a field that could hold a cookie value, so a value cannot enter
// the archive through these shapes; the validator also refuses any payload
// that carries a field these shapes do not declare.

// Names the outcomes of a document.cookie read or write as Blink reached them.
public static class BrowserDocumentCookieOutcomes
{
    public const string Returned = "returned";
    public const string SentToCookieManager = "sent-to-cookie-manager";
    public const string NoCookieUrl = "not-attempted-no-cookie-url";
    public const string CookieManagerCallFailed = "cookie-manager-call-failed";
    public const string CookiesDisabled = "refused-no-window-or-cookies-disabled";
    public const string SecurityError = "refused-security-error";
}

// Records one document.cookie read. ServedFrom is null when the read did not
// reach the renderer cookie cache or the cookie manager.
public sealed record BrowserDocumentCookieReadPayload(
    BrowserContext Context,
    string AccessId,
    string? CookieUrl,
    string Outcome,
    string? ServedFrom,
    int CookieCount,
    IReadOnlyList<string> CookieNames,
    bool CookieNamesTruncated,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

// The attributes script wrote with a cookie. Domain, Path, and SameSite are
// null when not written. The document.cookie-only flags are null on a Cookie
// Store API write, which cannot set them.
public sealed record BrowserCookieWriteAttributes(
    string? Domain,
    string? Path,
    string? SameSite,
    bool Partitioned,
    bool ExpiresPresent,
    bool? Secure = null,
    bool? HttpOnly = null,
    bool? MaxAgePresent = null,
    IReadOnlyList<string>? AttributeNames = null);

// Records one document.cookie assignment. Whether the cookie manager stored
// the cookie is reported by the browser-process cookie-access record.
public sealed record BrowserDocumentCookieWritePayload(
    BrowserContext Context,
    string AccessId,
    string? CookieUrl,
    string Outcome,
    string Name,
    BrowserCookieWriteAttributes Attributes,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

public static class BrowserCookieStoreMethods
{
    public const string Get = "get";
    public const string GetAll = "getAll";
    public const string Set = "set";
    public const string Delete = "delete";
}

// Records one Cookie Store API call. Name and Url are the call's filters for a
// read and the cookie name for a write. Attributes are null for a read.
public sealed record BrowserCookieStoreRequestPayload(
    BrowserContext Context,
    string RequestId,
    string Method,
    string ContextKind,
    string Outcome,
    string? Name,
    string? Url,
    BrowserCookieWriteAttributes? Attributes,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

// Records the cookie manager's reply to a Cookie Store API call. A read reports
// the names the cookie manager returned, of which get resolves only the first
// to script; a write reports whether it succeeded.
public sealed record BrowserCookieStoreResultPayload(
    BrowserContext Context,
    string RequestId,
    string Method,
    string Outcome,
    bool? Success,
    int? CookieCount,
    IReadOnlyList<string>? CookieNames,
    bool? CookieNamesTruncated);

// Records one cookie change reported to a CookieStore with change listeners.
public sealed record BrowserCookieStoreChangePayload(
    BrowserContext Context,
    string ContextKind,
    string Name,
    string Domain,
    string Path,
    string Cause,
    bool Dispatched);

// One cookie in a browser-process cookie access notification. A Set-Cookie
// line Chromium could not parse reports its name and inclusion only, with the
// attribute fields null. Reason names are Chromium's own.
public sealed record BrowserCookieAccessEntry(
    string Name,
    bool Parsed,
    string? Domain,
    string? Path,
    string? SameSite,
    bool? Secure,
    bool? HttpOnly,
    bool? HostOnly,
    bool? Partitioned,
    bool? Persistent,
    bool? Expired,
    bool Included,
    IReadOnlyList<string> ExclusionReasons,
    IReadOnlyList<string> WarningReasons,
    string? ExemptionReason);

// Records one cookie access notification the network service sent to the
// browser, observed for a committed frame document or for a navigation.
public sealed record BrowserCookieAccessPayload(
    BrowserContext Context,
    string Observer,
    string? NavigationId,
    int? RendererProcessId,
    string AccessType,
    string Url,
    string? FrameOrigin,
    string? TopFrameOrigin,
    string? RequestId,
    bool AdTagged,
    int CookieCount,
    IReadOnlyList<BrowserCookieAccessEntry> Cookies,
    bool CookiesTruncated);

// Interaction-state records report focus, selection, text-control values, and
// element-reflected active descendants as Blink holds them once a change is
// committed. Node identities are Blink DOM node ids, the same identities DOM
// checkpoint and mutation records carry. Location and World report the script
// that made the change, and are null for a change no script made.

// Names how one focus change ended, derived from the requested and the
// resulting focused node.
public static class BrowserFocusOutcomes
{
    public const string Focused = "focused";
    public const string Cleared = "cleared";
    public const string Redirected = "redirected";
    public const string NotFocused = "not-focused";
}

// Records the outcome of one request to change the focused element of a
// document. PreviousNodeId, RequestedNodeId, and FocusedNodeId are null when no
// element held or was given focus. ActiveDescendantNodeId is the element the
// focused element's aria-activedescendant resolved to at that moment, and is
// null when none resolved or nothing is focused. FocusVisible is null when the
// request did not state it.
public sealed record BrowserFocusChangedPayload(
    BrowserContext Context,
    int? PreviousNodeId,
    int? RequestedNodeId,
    int? FocusedNodeId,
    string Outcome,
    int? ActiveDescendantNodeId,
    string FocusType,
    string FocusTrigger,
    bool PreventScroll,
    bool? FocusVisible,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

// Records the selection a frame holds once a set-selection call is committed.
// The anchor and focus are container nodes and offsets in the DOM tree and are
// null for no selection. The text-control fields are null unless the anchor is
// inside a text control, and then report the control's own selection offsets.
public sealed record BrowserSelectionChangedPayload(
    BrowserContext Context,
    string SetBy,
    string SelectionType,
    int? AnchorNodeId,
    int? AnchorOffset,
    int? FocusNodeId,
    int? FocusOffset,
    bool Directional,
    int? TextControlNodeId,
    int? TextControlSelectionStart,
    int? TextControlSelectionEnd,
    string? TextControlSelectionDirection,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

// Records a text control's value after a value set or a user edit changed it.
// The value is bounded to MaximumValueLength UTF-16 code units; ValueLength
// reports the full length.
public sealed record BrowserTextControlValueChangedPayload(
    BrowserContext Context,
    int NodeId,
    string ControlType,
    string Source,
    string Value,
    int ValueLength,
    bool ValueTruncated,
    int MaximumValueLength,
    int SelectionStart,
    int SelectionEnd,
    string SelectionDirection,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

// Records an element set as an element's aria-activedescendant through element
// reflection, which leaves the referenced element out of the attribute state.
public sealed record BrowserActiveDescendantReferenceSetPayload(
    BrowserContext Context,
    int NodeId,
    int ReferencedNodeId,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

// Layout checkpoint records report the geometry and a defined list of computed
// styles Blink already held for a document once a rendering update reached the
// paint-clean state. Viewport, scroll, and rectangle values are CSS pixels.
// Node identities are Blink DOM node ids, the same identities DOM checkpoint
// records carry.

public sealed record BrowserLayoutSize(double Width, double Height);

public sealed record BrowserLayoutPoint(double X, double Y);

public sealed record BrowserLayoutRect(double X, double Y, double Width, double Height);

// Starts one layout checkpoint. StyleResolutionCount and LayoutCount are
// Blink's cumulative counters for the document and its frame view, and
// PreviousCheckpointId names the document's previous layout checkpoint, which
// is null for the first. StyleProperties lists the computed-style properties
// every element record reports, in order.
public sealed record BrowserLayoutCheckpointStartedPayload(
    BrowserContext Context,
    string CheckpointId,
    string Reason,
    string? PreviousCheckpointId,
    int StyleResolutionCount,
    int LayoutCount,
    BrowserLayoutSize Viewport,
    BrowserLayoutPoint ScrollOffset,
    double DevicePixelRatio,
    double LayoutZoomFactor,
    int MaximumNodes,
    IReadOnlyList<string> StyleProperties);

// Records one element or laid-out text node. BoundingClientRect is null when
// the node has no layout object. ComputedStyle maps each listed property to its
// resolved value, or to null when Blink produced none, and is null for a text
// node or an element without a current computed style.
public sealed record BrowserLayoutCheckpointNodePayload(
    BrowserContext Context,
    string CheckpointId,
    int NodeIndex,
    long NodeId,
    string NodeType,
    string NodeName,
    bool LayoutObjectPresent,
    bool DisplayLocked,
    BrowserLayoutRect? BoundingClientRect,
    IReadOnlyDictionary<string, string?>? ComputedStyle);

public sealed record BrowserLayoutCheckpointCompletedPayload(
    BrowserContext Context,
    string CheckpointId,
    string Reason,
    int NodeCount,
    bool Truncated,
    int MaximumNodes);
