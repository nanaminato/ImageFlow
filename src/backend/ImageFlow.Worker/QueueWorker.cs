using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace ImageFlow;

public sealed class QueueWorker(IServiceScopeFactory scopes, QueueGate gate, EngineOptions options, IImageGenerationProvider provider, ILogger<QueueWorker> log) : BackgroundService
{
    public static async Task Recover(QueueDb db, CancellationToken ct)
    {
        var tasks = await db.Tasks.Include(x => x.Attempts).Where(x => x.Status == "Running").ToListAsync(ct);
        foreach (var task in tasks)
        {
            task.Status = "Unknown"; task.Error = "Process stopped during request. Inspect provider records before creating replacement work.";
            foreach (var a in task.Attempts.Where(x => x.Outcome == "Running")) { a.Outcome = "Unknown"; a.FinishedAt = DateTime.UtcNow; a.Error = task.Error; }
        }
        var jobs = await db.Jobs.Include(x => x.Tasks).Where(x => x.Status == "Active").ToListAsync(ct);
        foreach (var job in jobs.Where(j => j.Tasks.All(t => t.Status is "Completed" or "Failed" or "Unknown" or "Cancelled")))
            job.Status = job.Tasks.All(t => t.Status == "Completed") ? "Completed" : "NeedsAttention";
        await db.SaveChangesAsync(ct);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.WorkerEnabled) return;
        var active = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var completed in active.Where(x => x.IsCompleted).ToArray()) { await completed; active.Remove(completed); }
                while (active.Count < options.MaxConcurrency && !stoppingToken.IsCancellationRequested)
                {
                    var id = await Claim(stoppingToken); if (id == null) break;
                    active.Add(Run(id.Value, stoppingToken));
                }
                await Task.Delay(150, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await Task.WhenAll(active); }
    }
    async Task<Guid?> Claim(CancellationToken ct)
    {
        await gate.Mutex.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            var now = DateTime.UtcNow;
            var task = await db.Tasks.Where(t => (t.Status == "Pending" || t.Status == "RetryWaiting") && (t.NextAttemptAt == null || t.NextAttemptAt <= now) && db.Jobs.Any(j => j.Id == t.JobId && j.Status == "Active")).OrderBy(t => t.Id).FirstOrDefaultAsync(ct);
            if (task == null) return null;
            task.Status = "Running"; task.Error = null;
            db.Attempts.Add(new Attempt { GenerationTaskId = task.Id, Provider = options.Provider, Model = options.Provider == "Mock" ? "mock-svg" : options.ImageModel });
            await db.SaveChangesAsync(ct); return task.Id;
        }
        finally { gate.Mutex.Release(); }
    }
    async Task Run(Guid id, CancellationToken ct)
    {
        ImageResult? result = null; Exception? error = null; string? path = null;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            var task = await db.Tasks.AsNoTracking().SingleAsync(x => x.Id == id, CancellationToken.None);
            try
            {
                result = await provider.GenerateAsync(task.PromptText, ct);
                path = Path.Combine(options.DataDirectory, "images", $"{id}.{result.Extension}");
                await File.WriteAllBytesAsync(path + ".tmp", result.Bytes, CancellationToken.None);
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception e) { error = e; }
        }
        await gate.Mutex.WaitAsync(CancellationToken.None);
        try
        {
            using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
            var task = await db.Tasks.Include(x => x.Attempts).SingleAsync(x => x.Id == id);
            var attempt = task.Attempts.Single(x => x.Outcome == "Running"); attempt.FinishedAt = DateTime.UtcNow;
            if (error == null)
            {
                task.Status = "Completed"; task.ImagePath = path; attempt.Outcome = "Completed"; attempt.UsageJson = result!.UsageJson; attempt.RequestId = result.RequestId;
            }
            else
            {
                // A local persistence failure following success must also prevent automatic resend.
                var failure = error as ProviderFailure;
                task.Error = failure?.Message ?? "Request or output persistence interrupted; outcome unknown.";
                attempt.Error = task.Error;
                if (failure?.Retryable == true && task.Attempts.Count < options.MaxAttempts)
                {
                    task.Status = "RetryWaiting";
                    var backoff = TimeSpan.FromSeconds(Math.Pow(2, task.Attempts.Count));
                    var delay = failure.RetryAfter is { } retry && retry > backoff ? retry : backoff;
                    task.NextAttemptAt = DateTime.UtcNow + delay;
                }
                else task.Status = failure is { Unknown: false } ? "Failed" : "Unknown";
                attempt.Outcome = task.Status;
                log.LogWarning("Task {TaskId} ended with {Status}", id, task.Status);
            }
            var job = await db.Jobs.Include(x => x.Tasks).SingleAsync(x => x.Id == task.JobId);
            if (job.Status == "Cancelled") foreach (var t in job.Tasks.Where(x => x.Status is "Pending" or "RetryWaiting")) t.Status = "Cancelled";
            if (job.Status == "Active" && job.Tasks.All(x => x.Status is "Completed" or "Failed" or "Unknown" or "Cancelled")) job.Status = job.Tasks.All(x => x.Status == "Completed") ? "Completed" : "NeedsAttention";
            await db.SaveChangesAsync();
        }
        finally { gate.Mutex.Release(); }
    }
}
