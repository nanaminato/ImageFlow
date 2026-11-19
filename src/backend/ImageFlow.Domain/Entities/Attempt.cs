namespace ImageFlow;

public class Attempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenerationTaskId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string Outcome { get; set; } = "Running";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string? RequestId { get; set; }
    public string? UsageJson { get; set; }
    public string? Error { get; set; }
}
