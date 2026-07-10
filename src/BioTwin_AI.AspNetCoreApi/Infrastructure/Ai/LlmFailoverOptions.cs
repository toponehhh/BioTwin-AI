namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;

public sealed class LlmFailoverOptions
{
    public const string SectionName = "LLM";

    public string PrimaryProvider { get; set; } = "CloudflareWorkersAI";
    public string FallbackProvider { get; set; } = "OpenRouter";
    public bool FallbackEnabled { get; set; } = true;
    public int FallbackCooldownSeconds { get; set; } = 60;
    public double RequestTimeoutSeconds { get; set; } = 180;
    public double ExtractionTimeoutSeconds { get; set; } = 600;
}
