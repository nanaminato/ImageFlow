namespace ImageFlow;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", (RuntimeSettingsService service, CancellationToken ct) => service.Get(ct));
        api.MapPut("/settings", async (SettingsUpdate update, RuntimeSettingsService service, CancellationToken ct) => (await service.Update(update, ct)).ToHttp());
    }
}
