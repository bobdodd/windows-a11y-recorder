using System.Text.Json.Serialization;

namespace Recorder.Recreation;

// A page to recreate and the evidence the evidence panel shows with it.
public sealed record RecreationContent(string Html, RecreationEvidence Evidence);

// What the evidence panel shows. Every value is a recorded value, or, for
// the fixed content of slice 3a, a value written in code and stated as such.
public sealed record RecreationEvidence(
    RecreationDescription Recreation,
    RecreationFidelity Fidelity,
    IReadOnlyList<RecordedTimer> Timers,
    IReadOnlyList<RecordedAnimation> Animations,
    IReadOnlyList<RecordedInteractiveElement> InteractiveElements,
    RecordedInteraction Interaction);

// Source is "fixed" for slice 3a and "recording" from slice 3b. The frame,
// recording time, and basis are null for fixed content.
public sealed record RecreationDescription(
    string Source,
    string Title,
    string Notice,
    long? FrameNanoseconds,
    long? RecordingNanoseconds,
    string? Basis);

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

// Kind is "timeout" or "interval". Times are recording times in
// nanoseconds, and the remaining time is from the frame's recording time.
public sealed record RecordedTimer(
    long TimerId,
    string Kind,
    double DelayMilliseconds,
    long ScheduledNanoseconds,
    double RemainingMilliseconds,
    NodePath? Owner);

// Kind is "animation" or "transition". Name is the animation name or the
// transitioned property.
public sealed record RecordedAnimation(
    string Kind,
    string Name,
    NodePath Target,
    long StartNanoseconds,
    double DurationMilliseconds,
    double Progress);

public sealed record RecordedInteractiveElement(
    NodePath Node,
    string Element,
    IReadOnlyList<string> Listeners,
    bool Focusable,
    string? Role,
    string? Name);

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
