using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ImageFlow.IntegrationTests;

public class ExecutionProfileTests
{
    static Dictionary<string, string?> Configuration(string model = ModelCatalog.DefaultImageModel) => new()
    {
        ["Engine:Provider"] = "OpenAI", ["Engine:ProviderProfile"] = "official-test",
        ["Engine:CredentialEnvironmentVariable"] = "IMAGEFLOW_ISOLATED_TEST_KEY", ["Engine:ApiKey"] = "test-only",
        ["Engine:PromptModel"] = ModelCatalog.DefaultPromptModel, ["Engine:ImageModel"] = model,
        ["Engine:ImageSize"] = "1024x1024", ["Engine:ImageQuality"] = "medium",
        ["Engine:ImageComplexity"] = "high", ["Engine:OutputFormat"] = "png", ["Engine:MaxAttempts"] = "3"
    };

    sealed class InterceptedApi(string? imageResponse = null) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Images { get; } = new();
        public ConcurrentQueue<string> Plans { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // No socket is opened, even if the supplied URI is wrong.
            Assert.Equal("api.openai.com", request.RequestUri!.Host);
            Assert.Equal("test-only", request.Headers.Authorization!.Parameter);
            var body = await request.Content!.ReadAsStringAsync(ct);
            string response;
            if (request.RequestUri.AbsolutePath == "/v1/responses")
            {
                Plans.Enqueue(body);
                response = JsonSerializer.Serialize(new { output = new[] { new { content = new[] {
                    new { type = "output_text", text = "{\"prompts\":[\"stored seaside prompt\"]}" }
                } } } });
            }
            else
            {
                Assert.Equal("/v1/images/generations", request.RequestUri.AbsolutePath);
                Images.Enqueue(body); response = imageResponse ?? PngFixture.Response;
            }
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
            result.Headers.Add("x-request-id", "req-isolated-test"); return result;
        }
    }

    static async Task<Job> Enqueue(HttpClient client)
    {
        var planResponse = await client.PostAsJsonAsync("/api/v1/plans", new PlanRequest("seaside", 1));
        planResponse.EnsureSuccessStatusCode(); var plan = (await planResponse.Content.ReadFromJsonAsync<Plan>())!;
        var jobResponse = await client.PostAsJsonAsync("/api/v1/jobs", new JobRequest(plan.Id));
        jobResponse.EnsureSuccessStatusCode(); return (await jobResponse.Content.ReadFromJsonAsync<Job>())!;
    }
    static async Task<Job> Wait(HttpClient client, Guid id, string status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var job = (await client.GetFromJsonAsync<Job>($"/api/v1/jobs/{id}", timeout.Token))!;
            if (job.Status == status) return job;
            await Task.Delay(30, timeout.Token);
        }
    }

    [Fact]
    public async Task OfficialPlanToPngPersistsExactRequestUsageAndRequestId()
    {
        var handler = new InterceptedApi();
        await using var app = new AppFactory { Overrides = Configuration(), ApiHandler = handler };
        using var client = app.CreateClient();
        var job = await Enqueue(client); job = await Wait(client, job.Id, "Completed");
        var task = Assert.Single(job.Tasks); var attempt = Assert.Single(task.Attempts);
        Assert.Equal(ModelCatalog.DefaultImageModel, job.Model);
        Assert.Equal("medium", job.ImageQuality); Assert.Equal("high", job.ImageComplexity);
        Assert.Equal("https://api.openai.com/v1/", job.Endpoint); Assert.Equal(3, job.AttemptLimit);
        Assert.Equal("req-isolated-test", attempt.RequestId); Assert.Equal(job.Model, attempt.Model);
        Assert.Contains("42", attempt.UsageJson); Assert.Equal("Retained", attempt.CostDisposition);
        var response = await client.GetAsync($"/api/v1/images/{task.Id}"); response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        PngFixture.AssertDecodable(await response.Content.ReadAsByteArrayAsync());
        PngFixture.AssertDecodable(await File.ReadAllBytesAsync(task.ImagePath!));
        using var body = JsonDocument.Parse(Assert.Single(handler.Images));
        Assert.Equal(job.Model, body.RootElement.GetProperty("model").GetString());
        Assert.Equal("stored seaside prompt", body.RootElement.GetProperty("prompt").GetString());
        Assert.Equal(job.ImageSize, body.RootElement.GetProperty("size").GetString());
        Assert.Equal(job.ImageQuality, body.RootElement.GetProperty("quality").GetString());
        Assert.Equal(job.ImageComplexity, body.RootElement.GetProperty("complexity").GetString());
        Assert.Equal("png", body.RootElement.GetProperty("output_format").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("n").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
        using var planning = JsonDocument.Parse(Assert.Single(handler.Plans));
        Assert.Equal(ModelCatalog.DefaultPromptModel, planning.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", planning.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
        Assert.DoesNotContain("test-only", JsonSerializer.Serialize(await db.Jobs.AsNoTracking().ToListAsync()));
    }

    [Fact]
    public async Task RestartUsesSavedGenerationOptionsInsteadOfNewDefaultsAndRequiresNewEstimate()
    {
        string path; Guid id;
        await using (var first = new AppFactory { Worker = false, Overrides = Configuration(), ApiHandler = new InterceptedApi() })
        {
            using var client = first.CreateClient(); path = first.DataPath;
            (await client.PutAsJsonAsync("/api/v1/settings", new SettingsUpdate(1, 0.01m))).EnsureSuccessStatusCode();
            id = (await Enqueue(client)).Id;
        }
        var updated = Configuration("gpt-image-2.6-sunburst"); updated["Engine:ImageQuality"] = "high";
        updated["Engine:ImageSize"] = "1536x1024"; updated["Engine:ImageComplexity"] = "low"; updated["Engine:MaxAttempts"] = "1";
        var handler = new InterceptedApi();
        await using var second = new AppFactory { ExistingPath = path, Overrides = updated, ApiHandler = handler };
        using var c = second.CreateClient(); var job = await Wait(c, id, "Completed");
        using var request = JsonDocument.Parse(Assert.Single(handler.Images));
        Assert.Equal(ModelCatalog.DefaultImageModel, request.RootElement.GetProperty("model").GetString());
        Assert.Equal("medium", request.RootElement.GetProperty("quality").GetString());
        Assert.Equal("1024x1024", request.RootElement.GetProperty("size").GetString());
        Assert.Equal("high", request.RootElement.GetProperty("complexity").GetString());
        Assert.Equal(3, job.AttemptLimit); Assert.Equal(0.01m, job.EstimatedExposureUsd);
        Assert.False((await c.GetFromJsonAsync<SettingsView>("/api/v1/settings"))!.EstimateConfirmed);
        var planResponse = await c.PostAsJsonAsync("/api/v1/plans", new PlanRequest("new", 1)); planResponse.EnsureSuccessStatusCode();
        var plan = (await planResponse.Content.ReadFromJsonAsync<Plan>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(plan.Id))).StatusCode);
        (await c.PutAsJsonAsync("/api/v1/settings", new SettingsUpdate(1, 0.02m))).EnsureSuccessStatusCode();
        var created = await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(plan.Id)); created.EnsureSuccessStatusCode();
        var newJob = (await created.Content.ReadFromJsonAsync<Job>())!;
        Assert.Equal("gpt-image-2.6-sunburst", newJob.Model); Assert.Equal(0.02m, newJob.EstimatedCostPerImageUsd);
        await Wait(c, newJob.Id, "Completed");
    }

    [Theory]
    [InlineData("Engine:ProviderProfile", "changed-profile")]
    [InlineData("Engine:CredentialEnvironmentVariable", "CHANGED_CREDENTIAL_REFERENCE")]
    public async Task ProfileChangesBlockOldPendingJobsBeforeAnyRequest(string setting, string value)
    {
        string path; Guid id;
        await using (var first = new AppFactory { Worker = false, Overrides = Configuration(), ApiHandler = new InterceptedApi() })
        { using var c = first.CreateClient(); path = first.DataPath; id = (await Enqueue(c)).Id; }
        var updated = Configuration(); updated[setting] = value; var handler = new InterceptedApi();
        await using var second = new AppFactory { ExistingPath = path, Overrides = updated, ApiHandler = handler };
        using var client = second.CreateClient(); var stopped = await Wait(client, id, "NeedsAttention");
        Assert.Equal("ProviderConfigurationChanged", stopped.StopReason); Assert.Empty(handler.Images);
        Assert.Empty(stopped.Tasks.Single().Attempts);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/jobs/{id}/resume", null)).StatusCode);
    }

    [Theory]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":[{\"b64_json\":\"AQID\"}],\"usage\":{\"total_tokens\":99}}")]
    [InlineData("{\"data\":[{\"b64_json\":\"not base64\"}]}")]
    [InlineData("[]")]
    public async Task InvalidImageIsUnknownRetainsMetadataAndIsNeverResent(string response)
    {
        var handler = new InterceptedApi(response);
        await using var app = new AppFactory { Overrides = Configuration(), ApiHandler = handler };
        using var c = app.CreateClient();
        (await c.PutAsJsonAsync("/api/v1/settings", new SettingsUpdate(1, 0.01m))).EnsureSuccessStatusCode();
        var job = await Enqueue(c); job = await Wait(c, job.Id, "NeedsAttention");
        var task = Assert.Single(job.Tasks); var attempt = Assert.Single(task.Attempts);
        Assert.Equal("Unknown", task.Status); Assert.Null(task.ImagePath);
        Assert.Equal("req-isolated-test", attempt.RequestId); Assert.Equal("Uncertain", attempt.CostDisposition);
        Assert.Equal(0.01m, job.EstimatedExposureUsd); Assert.Single(handler.Images);
        if (response.Contains("99")) Assert.Contains("99", attempt.UsageJson);
        await Task.Delay(350); Assert.Single(handler.Images);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/v1/jobs/{job.Id}/resume", null)).StatusCode);
    }
}
