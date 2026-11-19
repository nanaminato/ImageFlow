namespace ImageFlow;

public class Plan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Theme { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Prompt> Prompts { get; set; } = [];
}
