namespace ImageFlow;

public sealed record SettingsUpdate(int MaxConcurrency, decimal EstimatedCostPerImageUsd);
public sealed record SettingsView(string Provider, string PromptModel, string ImageModel, int MaxConcurrency,
 decimal EstimatedCostPerImageUsd, int Revision, bool EstimateConfirmed, string ImageSize, string ImageQuality,
 string ImageComplexity, string OutputFormat, int MaxPrompts = 20, int MaxContinuousImages = 1000);
public sealed class RuntimeSettingsService(IRuntimeSettingsRepository repository, QueueGate gate, EngineOptions options)
{
    public async Task Initialize(CancellationToken ct)
    {
        await gate.Mutex.WaitAsync(ct);
        try
        {
            if (await repository.Get(ct) != null) return;
            repository.Add(new RuntimeSettings { MaxConcurrency = options.MaxConcurrency });
            await repository.Save(ct);
        }
        finally { gate.Mutex.Release(); }
    }
    public async Task<SettingsView> Get(CancellationToken ct) => View(await repository.Get(ct) ?? throw new InvalidOperationException("Settings not initialized."));
    public async Task<OperationResult<SettingsView>> Update(SettingsUpdate update, CancellationToken ct)
    {
        if (update.MaxConcurrency is < 1 or > 8 || update.EstimatedCostPerImageUsd < 0 || update.EstimatedCostPerImageUsd > 1000 || decimal.Round(update.EstimatedCostPerImageUsd, 6) != update.EstimatedCostPerImageUsd)
            return new(Failure: FailureKind.InvalidInput, Message: "Concurrency must be 1–8; estimated USD per image must be 0–1000 with at most six decimal places.");
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var settings = await repository.Get(ct) ?? throw new InvalidOperationException("Settings not initialized.");
            settings.MaxConcurrency = update.MaxConcurrency;
            settings.EstimatedCostPerImageUsd = update.EstimatedCostPerImageUsd;
            settings.EstimateConfiguration = options.EstimateConfiguration();
            settings.Revision++;
            await repository.Save(ct);
            return new(View(settings));
        }
        finally { gate.Mutex.Release(); }
    }
    private SettingsView View(RuntimeSettings settings)
    {
        var execution = options.CreateImageRequest("");
        return new(options.Provider, options.PromptModel, execution.Model, settings.MaxConcurrency,
            settings.EstimatedCostPerImageUsd, settings.Revision,
            settings.EstimatedCostPerImageUsd == 0 || settings.EstimateConfiguration == options.EstimateConfiguration(),
            execution.Size, execution.Quality, execution.Complexity, execution.OutputFormat);
    }
}
