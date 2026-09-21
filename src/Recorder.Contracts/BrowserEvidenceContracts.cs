namespace Recorder.Contracts;

public static class BrowserEvidenceProtocol
{
    public const string CurrentVersion = "0.19";
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
    public const string CookieOperation = "cookie-operation";
    public const string Omission = "collector-omission";
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
    BrowserScriptLocation? Location);

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

public sealed record BrowserCookieOperationPayload(
    BrowserContext Context,
    string Operation,
    string Name,
    string? Domain,
    string? Path,
    string? SameSite,
    bool? Secure,
    bool? HttpOnly,
    bool? Partitioned,
    string Source,
    string Result,
    string? BlockedReason);
