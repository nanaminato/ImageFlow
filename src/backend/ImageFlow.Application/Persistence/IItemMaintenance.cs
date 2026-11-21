namespace ImageFlow;

public sealed record ItemDeletionResult(int Plans, int Jobs, int Images, int PendingImageFiles);
public interface IItemMaintenance
{
    Task<OperationResult<ItemDeletionResult>> Delete(string kind, Guid id, bool confirmed, CancellationToken ct);
    Task<OperationResult<ItemDeletionResult>> DeleteMany(string kind, IReadOnlyCollection<Guid>? ids, bool confirmed, CancellationToken ct);
    Task Cleanup(CancellationToken ct);
}
