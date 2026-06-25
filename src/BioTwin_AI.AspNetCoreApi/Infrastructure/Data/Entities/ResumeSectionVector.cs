namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

public sealed class ResumeSectionVector
{
    public int Id { get; set; }

    public int ResumeSectionId { get; set; }

    public ResumeSection? ResumeSection { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string ResumeTitle { get; set; } = string.Empty;

    public string SectionTitle { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string EmbeddingPayload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
