using System.Net;
using System.Net.Http.Json;
using ImageFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ImageFlow.IntegrationTests;

public class BulkDeletionTests
{
    static Guid Id(string resource, Job job) => resource == "plans" ? job.PlanId : resource == "jobs" ? job.Id : job.Tasks[0].Id;
    static Task<HttpResponseMessage> Delete(HttpClient c, string resource, Guid[]? ids, bool confirmed = true) =>
        c.PostAsJsonAsync($"/api/v1/{resource}/bulk-delete", new { ids, confirmed });

    [Theory]
    [InlineData("plans")]
    [InlineData("jobs")]
    [InlineData("images")]
    public async Task BatchDeletesOnlySelectedItemsDeduplicatesIdsAndPreservesUnselectedData(string resource)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        var first = await ItemMaintenanceTests.Seed(app, c, "first", 1);
        var second = await ItemMaintenanceTests.Seed(app, c, "second", 1);
        var kept = await ItemMaintenanceTests.Seed(app, c, "kept", 1);
        var response = await Delete(c, resource, [Id(resource, first), Id(resource, second), Id(resource, first)]);
        response.EnsureSuccessStatusCode(); var result = (await response.Content.ReadFromJsonAsync<ItemDeletionResult>())!;
        Assert.Equal(2, result.Images); Assert.Equal(resource == "plans" ? 2 : 0, result.Plans);
        Assert.Equal(resource == "images" ? 0 : 2, result.Jobs); Assert.Equal(0, result.PendingImageFiles);
        Assert.False(File.Exists(first.Tasks[0].ImagePath)); Assert.False(File.Exists(second.Tasks[0].ImagePath));
        Assert.True(File.Exists(kept.Tasks[0].ImagePath));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
        Assert.Equal(resource == "plans" ? 1 : 3, await db.Plans.CountAsync());
        Assert.Equal(resource == "images" ? 3 : 1, await db.Jobs.CountAsync());
        Assert.Equal(resource == "images" ? 3 : 1, await db.Attempts.CountAsync());
        Assert.Equal(resource == "images" ? 3 : 1, await db.TaskReviews.CountAsync());
        if (resource == "images")
        {
            Assert.All(await db.Tasks.ToArrayAsync(), t => Assert.Equal("Completed", t.Status));
            Assert.All(await db.Jobs.ToArrayAsync(), j => Assert.Equal(.02m, j.EstimatedExposureUsd));
        }
    }

    [Theory]
    [InlineData("plans")]
    [InlineData("jobs")]
    [InlineData("images")]
    public async Task AnyRunningOrMissingItemRejectsTheWholeBatch(string resource)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        var first = await ItemMaintenanceTests.Seed(app, c, "first", 1);
        var second = await ItemMaintenanceTests.Seed(app, c, "second", 1);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(c, resource, [Id(resource, first), Guid.NewGuid()])).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            (await db.Tasks.SingleAsync(t => t.Id == second.Tasks[0].Id)).Status = "Running";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(c, resource, [Id(resource, first), Id(resource, second)])).StatusCode);
        Assert.True(File.Exists(first.Tasks[0].ImagePath)); Assert.True(File.Exists(second.Tasks[0].ImagePath));
        Assert.Equal(2, (await c.GetFromJsonAsync<Job[]>("/api/v1/jobs"))!.Length);
    }

    [Theory]
    [InlineData("plans")]
    [InlineData("jobs")]
    [InlineData("images")]
    public async Task BatchRequiresExplicitConfirmationAndBoundedValidIds(string resource)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(c, resource, [Guid.NewGuid()], false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(c, resource, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(c, resource, [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(c, resource, [Guid.Empty])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(c, resource, Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToArray())).StatusCode);
    }

    [Fact]
    public async Task DatabaseFailureRollsBackEveryItemAndItsFileCleanupWork()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient();
        var first = await ItemMaintenanceTests.Seed(app, c, "first", 1);
        var second = await ItemMaintenanceTests.Seed(app, c, "second", 1);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_batch BEFORE DELETE ON Jobs BEGIN SELECT RAISE(ABORT, 'test batch rejection'); END;");
        }
        Assert.Equal(HttpStatusCode.InternalServerError, (await Delete(c, "jobs", [first.Id, second.Id])).StatusCode);
        using var check = app.Services.CreateScope(); var saved = check.ServiceProvider.GetRequiredService<QueueDb>();
        Assert.Equal(2, await saved.Jobs.CountAsync()); Assert.Equal(2, await saved.Tasks.CountAsync());
        Assert.Equal(2, await saved.Attempts.CountAsync()); Assert.Equal(2, await saved.TaskReviews.CountAsync());
        Assert.Empty(await saved.PendingImageDeletions.ToArrayAsync());
        Assert.True(File.Exists(first.Tasks[0].ImagePath)); Assert.True(File.Exists(second.Tasks[0].ImagePath));
    }
}
