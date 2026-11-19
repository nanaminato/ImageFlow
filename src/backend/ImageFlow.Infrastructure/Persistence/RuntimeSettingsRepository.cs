using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public sealed class RuntimeSettingsRepository(QueueDb db) : IRuntimeSettingsRepository
{
    public Task<RuntimeSettings?> Get(CancellationToken ct) => db.RuntimeSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
    public void Add(RuntimeSettings settings) => db.RuntimeSettings.Add(settings);
    public async Task Save(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
