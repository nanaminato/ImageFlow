namespace ImageFlow;

public class Prompt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public string Text { get; set; } = "";
}
