namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record MergeResumeMarkdownRequest(
    string Language,
    string DraftTitle,
    string DraftMarkdown,
    string? SourceFileName);
