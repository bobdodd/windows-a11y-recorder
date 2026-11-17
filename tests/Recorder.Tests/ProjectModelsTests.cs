using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

// A project's models: the profile, or no model, for report analysis,
// text-based checks and vision-based checks (owner, 2026-11-15;
// projects.md, "The project's models"). The catalog and the worker's
// refusal are in AnalysisWorkerTests.
public sealed class ProjectModelsTests
{
    private static ModelProfile Profile(string name, params string[] capabilities) =>
        new(name, new Uri("http://127.0.0.1:8080/"), "m", capabilities, new ModelFile("m.gguf", null, null, null), null, GenerationSettings.Default, null);

    private static readonly AnalysisSettings Settings = new(
        [Profile("Text", ModelCapabilities.Text), Profile("Vision", ModelCapabilities.Text, ModelCapabilities.Image)], null, 3);

    [Fact]
    public void A_project_starts_with_no_model_for_any_use()
    {
        Assert.False(ProjectModels.None.Any);
        Assert.All(ModelUses.All, use => Assert.Null(ProjectModels.None.For(use)));
        Assert.Empty(ProjectModels.None.ForChecks());
    }

    [Fact]
    public void Each_use_is_set_on_its_own_and_an_empty_name_is_no_model()
    {
        var models = ProjectModels.None.With(ModelUses.Report, "Text").With(ModelUses.VisionChecks, "Vision");
        Assert.Equal(new ProjectModels("Text", null, "Vision"), models);
        Assert.True(models.Any);
        Assert.Null(models.With(ModelUses.Report, " ").Report);
        Assert.Equal(new Dictionary<string, string> { [ModelUses.VisionChecks] = "Vision", [ModelUses.ComplexVisionChecks] = "Vision" }, models.ForChecks());
        Assert.Throws<ArgumentOutOfRangeException>(() => models.For("summary"));
    }

    [Fact]
    public void A_profile_must_be_there_and_take_what_the_use_needs()
    {
        Assert.Null(ProjectModels.Problem(Settings, ModelUses.Report, "Text"));
        Assert.Null(ProjectModels.Problem(Settings, ModelUses.VisionChecks, "Vision"));
        Assert.Equal("The project's model for vision-based checks, \"Text\", cannot take images.",
            ProjectModels.Problem(Settings, ModelUses.VisionChecks, "Text"));
        Assert.Equal("The project's model for report analysis, \"Gone\", is not offered on this PC.",
            ProjectModels.Problem(Settings, ModelUses.Report, "Gone"));
    }

    [Fact]
    public void The_choices_are_no_model_then_the_profiles_that_can_and_a_saved_name_is_kept()
    {
        Assert.Equal([(null, "No model"), ("Text", "Text"), ("Vision", "Vision")], AnalysisPlayer.ModelChoices(Settings, ModelUses.TextChecks, null));
        Assert.Equal([(null, "No model"), ("Vision", "Vision")], AnalysisPlayer.ModelChoices(Settings, ModelUses.VisionChecks, "Vision"));
        Assert.Equal((string?)"Gone", AnalysisPlayer.ModelChoices(Settings, ModelUses.Report, "Gone")[^1].Name);
        Assert.Equal("Gone (not offered on this PC)", AnalysisPlayer.ModelChoices(Settings, ModelUses.Report, "Gone")[^1].Label);
        Assert.Equal("Text (cannot take images)", AnalysisPlayer.ModelChoices(Settings, ModelUses.VisionChecks, "Text")[^1].Label);
    }

    [Fact]
    public void The_models_are_named_by_use_in_words()
    {
        Assert.Equal("report analysis, Text; text-based checks, no model; complex questions of text-based checks, the same model as simple questions; " +
            "vision-based checks, Gone (not offered on this PC); complex questions of vision-based checks, no model",
            AnalysisPlayer.ModelsLine(Settings, new ProjectModels("Text", null, "Gone") { ComplexVisionChecks = ProjectModels.NoModel }, ModelUses.All));
        Assert.Equal("No model.", AnalysisPlayer.TaskModelsLine(new Dictionary<string, string>(), aiAnalysis: true));
        Assert.Equal("No model.", AnalysisPlayer.TaskModelsLine(new Dictionary<string, string> { [ModelCapabilities.Text] = "Text" }, aiAnalysis: false));
        Assert.Equal("Models: report analysis, Vision.",
            AnalysisPlayer.TaskModelsLine(new Dictionary<string, string> { [ModelCapabilities.Text] = "Vision", [ModelCapabilities.Image] = "Vision" }, aiAnalysis: true));
        Assert.Equal("Models: text-based checks, Text; vision-based checks, Vision.",
            AnalysisPlayer.TaskModelsLine(new Dictionary<string, string> { [ModelUses.VisionChecks] = "Vision", [ModelUses.TextChecks] = "Text" }, aiAnalysis: true));
    }

    // projects.md, "Simple and complex questions" (owner, 2026-11-17).
    [Fact]
    public void Complex_questions_ask_the_model_for_simple_ones_unless_another_or_none_is_chosen()
    {
        var models = new ProjectModels(null, "Text", "Vision");
        Assert.Equal("Vision", models.For(ModelUses.ComplexVisionChecks));
        Assert.Equal("Text", models.For(ModelUses.ComplexTextChecks));
        Assert.Null(models.Chosen(ModelUses.ComplexVisionChecks));
        Assert.Null(CheckQuestions.EscalationProfile(models, ModelUses.VisionChecks));

        var other = models.With(ModelUses.ComplexVisionChecks, "Big");
        Assert.Equal("Big", other.For(ModelUses.ComplexVisionChecks));
        Assert.Equal("Big", CheckQuestions.EscalationProfile(other, ModelUses.VisionChecks));
        Assert.Equal("Big", other.ForChecks()[ModelUses.ComplexVisionChecks]);

        var none = models.With(ModelUses.ComplexVisionChecks, ProjectModels.NoModel);
        Assert.Null(none.For(ModelUses.ComplexVisionChecks));
        Assert.Equal(ProjectModels.NoModel, none.Chosen(ModelUses.ComplexVisionChecks));
        Assert.False(none.ForChecks().ContainsKey(ModelUses.ComplexVisionChecks));
        Assert.Null(CheckQuestions.EscalationProfile(none, ModelUses.VisionChecks));
        Assert.Null(none.With(ModelUses.ComplexVisionChecks, null).ComplexVisionChecks);

        // No model for simple questions is none for complex ones that are the same.
        Assert.Null(ProjectModels.None.For(ModelUses.ComplexVisionChecks));
        Assert.True(ProjectModels.None.With(ModelUses.ComplexTextChecks, "Text").Any);
        Assert.Equal(ModelCapabilities.Image, ModelUses.Capability(ModelUses.ComplexVisionChecks));
        Assert.Equal(ModelUses.ComplexTextChecks, ModelUses.Complex(ModelUses.TextChecks));
        Assert.Equal(ModelUses.VisionChecks, ModelUses.Simple(ModelUses.ComplexVisionChecks));
    }

    [Fact]
    public void The_choices_for_complex_questions_start_with_the_same_model_then_no_model()
    {
        Assert.Equal([(null, "The same model as simple questions"), ("", "No model"), ("Vision", "Vision")],
            AnalysisPlayer.ModelChoices(Settings, ModelUses.ComplexVisionChecks, null));
        Assert.Equal(3, AnalysisPlayer.ModelChoices(Settings, ModelUses.ComplexVisionChecks, ProjectModels.NoModel).Count);
        Assert.Equal("Text (cannot take images)", AnalysisPlayer.ModelChoices(Settings, ModelUses.ComplexVisionChecks, "Text")[^1].Label);
    }

    [Fact]
    public void A_question_is_not_settled_by_two_answers_or_by_agreeing_it_cannot_tell()
    {
        var logo = new ClosedAnswerOf("logotype", "A brand name.");
        var cannot = new ClosedAnswerOf("cannot-tell", "Too small.");
        Assert.False(CheckQuestions.Unsettled(logo, logo, "cannot-tell"));
        Assert.True(CheckQuestions.Unsettled(logo, cannot, "cannot-tell"));
        Assert.True(CheckQuestions.Unsettled(cannot, cannot, "cannot-tell"));

        var big = new GroupAnswers(logo, new ClosedAnswerOf("logotype", "A logo."), "Big", Cloud: true);
        var split = new QuestionAnswers(new GroupAnswers(logo, cannot, "Small", Cloud: true), big);
        var judged = CheckQuestions.Judge(split, group => AndroidImagesOfText.Interpret(group.One, group.Two, group.Model, group.Cloud));
        Assert.Equal(("Big", true), (judged.Model, judged.Cloud));
        Assert.StartsWith("Small, a cloud model, the project's model for simple questions, gave two answers, logotype and cannot-tell, " +
            "so the question was asked again of the project's model for complex questions; Big, a cloud model, inferred from the pictures twice", judged.Text, StringComparison.Ordinal);

        var once = CheckQuestions.Judge(new QuestionAnswers(new GroupAnswers(cannot, cannot, "Small", Cloud: false), null),
            group => AndroidImagesOfText.Interpret(group.One, group.Two, group.Model, group.Cloud));
        Assert.StartsWith("Small, a local model, could not tell", once.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Kept_answers_of_the_model_for_complex_questions_are_read_from_run_three()
    {
        static KeptAnswer Kept(string answer) => new(System.Text.Json.JsonDocument.Parse($$"""{ "reason": "r", "answer": "{{answer}}" }""").RootElement, AnalysisWorker.ServiceBasis, "{}");
        var kept = new Dictionary<string, KeptAnswer> { ["q-3"] = Kept("logotype"), ["q-4"] = Kept("cannot-tell") };
        var answers = CheckQuestions.FromKept(kept, run => $"q-{run}", CheckQuestions.EscalatedRun, "Big");
        Assert.Equal(("logotype", "cannot-tell", true), (answers!.One.Answer, answers.Two.Answer, answers.Cloud));
        Assert.Null(CheckQuestions.FromKept(kept, run => $"q-{run}", 1, "Big"));
        Assert.Null(CheckQuestions.FromKept(new Dictionary<string, KeptAnswer> { ["q-3"] = Kept("logotype") }, run => $"q-{run}", 3, "Big"));
    }

    [Fact]
    public void The_settings_file_no_longer_chooses_models()
    {
        var (settings, problems) = AnalysisSettingsFile.Parse("""{ "version": 1, "aiAnalysis": false, "defaults": { "text": "Text" } }""");
        Assert.Empty(problems);
        Assert.Empty(settings.Profiles);
    }

    [Fact]
    public void Only_the_summaries_need_a_model()
    {
        Assert.True(AnalysisPlayer.NeedsModel(RecordingSummaryKind.Name));
        Assert.True(AnalysisPlayer.NeedsModel(RecordingSummaryVisionKind.Name));
        Assert.True(AnalysisPlayer.NeedsModel(AndroidSummaryKind.Name));
        Assert.False(AnalysisPlayer.NeedsModel(AndroidReportKind.Name));
        Assert.False(AnalysisPlayer.NeedsModel(AndroidProjectReportKind.Name));
    }
}
