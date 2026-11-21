namespace ImageFlow;

// Deliberately reviewed adapter capabilities, not discovery from /models.
public sealed class CompatibleCapabilities
{
    public string[] PromptModels { get; set; } = [];
    public string[] ImageModels { get; set; } = [];
    public string[] Sizes { get; set; } = [];
    public string[] Qualities { get; set; } = [];
    public string[] Complexities { get; set; } = [];
    public string[] Formats { get; set; } = [];
}

public sealed record ImageCapability(string Model, string[] Sizes, string[] Qualities,
    string[] Complexities, string[] Formats, DateOnly? ShutdownDate, bool CustomDimensions = false);
public sealed record PlanningCapability(string Model, bool StructuredOutput, DateOnly? ShutdownDate);

public static class ModelCatalog
{
    public const string AdapterVersion = "images-png-v2";
    public const string DefaultPromptModel = "gpt-6-luna";
    public const string DefaultImageModel = "gpt-image-2.6-flare";
    // Reviewed 2026-11-21: official model pages and image-generation guide (see docs/model-upgrade.md).
    public static IReadOnlyList<PlanningCapability> OfficialPlanners { get; } = Array.AsReadOnly(new[]
    {
        new PlanningCapability("gpt-6-luna", true, null)
    });
    public static IReadOnlyList<ImageCapability> OfficialImages { get; } = Array.AsReadOnly(new[]
    {
        new ImageCapability(DefaultImageModel, ["1024x1024", "1536x1024", "1024x1536"],
            ["low", "medium", "high", "xhigh", "max"], ["low", "medium", "high"], ["png"], null, true),
        new ImageCapability("gpt-image-2.6-sunburst", ["1024x1024", "1536x1024", "1024x1536"],
            ["low", "medium", "high", "xhigh", "max"], ["low", "medium", "high"], ["png"], null, true)
    });

    public static void ValidatePlanner(EngineOptions options)
    {
        var capability = OfficialPlanners.SingleOrDefault(x => x.Model == options.PromptModel);
        var supported = options.Provider == "OpenAI"
            ? capability is { StructuredOutput: true } && !(capability.ShutdownDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            : options.CompatibleCapabilities.PromptModels.Contains(options.PromptModel);
        if (!supported) throw new InvalidOperationException("Prompt model is not declared for the Responses structured-output adapter.");
    }

    public static void ValidateImage(EngineOptions options, ImageGenerationRequest request)
    {
        if (request.AdapterVersion != AdapterVersion || request.Provider != options.Provider ||
            request.ProviderProfile != options.ProviderProfile || request.Endpoint != options.GetBaseUri().AbsoluteUri ||
            request.CredentialReference != options.CredentialEnvironmentVariable)
            throw new InvalidOperationException("Persisted execution profile does not match the configured provider endpoint/credential reference/adapter.");
        if (request.Provider == "Mock")
        {
            if (request.Model != "mock-svg" || request.Size != "768x512" || request.Quality != "preview" ||
                request.Complexity != "none" || request.OutputFormat != "svg") throw new InvalidOperationException("Unsupported Mock configuration.");
            return;
        }
        ImageCapability? capability;
        if (request.Provider == "OpenAI") capability = OfficialImages.SingleOrDefault(x => x.Model == request.Model);
        else
        {
            var c = options.CompatibleCapabilities;
            capability = c.ImageModels.Contains(request.Model)
                ? new(request.Model, c.Sizes, c.Qualities, c.Complexities, c.Formats, null) : null;
        }
        if (capability == null || capability.ShutdownDate <= DateOnly.FromDateTime(DateTime.UtcNow) ||
            !(capability.Sizes.Contains(request.Size) || capability.CustomDimensions && ValidDimensions(request.Size)) || !capability.Qualities.Contains(request.Quality) ||
            !capability.Complexities.Contains(request.Complexity) || !capability.Formats.Contains(request.OutputFormat) || request.OutputFormat != "png")
            throw new InvalidOperationException("Model/options are not supported by the declared image adapter capabilities.");
    }

    private static bool ValidDimensions(string size)
    {
        var parts = size.Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height) &&
            size == $"{width}x{height}" && width is > 0 and <= 3840 && height is > 0 and <= 3840 &&
            width % 16 == 0 && height % 16 == 0 && Math.Max(width, height) <= 3 * Math.Min(width, height) &&
            (long)width * height is >= 655360 and <= 8294400;
    }

    public static ImageGenerationRequest Request(Job job, string prompt) => new(prompt, job.Provider,
        job.ProviderProfile, job.Endpoint, job.CredentialReference, job.AdapterVersion, job.Model,
        job.ImageSize, job.ImageQuality, job.ImageComplexity, job.OutputFormat);

    public static bool CanExecute(EngineOptions options, Job job)
    {
        try { ValidateImage(options, Request(job, "")); return job.AttemptLimit is >= 1 and <= 5; }
        catch (InvalidOperationException) { return false; }
    }
}
