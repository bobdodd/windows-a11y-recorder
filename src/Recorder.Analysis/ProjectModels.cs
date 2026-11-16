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

    public static IReadOnlyList<string> All { get; } = [Report, TextChecks, VisionChecks];

    /// <summary>The use as the player names it, in a sentence.</summary>
    public static string Name(string use) => use switch
    {
        Report => "report analysis",
        TextChecks => "text-based checks",
        VisionChecks => "vision-based checks",
        _ => use
    };

    /// <summary>What a profile must take to be chosen for the use.</summary>
    public static string Capability(string use) =>
        use == VisionChecks ? ModelCapabilities.Image : ModelCapabilities.Text;
}

/// <summary>
/// A project's models: the profile, by name, chosen for each use, or null
/// for no model, when no model is asked, cloud or local. A new project
/// has no model for any use, so a recording is analysed without AI until
/// one is chosen (projects.md, "The project's models").
/// </summary>
public sealed record ProjectModels(string? Report, string? TextChecks, string? VisionChecks)
{
    public static ProjectModels None { get; } = new(null, null, null);

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

    /// <summary>The profile chosen for a use, or null for no model.</summary>
    public string? For(string use) => use switch
    {
        ModelUses.Report => Report,
        ModelUses.TextChecks => TextChecks,
        ModelUses.VisionChecks => VisionChecks,
        _ => throw new ArgumentOutOfRangeException(nameof(use), use, "No such use of a model.")
    };

    public ProjectModels With(string use, string? profile)
    {
        var name = string.IsNullOrWhiteSpace(profile) ? null : profile;
        return use switch
        {
            ModelUses.Report => this with { Report = name },
            ModelUses.TextChecks => this with { TextChecks = name },
            ModelUses.VisionChecks => this with { VisionChecks = name },
            _ => throw new ArgumentOutOfRangeException(nameof(use), use, "No such use of a model.")
        };
    }

    /// <summary>Whether any use has a model.</summary>
    public bool Any => Report is not null || TextChecks is not null || VisionChecks is not null;

    /// <summary>
    /// The stage profiles a check task is queued with: each check use that
    /// has a model, by its use, so that the checks that need one ask the
    /// model chosen when the task was queued.
    /// </summary>
    public IReadOnlyDictionary<string, string> ForChecks()
    {
        var profiles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (TextChecks is { } text)
        {
            profiles[ModelUses.TextChecks] = text;
        }

        if (VisionChecks is { } vision)
        {
            profiles[ModelUses.VisionChecks] = vision;
        }

        return profiles;
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
