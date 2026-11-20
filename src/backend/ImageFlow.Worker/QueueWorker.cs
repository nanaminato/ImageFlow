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
            foreach (var a in task.Attempts.Where(x => x.Outcome == "Running")) { a.Outcome = "Unknown"; a.FinishedAt = DateTime.UtcNow; a.Error = task.Error; a.CostDisposition = "Uncertain"; }
        }
        var jobs = await db.Jobs.Include(x => x.Tasks).Where(x => x.Status == "Active").ToListAsync(ct);
        foreach (var job in jobs) JobLifecycle.Reconcile(job);
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
                while (active.Count < 8 && !stoppingToken.IsCancellationRequested)
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
            // Re-read the durable setting under the same gate as PUT /settings.
            // Lowering the limit drains existing calls without cancelling paid requests.
            var settings = await db.RuntimeSettings.SingleAsync(x => x.Id == 1, ct);
            if (await db.Tasks.CountAsync(t => t.Status == "Running", ct) >= settings.MaxConcurrency) return null;
            var jobs = await db.Jobs.Include(x => x.Tasks).Where(j => j.Status == "Active").OrderBy(j => j.CreatedAt).ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var job in jobs)
            {
                if (job.Provider != "" && (job.Provider != options.Provider || job.Model != options.ImageModel || job.ImageSize != options.ImageSize))
                {
                    JobLifecycle.Stop(job, "ProviderConfigurationChanged", true); continue;
                }
                JobLifecycle.Reconcile(job);
                if (job.Status != "Active") continue;
                if (!JobLifecycle.CanReserve(job))
                {
                    if (!job.Tasks.Any(t => t.Status == "Running")) JobLifecycle.Stop(job, "BudgetLimit");
                    continue;
                }
                var task = job.Tasks.Where(t => (t.Status is "Pending" or "RetryWaiting") && (t.NextAttemptAt == null || t.NextAttemptAt <= now)).OrderBy(t => t.Id).FirstOrDefault();
                if (task == null && job.Mode == "Continuous" && job.Tasks.Count < job.MaxImages && !job.Tasks.Any(t => t.Status == "RetryWaiting"))
                {
                    var prompts = await db.Prompts.Where(x => x.PlanId == job.PlanId).OrderBy(x => x.Id).ToListAsync(ct);
                    if (prompts.Count == 0) { JobLifecycle.Stop(job, "MissingPrompts", true); continue; }
                    var prompt = prompts[job.Tasks.Count % prompts.Count];
                    task = new GenerationTask { JobId = job.Id, PromptId = prompt.Id, PromptText = prompt.Text };
                    db.Tasks.Add(task);
                    job.Tasks.Add(task);
                }
                if (task == null) continue;
                task.Status = "Running"; task.Error = null; task.NextAttemptAt = null;
                job.EstimatedExposureUsd += job.EstimatedCostPerImageUsd;
                db.Attempts.Add(new Attempt { GenerationTaskId = task.Id, Provider = options.Provider, Model = options.Provider == "Mock" ? "mock-svg" : options.ImageModel, EstimatedCostUsd = job.EstimatedCostPerImageUsd });
                // Entity graph, reservation and Running attempt commit together, before the request.
                await db.SaveChangesAsync(ct);
                return task.Id;
            }
            await db.SaveChangesAsync(ct);
            return null;
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
                task.Status = "Completed"; task.ImagePath = path; attempt.Outcome = "Completed"; attempt.UsageJson = result!.UsageJson; attempt.RequestId = result.RequestId; attempt.CostDisposition = "Retained";
            }
            else
            {
                // A local persistence failure following success must also prevent automatic resend.
                var failure = error as ProviderFailure;
                attempt.RequestId = failure?.RequestId ?? result?.RequestId;
                attempt.UsageJson = result?.UsageJson;
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
            if (error is ProviderFailure { Unknown: false })
            {
                // Explicit rejection (including 429): this attempt did not generate an image.
                job.EstimatedExposureUsd -= attempt.EstimatedCostUsd;
                attempt.EstimatedCostUsd = 0;
                attempt.CostDisposition = "Released";
            }
            else if (error != null) attempt.CostDisposition = "Uncertain";
            if (job.Status == "Cancelled") foreach (var t in job.Tasks.Where(x => x.Status is "Pending" or "RetryWaiting")) t.Status = "Cancelled";
            JobLifecycle.Reconcile(job);
            await db.SaveChangesAsync();
        }
        finally { gate.Mutex.Release(); }
    }
}
