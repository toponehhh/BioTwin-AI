namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record AcquireResumeOperationRequest(
    string OperationType,
    string ExpectedStateToken);

public sealed record ResumeOperationLeaseDto(
    string OperationId,
    string ResumeStateToken,
    DateTimeOffset ExpiresAt);

public sealed record ResumeApiErrorDto(
    string Code,
    string Message,
    string RecoveryHint,
    bool CanRetry);
