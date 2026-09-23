namespace Recorder.Contracts;

public static class BrowserEvidenceProtocol
{
    public const string CurrentVersion = "0.23";
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
