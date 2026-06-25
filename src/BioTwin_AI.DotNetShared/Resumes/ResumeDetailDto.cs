namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record ResumeDetailDto(
    int Id,
    string Title,
    string? SourceFileName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ResumeSectionDto> Sections);
