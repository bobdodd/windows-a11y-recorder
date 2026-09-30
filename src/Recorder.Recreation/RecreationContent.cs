using System.Text.Json.Serialization;

namespace Recorder.Recreation;

// A page to recreate and the evidence the evidence panel shows with it. A
// recorded page has a script nonce: its builder script is the only script
// the page's content security policy allows.
public sealed record RecreationContent(string Html, RecreationEvidence Evidence, string? ScriptNonce = null);

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
    double? RemainingMilliseconds);

// Kind is "animation" or "transition". Name is the animation name or the
// transitioned property.
public sealed record RecordedAnimation(
    string Kind,
    string Name,
    NodePath Target,
    long StartNanoseconds,
    double DurationMilliseconds,
    double Progress);

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
