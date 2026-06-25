namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed record ResumeMarkdownSection(
    string Title,
    string Content,
    int HeadingLevel,
    int? ParentIndex);
