using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/images", async (QueueDb db) => await db.Tasks.AsNoTracking().Where(x => x.ImagePath != null).Select(x => new { x.Id, x.JobId, x.PromptId, x.PromptText, url = "/api/v1/images/" + x.Id }).ToListAsync());
        api.MapGet("/images/{id:guid}", async (Guid id, QueueDb db) =>
        {
            var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            return task?.ImagePath is { } path && File.Exists(path) ? Results.File(path, Path.GetExtension(path) == ".svg" ? "image/svg+xml" : "image/png") : Results.NotFound();
        });
        api.MapGet("/usage", async (QueueDb db) => await db.Attempts.AsNoTracking().OrderByDescending(x => x.StartedAt).ToListAsync());

    }
}
