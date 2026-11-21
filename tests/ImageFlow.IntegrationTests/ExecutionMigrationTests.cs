using System.Net;
using System.Net.Http.Json;
using ImageFlow;
using Microsoft.EntityFrameworkCore;
namespace ImageFlow.IntegrationTests;

public class ExecutionMigrationTests
{
    [Fact]
    public async Task UpgradeKeepsHistoricalEvidenceButCannotExecuteOldJobs()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), ".cache", "upgrade", Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        var options = new DbContextOptionsBuilder<QueueDb>().UseSqlite($"Data Source={Path.Combine(path, "imageflow.db")}").Options;
        var plan = Guid.NewGuid(); var job = Guid.NewGuid(); var task = Guid.NewGuid(); var attempt = Guid.NewGuid(); var now = DateTime.UtcNow;
        using (var db = new QueueDb(options))
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
            await migrator.MigrateAsync("20261119150455_ManualTaskReviews");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Plans (Id,Theme,CreatedAt) VALUES ({plan},'history',{now})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Jobs (Id,PlanId,Status,CreatedAt,Mode,MaxImages,EstimatedCostPerImageUsd,EstimatedExposureUsd,Provider,Model,ImageSize) VALUES ({job},{plan},'Active',{now},'Finite',1,'0.1','0.1','OpenAI','gpt-image-1.5','1024x1024')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Tasks (Id,JobId,PromptId,PromptText,Status) VALUES ({task},{job},{Guid.NewGuid()},'original stored prompt','Running')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Attempts (Id,GenerationTaskId,StartedAt,Outcome,Provider,Model,EstimatedCostUsd,CostDisposition,RequestId,UsageJson) VALUES ({attempt},{task},{now},'Running','OpenAI','gpt-image-1.5','0.1','Reserved','req-history','{{\"total_tokens\":12}}')");
        }
        var provider = new ProbeProvider();
        await using var app = new AppFactory { ExistingPath = path, ImageProvider = provider };
        using var client = app.CreateClient();
        var saved = (await client.GetFromJsonAsync<Job>($"/api/v1/jobs/{job}"))!;
        Assert.Equal("NeedsAttention", saved.Status); Assert.Equal("ExecutionProfileRequired", saved.StopReason);
        Assert.Equal("gpt-image-1.5", saved.Model); Assert.Equal("", saved.Endpoint);
        Assert.Equal(0.1m, saved.EstimatedExposureUsd);
        var unknown = Assert.Single(saved.Tasks); Assert.Equal("Unknown", unknown.Status);
        var record = Assert.Single(unknown.Attempts); Assert.Equal("Unknown", record.Outcome);
        Assert.Equal("Uncertain", record.CostDisposition); Assert.Equal("req-history", record.RequestId);
        Assert.Contains("12", record.UsageJson); Assert.Equal(0, provider.Calls);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/jobs/{job}/resume", null)).StatusCode);
        await Task.Delay(350); Assert.Equal(0, provider.Calls);
    }
}
