using Recorder.Contracts;

namespace Recorder.Collectors.Automation;

/// <summary>
/// A tree a window snapshot is read from: UI Automation's control view of a
/// window in recordings, a fixed tree in tests. Each read may throw, as an
/// element can go while it is read.
/// </summary>
public interface IWindowTreeSource<TNode>
    where TNode : class
{
    TNode? FirstChild(TNode node);

    TNode? NextSibling(TNode node);

    WindowSnapshotElement Read(TNode node, int? parent, int depth, int position);
}

/// <param name="Parent">The index of the element's parent in the snapshot, or null for the window.</param>
/// <param name="Depth">0 for the window, 1 for its children, and so on.</param>
/// <param name="Position">The element's place among its parent's children, from 1.</param>
/// <param name="Value">The Value pattern's value, or null without the pattern or for a password.</param>
/// <param name="ToggleState">The Toggle pattern's state, or null without it.</param>
/// <param name="IsSelected">The SelectionItem pattern's state, or null without it.</param>
/// <param name="ExpandCollapseState">The ExpandCollapse pattern's state, or null without it.</param>
public sealed record WindowSnapshotElement(
    int? Parent,
    int Depth,
    int Position,
    int[]? RuntimeId,
    string? Name,
    string? ControlType,
    string? LocalizedControlType,
    string? AutomationId,
    string? ClassName,
    string? FrameworkId,
    string? AccessKey,
    string? HelpText,
    bool? IsEnabled,
    bool? IsOffscreen,
    bool? HasKeyboardFocus,
    bool? IsKeyboardFocusable,
    bool? IsPassword,
    WindowSnapshotRectangle? BoundingRectangle,
    string? Value,
    bool? ValueIsReadOnly,
    string? ToggleState,
    bool? IsSelected,
    string? ExpandCollapseState,
    IReadOnlyList<string> QualityFlags);

public sealed record WindowSnapshotRectangle(double X, double Y, double Width, double Height);

/// <param name="Truncated">Null, or why the read stopped: <see cref="WindowSnapshotRecords.Truncations"/>.</param>
/// <param name="Problems">Each read that failed, and where; the walk goes on past it.</param>
public sealed record WindowTreeReading(
    IReadOnlyList<WindowSnapshotElement> Elements,
    string? Truncated,
    long ReadNanoseconds,
    IReadOnlyList<string> Problems);

/// <summary>
/// Reads a window's tree in document order, each element before its
/// children, until the tree ends or a limit is reached. A child list whose
/// read fails is ended there, with the failure in the problems, and the
/// walk goes on with the next element.
/// </summary>
public static class WindowTreeWalk
{
    public static WindowTreeReading Read<TNode>(
        IWindowTreeSource<TNode> source,
        TNode window,
        Func<long> now,
        int maximumElements = WindowSnapshotRecords.MaximumElements,
        long maximumNanoseconds = WindowSnapshotRecords.MaximumReadNanoseconds)
        where TNode : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(now);
        var started = now();
        var elements = new List<WindowSnapshotElement>();
        var problems = new List<string>();
        string? truncated = null;
        elements.Add(source.Read(window, null, 0, 1));

        // Each frame is a child list being read: its parent, the last child
        // read, and how many have been read.
        var frames = new Stack<Frame<TNode>>();
        frames.Push(new Frame<TNode>(window, 0, 1, null, 0));
        while (frames.Count > 0)
        {
            if (elements.Count >= maximumElements)
            {
                truncated = WindowSnapshotRecords.ElementLimit;
                break;
            }

            if (now() - started >= maximumNanoseconds)
            {
                truncated = WindowSnapshotRecords.TimeLimit;
                break;
            }

            var frame = frames.Pop();
            TNode? next;
            try
            {
                next = frame.Previous is null ? source.FirstChild(frame.Parent) : source.NextSibling(frame.Previous);
            }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                problems.Add($"Children of element {frame.ParentIndex} after {frame.Count}: {Describe(exception)}");
                continue;
            }

            if (next is null)
            {
                continue;
            }

            var position = frame.Count + 1;
            frames.Push(frame with { Previous = next, Count = position });
            WindowSnapshotElement element;
            try
            {
                element = source.Read(next, frame.ParentIndex, frame.Depth, position);
            }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                problems.Add($"Child {position} of element {frame.ParentIndex}: {Describe(exception)}");
                continue;
            }

            elements.Add(element);
            frames.Push(new Frame<TNode>(next, elements.Count - 1, frame.Depth + 1, null, 0));
        }

        return new WindowTreeReading(elements, truncated, Math.Max(0, now() - started), problems);
    }

    /// <summary>Whether two elements' states, as a snapshot records them, are the same.</summary>
    public static bool SameState(WindowSnapshotElement first, WindowSnapshotElement second) =>
        first.Name == second.Name &&
        first.Value == second.Value &&
        first.ToggleState == second.ToggleState &&
        first.IsSelected == second.IsSelected &&
        first.ExpandCollapseState == second.ExpandCollapseState &&
        first.IsEnabled == second.IsEnabled;

    /// <summary>A runtime ID as one comparable string, or null.</summary>
    public static string? Key(int[]? runtimeId) =>
        runtimeId is { Length: > 0 } ? string.Join('.', runtimeId) : null;

    private static bool IsReadFailure(Exception exception) =>
        exception is System.Runtime.InteropServices.COMException or InvalidOperationException or
            UnauthorizedAccessException or TimeoutException;

    private static string Describe(Exception exception) =>
        exception is System.Runtime.InteropServices.COMException com
            ? UiaEvidenceText.ReadFailureFlag(com.HResult)
            : exception.GetType().Name;

    private sealed record Frame<TNode>(TNode Parent, int ParentIndex, int Depth, TNode? Previous, int Count)
        where TNode : class;
}
