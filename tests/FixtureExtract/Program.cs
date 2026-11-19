using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.FixtureExtract;
using Recorder.Session;

// Extracts a fixture from a recording of the fixture app's tour
// (docs/architecture/android-fixtures.md, "Rule tests"). Runs on the PC, as
// the checks read Windows OCR's readings, which the Linux build cannot make.
//
// 1. Trims the recording to the fixture app's visits: the events before the
//    first and after the last are left out, as are the sound, the video, and
//    the phone's notifications, so nothing of the phone's own apps is kept.
// 2. Runs the rules on the trimmed recording with Windows OCR, keeping only
//    the pictures they read, what OCR read in them, and their findings.
// 3. Lists every text the trimmed recording holds that is not in the fixture
//    app's source, for a person to read before the fixture is committed.
if (args.Length is < 2 or > 4 || (args.Length > 2 && (args[2] != "--ocr" || args.Length != 4 || args[3] is not ("windows" or "none"))))
{
    Console.Error.WriteLine("Usage: FixtureExtract SESSION_FOLDER OUTPUT_FOLDER [--ocr windows|none]");
    Console.Error.WriteLine("  --ocr none runs the rules without OCR, to try the extractor where Windows OCR is not; such a fixture is not for committing.");
    return 2;
}

var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[0]));
var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1]));
var useOcr = args.Length < 4 || args[3] == "windows";
var source = Path.Combine(session, "recording.mcap");
if (!File.Exists(source))
{
    Console.Error.WriteLine($"No recording file at {source}.");
    return 1;
}

if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
{
    Console.Error.WriteLine($"{output} is not empty; choose an empty or new folder.");
    return 1;
}

var repository = Repository();
using var expected = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository, "android", "fixtures", "expected.json")));
var package = expected.RootElement.GetProperty("package").GetString()!;

// 1. The fixture app's visits, from the first to the last.
var original = await RecordingFilePlayback.OpenAsync(session, source);
long start;
long end;
try
{
    var visits = original.Archive.PhoneScreens.Visits.Where(visit => visit.Package == package).ToList();
    if (visits.Count == 0)
    {
        Console.Error.WriteLine($"The recording has no visit to {package}.");
        return 1;
    }

    start = visits.Min(visit => visit.Start);
    end = visits.Max(visit => visit.End);
    var others = original.Archive.PhoneScreens.Visits.Where(visit => visit.Package != package && visit.Start < end && visit.End > start).ToList();
    foreach (var other in others)
    {
        Console.WriteLine($"Note: visit {other.Number} between the fixture app's visits is to {other.Package}; it is kept, so read its text below.");
    }
}
finally
{
    (original.Archive.Timeline as IDisposable)?.Dispose();
}

Directory.CreateDirectory(output);
var trimmed = Path.Combine(output, AndroidFixtureFiles.Recording);
var kept = new List<StoredEvent>();
var left = new Dictionary<string, int>(StringComparer.Ordinal);
IReadOnlyDictionary<string, string> recording;
using (var reader = RecordingFileReader.Open(source))
{
    recording = reader.Metadata.TryGetValue("recording", out var metadata)
        ? metadata
        : throw new InvalidDataException("The recording file has no recording metadata.");
    foreach (var message in reader.ReadAll().Where(message => RecordingFileBatchTarget.IsEventTopic(message.Channel.Topic)))
    {
        var stored = RecordingEventCodec.Decode(message.Data.Span);
        if (Leave(stored.Event, start, end) is { } why)
        {
            left[why] = left.GetValueOrDefault(why) + 1;
            continue;
        }

        kept.Add(stored);
    }
}

using (var target = new RecordingFileBatchTarget(trimmed, recording, recordState: false))
{
    var events = kept.OrderBy(stored => stored.EventKey).Select(stored => new BufferedEvent(stored.EventKey, stored.Event, string.Empty)).ToList();
    await target.WriteAsync(new EventBatch(events, [], []), CancellationToken.None);
    target.Finish();
}

// The session's manifest, with only the files the fixture keeps.
WriteManifest(session, output, kept.Count, []);

// 2. The rules on the trimmed recording, reading the session's pictures.
var copied = new List<string>();
PhonePixels? Load(string path)
{
    var relative = AndroidFixtureFiles.Relative(output, path);
    var from = Path.Combine(session, relative);
    lock (copied)
    {
        if (!File.Exists(path) && File.Exists(from))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(from, path);
            copied.Add(relative);
        }
    }

    return PhonePictures.Load(path);
}

var opened = await RecordingFilePlayback.OpenAsync(output, trimmed);
try
{
    var archive = opened.Archive;
    var ocr = useOcr ? new RecordingTextReader(new WindowsPhoneTextReader(), output) : null;
    if (ocr?.Unavailable is { } unavailable)
    {
        Console.Error.WriteLine($"Windows OCR is not available: {unavailable}.");
        return 1;
    }

    var audit = AndroidModelChecks.Unasked(
        AndroidAudit.Run(archive, PhoneCheckSettings.Default, ocr, Load, CancellationToken.None),
        AndroidFixtureFiles.NoModel);
    var findings = AndroidFixtureFiles.FindingsOf(archive, audit);
    if (ocr is not null)
    {
        File.WriteAllBytes(Path.Combine(output, AndroidFixtureFiles.Ocr), JsonSerializer.SerializeToUtf8Bytes(ocr.ToFile(), AndroidFixtureFiles.Json));
    }

    File.WriteAllBytes(Path.Combine(output, AndroidFixtureFiles.Findings), JsonSerializer.SerializeToUtf8Bytes(findings, AndroidFixtureFiles.Json));
    WriteManifest(session, output, kept.Count, copied);

    Console.WriteLine($"FIXTURE EXTRACTED to {output}");
    Console.WriteLine($"  {kept.Count} events kept, from {start / 1e9:0.0} to {end / 1e9:0.0} seconds; left out: " +
        string.Join(", ", left.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"{entry.Value} {entry.Key}")));
    Console.WriteLine($"  {copied.Count} pictures kept; OCR {(ocr is null ? "not used" : $"read {ocr.Pictures.Count}")}");
    Console.WriteLine($"  {new DirectoryInfo(output).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length) / 1024.0:0} KB in all");
    if (audit.MeasureProblem is { Length: > 0 } problem)
    {
        Console.WriteLine($"  Measuring problem: {problem}");
    }

    Console.WriteLine();
    foreach (var visit in archive.PhoneScreens.Visits)
    {
        Console.WriteLine($"VISIT {visit.Number}: {visit.Screen.Title} ({visit.Package}), {visit.Start / 1e9:0.0} to {visit.End / 1e9:0.0} s");
        foreach (var finding in findings.Where(finding => !finding.App && finding.Visit == visit.Number))
        {
            Console.WriteLine($"  {finding.Check} {finding.Level} {finding.Identity.Replace('\u001f', ' ')} | {finding.Label} | {finding.Measured}");
        }
    }

    foreach (var finding in findings.Where(finding => finding.App))
    {
        Console.WriteLine($"APP {finding.Check} {finding.Level} {finding.Identity} | {finding.Label}");
    }

    // 3. The text kept that is not the fixture app's own.
    var own = Own(Path.Combine(repository, "android", "fixtures"));
    var foreign = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var packages = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var stored in kept.Where(stored => stored.Event.Channel != "input.android.raw"))
    {
        Texts(stored.Event.Payload, stored.Event.Channel, own, foreign, packages);
    }

    Console.WriteLine();
    Console.WriteLine("READ BEFORE COMMITTING: the recording's metadata");
    foreach (var (name, value) in recording.OrderBy(entry => entry.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {name} = {value}");
    }

    Console.WriteLine("READ BEFORE COMMITTING: the packages named in the kept events");
    foreach (var name in packages)
    {
        Console.WriteLine($"  {name}");
    }

    Console.WriteLine("READ BEFORE COMMITTING: the text in the kept events that is not in the fixture app's source");
    foreach (var (text, count) in foreign)
    {
        Console.WriteLine($"  {count,4} x {text}");
    }
}
finally
{
    (opened.Archive.Timeline as IDisposable)?.Dispose();
}

return 0;

// The session's manifest with only the kept files listed, the trimmed
// recording file's size and hash in place of the original's, and the kept
// events' count.
static void WriteManifest(string session, string output, int events, IReadOnlyCollection<string> pictures)
{
    var manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(session, "manifest.json")))!.AsObject();
    var keep = new HashSet<string>(pictures, StringComparer.Ordinal);
    var artifacts = new JsonArray();
    foreach (var artifact in manifest["artifacts"]!.AsArray())
    {
        var path = artifact!["path"]!.GetValue<string>();
        if (path == AndroidFixtureFiles.Recording)
        {
            var file = Path.Combine(output, AndroidFixtureFiles.Recording);
            artifacts.Add(new JsonObject
            {
                ["path"] = path,
                ["sizeBytes"] = new FileInfo(file).Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))),
            });
        }
        else if (keep.Contains(path))
        {
            artifacts.Add(artifact.DeepClone());
        }
    }

    manifest["artifacts"] = artifacts;
    manifest["acceptedEventCount"] = events;
    File.WriteAllText(Path.Combine(output, "manifest.json"), manifest.ToJsonString(AndroidFixtureFiles.Json));
}

// Why an event is left out of the fixture, or null to keep it.
static string? Leave(Recorder.Contracts.RecorderEvent record, long start, long end)
{
    var channel = record.Channel;
    if (channel.StartsWith("audio.", StringComparison.Ordinal))
    {
        return "sound";
    }

    if (channel == "graphics.android.video")
    {
        return "video";
    }

    if (channel == "accessibility.android.events" &&
        record.Payload.ValueKind == JsonValueKind.Object &&
        record.Payload.TryGetProperty("eventType", out var type) && type.ValueKind == JsonValueKind.Number &&
        type.GetInt32() == 64)
    {
        // TYPE_NOTIFICATION_STATE_CHANGED: another app's notification, with its text.
        return "notifications";
    }

    var timed = channel.StartsWith("accessibility.android.", StringComparison.Ordinal) ||
        channel.StartsWith("graphics.android.", StringComparison.Ordinal) ||
        channel.StartsWith("input.android.", StringComparison.Ordinal) ||
        channel.StartsWith("screen-reader.", StringComparison.Ordinal);
    return timed && (record.MonotonicNanoseconds < start || record.MonotonicNanoseconds > end)
        ? "outside the fixture app's visits"
        : null;
}

// The words of the fixture app's source: its Java, XML and HTML.
static string Own(string folder) =>
    string.Join("\n", Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .Where(file => file.EndsWith(".java", StringComparison.Ordinal) || file.EndsWith(".xml", StringComparison.Ordinal) ||
            file.EndsWith(".html", StringComparison.Ordinal) || file.EndsWith(".json", StringComparison.Ordinal))
        .Select(File.ReadAllText));

// The text values in an event's payload that are not in the app's source,
// leaving out the names of the recorder's own values, such as SUBTREE or
// window-change. The touch screen's raw lines are not read: they hold no text.
static void Texts(JsonElement element, string channel, string own, SortedDictionary<string, int> foreign, SortedSet<string> packages)
{
    switch (element.ValueKind)
    {
        case JsonValueKind.Object:
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString()!;
                    if (property.Name is "packageName" or "package")
                    {
                        packages.Add(value);
                    }
                    else if (property.Name is "text" or "contentDescription" or "paneTitle" or "hintText" or "stateDescription" or
                             "tooltipText" or "title" or "beforeText" or "line" or "message" or "error" or "containerTitle" &&
                             value.Trim() is { Length: > 0 } trimmed && !own.Contains(trimmed, StringComparison.Ordinal) && !IsValueName(trimmed))
                    {
                        var key = $"{channel}: {Clip(trimmed)}";
                        foreign[key] = foreign.GetValueOrDefault(key) + 1;
                    }
                }
                else
                {
                    Texts(property.Value, channel, own, foreign, packages);
                }
            }

            break;
        case JsonValueKind.Array:
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString()!.Trim() is { Length: > 0 } text && !own.Contains(text, StringComparison.Ordinal) && !IsValueName(text))
                {
                    var key = $"{channel}: {Clip(text)}";
                    foreign[key] = foreign.GetValueOrDefault(key) + 1;
                }
                else
                {
                    Texts(item, channel, own, foreign, packages);
                }
            }

            break;
    }
}

static bool IsValueName(string text) => Regex.IsMatch(text, "^([A-Z][A-Z0-9_]*|[a-z]+(-[a-z]+)+)$");

static string Clip(string text)
{
    var single = Regex.Replace(text, @"\s+", " ");
    return single.Length > 120 ? single[..120] + "..." : single;
}

// The repository's folder, found from the program's.
static string Repository()
{
    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "windows-a11y-recorder.slnx")))
        {
            return folder.FullName;
        }
    }

    throw new DirectoryNotFoundException("The repository's folder was not found above the program's.");
}
