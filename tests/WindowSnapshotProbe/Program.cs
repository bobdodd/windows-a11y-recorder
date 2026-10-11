using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using Recorder.Collectors.Automation;
using Recorder.Contracts;
using Recorder.Session;

namespace WindowSnapshotProbe;

// A Windows integration test of the NVDA window snapshots
// (docs/architecture/screen-reader-activity.md, "2c-4, what NVDA's windows
// show"). It runs the recorder's UI Automation collector and window
// snapshot collector in this process, with this process standing in for
// NVDA's main process, against a dialog built like NVDA's Elements List from
// standard Win32 controls: type radio buttons, a filter edit, a tree, and two
// buttons. It changes the dialog as a user would and checks each snapshot
// the collector records, and that every record passes the recorder's
// checks. It needs no screen reader, and changes nothing outside itself.
//
// Run: WindowSnapshotProbe.exe [output folder]
// It writes snapshot-probe.json there (default: the current folder) and
// exits 0 when every check passes.
public static class Program
{
    private static readonly List<RecorderEvent> Records = [];
    private static readonly List<(string Check, bool Passed, string Detail)> Results = [];

    [STAThread]
    public static int Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
        Directory.CreateDirectory(output);
        Application.EnableVisualStyles();
        var form = new ElementsListForm();
        var ready = new ManualResetEventSlim();
        form.Shown += (_, _) => ready.Set();
        var probe = new Thread(() => Run(form, ready, output)) { IsBackground = true, Name = "Probe" };
        probe.SetApartmentState(ApartmentState.MTA);
        probe.Start();
        Application.Run(form);
        probe.Join(TimeSpan.FromSeconds(10));
        var failed = Results.Count(result => !result.Passed);
        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"All {Results.Count} checks passed."
            : $"{failed} of {Results.Count} checks failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(ElementsListForm form, ManualResetEventSlim ready, string output)
    {
        try
        {
            ready.Wait(TimeSpan.FromSeconds(10));
            RunChecks(form, output).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Record("The probe ran to the end", false, exception.ToString());
        }
        finally
        {
            form.BeginInvoke(form.Close);
        }
    }

    private static async Task RunChecks(ElementsListForm form, string output)
    {
        var clock = new SessionClock();
        var context = new CollectorInitializationContext("probe", output, clock, new Sink());
        var uiAutomation = new UiAutomationCollector();
        var snapshots = new NvdaWindowSnapshotCollector();
        uiAutomation.ObservationRecorded += snapshots.Observe;
        snapshots.SetScreenReaderProcesses("NVDA", [Environment.ProcessId]);
        await uiAutomation.InitializeAsync(context, CancellationToken.None);
        await snapshots.InitializeAsync(context, CancellationToken.None);
        var boundary = new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow);
        Record("The UI Automation collector started", (await uiAutomation.StartAsync(boundary, CancellationToken.None)).Accepted, "");
        Record("The snapshot collector started", (await snapshots.StartAsync(boundary, CancellationToken.None)).Accepted, "");

        // 1. Focus enters the dialog.
        var mark = Count();
        form.Invoke(() =>
        {
            form.Activate();
            form.Tree.Focus();
        });
        var entered = await Next(mark, snapshot => Reason(snapshot) == WindowSnapshotRecords.FocusEnteredReason);
        Record("Focus entering the dialog reads it", entered is not null, Describe(entered));
        if (entered is { } first)
        {
            Record("The snapshot names the window", Name(Elements(first)[0]) == ElementsListForm.Title, Name(Elements(first)[0]) ?? "none");
            Record(
                "The snapshot holds the type radio buttons, the selected one selected",
                Find(first, "Links") is { } links && Bool(links, "isSelected") == true &&
                Find(first, "Headings") is { } headings && Bool(headings, "isSelected") == false,
                Describe(first));
            Record("The snapshot holds the filter edit and its empty value", Find(first, "Filter by:") is { } filter && Text(filter, "value") == "",
                Describe(first));
            Record("The snapshot holds the tree's three items", Count(first, "ControlType.TreeItem") == 3, Describe(first));
            Record("The snapshot holds the selected tree item", Find(first, "Contact Us") is { } contact && Bool(contact, "isSelected") == true,
                Describe(first));
        }

        // 2. Another tree item is selected: a selection change.
        mark = Count();
        form.Invoke(() => form.Tree.SelectedNode = form.Tree.Nodes[1]);
        var selected = await Next(mark, snapshot => Find(snapshot, "Donate") is { } donate && Bool(donate, "isSelected") == true);
        Record("Selecting another tree item reads the dialog again", selected is not null, Describe(selected));

        // 3. Text typed in the filter, and the tree filled again, as the
        // Elements List does.
        mark = Count();
        form.Invoke(() =>
        {
            form.Filter.Focus();
            form.Filter.Text = "con";
            form.Fill(["Contact Us"]);
        });
        var filtered = await Next(mark, snapshot => Find(snapshot, "Filter by:") is { } edit && Text(edit, "value") == "con");
        Record("Typing in the filter reads the dialog again with the text", filtered is not null, Describe(filtered));
        if (filtered is { } refilled)
        {
            Record("The refilled tree has one item", Count(refilled, "ControlType.TreeItem") == 1, Describe(refilled));
        }

        // 4. The other element type is chosen.
        mark = Count();
        form.Invoke(() =>
        {
            form.Headings.Focus();
            form.Headings.Checked = true;
        });
        var headingsChosen = await Next(mark, snapshot => Find(snapshot, "Headings") is { } radio && Bool(radio, "isSelected") == true);
        Record("Choosing Headings reads the dialog again with it selected", headingsChosen is not null, Describe(headingsChosen));

        // 5. Another of the process's windows, then back.
        mark = Count();
        Form? other = null;
        form.Invoke(() =>
        {
            other = new Form { Text = "Second window", Width = 300, Height = 150 };
            other.Controls.Add(new Button { Text = "OK", Left = 20, Top = 20 });
            other.Show(form);
            other.Activate();
            other.Controls[0].Focus();
        });
        var second = await Next(mark, snapshot => Name(Elements(snapshot)[0]) == "Second window");
        Record("Focus entering another window of the process reads that window", second is { } window && Reason(window) == WindowSnapshotRecords.FocusEnteredReason,
            Describe(second));
        mark = Count();
        form.Invoke(() =>
        {
            other!.Close();
            form.Activate();
            form.Tree.Focus();
        });
        var back = await Next(mark, snapshot => Name(Elements(snapshot)[0]) == ElementsListForm.Title && Reason(snapshot) == WindowSnapshotRecords.FocusEnteredReason);
        Record("Focus coming back reads the dialog again", back is not null, Describe(back));

        // 6. A tree past the limits.
        mark = Count();
        form.Invoke(() => form.Fill([.. Enumerable.Range(1, 6_000).Select(index => $"Link {index}")]));
        var large = await Next(mark, snapshot => Text(snapshot, "truncated") is not null, TimeSpan.FromSeconds(15));
        Record(
            "A tree of 6,000 items stops at a limit and says so",
            large is { } big && (Text(big, "truncated") == WindowSnapshotRecords.ElementLimit
                ? Elements(big).Count == WindowSnapshotRecords.MaximumElements
                : big.GetProperty("readNanoseconds").GetInt64() >= WindowSnapshotRecords.MaximumReadNanoseconds),
            Describe(large));

        var end = new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow);
        await snapshots.StopAsync(end, CancellationToken.None);
        await uiAutomation.StopAsync(end, CancellationToken.None);

        // Every record passes the recorder's checks, in order in its stream.
        var validator = new EventRecordValidator("probe");
        RecorderEvent[] all;
        lock (Records)
        {
            all = [.. Records];
        }

        var issues = all.SelectMany(record => validator.Validate(record).Select(issue => $"{record.EventType} {record.Sequence}: {issue.Code} {issue.Message}")).ToList();
        Record("Every record passes the recorder's checks", issues.Count == 0, string.Join("; ", issues.Take(5)));
        var omissions = all.Where(record => record.EventType == "collector-omission").Select(record => record.Payload.GetRawText()).ToList();
        Record("No evidence was dropped", omissions.Count == 0, string.Join("; ", omissions));

        var path = Path.Combine(output, "snapshot-probe.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            results = Results.Select(result => new { result.Check, result.Passed, result.Detail }),
            snapshots = all.Where(record => record.EventType == WindowSnapshotRecords.EventType).Select(record => new
            {
                record.MonotonicNanoseconds,
                reason = Reason(record.Payload),
                summary = WindowSnapshots.Summary(record.Payload),
                readNanoseconds = record.Payload.GetProperty("readNanoseconds").GetInt64(),
                elementCount = record.Payload.GetProperty("elementCount").GetInt32(),
                truncated = Text(record.Payload, "truncated")
            }),
            uiaEvents = all.Count(record => record.CollectorType == "windows.ui-automation" && record.Channel == WindowSnapshotRecords.Channel)
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote {path}");
    }

    private sealed class Sink : IRecorderEventSink
    {
        public bool TryWrite(RecorderEvent record)
        {
            lock (Records)
            {
                Records.Add(record);
            }

            return true;
        }
    }

    private static int Count()
    {
        lock (Records)
        {
            return Records.Count;
        }
    }

    // The first snapshot after the mark that matches, waiting up to the time given.
    private static async Task<JsonElement?> Next(int mark, Func<JsonElement, bool> matches, TimeSpan? within = null)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < (within ?? TimeSpan.FromSeconds(5)))
        {
            lock (Records)
            {
                foreach (var record in Records.Skip(mark))
                {
                    if (record.EventType == WindowSnapshotRecords.EventType && matches(record.Payload))
                    {
                        return record.Payload;
                    }
                }
            }

            await Task.Delay(100);
        }

        return null;
    }

    private static void Record(string check, bool passed, string detail)
    {
        Results.Add((check, passed, detail));
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {check}" + (passed || detail.Length == 0 ? "" : $"{Environment.NewLine}      {detail}"));
    }

    private static string Describe(JsonElement? snapshot) =>
        snapshot is { } found ? $"{Reason(found)}: {WindowSnapshots.Summary(found)}" : "no such snapshot was recorded";

    private static List<JsonElement> Elements(JsonElement snapshot) => [.. snapshot.GetProperty("elements").EnumerateArray()];

    private static JsonElement? Find(JsonElement snapshot, string name) =>
        Elements(snapshot).Where(element => Name(element) == name).Select(element => (JsonElement?)element).FirstOrDefault();

    private static int Count(JsonElement snapshot, string controlType) =>
        Elements(snapshot).Count(element => Text(element, "controlType") == controlType);

    private static string? Reason(JsonElement snapshot) => Text(snapshot, "reason");

    private static string? Name(JsonElement element) => Text(element, "name");

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Bool(JsonElement? element, string name) =>
        element is { } found && found.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;
}

// Built like NVDA's Elements List: standard Win32 controls, each labelled by
// the label before it.
internal sealed class ElementsListForm : Form
{
    public const string Title = "Elements List probe";

    public ElementsListForm()
    {
        Text = Title;
        Width = 520;
        Height = 420;
        StartPosition = FormStartPosition.CenterScreen;
        var type = new GroupBox { Text = "Type:", Left = 10, Top = 10, Width = 480, Height = 50, TabIndex = 0 };
        Links = new RadioButton { Text = "Links", Left = 10, Top = 20, Width = 100, Checked = true, TabIndex = 0 };
        Headings = new RadioButton { Text = "Headings", Left = 120, Top = 20, Width = 100, TabIndex = 1 };
        type.Controls.Add(Links);
        type.Controls.Add(Headings);
        var label = new Label { Text = "Filter by:", Left = 10, Top = 72, Width = 70, TabIndex = 1 };
        Filter = new TextBox { Left = 90, Top = 70, Width = 400, TabIndex = 2 };
        Tree = new TreeView { Left = 10, Top = 100, Width = 480, Height = 230, TabIndex = 3, HideSelection = false };
        var activate = new Button { Text = "Activate", Left = 10, Top = 340, TabIndex = 4 };
        var moveTo = new Button { Text = "Move to", Left = 100, Top = 340, TabIndex = 5 };
        Controls.AddRange([type, label, Filter, Tree, activate, moveTo]);
        Fill(["Contact Us", "Donate", "CNIB Deafblind Community Services"]);
    }

    public RadioButton Links { get; }
    public RadioButton Headings { get; }
    public TextBox Filter { get; }
    public TreeView Tree { get; }

    public void Fill(IReadOnlyList<string> names)
    {
        Tree.BeginUpdate();
        Tree.Nodes.Clear();
        foreach (var name in names)
        {
            Tree.Nodes.Add(name);
        }

        Tree.EndUpdate();
        Tree.SelectedNode = Tree.Nodes.Count > 0 ? Tree.Nodes[0] : null;
    }
}
