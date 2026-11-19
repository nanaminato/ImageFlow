using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace ImageFlow;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/events", async (HttpContext context, QueueDb db, CancellationToken ct) =>
        {
            context.Response.ContentType = "text/event-stream"; context.Response.Headers.CacheControl = "no-cache";
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var snapshot = await db.Jobs.AsNoTracking().Select(x => new { x.Id, x.Status, completed = x.Tasks.Count(t => t.Status == "Completed"), total = x.Tasks.Count }).ToListAsync(ct);
                    await context.Response.WriteAsync("event: jobs\ndata: " + JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n\n", ct);
                    await context.Response.Body.FlushAsync(ct); await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        });

    }
}
