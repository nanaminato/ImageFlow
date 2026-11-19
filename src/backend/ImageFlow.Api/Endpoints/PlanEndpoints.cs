using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public static class PlanEndpoints
{
    public static void MapPlanEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/plans", async (QueueDb db, CancellationToken ct) => await db.Plans.AsNoTracking().Include(x => x.Prompts).OrderByDescending(x => x.CreatedAt).ToListAsync(ct));
        api.MapGet("/plans/{id:guid}", async (Guid id, QueueDb db, CancellationToken ct) => await db.Plans.AsNoTracking().Include(x => x.Prompts).SingleOrDefaultAsync(x => x.Id == id, ct) is { } plan ? Results.Ok(plan) : Results.NotFound());
        api.MapPost("/plans", async (PlanRequest request, PromptPlanService service, CancellationToken ct) => (await service.Create(request.Theme, request.Count, ct)).ToHttp(p => $"/api/v1/plans/{p.Id}"));
        api.MapPut("/prompts/{id:guid}", async (Guid id, EditPrompt request, PromptPlanService service, CancellationToken ct) => (await service.Edit(id, request.Text, ct)).ToHttp());
    }
}
