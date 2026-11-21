namespace ImageFlow;

public sealed record WorkspaceClearResult(int Plans, int Jobs, int Tasks, int Images, int RemainingImageFiles);
public interface IWorkspaceMaintenance
{
    Task<OperationResult<WorkspaceClearResult>> Clear(bool confirmed, CancellationToken ct);
}
