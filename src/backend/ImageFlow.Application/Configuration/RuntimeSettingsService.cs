namespace ImageFlow;

public sealed record SettingsUpdate(int MaxConcurrency, decimal EstimatedCostPerImageUsd, string? ImageModel = null, string? ImageSize = null,
 string? ImageQuality = null, string? ImageComplexity = null);
public sealed record SettingsView(string Provider, string PromptModel, string ImageModel, int MaxConcurrency,
 decimal EstimatedCostPerImageUsd, int Revision, bool EstimateConfirmed, string ImageSize, string ImageQuality,
 string ImageComplexity, string OutputFormat, int MaxPrompts = 20, int MaxContinuousImages = 1000, ImageCapability[]? ImageCapabilities = null);
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
            var current = Execution(settings);
            var execution = current with {
                Model = update.ImageModel ?? current.Model,
                Size = update.ImageSize ?? current.Size,
                Quality = update.ImageQuality ?? current.Quality,
                Complexity = update.ImageComplexity ?? current.Complexity
            };
            try { ModelCatalog.ValidateImage(options, execution); }
            catch (InvalidOperationException) { return new(Failure: FailureKind.InvalidInput, Message: "Selected model, size, quality or complexity is not supported by this provider profile."); }
            settings.ImageModel = execution.Model;
            settings.ImageSize = execution.Size;
            settings.ImageQuality = execution.Quality;
            settings.ImageComplexity = execution.Complexity;
            settings.MaxConcurrency = update.MaxConcurrency;
            settings.EstimatedCostPerImageUsd = update.EstimatedCostPerImageUsd;
            settings.EstimateConfiguration = Fingerprint(execution);
            settings.Revision++;
            await repository.Save(ct);
            return new(View(settings));
        }
        finally { gate.Mutex.Release(); }
    }
    private ImageGenerationRequest Execution(RuntimeSettings settings)
    {
        var defaults = options.CreateImageRequest("");
        return defaults with {
            Model = settings.ImageModel ?? defaults.Model,
            Size = settings.ImageSize ?? defaults.Size,
            Quality = settings.ImageQuality ?? defaults.Quality,
            Complexity = settings.ImageComplexity ?? defaults.Complexity
        };
    }
    public static ImageGenerationRequest Execution(SettingsView view, EngineOptions options) => options.CreateImageRequest("") with {
        Model = view.ImageModel, Size = view.ImageSize, Quality = view.ImageQuality, Complexity = view.ImageComplexity
    };
    private static string Fingerprint(ImageGenerationRequest request) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request)));
    private SettingsView View(RuntimeSettings settings)
    {
        var execution = Execution(settings);
        return new(options.Provider, options.PromptModel, execution.Model, settings.MaxConcurrency,
            settings.EstimatedCostPerImageUsd, settings.Revision,
            settings.EstimatedCostPerImageUsd == 0 || settings.EstimateConfiguration == Fingerprint(execution),
            execution.Size, execution.Quality, execution.Complexity, execution.OutputFormat, ImageCapabilities: ModelCatalog.AvailableImages(options));
    }
}
