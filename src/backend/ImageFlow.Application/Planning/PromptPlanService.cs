namespace ImageFlow;

public sealed class PromptPlanService(IPromptPlanningProvider planner, IGenerationRepository repository, QueueGate gate)
{
    public async Task<OperationResult<Plan>> Create(string theme, int count, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(theme) || theme.Length > 2000 || count is < 1 or > 20)
            return new(Failure: FailureKind.InvalidInput, Message: "Theme required (max 2000 characters); count must be 1–20.");
        await gate.Mutex.WaitAsync(ct);
        try { gate.ActivePlanningRequests++; }
        finally { gate.Mutex.Release(); }
        try
        {
            var texts = await planner.PlanAsync(theme.Trim(), count, ct);
            if (texts.Length != count || texts.Distinct().Count() != count || texts.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 8000))
                return new(Failure: FailureKind.Provider, Message: "Planner returned invalid or duplicate prompts.");
            var plan = new Plan { Theme = theme.Trim() };
            plan.Prompts = texts.Select(x => new Prompt { PlanId = plan.Id, Text = x }).ToList();
            repository.AddPlan(plan);
            await repository.SaveChanges(CancellationToken.None);
            return new(plan, Created: true);
        }
        catch (ProviderFailure e) { return new(Failure: FailureKind.Provider, Message: e.Message); }
        finally
        {
            await gate.Mutex.WaitAsync(CancellationToken.None);
            try { gate.ActivePlanningRequests--; }
            finally { gate.Mutex.Release(); }
        }
    }
    public async Task<OperationResult<Prompt>> Edit(Guid id, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 8000) return new(Failure: FailureKind.InvalidInput, Message: "Prompt must have 1–8000 characters.");
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var prompt = await repository.GetPrompt(id, ct);
            if (prompt == null) return new(Failure: FailureKind.NotFound);
            if (await repository.GetJobForPlan(prompt.PlanId, ct) != null) return new(Failure: FailureKind.Conflict, Message: "Queued plans are immutable; create a new plan.");
            prompt.Text = text.Trim();
            await repository.SaveChanges(ct);
            return new(prompt);
        }
        finally { gate.Mutex.Release(); }
    }
}
