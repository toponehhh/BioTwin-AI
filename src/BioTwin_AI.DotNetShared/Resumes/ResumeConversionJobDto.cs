namespace BioTwin_AI.DotNetShared.Resumes;

public static class ResumeConversionJobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record ResumeConversionJobDto(
    string JobId,
    string Status,
    int Progress,
    string Message,
    ConvertedResumeFileDto? Result,
    string? Error);
