using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public sealed class ItemMaintenance(QueueDb db, QueueGate gate, ImageStorage images) : IItemMaintenance
{
    public Task<OperationResult<ItemDeletionResult>> Delete(string kind, Guid id, bool confirmed, CancellationToken ct) =>
        DeleteMany(kind, [id], confirmed, ct);

    public async Task<OperationResult<ItemDeletionResult>> DeleteMany(string kind, IReadOnlyCollection<Guid>? ids, bool confirmed, CancellationToken ct)
    {
        if (!confirmed) return new(Failure: FailureKind.InvalidInput, Message: "Explicit confirmation is required to delete this item.");
        if (kind is not ("plan" or "job" or "image") || ids == null || ids.Count is < 1 or > 1000 || ids.Contains(Guid.Empty))
            return new(Failure: FailureKind.InvalidInput, Message: "Select between 1 and 1000 valid items to delete.");
        var selectedIds = ids.Distinct().ToArray();
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var existingCount = kind switch {
                "plan" => await db.Plans.CountAsync(p => selectedIds.Contains(p.Id), ct),
                "job" => await db.Jobs.CountAsync(j => selectedIds.Contains(j.Id), ct),
                _ => await db.Tasks.CountAsync(t => selectedIds.Contains(t.Id) && t.ImagePath != null, ct)
            };
            if (existingCount != selectedIds.Length) return new(Failure: FailureKind.NotFound, Message: "One or more selected items no longer exist. Refresh the list and select again.");
            var jobs = db.Jobs.Where(j => kind == "plan" ? selectedIds.Contains(j.PlanId) : selectedIds.Contains(j.Id));
            var jobIds = kind == "image" ? Array.Empty<Guid>() : await jobs.Select(j => j.Id).ToArrayAsync(ct);
            var tasks = kind == "image" ? db.Tasks.Where(t => selectedIds.Contains(t.Id)) : db.Tasks.Where(t => jobIds.Contains(t.JobId));
            if (await tasks.AnyAsync(t => t.Status == "Running" || t.Attempts.Any(a => a.Outcome == "Running"), ct))
                return new(Failure: FailureKind.Conflict, Message: "Wait for this item's in-flight image requests to finish before deleting. Pause the job to stop new requests.");

            var paths = await tasks.Where(t => t.ImagePath != null).Select(t => t.ImagePath!).ToArrayAsync(ct);
            string[] fileNames;
            try { fileNames = paths.Select(images.Validate).Select(p => Path.GetFileName(p)!).Distinct().ToArray(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { return new(Failure: FailureKind.Conflict, Message: "Image storage is unavailable or contains an unsafe path. No records were deleted."); }
            var taskIds = await tasks.Select(t => t.Id).ToArrayAsync(ct);
            // A corrupt/shared path must never delete another collection's output.
            if (await db.Tasks.AnyAsync(t => !taskIds.Contains(t.Id) && t.ImagePath != null && paths.Contains(t.ImagePath), ct))
                return new(Failure: FailureKind.Conflict, Message: "Image storage is shared with another item. No records were deleted.");

            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                foreach (var name in fileNames)
                    if (!await db.PendingImageDeletions.AnyAsync(x => x.FileName == name, ct))
                        db.PendingImageDeletions.Add(new() { FileName = name });
                await db.SaveChangesAsync(ct);
                if (kind == "image")
                    await tasks.ExecuteUpdateAsync(set => set.SetProperty(t => t.ImagePath, (string?)null), ct);
                else
                {
                    await db.TaskReviews.Where(r => taskIds.Contains(r.GenerationTaskId)).ExecuteDeleteAsync(ct);
                    await db.Attempts.Where(a => taskIds.Contains(a.GenerationTaskId)).ExecuteDeleteAsync(ct);
                    await tasks.ExecuteDeleteAsync(ct);
                    await db.Jobs.Where(j => jobIds.Contains(j.Id)).ExecuteDeleteAsync(ct);
                    if (kind == "plan")
                    {
                        await db.Prompts.Where(p => selectedIds.Contains(p.PlanId)).ExecuteDeleteAsync(ct);
                        await db.Plans.Where(p => selectedIds.Contains(p.Id)).ExecuteDeleteAsync(ct);
                    }
                }
                await transaction.CommitAsync(ct);
            }
            await CleanupUnderGate(CancellationToken.None);
            var pending = await db.PendingImageDeletions.CountAsync(x => fileNames.Contains(x.FileName), CancellationToken.None);
            return new(new(kind == "plan" ? selectedIds.Length : 0, jobIds.Length, paths.Length, pending));
        }
        finally { gate.Mutex.Release(); }
    }

    public async Task Cleanup(CancellationToken ct)
    {
        await gate.Mutex.WaitAsync(ct);
        try { await CleanupUnderGate(ct); }
        finally { gate.Mutex.Release(); }
    }
    private async Task CleanupUnderGate(CancellationToken ct)
    {
        foreach (var pending in await db.PendingImageDeletions.ToListAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var path = images.FromName(pending.FileName);
                if (await db.Tasks.AnyAsync(t => t.ImagePath == path, ct)) continue;
                File.Delete(path);
                db.PendingImageDeletions.Remove(pending);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Retain durable cleanup work. */ }
        }
        await db.SaveChangesAsync(ct);
    }
}
