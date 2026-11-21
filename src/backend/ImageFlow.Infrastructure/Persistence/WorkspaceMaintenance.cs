using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public sealed class WorkspaceMaintenance(QueueDb db, QueueGate gate, EngineOptions options) : IWorkspaceMaintenance
{
    public async Task<OperationResult<WorkspaceClearResult>> Clear(bool confirmed, CancellationToken ct)
    {
        if (!confirmed) return new(Failure: FailureKind.InvalidInput, Message: "Explicit confirmation is required to permanently clear the workspace.");
        await gate.Mutex.WaitAsync(ct);
        try
        {
            if (gate.ActivePlanningRequests > 0 || await db.Tasks.AnyAsync(t => t.Status == "Running", ct) ||
                await db.Attempts.AnyAsync(a => a.Outcome == "Running", ct))
                return new(Failure: FailureKind.Conflict, Message: "Wait for planning and in-flight image requests to finish before clearing the workspace. Pause jobs to stop new requests.");

            // Never follow database ImagePath values or recurse through directories.
            // The engine owns only the immediate files in its configured images directory.
            var dataRoot = Path.GetFullPath(options.DataDirectory);
            var imagesRoot = Path.GetFullPath(Path.Combine(dataRoot, "images"));
            if (Path.GetDirectoryName(imagesRoot) != dataRoot.TrimEnd(Path.DirectorySeparatorChar))
                return new(Failure: FailureKind.Conflict, Message: "Image storage path is invalid.");
            string[] files;
            try
            {
                if (Directory.Exists(imagesRoot) &&
                    ((File.GetAttributes(imagesRoot) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateDirectories(imagesRoot).Any()))
                    return new(Failure: FailureKind.Conflict, Message: "Image storage contains a linked directory or subdirectories. Check local storage before clearing.");
                files = Directory.Exists(imagesRoot) ? Directory.GetFiles(imagesRoot) : [];
                if (files.Any(f => (File.GetAttributes(f) & FileAttributes.ReparsePoint) != 0))
                    return new(Failure: FailureKind.Conflict, Message: "Image storage contains linked files. Check local storage before clearing.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { return new(Failure: FailureKind.Conflict, Message: "Image storage cannot be accessed. Check local file permissions before clearing."); }

            var plans = await db.Plans.CountAsync(ct);
            var jobs = await db.Jobs.CountAsync(ct);
            var tasks = await db.Tasks.CountAsync(ct);
            var images = await db.Tasks.CountAsync(t => t.ImagePath != null, ct);
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                await db.TaskReviews.ExecuteDeleteAsync(ct);
                await db.Attempts.ExecuteDeleteAsync(ct);
                await db.Tasks.ExecuteDeleteAsync(ct);
                await db.Jobs.ExecuteDeleteAsync(ct);
                await db.Prompts.ExecuteDeleteAsync(ct);
                await db.Plans.ExecuteDeleteAsync(ct);
                await transaction.CommitAsync(ct);
            }
            // Filesystem deletion cannot join the SQLite transaction. Commit records first;
            // report locked files honestly and allow another confirmed clear to clean them up.
            // Finish cleanup even if the caller disconnects after the commit.
            var remaining = 0;
            foreach (var file in files)
            {
                try { File.Delete(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { remaining++; }
            }
            return new(new(plans, jobs, tasks, images, remaining));
        }
        finally { gate.Mutex.Release(); }
    }
}
