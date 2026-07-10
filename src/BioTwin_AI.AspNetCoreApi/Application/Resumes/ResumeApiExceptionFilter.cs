using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var mapping = context.Exception switch
        {
            ResumeOperationConflictException conflict => new ErrorMapping(
                conflict.StatusCode,
                conflict.Code,
                conflict.Message,
                conflict.RecoveryHint,
                conflict.CanRetry),
            ResumeImportException import => new ErrorMapping(
                StatusCodes.Status400BadRequest,
                import.Code,
                import.Message,
                import.RecoveryHint,
                import.CanRetry),
            _ => null
        };

        if (mapping is null)
        {
            return;
        }

        context.Result = new ObjectResult(new ResumeApiErrorDto(
            mapping.Code,
            mapping.Message,
            mapping.RecoveryHint,
            mapping.CanRetry))
        {
            StatusCode = mapping.StatusCode
        };
        context.ExceptionHandled = true;
    }

    private sealed record ErrorMapping(
        int StatusCode,
        string Code,
        string Message,
        string RecoveryHint,
        bool CanRetry);
}
