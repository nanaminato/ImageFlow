using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public sealed class GenerationRepository(QueueDb db) : IGenerationRepository
{
    public Task<GenerationTask?> GetTask(Guid id, CancellationToken ct) => db.Tasks.Include(x => x.Attempts).Include(x => x.Reviews).SingleOrDefaultAsync(x => x.Id == id, ct);
    public void AddReview(TaskReview review) => db.TaskReviews.Add(review);
    public Task<Plan?> GetPlan(Guid id, CancellationToken ct) => db.Plans.Include(x => x.Prompts).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Prompt?> GetPrompt(Guid id, CancellationToken ct) => db.Prompts.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Job?> GetJob(Guid id, CancellationToken ct) => db.Jobs.Include(x => x.Tasks).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Job?> GetJobForPlan(Guid id, CancellationToken ct) => db.Jobs.Include(x => x.Tasks).SingleOrDefaultAsync(x => x.PlanId == id, ct);
    public void AddPlan(Plan plan) => db.Plans.Add(plan);
    public void AddJob(Job job) => db.Jobs.Add(job);
    public async Task SaveChanges(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
