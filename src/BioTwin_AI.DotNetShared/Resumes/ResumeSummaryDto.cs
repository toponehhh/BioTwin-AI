namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record ResumeSummaryDto(
    int Id,
    string Title,
    string Language,
    string? SourceFileName,
    DateTimeOffset CreatedAt,
    int SectionCount,
    bool HasOriginalFile);
