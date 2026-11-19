using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace ImageFlow;

public class OpenAiProvider(HttpClient http, EngineOptions options) : IPromptPlanningProvider, IImageGenerationProvider
{
    async Task<(JsonDocument, string?)> Send(string path, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(options.GetBaseUri(), path));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GetApiKey());
        req.Content = JsonContent.Create(body);
        HttpResponseMessage response;
        try { response = await http.SendAsync(req, ct); } catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { throw new ProviderFailure("Connection interrupted; provider outcome is unknown.", unknown: true); }
        using (response)
        {
            var id = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
            if (!response.IsSuccessStatusCode)
            {
                // Only an explicit rate-limit rejection is automatically safe to retry. 5xx can have ambiguous side effects.
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
                throw new ProviderFailure($"OpenAI HTTP {(int)response.StatusCode}; request {id ?? "unavailable"}.", response.StatusCode == HttpStatusCode.TooManyRequests, delay, (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout);
            }
            try { return (JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)), id); }
            catch (Exception e) when (e is JsonException or HttpRequestException or OperationCanceledException) { throw new ProviderFailure("Response interrupted or invalid; outcome unknown.", unknown: true); }
        }
    }
    public async Task<string[]> PlanAsync(string theme, int count, CancellationToken ct)
    {
        var (doc, _) = await Send("responses", new { model = options.PromptModel, input = $"Create {count} distinct detailed image prompts about: {theme}. Return a JSON object with a prompts array of strings, no other content.", text = new { format = new { type = "json_schema", name = "prompt_plan", strict = true, schema = new { type = "object", properties = new { prompts = new { type = "array", items = new { type = "string" } } }, required = new[] { "prompts" }, additionalProperties = false } } } }, ct);
        using (doc)
        {
            var text = string.Concat(doc.RootElement.GetProperty("output").EnumerateArray().Where(x => x.TryGetProperty("content", out _)).SelectMany(x => x.GetProperty("content").EnumerateArray()).Where(x => x.GetProperty("type").GetString() == "output_text").Select(x => x.GetProperty("text").GetString()));
            using var parsed = JsonDocument.Parse(text);
            var prompts = parsed.RootElement.GetProperty("prompts").EnumerateArray().Select(x => x.GetString() ?? "").Distinct().ToArray();
            if (prompts.Length != count || prompts.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 8000)) throw new ProviderFailure("Planner returned invalid prompt count or content.");
            return prompts;
        }
    }
    public async Task<ImageResult> GenerateAsync(string prompt, CancellationToken ct)
    {
        var (doc, id) = await Send("images/generations", new { model = options.ImageModel, prompt, n = 1, size = options.ImageSize, output_format = "png" }, ct);
        using (doc)
        {
            try { return new(Convert.FromBase64String(doc.RootElement.GetProperty("data")[0].GetProperty("b64_json").GetString()!), "png", "image/png", doc.RootElement.TryGetProperty("usage", out var u) ? u.GetRawText() : null, id); }
            catch (Exception e) when (e is KeyNotFoundException or FormatException or InvalidOperationException) { throw new ProviderFailure("Image response could not be decoded; outcome unknown.", unknown: true); }
        }
    }
}
