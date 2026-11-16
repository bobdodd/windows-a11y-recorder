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
        Assert.Equal(new Dictionary<string, string> { [ModelUses.VisionChecks] = "Vision" }, models.ForChecks());
        Assert.Throws<ArgumentOutOfRangeException>(() => models.For("summary"));
    }

    [Fact]
    public void A_profile_must_be_there_and_take_what_the_use_needs()
    {
        Assert.Null(ProjectModels.Problem(Settings, ModelUses.Report, "Text"));
        Assert.Null(ProjectModels.Problem(Settings, ModelUses.VisionChecks, "Vision"));
        Assert.Equal("The project's model for vision-based checks, \"Text\", cannot take images.",
            ProjectModels.Problem(Settings, ModelUses.VisionChecks, "Text"));
        Assert.Equal("The project's model for report analysis, \"Gone\", is not in the model profiles.",
            ProjectModels.Problem(Settings, ModelUses.Report, "Gone"));
    }

    [Fact]
    public void The_choices_are_no_model_then_the_profiles_that_can_and_a_saved_name_is_kept()
    {
        Assert.Equal([(null, "No model"), ("Text", "Text"), ("Vision", "Vision")], AnalysisPlayer.ModelChoices(Settings, ModelUses.TextChecks, null));
        Assert.Equal([(null, "No model"), ("Vision", "Vision")], AnalysisPlayer.ModelChoices(Settings, ModelUses.VisionChecks, "Vision"));
        Assert.Equal((string?)"Gone", AnalysisPlayer.ModelChoices(Settings, ModelUses.Report, "Gone")[^1].Name);
        Assert.Equal("Gone (not in the model profiles)", AnalysisPlayer.ModelChoices(Settings, ModelUses.Report, "Gone")[^1].Label);
        Assert.Equal("Text (cannot take images)", AnalysisPlayer.ModelChoices(Settings, ModelUses.VisionChecks, "Text")[^1].Label);
    }

    [Fact]
    public void The_models_are_named_by_use_in_words()
    {
        Assert.Equal("report analysis, Text; text-based checks, no model; vision-based checks, Gone (not in the model profiles)",
            AnalysisPlayer.ModelsLine(Settings, new ProjectModels("Text", null, "Gone"), ModelUses.All));
        Assert.Equal("No model.", AnalysisPlayer.TaskModelsLine(new Dictionary<string, string>(), aiAnalysis: true));
        Assert.Equal("No model.", AnalysisPlayer.TaskModelsLine(new Dictionary<string, string> { [ModelCapabilities.Text] = "Text" }, aiAnalysis: false));
        Assert.Equal("Models: report analysis, Vision.",
            AnalysisPlayer.TaskModelsLine(new Dictionary<string, string> { [ModelCapabilities.Text] = "Vision", [ModelCapabilities.Image] = "Vision" }, aiAnalysis: true));
        Assert.Equal("Models: text-based checks, Text; vision-based checks, Vision.",
            AnalysisPlayer.TaskModelsLine(new Dictionary<string, string> { [ModelUses.VisionChecks] = "Vision", [ModelUses.TextChecks] = "Text" }, aiAnalysis: true));
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
