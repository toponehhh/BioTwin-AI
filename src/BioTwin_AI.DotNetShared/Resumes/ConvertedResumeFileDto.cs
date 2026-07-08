namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record ConvertedResumeFileDto(
    string Title,
    string SourceFileName,
    string Markdown,
    string DetectedLanguage,
    bool IsDuplicate,
    int? ExistingResumeEntryId,
    string? ExistingResumeTitle);
