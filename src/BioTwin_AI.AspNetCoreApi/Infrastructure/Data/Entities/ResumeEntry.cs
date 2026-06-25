namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

public sealed class ResumeEntry
{
    public int Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? SourceFileName { get; set; }

    public byte[]? SourceFileContent { get; set; }

    public string? SourceContentType { get; set; }

    public long? SourceFileSize { get; set; }

    public string? SourceFileHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ResumeSection> Sections { get; set; } = [];
}
