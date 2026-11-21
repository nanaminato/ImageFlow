namespace ImageFlow;

public interface IPromptPlanningProvider { Task<string[]> PlanAsync(string theme, int count, CancellationToken ct); }
public sealed record ImageGenerationRequest(string Prompt, string Provider, string ProviderProfile,
    string Endpoint, string CredentialReference, string AdapterVersion, string Model, string Size,
    string Quality, string Complexity, string OutputFormat);
public interface IImageGenerationProvider { Task<ImageResult> GenerateAsync(ImageGenerationRequest request, CancellationToken ct); }
public record ImageResult(byte[] Bytes, string Extension, string ContentType, string? UsageJson, string? RequestId);
public class ProviderFailure(string message, bool retryable = false, TimeSpan? retryAfter = null, bool unknown = false, string? requestId = null, string? usageJson = null) : Exception(message)
{
    public string? UsageJson { get; } = usageJson;
    public string? RequestId { get; } = requestId;
    public bool Retryable { get; } = retryable; public TimeSpan? RetryAfter { get; } = retryAfter; public bool Unknown { get; } = unknown;
}
