namespace BioTwin_AI.DotNetShared.Rag;

public sealed record RagCitationDto(
    int ResumeEntryId,
    int ResumeSectionId,
    string ResumeTitle,
    string SectionTitle,
    string ContentPreview,
    float Score);
