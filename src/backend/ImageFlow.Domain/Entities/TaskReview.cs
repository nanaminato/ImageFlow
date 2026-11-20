namespace ImageFlow;

public class TaskReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenerationTaskId { get; set; }
    public Guid AttemptId { get; set; }
    public string Resolution { get; set; } = "";
    public string Evidence { get; set; } = "";
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RetryAuthorizedAt { get; set; }
}
