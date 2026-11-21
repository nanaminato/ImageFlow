using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using ImageFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ImageFlow.IntegrationTests;

public class ItemMaintenanceTests
{
    internal static async Task<Job> Seed(AppFactory app, HttpClient c, string theme, int count = 2)
    {
        var p = await c.PostAsJsonAsync("/api/v1/plans", new PlanRequest(theme, count)); p.EnsureSuccessStatusCode();
        var plan = (await p.Content.ReadFromJsonAsync<Plan>())!;
        var queued = await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(plan.Id)); queued.EnsureSuccessStatusCode();
        var job = (await queued.Content.ReadFromJsonAsync<Job>())!;
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
        var stored = await db.Jobs.Include(j => j.Tasks).SingleAsync(j => j.Id == job.Id);
        stored.Status = "Completed"; stored.EstimatedExposureUsd = .02m;
        foreach (var task in stored.Tasks)
        {
            task.Status = "Completed"; task.ImagePath = Path.Combine(app.DataPath, "images", task.Id + ".svg");
            await File.WriteAllTextAsync(task.ImagePath, $"<svg>{theme}-{task.Id}</svg>");
            var attempt = new Attempt { GenerationTaskId = task.Id, Outcome = "Completed", CostDisposition = "Retained", EstimatedCostUsd = .01m, FinishedAt = DateTime.UtcNow };
            db.Attempts.Add(attempt);
            db.TaskReviews.Add(new TaskReview { GenerationTaskId = task.Id, AttemptId = attempt.Id, Resolution = "Generated", Evidence = "Saved fixture output" });
        }
        await db.SaveChangesAsync();
        return (await c.GetFromJsonAsync<Job>($"/api/v1/jobs/{job.Id}"))!;
    }
    static Task<HttpResponseMessage> Delete(HttpClient c, string resource, Guid id, bool confirmed = true) =>
        c.DeleteAsync($"/api/v1/{resource}/{id}?confirmed={confirmed.ToString().ToLowerInvariant()}");

    [Theory]
    [InlineData("plans")]
    [InlineData("jobs")]
    public async Task DeletingOnePlanOrJobRemovesOnlyItsDependentRecordsAndImages(string resource)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        var first = await Seed(app, c, "first"); var other = await Seed(app, c, "other");
        var id = resource == "plans" ? first.PlanId : first.Id;
        var response = await Delete(c, resource, id); response.EnsureSuccessStatusCode();
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<ItemDeletionResult>())!.PendingImageFiles);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/jobs/{first.Id}")).StatusCode);
        var kept = (await c.GetFromJsonAsync<Job>($"/api/v1/jobs/{other.Id}"))!;
        Assert.Equal(2, kept.Tasks.Count); Assert.All(kept.Tasks, t => { Assert.True(File.Exists(t.ImagePath)); Assert.Single(t.Attempts); Assert.Single(t.Reviews); });
        Assert.All(first.Tasks, t => Assert.False(File.Exists(t.ImagePath)));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
        Assert.Equal(2, await db.Tasks.CountAsync()); Assert.Equal(2, await db.Attempts.CountAsync()); Assert.Equal(2, await db.TaskReviews.CountAsync());
        Assert.Equal(resource == "plans" ? 1 : 2, await db.Plans.CountAsync());
        Assert.Equal(1, await db.RuntimeSettings.CountAsync());
        if (resource == "jobs")
        {
            // New generation remains an explicit, separate enqueue command.
            var requeue = await c.PostAsJsonAsync("/api/v1/jobs", new JobRequest(first.PlanId)); requeue.EnsureSuccessStatusCode();
            Assert.NotEqual(first.Id, (await requeue.Content.ReadFromJsonAsync<Job>())!.Id);
        }
    }

    [Fact]
    public async Task DeletingAnImageRetainsTaskUsageReviewsAndBudgetAndDoesNotRegenerate()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var job = await Seed(app, c, "image");
        var task = job.Tasks[0]; (await Delete(c, "images", task.Id)).EnsureSuccessStatusCode();
        var saved = (await c.GetFromJsonAsync<Job>($"/api/v1/jobs/{job.Id}"))!;
        var removed = saved.Tasks.Single(t => t.Id == task.Id);
        Assert.Null(removed.ImagePath); Assert.Equal("Completed", removed.Status); Assert.Single(removed.Attempts); Assert.Single(removed.Reviews);
        Assert.Equal(job.EstimatedExposureUsd, saved.EstimatedExposureUsd); Assert.Equal(job.MaxImages, saved.MaxImages);
        Assert.False(File.Exists(task.ImagePath)); Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/images/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(c, "images", task.Id)).StatusCode);
        Assert.Single(saved.Tasks, t => t.ImagePath != null);
    }

    [Theory]
    [InlineData("plans")]
    [InlineData("jobs")]
    [InlineData("images")]
    public async Task UnconfirmedOrRunningDeletionIsRejectedWithoutChangingOtherData(string resource)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var job = await Seed(app, c, "safe");
        var id = resource == "plans" ? job.PlanId : resource == "jobs" ? job.Id : job.Tasks[0].Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(c, resource, id, false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync($"/api/v1/{resource}/{id}")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            (await db.Tasks.SingleAsync(t => t.Id == job.Tasks[0].Id)).Status = "Running";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(c, resource, id)).StatusCode);
        Assert.All(job.Tasks, t => Assert.True(File.Exists(t.ImagePath)));
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(c, resource, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task DraftPlanCanBeDeletedIndependently()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        var p = await c.PostAsJsonAsync("/api/v1/plans", new PlanRequest("draft", 2)); p.EnsureSuccessStatusCode();
        var plan = (await p.Content.ReadFromJsonAsync<Plan>())!;
        (await Delete(c, "plans", plan.Id)).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
        Assert.Empty(await db.Plans.ToArrayAsync()); Assert.Empty(await db.Prompts.ToArrayAsync());
    }

    [Fact]
    public async Task LockedImageCleanupIsDurableAcrossRestart()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path; string image;
        await using (var app = new AppFactory { Worker = false })
        {
            path = app.DataPath; using var c = app.CreateClient(); var job = await Seed(app, c, "locked", 1);
            image = job.Tasks[0].ImagePath!;
            using var locked = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.None);
            var response = await Delete(c, "jobs", job.Id); response.EnsureSuccessStatusCode();
            Assert.Equal(1, (await response.Content.ReadFromJsonAsync<ItemDeletionResult>())!.PendingImageFiles);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/jobs/{job.Id}")).StatusCode);
        }
        await using var restarted = new AppFactory { Worker = false, ExistingPath = path }; using var client = restarted.CreateClient();
        using var scope = restarted.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
        Assert.Single(await db.PendingImageDeletions.ToArrayAsync());
        await scope.ServiceProvider.GetRequiredService<IItemMaintenance>().Cleanup(CancellationToken.None);
        Assert.False(File.Exists(image)); Assert.Empty(await db.PendingImageDeletions.ToArrayAsync());
    }

    [Fact]
    public async Task UnsafePathRejectsDeletionAndDownloadWithoutTouchingExternalFiles()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var job = await Seed(app, c, "unsafe", 1);
        var outside = Path.Combine(app.DataPath, "outside.svg"); await File.WriteAllTextAsync(outside, "keep");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>(); (await db.Tasks.SingleAsync()).ImagePath = outside; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(c, "plans", job.PlanId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync($"/api/v1/jobs/{job.Id}/images.zip")).StatusCode);
        Assert.Equal("keep", await File.ReadAllTextAsync(outside));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/v1/plans/{job.PlanId}")).StatusCode);
    }

    [Fact]
    public async Task SharedOutputCannotBeRemovedFromAnotherCollection()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        var first = await Seed(app, c, "first", 1); var second = await Seed(app, c, "second", 1);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            (await db.Tasks.SingleAsync(t => t.Id == second.Tasks[0].Id)).ImagePath = first.Tasks[0].ImagePath;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(c, "jobs", first.Id)).StatusCode);
        Assert.True(File.Exists(first.Tasks[0].ImagePath));
        Assert.Equal(2, (await c.GetFromJsonAsync<Job[]>("/api/v1/jobs"))!.Length);
    }

    [Fact]
    public async Task FailedDatabaseDeletionRollsBackCleanupWorkAndLeavesImagesIntact()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var job = await Seed(app, c, "rollback", 1);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_attempt_deletion BEFORE DELETE ON Attempts BEGIN SELECT RAISE(ABORT, 'test rejection'); END;");
        }
        Assert.Equal(HttpStatusCode.InternalServerError, (await Delete(c, "jobs", job.Id)).StatusCode);
        Assert.True(File.Exists(job.Tasks[0].ImagePath));
        using var check = app.Services.CreateScope(); var saved = check.ServiceProvider.GetRequiredService<QueueDb>();
        Assert.Single(await saved.Jobs.ToArrayAsync()); Assert.Single(await saved.TaskReviews.ToArrayAsync());
        Assert.Single(await saved.Attempts.ToArrayAsync()); Assert.Empty(await saved.PendingImageDeletions.ToArrayAsync());
    }

    [Fact]
    public async Task CollectionZipContainsExactlyItsSavedImagesAndNoOtherCollection()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        var job = await Seed(app, c, "chosen"); var other = await Seed(app, c, "other");
        var response = await c.GetAsync($"/api/v1/jobs/{job.Id}/images.zip"); response.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains(job.Id.ToString(), response.Content.Headers.ContentDisposition!.ToString());
        using (var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync())))
        {
            Assert.Equal(2, zip.Entries.Count);
            foreach (var entry in zip.Entries)
            {
                Assert.Equal(entry.Name, entry.FullName);
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var text = await reader.ReadToEndAsync(); Assert.Contains("chosen", text); Assert.DoesNotContain("other", text);
            }
        }
        Assert.Empty(Directory.GetFiles(Path.Combine(app.DataPath, "exports")));
        (await Delete(c, "images", job.Tasks[0].Id)).EnsureSuccessStatusCode();
        var next = await c.GetAsync($"/api/v1/jobs/{job.Id}/images.zip"); next.EnsureSuccessStatusCode();
        using var remaining = new ZipArchive(new MemoryStream(await next.Content.ReadAsByteArrayAsync())); Assert.Single(remaining.Entries);
        Assert.All(other.Tasks, t => Assert.True(File.Exists(t.ImagePath)));
    }

    [Fact]
    public async Task EmptyOrMissingCollectionAndMissingFilesDoNotProduceMisleadingArchives()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); var job = await Seed(app, c, "missing", 1);
        File.Delete(job.Tasks[0].ImagePath!);
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync($"/api/v1/jobs/{job.Id}/images.zip")).StatusCode);
        (await Delete(c, "images", job.Tasks[0].Id)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/jobs/{job.Id}/images.zip")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/jobs/{Guid.NewGuid()}/images.zip")).StatusCode);
    }
}
