namespace Recorder.Contracts;

public static class BrowserEvidenceProtocol
{
    public const string CurrentVersion = "0.16";
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
    public const string Cookie = "browser.cookie";
}

public static class BrowserEvidenceEventTypes
{
    public const string Connected = "browser-connected";
    public const string ClockSynchronized = "browser-clock-synchronized";
    public const string ListenerRegistered = "listener-registered";
    public const string ListenerRemoved = "listener-removed";
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

public sealed record BrowserNodeReference(
    string DocumentId,
    long NodeId,
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
    BrowserNodeReference Target,
    bool Capture,
    bool Passive,
    bool Once,
    BrowserScriptLocation? Location);

public sealed record BrowserDispatchPayload(
    BrowserContext Context,
    string DispatchId,
    string EventName,
    bool Trusted,
    BrowserNodeReference? OriginalTarget,
    IReadOnlyList<BrowserNodeReference> ComposedPath,
    string Phase,
    string? ListenerId,
    bool DefaultPrevented,
    bool PropagationStopped,
    bool ImmediatePropagationStopped,
    string? DefaultAction,
    string? Outcome,
    BrowserNodeReference? CurrentTarget = null);

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
    int MaximumValueLength);

public sealed record BrowserDomAttributeChangedPayload(
    BrowserContext Context,
    string? CheckpointId,
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
    string? CheckpointId,
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
