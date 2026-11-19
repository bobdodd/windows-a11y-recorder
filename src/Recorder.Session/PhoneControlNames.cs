using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.Session;

/// <summary>
/// One control name a model is to be asked about (android-checks.md, step
/// 28, rule 4): the screen and the name, asked once for the screen from the
/// first time a control with the name was seen on a picture OCR read, with
/// that visit, the control's identity, the picture and the control's
/// bounds there, its class, its state, the text OCR read inside it,
/// whether it shows text, and what a screen reader says on the screen,
/// with the control's place marked.
/// </summary>
public sealed record PhoneControlNameQuestion(
    PhoneVisit Visit,
    string Identity,
    PhoneFindingTime Time,
    PhoneContrastPicture Picture,
    string Name,
    string Role,
    string? State,
    string ShownText,
    bool ShowsText,
    string ScreenText)
{
    public string App => Visit.Screen.Package;

    public string ScreenName => Visit.Screen.Name;

    public PhoneBounds Bounds => Time.Bounds;
}

/// <summary>What became of one control name in a visit.</summary>
public enum PhoneControlNameState
{
    /// <summary>The name is a placeholder, a warning with no model (rule 3).</summary>
    Placeholder,

    /// <summary>For the model to judge, once for the screen (rule 4).</summary>
    Asked,
}

/// <summary>
/// One control name as the check found it in a visit, once: the name, what
/// became of it, the placeholder it is where it is one, and its question
/// where it is asked.
/// </summary>
public sealed record PhoneControlNameEntry(
    PhoneVisit Visit,
    string Name,
    PhoneControlNameState State,
    string? Placeholder,
    PhoneControlNameQuestion? Question);

/// <summary>What the model's answers made of one control name.</summary>
public enum PhoneControlNameOutcome
{
    /// <summary>Judged to say what the control does: counted, no finding (rule 7).</summary>
    Describes,

    /// <summary>A warning: judged not to say what the control does, or the model could not tell, or gave two answers (rule 7).</summary>
    Finding,
}

/// <summary>One control name's answer: what it made of the name, and the model's inference.</summary>
public sealed record PhoneControlNameAnswer(PhoneControlNameOutcome Outcome, PhoneInference Inference);

/// <summary>
/// Step 28 of android-checks.md, a control name that does not say what the
/// control does (WCAG 1.1.1 for a control that shows no text, 2.4.6 for a
/// visible label). The owner chose the rules on 2026-11-18 from a probe of
/// 985 named controls on 20 recordings: (1) each visible control that can
/// be tapped, long pressed or checked, not a field, that a screen reader
/// stops on, with a name, on a settled snapshot whose picture OCR read;
/// (2) 1.1.1 where the control shows no text, 2.4.6 where its name is its
/// visible label; (3) a name that is a placeholder, a generic word, a
/// resource name, a numbered name or the control's class name, is a
/// warning with no model; (4) every other name is asked once for each
/// screen and name, the answer given to every visit with that name;
/// (5) controls the icon check asks about are included; (6) the question
/// gives the whole screen with the control outlined and a close-up, the
/// name, class and state, the text OCR read inside it and what a screen
/// reader says on the screen, with three answers, reason first; (7) agreed
/// does-not-describe is put to the model for complex questions to confirm,
/// as are unsettled answers, whose answers decide: agreed does-not-describe
/// a warning for a person to verify, medium impact; cannot tell or two
/// answers a warning to check manually; agreed describes counted, no
/// finding. With no model, only the placeholder warnings are made.
/// </summary>
public sealed partial class PhoneControlNameRun
{
    /// <summary>The most characters of the text OCR read inside a control given in its question.</summary>
    public const int MostShownCharacters = 300;

    /// <summary>The mark of the control's place in what a screen reader says on the screen.</summary>
    public const string ControlMark = "[the outlined control]";

    // Whole names that say nothing of what a control does (rule 3), as
    // the probe measured them, compared in lower case with punctuation as
    // spaces.
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "button", "btn", "image", "img", "icon", "picture", "photo", "graphic", "view", "imageview", "imagebutton", "item", "link", "click", "click here",
        "tap", "tap here", "here", "press", "select", "action", "unlabelled", "unlabeled", "untitled", "label", "text", "title", "null", "undefined",
        "none", "placeholder", "todo", "test", "content", "element", "control", "widget", "logo",
    };

    // The words of a developer's name for a view, one of which a name in
    // camel case must hold to be taken as a resource name (rule 3), so
    // that a product name such as "iPhone" is not.
    private static readonly HashSet<string> ResourceWords = new(StringComparer.Ordinal)
    {
        "btn", "button", "img", "image", "icon", "ic", "iv", "tv", "txt", "text", "label", "lbl", "view", "item", "tab", "menu", "title", "desc", "action",
    };

    private readonly Dictionary<PhoneVisit, List<PhoneFinding>> _byVisit = [];
    private readonly List<PhoneControlNameEntry> _entries = [];
    private readonly HashSet<PhoneVisit> _visitsRead = [];
    private readonly Dictionary<PhoneControlNameEntry, List<PhoneFindingTime>> _times = [];
    private readonly Dictionary<PhoneControlNameEntry, (string Identity, string Label)> _items = [];
    private readonly List<PhoneFinding> _placeholders = [];

    private PhoneControlNameRun()
    {
    }

    public static PhoneControlNameRun Empty { get; } = new();

    /// <summary>The warnings: the placeholder names' (rule 3), and those the model's answers made (rule 7).</summary>
    public IReadOnlyList<PhoneFinding> Findings { get; private set; } = [];

    /// <summary>The placeholder names' warnings, made with no model (rule 3).</summary>
    public IReadOnlyList<PhoneFinding> PlaceholderFindings => _placeholders;

    /// <summary>The settled snapshots whose pictures OCR read.</summary>
    public int SnapshotsRead { get; private set; }

    /// <summary>The named controls a screen reader stops on, once each in a visit (rule 1).</summary>
    public int Controls { get; private set; }

    /// <summary>Every control name, once a visit, in the order found.</summary>
    public IReadOnlyList<PhoneControlNameEntry> Entries => _entries;

    /// <summary>The names, once a visit, that are placeholders (rule 3).</summary>
    public int Placeholders => _entries.Count(entry => entry.State == PhoneControlNameState.Placeholder);

    /// <summary>The names to be asked about, once for each screen and name, in screen order (rule 4).</summary>
    public IReadOnlyList<PhoneControlNameQuestion> Questions { get; private set; } = [];

    /// <summary>The questions answered, with what each answer made of its name; empty until a model's answers are given.</summary>
    public IReadOnlyDictionary<PhoneControlNameQuestion, PhoneControlNameAnswer> Answers { get; private set; } =
        new Dictionary<PhoneControlNameQuestion, PhoneControlNameAnswer>();

    /// <summary>The names judged to say what their controls do, not confirmed, once for each screen.</summary>
    public int Described => Answers.Values.Count(answer => answer.Outcome == PhoneControlNameOutcome.Describes);

    /// <summary>The questions with no answer, not judged.</summary>
    public int Unanswered => Questions.Count - Answers.Count;

    /// <summary>
    /// Every inference the model's answers made, findings or not, so that
    /// a report says AI was used even where every name was judged to say
    /// what its control does.
    /// </summary>
    public IEnumerable<PhoneInference> Inferences => Answers.Values.Select(answer => answer.Inference);

    /// <summary>What became of the questions; null until a model's answers are given, or where there are none.</summary>
    public string? ModelNote { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
        _byVisit.TryGetValue(visit, out var list) ? list : [];

    /// <summary>Whether OCR read a picture of the visit, so its controls were checked.</summary>
    public bool Read(PhoneVisit visit) => _visitsRead.Contains(visit);

    /// <summary>The control names of a visit, once each.</summary>
    public IEnumerable<PhoneControlNameEntry> In(PhoneVisit visit) => _entries.Where(entry => entry.Visit == visit);

    /// <summary>Runs rules 1 to 5 on a measurement, and makes rule 3's warnings; no model is used.</summary>
    public static PhoneControlNameRun Run(PhoneContrastRun measured, PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(screens);
        var stopwatch = Stopwatch.StartNew();
        var run = new PhoneControlNameRun();
        var seen = new HashSet<(PhoneVisit, string)>();
        var entries = new Dictionary<(PhoneVisit, string), PhoneControlNameEntry>();
        var questions = new Dictionary<(PhoneScreen, string), PhoneControlNameQuestion>();
        foreach (var snapshot in measured.Snapshots)
        {
            // Rule 6 gives the text OCR read inside the control.
            if (snapshot.Window is not { } window || string.IsNullOrEmpty(snapshot.Engine))
            {
                continue;
            }

            run.SnapshotsRead++;
            var visit = snapshot.Visit;
            run._visitsRead.Add(visit);
            var state = PhoneCheckRun.StateAt(visit, snapshot.Settled.MonotonicNanoseconds);
            foreach (var control in window.Items.Where(IsControl))
            {
                // Rule 1: named, and a screen reader stops on it.
                if (!PhoneTreeChecks.ShouldFocusView(control) || PhoneTreeChecks.SpeakableText(control).Trim() is not { Length: > 0 } name)
                {
                    continue;
                }

                // Once a visit for each control.
                if (!seen.Add((visit, control.Identity)))
                {
                    continue;
                }

                run.Controls++;
                var nameKey = name.ToLowerInvariant();
                var shown = ShownText(snapshot.Lines, control.Bounds);
                var showsText = shown.Length > 0 || HasText(control);
                var role = Role(control);
                var placeholder = Placeholder(name, control.Node.ClassName);
                var time = new PhoneFindingTime(
                    snapshot.Picture.Time, visit, snapshot.Settled, snapshot.TakenBy, visit.WindowId, control.Bounds, control.TreeKey,
                    Measured(name, role, placeholder, showsText, snapshot.Picture.Kind), state);
                if (!entries.TryGetValue((visit, nameKey), out var entry))
                {
                    PhoneControlNameQuestion? question = null;
                    if (placeholder is null && !questions.TryGetValue((visit.Screen, nameKey), out question))
                    {
                        // Rule 4: once for the screen and name, from the first time seen.
                        questions[(visit.Screen, nameKey)] = question = new PhoneControlNameQuestion(
                            visit, control.Identity, time, snapshot.Picture, name, role, StateOf(control), shown, showsText,
                            PhoneImageAlternativeRun.ScreenText(window, control, ControlMark));
                    }

                    entry = new PhoneControlNameEntry(
                        visit, name, placeholder is null ? PhoneControlNameState.Asked : PhoneControlNameState.Placeholder, placeholder, question);
                    entries[(visit, nameKey)] = entry;
                    run._entries.Add(entry);
                    run._times[entry] = [];
                    run._items[entry] = (control.Identity, control.Label);
                }

                run._times[entry].Add(time);
            }
        }

        // Rule 3: a placeholder name is a warning, with no model.
        foreach (var entry in run._entries.Where(entry => entry.State == PhoneControlNameState.Placeholder))
        {
            run._placeholders.Add(run.FindingOf(entry, PhoneCheckLevel.Warning, null));
        }

        var screenOrder = screens.Apps
            .SelectMany(app => app.Screens)
            .Select((screen, index) => (screen, index))
            .ToDictionary(pair => pair.screen, pair => pair.index);
        run.Questions = [.. questions.Values
            .OrderBy(question => screenOrder.TryGetValue(question.Visit.Screen, out var at) ? at : int.MaxValue)
            .ThenBy(question => question.Visit.Start)
            .ThenBy(question => question.Time.Time)
            .ThenBy(question => question.Bounds.Top)
            .ThenBy(question => question.Bounds.Left)];
        run.Place(run._placeholders);
        run.Elapsed = stopwatch.Elapsed;
        return run;
    }

    /// <summary>
    /// The run with the model's answers given: each answer that makes a
    /// finding (rule 7) is a finding on every visit where a control on its
    /// screen has its name (rule 4), at the answer's level, with every time
    /// a control with the name was seen in the visit; an answer that the
    /// name says what the control does is counted, not a finding. The
    /// placeholder warnings stand. The note says what became of the
    /// questions.
    /// </summary>
    public PhoneControlNameRun Judged(IReadOnlyDictionary<PhoneControlNameQuestion, PhoneControlNameAnswer> answers, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var judged = new PhoneControlNameRun
        {
            SnapshotsRead = SnapshotsRead,
            Controls = Controls,
            Questions = Questions,
            ModelNote = note,
            Elapsed = Elapsed,
        };
        judged._entries.AddRange(_entries);
        judged._visitsRead.UnionWith(_visitsRead);
        judged._placeholders.AddRange(_placeholders);
        foreach (var (entry, times) in _times)
        {
            judged._times[entry] = times;
        }

        foreach (var (entry, item) in _items)
        {
            judged._items[entry] = item;
        }

        var kept = new Dictionary<PhoneControlNameQuestion, PhoneControlNameAnswer>();
        foreach (var question in Questions)
        {
            if (answers.TryGetValue(question, out var answer))
            {
                kept[question] = answer;
            }
        }

        var findings = new List<PhoneFinding>(_placeholders);
        foreach (var entry in _entries)
        {
            if (entry.Question is { } question && kept.TryGetValue(question, out var answer) && answer.Outcome == PhoneControlNameOutcome.Finding)
            {
                findings.Add(judged.FindingOf(entry, answer.Inference.Level, answer.Inference));
            }
        }

        judged.Answers = kept;
        judged.Place(findings);
        return judged;
    }

    /// <summary>
    /// Whether an item is a control for rule 1: visible, with bounds, not a
    /// field, and one that can be tapped, long pressed or checked.
    /// </summary>
    internal static bool IsControl(PhoneCheckItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var node = item.Node;
        return node.VisibleToUser && !node.Editable && !item.Bounds.IsEmpty && (node.Clickable || node.LongClickable || node.Checkable);
    }

    /// <summary>
    /// What placeholder a name is (rule 3), or null: "a generic word", a
    /// whole name such as "button" or "icon"; "its class name"; "a numbered
    /// name", such as "Button 2"; or "a resource name", such as
    /// "btn_close", "menuItem" or one with ":id/".
    /// </summary>
    public static string? Placeholder(string name, string? className)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        var words = Spaces().Replace(NotWord().Replace(trimmed.ToLowerInvariant(), " "), " ").Trim();
        if (Generic.Contains(words))
        {
            return "a generic word";
        }

        var shortClass = className is null ? null : className[(className.LastIndexOf('.') + 1)..];
        if ((shortClass is { Length: > 0 } && string.Equals(trimmed, shortClass, StringComparison.OrdinalIgnoreCase)) ||
            (className is { Length: > 0 } && string.Equals(trimmed, className, StringComparison.OrdinalIgnoreCase)))
        {
            return "its class name";
        }

        if (Numbered().IsMatch(words))
        {
            return "a numbered name";
        }

        if (trimmed.Contains(":id/", StringComparison.Ordinal) || SnakeCase().IsMatch(trimmed) ||
            (CamelCase().IsMatch(trimmed) && CamelWords().Matches(trimmed).Any(word => ResourceWords.Contains(word.Value.ToLowerInvariant()))))
        {
            return "a resource name";
        }

        return null;
    }

    /// <summary>
    /// The text OCR read inside a control's bounds, line by line, each
    /// line whose middle is inside them, at most
    /// <see cref="MostShownCharacters"/> characters.
    /// </summary>
    internal static string ShownText(IReadOnlyList<PhoneTextLine> lines, PhoneBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var read = string.Join(" | ", lines
            .Where(line => !string.IsNullOrWhiteSpace(line.OcrText) && Inside(line.Box, bounds))
            .Select(line => line.OcrText!.Trim()));
        return read.Length <= MostShownCharacters ? read : read[..(MostShownCharacters - 3)] + "...";
    }

    /// <summary>The control's class, its last part, for rule 6.</summary>
    internal static string Role(PhoneCheckItem control)
    {
        var className = control.Node.ClassName;
        return string.IsNullOrEmpty(className) ? "View" : className[(className.LastIndexOf('.') + 1)..];
    }

    /// <summary>The control's state for rule 6: its state description, checked or not, selected, and not enabled; null for none.</summary>
    internal static string? StateOf(PhoneCheckItem control)
    {
        var node = control.Node;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(node.StateDescription))
        {
            parts.Add(node.StateDescription.Trim());
        }
        else if (node.Checkable)
        {
            parts.Add(node.Checked == "checked" ? "checked" : "not checked");
        }

        if (node.Selected)
        {
            parts.Add("selected");
        }

        if (!node.Enabled)
        {
            parts.Add("not enabled");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    // Whether the tree gives text for the control to show: its own text,
    // or that of a visible item inside it (rule 2).
    private static bool HasText(PhoneCheckItem control)
    {
        if (!string.IsNullOrWhiteSpace(control.Text))
        {
            return true;
        }

        return control.Children.Any(child => child.Node.VisibleToUser && HasText(child));
    }

    private static bool Inside(PhoneBounds line, PhoneBounds box)
    {
        var x = (line.Left + line.Right) / 2.0;
        var y = (line.Top + line.Bottom) / 2.0;
        return x >= box.Left && x <= box.Right && y >= box.Top && y <= box.Bottom;
    }

    // What was measured of a control, with the criterion rule 2 gives it.
    private static string Measured(string name, string role, string? placeholder, bool showsText, PhonePictureKind kind)
    {
        var criterion = showsText
            ? "it shows text, so WCAG 2.4.6 applies to its label"
            : "neither the tree nor OCR gives it text, so WCAG 1.1.1 applies if it shows none";
        var what = placeholder is null ? "" : $"; the name is {placeholder}, a placeholder that does not say what the control does";
        return string.Create(CultureInfo.CurrentCulture,
            $"a control named \"{name}\", of class {role}{what}; {criterion}; {(kind == PhonePictureKind.Screenshot ? "from the phone's screenshot" : "approximate, from the video")}");
    }

    private PhoneFinding FindingOf(PhoneControlNameEntry entry, PhoneCheckLevel level, PhoneInference? inference)
    {
        var (identity, label) = _items[entry];
        var finding = new PhoneFinding(PhoneCheckNames.ControlNameNotDescriptive, level, entry.Visit, identity, label)
        {
            Inference = inference,
        };
        finding._times.AddRange(_times[entry]);
        return finding;
    }

    private void Place(IReadOnlyList<PhoneFinding> findings)
    {
        Findings = findings;
        _byVisit.Clear();
        foreach (var finding in findings)
        {
            if (!_byVisit.TryGetValue(finding.Visit, out var ofVisit))
            {
                _byVisit[finding.Visit] = ofVisit = [];
            }

            ofVisit.Add(finding);
        }
    }

    [GeneratedRegex(@"[^\p{L}\p{N} ]")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(button|btn|image|img|icon|item|view|link|tab|picture|photo)\s*\d+$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^[A-Za-z0-9]+(_[A-Za-z0-9]+)+$")]
    private static partial Regex SnakeCase();

    [GeneratedRegex(@"^[a-z]+([A-Z][a-z0-9]*)+$")]
    private static partial Regex CamelCase();

    [GeneratedRegex(@"[A-Z]?[a-z0-9]+")]
    private static partial Regex CamelWords();
}
