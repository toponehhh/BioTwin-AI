namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;

public sealed class EmbeddingFailoverOptions
{
    public const string SectionName = "Embedding";

    public string PrimaryProvider { get; set; } = "CloudflareWorkersAI";
    public string FallbackProvider { get; set; } = "BgeM3Onnx";
    public bool FallbackEnabled { get; set; } = true;
    public int FallbackCooldownSeconds { get; set; } = 60;
    public double RequestTimeoutSeconds { get; set; } = 60;
    public int ExpectedDimensions { get; set; } = 1024;
}
