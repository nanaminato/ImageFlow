namespace ImageFlow;

public interface IRuntimeSettingsRepository
{
    Task<RuntimeSettings?> Get(CancellationToken ct);
    void Add(RuntimeSettings settings);
    Task Save(CancellationToken ct);
}
