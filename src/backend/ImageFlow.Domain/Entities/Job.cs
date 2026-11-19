namespace ImageFlow;

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Mode { get; set; } = "Finite";
    public int MaxImages { get; set; }
    public decimal? BudgetUsd { get; set; }
    public decimal EstimatedCostPerImageUsd { get; set; }
    // Includes outstanding reservations and retained completed/uncertain attempts; never billed cost.
    public decimal EstimatedExposureUsd { get; set; }
    public string? StopReason { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string ImageSize { get; set; } = "";
    public List<GenerationTask> Tasks { get; set; } = [];
}
