namespace ImageFlow;

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<GenerationTask> Tasks { get; set; } = [];
}
