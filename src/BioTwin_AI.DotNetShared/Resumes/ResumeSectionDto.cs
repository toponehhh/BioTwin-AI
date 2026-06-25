namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record ResumeSectionDto(
    int Id,
    int ResumeEntryId,
    int? ParentSectionId,
    int HeadingLevel,
    string Title,
    string Content,
    int SortOrder,
    IReadOnlyList<ResumeSectionDto> Children);
