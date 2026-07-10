namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record ResumeStateDto(
    string Token,
    IReadOnlyList<ResumeSummaryDto> Resumes);
