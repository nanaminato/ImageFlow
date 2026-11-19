namespace ImageFlow;

public sealed class JobService(IGenerationRepository repository, QueueGate gate)
{
    public async Task<OperationResult<Job>> Enqueue(Guid planId, CancellationToken ct)
    {
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var existing = await repository.GetJobForPlan(planId, ct);
            if (existing != null) return new(existing);
            var plan = await repository.GetPlan(planId, ct);
            if (plan == null) return new(Failure: FailureKind.NotFound);
            var job = new Job { PlanId = plan.Id };
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
            if (action == "cancel") foreach (var task in job.Tasks.Where(x => x.Status is "Pending" or "RetryWaiting")) task.Status = "Cancelled";
            if (action == "resume" && job.Tasks.All(x => x.Status is "Completed" or "Failed" or "Unknown")) job.Status = job.Tasks.All(x => x.Status == "Completed") ? "Completed" : "NeedsAttention";
            await repository.SaveChanges(ct);
            return new(job);
        }
        finally { gate.Mutex.Release(); }
    }
}
