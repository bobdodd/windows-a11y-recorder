using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Recorder.Collectors.Input;
using Recorder.Contracts;
using Recorder.Session;

namespace MouseHookProbe;

// A Windows integration test of the mouse hook
// (docs/architecture/screen-reader-activity.md, "The mouse hook"). It runs
// the recorder's keyboard and mouse hook collector and raw input collector
// in this process, and injects mouse input with SendInput, as head pointer
// or eye tracking software does, over a window of its own: moves, each
// button pressed and released, a wheel turned each way, and a click marked
// as from a touch screen. It checks the hook's records of them, the
// reinstallation when a screen reader starts, that every record passes the
// recorder's checks, and the player's outcome of each. It needs no screen
// reader. The pointer is moved and clicked only inside the probe's window;
// leave the mouse still while it runs.
//
// Run: MouseHookProbe.exe [output folder]
// It writes mouse-hook-probe.json there (default: the current folder) and
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

        // The hook gives points in physical pixels; so does the window here.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var form = new Form
        {
            Text = "Mouse hook probe",
            Width = 600,
            Height = 400,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
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

    private static void Run(Form form, ManualResetEventSlim ready, string output)
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

    // One injected input: what it is, and the hook record it should make.
    private sealed record Step(string Name, uint Flags, int MouseData, long Extra, string Kind, string? Button, int? WheelDelta);

    private static async Task RunChecks(Form form, string output)
    {
        var clock = new SessionClock();
        var context = new CollectorInitializationContext("probe", output, clock, new Sink());
        var hooks = new KeyboardHookCollector();
        var rawInput = new RawInputCollector();
        rawInput.KeyboardObserved += hooks.ObserveRawKey;
        rawInput.MouseObserved += hooks.ObserveRawMouse;
        await hooks.InitializeAsync(context, CancellationToken.None);
        await rawInput.InitializeAsync(context, CancellationToken.None);
        var boundary = new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow);
        Record("The hook collector started", (await hooks.StartAsync(boundary, CancellationToken.None)).Accepted, "");
        Record("The raw input collector started", (await rawInput.StartAsync(boundary, CancellationToken.None)).Accepted, "");

        var first = Installations().Select(item => (JsonElement?)item).FirstOrDefault();
        Record(
            "The mouse hook was installed at the start",
            first is { } start && Text(start, "reason") == KeyboardHookRecords.RecordingStartedReason &&
            Bool(start, "installed") == true && Int(start, "installation") == 1,
            first?.GetRawText() ?? "no mouse hook installation was recorded");

        // The window's middle, and a point left of it, in screen pixels.
        var (middle, left) = form.Invoke(() =>
        {
            var centre = form.PointToScreen(new Point(form.ClientSize.Width / 2, form.ClientSize.Height / 2));
            return (centre, new Point(centre.X - 100, centre.Y));
        });

        var injectedFrom = clock.GetElapsedNanoseconds();
        Move(left);
        await Task.Delay(50);
        Move(middle);
        await Task.Delay(50);

        // Every button pressed and released, the wheels turned, and a click
        // marked as from a touch screen (the signature of
        // https://learn.microsoft.com/en-us/windows/win32/tablet/system-events-and-mouse-messages).
        const long touch = 0xFF515780;
        Step[] steps =
        [
            new("left down", 0x0002, 0, 0, MouseHookRecords.ButtonDownKind, "left", null),
            new("left up", 0x0004, 0, 0, MouseHookRecords.ButtonUpKind, "left", null),
            new("right down", 0x0008, 0, 0, MouseHookRecords.ButtonDownKind, "right", null),
            new("right up", 0x0010, 0, 0, MouseHookRecords.ButtonUpKind, "right", null),
            new("middle down", 0x0020, 0, 0, MouseHookRecords.ButtonDownKind, "middle", null),
            new("middle up", 0x0040, 0, 0, MouseHookRecords.ButtonUpKind, "middle", null),
            new("X button 1 down", 0x0080, 1, 0, MouseHookRecords.ButtonDownKind, "x1", null),
            new("X button 1 up", 0x0100, 1, 0, MouseHookRecords.ButtonUpKind, "x1", null),
            new("wheel toward the user", 0x0800, -120, 0, MouseHookRecords.WheelKind, null, -120),
            new("horizontal wheel to the right", 0x1000, 120, 0, MouseHookRecords.HorizontalWheelKind, null, 120),
            new("touch down", 0x0002, 0, touch, MouseHookRecords.ButtonDownKind, "left", null),
            new("touch up", 0x0004, 0, touch, MouseHookRecords.ButtonUpKind, "left", null)
        ];
        foreach (var step in steps)
        {
            Send(0, 0, step.Flags, step.MouseData, step.Extra);
            await Task.Delay(30);
        }

        await Task.Delay(500);
        var injectedTo = clock.GetElapsedNanoseconds();
        var injected = HookEvents().Where(item => item.Payload.GetProperty("injected").GetBoolean()).ToList();
        var moves = injected.Where(item => Text(item.Payload, "kind") == MouseHookRecords.MoveKind).ToList();
        Record(
            "The injected moves were recorded at the points moved to",
            moves.Any(item => Near(item.Payload, left)) && moves.Any(item => Near(item.Payload, middle)),
            $"moved to ({left.X}, {left.Y}) and ({middle.X}, {middle.Y}); recorded " +
            string.Join(", ", moves.Select(item => $"({Int(item.Payload, "x")}, {Int(item.Payload, "y")})")));

        var others = injected.Where(item => Text(item.Payload, "kind") != MouseHookRecords.MoveKind).ToList();
        Record(
            "Each button and wheel was recorded once, in order, with its kind and button",
            others.Count == steps.Length &&
            others.Zip(steps).All(pair => Text(pair.First.Payload, "kind") == pair.Second.Kind && Text(pair.First.Payload, "button") == pair.Second.Button),
            string.Join(", ", others.Select(item => $"{Text(item.Payload, "button")} {Text(item.Payload, "kind")}")));
        Record(
            "The wheels' turns were recorded as given",
            others.Count == steps.Length &&
            others.Zip(steps).All(pair => Int(pair.First.Payload, "wheelDelta") == pair.Second.WheelDelta),
            string.Join(", ", others.Select(item => Int(item.Payload, "wheelDelta")?.ToString() ?? "none")));
        Record(
            "The clicks were recorded at the window's middle",
            others.All(item => Near(item.Payload, middle)),
            string.Join(", ", others.Select(item => $"({Int(item.Payload, "x")}, {Int(item.Payload, "y")})")));
        Record(
            "Every injected event has the injected flag",
            injected.Count >= steps.Length + 2 &&
            injected.All(item => (Int(item.Payload, "flags")! & MouseHookRecords.InjectedFlag) != 0),
            $"{injected.Count} injected events");
        Record(
            "Only the clicks marked as touch were recorded as from a touch screen",
            others.Zip(steps).All(pair => Text(pair.First.Payload, "penOrTouch") == (pair.Second.Extra == touch ? "touch" : null)),
            string.Join(", ", others.Select(item => Text(item.Payload, "penOrTouch") ?? "none")));
        Record(
            "The records were made while the input was injected, in order",
            injected.All(item => item.MonotonicNanoseconds >= injectedFrom && item.MonotonicNanoseconds <= injectedTo) &&
            injected.Zip(injected.Skip(1)).All(pair =>
                pair.Second.MonotonicNanoseconds >= pair.First.MonotonicNanoseconds &&
                Int(pair.Second.Payload, "eventTimeMilliseconds") >= Int(pair.First.Payload, "eventTimeMilliseconds")),
            "");
        var physical = HookEvents().Count(item => !item.Payload.GetProperty("injected").GetBoolean());
        Record("No mouse input from a device came while the probe ran", physical == 0,
            $"{physical} events from a device; leave the mouse still and run again");

        // Raw input records injected input with no device.
        var rawButtons = RawMouse().Where(item =>
            item.Payload.GetProperty("deviceHandle").GetInt64() == 0 && Int(item.Payload, "buttonFlags") != 0).ToList();
        Record("Raw input recorded the buttons and wheels with no device", rawButtons.Count == steps.Length,
            $"{rawButtons.Count} raw input records with buttons and no device");

        // A screen reader starts: both hooks are installed again at once,
        // then every second while it runs.
        var installationsBefore = Installations().Count;
        hooks.SetScreenReaderRunning(true, started: true);
        await Task.Delay(1500);
        hooks.SetScreenReaderRunning(false, started: false);
        Move(left);
        await Task.Delay(300);
        var later = Installations().Skip(installationsBefore).ToList();
        var restarted = later.Select(item => (JsonElement?)item).FirstOrDefault();
        var firstEvents = HookEvents().Count(item => Int(item.Payload, "installation") == 1);
        Record(
            "A screen reader starting installs the mouse hook again, counting the previous installation's events",
            restarted is { } again && Text(again, "reason") == KeyboardHookRecords.ScreenReaderStartedReason &&
            Int(again, "installation") == 2 && Int(again, "previousInstallation") == 1 && Int(again, "previousEvents") == firstEvents,
            restarted?.GetRawText() ?? "no installation after the start");
        Record(
            "While it runs the mouse hook is installed again every second",
            later.Any(item => Text(item, "reason") == KeyboardHookRecords.RefreshReason),
            string.Join("; ", later.Select(item => Text(item, "reason"))));
        var last = HookEvents().LastOrDefault();
        Record(
            "A move after that is recorded with the latest installation",
            last is { } move && Int(move.Payload, "installation") == later.Select(item => Int(item, "installation")).Max(),
            last?.Payload.GetRawText() ?? "no event");
        var maximum = later.Select(item => item.TryGetProperty("previousMaxCallbackMicroseconds", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0).DefaultIfEmpty(0).Max();
        Record(
            "The hook's longest call is well within the time Windows allows (under 100 ms)",
            maximum < 100_000,
            $"{maximum:0.0} microseconds");
        Record(
            "The keyboard hook was installed again with the mouse hook",
            KeyboardInstallations().Count(item => Text(item, "reason") == KeyboardHookRecords.ScreenReaderStartedReason) == 1,
            "");

        var end = new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow);
        await rawInput.StopAsync(end, CancellationToken.None);
        await hooks.StopAsync(end, CancellationToken.None);

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

        // The player's outcome of each, with no screen reader running.
        var mouse = new MouseActivity(all.Select(record =>
            new KeyEvidenceRecord($"{record.Channel}/{record.Sequence}", record.MonotonicNanoseconds, record.Channel, record.EventType, record.Payload)));
        var outcomes = injected.Select(item => mouse.Of($"{item.Channel}/{item.Sequence}")?.Text ?? "none").ToList();
        Record(
            "The player gives each injected event as injected, and the touch clicks as from a touch screen",
            outcomes.Count(text => text.EndsWith(", injected by software", StringComparison.Ordinal)) == injected.Count - 2 &&
            outcomes.Count(text => text.EndsWith(", from a touch screen, injected", StringComparison.Ordinal)) == 2,
            string.Join("; ", outcomes.Distinct().Take(8)));

        var path = Path.Combine(output, "mouse-hook-probe.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            results = Results.Select(result => new { result.Check, result.Passed, result.Detail }),
            installations = Installations().Select(item => JsonSerializer.Deserialize<object>(item.GetRawText())),
            events = HookEvents().Select(item => new { item.MonotonicNanoseconds, payload = JsonSerializer.Deserialize<object>(item.Payload.GetRawText()) }),
            outcomes
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

    private static List<RecorderEvent> Of(string channel, string eventType)
    {
        lock (Records)
        {
            return [.. Records.Where(record => record.Channel == channel && record.EventType == eventType)];
        }
    }

    private static List<RecorderEvent> HookEvents() => Of(MouseHookRecords.Channel, MouseHookRecords.EventType);

    private static List<JsonElement> Installations() =>
        [.. Of(MouseHookRecords.Channel, MouseHookRecords.InstalledEventType).Select(record => record.Payload)];

    private static List<JsonElement> KeyboardInstallations() =>
        [.. Of(KeyboardHookRecords.Channel, KeyboardHookRecords.InstalledEventType).Select(record => record.Payload)];

    private static List<RecorderEvent> RawMouse() => Of("input.mouse", "raw-mouse");

    // Within a pixel, for the rounding of absolute coordinates.
    private static bool Near(JsonElement payload, Point point) =>
        Int(payload, "x") is { } x && Int(payload, "y") is { } y && Math.Abs(x - point.X) <= 1 && Math.Abs(y - point.Y) <= 1;

    // An absolute move, over the whole virtual screen
    // (https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput).
    private static void Move(Point point)
    {
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        var x = (int)Math.Round((point.X - left) * 65535.0 / (width - 1));
        var y = (int)Math.Round((point.Y - top) * 65535.0 / (height - 1));
        Send(x, y, 0x0001 | 0x8000 | 0x4000, 0, 0);
    }

    private static void Send(int dx, int dy, uint flags, int mouseData, long extra)
    {
        var input = new Input
        {
            Type = 0,
            Mouse = new MouseInput
            {
                X = dx,
                Y = dy,
                MouseData = unchecked((uint)mouseData),
                Flags = flags,
                Time = 0,
                ExtraInfo = (nuint)(ulong)extra
            }
        };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
        {
            throw new InvalidOperationException($"SendInput failed with error {Marshal.GetLastWin32Error()}.");
        }
    }

    private static void Record(string check, bool passed, string detail)
    {
        Results.Add((check, passed, detail));
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {check}" + (passed || detail.Length == 0 ? "" : $"{Environment.NewLine}      {detail}"));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var read) ? read : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
