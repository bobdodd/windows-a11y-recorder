using System.Text.Json;
using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// A check's question for a model: the recorder's own check of the answer,
/// and what the images-of-text check makes of two answers. See
/// docs/architecture/android-vision-checks.md.
/// </summary>
public sealed class ModelChecksTests
{
    private static readonly string[] Answers = ["logotype", "could-be-text", "cannot-tell"];

    [Fact]
    public void A_closed_answer_is_one_of_its_answers_with_a_reason()
    {
        var (output, failure) = ClosedAnswer.Check("""{ "answer": "logotype", "reason": "The words are the app's name in its logo." }""", Answers);

        Assert.Null(failure);
        Assert.Equal(new ClosedAnswerOf("logotype", "The words are the app's name in its logo."), ClosedAnswer.Read(output));
    }

    [Theory]
    [InlineData("", "The output is empty.")]
    [InlineData("[1]", "The output is not a JSON object.")]
    [InlineData("""{ "reason": "No answer." }""", "The output has no answer.")]
    [InlineData("""{ "answer": "decoration", "reason": "Plain." }""", "The answer \"decoration\" is not one of the question's answers: logotype, could-be-text, cannot-tell.")]
    [InlineData("""{ "answer": "logotype", "reason": " " }""", "The answer gives no reason.")]
    [InlineData("""{ "answer": "logotype", "reason": "A logo.", "confidence": 0.9 }""", "The output has a member \"confidence\" the question does not ask for.")]
    public void A_closed_answer_that_fails_is_refused_with_why(string output, string failure)
    {
        Assert.Equal(failure, ClosedAnswer.Check(output, Answers).Failure);
    }

    [Fact]
    public void A_closed_answers_schema_asks_for_a_reason_and_then_one_of_its_answers()
    {
        var schema = ClosedAnswer.Schema(Answers);

        Assert.Equal(
            """{"type":"object","properties":{"reason":{"type":"string","minLength":1},"answer":{"type":"string","enum":["logotype","could-be-text","cannot-tell"]}},"required":["reason","answer"],"additionalProperties":false}""",
            schema.ToJsonString());
        Assert.Null(ClosedAnswer.Read(JsonSerializer.SerializeToElement(new { answer = "logotype" })));
    }

    [Theory]
    [InlineData("logotype", PhoneCheckLevel.Info, "the exception for logotypes applies, not confirmed")]
    [InlineData("essential", PhoneCheckLevel.Info, "this presentation of the text is essential, so the exception applies, not confirmed")]
    [InlineData("picture-with-other-content", PhoneCheckLevel.Info, "so it is not an image of text, not confirmed")]
    [InlineData("could-be-text", PhoneCheckLevel.Error, "so it is an image of text; whether a user can customize it is not judged")]
    [InlineData("cannot-tell", PhoneCheckLevel.Warning, "check manually")]
    [InlineData("not-loaded", PhoneCheckLevel.NotRun, "the image had not loaded and the browser drew a stand-in in its place, so the image itself was not seen and the check is not run on it, not confirmed")]
    public void Two_answers_that_agree_judge_the_image_as_an_inference(string answer, PhoneCheckLevel level, string says)
    {
        var inference = AndroidImagesOfText.Interpret(new(answer, "First reason."), new(answer, "Second reason."), "Gemma", cloud: false);

        Assert.Equal(level, inference.Level);
        Assert.True(inference.Inferred);
        Assert.Equal(("Gemma", false), (inference.Model, inference.Cloud));
        Assert.StartsWith("Gemma, a local model,", inference.Text, StringComparison.Ordinal);
        Assert.Contains(says, inference.Text, StringComparison.Ordinal);
        Assert.Contains("\"First reason.\"", inference.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_answers_that_differ_are_a_warning_to_check_manually()
    {
        var inference = AndroidImagesOfText.Interpret(new("logotype", "A logo."), new("could-be-text", "Plain words."), "Haiku", cloud: true);

        Assert.Equal(PhoneCheckLevel.Warning, inference.Level);
        Assert.True(inference.Inferred);
        Assert.Equal(
            "Haiku, a cloud model, was asked twice and gave two answers, logotype and could-be-text: \"A logo.\" and \"Plain words.\"; check manually",
            inference.Text);
    }

    [Fact]
    public void One_answer_of_a_repeatable_model_is_not_said_to_be_twice()
    {
        var answer = new ClosedAnswerOf("logotype", "A logo.");

        Assert.DoesNotContain("twice", AndroidImagesOfText.Interpret(answer, answer, "Gemma", cloud: false).Text, StringComparison.Ordinal);
        Assert.Contains("twice", AndroidImagesOfText.Interpret(answer, answer with { }, "Gemma", cloud: false).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stated", """{"temperature": 0, "seed": 0}""", true)]
    [InlineData("stated", """{"temperature": 0.3, "seed": 0}""", false)]
    [InlineData("service", """{"provider": "anthropic"}""", false)]
    [InlineData("stated", "", false)]
    [InlineData("stated", "not json", false)]
    public void A_model_is_repeatable_only_at_temperature_0_on_this_network(string basis, string parameters, bool repeatable)
    {
        Assert.Equal(repeatable, AndroidImagesOfText.Repeatable(basis, parameters));
    }

    [Theory]
    [InlineData("heading", PhoneCheckLevel.Error, "that the text is shown as a heading, so it should be marked as one")]
    [InlineData("not-heading", PhoneCheckLevel.Info, "that the text is not shown as a heading, not confirmed")]
    [InlineData("cannot-tell", PhoneCheckLevel.Warning, "check manually")]
    public void Two_answers_that_agree_judge_the_text_shown_as_a_heading_as_an_inference(string answer, PhoneCheckLevel level, string says)
    {
        var inference = AndroidHeadingLook.Interpret(new(answer, "First reason."), new(answer, "Second reason."), "Haiku", cloud: true);

        Assert.Equal(level, inference.Level);
        Assert.True(inference.Inferred);
        Assert.Equal(("Haiku", true), (inference.Model, inference.Cloud));
        Assert.StartsWith("Haiku, a cloud model,", inference.Text, StringComparison.Ordinal);
        Assert.Contains(says, inference.Text, StringComparison.Ordinal);
        Assert.Contains("\"First reason.\"", inference.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_answers_about_a_heading_that_differ_are_a_warning_to_check_manually()
    {
        var inference = AndroidHeadingLook.Interpret(new("heading", "Large."), new("not-heading", "A value."), "Haiku", cloud: true);

        Assert.Equal(PhoneCheckLevel.Warning, inference.Level);
        Assert.EndsWith("; check manually", inference.Text, StringComparison.Ordinal);
        Assert.Equal(
            "1 piece of text left by the rules was not asked about, as no model may be used for vision-based checks: no model is chosen",
            AndroidHeadingLook.NotAsked(1, "no model is chosen"));
    }

    [Theory]
    [InlineData("informative", PhoneCheckLevel.Error, "that the image gives information, so it needs a text alternative")]
    [InlineData("decorative", PhoneCheckLevel.Info, "that the image is decorative, giving no information of its own, not confirmed")]
    [InlineData("cannot-tell", PhoneCheckLevel.Warning, "check manually")]
    public void Two_answers_that_agree_judge_the_image_with_no_text_alternative_as_an_inference(string answer, PhoneCheckLevel level, string says)
    {
        var inference = AndroidImageAlternative.Interpret(new(answer, "First reason."), new(answer, "Second reason."), "Haiku", cloud: true);

        Assert.Equal(level, inference.Level);
        Assert.True(inference.Inferred);
        Assert.Equal(("Haiku", true), (inference.Model, inference.Cloud));
        Assert.StartsWith("Haiku, a cloud model,", inference.Text, StringComparison.Ordinal);
        Assert.Contains(says, inference.Text, StringComparison.Ordinal);
        Assert.Contains("\"First reason.\"", inference.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("same image", inference.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_answers_about_an_image_that_differ_are_a_warning_to_check_manually()
    {
        var inference = AndroidImageAlternative.Interpret(new("informative", "An icon."), new("decorative", "A flourish."), "Haiku", cloud: true, again: true);

        Assert.Equal(PhoneCheckLevel.Warning, inference.Level);
        Assert.Contains("was asked twice for the same image, by its pixels, the text beside it and what a screen reader says on the screen, and gave two answers", inference.Text, StringComparison.Ordinal);
        Assert.EndsWith("; check manually", inference.Text, StringComparison.Ordinal);
        Assert.Equal(
            "2 images left by the rules were not asked about, as no model may be used for vision-based checks: no model is chosen",
            AndroidImageAlternative.NotAsked(2, "no model is chosen"));
    }

    [Fact]
    public void Questions_not_asked_say_why()
    {
        Assert.Equal(
            "2 images left by the rules were not asked about, as no model may be used for vision-based checks: no model is chosen",
            AndroidImagesOfText.NotAsked(2, "no model is chosen"));
    }
}
