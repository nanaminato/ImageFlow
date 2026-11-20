namespace ImageFlow;

public static class JobLifecycle
{
    public static bool CanReserve(Job job) => job.BudgetUsd is not { } budget || job.EstimatedExposureUsd + job.EstimatedCostPerImageUsd <= budget;
    public static void Stop(Job job, string reason, bool attention = false)
    {
        job.StopReason = reason;
        job.Status = attention ? "NeedsAttention" : "Completed";
        foreach (var task in job.Tasks.Where(t => t.Status is "Pending" or "RetryWaiting")) task.Status = "Cancelled";
    }
    public static void Reconcile(Job job)
    {
        if (job.Status != "Active") return;
        if (job.Mode == "Continuous" && job.Tasks.Any(t => t.Status is "Unknown" or "Failed"))
        {
            Stop(job, job.Tasks.Any(t => t.Status == "Unknown") ? "UnknownOutcome" : "ProviderFailure", true); return;
        }
        if (job.Tasks.Any(t => t.Status is "Running" or "Pending" or "RetryWaiting" or "Paused")) return;
        if (job.Mode == "Continuous")
        {
            if (job.Tasks.Count >= job.MaxImages) Stop(job, "QuantityLimit");
            else if (!CanReserve(job)) Stop(job, "BudgetLimit");
        }
        else
        {
            job.Status = job.Tasks.All(t => t.Status == "Completed") ? "Completed" : "NeedsAttention";
            job.StopReason = job.Status == "Completed" ? "Completed" : "ProviderFailure";
        }
    }
}
