using System.Net;
using System.Net.Http.Json;
using ImageFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ImageFlow.IntegrationTests;

public class TaskReviewTests
{
    static async Task<(Guid task, Guid attempt, Guid job)> Seed(AppFactory app, HttpClient c)
    {
        var p = await c.PostAsJsonAsync("/api/v1/plans", new PlanRequest("Review test", 1)); p.EnsureSuccessStatusCode(); var plan = (await p.Content.ReadFromJsonAsync<Plan>())!;
        var r = await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(plan.Id)); r.EnsureSuccessStatusCode(); var job = (await r.Content.ReadFromJsonAsync<Job>())!;
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>(); var entity = await db.Jobs.Include(j => j.Tasks).SingleAsync(j => j.Id == job.Id);
        entity.Status = "NeedsAttention"; entity.StopReason = "UnknownOutcome"; entity.Mode = "Continuous"; entity.MaxImages = 1; entity.BudgetUsd = .01m; entity.EstimatedCostPerImageUsd = .01m; entity.EstimatedExposureUsd = .01m;
        var task = entity.Tasks.Single(); task.Status = "Unknown"; task.Error = "interrupted";
        var attempt = new Attempt { GenerationTaskId = task.Id, Outcome = "Unknown", CostDisposition = "Uncertain", EstimatedCostUsd = .01m, RequestId = "provider-reference", FinishedAt = DateTime.UtcNow }; db.Attempts.Add(attempt); await db.SaveChangesAsync(); return (task.Id, attempt.Id, job.Id);
    }
    static ReviewRequest Review(Guid attempt, string resolution = "NotGeneratedNotCharged", bool confirmed = true, string evidence = "Checked provider request and billing records: no image and no charge.") => new(attempt, resolution, evidence, confirmed);
    [Theory]
    [InlineData("Generated", "Completed", .01)]
    [InlineData("Abandon", "Cancelled", .01)]
    [InlineData("NotGeneratedNotCharged", "Paused", 0)]
    public async Task ReviewsPersistEvidenceAndNeverSendRequests(string resolution, string status, double expected)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var ids = await Seed(app, c);
        var r = await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(ids.attempt, resolution)); r.EnsureSuccessStatusCode();
        var task = (await c.GetFromJsonAsync<GenerationTask>($"/api/v1/tasks/{ids.task}"))!;
        Assert.Equal(status, task.Status); Assert.Single(task.Attempts); Assert.Equal("Unknown", task.Attempts[0].Outcome); Assert.Equal("provider-reference", task.Attempts[0].RequestId);
        var review = Assert.Single(task.Reviews); Assert.Equal(resolution, review.Resolution); Assert.NotEmpty(review.Evidence);
        var job = (await c.GetFromJsonAsync<Job>($"/api/v1/jobs/{ids.job}"))!; Assert.Equal("Paused", job.Status); Assert.Equal((decimal)expected, job.EstimatedExposureUsd); Assert.Single(job.Tasks[0].Reviews);
    }
    [Fact]
    public async Task CannotRetryUnknownDirectlyOrResumeBeforeReviewAndAuthorization()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var ids = await Seed(app, c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/retry", new RetryRequest(ids.attempt, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/v1/jobs/{ids.job}/resume", null)).StatusCode);
        (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(ids.attempt))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/v1/jobs/{ids.job}/resume", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/retry", new RetryRequest(ids.attempt, false))).StatusCode);
    }
    [Fact]
    public async Task AuthorizedRetryRemainsPausedSurvivesRestartAndReservesAgain()
    {
        string path; Guid taskId, jobId;
        await using (var app = new AppFactory { Worker = false })
        {
            path = app.DataPath; using var c = app.CreateClient(); var ids = await Seed(app, c); taskId = ids.task; jobId = ids.job;
            (await c.PostAsJsonAsync($"/api/v1/tasks/{taskId}/reviews", Review(ids.attempt))).EnsureSuccessStatusCode();
            (await c.PostAsJsonAsync($"/api/v1/tasks/{taskId}/retry", new RetryRequest(ids.attempt, true))).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/v1/tasks/{taskId}/retry", new RetryRequest(ids.attempt, true))).StatusCode);
            var job = (await c.GetFromJsonAsync<Job>($"/api/v1/jobs/{jobId}"))!; Assert.Equal("Paused", job.Status); Assert.Single(job.Tasks[0].Attempts);
        }
        await using var next = new AppFactory { ExistingPath = path }; using var client = next.CreateClient();
        var before = (await client.GetFromJsonAsync<GenerationTask>($"/api/v1/tasks/{taskId}"))!; Assert.Equal("Pending", before.Status); Assert.NotNull(before.Reviews.Single().RetryAuthorizedAt);
        (await client.PostAsync($"/api/v1/jobs/{jobId}/resume", null)).EnsureSuccessStatusCode();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); Job finished;
        do { await Task.Delay(50, timeout.Token); finished = (await client.GetFromJsonAsync<Job>($"/api/v1/jobs/{jobId}", timeout.Token))!; } while (finished.Status != "Completed");
        Assert.Equal(.01m, finished.EstimatedExposureUsd); Assert.Equal(2, finished.Tasks[0].Attempts.Count); Assert.Contains(finished.Tasks[0].Attempts, a => a.Outcome == "Unknown" && a.CostDisposition == "ManuallyReleased");
    }
    [Theory]
    [InlineData(false, "evidence")]
    [InlineData(true, "")]
    public async Task RequiresConfirmationAndEvidence(bool confirmed, string evidence)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var ids = await Seed(app, c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(ids.attempt, confirmed: confirmed, evidence: evidence))).StatusCode);
    }
    [Fact]
    public async Task StaleAndDuplicateReviewsCannotReleaseEstimateTwice()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var ids = await Seed(app, c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(Guid.NewGuid()))).StatusCode);
        var results = await Task.WhenAll(c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(ids.attempt)), c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(ids.attempt)));
        Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(0, (await c.GetFromJsonAsync<Job>($"/api/v1/jobs/{ids.job}"))!.EstimatedExposureUsd);
    }
    [Theory]
    [InlineData("Generated")]
    [InlineData("Abandon")]
    public async Task OtherConclusionsNeverAuthorizeRetry(string resolution)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var ids = await Seed(app, c);
        (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(ids.attempt, resolution))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/retry", new RetryRequest(ids.attempt, true))).StatusCode);
    }
    [Fact]
    public async Task RetryStillChecksBudgetAndCancellation()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var ids = await Seed(app, c);
        (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(ids.attempt))).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<QueueDb>(); var job = await db.Jobs.SingleAsync(); job.BudgetUsd = .005m; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/retry", new RetryRequest(ids.attempt, true))).StatusCode);
        (await c.PostAsync($"/api/v1/jobs/{ids.job}/cancel", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/retry", new RetryRequest(ids.attempt, true))).StatusCode);
    }
    [Theory]
    [InlineData("AttemptLimit")]
    [InlineData("ProviderChanged")]
    [InlineData("OtherUnknown")]
    public async Task RetryRejectsExhaustedOrUnsafeState(string reason)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var ids = await Seed(app, c);
        (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(ids.attempt))).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>(); var job = await db.Jobs.SingleAsync();
            if (reason == "ProviderChanged") job.Model = "different-model";
            if (reason == "OtherUnknown") db.Tasks.Add(new GenerationTask { JobId = job.Id, Status = "Unknown", PromptText = "unresolved sibling" });
            if (reason == "AttemptLimit") for (var i = 0; i < 2; i++) db.Attempts.Add(new Attempt { GenerationTaskId = ids.task, Outcome = "Failed", StartedAt = DateTime.UtcNow.AddDays(-1 - i) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/retry", new RetryRequest(ids.attempt, true))).StatusCode);
        Assert.Null((await c.GetFromJsonAsync<GenerationTask>($"/api/v1/tasks/{ids.task}"))!.Reviews.Single().RetryAuthorizedAt);
    }
    [Fact]
    public async Task ReviewWaitsForSiblingRequestsToSettle()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var ids = await Seed(app, c);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<QueueDb>(); db.Tasks.Add(new GenerationTask { JobId = ids.job, Status = "Running", PromptText = "in flight" }); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/v1/tasks/{ids.task}/reviews", Review(ids.attempt))).StatusCode);
        var task = (await c.GetFromJsonAsync<GenerationTask>($"/api/v1/tasks/{ids.task}"))!; Assert.Empty(task.Reviews); Assert.Equal("Unknown", task.Status);
    }
    [Fact]
    public async Task WorkerPreservesProviderRequestIdOnUnknownFailure()
    {
        var provider = new ProbeProvider { Failure = new ProviderFailure("ambiguous", unknown: true, requestId: "req-uncertain") };
        await using var app = new AppFactory { ImageProvider = provider }; using var c = app.CreateClient();
        var response = await c.PostAsJsonAsync("/api/v1/plans", new PlanRequest("request history", 1)); response.EnsureSuccessStatusCode(); var plan = (await response.Content.ReadFromJsonAsync<Plan>())!;
        var queued = await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(plan.Id)); queued.EnsureSuccessStatusCode(); var job = (await queued.Content.ReadFromJsonAsync<Job>())!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); GenerationTask task;
        do { await Task.Delay(50, timeout.Token); task = (await c.GetFromJsonAsync<GenerationTask>($"/api/v1/tasks/{job.Tasks[0].Id}", timeout.Token))!; } while (task.Status != "Unknown");
        Assert.Equal("req-uncertain", task.Attempts.Single().RequestId); Assert.Equal(1, provider.Calls);
    }

}
