using System.Net;
using System.Text.Json;
using Recorder.Analysis;

namespace Recorder.Tests;

/// <summary>
/// The analysis runner's rules: which endpoints are allowed, the settings
/// file, the local server's preset, the output check, and the request body.
/// See docs/architecture/analysis-runner.md.
/// </summary>
public sealed class AnalysisRulesTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.1.20", true)]
    [InlineData("169.254.10.10", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:192.168.1.20", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void Only_this_PC_and_the_local_network_are_local(string address, bool local) =>
        Assert.Equal(local, EndpointPolicy.IsLocal(IPAddress.Parse(address)));

    [Fact]
    public void A_statement_in_the_answer_form_has_its_text_before_its_kind_and_evidence()
    {
        var statement = ClaimCheck.Schema(["description"])["properties"]!["description"]!["items"]!["items"]!;

        Assert.Equal(["text", "kind", "evidence"], statement["properties"]!.AsObject().Select(property => property.Key));
        Assert.Equal(["text", "kind", "evidence"], statement["required"]!.AsArray().Select(name => (string)name!));
    }

    [Fact]
    public void An_outside_address_is_refused_and_a_host_name_waits_to_be_resolved()
    {
        Assert.NotNull(EndpointPolicy.Refusal(new Uri("http://8.8.8.8:8080/")));
        Assert.Null(EndpointPolicy.Refusal(new Uri("http://192.168.1.50:8080/")));
        Assert.Null(EndpointPolicy.Refusal(new Uri("http://localhost:8080/")));
        Assert.Null(EndpointPolicy.Refusal(new Uri("http://ai-server.lan:8080/")));
    }

    [Fact]
    public async Task Localhost_passes_once_resolved() =>
        Assert.Null(await EndpointPolicy.RefusalAfterResolvingAsync(
            new Uri("http://localhost:8080/"),
            TestContext.Current.CancellationToken));

    private const string Sha = "ac0e2c1189e055faa36eff361580e79c5bd6f8e76bffb4ce547f167d53e31a61";

    private static string Settings(string profiles, string extra = "") => $$"""
        {
          "version": 1,
          {{extra}}
          "localServer": { "executable": "C:\\llama\\llama-server.exe", "port": 8091 },
          "profiles": [ {{profiles}} ],
          "defaults": { "text": "This PC" }
        }
        """;

    private const string LocalProfile = """
        {
          "name": "This PC",
          "endpoint": "http://127.0.0.1:8091",
          "model": "qwen3.6-35b-a3b",
          "capabilities": ["text", "image"],
          "file": { "name": "Qwen3.6-35B-A3B-UD-Q4_K_M.gguf", "quantization": "UD-Q4_K_M", "sha256": "AC0E2C1189E055FAA36EFF361580E79C5BD6F8E76BFFB4CE547F167D53E31A61" },
          "local": { "modelPath": "C:\\models\\q.gguf", "imageAddOnPath": "C:\\models\\mmproj.gguf", "arguments": "-ngl 99 -ncmoe 36 -fa on" },
          "generation": { "maxTokens": 4096, "temperature": 0, "seed": 7 }
        }
        """;

    [Fact]
    public void A_settings_file_is_read_with_its_profiles_defaults_and_limit()
    {
        var (settings, problems) = AnalysisSettingsFile.Parse(Settings(LocalProfile, "\"maxAttempts\": 4,"));

        Assert.Empty(problems);
        Assert.Equal(4, settings.MaxAttempts);
        Assert.Equal(new LocalServerSettings("C:\\llama\\llama-server.exe", 8091), settings.LocalServer);
        var profile = Assert.Single(settings.Profiles);
        Assert.Equal("This PC", profile.Name);
        Assert.True(profile.Supports(ModelCapabilities.Image));
        Assert.Equal(Sha, profile.File.Sha256);
        Assert.Equal("-ngl 99 -ncmoe 36 -fa on", profile.Local!.Arguments);
        Assert.Equal(new GenerationSettings(4096, 0, 7, false), profile.Generation);
        Assert.Equal("This PC", settings.Defaults[ModelCapabilities.Text]);
    }

    [Fact]
    public void A_missing_settings_file_gives_no_profiles_and_no_problems()
    {
        var (settings, problems) = AnalysisSettingsFile.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "none.json"));

        Assert.Empty(problems);
        Assert.Empty(settings.Profiles);
        Assert.Equal(AnalysisSettings.DefaultMaxAttempts, settings.MaxAttempts);
    }

    [Theory]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("{ \"version\": 2 }", "version 2")]
    public void A_damaged_settings_file_is_a_problem(string text, string expected)
    {
        var (settings, problems) = AnalysisSettingsFile.Parse(text);

        Assert.Empty(settings.Profiles);
        Assert.Contains(expected, Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"endpoint\": \"http://8.8.8.8:8091\"", "outside this PC and the local network")]
    [InlineData("\"endpoint\": \"ftp://127.0.0.1:8091\"", "http or https")]
    [InlineData("\"endpoint\": \"http://127.0.0.1:9000\"", "must be the local server's")]
    [InlineData("\"capabilities\": [\"text\", \"smell\"]", "not text or image")]
    [InlineData("\"capabilities\": []", "no capabilities")]
    [InlineData("\"local\": { \"modelPath\": \"C:\\\\q.gguf\", \"arguments\": \"-ngl 99\" }", "needs an image add-on")]
    [InlineData("\"local\": { \"modelPath\": \"C:\\\\q.gguf\", \"imageAddOnPath\": \"C:\\\\m.gguf\", \"arguments\": \"--port 9000\" }", "set by the worker")]
    [InlineData("\"local\": { \"modelPath\": \"C:\\\\q.gguf\", \"imageAddOnPath\": \"C:\\\\m.gguf\", \"arguments\": \"99\" }", "does not follow an option")]
    [InlineData("\"file\": { \"sha256\": \"abc\" }", "64 hexadecimal digits")]
    [InlineData("\"generation\": { \"maxTokens\": 0 }", "maxTokens of at least 1")]
    public void A_profile_that_breaks_a_rule_is_refused(string replacement, string expected)
    {
        // Replaces the profile's line for the same key, keeping its comma.
        var key = replacement[..(replacement.IndexOf(':', StringComparison.Ordinal))];
        var lines = LocalProfile.Split('\n').Select(line =>
            line.TrimStart().StartsWith(key, StringComparison.Ordinal)
                ? replacement + (line.TrimEnd().EndsWith(',') ? "," : "")
                : line);
        var profile = string.Join('\n', lines);

        var (settings, problems) = AnalysisSettingsFile.Parse(Settings(profile));

        Assert.Empty(settings.Profiles);
        Assert.Contains(problems, problem => problem.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_locally_served_profile_needs_the_local_server()
    {
        var text = Settings(LocalProfile).Replace(
            "\"localServer\": { \"executable\": \"C:\\\\llama\\\\llama-server.exe\", \"port\": 8091 },",
            "",
            StringComparison.Ordinal);

        var (settings, problems) = AnalysisSettingsFile.Parse(text);

        Assert.Empty(settings.Profiles);
        Assert.Contains(problems, problem => problem.Contains("no localServer is set", StringComparison.Ordinal));
    }

    [Fact]
    public void A_network_profile_needs_no_key_and_a_default_must_name_a_capable_profile()
    {
        var network = """
            { "name": "Server", "endpoint": "http://192.168.1.50:8080", "model": "big", "capabilities": ["text"] }
            """;
        var text = Settings(network).Replace("\"defaults\": { \"text\": \"This PC\" }", "\"defaults\": { \"text\": \"Server\", \"image\": \"Server\" }", StringComparison.Ordinal);

        var (settings, problems) = AnalysisSettingsFile.Parse(text);

        var profile = Assert.Single(settings.Profiles);
        Assert.Null(profile.Local);
        Assert.Null(profile.ProtectedApiKey);
        Assert.Equal("Server", settings.Defaults[ModelCapabilities.Text]);
        Assert.False(settings.Defaults.ContainsKey(ModelCapabilities.Image));
        Assert.Contains(problems, problem => problem.Contains("cannot take image", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("2.5")]
    public void The_attempt_limit_is_a_whole_number_from_1_to_10(string value)
    {
        var (settings, problems) = AnalysisSettingsFile.Parse(Settings(LocalProfile, $"\"maxAttempts\": {value},"));

        Assert.Equal(AnalysisSettings.DefaultMaxAttempts, settings.MaxAttempts);
        Assert.Contains(problems, problem => problem.Contains("maxAttempts", StringComparison.Ordinal));
    }

    [Fact]
    public void Server_arguments_become_preset_entries()
    {
        var (entries, problem) = LocalModelServer.PresetEntries("-ngl 99 -ncmoe 36 -fa on --no-mmap -c 16384 --override-kv \"a b\" --temp -1");

        Assert.Null(problem);
        Assert.Equal(
            [
                new("ngl", "99"), new("ncmoe", "36"), new("fa", "on"), new("no-mmap", "true"),
                new("c", "16384"), new("override-kv", "a b"), new("temp", "-1")
            ],
            entries);
    }

    [Fact]
    public void The_preset_has_a_section_for_each_locally_served_profile()
    {
        var (settings, _) = AnalysisSettingsFile.Parse(Settings(LocalProfile));

        var preset = LocalModelServer.Preset(settings.Profiles);

        Assert.Equal(
            "version = 1\n\n[qwen3.6-35b-a3b]\nmodel = C:\\models\\q.gguf\nmmproj = C:\\models\\mmproj.gguf\n" +
            "ngl = 99\nncmoe = 36\nfa = on\n",
            preset);
        Assert.Equal(
            ["--models-preset", "p.ini", "--models-max", "1", "--no-models-autoload", "--parallel", "1", "--host", "127.0.0.1", "--port", "8091"],
            LocalModelServer.Arguments("p.ini", 8091));
    }

    private static readonly HashSet<string> References = ["E1", "E2", "E3"];

    [Fact]
    public void Output_with_cited_claims_and_unknowns_passes()
    {
        var output = """
            { "description": [
                [ { "kind": "claim", "text": "The page is CNIB's home page.", "evidence": ["E1", "E2"] },
                  { "kind": "unknown", "text": "Who the page is for is not recorded.", "evidence": [] } ],
                [ { "kind": "claim", "text": "The participant pressed H.", "evidence": ["E3"] } ] ],
              "purpose": [] }
            """;

        var (claims, _, failure) = ClaimCheck.Check(output, ["description", "purpose"], References);

        Assert.Null(failure);
        Assert.Equal(3, claims.Count);
        Assert.Equal(
            ("description", 1, 0, "claim", "The participant pressed H."),
            (claims[2].Section, claims[2].Paragraph, claims[2].Ordinal, claims[2].Kind, claims[2].Text));
        Assert.Equal(["E3"], claims[2].Evidence);
        Assert.Equal("unknown", claims[1].Kind);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("{ \"other\": [] }", "no \"description\" section")]
    [InlineData("{ \"description\": [ { } ] }", "not a list of statements")]
    [InlineData("{ \"description\": [ [ { \"kind\": \"claim\", \"text\": \"x\" } ] ] }", "does not have a kind, a text, and a list of evidence")]
    [InlineData("{ \"description\": [ [ { \"kind\": \"guess\", \"text\": \"x\", \"evidence\": [\"E1\"] } ] ] }", "neither claim nor unknown")]
    [InlineData("{ \"description\": [ [ { \"kind\": \"claim\", \"text\": \" \", \"evidence\": [\"E1\"] } ] ] }", "has no text")]
    [InlineData("{ \"description\": [ [ { \"kind\": \"claim\", \"text\": \"x\", \"evidence\": [] } ] ] }", "cites no evidence")]
    [InlineData("{ \"description\": [ [ { \"kind\": \"claim\", \"text\": \"x\", \"evidence\": [\"E9\"] } ] ] }", "E9, which was not in the input")]
    [InlineData("{ \"description\": [ [ { \"kind\": \"unknown\", \"text\": \"x\", \"evidence\": [\"E9\"] } ] ] }", "E9, which was not in the input")]
    [InlineData("{ \"description\": [ [ { \"kind\": \"claim\", \"text\": \"x\", \"evidence\": [1] } ] ] }", "not a reference")]
    public void Output_that_fails_the_check_is_not_repaired(string output, string expected)
    {
        var (claims, _, failure) = ClaimCheck.Check(output, ["description"], References);

        Assert.Empty(claims);
        Assert.Contains(expected, failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Sampling_options_are_read_sent_and_recorded_and_the_worker_s_own_fields_refused()
    {
        var sampled = LocalProfile.Replace(
            "\"generation\": { \"maxTokens\": 4096, \"temperature\": 0, \"seed\": 7 }",
            "\"generation\": { \"maxTokens\": 4096, \"temperature\": 0, \"seed\": 7, \"sampling\": { \"presence_penalty\": 1.5, \"repeat_last_n\": 64 } }",
            StringComparison.Ordinal);
        var (settings, problems) = AnalysisSettingsFile.Parse(Settings(sampled));

        Assert.Empty(problems);
        var profile = Assert.Single(settings.Profiles);
        Assert.Equal(1.5, profile.Generation.Sampling["presence_penalty"]);
        var request = new ModelRequest("t", 1, "Describe.", "E1: a fact", [], ["description"], References);
        using (var document = JsonDocument.Parse(ChatRequest.Build(profile, request)))
        {
            Assert.Equal(1.5, document.RootElement.GetProperty("presence_penalty").GetDouble());
            Assert.Equal(64, document.RootElement.GetProperty("repeat_last_n").GetInt32());
        }

        Assert.Equal("{\"presence_penalty\":1.5,\"repeat_last_n\":64}", ChatRequest.Parameters(profile, request)["sampling"]!.ToJsonString());
        Assert.Equal("{\"presence_penalty\":1.5,\"repeat_last_n\":64}", AnalysisSettingsFile.Describe(profile)["generation"]!["sampling"]!.ToJsonString());

        var reserved = LocalProfile.Replace("\"seed\": 7 }", "\"seed\": 7, \"sampling\": { \"temperature\": 1 } }", StringComparison.Ordinal);
        Assert.Contains(
            "Profile \"This PC\" gives the sampling option \"temperature\", which the worker sets itself.",
            AnalysisSettingsFile.Parse(Settings(reserved)).Problems);
        var text = LocalProfile.Replace("\"seed\": 7 }", "\"seed\": 7, \"sampling\": { \"top_k\": \"20\" } }", StringComparison.Ordinal);
        Assert.Contains(
            "Profile \"This PC\" gives the sampling option \"top_k\" a value that is not a number.",
            AnalysisSettingsFile.Parse(Settings(text)).Problems);
    }

    private static string Statements(params string[] texts) =>
        "{ \"page\": [ [ " + string.Join(", ", texts.Select(text => $"{{ \"text\": {JsonSerializer.Serialize(text)}, \"kind\": \"claim\", \"evidence\": [\"E1\"] }}")) + " ] ] }";

    // Since 2026-10-14 an answer is stopped only at a loop: a block of one
    // or more statements said word for word three times in a row. Any other
    // repeat is let through.
    [Fact]
    public void The_repeat_watch_lets_repeats_through_that_are_not_three_copies_in_a_row()
    {
        // Moment 8 on the target machine: a conclusion restated in a later
        // section.
        var restated = new RepeatWatch();
        Assert.Null(restated.Check(Statements("The page is open.", "The page is \"open\".", "", "A card.", "The page is open.")));
        Assert.Equal(4, restated.Statements);

        // Moment 6: two statements in a row, each repeating an earlier one.
        var twoInARow = new RepeatWatch();
        Assert.Null(twoInARow.Check(Statements("The page is open.", "The page is \"open\".", "A card.", "The page is open.", "The page is \"open\".")));

        // Cards that each end with the same button.
        var cards = new RepeatWatch();
        Assert.Null(cards.Check(Statements("A button labeled Read more.", "A card.", "A button labeled Read more.", "Another card.", "A button labeled Read more.")));

        // A block said twice in a row is not yet three copies.
        var twice = new RepeatWatch();
        Assert.Null(twice.Check(Statements("A.", "B.", "C.", "A.", "B.", "C.")));
    }

    [Fact]
    public void The_repeat_watch_stops_a_statement_said_three_times_in_a_row()
    {
        var watch = new RepeatWatch();
        Assert.Equal(
            "The model said the same statement word for word three times in a row, as statements 2 to 4, so the answer was stopped there: \"The page is \"open\".\"",
            watch.Check(Statements("A card.", "The page is \"open\".", "The page is \"open\".", "The page is \"open\".", "Never read.")));
        Assert.Equal(4, watch.Statements);
    }

    [Fact]
    public void The_repeat_watch_stops_a_block_of_statements_said_three_times_in_a_row()
    {
        // Moment 10 on the target machine cycled through three statements.
        var watch = new RepeatWatch();
        Assert.Null(watch.Check(Statements("Intro.", "A.", "B.", "C.", "A.", "B.", "C.", "A.", "B.")));
        Assert.Equal(
            "The model said the same 3 statements word for word three times in a row, as statements 2 to 10, so the answer was stopped there. The repeated statements begin: \"A.\"",
            watch.Check(Statements("Intro.", "A.", "B.", "C.", "A.", "B.", "C.", "A.", "B.", "C.")));
        Assert.Equal(10, watch.Statements);
    }

    [Fact]
    public void The_loop_rule_finds_the_smallest_block_at_the_end_and_nothing_elsewhere()
    {
        Assert.Equal(1, RepeatWatch.Loop(["A.", "A.", "A."]));
        Assert.Equal(2, RepeatWatch.Loop(["A.", "B.", "A.", "B.", "A.", "B."]));
        Assert.Null(RepeatWatch.Loop(["A.", "A.", "A.", "B."]));
        Assert.Null(RepeatWatch.Loop(["A.", "B.", "A.", "C.", "A.", "B."]));
        Assert.Null(RepeatWatch.Loop([]));
    }

    [Fact]
    public void The_request_asks_for_repeatable_output_held_to_the_schema()
    {
        var (settings, _) = AnalysisSettingsFile.Parse(Settings(LocalProfile));
        var profile = settings.Profiles[0];
        var request = new ModelRequest(
            "test-template",
            2,
            "Describe only what the facts support.",
            "E1: a fact",
            [new ModelImage("E2", "image/png", [1, 2, 3])],
            ["description"],
            References);

        var body = ChatRequest.Build(profile, request);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("qwen3.6-35b-a3b", root.GetProperty("model").GetString());
        Assert.Equal(0, root.GetProperty("temperature").GetDouble());
        Assert.Equal(7, root.GetProperty("seed").GetInt64());
        Assert.Equal(4096, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("cache_prompt").GetBoolean());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.True(root.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.False(root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("description", root.GetProperty("response_format").GetProperty("schema").GetProperty("required")[0].GetString());
        var user = root.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal("E1: a fact", user[0].GetProperty("text").GetString());
        Assert.Equal("Picture E2:", user[1].GetProperty("text").GetString());
        Assert.Equal("data:image/png;base64,AQID", user[2].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal(ModelCapabilities.Image, request.Capability);
        Assert.Equal(ChatRequest.Sha256(body), ChatRequest.Sha256(ChatRequest.Build(profile, request)));
        Assert.Equal(64, ChatRequest.Sha256(body).Length);
    }

    [Fact]
    public void A_recorded_profile_says_whether_it_has_a_key_without_the_key()
    {
        var (settings, _) = AnalysisSettingsFile.Parse(Settings(LocalProfile));
        var profile = settings.Profiles[0] with { ProtectedApiKey = "secret" };

        var described = AnalysisSettingsFile.Describe(profile).ToJsonString();

        Assert.DoesNotContain("secret", described, StringComparison.Ordinal);
        Assert.Contains("\"hasApiKey\":true", described, StringComparison.Ordinal);
        Assert.Contains("-ncmoe 36", described, StringComparison.Ordinal);
    }
}
