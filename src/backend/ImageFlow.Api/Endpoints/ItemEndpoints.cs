using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public sealed record BulkDeleteRequest(Guid[]? Ids, bool Confirmed);

public static class ItemEndpoints
{
    public static void MapItemEndpoints(this RouteGroupBuilder api)
    {
        api.MapDelete("/plans/{id:guid}", async (Guid id, bool? confirmed, IItemMaintenance items, CancellationToken ct) => (await items.Delete("plan", id, confirmed == true, ct)).ToHttp());
        api.MapDelete("/jobs/{id:guid}", async (Guid id, bool? confirmed, IItemMaintenance items, CancellationToken ct) => (await items.Delete("job", id, confirmed == true, ct)).ToHttp());
        api.MapDelete("/images/{id:guid}", async (Guid id, bool? confirmed, IItemMaintenance items, CancellationToken ct) => (await items.Delete("image", id, confirmed == true, ct)).ToHttp());
        api.MapPost("/plans/bulk-delete", async (BulkDeleteRequest request, IItemMaintenance items, CancellationToken ct) => (await items.DeleteMany("plan", request.Ids, request.Confirmed, ct)).ToHttp());
        api.MapPost("/jobs/bulk-delete", async (BulkDeleteRequest request, IItemMaintenance items, CancellationToken ct) => (await items.DeleteMany("job", request.Ids, request.Confirmed, ct)).ToHttp());
        api.MapPost("/images/bulk-delete", async (BulkDeleteRequest request, IItemMaintenance items, CancellationToken ct) => (await items.DeleteMany("image", request.Ids, request.Confirmed, ct)).ToHttp());
        api.MapGet("/jobs/{id:guid}/images.zip", Download);
    }
    private static async Task<IResult> Download(Guid id, QueueDb db, QueueGate gate, ImageStorage storage, EngineOptions options, CancellationToken ct)
    {
        await gate.Mutex.WaitAsync(ct);
        FileStream? archive = null;
        try
        {
            if (!await db.Jobs.AnyAsync(j => j.Id == id, ct)) return Results.NotFound();
            var tasks = await db.Tasks.AsNoTracking().Where(t => t.JobId == id && t.ImagePath != null).OrderBy(t => t.Id).ToListAsync(ct);
            if (tasks.Count == 0) return Results.NotFound(new { error = "This collection has no saved images to download." });
            var paths = tasks.Select(t => storage.Validate(t.ImagePath!)).ToArray();
            if (paths.Any(p => !File.Exists(p))) return Results.Conflict(new { error = "Some collection image files are missing. Remove the missing images or restore the files before downloading." });
            var exports = Path.Combine(options.DataDirectory, "exports");
            Directory.CreateDirectory(exports);
            if ((File.GetAttributes(exports) & FileAttributes.ReparsePoint) != 0) throw new IOException("Export directory is linked.");
            archive = new FileStream(Path.Combine(exports, Guid.NewGuid() + ".zip"), FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
                for (var i = 0; i < paths.Length; i++)
                {
                    var entry = zip.CreateEntry($"{i + 1:D4}-{tasks[i].Id}{Path.GetExtension(paths[i])}", CompressionLevel.NoCompression);
                    await using var output = entry.Open();
                    await using var input = new FileStream(paths[i], FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, ct);
                }
            archive.Position = 0;
            var result = Results.Stream(archive, "application/zip", $"imageflow-{id}.zip");
            archive = null; // ASP.NET owns and disposes the stream, removing the temporary ZIP.
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { return Results.Conflict(new { error = "Collection image storage cannot be read safely. No archive was downloaded." }); }
        finally
        {
            if (archive != null) await archive.DisposeAsync();
            gate.Mutex.Release();
        }
    }
}
