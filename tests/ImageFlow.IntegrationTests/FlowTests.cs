using System.Net;
using System.Net.Http.Json;
using ImageFlow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace ImageFlow.IntegrationTests;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    public string DataPath { get; } = Path.Combine(Directory.GetCurrentDirectory(), ".cache", "tests", Guid.NewGuid().ToString());
    public bool Worker { get; init; } = true; public IImageGenerationProvider? ImageProvider { get; init; }
    public string? ExistingPath { get; init; }
    public Dictionary<string, string?> Overrides { get; init; } = new();
    public HttpMessageHandler? ApiHandler { get; init; }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DataDirectory"] = ExistingPath ?? DataPath,
            ["Engine:Provider"] = "Mock",
            ["Engine:WorkerEnabled"] = Worker.ToString(),
            ["Engine:MaxConcurrency"] = "2"
        }).AddInMemoryCollection(Overrides));
        builder.ConfigureServices(s => {
            if (ImageProvider != null) s.AddSingleton(ImageProvider);
            if (ApiHandler != null) s.AddHttpClient<OpenAiProvider>().ConfigurePrimaryHttpMessageHandler(() => ApiHandler);
        });
    }
}
public class FlowTests
{
    static async Task<Plan> Plan(HttpClient c, int count = 3)
    {
        var r = await c.PostAsJsonAsync("/api/v1/plans", new PlanRequest("Coastal watercolor", count)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<Plan>())!;
    }
    static async Task<Job> Queue(HttpClient c, Guid id)
    {
        var r = await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(id)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<Job>())!;
    }
    static async Task<Job> Wait(HttpClient c, Guid id, Func<Job, bool> ready)
    {
        for (int i = 0; i < 150; i++) { var j = (await c.GetFromJsonAsync<Job>($"/api/v1/jobs/{id}"))!; if (ready(j)) return j; await Task.Delay(50); }
        throw new TimeoutException("Job did not reach expected state.");
    }
    [Fact]
    public async Task PersistedPlan_Edit_Queue_Gallery_Usage()
    {
        await using var app = new AppFactory(); using var c = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);
        var publicSettings = await c.GetStringAsync("/api/v1/settings");
        Assert.DoesNotContain("apiKey", publicSettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rk_", publicSettings);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/openapi/v1.json")).StatusCode);
        var plan = await Plan(c); var prompt = plan.Prompts[0];
        (await c.PutAsJsonAsync($"/api/v1/prompts/{prompt.Id}", new EditPrompt("Edited coastal painting"))).EnsureSuccessStatusCode();
        var saved = (await c.GetFromJsonAsync<Plan>($"/api/v1/plans/{plan.Id}"))!;
        Assert.Contains(saved.Prompts, p => p.Text == "Edited coastal painting");
        var job = await Queue(c, plan.Id); job = await Wait(c, job.Id, j => j.Status == "Completed");
        Assert.All(job.Tasks, t => { Assert.Equal("Completed", t.Status); Assert.Single(t.Attempts); });
        Assert.Contains(job.Tasks, t => t.PromptText == "Edited coastal painting");
        var image = await c.GetAsync($"/api/v1/images/{job.Tasks[0].Id}");
        Assert.Equal("image/svg+xml", image.Content.Headers.ContentType!.MediaType);
        Assert.Contains("MOCK preview", await image.Content.ReadAsStringAsync());
        Assert.Equal(3, (await c.GetFromJsonAsync<Attempt[]>("/api/v1/usage"))!.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/v1/prompts/{prompt.Id}", new EditPrompt("No mutation"))).StatusCode);
    }
    [Fact]
    public async Task EnqueueIsIdempotent_AndControlsPersist()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        var p = await Plan(c); var j = await Queue(c, p.Id); Assert.Equal(j.Id, (await Queue(c, p.Id)).Id);
        var paused = await c.PostAsync($"/api/v1/jobs/{j.Id}/pause", null); paused.EnsureSuccessStatusCode();
        Assert.Equal("Paused", (await paused.Content.ReadFromJsonAsync<Job>())!.Status);
        (await c.PostAsync($"/api/v1/jobs/{j.Id}/resume", null)).EnsureSuccessStatusCode();
        var cancelled = await c.PostAsync($"/api/v1/jobs/{j.Id}/cancel", null); cancelled.EnsureSuccessStatusCode();
        Assert.All((await cancelled.Content.ReadFromJsonAsync<Job>())!.Tasks, t => Assert.Equal("Cancelled", t.Status));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/v1/jobs/{j.Id}/resume", null)).StatusCode);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task RejectsUnboundedCounts(int count)
    {
        await using var app = new AppFactory(); using var c = app.CreateClient(); Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/plans", new PlanRequest("theme", count))).StatusCode);
    }
    [Fact]
    public async Task RestartPreservesPendingAndMarksRunningUnknown()
    {
        string path; Guid jobId;
        await using (var first = new AppFactory { Worker = false })
        {
            path = first.DataPath; using var c = first.CreateClient(); var p = await Plan(c, 2); var j = await Queue(c, p.Id); jobId = j.Id;
            using var scope = first.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            var t = await db.Tasks.FirstAsync(); t.Status = "Running"; db.Attempts.Add(new Attempt { GenerationTaskId = t.Id }); await db.SaveChangesAsync();
        }
        await using (var second = new AppFactory { ExistingPath = path })
        {
            using var c = second.CreateClient(); var j = await Wait(c, jobId, j => j.Status == "NeedsAttention");
            Assert.Single(j.Tasks, t => t.Status == "Unknown"); Assert.Single(j.Tasks, t => t.Status == "Completed");
            var unknown = j.Tasks.Single(t => t.Status == "Unknown"); Assert.Single(unknown.Attempts); Assert.Equal("Unknown", unknown.Attempts[0].Outcome);
        }
    }
    [Fact]
    public async Task OutstandingRequestsAreBounded()
    {
        var provider = new ProbeProvider(); await using var app = new AppFactory { ImageProvider = provider }; using var c = app.CreateClient();
        var p = await Plan(c, 8); var j = await Queue(c, p.Id); await Wait(c, j.Id, j => j.Status == "Completed");
        Assert.Equal(2, provider.MaxObserved); Assert.Equal(8, provider.Calls);
    }
    [Fact]
    public async Task UnknownOutcomeIsNeverAutomaticallyRetried()
    {
        var provider = new ProbeProvider { Failure = new ProviderFailure("interrupted", unknown: true) };
        await using var app = new AppFactory { ImageProvider = provider }; using var c = app.CreateClient(); var p = await Plan(c, 1); var j = await Queue(c, p.Id);
        j = await Wait(c, j.Id, j => j.Status == "NeedsAttention"); Assert.Equal("Unknown", j.Tasks[0].Status); Assert.Equal(1, provider.Calls);
    }
    [Fact]
    public async Task RateLimitPersistsRetryAfterBeforeRetrying()
    {
        var provider = new ProbeProvider { Failure = new ProviderFailure("rate limited", true, TimeSpan.FromSeconds(30)) };
        await using var app = new AppFactory { ImageProvider = provider }; using var c = app.CreateClient(); var p = await Plan(c, 1); var j = await Queue(c, p.Id);
        j = await Wait(c, j.Id, j => j.Tasks[0].Status == "RetryWaiting"); Assert.True(j.Tasks[0].NextAttemptAt > DateTime.UtcNow.AddSeconds(20)); Assert.Equal(1, provider.Calls);
    }
    [Fact]
    public async Task SsePublishesJobSnapshots()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var response = await c.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token)); Assert.Equal("event: jobs", await reader.ReadLineAsync(timeout.Token));
    }
    [Fact]
    public void OpenAiCapabilitiesAndLimitsAreValidated()
    {
        Assert.Throws<InvalidOperationException>(() => new EngineOptions { MaxConcurrency = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new EngineOptions { Provider = "OpenAI", ImageModel = "unverified" }.Validate());
    }
}
public class ProbeProvider : IImageGenerationProvider
{
    int active, calls, max;
    public int Calls => calls; public int MaxObserved => max; public ProviderFailure? Failure { get; init; }
    public async Task<ImageResult> GenerateAsync(ImageGenerationRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref calls); var current = Interlocked.Increment(ref active); int old;
        do { old = max; if (old >= current) break; } while (Interlocked.CompareExchange(ref max, current, old) != old);
        try { await Task.Delay(250, ct); if (Failure != null) throw Failure; return await new MockProvider().GenerateAsync(request, ct); } finally { Interlocked.Decrement(ref active); }
    }
}
