using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Accessibility;
using IAccessible = Accessibility.IAccessible;
using Uia = Interop.UIAutomationClient;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Session;

namespace AccessibilityRequestProbe;

// A Windows integration test of protocol 0.60's accessibility request
// records (docs/architecture/accessibility-requests.md). It runs the
// recorder's browser evidence receiver in this process, which launches the
// instrumented Chromium given on the test page
// (tests/fixtures/accessibility-requests), served from a local port. Then it
// asks of the page what an assistive technology asks: hit tests through
// Microsoft Active Accessibility, recursing through accHitTest as NVDA does;
// accDoDefaultAction and accSelect to focus; IAccessible2 scrollTo, as NVDA's
// browse mode does; and UI Automation's ScrollIntoView. It checks the
// records Chromium sends of each, that every record passes the recorder's
// checks, and that the player gives each an outcome. It needs no screen
// reader, and moves neither the pointer nor the keyboard focus outside the
// browser it launches.
//
// Run: AccessibilityRequestProbe.exe <chrome.exe> [output folder]
// It writes accessibility-request-probe.json there (default: the current
// folder) and exits 0 when no check fails.
public static class Program
{
    private const string Title = "Accessibility requests test page";
    private const int ChildIdSelf = 0;
    private const int SelflagTakeFocus = 0x1;
    private const int Ia2ScrollTypeAnywhere = 6;
    private const int UiaNamePropertyId = 30005;
    private const int UiaScrollItemPatternId = 10017;
    private const long Millisecond = 1_000_000;

    private static readonly List<RecorderEvent> Records = [];
    private static readonly List<(string Check, string Status, string Detail)> Results = [];
    private static readonly SessionClock Clock = new();

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length < 1 || !File.Exists(args[0]))
        {
            Console.WriteLine("Usage: AccessibilityRequestProbe <chrome.exe> [output folder]");
            return 2;
        }

        var output = args.Length > 1 ? args[1] : Environment.CurrentDirectory;
        Directory.CreateDirectory(output);

        // Chromium's hit tests and bounds are in physical pixels; so are this
        // process's, when it is aware of each monitor's scale.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var server = new FixtureServer(Path.Combine(AppContext.BaseDirectory, "fixture"));
        var receiver = new BrowserEvidenceReceiver(new BrowserEvidenceReceiverOptions
        {
            ChromiumExecutablePath = Path.GetFullPath(args[0]),
            StartUrl = $"http://127.0.0.1:{server.Port}/index.html"
        });

        try
        {
            Run(receiver);
        }
        catch (Exception exception)
        {
            Record("The probe ran to the end", false, exception.ToString());
        }
        finally
        {
            try
            {
                receiver.StopAsync(new SessionBoundary(Clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                receiver.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Stopping the browser: {exception.Message}");
            }
        }

        Write(output);
        var failed = Results.Count(result => result.Status == "failed");
        var notRun = Results.Count(result => result.Status == "not run");
        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"No check failed: {Results.Count - notRun} passed, {notRun} not run."
            : $"{failed} of {Results.Count} checks failed; {notRun} not run.");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(BrowserEvidenceReceiver receiver)
    {
        var context = new CollectorInitializationContext("probe", Path.GetTempPath(), Clock, new Sink());
        receiver.InitializeAsync(context, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        var started = receiver.StartAsync(new SessionBoundary(Clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        Record("The browser started", started.Accepted, started.Message ?? started.ErrorCode ?? "");
        if (!started.Accepted)
        {
            return;
        }

        var window = WaitForWindow(TimeSpan.FromSeconds(60));
        Record("The test page opened", window != IntPtr.Zero, window == IntPtr.Zero ? $"No window titled \"{Title}\" within 60 s." : "");
        if (window == IntPtr.Zero)
        {
            return;
        }

        SetForegroundWindow(window);
        var root = FromWindow(window);
        var document = WaitFor(() => Find(root, Title, role: 15), TimeSpan.FromSeconds(20));
        Record("The page's document is in the accessibility tree", document is not null, "");
        if (document is null)
        {
            return;
        }

        // As NVDA does when it meets a document, ask for IAccessible2, which
        // tells Chromium an assistive technology is present.
        var probe = QueryIa2(document.Value.Accessible);
        if (probe != IntPtr.Zero)
        {
            Marshal.Release(probe);
        }

        Thread.Sleep(2000);
        document = Find(root, Title, role: 15) ?? document;
        var doc = document.Value.Accessible;

        HitTestCheck("button", root, doc, "Show message", "button");
        ActivateCheck(root, doc);
        FocusCheck(root, doc);
        var scrolled = ScrollIa2Check(doc, "Far down the page");
        ScrollUiaCheck(window, doc, "Section four");
        if (scrolled)
        {
            // The page is scrolled down, so content is under the sticky
            // header.
            HitTestCheck("sticky header", root, doc, "Home", "link");
        }
        else
        {
            Result("The sticky header's hit test", "not run", "The page did not scroll.");
        }

        FrameCheck(root, doc);
        RecordsCheck();
    }

    // --- The checks ------------------------------------------------------------

    private static void HitTestCheck(string what, IAccessible root, IAccessible doc, string name, string role)
    {
        if (Find(doc, name) is not { } target || Center(target) is not { } point)
        {
            Record($"The {what}'s hit test", false, $"\"{name}\" was not found, or has no location.");
            return;
        }

        var first = Clock.GetElapsedNanoseconds();
        var reached = HitTestLikeNvda(root, point.X, point.Y);
        var firstEnded = Clock.GetElapsedNanoseconds();
        Thread.Sleep(600);
        var second = Clock.GetElapsedNanoseconds();
        var reachedAgain = HitTestLikeNvda(root, point.X, point.Y);
        Thread.Sleep(600);

        // The two requests are told apart halfway through the pause between
        // them: a browser record's time is mapped from the browser's clock,
        // which can be a millisecond or two from this process's (first run,
        // 2026-10-12).
        var split = firstEnded + (second - firstEnded) / 2;
        var asked = Since(BrowserEvidenceEventTypes.AccessibilityHitTest, first)
            .Where(record => Near(record.Payload, point)).ToList();
        var firstAsked = asked.Where(record => record.MonotonicNanoseconds < split).ToList();
        var againAsked = asked.Where(record => record.MonotonicNanoseconds >= split).ToList();
        Record(
            $"The {what}'s hit test at {point.X}, {point.Y} was recorded",
            firstAsked.Count > 0,
            $"{firstAsked.Count} hit test records; the probe reached {Describe(reached)}.");

        // Chromium's precise result is the deepest object at the point: for
        // a button or link, the text inside it, of the same name (first run,
        // 2026-10-12).
        var result = Since(BrowserEvidenceEventTypes.AccessibilityHitTestResult, first)
            .FirstOrDefault(record => NodeName(record.Payload, "result") == name);
        Record(
            $"The page's precise hit test found {role} \"{name}\" or its text",
            result is not null && NodeRole(result.Payload, "result") is { } found && (found == role || found == "staticText"),
            "Results: " + string.Join("; ", Since(BrowserEvidenceEventTypes.AccessibilityHitTestResult, first)
                .Where(record => record.MonotonicNanoseconds < split)
                .Select(record => $"{NodeRole(record.Payload, "result")} \"{NodeName(record.Payload, "result")}\"")));

        var given = againAsked.Select(record =>
            $"{Text(record.Payload, "basis")}: {NodeRole(record.Payload, "answer")} \"{NodeName(record.Payload, "answer")}\"").ToList();
        Record(
            $"The second hit test was given \"{name}\" from the page's last precise result",
            againAsked.Any(record => Text(record.Payload, "basis") == BrowserAccessibilityRequests.LastPreciseResult &&
                NodeName(record.Payload, "answer") == name),
            $"Given: {string.Join("; ", given)}. The probe reached {Describe(reachedAgain)}.");

        // The first hit test's guess, which can differ from the precise
        // result where content is under the point, as under a sticky header.
        if (firstAsked.FirstOrDefault() is { } guess)
        {
            Result(
                $"The {what}'s first answer, for information",
                "passed",
                $"{Text(guess.Payload, "basis")}: {NodeRole(guess.Payload, "answer")} \"{NodeName(guess.Payload, "answer")}\".");
        }

        if (result is not null)
        {
            var tree = Text(Node(result.Payload, "result"), "treeId");
            var node = Number(Node(result.Payload, "result"), "nodeId");
            Record(
                $"The renderer's hit test for the {what} found the same node",
                Since(BrowserEvidenceEventTypes.AccessibilityHitTestComputed, first)
                    .Any(record => Text(record.Payload, "treeId") == tree && Number(record.Payload, "nodeId") == node),
                $"Node {node} of tree {tree}.");
        }
    }

    private static void ActivateCheck(IAccessible root, IAccessible doc)
    {
        if (Find(doc, "Show message") is not { } button)
        {
            Record("accDoDefaultAction on the button", false, "\"Show message\" was not found.");
            return;
        }

        var mark = Clock.GetElapsedNanoseconds();
        button.Accessible.accDoDefaultAction(button.Child);
        Thread.Sleep(800);
        ActionRecorded("accDoDefaultAction on the button", mark, "doDefault", "Show message");
        Record("The page ran the button's click handler", Find(doc, "Button activated") is not null, "");
    }

    private static void FocusCheck(IAccessible root, IAccessible doc)
    {
        if (Find(doc, "Read more about the test") is not { } link)
        {
            Record("accSelect to focus the link", false, "\"Read more about the test\" was not found.");
            return;
        }

        var mark = Clock.GetElapsedNanoseconds();
        link.Accessible.accSelect(SelflagTakeFocus, link.Child);
        Thread.Sleep(800);
        ActionRecorded("accSelect to focus the link", mark, "focus", "Read more about the test");
    }

    private static bool ScrollIa2Check(IAccessible doc, string name)
    {
        if (Find(doc, name) is not { } heading)
        {
            Record("IAccessible2 scrollTo on a heading", false, $"\"{name}\" was not found.");
            return false;
        }

        var ia2 = QueryIa2(heading.Accessible);
        if (ia2 == IntPtr.Zero)
        {
            Record("IAccessible2 scrollTo on a heading", false, "The heading has no IAccessible2 interface.");
            return false;
        }

        var mark = Clock.GetElapsedNanoseconds();
        int hr;
        try
        {
            hr = ScrollTo(ia2, Ia2ScrollTypeAnywhere);
        }
        finally
        {
            Marshal.Release(ia2);
        }

        Thread.Sleep(1000);
        Record("IAccessible2 scrollTo returned S_OK", hr == 0, $"0x{hr:X8}");
        ActionRecorded($"IAccessible2 scrollTo on heading \"{name}\"", mark, "scrollToMakeVisible", name);
        return Since(BrowserEvidenceEventTypes.AccessibilityActionSent, mark).Any(record => Text(record.Payload, "action") == "scrollToMakeVisible");
    }

    private static void ScrollUiaCheck(IntPtr window, IAccessible doc, string name)
    {
        Uia.IUIAutomationElement? element;
        string provider;
        try
        {
            var automation = new Uia.CUIAutomation8Class();
            element = automation.ElementFromHandle(window)
                .FindFirst(Uia.TreeScope.TreeScope_Descendants, automation.CreatePropertyCondition(UiaNamePropertyId, name));
            provider = element?.CurrentProviderDescription ?? "";
        }
        catch (COMException exception)
        {
            Record("UI Automation ScrollIntoView on a heading", false, exception.Message);
            return;
        }

        if (element?.GetCurrentPattern(UiaScrollItemPatternId) is not Uia.IUIAutomationScrollItemPattern pattern)
        {
            Result(
                "UI Automation ScrollIntoView on a heading",
                "not run",
                element is null
                    ? $"UI Automation found no \"{name}\"."
                    : $"\"{name}\" has no ScrollItem pattern. Provider: {provider}");
            return;
        }

        var mark = Clock.GetElapsedNanoseconds();
        pattern.ScrollIntoView();
        Thread.Sleep(1000);
        ActionRecorded($"UI Automation ScrollIntoView on heading \"{name}\"", mark, "scrollToMakeVisible", name);
        Result("The UI Automation provider, for information", "passed", provider);
    }

    private static void FrameCheck(IAccessible root, IAccessible doc)
    {
        if (Find(doc, "Frame button") is not { } button)
        {
            Record("The frame's button is in the tree", false, "\"Frame button\" was not found.");
            return;
        }

        var ia2 = QueryIa2(button.Accessible);
        if (ia2 != IntPtr.Zero)
        {
            try
            {
                ScrollTo(ia2, Ia2ScrollTypeAnywhere);
            }
            finally
            {
                Marshal.Release(ia2);
            }

            Thread.Sleep(1000);
        }

        var mark = Clock.GetElapsedNanoseconds();
        HitTestCheck("frame's button", root, doc, "Frame button", "button");
        var computed = Since(BrowserEvidenceEventTypes.AccessibilityHitTestComputed, mark);
        var documents = computed.Select(record => Text(Node(record.Payload, "context"), "documentToken")).Distinct().Count();
        Record(
            "The hit test went from the page's renderer into the frame's document",
            documents >= 2 && computed.Any(record => record.Payload.TryGetProperty("childTree", out var child) && child.ValueKind == JsonValueKind.True),
            $"{computed.Count} renderer hit tests in {documents} documents.");
    }

    private static void RecordsCheck()
    {
        // Every record passes the recorder's checks, in order in its stream.
        var requests = Requests();
        var validator = new EventRecordValidator("probe");
        RecorderEvent[] all;
        lock (Records)
        {
            all = [.. Records];
        }

        var invalid = all.SelectMany(record => validator.Validate(record)
            .Select(issue => $"{record.EventType} {record.Sequence}: {issue.Code} {issue.Message}")).ToList();
        Record("Every record passes the recorder's checks", requests.Count > 0 && invalid.Count == 0,
            $"{all.Length} records, {requests.Count} of them accessibility requests. " + string.Join("; ", invalid.Take(5)));
        var outcomes = new AccessibilityRequests(Evidence(requests));
        Record("The player gives every request record an outcome",
            requests.All(record => outcomes.Of(record.EventId) is not null), "");
    }

    private static void ActionRecorded(string what, long mark, string action, string name)
    {
        var sent = Since(BrowserEvidenceEventTypes.AccessibilityActionSent, mark)
            .FirstOrDefault(record => Text(record.Payload, "action") == action);
        Record(
            $"{what} was recorded as {action} sent to {(sent is null ? "the page" : $"\"{NodeName(sent.Payload, "target")}\"")}",
            sent is not null && NodeName(sent.Payload, "target") == name,
            "Sent: " + string.Join("; ", Since(BrowserEvidenceEventTypes.AccessibilityActionSent, mark)
                .Select(record => $"{Text(record.Payload, "action")} to \"{NodeName(record.Payload, "target")}\"")));
        if (sent is null)
        {
            return;
        }

        var node = Number(sent.Payload, "targetNodeId");
        var tree = Text(sent.Payload, "treeId");
        Record(
            $"The page's renderer received the {action}",
            Since(BrowserEvidenceEventTypes.AccessibilityActionReceived, mark).Any(record =>
                Text(record.Payload, "action") == action && Number(record.Payload, "targetNodeId") == node &&
                Text(record.Payload, "treeId") == tree),
            "");
    }

    // --- Microsoft Active Accessibility and IAccessible2 ------------------------

    private readonly record struct Found(IAccessible Accessible, object Child);

    // As NVDA's IAccessibleHandler does: accHitTest on each object in turn,
    // until one answers with itself or a simple child.
    private static Found? HitTestLikeNvda(IAccessible root, int x, int y)
    {
        var current = root;
        for (var depth = 0; depth < 64; depth++)
        {
            object? answer;
            try
            {
                answer = current.accHitTest(x, y);
            }
            catch (COMException)
            {
                return new Found(current, ChildIdSelf);
            }

            switch (answer)
            {
                case IAccessible next:
                    current = next;
                    continue;
                case int child when child != ChildIdSelf:
                    return new Found(current, child);
                default:
                    return new Found(current, ChildIdSelf);
            }
        }

        return new Found(current, ChildIdSelf);
    }

    private static Found? Find(IAccessible root, string name, int? role = null)
    {
        var queue = new Queue<IAccessible>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited++ < 5000)
        {
            var item = queue.Dequeue();
            string? itemName;
            object? itemRole;
            try
            {
                itemName = item.get_accName(ChildIdSelf);
                itemRole = item.get_accRole(ChildIdSelf);
            }
            catch (COMException)
            {
                continue;
            }

            if (itemName == name && (role is null || itemRole is int value && value == role))
            {
                return new Found(item, ChildIdSelf);
            }

            foreach (var child in Children(item))
            {
                queue.Enqueue(child);
            }
        }

        return null;
    }

    private static List<IAccessible> Children(IAccessible item)
    {
        try
        {
            var count = item.accChildCount;
            if (count <= 0)
            {
                return [];
            }

            var children = new object[count];
            if (AccessibleChildren(item, 0, count, children, out var obtained) < 0)
            {
                return [];
            }

            return [.. children.Take(obtained).OfType<IAccessible>()];
        }
        catch (COMException)
        {
            return [];
        }
    }

    private static Point? Center(Found found)
    {
        try
        {
            found.Accessible.accLocation(out var left, out var top, out var width, out var height, found.Child);
            return width > 0 && height > 0 ? new Point(left + width / 2, top + height / 2) : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string Describe(Found? found)
    {
        if (found is not { } item)
        {
            return "nothing";
        }

        try
        {
            return $"\"{item.Accessible.get_accName(item.Child)}\"";
        }
        catch (COMException)
        {
            return "an object without a name";
        }
    }

    private static IAccessible FromWindow(IntPtr window)
    {
        var iid = typeof(IAccessible).GUID;
        Marshal.ThrowExceptionForHR(AccessibleObjectFromWindow(window, ObjIdClient, ref iid, out var accessible));
        return (IAccessible)accessible;
    }

    private static IntPtr QueryIa2(IAccessible accessible)
    {
        if (accessible is not IComServiceProvider provider)
        {
            return IntPtr.Zero;
        }

        var service = typeof(IAccessible).GUID;
        var iid = Ia2Iid;
        return provider.QueryService(ref service, ref iid, out var pointer) == 0 ? pointer : IntPtr.Zero;
    }

    // IAccessible2::scrollTo, the fifth method after IAccessible's: 3 of
    // IUnknown, 4 of IDispatch, 21 of IAccessible, then get_nRelations,
    // get_relation, get_relations, role, and scrollTo (Accessible2.idl).
    private static unsafe int ScrollTo(IntPtr ia2, int type)
    {
        var table = *(IntPtr**)ia2;
        var scrollTo = (delegate* unmanaged[Stdcall]<IntPtr, int, int>)table[3 + 4 + 21 + 4];
        return scrollTo(ia2, type);
    }

    private static readonly Guid Ia2Iid = new("E89F726E-C4F4-4c19-BB19-B647D7FA8478");
    private const uint ObjIdClient = 0xFFFFFFFC;

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid iid, out IntPtr pointer);
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr window, uint id, ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object accessible);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren(IAccessible container, int start, int count,
        [Out] object[] children, out int obtained);

    // --- The browser window ---------------------------------------------------

    private static IntPtr WaitForWindow(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var found = IntPtr.Zero;
            EnumWindows((window, _) =>
            {
                var name = new StringBuilder(256);
                var text = new StringBuilder(512);
                GetClassName(window, name, name.Capacity);
                GetWindowText(window, text, text.Capacity);
                if (name.ToString() == "Chrome_WidgetWin_1" && text.ToString().StartsWith(Title, StringComparison.Ordinal))
                {
                    found = window;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero)
            {
                return found;
            }

            Thread.Sleep(250);
        }

        return IntPtr.Zero;
    }

    private static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout) where T : struct
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (probe() is { } value)
            {
                return value;
            }

            Thread.Sleep(500);
        }

        return null;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    // --- The records ------------------------------------------------------------

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

    // Records from a little before the mark, for the browser clock's mapping.
    private static List<RecorderEvent> Since(string eventType, long mark)
    {
        lock (Records)
        {
            return [.. Records.Where(record => record.Channel == BrowserEvidenceChannels.Accessibility &&
                record.EventType == eventType && record.MonotonicNanoseconds >= mark - 20 * Millisecond)];
        }
    }

    private static List<RecorderEvent> Requests()
    {
        lock (Records)
        {
            return [.. Records.Where(record => AccessibilityRequests.IsRequestEvidence(record.Channel, record.EventType))];
        }
    }

    private static List<KeyEvidenceRecord> Evidence(IEnumerable<RecorderEvent> records) =>
        [.. records.Select(record => new KeyEvidenceRecord(
            record.EventId, record.MonotonicNanoseconds, record.Channel, record.EventType, record.Payload))];

    private static bool Near(JsonElement payload, Point point) =>
        payload.TryGetProperty("point", out var at) &&
        Math.Abs((Number(at, "x") ?? int.MinValue) - point.X) <= 2 &&
        Math.Abs((Number(at, "y") ?? int.MinValue) - point.Y) <= 2;

    private static JsonElement Node(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var node) ? node : default;

    private static string? NodeName(JsonElement payload, string name) => Text(Node(payload, name), "name");

    private static string? NodeRole(JsonElement payload, string name) => Text(Node(payload, name), "role");

    private static string? Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static long? Number(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number
            ? item.GetInt64()
            : null;

    private static void Record(string check, bool passed, string detail) => Result(check, passed ? "passed" : "failed", detail);

    private static void Result(string check, string status, string detail)
    {
        Results.Add((check, status, detail));
        Console.WriteLine($"{status.ToUpperInvariant(),-8} {check}{(detail.Length > 0 ? $": {detail}" : "")}");
    }

    private static void Write(string output)
    {
        var requests = Requests();
        var outcomes = new AccessibilityRequests(Evidence(requests));
        var path = Path.Combine(output, "accessibility-request-probe.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            results = Results.Select(result => new { result.Check, result.Status, result.Detail }),
            records = requests.Select(record => new
            {
                record.EventId,
                record.EventType,
                record.MonotonicNanoseconds,
                payload = JsonSerializer.Deserialize<object>(record.Payload.GetRawText()),
                outcome = outcomes.Of(record.EventId) is { } outcome ? new { outcome.Text, outcome.Details } : null
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote {path}");
    }

    // --- The test page's server -------------------------------------------------

    // Serves the test page on a local port, so that its frame is of the same
    // site as the page.
    private sealed class FixtureServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly string _root;

        public FixtureServer(string root)
        {
            _root = root;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            new Thread(Serve) { IsBackground = true, Name = "Fixture server" }.Start();
        }

        public int Port { get; }

        private void Serve()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                using (client)
                {
                    try
                    {
                        var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                        var request = reader.ReadLine() ?? "";
                        while (!string.IsNullOrEmpty(reader.ReadLine()))
                        {
                        }

                        var path = request.Split(' ') is [_, var target, ..] ? target.Split('?')[0].TrimStart('/') : "";
                        var file = Path.Combine(_root, Path.GetFileName(path.Length == 0 ? "index.html" : path));
                        var found = File.Exists(file);
                        var body = found ? File.ReadAllBytes(file) : Encoding.UTF8.GetBytes("Not found");
                        var header = Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\n" +
                            $"Content-Type: {(found ? "text/html; charset=utf-8" : "text/plain")}\r\n" +
                            $"Content-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        stream.Write(header);
                        stream.Write(body);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        public void Dispose() => _listener.Stop();
    }
}
