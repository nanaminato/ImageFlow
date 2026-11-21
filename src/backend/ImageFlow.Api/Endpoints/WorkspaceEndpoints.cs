namespace ImageFlow;

public sealed record WorkspaceClearRequest(bool Confirmed);
public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this RouteGroupBuilder api) =>
        api.MapPost("/workspace/clear", async (WorkspaceClearRequest request, IWorkspaceMaintenance maintenance, CancellationToken ct) =>
            (await maintenance.Clear(request.Confirmed, ct)).ToHttp());
}
