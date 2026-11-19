namespace ImageFlow;

public record PlanRequest(string Theme, int Count = 3);
public record EditPrompt(string Text);
public record JobRequest(Guid PlanId);
