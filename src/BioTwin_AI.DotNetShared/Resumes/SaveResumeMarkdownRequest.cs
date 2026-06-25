namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record SaveResumeMarkdownRequest(
    string Title,
    string Markdown,
    string? SourceFileName,
    string? SourceContentType,
    long? SourceFileSize,
    string? SourceFileContentBase64 = null);
