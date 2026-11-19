namespace ImageFlow;

public interface IPromptPlanningProvider { Task<string[]> PlanAsync(string theme, int count, CancellationToken ct); }
public interface IImageGenerationProvider { Task<ImageResult> GenerateAsync(string prompt, CancellationToken ct); }
public record ImageResult(byte[] Bytes, string Extension, string ContentType, string? UsageJson, string? RequestId);
public class ProviderFailure(string message, bool retryable = false, TimeSpan? retryAfter = null, bool unknown = false) : Exception(message)
{
    public bool Retryable { get; } = retryable; public TimeSpan? RetryAfter { get; } = retryAfter; public bool Unknown { get; } = unknown;
}
