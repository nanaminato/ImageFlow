using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/tasks/{id:guid}", async (Guid id, QueueDb db, CancellationToken ct) => await db.Tasks.AsNoTracking().Include(t => t.Attempts).Include(t => t.Reviews).SingleOrDefaultAsync(t => t.Id == id, ct) is { } task ? Results.Ok(task) : Results.NotFound());
        api.MapPost("/tasks/{id:guid}/reviews", async (Guid id, ReviewRequest request, TaskReviewService service, CancellationToken ct) => (await service.Review(id, request, ct)).ToHttp());
        api.MapPost("/tasks/{id:guid}/retry", async (Guid id, RetryRequest request, TaskReviewService service, CancellationToken ct) => (await service.AuthorizeRetry(id, request, ct)).ToHttp());
    }
}
