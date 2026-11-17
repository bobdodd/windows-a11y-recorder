namespace Recorder.Analysis;

/// <summary>
/// What a model is used for, each chosen in the project's settings
/// (projects.md, "The project's models"; owner, 2026-11-15: "projects have
/// settings and we can choose models (or no model) for report analysis,
/// for text-based checks, for vision-based checks").
/// </summary>
public static class ModelUses
{
    /// <summary>The summaries: the recording summary, the summary with vision and the Android summary.</summary>
    public const string Report = "report";

    /// <summary>Checks that ask a model about text, such as an item's name.</summary>
    public const string TextChecks = "text-checks";

    /// <summary>Checks that ask a model about a picture, such as an image of text.</summary>
    public const string VisionChecks = "vision-checks";

    /// <summary>
    /// The complex questions of text-based checks, and the simple ones the
    /// model for text-based checks could not settle (projects.md, "Simple
    /// and complex questions"; owner, 2026-11-17: "We need to be able to
    /// set the model per group of checks").
    /// </summary>
    public const string ComplexTextChecks = "text-checks-complex";

    /// <summary>The complex questions of vision-based checks, and the simple ones the model for vision-based checks could not settle.</summary>
    public const string ComplexVisionChecks = "vision-checks-complex";

    public static IReadOnlyList<string> All { get; } = [Report, TextChecks, ComplexTextChecks, VisionChecks, ComplexVisionChecks];

    /// <summary>The use as the player names it, in a sentence.</summary>
    public static string Name(string use) => use switch
    {
        Report => "report analysis",
        TextChecks => "text-based checks",
        VisionChecks => "vision-based checks",
        ComplexTextChecks => "complex questions of text-based checks",
        ComplexVisionChecks => "complex questions of vision-based checks",
        _ => use
    };

    /// <summary>What a profile must take to be chosen for the use.</summary>
    public static string Capability(string use) =>
        use is VisionChecks or ComplexVisionChecks ? ModelCapabilities.Image : ModelCapabilities.Text;

    /// <summary>Whether the use is the complex questions of a kind of check.</summary>
    public static bool IsComplex(string use) => use is ComplexTextChecks or ComplexVisionChecks;

    /// <summary>The use for the complex questions of a kind of check, from its use for simple ones.</summary>
    public static string Complex(string use) => use switch
    {
        TextChecks => ComplexTextChecks,
        VisionChecks => ComplexVisionChecks,
        _ => throw new ArgumentOutOfRangeException(nameof(use), use, "Only checks have complex questions.")
    };

    /// <summary>The use for the simple questions of a kind of check, from its use for complex ones.</summary>
    public static string Simple(string use) => use switch
    {
        ComplexTextChecks => TextChecks,
        ComplexVisionChecks => VisionChecks,
        _ => throw new ArgumentOutOfRangeException(nameof(use), use, "Only complex questions have simple ones.")
    };
}

/// <summary>
/// A project's models: the profile, by name, chosen for each use, or null
/// for no model, when no model is asked, cloud or local. A new project
/// has no model for any use, so a recording is analysed without AI until
/// one is chosen (projects.md, "The project's models"). The complex
/// questions of each kind of check have their own choice, which is null
/// for the same model as its simple questions, so that choosing one model
/// for a kind of check is enough, or <see cref="NoModel"/> for none
/// (projects.md, "Simple and complex questions").
/// </summary>
public sealed record ProjectModels(string? Report, string? TextChecks, string? VisionChecks)
{
    public static ProjectModels None { get; } = new(null, null, null);

    /// <summary>The choice of no model for complex questions, as saved: null there is the same model as simple questions.</summary>
    public const string NoModel = "";

    /// <summary>The choice for complex questions of text-based checks: null for the same model as simple ones, <see cref="NoModel"/> for none, or a profile.</summary>
    public string? ComplexTextChecks { get; init; }

    /// <summary>The choice for complex questions of vision-based checks: null for the same model as simple ones, <see cref="NoModel"/> for none, or a profile.</summary>
    public string? ComplexVisionChecks { get; init; }

    /// <summary>
    /// The project's Anthropic API key, protected for this Windows user,
    /// or null for none. A Claude model chosen for a use needs it.
    /// </summary>
    public string? ProtectedAnthropicKey { get; init; }

    /// <summary>The models the project can choose from, its Claude models with its key.</summary>
    public AnalysisSettings Choices(AnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ForProject(ProtectedAnthropicKey);
    }

    /// <summary>
    /// The profile a use asks, or null for no model: for complex
    /// questions, the model for simple ones where the choice is the same.
    /// </summary>
    public string? For(string use) => use switch
    {
        ModelUses.Report => Report,
        ModelUses.TextChecks => TextChecks,
        ModelUses.VisionChecks => VisionChecks,
        ModelUses.ComplexTextChecks => Effective(ComplexTextChecks, TextChecks),
        ModelUses.ComplexVisionChecks => Effective(ComplexVisionChecks, VisionChecks),
        _ => throw new ArgumentOutOfRangeException(nameof(use), use, "No such use of a model.")
    };

    /// <summary>
    /// The choice for a use as saved: for complex questions, null for the
    /// same model as simple ones and <see cref="NoModel"/> for none; for
    /// the other uses, as <see cref="For"/>.
    /// </summary>
    public string? Chosen(string use) => use switch
    {
        ModelUses.ComplexTextChecks => ComplexTextChecks,
        ModelUses.ComplexVisionChecks => ComplexVisionChecks,
        _ => For(use)
    };

    /// <summary>
    /// The models with one use's choice changed: for complex questions,
    /// null is the same model as simple ones and <see cref="NoModel"/>, or
    /// any blank, is none; for the other uses, null or blank is no model.
    /// </summary>
    public ProjectModels With(string use, string? profile)
    {
        var name = string.IsNullOrWhiteSpace(profile) ? null : profile;
        var complex = profile is null ? null : name ?? NoModel;
        return use switch
        {
            ModelUses.Report => this with { Report = name },
            ModelUses.TextChecks => this with { TextChecks = name },
            ModelUses.VisionChecks => this with { VisionChecks = name },
            ModelUses.ComplexTextChecks => this with { ComplexTextChecks = complex },
            ModelUses.ComplexVisionChecks => this with { ComplexVisionChecks = complex },
            _ => throw new ArgumentOutOfRangeException(nameof(use), use, "No such use of a model.")
        };
    }

    /// <summary>Whether any use has a model.</summary>
    public bool Any => ModelUses.All.Any(use => For(use) is not null);

    private static string? Effective(string? complex, string? simple) =>
        complex is null ? simple : complex.Length == 0 ? null : complex;

    /// <summary>
    /// The stage profiles a check task is queued with: each check use that
    /// has a model, by its use, so that the checks that need one ask the
    /// model chosen when the task was queued. Complex questions are by
    /// their own use, with the model they ask.
    /// </summary>
    public IReadOnlyDictionary<string, string> ForChecks()
    {
        var profiles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var use in new[] { ModelUses.TextChecks, ModelUses.ComplexTextChecks, ModelUses.VisionChecks, ModelUses.ComplexVisionChecks })
        {
            if (For(use) is { } name)
            {
                profiles[use] = name;
            }
        }

        return profiles;
    }

    /// <summary>
    /// Why a kind of check's models cannot be asked, or null when they
    /// can: the model for its simple questions, then, where a question it
    /// does not settle is asked again of another model, that model
    /// (projects.md, "Simple and complex questions"; owner, 2026-11-17:
    /// "Make the button also require a usable complex model when one is
    /// chosen"). No model for simple questions is not a problem here: the
    /// kind's questions are then not asked. The settings are the project's
    /// choices, <see cref="Choices"/>.
    /// </summary>
    public string? ChecksProblem(AnalysisSettings settings, string use)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (use is not (ModelUses.TextChecks or ModelUses.VisionChecks))
        {
            throw new ArgumentOutOfRangeException(nameof(use), use, "Not the use for a kind of check's simple questions.");
        }

        if (For(use) is not { } simple)
        {
            return null;
        }

        if (Problem(settings, use, simple) is { } problem)
        {
            return problem;
        }

        var complex = ModelUses.Complex(use);
        return For(complex) is { } name && name != simple ? Problem(settings, complex, name) : null;
    }

    /// <summary>
    /// Why a profile cannot be used for a use, or null when it can: it is
    /// not one the project can choose from, cannot take what the use
    /// needs, or is a Claude model and the project has no API key. The
    /// settings are the project's choices, <see cref="Choices"/>.
    /// </summary>
    public static string? Problem(AnalysisSettings settings, string use, string profileName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Profile(profileName) is not { } profile)
        {
            return $"The project's model for {ModelUses.Name(use)}, \"{profileName}\", is not offered on this PC.";
        }

        var capability = ModelUses.Capability(use);
        if (!profile.Supports(capability))
        {
            return $"The project's model for {ModelUses.Name(use)}, \"{profileName}\", cannot take {(capability == ModelCapabilities.Image ? "images" : "text")}.";
        }

        return profile.SendsToAnthropic && profile.ProtectedApiKey is null
            ? $"The project's model for {ModelUses.Name(use)} is {profileName}, but no Anthropic API key is saved in the project's settings."
            : null;
    }
}
