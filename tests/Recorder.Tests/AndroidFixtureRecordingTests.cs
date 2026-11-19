using System.Text.Json;
using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Database.RecordingFiles;
using Recorder.FixtureExtract;
using Recorder.Session;

namespace Recorder.Tests;

// docs/architecture/android-fixtures.md, "Rule tests": the rules run on each
// extracted recording of the fixture app under tests/fixtures/android/, with
// OCR's readings from the PC and no model, and their findings are compared
// with the findings made on the PC and with the app's expected answers.
public sealed class AndroidFixtureRecordingTests
{
    private const string NoFixture =
        "No recording of the fixture app has been extracted to tests/fixtures/android/ yet (docs/architecture/android-fixtures.md).";

    [Fact]
    public async Task TheRulesFindWhatTheyFoundOnThePc()
    {
        var folders = Folders();
        Assert.SkipWhen(folders.Count == 0, NoFixture);
        foreach (var folder in folders)
        {
            var found = await FindingsAsync(folder);
            var kept = JsonSerializer.Deserialize<List<FixtureFinding>>(
                File.ReadAllBytes(Path.Combine(folder, AndroidFixtureFiles.Findings)), AndroidFixtureFiles.Json)!;
            Assert.Equal(kept.Select(Line), found.Select(Line));
        }
    }

    [Fact]
    public async Task TheRulesGiveTheFixtureAppsExpectedAnswers()
    {
        var folders = Folders();
        Assert.SkipWhen(folders.Count == 0, NoFixture);
        using var expected = JsonDocument.Parse(File.ReadAllText(RepositoryPath("android", "fixtures", "expected.json")));
        var package = expected.RootElement.GetProperty("package").GetString()!;
        var screens = expected.RootElement.GetProperty("screens").EnumerateArray().ToList();
        var problems = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            var found = await FindingsAsync(folder);
            foreach (var screen in screens)
            {
                var title = screen.GetProperty("title").GetString()!;
                var onScreen = found.Where(f => !f.App && f.Package == package && f.Screen == title).ToList();
                if (!VisitedIn(folder, package, title))
                {
                    continue;
                }

                visited.Add(screen.GetProperty("id").GetString()!);
                foreach (var expect in screen.GetProperty("expect").EnumerateArray())
                {
                    if (expect.GetProperty("by").GetString() != "rule")
                    {
                        continue;
                    }

                    var check = expect.GetProperty("check").GetString()!;
                    var (what, matches) = expect.TryGetProperty("item", out var item)
                        ? ($"item {item.GetString()}", onScreen.Where(f => f.Check == check && OfItem(f, item.GetString()!)).ToList())
                        : ($"line \"{expect.GetProperty("line").GetString()}\"",
                            onScreen.Where(f => f.Check == check && OfLine(f, expect.GetProperty("line").GetString()!)).ToList());
                    var finding = expect.GetProperty("finding").GetString()!;
                    var where = $"{name}, {title}: {check} on {what}";
                    if (finding == "none")
                    {
                        problems.AddRange(matches.Select(f => $"{where}: a false alarm on the near miss, {f.Level}: {f.Measured}"));
                    }
                    else if (matches.Count == 0)
                    {
                        problems.Add($"{where}: missed; {finding} expected");
                    }
                    else
                    {
                        problems.AddRange(matches
                            .Where(f => !string.Equals(f.Level, finding, StringComparison.OrdinalIgnoreCase))
                            .Select(f => $"{where}: {f.Level} found, {finding} expected"));
                    }
                }
            }
        }

        // Every tour screen whose checks are built is in some recording.
        problems.AddRange(screens
            .Where(s => s.GetProperty("how").GetString() == "tour" && s.GetProperty("stage").GetString() != "unbuilt")
            .Select(s => s.GetProperty("id").GetString()!)
            .Where(id => !visited.Contains(id))
            .Select(id => $"{id}: in the tour, but in no recording"));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // A finding of an element by its resource ID, which a view's identity
    // holds as "id:package:id/name#n" and a web page's as "id:name#n".
    private static bool OfItem(FixtureFinding finding, string item) =>
        finding.Identity.Contains($":id/{item}#", StringComparison.Ordinal) ||
        finding.Identity.StartsWith($"id:{item}#", StringComparison.Ordinal);

    // A finding of an OCR line, by the line's text.
    private static bool OfLine(FixtureFinding finding, string line) =>
        finding.Label.Contains(line, StringComparison.Ordinal) ||
        finding.Identity.Contains(line, StringComparison.Ordinal);

    private static string Line(FixtureFinding finding) =>
        $"{(finding.App ? "app" : $"visit {finding.Visit}")} {finding.Screen} {finding.Check} {finding.Level} {finding.Identity} | {finding.Label} | {finding.Measured}";

    private static bool VisitedIn(string folder, string package, string title) =>
        Visits.GetOrAdd(folder, _ => VisitTitles(folder)).Contains((package, title));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<(string, string)>> Visits = new(StringComparer.Ordinal);

    private static HashSet<(string, string)> VisitTitles(string folder)
    {
        var opened = RecordingFilePlayback.OpenAsync(folder, Path.Combine(folder, AndroidFixtureFiles.Recording)).GetAwaiter().GetResult();
        try
        {
            return [.. opened.Archive.PhoneScreens.Visits.Where(v => v.Screen.Title is not null).Select(v => (v.Package, v.Screen.Title!))];
        }
        finally
        {
            (opened.Archive.Timeline as IDisposable)?.Dispose();
        }
    }

    // The rules' findings on a fixture: OCR's readings from the PC, when it
    // was used there, the fixture's pictures and no model.
    private static async Task<IReadOnlyList<FixtureFinding>> FindingsAsync(string folder)
    {
        var opened = await RecordingFilePlayback.OpenAsync(
            folder, Path.Combine(folder, AndroidFixtureFiles.Recording), TestContext.Current.CancellationToken);
        try
        {
            var reader = File.Exists(Path.Combine(folder, AndroidFixtureFiles.Ocr)) ? RecordedTextReader.Load(folder) : null;
            var audit = AndroidModelChecks.Unasked(
                AndroidAudit.Run(opened.Archive, PhoneCheckSettings.Default, reader, PhonePictures.Load, TestContext.Current.CancellationToken),
                AndroidFixtureFiles.NoModel);
            return AndroidFixtureFiles.FindingsOf(opened.Archive, audit);
        }
        finally
        {
            (opened.Archive.Timeline as IDisposable)?.Dispose();
        }
    }

    private static List<string> Folders()
    {
        var root = RepositoryPath("tests", "fixtures", "android");
        return Directory.Exists(root)
            ? [.. Directory.EnumerateDirectories(root)
                .Where(folder => File.Exists(Path.Combine(folder, AndroidFixtureFiles.Recording)))
                .Order(StringComparer.Ordinal)]
            : [];
    }

    private static string RepositoryPath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "windows-a11y-recorder.slnx")))
            {
                return Path.Combine([directory.FullName, .. parts]);
            }
        }

        throw new InvalidOperationException("The repository was not found above " + AppContext.BaseDirectory);
    }
}
