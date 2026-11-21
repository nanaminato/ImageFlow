namespace ImageFlow;
// Only whitelisted, non-secret runtime settings belong in SQLite.
public class RuntimeSettings
{
    public int Id { get; set; } = 1;
    public int MaxConcurrency { get; set; } = 2;
    public decimal EstimatedCostPerImageUsd { get; set; }
    public string EstimateConfiguration { get; set; } = "";
    public string? ImageModel { get; set; }
    public string? ImageSize { get; set; }
    public string? ImageQuality { get; set; }
    public string? ImageComplexity { get; set; }
    public int Revision { get; set; } = 1;
}
