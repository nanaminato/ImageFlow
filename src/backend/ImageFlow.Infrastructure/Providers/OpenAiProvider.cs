using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace ImageFlow;

public class OpenAiProvider(HttpClient http, EngineOptions options) : IPromptPlanningProvider, IImageGenerationProvider
{
    async Task<(JsonDocument, string?)> Send(Uri endpoint, string path, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, path));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GetApiKey());
        req.Content = JsonContent.Create(body);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(http.Timeout);
        HttpResponseMessage response;
        try { response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, deadline.Token); } catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { throw new ProviderFailure("Connection interrupted; provider outcome is unknown.", unknown: true); }
        using (response)
        {
            var id = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
            if (!response.IsSuccessStatusCode)
            {
                // Only an explicit rate-limit rejection is automatically safe to retry. 5xx can have ambiguous side effects.
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
                throw new ProviderFailure($"OpenAI HTTP {(int)response.StatusCode}; request {id ?? "unavailable"}.", response.StatusCode == HttpStatusCode.TooManyRequests, delay, (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout, requestId: id);
            }
            try { return (JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token)), id); }
            catch (Exception e) when (e is JsonException or HttpRequestException or OperationCanceledException) { throw new ProviderFailure("Response interrupted or invalid; outcome unknown.", unknown: true, requestId: id); }
        }
    }
    public async Task<string[]> PlanAsync(string theme, int count, CancellationToken ct)
    {
        ModelCatalog.ValidatePlanner(options);
        var (doc, _) = await Send(options.GetBaseUri(), "responses", new { model = options.PromptModel, input = $"Create {count} distinct detailed image prompts about: {theme}. Return a JSON object with a prompts array of strings, no other content.", text = new { format = new { type = "json_schema", name = "prompt_plan", strict = true, schema = new { type = "object", properties = new { prompts = new { type = "array", items = new { type = "string" } } }, required = new[] { "prompts" }, additionalProperties = false } } } }, ct);
        using (doc)
        {
            var text = string.Concat(doc.RootElement.GetProperty("output").EnumerateArray().Where(x => x.TryGetProperty("content", out _)).SelectMany(x => x.GetProperty("content").EnumerateArray()).Where(x => x.GetProperty("type").GetString() == "output_text").Select(x => x.GetProperty("text").GetString()));
            using var parsed = JsonDocument.Parse(text);
            var prompts = parsed.RootElement.GetProperty("prompts").EnumerateArray().Select(x => x.GetString() ?? "").Distinct().ToArray();
            if (prompts.Length != count || prompts.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 8000)) throw new ProviderFailure("Planner returned invalid prompt count or content.");
            return prompts;
        }
    }
    public async Task<ImageResult> GenerateAsync(ImageGenerationRequest request, CancellationToken ct)
    {
        ModelCatalog.ValidateImage(options, request);
        var (doc, id) = await Send(new Uri(request.Endpoint), "images/generations", new {
            model = request.Model, prompt = request.Prompt, n = 1, size = request.Size,
            quality = request.Quality, complexity = request.Complexity, output_format = request.OutputFormat
        }, ct);
        using (doc)
        {
            var usage = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("usage", out var u) ? u.GetRawText() : null;
            try
            {
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() != 1) throw new FormatException();
                var bytes = Convert.FromBase64String(data[0].GetProperty("b64_json").GetString() ?? "");
                PngOutput.Validate(bytes);
                return new(bytes, "png", "image/png", usage, id);
            }
            catch (Exception e) when (e is KeyNotFoundException or FormatException or InvalidOperationException or ArgumentException)
            { throw new ProviderFailure("Image response is missing or is not a complete PNG; outcome unknown.", unknown: true, requestId: id, usageJson: usage); }
        }
    }
}
