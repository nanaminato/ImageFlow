namespace ImageFlow;

public class GenerationTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Guid PromptId { get; set; }
    public string PromptText { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? Error { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? ImagePath { get; set; }
    public List<Attempt> Attempts { get; set; } = [];
}
