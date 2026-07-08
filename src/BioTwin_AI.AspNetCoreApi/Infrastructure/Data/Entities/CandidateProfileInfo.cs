namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

public sealed class CandidateProfileInfo
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public UserAccount? User { get; set; }

    public string InfoType { get; set; } = string.Empty;

    public int Version { get; set; }

    public string JsonData { get; set; } = "{}";

    public string Source { get; set; } = "llm_generated";

    public int? SourceResumeVersion { get; set; }

    public int? BasedOnInfoId { get; set; }

    public CandidateProfileInfo? BasedOnInfo { get; set; }

    public string? ModelName { get; set; }

    public string? PromptVersion { get; set; }

    public bool IsCurrent { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int? CreatedByUserId { get; set; }
}
