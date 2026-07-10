namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeOperationConflictException(
    string code,
    string message,
    string recoveryHint,
    int statusCode = StatusCodes.Status409Conflict,
    bool canRetry = false) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    public string RecoveryHint { get; } = recoveryHint;

    public int StatusCode { get; } = statusCode;

    public bool CanRetry { get; } = canRetry;
}
