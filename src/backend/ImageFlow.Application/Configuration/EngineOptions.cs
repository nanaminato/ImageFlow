namespace ImageFlow;

public class EngineOptions
{
    public string Provider { get; set; } = "Mock";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string? ApiKey { get; set; }
    public string? GetApiKey() => Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? ApiKey;
    public Uri GetBaseUri() => new(Provider == "OpenAI" ? "https://api.openai.com/v1/" : BaseUrl.TrimEnd('/') + "/");
    public string PromptModel { get; set; } = "gpt-4.1-mini";
    public string ImageModel { get; set; } = "gpt-image-1.5";
    public string ImageSize { get; set; } = "1024x1024";
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
        if (Provider != "Mock" && PromptModel is not ("gpt-4.1-mini" or "gpt-4.1" or "gpt-4.1-nano")) throw new InvalidOperationException("Prompt model must support the configured structured-output adapter: gpt-4.1, gpt-4.1-mini, or gpt-4.1-nano.");
        if (Provider != "Mock" && (string.IsNullOrWhiteSpace(GetApiKey()) || ImageModel is not ("gpt-image-1.5" or "gpt-image-1-mini") || ImageSize is not ("1024x1024" or "1536x1024" or "1024x1536")))
            throw new InvalidOperationException("OpenAI requires OPENAI_API_KEY and a supported model/size configuration. See README.");
    }
}
