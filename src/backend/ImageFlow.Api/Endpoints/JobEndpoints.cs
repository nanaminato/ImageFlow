using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public static class JobEndpoints
{
    public static void MapJobEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/jobs", async (QueueDb db, CancellationToken ct) => await db.Jobs.AsNoTracking().Include(x => x.Tasks).ThenInclude(x => x.Attempts).Include(x => x.Tasks).ThenInclude(x => x.Reviews).OrderByDescending(x => x.CreatedAt).ToListAsync(ct));
        api.MapGet("/jobs/{id:guid}", async (Guid id, QueueDb db, CancellationToken ct) => await db.Jobs.AsNoTracking().Include(x => x.Tasks).ThenInclude(x => x.Attempts).Include(x => x.Tasks).ThenInclude(x => x.Reviews).SingleOrDefaultAsync(x => x.Id == id, ct) is { } job ? Results.Ok(job) : Results.NotFound());
        api.MapPost("/jobs", async (JobRequest request, JobService service, CancellationToken ct) => (await service.Enqueue(request.PlanId, ct, request.Mode, request.MaxImages, request.BudgetUsd)).ToHttp(j => $"/api/v1/jobs/{j.Id}"));
        api.MapPost("/jobs/{id:guid}/{action}", async (Guid id, string action, JobService service, CancellationToken ct) => (await service.Control(id, action, ct)).ToHttp());
    }
}
