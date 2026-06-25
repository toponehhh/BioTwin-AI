namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

public sealed class ResumeSection
{
    public int Id { get; set; }

    public int ResumeEntryId { get; set; }

    public ResumeEntry? ResumeEntry { get; set; }

    public int? ParentSectionId { get; set; }

    public ResumeSection? ParentSection { get; set; }

    public List<ResumeSection> ChildSections { get; set; } = [];

    public string TenantId { get; set; } = string.Empty;

    public int HeadingLevel { get; set; } = 2;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ResumeSectionVector? Vector { get; set; }
}
