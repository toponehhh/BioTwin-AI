namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record MergeResumeMarkdownResponse(
    string Language,
    int CanonicalResumeId,
    string MergedTitle,
    string MergedMarkdown,
    IReadOnlyList<string> Warnings);
