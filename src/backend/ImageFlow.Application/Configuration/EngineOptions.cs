namespace ImageFlow;

public class EngineOptions
{
    public string Provider { get; set; } = "Mock";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string? ApiKey { get; set; }
    public string ProviderProfile { get; set; } = "openai";
    public string CredentialEnvironmentVariable { get; set; } = "OPENAI_API_KEY";
    public CompatibleCapabilities CompatibleCapabilities { get; set; } = new();
    public string? GetApiKey() => Environment.GetEnvironmentVariable(CredentialEnvironmentVariable) ?? ApiKey;
    public Uri GetBaseUri() => new(Provider == "Mock" ? "https://mock.invalid/" : Provider == "OpenAI" ? "https://api.openai.com/v1/" : BaseUrl.TrimEnd('/') + "/");
    public string PromptModel { get; set; } = ModelCatalog.DefaultPromptModel;
    public string ImageModel { get; set; } = ModelCatalog.DefaultImageModel;
    public string ImageSize { get; set; } = "1024x1024";
    public string ImageQuality { get; set; } = "medium";
    public string ImageComplexity { get; set; } = "high";
    public string OutputFormat { get; set; } = "png";
    public int MaxConcurrency { get; set; } = 2;
    public string DataDirectory { get; set; } = "data";
    public int MaxAttempts { get; set; } = 3;
    public bool WorkerEnabled { get; set; } = true;
    public void Validate()
    {
        if (Provider == "OpenAICompatible" && (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)))
            throw new InvalidOperationException("Compatible provider BaseUrl must be an HTTPS URL without credentials, query or fragment.");
        if (MaxConcurrency is < 1 or > 8 || MaxAttempts is < 1 or > 5) throw new InvalidOperationException("Invalid engine limits.");
        if (Provider is not ("Mock" or "OpenAI" or "OpenAICompatible")) throw new InvalidOperationException("Provider must be Mock, OpenAI or OpenAICompatible.");
        if (string.IsNullOrWhiteSpace(ProviderProfile) || ProviderProfile.Length > 100 ||
            !System.Text.RegularExpressions.Regex.IsMatch(CredentialEnvironmentVariable, "^[A-Za-z_][A-Za-z0-9_]{0,99}$"))
            throw new InvalidOperationException("A provider profile ID and a valid credential environment variable name are required.");
        if (Provider == "OpenAICompatible" && (ProviderProfile == "openai" || CredentialEnvironmentVariable == "OPENAI_API_KEY"))
            throw new InvalidOperationException("Compatible providers require their own explicit profile ID, credential reference and declared capabilities.");
        if (Provider != "Mock")
        {
            if (string.IsNullOrWhiteSpace(GetApiKey())) throw new InvalidOperationException("Provider credential is missing. Configure the referenced environment variable or secure local ApiKey.");
            ModelCatalog.ValidatePlanner(this);
        }
        ModelCatalog.ValidateImage(this, CreateImageRequest(""));
    }

    public ImageGenerationRequest CreateImageRequest(string prompt) => new(prompt, Provider, ProviderProfile,
        GetBaseUri().AbsoluteUri, CredentialEnvironmentVariable, ModelCatalog.AdapterVersion,
        Provider == "Mock" ? "mock-svg" : ImageModel,
        Provider == "Mock" ? "768x512" : ImageSize, Provider == "Mock" ? "preview" : ImageQuality,
        Provider == "Mock" ? "none" : ImageComplexity, Provider == "Mock" ? "svg" : OutputFormat);

    public string EstimateConfiguration() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(CreateImageRequest(""))));
}
