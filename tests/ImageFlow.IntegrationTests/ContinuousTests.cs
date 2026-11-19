using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using ImageFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ImageFlow.IntegrationTests;

public class ContinuousTests
{
    static async Task<Plan> Plan(HttpClient client, int count = 1)
    {
        var response = await client.PostAsJsonAsync("/api/v1/plans", new PlanRequest("Bounded generation", count));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<Plan>())!;
    }
    static async Task<SettingsView> Settings(HttpClient client, int concurrency = 2, decimal price = 0.01m)
    {
        var response = await client.PutAsJsonAsync("/api/v1/settings", new SettingsUpdate(concurrency, price));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<SettingsView>())!;
    }
    static async Task<Job> Queue(HttpClient client, Guid plan, int max = 5, decimal budget = 1m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/jobs", new JobRequest(plan, "Continuous", max, budget));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<Job>())!;
    }
    static async Task<Job> Wait(HttpClient client, Guid id, Func<Job, bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { var job = (await client.GetFromJsonAsync<Job>($"/api/v1/jobs/{id}", timeout.Token))!; if (ready(job)) return job; await Task.Delay(30, timeout.Token); }
    }
    [Fact]
    public async Task SettingsSurviveRestartAndOverrideStartupDefaults()
    {
        string path;
        await using (var first = new AppFactory { Worker = false })
        {
            path = first.DataPath; using var client = first.CreateClient(); var saved = await Settings(client, 4, 0.015m); Assert.Equal(2, saved.Revision);
        }
        await using var second = new AppFactory { Worker = false, ExistingPath = path }; using var c = second.CreateClient();
        var restored = (await c.GetFromJsonAsync<SettingsView>("/api/v1/settings"))!;
        Assert.Equal(4, restored.MaxConcurrency); Assert.Equal(0.015m, restored.EstimatedCostPerImageUsd); Assert.Equal(2, restored.Revision);
    }
    [Theory]
    [InlineData(0, -1)]
    [InlineData(9, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 1001)]
    public async Task RejectsInvalidSettingsWithoutChangingPersistedValue(int concurrency, int price)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/v1/settings", new SettingsUpdate(concurrency, price))).StatusCode);
        Assert.Equal(2, (await c.GetFromJsonAsync<SettingsView>("/api/v1/settings"))!.MaxConcurrency);
    }
    [Fact]
    public async Task LiveConcurrencyDecreasesByDrainingAndIncreasesWithoutRestart()
    {
        var provider = new GatedProvider(); await using var app = new AppFactory { ImageProvider = provider }; using var c = app.CreateClient();
        var plan = await Plan(c, 6); var response = await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(plan.Id)); response.EnsureSuccessStatusCode(); var job = (await response.Content.ReadFromJsonAsync<Job>())!;
        var first = await provider.Next(); var second = await provider.Next();
        await Settings(c, 1); first.SetResult(); await Task.Delay(350); Assert.Equal(2, provider.Calls);
        second.SetResult(); var third = await provider.Next(); Assert.Equal(1, provider.Active);
        await Settings(c, 3); var fourth = await provider.Next(); var fifth = await provider.Next(); Assert.Equal(3, provider.Active);
        third.SetResult(); fourth.SetResult(); fifth.SetResult(); var sixth = await provider.Next(); sixth.SetResult();
        await Wait(c, job.Id, j => j.Status == "Completed"); Assert.Equal(6, provider.Calls);
    }
    [Fact]
    public async Task ContinuousHonorsQuantityLimitAndCyclesStoredPrompts()
    {
        await using var app = new AppFactory(); using var c = app.CreateClient(); await Settings(c, 3);
        var p = await Plan(c, 2); var job = await Queue(c, p.Id, 5); job = await Wait(c, job.Id, j => j.Status == "Completed");
        Assert.Equal("QuantityLimit", job.StopReason); Assert.Equal(5, job.Tasks.Count); Assert.Equal(0.05m, job.EstimatedExposureUsd);
        Assert.All(job.Tasks, t => { Assert.Equal("Completed", t.Status); Assert.Contains(p.Prompts, x => x.Id == t.PromptId && x.Text == t.PromptText); Assert.Single(t.Attempts); });
        var again = await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(p.Id, "Continuous", 5, 1)); Assert.Equal(job.Id, (await again.Content.ReadFromJsonAsync<Job>())!.Id);
    }
    [Fact]
    public async Task ConcurrentReservationsCannotOvershootBudget()
    {
        var provider = new ProbeProvider(); await using var app = new AppFactory { ImageProvider = provider }; using var c = app.CreateClient(); await Settings(c, 8);
        var p = await Plan(c); var job = await Queue(c, p.Id, 100, 0.025m); job = await Wait(c, job.Id, j => j.Status == "Completed");
        Assert.Equal("BudgetLimit", job.StopReason); Assert.Equal(2, provider.Calls); Assert.Equal(2, job.Tasks.Count); Assert.Equal(0.02m, job.EstimatedExposureUsd);
        Assert.True(job.EstimatedExposureUsd <= job.BudgetUsd); Assert.All(job.Tasks, t => Assert.Equal("Retained", t.Attempts.Single().CostDisposition));
    }
    [Fact]
    public async Task BudgetSmallerThanOneReservationSendsNothing()
    {
        var provider = new ProbeProvider(); await using var app = new AppFactory { ImageProvider = provider }; using var c = app.CreateClient(); await Settings(c);
        var p = await Plan(c); var job = await Queue(c, p.Id, 5, 0.005m); job = await Wait(c, job.Id, j => j.Status == "Completed");
        Assert.Equal("BudgetLimit", job.StopReason); Assert.Empty(job.Tasks); Assert.Equal(0, provider.Calls);
    }
    [Fact]
    public async Task UnknownRetainsExposureStopsContinuousAndSurvivesRestart()
    {
        var provider = new ProbeProvider { Failure = new ProviderFailure("ambiguous", unknown: true) }; string path; Guid id;
        await using (var first = new AppFactory { ImageProvider = provider })
        {
            path = first.DataPath; using var c = first.CreateClient(); await Settings(c, 1); var p = await Plan(c); var job = await Queue(c, p.Id); id = job.Id;
            job = await Wait(c, id, j => j.Status == "NeedsAttention"); Assert.Equal("UnknownOutcome", job.StopReason); Assert.Equal(0.01m, job.EstimatedExposureUsd);
            Assert.Equal("Uncertain", job.Tasks.Single().Attempts.Single().CostDisposition); Assert.Equal(1, provider.Calls);
        }
        await using var second = new AppFactory { ExistingPath = path }; using var client = second.CreateClient(); var restored = (await client.GetFromJsonAsync<Job>($"/api/v1/jobs/{id}"))!;
        Assert.Equal("NeedsAttention", restored.Status); Assert.Equal(0.01m, restored.EstimatedExposureUsd); Assert.Single(restored.Tasks);
    }
    [Fact]
    public async Task RestartRetainsPriorSpendAndContinuesOnlyWithinRemainingBudget()
    {
        string path; Guid id;
        await using (var first = new AppFactory { Worker = false })
        {
            path = first.DataPath; using var c = first.CreateClient(); await Settings(c, 1); var p = await Plan(c); var job = await Queue(c, p.Id, 5, 0.02m); id = job.Id;
            using var scope = first.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>(); var entity = await db.Jobs.SingleAsync();
            entity.EstimatedExposureUsd = 0.01m; db.Tasks.Add(new GenerationTask { JobId = id, PromptId = p.Prompts[0].Id, PromptText = p.Prompts[0].Text, Status = "Completed" }); await db.SaveChangesAsync();
        }
        await using var second = new AppFactory { ExistingPath = path }; using var client = second.CreateClient(); var complete = await Wait(client, id, j => j.Status == "Completed");
        Assert.Equal("BudgetLimit", complete.StopReason); Assert.Equal(2, complete.Tasks.Count); Assert.Equal(0.02m, complete.EstimatedExposureUsd);
    }
    [Fact]
    public async Task PauseAndCancelDoNotDispatchReplacementWork()
    {
        var provider = new GatedProvider(); await using var app = new AppFactory { ImageProvider = provider }; using var c = app.CreateClient(); await Settings(c, 1);
        var p = await Plan(c); var job = await Queue(c, p.Id); var first = await provider.Next();
        (await c.PostAsync($"/api/v1/jobs/{job.Id}/pause", null)).EnsureSuccessStatusCode(); first.SetResult(); await Wait(c, job.Id, j => j.Tasks[0].Status == "Completed"); Assert.Equal(1, provider.Calls);
        (await c.PostAsync($"/api/v1/jobs/{job.Id}/resume", null)).EnsureSuccessStatusCode(); var second = await provider.Next();
        (await c.PostAsync($"/api/v1/jobs/{job.Id}/cancel", null)).EnsureSuccessStatusCode(); second.SetResult(); job = await Wait(c, job.Id, j => j.Tasks.All(t => t.Status == "Completed"));
        Assert.Equal("Cancelled", job.Status); Assert.Equal(2, provider.Calls); Assert.Equal(0.02m, job.EstimatedExposureUsd);
    }
    [Fact]
    public async Task ExplicitRateLimitReleasesReservationAndRetryReservesAgain()
    {
        var provider = new RejectOnceProvider(); await using var app = new AppFactory { ImageProvider = provider }; using var c = app.CreateClient(); await Settings(c, 1);
        var p = await Plan(c); var job = await Queue(c, p.Id, 1, 0.01m); job = await Wait(c, job.Id, j => j.Tasks.Any(t => t.Status == "RetryWaiting"));
        Assert.Equal(0, job.EstimatedExposureUsd); Assert.Equal("Released", job.Tasks[0].Attempts[0].CostDisposition);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<QueueDb>(); var task = await db.Tasks.SingleAsync(); task.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); }
        job = await Wait(c, job.Id, j => j.Status == "Completed"); Assert.Equal(0.01m, job.EstimatedExposureUsd); Assert.Equal(2, job.Tasks[0].Attempts.Count);
    }
    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(1001, 1)]
    [InlineData(2, 0)]
    public async Task ContinuousRequiresBothSafetyLimits(int? count, int budget)
    {
        await using var app = new AppFactory(); using var c = app.CreateClient(); await Settings(c); var p = await Plan(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(p.Id, "Continuous", count, budget))).StatusCode);
    }
    [Fact]
    public async Task ContinuousCannotUseZeroOrMissingPriceEstimate()
    {
        await using var app = new AppFactory(); using var c = app.CreateClient(); var p = await Plan(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(p.Id, "Continuous", 5, 1))).StatusCode);
    }
    [Fact]
    public async Task FiniteBudgetCancelsUnsentTasksWithoutOverspending()
    {
        await using var app = new AppFactory(); using var c = app.CreateClient(); await Settings(c, 8); var p = await Plan(c, 5);
        var r = await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(p.Id, BudgetUsd: 0.025m)); r.EnsureSuccessStatusCode(); var job = (await r.Content.ReadFromJsonAsync<Job>())!;
        job = await Wait(c, job.Id, j => j.Status == "Completed"); Assert.Equal("BudgetLimit", job.StopReason); Assert.Equal(0.02m, job.EstimatedExposureUsd);
        Assert.Equal(2, job.Tasks.Count(t => t.Status == "Completed")); Assert.Equal(3, job.Tasks.Count(t => t.Status == "Cancelled"));
    }
    [Fact]
    public async Task EditingPriceDoesNotRepriceExistingJobs()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); await Settings(c); var p = await Plan(c); var job = await Queue(c, p.Id);
        await Settings(c, 4, 0.99m); var saved = (await c.GetFromJsonAsync<Job>($"/api/v1/jobs/{job.Id}"))!;
        Assert.Equal(0.01m, saved.EstimatedCostPerImageUsd); Assert.Equal(0, saved.EstimatedExposureUsd);
    }
    [Fact]
    public async Task RecoveryOfRunningContinuousTaskRetainsReservationAndStops()
    {
        string path; Guid id;
        await using (var first = new AppFactory { Worker = false })
        {
            path = first.DataPath; using var c = first.CreateClient(); await Settings(c, 1); var p = await Plan(c); var job = await Queue(c, p.Id); id = job.Id;
            using var scope = first.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>(); var entity = await db.Jobs.SingleAsync(); entity.EstimatedExposureUsd = 0.01m;
            var task = new GenerationTask { JobId = id, PromptId = p.Prompts[0].Id, PromptText = p.Prompts[0].Text, Status = "Running" }; db.Tasks.Add(task); db.Attempts.Add(new Attempt { GenerationTaskId = task.Id, EstimatedCostUsd = 0.01m }); await db.SaveChangesAsync();
        }
        var provider = new ProbeProvider(); await using var second = new AppFactory { ExistingPath = path, ImageProvider = provider }; using var client = second.CreateClient(); var recovered = (await client.GetFromJsonAsync<Job>($"/api/v1/jobs/{id}"))!;
        Assert.Equal("NeedsAttention", recovered.Status); Assert.Equal("UnknownOutcome", recovered.StopReason); Assert.Equal(0.01m, recovered.EstimatedExposureUsd); Assert.Equal("Uncertain", recovered.Tasks.Single().Attempts.Single().CostDisposition); Assert.Equal(0, provider.Calls);
    }
    [Fact]
    public async Task MigratingExistingDatabasePreservesFiniteHistoryAsUnpriced()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), ".cache", "upgrade", Guid.NewGuid().ToString()); Directory.CreateDirectory(path);
        var options = new DbContextOptionsBuilder<QueueDb>().UseSqlite($"Data Source={Path.Combine(path, "imageflow.db")}").Options;
        using (var db = new QueueDb(options))
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
            await migrator.MigrateAsync("20261119141017_UniquePlanJob");
            var plan = Guid.NewGuid(); var job = Guid.NewGuid(); var task = Guid.NewGuid(); var prompt = Guid.NewGuid(); var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Plans (Id,Theme,CreatedAt) VALUES ({plan},'legacy',{now})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Jobs (Id,PlanId,Status,CreatedAt) VALUES ({job},{plan},'Completed',{now})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Tasks (Id,JobId,PromptId,PromptText,Status) VALUES ({task},{job},{prompt},'legacy','Completed')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Attempts (Id,GenerationTaskId,StartedAt,Outcome,Provider,Model) VALUES ({Guid.NewGuid()},{task},{now},'Completed','Mock','mock-svg')");
        }
        await using var app = new AppFactory { ExistingPath = path, Worker = false }; using var client = app.CreateClient(); var history = (await client.GetFromJsonAsync<Job[]>("/api/v1/jobs"))!;
        var migrated = Assert.Single(history); Assert.Equal("Finite", migrated.Mode); Assert.Equal(1, migrated.MaxImages); Assert.Equal(0, migrated.EstimatedExposureUsd); Assert.Equal("Unpriced", migrated.Tasks.Single().Attempts.Single().CostDisposition);
    }

}
public sealed class GatedProvider : IImageGenerationProvider
{
    readonly Channel<TaskCompletionSource> calls = Channel.CreateUnbounded<TaskCompletionSource>(); int count, active;
    public int Calls => Volatile.Read(ref count); public int Active => Volatile.Read(ref active);
    public async Task<TaskCompletionSource> Next() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); return await calls.Reader.ReadAsync(timeout.Token); }
    public async Task<ImageResult> GenerateAsync(string prompt, CancellationToken ct)
    {
        Interlocked.Increment(ref count); Interlocked.Increment(ref active); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); await calls.Writer.WriteAsync(completion, ct);
        try { await completion.Task.WaitAsync(ct); return new ImageResult([1, 2, 3], "png", "image/png", null, null); } finally { Interlocked.Decrement(ref active); }
    }
}
public sealed class RejectOnceProvider : IImageGenerationProvider
{
    int count;
    public Task<ImageResult> GenerateAsync(string prompt, CancellationToken ct) { if (Interlocked.Increment(ref count) == 1) throw new ProviderFailure("429", true, TimeSpan.FromSeconds(30)); return new MockProvider().GenerateAsync(prompt, ct); }
}
