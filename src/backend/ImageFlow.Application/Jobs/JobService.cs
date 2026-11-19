namespace ImageFlow;

public sealed class JobService(IGenerationRepository repository, QueueGate gate, RuntimeSettingsService settings, EngineOptions options)
{
    public async Task<OperationResult<Job>> Enqueue(Guid planId, CancellationToken ct, string mode = "Finite", int? maxImages = null, decimal? budgetUsd = null)
    {
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var existing = await repository.GetJobForPlan(planId, ct);
            if (existing != null) return new(existing);
            var plan = await repository.GetPlan(planId, ct);
            if (plan == null) return new(Failure: FailureKind.NotFound);
            var current = await settings.Get(ct);
            var invalidMode = mode is not ("Finite" or "Continuous");
            var invalidContinuousLimits = mode == "Continuous" && (maxImages is null or < 1 or > 1000 || budgetUsd is null or <= 0);
            var invalidFiniteCount = mode == "Finite" && maxImages != null && maxImages != plan.Prompts.Count;
            var invalidBudget = budgetUsd is <= 0 or > 1000000 ||
                (budgetUsd is { } budget && (decimal.Round(budget, 6) != budget || current.EstimatedCostPerImageUsd <= 0));
            if (invalidMode || invalidContinuousLimits || invalidFiniteCount || invalidBudget)
                return new(Failure: FailureKind.InvalidInput, Message: "Continuous mode requires 1–1000 images, a positive USD estimate limit and a positive configured per-image estimate (up to six decimal places). Finite image count equals its plan size.");
            var job = new Job
            {
                PlanId = plan.Id,
                Mode = mode,
                MaxImages = mode == "Finite" ? plan.Prompts.Count : maxImages!.Value,
                BudgetUsd = budgetUsd,
                EstimatedCostPerImageUsd = current.EstimatedCostPerImageUsd,
                Provider = options.Provider,
                Model = options.ImageModel,
                ImageSize = options.ImageSize
            };
            if (mode == "Finite")
                job.Tasks = plan.Prompts.Select(x => new GenerationTask { JobId = job.Id, PromptId = x.Id, PromptText = x.Text }).ToList();
            repository.AddJob(job);
            await repository.SaveChanges(ct);
            return new(job, Created: true);
        }
        finally { gate.Mutex.Release(); }
    }
    public async Task<OperationResult<Job>> Control(Guid id, string action, CancellationToken ct)
    {
        if (action is not ("pause" or "resume" or "cancel")) return new(Failure: FailureKind.InvalidInput);
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var job = await repository.GetJob(id, ct);
            if (job == null) return new(Failure: FailureKind.NotFound);
            if (job.Status is "Completed" or "Cancelled" or "NeedsAttention") return new(Failure: FailureKind.Conflict, Message: "Terminal jobs cannot be resumed; Unknown requests require manual provider reconciliation.");
            job.Status = action == "pause" ? "Paused" : action == "resume" ? "Active" : "Cancelled";
            if (action == "cancel") job.StopReason = "ManualCancellation";
            if (action == "cancel") foreach (var task in job.Tasks.Where(x => x.Status is "Pending" or "RetryWaiting")) task.Status = "Cancelled";
            if (action == "resume") JobLifecycle.Reconcile(job);
            await repository.SaveChanges(ct);
            return new(job);
        }
        finally { gate.Mutex.Release(); }
    }
}
