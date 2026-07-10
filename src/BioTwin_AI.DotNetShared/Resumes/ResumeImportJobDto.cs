namespace BioTwin_AI.DotNetShared.Resumes;

public static class ResumeImportJobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}

public sealed record ResumeImportStageDto(
    string Key,
    string Label,
    string Status,
    int Progress);

public sealed record ResumeImportJobDto(
    string JobId,
    string Status,
    int Progress,
    string CurrentStage,
    string Message,
    IReadOnlyList<ResumeImportStageDto> Stages,
    bool ConversionSkipped,
    ResumeWizardDto? WizardResult,
    IReadOnlyList<ConvertedResumeFileDto>? WorkspaceResults,
    string? SourceMarkdown,
    string? ErrorCode,
    string? Error,
    string? RecoveryHint,
    bool CanRetry);
