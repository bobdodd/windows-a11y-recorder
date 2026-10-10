using System.Text.Json.Serialization;

namespace Recorder.Recreation;

// A page to recreate and the evidence the evidence panel shows with it. A
// recorded page has a script nonce: its builder script is the only script
// the page's content security policy allows.
// The viewport, when recorded, is emulated in the recreation's tab.
public sealed record RecreationContent(string Html, RecreationEvidence Evidence, string? ScriptNonce = null)
{
    public RecreationViewport? Viewport { get; init; }

    // The recorded document's address, when the page is served at it
    // (slice 4a): an absolute http or https URL. The recreation's tab is
    // navigated to it, and the recorder answers its request, and every other
    // request of the tab, itself, so that the page's relative URLs resolve
    // as recorded and nothing reaches the network. Null serves the page from
    // the loopback server, as for fixed content.
    public string? DocumentUrl { get; init; }

    // Sub-step 3: the recording's fonts and images for the page, and the
    // recorder's own address the builder reads the font files from. The
    // server that holds the recreation disposes the resources.
    public Recorder.Session.RecordedPageResources? Resources { get; init; }

    // Slice 4h: the document's scripts, whose texts the server answers for
    // the evidence panel wherever the page is served, and what owns the
    // reader they are read through, which the server disposes.
    public Recorder.Session.RecordedScripts? Scripts { get; init; }
    public IDisposable? ScriptsOwner { get; init; }

    public string? FontAddress { get; init; }

    // Slice 5b: the frames of the page, each with how it is built. Served
    // frames are answered by the recorder at their recorded addresses.
    public IReadOnlyList<RecreationFrame> Frames { get; init; } = [];
}

// A frame of a recreated page (slice 5b): its owner element, by recorded
// node ID and path in its parent document, and how it is built. Way is
// "served", for a document answered at its recorded http or https address
// with a page of its own; "in-place", for an about:blank document the
// parent's builder builds in the frame's document; "srcdoc", for one the
// parent's builder builds after the frame loads its recorded srcdoc
// markup; or "not-built", with the reason. The parent's markup holds the
// trees of the frames built in place, and a served frame's page holds the
// trees of its own.
public sealed record RecreationFrame(
    long OwnerNodeId,
    NodePath? OwnerPath,
    string Element,
    string Way,
    string? DocumentUrl,
    IReadOnlyList<RecreationFrame> Children)
{
    // For a served frame: the address its owner asks for, when it is an
    // http or https address other than the document's own, which the
    // recorder answers with a redirect to the document's address.
    public string? OwnerAddress { get; init; }

    // For a served frame: its page and the nonce of its builder.
    public string? Html { get; init; }
    public string? ScriptNonce { get; init; }

    // For a served frame and a frame built in place: the recording's fonts,
    // images, and style sheets for its document, which the recorder answers
    // to the frame's requests. The server disposes them.
    public Recorder.Session.RecordedPageResources? Resources { get; init; }

    // Why the frame is not built, for a frame not built.
    public string? Reason { get; init; }

    // The recorded document chosen for the frame, and whether it was
    // recorded in its parent's renderer process.
    public string? DocumentKey { get; init; }
    public bool? SameProcessAsParent { get; init; }

    // Slice 5c: the frame's key, as the server keys it; its own evidence,
    // for a frame whose document has a DOM walk at the frame, built or not;
    // how its document was chosen and on what basis; and its origin, read
    // from its address, inherited from its parent for an about:blank or
    // about:srcdoc document, or "opaque".
    public string Key { get; init; } = "";
    public RecreationEvidence? Evidence { get; init; }
    public string? Choice { get; init; }
    public string? Basis { get; init; }
    public string? Origin { get; init; }
    public bool OriginInherited { get; init; }
}

// What the evidence panel shows of a frame while the recreation is open
// (slice 5b): its owner, how it is built, whether the recreation asked for
// its document, and whether its document is in a renderer process of its
// own in the recreation, which is known only once a frame target is
// attached for it.
public sealed record RecreationFrameStatus(
    string Key,
    long OwnerNodeId,
    string? OwnerPath,
    string Element,
    string Way,
    string? DocumentUrl,
    string? Reason,
    bool? SameProcessAsParentWhenRecorded,
    bool AskedFor,
    bool OutOfProcess)
{
    // Slice 5c: the parent's key; the owner's path with its scopes, for
    // Select; the document chosen, how, and on what basis; its origin;
    // the address of its own evidence; and its times.
    public string ParentKey { get; init; } = "";
    public NodePath? Owner { get; init; }
    public string? DocumentKey { get; init; }
    public string? Choice { get; init; }
    public string? Basis { get; init; }
    public string? Origin { get; init; }
    public bool OriginInherited { get; init; }
    public string? EvidenceAddress { get; init; }
    public RecreationFrameTimes? Times { get; init; }
}

// Slice 5c: a frame's times, in milliseconds from the top document's time
// origin: when its load started, for a served frame; when its builder
// finished, or its build in place did; when its first frame after the
// build was painted; how long it took, on one clock: a served frame from
// its own load start to that paint, a frame built in place from its build's
// start to its end; and on what basis.
public sealed record RecreationFrameTimes(
    double? LoadStarted,
    double? Built,
    double? FirstPaint,
    double? Took,
    string Basis);

// What the recreation control asks of the recorder (slice 5b): the answer
// to a request of a frame, by the frame's key, the top document's being
// empty; the key of the frame whose owner has a path among a document's
// frames; and what the recreation did with its frames, for the panel.
public interface IRecreationAnswers
{
    RecreationAnswer? Answer(string url, string? resourceType, string frameKey);

    string? ChildKey(string parentKey, NodePath ownerPath);

    // Records that a frame's document was asked for, and answers whether
    // the frame is built: a frame not built has its load refused, which is
    // not a navigation the recreation blocked.
    bool FrameAskedFor(string key);

    void FrameOutOfProcess(string key);

    // Slice 5c: a document's build report, sent by its builder through the
    // recorder's binding; false when it is ignored.
    bool DocumentBuilt(string payload);
}

// The recorder's answer to a request of the recreation's tab: a status, the
// response headers, and the body.
public sealed record RecreationAnswer(int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body);

// How long one step of opening a recreation took, in milliseconds, measured
// by the recorder (stage 3).
public sealed record RecreationTiming(string Step, double Milliseconds);

// What the evidence panel shows. Every value is a recorded value, or, for
// the fixed content of slice 3a, a value written in code and stated as such.
public sealed record RecreationEvidence(
    RecreationDescription Recreation,
    RecreationFidelity Fidelity,
    IReadOnlyList<RecordedTimer> Timers,
    IReadOnlyList<RecordedAnimation> Animations,
    IReadOnlyList<RecordedInteractiveElement> InteractiveElements,
    RecordedInteraction Interaction)
{
    public IReadOnlyList<RecordedTargetListener> OtherListeners { get; init; } = [];

    // Notes on how the recreation was built: values cut in the recording,
    // and what the builder inferred or could not build.
    public IReadOnlyList<string> Notes { get; init; } = [];

    // Why the animations list is not evidence, when it is not: the recording
    // holds no animation records, as before protocol 0.53.
    public string? AnimationsNotRead { get; init; }

    // What the panel says of the animations listed: how their times at the
    // frame were found, and what is not listed.
    public IReadOnlyList<string> AnimationNotes { get; init; } = [];

    // The document's scripts at the frame (slice 4h, protocol 0.54).
    public IReadOnlyList<RecordedScript> Scripts { get; init; } = [];

    // Why the scripts list is not evidence, when it is not: the recording
    // holds no script records, as before protocol 0.54.
    public string? ScriptsNotRead { get; init; }

    public IReadOnlyList<string> ScriptNotes { get; init; } = [];

    // The size, device pixel ratio, and layout zoom factor the page is meant
    // to be shown at, which the evidence panel compares with the page's
    // own once it has painted; null when no layout checkpoint was recorded.
    public RecreationShownViewport? ShownViewport { get; init; }
}

// The viewport a recreation is meant to be shown at: its CSS size, its
// devicePixelRatio, and the layout zoom factor it is laid out at.
public sealed record RecreationShownViewport(double Width, double Height, double DevicePixelRatio, double LayoutZoomFactor)
{
    // Set when the recorded viewport may be out of date at the frame: in a
    // recording before protocol 0.58, the root element was laid out wider
    // than the checkpoint's viewport. The panel shows and announces it.
    public string? Warning { get; init; }
}

// Source is "fixed" for slice 3a and "recording" from slice 3b. The frame,
// recording time, and basis are null for fixed content.
public sealed record RecreationDescription(
    string Source,
    string Title,
    string Notice,
    long? FrameNanoseconds,
    long? RecordingNanoseconds,
    string? Basis)
{
    public string? Url { get; init; }
    public string? DocumentKey { get; init; }
}

// Status is "not-checked", "equal", or "different".
public sealed record RecreationFidelity(
    string Status,
    string Explanation,
    IReadOnlyList<RecreationDifference> Differences);

public sealed record RecreationDifference(
    NodePath Node,
    string Property,
    string Recorded,
    string Recreated);

// Kind is the recorded timer kind: "timeout", "interval",
// "animation-frame", or "idle-callback". Times are recording times in
// nanoseconds. The time remaining is from the frame's recording time to the
// timer's next run, from its last run for an interval timer; it is null for
// an animation frame or idle callback, which have no due time, and negative
// for a timer due before the frame that had not yet run.
public sealed record RecordedTimer(
    string TimerId,
    string Kind,
    double? RequestedDelayMilliseconds,
    double? EffectiveDelayMilliseconds,
    long ScheduledNanoseconds,
    long? LastRunNanoseconds,
    double? RemainingMilliseconds,
    RecordedTimerOrigin? ScheduledBy = null);

// Who scheduled a timer (protocol 0.52, slice 4f), as its timer-origin record
// and the script-compiled records of its document give it. Owner says whose
// script it was; Element names the script element or on... attribute the
// first stack frame with a script-compiled record came from, with its path,
// or is null; ElementNote says how that frame was found when it is not the
// innermost; Caller is the innermost frame; Callback is where the callback
// function is defined. Text that is not recorded is null.
public sealed record RecordedTimerOrigin(
    string Owner,
    string? Element,
    NodePath? ElementPath,
    string? ElementNote,
    string? Caller,
    string? Callback,
    string? Handler)
{
    // Slice 4h: the caller's and the callback's line in the recorded text
    // of their script, when the script's text is recorded.
    public RecordedSourceLink? CallerSource { get; init; }
    public RecordedSourceLink? CallbackSource { get; init; }
}

// A line of a script's recorded text (slice 4h): the script, the digest of
// its text, which the panel reads it by, and the one-based line and column.
public sealed record RecordedSourceLink(string ScriptId, string Digest, int Line, int? Column);

// A script of the document at the frame (slice 4h, protocol 0.54), from its
// script-parsed record. Kind is "classic", "module", "eval", or "function".
// Owner says whose script it was, from its world, as for a timer. Element
// names the script element or on... attribute it came from, with its path,
// joined by script ID to the slice 4f script-compiled record, or is null.
// Line and Column are its one-based start in its resource. EvalFrom names
// the script that called eval. Digest is the digest of its recorded text,
// null when the text is not recorded; Size is the text's UTF-8 byte count.
public sealed record RecordedScript(
    string ScriptId,
    string Kind,
    string Owner,
    string? Element,
    NodePath? ElementPath,
    string? Url,
    string? SourceUrl,
    string? SourceMapUrl,
    int? Line,
    int? Column,
    string? EvalFromScriptId,
    string? EvalFrom,
    bool CompileError,
    string? Digest,
    long Size,
    long RecordedNanoseconds);

// An animation of the document at the frame (slice 4g, protocol 0.53), as
// its latest animation-updated record at or before the frame gives it. Kind
// is "css-animation", "css-transition", or "web-animation"; Name is the
// animation's id, else its animation name, else the transitioned property.
// Target is the path of the target element, with the pseudo-element when
// the effect targets one. Times on the animation's timeline are
// milliseconds; StartNanoseconds is the start time as a recording time,
// when the timeline is a document timeline whose zero time is recorded.
// Iterations is null when infinite. The current time, iteration, and
// progress are at the frame: CurrentTimeBasis is "computed" when computed
// from the record for a running animation, or "recorded" when taken as
// recorded. Progress is the directed progress, before the easing; it and
// the current iteration are null when the effect is not in effect.
// RecordedProgress is Blink's own progress at the record, after the easing.
public sealed record RecordedAnimation(
    string Kind,
    string? Name,
    NodePath? Target,
    string? PseudoElement,
    string PlayState,
    bool Pending,
    double? StartTimeMilliseconds,
    long? StartNanoseconds,
    double? DelayMilliseconds,
    double? DurationMilliseconds,
    double? Iterations,
    string? Direction,
    string? Fill,
    string? Easing,
    double? CurrentTimeMilliseconds,
    string CurrentTimeBasis,
    double? CurrentIteration,
    double? Progress,
    string Timeline,
    bool OnCompositor,
    long RecordedNanoseconds,
    double? RecordedProgress);

// A listener as its registration record gives it. Location is the script
// address, line, and column of the registration, when recorded.
public sealed record RecordedListener(
    string EventName,
    string? RegistrationKind,
    bool Capture,
    bool Once,
    bool Passive,
    string? Location);

// A node with a listener registered at the frame, or with accessibility
// data. Focusable is read from Chromium's accessibility property text, as
// recorded, and is null when the node has no accessibility data. The
// accessibility values are those of the latest update batch that named the
// node, recorded at AccessibilityNanoseconds.
public sealed record RecordedInteractiveElement(
    NodePath Node,
    string Element,
    IReadOnlyList<RecordedListener> Listeners,
    bool? Focusable,
    string? Role,
    string? Name,
    long? AccessibilityNanoseconds,
    string? AccessibilityProperties);

// A listener on a target that is not a node of the tree, such as the window.
public sealed record RecordedTargetListener(string Target, RecordedListener Listener);

public sealed record RecordedInteraction(
    NodePath? Focus,
    string? Selection,
    IReadOnlyList<RecordedFormValue> FormValues);

public sealed record RecordedFormValue(NodePath Node, string Value);

// A node's path: one XPath expression for each tree scope from the document
// to the node. The first is evaluated from the document, and each later one
// from the shadow root of the element the previous one selects, whose mode is
// the matching entry of ShadowModes. Steps are positional only. See
// docs/architecture/page-recreation.md, "Paths through shadow roots".
public sealed record NodePath(IReadOnlyList<string> Scopes, IReadOnlyList<string> ShadowModes)
{
    public static NodePath Of(string path) => Create([path], []);

    [JsonInclude]
    public string Display => string.Concat(
        Scopes.Select((scope, index) => index == 0 ? scope : $"/#shadow-root({ShadowModes[index - 1]}){scope}"));

    public static NodePath Create(IReadOnlyList<string> scopes, IReadOnlyList<string> shadowModes)
    {
        if (scopes.Count == 0)
        {
            throw new ArgumentException("A path has at least one scope.", nameof(scopes));
        }
        if (shadowModes.Count != scopes.Count - 1)
        {
            throw new ArgumentException("A path has one shadow root mode for each scope after the first.", nameof(shadowModes));
        }
        foreach (var scope in scopes)
        {
            if (!scope.StartsWith('/') || scope.Length < 2)
            {
                throw new ArgumentException($"The scope '{scope}' is not an absolute path.", nameof(scopes));
            }
        }
        foreach (var mode in shadowModes)
        {
            if (mode is not ("open" or "closed"))
            {
                throw new ArgumentException($"The shadow root mode '{mode}' is not open or closed.", nameof(shadowModes));
            }
        }
        return new NodePath(scopes, shadowModes);
    }
}
