namespace ImageFlow;

public record PlanRequest(string Theme, int Count = 3);
public record EditPrompt(string Text);
public record JobRequest(Guid PlanId, string Mode = "Finite", int? MaxImages = null, decimal? BudgetUsd = null);
