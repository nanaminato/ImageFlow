namespace ImageFlow;

public record ReviewRequest(Guid AttemptId, string Resolution, string Evidence, bool Confirmed);
public record RetryRequest(Guid AttemptId, bool Confirmed);
public sealed class TaskReviewService(IGenerationRepository repository, QueueGate gate, EngineOptions options)
{
    public async Task<OperationResult<GenerationTask>> Review(Guid id, ReviewRequest request, CancellationToken ct)
    {
        if (!request.Confirmed || request.Resolution is not ("Generated" or "NotGeneratedNotCharged" or "Abandon") || string.IsNullOrWhiteSpace(request.Evidence) || request.Evidence.Length > 2000)
            return new(Failure: FailureKind.InvalidInput, Message: "Explicit confirmation and provider evidence (1–2000 characters) are required. Select Generated, NotGeneratedNotCharged, or Abandon.");
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var task = await repository.GetTask(id, ct); if (task == null) return new(Failure: FailureKind.NotFound);
            var attempt = task.Attempts.OrderByDescending(a => a.StartedAt).FirstOrDefault();
            var job = await repository.GetJob(task.JobId, ct);
            if (job == null || task.Status != "Unknown" || attempt == null || attempt.Id != request.AttemptId || attempt.Outcome != "Unknown" || task.Reviews.Any(r => r.AttemptId == attempt.Id) || job.Tasks.Any(t => t.Status == "Running"))
                return new(Failure: FailureKind.Conflict, Message: "Review is stale or work is still in flight. Refresh and review the latest Unknown attempt after all requests finish.");
            if (request.Resolution == "NotGeneratedNotCharged" && job.EstimatedExposureUsd < attempt.EstimatedCostUsd)
                return new(Failure: FailureKind.Conflict, Message: "Estimate ledger is inconsistent; automatic release is blocked.");
            repository.AddReview(new TaskReview { GenerationTaskId = id, AttemptId = attempt.Id, Resolution = request.Resolution, Evidence = request.Evidence.Trim() });
            if (request.Resolution == "NotGeneratedNotCharged")
            {
                job.EstimatedExposureUsd -= attempt.EstimatedCostUsd;
                attempt.EstimatedCostUsd = 0; attempt.CostDisposition = "ManuallyReleased";
                task.Status = "Paused"; task.Error = "Provider evidence confirms no output and no charge. Explicit retry authorization is required.";
            }
            else
            {
                task.Status = request.Resolution == "Generated" ? "Completed" : "Cancelled";
                task.Error = request.Resolution == "Generated" ? "Manually confirmed generated. Output may only be available from the provider; no local image was imported." : "Uncertain outcome abandoned; estimated exposure retained.";
                attempt.CostDisposition = request.Resolution == "Generated" ? "ManuallyRetained" : "Uncertain";
            }
            if (job.Status != "Cancelled") { job.Status = "Paused"; job.StopReason = "ManualReview"; }
            await repository.SaveChanges(ct); return new(task);
        }
        finally { gate.Mutex.Release(); }
    }
    public async Task<OperationResult<GenerationTask>> AuthorizeRetry(Guid id, RetryRequest request, CancellationToken ct)
    {
        if (!request.Confirmed) return new(Failure: FailureKind.InvalidInput, Message: "Explicit authorization of a new possibly charged request is required.");
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var task = await repository.GetTask(id, ct); if (task == null) return new(Failure: FailureKind.NotFound);
            var attempt = task.Attempts.OrderByDescending(a => a.StartedAt).FirstOrDefault();
            var review = task.Reviews.SingleOrDefault(r => r.AttemptId == request.AttemptId);
            var job = await repository.GetJob(task.JobId, ct);
            if (task.Status != "Paused" || attempt?.Id != request.AttemptId || review?.Resolution != "NotGeneratedNotCharged" || review.RetryAuthorizedAt != null || job?.Status != "Paused" || job.Tasks.Any(t => t.Status is "Running" or "Unknown") || task.Attempts.Count >= options.MaxAttempts || !JobLifecycle.CanReserve(job) || (job.Provider != "" && (job.Provider != options.Provider || job.Model != options.ImageModel || job.ImageSize != options.ImageSize)))
                return new(Failure: FailureKind.Conflict, Message: "Retry is not eligible. Check latest review, unresolved outcomes, attempt limit, remaining budget, job cancellation and provider configuration.");
            review.RetryAuthorizedAt = DateTime.UtcNow;
            task.Status = "Pending"; task.NextAttemptAt = null; task.Error = null;
            await repository.SaveChanges(ct); return new(task);
        }
        finally { gate.Mutex.Release(); }
    }
}
