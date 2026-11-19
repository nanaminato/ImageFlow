namespace ImageFlow;
// Scoped unit of work. SaveChanges atomically commits the staged entity graph.
public interface IGenerationRepository
{
    Task<Plan?> GetPlan(Guid id, CancellationToken ct);
    Task<Prompt?> GetPrompt(Guid id, CancellationToken ct);
    Task<Job?> GetJob(Guid id, CancellationToken ct);
    Task<Job?> GetJobForPlan(Guid planId, CancellationToken ct);
    void AddPlan(Plan plan);
    void AddJob(Job job);
    Task SaveChanges(CancellationToken ct);
}
