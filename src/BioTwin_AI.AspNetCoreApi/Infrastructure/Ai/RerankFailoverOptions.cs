namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;

public sealed class RerankFailoverOptions
{
    public const string SectionName = "Rerank";

    public bool Enabled { get; set; } = true;
    public string PrimaryProvider { get; set; } = "CloudflareWorkersAI";
    public string FallbackProvider { get; set; } = "BgeRerankerOnnx";
    public bool FallbackEnabled { get; set; } = true;
    public int FallbackCooldownSeconds { get; set; } = 60;
    public double RequestTimeoutSeconds { get; set; } = 60;
    public int CandidateCount { get; set; } = 20;
}
