namespace Recorder.Contracts;

public static class BrowserEvidenceProtocol
{
    public const string CurrentVersion = "0.50";
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
    public const string Presentation = "browser.presentation";
    public const string Network = "browser.network";
    public const string Resources = "browser.resources";
    public const string Compositor = "browser.compositor";
}

public static class BrowserEvidenceEventTypes
{
    public const string Connected = "browser-connected";
    public const string Exited = "browser-exited";
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
    public const string DomCheckpointNodeCharacterData = "dom-checkpoint-node-character-data";
    public const string DomCheckpointShadowRoot = "dom-checkpoint-shadow-root";
    public const string DomCheckpointSlotAssignment = "dom-checkpoint-slot-assignment";
    public const string DomCheckpointCompleted = "dom-checkpoint-completed";
    public const string DomAttributeChanged = "dom-attribute-changed";
    public const string DomCharacterDataChanged = "dom-character-data-changed";
    public const string DomNodeInserted = "dom-node-inserted";
    public const string DomInsertedNode = "dom-inserted-node";
    public const string DomInsertedNodeAttribute = "dom-inserted-node-attribute";
    public const string DomInsertedNodeCharacterData = "dom-inserted-node-character-data";
    public const string DomInsertedShadowRoot = "dom-inserted-shadow-root";
    public const string DomInsertedSlotAssignment = "dom-inserted-slot-assignment";
    public const string DomInsertionCompleted = "dom-insertion-completed";
    public const string DomNodeRemoved = "dom-node-removed";
    public const string DomChildrenRemoved = "dom-children-removed";
    public const string DomShadowRootChanged = "dom-shadow-root-changed";
    public const string DomSlotAssignmentChanged = "dom-slot-assignment-changed";
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
    public const string PagePopupOpened = "page-popup-opened";
    public const string PagePopupWindowRect = "page-popup-window-rect";
    public const string PagePopupClosed = "page-popup-closed";
    public const string PopupWidgetCreated = "popup-widget-created";
    public const string PopupWidgetShown = "popup-widget-shown";
    public const string PopupWidgetBoundsRequested =
        "popup-widget-bounds-requested";
    public const string PopupWidgetScreenRects = "popup-widget-screen-rects";
    public const string PopupWidgetHidden = "popup-widget-hidden";
    public const string OptionSelectednessChanged = "option-selectedness-changed";
    public const string InteractionCheckpointStarted =
        "interaction-checkpoint-started";
    public const string InteractionCheckpointTextControl =
        "interaction-checkpoint-text-control";
    public const string InteractionCheckpointCompleted =
        "interaction-checkpoint-completed";
    public const string LayoutCheckpointStarted = "layout-checkpoint-started";
    public const string LayoutCheckpointNode = "layout-checkpoint-node";
    public const string LayoutCheckpointCompleted = "layout-checkpoint-completed";
    public const string LayoutChangesStarted = "layout-changes-started";
    public const string LayoutTransformNode = "layout-transform-node";
    public const string LayoutNodeChanged = "layout-node-changed";
    public const string LayoutScrollOffsetChanged = "layout-scroll-offset-changed";
    public const string LayoutChangesCompleted = "layout-changes-completed";
    public const string PresentationRequested = "presentation-requested";
    public const string PresentationNotSwapped = "presentation-not-swapped";
    public const string PresentationSwapped = "presentation-swapped";
    public const string PresentationFeedback = "presentation-feedback";
    public const string CompositorAnimationStarted = "compositor-animation-started";
    public const string CompositorAnimationEnded = "compositor-animation-ended";
    public const string CompositorFrame = "compositor-frame";
    public const string CompositorFramePresented = "compositor-frame-presented";
    public const string PaintWorkletPainted = "paint-worklet-painted";
    public const string FontFile = "font-file";
    public const string FontFaceAdded = "font-face-added";
    public const string FontFaceLoaded = "font-face-loaded";
    public const string FontFaceRemoved = "font-face-removed";
    public const string ImageResource = "image-resource";
    public const string ImageData = "image-data";
    public const string ImagePaintImage = "image-paint-image";
    public const string NetworkRequestWillBeSent = "request-will-be-sent";
    public const string NetworkResponseReceived = "response-received";
    public const string NetworkRequestFinished = "request-finished";
    public const string NetworkRequestFailed = "request-failed";
    public const string NetworkMemoryCacheHit = "memory-cache-hit";
    public const string NetworkRequestHeadersSent = "request-headers-sent";
    public const string NetworkResponseHeadersReceived = "response-headers-received";
    public const string NetworkNavigationResponse = "navigation-response";
    public const string NetworkWebSocketCreated = "websocket-created";
    public const string NetworkWebSocketHandshakeRequest = "websocket-handshake-request";
    public const string NetworkWebSocketHandshakeResponse = "websocket-handshake-response";
    public const string NetworkWebSocketMessageSent = "websocket-message-sent";
    public const string NetworkWebSocketMessageReceived = "websocket-message-received";
    public const string NetworkWebSocketCloseRequested = "websocket-close-requested";
    public const string NetworkWebSocketError = "websocket-error";
    public const string NetworkWebSocketClosed = "websocket-closed";
    public const string NetworkEventSourceMessage = "event-source-message";
    public const string NetworkWebTransportCreated = "web-transport-created";
    public const string NetworkWebTransportEstablished = "web-transport-established";
    public const string NetworkWebTransportCloseRequested = "web-transport-close-requested";
    public const string NetworkWebTransportClosed = "web-transport-closed";
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
    public const string CrashReportCopyFailed =
        "browser-crash-report-copy-failed";
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
// the renderer process that reported it. DocumentId is null only for a
// non-Node target in a worker or worklet scope, which belongs to no document.
public sealed record BrowserEventTargetReference(
    string Kind,
    string? InterfaceName,
    string? TargetId,
    string? DocumentId,
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
    BrowserExecutionWorld? World,
    BrowserNetworkScope? Scope = null);

// Describes the tree scope one composed path entry is dispatched in, at the
// same index as that entry. TreeScopeRootNodeId is the document or shadow root
// that roots the scope, and is null for the window entry. ShadowRootMode is
// null unless the root is a shadow root. TargetNodeId and RelatedTargetNodeId
// are the target and related target Blink retargeted for the scope, and are
// null when absent or not a node. VisiblePathIndexes lists, in order, the
// composed path indexes that composedPath() returns to a listener in this
// scope; UnmatchedVisibleTargetCount counts entries Blink returned that are not
// in the recorded path.
public sealed record BrowserDispatchPathScope(
    long? TreeScopeRootNodeId,
    string? ShadowRootMode,
    long? TargetNodeId,
    long? RelatedTargetNodeId,
    IReadOnlyList<int> VisiblePathIndexes,
    int UnmatchedVisibleTargetCount);

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
    BrowserEventTargetReference? CurrentTarget = null,
    IReadOnlyList<BrowserDispatchPathScope>? PathScopes = null,
    BrowserNetworkScope? Scope = null);

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

// WalkReason (protocol 0.35) says why the document was walked: "first" when it
// had no walk yet, "after-loss" when a DOM record was lost since its last
// walk, "check" at the recording's full walk interval, or "started-parsing"
// (protocol 0.42) or "finished-parsing" for the start or the end of a parse,
// which is always walked. Reason is "started-parsing", "finished-parsing", or
// "post-mutation".
public sealed record BrowserDomCheckpointStartedPayload(
    BrowserContext Context,
    string CheckpointId,
    string Reason,
    string WalkReason,
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

// Records the data of a text, comment, CDATA section, or processing instruction
// node, after its node record in a DOM checkpoint (protocol 0.33). DataLength
// is the full length in UTF-16 code units.
public sealed record BrowserDomCheckpointNodeCharacterDataPayload(
    BrowserContext Context,
    string CheckpointId,
    long NodeId,
    string Data,
    int DataLength,
    bool DataTruncated,
    int MaximumValueLength);

// Records the shadow root that follows its host in a DOM checkpoint. Mode is
// "open", "closed", or "user-agent"; SlotAssignment is "named" or "manual".
// ReferenceTarget is null when the root has none.
public sealed record BrowserDomCheckpointShadowRootPayload(
    BrowserContext Context,
    string CheckpointId,
    long NodeId,
    long HostNodeId,
    string Mode,
    bool DelegatesFocus,
    string SlotAssignment,
    bool Clonable,
    bool Serializable,
    bool Declarative,
    bool AvailableToElementInternals,
    string? ReferenceTarget);

// Records the nodes one slot is assigned, in order, as Blink held them when the
// checkpoint read them. AssignmentCurrent is false when Blink had marked the
// assignment for recalculation, which the recorder never requests.
public sealed record BrowserDomCheckpointSlotAssignmentPayload(
    BrowserContext Context,
    string CheckpointId,
    long NodeId,
    IReadOnlyList<long?> AssignedNodeIds,
    int AssignedNodeCount,
    bool AssignedNodesTruncated,
    int MaximumAssignedNodes,
    bool AssignmentCurrent);

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
    string? CoveredTransitionLastId,
    int ShadowRootCount = 0,
    int SlotCount = 0,
    int CharacterDataCount = 0);

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

// Structural DOM changes (protocol 0.34), recorded as DOM transitions in the
// order Blink makes them, from the time a document's finished-parsing
// checkpoint is recorded, and from protocol 0.42 from its started-parsing
// checkpoint, the parser's changes included. An insertion names its container, the inserted node,
// and the node's previous sibling when it was recorded, or null for none.
// InsertionKind is "child", or "shadow-root" for a shadow root attached to a
// connected host, which has no previous sibling. The inserted subtree follows
// in dom-inserted-* records whose InsertionId is the insertion's TransitionId,
// in the order a DOM checkpoint walks a document, and a
// dom-insertion-completed record closes it with its counts.
public sealed record BrowserDomNodeInsertedPayload(
    BrowserContext Context,
    string TransitionId,
    string InsertionKind,
    long ContainerNodeId,
    long NodeId,
    long? PreviousSiblingNodeId);

public sealed record BrowserDomInsertedNodePayload(
    BrowserContext Context,
    string InsertionId,
    int NodeIndex,
    long NodeId,
    long ParentNodeId,
    string NodeType,
    string NodeName);

public sealed record BrowserDomInsertedNodeAttributePayload(
    BrowserContext Context,
    string InsertionId,
    long NodeId,
    int AttributeIndex,
    string? AttributeNamespace,
    string AttributeName,
    string AttributeValue,
    int AttributeValueLength,
    bool AttributeValueTruncated,
    int MaximumValueLength);

public sealed record BrowserDomInsertedNodeCharacterDataPayload(
    BrowserContext Context,
    string InsertionId,
    long NodeId,
    string Data,
    int DataLength,
    bool DataTruncated,
    int MaximumValueLength);

public sealed record BrowserDomInsertedShadowRootPayload(
    BrowserContext Context,
    string InsertionId,
    long NodeId,
    long HostNodeId,
    string Mode,
    bool DelegatesFocus,
    string SlotAssignment,
    bool Clonable,
    bool Serializable,
    bool Declarative,
    bool AvailableToElementInternals,
    string? ReferenceTarget);

public sealed record BrowserDomInsertedSlotAssignmentPayload(
    BrowserContext Context,
    string InsertionId,
    long NodeId,
    IReadOnlyList<long?> AssignedNodeIds,
    int AssignedNodeCount,
    bool AssignedNodesTruncated,
    int MaximumAssignedNodes,
    bool AssignmentCurrent);

public sealed record BrowserDomInsertionCompletedPayload(
    BrowserContext Context,
    string InsertionId,
    int NodeCount,
    int AttributeCount,
    int CharacterDataCount,
    int ShadowRootCount,
    int SlotCount);

public sealed record BrowserDomNodeRemovedPayload(
    BrowserContext Context,
    string TransitionId,
    long ContainerNodeId,
    long NodeId);

// Every child of the container was removed.
public sealed record BrowserDomChildrenRemovedPayload(
    BrowserContext Context,
    string TransitionId,
    long ContainerNodeId);

// The state of a connected shadow root after its reference target changed.
public sealed record BrowserDomShadowRootChangedPayload(
    BrowserContext Context,
    string TransitionId,
    long NodeId,
    long HostNodeId,
    string Mode,
    bool DelegatesFocus,
    string SlotAssignment,
    bool Clonable,
    bool Serializable,
    bool Declarative,
    bool AvailableToElementInternals,
    string? ReferenceTarget);

// The nodes assigned to a connected slot after Blink recalculated the slot
// assignments of its shadow root. A recalculation records every slot of the
// root, whether or not its assignment changed.
public sealed record BrowserDomSlotAssignmentChangedPayload(
    BrowserContext Context,
    string TransitionId,
    long NodeId,
    IReadOnlyList<long?> AssignedNodeIds,
    int AssignedNodeCount,
    bool AssignedNodesTruncated,
    int MaximumAssignedNodes);

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

// Page popup records (protocol 0.43). A select drawn as a menu list, and a
// date, time, or colour picker, opens a page popup: a page of its own, with its
// own document and widget. The context names the popup's document. Rectangles
// are in screen DIPs, except the owner's visible bounds, which are in its local
// root.
public sealed record BrowserPagePopupRect(int X, int Y, int Width, int Height);

// Kind is "select-list", "date-time", "color", or "other", from the owner
// element. The anchor and the first window rectangle are those the popup was
// shown with. The owner frame token (protocol 0.44) is the local frame token
// of the owner document's frame, which the browser's popup-widget-created
// record names as its opener.
public sealed record BrowserPagePopupOpenedPayload(
    BrowserContext Context,
    string Kind,
    string OwnerDocumentId,
    string OwnerDocumentToken,
    string OwnerFrameToken,
    int OwnerNodeId,
    BrowserPagePopupRect OwnerVisibleBoundsInLocalRoot,
    BrowserPagePopupRect OwnerLocalRootRectInScreen,
    BrowserPagePopupRect AnchorRectInScreen,
    BrowserPagePopupRect InitialWindowRect,
    double ZoomFactor);

// A window rectangle the popup asked for, deferred when asked for before the
// popup was shown. Where the browser put the window is recorded by the popup
// widget records (protocol 0.44).
public sealed record BrowserPagePopupWindowRectPayload(
    BrowserContext Context,
    bool Deferred,
    BrowserPagePopupRect WindowRect);

// ClosedBy is "renderer" or "browser".
public sealed record BrowserPagePopupClosedPayload(
    BrowserContext Context,
    string ClosedBy);

// Popup widget records (protocol 0.44), made by the browser process. A popup
// widget is named by its frame sink, written clientId:sinkId. The created
// record's context names the opener frame's document, and its opener frame
// token is the page popup's owner frame token; the other records carry the
// browser process's context and join to it by the frame sink. Rectangles are
// in screen DIPs, except the native window's rectangles, which are in screen
// pixels as Windows holds them.
public sealed record BrowserPopupWidgetCreatedPayload(
    BrowserContext Context,
    int? RendererProcessId,
    string OpenerFrameToken,
    string FrameSinkId);

// Outcome is "shown", or the reason WebContentsImpl::ShowCreatedWidget refused
// the popup: "window-not-active", "not-visible", or "permission-exclusion". A
// rectangle not reached before a refusal is null; the view's bounds are
// present only for a shown popup.
public sealed record BrowserPopupWidgetShownPayload(
    BrowserContext Context,
    string FrameSinkId,
    string Outcome,
    BrowserPagePopupRect ReceivedRect,
    BrowserPagePopupRect ReceivedAnchorRect,
    BrowserPagePopupRect? TransformedRect,
    BrowserPagePopupRect? TransformedAnchorRect,
    BrowserPagePopupRect? ConstrainedRect,
    BrowserPagePopupRect? ViewBounds,
    BrowserWindowsAnimationSettings WindowsAnimationSettings);

// The Windows animation settings read with SystemParametersInfo in the
// browser process as a popup window is shown (protocol 0.47), each null when
// the call failed. Which of them governs a desktop compositor transition of
// the window is not documented; they are recorded, not interpreted.
public sealed record BrowserWindowsAnimationSettings(
    bool? ClientAreaAnimation,
    bool? UiEffects,
    bool? MenuAnimation,
    bool? MenuFade,
    bool? ComboBoxAnimation);

// A bounds request from the renderer. The set rectangle is the one given to
// the view after ConstrainPopupBounds and the display clamp, or null when the
// request was ignored while a screen rectangle update was unacknowledged.
public sealed record BrowserPopupWidgetBoundsRequestedPayload(
    BrowserContext Context,
    string FrameSinkId,
    BrowserPagePopupRect RequestedRect,
    BrowserPagePopupRect? SetRect);

// The screen rectangles sent to a popup widget, the native window's rectangle
// and client area when Windows answered, and the view's device scale factor.
public sealed record BrowserPopupWidgetScreenRectsPayload(
    BrowserContext Context,
    string FrameSinkId,
    BrowserPagePopupRect ViewRect,
    BrowserPagePopupRect WindowRect,
    BrowserPagePopupRect? NativeWindowRect,
    BrowserPagePopupRect? NativeClientRect,
    double DeviceScaleFactor);

// A popup widget's view hiding its window (protocol 0.45). Cause is
// "hidden", RenderWidgetHostViewAura::Hide, or "destroyed", the view's
// clean-up before it is destroyed; each is recorded just after the view's
// window was hidden, and only when it had been shown. NativeWindowVisible is
// whether Windows still showed the popup's native window then, or null when
// it has none or Windows did not know it.
public sealed record BrowserPopupWidgetHiddenPayload(
    BrowserContext Context,
    string FrameSinkId,
    string Cause,
    bool? NativeWindowVisible);

// A change of an option's selectedness, which sets no attribute. In an open
// select list the highlighted item is the popup listbox's selected option.
public sealed record BrowserOptionSelectednessChangedPayload(
    BrowserContext Context,
    int NodeId,
    int? SelectNodeId,
    bool Selected,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

// Interaction checkpoint records report the interaction state Blink held for a
// document immediately after a DOM or layout checkpoint completed, read
// without requesting any lifecycle update. SourceCheckpointId names that
// checkpoint and SourceChannel its channel. From protocol 0.35 a snapshot also
// follows a mutation delivery that was not walked, naming no source record,
// and a layout change set recorded without a checkpoint, named by
// SourceChangeSetId. Node identities are Blink DOM node
// ids. FocusedNodeId is the element Blink holds as focused, which may be inside
// a shadow tree, and is not retargeted. The selection positions are null when
// SelectionType is "none".
public sealed record BrowserInteractionCheckpointStartedPayload(
    BrowserContext Context,
    string CheckpointId,
    string? SourceCheckpointId,
    string? SourceChangeSetId,
    string SourceChannel,
    string Reason,
    bool DocumentHasFocus,
    int? FocusedNodeId,
    bool FocusVisible,
    int? ActiveDescendantNodeId,
    string LastFocusType,
    string SelectionType,
    int? AnchorNodeId,
    int? AnchorOffset,
    int? FocusNodeId,
    int? FocusOffset,
    bool Directional,
    int MaximumTextControls,
    int MaximumValueLength);

// Records one text control of an interaction checkpoint in composed-tree
// order. The value is bounded to the start record's MaximumValueLength UTF-16
// code units; ValueLength reports the full length.
public sealed record BrowserInteractionCheckpointTextControlPayload(
    BrowserContext Context,
    string CheckpointId,
    int TextControlIndex,
    int NodeId,
    string ControlType,
    string Value,
    int ValueLength,
    bool ValueTruncated,
    int SelectionStart,
    int SelectionEnd,
    string SelectionDirection);

public sealed record BrowserInteractionCheckpointCompletedPayload(
    BrowserContext Context,
    string CheckpointId,
    int TextControlCount,
    bool Truncated,
    int MaximumTextControls);

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
// every element record reports, in order. WalkReason (protocol 0.35) says why
// the document was walked: "first", "after-loss", or "check".
public sealed record BrowserLayoutCheckpointStartedPayload(
    BrowserContext Context,
    string CheckpointId,
    string Reason,
    string WalkReason,
    string? PreviousCheckpointId,
    int StyleResolutionCount,
    int LayoutCount,
    BrowserLayoutSize Viewport,
    BrowserLayoutPoint ScrollOffset,
    double DevicePixelRatio,
    double LayoutZoomFactor,
    int MaximumNodes,
    IReadOnlyList<string> StyleProperties);

// Describes a pseudo-element record. PseudoType is the name Blink uses for it
// in events, such as "::before". GeneratedText is the text of the layout text
// objects the pseudo-element generated, including nested pseudo-elements.
public sealed record BrowserLayoutPseudoElement(
    long? OriginatingNodeId,
    string PseudoType,
    string GeneratedText,
    int GeneratedTextLength,
    bool GeneratedTextTruncated);

// Box fragments (protocol 0.38). Every length is a Blink layout unit written as
// a number: physical, not logical, and zoomed, so dividing by EffectiveZoom
// gives CSS pixels. A box fragment holds its border-box size, the break token
// it continues from when the box continues in a later fragment, its scrollable
// overflow when it has any, and its child links in order.
public sealed record BrowserLayoutBreakToken(
    double ConsumedBlockSize,
    bool BreakBefore,
    int? SequenceNumber,
    bool AtBlockEnd);

// One child link: Kind is "box" for a box with a DOM node, named by NodeId,
// FragmentIndex naming which of that node's fragments it is; "anonymous",
// "column", or "page" for a box with no node, which holds its own Fragment;
// or "line" for a line box. X and Y are its offset in the parent fragment.
public sealed record BrowserLayoutFragmentChild(
    string Kind,
    double X,
    double Y,
    long? NodeId,
    int? FragmentIndex,
    BrowserLayoutBoxFragment? Fragment);

// The font of a glyph run (protocol 0.39), as Blink's platform font data
// holds it; fonts are recorded by name.
public sealed record BrowserLayoutFont(
    string Family,
    string PostScriptName,
    double Size,
    bool SyntheticBold,
    bool SyntheticItalic);

// A run of glyphs with one font, orientation, and rotation (protocol 0.39).
// Glyphs is base64 of packed little-endian glyphs, 18 bytes each: the glyph
// identifier (2 bytes), the character index into the block's text content (4),
// the total advance before the glyph, and its offset x and y (4-byte floats).
public sealed record BrowserLayoutGlyphRun(
    BrowserLayoutFont Font,
    bool Horizontal,
    int Rotation,
    string Glyphs,
    BrowserLayoutFontFile? FontFile = null);

// The font file of a glyph run's typeface (protocol 0.40): the SHA-256 digest
// of the file's bytes in lowercase hexadecimal, which a font-file record of the
// same renderer holds, the typeface's index in a font collection, and its
// variation position. Null when Skia gave no readable file for the typeface.
public sealed record BrowserLayoutFontFile(
    string Digest,
    int Index,
    IReadOnlyList<BrowserLayoutFontVariation> Variations);

// One variation axis: its four-character OpenType tag, or the tag's number in
// decimal when it is not four printable ASCII characters, and its value.
public sealed record BrowserLayoutFontVariation(string Axis, double Value);

// One glyph of a glyph run, as Blink's shaping reports it.
public readonly record struct BrowserLayoutGlyph(
    ushort Glyph,
    uint CharacterIndex,
    float TotalAdvance,
    float OffsetX,
    float OffsetY);

// Reads a glyph run's packed glyphs.
public static class BrowserLayoutGlyphs
{
    public const int PackedGlyphBytes = 18;

    public static IReadOnlyList<BrowserLayoutGlyph> Unpack(string glyphs)
    {
        var bytes = Convert.FromBase64String(glyphs);
        if (bytes.Length % PackedGlyphBytes != 0)
        {
            throw new FormatException("Glyphs must be whole packed glyphs of 18 bytes.");
        }
        var span = bytes.AsSpan();
        var result = new BrowserLayoutGlyph[bytes.Length / PackedGlyphBytes];
        for (var index = 0; index < result.Length; index++)
        {
            var glyph = span.Slice(index * PackedGlyphBytes, PackedGlyphBytes);
            result[index] = new BrowserLayoutGlyph(
                System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(glyph),
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(glyph[2..]),
                System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(glyph[6..]),
                System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(glyph[10..]),
                System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(glyph[14..]));
        }
        return result;
    }
}

// One fragment item of a block fragment (protocol 0.39), in Blink's pre-order:
// Type is "line", "text", "generated-text", or "box"; the rectangle is in the
// fragment, in layout units. DescendantsCount, for a line or box item, counts
// the item and those it spans. Start and End, for a text item, are its range of
// the block's text content. A text or generated-text item has FirstLineStyle,
// Direction, HiddenForPaint, and GlyphRuns; a generated-text item has
// GeneratedText. Members that do not apply are null.
public sealed record BrowserLayoutFragmentItem(
    string Type,
    double X,
    double Y,
    double Width,
    double Height,
    int? DescendantsCount,
    long? NodeId,
    int? Start,
    int? End,
    bool? FirstLineStyle,
    string? Direction,
    bool? HiddenForPaint,
    IReadOnlyList<BrowserLayoutGlyphRun>? GlyphRuns,
    string? GeneratedText);

// Items (protocol 0.39) is null for a fragment that holds no lines.
// TextContent and FirstLineText are the text a fragment held by a child link,
// such as an anonymous block, lays out, which its items index; a node's own
// fragments leave them null, their text being the node's.
public sealed record BrowserLayoutBoxFragment(
    double Width,
    double Height,
    BrowserLayoutBreakToken? BreakToken,
    BrowserLayoutRect? ScrollableOverflow,
    IReadOnlyList<BrowserLayoutFragmentChild> Children,
    IReadOnlyList<BrowserLayoutFragmentItem>? Items = null,
    string? TextContent = null,
    string? FirstLineText = null);

// A replaced element's natural dimensions, in zoomed CSS pixels as Blink holds
// them.
public sealed record BrowserLayoutNaturalSize(
    double Width,
    double Height,
    bool HasWidth,
    bool HasHeight,
    double AspectRatioWidth,
    double AspectRatioHeight);

// The fragments of a node whose layout object is a layout box. NaturalSize is
// null except for a replaced element. TextContent (protocol 0.39) is a block's
// text as laid out, which its items' ranges index, and FirstLineText its
// ::first-line text when Blink holds one; both are null for a box without
// items. In a change record, TextContentUnchanged states that the text equals
// the node's last record and is left out, both being null.
public sealed record BrowserLayoutBoxFragments(
    double EffectiveZoom,
    IReadOnlyList<BrowserLayoutBoxFragment> Fragments,
    BrowserLayoutNaturalSize? NaturalSize,
    string? TextContent = null,
    string? FirstLineText = null,
    bool TextContentUnchanged = false);

// Records one element, laid-out text node, or pseudo-element.
// BoundingClientRect is null when the node has no layout object. ComputedStyle
// maps each listed property to its resolved value, or to null when Blink
// produced none, and is null for a text node or an element without a current
// computed style. ShadowHostNodeId and ShadowRootMode name the host and mode of
// the shadow tree that contains the node, and are null in a document tree.
// CustomProperties (protocol 0.37) maps each custom property of the computed
// style to its value, and is null when ComputedStyle is; ComputedStyle then
// holds every property getComputedStyle() lists. BoxFragments (protocol
// 0.38) holds the node's box fragments, and is null when its layout object is
// not a layout box.
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
    IReadOnlyDictionary<string, string?>? ComputedStyle,
    BrowserLayoutPseudoElement? PseudoElement = null,
    long? ShadowHostNodeId = null,
    string? ShadowRootMode = null,
    IReadOnlyDictionary<string, string?>? CustomProperties = null,
    BrowserLayoutBoxFragments? BoxFragments = null);

public sealed record BrowserLayoutCheckpointCompletedPayload(
    BrowserContext Context,
    string CheckpointId,
    string Reason,
    int NodeCount,
    bool Truncated,
    int MaximumNodes,
    int PseudoElementCount = 0,
    int ShadowRootCount = 0);

// Layout change records (protocol 0.32). A change set records, for one
// document at the end of a rendering update, the transform nodes and noted
// nodes whose record differs from the last one recorded for them.
// LayoutCheckpointId names the checkpoint recorded for the document since its
// previous change set, if any. ViewTransformNodeId is the transform node of
// the layout view's local border box, and ViewPaintOffset the view's paint
// offset in it, in physical pixels.
// CheckpointUpdate (protocol 0.46) is true when the change set was read for
// the rendering update its named checkpoint recorded, and so is part of the
// walked update presented through that checkpoint.
public sealed record BrowserLayoutChangesStartedPayload(
    BrowserContext Context,
    string ChangeSetId,
    string? LayoutCheckpointId,
    bool CheckpointUpdate,
    string ViewTransformNodeId,
    BrowserLayoutPoint ViewPaintOffset,
    double LayoutZoomFactor);

// One transform node of the paint property tree. Matrix is the node's matrix
// with its transform origin applied, 16 values in column-major order.
// ParentTransformNodeId is null at the view's node and at the tree's root.
public sealed record BrowserLayoutTransformNodePayload(
    BrowserContext Context,
    string ChangeSetId,
    string TransformNodeId,
    string? ParentTransformNodeId,
    IReadOnlyList<double> Matrix,
    bool FlattensInheritedTransform,
    bool ScrollTranslation,
    bool Sticky);

// The geometry of a changed node. LocalRect is the rectangle
// getBoundingClientRect is built from, before its zoom adjustment, mapped into
// the transform node's space; it is null when that rectangle is empty
// (ClientRectEmpty) or could not be mapped (LocalRectMapped false).
// LocalQuadRects (protocol 0.36) holds, when getBoundingClientRect unites more
// than one quad, such as the lines of a text node, the bounds of each quad in
// the transform node's space, and is null otherwise; under a transform that
// rotates or skews, the viewport rectangle is the union of the bounds of each
// mapped quad. ClientRectScale converts the rectangle derived in viewport
// space to CSS pixels.
public sealed record BrowserLayoutNodeGeometry(
    string TransformNodeId,
    BrowserLayoutRect? LocalRect,
    IReadOnlyList<BrowserLayoutRect>? LocalQuadRects,
    bool ClientRectEmpty,
    bool LocalRectMapped,
    double ClientRectScale);

// One noted node whose record changed. Reasons lists why it was noted:
// "style", "layout", or "paint-properties". The node fields are those of a
// checkpoint node record, and Geometry replaces its viewport rectangle.
// ComputedStyleComplete (protocol 0.37) is true when ComputedStyle and
// CustomProperties hold every value, as in a node's first record, and false
// when they hold only the values that changed since the node's last record,
// RemovedCustomProperties then naming the custom properties that record held
// and this one does not. Both are null when ComputedStyle is. BoxFragments
// (protocol 0.38) is always whole, as in a checkpoint node record.
public sealed record BrowserLayoutNodeChangedPayload(
    BrowserContext Context,
    string ChangeSetId,
    IReadOnlyList<string> Reasons,
    long NodeId,
    string NodeType,
    string NodeName,
    bool LayoutObjectPresent,
    bool DisplayLocked,
    BrowserLayoutNodeGeometry? Geometry,
    IReadOnlyDictionary<string, string?>? ComputedStyle,
    BrowserLayoutPseudoElement? PseudoElement = null,
    long? ShadowHostNodeId = null,
    string? ShadowRootMode = null,
    bool? ComputedStyleComplete = null,
    IReadOnlyDictionary<string, string?>? CustomProperties = null,
    IReadOnlyList<string>? RemovedCustomProperties = null,
    BrowserLayoutBoxFragments? BoxFragments = null);

public sealed record BrowserLayoutChangesCompletedPayload(
    BrowserContext Context,
    string ChangeSetId,
    int NotedNodeCount,
    int RecordedNodeCount,
    int UnchangedNodeCount,
    int TransformNodeCount,
    int ScrollOffsetCount = 0);

// A scroller whose scroll offset Blink stored during the rendering update
// (protocol 0.34), read at the end of the update. NodeId is the scroller's
// element, or the document for the frame's own scroller. ScrollOffset is the
// offset Blink holds, in physical pixels; WebExposedScrollOffset is the value
// scrollLeft and scrollTop divide by EffectiveZoom; ScrollOrigin is the
// position of offset zero. ScrollTranslationNodeId is the transform node the
// offset moves, or null when the scroller has none. From protocol 0.49 (slice
// 4b sub-step 2b-ii), ScrollElementId is the scroller's compositor element ID
// as decimal text, which the compositor-frame records name its scroll offset
// by, or null when it has none; it is absent before 0.49.
public sealed record BrowserLayoutScrollOffsetChangedPayload(
    BrowserContext Context,
    string ChangeSetId,
    long NodeId,
    BrowserLayoutPoint ScrollOffset,
    BrowserLayoutPoint WebExposedScrollOffset,
    BrowserLayoutPoint ScrollOrigin,
    double EffectiveZoom,
    string? ScrollTranslationNodeId,
    string? ScrollElementId = null);

// Presentation records follow the compositor frame that carries one layout
// checkpoint's rendering update, or, from protocol 0.35, one layout change
// set's when the update was not walked. A request names the checkpoint or the
// change set, and the local-root widget whose layer tree it rides. At most one terminal record
// follows: a not-swapped record whose action is "broken", or a swapped record
// and then, when viz reports it, feedback for the same frame token. Frame
// tokens are unsigned 32-bit decimal strings numbered per frame sink, and the
// frame sink is written "clientId:sinkId". From protocol 0.43 WidgetKind is
// "frame" for a frame widget or "page-popup" for a page popup's widget, whose
// frame sink the renderer is not told, so its FrameSinkId is null. Every tick field is a decimal
// QueryPerformanceCounter value, the clock the record envelope's native
// timestamp uses, or null when Chromium reported no time or its clock was not
// high resolution.
public sealed record BrowserPresentationRequestedPayload(
    BrowserContext Context,
    string RequestId,
    string? WidgetKind,
    string? FrameSinkId,
    string? LocalRootFrameToken,
    string? LayoutCheckpointId,
    string? LayoutChangeSetId,
    bool Queued,
    string? NotQueuedReason,
    int? SourceFrameNumber,
    bool? IsMainFrameWidget,
    bool HighResolutionTicks,
    int MaximumNotSwappedRecords);

public sealed record BrowserPresentationNotSwappedPayload(
    BrowserContext Context,
    string RequestId,
    string WidgetKind,
    string? FrameSinkId,
    string LocalRootFrameToken,
    string Reason,
    string Action,
    int NotSwappedIndex,
    int NotSwappedCount,
    string? TimestampTicks,
    string? TimestampTimeTicksMicroseconds);

public sealed record BrowserPresentationSwappedPayload(
    BrowserContext Context,
    string RequestId,
    string WidgetKind,
    string? FrameSinkId,
    string LocalRootFrameToken,
    string FrameToken,
    int NotSwappedCount);

public sealed record BrowserPresentationFeedbackPayload(
    BrowserContext Context,
    string RequestId,
    string WidgetKind,
    string? FrameSinkId,
    string LocalRootFrameToken,
    string FrameToken,
    string? PresentedTicks,
    string? PresentedTimeTicksMicroseconds,
    string IntervalMicroseconds,
    IReadOnlyList<string> Flags,
    string? ReceivedCompositorFrameTicks,
    string? DrawStartTicks,
    string? SwapStartTicks,
    string? SwapEndTicks,
    bool HighResolutionTicks,
    int NotSwappedCount);

// Compositor records (protocol 0.48, slice 4b), on browser.compositor. A
// compositor is named by its cc::LayerTreeHost ID, unique in its renderer
// process, and by the widget its presentation requests name (null before
// the first). Element IDs are cc::ElementId values as decimal strings.

// An animation started on the compositor, from Blink's main thread: its
// target node, cc animation ID, and keyframe models.
public sealed record BrowserCompositorAnimationStartedPayload(
    BrowserContext Context,
    int NodeId,
    int? CompositorAnimationId,
    IReadOnlyList<BrowserCompositorKeyframeModel> KeyframeModels);

// One keyframe model: its ID, cc::TargetProperty name, the element ID it
// animates, and that ID's Blink namespace name.
public sealed record BrowserCompositorKeyframeModel(
    int KeyframeModelId,
    string TargetProperty,
    string ElementId,
    string ElementIdNamespace);

// An animation's keyframe models removed from the compositor, cancelled or
// finished.
public sealed record BrowserCompositorAnimationEndedPayload(
    BrowserContext Context,
    int? NodeId,
    int? CompositorAnimationId,
    IReadOnlyList<int> KeyframeModelIds);

// The compositor's widget, as its presentation records name it.
public sealed record BrowserCompositorWidget(
    string WidgetKind,
    string? FrameSinkId,
    string LocalRootFrameToken);

// A submitted compositor frame, from LayerTreeHostImpl::DrawLayers, with the
// active tree's values that changed since the compositor's last recorded
// frame. Value is, by property: transform, the 16 matrix entries row by row;
// opacity, a number; filter and backdrop-filter, the operations;
// scroll-offset, x and y; background-color-progress and
// clip-path-progress, an object whose progress is the compositor progress the
// drawn paint worklet record was painted with, or null when it was painted
// with none; and image-frame, the frame index an animated paint image is
// drawn at. A null value means the element's node or paint worklet is no
// longer in the drawn tree, or the paint image is no longer held by the
// image animation controller.
public sealed record BrowserCompositorFramePayload(
    BrowserContext Context,
    int LayerTreeHostId,
    BrowserCompositorWidget? Widget,
    string FrameToken,
    int SourceFrameNumber,
    string? BeginFrameTicks,
    string? BeginFrameTimeTicksMicroseconds,
    bool HighResolutionTicks,
    IReadOnlyList<BrowserCompositorChange> Changes);

// ElementId names the compositor element, and is null for image-frame, whose
// PaintImageId names the paint image instead.
public sealed record BrowserCompositorChange(
    string? ElementId,
    string Property,
    System.Text.Json.JsonElement Value,
    string? PaintImageId = null);

// A cc::FilterOperation: its type and numbers, as the bridge README states.
public sealed record BrowserCompositorFilterOperation(
    string Type,
    IReadOnlyList<double> Numbers);

// What a native paint worklet painted, on the worklet's thread: the
// compositor element and property (background-color or clip-path), the
// compositor progress it was given, or null, and the value drawn.
public sealed record BrowserPaintWorkletPaintedPayload(
    BrowserContext Context,
    string ElementId,
    string Property,
    double? Progress,
    BrowserPaintWorkletPaintedValue Value);

// A painted background color is Color, four floats (SkColor4f). A painted
// clip path is the SkPath's fill type, verbs, points (x, y, ...), and conic
// weights, the translation the paint applied, and whether it was drawn as a
// rounded rectangle.
public sealed record BrowserPaintWorkletPaintedValue(
    IReadOnlyList<double>? Color,
    string? FillType,
    IReadOnlyList<string>? Verbs,
    IReadOnlyList<double>? Points,
    IReadOnlyList<double>? ConicWeights,
    BrowserPaintWorkletTranslation? Translation,
    bool? DrawnAsRoundedRect);

public sealed record BrowserPaintWorkletTranslation(double X, double Y);

// Viz's presentation of a recorded compositor frame, on the clock of the
// presentation records, or its failure.
public sealed record BrowserCompositorFramePresentedPayload(
    BrowserContext Context,
    int LayerTreeHostId,
    BrowserCompositorWidget? Widget,
    string FrameToken,
    bool Failed,
    string? PresentedTicks,
    string? PresentedTimeTicksMicroseconds,
    bool HighResolutionTicks);

// Page resource records (protocol 0.40), on browser.resources. A font file or
// an image is identified by the SHA-256 digest of its bytes, in lowercase
// hexadecimal, and a renderer records the bytes once for each digest, in a
// font-file or image-data record. Size is the byte count as a decimal string,
// and Bytes the bytes in base64. These records carry no document.
public sealed record BrowserResourceBytesPayload(
    BrowserContext Context,
    string Digest,
    string Size,
    string Bytes);

// A FontFace joining or leaving its document's set of faces. FaceNumber is
// unique in the renderer, as a decimal string.
public sealed record BrowserFontFacePayload(
    BrowserContext Context,
    string FaceNumber);

// A face's descriptors as Blink's FontFace getters serialize them.
public sealed record BrowserFontFaceDescriptors(
    string Style,
    string Weight,
    string Stretch,
    string UnicodeRange,
    string Variant,
    string FeatureSettings,
    string Display,
    string AscentOverride,
    string DescentOverride,
    string LineGapOverride,
    string SizeAdjust);

// The source a face loaded from: "url", with its URL; "data-url", whose bytes
// are the face's font file; "binary", from script; or "local", an installed
// font, for which Blink holds no file.
public sealed record BrowserFontFaceSource(string Kind, string? Url);

// The font file a face loaded, by digest, and its collection index.
public sealed record BrowserFontFaceFontFile(string Digest, int Index);

// A FontFace of a document that finished loading.
public sealed record BrowserFontFaceLoadedPayload(
    BrowserContext Context,
    string FaceNumber,
    string Family,
    BrowserFontFaceDescriptors Descriptors,
    BrowserFontFaceSource? Source,
    BrowserFontFaceFontFile? FontFile);

// An image resource that finished loading: the URL requested, the response's
// URL, status, and MIME type, and the size and digest of its encoded bytes.
// DataRecorded is false when the bytes' image-data record could not be queued.
// ImageId (protocol 0.48) is the Blink image's own ID the bytes were given to,
// as a decimal string, or null when no image was made.
public sealed record BrowserImageResourcePayload(
    BrowserContext Context,
    string Url,
    string? ResponseUrl,
    int Status,
    string MimeType,
    string Size,
    string Digest,
    bool DataRecorded,
    string? ImageId);

// A paint image Blink made from an image (protocol 0.48), the first time the
// renderer made one with its ID: the image's own ID, the paint image's ID,
// its animation sequence (shared, the image's, or own, an element's), the
// node it was made for, or null, and the paint image whose animation it is
// synchronised to, or null. IDs are decimal strings.
public sealed record BrowserImagePaintImagePayload(
    BrowserContext Context,
    string ImageId,
    string PaintImageId,
    string Sequence,
    int? NodeId,
    string? SyncTargetPaintImageId);

// Network records report request and response metadata as the Blink loader and
// the browser's network service observer already hold it. No record carries a
// request or response body. A header value the recorder classifies as a
// credential, including every Cookie and Set-Cookie value, is withheld: its
// entry keeps the name, reports a null value, and names the reason. Cookies a
// request sent or a response set are reported by name and attributes only.
// InspectorId is Blink's per-renderer-process request counter as a decimal
// string. Byte counts and connection ids are JSON numbers, exact up to 2^53.
// Times are milliseconds relative to the record's own request or navigation
// start, and are null when the phase was not observed.

public sealed record BrowserNetworkHeader(
    string Name,
    string? Value,
    bool ValueRedacted,
    string? RedactionReason);

// Names the execution context that issued a renderer request, and from
// protocol 0.31 the context a listener or dispatch record belongs to, so a
// worker's records on both channels carry one WorkerToken. WorkerToken is null
// for a window. GlobalObjectUrl is the worker script URL for a worker.
public sealed record BrowserNetworkScope(
    string ContextKind,
    string? WorkerToken,
    string? GlobalObjectUrl);

public sealed record BrowserNetworkInitiator(
    string? Type,
    string? Url,
    int? Line,
    int? Column,
    bool LinkPreload);

public sealed record BrowserNetworkRequest(
    string InspectorId,
    string? RequestId,
    string Url,
    string Method,
    string ResourceType,
    BrowserNetworkInitiator Initiator,
    bool Internal,
    string Destination,
    string Mode,
    string CredentialsMode,
    string RedirectMode,
    string CacheMode,
    string Priority,
    string InitialPriority,
    string FetchPriorityHint,
    string RenderBlocking,
    string? Referrer,
    string ReferrerPolicy,
    bool Keepalive,
    bool UserGesture,
    bool AdResource,
    bool FormSubmission,
    int HeaderCount,
    IReadOnlyList<BrowserNetworkHeader> Headers,
    bool HeadersTruncated);

public sealed record BrowserNetworkRemoteAddress(string Ip, int Port);

// Load timing phases are milliseconds after the request start, which itself is
// reported as milliseconds before the record was written.
public sealed record BrowserNetworkLoadTiming(
    double? RequestStartBeforeRecordMilliseconds,
    double? ProxyStart,
    double? ProxyEnd,
    double? DomainLookupStart,
    double? DomainLookupEnd,
    double? ConnectStart,
    double? ConnectEnd,
    double? SslStart,
    double? SslEnd,
    double? WorkerStart,
    double? WorkerReady,
    double? WorkerFetchStart,
    double? WorkerRespondWithSettled,
    double? WorkerRouterEvaluationStart,
    double? WorkerCacheLookupStart,
    double? SendStart,
    double? SendEnd,
    double? ReceiveHeadersStart,
    double? ReceiveHeadersEnd,
    double? ReceiveNonInformationalHeadersStart,
    double? ReceiveEarlyHintsStart,
    double? PushStart,
    double? PushEnd,
    double? ResponseEnd);

public sealed record BrowserNetworkResponse(
    string Url,
    string? ResponseUrl,
    int Status,
    string StatusText,
    string MimeType,
    string? Charset,
    string? AlpnProtocol,
    string? ConnectionInfo,
    BrowserNetworkRemoteAddress? RemoteAddress,
    double ConnectionId,
    bool ConnectionReused,
    bool WasCached,
    bool FetchedViaServiceWorker,
    string ServiceWorkerResponseSource,
    bool InPrefetchCache,
    bool NetworkAccessed,
    bool FromArchive,
    bool CookieInRequest,
    string ResponseType,
    double? EncodedDataLength,
    double ExpectedContentLength,
    int HeaderCount,
    IReadOnlyList<BrowserNetworkHeader> Headers,
    bool HeadersTruncated,
    BrowserNetworkLoadTiming? Timing);

// Records a renderer request about to be sent, or a redirect of it, in which
// case RedirectResponse is the redirect response. Location and World report the
// script current when Blink issued the request, and are null for none.
public sealed record BrowserNetworkRequestWillBeSentPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    BrowserNetworkRequest Request,
    bool Redirect,
    BrowserNetworkResponse? RedirectResponse,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

public sealed record BrowserNetworkResponseReceivedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    string? RequestId,
    string ResponseSource,
    BrowserNetworkResponse Response);

public sealed record BrowserNetworkRequestFinishedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    double? EncodedDataLength,
    double DecodedBodyLength,
    double? FinishBeforeRecordMilliseconds);

public sealed record BrowserNetworkCorsError(string Error, string? FailedParameter);

public sealed record BrowserNetworkRequestFailedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    string Url,
    int NetError,
    string? NetErrorName,
    bool Cancellation,
    bool Timeout,
    bool AccessCheck,
    bool BlockedByResponse,
    bool BlockedByOrb,
    bool HasCopyInCache,
    bool CancelledFromHttpError,
    bool Internal,
    string? BlockedReason,
    BrowserNetworkCorsError? CorsError);

// Records a resource Blink served from its in-memory cache without a loader.
public sealed record BrowserNetworkMemoryCacheHitPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    bool StaticData,
    BrowserNetworkRequest Request,
    BrowserNetworkResponse Response);

// Records the headers the network service put on the wire for a request, as
// the browser's network service observer received them. DevtoolsAgentId names
// the worker the observer was made for, and is null for a frame.
public sealed record BrowserNetworkRequestHeadersSentPayload(
    BrowserContext Context,
    string? DevtoolsAgentId,
    string RequestId,
    double? SentBeforeRecordMilliseconds,
    int HeaderCount,
    IReadOnlyList<BrowserNetworkHeader> Headers,
    bool HeadersTruncated,
    int CookieCount,
    IReadOnlyList<BrowserCookieAccessEntry> Cookies,
    bool CookiesTruncated);

public sealed record BrowserNetworkResponseHeadersReceivedPayload(
    BrowserContext Context,
    string? DevtoolsAgentId,
    string RequestId,
    int Status,
    int HeaderCount,
    IReadOnlyList<BrowserNetworkHeader> Headers,
    bool HeadersTruncated,
    int CookieCount,
    IReadOnlyList<BrowserCookieAccessEntry> Cookies,
    bool CookiesTruncated);

public sealed record BrowserNavigationResponseHead(
    int Status,
    string StatusText,
    string? MimeType,
    bool WasCached,
    BrowserNetworkRemoteAddress? RemoteAddress,
    string? ConnectionInfo,
    int HeaderCount,
    IReadOnlyList<BrowserNetworkHeader> Headers,
    bool HeadersTruncated);

// Navigation timing phases are milliseconds after the navigation start, which
// itself is reported as milliseconds before the record was written.
public sealed record BrowserNavigationResponseTiming(
    double? NavigationStartBeforeRecordMilliseconds,
    double? LoaderStart,
    double? FirstRequestStart,
    double? FirstResponseStart,
    double? FirstLoaderCallback,
    double? FinalRequestStart,
    double? FinalResponseStart,
    double? FinalNonInformationalResponseStart,
    double? FinalLoaderCallback,
    double? RequestFailed,
    double? CommitSent,
    double? CommitReceived,
    double? CommitReplySent,
    double? DidCommit,
    double? FinalRequestDomainLookupStart,
    double? FinalRequestDomainLookupEnd,
    double? FinalRequestConnectStart,
    double? FinalRequestConnectEnd,
    double? FinalRequestSslStart);

// Records the request and response of a finished navigation, written with the
// navigation-completed record. Response is null when the navigation received
// none. NavigationId matches the navigation channel records.
public sealed record BrowserNetworkNavigationResponsePayload(
    BrowserContext Context,
    string NavigationId,
    string? RequestId,
    string Url,
    string Method,
    bool Committed,
    bool ErrorPage,
    bool SameDocument,
    bool Download,
    bool BackForwardCache,
    int NetError,
    string? NetErrorName,
    IReadOnlyList<string> RedirectChain,
    int RequestHeaderCount,
    IReadOnlyList<BrowserNetworkHeader> RequestHeaders,
    bool RequestHeadersTruncated,
    BrowserNavigationResponseHead? Response,
    BrowserNavigationResponseTiming? Timing);

// A withheld part of a recorded text. Offset counts UTF-16 code units into the
// recorded text, where the withheld marker stands in for the credential.
public sealed record BrowserNetworkWithheldText(int Offset, string Reason);

// The recordable part of a message, event field, or close reason: text up to
// the recorder's length limit, with each credential-looking part withheld.
public sealed record BrowserNetworkText(
    string Text,
    bool Truncated,
    IReadOnlyList<BrowserNetworkWithheldText> Withheld);

// Records a script creating a WebSocket. InspectorId is the identifier Blink
// gives the channel, shared by every later record for it.
public sealed record BrowserNetworkWebSocketCreatedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    string Url,
    string? RequestedProtocols,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

// Records the opening handshake request as the network service reported it to
// the renderer. CookieNames lists the cookies named in its Cookie header. The
// network service of a recording browser reports that header, and each
// Set-Cookie header of the response, with every value replaced, so the names
// are available without any value reaching the renderer. The list is empty when
// the handshake sent no cookie.
public sealed record BrowserNetworkWebSocketHandshakeRequestPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    string Url,
    IReadOnlyList<string> CookieNames,
    int HeaderCount,
    IReadOnlyList<BrowserNetworkHeader> Headers,
    bool HeadersTruncated);

public sealed record BrowserNetworkWebSocketHandshakeResponsePayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    string? Extensions,
    string? Url,
    string? HttpVersion,
    int Status,
    string? StatusText,
    BrowserNetworkRemoteAddress? RemoteAddress,
    string? SelectedProtocol,
    IReadOnlyList<string> SetCookieNames,
    int HeaderCount,
    IReadOnlyList<BrowserNetworkHeader> Headers,
    bool HeadersTruncated);

// Records one WebSocket message. Payload is null for a binary message, whose
// content is not recorded. Location and World are present only for a message
// a script sent.
public sealed record BrowserNetworkWebSocketMessagePayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    string Opcode,
    double PayloadLength,
    BrowserNetworkText? Payload,
    BrowserScriptLocation? Location = null,
    BrowserExecutionWorld? World = null);

public sealed record BrowserNetworkWebSocketCloseRequestedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    int? Code,
    BrowserNetworkText Reason,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

public sealed record BrowserNetworkWebSocketErrorPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    string Message);

// Records the end of a WebSocket channel. A dropped channel reports how it
// closed; a disconnected one, closed because its context went away, reports
// only the cause.
public sealed record BrowserNetworkWebSocketClosedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    string Cause,
    bool? WasClean,
    int? Code,
    BrowserNetworkText? Reason);

public sealed record BrowserNetworkEventSourceMessagePayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string InspectorId,
    string Url,
    string EventType,
    BrowserNetworkText LastEventId,
    double DataLength,
    BrowserNetworkText Data);

// Records a script creating a WebTransport session. TransportId is a
// recorder-assigned identifier shared by every later record for the session.
public sealed record BrowserNetworkWebTransportCreatedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string TransportId,
    string Url,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

public sealed record BrowserNetworkWebTransportEstablishedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string TransportId,
    double? MaxDatagramSize,
    string? Url,
    string? HttpVersion,
    int Status,
    string? StatusText,
    BrowserNetworkRemoteAddress? RemoteAddress,
    string? SelectedProtocol,
    IReadOnlyList<string> SetCookieNames,
    int HeaderCount,
    IReadOnlyList<BrowserNetworkHeader> Headers,
    bool HeadersTruncated);

public sealed record BrowserNetworkWebTransportCloseRequestedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string TransportId,
    double? Code,
    BrowserNetworkText? Reason,
    BrowserScriptLocation? Location,
    BrowserExecutionWorld? World);

public sealed record BrowserNetworkWebTransportClosedPayload(
    BrowserContext Context,
    BrowserNetworkScope Scope,
    string TransportId,
    bool Abrupt,
    double? Code,
    BrowserNetworkText? Reason);
