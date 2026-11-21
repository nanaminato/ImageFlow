using System.Net;
using System.Net.Http.Json;
using ImageFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ImageFlow.IntegrationTests;

public class WorkspaceClearTests
{
    sealed class GatedPlanner : IPromptPlanningProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string[]> PlanAsync(string theme, int count, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return ["planned prompt"];
        }
    }

    [Fact]
    public async Task PlanningMustSettleBeforeClearingAndCannotRepopulateTheClearedWorkspace()
    {
        var planner = new GatedPlanner();
        await using var app = new AppFactory { Worker = false };
        using var configured = app.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IPromptPlanningProvider>(planner)));
        using var c = configured.CreateClient();
        var planning = c.PostAsJsonAsync("/api/v1/plans", new PlanRequest("In-flight", 1));
        try
        {
            await planner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = true })).StatusCode);
        }
        finally { planner.Release.TrySetResult(); }
        (await planning).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = true })).EnsureSuccessStatusCode();
        Assert.Empty((await c.GetFromJsonAsync<Plan[]>("/api/v1/plans"))!);
    }

    static async Task<Job> Seed(AppFactory app, HttpClient client)
    {
        var p = await client.PostAsJsonAsync("/api/v1/plans", new PlanRequest("Clear test", 1));
        p.EnsureSuccessStatusCode(); var plan = (await p.Content.ReadFromJsonAsync<Plan>())!;
        var j = await client.PostAsJsonAsync("/api/v1/jobs", new JobRequest(plan.Id));
        j.EnsureSuccessStatusCode(); var job = (await j.Content.ReadFromJsonAsync<Job>())!;
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
        var task = await db.Tasks.SingleAsync(); task.Status = "Completed";
        task.ImagePath = Path.Combine(app.DataPath, "images", task.Id + ".svg");
        await File.WriteAllTextAsync(task.ImagePath, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        var attempt = new Attempt { GenerationTaskId = task.Id, Outcome = "Unknown", CostDisposition = "Uncertain", FinishedAt = DateTime.UtcNow };
        db.Attempts.Add(attempt);
        db.TaskReviews.Add(new TaskReview { GenerationTaskId = task.Id, AttemptId = attempt.Id, Resolution = "Generated", Evidence = "Confirmed remote output" });
        await db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task ConfirmedClearDeletesAllRecordsAndImagesButPreservesSettingsAcrossRestart()
    {
        string path;
        await using (var app = new AppFactory { Worker = false })
        {
            path = app.DataPath; using var client = app.CreateClient(); await Seed(app, client);
            (await client.PutAsJsonAsync("/api/v1/settings", new SettingsUpdate(4, .02m))).EnsureSuccessStatusCode();
            // Unqueued plans and orphan/temporary output files are also cleared.
            (await client.PostAsJsonAsync("/api/v1/plans", new PlanRequest("Draft", 2))).EnsureSuccessStatusCode();
            await File.WriteAllTextAsync(Path.Combine(path, "images", "orphan.png.tmp"), "unfinished");
            var response = await client.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = true });
            response.EnsureSuccessStatusCode(); var result = (await response.Content.ReadFromJsonAsync<WorkspaceClearResult>())!;
            Assert.Equal(2, result.Plans); Assert.Equal(1, result.Jobs); Assert.Equal(1, result.Tasks); Assert.Equal(1, result.Images);
            Assert.Equal(0, result.RemainingImageFiles); Assert.Empty(Directory.GetFiles(Path.Combine(path, "images")));
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            Assert.Empty(await db.TaskReviews.ToListAsync()); Assert.Empty(await db.Attempts.ToListAsync());
            Assert.Empty(await db.Tasks.ToListAsync()); Assert.Empty(await db.Jobs.ToListAsync());
            Assert.Empty(await db.Prompts.ToListAsync()); Assert.Empty(await db.Plans.ToListAsync());
            // Repeating an explicitly confirmed clear is safe.
            (await client.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = true })).EnsureSuccessStatusCode();
        }
        await using var restarted = new AppFactory { Worker = false, ExistingPath = path }; using var c = restarted.CreateClient();
        Assert.Empty((await c.GetFromJsonAsync<Plan[]>("/api/v1/plans"))!);
        Assert.Empty((await c.GetFromJsonAsync<Job[]>("/api/v1/jobs"))!);
        var settings = (await c.GetFromJsonAsync<SettingsView>("/api/v1/settings"))!;
        Assert.Equal(4, settings.MaxConcurrency); Assert.Equal(.02m, settings.EstimatedCostPerImageUsd);
        // The cleared database is still usable.
        (await c.PostAsJsonAsync("/api/v1/plans", new PlanRequest("New start", 1))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ConfirmationIsRequiredAndRejectionPreservesRecordsAndFiles()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); await Seed(app, c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/workspace/clear", new { })).StatusCode);
        Assert.Single((await c.GetFromJsonAsync<Plan[]>("/api/v1/plans"))!);
        Assert.Single(Directory.GetFiles(Path.Combine(app.DataPath, "images")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunningTasksOrAttemptsBlockClearing(bool runningTask)
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); await Seed(app, c);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            if (runningTask) (await db.Tasks.SingleAsync()).Status = "Running";
            else (await db.Attempts.SingleAsync()).Outcome = "Running";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = true })).StatusCode);
        Assert.Single((await c.GetFromJsonAsync<Job[]>("/api/v1/jobs"))!);
        Assert.Single(Directory.GetFiles(Path.Combine(app.DataPath, "images")));
    }

    [Fact]
    public async Task LinkedOrNestedStorageIsNotRecursivelyDeleted()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); await Seed(app, c);
        Directory.CreateDirectory(Path.Combine(app.DataPath, "images", "unexpected"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = true })).StatusCode);
        Assert.Single((await c.GetFromJsonAsync<Plan[]>("/api/v1/plans"))!);
        Assert.Single(Directory.GetFiles(Path.Combine(app.DataPath, "images")));
    }

    [Fact]
    public async Task DatabasePathsCannotDeleteFilesOutsideTheImageDirectory()
    {
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); await Seed(app, c);
        var outside = Path.Combine(app.DataPath, "keep.txt"); await File.WriteAllTextAsync(outside, "keep");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            (await db.Tasks.SingleAsync()).ImagePath = outside; await db.SaveChangesAsync();
        }
        (await c.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = true })).EnsureSuccessStatusCode();
        Assert.Equal("keep", await File.ReadAllTextAsync(outside));
    }

    [Fact]
    public async Task LockedFilesAreReportedAndCanBeCleanedUpOnRetry()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows denies deleting a file opened without delete sharing.
        await using var app = new AppFactory { Worker = false }; using var c = app.CreateClient(); await Seed(app, c);
        var path = Directory.GetFiles(Path.Combine(app.DataPath, "images")).Single();
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var response = await c.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = true });
            response.EnsureSuccessStatusCode();
            Assert.Equal(1, (await response.Content.ReadFromJsonAsync<WorkspaceClearResult>())!.RemainingImageFiles);
            Assert.Empty((await c.GetFromJsonAsync<Plan[]>("/api/v1/plans"))!);
        }
        var retry = await c.PostAsJsonAsync("/api/v1/workspace/clear", new { confirmed = true });
        retry.EnsureSuccessStatusCode(); Assert.Equal(0, (await retry.Content.ReadFromJsonAsync<WorkspaceClearResult>())!.RemainingImageFiles);
        Assert.False(File.Exists(path));
    }
}
