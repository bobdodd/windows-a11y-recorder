using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Recorder.Session;

namespace Recorder.Tests;

// docs/architecture/android-fixtures.md: the fixture app's expected answers,
// android/fixtures/expected.json, kept in step with the app's source and the
// recorder's check names, so a renamed check, screen or element is caught
// before a recording is made with it.
public sealed partial class AndroidFixtureAppTests
{
    private const string Package = "io.github.bobdodd.a11yrecorder.fixtures";

    private static readonly string[] ScreenFields = ["id", "title", "framework", "how", "seconds", "stage", "steps", "expect"];
    private static readonly string[] ExpectFields = ["check", "item", "line", "by", "finding", "note"];
    private static readonly string[] Frameworks = ["views", "webview"];
    private static readonly string[] Hows = ["tour", "person"];
    private static readonly string[] Stages = ["proof", "unbuilt", "built"];
    private static readonly string[] Bys = ["rule", "model", "unbuilt"];
    private static readonly string[] Findings = ["error", "warning", "info", "not-run", "none"];

    [Fact]
    public void TheExpectedAnswersHaveOnlyTheFieldsThePlanNames()
    {
        using var expected = Expected();
        var root = expected.RootElement;
        Assert.Equal(["version", "package", "auditLevel", "screens"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(Package, root.GetProperty("package").GetString());
        Assert.Contains(root.GetProperty("auditLevel").GetString(), new[] { "AA", "AAA" });

        foreach (var screen in root.GetProperty("screens").EnumerateArray())
        {
            var id = screen.GetProperty("id").GetString();
            Assert.All(screen.EnumerateObject(), p => Assert.True(ScreenFields.Contains(p.Name), $"{id}: unknown field {p.Name}"));
            Assert.Contains(screen.GetProperty("framework").GetString(), Frameworks);
            Assert.Contains(screen.GetProperty("stage").GetString(), Stages);
            var how = screen.GetProperty("how").GetString();
            Assert.Contains(how, Hows);
            if (how == "tour")
            {
                Assert.InRange(screen.GetProperty("seconds").GetInt32(), 1, 60);
                Assert.False(screen.TryGetProperty("steps", out _), $"{id}: a tour screen has no steps");
            }
            else
            {
                // The owner tests in at most five steps.
                Assert.InRange(screen.GetProperty("steps").GetArrayLength(), 1, 5);
            }

            foreach (var expect in screen.GetProperty("expect").EnumerateArray())
            {
                Assert.All(expect.EnumerateObject(), p => Assert.True(ExpectFields.Contains(p.Name), $"{id}: unknown field {p.Name}"));
                Assert.Contains(expect.GetProperty("by").GetString(), Bys);
                Assert.Contains(expect.GetProperty("finding").GetString(), Findings);
                Assert.True(expect.TryGetProperty("item", out _) ^ expect.TryGetProperty("line", out _),
                    $"{id}: an expectation names an item or an OCR line, not both");
            }
        }
    }

    [Fact]
    public void EveryCheckNamedIsOneTheRecorderReports()
    {
        var names = typeof(PhoneCheckNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
        using var expected = Expected();
        foreach (var (screen, expect) in Expectations(expected))
        {
            var check = expect.GetProperty("check").GetString()!;
            if (expect.GetProperty("by").GetString() == "unbuilt")
            {
                Assert.DoesNotContain(check, names);
            }
            else
            {
                Assert.True(names.Contains(check), $"{screen}: {check} is not a check the recorder reports");
            }
        }
    }

    [Fact]
    public void TheScreensAreTheAppsScreensWithTheirTitles()
    {
        var source = File.ReadAllText(FixturePath("src", "io", "github", "bobdodd", "a11yrecorder", "fixtures", "Screens.java"));
        var inApp = ScreenPattern().Matches(source).Select(m => (m.Groups["id"].Value, m.Groups["title"].Value)).ToList();
        using var expected = Expected();
        var listed = expected.RootElement.GetProperty("screens").EnumerateArray()
            .Select(s => (s.GetProperty("id").GetString()!, s.GetProperty("title").GetString()!))
            .ToList();
        Assert.NotEmpty(inApp);
        Assert.Equal(inApp, listed);
    }

    [Fact]
    public void EveryElementNamedHasItsStableId()
    {
        var ids = IdPattern().Matches(File.ReadAllText(FixturePath("res", "values", "ids.xml")))
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var webIds = Directory.EnumerateFiles(FixturePath("assets"), "*.html", SearchOption.AllDirectories)
            .SelectMany(file => HtmlIdPattern().Matches(File.ReadAllText(file)).Select(m => m.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);
        var source = string.Join("\n", Directory.EnumerateFiles(FixturePath("src"), "*.java", SearchOption.AllDirectories).Select(File.ReadAllText));
        using var expected = Expected();
        foreach (var screen in expected.RootElement.GetProperty("screens").EnumerateArray())
        {
            var web = screen.GetProperty("framework").GetString() == "webview";
            foreach (var expect in screen.GetProperty("expect").EnumerateArray())
            {
                if (!expect.TryGetProperty("item", out var item) || item.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                var name = item.GetString()!;
                if (web)
                {
                    Assert.True(webIds.Contains(name), $"{name} is not an id in the app's pages");
                }
                else
                {
                    Assert.True(ids.Contains(name), $"{name} is not in res/values/ids.xml");
                    Assert.Contains("R.id." + name, source);
                }
            }
        }
    }

    [Fact]
    public void NoCheckIsExpectedTwiceOfOneElement()
    {
        using var expected = Expected();
        var keys = Expectations(expected)
            .Select(e => (e.Screen, e.Expect.GetProperty("check").GetString(),
                e.Expect.TryGetProperty("item", out var item) ? "item:" + item : "line:" + e.Expect.GetProperty("line").GetString()))
            .ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void EachProofCheckHasAFaultAndANearMiss()
    {
        using var expected = Expected();
        var byCheck = Expectations(expected)
            .GroupBy(e => e.Expect.GetProperty("check").GetString()!)
            .ToList();
        Assert.NotEmpty(byCheck);
        foreach (var check in byCheck)
        {
            var findings = check.Select(e => e.Expect.GetProperty("finding").GetString()).ToList();
            Assert.True(findings.Contains("none"), $"{check.Key} has no near miss");
            Assert.True(findings.Any(f => f != "none"), $"{check.Key} has no fault");
        }
    }

    private static IEnumerable<(string Screen, JsonElement Expect)> Expectations(JsonDocument expected) =>
        expected.RootElement.GetProperty("screens").EnumerateArray()
            .SelectMany(s => s.GetProperty("expect").EnumerateArray().Select(e => (s.GetProperty("id").GetString()!, e)));

    private static JsonDocument Expected() => JsonDocument.Parse(File.ReadAllText(FixturePath("expected.json")));

    private static string FixturePath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "windows-a11y-recorder.slnx")))
            {
                return Path.Combine([directory.FullName, "android", "fixtures", .. parts]);
            }
        }

        throw new InvalidOperationException("The repository was not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex("""new Screen\("(?<id>[^"]+)", "(?<title>[^"]+)", """)]
    private static partial Regex ScreenPattern();

    [GeneratedRegex("""<item type="id" name="(?<name>[^"]+)" />""")]
    private static partial Regex IdPattern();

    [GeneratedRegex("""\sid="(?<name>[^"]+)""")]
    private static partial Regex HtmlIdPattern();
}
